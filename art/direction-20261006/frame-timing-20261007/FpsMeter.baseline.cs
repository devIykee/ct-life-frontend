using System;
using System.Collections.Generic;
using System.Globalization;
using CTLife.Core;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.AdaptivePerformance;
using UnityEngine.Rendering.Universal;

namespace CTLife.World
{
    /// <summary>
    /// Frame-time statistics for the Seeker runs. Every 5 s logs one line that automated runs grep from logcat:
    /// FPS describes the Unity update loop. Avatar eligibility is not visibility;
    /// render counters are the latest recorded frame, not five-second peaks.
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
        private bool _paused, _focused = true, _skipNextFrame;
        private int _droppedSamples;
        private ProfilerRecorder _drawCalls, _batches, _triangles;

        private void OnEnable()
        {
            ResetWindow();
            // Discover the actual Player's counters instead of assuming that a
            // named counter exists in every build/configuration.
            var handles = new List<ProfilerRecorderHandle>();
            try
            {
                ProfilerRecorderHandle.GetAvailable(handles);
                foreach (var handle in handles)
                {
                    var description = ProfilerRecorderHandle.GetDescription(handle);
                    switch (description.Name)
                    {
                        case "Draw Calls Count" when !_drawCalls.Valid: _drawCalls = ProfilerRecorder.StartNew(description.Category, description.Name, 1); break;
                        case "Batches Count" when !_batches.Valid: _batches = ProfilerRecorder.StartNew(description.Category, description.Name, 1); break;
                        case "Triangles Count" when !_triangles.Valid: _triangles = ProfilerRecorder.StartNew(description.Category, description.Name, 1); break;
                    }
                }
            }
            catch (Exception e) { Log.Warn("fps", "Render counter discovery unavailable: " + e.GetType().Name); }
            Log.Info("fps-counters", $"drawCalls={CounterState(_drawCalls)} batches={CounterState(_batches)} triangles={CounterState(_triangles)} sample=last-recorded-frame");
        }

        private void OnDisable()
        {
            if (_drawCalls.Valid) _drawCalls.Dispose();
            if (_batches.Valid) _batches.Dispose();
            if (_triangles.Valid) _triangles.Dispose();
            _drawCalls = _batches = _triangles = default;
        }

        private static string CounterState(ProfilerRecorder recorder) => !recorder.Valid ? "unavailable" : recorder.Count == 0 ? "warming" : "available";
        private static string CounterValue(ProfilerRecorder recorder) => !recorder.Valid ? "unavailable" : recorder.Count == 0 ? "warming" : recorder.LastValue.ToString(CultureInfo.InvariantCulture);

        private void ResetWindow()
        {
            _count = 0;
            _droppedSamples = 0;
            _windowStart = Time.unscaledTime;
            _skipNextFrame = true;
        }

        private void OnApplicationPause(bool paused) { _paused = paused; ResetWindow(); }
        private void OnApplicationFocus(bool focused) { _focused = focused; ResetWindow(); }

        private void Update()
        {
            if (_paused || !_focused) return;
            if (_skipNextFrame) { _skipNextFrame = false; _windowStart = Time.unscaledTime; return; }
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f && !float.IsNaN(dt) && !float.IsInfinity(dt) && _count < _dts.Length) _dts[_count++] = dt;
            else _droppedSamples++;
            if (Time.unscaledTime - _windowStart < Window) return;
            Flush();
            _windowStart = Time.unscaledTime;
        }

        private void Flush()
        {
            if (_count == 0)
            {
                Log.Warn("fps", "No valid frame samples; droppedSamples=" + _droppedSamples);
                _droppedSamples = 0;
                return;
            }
            float total = 0f, worst = 0f;
            for (int i = 0; i < _count; i++) { total += _dts[i]; worst = Mathf.Max(worst, _dts[i]); }
            if (total <= 0f) { ResetWindow(); return; }
            Array.Sort(_dts, 0, _count);
            float p99 = _dts[Mathf.Clamp(Mathf.CeilToInt(_count * 0.99f) - 1, 0, _count - 1)];
            int lowCount = Mathf.Max(1, _count / 100);
            float lowSum = 0f;
            for (int i = _count - lowCount; i < _count; i++) lowSum += _dts[i];

            Average = _count / total;
            Min = 1f / worst;
            OnePercentLow = lowCount / lowSum;
            P99FrameMs = p99 * 1000f;
            var stats = AvatarLod.CollectStats();
            var urp = UniversalRenderPipeline.asset;
            string renderScale = urp != null ? urp.renderScale.ToString("0.000", CultureInfo.InvariantCulture) : "unavailable";
            string thermal = PerfManager.Instance != null ? PerfManager.Instance.ThermalTelemetry() : "thermal=unknown thermalSource=unavailable thermalApplied=unknown";
            string metrics = FormattableString.Invariant($"avg={Average:0.0} min={Min:0.0} p1={OnePercentLow:0.0} p99ms={P99FrameMs:0.0} frames={_count} sampleSeconds={total:0.000} windowSeconds={Time.unscaledTime - _windowStart:0.000} droppedSamples={_droppedSamples}");
            string avatars = $"avatars={stats.registered} avatarsMetric=registered lod0Enabled={stats.lod0Enabled} lod1Enabled={stats.lod1Enabled} lod2Enabled={stats.lod2Enabled} distanceCulled={stats.distanceCulled} lodPending={stats.pendingAssignment} renderersEnabled={stats.renderersEnabled} proceduralAvatars={stats.proceduralAvatars} animatorAvatars={stats.animatorAvatars}";
            string quality = FormattableString.Invariant($"qualityIndex={QualitySettings.GetQualityLevel()} lodBias={QualitySettings.lodBias:0.00} renderScaleAsset={renderScale} urpAdaptive={(urp != null && urp.useAdaptivePerformance ? 1 : 0)} apRenderScaleMultiplier={AdaptivePerformanceRenderSettings.RenderScaleMultiplier:0.000} animationThermalScale={AvatarLod.ThermalScale:0.00} targetFps={Application.targetFrameRate} screen={Screen.width}x{Screen.height}");
            string counters = $"drawCallsLast={CounterValue(_drawCalls)} batchesLast={CounterValue(_batches)} trianglesLast={CounterValue(_triangles)} countersMetric=last-recorded-frame";
            Log.Info("fps", metrics + " " + avatars + " " + quality + " " + thermal + " " + counters);
            _count = 0;
            _droppedSamples = 0;
        }
    }
}
