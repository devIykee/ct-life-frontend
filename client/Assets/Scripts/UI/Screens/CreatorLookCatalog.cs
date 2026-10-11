using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.World;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>
    /// Authored, complete character looks. This is presentation data, not a backend item registry.
    /// A choice is shown only when its entire seven-ID key resolves to its authored character prefab.
    /// </summary>
    [CreateAssetMenu(menuName = "CT Life/Creator Look Catalog", fileName = "CreatorLookCatalog")]
    public sealed class CreatorLookCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string labelKey;
            /// <summary>Literal label (cast looks from the manifest) used when labelKey has no string.</summary>
            public string label;
            public string[] look = new string[7];
            public string Display => Strings.T(labelKey) != labelKey ? Strings.T(labelKey) : (string.IsNullOrEmpty(label) ? labelKey : label);
            public float previewYaw = 180f;
        }

        public List<Entry> looks = new List<Entry>();
        private static readonly string[] Slots = { "skin", "head", "hair", "top", "bottom", "shoes", "acc" };

        private static string[][] _fallbackLooks;

        /// <summary>
        /// A stable creator look for a player whose look the server did not send (backend main's PlayerState has no
        /// `look`, feedback #47). Presentation only: picked from the shipped creator looks by a hash of the seed.
        /// </summary>
        public static string[] FallbackLook(string seed)
        {
            if (_fallbackLooks == null) _fallbackLooks = Available().ConvertAll(e => e.look).ToArray();
            if (_fallbackLooks.Length == 0 || string.IsNullOrEmpty(seed)) return null;
            uint h = 2166136261;
            foreach (var c in seed) h = (h ^ c) * 16777619;
            return _fallbackLooks[(int)(h % (uint)_fallbackLooks.Length)];
        }

        /// <summary>The authored preview yaw (facing the camera) for a look, or 0 (Humanoid faces +Z) when it is not in the catalog.</summary>
        public static float PreviewYawFor(string[] look)
        {
            if (look == null) return 0f;
            var key = string.Join("|", look);
            foreach (var e in Available()) if (e.look != null && string.Join("|", e.look) == key) return e.previewYaw;
            return 0f;
        }

        public static List<Entry> Available()
        {
            var result = new List<Entry>();
            var catalog = Resources.Load<CreatorLookCatalog>("UI/CreatorLookCatalog");
            var registry = CharacterRegistry.Instance;
            if (catalog == null || catalog.looks == null || registry == null) return result;
            var seen = new HashSet<GameObject>();
            foreach (var choice in catalog.looks)
            {
                if (choice?.look == null || choice.look.Length != Slots.Length || string.IsNullOrWhiteSpace(choice.labelKey)) continue;
                if (Strings.T(choice.labelKey) == choice.labelKey && string.IsNullOrWhiteSpace(choice.label)) continue;
                bool complete = true;
                foreach (var id in choice.look) if (string.IsNullOrWhiteSpace(id)) { complete = false; break; }
                if (!complete) continue;
                var resolved = registry.ForLook(choice.look);
                // A default prefab or one-item fallback does not demonstrate a complete authored look.
                if (resolved?.prefab == null || !string.Equals(resolved.key, string.Join("|", choice.look), StringComparison.Ordinal)) continue;
                // Different IDs resolving to the same whole-character prefab are not distinct visual choices.
                if (!seen.Add(resolved.prefab)) continue;
                result.Add(choice);
            }
            return result;
        }

    }
}
