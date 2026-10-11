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
        public static int WorldMask => ~(CharacterMask | (1 << 2) | (1 << 5) | (1 << TapTarget)); // 2 = Ignore Raycast, 5 = UI
        /// <summary>World v2 contract (2026-10-17): the World lane's doorway colliders that stop vehicles but not people.</summary>
        public const int VehicleBlocker = 15;
        /// <summary>
        /// Feel lane layer (20+ per the world v2 contract): the venues' seat tap boxes (VenueKit "Tap target" colliders under a
        /// SpotAnchor). Taps still hit them (WorldInput casts every layer); bodies and the camera pass through them.
        /// </summary>
        public const int TapTarget = 21;
        /// <summary>What the player's body stands on and its probes see: the world without vehicle-only blockers.</summary>
        public static int BodyMask => WorldMask & ~(1 << VehicleBlocker);

        /// <summary>Move a venue's seat tap boxes onto <see cref="TapTarget"/> (idempotent; venues stream in one by one).</summary>
        public static void MarkTapTargets(VenueSceneRoot venue)
        {
            if (venue == null) return;
            foreach (var anchor in venue.Anchors)
            {
                if (anchor == null) continue;
                foreach (var c in anchor.GetComponentsInChildren<Collider>(true))
                    if (c != null && !c.isTrigger) c.gameObject.layer = TapTarget;
            }
        }

        public static void Set(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
        }
    }
}
