using System.Collections.Generic;
using NUnit.Framework;

namespace CTLife.Careers.Tests
{
    /// <summary>A controllable round: points are set by the test, completes on demand.</summary>
    internal sealed class FakeRound : RoundBase
    {
        public double PointsValue, PointsMaxValue = 10;
        public bool Complete;
        public int Advances;
        public FakeRound(int maxScore = 100, double duration = 30) : base(maxScore, duration) { }
        public override bool IsComplete => Complete;
        protected override double Points => PointsValue;
        protected override double PointsMax => PointsMaxValue;
        protected override void OnAdvance() => Advances++;
        public override IReadOnlyList<ResultLine> Summary => new[] { new ResultLine("x", 1) };
    }

    public class ActiveClockTests
    {
        [Test]
        public void CountsOnlyActiveTime()
        {
            var c = new ActiveClock(30);
            c.Start(100);
            c.Step(101);
            Assert.AreEqual(1, c.Elapsed, 1e-9);
            Assert.IsTrue(c.Pause(101.5));
            c.Step(110);
            Assert.AreEqual(1.5, c.Elapsed, 1e-9);
            Assert.AreEqual(8.5, c.PausedSeconds, 1e-9);
            Assert.IsTrue(c.Resume(111));
            c.Step(112);
            Assert.AreEqual(2.5, c.Elapsed, 1e-9);
        }

        [Test]
        public void HitchIsAnInterruptionNotPlayTime()
        {
            var c = new ActiveClock(30);
            c.Start(0);
            c.Step(1);
            Assert.IsTrue(c.Step(20)); // app suspended for 19 s without a focus event
            Assert.AreEqual(1, c.Elapsed, 1e-9);
            Assert.IsTrue(c.Paused);
            Assert.AreEqual(1, c.PauseCount);
        }

        [Test]
        public void BackwardsClockAddsNothing()
        {
            var c = new ActiveClock(30);
            c.Start(10);
            c.Step(11);
            c.Step(5);
            c.Step(11.5);
            Assert.AreEqual(1.5, c.Elapsed, 1e-9);
        }

        [Test]
        public void ElapsedNeverPassesDuration()
        {
            var c = new ActiveClock(2);
            c.Start(0);
            for (double t = 0.1; t < 5; t += 0.1) c.Step(t);
            Assert.AreEqual(2, c.Elapsed, 1e-9);
            Assert.IsTrue(c.Expired);
            Assert.IsFalse(c.Pause(5.1));
        }

        [Test]
        public void RejectsBadDurations()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ActiveClock(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ActiveClock(601));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ActiveClock(double.NaN));
        }
    }

    public class MiniGameSessionTests
    {
        [Test]
        public void BriefingThenPlayingThenTimeUp()
        {
            var r = new FakeRound(duration: 3) { PointsValue = 5 };
            var s = new MiniGameSession(r);
            Assert.AreEqual(MiniGamePhase.Briefing, s.Phase);
            Assert.IsFalse(s.CanInteract);
            s.Tick(0); // ticking in Briefing does nothing
            Assert.AreEqual(MiniGamePhase.Briefing, s.Phase);
            Assert.IsTrue(s.Begin(10));
            Assert.IsFalse(s.Begin(10));
            for (double t = 10.1; t < 14; t += 0.1) s.Tick(t);
            Assert.AreEqual(MiniGamePhase.Results, s.Phase);
            Assert.AreEqual("time", s.EndReason);
            Assert.AreEqual(50, s.FinalScore);
        }

        [Test]
        public void CompletingTheWorkEndsEarly()
        {
            var r = new FakeRound { PointsValue = 10 };
            var s = new MiniGameSession(r);
            s.Begin(0);
            s.Tick(0.5);
            r.Complete = true;
            s.AfterInput();
            Assert.AreEqual(MiniGamePhase.Results, s.Phase);
            Assert.AreEqual("complete", s.EndReason);
            Assert.AreEqual(100, s.FinalScore);
        }

        [Test]
        public void PauseFreezesTheRoundAndResumeContinues()
        {
            var r = new FakeRound(duration: 10);
            var s = new MiniGameSession(r);
            s.Begin(0);
            s.Tick(1);
            Assert.IsTrue(s.Pause(1.5, PauseReason.Player));
            Assert.AreEqual(MiniGamePhase.Paused, s.Phase);
            Assert.IsFalse(s.CanInteract);
            s.Tick(60);
            Assert.AreEqual(1.5, r.Elapsed, 1e-9);
            Assert.IsTrue(s.Resume(61));
            s.Tick(62);
            Assert.AreEqual(2.5, r.Elapsed, 1e-9);
        }

        [Test]
        public void InterruptionPausesWithReason()
        {
            var r = new FakeRound();
            var s = new MiniGameSession(r);
            s.Begin(0);
            s.Tick(0.1);
            s.Tick(30); // returned from the background
            Assert.AreEqual(MiniGamePhase.Paused, s.Phase);
            Assert.AreEqual(PauseReason.Interruption, s.LastPauseReason);
            Assert.AreEqual(0.1, r.Elapsed, 1e-9);
        }

        [Test]
        public void PausingTooLongEndsTheRoundKeepingTheScore()
        {
            var r = new FakeRound { PointsValue = 3 };
            var s = new MiniGameSession(r, maxPausedSeconds: 60);
            s.Begin(0);
            s.Tick(1);
            s.Pause(1, PauseReason.Player);
            s.Tick(30);
            Assert.AreEqual(MiniGamePhase.Paused, s.Phase);
            s.Tick(62);
            Assert.AreEqual(MiniGamePhase.Results, s.Phase);
            Assert.AreEqual("pauseLimit", s.EndReason);
            Assert.AreEqual(30, s.FinalScore);
        }

        [Test]
        public void EndRoundKeepsScoreAndCancelDiscardsIt()
        {
            var r = new FakeRound { PointsValue = 4 };
            var s = new MiniGameSession(r);
            s.Begin(0);
            s.Tick(1);
            Assert.IsTrue(s.EndRound(1.2));
            Assert.AreEqual("player", s.EndReason);
            Assert.AreEqual(40, s.FinalScore);
            Assert.IsFalse(s.Cancel());

            var s2 = new MiniGameSession(new FakeRound());
            Assert.IsTrue(s2.Cancel());
            Assert.AreEqual(MiniGamePhase.Cancelled, s2.Phase);
            Assert.IsFalse(s2.Begin(0));
            Assert.IsFalse(s2.EndRound(0));
        }

        [Test]
        public void FinalScoreNeverExceedsTheCap()
        {
            var r = new FakeRound(maxScore: 10) { PointsValue = 999 };
            var s = new MiniGameSession(r);
            s.Begin(0);
            s.EndRound(1);
            Assert.AreEqual(10, s.FinalScore);
        }

        [Test]
        public void PhaseChangedFiresOncePerTransition()
        {
            var s = new MiniGameSession(new FakeRound());
            var seen = new List<MiniGamePhase>();
            s.PhaseChanged += p => seen.Add(p);
            s.Begin(0);
            s.Pause(0.5, PauseReason.Player);
            s.Pause(0.6, PauseReason.Player);
            s.Resume(1);
            s.EndRound(1.5);
            CollectionAssert.AreEqual(new[] { MiniGamePhase.Playing, MiniGamePhase.Paused, MiniGamePhase.Playing, MiniGamePhase.Results }, seen);
        }
    }
}
