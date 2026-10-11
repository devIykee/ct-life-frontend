using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>Native Play-mode component review. No network channel, asset save, or fabricated server traffic.</summary>
    [InitializeOnLoad]
    public static class SeatApproachReview
    {
        private const string Key = "CTLife.SeatApproachReview.";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static IEnumerator _steps;
        private static NavMeshData _data;
        private static NavMeshDataInstance _navigation;
        private static GameObject _root;
        private static readonly List<string> Checks = new List<string>();
        private static readonly List<string> Commands = new List<string>();
        private static readonly Vector3 Origin = new Vector3(8000f, 0f, 8000f);
        [Serializable] private sealed class SavedScene { public string path; public bool isLoaded, isActive; }
        static SeatApproachReview()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (Active && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                    SessionState.SetString(Key + "runtimeError", message);
            };
        }
        private static bool Active => SessionState.GetBool(Key + "active", false);
        private static string Phase => SessionState.GetString(Key + "phase", "");
        private static void PhaseIs(string value) => SessionState.SetString(Key + "phase", value);

        public static void Launch()
        {
            if (Active || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Seat review requires idle Edit mode.");
            RequireCleanScenes();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            SessionState.SetString(Key + "scenes", JsonConvert.SerializeObject(setup.Select(s => new SavedScene
                { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive })));
            SessionState.SetString(Key + "protection", JsonConvert.SerializeObject(ProtectedHashes()));
            SessionState.SetString(Key + "output", Argument("seatReviewOutput") ?? "/tmp/ct-seat-approach-native-20261006");
            SessionState.SetBool(Key + "exit", Argument("seatReviewExit") == "1");
            SessionState.SetInt(Key + "result", 1);
            SessionState.SetString(Key + "runtimeError", "");
            SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 180d).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            SessionState.SetBool(Key + "active", true);
            PhaseIs("startup");
            Debug.Log("CTLIFE [seat-approach] Waiting for imports and Search database initialization.");
        }

        private static void Tick()
        {
            if (!Active) return;
            try
            {
                if (Phase == "stopping")
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
                    return;
                }
                if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + "deadline", "0"), System.Globalization.CultureInfo.InvariantCulture))
                    throw new TimeoutException("Seat review timed out in " + Phase);
                var runtimeError = SessionState.GetString(Key + "runtimeError", "");
                if (!string.IsNullOrEmpty(runtimeError)) throw new InvalidOperationException("Native runtime error: " + runtimeError);
                if (Phase == "startup")
                {
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating || !SearchService.EnumerateDatabases().Any()) return;
                    RequireCleanScenes();
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    PhaseIs("entering");
                    EditorApplication.EnterPlaymode();
                    return;
                }
                if (!EditorApplication.isPlaying) return;
                if (Phase == "entering")
                {
                    // This preference is runtime-only and is discarded when the isolated Play session exits.
                    SessionState.SetBool(Key + "originalRunInBackground", Application.runInBackground);
                    Application.runInBackground = true;
                    if (EditorApplication.isPaused) throw new InvalidOperationException("Play mode is paused; review did not unpause it.");
                    Checks.Clear(); Commands.Clear();
                    _steps = Review();
                    PhaseIs("running");
                }
                if (Phase == "running" && !_steps.MoveNext()) Stop(null);
            }
            catch (Exception e) { Stop(e); }
        }

        private static IEnumerator Review()
        {
            _root = new GameObject("SeatApproachReview_RuntimeOnly");
            _root.transform.position = Origin;
            var venue = _root.AddComponent<VenueSceneRoot>();
            venue.venueId = "seat_review";
            var seat = Anchor("chair", new Vector3(4.8f, 0f, 0f));
            seat.anim = "act_sit";
            seat.approach = new GameObject("chair_approach").transform;
            seat.approach.SetParent(_root.transform, false);
            seat.approach.localPosition = new Vector3(3f, 0f, 0f);
            var island = Anchor("island", new Vector3(12f, 0f, 0f));
            island.anim = "act_sit";
            var fallback = Anchor("legacy", new Vector3(-2f, 0f, 0f));
            var defs = new VenueSpots();
            foreach (var anchor in new[] { seat, island, fallback })
            {
                var def = new SpotDef { id = anchor.spotId, wireId = "seat_review." + anchor.spotId, kind = "seat", anim = "act_sit" };
                defs.spots[def.id] = def; defs.spots[def.wireId] = def;
            }
            venue.Init(defs);
            Require(venue.TryGetSpotApproach("seat_review.chair", out var approach) && approach == seat.approach.position, "qualified ID resolves explicit approach");
            Require(venue.TryGetSpotApproach("legacy", out var oldPoint) && oldPoint == fallback.transform.position, "legacy anchor fallback is unchanged");
            var sources = new List<NavMeshBuildSource>();
            foreach (float x in new[] { 0f, 12f })
            {
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(8f, .2f, 8f),
                    transform = Matrix4x4.TRS(new Vector3(x, -.1f, 0f), Quaternion.identity, Vector3.one), area = 0 });
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "floor"; floor.transform.SetParent(_root.transform, false);
                floor.transform.localPosition = new Vector3(x, -.1f, 0f); floor.transform.localScale = new Vector3(8f, .2f, 8f);
            }
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = .3f; settings.agentHeight = 1.75f;
            settings.overrideVoxelSize = true; settings.voxelSize = .05f;
            _data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(new Vector3(6f, 0f, 0f), new Vector3(24f, 4f, 12f)), Vector3.zero, Quaternion.identity);
            if (_data == null) throw new InvalidOperationException("Native review navigation build returned null.");
            _navigation = NavMesh.AddNavMeshData(_data, Origin, Quaternion.identity);
            var actor = new GameObject("actual_runtime_controller");
            actor.transform.SetParent(_root.transform, false);
            var view = actor.AddComponent<AvatarView>();
            view.Init(null, null, null, null, 1.75f);
            var player = actor.AddComponent<LocalPlayerController>();
            // Editor asmdef deliberately does not reference CTLife.Net. Invoke the real public initializer
            // with a null transport without widening production assembly dependencies for a review.
            typeof(LocalPlayerController).GetMethod("Init").Invoke(player, new object[] { venue, null });
            player.ReviewCommandObserved += Commands.Add;
            var agent = actor.GetComponent<NavMeshAgent>();
            int frame = Time.frameCount + 3;
            while (Time.frameCount < frame) yield return null;
            Require(agent.isOnNavMesh, "actual agent is on native navigation");
            Require(!NavMesh.SamplePosition(seat.transform.position, out _, .35f, NavMesh.AllAreas), "authoritative chair point is deliberately off navigation");
            var path = new NavMeshPath();
            Require(agent.CalculatePath(island.transform.position, path) && path.status == NavMeshPathStatus.PathPartial, "native disconnected destination produces partial path");
            int sits = SitCount();
            Require(!player.GoSit("island"), "partial approach is rejected before walking");
            frame = Time.frameCount + 4;
            while (Time.frameCount < frame) yield return null;
            Require(SitCount() == sits, "partial path dispatched no sit intent");

            SetPrivate(player, "_requestedSpot", "legacy");
            player.CancelMovement(); // A was cancelled; its later rejection has no new request to cancel.
            Require(player.GoSit("chair"), "complete explicit approach accepted");
            player.OnSeatRejected();
            Require(PrivateValue<string>(player, "_pendingSpot") == "chair", "late rejection A preserves newer undispatched approach B");
            double until = EditorApplication.timeSinceStartup + 12d;
            while (SitCount() == sits && EditorApplication.timeSinceStartup < until) yield return null;
            Require(SitCount() == sits + 1 && Commands.Last(c => c.StartsWith("sit:")) == "sit:seat_review.chair", "arrival emits one unchanged wire ID");
            Require(string.IsNullOrEmpty(player.SeatedSpot) && FlatDistance(actor.transform.position, approach) < .15f, "offline arrival stays standing and does not fabricate ownership");
            var walkingBeforeSeat = PrivateValue<Vector3>(player, "_lastSentPos");
            player.OnServerSeat("seat_review.chair", "act_sit");
            Require(!agent.enabled && Vector3.Distance(actor.transform.position, seat.transform.position) < .0001f, "server confirmation applies exact off-mesh anchor with agent disabled");
            int moves = Commands.Count(c => c == "move");
            frame = Time.frameCount + 16;
            while (Time.frameCount < frame) yield return null;
            Require(Commands.Count(c => c == "move") == moves && PrivateValue<Vector3>(player, "_lastSentPos") == walkingBeforeSeat, "seated frames publish no move intent and retain last walking transmission");
            player.OnServerSeat("chair", "act_type");
            Require(PrivateValue<string>(view, "_baseAnim") == "act_type", "same-seat server animation change reconciles");
            player.SnapTo(approach, .2f);
            Require(Vector3.Distance(actor.transform.position, seat.transform.position) < .0001f && !agent.enabled, "pre-seat move correction does not displace seated visual pose");
            int stands = Commands.Count(c => c == "stand");
            player.OnServerSeat("", "loco_idle");
            Require(string.IsNullOrEmpty(player.SeatedSpot) && agent.enabled && agent.isOnNavMesh && FlatDistance(actor.transform.position, approach) < .15f, "empty authoritative spot restores standing navigation");
            Require(Commands.Count(c => c == "stand") == stands, "server stand sends no stand echo");

            player.OnServerSeat("chair", "act_sit");
            player.SetMoveInput(Vector2.up);
            Require(string.IsNullOrEmpty(player.SeatedSpot) && agent.enabled && agent.isOnNavMesh && player.DirectInputActive, "direct input stands before the disabled-agent guard");
            Require(Commands.Count(c => c == "stand") == stands + 1, "local stand emits one stand intent");
            player.OnServerSeat("seat_review.chair", "act_sit");
            player.OnServerSeat("chair", "act_sit");
            player.OnServerSeat("", "loco_idle");
            player.OnServerSeat("seat_review.chair", "act_sit");
            Require(string.IsNullOrEmpty(player.SeatedSpot) && agent.enabled, "repeated released-seat echoes including after empty state do not reseat");
            player.StopDirectInput();
            sits = SitCount();
            Require(player.GoSit("chair"), "explicit retry can approach released seat");
            until = EditorApplication.timeSinceStartup + 6d;
            while (SitCount() == sits && EditorApplication.timeSinceStartup < until) yield return null;
            Require(SitCount() == sits + 1, "new explicit claim clears only its released-seat tombstone");
            player.OnServerSeat("chair", "act_sit");
            Require(!agent.enabled && !string.IsNullOrEmpty(player.SeatedSpot), "explicit retry can confirm again");
            player.OnServerSeat(null, null);

            // The transport is deliberately null. Inject only the private 'request dispatched' marker to
            // exercise cancellation/late-ack state without inventing a joined server or sending any packet.
            SetPrivate(player, "_requestedSpot", "legacy");
            player.CancelMovement();
            player.OnServerSeat("seat_review.legacy", "act_sit");
            Require(string.IsNullOrEmpty(player.SeatedSpot), "cancelled dispatched request ignores its late acknowledgement");
            SetPrivate(player, "_requestedSpot", "island");
            player.OnSeatRejected();
            player.OnServerSeat("seat_review.island", "act_sit");
            Require(string.IsNullOrEmpty(player.SeatedSpot), "rejected claim does not enter seated visual mode");

            // Public input has already exercised released-seat filtering; use a fresh confirmed legacy
            // point for the missing-navigation failure case so there is no test-only state reset.
            Require(player.GoSit("legacy"), "fresh legacy claim remains usable");
            sits = SitCount(); until = EditorApplication.timeSinceStartup + 12d;
            while (SitCount() == sits && EditorApplication.timeSinceStartup < until) yield return null;
            Require(SitCount() == sits + 1, "legacy fallback arrives through a complete path");
            player.OnServerSeat("legacy", "act_sit");
            Require(!agent.enabled && Vector3.Distance(actor.transform.position, fallback.transform.position) < .0001f,
                "legacy nonzero arrival confirms at exact seat anchor");
            player.OnServerSeat("", "loco_idle");
            sits = SitCount();
            Require(player.GoSit("legacy"), "legacy already-at-approach retry accepted");
            until = EditorApplication.timeSinceStartup + 4d;
            while (SitCount() == sits && EditorApplication.timeSinceStartup < until) yield return null;
            Require(SitCount() == sits + 1, "legacy retry emits exactly one further claim");
            player.OnServerSeat("legacy", "act_sit");
            var exact = actor.transform.position;
            _navigation.Remove();
            player.StandIfSeated();
            Require(!agent.enabled && actor.transform.position == exact && !string.IsNullOrEmpty(player.SeatedSpot), "missing navigation retains exact seat instead of an invalid standing warp");
        }

        private static SpotAnchor Anchor(string id, Vector3 p)
        {
            var go = new GameObject(id); go.transform.SetParent(_root.transform, false); go.transform.localPosition = p;
            var a = go.AddComponent<SpotAnchor>(); a.spotId = id; a.kind = "seat"; return a;
        }
        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
        private static int SitCount() => Commands.Count(c => c.StartsWith("sit:", StringComparison.Ordinal));
        private static T PrivateValue<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
        private static void SetPrivate(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
        private static void Require(bool condition, string check)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + check);
            Checks.Add(check); Debug.Log("CTLIFE [seat-approach] PASS " + check);
        }
        private static void Stop(Exception failure)
        {
            if (Phase == "stopping") return;
            if (_navigation.valid) _navigation.Remove();
            if (_root != null) Object.Destroy(_root);
            if (_data != null) Object.Destroy(_data);
            string output = SessionState.GetString(Key + "output", "/tmp/ct-seat-approach-native-20261006");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "receipt.json"), JsonConvert.SerializeObject(new
            {
                status = failure == null ? "component_assertions_passed_connected_seating_unverified" : "failed",
                checks = Checks, observedEditorIntents = Commands, networkChannel = "null; no server calls",
                limitations = "No deployed server, actual furniture contact, or phone-device validation. Pending-request cases inject the private dispatched marker.",
                failure = failure?.ToString()
            }, Formatting.Indented));
            SessionState.SetInt(Key + "result", failure == null ? 0 : 1);
            if (failure != null) Debug.LogException(failure);
            PhaseIs("stopping");
            if (EditorApplication.isPlaying)
                Application.runInBackground = SessionState.GetBool(Key + "originalRunInBackground", false);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
            else Finish();
        }
        private static void Finish()
        {
            var scenes = JsonConvert.DeserializeObject<SavedScene[]>(SessionState.GetString(Key + "scenes", "[]"));
            if (scenes.Any(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(scenes.Select(s => new SceneSetup { path = s.path, isLoaded = s.isLoaded, isActive = s.isActive }).ToArray());
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var before = JsonConvert.DeserializeObject<Dictionary<string, string>>(SessionState.GetString(Key + "protection", "{}"));
            var after = ProtectedHashes();
            var drift = before.Keys.Union(after.Keys).Where(k => !before.TryGetValue(k, out var b) || !after.TryGetValue(k, out var a) || a != b).ToArray();
            string output = SessionState.GetString(Key + "output", "/tmp/ct-seat-approach-native-20261006");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "protected-drift.json"), JsonConvert.SerializeObject(new { count = before.Count, drift }, Formatting.Indented));
            if (drift.Length != 0) SessionState.SetInt(Key + "result", 1);
            int result = SessionState.GetInt(Key + "result", 1);
            SessionState.SetBool(Key + "active", false);
            Debug.Log("CTLIFE [seat-approach] END exit=" + result + " protectedDrift=" + drift.Length);
            if (SessionState.GetBool(Key + "exit", false)) EditorApplication.Exit(result);
        }
        private static Dictionary<string, string> ProtectedHashes()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var files = new[] { "ProjectSettings", "Packages", "Assets/Scripts/World", "Assets/Resources" }
                .SelectMany(p => Directory.GetFiles(Path.Combine(project, p), "*", SearchOption.AllDirectories));
            using var sha = SHA256.Create();
            return files.ToDictionary(p => p, p => BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p))).Replace("-", "").ToLowerInvariant());
        }
        private static void RequireCleanScenes()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.isDirty || (string.IsNullOrEmpty(scene.path) && scene.rootCount != 0))
                    throw new InvalidOperationException("Existing unsaved scene must be preserved; review did not replace it.");
            }
        }
        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-ctlife-" + name) return args[i + 1];
            return null;
        }
    }
}
