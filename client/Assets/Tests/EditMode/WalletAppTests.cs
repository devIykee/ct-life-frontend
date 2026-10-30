using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    public sealed class WalletAppTests
    {
        private WalletBalanceService _previous, _service;
        private WalletApp _app;
        private const string Wallet = "11111111111111111111111111111111";

        [SetUp]
        public void SetUp() => Services.TryGet(out _previous);

        [TearDown]
        public void TearDown()
        {
            _app?.Closed();
            _service?.Dispose();
            Services.Unregister<WalletBalanceService>();
            if (_previous != null) Services.Register(_previous);
        }

        private VisualElement Open(BalanceRpc rpc, string wallet = Wallet)
        {
            _service = Services.Register(new WalletBalanceService(() => wallet, () => new JObject(), rpc));
            _app = new WalletApp();
            return _app.Open();
        }

        [Test]
        public async Task ConfirmedZeroIsDistinctFromMissingTokenObservations()
        {
            var page = Open(new BalanceRpc { Lamports = 0 });
            await _service.Refresh();
            var values = page.Query<Label>(className: "ct-wallet-amount").ToList();
            Assert.AreEqual(4, values.Count);
            Assert.AreEqual("0", values[0].text);
            for (int i = 1; i < values.Count; i++)
                Assert.AreEqual(WalletStrings.T("unknown"), values[i].text);
            Assert.AreEqual(WalletStrings.T("mainnet"), page.Q<Label>(className: "ct-wallet-network").text);
        }

        [Test]
        public async Task DetailPageKeepsOneLamportVisibleAtFullPrecision()
        {
            var page = Open(new BalanceRpc { Lamports = 1 });
            await _service.Refresh();
            Assert.AreEqual("0.000000001", page.Q<Label>(className: "ct-wallet-amount").text);
        }

        [Test]
        public void DisconnectedPageHasNoInventedBalancesOrRefreshAction()
        {
            var rpc = new BalanceRpc();
            var page = Open(rpc, null);
            Assert.AreEqual(WalletStrings.T("disconnected"), page.Q<Label>(className: "ct-wallet-status").text);
            Assert.IsEmpty(page.Query<Label>(className: "ct-wallet-amount").ToList());
            Assert.IsFalse(page.Q<Button>("wallet-refresh").enabledSelf, "no refresh action without a wallet");
            Assert.AreEqual(0, rpc.Calls);
        }

        [Test]
        public async Task ClosingDetachesPageButLeavesSharedBalanceServiceUsable()
        {
            var rpc = new BalanceRpc { Lamports = 1 };
            var page = Open(rpc);
            await _service.Refresh();
            _app.Closed();
            rpc.Lamports = 2;
            _service.Invalidate();
            await _service.Refresh();
            Assert.AreEqual(0.000000002m, _service.State.Token("SOL").Amount);
            Assert.AreEqual("0.000000001", page.Q<Label>(className: "ct-wallet-amount").text);
        }

        [Test]
        public void WalletListsCashButNoLoanWithoutDebt()
        {
            var page = Open(new BalanceRpc(), null);
            Assert.IsNotNull(page.Q<DuoTokenWallet>(), "the Wallet app uses the token wallet layout");
            Assert.IsNotNull(page.Q("wallet-token-cash"), "game Cash is always listed");
            Assert.IsNull(page.Q("wallet-token-loan"), "no debt reported, no Loan row");
            Assert.AreEqual(4, page.Q("wallet-tokens").Query(className: "duo-tw-row").ToList().Count, "SOL, USDC, SKR and ORE rows");
            Assert.IsEmpty(page.Query<Label>(className: "ct-wallet-amount").ToList(), "disconnected: no token amounts at all");
        }

        private sealed class BalanceRpc : IWalletBalanceRpc
        {
            public long Lamports;
            public int Calls;
            public Task<JObject> Read(SolanaCluster cluster, string method, JArray parameters, CancellationToken ct)
            {
                Calls++;
                Assert.AreEqual("getBalance", method, "Unconfigured token mints must not trigger invented requests.");
                return Task.FromResult(new JObject { ["context"] = new JObject { ["slot"] = 42 }, ["value"] = Lamports });
            }
        }
    }
}
