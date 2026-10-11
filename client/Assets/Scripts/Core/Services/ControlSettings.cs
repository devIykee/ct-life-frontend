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

    /// <summary>Shared analog/arrow gain and gait hysteresis, independent of frame rate and UI coordinates.</summary>
    public static class DirectMovementInput
    {
        public const float DeadZone = 0.14f;
        public const float RunEnter = 0.82f;
        public const float RunExit = 0.70f;
        public const float WalkSpeed = 2.2f;
        public const float RunSpeed = 4.5f;
        public const float ArrowWalkStrength = 0.64f;

        public static float Speed(float strength, ref bool running)
        {
            if (float.IsNaN(strength) || float.IsInfinity(strength) || strength <= DeadZone)
            {
                running = false;
                return 0f;
            }
            strength = Math.Min(1f, strength);
            running = running ? strength >= RunExit : strength >= RunEnter;
            if (running)
                return 3.6f + 0.9f * Math.Max(0f, (strength - RunExit) / (1f - RunExit));
            return 0.5f + (WalkSpeed - 0.5f) * Math.Min(1f, (strength - DeadZone) / (RunEnter - DeadZone));
        }
    }
}
