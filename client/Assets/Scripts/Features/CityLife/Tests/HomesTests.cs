using System;
using System.Collections.Generic;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.CityLife.Tests
{
    public class HomeLeaseActionTests
    {
        [TestCase("studio", "studio", true)]
        [TestCase("studio", "penthouse", false)]
        [TestCase(null, "studio", false)]
        [TestCase("studio", null, false)]
        public void ExistingRentEndpointCannotBeUsedToMove(string current, string target, bool allowed)
        {
            Assert.That(HomeLeaseActions.CanRenew(current, target), Is.EqualTo(allowed));
        }
    }

    internal static class Keys
    {
        /// <summary>Deterministic Idempotency-Keys: k1, k2, ...</summary>
        public static Func<string> Seq()
        {
            int n = 0;
            return () => "k" + (++n);
        }

        public static CityError Network() => CityErrors.From(new ApiException(0, "NETWORK_ERROR", "offline", isNetwork: true), true);
        public static CityError Api(int status, string code, JObject details = null, bool write = true) =>
            CityErrors.From(new ApiException(status, code, "msg", details), write);
    }

    public class RentLedgerTests
    {
        private const double Hour = 3_600_000;
        private static RentInputs Inputs(long rent = 6000, long cash = 10000, long debt = 0, long? due = null, string status = null, int missed = -1) =>
            new RentInputs { HomeId = "hacker_house", RentCents = rent, CashCents = cash, DebtCents = debt, NextDueAtMs = due, ServerStatus = status, MissedWeeks = missed };

        [Test]
        public void NoHomeIsUnknown()
        {
            var v = RentLedger.Evaluate(new RentInputs(), new RentPolicy(), 0, 1000);
            Assert.AreEqual(RentState.Unknown, v.State);
        }

        [Test]
        public void FarFromDueIsPaid()
        {
            var v = RentLedger.Evaluate(Inputs(due: (long)(100 * Hour)), new RentPolicy(), 0, 0);
            Assert.AreEqual(RentState.Paid, v.State);
            Assert.AreEqual(6000, v.RentCents);
        }

        [Test]
        public void WithinDueSoonWindowWithCashIsDueSoon()
        {
            var v = RentLedger.Evaluate(Inputs(due: (long)(47 * Hour)), new RentPolicy(), 0, 0);
            Assert.AreEqual(RentState.DueSoon, v.State);
        }

        [Test]
        public void WithinDueSoonWindowWithoutCashIsShort()
        {
            var v = RentLedger.Evaluate(Inputs(cash: 2500, due: (long)(10 * Hour)), new RentPolicy(), 0, 0);
            Assert.AreEqual(RentState.ShortOfCash, v.State);
            Assert.AreEqual(3500, v.ShortfallCents);
        }

        [Test]
        public void PastDueIsDueNow()
        {
            var v = RentLedger.Evaluate(Inputs(due: 1000), new RentPolicy(), 2000, 0);
            Assert.AreEqual(RentState.DueNow, v.State);
        }

        [Test]
        public void MissingDueUsesSundayUnlockFallback()
        {
            var v = RentLedger.Evaluate(Inputs(due: null), new RentPolicy(), 0, 5 * Hour);
            Assert.AreEqual(RentState.DueSoon, v.State);
            Assert.AreEqual((long)(5 * Hour), v.DueAtMs);
        }

        [Test]
        public void ServerOverdueAndMissedWeeksWin()
        {
            Assert.AreEqual(RentState.Overdue, RentLedger.Evaluate(Inputs(status: "overdue", due: (long)(100 * Hour)), new RentPolicy(), 0, 0).State);
            Assert.AreEqual(RentState.Overdue, RentLedger.Evaluate(Inputs(missed: 1, due: (long)(100 * Hour)), new RentPolicy(), 0, 0).State);
        }

        [Test]
        public void EvictionRiskNeedsServerPolicyAndCount()
        {
            var policy = RentPolicy.Parse(JObject.Parse("{\"rentPolicy\":{\"graceWeeks\":3,\"evictToHomeId\":\"parents_basement\"}}"));
            var v = RentLedger.Evaluate(Inputs(missed: 2), policy, 0, 0);
            Assert.AreEqual(RentState.EvictionRisk, v.State);
            Assert.AreEqual(1, v.WeeksBeforeEviction);
            Assert.AreEqual("parents_basement", policy.EvictToHomeId);
            // Without a policy the client never invents an eviction countdown.
            var none = RentLedger.Evaluate(Inputs(missed: 2), new RentPolicy(), 0, 0);
            Assert.AreEqual(RentState.Overdue, none.State);
            Assert.AreEqual(-1, none.WeeksBeforeEviction);
        }

        [Test]
        public void EvictedIsTerminalAndDebtFlagged()
        {
            var v = RentLedger.Evaluate(Inputs(status: "evicted", debt: 7200), new RentPolicy(), 0, 0);
            Assert.AreEqual(RentState.Evicted, v.State);
            Assert.IsTrue(v.InDebt);
        }

        [Test]
        public void UnknownRentNeverGuessed()
        {
            var v = RentLedger.Evaluate(Inputs(rent: -1, due: 10), new RentPolicy(), 0, 0);
            Assert.AreEqual(RentState.Unknown, v.State);
        }

        [Test]
        public void LotteryDebtTermsFromEconomyConfig()
        {
            var economy = JObject.Parse("{\"lottery\":{\"exit_liquidity\":{\"weeklyDueCents\":12000,\"missedPenaltyBps\":1000}}}");
            var p = RentPolicy.Parse(economy);
            Assert.AreEqual(12000, p.LotteryWeeklyDueCents);
            Assert.AreEqual(1000, p.LotteryMissedPenaltyBps);
            Assert.AreEqual(-1, p.GraceWeeks);
        }

        private static RentView V(RentState s) => new RentView(s, false, 0, 6000, 0, -1);

        [TestCase(RentState.Paid, RentState.Overdue, "rent.toast.overdue")]
        [TestCase(RentState.Overdue, RentState.EvictionRisk, "rent.toast.evictionRisk")]
        [TestCase(RentState.EvictionRisk, RentState.Evicted, "rent.toast.evicted")]
        [TestCase(RentState.DueNow, RentState.Paid, "rent.toast.settled")]
        [TestCase(RentState.Paid, RentState.ShortOfCash, "rent.toast.short")]
        [TestCase(RentState.Paid, RentState.DueSoon, null)]
        [TestCase(RentState.Overdue, RentState.Overdue, null)]
        public void Transitions(RentState from, RentState to, string expected)
        {
            Assert.AreEqual(expected, RentLedger.Transition(V(from), V(to), 0, 0));
        }

        [Test]
        public void DebtIncreaseAndFirstObservation()
        {
            Assert.AreEqual("rent.toast.debtUp", RentLedger.Transition(V(RentState.Paid), V(RentState.Paid), 0, 12000));
            Assert.IsNull(RentLedger.Transition(null, V(RentState.Overdue), -1, 0));
            Assert.AreEqual("rent.toast.evicted", RentLedger.Transition(null, V(RentState.Evicted), -1, 0));
        }
    }

    public class MoveHomeFlowTests
    {
        private static HomeOffer Studio => new HomeOffer { Id = "studio", Name = "Studio", RentCents = 18000 };

        [Test]
        public void HappyPathRetiresKey()
        {
            var f = new MoveHomeFlow(Keys.Seq());
            Assert.IsTrue(f.Select(Studio, "hacker_house"));
            Assert.AreEqual(MoveStage.Confirming, f.Stage);
            Assert.IsFalse(f.IsRenewal);
            Assert.AreEqual("/homes/studio/rent", f.Path);
            Assert.AreEqual("k1", f.BeginSign());
            Assert.AreEqual(MoveStage.Signing, f.Stage);
            f.Succeeded(new LeaseReceipt { HomeId = "studio", RentCents = 18000 });
            Assert.AreEqual(MoveStage.Done, f.Stage);
            Assert.IsFalse(f.KeyRetained);
        }

        [Test]
        public void UnknownOutcomeRetriesWithSameKey()
        {
            var f = new MoveHomeFlow(Keys.Seq());
            f.Select(Studio, "hacker_house");
            Assert.AreEqual("k1", f.BeginSign());
            f.Failed(Keys.Network());
            Assert.AreEqual(MoveStage.Unknown, f.Stage);
            Assert.IsTrue(f.KeyRetained);
            Assert.IsFalse(f.Select(new HomeOffer { Id = "penthouse", RentCents = 150000 }, "hacker_house"), "cannot switch target while unknown");
            Assert.AreEqual("k1", f.BeginSign(), "same request, same key");
        }

        [Test]
        public void RejectionRetiresKeyAndNextTryIsNew()
        {
            var f = new MoveHomeFlow(Keys.Seq());
            f.Select(Studio, "hacker_house");
            f.BeginSign();
            f.Failed(Keys.Api(402, "INSUFFICIENT_CASH", JObject.Parse("{\"shortfallCents\":5000}")));
            Assert.AreEqual(MoveStage.Rejected, f.Stage);
            Assert.AreEqual("err.insufficientCash", f.Error.Key);
            Assert.AreEqual("Not enough Cash (short $50.00).", f.Error.Message);
            Assert.IsFalse(f.KeyRetained);
            Assert.AreEqual("k2", f.BeginSign());
        }

        [Test]
        public void RenewalAndGuards()
        {
            var f = new MoveHomeFlow(Keys.Seq());
            Assert.IsFalse(f.Select(new HomeOffer { Id = "studio", RentCents = -1 }, "studio"), "no price, no lease");
            Assert.IsFalse(f.Select(new HomeOffer { Id = "../x", RentCents = 1 }, "studio"));
            Assert.IsNull(f.BeginSign(), "nothing selected");
            Assert.IsTrue(f.Select(Studio, "studio"));
            Assert.IsTrue(f.IsRenewal);
            f.BeginSign();
            Assert.IsFalse(f.Select(Studio, "studio"), "busy");
            f.Cancel();
            Assert.AreEqual(MoveStage.Signing, f.Stage, "cannot cancel an in-flight request");
        }

        [Test]
        public void CancelFromUnknownAbandonsKey()
        {
            var f = new MoveHomeFlow(Keys.Seq());
            f.Select(Studio, "hacker_house");
            f.BeginSign();
            f.Failed(Keys.Network());
            f.Cancel();
            Assert.AreEqual(MoveStage.Idle, f.Stage);
            Assert.IsFalse(f.KeyRetained);
        }

        [Test]
        public void LeaseReceiptContractAndBackendShapes()
        {
            var c = LeaseReceipt.Parse(JObject.Parse("{\"homeId\":\"studio\",\"rentCents\":18000,\"nextDueAt\":\"2026-10-11T20:00:00.000Z\"}"));
            Assert.AreEqual("studio", c.HomeId);
            Assert.AreEqual(18000, c.RentCents);
            Assert.AreEqual(DateTimeOffset.Parse("2026-10-11T20:00:00Z").ToUnixTimeMilliseconds(), c.NextDueAtMs);
            var b = LeaseReceipt.Parse(JObject.Parse("{\"homeId\":\"home:0192\",\"kind\":\"studio\",\"rentCentsPerWeek\":\"18000\",\"lastRentPaidAt\":\"2026-10-05T00:00:00Z\"}"));
            Assert.AreEqual("studio", b.HomeId);
            Assert.AreEqual(18000, b.RentCents);
            Assert.IsNull(b.NextDueAtMs);
            Assert.Throws<FormatException>(() => LeaseReceipt.Parse(JArray.Parse("[]")));
        }
    }

    public class HomeCatalogTests
    {
        private const string Ids = "{\"homes\":[{\"id\":\"studio\",\"name\":\"Studio\",\"rentCents\":18000,\"rooms\":3,\"phase\":1},{\"id\":\"parents_basement\",\"name\":\"Parent's Basement\",\"rentCents\":2000,\"rooms\":4,\"phase\":1},{\"id\":\"marina_villa\",\"name\":\"Marina Villa\",\"rentCents\":500000,\"rooms\":12,\"phase\":2}]}";
        private const string Economy = "{\"rentCentsPerWeek\":{\"parents_basement\":2000,\"hacker_house\":6000,\"studio\":18000,\"penthouse\":150000,\"marina_villa\":500000}}";

        [Test]
        public void RegistryRowsInSpecOrderWithEconomyRent()
        {
            var list = HomeCatalog.FromRegistry(JObject.Parse(Ids), JObject.Parse(Economy));
            CollectionAssert.AreEqual(new[] { "parents_basement", "hacker_house", "studio", "penthouse", "marina_villa" }, list.ConvertAll(h => h.Id));
            Assert.AreEqual(6000, list[1].RentCents, "economy fills homes missing from the registry");
            Assert.AreEqual(12, list[4].Rooms);
            Assert.IsFalse(list[0].FromServer);
        }

        [Test]
        public void ServerRowsWin()
        {
            var reg = HomeCatalog.FromRegistry(JObject.Parse(Ids), JObject.Parse(Economy));
            var merged = HomeCatalog.Merge(reg, JArray.Parse("[{\"id\":\"studio\",\"name\":\"Studio\",\"rentCents\":9000,\"nextDueAt\":\"2026-10-11T20:00:00.000Z\",\"status\":\"overdue\",\"missedWeeks\":1,\"frameSlots\":2}]"));
            var studio = merged.Find(h => h.Id == "studio");
            Assert.AreEqual(9000, studio.RentCents);
            Assert.IsTrue(studio.FromServer);
            Assert.AreEqual("overdue", studio.Status);
            Assert.AreEqual(1, studio.MissedWeeks);
            Assert.AreEqual(2, studio.FrameSlots);
            Assert.IsNotNull(studio.NextDueAtMs);
            Assert.Throws<FormatException>(() => HomeCatalog.Merge(reg, JObject.Parse("{\"nope\":1}")));
            Assert.AreEqual(5, HomeCatalog.Merge(reg, JObject.Parse("{\"homes\":[]}")).Count, "wrapped list tolerated");
        }

        [Test]
        public void FrameSlotsUseServerThenRoomsThenAuthored()
        {
            var villa = new HomeOffer { Id = "marina_villa", Rooms = 12 };
            Assert.AreEqual(8, HomeCatalog.FrameSlots(villa, 8));
            Assert.AreEqual(3, HomeCatalog.FrameSlots(new HomeOffer { Rooms = 3 }, 8));
            Assert.AreEqual(2, HomeCatalog.FrameSlots(villa, 8, 2));
            Assert.AreEqual(0, HomeCatalog.FrameSlots(villa, 0));
            Assert.AreEqual(5, HomeCatalog.FrameSlots(null, 5));
        }

        [TestCase("home.bed", "bed", "sleep")]
        [TestCase("bed_2", "bed", "sleep")]
        [TestCase("home.desk", "desk", "eat")]
        [TestCase("home.plant", "plant", "touch_grass")]
        [TestCase("home.shower", "shower", "shower")]
        [TestCase("home.speaker", "speaker", "party")]
        public void FurnitureMapsToNeedsActions(string spot, string kind, string action)
        {
            Assert.AreEqual(kind, HomeFurniture.Kind(spot));
            CollectionAssert.Contains(HomeFurniture.ActionsFor(spot), action);
        }

        [Test]
        public void FrameSpots()
        {
            Assert.IsTrue(HomeFurniture.IsFrameSpot("home.wall_3"));
            Assert.AreEqual(3, HomeFurniture.FrameIndex("home.wall_3"));
            Assert.AreEqual(12, HomeFurniture.FrameIndex("frame_12"));
            Assert.AreEqual(0, HomeFurniture.FrameIndex("home.bed"));
            Assert.IsEmpty(HomeFurniture.ActionsFor("home.wall_1"));
            Assert.IsEmpty(HomeFurniture.ActionsFor(null));
        }
    }

    public class HomeVisitFlowTests
    {
        [Test]
        public void RequestGrantJoinLeave()
        {
            var f = new HomeVisitFlow(Keys.Seq());
            Assert.AreEqual("k1", f.Request("owner-1", "anon", "me"));
            Assert.AreEqual(VisitStage.Requesting, f.Stage);
            f.Granted(new VisitTicket { OwnerId = "owner-1", RoomId = "home:0192" });
            Assert.AreEqual(VisitStage.Joining, f.Stage);
            f.Joined(2);
            Assert.IsTrue(f.IsInside);
            f.PresenceChanged(3);
            Assert.AreEqual(3, f.Visitors);
            Assert.IsTrue(f.BeginLeave());
            f.Left(unexpected: false);
            Assert.AreEqual(VisitStage.Idle, f.Stage);
            Assert.IsNull(f.Error);
        }

        [Test]
        public void ServerWithoutRoomIsUnavailable()
        {
            var t = VisitTicket.Parse("owner-1", JObject.Parse("{\"visitId\":\"v\",\"homeId\":\"home:1\",\"visitorUserId\":\"me\",\"visitedAt\":\"2026-10-08T00:00:00Z\"}"));
            Assert.IsNull(t, "backend visit-log reply is not a room");
            var f = new HomeVisitFlow(Keys.Seq());
            f.Request("owner-1", null, "me");
            f.Granted(t);
            Assert.AreEqual(VisitStage.Unavailable, f.Stage);
            Assert.AreEqual("visit.noRoom", f.Error.Key);
        }

        [Test]
        public void RoomIdValidated()
        {
            Assert.AreEqual("home:0192a1c2-x", VisitTicket.Parse("o", JObject.Parse("{\"roomId\":\"home:0192a1c2-x\"}")).RoomId);
            Assert.Throws<FormatException>(() => VisitTicket.Parse("o", JObject.Parse("{\"roomId\":\"../../etc\"}")));
        }

        [Test]
        public void ClosedDoorIsDenied()
        {
            var f = new HomeVisitFlow(Keys.Seq());
            f.Request("owner-1", null, "me");
            f.RequestFailed(Keys.Api(403, "FORBIDDEN", JObject.Parse("{\"reason\":\"visits_closed\"}")));
            Assert.AreEqual(VisitStage.Denied, f.Stage);
            Assert.AreEqual("They're not taking visitors.", f.Error.Message);
            f.Dismiss();
            Assert.AreEqual(VisitStage.Idle, f.Stage);
            Assert.IsFalse(f.KeyRetained);
        }

        [Test]
        public void UnknownOutcomeKeepsKeyForSameOwner()
        {
            var f = new HomeVisitFlow(Keys.Seq());
            Assert.AreEqual("k1", f.Request("owner-1", null, "me"));
            f.RequestFailed(Keys.Network());
            Assert.AreEqual(VisitStage.Failed, f.Stage);
            Assert.IsTrue(f.KeyRetained);
            Assert.AreEqual("k1", f.Request("owner-1", null, "me"));
            f.RequestFailed(Keys.Network());
            Assert.AreEqual("k2", f.Request("owner-2", null, "me"), "another owner is another request");
        }

        [Test]
        public void UnexpectedCloseAndJoinFailure()
        {
            var f = new HomeVisitFlow(Keys.Seq());
            f.Request("o", null, "me");
            f.Granted(new VisitTicket { OwnerId = "o", RoomId = "r1" });
            f.JoinFailed(new CityError { Code = "VENUE_FULL", Key = "err.homeFull" });
            Assert.AreEqual(VisitStage.Denied, f.Stage);
            f.Dismiss();
            f.Request("o", null, "me");
            f.Granted(new VisitTicket { OwnerId = "o", RoomId = "r1" });
            f.Joined(1);
            f.Left(unexpected: true);
            Assert.AreEqual(VisitStage.Failed, f.Stage);
            Assert.AreEqual("visit.closed", f.Error.Key);
        }

        [Test]
        public void GuardsAndSelfVisit()
        {
            var f = new HomeVisitFlow(Keys.Seq());
            Assert.IsNull(f.Request("bad id!", null, "me"));
            Assert.IsNotNull(f.Request("me", "me", "me"));
            Assert.IsTrue(f.IsOwnHome("me"));
            Assert.IsNull(f.Request("x", null, "me"), "busy");
            Assert.IsFalse(new HomeVisitFlow().BeginLeave());
        }

        [Test]
        public void VisitorPolicyFailsClosed()
        {
            Assert.AreEqual(VisitorPolicy.Open, VisitorPolicies.Parse(JObject.Parse("{\"visitors\":\"open\"}")));
            Assert.AreEqual(VisitorPolicy.Followers, VisitorPolicies.Parse(JObject.Parse("{\"visitors\":\"followers\"}")));
            Assert.AreEqual(VisitorPolicy.Closed, VisitorPolicies.Parse(JObject.Parse("{\"visitors\":\"everyone\"}")));
            Assert.AreEqual(VisitorPolicy.Closed, VisitorPolicies.Parse(null));
            Assert.AreEqual("followers", VisitorPolicies.Wire(VisitorPolicy.Followers));
        }
    }
}
