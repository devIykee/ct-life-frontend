using NUnit.Framework;
using UnityEngine;

namespace CTLife.Economy.Tests
{
    /// <summary>The Factory UI's pure parts: the rug meter, the liquidity slider, the desk shot, the rug-warning rule.</summary>
    public sealed class FactoryUiTests
    {
        [Test]
        public void RugRange_FromZeroIsTwoToThreeStars()
        {
            var (lo, hi, sLo, sHi) = FactoryLaunchSheet.RugRange(0);
            Assert.AreEqual(35, lo); Assert.AreEqual(50, hi);
            Assert.AreEqual(2, sLo); Assert.AreEqual(3, sHi);   // the brief: "a rug from zero heat = 2 to 3 stars"
            var hot = FactoryLaunchSheet.RugRange(80);
            Assert.AreEqual(20, hot.lo, "capped at 100");
            Assert.AreEqual(20, hot.hi);
            Assert.AreEqual(5, hot.starsHi);
        }

        [TestCase(0, 0)] [TestCase(9, 0)] [TestCase(10, 1)] [TestCase(20, 1)] [TestCase(21, 2)] [TestCase(50, 3)] [TestCase(100, 5)]
        public void Stars_MatchHeatRules(int heat, int stars) => Assert.AreEqual(stars, FactoryLaunchSheet.Stars(heat));

        [TestCase(0, 0)] [TestCase(9, 0)] [TestCase(10, 1)] [TestCase(35, 2)] [TestCase(69, 2)] [TestCase(70, 3)]
        public void Levels(int heat, int idx) => Assert.AreEqual(idx, FactoryLaunchSheet.LevelIndex(heat));

        [Test]
        public void LiquiditySlider_LogScaleSnapsAndRoundTrips()
        {
            var c = FactoryConfig.Defaults;
            Assert.AreEqual(1_000, FactoryLaunchSheet.TToLiquidity(0f, c));
            Assert.AreEqual(100_000, FactoryLaunchSheet.TToLiquidity(1f, c));
            Assert.AreEqual(10_000, FactoryLaunchSheet.TToLiquidity(0.5f, c));   // $100 in the middle of $10..$1,000
            foreach (var chip in FactoryLaunchSheet.LiquidityChips)
                Assert.AreEqual(chip, FactoryLaunchSheet.TToLiquidity(FactoryLaunchSheet.LiquidityToT(chip, c), c));
            long v = FactoryLaunchSheet.TToLiquidity(0.83f, c);
            Assert.AreEqual(0, v % 500, "snapped");
        }

        [Test]
        public void Dollars_WholeAmountsHaveNoCents()
        {
            Assert.AreEqual("$250", FactoryLaunchSheet.Dollars(25_000));
            Assert.AreEqual("$1,005", FactoryLaunchSheet.Dollars(100_500));
        }

        [Test]
        public void DeskShot_LooksAtTheDeskFromTheRoom()
        {
            var desk = new Vector3(-299.6f, -4f, -252.7f);
            var toward = new Vector3(-1f, 0f, 0f);
            var (pos, rot) = FactoryDeskStage.Shot(desk, toward);
            Assert.Greater(Vector3.Dot(pos - desk, toward), 2.5f, "on the room side");
            Assert.Greater(pos.y, desk.y + 1.5f);
            var toDesk = (desk + Vector3.up - pos).normalized;
            Assert.Greater(Vector3.Dot(rot * Vector3.forward, toDesk), 0.85f, "the desk is in frame");
            // the desk sits left of centre (the sheet covers the right)
            var local = Quaternion.Inverse(rot) * (desk + Vector3.up - pos);
            Assert.Less(local.x, 0f);
        }

        [Test]
        public void RugWarning_OnlyMyCoinAndBigSells()
        {
            Assert.IsTrue(FactoryLaunchFlow.NeedsRugWarning(true, 30));
            Assert.IsTrue(FactoryLaunchFlow.NeedsRugWarning(true, 100));
            Assert.IsFalse(FactoryLaunchFlow.NeedsRugWarning(true, 25));
            Assert.IsFalse(FactoryLaunchFlow.NeedsRugWarning(false, 100));
        }
    }
}

namespace CTLife.Economy.Tests
{
    using System.Linq;
    using Newtonsoft.Json.Linq;
    using UnityEngine.UIElements;

    /// <summary>Security review 2026-10-26: player text (alias, coin name, ticker) never renders as markup.</summary>
    public sealed class FactoryMarkupTests
    {
        [Test]
        public void Markup_InAliasIsDroppedAtParse()
        {
            var json = JObject.Parse(FactoryFixtures.Launch);
            json["creator"]["alias"] = "<size=99><color=#ff0000>admin</color>";
            var c = FactoryParse.Coin(json);
            Assert.IsFalse(c.CreatorAlias.Contains("<") || c.CreatorAlias.Contains(">"));
            StringAssert.Contains("admin", c.CreatorAlias);
        }

        [Test]
        public void Markup_InPlayerTextRendersLiterally()
        {
            var coin = new MarketCoin { CoinId = "c1", Ticker = "<b>X</b>", Name = "<color=red>Frog</color>", Stage = "new", PriceMicros = 1000, PlayerLaunched = true, TemplateId = "rf_frog" };
            var head = FactoryPulse.PageHeader(coin);
            var labels = head.Query<Label>().ToList();
            Assert.Greater(labels.Count, 0);
            foreach (var l in labels) Assert.IsFalse(l.enableRichText, "label '" + l.text + "' must render markup literally");
            Assert.IsTrue(labels.Any(l => l.text == "<color=red>Frog</color>"), "shown as typed, not as red text");
        }
    }
}
