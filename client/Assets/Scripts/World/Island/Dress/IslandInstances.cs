using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// One streaming cell's dressing (World lane): instance records baked by the island builder (CTI1 binary: per
    /// prototype group, x, y, z, yaw degrees, horizontal scale, vertical scale, packed RGBA tint). Decoded into spatial
    /// chunks of matrices when the cell loads; drawn by <see cref="IslandInstanceRenderer"/>. Colliders are separate
    /// (boxes and capsules on the cell's own GameObjects).
    /// </summary>
    public sealed class IslandInstances : MonoBehaviour
    {
        public const int Magic = 0x31495443;   // "CTI1"
        public const float ChunkSize = 48f;

        public IslandKit kit;
        public TextAsset data;

        public sealed class Chunk
        {
            public Bounds bounds;
            public Matrix4x4[] matrices;
            public Vector4[] tints;
            public bool tinted;
        }

        /// <summary>Per prototype index: its chunks in this cell.</summary>
        public readonly Dictionary<int, List<Chunk>> Groups = new Dictionary<int, List<Chunk>>();
        public int Count { get; private set; }
        private readonly List<BoxCollider> _decorVehicleColliders = new List<BoxCollider>();
        /// <summary>Instances with no baked collider that must still be solid (trunks, posts, statues); see IslandSolidProxies.</summary>
        public readonly List<IslandSolidProxies.Solid> Solids = new List<IslandSolidProxies.Solid>();

        private void OnEnable()
        {
            if (Groups.Count == 0) Decode();
            IslandInstanceRenderer.Register(this);
            IslandSolidProxies.Register(this);
        }

        private void OnDisable() { IslandInstanceRenderer.Unregister(this); IslandSolidProxies.Unregister(this); }

        public void Decode()
        {
            Groups.Clear();
            Solids.Clear();
            Count = 0;
            foreach (var collider in _decorVehicleColliders) if (collider != null) collider.enabled = false;
            if (data == null || kit == null) return;
            var b = data.bytes;
            if (b.Length < 8 || BitConverter.ToInt32(b, 0) != Magic) { Debug.LogWarning($"CTLIFE [island] {name}: bad instance data"); return; }
            int groups = BitConverter.ToInt32(b, 4);
            int o = 8;
            int decorVehicles = 0;
            var parked = (transform.parent != null ? transform.parent : transform).GetComponentsInChildren<ParkedCar>(true);
            for (int g = 0; g < groups; g++)
            {
                int proto = BitConverter.ToInt32(b, o); int n = BitConverter.ToInt32(b, o + 4); o += 8;
                if (proto < 0 || proto >= kit.protos.Length || kit.protos[proto].lods.Length == 0) { o += n * 28; continue; }
                var lb = kit.protos[proto].bounds;
                bool solid = IslandSolidProxies.KindFor(kit.protos[proto]) != IslandSolidProxies.Kind.None;
                var buckets = new Dictionary<long, List<(Matrix4x4 m, Vector4 t, Bounds wb)>>();
                bool tinted = false;
                for (int i = 0; i < n; i++, o += 28)
                {
                    float x = BitConverter.ToSingle(b, o), y = BitConverter.ToSingle(b, o + 4), z = BitConverter.ToSingle(b, o + 8);
                    float yaw = BitConverter.ToSingle(b, o + 12), sxz = BitConverter.ToSingle(b, o + 16), sy = BitConverter.ToSingle(b, o + 20);
                    uint c = BitConverter.ToUInt32(b, o + 24);
                    var t = new Vector4((c & 255) / 255f, ((c >> 8) & 255) / 255f, ((c >> 16) & 255) / 255f, ((c >> 24) & 255) / 255f);
                    tinted |= t.w > 0f;
                    var m = Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f), new Vector3(sxz, sy, sxz));
                    var p = kit.protos[proto];
                    // Street markers own collision and disable it when swapping in a drivable car. Plain shot-dressing
                    // cars have no marker; give only those a static prop box at the same authored instance transform.
                    if (p.kind == "vehicle" && !Array.Exists(parked, car => car.set == this && car.model == p.id &&
                        (car.transform.position - new Vector3(x, y, z)).sqrMagnitude <= 0.5625f))
                        DecorVehicleCollider(decorVehicles++, p, new Vector3(x, y, z), yaw, sxz, sy);
                    if (solid) Solids.Add(new IslandSolidProxies.Solid { proto = proto, position = new Vector3(x, y, z), yaw = yaw, sxz = sxz, sy = sy });
                    var wb = TransformBounds(m, lb);
                    long key = ((long)Mathf.FloorToInt(x / ChunkSize) << 32) ^ (uint)Mathf.FloorToInt(z / ChunkSize);
                    if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<(Matrix4x4, Vector4, Bounds)>();
                    list.Add((m, t, wb));
                }
                var chunks = new List<Chunk>();
                foreach (var kv in buckets)
                {
                    var list = kv.Value;
                    var ch = new Chunk { matrices = new Matrix4x4[list.Count], tints = new Vector4[list.Count], tinted = tinted, bounds = list[0].wb };
                    for (int i = 0; i < list.Count; i++) { ch.matrices[i] = list[i].m; ch.tints[i] = list[i].t; ch.bounds.Encapsulate(list[i].wb); }
                    chunks.Add(ch);
                }
                Groups[proto] = chunks;
                Count += n;
            }
        }

        private void DecorVehicleCollider(int index, IslandKit.Proto proto, Vector3 position, float yaw, float sxz, float sy)
        {
            BoxCollider collider;
            if (index < _decorVehicleColliders.Count && _decorVehicleColliders[index] != null)
                collider = _decorVehicleColliders[index];
            else
            {
                var go = new GameObject("Decor vehicle collider") { layer = IslandRenderSettings.Prop };
                go.transform.SetParent(transform, false);
                collider = go.AddComponent<BoxCollider>();
                if (index < _decorVehicleColliders.Count) _decorVehicleColliders[index] = collider;
                else _decorVehicleColliders.Add(collider);
            }
            collider.name = "Decor vehicle collider: " + proto.id;
            collider.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            collider.transform.localScale = new Vector3(sxz, sy, sxz);
            collider.center = proto.bounds.center;
            // Match other solid island props: stay within 2 cm of the visual bounds rather than shrinking by a percentage.
            var size = proto.bounds.size;
            collider.size = new Vector3(Mathf.Max(.04f, size.x - .04f), Mathf.Max(.04f, size.y - .02f), Mathf.Max(.04f, size.z - .04f));
            collider.enabled = true;
        }

        private readonly Dictionary<(int, int, int), Matrix4x4> _hidden = new Dictionary<(int, int, int), Matrix4x4>();

        /// <summary>Hides (or shows again) the instance of a model nearest to a position (within 0.75 m).</summary>
        public bool SetHidden(string protoId, Vector3 position, bool hidden)
        {
            if (kit == null) return false;
            int proto = kit.IndexOf(protoId);
            if (proto < 0 || !Groups.TryGetValue(proto, out var chunks)) return false;
            for (int c = 0; c < chunks.Count; c++)
            {
                var ch = chunks[c];
                for (int i = 0; i < ch.matrices.Length; i++)
                {
                    var key = (proto, c, i);
                    var m = _hidden.TryGetValue(key, out var orig) ? orig : ch.matrices[i];
                    var t = new Vector3(m.m03, m.m13, m.m23);
                    if ((t - position).sqrMagnitude > 0.5625f) continue;
                    if (hidden) { if (!_hidden.ContainsKey(key)) _hidden[key] = ch.matrices[i]; ch.matrices[i] = Matrix4x4.zero; }
                    else if (_hidden.Remove(key, out var back)) ch.matrices[i] = back;
                    return true;
                }
            }
            return false;
        }

        private static Bounds TransformBounds(Matrix4x4 m, Bounds b)
        {
            var c = m.MultiplyPoint3x4(b.center);
            var e = b.extents;
            var ax = m.MultiplyVector(new Vector3(e.x, 0, 0)); var ay = m.MultiplyVector(new Vector3(0, e.y, 0)); var az = m.MultiplyVector(new Vector3(0, 0, e.z));
            var ext = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x), Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y), Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            return new Bounds(c, ext * 2f);
        }
    }
}
