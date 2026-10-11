using System;
using System.Collections.Generic;
using CTLife.Realtime;
using Newtonsoft.Json;

namespace CTLife.World
{
    public enum InteractionPhase { Idle, Approaching, Requesting, Engaged }

    public enum InteractionCommandKind
    {
        WalkTo,       // walk to target.approach (no claim yet)
        RequestSeat,  // send `sit {spotId: wireId}`; the server confirms with PlayerState.spotId
        EngageLocal,  // present engaged on this client only (local-only kinds, or no room)
        ReleaseSeat,  // send `stand {}` and present the stand-up
        Disengage,    // present the stand-up of a local-only engagement
        CancelWalk,
        OpenMap,      // the door: leave through the map
        Emote,        // networked one-shot while engaged (existing `emote` message)
        Toast,        // UI Strings key
    }

    public readonly struct InteractionCommand
    {
        public readonly InteractionCommandKind kind;
        public readonly Interactable target;
        public readonly string arg;
        public InteractionCommand(InteractionCommandKind kind, Interactable target = null, string arg = null)
        { this.kind = kind; this.target = target; this.arg = arg; }
        public override string ToString() => kind + (target != null ? "(" + target.id + ")" : "") + (arg != null ? "[" + arg + "]" : "");
    }

    /// <summary>
    /// The player's interaction state: nothing is used until the player presses the CTA. Approach, then (for
    /// server-owned spots) claim and wait for the authoritative confirmation; local-only kinds (counter) and rooms
    /// without a server engage on this client. Pure logic: inputs are player/server events, outputs are commands the
    /// InteractionController executes. Unit-tested in EditMode (InteractionMachineTests).
    /// </summary>
    public sealed class InteractionMachine
    {
        public const double RequestTimeoutSeconds = 5.0;

        public InteractionPhase Phase { get; private set; } = InteractionPhase.Idle;
        public Interactable Target { get; private set; }
        public bool LocalOnly { get; private set; }
        private double _deadline;

        private static readonly List<InteractionCommand> None = new List<InteractionCommand>();
        private static List<InteractionCommand> One(InteractionCommandKind k, Interactable t = null, string a = null) =>
            new List<InteractionCommand> { new InteractionCommand(k, t, a) };

        /// <summary>The player pressed the primary CTA for an offered target.</summary>
        public List<InteractionCommand> Use(Interactable t, bool online, double now)
        {
            if (t == null || Phase != InteractionPhase.Idle) return None;
            if (t.kind == InteractionKind.Door) return One(InteractionCommandKind.OpenMap, t);
            Target = t;
            LocalOnly = !(t.Networked && online);
            Phase = InteractionPhase.Approaching;
            return One(InteractionCommandKind.WalkTo, t);
        }

        /// <summary>The actor reached the approach point.</summary>
        public List<InteractionCommand> Arrived(double now)
        {
            if (Phase != InteractionPhase.Approaching) return None;
            if (LocalOnly)
            {
                Phase = InteractionPhase.Engaged;
                return One(InteractionCommandKind.EngageLocal, Target);
            }
            Phase = InteractionPhase.Requesting;
            _deadline = now + RequestTimeoutSeconds;
            return One(InteractionCommandKind.RequestSeat, Target, Target.wireId);
        }

        public List<InteractionCommand> WalkFailed()
        {
            if (Phase != InteractionPhase.Approaching) return None;
            Reset();
            return One(InteractionCommandKind.Toast, null, "interact.cantReach");
        }

        /// <summary>The authoritative spot of this player changed (PlayerState.spotId; empty = standing).</summary>
        public List<InteractionCommand> ServerSpot(string spotId, Func<string, Interactable> resolve)
        {
            if (string.IsNullOrEmpty(spotId))
            {
                // A server stand (or our own release echo) ends a networked engagement; a late empty echo while a new
                // claim is pending must not cancel it.
                if (Phase == InteractionPhase.Engaged && !LocalOnly) Reset();
                return None;
            }
            var it = resolve?.Invoke(spotId);
            if ((Phase == InteractionPhase.Requesting || Phase == InteractionPhase.Approaching) && Target != null && Same(it, Target))
            {
                Phase = InteractionPhase.Engaged;
                LocalOnly = false;
                return None;
            }
            if (it != null && !(Phase == InteractionPhase.Engaged && Same(it, Target)))
            {
                // The server seated us somewhere (rejoin, or it resolved a different claim): follow it.
                Target = it;
                Phase = InteractionPhase.Engaged;
                LocalOnly = false;
            }
            return None;
        }

        /// <summary>The actor stood (stick, server, or our own release) — any engagement is over.</summary>
        public List<InteractionCommand> Stood()
        {
            if (Phase == InteractionPhase.Engaged) Reset();
            return None;
        }

        /// <summary>`rejected {type: "sit"}` (e.g. SPOT_TAKEN; VenueSession shows the toast).</summary>
        public List<InteractionCommand> Rejected()
        {
            if (Phase != InteractionPhase.Requesting && Phase != InteractionPhase.Approaching) return None;
            Reset();
            return None;
        }

        public List<InteractionCommand> Tick(double now)
        {
            if (Phase != InteractionPhase.Requesting || now < _deadline) return None;
            var t = Target;
            Reset();
            // Release a claim the server may still apply late, then tell the player.
            return new List<InteractionCommand>
            {
                new InteractionCommand(InteractionCommandKind.ReleaseSeat, t),
                new InteractionCommand(InteractionCommandKind.Toast, null, "interact.noResponse"),
            };
        }

        /// <summary>The player took control (stick/arrows) — cancels a pending approach or claim.</summary>
        public List<InteractionCommand> PlayerMoved()
        {
            switch (Phase)
            {
                case InteractionPhase.Approaching: Reset(); return One(InteractionCommandKind.CancelWalk);
                case InteractionPhase.Requesting:
                {
                    var t = Target; Reset();
                    return new List<InteractionCommand> { new InteractionCommand(InteractionCommandKind.ReleaseSeat, t), new InteractionCommand(InteractionCommandKind.CancelWalk) };
                }
                case InteractionPhase.Engaged:
                {
                    // Moving off a seat is an explicit stand (LocalPlayerController already releases networked seats).
                    var t = Target; bool local = LocalOnly; Reset();
                    return local ? One(InteractionCommandKind.Disengage, t) : None;
                }
                default: return None;
            }
        }

        /// <summary>The "Stand up" / "Step away" CTA.</summary>
        public List<InteractionCommand> StandUp()
        {
            if (Phase != InteractionPhase.Engaged) return None;
            var t = Target; bool local = LocalOnly; Reset();
            return One(local ? InteractionCommandKind.Disengage : InteractionCommandKind.ReleaseSeat, t);
        }

        public List<InteractionCommand> Cancel() => PlayerMoved();

        /// <summary>A secondary CTA ("emote:&lt;id&gt;") while engaged.</summary>
        public List<InteractionCommand> Secondary(string actionId)
        {
            if (Phase != InteractionPhase.Engaged || string.IsNullOrEmpty(actionId) || !actionId.StartsWith("emote:", StringComparison.Ordinal)) return None;
            return One(InteractionCommandKind.Emote, Target, actionId.Substring(6));
        }

        /// <summary>The CTA buttons to show now, given the target the finder offers (Idle only).</summary>
        public bool AcceptsPrompt(string actionId, string targetId, Interactable offered)
        {
            var target = Phase == InteractionPhase.Idle ? offered : Target;
            if (target == null || string.IsNullOrEmpty(targetId) || target.id != targetId) return false;
            foreach (var action in Actions(offered)) if (action.id == actionId) return true;
            return false;
        }

        /// <summary>The CTA buttons to show now, given the target the finder offers (Idle only).</summary>
        public List<InteractionAction> Actions(Interactable offered)
        {
            var list = new List<InteractionAction>();
            switch (Phase)
            {
                case InteractionPhase.Idle:
                    if (offered != null) list.Add(new InteractionAction("use", offered.VerbKey, offered.Icon, true));
                    break;
                case InteractionPhase.Approaching:
                case InteractionPhase.Requesting:
                    list.Add(new InteractionAction("cancel", "interact.cancel", "close", false));
                    break;
                case InteractionPhase.Engaged:
                    bool standing = Target != null && (Target.kind == InteractionKind.Counter || Target.kind == InteractionKind.Mic);
                    list.Add(new InteractionAction("stand", standing ? "interact.stepAway" : "interact.stand", "stand", true));
                    if (Target != null && Target.kind == InteractionKind.Mic && !LocalOnly)
                    {
                        list.Add(new InteractionAction("emote:emote_wave", "interact.wave", "emote", false));
                        list.Add(new InteractionAction("emote:emote_wagmi", "interact.hype", "emote", false));
                    }
                    break;
            }
            return list;
        }

        private static bool Same(Interactable a, Interactable b) => a != null && b != null && a.id == b.id;

        private void Reset() { Phase = InteractionPhase.Idle; Target = null; LocalOnly = false; _deadline = 0; }
    }

    /// <summary>Wire payloads for interactions (contract realtime/messages.ts: SitMessage {spotId: string.min(1)}, StandMessage {}).</summary>
    public static class InteractionNet
    {
        public static SitMessage Sit(Interactable it) => it != null && it.Networked ? new SitMessage { spotId = it.wireId } : null;
        public static StandMessage Stand() => new StandMessage();
        public static string Json(object message) => JsonConvert.SerializeObject(message);

        /// <summary>Contract check for a sit payload: a non-empty spot id that the registry defines for this venue.</summary>
        public static bool Valid(SitMessage m, Func<string, bool> registryHas) =>
            m != null && !string.IsNullOrEmpty(m.spotId) && (registryHas == null || registryHas(m.spotId));
    }
}
