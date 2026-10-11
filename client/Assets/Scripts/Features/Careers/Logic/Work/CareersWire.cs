using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Defensive JSON readers for the work endpoints. They accept the contract shape and the variants the backend
    /// checkout (ct-life-backend 9f3e5ef) actually returns (wrapped lists, `id` vs `careerId`, epoch-ms times, numbers
    /// as strings). Malformed values become null/defaults; nothing here throws on server data.
    /// </summary>
    public static class CareersWire
    {
        /// <summary>Server ids that go into URL paths: [A-Za-z0-9_-], 1-128 chars.</summary>
        public static bool IsSafeId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128) return false;
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-')) return false;
            return true;
        }

        public static string Str(JToken t, params string[] keys)
        {
            if (!(t is JObject o)) return null;
            foreach (var k in keys)
            {
                var v = o[k];
                if (v == null || v.Type == JTokenType.Null) continue;
                if (v.Type == JTokenType.String || v.Type == JTokenType.Integer || v.Type == JTokenType.Float) return v.ToString();
            }
            return null;
        }

        public static long? Long(JToken t, params string[] keys)
        {
            if (!(t is JObject o)) return null;
            foreach (var k in keys)
            {
                var v = o[k];
                if (v == null || v.Type == JTokenType.Null) continue;
                if (v.Type == JTokenType.Integer) return v.Value<long>();
                if (v.Type == JTokenType.Float)
                {
                    double d = v.Value<double>();
                    if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 9.007e15) return (long)Math.Round(d);
                    continue;
                }
                if (v.Type == JTokenType.String && long.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)) return p;
            }
            return null;
        }

        public static int? Int(JToken t, params string[] keys)
        {
            var l = Long(t, keys);
            if (!l.HasValue || l.Value < int.MinValue || l.Value > int.MaxValue) return null;
            return (int)l.Value;
        }

        public static bool? Bool(JToken t, params string[] keys)
        {
            if (!(t is JObject o)) return null;
            foreach (var k in keys)
            {
                var v = o[k];
                if (v?.Type == JTokenType.Boolean) return v.Value<bool>();
            }
            return null;
        }

        /// <summary>ISO-8601 (contract) or epoch milliseconds (backend) → UTC.</summary>
        public static DateTime? Time(JToken v)
        {
            if (v == null || v.Type == JTokenType.Null) return null;
            try
            {
                if (v.Type == JTokenType.Date) return v.Value<DateTime>().ToUniversalTime();
                if (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)
                {
                    double ms = v.Value<double>();
                    if (ms < 1e11 || ms > 1e14) return null; // not plausibly epoch ms (1973..5138)
                    return DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime;
                }
                if (v.Type == JTokenType.String)
                {
                    var s = v.ToString();
                    if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var asMs)) return Time(new JValue(asMs));
                    if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
                        return d.UtcDateTime;
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>A bare array, or the first array found under one of `keys` (`{careers: [...]}`, `{gigs: [...]}`).</summary>
        public static JArray List(JToken t, params string[] keys)
        {
            if (t is JArray a) return a;
            if (t is JObject o)
            {
                foreach (var k in keys)
                    if (o[k] is JArray inner) return inner;
                foreach (var p in o.Properties())
                    if (p.Value is JArray any) return any;
            }
            return null;
        }

        public static List<string> Strings(JToken t)
        {
            var list = new List<string>();
            if (t is JArray a)
                foreach (var x in a)
                    if (x != null && x.Type == JTokenType.String) list.Add(x.ToString());
            return list;
        }

        public static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);

        /// <summary>Cash cents for display ("$1,234.56"), invariant culture; display only.</summary>
        public static string Cash(long cents)
        {
            bool neg = cents < 0;
            ulong abs = neg ? (ulong)(-(cents + 1)) + 1UL : (ulong)cents;
            return (neg ? "-$" : "$") + (abs / 100UL).ToString("#,0", CultureInfo.InvariantCulture) + "." + (abs % 100UL).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
