using System;

namespace CTLife.Audio
{
    /// <summary>Distance-driven local Foley. A pause, warp or invalid sample resets the cadence without a step.</summary>
    public sealed class FootstepCadence
    {
        // Initial cadence calibration: 1.33 s walk / 0.67 s run at 2.2 / 4.5 m/s, assuming two plants/cycle.
        // This follows measured travel, not imported animation events; foot contact still needs device review.
        public const float WalkStepMetres = 1.463f;
        public const float RunStepMetres = 1.508f;
        private float _distance;
        private bool _moving;

        public void Reset() { _distance = 0f; _moving = false; }

        public bool Advance(float horizontalMetres, float deltaSeconds, bool moving, bool running)
        {
            if (!moving || float.IsNaN(horizontalMetres) || float.IsInfinity(horizontalMetres) ||
                float.IsNaN(deltaSeconds) || deltaSeconds <= 0f || deltaSeconds > 0.5f || horizontalMetres <= 0.0001f ||
                horizontalMetres > Math.Max(1.5f, 9f * deltaSeconds))
            { Reset(); return false; }
            float stride = running ? RunStepMetres : WalkStepMetres;
            if (!_moving) { _moving = true; _distance = stride * 0.5f; }
            _distance += horizontalMetres;
            if (_distance < stride) return false;
            _distance %= stride;
            return true;
        }
    }
}
