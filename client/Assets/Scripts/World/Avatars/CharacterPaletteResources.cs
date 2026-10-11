using UnityEngine;

namespace CTLife.World
{
    /// <summary>One immutable, shared-across-LODs material owned by this instance, never a source asset.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterPaletteResources : MonoBehaviour
    {
        private Material _material;
        internal void Own(Material material) => _material = material;
        private void OnDestroy() { GeneratedAvatarResources.DestroyGenerated(_material); _material = null; }
    }
}
