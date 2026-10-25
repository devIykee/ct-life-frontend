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
using UnityEngine.UIElements;
using System.Collections.Generic;

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
        private bool _capture, _skipWorst, _censused;
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

        public static void EnsureHitchProbe() => HitchProbe.Ensure();

        public static void Ensure()
        {
            if (LaunchArgs.Get("hitchProbe") == "1") HitchProbe.Ensure();
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
            _n++; _sumDt += dt;
            if (!_skipWorst) _worst = Mathf.Max(_worst, dt);
            _skipWorst = false;
            if (!_censused && _sumDt > 4.0 && LaunchArgs.Get("islandCensus") == "1") { _censused = true; _skipWorst = true; DrawCensus.Run(_cur); }
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
                    FormattableString.Invariant($"tier={QualityTier.Current} renderScale={(urp != null ? urp.renderScale : 0f):0.00} fixedDt={Time.fixedDeltaTime:0.000} targetFps={Application.targetFrameRate} {thermal}"));
                var sb = new System.Text.StringBuilder(FormattableString.Invariant($"seg={_cur} cpu kitRenderMs={_kitMs / _n:0.00} kitOccluded={_occluded / _n:0} kitCasters={_casters / _n:0} kitOccMs={_occMs / _n:0.00} kitLoopMs={_loopMs / _n:0.00} kitSubmitMs={_submitMs / _n:0.00}"));
                for (int i = 0; i < _markers.Count; i++)
                    sb.Append(' ').Append(_markers[i].name).Append('=').Append((_markerSum[i] / _n / (_markers[i].name == "GC.Alloc" ? 1 : 1e6)).ToString("0.00", CultureInfo.InvariantCulture));
                Log.Info("perf", sb.ToString());
            }
            _censused = false;
            _n = 0; _timed = 0; _sumDt = _gpu = _main = _rt = _draws = _tris = _kit = _kitMs = _occluded = _casters = _occMs = _loopMs = _submitMs = 0; _worst = 0f; _drawsMax = _trisMax = 0;
            if (_markerSum != null) Array.Clear(_markerSum, 0, _markerSum.Length);
        }
    }

    /// <summary>
    /// Dev builds only (perf lane 2): <c>islandCensus=1</c> logs, once per perf segment, the renderers drawn that frame
    /// bucketed by owner (traffic / drivable / avatar / venue shell / other roots), with sub-mesh draws in the main view,
    /// shadow-casting sub-meshes and triangles, so the non-kit draw calls can be attributed. One frame's cost; that frame
    /// is left out of the segment's min fps.
    /// </summary>
    public static class DrawCensus
    {
        private sealed class B { public int renderers, main, casters, skinned; public long tris; public readonly System.Collections.Generic.HashSet<Material> mats = new System.Collections.Generic.HashSet<Material>(); }

        private static string Clean(string n)
        {
            var sb = new System.Text.StringBuilder(n.Length);
            foreach (char c in n) if (!char.IsDigit(c)) sb.Append(c);
            return sb.ToString().Replace("(Clone)", "").Replace(" ", "").Trim('_', '-', '.', ':');
        }

        private static string Key(Renderer r)
        {
            var t = r.transform;
            if (t.GetComponentInParent<CTLife.World.Vehicles.TrafficCar>() != null) return "traffic";
            var v = t.GetComponentInParent<Vehicle>();
            if (v != null) return "vehicle:" + Clean(v.name);
            if (t.GetComponentInParent<CTLife.World.Pedestrians.Pedestrian>() != null) return "pedestrian";
            var av = t.GetComponentInParent<AvatarView>();
            if (av != null) return av.isLocal ? "avatar:local" : "avatar";
            if (t.GetComponentInParent<VenueShell>() != null) return "venueShell";
            var root = t.root;
            string k = Clean(root.name);
            if (t != root)
            {
                var c = t; while (c.parent != root) c = c.parent;
                k += "/" + Clean(c.name);
                // inside an island cell: the cell's part (Terrain, Roads/<material>, Shells, Billboards ...)
                if (k == "CTIsland/cell" && t != c)
                {
                    var d = t; while (d.parent != c) d = d.parent;
                    k += "/" + Clean(d.name);
                    if (d.name == "Roads" && t != d) k += "/" + Clean(t.name);
                }
            }
            return k.Length > 48 ? k.Substring(0, 48) : k;
        }

        public static void Run(string label)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var buckets = new System.Collections.Generic.Dictionary<string, B>();
            int tm = 0, tc = 0;
            foreach (var r in all)
            {
                if (!r.enabled || !r.isVisible || r.forceRenderingOff) continue;
                if ((cam.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                int sub = r.sharedMaterials.Length;
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh != null) sub = Mathf.Min(sub, mesh.subMeshCount);
                bool inMain = GeometryUtility.TestPlanesAABB(planes, r.bounds) && (r.bounds.center - cam.transform.position).magnitude - r.bounds.extents.magnitude < cam.farClipPlane;
                bool casts = r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off;
                var key = Key(r);
                if (!buckets.TryGetValue(key, out var b)) buckets[key] = b = new B();
                b.renderers++;
                if (inMain) { b.main += sub; tm += sub; }
                if (casts) { b.casters += sub; tc += sub; }
                if (r is SkinnedMeshRenderer) b.skinned++;
                foreach (var m in r.sharedMaterials) if (m != null) b.mats.Add(m);
                if (mesh != null && inMain) for (int i = 0; i < Mathf.Min(sub, mesh.subMeshCount); i++) b.tris += (long)mesh.GetIndexCount(i) / 3;
            }
            var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, B>>(buckets);
            list.Sort((a, c) => (c.Value.main + c.Value.casters).CompareTo(a.Value.main + a.Value.casters));
            var sb = new System.Text.StringBuilder(FormattableString.Invariant($"census seg={label} rendererMain={tm} rendererCasters={tc} kitMain={IslandInstanceRenderer.LastDraws} kitCasters={IslandInstanceRenderer.LastCasters} buckets={list.Count}"));
            Log.Info("perf", sb.ToString());
            // every camera that renders this frame (an off-screen preview left on costs a whole extra pass)
            var cams = new System.Text.StringBuilder("census seg=" + label + " cameras:");
            foreach (var c in Camera.allCameras)
                cams.Append(' ').Append(c.name).Append(c.targetTexture != null ? FormattableString.Invariant($"[rt {c.targetTexture.width}x{c.targetTexture.height}]") : "[screen]");
            Log.Info("perf", cams.ToString());
            for (int i = 0; i < list.Count && i < 30; i++)
            {
                var b = list[i].Value;
                Log.Info("perf", FormattableString.Invariant($"census seg={label} {list[i].Key} renderers={b.renderers} main={b.main} casters={b.casters} skinned={b.skinned} mats={b.mats.Count} tris={b.tris / 1000}k"));
            }
        }
    }

    /// <summary>
    /// Dev builds only (perf lane 2): <c>hitchProbe=1</c> records, every frame, the main-thread time of the markers that
    /// cause one-frame spikes (shader program creation, loading, instantiation, texture/mesh uploads, GC, camera renders) and
    /// logs the frame's breakdown whenever it takes longer than 50 ms ("CTLIFE [hitch] ..."), so a spike seen in the fps log
    /// can be attributed. Markers that register later are picked up after a hitch (at most every 10 s; that scan costs a
    /// frame, which is not reported).
    /// </summary>
    public sealed class HitchProbe : MonoBehaviour
    {
        private static readonly string[] Keys = { "CreateGPUProgram", "Shader.Parse", "GC.Collect", "Instantiate", "Resources.Load", "Loading.ReadObject",
            "Loading.AwakeFromLoad", "AwakeFromLoad", "UploadTexture", "Texture.Upload", "Mesh.Upload", "UploadMesh", "Camera.Render", "RenderSingleCamera",
            "Inl_UniversalRenderTotal", "ScriptRunBehaviourUpdate", "ScriptRunBehaviourLateUpdate", "ScriptRunDelayedTasks", "UpdateAllRenderers",
            "Animator.Initialize", "Animator.Rebind", "Physics.Simulate", "PhysX", "UIElements", "UIR.", "Panel.", "Shader.WarmupAllShaders", "FinishFrameRendering",
            "Gfx.WaitFor", "Gfx.PresentFrame", "Shader.", "GpuProgram", "Compile", "Text", "Font", "Glyph", "Atlas", "Texture.", "RenderTexture.", "Mesh.", "SkinnedMesh", "Graphics.", "PlayerLoop" };
        private readonly System.Collections.Generic.HashSet<string> _seen = new System.Collections.Generic.HashSet<string>();
        private readonly System.Collections.Generic.List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle> _handles = new System.Collections.Generic.List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
        private float _discoverAt;
        private readonly FrameTiming[] _ft = new FrameTiming[1];
        private readonly System.Collections.Generic.List<(string name, ProfilerRecorder rec)> _rec = new System.Collections.Generic.List<(string, ProfilerRecorder)>();
        private ProfilerRecorder _gcAlloc;

        public static void Ensure()
        {
            if (FindAnyObjectByType<HitchProbe>() != null) return;
            var go = new GameObject("HitchProbe"); DontDestroyOnLoad(go); go.AddComponent<HitchProbe>();
        }

        private void OnEnable() { Discover(); Log.Info("hitch", "probe on: " + _rec.Count + " markers"); }

        /// <summary>Markers register when first used (a shader compile, a texture upload): look again every few seconds.</summary>
        private void Discover()
        {
            var handles = _handles; handles.Clear();
            Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
            var seen = _seen;
            foreach (var h in handles)
            {
                var d = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h);
                if (d.Name == "GC.Alloc") { if (!_gcAlloc.Valid) _gcAlloc = ProfilerRecorder.StartNew(d.Category, d.Name, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame); continue; }
                bool want = false;
                foreach (var k in Keys) if (d.Name.IndexOf(k, StringComparison.Ordinal) >= 0) { want = true; break; }
                if (!want || !seen.Add(d.Name) || _rec.Count >= 220) continue;
                _rec.Add((d.Name, ProfilerRecorder.StartNew(d.Category, d.Name, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame)));
            }
        }

        private void OnDisable() { foreach (var r in _rec) r.rec.Dispose(); _rec.Clear(); if (_gcAlloc.Valid) _gcAlloc.Dispose(); }

        private void Update()
        {
            FrameTimingManager.CaptureFrameTimings();
            float dt = Time.unscaledDeltaTime;
            // a rediscovery (below) costs ~90 ms itself: the frame after one is not reported
            if (_skipNext) { _skipNext = false; return; }
            if (dt < 0.05f || Time.frameCount < 30) return;
            try { Report(dt); }
            finally
            {
                // markers register when first used: after a hitch look again (at most every 10 s), so a repeat is named
                if (Time.unscaledTime > _discoverAt) { _discoverAt = Time.unscaledTime + 10f; Discover(); _skipNext = true; }
            }
        }

        private bool _skipNext;

        private void Report(float dt)
        {
            // the recorders hold the previous completed frame: the one that just took dt
            var sb = new System.Text.StringBuilder(FormattableString.Invariant($"frame={Time.frameCount - 1} dtMs={dt * 1000f:0} gcAllocKB={(_gcAlloc.Valid ? _gcAlloc.LastValue / 1024.0 : -1):0}"));
            if (FrameTimingManager.GetLatestTimings(1, _ft) > 0)
                sb.Append(FormattableString.Invariant($" [timing main={_ft[0].cpuMainThreadFrameTime:0} render={_ft[0].cpuRenderThreadFrameTime:0} gpu={_ft[0].gpuFrameTime:0}]"));
            var top = new System.Collections.Generic.List<(string, double)>();
            foreach (var r in _rec) if (r.rec.Valid && r.rec.LastValue > 1_000_000) top.Add((r.name, r.rec.LastValue / 1e6));
            top.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            foreach (var (n, ms) in top) sb.Append(' ').Append(n).Append('=').Append(ms.ToString("0.0", CultureInfo.InvariantCulture));
            Log.Info("hitch", sb.ToString());
            double ui = 0;
            foreach (var r in _rec) if (r.name == "PostLateUpdate.UIElementsRepaintPanels" && r.rec.Valid) ui = r.rec.LastValue / 1e6;
            if (ui > 20) UiDiff();
        }

        // ---------------------------------------------------------------- UI attribution: what changed in the UI trees

        private struct UiState { public int text, kids; public bool shown; public string group; }
        private Dictionary<VisualElement, UiState> _ui = new Dictionary<VisualElement, UiState>(), _uiNext = new Dictionary<VisualElement, UiState>();
        private float _uiAt;

        private void LateUpdate()
        {
            if (Time.unscaledTime < _uiAt) return;
            _uiAt = Time.unscaledTime + 1f;
            Snapshot(_ui);
        }

        private static void Snapshot(Dictionary<VisualElement, UiState> into)
        {
            into.Clear();
            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                var root = doc != null ? doc.rootVisualElement : null;
                if (root != null) Walk(root, root, null, into);
            }
        }

        private static void Walk(VisualElement e, VisualElement root, string group, Dictionary<VisualElement, UiState> into)
        {
            if (group == null && e.parent != null && (e.parent == root || e.parent.parent == root))
            {
                string n = !string.IsNullOrEmpty(e.name) ? e.name : null;
                if (n == null) foreach (var c in e.GetClasses()) { n = "." + c; break; }
                if (e.parent != root) group = (n ?? e.GetType().Name);
            }
            into[e] = new UiState { text = e is TextElement t ? (t.text ?? "").GetHashCode() : 0, kids = e.childCount, shown = e.resolvedStyle.display != DisplayStyle.None, group = group ?? "(root)" };
            for (int i = 0; i < e.childCount; i++) Walk(e[i], root, group, into);
        }

        private static string Describe(VisualElement e)
        {
            var sb = new System.Text.StringBuilder();
            for (var c = e; c != null && sb.Length < 160; c = c.parent)
            {
                string n = !string.IsNullOrEmpty(c.name) ? c.name : null;
                if (n == null) foreach (var k in c.GetClasses()) { n = "." + k; break; }
                if (n != null) sb.Insert(0, "/" + n);
            }
            var t = e as TextElement;
            return sb + " '" + (t != null && t.text != null ? (t.text.Length > 30 ? t.text.Substring(0, 30) : t.text) : "") + "' " +
                   FormattableString.Invariant($"{e.worldBound.width:0}x{e.worldBound.height:0}");
        }

        private void UiDiff()
        {
            Snapshot(_uiNext);
            var agg = new Dictionary<string, int[]>();   // added, removed, text, shown
            var changed = new List<string>();
            int[] A(string g) { if (!agg.TryGetValue(g, out var a)) agg[g] = a = new int[4]; return a; }
            foreach (var kv in _uiNext)
            {
                if (!_ui.TryGetValue(kv.Key, out var was)) { A(kv.Value.group)[0]++; continue; }
                if (was.text != kv.Value.text) { A(kv.Value.group)[2]++; if (changed.Count < 6) changed.Add(Describe(kv.Key)); }
                if (was.shown != kv.Value.shown) A(kv.Value.group)[3]++;
            }
            foreach (var kv in _ui) if (!_uiNext.ContainsKey(kv.Key)) A(kv.Value.group)[1]++;
            var sb = new System.Text.StringBuilder(FormattableString.Invariant($"ui-diff elements={_uiNext.Count}"));
            foreach (var kv in agg) sb.Append(' ').Append(kv.Key).Append("[+").Append(kv.Value[0]).Append(" -").Append(kv.Value[1]).Append(" txt").Append(kv.Value[2]).Append(" vis").Append(kv.Value[3]).Append(']');
            foreach (var c in changed) sb.Append(" | ").Append(c);
            Log.Info("hitch", sb.ToString());
            (_ui, _uiNext) = (_uiNext, _ui);
            _uiAt = Time.unscaledTime + 1f;
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
