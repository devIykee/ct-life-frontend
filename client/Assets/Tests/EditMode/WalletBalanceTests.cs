using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Tests
{
    public sealed class WalletBalanceTests
    {
        private const string Wallet = "11111111111111111111111111111111";
        private const string OtherWallet = "SysvarRent111111111111111111111111111111111";
        private const string Usdc = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";
        private const string Skr = "SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3";
        private const string Ore = "oreoU2P8bN6jkk3jbaiVxYnG1dCXcYxwhwyK9jSybcp";
        private static JObject Result(JToken value) => new JObject { ["context"] = new JObject { ["slot"] = 123 }, ["value"] = value };
        private static JObject Mint(int decimals = 6) => Result(new JObject
        {
            ["owner"] = WalletBalanceParser.TokenProgram,
            ["data"] = new JObject { ["parsed"] = new JObject { ["type"] = "mint", ["info"] =
                new JObject { ["isInitialized"] = true, ["decimals"] = decimals } } }
        });
        private static JObject Account(string raw, string mint = Usdc, string wallet = Wallet, string account = Wallet, int decimals = 6) => new JObject
        {
            ["pubkey"] = account,
            ["account"] = new JObject { ["owner"] = WalletBalanceParser.TokenProgram,
                ["data"] = new JObject { ["parsed"] = new JObject { ["type"] = "account", ["info"] = new JObject
                { ["owner"] = wallet, ["mint"] = mint, ["tokenAmount"] = new JObject { ["amount"] = raw, ["decimals"] = decimals } } } } }
        };
        private static JObject Currencies() => new JObject
        {
            ["USDC"] = new JObject { ["mint"] = Usdc, ["devnetMint"] = "4zMMC9srt5Ri5X14GAgXhaHii3GnPAEERYPJgZJDncDU", ["decimals"] = 6 },
            ["SKR"] = new JObject { ["mint"] = Skr, ["decimals"] = 5 }, // The current contract discrepancy must not distort holdings.
            ["ORE"] = new JObject { ["mint"] = Ore, ["decimals"] = 11 }
        };

        [Test]
        public void NativeLamportsPreserveU64PrecisionAndRealZero()
        {
            var large = WalletBalanceParser.Native(Result(new JValue(ulong.MaxValue)));
            Assert.AreEqual("18446744073709551615", large.RawAmount);
            Assert.AreEqual(18446744073.709551615m, large.Amount);
            Assert.AreEqual(0m, WalletBalanceParser.Native(Result(new JValue(0))).Amount);
            Assert.Throws<FormatException>(() => WalletBalanceParser.Native(Result(JValue.CreateNull())));
            Assert.Throws<FormatException>(() => WalletBalanceParser.Native(Result(new JValue(-1))));
            Assert.Throws<FormatException>(() => WalletBalanceParser.Native(Result(new JValue(0.5))));
            Assert.Throws<FormatException>(() => WalletBalanceParser.Native(new JObject { ["value"] = 0 }));
        }

        [Test]
        public void TokenHoldingsSumAllAccountsExactlyAndAcceptConfirmedEmptyList()
        {
            var mint = WalletBalanceParser.Mint(Mint());
            var value = WalletBalanceParser.Tokens(Result(new JArray(Account("9007199254740993"),
                Account("7", account: OtherWallet))), Wallet, Usdc, mint);
            Assert.AreEqual("9007199254741000", value.RawAmount);
            Assert.AreEqual(9007199254.741m, value.Amount);
            Assert.AreEqual(0m, WalletBalanceParser.Tokens(Result(new JArray()), Wallet, Usdc, mint).Amount);
            Assert.Throws<FormatException>(() => WalletBalanceParser.Tokens(Result(JValue.CreateNull()), Wallet, Usdc, mint));
        }

        [Test]
        public void MismatchedOwnerMintDecimalsAndDuplicateAccountsAreRejected()
        {
            var mint = WalletBalanceParser.Mint(Mint());
            foreach (var invalid in new[] { Account("1", wallet: OtherWallet), Account("1", mint: Skr),
                Account("1", decimals: 5), Account("-1"), Account("1.5") })
                Assert.Throws<FormatException>(() => WalletBalanceParser.Tokens(Result(new JArray(invalid)), Wallet, Usdc, mint));
            Assert.Throws<FormatException>(() => WalletBalanceParser.Tokens(Result(new JArray(Account("1"), Account("1"))), Wallet, Usdc, mint));
            var badMint = Mint(); badMint["value"]["owner"] = Wallet;
            Assert.Throws<FormatException>(() => WalletBalanceParser.Mint(badMint));
            Assert.Throws<FormatException>(() => WalletBalanceParser.Mint(Result(JValue.CreateNull())));
        }

        [Test]
        public async Task UsesOnChainSkrDecimalsInsteadOfTheIncorrectConfigValue()
        {
            var rpc = new FakeRpc();
            using var service = new WalletBalanceService(() => Wallet, Currencies, rpc);
            await service.Refresh();
            Assert.AreEqual(SolanaCluster.Mainnet, service.State.Cluster);
            Assert.AreEqual(WalletBalanceStatus.Ready, service.State.Status);
            Assert.AreEqual(6, service.State.Token("SKR").Decimals);
            Assert.AreEqual(1m, service.State.Token("SKR").Amount);
            Assert.AreEqual("1", service.State.Token("SKR").FormatAmount());
            Assert.IsFalse(service.State.Token("SKR").IsStale);
            Assert.AreEqual(7, rpc.Calls.Count); // Four holdings reads plus three verified mint metadata reads.
        }

        [Test]
        public async Task PartialFailureDoesNotInventAZeroAndPreservesOtherAssets()
        {
            var rpc = new FakeRpc { FailMint = Skr };
            using var service = new WalletBalanceService(() => Wallet, Currencies, rpc);
            await service.Refresh();
            Assert.AreEqual(WalletBalanceStatus.Partial, service.State.Status);
            Assert.AreEqual(WalletTokenState.Unavailable, service.State.Token("SKR").State);
            Assert.IsNull(service.State.Token("SKR").Amount);
            Assert.IsNull(service.State.Token("SKR").FormatAmount());
            Assert.AreEqual("rateLimited", service.State.Token("SKR").ErrorCode);
            Assert.AreEqual(2m, service.State.Token("SOL").Amount);
        }

        [Test]
        public async Task FailedRefreshKeepsExplicitlyStaleLastObservation()
        {
            var now = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
            var rpc = new FakeRpc();
            using var service = new WalletBalanceService(() => Wallet, Currencies, rpc, now: () => now);
            await service.Refresh();
            var observed = service.State.Token("USDC").UpdatedAtUtc;
            now += TimeSpan.FromSeconds(31); rpc.FailMint = Usdc;
            await service.Refresh();
            var token = service.State.Token("USDC");
            Assert.AreEqual(1m, token.Amount);
            Assert.AreEqual(observed, token.UpdatedAtUtc);
            Assert.IsTrue(token.IsStale);
            Assert.AreEqual(WalletTokenState.Unavailable, token.State);
            Assert.AreEqual("rateLimited", token.ErrorCode);
            now += TimeSpan.FromSeconds(31); rpc.FailMint = null;
            await service.Refresh();
            Assert.IsFalse(service.State.Token("USDC").IsStale);
        }

        [Test]
        public async Task CoalescesRefreshAndIgnoresLateResultsFromAnotherWallet()
        {
            string wallet = Wallet;
            var pending = new TaskCompletionSource<JObject>();
            var rpc = new FakeRpc { NativeWait = pending.Task };
            using var service = new WalletBalanceService(() => wallet, Currencies, rpc);
            var first = service.Refresh();
            Assert.AreSame(first, service.Refresh(true));
            Assert.IsTrue(service.State.IsRefreshing);
            wallet = OtherWallet; service.Invalidate();
            Assert.IsNull(service.State.Token("SOL").Amount);
            pending.SetResult(Result(new JValue(99000000000L)));
            await first;
            Assert.AreEqual(OtherWallet, service.State.Wallet);
            Assert.IsNull(service.State.Token("SOL").Amount);
            rpc.NativeWait = null;
            await service.Refresh();
            Assert.AreEqual(2m, service.State.Token("SOL").Amount);
            wallet = null; service.Invalidate();
            Assert.AreEqual(WalletBalanceStatus.Disconnected, service.State.Status);
            Assert.IsNull(service.State.Token("USDC").Amount);
        }

        [Test]
        public async Task RefreshCooldownAndMetadataCacheBoundRepeatedRequests()
        {
            var now = DateTimeOffset.UtcNow; var rpc = new FakeRpc();
            using var service = new WalletBalanceService(() => Wallet, Currencies, rpc, now: () => now);
            await service.Refresh(); await service.Refresh(); await service.Refresh(true);
            Assert.AreEqual(7, rpc.Calls.Count);
            now += TimeSpan.FromSeconds(6); await service.Refresh(true);
            Assert.AreEqual(11, rpc.Calls.Count);
        }

        [Test]
        public async Task DevnetMissingMintsStayUnavailableAndNeverFallBackToMainnet()
        {
            var rpc = new FakeRpc();
            using var service = new WalletBalanceService(() => Wallet, Currencies, rpc, SolanaCluster.Devnet);
            await service.Refresh();
            Assert.AreEqual(SolanaCluster.Devnet, service.State.Cluster);
            Assert.IsNull(service.State.Token("SKR").Amount);
            Assert.AreEqual("tokenNotConfigured", service.State.Token("SKR").ErrorCode);
            Assert.IsNull(service.State.Token("ORE").Amount);
            Assert.IsTrue(rpc.Clusters.TrueForAll(c => c == SolanaCluster.Devnet));
            Assert.IsFalse(rpc.Calls.Exists(c => c.Contains(Skr) || c.Contains(Ore)));
        }

        [Test]
        public void ChipLabelsFollowTheEnvironmentCluster()
        {
            // queue 0b (2026-10-25): staging (devnet) showed "WALLET · MAINNET · CHECKING…"
            var rpc = new FakeRpc();
            using var devnet = new WalletBalanceService(() => Wallet, Currencies, rpc, SolanaCluster.Devnet);
            using var mainnet = new WalletBalanceService(() => Wallet, Currencies, rpc, SolanaCluster.Mainnet);
            Assert.AreEqual("DEVNET", devnet.ClusterLabel);
            Assert.AreEqual("MAINNET", mainnet.ClusterLabel);
            Assert.AreEqual("WALLET · DEVNET · CHECKING…", CTLife.UI.Strings.F("wallet.refreshing", devnet.ClusterLabel));
            Assert.AreEqual("WALLET · MAINNET · LAST CHECK", CTLife.UI.Strings.F("wallet.stale", mainnet.ClusterLabel));
            var staging = EnvConfig.CreateDefault(EnvId.Staging); Assert.AreEqual(SolanaCluster.Devnet, staging.cluster); UnityEngine.Object.DestroyImmediate(staging);
        }

        [Test]
        public async Task TinyPositiveBalanceIsNotDisplayedAsZeroAndDisconnectedWalletDoesNotFetch()
        {
            var rpc = new FakeRpc { NativeWait = Task.FromResult(Result(new JValue(1))) };
            using var service = new WalletBalanceService(() => Wallet, Currencies, rpc);
            await service.Refresh();
            Assert.AreEqual("<0.0001", service.State.Token("SOL").FormatAmount());
            using var offline = new WalletBalanceService(() => null, Currencies, rpc);
            int count = rpc.Calls.Count; await offline.Refresh(true);
            Assert.AreEqual(count, rpc.Calls.Count);
            Assert.AreEqual(WalletBalanceStatus.Disconnected, offline.State.Status);
        }

        [Test]
        public async Task TransportTimeoutEndsLoadingWithoutInventingABalance()
        {
            var rpc = new FakeRpc { NativeWait = Task.FromCanceled<JObject>(new CancellationToken(true)) };
            using var service = new WalletBalanceService(() => Wallet, Currencies, rpc);
            await service.Refresh();
            Assert.IsFalse(service.State.IsRefreshing);
            Assert.AreEqual(WalletBalanceStatus.Partial, service.State.Status);
            Assert.AreEqual(WalletTokenState.Unavailable, service.State.Token("SOL").State);
            Assert.AreEqual("networkUnavailable", service.State.Token("SOL").ErrorCode);
            Assert.IsNull(service.State.Token("SOL").Amount);
        }

        [Test]
        public async Task ConfigInvalidationClearsOldMintValuesAndAgeMarksOldObservationsStale()
        {
            var now = DateTimeOffset.UtcNow; var config = Currencies();
            using var service = new WalletBalanceService(() => Wallet, () => config, new FakeRpc(), now: () => now);
            await service.Refresh();
            now += TimeSpan.FromMinutes(2);
            Assert.IsTrue(service.State.Token("USDC").IsStale);
            config["USDC"]["mint"] = Skr;
            service.Invalidate();
            Assert.AreEqual(Skr, service.State.Token("USDC").Mint);
            Assert.IsNull(service.State.Token("USDC").Amount);
            await service.Refresh();
            Assert.AreEqual(1m, service.State.Token("USDC").Amount);
        }

        [Test]
        public async Task UnexpectedRefreshSetupFailureEndsRefreshingAndMarksPriorDataStale()
        {
            bool fail = false; var now = DateTimeOffset.UtcNow;
            using var service = new WalletBalanceService(() => Wallet,
                () => fail ? throw new FormatException("Fixture config read failed") : Currencies(), new FakeRpc(), now: () => now);
            await service.Refresh(); now += TimeSpan.FromMinutes(1); fail = true;
            await service.Refresh();
            Assert.IsFalse(service.State.IsRefreshing);
            Assert.AreEqual(WalletBalanceStatus.Unavailable, service.State.Status);
            Assert.AreEqual(2m, service.State.Token("SOL").Amount);
            Assert.IsTrue(service.State.Token("SOL").IsStale);
        }

        private sealed class FakeRpc : IWalletBalanceRpc
        {
            public string FailMint;
            public Task<JObject> NativeWait;
            public readonly List<string> Calls = new();
            public readonly List<SolanaCluster> Clusters = new();
            public Task<JObject> Read(SolanaCluster cluster, string method, JArray args, CancellationToken ct)
            {
                Calls.Add(method + args); Clusters.Add(cluster);
                if (method == "getBalance") return NativeWait ?? Task.FromResult(Result(new JValue(2000000000L)));
                string mint = method == "getAccountInfo" ? (string)args[0] : (string)args[1]["mint"];
                if (mint == FailMint) throw new WalletBalanceReadException("rateLimited");
                int decimals = mint == Ore ? 11 : 6;
                if (method == "getAccountInfo") return Task.FromResult(Mint(decimals));
                return Task.FromResult(Result(new JArray(Account(decimals == 11 ? "100000000000" : "1000000", mint, (string)args[0], decimals: decimals))));
            }
        }
    }
}
