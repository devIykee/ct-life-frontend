using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.CityLife.RektBurger;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.CityLife.Tests
{
    /// <summary>Rekt Burger (backend PR #16): payloads (a real local GET /jobs/rekt-burger), career_promoted, the clock-in fake.</summary>
    public sealed class RektBurgerTests
    {
        private const string StateJson = @"{""jobId"":""rekt-burger"",""name"":""Rekt Burger"",""venueId"":""the_trenches"",""propId"":""rekt_burger_counter"",""unlocked"":true,""reason"":""trencher"",""lockedHint"":null,""employed"":false,""stintShifts"":0,""netWorthCents"":-42000,""glowUpNetWorthCents"":100000,""pay"":{""baseCents"":300,""perCorrectOrderCents"":60,""maxShiftCents"":1500,""dailyCapCents"":4500,""earnedTodayCents"":0,""remainingTodayCents"":4500,""resetsAt"":""2026-10-26T00:00:00.000Z""},""shift"":null,""uniformCosmeticId"":""reward_rekt_burger_uniform"",""hasUniform"":false}";

        [Test]
        public void State_TrencherUnlocked()
        {
            var s = RbState.Parse(JObject.Parse(StateJson));
            Assert.IsTrue(s.Unlocked);
            Assert.AreEqual("trencher", s.Reason);
            Assert.AreEqual(4500, s.DailyCapCents);
            Assert.AreEqual(4500, s.RemainingTodayCents);
            Assert.IsFalse(s.CapReached);
            Assert.AreEqual("reward_rekt_burger_uniform", s.UniformCosmeticId);
        }

        [Test]
        public void FakeShift_StartCurrentEnd()
        {
            var fake = new FakeRektShiftService("test-user-" + System.Guid.NewGuid(), () => 4500);
            var started = fake.Start("k1", default).Result;
            Assert.IsTrue(started.Active);
            Assert.AreEqual(started.ShiftId, fake.Start("k2", default).Result.ShiftId);   // idempotent while open
            var (cur, unseen) = fake.Current(default).Result;
            Assert.AreEqual(started.ShiftId, cur.ShiftId);
            Assert.IsNull(unseen);
            var sum = fake.End("k3", default).Result;
            Assert.AreEqual(started.ShiftId, sum.ShiftId);
            Assert.AreEqual("clock_out", sum.EndReason);
            Assert.IsNull(fake.Current(default).Result.Item1);
        }

        [Test]
        public void ClockIn_ParsesBackend21()
        {
            var st = ShiftState.Parse(JObject.Parse(@"{""shiftId"":""0198a2b4-0000-7000-8000-000000000002"",""startedAt"":""2026-10-25T10:00:00.000Z"",
                ""ratePerMinuteCents"":50,""capRemainingCents"":4500,""maxEndsAt"":""2026-10-25T11:00:00.000Z"",""accruedCents"":250,
                ""serverNow"":""2026-10-25T10:05:00.000Z"",""resumed"":true,""postId"":null}"));
            Assert.IsTrue(st.Active && st.Resumed);
            Assert.AreEqual(3600000, st.EndsAtMs - st.StartedAtMs);
            Assert.AreEqual(300.0, st.ElapsedSeconds(st.ReadAtRealtime), 0.001);
            Assert.AreEqual(250, st.AccruedNow(st.ReadAtRealtime));
            Assert.AreEqual(300, st.AccruedNow(st.ReadAtRealtime + 60f));       // one more minute at 50c
            var end = ShiftSummary.ParseEnd(JObject.Parse(@"{""shiftId"":""s"",""paidCents"":2100,""durationSec"":2530,""minutes"":42,""careerXp"":30,
                ""cappedCents"":0,""capRemainingCents"":2400,""uniformGranted"":true,""endReason"":""clock_out""}"));
            Assert.AreEqual(42, end.Minutes); Assert.AreEqual(2530, end.DurationSec); Assert.AreEqual(30, end.CareerXp); Assert.IsTrue(end.UniformGranted); Assert.IsFalse(end.WhileAway);
            var last = ShiftSummary.ParseLast(JObject.Parse(@"{""shiftId"":""s2"",""endedAt"":""2026-10-25T11:00:00Z"",""paidCents"":3000,""durationSec"":3600,""minutes"":60,""careerXp"":40,""endReason"":""auto""}"));
            Assert.IsTrue(last.WhileAway); Assert.AreEqual("auto", last.EndReason);
            Assert.IsNull(ShiftSummary.ParseLast(JValue.CreateNull()));
        }

        [Test]
        public void Promotion_FromRealtime()
        {
            var p = CareerPromotion.From(JObject.Parse(@"{""ladderId"":""trader"",""ladderName"":""Trader"",""fromRank"":1,""toRank"":3,""title"":""Swing Trader"",
                ""payBonusBps"":700,""venueAccess"":[""perp_pit:high_roller_floor""],""cosmeticIds"":[""reward_career_trader_mid""],""xp"":640,""at"":""2026-10-25T10:00:00Z""}"));
            Assert.AreEqual(3, p.ToRank);
            Assert.AreEqual(1, p.VenueAccess.Count);
            Assert.AreEqual("Swing Trader", p.Title);
        }
    
        [Test]
        public void ShiftUi_FormatsAndCapMath()
        {
            Assert.AreEqual("0:12:34", RektShiftFormat.Clock(754.9));
            Assert.AreEqual("4:59", RektShiftFormat.Short(298.2));
            Assert.AreEqual("7h 12m", RektShiftFormat.Span((7 * 3600 + 12 * 60) * 1000.0));
            Assert.AreEqual("$30", RektShiftFormat.Dollars(3000));
            // 4500 cap, 4260 left before this shift, 60 accrued: (240 + 60) / 4500
            Assert.AreEqual(300f / 4500f, RektShiftFormat.CapFraction(4500, 4260, 60), 1e-5);
            Assert.AreEqual(1f, RektShiftFormat.CapFraction(4500, 100, 500), 1e-5);
            Assert.IsTrue(RektShiftFormat.NearCap(0.86f, 2000, 50));
            Assert.IsTrue(RektShiftFormat.NearCap(0.5f, 700, 50));     // under 15 minutes of pay left
            Assert.IsFalse(RektShiftFormat.NearCap(0.5f, 2000, 50));
        }

        [Test]
        public void ShiftSummary_CapKnownOnlyFromEnd()
        {
            var end = ShiftSummary.ParseEnd(Newtonsoft.Json.Linq.JToken.Parse(@"{""shiftId"":""s"",""paidCents"":64,""durationSec"":77,""minutes"":1,""careerXp"":0,""cappedCents"":0,""capRemainingCents"":3628,""uniformGranted"":false,""endReason"":""clock_out""}"));
            Assert.IsTrue(end.CapKnown);
            Assert.AreEqual(3628, end.CapRemainingCents);
            var last = ShiftSummary.ParseLast(Newtonsoft.Json.Linq.JToken.Parse(@"{""shiftId"":""s"",""endedAt"":""2026-10-25T15:59:12.919Z"",""paidCents"":54,""durationSec"":65,""minutes"":1,""careerXp"":0,""endReason"":""clock_out""}"));
            Assert.IsFalse(last.CapKnown);
            Assert.IsTrue(last.WhileAway);
        }

        [Test]
        public void V2_SummaryLine_AndRankRow()
        {
            var sum = new ShiftSummary { PaidCents = 63, DurationSec = 70, Minutes = 1, EndReason = "clock_out" };
            StringAssert.Contains("left to earn today", ShiftBand.Subtitle(sum, 3442, "2h 6m"));
            StringAssert.Contains("Back in 2h 6m", ShiftBand.Subtitle(sum, 0, "2h 6m"));
            sum.DurationSec = 30;
            Assert.AreEqual(RbText.T("band.tiny"), ShiftBand.Subtitle(sum, 3442, null));
            var mine = new CTLife.Careers.Ladder { Id = "builder", Rank = 1, Xp = 12, Title = "Intern" };
            mine.Ranks.Add(new CTLife.Careers.LadderRank { Rank = 1, XpRequired = 0, Title = "Intern" });
            mine.Ranks.Add(new CTLife.Careers.LadderRank { Rank = 2, XpRequired = 50, Title = "Junior Dev" });
            var v = RektRank.Describe(mine, 2);
            Assert.AreEqual("Junior Dev", v.Title);
            Assert.AreEqual("12", v.Count);
            Assert.AreEqual("50", v.Of);
            Assert.AreEqual(10f / 50f, v.From, 1e-5);
            Assert.AreEqual(12f / 50f, v.To, 1e-5);
            StringAssert.Contains("38 XP", v.Divider);
        }

        [Test]
        public void JailName_ComesFromTheNamesTable()
        {
            var name = CTLife.Core.NameTable.Get("venue.ct_jail", "Jail");
            Assert.AreEqual(name, CTLife.CityLife.Heat.HeatText.T("jail.title"));
            StringAssert.Contains("of " + name + "?", CTLife.CityLife.Heat.HeatText.T("bail.title"));
            StringAssert.Contains(name, CTLife.CityLife.Heat.HeatText.T("jail.free"));
            StringAssert.DoesNotContain("Compliance", CTLife.CityLife.Heat.HeatText.T("jail.free"));
        }
    }
}
