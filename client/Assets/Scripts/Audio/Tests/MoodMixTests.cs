using NUnit.Framework;

namespace CTLife.Audio.Tests
{
    public class MoodMixTests
    {
        [TestCase(1f, 1f, 1f, 1f, 1f, 0f)]
        [TestCase(0.5f, 1f, 1f, 1f, 1f, 0f)]
        [TestCase(0f, 1f, 1f, 0f, 0f, 0f)]
        [TestCase(-0.5f, 1f, 1f, 0f, 0f, 1f)]
        [TestCase(-1f, 1f, 1f, 0f, 0f, 1f)]
        public void StemGainsFollowTheSpecTable(float mood, float pad, float bass, float drums, float lead, float tension)
        {
            var l = MoodMix.Evaluate(mood, fullMixMode: false);
            Assert.AreEqual(pad, l.Pad, 1e-4);
            Assert.AreEqual(bass, l.Bass, 1e-4);
            Assert.AreEqual(drums, l.Drums, 1e-4);
            Assert.AreEqual(lead, l.Lead, 1e-4);
            Assert.AreEqual(tension, l.Tension, 1e-4);
            Assert.AreEqual(0f, l.FullMix);
        }

        [Test]
        public void GreenBringsInDrumsBeforeLead()
        {
            var a = MoodMix.Evaluate(0.25f, false);
            Assert.That(a.Drums, Is.GreaterThan(0.2f).And.LessThan(0.9f), "drums fade in between 0 and +0.5");
            Assert.AreEqual(0f, a.Lead, 1e-4, "lead stays out below +0.35");
            var b = MoodMix.Evaluate(0.425f, false);
            Assert.That(b.Lead, Is.GreaterThan(0.3f).And.LessThan(0.8f), "lead fades in just below +0.5");
            Assert.AreEqual(1f, MoodMix.Evaluate(0.5f, false).Lead, 1e-4, "README: lead at 0 dB from +0.5 up");
        }

        [Test]
        public void RedLowPassesAlongTheReadmeCurve()
        {
            Assert.AreEqual(MoodMix.OpenCutoffHz, MoodMix.CutoffHz(0f), 1e-3);
            Assert.AreEqual(MoodMix.OpenCutoffHz, MoodMix.CutoffHz(0.7f), 1e-3);
            Assert.AreEqual(2500f, MoodMix.CutoffHz(-0.5f), 1f);
            Assert.AreEqual(900f, MoodMix.CutoffHz(-1f), 1f);
            float prev = float.MaxValue;
            for (float m = 0f; m >= -1f; m -= 0.05f)
            {
                float c = MoodMix.CutoffHz(m);
                Assert.That(c, Is.LessThanOrEqualTo(prev + 1e-3f), "cutoff falls monotonically as the market reddens");
                prev = c;
            }
            Assert.IsFalse(MoodMix.Evaluate(0.2f, false).FilterActive);
            Assert.IsTrue(MoodMix.Evaluate(-0.2f, false).FilterActive);
        }

        [Test]
        public void GainsAreMonotonicAcrossTheRange()
        {
            float d = -1f, l = -1f, t = 2f;
            for (float m = -1f; m <= 1.0001f; m += 0.02f)
            {
                var x = MoodMix.Evaluate(m, false);
                Assert.That(x.Drums, Is.GreaterThanOrEqualTo(d - 1e-5f));
                Assert.That(x.Lead, Is.GreaterThanOrEqualTo(l - 1e-5f));
                Assert.That(x.Tension, Is.LessThanOrEqualTo(t + 1e-5f));
                d = x.Drums; l = x.Lead; t = x.Tension;
            }
        }

        [Test]
        public void FullMixKeepsTheArrangementAndOnlyAddsRedLayers()
        {
            var green = MoodMix.Evaluate(1f, true);
            Assert.AreEqual(1f, green.FullMix);
            Assert.AreEqual(0f, green.Drums + green.Lead + green.Pad + green.Bass, 1e-6, "no stems in full-mix mode");
            Assert.AreEqual(0f, green.Tension);
            Assert.IsFalse(green.FilterActive);
            var red = MoodMix.Evaluate(-1f, true);
            Assert.AreEqual(1f, red.FullMix);
            Assert.AreEqual(1f, red.Tension, 1e-4);
            Assert.AreEqual(900f, red.CutoffHz, 1f);
        }

        [TestCase(500, 1f)]
        [TestCase(-500, -1f)]
        [TestCase(250, 0.5f)]
        [TestCase(10000, 1f)]
        [TestCase(-10000, -1f)]
        [TestCase(0, 0f)]
        public void BpsSaturatesAtFivePercent(int bps, float mood) => Assert.AreEqual(mood, MoodMix.FromBps(bps), 1e-5);

        [Test]
        public void NaNMoodIsNeutral()
        {
            var x = MoodMix.Evaluate(float.NaN, false);
            Assert.AreEqual(MoodMix.Evaluate(0f, false).Drums, x.Drums);
            Assert.AreEqual(MoodMix.OpenCutoffHz, x.CutoffHz);
        }

        [Test]
        public void BarClockFindsBarLines()
        {
            // 120 BPM: 2 s per 4/4 bar.
            Assert.AreEqual(2.0, BarClock.BarSeconds(120), 1e-9);
            Assert.AreEqual(-1, BarClock.BarIndex(10, 120, 9.9));
            Assert.AreEqual(0, BarClock.BarIndex(10, 120, 10.0));
            Assert.AreEqual(2, BarClock.BarIndex(10, 120, 14.5));
            Assert.AreEqual(16.0, BarClock.NextBarDsp(10, 120, 14.5), 1e-9);
            Assert.AreEqual(12.0, BarClock.NextBarDsp(10, 120, 10.0), 1e-9, "strictly after now");
            Assert.AreEqual(10.0, BarClock.NextBarDsp(10, 120, 3.0), 1e-9);
            Assert.That(BarClock.BarSeconds(0), Is.GreaterThan(0), "invalid bpm still yields a bar");
        }

        [Test]
        public void ControllerSnapsOnStartThenLatchesOnBarLines()
        {
            var c = new MoodController();
            c.SetMood(1f);
            c.Restart(false);
            var first = c.Tick(nowDsp: 0.0, setStartDsp: 0.0, bpm: 120, deltaSeconds: 0.016f);
            Assert.AreEqual(1f, first.Drums, 1e-4, "a new set starts in the current mood, no fade");
            Assert.AreEqual(1f, first.Lead, 1e-4);

            c.SetMood(-1f);                              // market crashes mid-bar
            var mid = c.Tick(0.5, 0.0, 120, 0.5f);       // still bar 0
            Assert.AreEqual(1f, c.AppliedMood, "mood switches on the bar line, not mid-bar");
            Assert.AreEqual(1f, mid.Drums, 1e-4);

            var bar1 = c.Tick(2.01, 0.0, 120, 0.016f);   // bar 1 starts: latch
            Assert.AreEqual(-1f, c.AppliedMood);
            Assert.That(bar1.Drums, Is.LessThan(1f), "fade has begun");
            Assert.That(bar1.Drums, Is.GreaterThan(0.9f), "and is gradual");

            // Two bars at 120 BPM = 4 s fade.
            Assert.AreEqual(4f, MoodController.FadeSeconds(120), 1e-4);
            MoodLayers x = bar1;
            for (int i = 0; i < 300; i++) x = c.Tick(2.01 + (i + 1) * 0.016, 0.0, 120, 0.016f);
            Assert.AreEqual(0f, x.Drums, 1e-3);
            Assert.AreEqual(0f, x.Lead, 1e-3);
            Assert.AreEqual(1f, x.Tension, 1e-3);
            Assert.AreEqual(900f, x.CutoffHz, 5f);
        }

        [Test]
        public void FadeSecondsAreClamped()
        {
            Assert.AreEqual(2f, MoodController.FadeSeconds(400), 1e-4);
            Assert.AreEqual(8f, MoodController.FadeSeconds(30), 1e-4);
            Assert.AreEqual(4.286f, MoodController.FadeSeconds(112), 1e-3, "club: two bars at 112 BPM");
        }

        [Test]
        public void ControllerIgnoresBadDeltaTimes()
        {
            var c = new MoodController();
            c.SetMood(0.8f);
            c.Restart(false);
            var a = c.Tick(0, 0, 100, 0.016f);
            var b = c.Tick(0.1, 0, 100, float.NaN);
            var d = c.Tick(0.2, 0, 100, -1f);
            Assert.AreEqual(a.Drums, b.Drums);
            Assert.AreEqual(a.Drums, d.Drums);
        }
    }
}
