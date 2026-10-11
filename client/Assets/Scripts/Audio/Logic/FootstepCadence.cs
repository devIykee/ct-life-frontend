using System;

namespace CTLife.Audio
{
    /// <summary>
    /// Distance-driven footstep cadence. A pause, warp or invalid sample resets the cadence without a step.
    /// The stride follows locomotion speed (<see cref="StrideModel"/>), so cadence = speed / stride: a slow drag-walk
    /// steps slower than a full walk, a run faster.
    /// </summary>
    public sealed class FootstepCadence
    {
        // Initial calibration: 1.33 s walk / 0.67 s run cycles at 2.2 / 4.5 m/s, two plants per cycle.
        // This follows measured travel, not imported animation events; foot contact still needs device review.
        public const float WalkStepMetres = StrideModel.WalkStride;
        public const float RunStepMetres = StrideModel.RunStride;
        private float _distance;
        private bool _moving;

        public void Reset() { _distance = 0f; _moving = false; }

        /// <summary>Existing call shape: stride picked by the walk/run flag.</summary>
        public bool Advance(float horizontalMetres, float deltaSeconds, bool moving, bool running) =>
            Step(horizontalMetres, deltaSeconds, moving, running ? RunStepMetres : WalkStepMetres);

        /// <summary>Stride from the measured speed (metres per second).</summary>
        public bool AdvanceAtSpeed(float horizontalMetres, float deltaSeconds, float speed) =>
            Step(horizontalMetres, deltaSeconds, speed > StrideModel.MinMovingSpeed, StrideModel.StrideFor(speed));

        private bool Step(float horizontalMetres, float deltaSeconds, bool moving, float stride)
        {
            if (!moving || float.IsNaN(horizontalMetres) || float.IsInfinity(horizontalMetres) ||
                float.IsNaN(deltaSeconds) || deltaSeconds <= 0f || deltaSeconds > 0.5f || horizontalMetres <= 0.0001f ||
                horizontalMetres > Math.Max(1.5f, 9f * deltaSeconds) || float.IsNaN(stride) || stride <= 0.05f)
            { Reset(); return false; }
            if (!_moving) { _moving = true; _distance = stride * 0.5f; }
            _distance += horizontalMetres;
            if (_distance < stride) return false;
            _distance %= stride;
            return true;
        }
    }

    /// <summary>Stride length and cadence from locomotion speed (calibrated on the two authored gaits).</summary>
    public static class StrideModel
    {
        public const float WalkSpeed = 2.2f, RunSpeed = 4.5f;
        public const float WalkStride = 1.463f, RunStride = 1.508f;
        public const float MinMovingSpeed = 0.2f;

        /// <summary>
        /// Constant stride up to walk speed, linear to the run stride at run speed, constant above. [Inference] that
        /// the walk clip plays proportionally slower below walk speed; verify by ear against foot plants on device.
        /// </summary>
        public static float StrideFor(float speed)
        {
            if (float.IsNaN(speed) || speed <= WalkSpeed) return WalkStride;
            if (speed >= RunSpeed) return RunStride;
            return AudioMath.Lerp(WalkStride, RunStride, (speed - WalkSpeed) / (RunSpeed - WalkSpeed));
        }

        /// <summary>Steps per second at this speed (0 when standing).</summary>
        public static float CadenceHz(float speed) => speed > MinMovingSpeed ? speed / StrideFor(speed) : 0f;
    }
}
