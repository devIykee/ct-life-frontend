using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>Owns clones only. Imported meshes, textures, source materials and the Avatar remain shared.</summary>
    [DisallowMultipleComponent]
    public sealed class ModularAvatarResources : MonoBehaviour
    {
        private readonly List<Material> _materials = new List<Material>();
        internal void Own(Material material) => _materials.Add(material);
        internal void Release()
        {
            foreach (var material in _materials) GeneratedAvatarResources.DestroyGenerated(material);
            _materials.Clear();
        }
        private void OnDestroy() => Release();
    }
}
