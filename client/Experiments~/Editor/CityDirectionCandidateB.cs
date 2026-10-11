using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CTLife.Core;
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
    /// Isolated, sourced Timeline composition study. Does not publish a venue, edit its contract,
    /// overwrite original assets or imply that the illustrated activity pockets are live services.
    /// Stage and bake in separate coordinator-owned batch processes; verify external hashes after exit.
    /// </summary>
    public static class CityDirectionCandidateB
    {
        public const string Output = "Assets/Art/Candidates/CityDirection20261006B";
        public const string PrefabPath = Output + "/TimelineStreetB.prefab";
        private const string Canonical = "Assets/Art/Generated/Venues/TimelineSquare.prefab";
        private const string Snapshot = "Assets/Resources/Contract/config_snapshot.json";
        private const string Manifest = "Assets/Art/environment/manifest.json";
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly List<Bounds> Solids = new List<Bounds>();
        private static readonly JArray Placements = new JArray();
        private static JObject _manifest;
        private static Transform _root;
        private static ProtectedFiles _protection;

        [MenuItem("CT Life/Candidates/Stage city street direction B")]
        public static void BuildCandidate() => Protected("stage", Stage);

        [MenuItem("CT Life/Candidates/Bake city street direction B")]
        public static void BakeCandidate() => Protected("bake", () =>
        {
            if (!File.Exists(Output + "/StageReceipt.json")) throw new FileNotFoundException("Stage the candidate first.");
            var stage = JObject.Parse(File.ReadAllText(Output + "/StageReceipt.json"));
            if ((string)stage["status"] != "staged_unbaked_not_published" || (string)stage["canonical_sha256"] != Hash(Canonical))
                throw new InvalidDataException("Candidate receipt is invalid or its canonical source changed; stage again.");
            VenueLightingBuilder.BakeVenue(PrefabPath, Output + "/Lighting");
            var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var maps = candidate != null ? candidate.GetComponent<VenueLightmaps>() : null;
            if (maps == null || maps.colors.Length == 0 || maps.bindings.Length == 0)
                throw new InvalidDataException("Candidate bake did not produce bound lightmaps.");
            File.WriteAllText(Output + "/BakeReceipt.json", new JObject
            {
                ["status"] = "baked_not_published_requires_visual_and_device_review", ["prefab"] = PrefabPath,
                ["lightmaps"] = maps.colors.Length, ["bound_renderers"] = maps.bindings.Length,
                ["prefab_sha256"] = Hash(PrefabPath), ["canonical_sha256"] = Hash(Canonical)
            }.ToString());
        });

        private static void Stage()
        {
            foreach (string sub in new[] { "Materials", "Meshes", "Textures" }) Directory.CreateDirectory(Output + "/" + sub);
            AssetDatabase.Refresh();
            _manifest = JObject.Parse(File.ReadAllText(Manifest));
            Materials.Clear(); Solids.Clear(); Placements.Clear();
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(Canonical);
            if (original == null) throw new FileNotFoundException("Timeline baseline missing", Canonical);
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = Object.Instantiate(original);
                root.name = "Timeline Street B - connected city frontages";
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
                _root = root.transform;
                var venue = root.GetComponent<VenueSceneRoot>();
                if (venue == null || venue.venueId != "timeline_square") throw new InvalidDataException("Unexpected baseline identity.");
                var beforeAnchors = Anchors(root);
                string beforeCamera = JsonUtility.ToJson(venue);
                DetachMaterials(root);
                var maps = root.GetComponent<VenueLightmaps>();
                if (maps != null) Object.DestroyImmediate(maps);
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.lightmapIndex = -1;
                foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Static contact shadows").ToArray())
                    Object.DestroyImmediate(t.gameObject);

                // Existing eight gardens become foreground, middle and background layers. Keep center/bench anchors clear.
                Garden(new Vector2(-12, -7), new Vector2(-5.3f, -9.4f), "tree_blossom_4");
                Garden(new Vector2(12, -7), new Vector2(5.3f, -9.4f), "tree_blossom_1");
                Move("street_lamp_single", new Vector2(-17, -11), new Vector2(-3.7f, -11.5f));
                Move("street_lamp_single", new Vector2(17, -11), new Vector2(3.7f, -11.5f));
                // Move the display clear of the new east frontage; its existing viewpoint still faces toward it.
                Move("billboard_large", new Vector2(16.8f, -5), new Vector2(17.5f, 0.4f));
                Move("ad_kiosk", new Vector2(-6.6f, -11.8f), new Vector2(-3.8f, -5.2f));
                Move("crypto_atm", new Vector2(6.6f, -11.5f), new Vector2(3.8f, -5.2f));

                // Asymmetric, linked street addresses. These are existing licensed full building models,
                // not height-stretched copies of one kiosk; their solid bounds still drive navigation.
                Add("shop_2story_sign", -11.5f, -4.1f, scale: .88f, solid: true);
                Add("shop_2story_balcony", -16.8f, -4.1f, scale: .85f, solid: true);
                Add("shop_3story_balcony", 12.1f, -4.1f, scale: .82f, solid: true);
                Add("billboard_wall", -11.5f, -6.71f, y: 2.35f, scale: .80f, screen: "sign_gm_coffee");
                Add("billboard_wall", -16.8f, -7.02f, y: 2.1f, scale: .76f, screen: "sign_cope_bakery");
                Add("billboard_wall", 12.1f, -6.94f, y: 2.45f, scale: .80f, screen: "sign_terminal_cafe");
                // Source awnings contain ground-height posts. Upper storeys remain open as balconies/windows.
                Add("awning_wide", -11.5f, -6.65f, scale: .92f);
                Add("awning_wide", -16.8f, -6.97f, scale: .86f);
                Add("awning_wide", 12.1f, -6.88f, scale: .92f);
                Add("news_ticker", 12.1f, -6.94f, y: 6.9f, scale: .61f, screen: "timeline_screen");
                Add("houseplant_tall", -14.3f, -7.15f, scale: .88f);
                Add("houseplant_tall", 15.2f, -7.0f, scale: .88f);
                var lighting = root.GetComponent<VenueContentLighting>();
                if (lighting == null) throw new InvalidDataException("Baseline venue lighting is missing.");
                lighting.sunColor = new Color(1f,.90f,.80f);
                lighting.sunIntensity = 1.08f;
                lighting.sunAngles = new Vector3(38f,-45f,0f);
                lighting.sky = new Color(.49f,.64f,.79f);
                lighting.equator = new Color(.63f,.62f,.59f);
                lighting.ground = new Color(.31f,.29f,.26f);

                // Café and market pockets frame a generous central promenade. Chairs here are dressing,
                // not advertised as claimable seats: only existing authored benches retain the sit contract.
                CafeTable(-9.2f, -11.0f, 12f);
                CafeTable(-13.6f, -10.7f, -8f);
                CafeTable(9.2f, -11.0f, -12f);
                CafeTable(13.6f, -10.7f, 8f);
                Add("parasol", -13.6f, -10.7f, scale: .76f);
                Add("parasol", 13.6f, -10.7f, scale: .76f);
                Add("sandwich_board", -7.0f, -11.8f, yaw: 12, screen: "neon_nfa");
                Add("sandwich_board", 7.0f, -11.8f, yaw: -12, screen: "cafe_pulse");
                Add("planter_sidewalk", -15.8f, -9.8f, yaw: 90, solid: true);
                Add("planter_sidewalk", 15.8f, -9.8f, yaw: 90, solid: true);
                Add("bush_large_flowers", -15.8f, -9.8f, y: .3f, scale: .7f);
                Add("bush_large_flowers", 15.8f, -9.8f, y: .3f, scale: .7f);
                // Large, shared event landmark stays behind the fountain; the center stage and alley remain walkable.
                Add("pixel_wall", 0, 13.4f, screen: "timeline_screen", solid: true);

                Paving("Entry promenade", new Rect(-1.65f, -15, 3.3f, 14.5f));
                Paving("West cafe terrace", new Rect(-15.7f, -12.45f, 8.1f, 3.7f));
                Paving("East cafe terrace", new Rect(7.6f, -12.45f, 8.1f, 3.7f));

                var snapshot = JObject.Parse(File.ReadAllText(Snapshot));
                var spots = Registries.Parse(snapshot["ids"], snapshot["config"]).SpotsFor(venue.venueId);
                if (spots == null || spots.walkable.Count == 0) throw new InvalidDataException("Positioned Timeline snapshot required.");
                // Existing garden/fountain exclusions plus newly placed solid footprints; clone colliders are updated too.
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)
                    .Where(c => c.gameObject.name == "Navigation obstacle").ToArray()) Object.DestroyImmediate(collider.gameObject);
                foreach (var p in new[] { new Vector2(-5.3f,-9.4f), new Vector2(5.3f,-9.4f), new Vector2(-13.8f,1.7f),
                    new Vector2(14,3), new Vector2(-8.2f,11.4f), new Vector2(8.5f,11.6f), new Vector2(-18.5f,11.4f), new Vector2(18.6f,11.4f) })
                    Solid(new Bounds(new Vector3(p.x, 1, p.y), new Vector3(2.1f, 2, 2.1f)));
                Solid(new Bounds(new Vector3(0, 1, 7.5f), new Vector3(4.5f, 2, 4.5f)));
                Solid(new Bounds(new Vector3(-3.8f, 1, -5.2f), new Vector3(1.5f, 2, .6f)));
                Solid(new Bounds(new Vector3(3.8f, 1, -5.2f), new Vector3(.8f, 2, .9f)));
                // The original framing is retained; root's reviewCloseCamera flag enables matched A/B testing.
                if (beforeCamera != JsonUtility.ToJson(venue)) throw new InvalidDataException("Unexpected candidate camera/identity change before navigation rebuild.");
                venue.navigation = Navigation(spots);
                venue.Init(spots);
                if (!beforeAnchors.OrderBy(p => p.Key).SequenceEqual(Anchors(root).OrderBy(p => p.Key)))
                    throw new InvalidDataException("Candidate moved an existing server-facing spot.");
                var nav = CheckNavigation(venue, spots);
                CheckMaterials(root);
                if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null) throw new IOException("Candidate save failed.");
                SaveCandidateAssets();
                File.WriteAllText(Output + "/StageReceipt.json", new JObject
                {
                    ["status"] = "staged_unbaked_not_published", ["canonical_sha256"] = Hash(Canonical),
                    ["snapshot_sha256"] = Hash(Snapshot), ["prefab"] = PrefabPath,
                    ["preserved_positioned_spots"] = beforeAnchors.Count, ["navigation"] = nav,
                    ["placements"] = Placements.DeepClone(), ["new_lod0_triangles_manifest_estimate"] = Placements.Sum(p => (int)p["lod0_triangles"]),
                    ["limitations"] = "No live cafe/market interactions added; decorative table chairs do not claim sit spots. Native visual and thermal review pending."
                }.ToString());
                Debug.Log("CTLIFE [city-direction-b] staged asymmetric linked frontages, calm wood cafe pockets and event landmark; bake and visual review required: " + PrefabPath);
            }
            finally { _root = null; EditorSceneManager.ClosePreviewScene(preview); Materials.Clear(); }
        }

        private static void CafeTable(float x, float z, float yaw)
        {
            var table = Add("home_table_roundsmall", x, z, yaw: yaw, scale:.85f, solid: true);
            var left = Add("home_chair_1", x - .91f, z, yaw: 90 + yaw, scale:1.08f, solid: true);
            var right = Add("home_chair_1", x + .91f, z, yaw: -90 + yaw, scale:1.08f, solid: true);
            WarmWood(table,"TableWood",new Color(.72f,.58f,.44f));
            WarmWood(left,"ChairWood",new Color(.82f,.68f,.51f));
            WarmWood(right,"ChairWood",new Color(.82f,.68f,.51f));
            Add("laptop", x, z, yaw: yaw, y: .638f);
            Add("cup_coffee", x + .23f, z - .1f, y: .638f);
        }
        private static void WarmWood(GameObject item,string key,Color tint)
        {
            // A material copy local to these meshes: the source palette/atlas and other props are untouched.
            foreach(var renderer in item.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(original =>
                {
                    string id = key + "_" + original.name;
                    if (Materials.TryGetValue(id,out var cached)) return cached;
                    var copy = new Material(original) { name=id };
                    copy.SetColor("_BaseColor",tint);
                    if(copy.HasProperty("_Smoothness"))copy.SetFloat("_Smoothness",.07f);
                    Materials[id]=Save(copy,Output + "/Materials/" + id + ".mat");
                    return Materials[id];
                }).ToArray();
        }
        private static void Garden(Vector2 from, Vector2 to, string tree)
        {
            Move("planter_round", from, to); Move(tree, from, to);
            Move("bush_large_flowers", from + new Vector2(1.7f, .2f), to + new Vector2(1.7f, .2f));
            Move("flower_clump_2", from + new Vector2(-.8f, -.72f), to + new Vector2(-.8f, -.72f));
        }
        private static void Move(string name, Vector2 from, Vector2 to)
        {
            var found = _root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name &&
                Vector2.Distance(new Vector2(t.position.x, t.position.z), from) < .03f).ToArray();
            if (found.Length != 1) throw new InvalidDataException("Expected one baseline " + name + " at " + from + "; found " + found.Length);
            found[0].position += new Vector3(to.x - from.x, 0, to.y - from.y);
        }
        private static GameObject Add(string id, float x, float z, float yaw = 0, float y = 0, float scale = 1, string screen = null, bool solid = false)
        {
            var row = _manifest[id] ?? throw new InvalidDataException("Unknown sourced asset " + id);
            string path = "Assets/Art/" + (string)row["fbx"];
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (original == null) throw new FileNotFoundException("Sourced model missing", path);
            var item = Object.Instantiate(original, _root);
            item.name = "Direction_" + id;
            item.transform.localPosition = new Vector3(x,y,z);
            item.transform.localRotation = Quaternion.Euler(0,yaw,0);
            item.transform.localScale = Vector3.one * scale;
            foreach (var c in item.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var group in item.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(group);
            var renderers = item.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                r.sharedMaterials = r.sharedMaterials.Select(m => Resolve(m != null ? m.name : "M_ctl_palette", row, screen)).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.gameObject.isStatic = true;
            }
            var levels = renderers.GroupBy(r => Lod(r.name)).OrderBy(g => g.Key).ToArray();
            if (levels.Length > 1)
            {
                var group = item.AddComponent<LODGroup>();
                group.SetLODs(levels.Select((g, i) => new LOD(i == levels.Length - 1 ? .002f : i == 0 ? .065f : .025f, g.Cast<Renderer>().ToArray())).ToArray());
                group.fadeMode = LODFadeMode.None; group.RecalculateBounds();
            }
            if (solid)
            {
                var bounds = renderers[0].bounds;
                foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                // Renderer bounds include every LOD; y is deliberately tall for nav exclusion, while XZ retain real source size.
                Solid(new Bounds(new Vector3(bounds.center.x, 1.2f, bounds.center.z), new Vector3(bounds.size.x, 2.4f, bounds.size.z)));
            }
            Placements.Add(new JObject { ["id"] = id, ["x"] = x, ["y"] = y, ["z"] = z, ["yaw"] = yaw, ["scale"] = scale,
                ["screen"] = screen, ["solid"] = solid, ["source"] = (string)row["source"], ["lod0_triangles"] = (int)row["tris"][0] });
            return item;
        }
        private static int Lod(string name)
        {
            int i = name.LastIndexOf("_LOD", StringComparison.Ordinal);
            return i >= 0 && i + 4 < name.Length && char.IsDigit(name[i + 4]) ? name[i + 4] - '0' : 0;
        }
        private static Material Resolve(string name, JToken row, string screen)
        {
            name = name.Replace(" (Instance)", "");
            int suffix = name.LastIndexOf('.');
            if (suffix >= 0 && int.TryParse(name.Substring(suffix + 1), out _)) name = name.Substring(0,suffix);
            bool display = name.StartsWith("KEEP_ad_slot") || name.StartsWith("KEEP_screen");
            string key = display ? "Screen_" + (screen ?? "timeline_screen") : name;
            if (Materials.TryGetValue(key, out var cached)) return cached;
            var shader = Shader.Find(display ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Simple Lit");
            if (shader == null) throw new InvalidDataException("Required URP shader missing.");
            var material = new Material(shader) { name = key, enableInstancing = true };
            material.SetColor("_BaseColor", Color.white);
            Texture2D texture = null;
            if (display) texture = Screen(screen ?? "timeline_screen");
            else if (name == "M_ctl_palette") texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/environment/_shared/ctl_palette.png");
            else if (name.StartsWith("M_"))
            {
                string stem = name.Substring(2);
                string tex = (row["textures"] as JArray)?.Values<string>().FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == stem);
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/" + (tex ?? "environment/_shared/textures/" + stem + ".png"));
            }
            if (texture != null) material.SetTexture("_BaseMap", texture);
            else if (name.StartsWith("M_")) throw new InvalidDataException("Missing sourced texture " + name);
            material.SetFloat("_Smoothness", .12f);
            if (name.Contains("glass")) material.SetColor("_BaseColor", new Color(.39f,.64f,.72f));
            if (name.Contains("neon")) material.SetColor("_BaseColor", new Color(.26f,.9f,.74f));
            string lower = name.ToLowerInvariant();
            if (new[] { "leaves", "leaf_", "flowers", "grass", "bush" }.Any(lower.Contains))
            {
                material.SetFloat("_AlphaClip", 1); material.SetFloat("_Cutoff", .45f); material.SetFloat("_Cull", (float)CullMode.Off);
                material.EnableKeyword("_ALPHATEST_ON"); material.SetOverrideTag("RenderType", "TransparentCutout"); material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            Materials[key] = Save(material, Output + "/Materials/" + key + ".mat");
            return Materials[key];
        }
        private static Texture2D Screen(string id)
        {
            string path = Output + "/Textures/" + id + ".png";
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/lookdev/tex", id + ".png"));
            File.Copy(source, path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidDataException("Missing candidate texture importer.");
            importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp; importer.maxTextureSize = 1024;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true; android.maxTextureSize = 1024; android.format = TextureImporterFormat.ASTC_8x8;
            importer.SetPlatformTextureSettings(android); importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        private static void Paving(string name, Rect rect)
        {
            var material = _root.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(m =>
                m != null && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null && m.GetTexture("_BaseMap").name.Contains("hexagonal_concrete_paving"));
            if (material == null) throw new InvalidDataException("Existing sourced hex paving material is missing.");
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { new Vector3(rect.xMin,.016f,rect.yMin), new Vector3(rect.xMin,.016f,rect.yMax), new Vector3(rect.xMax,.016f,rect.yMax), new Vector3(rect.xMax,.016f,rect.yMin) };
            mesh.triangles = new[] { 0,1,2,0,2,3 };
            mesh.uv = new[] { Vector2.zero, new Vector2(0,rect.height/2), new Vector2(rect.width/2,rect.height/2), new Vector2(rect.width/2,0) };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(name); go.transform.SetParent(_root,false); go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = Save(mesh,Output + "/Meshes/" + name.Replace(' ','_') + ".asset");
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
        private static void Solid(Bounds bounds)
        {
            Solids.Add(bounds);
            var go = new GameObject("Direction solid"); go.transform.SetParent(_root, false); go.transform.localPosition = bounds.center;
            go.AddComponent<BoxCollider>().size = bounds.size;
        }
        private static NavMeshData Navigation(VenueSpots spots)
        {
            var sources = new List<NavMeshBuildSource>();
            foreach (var r in spots.walkable) sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, area = 0,
                transform = Matrix4x4.TRS(new Vector3((r.x0+r.x1)*.5f,-.1f,(r.z0+r.z1)*.5f), Quaternion.identity,Vector3.one),
                size = new Vector3(Mathf.Abs(r.x1-r.x0),.2f,Mathf.Abs(r.z1-r.z0)) });
            foreach (var b in Solids) sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox, area = 1,
                transform = Matrix4x4.TRS(b.center,Quaternion.identity,Vector3.one), size = b.size });
            var nav = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0),sources,new Bounds(new Vector3(0,1,3),new Vector3(46,8,42)),Vector3.zero,Quaternion.identity);
            if (nav == null) throw new InvalidDataException("Candidate navigation generation failed.");
            nav.name = "Timeline Street navigation";
            return Save(nav,Output + "/Meshes/Navigation.asset");
        }
        private static JArray CheckNavigation(VenueSceneRoot venue, VenueSpots spots)
        {
            var offset = new Vector3(10000,0,10000);
            var instance = NavMesh.AddNavMeshData(venue.navigation,offset,Quaternion.identity);
            var rows = new JArray();
            try
            {
                var p = new Vector3(spots.Entrance.x,0,spots.Entrance.z) + offset;
                if (!NavMesh.SamplePosition(p,out var start,.75f,NavMesh.AllAreas)) throw new InvalidDataException("Entrance blocked.");
                foreach (var spot in spots.spots.Values.Distinct().Where(s => s.hasPosition))
                {
                    var point = new Vector3(spot.x,0,spot.z) + offset;
                    if (!NavMesh.SamplePosition(point,out var hit,.75f,NavMesh.AllAreas)) throw new InvalidDataException("Blocked spot " + spot.id);
                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(start.position,hit.position,NavMesh.AllAreas,path) || path.status != NavMeshPathStatus.PathComplete)
                        throw new InvalidDataException("No complete path to " + spot.id);
                    rows.Add(new JObject { ["spot"] = spot.id, ["sample_distance_m"] = Vector3.Distance(point,hit.position), ["path"] = "PathComplete" });
                }
            }
            finally { if (instance.valid) NavMesh.RemoveNavMeshData(instance); }
            return rows;
        }
        private static Dictionary<string,Vector3> Anchors(GameObject root) => root.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(a => a.spotId,a => a.transform.position);
        private static void CheckMaterials(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials)
                if (m == null || m.shader == null || m.shader.name.Contains("InternalError") || !AssetDatabase.GetAssetPath(m).StartsWith(Output + "/",StringComparison.Ordinal))
                    throw new InvalidDataException("Missing or non-isolated material on " + r.name);
        }
        private static void DetachMaterials(GameObject root)
        {
            var copies = new Dictionary<Material,Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = r.sharedMaterials.Select(m =>
            {
                if (m == null) throw new InvalidDataException("Baseline has a missing material.");
                if (copies.TryGetValue(m,out var found)) return found;
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m));
                if (string.IsNullOrEmpty(guid)) throw new InvalidDataException("Baseline material has no persistent identity.");
                var copy = new Material(m) { name = m.name };
                // Baked lightmaps are intentionally discarded. The new bake derives its own Simple Lit inputs.
                copies[m] = Save(copy,Output + "/Materials/Baseline_" + guid + ".mat");
                return copies[m];
            }).ToArray();
            _protection.ClearDirty();
        }
        private static T Save<T>(T value, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(value,path); return value; }
            EditorUtility.CopySerialized(value,existing); Object.DestroyImmediate(value); EditorUtility.SetDirty(existing); return existing;
        }
        private static void SaveCandidateAssets()
        {
            _protection.ClearDirty();
            foreach (string guid in AssetDatabase.FindAssets("",new[] { Output }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".mat",StringComparison.Ordinal) || path.EndsWith(".asset",StringComparison.Ordinal))
                    AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(path));
            }
        }
        private static void Protected(string phase, Action action)
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
                throw new InvalidOperationException("Use an idle, coordinator-owned Unity batch process.");
            if (_protection != null) throw new InvalidOperationException("Another candidate transaction is active.");
            _protection = new ProtectedFiles(phase);
            try { action(); _protection.ClearDirty(); _protection.Verify(); _protection.Record("passed_protected_bytes_unchanged",null); }
            catch (Exception error)
            {
                _protection.Restore(); _protection.Record("failed_original_bytes_restored",error.ToString());
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/" + (phase == "stage" ? "StageReceipt.json" : "BakeReceipt.json"),new JObject { ["status"] = "failed", ["error"] = error.ToString() }.ToString());
                throw;
            }
            finally { _protection = null; }
        }
        private sealed class ProtectedFiles
        {
            private readonly Dictionary<string,string> _hashes = new Dictionary<string,string>();
            private readonly string _backup;
            public ProtectedFiles(string phase)
            {
                _backup = Path.Combine(Path.GetTempPath(),"ctlife-city-direction-b-" + phase + "-" + Guid.NewGuid().ToString("N"));
                var paths = AssetDatabase.GetDependencies(Canonical,true).Concat(new[] { Canonical,Snapshot,Manifest,"Assets/Resources/World/VenueContentRegistry.asset" })
                    .Concat(Directory.GetFiles("ProjectSettings","*",SearchOption.AllDirectories)).Where(File.Exists).Distinct().ToList();
                paths.AddRange(paths.Where(p => File.Exists(p + ".meta")).Select(p => p + ".meta").ToArray());
                foreach (string path in paths.Distinct())
                {
                    string copy = Path.Combine(_backup,"before",path); Directory.CreateDirectory(Path.GetDirectoryName(copy));
                    File.Copy(path,copy); _hashes[path] = Hash(path);
                    if (Hash(copy) != _hashes[path]) throw new IOException("Cannot snapshot " + path);
                }
                Record("captured_after_editor_startup_before_candidate",null);
            }
            public void ClearDirty()
            {
                foreach (string path in _hashes.Keys.Where(p => p.StartsWith("Assets/",StringComparison.Ordinal) && !p.EndsWith(".meta",StringComparison.Ordinal)))
                    foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path)) if (obj != null && EditorUtility.IsDirty(obj)) EditorUtility.ClearDirty(obj);
            }
            public void Verify()
            {
                var changed = _hashes.Where(p => !File.Exists(p.Key) || Hash(p.Key) != p.Value).Select(p => p.Key).ToArray();
                if (changed.Length > 0) throw new InvalidDataException("Protected files changed: " + string.Join(", ",changed));
            }
            public void Restore()
            {
                ClearDirty();
                foreach (var p in _hashes.Where(p => !File.Exists(p.Key) || Hash(p.Key) != p.Value))
                {
                    if (File.Exists(p.Key)) { string failed = Path.Combine(_backup,"after-failure",p.Key); Directory.CreateDirectory(Path.GetDirectoryName(failed)); File.Copy(p.Key,failed); }
                    string source = Path.Combine(_backup,"before",p.Key);
                    if (Hash(source) != p.Value) throw new IOException("Snapshot corrupted: " + p.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(p.Key)); File.Copy(source,p.Key,true);
                }
                Verify(); // No AssetDatabase refresh on recovery: editor must exit, then coordinator rechecks bytes.
            }
            public void Record(string status,string error)
            {
                Directory.CreateDirectory(_backup);
                File.WriteAllText(Path.Combine(_backup,"transaction.json"),new JObject
                {
                    ["status"] = status, ["error"] = error, ["post_editor_exit_hash_check_required"] = true,
                    ["files"] = new JArray(_hashes.Select(p => new JObject { ["path"] = p.Key,["sha256"] = p.Value }))
                }.ToString());
                Debug.Log("CTLIFE [city-direction-b] protection receipt: " + Path.Combine(_backup,"transaction.json"));
            }
        }
        private static string Hash(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
    }
}
