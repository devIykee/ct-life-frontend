using System;
using System.Globalization;
using CTLife.Core;

namespace CTLife.UI
{
    /// <summary>
    /// The debt instalment as the Wallet's Loan card and the lobby's Owe pill say it (backend eeef49d: the next Sunday Unlock,
    /// Sun 20:00 UTC, shown in the player's own time): "$120 due Sun 21:00", "… due today 21:00", "… due tomorrow 21:00",
    /// "… due 26 Oct". Pure (the clock and the time zone are passed in); tested.
    /// </summary>
    public static class DebtDue
    {
        public static string When(long dueAtMs, long nowMs, TimeZoneInfo zone = null)
        {
            zone ??= TimeZoneInfo.Local;
            var due = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(dueAtMs), zone);
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nowMs), zone);
            string time = due.ToString("HH:mm", CultureInfo.InvariantCulture);
            int days = (due.Date - now.Date).Days;
            if (dueAtMs <= nowMs) return Strings.T("debt.overdue");
            if (days == 0) return Strings.F("debt.today", time);
            if (days == 1) return Strings.F("debt.tomorrow", time);
            if (days < 7) return due.ToString("ddd", CultureInfo.InvariantCulture) + " " + time;
            return due.ToString("d MMM", CultureInfo.InvariantCulture);
        }

        public static string When(long dueAtMs, double nowMs) => When(dueAtMs, (long)nowMs);
        public static string Line(long? weeklyCents, long? dueAtMs, double nowMs) => Line(weeklyCents, dueAtMs, (long)nowMs);

        /// <summary>"$120 due Sun 21:00", or null when the server sent no schedule (no debt, or an older backend).</summary>
        public static string Line(long? weeklyCents, long? dueAtMs, long nowMs, TimeZoneInfo zone = null)
        {
            if (!weeklyCents.HasValue || weeklyCents.Value <= 0 || !dueAtMs.HasValue) return null;
            return Strings.F("debt.dueLine", Money.Cash(weeklyCents.Value), When(dueAtMs.Value, nowMs, zone));
        }
    }
}
