using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.EditorTools.VenueBuild;
using CTLife.World;
using CTLife.World.Roads;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// Builds CT Island v2 (World lane, 2026-10-17) from the generator output (tools/world/islandgen/out, see
    /// docs/design/ct-island-v2-plan.md):
    ///  - Resources/World/Island/CTIsland.prefab (always loaded): IslandWorld with the venue placements, districts, road
    ///    network and pedestrian NavMesh; the far terrain; the sea, river and lakes; coast walls and the flight ceiling;
    ///    helipads and traffic zones; light and air.
    ///  - Resources/World/Island/Cells/cell_x_z.prefab (streamed): terrain chunks with LODs and colliders, roads, junctions,
    ///    bridges, the tunnel, rails, parking bays and venue door blockers.
    ///  - World/Island/Roads/RoadNetwork.asset (the contract's road graph and runtime API).
    /// Project settings it owns: the World layers 10-17, Characters ignore VehicleBlocker, prebaked collision meshes, URP
    /// main-light shadow support (the island sets the distance at runtime).
    /// Batch: Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.IslandV2.IslandV2Builder.Run
    /// </summary>
    public static class IslandV2Builder
    {
        [MenuItem("CT Life/World/Build CT Island v2")]
        public static void Menu() => Run();

        public static void Run()
        {
            string fatal = null;
            var receipt = new JObject();
            var t0 = DateTime.UtcNow;
            try { Build(receipt); }
            catch (Exception e) { fatal = e.GetType().Name + ": " + e.Message; Debug.LogException(e); }
            receipt["fatal"] = fatal;
            receipt["seconds"] = Math.Round((DateTime.UtcNow - t0).TotalSeconds, 1);
            var outDir = Arg("-ctlife-captureOut");
            if (outDir != null) { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, "island-v2-build-receipt.json"), receipt.ToString()); }
            Debug.Log("CTLIFE [islandv2] receipt " + receipt.ToString(Newtonsoft.Json.Formatting.None));
            Debug.Log($"CTLIFE [islandv2] {(fatal == null ? "PASS" : "FAIL " + fatal)}");
            if (Application.isBatchMode) EditorApplication.Exit(fatal == null ? 0 : 1);
        }

        public static void Build(JObject receipt)
        {
            // a fresh scene first: switching scenes later would unload the in-memory meshes built below
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var d in new[] { IslandV2Paths.Materials, IslandV2Paths.Meshes, IslandV2Paths.Roads, IslandV2Paths.ResourcesDir, IslandV2Paths.CellsDir })
                Directory.CreateDirectory(d);
            RetireV1(receipt);
            ProjectSettingsForWorld(receipt);
            AssetDatabase.Refresh();
            ImportTextures();
            var mats = Materials();
            var h = IslandV2Height.Load(IslandV2Paths.Out);
            var roads = IslandV2Paths.Json("roads.json");
            var plan = IslandV2Paths.Json("plan.json");
            receipt["heightfield"] = $"{h.N}x{h.N} @ {h.Cell} m, y {h.H.Min():0.0}..{h.H.Max():0.0}";

            // road network asset
            var segs = IslandV2Roads.Parse(roads);
            var net = AssetDatabase.LoadAssetAtPath<RoadNetwork>(RoadNetwork.AssetPath);
            if (net == null) { net = ScriptableObject.CreateInstance<RoadNetwork>(); AssetDatabase.CreateAsset(net, RoadNetwork.AssetPath); }
            IslandV2Roads.FillNetwork(net, roads, segs);
            EditorUtility.SetDirty(net);
            AssetDatabase.SaveAssetIfDirty(net);
            receipt["roadNetwork"] = $"{net.nodes.Count} nodes, {net.segments.Count} segments, {net.intersections.Count} intersections, {net.lanes.Count} lanes";

            // cells: terrain, venues' door blockers, roads
            var cells = new CellSet(h);
            var chunks = IslandV2Terrain.Chunks(h);
            foreach (var c in chunks) cells.Get(c.Cell).Chunks.Add(c);
            var hfAssets = HeightfieldAssets(h);
            receipt["terrainChunks"] = chunks.Count;
            var venues = Venues(plan, out var doors);
            receipt["venues"] = venues.Count;
            var ctx = new IslandV2Roads.Context { H = h, Cells = cells, Doors = doors.Select(d => d.pos).ToList() };
            IslandV2Roads.Build(segs, roads, ctx);
            receipt["roadPieces"] = ctx.Pieces; receipt["parkingBays"] = ctx.Bays; receipt["railMetres"] = ctx.RailMetres; receipt["crosswalks"] = ctx.Crosswalks; receipt["wallMetres"] = Mathf.RoundToInt(ctx.WallMetres);
            foreach (var (pos, fwd) in doors)
            {
                // a vehicle blocker across each venue doorway: people walk through it, cars stop
                var c = pos + fwd * 1.3f + Vector3.up * 1.3f;
                cells.At(c).Boxes.Add(new CellGeo.BoxCol { Center = c, Rotation = Quaternion.LookRotation(fwd, Vector3.up), Size = new Vector3(5.2f, 2.6f, 1.0f), Layer = Layers.VehicleBlocker, Name = "vehicle_blocker" });
            }

            // paved plazas (Timeline Square's block): the block inset to its sidewalk edges, pavers on the ground
            if (plan["plazas"] is JArray plazas)
                foreach (var pz in plazas)
                {
                    var poly = ((JArray)pz["polygon"]).Select(q => new Vector2((float)q[0], (float)q[1])).ToArray();
                    var inner = Inset(poly, 9.2f);
                    var c = inner.Aggregate(Vector2.zero, (a, b) => a + b) / inner.Length;
                    float y = inner.Concat(new[] { c }).Max(q => h.Sample(q.x, q.y)) + 0.05f;    // just above the graded ground
                    var v = inner.Select(q => new Vector3(q.x, y, q.y)).ToArray();
                    var geo = cells.At(new Vector3(c.x, 0f, c.y));
                    geo.V(Mat.Plaza).FlatQuad(v[0], v[3], v[2], v[1]);         // clockwise from above
                    receipt["plaza"] = $"{pz["id"]}: {string.Join(" ", v.Select(q => $"({q.x:0.0},{q.y:0.00},{q.z:0.0})"))}";
                }

            // waterfront: quay walls and paving, the bay boardwalk, the lighthouse
            IslandV2Coast.Build(plan, h, cells, receipt);
            // the venues' street-side buildings
            IslandV2Shells.Build(plan, cells, mats, receipt);

            // dressing: buildings, street furniture, trees, rocks, crops (dress.json) into their cells
            IslandV2Dress.Place(cells, h, receipt);

            // write the streamed cells
            foreach (var old in Directory.GetFiles(IslandV2Paths.CellsDir, "*.prefab")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
            foreach (var old in Directory.GetFiles(IslandV2Paths.Meshes, "*.asset")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
            if (Directory.Exists(IslandV2Paths.Data + "/Dress"))
                foreach (var old in Directory.GetFiles(IslandV2Paths.Data + "/Dress", "*.bytes")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
            var cellList = new List<IslandWorld.Cell>();
            var roadSurfaces = new List<Mesh>();
            int prefabs = 0, colliders = 0, renderers = 0, bays = 0;
            foreach (var g in cells.All.OrderBy(c => c.Cell.y).ThenBy(c => c.Cell.x))
            {
                var stats = WriteCell(g, mats, roadSurfaces);
                if (stats.objects == 0) continue;
                prefabs++; colliders += stats.colliders; renderers += stats.renderers; bays += stats.bays;
                cellList.Add(new IslandWorld.Cell { id = IslandV2Paths.CellId(g.Cell), rect = IslandV2Paths.CellRect(g.Cell, h) });
            }
            receipt["cells"] = prefabs; receipt["cellColliders"] = colliders; receipt["cellRenderers"] = renderers;

            // NavMesh (pedestrians)
            var nav = IslandV2Nav.Build(h, plan, venues, roadSurfaces, receipt);
            if (nav == null) throw new InvalidOperationException("island NavMesh build returned null");
            nav = Save(nav, IslandV2Paths.NavPath);
            receipt["navMeshBytes"] = new FileInfo(IslandV2Paths.NavPath).Length;

            // the base prefab
            WriteBase(h, plan, venues, cellList, net, nav, mats, hfAssets, receipt);
            AssetDatabase.SaveAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            receipt["meshBytes"] = Directory.GetFiles(IslandV2Paths.Meshes, "*.asset").Sum(f => new FileInfo(f).Length);
        }

        /// <summary>A convex polygon (counter-clockwise from above) shrunk by d metres: each edge moves inward.</summary>
        private static Vector2[] Inset(Vector2[] p, float d)
        {
            int n = p.Length;
            var lines = new (Vector2 a, Vector2 dir)[n];
            for (int i = 0; i < n; i++)
            {
                var a = p[i]; var b = p[(i + 1) % n];
                var t = (b - a).normalized;
                var inward = new Vector2(-t.y, t.x);                      // left of a counter-clockwise edge
                lines[i] = (a + inward * d, t);
            }
            var r = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var (a1, d1) = lines[(i + n - 1) % n]; var (a2, d2) = lines[i];
                float den = d1.x * d2.y - d1.y * d2.x;
                float t = Mathf.Abs(den) < 1e-6f ? 0f : ((a2.x - a1.x) * d2.y - (a2.y - a1.y) * d2.x) / den;
                r[i] = a1 + d1 * t;
            }
            return r;
        }

        // ------------------------------------------------------------------ project

        private static void RetireV1(JObject receipt)
        {
            var gone = new List<string>();
            foreach (var p in new[] { "Assets/World/Island/Ground", "Assets/World/Island/IslandGround.mat", "Assets/World/Island/IslandGround.png",
                         "Assets/World/Island/IslandSea.mat" })
                if (AssetDatabase.LoadAssetAtPath<Object>(p) != null || AssetDatabase.IsValidFolder(p)) { AssetDatabase.DeleteAsset(p); gone.Add(p); }
            receipt["retiredV1"] = new JArray(gone);
        }

        private static void ProjectSettingsForWorld(JObject receipt)
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            foreach (var (i, name) in Layers.All)
            {
                var el = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(el.stringValue) || el.stringValue == name) el.stringValue = name;
                else Debug.LogWarning($"CTLIFE [islandv2] layer {i} is '{el.stringValue}', expected '{name}'");
            }
            tm.ApplyModifiedPropertiesWithoutUndo();
            Physics.IgnoreLayerCollision(8, Layers.VehicleBlocker, true);    // Characters walk through door blockers
            Physics.IgnoreLayerCollision(9, Layers.VehicleBlocker, true);    // Ragdoll
            PlayerSettings.bakeCollisionMeshes = true;
            var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                var so = new SerializedObject(urp);
                so.FindProperty("m_MainLightShadowsSupported").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(urp);
            }
            receipt["layers"] = string.Join(", ", Layers.All.Select(l => $"{l.index} {l.name}"));
        }

        private static void ImportTextures()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            void Set(string file, bool srgb, bool repeat, int max, bool normal = false, TextureImporterFormat fmt = TextureImporterFormat.ASTC_6x6)
            {
                var path = IslandV2Paths.Textures + "/" + file;
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) { Debug.LogWarning("CTLIFE [islandv2] missing texture " + path); return; }
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = srgb && !normal;
                ti.mipmapEnabled = true;
                ti.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.anisoLevel = repeat ? 2 : 1;
                ti.maxTextureSize = max;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                var a = ti.GetPlatformTextureSettings("Android");
                a.overridden = true; a.maxTextureSize = max; a.format = fmt;
                ti.SetPlatformTextureSettings(a);
                ti.SaveAndReimport();
            }
            Set("terrain_control.png", false, false, 1024, fmt: TextureImporterFormat.ASTC_5x5);
            Set("terrain_macro.png", true, false, 1024);
            Set("terrain_far_macro.png", true, false, 2048);
            Set("terrain_detail.png", false, true, 1024, fmt: TextureImporterFormat.ASTC_5x5);
            Set("asphalt_detail.png", false, true, 512);
            Set("dirt_detail.png", false, true, 512);
            Set("pavers.png", true, true, 512);
            Set("concrete.png", true, true, 512);
            Set("water_ripples.png", false, true, 256, normal: true);
            Set("water_depth.png", false, false, 1024, fmt: TextureImporterFormat.R8);   // smooth gradients, no ASTC blocks
            Set("clouds.png", false, true, 512, fmt: TextureImporterFormat.ASTC_6x6);
            Set("boardwalk.png", true, true, 256);
            foreach (var f in System.IO.Directory.GetFiles(IslandV2Paths.Textures + "/Signs", "*.png")) Set("Signs/" + System.IO.Path.GetFileName(f), true, false, 512);
        }

        /// <summary>Import settings for a W4 dressing texture: mipmapped, clamped, alpha kept, ASTC 6x6 up to maxSize on Android.</summary>
        private static void DecorTexture(string file, int maxSize)
        {
            var path = IslandV2Paths.Textures + "/" + file;
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp;
            ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f;
            ti.maxTextureSize = maxSize;
            var a = ti.GetPlatformTextureSettings("Android"); a.overridden = true; a.maxTextureSize = maxSize; a.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(a);
            ti.SaveAndReimport();
        }

        private static Texture2D Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(IslandV2Paths.Textures + "/" + file);

        private static Dictionary<string, Material> Materials()
        {
            var m = new Dictionary<string, Material>();
            Material Mk(string key, string shader, Action<Material> setup)
            {
                var path = IslandV2Paths.Materials + "/" + key + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                var sh = Shader.Find(shader) ?? throw new InvalidOperationException("shader not found: " + shader);
                if (mat == null) { mat = new Material(sh) { name = key }; AssetDatabase.CreateAsset(mat, path); }
                mat.shader = sh;
                setup(mat);
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                m[key] = mat;
                return mat;
            }
            void Ground(Material x)
            {
                x.SetTexture("_Control", Tex("terrain_control.png")); x.SetTexture("_Detail", Tex("terrain_detail.png"));
                x.SetColor("_RockColor", new Color(0.38f, 0.36f, 0.37f)); x.SetColor("_RockColorHigh", new Color(0.55f, 0.53f, 0.56f));   // darker, so the range reads against the sky
                x.SetColor("_SandColor", new Color(0.92f, 0.83f, 0.62f)); x.SetColor("_DirtColor", new Color(0.56f, 0.43f, 0.30f));
                x.SetVector("_Snow", new Vector4(138f, 156f, 0.45f, 0.6f)); x.SetVector("_Shore", new Vector4(-6f, 1.1f, 0f, 0f)); x.SetVector("_DetailScale", new Vector4(0.16f, 0.035f, 0f, 0f));
                x.SetFloat("_Contrast", 0.65f); x.SetFloat("_Sharpness", 2.5f); x.SetFloat("_Toon", 0.3f);
            }
            Mk("terrain", "CT Life/World/Terrain", x => { Ground(x); x.SetTexture("_Macro", Tex("terrain_macro.png")); x.DisableKeyword("_FAR"); });
            Mk("terrain_far", "CT Life/World/Terrain", x => { Ground(x); x.SetTexture("_Macro", Tex("terrain_far_macro.png")); x.EnableKeyword("_FAR"); });
            Mk(Mat.Road, "CT Life/World/Road", x => { x.SetTexture("_Detail", Tex("asphalt_detail.png")); x.SetColor("_Color", new Color(0.34f, 0.35f, 0.38f)); });
            Mk(Mat.Dirt, "CT Life/World/Road", x =>
            {
                x.SetTexture("_Detail", Tex("dirt_detail.png")); x.SetColor("_Color", new Color(0.62f, 0.50f, 0.36f)); x.SetFloat("_Tracks", 0.18f); x.SetFloat("_Contrast", 1.1f);
            });
            Mk(Mat.Sidewalk, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Tex("pavers.png")); x.EnableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 1); x.SetFloat("_WorldScale", 0.42f); x.SetFloat("_Toon", 0.3f); x.SetVector("_TileVar", new Vector4(1.7f, 0.11f, 0.18f, 0f)); });
            Mk(Mat.Plaza, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Tex("pavers.png")); x.EnableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 1); x.SetFloat("_WorldScale", 0.3f); x.SetColor("_BaseColor", new Color(1.0f, 0.97f, 0.92f)); x.SetFloat("_Toon", 0.3f); x.SetVector("_TileVar", new Vector4(1.25f, 0.1f, 0.15f, 0f)); });
            Mk(Mat.Kerb, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Tex("concrete.png")); x.EnableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 1); x.SetFloat("_WorldScale", 0.5f); x.SetColor("_BaseColor", new Color(0.93f, 0.91f, 0.88f)); x.SetVector("_TileVar", new Vector4(1.0f, 0.16f, 0.22f, 0f)); });
            Mk(Mat.Concrete, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Tex("concrete.png")); x.EnableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 1); x.SetFloat("_WorldScale", 0.25f); x.SetColor("_BaseColor", new Color(0.86f, 0.84f, 0.8f)); x.SetVector("_TileVar", new Vector4(0.45f, 0.1f, 0.22f, 0f)); });
            Mk(Mat.Tunnel, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Tex("concrete.png")); x.EnableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 1); x.SetFloat("_WorldScale", 0.25f); x.SetColor("_BaseColor", new Color(0.62f, 0.6f, 0.57f)); x.SetFloat("_Cull", 0f); });
            Mk(Mat.Rail, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Texture2D.whiteTexture); x.SetColor("_BaseColor", new Color(0.78f, 0.80f, 0.82f)); x.SetVector("_Spec", new Vector4(0.25f, 40f, 0, 0)); x.SetFloat("_Cull", 0f); });
            Mk(Mat.Paint, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Tex("asphalt_detail.png")); x.SetColor("_BaseColor", new Color(1.0f, 1.0f, 0.98f)); x.SetFloat("_Toon", 0.1f); });
            Mk(Mat.Grass, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Tex("terrain_detail.png")); x.SetColor("_BaseColor", new Color(0.46f, 0.70f, 0.30f)); x.EnableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 1); x.SetFloat("_WorldScale", 0.2f); });
            Mk("water", "CT Life/World/Water", x => { x.SetTexture("_DepthMap", Tex("water_depth.png")); x.SetTexture("_Ripples", Tex("water_ripples.png")); x.SetVector("_Waves", new Vector4(0.13f, 42f, 1.0f, 0.5f)); });
            Mk("water_river", "CT Life/World/Water", x =>
            {
                x.SetTexture("_DepthMap", Tex("water_depth.png")); x.SetTexture("_Ripples", Tex("water_ripples.png")); x.SetVector("_Waves", new Vector4(0.03f, 20f, 1.0f, 0.5f));
                x.SetColor("_Shallow", new Color(0.42f, 0.72f, 0.66f)); x.SetColor("_Deep", new Color(0.16f, 0.42f, 0.48f)); x.SetVector("_RippleScale", new Vector4(0.11f, 0.2f, 0, 0));
            });
            Mk("barrier", "CT Life/World/Toon", x => { x.SetColor("_BaseColor", Color.white); });
            Mk("shell_roof", "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Tex("concrete.png")); x.EnableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 1); x.SetFloat("_WorldScale", 0.25f); x.SetColor("_BaseColor", Color.white); x.SetFloat("_Toon", 0.35f); x.SetFloat("_Cull", 0f); });
            Mk("bird", "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Texture2D.whiteTexture); x.SetColor("_BaseColor", new Color(0.97f, 0.97f, 0.95f)); x.SetFloat("_Toon", 0.2f); x.SetFloat("_Cull", 0f); });
            Mk(Mat.Quay, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Tex("concrete.png")); x.EnableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 1); x.SetFloat("_WorldScale", 0.3f); x.SetColor("_BaseColor", new Color(0.72f, 0.69f, 0.64f)); x.SetFloat("_Toon", 0.35f); });
            Mk(Mat.Boardwalk, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Tex("boardwalk.png")); x.DisableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 0); x.SetColor("_BaseColor", Color.white); x.SetFloat("_Toon", 0.3f); });
            Mk(Mat.Timber, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Texture2D.whiteTexture); x.SetColor("_BaseColor", new Color(0.50f, 0.38f, 0.27f)); x.SetFloat("_Toon", 0.35f); });
            Mk(Mat.Lighthouse, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Texture2D.whiteTexture); x.SetColor("_BaseColor", Color.white); x.SetFloat("_Toon", 0.4f); });
            Mk(Mat.LighthouseLamp, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Texture2D.whiteTexture); x.SetColor("_BaseColor", new Color(1f, 0.93f, 0.66f)); x.SetColor("_Emission", new Color(1.6f, 1.35f, 0.8f)); x.SetFloat("_Cull", 0f); });
            // W4 eye-level dressing (textures from tools/world/islandgen/decor.py)
            DecorTexture("wall_decor.png", 2048); DecorTexture("bus_ads.png", 1024); DecorTexture("helipad.png", 512);
            Mk(IslandV2Dress.WallDecor, "CT Life/World/Toon", x =>
            {
                x.SetTexture("_BaseMap", Tex("wall_decor.png")); x.DisableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 0); x.SetColor("_BaseColor", Color.white);
                x.SetFloat("_Toon", 0.35f); x.SetFloat("_Cutoff", 0.5f);
            });
            Mk(IslandV2Dress.BusAds, "CT Life/World/Toon", x =>
            {
                x.SetTexture("_BaseMap", Tex("bus_ads.png")); x.DisableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 0); x.SetColor("_BaseColor", Color.white);
                x.SetFloat("_Toon", 0.15f); x.SetColor("_Emission", new Color(0.55f, 0.55f, 0.55f));          // backlit poster
            });
            Mk(IslandV2Dress.Glass, "CT Life/World/Toon", x =>
            {
                x.SetTexture("_BaseMap", Texture2D.whiteTexture); x.DisableKeyword("_WORLDUV"); x.SetColor("_BaseColor", new Color(0.62f, 0.78f, 0.86f));
                x.SetVector("_Spec", new Vector4(0.7f, 70f, 0, 0)); x.SetFloat("_Toon", 0.2f);
            });
            Mk(IslandV2Dress.TreePit, "CT Life/World/Toon", x =>
            {
                x.SetTexture("_BaseMap", Tex("dirt_detail.png")); x.EnableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 1); x.SetFloat("_WorldScale", 0.6f);
                x.SetColor("_BaseColor", new Color(0.42f, 0.32f, 0.24f));
            });
            // W4.1 shot dressing: event banners and signposts (tools/world/islandgen/signs.py)
            Mk(IslandV2Dress.EventBanners, "CT Life/World/Toon", x =>
            {
                x.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(IslandV2Paths.Textures + "/Signs/event_banners.png") ?? Texture2D.whiteTexture);
                x.DisableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 0); x.SetColor("_BaseColor", Color.white); x.SetFloat("_Toon", 0.2f); x.SetColor("_Emission", new Color(0.2f, 0.2f, 0.2f));
            });
            foreach (var sid in new[] { "trail", "checkpoint" })
                Mk("sign_post_" + sid, "CT Life/World/Toon", x =>
                {
                    x.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(IslandV2Paths.Textures + "/Signs/sign_post_" + sid + ".png") ?? Texture2D.whiteTexture);
                    x.DisableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 0); x.SetColor("_BaseColor", Color.white); x.SetFloat("_Toon", 0.25f);
                });
            Mk("helipad", "CT Life/World/Toon", x =>
            {
                x.SetTexture("_BaseMap", Tex("helipad.png")); x.DisableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 0); x.SetColor("_BaseColor", Color.white); x.SetFloat("_Toon", 0.3f);
            });
            Mk(IslandV2Dress.ShopSigns, "CT Life/World/Toon", x =>
            {
                x.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(IslandV2Paths.Textures + "/Signs/shop_signs.png") ?? Texture2D.whiteTexture);
                x.DisableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 0); x.SetColor("_BaseColor", Color.white); x.SetFloat("_Toon", 0.2f);
                x.SetColor("_Emission", new Color(0.22f, 0.22f, 0.22f));
            });
            Mk(Mat.Wall, "CT Life/World/Toon", x => { x.SetTexture("_BaseMap", Tex("concrete.png")); x.EnableKeyword("_WORLDUV"); x.SetFloat("_UseWorldUV", 1); x.SetFloat("_WorldScale", 0.35f); x.SetColor("_BaseColor", new Color(0.74f, 0.70f, 0.64f)); x.SetFloat("_Toon", 0.35f); });
            AssetDatabase.SaveAssets();
            return m;
        }

        // ------------------------------------------------------------------ venues

        public static List<IslandVenue> Venues(JObject plan, out List<(Vector3 pos, Vector3 fwd)> doors)
        {
            var list = new List<IslandVenue>();
            doors = new List<(Vector3, Vector3)>();
            foreach (var v in (JArray)plan["venues"])
            {
                var origin = IslandV2Paths.V3(v["origin"]);
                float yaw = (float)v["yaw"];
                var mn = IslandV2Paths.V2(v["frame_min"]); var sz = IslandV2Paths.V2(v["frame_size"]);
                var w = (JArray)v["walk"];
                var localWalk = Rect.MinMaxRect((float)w[0], (float)w[1], (float)w[2], (float)w[3]);
                var iv = new IslandVenue
                {
                    venueId = (string)v["id"], district = (string)v["district"], origin = new Vector2(origin.x, origin.z), elevation = origin.y,
                    yaw = yaw, localFrame = new Rect(mn, sz), localWalk = localWalk,
                    shellHeight = v["shell"] is JObject shj ? (float)shj["h"] : 0f,
                };
                iv.frame = Aabb(iv, iv.localFrame);
                iv.walk = Aabb(iv, iv.localWalk);
                var door = IslandV2Paths.V3(v["door"]);
                iv.exit = new Vector2(door.x, door.z);
                list.Add(iv);
                var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;     // doors face local -z
                doors.Add((new Vector3(door.x, origin.y, door.z), fwd));
            }
            return list;
        }

        private static Rect Aabb(IslandVenue v, Rect local)
        {
            var pts = new[] { new Vector2(local.xMin, local.yMin), new Vector2(local.xMax, local.yMin), new Vector2(local.xMax, local.yMax), new Vector2(local.xMin, local.yMax) }
                .Select(v.ToIsland).ToArray();
            float x0 = pts.Min(p => p.x), x1 = pts.Max(p => p.x), z0 = pts.Min(p => p.y), z1 = pts.Max(p => p.y);
            return Rect.MinMaxRect(x0, z0, x1, z1);
        }

        // ------------------------------------------------------------------ cells

        private static readonly Dictionary<string, (int layer, bool shadows)> MatLayer = new Dictionary<string, (int, bool)>
        {
            { Mat.Road, (Layers.Road, false) }, { Mat.Dirt, (Layers.Road, false) }, { Mat.Sidewalk, (Layers.Road, false) }, { Mat.Plaza, (Layers.Road, false) }, { Mat.Quay, (Layers.Road, false) },
            { Mat.Boardwalk, (Layers.Road, false) }, { Mat.Timber, (Layers.Prop, true) }, { Mat.Lighthouse, (Layers.Building, true) }, { Mat.LighthouseLamp, (Layers.Building, false) },
            { Mat.Kerb, (Layers.Road, false) }, { Mat.Concrete, (Layers.Road, true) }, { Mat.Tunnel, (Layers.Road, true) },
            { Mat.Rail, (Layers.Prop, false) }, { Mat.Paint, (Layers.Road, false) }, { Mat.Grass, (Layers.Prop, false) },
            { IslandV2Dress.ShopSigns, (Layers.Prop, false) },
            { IslandV2Dress.WallDecor, (Layers.Building, false) }, { IslandV2Dress.BusAds, (Layers.Prop, false) },
            { IslandV2Dress.Glass, (Layers.Prop, true) }, { IslandV2Dress.TreePit, (Layers.Road, false) },
            { IslandV2Dress.EventBanners, (Layers.Prop, false) }, { "sign_post_trail", (Layers.Prop, false) }, { "sign_post_checkpoint", (Layers.Prop, false) },
        };

        private static (int objects, int colliders, int renderers, int bays) WriteCell(CellGeo g, Dictionary<string, Material> mats, List<Mesh> roadSurfaces)
        {
            string id = IslandV2Paths.CellId(g.Cell);
            var meshPath = IslandV2Paths.Meshes + "/" + id + ".asset";
            var meshes = new List<Mesh>();
            var root = new GameObject(id);
            int colliders = 0, renderers = 0, bays = 0;
            var parkedCars = new List<ParkedCar>();
            // terrain: generated from the heightfield when the cell loads
            if (g.Chunks.Count > 0)
            {
                var terrain = new GameObject("Terrain") { layer = Layers.Ground };
                terrain.transform.SetParent(root.transform, false);
                var tc = terrain.AddComponent<IslandTerrainCell>();
                tc.chunks = g.Chunks.Select(c => c.Grid).ToArray();
                tc.material = mats["terrain"];
                colliders += g.Chunks.Count; renderers += g.Chunks.Count * 3;
            }
            // road visuals by material
            if (g.Visual.Count > 0)
            {
                var roadsGo = new GameObject("Roads");
                roadsGo.transform.SetParent(root.transform, false);
                foreach (var kv in g.Visual.OrderBy(k => k.Key))
                {
                    if (kv.Value.Empty) continue;
                    var mesh = kv.Value.ToMesh($"{id}_{kv.Key}");
                    meshes.Add(mesh);
                    var (layer, shadows) = MatLayer.TryGetValue(kv.Key, out var ls) ? ls : (Layers.Road, false);
                    var go = new GameObject(kv.Key) { layer = layer };
                    go.transform.SetParent(roadsGo.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mats.TryGetValue(kv.Key, out var mm) ? mm : mats[Mat.Concrete];
                    mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderers++;
                }
            }
            // collision meshes and boxes
            if (g.Collision.Count > 0 || g.Boxes.Count > 0)
            {
                var col = new GameObject("Colliders");
                col.transform.SetParent(root.transform, false);
                foreach (var kv in g.Collision)
                {
                    if (kv.Value.Empty) continue;
                    var mesh = kv.Value.ToMesh($"{id}_col{kv.Key}");
                    meshes.Add(mesh);
                    var go = new GameObject("surface_" + kv.Key) { layer = kv.Key };
                    go.transform.SetParent(col.transform, false);
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    colliders++;
                    if (kv.Key == Layers.Road) roadSurfaces.Add(mesh);
                }
                foreach (var b in g.Boxes)
                {
                    var go = new GameObject(b.Name ?? "box") { layer = b.Layer };
                    go.transform.SetParent(col.transform, false);
                    go.transform.SetPositionAndRotation(b.Center, b.Rotation);
                    go.AddComponent<BoxCollider>().size = b.Size;
                    colliders++;
                }
            }
            // markers
            if (g.Markers.Count > 0)
            {
                var mk = new GameObject("Markers");
                mk.transform.SetParent(root.transform, false);
                foreach (var m in g.Markers)
                {
                    if (m.Kind == "vehicle" && IslandV2Dress.Kit != null && IslandV2Dress.CarProtos.Length > 0 && IslandV2Dress.Hash01(m.Id) < IslandV2Dress.ParkedShare(m.Position))
                    {
                        // a decor car parks here (drawn instanced); Feel can swap it for a drivable one via ParkedCar
                        int pick = IslandV2Dress.CarProtos[(int)(IslandV2Dress.Hash01(m.Id + "car") * IslandV2Dress.CarProtos.Length) % IslandV2Dress.CarProtos.Length];
                        var kp = IslandV2Dress.Kit.protos[pick];
                        float yaw = m.Rotation.eulerAngles.y + (IslandV2Dress.Hash01(m.Id + "flip") < 0.5f ? 0f : 180f);
                        g.Instances.Add(new IslandV2Dress.Inst { Proto = pick, X = m.Position.x, Y = m.Position.y + 0.02f, Z = m.Position.z, Yaw = yaw, S = 1f, Sy = 1f });
                        var pc = new GameObject("parked_" + m.Id) { layer = Layers.Prop };
                        pc.transform.SetParent(mk.transform, false);
                        pc.transform.SetPositionAndRotation(m.Position, Quaternion.Euler(0f, yaw, 0f));
                        var car = pc.AddComponent<ParkedCar>();
                        car.model = kp.id; car.segmentId = m.Segment;
                        car.vehicleClass = kp.id.Contains("suv") || kp.id.Contains("van") || kp.id.Contains("delivery") ? VehicleClass.Suv : kp.id.Contains("sports") ? VehicleClass.Sports : VehicleClass.Compact;
                        var bc = pc.AddComponent<BoxCollider>();
                        bc.size = new Vector3(kp.bounds.size.x * 0.92f, kp.bounds.size.y * 0.9f, kp.bounds.size.z * 0.95f);
                        bc.center = new Vector3(0f, kp.bounds.size.y * 0.45f, 0f);
                        parkedCars.Add(car);
                        colliders++;
                        continue;
                    }
                    var go = new GameObject("bay_" + m.Id) { layer = Layers.Road };
                    go.transform.SetParent(mk.transform, false);
                    go.transform.SetPositionAndRotation(m.Position, m.Rotation);
                    var vs = go.AddComponent<VehicleSpawn>();
                    vs.classes = VehicleClass.Cars | VehicleClass.Moped;
                    vs.size = m.Size; vs.kind = "street"; vs.segmentId = m.Segment; vs.showcase = m.Showcase;
                    bays++;
                }
            }
            // dressing: the instance file, trunk and post capsules
            if (g.Instances.Count > 0 && IslandV2Dress.Kit != null)
            {
                Directory.CreateDirectory(IslandV2Paths.Data + "/Dress");
                var dataPath = IslandV2Paths.Data + "/Dress/" + id + ".bytes";
                File.WriteAllBytes(dataPath, IslandV2Dress.Encode(g.Instances));
                AssetDatabase.ImportAsset(dataPath, ImportAssetOptions.ForceSynchronousImport);
                var dress = new GameObject("Dress");
                dress.transform.SetParent(root.transform, false);
                var set = dress.AddComponent<IslandInstances>();
                set.kit = IslandV2Dress.Kit;
                set.data = AssetDatabase.LoadAssetAtPath<TextAsset>(dataPath);
                foreach (var car in parkedCars) car.set = set;
                renderers++;
            }
            if (g.Shells.Count > 0)
            {
                var shRoot = new GameObject("Shells") { layer = Layers.Building };
                shRoot.transform.SetParent(root.transform, false);
                foreach (var d in g.Shells) { IslandV2Shells.Write(d, shRoot.transform, meshes, id); renderers += 6; colliders += 5; }
            }
            if (g.Billboards.Count > 0)
            {
                var bbRoot = new GameObject("Billboards") { layer = Layers.Prop };
                bbRoot.transform.SetParent(root.transform, false);
                foreach (var bj in g.Billboards) if (IslandV2Dress.Billboard(bj, bbRoot.transform, Layers.Prop) != null) { renderers++; colliders++; }
            }
            if (g.Capsules.Count > 0)
            {
                foreach (var byLayer in g.Capsules.GroupBy(c => c.Layer))
                {
                    var cg = new GameObject(byLayer.Key == Layers.Vegetation ? "Trunks" : "Posts") { layer = byLayer.Key };
                    cg.transform.SetParent(root.transform, false);
                    foreach (var c in byLayer)
                    {
                        var cc = cg.AddComponent<CapsuleCollider>();
                        cc.center = c.Center; cc.radius = c.Radius; cc.height = Mathf.Max(c.Height, c.Radius * 2f); cc.direction = 1;
                        colliders++;
                    }
                }
            }
            int objects = root.transform.childCount;
            if (objects == 0) { Object.DestroyImmediate(root); return (0, 0, 0, 0); }
            // meshes: one asset file per cell (first mesh is the main object)
            if (meshes.Count > 0)
            {
                AssetDatabase.CreateAsset(meshes[0], meshPath);
                for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshPath);
            }
            PrefabUtility.SaveAsPrefabAsset(root, IslandV2Paths.CellsDir + "/" + id + ".prefab");
            Object.DestroyImmediate(root);
            return (objects, colliders, renderers, bays);
        }

        // ------------------------------------------------------------------ base prefab

        private static void WriteBase(IslandHeightfield h, JObject plan, List<IslandVenue> venues, List<IslandWorld.Cell> cells, RoadNetwork net, NavMeshData nav,
            Dictionary<string, Material> mats, (TextAsset height, TextAsset holes) hfAssets, JObject receipt)
        {
            var root = new GameObject("CTIsland");
            var basePath = IslandV2Paths.Meshes + "/base.asset";
            var meshes = new List<Mesh>();
            // far terrain per streaming cell (meshes filled from the heightfield at load)
            var far = new GameObject("Far") { layer = Layers.Ground };
            far.transform.SetParent(root.transform, false);
            var farPieces = new List<IslandTerrainFar.Piece>();
            for (int cz = 0; cz < IslandV2Paths.CellCount(h); cz++)
                for (int cx = 0; cx < IslandV2Paths.CellCount(h); cx++)
                {
                    var cell = new Vector2Int(cx, cz);
                    if (!IslandV2Terrain.Far(h, cell, out var x0, out var z0, out var n)) continue;
                    var go = new GameObject(IslandV2Paths.CellId(cell) + "_far") { layer = Layers.Ground };
                    go.transform.SetParent(far.transform, false);
                    go.transform.localPosition = new Vector3(h.X(x0), 0f, h.Z(z0));
                    var mf = go.AddComponent<MeshFilter>();
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mats["terrain_far"];
                    mr.shadowCastingMode = ShadowCastingMode.Off; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    farPieces.Add(new IslandTerrainFar.Piece { filter = mf, x0 = x0, z0 = z0, cells = n });
                    var entry = cells.FirstOrDefault(c => c.id == IslandV2Paths.CellId(cell));
                    if (entry != null) entry.far = go;
                }
            root.AddComponent<IslandTerrainFar>().pieces = farPieces.ToArray();
            receipt["farCells"] = farPieces.Count;
            // water
            var water = new GameObject("Water") { layer = Layers.Water };
            water.transform.SetParent(root.transform, false);
            void WaterObj(string name, Mesh mesh, Vector3 pos, Material mat)
            {
                meshes.Add(mesh);
                var go = new GameObject(name) { layer = Layers.Water };
                go.transform.SetParent(water.transform, false);
                go.transform.localPosition = pos;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            foreach (var p in IslandV2Water.Sea(h)) WaterObj(p.Name, p.Mesh, p.Origin, mats["water"]);
            WaterObj("river", IslandV2Water.River(plan, h), Vector3.zero, mats["water_river"]);
            foreach (var p in IslandV2Water.Lakes(plan)) WaterObj(p.Name, p.Mesh, p.Origin, mats["water_river"]);
            // barriers: coast walls and the flight ceiling
            var barriers = new GameObject("Barriers");
            barriers.transform.SetParent(root.transform, false);
            var walls = IslandV2Water.CoastWalls(plan, h);
            foreach (var b in walls)
            {
                var go = new GameObject(b.Name) { layer = b.Layer };
                go.transform.SetParent(barriers.transform, false);
                go.transform.SetPositionAndRotation(b.Center, b.Rotation);
                go.AddComponent<BoxCollider>().size = b.Size;
            }
            var ceiling = new GameObject("flight_ceiling") { layer = Layers.Ground };
            ceiling.transform.SetParent(barriers.transform, false);
            ceiling.transform.localPosition = new Vector3(0f, h.FlightCeiling + 1f, 0f);
            ceiling.AddComponent<BoxCollider>().size = new Vector3(7000f, 2f, 7000f);
            receipt["coastWalls"] = walls.Count;
            // markers: helipads (with a painted pad) and traffic zones per district
            var markers = new GameObject("Markers");
            markers.transform.SetParent(root.transform, false);
            int pads = 0;
            foreach (var hp in (JArray)plan["helipads"])
            {
                var p = IslandV2Paths.V3(hp);
                p.y = Mathf.Max(p.y, h.Sample(p.x, p.z)) + 0.05f;
                var go = new GameObject("helipad_" + pads);
                go.transform.SetParent(markers.transform, false);
                go.transform.localPosition = p;
                go.AddComponent<HeliPad>().radius = 9f;
                var pad = new MeshBuilder();
                Disc(pad, Vector3.zero, 9f, 0.25f, true);
                var pm = pad.ToMesh("helipad_" + pads);
                meshes.Add(pm);
                var vis = new GameObject("pad") { layer = Layers.Road };
                vis.transform.SetParent(go.transform, false);
                vis.AddComponent<MeshFilter>().sharedMesh = pm;
                var padMr = vis.AddComponent<MeshRenderer>();
                padMr.sharedMaterial = mats.TryGetValue("helipad", out var hm) ? hm : mats[Mat.Concrete];
                padMr.lightProbeUsage = LightProbeUsage.Off; padMr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                vis.AddComponent<MeshCollider>().sharedMesh = pm;
                pads++;
            }
            foreach (var d in (JArray)plan["districts"])
            {
                var poly = ((JArray)d["poly"]).Select(IslandV2Paths.V2).ToArray();
                float x0 = poly.Min(q => q.x), x1 = poly.Max(q => q.x), z0 = poly.Min(q => q.y), z1 = poly.Max(q => q.y);
                var go = new GameObject("traffic_" + (string)d["id"]);
                go.transform.SetParent(markers.transform, false);
                float y = h.Sample((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
                go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f);
                var tz = go.AddComponent<TrafficZone>();
                tz.size = new Vector3(x1 - x0, 260f, z1 - z0);
                tz.district = (string)d["id"];
                tz.density = (string)d["id"] == "timeline" ? 1.6f : (string)d["id"] == "mountain" ? 0.3f : 0.8f;
            }
            // venue root, world, light, render settings
            var timeline = VenueCatalog.All.First(s => s.id == "timeline_square");
            var tlPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(timeline.PrefabPath);
            var vr = root.AddComponent<VenueSceneRoot>();
            EditorUtility.CopySerialized(tlPrefab.GetComponent<VenueSceneRoot>(), vr);
            vr.venueId = IslandWorld.VenueId; vr.districtId = "timeline"; vr.navigation = null;
            vr.cameraBounds = new Rect(h.MinX + 100f, h.MinZ + 100f, h.Size - 200f, h.Size - 200f);
            var world = root.AddComponent<IslandWorld>();
            world.navigation = nav;
            world.roads = net;
            world.venues = venues;
            world.cells = cells;
            world.seaLevel = h.Sea;
            world.flightCeiling = h.FlightCeiling;
            world.heightBytes = hfAssets.height; world.holeBytes = hfAssets.holes;
            world.hfSamples = h.N; world.hfCell = h.Cell; world.hfMinX = h.MinX; world.hfMinZ = h.MinZ; world.hfMin = h.HMin; world.hfMax = h.HMax;
            world.districts = ((JArray)plan["districts"]).Select(d => new IslandWorld.District
            {
                id = (string)d["id"], name = (string)d["name"], center = IslandV2Paths.V2(d["label"]),
                polygon = ((JArray)d["poly"]).Select(IslandV2Paths.V2).ToArray(),
            }).ToList();
            var atm = root.AddComponent<IslandAtmosphere>();
            atm.skybox = Skybox();
            root.AddComponent<IslandRenderSettings>();
            root.AddComponent<IslandInstanceRenderer>();
            var birds = root.AddComponent<IslandBirds>();
            birds.material = mats.TryGetValue("bird", out var bm) ? bm : null;
            birds.flocks = new[]
            {
                new IslandBirds.Flock { center = new Vector3(480f, 22f, -390f), radius = 60f, count = 9 },     // the marina
                new IslandBirds.Flock { center = new Vector3(0f, 26f, -300f), radius = 95f, count = 12 },      // the bay
                new IslandBirds.Flock { center = new Vector3(-420f, 18f, -380f), radius = 45f, count = 7 },    // the harbour
                new IslandBirds.Flock { center = new Vector3(760f, 30f, -560f), radius = 70f, count = 6 },     // the lighthouse
            };
            // the island's own look (golden hour grade, bloom, vignette; cool shadows, warm highlights)
            var lookGo = new GameObject("Look");
            lookGo.transform.SetParent(root.transform, false);
            var vol = lookGo.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 5f; vol.weight = 1f;
            vol.sharedProfile = LookProfile();
            // save meshes, then the prefab
            if (AssetDatabase.LoadAssetAtPath<Object>(basePath) != null) AssetDatabase.DeleteAsset(basePath);
            if (meshes.Count > 0)
            {
                AssetDatabase.CreateAsset(meshes[0], basePath);
                for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], basePath);
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, IslandV2Paths.BasePrefab);
            Object.DestroyImmediate(root);
            // the far references are scene objects inside the prefab: re-link them on the saved asset
            var pw = prefab.GetComponent<IslandWorld>();
            foreach (var c in pw.cells)
            {
                var t = prefab.transform.Find("Far/" + c.id + "_far");
                c.far = t != null ? t.gameObject : null;
            }
            EditorUtility.SetDirty(prefab);
            PrefabUtility.SavePrefabAsset(prefab);
            receipt["prefab"] = IslandV2Paths.BasePrefab;
            receipt["helipads"] = pads;
        }

        /// <summary>The heightfield and the packed tunnel-portal holes as TextAssets the island loads at runtime.</summary>
        private static (TextAsset height, TextAsset holes) HeightfieldAssets(IslandHeightfield h)
        {
            Directory.CreateDirectory(IslandV2Paths.Data);
            var hp = IslandV2Paths.Data + "/heightfield.bytes";
            var op = IslandV2Paths.Data + "/heightfield_holes.bytes";
            File.Copy(Path.Combine(IslandV2Paths.Out, "height.r16"), hp, true);
            File.WriteAllBytes(op, IslandHeightfield.PackHoles(h.Hole ?? new bool[h.N * h.N]));
            AssetDatabase.ImportAsset(hp, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(op, ImportAssetOptions.ForceSynchronousImport);
            return (AssetDatabase.LoadAssetAtPath<TextAsset>(hp), AssetDatabase.LoadAssetAtPath<TextAsset>(op));
        }

        private static void Disc(MeshBuilder b, Vector3 c, float r, float y, bool planarUv = false)
        {
            Vector2 Uv(float x, float z) => planarUv ? new Vector2(0.5f + x / (2f * r), 0.5f + z / (2f * r)) : Vector2.zero;
            int ci = b.Add(c + Vector3.up * y, Vector3.up, Uv(0f, 0f));
            const int S = 32;
            int b0 = b.Count;
            for (int i = 0; i <= S; i++)
            {
                float a = -i / (float)S * Mathf.PI * 2f;
                b.Add(c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), Vector3.up, Uv(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
            }
            for (int i = 0; i < S; i++) b.Tri(ci, b0 + i, b0 + i + 1);
        }

        private static VolumeProfile LookProfile()
        {
            var path = IslandV2Paths.Root + "/IslandLook.asset";
            var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "IslandLook";
            AssetDatabase.CreateAsset(profile, path);
            var tm = profile.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var wb = profile.Add<WhiteBalance>(true); wb.temperature.Override(6f); wb.tint.Override(2f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.08f); ca.contrast.Override(12f); ca.saturation.Override(14f); ca.colorFilter.Override(new Color(1f, 0.975f, 0.94f));
            var st = profile.Add<SplitToning>(true);
            st.shadows.Override(new Color(0.42f, 0.48f, 0.62f)); st.highlights.Override(new Color(0.64f, 0.56f, 0.44f)); st.balance.Override(-12f);
            var bl = profile.Add<Bloom>(true);
            bl.threshold.Override(1.0f); bl.intensity.Override(0.28f); bl.scatter.Override(0.6f); bl.highQualityFiltering.Override(false);
            bl.downscale.Override(BloomDownscaleMode.Quarter);
            var vg = profile.Add<Vignette>(true); vg.intensity.Override(0.18f); vg.smoothness.Override(0.42f);
            foreach (var c in profile.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, profile); }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        private static Material Skybox()
        {
            var path = IslandV2Paths.Materials + "/sky.mat";
            var sh = Shader.Find("CT Life/World/Sky");
            if (sh == null) return null;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(sh) { name = "sky" }; AssetDatabase.CreateAsset(mat, path); }
            mat.shader = sh;
            mat.SetTexture("_Clouds", Tex("clouds.png"));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ utils

        private static T Save<T>(T asset, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                return existing;
            }
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
