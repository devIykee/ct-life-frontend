using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.App;
using CTLife.Core;
using CTLife.Net;
using CTLife.SocialFeatures;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Duo iOS redesign wave 2 (`-ctlife-captureSet duoios2`): Wallet, Bank (+ top-up sheet), Perp Pit (+ order sheet),
    /// Jobs (+ details sheet), Timeline (+ compose sheet), Calls (+ in-call), Spaces (+ stage) at Large and XXL, through
    /// the shipping catalog apps and social Views. REST is an in-memory read-only fixture (writes rejected); every
    /// fixture row is labelled REVIEW on screen.
    /// </summary>
    internal static class DuoIos2Capture
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Kemi = "review-duo-kemi", Hiro = "review-duo-hiro", Amara = "review-duo-amara", Lucia = "review-duo-lucia";

        private sealed class SocialFixtureApp : PhoneApp
        {
            private readonly string _id, _view;
            private readonly SocialHub _hub;
            private readonly Func<PhoneShell, object[]> _args;
            private object _instance;
            public VisualElement Frame { get; private set; }
            public ScrollView Root { get; private set; }
            public SocialFixtureApp(string id, string view, SocialHub hub, Func<PhoneShell, object[]> args) { _id = id; _view = view; _hub = hub; _args = args; }
            public override string Id => _id;
            public override string Title => _id;
            public override DuoChrome Chrome => DuoChrome.Plain;
            public override VisualElement Open()
            {
                Closed();
                var type = typeof(SocialHub).Assembly.GetType("CTLife.SocialFeatures." + _view + "+View", true);
                _instance = Activator.CreateInstance(type, Instance, null, _args(Phone), null);
                Root = (ScrollView)type.BaseType.GetField("Root", Instance).GetValue(_instance);
                Frame = (VisualElement)type.GetProperty("Container", Instance).GetValue(_instance);
                return Frame;
            }
            public override void Closed() { (_instance as IDisposable)?.Dispose(); _instance = null; Frame = null; Root = null; }
        }

        private sealed class Rest : IRestClient
        {
            public IRestClient Inner;
            public string Me;
            public int Writes;
            public readonly List<string> Reads = new List<string>();
            public Func<Task<bool>> RefreshHandler { get; set; }
            private static long Ago(double minutes) => (long)(ServerClock.NowMs - minutes * 60000);
            private static string Iso(double minutes) => DateTimeOffset.FromUnixTimeMilliseconds(Ago(minutes)).ToString("o");

            private static JObject Coin(string id, string ticker, string name, long priceMicros, int bps, long mcap) => new JObject
            { ["coinId"] = id, ["ticker"] = ticker, ["name"] = name, ["stage"] = "graduated", ["priceMicros"] = priceMicros,
              ["change5mBps"] = bps, ["mcapCents"] = mcap, ["vol5mCents"] = mcap / 40, ["holders"] = 312 };

            private static JObject Detail(string coinId)
            {
                // REVIEW fixture series: 300 one-minute points, a deterministic walk ending at the coin's price.
                var candles = new JArray();
                double price = 196.0;
                var random = new System.Random(7);
                for (int i = 299; i >= 0; i--)
                {
                    price += 0.062 + (random.NextDouble() - 0.5) * 1.4; // REVIEW fixture: a noisy rise from ~196 to ~214
                    candles.Add(new JObject { ["at"] = Iso(i), ["priceMicros"] = (long)(price * 1_000_000) });
                }
                return new JObject { ["coin"] = Coin(coinId, "FROGE", "Froge", (long)(price * 1_000_000), 444, 1234567800), ["candles1m"] = candles,
                    ["topHolders"] = new JArray(), ["riskHint"] = new JObject { ["level"] = "medium" } };
            }

            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested(); Reads.Add(path);
                JToken value = null;
                if (path.StartsWith("/market/coins/", StringComparison.Ordinal)) value = Detail(path.Substring("/market/coins/".Length));
                else if (path.StartsWith("/market/coins", StringComparison.Ordinal)) value = new JObject { ["coins"] = new JArray(
                    Coin("review-froge", "FROGE", "Froge", 214380000, 444, 1234567800),
                    Coin("review-brrr", "BRRR", "Money Printer", 3412000, -215, 412000000),
                    Coin("review-cope", "COPE", "Copium", 88100, 1032, 98000000)) };
                else if (path == "/perps?status=open") value = new JObject { ["perps"] = new JArray(Perp("review-perp-1", "review-froge", "FROGE", "long", "open", 5, 50000, 205100000, 168200000, null)) };
                else if (path == "/perps") value = new JObject { ["perps"] = new JArray(
                    Perp("review-perp-1", "review-froge", "FROGE", "long", "open", 5, 50000, 205100000, 168200000, null),
                    Perp("review-perp-2", "review-brrr", "BRRR", "short", "closed", 3, 25000, 3620000, 4800000, 11310)) };
                else if (path.StartsWith("/rekt/tombstones", StringComparison.Ordinal)) value = new JObject { ["tombstones"] = new JArray(
                    new JObject { ["userId"] = Lucia, ["alias"] = "lucia.eth", ["coinId"] = "review-brrr", ["ticker"] = "BRRR", ["reason"] = "liquidated", ["lossCents"] = 72000, ["at"] = Iso(18) }) };
                else if (path == "/careers") value = new JObject { ["careers"] = new JArray(new JObject
                    { ["id"] = "discord_mod", ["name"] = "Discord Mod", ["level"] = 3, ["xp"] = 140, ["active"] = true, ["payCents"] = 7000, ["shiftSec"] = 45 }) };
                else if (path == "/gigs") value = new JObject { ["gigs"] = new JArray(
                    new JObject { ["id"] = "review-clip", ["name"] = "Clip Cutter", ["description"] = "REVIEW · cut a 30s clip from a Space", ["durationSec"] = 60, ["cooldownSec"] = 600, ["payCents"] = 5500 },
                    new JObject { ["id"] = "review-raid", ["name"] = "Raid Router", ["description"] = "REVIEW · route a raid to the right timeline", ["durationSec"] = 60, ["cooldownSec"] = 900, ["minPayCents"] = 8000, ["maxPayCents"] = 12000 }) };
                else if (path.StartsWith("/feed?", StringComparison.Ordinal)) value = new JObject { ["posts"] = new JArray(
                    Post("review-p1", Kemi, "kemi.sol", "$FROGE is sending. entry: here. target: moon. stop: my dignity. NFA", 4),
                    Post("review-p2", Hiro, "Hiro", "gm. coffee, charts, no leverage today (lie)", 22),
                    Post("review-p3", Amara, "Amara", "Day 12: shipped the feature, tested the failure path, then touched grass. $COPE holders hold.", 61),
                    Post("review-p4", Lucia, "lucia.eth", "drop your wallet below for a surprise in DMs", 95)) };
                else if (path == "/spaces?live=true") value = new JObject { ["spaces"] = new JArray(Space()) };
                else if (path.StartsWith("/spaces/", StringComparison.Ordinal)) value = Space();
                else if (path == "/dms") value = new JObject { ["threads"] = new JArray() };
                else if (path.StartsWith("/dms/", StringComparison.Ordinal)) value = new JObject { ["messages"] = new JArray() };
                else if (path.StartsWith("/users/", StringComparison.Ordinal)) { var id = path.Substring(7); value = new JObject { ["userId"] = id, ["alias"] = id.Replace("review-duo-", "") }; }
                if (value == null) return Inner != null ? Inner.Get<T>(path, ct) : Task.FromException<T>(new ApiException(404, "NOT_FOUND", "No review fixture for " + path));
                return Task.FromResult(value is T exact ? exact : value.ToObject<T>(Newtonsoft.Json.JsonSerializer.Create(Json.Settings)));
            }

            private static JObject Perp(string id, string coin, string ticker, string side, string status, int lev, long margin, long entry, long liq, long? realized)
            {
                var o = new JObject { ["perpId"] = id, ["coinId"] = coin, ["ticker"] = ticker, ["side"] = side, ["status"] = status, ["leverage"] = lev,
                    ["marginCents"] = margin, ["entryMicros"] = entry, ["liqMicros"] = liq };
                if (realized.HasValue) { o["realizedCents"] = realized.Value; o["closedAt"] = Iso(240); }
                return o;
            }

            private static JObject Post(string id, string user, string alias, string text, double minutes) =>
                new JObject { ["postId"] = id, ["userId"] = user, ["alias"] = alias, ["text"] = text, ["at"] = Ago(minutes) };

            private static JObject Space() => new JObject { ["spaceId"] = "review-space", ["title"] = "REVIEW · alpha night: what shipped this week", ["hostId"] = Kemi,
                ["speakerIds"] = new JArray(Hiro, Amara, Lucia), ["handIds"] = new JArray(), ["listenerCount"] = 41 };

            private Task<T> Reject<T>() { Writes++; return Task.FromException<T>(new InvalidOperationException("Duo wave 2 capture rejected a write.")); }
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
            var report = new JObject { ["scope"] = "REVIEW FIXTURE: shipping catalog apps and social Views; in-memory read-only REST (writes rejected); no RTC, payment or wallet operations." };
            receipt["duoIos2"] = report;
            return LobbyFlowCapture.Run(shot, wait, expect, step, receipt, phone => Duo(phone, shot, wait, expect, step, report), hookDebtCents: 72000);
        }

        private static async Task Duo(PhoneShell catalogPhone, Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JObject report)
        {
            var savedPrefs = DuoPrefs.Store;
            DuoPrefs.Store = new DuoPrefs.Memory();
            Services.TryGet<IRestClient>(out var previousRest);
            var rest = new Rest { Inner = previousRest, Me = "duo2-review-" + Guid.NewGuid().ToString("N") };
            SocialHub hub = null;
            PhoneShell phone = null;
            var mark = El.Text("REVIEW FIXTURE · DUO WAVE 2");
            mark.pickingMode = PickingMode.Ignore;
            mark.style.position = Position.Absolute; mark.style.left = 36; mark.style.bottom = 12;
            mark.style.fontSize = 18; mark.style.color = new Color(.24f, .24f, .3f); mark.style.backgroundColor = Color.white;
            UiRoot.Instance.Root.Add(mark);
            string prefix = $"ctlife.{Env.Current?.id}.social.{rest.Me}.";
            // Money views render only while "signed in": an in-memory placeholder token on the lobby fixture session
            // (never persisted; every request goes to the fixture REST). Cleared in finally.
            var session = Services.Get<Session>();
            var accessToken = typeof(Session).GetProperty(nameof(Session.AccessToken));
            var savedToken = session.AccessToken;
            string heldKey = null;
            try
            {
                accessToken.SetValue(session, "review-fixture-not-a-token");
                // The held career is a per-user device pref set on Apply; seed it for the fixture user (removed after).
                heldKey = "ctlife." + (Env.Current != null ? Env.Current.id : "env") + ".careers.held." + session.UserId;
                PlayerPrefs.SetString(heldKey, "discord_mod");
                // Fresh catalog so the shared money hub is created against the fixture REST.
                catalogPhone.Dispose();
                Services.Register<IRestClient>(rest);
                PlayerPrefs.SetString(prefix + "recentCalls", new JArray(
                    new JObject { ["userId"] = Hiro, ["kind"] = "missed", ["paid"] = false, ["at"] = ServerClock.NowMs - 25 * 60000 },
                    new JObject { ["userId"] = Kemi, ["kind"] = "outgoing", ["paid"] = false, ["at"] = ServerClock.NowMs - 3 * 3600000 },
                    new JObject { ["userId"] = Amara, ["kind"] = "incoming", ["paid"] = true, ["at"] = ServerClock.NowMs - 26 * 3600000 }).ToString());
                PlayerPrefs.SetString(prefix + "follows", new JArray(Kemi, Hiro, Amara).ToString());
                hub = (SocialHub)Activator.CreateInstance(typeof(SocialHub), Instance, null, new object[] { rest.Me }, null);
                foreach (var (id, alias) in new[] { (Kemi, "kemi.sol"), (Hiro, "Hiro"), (Amara, "Amara"), (Lucia, "lucia.eth") }) hub.Names.Remember(id, alias);
                foreach (var id in new[] { Kemi, Hiro, Amara }) hub.Follows.Set(id, true);
                phone = new PhoneShell(UiRoot.Instance.PhoneLayer);
                var timeline = new SocialFixtureApp("timeline", "TimelineApp", hub, p => new object[] { hub, p, null, null });
                var calls = new SocialFixtureApp("calls", "CallsApp", hub, p => new object[] { hub, p, null, null });
                var spaces = new SocialFixtureApp("spaces", "SpacesStudioApp", hub, p => new object[] { hub, p });
                foreach (var app in new PhoneApp[] { timeline, calls, spaces }) phone.Register(app);
                PhoneCatalog.Register(phone, _ => { }, () => { });
                VisualElement Layer() => UiRoot.Instance.PhoneLayer;

                foreach (var size in new[] { DuoTextSize.L, DuoTextSize.XXL })
                {
                    string tag = size == DuoTextSize.L ? "L" : "XXL";
                    bool large = size == DuoTextSize.L;
                    DuoText.Set(size);
                    step("Duo wave 2 " + tag);

                    phone.Open("wallet");
                    await shot("duo-wave2-wallet-" + tag, 1.0f);
                    expect(Layer().Q("wallet-card-solana")?.ClassListContains("duo-wcard-front") == true, tag + ": Wallet opens on the Solana card");
                    expect(Layer().Q("wallet-card-loan") != null, tag + ": Loan card shows the reported debt");

                    phone.Open("bank");
                    await shot("duo-wave2-bank-" + tag, 0.8f);
                    expect(Layer().Q("wallet-card-cash")?.ClassListContains("duo-wcard-front") == true, tag + ": Bank opens on CT Cash");
                    if (large)
                    {
                        phone.Open("topup");
                        await shot("duo-wave2-bank-topup-sheet-L", 1.0f);
                        expect(Layer().Q("bank-topup-sheet") != null, "Top up opens the top-up sheet");
                    }

                    phone.Open("perps");
                    await wait(1.2f);
                    var coin = Layer().Q<Button>("perps-coin-review-froge");
                    expect(coin != null, tag + ": fixture coins listed");
                    if (coin != null) { using (var e = NavigationSubmitEvent.GetPooled()) { e.target = coin; coin.SendEvent(e); } }
                    await wait(1.5f);
                    await shot("duo-wave2-perps-" + tag, 0.6f);
                    expect(Layer().Q("perps-chart") != null, tag + ": chart drawn from the fixture candles");
                    expect(Layer().Q(className: "duo-kv-grid") != null, tag + ": Your position grid from the open perp row");
                    if (large)
                    {
                        var longButton = Layer().Q<Button>("perps-long");
                        using (var e = NavigationSubmitEvent.GetPooled()) { e.target = longButton; longButton.SendEvent(e); }
                        await shot("duo-wave2-perps-order-sheet-L", 0.8f);
                        expect(Layer().Q("perps-order-sheet") != null, "Long opens the order sheet");
                    }

                    phone.Open("jobs");
                    await shot("duo-wave2-jobs-" + tag, 1.2f);
                    expect(Layer().Q("careers-featured") != null, tag + ": featured job card");
                    expect(Layer().Q<Button>("careers-work-discord_mod") != null, tag + ": Work pill on the held career");
                    if (large)
                    {
                        var row = Layer().Q<Button>("careers-details-discord_mod");
                        using (var e = NavigationSubmitEvent.GetPooled()) { e.target = row; row.SendEvent(e); }
                        await shot("duo-wave2-jobs-details-sheet-L", 0.8f);
                        expect(Layer().Q("careers-details-sheet") != null, "a job row opens its details sheet");
                    }

                    phone.Open("timeline");
                    await shot("duo-wave2-timeline-" + tag, 1.2f);
                    expect(timeline.Frame.Query(className: "duo-coin-chip").ToList().Count >= 1, tag + ": ticker chips on posts that name coins");
                    if (large)
                    {
                        var fab = timeline.Frame.Q<Button>("timeline-compose-button");
                        using (var e = NavigationSubmitEvent.GetPooled()) { e.target = fab; fab.SendEvent(e); }
                        await shot("duo-wave2-timeline-compose-sheet-L", 0.8f);
                        expect(Layer().Q("timeline-compose-sheet") != null, "compose opens the composer sheet");
                    }

                    phone.Open("calls");
                    await shot("duo-wave2-calls-" + tag, 1.0f);
                    expect(calls.Frame.Q("calls-recent-" + Hiro)?.Q(className: "duo-destructive") != null, tag + ": missed call in red");
                    if (large)
                    {
                        var call = CallSession.StartOutgoing(new[] { Kemi }, SocialHub.NowMs);
                        call.Dialed("review-call", SocialHub.NowMs);
                        typeof(CallController).GetProperty(nameof(CallController.Active), Instance).SetValue(hub.Calls, call);
                        hub.NotifyChanged();
                        await shot("duo-wave2-calls-incall-L", 1.0f);
                        expect(calls.Frame.Q("social-calls-hangup") != null, "in-call glass controls render without media");
                        typeof(CallController).GetProperty(nameof(CallController.Active), Instance).SetValue(hub.Calls, null);
                    }

                    phone.Open("spaces");
                    await shot("duo-wave2-spaces-" + tag, 1.2f);
                    expect(spaces.Frame.Q(className: "duo-space-card") != null, tag + ": live Space card");
                    if (large)
                    {
                        var space = new SpaceSession(rest.Me);
                        space.Open("review-space");
                        var snapshot = (JObject)((Task<JToken>)rest.Get<JToken>("/spaces/review-space")).Result;
                        space.ApplySnapshot(SocialParse.Space(snapshot), space.BeginFetch(), SocialHub.NowMs);
                        space.MediaConnecting();
                        typeof(SpacesController).GetProperty(nameof(SpacesController.Session), Instance).SetValue(hub.Spaces, space);
                        hub.NotifyChanged();
                        await shot("duo-wave2-spaces-stage-L", 1.2f);
                        expect(spaces.Frame.Query(className: "duo-stage-seat").ToList().Count == 4, "stage grid shows host + 3 speakers");
                        expect(spaces.Frame.Q("social-spaces-request") != null, "a listener sees Request in the glass bar");
                        typeof(SpacesController).GetProperty(nameof(SpacesController.Session), Instance).SetValue(hub.Spaces, null);
                    }
                    phone.Close();
                }
                report["reads"] = new JArray(rest.Reads.Distinct().Take(80));
                report["writes"] = rest.Writes;
                expect(rest.Writes == 0, "Duo wave 2 capture made zero writes");
            }
            finally
            {
                accessToken.SetValue(session, savedToken);
                if (heldKey != null) PlayerPrefs.DeleteKey(heldKey);
                DuoText.Set(DuoTextSize.L);
                phone?.Dispose();
                hub?.Dispose();
                mark.RemoveFromHierarchy();
                foreach (string key in new[] { "threads", "follows", "blocks", "creator", "recentCalls", "paidInFlight" }) PlayerPrefs.DeleteKey(prefix + key);
                PlayerPrefs.Save();
                DuoPrefs.Store = savedPrefs;
                if (previousRest != null) Services.Register(previousRest); else Services.Unregister<IRestClient>();
            }
        }
    }
}
