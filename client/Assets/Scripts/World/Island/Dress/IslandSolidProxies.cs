using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Solid stand-ins for dressing that has no baked collider (2026-10-22: "you can walk through the trees and lamps").
    /// Trees get a trunk capsule (never the canopy), lamps / traffic lights / sign and power poles a thin post capsule, hero
    /// statues a box. The island has ~14k such instances (mostly forest), so instead of a collider each, a pool of primitive
    /// colliders on the Prop layer covers only those within <see cref="Radius"/> of the player (or the car they drive), and
    /// is re-assigned a few times a second. Players and vehicles both collide with the Prop layer. Bushes, grass and flowers
    /// stay walk-through.
    /// </summary>
    public sealed class IslandSolidProxies : MonoBehaviour
    {
        public const float Radius = 48f, Every = 0.2f;
        public const int Max = 420;

        public enum Kind { None, Trunk, Post, Box }

        public struct Solid
        {
            public int proto;
            public Vector3 position;
            public float yaw, sxz, sy;
        }

        private sealed class Slot { public Collider collider; public Kind kind; public long key; public IslandInstances set; }

        private static readonly List<IslandInstances> Sets = new List<IslandInstances>();
        private static IslandSolidProxies _instance;
        private readonly Dictionary<long, Slot> _active = new Dictionary<long, Slot>();
        private readonly List<Slot> _free = new List<Slot>();
        private readonly List<(long key, IslandInstances set, int index, float d2)> _wanted = new List<(long, IslandInstances, int, float)>();
        private readonly HashSet<long> _wantedKeys = new HashSet<long>();
        private readonly List<long> _drop = new List<long>();
        private float _next;

        /// <summary>The runtime shape for a prototype without baked collision (None = it has its own, or it is soft).</summary>
        public static Kind KindFor(IslandKit.Proto p)
        {
            if (p == null) return Kind.None;
            string id = (p.id ?? "").ToLowerInvariant();
            if (id.Contains("bush") || id.Contains("flower") || id.Contains("grass")) return Kind.None;
            switch (p.kind)
            {
                case "tree": return Kind.Trunk;
                case "lamp": case "pole": return Kind.Post;
                case "hero": return Kind.Box;
                default: return Kind.None;
            }
        }

        public static void Register(IslandInstances set)
        {
            if (set == null || Sets.Contains(set)) return;
            Sets.Add(set);
            if (_instance == null && Application.isPlaying)
            {
                var go = new GameObject("Island solid proxies");
                _instance = go.AddComponent<IslandSolidProxies>();
            }
        }

        public static void Unregister(IslandInstances set) => Sets.Remove(set);

        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Every;
            var me = VenueSession.Current != null ? VenueSession.Current.LocalPlayer : null;
            Vector3 center;
            if (me != null) center = me.transform.position;
            else if (Camera.main != null) center = Camera.main.transform.position;
            else return;

            _wanted.Clear(); _wantedKeys.Clear();
            float r2 = Radius * Radius;
            for (int s = Sets.Count - 1; s >= 0; s--)
            {
                var set = Sets[s];
                if (set == null) { Sets.RemoveAt(s); continue; }
                if (!set.isActiveAndEnabled) continue;
                var list = set.Solids;
                long id = (long)set.GetInstanceID() << 32;
                for (int i = 0; i < list.Count; i++)
                {
                    var d = list[i].position - center; d.y *= 0.5f;
                    float d2 = d.sqrMagnitude;
                    if (d2 <= r2) _wanted.Add((id | (uint)i, set, i, d2));
                }
            }
            if (_wanted.Count > Max) { _wanted.Sort((a, b) => a.d2.CompareTo(b.d2)); _wanted.RemoveRange(Max, _wanted.Count - Max); }
            foreach (var w in _wanted) _wantedKeys.Add(w.key);

            _drop.Clear();
            foreach (var kv in _active)
                if (!_wantedKeys.Contains(kv.Key) || kv.Value.set == null) _drop.Add(kv.Key);
            foreach (var k in _drop)
            {
                var slot = _active[k]; _active.Remove(k);
                slot.collider.enabled = false; slot.set = null;
                _free.Add(slot);
            }
            foreach (var w in _wanted)
            {
                if (_active.ContainsKey(w.key)) continue;
                var solid = w.set.Solids[w.index];
                var proto = w.set.kit != null && solid.proto < w.set.kit.protos.Length ? w.set.kit.protos[solid.proto] : null;
                var kind = KindFor(proto);
                if (kind == Kind.None) continue;
                var slot = Take(kind);
                slot.key = w.key; slot.set = w.set;
                Configure(slot, proto, solid);
                _active[w.key] = slot;
            }
        }

        private Slot Take(Kind kind)
        {
            bool capsule = kind != Kind.Box;
            for (int i = _free.Count - 1; i >= 0; i--)
                if ((_free[i].collider is CapsuleCollider) == capsule) { var s = _free[i]; _free.RemoveAt(i); s.kind = kind; return s; }
            var go = new GameObject(capsule ? "Solid proxy (capsule)" : "Solid proxy (box)") { layer = IslandRenderSettings.Prop };
            go.transform.SetParent(transform, false);
            Collider c = capsule ? go.AddComponent<CapsuleCollider>() : (Collider)go.AddComponent<BoxCollider>();
            return new Slot { collider = c, kind = kind };
        }

        private static void Configure(Slot slot, IslandKit.Proto p, Solid s)
        {
            var t = slot.collider.transform;
            var b = p.bounds;
            float sxz = Mathf.Abs(s.sxz), sy = Mathf.Abs(s.sy);
            if (slot.collider is CapsuleCollider cap)
            {
                float radius, height;
                if (slot.kind == Kind.Trunk)
                {
                    // the trunk, not the canopy: scales with the tree's footprint, ~0.15-0.4 m, up to head height and a bit
                    // (the LOLIPOP maples' crowns are 9-13 m wide over a slim trunk)
                    float k = p.id != null && p.id.StartsWith("lp_", System.StringComparison.Ordinal) ? 0.025f : 0.06f;
                    radius = Mathf.Clamp(k * Mathf.Max(b.size.x, b.size.z) * sxz, 0.15f, 0.4f);
                    height = Mathf.Max(radius * 2f, Mathf.Min(2.5f, b.max.y * sy));
                }
                else
                {
                    radius = Mathf.Clamp((p.kind == "pole" ? 0.16f : 0.1f) * sxz, 0.06f, 0.2f);
                    height = Mathf.Max(radius * 2f, b.max.y * sy);
                }
                t.SetPositionAndRotation(s.position, Quaternion.Euler(0f, s.yaw, 0f));
                t.localScale = Vector3.one;
                cap.direction = 1; cap.radius = radius; cap.height = height; cap.center = new Vector3(0f, height * 0.5f, 0f);
            }
            else if (slot.collider is BoxCollider box)
            {
                t.SetPositionAndRotation(s.position, Quaternion.Euler(0f, s.yaw, 0f));
                t.localScale = new Vector3(sxz, sy, sxz);
                box.center = b.center;
                box.size = new Vector3(Mathf.Max(.04f, b.size.x - .04f), Mathf.Max(.04f, b.size.y - .02f), Mathf.Max(.04f, b.size.z - .04f));
            }
            slot.collider.enabled = true;
        }
    }
}
