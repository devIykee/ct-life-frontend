using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Speed-matched locomotion (no foot sliding). The shared controller's "Locomotion" tree blends walk → jog → sprint by
    /// the actor's real speed at the control's gait speeds (Feel lane F1: 1.6 / 3.5 / 6 m/s); "locoRate" plays the blend so
    /// a planted foot travels backward exactly as fast as the body moves forward. Native ground speeds are measured per
    /// family on a real cast look (CharacterFeelController; walk keeps Codex's accepted 1.0 m/s reference).
    /// Resources/Characters/LocomotionTuning.
    /// </summary>
    public sealed class LocomotionTuning : ScriptableObject
    {
        public const string ResourcePath = "Characters/LocomotionTuning";
        /// <summary>Blend thresholds = the gait speeds (DirectMovementInput): walk, jog (the "run" band), sprint.</summary>
        public const float BlendWalk = CTLife.Core.DirectMovementInput.WalkSpeed, BlendRun = CTLife.Core.DirectMovementInput.JogSpeed,
            BlendSprint = CTLife.Core.DirectMovementInput.SprintSpeed;

        [Serializable]
        public sealed class Family
        {
            [Tooltip("Family override controller (Snow m / Rain f); matches the registry entry's controller.")]
            public RuntimeAnimatorController controller;
            public float walk = 1f, run = 5.8f;
            [Tooltip("Native ground speed of the sprint clip on this family (m/s; measured by CharacterFeelController).")]
            public float sprint = 5.8f;
            [Tooltip("Planted-sole height above the ground in the walk / run-band clips on this family (m; measured by " +
                     "CharacterFeelController): CharacterIK lowers planted feet by it (the cast-wide run correction).")]
            public float walkSoleLift, runSoleLift;
        }

        public List<Family> families = new List<Family>();

        [Serializable]
        public sealed class StateLength { public string state; public float seconds; }
        [Tooltip("Clip length of FE presentation states (sit-down, stand-up, falls, get-ups, idle variations).")]
        public List<StateLength> lengths = new List<StateLength>();

        public float LengthOf(string state, float fallback)
        {
            if (!string.IsNullOrEmpty(state)) foreach (var l in lengths) if (l != null && l.state == state && l.seconds > 0.05f) return l.seconds;
            return fallback;
        }
        public float defaultWalk = 1f, defaultRun = 5.7f, defaultSprint = 5.8f;

        [Serializable]
        public sealed class StatePose { public string state; public Vector3 hipsStart, hipsEnd, headStart; }
        [Tooltip("Root-space hips/head of fall and get-up clips (first/last frame), to align a get-up with a ragdoll or fall.")]
        public List<StatePose> poses = new List<StatePose>();

        public bool TryPose(string state, out StatePose pose)
        {
            if (!string.IsNullOrEmpty(state)) foreach (var p in poses) if (p != null && p.state == state) { pose = p; return true; }
            pose = null;
            return false;
        }

        private static LocomotionTuning _instance;
        private static bool _loaded;
        public static LocomotionTuning Instance
        {
            get
            {
                if (!_loaded) { _instance = Resources.Load<LocomotionTuning>(ResourcePath); _loaded = true; }
                return _instance;
            }
        }

        /// <summary>Native (walk, run) ground speeds for the controller an actor plays.</summary>
        public (float walk, float run) SoleLiftFor(RuntimeAnimatorController controller)
        {
            if (controller != null)
                foreach (var f in families)
                    if (f != null && f.controller == controller) return (f.walkSoleLift, f.runSoleLift);
            return (0f, 0f);
        }

        public (float walk, float run, float sprint) NativeFor(RuntimeAnimatorController controller)
        {
            if (controller != null)
                foreach (var f in families)
                    if (f != null && f.controller == controller) return (f.walk, f.run, f.sprint > 0.1f ? f.sprint : defaultSprint);
            return (defaultWalk, defaultRun, defaultSprint);
        }

        /// <summary>
        /// Playback rate at <paramref name="speed"/> m/s: below the walk threshold the walk plays at speed/walkNative
        /// (Codex's walk, unchanged); above the run threshold the run plays at speed/runNative; in between the tree blends
        /// both and the native speed blends with it. Clamped so a start/stop never freezes or races the cycle.
        /// </summary>
        public static float Rate(float speed, float walkNative, float runNative) => Rate(speed, walkNative, runNative, runNative);

        /// <summary>
        /// Mocap v3: the 100STYLE sprint is upright and short-strided, so at speed-matched playback it reads as a fast jog.
        /// Towards full sprint the cadence runs this much faster than the ground speed (a little stance slide traded for a
        /// sprint that reads as one; AvatarView adds the forward lean).
        /// </summary>
        public const float SprintCadence = 1.08f;

        /// <summary>Three bands: walk → jog between the walk and jog speeds, jog → sprint between the jog and sprint speeds.</summary>
        public static float Rate(float speed, float walkNative, float runNative, float sprintNative)
        {
            if (speed <= 0.05f) return 1f;
            float walk = Mathf.Max(0.1f, walkNative), run = Mathf.Max(0.1f, runNative), sprint = Mathf.Max(0.1f, sprintNative);
            float sprintT = Mathf.InverseLerp(BlendRun, BlendSprint, speed);
            float native = speed <= BlendRun ? Mathf.Lerp(walk, run, Mathf.InverseLerp(BlendWalk, BlendRun, speed))
                : Mathf.Lerp(run, sprint, sprintT);
            return Mathf.Clamp(speed / native * Mathf.Lerp(1f, SprintCadence, sprintT), 0.3f, 2.4f);
        }
    }
}
