using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.Core
{
    /// <summary>
    /// Launch-time switches for automated runs. On Android they come from intent extras:
    /// `adb shell am start -n com.ctlife.app/com.unity3d.player.UnityPlayerGameActivity -e env mock -e autologin dev`.
    /// In the editor and on desktop they come from command-line args `-ctlife-env mock`.
    /// Ignored in prod builds (CTLIFE_PROD).
    /// </summary>
    public static class LaunchArgs
    {
        private static Dictionary<string, string> _args;

        public static bool TryGet(string key, out string value)
        {
            EnsureLoaded();
            return _args.TryGetValue(key, out value) && !string.IsNullOrEmpty(value);
        }

        public static string Get(string key, string fallback = null) => TryGet(key, out var v) ? v : fallback;

        public static bool Flag(string key) => TryGet(key, out var v) && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));

        /// <summary>Test hook: replaces the parsed args.</summary>
        public static void SetForTests(Dictionary<string, string> args) => _args = args;

        private static void EnsureLoaded()
        {
            if (_args != null) return;
            _args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
#if CTLIFE_PROD
            return;
#else
            try
            {
                var cli = Environment.GetCommandLineArgs();
                for (int i = 0; i < cli.Length - 1; i++)
                {
                    if (cli[i].StartsWith("-ctlife-", StringComparison.OrdinalIgnoreCase))
                        _args[cli[i].Substring("-ctlife-".Length)] = cli[i + 1];
                }
            }
            catch (Exception e)
            {
                Log.Warn("args", "Could not read command line: " + e.Message);
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            ReadAndroidExtras(new[] { "env", "autologin", "autocreate", "autoenter", "fresh", "alias", "wallet", "venue", "seeker", "phase", "livekitToken", "livekitUrl" });
#endif
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void ReadAndroidExtras(string[] keys)
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                foreach (var k in keys)
                {
                    var v = intent.Call<string>("getStringExtra", k);
                    if (!string.IsNullOrEmpty(v)) _args[k] = v;
                }
            }
            catch (Exception e)
            {
                Log.Warn("args", "Could not read intent extras: " + e.Message);
            }
        }
#endif
    }
}
