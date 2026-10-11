using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Careers.Tests
{
    public class CareerConfigTests
    {
        // Excerpt of contract/config/careers.json (rc1) shapes, including both skillGates forms.
        private const string Rc1 = @"{""version"":""1.0.0-rc1"",""careers"":[
          {""id"":""discord_mod"",""name"":""Discord Mod"",""active"":true,""phase"":1,""skillGates"":{},
           ""levels"":[{""level"":1,""payCents"":4000,""shiftSec"":45,""xp"":100,""miniGameId"":""discord_mod_ban""},
                       {""level"":2,""payCents"":5500,""shiftSec"":50,""xp"":120,""miniGameId"":""discord_mod_ban""}]},
          {""id"":""kol_engagement"",""name"":""KOL Engagement"",""active"":true,""phase"":1,""skillGates"":{""skill"":""writing"",""level"":2},
           ""levels"":[{""level"":1,""payCents"":4500,""shiftSec"":50,""xp"":100,""miniGameId"":""kols_engagement""}]},
          {""id"":""rug_sleuth"",""name"":""Rug Sleuth"",""active"":false,""phase"":4,""skillGates"":{""security"":4},
           ""levels"":[{""level"":1,""payCents"":8000,""shiftSec"":60,""xp"":150,""miniGameIds"":[""sleuth_thread"",""sleuth_bundle_check""]}]}
        ]}";

        [Test]
        public void ParsesContractShapeAndBothGateForms()
        {
            var list = CareerConfig.Parse(JToken.Parse(Rc1));
            Assert.AreEqual(3, list.Count);
            var mod = list[0];
            Assert.AreEqual(0, mod.Gates.Count);
            Assert.AreEqual(5500, mod.Level(2).PayCents);
            Assert.AreEqual("discord_mod_ban", mod.Level(1).MiniGameId);
            CollectionAssert.AreEqual(new[] { "discord_mod_ban" }, mod.Level(1).MiniGameIds);
            Assert.AreEqual("writing", list[1].Gates.Single().Skill);
            Assert.AreEqual(2, list[1].Gates.Single().Level);
            var sleuth = list[2];
            Assert.AreEqual("security", sleuth.Gates.Single().Skill);
            Assert.AreEqual(4, sleuth.Phase);
            Assert.IsFalse(sleuth.Active);
            Assert.AreEqual("sleuth_thread", sleuth.Level(1).MiniGameId);
            Assert.AreEqual(2, sleuth.Level(1).MiniGameIds.Count);
        }

        [Test]
        public void LevelOneGatesNeverBlockAndJunkIsSkipped()
        {
            var gates = CareerConfig.ParseGates(JToken.Parse(@"{""skill"":""coding"",""level"":1}"));
            Assert.AreEqual(0, gates.Count);
            var list = CareerConfig.Parse(JToken.Parse(@"{""careers"":[{""name"":""no id""}, 5, {""id"":""x"",""levels"":[{""level"":9},{""level"":1,""payCents"":-5}]}]}"));
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(1, list[0].Levels.Count);
            Assert.AreEqual(0, list[0].Levels[0].PayCents);
        }

        [Test]
        public void AcceptsIdKeyedObjects()
        {
            var list = CareerConfig.Parse(JToken.Parse(@"{""version"":""x"",""copywriter"":{""name"":""Copywriter"",""phase"":4,""levels"":[]}}"));
            Assert.AreEqual("copywriter", list.Single().Id);
        }
    }

    public class JobBoardTests
    {
        private static List<CareerDef> Config() => CareerConfig.Parse(JToken.Parse(@"{""careers"":[
            {""id"":""discord_mod"",""name"":""Discord Mod"",""phase"":1,""skillGates"":{},""levels"":[{""level"":1,""payCents"":4000,""shiftSec"":45,""xp"":100,""miniGameId"":""discord_mod_ban""},{""level"":2,""payCents"":5500,""shiftSec"":50,""xp"":120,""miniGameId"":""discord_mod_ban""}]},
            {""id"":""kol_engagement"",""name"":""KOL"",""phase"":1,""skillGates"":{""skill"":""writing"",""level"":2},""levels"":[{""level"":1,""payCents"":4500,""shiftSec"":50,""xp"":100,""miniGameId"":""kols_engagement""}]},
            {""id"":""junior_analyst"",""name"":""Junior Analyst"",""phase"":1,""levels"":[{""level"":1,""payCents"":5000,""shiftSec"":60,""xp"":100,""miniGameId"":""junior_analyst_earnings""}]},
            {""id"":""rug_sleuth"",""name"":""Rug Sleuth"",""phase"":4,""levels"":[{""level"":1,""payCents"":8000,""shiftSec"":60,""xp"":150,""miniGameId"":""sleuth_thread""}]}]}"));

        [Test]
        public void BackendShapeAllActiveMeansOfferedAndHeldComesFromApply()
        {
            // Backend checkout: every offered career active=true, level 1, extra pay fields.
            var rows = CareerRow.ParseList(JToken.Parse(@"[
                {""id"":""discord_mod"",""name"":""Discord Mod"",""level"":1,""xp"":0,""active"":true,""payCents"":4000,""shiftSec"":45,""miniGameId"":""discord_mod_ban"",""phase"":1},
                {""id"":""kol_engagement"",""name"":""KOL"",""level"":1,""xp"":0,""active"":true},
                {""id"":""junior_analyst"",""name"":""Junior Analyst"",""level"":1,""xp"":0,""active"":true}]"), out int bad);
            Assert.AreEqual(0, bad);
            Assert.IsFalse(JobBoard.ActiveMeansHeld(rows));
            var cards = JobBoard.Build(Config(), rows, s => 1, 1, heldCareerId: null);
            Assert.AreEqual(CareerStatus.CanApply, cards.First(c => c.Id == "discord_mod").Status);
            Assert.AreEqual(CareerStatus.SkillGated, cards.First(c => c.Id == "kol_engagement").Status);
            Assert.AreEqual("writing", cards.First(c => c.Id == "kol_engagement").BlockingGate.Skill);
            Assert.AreEqual(CareerStatus.PhaseLocked, cards.First(c => c.Id == "rug_sleuth").Status);
            var held = JobBoard.Build(Config(), rows, s => 1, 1, heldCareerId: "junior_analyst");
            Assert.AreEqual(CareerStatus.Hired, held[0].Status);
            Assert.AreEqual("junior_analyst", held[0].Id);
        }

        [Test]
        public void ContractShapeActiveMeansHeld()
        {
            var rows = CareerRow.ParseList(JToken.Parse(@"[
                {""id"":""discord_mod"",""name"":""Discord Mod"",""level"":2,""xp"":340,""active"":true},
                {""id"":""junior_analyst"",""name"":""Junior Analyst"",""level"":0,""xp"":0,""active"":false}]"), out _);
            Assert.IsTrue(JobBoard.ActiveMeansHeld(rows));
            var cards = JobBoard.Build(Config(), rows, s => 5, 1, heldCareerId: "junior_analyst");
            var mod = cards.First(c => c.Id == "discord_mod");
            Assert.AreEqual(CareerStatus.Hired, mod.Status);
            Assert.AreEqual(2, mod.Level);
            Assert.AreEqual(5500, mod.PayCents, "level 2 pay from config when the row has none");
            Assert.AreEqual(50, mod.ShiftSec);
            Assert.AreEqual(CareerStatus.CanApply, cards.First(c => c.Id == "junior_analyst").Status, "local memory never overrides an explicit server flag");
            Assert.AreEqual(CareerStatus.NotOffered, cards.First(c => c.Id == "kol_engagement").Status);
        }

        [Test]
        public void ServerPayWinsOverConfig()
        {
            var rows = CareerRow.ParseList(JToken.Parse(@"{""careers"":[{""id"":""discord_mod"",""level"":1,""xp"":0,""active"":true,""payCents"":""4100"",""shiftSec"":46},{""id"":""junior_analyst"",""active"":true}]}"), out _);
            var card = JobBoard.Build(Config(), rows, s => 1, 6, "discord_mod").First(c => c.Id == "discord_mod");
            Assert.AreEqual(4100, card.PayCents);
            Assert.AreEqual(46, card.ShiftSec);
        }

        [Test]
        public void UnsafeAndDuplicateRowsAreSkipped()
        {
            var rows = CareerRow.ParseList(JToken.Parse(@"[{""id"":""../admin""},{""id"":""a""},{""id"":""a""},{""name"":""x""}]"), out int bad);
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(3, bad);
            CareerRow.ParseList(JToken.Parse(@"""nope"""), out bad);
            Assert.AreEqual(1, bad);
        }

        [Test]
        public void JobGrantAcceptsBothShapesAndRejectsAnotherCareer()
        {
            Assert.AreEqual(2, JobGrant.Parse(JToken.Parse(@"{""careerId"":""x"",""level"":2,""xp"":10}"), "x").Level);
            Assert.AreEqual(1, JobGrant.Parse(JToken.Parse(@"{""id"":""x"",""level"":1}"), "x").Level);
            Assert.IsNull(JobGrant.Parse(JToken.Parse(@"{""id"":""y"",""level"":1}"), "x"));
            Assert.IsNull(JobGrant.Parse(JToken.Parse("[]"), "x"));
        }
    }

    public class ShiftPlanTests
    {
        private static ShiftTicket Ticket(string json) => ShiftTicket.Parse(JToken.Parse(json));

        private static RegistryGame Registry(string id) => id == "discord_mod_ban"
            ? new RegistryGame { Id = id, CareerId = "discord_mod", MaxScore = 10, DurationSec = 45 }
            : id == "odd_registry_game" ? new RegistryGame { Id = id, CareerId = "discord_mod", MaxScore = 7, DurationSec = 30 } : null;

        [Test]
        public void ParsesIsoAndEpochEndsAt()
        {
            var iso = Ticket(@"{""shiftId"":""0192a1c2-aaaa"",""miniGameIds"":[""discord_mod_ban""],""endsAt"":""2026-10-05T12:01:00.000Z""}");
            Assert.AreEqual(new DateTime(2026, 10, 5, 12, 1, 0, DateTimeKind.Utc), iso.EndsAtUtc);
            var ms = Ticket(@"{""shiftId"":""s1"",""miniGameIds"":[""a""],""endsAt"":1791201660000}");
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1791201660000).UtcDateTime, ms.EndsAtUtc);
            Assert.IsNull(Ticket(@"{""shiftId"":""s1"",""miniGameIds"":[""a""],""endsAt"":12}").EndsAtUtc);
        }

        [Test]
        public void RegistryCapsWinAndCatalogDefaultsFillProposals()
        {
            var t = Ticket(@"{""shiftId"":""s1"",""miniGameIds"":[""discord_mod_ban"",""discord_mod_raid_router""],""endsAt"":""2026-10-05T12:01:00Z""}");
            var plan = ShiftPlan.Build(t, "discord_mod", Registry, GameCatalog.Instance, out var err);
            Assert.IsNull(err);
            Assert.AreEqual(10, plan.Games[0].MaxScore);
            Assert.IsTrue(plan.Games[0].FromRegistry);
            Assert.AreEqual(100, plan.Games[1].MaxScore);
            Assert.IsFalse(plan.Games[1].FromRegistry);
            Assert.AreEqual(90, plan.TotalDuration, 1e-9);
        }

        [Test]
        public void RegisteredButUnknownGamesStillResolve()
        {
            var t = Ticket(@"{""shiftId"":""s1"",""miniGameIds"":[""odd_registry_game""],""endsAt"":""2026-10-05T12:01:00Z""}");
            var plan = ShiftPlan.Build(t, "discord_mod", Registry, GameCatalog.Instance, out var err);
            Assert.IsNull(err);
            Assert.IsNull(plan.Games[0].Spec);
            Assert.AreEqual(7, plan.Games[0].MaxScore);
        }

        [TestCase(@"{""shiftId"":""s1"",""miniGameIds"":[],""endsAt"":""2026-10-05T12:01:00Z""}", "shift.invalidAssignment")]
        [TestCase(@"{""shiftId"":""s1"",""miniGameIds"":[""discord_mod_ban"",""discord_mod_ban""],""endsAt"":""2026-10-05T12:01:00Z""}", "shift.invalidAssignment")]
        [TestCase(@"{""shiftId"":""s/1"",""miniGameIds"":[""discord_mod_ban""],""endsAt"":""2026-10-05T12:01:00Z""}", "shift.invalidAssignment")]
        [TestCase(@"{""shiftId"":""s1"",""miniGameIds"":[""discord_mod_ban""]}", "shift.invalidAssignment")]
        [TestCase(@"{""shiftId"":""s1"",""miniGameIds"":[""who_knows""],""endsAt"":""2026-10-05T12:01:00Z""}", "shift.unknownGame")]
        [TestCase(@"{""shiftId"":""s1"",""miniGameIds"":[""auditor_report""],""endsAt"":""2026-10-05T12:01:00Z""}", "shift.crossCareer")]
        public void RejectsBadAssignments(string json, string expected)
        {
            Assert.IsNull(ShiftPlan.Build(Ticket(json), "discord_mod", Registry, GameCatalog.Instance, out var err));
            Assert.AreEqual(expected, err);
        }

        [Test]
        public void MalformedRegistryCapsAreRefused()
        {
            Assert.IsNull(GameCatalog.Instance.Resolve("discord_mod_ban", 0, 45, "discord_mod"));
            Assert.IsNull(GameCatalog.Instance.Resolve("discord_mod_ban", 10, 5000, "discord_mod"));
            Assert.IsNull(GameCatalog.Instance.Resolve("nope", null, null, null));
        }
    }

    public class ShiftFlowTests
    {
        private static ShiftPlan Plan(params (string id, int max)[] games) => new ShiftPlan
        {
            ShiftId = "s1", CareerId = "c", EndsAtUtc = DateTime.UtcNow,
            Games = games.Select(g => new ResolvedGame { Id = g.id, CareerId = "c", MaxScore = g.max, Duration = 45 }).ToList(),
        };

        private static Func<string> Keys()
        {
            int n = 0;
            return () => "key-" + (++n);
        }

        [Test]
        public void HappyPathSubmitsExactCappedResultsInOrder()
        {
            var f = new ShiftFlow("c", Keys());
            Assert.IsTrue(f.BeginStart());
            Assert.AreEqual("key-1", f.StartKey);
            Assert.IsTrue(f.Assigned(Plan(("a", 10), ("b", 100))));
            Assert.AreEqual("a", f.CurrentGame.Id);
            Assert.IsNull(f.CompletionBody());
            f.RecordScore(15); // capped to 10
            Assert.AreEqual(ShiftStage.Playing, f.Stage);
            f.RecordScore(-4); // capped to 0
            Assert.AreEqual(ShiftStage.Waiting, f.Stage);
            Assert.IsFalse(f.RecordScore(1));
            var body = f.CompletionBody();
            Assert.AreEqual(@"{""results"":[{""miniGameId"":""a"",""score"":10},{""miniGameId"":""b"",""score"":0}]}", body.ToString(Newtonsoft.Json.Formatting.None));
            Assert.IsTrue(f.BeginSettle());
            Assert.AreEqual("key-2", f.CompletionKey);
            Assert.IsTrue(f.Settled(new ShiftReceipt { PayCents = 2000, Xp = 50 }));
            Assert.IsTrue(f.IsTerminal);
            Assert.IsFalse(f.Cancel());
        }

        [Test]
        public void UnknownOutcomeRetriesWithTheSameKey()
        {
            var f = new ShiftFlow("c", Keys());
            f.BeginStart(); f.Assigned(Plan(("a", 10))); f.RecordScore(5);
            f.BeginSettle();
            string first = f.CompletionKey;
            Assert.IsTrue(f.Unconfirmed("shift.unconfirmed"));
            Assert.IsTrue(f.BeginSettle());
            Assert.AreEqual(first, f.CompletionKey, "a retry after an unknown outcome must replay, not pay twice");
            Assert.AreEqual(2, f.SettleAttempts);
        }

        [Test]
        public void NotFinishedYetGetsANewKey()
        {
            var f = new ShiftFlow("c", Keys());
            f.BeginStart(); f.Assigned(Plan(("a", 10))); f.RecordScore(5);
            f.BeginSettle();
            string first = f.CompletionKey;
            Assert.IsTrue(f.NotFinishedYet());
            Assert.AreEqual(ShiftStage.Waiting, f.Stage);
            f.BeginSettle();
            Assert.AreNotEqual(first, f.CompletionKey);
        }

        [Test]
        public void CancelSubmitsNothingAndCannotInterruptASettle()
        {
            var f = new ShiftFlow("c", Keys());
            f.BeginStart(); f.Assigned(Plan(("a", 10), ("b", 10))); f.RecordScore(5);
            Assert.IsTrue(f.Cancel());
            Assert.IsFalse(f.BeginSettle());
            Assert.IsNull(f.CompletionBody());

            var g = new ShiftFlow("c", Keys());
            g.BeginStart(); g.Assigned(Plan(("a", 10))); g.RecordScore(1); g.BeginSettle();
            Assert.IsFalse(g.Cancel());
            Assert.AreEqual(ShiftStage.Settling, g.Stage);
        }

        [Test]
        public void GuardsAgainstOutOfOrderCalls()
        {
            var f = new ShiftFlow("c", Keys());
            Assert.IsFalse(f.Assigned(Plan(("a", 1))));
            Assert.IsFalse(f.BeginSettle());
            Assert.IsFalse(f.Settled(new ShiftReceipt()));
            Assert.IsFalse(f.Fail("x"));
            f.BeginStart();
            Assert.IsFalse(f.BeginStart());
            Assert.IsFalse(f.Assigned(new ShiftPlan()));
            Assert.IsTrue(f.Fail("shift.failed"));
            Assert.AreEqual("shift.failed", f.ErrorKey);
        }

        [Test]
        public void ReceiptsMustBeComplete()
        {
            Assert.IsNull(ShiftReceipt.Parse(JToken.Parse(@"{""xp"":1}")));
            Assert.IsNull(ShiftReceipt.Parse(JToken.Parse(@"{""payCents"":-1,""xp"":1}")));
            var r = ShiftReceipt.Parse(JToken.Parse(@"{""payCents"":""5500"",""xp"":120,""promoted"":true}"));
            Assert.AreEqual(5500, r.PayCents);
            Assert.IsTrue(r.Promoted);
        }

        [TestCase(40, 45, -99999, false, 5)]
        [TestCase(45, 45, 99999, false, 0)]
        [TestCase(45, 45, 3, true, 3.5)]
        [TestCase(40, 45, -3, true, 5)]
        public void ShiftTimingUsesTheSaferOfBothClocks(double now, double minEnd, double serverLeft, bool synced, double expected)
        {
            Assert.AreEqual(expected, ShiftTiming.SecondsUntilEnd(now, minEnd, serverLeft, synced), 1e-9);
        }

        [Test]
        public void MinimumEndCoversConfigAndPlay()
        {
            Assert.AreEqual(100 + 60 + 0.5, ShiftTiming.MinimumEnd(100, 60, 45), 1e-9);
            Assert.AreEqual(100 + 90 + 0.5, ShiftTiming.MinimumEnd(100, 60, 90), 1e-9);
        }
    }

    public class GigTests
    {
        [Test]
        public void ParsesContractAndBackendShapes()
        {
            var contract = GigRow.ParseList(JToken.Parse(@"[{""id"":""doot_feed"",""name"":""Doot the Feed"",""durationSec"":30,""payCents"":1050,""cooldownSec"":60}]"), out int bad);
            Assert.AreEqual(0, bad);
            Assert.AreEqual("$10.50", contract[0].PayText());
            Assert.AreEqual(60, contract[0].CooldownSec);
            var backend = GigRow.ParseList(JToken.Parse(@"{""gigs"":[{""gigId"":""food_delivery"",""name"":""Food Delivery"",""description"":""d"",""minPayCents"":300,""maxPayCents"":2000}]}"), out bad);
            Assert.AreEqual("food_delivery", backend[0].Id);
            Assert.AreEqual("$3.00–$20.00", backend[0].PayText());
            var junk = GigRow.ParseList(JToken.Parse(@"[{""id"":""x"",""payCents"":-1},{""id"":""a b""},{""id"":""ok""}]"), out bad);
            Assert.AreEqual(1, junk.Count);
            Assert.AreEqual(2, bad);
            Assert.IsNull(junk[0].PayText());
        }

        [Test]
        public void ReceiptsMustMatchTheGig()
        {
            Assert.AreEqual(550, GigReceipt.Parse(JToken.Parse(@"{""payCents"":550,""outcomeId"":""mid_route"",""gigId"":""food_delivery""}"), "food_delivery").PayCents);
            Assert.IsNull(GigReceipt.Parse(JToken.Parse(@"{""payCents"":550,""gigId"":""dog_walking""}"), "food_delivery"));
            Assert.IsNull(GigReceipt.Parse(JToken.Parse(@"{""outcomeId"":""x""}"), "x"));
        }

        [Test]
        public void LedgerRetainsKeysUntilResolvedAndTracksCooldowns()
        {
            int n = 0;
            var l = new GigLedger(() => "k" + (++n));
            Assert.AreEqual("k1", l.KeyFor("g"));
            Assert.AreEqual("k1", l.KeyFor("g"), "unknown outcome keeps the key");
            Assert.IsTrue(l.HasUnconfirmed("g"));
            l.Resolved("g");
            Assert.AreEqual("k2", l.KeyFor("g"));
            Assert.IsTrue(l.Ready("g", 0));
            l.StartCooldown("g", 10, 60);
            Assert.AreEqual(50, l.SecondsLeft("g", 20), 1e-9);
            l.StartCooldown("g", 10, 5); // a shorter cooldown never shortens an existing one
            Assert.AreEqual(50, l.SecondsLeft("g", 20), 1e-9);
            Assert.IsTrue(l.Ready("g", 70));
        }

        [Test]
        public void RetryAfterReadsMsOrSeconds()
        {
            Assert.AreEqual(30, GigLedger.RetryAfterSeconds(JToken.Parse(@"{""retryAfterMs"":30000}")));
            Assert.AreEqual(12, GigLedger.RetryAfterSeconds(JToken.Parse(@"{""retryAfterSec"":12}")));
            Assert.IsNull(GigLedger.RetryAfterSeconds(JToken.Parse("{}")));
            Assert.IsNull(GigLedger.RetryAfterSeconds(null));
        }

        [Test]
        public void EveryKnownGigHasCtFlavour()
        {
            foreach (var id in new[] { "doot_feed", "drop_alpha", "write_thread", "audit_token", "food_delivery", "data_entry", "dog_walking", "ad_clicks", "social_moderation" })
                Assert.IsNotNull(CareerFlavor.GigTagline(id), id);
            Assert.IsNotNull(CareerFlavor.GigOutcome("food_delivery", "long_route"));
            Assert.IsNull(CareerFlavor.GigOutcome("food_delivery", "teleport"));
        }

        [Test]
        public void EveryCareerHasFlavourAndFiveTitles()
        {
            foreach (var id in CatalogTests.ContractCareers.Keys)
            {
                Assert.IsTrue(CareerFlavor.Known(id), id);
                for (int l = 1; l <= 5; l++) Assert.IsFalse(string.IsNullOrEmpty(CareerFlavor.LevelTitle(id, l)), id + l);
            }
        }
    }

    public class WireTests
    {
        [TestCase("abc_DEF-123", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("a/b", false)]
        [TestCase("a b", false)]
        [TestCase("ü", false)]
        public void SafeIds(string id, bool safe) => Assert.AreEqual(safe, CareersWire.IsSafeId(id));

        [Test]
        public void CashFormatsInvariantly()
        {
            Assert.AreEqual("$1,234.56", CareersWire.Cash(123456));
            Assert.AreEqual("-$0.05", CareersWire.Cash(-5));
            Assert.AreEqual("-$92,233,720,368,547,758.08", CareersWire.Cash(long.MinValue));
        }

        [Test]
        public void LongsAcceptStringsAndRejectFractions()
        {
            var o = JToken.Parse(@"{""a"":""42"",""b"":4.0,""c"":4.5,""d"":true}");
            Assert.AreEqual(42, CareersWire.Long(o, "a"));
            Assert.AreEqual(4, CareersWire.Long(o, "b"));
            Assert.IsNull(CareersWire.Long(o, "c"));
            Assert.IsNull(CareersWire.Long(o, "d"));
            Assert.AreEqual(42, CareersWire.Long(o, "missing", "a"));
        }
    }
}

namespace CTLife.Careers.Tests
{
    /// <summary>Verbatim payloads from the local backend (ct-life-backend 9f3e5ef, 2026-10-08, throwaway dev wallet).</summary>
    public class LiveBackendShapeTests
    {
        [Test]
        public void CareersListFromBackend()
        {
            var rows = CareerRow.ParseList(JToken.Parse(@"[{""id"":""discord_mod"",""name"":""Discord Mod"",""level"":1,""xp"":0,""payCents"":4000,""shiftSec"":45,""miniGameId"":""discord_mod_ban"",""active"":true,""phase"":1},{""id"":""junior_analyst"",""name"":""Junior Analyst"",""level"":1,""xp"":0,""payCents"":5000,""shiftSec"":60,""miniGameId"":""junior_analyst_earnings"",""active"":true,""phase"":1},{""id"":""kol_engagement"",""name"":""KOL Engagement"",""level"":1,""xp"":0,""payCents"":4500,""shiftSec"":50,""miniGameId"":""kols_engagement"",""active"":true,""phase"":1}]"), out int bad);
            Assert.AreEqual(0, bad);
            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual("discord_mod_ban", rows[0].MiniGameId);
            Assert.IsFalse(JobBoard.ActiveMeansHeld(rows));
        }

        [Test]
        public void ApplyShiftAndNotFinishedFromBackend()
        {
            Assert.AreEqual("discord_mod", JobGrant.Parse(JToken.Parse(@"{""id"":""discord_mod"",""level"":1}"), "discord_mod").CareerId);
            var ticket = ShiftTicket.Parse(JToken.Parse(@"{""shiftId"":""01a118d6-4588-755d-aafb-e054ca0e1517"",""miniGameIds"":[""discord_mod_ban""],""endsAt"":""2026-10-08T00:08:23.120Z""}"));
            var plan = ShiftPlan.Build(ticket, "discord_mod", _ => null, GameCatalog.Instance, out var err);
            Assert.IsNull(err);
            Assert.AreEqual(10, plan.Games[0].MaxScore, "rc1 registry cap via catalog defaults");
            var error = JToken.Parse(@"{""shiftId"":""01a118d6-4588-755d-aafb-e054ca0e1517"",""endsAt"":1791418103120}");
            Assert.AreEqual(new DateTime(2026, 10, 8, 0, 8, 23, 120, DateTimeKind.Utc), CareersWire.Time(error["endsAt"]));
            Assert.AreEqual(ticket.EndsAtUtc, CareersWire.Time(error["endsAt"]), "both endsAt encodings agree");
        }

        [Test]
        public void GigsFromBackend()
        {
            var gigs = GigRow.ParseList(JToken.Parse(@"{""gigs"":[{""gigId"":""food_delivery"",""name"":""Food Delivery"",""description"":""Deliver orders around the city; pay scales with distance."",""minPayCents"":300,""maxPayCents"":2000},{""gigId"":""ad_clicks"",""name"":""Ad Clicker"",""description"":""Click on ads; pay scales with click count."",""minPayCents"":100,""maxPayCents"":350}]}"), out int bad);
            Assert.AreEqual(0, bad);
            Assert.AreEqual("$1.00–$3.50", gigs[1].PayText());
            var receipt = GigReceipt.Parse(JToken.Parse(@"{""runId"":""01a118d6-6f6b-7064-a27b-cd6c49553e9d"",""gigId"":""food_delivery"",""payCents"":425,""outcomeId"":""short_route"",""startedAt"":""2026-10-08T00:07:48.843Z"",""endedAt"":""2026-10-08T00:08:18.843Z""}"), "food_delivery");
            Assert.AreEqual(425, receipt.PayCents);
            Assert.AreEqual("Quick run across Timeline Square.", CareerFlavor.GigOutcome("food_delivery", receipt.OutcomeId));
        }
    }
}
