using System.Collections.Generic;
using System.IO;
using CTLife.App;
using CTLife.Core;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace CTLife.EditorTools
{
    /// <summary>
    /// One-shot project configuration (Unity agent steps 4–5). Idempotent; safe to re-run.
    /// Batch: Unity -batchmode -quit -projectPath client -executeMethod CTLife.EditorTools.ProjectSetup.Run
    /// </summary>
    public static class ProjectSetup
    {
        public const string CompanyName = "CT Life";
        public const string ProductName = "CT Life";
        public const string PackageName = "com.ctlife.app";

        [MenuItem("CT Life/Project Setup (all)")]
        public static void Run()
        {
            EnsureFolders();
            ConfigureAndroid();
            var urp = ConfigureUrp();
            ConfigureShaders(urp);
            CreatePanelSettings();
            CreateEnvAssets();
            CreateScenes();
            EnsureAddressables();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("CTLIFE [setup] Project setup complete");
        }

        private static void EnsureFolders()
        {
            foreach (var f in new[] { "Assets/Scenes", "Assets/Settings", "Assets/Resources/Env", "Assets/Resources/UI", "Assets/Resources/Materials", "Assets/Resources/Characters/Parts", "Assets/Venues" })
                Directory.CreateDirectory(f);
            AssetDatabase.Refresh();
        }

        // Narrow entry point for the user's landscape decision; does not rerun project setup.
        [MenuItem("CT Life/Setup/Landscape Gameplay")]
        public static void ConfigureLandscape()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            Debug.Log("CTLIFE [setup] Landscape gameplay: either landscape side, portrait rotation disabled.");
        }

        [MenuItem("CT Life/Setup/Gameplay Orientation and Frame Timing")]
        public static void ConfigureGameplaySettings()
        {
            ConfigureLandscape();
            // Required by the installed Google Adaptive Performance provider's timing hints.
            // Configure it once instead of showing its InitializeOnLoad dialog on every reload.
            PlayerSettings.enableFrameTimingStats = true;
            Debug.Log("CTLIFE [setup] CPU/GPU frame timing enabled for Android performance hints.");
        }

        [MenuItem("CT Life/Setup/Android Player Settings")]
        public static void ConfigureAndroid()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(android, PackageName);
            PlayerSettings.bundleVersion = Semver.ClientVersion;
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;
            PlayerSettings.Android.optimizedFramePacing = true;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);
            ConfigureGameplaySettings();
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.meshDeformation = MeshDeformation.GPUBatched;
            // mock/local REST is plain http over adb reverse; release builds use https only.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
            // Solana.Unity-SDK's build hook requires "Custom Main Gradle Template" and patches its androidx/guava deps in.
            const string mainTemplate = "Assets/Plugins/Android/mainTemplate.gradle";
            if (!File.Exists(mainTemplate))
            {
                var src = Path.Combine(BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None),
                    "Tools/GradleTemplates/mainTemplate.gradle");
                Directory.CreateDirectory(Path.GetDirectoryName(mainTemplate));
                File.Copy(src, mainTemplate);
                AssetDatabase.ImportAsset(mainTemplate);
            }
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            EditorUserBuildSettings.buildAppBundle = false; // The dApp Store takes APKs only.
            Debug.Log("CTLIFE [setup] Android: com.ctlife.app, IL2CPP, ARM64, Vulkan+GLES3, min 29, target 36, frame pacing, ASTC");
        }

        [MenuItem("CT Life/Setup/URP Mobile Asset")]
        public static UniversalRenderPipelineAsset ConfigureUrp()
        {
            const string assetPath = "Assets/Settings/CTLife_URP.asset";
            const string rendererPath = "Assets/Settings/CTLife_URP_Renderer.asset";
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (urp == null)
            {
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
                if (renderer == null)
                {
                    renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(renderer, rendererPath);
                }
                // Same as UniversalRenderPipelineAsset.Create() minus its eager ResourceReloader call, which throws
                // "AssetDatabase cannot load now" in batchmode (URP loads those resources lazily later).
                urp = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                AssetDatabase.CreateAsset(urp, assetPath);
                var init = new SerializedObject(urp);
                var list = init.FindProperty("m_RendererDataList");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                init.FindProperty("m_DefaultRendererIndex").intValue = 0;
                var ces = init.FindProperty("m_ConservativeEnclosingSphere");
                if (ces != null) ces.boolValue = true;
                init.ApplyModifiedPropertiesWithoutUndo();
            }
            // Build Spec "Performance budgets": render scale 0.7, 1 directional light, no realtime shadows.
            urp.renderScale = 0.7f;
            urp.supportsHDR = false;
            urp.msaaSampleCount = 2;
            // These two have `internal set` in URP 17.3, so go through the serialized fields.
            var so = new SerializedObject(urp);
            so.FindProperty("m_MainLightShadowsSupported").boolValue = false;
            so.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.Disabled;
            so.ApplyModifiedPropertiesWithoutUndo();
            urp.supportsCameraDepthTexture = false;
            urp.supportsCameraOpaqueTexture = false;
            urp.useSRPBatcher = true;
            urp.supportsDynamicBatching = false;
            urp.shadowDistance = 0f;
            EditorUtility.SetDirty(urp);

            EnsureRendererReference(assetPath, rendererPath);
            GraphicsSettings.defaultRenderPipeline = urp;
            var levels = QualitySettings.names.Length;
            var current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < levels; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
                QualitySettings.vSyncCount = 0;
                QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
                QualitySettings.skinWeights = SkinWeights.FourBones;
            }
            QualitySettings.SetQualityLevel(current, false);
            Debug.Log("CTLIFE [setup] URP asset at " + assetPath);
            return urp;
        }

        /// <summary>Runtime Shader.Find needs these in player builds (greybox and placeholder materials).</summary>
        /// <summary>
        /// In batchmode the SerializedObject assignment of m_RendererDataList[0] did not persist (the saved asset kept
        /// `- {fileID: 0}`, and players logged "Default Renderer is missing"). Verify the YAML and write the reference by
        /// GUID if needed.
        /// </summary>
        private static void EnsureRendererReference(string assetPath, string rendererPath)
        {
            AssetDatabase.SaveAssets();
            var guid = AssetDatabase.AssetPathToGUID(rendererPath);
            if (string.IsNullOrEmpty(guid) || !File.Exists(assetPath)) return;
            var yaml = File.ReadAllText(assetPath);
            const string empty = "m_RendererDataList:\n  - {fileID: 0}";
            if (!yaml.Contains(empty)) return;
            yaml = yaml.Replace(empty, "m_RendererDataList:\n  - {fileID: 11400000, guid: " + guid + ", type: 2}");
            File.WriteAllText(assetPath, yaml);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("CTLIFE [setup] URP default renderer reference written by GUID " + guid);
        }

        /// <summary>Shader.Find can return null for package shaders in batchmode; fall back to the asset path.</summary>
        private static Shader FindShader(string name)
        {
            var s = Shader.Find(name);
            if (s != null) return s;
            var file = name switch
            {
                "Universal Render Pipeline/Simple Lit" => "SimpleLit.shader",
                "Universal Render Pipeline/Lit" => "Lit.shader",
                "Universal Render Pipeline/Unlit" => "Unlit.shader",
                _ => null,
            };
            if (file == null) return null;
            s = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/" + file);
            if (s == null) Debug.LogWarning("CTLIFE [setup] shader not found: " + name);
            return s;
        }

        private static void ConfigureShaders(UniversalRenderPipelineAsset urp)
        {
            var palette = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Materials/AvatarPalette.mat");
            var simpleLit = FindShader("Universal Render Pipeline/Simple Lit");
            if (palette == null && simpleLit != null)
            {
                palette = new Material(simpleLit) { enableInstancing = true };
                AssetDatabase.CreateAsset(palette, "Assets/Resources/Materials/AvatarPalette.mat");
            }

            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            var wanted = new[] { "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Sprites/Default" };
            var have = new HashSet<Shader>();
            for (int i = 0; i < arr.arraySize; i++) have.Add(arr.GetArrayElementAtIndex(i).objectReferenceValue as Shader);
            foreach (var name in wanted)
            {
                var s = FindShader(name);
                if (s == null || have.Contains(s)) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = s;
            }
            so.ApplyModifiedProperties();
        }

        private static void CreatePanelSettings()
        {
            const string path = "Assets/Resources/UI/PanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, path);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/Resources/UI/CTLifeTheme.tss");
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1080, 2400);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
        }

        private static void CreateEnvAssets()
        {
            const string selPath = "Assets/Resources/Env/EnvSelection.asset";
            var sel = AssetDatabase.LoadAssetAtPath<EnvSelection>(selPath);
            if (sel == null)
            {
                sel = ScriptableObject.CreateInstance<EnvSelection>();
                AssetDatabase.CreateAsset(sel, selPath);
            }
            var list = new List<EnvConfig>();
            foreach (EnvId id in System.Enum.GetValues(typeof(EnvId)))
            {
                var p = $"Assets/Resources/Env/EnvConfig_{id}.asset";
                var cfg = AssetDatabase.LoadAssetAtPath<EnvConfig>(p);
                if (cfg == null)
                {
                    cfg = EnvConfig.CreateDefault(id);
                    AssetDatabase.CreateAsset(cfg, p);
                }
                list.Add(cfg);
            }
            sel.configs = list.ToArray();
            EditorUtility.SetDirty(sel);
        }

        [MenuItem("CT Life/Setup/Scenes")]
        public static void CreateScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>();
            // Only the game ships. The old engine-spike scene lives in client/Experiments~/Scenes.
            scenes.Add(new EditorBuildSettingsScene(MakeScene("Assets/Scenes/Bootstrap.unity"), true));
            foreach (var venue in Directory.Exists("Assets/Venues") ? Directory.GetFiles("Assets/Venues", "*.unity", SearchOption.AllDirectories) : new string[0])
                scenes.Add(new EditorBuildSettingsScene(venue.Replace('\\', '/'), true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static string MakeScene(string path)
        {
            if (File.Exists(path)) return path;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("GameBootstrap");
            go.AddComponent<GameBootstrap>();
            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            cam.AddComponent<Camera>();
            cam.AddComponent<AudioListener>();
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        private static void EnsureAddressables()
        {
            try
            {
                var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
                if (settings != null) Debug.Log("CTLIFE [setup] Addressables settings: " + AssetDatabase.GetAssetPath(settings));
            }
            catch (System.Exception e)
            {
                // Seen in batchmode on a re-run once the settings asset already exists; it is created on the first run.
                Debug.LogWarning("CTLIFE [setup] Addressables settings check skipped: " + e.Message);
            }
        }
    }
}
