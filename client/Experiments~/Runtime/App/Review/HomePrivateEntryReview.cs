#if UNITY_EDITOR && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.Rest.Model;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace CTLife.App.Review
{
    /// <summary>Actual Flow/Home button/scene lifecycle, isolated service boundaries and no transport writes.</summary>
    public static class HomePrivateEntryReview
    {
        private const string Prefab = "Assets/Art/Candidates/ParentsBasement20261007B2/ParentsBasementB2.prefab";
        private const string PrefabHash = "010c1c9ccf99d8cf6521afb8681eb68fa52ac90187cdead76f05ac89bcbc4de6";
        [Serializable] private sealed class Receipt
        {
            public bool pass, servicesRestored, originalVenueReloaded, cleanupComplete;
            public string phase, failure, selectedPrefab = Prefab, selectedPrefabSha256 = PrefabHash;
            public int errors, profileReads, transportMutations, publicJoinAttempts, privateJoinAttempts, globalLeaveAttempts, globalDisconnectAttempts;
            public int homeVisits, actualButtonCallbacks, cancelledPendingEntries, destroyedPendingEntries;
            public List<string> checks = new List<string>(), captures = new List<string>(), cleanupErrors = new List<string>();
            public string limits = "Actual Flow EnterGame, Home phone NavigationSubmit callback, VenueSession/Loader, additive scenes, baked maps, NavMesh, local avatar and owned cleanup. Isolated GET /me and realtime doubles; public joins deliberately fail and use the existing logged offline fallback. No HTTP, wallet, RTC connection, microphone, token persistence, economic mutations, home visits, furniture poses, Android lifecycle or physical touch acceptance. Stale-view callbacks are separately invoked through their actual Visit body. Frame cap is requested, not a device performance claim.";
        }
        private sealed class Saved<T> : IDisposable where T : class
        {
            private readonly T old; private readonly bool existed;
            public Saved(T value) { existed = Services.TryGet(out old); Services.Register(value); }
            public void Dispose() { if (existed) Services.Register(old); else Services.Unregister<T>(); }
            public bool Restored() => Services.TryGet<T>(out var value) ? existed && ReferenceEquals(value, old) : !existed;
        }
        private sealed class Transport : IRestClient
        {
            public Func<Task<bool>> RefreshHandler { get; set; }
            public Me Data; public int Reads, Mutations;
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested(); Need(path == "/me" && typeof(T) == typeof(Me), "Unexpected fixture read " + path);
                Reads++; return Task.FromResult((T)(object)Data);
            }
            private Task<T> Reject<T>() { Mutations++; return Task.FromException<T>(new InvalidOperationException("Home fixture forbids transport mutations.")); }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Reject<T>();
            public Task<T> Put<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Patch<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Delete<T>(string path, CancellationToken ct = default) => Reject<T>();
            public Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Reject<T>();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => Reject<RawResponse>();
        }
        private sealed class Realtime : IRealtimeService
        {
            public CityChannel City { get; } = new CityChannel();
            public MarketChannel Market => null; public VenueChannel Venue => null;
            public int PublicJoins, PrivateJoins, Leaves, Disconnects;
            public event Action<VenueChannel> VenueJoined { add { } remove { } }
            public event Action VenueLeft { add { } remove { } }
            public Task ConnectCity(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
            public Task ConnectMarket(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
            public Task<VenueChannel> JoinVenue(string id, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (id == "home" || id == "parents_basement") { PrivateJoins++; throw new InvalidOperationException("Private Home attempted a public join."); }
                PublicJoins++;
                return Task.FromException<VenueChannel>(new InvalidOperationException("Explicit isolated public offline fallback."));
            }
            public Task LeaveVenue() { Leaves++; throw new InvalidOperationException("Fixture forbids global venue leave."); }
            public Task DisconnectAll() { Disconnects++; throw new InvalidOperationException("Fixture forbids global disconnect."); }
        }

        public static async Task Run(string directory, CancellationToken owner)
        {
            directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            var result = new Receipt();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner);
            deadline.CancelAfter(TimeSpan.FromSeconds(180)); var ct = deadline.Token;
            var saved = new List<IDisposable>(); var restored = new List<Func<bool>>();
            var original = VenueSession.Current; Need(original != null && original.IsActive && !original.IsOwnHome, "An isolated active public review scene is required.");
            string originalId = original.VenueId;
            GameObject host = null; FlowController flow = null; PhoneShell phone = null;
            Transport transport = null; Realtime realtime = null;
            var oldFrameRate = Application.targetFrameRate; var oldVsync = QualitySettings.vSyncCount;
            var oldBackground = Application.runInBackground;
            List<int> baselineScenes = null; LightmapData[] baselineMaps = null;
            void Save<T>(T value) where T : class { var item = new Saved<T>(value); saved.Add(item); restored.Add(item.Restored); }
            void LogMessage(string text, string stack, LogType kind) { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) result.errors++; }
            Application.logMessageReceived += LogMessage;
            try
            {
                Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0; Application.runInBackground = true;
                result.phase = "exact-source-and-isolated-profile";
                Need(Sha(Prefab) == PrefabHash, "Selected Home prefab differs from the accepted B2 artifact.");
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Prefab)?.GetComponent<VenueSceneRoot>();
                Need(prefab != null && Resources.Load<VenueContentRegistry>("World/VenueContentRegistry").FindHome("parents_basement") == prefab, "Catalog does not select exact B2 Home.");
                var profileData = Clone(Services.Get<PlayerProfile>().Me);
                transport = new Transport { Data = profileData };
                var api = new Api(transport, Services.Get<ConfigStore>()); var profile = new PlayerProfile(api);
                var session = new Session();
                typeof(Session).GetProperty(nameof(Session.UserId)).SetValue(session, profileData.User.Id.ToString());
                typeof(Session).GetProperty(nameof(Session.AccessToken)).SetValue(session, "isolated-review-placeholder");
                realtime = new Realtime();
                await original.Exit(); await Frames(2, ct);
                baselineScenes = Scenes(); baselineMaps = LightmapSettings.lightmaps;
                Save(session); Save(api); Save(profile); Save<IRealtimeService>(realtime);
                host = new GameObject("ReviewPrivateHomeFlow"); flow = host.AddComponent<FlowController>(); flow.enabled = false;
                await (Task)Invoke(flow, "EnterGame");
                phone = Field<PhoneShell>(flow, "_phone"); Need(phone != null && Field<Hud>(flow, "_hud") != null, "Actual EnterGame did not build its phone/HUD.");
                var game = Field<FlowLifetime>(flow, "_gameplay");
                await (Task)Invoke(flow, "Travel", game, "timeline_square"); await Frames(2, ct);
                CheckPublic("timeline_square");
                result.checks.Add("PASS actual EnterGame GET /me, city/market service boundary and phone/HUD; actual public Travel used recorded offline fallback.");

                result.phase = "unsupported-and-stale-home-selection";
                var current = VenueSession.Current;
                foreach (string unsupported in new[] { "hacker_house", "studio", "unknown_home", null })
                {
                    profileData.HomeId = unsupported; phone.Open("home"); await Frames(2, ct);
                    var unavailable = Button(phone, "home-enter-own"); Need(!unavailable.enabledInHierarchy && !VenueSession.CanEnterOwnHome(), "Unsupported home enabled entry: " + unsupported);
                    bool rejected = false; try { await VenueSession.EnterOwnHome(ct); } catch (InvalidOperationException) { rejected = true; }
                    Need(rejected && ReferenceEquals(VenueSession.Current, current) && current.IsActive, "Unsupported home replaced the current visit.");
                    phone.Close();
                }
                profileData.HomeId = "parents_basement"; phone.Open("home"); await Frames(2, ct);
                var staleApp = Field<PhoneApp>(phone, "_current"); var staleView = Field<object>(staleApp, "_view");
                var staleButton = Button(phone, "home-enter-own"); profileData.HomeId = "studio";
                Submit(staleButton); await Frames(2, ct);
                Need(ReferenceEquals(current, VenueSession.Current) && !Field<bool>(flow, "_travelling"), "A displayed button ignored changed HomeId.");
                phone.Close(); profileData.HomeId = "parents_basement";
                result.checks.Add("PASS null, unknown, hacker_house and studio do not substitute basement or release the current visit; changed selection is rechecked by the actual button callback.");

                result.phase = "actual-button-entry-walk-return";
                int joinsBefore = realtime.PublicJoins;
                phone.Open("home"); await Frames(2, ct); await Capture(directory, "home-entry-phone", result, ct);
                var enter = Button(phone, "home-enter-own"); Need(enter.enabledInHierarchy, "Supported Home entry disabled.");
                Submit(enter); result.actualButtonCallbacks++;
                await Until(() => VenueSession.Current != null && VenueSession.Current.IsActive && VenueSession.Current.IsOwnHome && !Field<bool>(flow, "_travelling"), ct);
                Need(!phone.IsOpen, "Phone close did not complete with successful Home entry.");
                CheckHome(prefab, baselineMaps, result);
                Need(realtime.PublicJoins == joinsBefore && realtime.PrivateJoins == 0, "Home entry called realtime join.");
                Invoke(staleApp, "Visit", staleView); await Frames(2, ct);
                Need(VenueSession.Current.IsOwnHome && realtime.PublicJoins == joinsBefore, "Closed old Home callback affected the live Home.");
                await Capture(directory, "home-arrival", result, ct);
                var home = VenueSession.Current; var player = home.LocalPlayer;
                home.Venue.TryGetSpot("center", out var destination, out _);
                Need(player.MoveTo(destination), "Actual Home center walk was rejected.");
                await Until(() => Xz(player.transform.position, destination) < .25f && !player.IsMoving, ct);
                await Capture(directory, "home-center-walk", result, ct);
                phone.Open("home"); await Frames(2, ct); await Capture(directory, "home-return-phone", result, ct);
                Submit(Button(phone, "home-return-city")); result.actualButtonCallbacks++;
                await Until(() => VenueSession.Current != null && VenueSession.Current.IsActive && !VenueSession.Current.IsOwnHome && !Field<bool>(flow, "_travelling"), ct);
                CheckPublic("timeline_square"); await Frames(2, ct);
                Need(home == null && player == null && realtime.PublicJoins == joinsBefore + 1, "Return did not release Home or used wrong public join count.");
                result.checks.Add("PASS actual phone Go home → B2, phone detach survives transition, local movement to center, actual Return to city → remembered Timeline, old avatar/session destroyed, no Home join/voice owner.");

                result.phase = "dashboard-return";
                await EnterByButton(flow, phone, ct); result.actualButtonCallbacks++; CheckHome(prefab, baselineMaps, result);
                Invoke(flow, "ShowLobby"); await Frames(2, ct);
                await (Task)Invoke(flow, "EnterCityFromLobby", game); CheckPublic("timeline_square");
                result.checks.Add("PASS existing dashboard Enter city returns from Home through the public travel owner.");

                result.phase = "cancel-owner-during-real-entry";
                phone.Open("home"); await Frames(2, ct); Submit(Button(phone, "home-enter-own")); result.actualButtonCallbacks++;
                Need(Field<bool>(flow, "_travelling"), "Home completed before cancellation; pending-entry case was not exercised.");
                Invoke(flow, "EndGameplay");
                await Until(() => ReferenceEquals(VenueSession.Current, null) && SameScenes(baselineScenes), ct); await Frames(2, ct);
                CheckEmpty(baselineScenes, baselineMaps); result.cancelledPendingEntries++;
                Need(!Field<bool>(flow, "_travelling"), "Cancelled gameplay retained travel gate.");
                result.checks.Add("PASS gameplay cancellation while real Home entry is pending drains owned scenes/maps/avatar/input and clears the captured travel gate.");

                result.phase = "destroy-owner-during-real-entry";
                await (Task)Invoke(flow, "EnterGame"); game = Field<FlowLifetime>(flow, "_gameplay"); phone = Field<PhoneShell>(flow, "_phone");
                await (Task)Invoke(flow, "Travel", game, "timeline_square");
                phone.Open("home"); await Frames(2, ct);
                var dyingApp = Field<PhoneApp>(phone, "_current"); var dyingView = Field<object>(dyingApp, "_view");
                Submit(Button(phone, "home-enter-own")); result.actualButtonCallbacks++;
                Need(Field<bool>(flow, "_travelling"), "Home completed before native destruction; pending-entry case was not exercised.");
                var destroyed = flow.destroyCancellationToken; Object.Destroy(host); host = null;
                await Until(() => destroyed.IsCancellationRequested && flow == null && ReferenceEquals(VenueSession.Current, null) && SameScenes(baselineScenes), ct); await Frames(2, ct);
                CheckEmpty(baselineScenes, baselineMaps); result.destroyedPendingEntries++;
                int finalJoins = realtime.PublicJoins; Invoke(dyingApp, "Visit", dyingView); phone.Open("home"); await Frames(2, ct);
                Need(!phone.IsOpen && ReferenceEquals(VenueSession.Current, null) && realtime.PublicJoins == finalJoins, "Destroyed Home UI/continuation reopened a room.");
                result.checks.Add("PASS actual Destroy cancels in-flight local entry, releases native scene/navigation/maps/input; retained old view callback and disposed phone are inert.");
                Need(transport.Mutations == 0 && realtime.PrivateJoins == 0 && realtime.Leaves == 0 && realtime.Disconnects == 0 && result.errors == 0, "Unexpected transport/global cleanup/runtime error.");
                result.pass = true;
            }
            catch (Exception error) { result.failure = error.GetType().Name + ": " + error.Message; throw; }
            finally
            {
                result.phase = result.pass ? "cleanup" : result.phase;
                async Task Cleanup(Func<Task> operation) { try { await operation(); } catch (Exception e) { result.cleanupErrors.Add(e.GetType().Name + ": " + e.Message); result.pass = false; } }
                await Cleanup(async () => { if (host != null) { Object.Destroy(host); host = null; await Frames(2, CancellationToken.None); } });
                await Cleanup(async () => { if (!ReferenceEquals(VenueSession.Current, null)) await VenueSession.Current.Exit(); });
                for (int i = saved.Count - 1; i >= 0; i--) { try { saved[i].Dispose(); } catch (Exception e) { result.cleanupErrors.Add(e.GetType().Name); result.pass = false; } }
                result.servicesRestored = restored.All(check => check());
                if (!owner.IsCancellationRequested)
                    await Cleanup(async () => { var visit = await VenueSession.Enter(originalId, joinRoom: false, owner: owner); result.originalVenueReloaded = visit.IsActive && visit.VenueId == originalId; });
                Application.targetFrameRate = oldFrameRate; QualitySettings.vSyncCount = oldVsync; Application.runInBackground = oldBackground;
                result.profileReads = transport?.Reads ?? 0; result.transportMutations = transport?.Mutations ?? 0;
                result.publicJoinAttempts = realtime?.PublicJoins ?? 0; result.privateJoinAttempts = realtime?.PrivateJoins ?? 0;
                result.globalLeaveAttempts = realtime?.Leaves ?? 0; result.globalDisconnectAttempts = realtime?.Disconnects ?? 0;
                result.cleanupComplete = result.cleanupErrors.Count == 0 && result.servicesRestored && (owner.IsCancellationRequested || result.originalVenueReloaded);
                result.pass &= result.cleanupComplete && result.errors == 0;
                Application.logMessageReceived -= LogMessage;
                File.WriteAllText(Path.Combine(directory, "home-private-entry-review.json"), JsonUtility.ToJson(result, true));
                Need(result.cleanupComplete && result.errors == 0, "Native review cleanup did not restore its owners or logged an unexpected error.");
            }
        }

        private static async Task EnterByButton(FlowController flow, PhoneShell phone, CancellationToken ct)
        {
            phone.Open("home"); await Frames(2, ct); Submit(Button(phone, "home-enter-own"));
            await Until(() => VenueSession.Current != null && VenueSession.Current.IsActive && VenueSession.Current.IsOwnHome && !Field<bool>(flow, "_travelling"), ct);
        }
        private static void CheckHome(VenueSceneRoot prefab, LightmapData[] baseline, Receipt receipt)
        {
            var visit = VenueSession.Current;
            Need(visit != null && visit.IsActive && visit.IsOwnHome && visit.VenueId == "home" && visit.HomeId == "parents_basement", "Wrong private visit identity.");
            Need(visit.Channel == null && Field<CTLife.Net.VoiceService>(visit, "_voice") == null && visit.Players.Count == 0 && visit.Npcs.Count == 0, "Private Home has remote/voice ownership.");
            Need(visit.LocalPlayer != null && visit.LocalPlayer.GetComponent<NavMeshAgent>().isOnNavMesh && !visit.Venue.IsGreybox, "Private local avatar/nav/content missing.");
            Need(visit.Venue.navigation == prefab.navigation && visit.Venue.Anchors.Count() == 6 && InputSubscriptions() == 1, "Wrong Home nav/anchors/input owner.");
            foreach (var anchor in prefab.GetComponentsInChildren<SpotAnchor>(true))
            { Need(visit.Venue.TryGetSpot(anchor.spotId, out var p, out var yaw) && (p - anchor.transform.position).sqrMagnitude < 1e-10f && Mathf.Abs(yaw - anchor.RotY) < 1e-5f, "Immutable Home anchor differs: " + anchor.spotId); }
            var maps = visit.Venue.GetComponent<VenueLightmaps>(); var actual = LightmapSettings.lightmaps;
            Need(actual.Length == baseline.Length + maps.colors.Length && maps.colors.Length > 0, "Home lightmap table not installed exactly once.");
            for (int i = 0; i < maps.colors.Length; i++) Need(actual[baseline.Length + i].lightmapColor == maps.colors[i], "Wrong Home lightmap texture.");
            foreach (var binding in maps.bindings) Need(binding.renderer.lightmapIndex == baseline.Length + binding.lightmap && binding.renderer.lightmapScaleOffset == binding.scaleOffset, "Home renderer lost its baked map binding.");
            receipt.homeVisits++;
        }
        private static void CheckPublic(string id) { var visit = VenueSession.Current; Need(visit != null && visit.IsActive && !visit.IsOwnHome && visit.VenueId == id && visit.LocalPlayer != null, "Public return mismatch."); }
        private static void CheckEmpty(List<int> scenes, LightmapData[] maps)
        {
            Need(ReferenceEquals(VenueSession.Current, null) && SameScenes(scenes) && InputSubscriptions() == 0, "Cancelled Home retained scene/session/input.");
            var actual = LightmapSettings.lightmaps; Need(actual.Length == maps.Length, "Cancelled Home retained baked map slots.");
            for (int i = 0; i < maps.Length; i++) Need(actual[i].lightmapColor == maps[i].lightmapColor, "Cancelled Home changed baseline maps.");
        }
        private static int InputSubscriptions() => ((Delegate)typeof(WorldInput).GetField("SpotTapped", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))?.GetInvocationList().Count(d => d.Target is VenueSession) ?? 0;
        private static List<int> Scenes() { var scenes = new List<int>(); for (int i = 0; i < SceneManager.sceneCount; i++) scenes.Add(SceneManager.GetSceneAt(i).handle); return scenes.OrderBy(x => x).ToList(); }
        private static bool SameScenes(List<int> scenes) => Scenes().SequenceEqual(scenes);
        private static Button Button(PhoneShell phone, string name) { var b = Field<VisualElement>(phone, "_body").Q<Button>(name); Need(b != null && b.panel != null, "Actual Home button missing: " + name); return b; }
        private static void Submit(Button button) { using var e = NavigationSubmitEvent.GetPooled(); e.target = button; button.SendEvent(e); }
        private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static float Xz(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        private static async Task Until(Func<bool> predicate, CancellationToken ct) { double end = Time.realtimeSinceStartupAsDouble + 30; while (!predicate()) { ct.ThrowIfCancellationRequested(); Need(Time.realtimeSinceStartupAsDouble < end, "Bounded native predicate timed out."); await Awaitable.NextFrameAsync(ct); } }
        private static async Task Frames(int count, CancellationToken ct) { for (int i = 0; i < count; i++) await Awaitable.NextFrameAsync(ct); }
        private static async Task Capture(string directory, string name, Receipt receipt, CancellationToken ct)
        {
            await Awaitable.EndOfFrameAsync(ct); var image = ScreenCapture.CaptureScreenshotAsTexture(); Need(image != null, "Native screenshot missing.");
            try { File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG()); receipt.captures.Add(name + ".png"); } finally { Object.Destroy(image); }
        }
        private static Me Clone(Me source)
        {
            Need(source?.Character?.Look != null, "Complete review profile required."); var c = source.Character; var l = c.Look;
            return new Me(new User(Guid.Parse("00000000-0000-4000-8000-000000000089"), "isolated-review", false, true),
                new Character("Home entry review", new Look(l.SkinTone, l.HeadId, l.HairId, l.TopId, l.BottomId, l.ShoesId, l.AccId), c.TribeId, new List<string>(c.TraitIds), c.DreamId),
                new Needs(70, 70, 70, 70, 70), new Skills(1, 1, 1, 1, 1, 1), 1000, 0, "parents_basement", flags: new Dictionary<string, object> { { "lotteryRolled", true } });
        }
        private static string Sha(string path) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException("Private Home review: " + message); }
    }
}
#endif
