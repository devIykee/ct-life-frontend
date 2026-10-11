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
            public string[] look = new string[7];
            public float previewYaw = 180f;
        }

        public List<Entry> looks = new List<Entry>();
        private static readonly string[] Slots = { "skin", "head", "hair", "top", "bottom", "shoes", "acc" };

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
                if (Strings.T(choice.labelKey) == choice.labelKey) continue;
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
