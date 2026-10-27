using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Perf lane (lane B, 2026-10-27): the Unity Profiler over adb, headless. Connects to a development player through
    /// <c>adb forward tcp:34999 localabstract:Unity-&lt;package&gt;</c>, records while a trigger file exists, then writes the
    /// main thread's costs per sample (self ms, total ms and GC bytes per frame, merged by name along the call path) as text,
    /// so script Update/LateUpdate/FixedUpdate costs and allocations can be named. Saves nothing in the project.
    /// -executeMethod CTLife.EditorTools.RemoteProfileCapture.Run -profOut &lt;dir&gt; [-profIp 127.0.0.1:34999]
    /// Profiling runs from the start; control files in &lt;dir&gt;: create <c>go.&lt;label&gt;</c> to mark a label's first frame, <c>stop</c> to end it (the report is
    /// written as <c>&lt;label&gt;.txt</c>), <c>quit</c> to exit. Without the editor UI the batch editor must run without -quit.
    /// </summary>
    public static class RemoteProfileCapture
    {
        private static string _dir, _ip, _label;
        private static double _nextConnect, _deadline;
        private static bool _connected, _recording;

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string k, string d) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; }
            _dir = Arg("-profOut", Path.Combine(Path.GetTempPath(), "ctlife-prof"));
            _ip = Arg("-profIp", "127.0.0.1:34999");
            Directory.CreateDirectory(_dir);
            _deadline = EditorApplication.timeSinceStartup + 3600;
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.enabled = true;
            EditorApplication.update += Tick;
            Note("waiting for the player at " + _ip);
        }

        private static void Note(string s)
        {
            Debug.Log("CTLIFE [prof] " + s);
            File.AppendAllText(Path.Combine(_dir, "status.log"), DateTime.Now.ToString("HH:mm:ss") + " " + s + "\n");
        }

        private static int _seenFrame = -1, _startFrame;
        private static double _lastFlow;

        private static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (File.Exists(Path.Combine(_dir, "quit")) || now > _deadline) { Note("quit"); EditorApplication.update -= Tick; EditorApplication.Exit(0); return; }
            // dial the player until the connection is a player's (not the editor's own); never re-dial a live player
            // connection (a scene load stalls its frames for seconds, and re-dialing drops it)
            if (now >= _nextConnect)
            {
                _nextConnect = now + 4;
                string id = ProfilerDriver.GetConnectionIdentifier(ProfilerDriver.connectedProfiler) ?? "";
                bool player = id.Length > 0 && id.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) < 0;
                int lastNow = ProfilerDriver.lastFrameIndex;
                bool flowing = lastNow > _seenFrame;
                _seenFrame = lastNow;
                if (flowing) _lastFlow = now;
                if (player && flowing && !_connected) { _connected = true; Note("frames arriving from " + id); }
                // a failed dial can still leave a player identifier: no frames for 20 s (not while recording) dials again
                if (!player || (!_recording && now - _lastFlow > 20))
                {
                    _lastFlow = now;
                    if (_connected) { _connected = false; Note("player connection lost"); }
                    ProfilerDriver.profileEditor = false;
                    ProfilerDriver.DirectIPConnect(_ip);
                }
                ProfilerDriver.enabled = true;
            }
            if (!_recording)
            {
                var go = Directory.GetFiles(_dir, "go.*").FirstOrDefault();
                if (go == null) return;
                _label = Path.GetFileName(go).Substring(3);
                File.Delete(go);
                _startFrame = ProfilerDriver.lastFrameIndex + 1;
                _recording = true;
                Note("recording " + _label + (_connected ? "" : " (no frames arriving yet)"));
                return;
            }
            var stop = Path.Combine(_dir, "stop");
            if (!File.Exists(stop)) return;
            File.Delete(stop);
            _recording = false;
            try { Report(); } catch (Exception e) { Note("report failed: " + e); }
        }

        private sealed class Acc { public double self, total, gc; public int calls; }

        private static void Report()
        {
            int first = Math.Max(ProfilerDriver.firstFrameIndex, _startFrame), last = ProfilerDriver.lastFrameIndex;
            if (first < 0 || last < first) { Note("no frames for " + _label); return; }
            var byPath = new Dictionary<string, Acc>();
            var byName = new Dictionary<string, Acc>();
            int frames = 0; double frameMs = 0;
            for (int f = first; f <= last; f++)
            {
                using (var view = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                    HierarchyFrameDataView.columnTotalTime, false))
                {
                    if (view == null || !view.valid) continue;
                    frames++;
                    frameMs += view.frameTimeMs;
                    Walk(view, view.GetRootItemID(), "", 0, byPath, byName);
                }
            }
            if (frames == 0) { Note("no valid frames for " + _label); return; }
            var sb = new StringBuilder();
            sb.AppendLine(FormattableString.Invariant($"label={_label} frames={frames} meanFrameMs={frameMs / frames:0.00} (main thread, ms or bytes per frame)"));
            sb.AppendLine("== by self time (name)");
            foreach (var kv in byName.OrderByDescending(k => k.Value.self).Take(70))
                sb.AppendLine(FormattableString.Invariant($"{kv.Value.self / frames,8:0.000} self {kv.Value.total / frames,8:0.000} total {kv.Value.gc / frames,9:0} B  {kv.Value.calls / (double)frames,6:0.0}x  {kv.Key}"));
            sb.AppendLine("== by GC alloc (path)");
            foreach (var kv in byPath.Where(k => k.Value.gc > 0).OrderByDescending(k => k.Value.gc).Take(50))
                sb.AppendLine(FormattableString.Invariant($"{kv.Value.gc / frames,9:0} B {kv.Value.total / frames,8:0.000} ms  {kv.Key}"));
            sb.AppendLine("== by total time (path, depth <= 6)");
            foreach (var kv in byPath.OrderByDescending(k => k.Value.total).Take(160))
                sb.AppendLine(FormattableString.Invariant($"{kv.Value.total / frames,8:0.000} total {kv.Value.self / frames,8:0.000} self  {kv.Key}"));
            File.WriteAllText(Path.Combine(_dir, _label + ".txt"), sb.ToString());
            Note("wrote " + _label + ".txt (" + frames + " frames)");
        }


        private static void Walk(HierarchyFrameDataView view, int id, string path, int depth, Dictionary<string, Acc> byPath, Dictionary<string, Acc> byName)
        {
            var children = new List<int>();
            view.GetItemChildren(id, children);
            foreach (var c in children)
            {
                string name = view.GetItemName(c);
                float self = view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnSelfTime);
                float total = view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime);
                float gc = view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnGcMemory);
                float calls = view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnCalls);
                string p = depth == 0 ? name : path + " > " + name;
                if (depth < 6)
                {
                    if (!byPath.TryGetValue(p, out var a)) byPath[p] = a = new Acc();
                    a.self += self; a.total += total; a.gc += gc; a.calls += (int)calls;
                }
                if (!byName.TryGetValue(name, out var b)) byName[name] = b = new Acc();
                b.self += self; b.total += total; b.gc += gc; b.calls += (int)calls;
                Walk(view, c, depth < 6 ? p : path, depth + 1, byPath, byName);
            }
        }
    }
}
