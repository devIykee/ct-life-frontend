using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Rekt Burger's clock-in loop played for real in the editor (2026-10-25): the Bootstrap scene in PlayMode with the dev
    /// route's launch args, through the game's own flow, player, interiors, camera and avatars. While on shift it renders
    /// the game camera to numbered PNG frames (motion is judged on frames, not stills) and writes a receipt (the route's
    /// log, frame times, draw stats). Batch (no -nographics):
    /// Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.RektBurgerPlay.Launch
    ///   -ctlife-env mock -ctlife-autologin dev -ctlife-autoenter true -ctlife-islandOffline 1 -ctlife-islandRoom rekt_shift
    ///   -ctlife-captureOut /abs/dir
    /// Exit 0 when the route logged "done" with stations worked and customers served. With -ctlife-islandRoom nods it plays
    /// the community nods' route instead (frames from the walk on; exit 0 with two or more nods collected).
    /// </summary>
    [InitializeOnLoad]
    public static class RektBurgerPlay
    {
        private const string ActiveKey = "ctlife.rektburger.play";
        private const int W = 960, H = 540;
        private const float Every = 0.125f, Seconds = 300f;

        private static string _out;
        private static double _deadline;
        private static float _nextFrame;
        private static int _frames;
        private static readonly List<string> Lines = new List<string>();
        private static readonly List<float> Dts = new List<float>();
        private static readonly List<int> Batches = new List<int>();
        private static RenderTexture _rt;
        private static Texture2D _tex;
        private static bool _done, _nodRoute;

        static RektBurgerPlay()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            _out = Arg("-ctlife-captureOut") ?? Path.GetFullPath("../../ct-life-evidence/rektburger-20261025/play");
            if (EditorApplication.isPlaying) Begin();
            else EditorApplication.playModeStateChanged += OnPlayMode;
        }

        public static void Launch()
        {
            SessionState.SetBool(ActiveKey, true);
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity", OpenSceneMode.Single);
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Begin();
        }

        private static void Begin()
        {
            _out = Arg("-ctlife-captureOut") ?? _out;
            foreach (var d in new[] { "frames", "dining" })
            {
                Directory.CreateDirectory(Path.Combine(_out, d));
                foreach (var f in Directory.GetFiles(Path.Combine(_out, d), "*.png")) File.Delete(f);
            }
            _deadline = EditorApplication.timeSinceStartup + Seconds;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
            Debug.Log("CTLIFE [rekt-play] playing; frames to " + _out);
        }

        private static void OnLog(string msg, string stack, LogType type)
        {
            if (msg.StartsWith("CTLIFE [rekt", StringComparison.Ordinal) || msg.StartsWith("CTLIFE [nod", StringComparison.Ordinal) || type == LogType.Exception)
                Lines.Add($"{Time.realtimeSinceStartup:0.0} {msg}");
            if (msg.StartsWith("CTLIFE [nod-route] from", StringComparison.Ordinal)) _nodRoute = true;
            if (msg.StartsWith("CTLIFE [nod-route] done", StringComparison.Ordinal) || msg.StartsWith("CTLIFE [nod-route] no nod", StringComparison.Ordinal)) _done = true;
            if (msg.StartsWith("CTLIFE [rekt-route] done", StringComparison.Ordinal) || msg.StartsWith("CTLIFE [rekt-route] room not", StringComparison.Ordinal) ||
                msg.StartsWith("CTLIFE [rekt-route] no rekt_burger", StringComparison.Ordinal)) _done = true;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (RektBurgerHooks.OnShift || _nodRoute)
            {
                Dts.Add(Time.unscaledDeltaTime);
                Batches.Add(UnityStats.batches);
                if (Time.realtimeSinceStartup >= _nextFrame) { _nextFrame = Time.realtimeSinceStartup + Every; Frame(); }
            }
            if (_done || EditorApplication.timeSinceStartup > _deadline) Finish(_done ? null : "timeout");
        }

        private static Camera _dining;

        private static void Frame()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Shot(cam, "frames", "f");
            // a fixed view of the dining room (the door, the queue, the counter) for the customers' walk
            var room = IslandInteriors.Current != null ? IslandInteriors.Current.ActiveRoom : null;
            if (room == null || room.roomId != RektBurgerHooks.RoomId) { _frames++; return; }
            if (_dining == null) { _dining = new GameObject("rb_dining_cam").AddComponent<Camera>(); _dining.enabled = false; _dining.fieldOfView = 68f; _dining.nearClipPlane = 0.05f; }
            var t = room.transform;
            var eye = t.TransformPoint(room.FromSource(4.6f, 0.7f, 2.7f));
            _dining.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(t.TransformPoint(room.FromSource(-1.2f, 4.2f, 0.9f)) - eye));
            Shot(_dining, "dining", "d");
            _frames++;
        }

        private static void Shot(Camera cam, string dir, string prefix)
        {
            _rt ??= new RenderTexture(W, H, 24);
            _tex ??= new Texture2D(W, H, TextureFormat.RGB24, false);
            var keep = cam.targetTexture;
            cam.targetTexture = _rt;
            cam.Render();
            cam.targetTexture = keep;
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(_out, dir, $"{prefix}{_frames:0000}.png"), _tex.EncodeToPNG());
        }

        private static void Finish(string fail)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(ActiveKey, false);
            bool worked = Lines.Any(l => l.Contains("[rekt-work] at the pass")), served = Lines.Any(l => l.Contains("[rekt-customers] order:"));
            int nodsGot = Lines.Count(l => l.Contains("[nod-route] collected "));
            var sorted = Dts.OrderBy(x => x).ToList();
            var receipt = new JObject
            {
                ["fail"] = fail, ["frames"] = _frames, ["every_s"] = Every,
                ["shift_frames"] = Dts.Count,
                ["editor_frame_ms_p50"] = sorted.Count > 0 ? Math.Round(sorted[sorted.Count / 2] * 1000, 1) : 0,
                ["editor_frame_ms_p95"] = sorted.Count > 0 ? Math.Round(sorted[(int)(sorted.Count * 0.95)] * 1000, 1) : 0,
                ["batches_p50"] = Batches.Count > 0 ? Batches.OrderBy(x => x).ElementAt(Batches.Count / 2) : 0,
                ["batches_max"] = Batches.Count > 0 ? Batches.Max() : 0,
                ["worked_a_full_round"] = worked, ["customer_ordered"] = served, ["nods_collected"] = nodsGot,
                ["nod_draws"] = NodField.Current != null ? NodField.Current.LastDraws : -1,
                ["log"] = new JArray(Lines),
            };
            File.WriteAllText(Path.Combine(_out, "rekt-play-receipt.json"), receipt.ToString());
            bool ok = fail == null && (_nodRoute ? nodsGot >= 2 : worked && served);
            Debug.Log($"CTLIFE [rekt-play] {(ok ? "PASS" : "FAIL " + (fail ?? $"worked {worked} ordered {served}"))}; {_frames} frames");
            EditorApplication.ExitPlaymode();
            if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(ok ? 0 : 1);
        }

        private static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
    }
}
