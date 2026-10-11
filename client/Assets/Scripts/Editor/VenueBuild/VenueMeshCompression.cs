using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// APK size: Unity mesh compression (quantised vertex channels on disk; decompressed on load, no runtime cost
    /// after loading) on the venue mesh assets. Level Low keeps 16-bit-class UVs so lightmap UV2 and atlas UVs stay
    /// precise; positions/normals shrink most.
    ///   -executeMethod CTLife.EditorTools.VenueBuild.VenueMeshCompression.Run [-ctVenues ids]  (all venues by default)
    ///   -executeMethod CTLife.EditorTools.VenueBuild.VenueMeshCompression.Measure   (asset-bundle size of the meshes)
    /// New bakes apply the same level (VenueBake).
    /// </summary>
    public static class VenueMeshCompression
    {
        public const ModelImporterMeshCompression Level = ModelImporterMeshCompression.Low;

        public static void Run()
        {
            int changed = 0, total = 0;
            foreach (var spec in VenueBuildPipeline.Selected())
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { spec.Folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".asset", StringComparison.Ordinal)) continue;
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (mesh == null) continue;
                    total++;
                    if (MeshUtility.GetMeshCompression(mesh) == Level) continue;
                    MeshUtility.SetMeshCompression(mesh, Level);
                    EditorUtility.SetDirty(mesh);
                    AssetDatabase.SaveAssetIfDirty(mesh);
                    changed++;
                }
            }
            Debug.Log($"CTLIFE [mesh-compression] {changed} of {total} venue meshes set to {Level}.");
        }

        /// <summary>Builds the selected venues' mesh assets into a throwaway bundle (raw and LZ4) and reports sizes.</summary>
        public static void Measure()
        {
            var paths = VenueBuildPipeline.Selected()
                .SelectMany(s => AssetDatabase.FindAssets("t:Mesh", new[] { s.Folder }).Select(AssetDatabase.GUIDToAssetPath))
                .Where(p => p.EndsWith(".asset", StringComparison.Ordinal)).Distinct().ToArray();
            string outDir = Path.Combine(Path.GetTempPath(), "ctlife-mesh-measure");
            var result = new JObject { ["meshes"] = paths.Length };
            foreach (var (label, options) in new[] { ("raw", BuildAssetBundleOptions.UncompressedAssetBundle), ("lz4", BuildAssetBundleOptions.ChunkBasedCompression) })
            {
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                Directory.CreateDirectory(outDir);
                var build = new AssetBundleBuild { assetBundleName = "venue_meshes", assetNames = paths };
                BuildPipeline.BuildAssetBundles(outDir, new[] { build }, options | BuildAssetBundleOptions.DisableWriteTypeTree,
                    EditorUserBuildSettings.activeBuildTarget);
                long bytes = new FileInfo(Path.Combine(outDir, "venue_meshes")).Length;
                result[label + "Bytes"] = bytes;
            }
            Directory.Delete(outDir, true);
            File.WriteAllText(Path.Combine(VenueBuildPipeline.EvidenceDir, "receipts", "mesh-size-" + DateTime.UtcNow.ToString("HHmmss") + ".json"), result.ToString());
            Debug.Log($"CTLIFE [mesh-compression] {paths.Length} meshes: raw {(long)result["rawBytes"] / 1048576.0:F1} MiB, LZ4 {(long)result["lz4Bytes"] / 1048576.0:F1} MiB " +
                      $"(target {EditorUserBuildSettings.activeBuildTarget}).");
        }
    }
}
