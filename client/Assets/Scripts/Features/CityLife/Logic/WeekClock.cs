using System;
using System.Globalization;

namespace CTLife.CityLife
{
    /// <summary>
    /// The CT City week (contract section 1 "Times"): Mon 00:00 to Sun 23:59:59 UTC; Sunday Unlock and the Weekly Meta
    /// rotation at Sun 20:00 UTC; tax and weekly boards reset Mon 00:00 UTC. The schedule is read from config
    /// `economy.sundayUnlockCron` / `economy.mondayResetCron` ("Sun 20:00 UTC"); the contract value is the fallback when
    /// the field is missing or unreadable. Pure; every method takes "now" so tests can pin time.
    /// </summary>
    public static class WeekClock
    {
        public const string ContractUnlock = "Sun 20:00 UTC";
        public const string ContractReset = "Mon 00:00 UTC";

        public readonly struct Weekly
        {
            public readonly DayOfWeek Day;
            public readonly int Hour, Minute;
            public Weekly(DayOfWeek day, int hour, int minute) { Day = day; Hour = hour; Minute = minute; }
            public override string ToString() => Day.ToString().Substring(0, 3) + " " + Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + Minute.ToString("00", CultureInfo.InvariantCulture) + " UTC";
        }

        /// <summary>Parses "Sun 20:00 UTC" (economy.json). Only UTC schedules are accepted.</summary>
        public static bool TryParse(string spec, out Weekly weekly)
        {
            weekly = default;
            if (string.IsNullOrWhiteSpace(spec)) return false;
            var parts = spec.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts.Length > 3) return false;
            if (parts.Length == 3 && !string.Equals(parts[2], "UTC", StringComparison.OrdinalIgnoreCase)) return false;
            if (!TryDay(parts[0], out var day)) return false;
            var hm = parts[1].Split(':');
            if (hm.Length != 2 || !int.TryParse(hm[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h) ||
                !int.TryParse(hm[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m) || h > 23 || m > 59) return false;
            weekly = new Weekly(day, h, m);
            return true;
        }

        public static Weekly ParseOr(string spec, string fallback)
        {
            if (TryParse(spec, out var w)) return w;
            TryParse(fallback, out w);
            return w;
        }

        private static bool TryDay(string s, out DayOfWeek day)
        {
            day = DayOfWeek.Sunday;
            if (s == null || s.Length < 3) return false;
            switch (s.Substring(0, 3).ToLowerInvariant())
            {
                case "sun": day = DayOfWeek.Sunday; return true;
                case "mon": day = DayOfWeek.Monday; return true;
                case "tue": day = DayOfWeek.Tuesday; return true;
                case "wed": day = DayOfWeek.Wednesday; return true;
                case "thu": day = DayOfWeek.Thursday; return true;
                case "fri": day = DayOfWeek.Friday; return true;
                case "sat": day = DayOfWeek.Saturday; return true;
                default: return false;
            }
        }

        /// <summary>The next occurrence strictly after <paramref name="nowUtc"/>.</summary>
        public static DateTimeOffset Next(Weekly w, DateTimeOffset nowUtc)
        {
            var now = nowUtc.ToUniversalTime();
            var today = new DateTimeOffset(now.Year, now.Month, now.Day, w.Hour, w.Minute, 0, TimeSpan.Zero);
            int days = ((int)w.Day - (int)now.DayOfWeek + 7) % 7;
            var candidate = today.AddDays(days);
            return candidate > now ? candidate : candidate.AddDays(7);
        }

        /// <summary>The latest occurrence at or before <paramref name="nowUtc"/>.</summary>
        public static DateTimeOffset Previous(Weekly w, DateTimeOffset nowUtc) => Next(w, nowUtc).AddDays(-7);

        /// <summary>Monday 00:00 UTC of the week containing <paramref name="nowUtc"/>.</summary>
        public static DateTimeOffset WeekStart(DateTimeOffset nowUtc)
        {
            var now = nowUtc.ToUniversalTime();
            int back = ((int)now.DayOfWeek + 6) % 7;
            return new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero).AddDays(-back);
        }

        /// <summary>ISO-8601 week id, e.g. "2026-W41" (matches the backend's weekIso).</summary>
        public static string WeekIso(DateTimeOffset nowUtc)
        {
            var d = nowUtc.UtcDateTime.Date;
            int dayNr = ((int)d.DayOfWeek + 6) % 7;
            var thursday = d.AddDays(3 - dayNr);
            int week = 1 + (thursday.DayOfYear - 1) / 7;
            return thursday.Year.ToString(CultureInfo.InvariantCulture) + "-W" + week.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>"2d 4h", "3h 20m", "12m", "45s", "now". Display only.</summary>
        public static string Countdown(double milliseconds)
        {
            if (double.IsNaN(milliseconds) || milliseconds <= 0) return CityLifeStrings.T("time.now");
            long s = (long)Math.Ceiling(milliseconds / 1000.0);
            long d = s / 86400, h = s % 86400 / 3600, m = s % 3600 / 60;
            if (d > 0) return d + "d " + h + "h";
            if (h > 0) return h + "h " + m + "m";
            if (m > 0) return m + "m";
            return s + "s";
        }

        public static string Countdown(DateTimeOffset target, DateTimeOffset now) => Countdown((target - now).TotalMilliseconds);
    }
}
