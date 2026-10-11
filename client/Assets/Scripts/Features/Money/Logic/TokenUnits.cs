using System.Globalization;
using System.Numerics;
using System.Text;

namespace CTLife.Economy
{
    /// <summary>
    /// Token amounts are strings in base units, always (contract section 1): USDC 6, SOL 9 (lamports), ORE 11,
    /// SKR from economy.json (5), fictional coins 6. Everything here is exact BigInteger arithmetic on those strings.
    /// Display only: the client never derives a money outcome from these values.
    /// </summary>
    public static class TokenUnits
    {
        public const int UsdcDecimals = 6;
        public const int SolDecimals = 9;
        public const int OreDecimals = 11;
        public const int CoinDecimals = 6;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>An optional '-' followed by 1..80 ASCII digits. "1.5", "", "1e9", " 1" are not base units.</summary>
        public static bool IsBaseUnits(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 81) return false;
            int i = s[0] == '-' ? 1 : 0;
            if (i == s.Length) return false;
            for (; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }

        /// <summary>
        /// "1500000" with 6 decimals → "1.5"; grouping adds thousands separators to the whole part. With
        /// <paramref name="maxFraction"/> smaller than the decimals, the fraction is truncated (never rounded up); a non-zero
        /// value that truncates to zero shows as "&lt;0.0001". Returns null when the input is not base units.
        /// </summary>
        public static string Format(string baseUnits, int decimals, int maxFraction = -1, bool grouping = true)
        {
            if (!IsBaseUnits(baseUnits) || decimals < 0 || decimals > 30) return null;
            var v = BigInteger.Parse(baseUnits, NumberStyles.AllowLeadingSign, Inv);
            bool neg = v.Sign < 0;
            if (neg) v = -v;
            var scale = BigInteger.Pow(10, decimals);
            var whole = BigInteger.DivRem(v, scale, out var frac);
            int keep = maxFraction < 0 || maxFraction > decimals ? decimals : maxFraction;
            string fracText = decimals == 0 ? "" : frac.ToString(Inv).PadLeft(decimals, '0').Substring(0, keep).TrimEnd('0');
            if (whole.IsZero && fracText.Length == 0 && !frac.IsZero)
                return (neg ? "-" : "") + "<" + (keep == 0 ? "1" : "0." + new string('0', keep - 1) + "1");
            var sb = new StringBuilder();
            if (neg && !(whole.IsZero && fracText.Length == 0)) sb.Append('-');
            sb.Append(grouping ? Group(whole.ToString(Inv)) : whole.ToString(Inv));
            if (fracText.Length > 0) sb.Append('.').Append(fracText);
            return sb.ToString();
        }

        /// <summary>
        /// User input "1.25" with 11 decimals → "125000000000". Accepts digits with one optional '.', no sign, no exponent,
        /// no grouping, at most <paramref name="decimals"/> fraction digits, and a strictly positive result.
        /// </summary>
        public static bool TryParse(string input, int decimals, out string baseUnits)
        {
            baseUnits = null;
            if (input == null || decimals < 0 || decimals > 30) return false;
            var s = input.Trim();
            if (s.Length == 0 || s.Length > 60) return false;
            int dot = s.IndexOf('.');
            string w = dot < 0 ? s : s.Substring(0, dot);
            string f = dot < 0 ? "" : s.Substring(dot + 1);
            if (dot >= 0 && f.IndexOf('.') >= 0) return false;
            if (w.Length == 0 && f.Length == 0) return false;
            foreach (var c in w) if (c < '0' || c > '9') return false;
            foreach (var c in f) if (c < '0' || c > '9') return false;
            if (f.Length > decimals) return false;
            var value = BigInteger.Parse((w.Length == 0 ? "0" : w) + f.PadRight(decimals, '0'), Inv);
            if (value.Sign <= 0) return false;
            baseUnits = value.ToString(Inv);
            return true;
        }

        /// <summary>Exact comparison of two base-unit strings; invalid input compares as null (returns null).</summary>
        public static int? Compare(string a, string b)
        {
            if (!IsBaseUnits(a) || !IsBaseUnits(b)) return null;
            return BigInteger.Parse(a, NumberStyles.AllowLeadingSign, Inv).CompareTo(BigInteger.Parse(b, NumberStyles.AllowLeadingSign, Inv));
        }

        /// <summary>floor(units × percent / 100) for "sell 25 %" style quantity pickers. Null on invalid input.</summary>
        public static string Fraction(string units, int percent)
        {
            if (!IsBaseUnits(units) || percent <= 0 || percent > 100) return null;
            var v = BigInteger.Parse(units, NumberStyles.AllowLeadingSign, Inv);
            if (v.Sign <= 0) return null;
            return (v * percent / 100).ToString(Inv);
        }

        public static bool IsPositive(string units) => IsBaseUnits(units) && units[0] != '-' && units.TrimStart('0').Length > 0;

        private static string Group(string digits)
        {
            if (digits.Length <= 3) return digits;
            var sb = new StringBuilder(digits.Length + digits.Length / 3);
            int first = digits.Length % 3;
            if (first > 0) sb.Append(digits, 0, first);
            for (int i = first; i < digits.Length; i += 3)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(digits, i, 3);
            }
            return sb.ToString();
        }
    }
}
