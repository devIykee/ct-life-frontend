using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>A venue placed on CT Island (atlas metres = island world x/z; IslandBuilder fills these).</summary>
    [Serializable]
    public sealed class IslandVenue
    {
        public string venueId, district;
        public Vector2 origin;      // venue-local (0,0) on the island
        public Rect frame;          // the baked map frame (streaming distance is measured to it)
        public Rect walk;           // the venue's walkable rect (its room's server bounds) on the island
        public Vector2 exit;        // the door, on the island
    }

    /// <summary>
    /// Pure open-world rules (Phase B, EditMode-tested): which venue zone a point is in, which dioramas to stream in and out
    /// (with hysteresis), and the room handoff decision (join the next zone's room before leaving the current one; keep the
    /// current room a while on the street so a quick step outside does not churn rooms).
    /// </summary>
    public static class IslandStreaming
    {
        public const float ZoneMargin = 0.3f, LoadRadius = 42f, UnloadRadius = 64f, LeaveRoomDistance = 8f;

        public static float DistanceToRect(Vector2 p, Rect r)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax), dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>The venue whose walkable rect (plus a small margin) contains <paramref name="p"/>, or null (the street).</summary>
        public static IslandVenue ZoneAt(Vector2 p, IReadOnlyList<IslandVenue> venues, float margin = ZoneMargin)
        {
            IslandVenue best = null; float bd = float.MaxValue;
            foreach (var v in venues)
            {
                var r = new Rect(v.walk.xMin - margin, v.walk.yMin - margin, v.walk.width + 2f * margin, v.walk.height + 2f * margin);
                if (!r.Contains(p)) continue;
                float d = (p - v.walk.center).sqrMagnitude;
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        /// <summary>Streaming plan: load (nearest first) inside LoadRadius, unload beyond UnloadRadius (the band between keeps).</summary>
        public static (List<IslandVenue> load, List<string> unload) Plan(Vector2 p, IReadOnlyList<IslandVenue> venues, ICollection<string> loaded,
            float loadRadius = LoadRadius, float unloadRadius = UnloadRadius)
        {
            var load = new List<IslandVenue>(); var unload = new List<string>();
            foreach (var v in venues)
            {
                float d = DistanceToRect(p, v.frame);
                bool isLoaded = loaded.Contains(v.venueId);
                if (!isLoaded && d <= loadRadius) load.Add(v);
                else if (isLoaded && d > unloadRadius) unload.Add(v.venueId);
            }
            load.Sort((a, b) => DistanceToRect(p, a.frame).CompareTo(DistanceToRect(p, b.frame)));
            return (load, unload);
        }

        public enum RoomAction { None, Join, Leave }

        /// <summary>
        /// Room handoff: entering a different zone joins its room (the caller joins first, then leaves the old one); on the
        /// street the current room is kept until the player is <see cref="LeaveRoomDistance"/> from its zone.
        /// </summary>
        public static RoomAction Decide(Vector2 p, IslandVenue zone, IslandVenue room)
        {
            if (zone != null) return room == null || room.venueId != zone.venueId ? RoomAction.Join : RoomAction.None;
            if (room != null && DistanceToRect(p, room.walk) > LeaveRoomDistance) return RoomAction.Leave;
            return RoomAction.None;
        }
    }
}
