using CTLife.Core;
using NUnit.Framework;

namespace CTLife.CityLife.Tests
{
    /// <summary>The graphics tier's automatic choice (Core/Env/QualityTier).</summary>
    public sealed class QualityTierTests
    {
        [Test]
        public void EntryPhonesGetLowAndTheSeekerStaysHigh()
        {
            // Infinix X6517: Unisoc SC9863A, PowerVR GE8322, 4 GB (reports ~3862 MB)
            Assert.AreEqual(QualityTier.Tier.Low, QualityTier.Classify(3862, "PowerVR Rogue GE8322", true, out _));
            // Solana Seeker: Dimensity 7300, Mali-G615 MC2, 8 GB
            Assert.AreEqual(QualityTier.Tier.High, QualityTier.Classify(7600, "Mali-G615 MC2", true, out _));
            Assert.AreEqual(QualityTier.Tier.Low, QualityTier.Classify(6000, "Mali-G52 MC2", true, out _));
            Assert.AreEqual(QualityTier.Tier.Low, QualityTier.Classify(6000, "Adreno (TM) 506", true, out _));
            Assert.AreEqual(QualityTier.Tier.Low, QualityTier.Classify(6000, "Adreno (TM) 610", true, out _));
            Assert.AreEqual(QualityTier.Tier.High, QualityTier.Classify(8000, "Adreno (TM) 642L", true, out _));
            Assert.AreEqual(QualityTier.Tier.High, QualityTier.Classify(2000, "Apple M2", false, out _));
            Assert.IsFalse(QualityTier.WeakGpu(null));
        }

        [Test]
        public void ThePlayersSettingWins()
        {
            Assert.AreEqual(QualityTier.Tier.Low, QualityTier.Resolve(QualityTier.Pref.Auto, QualityTier.Tier.Low));
            Assert.AreEqual(QualityTier.Tier.High, QualityTier.Resolve(QualityTier.Pref.High, QualityTier.Tier.Low));
            Assert.AreEqual(QualityTier.Tier.Low, QualityTier.Resolve(QualityTier.Pref.Low, QualityTier.Tier.High));
        }
    }
}
