using System;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// CT Island v2 kit (World lane): every model the island dresses itself with (buildings, street furniture, parked
    /// cars, trees, bushes, rocks, crops), each with its LOD meshes, material, cull distances and shadow setting. Cells
    /// reference prototypes by index (<see cref="IslandInstances"/>), and <see cref="IslandInstanceRenderer"/> draws them
    /// with GPU instancing.
    /// </summary>
    [CreateAssetMenu(menuName = "CT Life/World/Island Kit")]
    public sealed class IslandKit : ScriptableObject
    {
        [Serializable]
        public sealed class Proto
        {
            public string id;
            [Tooltip("LOD meshes, most detailed first (single submesh each).")]
            public Mesh[] lods = Array.Empty<Mesh>();
            [Tooltip("Camera distance (m) where each LOD stops drawing; the last value is the cull distance.")]
            public float[] lodEnd = Array.Empty<float>();
            public Material material;
            [Tooltip("Material per submesh (a tree's bark and leaves); falls back to material.")]
            public Material[] materials = Array.Empty<Material>();
            [Tooltip("Per-LOD material override (the far billboard LOD of a tree draws with its own baked texture); null entries use materials.")]
            public Material[] lodMaterials = Array.Empty<Material>();
            [Tooltip("Casts realtime shadows from LODs whose end distance is within this range (0 = never).")]
            public float shadowRange = 60f;
            public int layer;
            [Tooltip("Local bounds of LOD0 (pivot at the bottom centre).")]
            public Bounds bounds;
            public string kind;
            [Tooltip("Baked collision for this source model; independent of the render LOD. Existing tree/parked-car colliders stay separately authored.")]
            public IslandCollisionSpec collision = new IslandCollisionSpec();
        }

        public Proto[] protos = Array.Empty<Proto>();

        public int IndexOf(string id)
        {
            for (int i = 0; i < protos.Length; i++) if (protos[i].id == id) return i;
            return -1;
        }
    }
}
