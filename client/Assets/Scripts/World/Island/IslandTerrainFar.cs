using UnityEngine;

namespace CTLife.World
{
    /// <summary>The far ground of the base prefab: one 16 m mesh per streaming cell, filled in when the island loads.</summary>
    public sealed class IslandTerrainFar : MonoBehaviour
    {
        [System.Serializable] public struct Piece { public MeshFilter filter; public int x0, z0, cells; }
        public Piece[] pieces;

        public void Build(IslandHeightfield h)
        {
            if (pieces == null) return;
            foreach (var p in pieces)
            {
                if (p.filter == null || p.filter.sharedMesh != null) continue;
                var origin = new Vector3(h.X(p.x0), 0f, h.Z(p.z0));
                p.filter.transform.position = origin;
                p.filter.sharedMesh = h.Grid(p.x0, p.z0, p.cells, 8, true, p.filter.name, origin, -0.35f, false);
            }
        }
    }
}
