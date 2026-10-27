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
            /// <summary>Stable person/creature identity. Several authored outfits may belong to this same identity.</summary>
            public string identityId;
            public string identityLabelKey;
            public string species = "human";
            /// <summary>Kept (assets, existing players) but not offered in the look picker. Coordinator 2026-10-27: creature
            /// and robot looks are off so every new player character matches the realistic cast; flip back to offer them.</summary>
            public bool hidden;
            public string[] look = new string[7];
            public string Display => Strings.T(labelKey) != labelKey ? Strings.T(labelKey) : (string.IsNullOrEmpty(label) ? labelKey : label);
            public float previewYaw = 180f;
            public string IdentityId => string.IsNullOrEmpty(identityId) ? LegacyIdentity(look) : identityId;
            public string IdentityDisplay => !string.IsNullOrEmpty(identityLabelKey) && Strings.T(identityLabelKey) != identityLabelKey ? Strings.T(identityLabelKey) :
                IdentityId == "head_snow" ? Strings.T("ob.character.snow") :
                IdentityId == "head_rain" ? Strings.T("ob.character.rain") : Display;
        }

        // These are the two documented source meshes of the original cast, not a heuristic for future characters.
        // Keep old saved look IDs valid; grouping changes presentation, not the backend's seven-ID outfit contract.
        public static string LegacyIdentity(string[] look)
        {
            var head = look != null && look.Length > 1 ? look[1] : null;
            if (head == "head_snow" || (head?.StartsWith("head_player_m_", StringComparison.Ordinal) ?? false)) return "head_snow";
            if (head == "head_rain" || (head?.StartsWith("head_player_f_", StringComparison.Ordinal) ?? false)) return "head_rain";
            return head ?? (look == null ? null : string.Join("|", look));
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
            foreach (var e in Available(true)) if (e.look != null && string.Join("|", e.look) == key) return e.previewYaw;
            return 0f;
        }

        /// <summary>The looks the picker offers (hidden ones left out unless <paramref name="includeHidden"/>).</summary>
        public static List<Entry> Available(bool includeHidden = false)
        {
            var result = new List<Entry>();
            var catalog = Resources.Load<CreatorLookCatalog>("UI/CreatorLookCatalog");
            var registry = CharacterRegistry.Instance;
            if (catalog == null || catalog.looks == null || registry == null) return result;
            var seen = new HashSet<GameObject>();
            var choices = new List<Entry>(catalog.looks);
            var additions = Resources.Load<CreatorLookCatalog>("UI/CreatorLookAdditions");
            if (additions?.looks != null) choices.AddRange(additions.looks);
            foreach (var choice in choices)
            {
                if (choice?.look == null || choice.look.Length != Slots.Length || string.IsNullOrWhiteSpace(choice.labelKey)) continue;
                if (choice.hidden && !includeHidden) continue;
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
