using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Picks the interactable the CTA offers: the nearest available one within its range of the actor, preferring what
    /// the actor faces, with hysteresis so the prompt does not flicker between two neighbouring chairs. A tapped
    /// (focused) target is offered from farther away until the player walks off.
    /// </summary>
    public static class InteractionFinder
    {
        public const float Hysteresis = 1.25f;    // keep the current target until 25% beyond its range
        public const float FocusRange = 14f;      // a tapped target stays offered within this distance
        public const float FacingPenalty = 0.6f;  // metres added for a target directly behind

        public static Interactable Best(Vector3 actor, Vector3 forward, IReadOnlyList<Interactable> all,
            Func<Interactable, bool> available, Interactable current, Interactable focused = null)
        {
            if (focused != null && available(focused) && Flat(actor, focused.approach) <= FocusRange) return focused;
            Interactable best = null;
            float bestScore = float.MaxValue;
            forward.y = 0;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                if (it == null || !available(it)) continue;
                float d = Mathf.Min(Flat(actor, it.approach), Flat(actor, it.position));
                float range = ReferenceEquals(it, current) ? it.range * Hysteresis : it.range;
                if (d > range) continue;
                var to = it.position - actor; to.y = 0;
                float facing = to.sqrMagnitude > 1e-4f ? Vector3.Angle(forward, to) / 180f : 0f;
                float score = d + facing * FacingPenalty - (ReferenceEquals(it, current) ? 0.3f : 0f);
                if (score < bestScore) { bestScore = score; best = it; }
            }
            return best;
        }

        public static float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }
    }

    /// <summary>
    /// Who occupies which spot right now (players' `spotId`, NPCs acting at a spot, and this client's own claim). Keys are
    /// local spot ids, so registry wire forms (`venue.spot`) and authored anchors compare equal.
    /// </summary>
    public sealed class SeatOccupancy
    {
        private readonly Dictionary<string, string> _byOccupant = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _bySpot = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly string _venuePrefix;

        public SeatOccupancy(string venueId) { _venuePrefix = string.IsNullOrEmpty(venueId) ? null : venueId + "."; }

        public string Key(string spotId) =>
            spotId != null && _venuePrefix != null && spotId.StartsWith(_venuePrefix, StringComparison.Ordinal) ? spotId.Substring(_venuePrefix.Length) : spotId;

        /// <summary>Set (or clear with null/empty) the spot an occupant (session id, "npc:&lt;id&gt;") holds.</summary>
        public void Set(string occupant, string spotId)
        {
            if (string.IsNullOrEmpty(occupant)) return;
            if (_byOccupant.TryGetValue(occupant, out var old))
            {
                if (_bySpot.TryGetValue(old, out var who) && who == occupant) _bySpot.Remove(old);
                _byOccupant.Remove(occupant);
            }
            var key = Key(spotId);
            if (string.IsNullOrEmpty(key)) return;
            _byOccupant[occupant] = key;
            _bySpot[key] = occupant;
        }

        public void Remove(string occupant) => Set(occupant, null);

        public string OccupantOf(string spotId) => _bySpot.TryGetValue(Key(spotId) ?? "", out var who) ? who : null;

        /// <summary>Free for <paramref name="me"/>: nobody holds it, or I do. Doors and counters are never exclusive.</summary>
        public bool IsFree(Interactable it, string me)
        {
            if (it == null) return false;
            if (it.kind == InteractionKind.Door || it.kind == InteractionKind.Counter) return true;
            var who = OccupantOf(it.id);
            return who == null || who == me;
        }

        public int Count => _bySpot.Count;
    }
}
