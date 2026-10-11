using System;
using System.Collections.Generic;

namespace CTLife.WorldLife
{
    /// <summary>Plain copy of a replicated NpcState (contract `NpcState`), so the interpreter is testable without Colyseus.</summary>
    public sealed class NpcSnapshot
    {
        public string ArchetypeId = "";
        public string State = "idle";
        public string SpotId = "";
        public string AnimId = "";
        public string TargetUserId = "";
        public string LineId = "";
        public Dictionary<string, string> LineParams = new Dictionary<string, string>();
        public string EmoteId = "";
        public double StartedAt;

        public NpcSnapshot Clone() => new NpcSnapshot
        {
            ArchetypeId = ArchetypeId, State = State, SpotId = SpotId, AnimId = AnimId, TargetUserId = TargetUserId,
            LineId = LineId, LineParams = LineParams != null ? new Dictionary<string, string>(LineParams) : new Dictionary<string, string>(),
            EmoteId = EmoteId, StartedAt = StartedAt,
        };
    }

    /// <summary>What to present for one replicated NPC update.</summary>
    public sealed class NpcBeat
    {
        /// <summary>The state the navigation/animation follower should play. Stable within a run (see interpreter).</summary>
        public NpcSnapshot Shown;
        public string RawState;
        public bool UnknownState;
        public bool NewRun;
        /// <summary>A line id arrived that this run has not presented yet (show bubble + babble once).</summary>
        public bool FreshLine;
        public string LineId;
        /// <summary>An emote id arrived that differs from the previous update.</summary>
        public bool FreshEmote;
        public string EmoteId;
        /// <summary>A new non-locomotion animation arrived inside the run (replay).</summary>
        public bool FreshAnim;
        /// <summary>The NPC is in another venue (or walking there): present as `leave` and keep hidden.</summary>
        public bool Offstage;
        /// <summary>Previous run was hidden (offstage or leave) and this one is visible: a fresh spawn.</summary>
        public bool Respawn;
        public SpotResolution Spot;
        public double RunStartedAt;
        /// <summary>The spot was not recognised (contract gap; report once).</summary>
        public bool UnknownSpot;
    }

    /// <summary>
    /// Turns the replicated NpcState stream into presentation beats. Written against what backend main (9f3e5ef) really
    /// sends from `server/realtime/rooms/venue.ts`:
    /// <list type="bullet">
    /// <item>`startedAt` is rewritten to "now" on every 1 Hz tick, for every NPC, even when nothing changed. Followers
    /// that key on it restart walks, react one-shots and idle timers every second. A run's start is the first
    /// `startedAt` seen for it.</item>
    /// <item>After the first tick of `act`/`react`/`goto`, the brain's fallthrough branch sends `animId: loco_idle` and
    /// `lineId: ''` while the state continues. Inside a run the first action anim and the first line are sticky.</item>
    /// <item>Spot ids are brain names, venue ids or `"x,z"` (see <see cref="SpotResolver"/>).</item>
    /// <item>The Perp Degen "leaves to the Rekt Clinic" by setting `spotId: rekt_clinic` inside the Perp Pit room and
    /// then acting there: presented as leave + hidden until it is back on a local spot (contract: leave here, fresh
    /// spawn there).</item>
    /// </list>
    /// A run is a sequence of updates with the same presented state, spot and target.
    /// </summary>
    public sealed class NpcBeatInterpreter
    {
        public static readonly string[] States = { "idle", "wander", "goto", "act", "talk", "react", "follow", "leave" };

        private string _runKey;
        private double _runStart;
        private string _runAnim = "";
        private string _runLine = "";
        private string _runSpot = "";
        private string _lastEmote = "";
        private bool _hidden;
        private bool _hasRun;

        public bool Hidden => _hidden;

        public static string NormalizeState(string raw, out bool unknown)
        {
            var s = (raw ?? "").Trim().ToLowerInvariant();
            unknown = Array.IndexOf(States, s) < 0;
            return unknown ? "idle" : s;
        }

        public static bool IsLoco(string animId) => string.IsNullOrEmpty(animId) || animId.StartsWith("loco_", StringComparison.Ordinal);

        /// <param name="emoteAlias">Maps emote ids the brain sends but anims.json lacks (`emote_moon`); may be null.</param>
        public NpcBeat Next(NpcSnapshot raw, SpotResolver spots, string npcId, Func<string, string> emoteAlias = null)
        {
            raw = raw ?? new NpcSnapshot();
            var beat = new NpcBeat { RawState = raw.State };
            var state = NormalizeState(raw.State, out beat.UnknownState);
            var spot = spots != null ? spots.Resolve(raw.SpotId, npcId) : new SpotResolution { Kind = SpotKind.Keep, Raw = raw.SpotId };
            beat.Spot = spot;

            // Offstage only for states that put the NPC somewhere (walk there / do something there / leave).
            bool positional = state == "goto" || state == "wander" || state == "act" || state == "leave";
            bool offstage = spot.Kind == SpotKind.Offstage && positional;
            string shownState = offstage ? "leave" : state;
            string shownSpot;
            if (offstage) shownSpot = "exit";
            else if (spot.Kind == SpotKind.Local) shownSpot = spot.SpotId;
            else
            {
                shownSpot = _runSpot;
                beat.UnknownSpot = spot.Kind == SpotKind.Unknown;
            }
            if (shownState == "leave" && string.IsNullOrEmpty(shownSpot)) shownSpot = "exit";

            var target = raw.TargetUserId ?? "";
            var key = shownState + "|" + shownSpot + "|" + target + (offstage ? "|off" : "");
            bool hiddenNow = shownState == "leave";
            if (key != _runKey)
            {
                beat.NewRun = true;
                beat.Respawn = _hasRun && _hidden && !hiddenNow;
                _runKey = key;
                _runStart = raw.StartedAt;
                _runAnim = raw.AnimId ?? "";
                _runLine = "";
                _runSpot = shownSpot ?? "";
                _hasRun = true;
                if (!IsLoco(_runAnim)) beat.FreshAnim = true;
            }
            else if (!IsLoco(raw.AnimId) && raw.AnimId != _runAnim)
            {
                _runAnim = raw.AnimId;
                beat.FreshAnim = true;
            }
            _hidden = hiddenNow;
            beat.Offstage = offstage;

            if (!string.IsNullOrEmpty(raw.LineId) && raw.LineId != _runLine)
            {
                _runLine = raw.LineId;
                beat.FreshLine = true;
            }
            beat.LineId = _runLine;

            var emote = raw.EmoteId ?? "";
            if (emote.Length > 0 && emoteAlias != null) emote = emoteAlias(emote) ?? emote;
            if (emote.Length > 0 && emote != _lastEmote) beat.FreshEmote = true;
            _lastEmote = emote;
            beat.EmoteId = emote;

            beat.RunStartedAt = _runStart;
            beat.Shown = new NpcSnapshot
            {
                ArchetypeId = raw.ArchetypeId ?? "",
                State = shownState,
                SpotId = shownSpot ?? "",
                AnimId = _runAnim,
                TargetUserId = target,
                LineId = _runLine,
                LineParams = raw.LineParams ?? new Dictionary<string, string>(),
                EmoteId = offstage ? "" : emote,
                StartedAt = _runStart,
            };
            return beat;
        }
    }
}
