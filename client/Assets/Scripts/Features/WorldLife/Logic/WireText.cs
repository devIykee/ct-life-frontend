using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CTLife.WorldLife
{
    /// <summary>
    /// Text that comes off the wire (NPC lineParams, event params, feed posts, ad links) is untrusted. Everything shown
    /// in a bubble, on a screen or in a shout goes through here: control/format characters removed, rich-text brackets
    /// removed (UI Toolkit labels parse rich text by default), whitespace collapsed, length capped. Pure C#: no UnityEngine.
    /// </summary>
    public static class WireText
    {
        public const int SlotMax = 24;

        public static string Clean(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var sb = new StringBuilder(Math.Min(value.Length, max + 1));
            bool space = false;
            foreach (char c in value)
            {
                if (c == '<' || c == '>' || c == '{' || c == '}') continue;
                if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (char.IsControl(c)) continue;
                if (char.GetUnicodeCategory(c) == UnicodeCategory.Format) continue;
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
                if (sb.Length >= max) break;
            }
            if (sb.Length > 0 && char.IsHighSurrogate(sb[sb.Length - 1])) sb.Length--; // never cut an emoji in half
            var s = sb.ToString().Trim();
            return s.Length == 0 ? null : s;
        }

        /// <summary>Ticker-looking value (letters/digits, 2–10 chars, optional leading $).</summary>
        public static bool LooksLikeTicker(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            var t = v.StartsWith("$") ? v.Substring(1) : v;
            if (t.Length < 2 || t.Length > 10) return false;
            foreach (char c in t) if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9')) return false;
            return true;
        }

        /// <summary>
        /// Presentation of one slot value. `*Cents` integers become dollars, `*Bps` become percent, coin/ticker slots
        /// get the CT `$TICKER` form. Anything else is cleaned and capped. Returns null when the value is unusable.
        /// </summary>
        public static string FormatSlot(string key, string raw)
        {
            var v = Clean(raw, SlotMax);
            if (v == null || string.IsNullOrEmpty(key)) return v;
            if (key.EndsWith("Cents", StringComparison.Ordinal))
                return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cents) ? Dollars(cents) : null;
            if (key.EndsWith("Bps", StringComparison.Ordinal))
                return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bps) ? Percent(bps) : null;
            if (key == "coin" || key == "ticker" || key == "coinA" || key == "coinB")
                return LooksLikeTicker(v) && !v.StartsWith("$") ? "$" + v.ToUpperInvariant() : v;
            return v;
        }

        public static string Dollars(long cents)
        {
            bool neg = cents < 0;
            ulong abs = neg ? (ulong)(-(cents + 1)) + 1UL : (ulong)cents;
            string body;
            if (abs >= 100_000_000UL * 100UL) body = (abs / 100_000_000UL / 100.0).ToString("0.#", CultureInfo.InvariantCulture) + "B";
            else if (abs >= 1_000_000UL * 100UL) body = (abs / 1_000_000UL / 100.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            else if (abs >= 10_000UL * 100UL) body = (abs / 1000UL / 100.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            else if (abs >= 100UL * 100UL) body = (abs / 100UL).ToString("#,0", CultureInfo.InvariantCulture);
            else body = (abs / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
            return (neg ? "-$" : "$") + body;
        }

        public static string Percent(long bps)
        {
            var p = bps / 100.0;
            return (p > 0 ? "+" : "") + p.ToString(Math.Abs(bps) % 100 == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>FNV-1a over UTF-8, the same family NpcCastCatalog uses, so picks are identical on every client.</summary>
        public static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(s ?? "")) h = unchecked((h ^ b) * 16777619);
            return h;
        }

        public static int Pick(string seed, int count) => count <= 0 ? -1 : (int)(Hash(seed) % (uint)count);

        // ---- tiny JSON helpers (independent of CTLife.Core so the logic stays testable without Unity) ----

        public static string Str(JToken t, string key, string fallback = null)
        {
            var v = t is JObject o ? o[key] : null;
            return v == null || v.Type == JTokenType.Null ? fallback : v.Type == JTokenType.String ? (string)v : v.ToString();
        }

        public static double Num(JToken t, string key, double fallback = 0)
        {
            var v = t is JObject o ? o[key] : null;
            if (v == null) return fallback;
            if (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) return v.Value<double>();
            return double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;
        }

        public static bool Bool(JToken t, string key, bool fallback = false)
        {
            var v = t is JObject o ? o[key] : null;
            if (v == null) return fallback;
            if (v.Type == JTokenType.Boolean) return v.Value<bool>();
            return bool.TryParse(v.ToString(), out var b) ? b : fallback;
        }

        public static List<string> Strs(JToken t)
        {
            var list = new List<string>();
            if (t is JArray a) foreach (var x in a) { if (x != null && x.Type != JTokenType.Null) list.Add(x.ToString()); }
            else if (t != null && t.Type == JTokenType.String) list.Add((string)t);
            return list;
        }
    }
}
