using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CTLife.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Mobile static-lighting bake for the venue dioramas. Same approach as VenueLightingBuilder (synchronous
    /// Lightmapping.Bake with ProgressiveGPU on Apple silicon, unique UV2 copies, Baked Lit material copies, prefab-bound
    /// VenueLightmaps) with venue-builder specifics:
    ///  * renderers under "Backdrop" (far ground, water, skyline) and "Hooks" (live surfaces) are not lightmapped;
    ///  * 8 texels/m inside the walkable area, scaled down outside it; no default reflection probe;
    ///  * baked-only lights are removed from the prefab afterwards (an unbaked prefab light would render realtime);
    ///  * the throwaway bake scene, LightingData and unreferenced pre-bake meshes are deleted to keep the repo small.
    /// Needs graphics (do not pass -nographics).
    /// </summary>
    public static class VenueBake
    {
        private static readonly Dictionary<Mesh, Mesh> Meshes = new Dictionary<Mesh, Mesh>();
        private static readonly Dictionary<Material, Material> Materials = new Dictionary<Material, Material>();

        public static void Bake(VenueSpec spec)
        {
            string output = spec.Folder + "/Lighting";
            string scenePath = output + "/" + spec.id + "_bake.unity";
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) throw new InvalidOperationException("Busy.");
            VenueLightingBuilder.RequireLightmapVariants();
            bool appleSilicon = Application.platform == RuntimePlatform.OSXEditor &&
                                SystemInfo.processorType.IndexOf("Apple", StringComparison.OrdinalIgnoreCase) >= 0;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("The venue bake needs a graphics device (remove -nographics).");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) ?? throw new FileNotFoundException(spec.PrefabPath);
            if (prefab.GetComponent<VenueLightmaps>() != null)
                throw new InvalidOperationException(spec.id + " is already baked; run Build first (Build→Bake is one unit).");
            foreach (var dir in new[] { output, output + "/Meshes", output + "/Materials" }) Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
            Meshes.Clear();
            Materials.Clear();
            var watch = Stopwatch.StartNew();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var root = (GameObject)Object.Instantiate(prefab);
                root.name = spec.id;
                var venue = root.GetComponent<VenueSceneRoot>();
                var walk = venue.cameraBounds;
                var bakeRenderers = Prepare(root, output, walk);
                var settings = Configure(appleSilicon, output, spec.id);
                var look = root.GetComponent<VenueContentLighting>();
                var sun = new GameObject("Bake-only sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.lightmapBakeType = LightmapBakeType.Baked;
                sun.shadows = LightShadows.Soft;
                sun.color = look.sunColor;
                sun.intensity = look.sunIntensity;
                sun.transform.rotation = Quaternion.Euler(look.sunAngles);
                RenderSettings.sun = sun;
                RenderSettings.skybox = null;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = look.sky;
                RenderSettings.ambientEquatorColor = look.equator;
                RenderSettings.ambientGroundColor = look.ground;
                RenderSettings.ambientIntensity = 0.8f;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = null;
                RenderSettings.fog = false;
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("Could not save bake scene.");
                if (!Lightmapping.Bake() || Lightmapping.isRunning) throw new InvalidOperationException(spec.id + ": bake failed.");
                var maps = LightmapSettings.lightmaps;
                if (maps == null || maps.Length == 0 || maps.Any(m => m.lightmapColor == null))
                    throw new InvalidDataException(spec.id + ": bake produced no colour lightmaps.");
                var rows = new List<VenueLightmaps.Binding>();
                foreach (var r in bakeRenderers)
                {
                    var st = r.lightmapScaleOffset;
                    if (r.lightmapIndex < 0 || r.lightmapIndex >= maps.Length || st.x <= 0f || st.y <= 0f)
                        throw new InvalidDataException(spec.id + ": renderer without a lightmap binding: " + r.name);
                    rows.Add(new VenueLightmaps.Binding { renderer = r, lightmap = r.lightmapIndex, scaleOffset = st });
                    r.sharedMaterials = r.sharedMaterials.Select(m => BakedMaterial(m, output)).ToArray();
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.lightProbeUsage = LightProbeUsage.Off;
                }
                var bindings = root.AddComponent<VenueLightmaps>();
                bindings.colors = maps.Select(m => m.lightmapColor).ToArray();
                bindings.mode = LightmapsMode.NonDirectional;
                bindings.bindings = rows.ToArray();
                foreach (var light in root.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(light.gameObject);
                // Backdrop stays Simple Lit and is lit by the venue's realtime sun at runtime.
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) r.lightProbeUsage = LightProbeUsage.Off;
                string guid = AssetDatabase.AssetPathToGUID(spec.PrefabPath);
                if (PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath) == null) throw new IOException("Prefab save failed.");
                if (AssetDatabase.AssetPathToGUID(spec.PrefabPath) != guid) throw new InvalidDataException("Prefab GUID changed.");
                VenueBuildPipeline.SaveFolder(spec.Folder);
                long texels = maps.Sum(m => (long)m.lightmapColor.width * m.lightmapColor.height);
                Debug.Log($"CTLIFE [venue-bake] {spec.id}: {settings.lightmapper}, {maps.Length} lightmaps, {texels} texels, " +
                          $"{rows.Count} bound renderers, {watch.Elapsed.TotalSeconds:F1}s.");
            }
            finally
            {
                Meshes.Clear();
                Materials.Clear();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Cleanup(spec, scenePath);
        }

        private static List<MeshRenderer> Prepare(GameObject root, string output, Rect walk)
        {
            var result = new List<MeshRenderer>();
            var expanded = new Rect(walk.xMin - 3f, walk.yMin - 3f, walk.width + 6f, walk.height + 6f);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool backdrop = r.transform.GetComponentsInParent<Transform>(true).Any(t => t.name == "Backdrop" || t.name == "Hooks");
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                bool display = r.sharedMaterials.All(m => m == null || m.shader.name.Contains("Unlit"));
                if (backdrop || display)
                {
                    r.receiveGI = ReceiveGI.LightProbes;
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
                    continue;
                }
                filter.sharedMesh = Unwrapped(filter.sharedMesh, output);
                r.receiveGI = ReceiveGI.Lightmaps;
                var c = r.bounds.center;
                bool inside = expanded.Contains(new Vector2(c.x, c.z));
                r.scaleInLightmap = !inside ? 0.25f : r.bounds.size.magnitude < 1.5f ? 0.5f : 1f;
                r.shadowCastingMode = ShadowCastingMode.TwoSided;
                r.receiveShadows = true;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
                result.Add(r);
            }
            return result;
        }

        private static Mesh Unwrapped(Mesh original, string output)
        {
            if (Meshes.TryGetValue(original, out var cached)) return cached;
            string path = output + "/Meshes/" + VB.Sanitize(original.name) + "_uv2.asset";
            var mesh = Object.Instantiate(original);
            mesh.name = original.name + "_uv2";
            UnwrapParam.SetDefaults(out var p);
            if (!Unwrapping.GenerateSecondaryUVSet(mesh, p) || mesh.uv2.Length != mesh.vertexCount)
                throw new InvalidDataException("UV2 unwrap failed for " + original.name);
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
            else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved); }
            Meshes[original] = saved;
            return saved;
        }

        private static LightingSettings Configure(bool gpu, string output, string id)
        {
            string path = output + "/" + id + "_lighting.lighting";
            var s = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (s == null) { s = new LightingSettings { name = id + " mobile baked lighting" }; AssetDatabase.CreateAsset(s, path); }
            s.bakedGI = true;
            s.realtimeGI = false;
            s.lightmapper = gpu ? LightingSettings.Lightmapper.ProgressiveGPU : LightingSettings.Lightmapper.ProgressiveCPU;
            s.directionalityMode = LightmapsMode.NonDirectional;
            s.lightmapResolution = 8f;
            s.lightmapMaxSize = 1024;
            s.lightmapPadding = 3;
            s.lightmapCompression = LightmapCompression.NormalQuality;
            s.directSampleCount = 32;
            s.indirectSampleCount = 128;
            s.environmentSampleCount = 64;
            s.minBounces = 1;
            s.maxBounces = 2;
            s.ao = true;
            s.aoMaxDistance = 1.5f;
            s.aoExponentIndirect = 0.65f;
            s.aoExponentDirect = 0.15f;
            s.prioritizeView = false;
            s.filteringMode = LightingSettings.FilterMode.Advanced;
            s.denoiserTypeDirect = LightingSettings.DenoiserType.None;
            s.denoiserTypeIndirect = LightingSettings.DenoiserType.None;
            s.denoiserTypeAO = LightingSettings.DenoiserType.None;
            s.filterTypeDirect = LightingSettings.FilterType.Gaussian;
            s.filterTypeIndirect = LightingSettings.FilterType.Gaussian;
            s.filterTypeAO = LightingSettings.FilterType.Gaussian;
            s.filteringGaussianRadiusDirect = 1;
            s.filteringGaussianRadiusIndirect = 2;
            s.filteringGaussianRadiusAO = 1;
            Lightmapping.lightingSettings = s;
            EditorUtility.SetDirty(s);
            return s;
        }

        private static Material BakedMaterial(Material original, string output)
        {
            if (original == null) return null;
            if (Materials.TryGetValue(original, out var cached)) return cached;
            var shader = Shader.Find("Universal Render Pipeline/Baked Lit") ?? throw new InvalidOperationException("Baked Lit shader missing.");
            string path = output + "/Materials/Baked_" + VB.Sanitize(original.name) + ".mat";
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null) { copy = new Material(shader); AssetDatabase.CreateAsset(copy, path); }
            copy.shader = shader;
            copy.name = "Baked_" + original.name;
            copy.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
            copy.SetColor("_BaseColor", original.GetColor("_BaseColor"));
            copy.SetFloat("_Cull", original.HasProperty("_Cull") ? original.GetFloat("_Cull") : (float)CullMode.Back);
            bool cutout = original.IsKeywordEnabled("_ALPHATEST_ON");
            copy.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            copy.SetFloat("_Cutoff", original.HasProperty("_Cutoff") ? original.GetFloat("_Cutoff") : 0.5f);
            if (cutout) copy.EnableKeyword("_ALPHATEST_ON"); else copy.DisableKeyword("_ALPHATEST_ON");
            copy.renderQueue = cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            copy.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
            copy.enableInstancing = true;
            EditorUtility.SetDirty(copy);
            Materials[original] = copy;
            return copy;
        }

        /// <summary>Remove the bake scene, LightingData, reflection output and assets the baked prefab no longer uses.</summary>
        private static void Cleanup(VenueSpec spec, string scenePath)
        {
            var keep = new HashSet<string>(AssetDatabase.GetDependencies(spec.PrefabPath, true));
            AssetDatabase.DeleteAsset(scenePath);
            string sceneFolder = spec.Folder + "/Lighting/" + Path.GetFileNameWithoutExtension(scenePath);
            int removed = 0;
            foreach (var folder in new[] { spec.Folder + "/Meshes", spec.Folder + "/Materials", spec.Folder + "/Lighting/Meshes", spec.Folder + "/Lighting/Materials", sceneFolder })
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                foreach (var guid in AssetDatabase.FindAssets("", new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(path) || keep.Contains(path)) continue;
                    if (AssetDatabase.DeleteAsset(path)) removed++;
                }
            }
            Debug.Log($"CTLIFE [venue-bake] {spec.id}: removed {removed} unreferenced bake/build assets.");
        }
    }
}
