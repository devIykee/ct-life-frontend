#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.World.Vehicles;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CTLife.World
{
    /// <summary>
    /// Dev builds only (perf lane): one launch extra, <c>islandPerf=1</c>, runs the same four downtown spots every time and logs
    /// one line per spot, so before/after builds compare like for like:
    /// <c>eye</c> (shot 2, eye level on Consensus Boulevard, 20 s), <c>drive</c> (the sports car on the road network),
    /// <c>fly80</c> (the hovercar crossing downtown at 80 m) and <c>skyline</c> (the hovercar hovering at 80 m, facing the
    /// tower). Each line: fps avg/min, mean GPU / main / render-thread ms (FrameTimingManager), mean and peak draw calls and
    /// triangles, the kit renderer's draws, render scale, thermal and the frame cap. Combine with <c>islandFps=60</c>
    /// (uncapped) and <c>islandRenderScale=0.75</c>.
    /// </summary>
    public sealed class PerfSegments : MonoBehaviour
    {
        /// <summary>The spot being measured now; null records nothing.</summary>
        public static string Label;

        private string _cur;
        private int _n, _timed;
        private double _sumDt, _gpu, _main, _rt, _draws, _tris, _kit;
        private float _worst; private long _drawsMax, _trisMax;
        private ProfilerRecorder _dc, _tr;
        private readonly FrameTiming[] _t = new FrameTiming[1];
        private ulong _lastStamp;
        private bool _capture;
        // main-thread breakdown: Unity markers found by name in this player (missing ones are skipped)
        private static readonly string[] MarkerNames = {
            "PlayerLoop", "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.PhysicsFixedUpdate",
            "PreUpdate.AIUpdatePostScript", "PreLateUpdate.DirectorUpdateAnimationBegin", "PreLateUpdate.DirectorUpdateAnimationEnd",
            "PostLateUpdate.UpdateAllRenderers", "PostLateUpdate.FinishFrameRendering", "Inl_UniversalRenderTotal", "Inl_Shadows",
            "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForRenderThread", "Gfx.WaitForGfxCommandsFromMainThread", "GC.Alloc", "Update.ScriptRunDelayedTasks",
            "FixedUpdate.ScriptRunBehaviourFixedUpdate", "PostLateUpdate.UpdateAudio", "Culling", "CullScriptable" };
        private readonly System.Collections.Generic.List<(string name, ProfilerRecorder rec)> _markers = new System.Collections.Generic.List<(string, ProfilerRecorder)>();
        private double[] _markerSum;
        private double _kitMs, _occluded, _casters, _occMs, _loopMs, _submitMs;

        public static void Ensure()
        {
            if (FindAnyObjectByType<PerfSegments>() != null) return;
            var go = new GameObject("PerfSegments"); DontDestroyOnLoad(go); go.AddComponent<PerfSegments>();
        }

        private void OnEnable()
        {
            _dc = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _tr = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _capture = FindAnyObjectByType<FpsMeter>() == null;       // FpsMeter captures timings every frame already
            var handles = new System.Collections.Generic.List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
            Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var h in handles)
            {
                var d = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h);
                if (Array.IndexOf(MarkerNames, d.Name) >= 0 && !_markers.Exists(m => m.name == d.Name))
                    _markers.Add((d.Name, ProfilerRecorder.StartNew(d.Category, d.Name, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame)));
            }
            _markerSum = new double[_markers.Count];
        }

        private void OnDisable() { _dc.Dispose(); _tr.Dispose(); foreach (var m in _markers) m.rec.Dispose(); _markers.Clear(); }

        private void LateUpdate()
        {
            if (Label != _cur)
            {
                Flush();
                _cur = Label;
                if (_cur != null) Log.Info("perf", "seg start " + _cur);
            }
            if (_cur == null) return;
            if (_capture) FrameTimingManager.CaptureFrameTimings();
            float dt = Time.unscaledDeltaTime;
            _n++; _sumDt += dt; _worst = Mathf.Max(_worst, dt);
            if (FrameTimingManager.GetLatestTimings(1, _t) > 0 && _t[0].frameStartTimestamp != _lastStamp && _t[0].gpuFrameTime > 0)
            {
                _lastStamp = _t[0].frameStartTimestamp;
                _timed++; _gpu += _t[0].gpuFrameTime; _main += _t[0].cpuMainThreadFrameTime; _rt += _t[0].cpuRenderThreadFrameTime;
            }
            long dc = _dc.Valid ? _dc.LastValue : 0, tr = _tr.Valid ? _tr.LastValue : 0;
            _draws += dc; _tris += tr; _kit += IslandInstanceRenderer.LastDraws;
            _drawsMax = Math.Max(_drawsMax, dc); _trisMax = Math.Max(_trisMax, tr);
            _kitMs += IslandInstanceRenderer.LastCpuMs; _occluded += IslandInstanceRenderer.LastOccluded; _casters += IslandInstanceRenderer.LastCasters;
            _occMs += IslandInstanceRenderer.LastOccMs; _loopMs += IslandInstanceRenderer.LastLoopMs; _submitMs += IslandInstanceRenderer.LastSubmitMs;
            for (int i = 0; i < _markers.Count; i++) if (_markers[i].rec.Valid) _markerSum[i] += _markers[i].rec.LastValue;
        }

        private void Flush()
        {
            if (_cur != null && _n > 10)
            {
                var urp = UniversalRenderPipeline.asset;
                string thermal = PerfManager.Instance != null ? PerfManager.Instance.ThermalTelemetry() : "thermal=unknown";
                int k = Math.Max(1, _timed);
                Log.Info("perf", FormattableString.Invariant(
                    $"seg={_cur} frames={_n} seconds={_sumDt:0.0} avg={_n / _sumDt:0.0} min={1f / _worst:0.0} gpuMs={_gpu / k:0.0} mainMs={_main / k:0.0} renderMs={_rt / k:0.0} ") +
                    FormattableString.Invariant($"draws={_draws / _n:0} drawsMax={_drawsMax} tris={_tris / _n / 1e6:0.00}M trisMax={_trisMax / 1e6:0.00}M kitDraws={_kit / _n:0} ") +
                    FormattableString.Invariant($"renderScale={(urp != null ? urp.renderScale : 0f):0.00} targetFps={Application.targetFrameRate} {thermal}"));
                var sb = new System.Text.StringBuilder(FormattableString.Invariant($"seg={_cur} cpu kitRenderMs={_kitMs / _n:0.00} kitOccluded={_occluded / _n:0} kitCasters={_casters / _n:0} kitOccMs={_occMs / _n:0.00} kitLoopMs={_loopMs / _n:0.00} kitSubmitMs={_submitMs / _n:0.00}"));
                for (int i = 0; i < _markers.Count; i++)
                    sb.Append(' ').Append(_markers[i].name).Append('=').Append((_markerSum[i] / _n / (_markers[i].name == "GC.Alloc" ? 1 : 1e6)).ToString("0.00", CultureInfo.InvariantCulture));
                Log.Info("perf", sb.ToString());
            }
            _n = 0; _timed = 0; _sumDt = _gpu = _main = _rt = _draws = _tris = _kit = _kitMs = _occluded = _casters = _occMs = _loopMs = _submitMs = 0; _worst = 0f; _drawsMax = _trisMax = 0;
            if (_markerSum != null) Array.Clear(_markerSum, 0, _markerSum.Length);
        }
    }

    public static class PerfRoute
    {
        public static async Task Run(VenueSession s, Func<bool> alive, CancellationToken ct)
        {
            PerfSegments.Ensure();
            var shots = IslandWaypoints.Load();
            // eye level on Consensus Boulevard: the authored shot-2 pose, 20 s after a 4 s settle
            await IslandWaypoints.Shot(s, "2", ct);
            await Awaitable.WaitForSecondsAsync(4f, ct);
            PerfSegments.Label = "eye";
            await Awaitable.WaitForSecondsAsync(20f, ct);
            PerfSegments.Label = null;
            CameraRig.Instance?.GetComponent<IslandWaypoints.ShotHold>()?.Stop();
            // back beside the showcase fleet, then the sports car on the road and the hovercar at 80 m
            var plaza = IslandWaypoints.Select(shots, "1");
            if (plaza.Count > 0) await IslandWaypoints.Jump(s, plaza[0], ct);
            await Awaitable.WaitForSecondsAsync(3f, ct);
            var sys = VehicleSystem.Current;
            if (sys == null || s.LocalPlayer == null) { Log.Warn("perf", "no vehicle system"); return; }
            sys.StartCoroutine(VehicleDemo.Run(sys, s.LocalPlayer, "sports,roadstar@high", 1f));
            float t0 = Time.realtimeSinceStartup;
            await Awaitable.NextFrameAsync(ct);
            while (alive() && VehicleDemo.Running && Time.realtimeSinceStartup - t0 < 240f)
            {
                var cur = VehicleDemo.Current;
                PerfSegments.Label = cur == null ? null : cur == "sports drive" || cur == "sports drift" || cur == "sports drive2" ? "drive"
                    : cur.EndsWith(" fly80", StringComparison.Ordinal) ? "fly80" : cur.EndsWith(" skyline", StringComparison.Ordinal) ? "skyline" : null;
                await Awaitable.NextFrameAsync(ct);
            }
            PerfSegments.Label = null;
            await Awaitable.NextFrameAsync(ct);
            Log.Info("perf", "route done");
        }
    }
}
#endif
