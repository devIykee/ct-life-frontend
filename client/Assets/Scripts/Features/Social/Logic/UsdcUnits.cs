using System;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// USDC amounts as contract base-unit strings (6 decimals). Display and input only: the client never adds, scales or
    /// splits money; every amount it shows is a server value (contract section 1, "Token amounts are strings in base units").
    /// </summary>
    public static class UsdcUnits
    {
        public const int Decimals = 6;
        /// <summary>PUT /creator: `ratePerMinUsdc` between 250000 and 50000000 base units (0.25 to 50 USDC).</summary>
        public const long MinRate = 250_000, MaxRate = 50_000_000;
        public const int MinMinutes = 1, MaxMinutes = 60;

        public static bool IsBaseUnits(string s) => !string.IsNullOrEmpty(s) && s.Length <= 30 && s.All(c => c >= '0' && c <= '9');

        /// <summary>"2500000" → "2.50 USDC" (trailing zeros trimmed past 2 decimals). Null/invalid → "-".</summary>
        public static string Format(string baseUnits, bool withSymbol = true)
        {
            if (!IsBaseUnits(baseUnits)) return "-";
            var v = BigInteger.Parse(baseUnits, CultureInfo.InvariantCulture);
            var whole = BigInteger.DivRem(v, BigInteger.Pow(10, Decimals), out var frac);
            var f = frac.ToString(CultureInfo.InvariantCulture).PadLeft(Decimals, '0').TrimEnd('0');
            if (f.Length < 2) f = f.PadRight(2, '0');
            var text = whole.ToString("N0", CultureInfo.InvariantCulture) + "." + f;
            return withSymbol ? text + " USDC" : text;
        }

        /// <summary>User input "2.5" → "2500000". Invariant culture, '.' decimal point, at most 6 decimals, no grouping.</summary>
        public static bool TryParse(string input, out string baseUnits)
        {
            baseUnits = null;
            if (input == null) return false;
            var s = input.Trim();
            if (s.Length == 0 || s.Length > 20) return false;
            if (s.Count(c => c == '.') > 1 || s.Any(c => !(c >= '0' && c <= '9') && c != '.')) return false;
            var parts = s.Split('.');
            var whole = parts[0].Length == 0 ? "0" : parts[0];
            var frac = parts.Length > 1 ? parts[1] : "";
            if (frac.Length > Decimals) return false;
            var digits = (whole + frac.PadRight(Decimals, '0')).TrimStart('0');
            baseUnits = digits.Length == 0 ? "0" : digits;
            return true;
        }

        public static int Compare(string a, string b) =>
            BigInteger.Parse(a, CultureInfo.InvariantCulture).CompareTo(BigInteger.Parse(b, CultureInfo.InvariantCulture));

        /// <summary>Creator rate check for PUT /creator. Returns a SocialStrings key, or null when valid.</summary>
        public static string RateError(string baseUnits)
        {
            if (!IsBaseUnits(baseUnits)) return "creator.rateInvalid";
            if (Compare(baseUnits, MinRate.ToString(CultureInfo.InvariantCulture)) < 0 ||
                Compare(baseUnits, MaxRate.ToString(CultureInfo.InvariantCulture)) > 0) return "creator.rateRange";
            return null;
        }

        public static bool TryMinutes(string input, out int minutes)
        {
            minutes = 0;
            if (string.IsNullOrWhiteSpace(input) || !int.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var m)) return false;
            if (m < MinMinutes || m > MaxMinutes) return false;
            minutes = m;
            return true;
        }
    }

    /// <summary>Cash (game money, int64 cents) input and display for tips.</summary>
    public static class CashInput
    {
        /// <summary>JavaScript's Number.MAX_SAFE_INTEGER: the largest integer the backend parses exactly.</summary>
        public const long MaxSafe = 9007199254740991L;

        /// <summary>"12.5" → 1250. At most 2 decimals, strictly positive.</summary>
        public static bool TryCents(string input, out long cents)
        {
            cents = 0;
            if (input == null) return false;
            var s = input.Trim().TrimStart('$');
            if (s.Length == 0 || s.Length > 18 || s.Any(c => !(c >= '0' && c <= '9') && c != '.')) return false;
            if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) || d <= 0) return false;
            int dot = s.IndexOf('.');
            if (dot >= 0 && s.Length - dot - 1 > 2) return false;
            var c = d * 100m;
            if (c > MaxSafe) return false;
            cents = (long)c;
            return cents > 0;
        }

        public static string Format(long cents)
        {
            var sign = cents < 0 ? "-" : "";
            var abs = Math.Abs((decimal)cents) / 100m;
            return sign + "$" + abs.ToString("N2", CultureInfo.InvariantCulture);
        }
    }
}
