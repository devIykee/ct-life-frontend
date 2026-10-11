#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Economy.Tests
{
    // Captured public HTTP body; all transport is fixture-only, never authenticated or sent.
    public class MarketReadStateTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string EmptyPage = "{\"coins\":[],\"nextCursor\":null}";
        private IRestClient _oldRest;
        private IRealtimeService _oldRealtime;
        private Session _oldSession;
        private MoneyHub _hub;
        private object _view;
        private ScrollView _root;
        private EditorWindow _window;
        private FixtureRest _rest;

        [SetUp]
        public void SetUp()
        {
            Services.TryGet(out _oldRest);
            Services.TryGet(out _oldSession);
            Services.TryGet(out _oldRealtime);
            Services.Unregister<IRealtimeService>();
            _rest = new FixtureRest();
            Services.Register<IRestClient>(_rest);
            var session = new Session();
            // In-memory test sentinel only: do not call SetTokens or touch saved credentials/preferences.
            typeof(Session).GetProperty("AccessToken").SetValue(session, "fixture-not-a-token");
            Services.Register(session);
            _hub = (MoneyHub)Activator.CreateInstance(typeof(MoneyHub), Instance, null, new object[] { null }, null);
        }

        [TearDown]
        public void TearDown()
        {
            (_view as IDisposable)?.Dispose();
            _hub?.Dispose();
            if (_window != null) _window.Close();
            Restore(_oldRest);
            Restore(_oldSession);
            Restore(_oldRealtime);
        }

        private static void Restore<T>(T service) where T : class
        {
            if (service == null) Services.Unregister<T>();
            else Services.Register(service);
        }

        private void Open(string type, object arg)
        {
            var t = typeof(MoneyHub).Assembly.GetType("CTLife.Economy." + type, true);
            _view = Activator.CreateInstance(t, Instance, null, new[] { (object)_hub, arg }, null);
            _root = (ScrollView)t.BaseType.GetField("Root", Instance).GetValue(_view);
            _window = ScriptableObject.CreateInstance<MarketReadTestWindow>();
            _window.Show();
            _window.rootVisualElement.Add(_root);
            Assert.That(_root.panel, Is.Not.Null);
        }

        private string[] Labels() => _root.Query<Label>().ToList().Select(l => l.text).ToArray();
        private Button OpenButton() => _root.Query<Button>().ToList().Single(b => b.text == MoneyStrings.T("perps.open"));

        [Test]
        public void PerpSuccessfulEmptyPageEndsLoadingAndDisablesOrders()
        {
            _hub.Market.Pump(new string[] { null }, CancellationToken.None);
            Assert.That(_rest.Reads, Is.EqualTo(1));
            Assert.That(double.IsNaN(_hub.Market.Age(null)), Is.False);
            Assert.That(_hub.Market.LastError, Is.Null);
            Assert.That(_hub.Market.List(null), Is.Empty);
            Open("PerpPitView", "trade");
            Assert.That(Labels(), Does.Not.Contain(MoneyStrings.T("common.loading")));
            Assert.That(Labels(), Does.Contain("No coins available to trade yet."));
            Assert.That(OpenButton().enabledSelf, Is.False);
            Assert.That(_rest.Writes, Is.Zero);
        }

        [Test]
        public void PerpFastEmptyCompletionReplacesAlreadyVisibleLoading()
        {
            Open("PerpPitView", "trade");
            Assert.That(Labels(), Does.Contain(MoneyStrings.T("common.loading")));
            _hub.Market.Pump(new string[] { null }, CancellationToken.None);
            Assert.That(Labels(), Does.Contain("No coins available to trade yet."));
            Assert.That(Labels(), Does.Not.Contain(MoneyStrings.T("common.loading")));
        }

        [Test]
        public void PerpPopulatedCoinSelectionEnablesOrders()
        {
            // Explicit synthetic unit fixture, not claimed hosted market content.
            _hub.Market.Remember(new MarketCoin { CoinId = "fixture-coin", Ticker = "TEST", Stage = "new", PriceMicros = 1 });
            Open("PerpPitView", "trade");
            Assert.That(OpenButton().enabledSelf, Is.False);
            var coin = _root.Query<Button>().ToList().Single(b => b.text.StartsWith("$TEST"));
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = coin; coin.SendEvent(evt); }
            Assert.That(OpenButton().enabledSelf, Is.True);
            Assert.That(_rest.Writes, Is.Zero);
        }

        [TestCase(true, 0, "Market room joined · no live coins · REST refreshed now")]
        [TestCase(false, 0, "Market room disconnected · REST refreshed now")]
        [TestCase(true, 12, "Market room joined · no live coins · REST refreshed 12s ago")]
        [TestCase(false, 12, "Market room disconnected · REST refreshed 12s ago")]
        public void PulseEmptyRestStatusSeparatesRoomConnection(bool joined, int age, string expected)
        {
            _hub.Market.Pump(new[] { "new" }, CancellationToken.None);
            Assert.That(_hub.Market.LastError, Is.Null);
            Assert.That(_hub.Market.List("new"), Is.Empty);
            var stamps = (System.Collections.Generic.Dictionary<string, double>)typeof(MarketFeed).GetField("_restFetchedAt", Instance).GetValue(_hub.Market);
            stamps["new"] = Time.realtimeSinceStartupAsDouble - age;
            if (joined)
            {
                // Inject only the channel join sentinel; no websocket, tokens or invented server rows.
                var channel = new MarketChannel();
                var room = channel.GetType().BaseType.GetProperty("Room", Instance);
                room.SetValue(channel, System.Runtime.Serialization.FormatterServices.GetUninitializedObject(room.PropertyType));
                typeof(MarketFeed).GetField("_channel", Instance).SetValue(_hub.Market, channel);
            }
            Assert.That(_hub.Market.RoomJoined, Is.EqualTo(joined));
            Assert.That(_hub.Market.RoomLive, Is.False);
            Open("PulseView", null);
            Assert.That(Labels(), Does.Contain(expected));
            Assert.That(string.Join(" | ", Labels()), Does.Not.Contain("now ago"));
            Assert.That(string.Join(" | ", Labels()), Does.Not.Contain("offline"));
        }

        [TestCase("PerpPitView", "trade", null)]
        [TestCase("PulseView", null, "new")]
        public void FailedReadRemainsAnErrorNotEmptySuccess(string type, string tab, string stage)
        {
            _rest.Error = new ApiException(0, "NETWORK_ERROR", "Fixture transport failure", isNetwork: true);
            _hub.Market.Pump(new[] { stage }, CancellationToken.None);
            Assert.That(_hub.Market.LastError, Is.Not.Null);
            Assert.That(double.IsNaN(_hub.Market.Age(stage)), Is.True);
            Open(type, tab);
            Assert.That(Labels(), Does.Contain(MoneyStrings.T(_hub.Market.LastError)));
            Assert.That(Labels(), Does.Not.Contain("No coins available to trade yet."));
            Assert.That(string.Join(" | ", Labels()), Does.Not.Contain("refreshed"));
            if (type == "PerpPitView") Assert.That(OpenButton().enabledSelf, Is.False);
        }

        [Test]
        public void PerpSelectedCoinRemovedByEmptyRefreshDisablesOrders()
        {
            _hub.Market.Remember(new MarketCoin { CoinId = "fixture-coin", Ticker = "TEST", Stage = "new", PriceMicros = 1 });
            Open("PerpPitView", "trade");
            var coin = _root.Query<Button>().ToList().Single(b => b.text.StartsWith("$TEST"));
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = coin; coin.SendEvent(evt); }
            Assert.That(OpenButton().enabledSelf, Is.True);
            _hub.Market.Pump(new string[] { null }, CancellationToken.None);
            Assert.That(OpenButton().enabledSelf, Is.False);
            Assert.That(Labels(), Does.Contain("No coins available to trade yet."));
            Assert.That(_rest.Writes, Is.Zero);
        }

        private sealed class MarketReadTestWindow : EditorWindow { }

        private sealed class FixtureRest : IRestClient
        {
            public int Reads, Writes;
            public Exception Error;
            public Func<Task<bool>> RefreshHandler { get; set; }
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                Assert.That(path, Does.StartWith("/market/coins"));
                Reads++;
                if (Error != null) return Task.FromException<T>(Error);
                return Task.FromResult(JToken.Parse(EmptyPage).ToObject<T>());
            }
            private Task<T> Reject<T>() { Writes++; throw new InvalidOperationException("Unexpected fixture write"); }
            public Task<T> Post<T>(string p, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Reject<T>();
            public Task<T> Put<T>(string p, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Patch<T>(string p, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Delete<T>(string p, CancellationToken ct = default) => Reject<T>();
            public Task<T> Send<T>(string method, string p, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Reject<T>();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => Reject<RawResponse>();
        }
    }
}
#endif
