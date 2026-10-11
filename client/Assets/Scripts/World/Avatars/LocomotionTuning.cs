using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Speed-matched locomotion (no foot sliding). The shared controller's "Locomotion" tree blends walk → run by the
    /// actor's real speed between the control's walk maximum and run minimum; "locoRate" plays the blend so a planted
    /// foot travels backward exactly as fast as the body moves forward. Native ground speeds are measured per family on a
    /// real cast look (CharacterFeelController; walk keeps Codex's accepted 1.0 m/s reference).
    /// Resources/Characters/LocomotionTuning.
    /// </summary>
    public sealed class LocomotionTuning : ScriptableObject
    {
        public const string ResourcePath = "Characters/LocomotionTuning";
        /// <summary>Blend thresholds = DirectMovementInput: walk tops out at 2.2 m/s, run starts at 3.6 m/s.</summary>
        public const float BlendWalk = 2.2f, BlendRun = 3.6f;

        [Serializable]
        public sealed class Family
        {
            [Tooltip("Family override controller (Snow m / Rain f); matches the registry entry's controller.")]
            public RuntimeAnimatorController controller;
            public float walk = 1f, run = 5.8f;
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
        public float defaultWalk = 1f, defaultRun = 5.7f;

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
        public (float walk, float run) NativeFor(RuntimeAnimatorController controller)
        {
            if (controller != null)
                foreach (var f in families)
                    if (f != null && f.controller == controller) return (f.walk, f.run);
            return (defaultWalk, defaultRun);
        }

        /// <summary>
        /// Playback rate at <paramref name="speed"/> m/s: below the walk threshold the walk plays at speed/walkNative
        /// (Codex's walk, unchanged); above the run threshold the run plays at speed/runNative; in between the tree blends
        /// both and the native speed blends with it. Clamped so a start/stop never freezes or races the cycle.
        /// </summary>
        public static float Rate(float speed, float walkNative, float runNative)
        {
            if (speed <= 0.05f) return 1f;
            float w = Mathf.InverseLerp(BlendWalk, BlendRun, speed);
            float native = Mathf.Lerp(Mathf.Max(0.1f, walkNative), Mathf.Max(0.1f, runNative), w);
            return Mathf.Clamp(speed / native, 0.3f, 2.4f);
        }
    }
}
