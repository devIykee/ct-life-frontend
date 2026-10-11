#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.Rest.Model;
using CTLife.UI;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.App.Review
{
    /// <summary>Actual overlay/Bus/UiRoot lifetime review with an isolated in-memory transport; no HTTP or wallet calls.</summary>
    public static class OverlayOwnershipReview
    {
        private const string PlayerA = "00000000-0000-4000-8000-000000000081";
        private const string PlayerB = "00000000-0000-4000-8000-000000000082";
        private static readonly Type[] SignalTypes = { typeof(ToastSignal), typeof(EventMessage), typeof(BubbleSignal), typeof(LiquidatedMessage), typeof(AvatarTappedSignal), typeof(ServerBusySignal) };
        [Serializable] private sealed class Receipt
        {
            public bool pass, servicesRestored, uiRootRestored;
            public string phase, failure;
            public int reads, localBlocks, localReports, unexpectedRequests, runtimeErrors, nativeOwnerCycles;
            public string limits = "Actual UI Toolkit callbacks, panel detach, real Bus and captured UiRoot updater ownership; actual MonoBehaviour destruction with controllers disabled before Start and owned handles injected. Local controlled transport only: no HTTP, persisted credentials, wallet, real moderation/economic requests, normal authentication or room startup. Per-sheet duplicate prevention is not target-wide idempotency. Physical touch, backend cancellation and Android process lifetime remain separate acceptance.";
            public List<string> checks = new List<string>(), captures = new List<string>();
        }
        private sealed class Saved<T> : IDisposable where T : class
        {
            private readonly T original;
            private readonly bool existed;
            public Saved(T replacement) { existed = Services.TryGet(out original); Services.Register(replacement); }
            public void Dispose() { if (existed) Services.Register(original); else Services.Unregister<T>(); }
            public bool Restored() { bool exists = Services.TryGet<T>(out var current); return existed ? exists && ReferenceEquals(current, original) : !exists; }
        }
        private sealed class Read
        {
            public readonly string Id;
            public readonly CancellationToken Token;
            public readonly TaskCompletionSource<PublicProfile> Completion = new TaskCompletionSource<PublicProfile>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Read(string id, CancellationToken token) { Id = id; Token = token; }
            public void Complete(string alias) => Completion.TrySetResult(Profile(Id, alias));
        }
        private sealed class Write
        {
            public readonly string Path, Body;
            public readonly CancellationToken Token;
            public readonly TaskCompletionSource<NoContent> Completion = new TaskCompletionSource<NoContent>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Write(string path, string body, CancellationToken token) { Path = path; Body = body; Token = token; }
            public void Complete() => Completion.TrySetResult(default);
        }
        private sealed class Transport : IRestClient, IDisposable
        {
            public Func<Task<bool>> RefreshHandler { get; set; }
            public readonly List<Read> Reads = new List<Read>();
            public readonly List<Write> Writes = new List<Write>();
            public int Unexpected;
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                if (typeof(T) != typeof(PublicProfile) || (path != "/users/" + PlayerA && path != "/users/" + PlayerB)) return Reject<T>();
                ct.ThrowIfCancellationRequested();
                var pending = new Read(path.Substring("/users/".Length), ct); Reads.Add(pending);
                // Intentionally non-cooperative: late completion tests require the view to reject stale data itself.
                return Cast<T, PublicProfile>(pending.Completion.Task);
            }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default)
            {
                if (typeof(T) != typeof(NoContent) || !auth || idempotencyKey != null || ct.CanBeCanceled) return Reject<T>();
                if (path == "/blocks/" + PlayerA || path == "/blocks/" + PlayerB) { if (body != null) return Reject<T>(); }
                else if (path == "/reports")
                {
                    var value = JObject.Parse(body as string ?? "{}");
                    if ((string)value["targetType"] != "user" || ((string)value["targetId"] != PlayerA && (string)value["targetId"] != PlayerB) ||
                        string.IsNullOrEmpty((string)value["reasonId"]) || value.Properties().Count() != 3) return Reject<T>();
                }
                else return Reject<T>();
                var pending = new Write(path, body as string, ct); Writes.Add(pending);
                return Cast<T, NoContent>(pending.Completion.Task);
            }
            private static async Task<T> Cast<T, U>(Task<U> task) => (T)(object)await task;
            private Task<T> Reject<T>() { Unexpected++; return Task.FromException<T>(new InvalidOperationException("Overlay fixture rejected an unexpected request.")); }
            public Task<T> Put<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Patch<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Delete<T>(string path, CancellationToken ct = default) => Reject<T>();
            public Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Reject<T>();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => Reject<RawResponse>();
            public void Dispose()
            {
                foreach (var read in Reads) read.Complete("REVIEW CLEANUP");
                foreach (var write in Writes) write.Complete();
            }
        }
        public static async Task Run(PhoneShell originalPhone, string directory, CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Path.IsPathRooted(directory)) directory = Path.Combine(Application.persistentDataPath, directory);
#endif
            directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner); deadline.CancelAfter(TimeSpan.FromSeconds(45));
            var ct = deadline.Token; var receipt = new Receipt(); var ui = UiRoot.Instance;
            var disposables = new List<IDisposable>(); var objects = new List<GameObject>();
            VisualElement host = null; Transport transport = null;
            Saved<Session> savedSession = null; Saved<ConfigStore> savedConfig = null; Saved<Api> savedApi = null;
            Action removeToastObserver = null; int observedToasts = 0;
            var resolver = VenueLoader.ReviewPrefabResolver;
            void OnLog(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) receipt.runtimeErrors++; }
            Application.logMessageReceived += OnLog;
            try
            {
                receipt.phase = "construct"; originalPhone.Close();
                var snapshot = Resources.Load<TextAsset>("Contract/config_snapshot"); Need(snapshot != null, "Bundled config missing");
                var config = new ConfigStore(); config.Apply(snapshot.text, null, persist: false);
                var session = new Session(); Identity(session, PlayerA); transport = new Transport();
                savedSession = new Saved<Session>(session); savedConfig = new Saved<ConfigStore>(config); savedApi = new Saved<Api>(new Api(transport, config));
                // Execute the actual generated required-field constructors before starting asynchronous fixture work.
                Need(Profile(PlayerA, "Review profile").Look != null, "Complete generated fixture missing");
                host = El.Div(); host.name = "overlay-review-host";
                host.style.position = UnityEngine.UIElements.Position.Absolute; host.style.left = 0; host.style.right = 0; host.style.top = 0; host.style.bottom = 0;
                host.pickingMode = PickingMode.Ignore; ui.ModalLayer.Add(host);
                var watermark = El.Text("REVIEW · LOCAL OVERLAY LIFETIMES · NO NETWORK", "ct-small");
                watermark.style.position = UnityEngine.UIElements.Position.Absolute; watermark.style.top = 8; watermark.style.left = 24; watermark.pickingMode = PickingMode.Ignore;
                var modal = El.Div(); modal.StretchToParentSize(); modal.pickingMode = PickingMode.Ignore;
                var labels = El.Div(); labels.StretchToParentSize(); labels.pickingMode = PickingMode.Ignore;
                var toasts = El.Div(); toasts.StretchToParentSize(); toasts.pickingMode = PickingMode.Ignore;
                host.Add(labels); host.Add(modal); host.Add(toasts); host.Add(watermark);
                var sentinel = El.Div(); sentinel.name = "unrelated-view-sentinel"; modal.Add(sentinel);
                removeToastObserver = Bus.Subscribe<ToastSignal>(_ => observedToasts++);
                var baseline = Counts(); var initialUpdates = Updates(ui).ToArray(); int initialSession = SessionCount(session);
                int initialIncidents = ui.HudLayer.Query<VisualElement>("city-incidents").ToList().Count;
                var toast = new ToastLayer(toasts); var world = new WorldLabels(labels); var rekt = new RektCard(modal); var profile = new ProfileCard(modal); var busy = new BusyOverlay(toasts);
                disposables.AddRange(new IDisposable[] { toast, world, rekt, profile, busy });
                Need(Counts().SequenceEqual(baseline.Select(n => n + 1)), "Six overlay Bus subscriptions not registered exactly once");
                Need(Updates(ui).Count == initialUpdates.Length + 4 && SessionCount(session) == initialSession + 2, "Updater/session ownership delta incorrect");
                Need(ui.HudLayer.Query<VisualElement>("city-incidents").ToList().Count == initialIncidents + 1, "Incident root not mounted");
                receipt.phase = "profile-read-replacement";
                Tap(PlayerA, "REVIEW A"); Need(transport.Reads.Count == 1, "Avatar callback did not start one GET");
                var first = transport.Reads[0]; var oldSheet = modal.Q(className: "ct-sheet");
                var oldTitle = oldSheet.Q<Label>(className: "ct-h1"); var child = El.Text("temporary child", "ct-small"); oldSheet.Add(child);
                await Frames(2, ct); child.RemoveFromHierarchy(); Need(!first.Token.IsCancellationRequested, "Child detach cancelled profile GET");
                Tap(PlayerB, "REVIEW B"); Need(first.Token.IsCancellationRequested && transport.Reads.Count == 2, "Replacing sheet did not cancel only old GET");
                first.Complete("STALE RESPONSE"); transport.Reads[1].Complete("REVIEW B LOADED");
                await Until(() => modal.Query<Label>().ToList().Any(x => x.text == "REVIEW B LOADED"), 3, ct, "current profile completion");
                Need(oldTitle.text == "REVIEW A", "Late response wrote detached old profile");
                Need(!modal.Query<Label>().ToList().Any(x => x.text == "STALE RESPONSE"), "Late profile replaced current sheet");
                await Capture(directory, "profile-current", receipt, ct);
                receipt.checks.Add("PASS actual avatar Bus callback, child detach, cancelled superseded GET and non-cooperative late response rejection.");

                receipt.phase = "stale-block";
                Tap(PlayerA, "REVIEW BLOCK ORIGIN"); var block = modal.Q<Button>("profile-block");
                Submit(block); Submit(block); Need(transport.Writes.Count == 1 && !block.enabledSelf, "Repeated Block callback dispatched more than one request");
                var pendingBlock = transport.Writes[0]; Need(!pendingBlock.Token.CanBeCanceled, "Started Block lifetime changed");
                Tap(PlayerB, "REVIEW NEWER PROFILE"); var newer = modal.Q(className: "ct-sheet"); int beforeToast = observedToasts;
                pendingBlock.Complete(); await Frames(5, ct);
                Need(newer.parent != null && ReferenceEquals(newer, modal.Q(className: "ct-sheet")) && observedToasts == beforeToast, "Old Block closed/toasted newer view");
                Submit(block); Need(transport.Writes.Count == 1, "Detached Block callback dispatched a new request");
                receipt.checks.Add("PASS one local Block per sheet, duplicate/stale callback guards, original uncancelled request token, completion cannot close/toast a newer profile.");

                receipt.phase = "stale-report";
                Submit(newer.Q<Button>("profile-report")); var reportSheet = modal.Q(className: "ct-sheet");
                var reasons = reportSheet.Query<Button>().ToList().Where(x => x.name.StartsWith("profile-reason-", StringComparison.Ordinal)).ToList();
                Need(reasons.Count >= 2, "Report reason controls missing");
                await Capture(directory, "report-reasons", receipt, ct);
                if (reportSheet is ScrollView reportScroll)
                {
                    reportScroll.ScrollTo(reportSheet.Query<Button>().ToList().Last());
                    await Frames(3, ct);
                    await Capture(directory, "report-reasons-bottom", receipt, ct);
                }
                Submit(reasons[0]); Submit(reasons[1]);
                Need(transport.Writes.Count == 2 && reasons.All(x => !x.enabledSelf), "Report reason callbacks did not share one in-flight gate");
                var pendingReport = transport.Writes[1]; var body = JObject.Parse(pendingReport.Body);
                Need(pendingReport.Path == "/reports" && (string)body["targetId"] == PlayerB && (string)body["reasonId"] == reasons[0].name.Substring("profile-reason-".Length), "Report target/reason payload changed");
                profile.ShowReport("user", PlayerA, "REVIEW NEWER REPORT"); var laterReport = modal.Q(className: "ct-sheet");
                beforeToast = observedToasts; pendingReport.Complete(); await Frames(5, ct);
                Need(laterReport.parent != null && ReferenceEquals(laterReport, modal.Q(className: "ct-sheet")) && observedToasts == beforeToast, "Old Report closed/toasted newer report");
                Submit(reasons[0]); Need(transport.Writes.Count == 2, "Stale reason callback dispatched again");
                receipt.checks.Add("PASS Report reason controls share a single-flight gate; exact existing payload retained; late completion and old controls cannot change a replacement report.");

                receipt.phase = "session-and-root-detach";
                Tap(PlayerA, "REVIEW IDENTITY"); var identityRead = transport.Reads.Last();
                Identity(session, PlayerB); NotifyIdentity(session);
                Need(identityRead.Token.IsCancellationRequested && modal.Q(className: "ct-sheet") == null, "Identity change retained profile/read");
                identityRead.Complete("STALE IDENTITY"); await Frames(3, ct);
                Tap(PlayerA, "REVIEW DETACH"); var detachedRead = transport.Reads.Last();
                modal.Q(className: "ct-sheet").parent.RemoveFromHierarchy(); Need(detachedRead.Token.IsCancellationRequested, "Whole sheet detach did not cancel GET");
                detachedRead.Complete("STALE DETACH"); await Frames(3, ct);
                Tap(PlayerB, "REVIEW DISPOSE"); var disposedRead = transport.Reads.Last();
                Submit(modal.Q<Button>("profile-block")); var disposedWrite = transport.Writes.Last(); int writeCount = transport.Writes.Count;
                profile.Dispose(); profile.Dispose(); Need(disposedRead.Token.IsCancellationRequested, "Dispose did not cancel profile GET");
                beforeToast = observedToasts; disposedRead.Complete("STALE DISPOSE"); disposedWrite.Complete(); await Frames(5, ct);
                profile.ShowReport("user", PlayerA, "MUST NOT OPEN"); Tap(PlayerA, "MUST NOT OPEN");
                Need(modal.Q(className: "ct-sheet") == null && observedToasts == beforeToast && transport.Writes.Count == writeCount, "Disposed profile retained callbacks or late UI");
                receipt.checks.Add("PASS identity change, whole-sheet detach and idempotent disposal cancel only reads; started local mutation completes with no late UI/toast; disposed public entry is inert.");

                receipt.phase = "owned-views-and-captured-root";
                Invoke(toast, "OnToast", ToastSignal.Text("REVIEW OWNED TOAST"));
                var anchor = new GameObject("ReviewBubbleAnchor"); objects.Add(anchor);
                Bus.Publish(new BubbleSignal { anchor = anchor.transform, text = "REVIEW OWNED BUBBLE", seconds = 20 });
                rekt.Show(new LiquidatedMessage { lossCents = 123, coinId = "REVIEW" });
                Need(modal.Q(className: "ct-rekt") != null && labels.Q(className: "ct-bubble") != null && toasts.Q(className: "ct-toast") != null, "Owned views not mounted");
                Bus.Publish(new ServerBusySignal { active = true, retryInSeconds = 2, source = "review" });
                var replacementObject = new GameObject("ReviewInactiveReplacementUi"); replacementObject.SetActive(false); objects.Add(replacementObject);
                var replacementUi = replacementObject.AddComponent<UiRoot>();
                // No frame elapses while the singleton is replaced. The inactive replacement never runs Awake/OnEnable.
                typeof(UiRoot).GetProperty(nameof(UiRoot.Instance)).SetValue(null, replacementUi);
                try { foreach (var overlay in disposables) { overlay.Dispose(); overlay.Dispose(); } }
                finally { typeof(UiRoot).GetProperty(nameof(UiRoot.Instance)).SetValue(null, ui); }
                Need(Updates(ui).SequenceEqual(initialUpdates) && Updates(replacementUi).Count == 0, "Disposal unregistered from replacement UiRoot instead of captured owner");
                Need(Counts().SequenceEqual(baseline) && SessionCount(session) == initialSession, "Overlay Bus/session subscriptions did not return exactly to baseline");
                Need(ui.HudLayer.Query<VisualElement>("city-incidents").ToList().Count == initialIncidents, "Disposed Incident root remains mounted");
                Need(modal.childCount == 1 && ReferenceEquals(modal[0], sentinel) && labels.childCount == 0 && toasts.childCount == 0, "Disposal leaked own views or removed unrelated sentinel");
                disposables.Clear(); receipt.checks.Add("PASS all six exact Bus deltas, four updater deltas, two session subscriptions, captured-old-UiRoot cleanup, Incident root removal and unrelated-view preservation.");

                receipt.phase = "whole-layer-detach";
                var detachedLayer = El.Div(); host.Add(detachedLayer); var detachedToast = new ToastLayer(detachedLayer); var detachedWorld = new WorldLabels(detachedLayer); var detachedProfile = new ProfileCard(detachedLayer); var detachedRekt = new RektCard(detachedLayer); var detachedBusy = new BusyOverlay(detachedLayer);
                disposables.AddRange(new IDisposable[] { detachedToast, detachedWorld, detachedProfile, detachedRekt, detachedBusy });
                await Frames(2, ct); detachedLayer.RemoveFromHierarchy();
                Need(Counts().SequenceEqual(baseline) && Updates(ui).SequenceEqual(initialUpdates) && SessionCount(session) == initialSession, "Whole layer detach left live overlay owners");
                foreach (var overlay in disposables) overlay.Dispose(); disposables.Clear();
                receipt.checks.Add("PASS actual native whole-layer DetachFromPanelEvent releases overlay owners; repeated disposal remains harmless.");

                receipt.phase = "native-controller-teardown";
                for (int kind = 0; kind < 3; kind++)
                {
                    var go = new GameObject("ReviewOverlayOwner-" + kind); objects.Add(go);
                    if (kind == 0)
                    {
                        var flow = go.AddComponent<FlowController>(); flow.enabled = false;
                        Field<List<IDisposable>>(flow, "_overlays").AddRange(new IDisposable[] { new ToastLayer(toasts), new WorldLabels(labels), new RektCard(modal), new ProfileCard(modal), new BusyOverlay(toasts) });
                    }
                    else if (kind == 1)
                    {
                        var review = go.AddComponent<FrontendReviewController>(); review.enabled = false;
                        Set(review, "_toasts", new ToastLayer(toasts));
                    }
                    else
                    {
                        var spike = go.AddComponent<SpikeController>(); spike.enabled = false; Set(spike, "_ui", ui); Invoke(spike, "BuildOverlay");
                        Field<List<IDisposable>>(spike, "_overlays").AddRange(new IDisposable[] { new WorldLabels(labels), new ToastLayer(toasts), new BusyOverlay(toasts) });
                    }
                    UnityEngine.Object.Destroy(go); await Frames(3, ct); VenueLoader.ReviewPrefabResolver = resolver;
                    Need(go == null && Counts().SequenceEqual(baseline) && Updates(ui).SequenceEqual(initialUpdates) && SessionCount(session) == initialSession, "Native owner destruction leaked overlay resources: " + kind);
                    Need(ui.HudLayer.Query<VisualElement>("city-incidents").ToList().Count == initialIncidents, "Owner destruction retained Incident root: " + kind);
                    receipt.nativeOwnerCycles++;
                }
                receipt.checks.Add("PASS actual Flow, FrontendReview and Spike OnDestroy cleanup with owned handles; Spike panel updater removed. Start/auth/room workflows intentionally not executed.");
                Need(transport.Unexpected == 0 && receipt.runtimeErrors == 0, "Unexpected transport/native failure");
                await Capture(directory, "overlay-cleanup-complete", receipt, ct); receipt.pass = true;
                Log.Info("overlay-review", "PASS owned overlays, stale profile responses, exact subscriptions/updaters and three native controller teardowns. Local transport only.");
            }
            catch (Exception error)
            {
                receipt.failure = error.Message;
                try { await Capture(directory, "overlay-failure-" + receipt.phase, receipt, CancellationToken.None); } catch { }
                throw;
            }
            finally
            {
                typeof(UiRoot).GetProperty(nameof(UiRoot.Instance)).SetValue(null, ui); VenueLoader.ReviewPrefabResolver = resolver;
                foreach (var disposable in disposables) disposable.Dispose();
                foreach (var go in objects) if (go != null) UnityEngine.Object.Destroy(go);
                // Flush native OnDestroy before restoring services/resolver; review teardown itself clears the resolver.
                await Frames(2, CancellationToken.None);
                VenueLoader.ReviewPrefabResolver = resolver;
                host?.RemoveFromHierarchy(); removeToastObserver?.Invoke(); transport?.Dispose();
                savedApi?.Dispose(); savedConfig?.Dispose(); savedSession?.Dispose();
                receipt.servicesRestored = (savedApi?.Restored() ?? true) && (savedConfig?.Restored() ?? true) && (savedSession?.Restored() ?? true);
                receipt.uiRootRestored = ReferenceEquals(UiRoot.Instance, ui);
                receipt.reads = transport?.Reads.Count ?? 0; receipt.localBlocks = transport?.Writes.Count(x => x.Path.StartsWith("/blocks/", StringComparison.Ordinal)) ?? 0;
                receipt.localReports = transport?.Writes.Count(x => x.Path == "/reports") ?? 0; receipt.unexpectedRequests = transport?.Unexpected ?? 0;
                if (!receipt.servicesRestored || !receipt.uiRootRestored) { receipt.pass = false; receipt.failure = "Original services/UI owner not restored."; }
                Application.logMessageReceived -= OnLog;
                File.WriteAllText(Path.Combine(directory, "overlay-ownership-review.json"), JsonUtility.ToJson(receipt, true));
                Need(receipt.servicesRestored && receipt.uiRootRestored, "Original services/UI owner not restored");
            }
        }
        private static PublicProfile Profile(string id, string alias) => new PublicProfile(Guid.Parse(id), alias, "", false, "trencher", PublicProfile.MinerTierEnum.None, new Look("skin_01", "head_01", "hair_01", "top_01", "bottom_01", "shoes_01", "acc_none"), 12345);
        private static void Identity(Session session, string id)
        {
            // SetTokens/Clear would persist PlayerPrefs. Only private in-memory fields are changed in this isolated fixture.
            typeof(Session).GetProperty(nameof(Session.UserId)).SetValue(session, id);
            typeof(Session).GetProperty(nameof(Session.AccessToken)).SetValue(session, "isolated-review-not-a-token");
        }
        private static void NotifyIdentity(Session session) => ((Action)typeof(Session).GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session))?.Invoke();
        private static int SessionCount(Session session) => ((Action)typeof(Session).GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session))?.GetInvocationList().Length ?? 0;
        private static int[] Counts()
        {
            var handlers = (Dictionary<Type, Delegate>)typeof(Bus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            return SignalTypes.Select(type => handlers.TryGetValue(type, out var entry) ? entry.GetInvocationList().Length : 0).ToArray();
        }
        private static List<Action> Updates(UiRoot ui) => Field<List<Action>>(ui, "_updaters");
        private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private static object Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, args);
        private static void Tap(string id, string alias) => Bus.Publish(new AvatarTappedSignal { userId = id, displayName = alias });
        private static void Submit(Button button) { Need(button != null, "Missing callback target"); using (var e = NavigationSubmitEvent.GetPooled()) { e.target = button; button.SendEvent(e); } }
        private static async Task Frames(int count, CancellationToken ct) { for (int i = 0; i < count; i++) await Awaitable.NextFrameAsync(ct); }
        private static async Task Until(Func<bool> predicate, double seconds, CancellationToken ct, string phase)
        { double until = Time.realtimeSinceStartupAsDouble + seconds; while (!predicate()) { if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("Overlay native predicate timed out: " + phase); await Awaitable.NextFrameAsync(ct); } }
        private static async Task Capture(string directory, string name, Receipt receipt, CancellationToken ct)
        { await Awaitable.EndOfFrameAsync(ct); var texture = ScreenCapture.CaptureScreenshotAsTexture(); Need(texture != null, "Native screenshot missing"); try { File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG()); receipt.captures.Add(name + ".png"); } finally { UnityEngine.Object.Destroy(texture); } }
        private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException("Overlay ownership review: " + message); }
    }
}
#endif
