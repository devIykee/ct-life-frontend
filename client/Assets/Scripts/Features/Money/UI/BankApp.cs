using System;
using CTLife.Core;
using FeatureFlags = CTLife.Core.Features;
using CTLife.Net;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// Bank (feature #13): Cash balance and debt from GET /me, plus Cash top-ups in USDC, SOL, SKR and ORE through
    /// payment intents, MWA sign_and_send, intent polling and the city room's `payment_confirmed`.
    /// Registered twice: id "bank" (Phase 1 tile; the top-up section appears from Phase 2) and id "topup" (Phase 2 tile).
    /// </summary>
    public sealed class MoneyBankApp : MoneyApp
    {
        private readonly string _id;
        private readonly bool _topUpTile;

        public MoneyBankApp(bool topUpTile = false)
        {
            _topUpTile = topUpTile;
            _id = topUpTile ? "topup" : "bank";
        }

        public override string Id => _id;
        public override string Title => _topUpTile ? "Top up" : MoneyStrings.T("bank.title");
        public override string Color => "#f7b731";
        public override int Phase => _topUpTile ? 2 : 1;
        public override string Feature => _topUpTile ? FeatureFlags.Payments : null;

        internal override MoneyView CreateView(MoneyHub hub) => new BankView(hub, showTopUp: _topUpTile || (Shell?.CurrentPhase ?? 1) >= 2);
    }

    internal sealed class BankView : MoneyView
    {
        private readonly bool _showTopUp;
        private readonly TopUpFlow _flow;
        private readonly TopUpCatalog _catalog;
        private readonly SolanaCluster _cluster;
        private readonly VisualElement _hero = El.Div("ct-col"), _topUp = El.Div("ct-col");
        private string _currency = "USDC";
        private bool _ack;
        private Label _countdown;

        public BankView(MoneyHub hub, bool showTopUp) : base(hub)
        {
            _showTopUp = showTopUp;
            _flow = hub.TopUp;
            _cluster = Env.Current.cluster;
            _catalog = TopUpCatalog.Parse(Services.TryGet<ConfigStore>(out var cfg) ? cfg.Config("economy") : null);
            foreach (var code in TopUpCatalog.CurrencyOrder)
                if (_catalog.Availability(code, _cluster).CanPay) { _currency = code; break; }

            Root.Add(MoneyUi.Text(MoneyStrings.T("bank.title"), "ct-h1"));
            Root.Add(_hero);
            if (_showTopUp) Root.Add(_topUp);
            RenderHero();
            Subs.On<ProfileChangedSignal>(_ => RenderHero());
            Subs.On<DegradedChangedSignal>(_ => RenderTopUp());
            _flow.Changed += FlowChanged;
            Hub.RefreshProfile(force: true);
            RenderTopUp();
            if (_flow.Stage == TopUpStage.Pending) _ = Check();
        }

        public override void Dispose()
        {
            _flow.Changed -= FlowChanged;
            base.Dispose();
        }

        private void FlowChanged()
        {
            if (!Alive) return;
            if (_flow.Stage == TopUpStage.Confirmed) Hub.RefreshProfile(force: true);
            RenderTopUp();
        }

        protected override void Tick()
        {
            if (_countdown == null || _flow.Intent == null) return;
            if (_flow.Stage == TopUpStage.Review && _flow.Intent.ExpiresAtMs.HasValue)
                _countdown.text = MoneyFormat.Countdown((_flow.Intent.ExpiresAtMs.Value - ServerClock.NowMs) / 1000.0);
            else if (_flow.Stage == TopUpStage.Confirming)
                _countdown.text = MoneyStrings.F("topup.confirming", (int)Math.Max(0, (ServerClock.NowMs - _flow.PollStartedMs) / 1000.0));
        }

        private void RenderHero()
        {
            _hero.Clear();
            if (!Services.TryGet<PlayerProfile>(out var p)) return;
            _hero.Add(El.Div("ct-bank-hero",
                MoneyUi.Text(MoneyStrings.T("bank.balance"), "ct-eyebrow"),
                MoneyUi.Text(MoneyFormat.Cash(p.CashCents), "ct-big-number"),
                MoneyUi.Small(MoneyStrings.T("bank.note"))));
            _hero.Add(MoneyUi.Card(MoneyUi.Text(p.DebtCents > 0 ? MoneyStrings.F("bank.debt", MoneyFormat.Cash(p.DebtCents)) : MoneyStrings.T("bank.noDebt"))));
        }

        private bool WalletAvailable => Services.TryGet<IWalletSignIn>(out var w) && w.IsAvailable && Services.TryGet<ITransactionSigner>(out _);

        private void RenderTopUp()
        {
            if (!_showTopUp || !Alive) return;
            _topUp.Clear();
            _countdown = null;
            _topUp.Add(MoneyUi.Text(MoneyStrings.T("topup.title"), "ct-h2"));
            switch (_flow.Stage)
            {
                case TopUpStage.Choose:
                case TopUpStage.Error:
                    RenderChoose();
                    break;
                case TopUpStage.Creating:
                    _topUp.Add(MoneyUi.Notice(MoneyStrings.T("topup.creating")));
                    break;
                case TopUpStage.Review:
                    RenderReview();
                    break;
                case TopUpStage.Signing:
                    _topUp.Add(MoneyUi.Notice(MoneyStrings.T("topup.signing")));
                    break;
                case TopUpStage.Confirming:
                    _countdown = MoneyUi.Notice(MoneyStrings.F("topup.confirming", 0));
                    _topUp.Add(_countdown);
                    AddSignature();
                    break;
                case TopUpStage.Confirmed:
                    _topUp.Add(MoneyUi.Card(
                        MoneyUi.Text(MoneyStrings.T("topup.confirmed"), "ct-h2 ct-money-up"),
                        MoneyUi.Text(_flow.CreditedCashCents.HasValue ? MoneyStrings.F("topup.credited", MoneyFormat.Cash(_flow.CreditedCashCents.Value)) : MoneyStrings.T("topup.creditedSoon"))));
                    AddSignature();
                    _topUp.Add(MoneyUi.Btn(MoneyStrings.T("common.done"), () => _flow.Reset()));
                    break;
                case TopUpStage.Pending:
                    _topUp.Add(MoneyUi.Card(
                        MoneyUi.Text(MoneyStrings.T("topup.pending"), "ct-h2"),
                        MoneyUi.Text(MoneyStrings.T(_flow.SignUncertain ? "topup.pendingUnsure" : "topup.pendingBody")),
                        _flow.StatusUnavailable ? MoneyUi.Small(MoneyStrings.T("topup.statusUnavailable")) : null));
                    AddSignature();
                    _topUp.Add(MoneyUi.Row(
                        MoneyUi.Btn(MoneyStrings.T("topup.checkAgain"), () => _ = Check()),
                        MoneyUi.Btn(MoneyStrings.T("topup.newTopup"), () => _flow.Reset(), "ct-btn ct-btn-ghost")));
                    break;
                case TopUpStage.Expired:
                case TopUpStage.Failed:
                    _topUp.Add(MoneyUi.Card(
                        MoneyUi.Text(MoneyStrings.T(_flow.Stage == TopUpStage.Expired ? "topup.expired" : "topup.failedTitle"), "ct-h2 ct-money-down"),
                        MoneyUi.Text(MoneyStrings.Err(_flow.Error))));
                    _topUp.Add(MoneyUi.Btn(MoneyStrings.T("topup.newTopup"), () => _flow.Reset()));
                    break;
            }
        }

        private void AddSignature()
        {
            if (!string.IsNullOrEmpty(_flow.Signature))
                _topUp.Add(MoneyUi.Small(MoneyStrings.F("topup.signature", MoneyFormat.Short(_flow.Signature, 6, 6))));
        }

        private void RenderChoose()
        {
            _topUp.Add(MoneyUi.Small(MoneyStrings.T("topup.note")));
            bool paymentsOn = FeatureFlags.IsAvailable(FeatureFlags.Payments);
            if (!paymentsOn) _topUp.Add(MoneyUi.Notice(MoneyStrings.T("topup.paymentsOff"), "warn"));
            if (!WalletAvailable) _topUp.Add(MoneyUi.Notice(MoneyStrings.T("topup.walletOff"), "warn"));
            if (_flow.Stage == TopUpStage.Error && _flow.Error != null)
            {
                _topUp.Add(MoneyUi.Notice(MoneyStrings.Err(_flow.Error), "error"));
                if (_flow.Error.OutcomeUnknown && _flow.Sku != null)
                    _topUp.Add(MoneyUi.Btn(MoneyStrings.T("common.retry"), () => Create(_flow.Sku, _flow.Currency)));
            }

            _topUp.Add(MoneyUi.Text(MoneyStrings.T("topup.currency"), "ct-section-title"));
            var chips = El.Div("ct-app-tabs ct-money-tabs");
            foreach (var code in TopUpCatalog.CurrencyOrder)
            {
                var c = code;
                var access = _catalog.Availability(c, _cluster);
                var chip = MoneyUi.Chip(c, () => { _currency = c; _ack = false; RenderTopUp(); }, c == _currency);
                chip.SetEnabled(access.CanPay);
                chips.Add(chip);
            }
            _topUp.Add(chips);
            var avail = _catalog.Availability(_currency, _cluster);
            var bonus = _catalog.Currencies.TryGetValue(_currency, out var cur) ? cur.BonusBps : 0;
            _topUp.Add(MoneyUi.Small(MoneyStrings.T(avail.ReasonKey) + (bonus > 0 ? "  " + MoneyStrings.F("topup.bonus", MoneyFormat.Bps(bonus, false)) : "")));

            if (!_catalog.Loaded)
            {
                _topUp.Add(MoneyUi.Notice(MoneyStrings.T("err.invalidResponse"), "error"));
                return;
            }
            foreach (var pack in _catalog.Packs)
            {
                var p = pack;
                var price = _currency == "USDC" ? MoneyStrings.F("topup.packPrice", p.PriceUsdc) : MoneyStrings.F("topup.packQuote", p.PriceUsdc, _currency);
                var button = MoneyUi.Btn(MoneyStrings.T("topup.choose"), () => Create(p.Id, _currency));
                // Creating an intent moves nothing, so it is allowed without a wallet (editor verification against a local
                // backend); "Pay with wallet" stays disabled until MWA is available.
                button.SetEnabled(paymentsOn && avail.CanPay && _flow.CanStartNew);
                _topUp.Add(MoneyUi.Card(
                    MoneyUi.Row(MoneyUi.Text(p.Label, "ct-h2"), El.Div("ct-grow"), p.BestValue ? MoneyUi.Text(MoneyStrings.T("topup.best"), "ct-badge") : null),
                    MoneyUi.Text(MoneyStrings.F("topup.get", MoneyFormat.Cash(p.CashCents))),
                    MoneyUi.Small(price),
                    button));
            }
        }

        private void Create(string sku, string currency)
        {
            _ack = false;
            _ = CreateAsync(sku, currency);
        }

        private async System.Threading.Tasks.Task CreateAsync(string sku, string currency)
        {
            try { await _flow.Create(sku, currency, Hub.Life); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Exception("money", e); }
        }

        private void RenderReview()
        {
            var intent = _flow.Intent;
            var currency = intent.Currency ?? _flow.Currency;
            int decimals = _catalog.Decimals(currency, -1);
            string amount = decimals >= 0 ? MoneyFormat.Token(intent.Amount, decimals, currency) : (intent.Amount ?? "—") + " " + currency;
            var access = _catalog.Availability(currency, _cluster);
            _countdown = MoneyUi.Text("", "ct-money-value");
            _topUp.Add(MoneyUi.Card(
                MoneyUi.Text(MoneyStrings.T("topup.reviewTitle"), "ct-h2"),
                MoneyUi.Pair(MoneyStrings.T("topup.pay"), amount),
                MoneyUi.Pair(MoneyStrings.T("topup.receive"), intent.CashCents.HasValue ? MoneyFormat.Cash(intent.CashCents.Value) : MoneyStrings.T("topup.notShown")),
                intent.BonusBps.HasValue && intent.BonusBps.Value > 0 ? MoneyUi.Pair(MoneyStrings.T("topup.bonusLine"), MoneyFormat.Bps(intent.BonusBps.Value)) : null,
                MoneyUi.Pair(MoneyStrings.T("topup.to"), MoneyFormat.Short(intent.Recipient)),
                El.Div("ct-row ct-money-pair", MoneyUi.Text(MoneyStrings.T("topup.expires"), "ct-money-key"), El.Div("ct-grow"), _countdown),
                MoneyUi.Small(access.NeedsMainnetConfirm ? "" : MoneyStrings.T("topup.devnet"))));
            Tick();

            if (_flow.Error != null) _topUp.Add(MoneyUi.Notice(MoneyStrings.Err(_flow.Error), "error"));
            if (!intent.HasTx && !string.IsNullOrEmpty(intent.SolanaPayUrl))
                _topUp.Add(MoneyUi.Card(MoneyUi.Small(MoneyStrings.T("topup.solanaPay")), MoneyUi.Small(MoneyFormat.Short(intent.SolanaPayUrl, 24, 8)),
                    MoneyUi.Btn(MoneyStrings.T("topup.copyLink"), () => MoneyUi.Copy(intent.SolanaPayUrl), "ct-btn ct-btn-ghost")));

            var pay = MoneyUi.Btn(MoneyStrings.T("topup.payWallet"), () => _ = PayAsync());
            if (access.NeedsMainnetConfirm)
            {
                _topUp.Add(MoneyUi.Notice(MoneyStrings.T("topup.mainnetTitle") + ". " + MoneyStrings.F("topup.mainnetBody", currency), "warn"));
                _topUp.Add(MoneyUi.Ack(MoneyStrings.F("topup.mainnetAck", currency), on => { _ack = on; pay.SetEnabled(CanPay(intent, access)); }));
            }
            pay.SetEnabled(CanPay(intent, access));
            _topUp.Add(MoneyUi.Row(pay, MoneyUi.Btn(MoneyStrings.T("common.cancel"), () => _flow.Reset(), "ct-btn ct-btn-ghost")));
        }

        private bool CanPay(PaymentIntentRow intent, CurrencyAvailability access) =>
            intent.HasTx && access.CanPay && WalletAvailable && (!access.NeedsMainnetConfirm || _ack);

        private async System.Threading.Tasks.Task PayAsync()
        {
            var intent = _flow.Intent;
            if (intent == null) return;
            var access = _catalog.Availability(intent.Currency ?? _flow.Currency, _cluster);
            if (!CanPay(intent, access)) return; // Re-checked at tap time: mainnet needs the acknowledgement.
            try { await _flow.Pay(Hub.Life); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Exception("money", e); }
        }

        private async System.Threading.Tasks.Task Check()
        {
            try { await _flow.CheckAgain(Hub.Life); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Exception("money", e); }
        }
    }
}
