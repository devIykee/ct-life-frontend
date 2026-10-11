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
    /// <summary>Hash-pinned, isolated Timeline study. Never publishes a registry entry or overwrites canonical venue assets.</summary>
    public static class TimelinePolishImport
    {
        public const string Output = "Assets/Art/Candidates/TimelinePolish";
        public const string PrefabPath = Output + "/TimelineSquarePolish.prefab";
        private const string Canonical = "Assets/Art/Generated/Venues/TimelineSquare.prefab";
        private const string Snapshot = "Assets/Resources/Contract/config_snapshot.json";
        private const string Receipt = Output + "/BuildReceipt.json";
        private static readonly string[] Files = { "plaza_fountain_polish.fbx", "timeline_plaza_surface.fbx", "timeline_polish_stone.png", "timeline_polish_water.png" };
        private static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        private static string Source => Path.Combine(Repo, "art/character-candidates/timeline-polish");

        [MenuItem("CT Life/Candidates/Stage Timeline polish")]
        public static void BuildCandidate() => RunProtected("stage", BuildCandidateCore);

        private static void BuildCandidateCore()
        {
            RequireIdle();
            var manifest = Preflight();
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(Canonical);
            if (original == null) throw new FileNotFoundException("Current authored Timeline prefab is required.", Canonical);
            Directory.CreateDirectory(Output + "/Models");
            Directory.CreateDirectory(Output + "/Textures");
            Directory.CreateDirectory(Output + "/Materials");
            Directory.CreateDirectory(Output + "/Meshes");
            AssetDatabase.Refresh();
            foreach (string file in Files)
            {
                string dest = Output + (file.EndsWith(".fbx") ? "/Models/" : "/Textures/") + file;
                File.Copy(Path.Combine(Source, file), dest, true);
                AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                if (file.EndsWith(".fbx"))
                {
                    var importer = AssetImporter.GetAtPath(dest) as ModelImporter;
                    if (importer == null) throw new InvalidDataException("No model importer for " + dest);
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.importAnimation = false;
                    importer.animationType = ModelImporterAnimationType.None;
                    importer.addCollider = false;
                    importer.globalScale = 1f;
                    importer.useFileScale = true;
                    importer.isReadable = true; // Candidate/editor geometry checks; final stripping is a separate acceptance step.
                    importer.generateSecondaryUV = false; // Isolated lighting pass generates unique UV2 copies.
                    importer.SaveAndReimport();
                }
                else
                {
                    var importer = AssetImporter.GetAtPath(dest) as TextureImporter;
                    if (importer == null) throw new InvalidDataException("No texture importer for " + dest);
                    importer.sRGBTexture = true;
                    importer.alphaSource = TextureImporterAlphaSource.None;
                    importer.mipmapEnabled = false;
                    importer.filterMode = FilterMode.Point;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
            }
            var preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = Object.Instantiate(original);
                root.name = "Timeline Square polish candidate";
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
                var venue = root.GetComponent<VenueSceneRoot>();
                if (venue == null || venue.venueId != "timeline_square" || venue.districtId != "timeline")
                    throw new InvalidDataException("Unexpected baseline venue identity.");
                DetachMaterials(root);
                var anchors = AnchorPositions(root);
                string camera = CameraSignature(venue);
                var oldMaps = root.GetComponent<VenueLightmaps>();
                if (oldMaps != null) Object.DestroyImmediate(oldMaps);
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.lightmapIndex = -1;
                // The bake replaces all contact cards. Removing the candidate copy avoids old shadow blobs at moved trees.
                foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Static contact shadows").ToArray())
                    Object.DestroyImmediate(t.gameObject);
                MoveGarden(root, new Vector2(-12f, -7f), new Vector2(-4.8f, -4.6f), "tree_blossom_4");
                MoveGarden(root, new Vector2(12f, -7f), new Vector2(4.8f, -4.6f), "tree_blossom_1");
                Move(root, "street_lamp_single", new Vector2(-17f, -11f), new Vector2(-3.2f, -10.7f));
                Move(root, "street_lamp_single", new Vector2(17f, -11f), new Vector2(3.2f, -10.7f));
                var hex = root.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials)
                    .FirstOrDefault(m => m != null && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null &&
                        m.GetTexture("_BaseMap").name.Contains("hexagonal_concrete_paving"));
                if (hex == null) throw new InvalidDataException("Baseline hex paving material/texture is missing.");
                Object.DestroyImmediate(Find(root, "plaza_fountain", new Vector2(0f, 7.5f)).gameObject);
                var fountain = InstantiateModel("plaza_fountain_polish.fbx", root.transform, new Vector3(0f, 0f, 7.5f));
                fountain.name = "plaza_fountain_polish";
                var stone = Material("M_timeline_polish_stone", "timeline_polish_stone.png");
                var water = Material("M_timeline_polish_water", "timeline_polish_water.png");
                var renderers = fountain.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length != 2) throw new InvalidDataException("Fountain requires exactly two LOD renderers.");
                var lods = new LOD[2];
                for (int i = 0; i < 2; i++)
                {
                    var r = renderers.Single(r => r.name.EndsWith("_LOD" + i, StringComparison.Ordinal));
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    int expected = i == 0 ? 1236 : 592;
                    if (mesh == null || mesh.subMeshCount != 2 || mesh.triangles.Length / 3 != expected || mesh.uv.Length != mesh.vertexCount)
                        throw new InvalidDataException("Fountain geometry/UV contract changed at LOD" + i);
                    r.sharedMaterials = new[] { stone, water };
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    var size = r.bounds.size;
                    if (Mathf.Abs(size.x - 4.8f) > 0.02f || Mathf.Abs(size.z - 4.8f) > 0.02f || Mathf.Abs(r.bounds.min.y) > 0.005f)
                        throw new InvalidDataException("Fountain import scale/axis/ground contact is wrong: " + r.bounds);
                    lods[i] = new LOD(i == 0 ? 0.02f : 0.002f, new Renderer[] { r });
                }
                var group = fountain.AddComponent<LODGroup>();
                group.SetLODs(lods);
                group.fadeMode = LODFadeMode.None;
                group.RecalculateBounds();
                var solid = new GameObject("Basin collision only");
                solid.transform.SetParent(fountain.transform, false);
                var collider = solid.AddComponent<MeshCollider>();
                collider.sharedMesh = BasinCollision();
                collider.convex = true;
                if (fountain.GetComponentsInChildren<Collider>(true).Length != 1)
                    throw new InvalidDataException("Only the basin may have a fountain collider; jets are visual.");
                var paving = InstantiateModel("timeline_plaza_surface.fbx", root.transform, Vector3.zero);
                paving.name = "timeline_plaza_surface";
                var surface = paving.GetComponentsInChildren<MeshRenderer>(true);
                if (surface.Length != 1 || surface[0].GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3 != 42 ||
                    paving.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidDataException("Paving must be a 42-triangle visual mesh without a collider.");
                surface[0].sharedMaterial = hex;
                var snapshot = JObject.Parse(File.ReadAllText(Snapshot));
                var spots = Registries.Parse(snapshot["ids"], snapshot["config"]).SpotsFor("timeline_square");
                if (spots == null || spots.walkable.Count == 0) throw new InvalidDataException("No contract Timeline walkable rectangles.");
                venue.navigation = BuildNavigation(spots);
                venue.Init(spots);
                if (CameraSignature(venue) != camera || !SameAnchors(anchors, AnchorPositions(root)))
                    throw new InvalidDataException("Candidate changed an existing camera setting or spot anchor.");
                var nav = ValidateNavigation(venue, spots);
                ValidateMaterials(root);
                if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null) throw new IOException("Candidate prefab save failed.");
                SaveCandidateAssets();
                var receipt = new JObject { ["status"] = "staged_unbaked_not_published", ["manifest_sha256"] = Hash(Path.Combine(Source, "frozen-manifest.json")),
                    ["prefab_sha256"] = Hash(PrefabPath), ["baseline_sha256"] = Hash(Canonical), ["anchor_count"] = anchors.Count,
                    ["navigation"] = nav, ["fountain_lod_triangles"] = new JArray(1236, 592), ["surface_triangles"] = 42,
                    ["collider"] = "one 24-sided convex basin hull, y=0..0.707m; no jet/paving collider", ["canonical_files_preserved"] = _protection.Count };
                File.WriteAllText(Receipt, receipt.ToString());
                AssetDatabase.ImportAsset(Receipt);
                Debug.Log("CTLIFE [timeline-polish] staged isolated prefab and rebuilt navigation; anchors/camera/canonical dependencies preserved. BakeCandidate is required before native review. " + nav);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        [MenuItem("CT Life/Candidates/Bake Timeline polish")]
        public static void BakeCandidate() => RunProtected("bake", BakeCandidateCore);

        private static void BakeCandidateCore()
        {
            RequireIdle();
            Preflight();
            var receipt = JObject.Parse(File.ReadAllText(Receipt));
            if ((string)receipt["status"] != "staged_unbaked_not_published" && (string)receipt["status"] != "baked_isolated_not_published")
                throw new InvalidDataException("Latest candidate transaction failed or has not staged successfully.");
            string expected = (string)receipt["baked_prefab_sha256"] ?? (string)receipt["prefab_sha256"];
            if (Hash(PrefabPath) != expected || (string)receipt["manifest_sha256"] != Hash(Path.Combine(Source, "frozen-manifest.json")))
                throw new InvalidDataException("Candidate changed after its staged validation. Stage again.");
            try
            {
                _protection.ClearDirtyProtectedObjects();
                VenueLightingBuilder.BakeVenue(PrefabPath, Output + "/Lighting");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var maps = prefab != null ? prefab.GetComponent<VenueLightmaps>() : null;
                if (maps == null || maps.colors == null || maps.colors.Length == 0 || maps.colors.Any(t => t == null))
                    throw new InvalidDataException("Candidate bake has no complete lightmap binding.");
                receipt["status"] = "baked_isolated_not_published";
                receipt["baked_prefab_sha256"] = Hash(PrefabPath);
                receipt["lightmaps"] = maps.colors.Length;
                receipt["canonical_files_preserved_after_bake"] = _protection.Count;
                File.WriteAllText(Receipt, receipt.ToString());
                AssetDatabase.ImportAsset(Receipt);
                Debug.Log("CTLIFE [timeline-polish] isolated bake verified. No registry entry changed; native candidate review still required.");
            }
            finally { _protection.ClearDirtyProtectedObjects(); }
        }

        private static JObject Preflight()
        {
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(Source, "frozen-manifest.json")));
            if (manifest["export_validation_pass"]?.Value<bool>() != true) throw new InvalidDataException("Export geometry gate is not recorded as passed.");
            foreach (var file in manifest["files"] as JArray ?? throw new InvalidDataException("No frozen source files."))
            {
                string relative = (string)file["path"];
                string full = Path.GetFullPath(Path.Combine(Repo, relative));
                if (!full.StartsWith(Repo + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Hash(full) != (string)file["sha256"])
                    throw new InvalidDataException("Frozen source/baseline mismatch: " + relative);
            }
            foreach (string file in Files)
                if (!(manifest["files"] as JArray).Any(row => ((string)row["path"]).EndsWith("/timeline-polish/" + file, StringComparison.Ordinal)))
                    throw new InvalidDataException("Frozen manifest omitted " + file);
            return manifest;
        }

        private static void RequireIdle()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run this byte-protected candidate tool in a dedicated Unity batch process; interactive unsaved assets are not a supported baseline.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
                throw new InvalidOperationException("Finish Play mode / active lighting bake first.");
        }
        private static Transform Find(GameObject root, string name, Vector2 position)
        {
            var matches = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name &&
                Vector2.Distance(new Vector2(t.position.x, t.position.z), position) < 0.02f).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Expected one " + name + " at " + position + "; found " + matches.Length);
            return matches[0];
        }
        private static void Move(GameObject root, string name, Vector2 from, Vector2 to)
        {
            var t = Find(root, name, from);
            t.position += new Vector3(to.x - from.x, 0f, to.y - from.y);
        }
        private static void MoveGarden(GameObject root, Vector2 from, Vector2 to, string tree)
        {
            Move(root, "planter_round", from, to);
            Move(root, tree, from, to);
            Move(root, "bush_large_flowers", from + new Vector2(1.7f, 0.2f), to + new Vector2(1.7f, 0.2f));
            Move(root, "flower_clump_2", from + new Vector2(-0.8f, -0.72f), to + new Vector2(-0.8f, -0.72f));
        }
        private static GameObject InstantiateModel(string name, Transform parent, Vector3 position)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Output + "/Models/" + name);
            if (source == null) throw new FileNotFoundException("Candidate model was not imported.", name);
            var model = Object.Instantiate(source, parent);
            model.transform.localPosition = position;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            foreach (var group in model.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(group);
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            return model;
        }
        private static Material Material(string name, string texture)
        {
            string path = Output + "/Materials/" + name + ".mat";
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) throw new InvalidDataException("Pinned URP Simple Lit shader is unavailable.");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Output + "/Textures/" + texture));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Cull", (float)CullMode.Back);
            m.SetFloat("_Smoothness", 0.12f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
        private static Mesh BasinCollision()
        {
            const int n = 24;
            var vertices = new Vector3[n * 2 + 2];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                vertices[i] = new Vector3(Mathf.Cos(a) * 2.4f, 0f, Mathf.Sin(a) * 2.4f);
                vertices[n + i] = vertices[i] + Vector3.up * 0.707f;
            }
            vertices[n * 2] = Vector3.zero; vertices[n * 2 + 1] = Vector3.up * 0.707f;
            var triangles = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                triangles.AddRange(new[] { i, n + i, n + j, i, n + j, j, n * 2, i, j, n * 2 + 1, n + j, n + i });
            }
            var mesh = new Mesh { name = "Fountain basin collision", vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return SaveAsset(mesh, Output + "/Meshes/BasinCollision.asset");
        }
        private static NavMeshData BuildNavigation(VenueSpots spots)
        {
            var sources = new List<NavMeshBuildSource>();
            foreach (var r in spots.walkable)
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(new Vector3((r.x0 + r.x1) * 0.5f, -0.1f, (r.z0 + r.z1) * 0.5f), Quaternion.identity, Vector3.one),
                    size = new Vector3(Mathf.Abs(r.x1 - r.x0), 0.2f, Mathf.Abs(r.z1 - r.z0)), area = 0 });
            foreach (var p in new[] { new Vector2(-4.8f,-4.6f), new Vector2(4.8f,-4.6f), new Vector2(-13.8f,1.7f), new Vector2(14f,3f),
                new Vector2(-8.2f,11.4f), new Vector2(8.5f,11.6f), new Vector2(-18.5f,11.4f), new Vector2(18.6f,11.4f) })
                sources.Add(Exclusion(new Vector3(p.x, 0.9f, p.y), new Vector3(2f, 2f, 2f)));
            sources.Add(Exclusion(new Vector3(0f, 1f, 7.5f), new Vector3(4.5f, 3f, 4.5f)));
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(new Vector3(0f, 1f, 3f), new Vector3(46f, 8f, 42f)), Vector3.zero, Quaternion.identity);
            if (data == null) throw new InvalidOperationException("Candidate NavMesh bake returned null.");
            data.name = "TimelinePolishNavigation";
            return SaveAsset(data, Output + "/Meshes/Navigation.asset");
        }
        private static NavMeshBuildSource Exclusion(Vector3 center, Vector3 size) => new NavMeshBuildSource
        { shape = NavMeshBuildSourceShape.ModifierBox, transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one), size = size, area = 1 };
        private static JObject ValidateNavigation(VenueSceneRoot venue, VenueSpots spots)
        {
            // Offset the validation instance away from any editor-open venue navigation.
            var offset = new Vector3(10000f, 0f, 10000f);
            var instance = NavMesh.AddNavMeshData(venue.navigation, offset, Quaternion.identity);
            var rows = new JArray();
            try
            {
                if (spots.Entrance == null) throw new InvalidDataException("Contract entrance missing.");
                var startPoint = new Vector3(spots.Entrance.x, 0f, spots.Entrance.z) + offset;
                if (!NavMesh.SamplePosition(startPoint, out var start, 0.75f, NavMesh.AllAreas)) throw new InvalidDataException("Candidate entrance not reachable.");
                foreach (var spot in spots.spots.Values.Distinct().Where(s => s.hasPosition))
                {
                    var point = new Vector3(spot.x, 0f, spot.z) + offset;
                    if (!NavMesh.SamplePosition(point, out var end, 0.75f, NavMesh.AllAreas)) throw new InvalidDataException("Candidate lost spot " + spot.id);
                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                        throw new InvalidDataException("Candidate path incomplete to " + spot.id);
                    rows.Add(new JObject { ["spot"] = spot.id, ["sample_distance_m"] = Vector3.Distance(point, end.position), ["path"] = "PathComplete" });
                }
                foreach (var point in new[] { new Vector3(0,0,-14), new Vector3(0,0,-15), Vector3.zero })
                    if (!NavMesh.SamplePosition(point + offset, out var hit, 0.75f, NavMesh.AllAreas))
                        throw new InvalidDataException("Known spawn/exit/center no longer lies within navigation projection tolerance: " + point);
            }
            finally { if (instance.valid) NavMesh.RemoveNavMeshData(instance); }
            return new JObject { ["all_positioned_spots"] = rows, ["known_spawn_exit_center_checked"] = true, ["temporary_test_offset_m"] = 10000 };
        }
        private static Dictionary<string, Vector3> AnchorPositions(GameObject root) => root.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(a => a.spotId, a => a.transform.position);
        private static bool SameAnchors(Dictionary<string, Vector3> a, Dictionary<string, Vector3> b) => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && Vector3.Distance(p.Value, v) < 0.0001f);
        private static string CameraSignature(VenueSceneRoot v) => string.Join("|", v.venueId, v.districtId, v.cameraBounds, v.cameraDistance, v.cameraPitch, v.cameraYaw, v.cameraFieldOfView, v.cameraLookAhead, v.cameraTargetViewportY, v.cameraFocusHeight, v.groundMask.value);
        private static void ValidateMaterials(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m == null || m.shader == null || m.shader.name.Contains("InternalError")) throw new InvalidDataException("Candidate has missing material on " + r.name);
        }
        private static T SaveAsset<T>(T asset, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(asset, path); return asset; }
            EditorUtility.CopySerialized(asset, existing); Object.DestroyImmediate(asset); EditorUtility.SetDirty(existing); return existing;
        }
        private static ProtectionSnapshot _protection;

        private static void DetachMaterials(GameObject root)
        {
            // Even display surfaces must reference candidate copies before baking. URP validation
            // can dirty a loaded material; sharing it with the canonical prefab was insufficient isolation.
            var copies = new Dictionary<Material, Material>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(original =>
                {
                    if (original == null) return null;
                    if (copies.TryGetValue(original, out var cached)) return cached;
                    string path = AssetDatabase.GetAssetPath(original);
                    string id = AssetDatabase.AssetPathToGUID(path);
                    if (string.IsNullOrEmpty(id)) throw new InvalidDataException("Baseline material has no persistent asset identity: " + original.name);
                    var copy = new Material(original) { name = original.name };
                    copies[original] = SaveAsset(copy, Output + "/Materials/Baseline_" + id + ".mat");
                    return copies[original];
                }).ToArray();
            }
            _protection.ClearDirtyProtectedObjects();
        }

        private static void SaveCandidateAssets()
        {
            _protection.ClearDirtyProtectedObjects();
            foreach (string guid in AssetDatabase.FindAssets("", new[] { Output }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".mat", StringComparison.Ordinal) || path.EndsWith(".asset", StringComparison.Ordinal))
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
                }
            }
        }

        private static void RunProtected(string phase, Action operation)
        {
            RequireIdle();
            if (_protection != null) throw new InvalidOperationException("Candidate transaction is already active.");
            // No git baseline is used: preserve this exact dirty working tree's on-disk bytes.
            var snapshot = new ProtectionSnapshot(phase);
            _protection = snapshot;
            try
            {
                operation();
                snapshot.ClearDirtyProtectedObjects();
                snapshot.AssertUnchanged();
                snapshot.Record("passed_protected_bytes_unchanged", null, new JArray());
                Debug.Log("CTLIFE [timeline-polish] canonical byte protection passed; durable receipt: " + snapshot.ReceiptPath);
            }
            catch (Exception failure)
            {
                var changed = snapshot.Changes();
                Exception restoreFailure = null;
                try { snapshot.RestoreChanged(changed); }
                catch (Exception error) { restoreFailure = error; }
                string status = restoreFailure == null ? "failed_original_bytes_restored" : "failed_restore_incomplete";
                snapshot.Record(status, failure.ToString(), changed, restoreFailure?.ToString());
                Directory.CreateDirectory(Output);
                // Plain JSON write only: importing it while recovering could trigger another save.
                File.Copy(snapshot.ReceiptPath, Output + "/FailureReceipt.json", true);
                File.WriteAllText(Receipt, new JObject { ["status"] = phase + "_" + status, ["protection_receipt"] = snapshot.ReceiptPath, ["native_acceptance"] = false }.ToString());
                throw new InvalidOperationException("Timeline candidate " + phase + " failed; " + status + ". Backup/complete mismatch receipt: " + snapshot.ReceiptPath,
                    restoreFailure == null ? failure : new AggregateException(failure, restoreFailure));
            }
            finally { _protection = null; }
        }

        private sealed class ProtectionSnapshot
        {
            private readonly string _phase, _directory;
            private readonly Dictionary<string, string> _hashes = new Dictionary<string, string>();
            private readonly Dictionary<string, string> _backups = new Dictionary<string, string>();
            public int Count => _hashes.Count;
            public string ReceiptPath => Path.Combine(_directory, "transaction.json");

            public ProtectionSnapshot(string phase)
            {
                _phase = phase;
                _directory = Path.Combine(Path.GetTempPath(), "ctlife-timeline-polish-" + phase + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_directory);
                var paths = AssetDatabase.GetDependencies(Canonical, true).Concat(new[] { Canonical, Snapshot, "Assets/Venues/VenueContentRegistry.asset" })
                    .Where(File.Exists).Distinct().ToList();
                paths.AddRange(Directory.GetFiles("ProjectSettings", "*", SearchOption.AllDirectories));
                paths.AddRange(paths.Where(p => File.Exists(p + ".meta")).Select(p => p + ".meta").ToArray());
                foreach (string path in paths.Distinct())
                {
                    string full = Path.GetFullPath(path);
                    string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")) + Path.DirectorySeparatorChar;
                    if (!full.StartsWith(project, StringComparison.Ordinal)) throw new InvalidDataException("Protected asset escaped project: " + path);
                    string backup = Path.Combine(_directory, "before", path);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    File.Copy(path, backup, false);
                    string hash = Hash(path);
                    if (Hash(backup) != hash) throw new IOException("Byte snapshot failed: " + path);
                    _hashes[path] = hash; _backups[path] = backup;
                }
                Record("captured_before_candidate_import", null, new JArray());
                Debug.Log("CTLIFE [timeline-polish] durable originals captured before import: " + ReceiptPath);
            }
            public JArray Changes()
            {
                var rows = new JArray();
                foreach (var item in _hashes)
                {
                    string current = File.Exists(item.Key) ? Hash(item.Key) : null;
                    if (current != item.Value) rows.Add(new JObject { ["path"] = item.Key, ["before_sha256"] = item.Value, ["after_sha256"] = current, ["backup"] = _backups[item.Key] });
                }
                return rows;
            }
            public void AssertUnchanged()
            {
                var changed = Changes();
                if (changed.Count > 0) throw new InvalidDataException("Canonical/project bytes changed: " + string.Join(", ", changed.Select(r => (string)r["path"])));
            }
            public void ClearDirtyProtectedObjects()
            {
                // This tool runs explicitly in an idle editor/batch. Clear only protected assets;
                // never call a global SaveAssets to flush their incidental validation changes.
                foreach (string path in _hashes.Keys.Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !p.EndsWith(".meta", StringComparison.Ordinal)))
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                        if (asset != null && EditorUtility.IsDirty(asset)) EditorUtility.ClearDirty(asset);
            }
            public void RestoreChanged(JArray changed)
            {
                ClearDirtyProtectedObjects();
                foreach (var row in changed)
                {
                    string path = (string)row["path"];
                    if (File.Exists(path))
                    {
                        string evidence = Path.Combine(_directory, "after-failure", path);
                        Directory.CreateDirectory(Path.GetDirectoryName(evidence)); File.Copy(path, evidence, false);
                    }
                    if (Hash(_backups[path]) != _hashes[path]) throw new IOException("Original backup is corrupt: " + path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.Copy(_backups[path], path, true);
                }
                // Deliberately do not Refresh/Reimport after restoration. That can re-run the same
                // material upgrader. Root verifies hashes again after the Unity process exits.
                AssertUnchanged();
            }
            public void Record(string status, string error, JArray changed, string restoreError = null)
            {
                var files = new JArray(_hashes.Select(p => new JObject { ["path"] = p.Key, ["sha256"] = p.Value, ["backup"] = _backups[p.Key] }));
                var report = new JObject { ["phase"] = _phase, ["status"] = status, ["utc"] = DateTime.UtcNow.ToString("O"),
                    ["files"] = files, ["changed_files"] = changed, ["error"] = error, ["restore_error"] = restoreError,
                    ["post_process_exit_hash_check_required"] = true };
                File.WriteAllText(ReceiptPath, report.ToString());
            }
        }
        private static string Hash(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
