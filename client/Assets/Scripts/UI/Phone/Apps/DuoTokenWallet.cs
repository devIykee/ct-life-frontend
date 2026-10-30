using System;
using CTLife.Core;
using CTLife.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// The Wallet app laid out like a phone crypto wallet (Phantom-style): account chip with the short address, the SOL
    /// balance large, a row of round actions, then a token list (SOL, USDC, SKR, ORE) and the in-game money (Cash, Loan).
    /// Same data as <see cref="DuoWalletStack"/>: observations from <see cref="WalletBalanceService"/>, Cash and debt
    /// from the profile. Values are never invented: no USD prices (the game has no price feed for these), a missing
    /// observation reads Unavailable. Only real actions: copy address, top up Cash, open Pulse, refresh.
    /// Rebuilds only on a wallet or profile change, plus a 30 s refresh while shown.
    /// </summary>
    public sealed class DuoTokenWallet : VisualElement, IDisposable
    {
        private static readonly string[] Symbols = { "SOL", "USDC", "SKR", "ORE" };

        private readonly Action _topUp, _repay, _trade;
        private readonly Subscriptions _subs = new Subscriptions();
        private WalletBalanceService _wallet;
        private IVisualElementScheduledItem _tick;
        private bool _disposed;

        public DuoTokenWallet(Action topUp, Action repay, Action trade)
        {
            _topUp = topUp; _repay = repay; _trade = trade;
            AddToClassList("duo-tw");
            Services.TryGet(out _wallet);
            if (_wallet != null) _wallet.Changed += Rebuild;
            _subs.On<ProfileChangedSignal>(_ => Rebuild());
            RegisterCallback<AttachToPanelEvent>(e =>
            {
                if (e.target != this || _disposed) return;
                _tick ??= schedule.Execute(() => Refresh(false)).Every(30000);
            });
            RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == this) Dispose(); });
            Rebuild();
            Refresh(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tick?.Pause();
            _subs.Dispose();
            if (_wallet != null) _wallet.Changed -= Rebuild;
            _wallet = null;
        }

        private async void Refresh(bool force)
        {
            var service = _wallet;
            if (service == null) return;
            try { await service.Refresh(force); }
            catch (Exception e) { Log.Exception("wallet-balance-ui", e); }
        }

        private void Rebuild()
        {
            if (_disposed) return;
            Clear();
            var snap = _wallet?.State;
            bool connected = snap != null && snap.Status != WalletBalanceStatus.Disconnected;
            Services.TryGet<PlayerProfile>(out var profile);

            // Account chip: the network and the short address (tap = copy).
            var account = new Button(() => CopyAddress(snap)) { name = "wallet-account", tooltip = WalletStrings.T("copy") };
            account.AddToClassList("duo-tw-account");
            account.Add(El.PassThrough(Duo.Avatar(profile?.Alias ?? "?", snap?.Wallet ?? "wallet", "duo-avatar-s")));
            account.Add(El.PassThrough(El.Text(connected ? Short(snap.Wallet) : Strings.T("wallet.card.notConnected"), "duo-tw-address")));
            account.Add(El.PassThrough(El.Text(WalletStrings.T(snap != null && snap.Cluster == SolanaCluster.Mainnet ? "mainnet" : "devnet"), "duo-tw-network ct-wallet-network")));
            Add(El.Div("duo-tw-account-row", account));

            // The hero: SOL, large.
            var sol = connected ? snap.Token("SOL") : null;
            string solText = sol?.FormatAmount(sol.Decimals);
            var hero = El.Div("duo-tw-hero");
            hero.Add(El.Text(solText != null ? solText + " SOL" : connected ? WalletStrings.T(sol?.State == WalletTokenState.Loading ? "loading" : "unknown") : "—", "duo-tw-balance"));
            string status = snap == null ? WalletStrings.T("unavailable") : snap.IsRefreshing ? WalletStrings.T("refreshing")
                : snap.Status switch
                {
                    WalletBalanceStatus.Disconnected => WalletStrings.T("disconnected"),
                    WalletBalanceStatus.Unavailable => WalletStrings.T("unavailable"),
                    WalletBalanceStatus.Partial => WalletStrings.T("partial"),
                    _ => WalletStrings.T("observed"),
                };
            hero.Add(El.Text(status, "duo-tw-status ct-wallet-status"));
            Add(hero);

            // Round actions, like a wallet app's Receive / Buy / Swap row (only ones that really work).
            var actions = El.Div("duo-tw-actions");
            actions.Add(ActionButton("receive", "down", WalletStrings.T("receive"), () => CopyAddress(snap), connected));
            if (_topUp != null) actions.Add(ActionButton("topup", "topup", WalletStrings.T("topUp"), _topUp, true));
            if (_trade != null) actions.Add(ActionButton("trade", "up", WalletStrings.T("trade"), _trade, true));
            actions.Add(ActionButton("refresh", "rotate", WalletStrings.T("refreshShort"), () => Refresh(true), connected && !snap.IsRefreshing));
            Add(actions);

            // Tokens.
            Add(Duo.GroupHeader(WalletStrings.T("holdings")));
            var tokens = Duo.Group();
            tokens.name = "wallet-tokens";
            bool anyStale = false;
            foreach (var symbol in Symbols)
            {
                var token = connected ? snap.Token(symbol) : null;
                string amount = token?.FormatAmount(token.Decimals);
                string value = amount ?? (connected ? WalletStrings.T(token?.State == WalletTokenState.Loading ? "loading" : "unknown") : "—");
                // ct-wallet-amount marks a real observation slot (tests: four when connected, none when not).
                tokens.Add(TokenRow(symbol, WalletStrings.T("name." + symbol), symbol, value, null, connected ? "ct-wallet-amount" : null));
                anyStale |= token?.IsStale == true;
            }
            LastRow(tokens);
            Add(tokens);
            if (anyStale) Add(Duo.Footnote(WalletStrings.T("stale")));

            // The game's own money: Cash (and the loan while there is one).
            Add(Duo.GroupHeader(WalletStrings.T("inGame")));
            var game = Duo.Group();
            game.name = "wallet-game-money";
            game.Add(TokenRow("CASH", Strings.T("wallet.card.cash"), WalletStrings.T("cashTicker"), profile != null ? Money.Cash(profile.CashCents) : "—", _topUp));
            if (profile != null && profile.DebtCents > 0)
            {
                var due = DebtDue.Line(profile.DebtWeeklyDueCents, profile.DebtDueAtMs, ServerClock.NowMs);
                var loan = TokenRow("LOAN", Strings.T("wallet.card.loan"), due ?? Strings.T("wallet.card.toPayBack"), "−" + Money.Cash(profile.DebtCents), _repay);
                loan.AddToClassList("duo-tw-row-owed");
                game.Add(loan);
            }
            LastRow(game);
            Add(game);
        }

        private static VisualElement ActionButton(string id, string icon, string label, Action onClick, bool enabled)
        {
            var b = new Button(() => { Bus.Publish(new SfxSignal("sfx_ui_tap")); onClick?.Invoke(); }) { name = "wallet-" + id, tooltip = label };
            b.AddToClassList("duo-tw-action");
            b.Add(El.PassThrough(El.Div("duo-tw-action-face", new UiIcon(icon))));
            b.Add(El.PassThrough(El.Text(label, "duo-tw-action-label")));
            b.SetEnabled(enabled);
            return b;
        }

        /// <summary>A list row: token badge, name over ticker, amount on the right. Tappable when it has an action.</summary>
        private static VisualElement TokenRow(string badge, string name, string sub, string amount, Action onClick, string amountClass = null)
        {
            VisualElement row = onClick != null ? (VisualElement)new Button(onClick) : new VisualElement();
            row.AddToClassList("duo-tw-row");
            row.name = "wallet-token-" + badge.ToLowerInvariant();
            var icon = El.Div("duo-tw-token duo-tw-token--" + badge.ToLowerInvariant(), El.Text(badge.Substring(0, 1), "duo-tw-token-letter"));
            icon.pickingMode = PickingMode.Ignore;
            row.Add(icon);
            var col = El.PassThrough(El.Div("duo-tw-row-text", El.Text(name, "duo-tw-row-name"), El.Text(sub ?? "", "duo-tw-row-sub")));
            row.Add(col);
            row.Add(El.PassThrough(El.Text(amount, "duo-tw-row-amount" + (amountClass != null ? " " + amountClass : ""))));
            return row;
        }

        /// <summary>No divider under the final row (USS has no :last-child).</summary>
        private static void LastRow(VisualElement group)
        {
            if (group.childCount > 0) group[group.childCount - 1].AddToClassList("duo-row-last");
        }

        private static void CopyAddress(WalletBalanceSnapshot snap)
        {
            if (snap == null || string.IsNullOrEmpty(snap.Wallet)) return;
            GUIUtility.systemCopyBuffer = snap.Wallet;
            Bus.Publish(ToastSignal.Text(WalletStrings.T("copied"), ToastKind.Good));
        }

        private static string Short(string w) => string.IsNullOrEmpty(w) || w.Length < 10 ? w : w.Substring(0, 4) + "…" + w.Substring(w.Length - 4);
    }
}
