using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    public enum SeatKind { Chair, Office, Bench, Pouf, Lounge, BeanBag, Stool }

    /// <summary>
    /// Measured seat geometry for every claimable seat in the shipped venues (SeatFitBuilder measures the built venue
    /// prefabs: seat top under the hips, backrest, front edge, footrest, floor under the feet) and measured seated-pose
    /// metrics per family (hip contact and feet relative to the root, at scale 1). At runtime the seated body is placed so
    /// its hip contact rests on the seat top (soft seats sink a little), against the backrest when there is one; feet are
    /// planted by CharacterIK (floor, or the stool's footrest). Resources/Characters/SeatFits.
    /// </summary>
    public sealed class SeatFitCatalog : ScriptableObject
    {
        public const string ResourcePath = "Characters/SeatFits";

        [Serializable]
        public sealed class Seat
        {
            public string venueId, spotId;
            public SeatKind kind;
            [Tooltip("Seat-local metres (x right, y up from the anchor, z forward): where the hip contact should rest.")]
            public Vector3 hipTarget;
            public float seatTop;        // above the anchor
            public float backDistance;   // behind the anchor (z < 0) at lumbar height; 0 = no backrest
            public float frontEdge;      // forward of the anchor where the seat drops away
            public float rearEdge;       // the seat plateau's rear end (z, usually negative)
            public float floor;          // floor under the feet, relative to the anchor
            public float footrest;       // footrest top relative to the anchor; NaN-free: 0 = none
            public float footrestForward; // the bar's centre line (z)
            public float workTop;        // table/desk top in front (relative to the anchor); 0 = none
            public float workNear;       // its near edge (z)
            public float workZ, workDeviceTop; // laptop/keyboard on it: centre (z) and top; workZ 0 = none found
            public string note;
        }

        [Serializable]
        public sealed class PoseMetric
        {
            public string state;
            public string family;        // "m" | "f"
            public float height;         // seatHeight of the blend child (0 = single clip)
            public Vector3 hipContact;   // root-relative (scale 1): lowest buttock point (y) and its depth (z)
            public Vector3 backContact;  // root-relative: rearmost lumbar point
            public Vector3 leftFoot, rightFoot; // root-relative sole contact patches (lowest vertices)
            public Vector3 calfBack;     // root-relative: rearmost calf point below the seat line (deep seats)
            public Vector3 leftAnkle, rightAnkle; // root-relative ankle joints (the Humanoid foot IK goals)
            public Vector3 leftHand, rightHand;   // root-relative wrists (typing: where the clip holds the hands)
        }

        public List<Seat> seats = new List<Seat>();
        [Tooltip("Controllers of the f family (Rain fits); everything else uses the m metrics.")]
        public RuntimeAnimatorController[] femaleControllers = Array.Empty<RuntimeAnimatorController>();
        public List<PoseMetric> poses = new List<PoseMetric>();

        /// <summary>Seated loop per seat kind (a shared FE rule: every client presents the same pose).</summary>
        public static string PoseFor(SeatKind kind, string serverAnim)
        {
            if (serverAnim == "act_type" || serverAnim == "act_frantic_type") return serverAnim; // typing keeps its own loop
            switch (kind)
            {
                case SeatKind.Lounge: return "act_sit_lounge";
                case SeatKind.BeanBag: return "act_sit_beanbag";
                case SeatKind.Stool: return "act_sit_stool";
                default: return string.IsNullOrEmpty(serverAnim) || serverAnim == "loco_idle" ? "act_sit" : serverAnim;
            }
        }

        /// <summary>How far a soft seat gives under the body (m).</summary>
        public static float Sink(SeatKind kind) => kind switch
        {
            SeatKind.BeanBag => 0.16f,
            SeatKind.Lounge => 0.035f,
            SeatKind.Pouf => 0.02f,
            _ => 0.005f,
        };

        private static SeatFitCatalog _instance;
        private static bool _loaded;
        private Dictionary<string, Seat> _index;

        public static SeatFitCatalog Instance
        {
            get
            {
                if (!_loaded) { _instance = Resources.Load<SeatFitCatalog>(ResourcePath); _loaded = true; }
                return _instance;
            }
        }

        public static string Key(string venueId, string localSpotId) => venueId + "/" + localSpotId;

        public bool TryGet(string venueId, string localSpotId, out Seat seat)
        {
            if (_index == null)
            {
                _index = new Dictionary<string, Seat>(StringComparer.Ordinal);
                foreach (var s in seats) if (s != null) _index[Key(s.venueId, s.spotId)] = s;
            }
            return _index.TryGetValue(Key(venueId, localSpotId), out seat);
        }

        /// <summary>Metric for a state at a seat height (blend children interpolated like the blend tree does).</summary>
        public bool TryGetPose(string state, string family, float seatHeight, out PoseMetric metric)
        {
            PoseMetric lo = null, hi = null;
            foreach (var p in poses)
            {
                if (p == null || p.state != state || p.family != family) continue;
                if (p.height <= seatHeight && (lo == null || p.height > lo.height)) lo = p;
                if (p.height >= seatHeight && (hi == null || p.height < hi.height)) hi = p;
            }
            if (lo == null && hi == null) { metric = null; return false; }
            if (lo == null || hi == null || Mathf.Approximately(lo.height, hi.height)) { metric = lo ?? hi; return true; }
            float t = Mathf.InverseLerp(lo.height, hi.height, seatHeight);
            metric = new PoseMetric
            {
                state = state, family = family, height = seatHeight,
                hipContact = Vector3.Lerp(lo.hipContact, hi.hipContact, t), backContact = Vector3.Lerp(lo.backContact, hi.backContact, t),
                leftFoot = Vector3.Lerp(lo.leftFoot, hi.leftFoot, t), rightFoot = Vector3.Lerp(lo.rightFoot, hi.rightFoot, t),
                calfBack = Vector3.Lerp(lo.calfBack, hi.calfBack, t),
                leftAnkle = Vector3.Lerp(lo.leftAnkle, hi.leftAnkle, t), rightAnkle = Vector3.Lerp(lo.rightAnkle, hi.rightAnkle, t),
                leftHand = Vector3.Lerp(lo.leftHand, hi.leftHand, t), rightHand = Vector3.Lerp(lo.rightHand, hi.rightHand, t),
            };
            return true;
        }

        /// <summary>The seatHeight the blend uses for a seat (hip contact → support under the feet), at look scale 1.</summary>
        public static float SeatHeight(Seat seat, float scale)
        {
            float support = seat.kind == SeatKind.Stool && seat.footrest > 0f ? seat.footrest : seat.floor;
            return (seat.seatTop - Sink(seat.kind) - support) / Mathf.Max(0.5f, scale);
        }

        /// <summary>Upholstered to the floor (no room under the front edge for the calves).</summary>
        public static bool SolidBelow(SeatKind kind) => kind == SeatKind.Pouf || kind == SeatKind.Lounge || kind == SeatKind.BeanBag;

        /// <summary>
        /// Visual-model offset in seat space so the pose's hip contact (scaled with the look) rests on the seat: on the
        /// seat top (minus the soft sink); back against the backrest when the seat has one; on a stool with a footrest,
        /// the sole contact patches on the bar. Upholstered seats keep the calves in front of their front edge (a deep
        /// sofa or pouf is sat on forward, not through), and the hips always stay on the seat.
        /// </summary>
        public static Vector3 ModelOffset(Seat seat, PoseMetric pose, float scale)
        {
            if (seat == null || pose == null) return Vector3.zero;
            var hip = pose.hipContact * scale;
            float y = seat.seatTop - Sink(seat.kind) - hip.y;
            float z;
            if (seat.kind == SeatKind.Stool && seat.footrest > 0f)
                z = seat.footrestForward - (pose.leftFoot.z + pose.rightFoot.z) * 0.5f * scale; // sole contacts on the bar
            else if (seat.backDistance > 0.05f)
                z = -seat.backDistance + 0.015f - pose.backContact.z * scale; // lumbar 1.5 cm off the backrest (cushion give)
            else z = seat.hipTarget.z - hip.z;
            if (SolidBelow(seat.kind) && pose.calfBack.sqrMagnitude > 1e-8f)
                z = Mathf.Max(z, seat.frontEdge + 0.02f - pose.calfBack.z * scale);
            float rear = seat.rearEdge < seat.frontEdge - 0.15f ? seat.rearEdge + 0.04f : seat.frontEdge - 0.4f;
            z = Mathf.Clamp(z, rear - hip.z, Mathf.Max(rear, seat.frontEdge - 0.1f) - hip.z);
            return new Vector3(0f, y, z);
        }

        private void OnValidate() => _index = null;
    }
}
