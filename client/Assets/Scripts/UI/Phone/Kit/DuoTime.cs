using System;
using System.Globalization;

namespace CTLife.UI
{
    /// <summary>iMessage-style time labels for lists and conversation separators (local time, 24-hour like the status bar).</summary>
    public static class DuoTime
    {
        /// <summary>Messages separates bubbles with a centred time when the gap exceeds this.</summary>
        public const long GapMs = 15 * 60 * 1000;

        public static DateTime Local(long epochMs) => DateTimeOffset.FromUnixTimeMilliseconds(epochMs).LocalDateTime;

        /// <summary>Conversation list: "22:38", "Yesterday", "Tue", "12/10/26".</summary>
        public static string ListStamp(DateTime at, DateTime now)
        {
            var days = (now.Date - at.Date).TotalDays;
            if (days <= 0) return at.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (days < 2) return Strings.T("duo.time.yesterday");
            if (days < 7) return at.ToString("ddd", CultureInfo.InvariantCulture);
            return at.ToString("dd/MM/yy", CultureInfo.InvariantCulture);
        }

        /// <summary>Thread separator: "Today 22:31", "Yesterday 09:12", "Tue 14:02", "12 Oct 14:02".</summary>
        public static string ThreadStamp(DateTime at, DateTime now)
        {
            var days = (now.Date - at.Date).TotalDays;
            string time = at.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (days <= 0) return Strings.T("duo.time.today") + " " + time;
            if (days < 2) return Strings.T("duo.time.yesterday") + " " + time;
            if (days < 7) return at.ToString("ddd", CultureInfo.InvariantCulture) + " " + time;
            return at.ToString("d MMM", CultureInfo.InvariantCulture) + " " + time;
        }

        /// <summary>True when a separator belongs before a message (first message, or gap &gt; 15 min).</summary>
        public static bool NeedsSeparator(long previousMs, long currentMs, bool first) => first || currentMs - previousMs > GapMs;
    }
}
