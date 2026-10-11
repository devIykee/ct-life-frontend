using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.UI;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Imports the CT cast exported by the cast lane (art/cast/export, manifest.json) per its import plan
    /// (docs/progress/requests.md "2026-10-08 cast → integration lead"):
    /// looks → Assets/Art/characters/cast/looks/&lt;look&gt;/ (Humanoid, Copy From Other Avatar: family m → Snow,
    /// f → Rain creator avatar), one URP/Lit material per look, ASTC 6x6 colour / 4x4 normal, 512 or 1024 max,
    /// prefabs with the manifest root scale and an LODGroup (LOD0/1/2 from the _LODn meshes; tiers are driven by
    /// AvatarLod's 12/24 count rule), then: contract archetypes → CharacterRegistry.archetypes, identity looks keep the
    /// existing Snow/Rain look keys, player looks become creator choices.
    /// Batch: Unity -batchmode -quit -projectPath client -executeMethod CTLife.EditorTools.CastImport.RunBatch
    /// Runtime wardrobe parts (art/cast/export/parts) are not imported yet (runtime merge is a later step).
    /// </summary>
    public static class CastImport
    {
        public const string Root = "Assets/Art/characters/cast";
        private const string LooksRoot = Root + "/looks";
        private const string PrefabRoot = Root + "/prefabs";
        private const string MaterialRoot = Root + "/materials";
        private const string ManifestAsset = Root + "/manifest.json";
        private const string RegistryPath = "Assets/Resources/Characters/CharacterRegistry.asset";
        private const string CreatorCatalogPath = "Assets/Resources/UI/CreatorLookCatalog.asset";
        public const string SnowAvatarFbx = "Assets/Art/characters/authored/Snow/SnowAdult_Creator.fbx";
        public const string RainAvatarFbx = "Assets/Art/characters/authored/Rain/RainAdult_Creator.fbx";
        // Codex-tuned family motion (Snow: contact-fitted walk; Rain: own relaxed idle). Humanoid muscle clips, so they
        // drive every look of the family; the cast import used to clear them.
        public const string SnowControllerPath = "Assets/Resources/Characters/SnowController.overrideController";
        public const string RainControllerPath = "Assets/Resources/Characters/RainController.overrideController";
        /// <summary>userData of a cast model whose own Humanoid Avatar was built by <see cref="ConfigureAvatar"/>.</summary>
        public const string AvatarTag = "ct-life:cast-own-avatar-v1";
        /// <summary>Models being re-read for their rest skeleton: the postprocessor imports them unoptimized, no rig.</summary>
        internal static readonly HashSet<string> RestProbe = new HashSet<string>(StringComparer.Ordinal);
        // Existing saved characters use these keys; they now resolve to the cast identity looks.
        private const string SnowKey = "skin_tone3|head_snow|hair_snow_bun|top_vneck_charcoal|bottom_denim_light|shoes_sneakers_blue|acc_none";
        private const string RainKey = "skin_tone3|head_rain|hair_rain_ponytail|top_sleeveless_white|bottom_denim_fitted|shoes_canvas_ochre|acc_scarf_sky";

        private static string RepoExport => Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/cast/export"));

        private static JObject _manifest;
        internal static JObject Manifest
        {
            get
            {
                if (_manifest != null) return _manifest;
                var path = File.Exists(ManifestAsset) ? ManifestAsset : Path.Combine(RepoExport, "manifest.json");
                return _manifest = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null;
            }
        }

        internal static JObject Look(string lookId) => Manifest?["looks"]?[lookId] as JObject;

        /// <summary>Look id from an asset path under the cast looks folder, else null.</summary>
        internal static string LookIdFromPath(string assetPath)
        {
            if (!assetPath.StartsWith(LooksRoot + "/", StringComparison.Ordinal)) return null;
            var rest = assetPath.Substring(LooksRoot.Length + 1);
            int slash = rest.IndexOf('/');
            return slash > 0 ? rest.Substring(0, slash) : null;
        }

        [MenuItem("CT Life/Cast/Import cast from art/cast/export")]
        public static void Run()
        {
            _manifest = null;
            var manifestPath = Path.Combine(RepoExport, "manifest.json");
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("Cast manifest missing", manifestPath);
            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            Directory.CreateDirectory(Root);
            File.Copy(manifestPath, ManifestAsset, true);
            _manifest = manifest;

            // 1) Copy the files the manifest lists (FBX + textures); unchanged files are left alone.
            int copied = 0;
            foreach (var kv in (JObject)manifest["looks"])
            {
                var dst = Path.Combine(LooksRoot, kv.Key);
                Directory.CreateDirectory(dst);
                foreach (var f in ((JObject)kv.Value["files"]).Properties())
                {
                    var src = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", f.Value.ToString()));
                    var to = Path.Combine(dst, Path.GetFileName(src));
                    if (File.Exists(to) && new FileInfo(to).Length == new FileInfo(src).Length) continue;
                    File.Copy(src, to, true);
                    copied++;
                }
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"CTLIFE [cast] copied {copied} files; importing with CastPostprocessor");

            // 1b) Every look gets its own Humanoid Avatar (stored in its .meta; no cross-asset import dependency).
            int avatars = ConfigureAvatars(force: false);
            Debug.Log($"CTLIFE [cast] {avatars} look Avatars (re)built");

            // 2) Materials and prefabs.
            Directory.CreateDirectory(PrefabRoot);
            Directory.CreateDirectory(MaterialRoot);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? throw new InvalidOperationException("URP Lit shader missing");
            var crowd = new Dictionary<string, GameObject>();
            var hero = new Dictionary<string, GameObject>();
            int problems = 0;
            foreach (var kv in (JObject)manifest["looks"])
            {
                try
                {
                    var look = (JObject)kv.Value;
                    var mat = BuildMaterial(kv.Key, look, shader);
                    crowd[kv.Key] = BuildPrefab(kv.Key, look, mat, hero: false);
                    if (look["files"]?["hero_fbx"] != null) hero[kv.Key] = BuildPrefab(kv.Key, look, mat, hero: true);
                }
                catch (Exception e) { problems++; Debug.LogError($"CTLIFE [cast] {kv.Key}: {e.Message}"); }
            }
            AssetDatabase.SaveAssets();

            // 3) Registry: archetypes, identity looks, player looks; creator choices.
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>(RegistryPath) ?? throw new FileNotFoundException(RegistryPath);
            int archetypes = 0;
            foreach (var kv in (JObject)manifest["contract_binding"]["archetype_to_looks"])
            {
                var first = (kv.Value as JArray)?.FirstOrDefault()?.ToString();
                if (first == null || !crowd.TryGetValue(first, out var prefab) || prefab == null) continue;
                Upsert(registry.archetypes, kv.Key, prefab, null, FamilyController(first));
                archetypes++;
            }
            if (crowd.TryGetValue("snow_everyday", out var snow)) Upsert(registry.looks, SnowKey, snow, hero.GetValueOrDefault("snow_everyday"), FamilyController("snow_everyday"));
            if (crowd.TryGetValue("rain_breeze", out var rain)) Upsert(registry.looks, RainKey, rain, hero.GetValueOrDefault("rain_breeze"), FamilyController("rain_breeze"));
            if (crowd.TryGetValue("snow_everyday", out var defaultPlayer)) registry.defaultPlayer = defaultPlayer;
            var catalog = AssetDatabase.LoadAssetAtPath<CreatorLookCatalog>(CreatorCatalogPath) ?? throw new FileNotFoundException(CreatorCatalogPath);
            int players = 0;
            foreach (var kv in (JObject)manifest["looks"])
            {
                if ((string)kv.Value["kind"] != "player" || !crowd.TryGetValue(kv.Key, out var prefab)) continue;
                var ids = PlayerLookIds(kv.Key, (JObject)kv.Value);
                Upsert(registry.looks, string.Join("|", ids), prefab, hero.GetValueOrDefault(kv.Key), FamilyController(kv.Key));
                var entry = catalog.looks.FirstOrDefault(e => e.look != null && string.Join("|", e.look) == string.Join("|", ids));
                if (entry == null) catalog.looks.Add(entry = new CreatorLookCatalog.Entry());
                entry.look = ids;
                entry.labelKey = "cast." + kv.Key;
                entry.label = (string)kv.Value["label"] ?? kv.Key;
                // Unity Humanoid faces +Z and the preview camera sits in front at +Z, as for Snow/Rain: 0 shows the face
                // (180 showed every cast look's back in the creator; editor captures 2026-10-09).
                entry.previewYaw = 0f;
                players++;
            }
            EditorUtility.SetDirty(registry);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            var report = CastValidation.Report(out int unanimatable);
            problems += unanimatable;
            Debug.Log($"CTLIFE [cast] {crowd.Count} looks ({hero.Count} hero), {archetypes} archetypes bound, {players} player looks in the creator, {problems} problems");
            if (unanimatable > 0) throw new InvalidDataException($"{unanimatable} spawnable characters cannot animate: " + CastValidation.Summary(report));
        }

        /// <summary>Family motion override for a look (m → Snow, f → Rain); null keeps the shared controller.</summary>
        internal static RuntimeAnimatorController FamilyController(string lookId)
        {
            var family = (string)Look(lookId)?["family"];
            var path = family == "f" ? RainControllerPath : family == "m" ? SnowControllerPath : null;
            return path == null ? null : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
        }

        /// <summary>Cast model FBX paths (crowd and hero) listed by the manifest.</summary>
        internal static IEnumerable<string> ModelPaths()
        {
            foreach (var kv in (JObject)Manifest["looks"])
                foreach (var key in new[] { "crowd_fbx", "hero_fbx" })
                    if (kv.Value["files"]?[key] != null)
                        yield return $"{LooksRoot}/{kv.Key}/{Path.GetFileName(kv.Value["files"][key].ToString())}";
        }

        [MenuItem("CT Life/Cast/Rebuild look Avatars")]
        public static void RebuildAvatars() => Debug.Log($"CTLIFE [cast] {ConfigureAvatars(force: true)} look Avatars rebuilt");

        /// <summary>Batch: rebuild every look Avatar, then every prefab and registry binding (no file copy needed).</summary>
        public static void RepairBatch()
        {
            int code = 0;
            try { _manifest = null; ConfigureAvatars(force: true); Run(); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        internal static int ConfigureAvatars(bool force)
        {
            int n = 0;
            foreach (var path in ModelPaths())
            {
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) throw new FileNotFoundException("Cast model not imported", path);
                if (!force && mi.userData == AvatarTag && ModelAvatarValid(path)) continue;
                ConfigureAvatar(path, mi);
                n++;
            }
            return n;
        }

        private static bool ModelAvatarValid(string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var animator = model != null ? model.GetComponent<Animator>() : null;
            return animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
        }

        /// <summary>
        /// Builds the look's own Humanoid Avatar exactly the way Snow and Rain build theirs (AuthoredCharacterImport /
        /// RainCharacterImport): the family's explicit 54-bone map, and a skeleton description captured from THIS
        /// model's rest transforms after Unity's Enforce T-Pose. Copy From Other Avatar cannot work here: Snow/Rain's
        /// skeleton description names their own root and mesh nodes (SnowAdult_Creator, SnowAdult_CreatorMesh), which no
        /// cast look has, so Unity silently builds no Avatar and adds no Animator (the 2026-10-08 T-pose).
        /// </summary>
        internal static void ConfigureAvatar(string path, ModelImporter mi)
        {
            // 1) Re-read the rest hierarchy unoptimized (no rig), so every bone transform is present.
            RestProbe.Add(path);
            try { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport); }
            finally { RestProbe.Remove(path); }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new InvalidDataException("Cast model did not import: " + path);
            var human = new HumanDescription();
            var reference = UnityEngine.Object.Instantiate(model);
            try
            {
                reference.name = model.name;
                var map = CharacterValidation.SnowBoneMap(); // Snow and Rain share the 55-bone naming
                var transforms = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var t in reference.GetComponentsInChildren<Transform>(true)) if (!transforms.ContainsKey(t.name)) transforms[t.name] = t;
                var missing = map.Values.Where(b => !transforms.ContainsKey(b)).ToList();
                if (missing.Count > 0) throw new InvalidDataException($"{path}: bones missing from the cast rig: {string.Join(", ", missing)}");
                // Unity's Enforce T-Pose (AvatarSetupTool.MakePoseValid), as in AuthoredCharacterImport (pinned to 6000.3).
                var setup = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool", true);
                var wrapper = setup.GetNestedType("BoneWrapper", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var enforce = setup.GetMethod("MakePoseValid", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (wrapper == null || enforce == null) throw new MissingMethodException("Unity Avatar T-pose tooling changed.");
                var bones = Array.CreateInstance(wrapper, HumanTrait.BoneCount);
                for (int i = 0; i < HumanTrait.BoneCount; i++)
                {
                    Transform bone = map.TryGetValue((HumanBodyBones)i, out var boneName) ? transforms[boneName] : null;
                    bones.SetValue(Activator.CreateInstance(wrapper, new object[] { HumanTrait.BoneName[i], bone }), i);
                }
                enforce.Invoke(null, new object[] { bones });
                human.skeleton = reference.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                {
                    name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
                }).ToArray();
                human.human = map.Select(pair => new HumanBone
                {
                    boneName = pair.Value, humanName = HumanTrait.BoneName[(int)pair.Key], limit = new HumanLimit { useDefaultValues = true },
                }).ToArray();
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
            human.upperArmTwist = 0.5f; human.lowerArmTwist = 0.5f; human.upperLegTwist = 0.5f; human.lowerLegTwist = 0.5f;
            human.armStretch = 0.05f; human.legStretch = 0.05f; human.feetSpacing = 0; human.hasTranslationDoF = false;
            // 2) Own Avatar from that description; the postprocessor then optimizes the hierarchy as usual.
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.sourceAvatar = null;
            mi.humanDescription = human;
            mi.userData = AvatarTag;
            mi.SaveAndReimport();
            if (!ModelAvatarValid(path)) throw new InvalidDataException("Cast look did not build a valid Humanoid Avatar: " + path);
        }

        public static void RunBatch()
        {
            int code = 0;
            try { Run(); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>The creator/realtime 7-ID look for a cast player look (FE ids; see starter-look proposal).</summary>
        public static string[] PlayerLookIds(string lookId, JObject look)
        {
            var skin = (string)look["skin_tone"];
            return new[]
            {
                string.IsNullOrEmpty(skin) ? "skin_tone3" : "skin_" + skin,
                "head_" + lookId, "hair_" + lookId, "top_" + lookId, "bottom_" + lookId, "shoes_" + lookId, "acc_none",
            };
        }

        private static void Upsert(List<CharacterRegistry.Entry> list, string key, GameObject prefab, GameObject preview,
            RuntimeAnimatorController controller)
        {
            var e = list.FirstOrDefault(x => x != null && string.Equals(x.key, key, StringComparison.OrdinalIgnoreCase));
            if (e == null) list.Add(e = new CharacterRegistry.Entry { key = key });
            e.prefab = prefab;
            e.previewPrefab = preview;
            e.controllerOverride = controller;
            e.height = 0f;
        }

        private static Material BuildMaterial(string lookId, JObject look, Shader shader)
        {
            var path = $"{MaterialRoot}/{lookId}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            Texture2D Tex(string key) => look["files"]?[key] == null ? null :
                AssetDatabase.LoadAssetAtPath<Texture2D>($"{LooksRoot}/{lookId}/{Path.GetFileName(look["files"][key].ToString())}");
            mat.SetTexture("_BaseMap", Tex("base"));
            mat.SetColor("_BaseColor", Color.white);
            var normal = Tex("normal");
            mat.SetTexture("_BumpMap", normal);
            if (normal != null) mat.EnableKeyword("_NORMALMAP");
            var ms = Tex("ms");
            mat.SetTexture("_MetallicGlossMap", ms);
            if (ms != null) mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f); // metallic alpha
            var emission = Tex("emis");
            if (emission != null && (bool?)look["emissive"] != false)
            {
                mat.SetTexture("_EmissionMap", emission);
                mat.SetColor("_EmissionColor", Color.white);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            mat.enableInstancing = false; // skinned meshes cannot instance; SRP Batcher handles them
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameObject BuildPrefab(string lookId, JObject look, Material mat, bool hero)
        {
            var fbx = $"{LooksRoot}/{lookId}/{lookId}{(hero ? "_Hero" : "")}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx) ?? throw new FileNotFoundException(fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                go.name = lookId + (hero ? "_Hero" : "");
                float scale = (float?)look["scale"] ?? 1f;
                go.transform.localScale = Vector3.one * scale;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Material[Math.Max(1, r.sharedMaterials.Length)];
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.Off; // blob shadows only (spec)
                    r.receiveShadows = false;
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
                }
                // The model's own Animator carries its Humanoid Avatar. Never add one: an Animator without an Avatar on
                // an optimized hierarchy cannot pose the mesh, which then renders in its bind (T) pose.
                var animators = go.GetComponentsInChildren<Animator>(true);
                var animator = go.GetComponent<Animator>();
                if (animators.Length != 1 || animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                    throw new InvalidDataException($"{fbx}: needs exactly one root Animator with a valid Humanoid Avatar " +
                        $"(found {animators.Length}; avatar={(animator != null && animator.avatar != null ? animator.avatar.name : "none")}). Run CT Life > Cast > Rebuild look Avatars.");
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
                if (!hero)
                {
                    var lod = go.GetComponent<LODGroup>();
                    if (lod != null)
                    {
                        // AvatarLod forces the tier by crowd rank; these thresholds only apply when it does not.
                        var lods = lod.GetLODs();
                        float[] heights = { 0.25f, 0.10f, 0.01f };
                        for (int i = 0; i < lods.Length && i < heights.Length; i++) lods[i].screenRelativeTransitionHeight = heights[i];
                        lod.SetLODs(lods);
                    }
                    else Debug.LogWarning($"CTLIFE [cast] {lookId}: no LODGroup created from _LODn meshes");
                }
                var path = $"{PrefabRoot}/{go.name}.prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
                return saved;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }

    /// <summary>Import settings for everything under Assets/Art/characters/cast (cast lane plan, steps 2, 3, 5, 6).</summary>
    public sealed class CastPostprocessor : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 50;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(CastImport.Root + "/", StringComparison.Ordinal)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileUnits = true;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.isReadable = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false;
            mi.bakeAxisConversion = true; // as the Snow/Rain sources (AuthoredCharacterImport); keeps the stored rest pose
            mi.useFileScale = true;
            mi.skinWeights = ModelImporterSkinWeights.Standard;
            mi.meshCompression = ModelImporterMeshCompression.Medium; // APK budget: quantized positions/normals/uvs
            mi.meshOptimizationFlags = MeshOptimizationFlags.Everything;
            if (CastImport.RestProbe.Contains(assetPath))
            {
                // CastImport.ConfigureAvatar is re-reading the full rest hierarchy.
                mi.animationType = ModelImporterAnimationType.None;
                mi.optimizeGameObjects = false;
                return;
            }
            // The Avatar is the look's own (CreateFromThisModel + the HumanDescription that CastImport.ConfigureAvatar
            // stored in this .meta). Nothing here reads another asset, so a fresh Library imports identically.
            mi.animationType = ModelImporterAnimationType.Human;
            mi.optimizeGameObjects = mi.userData == CastImport.AvatarTag; // optimize only once the Avatar is configured
            if (mi.userData != CastImport.AvatarTag)
                Debug.LogWarning("CTLIFE [cast] look Avatar not configured yet (CT Life > Cast > Rebuild look Avatars): " + assetPath);
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(CastImport.Root + "/", StringComparison.Ordinal)) return;
            var ti = (TextureImporter)assetImporter;
            var name = Path.GetFileNameWithoutExtension(assetPath);
            bool normal = name.EndsWith("_Normal", StringComparison.Ordinal);
            bool data = name.EndsWith("_MetallicSmoothness", StringComparison.Ordinal);
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal && !data;
            ti.alphaIsTransparency = false;
            ti.alphaSource = data ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.FromInput;
            ti.mipmapEnabled = true;
            ti.isReadable = false;
            var lookId = CastImport.LookIdFromPath(assetPath);
            int max = (int?)CastImport.Look(lookId ?? "")?["atlas_size"] ?? 512;
            ti.maxTextureSize = max;
            // Android (APK budget 150 MB): only the BaseColor of hero looks (creator close-ups) keeps 1024; normal and
            // metallic/smoothness maps are 512 everywhere, and normals use ASTC 6x6 (the plan's 4x4 cost 1.33 MB per
            // 1024 normal map; 24 hero looks x 3 maps were 60 MB of the build).
            int androidMax = name.EndsWith("_BaseColor", StringComparison.Ordinal) ? max : Math.Min(max, 512);
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = androidMax;
            android.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
        }
    }
}
