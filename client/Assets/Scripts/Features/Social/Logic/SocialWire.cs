using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// Tolerant readers for the Phase 3 REST payloads. Pure (no UnityEngine) so the EditMode tests and the out-of-editor
    /// runner exercise them.
    ///
    /// Why tolerant: the contract (openapi.yaml rc1) and the backend checkout (ct-life-backend 9f3e5ef) disagree on several
    /// shapes: lists come wrapped (`{spaces: []}`, `{posts: []}`, `{messages: []}`), field names differ (`hostUserId` vs
    /// `hostId`, `createdAt` vs `at`), Space ids are `space:{uuid}` strings instead of UUIDs, and times arrive as ISO strings,
    /// epoch seconds or epoch milliseconds. Every accepted variance is listed in docs/progress/phase3-client.md. Anything
    /// that is still unreadable throws FormatException; the caller shows "unexpected response".
    /// </summary>
    public static class SocialWire
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static bool IsNull(JToken t) => t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined;

        /// <summary>The first present, non-null field of <paramref name="o"/> among <paramref name="names"/>.</summary>
        public static JToken First(JToken o, params string[] names)
        {
            if (!(o is JObject obj)) return null;
            foreach (var n in names)
            {
                var v = obj[n];
                if (!IsNull(v)) return v;
            }
            return null;
        }

        public static string Str(JToken t)
        {
            if (IsNull(t)) return null;
            if (t.Type == JTokenType.String) return (string)t;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float || t.Type == JTokenType.Boolean)
                return Convert.ToString(((JValue)t).Value, Inv);
            if (t.Type == JTokenType.Guid) return t.ToString();
            if (t.Type == JTokenType.Date) return t.ToString(Newtonsoft.Json.Formatting.None).Trim('"');
            return null;
        }

        public static string ReqStr(JToken t, string field)
        {
            var s = Str(t);
            if (string.IsNullOrEmpty(s)) throw new FormatException("Missing " + field);
            return s;
        }

        public static long Long(JToken t)
        {
            if (t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.String) &&
                long.TryParse(t.ToString().Trim(), NumberStyles.AllowLeadingSign, Inv, out var v))
                return v;
            if (t != null && t.Type == JTokenType.Float)
            {
                var d = (double)t;
                if (!double.IsNaN(d) && !double.IsInfinity(d) && Math.Abs(d) < 9e15 && Math.Floor(d) == d) return (long)d;
            }
            throw new FormatException("Expected an integer");
        }

        public static long? OptLong(JToken t)
        {
            if (IsNull(t)) return null;
            try { return Long(t); }
            catch (FormatException) { return null; }
        }

        public static int IntOr(JToken t, int fallback)
        {
            var v = OptLong(t);
            return v.HasValue && v.Value >= int.MinValue && v.Value <= int.MaxValue ? (int)v.Value : fallback;
        }

        public static bool BoolOr(JToken t, bool fallback)
        {
            if (IsNull(t)) return fallback;
            if (t.Type == JTokenType.Boolean) return (bool)t;
            if (t.Type == JTokenType.String && bool.TryParse((string)t, out var b)) return b;
            return fallback;
        }

        /// <summary>
        /// Epoch milliseconds from an ISO-8601 string, an epoch-ms number, or an epoch-seconds number (values below 1e11 are
        /// read as seconds: the backend's LiveKit `expiresAt` is `Math.floor(Date.now() / 1000) + ttl`). Null when absent.
        /// </summary>
        public static long? TimeMs(JToken t)
        {
            if (IsNull(t)) return null;
            if (t.Type == JTokenType.Date)
            {
                var v = ((JValue)t).Value;
                if (v is DateTimeOffset dto) return dto.ToUnixTimeMilliseconds();
                if (v is DateTime dt) return new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime()).ToUnixTimeMilliseconds();
            }
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
            {
                var d = (double)t;
                if (double.IsNaN(d) || double.IsInfinity(d) || d < 0) throw new FormatException("Invalid time");
                return d < 1e11 ? (long)(d * 1000) : (long)d;
            }
            if (t.Type == JTokenType.String)
            {
                var s = ((string)t).Trim();
                if (double.TryParse(s, NumberStyles.Float, Inv, out var n) && s.All(c => char.IsDigit(c) || c == '.'))
                    return n < 1e11 ? (long)(n * 1000) : (long)n;
                if (DateTimeOffset.TryParse(s, Inv, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when))
                    return when.ToUnixTimeMilliseconds();
            }
            throw new FormatException("Invalid time");
        }

        public static string IsoFromMs(long ms) =>
            DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Inv);

        /// <summary>A bare array, or the first array found under one of the wrapper keys.</summary>
        public static JArray List(JToken t, params string[] wrapperKeys)
        {
            if (t is JArray a) return a;
            if (t is JObject o)
                foreach (var k in wrapperKeys)
                    if (o[k] is JArray inner) return inner;
            throw new FormatException("Expected a list");
        }

        /// <summary>A list of ids (strings). Missing → empty. Blank entries are dropped.</summary>
        public static List<string> Ids(JToken t)
        {
            var list = new List<string>();
            if (!(t is JArray a)) return list;
            foreach (var x in a)
            {
                var s = Str(x);
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
            }
            return list;
        }

        /// <summary>Display-safe text: control characters (except newlines) dropped, length capped. UI labels also disable rich text.</summary>
        public static string Plain(string s, int limit = 120)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var chars = s.Where(c => c == '\n' || !char.IsControl(c)).Take(limit).ToArray();
            return new string(chars);
        }

        /// <summary>
        /// Server ids that go into URL paths: letters, digits, '-', '_', ':' and '.'. (':' because the backend's Space ids are
        /// `space:{uuidv7}`.) The segment is percent-escaped by <see cref="Seg"/> before use.
        /// </summary>
        public static bool IsSafeId(string id) =>
            !string.IsNullOrEmpty(id) && id.Length <= 96 &&
            id.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == ':' || c == '.');

        public static string Seg(string id)
        {
            if (!IsSafeId(id)) throw new ArgumentException("Unsafe path id");
            return Uri.EscapeDataString(id);
        }

        /// <summary>
        /// A stable, non-empty Guid for an opaque id. The backend's Space ids are `space:{uuid}`; SpacesAudio (generated
        /// `Space` model) only needs a non-empty Guid per Space, so the uuid part is used, else an MD5 of the id.
        /// </summary>
        public static Guid StableGuid(string id)
        {
            if (Guid.TryParse(id, out var g)) return g;
            var colon = id?.LastIndexOf(':') ?? -1;
            if (colon >= 0 && Guid.TryParse(id.Substring(colon + 1), out g)) return g;
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(id ?? "")));
        }

        public static bool SameId(string a, string b) =>
            !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Token base units (contract: always a digit string). Accepts a digit string or a JSON integer.</summary>
        public static string OptUnits(JToken t)
        {
            if (IsNull(t)) return null;
            var s = t.Type == JTokenType.Integer ? t.ToString() : t.Type == JTokenType.String ? ((string)t).Trim() : null;
            return UsdcUnits.IsBaseUnits(s) ? s : null;
        }
    }
}
