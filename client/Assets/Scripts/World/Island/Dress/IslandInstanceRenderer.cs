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
    /// Camera.main (Feel's camera). LOD switches cross-fade with a screen-space dither over a short distance band (the
    /// fading draws use a _CTLODFADE copy of the material, so the rest keep early depth); buildings cast their shadows
    /// from LOD1; chunks beyond the sun's shadow reach cast none.
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
        [Tooltip("LOD switches cross-fade (dithered) over this share of the switch distance, clamped to 2-25 m.")]
        public float fadeBand = 0.1f;
        [Tooltip("Instances further than the sun's shadow distance plus this cast no shadows (they cannot reach the map).")]
        public float shadowCasterMargin = 50f;

        private const int Batch = 1023;
        // mode: 0 drawn as usual, 1 beyond the shadow casters' reach (no shadow), 2 outgoing LOD of a cross-fade,
        // 3 incoming LOD of a cross-fade (no shadow: the outgoing one still casts)
        private sealed class Stage { public Matrix4x4[] m = new Matrix4x4[Batch]; public Vector4[] t = new Vector4[Batch]; public float[] f = new float[Batch]; public int n; public bool tinted; }
        private readonly Dictionary<(int, int, int), List<Stage>> _stages = new Dictionary<(int, int, int), List<Stage>>();
        private readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
        private readonly Dictionary<Material, Material> _fadeMats = new Dictionary<Material, Material>();
        private readonly Plane[] _planes = new Plane[6];
        private static readonly int LodFadeId = Shader.PropertyToID("_LodFade");

        public static void Register(IslandInstances s) { if (!Sets.Contains(s)) Sets.Add(s); }
        public static void Unregister(IslandInstances s) => Sets.Remove(s);

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || Sets.Count == 0) { LastDraws = 0; LastInstances = 0; return; }
            Render(cam);
        }

        private void OnDestroy()
        {
            foreach (var m in _fadeMats.Values) if (m != null) Destroy(m);
            _fadeMats.Clear();
        }

        /// <summary>The material's dithered cross-fade copy (_CTLODFADE): only fading draws pay for the discard.</summary>
        private Material FadeMat(Material m)
        {
            if (m == null) return null;
            if (!_fadeMats.TryGetValue(m, out var f) || f == null)
            {
                f = new Material(m) { name = m.name + " (lod fade)", hideFlags = HideFlags.DontSave };
                f.EnableKeyword("_CTLODFADE");
                _fadeMats[m] = f;
            }
            return f;
        }

        public void Render(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            for (int i = 0; i < 6; i++) _planes[i].distance += 24f;            // casters just outside the view
            var eye = cam.transform.position;
            float shadowReach = (UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset != null
                ? UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset.shadowDistance : QualitySettings.shadowDistance) + shadowCasterMargin;
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
                        float end = p.lodEnd[lod] * distanceScale;
                        float band = Mathf.Clamp(end * fadeBand, 2f, 25f);
                        // split off "too far to cast" only where this LOD would cast at all (no extra draws otherwise)
                        bool casts = shadows && p.shadowRange > 0f && (lod == 0 || p.lodEnd[lod - 1] < p.shadowRange);
                        int baseMode = casts && d > shadowReach ? 1 : 0;
                        if (d > end - band)
                        {
                            float t = Mathf.Clamp01((d - (end - band)) / band);
                            Append(g.Key, lod, 2, ch, -t);                         // outgoing (or fading out at the cull distance)
                            if (lod < lodCount - 1) Append(g.Key, lod + 1, 3, ch, t);
                        }
                        else Append(g.Key, lod, baseMode, ch, 0f);
                    }
                }
            }
            if (kit == null) return;
            int draws = 0, inst = 0, block = 0;
            foreach (var kv in _stages)
            {
                var (protoI, lodI, mode) = kv.Key;
                var p = kit.protos[protoI];
                var mesh = p.lods[lodI];
                if (mesh == null || p.material == null) continue;
                bool castShadow = shadows && mode != 1 && mode != 3 && p.shadowRange > 0f && (lodI == 0 || p.lodEnd[lodI - 1] < p.shadowRange);
                // buildings cast from their LOD1 (about a quarter of LOD0's triangles; the shadow map cannot tell)
                Mesh shadowMesh = castShadow && lodI == 0 && p.kind == "building" && p.lods.Length > 1 && p.lods[1] != null
                    && p.lods[1].subMeshCount == mesh.subMeshCount ? p.lods[1] : null;
                bool fade = mode >= 2;
                int subs = mesh.subMeshCount;
                foreach (var st in kv.Value)
                {
                    if (st.n == 0) continue;
                    MaterialPropertyBlock mpb = null;
                    if (st.tinted || fade)
                    {
                        if (block >= _blocks.Count) _blocks.Add(new MaterialPropertyBlock());
                        mpb = _blocks[block++];
                        mpb.Clear();
                        if (st.tinted) mpb.SetVectorArray(TintId, st.t);
                        if (fade) mpb.SetFloatArray(LodFadeId, st.f);
                    }
                    for (int sm = 0; sm < subs; sm++)
                    {
                        var mat = lodI < p.lodMaterials.Length && p.lodMaterials[lodI] != null ? p.lodMaterials[lodI]
                            : sm < p.materials.Length && p.materials[sm] != null ? p.materials[sm] : p.material;
                        if (fade) mat = FadeMat(mat);
                        var rp = new RenderParams(mat)
                        {
                            layer = p.layer, receiveShadows = true,
                            shadowCastingMode = castShadow && shadowMesh == null ? ShadowCastingMode.On : ShadowCastingMode.Off,
                            worldBounds = new Bounds(eye, Vector3.one * 8000f),
                            camera = TargetCamera,
                        };
                        if (mpb != null) rp.matProps = mpb;
                        Graphics.RenderMeshInstanced(rp, mesh, sm, st.m, st.n);
                        draws++;
                        if (shadowMesh != null)
                        {
                            var srp = rp;
                            srp.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                            Graphics.RenderMeshInstanced(srp, shadowMesh, sm, st.m, st.n);
                            draws++;
                        }
                    }
                    inst += st.n;
                }
            }
            LastDraws = draws; LastInstances = inst;
        }

        private void Append(int proto, int lod, int mode, IslandInstances.Chunk ch, float fade)
        {
            var key = (proto, lod, mode);
            if (!_stages.TryGetValue(key, out var list)) _stages[key] = list = new List<Stage> { new Stage() };
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
                if (mode >= 2) for (int i = 0; i < k; i++) st.f[st.n + i] = fade;
                st.tinted |= ch.tinted;
                st.n += k; src += k;
            }
        }
    }
}
