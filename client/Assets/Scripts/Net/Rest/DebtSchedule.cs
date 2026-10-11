using System;
using System.Globalization;
using CTLife.Rest.Model;
using Newtonsoft.Json.Linq;

namespace CTLife.Net
{
    /// <summary>
    /// The debt instalment fields GET /me gained in backend eeef49d, now typed on the generated <see cref="Me"/>:
    /// <c>debtDueAt</c> (the next Sunday Unlock, Sun 20:00 UTC) and <c>debtWeeklyDueCents</c> (at most $120, capped at the
    /// debt). Both null with no debt. <see cref="Sanitize"/> runs before the typed decode so an unreadable value reads as
    /// unknown (never as zero, and never as a failed /me); <see cref="From"/> turns the typed fields into epoch ms and cents.
    /// </summary>
    public static class DebtSchedule
    {
        public static (long? dueAtMs, long? weeklyCents) From(Me me)
        {
            if (me == null) return (null, null);
            long? due = null;
            if (me.DebtDueAt is DateTime dt && dt != default)
            {
                var utc = dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime();
                due = new DateTimeOffset(utc).ToUnixTimeMilliseconds();
            }
            long? weekly = me.DebtWeeklyDueCents is long c && c >= 0 ? c : (long?)null;
            return (due, weekly);
        }

        /// <summary>Drops a <c>debtDueAt</c> that is not a date and a <c>debtWeeklyDueCents</c> that is not a whole,
        /// non-negative number, so they decode as null. Everything else is left alone.</summary>
        public static void Sanitize(JObject me)
        {
            if (me == null) return;
            var d = me["debtDueAt"];
            if (d != null && d.Type != JTokenType.Null && d.Type != JTokenType.Date &&
                !(d.Type == JTokenType.String && DateTimeOffset.TryParse((string)d, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _)))
                me["debtDueAt"] = JValue.CreateNull();
            var w = me["debtWeeklyDueCents"];
            if (w != null && w.Type != JTokenType.Null)
            {
                if ((w.Type == JTokenType.Integer || w.Type == JTokenType.String) &&
                    long.TryParse(w.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var cents))
                    me["debtWeeklyDueCents"] = cents;
                else
                    me["debtWeeklyDueCents"] = JValue.CreateNull();
            }
        }
    }
}
