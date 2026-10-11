using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Avatar budget from Build Spec "Performance budgets": nearest 12 at LOD0, next 24 at LOD1 with half-rate
    /// animation, the rest at LOD2 with quarter-rate animation, culled beyond 25 m. On a thermal warning
    /// <see cref="ThermalScale"/> drops to 0.5 and animation rates halve again.
    /// </summary>
    public sealed class AvatarLod : MonoBehaviour
    {
        public const int Lod0Count = 12;
        public const int Lod1Count = 24;
        public const float CullDistance = 25f;

        public static float ThermalScale = 1f;

        public struct Stats
        {
            public int registered, pendingAssignment, distanceCulled, lod0Enabled, lod1Enabled, lod2Enabled;
            public int renderersEnabled, proceduralAvatars, animatorAvatars;
        }

        /// <summary>Eligibility snapshot only: camera frustum/occlusion and renderer LOD groups are separate.</summary>
        public static Stats CollectStats()
        {
            var stats = new Stats();
            foreach (var avatar in AvatarView.All)
            {
                if (avatar == null) continue;
                stats.registered++;
                stats.renderersEnabled += avatar.EnabledRendererCount;
                if (avatar.Procedural != null) stats.proceduralAvatars++;
                if (avatar.Animator != null) stats.animatorAvatars++;
                if (!avatar.HasLodAssignment) stats.pendingAssignment++;
                else if (avatar.Culled) stats.distanceCulled++;
                else if (avatar.LodTier == 0) stats.lod0Enabled++;
                else if (avatar.LodTier == 1) stats.lod1Enabled++;
                else stats.lod2Enabled++;
            }
            return stats;
        }

        private readonly List<AvatarView> _sorted = new List<AvatarView>();
        private float _next;

        public static AvatarLod Ensure()
        {
            var existing = FindAnyObjectByType<AvatarLod>();
            if (existing != null) return existing;
            var go = new GameObject("CTLife.AvatarLod");
            DontDestroyOnLoad(go);
            return go.AddComponent<AvatarLod>();
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            var cam = Camera.main;
            if (cam == null) return;
            var origin = cam.transform.position;
            Vector3 focus = origin;
            foreach (var a in AvatarView.All)
                if (a.isLocal) { focus = a.PresentationPosition; break; }

            _sorted.Clear();
            _sorted.AddRange(AvatarView.All);
            _sorted.Sort((a, b) =>
                (a.PresentationPosition - focus).sqrMagnitude.CompareTo((b.PresentationPosition - focus).sqrMagnitude));

            for (int i = 0; i < _sorted.Count; i++)
            {
                var a = _sorted[i];
                float d = Vector3.Distance(a.PresentationPosition, focus);
                bool culled = !a.isLocal && d > CullDistance;
                int tier = a.isLocal || i < Lod0Count ? 0 : i < Lod0Count + Lod1Count ? 1 : 2;
                a.ApplyLod(tier, culled, ThermalScale);
            }
        }
    }
}
