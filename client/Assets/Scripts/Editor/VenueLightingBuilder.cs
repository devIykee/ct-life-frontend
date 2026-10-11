using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CTLife.World;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Optional, explicit static-lighting pass after VenueContentBuilder. Uses synchronous Lightmapping.Bake
    /// (Unity 6.3 API), so command-line -quit runs only after bake, validation and prefab binding have completed.
    /// Apple silicon requires ProgressiveGPU and graphics enabled; do not pass -nographics.
    /// </summary>
    public static class VenueLightingBuilder
    {
        internal const string VenuePath = "Assets/Art/Generated/Venues/TimelineSquare.prefab";
        private const string Output = "Assets/Art/Generated/Venues/Lighting";
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<Material, Material> Materials = new Dictionary<Material, Material>();

        [MenuItem("CT Life/Content/Bake Timeline Square Lighting")]
        public static void BakeTimelineSquare() => BakeVenue(VenuePath, Output);

        /// <summary>Bake an explicit prefab into its own output folder; source meshes/materials stay untouched.</summary>
        public static void BakeVenue(string prefabPath, string outputDirectory)
        {
            ValidateAssetPath(prefabPath, ".prefab");
            ValidateAssetPath(outputDirectory, null);
            outputDirectory = outputDirectory.TrimEnd('/');
            string scenePath = outputDirectory + "/" + Path.GetFileNameWithoutExtension(prefabPath) + ".unity";
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
                throw new InvalidOperationException("Finish Play mode or the current light bake before baking the venue.");
            RequireLightmapVariants();
            bool appleSilicon = Application.platform == RuntimePlatform.OSXEditor &&
                SystemInfo.processorType.IndexOf("Apple", StringComparison.OrdinalIgnoreCase) >= 0;
            bool requestCpu = Environment.GetCommandLineArgs().Contains("--ct-lightmapper=cpu");
            if (appleSilicon && requestCpu)
                throw new NotSupportedException("Unity 6.3 does not support CPU lightmapping on Apple silicon. Use GPU with graphics enabled.");
            bool gpu = appleSilicon || Environment.GetCommandLineArgs().Contains("--ct-lightmapper=gpu");
            if (gpu && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("ProgressiveGPU needs graphics enabled. Remove -nographics.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new FileNotFoundException("Generate the venue prefab before running its optional bake: " + prefabPath);
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scene changes before running the isolated venue bake.");

            var previousSetup = EditorSceneManager.GetSceneManagerSetup().Where(s => !string.IsNullOrEmpty(s.path)).ToArray();
            var watch = Stopwatch.StartNew();
            foreach (var path in new[] { outputDirectory, outputDirectory + "/Meshes", outputDirectory + "/Materials" }) Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
            Meshes.Clear();
            Materials.Clear();
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = Object.Instantiate(prefab);
                root.name = prefabPath == VenuePath ? "Timeline Square" : prefab.name;
                var oldBindings = root.GetComponent<VenueLightmaps>();
                if (oldBindings != null) Object.DestroyImmediate(oldBindings);
                var bakeRenderers = PrepareGeometry(root, outputDirectory);
                var settings = ConfigureLighting(gpu, outputDirectory);
                CreateBakeSun(root.GetComponent<VenueContentLighting>());
                // Save before Bake so Unity's lightmap/lighting-data assets have a persistent generated path.
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("Could not save generated lighting scene.");
                SaveGeneratedAssets(outputDirectory);
                Debug.Log($"CTLIFE [venue-bake] starting {settings.lightmapper}, {bakeRenderers.Count} static renderers, " +
                    $"10 texels/m, 1024 atlas, 32/128/64 samples, 2 bounces. Device={SystemInfo.graphicsDeviceName}. " +
                    "Lightmapping.Bake is synchronous; this executeMethod will not return while baking.");
                if (!Lightmapping.Bake() || Lightmapping.isRunning)
                    throw new InvalidOperationException("Venue light bake did not complete successfully. Prefab has not been updated.");

                var maps = LightmapSettings.lightmaps;
                if (maps == null || maps.Length == 0 || maps.Any(m => m.lightmapColor == null))
                    throw new InvalidDataException("Bake produced no complete color lightmaps. Prefab has not been updated.");
                ValidateBakedRenderers(root, bakeRenderers, maps.Length);
                var bindings = root.AddComponent<VenueLightmaps>();
                bindings.colors = maps.Select(m => m.lightmapColor).ToArray();
                bindings.mode = LightmapsMode.NonDirectional;
                var rows = new List<VenueLightmaps.Binding>();
                foreach (var renderer in bakeRenderers)
                {
                    rows.Add(new VenueLightmaps.Binding
                    {
                        renderer = renderer,
                        lightmap = renderer.lightmapIndex,
                        scaleOffset = renderer.lightmapScaleOffset,
                    });
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(material => ToBakedMaterial(material, outputDirectory)).ToArray();
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                }
                bindings.bindings = rows.ToArray();
                var originalGuid = AssetDatabase.AssetPathToGUID(prefabPath);
                if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                    throw new IOException("Could not save lightmap bindings to the existing venue prefab.");
                if (AssetDatabase.AssetPathToGUID(prefabPath) != originalGuid)
                    throw new InvalidDataException("Venue prefab GUID unexpectedly changed.");
                // Keep the bake scene as reproducible evidence. The sun belongs only to this scene, not the prefab.
                EditorSceneManager.SaveScene(scene);
                SaveGeneratedAssets(outputDirectory);
                watch.Stop();
                long pixels = maps.Sum(m => (long)m.lightmapColor.width * m.lightmapColor.height);
                long editorPeak = Process.GetCurrentProcess().PeakWorkingSet64;
                string report = $"Backend: {settings.lightmapper}\nElapsed seconds: {watch.Elapsed.TotalSeconds:F1}\n" +
                    $"Lightmaps: {maps.Length}\nColor texels: {pixels}\nBound renderers: {rows.Count}\n" +
                    $"Peak editor working set bytes: {editorPeak}\n" +
                    "Peak editor working set excludes external bake workers and GPU allocation. Use process/device monitoring for those.\n";
                File.WriteAllText(outputDirectory + "/LastBake.txt", report);
                AssetDatabase.ImportAsset(outputDirectory + "/LastBake.txt");
                Debug.Log("CTLIFE [venue-bake] completed; prefab GUID retained.\n" + report);
            }
            finally
            {
                Meshes.Clear();
                Materials.Clear();
                if (previousSetup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        private static void ValidateAssetPath(string path, string extension)
        {
            string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !Path.GetFullPath(path).StartsWith(assets, StringComparison.Ordinal) ||
                path.Contains("/../") || path.Contains("/./") || path.Contains("\\") ||
                (extension != null && Path.GetExtension(path) != extension))
                throw new ArgumentException("Expected a project Assets path" + (extension ?? "") + ": " + path);
        }

        internal static void RequireLightmapVariants()
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (asset == null) throw new BuildFailedException("Cannot inspect GraphicsSettings for runtime venue lightmaps.");
            var graphics = new SerializedObject(asset);
            var stripping = graphics.FindProperty("m_LightmapStripping");
            var plain = graphics.FindProperty("m_LightmapKeepPlain");
            // Unity GraphicsSettingsInspector.StrippingModes: Automatic = 0, Custom = 1.
            // Prefab-bound maps are absent from build scenes; Automatic may remove LIGHTMAP_ON.
            if (stripping == null || plain == null || stripping.intValue != 1 || !plain.boolValue)
                throw new BuildFailedException("Runtime venue lightmaps require Graphics Settings > Lightmap Modes = Custom " +
                    "and Baked Non-Directional enabled. This read-only guard does not alter project settings.");
            Debug.Log("CTLIFE [venue-bake] shader preflight: Custom lightmap modes; Baked Non-Directional retained.");
        }

        private static void ValidateBakedRenderers(GameObject root, List<MeshRenderer> renderers, int count)
        {
            // ProgressiveGPU bakes direct + indirect into each contributing LOD. Keep LODGroups intact so
            // Unity uses only LOD0 as an occluder, rather than baking overlapping copies of each model.
            foreach (var renderer in renderers)
            {
                var st = renderer.lightmapScaleOffset;
                if (renderer.lightmapIndex < 0 || renderer.lightmapIndex >= count ||
                    st.x <= 0f || st.y <= 0f || float.IsNaN(st.x + st.y + st.z + st.w))
                    throw new InvalidDataException("Static renderer has no complete baked lightmap binding: " + renderer.name +
                        ". Prefab has not been updated; lower LODs are never silently published without a map.");
            }
            var included = new HashSet<MeshRenderer>(renderers);
            var counts = new Dictionary<int, int>();
            foreach (var group in root.GetComponentsInChildren<LODGroup>())
            {
                var lods = group.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                {
                    int bound = lods[i].renderers.OfType<MeshRenderer>().Count(included.Contains);
                    counts[i] = counts.TryGetValue(i, out var prior) ? prior + bound : bound;
                }
            }
            Debug.Log("CTLIFE [venue-bake] validated bound static renderers per LOD: " +
                string.Join(", ", counts.OrderBy(p => p.Key).Select(p => $"LOD{p.Key}={p.Value}")));
        }

        private static LightingSettings ConfigureLighting(bool gpu, string outputDirectory)
        {
            string path = outputDirectory + "/TimelineLightingSettings.lighting";
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (settings == null) { settings = new LightingSettings { name = "Timeline mobile baked lighting" }; AssetDatabase.CreateAsset(settings, path); }
            // Unity 6.3 removed automatic generation; this builder explicitly starts the bake.
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.lightmapper = gpu ? LightingSettings.Lightmapper.ProgressiveGPU : LightingSettings.Lightmapper.ProgressiveCPU;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            settings.lightmapResolution = 10f;
            settings.lightmapMaxSize = 1024;
            settings.lightmapPadding = 4;
            settings.lightmapCompression = LightmapCompression.NormalQuality;
            settings.directSampleCount = 32;
            settings.indirectSampleCount = 128;
            settings.environmentSampleCount = 64;
            settings.minBounces = 1;
            settings.maxBounces = 2;
            settings.ao = true;
            settings.aoMaxDistance = 1.5f;
            settings.aoExponentIndirect = 0.65f;
            settings.aoExponentDirect = 0.15f;
            settings.prioritizeView = false;
            // Gaussian filtering is available without Optix/Intel denoiser hardware assumptions.
            settings.filteringMode = LightingSettings.FilterMode.Advanced;
            settings.denoiserTypeDirect = LightingSettings.DenoiserType.None;
            settings.denoiserTypeIndirect = LightingSettings.DenoiserType.None;
            settings.denoiserTypeAO = LightingSettings.DenoiserType.None;
            settings.filterTypeDirect = LightingSettings.FilterType.Gaussian;
            settings.filterTypeIndirect = LightingSettings.FilterType.Gaussian;
            settings.filterTypeAO = LightingSettings.FilterType.Gaussian;
            settings.filteringGaussianRadiusDirect = 1;
            settings.filteringGaussianRadiusIndirect = 2;
            settings.filteringGaussianRadiusAO = 1;
            Lightmapping.lightingSettings = settings;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static void CreateBakeSun(VenueContentLighting look)
        {
            var sun = new GameObject("Bake-only golden-hour sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.lightmapBakeType = LightmapBakeType.Baked;
            sun.shadows = LightShadows.Soft;
            sun.color = look.sunColor;
            sun.intensity = look.sunIntensity;
            sun.transform.rotation = Quaternion.Euler(look.sunAngles);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.sky;
            RenderSettings.ambientEquatorColor = look.equator;
            RenderSettings.ambientGroundColor = look.ground;
            RenderSettings.ambientIntensity = 0.7f;
            RenderSettings.fog = false;
        }

        private static List<MeshRenderer> PrepareGeometry(GameObject root, string outputDirectory)
        {
            var result = new List<MeshRenderer>();
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name == "Static contact shadows")
                {
                    renderer.gameObject.SetActive(false); // Replaced by the actual bake; do not darken it twice.
                    continue;
                }
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var original = filter.sharedMesh;
                var materials = renderer.sharedMaterials;
                var solid = Enumerable.Range(0, materials.Length).Where(i => !IsDisplay(materials[i])).ToArray();
                var screens = Enumerable.Range(0, materials.Length).Where(i => IsDisplay(materials[i])).ToArray();
                if (solid.Length == 0)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic);
                    continue;
                }
                if (screens.Length > 0)
                {
                    var displayObject = new GameObject("Unlit content surfaces");
                    displayObject.transform.SetParent(renderer.transform, false);
                    displayObject.AddComponent<MeshFilter>().sharedMesh = Extract(original, screens, false, outputDirectory);
                    var display = displayObject.AddComponent<MeshRenderer>();
                    display.sharedMaterials = screens.Select(i => ToDisplayMaterial(materials[i], outputDirectory)).ToArray();
                    display.shadowCastingMode = ShadowCastingMode.Off;
                    display.receiveShadows = false;
                    display.lightProbeUsage = LightProbeUsage.Off;
                    GameObjectUtility.SetStaticEditorFlags(displayObject, StaticEditorFlags.BatchingStatic);
                    AddToSameLod(renderer, display);
                }
                filter.sharedMesh = Extract(original, solid, true, outputDirectory);
                renderer.sharedMaterials = solid.Select(i => materials[i]).ToArray();
                renderer.receiveGI = ReceiveGI.Lightmaps;
                renderer.scaleInLightmap = renderer.bounds.center.z > 35f ? 0.2f : renderer.bounds.size.magnitude < 1.5f ? 0.5f : 1f;
                renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                renderer.receiveShadows = true;
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
                result.Add(renderer);
            }
            Debug.Log($"CTLIFE [venue-bake] prepared {result.Count} renderers and {Meshes.Count} generated mesh copies with unique UV2; source FBX unchanged.");
            return result;
        }

        private static void SaveGeneratedAssets(string outputDirectory)
        {
            // Loading URP materials can dirty unrelated assets through validation. Save only
            // the explicit bake output; a candidate bake must not flush those incidental edits.
            foreach (string guid in AssetDatabase.FindAssets("", new[] { outputDirectory }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith(outputDirectory + "/", StringComparison.Ordinal)) continue;
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
            }
        }

        private static bool IsDisplay(Material material) => material == null || material.shader.name.Contains("Unlit") ||
            material.name.Contains("KEEP_neon") || material.name.Contains("KEEP_on_air");

        private static Mesh Extract(Mesh original, int[] submeshes, bool unwrap, string outputDirectory)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string guid, out long localId);
            string key = guid + "_" + localId + "_" + string.Join("-", submeshes) + (unwrap ? "_uv2" : "_display");
            if (Meshes.TryGetValue(key, out var cached)) return cached;
            string originalPath = AssetDatabase.GetAssetPath(original);
            string path = originalPath.StartsWith(outputDirectory + "/Meshes/") && submeshes.Length == original.subMeshCount
                ? originalPath : outputDirectory + "/Meshes/" + key + ".asset";
            var mesh = new Mesh { name = original.name + (unwrap ? "_lightmapped" : "_display"), indexFormat = original.indexFormat };
            mesh.vertices = original.vertices;
            mesh.normals = original.normals;
            mesh.tangents = original.tangents;
            mesh.colors = original.colors;
            mesh.uv = original.uv;
            mesh.subMeshCount = submeshes.Length;
            for (int i = 0; i < submeshes.Length; i++) mesh.SetTriangles(original.GetTriangles(submeshes[i]), i);
            if (unwrap)
            {
                UnwrapParam.SetDefaults(out var unwrapSettings);
                if (!Unwrapping.GenerateSecondaryUVSet(mesh, unwrapSettings) || mesh.uv2.Length != mesh.vertexCount)
                    throw new InvalidDataException("Could not create unique bake UV2 for " + original.name);
            }
            mesh.RecalculateBounds();
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
            else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
            Meshes[key] = saved;
            return saved;
        }

        private static void AddToSameLod(MeshRenderer original, MeshRenderer display)
        {
            var group = original.GetComponentInParent<LODGroup>();
            if (group == null) return;
            var lods = group.GetLODs();
            for (int i = 0; i < lods.Length; i++)
                if (lods[i].renderers.Contains(original)) lods[i].renderers = lods[i].renderers.Concat(new Renderer[] { display }).ToArray();
            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        private static Material ToDisplayMaterial(Material original, string outputDirectory)
        {
            if (original.shader.name.Contains("Unlit")) return original;
            return CopyMaterial(original, "Universal Render Pipeline/Unlit", "Display_", outputDirectory);
        }

        private static Material ToBakedMaterial(Material original, string outputDirectory) => CopyMaterial(original, "Universal Render Pipeline/Baked Lit", "Baked_", outputDirectory);

        private static Material CopyMaterial(Material original, string shaderName, string prefix, string outputDirectory)
        {
            if (Materials.TryGetValue(original, out var cached)) return cached;
            string originalPath = AssetDatabase.GetAssetPath(original);
            string path = originalPath.StartsWith(outputDirectory + "/Materials/") ? originalPath : outputDirectory + "/Materials/" + prefix + original.name + ".mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("Required venue shader unavailable: " + shaderName);
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null) { copy = new Material(shader); AssetDatabase.CreateAsset(copy, path); }
            copy.shader = shader;
            copy.name = prefix + original.name.Replace(prefix, "");
            copy.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
            copy.SetColor("_BaseColor", original.GetColor("_BaseColor"));
            copy.SetFloat("_Cull", original.GetFloat("_Cull"));
            bool cutout = original.IsKeywordEnabled("_ALPHATEST_ON");
            copy.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            copy.SetFloat("_Cutoff", original.GetFloat("_Cutoff"));
            if (cutout) copy.EnableKeyword("_ALPHATEST_ON"); else copy.DisableKeyword("_ALPHATEST_ON");
            copy.renderQueue = cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            copy.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
            copy.enableInstancing = true;
            EditorUtility.SetDirty(copy);
            Materials[original] = copy;
            return copy;
        }
    }

    /// <summary>Checks the exact forward-pass variants left after SRP stripping; never adds or broadens variants.</summary>
    public sealed class VenueLightmapShaderGuard : IPreprocessBuildWithReport, IPreprocessShaders
    {
        public int callbackOrder => int.MaxValue; // SRP ShaderPreprocessor runs at 0 in pinned Core RP 17.3.
        private static bool _bakedVenue;

        public void OnPreprocessBuild(BuildReport report)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VenueLightingBuilder.VenuePath);
            var binding = prefab != null ? prefab.GetComponent<VenueLightmaps>() : null;
            _bakedVenue = binding != null && binding.colors != null && binding.colors.Length > 0;
            if (_bakedVenue) VenueLightingBuilder.RequireLightmapVariants();
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!_bakedVenue || shader.name != "Universal Render Pipeline/Baked Lit" || snippet.passName != "BakedLit") return;
            var lightmap = new LocalKeyword(shader, "LIGHTMAP_ON");
            var alpha = new LocalKeyword(shader, "_ALPHATEST_ON");
            int retained = data.Count(v => v.shaderKeywordSet.IsEnabled(lightmap));
            int cutout = data.Count(v => v.shaderKeywordSet.IsEnabled(lightmap) && v.shaderKeywordSet.IsEnabled(alpha));
            if (retained == 0 || (snippet.shaderType == ShaderType.Fragment && cutout == 0))
                throw new BuildFailedException("BakedLit venue forward-pass variants were stripped: LIGHTMAP_ON=" + retained +
                    ", lightmapped foliage=" + cutout + ". Runtime venue lighting is not ready to ship.");
            Debug.Log($"CTLIFE [venue-bake] retained BakedLit {snippet.shaderType} variants after SRP stripping: " +
                $"LIGHTMAP_ON={retained}, lightmapped cutout={cutout}.");
        }
    }
}
