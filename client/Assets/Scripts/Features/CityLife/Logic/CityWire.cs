using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    /// <summary>
    /// Tolerant readers for the City Life REST payloads. Pure (no UnityEngine) so the out-of-editor test runner can use them.
    ///
    /// Why tolerant: the contract (openapi.yaml rc1) types cents as JSON integers and returns bare arrays; the backend
    /// checkout (ct-life-backend 9f3e5ef) sends Postgres bigints as strings and wraps lists (`{items: []}`). Every
    /// accepted variance is listed in docs/progress/city-life.md. Anything else throws FormatException and the caller
    /// shows "unexpected response" instead of crashing.
    /// </summary>
    public static class CityWire
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>JavaScript's Number.MAX_SAFE_INTEGER.</summary>
        public const long MaxSafeInteger = 9007199254740991L;

        public static bool IsNull(JToken t) => t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined;

        /// <summary>An int64 from a JSON integer or a decimal-integer string.</summary>
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

        public static double DoubleOr(JToken t, double fallback)
        {
            if (IsNull(t)) return fallback;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) return (double)t;
            if (t.Type == JTokenType.String && double.TryParse(((string)t).Trim(), NumberStyles.Float, Inv, out var d)) return d;
            return fallback;
        }

        public static bool BoolOr(JToken t, bool fallback)
        {
            if (IsNull(t)) return fallback;
            if (t.Type == JTokenType.Boolean) return (bool)t;
            if (t.Type == JTokenType.String && bool.TryParse(((string)t).Trim(), out var b)) return b;
            return fallback;
        }

        /// <summary>A string field; numbers are rendered invariantly. Null when missing.</summary>
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

        /// <summary>REST times are ISO-8601 strings; the backend also sends epoch milliseconds in places. Returns epoch ms.</summary>
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

        /// <summary>Display-safe text: control characters dropped, length capped. UI labels also disable rich text.</summary>
        public static string Plain(JToken t, int limit = 64) => Plain(Str(t), limit);

        public static string Plain(string s, int limit)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return new string(s.Where(c => !char.IsControl(c)).Take(limit).ToArray());
        }

        /// <summary>Server ids that go into URL paths: letters, digits, '-' and '_' only, at most 80 characters.</summary>
        public static bool IsSafeId(string id) =>
            !string.IsNullOrEmpty(id) && id.Length <= 80 && id.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_');

        /// <summary>
        /// Path segment ids that may carry a ':' (the backend's home ids are `home:{uuidv7}`; the contract's roomId example
        /// is `home:0192a1c2-...`). Escaped with Uri.EscapeDataString before use in a path.
        /// </summary>
        public static bool IsSafeRoomId(string id) =>
            !string.IsNullOrEmpty(id) && id.Length <= 120 && id.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == ':' || c == '.');

        public static string Seg(string id)
        {
            if (!IsSafeRoomId(id)) throw new ArgumentException("Unsafe path id");
            return Uri.EscapeDataString(id);
        }
    }
}
