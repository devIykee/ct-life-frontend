using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CTLife.WorldLife
{
    /// <summary>
    /// Per-archetype presentation cues derived from a beat. The server decides what an NPC does; these only decide how
    /// that is staged (who faces whom, which lines land on the Timeline screen, cosmetic clones/drones, a siren).
    /// Triggers are data (`behaviours` in npc_presentation.json) so the backend's final ids can be bound without code.
    /// </summary>
    public static class Behaviours
    {
        public const string PostToTimeline = "post_to_timeline";
        public const string FaceTarget = "face_target";
        public const string FaceRival = "face_rival";
        public const string SplitClones = "split_clones";
        public const string ClearClones = "clear_clones";
        public const string Siren = "siren";
        public const string Drone = "drone_body";

        public static List<string> Cues(ArchetypeLook look, NpcBeat beat, JToken behavioursCfg, bool clonesActive)
        {
            var cues = new List<string>();
            if (look == null || beat == null || beat.Offstage) return cues;
            if (beat.FreshLine && look.Has("post_to_timeline")) cues.Add(PostToTimeline);
            if (beat.FreshLine && look.Has("face_target_on_talk") && !string.IsNullOrEmpty(beat.Shown?.TargetUserId)) cues.Add(FaceTarget);
            if (beat.FreshLine && look.Has("face_rival")) cues.Add(FaceRival);
            if (look.Has("siren_on_liquidation") && beat.FreshAnim && beat.Shown?.AnimId == "react_liquidated") cues.Add(Siren);
            if (look.Has("sybil_split") && Triggered(behavioursCfg?["sybil_split"], beat)) cues.Add(SplitClones);
            if (look.Has("clear_clones") && clonesActive)
            {
                var cfg = behavioursCfg?["mod_clear"];
                bool arrive = WireText.Bool(cfg, "onArrive", true) && beat.NewRun && !beat.Respawn &&
                              (beat.Shown?.State == "act" || beat.Shown?.State == "talk");
                if (arrive || Triggered(cfg, beat)) cues.Add(ClearClones);
            }
            return cues;
        }

        /// <summary>Fresh anim/emote/line id listed under the behaviour's `anims`/`emotes`/`lines`.</summary>
        public static bool Triggered(JToken cfg, NpcBeat beat)
        {
            if (cfg == null || beat == null) return false;
            if (beat.FreshAnim && WireText.Strs(cfg["anims"]).Contains(beat.Shown?.AnimId ?? "")) return true;
            if (beat.FreshEmote && WireText.Strs(cfg["emotes"]).Contains(beat.EmoteId ?? "")) return true;
            if (beat.FreshLine && WireText.Strs(cfg["lines"]).Contains(beat.LineId ?? "")) return true;
            return false;
        }

        /// <summary>Emote aliases (`emote_moon` → `emote_moon_jump`): ids the brain sends that anims.json lacks.</summary>
        public static Func<string, string> EmoteAliases(JToken feData)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (feData?["emoteAliases"] is JObject o) foreach (var p in o.Properties()) map[p.Name] = p.Value.ToString();
            return id => id != null && map.TryGetValue(id, out var to) ? to : id;
        }
    }
}
