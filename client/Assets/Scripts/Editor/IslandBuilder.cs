using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.EditorTools.VenueBuild;
using CTLife.UI.Map;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Phase B (open world, 2026-10-17): builds the walkable CT Island from the SAME layout and terrain functions as the painted
    /// atlas (AtlasBaker plots and placements, AtlasPainter coast/hills/mountains/canal), so the baked maps and the minimap stay
    /// truthful, and so a later top-down bake of this 3D island can replace the hand-painted 2D atlas.
    ///  - Ground: chunked meshes (28 m, unlit, MeshColliders; replaced Unity Terrain, 23.8 ms GPU on the Seeker): flat town inside the ring road (venue frames sunk 12 cm so streamed dioramas sit flush), beaches sloping
    ///    into the sea, the canal carved except under the road bridges, real hills and mountains on the north and north-east.
    ///  - Ground texture: the painted atlas with every baked venue map composited in (the far-district impostor for free).
    ///  - One island NavMesh: each venue's own baked walkable surface (its triangulation, so seats and aisles stay exactly as
    ///    tuned), a generated street ground outside the venue frames, and a 2.4 m door corridor from every exit to the street.
    ///    Steep terrain and the sea are not walkable; the NavMesh is the coast wall.
    ///  - Prefab Resources/World/Island/CTIsland: ground chunks, water, island VenueSceneRoot, IslandWorld, Timeline's look.
    /// Batch: Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.IslandBuilder.Run
    /// </summary>
    public static class IslandBuilder
    {
        public const string Folder = "Assets/World/Island";
        public const string PrefabPath = "Assets/Resources/World/Island/CTIsland.prefab";
        public const int HeightRes = 513, GroundRes = 2048;
        public const float Half = AtlasBaker.Half, Base = -6f, Top = 40f, Water = -0.35f;
        public const float CorridorWidth = 2.4f;

        public static void Run()
        {
            string fatal = null;
            var receipt = new JObject();
            try { Build(receipt); }
            catch (Exception e) { fatal = e.GetType().Name + ": " + e.Message; Debug.LogException(e); }
            receipt["fatal"] = fatal;
            var outDir = Arg("-ctlife-captureOut");
            if (outDir != null) { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, "island-build-receipt.json"), receipt.ToString()); }
            Debug.Log($"CTLIFE [island] {(fatal == null ? "PASS" : "FAIL " + fatal)}");
            if (Application.isBatchMode) EditorApplication.Exit(fatal == null ? 0 : 1);
        }

        // ------------------------------------------------------------------ layout

        public sealed class Venue { public string Id; public Rect Frame; public Rect Walk; public Vector2 Origin; public Vector2 Exit; public VenueSceneRoot Prefab; }

        /// <summary>Every public venue placed on the island (homes stay private visits; their lots are painted only).</summary>
        public static List<Venue> Venues()
        {
            MapCatalog.Reset();
            var list = new List<Venue>();
            foreach (var p in MapCatalog.Atlas.Placements.Values)
            {
                if (!MapCatalog.Areas.TryGetValue(p.AreaId, out var a) || a.IsHome) continue;
                var spots = VenueSpotsFile.Load(a.VenueId);
                var exit = spots?.spots.Values.FirstOrDefault(s => s.kind == "exit" && (s.id == "exit" || s.id.EndsWith(".exit")));
                var entry = AssetDatabase.FindAssets("t:VenueContentEntry").Select(g => AssetDatabase.LoadAssetAtPath<VenueContentEntry>(AssetDatabase.GUIDToAssetPath(g)))
                    .FirstOrDefault(e => e != null && !e.isHome && e.contentId == a.VenueId);
                if (entry == null || entry.prefab == null) continue;
                list.Add(new Venue
                {
                    Id = a.VenueId, Frame = MapSurface.AreaAtlasRect(a, p), Origin = p.Origin, Prefab = entry.prefab,
                    Walk = new Rect(p.Origin + a.Walkable.min, a.Walkable.size),
                    Exit = p.Origin + (exit != null ? new Vector2(exit.x, exit.z) : new Vector2(a.Walkable.center.x, a.Walkable.yMin + 0.6f)),
                });
            }
            return list;
        }

        // ------------------------------------------------------------------ terrain height (metres, world y)

        private static float RectDist(Vector2 p, Rect r) => AtlasPainter.RoundRect(p - r.center, r.size / 2f, 0.01f);

        /// <summary>Ground height of the island at atlas point <paramref name="p"/>.</summary>
        public static float GroundHeight(Vector2 p, IReadOnlyList<Rect> frames)
        {
            float c = AtlasPainter.Coast(p);
            if (c > 0f) return Mathf.Lerp(-0.6f, -5.5f, Mathf.Clamp01(c / 45f));      // seabed
            float inland = -c;
            float k = 0f;
            foreach (var plot in AtlasBaker.Plots) k = Mathf.Max(k, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(14f, -8f, AtlasPainter.PlotDist(p, plot))));
            float h = AtlasPainter.Height(p, c, k);
            // Flat town: everything inside the ring boulevard (plus a verge) is level; the countryside rises outside it.
            float ring = AtlasPainter.RoundRect(p, new Vector2(216f, 216f), 64f);
            h *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 34f, ring));
            // Beaches: from the water line up to the land over 14 m.
            if (inland < 14f) h = Mathf.Lerp(Mathf.Lerp(-0.6f, 0.15f, inland / 14f), h, Mathf.SmoothStep(9f, 14f, inland));
            // The canal (bridges where the roads cross keep the ground).
            float cd = AtlasPainter.CanalDist(p, out float along);
            float hw = AtlasPainter.CanalHalfWidth(along);
            if (cd < hw + 1.5f && AtlasPainter.RoadDist(p, out _, out _) > 6.2f)
                h = Mathf.Lerp(h, -1.6f, Mathf.SmoothStep(hw + 1.5f, hw - 0.5f, cd));
            // Venue frames: 12 cm below the street so each diorama's own ground sits flush on top.
            if (frames != null)
                foreach (var f in frames)
                {
                    float d = RectDist(p, f);
                    if (d < 0.8f) h = Mathf.Lerp(-0.12f, h, Mathf.SmoothStep(-0.8f, 0.8f, d));
                }
            return h;
        }

        // ------------------------------------------------------------------ build

        public static void Build(JObject receipt)
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var venues = Venues();
            var frames = venues.Select(v => v.Frame).ToList();
            receipt["venues"] = venues.Count;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Terrain.

            var heights = new float[HeightRes, HeightRes];
            float step = 2f * Half / (HeightRes - 1);
            float maxH = float.MinValue, minH = float.MaxValue;
            for (int y = 0; y < HeightRes; y++)
                for (int x = 0; x < HeightRes; x++)
                {
                    var p = new Vector2(-Half + x * step, -Half + y * step);
                    float h = GroundHeight(p, frames);
                    maxH = Mathf.Max(maxH, h); minH = Mathf.Min(minH, h);
                    heights[y, x] = Mathf.Clamp01((h - Base) / (Top - Base));
                }

            receipt["heightRange"] = new JArray(Math.Round(minH, 2), Math.Round(maxH, 2));
            var ground = GroundTexture();


            var root = new GameObject("CTIsland");
            SceneManager_Move(root, scene);
            // Ground: chunked meshes from the same heightfield (frustum-culled per chunk; unlit, the painted texture carries the
            // light). The Seeker spent 23.8 ms of GPU on the URP Terrain shader over the whole screen.
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "IslandGround" };
            groundMat.SetTexture("_BaseMap", ground);
            groundMat.SetColor("_BaseColor", Color.white);
            groundMat = Save(groundMat, Folder + "/IslandGround.mat");
            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(root.transform, false);
            int chunks = BuildGroundChunks(groundGo.transform, heights, groundMat);
            receipt["groundChunks"] = chunks;
            // Water.
            var water = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.name = "Sea";
            water.transform.SetParent(root.transform, false);
            water.transform.SetPositionAndRotation(new Vector3(0f, Water, 0f), Quaternion.Euler(90f, 0f, 0f));
            water.transform.localScale = new Vector3(2f * Half + 400f, 2f * Half + 400f, 1f);
            var wm = new Material(Shader.Find("Universal Render Pipeline/Simple Lit")) { name = "IslandSea" };
            ColorUtility.TryParseHtmlString("#3e98cc", out var sea);
            wm.SetColor("_BaseColor", sea);
            wm.SetFloat("_Smoothness", 0.6f);
            wm = Save(wm, Folder + "/IslandSea.mat");
            water.GetComponent<MeshRenderer>().sharedMaterial = wm;
            water.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // The island as a venue root (camera framing and look from Timeline Square), and the streamer.
            var timeline = VenueCatalog.All.First(s => s.id == "timeline_square");
            var tlPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(timeline.PrefabPath);
            var vr = root.AddComponent<VenueSceneRoot>();
            var tlRoot = tlPrefab.GetComponent<VenueSceneRoot>();
            EditorUtility.CopySerialized(tlRoot, vr);
            vr.venueId = IslandWorld.VenueId; vr.districtId = "timeline"; vr.navigation = null;
            vr.cameraBounds = new Rect(-Half + 20f, -Half + 20f, 2f * Half - 40f, 2f * Half - 40f);
            foreach (var t in new[] { typeof(VenueContentLighting), typeof(VenueLook) })
            {
                var src = tlPrefab.GetComponent(t);
                if (src == null) continue;
                var dst = root.AddComponent(t);
                EditorUtility.CopySerialized(src, dst);
            }
            var look = root.GetComponent<VenueLook>();
            if (look != null) look.lightProbes = null;   // Timeline's probes describe the square, not the island
            var volume = tlPrefab.GetComponentInChildren<Volume>(true);
            if (volume != null)
            {
                var v = Object.Instantiate(volume.gameObject, root.transform);
                v.name = "Look";
            }

            // NavMesh.
            var nav = BuildNavMesh(venues, frames, receipt);
            nav = Save(nav, Folder + "/CTIsland_NavMesh.asset");
            var world = root.AddComponent<IslandWorld>();
            world.navigation = nav;
            world.venues = venues.Select(v => new IslandVenue
            {
                venueId = v.Id, origin = v.Origin, frame = v.Frame, walk = v.Walk, exit = v.Exit,
                district = AtlasBaker.Plots.FirstOrDefault(pl => pl.venues.Contains(v.Id))?.id,
            }).ToList();
            world.districts = AtlasBaker.Plots.Select(pl => new IslandWorld.District { id = pl.id, name = pl.name, center = pl.Center, half = AtlasBaker.Plot / 2f + AtlasBaker.Street / 2f }).ToList();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            receipt["prefab"] = PrefabPath;
            receipt["navMeshBytes"] = new FileInfo(Folder + "/CTIsland_NavMesh.asset").Length;
            receipt["connectivity"] = Connectivity(nav, venues);
            receipt["groundBytes"] = Directory.GetFiles(Folder + "/Ground", "*.asset").Sum(f => new FileInfo(f).Length);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        public const int ChunkCells = 16, Stride = 2;   // every 2nd height sample (2.34 m), 16 cells = 37 m chunks

        /// <summary>Mesh chunks over the heightfield, each with a MeshCollider; uv = the island texture.</summary>
        private static int BuildGroundChunks(Transform parent, float[,] heights, Material mat)
        {
            string dir = Folder + "/Ground";
            if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
            AssetDatabase.CreateFolder(Folder, "Ground");
            int full = HeightRes - 1, cells = full / Stride, count = 0;
            float step = 2f * Half / cells;
            for (int cy = 0; cy < cells; cy += ChunkCells)
                for (int cx = 0; cx < cells; cx += ChunkCells)
                {
                    int w = Mathf.Min(ChunkCells, cells - cx), h = Mathf.Min(ChunkCells, cells - cy);
                    var v = new Vector3[(w + 1) * (h + 1)]; var uv = new Vector2[v.Length]; var t = new int[w * h * 6];
                    bool anyLand = false;
                    for (int y = 0; y <= h; y++)
                        for (int x = 0; x <= w; x++)
                        {
                            int gx = cx + x, gy = cy + y;
                            float hy = Base + heights[gy * Stride, gx * Stride] * (Top - Base);
                            if (hy > Water - 1f) anyLand = true;
                            v[y * (w + 1) + x] = new Vector3(-Half + gx * step, hy, -Half + gy * step);
                            uv[y * (w + 1) + x] = new Vector2(gx / (float)cells, gy / (float)cells);
                        }
                    if (!anyLand) continue;   // deep sea: the water plane covers it
                    int k = 0;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int a = y * (w + 1) + x, b = a + 1, c = a + w + 1, d = c + 1;
                            t[k++] = a; t[k++] = c; t[k++] = b; t[k++] = b; t[k++] = c; t[k++] = d;
                        }
                    var m = new Mesh { name = $"ground_{cx}_{cy}" };
                    m.vertices = v; m.uv = uv; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
                    AssetDatabase.CreateAsset(m, $"{dir}/ground_{cx}_{cy}.asset");
                    var go = new GameObject(m.name);
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                    r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    go.AddComponent<MeshCollider>().sharedMesh = m;
                    go.isStatic = true;
                    count++;
                }
            return count;
        }

        private static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene) => UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

        /// <summary>Writes <paramref name="asset"/> to <paramref name="path"/> and returns the PERSISTENT object to reference.</summary>
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

        /// <summary>The painted atlas with every baked venue map composited at its placement (2048 px over the island).</summary>
        private static Texture2D GroundTexture()
        {
            string path = Folder + "/IslandGround.png";
            var atlasTex = new Texture2D(2, 2); atlasTex.LoadImage(File.ReadAllBytes(AtlasBaker.AtlasPng));
            var px = new Color[GroundRes * GroundRes];
            float mpp = 2f * Half / GroundRes;
            for (int y = 0; y < GroundRes; y++)
                for (int x = 0; x < GroundRes; x++)
                    px[y * GroundRes + x] = atlasTex.GetPixelBilinear((x + 0.5f) / GroundRes, (y + 0.5f) / GroundRes);
            foreach (var p in MapCatalog.Atlas.Placements.Values)
            {
                if (!MapCatalog.Areas.TryGetValue(p.AreaId, out var a)) continue;
                string file = MapBaker.AreasFolder + "/" + a.Id + ".png";
                if (!File.Exists(file)) continue;
                var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(file));
                var r = MapSurface.AreaAtlasRect(a, p);
                int x0 = Mathf.FloorToInt((r.xMin + Half) / mpp), x1 = Mathf.CeilToInt((r.xMax + Half) / mpp);
                int y0 = Mathf.FloorToInt((r.yMin + Half) / mpp), y1 = Mathf.CeilToInt((r.yMax + Half) / mpp);
                for (int y = Mathf.Max(0, y0); y < Mathf.Min(GroundRes, y1); y++)
                    for (int x = Mathf.Max(0, x0); x < Mathf.Min(GroundRes, x1); x++)
                    {
                        var c = t.GetPixelBilinear(((x + 0.5f) * mpp - Half - r.xMin) / r.width, ((y + 0.5f) * mpp - Half - r.yMin) / r.height);
                        int i = y * GroundRes + x;
                        px[i] = Color.Lerp(px[i], c, c.a);
                    }
                Object.DestroyImmediate(t);
            }
            Object.DestroyImmediate(atlasTex);
            var o = new Texture2D(GroundRes, GroundRes, TextureFormat.RGB24, false);
            o.SetPixels(px); o.Apply();
            File.WriteAllBytes(path, o.EncodeToPNG());
            Object.DestroyImmediate(o);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.maxTextureSize = GroundRes; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.sRGBTexture = true;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true; android.maxTextureSize = GroundRes; android.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ navigation

        private static NavMeshData BuildNavMesh(List<Venue> venues, List<Rect> frames, JObject receipt)
        {
            var sources = new List<NavMeshBuildSource>();
            // 1) Each venue's own walkable surface: add its baked NavMesh at the island offset and take the triangulation.
            NavMesh.RemoveAllNavMeshData();
            var instances = new List<NavMeshDataInstance>();
            foreach (var v in venues)
                if (v.Prefab.navigation != null)
                    instances.Add(NavMesh.AddNavMeshData(v.Prefab.navigation, new Vector3(v.Origin.x, 0f, v.Origin.y), Quaternion.identity));
            var tri = NavMesh.CalculateTriangulation();
            foreach (var i in instances) NavMesh.RemoveNavMeshData(i);
            var venueMesh = new Mesh { name = "island-venue-walkable", indexFormat = IndexFormat.UInt32 };
            venueMesh.SetVertices(tri.vertices); venueMesh.SetTriangles(tri.indices, 0);
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = venueMesh, transform = Matrix4x4.identity, area = 0 });
            receipt["venueWalkableTriangles"] = tri.indices.Length / 3;

            // 2) Street ground: a 2 m grid of the terrain surface outside the venue frames, above the water line.
            var verts = new List<Vector3>(); var tris = new List<int>();
            const float G = 2f;
            int n = Mathf.RoundToInt(2f * Half / G);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    var c = new Vector2(-Half + (i + 0.5f) * G, -Half + (j + 0.5f) * G);
                    if (frames.Any(f => RectDist(c, f) < 0.4f)) continue;
                    var p00 = new Vector2(-Half + i * G, -Half + j * G);
                    float h00 = GroundHeight(p00, frames), h10 = GroundHeight(p00 + new Vector2(G, 0), frames),
                          h01 = GroundHeight(p00 + new Vector2(0, G), frames), h11 = GroundHeight(p00 + new Vector2(G, G), frames);
                    if (Mathf.Min(Mathf.Min(h00, h10), Mathf.Min(h01, h11)) < Water + 0.05f) continue;
                    int b = verts.Count;
                    verts.Add(new Vector3(p00.x, h00, p00.y)); verts.Add(new Vector3(p00.x + G, h10, p00.y));
                    verts.Add(new Vector3(p00.x, h01, p00.y + G)); verts.Add(new Vector3(p00.x + G, h11, p00.y + G));
                    tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            var groundMesh = new Mesh { name = "island-street-ground", indexFormat = IndexFormat.UInt32 };
            groundMesh.SetVertices(verts); groundMesh.SetTriangles(tris, 0);
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = groundMesh, transform = Matrix4x4.identity, area = 0 });
            receipt["streetQuads"] = tris.Count / 6;

            // 3) Door corridors: from just inside each venue's exit, straight south past its frame onto the street.
            int corridors = 0;
            foreach (var v in venues)
            {
                float zIn = Mathf.Max(v.Exit.y, v.Walk.yMin) + 2.5f, zOut = v.Frame.yMin - 1.2f;   // well into the venue floor
                var size = new Vector3(CorridorWidth, 0.05f, zIn - zOut);
                var m = Matrix4x4.TRS(new Vector3(v.Exit.x, -0.02f, (zIn + zOut) / 2f), Quaternion.identity, Vector3.one);
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = size, transform = m, area = 0 });
                corridors++;
            }
            receipt["doorCorridors"] = corridors;

            var settings = NavMesh.GetSettingsByID(0);
            // The venue surfaces are already eroded by the venue bake; a small radius keeps their aisles and seats intact.
            settings.agentRadius = 0.08f;
            settings.overrideVoxelSize = true; settings.voxelSize = 0.08f;
            settings.overrideTileSize = true; settings.tileSize = 256;
            var bounds = new Bounds(new Vector3(0f, (Top + Base) / 2f, 0f), new Vector3(2f * Half, Top - Base + 4f, 2f * Half));
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null) throw new InvalidOperationException("island NavMesh build returned null");
            data.name = "CTIsland_NavMesh";
            Object.DestroyImmediate(venueMesh); Object.DestroyImmediate(groundMesh);
            return data;
        }

        /// <summary>Every venue's entrance reachable on foot from Timeline Square's (the island is one walkable world).</summary>
        public static JObject Connectivity(NavMeshData nav, List<Venue> venues)
        {
            NavMesh.RemoveAllNavMeshData();
            var inst = NavMesh.AddNavMeshData(nav);
            var o = new JObject();
            try
            {
                Vector3 Entrance(Venue v)
                {
                    var spots = VenueSpotsFile.Load(v.Id);
                    var e = spots?.spots.Values.FirstOrDefault(s => s.id == "entrance" || s.id.EndsWith(".entrance"));
                    var local = e != null ? new Vector2(e.x, e.z + 2.5f) : v.Walk.center - v.Origin;
                    return new Vector3(v.Origin.x + local.x, 0.1f, v.Origin.y + local.y);
                }
                var from = venues.First(v => v.Id == "timeline_square");
                if (!NavMesh.SamplePosition(Entrance(from), out var start, 1f, NavMesh.AllAreas)) { o["error"] = "timeline entrance off the island NavMesh"; return o; }
                int ok = 0;
                foreach (var v in venues)
                {
                    bool on = NavMesh.SamplePosition(Entrance(v), out var hit, 2.5f, NavMesh.AllAreas);
                    var path = new NavMeshPath();
                    bool reach = on && NavMesh.CalculatePath(start.position, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    float len = 0f; for (int i = 1; reach && i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                    o[v.Id] = reach ? $"walk {len:0} m" : on ? "UNREACHABLE" : "OFF NAVMESH";
                    if (reach) ok++;
                }
                o["reachable"] = $"{ok}/{venues.Count}";
                var dbg = Arg("-ctlife-captureOut");
                if (dbg != null) CoverageMap(Path.Combine(dbg, "island-navmesh-timeline.png"), new Rect(-75f, -60f, 150f, 120f), 0.25f, start.position);
                var sp = venues.FirstOrDefault(v => v.Id == "checker_kiosk");
                if (dbg != null && sp != null) CoverageMap(Path.Combine(dbg, "island-navmesh-spaces-door.png"), new Rect(sp.Frame.xMin - 2f, sp.Frame.yMin - 4f, sp.Frame.width + 4f, sp.Frame.height + 6f), 0.1f, start.position);
            }
            finally { NavMesh.RemoveNavMeshData(inst); }
            return o;
        }

        /// <summary>Debug raster: green = on the NavMesh and reachable from <paramref name="from"/>, red = on it but not reachable.</summary>
        private static void CoverageMap(string path, Rect r, float res, Vector3 from)
        {
            int W = Mathf.RoundToInt(r.width / res), H = Mathf.RoundToInt(r.height / res);
            var px = new Color[W * H];
            var nav = NavMesh.CalculateTriangulation();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = new Vector3(r.xMin + (x + 0.5f) * res, 0.2f, r.yMin + (y + 0.5f) * res);
                    var c = new Color(0.15f, 0.15f, 0.2f);
                    if (NavMesh.SamplePosition(p, out var hit, 0.6f, NavMesh.AllAreas) && new Vector2(hit.position.x - p.x, hit.position.z - p.z).magnitude < res)
                        c = new Color(0.3f, 0.85f, 0.4f);
                    px[y * W + x] = c;
                }
            // Reachability as a flood from 'from' is costly; mark only the polygons' islands by a coarse path test every 2 m.
            for (int y = 0; y < H; y += 8)
                for (int x = 0; x < W; x += 8)
                {
                    if (px[y * W + x].g < 0.5f) continue;
                    var p = new Vector3(r.xMin + (x + 0.5f) * res, 0.2f, r.yMin + (y + 0.5f) * res);
                    if (!NavMesh.SamplePosition(p, out var hit, 0.6f, NavMesh.AllAreas)) continue;
                    var np = new NavMeshPath();
                    bool reach = NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, np) && np.status == NavMeshPathStatus.PathComplete;
                    if (!reach) for (int dy = 0; dy < 8 && y + dy < H; dy++) for (int dx = 0; dx < 8 && x + dx < W; dx++)
                        if (px[(y + dy) * W + x + dx].g > 0.5f) px[(y + dy) * W + x + dx] = new Color(0.9f, 0.3f, 0.3f);
                }
            MapBaker.SavePng(px, W, H, path);
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
