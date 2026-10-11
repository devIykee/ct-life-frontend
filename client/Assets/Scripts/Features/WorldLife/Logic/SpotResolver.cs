using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace CTLife.WorldLife
{
    public enum SpotKind
    {
        /// <summary>No spot given: keep whatever the NPC is doing.</summary>
        Keep,
        /// <summary>A spot in this venue (local id in <see cref="SpotResolution.SpotId"/>).</summary>
        Local,
        /// <summary>The spot names another venue: the NPC is "over there" (contract: `leave` here, fresh spawn there).</summary>
        Offstage,
        /// <summary>Unrecognised: keep position, report once.</summary>
        Unknown,
    }

    public struct SpotResolution
    {
        public SpotKind Kind;
        public string SpotId;
        public string Raw;
        public override string ToString() => Kind + ":" + (SpotId ?? "-");
    }

    public struct SpotPoint
    {
        public string Id;
        public float X, Z;
        public bool HasPosition;
    }

    /// <summary>
    /// Maps the spot ids the backend NPC brain actually sends onto this venue's authored spots. Backend 9f3e5ef sends
    /// `square_center`, `perp_terminal`, `cafe_terminal`, `venue_exit`, venue ids used as spots (`rekt_clinic`,
    /// `spaces_studio`, `ore_refinery`, `vc_row`) and, for wander, `"x0,z0"` corners of walkable rectangles.
    /// Only `square_center` exists in contract/ids/spots.json. Pure logic; the alias table is data (npc_presentation.json).
    /// </summary>
    public sealed class SpotResolver
    {
        private readonly string _venueId;
        private readonly Dictionary<string, SpotPoint> _spots = new Dictionary<string, SpotPoint>(StringComparer.Ordinal);
        private readonly List<string> _ordered = new List<string>();
        private readonly HashSet<string> _venueIds;
        private readonly Dictionary<string, List<string>> _aliases;

        public string VenueId => _venueId;

        public SpotResolver(string venueId, IEnumerable<SpotPoint> spots, IEnumerable<string> venueIds, Dictionary<string, List<string>> aliases)
        {
            _venueId = venueId ?? "";
            if (spots != null)
                foreach (var s in spots)
                {
                    if (string.IsNullOrEmpty(s.Id)) continue;
                    var local = Local(s.Id);
                    if (_spots.ContainsKey(local)) continue;
                    var p = s; p.Id = local;
                    _spots[local] = p;
                    _ordered.Add(local);
                }
            _ordered.Sort(StringComparer.Ordinal);
            _venueIds = new HashSet<string>(venueIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            _aliases = aliases ?? new Dictionary<string, List<string>>();
        }

        /// <summary>Global `spotAliases`, overridden by `spotAliasesByVenue[venueId]` (venue ids as spots resolve per venue).</summary>
        public static Dictionary<string, List<string>> AliasesFrom(JToken feData, string venueId)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (feData?["spotAliases"] is JObject o)
                foreach (var p in o.Properties()) map[p.Name] = WireText.Strs(p.Value);
            if (!string.IsNullOrEmpty(venueId) && feData?["spotAliasesByVenue"]?[venueId] is JObject v)
                foreach (var p in v.Properties()) map[p.Name] = WireText.Strs(p.Value);
            return map;
        }

        public bool Has(string spotId) => !string.IsNullOrEmpty(spotId) && _spots.ContainsKey(Local(spotId));

        private string Local(string id) =>
            _venueId.Length > 0 && id.StartsWith(_venueId + ".", StringComparison.Ordinal) ? id.Substring(_venueId.Length + 1) : id;

        public SpotResolution Resolve(string raw, string npcId)
        {
            var r = new SpotResolution { Raw = raw };
            if (string.IsNullOrEmpty(raw)) { r.Kind = SpotKind.Keep; return r; }
            var local = Local(raw);
            if (_spots.ContainsKey(local)) { r.Kind = SpotKind.Local; r.SpotId = local; return r; }

            if (_aliases.TryGetValue(local, out var targets))
                foreach (var target in targets)
                {
                    var hit = Match(target, npcId);
                    if (hit != null) { r.Kind = SpotKind.Local; r.SpotId = hit; return r; }
                }

            // A venue id as a spot: our own venue means its centre; another venue means the NPC is elsewhere.
            if (_venueIds.Contains(local) || local.IndexOf('.') > 0 && _venueIds.Contains(local.Substring(0, local.IndexOf('.'))))
            {
                var venue = local.IndexOf('.') > 0 ? local.Substring(0, local.IndexOf('.')) : local;
                if (venue == _venueId)
                {
                    var c = Match("center", npcId) ?? Match("entrance", npcId);
                    r.Kind = c != null ? SpotKind.Local : SpotKind.Keep;
                    r.SpotId = c;
                    return r;
                }
                r.Kind = SpotKind.Offstage;
                r.SpotId = venue;
                return r;
            }

            // `"x,z"`: nearest authored spot with a position.
            if (TryCoordinate(local, out var x, out var z))
            {
                string best = null;
                double bestD = double.MaxValue;
                foreach (var id in _ordered)
                {
                    var s = _spots[id];
                    if (!s.HasPosition || id == "exit" || id == "entrance") continue;
                    double d = (s.X - x) * (s.X - x) + (s.Z - z) * (s.Z - z);
                    if (d < bestD) { bestD = d; best = id; }
                }
                if (best != null) { r.Kind = SpotKind.Local; r.SpotId = best; return r; }
            }
            r.Kind = SpotKind.Unknown;
            return r;
        }

        /// <summary>`terminal_*` picks one of the matching spots by npcId (stable on every client); otherwise exact.</summary>
        private string Match(string pattern, string npcId)
        {
            if (string.IsNullOrEmpty(pattern)) return null;
            if (!pattern.EndsWith("*", StringComparison.Ordinal)) return _spots.ContainsKey(pattern) ? pattern : null;
            var prefix = pattern.Substring(0, pattern.Length - 1);
            var matches = new List<string>();
            foreach (var id in _ordered) if (id.StartsWith(prefix, StringComparison.Ordinal)) matches.Add(id);
            if (matches.Count == 0) return null;
            return matches[WireText.Pick((npcId ?? "") + "|" + pattern, matches.Count)];
        }

        public static bool TryCoordinate(string s, out float x, out float z)
        {
            x = z = 0;
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split(',');
            return parts.Length == 2 &&
                   float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                   float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z) &&
                   !float.IsNaN(x) && !float.IsNaN(z) && !float.IsInfinity(x) && !float.IsInfinity(z);
        }
    }
}
