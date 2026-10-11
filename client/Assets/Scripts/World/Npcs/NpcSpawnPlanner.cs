using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Where an NPC stands when the server has not placed it yet (pure; unit-tested).
    ///
    /// The venue room sends every NPC as `idle` with no `spotId` at join (backend main @ 7243073), so the client used to
    /// put all of them on the venue entrance, which is also the player's spawn: on arrival in Timeline Square five NPCs
    /// stood on the new player and hid them (first-time flow, 2026-10-08). Each NPC now gets its own "home" spot: an
    /// NPC/standing spot of the venue at least <see cref="MinDistance"/> from the entrance and off the camera's centre
    /// line (the strip straight ahead of the spawn, where the camera looks on arrival). The choice is stable per NPC id
    /// and two NPCs never share a home. When no spot qualifies, a ring beside the entrance is used instead.
    /// </summary>
    public static class NpcSpawnPlanner
    {
        public const float MinDistance = 2.5f;
        public const float CentreLineHalfWidth = 1.5f;
        public const float CentreLineLength = 12f;

        /// <summary>Spot kinds an NPC may idle at (seats and stages stay free for players).</summary>
        public static readonly string[] HomeKinds = { "npc", "standing", "stand" };

        public static bool IsHomeKind(string kind)
        {
            foreach (var k in HomeKinds) if (k == kind) return true;
            return false;
        }

        /// <summary>True when a venue-local point is far enough from the entrance and off the camera's centre line.</summary>
        public static bool Allowed(Vector2 point, Vector2 entrance, Vector2 forward)
        {
            var d = point - entrance;
            if (d.magnitude < MinDistance) return false;
            var f = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector2.up;
            float along = Vector2.Dot(d, f);
            float lateral = Mathf.Abs(d.x * f.y - d.y * f.x);
            return !(along > 0f && along < CentreLineLength && lateral < CentreLineHalfWidth);
        }

        /// <summary>
        /// The candidate id for this NPC, or null when none is free and allowed. Candidates are tried in id order starting
        /// from a stable hash of the NPC id, so the same NPC lands on the same spot on every device and every join.
        /// </summary>
        public static string Pick(string npcId, IList<(string id, Vector2 pos)> candidates, Vector2 entrance, Vector2 forward,
            ICollection<string> taken)
        {
            if (candidates == null || candidates.Count == 0) return null;
            var allowed = new List<(string id, Vector2 pos)>();
            foreach (var c in candidates)
                if (!string.IsNullOrEmpty(c.id) && Allowed(c.pos, entrance, forward)) allowed.Add(c);
            if (allowed.Count == 0) return null;
            allowed.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            int start = (int)(StableHash(npcId ?? "") % (uint)allowed.Count);
            for (int i = 0; i < allowed.Count; i++)
            {
                var c = allowed[(start + i) % allowed.Count];
                if (taken == null || !taken.Contains(c.id)) return c.id;
            }
            return null;
        }

        /// <summary>
        /// Fallback home number <paramref name="index"/> (0, 1, 2…): beside the entrance, alternating right and left,
        /// 2.5 m, 3.5 m, 4.5 m… out and 1 m in, so it is never on the centre line and never on the spawn.
        /// </summary>
        public static Vector2 Ring(int index, Vector2 entrance, Vector2 forward)
        {
            var f = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector2.up;
            var right = new Vector2(f.y, -f.x);
            float side = index % 2 == 0 ? 1f : -1f;
            float lateral = MinDistance + (index / 2) * 1f;
            return entrance + right * (side * lateral) + f * 1f;
        }

        /// <summary>Venue forward from the entrance spot's rotation (radians, Unity yaw: 0 = +z).</summary>
        public static Vector2 Forward(float rotYRad) => new Vector2(Mathf.Sin(rotYRad), Mathf.Cos(rotYRad));

        /// <summary>FNV-1a: stable across runs and platforms (string.GetHashCode is not).</summary>
        public static uint StableHash(string s)
        {
            uint h = 2166136261;
            foreach (char ch in s) { h ^= ch; h *= 16777619; }
            return h;
        }
    }
}
