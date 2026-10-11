using System;
using System.Globalization;
using CoreMoney = CTLife.Core.Money;

namespace CTLife.Economy
{
    /// <summary>Display formatting for server money values. Never used to compute an outcome.</summary>
    public static class MoneyFormat
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Cash in int64 cents: 123456 → "$1,234.56".</summary>
        public static string Cash(long cents, bool compact = false) => CoreMoney.Cash(cents, compact);

        /// <summary>PnL style: "+$1.00", "-$1.00", "$0.00".</summary>
        public static string SignedCash(long cents, bool compact = false) => cents > 0 ? "+" + CoreMoney.Cash(cents, compact) : CoreMoney.Cash(cents, compact);

        /// <summary>Basis points: -310 → "-3.10%", 150 → "+1.50%".</summary>
        public static string Bps(long bps, bool signed = true) => CoreMoney.Bps(bps, signed);

        /// <summary>Micro-dollars per whole coin (contract `priceMicros`) → "$0.015000".</summary>
        public static string Micros(long micros) => micros < 0 ? "-" + CoreMoney.Micros(-micros) : CoreMoney.Micros(micros);

        /// <summary>Fictional coin units (6 decimals): "2500000" → "2.5". Falls back to the raw string when invalid.</summary>
        public static string CoinUnits(string units) => TokenUnits.Format(units, TokenUnits.CoinDecimals, 4) ?? units ?? "—";

        /// <summary>A token amount with its symbol, e.g. ("10000000", 6, "USDC") → "10 USDC". Invalid input is shown raw and flagged.</summary>
        public static string Token(string baseUnits, int decimals, string symbol, int maxFraction = -1)
        {
            var f = TokenUnits.Format(baseUnits, decimals, maxFraction);
            return f != null ? f + " " + symbol : (baseUnits ?? "—") + " (raw) " + symbol;
        }

        /// <summary>Seconds → "m:ss" or "h:mm:ss". Negative clamps to "0:00".</summary>
        public static string Countdown(double seconds)
        {
            if (double.IsNaN(seconds) || seconds <= 0) return "0:00";
            long s = (long)Math.Ceiling(seconds);
            long h = s / 3600, m = s % 3600 / 60, sec = s % 60;
            return h > 0 ? string.Format(Inv, "{0}:{1:00}:{2:00}", h, m, sec) : string.Format(Inv, "{0}:{1:00}", m, sec);
        }

        /// <summary>Elapsed seconds → "now", "12s", "3m", "2h", "4d".</summary>
        public static string Ago(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 5) return "now";
            if (seconds < 60) return ((long)seconds).ToString(Inv) + "s";
            if (seconds < 3600) return ((long)(seconds / 60)).ToString(Inv) + "m";
            if (seconds < 86400) return ((long)(seconds / 3600)).ToString(Inv) + "h";
            return ((long)(seconds / 86400)).ToString(Inv) + "d";
        }

        /// <summary>"Dgw3c4GbQ…" style shortening of wallet addresses, signatures and ids.</summary>
        public static string Short(string s, int head = 4, int tail = 4)
        {
            if (string.IsNullOrEmpty(s)) return "—";
            return s.Length <= head + tail + 1 ? s : s.Substring(0, head) + "…" + s.Substring(s.Length - tail);
        }

        /// <summary>Hit rate in [0,1] (TrustScore.hitRate) → "62%".</summary>
        public static string Ratio(double r)
        {
            if (double.IsNaN(r) || double.IsInfinity(r)) return "—";
            return Math.Round(r * 100.0, MidpointRounding.AwayFromZero).ToString("0", Inv) + "%";
        }
    }
}
