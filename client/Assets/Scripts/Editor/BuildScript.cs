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
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".");

            // Phase 1–2 venues ship in the APK (Build Settings scenes or greybox); Addressables content only matters once
            // later venues stream in. Build it when the settings object resolves, otherwise continue without it.
            if (UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings != null)
            {
                AddressableAssetSettings.BuildPlayerContent(out var addrResult);
                if (addrResult != null && !string.IsNullOrEmpty(addrResult.Error))
                {
                    Debug.LogError("CTLIFE [build] Addressables content build failed: " + addrResult.Error);
                    return false;
                }
            }
            else Debug.LogWarning("CTLIFE [build] No Addressables settings object; building the player without Addressables content");

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = development ? BuildOptions.Development | BuildOptions.ConnectWithProfiler : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"CTLIFE [build] {s.result}: {outPath} {s.totalSize / (1024f * 1024f):0.0} MB in {s.totalTime.TotalSeconds:0}s, {s.totalErrors} errors, {s.totalWarnings} warnings");
            return s.result == BuildResult.Succeeded;
        }
    }
}
