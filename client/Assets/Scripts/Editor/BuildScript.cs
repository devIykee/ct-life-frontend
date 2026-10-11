using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// CLI APK builds. Example:
    /// Unity -batchmode -quit -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.BuildScript.BuildAndroid
    ///       -ctlifeOut Builds/ctlife-dev.apk -ctlifeDev
    /// `-ctlifeDev` makes a development build (profiler, logs). Never uploads anywhere.
    /// </summary>
    public static class BuildScript
    {
        [MenuItem("CT Life/Build/Android APK (dev)")]
        public static void BuildAndroidDevMenu() => Build("Builds/ctlife-dev.apk", true);

        public static void BuildAndroid()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name, string fallback)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
            }
            bool dev = args.Contains("-ctlifeDev");
            var ok = Build(Arg("-ctlifeOut", dev ? "Builds/ctlife-dev.apk" : "Builds/ctlife.apk"), dev);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Build(string outPath, bool development)
        {
            ProjectSetup.ConfigureAndroid();
            if (File.Exists(AuthoredCharacterImport.PrefabPath))
                CharacterValidation.ValidateSnowAsset(AuthoredCharacterImport.PrefabPath);
            if (File.Exists(AuthoredCharacterImport.WorldPrefabPath))
                CharacterValidation.ValidateSnowAsset(AuthoredCharacterImport.WorldPrefabPath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".");

            // Shipped venue prefabs/scenes need no Addressables catalog. Build configured Addressables explicitly
            // once; the automatic player-build hook otherwise tries again, even when no data builder is configured.
            var addr = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            bool hasEntries = addr != null && addr.groups.Any(g => g != null && g.entries.Count > 0);
            if (hasEntries)
            {
                AddressableAssetSettings.BuildPlayerContent(out var addrResult);
                if (addrResult != null && !string.IsNullOrEmpty(addrResult.Error))
                {
                    Debug.LogError("CTLIFE [build] Addressables content build failed: " + addrResult.Error);
                    return false;
                }
            }
            else Debug.Log("CTLIFE [build] No Addressables entries; shipped content comes from scenes and Resources");

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = outPath,
                target = BuildTarget.Android,
                // Ordinary device review has no Editor profiler consumer; auto-connect
                // was buffering 128 MiB at startup before disabling itself on Seeker.
                options = development ? BuildOptions.Development |
                    (Environment.GetCommandLineArgs().Contains("-ctlifeProfiler") ? BuildOptions.ConnectWithProfiler : BuildOptions.None)
                    : BuildOptions.None,
            };
            var previous = addr != null ? addr.BuildAddressablesWithPlayerBuild : AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
            try
            {
                if (addr != null) addr.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
                var report = BuildPipeline.BuildPlayer(options);
                var s = report.summary;
                double apkMb = File.Exists(outPath) ? new FileInfo(outPath).Length / (1024.0 * 1024.0) : 0;
                Debug.Log($"CTLIFE [build] {s.result}: {outPath} APK {apkMb:0.0} MiB in {s.totalTime.TotalSeconds:0}s, {s.totalErrors} errors, {s.totalWarnings} warnings");
                return s.result == BuildResult.Succeeded;
            }
            finally
            {
                if (addr != null)
                {
                    addr.BuildAddressablesWithPlayerBuild = previous;
                    EditorUtility.SetDirty(addr);
                    AssetDatabase.SaveAssetIfDirty(addr);
                }
            }
        }
    }
}
