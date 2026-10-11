using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.World
{
    /// <summary>
    /// Draws every loaded cell's kit instances (World lane). Once per frame, for each prototype: chunks outside the camera
    /// frustum (slightly widened for shadows) or beyond the prototype's cull distance are skipped, each remaining chunk
    /// picks an LOD by its distance, and the chunks' matrices are copied into per-(prototype, LOD) batches drawn with
    /// Graphics.RenderMeshInstanced (1023 per call, per-instance tint through a property block). One draw call per
    /// prototype and LOD in view, not per chunk or object. Lives on the island root; works for whatever camera is
    /// Camera.main (Feel's camera).
    /// </summary>
    public sealed class IslandInstanceRenderer : MonoBehaviour
    {
        private static readonly List<IslandInstances> Sets = new List<IslandInstances>();
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        public static int LastDraws { get; private set; }
        /// <summary>Edit-time captures render through one camera; at runtime this stays null (every camera).</summary>
        public static Camera TargetCamera;
        /// <summary>Dev builds: instance kinds not drawn (launch extra `islandSkip=tree,grass`), for frame-rate bisection.</summary>
        public static readonly HashSet<string> SkipKinds = new HashSet<string>();
        public static int LastInstances { get; private set; }

        [Tooltip("Multiplies every prototype's LOD and cull distances (quality knob).")]
        public float distanceScale = 1f;
        public bool shadows = true;

        private const int Batch = 1023;
        private sealed class Stage { public Matrix4x4[] m = new Matrix4x4[Batch]; public Vector4[] t = new Vector4[Batch]; public int n; public bool tinted; }
        private readonly Dictionary<(int, int), List<Stage>> _stages = new Dictionary<(int, int), List<Stage>>();
        private readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
        private readonly Plane[] _planes = new Plane[6];

        public static void Register(IslandInstances s) { if (!Sets.Contains(s)) Sets.Add(s); }
        public static void Unregister(IslandInstances s) => Sets.Remove(s);

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || Sets.Count == 0) { LastDraws = 0; LastInstances = 0; return; }
            Render(cam);
        }

        public void Render(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            for (int i = 0; i < 6; i++) _planes[i].distance += 24f;            // casters just outside the view
            var eye = cam.transform.position;
            foreach (var list in _stages.Values) foreach (var st in list) { st.n = 0; st.tinted = false; }
            IslandKit kit = null;
            foreach (var set in Sets)
            {
                if (set == null || set.kit == null) continue;
                kit = set.kit;
                foreach (var g in set.Groups)
                {
                    var p = kit.protos[g.Key];
                    if (SkipKinds.Count > 0 && SkipKinds.Contains(p.kind)) continue;
                    int lodCount = Mathf.Min(p.lods.Length, p.lodEnd.Length);
                    if (lodCount == 0) continue;
                    float cull = p.lodEnd[lodCount - 1] * distanceScale;
                    foreach (var ch in g.Value)
                    {
                        float d = Mathf.Sqrt(ch.bounds.SqrDistance(eye));
                        if (d > cull) continue;
                        if (!GeometryUtility.TestPlanesAABB(_planes, ch.bounds)) continue;
                        int lod = 0;
                        while (lod < lodCount - 1 && d > p.lodEnd[lod] * distanceScale) lod++;
                        Append(g.Key, lod, ch);
                    }
                }
            }
            if (kit == null) return;
            int draws = 0, inst = 0, block = 0;
            foreach (var kv in _stages)
            {
                var p = kit.protos[kv.Key.Item1];
                var mesh = p.lods[kv.Key.Item2];
                if (mesh == null || p.material == null) continue;
                bool castShadow = shadows && p.shadowRange > 0f && (kv.Key.Item2 == 0 || p.lodEnd[kv.Key.Item2 - 1] < p.shadowRange);
                int subs = mesh.subMeshCount;
                foreach (var st in kv.Value)
                {
                    if (st.n == 0) continue;
                    MaterialPropertyBlock mpb = null;
                    if (st.tinted)
                    {
                        if (block >= _blocks.Count) _blocks.Add(new MaterialPropertyBlock());
                        mpb = _blocks[block++];
                        mpb.Clear();
                        mpb.SetVectorArray(TintId, st.t);
                    }
                    for (int sm = 0; sm < subs; sm++)
                    {
                        int lodI = kv.Key.Item2;
                        var mat = lodI < p.lodMaterials.Length && p.lodMaterials[lodI] != null ? p.lodMaterials[lodI]
                            : sm < p.materials.Length && p.materials[sm] != null ? p.materials[sm] : p.material;
                        var rp = new RenderParams(mat)
                        {
                            layer = p.layer, receiveShadows = true,
                            shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off,
                            worldBounds = new Bounds(eye, Vector3.one * 8000f),
                            camera = TargetCamera,
                        };
                        if (mpb != null) rp.matProps = mpb;
                        Graphics.RenderMeshInstanced(rp, mesh, sm, st.m, st.n);
                        draws++;
                    }
                    inst += st.n;
                }
            }
            LastDraws = draws; LastInstances = inst;
        }

        private void Append(int proto, int lod, IslandInstances.Chunk ch)
        {
            if (!_stages.TryGetValue((proto, lod), out var list)) _stages[(proto, lod)] = list = new List<Stage> { new Stage() };
            int src = 0, total = ch.matrices.Length;
            while (src < total)
            {
                var st = list[list.Count - 1];
                if (st.n == Batch)
                {
                    // reuse an empty stage if one exists further on, else grow
                    st = null;
                    foreach (var s in list) if (s.n < Batch) { st = s; break; }
                    if (st == null) { st = new Stage(); list.Add(st); }
                }
                int k = Mathf.Min(Batch - st.n, total - src);
                System.Array.Copy(ch.matrices, src, st.m, st.n, k);
                System.Array.Copy(ch.tints, src, st.t, st.n, k);
                st.tinted |= ch.tinted;
                st.n += k; src += k;
            }
        }
    }
}
