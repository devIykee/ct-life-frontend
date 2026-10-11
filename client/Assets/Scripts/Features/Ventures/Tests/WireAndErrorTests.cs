using System;
using System.Collections.Generic;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Ventures.Tests
{
    public sealed class WireTests
    {
        [TestCase("12", 12L)]
        [TestCase("\"9007199254740991\"", 9007199254740991L)]
        [TestCase("-5", -5L)]
        [TestCase("3.0", 3L)]
        public void Long_AcceptsIntegerStringAndIntegralFloat(string json, long expected) => Assert.AreEqual(expected, VWire.Long(JToken.Parse(json)));

        [TestCase("1.5")]
        [TestCase("\"abc\"")]
        [TestCase("true")]
        [TestCase("null")]
        [TestCase("{}")]
        public void Long_RejectsEverythingElse(string json) => Assert.Throws<FormatException>(() => VWire.Long(JToken.Parse(json)));

        [TestCase("\"20000000\"", "20000000")]
        [TestCase("20000000", "20000000")]
        [TestCase("\"000120\"", "120")]
        [TestCase("\"0\"", "0")]
        public void Units_ReturnsDigitStrings(string json, string expected) => Assert.AreEqual(expected, VWire.Units(JToken.Parse(json)));

        [TestCase("\"-5\"")]
        [TestCase("\"1.5\"")]
        [TestCase("\"0x10\"")]
        [TestCase("-1")]
        [TestCase("\"\"")]
        public void Units_RejectsNegativeDecimalAndGarbage(string json) => Assert.Throws<FormatException>(() => VWire.Units(JToken.Parse(json)));

        [Test]
        public void UnitsOr_FallsBack() => Assert.AreEqual("x", VWire.UnitsOr(JToken.Parse("\"zz\""), "x"));

        [Test]
        public void TimeMs_ReadsIsoAndEpoch()
        {
            Assert.AreEqual(1_791_000_000_000, VWire.TimeMs(JToken.Parse("1791000000000")));
            Assert.AreEqual(1_791_000_000_000, VWire.TimeMs(JToken.Parse("\"1791000000000\"")));
            Assert.AreEqual(1_791_000_000_000, VWire.TimeMs(JToken.FromObject("2026-10-03T04:00:00.000Z")));
            Assert.IsNull(VWire.TimeMs(JValue.CreateNull()));
            Assert.Throws<FormatException>(() => VWire.TimeMs(JToken.Parse("\"someday\"")));
            Assert.IsNull(VWire.TimeMsOrNull(JToken.Parse("\"someday\"")));
        }

        [Test]
        public void List_AcceptsBareAndWrapped()
        {
            Assert.AreEqual(2, VWire.List(JToken.Parse("[1,2]"), "x").Count);
            Assert.AreEqual(1, VWire.List(JToken.Parse("{\"bookings\":[1]}"), "bookings").Count);
            Assert.Throws<FormatException>(() => VWire.List(JToken.Parse("{\"a\":1}"), "bookings"));
        }

        [Test]
        public void Plain_DropsControlCharactersAndCaps()
        {
            Assert.AreEqual("abc", VWire.Plain("a\u0000b\nc", 10));
            Assert.AreEqual("abc", VWire.Plain("abcdef", 3));
            Assert.AreEqual("", VWire.Plain((string)null, 3));
        }

        [TestCase("billboard_01", true)]
        [TestCase("0192a1c2-7b3e-7c4d-8e5f-6a7b8c9d0e10", true)]
        [TestCase("a/b", false)]
        [TestCase("a b", false)]
        [TestCase("../x", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsSafeId(string id, bool expected) => Assert.AreEqual(expected, VWire.IsSafeId(id));

        [Test]
        public void Seg_RefusesUnsafeIds() => Assert.Throws<ArgumentException>(() => VWire.Seg("a/../b"));

        [Test]
        public void ToToken_NormalisesDictionariesStringsAndTokens()
        {
            var dict = new Dictionary<string, object> { ["bookingId"] = "b1", ["n"] = (sbyte)5, ["nested"] = new Dictionary<object, object> { ["k"] = 1L } };
            var t = VWire.ToToken(dict);
            Assert.AreEqual("b1", (string)t["bookingId"]);
            Assert.AreEqual(5, (int)t["n"]);
            Assert.AreEqual(1, (int)t["nested"]["k"]);
            Assert.AreEqual("x", (string)VWire.ToToken("{\"a\":\"x\"}")["a"]);
            Assert.IsNull(VWire.ToToken("not json"));
            Assert.IsNull(VWire.ToToken(null));
            var j = new JObject();
            Assert.AreSame(j, VWire.ToToken(j));
        }

        [TestCase("nft_studio", "Nft studio")]
        [TestCase("l2", "L2")]
        [TestCase("", "")]
        public void Readable(string id, string expected) => Assert.AreEqual(expected, VWire.Readable(id));
    }

    public sealed class ErrorTests
    {
        private static VError Api(int status, string code, string reason = null, JObject extra = null)
        {
            var d = extra ?? new JObject();
            if (reason != null) d["reason"] = reason;
            return VErrors.From(new ApiException(status, code, "msg", d), true);
        }

        [Test]
        public void MissingRoute_IsUnavailable_NotAnError()
        {
            foreach (var status in new[] { 404, 405, 501 })
            {
                var e = VErrors.From(new ApiException(status, "HTTP_" + status, "x"), false);
                Assert.IsTrue(e.IsUnavailable, "status " + status);
                Assert.AreEqual("err.unavailable", e.Key);
            }
        }

        [Test]
        public void ServerNoRoute_IsUnavailable()
        {
            var e = VErrors.From(new ApiException(404, "NOT_FOUND", "No route for this path and method."), false);
            Assert.IsTrue(e.IsUnavailable);
            Assert.AreEqual("err.unavailable", e.Key);
        }

        [Test]
        public void ContractNotFound_IsNotUnavailable()
        {
            var e = VErrors.From(new ApiException(404, "NOT_FOUND", "no such booking"), false);
            Assert.IsFalse(e.IsUnavailable);
            Assert.AreEqual("err.notFound", e.Key);
        }

        [Test]
        public void Conflict_Reasons()
        {
            Assert.AreEqual("err.slotTaken", Api(409, "CONFLICT", "slot_unavailable").Key);
            Assert.AreEqual("err.soldOut", Api(409, "CONFLICT", "sold_out").Key);
            var limit = Api(409, "CONFLICT", "project_limit", new JObject { ["max"] = 1 });
            Assert.AreEqual("err.projectLimit", limit.Key);
            Assert.AreEqual("1", limit.Args[0]);
            Assert.AreEqual("err.conflict", Api(409, "CONFLICT").Key);
        }

        [Test]
        public void QuoteChanged_CarriesTheNewCost()
        {
            var e = Api(409, "CONFLICT", "quote_changed", new JObject { ["launchCostCents"] = 180000 });
            Assert.AreEqual("err.quoteChanged", e.Key);
            Assert.AreEqual(180000, e.NewCostCents);
            StringAssert.Contains("1,800.00", e.Message);
        }

        [Test]
        public void InsufficientCash_BothShapes()
        {
            var contract = Api(402, "INSUFFICIENT_CASH", null, new JObject { ["shortfallCents"] = 12345 });
            Assert.AreEqual("err.insufficientCash", contract.Key);
            StringAssert.Contains("$123.45", contract.Message);
            var backend = Api(400, "VALIDATION_FAILED", "insufficient cash", new JObject { ["need"] = 5000, ["have"] = 1000 });
            Assert.AreEqual("INSUFFICIENT_CASH", backend.Code);
            StringAssert.Contains("$40.00", backend.Message);
        }

        [Test]
        public void Network_OnWrite_IsUnknownOutcome()
        {
            var w = VErrors.From(new ApiException(0, "NETWORK_ERROR", "x", null, true), true);
            Assert.IsTrue(w.OutcomeUnknown);
            Assert.IsTrue(w.Retryable);
            var r = VErrors.From(new ApiException(0, "NETWORK_ERROR", "x", null, true), false);
            Assert.IsFalse(r.OutcomeUnknown);
            Assert.IsTrue(VErrors.From(new TimeoutException(), true).OutcomeUnknown);
            Assert.IsTrue(VErrors.From(new FormatException(), true).OutcomeUnknown);
        }

        [Test]
        public void Server5xx_OnWrite_IsUnknownOutcome()
        {
            var e = VErrors.From(new ApiException(500, "INTERNAL_ERROR", "x"), true);
            Assert.IsTrue(e.OutcomeUnknown);
            Assert.AreEqual("err.unknownOutcome", e.Key);
        }

        [Test]
        public void Wallet_DeclineIsRetryableNotUnknown()
        {
            var d = VErrors.From(new CTLife.Core.WalletException("no", userDeclined: true), true);
            Assert.AreEqual("WALLET_DECLINED", d.Code);
            Assert.IsFalse(d.OutcomeUnknown);
            Assert.IsTrue(VErrors.From(new CTLife.Core.WalletException("boom"), true).OutcomeUnknown);
        }

        [Test]
        public void RateLimit_ShowsSeconds()
        {
            var e = Api(429, "RATE_LIMITED", null, new JObject { ["retryAfterMs"] = 4200 });
            Assert.IsTrue(e.Retryable);
            StringAssert.Contains("5s", e.Message);
        }

        [Test]
        public void UnknownCode_FallsBackToGeneric()
        {
            Assert.AreEqual("err.generic", Api(418, "TEAPOT").Key);
            Assert.AreEqual("err.generic", VErrors.From(null, false).Key);
        }
    }

    public sealed class WriteFlowTests
    {
        private int _n;
        private WriteFlow Make() => new WriteFlow(() => "k" + (++_n));

        [Test]
        public void Success_RetiresTheKey()
        {
            var f = Make();
            Assert.AreEqual("k1", f.Begin("POST", "/x", "{}"));
            Assert.IsNull(f.Begin("POST", "/x", "{}"), "busy while sending");
            f.Succeeded();
            Assert.AreEqual(WriteStage.Done, f.Stage);
            f.Reset();
            Assert.AreEqual("k2", f.Begin("POST", "/x", "{}"));
        }

        [Test]
        public void UnknownOutcome_ReplaysTheSameKey_AndBlocksADifferentRequest()
        {
            var f = Make();
            var k = f.Begin("POST", "/x", "a");
            f.Failed(new VError { Code = "TIMEOUT", Key = "err.unknownOutcome", OutcomeUnknown = true });
            Assert.AreEqual(WriteStage.Unknown, f.Stage);
            Assert.IsNull(f.Begin("POST", "/x", "b"), "a different body must not abandon an unknown outcome");
            Assert.AreEqual(k, f.Begin("POST", "/x", "a"));
        }

        [Test]
        public void Rejection_RetiresTheKey()
        {
            var f = Make();
            f.Begin("POST", "/x", "a");
            f.Failed(new VError { Code = "CONFLICT", Key = "err.conflict" });
            Assert.AreEqual(WriteStage.Rejected, f.Stage);
            Assert.IsFalse(f.KeyRetained);
            Assert.AreEqual("k2", f.Begin("POST", "/x", "a"));
        }

        [Test]
        public void Reset_DoesNotInterruptASend()
        {
            var f = Make();
            f.Begin("POST", "/x", "a");
            f.Reset();
            Assert.AreEqual(WriteStage.Sending, f.Stage);
        }
    }
}
