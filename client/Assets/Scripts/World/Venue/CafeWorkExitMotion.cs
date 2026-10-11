using System;
using UnityEngine;

namespace CTLife.World
{
    //Per-look authoring samples. Only an explicitly fitted venue-seat profile may reference these.
    public sealed class CafeWorkExitMotion : ScriptableObject
    {
        [Serializable] public sealed class Frame
        {
            public Vector3 body;
            public Vector3 leftFoot, rightFoot;
            public Quaternion leftFootRotation, rightFootRotation;
            public Vector3 leftHand, rightHand, leftElbow, rightElbow;
            public Quaternion leftHandRotation, rightHandRotation;
        }
        public const float Length = 2.2f;
        public const float Rate = 1.5f;
        public const int SampleCount = 265;
        // Horizontal presentation travel mirrors the same two planted pairs in frames.
        // Defaults are the current inspected work exit; social bindings set their signed pair explicitly.
        public Vector3 firstStep = new Vector3(.45f, 0f, -.05f);
        public Vector3 secondStep = new Vector3(.30f, 0f, -.02f);
        public bool hands;
        public bool settleContact; // Only enabled by the inspected Snow exit handoff profile.
        public Frame[] frames = Array.Empty<Frame>();
        public bool Valid => frames != null && frames.Length == SampleCount &&
            ValidStep(firstStep) && ValidStep(secondStep);
        private static bool ValidStep(Vector3 step) => float.IsFinite(step.x) &&
            float.IsFinite(step.y) && float.IsFinite(step.z) && Mathf.Abs(step.y) < .00001f &&
            step.sqrMagnitude > .0001f && step.sqrMagnitude <= .25f;
    }
}
