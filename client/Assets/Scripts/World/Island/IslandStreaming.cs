using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// A venue placed on CT Island (IslandV2Builder fills these). The diorama's local frame sits at <see cref="origin"/>
    /// (island x/z) and <see cref="elevation"/> (y), turned by <see cref="yaw"/> degrees clockwise seen from above (Unity's
    /// +Y rotation), so its door faces its street. Rects named local* are venue-local; frame/walk are their island-space
    /// bounding boxes (kept for coarse checks and older callers).
    /// </summary>
    [Serializable]
    public sealed class IslandVenue
    {
        public string venueId, district;
        public Vector2 origin;      // venue-local (0,0) on the island
        public Rect frame;          // island-space AABB of the baked map frame
        public Rect walk;           // island-space AABB of the walkable rect (its room's server bounds)
        public Vector2 exit;        // the door, on the island
        public float yaw;           // degrees clockwise from north
        public float elevation;     // y of the venue's local ground
        public Rect localFrame;     // venue-local frame (min/size)
        public Rect localWalk;      // venue-local walkable rect
        public float shellHeight;   // the street-side building's wall height (0 = open air): diorama content above is trimmed

        public bool HasLocal => localWalk.width > 0f && localWalk.height > 0f;
        public Quaternion Rotation => Quaternion.Euler(0f, yaw, 0f);
        public Vector3 Position => new Vector3(origin.x, elevation, origin.y);

        /// <summary>Island x/z to venue-local x/z (inverse of the venue root's pose).</summary>
        public Vector2 ToLocal(Vector2 p)
        {
            float a = yaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            float dx = p.x - origin.x, dz = p.y - origin.y;
            return new Vector2(dx * c - dz * s, dx * s + dz * c);
        }

        public Vector2 ToIsland(Vector2 local)
        {
            float a = yaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(origin.x + local.x * c + local.y * s, origin.y - local.x * s + local.y * c);
        }
    }

    /// <summary>
    /// Pure open-world rules (EditMode-tested): which venue zone a point is in, which dioramas to stream in and out (with
    /// hysteresis), and the room handoff decision (join the next zone's room before leaving the current one; keep the
    /// current room a while on the street so a quick step outside does not churn rooms). Rotated venues are tested in
    /// their own local frame.
    /// </summary>
    public static class IslandStreaming
    {
        public const float ZoneMargin = 0.3f, LoadRadius = 42f, UnloadRadius = 64f, LeaveRoomDistance = 8f;

        public static float DistanceToRect(Vector2 p, Rect r)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax), dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Distance from an island point to a venue's walkable area (its own frame when rotated).</summary>
        public static float DistanceToWalk(Vector2 p, IslandVenue v) =>
            v.HasLocal ? DistanceToRect(v.ToLocal(p), v.localWalk) : DistanceToRect(p, v.walk);

        public static float DistanceToFrame(Vector2 p, IslandVenue v) =>
            v.HasLocal && v.localFrame.width > 0f ? DistanceToRect(v.ToLocal(p), v.localFrame) : DistanceToRect(p, v.frame);

        /// <summary>The venue whose walkable rect (plus a small margin) contains <paramref name="p"/>, or null (the street).</summary>
        public static IslandVenue ZoneAt(Vector2 p, IReadOnlyList<IslandVenue> venues, float margin = ZoneMargin)
        {
            IslandVenue best = null; float bd = float.MaxValue;
            foreach (var v in venues)
            {
                Rect w; Vector2 q;
                if (v.HasLocal) { w = v.localWalk; q = v.ToLocal(p); }
                else { w = v.walk; q = p; }
                var r = new Rect(w.xMin - margin, w.yMin - margin, w.width + 2f * margin, w.height + 2f * margin);
                if (!r.Contains(q)) continue;
                float d = (q - w.center).sqrMagnitude;
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        /// <summary>Streaming plan: load (nearest first) inside LoadRadius, unload beyond UnloadRadius (the band between keeps).</summary>
        public static (List<IslandVenue> load, List<string> unload) Plan(Vector2 p, IReadOnlyList<IslandVenue> venues, ICollection<string> loaded,
            float loadRadius = LoadRadius, float unloadRadius = UnloadRadius, IReadOnlyList<Vector2> extra = null)
        {
            var load = new List<IslandVenue>(); var unload = new List<string>();
            float Dist(IslandVenue v)
            {
                float d = DistanceToFrame(p, v);
                if (extra != null) foreach (var e in extra) d = Mathf.Min(d, DistanceToFrame(e, v));
                return d;
            }
            foreach (var v in venues)
            {
                float d = Dist(v);
                bool isLoaded = loaded.Contains(v.venueId);
                if (!isLoaded && d <= loadRadius) load.Add(v);
                else if (isLoaded && d > unloadRadius) unload.Add(v.venueId);
            }
            load.Sort((a, b) => Dist(a).CompareTo(Dist(b)));
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
            if (room != null && DistanceToWalk(p, room) > LeaveRoomDistance) return RoomAction.Leave;
            return RoomAction.None;
        }

        // ------------------------------------------------------------------ cells

        /// <summary>
        /// Cells to stream: every cell whose rect is within <paramref name="loadRadius"/> of the player or of a prefetch point
        /// (nearest first), and loaded cells farther than <paramref name="unloadRadius"/> from all of them.
        /// </summary>
        public static (List<int> load, List<int> unload) PlanCells(Vector2 p, IReadOnlyList<Rect> cells, ICollection<int> loaded,
            float loadRadius, float unloadRadius, IReadOnlyList<Vector2> extra = null)
        {
            var load = new List<int>(); var unload = new List<int>();
            var dist = new float[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                float d = DistanceToRect(p, cells[i]);
                if (extra != null) foreach (var e in extra) d = Mathf.Min(d, DistanceToRect(e, cells[i]) * 1.15f);
                dist[i] = d;
                bool isLoaded = loaded.Contains(i);
                if (!isLoaded && d <= loadRadius) load.Add(i);
                else if (isLoaded && d > unloadRadius) unload.Add(i);
            }
            load.Sort((a, b) => dist[a].CompareTo(dist[b]));
            return (load, unload);
        }

        /// <summary>Points along a predicted path (position + velocity * t) every <paramref name="step"/> seconds.</summary>
        public static List<Vector2> PredictPath(Vector3 position, Vector3 velocity, float horizonSeconds, float step = 1f)
        {
            var o = new List<Vector2>();
            if (horizonSeconds <= 0f || velocity.sqrMagnitude < 0.25f) return o;
            for (float t = step; t <= horizonSeconds + 1e-3f; t += step)
                o.Add(new Vector2(position.x + velocity.x * t, position.z + velocity.z * t));
            return o;
        }
    }
}
