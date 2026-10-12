#if UNITY_INCLUDE_TESTS && (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CTLife.Tests
{
    /// <summary>Opt-in real Unity transport check; public system account only, never a player's wallet or transaction.</summary>
    public sealed class WalletBalanceRpcTests
    {
        [UnityTest]
        public IEnumerator ReadsPublicMainnetAccountThroughUnityTransport()
        {
            if (Environment.GetEnvironmentVariable("CTLIFE_WALLET_RPC_REVIEW") != "1")
                Assert.Ignore("Set CTLIFE_WALLET_RPC_REVIEW=1 to permit three read-only public RPC calls.");
            var work = Verify();
            while (!work.IsCompleted) yield return null;
            Assert.IsFalse(work.IsFaulted, work.Exception?.ToString());
            Assert.IsFalse(work.IsCanceled);
        }

        private static async Task Verify()
        {
            const string owner = "11111111111111111111111111111111";
            const string mint = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";
            var rpc = new WalletBalanceRpc();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var native = WalletBalanceParser.Native(await rpc.Read(SolanaCluster.Mainnet, "getBalance",
                new JArray(owner, new JObject { ["commitment"] = "confirmed" }), timeout.Token));
            var metadata = WalletBalanceParser.Mint(await rpc.Read(SolanaCluster.Mainnet, "getAccountInfo",
                new JArray(mint, new JObject { ["encoding"] = "jsonParsed", ["commitment"] = "confirmed" }), timeout.Token));
            var token = WalletBalanceParser.Tokens(await rpc.Read(SolanaCluster.Mainnet, "getTokenAccountsByOwner",
                new JArray(owner, new JObject { ["mint"] = mint },
                    new JObject { ["encoding"] = "jsonParsed", ["commitment"] = "confirmed" }), timeout.Token), owner, mint, metadata);
            Assert.AreEqual(9, native.Decimals);
            Assert.AreEqual(6, token.Decimals);
            Assert.That(native.Amount, Is.GreaterThanOrEqualTo(0m));
            Assert.That(token.Amount, Is.GreaterThanOrEqualTo(0m));
            string evidence = Environment.GetEnvironmentVariable("CTLIFE_WALLET_RPC_EVIDENCE");
            if (!string.IsNullOrEmpty(evidence))
            {
                Directory.CreateDirectory(evidence);
                File.WriteAllText(Path.Combine(evidence, "unity-public-rpc.json"), new JObject
                {
                    ["cluster"] = "mainnet", ["fixture"] = "public system-program account, not a connected player wallet",
                    ["observedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["solLamports"] = native.RawAmount,
                    ["usdcRaw"] = token.RawAmount, ["usdcDecimals"] = token.Decimals,
                    ["transport"] = "actual WalletBalanceRpc UnityWebRequest, three read-only calls"
                }.ToString());
            }
        }
    }
}
#endif
