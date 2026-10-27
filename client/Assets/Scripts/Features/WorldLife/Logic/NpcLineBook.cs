using CTLife.Core;
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CTLife.WorldLife
{
    /// <summary>`{slot}` template helpers. A slot is `{` + identifier + `}`.</summary>
    public static class LineTemplates
    {
        public static List<string> Slots(string template)
        {
            var slots = new List<string>();
            if (string.IsNullOrEmpty(template)) return slots;
            int i = 0;
            while ((i = template.IndexOf('{', i)) >= 0)
            {
                int end = template.IndexOf('}', i + 1);
                if (end < 0) break;
                var name = template.Substring(i + 1, end - i - 1);
                if (IsIdentifier(name) && !slots.Contains(name)) slots.Add(name);
                i = end + 1;
            }
            return slots;
        }

        private static bool IsIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 32) return false;
            foreach (char c in s) if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        /// <summary>True when every slot has a usable (formatted, non-empty) value.</summary>
        public static bool CanFill(string template, IReadOnlyDictionary<string, string> parameters)
        {
            foreach (var slot in Slots(template))
            {
                if (parameters == null || !parameters.TryGetValue(slot, out var raw)) return false;
                if (WireText.FormatSlot(slot, raw) == null) return false;
            }
            return true;
        }

        /// <summary>Fills every slot; returns null if any slot cannot be filled (never shows a raw `{slot}`).</summary>
        public static string Fill(string template, IReadOnlyDictionary<string, string> parameters)
        {
            if (template == null) return null;
            var sb = new StringBuilder(NameTable.Apply(template));   // approved names (names.json)
            foreach (var slot in Slots(template))
            {
                string raw = null;
                if (parameters == null || !parameters.TryGetValue(slot, out raw)) return null;
                var value = WireText.FormatSlot(slot, raw);
                if (value == null) return null;
                sb.Replace("{" + slot + "}", value);
            }
            var text = sb.ToString();
            // `$$FROGE` when a template already prefixes `$` before a coin slot.
            text = text.Replace("$$", "$");
            return text.IndexOf('{') >= 0 && Slots(text).Count > 0 ? null : text;
        }
    }

    public sealed class LineResult
    {
        public string Text;
        /// <summary>be (backend npc_lines), fe (FE proposal set), signature (spec table line), event (FE event shout).</summary>
        public string Source;
        public string ArchetypeId;
        /// <summary>True when the server line id was not recognised and a pool line stood in (log once as a contract gap).</summary>
        public bool UnknownLineId;
    }

    /// <summary>
    /// Resolves what an NPC says. Backend rc1 sends named line ids (`crier_long_liquidation`, `giveaway_pitch`,
    /// `reply_gm`…) from `packages/npc-brain`, but `npc_lines.json` is an unnamed array per archetype, and only 9 of the
    /// 28 archetypes have a set. Resolution, in order:
    /// 1. an exact keyed line (future/stand-in layout `{id, text}`);
    /// 2. `archetype.N` index ids;
    /// 3. FE alias table: line id → archetype + substring matches inside that archetype's pool;
    /// 4. the archetype pool (backend set, else the FE proposal set);
    /// 5. the archetype's spec signature line.
    /// Only lines whose every `{slot}` can be filled from lineParams are eligible, so a raw `{coin}` never reaches a
    /// bubble. The pick is deterministic from a seed (npcId|lineId|run start) so every client shows the same line.
    /// </summary>
    public sealed class NpcLineBook
    {
        private sealed class Alias
        {
            public string archetype;
            public List<string> match = new List<string>();
        }

        private readonly Dictionary<string, List<string>> _be = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _fe = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _keyed = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Alias> _aliases = new Dictionary<string, Alias>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _archetypeAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _signature = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _eventLines = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public IEnumerable<string> BackendArchetypes => _be.Keys;
        public IEnumerable<string> FrontendArchetypes => _fe.Keys;

        /// <param name="beNpcLines">config `npc_lines` (rc1: `{archetype: ["text", …]}`; stand-in: `{archetypes: {a: [{id,text}]}}`).</param>
        /// <param name="feData">Resources/WorldLife/npc_presentation.json.</param>
        /// <param name="looks">Resources/WorldLife/archetype_looks.json (signature lines).</param>
        public static NpcLineBook Build(JToken beNpcLines, JToken feData, JToken looks)
        {
            var book = new NpcLineBook();
            if (beNpcLines is JObject be)
            {
                var root = be["archetypes"] as JObject ?? be;
                foreach (var prop in root.Properties())
                {
                    if (!(prop.Value is JArray arr)) continue;
                    var texts = new List<string>();
                    int i = 0;
                    foreach (var l in arr)
                    {
                        string text, id;
                        if (l.Type == JTokenType.String) { text = (string)l; id = null; }
                        else { text = WireText.Str(l, "text"); id = WireText.Str(l, "id"); }
                        if (string.IsNullOrWhiteSpace(text)) { i++; continue; }
                        texts.Add(text);
                        if (!string.IsNullOrEmpty(id)) book._keyed[id] = text;
                        book._keyed[prop.Name + "." + i] = text;
                        i++;
                    }
                    if (texts.Count > 0) book._be[prop.Name] = texts;
                }
            }
            if (feData is JObject fe)
            {
                if (fe["archetypeAliases"] is JObject aa)
                    foreach (var p in aa.Properties()) book._archetypeAliases[p.Name] = p.Value.ToString();
                if (fe["lineAliases"] is JObject la)
                    foreach (var p in la.Properties())
                        book._aliases[p.Name] = new Alias { archetype = WireText.Str(p.Value, "archetype"), match = WireText.Strs(p.Value["match"]) };
                if (fe["feLines"] is JObject fl)
                    foreach (var p in fl.Properties())
                    {
                        var lines = WireText.Strs(p.Value);
                        if (lines.Count > 0) book._fe[p.Name] = lines;
                    }
                if (fe["eventLines"] is JObject el)
                    foreach (var p in el.Properties()) book._eventLines[p.Name] = WireText.Strs(p.Value);
            }
            if (looks?["archetypes"] is JObject la2)
                foreach (var p in la2.Properties())
                {
                    var sig = WireText.Str(p.Value, "signatureLine");
                    if (!string.IsNullOrWhiteSpace(sig) && LineTemplates.Slots(sig).Count == 0) book._signature[p.Name] = sig;
                }
            return book;
        }

        public string Canonical(string archetypeId)
        {
            if (string.IsNullOrEmpty(archetypeId)) return archetypeId;
            return _archetypeAliases.TryGetValue(archetypeId, out var a) ? a : archetypeId;
        }

        public bool HasBackendSet(string archetypeId) => _be.ContainsKey(Canonical(archetypeId) ?? "");
        public bool HasFrontendSet(string archetypeId) => _fe.ContainsKey(Canonical(archetypeId) ?? "");

        /// <summary>The backend set when it exists, else the FE proposal set (empty if neither).</summary>
        public IReadOnlyList<string> Pool(string archetypeId)
        {
            var a = Canonical(archetypeId) ?? "";
            if (_be.TryGetValue(a, out var be)) return be;
            if (_fe.TryGetValue(a, out var fe)) return fe;
            return Array.Empty<string>();
        }

        public string Signature(string archetypeId) =>
            _signature.TryGetValue(Canonical(archetypeId) ?? "", out var s) ? s : null;

        public LineResult Resolve(string archetypeId, string lineId, IReadOnlyDictionary<string, string> parameters, string seed)
        {
            if (string.IsNullOrEmpty(lineId)) return null;
            var arch = Canonical(archetypeId);
            seed = (seed ?? "") + "|" + lineId;

            // 1 + 2: exact keyed line or `archetype.N`.
            if (_keyed.TryGetValue(lineId, out var exact))
            {
                var text = LineTemplates.Fill(exact, parameters);
                if (text != null) return new LineResult { Text = text, Source = "be", ArchetypeId = arch };
            }

            // 3: alias → archetype + matches.
            bool known = false;
            if (_aliases.TryGetValue(lineId, out var alias))
            {
                known = true;
                var aliasArch = Canonical(alias.archetype) ?? arch;
                var pool = Pool(aliasArch);
                var picked = PickFillable(pool, parameters, seed, alias.match);
                if (picked != null) return new LineResult { Text = picked, Source = Source(aliasArch), ArchetypeId = aliasArch };
                arch = aliasArch ?? arch;
            }

            // 4: archetype pool.
            var any = PickFillable(Pool(arch), parameters, seed, null);
            if (any != null) return new LineResult { Text = any, Source = Source(arch), ArchetypeId = arch, UnknownLineId = !known };

            // 5: signature line.
            var sig = Signature(arch);
            if (sig != null) return new LineResult { Text = sig, Source = "signature", ArchetypeId = arch, UnknownLineId = !known };
            return null;
        }

        /// <summary>An FE event shout (`eventLines[eventId]`), filled from the event params; null if none can be filled.</summary>
        public LineResult ResolveEvent(string eventId, IReadOnlyDictionary<string, string> parameters, string seed)
        {
            if (string.IsNullOrEmpty(eventId) || !_eventLines.TryGetValue(eventId, out var lines)) return null;
            var text = PickFillable(lines, parameters, (seed ?? "") + "|" + eventId, null);
            return text == null ? null : new LineResult { Text = text, Source = "event", ArchetypeId = "town_crier" };
        }

        private string Source(string arch) => _be.ContainsKey(arch ?? "") ? "be" : "fe";

        /// <summary>
        /// Deterministic pick among fillable lines. Lines that use slots are preferred when they can be filled (they carry
        /// the live numbers); slot-free lines are the fallback.
        /// </summary>
        public static string PickFillable(IReadOnlyList<string> pool, IReadOnlyDictionary<string, string> parameters, string seed, List<string> match)
        {
            if (pool == null || pool.Count == 0) return null;
            var withSlots = new List<string>();
            var plain = new List<string>();
            foreach (var line in pool)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (match != null && match.Count > 0 && !Matches(line, match)) continue;
                var slots = LineTemplates.Slots(line);
                if (slots.Count == 0) plain.Add(line);
                else if (LineTemplates.CanFill(line, parameters)) withSlots.Add(line);
            }
            var from = withSlots.Count > 0 ? withSlots : plain;
            if (from.Count == 0) return null;
            var chosen = from[WireText.Pick(seed, from.Count)];
            return LineTemplates.Fill(chosen, parameters);
        }

        private static bool Matches(string line, List<string> match)
        {
            foreach (var m in match)
                if (!string.IsNullOrEmpty(m) && line.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
