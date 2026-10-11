using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.SocialFeatures;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.EditorTools
{
    /// <summary>Editor-only review of the shipping private social Views inside the shipping PhoneShell.
    /// Isolated REST fixtures reject writes; no media room, camera, mic, wallet or account is opened.</summary>
    internal static class DuoSocialCapture
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Peer = "review-social-peer", SecondPeer = "review-social-second";
        private static readonly string LongName = new string('W', 20);

        private static bool FitsOneLine(float available, float measured) => available > 0 && measured > 0 && measured <= available + 1f;

        private static VisualElement DisplayNoneAncestor(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (current.resolvedStyle.display == DisplayStyle.None) return current;
            return null;
        }

        private static void CheckShortLabels(VisualElement root, string view, Action<bool, string> expect, JArray records, JArray hidden)
        {
            // A control can remain inside its card while its text collapses to several lines. Measure the
            // actual resolved font independently of its assigned width, then check the label's bounds too.
            var labels = root.Query<Label>(className: "ct-social-action-label").ToList().Cast<TextElement>()
                .Concat(root.Query<Button>(className: "ct-chip").ToList())
                .Concat(root.Query<Button>(className: "ct-social-back").ToList())
                .Concat(root.Query<VisualElement>(className: "ct-social-post-head").ToList()
                    .SelectMany(head => head.Query<Label>(className: "ct-social-status-copy").ToList()));
            foreach (var label in labels)
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                expect(label.panel != null, view + " short control/tag remains attached: " + label.text);
                if (label.panel == null) continue;
                var hiddenAncestor = DisplayNoneAncestor(label);
                // display does not inherit: a child of a display:none footer still resolves display:flex.
                // Visibility does inherit, so use the label's resolved value (a descendant may override it).
                if (hiddenAncestor != null || label.resolvedStyle.visibility == Visibility.Hidden)
                {
                    hidden.Add(new JObject { ["view"] = view, ["text"] = label.text,
                        ["reason"] = hiddenAncestor != null ? "display:none ancestor" : "resolved visibility:hidden",
                        ["ancestorClasses"] = hiddenAncestor != null ? string.Join(" ", hiddenAncestor.GetClasses()) : null });
                    continue;
                }
                float measured = label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                bool fits = FitsOneLine(label.contentRect.width, measured);
                var container = label is Button ? label.parent : label.GetFirstAncestorOfType<Button>() ?? label.parent;
                bool contained = container != null && label.worldBound.xMin >= container.worldBound.xMin - 1f
                    && label.worldBound.xMax <= container.worldBound.xMax + 1f;
                records.Add(new JObject { ["view"] = view, ["text"] = label.text, ["availableWidth"] = label.contentRect.width,
                    ["singleLineWidth"] = measured, ["fitsOneLine"] = fits, ["contained"] = contained });
                expect(fits, view + " short control/tag fits one line: " + label.text);
                expect(contained, view + " short control/tag remains inside its container: " + label.text);
            }
        }

        private sealed class FixtureApp : PhoneApp
        {
            private readonly string _id, _viewName;
            private readonly SocialHub _hub;
            private object _view;
            public ScrollView Root { get; private set; }
            public bool Thread;
            public override string Id => _id;
            public override DuoChrome Chrome => DuoChrome.Plain;
            public override string Title => SocialStrings.T(_id == "messages" ? "dm.title" : _id == "timeline" ? "feed.title" : _id + ".title");
            public FixtureApp(string id, string viewName, SocialHub hub) { _id = id; _viewName = viewName; _hub = hub; }
            public override VisualElement Open()
            {
                Closed();
                var type = typeof(SocialHub).Assembly.GetType("CTLife.SocialFeatures." + _viewName + "+View", true);
                object[] args = _id == "spaces" ? new object[] { _hub, Phone }
                    : _id == "messages" ? new object[] { _hub, Phone, Thread ? Peer : null, Thread ? LongName : null, false }
                    : new object[] { _hub, Phone, Thread ? Peer : null, Thread ? LongName : null };
                _view = Activator.CreateInstance(type, Instance, null, args, null);
                Root = (ScrollView)type.BaseType.GetField("Root", Instance).GetValue(_view);
                return (VisualElement)type.GetProperty("Container", Instance).GetValue(_view) ?? Root;
            }
            public override void Closed()
            {
                (_view as IDisposable)?.Dispose();
                _view = null; Root = null;
            }
        }

        private sealed class FixtureRest : IRestClient
        {
            public string Me;
            public bool EmptySpaces, FailSpaces;
            public int Writes;
            public readonly List<string> Reads = new List<string>();
            public Func<Task<bool>> RefreshHandler { get; set; }
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested(); Reads.Add(path);
                long now = (long)ServerClock.NowMs;
                JToken value;
                if (path == "/spaces?live=true")
                {
                    if (FailSpaces) return Task.FromException<T>(new ApiException(503, "SERVER_BUSY", "REVIEW fixture unavailable"));
                    value = new JObject { ["spaces"] = EmptySpaces ? new JArray() : new JArray(Space(), new JObject
                    { ["spaceId"] = "review-space-second", ["title"] = "REVIEW · Builders after hours", ["hostId"] = SecondPeer,
                      ["speakerIds"] = new JArray(), ["handIds"] = new JArray(), ["listenerCount"] = 4 }) };
                }
                else if (path.StartsWith("/spaces/", StringComparison.Ordinal)) value = Space();
                else if (path == "/dms") value = new JObject { ["threads"] = new JArray(
                    new JObject { ["userId"] = Peer, ["alias"] = LongName, ["lastMessage"] = "REVIEW · I saved a place for you in the next Space.", ["lastAt"] = now - 60000 },
                    new JObject { ["userId"] = SecondPeer, ["alias"] = "MemeArchivist", ["lastMessage"] = "REVIEW · That chart needs its own support group.", ["lastAt"] = now - 180000 }) };
                else if (path.StartsWith("/dms/", StringComparison.Ordinal)) value = new JObject { ["messages"] = new JArray(
                    new JObject { ["messageId"] = "review-message-1", ["fromUserId"] = Peer, ["toUserId"] = Me,
                        ["text"] = "REVIEW · I saved a place for you. Bring your best thesis and your least convincing chart.", ["at"] = now - 120000 },
                    new JObject { ["messageId"] = "review-message-2", ["fromUserId"] = Me, ["toUserId"] = Peer,
                        ["text"] = "REVIEW · On my way. The chart has already requested legal representation.", ["at"] = now - 60000 }) };
                else if (path.StartsWith("/feed?", StringComparison.Ordinal)) value = new JObject { ["posts"] = new JArray(
                    new JObject { ["postId"] = "review-post-long", ["userId"] = Peer, ["alias"] = LongName,
                        ["text"] = "Day 12: REVIEW fixture · Built the feature, tested the failure path, then touched grass. Keeping receipts before posting the victory thread.", ["at"] = now - 60000 },
                    new JObject { ["postId"] = "review-post-second", ["userId"] = SecondPeer, ["alias"] = "MemeArchivist",
                        ["text"] = "gm. REVIEW fixture · The market can wait until after breakfast.", ["at"] = now - 240000 }) };
                else if (path.StartsWith("/users/", StringComparison.Ordinal)) value = new JObject { ["userId"] = path.Substring(7), ["alias"] = path.EndsWith(Peer, StringComparison.Ordinal) ? LongName : "MemeArchivist" };
                else return Task.FromException<T>(new ApiException(404, "NOT_FOUND", "No review fixture for " + path));
                return Task.FromResult(value is T exact ? exact : value.ToObject<T>(Newtonsoft.Json.JsonSerializer.Create(Json.Settings)));
            }
            public JObject Space() => new JObject { ["spaceId"] = "review-space", ["title"] = "REVIEW · The thesis survived the group chat", ["hostId"] = Peer,
                ["speakerIds"] = new JArray(SecondPeer), ["handIds"] = new JArray(), ["listenerCount"] = 12 };
            private Task<T> Reject<T>() { Writes++; return Task.FromException<T>(new InvalidOperationException("Social capture rejected a write.")); }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Reject<T>();
            public Task<T> Put<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Patch<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Delete<T>(string path, CancellationToken ct = default) => Reject<T>();
            public Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => method == "GET" ? Get<T>(path, ct) : Reject<T>();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => Reject<RawResponse>();
        }

        public static async Task Run(Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JObject receipt)
        {
            bool hadRest = Services.TryGet<IRestClient>(out var previousRest);
            var rest = new FixtureRest { Me = "social-review-" + Guid.NewGuid().ToString("N") };
            var report = new JObject { ["scope"] = "REVIEW FIXTURE: shipping Social Views and PhoneShell; isolated in-memory GET responses, rejected writes, no RTC/media/payment or wallet operations.", ["views"] = new JArray(), ["shortLabelFit"] = new JArray(), ["hiddenShortLabels"] = new JArray() };
            receipt["duoSocial"] = report;
            Services.Register<IRestClient>(rest);
            SocialHub hub = null;
            PhoneShell phone = null;
            var mark = El.Text("REVIEW FIXTURE · SOCIAL APP PRESENTATION");
            mark.pickingMode = PickingMode.Ignore;
            mark.style.position = Position.Absolute; mark.style.left = 36; mark.style.bottom = 32;
            mark.style.fontSize = 23; mark.style.color = new Color(.24f, .24f, .3f); mark.style.backgroundColor = Color.white;
            UiRoot.Instance.Root.Add(mark);
            try
            {
                hub = (SocialHub)Activator.CreateInstance(typeof(SocialHub), Instance, null, new object[] { rest.Me }, null);
                hub.Names.Remember(Peer, LongName); hub.Names.Remember(SecondPeer, "MemeArchivist");
                phone = new PhoneShell(UiRoot.Instance.PhoneLayer);
                var messages = new FixtureApp("messages", "MessagesApp", hub);
                var timeline = new FixtureApp("timeline", "TimelineApp", hub);
                var spaces = new FixtureApp("spaces", "SpacesStudioApp", hub);
                var calls = new FixtureApp("calls", "CallsApp", hub);
                foreach (var app in new[] { messages, timeline, spaces, calls }) phone.Register(app);

                async Task Capture(FixtureApp app, string name, bool bottom = false)
                {
                    step("Duo social " + name);
                    phone.Open(app.Id);
                    await wait(.6f);
                    if (bottom) { app.Root.scrollOffset = new Vector2(0, 99999); await wait(.15f); }
                    await shot("duo-social-" + name, .2f);
                    expect(app.Root != null && app.Root.panel != null, name + " uses the attached shipping view");
                    expect(DisplayNoneAncestor(app.Root) == null && app.Root.resolvedStyle.visibility == Visibility.Visible,
                        name + " shipping view is visible");
                    CheckShortLabels(app.Root, name, expect, (JArray)report["shortLabelFit"], (JArray)report["hiddenShortLabels"]);
                    ((JArray)report["views"]).Add(name);
                }
                await Capture(calls, "calls-empty");
                await Capture(messages, "messages-inbox");
                expect(hub.Messages.Inbox.All.Count() == 2, "the shipping DM parser populated two review conversations");
                var frame = messages.Root.parent;
                expect(ReferenceEquals(DisplayNoneAncestor(frame.Q("social-common-send")), frame.Q(className: "ct-social-footer")),
                    "the inbox intentionally hides Send through its display:none composer footer");
                messages.Thread = true;
                await Capture(messages, "messages-thread", bottom: true);
                var threadFrame = messages.Root.parent;
                expect(threadFrame.Q("social-common-send") != null, "the shipping message composer contains its real Send control");
                expect(threadFrame.Q(className: "ct-social-footer").resolvedStyle.display != DisplayStyle.None,
                    "the thread restores the visible composer (Send itself appears once there is text)");
                await Capture(calls, "calls-contacts");

                var call = CallSession.StartOutgoing(new[] { Peer }, SocialHub.NowMs);
                call.Dialed("review-call", SocialHub.NowMs);
                typeof(CallController).GetProperty(nameof(CallController.Active), Instance).SetValue(hub.Calls, call);
                await Capture(calls, "calls-connecting-controls");
                expect(calls.Root.Q("social-calls-hangup") != null, "the shipping call controls are rendered without opening media");
                typeof(CallController).GetProperty(nameof(CallController.Active), Instance).SetValue(hub.Calls, null);

                await Capture(timeline, "timeline-composer");
                var scopeLabel = timeline.Root.parent.Q<Button>("timeline-tab-global");
                float scopeTextWidth = scopeLabel.MeasureTextSize(scopeLabel.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                expect(!FitsOneLine(20f, scopeTextWidth), "short-label measurement rejects the former collapsed tab width");
                timeline.Root.scrollOffset = new Vector2(0, timeline.Root.Q(className: "ct-social-header").layout.height);
                await shot("duo-social-timeline-long-name", .3f);
                CheckShortLabels(timeline.Root, "timeline-long-name", expect, (JArray)report["shortLabelFit"], (JArray)report["hiddenShortLabels"]);
                var head = timeline.Root.Q(className: "duo-post-head");
                expect(head != null, "the shipping feed parser produced the long-name post");
                if (head != null)
                {
                    foreach (var child in head.Children())
                        expect(child.worldBound.xMin >= head.worldBound.xMin - 1 && child.worldBound.xMax <= head.worldBound.xMax + 1,
                            "long-name post header child remains within its card: " + child.GetType().Name);
                }
                await Capture(spaces, "spaces-list");
                var session = new SpaceSession(rest.Me);
                session.Open("review-space");
                session.ApplySnapshot(SocialParse.Space(rest.Space()), session.BeginFetch(), SocialHub.NowMs);
                session.MediaConnecting();
                typeof(SpacesController).GetProperty(nameof(SpacesController.Session), Instance).SetValue(hub.Spaces, session);
                await Capture(spaces, "space-stage-connecting");
                expect(spaces.Root.Q("social-spaces-request") != null, "stage controls use the actual listener role");
                typeof(SpacesController).GetProperty(nameof(SpacesController.Session), Instance).SetValue(hub.Spaces, null);
                rest.EmptySpaces = true; hub.Spaces.RefreshList();
                await Capture(spaces, "spaces-empty");
                rest.FailSpaces = true; hub.Spaces.RefreshList();
                await Capture(spaces, "spaces-error");
                report["reads"] = new JArray(rest.Reads); report["writes"] = rest.Writes;
                expect(rest.Writes == 0, "social review performed zero writes and zero RTC grant requests");
            }
            finally
            {
                phone?.Dispose(); hub?.Dispose(); mark.RemoveFromHierarchy();
                // A unique review identity owns these temporary local thread summaries. Never touch user keys.
                foreach (string key in new[] { "threads", "follows", "blocks", "creator", "recentCalls", "paidInFlight" })
                    PlayerPrefs.DeleteKey($"ctlife.{Env.Current?.id}.social.{rest.Me}.{key}");
                PlayerPrefs.Save();
                if (hadRest) Services.Register<IRestClient>(previousRest); else Services.Unregister<IRestClient>();
                bool restored = Services.TryGet<IRestClient>(out var current) ? hadRest && ReferenceEquals(current, previousRest) : !hadRest;
                report["restRestored"] = restored;
                expect(restored, "social capture restores the previous REST service");
            }
        }
    }
}
