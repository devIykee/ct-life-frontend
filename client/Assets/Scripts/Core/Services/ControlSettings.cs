using System;
using UnityEngine;

namespace CTLife.Core
{
    public enum MovementControlMode { Analog = 0, Arrows = 1 }

    /// <summary>Local input preference only; not part of the server profile or movement payload.</summary>
    public static class ControlSettings
    {
        private const string ModeKey = "ctlife.controls.movement";
        private static MovementControlMode? _mode;
        public static event Action Changed;

        public static MovementControlMode MovementMode
        {
            get
            {
                if (!_mode.HasValue)
                    _mode = PlayerPrefs.GetInt(ModeKey, 0) == 1 ? MovementControlMode.Arrows : MovementControlMode.Analog;
                return _mode.Value;
            }
            set
            {
                if (value != MovementControlMode.Analog && value != MovementControlMode.Arrows)
                    throw new ArgumentOutOfRangeException(nameof(value));
                if (MovementMode == value) return;
                _mode = value;
                PlayerPrefs.SetInt(ModeKey, (int)value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }
    }

    /// <summary>On-foot gaits (Feel lane F1): a human walk, a jog, and a sprint only with the stick pushed to its edge.</summary>
    public enum Gait { Stop = 0, Walk = 1, Jog = 2, Sprint = 3 }

    /// <summary>
    /// Shared analog/arrow gain and gait hysteresis, independent of frame rate and UI coordinates (Feel lane F1, 2026-10-17:
    /// "does it not feel a little bit too fast?"). Human speeds: walk up to 1.6 m/s, jog 3.5 m/s, sprint 6 m/s; the old
    /// 2.2 m/s walk played Codex's 1.0 m/s walk cycle at 2.2x.
    /// </summary>
    public static class DirectMovementInput
    {
        public const float DeadZone = 0.12f;
        public const float JogEnter = 0.62f, JogExit = 0.52f;
        public const float SprintEnter = 0.96f, SprintExit = 0.88f;
        public const float MinWalkSpeed = 0.7f, WalkSpeed = 1.6f, JogSpeed = 3.5f, SprintSpeed = 6f;
        /// <summary>Legacy name of the jog speed (editor import tools, the room's "run above 3.5" anim rule).</summary>
        public const float RunSpeed = JogSpeed;
        /// <summary>Arrow mode: a held arrow walks; dragging out from it jogs, then sprints.</summary>
        public const float ArrowWalkStrength = 0.45f;

        public static float Speed(float strength, ref Gait gait)
        {
            if (float.IsNaN(strength) || float.IsInfinity(strength) || strength <= DeadZone)
            {
                gait = Gait.Stop;
                return 0f;
            }
            strength = Math.Min(1f, strength);
            gait = gait switch
            {
                Gait.Sprint => strength >= SprintExit ? Gait.Sprint : strength >= JogExit ? Gait.Jog : Gait.Walk,
                Gait.Jog => strength >= SprintEnter ? Gait.Sprint : strength >= JogExit ? Gait.Jog : Gait.Walk,
                _ => strength >= SprintEnter ? Gait.Sprint : strength >= JogEnter ? Gait.Jog : Gait.Walk,
            };
            return gait switch
            {
                Gait.Sprint => SprintSpeed,
                Gait.Jog => JogSpeed,
                _ => MinWalkSpeed + (WalkSpeed - MinWalkSpeed) * Math.Min(1f, (strength - DeadZone) / (JogExit - DeadZone)),
            };
        }
    }
}
