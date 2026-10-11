using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// Island dressing (World lane): reads tools/world/islandgen/out/dress.json (dress.py), builds the kit for the
    /// models it uses, and hands every instance, building box and trunk capsule to its streaming cell. Cells then
    /// write a CTI1 instance file (<see cref="IslandInstances"/>), colliders and markers. Parked decor cars are chosen
    /// here too, in a share of the street bays, so those bays stop being vehicle spawns.
    /// </summary>
    public static class IslandV2Dress
    {
        public const string ShopSigns = "shop_signs";
        public static IslandKit Kit;
        public static int[] CarProtos = Array.Empty<int>();
        /// <summary>Share of street bays with a decor car, by district (downtown busier than the hills).</summary>
        public static float ParkedShare(Vector3 p)
        {
            float r = new Vector2(p.x, p.z).magnitude;
            return r < 260f ? 0.4f : 0.22f;
        }

        public sealed class Inst { public int Proto; public float X, Y, Z, Yaw, S, Sy; public uint Tint; }

        public static void Place(CellSet cells, IslandHeightfield h, JObject receipt)
        {
            var path = Path.Combine(IslandV2Paths.Out, "dress.json");
            if (!File.Exists(path)) { Debug.LogWarning("CTLIFE [islandv2] no dress.json: the island stays bare"); Kit = null; return; }
            var doc = JObject.Parse(File.ReadAllText(path));
            var groups = (JArray)doc["instances"];
            var used = new HashSet<string>(groups.Select(g => (string)g["proto"]));
            foreach (var c in CarModels) used.Add(c);
            Kit = IslandV2Kit.Build(used, receipt);
            CarProtos = CarModels.Select(c => Kit.IndexOf(c)).Where(i => i >= 0).ToArray();
            int n = 0, trunks = 0;
            foreach (var g in groups)
            {
                int proto = Kit.IndexOf((string)g["proto"]);
                if (proto < 0) continue;
                var p = Kit.protos[proto];
                var d = ((JArray)g["d"]).Select(v => (float)v).ToArray();
                for (int i = 0; i + 9 < d.Length + 0; i += 10)
                {
                    var inst = new Inst { Proto = proto, X = d[i], Y = d[i + 1], Z = d[i + 2], Yaw = d[i + 3], S = d[i + 4], Sy = d[i + 5], Tint = Pack(d[i + 6], d[i + 7], d[i + 8], d[i + 9]) };
                    var cell = cells.At(new Vector3(inst.X, 0f, inst.Z));
                    cell.Instances.Add(inst);
                    n++;
                    if (p.kind == "tree")
                    {
                        float r = Mathf.Clamp(p.bounds.size.x * inst.S * 0.06f, 0.18f, 0.55f);
                        float ht = Mathf.Min(p.bounds.size.y * inst.S * inst.Sy * 0.45f, 4.5f);
                        cell.Capsules.Add(new CellGeo.CapsuleCol { Center = new Vector3(inst.X, inst.Y + ht / 2f, inst.Z), Radius = r, Height = ht, Layer = Layers.Vegetation });
                        trunks++;
                    }
                    else if (p.kind == "lamp" || p.kind == "pole")
                    {
                        cell.Capsules.Add(new CellGeo.CapsuleCol { Center = new Vector3(inst.X, inst.Y + 2f, inst.Z), Radius = 0.16f, Height = 4f, Layer = Layers.Prop });
                    }
                }
            }
            int boxes = 0;
            foreach (var b in (JArray)doc["buildingBoxes"])
            {
                var a = ((JArray)b).Select(v => (float)v).ToArray();
                var c = new Vector3(a[0], a[1], a[2]);
                cells.At(c).Boxes.Add(new CellGeo.BoxCol
                {
                    Center = c, Rotation = Quaternion.Euler(0f, a[6], 0f), Size = new Vector3(a[3] * 0.96f, a[4], a[5] * 0.96f), Layer = Layers.Building, Name = "building",
                });
                boxes++;
            }
            int boards = 0;
            if (doc["billboards"] is JArray bbs)
                foreach (var b in bbs) { cells.At(IslandV2Paths.V3(b["p"])).Billboards.Add((JObject)b); boards++; }
            int heroes = 0;
            if (doc["heroes"] is JArray hs)
                foreach (JObject hj in hs)
                {
                    heroes++;
                    if (!(hj["plinth"] is JArray pl)) continue;
                    var p = IslandV2Paths.V3(hj["p"]);
                    float w = (float)pl[0], d = (float)pl[1], hh = (float)pl[2];
                    var rot = Quaternion.Euler(0f, (float)hj["yaw"], 0f);
                    var cell = cells.At(p);
                    var c = p + Vector3.up * (hh * 0.5f + 0.05f);
                    var stone = cell.V(Mat.Quay);
                    stone.Box(c, rot * Vector3.right, Vector3.up, rot * Vector3.forward, new Vector3(w * 0.5f, hh * 0.5f + 0.05f, d * 0.5f));
                    stone.Box(p + Vector3.up * (hh + 0.12f), rot * Vector3.right, Vector3.up, rot * Vector3.forward, new Vector3(w * 0.5f + 0.12f, 0.08f, d * 0.5f + 0.12f));
                    cell.Boxes.Add(new CellGeo.BoxCol { Center = p + Vector3.up * (hh * 0.5f + 1.2f), Rotation = rot, Size = new Vector3(w, hh + 2.4f, d), Layer = Layers.Prop, Name = "hero_" + (string)hj["id"] });
                }
            receipt["dressHeroes"] = heroes;
            // W3 street life: blade shop signs projecting from downtown ground floors (atlas Textures/Signs/shop_signs.png,
            // 4 x 8 cells), a two-sided panel with a slim bracket to the facade
            int signs = 0;
            if (doc["shopSigns"] is JArray ss)
                foreach (JArray sj in ss)
                {
                    var q = new Vector3((float)sj[0], (float)sj[1], (float)sj[2]);
                    var rot = Quaternion.Euler(0f, (float)sj[3], 0f);
                    int idx = (int)sj[4];
                    var cell = cells.At(q);
                    var b = cell.V(ShopSigns);
                    Vector3 R = rot * Vector3.right, O = rot * Vector3.forward, U = Vector3.up;
                    const float hw = 0.68f, hh = 0.34f;
                    float u0 = (idx % 4) / 4f, u1 = u0 + 0.25f, v1 = 1f - (idx / 4) / 8f, v0 = v1 - 0.125f;
                    void Face(Vector3 n, Vector3 right)
                    {
                        var c = q + n * 0.025f;
                        int bl = b.Add(c - right * hw - U * hh, n, new Vector2(u0, v0)), tl = b.Add(c - right * hw + U * hh, n, new Vector2(u0, v1));
                        int tr = b.Add(c + right * hw + U * hh, n, new Vector2(u1, v1)), br = b.Add(c + right * hw - U * hh, n, new Vector2(u1, v0));
                        b.Tri(bl, tl, tr); b.Tri(bl, tr, br);
                    }
                    Face(R, O);
                    Face(-R, -O);
                    var rail = cell.V(Mat.Rail);
                    rail.Box(q + O * ((hw - 0.62f) * 0.5f) + U * (hh + 0.06f), R, U, O, new Vector3(0.03f, 0.03f, (hw + 0.62f) * 0.5f));   // facade to the outer edge
                    signs++;
                }
            receipt["dressShopSigns"] = signs;
            receipt["dressInstances"] = n; receipt["dressBuildings"] = boxes; receipt["dressTrunks"] = trunks; receipt["dressBillboards"] = boards;
            receipt["dressStats"] = doc["stats"];
        }

        private static readonly Dictionary<string, Material> ScreenMats = new Dictionary<string, Material>();

        /// <summary>
        /// A live billboard (dress.json billboards): the venue kit's billboard frame as a real renderer whose panel
        /// submesh gets a Screen_bb_island_* material (an idle house-ad texture), an IslandScreen for LiveSurfaces, and
        /// a box collider for the posts or the roof stand.
        /// </summary>
        public static GameObject Billboard(JObject b, Transform parent, int layer)
        {
            var model = (string)b["model"];
            var fbx = "Assets/Art/props/" + model + ".fbx";
            var meshes = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().ToArray();
            var mesh = meshes.FirstOrDefault(m => m.name.EndsWith("_LOD0")) ?? meshes.FirstOrDefault();
            if (mesh == null) { Debug.LogWarning("CTLIFE [islandv2] billboard model missing " + fbx); return null; }
            var go = new GameObject("billboard_" + (string)b["screen"]) { layer = layer };
            go.transform.SetParent(parent, false);
            float sc = (float)b["scale"];
            // the kit billboard's panel faces its local -Z: turn it so the screen looks along the yaw dress.py gives
            go.transform.SetPositionAndRotation(IslandV2Paths.V3(b["p"]), Quaternion.Euler(0f, (float)b["yaw"] + 180f, 0f));
            go.transform.localScale = Vector3.one * sc;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var frame = IslandV2Kit.KitMaterial("M_ctl_palette", "large_prop", model);
            var idle = (string)b["idle"] ?? "bb_01";
            if (!ScreenMats.TryGetValue(idle, out var screen) || screen == null)
            {
                var path = IslandV2Kit.KitDir + "/Materials/Screen_bb_island_" + idle + ".mat";
                screen = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (screen == null) { screen = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Screen_bb_island_" + idle }; AssetDatabase.CreateAsset(screen, path); }
                var tex = AssetDatabase.FindAssets("Screen_" + idle + "_idle t:Texture2D").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Texture2D>).FirstOrDefault(t => t != null);
                if (tex != null) screen.SetTexture("_BaseMap", tex);
                screen.enableInstancing = true;
                EditorUtility.SetDirty(screen);
                ScreenMats[idle] = screen;
            }
            mr.sharedMaterials = mesh.subMeshCount > 1 ? new[] { frame, screen } : new[] { screen };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            go.AddComponent<IslandScreen>().screen = mr;
            var bc = go.AddComponent<BoxCollider>();
            var bb = mesh.bounds;
            bc.center = new Vector3(bb.center.x, bb.size.y * 0.25f, bb.center.z);
            bc.size = new Vector3(bb.size.x * 0.9f, bb.size.y * 0.5f, Mathf.Max(bb.size.z, 0.6f));
            return go;
        }

        /// <summary>Kenney car kit models used as parked decor (Feel's drivable cars are separate).</summary>
        public static readonly string[] CarModels = { "car_sedan", "car_sedan_sports", "car_hatchback_sports", "car_suv", "car_suv_luxury", "car_taxi", "car_van", "car_delivery", "car_police" };

        public static uint Pack(float r, float g, float b, float a) =>
            (uint)Mathf.RoundToInt(Mathf.Clamp01(r) * 255f) | ((uint)Mathf.RoundToInt(Mathf.Clamp01(g) * 255f) << 8) |
            ((uint)Mathf.RoundToInt(Mathf.Clamp01(b) * 255f) << 16) | ((uint)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f) << 24);

        /// <summary>The cell's CTI1 file (see <see cref="IslandInstances"/>).</summary>
        public static byte[] Encode(List<Inst> list)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            var groups = list.GroupBy(i => i.Proto).OrderBy(g => g.Key).ToList();
            w.Write(IslandInstances.Magic);
            w.Write(groups.Count);
            foreach (var g in groups)
            {
                w.Write(g.Key);
                w.Write(g.Count());
                foreach (var i in g) { w.Write(i.X); w.Write(i.Y); w.Write(i.Z); w.Write(i.Yaw); w.Write(i.S); w.Write(i.Sy); w.Write(i.Tint); }
            }
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>Stable 0..1 hash of a bay id (the same bays get cars on every rebuild).</summary>
        public static float Hash01(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) { h ^= c; h *= 16777619; }
                return (h & 0xffffff) / (float)0x1000000;
            }
        }
    }
}
