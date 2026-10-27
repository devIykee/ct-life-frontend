using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.Core
{
    /// <summary>
    /// Every brand, place, app and character name players see, from one table (QUALITY-BAR.md, 2026-10-26):
    /// contract/ids/names.json, shipped as Resources/Contract/names.json. <c>names</c> maps an id ("venue.perp_pit",
    /// "app.pulse", "npc.sleuth") to its display name; <c>strings</c> overrides UI string keys whose text is a name
    /// ("rekt.tag"). A rename is one value in that file. Missing ids fall back to the caller's text, so nothing breaks.
    /// </summary>
    public static class NameTable
    {
        private static Dictionary<string, string> _names, _strings;
        private static Dictionary<string, string> _replace;   // old invented name (exact and UPPER) -> new name
        private static System.Text.RegularExpressions.Regex _replaceRx;

        private static void Ensure()
        {
            if (_names != null) return;
            _names = new Dictionary<string, string>(StringComparer.Ordinal);
            _strings = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var ta = Resources.Load<TextAsset>("Contract/names");
                if (ta != null) LoadFrom(ta.text);
            }
            catch (Exception e) { Debug.LogWarning("CTLIFE [names] names.json unreadable: " + e.Message); }
        }

        /// <summary>Replaces the table (tests, or a server copy later).</summary>
        public static void LoadFrom(string json)
        {
            _names = new Dictionary<string, string>(StringComparer.Ordinal);
            _strings = new Dictionary<string, string>(StringComparer.Ordinal);
            _replace = new Dictionary<string, string>(StringComparer.Ordinal);
            _replaceRx = null;
            if (string.IsNullOrEmpty(json)) return;
            var root = JObject.Parse(json);
            Fill(root["names"] as JObject, _names);
            Fill(root["strings"] as JObject, _strings);
            if (root["replace"] is JObject rep)
            {
                foreach (var kv in rep)
                {
                    var id = kv.Value?.Type == JTokenType.String ? (string)kv.Value : null;
                    if (string.IsNullOrEmpty(kv.Key) || id == null || !_names.TryGetValue(id, out var to)) continue;
                    _replace[kv.Key] = to;
                    var upper = kv.Key.ToUpperInvariant();
                    if (upper != kv.Key) _replace[upper] = to.ToUpperInvariant();
                }
                var keys = new List<string>(_replace.Keys);
                keys.Sort((a, b) => b.Length != a.Length ? b.Length.CompareTo(a.Length) : string.CompareOrdinal(a, b));
                if (keys.Count > 0)
                {
                    var parts = keys.ConvertAll(k => System.Text.RegularExpressions.Regex.Escape(k));
                    _replaceRx = new System.Text.RegularExpressions.Regex(@"(?<![\w$])(" + string.Join("|", parts) + @")(?!\w)",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                }
            }
        }

        private static void Fill(JObject o, Dictionary<string, string> into)
        {
            if (o == null) return;
            foreach (var kv in o)
                if (kv.Value?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)kv.Value)) into[kv.Key] = ((string)kv.Value).Trim();
        }

        /// <summary>The display name for an id, or <paramref name="fallback"/>.</summary>
        public static string Get(string id, string fallback = null)
        {
            Ensure();
            return id != null && _names.TryGetValue(id, out var v) ? v : fallback;
        }

        /// <summary>A UI string key whose text is a name (Strings.T reads through this).</summary>
        public static bool TryString(string key, out string text)
        {
            Ensure();
            text = null;
            return key != null && _strings.TryGetValue(key, out text);
        }

        public static int Count { get { Ensure(); return _names.Count + _strings.Count; } }

        /// <summary>
        /// Swaps every old invented name in a piece of UI copy for its approved name (names.json `replace`, longest first,
        /// whole words, exact or UPPER case). Every string table's lookup runs its text through this, so copy written before
        /// the rename ("Rekt Burger is hiring") reads with the approved names ("McDonald's is hiring").
        /// </summary>
        public static string Apply(string text)
        {
            Ensure();
            if (string.IsNullOrEmpty(text) || _replaceRx == null) return text;
            return _replaceRx.Replace(text, m => _replace.TryGetValue(m.Value, out var to) ? to : m.Value);
        }

        /// <summary>Test hook: forget the loaded table so the next read loads Resources again.</summary>
        public static void Reset() { _names = null; _strings = null; _replace = null; _replaceRx = null; }
    }
}
