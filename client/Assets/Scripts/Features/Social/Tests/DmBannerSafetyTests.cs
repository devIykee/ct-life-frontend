#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.SocialFeatures.Tests
{
    /// <summary>
    /// Duo notification banners must not bypass Messages' safety gates: request separation, the scam warning,
    /// per-message scam hints and block (security review of the 2026-10-16 wave 1 commit).
    /// </summary>
    public class DmBannerSafetyTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Friend = "11111111-1111-4111-8111-111111111111", Stranger = "22222222-2222-4222-8222-222222222222";
        private static readonly FieldInfo RunnerInstance = typeof(Runner).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);

        private SocialHub _hub;
        private IRestClient _previousRest;
        private Runner _previousRunner;
        private GameObject _runner;
        private IDuoPrefs _previousPrefs;

        [SetUp]
        public void SetUp()
        {
            _previousPrefs = DuoPrefs.Store;
            DuoPrefs.Store = new DuoPrefs.Memory();
            DuoNotificationCenter.Reset();
            DuoNotificationCenter.Queue.SetForeground(false, null);
            Services.TryGet(out _previousRest);
            _previousRunner = (Runner)RunnerInstance.GetValue(null);
            _runner = new GameObject("dm-banner-test-runner");
            RunnerInstance.SetValue(null, _runner.AddComponent<Runner>());
            Services.Register<IRestClient>(new EmptyRest());
            _hub = (SocialHub)Activator.CreateInstance(typeof(SocialHub), Instance, null,
                new object[] { "dm-banner-test-" + Guid.NewGuid().ToString("N") }, null);
            _hub.Names.Remember(Friend, "kemi.sol");
            _hub.Names.Remember(Stranger, "airdrop_admin");
            _hub.Follows.Set(Friend, true);
        }

        [TearDown]
        public void TearDown()
        {
            MessagesApp.PendingPeer = null; MessagesApp.PendingAlias = null; MessagesApp.PendingFromNotification = false;
            MessagesApp.Opener = null;
            _hub?.Dispose();
            DuoNotificationCenter.Reset();
            DuoPrefs.Store = _previousPrefs;
            if (_previousRest != null) Services.Register(_previousRest); else Services.Unregister<IRestClient>();
            RunnerInstance.SetValue(null, _previousRunner);
            if (_runner != null) UnityEngine.Object.DestroyImmediate(_runner);
        }

        private void Deliver(string from, string text, string id = null) =>
            _hub.Messages.OnRealtime(new DmMessage { messageId = id ?? Guid.NewGuid().ToString("N"), fromUserId = from, text = text, at = ServerClock.NowMs });

        private DuoNotification Banner(string from) => DuoNotificationCenter.Queue.Stack.FirstOrDefault(n => n.ThreadKey == from);

        // Rule 1: a message request never banners and never leaks its text; it only counts in the badge.
        [Test]
        public void RequestShowsNoBannerAndOnlyCounts()
        {
            Deliver(Stranger, "verify your wallet here https://evil.example");
            Assert.IsNull(Banner(Stranger), "no banner for a message request");
            Assert.IsFalse(DuoNotificationCenter.Queue.Stack.Any(n => (n.Body ?? "").Contains("evil")), "request text never reaches the game screen");
            Assert.AreEqual(1, _hub.Messages.Unread, "the request still counts toward the Messages badge");
            Assert.IsTrue(_hub.Messages.IsRequest(_hub.Messages.Inbox.Get(Stranger)), "it stays in the requests bucket");
            Assert.IsFalse(DmBannerPolicy.For(Stranger, "x", "hi", blocked: false, request: true).Show);
        }

        // Rule 2: never a banner for a blocked sender.
        [Test]
        public void BlockedSenderNeverBanners()
        {
            Assert.IsFalse(DmBannerPolicy.For(Friend, "kemi.sol", "gm", blocked: true, request: false).Show);
            _hub.Blocks.Set(Friend, true);
            Deliver(Friend, "gm");
            Assert.IsNull(Banner(Friend));
            Assert.AreEqual(0, _hub.Messages.Unread);
        }

        // Rule 3: a notification deep link opens a thread only for a known, accepted chat; a request opens the inbox
        // and is not accepted by the tap; ids are validated.
        [Test]
        public void DeepLinkNeverOpensOrAcceptsARequest()
        {
            Assert.AreEqual(DmBannerPolicy.Target.Inbox, DmBannerPolicy.DeepLink(Stranger, known: true, blocked: false, request: true));
            Assert.AreEqual(DmBannerPolicy.Target.Inbox, DmBannerPolicy.DeepLink(Stranger, known: false, blocked: false, request: false));
            Assert.AreEqual(DmBannerPolicy.Target.Thread, DmBannerPolicy.DeepLink(Friend, known: true, blocked: false, request: false));
            Assert.AreEqual(DmBannerPolicy.Target.None, DmBannerPolicy.DeepLink(Friend, known: true, blocked: true, request: false));

            Deliver(Stranger, "hello");
            string opened = null;
            MessagesApp.Opener = id => opened = id;
            MessagesApp.OpenFromNotification(Stranger);
            Assert.AreEqual("messages", opened);
            Assert.IsTrue(MessagesApp.PendingFromNotification);
            var viewType = typeof(SocialHub).Assembly.GetType("CTLife.SocialFeatures.MessagesApp+View", true);
            var view = (IDisposable)Activator.CreateInstance(viewType, Instance, null,
                new object[] { _hub, null, MessagesApp.PendingPeer, null, true }, null);
            try
            {
                Assert.IsNull(_hub.Messages.Inbox.OpenPeer, "a request opens the inbox (requests + scam warning), not the thread");
                var entry = _hub.Messages.Inbox.Get(Stranger);
                Assert.IsFalse(entry.Accepted, "a notification tap never accepts a request");
                Assert.IsTrue(_hub.Messages.IsRequest(entry));
            }
            finally { view.Dispose(); }

            Deliver(Friend, "gm");
            view = (IDisposable)Activator.CreateInstance(viewType, Instance, null, new object[] { _hub, null, Friend, null, true }, null);
            try { Assert.AreEqual(Friend, _hub.Messages.Inbox.OpenPeer, "an accepted chat opens straight into its thread"); }
            finally { view.Dispose(); }
        }

        // Rule 5: deep-link peer ids go through SocialWire.IsSafeId.
        [Test]
        public void UnsafeDeepLinkIdsAreIgnored()
        {
            string opened = null;
            MessagesApp.Opener = id => opened = id;
            foreach (var bad in new[] { null, "", "../../dms", "<b>x</b>", new string('a', 400) })
            {
                MessagesApp.OpenFromNotification(bad);
                Assert.IsNull(opened, "rejected: " + bad);
                Assert.AreEqual(DmBannerPolicy.Target.None, DmBannerPolicy.DeepLink(bad, true, false, false));
                Assert.IsFalse(DmBannerPolicy.For(bad, "x", "hi", false, false).Show);
            }
        }

        // Rule 4 (logic half): a followed sender's scam-flagged text shows its scam hint, not the bait; text is plain.
        [Test]
        public void ScamFlaggedTextShowsTheHintInstead()
        {
            Deliver(Friend, "dm me your seed phrase to claim");
            var banner = Banner(Friend);
            Assert.IsNotNull(banner);
            Assert.IsFalse(banner.Body.Contains("seed phrase to claim"), "the bait text is replaced");
            Assert.AreEqual(SocialStrings.T("flag." + ScamSignals.Flag.Secret), banner.Body);
            var ok = DmBannerPolicy.For(Friend, "kemi.sol", "<b>gm</b>\nsee you", false, false);
            Assert.IsTrue(ok.Show);
            Assert.AreEqual(ScamSignals.Flag.None, ok.Flag);
            Assert.AreEqual(SocialWire.Plain("<b>gm</b>\nsee you", 160), ok.Body);
        }

        private sealed class EmptyRest : IRestClient
        {
            public Func<Task<bool>> RefreshHandler { get; set; }
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                var value = JToken.Parse(path.StartsWith("/dms/", StringComparison.Ordinal) ? "{\"messages\":[]}" : "{\"threads\":[]}");
                return Task.FromResult(value is T exact ? exact : value.ToObject<T>(Newtonsoft.Json.JsonSerializer.Create(Json.Settings)));
            }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Put<T>(string path, object body, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Patch<T>(string path, object body, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Delete<T>(string path, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Get<T>(path, ct);
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => throw new NotSupportedException();
        }
    }
}
#endif
