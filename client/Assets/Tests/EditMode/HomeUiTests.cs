using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>The home redesign (docs/progress/home-ui.md): HomeModel rules, the creator look order, and the new kit pieces.</summary>
    public class HomeUiTests
    {
        private static Me NewMe(long cash = 30000, long debt = 0, int energy = 50, int sanity = 50, JObject flags = null,
            string dream = "make_10k", string home = "parents_basement")
        {
            var body = new JObject
            {
                ["user"] = new JObject { ["id"] = "01a11860-9c1f-77a4-a496-17038bb21f01", ["wallet"] = "HAoo9696iSShm1io2SsnU6R7XhAn8y1YH7sEHgFajESf",
                    ["isSeeker"] = false, ["skrName"] = null },
                ["character"] = new JObject
                {
                    ["alias"] = "gm_anon", ["tribeId"] = "trencher", ["traitIds"] = new JArray("fast_talker", "iron_will"), ["dreamId"] = dream,
                    ["look"] = new JObject { ["skinTone"] = "skin_tan", ["headId"] = "head_player_f_kol", ["hairId"] = "h", ["topId"] = "t",
                        ["bottomId"] = "b", ["shoesId"] = "s", ["accId"] = "acc_none" },
                },
                ["needs"] = new JObject { ["energy"] = energy, ["hunger"] = 50, ["fun"] = 50, ["hygiene"] = 50, ["sanity"] = sanity },
                ["skills"] = new JObject { ["clout"] = 1, ["coding"] = 1, ["trading"] = 1, ["writing"] = 1, ["design"] = 1, ["security"] = 1 },
                ["cashCents"] = cash, ["debtCents"] = debt, ["homeId"] = home, ["minerTier"] = "none",
                ["flags"] = flags ?? new JObject(),
            };
            return Json.Deserialize<Me>(MeCompat.Normalize(body).ToString());
        }

        private static Registries Reg()
        {
            var ids = new JObject
            {
                ["tribes"] = new JObject { ["tribes"] = new JArray(new JObject { ["id"] = "trencher", ["name"] = "Trencher", ["perk"] = "x" }) },
                ["dreams"] = new JObject { ["dreams"] = new JArray(new JObject { ["id"] = "make_10k", ["label"] = "Make $10K" }) },
                ["homes"] = new JObject { ["homes"] = new JArray(new JObject { ["id"] = "parents_basement", ["name"] = "Parent's Basement" }) },
            };
            return Registries.Parse(ids, null);
        }

        private static readonly JObject Lottery = JObject.Parse(@"{ ""debtRepayment"": { ""weeklyDueCents"": 12000, ""totalCents"": 72000 } }");

        // ---------------------------------------------------------------- status chips

        [Test]
        public void Chips_CashAlways_DebtOnlyWhenOwed()
        {
            var s = HomeModel.Build(NewMe(debt: 0), Reg(), null, Lottery, null, false);
            CollectionAssert.AreEqual(new[] { "cash", "energy", "sanity" }, s.Stats.Select(x => x.id).ToArray());
            Assert.AreEqual("$300", s.Stats[0].value);
            var d = HomeModel.Build(NewMe(debt: 72000), Reg(), null, Lottery, null, false);
            CollectionAssert.AreEqual(new[] { "cash", "debt", "energy", "sanity" }, d.Stats.Select(x => x.id).ToArray());
            var debt = d.Stats[1];
            Assert.AreEqual("$720", debt.value);
            StringAssert.Contains("$720", debt.explainBody);
            StringAssert.Contains("$120", debt.explainBody, "weekly due comes from lottery.json debtRepayment");
            Assert.AreEqual("bank", debt.actionApp);
            var noWeekly = HomeModel.Build(NewMe(debt: 72000), Reg(), null, null, null, false);
            StringAssert.DoesNotContain("each week", noWeekly.Stats[1].explainBody, "no weekly line without config");
        }

        [Test]
        public void Chips_KeepTheOldRoutes()
        {
            var s = HomeModel.Build(NewMe(debt: 100), Reg(), null, Lottery, null, false);
            Assert.AreEqual("bank", s.Stats.First(x => x.id == "cash").actionApp);
            Assert.AreEqual("home", s.Stats.First(x => x.id == "energy").actionApp);
            Assert.AreEqual("home", s.Stats.First(x => x.id == "sanity").actionApp);
        }

        [Test]
        public void Needs_LevelsUseServerWarningsOrTheHudDefaults()
        {
            Assert.AreEqual(StatLevel.Ok, HomeModel.Level(26, 25, 10));
            Assert.AreEqual(StatLevel.Low, HomeModel.Level(25, 25, 10));
            Assert.AreEqual(StatLevel.Critical, HomeModel.Level(10, 25, 10));
            Assert.AreEqual((25, 10), HomeModel.NeedThresholds(null));
            Assert.AreEqual((40, 15), HomeModel.NeedThresholds(JObject.Parse("{\"warnings\":{\"low\":40,\"critical\":15}}")));
            var tired = HomeModel.Build(NewMe(energy: 8), Reg(), null, Lottery, null, false);
            Assert.AreEqual(StatLevel.Critical, tired.Stats.First(x => x.id == "energy").level);
            Assert.IsTrue(tired.LowEnergy);
            Assert.AreEqual(0.08f, tired.Stats.First(x => x.id == "energy").fill, 0.001f);
        }

        [Test]
        public void NoProfile_NoStats()
        {
            var s = HomeModel.Build(null, Reg(), null, Lottery, null, false);
            Assert.IsFalse(s.HasProfile);
            Assert.AreEqual(0, s.Stats.Count);
        }

        // ---------------------------------------------------------------- greeting, mood, reaction

        [Test]
        public void MoodLine_FromMarketMoodOnly()
        {
            Assert.AreEqual("home.sub.noMood", HomeModel.MoodLineKey(null));
            Assert.AreEqual("home.sub.up", HomeModel.MoodLineKey(420));
            Assert.AreEqual("home.sub.down", HomeModel.MoodLineKey(-300));
            Assert.AreEqual("home.sub.flat", HomeModel.MoodLineKey(120));
            var first = HomeModel.Build(NewMe(), Reg(), null, Lottery, 420, true);
            Assert.AreEqual("home.greet.first", first.GreetingKey);
            Assert.AreEqual("home.sub.first", first.SubKey);
        }

        [Test]
        public void Reaction_NeedsFirst_ThenFirstDay_ThenMarket()
        {
            Assert.AreEqual("home.say.tired", HomeModel.Build(NewMe(energy: 5), Reg(), null, null, 500, true).ReactionKey);
            Assert.AreEqual("home.say.stressed", HomeModel.Build(NewMe(sanity: 20), Reg(), null, null, 500, false).ReactionKey);
            Assert.AreEqual("home.say.first", HomeModel.Build(NewMe(), Reg(), null, null, 500, true).ReactionKey);
            Assert.AreEqual("home.say.up", HomeModel.Build(NewMe(), Reg(), null, null, 500, false).ReactionKey);
            Assert.AreEqual("home.say.down", HomeModel.Build(NewMe(), Reg(), null, null, -500, false).ReactionKey);
            var back = HomeModel.Build(NewMe(), Reg(), null, null, null, false);
            Assert.AreEqual("home.say.back", back.ReactionKey);
            CollectionAssert.Contains(back.ReactionEmotes, "emote_wave");
        }

        [Test]
        public void Names_ComeFromTheRegistries()
        {
            var s = HomeModel.Build(NewMe(), Reg(), null, null, null, false);
            Assert.AreEqual("gm_anon", s.Alias);
            Assert.AreEqual("Trencher", s.TribeName);
            Assert.AreEqual("Make $10K", s.DreamName);
            Assert.AreEqual("Parent's Basement", s.HomeName);
        }

        // ---------------------------------------------------------------- dream progress

        [Test]
        public void DreamProgress_OnlyFromServerFlags()
        {
            Assert.IsNull(HomeModel.Build(NewMe(), Reg(), null, null, null, false).Dream, "no server progress, no bar");
            var me = NewMe(flags: JObject.Parse("{\"dreamProgress\":{\"current\":250000,\"target\":1000000,\"unit\":\"cents\"}}"));
            var p = HomeModel.ReadDreamProgress(me);
            Assert.AreEqual(0.25f, p.Fraction, 0.0001f);
            Assert.AreEqual("$2,500 of $10,000", p.Text());
            Assert.IsNull(HomeModel.ReadDreamProgress(NewMe(flags: JObject.Parse("{\"dreamProgress\":{\"current\":1,\"target\":0}}"))));
            Assert.IsNull(HomeModel.ReadDreamProgress(NewMe(flags: JObject.Parse("{\"dreamProgress\":\"soon\"}"))));
            var count = HomeModel.ReadDreamProgress(NewMe(flags: JObject.Parse("{\"dreamProgress\":{\"current\":3,\"target\":100}}")));
            Assert.AreEqual("3 of 100", count.Text());
        }

        // ---------------------------------------------------------------- creator look order

        private static readonly string[][] Catalog =
        {
            new[] { "skin_tone3", "head_snow" }, new[] { "skin_tone3", "head_rain" },
            new[] { "skin_brown", "head_player_m_trencher" }, new[] { "skin_tan", "head_player_m_perp" },
            new[] { "skin_olive", "head_player_m_shark" }, new[] { "skin_fair", "head_player_m_farmer" },
            new[] { "skin_dark", "head_player_m_kol" }, new[] { "skin_deep", "head_player_m_builder" },
            new[] { "skin_light", "head_player_m_anon" }, new[] { "skin_deep", "head_player_m_lagos_kaftan" },
            new[] { "skin_light", "head_player_m_tradfi" }, new[] { "skin_olive", "head_player_m_coat" },
            new[] { "skin_dark", "head_player_m_skr" }, new[] { "skin_warm", "head_player_f_trencher" },
            new[] { "skin_deep", "head_player_f_lagos_ankara" }, new[] { "skin_light", "head_player_f_blazer" },
            new[] { "skin_tan", "head_player_f_kol" }, new[] { "skin_brown", "head_player_f_builder" },
            new[] { "skin_olive", "head_player_f_anon" }, new[] { "skin_dark", "head_player_f_perp" },
            new[] { "skin_fair", "head_player_f_farmer" }, new[] { "skin_tan", "head_player_f_shark" },
            new[] { "skin_olive", "head_player_f_coat" }, new[] { "skin_brown", "head_player_f_lagos_kaftan" },
        };

        [Test]
        public void LookOrder_IsAPermutation_ThatAlternatesBandsAndFamilies()
        {
            foreach (var seed in new[] { 0, 1, 2, 3, 4, 5, 77, 123456 })
            {
                var order = CreatorLookOrder.Arrange(Catalog, seed);
                CollectionAssert.AreEquivalent(Enumerable.Range(0, Catalog.Length), order, "seed " + seed);
                // The light band has 5 looks: while all three bands have looks, neighbours never share a band.
                for (int i = 1; i < 15; i++)
                    Assert.AreNotEqual(CreatorLookOrder.BandOf(Catalog[order[i]][0]), CreatorLookOrder.BandOf(Catalog[order[i - 1]][0]),
                        $"seed {seed}: looks {i - 1} and {i} share a skin-tone band");
                int sameFamily = 0;
                for (int i = 1; i < 12; i++)
                    if (CreatorLookOrder.FamilyOf(Catalog[order[i]][1]) == CreatorLookOrder.FamilyOf(Catalog[order[i - 1]][1])) sameFamily++;
                Assert.LessOrEqual(sameFamily, 2, "seed " + seed + ": body families mostly alternate");
                var lagos = order.Take(6).Count(i => Catalog[i][1].Contains("lagos"));
                Assert.LessOrEqual(lagos, 1, "no region-specific run at the start");
            }
        }

        [Test]
        public void LookOrder_FirstLookVariesWithTheSeed()
        {
            var firstBands = new HashSet<CreatorLookOrder.Band>();
            var firstFamilies = new HashSet<char>();
            for (int seed = 0; seed < 12; seed++)
            {
                var first = Catalog[CreatorLookOrder.Arrange(Catalog, seed)[0]];
                firstBands.Add(CreatorLookOrder.BandOf(first[0]));
                firstFamilies.Add(CreatorLookOrder.FamilyOf(first[1]));
            }
            Assert.AreEqual(3, firstBands.Count, "light, medium and deep each lead for some installs");
            Assert.AreEqual(2, firstFamilies.Count);
            Assert.AreEqual(CreatorLookOrder.Band.Deep, CreatorLookOrder.BandOf("skin_tone3"));
            Assert.AreEqual('m', CreatorLookOrder.FamilyOf("head_snow"));
            Assert.AreEqual('f', CreatorLookOrder.FamilyOf("head_player_f_kol"));
            CollectionAssert.AreEqual(new[] { 0 }, CreatorLookOrder.Arrange(new[] { new[] { "odd", "head_x" } }, 9));
        }

        // ---------------------------------------------------------------- kit

        [Test]
        public void StatChip_StateIsMoreThanColour()
        {
            int taps = 0;
            var chip = new UI.Kit.StatChip("energy", "energy", "green", "ENERGY", _ => taps++, withBar: true);
            chip.Set("8", UI.Kit.StatChip.Tone.Bad, 0.08f);
            Assert.IsTrue(chip.ClassListContains("kit-chip--bad"));
            Assert.AreEqual(DisplayStyle.Flex, chip.Q(className: "kit-chip__pip").style.display.value, "a ! pip, not colour alone");
            chip.Press();
            Assert.AreEqual(1, taps);
            chip.Set("80");
            Assert.AreEqual(DisplayStyle.None, chip.Q(className: "kit-chip__pip").style.display.value);
            Assert.AreEqual("80", chip.Value);
        }

        [Test]
        public void PaintedBadge_DrawsNoToneFillOrBorderBehindTheArt()
        {
            var tex = new UnityEngine.Texture2D(4, 4);
            try
            {
                var badge = new UI.Kit.IconBadge("map", "cyan", tex);
                Assert.IsTrue(badge.ClassListContains("kit-badge--painted"));
                Assert.AreEqual(UnityEngine.Color.clear, badge.style.backgroundColor.value);
                Assert.AreEqual(0f, badge.style.borderTopWidth.value);
                Assert.AreEqual(0f, badge.style.borderRightWidth.value);
                Assert.AreEqual(0f, badge.style.borderBottomWidth.value);
                Assert.AreEqual(0f, badge.style.borderLeftWidth.value);
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        [Test]
        public void IconTileAndBadge_Build()
        {
            int opened = 0;
            var tile = new UI.Kit.IconTile("map", "Explore", "cyan", () => opened++, sfx: null);
            Assert.IsNotNull(tile.Q(className: "kit-badge"));
            Assert.IsNotNull(tile.Q(className: "kit-badge--cyan"));
            tile.Press();
            Assert.AreEqual(1, opened);
            var pop = new UI.Kit.Popover();
            pop.Show(new UnityEngine.Rect(100, 100, 300, 130), "Energy", "8/100", "OPEN HOME", () => opened++);
            Assert.AreEqual("Energy", pop.Title);
            pop.Hide();
            Assert.AreEqual(DisplayStyle.None, pop.style.display.value);
        }

        [Test]
        public void EveryHomeKeyHasCopy()
        {
            foreach (var k in new[] { "home.greet", "home.greet.first", "home.sub.first", "home.sub.noMood", "home.sub.up", "home.sub.down",
                         "home.sub.flat", "home.cash", "home.debt", "home.energy", "home.sanity", "home.explain.cashBody", "home.explain.debtBody",
                         "home.explain.debtWeekly", "home.explain.energyBody", "home.explain.sanityBody", "home.action.bank", "home.action.home",
                         "home.say.first", "home.say.back", "home.say.tired", "home.say.stressed", "home.say.up", "home.say.down",
                         "home.say.error", "home.say.tap.0", "home.say.tap.1", "home.say.tap.2", "home.error.travel", "lobby.enter",
                         "cast.player_m_lagos_kaftan", "cast.player_f_lagos_ankara" })
                Assert.AreNotEqual(k, Strings.T(k), "missing copy for " + k);
            StringAssert.Contains("can't be withdrawn", Strings.T("home.explain.cashBody"));
            StringAssert.DoesNotContain("Lagos", Strings.T("cast.player_m_lagos_kaftan"));
        }
    }
}
