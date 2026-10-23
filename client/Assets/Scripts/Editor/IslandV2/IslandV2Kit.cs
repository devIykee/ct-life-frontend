using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using CTLife.World.Vehicles;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// The island kit (World lane): copies the processed world kit (art/processed/world, made by the env pipeline with
    /// art/pipeline/world_config.py) into Assets/World/Island/Kit, sets lean import settings (no materials, no
    /// animation, no tangents), makes one CT Life/World/Kit material per texture (palette facades tintable, foliage
    /// with wind and cut-out) and writes the IslandKit asset with each model's LODs, cull distances and layer. Models
    /// already in the project from the venue kit (art/processed/environment -> Assets/Art) are referenced in place.
    /// Only models the dressing uses are included, so unused kit models never reach the APK.
    /// </summary>
    public static class IslandV2Kit
    {
        public const string KitDir = "Assets/World/Island/Kit";
        public const string KitAsset = KitDir + "/IslandKit.asset";

        private static string WorldRoot => Path.Combine(IslandV2Paths.Repo, "art", "processed", "world");
        /// <summary>World v3 (2026-10-22): the semi-real kit (Neighbourhood City anchor, 99.Miles buildings, Poly Haven props),
        /// one merged manifest in the same id format plus kind, family, door_main, lod2_material and credit. It covers part
        /// of the city for now, so both manifests load and dress.py mixes their pools.</summary>
        private static string WorldV3Root => Path.Combine(IslandV2Paths.Repo, "art", "processed", "world_v3");
        private static string EnvManifest => Path.Combine(IslandV2Paths.Repo, "art", "processed", "environment", "manifest.json");

        /// <summary>Distances (m) where LOD0, LOD1 and LOD2 stop drawing, shadow range, layer, by kind.</summary>
        private static (float[] ends, float shadow, int layer, string kind) Rules(string id, JObject item)
        {
            var tags = (item["tags"] as JArray)?.Select(t => (string)t).ToHashSet() ?? new HashSet<string>();
            // world v3: 15k-tri LOD0 (cards included) only near the camera, the 4k LOD1 through the street, the baked box
            // far; the 105 m tower keeps its detail further so the skyline does not pop (Seeker 2026-10-22: GPU ~30 ms of the
            // 33 ms budget downtown with LOD0 to 42 m, 2.1M triangles; tightened)
            if (id == "m99_office_tower") return (new[] { 100f, 600f, 3200f }, 260f, Layers.Building, "building");
            // (2026-10-22 LOD continuity: the baked box took over at 160 m and read as a different building; switches now
            // sit further out and cross-fade, IslandInstanceRenderer)
            if (id.StartsWith("m99_")) return (new[] { 45f, 240f, 1500f }, 140f, Layers.Building, "building");
            // world v3 districts (2026-10-23): DanielZhabotinsky industrial and Lost Gecko flats (2-3.5k-tri LOD0, own LOD2 box)
            if (id.StartsWith("dz_") || id.StartsWith("lg_")) return (new[] { 60f, 260f, 1500f }, 140f, Layers.Building, "building");
            // world polish (2026-10-23, art/pipeline/world_v3_plants.py): leaf-card shrubs and hedges (shadows close by only),
            // ground-cover cards (one LOD, no shadows, faded out by 36 m), the chain-link fence panel
            if (id.StartsWith("pv_bush") || id.StartsWith("pv_hedge")) return (new[] { 32f, 90f, 240f }, 22f, Layers.Vegetation, "bush");
            if (id.StartsWith("pv_")) return (new[] { 36f }, 0f, Layers.Vegetation, "grass");
            if (id == "yd_chainlink_fence") return (new[] { 40f, 140f }, 30f, Layers.Prop, "prop");
            // TampaJoey city props: the street light is a lamp (post proxy); flat grates and covers draw close only
            if (id == "tj_street_light") return (new[] { 40f, 140f, 380f }, 35f, Layers.Prop, "lamp");
            if (id == "tj_drain" || id == "tj_storm_drain" || id == "tj_manhole" || id == "tj_tree_grate") return (new[] { 30f, 60f }, 0f, Layers.Prop, "prop");
            if (id == "tj_bus_stop" || id == "tj_dumpster") return (new[] { 40f, 200f }, 30f, Layers.Prop, "prop");
            if (id.StartsWith("tj_")) return (new[] { 28f, 110f }, 15f, Layers.Prop, "prop");
            if (id.StartsWith("nc_building")) return (new[] { 30f, 170f, 1300f }, 110f, Layers.Building, "building");
            // planter trees: their own billboard LOD2 (no baked impostor)
            if (id.StartsWith("nc_tree") || id.StartsWith("lp_")) return (new[] { 30f, 100f, 320f }, 45f, Layers.Vegetation, "tree");
            if ((id.StartsWith("nc_prop_lamp") || id.StartsWith("ph_street_lamp"))) return (new[] { 40f, 140f, 380f }, 35f, Layers.Prop, "lamp");
            // world v3 street kit (art/processed/world_v3/v3_street, 2026-10-22)
            if (id == "v3_traffic_light" || id == "v3_stop_sign") return (new[] { 45f, 380f }, 35f, Layers.Prop, "lamp");
            if (id == "v3_bus_shelter") return (new[] { 40f, 260f }, 30f, Layers.Prop, "prop");
            if (id == "v3_vendor_cart" || id == "v3_cafe_set") return (new[] { 35f, 160f }, 20f, Layers.Prop, "prop");
            if (id == "v3_manhole" || id == "v3_storm_drain") return (new[] { 35f, 70f }, 0f, Layers.Prop, "prop");
            if (id.StartsWith("v3_")) return (new[] { 28f, 120f }, 12f, Layers.Prop, "prop");
            if (id.StartsWith("nc_prop") || id.StartsWith("ph_")) return (new[] { 28f, 70f, 140f }, 18f, Layers.Prop, "prop");
            if (id.StartsWith("hero_")) return (new[] { 70f, 240f, 900f }, 80f, Layers.Prop, "hero");
            if (id.StartsWith("kw_ship")) return (new[] { 260f, 900f, 3600f }, 0f, Layers.Prop, "ship");
            if (id.StartsWith("kw_")) return (new[] { 55f, 170f, 800f }, 45f, Layers.Prop, "boat");
            if (id.StartsWith("kc_tower")) return (new[] { 120f, 420f, 2400f }, 160f, Layers.Building, "building");
            // Quaternius town buildings: 3.5k-tri LOD0 (W2 perf note), so LOD1 from 45 m and shadows to 90 m
            if (id.StartsWith("qtb_")) return (new[] { 45f, 150f, 1100f }, 90f, Layers.Building, "building");
            if (id.StartsWith("pp_")) return (new[] { 35f, 90f, 200f }, 35f, Layers.Prop, "prop");
            if (id.StartsWith("kc_detail")) return (new[] { 40f, 90f, 160f }, 30f, Layers.Prop, "prop");
            if (tags.Contains("building") || id.StartsWith("shop_") || id.StartsWith("tower_glass")) return (new[] { 80f, 260f, 1300f }, 130f, Layers.Building, "building");
            if (id.StartsWith("w_tree") || id.StartsWith("w_pine")) return (new[] { 45f, 110f, 900f }, 70f, Layers.Vegetation, "tree");
            if (id.StartsWith("kf_tree") || id.StartsWith("palm") || id.StartsWith("tree_")) return (new[] { 55f, 170f, 900f }, 70f, Layers.Vegetation, "tree");
            // eye-level ground detail (grass clumps, clover, ferns, flowers, pebbles): only near the camera
            if (tags.Contains("ground_detail")) return (new[] { 14f, 26f, 38f }, 0f, Layers.Vegetation, tags.Contains("pebble") ? "prop" : "grass");
            if (id.StartsWith("kf_bush")) return (new[] { 30f, 80f, 200f }, 25f, Layers.Vegetation, "bush");
            if (id.StartsWith("kf_rock")) return (new[] { 45f, 140f, 420f }, 40f, Layers.Prop, "rock");
            if (id.StartsWith("kf_grass")) return (new[] { 25f, 45f, 60f }, 0f, Layers.Vegetation, "grass");
            if (id.StartsWith("kn_crop")) return (new[] { 40f, 110f, 260f }, 0f, Layers.Vegetation, "crop");
            if (id.StartsWith("car_")) return (new[] { 35f, 110f, 320f }, 45f, Layers.Prop, "vehicle");
            if (id.StartsWith("kr_electricity")) return (new[] { 70f, 220f, 600f }, 40f, Layers.Prop, "pole");
            if (id.StartsWith("kr_light") || id.StartsWith("street_lamp") || id.StartsWith("kr_traffic")) return (new[] { 45f, 140f, 380f }, 35f, Layers.Prop, "lamp");
            if (id.StartsWith("ki_") || id.StartsWith("billboard")) return (new[] { 70f, 220f, 800f }, 60f, Layers.Prop, "large_prop");
            return (new[] { 25f, 60f, 130f }, 15f, Layers.Prop, "prop");
        }

        public static IslandKit Build(HashSet<string> used, JObject receipt)
        {
            Directory.CreateDirectory(KitDir);
            Directory.CreateDirectory(KitDir + "/Models");
            Directory.CreateDirectory(KitDir + "/Textures");
            Directory.CreateDirectory(KitDir + "/Materials");
            var world = File.Exists(Path.Combine(WorldRoot, "manifest.json")) ? JObject.Parse(File.ReadAllText(Path.Combine(WorldRoot, "manifest.json"))) : new JObject();
            var env = File.Exists(EnvManifest) ? JObject.Parse(File.ReadAllText(EnvManifest)) : new JObject();
            var v3 = File.Exists(Path.Combine(WorldV3Root, "manifest.json")) ? JObject.Parse(File.ReadAllText(Path.Combine(WorldV3Root, "manifest.json"))) : new JObject();
            // 1. copy the world models and textures the dressing uses
            var paths = new Dictionary<string, string>();          // id -> asset path of the FBX
            var texFiles = new HashSet<string>();
            int copied = 0;
            foreach (var id in used.OrderBy(x => x))
            {
                if ((world[id] ?? v3[id]) is JObject wi)
                {
                    var rel = (string)wi["fbx"];                         // e.g. world/buildings/kc_building_a.fbx
                    var src = Path.Combine(IslandV2Paths.Repo, "art", "processed", rel);
                    var sub = Path.GetFileName(Path.GetDirectoryName(rel));
                    var dst = $"{KitDir}/Models/{sub}/{id}.fbx";
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (CopyIfChanged(src, dst)) copied++;
                    paths[id] = dst;
                    foreach (var t in (JArray)wi["textures"]) texFiles.Add((string)t);
                }
                else if (env[id] is JObject ei)
                {
                    var p = "Assets/Art/" + (string)ei["fbx"];
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(p) != null) paths[id] = p;
                }
            }
            CopyIfChanged(Path.Combine(WorldRoot, "_shared", "world_palette.png"), KitDir + "/Textures/world_palette.png");
            foreach (var t in texFiles) CopyIfChanged(Path.Combine(IslandV2Paths.Repo, "art", "processed", t), KitDir + "/Textures/" + Path.GetFileName(t));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var kv in paths.Where(kv => kv.Value.StartsWith(KitDir))) ModelImport(kv.Value, v3[kv.Key] != null);
            foreach (var f in Directory.GetFiles(KitDir + "/Textures", "*.png")) TextureImport(f.Replace('\\', '/'));
            // 2. materials and prototypes
            var kit = AssetDatabase.LoadAssetAtPath<IslandKit>(KitAsset);
            if (kit == null) { kit = ScriptableObject.CreateInstance<IslandKit>(); AssetDatabase.CreateAsset(kit, KitAsset); }
            var protos = new List<IslandKit.Proto>();
            var missing = new List<string>();
            foreach (var id in used.OrderBy(x => x))
            {
                if (!paths.TryGetValue(id, out var fbx)) { missing.Add(id); continue; }
                bool isV3 = v3[id] != null;
                var item = (world[id] ?? v3[id] ?? env[id]) as JObject;
                var meshes = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>()
                    .OrderBy(m => LodIndex(m.name)).ToArray();
                if (meshes.Length == 0) { missing.Add(id); continue; }
                var lods = meshes.Where(m => LodIndex(m.name) < 9).ToArray();
                if (lods.Length == 0) lods = new[] { meshes[0] };
                var (ends, shadow, layer, kind) = Rules(id, item);
                var mats = ((JArray)item["materials"]).Select(m => (string)m).ToArray();
                var proto = new IslandKit.Proto
                {
                    id = id, lods = lods, lodEnd = ends.Take(Math.Max(1, lods.Length)).ToArray(), shadowRange = shadow, layer = layer, kind = kind,
                    bounds = lods[0].bounds,
                };
                if (proto.lodEnd.Length < lods.Length) proto.lodEnd = ends;
                if (lods.Length < ends.Length) proto.lodEnd[proto.lodEnd.Length - 1] = ends[ends.Length - 1];   // last LOD lives to the cull distance
                if (isV3)
                {
                    // LOD0/LOD1 share the manifest's slot order; the far LOD is one baked material (the building's box
                    // facade or the tree's billboard)
                    proto.materials = mats.Select(m => V3Material(m, kind, id)).ToArray();
                    var far = (string)item["lod2_material"] ?? (kind == "tree" ? $"M_{id}_billboard" : null);
                    if (far != null && lods.Length >= 3 && lods[lods.Length - 1].subMeshCount == 1)
                    {
                        proto.lodMaterials = new Material[lods.Length];
                        proto.lodMaterials[lods.Length - 1] = V3Material(far, kind, id);
                    }
                }
                else proto.materials = mats.Select(m => Material(m, kind, id)).ToArray();
                proto.material = proto.materials.FirstOrDefault();
                protos.Add(proto);
            }
            // W4: trees get a far billboard LOD (two crossed quads with a baked front/side view), so a 13k-tree forest
            // with 3-6k-triangle trees stays cheap beyond ~200 m
            int imp = 0;
            foreach (var proto in protos.Where(p => p.kind == "tree" && (p.lodMaterials == null || p.lodMaterials.Length == 0 || p.lodMaterials.All(m => m == null))))
            {
                var (im, imat) = BakeImpostor(proto);
                if (im == null) continue;
                proto.lods = proto.lods.Concat(new[] { im }).ToArray();
                float cull = proto.lodEnd[proto.lodEnd.Length - 1];
                var ends = proto.lodEnd.ToList();
                // detailed LODs to ~200 m, then the billboard to the old cull distance
                for (int i = 0; i < ends.Count; i++) ends[i] = Mathf.Min(ends[i], i == ends.Count - 1 ? 200f : ends[i]);
                ends.Add(cull);
                proto.lodEnd = ends.ToArray();
                proto.lodMaterials = new Material[proto.lods.Length];
                proto.lodMaterials[proto.lods.Length - 1] = imat;
                imp++;
            }
            receipt["kitImpostors"] = imp;
            protos.AddRange(V3Cars(receipt));
            kit.protos = protos.ToArray();
            receipt["kitAlphaTested"] = SyncKeywords(kit);
            IslandV2Collision.BakeKit(kit);
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssetIfDirty(kit);
            receipt["kitModels"] = protos.Count;
            receipt["kitCopied"] = copied;
            if (missing.Count > 0) { receipt["kitMissing"] = new JArray(missing); Debug.LogWarning("CTLIFE [islandv2] kit models missing: " + string.Join(", ", missing)); }
            return kit;
        }

        /// <summary>
        /// The kit shaders' alpha test is a keyword (_ALPHATEST_ON) so opaque facades keep early depth on Mali: on for every
        /// kit material with a cutoff, off for the rest. Returns how many are alpha-tested.
        /// </summary>
        public static int SyncKeywords(IslandKit kit)
        {
            int n = 0;
            var seen = new HashSet<Material>();
            foreach (var p in kit.protos)
                foreach (var m in p.materials.Concat(p.lodMaterials ?? Array.Empty<Material>()).Append(p.material))
                {
                    if (m == null || !seen.Add(m) || !m.HasProperty("_Cutoff")) continue;
                    bool on = m.GetFloat("_Cutoff") > 0f;
                    if (on) { m.EnableKeyword("_ALPHATEST_ON"); n++; } else m.DisableKeyword("_ALPHATEST_ON");
                    EditorUtility.SetDirty(m);
                }
            return n;
        }

        /// <summary>Batch: re-sync the kit materials' keywords without rebuilding the island.</summary>
        public static void SyncKeywordsBatch()
        {
            var kit = AssetDatabase.LoadAssetAtPath<IslandKit>(KitAsset);
            Debug.Log("CTLIFE [islandv2] alpha-tested kit materials: " + SyncKeywords(kit));
            AssetDatabase.SaveAssets();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Batch: re-merge the v3 decor cars from the current vehicle prefabs (after VehicleImport.RunBatch) and swap them into
        /// the kit in place: same prototype ids and indices (the cells reference prototypes by index), no island rebuild.
        /// </summary>
        public static void RefreshV3CarsBatch()
        {
            int code = 0;
            try
            {
                var kit = AssetDatabase.LoadAssetAtPath<IslandKit>(KitAsset) ?? throw new InvalidOperationException("no island kit");
                var receipt = new JObject();
                int swapped = 0;
                foreach (var fresh in V3Cars(receipt).ToList())
                {
                    int i = kit.IndexOf(fresh.id);
                    if (i < 0) { Debug.LogWarning("CTLIFE [islandv2] decor car not in the kit: " + fresh.id); continue; }
                    var old = kit.protos[i];
                    fresh.collision = old.collision;
                    kit.protos[i] = fresh;
                    swapped++;
                }
                SyncKeywords(kit);
                EditorUtility.SetDirty(kit);
                AssetDatabase.SaveAssets();
                Debug.Log($"CTLIFE [islandv2] decor cars refreshed: {swapped}; {receipt["kitDecorCars"]}");
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        // ------------------------------------------------------------------ v3 parked decor cars

        /// <summary>The v3 drivables the street's parked decor shows (same models the player swaps into on approach).</summary>
        public static readonly string[] V3CarSpecs = { "compact", "sedan", "sports", "taxi", "police", "van", "pickup" };
        public static string V3CarId(string spec) => "v3car_" + spec;

        /// <summary>
        /// Instanced decor versions of the v3 cars (Resources/Vehicles/&lt;spec&gt;.prefab, built by VehicleImport.V3): the
        /// prefab's LOD1_* and LOD2_* renderers merged per LOD into one mesh with three submeshes (Body, Paint, glass) in
        /// the prefab's own space (its +Z front turn included). Body: the atlas with its mask; Paint: the near-white paint
        /// tinted per instance with the traffic palette (_Tint, the colour the drivable gets on swap); glass: dark and
        /// smooth. Same CT Life/World/Kit PBR shader as the v3 buildings, so parked cars sit in the same light.
        /// </summary>
        private static IEnumerable<IslandKit.Proto> V3Cars(JObject receipt)
        {
            var made = new JArray();
            string dir = KitDir + "/Models/v3cars";
            Directory.CreateDirectory(dir);
            foreach (var name in V3CarSpecs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Vehicles/{name}.prefab");
                var spec = VehicleCatalog.Get(name);
                if (prefab == null || spec == null) { Debug.LogWarning("CTLIFE [islandv2] no v3 prefab for decor car " + name); continue; }
                var toRoot = prefab.transform.worldToLocalMatrix;
                var roles = new[] { "Body", "Paint", "Glass" };
                string RoleOf(Material m) => m == null ? "Body" : m.name.StartsWith("VehicleGlass", StringComparison.Ordinal) || m.name.StartsWith("Windows", StringComparison.Ordinal) ? "Glass"
                    : m.name.EndsWith("_Paint", StringComparison.Ordinal) ? "Paint" : "Body";
                var lods = new List<Mesh>();
                bool anyPaint = false;
                foreach (int lod in new[] { 1, 2, 3 })
                {
                    var parts = roles.ToDictionary(r => r, r => new List<CombineInstance>());
                    foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (!r.name.StartsWith($"LOD{lod}_", StringComparison.Ordinal)) continue;
                        var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                        if (mesh == null) continue;
                        var mats = r.sharedMaterials;
                        for (int sm = 0; sm < mesh.subMeshCount; sm++)
                            parts[RoleOf(sm < mats.Length ? mats[sm] : null)].Add(new CombineInstance { mesh = mesh, subMeshIndex = sm, transform = toRoot * r.transform.localToWorldMatrix });
                    }
                    if (parts.Values.All(l => l.Count == 0)) continue;
                    anyPaint |= parts["Paint"].Count > 0;
                    var subs = new List<Mesh>();
                    foreach (var role in roles)
                    {
                        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                        if (parts[role].Count > 0) m.CombineMeshes(parts[role].ToArray(), true, true);
                        subs.Add(m);
                    }
                    // one mesh, submesh per role (an empty role keeps its slot so every LOD has the same material order)
                    var verts = new List<Vector3>(); var nrm = new List<Vector3>(); var tan = new List<Vector4>(); var uv = new List<Vector2>();
                    var tris = new List<int[]>();
                    foreach (var m in subs)
                    {
                        int o = verts.Count;
                        verts.AddRange(m.vertices); nrm.AddRange(m.normals);
                        var t = m.tangents; tan.AddRange(t.Length == m.vertexCount ? t : new Vector4[m.vertexCount]);
                        var u = m.uv; uv.AddRange(u.Length == m.vertexCount ? u : new Vector2[m.vertexCount]);
                        tris.Add(m.vertexCount == 0 ? Array.Empty<int>() : m.triangles.Select(i => i + o).ToArray());
                        Object.DestroyImmediate(m);
                    }
                    var outMesh = new Mesh { name = $"{V3CarId(name)}_LOD{lod}", indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                    outMesh.SetVertices(verts); outMesh.SetNormals(nrm); outMesh.SetTangents(tan); outMesh.SetUVs(0, uv);
                    outMesh.subMeshCount = roles.Length;
                    for (int i = 0; i < roles.Length; i++) outMesh.SetTriangles(tris[i], i, false);
                    outMesh.RecalculateBounds();
                    outMesh.UploadMeshData(false);
                    var path = $"{dir}/{V3CarId(name)}_LOD{lod}.asset";
                    var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (old != null) { EditorUtility.CopySerialized(outMesh, old); Object.DestroyImmediate(outMesh); outMesh = old; }
                    else AssetDatabase.CreateAsset(outMesh, path);
                    lods.Add(outMesh);
                }
                if (lods.Count == 0) continue;
                var proto = new IslandKit.Proto
                {
                    // LOD1 (~10k) to 25 m, the 5k LOD2 (same atlas and silhouette) beyond, cross-faded: the old 30 m switch onto a 2.4k melted mesh read
                    // as a different car (2026-10-22). With the ~1.2k LOD3 (same weld and atlas) LOD2 hands over at 70 m (perf lane).
                    id = V3CarId(name), lods = lods.ToArray(), lodEnd = lods.Count == 3 ? new[] { 25f, 70f, 220f } : lods.Count == 2 ? new[] { 25f, 220f } : new[] { 340f },
                    shadowRange = 45f, layer = Layers.Prop, kind = "vehicle", bounds = lods[0].bounds,
                };
                proto.materials = new[] { V3CarMaterial(name, "Body", spec), V3CarMaterial(name, "Paint", spec), V3CarMaterial(name, "Glass", spec) };
                proto.material = proto.materials[0];
                made.Add($"{proto.id}: LOD tris {string.Join("/", lods.Select(m => Enumerable.Range(0, m.subMeshCount).Sum(i => (int)m.GetIndexCount(i)) / 3))}{(anyPaint ? "" : " (no paint slot)")}");
                yield return proto;
            }
            receipt["kitDecorCars"] = made;
        }

        private static Material V3CarMaterial(string name, string role, VehicleSpec spec)
        {
            string key = $"v3car_{name}_{role}";
            var path = $"{KitDir}/Materials/{key}.mat";
            var sh = Shader.Find("CT Life/World/Kit PBR") ?? throw new InvalidOperationException("CT Life/World/Kit PBR shader missing");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(sh) { name = key }; AssetDatabase.CreateAsset(mat, path); }
            mat.shader = sh;
            Texture2D Tex(string k) => AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Art/vehicles/v3/{name}_{k}.png");
            void Kw(string k, bool on) { if (on) mat.EnableKeyword(k); else mat.DisableKeyword(k); }
            var nrm = Tex("Normal");
            bool glass = role == "Glass", paint = role == "Paint";
            mat.SetTexture("_BaseMap", glass ? null : Tex("BaseColor"));
            mat.SetColor("_BaseColor", glass ? new Color(0.30f, 0.33f, 0.37f, 0.4f) : Color.white);
            mat.SetTexture("_BumpMap", glass ? null : nrm);
            Kw("_NORMALMAP", !glass && nrm != null); mat.SetFloat("_UseNormal", !glass && nrm != null ? 1f : 0f);
            var mask = role == "Body" ? Tex("MaskMap") : null;
            mat.SetTexture("_MaskMap", mask);
            Kw("_MASKMAP", mask != null); mat.SetFloat("_UseMask", mask != null ? 1f : 0f);
            Kw("_GLASS", glass); mat.SetFloat("_Glass", glass ? 1f : 0f);
            mat.SetFloat("_BumpScale", 1f);
            mat.SetFloat("_Metallic", mask != null ? 1f : paint ? spec.paintMetallic : 0f);
            mat.SetFloat("_Smoothness", mask != null ? 1f : paint ? spec.paintSmoothness : 0.92f);
            mat.SetFloat("_OcclusionStrength", 0.85f);
            mat.SetFloat("_Reflection", 1f);
            mat.SetFloat("_TintStrength", paint ? 1f : 0f);
            mat.SetVector("_GroundAO", new Vector4(0.5f, 0.3f, 0, 0));
            mat.SetFloat("_Cutoff", 0f);
            mat.SetFloat("_Cull", 2f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static int LodIndex(string name)
        {
            int k = name.LastIndexOf("_LOD", StringComparison.Ordinal);
            return k >= 0 && int.TryParse(name.Substring(k + 4), out var i) ? i : 0;
        }

        private static bool CopyIfChanged(string src, string dst)
        {
            if (!File.Exists(src)) { Debug.LogWarning("CTLIFE [islandv2] kit source missing " + src); return false; }
            if (File.Exists(dst) && new FileInfo(dst).Length == new FileInfo(src).Length && File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(src))) return false;
            File.Copy(src, dst, true);
            return true;
        }

        private static void ModelImport(string path, bool v3 = false)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) return;
            bool dirty = false;
            void Set<T>(T cur, T want, Action<T> set) { if (!EqualityComparer<T>.Default.Equals(cur, want)) { set(want); dirty = true; } }
            Set(mi.materialImportMode, ModelImporterMaterialImportMode.None, v => mi.materialImportMode = v);
            Set(mi.importAnimation, false, v => mi.importAnimation = v);
            Set(mi.animationType, ModelImporterAnimationType.None, v => mi.animationType = v);
            Set(mi.importBlendShapes, false, v => mi.importBlendShapes = v);
            Set(mi.importCameras, false, v => mi.importCameras = v);
            Set(mi.importLights, false, v => mi.importLights = v);
            Set(mi.isReadable, false, v => mi.isReadable = v);
            // v3 PBR models carry normal maps: tangents from the mesh (MikkTSpace, as Blender baked them)
            Set(mi.importTangents, v3 ? ModelImporterTangents.CalculateMikk : ModelImporterTangents.None, v => mi.importTangents = v);
            Set(mi.meshCompression, ModelImporterMeshCompression.Low, v => mi.meshCompression = v);
            Set(mi.addCollider, false, v => mi.addCollider = v);
            if (dirty) mi.SaveAndReimport();
        }

        private static void TextureImport(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            string file = Path.GetFileNameWithoutExtension(path);
            string before = EditorJsonUtility.ToJson(ti) + JsonUtility.ToJson(ti.GetPlatformTextureSettings("Android"));
            if (V3Texture(ti, file))
            {
                // v3 atlases are big (2048 ASTC): re-encode only when a setting changed
                if (before != EditorJsonUtility.ToJson(ti) + JsonUtility.ToJson(ti.GetPlatformTextureSettings("Android"))) ti.SaveAndReimport();
                return;
            }
            bool palette = path.Contains("palette") || path.Contains("colormap") || path.Contains("citybits");
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = !palette;
            ti.filterMode = palette ? FilterMode.Point : FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            var a = ti.GetPlatformTextureSettings("Android");
            // Tripo statue textures: painted, noisy detail where ASTC 6x6 is indistinguishable at statue distance and 56 % smaller
            bool tripo = Path.GetFileName(path).StartsWith("tripo__", StringComparison.Ordinal);
            a.overridden = true; a.maxTextureSize = 1024; a.format = tripo ? TextureImporterFormat.ASTC_6x6 : TextureImporterFormat.ASTC_4x4;
            ti.SetPlatformTextureSettings(a);
            ti.SaveAndReimport();
        }

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        /// <summary>
        /// World v3 texture budget on the Seeker (ASTC 6x6): building colour atlases 2048 (a whole building, every side,
        /// in one atlas), their normal maps 1024 and mask maps 512; facade cards and props 1024 colour, 512 normal, 256 mask;
        /// far boxes and billboards 512. Normal maps import as normal maps (OpenGL, Unity's convention), mask maps linear.
        /// </summary>
        private static bool V3Texture(TextureImporter ti, string file)
        {
            bool building = file.StartsWith("nc_building") || file.StartsWith("m99_") || file.StartsWith("dz_") || file.StartsWith("lg_");
            bool v3 = building || file.StartsWith("nc_") || file.StartsWith("ph_") || file.StartsWith("v3_") || file.StartsWith("tj_") || file.StartsWith("lp_")
                || file.StartsWith("pv_") || file.StartsWith("yd_");
            if (!v3) return false;
            bool normal = file.EndsWith("_Normal") || file == "nc_trees_01" || file == "nc_trees_04" || file == "nc_trees_07";
            bool mask = file.EndsWith("_MaskMap");
            bool far = file.EndsWith("_lod2_BaseColor") || file.EndsWith("_billboard");
            bool cards = file.EndsWith("_cards_BaseColor");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal && !mask;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = building && !far ? 2 : 1;
            ti.wrapMode = file.StartsWith("nc_trees_") || file == "lp_maple_bark" || file == "lp_maple_leaves" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            bool cut = file.EndsWith("_billboard") || file == "nc_trees_00" || file == "nc_trees_05" || file == "lp_maple_leaves" || file == "pv_ground_cards"
                || file.EndsWith("_cutout_BaseColor");
            // colour dilated into the clear atlas texels (alphaIsTransparency) for every colour map: mips no longer bleed the
            // padding's black/white into UV island edges ("texture to their edges", 2026-10-22)
            ti.alphaIsTransparency = !normal && !mask;
            ti.mipMapsPreserveCoverage = cut;
            if (cut) ti.alphaTestReferenceValue = 0.45f;
            int size = far ? 512 : mask ? (building ? 512 : 256) : normal ? (building ? 1024 : 512) : building && !cards || file == "pv_ground_cards" ? 2048 : 1024;
            ti.maxTextureSize = 2048;
            var a = ti.GetPlatformTextureSettings("Android");
            a.overridden = true; a.maxTextureSize = size; a.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(a);
            return true;
        }

        /// <summary>The Neighbourhood City trees keep their source materials (no M_ prefix): base colour, normal.</summary>
        private static readonly Dictionary<string, (string tex, string role)> V3TreeMaterials = new Dictionary<string, (string, string)>
        {
            ["Wall_3.056_7"] = ("nc_trees_00", "leaves"),       // grass and bamboo cut-outs, planter swatches
            ["Wall_3.056_15"] = ("nc_trees_02", "tree"),        // bark
            ["Wall_3.056_14"] = ("nc_trees_05", "leaves"),      // canopy cut-outs
            ["lp_maple_bark"] = ("lp_maple_bark", "tree"),      // LOLIPOP maples: tiling bark, leaf-cluster cut-outs
            ["lp_maple_leaves"] = ("lp_maple_leaves", "leaves"),
            ["pv_ground_cards"] = ("pv_ground_cards", "grass"),       // Poly Haven plants rendered to cut-out cards
            ["pv_bush_leaves"] = ("lp_maple_leaves", "shrub"),         // the maple's leaf sprays as shrub cards
        };

        /// <summary>
        /// World v3 materials. M_&lt;x&gt;: PBR (&lt;x&gt;_BaseColor, &lt;x&gt;_Normal, &lt;x&gt;_MaskMap) on CT Life/World/Kit PBR;
        /// M_&lt;x&gt;_glass: the same atlas as opaque window glass; M_&lt;x&gt;_cards and M_&lt;x&gt;_lod2: base colour only
        /// (facade cards two-sided). Tree foliage and billboards stay on the toon kit shader (wind, cut-out), toon off.
        /// </summary>
        private static Material V3Material(string fbxMaterial, string kind, string id)
        {
            string key = "v3__" + fbxMaterial;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var path = $"{KitDir}/Materials/{key}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool billboard = fbxMaterial.EndsWith("_billboard");
            if (V3TreeMaterials.TryGetValue(fbxMaterial, out var tm) || billboard)
            {
                var sh = Shader.Find("CT Life/World/Kit") ?? throw new InvalidOperationException("CT Life/World/Kit shader missing");
                if (mat == null) { mat = new Material(sh) { name = key }; AssetDatabase.CreateAsset(mat, path); }
                mat.shader = sh;
                string tex = billboard ? fbxMaterial.Substring(2) : tm.tex;
                string role = billboard ? "billboard" : tm.role;
                mat.SetTexture("_BaseMap", FindTexture(tex));
                // the ground-cover cards are rendered albedo of darkish scanned plants: lifted to sit in the lawn's tone
                mat.SetColor("_BaseColor", role == "grass" ? new Color(1.45f, 1.5f, 1.35f) : role == "shrub" ? new Color(1.3f, 1.35f, 1.2f) : Color.white);
                mat.SetFloat("_Toon", 0f);
                mat.DisableKeyword("_TINTPALETTE"); mat.SetFloat("_TintPalette", 0f);
                mat.DisableKeyword("_TINTALL"); mat.SetFloat("_TintAll", 0f);
                mat.SetVector("_Wind", role == "leaves" ? new Vector4(0.06f, 1.3f, 5f, 0) : role == "tree" ? new Vector4(0.03f, 1.1f, 5f, 0)
                    : role == "grass" ? new Vector4(0.035f, 1.7f, 0.45f, 0) : role == "shrub" ? new Vector4(0.025f, 1.4f, 1.6f, 0) : Vector4.zero);
                mat.SetVector("_GroundAO", role == "grass" ? new Vector4(0.25f, 0.2f, 0, 0) : new Vector4(0.8f, 0.3f, 0, 0));
                bool cut = role != "tree";
                mat.SetFloat("_Cutoff", cut ? (role == "grass" ? 0.4f : 0.45f) : 0f);
                mat.SetFloat("_Cull", cut ? 0f : 2f);
            }
            else
            {
                var sh = Shader.Find("CT Life/World/Kit PBR") ?? throw new InvalidOperationException("CT Life/World/Kit PBR shader missing");
                if (mat == null) { mat = new Material(sh) { name = key }; AssetDatabase.CreateAsset(mat, path); }
                mat.shader = sh;
                string stem = fbxMaterial.StartsWith("M_") ? fbxMaterial.Substring(2) : fbxMaterial;
                bool glass = stem.EndsWith("_glass");
                bool flat = stem.EndsWith("_cards") || stem.EndsWith("_lod2");
                if (glass) stem = stem.Substring(0, stem.Length - "_glass".Length);
                mat.SetTexture("_BaseMap", FindTexture(stem + "_BaseColor"));
                // the far box has no mask (no baked AO, no normal-map relief): a touch darker so it matches LOD1's tone
                mat.SetColor("_BaseColor", stem.EndsWith("_lod2") ? new Color(0.84f, 0.84f, 0.84f, 1f) : Color.white);
                Texture2D nrm = flat ? null : FindTextureOrNull(stem + "_Normal"), msk = flat ? null : FindTextureOrNull(stem + "_MaskMap");
                mat.SetTexture("_BumpMap", nrm);
                mat.SetTexture("_MaskMap", msk);
                void Kw(string k, bool on) { if (on) mat.EnableKeyword(k); else mat.DisableKeyword(k); }
                Kw("_NORMALMAP", nrm != null); mat.SetFloat("_UseNormal", nrm != null ? 1f : 0f);
                Kw("_MASKMAP", msk != null); mat.SetFloat("_UseMask", msk != null ? 1f : 0f);
                Kw("_GLASS", glass); mat.SetFloat("_Glass", glass ? 1f : 0f);
                mat.SetFloat("_BumpScale", 1f);
                mat.SetFloat("_Metallic", msk != null ? 1f : 0f);
                mat.SetFloat("_TintStrength", 1f);
                // with a mask its alpha is the smoothness (scale 1); flat cards and far boxes: a matt plaster/brick value
                mat.SetFloat("_Smoothness", msk != null ? 1f : 0.18f);
                mat.SetFloat("_OcclusionStrength", 1f);
                mat.SetFloat("_Reflection", 1f);
                mat.SetVector("_GroundAO", kind == "building" ? new Vector4(2.6f, 0.4f, 0, 0) : new Vector4(0.6f, 0.25f, 0, 0));
                // world polish: "_cutout" atlases (the chain-link wire) are alpha-tested and two-sided
                bool cutout = stem.EndsWith("_cutout");
                mat.SetFloat("_Cutoff", glass ? 0.08f : cutout ? 0.5f : 0f);
                mat.SetFloat("_Cull", stem.EndsWith("_cards") || glass || cutout ? 0f : 2f);
            }
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            Cache[key] = mat;
            return mat;
        }

        private static Texture2D FindTextureOrNull(string tex) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{KitDir}/Textures/{tex}.png");

        /// <summary>The kit material for an FBX material name and a kind (billboard frames, other one-off props).</summary>
        public static Material KitMaterial(string fbxMaterial, string kind, string id) => Material(fbxMaterial, kind, id);

        /// <summary>One kit material per (texture, role): palette (tintable facades), foliage (wind, cut-out), plain.</summary>
        private static Material Material(string fbxMaterial, string kind, string id)
        {
            string tex = fbxMaterial.StartsWith("M_") ? fbxMaterial.Substring(2) : fbxMaterial;
            bool foliage = kind == "tree" || kind == "bush" || kind == "grass" || kind == "crop";
            bool leaves = tex.IndexOf("leaves", StringComparison.OrdinalIgnoreCase) >= 0 || tex.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0;
            string role = foliage ? (kind == "tree" ? (leaves ? "leaves" : "tree") : kind) : kind == "hero" ? "hero" : "plain";
            string key = tex + "__" + role;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var path = $"{KitDir}/Materials/{key}.mat";
            var sh = Shader.Find("CT Life/World/Kit") ?? throw new InvalidOperationException("CT Life/World/Kit shader missing");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(sh) { name = key }; AssetDatabase.CreateAsset(mat, path); }
            mat.shader = sh;
            mat.SetTexture("_BaseMap", FindTexture(tex));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Toon", 0.4f);
            bool tint = tex == "world_palette";
            if (tint) mat.EnableKeyword("_TINTPALETTE"); else mat.DisableKeyword("_TINTPALETTE");
            mat.SetFloat("_TintPalette", tint ? 1f : 0f);
            bool tintAll = foliage && (role == "tree" || role == "bush" || role == "leaves") || kind == "hero";
            if (tintAll) mat.EnableKeyword("_TINTALL"); else mat.DisableKeyword("_TINTALL");
            mat.SetFloat("_TintAll", tintAll ? 1f : 0f);
            var wind = role switch
            {
                "tree" => new Vector4(0.10f, 1.1f, 9f, 0), "leaves" => new Vector4(0.22f, 1.3f, 8f, 0), "bush" => new Vector4(0.05f, 1.6f, 1.4f, 0),
                "grass" => new Vector4(0.06f, 2.2f, 0.6f, 0), "crop" => new Vector4(0.08f, 1.8f, 1.6f, 0), _ => Vector4.zero,
            };
            mat.SetVector("_Wind", wind);
            mat.SetVector("_GroundAO", kind == "building" ? new Vector4(2.6f, 0.5f, 0, 0) : new Vector4(1.0f, 0.36f, 0, 0));   // contact darkening at the base
            mat.SetFloat("_Cutoff", leaves || role == "grass" ? 0.45f : 0f);
            mat.SetFloat("_Cull", leaves || role == "grass" || role == "crop" ? 0f : 2f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            Cache[key] = mat;
            return mat;
        }

        /// <summary>
        /// Renders a tree's LOD0 (albedo only: unlit copies of its materials, alpha cut-out) from the front and the side
        /// into one 512 x 512 atlas (Kit/Textures/imp_&lt;id&gt;.png, colours dilated into the clear texels so mips keep no
        /// dark fringe) and returns two crossed quads (normals up, so the kit shader lights them like the canopy top)
        /// and a cut-out, double-sided, tintable kit material for them.
        /// </summary>
        private static (Mesh, Material) BakeImpostor(IslandKit.Proto p)
        {
            var mesh0 = p.lods[0];
            if (mesh0 == null) return (null, null);
            var b = mesh0.bounds;
            float h = b.max.y, wv = Mathf.Max(b.size.x, b.size.z) * 1.04f;
            float size = Mathf.Max(h * 1.02f / 2f, wv);           // ortho half-height; the view is 0.5 wide so its width is 'size'
            const int RW = 256, RH = 512, L = 31;
            var go = new GameObject("impostor_bake") { layer = L, hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<MeshFilter>().sharedMesh = mesh0;
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var mats = new Material[mesh0.subMeshCount];
            for (int i = 0; i < mats.Length; i++)
            {
                var src = i < p.materials.Length && p.materials[i] != null ? p.materials[i] : p.material;
                var m = new Material(unlit);
                m.SetTexture("_BaseMap", src != null ? src.GetTexture("_BaseMap") : Texture2D.whiteTexture);
                m.SetColor("_BaseColor", Color.white);
                bool clip = src != null && src.GetFloat("_Cutoff") > 0f;
                m.SetFloat("_AlphaClip", clip ? 1f : 0f); m.SetFloat("_Cutoff", 0.45f);
                if (clip) m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f);
                mats[i] = m;
            }
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            var camGo = new GameObject("impostor_cam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = size; cam.aspect = 0.5f;
            cam.cullingMask = 1 << L; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
            var data = cam.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.renderShadows = false;
            var rt = new RenderTexture(RW, RH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var atlas = new Texture2D(RW * 2, RH, TextureFormat.RGBA32, false);
            for (int v = 0; v < 2; v++)
            {
                var dir = v == 0 ? Vector3.forward : Vector3.right;   // front view looks along +z, side view along +x
                cam.transform.position = new Vector3(0f, size, 0f) - dir * 60f;
                cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                atlas.ReadPixels(new Rect(0, 0, RW, RH), v * RW, 0);
                RenderTexture.active = prev;
            }
            atlas.Apply();
            Object.DestroyImmediate(camGo); Object.DestroyImmediate(go); rt.Release(); Object.DestroyImmediate(rt);
            foreach (var m in mats) Object.DestroyImmediate(m);
            Dilate(atlas, 8);
            var texPath = $"{KitDir}/Textures/imp_{p.id}.png";
            File.WriteAllBytes(texPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.alphaIsTransparency = true;
            ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.45f; ti.wrapMode = TextureWrapMode.Clamp;
            var a = ti.GetPlatformTextureSettings("Android"); a.overridden = true; a.maxTextureSize = 512; a.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(a); ti.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            // the mesh: quad 0 in the XY plane (front view), quad 1 in the ZY plane (side view), each 'size' wide, 2*size tall
            var mb = new MeshBuilder();
            float hw = size * 0.5f, top = size * 2f;
            void Quad(Vector3 right, float u0)
            {
                int i0 = mb.Add(-right * hw, Vector3.up, new Vector2(u0, 0f)), i1 = mb.Add(-right * hw + Vector3.up * top, Vector3.up, new Vector2(u0, 1f));
                int i2 = mb.Add(right * hw + Vector3.up * top, Vector3.up, new Vector2(u0 + 0.5f, 1f)), i3 = mb.Add(right * hw, Vector3.up, new Vector2(u0 + 0.5f, 0f));
                mb.Tri(i0, i1, i2); mb.Tri(i0, i2, i3);
            }
            Quad(Vector3.right, 0f);
            Quad(Vector3.back, 0.5f);     // seen from -x looking +x, its right is -z
            var mesh = mb.ToMesh("imp_" + p.id);
            var meshPath = $"{KitDir}/Models/imp_{p.id}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (old != null) { EditorUtility.CopySerialized(mesh, old); mesh = old; } else AssetDatabase.CreateAsset(mesh, meshPath);
            var matPath = $"{KitDir}/Materials/imp_{p.id}.mat";
            var sh = Shader.Find("CT Life/World/Kit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = sh;
            mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", Color.white); mat.SetFloat("_Toon", 0.4f);
            mat.EnableKeyword("_TINTALL"); mat.SetFloat("_TintAll", 1f); mat.DisableKeyword("_TINTPALETTE"); mat.SetFloat("_TintPalette", 0f);
            mat.SetVector("_Wind", Vector4.zero); mat.SetVector("_GroundAO", new Vector4(1.0f, 0.3f, 0, 0));
            mat.SetFloat("_Cutoff", 0.45f); mat.SetFloat("_Cull", 0f); mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return (mesh, mat);
        }

        /// <summary>Pushes opaque colours outwards into transparent texels (alpha stays 0), n rings.</summary>
        private static void Dilate(Texture2D t, int n)
        {
            int w = t.width, h = t.height;
            var px = t.GetPixels32();
            var filled = px.Select(c => c.a > 8).ToArray();
            for (int it = 0; it < n; it++)
            {
                var next = (bool[])filled.Clone();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (filled[i]) continue;
                        int r = 0, g = 0, bl = 0, k = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                                int j = yy * w + xx;
                                if (!filled[j]) continue;
                                r += px[j].r; g += px[j].g; bl += px[j].b; k++;
                            }
                        if (k == 0) continue;
                        px[i] = new Color32((byte)(r / k), (byte)(g / k), (byte)(bl / k), 0);
                        next[i] = true;
                    }
                filled = next;
            }
            t.SetPixels32(px); t.Apply();
        }

        private static Texture2D FindTexture(string tex)
        {
            foreach (var p in new[] { $"{KitDir}/Textures/{tex}.png", $"Assets/Art/environment/_shared/textures/{tex}.png", $"Assets/Art/environment/_shared/{tex}.png" })
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (t != null) return t;
            }
            Debug.LogWarning("CTLIFE [islandv2] kit texture not found: " + tex);
            return Texture2D.whiteTexture;
        }
    }
}
