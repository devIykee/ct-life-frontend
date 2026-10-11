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
        /// <summary>The raw entry, for set-specific fields (babble: chars_per_second, npc_pitch_range_semitones…).</summary>
        public JToken raw;
    }

    public sealed class MusicEntry
    {
        public string districtId;
        public float bpm;
        /// <summary>A licensed complete composition. Empty selects the original adaptive-stem mode.</summary>
        public string fullMix;
        public float volume = 1f;
        public readonly Dictionary<string, string> stems = new Dictionary<string, string>();
    }

    /// <summary>
    /// Parses audio/sfx-map.json (written by the audio agent, copied into Resources/Audio by the editor sync).
    /// Paths are relative to audio/processed without changing folders, e.g. "sfx/sfx_buy_01.ogg".
    /// Accepted shapes per sfx id: a string path, `{file}`, or `{files[], volume, pitchJitter, category}`.
    /// </summary>
    public sealed class SfxMap
    {
        public readonly Dictionary<string, SfxEntry> Sfx = new Dictionary<string, SfxEntry>();
        public readonly Dictionary<string, SfxEntry> Ambience = new Dictionary<string, SfxEntry>();
        public readonly Dictionary<string, SfxEntry> Babble = new Dictionary<string, SfxEntry>();
        public readonly Dictionary<string, SfxEntry> Footsteps = new Dictionary<string, SfxEntry>();
        public readonly Dictionary<string, MusicEntry> Music = new Dictionary<string, MusicEntry>();

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
                    var m = new MusicEntry
                    {
                        districtId = p.Name,
                        bpm = p.Value.Float("bpm", 100),
                        fullMix = p.Value.Str("full_mix"),
                        volume = UnityEngine.Mathf.Clamp01(p.Value.Float("volume", 1f)),
                    };
                    if (p.Value["stems"] is JObject stems)
                        foreach (var s in stems.Properties()) m.stems[s.Name] = s.Value.ToString();
                    map.Music[p.Name] = m;
                }
            }
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
                    e.volume = o.Float("volume", 1f);
                    e.pitchJitter = o.Float("pitchJitter", 0.03f);
                    e.category = o.Str("category");
                }
                if (e.files.Count > 0) into[e.id] = e;
            }
        }
    }
}
