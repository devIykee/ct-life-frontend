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
using Unity.Collections;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>Native Play-mode component review. No network channel, asset save, or fabricated server traffic.</summary>
    [InitializeOnLoad]
    public static class CafeSeatHitReview
    {
        private const string Key = "CTLife.CafeSeatHitReview.";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static IEnumerator _steps;
        private static CameraRig _rig;
        private static GameObject _root;
        private static readonly List<string> Checks = new List<string>();
        private static readonly List<string> Commands = new List<string>();
        private const string Candidate = "Assets/Art/Candidates/TerminalCafe20261006B3Hits/TerminalCafeB3Hits.prefab";
        private const string Baseline = "Assets/Art/Candidates/TerminalCafe20261006B3Social/TerminalCafeB3Social.prefab";
        private static readonly List<object> Rays = new List<object>();
        [Serializable] private sealed class SavedScene { public string path; public bool isLoaded, isActive; }
        static CafeSeatHitReview()
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
            SessionState.SetString(Key + "output", Argument("cafeHitOutput") ?? "/tmp/ct-cafe-seat-hit-native-20261006");
            SessionState.SetBool(Key + "exit", Argument("cafeHitExit") == "1");
            SessionState.SetInt(Key + "result", 1);
            SessionState.SetString(Key + "runtimeError", "");
            SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 180d).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            SessionState.SetBool(Key + "active", true);
            PhaseIs("startup");
            Debug.Log("CTLIFE [cafe-seat-hit] Waiting for imports and Search database initialization.");
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
                    Checks.Clear(); Commands.Clear(); Rays.Clear();
                    _steps = Review();
                    PhaseIs("running");
                }
                if (Phase == "running" && !_steps.MoveNext()) Stop(null);
            }
            catch (Exception e) { Stop(e); }
        }

        private sealed class Probe
        {
            public string spot, part;
            public Vector3 world, approach;
        }
        private static IEnumerator Review()
        {
            _rig = CameraRig.Ensure();
            _rig.Camera.enabled = false; // Real projection/input code; this fixture performs no image rendering.
            _rig.Camera.aspect = 16f / 9f;
            var probes = new List<Probe>();
            _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Candidate));
            Require(_root != null, "candidate prefab staged");
            var venue = _root.GetComponent<VenueSceneRoot>();
            _rig.ConfigureVenue(venue);
            var focus = new GameObject("Read-only player approach").transform;
            focus.SetParent(_root.transform, false); _rig.Target = focus;
            Physics.SyncTransforms();
            var anchors = _root.GetComponentsInChildren<SpotAnchor>().Where(a => !string.IsNullOrEmpty(a.anim)).OrderBy(a => a.spotId).ToArray();
            Require(anchors.Length == 12, "all twelve claimable immutable anchors retained");
            foreach (var anchor in anchors)
            {
                Require(anchor.approach != null, "authored approach " + anchor.spotId);
                focus.position = anchor.approach.position; _rig.SnapToTarget(); Physics.SyncTransforms();
                var furniture = anchor.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Direction_", StringComparison.Ordinal)).ToArray();
                var chair = furniture.Single(t => t.name == "Direction_office_chair" || t.name == "Direction_home_chair_1");
                var table = furniture.Single(t => t.name == "Direction_home_desk" || t.name == "Direction_home_table_roundsmall");
                Require(chair.GetComponentsInChildren<MeshCollider>().Length > 0 && table.GetComponentsInChildren<MeshCollider>().Length > 0,
                    "actual mesh collider ownership " + anchor.spotId);
                foreach (string part in new[] { "back", "table" })
                {
                    var selectedFurniture = part == "back" ? chair : table;
                    var point = VisibleSurface(selectedFurniture, anchor, part, _rig.Camera);
                    Require(point.HasValue, "visible " + part + " source triangle from gameplay camera " + anchor.spotId);
                    var probe = new Probe { spot = anchor.spotId, part = part, world = point.Value, approach = focus.position };
                    probes.Add(probe);
                    Require(Tap(probe, "candidate", out var hit) == anchor.spotId, "WorldInput.Tap selects " + anchor.spotId + " via " + part);
                    if (part == "back") Require(Vector3.Distance(hit.point, anchor.transform.position) > .9f,
                        "back probe actually exceeds legacy three-dimensional radius " + anchor.spotId);
                }
                yield return null;
            }
            Object.Destroy(_root);
            while (_root != null) yield return null;
            _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Baseline));
            Require(_root != null, "original B3 preserved for negative control");
            venue = _root.GetComponent<VenueSceneRoot>(); _rig.ConfigureVenue(venue);
            focus = new GameObject("Read-only baseline approach").transform; focus.SetParent(_root.transform, false); _rig.Target = focus;
            Physics.SyncTransforms();
            int missedBacks = 0;
            foreach (var probe in probes)
            {
                focus.position = probe.approach; _rig.SnapToTarget(); Physics.SyncTransforms();
                string selected = Tap(probe, "original_b3", out _);
                if (probe.part == "back" && selected != probe.spot) missedBacks++;
                yield return null;
            }
            Require(missedBacks > 0, "original B3 negative control reproduces unselectable visible chair backs");
            Commands.Add("original_b3_back_misses=" + missedBacks + "/12");
        }
        private static Vector3? VisibleSurface(Transform furniture, SpotAnchor anchor, string part, Camera camera)
        {
            var points = new List<Vector3>();
            foreach (var collider in furniture.GetComponentsInChildren<MeshCollider>())
            {
                // Imported source meshes deliberately have Read/Write disabled in players.
                // Editor MeshUtility reads a snapshot without mutating their importer or source bytes.
                using var data = MeshUtility.AcquireReadOnlyMeshData(collider.sharedMesh);
                var mesh = data[0];
                using var vertexData = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp);
                mesh.GetVertices(vertexData);
                var vertices = vertexData.ToArray();
                var allTriangles = new List<int>();
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var descriptor = mesh.GetSubMesh(sub);
                    Require(descriptor.topology == MeshTopology.Triangles, "triangle source mesh topology");
                    using var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp);
                    mesh.GetIndices(indices, sub, true);
                    allTriangles.AddRange(indices.ToArray());
                }
                var triangles = allTriangles.ToArray();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = collider.transform.TransformPoint(vertices[triangles[i]]);
                    var b = collider.transform.TransformPoint(vertices[triangles[i + 1]]);
                    var c = collider.transform.TransformPoint(vertices[triangles[i + 2]]);
                    var point = (a + b + c) / 3f;
                    var n = Vector3.Cross(b-a,c-a).normalized;
                    float y = point.y - anchor.transform.position.y;
                    if (part == "back" ? y < .82f || Vector3.Distance(point,anchor.transform.position) <= .95f : y < .65f || y > .85f || Mathf.Abs(n.y) < .9f) continue;
                    var v = camera.WorldToViewportPoint(point);
                    if (v.z <= camera.nearClipPlane || v.x <= .01f || v.x >= .99f || v.y <= .01f || v.y >= .99f) continue;
                    points.Add(point);
                }
            }
            var ideal = anchor.transform.position + (part == "back" ? new Vector3(0,.95f,-.4f) : new Vector3(0,.75f,.8f));
            foreach (var point in points.OrderBy(p => (p - ideal).sqrMagnitude))
            {
                var screen = camera.WorldToScreenPoint(point); var ray = camera.ScreenPointToRay(screen);
                var first = Physics.RaycastAll(ray,200f,~0,QueryTriggerInteraction.Collide).Where(h => !h.collider.isTrigger).OrderBy(h=>h.distance).FirstOrDefault();
                if (first.collider != null && first.collider.GetComponentInParent<SpotAnchor>() == anchor &&
                    first.collider.transform.IsChildOf(furniture) && Vector3.Distance(first.point,point) < .02f) return point;
            }
            return null;
        }
        private static string Tap(Probe probe, string variant, out RaycastHit first)
        {
            var screen = _rig.Camera.WorldToScreenPoint(probe.world);
            var ray = _rig.Camera.ScreenPointToRay(screen);
            first = Physics.RaycastAll(ray,200f,~0,QueryTriggerInteraction.Collide).Where(h=>!h.collider.isTrigger).OrderBy(h=>h.distance).FirstOrDefault();
            string selected = null; int count = 0;
            void Pick(SpotAnchor spot) { selected = spot.spotId; count++; }
            WorldInput.SpotTapped += Pick;
            try { WorldInput.Tap(new Vector2(screen.x,screen.y)); }
            finally { WorldInput.SpotTapped -= Pick; }
            Require(count <= 1, "one-or-zero selection event per actual tap");
            Rays.Add(new { variant, expected=probe.spot, part=probe.part, selected, eventCount=count,
                world=new[]{probe.world.x,probe.world.y,probe.world.z}, screen=new[]{screen.x,screen.y},
                rayHit=first.collider != null ? first.collider.name : null,
                hitPoint=new[]{first.point.x,first.point.y,first.point.z},
                cameraPosition=new[]{_rig.transform.position.x,_rig.transform.position.y,_rig.transform.position.z},
                pitch=_rig.pitch,yaw=_rig.yaw,distance=_rig.distance,fov=_rig.Camera.fieldOfView,aspect=_rig.Camera.aspect });
            return selected;
        }
        private static void Require(bool condition, string check)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + check);
            Checks.Add(check); Debug.Log("CTLIFE [cafe-seat-hit] PASS " + check);
        }
        private static void Stop(Exception failure)
        {
            if (Phase == "stopping") return;
            if (_root != null) Object.Destroy(_root);
            if (_rig != null) Object.Destroy(_rig.gameObject);
            string output = SessionState.GetString(Key + "output", "/tmp/ct-cafe-seat-hit-native-20261006");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "receipt.json"), JsonConvert.SerializeObject(new
            {
                status = failure == null ? "camera_ray_selection_passed_connected_claims_unverified" : "failed",
                checks = Checks, diagnostics = Commands, rays = Rays, networkChannel = "none; WorldInput selection only",
                limitations = "Actual CameraRig venue settings at16:9 plus native Physics rays and WorldInput.Tap; no UI touch dispatch, nav walking, claim transport, character contact or phone validation. No direct seat claims invoked.",
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
            string output = SessionState.GetString(Key + "output", "/tmp/ct-cafe-seat-hit-native-20261006");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "protected-drift.json"), JsonConvert.SerializeObject(new { count = before.Count, drift }, Formatting.Indented));
            if (drift.Length != 0) SessionState.SetInt(Key + "result", 1);
            int result = SessionState.GetInt(Key + "result", 1);
            SessionState.SetBool(Key + "active", false);
            Debug.Log("CTLIFE [cafe-seat-hit] END exit=" + result + " protectedDrift=" + drift.Length);
            if (SessionState.GetBool(Key + "exit", false)) EditorApplication.Exit(result);
        }
        private static Dictionary<string, string> ProtectedHashes()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var files = new[] { "ProjectSettings", "Packages", "Assets/Scripts/World", "Assets/Resources", "Assets/Art/Candidates/TerminalCafe20261006B3Social", "Assets/Art/Candidates/TerminalCafe20261006B3Hits" }
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
