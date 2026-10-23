using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>Always-loaded collision beneath streamed terrain. No renderer and no replacement for landing readiness.</summary>
    public sealed class IslandGroundFallback : MonoBehaviour
    {
        public const float Spacing = 8f, Clearance = 0.15f;
        private readonly List<Mesh> _meshes = new List<Mesh>();

        /// <summary>Only coarse floor colliders may descend from this owner; ordinary island floors are siblings.</summary>
        public static IslandGroundFallback Create(Transform island)
        {
            var owner = new GameObject("GroundFallback");
            owner.transform.SetParent(island, false);
            return owner.AddComponent<IslandGroundFallback>();
        }

        /// <summary>
        /// A conservative lower envelope. Plain 8m samples can cut above a valley/road; each vertex uses the minimum
        /// in its adjacent coarse quads so triangles never create an invisible hill above the detailed terrain.
        /// Authored tunnel holes stay open. Roads and tunnel floors retain their own collision.
        /// </summary>
        public static IslandHeightfield LowerEnvelope(IslandHeightfield h, int stride)
        {
            var lower = new IslandHeightfield { N = h.N, Cell = h.Cell, MinX = h.MinX, MinZ = h.MinZ, Size = h.Size,
                Sea = h.Sea, HMin = h.HMin, HMax = h.HMax, FlightCeiling = h.FlightCeiling, Hole = h.Hole, H = (float[])h.H.Clone() };
            for (int z = 0; z < h.N; z += stride) for (int x = 0; x < h.N; x += stride)
            {
                float y = h.At(x, z);
                for (int dz = -stride; dz <= stride; dz++) for (int dx = -stride; dx <= stride; dx++)
                    y = Mathf.Min(y, h.At(x + dx, z + dz));
                lower.H[z * h.N + x] = y - Clearance;
            }
            return lower;
        }

        public async Task Build(IslandHeightfield h, CancellationToken ct)
        {
            if (h == null || _meshes.Count > 0) return;
            int stride = Mathf.Max(1, Mathf.RoundToInt(Spacing / h.Cell));
            var lower = LowerEnvelope(h, stride);
            int tile = (h.N - 1) / 2;
            for (int z = 0; z < h.N - 1; z += tile) for (int x = 0; x < h.N - 1; x += tile)
            {
                ct.ThrowIfCancellationRequested();
                var mesh = lower.Grid(x, z, tile, stride, false, $"ground_fallback_{x}_{z}", Vector3.zero);
                _meshes.Add(mesh);
                var id = mesh.GetEntityId();
                await Task.Run(() => Physics.BakeMesh(id, false), ct);
                if (this == null || ct.IsCancellationRequested) return;
                var go = new GameObject(mesh.name) { layer = IslandTerrainCell.GroundLayer };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                await Awaitable.NextFrameAsync();
            }
        }

        private void OnDestroy()
        {
            foreach (var mesh in _meshes) if (mesh != null) Destroy(mesh);
            _meshes.Clear();
        }
    }
}
