using System;
using System.Collections.Generic;
using CTLife.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.WorldLife
{
    /// <summary>
    /// Presentation data: Resources/WorldLife/*.json (FE) plus the backend's `npc_lines` and `ids` from ConfigStore.
    /// Rebuilt when the config changes (a live /config with new npc_lines replaces FE proposal lines automatically).
    /// </summary>
    public sealed class WorldLifeData
    {
        public JObject Presentation { get; private set; } = new JObject();
        public JObject Looks { get; private set; } = new JObject();
        public JObject HouseAds { get; private set; } = new JObject();
        public NpcLineBook Lines { get; private set; } = NpcLineBook.Build(null, null, null);
        public ArchetypeBook Archetypes { get; private set; } = ArchetypeBook.Build(null, null);
        public Func<string, string> EmoteAlias { get; private set; } = id => id;
        public JToken AdSlots { get; private set; }
        public readonly List<string> VenueIds = new List<string>();

        public JToken Moments => Presentation["moments"];
        public JToken BehavioursCfg => Presentation["behaviours"];

        public void LoadLocal()
        {
            Presentation = Load("WorldLife/npc_presentation");
            Looks = Load("WorldLife/archetype_looks");
            HouseAds = Load("WorldLife/house_ads");
            EmoteAlias = Behaviours.EmoteAliases(Presentation);
            Archetypes = ArchetypeBook.Build(Looks, Presentation);
            var missing = Archetypes.MissingCoverage();
            if (missing.Count > 0) Log.Warn("world-life", "Archetype look coverage gaps: " + string.Join(", ", missing));
        }

        /// <summary>Re-read backend npc_lines / ids from the current config (snapshot, cache or live).</summary>
        public void BindConfig(ConfigStore cfg)
        {
            var root = cfg?.Root;
            var config = root?["config"];
            Lines = NpcLineBook.Build(config?["npc_lines"] ?? config?["npcLines"], Presentation, Looks);
            AdSlots = root?["ids"]?["ad_slots"];
            VenueIds.Clear();
            if (cfg?.Registries != null) VenueIds.AddRange(cfg.Registries.Venues.Keys);
            foreach (var id in new[] { "rekt_clinic", "perp_pit", "timeline_square", "terminal_cafe", "spaces_studio", "ore_refinery",
                         "launchpad_tower", "prediction_pit", "builder_hills", "audit_row", "vc_row", "ratio_arena", "airdrop_farm",
                         "dubai_marina", "cabal_lounge", "discord_heights", "market_room" })
                if (!VenueIds.Contains(id)) VenueIds.Add(id);
            int be = 0, fe = 0;
            foreach (var _ in Lines.BackendArchetypes) be++;
            foreach (var _ in Lines.FrontendArchetypes) fe++;
            Log.Info("world-life", $"NPC lines: {be} backend sets, {fe} FE proposal sets; {Archetypes.All.Count} archetype looks");
        }

        private static JObject Load(string path)
        {
            try
            {
                var ta = Resources.Load<TextAsset>(path);
                if (ta == null) { Log.Warn("world-life", "Missing Resources/" + path + ".json"); return new JObject(); }
                return JToken.Parse(ta.text) as JObject ?? new JObject();
            }
            catch (Exception e)
            {
                Log.Exception("world-life", e);
                return new JObject();
            }
        }
    }
}
