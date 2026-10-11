using System.Diagnostics;

namespace CTLife.Core
{
    /// <summary>Tagged logging. Every line starts with "CTLIFE" so `adb logcat -s Unity | grep CTLIFE` finds it.</summary>
    public static class Log
    {
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("CTLIFE_VERBOSE")]
        public static void Debug(string tag, string msg) => UnityEngine.Debug.Log($"CTLIFE [{tag}] {msg}");

        public static void Info(string tag, string msg) => UnityEngine.Debug.Log($"CTLIFE [{tag}] {msg}");

        public static void Warn(string tag, string msg) => UnityEngine.Debug.LogWarning($"CTLIFE [{tag}] {msg}");

        public static void Error(string tag, string msg) => UnityEngine.Debug.LogError($"CTLIFE [{tag}] {msg}");

        public static void Exception(string tag, System.Exception e)
        {
            UnityEngine.Debug.LogError($"CTLIFE [{tag}] {e.GetType().Name}: {e.Message}");
            UnityEngine.Debug.LogException(e);
        }

        /// <summary>Contract rule: unknown IDs show a fallback, are logged once, and never crash.</summary>
        public static void UnknownId(string registry, string id)
        {
            var key = registry + ":" + id;
            if (_reported.Add(key)) Warn("ids", $"Unknown {registry} id '{id}', using fallback");
        }

        private static readonly System.Collections.Generic.HashSet<string> _reported = new System.Collections.Generic.HashSet<string>();
    }
}
