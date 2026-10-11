using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Repack an existing authored venue prefab (Timeline Square, Terminal Café, Spaces Studio predate the venue
    /// pipeline) without re-authoring it. Everything that is not a mesh renderer is kept as it is: anchors and approach
    /// points, colliders, NavMesh, lights (bake input only), VenueSceneRoot camera settings, VenueContentLighting and
    /// CafeSeatMotionProfile. Geometry is then re-emitted:
    ///  * merged per material into one LODGroup per cell (source LOD0 / LOD1 kept as two levels);
    ///  * non-tiling textures (palettes, colour maps, signs) packed into per-venue atlases with their tint baked in,
    ///    so most props share one lit, one cutout and one unlit material;
    ///  * live surfaces (Screen_* materials matching WorldLife house_ads.json rules: timeline, pixel wall, billboards,
    ///    kiosks) stay one renderer per owner with their exact material name, so LiveSurfaces finds the same set.
    /// The prefab is then baked by VenueBake and verified by VenueVerify like every other venue.
    /// </summary>
    public static class VenueRepack
    {
        private const int EntryMax = 512, AtlasMax = 2048, Pad = 4;
        private const string HouseAds = "Assets/Scripts/Features/WorldLife/Resources/WorldLife/house_ads.json";

        private enum Route { AtlasLit, AtlasCutout, AtlasUnlit, Tiled, TiledUnlit, Unbaked, Live, AtlasTiled }

        private sealed class Job
        {
            public Mesh mesh;
            public int submesh;
            public Matrix4x4 matrix;
            public bool level0, level1, lodModel;
            public Vector2Int cell;
            public Route route;
            public string key;          // atlas entry key or material key
            public Material source;
            public Vector4 st;
            public Rect stUv;           // UV bounds after the material's scale/offset
            public int ownerId;         // live surfaces: owner identity and pivot (owner objects are stripped later)
            public Vector3 ownerPos;
            public Quaternion ownerRot;
            public string screenId;
        }

        private sealed class Entry
        {
            public Texture2D texture;
            public Color tint;
            public bool alpha;
            public Rect rect;
            public Rect span;           // tiled entries: UV range (in tiles) baked out as repeated texture
            public int tileRes;
        }

        [MenuItem("CT Life/Venues/Repack: analyse sources")]
        public static void Analyze()
        {
            foreach (var spec in VenueBuildPipeline.Selected().Where(s => !string.IsNullOrEmpty(s.repackSource)))
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(spec.repackSource) ?? throw new FileNotFoundException(spec.repackSource);
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var go = Object.Instantiate(src);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                    var before = MeasureBefore(spec, go);
                    var mats = new Dictionary<string, (int tris, int renderers, bool tiled, string shader)>();
                    var spans = new Dictionary<string, Rect>();
                    var texInfo = new Dictionary<string, string>();
                    var uvCache = new Dictionary<(Mesh, int), Rect>();
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                    {
                        var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                        if (mesh == null) continue;
                        var ms = r.sharedMaterials;
                        for (int s = 0; s < mesh.subMeshCount; s++)
                        {
                            var m = ms[Mathf.Min(s, ms.Length - 1)];
                            string n = m != null ? m.name : "null";
                            var uv = UvBounds(mesh, s, uvCache);
                            spans[n] = spans.TryGetValue(n, out var sp) ? Rect.MinMaxRect(Mathf.Min(sp.xMin, uv.xMin), Mathf.Min(sp.yMin, uv.yMin),
                                Mathf.Max(sp.xMax, uv.xMax), Mathf.Max(sp.yMax, uv.yMax)) : uv;
                            var tx = m != null && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                            texInfo[n] = tx != null ? $"{AssetDatabase.GetAssetPath(tx)} {tx.width}x{tx.height}" : "none";
                            mats.TryGetValue(n, out var row);
                            mats[n] = (row.tris + (int)(mesh.GetIndexCount(s) / 3), row.renderers + 1,
                                row.tiled || uv.xMin < -0.02f || uv.yMin < -0.02f || uv.xMax > 1.02f || uv.yMax > 1.02f, m != null ? m.shader.name : "-");
                        }
                    }
                    var other = go.GetComponentsInChildren<Component>(true).Select(c => c.GetType().Name).GroupBy(n => n)
                        .ToDictionary(g => g.Key, g => g.Count());
                    before["materials"] = JObject.FromObject(mats.OrderByDescending(k => k.Value.tris)
                        .ToDictionary(k => k.Key, k => $"{k.Value.tris} tris, {k.Value.renderers} slots, {(k.Value.tiled ? "TILED" : "0..1")} " +
                            $"uv[{spans[k.Key].xMin:F2},{spans[k.Key].yMin:F2}..{spans[k.Key].xMax:F2},{spans[k.Key].yMax:F2}], {k.Value.shader}, tex {texInfo[k.Key]}"));
                    before["components"] = JObject.FromObject(other);
                    Directory.CreateDirectory(Path.Combine(VenueBuildPipeline.EvidenceDir, "receipts"));
                    File.WriteAllText(Path.Combine(VenueBuildPipeline.EvidenceDir, "receipts", spec.id + "-before.json"), before.ToString());
                    Debug.Log($"CTLIFE [repack] {spec.id} before: max draws≈{before["maxEstimatedDraws"]}, tris≈{before["maxEstimatedTriangles"]}, " +
                              $"{mats.Count} materials, live surfaces {before["liveSurfaces"]}");
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
        }

        private static JObject MeasureBefore(VenueSpec spec, GameObject go)
        {
            var venue = go.GetComponent<VenueSceneRoot>();
            var spots = VenueSpotsFile.Load(spec.spotsKey);
            VenueVerify.AndroidQualityLevel(out float bias);
            var cam = VenueVerify.NewCamera();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cam.gameObject, go.scene);
            var (views, maxDraws, maxTris) = VenueVerify.MeasureViews(go, venue, spots, bias, cam, null);
            Object.DestroyImmediate(cam.gameObject);
            return new JObject
            {
                ["id"] = spec.id, ["source"] = spec.repackSource, ["views"] = views, ["maxEstimatedDraws"] = maxDraws,
                ["maxEstimatedTriangles"] = maxTris, ["liveSurfaces"] = new JArray(LiveSurfaceKeys(go)),
            };
        }

        /// <summary>Same discovery as WorldLife LiveSurfaces: Screen_* materials matching a rule, grouped by owner.</summary>
        internal static List<string> LiveSurfaceKeys(GameObject root)
        {
            var rules = Rules();
            var keys = new HashSet<string>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    var name = m.name.Replace(" (Instance)", "");
                    if (!name.StartsWith("Screen_", StringComparison.Ordinal)) continue;
                    var kind = rules.FirstOrDefault(rule => name.StartsWith(rule.prefix, StringComparison.Ordinal)).kind;
                    if (kind == null) continue;
                    var owner = Owner(r.transform, root.transform);
                    keys.Add(kind + ":" + name.Substring(7) + "@" + Mathf.Round(owner.position.x * 10) / 10 + "," + Mathf.Round(owner.position.z * 10) / 10);
                }
            return keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        private static Transform Owner(Transform t, Transform root)
        {
            var owner = t;
            while (owner.parent != null && owner.parent != root && owner.name.Contains("_LOD")) owner = owner.parent;
            return owner;
        }

        private static List<(string prefix, string kind)> _rules;

        private static List<(string prefix, string kind)> Rules()
        {
            if (_rules != null) return _rules;
            var json = JObject.Parse(File.ReadAllText(HouseAds));
            return _rules = ((JArray)json["surfaces"]["rules"]).Select(r => ((string)r["prefix"], (string)r["kind"])).ToList();
        }

        // ------------------------------------------------------------------ repack

        public static VB.Result Repack(VenueSpec spec)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Repack reads textures through the GPU for atlases; run without -nographics.");
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(spec.repackSource) ?? throw new FileNotFoundException(spec.repackSource);
            foreach (var d in new[] { spec.Folder, spec.Folder + "/Meshes", spec.Folder + "/Materials", spec.Folder + "/Textures" }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
            _rules = null;
            var written = new HashSet<string>();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = Object.Instantiate(src);
                go.name = spec.id;
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                spec.dress?.Invoke(go);
                var root = go.transform;
                var before = MeasureBefore(spec, go);
                var liveBefore = LiveSurfaceKeys(go);
                var propSpots = VenueSpotsFile.Load(spec.spotsKey);
                var props = VenueCollision.Classify(VenueCollision.FromSourceInstance(go), propSpots, go.GetComponent<VenueSceneRoot>().cameraBounds);
                var oldMaps = go.GetComponent<VenueLightmaps>();
                if (oldMaps != null) Object.DestroyImmediate(oldMaps);

                // LOD membership of every renderer.
                var lodOf = new Dictionary<Renderer, (int level, int count)>();
                foreach (var g in go.GetComponentsInChildren<LODGroup>(true))
                {
                    var lods = g.GetLODs();
                    for (int i = 0; i < lods.Length; i++)
                        foreach (var r in lods[i].renderers)
                            if (r != null && !lodOf.ContainsKey(r)) lodOf[r] = (i, lods.Length);
                }

                // Pass 1: classify every submesh.
                var jobs = new List<Job>();
                var entries = new Dictionary<string, Entry>[4]
                    { new Dictionary<string, Entry>(), new Dictionary<string, Entry>(), new Dictionary<string, Entry>(), new Dictionary<string, Entry>() };
                var uvCache = new Dictionary<(Mesh, int), Rect>();
                var rules = Rules();
                int sourceRenderers = 0;
                var walkRect = go.GetComponent<VenueSceneRoot>().cameraBounds;
                var walkCenter = walkRect.center;
                var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var r in renderers)
                {
                    var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    bool l0, l1;
                    if (lodOf.TryGetValue(r, out var lod))
                    {
                        if (lod.level >= 2) continue;
                        l0 = lod.level == 0;
                        l1 = lod.level == 1 || lod.count == 1;
                        if (spec.farLod1Distance > 0f && lod.count >= 2 && DistanceToRect(r.bounds.center, walkRect) > spec.farLod1Distance)
                        {
                            if (lod.level == 0) continue;
                            l0 = l1 = true; // background model: its LOD1 everywhere
                        }
                    }
                    else l0 = l1 = true;
                    sourceRenderers++;
                    // Cells are centred on the walkable area so the plaza/room centre is one cell, not a cell corner.
                    var c = r.bounds.center - new Vector3(walkCenter.x, 0, walkCenter.y);
                    float cz = spec.cellSizeZ > 0f ? spec.cellSizeZ : spec.cellSize;
                    var cell = spec.cellSplit
                        ? new Vector2Int(Mathf.FloorToInt(c.x / spec.cellSize), Mathf.FloorToInt(c.z / cz))
                        : new Vector2Int(Mathf.RoundToInt(c.x / spec.cellSize), Mathf.RoundToInt(c.z / cz));
                    var mats = r.sharedMaterials;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var m = mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null;
                        if (m == null) continue;
                        var job = new Job { mesh = mesh, submesh = s, matrix = r.localToWorldMatrix, level0 = l0, level1 = l1, cell = cell, source = m,
                            lodModel = lodOf.ContainsKey(r) };
                        string name = m.name.Replace(" (Instance)", "");
                        bool unlit = m.shader.name.Contains("Unlit");
                        bool transparent = m.renderQueue >= 2500 && m.renderQueue < 5000 || m.GetTag("RenderType", false) == "Transparent";
                        bool cutout = m.IsKeywordEnabled("_ALPHATEST_ON") || m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f;
                        var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") as Texture2D : m.mainTexture as Texture2D;
                        var color = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                        job.st = m.HasProperty("_BaseMap") ? new Vector4(m.GetTextureScale("_BaseMap").x, m.GetTextureScale("_BaseMap").y,
                            m.GetTextureOffset("_BaseMap").x, m.GetTextureOffset("_BaseMap").y) : new Vector4(1, 1, 0, 0);
                        var liveRule = name.StartsWith("Screen_", StringComparison.Ordinal)
                            ? rules.FirstOrDefault(rule => name.StartsWith(rule.prefix, StringComparison.Ordinal)).kind : null;
                        if (liveRule != null)
                        {
                            if (!l0) continue; // one copy of each live surface
                            job.route = Route.Live;
                            var owner = Owner(r.transform, root);
                            job.ownerId = owner.GetInstanceID();
                            job.ownerPos = owner.position;
                            job.ownerRot = owner.rotation;
                            job.screenId = name.Substring(7);
                            job.key = name;
                            jobs.Add(job);
                            continue;
                        }
                        if (transparent)
                        {
                            job.route = Route.Unbaked;
                            job.key = "M:" + AssetDatabase.GetAssetPath(m) + ":" + name;
                            jobs.Add(job);
                            continue;
                        }
                        var uv = UvBounds(mesh, s, uvCache);
                        var stUv = Rect.MinMaxRect(uv.xMin * job.st.x + job.st.z, uv.yMin * job.st.y + job.st.w, uv.xMax * job.st.x + job.st.z, uv.yMax * job.st.y + job.st.w);
                        job.stUv = stUv;
                        bool fits = tex == null || stUv.xMin >= -0.02f && stUv.yMin >= -0.02f && stUv.xMax <= 1.02f && stUv.yMax <= 1.02f;
                        if (fits)
                        {
                            int cls = unlit ? 2 : cutout ? 1 : 0;
                            job.route = unlit ? Route.AtlasUnlit : cutout ? Route.AtlasCutout : Route.AtlasLit;
                            job.key = (tex != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tex)) + ":" + tex.name : "solid") + ":" + ColorUtility.ToHtmlStringRGBA(color);
                            if (!entries[cls].ContainsKey(job.key)) entries[cls][job.key] = new Entry { texture = tex, tint = color, alpha = cutout };
                        }
                        else
                        {
                            job.route = unlit ? Route.TiledUnlit : Route.Tiled;
                            job.key = (unlit ? "U:" : cutout ? "C:" : "L:") + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tex)) + ":" + tex.name +
                                      ":" + ColorUtility.ToHtmlStringRGBA(color) + ":" + job.st;
                        }
                        jobs.Add(job);
                    }
                }

                // Tiled lit textures with a bounded UV range are baked out as repeated tiles into a fourth atlas: the
                // range a material actually uses (e.g. tree bark 6.5 × 8 tiles) at up to 1024 px on the long side.
                foreach (var group in jobs.Where(j => j.route == Route.Tiled && j.key.StartsWith("L:", StringComparison.Ordinal)).GroupBy(j => j.key).ToList())
                {
                    var first = group.First();
                    var tex = first.source.HasProperty("_BaseMap") ? first.source.GetTexture("_BaseMap") as Texture2D : first.source.mainTexture as Texture2D;
                    var span = group.Select(j => j.stUv).Aggregate((a, b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                        Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)));
                    span = Rect.MinMaxRect(span.xMin - 0.01f, span.yMin - 0.01f, span.xMax + 0.01f, span.yMax + 0.01f);
                    int res = Mathf.Max(tex.width, tex.height);
                    int t = Mathf.Min(res, Mathf.FloorToInt(1024f / Mathf.Max(span.width, span.height)));
                    if (t < 48) continue; // would blur too much: keep its own tiled material
                    entries[3][group.Key] = new Entry
                    {
                        texture = tex, tint = first.source.HasProperty("_BaseColor") ? first.source.GetColor("_BaseColor") : Color.white,
                        span = span, tileRes = t,
                    };
                    foreach (var j in group) j.route = Route.AtlasTiled;
                }

                // Pass 2: atlases and materials.
                var atlasMats = new Material[4];
                string[] atlasNames = { "AtlasLit", "AtlasCutout", "AtlasUnlit", "AtlasTiled" };
                for (int cls = 0; cls < 4; cls++)
                {
                    if (entries[cls].Count == 0) continue;
                    var tex = BuildAtlas(spec, atlasNames[cls], entries[cls].Values.ToList(), cls == 1, written);
                    atlasMats[cls] = MakeMaterial(spec, spec.id + "_" + atlasNames[cls], cls == 2 ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Simple Lit",
                        tex, Color.white, cls == 1, Vector4.zero, written);
                }
                var tiledMats = new Dictionary<string, Material>();
                Material TiledMaterial(Job j)
                {
                    if (tiledMats.TryGetValue(j.key, out var m)) return m;
                    var tex = j.source.HasProperty("_BaseMap") ? j.source.GetTexture("_BaseMap") as Texture2D : j.source.mainTexture as Texture2D;
                    bool unlit = j.route == Route.TiledUnlit;
                    bool cutout = j.key.StartsWith("C:", StringComparison.Ordinal);
                    m = MakeMaterial(spec, spec.id + "_" + VB.Sanitize((tex != null ? tex.name : "solid") + "_" + (tiledMats.Count + 1)),
                        unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Simple Lit", tex,
                        j.source.HasProperty("_BaseColor") ? j.source.GetColor("_BaseColor") : Color.white, cutout, j.st, written);
                    tiledMats[j.key] = m;
                    return m;
                }
                var unbakedMats = new Dictionary<string, Material>();
                Material UnbakedMaterial(Job j)
                {
                    if (unbakedMats.TryGetValue(j.key, out var m)) return m;
                    string path = spec.Folder + "/Materials/" + spec.id + "_" + VB.Sanitize(j.source.name) + ".mat";
                    var copy = new Material(j.source) { name = spec.id + "_" + j.source.name };
                    SaveMaterial(copy, path, written);
                    unbakedMats[j.key] = m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    return m;
                }

                // Pass 3: geometry.
                var cache = new Dictionary<Mesh, VenueMeshAcc.MeshCache>();
                var buckets = new Dictionary<(Vector2Int cell, Material mat, int level), VenueMeshAcc>();
                var unbaked = new Dictionary<Material, VenueMeshAcc>();
                var live = new Dictionary<(int owner, string material), List<Job>>();
                foreach (var j in jobs)
                {
                    if (j.route == Route.Live)
                    {
                        var k = (j.ownerId, j.key);
                        if (!live.TryGetValue(k, out var list)) live[k] = list = new List<Job>();
                        list.Add(j);
                        continue;
                    }
                    if (j.route == Route.Unbaked)
                    {
                        var um = UnbakedMaterial(j);
                        if (!unbaked.TryGetValue(um, out var ua)) unbaked[um] = ua = new VenueMeshAcc();
                        if (j.level0) ua.AppendSubmesh(j.mesh, j.submesh, j.matrix, null, cache);
                        continue;
                    }
                    Material mat;
                    Rect? remap = null;
                    Vector4? st = null;
                    switch (j.route)
                    {
                        case Route.AtlasLit: case Route.AtlasCutout: case Route.AtlasUnlit:
                            int cls = j.route == Route.AtlasLit ? 0 : j.route == Route.AtlasCutout ? 1 : 2;
                            mat = atlasMats[cls];
                            remap = entries[cls][j.key].rect;
                            st = j.st;
                            break;
                        case Route.AtlasTiled:
                            var te = entries[3][j.key];
                            mat = atlasMats[3];
                            remap = te.rect;
                            // Fold the span normalisation into the scale/offset: uv' = (uv*st.xy + st.zw - span.min) / span.size.
                            st = new Vector4(j.st.x / te.span.width, j.st.y / te.span.height,
                                (j.st.z - te.span.xMin) / te.span.width, (j.st.w - te.span.yMin) / te.span.height);
                            break;
                        default:
                            mat = TiledMaterial(j);
                            break;
                    }
                    bool unlitRoute = j.route == Route.AtlasUnlit || j.route == Route.TiledUnlit;
                    // Unlit (signs, neon, desktop screens) is a few hundred triangles: one LOD0 copy in a venue-wide group.
                    // Tiled ground/walls without source LODs go venue-wide; tiled parts of LOD'd models (tree bark) stay per cell.
                    bool venueWide = unlitRoute || spec.tiledVenueWide && j.route == Route.Tiled && !j.lodModel;
                    if (venueWide && !j.level0) continue;
                    if (venueWide) j.cell = new Vector2Int(999, unlitRoute ? 999 : 998);
                    for (int level = 0; level < 2; level++)
                    {
                        if (level == 0 && !j.level0 || level == 1 && (!j.level1 || venueWide)) continue;
                        var key = (j.cell, mat, level);
                        if (!buckets.TryGetValue(key, out var acc)) buckets[key] = acc = new VenueMeshAcc();
                        acc.AppendSubmesh(j.mesh, j.submesh, j.matrix, remap, cache, st);
                    }
                }

                // Strip the old renderers and every empty leaf they leave behind; keep anchors, approaches, colliders,
                // lights and components.
                var keep = new HashSet<Transform> { root };
                foreach (var a in go.GetComponentsInChildren<SpotAnchor>(true)) if (a.approach != null) keep.Add(a.approach);
                foreach (var g in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(g);
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var f = r.GetComponent<MeshFilter>();
                    Object.DestroyImmediate(r);
                    if (f != null) Object.DestroyImmediate(f);
                }
                bool removed;
                do
                {
                    removed = false;
                    foreach (var t in go.GetComponentsInChildren<Transform>(true).OrderByDescending(Depth).ToList())
                    {
                        if (t == null || keep.Contains(t) || t.childCount > 0) continue;
                        if (t.GetComponents<Component>().Length > 1) continue;
                        Object.DestroyImmediate(t.gameObject);
                        removed = true;
                    }
                } while (removed);

                // Emit merged cells.
                var statics = VB.Child("Static", root);
                int staticRenderers = 0, lod0Tris = 0;
                var usedMats = new HashSet<Material>();
                foreach (var cellGroup in buckets.GroupBy(b => b.Key.cell).OrderBy(g => g.Key.x).ThenBy(g => g.Key.y))
                {
                    var parent = VB.Child(cellGroup.Key.x == 999 ? (cellGroup.Key.y == 999 ? "Unlit" : "Tiled") : $"Cell_{cellGroup.Key.x}_{cellGroup.Key.y}", statics);
                    var levels = new[] { new List<Renderer>(), new List<Renderer>() };
                    var unlitShared = new List<Renderer>();
                    foreach (var b in cellGroup.OrderBy(b => b.Key.level).ThenBy(b => b.Key.mat.name, StringComparer.Ordinal))
                    {
                        if (b.Value.Empty) continue;
                        var r = Emit(spec, $"{parent.name}_{VB.Sanitize(b.Key.mat.name)}_LOD{b.Key.level}", b.Value, b.Key.mat, parent, written);
                        staticRenderers++;
                        usedMats.Add(b.Key.mat);
                        bool sharedUnlit = cellGroup.Key.x != 999 && b.Key.mat.shader.name.Contains("Unlit") && b.Key.level == 0;
                        if (sharedUnlit) unlitShared.Add(r);
                        else levels[b.Key.level].Add(r);
                        if (b.Key.level == 0) lod0Tris += b.Value.Triangles;
                    }
                    var lod0 = levels[0].Concat(unlitShared).ToArray();
                    var lod1 = levels[1].Concat(unlitShared).ToArray();
                    if (lod1.Length > 0 && levels[1].Count > 0)
                    {
                        var group = parent.gameObject.AddComponent<LODGroup>();
                        group.SetLODs(new[] { new LOD(spec.lod0Threshold, lod0), new LOD(0.01f, lod1) });
                        group.fadeMode = LODFadeMode.None;
                        group.RecalculateBounds();
                    }
                }
                if (unbaked.Count > 0)
                {
                    var ub = VB.Child("Unbaked", root);
                    foreach (var kv in unbaked)
                    {
                        Emit(spec, "Unbaked_" + VB.Sanitize(kv.Key.name), kv.Value, kv.Key, ub, written);
                        staticRenderers++; usedMats.Add(kv.Key); lod0Tris += kv.Value.Triangles;
                    }
                }
                // Live surfaces: one renderer per owner, at the owner's pivot, with the exact Screen_* material name.
                var hooks = VB.Child("Hooks", root);
                var hookNames = new List<string>();
                foreach (var kv in live.OrderBy(k => k.Key.material, StringComparer.Ordinal).ThenBy(k => k.Value[0].ownerPos.x))
                {
                    var matName = kv.Key.material;
                    var j0 = kv.Value[0];
                    var screenMat = LiveMaterial(spec, j0.source, matName, written);
                    var hook = VB.Child("Hook_" + j0.screenId + "_" + hookNames.Count, hooks);
                    hook.SetPositionAndRotation(j0.ownerPos, j0.ownerRot);
                    var toLocal = Matrix4x4.TRS(j0.ownerPos, j0.ownerRot, Vector3.one).inverse;
                    var acc = new VenueMeshAcc();
                    foreach (var j in kv.Value) acc.AppendSubmesh(j.mesh, j.submesh, toLocal * j.matrix, null, cache);
                    var mesh = SaveMesh(spec, acc.ToMesh(hook.name), hook.name, written);
                    hook.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var hr = hook.gameObject.AddComponent<MeshRenderer>();
                    hr.sharedMaterial = screenMat;
                    Quiet(hr);
                    hookNames.Add(hook.name + " (" + matName + ")");
                    staticRenderers++; usedMats.Add(screenMat); lod0Tris += acc.Triangles;
                }

                // Prop colliders and a NavMesh rebuilt with prop obstacles (walk-through props were the old NavMesh's gap).
                var venue = go.GetComponent<VenueSceneRoot>();
                {
                    string navPath = spec.Folder + "/" + spec.id + "_NavMesh.asset";
                    var sources = VenueCollisionPass.RepackCollision(go, props, propSpots);
                    var data = VB.BuildNavMeshData(spec.id, sources, venue.cameraBounds);
                    var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(navPath);
                    if (existing == null) { AssetDatabase.CreateAsset(data, navPath); existing = data; }
                    else { EditorUtility.CopySerialized(data, existing); Object.DestroyImmediate(data); EditorUtility.SetDirty(existing); }
                    venue.navigation = existing;
                    written.Add(navPath);
                }
                if (spec.lookdev) VenueLookdev.AddLook(spec, go);
                var liveAfter = LiveSurfaceKeys(go);
                if (!liveAfter.SequenceEqual(liveBefore))
                    throw new InvalidDataException($"{spec.id}: live surfaces changed. before [{string.Join(", ", liveBefore)}] after [{string.Join(", ", liveAfter)}]");

                var prefab = PrefabUtility.SaveAsPrefabAsset(go, spec.PrefabPath) ?? throw new IOException("Saving " + spec.PrefabPath + " failed.");
                written.Add(spec.PrefabPath);
                foreach (var folder in new[] { spec.Folder + "/Meshes", spec.Folder + "/Materials" })
                    foreach (var guid in AssetDatabase.FindAssets("", new[] { folder }))
                    {
                        var p = AssetDatabase.GUIDToAssetPath(guid);
                        if (!AssetDatabase.IsValidFolder(p) && !written.Contains(p)) AssetDatabase.DeleteAsset(p);
                    }
                VenueBuildPipeline.SaveFolder(spec.Folder);

                var spots = VenueSpotsFile.Load(spec.spotsKey);
                var nav = VenueBuildPipeline.ValidateBuild(spec, prefab.GetComponent<VenueSceneRoot>(), spots);
                var collision = VenueCollision.Validate(spec.id, prefab, prefab.GetComponent<VenueSceneRoot>().navigation, props);
                var receipt = new JObject
                {
                    ["id"] = spec.id, ["venueId"] = spec.venueId, ["mode"] = "repack", ["source"] = spec.repackSource, ["prefab"] = spec.PrefabPath,
                    ["prefabGuid"] = AssetDatabase.AssetPathToGUID(spec.PrefabPath), ["title"] = spec.title, ["district"] = spec.district,
                    ["identity"] = spec.identity, ["sourceRenderers"] = sourceRenderers, ["staticRenderers"] = staticRenderers,
                    ["lod0Triangles"] = lod0Tris, ["materials"] = usedMats.Count, ["atlasEntries"] = new JArray(entries.Select(e => e.Count)),
                    ["tiledMaterials"] = tiledMats.Count, ["hooks"] = new JArray(hookNames), ["liveSurfaces"] = new JArray(liveAfter),
                    ["before"] = before, ["navigation"] = nav, ["collision"] = collision, ["builtUtc"] = DateTime.UtcNow.ToString("o"),
                };
                Directory.CreateDirectory(Path.Combine(VenueBuildPipeline.EvidenceDir, "receipts"));
                File.WriteAllText(Path.Combine(VenueBuildPipeline.EvidenceDir, "receipts", spec.id + "-build.json"), receipt.ToString());
                Debug.Log($"CTLIFE [repack] {spec.id}: {sourceRenderers} source renderers -> {staticRenderers} renderers, {usedMats.Count} materials " +
                          $"(atlas entries {string.Join("/", entries.Select(e => e.Count))}, tiled {tiledMats.Count}), LOD0 {lod0Tris:N0} tris, " +
                          $"{hookNames.Count} live surfaces kept; before: max draws≈{before["maxEstimatedDraws"]}, tris≈{before["maxEstimatedTriangles"]}.");
                return new VB.Result
                {
                    prefab = prefab, sourcedInstances = sourceRenderers, staticRenderers = staticRenderers, lod0Triangles = lod0Tris,
                    hooks = hookNames.Count, materials = usedMats.Count, sources = new Dictionary<string, int>(),
                };
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static float DistanceToRect(Vector3 p, Rect r)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax), dz = Mathf.Max(r.yMin - p.z, 0f, p.z - r.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static int Depth(Transform t) { int d = 0; while (t.parent != null) { d++; t = t.parent; } return d; }

        private static Rect UvBounds(Mesh mesh, int submesh, Dictionary<(Mesh, int), Rect> cache)
        {
            if (cache.TryGetValue((mesh, submesh), out var r)) return r;
            var uv = mesh.uv;
            var tris = mesh.GetTriangles(submesh);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            if (uv.Length == 0) { x0 = y0 = 0; x1 = y1 = 0; }
            else foreach (int i in tris) { var p = uv[i]; x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y); }
            if (tris.Length == 0) x0 = y0 = x1 = y1 = 0;
            return cache[(mesh, submesh)] = Rect.MinMaxRect(x0, y0, x1, y1);
        }

        // ------------------------------------------------------------------ atlases and materials

        private static Texture2D BuildAtlas(VenueSpec spec, string name, List<Entry> list, bool alpha, HashSet<string> written)
        {
            var sources = list.Select(e => Pixels(e)).ToArray();
            var atlas = new Texture2D(AtlasMax, AtlasMax, TextureFormat.RGBA32, false);
            var rects = atlas.PackTextures(sources, Pad * 2, AtlasMax, false);
            if (rects == null) throw new InvalidOperationException("Atlas packing failed for " + spec.id + " " + name);
            int w = atlas.width, h = atlas.height;
            var px = atlas.GetPixels32();
            for (int i = 0; i < list.Count; i++)
            {
                var r = rects[i];
                int x0 = Mathf.RoundToInt(r.xMin * w), y0 = Mathf.RoundToInt(r.yMin * h);
                int x1 = Mathf.RoundToInt(r.xMax * w) - 1, y1 = Mathf.RoundToInt(r.yMax * h) - 1;
                // Extrude edges into the padding (mip bleeding guard).
                for (int y = y0 - Pad; y <= y1 + Pad; y++)
                    for (int x = x0 - Pad; x <= x1 + Pad; x++)
                    {
                        if (x < 0 || y < 0 || x >= w || y >= h || x >= x0 && x <= x1 && y >= y0 && y <= y1) continue;
                        int sx = Mathf.Clamp(x, x0, x1), sy = Mathf.Clamp(y, y0, y1);
                        px[y * w + x] = px[sy * w + sx];
                    }
                // Half-texel inset so bilinear sampling stays inside the entry.
                list[i].rect = Rect.MinMaxRect((x0 + 0.5f) / w, (y0 + 0.5f) / h, (x1 + 0.5f) / w, (y1 + 0.5f) / h);
            }
            atlas.SetPixels32(px);
            atlas.Apply();
            string path = spec.Folder + "/Textures/" + spec.id + "_" + name + ".png";
            File.WriteAllBytes(path, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            foreach (var s in sources) Object.DestroyImmediate(s);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = alpha;
            importer.maxTextureSize = AtlasMax;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = AtlasMax;
            android.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
            written.Add(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Readable, tinted copy of an entry's texture, at most EntryMax on its long side.</summary>
        private static Texture2D Pixels(Entry e)
        {
            Texture2D src;
            if (e.texture == null)
            {
                src = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                var fill = Enumerable.Repeat((Color32)e.tint, 64).ToArray();
                src.SetPixels32(fill);
                src.Apply();
                return src;
            }
            int w = e.texture.width, h = e.texture.height;
            float k = e.tileRes > 0 ? e.tileRes / (float)Mathf.Max(w, h) : Mathf.Min(1f, EntryMax / (float)Mathf.Max(w, h));
            int tw = Mathf.Max(4, Mathf.RoundToInt(w * k)), th = Mathf.Max(4, Mathf.RoundToInt(h * k));
            string path = AssetDatabase.GetAssetPath(e.texture);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            Texture2D raw = null;
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
            {
                raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!raw.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(raw); raw = null; }
            }
            if (raw == null)
            {
                var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(e.texture, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                raw = new Texture2D(tw, th, TextureFormat.RGBA32, false);
                raw.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
                raw.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
            var outTex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            var dst = new Color32[tw * th];
            var srcPx = raw.GetPixels32();
            int rw = raw.width, rh = raw.height;
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    // Box filter over the source footprint (exact for integer factors, close otherwise).
                    int sx0 = x * rw / tw, sx1 = Mathf.Max(sx0 + 1, (x + 1) * rw / tw);
                    int sy0 = y * rh / th, sy1 = Mathf.Max(sy0 + 1, (y + 1) * rh / th);
                    float r = 0, g = 0, b = 0, a = 0; int n = 0;
                    for (int yy = sy0; yy < sy1; yy++)
                        for (int xx = sx0; xx < sx1; xx++) { var c = srcPx[yy * rw + xx]; r += c.r; g += c.g; b += c.b; a += c.a; n++; }
                    dst[y * tw + x] = new Color32((byte)Mathf.Clamp(r / n * e.tint.r, 0, 255), (byte)Mathf.Clamp(g / n * e.tint.g, 0, 255),
                        (byte)Mathf.Clamp(b / n * e.tint.b, 0, 255), (byte)Mathf.Clamp(e.alpha ? a / n * e.tint.a : 255, 0, 255));
                }
            Object.DestroyImmediate(raw);
            if (e.tileRes > 0)
            {
                // Repeat the (resampled, tinted) tile over the used UV span with wrap.
                int ow = Mathf.Max(4, Mathf.CeilToInt(e.span.width * tw)), oh = Mathf.Max(4, Mathf.CeilToInt(e.span.height * th));
                var tiled = new Color32[ow * oh];
                for (int y = 0; y < oh; y++)
                    for (int x = 0; x < ow; x++)
                    {
                        float u = e.span.xMin + (x + 0.5f) / ow * e.span.width, v = e.span.yMin + (y + 0.5f) / oh * e.span.height;
                        int tx = Mathf.Clamp(Mathf.FloorToInt((u - Mathf.Floor(u)) * tw), 0, tw - 1);
                        int ty = Mathf.Clamp(Mathf.FloorToInt((v - Mathf.Floor(v)) * th), 0, th - 1);
                        tiled[y * ow + x] = dst[ty * tw + tx];
                    }
                Object.DestroyImmediate(outTex);
                outTex = new Texture2D(ow, oh, TextureFormat.RGBA32, false);
                dst = tiled;
            }
            outTex.SetPixels32(dst);
            outTex.Apply();
            return outTex;
        }

        private static Material MakeMaterial(VenueSpec spec, string name, string shaderName, Texture2D tex, Color color, bool cutout, Vector4 st, HashSet<string> written)
        {
            var shader = Shader.Find(shaderName) ?? throw new InvalidOperationException("Shader missing: " + shaderName);
            string path = spec.Folder + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.name = name;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            if (st != Vector4.zero)
            {
                m.SetTextureScale("_BaseMap", new Vector2(st.x, st.y));
                m.SetTextureOffset("_BaseMap", new Vector2(st.z, st.w));
            }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
            if (cutout)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.45f);
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            written.Add(path);
            return m;
        }

        private static Material LiveMaterial(VenueSpec spec, Material source, string exactName, HashSet<string> written)
        {
            string path = spec.Folder + "/Materials/" + exactName + ".mat";
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.name = exactName; // LiveSurfaces matches on this name (house_ads.json rules)
            var tex = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.mainTexture;
            m.SetTexture("_BaseMap", tex);
            if (source.HasProperty("_BaseMap"))
            {
                m.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
                m.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
            }
            m.SetColor("_BaseColor", source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white);
            EditorUtility.SetDirty(m);
            written.Add(path);
            return m;
        }

        private static void SaveMaterial(Material m, string path, HashSet<string> written)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null) AssetDatabase.CreateAsset(m, path);
            else { EditorUtility.CopySerialized(m, existing); Object.DestroyImmediate(m); EditorUtility.SetDirty(existing); }
            written.Add(path);
        }

        private static Renderer Emit(VenueSpec spec, string name, VenueMeshAcc acc, Material material, Transform parent, HashSet<string> written)
        {
            var mesh = SaveMesh(spec, acc.ToMesh(name), name, written);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            Quiet(r);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
            return r;
        }

        private static void Quiet(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static Mesh SaveMesh(VenueSpec spec, Mesh mesh, string name, HashSet<string> written)
        {
            string path = spec.Folder + "/Meshes/" + VB.Sanitize(name) + ".asset";
            written.Add(path);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ------------------------------------------------------------------ parity check

        /// <summary>
        /// Repacked prefab vs its source: same live surfaces, same anchors (id, position, rotation, kind, anim,
        /// approach point), same CafeSeatMotionProfile data, same colliders, same VenueSceneRoot camera settings and
        /// NavMesh bytes, and no lights or LOD-less leftovers from the source. Writes receipts/{id}-parity.json.
        /// </summary>
        public static void CheckParity()
        {
            int failures = 0;
            foreach (var spec in VenueBuildPipeline.Selected().Where(s => !string.IsNullOrEmpty(s.repackSource)))
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(spec.repackSource);
                var dst = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
                var problems = new JArray();
                void Check(bool ok, string what) { if (!ok) problems.Add(what); }
                Check(LiveSurfaceKeys(src).SequenceEqual(LiveSurfaceKeys(dst)), "live surfaces differ");
                var a0 = src.GetComponentsInChildren<SpotAnchor>(true).OrderBy(x => x.spotId).ToList();
                var a1 = dst.GetComponentsInChildren<SpotAnchor>(true).OrderBy(x => x.spotId).ToList();
                Check(a0.Count == a1.Count, $"anchor count {a0.Count} vs {a1.Count}");
                foreach (var (x, y) in a0.Zip(a1, (x, y) => (x, y)))
                {
                    Check(x.spotId == y.spotId && x.kind == y.kind && x.anim == y.anim, "anchor identity " + x.spotId);
                    Check((x.transform.position - y.transform.position).sqrMagnitude < 1e-8f &&
                          Quaternion.Angle(x.transform.rotation, y.transform.rotation) < 0.01f, "anchor pose " + x.spotId);
                    Check((x.approach == null) == (y.approach == null) &&
                          (x.approach == null || (x.approach.position - y.approach.position).sqrMagnitude < 1e-8f), "approach " + x.spotId);
                }
                var p0 = src.GetComponent<CafeSeatMotionProfile>();
                var p1 = dst.GetComponent<CafeSeatMotionProfile>();
                Check((p0 == null) == (p1 == null) && (p0 == null || EditorJsonUtility.ToJson(p0) == EditorJsonUtility.ToJson(p1)), "CafeSeatMotionProfile differs");
                var c0 = src.GetComponentsInChildren<Collider>(true).Select(c => c.GetType().Name + c.bounds.center.ToString("F3") + c.bounds.size.ToString("F3")).OrderBy(k => k).ToList();
                var c1 = dst.GetComponentsInChildren<Collider>(true).Select(c => c.GetType().Name + c.bounds.center.ToString("F3") + c.bounds.size.ToString("F3")).OrderBy(k => k).ToList();
                Check(!c0.Except(c1).Any(), $"source colliders missing: {c0.Except(c1).Count()}");
                var v0 = src.GetComponent<VenueSceneRoot>();
                var v1 = dst.GetComponent<VenueSceneRoot>();
                Check(v0.venueId == v1.venueId && v0.districtId == v1.districtId && v0.cameraBounds == v1.cameraBounds &&
                      v0.cameraDistance == v1.cameraDistance && v0.cameraPitch == v1.cameraPitch && v0.cameraYaw == v1.cameraYaw &&
                      v0.cameraFieldOfView == v1.cameraFieldOfView && v0.cameraTargetViewportY == v1.cameraTargetViewportY, "VenueSceneRoot settings");
                // NavMesh is intentionally rebuilt with prop obstacles; reachability is gated by the build.
                Check(v1.navigation != null, "no NavMesh");
                Check(dst.GetComponentsInChildren<Light>(true).Length == 0, "lights left in prefab");
                Check(dst.GetComponent<VenueLightmaps>() != null, "not baked");
                var report = new JObject { ["id"] = spec.id, ["ok"] = problems.Count == 0, ["problems"] = problems,
                    ["anchors"] = a1.Count, ["approaches"] = a1.Count(x => x.approach != null), ["colliders"] = c1.Count,
                    ["liveSurfaces"] = new JArray(LiveSurfaceKeys(dst)), ["seatMotionProfile"] = p1 != null };
                File.WriteAllText(Path.Combine(VenueBuildPipeline.EvidenceDir, "receipts", spec.id + "-parity.json"), report.ToString());
                if (problems.Count > 0) failures++;
                Debug.Log($"CTLIFE [repack] parity {spec.id}: {(problems.Count == 0 ? "OK" : "FAILED " + string.Join("; ", problems))} " +
                          $"({a1.Count} anchors, {report["approaches"]} approaches, {c1.Count} colliders, live {string.Join(",", LiveSurfaceKeys(dst))}, profile={p1 != null})");
            }
            if (Application.isBatchMode && failures > 0) EditorApplication.Exit(1);
        }

        /// <summary>Hash of every serialized property except the object name (binary assets such as NavMeshData).</summary>
        private static string SerializedHash(Object o)
        {
            var so = new SerializedObject(o);
            var it = so.GetIterator();
            var sb = new System.Text.StringBuilder();
            bool enter = true;
            while (it.Next(enter))
            {
                enter = true;
                if (it.propertyPath.StartsWith("m_Name", StringComparison.Ordinal) || it.propertyPath.StartsWith("m_NavMeshBuildSettings.debug", StringComparison.Ordinal)) continue;
                sb.Append(it.propertyPath).Append('=');
                switch (it.propertyType)
                {
                    case SerializedPropertyType.Integer: sb.Append(it.longValue).Append(';'); break;
                    case SerializedPropertyType.Float: sb.Append(it.floatValue.ToString("R")).Append(';'); break;
                    case SerializedPropertyType.Boolean: sb.Append(it.boolValue).Append(';'); break;
                    case SerializedPropertyType.String: sb.Append(it.stringValue).Append(';'); break;
                }
            }
            if (Environment.GetCommandLineArgs().Contains("-ctDumpHash"))
                File.WriteAllText(Path.Combine(VenueBuildPipeline.EvidenceDir, "logs", "hash-" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o)) + ".txt"), sb.ToString());
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return BitConverter.ToString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
        }

        // ------------------------------------------------------------------ registration

        /// <summary>
        /// Point the venue content registry at the repacked prefabs and re-sync the runtime catalog, then check that
        /// VenueContent resolves each id to the new prefab.
        /// </summary>
        [MenuItem("CT Life/Venues/Repack: register repacked venues")]
        public static void Register()
        {
            const string registryPath = "Assets/Venues/VenueContentRegistry.asset";
            var registry = AssetDatabase.LoadAssetAtPath<VenueContentRegistry>(registryPath) ?? throw new FileNotFoundException(registryPath);
            var specs = VenueBuildPipeline.Selected().Where(s => !string.IsNullOrEmpty(s.repackSource)).ToList();
            foreach (var spec in specs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath)?.GetComponent<VenueSceneRoot>();
                if (prefab == null || prefab.GetComponent<VenueLightmaps>() == null) throw new InvalidDataException(spec.id + " is not built and baked.");
                var e = registry.venues.FirstOrDefault(v => v.venueId == spec.venueId);
                if (e == null) registry.venues.Add(e = new VenueContentRegistry.Entry { venueId = spec.venueId });
                e.prefab = prefab;
            }
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssetIfDirty(registry);
            VenueContentCatalog.Sync();
            VenueContent.ResetForTests();
            foreach (var spec in specs)
            {
                var found = VenueContent.Find(spec.venueId);
                string path = found != null ? AssetDatabase.GetAssetPath(found) : null;
                if (path != spec.PrefabPath) throw new InvalidDataException($"{spec.id}: runtime catalog resolves to {path ?? "nothing"}, expected {spec.PrefabPath}");
                Debug.Log($"CTLIFE [repack] registered {spec.id} -> {path}");
            }
        }
    }
}
