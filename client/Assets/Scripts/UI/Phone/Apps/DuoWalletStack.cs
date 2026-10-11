using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Apple-Wallet style card stack (Duo board #5): CT Cash (balance on its strip), Loan (only while there is debt) and
    /// the connected Solana wallet (real SOL/USDC/SKR/ORE observations from <see cref="WalletBalanceService"/>, the
    /// lobby's source). Tapping a card's strip brings it to the front with its actions. Values are never invented:
    /// a missing observation reads "Unavailable", a disconnected wallet shows no amounts.
    /// </summary>
    public sealed class DuoWalletStack : VisualElement, IDisposable
    {
        public enum Card { Cash, Loan, Solana }

        private static readonly string[] Symbols = { "SOL", "USDC", "SKR", "ORE" };
        private const float StripStep = 92f;

        private readonly Action _topUp, _repay;
        private readonly Dictionary<Card, VisualElement> _cards = new Dictionary<Card, VisualElement>();
        private WalletBalanceService _wallet;
        private readonly Subscriptions _subs = new Subscriptions();
        private IVisualElementScheduledItem _tick;
        private Card _front;
        private bool _disposed;

        public Card Front => _front;

        /// <param name="front">Card shown in front first (Bank: Cash, Wallet: Solana).</param>
        /// <param name="topUp">Opens the Cash top-up flow; null hides the action (no payments yet).</param>
        /// <param name="repay">Opens the Repay sheet (POST /bank/repay); null hides the action.</param>
        public DuoWalletStack(Card front, Action topUp, Action repay = null)
        {
            _repay = repay;
            AddToClassList("duo-wallet-stack");
            AddToClassList("ct-wallet-page");
            _front = front;
            _topUp = topUp;
            Services.TryGet(out _wallet);
            if (_wallet != null) _wallet.Changed += Rebuild;
            _subs.On<ProfileChangedSignal>(_ => Rebuild());
            RegisterCallback<AttachToPanelEvent>(e =>
            {
                if (e.target != this || _disposed) return;
                // Updating the visible page also ages cached observations; the shared service coalesces RPC work.
                _tick ??= schedule.Execute(() => { Rebuild(); RefreshWallet(false); }).Every(30000);
            });
            RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == this) Dispose(); });
            Rebuild();
            RefreshWallet(false);
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

        /// <summary>Brings a card to the front (also used by a deep link such as the dock's Wallet).</summary>
        public void Show(Card card)
        {
            if (!_cards.ContainsKey(card)) return;
            _front = card;
            Layout();
        }

        private async void RefreshWallet(bool force)
        {
            var service = _wallet;
            if (service == null) return;
            try { await service.Refresh(force); }
            catch (Exception e) { Log.Exception("wallet-balance-ui", e); }
        }

        // ------------------------------------------------------------------ build

        private void Rebuild()
        {
            if (_disposed) return;
            Clear();
            _cards.Clear();
            Services.TryGet<PlayerProfile>(out var p);
            _cards[Card.Cash] = CashCard(p);
            if (p != null && p.DebtCents > 0) _cards[Card.Loan] = LoanCard(p);
            _cards[Card.Solana] = SolanaCard();
            foreach (var c in _cards.Values) Add(c);
            if (!_cards.ContainsKey(_front)) _front = Card.Cash;
            Layout();
        }

        private void Layout()
        {
            int i = 0, stacked = _cards.Count - 1;
            foreach (var pair in _cards)
            {
                bool front = pair.Key == _front;
                var card = pair.Value;
                card.EnableInClassList("duo-wcard-front", front);
                card.EnableInClassList("duo-wcard-stacked", !front);
                card.style.position = front ? Position.Relative : Position.Absolute;
                card.style.top = front ? 0 : i * StripStep;
                card.style.marginTop = front ? stacked * StripStep : 0;
                if (!front) i++;
            }
            _cards[_front].BringToFront();
        }

        private VisualElement Shell(Card kind, string tint, string name, string stripValue, string stripValueClass = null)
        {
            var card = El.Div("duo-wcard duo-wcard-" + tint);
            card.name = "wallet-card-" + kind.ToString().ToLowerInvariant();
            var sheen = El.Div("duo-wcard-sheen");
            sheen.style.backgroundImage = Background.FromTexture2D(Duo.Sheen);
            card.Add(El.PassThrough(sheen));
            // The strip is the part of a stacked card you can see and tap.
            var strip = new Button(() => { Bus.Publish(new SfxSignal("sfx_ui_tap")); Show(kind); }) { tooltip = name };
            strip.AddToClassList("duo-wcard-strip");
            strip.name = card.name + "-strip";
            strip.Add(El.PassThrough(El.Text(name, "duo-wcard-name")));
            var value = El.Text(stripValue ?? "", "duo-wcard-strip-value" + (stripValueClass != null ? " " + stripValueClass : ""));
            value.pickingMode = PickingMode.Ignore;
            strip.Add(value);
            card.Add(strip);
            return card;
        }

        private static VisualElement Body(params VisualElement[] children) => El.Div("duo-wcard-body", children);

        private VisualElement CashCard(PlayerProfile p)
        {
            string cash = p != null ? Money.Cash(p.CashCents) : "—";
            var card = Shell(Card.Cash, "cash", Strings.T("wallet.card.cash"), cash);
            var body = Body(El.Text(Strings.T("wallet.card.balance"), "duo-wcard-caption"),
                El.Text(cash, "duo-wcard-amount"), El.Text(Strings.T("wallet.card.cashNote"), "duo-wcard-note"));
            if (_topUp != null)
            {
                var top = Duo.Pill(Strings.T("wallet.card.topUp"), _topUp, Duo.PillKind.Primary);
                top.name = "wallet-topup";
                top.AddToClassList("duo-wcard-pill");
                body.Add(El.Div("duo-wcard-actions", top));
            }
            card.Add(body);
            return card;
        }

        private VisualElement LoanCard(PlayerProfile p)
        {
            string debt = Money.Cash(p.DebtCents);
            var card = Shell(Card.Loan, "loan", Strings.T("wallet.card.loan"), "−" + debt, "duo-wcard-owed");
            var body = Body(El.Text(Strings.T("wallet.card.toPayBack"), "duo-wcard-caption"), El.Text(debt, "duo-wcard-amount"));
            // The instalment the server schedules (weekly amount, due at Sunday Unlock); nothing is shown when it sent none.
            var due = DebtDue.Line(p.DebtWeeklyDueCents, p.DebtDueAtMs, ServerClock.NowMs);
            var dueLabel = El.Text(due ?? Strings.T("wallet.card.loanNote"), "duo-wcard-note" + (due != null ? " duo-wcard-due" : ""));
            dueLabel.name = "wallet-loan-due";
            body.Add(dueLabel);
            if (_repay != null)
            {
                var repay = Duo.Pill(Strings.T("wallet.card.repay"), _repay, Duo.PillKind.Primary);
                repay.name = "wallet-repay";
                repay.AddToClassList("duo-wcard-pill");
                body.Add(El.Div("duo-wcard-actions", repay));
            }
            card.Add(body);
            return card;
        }

        private VisualElement SolanaCard()
        {
            var snapshot = _wallet?.State;
            bool connected = snapshot != null && snapshot.Status != WalletBalanceStatus.Disconnected;
            string address = connected ? Short(snapshot.Wallet) : Strings.T("wallet.card.notConnected");
            var card = Shell(Card.Solana, "solana", Strings.T("wallet.card.solana"), address);
            var body = Body();
            var network = El.Text(WalletStrings.T(snapshot != null && snapshot.Cluster == SolanaCluster.Mainnet ? "mainnet" : "devnet"), "duo-wcard-caption ct-wallet-network");
            body.Add(network);
            var status = El.Text("", "duo-wcard-note ct-wallet-status");
            if (snapshot == null)
            {
                status.text = WalletStrings.T("unavailable");
                body.Add(status);
            }
            else
            {
                status.text = WalletStrings.T(snapshot.IsRefreshing ? "refreshing" : snapshot.Status switch
                {
                    WalletBalanceStatus.Disconnected => "disconnected",
                    WalletBalanceStatus.Unavailable => "unavailable",
                    WalletBalanceStatus.Partial => "partial",
                    WalletBalanceStatus.Loading => "refreshing",
                    _ => "observed",
                });
                status.Show(snapshot.IsRefreshing || snapshot.Status != WalletBalanceStatus.Ready);
                if (connected)
                {
                    // SOL is the hero; the other tokens sit underneath. Full precision; missing never becomes zero.
                    bool anyStale = false;
                    for (int i = 0; i < Symbols.Length; i++)
                    {
                        var token = snapshot.Token(Symbols[i]);
                        string amount = token?.FormatAmount(token.Decimals);
                        string text = amount ?? WalletStrings.T(token?.State == WalletTokenState.Loading ? "loading" : "unknown");
                        if (i == 0)
                        {
                            body.Add(El.Div("duo-wcard-hero-row", El.Text(text, "duo-wcard-amount ct-wallet-amount"),
                                El.Text(amount != null ? Symbols[i] : "", "duo-wcard-symbol")));
                        }
                        else
                        {
                            body.Add(El.Div("duo-wcard-token", El.Text(WalletStrings.T("name." + Symbols[i]), "duo-wcard-token-name"),
                                El.Text(text, "duo-wcard-token-value ct-wallet-amount"), El.Text(amount != null ? Symbols[i] : "", "duo-wcard-token-symbol")));
                        }
                        anyStale |= token?.IsStale == true;
                    }
                    // One note for the card (it repeated under every token on the Seeker).
                    if (anyStale) body.Add(El.Text(WalletStrings.T("stale"), "duo-wcard-note ct-wallet-status ct-wallet-stale"));
                }
                body.Add(status);
            }
            var refresh = Duo.Pill(WalletStrings.T("refresh"), () => RefreshWallet(true));
            refresh.name = "wallet-refresh";
            refresh.AddToClassList("duo-wcard-pill");
            refresh.SetEnabled(connected && !snapshot.IsRefreshing);
            body.Add(El.Div("duo-wcard-actions", refresh));
            card.Add(body);
            return card;
        }

        private static string Short(string w) => string.IsNullOrEmpty(w) || w.Length < 10 ? w : w.Substring(0, 4) + "…" + w.Substring(w.Length - 4);
    }
}
