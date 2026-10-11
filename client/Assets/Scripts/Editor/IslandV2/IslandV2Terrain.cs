using System.Collections.Generic;
using CTLife.World;
using UnityEngine;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// Which 64 m terrain chunks exist (land or shallow sea) and which streaming cell each belongs to. The chunk meshes
    /// themselves are generated from the heightfield when a cell loads (IslandTerrainCell), not stored as assets.
    /// </summary>
    public static class IslandV2Terrain
    {
        public const int ChunkCells = IslandTerrainCell.ChunkCells;

        public struct Chunk { public Vector2Int Cell; public Vector2Int Grid; public float MinY, MaxY; }

        public static List<Chunk> Chunks(IslandHeightfield h)
        {
            var list = new List<Chunk>();
            int quads = h.N - 1;
            for (int z0 = 0; z0 < quads; z0 += ChunkCells)
                for (int x0 = 0; x0 < quads; x0 += ChunkCells)
                {
                    h.Range(x0, z0, ChunkCells, out var minY, out var maxY);
                    if (maxY < h.Sea - 14f) continue;   // deep sea: the water covers it
                    list.Add(new Chunk { Grid = new Vector2Int(x0, z0), MinY = minY, MaxY = maxY,
                        Cell = IslandV2Paths.CellOf(h.X(x0) + 1f, h.Z(z0) + 1f, h) });
                }
            return list;
        }

        /// <summary>The far-terrain piece of a streaming cell (grid origin and size), or false when it is all deep sea.</summary>
        public static bool Far(IslandHeightfield h, Vector2Int cell, out int x0, out int z0, out int cells)
        {
            cells = Mathf.RoundToInt(IslandV2Paths.CellSize / h.Cell);
            x0 = cell.x * cells; z0 = cell.y * cells;
            h.Range(x0, z0, cells, out _, out var max);
            return max > h.Sea - 14f;
        }
    }
}
