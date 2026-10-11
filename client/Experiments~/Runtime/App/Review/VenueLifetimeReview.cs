#if UNITY_EDITOR && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.World;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.App.Review
{
    /// <summary>Actual additive venue/scene/avatar lifecycle; held joins use a local boundary, no socket.</summary>
    public static class VenueLifetimeReview
    {
        [Serializable] private sealed class Receipt
        {
            public bool pass, servicesRestored, resolverRestored, originalVenueReloaded;
            public string phase, failure;
            public int errors, heldJoins, nativeDestructions;
            public List<string> checks = new List<string>();
            public List<string> scenes = new List<string>();
            public string limits = "Real Unity VenueSession, VenueLoader, additive scenes, NavMesh, installed lightmaps, avatar creation/destruction and input subscriptions. Isolated delayed IRealtimeService joins return unconnected channels. No HTTP, RTC socket, microphone, role authority, capture permission, payout, phone lifecycle or continuous seat animation is certified. Repeated cancellation and late local completion are checked; injected native unload failure is not tested here.";
        }
        private sealed class HeldRealtime : IRealtimeService
        {
            public CityChannel City => null; public MarketChannel Market => null; public VenueChannel Venue => null;
            public event Action<VenueChannel> VenueJoined { add { } remove { } }
            public event Action VenueLeft { add { } remove { } }
            public TaskCompletionSource<VenueChannel> gate;
            public int joins; public CancellationToken token;
            public Task<VenueChannel> JoinVenue(string id, CancellationToken ct = default)
            { joins++; token = ct; return gate.Task; }
            public Task ConnectCity(CancellationToken ct = default) => throw new InvalidOperationException("No city connection in this fixture");
            public Task ConnectMarket(CancellationToken ct = default) => throw new InvalidOperationException("No market connection in this fixture");
            public Task LeaveVenue() => throw new InvalidOperationException("No global leave in this fixture");
            public Task DisconnectAll() => throw new InvalidOperationException("No global disconnect in this fixture");
        }
        public static async Task Run(string directory, CancellationToken owner)
        {
            Directory.CreateDirectory(directory);
            var receipt = new Receipt();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner);
            deadline.CancelAfter(TimeSpan.FromSeconds(90)); var ct = deadline.Token;
            var original = VenueSession.Current;
            Need(original != null && original.IsActive, "Requires an active isolated review venue");
            var originalId = original.VenueId;
            var resolver = VenueLoader.ReviewPrefabResolver;
            bool hadRealtime = Services.TryGet<IRealtimeService>(out var previousRealtime);
            var held = new HeldRealtime();
            var pending = new List<Task>();
            var sources = new List<CancellationTokenSource>();
            var gates = new List<TaskCompletionSource<VenueChannel>>();
            void OnLog(string message, string stack, LogType type)
            { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) receipt.errors++; }
            Application.logMessageReceived += OnLog;
            try
            {
                receipt.phase = "setup";
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Candidates/SpacesStudio20261006E2/SpacesStudioE2.prefab");
                Need(prefab != null, "Baked Studio E2 candidate missing");
                var studio = prefab.GetComponent<VenueSceneRoot>();
                Need(studio != null && studio.GetComponent<VenueLightmaps>()?.colors.Length > 0, "Baked Studio E2 required");
                await original.Exit(); await Frames(2, ct);
                Need(ReferenceEquals(VenueSession.Current, null) && OwnedSubscriptions() == 0, "Original review venue did not release ownership");
                var baselineScenes = Scenes(); var baselineMaps = LightmapSettings.lightmaps;
                VenueLoader.ReviewPrefabResolver = id => id == "spaces_studio" ? studio : resolver?.Invoke(id);
                Services.Register<IRealtimeService>(held);

                receipt.phase = "two-authored-venues";
                var first = await VenueSession.Enter("spaces_studio", joinRoom: false, owner: ct);
                CheckActive(first, "studio", receipt);
                var firstScene = first.Venue.gameObject.scene; var firstPlayer = first.LocalPlayer;
                var second = await VenueSession.Enter("timeline_square", joinRoom: false, owner: ct);
                await Frames(2, ct); CheckActive(second, "timeline", receipt);
                Need(!firstScene.isLoaded && firstPlayer == null && first == null, "Replacement retained old scene/player/session");
                receipt.checks.Add("PASS real Studio-to-Timeline replacement releases the old additive scene and avatars, installs one lightmap table and one input subscription.");
                await second.Exit(); await second.Exit(); await Frames(2, ct);
                CheckEmpty(baselineScenes, baselineMaps);

                receipt.phase = "worker-cancellation";
                var workerOwner = CancellationTokenSource.CreateLinkedTokenSource(ct); sources.Add(workerOwner);
                var workerVisit = await VenueSession.Enter("spaces_studio", joinRoom: false, owner: workerOwner.Token);
                var workerScene = workerVisit.Venue.gameObject.scene;
                await Task.Run(() => workerOwner.Cancel());
                await Until(() => ReferenceEquals(VenueSession.Current, null) && !workerScene.isLoaded, ct);
                await Frames(2, ct); CheckEmpty(baselineScenes, baselineMaps);
                receipt.checks.Add("PASS worker-thread owner cancellation marshals real scene/avatar cleanup to Unity and restores baseline scenes/maps/subscriptions.");

                receipt.phase = "held-join-replacement";
                held.gate = new TaskCompletionSource<VenueChannel>(TaskCreationOptions.RunContinuationsAsynchronously); gates.Add(held.gate);
                var oldEnter = VenueSession.Enter("spaces_studio", joinRoom: true, owner: ct); pending.Add(oldEnter);
                await Until(() => held.joins == 1, ct);
                var pendingVisit = VenueSession.Current;
                Need(pendingVisit != null && !pendingVisit.IsActive && pendingVisit.LocalPlayer == null, "Held join published a ready player");
                var pendingScene = pendingVisit.Venue.gameObject.scene;
                var replacement = await VenueSession.Enter("timeline_square", joinRoom: false, owner: ct);
                await Cancelled(oldEnter); pending.Remove(oldEnter);
                Need(held.token.IsCancellationRequested && !pendingScene.isLoaded, "Replacement left held visit token/scene live");
                held.gate.TrySetResult(new VenueChannel()); await Frames(3, ct);
                CheckActive(replacement, "late-join-successor", receipt);
                Need(ReferenceEquals(VenueSession.Current, replacement) && pendingVisit == null, "Late local join replaced successor");
                receipt.checks.Add("PASS newer visit cancels a held join after real scene loading; old scene drains, late local channel completion cannot spawn or publish over successor.");

                receipt.phase = "native-destruction";
                var destroyedScene = replacement.Venue.gameObject.scene;
                var destructionToken = replacement.destroyCancellationToken;
                Object.Destroy(replacement.gameObject);
                await Until(() => destructionToken.IsCancellationRequested && ReferenceEquals(VenueSession.Current, null) && !destroyedScene.isLoaded, ct);
                receipt.nativeDestructions++; await Frames(2, ct); CheckEmpty(baselineScenes, baselineMaps);
                var last = await VenueSession.Enter("spaces_studio", joinRoom: false, owner: ct);
                CheckActive(last, "after-destruction", receipt); await last.Exit(); await Frames(2, ct);
                CheckEmpty(baselineScenes, baselineMaps);
                receipt.checks.Add("PASS real Destroy cancels its visit, releases native scene/nav/avatar/input ownership, and a subsequent visit enters and exits normally.");
                Need(receipt.errors == 0, "Unexpected native errors"); receipt.pass = true;
            }
            catch (Exception error) { receipt.failure = error.GetType().Name + ": " + error.Message; throw; }
            finally
            {
                foreach (var gate in gates) gate.TrySetResult(new VenueChannel());
                foreach (var task in pending) Observe(task);
                try { if (!ReferenceEquals(VenueSession.Current, null)) await VenueSession.Current.Exit(); }
                catch (Exception cleanup) { receipt.pass = false; receipt.failure += " Exit cleanup: " + cleanup.GetType().Name + ": " + cleanup.Message; }
                try
                {
                    foreach (var source in sources) source.Dispose();
                    VenueLoader.ReviewPrefabResolver = resolver;
                    if (hadRealtime) Services.Register(previousRealtime); else Services.Unregister<IRealtimeService>();
                    receipt.servicesRestored = Services.TryGet<IRealtimeService>(out var current) ? hadRealtime && ReferenceEquals(current, previousRealtime) : !hadRealtime;
                    receipt.resolverRestored = ReferenceEquals(VenueLoader.ReviewPrefabResolver, resolver);
                    if (!owner.IsCancellationRequested)
                    {
                        var restored = await VenueSession.Enter(originalId, joinRoom: false, owner: owner);
                        receipt.originalVenueReloaded = restored.IsActive && restored.VenueId == originalId;
                    }
                }
                catch (Exception cleanup) { receipt.pass = false; receipt.failure += " Restoration: " + cleanup.GetType().Name + ": " + cleanup.Message; }
                Application.logMessageReceived -= OnLog;
                receipt.heldJoins = held.joins;
                if (!receipt.servicesRestored || !receipt.resolverRestored || !receipt.originalVenueReloaded) receipt.pass = false;
                File.WriteAllText(Path.Combine(directory, "venue-lifetime-review.json"), JsonUtility.ToJson(receipt, true));
                Need(receipt.pass, receipt.failure ?? "Venue review failed or restoration incomplete");
            }
        }
        private static void CheckActive(VenueSession session, string label, Receipt receipt)
        {
            Need(session != null && session.IsActive && ReferenceEquals(VenueSession.Current, session), "Wrong Current in " + label);
            Need(Object.FindObjectsByType<VenueSession>(FindObjectsSortMode.None).Length == 1, "Duplicate native sessions in " + label);
            Need(OwnedSubscriptions() == 1, "Expected one venue input subscription in " + label);
            Need(session.LocalPlayer != null && CameraRig.Instance.Target == session.LocalPlayer.transform, "Player/camera mismatch in " + label);
            var agent = session.LocalPlayer.GetComponent<NavMeshAgent>();
            Need(agent != null && agent.enabled && agent.isOnNavMesh, "Native navigation missing in " + label);
            Need(SceneManager.GetActiveScene() == session.Venue.gameObject.scene, "Venue is not active scene in " + label);
            var maps = session.Venue.GetComponent<VenueLightmaps>();
            Need(maps != null && maps.colors.Length > 0 && maps.bindings.Length > 0, "Expected baked venue in " + label);
            foreach (var binding in maps.bindings)
                Need(binding.renderer != null && binding.renderer.lightmapIndex >= 0 && binding.renderer.lightmapIndex < LightmapSettings.lightmaps.Length
                    && LightmapSettings.lightmaps[binding.renderer.lightmapIndex].lightmapColor == maps.colors[binding.lightmap], "Wrong installed lightmap in " + label);
            receipt.scenes.Add(label + " scene=" + session.Venue.gameObject.scene.handle + " maps=" + maps.colors.Length + " renderers=" + maps.bindings.Length);
        }
        private static int OwnedSubscriptions()
        {
            var field = typeof(WorldInput).GetField("SpotTapped", BindingFlags.Static | BindingFlags.NonPublic);
            return (field.GetValue(null) as Delegate)?.GetInvocationList().Count(d => d.Target is VenueSession) ?? 0;
        }
        private static Scene[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        private static void CheckEmpty(Scene[] scenes, LightmapData[] maps)
        {
            Need(ReferenceEquals(VenueSession.Current, null) && OwnedSubscriptions() == 0, "Empty venue ownership was retained");
            Need(Object.FindObjectsByType<VenueSession>(FindObjectsSortMode.None).Length == 0, "Destroyed venue session still live");
            Need(new HashSet<Scene>(Scenes()).SetEquals(scenes), "Additive scene leaked");
            Need(LightmapSettings.lightmaps.Length == maps.Length && LightmapSettings.lightmaps.Select(x => x.lightmapColor).SequenceEqual(maps.Select(x => x.lightmapColor)), "Global lightmaps not restored");
        }
        private static async Task Cancelled(Task task) { try { await task; throw new InvalidOperationException("Expected cancellation"); } catch (OperationCanceledException) { } }
        private static async Task Until(Func<bool> ready, CancellationToken ct)
        { double end = Time.realtimeSinceStartupAsDouble + 12; while (!ready()) { ct.ThrowIfCancellationRequested(); Need(Time.realtimeSinceStartupAsDouble < end, "Native predicate timed out"); await Awaitable.NextFrameAsync(ct); } }
        private static async Task Frames(int count, CancellationToken ct) { for (int i = 0; i < count; i++) await Awaitable.NextFrameAsync(ct); }
        private static void Observe(Task task) { _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
#endif
