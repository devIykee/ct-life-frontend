using System;
using System.Globalization;

namespace CTLife.Core
{
    /// <summary>Formatting for Cash (int64 cents) and basis points. Display only; never compute with the strings.</summary>
    public static class Money
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>123456 → "$1,234.56"; large values compact: "$4.2M".</summary>
        public static string Cash(long cents, bool compact = false)
        {
            bool neg = cents < 0;
            ulong abs = neg ? (ulong)(-(cents + 1)) + 1UL : (ulong)cents;
            string body;
            if (compact && abs >= 100_000_00UL)
            {
                double dollars = abs / 100.0;
                body = dollars >= 1e9 ? (dollars / 1e9).ToString("0.#", Inv) + "B"
                     : dollars >= 1e6 ? (dollars / 1e6).ToString("0.#", Inv) + "M"
                     : (dollars / 1e3).ToString("0.#", Inv) + "K";
            }
            else
            {
                ulong whole = abs / 100UL;
                ulong frac = abs % 100UL;
                body = whole.ToString("#,0", Inv) + "." + frac.ToString("00", Inv);
            }
            return (neg ? "-$" : "$") + body;
        }

        /// <summary>-310 → "-3.10%".</summary>
        public static string Bps(long bps, bool signed = true)
        {
            double pct = bps / 100.0;
            var s = pct.ToString("0.00", Inv) + "%";
            if (signed && bps > 0) s = "+" + s;
            return s;
        }

        /// <summary>Price in micro-dollars per whole coin → "$0.000123" with adaptive precision.</summary>
        public static string Micros(long micros)
        {
            double usd = micros / 1_000_000.0;
            string fmt = usd >= 1 ? "0.00" : usd >= 0.01 ? "0.0000" : "0.000000";
            return "$" + usd.ToString(fmt, Inv);
        }

        public static bool TryParseCents(string s, out long cents)
        {
            cents = 0;
            if (string.IsNullOrEmpty(s)) return false;
            return long.TryParse(s, NumberStyles.Integer, Inv, out cents);
        }
    }
}
