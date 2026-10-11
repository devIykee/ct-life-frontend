using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Remote-player jitter buffer (pure, EditMode-tested). The interpolation delay adapts to the measured jitter (mean interval plus two
    /// deviations, 120 to 350 ms), and the rendered position follows the interpolated target at a speed the motion can explain.
    /// </summary>
    public sealed class RemoteTimeline
    {
        public const float MinDelay = 0.12f, MaxDelay = 0.35f, MaxExtrapolate = 0.25f, TeleportDistance = 2.5f;
        private double _lastArrival = -1, _lastStamp = -1;
        private float _mean = 0.1f, _dev = 0.01f;

        public float Delay => Mathf.Clamp(_mean + 2f * _dev + 0.03f, MinDelay, MaxDelay);
        public float MeanInterval => _mean;

        public double Stamp(double arrival)
        {
            if (_lastArrival < 0) { _lastArrival = _lastStamp = arrival; return arrival; }
            float interval = (float)(arrival - _lastArrival);
            _lastArrival = arrival;
            if (interval > 1e-4f && interval < 1.5f)
            {
                _dev = Mathf.Lerp(_dev, Mathf.Abs(interval - _mean), 0.15f);
                _mean = Mathf.Lerp(_mean, interval, 0.1f);
            }
            // Arrival stamps (measured: smoothing them toward the cadence made +-70 ms worse); the delay absorbs the jitter.
            _lastStamp = arrival;
            return arrival;
        }

        /// <summary>Move from <paramref name="current"/> toward <paramref name="target"/> at most ~1.6x the observed speed (plus slack).</summary>
        public static Vector3 Follow(Vector3 current, Vector3 target, float observedSpeed, float dt)
        {
            var d = target - current;
            if (d.magnitude > TeleportDistance) return target;
            float max = (Mathf.Max(observedSpeed, 1.2f) * 1.6f + 0.5f) * Mathf.Max(dt, 1e-4f);
            return d.magnitude <= max ? target : current + d.normalized * max;
        }
    }
}
