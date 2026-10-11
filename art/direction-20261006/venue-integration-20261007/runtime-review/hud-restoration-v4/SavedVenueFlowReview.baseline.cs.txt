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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;

namespace CTLife.App.Review
{
    /// <summary>Actual saved-room map/Home travel and representative seat routing; isolated public transport.</summary>
    public static class SavedVenueFlowReview
    {
        private const string Content = "Assets/Art/Candidates/PlayableVenues20261007";
        private const string CatalogPath = "Assets/Resources/World/VenueContentRegistry.asset";
        private const string HomePath = "Assets/Art/Candidates/ParentsBasement20261007B2/ParentsBasementB2.prefab";
        private const string HomeHash = "010c1c9ccf99d8cf6521afb8681eb68fa52ac90187cdead76f05ac89bcbc4de6";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
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
            private Task<T> Reject<T>() { Mutations++; return Task.FromException<T>(new InvalidOperationException("Saved-room fixture forbids transport mutations.")); }
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
            public int PublicJoins, PrivateJoins, Leaves, Disconnects, HeldJoins, CancelledJoins;
            public bool HoldNext;
            public readonly List<string> JoinIds = new List<string>();
            public event Action<VenueChannel> VenueJoined { add { } remove { } }
            public event Action VenueLeft { add { } remove { } }
            public Task ConnectCity(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
            public Task ConnectMarket(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
            public async Task<VenueChannel> JoinVenue(string id, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (id == "home" || id == "parents_basement") { PrivateJoins++; throw new InvalidOperationException("Private Home attempted a public join."); }
                PublicJoins++; JoinIds.Add(id);
                if (HoldNext)
                {
                    HoldNext = false; HeldJoins++;
                    var pending = new TaskCompletionSource<VenueChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = ct.Register(() => pending.TrySetCanceled(ct));
                    try { return await pending.Task; } finally { if (ct.IsCancellationRequested) CancelledJoins++; }
                }
                throw new InvalidOperationException("Explicit isolated public offline fallback; no server success.");
            }
            public Task LeaveVenue() { Leaves++; throw new InvalidOperationException("Fixture forbids global venue leave."); }
            public Task DisconnectAll() { Disconnects++; throw new InvalidOperationException("Fixture forbids global disconnect."); }
        }

        public static async Task Run(string directory, string manifestPath, CancellationToken owner)
        {
            directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            var receipt = new JObject
            {
                ["revision"] = "saved-playable-venue-flow-arrival-queue-v3", ["pass"] = false,
                ["checks"] = new JArray(), ["visits"] = new JArray(), ["seat_cases"] = new JArray(),
                ["captures"] = new JArray(), ["cleanup_errors"] = new JArray(), ["ui_trace"] = new JArray(), ["hud_capture_checks"] = new JArray(),
                ["limits"] = "Actual Flow EnterGame, MapApp/Home UI callbacks, VenueSession/Loader, persisted profiles/controllers/motions, two approaches per family per look, injected seat confirmations and real stand presentation. Public joins are counted failures and use Flow's existing offline fallback; no HTTP/RTC/mic/server success or economy mutation. Instant entry is the selected game interaction. No new full-pose, all-seat-exit, remote, device, touch or frame-rate acceptance. Deliberate early private arrival calls test request retention, followed by real arrival, cancellation and newer-target controls. They are not a reproduction or diagnosis of the earlier intermittent natural failure."
            };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner);
            deadline.CancelAfter(TimeSpan.FromSeconds(600)); var ct = deadline.Token;
            var saved = new List<IDisposable>(); var restored = new List<Func<bool>>();
            var original = VenueSession.Current; Need(original != null && original.IsActive && !original.IsOwnHome, "An active isolated public review visit is required.");
            string originalId = original.VenueId; var resolver = VenueLoader.ReviewPrefabResolver;
            GameObject host = null; FlowController flow = null; PhoneShell phone = null;
            Transport transport = null; Realtime realtime = null; VenueContentRegistry catalog = null;
            List<VenueContentRegistry.Entry> oldVenues = null; string catalogJson = null; bool catalogDirty = false;
            List<int> baselineScenes = null; LightmapData[] baselineMaps = null;
            var oldFrameRate = Application.targetFrameRate; var oldVsync = QualitySettings.vSyncCount; var oldBackground = Application.runInBackground;
            int errors = 0, buttonCallbacks = 0, visits = 0, completedSeatCases = 0; string phase = "preflight"; bool tookOwnership = false;
            var pins = new Dictionary<string, string>(StringComparer.Ordinal); var content = new Dictionary<string, VenueSceneRoot>();
            void Save<T>(T value) where T : class { var item = new Saved<T>(value); saved.Add(item); restored.Add(item.Restored); }
            void Check(string message) => ((JArray)receipt["checks"]).Add("PASS " + message);
            void LogMessage(string text, string stack, LogType kind) { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) errors++; }
            Application.logMessageReceived += LogMessage;
            try
            {
                Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0; Application.runInBackground = true;
                receipt["helper_sha256"] = Sha("Assets/Scripts/App/Review/SavedVenueFlowReview.cs");
                var proof = ReadManifest(manifestPath, pins, receipt);
                foreach (string id in new[] { "terminal_cafe", "spaces_studio" })
                {
                    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Content + "/" + id + ".prefab");
                    Need(go != null, "Saved room is missing: " + id); content[id] = go.GetComponent<VenueSceneRoot>();
                    CheckPersistedProfile(content[id], id, proof, receipt);
                }
                Need(Sha(HomePath) == HomeHash, "Exact accepted private Home changed."); pins[HomePath] = HomeHash;
                catalog = Resources.Load<VenueContentRegistry>("World/VenueContentRegistry"); Need(catalog != null, "Catalog missing.");
                catalogDirty = UnityEditor.EditorUtility.IsDirty(catalog); Need(!catalogDirty, "Do not override a dirty user catalog.");
                catalogJson = JsonUtility.ToJson(catalog); oldVenues = catalog.venues;
                pins[CatalogPath] = Sha(CatalogPath);
                Need(catalog.FindHome("parents_basement") == UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(HomePath).GetComponent<VenueSceneRoot>(), "Catalog lost the selected private Home.");
                Need(catalog.Find("timeline_square") != null, "Existing selected Timeline missing.");
                // Ordinary MapApp uses Resources.Load. Only replace the loaded list for this review;
                // never SetDirty, save, import, mutate an existing row, or attach a transient seat profile.
                catalog.venues = oldVenues.Where(x => !content.ContainsKey(x.venueId)).ToList();
                foreach (var room in content) catalog.venues.Add(new VenueContentRegistry.Entry { venueId = room.Key, prefab = room.Value });
                VenueLoader.ReviewPrefabResolver = id => content.TryGetValue(id, out var room) ? room : resolver?.Invoke(id);
                var template = Services.Get<PlayerProfile>().Me;
                transport = new Transport { Data = Clone(template, LookKey("snow", proof)) };
                var api = new Api(transport, Services.Get<ConfigStore>()); var profile = new PlayerProfile(api);
                var session = new Session(); typeof(Session).GetProperty(nameof(Session.UserId)).SetValue(session, transport.Data.User.Id.ToString());
                typeof(Session).GetProperty(nameof(Session.AccessToken)).SetValue(session, "isolated-review-placeholder"); realtime = new Realtime();
                tookOwnership = true; await original.Exit(); await Frames(2, ct); baselineScenes = Scenes(); baselineMaps = LightmapSettings.lightmaps;
                Save(session); Save(api); Save(profile); Save<IRealtimeService>(realtime);
                host = new GameObject("ReviewSavedVenueFlow"); flow = host.AddComponent<FlowController>(); flow.enabled = false;
                await (Task)Invoke(flow, "EnterGame"); phone = Field<PhoneShell>(flow, "_phone"); Need(phone != null, "Actual EnterGame phone missing.");
                Check("actual EnterGame used isolated GET /me and built the ordinary phone/HUD");
                UiTrace(flow, phone, receipt, "after-EnterGame-dashboard");

                async Task TravelByMap(string id)
                {
                    phase = "map-to-" + id;
                    var old = VenueSession.Current; var oldPlayer = old != null ? old.LocalPlayer : null;
                    int oldScene = old != null ? old.Venue.gameObject.scene.handle : -1;
                    UiTrace(flow, phone, receipt, "before-map-open-" + id);
                    phone.Open("map"); await Frames(2, ct); UiTrace(flow, phone, receipt, "after-map-open-" + id);
                    var button = Button(phone, "map-venue-" + id); Need(button.enabledInHierarchy, "Ordinary map destination is locked: " + id);
                    Submit(button); buttonCallbacks++; UiTrace(flow, phone, receipt, "after-map-submit-" + id);
                    await Until(() => VenueSession.Current != null && VenueSession.Current.IsActive && VenueSession.Current.VenueId == id && !Field<bool>(flow, "_travelling"), ct, 30, "Map travel " + id);
                    await Frames(2, ct);
                    Need(!phone.IsOpen && (old == null || !ReferenceEquals(old, VenueSession.Current)), "Actual map callback did not replace visit.");
                    if (oldScene >= 0) Need(old == null && oldPlayer == null && !Scenes().Contains(oldScene), "Old room/avatar scene survived map travel.");
                    CheckRoom(VenueSession.Current, content.TryGetValue(id, out var expected) ? expected : catalog.Find(id), baselineScenes, baselineMaps, receipt);
                    visits++; UiTrace(flow, phone, receipt, "after-map-arrival-" + id);
                }
                foreach (string look in new[] { "snow", "rain" })
                {
                    phase = look + "-profile"; transport.Data = Clone(template, LookKey(look, proof)); await profile.Refresh(ct);
                    await TravelByMap("timeline_square");
                    await TravelByMap("terminal_cafe");
                    var local = VenueSession.Current.LocalPlayer;
                    CheckSelectedLook(local, look, proof);
                    await Capture(directory, look + "-cafe-arrival", receipt, flow, phone, ct);
                    if (look == "snow") await ArrivalPreservationReview(local, directory, receipt, ct);
                    foreach (string spot in new[] { "terminal_1", "table_1" })
                        for (int repeat = 0; repeat < 2; repeat++)
                        { phase = look + "-" + spot + "-" + repeat; await SeatCase(local, spot, look, repeat, directory, receipt, flow, phone, ct); completedSeatCases++; }
                    await TravelByMap("spaces_studio"); local = VenueSession.Current.LocalPlayer;
                    CheckSelectedLook(local, look, proof); await Capture(directory, look + "-studio-arrival", receipt, flow, phone, ct);
                    foreach (string spot in new[] { "seat_1", "stage_1" })
                        for (int repeat = 0; repeat < 2; repeat++)
                        { phase = look + "-" + spot + "-" + repeat; await SeatCase(local, spot, look, repeat, directory, receipt, flow, phone, ct); completedSeatCases++; }
                    phase = look + "-private-home-and-public-return";
                    var studio = VenueSession.Current; var studioPlayer = studio.LocalPlayer; int studioScene = studio.Venue.gameObject.scene.handle;
                    int joins = realtime.PublicJoins; phone.Open("home"); await Frames(2, ct);
                    Submit(Button(phone, "home-enter-own")); buttonCallbacks++;
                    await Until(() => VenueSession.Current != null && VenueSession.Current.IsActive && VenueSession.Current.IsOwnHome && !Field<bool>(flow, "_travelling"), ct, 30, "Private Home arrival");
                    await Frames(2, ct);
                    Need(studio == null && studioPlayer == null && !Scenes().Contains(studioScene) && realtime.PublicJoins == joins && realtime.PrivateJoins == 0, "Home entry leaked Studio or attempted public join.");
                    var home = VenueSession.Current; var homePlayer = home.LocalPlayer; int homeScene = home.Venue.gameObject.scene.handle;
                    Need(home.HomeId == "parents_basement", "HomeId route changed.");
                    CheckRoom(home, catalog.FindHome("parents_basement"), baselineScenes, baselineMaps, receipt); visits++;
                    await Capture(directory, look + "-home-arrival", receipt, flow, phone, ct);
                    phone.Open("home"); await Frames(2, ct); Submit(Button(phone, "home-return-city")); buttonCallbacks++;
                    await Until(() => VenueSession.Current != null && VenueSession.Current.IsActive && VenueSession.Current.VenueId == "spaces_studio" && !Field<bool>(flow, "_travelling"), ct, 30, "Remembered public return");
                    await Frames(2, ct);
                    Need(home == null && homePlayer == null && !Scenes().Contains(homeScene) && realtime.PublicJoins == joins + 1, "Home return did not release its scene or return via ordinary public travel.");
                    CheckRoom(VenueSession.Current, content["spaces_studio"], baselineScenes, baselineMaps, receipt); visits++;
                    Check(look + " actual map Timeline → Café → Studio → private Home → remembered Studio, isolated public fallback and owned unload");
                }
                phase = "destroy-owner-during-pending-map-travel";
                phone.Open("map"); await Frames(2, ct); var staleApp = Field<PhoneApp>(phone, "_current");
                var staleTravel = Field<Action<string>>(staleApp, "_travel"); realtime.HoldNext = true;
                Submit(Button(phone, "map-venue-terminal_cafe")); buttonCallbacks++;
                await Until(() => realtime.HeldJoins == 1 && Field<bool>(flow, "_travelling"), ct, 10, "Held public join pending");
                var destroyed = flow.destroyCancellationToken; Object.Destroy(host); host = null;
                await Until(() => destroyed.IsCancellationRequested && flow == null && ReferenceEquals(VenueSession.Current, null) && SameScenes(baselineScenes) && realtime.CancelledJoins == 1, ct, 30, "Destroyed owner drains pending travel");
                await Frames(2, ct); CheckEmpty(baselineScenes, baselineMaps);
                int finalJoins = realtime.PublicJoins; staleTravel("spaces_studio"); phone.Open("map"); await Frames(2, ct);
                Need(!phone.IsOpen && VenueSession.Current == null && realtime.PublicJoins == finalJoins, "Obsolete map callback reopened a visit.");
                Check("Destroy cancels an actual pending map→public join, drains loaded scene/maps/input and leaves stale MapApp callback inert");
                Need(completedSeatCases == 16 && buttonCallbacks == 11 && visits == 10 && realtime.PublicJoins == 9 && realtime.PrivateJoins == 0 && realtime.Leaves == 0 && realtime.Disconnects == 0 && transport.Mutations == 0 && errors == 0, "Fixture command or ownership boundary failed.");
                receipt["hud_visibility_pass"] = ((JArray)receipt["hud_capture_checks"]).Count == 22 && ((JArray)receipt["hud_capture_checks"]).All(x => (bool?)x["pass"] == true);
                Need((bool)receipt["hud_visibility_pass"], "All 22 actual gameplay captures require visible owned HUD.");
                receipt["pass"] = true;
            }
            catch (Exception e) { receipt["failure"] = e.GetType().Name + ": " + e.Message; throw; }
            finally
            {
                receipt["phase"] = phase;
                async Task Clean(Func<Task> action) { try { await action(); } catch (Exception e) { ((JArray)receipt["cleanup_errors"]).Add(e.GetType().Name + ": " + e.Message); receipt["pass"] = false; } }
                await Clean(async () => { if (host != null) { Object.Destroy(host); host = null; await Frames(2, CancellationToken.None); } });
                await Clean(async () => { if (tookOwnership && !ReferenceEquals(VenueSession.Current, null)) await VenueSession.Current.Exit(); });
                for (int i = saved.Count - 1; i >= 0; i--) { try { saved[i].Dispose(); } catch (Exception e) { ((JArray)receipt["cleanup_errors"]).Add(e.Message); receipt["pass"] = false; } }
                VenueLoader.ReviewPrefabResolver = resolver;
                if (catalog != null && oldVenues != null) catalog.venues = oldVenues;
                bool restoredServices = restored.All(f => f());
                bool restoredCatalog = catalog == null || oldVenues == null || (ReferenceEquals(catalog.venues, oldVenues) && JsonUtility.ToJson(catalog) == catalogJson && UnityEditor.EditorUtility.IsDirty(catalog) == catalogDirty);
                receipt["services_restored"] = restoredServices; receipt["catalog_memory_restored"] = restoredCatalog;
                receipt["resolver_restored"] = ReferenceEquals(VenueLoader.ReviewPrefabResolver, resolver);
                bool originalReloaded = !tookOwnership && ReferenceEquals(VenueSession.Current, original) && original.IsActive;
                if (tookOwnership && !owner.IsCancellationRequested) await Clean(async () => { var visit = await VenueSession.Enter(originalId, joinRoom: false, owner: owner); originalReloaded = visit.IsActive; });
                receipt["original_venue_reloaded"] = originalReloaded;
                Application.targetFrameRate = oldFrameRate; QualitySettings.vSyncCount = oldVsync; Application.runInBackground = oldBackground;
                var drift = new JArray(); foreach (var pin in pins) if (!File.Exists(Resolve(pin.Key)) || Sha(Resolve(pin.Key)) != pin.Value) drift.Add(pin.Key);
                receipt["pinned_file_drift"] = drift; receipt["protected_scope"] = "Pinned stage outputs, runtime inputs, exact Home and catalog only; root launcher owns whole-project post-exit audit.";
                receipt["errors"] = errors; receipt["button_callbacks"] = buttonCallbacks; receipt["visit_count"] = visits; receipt["completed_seat_cases"] = completedSeatCases; receipt["injected_fixture_seat_confirmations"] = ((JArray)receipt["seat_cases"]).Count(row => (bool?)row["fixture_confirmation_injected"] == true);
                receipt["public_join_attempts"] = realtime?.PublicJoins ?? 0; receipt["public_join_ids"] = realtime != null ? JArray.FromObject(realtime.JoinIds) : new JArray();
                receipt["private_join_attempts"] = realtime?.PrivateJoins ?? 0; receipt["transport_mutations"] = transport?.Mutations ?? 0;
                receipt["held_joins"] = realtime?.HeldJoins ?? 0; receipt["cancelled_joins"] = realtime?.CancelledJoins ?? 0;
                bool clean = restoredServices && restoredCatalog && drift.Count == 0 && ((JArray)receipt["cleanup_errors"]).Count == 0 && (owner.IsCancellationRequested || originalReloaded) && errors == 0;
                receipt["cleanup_complete"] = clean; receipt["pass"] = (bool)receipt["pass"] && clean;
                Application.logMessageReceived -= LogMessage;
                File.WriteAllText(Path.Combine(directory, "saved-venue-flow-review.json"), receipt.ToString(Formatting.Indented));
                Need(clean, "Saved-room review cleanup did not restore captured owners/files.");
            }
        }
        private static JObject ReadManifest(string path, Dictionary<string, string> pins, JObject receipt)
        {
            Need(!string.IsNullOrWhiteSpace(path), "Supply -ctlife-reviewSavedVenueManifest; unfilled template is deliberately blocked.");
            path = Resolve(path); var proof = JObject.Parse(File.ReadAllText(path));
            Need((string)proof["revision"] == "saved-venue-flow-inputs-v1", "Wrong saved-room input schema.");
            JObject ReadPinned(JToken row)
            {
                string p = (string)row?["path"], hash = (string)row?["sha256"];
                Need(!string.IsNullOrEmpty(p) && hash?.Length == 64 && Sha(Resolve(p)) == hash, "Missing or changed exact receipt: " + p);
                pins[p] = hash; return JObject.Parse(File.ReadAllText(Resolve(p)));
            }
            var stage = ReadPinned(proof["stage_receipt"]);
            Need((string)stage["revision"] == "persistent-playable-venues-v1" && (string)stage["status"] == "persisted_profiles_unregistered_requires_actual_saved_runtime_review" &&
                (int?)stage["route_count"] == 42 && (int?)stage["motion_assets"] == 6 && (int?)stage["derived_controllers"] == 2 && (bool?)stage["art_changed"] == false, "Incomplete persistent stage.");
            foreach (var file in ((JObject)stage["assets"]).Properties())
            {
                Need(file.Name.StartsWith(Content + "/", StringComparison.Ordinal) && !file.Name.Contains(".."), "Stage asset is outside isolated output.");
                string hash = (string)file.Value; Need(Sha(Resolve(file.Name)) == hash, "Stage asset changed: " + file.Name); pins[file.Name] = hash;
            }
            foreach (string id in new[] { "terminal_cafe", "spaces_studio" })
            {
                string hash = (string)proof["prefabs"]?[id];
                Need(hash?.Length == 64 && hash == (string)stage["assets"]?[Content + "/" + id + ".prefab"], "Future saved prefab pin missing or different: " + id);
            }
            var sources = ReadPinned(proof["controller_proof"]); Need((bool?)sources["all_pass"] == true && ((JArray)sources["protected_drift"]).Count == 0, "Original controller proof incomplete.");
            Need(proof["runtime_files"] is JObject runtime && runtime.Count >= 7, "Runtime pins must be supplied.");
            foreach (var file in ((JObject)proof["runtime_files"]).Properties())
            { string hash = (string)file.Value; Need(hash?.Length == 64 && Sha(Resolve(file.Name)) == hash, "Runtime source changed: " + file.Name); pins[file.Name] = hash; }
            pins[path] = Sha(path); receipt["input_manifest_sha256"] = pins[path]; receipt["stage_receipt_sha256"] = (string)proof["stage_receipt"]["sha256"];
            receipt["prefabs"] = proof["prefabs"].DeepClone(); receipt["stage_assets"] = stage["assets"].DeepClone();
            // Keep source proof in memory only for exact look identities, not mutable state overrides.
            proof["resolved_controller_proof"] = sources; return proof;
        }
        private static void CheckPersistedProfile(VenueSceneRoot room, string id, JObject proof, JObject receipt)
        {
            Need(room != null && room.venueId == id && room.navigation != null && !room.IsGreybox, "Incomplete saved room: " + id);
            var profile = room.GetComponent<CafeSeatMotionProfile>(); Need(profile != null, "Saved room lacks its persistent profile.");
            var bindings = Field<CafeSeatMotionProfile.Binding[]>(profile, "bindings");
            var routes = Field<CafeSeatMotionProfile.ExitRoute[]>(profile, "seatExits");
            string[] expected = id == "terminal_cafe" ? Enumerable.Range(1, 8).Select(n => "terminal_" + n).Concat(Enumerable.Range(1, 4).Select(n => "table_" + n)).ToArray() :
                Enumerable.Range(1, 20).Select(n => "seat_" + n).Concat(Enumerable.Range(1, 10).Select(n => "stage_" + n)).ToArray();
            Need(bindings.Length == 2 && routes.Length == expected.Length && routes.Select(x => x.spotId).OrderBy(x => x).SequenceEqual(expected.OrderBy(x => x)), "Persisted route set differs: " + id);
            var anchors = room.GetComponentsInChildren<SpotAnchor>(true); var rows = new JArray();
            foreach (var route in routes)
            {
                var anchor = anchors.Single(a => a.spotId == route.spotId);
                Need(anchor.approach != null && route.points.Length == 1 && Vector3.Distance(room.transform.TransformPoint(route.points[0]), anchor.approach.position) < .00001f, "Persistent route differs from exact authored approach: " + route.spotId);
                Need(anchor.anim == (route.spotId.StartsWith("terminal_", StringComparison.Ordinal) ? "act_type" : "act_sit"), "Seat action differs.");
                rows.Add(new JObject { ["spot"] = route.spotId, ["action"] = anchor.anim, ["endpoint"] = V(route.points[0]) });
            }
            foreach (string look in new[] { "snow", "rain" })
            {
                var source = proof["resolved_controller_proof"]["cases"].Single(x => (string)x["character"] == look);
                var baseline = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>((string)source["baseline_controller_asset"]);
                var avatar = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>((string)source["world_prefab_asset"]).GetComponentsInChildren<Animator>(true).Single().avatar;
                var row = bindings.Single(b => b.baselineController == baseline && b.ownAvatar == avatar);
                Need(UnityEditor.AssetDatabase.GetAssetPath(row.cafeController) == Content + "/Controllers/" + look + ".controller", "Binding does not use exact saved derived controller.");
                void Motion(CafeWorkExitMotion motion, string kind, bool required)
                {
                    if (!required) { Need(motion == null, "Unexpected unused motion binding: " + kind); return; }
                    Need(motion != null && motion.Valid && UnityEditor.AssetDatabase.GetAssetPath(motion) == Content + "/Motion/" + look + "-" + kind + ".asset", "Motion binding is missing, transient or wrong: " + kind);
                    Need(motion.settleContact == (look == "snow"), "Wrong per-look settle ownership.");
                    Vector3 first = kind == "work" ? new Vector3(.45f, 0, -.05f) : new Vector3(kind == "social-left" ? -.375f : .375f, 0, -.05f);
                    Vector3 second = kind == "work" ? new Vector3(.30f, 0, -.02f) : new Vector3(kind == "social-left" ? -.375f : .375f, 0, 0);
                    Need(motion.firstStep == first && motion.secondStep == second, "Signed persisted step schema differs.");
                }
                Motion(row.workExitMotion, "work", id == "terminal_cafe"); Motion(row.socialRightExitMotion, "social-right", true); Motion(row.socialLeftExitMotion, "social-left", id == "spaces_studio");
            }
            var profiles = receipt["persisted_profiles"] as JObject ?? new JObject(); receipt["persisted_profiles"] = profiles; profiles[id] = rows;
        }
        private static void CheckSelectedLook(LocalPlayerController local, string look, JObject proof)
        {
            var view = local.GetComponent<AvatarView>(); var source = proof["resolved_controller_proof"]["cases"].Single(x => (string)x["character"] == look);
            Need(view != null && view.Animator != null && UnityEditor.AssetDatabase.GetAssetPath(view.Animator.runtimeAnimatorController) == Content + "/Controllers/" + look + ".controller" &&
                UnityEditor.AssetDatabase.GetAssetPath(view.Animator.avatar) == (string)source["own_avatar_asset"] && view.Animator.avatar.name == (string)source["own_avatar_name"], "Actual normal Flow player did not resolve the persisted " + look + " binding.");
            var venue = VenueSession.Current.Venue; var profile = venue.GetComponent<CafeSeatMotionProfile>();
            var routes = Field<CafeSeatMotionProfile.ExitRoute[]>(profile, "seatExits");
            foreach (var route in routes)
            {
                var anchor = venue.Anchors.Single(x => x.spotId == route.spotId);
                object[] args = { anchor.anim, venue.WireSpotId(route.spotId), view.Animator, null, null };
                Need((bool)Invoke(profile, "TryGetExit", args), "Actual profile rejects persisted route/look: " + look + "/" + route.spotId);
                var points = (Vector3[])args[3]; var motion = (CafeWorkExitMotion)args[4];
                string kind = route.spotId.StartsWith("terminal_", StringComparison.Ordinal) ? "work" : venue.venueId == "spaces_studio" && route.spotId.StartsWith("stage_", StringComparison.Ordinal) ? "social-left" : "social-right";
                Need(UnityEditor.AssetDatabase.GetAssetPath(motion) == Content + "/Motion/" + look + "-" + kind + ".asset" && Vector3.Distance(points.Last(), anchor.approach.position) < .00001f, "Actual runtime selected wrong saved motion/route.");
            }
        }
        private static void CheckRoom(VenueSession visit, VenueSceneRoot expected, List<int> baselineScenes, LightmapData[] baselineMaps, JObject receipt)
        {
            Need(visit != null && visit.IsActive && expected != null && visit.Venue.venueId == expected.venueId && !visit.Venue.IsGreybox, "Wrong actual saved visit.");
            Need(visit.Channel == null && Field<VoiceService>(visit, "_voice") == null, "Offline fixture acquired network/RTC ownership.");
            Need(visit.Venue.navigation == expected.navigation && visit.LocalPlayer != null && visit.LocalPlayer.GetComponent<NavMeshAgent>().isOnNavMesh && InputSubscriptions() == 1, "Actual nav/player/input owner differs.");
            Need(Scenes().Except(baselineScenes).SequenceEqual(new[] { (int)visit.Venue.gameObject.scene.handle }) && baselineScenes.All(Scenes().Contains), "Unexpected loaded room scene ownership.");
            var expectedMaps = expected.GetComponent<VenueLightmaps>(); var maps = visit.Venue.GetComponent<VenueLightmaps>(); var actual = LightmapSettings.lightmaps;
            Need(maps != null && maps.colors.Length > 0 && maps.colors.SequenceEqual(expectedMaps.colors) && actual.Length == baselineMaps.Length + maps.colors.Length, "Saved lightmap count/source mismatch.");
            for (int i = 0; i < baselineMaps.Length; i++) Need(actual[i].lightmapColor == baselineMaps[i].lightmapColor, "Baseline map changed.");
            for (int i = 0; i < maps.colors.Length; i++) Need(actual[baselineMaps.Length + i].lightmapColor == maps.colors[i], "Wrong saved map slot.");
            foreach (var binding in maps.bindings) Need(binding.renderer.lightmapIndex == baselineMaps.Length + binding.lightmap && binding.renderer.lightmapScaleOffset == binding.scaleOffset, "Saved renderer binding differs.");
            foreach (var anchor in expected.GetComponentsInChildren<SpotAnchor>(true))
                Need(visit.Venue.TryGetSpot(anchor.spotId, out var p, out var yaw) && Vector3.Distance(p, anchor.transform.position) < .00001f && Mathf.Abs(yaw - anchor.RotY) < .00001f, "Immutable anchor differs: " + anchor.spotId);
            int routes = 0;
            var profile = visit.Venue.GetComponent<CafeSeatMotionProfile>();
            if (profile != null) foreach (var route in Field<CafeSeatMotionProfile.ExitRoute[]>(profile, "seatExits"))
            {
                Need(visit.Venue.TryGetSpotApproach(route.spotId, out var goal), "Route approach missing.");
                var path = new NavMeshPath();
                Need(NavMesh.SamplePosition(visit.Venue.EntrancePosition, out var start, .75f, NavMesh.AllAreas) && NavMesh.SamplePosition(goal, out var finish, .35f, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(start.position, finish.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete, "Saved nav does not reach " + route.spotId); routes++;
            }
            ((JArray)receipt["visits"]).Add(new JObject { ["venue"] = visit.VenueId, ["home"] = visit.HomeId, ["scene"] = (int)visit.Venue.gameObject.scene.handle,
                ["maps"] = maps.colors.Length, ["bindings"] = maps.bindings.Length, ["complete_route_paths"] = routes, ["input_owners"] = InputSubscriptions() });
        }
        private static async Task SeatCase(LocalPlayerController local, string spot, string look, int repeat, string directory, JObject receipt, FlowController flow, PhoneShell phone, CancellationToken ct)
        {
            var venue = VenueSession.Current.Venue; var view = local.GetComponent<AvatarView>(); var agent = local.GetComponent<NavMeshAgent>();
            string label = look + "-" + venue.venueId + "-" + spot + "-repeat" + repeat;
            string wire = venue.WireSpotId(spot); int sits = 0, moves = 0, stands = 0;
            void Command(string command) { if (command == "sit:" + wire) sits++; else if (command == "move") moves++; else if (command == "stand") stands++; }
            var record = new JObject { ["label"] = label, ["fixture_confirmation_injected"] = false, ["pass"] = false };
            var approachRows = new JArray(); var exitRows = new JArray(); ((JArray)receipt["seat_cases"]).Add(record);
            local.ReviewCommandObserved += Command;
            try
            {
                Need(string.IsNullOrEmpty(local.SeatedSpot) && !view.SeatExitActive, "Seat case starts free: " + label);
                Need(local.GoSit(wire), "Actual GoSit rejected approach: " + label);
                double began = Time.realtimeSinceStartupAsDouble; float beganPlay = Time.time;
                approachRows.Add(NavRow(local, began));
                while (sits == 0 && Time.realtimeSinceStartupAsDouble - began < 18)
                { ct.ThrowIfCancellationRequested(); await Awaitable.NextFrameAsync(ct); approachRows.Add(NavRow(local, began)); }
                record["arrival_wall_seconds"] = Time.realtimeSinceStartupAsDouble - began; record["arrival_play_seconds"] = Time.time - beganPlay;
                record["sit_intents"] = sits; record["arrival_final"] = NavRow(local, began); record["arrival_frames"] = approachRows.Count;
                var recheck = new NavMeshPath(); bool calculated = agent.CalculatePath(Field<Vector3>(local, "_destination"), recheck);
                record["arrival_recheck"] = new JObject { ["calculated"] = calculated, ["status"] = recheck.status.ToString(), ["corners"] = new JArray(recheck.corners.Select(V)) };
                Need(sits == 1 && string.IsNullOrEmpty(local.SeatedSpot), "Exactly one real arrival intent before any fixture confirmation: " + label);
                Need(venue.TryGetSpotApproach(spot, out var approach) && Xz(agent.nextPosition, approach) <= .1f, "Intent emitted away from authored standing approach.");
                Need(venue.TryGetSpot(spot, out var seat, out var yaw), "Exact occupied anchor missing.");
                // Explicit test packet, never a successful mock JoinVenue or claimed server decision.
                local.OnServerSeat(wire, "act_sit"); record["fixture_confirmation_injected"] = true;
                Need(local.SeatedSpot == wire && Vector3.Distance(local.transform.position, seat) < .00001f && !agent.enabled, "Injected confirmation did not enter exact seat.");
                string fitted = spot.StartsWith("terminal_", StringComparison.Ordinal) ? CafeSeatMotionProfile.TypingState : CafeSeatMotionProfile.SocialState;
                await Until(() => !view.Animator.IsInTransition(0) && view.Animator.GetCurrentAnimatorStateInfo(0).IsName(fitted), ct, 8, "Persisted fitted state " + label);
                float initialTime = view.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime; await PlaySeconds(.18f, ct);
                Need(view.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime > initialTime, "Actual fitted animation did not advance.");
                int seatedMoves = moves; await Frames(3, ct); Need(moves == seatedMoves, "Movement intent published while seated.");
                if (repeat == 0) await Capture(directory, label + "-seated", receipt, flow, phone, ct);
                local.StandIfSeated();
                Need(view.SeatExitActive && agent.enabled && agent.isOnNavMesh && string.IsNullOrEmpty(local.SeatedSpot), "Actual stand did not start saved exit profile.");
                Need(Vector3.Distance(view.PresentationPosition, seat) < .00001f, "Visible body snapped away in stand call.");
                var logical = local.transform.position; double exitStart = Time.realtimeSinceStartupAsDouble; float exitPlay = Time.time;
                exitRows.Add(ExitRow(view, exitStart));
                while (view.SeatExitActive && Time.realtimeSinceStartupAsDouble - exitStart < 10)
                { await Awaitable.NextFrameAsync(ct); exitRows.Add(ExitRow(view, exitStart)); }
                record["exit_wall_seconds"] = Time.realtimeSinceStartupAsDouble - exitStart; record["exit_play_seconds"] = Time.time - exitPlay;
                Need(!view.SeatExitActive && Vector3.Distance(view.PresentationPosition, local.transform.position) < .001f && Vector3.Distance(logical, local.transform.position) < .001f && stands == 1,
                    "Saved exit failed to reconcile one stand-only local owner: " + label);
                var settle = Field<object>(view, "_exitSettleContact");
                if (settle != null) Need(settle.GetType().GetProperty("Failure")?.GetValue(settle) == null, "Exit contact driver reported failure.");
                Need(exitRows.Count >= 4 && exitRows.Any(r => (bool)r["active"] && Vector3.Distance(new Vector3((float)r["presentation"][0], (float)r["presentation"][1], (float)r["presentation"][2]), seat) > .1f), "No actual multi-frame emergence travel was observed.");
                await PlaySeconds(.12f, ct);
                if (repeat == 0) await Capture(directory, label + "-stood", receipt, flow, phone, ct);
                record["controller"] = UnityEditor.AssetDatabase.GetAssetPath(view.Animator.runtimeAnimatorController);
                record["fitted_state"] = fitted; record["normalized_time_before"] = initialTime; record["pass"] = true;
                record["limits"] = "Saved-profile functional routing, real approach and visible stand ownership; no repeated triangle/sole/remote/Android acceptance.";
            }
            finally
            {
                local.ReviewCommandObserved -= Command;
                File.WriteAllText(Path.Combine(directory, label + "-arrival.json"), approachRows.ToString(Formatting.Indented));
                File.WriteAllText(Path.Combine(directory, label + "-exit.json"), exitRows.ToString(Formatting.Indented));
                record["arrival_trace"] = label + "-arrival.json"; record["exit_trace"] = label + "-exit.json";
            }
        }
        private static async Task ArrivalPreservationReview(LocalPlayerController local, string directory, JObject receipt, CancellationToken ct)
        {
            var venue = VenueSession.Current.Venue; var intents = new List<string>(); var submissions = new List<string>();
            void Observe(string command) { if (command.StartsWith("sit:", StringComparison.Ordinal)) intents.Add(command); if (command.StartsWith("path-assigned:", StringComparison.Ordinal)) submissions.Add(command); }
            local.ReviewCommandObserved += Observe;
            var evidence = new JObject { ["pass"] = false, ["trials"] = new JArray(),
                ["scope"] = "Only real GoSit/CancelMovement/arrival plus deliberately early private ArriveAtDestination calls. No agent property injection. Observed consume-before-validation behavior is corrected; earlier intermittent failure's cause remains unknown." };
            receipt["arrival_preservation_review"] = evidence;
            string Wire(string spot) => "sit:" + venue.WireSpotId(spot);
            JObject Early(string label)
            {
                double time = Time.realtimeSinceStartupAsDouble; var before = NavRow(local, time);
                bool hasDestination = Field<bool>(local, "_hasDestination"); string pending = Field<string>(local, "_pendingSpot"); var destination = Field<Vector3>(local, "_destination");
                Need(hasDestination && pending != null && (float)before["distance_to_destination"] > .3f, "Early control must be outside arrival radius: " + label);
                int count = intents.Count; bool arrived = (bool)Invoke(local, "ArriveAtDestination");
                var trial = new JObject { ["label"] = label, ["before"] = before, ["after"] = NavRow(local, time), ["returned"] = arrived };
                ((JArray)evidence["trials"]).Add(trial);
                Need(!arrived && intents.Count == count && Field<bool>(local, "_hasDestination") == hasDestination && Field<string>(local, "_pendingSpot") == pending && Field<Vector3>(local, "_destination") == destination,
                    "Rejected early arrival consumed/replaced an accepted request: " + label);
                trial["retained_pending_request"] = true; return trial;
            }
            async Task Arrival(string spot, string label, int before)
            {
                double began = Time.realtimeSinceStartupAsDouble; var rows = new JArray { NavRow(local, began) };
                try
                {
                    while (intents.Count == before && Time.realtimeSinceStartupAsDouble - began < 18)
                    { await Awaitable.NextFrameAsync(ct); rows.Add(NavRow(local, began)); }
                    Need(intents.Count == before + 1 && intents.Last() == Wire(spot) && string.IsNullOrEmpty(local.SeatedSpot), "Actual retained/newest approach must dispatch exactly once: " + label);
                    Need(!Field<bool>(local, "_hasDestination") && Field<string>(local, "_pendingSpot") == null, "Validated arrival did not consume its request.");
                    Need(venue.TryGetSpotApproach(spot, out var approach) && Xz(local.GetComponent<NavMeshAgent>().nextPosition, approach) <= .1f, "Retained intent dispatched away from exact approach.");
                    int once = intents.Count; Invoke(local, "ArriveAtDestination"); Invoke(local, "ArriveAtDestination"); await Frames(2, ct);
                    Need(intents.Count == once, "Repeated valid arrival emitted a duplicate seat request.");
                    ((JArray)evidence["trials"]).Add(new JObject { ["label"] = label, ["actual_arrival_once"] = true, ["duplicate_calls_emit_nothing"] = true, ["final"] = NavRow(local, began), ["trace"] = "arrival-control-" + label + ".json" });
                }
                finally { File.WriteAllText(Path.Combine(directory, "arrival-control-" + label + ".json"), rows.ToString(Formatting.Indented)); }
            }
            try
            {
                Need(local.GoSit("terminal_1"), "Retained-request control could not start."); Early("retained-first");
                await Arrival("terminal_1", "retained-first", 0);
                int beforeCancelledSubmission = submissions.Count;
                Need(local.GoSit("terminal_2"), "Cancellation control could not start."); Early("before-cancel");
                local.CancelMovement(); int afterCancel = intents.Count;
                Need(!Field<bool>(local, "_hasDestination") && Field<string>(local, "_pendingSpot") == null, "Cancellation did not release queued intent.");
                Invoke(local, "ArriveAtDestination"); await Frames(2, ct);
                Need(intents.Count == afterCancel && submissions.Count == beforeCancelledSubmission && !Field<bool>(local, "_pathSubmissionQueued") && string.IsNullOrEmpty(local.SeatedSpot), "Cancelled intent reached native submission or dispatched after early retry.");
                ((JArray)evidence["trials"]).Add(new JObject { ["label"] = "cancel", ["pending_cleared"] = true, ["no_dispatch"] = true });
                int submittedBefore = submissions.Count; int replaceFrame = Time.frameCount;
                var replacePosition = local.transform.position;
                Need(local.GoSit("terminal_2") && local.GoSit("terminal_3"), "Newer-target control could not replace destination.");
                Need(submissions.Count == submittedBefore && Field<bool>(local, "_pathSubmissionQueued"), "Repeated same-frame target submitted before actual Update.");
                Need(Field<string>(local, "_pendingSpot") == "terminal_3", "Newest spot does not own destination."); Early("newer-target");
                await Arrival("terminal_3", "newer-target", afterCancel);
                Need(submissions.Count == submittedBefore + 1 && submissions.Last().Contains("spot=terminal_3;") && Vector3.Distance(local.transform.position, replacePosition) > 1f,
                    "Latest target must submit once and actually move; no stale native request.");
                evidence["latest_update_submission"] = new JObject { ["pass"] = true, ["request_frame"] = replaceFrame, ["submission"] = submissions.Last(), ["submission_count"] = submissions.Count - submittedBefore,
                    ["movement_m"] = Vector3.Distance(local.transform.position, replacePosition) };
                Need(!intents.Contains(Wire("terminal_2")), "Superseded/cancelled target dispatched.");
                int immediate = intents.Count; Need(local.GoSit("terminal_3"), "Validated immediate-arrival branch rejected.");
                Need(intents.Count == immediate + 1 && intents.Last() == Wire("terminal_3") && !Field<bool>(local, "_hasDestination") && Field<string>(local, "_pendingSpot") == null,
                    "Immediate validated arrival did not dispatch once and consume fields.");
                Invoke(local, "ArriveAtDestination"); await Frames(2, ct); Need(intents.Count == immediate + 1, "Immediate-arrival repeated callback duplicated claim.");
                evidence["immediate_positive_control"] = true; evidence["pass"] = true;
                evidence["not_exercised"] = "An immediate preflight PathComplete followed synchronously by a failed fresh path check requires an actual changed path; this fixture does not inject NavMesh state to fabricate that branch. Rejected-immediate cleanup is source-reviewed only.";
            }
            finally
            {
                evidence["observed_sit_intents"] = JArray.FromObject(intents); evidence["native_submissions"] = JArray.FromObject(submissions);
                local.CancelMovement(); local.ReviewCommandObserved -= Observe;
            }
        }
        private static JObject NavRow(LocalPlayerController local, double start)
        {
            var a = local.GetComponent<NavMeshAgent>(); var destination = Field<Vector3>(local, "_destination");
            return new JObject { ["frame"] = Time.frameCount, ["wall_seconds"] = Time.realtimeSinceStartupAsDouble - start,
                ["position"] = V(local.transform.position), ["next_position"] = V(a.nextPosition), ["velocity"] = V(a.velocity), ["desired_velocity"] = V(a.desiredVelocity),
                ["destination"] = V(destination), ["distance_to_destination"] = Xz(a.nextPosition, destination), ["remaining"] = float.IsFinite(a.remainingDistance) ? (JToken)a.remainingDistance : a.remainingDistance.ToString(),
                ["has_path"] = a.hasPath, ["path_pending"] = a.pathPending, ["path_status"] = a.pathStatus.ToString(), ["is_stopped"] = a.isStopped,
                ["has_destination"] = Field<bool>(local, "_hasDestination"), ["submission_queued"] = Field<bool>(local, "_pathSubmissionQueued"), ["pending_spot"] = Field<string>(local, "_pendingSpot"), ["app_focused"] = Application.isFocused };
        }
        private static JObject ExitRow(AvatarView view, double start) => new JObject
        {
            ["frame"] = Time.frameCount, ["wall_seconds"] = Time.realtimeSinceStartupAsDouble - start, ["stage"] = view.SeatExitStage,
            ["active"] = view.SeatExitActive, ["root"] = V(view.transform.position), ["presentation"] = V(view.PresentationPosition),
            ["state"] = view.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash, ["normalized_time"] = view.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime
        };
        private static void CheckEmpty(List<int> scenes, LightmapData[] maps)
        {
            Need(ReferenceEquals(VenueSession.Current, null) && SameScenes(scenes) && InputSubscriptions() == 0, "Cancelled travel retained a scene/session/input.");
            var actual = LightmapSettings.lightmaps; Need(actual.Length == maps.Length, "Cancelled travel retained baked map slots.");
            for (int i = 0; i < maps.Length; i++) Need(actual[i].lightmapColor == maps[i].lightmapColor, "Cancelled travel changed baseline maps.");
        }
        private static string LookKey(string look, JObject proof) => (string)proof["resolved_controller_proof"]["cases"].Single(x => (string)x["character"] == look)["look_key"];
        private static Me Clone(Me source, string key)
        {
            Need(source?.Character?.Look != null, "Complete profile template required."); var c = source.Character; var l = key.Split('|'); Need(l.Length == 7, "Exact full look key required.");
            return new Me(new User(Guid.Parse("00000000-0000-4000-8000-000000000090"), "isolated-review", false, true),
                new Character("Saved-room review", new Look(l[0], l[1], l[2], l[3], l[4], l[5], l[6]), c.TribeId, new List<string>(c.TraitIds), c.DreamId),
                new Needs(70, 70, 70, 70, 70), new Skills(1, 1, 1, 1, 1, 1), 1000, 0, "parents_basement", flags: new Dictionary<string, object> { { "lotteryRolled", true } });
        }
        private static int InputSubscriptions() => ((Delegate)typeof(WorldInput).GetField("SpotTapped", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))?.GetInvocationList().Count(d => d.Target is VenueSession) ?? 0;
        private static List<int> Scenes() { var rows = new List<int>(); for (int i = 0; i < SceneManager.sceneCount; i++) rows.Add(SceneManager.GetSceneAt(i).handle); return rows.OrderBy(x => x).ToList(); }
        private static bool SameScenes(List<int> scenes) => Scenes().SequenceEqual(scenes);
        private static Button Button(PhoneShell phone, string name) { var b = Field<VisualElement>(phone, "_body").Q<Button>(name); Need(b != null && b.panel != null, "Actual phone button missing: " + name); return b; }
        private static void Submit(Button button) { using var e = NavigationSubmitEvent.GetPooled(); e.target = button; button.SendEvent(e); }
        private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        private static float Xz(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        private static JArray V(Vector3 v) => new JArray(v.x, v.y, v.z);
        private static async Task Until(Func<bool> predicate, CancellationToken ct, double seconds, string label)
        { double end = Time.realtimeSinceStartupAsDouble + seconds; while (!predicate()) { ct.ThrowIfCancellationRequested(); Need(Time.realtimeSinceStartupAsDouble < end, "Bounded native predicate timed out: " + label); await Awaitable.NextFrameAsync(ct); } }
        private static async Task Frames(int count, CancellationToken ct) { for (int i = 0; i < count; i++) await Awaitable.NextFrameAsync(ct); }
        private static async Task PlaySeconds(float seconds, CancellationToken ct)
        { float end = Time.time + seconds; int first = Time.frameCount; await Until(() => Time.time >= end && Time.frameCount - first >= 3, ct, 10, "Actual Play advancement"); }
        private static async Task Capture(string directory, string name, JObject receipt, FlowController flow, PhoneShell phone, CancellationToken ct)
        {
            await Awaitable.EndOfFrameAsync(ct);
            var witness = UiTrace(flow, phone, receipt, "capture-" + name); witness["capture"] = name;
            var hud = Field<Hud>(flow, "_hud"); bool visible = hud != null && !phone.IsOpen && Visible(hud.Element) &&
                Visible(hud.Element.Q<VisualElement>(className: "ct-hud-top")) && Visible(hud.Element.Q<VisualElement>(className: "ct-hud-social")) &&
                hud.Element.worldBound.width > 100f && hud.Element.worldBound.height > 100f;
            witness["pass"] = visible; ((JArray)receipt["hud_capture_checks"]).Add(witness.DeepClone());
            // Preserve the failing actual image and exact UI states before asserting; never force-show a layer.
            var image = ScreenCapture.CaptureScreenshotAsTexture(); Need(image != null, "Native screenshot missing.");
            try { File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG()); ((JArray)receipt["captures"]).Add(name + ".png"); } finally { Object.Destroy(image); }
            Need(visible, "Actual gameplay HUD is hidden/detached/outside layout at " + name + "; see UI state witness.");
        }
        private static bool Visible(VisualElement element)
        {
            if (element == null || element.panel == null || !float.IsFinite(element.worldBound.width) || !float.IsFinite(element.worldBound.height) || element.worldBound.width <= 0f || element.worldBound.height <= 0f) return false;
            for (var p = element; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.visibility != Visibility.Visible || p.resolvedStyle.opacity <= .001f) return false;
            return true;
        }
        private static JObject UiElement(VisualElement element)
        {
            if (element == null) return new JObject { ["missing"] = true };
            var r = element.worldBound;
            return new JObject { ["name"] = element.name, ["classes"] = new JArray(element.GetClasses()), ["attached"] = element.panel != null,
                ["inline_display_value"] = element.style.display.value.ToString(), ["inline_display_keyword"] = element.style.display.keyword.ToString(),
                ["display"] = element.resolvedStyle.display.ToString(), ["visibility"] = element.resolvedStyle.visibility.ToString(), ["opacity"] = element.resolvedStyle.opacity,
                ["bounds"] = new JArray(r.x, r.y, r.width, r.height), ["children"] = element.childCount };
        }
        private static JObject UiTrace(FlowController flow, PhoneShell phone, JObject receipt, string label)
        {
            var hud = flow != null ? Field<Hud>(flow, "_hud") : null; var ui = UiRoot.Instance;
            var ancestors = new JArray(); for (var p = hud?.Element; p != null; p = p.parent) ancestors.Add(UiElement(p));
            var previous = phone != null ? Field<StyleEnum<DisplayStyle>>(phone, "_previousHudDisplay") : default;
            var row = new JObject { ["label"] = label, ["frame"] = Time.frameCount, ["owned_hud"] = UiElement(hud?.Element), ["owned_hud_controls"] = hud != null ? new JArray(hud.Element.Children().Select(UiElement)) : new JArray(),
                ["hud_ancestors"] = ancestors, ["hud_layer"] = UiElement(ui?.HudLayer), ["hud_children"] = ui != null ? new JArray(ui.HudLayer.Children().Select(UiElement)) : new JArray(),
                ["phone_open"] = phone?.IsOpen ?? false, ["phone_hidden_hud"] = UiElement(phone != null ? Field<VisualElement>(phone, "_hiddenHud") : null),
                ["phone_previous_hud_value"] = previous.value.ToString(), ["phone_previous_hud_keyword"] = previous.keyword.ToString(),
                ["screens"] = ui != null ? new JArray(ui.ScreenLayer.Children().Select(UiElement)) : new JArray(),
                ["phones"] = ui != null ? new JArray(ui.PhoneLayer.Children().Select(UiElement)) : new JArray() };
            ((JArray)receipt["ui_trace"]).Add(row); return row;
        }
        private static string Resolve(string path)
        {
            Need(!string.IsNullOrWhiteSpace(path), "Missing exact input path.");
            if (Path.IsPathRooted(path)) return path;
            if (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)) return Path.GetFullPath(path);
            return Path.GetFullPath(Path.Combine(Application.dataPath, "../..", path));
        }
        private static string Sha(string path) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException("Saved-room Flow review: " + message); }
    }
}
#endif
