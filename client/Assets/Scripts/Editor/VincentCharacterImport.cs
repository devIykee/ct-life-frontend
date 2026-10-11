using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CTLife.UI;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Frozen-source Vincent import. CLI: CTLife.EditorTools.VincentCharacterImport.ImportVincentAll.
    /// Optional -ctVincentSource points to exports; -ctVincentController selects an explicit own override.
    /// Execution belongs to the coordinator; no runtime motion or shared controller is edited.
    /// An idle that cannot establish hand/sole clearance blocks registry/catalog publication. Candidate
    /// assets from a first import remain available for an isolated own-idle correction; no aliases are added.
    /// </summary>
    public sealed class VincentCharacterImport : AssetPostprocessor
    {
        public const string VincentRoot = "Assets/Art/characters/authored/Vincent/";
        public const string ModelPath = VincentRoot + "VincentAdult_Creator.fbx";
        public const string PrefabPath = VincentRoot + "Weekender.prefab";
        public const string WorldPrefabPath = VincentRoot + "WeekenderWorld.prefab";
        public const string LabelKey = "creator.look.weekender";
        private const string MappingPath = VincentRoot + "unity-humanoid-map.json";
        private const string MaterialPath = VincentRoot + "Weekender.mat";
        private const string ControllerPath = "Assets/Resources/Characters/AvatarController.controller";
        private const string RegistryPath = "Assets/Resources/Characters/CharacterRegistry.asset";
        private const string CatalogPath = "Assets/Resources/UI/CreatorLookCatalog.asset";
        private const string HumanTag = "ct-life:vincent-own-humanoid-v1";
        public static readonly string[] Look = { "skin_tone1", "head_vincent", "hair_vincent_swept", "top_shirt_mustard", "bottom_trousers_slate", "shoes_slipon_white", "acc_glasses_charcoal" };
        private static readonly string[] Morphs = { "BlinkLeft", "BlinkRight", "Smile", "BrowUp", "MouthOpen" };
        private static readonly string[] Files = { "VincentAdult_Creator.fbx", "VincentAdult_LOD1.fbx", "VincentAdult_LOD2.fbx", "VincentAdult_BaseColor.png", "VincentAdult_Normal.png", "VincentAdult_MetallicSmoothness.png" };

        private void OnPreprocessModel()
        {
            if (assetPath != ModelPath && assetPath != VincentRoot + "VincentAdult_LOD1.fbx" && assetPath != VincentRoot + "VincentAdult_LOD2.fbx") return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = true;
            mi.importCameras = false; mi.importLights = false; mi.importAnimation = false;
            mi.importBlendShapes = true; mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.animationType = assetPath == ModelPath && mi.userData == HumanTag ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; mi.sourceAvatar = null;
            mi.optimizeGameObjects = false; mi.skinWeights = ModelImporterSkinWeights.Standard;
            mi.meshCompression = ModelImporterMeshCompression.Off; mi.isReadable = true;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(VincentRoot, StringComparison.Ordinal)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = assetPath.EndsWith("_Normal.png", StringComparison.Ordinal) ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = assetPath.EndsWith("_BaseColor.png", StringComparison.Ordinal);
            ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
            ti.isReadable = false; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear; ti.maxTextureSize = 2048;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true; android.maxTextureSize = 2048; android.format = TextureImporterFormat.ASTC_4x4;
            ti.SetPlatformTextureSettings(android);
        }

        [MenuItem("CT Life/Characters/Import complete Vincent Weekender bundle")]
        public static void ImportVincentAll()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-ctVincentSource");
            string source = at >= 0 && at + 1 < args.Length ? Path.GetFullPath(args[at + 1]) : Path.Combine(repo, "art/character-candidates/vincent-adult-production/exports");
            var gate = RequireFrozenBundle(source);
            var mapping = ReadMapping(Path.Combine(Path.GetDirectoryName(source), "unity-humanoid-map.json"));
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>(RegistryPath);
            var catalog = AssetDatabase.LoadAssetAtPath<CreatorLookCatalog>(CatalogPath);
            Require(registry != null && catalog != null, "Existing character registry and creator catalog are required.");
            string key = string.Join("|", Look);
            var existing = registry.looks.SingleOrDefault(entry => entry.key == key);
            var controller = existing != null && existing.controllerOverride != null ? existing.controllerOverride : registry.controller;
            int controllerAt = Array.IndexOf(args, "-ctVincentController");
            bool explicitController = controllerAt >= 0;
            if (explicitController)
            {
                Require(controllerAt + 1 < args.Length, "Missing explicit Vincent controller asset path.");
                string path = args[controllerAt + 1];
                Require(path.StartsWith(VincentRoot, StringComparison.Ordinal) || path.StartsWith("Assets/Art/characters/authored/VincentIdle/", StringComparison.Ordinal),
                    "An explicit character controller must live in Vincent's own asset folder.");
                controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
            }
            Require(controller != null, "Shared/current Weekender controller is missing.");
            if (controller != registry.controller)
                Require(controller is AnimatorOverrideController own && own.runtimeAnimatorController == registry.controller,
                    "Vincent override must be explicitly based on the current shared controller; rebase and validate after a shared-controller GUID change.");
            // Re-import preserves a previously validated, exact-entry own idle override. No other character's
            // override is automatically borrowed. The initial shared idle is explicitly tested below.
            var protectedControllerHashes = ControllerFiles(controller).ToDictionary(p => p, Hash);
            string registryJson = EditorJsonUtility.ToJson(registry), catalogJson = EditorJsonUtility.ToJson(catalog);
            var managed = Files.Select(file => VincentRoot + file).Concat(new[] { MappingPath, MaterialPath, PrefabPath, WorldPrefabPath }).ToArray();
            var previous = managed.SelectMany(path => new[] { path, path + ".meta" }).ToDictionary(path => path, ReadIfPresent);
            var receipt = new JObject { ["all_pass"] = false, ["source"] = source, ["source_fbx_gate"] = gate, ["look"] = new JArray(Look), ["label_key"] = LabelKey, ["native_checks"] = new JArray() };
            bool published = false;
            try
            {
                Directory.CreateDirectory(VincentRoot);
                foreach (string file in Files) File.Copy(Path.Combine(source, file), VincentRoot + file, true);
                File.Copy(Path.Combine(Path.GetDirectoryName(source), "unity-humanoid-map.json"), MappingPath, true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
                importer.userData = "ct-life:vincent-fresh-rest";
                importer.humanDescription = new HumanDescription();
                AssetDatabase.WriteImportSettingsIfDirty(ModelPath);
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                ConfigureOwnAvatar(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), importer, mapping);
                var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().SingleOrDefault();
                Require(avatar != null && avatar.isValid && avatar.isHuman && AssetDatabase.GetAssetPath(avatar) == ModelPath,
                    "Vincent must have a valid own Avatar from this exact model.");
                var material = ConfigureMaterial();
                BuildCreator(avatar, controller, material);
                BuildWorld();
                var checks = (JArray)receipt["native_checks"];
                ValidateNativeAsset(PrefabPath, avatar, mapping, gate, checks);
                ValidateNativeAsset(WorldPrefabPath, avatar, mapping, gate, checks);
                // Historical function name; this checks the common 54 canonical names against the candidate's
                // OWN Avatar, then samples nine real shared motion states. It does not copy Snow transforms.
                CharacterValidation.ValidateSnowAsset(PrefabPath);
                CharacterValidation.ValidateSnowAsset(WorldPrefabPath);
                ValidateIdle(PrefabPath, avatar, controller, checks);
                ValidateIdle(WorldPrefabPath, avatar, controller, checks);
                foreach (var pair in protectedControllerHashes) Require(Hash(pair.Key) == pair.Value, "Import changed a protected controller/clip: " + pair.Key);

                var entry = existing ?? new CharacterRegistry.Entry { key = key };
                if (existing == null) registry.looks.Add(entry);
                entry.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorldPrefabPath);
                entry.previewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                entry.height = 0f;
                if (explicitController) entry.controllerOverride = controller;
                // Otherwise keep entry.controllerOverride; always preserve variant and unrelated entries.
                var choice = catalog.looks.SingleOrDefault(item => item.look != null && string.Join("|", item.look) == key);
                if (choice == null) { choice = new CreatorLookCatalog.Entry(); catalog.looks.Add(choice); }
                choice.look = (string[])Look.Clone(); choice.labelKey = LabelKey; choice.previewYaw = 0f;
                EditorUtility.SetDirty(registry); EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                published = true; receipt["all_pass"] = true;
                Debug.Log("CTLIFE [vincent-import] PASS own Avatar, five live morphs, tier binds, shared retarget and sampled idle contact. Published exact Weekender entry/catalog only. Backend IDs remain a proposal; device visual acceptance remains required.");
            }
            catch (Exception error)
            {
                receipt["failure"] = error.ToString();
                EditorJsonUtility.FromJsonOverwrite(registryJson, registry);
                EditorJsonUtility.FromJsonOverwrite(catalogJson, catalog);
                EditorUtility.SetDirty(registry); EditorUtility.SetDirty(catalog);
                // A failed repeat import must not replace an already published Weekender's assets.
                // A failed FIRST candidate stays unregistered so its own Avatar can support native idle repair.
                if (existing != null)
                {
                    foreach (var pair in previous) Restore(pair.Key, pair.Value);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
                AssetDatabase.SaveAssets();
                Debug.LogError("CTLIFE [vincent-import] FAILED; registry/catalog restored. " + (existing == null ? "Unregistered candidate assets retained for isolated review." : "Previous Weekender asset bytes restored."));
                throw;
            }
            finally
            {
                receipt["published"] = published;
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/vincent-import-validation.json", receipt.ToString());
            }
        }

        private static Dictionary<HumanBodyBones, string> ReadMapping(string path)
        {
            var source = JObject.Parse(File.ReadAllText(path));
            Require(source["model_importer_avatar_setup"]?.Value<string>() == "CreateFromThisModel" &&
                source["skeleton_object"]?.Value<string>() == "VincentAdult_Rig" && source["skeleton_root_bone"]?.Value<string>() == "Root", "Vincent's own mapping provenance is missing.");
            var raw = source["mapping_human_name_to_transform_name"] as JObject;
            Require(raw != null, "Explicit Humanoid map is missing.");
            var map = raw.Properties().ToDictionary(p => (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), p.Name), p => p.Value.Value<string>());
            Require(map.Count == 54 && map.Values.Distinct().Count() == 54 && !map.ContainsKey(HumanBodyBones.UpperChest), "Expected 54 distinct Humanoid mappings and separate Root, without a duplicate UpperChest.");
            return map;
        }

        private static void ConfigureOwnAvatar(GameObject model, ModelImporter importer, Dictionary<HumanBodyBones, string> mapping)
        {
            Require(model != null, "Vincent FBX failed to import.");
            var reference = Object.Instantiate(model);
            var human = new HumanDescription();
            try
            {
                reference.name = model.name;
                var transforms = reference.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                Require(transforms.ContainsKey("VincentAdult_Rig") && transforms.ContainsKey("Root") && mapping.Values.All(transforms.ContainsKey), "Fresh Vincent rest hierarchy is incomplete.");
                var setup = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool", true);
                var wrapper = setup.GetNestedType("BoneWrapper", BindingFlags.Public | BindingFlags.NonPublic);
                var enforce = setup.GetMethod("MakePoseValid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                Require(wrapper != null && enforce != null, "Pinned Unity Avatar T-pose tooling changed.");
                var bones = Array.CreateInstance(wrapper, HumanTrait.BoneCount);
                for (int i = 0; i < HumanTrait.BoneCount; i++)
                {
                    Transform bone = mapping.TryGetValue((HumanBodyBones)i, out string name) ? transforms[name] : null;
                    bones.SetValue(Activator.CreateInstance(wrapper, new object[] { HumanTrait.BoneName[i], bone }), i);
                }
                enforce.Invoke(null, new object[] { bones });
                human.skeleton = reference.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            }
            finally { Object.DestroyImmediate(reference); }
            human.human = mapping.Select(pair => new HumanBone { boneName = pair.Value, humanName = HumanTrait.BoneName[(int)pair.Key], limit = new HumanLimit { useDefaultValues = true } }).ToArray();
            human.upperArmTwist = human.lowerArmTwist = human.upperLegTwist = human.lowerLegTwist = .5f;
            human.armStretch = human.legStretch = .05f; human.feetSpacing = 0f; human.hasTranslationDoF = false;
            importer.humanDescription = human; importer.userData = HumanTag;
            importer.animationType = ModelImporterAnimationType.Human; importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null; importer.SaveAndReimport();
        }

        private static Material ConfigureMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                Require(shader != null, "URP Lit is unavailable.");
                material = new Material(shader) { name = "Weekender" }; AssetDatabase.CreateAsset(material, MaterialPath);
            }
            Texture2D Map(string suffix)
            {
                var map = AssetDatabase.LoadAssetAtPath<Texture2D>(VincentRoot + "VincentAdult_" + suffix + ".png");
                Require(map != null && map.width == 2048 && map.height == 2048, "Expected final 2048 atlas " + suffix);
                return map;
            }
            material.SetTexture("_BaseMap", Map("BaseColor")); material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", Map("Normal")); material.SetFloat("_BumpScale", 1f); material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", Map("MetallicSmoothness")); material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f); material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material); return material;
        }

        private static void BuildCreator(Avatar avatar, RuntimeAnimatorController controller, Material material)
        {
            var go = new GameObject("Weekender");
            try
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                var visual = Object.Instantiate(model, go.transform);
                visual.name = model.name;
                visual.transform.localPosition = Vector3.zero; visual.transform.localRotation = Quaternion.identity; visual.transform.localScale = Vector3.one;
                var skin = SingleSkin(visual);
                Debug.Log("CTLIFE [vincent-import] imported facial defaults: " + string.Join(",", Enumerable.Range(0, skin.sharedMesh.blendShapeCount).Select(skin.GetBlendShapeWeight)));
                NeutralizeMorphs(skin); // FBX defaults are not the playable neutral expression.
                skin.sharedMaterial = material; skin.updateWhenOffscreen = false;
                skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var animator = go.GetComponentInChildren<Animator>();
                if (animator == null) animator = visual.AddComponent<Animator>();
                animator.avatar = avatar; animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.CullCompletely;
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static void BuildWorld()
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                go.name = "WeekenderWorld";
                var high = SingleSkin(go);
                var transforms = go.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                var levels = new List<LOD> { new LOD(.28f, new Renderer[] { high }) };
                for (int level = 1; level <= 2; level++)
                {
                    var source = SingleSkin(AssetDatabase.LoadAssetAtPath<GameObject>(VincentRoot + $"VincentAdult_LOD{level}.fbx"));
                    for (int i = 0; i < source.bones.Length; i++)
                    {
                        int referenceIndex = Array.FindIndex(high.bones, bone => bone.name == source.bones[i].name);
                        Require(referenceIndex >= 0 && transforms.ContainsKey(source.bones[i].name), "LOD bone absent from Creator: " + source.bones[i].name);
                        for (int component = 0; component < 16; component++) Require(Mathf.Abs(source.sharedMesh.bindposes[i][component] - high.sharedMesh.bindposes[referenceIndex][component]) < .001f, "LOD bind differs from Vincent Creator: " + source.bones[i].name);
                    }
                    var child = new GameObject("Vincent_LOD" + level); child.transform.SetParent(high.transform.parent, false);
                    child.transform.localPosition = high.transform.localPosition; child.transform.localRotation = high.transform.localRotation; child.transform.localScale = high.transform.localScale;
                    var skin = child.AddComponent<SkinnedMeshRenderer>();
                    skin.sharedMesh = source.sharedMesh; skin.sharedMaterial = high.sharedMaterial;
                    skin.bones = source.bones.Select(bone => transforms[bone.name]).ToArray(); skin.rootBone = transforms[source.rootBone.name];
                    NeutralizeMorphs(skin);
                    skin.localBounds = high.localBounds; skin.updateWhenOffscreen = false; skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    levels.Add(new LOD(level == 1 ? .08f : .02f, new Renderer[] { skin }));
                }
                var group = go.AddComponent<LODGroup>(); group.fadeMode = LODFadeMode.None; group.SetLODs(levels.ToArray()); group.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(go, WorldPrefabPath);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static SkinnedMeshRenderer SingleSkin(GameObject model)
        {
            Require(model != null, "Imported model is missing.");
            var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Require(skins.Length == 1 && skins[0].sharedMesh != null && skins[0].sharedMesh.subMeshCount == 1, "Each Vincent tier must have one skinned mesh and one material.");
            return skins[0];
        }

        private static void ValidateNativeAsset(string path, Avatar avatar, Dictionary<HumanBodyBones, string> map, JObject gate, JArray receipt)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var baked = new Mesh();
            try
            {
                var animator = go.GetComponentsInChildren<Animator>(true).Single(); animator.enabled = false;
                Require(animator.avatar == avatar, "Prefab uses a different character's Avatar.");
                foreach (var pair in map) Require(animator.GetBoneTransform(pair.Key) != null && animator.GetBoneTransform(pair.Key).name == pair.Value, "Incorrect native mapping: " + pair.Key);
                foreach (var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = skin.sharedMesh;
                    string tier = skin.name.Contains("LOD1") ? "LOD1" : skin.name.Contains("LOD2") ? "LOD2" : "Creator";
                    Require((long)mesh.GetIndexCount(0) / 3 == gate["tiers"]?[tier]?["triangles"]?.Value<long>(), "Native triangle count differs from frozen source " + tier);
                    Require(mesh.blendShapeCount == Morphs.Length, "Source facial morph count changed in " + tier);
                    var indices = Morphs.ToDictionary(name => name, name => Enumerable.Range(0, mesh.blendShapeCount).Single(i => mesh.GetBlendShapeName(i).Split('.').Last() == name));
                    var defaults = Enumerable.Range(0, mesh.blendShapeCount).Select(skin.GetBlendShapeWeight).ToArray();
                    receipt.Add(new JObject { ["prefab"] = path, ["tier"] = tier, ["default_morph_weights"] = new JArray(defaults) });
                    Require(defaults.All(weight => Mathf.Abs(weight) < .0001f), "Published prefab expression must start neutral: " + tier);
                    NeutralizeMorphs(skin);
                    skin.BakeMesh(baked); var neutral = baked.vertices;
                    var local = neutral.Select(v => go.transform.InverseTransformPoint(skin.transform.TransformPoint(v))).ToArray();
                    float sole = local.Min(v => v.y), height = local.Max(v => v.y) - sole;
                    Require(height > 1.4f && height < 2.2f && Mathf.Abs(sole) < .005f, "Native rest height/sole is invalid: " + tier + " height=" + height + " sole=" + sole);
                    foreach (var shape in indices)
                    {
                        int frameCount = mesh.GetBlendShapeFrameCount(shape.Value);
                        Require(frameCount > 0, "Empty facial morph " + shape.Key);
                        var rawDeltas = new Vector3[mesh.vertexCount];
                        var frameFacts = new JArray(); float rawMaximum = 0f;
                        for (int frame = 0; frame < frameCount; frame++)
                        {
                            mesh.GetBlendShapeFrameVertices(shape.Value, frame, rawDeltas, null, null);
                            Require(rawDeltas.All(Finite), "Nonfinite imported morph deltas: " + shape.Key);
                            float maximumRaw = rawDeltas.Max(delta => delta.magnitude);
                            rawMaximum = Mathf.Max(rawMaximum, maximumRaw);
                            frameFacts.Add(new JObject { ["weight"] = mesh.GetBlendShapeFrameWeight(shape.Value, frame), ["raw_mesh_max_delta_m"] = maximumRaw,
                                ["vertices_above10micrometres"] = rawDeltas.Count(delta => delta.sqrMagnitude > 1e-10f) });
                        }
                        NeutralizeMorphs(skin);
                        skin.SetBlendShapeWeight(shape.Value, 100f); skin.BakeMesh(baked);
                        var changed = baked.vertices;
                        Require(changed.Length == neutral.Length && changed.All(v => Finite(v)), "Nonfinite facial morph " + shape.Key);
                        float maximum = 0f;
                        for (int i = 0; i < changed.Length; i++) maximum = Mathf.Max(maximum, Vector3.Distance(changed[i], neutral[i]));
                        receipt.Add(new JObject { ["prefab"] = path, ["tier"] = tier, ["morph"] = shape.Key,
                            ["imported_frames"] = frameFacts, ["weight_at_sample"] = skin.GetBlendShapeWeight(shape.Value), ["native_max_delta_m"] = maximum });
                        Require(rawMaximum > .00001f, "Facial morph has no imported vertex deltas: " + shape.Key);
                        Require(maximum > .00001f, "Facial morph has no native surface deformation from neutral: " + shape.Key);
                        NeutralizeMorphs(skin);
                    }
                    receipt.Add(new JObject { ["prefab"] = path, ["tier"] = tier, ["rest_height_m"] = height, ["rest_sole_m"] = sole, ["triangles"] = (long)mesh.GetIndexCount(0) / 3, ["morphs"] = new JArray(Morphs) });
                }
            }
            finally { Object.DestroyImmediate(baked); Object.DestroyImmediate(go); }
        }

        private static void NeutralizeMorphs(SkinnedMeshRenderer skin)
        {
            for (int shape = 0; shape < skin.sharedMesh.blendShapeCount; shape++) skin.SetBlendShapeWeight(shape, 0f);
        }

        private sealed class SkinProbe
        {
            public SkinnedMeshRenderer skin; public Mesh baked;
            public int[] leftHand, rightHand, lowerBody, leftSurface, rightSurface, lowerSurface;
        }
        private static void ValidateIdle(string path, Avatar avatar, RuntimeAnimatorController controller, JArray receipt)
        {
            ValidateSurfaceMath();
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject go = null; var probes = new List<SkinProbe>();
            try
            {
                go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)); SceneManager.MoveGameObjectToScene(go, scene);
                var animator = go.GetComponentsInChildren<Animator>(true).Single();
                Require(animator.avatar == avatar, "Idle must use Vincent's own Avatar.");
                animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false; animator.enabled = true; animator.Rebind();
                foreach (var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var weights = skin.sharedMesh.boneWeights; var names = skin.bones.Select(b => b.name).ToArray();
                    int[] Select(Func<string, bool> predicate) => Enumerable.Range(0, weights.Length).Where(i =>
                    {
                        var w = weights[i];
                        float Part(int bone, float weight) => weight > 0f && predicate(names[bone]) ? weight : 0f;
                        return Part(w.boneIndex0, w.weight0) + Part(w.boneIndex1, w.weight1) + Part(w.boneIndex2, w.weight2) + Part(w.boneIndex3, w.weight3) > .5f;
                    }).ToArray();
                    bool Hand(string name, string side) => name == "Hand." + side || name.EndsWith("." + side, StringComparison.Ordinal) && new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" }.Any(d => name.StartsWith(d, StringComparison.Ordinal));
                    var probe = new SkinProbe { skin = skin, baked = new Mesh(), leftHand = Select(n => Hand(n, "L")), rightHand = Select(n => Hand(n, "R")), lowerBody = Select(n => n == "Hips" || n.StartsWith("Thigh.", StringComparison.Ordinal)) };
                    probe.leftSurface=SurfaceFaces(skin.sharedMesh.triangles,probe.leftHand);
                    probe.rightSurface=SurfaceFaces(skin.sharedMesh.triangles,probe.rightHand);
                    probe.lowerSurface=SurfaceFaces(skin.sharedMesh.triangles,probe.lowerBody);
                    probes.Add(probe); Require(probe.leftHand.Length > 0 && probe.rightHand.Length > 0 && probe.lowerBody.Length > 0, "Cannot identify hand/thigh surface for native idle check.");
                }
                float minGap = float.PositiveInfinity, minSurfaceGap = .02f, maxSole = 0f, maxFootDrift = 0f; var firstFeet = new Vector3[2];
                Vector3 Local(HumanBodyBones bone) => go.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position);
                for (int sample = 0; sample <= 12; sample++)
                {
                    animator.Play("loco_idle", 0, sample / 12f); animator.Update(0f);
                    var lateral = Vector3.ProjectOnPlane(Local(HumanBodyBones.LeftUpperLeg) - Local(HumanBodyBones.RightUpperLeg), Vector3.up).normalized;
                    var face = (Local(HumanBodyBones.LeftEye) + Local(HumanBodyBones.RightEye)) * .5f - Local(HumanBodyBones.Head);
                    Require(lateral.sqrMagnitude > .9f && face.z > .02f, "Idle anatomical basis/facing is invalid.");
                    var feet = new[] { Local(HumanBodyBones.LeftFoot), Local(HumanBodyBones.RightFoot) };
                    for (int side = 0; side < 2; side++) { if (sample == 0) firstFeet[side] = feet[side]; maxFootDrift = Mathf.Max(maxFootDrift, Vector3.Distance(firstFeet[side], feet[side])); }
                    foreach (var probe in probes)
                    {
                        probe.skin.BakeMesh(probe.baked);
                        var vertices = probe.baked.vertices.Select(v => go.transform.InverseTransformPoint(probe.skin.transform.TransformPoint(v))).ToArray();
                        Require(vertices.All(Finite), "Nonfinite idle surface.");
                        float sole = vertices.Min(v => v.y); maxSole = Mathf.Max(maxSole, Mathf.Abs(sole));
                        Require(Mathf.Abs(sole) < .03f, "Shared/current idle does not keep Vincent's soles near the floor: " + sole + "m. Prepare an isolated own idle; do not lift the entire character.");
                        foreach (float side in new[] { 1f, -1f })
                        {
                            var hand = side > 0 ? probe.leftHand : probe.rightHand;
                            float low = hand.Min(i => vertices[i].y) - .04f, high = hand.Max(i => vertices[i].y) + .04f;
                            var thigh = probe.lowerBody.Where(i => vertices[i].y >= low && vertices[i].y <= high).ToArray();
                            Require(thigh.Length > 0, "No thigh surface at hand height; lateral clearance metric is inapplicable.");
                            float gap = hand.Min(i => side * Vector3.Dot(vertices[i], lateral)) - thigh.Max(i => side * Vector3.Dot(vertices[i], lateral));
                            minGap = Mathf.Min(minGap, gap);
                            // Lateral overlap alone is inapplicable when the hand lies in front of the thigh.
                            // Use all selected surface triangles, including their boundary faces. The 2mm
                            // margin is an engineering contact tolerance, not the old 10mm axis heuristic.
                            float surfaceGap=SurfaceDistance(vertices,side>0?probe.leftSurface:probe.rightSurface,probe.lowerSurface,.02f);
                            minSurfaceGap=Mathf.Min(minSurfaceGap,surfaceGap);
                            receipt.Add(new JObject { ["prefab"]=path,["skin"]=probe.skin.name,["phase"]=sample/12f,["side"]=side>0?"left":"right",
                                ["lateral_proxy_gap_m"]=gap,["triangle_surface_gap_capped_20mm_m"]=surfaceGap,["required_engineering_margin_m"]=.002f });
                            Require(surfaceGap>=.002f,$"Vincent idle hand/thigh surface gap below 2mm engineering tolerance: {probe.skin.name}, phase={sample}/12, side={side}, gap={surfaceGap:F6}m. Selected surfaces are not a whole-body containment test.");
                        }
                    }
                }
                Require(maxFootDrift < .02f, "Current idle moves Vincent's feet more than 20mm: " + maxFootDrift);
                receipt.Add(new JObject { ["prefab"] = path, ["idle_controller"] = AssetDatabase.GetAssetPath(controller), ["sampled_phases"] = 13, ["skins"] = probes.Count, ["min_lateral_hand_clearance_m"] = minGap, ["min_triangle_surface_gap_capped_20mm_m"] = minSurfaceGap, ["surface_math_fixtures_pass"] = true, ["max_sole_offset_m"] = maxSole, ["max_foot_drift_m"] = maxFootDrift });
                Debug.Log($"CTLIFE [vincent-idle] {path}: minTriangleGap={minSurfaceGap:F5}m (capped20mm); lateralProxy={minGap:F5}m; maxSole={maxSole:F5}m; maxFootDrift={maxFootDrift:F5}m. Device pose review still required.");
            }
            finally
            {
                foreach (var probe in probes) Object.DestroyImmediate(probe.baked);
                if (go != null) Object.DestroyImmediate(go); EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static JObject RequireFrozenBundle(string exports)
        {
            string root = Path.GetDirectoryName(exports);
            Require(File.Exists(Path.Combine(root, "bundle-ready.json")), "Vincent's artist-reviewed bundle is not frozen yet: " + root);
            var ready = JObject.Parse(File.ReadAllText(Path.Combine(root, "bundle-ready.json")));
            var gate = JObject.Parse(File.ReadAllText(Path.Combine(root, "fbx-export-validation.json")));
            Require(ready["all_pass"]?.Value<bool>() == true && gate["all_pass"]?.Value<bool>() == true, "Vincent export/UV/visual bundle is not frozen and passing.");
            foreach (string file in Files)
            {
                string actual = Hash(Path.Combine(exports, file));
                string frozen = ready["files"]?[file]?.Value<string>() ?? ready["files"]?["exports/" + file]?.Value<string>();
                string gateHash = file.EndsWith(".fbx", StringComparison.Ordinal) ? gate["tiers"]?[Path.GetFileNameWithoutExtension(file).Substring("VincentAdult_".Length)]?["sha256"]?.Value<string>() : gate["texture_sha256"]?[file]?.Value<string>();
                Require(actual == frozen && actual == gateHash, "Vincent file differs from frozen native-source gate: " + file);
            }
            Require(Hash(Path.Combine(root, "unity-humanoid-map.json")) == ready["files"]?["unity-humanoid-map.json"]?.Value<string>(), "Explicit Vincent mapping changed since freeze.");
            return gate;
        }
        // Editor-only finite-triangle distance. BVH bounds prune pairs that cannot improve the
        // current distance; includes edge-through-face intersections and selection-boundary faces.
        private struct SurfaceTriangle
        {
            public Vector3 a,b,c,min,max,centre;
            public SurfaceTriangle(Vector3 x,Vector3 y,Vector3 z)
            { a=x;b=y;c=z;min=Vector3.Min(x,Vector3.Min(y,z));max=Vector3.Max(x,Vector3.Max(y,z));centre=(x+y+z)/3f; }
            public Vector3 Point(int i)=>i==0?a:i==1?b:c;
        }
        private sealed class SurfaceNode
        {
            public Vector3 min,max;public int start,count;public SurfaceNode left,right;
        }
        private static int[] SurfaceFaces(int[] triangles,int[] region)
        {
            var wanted=new HashSet<int>(region);var result=new List<int>();
            for(int i=0;i<triangles.Length;i+=3)
                if(wanted.Contains(triangles[i])||wanted.Contains(triangles[i+1])||wanted.Contains(triangles[i+2]))
                {result.Add(triangles[i]);result.Add(triangles[i+1]);result.Add(triangles[i+2]);}
            return result.ToArray();
        }
        private static SurfaceTriangle[] SurfaceTriangles(Vector3[] vertices,int[] indices)
        {
            var result=new SurfaceTriangle[indices.Length/3];
            for(int i=0;i<result.Length;i++)result[i]=new SurfaceTriangle(vertices[indices[i*3]],vertices[indices[i*3+1]],vertices[indices[i*3+2]]);
            return result;
        }
        private static SurfaceNode BuildSurfaceTree(SurfaceTriangle[] triangles,int start,int count)
        {
            var node=new SurfaceNode{start=start,count=count,min=triangles[start].min,max=triangles[start].max};
            for(int i=start+1;i<start+count;i++){node.min=Vector3.Min(node.min,triangles[i].min);node.max=Vector3.Max(node.max,triangles[i].max);}
            if(count<=8)return node;
            Vector3 size=node.max-node.min;int axis=size.x>=size.y&&size.x>=size.z?0:size.y>=size.z?1:2;
            Array.Sort(triangles,start,count,Comparer<SurfaceTriangle>.Create((a,b)=>a.centre[axis].CompareTo(b.centre[axis])));
            int half=count/2;node.left=BuildSurfaceTree(triangles,start,half);node.right=BuildSurfaceTree(triangles,start+half,count-half);return node;
        }
        private static float BoundsDistanceSq(Vector3 aMin,Vector3 aMax,Vector3 bMin,Vector3 bMax)
        {
            float x=Mathf.Max(0,Mathf.Max(aMin.x-bMax.x,bMin.x-aMax.x));
            float y=Mathf.Max(0,Mathf.Max(aMin.y-bMax.y,bMin.y-aMax.y));
            float z=Mathf.Max(0,Mathf.Max(aMin.z-bMax.z,bMin.z-aMax.z));return x*x+y*y+z*z;
        }
        private static void ClosestSurface(SurfaceTriangle query,SurfaceTriangle[] triangles,SurfaceNode node,ref float bestSq)
        {
            if(BoundsDistanceSq(query.min,query.max,node.min,node.max)>bestSq)return;
            if(node.left==null)
            {
                for(int i=node.start;i<node.start+node.count;i++)
                    if(BoundsDistanceSq(query.min,query.max,triangles[i].min,triangles[i].max)<=bestSq)bestSq=Mathf.Min(bestSq,TriangleDistanceSq(query,triangles[i]));
                return;
            }
            float left=BoundsDistanceSq(query.min,query.max,node.left.min,node.left.max),right=BoundsDistanceSq(query.min,query.max,node.right.min,node.right.max);
            if(left<=right){ClosestSurface(query,triangles,node.left,ref bestSq);ClosestSurface(query,triangles,node.right,ref bestSq);}
            else {ClosestSurface(query,triangles,node.right,ref bestSq);ClosestSurface(query,triangles,node.left,ref bestSq);}
        }
        private static float SurfaceDistance(Vector3[] vertices,int[] hand,int[] thigh,float cap)
        {
            Require(hand.Length>0&&thigh.Length>0,"Empty contact surface triangles.");
            var target=SurfaceTriangles(vertices,thigh);var tree=BuildSurfaceTree(target,0,target.Length);float best=cap*cap;
            foreach(var triangle in SurfaceTriangles(vertices,hand))ClosestSurface(triangle,target,tree,ref best);
            return Mathf.Sqrt(best);
        }
        private static float TriangleDistanceSq(SurfaceTriangle a,SurfaceTriangle b)
        {
            for(int i=0;i<3;i++)if(SegmentCrossesTriangle(a.Point(i),a.Point((i+1)%3),b)||SegmentCrossesTriangle(b.Point(i),b.Point((i+1)%3),a))return 0;
            float best=float.PositiveInfinity;
            for(int i=0;i<3;i++)
            {
                best=Mathf.Min(best,PointTriangleSq(a.Point(i),b));best=Mathf.Min(best,PointTriangleSq(b.Point(i),a));
                for(int j=0;j<3;j++)best=Mathf.Min(best,SegmentDistanceSq(a.Point(i),a.Point((i+1)%3),b.Point(j),b.Point((j+1)%3)));
            }
            return best;
        }
        private static bool SegmentCrossesTriangle(Vector3 a,Vector3 b,SurfaceTriangle tri)
        {
            var direction=b-a;var edge1=tri.b-tri.a;var edge2=tri.c-tri.a;var p=Vector3.Cross(direction,edge2);float det=Vector3.Dot(edge1,p);
            if(Mathf.Abs(det)<1e-12f)return false;
            float inv=1f/det;var t=a-tri.a;float u=Vector3.Dot(t,p)*inv;if(u<0||u>1)return false;
            var q=Vector3.Cross(t,edge1);float v=Vector3.Dot(direction,q)*inv;if(v<0||u+v>1)return false;
            float distance=Vector3.Dot(edge2,q)*inv;return distance>=0&&distance<=1;
        }
        private static float PointTriangleSq(Vector3 p,SurfaceTriangle t)
        {
            var ab=t.b-t.a;var ac=t.c-t.a;var ap=p-t.a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if(d1<=0&&d2<=0)return ap.sqrMagnitude;
            var bp=p-t.b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0&&d4<=d3)return bp.sqrMagnitude;
            float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return (p-(t.a+ab*(d1/(d1-d3)))).sqrMagnitude;
            var cp=p-t.c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6)return cp.sqrMagnitude;
            float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return (p-(t.a+ac*(d2/(d2-d6)))).sqrMagnitude;
            float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return (p-(t.b+(t.c-t.b)*((d4-d3)/((d4-d3)+(d5-d6))))).sqrMagnitude;
            float denom=va+vb+vc;
            if(Mathf.Abs(denom)<1e-20f)return Mathf.Min(SegmentDistanceSq(p,p,t.a,t.b),Mathf.Min(SegmentDistanceSq(p,p,t.b,t.c),SegmentDistanceSq(p,p,t.c,t.a)));
            return (p-(t.a+ab*(vb/denom)+ac*(vc/denom))).sqrMagnitude;
        }
        private static float SegmentDistanceSq(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            var u=b-a;var v=d-c;var w=a-c;float aa=Vector3.Dot(u,u),bb=Vector3.Dot(u,v),cc=Vector3.Dot(v,v),dd=Vector3.Dot(u,w),ee=Vector3.Dot(v,w);
            if(aa<1e-20f)return cc<1e-20f?(a-c).sqrMagnitude:(a-(c+v*Mathf.Clamp01(ee/cc))).sqrMagnitude;
            if(cc<1e-20f)return (c-(a+u*Mathf.Clamp01(-dd/aa))).sqrMagnitude;
            float den=aa*cc-bb*bb,s=den>1e-20f?Mathf.Clamp01((bb*ee-cc*dd)/den):0,t=(bb*s+ee)/cc;
            if(t<0){t=0;s=Mathf.Clamp01(-dd/aa);}else if(t>1){t=1;s=Mathf.Clamp01((bb-dd)/aa);}
            return ((a+s*u)-(c+t*v)).sqrMagnitude;
        }
        private static void ValidateSurfaceMath()
        {
            var a=new SurfaceTriangle(Vector3.zero,Vector3.right,Vector3.up);
            var b=new SurfaceTriangle(a.a+Vector3.forward*.003f,a.b+Vector3.forward*.003f,a.c+Vector3.forward*.003f);
            Require(Mathf.Abs(Mathf.Sqrt(TriangleDistanceSq(a,b))-.003f)<1e-6f,"Parallel-triangle distance fixture failed.");
            var crossing=new SurfaceTriangle(new Vector3(.25f,.25f,-1),new Vector3(.25f,.25f,1),new Vector3(.75f,.25f,0));
            Require(TriangleDistanceSq(a,crossing)<1e-12f,"Edge-through-face crossing fixture failed.");
            Require(Mathf.Abs(Mathf.Sqrt(SegmentDistanceSq(Vector3.zero,Vector3.right,new Vector3(.5f,-1,.001f),new Vector3(.5f,1,.001f)))-.001f)<1e-6f,"Edge-edge distance fixture failed.");
        }

        private static IEnumerable<string> ControllerFiles(RuntimeAnimatorController controller)
        {
            var paths = new HashSet<string> { ControllerPath, AssetDatabase.GetAssetPath(controller) };
            if (controller is AnimatorOverrideController overrides) paths.Add(AssetDatabase.GetAssetPath(overrides.runtimeAnimatorController));
            foreach (var clip in controller.animationClips) paths.Add(AssetDatabase.GetAssetPath(clip));
            return paths.Where(path => !string.IsNullOrEmpty(path));
        }
        private static byte[] ReadIfPresent(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
        private static void Restore(string path, byte[] previous) { if (previous == null) { if (File.Exists(path)) File.Delete(path); } else File.WriteAllBytes(path, previous); }
        private static bool Finite(Vector3 value) => !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);
        private static string Hash(string path) { using var hash = SHA256.Create(); using var stream = File.OpenRead(path); return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        private static void Require(bool value, string message) { if (!value) throw new InvalidDataException("Vincent import: " + message); }
    }
}
