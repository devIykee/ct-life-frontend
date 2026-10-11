using System;
using CTLife.Core;
using CTLife.Net;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Observed token holdings. The shared service owns requests and wallet identity.</summary>
    public sealed class WalletApp : PhoneApp
    {
        private static readonly string[] Symbols = { "SOL", "USDC", "SKR", "ORE" };
        private WalletBalanceService _service;
        private VisualElement _tokens;
        private Label _network, _address, _status;
        private Button _refresh;
        private IVisualElementScheduledItem _tick;

        public override string Id => "wallet";
        public override string Title => WalletStrings.T("title");

        public override VisualElement Open()
        {
            Closed();
            _network = El.Text("", "ct-wallet-network");
            _address = El.Text("", "ct-wallet-address");
            _status = El.Text("", "ct-wallet-status");
            _tokens = El.Div("ct-wallet-tokens");
            _refresh = El.Btn(WalletStrings.T("refresh"), () => Refresh(true), "ct-btn");
            var page = AppUi.Page(
                El.Div("ct-wallet-summary", _network, _address, _status),
                _tokens, _refresh,
                El.Text(WalletStrings.T("cashNote"), "ct-small ct-muted"));
            page.AddToClassList("ct-wallet-page");
            if (!Services.TryGet(out _service))
            {
                _network.text = WalletStrings.T("network");
                _status.text = WalletStrings.T("unavailable");
                _refresh.SetEnabled(false);
                return page;
            }
            _service.Changed += Render;
            Render();
            Refresh(false);
            // Updating the visible page also ages cached observations; the shared service coalesces RPC work.
            _tick = page.schedule.Execute(() => { Render(); Refresh(false); }).Every(30000);
            return page;
        }

        private async void Refresh(bool force)
        {
            var service = _service;
            if (service == null) return;
            try { await service.Refresh(force); }
            catch (Exception e) { Log.Exception("wallet-balance-ui", e); }
        }

        private void Render()
        {
            if (_service == null || _tokens == null) return;
            var snapshot = _service.State;
            bool connected = snapshot.Status != WalletBalanceStatus.Disconnected;
            _network.text = WalletStrings.T(snapshot.Cluster == SolanaCluster.Mainnet ? "mainnet" : "devnet");
            _address.text = connected ? snapshot.Wallet : "";
            _status.text = WalletStrings.T(snapshot.IsRefreshing ? "refreshing" : snapshot.Status switch
            {
                WalletBalanceStatus.Disconnected => "disconnected",
                WalletBalanceStatus.Unavailable => "unavailable",
                WalletBalanceStatus.Partial => "partial",
                WalletBalanceStatus.Loading => "refreshing",
                _ => "observed",
            });
            _status.Show(snapshot.IsRefreshing || snapshot.Status != WalletBalanceStatus.Ready);
            _refresh.SetEnabled(connected && !snapshot.IsRefreshing);
            _tokens.Clear();
            if (!connected) return;
            foreach (string symbol in Symbols)
            {
                var token = snapshot.Token(symbol);
                // Full precision in the detail app. Missing observations never become a zero balance.
                string amount = token?.FormatAmount(token.Decimals);
                string state = amount != null ? amount : WalletStrings.T(token?.State == WalletTokenState.Loading ? "loading" : "unknown");
                var copy = El.Div("ct-grow", El.Text(WalletStrings.T("name." + symbol), "ct-wallet-name"),
                    El.Text(state, "ct-wallet-amount"));
                if (token?.IsStale == true)
                    copy.Add(El.Text(WalletStrings.T("stale"), "ct-wallet-status ct-wallet-stale"));
                _tokens.Add(El.Div("ct-wallet-token",
                    El.Div("ct-wallet-symbol ct-wallet-" + symbol.ToLowerInvariant(), El.Text(symbol)), copy));
            }
        }

        public override void Closed()
        {
            _tick?.Pause();
            _tick = null;
            if (_service != null) _service.Changed -= Render;
            _service = null;
            _tokens = null;
        }
    }
}
