using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Economy.Tests
{
    /// <summary>Real local backend #14 replies (origin/main 40019ce, 2026-10-25), trimmed.</summary>
    internal static class FactoryFixtures
    {
        internal const string Config = @"{""launchFeeCents"":500,""minLiquidityCents"":1000,""maxLiquidityCents"":100000,""maxDevAllocationBps"":5000,""dailyLaunchCap"":3,""cooldownSec"":600,""name"":{""min"":2,""max"":24},""ticker"":{""min"":2,""max"":6},""templates"":[""rf_frog"",""rf_dog"",""rf_cat"",""rf_rocket"",""rf_robot"",""rf_skull"",""rf_diamond"",""rf_banana"",""rf_ghost"",""rf_chad"",""rf_clown"",""rf_crown""],""arcOddsPct"":{""rug"":25,""slow_bleed"":40,""sideways"":20,""moon"":15},""moonMultipleBps"":30000,""creatorFeeShareBps"":5000,""rug"":{""sellBps"":3000,""windowMin"":60}}";
        internal const string MeCooldown = @"{""heat"":0,""heatLevel"":""cool"",""launchesToday"":1,""dailyLaunchCap"":3,""nextLaunchAt"":""2026-10-25T22:22:01.621Z""}";
        internal const string MeCapped = @"{""heat"":0,""heatLevel"":""cool"",""launchesToday"":3,""dailyLaunchCap"":3,""nextLaunchAt"":""2026-10-26T21:42:15.673Z""}";
        internal const string MeHeat = @"{""heat"":44,""heatLevel"":""hot"",""launchesToday"":1,""dailyLaunchCap"":3,""nextLaunchAt"":""2026-10-25T22:22:45.794Z""}";
        internal const string Launch = @"{""coinId"":""01a1751f-9262-78cd-ae09-8df5ddc5eb25"",""ticker"":""RUGME"",""name"":""Rug Me Daddy"",""templateId"":""rf_skull"",""stage"":""new"",""priceMicros"":""1000"",""mcapCents"":""1000000"",""vol5mCents"":""0"",""change5mBps"":0,""holders"":1,""launchedAt"":""2026-10-25T22:12:45.794Z"",""liquidityCents"":""5000"",""devAllocationBps"":5000,""creator"":{""userId"":""01a1751e-4395-7f39-aabe-ae41cf57e656"",""alias"":""lanec_rugger""},""isMine"":true,""mooned"":false,""rugFlagged"":false,""creatorFeesCents"":""0""}";
        internal const string Detail = @"{""coinId"":""01a1751e-e5d6-77ba-af6a-03a0633fbdfe"",""ticker"":""FLRD"",""name"":""Frog Lord"",""templateId"":""rf_frog"",""stage"":""new"",""priceMicros"":""969"",""mcapCents"":""969000"",""vol5mCents"":""0"",""change5mBps"":-310,""holders"":2,""launchedAt"":""2026-10-25T22:12:01.621Z"",""liquidityCents"":""5000"",""devAllocationBps"":3000,""creator"":{""userId"":""01a1751e-4395-7f39-aabe-ae41cf57e656"",""alias"":""lanec_rugger""},""isMine"":false,""mooned"":false,""rugFlagged"":false,""creatorFeesCents"":null,""candles1m"":[{""at"":1792966322818,""priceMicros"":""1000""},{""at"":1792966324483,""priceMicros"":""997""},{""at"":1792966326486,""priceMicros"":""996""}],""topHolders"":[{""alias"":""lanec_sniper"",""units"":""204199""},{""alias"":""lanec_rugger"",""units"":""15000""}],""riskHint"":{""level"":""hidden"",""reason"":""Reach security 5 to unlock the risk read-out.""}}";
        internal const string List = @"{""coins"":[{""coinId"":""01a1751f-1d31-790b-aef7-19fcc40a8304"",""ticker"":""RBPM"",""name"":""Robo Pump"",""templateId"":""rf_robot"",""stage"":""new"",""priceMicros"":""984"",""mcapCents"":""984000"",""vol5mCents"":""0"",""change5mBps"":-160,""holders"":1,""launchedAt"":""2026-10-25T22:12:15.793Z"",""liquidityCents"":""1000"",""devAllocationBps"":5000,""creator"":{""userId"":""01a1751e-4395-7f39-aabe-ae41cf57e656"",""alias"":""lanec_rugger""},""isMine"":false,""mooned"":false,""rugFlagged"":false,""creatorFeesCents"":null},{""coinId"":""01a1751f-1cdf-71eb-a5ee-2852f110781f"",""ticker"":""MCAT"",""name"":""Moon Cat"",""templateId"":""rf_cat"",""stage"":""new"",""priceMicros"":""742"",""mcapCents"":""742000"",""vol5mCents"":""0"",""change5mBps"":-2580,""holders"":0,""launchedAt"":""2026-10-25T22:12:15.711Z"",""liquidityCents"":""1000"",""devAllocationBps"":0,""creator"":{""userId"":""01a1751e-4395-7f39-aabe-ae41cf57e656"",""alias"":""lanec_rugger""},""isMine"":false,""mooned"":false,""rugFlagged"":false,""creatorFeesCents"":null}]}";
        internal const string ErrCooldown = @"{""error"":{""code"":""LAUNCH_COOLDOWN"",""message"":""Wait before launching another coin."",""details"":{""retryAfterMs"":599913}}}";
        internal const string ErrLimit = @"{""error"":{""code"":""LAUNCH_LIMIT_REACHED"",""message"":""You have reached today's coin launch limit."",""details"":{""cap"":3,""retryAfterMs"":85499961}}}";
        internal const string ErrCash = @"{""error"":{""code"":""INSUFFICIENT_CASH"",""message"":""Cash balance is insufficient."",""details"":{""have"":""900"",""need"":""1500""}}}";
        internal const string ErrTaken = @"{""error"":{""code"":""TICKER_TAKEN"",""message"":""That ticker is already used by another coin."",""details"":{}}}";
        internal const string ErrName = @"{""error"":{""code"":""NAME_NOT_ALLOWED"",""message"":""That coin name or ticker is not allowed."",""details"":{""reason"":""name or ticker not allowed""}}}";
        internal const string Feed = @"{""posts"":[{""postId"":""01a1751f-93cb-757b-a3ef-602a721c734a"",""userId"":""00000000-0000-4000-8000-0000000051e7"",""alias"":""The Sleuth"",""text"":""Public service note. $RUGME dev lanec_rugger exited 63% of the coin the minute liquidity showed up. Check who you ape for. Their heat reads 44."",""createdAt"":""2026-10-25T22:12:46.141Z"",""kind"":""sleuth"",""meta"":{""heat"":44,""coinId"":""01a1751f-9262-78cd-ae09-8df5ddc5eb25"",""ticker"":""RUGME"",""deepLink"":""venue:launchpad_tower"",""sellSharePct"":63,""launcherAlias"":""lanec_rugger"",""launcherUserId"":""01a1751e-4395-7f39-aabe-ae41cf57e656""}}]}";
        internal const double Now = 1792966400000;   // 2026-10-25T22:13:20Z
    }

    public sealed class FactoryParseTests
    {
        [Test]
        public void Config_ParsesTheRealReply()
        {
            var c = FactoryParse.Config(JToken.Parse(FactoryFixtures.Config));
            Assert.IsTrue(c.FromServer);
            Assert.AreEqual(500, c.LaunchFeeCents);
            Assert.AreEqual(1000, c.MinLiquidityCents);
            Assert.AreEqual(100000, c.MaxLiquidityCents);
            Assert.AreEqual(5000, c.MaxDevAllocationBps);
            Assert.AreEqual(3, c.DailyLaunchCap);
            Assert.AreEqual(600, c.CooldownSec);
            Assert.AreEqual(24, c.NameMax);
            Assert.AreEqual(6, c.TickerMax);
            Assert.AreEqual(12, c.Templates.Count);
            Assert.IsTrue(c.HasTemplate("rf_crown"));
            Assert.AreEqual(25, c.ArcOddsPct["rug"]);
            Assert.AreEqual(3000, c.RugSellBps);
        }

        [Test]
        public void Config_DefaultsMatchBackend14()
        {
            var d = FactoryConfig.Defaults;
            var c = FactoryParse.Config(JToken.Parse(FactoryFixtures.Config));
            Assert.AreEqual(c.LaunchFeeCents, d.LaunchFeeCents);
            Assert.AreEqual(c.MinLiquidityCents, d.MinLiquidityCents);
            Assert.AreEqual(c.MaxLiquidityCents, d.MaxLiquidityCents);
            Assert.AreEqual(c.MaxDevAllocationBps, d.MaxDevAllocationBps);
            CollectionAssert.AreEqual(c.Templates, d.Templates);
            Assert.IsFalse(d.FromServer);
        }

        [Test]
        public void Config_RejectsInconsistentLimits()
        {
            Assert.Throws<FormatException>(() => FactoryParse.Config(JToken.Parse(@"{""minLiquidityCents"":5000,""maxLiquidityCents"":100}")));
            Assert.Throws<FormatException>(() => FactoryParse.Config(JToken.Parse("[]")));
        }

        [Test]
        public void Me_CooldownAndCap()
        {
            var me = FactoryParse.Me(JToken.Parse(FactoryFixtures.MeCooldown));
            Assert.AreEqual(1, me.LaunchesToday);
            Assert.AreEqual(2, me.LaunchesLeft);
            Assert.AreEqual("cool", me.HeatLevel);
            long next = me.NextLaunchAtMs.Value;
            Assert.AreEqual(DateTimeOffset.Parse("2026-10-25T22:22:01.621Z").ToUnixTimeMilliseconds(), next);
            Assert.IsFalse(me.CanLaunch(next - 1000));
            Assert.AreEqual(1000, me.WaitMs(next - 1000));
            Assert.IsTrue(me.CanLaunch(next));

            var capped = FactoryParse.Me(JToken.Parse(FactoryFixtures.MeCapped));
            Assert.IsTrue(capped.Capped);
            Assert.IsFalse(capped.CanLaunch(FactoryFixtures.Now));
            Assert.IsTrue(capped.CanLaunch(capped.NextLaunchAtMs.Value + 1));

            var hot = FactoryParse.Me(JToken.Parse(FactoryFixtures.MeHeat));
            Assert.AreEqual(44, hot.Heat);
            Assert.AreEqual("hot", hot.HeatLevel);
        }

        [Test]
        public void Me_FractionalHeatFromDecay()
        {
            // a real local reply after the heat decayed on read
            var me = FactoryParse.Me(JToken.Parse(@"{""heat"":43.1,""heatLevel"":""hot"",""launchesToday"":1,""dailyLaunchCap"":3,""nextLaunchAt"":null}"));
            Assert.AreEqual(43, me.Heat);
            Assert.AreEqual(44, FactoryParse.HeatOf(new JValue("43.5")));
        }

        [Test]
        public void Me_UnknownLevelReadsCoolAndHeatIsClamped()
        {
            var me = FactoryParse.Me(JToken.Parse(@"{""heat"":250,""heatLevel"":""inferno"",""launchesToday"":0,""dailyLaunchCap"":3,""nextLaunchAt"":null}"));
            Assert.AreEqual(100, me.Heat);
            Assert.AreEqual("cool", me.HeatLevel);
            Assert.IsNull(me.NextLaunchAtMs);
            Assert.IsTrue(me.CanLaunch(FactoryFixtures.Now));
        }

        [Test]
        public void Coin_LaunchReplyIsMineWithFees()
        {
            var c = FactoryParse.Coin(JToken.Parse(FactoryFixtures.Launch));
            Assert.AreEqual("RUGME", c.Ticker);
            Assert.AreEqual("rf_skull", c.TemplateId);
            Assert.IsTrue(c.Coin.PlayerLaunched);
            Assert.AreEqual("rf_skull", c.Coin.TemplateId);
            Assert.AreEqual(5000, c.LiquidityCents);
            Assert.AreEqual(5000, c.DevAllocationBps);
            Assert.IsTrue(c.IsMine);
            Assert.AreEqual(0, c.CreatorFeesCents);
            Assert.AreEqual("lanec_rugger", c.CreatorAlias);
            Assert.AreEqual(1000, c.Coin.PriceMicros);
            Assert.IsFalse(c.RugFlagged);
        }

        [Test]
        public void List_ViewerSeesNoFees()
        {
            var list = FactoryParse.Coins(JToken.Parse(FactoryFixtures.List), out int skipped);
            Assert.AreEqual(0, skipped);
            Assert.AreEqual(2, list.Count);
            Assert.IsFalse(list[0].IsMine);
            Assert.IsNull(list[0].CreatorFeesCents);
        }

        [Test]
        public void List_SkipsABadRow()
        {
            var list = FactoryParse.Coins(JToken.Parse(@"{""coins"":[{""coinId"":""x"",""stage"":""weird"",""ticker"":""A""}, " + FactoryFixtures.Launch + "]}"), out int skipped);
            Assert.AreEqual(1, skipped);
            Assert.AreEqual(1, list.Count);
        }

        [Test]
        public void Detail_FlatShapeWithChartHoldersAndRisk()
        {
            var d = FactoryParse.Detail(JToken.Parse(FactoryFixtures.Detail));
            Assert.AreEqual("FLRD", d.Coin.Ticker);
            Assert.AreEqual(3, d.Candles.Count);
            Assert.AreEqual(1000, d.Candles[0].Close);
            Assert.AreEqual(2, d.Holders.Count);
            Assert.AreEqual("lanec_sniper", d.Holders[0].Alias);
            StringAssert.Contains("security 5", d.RiskHint);
        }

        [Test]
        public void TemplateIds_OnlyRfLowerCase()
        {
            Assert.AreEqual("rf_frog", MoneyParse.TemplateIdOrNull("rf_frog"));
            Assert.IsNull(MoneyParse.TemplateIdOrNull("frog"));
            Assert.IsNull(MoneyParse.TemplateIdOrNull("rf_Frog"));
            Assert.IsNull(MoneyParse.TemplateIdOrNull("rf_../x"));
            Assert.IsNull(MoneyParse.TemplateIdOrNull(""));
            Assert.IsNull(MoneyParse.TemplateIdOrNull(JValue.CreateNull()));
        }

        [Test]
        public void CoinState_PlayerLaunchedAndTemplateIdMap()
        {
            var fields = new Dictionary<string, object> { ["coinId"] = "c1", ["ticker"] = "LCABC", ["stage"] = "new", ["playerLaunched"] = true, ["templateId"] = "rf_cat" };
            var s = new CoinState();
            StateMapper.ApplyCoin(k => fields.TryGetValue(k, out var v) ? v : null, s);
            Assert.IsTrue(s.playerLaunched);
            Assert.AreEqual("rf_cat", s.templateId);
            var house = new CoinState();
            StateMapper.ApplyCoin(k => k == "coinId" ? "c2" : null, house);
            Assert.IsFalse(house.playerLaunched);
            Assert.AreEqual("", house.templateId);
        }

        [Test]
        public void VenueLinks_ParseOnlyVenueIds()
        {
            Assert.IsTrue(VenueLinks.TryParse("venue:launchpad_tower", out var id));
            Assert.AreEqual("launchpad_tower", id);
            Assert.IsFalse(VenueLinks.TryParse("venue:", out _));
            Assert.IsFalse(VenueLinks.TryParse("venue:../etc", out _));
            Assert.IsFalse(VenueLinks.TryParse("venue:Launchpad", out _));
            Assert.IsFalse(VenueLinks.TryParse("https://example.com", out _));
            Assert.IsFalse(VenueLinks.TryParse("ctlife://quests", out _));
            Assert.IsTrue(VenueLinks.TryParse("ctlife://venue/perp_pit", out var v2));
            Assert.AreEqual("perp_pit", v2);
            Assert.IsTrue(VenueLinks.TryParseCoin("ctlife://coin/01a1751e-e5d6-77ba-af6a-03a0633fbdfe", out var coin));
            Assert.AreEqual("01a1751e-e5d6-77ba-af6a-03a0633fbdfe", coin);
            Assert.IsFalse(VenueLinks.TryParseCoin("ctlife://coin/../x", out _));
            var old = VenueLinks.Travel;
            try
            {
                string went = null;
                VenueLinks.Travel = v => went = v;
                Assert.IsTrue(VenueLinks.Open("venue:launchpad_tower"));
                Assert.AreEqual("launchpad_tower", went);
                VenueLinks.Travel = null;
                Assert.IsFalse(VenueLinks.Open("venue:launchpad_tower"));
            }
            finally { VenueLinks.Travel = old; }
        }
    }

    public sealed class FactoryRulesTests
    {
        private static LaunchDraft Good() => new LaunchDraft { Name = "  Frog   Lord ", Ticker = "$flrd", TemplateId = "rf_frog", LiquidityCents = 5000, DevAllocationBps = 3000 };
        private static readonly FactoryConfig C = FactoryConfig.Defaults;

        [Test]
        public void Body_NormalisesLikeTheServer()
        {
            var b = Good().Body();
            Assert.AreEqual("Frog Lord", (string)b["name"]);
            Assert.AreEqual("FLRD", (string)b["ticker"]);
            Assert.AreEqual("rf_frog", (string)b["templateId"]);
            Assert.AreEqual(5000, (long)b["liquidityCents"]);
            Assert.AreEqual(3000, (int)b["devAllocationBps"]);
            Assert.AreEqual(5, b.Count, "the server body is strict (additionalProperties false)");
        }

        [TestCase("Frog Lord", null)]
        [TestCase("A", "check.nameLength")]
        [TestCase("Twenty five characters xx", "check.nameLength")]
        [TestCase(" frog", null)]
        [TestCase("-frog", "check.nameChars")]
        [TestCase("Frög", "check.nameChars")]
        [TestCase("gm! it's_wif-2.0", null)]
        [TestCase("visit scam.com", null)]   // shape OK; the server's filter refuses links (NAME_NOT_ALLOWED)
        public void Names(string name, string key) => Assert.AreEqual(key, FactoryRules.CheckName(name, C).Key);

        [TestCase("FLRD", null)]
        [TestCase("flrd", null)]
        [TestCase("$wif", null)]
        [TestCase("A", "check.tickerLength")]
        [TestCase("SEVENNN", "check.tickerLength")]
        [TestCase("AB-1", "check.tickerChars")]
        [TestCase("ÅBC", "check.tickerChars")]
        public void Tickers(string ticker, string key) => Assert.AreEqual(key, FactoryRules.CheckTicker(ticker, C).Key);

        [Test]
        public void Check_RangesArtCashAllowance()
        {
            Assert.IsTrue(FactoryRules.Check(Good(), C, 200000, null, FactoryFixtures.Now).Ok);
            var d = Good(); d.TemplateId = "rf_unknown";
            Assert.AreEqual("check.art", FactoryRules.Check(d, C, null, null, 0).Key);
            d = Good(); d.LiquidityCents = 999;
            Assert.AreEqual("check.liquidity", FactoryRules.Check(d, C, null, null, 0).Key);
            d = Good(); d.LiquidityCents = 100001;
            Assert.AreEqual("check.liquidity", FactoryRules.Check(d, C, null, null, 0).Key);
            d = Good(); d.DevAllocationBps = 5001;
            var dev = FactoryRules.Check(d, C, null, null, 0);
            Assert.AreEqual("check.dev", dev.Key);
            Assert.AreEqual("50%", dev.Args[0]);
            var poor = FactoryRules.Check(Good(), C, 5000, null, 0);   // needs 500 + 5000
            Assert.AreEqual("err.cash", poor.Key);
            Assert.AreEqual(MoneyFormat.Cash(500), poor.Args[0]);
            var me = FactoryParse.Me(JToken.Parse(FactoryFixtures.MeCooldown));
            var cool = FactoryRules.Check(Good(), C, 200000, me, me.NextLaunchAtMs.Value - 65000);
            Assert.AreEqual("err.cooldown", cool.Key);
            Assert.AreEqual("1:05", cool.Args[0]);
            Assert.IsTrue(FactoryRules.Check(Good(), C, 200000, me, me.NextLaunchAtMs.Value).Ok);
            var capped = FactoryParse.Me(JToken.Parse(FactoryFixtures.MeCapped));
            Assert.AreEqual("err.limit", FactoryRules.Check(Good(), C, 200000, capped, FactoryFixtures.Now).Key);
        }

        [Test]
        public void Preview_UnitsFollowTheLaunchPrice()
        {
            // backend: seedUnits = liquidity * 10,000 / 1,000 micros; creator = seed * bps / 10,000 (5000c at 30% -> 15,000 units,
            // the creator's holding in the real FLRD detail)
            Assert.AreEqual(50000, FactoryRules.SeedUnits(5000));
            Assert.AreEqual(15000, FactoryRules.DevUnits(5000, 3000));
            Assert.AreEqual(5500, FactoryRules.TotalCostCents(Good(), C));
        }

        [Test]
        public void MoonProgress_LaunchToThreeX()
        {
            var c = FactoryConfig.Defaults;
            Assert.AreEqual(0f, FactoryRules.MoonProgress(1000, c));
            Assert.AreEqual(0f, FactoryRules.MoonProgress(969, c), "below launch clamps to 0");
            Assert.AreEqual(0.5f, FactoryRules.MoonProgress(2000, c), 1e-6);
            Assert.AreEqual(1f, FactoryRules.MoonProgress(3000, c));
            Assert.AreEqual(1f, FactoryRules.MoonProgress(9000, c));
        }

        [TestCase(0, "0:00")]
        [TestCase(1, "0:01")]
        [TestCase(599913, "10:00")]
        [TestCase(3599000, "59:59")]
        [TestCase(85499961, "23h 45m")]
        public void Clock(long ms, string text) => Assert.AreEqual(text, FactoryText.Clock(ms));
    }

    public sealed class FactoryErrorTests
    {
        private static ApiException Api(int status, string json)
        {
            var e = JObject.Parse(json)["error"];
            return new ApiException(status, (string)e["code"], (string)e["message"], e["details"]);
        }

        [Test]
        public void Cooldown_CarriesRetryAfter()
        {
            var err = FactoryErrors.From(Api(429, FactoryFixtures.ErrCooldown), true);
            Assert.AreEqual("LAUNCH_COOLDOWN", err.Code);
            Assert.AreEqual(599913, err.RetryAfterMs);
            Assert.AreEqual("err.cooldown", err.Key);
            StringAssert.Contains("10:00", err.Message);
            Assert.IsFalse(err.OutcomeUnknown);
        }

        [Test]
        public void Limit_Name_Taken_Cash()
        {
            var limit = FactoryErrors.From(Api(429, FactoryFixtures.ErrLimit), true);
            Assert.AreEqual("err.limit", limit.Key);
            StringAssert.Contains("23h 45m", limit.Message);
            Assert.AreEqual("err.name", FactoryErrors.From(Api(400, FactoryFixtures.ErrName), true).Key);
            Assert.AreEqual("err.taken", FactoryErrors.From(Api(409, FactoryFixtures.ErrTaken), true).Key);
            var cash = FactoryErrors.From(Api(402, FactoryFixtures.ErrCash), true);
            Assert.AreEqual("err.cash", cash.Key);
            StringAssert.Contains(MoneyFormat.Cash(600), cash.Message, "need 1500 - have 900");
        }

        [Test]
        public void Network_LeavesTheOutcomeUnknown()
        {
            var err = FactoryErrors.From(new ApiException(0, "NETWORK_ERROR", "offline", null, true), true);
            Assert.IsTrue(err.OutcomeUnknown);
            Assert.IsNull(err.Key);
            Assert.IsFalse(string.IsNullOrEmpty(err.Message));
        }
    }

    public sealed class LaunchpadDeskTests
    {
        [Test]
        public void Caption_States()
        {
            var c = FactoryConfig.Defaults;
            var closed = LaunchpadDesk.Caption(null, c, FactoryFixtures.Now, false, false);
            Assert.IsTrue(closed.disabled);
            Assert.AreEqual(FactoryText.T("desk.soon"), closed.sub);
            Assert.IsTrue(LaunchpadDesk.Caption(null, c, FactoryFixtures.Now, true, true).disabled);
            var unknown = LaunchpadDesk.Caption(null, c, FactoryFixtures.Now, true, false);
            Assert.IsFalse(unknown.disabled);
            StringAssert.Contains("3 of 3", unknown.sub);
            var me = FactoryParse.Me(JToken.Parse(FactoryFixtures.MeCooldown));
            var cooling = LaunchpadDesk.Caption(me, c, me.NextLaunchAtMs.Value - 90000, true, false);
            Assert.IsTrue(cooling.disabled);
            Assert.AreEqual("warn", cooling.tone);
            StringAssert.Contains("1:30", cooling.sub);
            var ready = LaunchpadDesk.Caption(me, c, me.NextLaunchAtMs.Value, true, false);
            Assert.IsFalse(ready.disabled);
            StringAssert.Contains("2 of 3", ready.sub);
            StringAssert.Contains(MoneyFormat.Cash(500), ready.sub);
            var capped = FactoryParse.Me(JToken.Parse(FactoryFixtures.MeCapped));
            var cap = LaunchpadDesk.Caption(capped, c, FactoryFixtures.Now, true, false);
            Assert.IsTrue(cap.disabled);
            Assert.AreEqual("bad", cap.tone);
            var poor = LaunchpadDesk.Caption(null, c, FactoryFixtures.Now, true, false, 1499);
            Assert.IsTrue(poor.disabled);
            StringAssert.Contains(MoneyFormat.Cash(1500), poor.sub);
            Assert.IsFalse(LaunchpadDesk.Caption(null, c, FactoryFixtures.Now, true, false, 1500).disabled);
        }
    }

    public sealed class FactoryServiceTests
    {
        private sealed class FakeRest : IRestClient
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> Keys = new List<string>();
            public readonly List<string> Bodies = new List<string>();
            public Func<string, object> OnGet = _ => null;
            public Func<string, object> OnPost = _ => null;
            public Func<Task<bool>> RefreshHandler { get; set; }

            private static Task<T> Answer<T>(object o)
            {
                if (o is Exception e) return Task.FromException<T>(e);
                return Task.FromResult((T)(object)(o is string s ? JToken.Parse(s) : (JToken)o));
            }

            public Task<T> Get<T>(string path, CancellationToken ct = default) { Calls.Add("GET " + path); return Answer<T>(OnGet(path)); }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default)
            {
                Calls.Add("POST " + path); Keys.Add(idempotencyKey); Bodies.Add(body as string);
                return Answer<T>(OnPost(path));
            }
            public Task<T> Put<T>(string path, object body, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Patch<T>(string path, object body, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Delete<T>(string path, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => throw new NotSupportedException();
        }

        private static LaunchDraft Draft() => new LaunchDraft { Name = "Rug Me Daddy", Ticker = "rugme", TemplateId = "rf_skull", LiquidityCents = 5000, DevAllocationBps = 5000 };
        private static double _clock;
        private static FactoryService Svc(FakeRest rest) => new FactoryService("01a1751e-4395-7f39-aabe-ae41cf57e656", rest, () => FactoryFixtures.Now, () => _clock, subscribe: false);

        private static ApiException Api(int status, string json)
        {
            var e = JObject.Parse(json)["error"];
            return new ApiException(status, (string)e["code"], (string)e["message"], e["details"]);
        }

        [Test]
        public void Launch_PostsTheStrictBodyAndAddsToMine()
        {
            var rest = new FakeRest { OnPost = _ => FactoryFixtures.Launch, OnGet = p => p == "/factory/me" ? FactoryFixtures.MeCooldown : null };
            var svc = Svc(rest);
            FactoryCoin launched = null;
            svc.Launched += c => launched = c;
            var r = svc.Launch(Draft(), 200000).GetAwaiter().GetResult();
            Assert.IsTrue(r.Ok);
            Assert.AreEqual("RUGME", launched.Ticker);
            Assert.AreEqual(1, svc.Mine.Count);
            Assert.AreEqual("POST /factory/coins", rest.Calls[0]);
            var body = JObject.Parse(rest.Bodies[0]);
            Assert.AreEqual("RUGME", (string)body["ticker"]);
            Assert.AreEqual(5, body.Count);
            Assert.IsFalse(string.IsNullOrEmpty(rest.Keys[0]));
            Assert.Contains("GET /factory/me", rest.Calls, "the allowance refreshes after a launch");
            Assert.IsFalse(svc.LaunchPending);
            svc.Dispose();
        }

        [Test]
        public void Launch_PreCheckSendsNothing()
        {
            var rest = new FakeRest();
            var svc = Svc(rest);
            var r = svc.Launch(Draft(), 100).GetAwaiter().GetResult();
            Assert.IsFalse(r.Ok);
            Assert.AreEqual("err.cash", r.Check.Key);
            Assert.AreEqual(0, rest.Calls.Count);
            svc.Dispose();
        }

        [Test]
        public void Launch_UnknownOutcomeReusesTheKey_DefinitiveRefusalDropsIt()
        {
            int n = 0;
            var rest = new FakeRest
            {
                OnPost = _ => ++n == 1 ? (object)new ApiException(0, "NETWORK_ERROR", "offline", null, true)
                    : n == 2 ? FactoryFixtures.Launch : Api(409, FactoryFixtures.ErrTaken),
                // an allowance with no wait, so the retries reach the server (the cooldown pre-check is tested elsewhere)
                OnGet = p => p == "/factory/me" ? @"{""heat"":0,""heatLevel"":""cool"",""launchesToday"":0,""dailyLaunchCap"":3,""nextLaunchAt"":null}" : null,
            };
            var svc = Svc(rest);
            var first = svc.Launch(Draft(), null).GetAwaiter().GetResult();
            Assert.IsTrue(first.Error.OutcomeUnknown);
            Assert.IsTrue(svc.LaunchPending);
            var retry = svc.Launch(Draft(), null).GetAwaiter().GetResult();
            Assert.IsTrue(retry.Ok);
            Assert.AreEqual(rest.Keys[0], rest.Keys[1], "a retry after an unknown outcome must reuse the Idempotency-Key");
            var other = Draft(); other.Ticker = "FLRD";
            var refused = svc.Launch(other, null).GetAwaiter().GetResult();
            Assert.AreEqual("TICKER_TAKEN", refused.Error.Code);
            Assert.AreNotEqual(rest.Keys[1], rest.Keys[2]);
            Assert.IsFalse(svc.LaunchPending, "a definitive refusal frees the draft");
            svc.Dispose();
        }

        [Test]
        public void Launch_CooldownRefusalRefreshesTheAllowance()
        {
            var rest = new FakeRest { OnPost = _ => Api(429, FactoryFixtures.ErrCooldown), OnGet = p => p == "/factory/me" ? FactoryFixtures.MeCooldown : null };
            var svc = Svc(rest);
            var r = svc.Launch(Draft(), null).GetAwaiter().GetResult();
            Assert.AreEqual("LAUNCH_COOLDOWN", r.Error.Code);
            Assert.Contains("GET /factory/me", rest.Calls);
            Assert.IsNotNull(svc.Me);
            Assert.IsFalse(svc.CanLaunchNow);
            svc.Dispose();
        }

        [Test]
        public void Launch_CountsLocallySoAnImmediateRetryIsPreChecked()
        {
            // device 2026-10-26: a second launch sent before /factory/me answered reached the server (429 + RestClient's 1 s retry)
            var rest = new FakeRest
            {
                OnPost = _ => FactoryFixtures.Launch,
                OnGet = p => p == "/factory/me" ? (object)new ApiException(0, "NETWORK_ERROR", "slow", null, true) : null,
            };
            var svc = Svc(rest);
            svc.RefreshMe(force: true).GetAwaiter().GetResult();   // fails: Me stays null
            rest.OnGet = p => @"{""heat"":0,""heatLevel"":""cool"",""launchesToday"":0,""dailyLaunchCap"":3,""nextLaunchAt"":null}";
            svc.RefreshMe(force: true).GetAwaiter().GetResult();
            rest.OnGet = p => new ApiException(0, "NETWORK_ERROR", "slow", null, true);
            Assert.IsTrue(svc.Launch(Draft(), null).GetAwaiter().GetResult().Ok);
            Assert.AreEqual(1, svc.Me.LaunchesToday);
            Assert.IsFalse(svc.CanLaunchNow);
            var second = Draft(); second.Ticker = "AGAIN";
            var r = svc.Launch(second, null).GetAwaiter().GetResult();
            Assert.AreEqual("err.cooldown", r.Check.Key);
            Assert.AreEqual(1, rest.Calls.FindAll(c => c.StartsWith("POST")).Count);
            svc.Dispose();
        }

        [Test]
        public void RefreshMe_IsCachedUntilStale()
        {
            var rest = new FakeRest { OnGet = p => FactoryFixtures.MeCooldown };
            var svc = Svc(rest);
            _clock = 100;
            svc.RefreshMe().GetAwaiter().GetResult();
            svc.RefreshMe().GetAwaiter().GetResult();
            Assert.AreEqual(1, rest.Calls.Count);
            _clock = 100 + FactoryService.MeStaleSeconds + 1;
            svc.RefreshMe().GetAwaiter().GetResult();
            Assert.AreEqual(2, rest.Calls.Count);
            svc.RefreshMe(force: true).GetAwaiter().GetResult();
            Assert.AreEqual(3, rest.Calls.Count);
            svc.Dispose();
        }

        [Test]
        public void Events_PlayerLaunchAndMyRug()
        {
            var rest = new FakeRest { OnPost = _ => FactoryFixtures.Launch, OnGet = p => FactoryFixtures.MeHeat };
            var svc = Svc(rest);
            svc.Launch(Draft(), null).GetAwaiter().GetResult();
            PlayerLaunchSeen seen = null; RugSeen rug = null;
            svc.PlayerLaunch += s => seen = s;
            svc.Rug += x => rug = x;
            svc.OnEvent(new EventMessage { eventId = "evt_coin_launch", @params = new Dictionary<string, string> { ["coinId"] = "01a1751f-9262-78cd-ae09-8df5ddc5eb25", ["ticker"] = "RUGME", ["playerLaunched"] = "1", ["templateId"] = "rf_skull", ["creatorAlias"] = "lanec_rugger" } });
            Assert.IsNotNull(seen);
            Assert.IsTrue(seen.Mine);
            Assert.AreEqual("rf_skull", seen.TemplateId);
            seen = null;
            svc.OnEvent(new EventMessage { eventId = "evt_coin_launch", @params = new Dictionary<string, string> { ["coinId"] = "c-house", ["ticker"] = "HOUSE" } });
            Assert.IsNull(seen, "house launches are not player launches");
            int before = rest.Calls.Count;
            svc.OnEvent(new EventMessage { eventId = "evt_rug", @params = new Dictionary<string, string> { ["coinId"] = "01a1751f-9262-78cd-ae09-8df5ddc5eb25", ["ticker"] = "RUGME", ["kind"] = "creator_dump", ["launcherAlias"] = "lanec_rugger", ["heat"] = "44" } });
            Assert.IsTrue(rug.Mine);
            Assert.AreEqual(44, rug.Heat);
            Assert.IsTrue(svc.Mine[0].RugFlagged);
            Assert.Greater(rest.Calls.Count, before, "my rug refreshes the heat");
            svc.Dispose();
        }
    }
}
