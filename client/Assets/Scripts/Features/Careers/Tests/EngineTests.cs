using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CTLife.Careers.Tests
{
    public class SortEngineTests
    {
        private static List<SortCard> Deck(int n, int bins) => Enumerable.Range(0, n).Select(i => new SortCard { Title = "c" + i, Bin = i % bins, Why = "w" }).ToList();

        [Test]
        public void PerfectPlayScoresTheCap()
        {
            var r = new SortRound(new[] { "A", "B" }, Deck(10, 2), 100, 45);
            while (!r.IsComplete) r.Answer(r.Current.Bin);
            Assert.AreEqual(100, r.Score);
            Assert.AreEqual(10, r.BestStreak);
            Assert.IsNull(r.Answer(0));
        }

        [Test]
        public void BlindGuessingScoresAboutZero()
        {
            var r = new SortRound(new[] { "A", "B" }, Deck(10, 2), 100, 45);
            while (!r.IsComplete) r.Answer(0); // half right, half wrong
            Assert.AreEqual(0, r.Score);
        }

        [Test]
        public void FourBinWrongCostsAThird()
        {
            var r = new SortRound(new[] { "A", "B", "C", "D" }, Deck(12, 4), 100, 45);
            for (int i = 0; i < 9; i++) r.Answer(r.Current.Bin);
            for (int i = 0; i < 3; i++) r.Answer((r.Current.Bin + 1) % 4);
            Assert.AreEqual(Scoring.Scale(9 - 1, 12, 100), r.Score);
        }

        [Test]
        public void NoInputAfterTimeAndUnansweredScoreNothing()
        {
            var r = new SortRound(new[] { "A", "B" }, Deck(10, 2), 100, 10);
            r.Answer(r.Current.Bin);
            r.Advance(10);
            Assert.IsNull(r.Answer(0));
            Assert.AreEqual(10, r.Score);
            Assert.AreEqual("9", r.Summary.First(l => l.LabelKey == "stat.unanswered").Value);
        }

        [Test]
        public void RejectsBadBinsAndDecks()
        {
            Assert.Throws<ArgumentException>(() => new SortRound(new[] { "A" }, Deck(2, 1), 100, 45));
            Assert.Throws<ArgumentException>(() => new SortRound(new[] { "A", "B" }, new List<SortCard>(), 100, 45));
            Assert.Throws<ArgumentException>(() => new SortRound(new[] { "A", "B" }, new[] { new SortCard { Bin = 5 } }, 100, 45));
            var r = new SortRound(new[] { "A", "B" }, Deck(4, 2), 100, 45);
            Assert.IsNull(r.Answer(-1));
            Assert.IsNull(r.Answer(2));
            Assert.AreEqual(0, r.Index);
        }

        [Test]
        public void AgentJudgeTracksTheBudget()
        {
            var judge = new AgentPolicyJudge(1000);
            var cards = new[]
            {
                new SortCard { Kind = "swap:ok", Amount = 600, Why = "ok" },
                new SortCard { Kind = "swap:ok", Amount = 600, Why = "ok" }, // allowed but now over budget
                new SortCard { Kind = "dm:no", Amount = 0, Why = "no DMs" },
                new SortCard { Kind = "post:ok", Amount = 0, Why = "ok" },
            };
            var r = new SortRound(new[] { "Approve", "Block" }, cards, 100, 45, judge);
            Assert.IsTrue(r.Answer(0).Value.Correct);
            Assert.AreEqual(400, judge.Left);
            var second = r.Answer(0).Value; // approving the over-budget spend is wrong and still spends
            Assert.IsFalse(second.Correct);
            Assert.AreEqual(1, second.CorrectBin);
            Assert.AreEqual(-200, judge.Left);
            StringAssert.Contains("overspent", r.Status);
            Assert.IsTrue(r.Answer(1).Value.Correct);
            Assert.IsTrue(r.Answer(0).Value.Correct);
        }
    }

    public class FlagEngineTests
    {
        private static FlagBoard Board(params bool[] flaws) => new FlagBoard { Title = "b", Lines = flaws.Select((f, i) => new FlagLine("l" + i, f, "w")).ToArray() };

        [Test]
        public void HitsMinusFalseFlagsNeverNegative()
        {
            var r = new FlagRound(new[] { Board(true, false, true, false, false), Board(false, true, false) }, 100, 50);
            r.Toggle(0); r.Toggle(2);
            var f = r.File().Value;
            Assert.AreEqual(2, f.Hits);
            Assert.AreEqual(2, f.Points);
            for (int i = 0; i < 3; i++) r.Toggle(i); // flag everything: 1 hit, 2 false
            Assert.AreEqual(0, r.File().Value.Points);
            Assert.IsTrue(r.IsComplete);
            Assert.AreEqual(Scoring.Scale(2, 3, 100), r.Score);
        }

        [Test]
        public void ToggleTwiceUnflagsAndFlagsClearBetweenBoards()
        {
            var r = new FlagRound(new[] { Board(true, false), Board(false, true) }, 100, 50);
            r.Toggle(1); r.Toggle(1);
            Assert.IsFalse(r.IsFlagged(1));
            r.Toggle(0);
            r.File();
            Assert.AreEqual(0, r.FlagCount);
            Assert.IsFalse(r.Toggle(5));
        }

        [Test]
        public void BoardsNeedAFlawAndACleanLine()
        {
            Assert.Throws<ArgumentException>(() => new FlagRound(new[] { Board(false, false) }, 100, 50));
            Assert.Throws<ArgumentException>(() => new FlagRound(new[] { Board(true, true) }, 100, 50));
        }
    }

    public class SequenceAndMatchEngineTests
    {
        [Test]
        public void SequenceScoresExactPositions()
        {
            var board = new SequenceBoard { Title = "t", Items = new[] { "a", "b", "c", "d" } };
            var r = new SequenceRound(new[] { board }, 3, 100, 50);
            Assert.AreNotEqual(new[] { 0, 1, 2, 3 }, r.Shown.ToArray());
            Assert.IsNull(r.File()); // not all placed
            r.Place(0); r.Place(1); r.Place(3); r.Place(2);
            Assert.IsFalse(r.Place(2));
            var f = r.File().Value;
            Assert.AreEqual(2, f.InPlace);
            Assert.AreEqual(50, r.Score);
        }

        [Test]
        public void SequenceUnplaceShiftsLaterItems()
        {
            var r = new SequenceRound(new[] { new SequenceBoard { Items = new[] { "a", "b", "c" } } }, 1, 100, 50);
            r.Place(2); r.Place(0); r.Place(1);
            Assert.IsTrue(r.Unplace(0));
            CollectionAssert.AreEqual(new[] { 0, 1 }, r.Placed);
            r.Place(2);
            Assert.IsTrue(r.File().Value.Perfect);
            Assert.AreEqual(100, r.Score);
        }

        [Test]
        public void MatchScoresPairsAndRepairingMovesACard()
        {
            var board = new MatchBoard { Left = new[] { "L0", "L1", "L2" }, Right = new[] { "R0", "R1", "R2" }, Solution = new[] { 2, 0, 1 } };
            var r = new MatchRound(new[] { board }, 5, 100, 50);
            Assert.IsFalse(Enumerable.Range(0, 3).All(i => board.Solution[i] == r.RightShown[i]), "column starts solved");
            r.SelectLeft(0); r.PairWith(0);   // wrong
            r.SelectLeft(1); r.PairWith(0);   // steals R0 from L0
            Assert.AreEqual(-1, r.PairOf(0));
            Assert.AreEqual(0, r.PairOf(1));
            Assert.IsNull(r.File());
            r.SelectLeft(0); r.PairWith(2);
            r.SelectLeft(2); r.PairWith(1);
            Assert.AreEqual(3, r.File().Value.Correct);
            Assert.AreEqual(100, r.Score);
        }

        [Test]
        public void MatchRejectsNonPermutationSolutions()
        {
            Assert.Throws<ArgumentException>(() => new MatchRound(new[] { new MatchBoard { Left = new[] { "a", "b" }, Right = new[] { "c", "d" }, Solution = new[] { 0, 0 } } }, 1, 100, 50));
        }

        [Test]
        public void UnpairReselectsThatCard()
        {
            var r = new MatchRound(new[] { new MatchBoard { Left = new[] { "a", "b" }, Right = new[] { "c", "d" }, Solution = new[] { 0, 1 } } }, 1, 100, 50);
            r.SelectLeft(0); r.PairWith(1);
            Assert.IsTrue(r.Unpair(0));
            Assert.AreEqual(0, r.SelectedLeft);
            Assert.IsFalse(r.Unpair(1));
        }
    }

    public class PickAndComboEngineTests
    {
        [Test]
        public void PickCountsCorrectBoards()
        {
            var boards = Enumerable.Range(0, 4).Select(i => new PickBoard { Options = new[] { new PickOption(), new PickOption(), new PickOption() }, Correct = i % 3 }).ToList();
            var r = new PickRound(boards, 100, 45);
            r.Pick(0); r.Pick(1); r.Pick(0); r.Pick(0);
            Assert.AreEqual(Scoring.Scale(3, 4, 100), r.Score);
            Assert.IsTrue(r.IsComplete);
            Assert.IsNull(r.Pick(0));
        }

        [Test]
        public void ComboPassesOnlyInsideBandWithoutForbiddenOptions()
        {
            var board = new ComboBoard
            {
                Min = 10, Max = 12,
                Slots = new[]
                {
                    new ComboSlot { Options = new[] { new ComboOption("a", 5), new ComboOption("cut", 0, true, "must keep") } },
                    new ComboSlot { Options = new[] { new ComboOption("b", 6), new ComboOption("cut", 0) } },
                    new ComboSlot { Options = new[] { new ComboOption("c", 3), new ComboOption("cut", 0) } },
                },
            };
            Assert.AreEqual(1, board.CountSolutions()); // 5 + 6 + 0 = 11
            var r = new ComboRound(new[] { board, board, board }, 100, 50);
            Assert.IsNull(r.File());
            r.Choose(0, 0); r.Choose(1, 0); r.Choose(2, 1);
            Assert.IsTrue(r.File().Value.Passed);
            r.Choose(0, 1); r.Choose(1, 0); r.Choose(2, 0);
            var forbidden = r.File().Value;
            Assert.IsFalse(forbidden.Passed);
            Assert.AreEqual("must keep", forbidden.FailWhy);
            r.Choose(0, 0); r.Choose(1, 0); r.Choose(2, 0); // 14, over the band
            Assert.IsFalse(r.File().Value.Passed);
            Assert.AreEqual(Scoring.Scale(1, 3, 100), r.Score);
        }
    }

    public class TimingEngineTests
    {
        private static TimingWindow Window(double[] payoff) => new TimingWindow { Step = 0.1, Signal = payoff.ToArray(), Payoff = payoff };

        private static double[] Ramp(int n, double peakAt, double peak) => Enumerable.Range(0, n).Select(i => peak - Math.Abs(i - peakAt)).ToArray();

        [Test]
        public void ActingAtTheBestMomentScoresFull()
        {
            var r = new TimingRound(new[] { Window(Ramp(30, 20, 10)) }, 100, 10);
            r.Advance(2.0);
            Assert.AreEqual(20, r.Sample);
            var res = r.Act().Value;
            Assert.AreEqual(1, res.Points, 1e-9);
            Assert.IsNull(r.Act());
            Assert.AreEqual(100, r.Score);
        }

        [Test]
        public void ActingEarlyScoresPartAndLossesScoreZero()
        {
            var r = new TimingRound(new[] { Window(Ramp(30, 20, 10)), Window(Ramp(30, 20, 10)) }, 100, 10);
            r.Advance(1.5); // sample 15: payoff 5 of 10
            Assert.AreEqual(0.5, r.Act().Value.Points, 1e-9);
            r.Advance(1.5 + TimingRound.FeedbackSeconds); // next window starts
            Assert.AreEqual(1, r.WindowIndex);
            // sample 0 of window 2 has payoff -10: a loss
            Assert.AreEqual(0, r.Act().Value.Points, 1e-9);
            Assert.AreEqual(25, r.Score);
        }

        [Test]
        public void PassingANoEdgeWindowScoresFull()
        {
            var r = new TimingRound(new[] { Window(Enumerable.Repeat(-1.0, 20).ToArray()) }, 100, 5);
            r.Advance(2.0);
            Assert.IsTrue(r.IsComplete);
            Assert.AreEqual(100, r.Score);
            Assert.IsFalse(r.Results[0].Acted);
        }

        [Test]
        public void MissingAProfitableWindowScoresZeroAndMovesOn()
        {
            var r = new TimingRound(new[] { Window(Ramp(20, 10, 5)), Window(Ramp(20, 10, 5)) }, 100, 10);
            r.Advance(2.05);
            Assert.AreEqual(1, r.WindowIndex);
            Assert.AreEqual(0, r.Results[0].Points);
            r.Advance(2.05 + 1.0);
            Assert.AreEqual(1, r.Act().Value.Points, 1e-9);
        }

        [Test]
        public void WindowsMustFitTheDuration()
        {
            Assert.Throws<ArgumentException>(() => new TimingRound(new[] { Window(Ramp(100, 50, 1)) }, 100, 5));
        }
    }

    public class BalanceEngineTests
    {
        private static BalanceSpec Spec() => new BalanceSpec
        {
            Min = -100, Max = 100, Start = 0, BandLo = -10, BandHi = 10, Drift = Enumerable.Repeat(20.0, 40).ToArray(),
            Actions = new[] { new BalanceAction { Label = "down", Delta = -10, Cooldown = 0.5 }, new BalanceAction { Label = "hedge", PullToCentre = 1, Cooldown = 5, Cost = 1 } },
        };

        [Test]
        public void ResultsDoNotDependOnFrameRate()
        {
            double Run(double frame)
            {
                var r = new BalanceRound(Spec(), 100, 10);
                double t = 0;
                bool hedged = false;
                while (t < 10 - 1e-9)
                {
                    t = Math.Min(10, t + frame);
                    r.Advance(t);
                    if (!hedged && t >= 3) { r.Apply(1); hedged = true; }
                }
                return r.InBandSeconds;
            }
            Assert.AreEqual(Run(0.1), Run(0.016), 0.06); // one 20 Hz step of input-timing quantisation at most
        }

        [Test]
        public void CooldownsAndCostsApply()
        {
            var r = new BalanceRound(Spec(), 100, 10);
            r.Advance(1); // drift +20/s → 20, out of band
            Assert.AreEqual(BalanceOutcome.Applied, r.Apply(0));
            Assert.IsNull(r.Apply(0)); // cooling down
            Assert.AreEqual(10, r.Value, 1e-6);
            Assert.AreEqual(BalanceOutcome.Applied, r.Apply(1));
            Assert.AreEqual(0, r.Value, 1e-6);
            Assert.Greater(r.CooldownLeft(1), 4.9);
        }

        [Test]
        public void CuesAnswerMissAndMisfire()
        {
            var spec = new BalanceSpec
            {
                Min = 0, Max = 100, Start = 50, BandLo = 40, BandHi = 100, Drift = new[] { 0.0 }, MisfirePenalty = -5,
                Cues = new[]
                {
                    new BalanceCue { At = 1, Window = 2, Kind = "mute", Reward = 10, Penalty = -20 },
                    new BalanceCue { At = 4, Window = 1, Kind = "topic", Reward = 10, Penalty = -20 },
                },
                Actions = new[] { new BalanceAction { Label = "Mute", Kind = "mute" }, new BalanceAction { Label = "Topic", Kind = "topic" } },
            };
            var r = new BalanceRound(spec, 100, 10);
            r.Advance(1.5);
            Assert.AreEqual("mute", r.ActiveCue.Kind);
            Assert.AreEqual(BalanceOutcome.Misfire, r.Apply(1));
            Assert.AreEqual(45, r.Value, 1e-6);
            Assert.AreEqual(BalanceOutcome.Answered, r.Apply(0));
            Assert.AreEqual(55, r.Value, 1e-6);
            r.Advance(5.5); // the topic cue expires unanswered
            Assert.AreEqual(1, r.Missed);
            Assert.AreEqual(35, r.Value, 1e-6);
            Assert.IsFalse(r.InBand);
        }
    }
}
