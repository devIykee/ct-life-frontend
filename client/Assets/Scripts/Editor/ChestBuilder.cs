using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CTLife.EditorTools
{
    /// <summary>
    /// The daily chest (2026-10-24): copies art/processed/ui/chest (art/pipeline/ui_chest.py, Poly Haven "Treasure Chest",
    /// CC0) into Assets/World/UiChest and writes Resources/UI/Chest/ui_chest.prefab: the root on the floor under the chest's
    /// centre, "model/base" and "model/lid" (the lid's pivot on the hinge), one Kit PBR material.
    /// Batch: -executeMethod CTLife.EditorTools.ChestBuilder.Build ; render check: ChestBuilder.RenderCheck
    /// </summary>
    public static class ChestBuilder
    {
        private const string Dir = "Assets/World/UiChest", Out = "Assets/Resources/UI/Chest";
        private static string Src => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "art", "processed", "ui", "chest"));
        private static readonly string[] Textures = { "ui_chest_BaseColor.png", "ui_chest_Normal.png", "ui_chest_MaskMap.png" };

        [MenuItem("CT Life/Retention/Build Chest Prefab")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir); Directory.CreateDirectory(Out);
            File.Copy(Path.Combine(Src, "ui_chest.fbx"), Dir + "/ui_chest.fbx", true);
            foreach (var t in Textures) File.Copy(Path.Combine(Src, t), Dir + "/" + t, true);
            AssetDatabase.Refresh();
            var mi = (ModelImporter)AssetImporter.GetAtPath(Dir + "/ui_chest.fbx");
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false; mi.isReadable = false;
            mi.importTangents = ModelImporterTangents.CalculateMikk; mi.meshCompression = ModelImporterMeshCompression.Low;
            mi.SaveAndReimport();
            foreach (var t in Textures)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(Dir + "/" + t);
                bool normal = t.Contains("_Normal"), mask = t.Contains("_MaskMap");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal && !mask;
                ti.mipmapEnabled = true; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 2; ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaIsTransparency = false; ti.maxTextureSize = 1024;
                var a = ti.GetPlatformTextureSettings("Android");
                a.overridden = true; a.maxTextureSize = mask ? 512 : 1024; a.format = TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(a);
                ti.SaveAndReimport();
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/ui_chest.mat");
            var sh = Shader.Find("CT Life/World/Kit PBR") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (mat == null) { mat = new Material(sh) { name = "ui_chest" }; AssetDatabase.CreateAsset(mat, Dir + "/ui_chest.mat"); }
            mat.shader = sh;
            Texture2D T(string k) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/ui_chest_{k}.png");
            mat.SetTexture("_BaseMap", T("BaseColor")); mat.SetColor("_BaseColor", new Color(0.86f, 0.74f, 0.62f, 1f));   // warmer, deeper wood under the studio key
            mat.SetTexture("_BumpMap", T("Normal")); mat.EnableKeyword("_NORMALMAP"); mat.SetFloat("_UseNormal", 1f);
            mat.SetTexture("_MaskMap", T("MaskMap")); mat.EnableKeyword("_MASKMAP"); mat.SetFloat("_UseMask", 1f);
            mat.SetFloat("_BumpScale", 1f); mat.SetFloat("_Metallic", 1f); mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_OcclusionStrength", 1f); mat.SetFloat("_Reflection", 1f); mat.SetFloat("_TintStrength", 0f);
            mat.SetVector("_GroundAO", Vector4.zero); mat.SetFloat("_Cutoff", 0f); mat.SetFloat("_Cull", 2f);
            EditorUtility.SetDirty(mat);

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/ui_chest.fbx");
            var root = new GameObject("ui_chest");
            var model = Object.Instantiate(fbx);
            model.name = "model";
            model.transform.SetParent(root.transform, false);
            var b = new Bounds(); bool any = false;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            model.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            PrefabUtility.SaveAsPrefabAsset(root, Out + "/ui_chest.prefab");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            var lid = fbx.transform.Find("lid");
            Debug.Log($"CTLIFE [chest] prefab: bounds {b.size}, lid {(lid != null ? lid.localPosition.ToString("F3") + " rot " + lid.localEulerAngles.ToString("F1") : "MISSING")}");
        }

        /// <summary>Renders the prefab shut and open into Builds/chest-check-*.png (orientation and hinge check).</summary>
        public static void RenderCheck()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Out + "/ui_chest.prefab");
            var go = Object.Instantiate(prefab);
            go.transform.position = new Vector3(0, -500, 0);
            go.transform.rotation = Quaternion.Euler(0f, 162f, 0f);
            var lid = go.transform.Find("model/lid");
            var shut = lid.localRotation;
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.05f, 0.29f, 1f);
            cam.fieldOfView = 24f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 20f;
            var rot = Quaternion.Euler(17f, 0f, 0f);
            cam.transform.SetPositionAndRotation(new Vector3(0, -500 + 0.36f, 0) - rot * Vector3.forward * 3.7f, rot);
            cam.GetUniversalAdditionalCameraData().renderShadows = false;
            var key = new GameObject("key").AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.35f;
            key.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            RenderSettings.sun = key;
            var rt = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            foreach (var (name, open) in new[] { ("shut", 0f), ("open", 1f) })
            {
                lid.localRotation = shut * Quaternion.Euler(-105f * open, 0f, 0f);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "Builds", "chest-check-" + name + ".png"), tex.EncodeToPNG());
            }
            Debug.Log("CTLIFE [chest] render check written to Builds/");
        }

        /// <summary>Renders the runtime ChestStage (CTLife.Features.CityLife) at lid 0 / 0.5 / 1 into Builds/chest-stage-*.png.</summary>
        public static void StageCheck()
        {
            var t = System.Type.GetType("CTLife.CityLife.ChestStage, CTLife.Features.CityLife");
            var F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
            t.GetField("_prefab", F).SetValue(null, AssetDatabase.LoadAssetAtPath<GameObject>(Out + "/ui_chest.prefab"));
            var stage = t.GetProperty("Instance", F).GetValue(null);
            t.GetMethod("Awake", F).Invoke(stage, null);
            t.GetMethod("Show", F).Invoke(stage, null);
            var cam = (Camera)t.GetField("_cam", F).GetValue(stage);
            var rt = (RenderTexture)t.GetProperty("Texture", F).GetValue(stage);
            foreach (var lid in new[] { 0f, 0.5f, 1f })
            {
                t.GetField("Lid", F).SetValue(stage, lid);
                t.GetMethod("Apply", F).Invoke(stage, null);
                Debug.Log("CTLIFE [chest] " + t.GetMethod("Describe").Invoke(stage, null));
                RenderSettings.sun = (Light)t.GetField("_key", F).GetValue(stage);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(Application.dataPath, "..", "Builds", $"chest-stage-{lid:0.0}.png"), tex.EncodeToPNG());
            }
            Debug.Log("CTLIFE [chest] stage check written");
        }
    }
}
