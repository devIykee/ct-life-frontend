using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.WorldLife.Tests
{
    public class WorldLifeRouteTests
    {
        [Test]
        public void MissingEntityDoesNotDisablePollingForTheWholeSession()
        {
            Assert.IsFalse(WorldLifeApi.IsUnsupported(new CTLife.Net.ApiException(404, "NOT_FOUND", "Not found.",
                JObject.Parse("{\"resource\":\"creative\"}"))));
            Assert.IsTrue(WorldLifeApi.IsUnsupported(new CTLife.Net.ApiException(404, "NOT_FOUND", "No route for this path and method.",
                JObject.Parse("{\"path\":\"/v1/ads/live\",\"method\":\"GET\"}"))));
            Assert.IsFalse(WorldLifeApi.IsUnsupported(new CTLife.Net.ApiException(503, "SERVER_BUSY", "Busy")));
        }
    }

    public class TimelineFeedTests
    {
        [Test]
        public void FeedIsBoundedNewestFirstAndDeduped()
        {
            var f = new TimelineFeed();
            for (int i = 0; i < 40; i++) f.Add(new TimelineEntry { Key = "k" + i, Kind = "event", Text = "JUST IN: " + i, At = 1000 + i });
            Assert.AreEqual(TimelineFeed.Limit, f.Entries.Count);
            Assert.AreEqual(1039, f.Entries[0].At);
            int v = f.Version;
            Assert.IsFalse(f.Add(new TimelineEntry { Key = "k39", Text = "dup", At = 5000 }));
            Assert.IsFalse(f.Add(new TimelineEntry { Key = "old", Text = "too old", At = 1 }), "older than a full list");
            Assert.AreEqual(v, f.Version, "no repaint when nothing visible changed");
            Assert.IsFalse(f.Add(new TimelineEntry { Key = "empty", Text = "  \n ", At = 9999 }));
        }

        [Test]
        public void MergesContractAndBackendFeedShapes()
        {
            var f = new TimelineFeed();
            var contract = JArray.Parse("[{\"postId\":\"p1\",\"userId\":\"u\",\"alias\":\"anon\",\"text\":\"gm\",\"at\":\"2026-10-07T10:00:00Z\"}]");
            var backend = JObject.Parse("{\"posts\":[{\"postId\":\"p2\",\"alias\":\"degen<b>\",\"text\":\"wagmi\",\"at\":1801828800000},{\"postId\":\"p3\",\"text\":\"no time\"}]}");
            Assert.AreEqual(1, f.MergePosts(contract));
            Assert.AreEqual(1, f.MergePosts(backend), "rows without a time are skipped");
            Assert.AreEqual(0, f.MergePosts(backend), "deduped by postId");
            var lines = f.Lines(10, 200);
            CollectionAssert.AreEqual(new[] { "@degenb: wagmi", "@anon: gm" }, lines);
            Assert.AreEqual(0, f.MergePosts(JToken.Parse("{\"error\":1}")));
        }

        [Test]
        public void LinesAreTruncatedForTheScreen()
        {
            var f = new TimelineFeed();
            f.Add(new TimelineEntry { Key = "a", Kind = "npc", Author = "sleuth", Text = new string('x', 140), At = 1 });
            var line = f.Lines(6, 40)[0];
            Assert.AreEqual(40, line.Length);
            StringAssert.EndsWith("…", line);
        }

        [TestCase("2026-10-07T10:00:00Z", 1791367200000d)]
        [TestCase("1801828800000", 1801828800000d)]
        [TestCase("not a date", 0d)]
        public void ParsesTimes(string raw, double expected)
        {
            Assert.AreEqual(expected, TimelineFeed.ParseTime(new JValue(raw)));
        }
    }

    public class LiveAdsTests
    {
        [Test]
        public void ParsesTheContractShape()
        {
            var body = JObject.Parse("{\"billboard_01\":{\"slotId\":\"billboard_01\",\"imageUrl\":\"https://cdn.example/a.png\",\"link\":\"https://example.org\",\"sponsored\":false}}");
            var map = LiveAds.Parse(body, null, false, out var rejected);
            Assert.AreEqual(0, rejected);
            var c = map["billboard_01"].Single();
            Assert.AreEqual("https://cdn.example/a.png", c.ImageUrl);
            Assert.IsTrue(c.Sponsored, "every paid placement is labelled Sponsored regardless of the flag");
        }

        [Test]
        public void BackendShapeWithOnlyAnS3KeyIsRejectedUnlessAUrlCanBeBuilt()
        {
            var body = JObject.Parse("{\"ads\":[{\"slotId\":\"billboard_02\",\"bookingId\":\"b1\",\"link\":\"x\",\"imageKey\":\"ads/u.png\",\"mimeType\":\"image/png\"}]}");
            Assert.AreEqual(0, LiveAds.Parse(body, null, false, out var r1).Count);
            Assert.AreEqual(1, r1);
            var map = LiveAds.Parse(body, key => "https://ads.example/" + key, false, out var r2);
            Assert.AreEqual(0, r2);
            Assert.AreEqual("https://ads.example/ads/u.png", map["billboard_02"][0].ImageUrl);
            Assert.IsNull(map["billboard_02"][0].Link, "non-URL link dropped");
        }

        [TestCase("http://cdn.example/a.png", false, false)]
        [TestCase("http://localhost:9000/a.png", true, true)]
        [TestCase("javascript:alert(1)", true, false)]
        [TestCase("https://user:pw@cdn.example/a.png", false, false)]
        [TestCase("/relative.png", false, false)]
        [TestCase("https://cdn.example/a.png", false, true)]
        public void OnlySafeImageUrls(string url, bool allowHttp, bool ok)
        {
            Assert.AreEqual(ok, LiveAds.IsImageUrl(url, allowHttp));
        }

        [Test]
        public void UnsafeSlotIdsAreRejected()
        {
            var body = JArray.Parse("[{\"slotId\":\"../../etc\",\"imageUrl\":\"https://a.b/c.png\"},{\"slotId\":\"pixel_tile_001\",\"imageUrl\":\"https://a.b/c.png\"}]");
            var map = LiveAds.Parse(body, null, false, out var rejected);
            Assert.AreEqual(1, rejected);
            Assert.IsTrue(map.ContainsKey("pixel_tile_001"));
        }

        [Test]
        public void SlotsForAVenueComeFromTheContractRegistry()
        {
            var slots = TestData.Contract("ids/ad_slots.json");
            CollectionAssert.AreEqual(new[] { "billboard_01", "billboard_02" }, AdSlots.ForVenue(slots, "timeline_square", "billboard"));
            CollectionAssert.AreEqual(new[] { "pixel_tile_001", "pixel_tile_002" }, AdSlots.ForVenue(slots, "timeline_square", "pixel_tile"));
            CollectionAssert.AreEqual(new[] { "billboard_03" }, AdSlots.ForVenue(slots, "spaces_studio", "billboard"));
            Assert.IsEmpty(AdSlots.ForVenue(slots, "perp_pit", "billboard"));
        }

        [Test]
        public void RotationPrefersLiveAndIsTheSameOnEveryClient()
        {
            var a = AdRotation.Current(2, 13, 1_801_828_800_000, 12, 0);
            var b = AdRotation.Current(2, 13, 1_801_828_800_000, 12, 0);
            Assert.IsTrue(a.Live);
            Assert.AreEqual(a.Index, b.Index);
            var house = AdRotation.Current(0, 13, 1_801_828_812_000, 12, 1);
            Assert.IsFalse(house.Live);
            Assert.That(house.Index, Is.InRange(0, 12));
            Assert.AreNotEqual(AdRotation.Current(0, 13, 1_801_828_800_000, 12, 0).Index, AdRotation.Current(0, 13, 1_801_828_812_000, 12, 0).Index);
            Assert.AreEqual(0, AdRotation.Current(0, 0, 5, 12, 3).Index);
        }

        [Test]
        public void MagicBytes()
        {
            Assert.IsTrue(LiveAds.IsPngOrJpeg(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }));
            Assert.IsTrue(LiveAds.IsPngOrJpeg(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0 }));
            Assert.IsFalse(LiveAds.IsPngOrJpeg(new byte[] { (byte)'<', (byte)'h', (byte)'t', (byte)'m', (byte)'l', 0, 0, 0, 0 }));
            Assert.IsFalse(LiveAds.IsPngOrJpeg(null));
        }

        private static byte[] Png(uint w, uint h) => new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            (byte)(w >> 24), (byte)(w >> 16), (byte)(w >> 8), (byte)w, (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h,
            8, 6, 0, 0, 0,
        };

        // SOI, APP0 (JFIF, 16 bytes), optional fill byte, SOF2 (progressive) 8-bit 3 components.
        private static byte[] Jpeg(int w, int h, bool fill = false)
        {
            var b = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0 };
            if (fill) b.Add(0xFF);
            b.AddRange(new byte[] { 0xFF, 0xC2, 0, 17, 8, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1 });
            b.AddRange(new byte[] { 0xFF, 0xDA, 0, 8, 1, 1, 0, 0, 0x3F, 0 });
            return b.ToArray();
        }

        [Test]
        public void ReadsDeclaredSizeWithoutDecoding()
        {
            Assert.IsTrue(LiveAds.TryReadImageSize(Png(1200, 628), out int w, out int h));
            Assert.AreEqual((1200, 628), (w, h));
            Assert.IsTrue(LiveAds.TryReadImageSize(Jpeg(1080, 1920), out w, out h));
            Assert.AreEqual((1080, 1920), (w, h));
            Assert.IsTrue(LiveAds.TryReadImageSize(Jpeg(640, 480, fill: true), out w, out h));
            Assert.AreEqual((640, 480), (w, h));
        }

        [Test]
        public void RejectsMalformedHeaders()
        {
            Assert.IsFalse(LiveAds.TryReadImageSize(null, out _, out _));
            Assert.IsFalse(LiveAds.TryReadImageSize(Png(0x80000000, 10), out _, out _), "negative width");
            Assert.IsFalse(LiveAds.TryReadImageSize(Png(0, 10), out _, out _));
            var noIhdr = Png(10, 10); noIhdr[12] = (byte)'X';
            Assert.IsFalse(LiveAds.TryReadImageSize(noIhdr, out _, out _));
            Assert.IsFalse(LiveAds.TryReadImageSize(Jpeg(640, 0), out _, out _), "DNL-deferred height");
            var scanFirst = new List<byte> { 0xFF, 0xD8, 0xFF, 0xDA, 0, 8 }; scanFirst.AddRange(new byte[24]);
            Assert.IsFalse(LiveAds.TryReadImageSize(scanFirst.ToArray(), out _, out _));
            var truncated = Jpeg(640, 480); Array.Resize(ref truncated, 26);
            Assert.IsFalse(LiveAds.TryReadImageSize(truncated, out _, out _));
            var zeroLen = Jpeg(640, 480); zeroLen[5] = 0;
            Assert.IsFalse(LiveAds.TryReadImageSize(zeroLen, out _, out _));
        }

        [TestCase(1024, 1024, true)]
        [TestCase(4096, 2048, true)]
        [TestCase(4097, 16, false)]
        [TestCase(16, 4097, false)]
        [TestCase(4096, 4096, false)]
        [TestCase(30000, 30000, false)]
        [TestCase(0, 10, false)]
        public void DecodeSizeCap(int w, int h, bool ok) => Assert.AreEqual(ok, LiveAds.IsDecodeSizeOk(w, h));

        [TestCase(0, 60f)]
        [TestCase(1, 60f)]
        [TestCase(2, 120f)]
        [TestCase(4, 480f)]
        [TestCase(9, 600f)]
        public void PollingBacksOff(int failures, float seconds)
        {
            Assert.AreEqual(seconds, Poll.Backoff(failures, 60f, 600f));
        }
    }

    public class TextureBudgetTests
    {
        [Test]
        public void EvictsLeastRecentlyUsedToStayInBudget()
        {
            var b = new TextureBudget<string>(100);
            var evicted = new List<string>();
            Assert.IsTrue(b.Add("a", 40, "A", evicted));
            Assert.IsTrue(b.Add("b", 40, "B", evicted));
            Assert.IsTrue(b.TryGet("a", out _)); // a is now most recent
            Assert.IsTrue(b.Add("c", 40, "C", evicted));
            CollectionAssert.AreEqual(new[] { "B" }, evicted);
            Assert.AreEqual(80, b.UsedBytes);
            Assert.IsFalse(b.TryGet("b", out _));
        }

        [Test]
        public void PinnedItemsSurviveAndOversizeIsRefused()
        {
            var b = new TextureBudget<string>(100);
            var evicted = new List<string>();
            b.Add("shown", 60, "S", evicted);
            b.Add("x", 30, "X", evicted);
            b.Add("y", 30, "Y", evicted, new HashSet<string> { "shown" });
            CollectionAssert.AreEqual(new[] { "X" }, evicted);
            Assert.IsTrue(b.TryGet("shown", out _));
            Assert.IsFalse(b.Add("huge", 101, "H", evicted));
            evicted.Clear();
            b.Add("y", 10, "Y2", evicted);
            CollectionAssert.AreEqual(new[] { "Y" }, evicted, "replacing a key frees the old texture");
            b.Clear(evicted);
            Assert.AreEqual(0, b.UsedBytes);
            Assert.AreEqual(0, b.Count);
        }

        [Test]
        public void ByteEstimateCountsMips()
        {
            Assert.AreEqual(1024L * 1024 * 4, TextureBudget<string>.EstimateBytes(1024, 1024, false));
            Assert.AreEqual(1024L * 1024 * 4 * 4 / 3, TextureBudget<string>.EstimateBytes(1024, 1024, true));
        }
    }

    public class CoverageTests
    {
        private static readonly HashSet<string> ProposedAnims = new HashSet<string> { "act_sybil_split", "emote_sybil_split", "act_ban_hammer", "emote_ban" };
        private static readonly HashSet<string> ProposedSfx = new HashSet<string> { "sfx_liquidation_siren", "sfx_sniper_swarm", "sfx_exploit_alarm" };

        [Test]
        public void EverySpecArchetypeHasALookPropsAndSignature()
        {
            var book = ArchetypeBook.Build(TestData.Looks, TestData.Presentation);
            Assert.AreEqual(29, book.SpecArchetypes.Count, "28 spec rows, Farmer + Sybil Farmer split as in the contract");
            CollectionAssert.IsEmpty(book.MissingCoverage());
        }

        [Test]
        public void EveryContractArchetypeHasALookAndLines()
        {
            var book = ArchetypeBook.Build(TestData.Looks, TestData.Presentation);
            var lines = TestData.Book();
            foreach (var id in TestData.Contract("ids/archetypes.json")["archetypes"].Select(a => (string)a["id"]).Distinct())
            {
                Assert.IsNotNull(book.Get(id), id);
                Assert.IsTrue(book.Get(id).InContract, id + " is in contract/ids/archetypes.json");
                Assert.Greater(lines.Pool(id).Count, 0, id + " has no line set (backend or FE proposal)");
            }
            Assert.IsNotNull(book.Get("vc_npc"), "backend brain id resolves through archetypeAliases");
            Assert.IsNotNull(book.Get("builder_npc"));
            Assert.IsFalse(book.Get("laser_eyes_maxi").InContract);
            Assert.IsFalse(book.Get("chain_tribalist").InContract);
        }

        [Test]
        public void AnimationIdsExistInTheContract()
        {
            var anims = new HashSet<string>(TestData.Contract("ids/anims.json")["anims"].Values<string>());
            var used = new List<string>();
            foreach (var a in ((JObject)TestData.Looks["archetypes"]).Properties())
            {
                used.Add((string)a.Value["talkGesture"]);
                used.Add((string)a.Value["signatureEmote"]);
                used.AddRange(WireText.Strs(a.Value["idleEmotes"]));
            }
            foreach (var m in ((JObject)TestData.Presentation["moments"]).Properties())
                foreach (var key in new[] { "crowd", "crowdUp", "crowdDown" }) used.AddRange(WireText.Strs(m.Value[key]));
            foreach (var p in ((JObject)TestData.Presentation["emoteAliases"]).Properties()) used.Add((string)p.Value);
            foreach (var b in ((JObject)TestData.Presentation["behaviours"]).Properties())
                foreach (var key in new[] { "anims", "emotes" }) used.AddRange(WireText.Strs(b.Value[key]));
            foreach (var id in used.Where(u => !string.IsNullOrEmpty(u)).Distinct())
                Assert.IsTrue(anims.Contains(id) || ProposedAnims.Contains(id), id + " is not in contract/ids/anims.json");
        }

        [Test]
        public void SoundIdsExistInTheContractOrAreProposed()
        {
            var sfx = new HashSet<string>(TestData.Contract("ids/sfx.json")["sfx"].Values<string>());
            foreach (var m in ((JObject)TestData.Presentation["moments"]).Properties())
            {
                var stinger = (string)m.Value["stinger"];
                if (stinger != null) Assert.IsTrue(sfx.Contains(stinger), stinger);
                if (m.Value["venueSfx"] is JObject vs)
                    foreach (var p in vs.Properties())
                    {
                        var ids = WireText.Strs(p.Value);
                        Assert.IsTrue(sfx.Contains(ids.Last()), m.Name + ": the last candidate must be a contract sfx id");
                        foreach (var id in ids) Assert.IsTrue(sfx.Contains(id) || ProposedSfx.Contains(id), id);
                    }
            }
        }

        [Test]
        public void HouseAdsNamePlacesFromTheTableAndAreComplete()
        {
            var ads = TestData.HouseAds;
            Assert.GreaterOrEqual(ads["billboard"].Count(), 8);
            Assert.GreaterOrEqual(ads["pixelTiles"].Count(), 12);
            Assert.IsTrue(ads["billboard"].Any(a => ((string)a["headline"] ?? "").Contains("ADVERTISE")), "an advertise-here CTA is in the rotation");
            foreach (var ad in ads["billboard"].Append(ads["timeline"]))
            {
                var id = (string)ad["nameId"];
                if (id == null) continue;
                Assert.IsNotNull(CTLife.Core.NameTable.Get(id), id + " must be in contract/ids/names.json");
                Assert.AreEqual(CTLife.Core.NameTable.Get(id).ToUpperInvariant(), LiveSurfaces.Headline(ad), id);
            }
            foreach (var rule in ads["surfaces"]["rules"]) StringAssert.StartsWith("Screen_", (string)rule["prefix"]);
        }
    }
}
