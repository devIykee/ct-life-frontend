using System.Collections.Generic;
using UnityEngine;

namespace CTLife.UI.Kit
{
    /// <summary>
    /// The glossy cartoon icon set (purple glossy round buttons, white glyph, plum rim; art thread, 2026-10-09):
    /// Resources/UI/Kit/Icons/&lt;name&gt;.png, imported by Editor/OnboardingArtImporter (ASTC 6x6, max 128).
    /// Names: traits fast_talker, deep_pockets, diamond_hands, early_bird, tipster, rug_veteran; tiles explore, jobs, bank, me,
    /// phone; stats cash, debt, energy, sanity; system settings, back, close, sound_on, sound_off, info, check.
    /// <see cref="Load"/> returns null for a file that is not there yet, and every caller falls back to the USS-drawn badge,
    /// so icons can land one by one without code changes.
    /// </summary>
    public static class KitIcons
    {
        public const string Folder = "UI/Kit/Icons/";
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static readonly HashSet<string> Missing = new HashSet<string>();

        public static Texture2D Load(string name)
        {
            if (string.IsNullOrEmpty(name) || Missing.Contains(name)) return null;
            if (Cache.TryGetValue(name, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>(Folder + name);
            if (t == null) { Missing.Add(name); return null; }
            Cache[name] = t;
            return t;
        }

        /// <summary>Tests and captures after an import: forget the "missing" answers.</summary>
        public static void Reset()
        {
            Cache.Clear();
            Missing.Clear();
        }

        /// <summary>The set's name for a lobby route / UiIcon kind, where they differ.</summary>
        public static string ForRoute(string route) => route switch
        {
            "map" => "explore", "profile" => "me", "phone" => "phone", "jobs" => "jobs", "bank" => "bank", null => "phone", _ => route,
        };
    }
}
