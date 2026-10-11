using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Batchmode PlayMode capture of real characters in real venues, through the game camera (CameraRig with each venue's
    /// authored framing), the production spawn path (AvatarFactory.Create / CreateNpc with the shipped registry), the
    /// crowd LOD/culling (AvatarLod) and the real LocalPlayerController. Writes PNG stills/strips plus a JSON receipt with
    /// per-character pose metrics (skinned span/height: ~0.3 idle, ~0.9 for a T/bind pose).
    /// Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.CharacterFeelCapture.Launch
    ///       [-ctlife-captureOut /abs/dir] [-ctlife-captureSet p1]
    /// (no -nographics: the camera must render). Exit 0 = every character posed (no T/bind pose).
    /// </summary>
    [InitializeOnLoad]
    public static partial class CharacterFeelCapture
    {
        private const string ActiveKey = "ctlife.characterfeel.capture";
        private static readonly string[] Venues = { "timeline_square", "terminal_cafe", "spaces_studio" };
        private static readonly string[] NpcRoles = { "rekt_guy", "giveaway_guy", "spaces_host" };
        private static readonly string[] PlayerLooks = { "player_m_kol", "player_f_lagos_ankara" };
        private const int Width = 1600, Height = 720;

        private static string _out;
        private static bool _started, _driving;
        private static double _deadline;
        private static readonly JObject Receipt = new JObject();
        private static readonly List<string> Failures = new List<string>();

        static CharacterFeelCapture()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            Debug.Log($"CTLIFE [feel-capture] domain loaded (playing={EditorApplication.isPlaying}, willChange={EditorApplication.isPlayingOrWillChangePlaymode})");
            _out = Arg("-ctlife-captureOut") ?? DefaultOut();
            if (EditorApplication.isPlaying) EditorApplication.delayCall += Drive;
            else
            {
                EditorApplication.playModeStateChanged += OnPlayMode;
                // An asset refresh during the play-mode transition (e.g. a package rewriting a file at startup) reloads
                // the domain and drops the request; ask again once the editor is idle.
                _deadline = EditorApplication.timeSinceStartup + 240;
                EditorApplication.update += ReEnter;
            }
        }

        private static void Watchdog()
        {
            if (EditorApplication.timeSinceStartup < _deadline) return;
            EditorApplication.update -= Watchdog;
            Finish("watchdog: capture did not finish in 600 s");
        }

        private static void ReEnter()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.update -= ReEnter; return; }
            if (EditorApplication.timeSinceStartup > _deadline) { EditorApplication.update -= ReEnter; Finish("play mode never started"); return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= ReEnter;
            Debug.Log("CTLIFE [feel-capture] re-entering play mode");
            EditorApplication.EnterPlaymode();
        }

        private static string DefaultOut() =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../../ct-life-evidence/character-feel"));

        public static void Launch()
        {
            SessionState.SetBool(ActiveKey, true);
            _out = Arg("-ctlife-captureOut") ?? DefaultOut();
            _deadline = EditorApplication.timeSinceStartup + 240;
            EditorApplication.update += WaitForEditor;
        }

        private static void WaitForEditor()
        {
            if (EditorApplication.timeSinceStartup > _deadline) { EditorApplication.update -= WaitForEditor; Finish("editor not ready"); return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || _started) return;
            _started = true;
            EditorApplication.update -= WaitForEditor;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            Debug.Log("CTLIFE [feel-capture] play mode change: " + change);
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Drive();
        }

        private static void Step(string what) => Debug.Log($"CTLIFE [feel-capture] step: {what} (playing={EditorApplication.isPlaying}, t={Time.realtimeSinceStartup:F1})");

        private static async void Drive()
        {
            if (_driving) return;
            _driving = true;
            Step("drive");
            _deadline = EditorApplication.timeSinceStartup + 600;
            EditorApplication.update += Watchdog;
            string fatal = null;
            try
            {
                Directory.CreateDirectory(_out);
                if (!Services.TryGet<ConfigStore>(out var config))
                {
                    config = Services.Register(new ConfigStore());
                    config.LoadLocal();
                }
                var set = Arg("-ctlife-captureSet") ?? "p1";
                if (set == "p1" || set == "all") foreach (var venue in Venues) await CaptureVenue(venue);
                if (set == "p2" || set == "all") await CaptureFeel();
            }
            catch (Exception e) { fatal = e.GetType().Name + ": " + e.Message; Debug.LogException(e); }
            Finish(fatal);
        }

        // ------------------------------------------------------------------ one venue

        private sealed class Actor
        {
            public string label;
            public AvatarView view;
            public NavMeshAgent agent; // NPC walkers (the player walks through LocalPlayerController)
        }

        private static RenderTexture _target;
        private static Camera _pumped;

        /// <summary>
        /// Batchmode has no Game view, so the camera would only render when asked. Render it every editor tick so
        /// renderer visibility, AvatarLod and Animator culling (CullCompletely) behave as on the device.
        /// </summary>
        private static void Pump()
        {
            if (_pumped == null || _target == null || !EditorApplication.isPlaying) return;
            _pumped.targetTexture = _target;
            _pumped.Render();
        }

        private static void StartPump(Camera cam)
        {
            if (_target == null) _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "feel-capture" };
            _pumped = cam;
            cam.targetTexture = _target;
            cam.aspect = (float)Width / Height;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

        private static async Task CaptureVenue(string venueId)
        {
            Step("load " + venueId);
            var loader = new VenueLoader();
            var venue = await loader.Load(venueId);
            Step("loaded " + venueId);
            AvatarLod.Ensure();
            var root = new GameObject("CaptureAvatars").transform;
            var rig = CameraRig.Ensure();
            StartPump(rig.Camera);
            var actors = new List<Actor>();
            try
            {
                var reg = CharacterRegistry.Instance;
                // Spawn: the local player (first player look) at the entrance; others beside it along the camera's right.
                var spawn = venue.EntrancePosition;
                var right = Quaternion.Euler(0, rig.yaw, 0) * Vector3.right;
                var playerLook = LookFor(reg, PlayerLooks[0]);
                var player = AvatarFactory.Create("player:" + PlayerLooks[0], playerLook, false, null, root);
                player.transform.position = Sample(spawn);
                var local = player.gameObject.AddComponent<LocalPlayerController>();
                local.Init(venue, null);
                actors.Add(new Actor { label = "player:" + PlayerLooks[0], view = player });
                var offsets = new[] { -1.1f, 1.1f, -2.2f, 2.2f };
                int slot = 0;
                var second = AvatarFactory.Create("player:" + PlayerLooks[1], LookFor(reg, PlayerLooks[1]), false, null, root);
                actors.Add(Place(second, "player:" + PlayerLooks[1], Sample(spawn + right * offsets[slot++])));
                foreach (var role in NpcRoles)
                {
                    var npc = AvatarFactory.CreateNpc("npc:" + role, "capture-" + role, role, root);
                    actors.Add(Place(npc, "npc:" + role, Sample(spawn + right * offsets[slot++])));
                }
                rig.Target = player.transform;
                rig.SnapToTarget();
                foreach (var a in actors) { a.view.SetSpeed(0f); a.view.SetBase("loco_idle"); }
                foreach (var a in actors) a.view.transform.rotation = Quaternion.LookRotation(-Flat(rig.transform.forward));

                // Idle (first warm textures/shaders: the editor streams them in over the first frames).
                await Seconds(2.5f);
                await Shoot(venueId, "idle", actors, rig, 1);
                await Close(rig, () => Shoot(venueId, "idle-close", actors, rig, 1));

                // Walk across the frame (camera right): player by direct input, others on their NavMesh agents.
                foreach (var a in actors.Skip(1))
                {
                    a.agent = a.view.gameObject.AddComponent<NavMeshAgent>();
                    a.agent.speed = 1.6f; a.agent.radius = 0.3f; a.agent.height = 1.75f; a.agent.acceleration = 8f;
                    a.agent.SetDestination(a.view.transform.position + right * 6f);
                }
                float until = Time.realtimeSinceStartup + 1.6f;
                await WhileWalking(local, actors, until);
                await Shoot(venueId, "walk", actors, rig, 3, () => WhileWalking(local, actors, Time.realtimeSinceStartup + 0.22f));
                local.StopDirectInput();
                foreach (var a in actors.Skip(1)) { if (a.agent != null) { a.agent.ResetPath(); Object.Destroy(a.agent); } a.agent = null; }
                await Seconds(0.3f);

                // Sit: the player claims a seat through the real controller path (offline: presentation of a confirmed seat);
                // the others take the nearest other seats with the seat's own animation.
                var seats = venue.Anchors.Where(s => !string.IsNullOrEmpty(s.anim) && (s.kind == "seat" || s.kind == "terminal" || s.kind == "stage"))
                    .OrderBy(s => s.spotId, StringComparer.Ordinal).ToList();
                if (seats.Count == 0) Failures.Add($"{venueId}: no seat anchors");
                else
                {
                    var hub = seats.OrderByDescending(s => seats.Count(o => (o.transform.position - s.transform.position).sqrMagnitude < 25f)).First();
                    var chosen = seats.OrderBy(s => (s.transform.position - hub.transform.position).sqrMagnitude).Take(actors.Count).ToList();
                    for (int i = 0; i < actors.Count && i < chosen.Count; i++)
                    {
                        var s = chosen[i];
                        string anim = OccupiedSpotAnimation.Resolve(venue, venue.WireSpotId(s.spotId), s.anim) ?? s.anim;
                        if (i == 0)
                        {
                            player.transform.position = Sample(venue.TryGetSpotApproach(s.spotId, out var ap) ? ap : s.transform.position);
                            local.Init(venue, null); // re-bind the agent at the approach, as after walking there
                            local.OnServerSeat(s.spotId, s.anim);
                        }
                        else
                        {
                            actors[i].view.transform.SetPositionAndRotation(s.transform.position, s.transform.rotation);
                            actors[i].view.SetSpeed(0f);
                            actors[i].view.SetBase(anim, s.spotId);
                        }
                        actors[i].label += "@" + s.spotId;
                    }
                    rig.SnapToTarget();
                    await Seconds(1.5f);
                    await Shoot(venueId, "sit", actors, rig, 1);
                    await Close(rig, () => Shoot(venueId, "sit-close", actors, rig, 1));
                }
            }
            finally
            {
                foreach (var a in actors) if (a.view != null) Object.Destroy(a.view.gameObject);
                Object.Destroy(root.gameObject);
                await loader.Unload();
            }
        }

        /// <summary>The same camera pinch-zoomed to its closest allowed distance.</summary>
        private static async Task Close(CameraRig rig, Func<Task> shoot)
        {
            float d = rig.distance;
            rig.distance = rig.minDistance;
            rig.SnapToTarget();
            await Seconds(0.3f);
            await shoot();
            rig.distance = d;
            rig.SnapToTarget();
        }

        private static Actor Place(AvatarView view, string label, Vector3 at)
        {
            view.transform.position = at;
            return new Actor { label = label, view = view };
        }

        private static async Task WhileWalking(LocalPlayerController local, List<Actor> actors, float until)
        {
            while (Time.realtimeSinceStartup < until)
            {
                local.SetMoveInput(new Vector2(1f, 0f) * 0.6f); // walk strength (not run)
                foreach (var a in actors.Skip(1))
                {
                    if (a.agent == null) continue;
                    float v = a.agent.velocity.magnitude;
                    a.view.SetSpeed(v);
                    a.view.SetBase(v > 0.2f ? "loco_walk" : "loco_idle");
                }
                await Frame();
            }
        }

        // ------------------------------------------------------------------ capture

        private static async Task Shoot(string venueId, string behaviour, List<Actor> actors, CameraRig rig, int frames, Func<Task> between = null)
        {
            var shots = new List<Texture2D>();
            var metrics = new JArray();
            for (int f = 0; f < frames; f++)
            {
                if (f > 0 && between != null) await between();
                await Frame();
                shots.Add(Render(rig.Camera));
                foreach (var a in actors)
                {
                    var m = Metrics(a.view);
                    m["actor"] = a.label; m["frame"] = f;
                    metrics.Add(m);
                    if (f == 0 && (bool)m["tpose"]) Failures.Add($"{venueId}/{behaviour}/{a.label}: T/bind pose (span {m["span"]})");
                }
            }
            var file = $"{venueId}-{behaviour}{(frames > 1 ? "-strip" : "")}.png";
            var strip = Stitch(shots);
            File.WriteAllBytes(Path.Combine(_out, file), strip.EncodeToPNG());
            Object.Destroy(strip);
            foreach (var s in shots) Object.Destroy(s);
            Receipt[$"{venueId}/{behaviour}"] = new JObject { ["file"] = file, ["actors"] = metrics };
            Debug.Log($"CTLIFE [feel-capture] {venueId}/{behaviour} -> {file}");
        }

        private static JObject Metrics(AvatarView v)
        {
            var o = new JObject();
            var a = v.Animator;
            o["avatar"] = a != null && a.avatar != null ? $"{a.avatar.name} valid={a.avatar.isValid} human={a.avatar.isHuman}" : "none";
            o["controller"] = a != null && a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "none";
            o["animatorEnabled"] = a != null && a.enabled;
            o["culling"] = a != null ? a.cullingMode.ToString() : "-";
            o["lodTier"] = v.LodTier;
            string state = "-";
            if (a != null && a.runtimeAnimatorController != null && a.isActiveAndEnabled)
            {
                var info = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : a.GetCurrentAnimatorStateInfo(0);
                foreach (var id in new[] { "loco_idle", "loco_walk", "loco_run", "act_sit", "act_type", "act_lean_back", "act_host_mic", "act_listen_nod",
                             CafeSeatMotionProfile.SocialState, CafeSeatMotionProfile.TypingState })
                    if (info.IsName(id)) { state = id; break; }
            }
            o["state"] = state;
            float span = Span(v.Body);
            o["span"] = Math.Round(span, 3);
            o["tpose"] = float.IsNaN(span) || span > CastValidation.TPoseSpanRatio;
            return o;
        }

        /// <summary>Widest horizontal extent / height of the visible LOD's skinned surface (bind T-pose ≈ 0.9).</summary>
        private static float Span(SkinnedMeshRenderer fallback)
        {
            if (fallback == null) return float.NaN;
            var owner = fallback.GetComponentInParent<AvatarView>();
            var lodGroup = owner != null ? owner.GetComponentInChildren<LODGroup>() : null;
            SkinnedMeshRenderer body = fallback;
            if (lodGroup != null)
            {
                var lods = lodGroup.GetLODs();
                var visible = lods.SelectMany(l => l.renderers).OfType<SkinnedMeshRenderer>().FirstOrDefault(r => r.isVisible);
                if (visible != null) body = visible;
            }
            var mesh = new Mesh();
            try
            {
                body.BakeMesh(mesh, true);
                var m = body.transform.localToWorldMatrix;
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                foreach (var p in mesh.vertices) { var w = m.MultiplyPoint3x4(p); min = Vector3.Min(min, w); max = Vector3.Max(max, w); }
                var size = max - min;
                return size.y > 0.01f ? Mathf.Max(size.x, size.z) / size.y : float.NaN;
            }
            finally { Object.Destroy(mesh); }
        }

        private static Texture2D Render(Camera cam)
        {
            cam.targetTexture = _target;
            cam.Render();
            var active = RenderTexture.active;
            RenderTexture.active = _target;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            return tex;
        }

        private static Texture2D Stitch(List<Texture2D> shots)
        {
            if (shots.Count == 1) { var copy = new Texture2D(Width, Height, TextureFormat.RGB24, false); copy.SetPixels(shots[0].GetPixels()); copy.Apply(); return copy; }
            int w = Width / 2, h = Height / 2; // half-size frames side by side
            var strip = new Texture2D(w * shots.Count, h, TextureFormat.RGB24, false);
            for (int i = 0; i < shots.Count; i++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        strip.SetPixel(i * w + x, y, shots[i].GetPixel(x * 2, y * 2));
            strip.Apply();
            return strip;
        }

        // ------------------------------------------------------------------ helpers

        private static string[] LookFor(CharacterRegistry reg, string lookId)
        {
            var e = reg.looks.FirstOrDefault(x => x != null && x.key != null && x.key.Contains("|head_" + lookId + "|"));
            return e != null ? e.key.Split('|') : null;
        }

        private static Vector3 Sample(Vector3 p) => NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas) ? hit.position : p;
        private static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
        // Task.Delay continuations run on the editor-pumped UnitySynchronizationContext; Awaitable timers did not resume in
        // batchmode PlayMode here.
        private static Task Seconds(float s) => Task.Delay((int)(s * 1000));
        private static Task Frame() => Task.Delay(16);

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        private static void Finish(string fatal)
        {
            EditorApplication.update -= Pump;
            SessionState.SetBool(ActiveKey, false);
            Receipt["fatal"] = fatal;
            Receipt["failures"] = new JArray(Failures);
            bool pass = fatal == null && Failures.Count == 0;
            Receipt["pass"] = pass;
            try { Directory.CreateDirectory(_out ?? DefaultOut()); File.WriteAllText(Path.Combine(_out ?? DefaultOut(), (Arg("-ctlife-captureSet") ?? "p1") + "-receipt.json"), Receipt.ToString()); }
            catch (Exception e) { Debug.LogException(e); }
            Debug.Log($"CTLIFE [feel-capture] {(pass ? "PASS" : "FAIL")} fatal={fatal} failures={Failures.Count}");
            if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
