#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections;
using System.IO;
using CTLife.Audio;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

namespace CTLife.App.Review
{
    /// <summary>Actual game views with read-only sample data for repeatable presentation QA.</summary>
    public sealed class FrontendReviewController : MonoBehaviour
    {
        private ToastLayer _toasts;
        private UiRoot _ui;
        private bool _disposed;
        private Hud _hud;
        private PhoneShell _phone;
        private PracticeJobsApp _practiceJobs;
        private UiScreen _screen;
        private VisualElement _reviewBar;
        private bool _travelling;
        private bool _inputTrace;
#if UNITY_EDITOR
        private bool _reviewRuntimeConfigured, _originalRunInBackground;
#endif

        private async void Start()
        {
            try
            {
#if UNITY_EDITOR
                _originalRunInBackground = Application.runInBackground;
                _reviewRuntimeConfigured = true;
                Application.runInBackground = true;
                if (LaunchArgs.TryGet("reviewVenuePrefab", out var candidatePath))
                {
                    if (!candidatePath.StartsWith("Assets/Art/Candidates/", StringComparison.Ordinal) || candidatePath.Contains(".."))
                        throw new ArgumentException("Review venue must be an isolated Assets/Art/Candidates prefab.");
                    var candidateObject = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(candidatePath);
                    var candidate = candidateObject != null ? candidateObject.GetComponent<VenueSceneRoot>() : null;
                    if (candidate == null) throw new InvalidOperationException("Review venue prefab is missing its VenueSceneRoot.");
                    var lightmaps = candidate.GetComponent<VenueLightmaps>();
                    if (lightmaps == null || lightmaps.colors.Length == 0 || lightmaps.bindings.Length == 0)
                        throw new InvalidOperationException("Review venue must be baked before visual acceptance.");
                    VenueLoader.ReviewPrefabResolver = id => id == candidate.venueId ? candidate : null;
                    Log.Info("review", "Isolated baked venue override: " + candidatePath);
                }
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
                if (LaunchArgs.Flag("reviewLandscape")) Screen.orientation = ScreenOrientation.LandscapeLeft;
#endif
                await Services.Get<PlayerProfile>().Refresh();
                if (_disposed) return;
                var ui = UiRoot.Instance;
                _ui = ui;
                _inputTrace = LaunchArgs.Flag("reviewInputTrace");
                if (_inputTrace)
                {
                    ui.Root.RegisterCallback<PointerDownEvent>(TracePointerDown, TrickleDown.TrickleDown);
                    ui.Root.RegisterCallback<PointerUpEvent>(TracePointerUp, TrickleDown.TrickleDown);
                }
                _toasts = new ToastLayer(ui.ToastLayer);
                _phone = new PhoneShell(ui.PhoneLayer);
                _practiceJobs = new PracticeJobsApp(destroyCancellationToken);
                PhoneCatalog.Register(_phone, id => _ = Travel(id), ReadOnly, _practiceJobs);
                _hud = new Hud { OpenPhone = () => OpenReviewApp(null), OpenMap = () => OpenReviewApp("map"), OpenHome = () => ShowView("dashboard") };
                ui.HudLayer.Add(_hud.Element);
                await Travel(LaunchArgs.Get("venue", "timeline_square"));
                if (_disposed) return;
                AddReviewBar();
#if UNITY_EDITOR
                // Native gait captures must also wait for the real shaders, not compiler placeholders.
                if (LaunchArgs.Flag("reviewChecks"))
                {
                    float deadline = Time.realtimeSinceStartup + 180f;
                    int stable = 0;
                    while (stable < 3)
                    {
                        await Awaitable.EndOfFrameAsync();
                        stable = UnityEditor.ShaderUtil.anythingCompiling || UnityEditor.EditorApplication.isUpdating ? 0 : stable + 1;
                        if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Shader warm-up before movement review did not settle.");
                    }
                }
#endif
#if UNITY_EDITOR
                if (LaunchArgs.Flag("reviewPrivateHomeChecks"))
                {
                    if (!LaunchArgs.TryGet("reviewCapture", out var privateHomeDirectory))
                        throw new ArgumentException("Private Home review requires reviewCapture.");
                    _phone.Close(); _hud.Show(false);
                    try { await HomePrivateEntryReview.Run(privateHomeDirectory, destroyCancellationToken); }
                    finally { if (!_disposed) _hud.Show(true); }
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Flag("reviewVenueLifetimeChecks"))
                {
                    if (!LaunchArgs.TryGet("reviewCapture", out var venueLifetimeDirectory))
                        throw new ArgumentException("Venue lifetime review requires reviewCapture.");
                    await VenueLifetimeReview.Run(venueLifetimeDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
#endif
                if (LaunchArgs.Flag("reviewGameplayGenerationChecks"))
                {
                    if (!LaunchArgs.TryGet("reviewCapture", out var generationDirectory))
                        throw new ArgumentException("Gameplay generation review requires reviewCapture.");
                    await GameplayGenerationReview.Run(generationDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Flag("reviewFlowOwnershipChecks"))
                {
                    if (!LaunchArgs.TryGet("reviewCapture", out var flowDirectory))
                        throw new ArgumentException("Flow ownership review requires reviewCapture.");
                    await FlowOwnershipReview.Run(flowDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Flag("reviewJobsCatalogChecks"))
                {
                    if (!LaunchArgs.TryGet("reviewCapture", out var jobsDirectory))
                        throw new ArgumentException("Jobs catalog review requires reviewCapture.");
                    await JobsCatalogPresentationReview.Run(_phone, jobsDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Flag("reviewHomeChecks"))
                {
                    if (!LaunchArgs.TryGet("reviewCapture", out var homeDirectory))
                        throw new ArgumentException("Home review requires reviewCapture.");
                    await HomePresentationReview.Run(_phone, homeDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Flag("reviewPracticeJobsChecks"))
                {
                    await PracticeJobsPresentationReview.Run(_phone, _practiceJobs, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Flag("reviewChecks")) await CheckMovementAndTravel();
                if (LaunchArgs.Flag("reviewCastChecks")) await NpcCastReview.Run();
                if (LaunchArgs.Flag("reviewNpcChecks")) await NpcNavigationReview.Run();
                if (LaunchArgs.Flag("reviewAudioChecks")) await AudioPresentationReview.Run();
                if (LaunchArgs.Flag("reviewIncidentChecks")) Log.Info("review-check", IncidentPresentationChecks.Run());
#if UNITY_EDITOR
                if (LaunchArgs.TryGet("reviewVoiceNativeTokens", out var voiceTokenPath))
                {
                    if (!LaunchArgs.TryGet("reviewCapture", out var voiceNativeDirectory))
                        throw new ArgumentException("Voice native review requires reviewCapture.");
                    await VoiceNativeReview.Run(voiceTokenPath, voiceNativeDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.TryGet("reviewSpacesNativeTokens", out var spacesTokenPath))
                {
                    if (!LaunchArgs.TryGet("reviewCapture", out var spacesNativeDirectory))
                        throw new ArgumentException("Spaces native review requires reviewCapture.");
                    await SpacesNativeReview.Run(spacesTokenPath, spacesNativeDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
#endif
                if (LaunchArgs.Flag("reviewEarningsRecovery"))
                    Log.Info("earnings-review", await EarningsPresentationReview.CheckPracticeReplayFailure(destroyCancellationToken));
                if (LaunchArgs.Get("reviewScreen", "city") == "overlay_lifetime")
                {
                    ShowView("city");
                    if (!LaunchArgs.TryGet("reviewCapture", out var overlayDirectory))
                        throw new ArgumentException("Overlay lifetime review requires reviewCapture.");
                    await OverlayOwnershipReview.Run(_phone, overlayDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Get("reviewScreen", "city") == "perps")
                {
                    ShowView("city");
                    if (!LaunchArgs.TryGet("reviewCapture", out var perpsDirectory))
                        throw new ArgumentException("Perps review requires reviewCapture.");
                    await PerpsPresentationReview.Run(_phone, perpsDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Get("reviewScreen", "city") == "spaces_ui")
                {
                    ShowView("city");
                    if (!LaunchArgs.TryGet("reviewCapture", out var spacesUiDirectory))
                        throw new ArgumentException("Spaces UI review requires reviewCapture.");
                    await SpacesPresentationReview.Run(_phone, spacesUiDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Get("reviewScreen", "city") == "clip_cut")
                {
                    ShowView("city");
                    if (!LaunchArgs.TryGet("reviewCapture", out var clipDirectory))
                        throw new ArgumentException("Clip Cut review requires reviewCapture.");
                    await ClipCutPresentationReview.Run(_phone, _practiceJobs, clipDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Get("reviewScreen", "city") == "runway_plan")
                {
                    ShowView("city");
                    if (!LaunchArgs.TryGet("reviewCapture", out var runwayDirectory))
                        throw new ArgumentException("Runway Plan review requires reviewCapture.");
                    await RunwayPlanPresentationReview.Run(_phone, _practiceJobs, runwayDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Get("reviewScreen", "city") == "live_cues")
                {
                    ShowView("city");
                    if (!LaunchArgs.TryGet("reviewCapture", out var cuesDirectory))
                        throw new ArgumentException("Live Cues review requires reviewCapture.");
                    await LiveCuesPresentationReview.Run(_phone, _practiceJobs, cuesDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Get("reviewScreen", "city") == "receipt_match")
                {
                    ShowView("city");
                    if (!LaunchArgs.TryGet("reviewCapture", out var receiptDirectory))
                        throw new ArgumentException("Receipt Match review requires reviewCapture.");
                    await ReceiptMatchPresentationReview.Run(_phone, _practiceJobs, receiptDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Get("reviewScreen", "city") == "role_lock")
                {
                    ShowView("city");
                    if (!LaunchArgs.TryGet("reviewCapture", out var roleDirectory))
                        throw new ArgumentException("Role Lock review requires reviewCapture.");
                    await RoleLockPresentationReview.Run(_phone, _practiceJobs, roleDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Get("reviewScreen", "city") == "raid_router")
                {
                    ShowView("city");
                    if (!LaunchArgs.TryGet("reviewCapture", out var routerDirectory))
                        throw new ArgumentException("Raid Router review requires reviewCapture.");
                    await RaidRouterPresentationReview.Run(routerDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Get("reviewScreen", "city") == "thread_pool")
                {
                    ShowView("city");
                    if (!LaunchArgs.TryGet("reviewCapture", out var threadPoolDirectory))
                        throw new ArgumentException("Thread pool review requires reviewCapture.");
                    await ThreadPresentationReview.RunPool(threadPoolDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Get("reviewScreen", "city") == "thread_workshop")
                {
                    ShowView("city");
                    if (!LaunchArgs.TryGet("reviewCapture", out var threadDirectory))
                        throw new ArgumentException("Thread review requires reviewCapture.");
                    await ThreadPresentationReview.Run(threadDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Get("reviewScreen", "city") == "earnings")
                {
                    ShowView("city");
                    if (!LaunchArgs.TryGet("reviewCapture", out var earningsDirectory))
                        throw new ArgumentException("Earnings review requires reviewCapture.");
                    await EarningsPresentationReview.Run(earningsDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                if (LaunchArgs.Get("reviewScreen", "city") == "mod_desk")
                {
                    ShowView("city");
                    if (!LaunchArgs.TryGet("reviewCapture", out var practiceDirectory))
                        throw new ArgumentException("Mod desk review requires reviewCapture.");
                    await ModDeskPresentationReview.Run(practiceDirectory, destroyCancellationToken);
                    if (LaunchArgs.Flag("reviewExit")) Finish(0);
                    return;
                }
                ShowView(LaunchArgs.Get("reviewScreen", "city"));
                if (LaunchArgs.TryGet("reviewCapture", out var directory)) StartCoroutine(CaptureViews(directory));
                Log.Info("review", "Ready. Read-only sample profile; no authentication or backend connection.");
            }
            catch (Exception e)
            {
                Log.Exception("review", e);
                Finish(1);
            }
        }

        private async System.Threading.Tasks.Task Travel(string venue)
        {
            if (_travelling) return;
            _travelling = true;
            try
            {
                _phone?.Close();
                await VenueSession.Enter(venue, joinRoom: false, voice: false);
                if (LaunchArgs.Flag("reviewCloseCamera"))
                {
                    var scene = VenueSession.Current.Venue;
                    scene.cameraDistance = ReviewFloat("reviewCameraDistance", 13.8f, 8f, 22f);
                    scene.cameraPitch = ReviewFloat("reviewCameraPitch", 32f, 20f, 50f);
                    scene.cameraYaw = -12f;
                    scene.cameraTargetViewportY = ReviewFloat("reviewCameraTargetY", Screen.width > Screen.height ? 0.38f : 0.43f, 0.2f, 0.6f);
                    CameraRig.Instance.ConfigureVenue(scene);
                    CameraRig.Instance.SnapToTarget();
                }
                AddReviewCrowd();
            }
            finally { _travelling = false; }
        }

        private static void ReadOnly() => Bus.Publish(ToastSignal.Text("Visual review only — sample data, no account changes."));

        private static float ReviewFloat(string key, float fallback, float min, float max)
        {
            if (!LaunchArgs.TryGet(key, out var raw)) return fallback;
            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                    out var value) || float.IsNaN(value) || value < min || value > max)
                throw new ArgumentException($"{key} must be between {min} and {max}.");
            return value;
        }

        private static void AddReviewCrowd()
        {
            if (!LaunchArgs.TryGet("reviewCrowd", out var requested)) return;
            if (!int.TryParse(requested, out int total) || total < 1 || total > 62)
                throw new ArgumentException("reviewCrowd must be a total avatar count from 1 to 62.");
            var session = VenueSession.Current;
            var spots = Services.Get<ConfigStore>().Registries.SpotsFor(session.VenueId);
            var look = Services.Get<PlayerProfile>().LookArray();
            var random = new System.Random(62);
            string[] emotes = { "emote_wave", "emote_clap", "emote_laugh", "emote_shrug" };
            for (int i = 1; i < total; i++)
            {
                var avatar = AvatarFactory.Create("Review crowd " + i, (string[])look.Clone(), false, null, session.transform);
                avatar.userId = "review-crowd-" + i;
                avatar.gameObject.AddComponent<CrowdWalker>().Init(session.Venue, spots, random.Next(), emotes);
            }
            Log.Info("review", $"Synthetic local crowd ready: {total} avatars including player; current character assets, no networking or server NPC simulation. Quality={QualitySettings.GetQualityLevel()}, lodBias={QualitySettings.lodBias:0.00}.");
        }

        private async System.Threading.Tasks.Task CheckMovementAndTravel()
        {
            var venue = VenueSession.Current;
            int initialMapCount = LightmapSettings.lightmaps.Length;
            bool hadBakedLighting = CheckBakedLighting(venue);
            var initialVenue = venue.VenueId;
            var player = venue.LocalPlayer;
            if (!venue.Venue.TryGetSpot("center", out var destination, out _))
                throw new InvalidOperationException("Review venue has no center anchor.");
            var start = player.transform.position;
            bool longRun = Vector3.Distance(start, destination) > LocalPlayerController.RunIfFartherThan;
            await CheckMove(player, destination, longRun ? "run to center" : "walk to center",
                longRun ? "city-mid-run" : "city-mid-walk", longRun);
            var side = Vector3.Cross(Vector3.up, (destination - start).normalized).normalized;
            var sideDestination = FindSideWalk(venue.Venue, player, destination, side);
            await CheckMove(player, sideDestination, "sideways short walk", "city-side-walk", false);
            await CheckMove(player, destination, "short walk return", null, false);
            await CheckDirectMovement(player);
            Log.Info("review-audio", AudioManager.Instance.ReviewSnapshot());
            await Travel(initialVenue == "terminal_cafe" ? "timeline_square" : "terminal_cafe");
            if (hadBakedLighting && VenueSession.Current.Venue.GetComponentInChildren<VenueLightmaps>() == null && LightmapSettings.lightmaps.Length != 0)
                throw new InvalidOperationException("Baked venue left lightmaps behind in the unbaked venue.");
            await Travel(initialVenue);
            if (CameraRig.Instance == null || CameraRig.Instance.Target != VenueSession.Current.LocalPlayer.transform)
                throw new InvalidOperationException("Camera did not survive the venue round trip with the current player as target.");
            var p = VenueSession.Current.LocalPlayer.transform.position;
            if (!UnityEngine.AI.NavMesh.SamplePosition(p, out _, 0.4f, UnityEngine.AI.NavMesh.AllAreas))
                throw new InvalidOperationException("No navigation beneath the returned player.");
            if (hadBakedLighting)
            {
                if (!CheckBakedLighting(VenueSession.Current) || LightmapSettings.lightmaps.Length != initialMapCount)
                    throw new InvalidOperationException("Baked lighting did not return with exactly one venue lightmap table.");
                Log.Info("review-check", $"PASS baked lightmap binding and unload/reload lifecycle ({initialMapCount} maps).");
            }
            Log.Info("review-check", "PASS venue round trip, camera target and returned-player navigation.");
            Log.Info("review-audio", "immediate-return " + AudioManager.Instance.ReviewSnapshot());
            await Awaitable.WaitForSecondsAsync(2f);
            await Awaitable.EndOfFrameAsync();
            Log.Info("review-audio", "settled-return " + AudioManager.Instance.ReviewSnapshot());
            await CheckLiveSfxMute();
        }

        private static Vector3 FindSideWalk(VenueSceneRoot venue, LocalPlayerController player, Vector3 center, Vector3 side)
        {
            if (side.sqrMagnitude < 0.5f) side = player.transform.right;
            foreach (float sign in new[] { 1f, -1f })
            {
                if (!NavMesh.SamplePosition(center + side * (3f * sign), out var hit, 0.5f, NavMesh.AllAreas) ||
                    !venue.IsWalkable(hit.position) || Vector3.Distance(center, hit.position) < 2.5f) continue;
                var path = new NavMeshPath();
                if (NavMesh.CalculatePath(player.transform.position, hit.position, NavMesh.AllAreas, path) &&
                    path.status == NavMeshPathStatus.PathComplete) return hit.position;
            }
            throw new InvalidOperationException("No complete navigation path for the 3m sideways-walk review.");
        }

        private static async System.Threading.Tasks.Task CheckDirectMovement(LocalPlayerController player)
        {
            var origin = player.transform.position;
            var agent = player.GetComponent<NavMeshAgent>();
            var yaw = CameraRig.Instance.yaw;
            // Select an actually navigable direction instead of assuming the center's art layout is empty.
            Vector2 axis = Vector2.zero;
            foreach (var candidate in new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left })
            {
                var direction = Quaternion.Euler(0f, yaw, 0f) * new Vector3(candidate.x, 0f, candidate.y);
                if (!agent.Raycast(agent.nextPosition + direction * 6f, out _)) { axis = candidate; break; }
            }
            if (axis == Vector2.zero) throw new InvalidOperationException("Direct-movement review needs 6m of clear NavMesh.");
            foreach (float strength in new[] { 0.64f, 1f })
            {
                player.SnapTo(origin, 0f);
                float end = Time.realtimeSinceStartup + 1.4f, maxSpeed = 0f;
                var gait = LaunchArgs.Flag("reviewGaitChecks") ? new DeviceGaitSampler(player,
                    strength < 1f ? "direct-walk" : "direct-run", VenueSession.Current.Venue.groundMask.value) : null;
                try
                {
                    while (Time.realtimeSinceStartup < end)
                    {
                        player.SetMoveInput(axis * strength);
                        await Awaitable.NextFrameAsync();
                        maxSpeed = Mathf.Max(maxSpeed, player.ActualVelocity.magnitude);
                        if (gait != null)
                        {
                            await Awaitable.EndOfFrameAsync();
                            gait.SampleAfterEndOfFrame();
                        }
                    }
                }
                finally { gait?.WriteAndDispose(LaunchArgs.Get("reviewCapture", Path.Combine(Application.persistentDataPath, "review-gait"))); }
                await Awaitable.EndOfFrameAsync();
                CaptureMovingFrame(strength < 1f ? "direct-walk" : "direct-run");
                Log.Info("review-direct", "held " + player.ReviewSnapshot());
                if (maxSpeed < (strength < 1f ? 1f : 4f) || maxSpeed > LocalPlayerController.RunSpeed + 0.2f)
                    throw new InvalidOperationException($"Direct movement strength {strength} produced maxSpeed {maxSpeed}.");
                player.StopDirectInput();
                await Awaitable.WaitForSecondsAsync(0.25f);
                var stopped = player.transform.position;
                await Awaitable.WaitForSecondsAsync(0.3f);
                if (Vector3.Distance(stopped, player.transform.position) > 0.03f || player.DirectInputActive)
                    throw new InvalidOperationException("Player moved after direct-control release.");
                Log.Info("review-check", $"PASS direct {(strength < 1f ? "walk" : "run")}, maxSpeed={maxSpeed:0.000}, release drift={Vector3.Distance(stopped, player.transform.position):0.000}m.");
            }
            player.SnapTo(origin, 0f);
            await Awaitable.WaitForSecondsAsync(0.3f);
            var nav = agent.nextPosition;
            if (Physics.Raycast(nav + Vector3.up * 0.35f, Vector3.down, out var floor, 0.85f, ~0, QueryTriggerInteraction.Ignore))
            {
                float gap = Mathf.Abs(player.transform.position.y - floor.point.y);
                if (gap > 0.025f) throw new InvalidOperationException($"Grounded root is {gap:0.000}m from paving.");
                Log.Info("review-check", $"PASS collider grounding: rootGap={gap:0.0000}m; " + player.ReviewSnapshot());
            }
            else throw new InvalidOperationException("Direct-movement review cannot find the paving collider.");
        }

        private static async System.Threading.Tasks.Task CheckMove(LocalPlayerController player, Vector3 destination,
            string label, string captureName, bool expectRun)
        {
            var start = player.transform.position;
            var agent = player.GetComponent<NavMeshAgent>();
            var animator = player.GetComponent<AvatarView>().Animator;
            if (animator == null || !animator.isHuman)
                throw new InvalidOperationException("Movement-facing review requires the authored Humanoid avatar.");
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (head == null || leftEye == null || rightEye == null)
                throw new InvalidOperationException("Movement-facing review requires mapped head and eye bones.");
            if (!player.MoveTo(destination)) throw new InvalidOperationException("Offline movement rejected " + label + ".");
            float began = Time.realtimeSinceStartup, deadline = began + 15f;
            float minFacingDot = 1f, maxSpeed = 0f;
            int facingSamples = 0;
            bool moved = false, captured = captureName == null;
            float captureDistance = Vector3.Distance(start, destination) * 0.4f;
            while (Time.realtimeSinceStartup < deadline)
            {
                await Awaitable.NextFrameAsync();
                moved |= player.IsMoving;
                var velocity = agent.velocity;
                velocity.y = 0f;
                maxSpeed = Mathf.Max(maxSpeed, velocity.magnitude);
                if (Time.realtimeSinceStartup - began >= 0.45f && velocity.magnitude > 0.5f)
                {
                    // Measure rendered bones after animation/camera updates, after the initial turn has settled.
                    await Awaitable.EndOfFrameAsync();
                    velocity = agent.velocity;
                    velocity.y = 0f;
                    if (velocity.magnitude > 0.5f)
                    {
                        var face = (leftEye.position + rightEye.position) * 0.5f - head.position;
                        face.y = 0f;
                        if (face.sqrMagnitude < 0.000001f)
                            throw new InvalidOperationException("Degenerate projected head-to-eyes direction during " + label + ".");
                        float dot = Vector3.Dot(face.normalized, velocity.normalized);
                        minFacingDot = Mathf.Min(minFacingDot, dot);
                        facingSamples++;
                        if (dot <= 0.3f)
                            throw new InvalidOperationException($"Rendered face opposes movement during {label}: dot={dot:0.000}, velocity={velocity}, face={face}.");
                        if (!captured && Vector3.Distance(start, player.transform.position) >= captureDistance)
                        {
                            bool screenshot = CaptureMovingFrame(captureName);
                            captured = true;
                            Log.Info("review-check", $"Movement sample {label}: speed={velocity.magnitude:0.00}m/s, faceVelocityDot={dot:0.000}, position={player.transform.position}, screenshot={screenshot}.");
                        }
                    }
                }
                if (Vector3.Distance(player.transform.position, destination) < 0.15f) break;
            }
            float remaining = Vector3.Distance(player.transform.position, destination);
            bool speedMatches = expectRun ? maxSpeed > 3.5f : maxSpeed > 0.5f && maxSpeed <= 3.5f;
            if (!moved || remaining > 0.15f || Vector3.Distance(start, player.transform.position) < 1f ||
                facingSamples == 0 || !captured || !speedMatches)
                throw new InvalidOperationException($"Offline {label} incomplete: moved={moved}, remaining={remaining:0.00}m, maxSpeed={maxSpeed:0.00}, facingSamples={facingSamples}, captured={captured}.");
            Log.Info("review-check", $"PASS offline {label}, remaining={remaining:0.00}m, maxSpeed={maxSpeed:0.00}m/s, facingSamples={facingSamples}, minFaceVelocityDot={minFacingDot:0.000}.");
        }

        private static bool CaptureMovingFrame(string name)
        {
            if (!LaunchArgs.TryGet("reviewCapture", out var directory)) return false;
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG()); }
            finally { Destroy(texture); }
            Log.Info("review", $"Captured {name} while moving: {Screen.width}x{Screen.height}");
            return true;
        }

        private static async System.Threading.Tasks.Task CheckLiveSfxMute()
        {
            var audio = AudioManager.Instance;
            float previous = audio.sfxVolume;
            try
            {
                if (!audio.Map.Sfx.TryGetValue("sfx_ui_open", out var entry) || entry.files.Count == 0 || audio.Clip(entry.files[0]) == null)
                    throw new InvalidOperationException("Live-mute review requires the sourced UI-open clip.");
                audio.sfxVolume = 0.6f;
                audio.Play("sfx_ui_open");
                await Awaitable.NextFrameAsync();
                int activeBefore = 0;
                foreach (var source in audio.GetComponentsInChildren<AudioSource>())
                    if (source.name.StartsWith("sfx", StringComparison.Ordinal) && source.isPlaying && source.volume > 0f) activeBefore++;
                if (activeBefore == 0) throw new InvalidOperationException("UI-open cue was no longer active before the live-mute sample.");
                Log.Info("review-audio", "mute-before " + audio.ReviewSnapshot());
                audio.sfxVolume = 0f;
                await Awaitable.NextFrameAsync();
                int activeMuted = 0;
                foreach (var source in audio.GetComponentsInChildren<AudioSource>())
                {
                    if (!source.name.StartsWith("sfx", StringComparison.Ordinal) || !source.isPlaying) continue;
                    activeMuted++;
                    if (source.volume > 0.0001f) throw new InvalidOperationException("Playing SFX source retained gain after Sound was set to zero.");
                }
                if (activeMuted == 0) throw new InvalidOperationException("No playing SFX source remained during the mute sample.");
                Log.Info("review-audio", "mute-active " + audio.ReviewSnapshot());
                Log.Info("review-check", $"PASS live SFX source gain mute, activeBefore={activeBefore}, activeMuted={activeMuted}; runtime state only, no listening assertion.");
            }
            finally
            {
                audio.sfxVolume = previous;
                Log.Info("review-audio", $"mute-restored sfxVolume={audio.sfxVolume:0.000}");
            }
        }

        private static bool CheckBakedLighting(VenueSession session)
        {
            var baked = session.Venue.GetComponentInChildren<VenueLightmaps>();
            if (baked == null) return false;
            var maps = LightmapSettings.lightmaps;
            if (baked.colors.Length == 0 || baked.bindings.Length == 0)
                throw new InvalidOperationException("Baked venue has an empty lighting payload.");
            foreach (var row in baked.bindings)
            {
                int index = row.renderer.lightmapIndex;
                if (index < 0 || index >= maps.Length || maps[index].lightmapColor != baked.colors[row.lightmap] ||
                    (row.renderer.lightmapScaleOffset - row.scaleOffset).sqrMagnitude > 0.000001f)
                    throw new InvalidOperationException("Baked lightmap binding mismatch on " + row.renderer.name);
            }
            return true;
        }

        private void ShowView(string view)
        {
            if (_inputTrace) Log.Info("review-input", "ShowView=" + view + "; before " + LayerState());
            _screen?.Close();
            _screen = null;
            _phone.Close();
            VenueSession.Current?.LocalPlayer?.CancelMovement();
            bool inWorld = view != "signin" && view != "creator" && view != "dashboard";
            _hud.Show(inWorld);
            switch (view)
            {
                case "signin": _screen = new SignInScreen(ReadOnly, true); break;
                case "dashboard":
                    _screen = new DashboardScreen(EnterReviewCity, OpenReviewApp);
                    AudioManager.Instance?.Music.SetDistrict("lobby");
                    break;
                case "creator": _screen = new CharacterCreatorScreen(Services.Get<ConfigStore>().Registries, _ => ReadOnly(), readOnlyReview: true); break;
                case "phone": _phone.Open(); break;
                case "map": _phone.Open("map"); break;
                case "bank": _phone.Open("bank"); break;
                case "profile": _phone.Open("profile"); break;
                case "city":
                    var current = VenueSession.Current;
                    if (current != null)
                    {
                        var v = Services.Get<ConfigStore>().Registries.Venue(current.VenueId);
                        AudioManager.Instance?.Music.SetMusic(current.VenueId, v?.districtId, v != null && v.isClub);
                    }
                    break;
                default: throw new ArgumentException("Unknown review view: " + view);
            }
            _screen?.Show();
            if (_inputTrace) Log.Info("review-input", "ShowView complete=" + view + "; " + LayerState());
        }

        private void EnterReviewCity()
        {
            if (_inputTrace) Log.Info("review-input", "Dashboard callback=enter-city");
            ShowView("city");
        }

        private void OpenReviewApp(string id)
        {
            if (_inputTrace) Log.Info("review-input", "App callback=" + (id ?? "phone-home") + "; before " + LayerState());
            _phone.Open(id);
            if (_inputTrace) Log.Info("review-input", "App callback complete; " + LayerState());
        }

        private string LayerState() => $"screen={_screen?.GetType().Name ?? "none"}; " +
            $"screenAttached={_screen?.Element?.parent != null}; phoneOpen={_phone?.IsOpen == true}; " +
            $"screenChildren={UiRoot.Instance.ScreenLayer.childCount}; phoneChildren={UiRoot.Instance.PhoneLayer.childCount}; " +
            $"hudDisplay={UiRoot.Instance.HudLayer.resolvedStyle.display}";

        private void TracePointerDown(PointerDownEvent e) => TracePointer("down", e.pointerId, e.position, e.target as VisualElement);
        private void TracePointerUp(PointerUpEvent e) => TracePointer("up", e.pointerId, e.position, e.target as VisualElement);

        private void TracePointer(string phase, int pointerId, Vector3 position, VisualElement target)
        {
            var ui = UiRoot.Instance;
            var picked = ui.Root.panel?.Pick(new Vector2(position.x, position.y));
            // Classes identify controls without logging labels, aliases, balances, input text or accounts.
            Log.Info("review-input", $"pointer={phase}; id={pointerId}; panelPosition={position:F2}; " +
                $"screen={Screen.width}x{Screen.height}; rootBounds={ui.Root.worldBound}; " +
                $"target={TraceControl(target)}; pick={TraceControl(picked)}; " + LayerState());
            Log.Info("review-input", "lobby-settings=" + TraceControl(_screen?.Element?.Q<Button>(className: "ct-lobby-settings")) +
                "; lobby-enter=" + TraceControl(_screen?.Element?.Q<Button>(className: "ct-lobby-enter")));
        }

        private static string TraceControl(VisualElement element)
        {
            if (element == null) return "none";
            var control = element;
            for (var ancestor = element; ancestor != null; ancestor = ancestor.parent)
                if (ancestor is Button) { control = ancestor; break; }
            return control.GetType().Name + "[" + string.Join(",", control.GetClasses()) + "] bounds=" + control.worldBound;
        }

        private void AddReviewBar()
        {
            // QA tooling is deliberately styled locally and clearly separated from product UI.
            _reviewBar = new VisualElement { name = "visual-review-controls" };
            _reviewBar.style.position = Position.Absolute;
            _reviewBar.style.left = 20;
            _reviewBar.style.right = StyleKeyword.Auto;
            _reviewBar.style.bottom = 4;
            _reviewBar.style.flexDirection = FlexDirection.Row;
            _reviewBar.style.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 0.9f);
            _reviewBar.style.paddingLeft = 8;
            _reviewBar.style.paddingRight = 8;
            var label = new Label("VISUAL REVIEW · SAMPLE DATA");
            label.style.color = Color.white;
            label.style.fontSize = 18;
            label.style.flexGrow = 1;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            _reviewBar.Add(label);
            // Normal review captures show only a sample-data label. Opt in to the
            // engineering navigation strip when testing; it is not the game's HUD.
            if (!LaunchArgs.Flag("reviewControls"))
            {
                _reviewBar.pickingMode = PickingMode.Ignore;
                UiRoot.Instance.ToastLayer.Add(_reviewBar);
                return;
            }
            foreach (var name in new[] { "dashboard", "city", "signin", "creator", "phone", "map", "bank" })
            {
                var target = name;
                var button = new Button(() => ShowView(target)) { text = name };
                button.style.fontSize = 18;
                button.style.height = 42;
                _reviewBar.Add(button);
            }
            UiRoot.Instance.ToastLayer.Add(_reviewBar);
        }

        private IEnumerator CaptureViews(string directory)
        {
            // Coroutine exceptions do not propagate into Start's async try/catch. Report a
            // failed capture and honor the explicit process-exit flag instead of hanging QA.
            var capture = CaptureViewsCore(directory);
            while (true)
            {
                bool hasNext = false;
                object wait = null;
                Exception failure = null;
                try
                {
                    hasNext = capture.MoveNext();
                    if (hasNext) wait = capture.Current;
                }
                catch (Exception e) { failure = e; }
                if (failure != null)
                {
                    (capture as IDisposable)?.Dispose();
                    Log.Exception("review-capture", failure);
                    Finish(1);
                    yield break;
                }
                if (!hasNext) yield break;
                yield return wait;
            }
        }

        private IEnumerator CaptureViewsCore(string directory)
        {
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            string sequence = LaunchArgs.Get("reviewSequence", "dashboard,city,signin,creator,phone,map,bank");
            foreach (var part in sequence.Split(','))
            {
                var captureName = part.Trim();
                var view = captureName;
#if UNITY_EDITOR
                if (captureName == "city-portrait" || captureName == "city-landscape")
                {
                    bool landscape = captureName == "city-landscape";
                    var type = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
                    var window = UnityEditor.EditorWindow.GetWindow(type);
                    var setResolution = type.GetMethod("SetCustomResolution", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (setResolution == null) { Log.Error("review", "GameView resolution API unavailable."); Finish(1); yield break; }
                    setResolution.Invoke(window, new object[] { landscape ? new Vector2(2670, 1200) : new Vector2(1200, 2670), "CT Life paired orientation review" });
                    window.Focus();
                    view = "city";
                }
#endif
                ShowView(view);
                yield return new WaitForSecondsRealtime(2);
#if UNITY_EDITOR
                // First use can render Unity's cyan placeholder while URP variants compile.
                // Capture only after several settled rendered frames, with a bounded failure path.
                float compileDeadline = Time.realtimeSinceStartup + 180f;
                int stableFrames = 0;
                while (stableFrames < 3)
                {
                    yield return new WaitForEndOfFrame();
                    stableFrames = UnityEditor.ShaderUtil.anythingCompiling || UnityEditor.EditorApplication.isUpdating
                        ? 0 : stableFrames + 1;
                    if (Time.realtimeSinceStartup > compileDeadline)
                    {
                        Log.Error("review", "Shader/import warm-up did not settle; visual capture rejected.");
                        Finish(1);
                        yield break;
                    }
                }
#endif
                // Normal Editor/Game view or a player only; WaitForEndOfFrame cannot run in Editor batchmode.
                yield return new WaitForEndOfFrame();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(directory, captureName + ".png"), texture.EncodeToPNG());
                Destroy(texture);
                Log.Info("review", $"Captured {view}: {Screen.width}x{Screen.height}");
                if (view == "city")
                {
                    bool layoutPassed = true;
                    var layoutDirectory = captureName == "city" ? directory : Path.Combine(directory, captureName);
                    Directory.CreateDirectory(layoutDirectory);
                    try { HudLayoutReview.Capture(_hud.Element, layoutDirectory); }
                    catch (Exception e) { Log.Exception("review-layout", e); layoutPassed = false; }
                    if (!layoutPassed) { Finish(1); yield break; }
                    var camera = CameraRig.Instance.Camera;
                    var player = VenueSession.Current.LocalPlayer.transform.position;
                    Log.Info("review", $"City framing: camera={camera.transform.position}, rotation={camera.transform.eulerAngles}, fov={camera.fieldOfView}, player={player}, playerViewport={camera.WorldToViewportPoint(player)}");
                    if (LaunchArgs.Flag("reviewIncidentFixture"))
                    {
                        // Explicit offline visual fixture; the review watermark remains visible.
                        ServerClock.OnWelcome(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                        Bus.Publish(new EventMessage
                        {
                            eventId = "evt_rug", at = ServerClock.NowMs,
                            @params = new System.Collections.Generic.Dictionary<string, string>
                            { { "ticker", "FROGE" }, { "peakMcapCents", "420000000" } }
                        });
                        yield return new WaitForSecondsRealtime(0.35f);
                        yield return new WaitForEndOfFrame();
                        bool incidentLayoutPassed = true;
                        try { HudLayoutReview.CaptureIncident(UiRoot.Instance.HudLayer, layoutDirectory); }
                        catch (Exception e) { Log.Exception("review-layout", e); incidentLayoutPassed = false; }
                        if (!incidentLayoutPassed) { Finish(1); yield break; }
                        var incidentTexture = ScreenCapture.CaptureScreenshotAsTexture();
                        File.WriteAllBytes(Path.Combine(directory, captureName + "-incident.png"), incidentTexture.EncodeToPNG());
                        Destroy(incidentTexture);
                        Log.Info("review", "Captured explicit incident fixture; no backend broadcast is implied.");
                    }
                }
            }
            Log.Info("review", "Capture sequence complete: " + directory);
            if (LaunchArgs.Flag("reviewExit")) Finish(0);
        }

        private static void Finish(int code)
        {
#if UNITY_EDITOR
            if (LaunchArgs.Flag("reviewExit")) UnityEditor.EditorApplication.Exit(code);
#else
            if (LaunchArgs.Flag("reviewExit")) Application.Quit(code);
#endif
        }

        private void OnDestroy()
        {
            if (_disposed) return;
            _disposed = true;
#if UNITY_EDITOR
            if (_reviewRuntimeConfigured) Application.runInBackground = _originalRunInBackground;
#endif
            VenueLoader.ReviewPrefabResolver = null;
            if (_inputTrace && _ui != null)
            {
                _ui.Root.UnregisterCallback<PointerDownEvent>(TracePointerDown, TrickleDown.TrickleDown);
                _ui.Root.UnregisterCallback<PointerUpEvent>(TracePointerUp, TrickleDown.TrickleDown);
            }
            _toasts?.Dispose();
            _toasts = null;
            _practiceJobs?.Dispose();
            _screen?.Close();
            _phone?.Dispose();
            _phone = null;
            _hud?.Dispose();
            _reviewBar?.RemoveFromHierarchy();
        }
    }
}
#endif
