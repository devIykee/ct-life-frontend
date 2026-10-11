using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.Economy
{
    /// <summary>
    /// Tolerant readers for REST payloads. Pure (no UnityEngine), so EditMode tests and the out-of-editor runner can
    /// exercise them.
    ///
    /// Why tolerant: the contract (openapi.yaml rc1) types cents/micros as JSON integers, the backend checkout
    /// (ct-life-backend 9f3e5ef) sends most of them as decimal strings (Postgres bigint), and several lists come wrapped
    /// (`{positions: []}`, `{perps: []}`, `{tombstones: []}`) instead of bare arrays. Every accepted variance is named in
    /// docs/progress/phase2-client.md. Anything else throws FormatException; the caller shows "unexpected response".
    /// </summary>
    public static class Wire
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>JavaScript's Number.MAX_SAFE_INTEGER: the largest integer the backend parses exactly from a JSON number.</summary>
        public const long MaxSafeInteger = 9007199254740991L;

        public static bool IsNull(JToken t) => t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined;

        /// <summary>A signed int64 from a JSON integer or a decimal-integer string. Throws FormatException otherwise.</summary>
        public static long Long(JToken t)
        {
            if (t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.String) &&
                long.TryParse(t.ToString().Trim(), NumberStyles.AllowLeadingSign, Inv, out var v))
                return v;
            throw new FormatException("Expected an integer");
        }

        public static long? OptLong(JToken t) => IsNull(t) ? (long?)null : Long(t);

        public static long LongOr(JToken t, long fallback)
        {
            if (IsNull(t)) return fallback;
            try { return Long(t); }
            catch (FormatException) { return fallback; }
        }

        public static int Int(JToken t) => checked((int)Long(t));

        public static int? OptInt(JToken t) => IsNull(t) ? (int?)null : Int(t);

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

        /// <summary>Token base units (contract: always a string). Accepts a digit string or a JSON integer.</summary>
        public static string Units(JToken t)
        {
            var s = IsNull(t) ? null : t.Type == JTokenType.Integer ? t.ToString() : t.Type == JTokenType.String ? ((string)t).Trim() : null;
            if (!TokenUnits.IsBaseUnits(s)) throw new FormatException("Expected base units");
            return s;
        }

        public static string OptUnits(JToken t) => IsNull(t) ? null : Units(t);

        /// <summary>REST times are ISO-8601 strings; the backend also sends epoch milliseconds in places. Returns epoch ms.</summary>
        public static long? TimeMs(JToken t)
        {
            if (IsNull(t)) return null;
            if (t.Type == JTokenType.Integer) return Long(t);
            if (t.Type == JTokenType.Date)
            {
                // A JToken parsed with Newtonsoft's default DateParseHandling (RestClient uses ParseRaw, which keeps strings).
                var v = ((JValue)t).Value;
                if (v is DateTimeOffset dto) return dto.ToUnixTimeMilliseconds();
                if (v is DateTime dt) return new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime()).ToUnixTimeMilliseconds();
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
        public static string Plain(JToken t, int limit = 64)
        {
            var s = Str(t) ?? "";
            return new string(s.Where(c => !char.IsControl(c)).Take(limit).ToArray());
        }

        /// <summary>Server ids that go into URL paths: letters, digits, '-' and '_' only.</summary>
        public static bool IsSafeId(string id) =>
            !string.IsNullOrEmpty(id) && id.Length <= 80 && id.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_');

        public static string SafeId(JToken t, string field)
        {
            var s = ReqStr(t, field);
            if (!IsSafeId(s)) throw new FormatException("Invalid " + field);
            return s;
        }
    }
}
