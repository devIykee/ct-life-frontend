using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.SocialFeatures;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Duo iOS redesign wave 1 (`-ctlife-captureSet duoios`): Home, Messages inbox and thread, Settings at Large and
    /// XXL, and a notification banner over the real lobby. Shipping PhoneShell, PhoneCatalog, MessagesApp View,
    /// SettingsApp, ToastLayer/DuoBannerLayer and the real realtime DM path (MessagesController.OnRealtime). REST is an
    /// in-memory read-only fixture (writes rejected); a REVIEW FIXTURE mark stays on screen.
    /// </summary>
    internal static class DuoIosCapture
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Kemi = "review-duo-kemi", Hiro = "review-duo-hiro", Cabal = "review-duo-cabal", Amara = "review-duo-amara", Fresh = "review-duo-fresh";

        private sealed class MessagesFixtureApp : PhoneApp
        {
            private readonly SocialHub _hub;
            private object _view;
            public string Peer;
            public ScrollView Root { get; private set; }
            public VisualElement Frame { get; private set; }
            public MessagesFixtureApp(SocialHub hub) { _hub = hub; }
            public override string Id => "messages";
            public override string Title => SocialStrings.T("dm.title");
            public override DuoChrome Chrome => DuoChrome.Plain;
            public override VisualElement Open()
            {
                Closed();
                var type = typeof(SocialHub).Assembly.GetType("CTLife.SocialFeatures.MessagesApp+View", true);
                _view = Activator.CreateInstance(type, Instance, null, new object[] { _hub, Phone, Peer, null }, null);
                Root = (ScrollView)type.BaseType.GetField("Root", Instance).GetValue(_view);
                Frame = (VisualElement)type.GetProperty("Container", Instance).GetValue(_view);
                return Frame;
            }
            public override void Closed() { (_view as IDisposable)?.Dispose(); _view = null; Root = null; Frame = null; }
        }

        private sealed class Rest : IRestClient
        {
            public string Me;
            public int Writes;
            public readonly List<string> Reads = new List<string>();
            public Func<Task<bool>> RefreshHandler { get; set; }
            private static long Ago(double minutes) => (long)(ServerClock.NowMs - minutes * 60000);
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested(); Reads.Add(path);
                JToken value;
                if (path == "/dms") value = new JObject { ["threads"] = new JArray(
                    Row(Kemi, "kemi.sol", "yo did you see the new coin in the Pit", 70),
                    Row(Hiro, "Hiro", "Sent you $250 · for the alpha, don't tell anyone", 88),
                    Row(Cabal, "Builders' Cabal", "Lucía: ship it tonight, we review in the Space at 9", 60 * 26),
                    Row(Amara, "Amara", "gm you still holding that bag?", 60 * 24 * 3),
                    Row(Fresh, "NewPlayer_88", "hey can you check my wallet real quick", 60 * 5)) };
                else if (path.StartsWith("/dms/", StringComparison.Ordinal) && path.Contains(Kemi)) value = new JObject { ["messages"] = new JArray(
                    Msg("k1", Kemi, Me, "yo did you see the new coin in the Pit", 72),
                    Msg("k2", Kemi, Me, "it's giving rug but the chart is pretty", 71.5),
                    Msg("k3", Me, Kemi, "i'm in for $50. purely for research", 70),
                    Msg("rt-kemi", Kemi, Me, "pull up to the Perp Pit, SOL is ripping and I need someone to fade me", 0.33)) };
                else if (path.StartsWith("/dms/", StringComparison.Ordinal)) value = new JObject { ["messages"] = new JArray() };
                else if (path.StartsWith("/users/", StringComparison.Ordinal)) value = new JObject { ["userId"] = path.Substring(7), ["alias"] = path.Substring(7) };
                else return Task.FromException<T>(new ApiException(404, "NOT_FOUND", "No review fixture for " + path));
                return Task.FromResult(value is T exact ? exact : value.ToObject<T>(Newtonsoft.Json.JsonSerializer.Create(Json.Settings)));
            }
            private static JObject Row(string id, string alias, string last, double minutes) =>
                new JObject { ["userId"] = id, ["alias"] = alias, ["lastMessage"] = last, ["lastAt"] = Ago(minutes) };
            private static JObject Msg(string id, string from, string to, string text, double minutes) =>
                new JObject { ["messageId"] = id, ["fromUserId"] = from, ["toUserId"] = to, ["text"] = text, ["at"] = Ago(minutes) };
            private Task<T> Reject<T>() { Writes++; return Task.FromException<T>(new InvalidOperationException("Duo capture rejected a write.")); }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Reject<T>();
            public Task<T> Put<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Patch<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Delete<T>(string path, CancellationToken ct = default) => Reject<T>();
            public Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => method == "GET" ? Get<T>(path, ct) : Reject<T>();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => Reject<RawResponse>();
        }

        public static Task Run(Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JObject receipt)
        {
            var report = new JObject { ["scope"] = "REVIEW FIXTURE: shipping Duo shell, catalog apps, Messages View, Settings, banners; in-memory read-only REST; realtime DMs injected through MessagesController.OnRealtime." };
            receipt["duoIos"] = report;
            return LobbyFlowCapture.Run(shot, wait, expect, step, receipt, phone => Duo(phone, shot, wait, expect, step, report));
        }

        private static async Task Duo(PhoneShell catalogPhone, Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JObject report)
        {
            var savedPrefs = DuoPrefs.Store;
            DuoPrefs.Store = new DuoPrefs.Memory(); // never touch the developer's own Duo settings
            bool hadRest = Services.TryGet<IRestClient>(out var previousRest);
            var rest = new Rest { Me = "duo-review-" + Guid.NewGuid().ToString("N") };
            SocialHub hub = null;
            PhoneShell phone = null;
            ToastLayer toasts = null;
            var mark = El.Text("REVIEW FIXTURE · DUO WAVE 1");
            mark.pickingMode = PickingMode.Ignore;
            mark.style.position = Position.Absolute; mark.style.left = 36; mark.style.bottom = 12;
            mark.style.fontSize = 18; mark.style.color = new Color(.24f, .24f, .3f); mark.style.backgroundColor = Color.white;
            UiRoot.Instance.Root.Add(mark);
            try
            {
                Services.Register<IRestClient>(rest);
                hub = (SocialHub)Activator.CreateInstance(typeof(SocialHub), Instance, null, new object[] { rest.Me }, null);
                foreach (var (id, alias) in new[] { (Kemi, "kemi.sol"), (Hiro, "Hiro"), (Cabal, "Builders' Cabal"), (Amara, "Amara"), (Fresh, "NewPlayer_88") })
                {
                    hub.Names.Remember(id, alias);
                    if (id != Fresh) hub.Follows.Set(id, true);
                }
                toasts = new ToastLayer(UiRoot.Instance.ToastLayer);
                void SyncBadge() => DuoNotificationCenter.SetBadge("messages", hub.Messages.Unread);
                hub.Changed += SyncBadge;
                hub.Messages.RefreshInbox();
                await wait(.4f);

                // Real realtime DMs (the same path as Channels.cs `dm`): unread dots, badges, banners.
                hub.Messages.OnRealtime(new DmMessage { messageId = "rt-hiro", fromUserId = Hiro, text = "Sent you $250 · for the alpha, don't tell anyone", at = ServerClock.NowMs - 60000 });
                await wait(.2f);
                hub.Messages.OnRealtime(new DmMessage { messageId = "rt-kemi", fromUserId = Kemi, text = "pull up to the Perp Pit, SOL is ripping and I need someone to fade me", at = ServerClock.NowMs - 20000 });
                SyncBadge();
                await wait(.5f);
                expect(DuoNotificationCenter.Queue.Stack.Count == 2, "two banners stacked from two realtime DMs");
                expect(DuoNotificationCenter.Badge("messages") == 2, "Messages badge counts the two unread conversations' messages");
                await shot("duo-wave1-banner-lobby-stacked", .3f);
                var front = UiRoot.Instance.ToastLayer.Q("duo-banner");
                expect(front != null && front.worldBound.width > 600, "front banner rendered top-centre");
                report["bannerRect"] = front == null ? null : new JArray(front.worldBound.x, front.worldBound.y, front.worldBound.width, front.worldBound.height);
                DuoNotificationCenter.Queue.Dismiss(DuoNotificationCenter.Queue.Stack[1].Id, Time.unscaledTime);
                await shot("duo-wave1-banner-lobby", .4f);

                foreach (var size in new[] { DuoTextSize.L, DuoTextSize.XXL })
                {
                    string tag = size == DuoTextSize.L ? "L" : "XXL";
                    DuoText.Set(size);
                    DuoNotificationCenter.Queue.Clear();
                    step("Duo " + tag);
                    catalogPhone.Open();
                    await shot("duo-wave1-home-" + tag, .9f);
                    var badge = UiRoot.Instance.PhoneLayer.Q<Label>("badge-messages");
                    expect(badge != null && badge.resolvedStyle.display == DisplayStyle.Flex && badge.text == "2", tag + ": Messages icon shows the unread badge");
                    catalogPhone.Open("settings");
                    await shot("duo-wave1-settings-" + tag, .8f);
                    expect(UiRoot.Instance.PhoneLayer.Q("settings-text-size") != null, tag + ": Settings shows the text size slider");
                    catalogPhone.Close();
                }

                // Messages on a shell whose Messages app is the shipping View over the fixture hub.
                catalogPhone.Dispose();
                phone = new PhoneShell(UiRoot.Instance.PhoneLayer);
                var messages = new MessagesFixtureApp(hub);
                phone.Register(messages);
                foreach (var size in new[] { DuoTextSize.L, DuoTextSize.XXL })
                {
                    string tag = size == DuoTextSize.L ? "L" : "XXL";
                    DuoText.Set(size);
                    messages.Peer = null;
                    phone.Open("messages");
                    await shot("duo-wave1-messages-" + tag, .9f);
                    expect(messages.Frame.Q("messages-row-" + Hiro)?.ClassListContains("duo-conv-unread") == true, tag + ": unread dot on Hiro");
                    expect(DuoNotificationCenter.Queue.Post(new DuoNotification { AppId = "messages", ThreadKey = Amara, Title = "Amara", Body = "x" }, Time.unscaledTime)
                        == DuoPostResult.SuppressedForeground, tag + ": no banner while the inbox is in front");
                    messages.Peer = Kemi;
                    phone.Open("messages");
                    await wait(.6f);
                    messages.Root.scrollOffset = new Vector2(0, 99999);
                    await shot("duo-wave1-thread-" + tag, .4f);
                    expect(DuoNotificationCenter.Badge("messages") == 1, tag + ": opening kemi.sol's chat clears its unread from the badge");
                    var tails = messages.Frame.Query(className: "duo-bubble-tail").ToList().Count;
                    var bubbles = messages.Frame.Query(className: "duo-bubble").ToList().Count;
                    expect(bubbles == 4 && tails == 3, tag + ": 4 bubbles in 3 runs → 3 tails (got " + bubbles + "/" + tails + ")");
                    expect(messages.Frame.Query(className: "duo-thread-time").ToList().Count == 2, tag + ": two centred times (start, then a > 15 min gap)");
                    var input = messages.Frame.Q<TextField>("messages-input");
                    var send = messages.Frame.Q<Button>("social-common-send");
                    expect(send.resolvedStyle.display == DisplayStyle.None, tag + ": no send arrow while empty");
                    if (size == DuoTextSize.L)
                    {
                        input.value = "bet. omw";
                        await shot("duo-wave1-thread-typing-L", .3f);
                        expect(send.resolvedStyle.display == DisplayStyle.Flex, "send arrow appears inside the pill with text");
                        input.value = new string('x', 470);
                        await wait(.2f);
                        expect(messages.Frame.Q<Label>("messages-input-error").text.Contains("30"), "inline count near the 500 limit");
                        input.value = "";
                    }
                    // Banner while another chat is open does show (iOS).
                    expect(DuoNotificationCenter.Queue.Post(new DuoNotification { AppId = "messages", ThreadKey = Amara, Title = "Amara", Body = "x" }, Time.unscaledTime)
                        == DuoPostResult.Shown, tag + ": a different conversation banners while a chat is open");
                    DuoNotificationCenter.Queue.Clear();
                    phone.Close();
                }
                report["reads"] = new JArray(rest.Reads.Distinct());
                report["writes"] = rest.Writes;
                expect(rest.Writes == 0, "Duo capture made zero writes");
                hub.Changed -= SyncBadge;
            }
            finally
            {
                DuoText.Set(DuoTextSize.L);
                DuoNotificationCenter.Reset();
                phone?.Dispose();
                toasts?.Dispose();
                hub?.Dispose();
                mark.RemoveFromHierarchy();
                foreach (string key in new[] { "threads", "follows", "blocks", "creator", "recentCalls", "paidInFlight" })
                    PlayerPrefs.DeleteKey($"ctlife.{Env.Current?.id}.social.{rest.Me}.{key}");
                PlayerPrefs.Save();
                DuoPrefs.Store = savedPrefs;
                if (hadRest) Services.Register<IRestClient>(previousRest); else Services.Unregister<IRestClient>();
            }
        }
    }
}
