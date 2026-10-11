using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Maps items.json ids to part prefabs through the FE-added `prefabKey` (contract section 2).
    /// Convention (until the parts move to Addressables): Resources/Characters/Base.prefab is the skeleton with an
    /// Animator; Resources/Characters/Parts/&lt;prefabKey&gt;.prefab holds the part's SkinnedMeshRenderer(s).
    /// Missing base or parts → caller uses the placeholder humanoid (the contract's fallback rule).
    /// </summary>
    public static class ItemCatalog
    {
        private static GameObject _base;
        private static bool _baseChecked;
        private static readonly Dictionary<string, GameObject> _parts = new Dictionary<string, GameObject>();

        public static GameObject BasePrefab
        {
            get
            {
                if (!_baseChecked)
                {
                    _base = Resources.Load<GameObject>("Characters/Base");
                    _baseChecked = true;
                }
                return _base;
            }
        }

        public static RuntimeAnimatorController AvatarController => Resources.Load<RuntimeAnimatorController>("Characters/AvatarController");

        /// <summary>look = [skinTone, headId, hairId, topId, bottomId, shoesId, accId].</summary>
        public static bool TryResolve(string[] look, out GameObject basePrefab, out List<GameObject> partPrefabs)
        {
            basePrefab = BasePrefab;
            partPrefabs = new List<GameObject>();
            if (basePrefab == null || look == null) return false;
            var reg = Services.TryGet<ConfigStore>(out var cfg) ? cfg.Registries : null;
            for (int i = 1; i < look.Length; i++)
            {
                var id = look[i];
                if (string.IsNullOrEmpty(id) || id == "acc_none") continue;
                var key = reg?.Item(id)?.prefabKey;
                if (string.IsNullOrEmpty(key)) continue;
                var prefab = Part(key);
                if (prefab != null) partPrefabs.Add(prefab);
            }
            return partPrefabs.Count > 0;
        }

        private static GameObject Part(string key)
        {
            if (_parts.TryGetValue(key, out var p)) return p;
            p = Resources.Load<GameObject>("Characters/Parts/" + key);
            if (p == null) Log.UnknownId("prefabKey", key);
            _parts[key] = p;
            return p;
        }
    }
}
