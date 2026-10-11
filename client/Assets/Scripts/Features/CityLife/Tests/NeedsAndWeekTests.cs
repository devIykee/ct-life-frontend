using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.CityLife.Tests
{
    public class NeedsTests
    {
        /// <summary>contract/config/needs.json (1.0.0-rc1), verbatim values.</summary>
        public const string Contract = @"{""version"":""1.0.0-rc1"",""decayPerHour"":{""energy"":4,""hunger"":5,""fun"":6,""hygiene"":3,""sanity"":2},
""minValue"":0,""maxValue"":100,""startValue"":50,""actions"":[
{""id"":""eat"",""raises"":""hunger"",""amount"":30,""where"":""anywhere"",""cooldownSec"":0,""costCents"":0},
{""id"":""sleep"",""raises"":""energy"",""amount"":60,""where"":""anywhere"",""cooldownSec"":0,""costCents"":0,""durationSec"":60},
{""id"":""shower"",""raises"":""hygiene"",""amount"":50,""where"":""anywhere"",""cooldownSec"":0,""costCents"":0},
{""id"":""touch_grass"",""raises"":""sanity"",""amount"":25,""where"":""anywhere"",""cooldownSec"":0,""costCents"":0},
{""id"":""party"",""raises"":""fun"",""amount"":35,""where"":""club_venues"",""clubVenueIds"":[""spaces_studio"",""cabal_lounge"",""dubai_marina""],""cooldownSec"":0,""costCents"":0}],
""clubVenues"":[""spaces_studio"",""cabal_lounge"",""dubai_marina""]}";

        private static NeedsConfig Cfg()
        {
            Assert.IsTrue(NeedsConfig.TryParse(JObject.Parse(Contract), out var c, out var reason), reason);
            return c;
        }

        [Test]
        public void ParsesContractNeeds()
        {
            var c = Cfg();
            Assert.AreEqual(5, c.Actions.Count);
            Assert.AreEqual(6, c.Rate("fun"));
            Assert.AreEqual(60, c.Action("sleep").DurationSec);
            CollectionAssert.AreEqual(new[] { "spaces_studio", "cabal_lounge", "dubai_marina" }, c.Action("party").Venues);
            Assert.AreEqual(NeedsConfig.DefaultLow, c.Low);
            Assert.IsFalse(c.WarningsFromServer);
        }

        [Test]
        public void RejectsBrokenConfig()
        {
            Assert.IsFalse(NeedsConfig.TryParse(null, out _, out _));
            Assert.IsFalse(NeedsConfig.TryParse(JObject.Parse("{\"decayPerHour\":{\"energy\":-1}}"), out _, out var r));
            Assert.AreEqual("decay_invalid", r);
        }

        [Test]
        public void ServerWarningsOverrideDefaults()
        {
            var o = JObject.Parse(Contract);
            o["warnings"] = JObject.Parse("{\"low\":30,\"critical\":15}");
            o["consequences"] = JArray.Parse("[{\"need\":\"energy\",\"below\":10,\"textId\":\"needs.consequence.energy\"},{\"need\":\"nope\",\"textId\":\"x\"}]");
            Assert.IsTrue(NeedsConfig.TryParse(o, out var c, out _));
            Assert.AreEqual(30, c.Low);
            Assert.AreEqual(15, c.Critical);
            Assert.IsTrue(c.WarningsFromServer);
            Assert.AreEqual(1, c.Consequences.Count);
        }

        [Test]
        public void LinearDecayFromServerReading()
        {
            var c = Cfg();
            var s = new NeedsSnapshot(50, 50, 50, 50, 50, atMs: 0);
            var p = NeedsProjection.Project(s, c, nowMs: 2 * 3_600_000.0);
            Assert.AreEqual(42, p[0], 1e-9); // energy 4/h
            Assert.AreEqual(40, p[1], 1e-9); // hunger 5/h
            Assert.AreEqual(38, p[2], 1e-9); // fun 6/h
            Assert.AreEqual(44, p[3], 1e-9); // hygiene 3/h
            Assert.AreEqual(46, p[4], 1e-9); // sanity 2/h
        }

        [Test]
        public void ProjectionClampsAndNeverRunsBackwards()
        {
            var c = Cfg();
            var s = new NeedsSnapshot(5, 100, 50, 50, 50, atMs: 10_000);
            var later = NeedsProjection.Project(s, c, 10_000 + 100 * 3_600_000.0);
            Assert.AreEqual(0, later[0]);
            var earlier = NeedsProjection.Project(s, c, 0);
            Assert.AreEqual(100, earlier[1], "a clock behind the reading does not raise needs");
        }

        [Test]
        public void HoursUntilAndUrgency()
        {
            Assert.AreEqual(5, NeedsProjection.HoursUntil(30, 6, 0), 1e-9);
            Assert.AreEqual(0, NeedsProjection.HoursUntil(10, 6, 25));
            Assert.IsTrue(double.IsPositiveInfinity(NeedsProjection.HoursUntil(30, 0, 0)));
            var c = Cfg();
            Assert.AreEqual(2, NeedsProjection.MostUrgent(new double[] { 40, 40, 40, 50, 50 }, c), "tie → fastest decay (fun 6/h)");
            Assert.AreEqual(4, NeedsProjection.MostUrgent(new double[] { 40, 40, 40, 50, 5 }, c));
            Assert.AreEqual("touch_grass", NeedsProjection.Remedy("sanity", c).Id);
        }

        [Test]
        public void LevelsUseThresholds()
        {
            var c = Cfg();
            Assert.AreEqual(NeedLevel.Ok, c.LevelOf(25));
            Assert.AreEqual(NeedLevel.Low, c.LevelOf(24.9));
            Assert.AreEqual(NeedLevel.Critical, c.LevelOf(9));
            Assert.AreEqual(NeedLevel.Empty, c.LevelOf(0));
        }

        [Test]
        public void WarningTrackerEscalatesOnceAndRearms()
        {
            var c = Cfg();
            var t = new NeedsWarningTracker();
            Assert.IsEmpty(t.Update(new double[] { 50, 50, 50, 50, 50 }, c));
            var w = t.Update(new double[] { 20, 50, 50, 50, 50 }, c);
            Assert.AreEqual(1, w.Count);
            Assert.AreEqual(("energy", NeedLevel.Low), w[0]);
            Assert.IsEmpty(t.Update(new double[] { 19, 50, 50, 50, 50 }, c), "same level, no repeat");
            Assert.AreEqual(NeedLevel.Critical, t.Update(new double[] { 5, 50, 50, 50, 50 }, c)[0].Item2);
            Assert.IsEmpty(t.Update(new double[] { 20, 50, 50, 50, 50 }, c), "improving within warning does not re-warn");
            Assert.IsEmpty(t.Update(new double[] { 60, 50, 50, 50, 50 }, c));
            Assert.AreEqual(1, t.Update(new double[] { 20, 50, 50, 50, 50 }, c).Count, "re-armed after recovery");
        }

        [Test]
        public void PartyIsClubOnly()
        {
            var c = Cfg();
            Assert.AreEqual(ActionGateResult.ClubOnly, NeedsActionGate.Check(c, "party", "home"));
            Assert.AreEqual(ActionGateResult.ClubOnly, NeedsActionGate.Check(c, "party", null));
            Assert.AreEqual(ActionGateResult.Allowed, NeedsActionGate.Check(c, "party", "cabal_lounge"));
            Assert.AreEqual(ActionGateResult.Allowed, NeedsActionGate.Check(c, "sleep", "home"));
            Assert.AreEqual(ActionGateResult.UnknownAction, NeedsActionGate.Check(c, "fly", "home"));
            Assert.AreEqual(ActionGateResult.Unavailable, NeedsActionGate.Check(null, "eat", "home"));
        }

        [Test]
        public void SnapshotValueComparison()
        {
            var a = new NeedsSnapshot(1, 2, 3, 4, 5, 0);
            Assert.IsTrue(a.SameValues(new NeedsSnapshot(1, 2, 3, 4, 5, 99)));
            Assert.IsFalse(a.SameValues(new NeedsSnapshot(1, 2, 3, 4, 6, 0)));
            Assert.IsFalse(a.SameValues(null));
        }
    }

    public class WeekClockTests
    {
        private static DateTimeOffset T(string iso) => DateTimeOffset.Parse(iso, null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime();

        [TestCase("Sun 20:00 UTC", DayOfWeek.Sunday, 20, 0)]
        [TestCase("Mon 00:00 UTC", DayOfWeek.Monday, 0, 0)]
        [TestCase("sat 09:30", DayOfWeek.Saturday, 9, 30)]
        public void Parses(string spec, DayOfWeek day, int h, int m)
        {
            Assert.IsTrue(WeekClock.TryParse(spec, out var w));
            Assert.AreEqual(day, w.Day);
            Assert.AreEqual(h, w.Hour);
            Assert.AreEqual(m, w.Minute);
        }

        [TestCase("Sun 25:00 UTC")]
        [TestCase("Sun 20:00 CET")]
        [TestCase("Someday 10:00")]
        [TestCase("")]
        public void RejectsBadSpecs(string spec) => Assert.IsFalse(WeekClock.TryParse(spec, out _));

        [Test]
        public void FallbackIsContractUnlock()
        {
            var w = WeekClock.ParseOr("garbage", WeekClock.ContractUnlock);
            Assert.AreEqual(DayOfWeek.Sunday, w.Day);
            Assert.AreEqual(20, w.Hour);
            Assert.AreEqual("Sun 20:00 UTC", w.ToString());
        }

        [Test]
        public void NextSundayUnlock()
        {
            var unlock = WeekClock.ParseOr(WeekClock.ContractUnlock, null);
            // 2026-10-08 is a Thursday.
            Assert.AreEqual(T("2026-10-11T20:00:00Z"), WeekClock.Next(unlock, T("2026-10-08T12:00:00Z")));
            Assert.AreEqual(T("2026-10-11T20:00:00Z"), WeekClock.Next(unlock, T("2026-10-11T19:59:59Z")));
            Assert.AreEqual(T("2026-10-18T20:00:00Z"), WeekClock.Next(unlock, T("2026-10-11T20:00:00Z")), "exactly at unlock → next week");
            Assert.AreEqual(T("2026-10-11T20:00:00Z"), WeekClock.Previous(unlock, T("2026-10-12T00:00:00Z")));
        }

        [Test]
        public void WeekStartAndIsoWeek()
        {
            Assert.AreEqual(T("2026-10-05T00:00:00Z"), WeekClock.WeekStart(T("2026-10-08T12:00:00Z")));
            Assert.AreEqual(T("2026-10-05T00:00:00Z"), WeekClock.WeekStart(T("2026-10-11T23:59:59Z")));
            Assert.AreEqual("2026-W41", WeekClock.WeekIso(T("2026-10-08T12:00:00Z")));
            Assert.AreEqual("2026-W53", WeekClock.WeekIso(T("2027-01-01T00:00:00Z")), "2026 has 53 ISO weeks");
            Assert.AreEqual("2026-W01", WeekClock.WeekIso(T("2025-12-29T00:00:00Z")));
        }

        [TestCase(0, "now")]
        [TestCase(-5, "now")]
        [TestCase(45_000, "45s")]
        [TestCase(12 * 60_000, "12m")]
        [TestCase(3 * 3_600_000 + 20 * 60_000, "3h 20m")]
        [TestCase(2 * 86_400_000 + 4 * 3_600_000, "2d 4h")]
        public void Countdown(double ms, string expected) => Assert.AreEqual(expected, WeekClock.Countdown(ms));
    }

    public class WeeklyMetaTests
    {
        private const string Rotation = "{\"weekly\":[{\"weekday\":0,\"bonus\":\"rent_half\"},{\"weekday\":1,\"bonus\":\"trading_fee_half\"},{\"weekday\":2,\"bonus\":\"double_tip\"},{\"weekday\":3,\"bonus\":\"npc_density_double\"},{\"weekday\":4,\"bonus\":\"leverage_cap_plus_2x\"},{\"weekday\":5,\"bonus\":\"cosmetics_sale\"},{\"weekday\":6,\"bonus\":\"alpha_clue_prize_x3\"}]}";

        [Test]
        public void ContractShape()
        {
            var m = MetaView.Parse(JObject.Parse("{\"weekday\":3,\"bonus\":\"npc_density_double\"}"));
            Assert.AreEqual("npc_density_double", m.BonusId);
            Assert.AreEqual(3, m.Slot);
            Assert.AreEqual("Conference week", CityLifeStrings.MetaTitle(m.BonusId));
            Assert.AreEqual("leverage_cap_plus_2x", MetaRotation.Next(m, MetaRotation.Read(JObject.Parse(Rotation))));
        }

        [Test]
        public void BackendKeyValueRowWithContractValue()
        {
            var m = MetaView.Parse(JObject.Parse("{\"key\":\"current\",\"value\":{\"weekday\":6,\"bonus\":\"alpha_clue_prize_x3\"},\"updatedAt\":\"2026-10-04T20:05:00Z\"}"));
            Assert.AreEqual("alpha_clue_prize_x3", m.BonusId);
            Assert.IsNotNull(m.UpdatedAtMs);
            Assert.AreEqual("rent_half", MetaRotation.Next(m, MetaRotation.Read(JObject.Parse(Rotation))), "slot 6 wraps to 0");
        }

        [Test]
        public void BackendWorkerShapeHasNoBonus()
        {
            var m = MetaView.Parse(JObject.Parse("{\"key\":\"current\",\"value\":{\"weekIso\":\"2026-W40\",\"winnersCount\":12,\"losersCount\":30,\"perkCents\":500,\"generatedAt\":\"2026-10-04T20:05:00Z\"},\"updatedAt\":\"2026-10-04T20:05:00Z\"}"));
            Assert.IsFalse(m.HasBonus);
            Assert.AreEqual("2026-W40", m.WeekIso);
            Assert.AreEqual(500, m.PerkCents);
            Assert.AreEqual(12, m.Winners);
            Assert.IsNull(MetaRotation.Next(m, MetaRotation.Read(JObject.Parse(Rotation))), "unknown slot → no guess");
        }

        [Test]
        public void SlotFoundByBonusWhenMissing()
        {
            var m = MetaView.Parse(JObject.Parse("{\"bonus\":\"double_tip\"}"));
            Assert.AreEqual(2, MetaRotation.SlotOf(m, MetaRotation.Read(JObject.Parse(Rotation))));
            Assert.AreEqual("Unknown Bonus", CityLifeStrings.MetaTitle("unknown_bonus"), "unknown ids fall back readably");
            Assert.Throws<FormatException>(() => MetaView.Parse(new JArray()));
        }

        [TestCase(-600, MoodTier.Capitulation)]
        [TestCase(-500, MoodTier.Capitulation)]
        [TestCase(-201, MoodTier.Red)]
        [TestCase(-200, MoodTier.Crab)]
        [TestCase(200, MoodTier.Crab)]
        [TestCase(201, MoodTier.Green)]
        [TestCase(500, MoodTier.Euphoria)]
        public void MoodTiers(int bps, MoodTier tier) => Assert.AreEqual(tier, MarketMood.Tier(bps));

        [Test]
        public void DoomposterRuleFromSpec()
        {
            Assert.IsTrue(MarketMood.DoomposterOut(-201));
            Assert.IsFalse(MarketMood.DoomposterOut(-200));
        }
    }

    public class DailyClueTests
    {
        private static DailyClue Clue(long? expires = 10_000) => new DailyClue { Text = "Which chain's tribalists argue in the Ratio Arena?", ExpiresAtMs = expires };

        [Test]
        public void ParsesContractAndBackend()
        {
            var c = DailyClue.Parse(JObject.Parse("{\"clueText\":\"gm\",\"expiresAt\":\"2026-10-09T00:00:00Z\"}"));
            Assert.AreEqual("gm", c.Text);
            Assert.AreEqual(-1, c.PrizeCents);
            var b = DailyClue.Parse(JObject.Parse("{\"date\":\"2026-10-08\",\"clueText\":\"gm\",\"expiresAt\":\"2026-10-09T00:00:00Z\",\"prizeCents\":5000}"));
            Assert.AreEqual("2026-10-08", b.DateUtc);
            Assert.AreEqual(5000, b.PrizeCents);
            Assert.Throws<FormatException>(() => DailyClue.Parse(JObject.Parse("{\"clueText\":\"  \"}")));
            Assert.IsTrue(ClueResult.Parse(JObject.Parse("{\"correct\":true,\"prizeCents\":5000}")).Correct);
            Assert.Throws<FormatException>(() => ClueResult.Parse(JObject.Parse("{\"prizeCents\":1}")));
        }

        [Test]
        public void NormalizesAndSendsBothFieldNames()
        {
            Assert.AreEqual("send it", DailyClueFlow.Normalize("  send \t\n it "));
            Assert.AreEqual(DailyClueFlow.MaxAnswerLength, DailyClueFlow.Normalize(new string('a', 500)).Length);
            var body = DailyClueFlow.Body("x");
            Assert.AreEqual("x", (string)body["answer"]);
            Assert.AreEqual("x", (string)body["attempt"]);
        }

        [Test]
        public void ReadyCorrectFlow()
        {
            var f = new DailyClueFlow(Keys.Seq());
            Assert.IsTrue(f.BeginLoad());
            f.Loaded(Clue(), nowMs: 0, answeredLocally: false);
            Assert.AreEqual(ClueStage.Ready, f.Stage);
            Assert.IsNull(f.BeginSubmit("   ", 0));
            Assert.AreEqual("k1", f.BeginSubmit("Solana", 0));
            f.Answered(new ClueResult { Correct = true, PrizeCents = 5000 });
            Assert.AreEqual(ClueStage.Correct, f.Stage);
            Assert.IsFalse(f.KeyRetained);
        }

        [Test]
        public void LoadOutcomes()
        {
            var f = new DailyClueFlow();
            f.BeginLoad(); f.Loaded(Clue(expires: 5), 10, false);
            Assert.AreEqual(ClueStage.Expired, f.Stage);
            f.BeginLoad(); f.Loaded(Clue(), 0, answeredLocally: true);
            Assert.AreEqual(ClueStage.AlreadyAnswered, f.Stage);
            f.BeginLoad(); f.LoadFailed(Keys.Api(404, "NOT_FOUND", write: false));
            Assert.AreEqual(ClueStage.NoClue, f.Stage);
            f.BeginLoad(); f.LoadFailed(Keys.Api(404, "HTTP_404", write: false));
            Assert.AreEqual(ClueStage.Unavailable, f.Stage);
            f.BeginLoad(); f.LoadFailed(Keys.Network());
            Assert.AreEqual(ClueStage.Error, f.Stage);
        }

        [Test]
        public void BackendOneAttemptRule()
        {
            var f = new DailyClueFlow(Keys.Seq());
            f.BeginLoad(); f.Loaded(Clue(), 0, false);
            f.BeginSubmit("x", 0);
            f.SubmitFailed(Keys.Api(403, "FORBIDDEN", JObject.Parse("{\"reason\":\"already_attempted\"}")));
            Assert.AreEqual(ClueStage.AlreadyAnswered, f.Stage);
        }

        [Test]
        public void UnknownOutcomeOnlyResendsSameAnswerWithSameKey()
        {
            var f = new DailyClueFlow(Keys.Seq());
            f.BeginLoad(); f.Loaded(Clue(), 0, false);
            Assert.AreEqual("k1", f.BeginSubmit("Solana", 0));
            f.SubmitFailed(Keys.Network());
            Assert.AreEqual(ClueStage.Unknown, f.Stage);
            Assert.IsNull(f.BeginSubmit("Ethereum", 0), "a different answer would be a second attempt");
            Assert.AreEqual("k1", f.BeginSubmit("  Solana ", 0));
            f.Answered(new ClueResult { Correct = false });
            Assert.AreEqual(ClueStage.Wrong, f.Stage);
        }

        [Test]
        public void ValidationRejectionLetsPlayerRetry()
        {
            var f = new DailyClueFlow(Keys.Seq());
            f.BeginLoad(); f.Loaded(Clue(), 0, false);
            f.BeginSubmit("x", 0);
            f.SubmitFailed(Keys.Api(400, "VALIDATION_FAILED"));
            Assert.AreEqual(ClueStage.Ready, f.Stage);
            Assert.AreEqual("k2", f.BeginSubmit("y", 0));
        }

        [Test]
        public void ExpiredClueCannotBeAnswered()
        {
            var f = new DailyClueFlow(Keys.Seq());
            f.BeginLoad(); f.Loaded(Clue(expires: 100), 0, false);
            Assert.IsFalse(f.CanSubmit("x", 200));
        }
    }

    public class WeekRecapTests
    {
        [Test]
        public void SinceLastCheckAndShareCard()
        {
            var before = new WeekSnapshot { WeekIso = "2026-W40", HomeId = "parents_basement", CashCents = 100000, DebtCents = 72000 };
            var meta = new MetaView { BonusId = "rent_half" };
            var rent = new RentView(RentState.Paid, true, 0, 2000, 0, -1);
            var lines = WeekRecap.Lines(before, 150000, 60000, "hacker_house", "Hacker House", meta, -350, rent);
            CollectionAssert.Contains(lines, "Cash up $500.00");
            CollectionAssert.Contains(lines, "Debt paid down $120.00");
            CollectionAssert.Contains(lines, "Moved to Hacker House");
            CollectionAssert.Contains(lines, "Meta: Landlord got rugged");
            CollectionAssert.Contains(lines, "Mood: " + CityLifeStrings.T("mood.red") + " (-3.50%)");
            CollectionAssert.Contains(lines, "Rent: Paid up");
            var card = WeekRecap.Card("anon", "2026-W41", lines);
            StringAssert.StartsWith("anon · CT City 2026-W41", card.Text());
            StringAssert.Contains("• Cash up $500.00", card.Text());
        }

        [Test]
        public void FirstCheckIn()
        {
            var lines = WeekRecap.Lines(null, 1, 0, "studio", null, null, 0, new RentView(RentState.Unknown, false, 0, -1, 0, -1));
            Assert.AreEqual(CityLifeStrings.T("recap.first"), lines[0]);
            Assert.AreEqual(2, lines.Count, "first line + mood; no meta, no unknown rent");
        }

        [Test]
        public void SnapshotRoundTripAndGarbage()
        {
            var s = new WeekSnapshot { WeekIso = "2026-W41", HomeId = "studio", CashCents = 5, DebtCents = 0, AtMs = 1 };
            var back = WeekSnapshot.FromJson(s.ToJson());
            Assert.AreEqual("2026-W41", back.WeekIso);
            Assert.AreEqual(5, back.CashCents);
            Assert.IsNull(WeekSnapshot.FromJson(JObject.Parse("{\"weekIso\":\"x\",\"cashCents\":\"abc\"}")));
            Assert.IsNull(WeekSnapshot.FromJson(null));
        }
    }
}
