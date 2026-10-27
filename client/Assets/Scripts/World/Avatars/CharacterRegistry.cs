using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Which character prefab to spawn, decoupled from how the art was made. Any Unity Humanoid prefab works
    /// (VRM/FBX, single mesh or modular): clips in the shared controller retarget through the Humanoid avatar.
    /// Resolution order for a player look: a `looks` entry matching the full look key, then any single look item id
    /// (head, hair, top, skin… in that order), then `defaultPlayer`. NPCs: `archetypes[archetypeId]`, then `defaultNpc`.
    /// If nothing resolves, AvatarFactory falls back to modular parts (ItemCatalog) and finally the procedural placeholder.
    /// Asset: Resources/Characters/CharacterRegistry.asset (CT Life → Content → Fill Character Registry).
    /// </summary>
    [CreateAssetMenu(menuName = "CT Life/Character Registry", fileName = "CharacterRegistry")]
    public sealed class CharacterRegistry : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("archetypeId (NPCs), or a look key: an item id (head_*, hair_*, top_*, skin_*) or the full look joined with '|'")]
            public string key;
            public GameObject prefab;
            [Tooltip("Optional full-detail creator/portrait prefab; the world uses prefab with its distance LODs.")]
            public GameObject previewPrefab;
            [Tooltip("Optional authored per-character motion overrides; unset uses the shared registry controller.")]
            public RuntimeAnimatorController controllerOverride;
            [Tooltip("Eye-to-floor height for bubbles/name tags; 0 = measure from renderer bounds")]
            public float height;
            [Tooltip("Optional tint/material variant index the prefab understands (0 = default)")]
            public int variant;
        }

        public RuntimeAnimatorController controller;
        public GameObject defaultPlayer;
        public GameObject defaultNpc;
        public List<Entry> archetypes = new List<Entry>();
        public List<Entry> looks = new List<Entry>();

        private Dictionary<string, Entry> _arch, _look;

        private static CharacterRegistry _instance;
        private static bool _loaded;

        public static CharacterRegistry Instance
        {
            get
            {
                if (!_loaded)
                {
                    _instance = Resources.Load<CharacterRegistry>("Characters/CharacterRegistry");
                    _loaded = true;
                }
                return _instance;
            }
        }

        private void Index()
        {
            if (_arch != null) return;
            _arch = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            _look = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in archetypes) if (e?.prefab != null && !string.IsNullOrEmpty(e.key)) _arch[e.key] = e;
            foreach (var e in looks) if (e?.prefab != null && !string.IsNullOrEmpty(e.key)) _look[e.key] = e;
        }

        public Entry ForArchetype(string archetypeId)
        {
            Index();
            if (archetypeId != null && _arch.TryGetValue(archetypeId, out var e)) return e;
            return defaultNpc != null ? new Entry { key = "defaultNpc", prefab = defaultNpc } : null;
        }

        /// <summary>look = [skinTone, headId, hairId, topId, bottomId, shoesId, accId].</summary>
        public Entry ForLook(string[] look)
        {
            Index();
            if (look != null)
            {
                if (_look.TryGetValue(string.Join("|", look), out var full)) return full;
                foreach (var i in new[] { 1, 2, 3, 0, 4, 5, 6 })
                    if (i < look.Length && !string.IsNullOrEmpty(look[i]) && _look.TryGetValue(look[i], out var e)) return e;
            }
            return defaultPlayer != null ? new Entry { key = "defaultPlayer", prefab = defaultPlayer } : null;
        }

        /// <summary>An exact looks key ("uniform:player_m_rekt_crew"), or null.</summary>
        public Entry ForLookKey(string key)
        {
            Index();
            return key != null && _look.TryGetValue(key, out var e) ? e : null;
        }

        private void OnValidate() => _arch = null;
    }
}
