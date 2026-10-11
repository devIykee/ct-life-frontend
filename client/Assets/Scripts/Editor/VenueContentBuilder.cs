using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// Reproducible scene dressing from the imported CC0 environment manifest. Never edits source FBX, the
    /// backend contract, existing scenes, import settings, or hand-authored registry entries for other venues.
    /// Unity -batchmode -quit -projectPath client -executeMethod CTLife.EditorTools.VenueContentBuilder.BuildTimelineSquare
    /// </summary>
    public static class VenueContentBuilder
    {
        private const string Output = "Assets/Art/Generated/Venues";
        private const string RegistryPath = "Assets/Resources/World/VenueContentRegistry.asset";
        private const string ManifestPath = "Assets/Art/environment/manifest.json";
        private const string SnapshotPath = "Assets/Resources/Contract/config_snapshot.json";
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, GameObject> Models = new Dictionary<string, GameObject>();
        private static readonly List<NavMeshBuildSource> NavSources = new List<NavMeshBuildSource>();
        private static readonly List<CombineInstance> ShadowInstances = new List<CombineInstance>();
        private static JObject _manifest;
        private static Transform _root, _paving;
        private static Mesh _shadowQuad;
        private static int _placed;

        [MenuItem("CT Life/Content/Build Timeline Square")]
        public static void BuildTimelineSquare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build venue content outside Play mode.");
            if (!File.Exists(ManifestPath) || !File.Exists(SnapshotPath))
                throw new FileNotFoundException("Import the environment manifest and contract snapshot first.");
            foreach (var directory in new[] { Output, Output + "/Materials", Output + "/Meshes", Output + "/Textures", "Assets/Resources/World" })
                Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            Materials.Clear();
            Models.Clear();
            NavSources.Clear();
            ShadowInstances.Clear();
            _placed = 0;
            _manifest = JObject.Parse(File.ReadAllText(ManifestPath));
            var snapshot = JObject.Parse(File.ReadAllText(SnapshotPath));
            var spots = Registries.Parse(snapshot["ids"], snapshot["config"]).SpotsFor("timeline_square");
            if (spots == null || spots.walkable.Count == 0)
                throw new InvalidOperationException("Timeline Square requires contract walkable rectangles.");

            // The preview scene isolates generation from whichever scene the editor currently has open.
            var preview = EditorSceneManager.NewPreviewScene();
            var go = new GameObject("Timeline Square");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, preview);
            _root = go.transform;
            try
            {
                var venue = go.AddComponent<VenueSceneRoot>();
                venue.venueId = "timeline_square";
                venue.districtId = "timeline";
                venue.cameraBounds = new Rect(-20f, -15f, 40f, 36f);
                venue.cameraDistance = 18.5f;
                venue.cameraPitch = 35f;
                venue.cameraYaw = -8f;
                venue.cameraFieldOfView = 42f;
                venue.cameraLookAhead = Vector3.zero;
                venue.cameraTargetViewportY = 0.39f;
                venue.cameraFocusHeight = 1f;
                go.AddComponent<VenueContentLighting>();
                _paving = Child("Paving - combined by material", _root);

                BuildGround(spots);
                BuildStreet();
                BuildArchitecture();
                BuildGardens();
                BuildLandmarks();
                BuildAnchors(spots);
                CombinePaving();
                BuildContactShadows();
                venue.navigation = BuildNavigation();
                venue.Init(spots);

                const string prefabPath = Output + "/TimelineSquare.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                if (prefab == null) throw new InvalidOperationException("Saving TimelineSquare.prefab failed.");
                var registry = AssetDatabase.LoadAssetAtPath<VenueContentRegistry>(RegistryPath);
                if (registry == null)
                {
                    registry = ScriptableObject.CreateInstance<VenueContentRegistry>();
                    AssetDatabase.CreateAsset(registry, RegistryPath);
                }
                var entry = registry.venues.FirstOrDefault(e => e.venueId == venue.venueId);
                if (entry == null) { entry = new VenueContentRegistry.Entry { venueId = venue.venueId }; registry.venues.Add(entry); }
                entry.prefab = prefab.GetComponent<VenueSceneRoot>();
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssets();
                Validate(prefab.GetComponent<VenueSceneRoot>(), spots);
                ValidateCameraFraming(prefab.GetComponent<VenueSceneRoot>(), spots);
                ValidateHeroDetail(prefab.GetComponent<VenueSceneRoot>(), spots);
                Debug.Log($"CTLIFE [venue-content] Timeline Square: {_placed} sourced asset instances; {Materials.Count} shared materials. " +
                    "Prefab + catalog + navigation saved. Run on-device render/performance checks before calling the venue done.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                if (_shadowQuad != null) Object.DestroyImmediate(_shadowQuad);
                _shadowQuad = null;
                _root = _paving = null;
                Models.Clear();
                Materials.Clear();
                ShadowInstances.Clear();
            }
        }

        private static void BuildGround(VenueSpots spots)
        {
            // The hero floor uses actual 2 m paving modules, with a two-tone border and entry promenade.
            // Tile origins sit 0.101 m below the walking plane (manifest thickness); top faces are y = 0.
            for (int x = -22; x < 22; x += 2)
                for (int z = -16; z < 22; z += 2)
                {
                    bool border = x < -18 || x >= 18 || z < -14 || z >= 14;
                    Add(border ? "plaza_hexpaving_2m" : "plaza_paving_2m", x + 1, z + 1, y: -0.101f, parent: _paving);
                }
            // Supplied 4 m floor modules continue behind the storefronts and below the skyline.
            // This is visual dressing only: the contract collider and NavMesh rectangles below stay exact.
            for (int x = -42; x <= 42; x += 4)
                for (int z = -14; z <= 72; z += 4)
                {
                    if (z < 22 && x > -22 && x < 22) continue;
                    Add("floor_concrete_4m", x, z, y: -0.105f, parent: _paving);
                }
            foreach (var r in spots.walkable)
            {
                var center = new Vector3((r.x0 + r.x1) * 0.5f, -0.1f, (r.z0 + r.z1) * 0.5f);
                var size = new Vector3(Mathf.Abs(r.x1 - r.x0), 0.2f, Mathf.Abs(r.z1 - r.z0));
                var ground = Child("Walkable contract rectangle", _root).gameObject;
                ground.transform.localPosition = center;
                ground.AddComponent<BoxCollider>().size = size;
                NavSources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one),
                    size = size,
                    area = 0,
                });
            }
        }

        private static void BuildStreet()
        {
            for (int x = -24; x <= 24; x += 12)
                // Road is 6 m across after rotation. Its north edge (-19) meets the 3 m sidewalk edge.
                Add("street_2lane", x, -22f, yaw: 90f, y: -0.15f, parent: _paving);
            for (int x = -21; x <= 21; x += 3)
                Add("sidewalk_straight_3m", x, -17.5f, yaw: 90f, y: -0.15f, parent: _paving);
            Add("car_taxi", 11f, -20.8f, yaw: 90f, scale: 0.8f);
            Add("car_sedan", -12f, -23.2f, yaw: -90f, scale: 0.8f);
            // A continuous rear avenue gives the alley a readable destination instead of clear-color void.
            for (int x = -36; x <= 36; x += 12)
                Add("street_2lane", x, 35f, yaw: 90f, y: -0.145f, parent: _paving);
            for (int x = -36; x <= 36; x += 3)
                Add("sidewalk_straight_3m", x, 30.5f, yaw: 90f, y: -0.145f, parent: _paving);
            foreach (float x in new[] { -13f, -7f, 7f, 13f })
                Add("bollard", x, -15.6f);
        }

        private static void BuildArchitecture()
        {
            var north = Child("Solana Street shopfronts", _root);
            // The center alley remains open to the north crier at (0, 18).
            Add("shop_2story_sign", -17.5f, 20f, parent: north);
            Add("shop_3story_balcony", -11.4f, 20f, parent: north);
            Add("shop_2story_columns", -5.3f, 23.5f, parent: north);
            Add("shop_2story_balcony", 5.4f, 23.5f, parent: north);
            Add("shop_2story_wide", 13.6f, 20f, parent: north);
            Add("shop_3story_slim", 21f, 20f, parent: north);
            Add("shop_2story_sign", -25.5f, 8f, yaw: 90f, parent: north);
            Add("shop_1story_sign", -25.5f, 1.7f, yaw: 90f, parent: north);
            Add("shop_2story_columns", 25.5f, 7.5f, yaw: -90f, parent: north);

            // Original CT house art is already licensed/documented under art/lookdev/LICENSES.md.
            Add("billboard_wall", -17.5f, 16.95f, y: 2.5f, screen: "sign_gm_coffee");
            Add("billboard_wall", 13.3f, 16.85f, y: 2.7f, screen: "sign_terminal_cafe");
            Add("news_ticker", -11.4f, 16.55f, y: 7.7f, screen: "timeline_screen");
            Add("billboard_rooftop", 13.4f, 19.3f, y: 7.1f, screen: "bb_froge");
            Add("billboard_wall", -22.4f, 1.7f, yaw: 90f, y: 2.6f, screen: "sign_cope_bakery");

            var skyline = Child("Marina skyline", _root);
            Add("tower_glass_a", -27f, 49f, scale: 0.82f, parent: skyline);
            Add("tower_glass_c", -11f, 56f, scale: 0.85f, parent: skyline);
            Add("tower_glass_b", 4f, 59f, scale: 0.9f, parent: skyline);
            Add("tower_glass_e", 21f, 54f, scale: 0.86f, parent: skyline);
            Add("tower_glass_d", 34f, 47f, scale: 0.75f, parent: skyline);
        }

        private static void BuildGardens()
        {
            var gardens = Child("Blossom gardens", _root);
            var positions = new[]
            {
                new Vector2(-12f, -7f), new Vector2(12f, -7f), new Vector2(-13.8f, 1.7f),
                new Vector2(14f, 3f), new Vector2(-8.2f, 11.4f), new Vector2(8.5f, 11.6f),
                new Vector2(-18.5f, 11.4f), new Vector2(18.6f, 11.4f),
            };
            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                Add("planter_round", p.x, p.y, scale: 1.15f, parent: gardens);
                Add(i % 3 == 0 ? "tree_blossom_4" : i % 3 == 1 ? "tree_blossom_1" : "tree_blossom_2",
                    p.x, p.y, yaw: i * 71f, y: 0.35f, scale: 0.85f, parent: gardens);
                Add("bush_large_flowers", p.x + 1.7f, p.y + 0.2f, yaw: i * 43f, scale: 0.72f, parent: gardens);
                Add("flower_clump_2", p.x - 0.8f, p.y - 0.72f, scale: 1.7f, parent: gardens);
                Obstacle(new Vector3(p.x, 0.9f, p.y), new Vector3(2f, 2f, 2f));
                Contact(p.x, p.y, 3.1f, 2.5f, 0.3f);
            }
            Add("tree_common_4", -20.5f, -8f, scale: 0.75f, parent: gardens);
            Add("tree_blossom_4", 19.5f, -9f, scale: 0.7f, yaw: 35f, parent: gardens);
            // The rear promenade sits beyond the last authoritative walkable rectangle (z = 21).
            foreach (float x in new[] { -30f, -21f, -11f, 0f, 11f, 23f, 32f })
            {
                Add("planter_sidewalk", x, 29.5f, parent: gardens);
                Add("tree_blossom_4", x, 29.5f, y: 0.28f, yaw: x * 11f, parent: gardens);
                Add("bush_large_flowers", x + 1.6f, 28.5f, scale: 0.9f, parent: gardens);
                Contact(x, 29.5f, 3.4f, 3f, 0.3f);
            }
            foreach (var x in new[] { -17f, 17f })
                foreach (var z in new[] { -11f, 8f })
                {
                    Add("street_lamp_single", x, z, parent: gardens);
                    Contact(x, z, 0.6f, 0.6f, 0.3f);
                }
        }

        private static void BuildLandmarks()
        {
            // The contract's center (0,0) stays clear. The fountain sits beyond the meeting area.
            Add("plaza_fountain", 0f, 7.5f);
            Obstacle(new Vector3(0f, 1f, 7.5f), new Vector3(4.5f, 3f, 4.5f));
            Contact(0f, 7.5f, 5.3f, 4.6f, 0.35f);
            Add("pixel_wall", -20.1f, 0f, yaw: 90f, screen: "pixel_wall");
            Add("billboard_large", 16.8f, -5f, yaw: -18f, screen: "bb_gm");
            Add("ad_kiosk", -6.6f, -11.8f, yaw: 8f, screen: "kiosk_chart");
            Add("crypto_atm", 6.6f, -11.5f, yaw: -12f, screen: "cafe_pulse");
            Add("sandwich_board", -9.4f, 15.1f, yaw: 5f, screen: "neon_nfa");
            Add("parasol", 13.7f, 14.6f, scale: 1.1f);
            Add("cafe_table_small", 13.7f, 14.6f);
            Add("cafe_chair_a", 12.8f, 14.6f, yaw: 90f);
            Add("cafe_chair_b", 14.6f, 14.6f, yaw: -90f);
            Add("trash_can", -7.8f, -12f);
            Add("trash_can", 11.5f, 15f);
            Contact(6.6f, -11.5f, 1.4f, 1.1f, 0.38f);
            Contact(-6.6f, -11.8f, 1.8f, 1.2f, 0.38f);
        }

        private static void BuildAnchors(VenueSpots spots)
        {
            var anchors = Child("Contract spots - do not move independently", _root);
            foreach (var spot in spots.spots.Values.Distinct())
            {
                if (!spot.hasPosition) continue;
                var anchor = Child("spot_" + spot.id, anchors);
                anchor.localPosition = new Vector3(spot.x, 0f, spot.z);
                anchor.localRotation = Quaternion.Euler(0f, spot.rotY * Mathf.Rad2Deg, 0f);
                var component = anchor.gameObject.AddComponent<SpotAnchor>();
                component.spotId = spot.id;
                component.kind = spot.kind;
                component.anim = spot.anim ?? "";
                if (spot.kind == "seat")
                {
                    // Bench front is local -Z in the supplied mesh; the seated avatar faces +Z.
                    var offset = anchor.localRotation * new Vector3(0f, 0f, -0.12f);
                    Add("park_bench", spot.x + offset.x, spot.z + offset.z, yaw: spot.rotY * Mathf.Rad2Deg + 180f);
                    Contact(spot.x, spot.z, 1.9f, 0.95f, 0.3f);
                }
            }
        }

        private static Transform Child(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static GameObject Add(string id, float x, float z, float yaw = 0f, float y = 0f, float scale = 1f,
            Transform parent = null, string screen = null)
        {
            var row = _manifest[id] ?? throw new InvalidDataException("Unknown environment id " + id);
            if (!Models.TryGetValue(id, out var model))
            {
                var path = "Assets/Art/" + row["fbx"];
                model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) throw new FileNotFoundException("Missing imported model: " + path);
                Models[id] = model;
            }
            var instance = Object.Instantiate(model, parent != null ? parent : _root);
            instance.name = id;
            instance.transform.localPosition = new Vector3(x, y, z);
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale = Vector3.one * scale;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (var group in instance.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(group);
            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            int maxLod = renderers.Max(r => LodIndex(r.name));
            foreach (var renderer in renderers)
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => ResolveMaterial(m != null ? m.name : "M_ctl_palette", row, screen)).ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.gameObject.isStatic = true;
            }
            renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            if (id == "plaza_fountain") RepairFountainSurface(renderers);
            if (maxLod > 0)
            {
                var lods = new List<LOD>();
                for (int i = 0; i <= maxLod; i++)
                {
                    var level = renderers.Where(r => LodIndex(r.name) == i).Cast<Renderer>().ToArray();
                    if (level.Length > 0) lods.Add(new LOD(LodThreshold(id, i, maxLod, screen != null), level));
                }
                var group = instance.AddComponent<LODGroup>();
                group.SetLODs(lods.ToArray());
                group.fadeMode = LODFadeMode.None; // No doubled alpha-tested foliage during fades.
                group.RecalculateBounds();
            }
            _placed++;
            return instance;
        }

        private static float LodThreshold(string id, int level, int last, bool display)
        {
            if (level == last) return 0.002f;
            // Source LOD1/2 are generic 50%/20% decimations, including textured window/leaf seams.
            // Preserve original detail until those features are small on the actual portrait display.
            // Android's 0.7 bias means .035/.025 correspond to ~134/95 px at 2670 px tall.
            bool architecture = id.StartsWith("shop_") || id.StartsWith("tower_");
            bool foliage = id.StartsWith("tree_");
            if (level == 0) return architecture ? 0.035f : foliage ? 0.025f : display ? 0.012f : 0.02f;
            return architecture ? 0.012f : foliage ? 0.009f : 0.006f;
        }

        private static void RepairFountainSurface(MeshRenderer[] renderers)
        {
            // The supplied source has a stone top and water top both at 0.50 m. Preserve the source FBX;
            // derive a mesh that moves only water vertices upward 4 cm (normal materials/UVs unchanged).
            foreach (var renderer in renderers)
            {
                int water = Array.FindIndex(renderer.sharedMaterials, m => m != null && m.name == "KEEP_water");
                if (water < 0) throw new InvalidDataException("Fountain lost its water material slot.");
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = ReadableCopy(filter.sharedMesh);
                var vertices = mesh.vertices;
                var waterVertices = new HashSet<int>(mesh.GetTriangles(water));
                float oldTop = waterVertices.Max(i => vertices[i].y);
                foreach (int i in waterVertices) vertices[i] += Vector3.up * 0.04f;
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                filter.sharedMesh = SaveMesh(mesh, Output + "/Meshes/FountainSurface_LOD" + LodIndex(renderer.name) + ".asset");
                Debug.Log($"CTLIFE [venue-content] fountain {renderer.name}: water top {oldTop:F3} -> {oldTop + 0.04f:F3} m in mesh space.");
            }
        }

        private static int LodIndex(string name)
        {
            int i = name.LastIndexOf("_LOD", StringComparison.Ordinal);
            return i >= 0 && i + 4 < name.Length && char.IsDigit(name[i + 4]) ? name[i + 4] - '0' : 0;
        }

        private static Material ResolveMaterial(string name, JToken row, string screen)
        {
            name = name.Replace(" (Instance)", "");
            int suffix = name.LastIndexOf('.');
            if (suffix >= 0 && int.TryParse(name.Substring(suffix + 1), out _)) name = name.Substring(0, suffix);
            bool isScreen = name.StartsWith("KEEP_ad_slot") || name.StartsWith("KEEP_screen");
            string key = isScreen && screen != null ? "Screen_" + screen : name;
            if (Materials.TryGetValue(key, out var found)) return found;
            var shader = Shader.Find(isScreen ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Simple Lit");
            if (shader == null) throw new InvalidOperationException("Required URP shader unavailable.");
            var path = Output + "/Materials/" + key + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader) { name = key }; AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.enableInstancing = true;
            material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
            if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 0f);
            Texture2D texture = null;
            if (isScreen) texture = ScreenTexture(screen ?? "timeline_screen");
            else if (name == "M_ctl_palette") texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/environment/_shared/ctl_palette.png");
            else if (name.StartsWith("M_"))
            {
                string stem = name.Substring(2);
                var textures = row["textures"] as JArray;
                var texPath = textures?.Values<string>().FirstOrDefault(t => Path.GetFileNameWithoutExtension(t) == stem);
                if (texPath != null) texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/" + texPath);
                if (texture == null) texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/environment/_shared/textures/" + stem + ".png");
            }
            if (texture != null) material.SetTexture("_BaseMap", texture);
            else if (name.StartsWith("M_")) throw new InvalidDataException("Missing texture for environment material " + name);
            if (name.Contains("water")) material.SetColor("_BaseColor", new Color(0.27f, 0.73f, 0.78f));
            if (name.Contains("glass")) material.SetColor("_BaseColor", new Color(0.39f, 0.64f, 0.72f));
            if (name.Contains("neon"))
            {
                material.SetColor("_BaseColor", new Color(0.26f, 0.9f, 0.74f));
                material.SetColor("_EmissionColor", new Color(0.22f, 0.8f, 0.61f));
                material.EnableKeyword("_EMISSION");
            }
            string lower = name.ToLowerInvariant();
            if (new[] { "leaves", "leaf_", "flowers", "grass", "bush" }.Any(lower.Contains))
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.45f);
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            Materials[key] = material;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D ScreenTexture(string id)
        {
            var source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/lookdev/tex/", id + ".png"));
            var path = Output + "/Textures/" + id + ".png";
            if (!File.Exists(source)) throw new FileNotFoundException("Missing licensed house art " + source);
            if (!File.Exists(path) || !File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(path)))
            {
                File.Copy(source, path, true);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 1024;
                var android = importer.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = 1024;
                android.format = TextureImporterFormat.ASTC_8x8;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void CombinePaving()
        {
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var copies = new Dictionary<Mesh, Mesh>();
            foreach (var filter in _paving.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                var original = filter.sharedMesh;
                if (renderer == null || original == null) continue;
                if (!copies.TryGetValue(original, out var readable))
                {
                    // Editor-only data access is allowed even with Read/Write off. Create a CPU-readable copy
                    // for CombineMeshes, without changing the source model importer or its memory budget.
                    readable = ReadableCopy(original);
                    copies[original] = readable;
                }
                for (int s = 0; s < readable.subMeshCount; s++)
                {
                    var material = renderer.sharedMaterials[s];
                    if (!byMaterial.TryGetValue(material, out var sources)) byMaterial[material] = sources = new List<CombineInstance>();
                    sources.Add(new CombineInstance { mesh = readable, subMeshIndex = s, transform = _root.worldToLocalMatrix * filter.transform.localToWorldMatrix });
                }
            }
            int part = 0;
            foreach (var group in byMaterial)
            {
                var mesh = new Mesh { name = "Timeline paving " + group.Key.name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(group.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                var saved = SaveMesh(mesh, Output + "/Meshes/TimelinePaving_" + part++ + ".asset");
                var child = Child(saved.name, _root);
                child.gameObject.AddComponent<MeshFilter>().sharedMesh = saved;
                var renderer = child.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = group.Key;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                child.gameObject.isStatic = true;
            }
            foreach (var copy in copies.Values) Object.DestroyImmediate(copy);
            Object.DestroyImmediate(_paving.gameObject);
        }

        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Mesh ReadableCopy(Mesh original)
        {
            var mesh = new Mesh { name = original.name + "_derived", indexFormat = original.indexFormat };
            mesh.vertices = original.vertices;
            mesh.normals = original.normals;
            mesh.uv = original.uv;
            mesh.uv2 = original.uv2;
            mesh.subMeshCount = original.subMeshCount;
            for (int s = 0; s < original.subMeshCount; s++) mesh.SetTriangles(original.GetTriangles(s), s);
            return mesh;
        }

        private static void ValidateCameraFraming(VenueSceneRoot venue, VenueSpots spots)
        {
            var entrance = new Vector3(spots.Entrance.x, 0f, spots.Entrance.z);
            float ahead = CameraRig.LookAheadForViewport(venue.cameraDistance, venue.cameraPitch,
                venue.cameraFieldOfView, venue.cameraTargetViewportY, venue.cameraFocusHeight);
            var yaw = Quaternion.Euler(0f, venue.cameraYaw, 0f);
            var rotation = Quaternion.Euler(venue.cameraPitch, venue.cameraYaw, 0f);
            var focus = entrance + yaw * Vector3.forward * ahead + Vector3.up * venue.cameraFocusHeight;
            var position = focus - rotation * Vector3.forward * venue.cameraDistance;
            // Match the connected Seeker's portrait aspect explicitly; log projections for review.
            const float aspect = 1200f / 2670f;
            float tan = Mathf.Tan(venue.cameraFieldOfView * Mathf.Deg2Rad * 0.5f);
            Vector2 Project(Vector3 point)
            {
                var v = Quaternion.Inverse(rotation) * (point - position);
                return new Vector2(0.5f + v.x / (2f * v.z * tan * aspect), 0.5f + v.y / (2f * v.z * tan));
            }
            var feet = Project(entrance);
            var head = Project(entrance + Vector3.up * 1.8f);
            var westRoof = Project(new Vector3(-5.3f, 7.5f, 23.5f));
            var eastRoof = Project(new Vector3(5.4f, 8f, 23.5f));
            if (Mathf.Abs(feet.x - 0.5f) > 0.01f || Mathf.Abs(feet.y - 0.27f) > 0.01f)
                throw new InvalidDataException("Entrance avatar is outside the measured framing target.");
            if (westRoof.y >= 0.95f || eastRoof.y >= 0.95f)
                throw new InvalidDataException("Central storefront rooflines would be cropped in portrait.");
            Debug.Log($"CTLIFE [venue-content] portrait camera: position={position:F2}, focus={focus:F2}, ahead={ahead:F2}m; " +
                $"entrance feet={feet:F3}, head={head:F3}; west/east roof={westRoof:F3}/{eastRoof:F3}. Viewport origin is bottom-left.");
        }

        private static void ValidateHeroDetail(VenueSceneRoot venue, VenueSpots spots)
        {
            // Read the shipped Android quality selection, not the editor's currently selected quality tier.
            string quality = File.ReadAllText("ProjectSettings/QualitySettings.asset");
            var android = Regex.Match(quality, @"(?m)^\s+Android: (\d+)");
            var biases = Regex.Matches(quality, @"(?m)^\s+lodBias: ([0-9.]+)");
            if (!android.Success || !int.TryParse(android.Groups[1].Value, out int level) || level >= biases.Count)
                throw new InvalidDataException("Cannot measure venue LODs without the Android quality selection.");
            float bias = float.Parse(biases[level].Groups[1].Value, CultureInfo.InvariantCulture);
            var entrance = new Vector3(spots.Entrance.x, 0f, spots.Entrance.z);
            float ahead = CameraRig.LookAheadForViewport(venue.cameraDistance, venue.cameraPitch,
                venue.cameraFieldOfView, venue.cameraTargetViewportY, venue.cameraFocusHeight);
            var rotation = Quaternion.Euler(venue.cameraPitch, venue.cameraYaw, 0f);
            var position = entrance + Quaternion.Euler(0f, venue.cameraYaw, 0f) * Vector3.forward * ahead +
                Vector3.up * venue.cameraFocusHeight - rotation * Vector3.forward * venue.cameraDistance;
            float metric = 2f * Mathf.Tan(venue.cameraFieldOfView * Mathf.Deg2Rad * 0.5f);
            foreach (var set in venue.GetComponentsInChildren<LODGroup>().Where(g =>
                         g.name.StartsWith("shop_") || g.name.StartsWith("tree_") || g.name.StartsWith("tower_")).GroupBy(g => g.name))
            {
                float minPixels = float.PositiveInfinity, maxPixels = 0f;
                foreach (var group in set)
                {
                    var lods = group.GetLODs();
                    if (lods[0].renderers.Any(r => LodIndex(r.name) != 0))
                        throw new InvalidDataException("Hero detail lost LOD0: " + group.name);
                    var bounds = lods[0].renderers[0].bounds;
                    foreach (var renderer in lods[0].renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                    float expectedHeight = (float)_manifest[group.name]["size_m"][2] * group.transform.lossyScale.y;
                    if (Mathf.Abs(bounds.size.y - expectedHeight) > 0.04f)
                        throw new InvalidDataException($"Hero source height changed: {group.name} is {bounds.size.y:F3}m, expected {expectedHeight:F3}m.");
                    float worldSize = group.size * Mathf.Max(Mathf.Abs(group.transform.lossyScale.x),
                        Mathf.Abs(group.transform.lossyScale.y), Mathf.Abs(group.transform.lossyScale.z));
                    float relativeHeight = worldSize / (Vector3.Distance(position, group.transform.TransformPoint(group.localReferencePoint)) * metric);
                    minPixels = Mathf.Min(minPixels, relativeHeight * 2670f);
                    maxPixels = Mathf.Max(maxPixels, relativeHeight * 2670f);
                    if (relativeHeight * bias < lods[0].screenRelativeTransitionHeight)
                        throw new InvalidDataException($"Hero {group.name} would already drop LOD0 at the entrance with Android bias {bias}.");
                }
                var first = set.First().GetLODs();
                Debug.Log($"CTLIFE [venue-content] hero detail {set.Key} x{set.Count()}: supplied LOD triangles " +
                    $"{string.Join("/", first.Select(l => l.renderers.Sum(TriangleCount)))}; estimated screen height {minPixels:F0}–{maxPixels:F0}px " +
                    $"at 2670px portrait, Android bias {bias:F2}; LOD0 retained, transitions {string.Join("/", first.Select(l => l.screenRelativeTransitionHeight.ToString("F3")))}.");
            }
        }

        private static void Obstacle(Vector3 center, Vector3 size)
        {
            NavSources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.ModifierBox,
                transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one),
                size = size,
                area = 1, // Unity's built-in Not Walkable area. The enclosing contract bounds remain unchanged.
            });
        }

        private static NavMeshData BuildNavigation()
        {
            var settings = NavMesh.GetSettingsByID(0);
            var data = NavMeshBuilder.BuildNavMeshData(settings, NavSources,
                new Bounds(new Vector3(0f, 1f, 3f), new Vector3(46f, 8f, 42f)), Vector3.zero, Quaternion.identity);
            if (data == null) throw new InvalidOperationException("Timeline Square NavMesh bake returned null.");
            data.name = "TimelineSquareNavigation";
            const string path = Output + "/Meshes/TimelineNavigation.asset";
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            if (existing == null) { AssetDatabase.CreateAsset(data, path); return data; }
            EditorUtility.CopySerialized(data, existing);
            Object.DestroyImmediate(data);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void Contact(float x, float z, float width, float depth, float opacity)
        {
            // Baked contact cards ground the sourced props without realtime shadow maps. One merged draw.
            if (_shadowQuad == null)
            {
                _shadowQuad = new Mesh { name = "Contact card" };
                _shadowQuad.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) };
                _shadowQuad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                _shadowQuad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                _shadowQuad.RecalculateNormals();
            }
            ShadowInstances.Add(new CombineInstance { mesh = _shadowQuad, transform = Matrix4x4.TRS(new Vector3(x, 0.015f, z), Quaternion.identity, new Vector3(width, 1, depth)) });
        }

        private static void BuildContactShadows()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, true) { name = "StaticContact" };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float r = new Vector2((x - 31.5f) / 31.5f, (y - 31.5f) / 31.5f).magnitude;
                    float a = Mathf.Pow(Mathf.Clamp01(1f - r), 1.4f) * 0.38f;
                    texture.SetPixel(x, y, new Color(0.17f, 0.19f, 0.17f, a));
                }
            texture.Apply();
            const string texturePath = Output + "/Textures/StaticContact.png";
            File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            const string matPath = Output + "/Materials/StaticContact.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "StaticContact" };
                AssetDatabase.CreateAsset(material, matPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(material);
            var mesh = new Mesh { name = "Static contact shadows" };
            mesh.CombineMeshes(ShadowInstances.ToArray(), true, true);
            mesh = SaveMesh(mesh, Output + "/Meshes/TimelineContacts.asset");
            var go = Child("Static contact shadows", _root).gameObject;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
        }

        private static void Validate(VenueSceneRoot prefab, VenueSpots spots)
        {
            var anchors = prefab.GetComponentsInChildren<SpotAnchor>();
            foreach (var s in spots.spots.Values.Distinct().Where(s => s.hasPosition))
            {
                var anchor = anchors.FirstOrDefault(a => a.spotId == s.id);
                if (anchor == null || Vector3.Distance(anchor.transform.position, new Vector3(s.x, 0f, s.z)) > 0.001f)
                    throw new InvalidDataException("Generated scene moved or lost contract spot " + s.id);
            }
            foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material == null || material.shader == null || material.shader.name.Contains("InternalError"))
                        throw new InvalidDataException("Missing venue material on " + renderer.name);
            if (prefab.navigation == null) throw new InvalidDataException("Generated venue has no navigation.");
            var navigation = NavMesh.AddNavMeshData(prefab.navigation);
            try
            {
                var entrance = spots.Entrance;
                if (entrance == null || !NavMesh.SamplePosition(new Vector3(entrance.x, 0f, entrance.z), out var start, 0.75f, NavMesh.AllAreas))
                    throw new InvalidDataException("Generated navigation does not reach the registered entrance.");
                foreach (var spot in spots.spots.Values.Distinct().Where(s => s.hasPosition))
                {
                    var position = new Vector3(spot.x, 0f, spot.z);
                    if (!NavMesh.SamplePosition(position, out var end, 0.75f, NavMesh.AllAreas))
                        throw new InvalidDataException("Generated navigation lost contract spot " + spot.id);
                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                        throw new InvalidDataException("Generated navigation cannot reach contract spot " + spot.id + " from entrance.");
                }
            }
            finally
            {
                if (navigation.valid) NavMesh.RemoveNavMeshData(navigation);
            }
            int tris = 0;
            var lodChildren = new HashSet<Renderer>();
            foreach (var group in prefab.GetComponentsInChildren<LODGroup>())
            {
                var lods = group.GetLODs();
                foreach (var lod in lods) foreach (var r in lod.renderers) lodChildren.Add(r);
                foreach (var r in lods[0].renderers) tris += TriangleCount(r);
            }
            foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>())
                if (!lodChildren.Contains(renderer)) tris += TriangleCount(renderer);
            Debug.Log($"CTLIFE [venue-content] validation: {anchors.Length} unchanged anchors; worst authored environment LOD = {tris:N0} triangles " +
                "before camera culling. Character triangles and measured draw calls must be profiled separately.");
        }

        private static int TriangleCount(Renderer renderer)
        {
            var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return 0;
            ulong indices = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) indices += mesh.GetIndexCount(i);
            return (int)(indices / 3);
        }
    }
}
