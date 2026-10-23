using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using CTLife.World.Vehicles;
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
        public const string WallDecor = "wall_decor", BusAds = "bus_ads", Glass = "glass", TreePit = "tree_pit", EventBanners = "event_banners";
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
            IslandV2PlacementRepair.ApplyDressOverrides(doc);
            var groups = (JArray)doc["instances"];
            receipt["dressDecorCarsRestyled"] = V3DecorCars(groups);
            var used = new HashSet<string>(groups.Select(g => (string)g["proto"]).Where(p => !p.StartsWith("v3car_", StringComparison.Ordinal)));
            Kit = IslandV2Kit.Build(used, receipt);
            CarProtos = IslandV2Kit.V3CarSpecs.Select(c => Kit.IndexOf(IslandV2Kit.V3CarId(c))).Where(i => i >= 0).ToArray();
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
            BuildW4(doc, cells, h, receipt);
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

        /// <summary>Kenney car kit models that dress.py still places (shot dressing, yards); restyled to the v3 decor cars.</summary>
        public static readonly string[] CarModels = { "car_sedan", "car_sedan_sports", "car_hatchback_sports", "car_suv", "car_suv_luxury", "car_taxi", "car_van", "car_delivery", "car_police" };

        /// <summary>The v3 drivable a Kenney car model becomes (vans and delivery vans the v3 van, trucks the pickup).</summary>
        public static string V3SpecFor(string kenney) =>
            kenney.Contains("police") ? "police" : kenney.Contains("taxi") ? "taxi" : kenney.Contains("sports") ? "sports" : kenney.Contains("hatchback") ? "compact"
            : kenney.Contains("van") || kenney.Contains("delivery") ? "van" : kenney.Contains("truck") ? "pickup" : "sedan";

        /// <summary>World v3: every Kenney car dress.json places becomes the v3 decor car of its class, painted from the
        /// traffic palette by a hash of its position, so the street never shows the old low-poly cars.</summary>
        private static int V3DecorCars(JArray groups)
        {
            int n = 0;
            var merged = new Dictionary<string, JArray>();
            foreach (JObject g in groups.ToList())
            {
                var proto = (string)g["proto"];
                if (!proto.StartsWith("car_", StringComparison.Ordinal)) continue;
                string spec = V3SpecFor(proto), id = IslandV2Kit.V3CarId(spec);
                var d = (JArray)g["d"];
                for (int i = 0; i + 9 < d.Count; i += 10)
                {
                    var col = VehiclePaint.Pick(Hash01($"{(float)d[i]:F1},{(float)d[i + 2]:F1}"));
                    bool paint = spec != "police" && spec != "taxi";
                    d[i + 6] = paint ? col.r : 0f; d[i + 7] = paint ? col.g : 0f; d[i + 8] = paint ? col.b : 0f; d[i + 9] = paint ? 1f : 0f;
                    n++;
                }
                groups.Remove(g);
                if (!merged.TryGetValue(id, out var all)) merged[id] = all = new JArray();
                foreach (var v in d) all.Add(v);
            }
            foreach (var kv in merged) groups.Add(new JObject { ["proto"] = kv.Key, ["n"] = kv.Value.Count / 10, ["d"] = kv.Value });
            return n;
        }

        /// <summary>
        /// A parked decor car for a street bay, chosen exactly as <see cref="ParkedCars"/> will choose the drivable that
        /// replaces it (same position hash: the police one bay in ten, the model by class, the traffic paint), so the swap
        /// on approach changes nothing the player can see. Returns the kit proto, the bay class and the packed tint.
        /// </summary>
        public static (int proto, VehicleClass cls, uint tint) DecorCarFor(string bayId, Vector3 p)
        {
            int pick = unchecked(Mathf.RoundToInt(p.x * 10) * 73856093 ^ Mathf.RoundToInt(p.z * 10) * 19349663);
            var cls = Hash01(bayId + "car") < 0.22f ? VehicleClass.Sports : VehicleClass.Compact;
            string spec = ParkedCars.SpecFor(cls, null, pick);
            if (spec == "sedan" && Hash01(bayId + "taxi") < 0.2f) spec = "taxi";
            int proto = Kit != null ? Kit.IndexOf(IslandV2Kit.V3CarId(spec)) : -1;
            if (proto < 0 && CarProtos.Length > 0) proto = CarProtos[(int)(Hash01(bayId + "car") * CarProtos.Length) % CarProtos.Length];
            uint tint = 0;
            if (spec != "police" && spec != "taxi")
            {
                var c = VehiclePaint.Pick(((pick >> 8) & 0xFFFF) / 65535.0);
                tint = Pack(c.r, c.g, c.b, 1f);
            }
            return (proto, cls, tint);
        }

        // ------------------------------------------------------------------ W4 eye-level dressing

        /// <summary>A two-sided textured panel: centre c, facing n, 'right' along its width, uv rect (u0, v0)-(u1, v1).</summary>
        private static void Panel(MeshBuilder b, Vector3 c, Vector3 n, Vector3 right, float hw, float hh, Vector4 uv, bool twoSided = true, float lift = 0.02f)
        {
            void Face(Vector3 nn, Vector3 rr)
            {
                var q = c + nn * lift;
                int bl = b.Add(q - rr * hw - Vector3.up * hh, nn, new Vector2(uv.x, uv.y)), tl = b.Add(q - rr * hw + Vector3.up * hh, nn, new Vector2(uv.x, uv.w));
                int tr = b.Add(q + rr * hw + Vector3.up * hh, nn, new Vector2(uv.z, uv.w)), br = b.Add(q + rr * hw - Vector3.up * hh, nn, new Vector2(uv.z, uv.y));
                b.Tri(bl, tl, tr); b.Tri(bl, tr, br);
            }
            Face(n, right);
            if (twoSided) Face(-n, -right);
        }

        /// <summary>Atlas cell in pixels (top-left origin) to a uv rect (u0, v0, u1, v1).</summary>
        private static Vector4 Cell(float px, float py, float w, float hgt, float size) =>
            new Vector4(px / size, 1f - (py + hgt) / size, (px + w) / size, 1f - py / size);

        private static void BuildW4(JObject doc, CellSet cells, IslandHeightfield h, JObject receipt)
        {
            // street tree pits: a sunk square of soil with a steel edge round the trunk
            int pits = 0;
            if (doc["treePits"] is JArray tps)
                foreach (JArray t in tps)
                {
                    var c = new Vector3((float)t[0], (float)t[1], (float)t[2]);
                    var rot = Quaternion.Euler(0f, (float)t[3], 0f);
                    var cell = cells.At(c);
                    Vector3 R = rot * Vector3.right, F = rot * Vector3.forward;
                    const float hs = 0.75f;
                    cell.V(TreePit).FlatQuad(c - R * hs - F * hs + Vector3.up * 0.16f, c - R * hs + F * hs + Vector3.up * 0.16f,
                                             c + R * hs + F * hs + Vector3.up * 0.16f, c + R * hs - F * hs + Vector3.up * 0.16f);
                    var rail = cell.V(Mat.Rail);
                    for (int k = 0; k < 4; k++)
                    {
                        var q = Quaternion.AngleAxis(90f * k, Vector3.up);
                        rail.Box(c + q * (F * hs) + Vector3.up * 0.17f, q * R, Vector3.up, q * F, new Vector3(hs + 0.05f, 0.025f, 0.05f));
                    }
                    pits++;
                }
            // bus shelters: back glass, a lit two-sided ad panel on the downstream end, roof, posts and a bench
            int shelters = 0;
            if (doc["busShelters"] is JArray bss)
                foreach (JArray t in bss)
                {
                    var c = new Vector3((float)t[0], (float)t[1], (float)t[2]);
                    var rot = Quaternion.Euler(0f, (float)t[3], 0f);
                    int ad = (int)t[4];
                    var cell = cells.At(c);
                    Vector3 R = rot * Vector3.right, F = rot * Vector3.forward, U = Vector3.up;   // F: towards the road
                    var rail = cell.V(Mat.Rail);
                    var glass = cell.V(Glass);
                    const float hw = 2.2f, d = 0.75f, top = 2.6f;
                    foreach (var sx in new[] { -hw, hw })
                        foreach (var sz in new[] { -d, d })
                            rail.Box(c + R * sx + F * sz + U * (top / 2f), R, U, F, new Vector3(0.05f, top / 2f, 0.05f));
                    rail.Box(c + U * (top + 0.06f), R, U, F, new Vector3(hw + 0.15f, 0.06f, d + 0.25f));                    // roof
                    glass.Box(c - F * d + U * 1.35f, R, U, F, new Vector3(hw - 0.05f, 1.05f, 0.02f));                         // back pane
                    glass.Box(c - R * hw + U * 1.35f, R, U, F, new Vector3(0.02f, 1.05f, d - 0.05f));                         // side pane
                    rail.Box(c + R * hw + U * 1.35f, R, U, F, new Vector3(0.06f, 1.1f, d - 0.02f));                          // ad frame
                    float u0 = (ad % 4) * 0.25f, v1 = ad < 4 ? 1f : 0.5f;
                    Panel(cell.V(BusAds), c + R * hw + U * 1.35f, R, F, d - 0.1f, 1.0f, new Vector4(u0, v1 - 0.5f, u0 + 0.25f, v1), true, 0.07f);
                    cell.V(Mat.Timber).Box(c - F * (d - 0.32f) + U * 0.46f, R, U, F, new Vector3(hw - 0.5f, 0.04f, 0.22f));    // bench
                    cell.Boxes.Add(new CellGeo.BoxCol { Center = c - F * d + U * 1.3f, Rotation = rot, Size = new Vector3(hw * 2f, 2.6f, 0.2f), Layer = Layers.Prop, Name = "bus_shelter" });
                    shelters++;
                }
            // hero plazas: paving draped on the ground with a raised stone edging ring
            int plazas = 0;
            if (doc["heroPlazas"] is JArray hps)
                foreach (JArray t in hps)
                {
                    var c = new Vector3((float)t[0], (float)t[1], (float)t[2]);
                    float r = (float)t[3];
                    var cell = cells.At(c);
                    var pave = cell.V(Mat.Plaza);
                    var edge = cell.V(Mat.Kerb);
                    const int S = 40, RINGS = 5;
                    Vector3 P(float rr, float a) { float x = c.x + Mathf.Cos(a) * rr, z = c.z + Mathf.Sin(a) * rr; return new Vector3(x, h.Sample(x, z) + 0.06f, z); }
                    int ci = pave.Add(new Vector3(c.x, h.Sample(c.x, c.z) + 0.06f, c.z), Vector3.up, Vector2.zero);
                    int prev0 = -1;
                    for (int ring = 1; ring <= RINGS; ring++)
                    {
                        float rr = r * ring / RINGS;
                        int b0 = pave.Count;
                        for (int i = 0; i <= S; i++) pave.Add(P(rr, -i / (float)S * Mathf.PI * 2f), Vector3.up, Vector2.zero);
                        for (int i = 0; i < S; i++)
                        {
                            if (ring == 1) pave.Tri(ci, b0 + i, b0 + i + 1);
                            else pave.Quad(prev0 + i, b0 + i, b0 + i + 1, prev0 + i + 1);
                        }
                        prev0 = b0;
                    }
                    for (int i = 0; i < S; i++)
                    {
                        float a0 = -i / (float)S * Mathf.PI * 2f, a1 = -(i + 1) / (float)S * Mathf.PI * 2f;
                        Vector3 i0 = P(r, a0), i1 = P(r, a1), o0 = P(r + 0.3f, a0), o1 = P(r + 0.3f, a1);
                        var up = Vector3.up * 0.12f;
                        edge.FlatQuad(i0 + up, o0 + up, o1 + up, i1 + up);
                        edge.FlatQuad(o0 + up, o0 - Vector3.up * 0.15f, o1 - Vector3.up * 0.15f, o1 + up);
                        edge.FlatQuad(i1 + up, i1 - Vector3.up * 0.02f, i0 - Vector3.up * 0.02f, i0 + up);
                    }
                    plazas++;
                }
            // exposed side and back walls: painted windows per floor, a mural, or ivy (atlas Textures/wall_decor.png)
            int walls = 0, quads = 0;
            if (doc["wallDecor"] is JArray wds)
                foreach (JArray t in wds)
                {
                    var c = new Vector3((float)t[0], (float)t[1], (float)t[2]);
                    var n = Quaternion.Euler(0f, (float)t[3], 0f) * Vector3.forward;
                    float len = (float)t[4], ht = (float)t[5];
                    int kind = (int)t[6], v = (int)t[7];
                    var right = Vector3.Cross(Vector3.up, -n);                // the viewer's right, looking at the wall
                    var b = cells.At(c).V(WallDecor);
                    if (kind == 0)
                    {
                        int floors = Mathf.Max(1, Mathf.RoundToInt((ht - 0.9f) / 3.7f));
                        float fh = (ht - 0.9f) / floors;
                        int cols = Mathf.Max(1, Mathf.FloorToInt((len - 1.4f) / 2.8f));
                        float span = cols * 2.8f;
                        var uv = Cell(1024 + (v % 4) * 256, 512 + (v / 4) * 256, 256, 256, 2048f);
                        for (int f = 0; f < floors; f++)
                            for (int k = 0; k < cols; k++)
                            {
                                float x = -span / 2f + 1.4f + k * 2.8f;
                                Panel(b, c + right * x + Vector3.up * (0.9f + f * fh + fh * 0.5f), n, right, 0.85f, 0.85f, uv, false, 0.06f);
                                quads++;
                            }
                    }
                    else if (kind == 1)
                    {
                        float side = Mathf.Min(len - 1.2f, ht - 2.2f, 14f);
                        if (side < 3f) continue;
                        Panel(b, c + Vector3.up * (1.2f + side / 2f), n, right, side / 2f, side / 2f, Cell(v * 512, 0, 512, 512, 2048f), false, 0.06f);
                        quads++;
                    }
                    else
                    {
                        float side = Mathf.Min(len - 0.4f, 9f, ht * 0.9f);
                        Panel(b, c + Vector3.up * (side / 2f), n, right, side / 2f, side / 2f, Cell(v * 512, 512, 512, 512, 2048f), false, 0.07f);
                        quads++;
                    }
                    walls++;
                }
            // W4.1 shot dressing: signposts, flag poles and the convention centre's event props
            int posts = 0, flags = 0;
            if (doc["signposts"] is JArray sps)
                foreach (JArray t in sps)
                {
                    var c = new Vector3((float)t[0], (float)t[1], (float)t[2]);
                    var n = Quaternion.Euler(0f, (float)t[3], 0f) * Vector3.forward;
                    var right = Vector3.Cross(Vector3.up, -n);
                    var cell = cells.At(c);
                    var timber = cell.V(Mat.Timber);
                    foreach (var sx in new[] { -1.15f, 1.15f }) timber.Box(c + right * sx + Vector3.up * 1.1f, right, Vector3.up, n, new Vector3(0.07f, 1.1f, 0.07f));
                    Panel(cell.V("sign_post_" + (string)t[4]), c + Vector3.up * 1.75f, n, right, 1.2f, 0.6f, new Vector4(0, 0, 1, 1), true, 0.08f);
                    cell.Boxes.Add(new CellGeo.BoxCol { Center = c + Vector3.up * 1.1f, Rotation = Quaternion.LookRotation(n), Size = new Vector3(2.5f, 2.2f, 0.2f), Layer = Layers.Prop, Name = "signpost" });
                    posts++;
                }
            if (doc["flagPoles"] is JArray fps)
                foreach (JArray t in fps)
                {
                    var c = new Vector3((float)t[0], (float)t[1], (float)t[2]);
                    var fly = Quaternion.Euler(0f, (float)t[3], 0f) * Vector3.forward;   // the yaw is the direction the cloth flies
                    var col = new Color((float)t[4], (float)t[5], (float)t[6]);
                    var cell = cells.At(c);
                    cell.V(Mat.Rail).Box(c + Vector3.up * 3.6f, Vector3.right, Vector3.up, Vector3.forward, new Vector3(0.05f, 3.6f, 0.05f));
                    var cloth = cell.V(Mat.Lighthouse, color: true);
                    var top = c + Vector3.up * 7.0f;
                    var nrm = Vector3.Cross(fly, Vector3.up).normalized;
                    // a cloth with a little wave in it: three panels, both sides
                    for (int side = 0; side < 2; side++)
                        for (int k = 0; k < 3; k++)
                        {
                            float u0 = k * 0.6f, u1 = (k + 1) * 0.6f;
                            Vector3 w0 = nrm * Mathf.Sin(k * 1.4f) * 0.12f, w1 = nrm * Mathf.Sin((k + 1) * 1.4f) * 0.12f;
                            var a = top + fly * u0 + w0; var b = top + fly * u1 + w1; var d = a - Vector3.up * 1.2f; var e = b - Vector3.up * 1.2f;
                            var nn = side == 0 ? nrm : -nrm;
                            int i0 = cloth.Add(a, nn, Vector2.zero, default, default, col), i1 = cloth.Add(b, nn, Vector2.zero, default, default, col);
                            int i2 = cloth.Add(e, nn, Vector2.zero, default, default, col), i3 = cloth.Add(d, nn, Vector2.zero, default, default, col);
                            if (side == 0) { cloth.Tri(i0, i1, i2); cloth.Tri(i0, i2, i3); } else { cloth.Tri(i0, i2, i1); cloth.Tri(i0, i3, i2); }
                        }
                    flags++;
                }
            int eventProps = 0;
            if (doc["eventDress"] is JObject ev && ev.HasValues)
            {
                var navy = new Color(0.10f, 0.14f, 0.32f); var coral = new Color(0.98f, 0.50f, 0.40f);
                if (ev["arch"] is JArray ar)
                {
                    var c = new Vector3((float)ar[0], (float)ar[1], (float)ar[2]);
                    var n = Quaternion.Euler(0f, (float)ar[3], 0f) * Vector3.forward;
                    var right = Vector3.Cross(Vector3.up, -n);
                    float hw = (float)ar[4] / 2f;
                    var cell = cells.At(c);
                    var body = cell.V(Mat.Lighthouse, color: true);
                    foreach (var sx in new[] { -hw, hw })
                    {
                        body.Box(c + right * sx + Vector3.up * 2.4f, right, Vector3.up, n, new Vector3(0.35f, 2.4f, 0.35f), navy);
                        body.Box(c + right * sx + Vector3.up * 0.15f, right, Vector3.up, n, new Vector3(0.5f, 0.15f, 0.5f), coral);
                        cell.Boxes.Add(new CellGeo.BoxCol { Center = c + right * sx + Vector3.up * 2.4f, Rotation = Quaternion.LookRotation(n), Size = new Vector3(0.7f, 4.8f, 0.7f), Layer = Layers.Prop, Name = "event_arch" });
                    }
                    body.Box(c + Vector3.up * 5.25f, right, Vector3.up, n, new Vector3(hw + 0.45f, 0.55f, 0.3f), navy);
                    Panel(cell.V(EventBanners), c + Vector3.up * 5.25f, n, right, hw + 0.3f, 0.5f, new Vector4(0f, 0.75f, 1f, 1f), true, 0.32f);
                    eventProps++;
                }
                if (ev["banners"] is JArray bns)
                    foreach (JArray t in bns)
                    {
                        var c = new Vector3((float)t[0], (float)t[1], (float)t[2]);
                        var n = Quaternion.Euler(0f, (float)t[3], 0f) * Vector3.forward;
                        var right = Vector3.Cross(Vector3.up, -n);
                        int k = (int)t[4];
                        var cell = cells.At(c);
                        cell.V(Mat.Rail).Box(c + Vector3.up * 2.2f, right, Vector3.up, n, new Vector3(0.05f, 2.2f, 0.05f));
                        cell.V(Mat.Rail).Box(c + Vector3.up * 4.25f + right * 0.45f, right, Vector3.up, n, new Vector3(0.45f, 0.03f, 0.03f));
                        // a vertical banner facing along the approach, both sides (atlas cell k: 256 x 768 below the arch banner)
                        Panel(cell.V(EventBanners), c + right * 0.45f + Vector3.up * 2.95f, right, n, 0.42f, 1.25f,
                              new Vector4(k * 0.25f, 0f, (k + 1) * 0.25f, 0.75f), true, 0.02f);
                        eventProps++;
                    }
                if (ev["queues"] is JArray qs)
                    foreach (JArray t in qs)
                    {
                        var c = new Vector3((float)t[0], (float)t[1], (float)t[2]);
                        var along = Quaternion.Euler(0f, (float)t[3], 0f) * Vector3.forward;
                        var side = Vector3.Cross(Vector3.up, along);
                        float len = (float)t[4];
                        var cell = cells.At(c);
                        var body = cell.V(Mat.Lighthouse, color: true);
                        int posts2 = Mathf.Max(2, Mathf.RoundToInt(len / 1.4f) + 1);
                        for (int i = 0; i < posts2; i++)
                        {
                            var p0 = c + along * (-len / 2f + i * len / (posts2 - 1));
                            body.Box(p0 + Vector3.up * 0.5f, side, Vector3.up, along, new Vector3(0.04f, 0.5f, 0.04f), new Color(0.82f, 0.80f, 0.70f));
                            body.Box(p0 + Vector3.up * 0.02f, side, Vector3.up, along, new Vector3(0.17f, 0.02f, 0.17f), new Color(0.3f, 0.3f, 0.32f));
                            if (i > 0) body.Box(p0 - along * (len / (posts2 - 1) / 2f) + Vector3.up * 0.88f, side, Vector3.up, along, new Vector3(0.012f, 0.035f, len / (posts2 - 1) / 2f), coral);
                        }
                        eventProps++;
                    }
            }
            receipt["dressSignposts"] = posts; receipt["dressFlagPoles"] = flags; receipt["dressEventProps"] = eventProps;
            receipt["dressTreePits"] = pits; receipt["dressBusShelters"] = shelters; receipt["dressHeroPlazas"] = plazas;
            receipt["dressDecorWalls"] = walls; receipt["dressDecorQuads"] = quads;
        }

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
