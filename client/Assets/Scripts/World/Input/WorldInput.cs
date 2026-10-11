using System;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Turns screen taps (delivered by the UI layer's full-screen world-input element) into world actions:
    /// tap an avatar → AvatarTappedSignal (profile card / NPC actions); tap a claimable spot → walk and sit;
    /// Ground taps do not move the player; the HUD's direct controls own locomotion.
    /// </summary>
    public static class WorldInput
    {
        public static event Action<SpotAnchor> SpotTapped;

        private static readonly RaycastHit[] _hits = new RaycastHit[16];

        /// <param name="screenPos">Screen pixels, origin bottom-left (Unity screen space).</param>
        public static void Tap(Vector2 screenPos)
        {
            var cam = CameraRig.Instance != null ? CameraRig.Instance.Camera : Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(screenPos);
            int n = Physics.RaycastNonAlloc(ray, _hits, 200f, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(_hits, 0, n, HitComparer.Instance);

            for (int i = 0; i < n; i++)
            {
                var avatar = _hits[i].collider.GetComponentInParent<AvatarView>();
                if (avatar != null)
                {
                    if (avatar.isLocal || avatar.ambient) continue;   // a passer-by: tap through to the world
                    Bus.Publish(new AvatarTappedSignal
                    {
                        userId = avatar.userId, npcId = avatar.npcId, archetypeId = avatar.archetypeId, displayName = avatar.displayName,
                    });
                    return;
                }
            }

            var spot = ResolveSpotTap(_hits, n, UnityEngine.Object.FindObjectsByType<SpotAnchor>(FindObjectsSortMode.None));
            if (Debug.isDebugBuild)
            {
                var first = new System.Text.StringBuilder();
                for (int i = 0, shown = 0; i < n && shown < 3; i++)
                {
                    if (_hits[i].collider == null || _hits[i].collider.isTrigger) continue;
                    first.Append(shown++ == 0 ? "" : ", ").Append(_hits[i].collider.name).Append(" @").Append(_hits[i].distance.ToString("0.0"));
                }
                Log.Info("input", $"tap {screenPos.x:0},{screenPos.y:0}: {n} hits [{first}] -> {(spot != null ? spot.spotId : "no spot")}");
            }
            if (spot != null) SpotTapped?.Invoke(spot);
            // A ground tap must not unexpectedly replace direct thumb movement with a distant path.
            // Intentional paths (seat interactions and review) use LocalPlayerController.MoveTo.
        }

        /// <summary>Hits further than this beyond the first solid hit are ignored (a tap is about what you touched).</summary>
        public const float TapDepth = 3f;
        /// <summary>Horizontal reach from the touched point to a seat/stage anchor.</summary>
        public const float SpotReachXZ = 0.75f;
        /// <summary>A standing spot (the host's mic) stands beside its desk/mic rather than on it: a longer reach.</summary>
        public const float StandingReachXZ = 1.3f;
        /// <summary>An explicit tap target this far behind the first solid hit still wins (e.g. a seat's box inside a sofa).</summary>
        public const float TapTargetDepth = 1f;
        /// <summary>Furniture-sized colliders only: a touched collider at most this wide offers the anchors it covers.</summary>
        public const float FurnitureMaxSize = 4f;

        /// <summary>
        /// The claimable spot a tap means, from its sorted hits (shared with the editor audit `SpotTapAudit`). Since venues
        /// round 4 every prop is solid, so a tap lands on the chair, sofa, table or stage desk rather than near the floor
        /// anchor; the old rule (first hit only, 3D distance ≤ 0.9 m) missed most seats (2026-10-08). In hit order:
        /// an anchor the touched object belongs to; the anchor horizontally nearest the touched point (≤ 0.75 m, the point
        /// no more than 2 m above / 0.5 m below it); the anchors a touched furniture-sized collider covers (bounds + 0.35 m).
        /// </summary>
        public static SpotAnchor ResolveSpotTap(RaycastHit[] hits, int n, IReadOnlyList<SpotAnchor> anchors)
        {
            // Explicit tap targets (colliders parented to an anchor) right behind what was touched win first.
            float front = -1f;
            for (int i = 0; i < n; i++)
            {
                var hit = hits[i];
                if (hit.collider == null || hit.collider.isTrigger) continue;
                if (front < 0f) front = hit.distance;
                else if (hit.distance > front + TapTargetDepth) break;
                var target = hit.collider.GetComponentInParent<SpotAnchor>();
                if (target != null && !string.IsNullOrEmpty(target.anim)) return target;
            }

            float firstSolid = -1f;
            for (int i = 0; i < n; i++)
            {
                var hit = hits[i];
                if (hit.collider == null || hit.collider.isTrigger) continue;
                if (firstSolid < 0f) firstSolid = hit.distance;
                else if (hit.distance > firstSolid + TapDepth) break;

                var tagged = hit.collider.GetComponentInParent<SpotAnchor>();
                if (tagged != null && !string.IsNullOrEmpty(tagged.anim)) return tagged;

                SpotAnchor best = null;
                float bestD = float.MaxValue;
                foreach (var s in anchors)
                {
                    if (s == null || string.IsNullOrEmpty(s.anim)) continue;
                    var a = s.transform.position;
                    float dy = hit.point.y - a.y;
                    if (dy > 2f || dy < -0.5f) continue;
                    float reach = s.anim.StartsWith("loco_", StringComparison.Ordinal) ? StandingReachXZ : SpotReachXZ;
                    float dx = a.x - hit.point.x, dz = a.z - hit.point.z, d = dx * dx + dz * dz;
                    if (d <= reach * reach && d < bestD) { bestD = d; best = s; }
                }
                if (best != null) return best;

                var bounds = hit.collider.bounds;
                if (bounds.size.x <= FurnitureMaxSize && bounds.size.z <= FurnitureMaxSize)
                {
                    bounds.Expand(new Vector3(0.7f, 0f, 0.7f));
                    float bestCover = float.MaxValue;
                    foreach (var s in anchors)
                    {
                        if (s == null || string.IsNullOrEmpty(s.anim)) continue;
                        var a = s.transform.position;
                        if (a.x < bounds.min.x || a.x > bounds.max.x || a.z < bounds.min.z || a.z > bounds.max.z) continue;
                        float dx = a.x - hit.point.x, dz = a.z - hit.point.z, d = dx * dx + dz * dz;
                        if (d < bestCover) { bestCover = d; best = s; }
                    }
                    if (best != null) return best;
                }
            }
            return null;
        }

        private sealed class HitComparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly HitComparer Instance = new HitComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
