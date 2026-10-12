using System;
using System.Linq;
using CTLife.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>
    /// Keeps an unfinished character across pause/resume and relaunch (device QA 2026-10-08: backgrounding reset the creator to
    /// step 1 with the model turned away). Saved per account in PlayerPrefs on every change and flushed when the app pauses;
    /// cleared when the character is created. The 18+ confirmation is deliberately not kept: it is asked again.
    /// </summary>
    public sealed class CreatorDraftStore
    {
        public const int Version = 2; // 2: Customize step inserted; the full customized look is kept
        private readonly string _key;

        public CreatorDraftStore(string accountId) =>
            _key = "ctlife.creator.draft." + (string.IsNullOrEmpty(accountId) ? "local" : accountId);

        public sealed class Saved
        {
            public CreatorStep step, furthest;
            public string lookKey, tribeId, dreamId, alias, likenessJobId;
            /// <summary>The customized look (seven ids), or null.</summary>
            public string[] look;
            /// <summary>The customizer spec (base, parts, tints, accessories), or null.</summary>
            public AvatarSpec spec;
            public string[] traitIds = Array.Empty<string>();
            public float yaw;
            public bool portrait;
        }

        public static string Serialize(CreatorFlow flow, string lookKey, float yaw, bool portrait, AvatarSpec spec = null)
        {
            var d = flow.Draft;
            var o = new JObject
            {
                ["custom"] = new JArray(d.look.Select(x => (object)x ?? "").ToArray()),
                ["spec"] = spec?.ToJson(),
                ["v"] = Version, ["step"] = (int)flow.Step, ["furthest"] = (int)flow.Furthest, ["look"] = lookKey,
                ["tribe"] = d.tribeId, ["traits"] = new JArray(d.traitIds.ToArray()), ["dream"] = d.dreamId, ["alias"] = d.alias,
                ["yaw"] = yaw, ["portrait"] = portrait,
                ["likenessJobId"] = d.likenessJobId,
            };
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static Saved Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var o = JObject.Parse(json);
                if ((int?)o["v"] != Version) return null;
                var s = new Saved
                {
                    step = ClampStep((int?)o["step"] ?? 0), furthest = ClampStep((int?)o["furthest"] ?? 0),
                    lookKey = (string)o["look"], tribeId = (string)o["tribe"], dreamId = (string)o["dream"], alias = (string)o["alias"] ?? "",
                    yaw = (float?)o["yaw"] ?? 180f, portrait = (bool?)o["portrait"] ?? false,
                    likenessJobId = (string)o["likenessJobId"],
                };
                if (o["traits"] is JArray traits) s.traitIds = traits.ToObject<string[]>() ?? Array.Empty<string>();
                if (o["custom"] is JArray custom && custom.Count == 7) s.look = custom.ToObject<string[]>();
                s.spec = AvatarSpec.FromJson(o["spec"]);
                return s;
            }
            catch (Exception e)
            {
                Log.Warn("creator", "Saved draft unreadable, starting fresh: " + e.Message);
                return null;
            }
        }

        private static CreatorStep ClampStep(int v) => (CreatorStep)Mathf.Clamp(v, 0, CreatorFlow.StepCount - 1);

        /// <summary>Copies a saved draft into `flow` (selections only; the caller restores the step once the registries are checked).</summary>
        public static void Apply(Saved s, CreatorFlow flow)
        {
            if (s == null) return;
            var d = flow.Draft;
            d.tribeId = s.tribeId;
            d.dreamId = s.dreamId;
            d.alias = s.alias ?? "";
            d.likenessJobId = s.likenessJobId;
            d.traitIds.Clear();
            foreach (var t in s.traitIds) if (!string.IsNullOrEmpty(t) && !d.traitIds.Contains(t)) d.traitIds.Add(t);
        }

        public Saved Load()
        {
            try { return Parse(PlayerPrefs.GetString(_key, null)); }
            catch (Exception e) { Log.Warn("creator", "Saved draft unavailable: " + e.Message); return null; }
        }

        public void Save(CreatorFlow flow, string lookKey, float yaw, bool portrait, AvatarSpec spec = null)
        {
            try { PlayerPrefs.SetString(_key, Serialize(flow, lookKey, yaw, portrait, spec)); }
            catch (Exception e) { Log.Warn("creator", "Draft not saved: " + e.Message); }
        }

        /// <summary>Writes PlayerPrefs to disk (on pause; Android may kill a backgrounded app).</summary>
        public static void Flush()
        {
            try { PlayerPrefs.Save(); }
            catch (Exception e) { Log.Warn("creator", "Draft flush failed: " + e.Message); }
        }

        public void Clear()
        {
            try { PlayerPrefs.DeleteKey(_key); PlayerPrefs.Save(); }
            catch (Exception e) { Log.Warn("creator", "Draft not cleared: " + e.Message); }
        }
    }
}
