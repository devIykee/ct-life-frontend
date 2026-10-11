using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// APK size budget (150 MB) helpers. Android-only texture caps for venue art: anything above 1024 is capped at
    /// 1024 with ASTC 6x6 (the venue sign atlases were 2048 = 2.4 MB each). Source files are not touched.
    /// Batch: Unity -batchmode -quit -projectPath client -executeMethod CTLife.EditorTools.SizeBudget.ApplyBatch
    /// </summary>
    public static class SizeBudget
    {
        public const int VenueAndroidMax = 1024;

        [MenuItem("CT Life/Build/Apply APK size caps")]
        public static void Apply()
        {
            int changed = 0;
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/World/Venues", "Assets/Resources/UI/Art" });
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var g in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                    if (path.Contains("/Lighting/") || path.Contains("Lightmap")) continue; // lightmaps: VenueLightingBuilder owns them
                    var android = ti.GetPlatformTextureSettings("Android");
                    int current = android.overridden ? android.maxTextureSize : ti.maxTextureSize;
                    if (current <= VenueAndroidMax) continue;
                    android.overridden = true;
                    android.maxTextureSize = VenueAndroidMax;
                    if (!android.format.ToString().StartsWith("ASTC", StringComparison.Ordinal)) android.format = TextureImporterFormat.ASTC_6x6;
                    ti.SetPlatformTextureSettings(android);
                    ti.SaveAndReimport();
                    changed++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            Debug.Log($"CTLIFE [size] capped {changed} venue/UI textures at {VenueAndroidMax} on Android");
        }

        public static void ApplyBatch()
        {
            int code = 0;
            try
            {
                Apply();
                // Re-apply the cast import policy (mesh compression, texture caps) to already imported cast assets.
                var cast = AssetDatabase.FindAssets("", new[] { CastImport.Root + "/looks" }).Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
                AssetDatabase.StartAssetEditing();
                try { foreach (var p in cast) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); }
                finally { AssetDatabase.StopAssetEditing(); }
                Debug.Log($"CTLIFE [size] reimported {cast.Count} cast assets with the current policy");
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
