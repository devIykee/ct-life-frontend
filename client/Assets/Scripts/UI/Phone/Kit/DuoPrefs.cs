using System.Collections.Generic;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>Per-device storage for Duo preferences. PlayerPrefs in the game; an in-memory store in tests.</summary>
    public interface IDuoPrefs
    {
        int GetInt(string key, int fallback);
        void SetInt(string key, int value);
        void Save();
    }

    public static class DuoPrefs
    {
        /// <summary>The store every Duo setting reads; tests swap in <see cref="Memory"/>.</summary>
        public static IDuoPrefs Store = new PlayerPrefsStore();

        public static bool GetBool(string key, bool fallback) => Store.GetInt(key, fallback ? 1 : 0) != 0;

        public static void SetBool(string key, bool value)
        {
            Store.SetInt(key, value ? 1 : 0);
            Store.Save();
        }

        private sealed class PlayerPrefsStore : IDuoPrefs
        {
            public int GetInt(string key, int fallback) => PlayerPrefs.GetInt(key, fallback);
            public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
            public void Save() => PlayerPrefs.Save();
        }

        /// <summary>Volatile store for EditMode tests.</summary>
        public sealed class Memory : IDuoPrefs
        {
            public readonly Dictionary<string, int> Values = new Dictionary<string, int>();
            public int Saves;
            public int GetInt(string key, int fallback) => Values.TryGetValue(key, out var v) ? v : fallback;
            public void SetInt(string key, int value) => Values[key] = value;
            public void Save() => Saves++;
        }
    }
}
