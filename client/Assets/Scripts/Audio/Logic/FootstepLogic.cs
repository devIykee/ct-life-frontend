using System;
using System.Collections.Generic;

namespace CTLife.Audio
{
    /// <summary>
    /// Which footstep set to play (Build Spec SFX list: concrete, wood, metal grate, carpet, neon tile, grass).
    /// Priority: explicit tag on the ground object (FootstepSurface component) → physic material name → renderer
    /// material name → ground object name → the venue's floor default → concrete.
    /// </summary>
    public static class SurfaceResolver
    {
        public const string Concrete = "concrete", Wood = "wood", MetalGrate = "metal_grate", Carpet = "carpet",
            NeonTile = "neon_tile", Grass = "grass";

        public static readonly string[] All = { Concrete, Wood, MetalGrate, Carpet, NeonTile, Grass };

        /// <summary>
        /// Venue floor defaults. [Inference] from each venue's theme in the spec; a scene can override any patch with
        /// a FootstepSurface component or a recognisable material name. Unknown venues fall back to concrete.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> VenueDefaults = new Dictionary<string, string>
        {
            ["timeline_square"] = Concrete, ["terminal_cafe"] = Wood, ["spaces_studio"] = Carpet,
            ["perp_pit"] = MetalGrate, ["rekt_clinic"] = NeonTile, ["ore_refinery"] = MetalGrate,
            ["launchpad_tower"] = NeonTile, ["prediction_pit"] = Concrete, ["builder_hills"] = Grass,
            ["audit_row"] = Concrete, ["vc_row"] = Wood, ["ratio_arena"] = Concrete, ["airdrop_farm"] = Grass,
            ["dubai_marina"] = Wood, ["cabal_lounge"] = Carpet, ["discord_heights"] = NeonTile,
            ["club_liquidity"] = NeonTile, ["parents_basement"] = Carpet, ["hackathon_house"] = Wood,
            ["conference_hall"] = Carpet, ["airdrop_fields"] = Grass, ["sybil_barn"] = Wood,
        };

        private static readonly (string token, string surface)[] Keywords =
        {
            ("grate", MetalGrate), ("grating", MetalGrate), ("metal", MetalGrate), ("steel", MetalGrate), ("iron", MetalGrate),
            ("catwalk", MetalGrate), ("mesh", MetalGrate),
            ("wood", Wood), ("wooden", Wood), ("plank", Wood), ("planks", Wood), ("parquet", Wood), ("deck", Wood),
            ("decking", Wood), ("boardwalk", Wood), ("floorboard", Wood), ("floorboards", Wood), ("pier", Wood),
            ("carpet", Carpet), ("rug", Carpet), ("fabric", Carpet), ("felt", Carpet), ("mat", Carpet),
            ("grass", Grass), ("lawn", Grass), ("turf", Grass), ("field", Grass), ("soil", Grass), ("dirt", Grass),
            ("garden", Grass), ("hay", Grass), ("meadow", Grass), ("terrain", Grass),
            ("tile", NeonTile), ("tiles", NeonTile), ("tiled", NeonTile), ("neon", NeonTile), ("marble", NeonTile),
            ("ceramic", NeonTile), ("terrazzo", NeonTile), ("glass", NeonTile), ("vinyl", NeonTile), ("linoleum", NeonTile),
            ("concrete", Concrete), ("asphalt", Concrete), ("pavement", Concrete), ("sidewalk", Concrete),
            ("stone", Concrete), ("brick", Concrete), ("cobble", Concrete), ("cobblestone", Concrete), ("road", Concrete),
            ("plaza", Concrete), ("street", Concrete), ("paving", Concrete),
        };

        private static readonly Dictionary<string, string> KeywordMap = BuildKeywordMap();

        private static Dictionary<string, string> BuildKeywordMap()
        {
            var map = new Dictionary<string, string>();
            foreach (var (token, surface) in Keywords) map[token] = surface;
            return map;
        }

        /// <summary>Add or override venue floor defaults from sfx-map.json `surfaces.venues` (data beats code).</summary>
        public static void Override(IDictionary<string, string> venueSurfaces)
        {
            if (venueSurfaces == null) return;
            var target = (Dictionary<string, string>)VenueDefaults;
            foreach (var kv in venueSurfaces)
                if (!string.IsNullOrEmpty(kv.Key) && IsKnown(kv.Value)) target[kv.Key] = kv.Value;
        }

        public static bool IsKnown(string surface) => surface != null && Array.IndexOf(All, surface) >= 0;

        public static string VenueDefault(string venueId, bool isClub = false)
        {
            if (venueId != null && VenueDefaults.TryGetValue(venueId, out var s)) return s;
            return isClub ? NeonTile : Concrete;
        }

        /// <summary>
        /// Surface from a name such as "Floor_WoodPlanks_01", "pm_metal_grate", "M_Neon Tile (Instance)".
        /// Names are split on non-letters and camelCase so "Material" never matches "mat". Null when nothing matches.
        /// </summary>
        public static string FromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var direct = name.Trim().ToLowerInvariant();
            if (IsKnown(direct)) return direct;
            // First matching token wins ("WoodGrass" → wood): deterministic, left to right.
            foreach (var token in Tokens(name))
                if (KeywordMap.TryGetValue(token, out var surface)) return surface;
            return null;
        }

        public static string Resolve(string taggedSurface, string physicMaterialName, string materialName, string objectName,
            string venueDefault)
        {
            if (IsKnown(taggedSurface)) return taggedSurface;
            return FromName(physicMaterialName) ?? FromName(materialName) ?? FromName(objectName) ??
                   (IsKnown(venueDefault) ? venueDefault : Concrete);
        }

        public static List<string> Tokens(string name)
        {
            var tokens = new List<string>();
            var sb = new System.Text.StringBuilder();
            char prev = '\0';
            foreach (char c in name)
            {
                bool letter = char.IsLetter(c);
                bool split = !letter || (char.IsUpper(c) && char.IsLower(prev));
                if (split && sb.Length > 0) { tokens.Add(sb.ToString().ToLowerInvariant()); sb.Clear(); }
                if (letter) sb.Append(c);
                prev = c;
            }
            if (sb.Length > 0) tokens.Add(sb.ToString().ToLowerInvariant());
            return tokens;
        }
    }

    /// <summary>
    /// Footstep voice budget for other avatars (remote players and NPCs). The local player never goes through it.
    /// Rules: silent beyond <see cref="MaxDistance"/>; at most <see cref="MaxVoices"/> other-avatar steps sounding at
    /// once and <see cref="MaxStepsPerWindow"/> starts per <see cref="WindowSeconds"/>; beyond
    /// <see cref="FullRateDistance"/> only every second step plays; when the budget is full a nearer step steals the
    /// farthest sounding voice. Spec budget: 50 players + 12 NPCs in a venue must still hold 60 fps.
    /// </summary>
    public sealed class FootstepBudget
    {
        public const float MaxDistance = 12f;
        public const float FullRateDistance = 6f;
        public const int MaxVoices = 6;
        public const int MaxStepsPerWindow = 10;
        public const float WindowSeconds = 0.25f;
        public const float VoiceSeconds = 0.35f;

        public struct Decision
        {
            public bool Play;
            /// <summary>Voice slot to (re)use, 0..MaxVoices-1.</summary>
            public int Voice;
        }

        private readonly double[] _voiceEnds = new double[MaxVoices];
        private readonly float[] _voiceDistance = new float[MaxVoices];
        private readonly Queue<double> _starts = new Queue<double>();
        private readonly Dictionary<int, int> _parity = new Dictionary<int, int>();

        public int Sounding(double now)
        {
            int n = 0;
            foreach (var end in _voiceEnds) if (end > now) n++;
            return n;
        }

        /// <param name="emitterId">Stable per avatar (instance id); drives the every-second-step thinning.</param>
        public Decision Request(int emitterId, float distance, double now)
        {
            var no = new Decision { Play = false, Voice = -1 };
            if (float.IsNaN(distance) || distance > MaxDistance || distance < 0f) return no;
            if (distance > FullRateDistance)
            {
                _parity.TryGetValue(emitterId, out var p);
                _parity[emitterId] = p + 1;
                if ((p & 1) == 1) return no;
            }
            while (_starts.Count > 0 && now - _starts.Peek() >= WindowSeconds) _starts.Dequeue();
            if (_starts.Count >= MaxStepsPerWindow) return no;

            int free = -1, farthest = -1;
            for (int i = 0; i < MaxVoices; i++)
            {
                if (_voiceEnds[i] <= now) { if (free < 0) free = i; continue; }
                if (farthest < 0 || _voiceDistance[i] > _voiceDistance[farthest]) farthest = i;
            }
            int slot = free;
            if (slot < 0)
            {
                if (farthest < 0 || _voiceDistance[farthest] <= distance) return no;
                slot = farthest; // steal: a nearer step matters more than a distant one already sounding
            }
            _voiceEnds[slot] = now + VoiceSeconds;
            _voiceDistance[slot] = distance;
            _starts.Enqueue(now);
            return new Decision { Play = true, Voice = slot };
        }

        public void Forget(int emitterId) => _parity.Remove(emitterId);
    }
}
