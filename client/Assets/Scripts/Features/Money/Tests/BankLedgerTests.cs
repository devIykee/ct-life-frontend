using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Economy.Tests
{
    /// <summary>
    /// Backend eeef49d (2026-10-17): POST /bank/repay, GET /ledger and live perp PnL, against the contract and the shapes the
    /// Railway deployment really sent on 2026-10-18 (a probe account: a lottery payout, a 1x perp on BASAI).
    /// </summary>
    public sealed class BankLedgerTests
    {
        [Test]
        public void Repay_ReceiptParses_AndDebtNeverGoesNegative()
        {
            var r = MoneyParse.Repay(JToken.Parse("{\"repaidCents\":12000,\"cashCents\":18000,\"debtCents\":60000}"));
            Assert.AreEqual(12000, r.RepaidCents); Assert.AreEqual(18000, r.CashCents); Assert.AreEqual(60000, r.DebtCents);
            Assert.AreEqual(0, MoneyParse.Repay(JToken.Parse("{\"repaidCents\":\"5\",\"cashCents\":\"0\",\"debtCents\":-3}")).DebtCents);
            Assert.Throws<FormatException>(() => MoneyParse.Repay(JToken.Parse("[]")));
        }

        [Test]
        public void Ledger_RealPage_SignedAmounts_LabelsAndPaging()
        {
            // as Railway sent it, plus one unreadable row (counted, never dropped silently)
            var page = MoneyParse.Ledger(JToken.Parse(@"{""items"":[
                {""id"":""10"",""kind"":""perp_margin"",""amountCents"":-1000,""label"":""Perp opened"",""at"":""2026-10-18T11:35:21.129Z""},
                {""id"":""9"",""kind"":""lottery_payout"",""amountCents"":300000,""label"":""Birth lottery"",""at"":""2026-10-18T11:34:00.587Z""},
                {""kind"":""tip""}],""next"":""9""}"));
            Assert.AreEqual(2, page.Rows.Items.Count);
            Assert.AreEqual(1, page.Rows.Skipped);
            Assert.AreEqual(-1000, page.Rows.Items[0].AmountCents);
            Assert.AreEqual("perp_margin", page.Rows.Items[0].Kind);
            Assert.AreEqual("Birth lottery", page.Rows.Items[1].Label);
            Assert.AreEqual(DateTimeOffset.Parse("2026-10-18T11:34:00.587Z").ToUnixTimeMilliseconds(), page.Rows.Items[1].AtMs);
            Assert.AreEqual("9", page.Next);
            Assert.IsNull(MoneyParse.Ledger(JToken.Parse("{\"items\":[],\"next\":null}")).Next, "the last page");
            Assert.IsNull(MoneyParse.Ledger(JToken.Parse("{\"items\":[],\"next\":\"../me\"}")).Next, "a cursor is a number or nothing");
        }

        [Test]
        public void Perps_OpenPositionsCarryMarkAndUnrealizedPnl_AsRailwaySendsThem()
        {
            var rows = MoneyParse.Perps(JToken.Parse(@"[{""perpId"":""01a14ecb-7d3d-7673-a6b8-92b00838b21b"",""coinId"":""01a12911-23e2-7756-a66d-3dd5476e1d43"",
                ""ticker"":""BASAI"",""side"":""long"",""leverage"":1,""marginCents"":""1000"",""entryMicros"":""9202756012171097"",""liqMicros"":""0"",
                ""status"":""open"",""realizedCents"":null,""closedAt"":null,""createdAt"":""2026-10-18T11:35:21.129Z"",
                ""markMicros"":""9202756012171097"",""unrealizedPnlCents"":""-37""}]"));
            Assert.AreEqual(1, rows.Items.Count);
            var p = rows.Items[0];
            Assert.AreEqual(9202756012171097L, p.MarkMicros, "micros stay exact (10,000 micros = 1 cent)");
            Assert.AreEqual(-37, p.UnrealizedPnlCents);
            Assert.IsNull(p.RealizedCents);
            var closed = MoneyParse.Perp(JToken.Parse(@"{""perpId"":""a1"",""coinId"":""c1"",""ticker"":""X"",""side"":""short"",""leverage"":2,""marginCents"":500,
                ""entryMicros"":1,""liqMicros"":2,""status"":""closed"",""realizedCents"":120}"));
            Assert.AreEqual(120, closed.RealizedCents);
            Assert.IsNull(closed.UnrealizedPnlCents, "closed positions carry realizedCents only");
        }

        [Test]
        public void RepayChips_WeeklyRoundAndAll_CappedSortedAndAffordable()
        {
            var chips = RepayChips.For(debtCents: 72000, weeklyCents: 12000, cashCents: 30000);
            CollectionAssert.AreEqual(new long[] { 2000, 5000, 10000, 12000, 72000 }, chips.Select(c => c.AmountCents).ToArray());
            Assert.AreEqual(RepayChips.Kind.Weekly, chips.Single(c => c.AmountCents == 12000).Kind);
            Assert.AreEqual(RepayChips.Kind.All, chips.Last().Kind);
            Assert.IsFalse(chips.Last().Affordable, "Cash cannot cover all of it");
            Assert.AreEqual(12000, RepayChips.Default(chips).AmountCents, "this week's instalment first");
            // a small debt: the weekly instalment is the whole debt (one "all" chip), nothing affordable with no Cash
            var small = RepayChips.For(3000, 3000, 0);
            CollectionAssert.AreEqual(new long[] { 2000, 3000 }, small.Select(c => c.AmountCents).ToArray());
            Assert.AreEqual(RepayChips.Kind.All, small[1].Kind);
            Assert.IsNull(RepayChips.Default(small));
            Assert.IsEmpty(RepayChips.For(0, null, 5000), "no debt, no chips");
            Assert.AreEqual(5000, RepayChips.Default(RepayChips.For(80000, 12000, 6000)).AmountCents, "the weekly is out of reach: the largest that is not");
        }

        [Test]
        public void LedgerKinds_KnownCodesGetOurTitle_UnknownOnesTheServerLabel()
        {
            Assert.IsTrue(LedgerKinds.Recognised("shift_pay"));
            Assert.AreEqual("ledger.loanRepay", LedgerKinds.TitleKey("loan_repay"));
            Assert.IsNull(LedgerKinds.TitleKey("casino_spin"), "unknown: show the row's label");
            Assert.AreEqual("cash", LedgerKinds.Icon("casino_spin"));
            Assert.AreEqual("perps", LedgerKinds.Icon("perp_close"));
        }

        [Test]
        public void SignedMoney_GreenUpRedDown()
        {
            Assert.AreEqual(SignedMoney.Tone.Up, SignedMoney.ToneOf(1));
            Assert.AreEqual(SignedMoney.Tone.Down, SignedMoney.ToneOf(-1));
            Assert.AreEqual(SignedMoney.Tone.Flat, SignedMoney.ToneOf(0));
            StringAssert.StartsWith("+", SignedMoney.Text(1240));
            StringAssert.StartsWith("−", SignedMoney.Text(-300));
            StringAssert.DoesNotContain("-", SignedMoney.Text(-300), "a real minus sign, once");
        }

        [Test]
        public void NothingToRepay_HasItsOwnMessage()
        {
            var e = new CTLife.Net.ApiException(409, "NOTHING_TO_REPAY", "There is no debt to repay.");
            var err = MoneyErrors.From(e, write: true);
            Assert.AreEqual("err.nothingToRepay", err.Key);
            Assert.IsFalse(err.OutcomeUnknown, "a definite answer");
        }
    }
}
