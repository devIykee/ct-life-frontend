using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    // One-off live offline-bootstrap diagnostic. No saved scene, importer, or runtime source changes.
    [InitializeOnLoad]
    public static class GroundingPlayProbe
    {
        private const string Key = "ctlife.grounding-play-probe";
        private static bool _running;
        private static double _deadline;
        private static readonly JObject Report = new JObject();
        private static AvatarView _view;
        private static GroundingLateSampler _sampler;
        private static Vector3 _left, _right;
        private static int _footFrame = -1;
        private static string Output => Environment.GetEnvironmentVariable("CTLIFE_GROUNDING_PROBE_OUT") ??
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../../ct-life-evidence/audit-20261019/grounding-play.json"));

        static GroundingPlayProbe()
        {
            if (!SessionState.GetBool(Key, false)) return;
            _deadline = EditorApplication.timeSinceStartup + 240;
            EditorApplication.playModeStateChanged += Changed;
            EditorApplication.update += Pump;
            if (EditorApplication.isPlaying) EditorApplication.delayCall += Drive;
        }

        public static void Launch()
        {
            if (!LaunchArgs.Flag("islandDemo")) throw new InvalidOperationException("Pass -ctlife-islandDemo 1; this probe is offline only.");
            SessionState.SetBool(Key, true);
            _deadline = EditorApplication.timeSinceStartup + 240;
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");
            EditorApplication.playModeStateChanged += Changed;
            EditorApplication.update += Pump;
            EditorApplication.EnterPlaymode();
        }

        private static void Changed(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) Drive();
        }

        private static void Pump()
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (EditorApplication.timeSinceStartup > _deadline) { Finish("timeout waiting for actual island/player"); return; }
            if (EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
            else if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
                EditorApplication.EnterPlaymode();
        }

        private static async void Drive()
        {
            if (_running) return;
            _running = true;
            int capture = Time.captureFramerate;
            bool previousIk = CharacterIK.Enabled;
            var previousProbe = CharacterIK.FootProbe;
            try
            {
                Application.runInBackground = true;
                if (!RuntimeMode.IsOfflineDemo) throw new InvalidOperationException("Probe did not enter offline mode.");
                while (VenueSession.Current?.IsActive != true || VenueSession.Current.LocalPlayer == null)
                    await Awaitable.NextFrameAsync();
                var visit = VenueSession.Current;
                var player = visit.LocalPlayer;
                _view = player.GetComponent<AvatarView>();
                Time.captureFramerate = 60;
                _sampler = new GameObject("grounding-late-sampler").AddComponent<GroundingLateSampler>();
                CharacterIK.FootProbe = (view, left, right) =>
                { if (view == _view) { _left = left; _right = right; _footFrame = Time.frameCount; } };
                Report["unity"] = Application.unityVersion;
                Report["venue"] = visit.VenueId;
                Report["island"] = visit.IsIsland;
                Report["offline"] = RuntimeMode.IsOfflineDemo;
                Report["layer8IgnoresInterior17"] = Physics.GetIgnoreLayerCollision(8, 17);
                Report["layer8IgnoresDefault0"] = Physics.GetIgnoreLayerCollision(8, 0);
                Report["animator"] = _view.Animator.name;
                Report["avatar"] = _view.Animator.avatar?.name;
                Report["controller"] = _view.Animator.runtimeAnimatorController?.name;
                Report["characterRegistry"] = CharacterRegistry.Instance?.name;
                Report["defaultPlayer"] = CharacterRegistry.Instance?.defaultPlayer?.name;
                Report["npcCatalog"] = NpcCastCatalog.Instance?.name;
                Report["initial"] = Sample(player);
                SaveReport();
                var phases = new JArray(); Report["phases"] = phases;
                await Phase("production-idle", player, phases);
                CharacterIK.Enabled = false;
                _view.Animator.Rebind(); _view.Animator.Play("loco_idle", 0, 0f);
                await Phase("same-root-without-ik", player, phases);
                CharacterIK.Enabled = true;
                _view.Animator.Rebind(); _view.Animator.Play("loco_idle", 0, 0f);
                await Phase("ik-restored", player, phases);
                Report["renderedGround"] = Surface(player.transform.position);
                Report["venuePosition"] = V(visit.Venue.transform.position);
                Report["venueScale"] = V(visit.Venue.transform.lossyScale);
                Finish(null);
            }
            catch (Exception error) { Debug.LogException(error); Finish(error.ToString()); }
            finally { CharacterIK.Enabled = previousIk; CharacterIK.FootProbe = previousProbe; Time.captureFramerate = capture;
                if (_sampler != null) Object.Destroy(_sampler.gameObject); }
        }

        private static async Task Phase(string label, LocalPlayerController player, JArray phases)
        {
            var samples = new JArray();
            for (int i = 0; i < 90; i++)
            {
                await _sampler.NextLateFrame();
                if (i % 15 == 0 || i == 89) samples.Add(Sample(player));
            }
            phases.Add(new JObject { ["label"] = label, ["samples"] = samples });
            SaveReport();
            ScreenCapture.CaptureScreenshot(Path.ChangeExtension(Output, null) + "-" + label + ".png");
            for (int i = 0; i < 3; i++) await Awaitable.NextFrameAsync();
        }

        private static JObject V(Vector3 p) => new JObject { ["x"] = p.x, ["y"] = p.y, ["z"] = p.z };
        private static JObject Sample(LocalPlayerController player)
        {
            var a = _view.Animator; var body = player.Body; var cc = body.Controller;
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var ik = a.GetComponent<CharacterIK>();
            var hitList = new JArray();
            foreach (var hit in Physics.RaycastAll(player.transform.position + Vector3.up * 2f, Vector3.down, 8f,
                         CharacterBody.Mask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(12))
                hitList.Add(new JObject { ["name"] = hit.collider.name, ["parent"] = hit.collider.transform.parent?.name,
                    ["layer"] = hit.collider.gameObject.layer, ["point"] = V(hit.point), ["normal"] = V(hit.normal),
                    ["top"] = hit.collider.bounds.max.y, ["type"] = hit.collider.GetType().Name });
            var skins = new JArray(); var mesh = new Mesh();
            try
            {
                foreach (var skin in _view.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
                    skin.BakeMesh(mesh); float min = float.PositiveInfinity, max = float.NegativeInfinity; Vector3 low = default;
                    foreach (var vertex in mesh.vertices)
                    {
                        var world = skin.transform.TransformPoint(vertex);
                        if (world.y < min) { min = world.y; low = world; }
                        max = Mathf.Max(max, world.y);
                    }
                    skins.Add(new JObject { ["name"] = skin.name, ["low"] = V(low), ["maxY"] = max,
                        ["rendererBoundsMin"] = V(skin.bounds.min), ["rendererLocal"] = V(skin.transform.localPosition) });
                }
            }
            finally { Object.Destroy(mesh); }
            return new JObject { ["frame"] = Time.frameCount, ["root"] = V(player.transform.position),
                ["modelWorld"] = V(a.transform.position), ["modelLocal"] = V(a.transform.localPosition),
                ["modelScale"] = V(a.transform.lossyScale), ["seatOffset"] = V(_view.SeatOffset),
                ["rootScale"] = V(player.transform.lossyScale), ["hips"] = hips != null ? V(hips.position) : null,
                ["stateHash"] = a.GetCurrentAnimatorStateInfo(0).shortNameHash, ["transition"] = a.IsInTransition(0),
                ["speed"] = a.GetFloat(AnimationLibrary.SpeedParam), ["grounded"] = body.Grounded,
                ["controllerGrounded"] = cc.isGrounded, ["suspended"] = body.Suspended,
                ["controllerCenter"] = V(cc.center), ["controllerHeight"] = cc.height, ["controllerRadius"] = cc.radius,
                ["controllerScale"] = V(cc.transform.lossyScale), ["controllerBoundsMin"] = V(cc.bounds.min),
                ["controllerSkin"] = cc.skinWidth, ["groundAt"] = CharacterBody.GroundAt(player.transform.position, out var y) ? (JToken)new JValue(y) : JValue.CreateNull(),
                ["terrainY"] = IslandStreamer.GroundHeight(player.transform.position.x, player.transform.position.z),
                ["ikEnabled"] = CharacterIK.Enabled, ["preIkFrame"] = _footFrame,
                ["preIkLeft"] = V(_left), ["preIkRight"] = V(_right),
                ["leftIkFloor"] = FootSurface(_left, a.transform.position.y),
                ["rightIkFloor"] = FootSurface(_right, a.transform.position.y),
                ["preIkBody"] = ik != null ? V(ik.BodyPosition) : null,
                ["pelvisOffset"] = ik != null ? (float)typeof(CharacterIK).GetField("_pelvisOffset", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ik) : 0f,
                ["bodySummary"] = player.ReviewSnapshot(), ["floorHits"] = hitList, ["skins"] = skins };
        }

        private static JObject FootSurface(Vector3 goal, float rootY)
        {
            var origin = new Vector3(goal.x, rootY + .45f, goal.z);
            if (!Physics.Raycast(origin, Vector3.down, out var hit, 1.35f, CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore))
                return new JObject { ["origin"] = V(origin), ["hit"] = false };
            return new JObject { ["origin"] = V(origin), ["hit"] = true, ["point"] = V(hit.point),
                ["name"] = hit.collider.name, ["layer"] = hit.collider.gameObject.layer, ["normal"] = V(hit.normal) };
        }

        private static JObject Surface(Vector3 point)
        {
            float y = float.NegativeInfinity; string name = null; int unreadable = 0;
            foreach (var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                var renderer = filter.GetComponent<MeshRenderer>(); var mesh = filter.sharedMesh;
                if (mesh == null || renderer == null || !renderer.enabled || filter.GetComponentInParent<AvatarView>() != null) continue;
                var bounds = renderer.bounds;
                if (point.x < bounds.min.x || point.x > bounds.max.x || point.z < bounds.min.z || point.z > bounds.max.z) continue;
                if (!mesh.isReadable) { unreadable++; continue; }
                var vertices = mesh.vertices; var tris = mesh.triangles;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var a = filter.transform.TransformPoint(vertices[tris[i]]); var b = filter.transform.TransformPoint(vertices[tris[i+1]]);
                    var c = filter.transform.TransformPoint(vertices[tris[i+2]]);
                    if (Mathf.Abs(Vector3.Cross(b-a,c-a).normalized.y) < .7f) continue;
                    float d=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z); if(Mathf.Abs(d)<1e-8f) continue;
                    float u=((b.z-c.z)*(point.x-c.x)+(c.x-b.x)*(point.z-c.z))/d;
                    float v=((c.z-a.z)*(point.x-c.x)+(a.x-c.x)*(point.z-c.z))/d;
                    if(u<0f||v<0f||u+v>1f) continue;
                    float h=u*a.y+v*b.y+(1-u-v)*c.y;
                    if(h>point.y+1f||h<point.y-3f||h<=y) continue;
                    y=h;name=filter.name;
                }
            }
            return new JObject { ["y"] = y, ["mesh"] = name, ["unreadableRelevantMeshes"] = unreadable };
        }

        private static void SaveReport()
        { Directory.CreateDirectory(Path.GetDirectoryName(Output)); File.WriteAllText(Output, Report.ToString()); }

        private static void Finish(string error)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Pump; EditorApplication.playModeStateChanged -= Changed;
            Report["error"] = error; SaveReport();
            Debug.Log("CTLIFE grounding play probe: " + Output + " " + (error == null ? "complete" : "failed"));
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }
    }
    [DefaultExecutionOrder(32000)]
    public sealed class GroundingLateSampler : MonoBehaviour
    {
        private TaskCompletionSource<bool> _pending;
        public Task NextLateFrame()
        {
            if (_pending != null) throw new InvalidOperationException("Only one diagnostic frame request is allowed.");
            _pending = new TaskCompletionSource<bool>();
            return _pending.Task;
        }
        private void LateUpdate()
        {
            var pending = _pending; _pending = null;
            pending?.SetResult(true);
        }
        private void OnDestroy() { _pending?.TrySetCanceled(); _pending = null; }
    }
}
