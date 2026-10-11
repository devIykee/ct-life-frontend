using System;
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
                    if (avatar.isLocal) continue;
                    Bus.Publish(new AvatarTappedSignal
                    {
                        userId = avatar.userId, npcId = avatar.npcId, archetypeId = avatar.archetypeId, displayName = avatar.displayName,
                    });
                    return;
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.isTrigger) continue;
                // Authored furniture can live under its existing immutable spot anchor.
                // Resolve the touched seat directly; a chair back may be above the legacy floor-radius test.
                var tagged = _hits[i].collider.GetComponentInParent<SpotAnchor>();
                if (tagged != null)
                {
                    if (!string.IsNullOrEmpty(tagged.anim)) SpotTapped?.Invoke(tagged);
                    return;
                }
                var spot = NearestClaimableSpot(_hits[i].point, 0.9f);
                if (spot != null)
                {
                    SpotTapped?.Invoke(spot);
                    return;
                }
                // A ground tap must not unexpectedly replace direct thumb movement with a distant path.
                // Intentional paths (seat interactions and review) use LocalPlayerController.MoveTo.
                return;
            }
        }

        private static SpotAnchor NearestClaimableSpot(Vector3 p, float radius)
        {
            SpotAnchor best = null;
            float bestD = radius * radius;
            foreach (var s in UnityEngine.Object.FindObjectsByType<SpotAnchor>(FindObjectsSortMode.None))
            {
                if (string.IsNullOrEmpty(s.anim)) continue;
                float d = (s.transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        private sealed class HitComparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly HitComparer Instance = new HitComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
