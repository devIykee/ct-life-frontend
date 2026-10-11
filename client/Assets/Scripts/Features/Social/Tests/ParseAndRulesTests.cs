using System;
using System.Linq;
using System.Numerics;
using System.Text;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.SocialFeatures.Tests
{
    /// <summary>Contract shapes (openapi.yaml rc1 examples) and the backend checkout's variants (ct-life-backend 9f3e5ef).</summary>
    public class ParseTests
    {
        private const string Host = "0192a1c2-0000-7000-8000-0000000000aa";
        private const string Sp = "0192a1c2-0000-7000-8000-0000000000bb";
        private const string Hand = "0192a1c2-0000-7000-8000-0000000000cc";

        [Test]
        public void ContractSpace()
        {
            var s = SocialParse.Space(JToken.Parse($"{{\"spaceId\":\"0192a1c2-0000-7000-8000-000000000001\",\"title\":\"gm\",\"hostId\":\"{Host}\",\"speakerIds\":[\"{Sp}\"],\"handIds\":[\"{Hand}\"],\"listenerCount\":7}}"));
            Assert.AreEqual("0192a1c2-0000-7000-8000-000000000001", s.Id);
            Assert.AreEqual(Host, s.HostId);
            CollectionAssert.AreEqual(new[] { Sp }, s.SpeakerIds);
            CollectionAssert.AreEqual(new[] { Hand }, s.HandIds);
            Assert.AreEqual(7, s.ListenerCount);
            Assert.IsFalse(s.Ended);
        }

        [Test]
        public void BackendSpaceWithStringIdHostInSpeakersAndStatus()
        {
            // server/application/spaces.ts SpaceRow: `space:{uuidv7}`, hostUserId, speakerUserIds includes the host, handUserIds.
            var json = $"{{\"spaceId\":\"space:0192a1c2-0000-7000-8000-000000000001\",\"hostUserId\":\"{Host}\",\"title\":\"oil shock\",\"status\":\"live\"," +
                       $"\"startedAt\":\"2026-10-07T10:00:00.000Z\",\"endedAt\":null,\"maxSpeakers\":8,\"speakerUserIds\":[\"{Host}\",\"{Sp}\"],\"handUserIds\":[\"{Sp}\",\"{Hand}\"],\"listenerCount\":0}}";
            var s = SocialParse.Space(JToken.Parse(json));
            Assert.AreEqual("space:0192a1c2-0000-7000-8000-000000000001", s.Id);
            Assert.AreEqual(Host, s.HostId);
            CollectionAssert.AreEqual(new[] { Sp }, s.SpeakerIds, "the host is not listed as a speaker");
            CollectionAssert.AreEqual(new[] { Hand }, s.HandIds, "a speaker's stale hand is not a request");
            var ended = SocialParse.Space(JToken.Parse(json.Replace("\"status\":\"live\"", "\"status\":\"ended\"")));
            Assert.IsTrue(ended.Ended);
        }

        [Test]
        public void SpaceListBareOrWrappedSkipsBadRowsAndEnded()
        {
            var row = $"{{\"spaceId\":\"s1\",\"title\":\"a\",\"hostId\":\"{Host}\",\"speakerIds\":[],\"handIds\":[],\"listenerCount\":1}}";
            var bad = "{\"title\":\"no id\"}";
            var ended = $"{{\"spaceId\":\"s2\",\"title\":\"b\",\"hostId\":\"{Host}\",\"status\":\"ended\"}}";
            Assert.AreEqual(1, SocialParse.Spaces(JToken.Parse($"[{row},{bad},{row}]"), out var skipped).Count, "duplicates collapse");
            Assert.AreEqual(1, skipped);
            Assert.AreEqual(1, SocialParse.Spaces(JToken.Parse($"{{\"spaces\":[{row},{ended}]}}"), out _).Count);
            Assert.Throws<FormatException>(() => SocialParse.Spaces(JToken.Parse("{\"nope\":1}"), out _));
        }

        [Test]
        public void RtcGrantAcceptsIsoAndEpochSeconds()
        {
            var iso = SocialParse.Rtc(JToken.Parse("{\"url\":\"wss://lk\",\"token\":\"t\",\"expiresAt\":\"2026-10-07T12:00:00Z\",\"canPublish\":true}"));
            Assert.AreEqual(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), iso.ExpiresAtMs);
            Assert.IsTrue(iso.CanPublish);
            var secs = SocialParse.Rtc(JToken.Parse("{\"url\":\"ws://x\",\"token\":\"t\",\"expiresAt\":1791000000}"));
            Assert.AreEqual(1_791_000_000_000L, secs.ExpiresAtMs, "backend issuer sends epoch seconds");
            Assert.IsFalse(secs.CanPublish);
            Assert.IsTrue(secs.Usable(1_790_999_000_000));
            Assert.IsFalse(secs.Usable(1_790_999_996_000), "within the 5 s margin");
            Assert.Throws<FormatException>(() => SocialParse.Rtc(JToken.Parse("{\"url\":\"ws://x\"}")));
            var noExpiry = SocialParse.Rtc(JToken.Parse("{\"url\":\"ws://x\",\"token\":\"t\",\"roomName\":\"paid_call:c\"}"));
            Assert.AreEqual(0, noExpiry.ExpiresAtMs);
            Assert.IsTrue(noExpiry.Usable(1_790_999_000_000), "no expiry sent: LiveKit decides at connect");
        }

        [Test]
        public void FeedContractAndBackendShapes()
        {
            var contract = SocialParse.Feed(JToken.Parse("[{\"postId\":\"p1\",\"userId\":\"u1\",\"alias\":\"Froggy\",\"text\":\"gm\",\"at\":\"2026-10-07T10:00:00Z\"}]"), out _);
            Assert.AreEqual("Froggy", contract[0].Alias);
            Assert.Greater(contract[0].AtMs, 0);
            var backend = SocialParse.Feed(JToken.Parse("{\"posts\":[{\"postId\":\"p2\",\"userId\":\"u2\",\"alias\":\"\",\"text\":\"wen\",\"createdAt\":\"2026-10-07T10:00:01.000Z\"},{\"bad\":1}]}"), out var skipped);
            Assert.AreEqual(1, backend.Count);
            Assert.AreEqual(1, skipped);
            Assert.AreEqual(contract[0].AtMs + 1000, backend[0].AtMs);
        }

        [Test]
        public void DmShapes()
        {
            var contract = SocialParse.Dm(JToken.Parse("{\"messageId\":\"m1\",\"fromUserId\":\"a\",\"toUserId\":\"b\",\"text\":\"gm\",\"at\":\"2026-10-07T10:00:00Z\"}"));
            Assert.AreEqual("b", contract.ToUserId);
            var backend = SocialParse.Dms(JToken.Parse("{\"messages\":[{\"messageId\":\"m2\",\"fromUserId\":\"a\",\"toUserId\":\"b\",\"text\":\"x\",\"createdAt\":\"2026-10-07T10:00:00Z\"}]}"), out _);
            Assert.AreEqual(contract.AtMs, backend[0].AtMs);
            var threads = SocialParse.Threads(JToken.Parse("[{\"userId\":\"a\",\"alias\":\"Froggy\",\"lastMessage\":\"gm\",\"lastAt\":\"2026-10-07T10:00:00Z\"}]"), out _);
            Assert.AreEqual("Froggy", threads[0].Alias);
        }

        [Test]
        public void ControlCharactersAreStripped()
        {
            var p = SocialParse.Post(JToken.Parse("{\"postId\":\"p\",\"userId\":\"u\",\"text\":\"a\\u0007b\\nc\",\"at\":1}"));
            Assert.AreEqual("ab\nc", p.Text);
        }

        [TestCase("awaiting_payment", PaidCallStatus.AwaitingPayment)]
        [TestCase("ringing", PaidCallStatus.Ringing)]
        [TestCase("live", PaidCallStatus.Live)]
        [TestCase("settled", PaidCallStatus.Settled)]
        [TestCase("refunded", PaidCallStatus.Refunded)]
        [TestCase("pending", PaidCallStatus.AwaitingPayment)]
        [TestCase("active", PaidCallStatus.Live)]
        [TestCase("pending_onchain", PaidCallStatus.Unknown)]
        public void PaidStatuses(string raw, PaidCallStatus status) => Assert.AreEqual(status, SocialParse.Status(raw));

        [Test]
        public void PaidCallContractExample()
        {
            var row = SocialParse.PaidCall(JToken.Parse("{\"callId\":\"0192a1c2-0000-7000-8000-000000000009\",\"status\":\"live\",\"minutesUsed\":5,\"creatorAmount\":\"10000000\",\"refundAmount\":\"0\"}"));
            Assert.AreEqual(PaidCallStatus.Live, row.Status);
            Assert.AreEqual(5, row.MinutesUsed);
            Assert.AreEqual("10000000", row.CreatorAmount);
            Assert.AreEqual("0", row.RefundAmount);
            Assert.Throws<FormatException>(() => SocialParse.PaidCall(JToken.Parse("{\"callId\":\"x\",\"status\":\"weird\"}")));
        }

        [Test]
        public void PaidStartContractAndBackend()
        {
            var c = SocialParse.PaidStart(JToken.Parse("{\"callId\":\"c1\",\"sessionPda\":\"Pda\",\"tx\":\"AQID\"}"));
            Assert.AreEqual("AQID", c.Tx);
            Assert.IsNull(c.EmbeddedGrant);
            // server/application/paid-calls.ts startPaidCall: {call: {...}, livekit: {token, url, roomName}} and no tx.
            var b = SocialParse.PaidStart(JToken.Parse("{\"call\":{\"callId\":\"c2\",\"status\":\"awaiting_payment\"},\"livekit\":{\"token\":\"t\",\"url\":\"ws://lk\",\"roomName\":\"paid_call:c2\"}}"));
            Assert.AreEqual("c2", b.CallId);
            Assert.IsNull(b.Tx, "no transaction to sign: the flow fails with paid.noTx");
            Assert.IsNotNull(b.EmbeddedGrant);
        }

        [Test]
        public void VideoStartPicksOnlyMyToken()
        {
            const string me = "0192a1c2-0000-7000-8000-000000000001";
            var json = "{\"call\":{\"callId\":\"v1\"},\"tokens\":[{\"userId\":\"other\",\"token\":\"theirs\",\"url\":\"ws://lk\",\"expiresAt\":1791000000}," +
                       "{\"userId\":\"" + me + "\",\"token\":\"mine\",\"url\":\"ws://lk\",\"expiresAt\":1791000000}]}";
            var v = SocialParse.VideoStart(JToken.Parse(json), me);
            Assert.AreEqual("v1", v.CallId);
            Assert.AreEqual("mine", v.EmbeddedGrant.Token);
            Assert.IsNull(SocialParse.VideoStart(JToken.Parse("{\"callId\":\"v2\"}"), me).EmbeddedGrant);
        }

        [Test]
        public void CreatorTipAndUser()
        {
            var c = SocialParse.Creator(JToken.Parse("{\"enabled\":true,\"ratePerMinUsdc\":\"2000000\",\"maxMinutes\":10}"));
            Assert.AreEqual("2000000", c.RatePerMinUsdc);
            Assert.Throws<FormatException>(() => SocialParse.Creator(JToken.Parse("{\"enabled\":true,\"ratePerMinUsdc\":\"2.5\"}")));
            Assert.AreEqual(12345, SocialParse.TipBalance(JToken.Parse("{\"cashCents\":12345}")));
            Assert.IsNull(SocialParse.TipBalance(JToken.Parse("{\"tipId\":\"t\",\"amountCents\":100}")), "backend TipRow carries no balance");
            var u = SocialParse.User(JToken.Parse("{\"id\":\"u\",\"alias\":\"Froggy\",\"isSeeker\":true}"));
            Assert.AreEqual("Froggy", u.Alias);
            Assert.IsTrue(u.IsSeeker);
        }
    }

    public class RulesTests
    {
        [TestCase("2500000", "2.50 USDC")]
        [TestCase("250000", "0.25 USDC")]
        [TestCase("50000000", "50.00 USDC")]
        [TestCase("1234567", "1.234567 USDC")]
        [TestCase("1000000000000", "1,000,000.00 USDC")]
        [TestCase("0", "0.00 USDC")]
        [TestCase("-5", "-")]
        [TestCase(null, "-")]
        public void UsdcFormat(string units, string text) => Assert.AreEqual(text, UsdcUnits.Format(units));

        [TestCase("2.5", "2500000")]
        [TestCase(".25", "250000")]
        [TestCase("50", "50000000")]
        [TestCase("0.000001", "1")]
        public void UsdcParse(string input, string units)
        {
            Assert.IsTrue(UsdcUnits.TryParse(input, out var u));
            Assert.AreEqual(units, u);
        }

        [TestCase("1.2345678")]
        [TestCase("1,5")]
        [TestCase("1.2.3")]
        [TestCase("")]
        [TestCase("-1")]
        public void UsdcParseRejects(string input) => Assert.IsFalse(UsdcUnits.TryParse(input, out _));

        [TestCase("249999", "creator.rateRange")]
        [TestCase("250000", null)]
        [TestCase("50000000", null)]
        [TestCase("50000001", "creator.rateRange")]
        [TestCase("abc", "creator.rateInvalid")]
        public void CreatorRateRange(string units, string error) => Assert.AreEqual(error, UsdcUnits.RateError(units));

        [TestCase("12.5", 1250L)]
        [TestCase("$25", 2500L)]
        [TestCase("0.01", 1L)]
        public void CashInputParses(string input, long cents)
        {
            Assert.IsTrue(CashInput.TryCents(input, out var c));
            Assert.AreEqual(cents, c);
        }

        [TestCase("0")]
        [TestCase("1.234")]
        [TestCase("abc")]
        [TestCase("")]
        public void CashInputRejects(string input) => Assert.IsFalse(CashInput.TryCents(input, out _));

        [Test]
        public void CashFormat() => Assert.AreEqual("$1,234.50", CashInput.Format(123450));

        [Test]
        public void TipChecksCapThenBalance()
        {
            Assert.AreEqual("tip.badAmount", TipRules.CashError("x", 1000, null, out _));
            Assert.AreEqual("tip.overCap", TipRules.CashError("60", 100_000, 5000, out _));
            Assert.AreEqual("tip.overBalance", TipRules.CashError("20", 1000, null, out _));
            Assert.IsNull(TipRules.CashError("10", 1000, 5000, out var cents));
            Assert.AreEqual(1000, cents);
            Assert.IsNull(TipRules.CashError("10", null, null, out _), "unknown balance: the server decides");
        }

        private static string Encode58(byte[] data)
        {
            const string a = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
            var v = new BigInteger(data.Reverse().Concat(new byte[] { 0 }).ToArray());
            var sb = new StringBuilder();
            while (v > 0) { v = BigInteger.DivRem(v, 58, out var r); sb.Insert(0, a[(int)r]); }
            foreach (var b in data) { if (b != 0) break; sb.Insert(0, '1'); }
            return sb.ToString();
        }

        [Test]
        public void SignatureMustBe64BytesOfBase58()
        {
            var bytes = Enumerable.Range(1, 64).Select(i => (byte)(i * 3)).ToArray();
            var sig = Encode58(bytes);
            Assert.IsTrue(TipRules.IsSignature(sig));
            CollectionAssert.AreEqual(bytes, Base58Lite.Decode(sig));
            Assert.IsFalse(TipRules.IsSignature(sig.Substring(0, 60)));
            Assert.IsFalse(TipRules.IsSignature(sig.Replace(sig[3], '0')), "0 is not base58");
            Assert.IsFalse(TipRules.IsSignature(Encode58(Enumerable.Repeat((byte)7, 32).ToArray())), "a 32-byte address is not a signature");
            var leadingZeros = new byte[64]; leadingZeros[63] = 9; leadingZeros[2] = 1;
            CollectionAssert.AreEqual(leadingZeros, Base58Lite.Decode(Encode58(leadingZeros)));
        }

        [Test]
        public void RetainedKeyFollowsTheExactRequest()
        {
            int n = 0;
            var k = new RetainedKey(() => "k" + (++n));
            Assert.AreEqual("k1", k.For("POST", "/feed/posts", "{\"text\":\"gm\"}"));
            Assert.AreEqual("k1", k.For("POST", "/feed/posts", "{\"text\":\"gm\"}"), "unknown outcome: same key");
            Assert.AreEqual("k2", k.For("POST", "/feed/posts", "{\"text\":\"gn\"}"), "different body: new key");
            k.Complete();
            Assert.IsFalse(k.Pending);
            Assert.AreEqual("k3", k.For("POST", "/feed/posts", "{\"text\":\"gn\"}"), "after a definitive answer the same body is a new post");
            Assert.AreEqual("k4", k.For("DELETE", "/feed/posts", "{\"text\":\"gn\"}"), "method is part of the request");
        }

        [Test]
        public void BackoffSchedule()
        {
            var b = new Backoff(3);
            Assert.IsTrue(b.Due(0));
            b.Fail(0); Assert.AreEqual(1000, b.NextAtMs);
            b.Fail(1000); Assert.AreEqual(3000, b.NextAtMs);
            b.Fail(3000); Assert.IsTrue(b.Exhausted);
            Assert.IsFalse(b.Due(1e9));
            b.Hurry(5000);
            Assert.IsTrue(b.Due(5000));
            b.Reset();
            Assert.AreEqual(0, b.Attempts);
        }

        [Test]
        public void ErrorsAreClassified()
        {
            Assert.AreEqual(ErrorKind.Offline, SocialError.From(new ApiException(0, "NETWORK_ERROR", "x", isNetwork: true)).Kind);
            Assert.IsTrue(SocialError.From(new ApiException(503, "SERVER_BUSY", "x")).OutcomeUnknown);
            Assert.IsTrue(SocialError.From(new ApiException(500, "HTTP_500", "x")).OutcomeUnknown);
            Assert.IsTrue(SocialError.From(new ApiException(200, "DECODE_FAILED", "x")).OutcomeUnknown);
            var route = SocialError.From(new ApiException(404, "NOT_FOUND", "No route", JObject.Parse("{\"path\":\"/v1/rtc/token\",\"method\":\"POST\"}")));
            Assert.AreEqual(ErrorKind.RouteMissing, route.Kind);
            var gone = SocialError.From(new ApiException(404, "NOT_FOUND", "Resource not found.", JObject.Parse("{\"resource\":\"space\"}")));
            Assert.AreEqual(ErrorKind.Definitive, gone.Kind);
            Assert.AreEqual("err.notFound", gone.CopyKey);
            var host = SocialError.From(new ApiException(403, "FORBIDDEN", "x", JObject.Parse("{\"reason\":\"NOT_SPACE_HOST\"}")));
            Assert.AreEqual("NOT_SPACE_HOST", host.Code);
            Assert.AreEqual("err.notHost", host.CopyKey);
            Assert.AreEqual(ErrorKind.Session, SocialError.From(new ApiException(401, "TOKEN_EXPIRED", "x")).Kind);
            Assert.AreEqual("err.blocked", SocialError.From(new ApiException(403, "BLOCKED", "x")).CopyKey);
            Assert.AreEqual("WALLET_DECLINED", SocialError.From(new WalletException("no", true)).Code);
            Assert.AreEqual(ErrorKind.OutcomeUnknown, SocialError.From(new TimeoutException()).Kind);
            Assert.AreEqual(ErrorKind.Canceled, SocialError.From(new OperationCanceledException()).Kind);
            Assert.AreEqual("RATE_LIMITED", SocialError.From(new ApiException(429, "HTTP_429", "x")).Code);
        }

        [Test]
        public void WireTimesIdsAndSegments()
        {
            Assert.AreEqual(1_791_000_000_000L, SocialWire.TimeMs(new JValue(1_791_000_000)));
            Assert.AreEqual(1_791_000_000_123L, SocialWire.TimeMs(new JValue(1_791_000_000_123)));
            Assert.AreEqual(1_791_000_000_000L, SocialWire.TimeMs(new JValue("1791000000")));
            Assert.AreEqual(1_791_000_000_123L, SocialWire.TimeMs(new JValue("2026-10-03T04:00:00.123Z")));
            Assert.IsNull(SocialWire.TimeMs(null));
            Assert.Throws<FormatException>(() => SocialWire.TimeMs(new JValue("soon")));
            Assert.IsTrue(SocialWire.IsSafeId("space:0192a1c2-0000-7000-8000-000000000001"));
            Assert.IsFalse(SocialWire.IsSafeId("a/../b"));
            Assert.IsFalse(SocialWire.IsSafeId("a?b"));
            Assert.AreEqual("space%3Aabc", SocialWire.Seg("space:abc"));
            Assert.Throws<ArgumentException>(() => SocialWire.Seg("../x"));
            Assert.AreEqual("2026-10-03T04:00:00.123Z", SocialWire.IsoFromMs(1_791_000_000_123));
        }

        [Test]
        public void IdSetRoundTripsAndIgnoresJunk()
        {
            var s = new IdSet();
            int changes = 0;
            s.Changed += () => changes++;
            Assert.IsTrue(s.Set("a", true));
            Assert.IsFalse(s.Set("A", true), "case-insensitive");
            Assert.IsTrue(s.Set("b", true));
            var t = new IdSet();
            t.Load(s.Serialize() + "\n../evil\n\n");
            Assert.AreEqual(2, t.Count);
            Assert.IsTrue(t.Contains("B"));
            Assert.IsTrue(s.Set("a", false));
            Assert.AreEqual(3, changes);
        }

        [Test]
        public void StableGuidForBackendSpaceIds()
        {
            var g = SpacesStableGuid("space:0192a1c2-0000-7000-8000-000000000001");
            Assert.AreEqual(Guid.Parse("0192a1c2-0000-7000-8000-000000000001"), g);
            Assert.AreNotEqual(Guid.Empty, SpacesStableGuid("not-a-guid"));
            Assert.AreEqual(SpacesStableGuid("not-a-guid"), SpacesStableGuid("not-a-guid"));
        }

        private static Guid SpacesStableGuid(string id) => SocialWire.StableGuid(id);
    }
}
