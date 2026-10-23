using System;
using UnityEngine;

namespace CTLife.World
{
    public enum InteractionKind { Seat, Bench, Sofa, StageSeat, Terminal, Mic, Counter, Door }

    /// <summary>
    /// One thing the player can choose to use in a venue: a seat, bench, sofa, terminal, the host's mic, a counter or a
    /// door. Built from the venue's authored SpotAnchors (and the contract spot registry); positions are world space.
    /// A non-empty <see cref="wireId"/> means the server owns it (`sit {spotId}` / `stand`), so everyone in the room sees
    /// it; otherwise it is presentation on this client only.
    /// </summary>
    public sealed class Interactable
    {
        public string id;          // venue-local spot id (table_1, bench_3, exit...)
        public string wireId;      // id sent in `sit` (registry form), or null when the contract has no message for it
        public InteractionKind kind;
        public string anim;        // loop while engaged (spot anim, or the FE presentation for local-only kinds)
        public Vector3 position;   // where the actor is while engaged (seat / stand point)
        public Vector3 approach;   // standing point to walk to first (on the NavMesh)
        public float yaw;          // degrees, facing while engaged
        public float range = 2.0f; // the CTA appears within this distance of the approach point
        /// <summary>Seats whose position is the floor under the seat's centre (walk-in rooms): the sitter first stands this far
        /// in front of it, facing away from the seat, then lowers back into it (0 = the position is already the feet point).</summary>
        public float sitStandOff;

        public bool Networked => !string.IsNullOrEmpty(wireId);
        public bool Sits => kind == InteractionKind.Seat || kind == InteractionKind.Bench || kind == InteractionKind.Sofa ||
                            kind == InteractionKind.StageSeat || kind == InteractionKind.Terminal;

        /// <summary>String key of the primary verb (UI Strings: "interact.*").</summary>
        public string VerbKey => kind switch
        {
            InteractionKind.Seat => "interact.sit",
            InteractionKind.Bench => "interact.sitBench",
            InteractionKind.Sofa => "interact.sitSofa",
            InteractionKind.StageSeat => "interact.sitStage",
            InteractionKind.Terminal => "interact.terminal",
            InteractionKind.Mic => "interact.mic",
            InteractionKind.Counter => "interact.counter",
            InteractionKind.Door => "interact.door",
            _ => "interact.use",
        };

        /// <summary>Icon kind for the CTA (UiIcon mapping).</summary>
        public string Icon => kind switch
        {
            InteractionKind.Terminal => "terminal",
            InteractionKind.Mic => "mic_stage",
            InteractionKind.Counter => "counter",
            InteractionKind.Door => "door",
            _ => "seat",
        };

        public override string ToString() => $"{kind}:{id}";
    }

    /// <summary>One CTA button: an action the player can take right now.</summary>
    public readonly struct InteractionAction : IEquatable<InteractionAction>
    {
        public readonly string id;       // "use", "stand", "cancel", or "emote:<animId>"
        public readonly string labelKey; // UI Strings key
        public readonly string icon;
        public readonly bool primary;

        public InteractionAction(string id, string labelKey, string icon, bool primary)
        {
            this.id = id; this.labelKey = labelKey; this.icon = icon; this.primary = primary;
        }

        public bool Equals(InteractionAction o) => id == o.id && labelKey == o.labelKey && icon == o.icon && primary == o.primary;
        public override bool Equals(object obj) => obj is InteractionAction o && Equals(o);
        public override int GetHashCode() => (id ?? "").GetHashCode() ^ (labelKey ?? "").GetHashCode();
        public override string ToString() => id;
    }
}
