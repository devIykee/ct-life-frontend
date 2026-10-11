using System.Collections.Generic;
using CTLife.World;
using UnityEngine;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>Everything one streaming cell will hold, collected by the terrain/road/prop passes before prefabs are written.</summary>
    public sealed class CellGeo
    {
        public Vector2Int Cell;
        public readonly Dictionary<string, MeshBuilder> Visual = new Dictionary<string, MeshBuilder>();
        /// <summary>Collision meshes by layer index.</summary>
        public readonly Dictionary<int, MeshBuilder> Collision = new Dictionary<int, MeshBuilder>();
        public readonly List<BoxCol> Boxes = new List<BoxCol>();
        public readonly List<Marker> Markers = new List<Marker>();
        public readonly List<IslandV2Terrain.Chunk> Chunks = new List<IslandV2Terrain.Chunk>();
        /// <summary>Kit instances (dress.json) drawn by the cell's IslandInstances.</summary>
        public readonly List<IslandV2Dress.Inst> Instances = new List<IslandV2Dress.Inst>();
        /// <summary>Vertical capsules (tree trunks, lamp posts): all on one GameObject per layer.</summary>
        public readonly List<CapsuleCol> Capsules = new List<CapsuleCol>();
        /// <summary>Live billboards (dress.json) built as real renderers in the cell.</summary>
        public readonly List<Newtonsoft.Json.Linq.JObject> Billboards = new List<Newtonsoft.Json.Linq.JObject>();

        public struct CapsuleCol { public Vector3 Center; public float Radius, Height; public int Layer; }
        public struct BoxCol { public Vector3 Center; public Quaternion Rotation; public Vector3 Size; public int Layer; public string Name; }
        public struct Marker { public string Kind, Id, Segment; public Vector3 Position; public Quaternion Rotation; public Vector2 Size; public float Showcase; }

        public MeshBuilder V(string material, bool uv1 = false, bool uv2 = false, bool color = false)
        {
            if (!Visual.TryGetValue(material, out var b)) Visual[material] = b = new MeshBuilder();
            b.HasUV1 |= uv1; b.HasUV2 |= uv2; b.HasColor |= color;
            return b;
        }

        public MeshBuilder Col(int layer)
        {
            if (!Collision.TryGetValue(layer, out var b)) Collision[layer] = b = new MeshBuilder();
            return b;
        }
    }

    public sealed class CellSet
    {
        private readonly Dictionary<Vector2Int, CellGeo> _cells = new Dictionary<Vector2Int, CellGeo>();
        private readonly IslandHeightfield _h;
        public CellSet(IslandHeightfield h) { _h = h; }
        public IEnumerable<CellGeo> All => _cells.Values;
        public CellGeo At(Vector3 p) => Get(IslandV2Paths.CellOf(p.x, p.z, _h));
        public CellGeo Get(Vector2Int c)
        {
            if (!_cells.TryGetValue(c, out var g)) _cells[c] = g = new CellGeo { Cell = c };
            return g;
        }
    }

    /// <summary>Material keys shared by the passes and the builder's material table.</summary>
    public static class Mat
    {
        public const string Road = "road", Dirt = "road_dirt", Sidewalk = "sidewalk", Kerb = "kerb", Concrete = "concrete",
            Rail = "rail", Paint = "paint", Tunnel = "tunnel", Grass = "grass_island", Quay = "quay", Wall = "wall_stone", Plaza = "plaza";
    }

    public static class Layers
    {
        public const int Ground = 10, Road = 11, Building = 12, Prop = 13, Vegetation = 14, VehicleBlocker = 15, CoastWall = 16, Interior = 17, Water = 4;
        public static readonly (int index, string name)[] All =
        {
            (Ground, "Ground"), (Road, "Road"), (Building, "Building"), (Prop, "Prop"), (Vegetation, "Vegetation"),
            (VehicleBlocker, "VehicleBlocker"), (CoastWall, "CoastWall"), (Interior, "Interior"),
        };
    }
}
