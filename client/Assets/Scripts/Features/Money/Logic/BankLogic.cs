using System;
using System.Collections.Generic;

namespace CTLife.Economy
{
    /// <summary>
    /// Repay sheet chips (backend eeef49d), pure and tested: this week's instalment, a few round amounts below the debt,
    /// and everything owed. Every chip is capped at the debt (the server caps overpayment the same way), sorted, de-duplicated,
    /// and marked unaffordable when Cash cannot cover it (the server would answer 402 INSUFFICIENT_CASH).
    /// </summary>
    public static class RepayChips
    {
        public enum Kind { Weekly, Fixed, All }

        public sealed class Chip
        {
            public Kind Kind;
            public long AmountCents;
            public bool Affordable;
        }

        public static readonly long[] Round = { 2000, 5000, 10000, 50000 };

        public static List<Chip> For(long debtCents, long? weeklyCents, long cashCents)
        {
            var chips = new List<Chip>();
            if (debtCents <= 0) return chips;
            void Add(Kind k, long amount)
            {
                amount = Math.Min(amount, debtCents);
                if (amount <= 0) return;
                foreach (var c in chips) if (c.AmountCents == amount) { if (k == Kind.All || k == Kind.Weekly) c.Kind = k; return; }
                chips.Add(new Chip { Kind = k, AmountCents = amount, Affordable = amount <= cashCents });
            }
            if (weeklyCents.HasValue && weeklyCents.Value > 0) Add(Kind.Weekly, weeklyCents.Value);
            foreach (var r in Round) if (r < debtCents) Add(Kind.Fixed, r);
            Add(Kind.All, debtCents);
            chips.Sort((a, b) => a.AmountCents.CompareTo(b.AmountCents));
            // keep the list short: the weekly and "all" chips, plus up to three round amounts
            int fixedLeft = 3;
            chips.RemoveAll(c => c.Kind == Kind.Fixed && fixedLeft-- <= 0);
            return chips;
        }

        /// <summary>The chip picked when the sheet opens: this week's instalment if Cash covers it, else the largest affordable.</summary>
        public static Chip Default(List<Chip> chips)
        {
            Chip best = null;
            foreach (var c in chips) if (c.Kind == Kind.Weekly && c.Affordable) return c;
            foreach (var c in chips) if (c.Affordable) best = c;
            return best;
        }
    }

    /// <summary>How a Cash ledger row reads: our title and icon for reason codes we know, the server's label otherwise.</summary>
    public static class LedgerKinds
    {
        private static readonly Dictionary<string, (string key, string icon)> Known = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["shift_pay"] = ("ledger.shiftPay", "jobs"), ["gig_pay"] = ("ledger.gigPay", "jobs"),
            ["tip"] = ("ledger.tip", "cash"), ["tip_out"] = ("ledger.tipOut", "cash"),
            ["trade_buy"] = ("ledger.tradeBuy", "pulse"), ["trade_sell"] = ("ledger.tradeSell", "pulse"),
            ["perp_margin"] = ("ledger.perpMargin", "perps"), ["perp_close"] = ("ledger.perpClose", "perps"),
            ["rent"] = ("ledger.rent", "home"), ["topup"] = ("ledger.topup", "topup"), ["loan_repay"] = ("ledger.loanRepay", "bank"),
            ["prediction"] = ("ledger.prediction", "predict"), ["prediction_payout"] = ("ledger.predictionPayout", "predict"),
            ["business_pay"] = ("ledger.businessPay", "projects"), ["lottery_payout"] = ("ledger.lottery", "dream"),
        };

        public static bool Recognised(string kind) => kind != null && Known.ContainsKey(kind);

        /// <summary>The string key for a recognised kind, else null (show the row's label).</summary>
        public static string TitleKey(string kind) => kind != null && Known.TryGetValue(kind, out var k) ? k.key : null;

        public static string Icon(string kind) => kind != null && Known.TryGetValue(kind, out var k) ? k.icon : "cash";
    }

    /// <summary>Signed money as a phone shows it: "+$12.40" in green, "−$3.00" in red, "$0.00" plain.</summary>
    public static class SignedMoney
    {
        public enum Tone { Up, Down, Flat }

        public static Tone ToneOf(long cents) => cents > 0 ? Tone.Up : cents < 0 ? Tone.Down : Tone.Flat;

        public static string Text(long cents) => cents > 0 ? "+" + MoneyFormat.Cash(cents) : cents < 0 ? "−" + MoneyFormat.Cash(-cents) : MoneyFormat.Cash(0);
    }
}
