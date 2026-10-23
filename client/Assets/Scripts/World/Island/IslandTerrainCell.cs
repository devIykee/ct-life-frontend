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
            return (lods[0], mc);
        }

        private void OnDestroy()
        {
            foreach (var m in _meshes) if (m != null) Destroy(m);
            _meshes.Clear();
        }
    }
}
