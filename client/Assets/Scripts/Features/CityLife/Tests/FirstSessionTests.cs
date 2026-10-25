using System.Collections.Generic;
using NUnit.Framework;

namespace CTLife.CityLife.Tests
{
    /// <summary>Queue 17 + 18 plumbing: the first-session state machine and the dream ladder model.</summary>
    public sealed class FirstSessionTests
    {
        [Test]
        public void BeatsCompleteInAnyOrderAndNextIsTheFirstUnfinished()
        {
            var s = new FirstSessionState();
            Assert.IsFalse(s.Active());
            s.Mark(FirstBeat.Origin, 1000);
            s.Mark(FirstBeat.Character, 1100);
            s.Mark(FirstBeat.Home, 1200);
            Assert.AreEqual(FirstBeat.Moved, s.Next());
            Assert.IsTrue(s.Mark(FirstBeat.Trade, 1300), "a trade before the shift still counts");
            Assert.IsFalse(s.Mark(FirstBeat.Trade, 1301), "only once");
            s.Mark(FirstBeat.Moved, 1400);
            Assert.AreEqual(FirstBeat.Shift, s.Next());
            Assert.AreEqual((1, 3), s.Progress(FirstSessionCopy.Starter));
            s.Mark(FirstBeat.Shift, 1500); s.Mark(FirstBeat.Quest, 1600);
            Assert.IsFalse(s.Completed);
            s.Mark(FirstBeat.Chest, 1700);
            Assert.IsTrue(s.Completed);
            Assert.AreEqual(1700, s.CompletedAtMs);
            Assert.IsFalse(s.Active());
        }

        [Test]
        public void UnavailableBeatsNeverBlockAndSkipReplayPersist()
        {
            var s = new FirstSessionState();
            System.Func<FirstBeat, bool> noMarkets = b => b != FirstBeat.Trade;
            foreach (var b in new[] { FirstBeat.Origin, FirstBeat.Character, FirstBeat.Home, FirstBeat.Moved, FirstBeat.Shift }) s.Mark(b, 10, noMarkets);
            Assert.AreEqual(FirstBeat.Quest, s.Next(noMarkets), "the first trade is passed over while markets are off");
            Assert.AreEqual((1, 2), s.Progress(FirstSessionCopy.Starter, noMarkets));
            s.Skip();
            Assert.IsFalse(s.Active(noMarkets));
            var back = FirstSessionState.Parse(s.Serialize());
            Assert.IsTrue(back.Skipped);
            Assert.IsTrue(back.IsDone(FirstBeat.Shift));
            Assert.AreEqual(10, back.StartedAtMs);
            back.Replay(99);
            Assert.IsFalse(back.Skipped);
            Assert.IsTrue(back.IsDone(FirstBeat.Home), "replay keeps the screens already done");
            Assert.AreEqual(FirstBeat.Moved, back.Next());
            Assert.IsTrue(FirstSessionState.Parse("garbage").Next() == FirstBeat.Origin);
            Assert.IsTrue(FirstSessionState.ForReturning(5).Completed);
            Assert.AreEqual("first.shift", FirstSessionCopy.ObjectiveKey(FirstBeat.Shift));
            Assert.IsNull(FirstSessionCopy.ObjectiveKey(FirstBeat.Origin));
        }

        [Test]
        public void LadderGoalsAreWeeksOfTheNextRungsRent()
        {
            var rent = new Dictionary<string, long> { ["parents_basement"] = 2000, ["hacker_house"] = 6000, ["studio"] = 18000, ["flat"] = 18000, ["penthouse"] = 150000, ["marina_villa"] = 500000 };
            var basement = DreamLadder.Compute("parents_basement", false, 25200, false, rent);
            Assert.AreEqual(LadderRung.Basement, basement.Current);
            Assert.AreEqual(LadderRung.Studio, basement.Next);
            Assert.AreEqual(72000, basement.GoalCents, "4 weeks of the studio's $180");
            Assert.AreEqual(0.35f, basement.Fraction, 0.001f);
            Assert.IsFalse(basement.CanReachNext);
            var flat = DreamLadder.Compute("studio", true, 700000, false, rent);
            Assert.AreEqual(LadderRung.Flat, flat.Current);
            Assert.AreEqual(600000, flat.GoalCents);
            Assert.IsTrue(flat.CanReachNext);
            var pent = DreamLadder.Compute("penthouse", false, 1, false, rent);
            Assert.IsTrue(pent.NextIsEntitlement, "the hovercar is never Cash");
            Assert.AreEqual(0, pent.GoalCents);
            var top = DreamLadder.Compute("parents_basement", false, 0, true, rent);
            Assert.AreEqual(LadderRung.Hovercar, top.Current);
            Assert.IsNull(top.Next);
            Assert.AreEqual(1f, top.Fraction);
            Assert.AreEqual(LadderRung.Basement, DreamLadder.Compute(null, false, -500, false, rent).Current, "homeless: the bottom rung, debt is not progress");
            Assert.AreEqual(0, DreamLadder.Compute(null, false, -500, false, rent).ProgressCents);
        }
    }
}
