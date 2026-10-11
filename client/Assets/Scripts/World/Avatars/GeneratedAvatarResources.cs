using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.World
{
    /// <summary>Owns only native assets allocated by CharacterAssembler for one generated body.</summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedAvatarResources : MonoBehaviour
    {
        [SerializeField, HideInInspector] private Mesh _mesh;
        [SerializeField, HideInInspector] private Material _material;
        [SerializeField, HideInInspector] private Texture2D _atlas;

        internal void TakeOwnership(Mesh mesh, Material material, Texture2D atlas)
        {
            _mesh = mesh;
            _material = material;
            _atlas = atlas;
        }

        private void OnDestroy()
        {
            DestroyGenerated(_mesh);
            DestroyGenerated(_material);
            DestroyGenerated(_atlas);
            _mesh = null;
            _material = null;
            _atlas = null;
        }

        internal static void DestroyGenerated(Object resource)
        {
            if (resource == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(resource);
                return;
            }
#endif
            Object.Destroy(resource);
        }
    }
}
