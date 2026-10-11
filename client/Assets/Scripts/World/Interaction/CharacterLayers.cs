using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Physics layers for characters (ProjectSettings/TagManager: 8 = Characters, 9 = Ragdoll). Venue props stay on their
    /// own layers; character bodies collide with everything solid except other ragdoll bones of the same body.
    /// </summary>
    public static class CharacterLayers
    {
        public const int Characters = 8;
        public const int Ragdoll = 9;
        public const int CharacterMask = (1 << Characters) | (1 << Ragdoll);
        /// <summary>Solid world for character bodies and ground probes (no characters, no ragdoll bones, no UI).</summary>
        public static int WorldMask => ~(CharacterMask | (1 << 2) | (1 << 5)); // 2 = Ignore Raycast, 5 = UI

        public static void Set(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
        }
    }
}
