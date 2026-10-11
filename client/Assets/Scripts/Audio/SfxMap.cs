using System.Collections.Generic;
using CTLife.Core;
using Newtonsoft.Json.Linq;

namespace CTLife.Audio
{
    public sealed class SfxEntry
    {
        public string id;
        public List<string> files = new List<string>();
        public float volume = 1f;
        public float pitchJitter = 0.03f;
        public string category;
        /// <summary>Seamless loop (e.g. `sfx_call_ring`): play through <see cref="Sfx.StartLoop"/>, not as a one-shot.</summary>
        public bool loop;
        /// <summary>Stingers duck the music for their length (map `ducks_music`, or category "stinger").</summary>
        public bool ducksMusic;
        /// <summary>The raw entry, for set-specific fields (babble: chars_per_second, npc_pitch_range_semitones…).</summary>
        public JToken raw;
    }

    public sealed class MusicEntry
    {
        public string districtId;
        /// <summary>The prefixed id (`mus_title`, `mus_lobby`…). <see cref="SfxMap.MusicKeyFor"/> accepts either.</summary>
        public string id;
        /// <summary>False: the set ignores market_mood (title / onboarding). Map field `mood_reactive`, default true.</summary>
        public bool moodReactive = true;
        public float bpm;
        /// <summary>A licensed complete composition. Empty selects the adaptive-stem mode.</summary>
        public string fullMix;
        public float volume = 1f;
        /// <summary>pad/bass/drums/lead/tension. With a full mix only `tension` is used (the red-market layer).</summary>
        public readonly Dictionary<string, string> stems = new Dictionary<string, string>();
        public bool HasStems => stems.ContainsKey("pad") || stems.ContainsKey("bass") || stems.ContainsKey("drums") || stems.ContainsKey("lead");
        public bool FullMixMode => !string.IsNullOrEmpty(fullMix) && !HasStems;
    }

    /// <summary>
    /// Parses audio/sfx-map.json (written by the audio lane, copied into Resources/Audio by the content sync).
    /// Paths are relative to audio/processed without changing folders, e.g. "sfx/sfx_buy.wav".
    /// Accepted shapes per sfx id: a string path, `{file}`, or `{files[], volume, pitchJitter, category, loop, ducks_music}`.
    /// Optional `surfaces.venues` maps venue ids to footstep surfaces (overrides <see cref="SurfaceResolver.VenueDefaults"/>).
    /// </summary>
    public sealed class SfxMap
    {
        public readonly Dictionary<string, SfxEntry> Sfx = new Dictionary<string, SfxEntry>();
        public readonly Dictionary<string, SfxEntry> Ambience = new Dictionary<string, SfxEntry>();
        public readonly Dictionary<string, SfxEntry> Babble = new Dictionary<string, SfxEntry>();
        public readonly Dictionary<string, SfxEntry> Footsteps = new Dictionary<string, SfxEntry>();
        public readonly Dictionary<string, MusicEntry> Music = new Dictionary<string, MusicEntry>();
        public readonly Dictionary<string, string> VenueSurfaces = new Dictionary<string, string>();

        /// <summary>The `music` key for a key or a prefixed id ("title" or "mus_title" → "title"); null when unknown.</summary>
        public string MusicKeyFor(string keyOrId)
        {
            if (string.IsNullOrEmpty(keyOrId)) return null;
            if (Music.ContainsKey(keyOrId)) return keyOrId;
            foreach (var kv in Music)
                if (kv.Value.id == keyOrId) return kv.Key;
            return null;
        }

        public static SfxMap Parse(string json)
        {
            var map = new SfxMap();
            if (string.IsNullOrEmpty(json)) return map;
            JToken root;
            try
            {
                root = Json.ParseRaw(json);
            }
            catch (System.Exception e)
            {
                Log.Warn("audio", "sfx-map.json is not valid JSON: " + e.Message);
                return map;
            }
            if (!(root is JObject)) return map;

            // Either {"sfx": {...}, "music": {...}} or a flat {id: entry} object.
            var sfx = root["sfx"] as JObject ?? (root["music"] == null ? root as JObject : null);
            Fill(sfx, map.Sfx);
            Fill(root["ambience"] as JObject, map.Ambience);
            Fill(root["babble"] as JObject, map.Babble);
            Fill(root["footsteps"] as JObject, map.Footsteps);

            if (root["music"] is JObject music)
            {
                foreach (var p in music.Properties())
                {
                    if (p.Name.StartsWith("$") || p.Name.StartsWith("_") || !(p.Value is JObject)) continue;
                    var m = new MusicEntry
                    {
                        districtId = p.Name,
                        id = p.Value.Str("id"),
                        moodReactive = p.Value.Bool("mood_reactive", true),
                        bpm = p.Value.Float("bpm", 100),
                        fullMix = p.Value.Str("full_mix"),
                        volume = UnityEngine.Mathf.Clamp01(p.Value.Float("volume", 1f)),
                    };
                    if (p.Value["stems"] is JObject stems)
                        foreach (var s in stems.Properties())
                            if (s.Value.Type == JTokenType.String && s.Value.ToString().Length > 0) m.stems[s.Name] = s.Value.ToString();
                    if (!string.IsNullOrEmpty(m.fullMix) || m.stems.Count > 0) map.Music[p.Name] = m;
                }
            }
            if (root["surfaces"]?["venues"] is JObject surfaces)
                foreach (var p in surfaces.Properties())
                    if (p.Value.Type == JTokenType.String && SurfaceResolver.IsKnown(p.Value.ToString())) map.VenueSurfaces[p.Name] = p.Value.ToString();
            return map;
        }

        private static void Fill(JObject obj, Dictionary<string, SfxEntry> into)
        {
            if (obj == null) return;
            foreach (var p in obj.Properties())
            {
                if (p.Name.StartsWith("$") || p.Name.StartsWith("_") || p.Name == "version") continue;
                var e = new SfxEntry { id = p.Name, raw = p.Value };
                var v = p.Value;
                if (v.Type == JTokenType.String) e.files.Add(v.ToString());
                else if (v is JObject o)
                {
                    if (o["file"] != null) e.files.Add(o["file"].ToString());
                    if (o["files"] is JArray arr) foreach (var f in arr) e.files.Add(f.ToString());
                    e.volume = UnityEngine.Mathf.Clamp(o.Float("volume", 1f), 0f, 2f);
                    e.pitchJitter = UnityEngine.Mathf.Clamp(o.Float("pitchJitter", 0.03f), 0f, 0.5f);
                    e.category = o.Str("category");
                    e.loop = o.Bool("loop");
                    e.ducksMusic = o.Bool("ducks_music") || e.category == "stinger";
                }
                e.files.RemoveAll(string.IsNullOrEmpty);
                if (e.files.Count > 0) into[e.id] = e;
            }
        }
    }
}
