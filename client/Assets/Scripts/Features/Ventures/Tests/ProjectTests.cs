using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Ventures.Tests
{
    public sealed class ProjectParseTests
    {
        private const string P = "0192a1c2-7b3e-7c4d-8e5f-6a7b8c9d0e20";
        private const string Founder = "0192a1c2-7b3e-7c4d-8e5f-6a7b8c9d0e00";
        private const string Dev = "0192a1c2-7b3e-7c4d-8e5f-6a7b8c9d0e01";

        private static JObject Summary(string status = "founded") => JObject.Parse(@"{""projectId"":""" + P + @""",""name"":""FrogeSwap"",""kind"":""dex"",""status"":""" + status + @""",
            ""ownerUserId"":""" + Founder + @""",""ownerAlias"":""anon"",""memberCount"":3,""treasuryCents"":250000,""coinId"":null,""openRoles"":[""auditor""],""createdAt"":""2026-10-07T10:00:00.000Z""}");

        private static JObject DetailJson() => JObject.Parse(@"{""project"":" + Summary() + @",
            ""members"":[{""userId"":""" + Founder + @""",""alias"":""anon"",""role"":""founder"",""salaryCentsPerWeek"":0,""joinedAt"":""2026-10-07T10:00:00.000Z""},
                         {""userId"":""" + Dev + @""",""alias"":""devsol"",""role"":""dev"",""salaryCentsPerWeek"":4000,""joinedAt"":""2026-10-07T11:00:00.000Z""},
                         {""alias"":""ghost"",""role"":""mod""}],
            ""payrollCentsPerWeek"":4000,""nextPayrollAt"":""2026-10-14T00:00:00.000Z"",
            ""vcStakes"":[{""vcArchetypeId"":""vc"",""equityBps"":750,""investedCents"":500000,""unlockAt"":""2026-11-07T00:00:00.000Z""}],
            ""recentEvents"":[{""eventId"":""proj_launched"",""projectId"":""" + P + @""",""cashDeltaCents"":-175000,""params"":{""ticker"":""FSWAP""},""at"":""2026-10-07T12:00:00.000Z""},
                              {""eventId"":""proj_founded"",""projectId"":""" + P + @""",""cashDeltaCents"":0,""params"":{},""at"":""2026-10-07T10:00:00.000Z""}]}");

        [Test]
        public void Summary_ReadsTheProposalRow()
        {
            var s = ProjectParse.Summary(Summary());
            Assert.AreEqual("FrogeSwap", s.Name);
            Assert.AreEqual("dex", s.Kind);
            Assert.AreEqual(3, s.MemberCount);
            Assert.AreEqual(250000, s.TreasuryCents);
            Assert.IsNull(s.CoinId);
            CollectionAssert.AreEqual(new[] { "auditor" }, s.OpenRoles);
        }

        [Test]
        public void Summary_AcceptsABackendRowWithIdOnly()
        {
            var s = ProjectParse.Summary(JObject.Parse(@"{""id"":""p-1"",""name"":""X"",""kind"":""memecoin"",""status"":""live"",""founderId"":""u1"",""treasuryCents"":""900""}"));
            Assert.AreEqual("p-1", s.ProjectId);
            Assert.AreEqual("u1", s.OwnerUserId);
            Assert.AreEqual(900, s.TreasuryCents, "bigints may arrive as strings");
        }

        [Test]
        public void Summary_StripsControlCharactersFromNames()
        {
            var j = Summary();
            j["name"] = "Fro\u0000ge\nSwap";
            Assert.AreEqual("FrogeSwap", ProjectParse.Summary(j).Name);
        }

        [Test]
        public void Detail_CurrentBackendCashAndHiresPreserveTreasuryAndMembership()
        {
            var j = JObject.Parse(@"{""project"":{""projectId"":""p-1"",""ownerUserId"":""u1"",""kind"":""dex"",""status"":""founded"",""cashCents"":250000},
                ""hires"":[{""hireId"":""h-1"",""userId"":""u2"",""role"":""dev"",""salaryCentsPerWeek"":4000,""hiredAt"":""2026-10-07T11:00:00.000Z""}]}");
            var d = ProjectParse.Detail(j);
            Assert.AreEqual(250000, d.Project.TreasuryCents, "GET detail used to show zero despite a funded project");
            Assert.AreEqual(VStrings.Kind("dex"), d.Project.Name, "the backend omits the saved name; show its category, not a blank heading");
            Assert.AreEqual(1, d.Project.MemberCount);
            Assert.AreEqual(4000, d.PayrollCentsPerWeek);
            Assert.Greater(d.Members[0].JoinedAtMs, 0);
            Assert.AreEqual(ProjectRules.Standing.Member, ProjectRules.StandingOf(d, "u2"));
        }

        [Test]
        public void Detail_ExplicitServerSummaryWinsOverLegacyHires()
        {
            var j = DetailJson();
            j["hires"] = new JArray(new JObject { ["userId"] = "other", ["salaryCentsPerWeek"] = 99 });
            j["project"]["cashCents"] = 1;
            var d = ProjectParse.Detail(j);
            Assert.AreEqual(250000, d.Project.TreasuryCents);
            Assert.AreEqual(2, d.Members.Count);
            Assert.AreEqual(4000, d.PayrollCentsPerWeek);
        }

        [Test]
        public void Summary_RejectsAnUnsafeId()
        {
            var j = Summary();
            j["projectId"] = "../x";
            Assert.Throws<FormatException>(() => ProjectParse.Summary(j));
            Assert.Throws<FormatException>(() => ProjectParse.Summary(JValue.CreateString("x")));
        }

        [Test]
        public void SummaryPage_CountsUnreadableRows()
        {
            var page = ProjectParse.SummaryPage(new JObject { ["projects"] = new JArray(Summary(), new JObject { ["name"] = "no id" }), ["nextCursor"] = "n" });
            Assert.AreEqual(1, page.Items.Count);
            Assert.AreEqual(1, page.Skipped);
            Assert.AreEqual("n", page.NextCursor);
        }

        [Test]
        public void Detail_ReadsMembersStakesAndEvents_SkippingBadRows()
        {
            var d = ProjectParse.Detail(DetailJson());
            Assert.AreEqual(2, d.Members.Count, "a member without a userId is skipped");
            Assert.AreEqual(4000, d.PayrollCentsPerWeek);
            Assert.IsNotNull(d.NextPayrollAtMs);
            Assert.AreEqual(750, d.VcStakes[0].EquityBps);
            Assert.AreEqual("proj_launched", d.Events[0].EventId, "newest first");
            Assert.AreEqual("FSWAP", d.Events[0].Params["ticker"]);
            Assert.AreEqual(-175000, d.Events[0].CashDeltaCents);
        }

        [Test]
        public void Detail_AcceptsABareProjectRow()
        {
            var d = ProjectParse.Detail(Summary("live"));
            Assert.AreEqual("live", d.Project.Status);
            Assert.AreEqual(0, d.Members.Count);
        }

        [Test]
        public void Standing_AndPermissions()
        {
            var d = ProjectParse.Detail(DetailJson());
            Assert.AreEqual(ProjectRules.Standing.Founder, ProjectRules.StandingOf(d, Founder.ToUpperInvariant()));
            Assert.AreEqual(ProjectRules.Standing.Member, ProjectRules.StandingOf(d, Dev));
            Assert.AreEqual(ProjectRules.Standing.Visitor, ProjectRules.StandingOf(d, "someone-else"));
            Assert.AreEqual(ProjectRules.Standing.Visitor, ProjectRules.StandingOf(d, null));
            Assert.IsTrue(ProjectRules.CanFund(ProjectRules.Standing.Member));
            Assert.IsFalse(ProjectRules.CanManage(ProjectRules.Standing.Member));
            Assert.IsFalse(ProjectRules.CanFund(ProjectRules.Standing.Visitor));
            Assert.IsTrue(ProjectRules.CanManage(ProjectRules.Standing.Founder));
        }

        [Test]
        public void Offer_AndPage()
        {
            var j = JObject.Parse(@"{""offerId"":""o-1"",""projectId"":""" + P + @""",""projectName"":""FrogeSwap"",""fromUserId"":""" + Founder + @""",""toUserId"":""" + Dev + @""",
              ""role"":""dev"",""salaryCentsPerWeek"":4000,""status"":""pending"",""expiresAt"":""2026-10-10T00:00:00.000Z"",""createdAt"":""2026-10-08T00:00:00.000Z""}");
            var o = ProjectParse.Offer(j);
            Assert.AreEqual("dev", o.Role);
            Assert.AreEqual(4000, o.SalaryCentsPerWeek);
            var page = ProjectParse.OfferPage(new JObject { ["offers"] = new JArray(j, new JObject()), ["nextCursor"] = null });
            Assert.AreEqual(1, page.Items.Count);
            Assert.AreEqual(1, page.Skipped);
        }

        [Test]
        public void Quote_ReadsBlockers_AndRejectsANegativeCost()
        {
            var q = ProjectParse.Quote(JObject.Parse(@"{""projectId"":""" + P + @""",""launchCostCents"":175000,""builderDiscountBps"":3000,""treasuryCents"":100000,
              ""canLaunch"":false,""blockers"":[""insufficient_treasury"",""no_dev_hired""],""quotedAt"":""2026-10-08T00:00:00.000Z""}"));
            Assert.AreEqual(175000, q.LaunchCostCents);
            Assert.AreEqual(3000, q.BuilderDiscountBps);
            Assert.IsFalse(q.CanLaunch);
            CollectionAssert.AreEqual(new[] { "insufficient_treasury", "no_dev_hired" }, q.Blockers);
            Assert.Throws<FormatException>(() => ProjectParse.Quote(JObject.Parse(@"{""launchCostCents"":-1}")));
            Assert.Throws<FormatException>(() => ProjectParse.Quote(JObject.Parse(@"{""canLaunch"":true}")), "a quote without a cost is not a quote");
        }

        [Test]
        public void Ticket_RejectsAnythingThatIsNotARoomId()
        {
            var t = ProjectParse.Ticket(JObject.Parse(@"{""roomName"":""project_hq"",""roomId"":""AbC123xyz"",""expiresAt"":""2026-10-08T00:10:00.000Z""}"));
            Assert.AreEqual("project_hq", t.RoomName);
            Assert.Throws<FormatException>(() => ProjectParse.Ticket(JObject.Parse(@"{""roomName"":""project_hq"",""roomId"":""a b""}")));
            Assert.Throws<FormatException>(() => ProjectParse.Ticket(JObject.Parse(@"{""roomName"":""x y"",""roomId"":""abc""}")));
            Assert.Throws<FormatException>(() => ProjectParse.Ticket(JObject.Parse(@"{""roomName"":""project_hq""}")));
        }

        [Test]
        public void VcOffers_AreRangeChecked()
        {
            var good = JObject.Parse(@"{""offerId"":""v-1"",""projectId"":""" + P + @""",""vcArchetypeId"":""vc"",""investCents"":500000,""equityBps"":750,""unlockAt"":""2026-11-07T00:00:00.000Z"",""status"":""pending"",""expiresAt"":""2026-10-10T00:00:00.000Z""}");
            var zeroEquity = (JObject)good.DeepClone();
            zeroEquity["equityBps"] = 0;
            var free = (JObject)good.DeepClone();
            free["investCents"] = 0;
            var offers = ProjectParse.VcOffers(new JObject { ["offers"] = new JArray(good, zeroEquity, free) }, out var skipped);
            Assert.AreEqual(1, offers.Count);
            Assert.AreEqual(2, skipped);
            Assert.AreEqual(500000, offers[0].InvestCents);
        }

        [Test]
        public void VcOffer_IsActionableOnlyWhilePendingAndNotExpired()
        {
            var o = new VcOffer { Status = "pending", ExpiresAtMs = 1000 };
            Assert.IsTrue(o.IsActionable(999));
            Assert.IsFalse(o.IsActionable(1000));
            o.Status = "accepted";
            Assert.IsFalse(o.IsActionable(0));
        }

        [Test]
        public void VcDeals()
        {
            var page = ProjectParse.VcDeals(JObject.Parse(@"{""deals"":[{""projectId"":""" + P + @""",""projectName"":""FrogeSwap"",""vcArchetypeId"":""vc"",""investCents"":500000,""equityBps"":750,""status"":""dumped"",""at"":""2026-10-08T00:00:00.000Z""},{""projectName"":""bad""}],""nextCursor"":""z""}"));
            Assert.AreEqual(1, page.Items.Count);
            Assert.AreEqual(1, page.Skipped);
            Assert.AreEqual("dumped", page.Items[0].Status);
            Assert.AreEqual("z", page.NextCursor);
        }
    }

    public sealed class ProjectRulesTests
    {
        [TestCase("FrogeSwap", NameProblem.None)]
        [TestCase("  Froge   Swap  ", NameProblem.None)]
        [TestCase("$MOON-lab_2.0", NameProblem.None)]
        [TestCase("", NameProblem.Empty)]
        [TestCase("   ", NameProblem.Empty)]
        [TestCase("ab", NameProblem.TooShort)]
        [TestCase("abcdefghijklmnopqrstuvwxy", NameProblem.TooLong)]
        [TestCase("Froge<b>Swap", NameProblem.BadChars)]
        [TestCase("Froge😀Swap", NameProblem.BadChars)]
        [TestCase("froge.com", NameProblem.LooksLikeLink)]
        [TestCase("HTTPS froge", NameProblem.LooksLikeLink)]
        [TestCase("go www.x", NameProblem.LooksLikeLink)]
        public void Name(string raw, NameProblem expected) => Assert.AreEqual(expected, ProjectRules.CheckName(raw));

        [Test]
        public void CleanName_CollapsesSpaces() => Assert.AreEqual("Froge Swap", ProjectRules.CleanName("  Froge \t  Swap "));

        [TestCase("FSWAP", true)]
        [TestCase("fswap", true)]
        [TestCase("$FSWAP", true)]
        [TestCase("A1", true)]
        [TestCase("A", false)]
        [TestCase("ABCDEFGHI", false)]
        [TestCase("AB-C", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void Ticker(string raw, bool ok) => Assert.AreEqual(ok, ProjectRules.IsTicker(raw));

        [Test]
        public void CleanTicker() => Assert.AreEqual("FSWAP", ProjectRules.CleanTicker(" $fswap "));

        [TestCase("250", 25000L)]
        [TestCase("99.5", 9950L)]
        [TestCase("99.50", 9950L)]
        [TestCase("0.01", 1L)]
        [TestCase(".5", 50L)]
        [TestCase("$1,200.00", 120000L)]
        [TestCase("1_000", 100000L)]
        public void Cash_Parses(string text, long cents)
        {
            Assert.IsTrue(ProjectRules.TryParseCash(text, out var c));
            Assert.AreEqual(cents, c);
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("0")]
        [TestCase("0.00")]
        [TestCase("-5")]
        [TestCase("1.234")]
        [TestCase("1.2.3")]
        [TestCase("9999999999999999999")]
        [TestCase("90071992547410")]
        [TestCase(".")]
        [TestCase(null)]
        public void Cash_RefusesEverythingElse(string text) => Assert.IsFalse(ProjectRules.TryParseCash(text, out _), text);

        [Test]
        public void Fund_ChecksAgainstTheCashYouHave()
        {
            Assert.AreEqual(ProjectRules.FundProblem.None, ProjectRules.CheckFund("50", 5000, out var c, out _));
            Assert.AreEqual(5000, c);
            Assert.AreEqual(ProjectRules.FundProblem.NotEnoughCash, ProjectRules.CheckFund("50.01", 5000, out _, out var shortfall));
            Assert.AreEqual(1, shortfall);
            Assert.AreEqual(ProjectRules.FundProblem.Invalid, ProjectRules.CheckFund("x", 5000, out _, out _));
        }

        private const string Uid = "0192a1c2-7b3e-7c4d-8e5f-6a7b8c9d0e01";
        private const string Me = "0192a1c2-7b3e-7c4d-8e5f-6a7b8c9d0e00";

        [Test]
        public void Hire_Validation()
        {
            Assert.AreEqual(ProjectRules.HireProblem.None, ProjectRules.CheckHire(Uid, "dev", "40", Me, out var salary));
            Assert.AreEqual(4000, salary);
            Assert.AreEqual(ProjectRules.HireProblem.NoPlayer, ProjectRules.CheckHire("nobody", "dev", "40", Me, out _));
            Assert.AreEqual(ProjectRules.HireProblem.NoPlayer, ProjectRules.CheckHire("", "dev", "40", Me, out _));
            Assert.AreEqual(ProjectRules.HireProblem.Self, ProjectRules.CheckHire(Me, "dev", "40", Me, out _));
            Assert.AreEqual(ProjectRules.HireProblem.BadRole, ProjectRules.CheckHire(Uid, "ceo", "40", Me, out _));
            Assert.AreEqual(ProjectRules.HireProblem.BadRole, ProjectRules.CheckHire(Uid, "founder", "40", Me, out _), "the founder role cannot be offered");
            Assert.AreEqual(ProjectRules.HireProblem.BadSalary, ProjectRules.CheckHire(Uid, "mod", "0", Me, out _));
            Assert.AreEqual(ProjectRules.HireProblem.None, ProjectRules.CheckHire("  " + Uid.ToUpperInvariant() + " ", "kol", "12.50", Me, out _));
        }

        [Test]
        public void Equity_AndImpliedValuation()
        {
            Assert.AreEqual("7.5%", ProjectRules.EquityText(750));
            Assert.AreEqual("100%", ProjectRules.EquityText(10000));
            Assert.AreEqual("0.01%", ProjectRules.EquityText(1));
            Assert.AreEqual(6_666_666L, ProjectRules.ImpliedValuationCents(500000, 750), "$5,000 for 7.5% implies about $66,666.66");
            Assert.AreEqual(500000L, ProjectRules.ImpliedValuationCents(500000, 10000));
            Assert.IsNull(ProjectRules.ImpliedValuationCents(500000, 0));
            Assert.IsNull(ProjectRules.ImpliedValuationCents(0, 100));
            Assert.IsNull(ProjectRules.ImpliedValuationCents(long.MaxValue / 2, 1), "overflow is not a number");
        }

        [Test]
        public void Dead()
        {
            Assert.IsTrue(ProjectRules.IsDead("dead"));
            Assert.IsTrue(ProjectRules.IsDead("rugged"));
            Assert.IsFalse(ProjectRules.IsDead("paused"));
        }
    }

    public sealed class LaunchFlowTests
    {
        private static LaunchQuote Q(long cost = 175000, long treasury = 250000, bool can = true, string blocker = null)
        {
            var q = new LaunchQuote { ProjectId = "p", LaunchCostCents = cost, TreasuryCents = treasury, CanLaunch = can };
            if (blocker != null) q.Blockers.Add(blocker);
            return q;
        }

        [Test]
        public void NoQuote_NothingToConfirm()
        {
            var f = new LaunchFlow();
            Assert.AreEqual(LaunchFlow.Gate.NoQuote, f.Check("FSWAP"));
            Assert.IsFalse(f.AskConfirm("FSWAP"));
        }

        [Test]
        public void Gates()
        {
            var f = new LaunchFlow();
            f.SetQuote(Q());
            Assert.AreEqual(LaunchFlow.Gate.Ok, f.Check("FSWAP"));
            Assert.AreEqual(LaunchFlow.Gate.BadTicker, f.Check("x"));
            f.SetQuote(Q(can: false, blocker: "no_dev_hired"));
            Assert.AreEqual(LaunchFlow.Gate.Blocked, f.Check("FSWAP"));
            f.SetQuote(Q(treasury: 100));
            Assert.AreEqual(LaunchFlow.Gate.NotEnoughTreasury, f.Check("FSWAP"));
            f.SetQuote(Q(can: true, blocker: "cooldown"));
            Assert.AreEqual(LaunchFlow.Gate.Blocked, f.Check("FSWAP"), "any blocker blocks, whatever canLaunch says");
        }

        [Test]
        public void HappyPath_SendsExactlyTheConfirmedCost()
        {
            var f = new LaunchFlow();
            f.SetQuote(Q());
            Assert.IsNull(f.Request("FSWAP"), "nothing is sent before the player confirms");
            Assert.IsTrue(f.AskConfirm("fswap"));
            var req = f.Request("fswap");
            Assert.AreEqual("FSWAP", req.Value.ticker);
            Assert.AreEqual(175000, req.Value.quotedCostCents);
            Assert.IsTrue(f.BeginSend());
            Assert.IsFalse(f.BeginSend(), "no double send");
            f.Succeeded();
            Assert.AreEqual(LaunchStage.Done, f.Stage);
        }

        [Test]
        public void QuoteChanged_NeedsAFreshConfirmation_AtTheNewCost()
        {
            var f = new LaunchFlow();
            f.SetQuote(Q());
            f.AskConfirm("FSWAP");
            f.BeginSend();
            f.Failed(new VError { Code = "CONFLICT", Reason = "quote_changed", NewCostCents = 180000, Key = "err.quoteChanged" });
            Assert.AreEqual(LaunchStage.QuoteChanged, f.Stage);
            Assert.AreEqual(180000, f.Quote.LaunchCostCents);
            Assert.IsNull(f.Request("FSWAP"), "must confirm again");
            Assert.IsTrue(f.AskConfirm("FSWAP"));
            Assert.AreEqual(180000, f.Request("FSWAP").Value.quotedCostCents);
        }

        [Test]
        public void NewCostAboveTheTreasury_BlocksTheReconfirmation()
        {
            var f = new LaunchFlow();
            f.SetQuote(Q(treasury: 176000));
            f.AskConfirm("FSWAP");
            f.BeginSend();
            f.Failed(new VError { Reason = "quote_changed", NewCostCents = 180000 });
            Assert.IsFalse(f.AskConfirm("FSWAP"));
        }

        [Test]
        public void OtherFailures_AllowAnotherTry()
        {
            var f = new LaunchFlow();
            f.SetQuote(Q());
            f.AskConfirm("FSWAP");
            f.BeginSend();
            f.Failed(new VError { Code = "TIMEOUT", OutcomeUnknown = true });
            Assert.AreEqual(LaunchStage.Failed, f.Stage);
            Assert.IsTrue(f.AskConfirm("FSWAP"));
        }

        [Test]
        public void Cancel_ReturnsToQuoted()
        {
            var f = new LaunchFlow();
            f.SetQuote(Q());
            f.AskConfirm("FSWAP");
            f.Cancel();
            Assert.AreEqual(LaunchStage.Quoted, f.Stage);
            Assert.IsNull(f.Request("FSWAP"));
        }

        [Test]
        public void ANewQuote_DoesNotInterruptASend()
        {
            var f = new LaunchFlow();
            f.SetQuote(Q());
            f.AskConfirm("FSWAP");
            f.BeginSend();
            f.SetQuote(Q(cost: 1));
            Assert.AreEqual(LaunchStage.Sending, f.Stage);
            Assert.AreEqual(175000, f.Quote.LaunchCostCents);
        }
    }
}
