using System;
using System.IO;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>Loads the generator's heightfield (tools/world/islandgen/out/height.r16 + terrain.json + holes.png).</summary>
    public static class IslandV2Height
    {
        public static IslandHeightfield Load(string outDir)
        {
            var meta = JObject.Parse(File.ReadAllText(Path.Combine(outDir, "terrain.json")));
            var bytes = File.ReadAllBytes(IslandV2Paths.HeightFile(outDir));
            var h = IslandHeightfield.FromBytes(bytes, null, (int)meta["samples"], (float)meta["cell"], (float)meta["min"][0], (float)meta["min"][1],
                (float)meta["hmin"], (float)meta["hmax"], (float)meta["sea"], (float)meta["flightCeiling"]);
            var holesPath = Path.Combine(outDir, "holes.png");
            if (File.Exists(holesPath))
            {
                var t = new Texture2D(2, 2, TextureFormat.R8, false);
                t.LoadImage(File.ReadAllBytes(holesPath));
                if (t.width == h.N && t.height == h.N)
                {
                    var px = t.GetPixels32();
                    h.Hole = new bool[h.N * h.N];
                    for (int i = 0; i < px.Length; i++) h.Hole[i] = px[i].r > 127;   // texture row 0 = bottom = south
                }
                UnityEngine.Object.DestroyImmediate(t);
            }
            return h;
        }
    }

    public static class IslandV2Paths
    {
        public static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        public static string Out => Path.Combine(Repo, "tools", "world", "islandgen", "out");
        /// <summary>dress.py's copy of the heightfield (levelled under the walk-in rooms) when it wrote one, else islandgen's.</summary>
        public static string HeightFile(string outDir)
        {
            var dressed = Path.Combine(outDir, "height_dressed.r16");
            return File.Exists(dressed) ? dressed : Path.Combine(outDir, "height.r16");
        }
        public const string Root = "Assets/World/Island";
        public const string Textures = Root + "/Textures";
        public const string Shaders = Root + "/Shaders";
        public const string Materials = Root + "/Materials";
        public const string Meshes = Root + "/Meshes";
        public const string Roads = Root + "/Roads";
        public const string NavPath = Root + "/CTIsland_NavMesh.asset";
        public const string Data = Root + "/Data";
        public const string ResourcesDir = "Assets/Resources/World/Island";
        public const string BasePrefab = ResourcesDir + "/CTIsland.prefab";
        public const string CellsDir = ResourcesDir + "/Cells";
        public const float CellSize = 256f, ChunkSize = 64f;

        public static JObject Json(string name) => JObject.Parse(File.ReadAllText(Path.Combine(Out, name)));

        public static Vector3 V3(JToken t) => t is JArray a && a.Count >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : Vector3.zero;
        public static Vector2 V2(JToken t) => t is JArray a && a.Count >= 2 ? new Vector2((float)a[0], (float)a[1]) : Vector2.zero;

        /// <summary>Cell index (x, z) of an island point on the 256 m streaming grid over the generator domain.</summary>
        public static Vector2Int CellOf(float x, float z, IslandHeightfield h) =>
            new Vector2Int(Mathf.Clamp(Mathf.FloorToInt((x - h.MinX) / CellSize), 0, CellCount(h) - 1),
                           Mathf.Clamp(Mathf.FloorToInt((z - h.MinZ) / CellSize), 0, CellCount(h) - 1));

        public static int CellCount(IslandHeightfield h) => Mathf.RoundToInt(h.Size / CellSize);
        public static string CellId(Vector2Int c) => $"cell_{c.x}_{c.y}";
        public static Rect CellRect(Vector2Int c, IslandHeightfield h) => new Rect(h.MinX + c.x * CellSize, h.MinZ + c.y * CellSize, CellSize, CellSize);
    }
}
