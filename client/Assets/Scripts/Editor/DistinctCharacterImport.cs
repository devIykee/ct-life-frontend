using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CTLife.UI;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Imports the separately authored Sketchfab and UBC characters preserved by Claude and Hermes.
    /// Source geometry is pinned in manifest.json; outfit variants are not additional identities.
    /// Each FBX owns its CTS Humanoid Avatar. No Snow/Rain Avatar or source art is overwritten.
    /// </summary>
    public static class DistinctCharacterImport
    {
        public const string Root = "Assets/Art/characters/distinct";
        internal const string AvatarTag = "ct-life:distinct-cts-avatar-v1";
        internal static readonly HashSet<string> RestProbe = new HashSet<string>(StringComparer.Ordinal);
        private const string RegistryPath = "Assets/Resources/Characters/CharacterRegistry.asset";
        private const string CatalogPath = "Assets/Resources/UI/CreatorLookAdditions.asset";
        private static readonly string[] States = { "loco_idle", "loco_walk", "loco_run", "act_sit" };

        public static string[] Look(string id) => new[]
        {
            "skin_tone3", "head_" + id, "hair_" + id, "top_" + id,
            "bottom_" + id, "shoes_" + id, "acc_none"
        };

        [MenuItem("CT Life/Cast/Import distinct characters")]
        public static void Run()
        {
            var manifest = JObject.Parse(File.ReadAllText(Root + "/manifest.json"));
            var rig = (JObject)JObject.Parse(File.ReadAllText(Root + "/cts_humanoid_map.json"))["unity_humanoid"];
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>(RegistryPath)
                ?? throw new FileNotFoundException(RegistryPath);
            var controller = registry.controller ?? throw new InvalidDataException("Shared character controller is missing.");
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? throw new InvalidDataException("The project URP Lit shader is missing.");
            Directory.CreateDirectory(Root + "/materials");
            Directory.CreateDirectory(Root + "/prefabs");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var entries = new List<CharacterRegistry.Entry>();
            var choices = new List<CreatorLookCatalog.Entry>();
            var report = new JObject();

            foreach (var record in manifest["models"])
            {
                string id = (string)record["id"];
                var files = (JObject)record["files"];
                var modelRig = record["humanoid_map"] == null ? rig :
                    (JObject)JObject.Parse(File.ReadAllText(Root + "/" + (string)record["humanoid_map"]))["unity_humanoid"];
                foreach (var role in new[] { "crowd_fbx", "hero_fbx" })
                {
                    string path = Root + "/" + (string)files[role];
                    ConfigureAvatar(path, modelRig);
                }
                var materialPath = Root + "/materials/" + id + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                material.shader = shader;
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/" + (string)files["albedo"]));
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Smoothness", .22f);
                material.DisableKeyword("_EMISSION");
                EditorUtility.SetDirty(material);
                var crowd = BuildPrefab(id, Root + "/" + (string)files["crowd_fbx"], material, false);
                var hero = BuildPrefab(id, Root + "/" + (string)files["hero_fbx"], material, true);
                var detail = new JObject();
                var problems = CastValidation.Check(crowd, controller, detail);
                problems.AddRange(CastValidation.Check(hero, controller, new JObject()));
                var spans = CastValidation.PoseSpans(hero, controller, States);
                detail["sampled_pose_spans"] = JObject.FromObject(spans);
                detail["problems"] = new JArray(problems);
                report[id] = detail;
                if (problems.Count > 0 || spans.Count != States.Length || spans.Values.Any(float.IsNaN))
                    throw new InvalidDataException(id + ": character animation gate failed: " + string.Join("; ", problems));
                var look = Look(id);
                entries.Add(new CharacterRegistry.Entry
                {
                    key = string.Join("|", look), prefab = crowd, previewPrefab = hero,
                    height = (float)record["height"]
                });
                choices.Add(new CreatorLookCatalog.Entry
                {
                    labelKey = "character." + id, label = (string)record["label"], look = look, previewYaw = 0f,
                    identityId = "head_" + id, identityLabelKey = "character." + id, species = (string)record["species"]
                });
            }

            // Nothing becomes selectable until every imported model passed the actual Humanoid/controller gate.
            foreach (var entry in entries)
            {
                int existing = registry.looks.FindIndex(e => e.key == entry.key);
                if (existing < 0) registry.looks.Add(entry); else registry.looks[existing] = entry;
            }
            var catalog = AssetDatabase.LoadAssetAtPath<CreatorLookCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CreatorLookCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.looks = choices;
            EditorUtility.SetDirty(registry);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            // A second editor invocation may have indexed the registry before the import.
            typeof(CharacterRegistry).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(registry, null);
            BakePortraits(choices, registry);
            string reportPath = Argument("-ctlifeDistinctReport");
            if (!string.IsNullOrEmpty(reportPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
                File.WriteAllText(reportPath, report.ToString());
            }
            Debug.Log("CTLIFE [distinct-cast] imported " + choices.Count + " separate characters; original creator looks preserved.");
        }

        /// <summary>Editor-only portraits of the actual hero meshes; no runtime scene or persistent objects are created.</summary>
        internal static void BakePortraits(IEnumerable<CreatorLookCatalog.Entry> choices, CharacterRegistry registry,
            string output = "Assets/Resources/UI/Customizer/Portraits/")
        {
            const int layer = 31;
            Directory.CreateDirectory(output);
            foreach (var choice in choices)
            {
                var entry = registry.looks.Single(e => e.key == string.Join("|", choice.look));
                if (entry.previewPrefab == null) throw new InvalidDataException("Portrait hero is missing: " + choice.identityId);
                var scene = EditorSceneManager.NewPreviewScene();
                var texture = new RenderTexture(480, 600, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                    { name = "distinct_portrait", antiAliasing = 1 };
                var small = new RenderTexture(160, 200, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var read = new Texture2D(160, 200, TextureFormat.RGBA32, false, false);
                var previous = RenderTexture.active;
                var previousSun = RenderSettings.sun;
                var pipeline = UniversalRenderPipeline.asset;
                float previousScale = pipeline != null ? pipeline.renderScale : 1f;
                bool previousAdaptive = pipeline != null && pipeline.useAdaptivePerformance;
                try
                {
                    var actor = UnityEngine.Object.Instantiate(entry.previewPrefab);
                    SceneManager.MoveGameObjectToScene(actor, scene);
                    actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    foreach (var child in actor.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
                    var animator = actor.GetComponent<Animator>();
                    if (animator == null) throw new InvalidDataException("Portrait has no actor: " + choice.identityId);
                    animator.runtimeAnimatorController = entry.controllerOverride != null ? entry.controllerOverride : registry.controller;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.applyRootMotion = false;
                    animator.Rebind();
                    string state = animator.HasState(0, Animator.StringToHash("idle_fidget_foldarms"))
                        ? "idle_fidget_foldarms" : "loco_idle";
                    animator.Play(state, 0, .2f);
                    animator.Update(0f);
                    animator.Update(.05f);
                    actor.transform.rotation = Quaternion.Euler(0f, choice.previewYaw - 18f, 0f);
                    var bounds = PortraitBounds(actor, animator);

                    var cameraObject = new GameObject("distinct_portrait_camera");
                    SceneManager.MoveGameObjectToScene(cameraObject, scene);
                    var camera = cameraObject.AddComponent<Camera>();
                    camera.enabled = false;
                    camera.scene = scene;
                    camera.allowHDR = camera.allowMSAA = false;
                    camera.cullingMask = 1 << layer;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.clear;
                    camera.aspect = .8f;
                    camera.fieldOfView = 30f;
                    camera.nearClipPlane = .01f;
                    camera.farClipPlane = 20f;
                    camera.targetTexture = texture;
                    var cameraData = camera.GetUniversalAdditionalCameraData();
                    cameraData.renderPostProcessing = false;
                    cameraData.renderShadows = false;
                    cameraData.allowXRRendering = false;
                    float tangent = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
                    float distance = Mathf.Max(bounds.extents.y / tangent,
                        bounds.extents.x / (tangent * camera.aspect)) * 1.08f + bounds.extents.z;
                    camera.farClipPlane = Mathf.Max(20f, distance + bounds.extents.z + 1f);
                    camera.transform.SetPositionAndRotation(bounds.center + Vector3.forward * distance,
                        Quaternion.LookRotation(Vector3.back, Vector3.up));
                    Debug.Log("CTLIFE [distinct-portrait-frame] " + choice.identityId + " bounds=" + bounds +
                        " camera=" + camera.transform.position + " far=" + camera.farClipPlane +
                        " humanScale=" + animator.humanScale);

                    var lightObject = new GameObject("distinct_portrait_key");
                    SceneManager.MoveGameObjectToScene(lightObject, scene);
                    lightObject.transform.rotation = Quaternion.Euler(30f, 150f, 0f);
                    var light = lightObject.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.intensity = 1.1f;
                    light.color = new Color(1f, .95f, .88f);
                    light.cullingMask = 1 << layer;
                    light.shadows = LightShadows.None;
                    RenderSettings.sun = light;
                    if (pipeline != null) { pipeline.renderScale = 1f; pipeline.useAdaptivePerformance = false; }
                    texture.Create();
                    small.Create();
                    camera.Render();
                    Graphics.Blit(texture, small);
                    RenderTexture.active = small;
                    read.ReadPixels(new Rect(0, 0, 160, 200), 0, 0, false);
                    read.Apply(false);
                    int visible = read.GetPixels32().Count(p => p.a > 16);
                    if (visible < 300) throw new InvalidDataException("Portrait rendered empty: " + choice.identityId);
                    string path = output + choice.identityId + ".png";
                    File.WriteAllBytes(path, read.EncodeToPNG());
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    Debug.Log("CTLIFE [distinct-portrait] " + choice.identityId + " visible pixels=" + visible);
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderSettings.sun = previousSun;
                    if (pipeline != null) { pipeline.renderScale = previousScale; pipeline.useAdaptivePerformance = previousAdaptive; }
                    EditorSceneManager.ClosePreviewScene(scene);
                    texture.Release();
                    UnityEngine.Object.DestroyImmediate(texture);
                    small.Release();
                    UnityEngine.Object.DestroyImmediate(small);
                    UnityEngine.Object.DestroyImmediate(read);
                }
            }
        }

        private static Bounds PortraitBounds(GameObject actor, Animator animator)
        {
            var points = new List<Vector3>();
            var mesh = new Mesh();
            try
            {
                foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.updateWhenOffscreen = true;
                    // Unity 6000.3 useScale=true compensates the renderer Transform scale.
                    // TransformPoint below then applies the authored FBX scale exactly once.
                    renderer.BakeMesh(mesh, true);
                    mesh.RecalculateBounds();
                    Debug.Log("CTLIFE [distinct-portrait-skin] " + actor.name + " skin=" + renderer.name +
                        " scale=" + renderer.transform.lossyScale + " localBounds=" + mesh.bounds +
                        " worldBounds=" + renderer.bounds);
                    points.AddRange(mesh.vertices.Select(renderer.transform.TransformPoint));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
            if (points.Count == 0) throw new InvalidDataException("Portrait actor has no visible mesh.");
            var body = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) body.Encapsulate(point);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            float headY = head != null ? head.position.y : body.max.y - body.size.y * .12f;
            float lower = Mathf.Clamp(headY - body.size.y * .10f,
                body.min.y + body.size.y * .66f, body.min.y + body.size.y * .76f);
            var upper = points.Where(p => p.y >= lower).ToArray();
            if (upper.Length == 0) throw new InvalidDataException("Portrait actor has no head surface.");
            var portrait = new Bounds(upper[0], Vector3.zero);
            foreach (var point in upper) portrait.Encapsulate(point);
            // Measure the actual upper surface so wide creature heads and original hair stay inside the crop.
            portrait.Encapsulate(new Vector3(portrait.center.x, lower, portrait.center.z));
            return portrait;
        }

        /// <summary>Retry only the portrait step after a successful model/pose import.</summary>
        public static void BakePortraitsBatch()
        {
            int code = 0;
            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>(RegistryPath)
                    ?? throw new FileNotFoundException(RegistryPath);
                var catalog = AssetDatabase.LoadAssetAtPath<CreatorLookCatalog>(CatalogPath)
                    ?? throw new FileNotFoundException(CatalogPath);
                BakePortraits(catalog.looks, registry);
                Debug.Log("CTLIFE [distinct-portrait] baked " + catalog.looks.Count + " character portraits.");
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        internal static void ConfigureAvatar(string path, JObject map)
        {
            RestProbe.Add(path);
            try { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport); }
            finally { RestProbe.Remove(path); }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new FileNotFoundException(path);
            var instance = UnityEngine.Object.Instantiate(model);
            var human = new HumanDescription();
            try
            {
                instance.name = model.name;
                // Some FBX files name their root and mesh alike. Bone lookup only needs the first matching transform.
                var transforms = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                    if (!transforms.ContainsKey(t.name)) transforms.Add(t.name, t);
                var missing = map.Properties().Where(p => !transforms.ContainsKey((string)p.Value)).Select(p => p.Name).ToArray();
                if (missing.Length > 0) throw new InvalidDataException(path + " missing CTS bones: " + string.Join(", ", missing));
                // Match the existing project's pinned Unity 6000.3 T-pose import procedure, with the CTS map.
                var setup = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool", true);
                var wrapper = setup.GetNestedType("BoneWrapper", BindingFlags.Public | BindingFlags.NonPublic);
                var enforce = setup.GetMethod("MakePoseValid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (wrapper == null || enforce == null) throw new MissingMethodException("Unity Avatar T-pose tooling changed.");
                var wrappers = Array.CreateInstance(wrapper, HumanTrait.BoneCount);
                for (int i = 0; i < HumanTrait.BoneCount; i++)
                {
                    string name = (string)map[((HumanBodyBones)i).ToString()];
                    wrappers.SetValue(Activator.CreateInstance(wrapper, new object[]
                        { HumanTrait.BoneName[i], name == null ? null : transforms[name] }), i);
                }
                enforce.Invoke(null, new object[] { wrappers });
                human.skeleton = instance.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
                human.human = map.Properties().Select(p => new HumanBone
                {
                    boneName = (string)p.Value,
                    humanName = HumanTrait.BoneName[(int)Enum.Parse(typeof(HumanBodyBones), p.Name)],
                    limit = new HumanLimit { useDefaultValues = true }
                }).ToArray();
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
            human.upperArmTwist = human.lowerArmTwist = human.upperLegTwist = human.lowerLegTwist = .5f;
            human.armStretch = human.legStretch = .05f;
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            // Assigning sourceAvatar changes the importer's Avatar setup. Clear a stale copied
            // Avatar first, then commit this body's mapping and the own-source mode last.
            importer.animationType = ModelImporterAnimationType.Human;
            importer.sourceAvatar = null;
            importer.humanDescription = human;
            importer.userData = AvatarTag;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
            var animator = AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidDataException("No valid own Humanoid Avatar: " + path);
        }

        internal static GameObject BuildPrefab(string id, string path, Material material, bool hero)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new FileNotFoundException(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                instance.name = id + (hero ? "_Hero" : "");
                foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.sharedMaterials = new[] { material };
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.updateWhenOffscreen = false;
                }
                var animator = instance.GetComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
                if (!hero)
                {
                    var tiers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .OrderByDescending(r => r.sharedMesh.vertexCount).ToArray();
                    if (tiers.Length != 3) throw new InvalidDataException(id + " requires three world LOD meshes.");
                    var group = instance.GetComponent<LODGroup>() ?? instance.AddComponent<LODGroup>();
                    group.SetLODs(new[] { new LOD(.25f, new Renderer[] { tiers[0] }),
                        new LOD(.10f, new Renderer[] { tiers[1] }), new LOD(.01f, new Renderer[] { tiers[2] }) });
                    group.RecalculateBounds();
                }
                return PrefabUtility.SaveAsPrefabAsset(instance, Root + "/prefabs/" + instance.name + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        public static void RunBatch()
        {
            int code = 0;
            try { Run(); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>
        /// Regression gate for target switches/fresh imports. Run in a separate Android Editor invocation
        /// after RunBatch; validates the persisted setup, force-reimports each source, and samples the prefabs.
        /// </summary>
        public static void ValidateOwnSourcesBatch()
        {
            int code = 0;
            try
            {
                var manifest = JObject.Parse(File.ReadAllText(Root + "/manifest.json"));
                var sharedMap = (JObject)JObject.Parse(File.ReadAllText(Root + "/cts_humanoid_map.json"))["unity_humanoid"];
                int count = 0;
                foreach (var record in manifest["models"])
                {
                    var map = record["humanoid_map"] == null ? sharedMap :
                        (JObject)JObject.Parse(File.ReadAllText(Root + "/" + (string)record["humanoid_map"]))["unity_humanoid"];
                    foreach (string role in new[] { "crowd_fbx", "hero_fbx" })
                    {
                        string path = Root + "/" + (string)record["files"][role];
                        RequireOwnSource(path, map);
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                        RequireOwnSource(path, map);
                        count++;
                    }
                }
                CastValidation.RequireAnimatable();
                Debug.Log($"CTLIFE [distinct-source] {count} own Humanoid sources survived a forced {EditorUserBuildSettings.activeBuildTarget} reimport.");
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static void RequireOwnSource(string path, JObject map)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || importer.animationType != ModelImporterAnimationType.Human ||
                importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || importer.sourceAvatar != null)
                throw new InvalidDataException("Distinct body must create its own Humanoid Avatar: " + path);
            var bones = importer.humanDescription.human.ToDictionary(b => b.humanName, b => b.boneName);
            foreach (var bone in map.Properties())
            {
                string humanName = HumanTrait.BoneName[(int)Enum.Parse(typeof(HumanBodyBones), bone.Name)];
                if (!bones.TryGetValue(humanName, out string actual) || actual != (string)bone.Value)
                    throw new InvalidDataException("Distinct body lost its reviewed bone mapping: " + path + " " + bone.Name);
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var animator = model != null ? model.GetComponent<Animator>() : null;
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman ||
                AssetDatabase.GetAssetPath(animator.avatar) != path)
                throw new InvalidDataException("Distinct body does not own its imported Humanoid Avatar: " + path);
        }
    }

    public sealed class DistinctCharacterPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(DistinctCharacterImport.Root + "/", StringComparison.Ordinal)) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileUnits = importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importBlendShapes = importer.importVisibility = importer.importCameras = importer.importLights = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.isReadable = false;
            importer.skinWeights = ModelImporterSkinWeights.Standard;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.optimizeGameObjects = false;
            bool configured = !DistinctCharacterImport.RestProbe.Contains(assetPath) && importer.userData == DistinctCharacterImport.AvatarTag;
            importer.animationType = configured ? ModelImporterAnimationType.Human : ModelImporterAnimationType.None;
            if (configured)
            {
                importer.sourceAvatar = null;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(DistinctCharacterImport.Root + "/", StringComparison.Ordinal)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.maxTextureSize = 1024;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(android);
        }
    }
}
