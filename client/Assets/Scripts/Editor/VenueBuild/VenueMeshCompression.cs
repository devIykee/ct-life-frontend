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

        /// <summary>
        /// APK-size proxy for the venues: every venue prefab (all catalogue venues, or -ctVenues) with all its dependencies in
        /// one throwaway asset bundle for the active build target (run with -buildTarget Android for the release numbers),
        /// raw and LZ4, plus one LZ4 bundle per selected venue. -ctTag names the receipt (receipts/venue-size-TAG.json).
        /// Shared dependencies (environment textures, palette) are counted once in the combined bundle.
        /// </summary>
        public static void MeasureVenues()
        {
            var args = Environment.GetCommandLineArgs();
            int ti = Array.IndexOf(args, "-ctTag");
            string tag = ti >= 0 && ti + 1 < args.Length ? args[ti + 1] : DateTime.UtcNow.ToString("HHmmss");
            var specs = VenueBuildPipeline.Selected().Where(s => AssetDatabase.LoadAssetAtPath<GameObject>(s.PrefabPath) != null).ToList();
            string outDir = Path.Combine(Path.GetTempPath(), "ctlife-venue-measure");
            var target = EditorUserBuildSettings.activeBuildTarget;
            long Build(string name, string[] assets, BuildAssetBundleOptions options)
            {
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                Directory.CreateDirectory(outDir);
                var build = new AssetBundleBuild { assetBundleName = name, assetNames = assets };
                BuildPipeline.BuildAssetBundles(outDir, new[] { build }, options | BuildAssetBundleOptions.DisableWriteTypeTree, target);
                return new FileInfo(Path.Combine(outDir, name)).Length;
            }
            var all = specs.Select(s => s.PrefabPath).Distinct().ToArray();
            var result = new JObject
            {
                ["tag"] = tag, ["target"] = target.ToString(), ["venues"] = all.Length,
                ["allRawBytes"] = Build("venues_all", all, BuildAssetBundleOptions.UncompressedAssetBundle),
                ["allLz4Bytes"] = Build("venues_all", all, BuildAssetBundleOptions.ChunkBasedCompression),
            };
            var per = new JObject();
            foreach (var id in new[] { "timeline_square", "spaces_studio", "terminal_cafe" })
            {
                var spec = specs.FirstOrDefault(s => s.id == id);
                if (spec != null) per[id] = Build("venue_" + id, new[] { spec.PrefabPath }, BuildAssetBundleOptions.ChunkBasedCompression);
            }
            result["perVenueLz4Bytes"] = per;
            Directory.Delete(outDir, true);
            File.WriteAllText(Path.Combine(VenueBuildPipeline.EvidenceDir, "receipts", "venue-size-" + tag + ".json"), result.ToString());
            Debug.Log($"CTLIFE [venue-size] {tag} ({target}): {all.Length} venues, raw {(long)result["allRawBytes"] / 1048576.0:F1} MiB, " +
                      $"LZ4 {(long)result["allLz4Bytes"] / 1048576.0:F1} MiB; per venue {per.ToString(Newtonsoft.Json.Formatting.None)}");
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
