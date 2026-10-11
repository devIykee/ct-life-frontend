using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>FE presentation bindings. No entries are enabled until an explicit reviewed import.</summary>
    [CreateAssetMenu(menuName = "CT Life/NPC Cast Catalog", fileName = "NpcCastCatalog")]
    public sealed class NpcCastCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Binding
        {
            public string archetypeId;
            // Exact CharacterRegistry keys, never player-look aliases or server cosmetic permissions.
            public List<string> presentationKeys = new List<string>();
        }

        public List<Binding> bindings = new List<Binding>();
        private static NpcCastCatalog _instance;

        public static NpcCastCatalog Instance
        {
            get
            {
                // Unity's destroyed-object null check also allows a later resource reload.
                if (_instance == null) _instance = Resources.Load<NpcCastCatalog>("Characters/NpcCastCatalog");
                return _instance;
            }
        }

        public bool TryResolve(string npcId, string archetypeId, CharacterRegistry registry,
            out CharacterRegistry.Entry entry, out string reason)
        {
            entry = null;
            reason = "unbound-role";
            if (string.IsNullOrEmpty(npcId) || string.IsNullOrEmpty(archetypeId))
            { reason = "missing-identity"; return false; }
            if (registry == null) { reason = "missing-character-registry"; return false; }
            if (bindings == null) { reason = "empty-catalog"; return false; }
            Binding binding = null;
            foreach (var candidate in bindings)
            {
                if (candidate == null || candidate.archetypeId != archetypeId) continue;
                if (binding != null) { reason = "duplicate-role"; return false; }
                binding = candidate;
            }
            if (binding == null) return false;
            if (binding.presentationKeys == null || binding.presentationKeys.Count == 0)
            { reason = "empty-variants"; return false; }
            // Validate the entire role before choosing. A missing variant must not silently
            // change the pool and produce a different identity on another client.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in binding.presentationKeys)
            {
                if (string.IsNullOrEmpty(key) || !seen.Add(key)) { reason = "invalid-variant-key"; return false; }
                if (!TryExactEntry(registry, key, out _)) { reason = "missing-or-ambiguous-presentation"; return false; }
            }
            int index = (int)(IdentityHash(archetypeId, npcId) % (uint)binding.presentationKeys.Count);
            TryExactEntry(registry, binding.presentationKeys[index], out entry);
            reason = "authored";
            return true;
        }

        public static bool TryExactEntry(CharacterRegistry registry, string key, out CharacterRegistry.Entry entry)
        {
            entry = null;
            if (registry == null || registry.looks == null || string.IsNullOrEmpty(key)) return false;
            var parts = key.Split('|');
            if (parts.Length != 7) return false;
            foreach (var part in parts) if (string.IsNullOrWhiteSpace(part)) return false;
            foreach (var candidate in registry.looks)
            {
                if (candidate == null || !string.Equals(candidate.key, key, StringComparison.Ordinal)) continue;
                if (entry != null || candidate.prefab == null) { entry = null; return false; }
                entry = candidate;
            }
            return entry != null;
        }

        /// <summary>Specified FNV-1a over UTF-8. Does not use process/runtime-dependent string hashing.</summary>
        public static uint IdentityHash(string archetypeId, string npcId)
        {
            uint hash = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes((archetypeId ?? "") + "\0" + (npcId ?? "")))
                hash = unchecked((hash ^ b) * 16777619);
            return hash;
        }
    }
}
