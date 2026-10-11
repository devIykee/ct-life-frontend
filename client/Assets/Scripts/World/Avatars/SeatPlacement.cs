using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Where and how an actor sits on a measured seat (SeatFitCatalog): the seated loop for the seat's kind (or Codex's
    /// fitted café state where it was fitted), the "seatHeight" blend value for this look's scale (the fitted children put
    /// the soles on the floor or footrest), and the visual-model offset in seat space that rests the hip contact on the
    /// seat top (minus the soft sink) and the back against the backrest. Pure lookups; AvatarView applies them.
    /// </summary>
    public static class SeatPlacement
    {
        public struct Result
        {
            public bool valid;
            public string state;
            public float seatHeight;
            public Vector3 offset;
            public SeatFitCatalog.Seat seat;
            public SeatFitCatalog.PoseMetric metric; // the pose at this seat height (feet targets for CharacterIK)
            public float scale;
        }

        private static VenueSceneRoot _loaded;

        /// <summary>The venue on screen: the visit's, else the loaded root (editor captures and tests load without a visit).</summary>
        private static VenueSceneRoot ActiveRoot()
        {
            var visit = VenueSession.Current;
            if (visit != null && visit.Venue != null) return visit.Venue;
            if (_loaded == null || !_loaded.isActiveAndEnabled) _loaded = Object.FindAnyObjectByType<VenueSceneRoot>();
            return _loaded;
        }

        /// <summary>Venue key for the current visit (homes are measured per home type).</summary>
        public static string VenueKey()
        {
            var visit = VenueSession.Current;
            if (visit != null && visit.VenueId == "home") return string.IsNullOrEmpty(visit.HomeId) ? null : "home:" + visit.HomeId;
            if (visit != null) return visit.VenueId;
            var root = ActiveRoot();
            return root != null && !string.IsNullOrEmpty(root.venueId) && root.venueId != "home" ? root.venueId : null;
        }

        public static string LocalSpot(string spotId)
        {
            var venue = ActiveRoot();
            return venue != null ? venue.LocalSpotId(spotId) : spotId;
        }

        public static string Family(Animator animator)
        {
            var catalog = SeatFitCatalog.Instance;
            if (animator == null || catalog == null || catalog.femaleControllers == null) return "m";
            foreach (var c in catalog.femaleControllers) if (c != null && c == animator.runtimeAnimatorController) return "f";
            return "m";
        }

        /// <param name="fittedState">Codex's fitted café state when CafeSeatMotionProfile chose one, else null.</param>
        public static Result Resolve(Animator animator, string serverAnim, string spotId, string fittedState)
        {
            var r = new Result();
            var catalog = SeatFitCatalog.Instance;
            string venue = VenueKey();
            if (catalog == null || animator == null || string.IsNullOrEmpty(spotId) || venue == null) return r;
            if (!catalog.TryGet(venue, LocalSpot(spotId), out var seat)) return r;
            float scale = Mathf.Max(0.5f, animator.transform.localScale.x);
            string family = Family(animator);
            r.seat = seat;
            r.state = fittedState ?? SeatFitCatalog.PoseFor(seat.kind, serverAnim);
            r.seatHeight = SeatFitCatalog.SeatHeight(seat, scale);
            if (!catalog.TryGetPose(r.state, family, r.seatHeight, out var metric) && !catalog.TryGetPose(r.state, family, 0f, out metric)) return r;
            r.offset = SeatFitCatalog.ModelOffset(seat, metric, scale);
            r.metric = metric;
            r.scale = scale;
            r.valid = true;
            return r;
        }
    }
}
