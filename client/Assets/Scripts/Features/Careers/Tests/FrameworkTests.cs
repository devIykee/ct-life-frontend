using System;
using System.Linq;
using NUnit.Framework;

namespace CTLife.Careers.Tests
{
    public class RngAndSeedTests
    {
        [Test]
        public void Mulberry32MatchesReferenceVectors()
        {
            // Reference values from the canonical JS implementation (also recomputed in Python for the BE proposal).
            var r = new DetRng(1);
            Assert.AreEqual(2693262067u, r.NextUInt());
            Assert.AreEqual(11749833u, r.NextUInt());
            Assert.AreEqual(2265367787u, r.NextUInt());
            Assert.AreEqual(4213581821u, r.NextUInt());
            var d = new DetRng(unchecked((int)0xDEADBEEF));
            Assert.AreEqual(4043151706u, d.NextUInt());
            Assert.AreEqual(1147597007u, d.NextUInt());
        }

        [Test]
        public void Fnv1aMatchesReferenceVectors()
        {
            Assert.AreEqual(2166136261u, MiniGameSeed.Fnv1a(""));
            Assert.AreEqual(3826002220u, MiniGameSeed.Fnv1a("a"));
            Assert.AreEqual(74929860, MiniGameSeed.ForShift("shift_1", "discord_mod_ban", 0));
        }

        [Test]
        public void ShiftSeedsAreStableAndDistinctPerGameAndIndex()
        {
            int a = MiniGameSeed.ForShift("0192a1c2-aaaa", "auditor_report", 0);
            Assert.AreEqual(a, MiniGameSeed.ForShift("0192a1c2-aaaa", "auditor_report", 0));
            Assert.AreNotEqual(a, MiniGameSeed.ForShift("0192a1c2-aaaa", "auditor_report", 1));
            Assert.AreNotEqual(a, MiniGameSeed.ForShift("0192a1c2-aaaa", "auditor_spot_the_bug", 0));
            Assert.AreNotEqual(a, MiniGameSeed.ForShift("0192a1c2-bbbb", "auditor_report", 0));
            Assert.AreNotEqual(MiniGameSeed.ForPractice("x", 1), MiniGameSeed.ForPractice("x", 2));
        }

        [TestCase(1)]
        [TestCase(7)]
        [TestCase(1000)]
        public void NextStaysInRange(int max)
        {
            var r = new DetRng(42);
            for (int i = 0; i < 2000; i++)
            {
                int v = r.Next(max);
                Assert.That(v, Is.InRange(0, max - 1));
            }
        }

        [Test]
        public void DerangementIsNeverIdentity()
        {
            for (int seed = 0; seed < 300; seed++)
                for (int n = 2; n <= 7; n++)
                {
                    var p = new DetRng(seed).Derangement(n);
                    CollectionAssert.AreEquivalent(Enumerable.Range(0, n), p);
                    Assert.IsFalse(p.Select((v, i) => v == i).All(x => x), $"identity for seed {seed} n {n}");
                }
        }

        [Test]
        public void ShuffleIsDeterministic()
        {
            var a = Enumerable.Range(0, 20).ToList();
            var b = Enumerable.Range(0, 20).ToList();
            new DetRng(9).Shuffle(a);
            new DetRng(9).Shuffle(b);
            CollectionAssert.AreEqual(a, b);
            CollectionAssert.AreNotEqual(Enumerable.Range(0, 20), a);
        }
    }

    public class ScoringTests
    {
        [TestCase(0, 10, 100, 0)]
        [TestCase(10, 10, 100, 100)]
        [TestCase(15, 10, 100, 100)]
        [TestCase(-3, 10, 100, 0)]
        [TestCase(7, 12, 100, 58)]
        [TestCase(1, 8, 100, 13)]
        [TestCase(5, 10, 10, 5)]
        [TestCase(5, 0, 100, 0)]
        [TestCase(5, 10, 0, 0)]
        public void ScaleIsCappedAndRounded(double points, double max, int cap, int expected)
        {
            Assert.AreEqual(expected, Scoring.Scale(points, max, cap));
        }

        [Test]
        public void ScaleRejectsNaN()
        {
            Assert.AreEqual(0, Scoring.Scale(double.NaN, 10, 100));
            Assert.AreEqual(0, Scoring.Scale(5, double.NaN, 100));
        }

        [TestCase(10, 0, 2, 10)]
        [TestCase(5, 5, 2, 0)]
        [TestCase(6, 6, 4, 4)]
        [TestCase(0, 9, 3, 0)]
        public void ChanceCorrectedCancelsGuessing(int correct, int wrong, int options, double expected)
        {
            Assert.AreEqual(expected, Scoring.ChanceCorrected(correct, wrong, options), 1e-9);
        }

        [Test]
        public void ClampKeepsScoresInsideTheCap()
        {
            Assert.AreEqual(0, Scoring.Clamp(-1, 100));
            Assert.AreEqual(100, Scoring.Clamp(101, 100));
            Assert.AreEqual(0, Scoring.Clamp(5, 0));
        }
    }
}
