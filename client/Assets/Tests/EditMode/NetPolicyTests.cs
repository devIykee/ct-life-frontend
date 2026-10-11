using System.Globalization;
using System.Threading;
using CTLife.Core;
using CTLife.Net;
using CTLife.World;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Tests
{
    public class RetryPolicyTests
    {
        [Test]
        public void RetryAfterIsCultureInvariant()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.AreEqual(1.5f, RestClient.RetryAfterSeconds("1.5", 1), 1e-4);
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }

        [Test]
        public void RetryAfterIsClampedAndHasBackoffWhenMissing()
        {
            Assert.AreEqual(120f, RestClient.RetryAfterSeconds("9999", 1));
            Assert.AreEqual(1f, RestClient.RetryAfterSeconds("0", 1));
            Assert.AreEqual(2f, RestClient.RetryAfterSeconds(null, 1));
            Assert.AreEqual(30f, RestClient.RetryAfterSeconds(null, 10));
        }

        [Test]
        public void ServerBusyRetriesAreCapped()
        {
            int retries = 0;
            while (RestClient.ShouldRetryBusy(retries)) retries++;
            Assert.AreEqual(RestClient.MaxBusyAttempts, retries);
            Assert.LessOrEqual(retries, 5, "an unbounded busy loop leaves the player waiting forever");
        }

        [Test]
        public void PersistentRoomBackoffGrowsAndCaps()
        {
            Assert.AreEqual(0f, ReconnectBackoff.DelaySeconds(0));
            Assert.AreEqual(2f, ReconnectBackoff.DelaySeconds(1));
            Assert.AreEqual(4f, ReconnectBackoff.DelaySeconds(2));
            Assert.AreEqual(32f, ReconnectBackoff.DelaySeconds(5));
            Assert.AreEqual(ReconnectBackoff.MaxSeconds, ReconnectBackoff.DelaySeconds(50));
        }
    }

    public class EnvAndPhaseTests
    {
        [Test]
        public void LocalAndMockUseTheBackendDefaultPorts()
        {
            foreach (var id in new[] { EnvId.Local, EnvId.Mock })
            {
                var c = EnvConfig.CreateDefault(id);
                Assert.AreEqual("http://localhost:4010/v1", c.restBaseUrl, id.ToString());
                Assert.AreEqual("ws://localhost:2567", c.realtimeUrl);
                Assert.IsTrue(c.allowDevAuth);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StagingUsesConfirmedRailwayConfiguration(bool useBuildAsset)
        {
            EnvConfig c;
            if (useBuildAsset)
            {
                // BuildScript reads this selection; runtime resolves the same Resources assets.
                var selection = UnityEditor.AssetDatabase.LoadAssetAtPath<EnvSelection>(
                    "Assets/Resources/Env/EnvSelection.asset");
                Assert.IsNotNull(selection);
                c = selection.Find(EnvId.Staging);
                Assert.IsNotNull(c, "the build must have a staging configuration");
                Assert.AreSame(UnityEngine.Resources.Load<EnvConfig>("Env/EnvConfig_Staging"), c);
            }
            else c = EnvConfig.CreateDefault(EnvId.Staging);

            try
            {
                Assert.AreEqual(EnvId.Staging, c.id);
                Assert.AreEqual("https://ctlife-api-production.up.railway.app/v1", c.restBaseUrl);
                Assert.AreEqual("wss://ctlife-realtime-production.up.railway.app", c.realtimeUrl);
                Assert.AreEqual(SolanaCluster.Devnet, c.cluster);
                Assert.IsFalse(c.allowDevAuth);
                Assert.AreEqual("https://ctlife.fun/", c.IdentityUri);
                Assert.AreEqual("ctlife.fun", c.SiwsDomain);
                Assert.IsEmpty(c.walletIdentityUri, "use the app-wide identity");
                Assert.IsEmpty(c.liveKitUrlOverride, "RTC must use the backend-issued endpoint");
                Assert.IsEmpty(c.liveKitDevToken, "never ship a pre-minted token");
                Assert.IsNull(c.PlaceholderProblem(), "staging must pass the build's endpoint guard");
            }
            finally
            {
                if (!useBuildAsset) UnityEngine.Object.DestroyImmediate(c);
            }
        }

        [TestCase(EnvId.Staging)]
        [TestCase(EnvId.Prod)]
        public void ShippingEnvironmentsRefusePlaceholderEndpoints(EnvId id)
        {
            var c = EnvConfig.CreateDefault(id);
            try
            {
                c.restBaseUrl = "https://api.invalid/v1";
                c.realtimeUrl = "wss://realtime.example.com";
                Assert.AreEqual("restBaseUrl is a placeholder", c.PlaceholderProblem());
                c.restBaseUrl = "https://api.example.com/v1";
                c.realtimeUrl = "wss://realtime.invalid";
                Assert.AreEqual("realtimeUrl is a placeholder", c.PlaceholderProblem());
            }
            finally { UnityEngine.Object.DestroyImmediate(c); }
        }

        [Test]
        public void ProductionRemainsBlockedUntilItsHostsAreConfirmed()
        {
            Assert.IsNotNull(EnvConfig.CreateDefault(EnvId.Prod).PlaceholderProblem());
            var real = EnvConfig.CreateDefault(EnvId.Prod);
            real.restBaseUrl = "https://api.example.com/v1";
            real.realtimeUrl = "wss://realtime.example.com";
            real.walletIdentityUri = "https://example.com/";
            Assert.IsNull(real.PlaceholderProblem());
            real.allowDevAuth = true;
            Assert.IsNotNull(real.PlaceholderProblem(), "prod must never allow dev auth");
            Assert.IsFalse(EnvConfig.CreateDefault(EnvId.Prod).allowDevAuth);
        }

        [Test]
        public void IdentityAndSiwsDomainComeFromOneValue()
        {
            // The deployed SIWS challenge and MWA identity share the purchased domain, including the URI's trailing slash.
            foreach (EnvId id in System.Enum.GetValues(typeof(EnvId)))
            {
                var asset = UnityEngine.Resources.Load<EnvConfig>("Env/EnvConfig_" + id);
                if (asset == null) continue;
                Assert.IsTrue(string.IsNullOrEmpty(asset.walletIdentityUri), $"{asset.name} overrides the identity; leave it empty to use EnvConfig.AppIdentityUri");
                Assert.AreEqual(EnvConfig.AppIdentityUri, asset.IdentityUri);
                StringAssert.DoesNotContain(".dev/", asset.restBaseUrl + "/", asset.name);
            }
            var c = EnvConfig.CreateDefault(EnvId.Local);
            Assert.AreEqual("https://ctlife.fun/", c.IdentityUri);
            Assert.AreEqual("ctlife.fun", c.SiwsDomain);
            c.walletIdentityUri = "https://ctlife.example:8443/";
            Assert.AreEqual("ctlife.example:8443", c.SiwsDomain);
        }

        [Test]
        public void ApiUrlJoinsVersionedBase()
        {
            var c = EnvConfig.CreateDefault(EnvId.Local);
            Assert.AreEqual("http://localhost:4010/v1/me", c.ApiUrl("/me"));
            Assert.AreEqual("http://localhost:4010/v1/me", c.ApiUrl("/v1/me"));
            Assert.AreEqual("http://localhost:4010/v1/me", c.ApiUrl("me"));
        }

        [Test]
        public void PhaseComesFromConfigThenEnvironment()
        {
            Assert.AreEqual(3, Phases.Resolve(null, 3));
            Assert.AreEqual(2, Phases.Resolve(new JObject { ["livePhase"] = 2 }, 6));
            Assert.AreEqual(4, Phases.Resolve(new JObject { ["meta"] = new JObject { ["livePhase"] = 4 } }, 1));
            Assert.AreEqual(6, Phases.Resolve(new JObject { ["livePhase"] = 99 }, 1));
            Assert.AreEqual(1, Phases.Resolve(new JObject { ["livePhase"] = "two" }, 1));
        }

        [Test]
        public void ProdBuildsDefineCtlifeProdAndOthersDoNot()
        {
            CollectionAssert.Contains(EditorTools.BuildScript.ExtraDefines(EnvId.Prod), "CTLIFE_PROD");
            CollectionAssert.IsEmpty(EditorTools.BuildScript.ExtraDefines(EnvId.Local));
            Assert.IsTrue(EditorTools.BuildScript.TryParseEnv("staging", out var env));
            Assert.AreEqual(EnvId.Staging, env);
            Assert.IsFalse(EditorTools.BuildScript.TryParseEnv("qa", out _));
        }

        [Test]
        public void UnplacedMeansTheSchemaDefault()
        {
            Assert.IsTrue(VenueRoomCheck.IsUnplaced(0, 0));
            Assert.IsFalse(VenueRoomCheck.IsUnplaced(0.5, 0));
        }

        [Test]
        public void VolumeStepsByTenPercentAndClamps()
        {
            Assert.AreEqual(0.6f, UI.VolumeStep.Up(0.5f), 1e-4);
            Assert.AreEqual(0.4f, UI.VolumeStep.Down(0.5f), 1e-4);
            Assert.AreEqual(1f, UI.VolumeStep.Up(1f), 1e-4);
            Assert.AreEqual(0f, UI.VolumeStep.Down(0.02f), 1e-4);
        }
    }
}
