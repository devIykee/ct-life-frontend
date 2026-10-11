using System;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Explicit presentation binding on inspected Café/Studio prefabs. Original server actions and
    /// actor roots remain unchanged. This component has no Update/IK callback or runtime clip bake.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VenueSceneRoot))]
    public sealed class CafeSeatMotionProfile : MonoBehaviour
    {
        public const string SocialState = "fe_cafe_social_v1";
        public const string TypingState = "fe_cafe_typing_v1";
        public const string WorkExitState = "fe_cafe_work_exit_v1";
        public const string WorkExitIdleState = "fe_cafe_work_exit_idle_v1";
        private const string CafeId = "terminal_cafe", StudioId = "spaces_studio";

        [Serializable]
        public sealed class Binding
        {
            public RuntimeAnimatorController baselineController;
            public Avatar ownAvatar;
            public RuntimeAnimatorController cafeController;
            public CafeWorkExitMotion workExitMotion;
            public CafeWorkExitMotion socialRightExitMotion, socialLeftExitMotion;
        }

        // Filled only by the guarded promotion helper after the two source poses, persisted
        // typing replay, furniture and full-controller transition receipts are accepted.
        [SerializeField] private Binding[] bindings = Array.Empty<Binding>();
        [Serializable]
        public sealed class ExitRoute
        {
            public string spotId;
            // Venue-local ground points, ending at that spot's existing NavMesh approach.
            public Vector3[] points = Array.Empty<Vector3>();
        }
        [SerializeField] private ExitRoute[] seatExits = Array.Empty<ExitRoute>();
        private VenueSceneRoot _venue;

        internal bool TryGetExit(string action, string spotId, Animator animator, out Vector3[] points, out CafeWorkExitMotion motion)
        {
            points = null; motion = null;
            string fitted = ResolveState(action, spotId);
            if (fitted != TypingState && fitted != SocialState) return false;
            string id = _venue.LocalSpotId(spotId);
            if (!TryGetBoundMotion(animator, fitted, id, out motion)) return false;
            ExitRoute found = null;
            foreach (var row in seatExits)
                if (row != null && row.spotId == id) { if (found != null) return false; found = row; }
            if (found == null || found.points == null || found.points.Length == 0 || found.points.Length > 4 ||
                !_venue.TryGetSpot(spotId, out var seat, out _) || !_venue.TryGetSpotApproach(spotId, out var approach)) return false;
            points = new Vector3[found.points.Length];
            float length = 0f; var previous = seat;
            for (int i = 0; i < points.Length; i++)
            {
                var local = found.points[i];
                if (float.IsNaN(local.x) || float.IsInfinity(local.x) || float.IsNaN(local.y) || float.IsInfinity(local.y) || float.IsNaN(local.z) || float.IsInfinity(local.z)) return false;
                points[i] = _venue.transform.TransformPoint(local);
                if (Mathf.Abs(points[i].y - seat.y) > .02f) return false;
                length += Vector3.Distance(previous, points[i]); previous = points[i];
            }
            return length > .02f && length <= 2.5f && Vector3.Distance(points[points.Length - 1], approach) < .02f;
        }

        internal bool TryGetController(Animator animator, out RuntimeAnimatorController controller)
        {
            controller = null;
            if (!isActiveAndEnabled || animator == null) return false;
            _venue = GetComponent<VenueSceneRoot>();
            if (_venue == null || _venue.IsGreybox || !SupportedVenue(_venue.venueId) || bindings == null) return false;
            Binding match = null;
            foreach (var binding in bindings)
            {
                if (binding == null || binding.baselineController == null || binding.ownAvatar == null || binding.cafeController == null) continue;
                if (animator.runtimeAnimatorController != binding.baselineController || animator.avatar != binding.ownAvatar) continue;
                if (match != null) return false; // Ambiguous authored binding must keep the baseline.
                match = binding;
            }
            if (match == null) return false;
            controller = match.cafeController;
            return true;
        }

        private static bool SupportedVenue(string id) => id == CafeId || id == StudioId;
        private static bool NumberedSpot(string id, string prefix, int maximum)
        {
            if (!id.StartsWith(prefix, StringComparison.Ordinal) ||
                !int.TryParse(id.Substring(prefix.Length), out int number) || number < 1 || number > maximum) return false;
            return id == prefix + number; // Reject leading zeros, signed and noncanonical spellings.
        }
        private bool TryGetBoundMotion(Animator animator, string fitted, string id, out CafeWorkExitMotion motion)
        {
            motion = null;
            if (animator == null || bindings == null) return false;
            Binding match = null;
            foreach (var binding in bindings)
            {
                if (binding == null || binding.ownAvatar != animator.avatar ||
                    binding.cafeController != animator.runtimeAnimatorController) continue;
                if (match != null) return false;
                match = binding;
            }
            if (match == null) return false;
            bool left = _venue.venueId == StudioId && NumberedSpot(id, "stage_", 10);
            motion = fitted == TypingState ? match.workExitMotion :
                left ? match.socialLeftExitMotion : match.socialRightExitMotion;
            return motion != null && motion.Valid;
        }

        internal string ResolveState(string authoritativeAction, string spotId)
        {
            if (!isActiveAndEnabled || _venue == null || !SupportedVenue(_venue.venueId) || string.IsNullOrEmpty(spotId)) return authoritativeAction;
            string local = _venue.LocalSpotId(spotId);
            bool cafe = _venue.venueId == CafeId;
            bool social = cafe ? NumberedSpot(local, "table_", 4) :
                NumberedSpot(local, "seat_", 20) || NumberedSpot(local, "stage_", 10);
            bool typing = cafe && NumberedSpot(local, "terminal_", 8);
            if (!(social && authoritativeAction == "act_sit") && !(typing && authoritativeAction == "act_type")) return authoritativeAction;
            // A registry fallback alone cannot opt an unauthored spot into fitted furniture motion.
            foreach (var anchor in _venue.Anchors)
                if (anchor != null && _venue.LocalSpotId(anchor.spotId) == local && anchor.anim == authoritativeAction)
                    return social ? SocialState : TypingState;
            return authoritativeAction;
        }
    }
}
