using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace CTLife.Ventures
{
    public enum NameProblem { None, Empty, TooShort, TooLong, BadChars, LooksLikeLink }

    /// <summary>
    /// Client-side checks that mirror the proposal's request schemas (`name` 3-24 characters, `ticker` ^[A-Z0-9]{2,8}$) so the
    /// player gets instant feedback. The server's chat filter is authoritative for "parody names only"; this lane cannot
    /// recognise real brands, so the form says so and a VALIDATION_FAILED reply is shown as-is.
    /// </summary>
    public static class ProjectRules
    {
        public const int NameMin = 3, NameMax = 24, TickerMin = 2, TickerMax = 8;

        /// <summary>Collapses runs of spaces and trims. The result is what is validated and sent.</summary>
        public static string CleanName(string raw)
        {
            if (raw == null) return "";
            var parts = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        public static NameProblem CheckName(string raw)
        {
            var n = CleanName(raw);
            if (n.Length == 0) return NameProblem.Empty;
            if (n.Length < NameMin) return NameProblem.TooShort;
            if (n.Length > NameMax) return NameProblem.TooLong;
            foreach (var c in n)
                if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.' || c == '$' || c == '\'')) return NameProblem.BadChars;
            var lower = n.ToLowerInvariant();
            if (lower.Contains("http") || lower.Contains("www.") || lower.Contains(".com") || lower.Contains(".io") || lower.Contains(".xyz") || lower.Contains(".gg"))
                return NameProblem.LooksLikeLink;
            return NameProblem.None;
        }

        public static string CleanTicker(string raw) => (raw ?? "").Trim().TrimStart('$').ToUpperInvariant();

        public static bool IsTicker(string raw)
        {
            var t = CleanTicker(raw);
            return t.Length >= TickerMin && t.Length <= TickerMax && t.All(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'));
        }

        /// <summary>
        /// Game Cash typed by the player ("12", "12.5", "$1,200.00") to cents. Up to two decimals, positive, within int64.
        /// False for anything else; never rounds.
        /// </summary>
        public static bool TryParseCash(string text, out long cents)
        {
            cents = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.Trim().Replace("$", "").Replace(",", "").Replace("_", "");
            if (s.Length == 0 || s.Length > 18) return false;
            int dot = s.IndexOf('.');
            string whole = dot < 0 ? s : s.Substring(0, dot), frac = dot < 0 ? "" : s.Substring(dot + 1);
            if (whole.Length == 0 && frac.Length == 0) return false;
            if (frac.Length > 2 || !whole.All(char.IsDigit) || !frac.All(char.IsDigit)) return false;
            if (whole.Length == 0) whole = "0";
            frac = frac.PadRight(2, '0');
            if (!long.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out var w) || w > VWire.MaxSafeInteger / 100) return false;
            cents = w * 100 + long.Parse(frac, CultureInfo.InvariantCulture);
            return cents > 0;
        }

        public enum FundProblem { None, Invalid, NotEnoughCash }

        public static FundProblem CheckFund(string text, long myCashCents, out long cents, out long shortfallCents)
        {
            shortfallCents = 0;
            if (!TryParseCash(text, out cents)) return FundProblem.Invalid;
            if (cents > myCashCents) { shortfallCents = cents - myCashCents; return FundProblem.NotEnoughCash; }
            return FundProblem.None;
        }

        public enum HireProblem { None, NoPlayer, BadRole, BadSalary, Self }

        /// <summary>Proposal `HireOfferRequest`: userId (uuid), role, weekly salary in cents &gt; 0.</summary>
        public static HireProblem CheckHire(string userId, string role, string salaryText, string myUserId, out long salaryCents)
        {
            salaryCents = 0;
            if (!Guid.TryParse((userId ?? "").Trim(), out _)) return HireProblem.NoPlayer;
            if (string.Equals(userId.Trim(), myUserId, StringComparison.OrdinalIgnoreCase)) return HireProblem.Self;
            if (Array.IndexOf(ProjectKinds.HireRoles, role) < 0) return HireProblem.BadRole;
            if (!TryParseCash(salaryText, out salaryCents)) return HireProblem.BadSalary;
            return HireProblem.None;
        }

        public enum Standing { Visitor, Member, Founder }

        public static Standing StandingOf(ProjectDetail d, string myUserId)
        {
            if (d?.Project == null || string.IsNullOrEmpty(myUserId)) return Standing.Visitor;
            if (string.Equals(d.Project.OwnerUserId, myUserId, StringComparison.OrdinalIgnoreCase)) return Standing.Founder;
            return d.Members.Any(m => string.Equals(m.UserId, myUserId, StringComparison.OrdinalIgnoreCase)) ? Standing.Member : Standing.Visitor;
        }

        /// <summary>Proposal: fund is for the founder and members; offers, firing and launch are the founder's.</summary>
        public static bool CanFund(Standing s) => s == Standing.Founder || s == Standing.Member;
        public static bool CanManage(Standing s) => s == Standing.Founder;

        public static bool IsDead(string status) => status == "dead" || status == "rugged";

        /// <summary>Equity as a percentage string from basis points: 750 → "7.50%".</summary>
        public static string EquityText(int bps) => (bps / 100.0).ToString("0.##", CultureInfo.InvariantCulture) + "%";

        /// <summary>
        /// The company value a term sheet implies: invested cents / equity. A plain ratio of the two server values, shown as an
        /// estimate ("we invest $X for Y%"); null when equity is zero.
        /// </summary>
        public static long? ImpliedValuationCents(long investCents, int equityBps)
        {
            if (equityBps <= 0 || investCents <= 0) return null;
            var v = (BigInteger)investCents * 10_000 / equityBps;
            return v > long.MaxValue ? (long?)null : (long)v;
        }
    }

    public enum LaunchStage { NoQuote, Quoted, Confirm, Sending, Done, QuoteChanged, Failed }

    /// <summary>
    /// Launch as a state machine: a quote (GET launch-quote) → the player picks a ticker and confirms the exact cost → POST
    /// launch with `quotedCostCents`. A CONFLICT quote_changed returns the new cost and requires a fresh confirmation; nothing
    /// is launched at a cost the player did not see.
    /// </summary>
    public sealed class LaunchFlow
    {
        public LaunchStage Stage { get; private set; } = LaunchStage.NoQuote;
        public LaunchQuote Quote { get; private set; }
        public long ConfirmedCostCents { get; private set; }
        public VError Error { get; private set; }
        public event Action Changed;

        public void SetQuote(LaunchQuote q)
        {
            if (Stage == LaunchStage.Sending) return;
            Quote = q;
            Stage = q == null ? LaunchStage.NoQuote : LaunchStage.Quoted;
            Error = null;
            Changed?.Invoke();
        }

        public enum Gate { Ok, NoQuote, Blocked, BadTicker, NotEnoughTreasury }

        public Gate Check(string ticker)
        {
            if (Quote == null) return Gate.NoQuote;
            if (!Quote.CanLaunch || Quote.Blockers.Count > 0) return Gate.Blocked;
            if (!ProjectRules.IsTicker(ticker)) return Gate.BadTicker;
            if (Quote.TreasuryCents < Quote.LaunchCostCents) return Gate.NotEnoughTreasury;
            return Gate.Ok;
        }

        /// <summary>The player taps "Launch": show the cost to confirm. Only from Quoted / QuoteChanged / Failed.</summary>
        public bool AskConfirm(string ticker)
        {
            if (Stage != LaunchStage.Quoted && Stage != LaunchStage.QuoteChanged && Stage != LaunchStage.Failed) return false;
            if (Check(ticker) != Gate.Ok) return false;
            ConfirmedCostCents = Quote.LaunchCostCents;
            Stage = LaunchStage.Confirm;
            Error = null;
            Changed?.Invoke();
            return true;
        }

        public void Cancel()
        {
            if (Stage == LaunchStage.Confirm) { Stage = LaunchStage.Quoted; Changed?.Invoke(); }
        }

        /// <summary>The body fields for POST launch, or null when not confirmed.</summary>
        public (string ticker, long quotedCostCents)? Request(string ticker) =>
            Stage == LaunchStage.Confirm && ProjectRules.IsTicker(ticker) ? (ProjectRules.CleanTicker(ticker), ConfirmedCostCents) : ((string, long)?)null;

        public bool BeginSend()
        {
            if (Stage != LaunchStage.Confirm) return false;
            Stage = LaunchStage.Sending;
            Changed?.Invoke();
            return true;
        }

        public void Succeeded()
        {
            if (Stage != LaunchStage.Sending) return;
            Stage = LaunchStage.Done;
            Changed?.Invoke();
        }

        public void Failed(VError e)
        {
            if (Stage != LaunchStage.Sending) return;
            Error = e;
            if (e != null && e.Reason == "quote_changed" && e.NewCostCents.HasValue && Quote != null)
            {
                Quote.LaunchCostCents = e.NewCostCents.Value;
                Stage = LaunchStage.QuoteChanged;
            }
            else Stage = LaunchStage.Failed;
            Changed?.Invoke();
        }
    }
}
