using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.WorldLife.Tests
{
    public class LineFormattingTests
    {
        private static Dictionary<string, string> P(params string[] kv)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }

        [Test]
        public void SlotsAreExtractedOnce()
        {
            CollectionAssert.AreEqual(new[] { "coin", "x", "minutes" }, LineTemplates.Slots("JUST IN: {coin} did {x}x in {minutes} minutes. {coin}!"));
            Assert.IsEmpty(LineTemplates.Slots("no slots { here } or {}"));
        }

        [Test]
        public void FillsSlotsFromLineParams()
        {
            var text = LineTemplates.Fill("JUST IN: {coin} did {x}x in {minutes} minutes.", P("coin", "FROGE", "x", "3.2", "minutes", "12"));
            Assert.AreEqual("JUST IN: $FROGE did 3.2x in 12 minutes.", text);
        }

        [Test]
        public void MissingSlotNeverShowsRawBraces()
        {
            Assert.IsNull(LineTemplates.Fill("JUST IN: {coin} rugged.", P("x", "3")));
            Assert.IsNull(LineTemplates.Fill("JUST IN: {coin} rugged.", null));
            Assert.IsFalse(LineTemplates.CanFill("{alias} got REKT", P("alias", "   ")));
        }

        [TestCase("<size=999>BIG</size>", "size=999BIG/size")]
        [TestCase("line\nbreak\u0000x", "line breakx")]
        [TestCase("{coin}", "coin")]
        [TestCase("‮evil", "evil")]
        public void UntrustedSlotValuesAreCleaned(string raw, string expected)
        {
            Assert.AreEqual(expected, WireText.FormatSlot("alias", raw));
        }

        [Test]
        public void TruncationNeverSplitsAnEmoji()
        {
            var v = WireText.Clean(new string('a', 23) + "\U0001F525", 24);
            Assert.AreEqual(23, v.Length);
        }

        [Test]
        public void SlotValuesAreCapped()
        {
            Assert.AreEqual(WireText.SlotMax, WireText.FormatSlot("alias", new string('a', 300)).Length);
        }

        [TestCase("cashCents", "4400", "$44.00")]
        [TestCase("lossCents", "50920000", "$509.2k")]
        [TestCase("lossCents", "12345", "$123")]
        [TestCase("peakMcapCents", "420000000", "$4.2M")]
        [TestCase("lossCents", "-500", "-$5.00")]
        [TestCase("lossCents", "abc", null)]
        [TestCase("moodBps", "-300", "-3%")]
        [TestCase("moodBps", "125", "+1.3%")]
        [TestCase("coin", "froge", "$FROGE")]
        [TestCase("coin", "$FROGE", "$FROGE")]
        [TestCase("coin", "Not A Ticker", "Not A Ticker")]
        public void SlotFormatting(string key, string raw, string expected)
        {
            Assert.AreEqual(expected, WireText.FormatSlot(key, raw));
        }

        [Test]
        public void TemplateDollarBeforeCoinSlotIsNotDoubled()
        {
            Assert.AreEqual("buy $FROGE", LineTemplates.Fill("buy ${coin}", P("coin", "FROGE")));
        }

        [Test]
        public void HashAndPickAreStable()
        {
            Assert.AreEqual(WireText.Hash("npc-0-town_crier|crier_rug"), WireText.Hash("npc-0-town_crier|crier_rug"));
            Assert.AreNotEqual(WireText.Hash("a"), WireText.Hash("b"));
            Assert.AreEqual(-1, WireText.Pick("x", 0));
            for (int i = 0; i < 50; i++) Assert.That(WireText.Pick("seed" + i, 7), Is.InRange(0, 6));
        }
    }

    public class NpcLineBookTests
    {
        // Every line id backend main @ 9f3e5ef can send (packages/npc-brain/src/index.ts).
        public static readonly string[][] BrainLines =
        {
            new[] { "town_crier", "crier_long_liquidation" }, new[] { "town_crier", "crier_coin_moon" }, new[] { "town_crier", "crier_rug" },
            new[] { "town_crier", "crier_motherlode" }, new[] { "town_crier", "crier_exploit" }, new[] { "town_crier", "crier_new_launch" },
            new[] { "town_crier", "crier_tribe_control" }, new[] { "town_crier", "crier_turnaround" }, new[] { "town_crier", "crier_rich_list" },
            new[] { "town_crier", "crier_peak_normies" }, new[] { "town_crier", "crier_mood_down" }, new[] { "town_crier", "crier_smart_close" },
            new[] { "town_crier", "crier_rekt" }, new[] { "town_crier", "crier_graduation" }, new[] { "town_crier", "crier_air_thin" },
            new[] { "town_crier", "crier_sleuth_thread" }, new[] { "giveaway_guy", "giveaway_pitch" }, new[] { "reply_guy", "reply_gm" },
            new[] { "perp_degen_npc", "perp_max_long" }, new[] { "perp_degen_npc", "perp_its_so_over" }, new[] { "perp_degen_npc", "perp_to_the_moon" },
            new[] { "rekt_guy", "rekt_stimmy" }, new[] { "wallet_tracker", "wallet_turnaround" }, new[] { "doomposter", "doom_bear" },
            new[] { "spaces_host", "spaces_gm" }, new[] { "ore_miner", "ore_board" }, new[] { "ore_miner", "ore_motherlode" },
            new[] { "vc_npc", "vc_dibs" }, new[] { "vc_npc", "vc_pitch" }, new[] { "vc_npc", "vc_idle" },
            new[] { "builder_npc", "builder_hiring" }, new[] { "builder_npc", "builder_ship" },
        };

        [Test]
        public void EveryBrainLineIdResolvesWithoutParams()
        {
            var book = TestData.Book();
            foreach (var pair in BrainLines)
            {
                var r = book.Resolve(pair[0], pair[1], new Dictionary<string, string>(), "npc-1-" + pair[0] + "|1000");
                Assert.IsNotNull(r, pair[1]);
                Assert.IsFalse(r.UnknownLineId, pair[1] + " should be bound by lineAliases");
                StringAssert.DoesNotContain("{", r.Text, pair[1]);
                Assert.IsNotEmpty(r.Text.Trim(), pair[1]);
            }
        }

        [Test]
        public void AliasMatchesTheRightBackendLine()
        {
            var book = TestData.Book();
            StringAssert.Contains("rugged", book.Resolve("town_crier", "crier_rug", new Dictionary<string, string> { ["coin"] = "FROGE" }, "s").Text);
            StringAssert.Contains("exploit on Audit Row", book.Resolve("town_crier", "crier_exploit", null, "s").Text);
            for (int i = 0; i < 20; i++)
            {
                var stimmy = book.Resolve("rekt_guy", "rekt_stimmy", null, "s" + i).Text.ToLowerInvariant();
                Assert.IsTrue(stimmy.Contains("stimmy") || stimmy.Contains("small one"), stimmy);
            }
            StringAssert.Contains("liquidated", book.Resolve("town_crier", "crier_long_liquidation", null, "s").Text);
        }

        [Test]
        public void SlotLinesArePreferredWhenParamsArePresent()
        {
            var book = TestData.Book();
            var p = new Dictionary<string, string> { ["coin"] = "FROGE", ["x"] = "3.2", ["minutes"] = "12" };
            Assert.AreEqual("JUST IN: $FROGE did 3.2x in 12 minutes.", book.Resolve("town_crier", "crier_coin_moon", p, "npc-0|1").Text);
        }

        [Test]
        public void ThePickIsDeterministicPerSeedAndVariesAcrossSeeds()
        {
            var book = TestData.Book();
            var a = book.Resolve("reply_guy", "reply_gm", null, "npc-2-reply_guy|1700000000000").Text;
            Assert.AreEqual(a, book.Resolve("reply_guy", "reply_gm", null, "npc-2-reply_guy|1700000000000").Text);
            var seen = new HashSet<string>();
            for (int i = 0; i < 40; i++) seen.Add(book.Resolve("reply_guy", "reply_gm", null, "npc-2|" + i).Text);
            Assert.Greater(seen.Count, 5, "reply guy should not repeat one line forever");
        }

        [Test]
        public void UnknownLineIdFallsBackToThePoolAndIsFlagged()
        {
            var r = TestData.Book().Resolve("doomposter", "doom_brand_new_id", null, "s");
            Assert.IsNotNull(r);
            Assert.IsTrue(r.UnknownLineId);
            Assert.AreEqual("be", r.Source);
        }

        [Test]
        public void ArchetypeWithoutAnyLinesUsesItsSignatureLine()
        {
            var looks = JObject.Parse("{\"archetypes\":{\"mystery\":{\"signatureLine\":\"hello ser\"}}}");
            var book = NpcLineBook.Build(new JObject(), new JObject(), looks);
            var r = book.Resolve("mystery", "anything", null, "s");
            Assert.AreEqual("hello ser", r.Text);
            Assert.AreEqual("signature", r.Source);
            Assert.IsNull(book.Resolve("nobody", "anything", null, "s"));
            Assert.IsNull(book.Resolve("mystery", "", null, "s"));
        }

        [Test]
        public void BackendSetReplacesTheFrontendProposalAutomatically()
        {
            var be = JObject.Parse("{\"version\":\"x\",\"vc\":[\"BE line only\"]}");
            var book = NpcLineBook.Build(be, TestData.Presentation, TestData.Looks);
            var r = book.Resolve("vc_npc", "vc_idle", null, "s");
            Assert.AreEqual("BE line only", r.Text);
            Assert.AreEqual("be", r.Source);
            Assert.IsTrue(book.HasBackendSet("vc"));
        }

        [Test]
        public void ExactAndIndexedLineIdsResolve()
        {
            var be = JObject.Parse("{\"archetypes\":{\"town_crier\":[{\"id\":\"crier_named\",\"text\":\"JUST IN: named {coin}\"}]},\"rekt_guy\":[\"a\",\"b\"]}");
            var book = NpcLineBook.Build(be, null, null);
            Assert.AreEqual("JUST IN: named $FROGE", book.Resolve("town_crier", "crier_named", new Dictionary<string, string> { ["coin"] = "FROGE" }, "s").Text);
            Assert.AreEqual("JUST IN: named $FROGE", book.Resolve("town_crier", "town_crier.0", new Dictionary<string, string> { ["coin"] = "FROGE" }, "s").Text);
        }

        [Test]
        public void EventShoutsFillLiveNumbersOrFallBackToPlainLines()
        {
            var book = TestData.Book();
            var full = book.ResolveEvent("evt_liquidation",
                new Dictionary<string, string> { ["alias"] = "degen42", ["ticker"] = "FROGE", ["lossCents"] = "123400", ["leverage"] = "50" }, "k");
            StringAssert.StartsWith("JUST IN:", full.Text);
            StringAssert.DoesNotContain("{", full.Text);
            Assert.IsTrue(full.Text.Contains("degen42") || full.Text.Contains("$FROGE"), full.Text);
            var bare = book.ResolveEvent("evt_rug", null, "k");
            StringAssert.StartsWith("JUST IN:", bare.Text);
            StringAssert.DoesNotContain("{", bare.Text);
            Assert.IsNull(book.ResolveEvent("evt_unknown", null, "k"));
        }
    }

    public class LineContentTests
    {
        [Test]
        public void EveryProposalSetHasThirtyCleanLines()
        {
            var fe = (JObject)TestData.Presentation["feLines"];
            Assert.GreaterOrEqual(fe.Count, 21);
            foreach (var p in fe.Properties())
            {
                var lines = p.Value.Values<string>().ToList();
                Assert.GreaterOrEqual(lines.Count, 30, p.Name + " needs 30–60 lines (spec)");
                Assert.LessOrEqual(lines.Count, 60, p.Name);
                Assert.AreEqual(lines.Count, lines.Distinct().Count(), p.Name + " has duplicate lines");
                foreach (var l in lines)
                {
                    StringAssert.DoesNotContain("jeet", l.ToLowerInvariant(), p.Name + ": spec bans 'jeet'");
                    Assert.AreEqual(l.Count(c => c == '{'), l.Count(c => c == '}'), p.Name + ": " + l);
                    Assert.LessOrEqual(l.Length, 120, p.Name + ": bubble-length line: " + l);
                }
                Assert.Greater(lines.Count(l => LineTemplates.Slots(l).Count == 0), 20, p.Name + " needs mostly slot-free lines (backend sends no lineParams yet)");
            }
        }

        [Test]
        public void EveryArchetypeHasSomethingToSay()
        {
            var book = TestData.Book();
            foreach (var id in TestData.Looks["specArchetypes"].Values<string>())
                Assert.IsTrue(book.Pool(id).Count >= 9 || book.Signature(id) != null, id);
        }

        [Test]
        public void EventLinesAreShoutsWithBalancedSlots()
        {
            foreach (var p in ((JObject)TestData.Presentation["eventLines"]).Properties())
                foreach (var l in p.Value.Values<string>())
                {
                    StringAssert.StartsWith("JUST IN:", l, p.Name);
                    Assert.AreEqual(l.Count(c => c == '{'), l.Count(c => c == '}'), l);
                }
        }
    }
}
