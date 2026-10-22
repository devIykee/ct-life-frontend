using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Ventures;
using NUnit.Framework;

namespace CTLife.Tests
{
    public sealed class OfflineDemoTransportTests
    {
        [TearDown] public void ClearArgs() => LaunchArgs.SetForTests(new Dictionary<string, string>());

        [TestCase("GET", "/ads/live")]
        [TestCase("POST", "/shop/purchase")]
        public void DemoRejectsReadsAndWritesBeforeOpeningTransport(string method, string path)
        {
            LaunchArgs.SetForTests(new Dictionary<string, string> { ["islandDemo"] = "1" });
            var rest = new RestClient(new Session());
            var error = Assert.ThrowsAsync<ApiException>(async () =>
                await rest.SendRaw(method, path, "{}", false, null, null, CancellationToken.None));
            Assert.AreEqual("OFFLINE_DEMO", error.Code);
        }

        [Test]
        public void OfflineRoomsFlagDoesNotReplaceTheRealProfile()
        {
            LaunchArgs.SetForTests(new Dictionary<string, string> { ["islandOffline"] = "1" });
            Assert.IsTrue(RuntimeMode.IsOfflineIsland);
            Assert.IsFalse(RuntimeMode.IsOfflineDemo);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DemoRejectsRestBeforeRefreshingStaleSavedIdentity(bool once)
        {
            LaunchArgs.SetForTests(new Dictionary<string, string> { ["islandDemo"] = "1" });
            var session = new Session();
            // Populate memory only: SetTokens would overwrite the developer's stored session preferences.
            typeof(Session).GetProperty(nameof(Session.AccessToken)).SetValue(session, "synthetic-access");
            typeof(Session).GetProperty(nameof(Session.RefreshToken)).SetValue(session, "synthetic-refresh");
            typeof(Session).GetProperty(nameof(Session.UserId)).SetValue(session, "offline-test-user");
            int refreshes = 0;
            var rest = new RestClient(session)
            {
                RefreshHandler = () => { refreshes++; throw new InvalidOperationException("Refresh must not run in the offline demo."); }
            };
            Task request = once
                ? rest.PostOnce<NoContent>("/shop/purchase", new { }, "00000000-0000-0000-0000-000000000001")
                : rest.SendRaw("POST", "/shop/purchase", "{}", true, null, null, CancellationToken.None);
            var error = Assert.ThrowsAsync<ApiException>(async () => await request);
            Assert.AreEqual("OFFLINE_DEMO", error.Code);
            Assert.AreEqual(0, refreshes);
        }

        [TestCase("rest")]
        [TestCase("once")]
        [TestCase("likeness")]
        [TestCase("ad")]
        public void CancellationWinsOverOfflineRejection(string transport)
        {
            LaunchArgs.SetForTests(new Dictionary<string, string> { ["islandDemo"] = "1" });
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var session = new Session();
            var rest = new RestClient(session);
            Task request = transport switch
            {
                "rest" => rest.SendRaw("GET", "/ads/live", null, false, null, null, cancelled.Token),
                "once" => rest.PostOnce<NoContent>("/shop/purchase", new { }, "00000000-0000-0000-0000-000000000001", cancelled.Token),
                "likeness" => new LikenessClient(rest, session).Upload(new byte[] { 137 }, "image/png", "offline-test", cancelled.Token),
                _ => new UnityMultipartClient().PostFile("/uploads", "file", "creative.png", "image/png", new byte[] { 137 }, "offline-test", cancelled.Token)
            };
            var error = Assert.CatchAsync<OperationCanceledException>(async () => await request);
            Assert.AreEqual(cancelled.Token, error.CancellationToken);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DemoBlocksRawLikenessBeforeRefreshOrTransport(bool upload)
        {
            LaunchArgs.SetForTests(new Dictionary<string, string> { ["islandDemo"] = "1" });
            int refreshes = 0;
            var session = new Session(); // Stale credentials would otherwise enter RefreshHandler first.
            var rest = new RestClient(session)
            {
                RefreshHandler = () =>
                {
                    refreshes++;
                    throw new InvalidOperationException("Refresh trap: transport must not be reached by this test.");
                }
            };
            var likeness = new LikenessClient(rest, session);
            Task request = upload
                ? likeness.Upload(new byte[] { 137, 80, 78, 71 }, "image/png", "offline-test-upload", CancellationToken.None)
                : likeness.Preview("00000000-0000-0000-0000-000000000001", CancellationToken.None);
            Assert.IsTrue(request.IsCompleted, "Demo rejection must finish before any transport await.");
            var error = Assert.ThrowsAsync<ApiException>(async () => await request);
            Assert.AreEqual("OFFLINE_DEMO", error.Code);
            Assert.AreEqual(0, refreshes);
        }

        [Test]
        public void DemoBlocksRawAdUploadBeforeAuthOrTransport()
        {
            LaunchArgs.SetForTests(new Dictionary<string, string> { ["islandDemo"] = "1" });
            Services.TryGet<Session>(out var priorSession);
            Services.TryGet<IAuthService>(out var priorAuth);
            var auth = new RefreshTrap();
            try
            {
                Services.Register(new Session());
                Services.Register<IAuthService>(auth);
                var request = new UnityMultipartClient().PostFile("/uploads", "file", "creative.png", "image/png",
                    new byte[] { 137, 80, 78, 71 }, "offline-test-ad", CancellationToken.None);
                Assert.IsTrue(request.IsCompleted, "Demo rejection must finish before any transport await.");
                var error = Assert.ThrowsAsync<ApiException>(async () => await request);
                Assert.AreEqual("OFFLINE_DEMO", error.Code);
                Assert.AreEqual(0, auth.Attempts);
            }
            finally
            {
                Services.Unregister<Session>(); Services.Unregister<IAuthService>();
                if (priorSession != null) Services.Register(priorSession);
                if (priorAuth != null) Services.Register<IAuthService>(priorAuth);
            }
        }

        private sealed class RefreshTrap : IAuthService
        {
            public int Attempts;
            public bool DevAuthAllowed => false;
            private Exception Trap() { Attempts++; return new InvalidOperationException("Auth trap: transport must not be reached by this test."); }
            public Task EnsureFresh(CancellationToken ct = default) => throw Trap();
            public Task<bool> Refresh() => throw Trap();
            public Task<AuthResult> SignInWithWallet(IWalletSignIn wallet, CancellationToken ct = default) => throw Trap();
            public Task<AuthResult> SignInDev(string wallet, bool isSeeker = false, CancellationToken ct = default) => throw Trap();
            public Task<bool> TryResume(CancellationToken ct = default) => throw Trap();
            public Task SignOut(IWalletSignIn wallet) => throw Trap();
        }
    }
}
