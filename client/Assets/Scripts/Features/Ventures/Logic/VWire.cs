using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.Ventures
{
    /// <summary>
    /// Tolerant readers for the Phase 5 REST and realtime payloads (docs/fe-contract-proposals/phase4-6). Pure (no UnityEngine).
    /// The proposal types money as int64 cents or base-unit digit strings, wraps lists (`{bookings}`), and the backend
    /// checkout may differ in small ways, so every reader accepts the documented variants and throws FormatException
    /// for anything else; callers show "unexpected response" instead of crashing.
    /// </summary>
    public static class VWire
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const long MaxSafeInteger = 9007199254740991L;

        public static double DoubleOr(JToken t, double fallback)
        {
            if (IsNull(t)) return fallback;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) return (double)t;
            if (t.Type == JTokenType.String && double.TryParse(((string)t).Trim(), NumberStyles.Float, Inv, out var d)) return d;
            return fallback;
        }

        public static bool IsNull(JToken t) => t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined;

        public static long Long(JToken t)
        {
            if (t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.String) &&
                long.TryParse(t.ToString().Trim(), NumberStyles.AllowLeadingSign, Inv, out var v))
                return v;
            if (t != null && t.Type == JTokenType.Float)
            {
                var d = (double)t;
                if (!double.IsNaN(d) && !double.IsInfinity(d) && Math.Abs(d) <= MaxSafeInteger && Math.Floor(d) == d) return (long)d;
            }
            throw new FormatException("Expected an integer");
        }

        public static long? OptLong(JToken t) => IsNull(t) ? (long?)null : Long(t);

        public static long LongOr(JToken t, long fallback)
        {
            if (IsNull(t)) return fallback;
            try { return Long(t); }
            catch (FormatException) { return fallback; }
        }

        public static int IntOr(JToken t, int fallback)
        {
            var v = LongOr(t, fallback);
            return v > int.MaxValue || v < int.MinValue ? fallback : (int)v;
        }

        public static bool BoolOr(JToken t, bool fallback)
        {
            if (IsNull(t)) return fallback;
            if (t.Type == JTokenType.Boolean) return (bool)t;
            if (t.Type == JTokenType.String && bool.TryParse(((string)t).Trim(), out var b)) return b;
            return fallback;
        }

        public static string Str(JToken t)
        {
            if (IsNull(t)) return null;
            if (t.Type == JTokenType.String) return (string)t;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float || t.Type == JTokenType.Boolean)
                return Convert.ToString(((JValue)t).Value, Inv);
            return null;
        }

        public static string ReqStr(JToken t, string field)
        {
            var s = Str(t);
            if (string.IsNullOrEmpty(s)) throw new FormatException("Missing " + field);
            return s;
        }

        /// <summary>First non-null string among the given keys of an object (`bookingId`, then `id`).</summary>
        public static string FirstStr(JToken row, params string[] keys)
        {
            if (!(row is JObject o)) return null;
            foreach (var k in keys)
            {
                var s = Str(o[k]);
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return null;
        }

        /// <summary>A base-unit amount as a digit string (JSON string of digits, integer, or integral float). Throws otherwise.</summary>
        public static string Units(JToken t)
        {
            if (t != null && t.Type == JTokenType.String)
            {
                var s = ((string)t).Trim();
                if (s.Length > 0 && s.Length <= 40 && s.All(c => c >= '0' && c <= '9')) return s.TrimStart('0').Length == 0 ? "0" : s.TrimStart('0');
                throw new FormatException("Expected base units");
            }
            var v = Long(t);
            if (v < 0) throw new FormatException("Negative amount");
            return v.ToString(Inv);
        }

        public static string UnitsOr(JToken t, string fallback)
        {
            if (IsNull(t)) return fallback;
            try { return Units(t); }
            catch (FormatException) { return fallback; }
        }

        /// <summary>ISO-8601 string or epoch milliseconds → epoch ms. Null when missing.</summary>
        public static long? TimeMs(JToken t)
        {
            if (IsNull(t)) return null;
            if (t.Type == JTokenType.Integer) return Long(t);
            if (t.Type == JTokenType.Date)
            {
                var v = ((JValue)t).Value;
                if (v is DateTimeOffset dto) return dto.ToUnixTimeMilliseconds();
                if (v is DateTime dt)
                    return new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime()).ToUnixTimeMilliseconds();
            }
            if (t.Type == JTokenType.Float)
            {
                var d = (double)t;
                if (double.IsNaN(d) || double.IsInfinity(d)) throw new FormatException("Invalid time");
                return (long)d;
            }
            if (t.Type == JTokenType.String)
            {
                var s = ((string)t).Trim();
                if (long.TryParse(s, NumberStyles.None, Inv, out var ms)) return ms;
                if (DateTimeOffset.TryParse(s, Inv, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when))
                    return when.ToUnixTimeMilliseconds();
            }
            throw new FormatException("Invalid time");
        }

        public static long? TimeMsOrNull(JToken t)
        {
            try { return TimeMs(t); }
            catch (FormatException) { return null; }
        }

        /// <summary>A bare array, or the first array found under one of the wrapper keys.</summary>
        public static JArray List(JToken t, params string[] wrapperKeys)
        {
            if (t is JArray a) return a;
            if (t is JObject o)
                foreach (var k in wrapperKeys)
                    if (o[k] is JArray inner) return inner;
            throw new FormatException("Expected a list");
        }

        /// <summary>Display-safe text: control characters dropped, length capped. UI labels also have rich text off.</summary>
        public static string Plain(JToken t, int limit = 64) => Plain(Str(t), limit);

        public static string Plain(string s, int limit)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return new string(s.Where(c => !char.IsControl(c)).Take(limit).ToArray());
        }

        /// <summary>Server ids that go into URL paths: letters, digits, '-' and '_' only, at most 80 characters.</summary>
        public static bool IsSafeId(string id) =>
            !string.IsNullOrEmpty(id) && id.Length <= 80 && id.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_');

        public static string Seg(string id)
        {
            if (!IsSafeId(id)) throw new ArgumentException("Unsafe path id");
            return Uri.EscapeDataString(id);
        }

        /// <summary>Colyseus delivers realtime payloads as dictionaries/objects; normalise anything to a JToken (null when not convertible).</summary>
        public static JToken ToToken(object o)
        {
            if (o == null) return null;
            if (o is JToken t) return t;
            try
            {
                if (o is string s) return JToken.Parse(s);
                return JToken.FromObject(o);
            }
            catch (JsonException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        /// <summary>"nft_studio" → "Nft studio" for ids with no copy of their own.</summary>
        public static string Readable(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var s = id.Replace('_', ' ').Replace('-', ' ').Trim();
            return s.Length == 0 ? id : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
