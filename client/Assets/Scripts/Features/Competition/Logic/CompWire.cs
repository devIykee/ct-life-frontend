using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.Competition
{
    /// <summary>
    /// Tolerant readers for the Phase 6 payloads. Pure (no UnityEngine) so the out-of-editor test runner can use them.
    /// The proposal (docs/fe-contract-proposals/phase4-6) is not merged, and the backend on main differs from it in places
    /// (bigints as strings, wrapped lists), so every reader accepts the documented variance and anything else throws
    /// FormatException, which the UI reports as "unexpected response" instead of crashing.
    /// </summary>
    public static class CompWire
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const long MaxSafeInteger = 9007199254740991L;

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

        /// <summary>ISO-8601 string, epoch milliseconds, or a JSON date; returns epoch ms. Null when absent.</summary>
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

        /// <summary>Display-safe text: control characters dropped, length capped.</summary>
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

        /// <summary>A realtime payload (Colyseus decodes maps to dictionaries) as a JObject. Null when it is not a map.</summary>
        public static JObject Obj(object payload)
        {
            switch (payload)
            {
                case null: return null;
                case JObject j: return j;
                case IDictionary<string, object> d:
                    return Build(d.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)));
                case System.Collections.IDictionary any:
                {
                    var pairs = new List<KeyValuePair<string, object>>();
                    foreach (System.Collections.DictionaryEntry e in any) pairs.Add(new KeyValuePair<string, object>(Convert.ToString(e.Key, Inv), e.Value));
                    return Build(pairs);
                }
                default: return null;
            }
        }

        private static JObject Build(IEnumerable<KeyValuePair<string, object>> pairs)
        {
            var o = new JObject();
            foreach (var kv in pairs)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                o[kv.Key] = Tok(kv.Value);
            }
            return o;
        }

        private static JToken Tok(object v)
        {
            switch (v)
            {
                case null: return JValue.CreateNull();
                case JToken t: return t;
                case string s: return new JValue(s);
                case bool b: return new JValue(b);
                case sbyte _: case byte _: case short _: case ushort _: case int _: case uint _: case long _:
                    return new JValue(Convert.ToInt64(v, Inv));
                case ulong ul: return ul <= long.MaxValue ? new JValue((long)ul) : new JValue(ul.ToString(Inv));
                case float _: case double _: case decimal _: return new JValue(Convert.ToDouble(v, Inv));
                case IDictionary<string, object> _:
                case System.Collections.IDictionary _:
                    return Obj(v) ?? new JObject();
                case System.Collections.IEnumerable list:
                {
                    var arr = new JArray();
                    foreach (var item in list) arr.Add(Tok(item));
                    return arr;
                }
                default: return new JValue(Convert.ToString(v, Inv));
            }
        }
    }
}
