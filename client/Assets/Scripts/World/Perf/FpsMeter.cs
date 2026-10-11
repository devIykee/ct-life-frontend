using System;
using CTLife.Core;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Frame-time statistics for the Seeker runs. Every 5 s logs one line that automated runs grep from logcat:
    /// `CTLIFE [fps] avg=59.9 min=55.1 p1=56.0 p99ms=18.2 avatars=62 tris~ draw~ thermal=0`.
    /// </summary>
    public sealed class FpsMeter : MonoBehaviour
    {
        public const float Window = 5f;

        public static float Average { get; private set; }
        public static float Min { get; private set; }
        public static float OnePercentLow { get; private set; }
        public static float P99FrameMs { get; private set; }

        private readonly float[] _dts = new float[1200];
        private int _count;
        private float _windowStart;

        private void Update()
        {
            if (_count < _dts.Length) _dts[_count++] = Time.unscaledDeltaTime;
            if (Time.unscaledTime - _windowStart < Window) return;
            Flush();
            _windowStart = Time.unscaledTime;
        }

        private void Flush()
        {
            if (_count < 2) { _count = 0; return; }
            float total = 0f, worst = 0f;
            for (int i = 0; i < _count; i++) { total += _dts[i]; worst = Mathf.Max(worst, _dts[i]); }
            Array.Sort(_dts, 0, _count);
            float p99 = _dts[Mathf.Clamp(Mathf.CeilToInt(_count * 0.99f) - 1, 0, _count - 1)];
            int lowCount = Mathf.Max(1, _count / 100);
            float lowSum = 0f;
            for (int i = _count - lowCount; i < _count; i++) lowSum += _dts[i];

            Average = _count / total;
            Min = 1f / worst;
            OnePercentLow = lowCount / lowSum;
            P99FrameMs = p99 * 1000f;
            int thermal = PerfManager.Instance != null ? PerfManager.Instance.ThermalLevel : -1;
            Log.Info("fps", $"avg={Average:0.0} min={Min:0.0} p1={OnePercentLow:0.0} p99ms={P99FrameMs:0.0} frames={_count} avatars={AvatarView.All.Count} thermal={thermal}");
            _count = 0;
        }
    }
}
