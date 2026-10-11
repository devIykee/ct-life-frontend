using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace CTLife.Net
{
    /// <summary>
    /// The debt instalment fields GET /me gained in backend eeef49d (not in the client's generated Me model yet):
    /// <c>debtDueAt</c> (the next Sunday Unlock, Sun 20:00 UTC, ISO date-time) and <c>debtWeeklyDueCents</c> (at most $120,
    /// capped at the debt). Both null with no debt; anything unreadable reads as unknown, never as zero.
    /// </summary>
    public static class DebtSchedule
    {
        public static (long? dueAtMs, long? weeklyCents) Read(JObject me)
        {
            if (me == null) return (null, null);
            long? due = null, weekly = null;
            var d = me["debtDueAt"];
            if (d != null && d.Type == JTokenType.String &&
                DateTimeOffset.TryParse((string)d, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when))
                due = when.ToUnixTimeMilliseconds();
            else if (d != null && d.Type == JTokenType.Date && d.ToObject<DateTime>() is DateTime dt)
                due = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            var w = me["debtWeeklyDueCents"];
            if (w != null && (w.Type == JTokenType.Integer || w.Type == JTokenType.String) && long.TryParse(w.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) && c >= 0)
                weekly = c;
            return (due, weekly);
        }
    }
}
