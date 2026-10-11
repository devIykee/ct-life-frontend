using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Ventures.Tests
{
    public sealed class RealtimeTests
    {
        private static JToken J(string json) => JToken.Parse(json);

        private static object Ok(string type, string json)
        {
            Assert.AreEqual(ParseOutcome.Ok, VenturesRealtime.Parse(type, J(json), out var s), type + " " + json);
            return s;
        }

        [SetUp]
        public void SetUp() => VenturesRealtime.Warn = null;

        [TestCase("pending_review", AdStatus.PendingReview)]
        [TestCase("scheduled", AdStatus.Scheduled)]
        [TestCase("live", AdStatus.Live)]
        [TestCase("expired", AdStatus.Expired)]
        [TestCase("rejected", AdStatus.Rejected)]
        [TestCase("cancelled", AdStatus.Cancelled)]
        [TestCase("refunded", AdStatus.Refunded)]
        public void AdBookingUpdate_EveryStatusOfTheProposal(string wire, AdStatus expected)
        {
            var s = (AdBookingUpdateSignal)Ok("ad_booking_update", @"{""bookingId"":""b1"",""status"":""" + wire + @"""}");
            Assert.AreEqual(expected, s.status);
            Assert.AreEqual("b1", s.bookingId);
            Assert.IsNull(s.reasonId);
        }

        [Test]
        public void AdBookingUpdate_Rejected_CarriesTheReason()
        {
            var s = (AdBookingUpdateSignal)Ok("ad_booking_update", @"{""bookingId"":""b1"",""status"":""rejected"",""reasonId"":""ad_nsfw""}");
            Assert.AreEqual("ad_nsfw", s.reasonId);
        }

        [Test]
        public void AdBookingUpdate_AnUnknownStatusStillArrives_ForAResync()
        {
            var s = (AdBookingUpdateSignal)Ok("ad_booking_update", @"{""bookingId"":""b1"",""status"":""quarantined""}");
            Assert.AreEqual(AdStatus.Unknown, s.status);
            Assert.AreEqual("quarantined", s.statusRaw);
        }

        [TestCase(@"{""status"":""live""}")]
        [TestCase(@"{""bookingId"":""b1""}")]
        [TestCase(@"{""bookingId"":""a/b"",""status"":""live""}")]
        [TestCase(@"{""bookingId"":"""",""status"":""live""}")]
        [TestCase(@"[1,2]")]
        public void AdBookingUpdate_MalformedIsInvalid(string json) =>
            Assert.AreEqual(ParseOutcome.Invalid, VenturesRealtime.Parse("ad_booking_update", J(json), out _));

        [Test]
        public void AdBookingUpdate_ExtraFieldsAreIgnored()
        {
            var s = (AdBookingUpdateSignal)Ok("ad_booking_update", @"{""bookingId"":""b1"",""status"":""live"",""future"":{""x"":1}}");
            Assert.AreEqual(AdStatus.Live, s.status);
        }

        [Test]
        public void AdsChanged_FiltersAndCapsSlotIds()
        {
            var s = (AdsChangedSignal)Ok("ads_changed", @"{""slotIds"":[""billboard_01"",""billboard_01"",""bad slot"",""pixel_tile_001""]}");
            CollectionAssert.AreEqual(new[] { "billboard_01", "pixel_tile_001" }, s.slotIds);
            var many = new JObject { ["slotIds"] = new JArray(Enumerable.Range(0, 200).Select(i => "s" + i)) };
            Assert.AreEqual(64, ((AdsChangedSignal)Ok("ads_changed", many.ToString())).slotIds.Length);
        }

        [TestCase(@"{""slotIds"":[]}")]
        [TestCase(@"{""slotIds"":[""a b""]}")]
        [TestCase(@"{""slotIds"":""billboard_01""}")]
        [TestCase(@"{}")]
        public void AdsChanged_NeedsAtLeastOneSlot(string json) =>
            Assert.AreEqual(ParseOutcome.Invalid, VenturesRealtime.Parse("ads_changed", J(json), out _));

        [Test]
        public void VcOffer_Parses()
        {
            var s = (VcOfferSignal)Ok("vc_offer", @"{""offerId"":""o1"",""projectId"":""p1"",""vcArchetypeId"":""vc"",""investCents"":500000,""equityBps"":750,""expiresAt"":1791100000000}");
            Assert.AreEqual(500000, s.offer.InvestCents);
            Assert.AreEqual(750, s.offer.EquityBps);
            Assert.AreEqual(1791100000000, s.offer.ExpiresAtMs);
            Assert.AreEqual("pending", s.offer.Status);
        }

        [TestCase(@"{""offerId"":""o1"",""projectId"":""p1"",""vcArchetypeId"":""vc"",""investCents"":0,""equityBps"":750}")]
        [TestCase(@"{""offerId"":""o1"",""projectId"":""p1"",""vcArchetypeId"":""vc"",""investCents"":5,""equityBps"":10001}")]
        [TestCase(@"{""offerId"":""o1"",""projectId"":""p1"",""vcArchetypeId"":""vc"",""investCents"":5,""equityBps"":0}")]
        [TestCase(@"{""projectId"":""p1"",""investCents"":5,""equityBps"":5}")]
        [TestCase(@"{""offerId"":""o1"",""projectId"":""p1"",""vcArchetypeId"":""vc"",""investCents"":""lots"",""equityBps"":5}")]
        public void VcOffer_OutOfRangeOrIncompleteIsInvalid(string json) =>
            Assert.AreEqual(ParseOutcome.Invalid, VenturesRealtime.Parse("vc_offer", J(json), out _));

        [Test]
        public void HireOffer_Parses()
        {
            var s = (HireOfferSignal)Ok("hire_offer", @"{""offerId"":""o1"",""projectId"":""p1"",""projectName"":""FrogeSwap"",""fromUserId"":""u1"",""role"":""dev"",""salaryCentsPerWeek"":4000,""expiresAt"":1791100000000}");
            Assert.AreEqual("dev", s.offer.Role);
            Assert.AreEqual(4000, s.offer.SalaryCentsPerWeek);
            Assert.AreEqual(ParseOutcome.Invalid, VenturesRealtime.Parse("hire_offer", J(@"{""offerId"":""o1"",""projectId"":""p1"",""role"":""dev"",""salaryCentsPerWeek"":0}"), out _));
        }

        [Test]
        public void ProjectEvent_Parses()
        {
            var s = (ProjectEventSignal)Ok("project_event", @"{""projectId"":""p1"",""eventId"":""proj_exploit"",""cashDeltaCents"":-50000,""params"":{""lossCents"":""50000""},""at"":1791100000000}");
            Assert.AreEqual("proj_exploit", s.projectEvent.EventId);
            Assert.AreEqual(-50000, s.projectEvent.CashDeltaCents);
            Assert.AreEqual("50000", s.projectEvent.Params["lossCents"]);
            Assert.AreEqual(ParseOutcome.Invalid, VenturesRealtime.Parse("project_event", J(@"{""eventId"":""proj_exploit""}"), out _));
        }

        [Test]
        public void OtherMessages_AreNotOurs()
        {
            Assert.AreEqual(ParseOutcome.Unknown, VenturesRealtime.Parse("payment_confirmed", J("{}"), out var s));
            Assert.IsNull(s);
            Assert.IsFalse(VenturesRealtime.IsVenturesType("payment_confirmed"));
            Assert.IsTrue(VenturesRealtime.IsVenturesType("vc_offer"));
        }

        [Test]
        public void VcDump_FromTheCityEvent()
        {
            Assert.IsTrue(VenturesRealtime.TryParseVcDump("evt_vc_dump", new Dictionary<string, string> { ["projectId"] = "p1", ["name"] = "FrogeSwap", ["coinId"] = "c1" }, out var d));
            Assert.AreEqual("p1", d.projectId);
            Assert.AreEqual("FrogeSwap", d.name);
            Assert.AreEqual("c1", d.coinId);
            Assert.IsFalse(VenturesRealtime.TryParseVcDump("evt_rug", new Dictionary<string, string> { ["projectId"] = "p1" }, out _));
            Assert.IsFalse(VenturesRealtime.TryParseVcDump("evt_vc_dump", new Dictionary<string, string> { ["name"] = "x" }, out _), "no project id");
            Assert.IsFalse(VenturesRealtime.TryParseVcDump("evt_vc_dump", null, out _));
            Assert.IsTrue(VenturesRealtime.TryParseVcDump("evt_vc_dump", new Dictionary<string, string> { ["projectId"] = "p1", ["coinId"] = "bad id" }, out var d2));
            Assert.IsNull(d2.coinId);
        }

        [Test]
        public void Dispatch_AcceptsRoomDictionaries_AndPublishesTheSignal()
        {
            AdBookingUpdateSignal? got = null;
            var off = Bus.Subscribe<AdBookingUpdateSignal>(s => got = s);
            try
            {
                var payload = new Dictionary<string, object> { ["bookingId"] = "b9", ["status"] = "live" };
                Assert.IsTrue(VenturesRealtime.Dispatch("ad_booking_update", payload));
                Assert.AreEqual("b9", got.Value.bookingId);
                Assert.AreEqual(AdStatus.Live, got.Value.status);
            }
            finally { off(); }
        }

        [Test]
        public void Dispatch_DropsMalformedPayloads_WithoutThrowing()
        {
            int before = VenturesRealtime.Dropped;
            AdsChangedSignal? got = null;
            var off = Bus.Subscribe<AdsChangedSignal>(s => got = s);
            try
            {
                Assert.IsFalse(VenturesRealtime.Dispatch("ads_changed", "not json"));
                Assert.IsFalse(VenturesRealtime.Dispatch("ads_changed", null));
                Assert.IsFalse(VenturesRealtime.Dispatch("ads_changed", 42));
                Assert.IsFalse(VenturesRealtime.Dispatch("ads_changed", new Dictionary<string, object> { ["slotIds"] = new object[0] }));
                Assert.AreEqual(before + 4, VenturesRealtime.Dropped);
                Assert.IsNull(got);
                Assert.IsFalse(VenturesRealtime.Dispatch("something_else", new Dictionary<string, object>()), "not ours: ignored, not counted");
                Assert.AreEqual(before + 4, VenturesRealtime.Dropped);
            }
            finally { off(); }
        }

        [Test]
        public void Spots_MapToApps()
        {
            Assert.AreEqual("vc", VenturesSpots.AppFor("vc_row", "vc_row.center"));
            Assert.AreEqual("vc", VenturesSpots.AppFor("vc_row", "center"), "venue-local ids resolve against the venue");
            Assert.AreEqual("ads", VenturesSpots.AppFor("timeline_square", "timeline_square.pixel_wall"));
            Assert.AreEqual("ads", VenturesSpots.AppFor("conference_hall", "center"));
            Assert.AreEqual("builder", VenturesSpots.AppFor("builder_hills", "project_board"));
            Assert.IsNull(VenturesSpots.AppFor("vc_row", "vc_row.spot_1"));
            Assert.IsNull(VenturesSpots.AppFor(null, "center"));
            Assert.IsNull(VenturesSpots.AppFor("vc_row", ""));
        }
    }

    public sealed class RegistryTests
    {
        [Test]
        public void ReasonLabel_FromTheProposedRegistryShape()
        {
            var reg = JToken.Parse(@"{""adRejectionReasons"":[{""id"":""ad_nsfw"",""label"":""NSFW""},{""id"":""ad_bad_image"",""label"":""Image unreadable or wrong size""}]}");
            Assert.AreEqual("NSFW", VenturesRegistry.ReasonLabel(reg, "ad_nsfw"));
            Assert.IsNull(VenturesRegistry.ReasonLabel(reg, "ad_new_reason"));
            Assert.IsNull(VenturesRegistry.ReasonLabel(null, "ad_nsfw"));
            Assert.AreEqual("NSFW", VenturesRegistry.ReasonLabel(JToken.Parse(@"[{""id"":""ad_nsfw"",""label"":""NSFW""}]"), "ad_nsfw"));
        }

        [Test]
        public void Registries_AlsoReadTheConfigStoresItemsLayout()
        {
            Assert.AreEqual("NSFW", VenturesRegistry.ReasonLabel(JToken.Parse(@"{""registry"":""ad_rejection_reasons"",""items"":[{""id"":""ad_nsfw"",""label"":""NSFW""}]}"), "ad_nsfw"));
            Assert.IsTrue(VenturesRegistry.IsRiskEvent(JToken.Parse(@"{""items"":[{""id"":""proj_exploit"",""risk"":true}]}"), "proj_exploit"));
        }

        [Test]
        public void RiskEvents()
        {
            var reg = JToken.Parse(@"{""projectEvents"":[{""id"":""proj_exploit"",""risk"":true},{""id"":""proj_founded"",""risk"":false}]}");
            Assert.IsTrue(VenturesRegistry.IsRiskEvent(reg, "proj_exploit"));
            Assert.IsFalse(VenturesRegistry.IsRiskEvent(reg, "proj_founded"));
            Assert.IsFalse(VenturesRegistry.IsRiskEvent(reg, "proj_unknown"));
            Assert.IsFalse(VenturesRegistry.IsRiskEvent(null, "proj_exploit"));
        }
    }

    public sealed class CopyTests
    {
        [Test]
        public void EveryMessageFormatsWithoutThrowing()
        {
            foreach (var key in VStrings.Keys)
            {
                var text = VStrings.T(key);
                Assert.IsNotEmpty(text, key);
                // Up to four placeholders: a message with more would throw FormatException and fall back to the raw template.
                var args = new object[] { "a", "b", "c", "d" };
                Assert.DoesNotThrow(() => string.Format(System.Globalization.CultureInfo.InvariantCulture, text, args), key);
            }
        }

        [Test]
        public void UnknownKeyReturnsTheKey_NotAnException() => Assert.AreEqual("nope.nope", VStrings.T("nope.nope"));

        [Test]
        public void F_FallsBackToTheTemplateOnABadFormat() => Assert.AreEqual("nope {0} {1}", VStrings.F("nope {0} {1}", "x"), "a missing argument must not throw");

        [Test]
        public void EveryAdStatusAndProjectStatusHasCopy()
        {
            foreach (AdStatus s in Enum.GetValues(typeof(AdStatus)))
            {
                Assert.IsTrue(VStrings.Has("ad.status." + AdStatuses.Wire(s)), s.ToString());
                if (s != AdStatus.Unknown) Assert.IsTrue(VStrings.Has("toast.ad." + AdStatuses.Wire(s)), "toast " + s);
            }
            foreach (var st in new[] { "founded", "launching", "live", "paused", "rugged", "dead" }) Assert.IsTrue(VStrings.Has("pstatus." + st), st);
            foreach (var k in ProjectKinds.All) { Assert.IsTrue(VStrings.Has("kind." + k), k); Assert.IsTrue(VStrings.Has("kindhint." + k), k); }
            foreach (var r in ProjectKinds.HireRoles) Assert.IsTrue(VStrings.Has("role." + r), r);
            foreach (var b in new[] { "insufficient_treasury", "wrong_status", "no_dev_hired", "cooldown" }) Assert.IsTrue(VStrings.Has("launch.blocker." + b), b);
            foreach (BookProblem p in Enum.GetValues(typeof(BookProblem))) Assert.IsTrue(VStrings.Has("book.problem." + p), p.ToString());
            foreach (CreativeProblem p in Enum.GetValues(typeof(CreativeProblem))) if (p != CreativeProblem.None) Assert.IsTrue(VStrings.Has("creative." + p), p.ToString());
            foreach (NameProblem p in Enum.GetValues(typeof(NameProblem))) if (p != NameProblem.None) Assert.IsTrue(VStrings.Has("found.problem." + p), p.ToString());
            foreach (ProjectRules.HireProblem p in Enum.GetValues(typeof(ProjectRules.HireProblem))) if (p != ProjectRules.HireProblem.None) Assert.IsTrue(VStrings.Has("hire.problem." + p), p.ToString());
            foreach (ProjectRules.FundProblem p in Enum.GetValues(typeof(ProjectRules.FundProblem))) if (p != ProjectRules.FundProblem.None) Assert.IsTrue(VStrings.Has("fund.problem." + p), p.ToString());
            foreach (var g in new[] { "BadTicker", "NotEnoughTreasury", "Blocked" }) Assert.IsTrue(VStrings.Has("launch.gate." + g), g);
        }

        [Test]
        public void EveryErrorKeyTheMapperCanProduceHasCopy()
        {
            var codes = new[] { "INSUFFICIENT_CASH", "VALIDATION_FAILED", "NOT_FOUND", "FORBIDDEN", "BLOCKED", "RATE_LIMITED", "SERVER_BUSY", "TOKEN_EXPIRED", "UNAUTHORIZED", "CLIENT_OUTDATED", "PAYMENT_EXPIRED", "PAYMENT_UNDERPAID", "CONFLICT", "TEAPOT" };
            foreach (var c in codes)
                Assert.IsTrue(VStrings.Has(VErrors.From(new CTLife.Net.ApiException(400, c, "m"), true).Key), c);
            foreach (var r in new[] { "slot_unavailable", "project_limit", "quote_changed", "sold_out", "other" })
                Assert.IsTrue(VStrings.Has(VErrors.From(new CTLife.Net.ApiException(409, "CONFLICT", "m", new JObject { ["reason"] = r }), true).Key), r);
            foreach (var key in new[] { "err.walletDeclined", "err.walletFailed", "err.unknownOutcome", "err.network", "err.invalidResponse", "err.server", "err.unavailable" })
                Assert.IsTrue(VStrings.Has(key), key);
        }

        [Test]
        public void ProjectEventLine_UsesCopyOrAReadableFallback()
        {
            Assert.AreEqual("Token launched: -$1,750.00", VStrings.ProjectEventLine(new ProjectEvent { EventId = "proj_launched", CashDeltaCents = -175000 }));
            Assert.AreEqual("Project founded", VStrings.ProjectEventLine(new ProjectEvent { EventId = "proj_founded" }));
            Assert.AreEqual("Treasury hacked", VStrings.ProjectEventLine(new ProjectEvent { EventId = "proj_treasury_hacked" }));
            Assert.AreEqual("", VStrings.ProjectEventLine(null));
        }

        [TestCase(1048576L, "1 MB")]
        [TestCase(482113L, "471 KB")]
        [TestCase(900L, "900 B")]
        [TestCase(1572864L, "1.5 MB")]
        public void Bytes(long n, string expected) => Assert.AreEqual(expected, VStrings.Bytes(n));

        [Test]
        public void CreativeTooLarge_NamesTheLimit() => StringAssert.Contains("1 MB", VStrings.CreativeProblemText(CreativeProblem.TooLarge, 1048576));

        [Test]
        public void NamingFallbacks()
        {
            Assert.AreEqual("NFT studio", VStrings.Kind("nft_studio"));
            Assert.AreEqual("Weird thing", VStrings.Kind("weird_thing"));
            Assert.AreEqual("A VC partner", VStrings.VcName("some_future_vc"));
            Assert.AreEqual("Billboard", VStrings.AdKind("billboard"));
            Assert.AreEqual("Pixel Wall tile", VStrings.AdKind("pixel_wall"));
            Assert.AreEqual("Sky sign", VStrings.AdKind("sky_sign"), "a kind added later still shows something sensible");
        }
    }
}
