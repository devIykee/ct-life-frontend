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
            Binding match = FamilyBinding(animator, out bool exactAvatar);
            if (match == null) return false;
            // The shared controller carries the fitted states (CharacterFeelController): keep it, no swap (a swap to the
            // standalone café controller would drop locomotion blending, IK and the newer states). The original
            // Snow/Rain avatars with an older controller still get Codex's standalone controller.
            controller = animator.HasState(0, AnimationLibrary.Hash(SocialState)) && animator.HasState(0, AnimationLibrary.Hash(TypingState))
                ? animator.runtimeAnimatorController : exactAvatar ? match.cafeController : null;
            return controller != null;
        }

        private static bool SupportedVenue(string id) => id == CafeId || id == StudioId;

        /// <summary>
        /// The binding for this actor's family. A binding's baseline controller is the family's motion controller
        /// (SnowController / RainController), which every look of that family uses, so it matches cast looks as well as
        /// the original Snow/Rain (whose exact Avatar is reported for the standalone-controller path).
        /// </summary>
        private Binding FamilyBinding(Animator animator, out bool exactAvatar)
        {
            exactAvatar = false;
            if (animator == null || bindings == null) return null;
            Binding match = null;
            foreach (var binding in bindings)
            {
                if (binding == null || binding.baselineController == null) continue;
                bool family = animator.runtimeAnimatorController == binding.baselineController || animator.runtimeAnimatorController == binding.cafeController;
                if (!family) continue;
                if (match != null) return null; // ambiguous
                match = binding;
                exactAvatar = binding.ownAvatar != null && animator.avatar == binding.ownAvatar;
            }
            return match;
        }

        /// <summary>
        /// Codex fitted these states to the café's own furniture: the social sit to its ~0.45 m dining chairs, typing to its
        /// office chairs at the terminals. Other seats (the Studio's bean bags, poufs, stools, armchairs) use the measured
        /// seat-kind poses instead (SeatFitCatalog).
        /// </summary>
        private bool FittedFurniture(string local, bool social)
        {
            var catalog = SeatFitCatalog.Instance;
            if (catalog == null || _venue == null) return true; // no measurements: keep the authored behaviour
            if (!catalog.TryGet(_venue.venueId, local, out var seat)) return false;
            float above = seat.seatTop - seat.floor;
            return social ? seat.kind == SeatKind.Chair && Mathf.Abs(above - 0.45f) < 0.04f
                          : seat.kind == SeatKind.Office && Mathf.Abs(above - 0.45f) < 0.04f;
        }
        private static bool NumberedSpot(string id, string prefix, int maximum)
        {
            if (!id.StartsWith(prefix, StringComparison.Ordinal) ||
                !int.TryParse(id.Substring(prefix.Length), out int number) || number < 1 || number > maximum) return false;
            return id == prefix + number; // Reject leading zeros, signed and noncanonical spellings.
        }
        private bool TryGetBoundMotion(Animator animator, string fitted, string id, out CafeWorkExitMotion motion)
        {
            motion = null;
            var match = FamilyBinding(animator, out _);
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
            if (!FittedFurniture(local, social)) return authoritativeAction;
            // A registry fallback alone cannot opt an unauthored spot into fitted furniture motion.
            foreach (var anchor in _venue.Anchors)
                if (anchor != null && _venue.LocalSpotId(anchor.spotId) == local && anchor.anim == authoritativeAction)
                    return social ? SocialState : TypingState;
            return authoritativeAction;
        }
    }
}
