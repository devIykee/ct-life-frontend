using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Duo Shop furniture (2026-10-23): copies the processed CC0 Poly Haven pieces (art/processed/world_v3/v3_furniture,
    /// fu_* entries from art/pipeline/world_v3_furniture.py) into Assets/World/Furniture and writes one prefab per modelKey at
    /// Resources/World/Furniture/&lt;modelKey&gt;.prefab: the root's origin is the floor under the piece's centre (place mode and
    /// the footprint expect it), LOD0/LOD1 under a LODGroup, Kit PBR materials (base colour, normal, mask map).
    /// Batch: -executeMethod CTLife.EditorTools.ShopFurnitureBuilder.Build
    /// </summary>
    public static class ShopFurnitureBuilder
    {
        private const string Dir = "Assets/World/Furniture", Out = "Assets/Resources/World/Furniture";
        private static string Src => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "art", "processed", "world_v3", "v3_furniture"));

        [MenuItem("CT Life/Shop/Build Furniture Prefabs")]
        public static void Build()
        {
            var man = JObject.Parse(File.ReadAllText(Path.Combine(Src, "manifest.json")));
            Directory.CreateDirectory(Dir + "/Models"); Directory.CreateDirectory(Dir + "/Textures"); Directory.CreateDirectory(Dir + "/Materials");
            Directory.CreateDirectory(Out);
            var names = man.Properties().Select(p => p.Name).Where(n => n.StartsWith("fu_", StringComparison.Ordinal)).ToList();
            foreach (var n in names)
            {
                var e = (JObject)man[n];
                Copy(Path.Combine(Src, n + ".fbx"), $"{Dir}/Models/{n}.fbx");
                foreach (var t in e["textures"] ?? new JArray()) Copy(Path.Combine(Src, "textures", (string)t), $"{Dir}/Textures/{(string)t}");
            }
            AssetDatabase.Refresh();
            foreach (var n in names)
            {
                ModelImport($"{Dir}/Models/{n}.fbx");
                foreach (var t in man[n]["textures"] ?? new JArray()) TextureImport($"{Dir}/Textures/{(string)t}");
            }
            ParcelTextures();
            AssetDatabase.Refresh();
            var report = new List<string>();
            foreach (var n in names) report.Add(Prefab(n, (JObject)man[n]));
            AssetDatabase.SaveAssets();
            Debug.Log("CTLIFE [shop] furniture prefabs:\n" + string.Join("\n", report));
        }

        private static void Copy(string src, string dst)
        {
            if (!File.Exists(src)) { Debug.LogWarning("CTLIFE [shop] missing " + src); return; }
            if (File.Exists(dst) && new FileInfo(dst).Length == new FileInfo(src).Length && File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(src))) return;
            File.Copy(src, dst, true);
        }

        private static void ModelImport(string path)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) return;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false; mi.isReadable = false;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.meshCompression = ModelImporterMeshCompression.Low;
            mi.addCollider = false;
            mi.SaveAndReimport();
        }

        private static void TextureImport(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            var file = Path.GetFileNameWithoutExtension(path);
            bool normal = file.EndsWith("_Normal"), mask = file.EndsWith("_MaskMap");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal && !mask;
            ti.mipmapEnabled = true; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 2;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.alphaIsTransparency = !normal && !mask;
            ti.maxTextureSize = 1024;
            var a = ti.GetPlatformTextureSettings("Android");
            a.overridden = true; a.maxTextureSize = mask ? 512 : 1024; a.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(a);
            ti.SaveAndReimport();
        }

        private static void ParcelTextures()
        {
            foreach (var (p, normal) in new[] { ($"{Out}/Textures/parcel_kraft_BaseColor.jpg", false), ($"{Out}/Textures/parcel_kraft_Normal.jpg", true) })
            {
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Repeat; ti.maxTextureSize = 512;
                var a = ti.GetPlatformTextureSettings("Android");
                a.overridden = true; a.maxTextureSize = 512; a.format = TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(a);
                ti.SaveAndReimport();
            }
        }

        private static Material Mat(string fbxMaterial)
        {
            string stem = fbxMaterial.StartsWith("M_") ? fbxMaterial.Substring(2) : fbxMaterial;
            bool glass = stem.EndsWith("_glass");
            string tex = glass ? stem.Substring(0, stem.Length - "_glass".Length) : stem;
            var path = $"{Dir}/Materials/{stem}.mat";
            var sh = Shader.Find("CT Life/World/Kit PBR") ?? Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh) { name = stem }; AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            Texture2D T(string k) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Textures/{tex}_{k}.png");
            void Kw(string k, bool on) { if (on) m.EnableKeyword(k); else m.DisableKeyword(k); }
            var nrm = T("Normal"); var msk = T("MaskMap");
            m.SetTexture("_BaseMap", T("BaseColor"));
            m.SetColor("_BaseColor", glass ? new Color(0.85f, 0.88f, 0.9f, 0.35f) : Color.white);
            m.SetTexture("_BumpMap", nrm); Kw("_NORMALMAP", nrm != null); m.SetFloat("_UseNormal", nrm != null ? 1f : 0f);
            m.SetTexture("_MaskMap", msk); Kw("_MASKMAP", msk != null); m.SetFloat("_UseMask", msk != null ? 1f : 0f);
            Kw("_GLASS", glass); m.SetFloat("_Glass", glass ? 1f : 0f);
            m.SetFloat("_BumpScale", 1f);
            m.SetFloat("_Metallic", msk != null ? 1f : 0f);
            m.SetFloat("_Smoothness", msk != null ? 1f : 0.3f);
            m.SetFloat("_OcclusionStrength", 1f);
            m.SetFloat("_Reflection", 1f);
            m.SetFloat("_TintStrength", 0f);
            m.SetVector("_GroundAO", new Vector4(0.35f, 0.18f, 0f, 0f));
            m.SetFloat("_Cutoff", glass ? 0.08f : 0f);
            m.SetFloat("_Cull", glass ? 0f : 2f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static string Prefab(string name, JObject entry)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/Models/{name}.fbx");
            if (fbx == null) return name + ": no model";
            var root = new GameObject(name);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            model = UnityEngine.Object.Instantiate(model);   // unpacked copy
            model.name = "model";
            model.transform.SetParent(root.transform, false);
            var lod0 = new List<Renderer>(); var lod1 = new List<Renderer>();
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                string n = r.gameObject.name + (mf != null && mf.sharedMesh != null ? "|" + mf.sharedMesh.name : "");
                (n.Contains("_LOD1") || n.Contains("_lod1") ? lod1 : lod0).Add(r);
                var mats = r.sharedMaterials;
                var names = mf != null && mf.sharedMesh != null ? MaterialNames(entry, mats.Length) : null;
                for (int i = 0; i < mats.Length; i++) mats[i] = Mat(names != null && i < names.Count ? names[i] : (string)entry["materials"][0]);
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            // origin on the floor under the centre of LOD0's bounds
            var b = new Bounds(); bool any = false;
            foreach (var r in lod0) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            model.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            if (lod1.Count > 0)
            {
                var g = root.AddComponent<LODGroup>();
                g.SetLODs(new[] { new LOD(0.12f, lod0.ToArray()), new LOD(0.01f, lod1.ToArray()) });
                g.RecalculateBounds();
            }
            var path = $"{Out}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            var sizes = entry["size_m"] as JArray;
            return $"{name}: {lod0.Count}+{lod1.Count} renderers, bounds {b.size.x:0.00} x {b.size.z:0.00} x {b.size.y:0.00} m (manifest {sizes})";
        }

        /// <summary>The FBX's material slots by name (M_x, M_x_glass), from the manifest's list in slot order.</summary>
        private static List<string> MaterialNames(JObject entry, int count)
        {
            var list = (entry["materials"] as JArray)?.Select(t => (string)t).ToList() ?? new List<string>();
            return list.Count >= count ? list : null;
        }
    }
}
