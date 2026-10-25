using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.World
{
    /// <summary>
    /// Draws every loaded cell's kit instances (World lane). Once per frame, for each prototype: chunks outside the camera
    /// frustum or beyond the prototype's cull distance are skipped, each remaining chunk
    /// picks an LOD by its distance, and the chunks' matrices are copied into per-(prototype, LOD) batches drawn with
    /// Graphics.RenderMeshInstanced (1023 per call, per-instance tint through a property block). One draw call per
    /// prototype and LOD in view, not per chunk or object. Lives on the island root; works for whatever camera is
    /// Camera.main (Feel's camera). LOD switches cross-fade with a screen-space dither over a short distance band (the
    /// fading draws use a _CTLODFADE copy of the material, so the rest keep early depth). Shadows are a separate
    /// shadows-only pass of just the instances whose shadow can reach the shadowed part of the view (AppendCasters);
    /// buildings cast from LOD1.
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
        /// <summary>Main-thread ms the last Render call took (perf harness).</summary>
        public static double LastCpuMs { get; private set; }
        /// <summary>Phases of the last Render (ms): occluder raster, chunk loop (culling, LOD, casters, copies), draw submission.</summary>
        public static double LastOccMs, LastLoopMs, LastSubmitMs;
        private readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();

        [Tooltip("Multiplies every prototype's LOD and cull distances (quality knob).")]
        public float distanceScale = 1f;
        public bool shadows = true;
        [Tooltip("LOD switches cross-fade (dithered) over this share of the switch distance, clamped to 2-25 m.")]
        public float fadeBand = 0.1f;
        [Tooltip("Upper bound: instances further than the sun's shadow distance plus this never cast (towers further away are not drawn into the map).")]
        public float shadowCasterMargin = 50f;

        private const int Batch = 1023;
        // mode: 0 drawn as usual, 2 outgoing LOD of a cross-fade, 3 incoming LOD of a cross-fade (none of them cast shadows)
        private sealed class Stage
        {
            public Matrix4x4[] m = new Matrix4x4[Batch]; public Vector4[] t = new Vector4[Batch]; public float[] f = new float[Batch]; public int n; public bool tinted;
            // its own property block, whose tint array is re-sent only when the stage's contents change (sig: the chunk runs
            // appended this frame); fading stages send their fade array every frame
            public MaterialPropertyBlock mpb; public long sig, sentSig = -1; public int sentN = -1;
        }
        private readonly Dictionary<(int, int, int), List<Stage>> _stages = new Dictionary<(int, int, int), List<Stage>>();
        private readonly Dictionary<Material, Material> _fadeMats = new Dictionary<Material, Material>();
        private readonly Plane[] _planes = new Plane[6];   // the camera frustum (exact: casters are chosen separately)
        private readonly float[] _pl = new float[24];

        /// <summary>GeometryUtility.TestPlanesAABB without the native call (thousands per frame): false only when the box
        /// lies wholly outside one plane.</summary>
        private bool InFrustum(in Bounds b)
        {
            var c = b.center; var e = b.extents; var pl = _pl;
            for (int i = 0; i < 24; i += 4)
            {
                float nx = pl[i], ny = pl[i + 1], nz = pl[i + 2];
                float r = e.x * (nx < 0f ? -nx : nx) + e.y * (ny < 0f ? -ny : ny) + e.z * (nz < 0f ? -nz : nz);
                if (nx * c.x + ny * c.y + nz * c.z + pl[i + 3] + r < 0f) return false;
            }
            return true;
        }
        [Tooltip("Same-frame CPU occlusion: the v3 buildings' backing boxes hide what is certainly behind them (main pass only; shadows unaffected).")]
        public bool occlusion = true;
        [Tooltip("Buildings nearer than this (m) act as occluders.")]
        public float occluderDistance = 240f;
        private readonly IslandOcclusion _occ = new IslandOcclusion();
        private readonly Dictionary<IslandKit.Proto, (bool has, Vector3 min, Vector3 max)> _occBoxes = new Dictionary<IslandKit.Proto, (bool, Vector3, Vector3)>();
        /// <summary>Instances the occlusion test removed last frame, and the occluders drawn (perf harness).</summary>
        public static int LastOccluded { get; private set; }
        public static int LastOccluders { get; private set; }
        private static readonly int LodFadeId = Shader.PropertyToID("_LodFade");

        public static void Register(IslandInstances s) { if (!Sets.Contains(s)) Sets.Add(s); }
        public static void Unregister(IslandInstances s) => Sets.Remove(s);

        /// <summary>Set while the camera is inside a closed walk-in room (IslandInteriors): nothing outside can be seen, so the
        /// kit draws nothing (main pass and shadow casters).</summary>
        public static bool HideWorld;

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || Sets.Count == 0 || HideWorld) { LastDraws = 0; LastInstances = 0; return; }
            _watch.Restart();
            Render(cam);
            LastCpuMs = _watch.Elapsed.TotalMilliseconds;
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
            for (int i = 0; i < 6; i++)
            {
                var n = _planes[i].normal;
                _pl[i * 4] = n.x; _pl[i * 4 + 1] = n.y; _pl[i * 4 + 2] = n.z; _pl[i * 4 + 3] = _planes[i].distance;
            }
            var eye = cam.transform.position;
            float shadowDist = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset != null
                ? UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset.shadowDistance : QualitySettings.shadowDistance;
            float shadowReach = shadowDist + shadowCasterMargin;
            var sun = RenderSettings.sun;
            bool castAny = shadows && shadowDist > 0f && sun != null && sun.isActiveAndEnabled && sun.shadows != LightShadows.None;
            var light = castAny ? sun.transform.forward : Vector3.down;
            float fall = Mathf.Max(0.1f, -light.y);                    // shadow length per metre of height
            foreach (var list in _stages.Values) foreach (var st in list) { st.n = 0; st.tinted = false; st.sig = 17; }
            foreach (var st in _shadowStages.Values) st.n = 0;
            LastCasters = 0;
            if (CollectStats) { Stats.Clear(); LastTris = LastCasterTris = 0; }
            bool occ = occlusion && (!CTLife.Core.QualityTier.IsLow || CTLife.Core.QualityTier.LowKitOcclusion);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (occ) BuildOccluders(cam, eye);
            LastOccMs = sw.Elapsed.TotalMilliseconds;
            int occluded = 0;
            IslandKit kit = null;
            // the graphics tier scales every kit LOD and cull distance (Low: entry phones)
            float distanceScale = this.distanceScale * CTLife.Core.QualityTier.KitDistance;
            foreach (var set in Sets)
            {
                if (set == null || set.kit == null) continue;
                kit = set.kit;
                // whole cell out of reach (or, casting no shadows, out of view): skip its chunks
                if (set.MaxCull > 0f && (set.AllBounds.SqrDistance(eye) > set.MaxCull * distanceScale * set.MaxCull * distanceScale
                    || (!castAny && !InFrustum(set.AllBounds)))) continue;
                foreach (var g in set.Groups)
                {
                    var p = kit.protos[g.Key];
                    if (SkipKinds.Count > 0 && SkipKinds.Contains(p.kind)) continue;
                    int lodCount = Mathf.Min(p.lods.Length, p.lodEnd.Length);
                    if (lodCount == 0) continue;
                    float cull = p.lodEnd[lodCount - 1] * distanceScale;
                    bool protoCasts = castAny && p.shadowRange > 0f;
                    foreach (var ch in g.Value)
                    {
                        float d = Mathf.Sqrt(ch.bounds.SqrDistance(eye));
                        if (d > cull) continue;
                        if (protoCasts && d <= shadowReach) AppendCasters(g.Key, p, lodCount, ch, eye, shadowDist, light, fall);
                        if (!InFrustum(ch.bounds)) continue;
                        int cls = occ ? _occ.Classify(ch.bounds) : 2;
                        if (cls == 0) { occluded += ch.matrices.Length; continue; }
                        // per instance only where the chunk is partly behind something
                        bool perInstance = cls == 1 && ch.boxes != null && p.kind != "grass" && p.kind != "crop";
                        int lod = 0;
                        while (lod < lodCount - 1 && d > p.lodEnd[lod] * distanceScale) lod++;
                        float end = p.lodEnd[lod] * distanceScale;
                        float band = Mathf.Clamp(end * fadeBand, 2f, 25f);
                        bool fading = d > end - band;
                        float t = fading ? Mathf.Clamp01((d - (end - band)) / band) : 0f;
                        // runs of instances that pass the occlusion test (all of them without it)
                        int n = ch.matrices.Length;
                        for (int i = 0; i < n;)
                        {
                            int start = i;
                            if (perInstance)
                            {
                                while (i < n && _occ.Visible(ch.boxes[i])) i++;
                            }
                            else i = n;
                            if (i > start)
                            {
                                if (fading)
                                {
                                    AppendRange(g.Key, lod, 2, ch, start, i - start, -t);       // outgoing (or fading out at the cull distance)
                                    if (lod < lodCount - 1) AppendRange(g.Key, lod + 1, 3, ch, start, i - start, t);
                                }
                                else AppendRange(g.Key, lod, 0, ch, start, i - start, 0f);
                            }
                            if (i < n) { i++; occluded++; }                                    // the one that failed the test
                        }
                    }
                }
            }
            LastLoopMs = sw.Elapsed.TotalMilliseconds - LastOccMs;
            if (kit == null) return;
            int draws = 0, inst = 0, block = 0;
            var bounds = new Bounds(eye, Vector3.one * 8000f);
            foreach (var kv in _stages)
            {
                var (protoI, lodI, mode) = kv.Key;
                var p = kit.protos[protoI];
                var mesh = p.lods[lodI];
                if (mesh == null || p.material == null) continue;
                bool fade = mode >= 2;
                int subs = mesh.subMeshCount;
                foreach (var st in kv.Value)
                {
                    if (st.n == 0) continue;
                    MaterialPropertyBlock mpb = null;
                    if (st.tinted || fade)
                    {
                        mpb = st.mpb ??= new MaterialPropertyBlock();
                        if (st.tinted) { if (st.sig != st.sentSig || st.n != st.sentN) { mpb.SetVectorArray(TintId, st.t); st.sentSig = st.sig; st.sentN = st.n; } }
                        else if (st.sentN > 0) { mpb.Clear(); st.sentSig = -1; st.sentN = -1; }   // no stale tints on an untinted stage
                        if (fade) mpb.SetFloatArray(LodFadeId, st.f);
                        block++;
                    }
                    for (int sm = 0; sm < subs; sm++)
                    {
                        var mat = LodMaterial(p, lodI, sm);
                        if (fade) mat = FadeMat(mat);
                        var rp = new RenderParams(mat)
                        {
                            layer = p.layer, receiveShadows = true, shadowCastingMode = ShadowCastingMode.Off,
                            worldBounds = bounds, camera = TargetCamera,
                        };
                        if (mpb != null) rp.matProps = mpb;
                        Graphics.RenderMeshInstanced(rp, mesh, sm, st.m, st.n);
                        draws++;
                        if (CollectStats) Count(p.kind, 0, 1, (long)mesh.GetIndexCount(sm) / 3 * st.n);
                    }
                    inst += st.n;
                }
            }
            // shadow casters: only instances whose shadow can land in the shadowed part of the view (see AppendCasters)
            foreach (var kv in _shadowStages)
            {
                var st = kv.Value;
                if (st.n == 0) continue;
                var (protoI, lodI) = kv.Key;
                var p = kit.protos[protoI];
                var mesh = ShadowMesh(p, lodI);
                if (mesh == null || p.material == null) continue;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    var rp = new RenderParams(LodMaterial(p, lodI, sm))
                    {
                        layer = p.layer, receiveShadows = false, shadowCastingMode = ShadowCastingMode.ShadowsOnly,
                        worldBounds = bounds, camera = TargetCamera,
                    };
                    for (int o = 0; o < st.n; o += Batch)
                    {
                        Graphics.RenderMeshInstanced(rp, mesh, sm, st.m, Mathf.Min(Batch, st.n - o), o);
                        draws++;
                        if (CollectStats) Count(p.kind, 2, 1, (long)mesh.GetIndexCount(sm) / 3 * Mathf.Min(Batch, st.n - o));
                    }
                }
                LastCasters += st.n;
            }
            LastSubmitMs = sw.Elapsed.TotalMilliseconds - LastOccMs - LastLoopMs;
            LastDraws = draws; LastInstances = inst; LastOccluded = occluded; LastOccluders = occ ? _occ.Occluders : 0;
        }

        /// <summary>The v3 building's backing box (LOD2's box shrunk inside the shell, as world_v3_lib.building_lods bakes it
        /// into LOD0 and LOD1), less a further 0.25 m; only prototypes whose last material is the backing box's _lod2 slot.</summary>
        private (bool has, Vector3 min, Vector3 max) OccluderBox(IslandKit.Proto p)
        {
            if (_occBoxes.TryGetValue(p, out var e)) return e;
            bool has = p.kind == "building" && p.lods.Length >= 3 && p.lods[2] != null && p.materials.Length > 1
                && p.materials[p.materials.Length - 1] != null && p.materials[p.materials.Length - 1].name.EndsWith("_lod2", System.StringComparison.Ordinal);
            Vector3 mn = default, mx = default;
            if (has)
            {
                var b = p.lods[2].bounds;
                var size = b.size;
                float hx = Mathf.Max(0.2f, size.x * 0.5f - Mathf.Min(1.2f, 0.2f * size.x)) - 0.25f;
                float hz = Mathf.Max(0.2f, size.z * 0.5f - Mathf.Min(1.2f, 0.2f * size.z)) - 0.25f;
                float top = b.max.y - Mathf.Min(0.5f, 0.1f * size.y) - 0.25f;
                has = hx > 0.5f && hz > 0.5f && top - b.min.y > 2f;
                mn = new Vector3(b.center.x - hx, b.min.y + 0.25f, b.center.z - hz);
                mx = new Vector3(b.center.x + hx, top, b.center.z + hz);
            }
            return _occBoxes[p] = (has, mn, mx);
        }

        private void BuildOccluders(Camera cam, Vector3 eye)
        {
            _occ.Begin(cam);
            float reach2 = occluderDistance * occluderDistance;
            foreach (var set in Sets)
            {
                if (set == null || set.kit == null) continue;
                if (set.MaxCull > 0f && (set.AllBounds.SqrDistance(eye) > reach2 || !InFrustum(set.AllBounds))) continue;
                foreach (var g in set.Groups)
                {
                    var p = set.kit.protos[g.Key];
                    if (p.kind != "building") continue;
                    var (has, mn, mx) = OccluderBox(p);
                    if (!has) continue;
                    float extent = (mx - mn).magnitude;
                    foreach (var ch in g.Value)
                    {
                        if (ch.bounds.SqrDistance(eye) > reach2 || !InFrustum(ch.bounds)) continue;
                        for (int i = 0; i < ch.matrices.Length; i++)
                        {
                            var m = ch.matrices[i];
                            if (m.m33 == 0f) continue;                                         // hidden
                            if (ch.boxes != null && !InFrustum(ch.boxes[i])) continue;
                            float d = Vector3.Distance(eye, new Vector3(m.m03, m.m13, m.m23));
                            if (d > occluderDistance || extent * m.m11 < d * 0.04f) continue;  // too far or too small to hide much
                            _occ.AddBox(m, mn, mx);
                        }
                    }
                }
            }
        }

        /// <summary>Perf captures: per kind, main draws / main triangles / caster draws / caster triangles of the last Render.</summary>
        public static bool CollectStats;
        public static readonly Dictionary<string, long[]> Stats = new Dictionary<string, long[]>();
        public static long LastTris, LastCasterTris;
        private void Count(string kind, int slot, int draws, long tris)
        {
            if (!Stats.TryGetValue(kind ?? "?", out var a)) Stats[kind ?? "?"] = a = new long[4];
            a[slot] += draws; a[slot + 1] += tris;
            if (slot == 0) LastTris += tris; else LastCasterTris += tris;
        }

        /// <summary>Casters per (prototype, LOD); grown as needed, never shrunk.</summary>
        private sealed class CasterStage { public Matrix4x4[] m = new Matrix4x4[256]; public int n; }
        private readonly Dictionary<(int, int), CasterStage> _shadowStages = new Dictionary<(int, int), CasterStage>();
        /// <summary>Shadow-casting instances submitted last frame (perf harness).</summary>
        public static int LastCasters { get; private set; }
        [Tooltip("Casters smaller than this radius (m) stop casting beyond smallCasterDistance (benches, bins, bollards).")]
        public float smallCasterRadius = 1.2f;
        public float smallCasterDistance = 32f;

        private static Material LodMaterial(IslandKit.Proto p, int lod, int sm) =>
            lod < p.lodMaterials.Length && p.lodMaterials[lod] != null ? p.lodMaterials[lod]
            : sm < p.materials.Length && p.materials[sm] != null ? p.materials[sm] : p.material;

        /// <summary>Buildings cast from LOD1 (about a quarter of LOD0's triangles; the shadow map cannot tell).</summary>
        private static Mesh ShadowMesh(IslandKit.Proto p, int lod)
        {
            var mesh = p.lods[lod];
            if (lod == 0 && p.kind == "building" && p.lods.Length > 1 && p.lods[1] != null && mesh != null && p.lods[1].subMeshCount == mesh.subMeshCount)
                return p.lods[1];
            return mesh;
        }

        /// <summary>
        /// Adds a chunk's instances that can cast a visible shadow. An instance's shadow lies inside its bounds swept along the
        /// sun direction down to 5 m below its base; it can only show if that swept volume reaches the shadowed region (the view
        /// frustum within the shadow distance of the eye). Everything else is skipped: casters behind the camera whose shadows
        /// fall away from the view, towers far down the street, and, in flight, almost every caster (the ground is beyond the
        /// shadow distance). The same LOD rule as before picks which ones may cast (LODs that start inside shadowRange).
        /// </summary>
        private void AppendCasters(int protoI, IslandKit.Proto p, int lodCount, IslandInstances.Chunk ch, Vector3 eye, float shadowDist, Vector3 light, float fall)
        {
            // whole chunk first: its bounds swept by the tallest shadow
            var cb = ch.bounds;
            float chunkLen = (cb.max.y - cb.min.y + 5f) / fall;
            var swept = cb; swept.Encapsulate(cb.min + light * chunkLen); swept.Encapsulate(cb.max + light * chunkLen);
            if (swept.SqrDistance(eye) > shadowDist * shadowDist || !InFrustum(swept)) return;
            var lb = p.bounds;
            float baseR = new Vector2(lb.center.x, lb.center.z).magnitude;
            for (int i = 0; i < ch.matrices.Length; i++)
            {
                var m = ch.matrices[i];
                if (m.m33 == 0f) continue;                                                     // hidden (SetHidden)
                float sxz = Mathf.Sqrt(m.m00 * m.m00 + m.m20 * m.m20), sy = m.m11;
                var c = new Vector3(m.m03, m.m13 + lb.center.y * sy, m.m23);
                float r = new Vector3(lb.extents.x * sxz, lb.extents.y * sy, lb.extents.z * sxz).magnitude + baseR * sxz;
                float di = Mathf.Max(0f, Vector3.Distance(eye, c) - r);
                int lod = 0;
                while (lod < lodCount - 1 && di > p.lodEnd[lod] * distanceScale) lod++;
                if (lod > 0 && p.lodEnd[lod - 1] >= p.shadowRange) continue;                  // this LOD never cast
                if (r < smallCasterRadius && di > smallCasterDistance) continue;
                float top = c.y + lb.extents.y * sy, len = (top - (m.m13 - 5f)) / fall;
                var e = c + light * len;
                // distance from the eye to the swept segment, less the instance radius, against the shadow distance
                var seg = e - c; float t = Mathf.Clamp01(Vector3.Dot(eye - c, seg) / Mathf.Max(1e-4f, seg.sqrMagnitude));
                if ((c + seg * t - eye).magnitude - r > shadowDist) continue;
                var sb = new Bounds(c, Vector3.one * (2f * r)); sb.Encapsulate(new Bounds(e, Vector3.one * (2f * r)));
                if (!InFrustum(sb)) continue;
                var key = (protoI, lod);
                if (!_shadowStages.TryGetValue(key, out var st)) _shadowStages[key] = st = new CasterStage();
                if (st.n == st.m.Length) System.Array.Resize(ref st.m, st.m.Length * 2);
                st.m[st.n++] = m;
            }
        }

        private void AppendRange(int proto, int lod, int mode, IslandInstances.Chunk ch, int start, int count, float fade)
        {
            var key = (proto, lod, mode);
            if (!_stages.TryGetValue(key, out var list)) _stages[key] = list = new List<Stage> { new Stage() };
            int src = start, total = start + count;
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
                st.sig = st.sig * 1000003L + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ch) * 7919L + src * 131L + k;
                System.Array.Copy(ch.matrices, src, st.m, st.n, k);
                System.Array.Copy(ch.tints, src, st.t, st.n, k);
                if (mode >= 2) for (int i = 0; i < k; i++) st.f[st.n + i] = fade;
                st.tinted |= ch.tinted;
                st.n += k; src += k;
            }
        }
    }
}
