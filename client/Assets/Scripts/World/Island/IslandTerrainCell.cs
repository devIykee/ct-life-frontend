using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.World
{
    /// <summary>
    /// The ground of one streamed cell, generated from the island heightfield when the cell loads: 64 m chunks with three
    /// LODs (2, 4 and 8 m; skirts on the coarse ones), LOD0 doubling as the MeshCollider. Chunks are built a few per
    /// frame, and the collision meshes are baked on a worker thread (Physics.BakeMesh) before they are assigned, so a cell
    /// never stalls a frame. Shipping the heightfield (2 MB) instead of baked chunk meshes keeps the build and repo small.
    /// Perf lane 2 (2026-10-23): each cell also merges its chunks' LOD1 and LOD2 meshes into one mesh per level, drawn in
    /// place of the chunks while every chunk of the cell is (with a 15% margin) past that LOD's switch distance: the same
    /// triangles in one draw instead of one per chunk (terrain was ~105 draws at eye level downtown). Near ground (LOD0,
    /// the only level that casts shadows) is always the chunks' own.
    /// </summary>
    public sealed class IslandTerrainCell : MonoBehaviour
    {
        public const int ChunkCells = 32;
        public const int GroundLayer = 10;
        [Tooltip("Grid sample index (x, z) of each chunk's south-west corner.")]
        public Vector2Int[] chunks;
        public Material material;
        public float lod0 = 0.32f, lod1 = 0.11f;
        public bool colliders = true;

        public bool Built { get; private set; }
        private bool _building;
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private static int _budgetFrame = -1, _chunksThisFrame;

        private static bool TakeChunkBudget(int limit)
        {
            if (_budgetFrame != Time.frameCount) { _budgetFrame = Time.frameCount; _chunksThisFrame = 0; }
            if (_chunksThisFrame >= limit) return false;
            _chunksThisFrame++;
            return true;
        }

        /// <summary>Builds the chunks (a few per frame) and their colliders; returns when the ground is solid.</summary>
        public async Task Build(IslandHeightfield h, CancellationToken ct, int perFrame = 3)
        {
            if (Built || _building || h == null || chunks == null) return;
            _building = true;
            var bakes = new List<(Mesh mesh, MeshCollider col)>();
            foreach (var c in chunks)
            {
                // Three cell requests must not multiply the old three-chunks-per-frame main-thread cost.
                while (!TakeChunkBudget(Mathf.Max(1, perFrame)))
                {
                    await Awaitable.NextFrameAsync();
                    ct.ThrowIfCancellationRequested();
                    if (this == null) return;
                }
                ct.ThrowIfCancellationRequested();
                var col = BuildChunk(h, c);
                if (col.col != null) bakes.Add(col);
            }
            var ids = new EntityId[bakes.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = bakes[i].mesh.GetEntityId();
            await Task.Run(() => { foreach (var id in ids) Physics.BakeMesh(id, false); }, ct);
            if (this == null) return;
            ct.ThrowIfCancellationRequested();
            foreach (var b in bakes) if (b.col != null) b.col.sharedMesh = b.mesh;
            BuildMerged();
            Built = true;
            _building = false;
        }

        /// <summary>Editor and tests: everything at once on the calling thread.</summary>
        public void BuildNow(IslandHeightfield h)
        {
            if (Built || h == null || chunks == null) return;
            foreach (var c in chunks)
            {
                var (mesh, col) = BuildChunk(h, c);
                if (col != null) col.sharedMesh = mesh;
            }
            BuildMerged();
            Built = true;
        }

        private (Mesh mesh, MeshCollider col) BuildChunk(IslandHeightfield h, Vector2Int c)
        {
            var origin = new Vector3(h.X(c.x), 0f, h.Z(c.y));
            string name = $"ground_{c.x / ChunkCells}_{c.y / ChunkCells}";
            var go = new GameObject(name) { layer = GroundLayer };
            go.transform.SetParent(transform, false);
            go.transform.position = origin;
            var lods = new[]
            {
                h.Grid(c.x, c.y, ChunkCells, 1, false, name + "_lod0", origin),
                h.Grid(c.x, c.y, ChunkCells, 2, true, name + "_lod1", origin),
                h.Grid(c.x, c.y, ChunkCells, 4, true, name + "_lod2", origin),
            };
            var rs = new Renderer[3];
            MeshCollider mc = null;
            for (int i = 0; i < 3; i++)
            {
                _meshes.Add(lods[i]);
                var child = new GameObject("LOD" + i) { layer = GroundLayer };
                child.transform.SetParent(go.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = lods[i];
                var mr = child.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = i == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                rs[i] = mr;
                if (i == 0 && colliders) mc = child.AddComponent<MeshCollider>();
            }
            var lg = go.AddComponent<LODGroup>();
            lg.SetLODs(new[] { new LOD(lod0, new[] { rs[0] }), new LOD(lod1, new[] { rs[1] }), new LOD(0f, new[] { rs[2] }) });
            lg.fadeMode = LODFadeMode.None;
            lg.RecalculateBounds();
            _chunks.Add((lg, rs));
            return (lods[0], mc);
        }

        // ------------------------------------------------------------------ merged far levels

        private readonly List<(LODGroup lg, Renderer[] rs)> _chunks = new List<(LODGroup, Renderer[])>();
        private readonly Renderer[] _merged = new Renderer[3];   // [1], [2]
        private int _state = -1;                                  // 0 chunks, 1 merged LOD1, 2 merged LOD2
        private float _nextCheck;
        public const float MergeMargin = 1.15f;

        private void BuildMerged()
        {
            if (_chunks.Count < 2) return;
            for (int level = 1; level <= 2; level++)
            {
                var ci = new CombineInstance[_chunks.Count];
                int verts = 0;
                for (int i = 0; i < _chunks.Count; i++)
                {
                    var r = _chunks[i].rs[level];
                    var m = r.GetComponent<MeshFilter>().sharedMesh;
                    verts += m.vertexCount;
                    ci[i] = new CombineInstance { mesh = m, transform = transform.worldToLocalMatrix * r.transform.localToWorldMatrix };
                }
                var mesh = new Mesh { name = name + "_merged_lod" + level, indexFormat = verts > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.CombineMeshes(ci, true, true, false);
                mesh.UploadMeshData(true);
                _meshes.Add(mesh);
                var go = new GameObject("merged_LOD" + level) { layer = GroundLayer };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.enabled = false;
                _merged[level] = mr;
            }
            _state = 0;
        }

        /// <summary>The LOD a chunk's LODGroup is certainly at (or past) for this camera: Unity's screen-relative height
        /// (size / 2 / (distance * tan(fov / 2)) * lodBias) against the transitions, with <see cref="MergeMargin"/> to spare.</summary>
        private int SafeLevel(LODGroup lg, Vector3 eye, float tanHalf, float bias)
        {
            float d = Vector3.Distance(eye, lg.transform.TransformPoint(lg.localReferencePoint));
            float rel = lg.size * 0.5f / (Mathf.Max(0.01f, d / MergeMargin) * tanHalf) * bias;
            return rel < lod1 ? 2 : rel < lod0 ? 1 : 0;
        }

        // ------------------------------------------------------------------ graphics tier

        private static readonly Dictionary<Material, Material> LowMats = new Dictionary<Material, Material>();
        private bool _lowShown;
        private int _tierCount = -1;

        /// <summary>Low tier (CTLife.Core.QualityTier): the near ground draws with the shader's far variant (_FAR: no detail
        /// texture, no alpine / grass detail passes, flat rock and scree), the terrain being most of the screen on an entry
        /// GPU. The variant is in the build because the far terrain material uses it.</summary>
        private static Material LowMaterial(Material m)
        {
            if (m == null || m.IsKeywordEnabled("_FAR")) return m;
            if (!LowMats.TryGetValue(m, out var f) || f == null)
            {
                f = new Material(m) { name = m.name + " (low)", hideFlags = HideFlags.DontSave };
                f.EnableKeyword("_FAR");
                LowMats[m] = f;
            }
            return f;
        }

        private void ApplyTierMaterial()
        {
            bool low = CTLife.Core.QualityTier.IsLow;
            if (low == _lowShown && _tierCount == _chunks.Count && (_state < 0 || _merged[1] == null || _merged[1].sharedMaterial == (low ? LowMaterial(material) : material))) return;
            _lowShown = low; _tierCount = _chunks.Count;
            var mat = low ? LowMaterial(material) : material;
            foreach (var (_, rs) in _chunks) foreach (var r in rs) if (r != null) r.sharedMaterial = mat;
            for (int i = 1; i < _merged.Length; i++) if (_merged[i] != null) _merged[i].sharedMaterial = mat;
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.1f;
            if (_chunks.Count > 0) ApplyTierMaterial();
            if (_state < 0) return;
            var cam = Camera.main;
            int want = 0;
            if (cam != null && !cam.orthographic)
            {
                float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), bias = QualitySettings.lodBias;
                var eye = cam.transform.position;
                want = 2;
                foreach (var (lg, _) in _chunks) { if (lg == null) continue; want = Mathf.Min(want, SafeLevel(lg, eye, tanHalf, bias)); if (want == 0) break; }
            }
            if (want == _state) return;
            _state = want;
            foreach (var (_, rs) in _chunks) foreach (var r in rs) if (r != null) r.enabled = want == 0;
            if (_merged[1] != null) _merged[1].enabled = want == 1;
            if (_merged[2] != null) _merged[2].enabled = want == 2;
        }

        private void OnDestroy()
        {
            foreach (var m in _meshes) if (m != null) Destroy(m);
            _meshes.Clear();
        }
    }
}
