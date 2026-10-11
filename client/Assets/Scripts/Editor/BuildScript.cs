using System;
using System.IO;
using System.Linq;
using CTLife.Core;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// CLI APK builds. Example:
    /// Unity -batchmode -quit -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.BuildScript.BuildAndroid
    ///       -ctlifeEnv local -ctlifeOut Builds/ctlife-local-dev.apk -ctlifeDev
    /// `-ctlifeEnv mock|local|staging|prod` selects Resources/Env/EnvConfig_&lt;env&gt; for this build only (the
    /// committed EnvSelection is restored afterwards). `prod` always builds a release player with CTLIFE_PROD
    /// defined (no launch-arg overrides, no dev auth, no dev automation) and refuses `-ctlifeDev`.
    /// staging/prod refuse to build while their EnvConfig still holds placeholder hosts (`.invalid`).
    /// `-ctlifeDev` makes a development build (logs; `-ctlifeProfiler` connects the profiler). Never uploads anywhere.
    /// </summary>
    public static class BuildScript
    {
        private const string SelectionPath = "Assets/Resources/Env/EnvSelection.asset";

        [MenuItem("CT Life/Build/Android APK (local, dev)")]
        public static void BuildAndroidDevMenu() => Build("Builds/ctlife-local-dev.apk", true, EnvId.Local);

        public static void BuildAndroid()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name, string fallback)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
            }
            bool dev = args.Contains("-ctlifeDev");
            bool ok;
            if (!TryParseEnv(Arg("-ctlifeEnv", "local"), out var env))
            {
                Debug.LogError("CTLIFE [build] -ctlifeEnv must be one of mock, local, staging, prod");
                ok = false;
            }
            else ok = Build(Arg("-ctlifeOut", $"Builds/ctlife-{env.ToString().ToLowerInvariant()}{(dev ? "-dev" : "")}.apk"), dev, env);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool TryParseEnv(string value, out EnvId env) =>
            Enum.TryParse(value ?? "", true, out env) && Enum.IsDefined(typeof(EnvId), env);

        /// <summary>The scripting defines a build for <paramref name="env"/> adds on top of the project's.</summary>
        public static string[] ExtraDefines(EnvId env) => env == EnvId.Prod ? new[] { "CTLIFE_PROD" } : Array.Empty<string>();

        public static bool Build(string outPath, bool development, EnvId env)
        {
            VenueContentCatalog.Sync();
            if (env == EnvId.Prod && development)
            {
                Debug.LogError("CTLIFE [build] prod builds are release builds; drop -ctlifeDev");
                return false;
            }
            var selection = AssetDatabase.LoadAssetAtPath<EnvSelection>(SelectionPath);
            var config = selection != null ? selection.Find(env) : null;
            if (selection == null || config == null)
            {
                Debug.LogError($"CTLIFE [build] {SelectionPath} has no EnvConfig for {env}; run CT Life → Setup first");
                return false;
            }
            if ((env == EnvId.Staging || env == EnvId.Prod) && config.PlaceholderProblem() is string problem)
            {
                Debug.LogError($"CTLIFE [build] EnvConfig_{env}: {problem}. Fill in the real hosts (docs/progress/integration.md) before building {env}.");
                return false;
            }
            var previous = selection.active;
            try
            {
                selection.active = env;
                EditorUtility.SetDirty(selection);
                AssetDatabase.SaveAssetIfDirty(selection);
                Debug.Log($"CTLIFE [build] env={env} rest={config.restBaseUrl} realtime={config.realtimeUrl} devAuth={config.allowDevAuth} " +
                          $"livePhase={config.livePhase} development={development} defines=[{string.Join(",", ExtraDefines(env))}]");
                return BuildPlayer(outPath, development, ExtraDefines(env));
            }
            finally
            {
                // BuildPlayer unloads assets; reload the selection before restoring the committed value.
                var reloaded = AssetDatabase.LoadAssetAtPath<EnvSelection>(SelectionPath);
                if (reloaded != null && reloaded.active != previous)
                {
                    reloaded.active = previous;
                    EditorUtility.SetDirty(reloaded);
                    AssetDatabase.SaveAssetIfDirty(reloaded);
                }
            }
        }

        private static bool BuildPlayer(string outPath, bool development, string[] extraDefines)
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
                extraScriptingDefines = extraDefines,
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
