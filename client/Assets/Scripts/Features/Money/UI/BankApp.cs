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
    /// Duo layout (board #5): the Wallet card stack with Cash in front, the top-up flow in a sheet, and Latest
    /// transactions listing only what the client really knows (this session's top-up). No history is invented.
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
        public override DuoChrome Chrome => DuoChrome.Plain;

        internal override MoneyView CreateView(MoneyHub hub) =>
            new BankView(hub, Shell, Title, showTopUp: _topUpTile || (Shell?.CurrentPhase ?? 1) >= 2, openTopUp: _topUpTile);
    }

    internal sealed class BankView : MoneyView
    {
        private readonly bool _showTopUp;
        private readonly TopUpFlow _flow;
        private readonly TopUpCatalog _catalog;
        private readonly SolanaCluster _cluster;
        private readonly DuoPage _page;
        private readonly DuoWalletStack _stack;
        private readonly VisualElement _topUp = El.Div("duo-topup"), _latest = El.Div("duo-latest");
        private DuoSheet _sheet;
        private string _currency = "USDC";
        private bool _ack;
        private Label _countdown;

        public override VisualElement Container => _page;

        public BankView(MoneyHub hub, PhoneShell phone, string title, bool showTopUp, bool openTopUp) : base(hub)
        {
            _showTopUp = showTopUp;
            _flow = hub.TopUp;
            _cluster = Env.Current.cluster;
            _catalog = TopUpCatalog.Parse(Services.TryGet<ConfigStore>(out var cfg) ? cfg.Config("economy") : null);
            foreach (var code in TopUpCatalog.CurrencyOrder)
                if (_catalog.Availability(code, _cluster).CanPay) { _currency = code; break; }

            _page = new DuoPage(Root, title);
            _page.name = "bank-page";
            _page.Nav.AddLeft(DuoGlyph.Kind.Back, Strings.T("common.back"), () => phone?.Back(), "bank-back");
            if (_showTopUp) _page.Nav.AddRight(DuoGlyph.Kind.Plus, MoneyStrings.T("topup.title"), OpenTopUp, "bank-plus");
            _stack = new DuoWalletStack(DuoWalletStack.Card.Cash, _showTopUp ? OpenTopUp : (Action)null);
            _page.Body.Add(_stack);
            _page.Body.Add(_latest);
            Subs.On<DegradedChangedSignal>(_ => RenderTopUp());
            _flow.Changed += FlowChanged;
            Hub.RefreshProfile(force: true);
            RenderTopUp();
            RenderLatest();
            if (_flow.Stage == TopUpStage.Pending) _ = Check();
            if (openTopUp && _showTopUp) _page.schedule.Execute(OpenTopUp);
        }

        public override void Dispose()
        {
            _flow.Changed -= FlowChanged;
            _sheet?.Close();
            _stack?.Dispose();
            base.Dispose();
        }

        private void FlowChanged()
        {
            if (!Alive) return;
            if (_flow.Stage == TopUpStage.Confirmed) Hub.RefreshProfile(force: true);
            RenderTopUp();
            RenderLatest();
        }

        protected override void Tick()
        {
            if (_countdown == null || _flow.Intent == null) return;
            if (_flow.Stage == TopUpStage.Review && _flow.Intent.ExpiresAtMs.HasValue)
                _countdown.text = MoneyFormat.Countdown((_flow.Intent.ExpiresAtMs.Value - ServerClock.NowMs) / 1000.0);
            else if (_flow.Stage == TopUpStage.Confirming)
                _countdown.text = MoneyStrings.F("topup.confirming", (int)Math.Max(0, (ServerClock.NowMs - _flow.PollStartedMs) / 1000.0));
        }

        // ------------------------------------------------------------------ latest transactions

        /// <summary>Only this session's top-up exists client-side (no ledger route): one row, or no section at all.</summary>
        private void RenderLatest()
        {
            _latest.Clear();
            if (!_showTopUp || _flow.Stage == TopUpStage.Choose || (_flow.Stage == TopUpStage.Error && _flow.Intent == null)) return;
            _latest.Add(Duo.SectionHeader(Strings.T("wallet.latest")));
            string subtitle = _flow.Stage switch
            {
                TopUpStage.Creating => MoneyStrings.T("topup.creating"),
                TopUpStage.Review => MoneyStrings.T("topup.reviewTitle"),
                TopUpStage.Signing => MoneyStrings.T("topup.signing"),
                TopUpStage.Confirming => MoneyStrings.F("topup.confirming", (int)Math.Max(0, (ServerClock.NowMs - _flow.PollStartedMs) / 1000.0)),
                TopUpStage.Confirmed => MoneyStrings.T("topup.confirmed"),
                TopUpStage.Pending => MoneyStrings.T("topup.pending"),
                TopUpStage.Expired => MoneyStrings.T("topup.expired"),
                TopUpStage.Failed => MoneyStrings.T("topup.failedTitle"),
                _ => MoneyStrings.T("topup.title"),
            };
            var row = new DuoRow(OpenTopUp).Leading(Duo.IconSquare("cash", _flow.Stage == TopUpStage.Confirmed ? "green" : "gray"))
                .Title(MoneyStrings.T("topup.title")).Subtitle(subtitle + (_flow.Currency != null ? " · " + _flow.Currency : ""));
            row.name = "bank-latest-topup";
            long? cash = _flow.CreditedCashCents ?? _flow.Intent?.CashCents;
            if (cash.HasValue) row.Value((_flow.Stage == TopUpStage.Confirmed ? "+" : "") + MoneyFormat.Cash(cash.Value),
                _flow.Stage == TopUpStage.Confirmed ? "duo-value-positive" : null);
            var list = El.Div("duo-list", row);
            Duo.MarkLast(list);
            _latest.Add(list);
        }

        // ------------------------------------------------------------------ top-up sheet

        private void OpenTopUp()
        {
            if (!_showTopUp || !Alive) return;
            _sheet?.Close();
            _topUp.RemoveFromHierarchy();
            var scroll = El.VerticalScroll("duo-sheet-scroll");
            scroll.Add(_topUp);
            _sheet = new DuoSheet(MoneyStrings.T("topup.title"));
            _sheet.name = "bank-topup-sheet";
            _sheet.AddContent(scroll);
            var sheet = _sheet;
            _sheet.Closed += () => { if (_sheet == sheet) _sheet = null; };
            RenderTopUp();
            _sheet.Show(_page);
        }

        private bool WalletAvailable => Services.TryGet<IWalletSignIn>(out var w) && w.IsAvailable && Services.TryGet<ITransactionSigner>(out _);

        private static Button SheetButton(string text, Action action, bool primary = true, string name = null)
        {
            var b = El.Btn(text, action, primary ? "duo-sheet-primary" : "duo-sheet-secondary");
            if (name != null) b.name = name;
            return b;
        }

        private static VisualElement Pair(string key, string value, Label valueLabel = null)
        {
            var row = new DuoRow().Title(key);
            if (valueLabel != null) { valueLabel.AddToClassList("duo-row-value"); row.Trailing(valueLabel); }
            else row.Value(value);
            return row;
        }

        private void RenderTopUp()
        {
            if (!_showTopUp || !Alive) return;
            _topUp.Clear();
            _countdown = null;
            switch (_flow.Stage)
            {
                case TopUpStage.Choose:
                case TopUpStage.Error:
                    RenderChoose();
                    break;
                case TopUpStage.Creating:
                    _topUp.Add(Duo.Notice(MoneyStrings.T("topup.creating")));
                    break;
                case TopUpStage.Review:
                    RenderReview();
                    break;
                case TopUpStage.Signing:
                    _topUp.Add(Duo.Notice(MoneyStrings.T("topup.signing")));
                    break;
                case TopUpStage.Confirming:
                    _countdown = El.Text(MoneyStrings.F("topup.confirming", 0), "duo-notice-text");
                    _topUp.Add(El.Div("duo-notice duo-notice-info", _countdown));
                    AddSignature();
                    break;
                case TopUpStage.Confirmed:
                    _topUp.Add(Duo.Empty(MoneyStrings.T("topup.confirmed"),
                        _flow.CreditedCashCents.HasValue ? MoneyStrings.F("topup.credited", MoneyFormat.Cash(_flow.CreditedCashCents.Value)) : MoneyStrings.T("topup.creditedSoon")));
                    AddSignature();
                    _topUp.Add(SheetButton(MoneyStrings.T("common.done"), () => { _flow.Reset(); _sheet?.Close(); }));
                    break;
                case TopUpStage.Pending:
                    _topUp.Add(Duo.Notice(MoneyStrings.T("topup.pending") + ". " +
                        MoneyStrings.T(_flow.SignUncertain ? "topup.pendingUnsure" : "topup.pendingBody"), Duo.Tone.Warning));
                    if (_flow.StatusUnavailable) _topUp.Add(Duo.Footnote(MoneyStrings.T("topup.statusUnavailable")));
                    AddSignature();
                    _topUp.Add(SheetButton(MoneyStrings.T("topup.checkAgain"), () => _ = Check()));
                    break;
                case TopUpStage.Expired:
                case TopUpStage.Failed:
                    _topUp.Add(Duo.Notice(MoneyStrings.T(_flow.Stage == TopUpStage.Expired ? "topup.expired" : "topup.failedTitle") + ". " +
                        MoneyStrings.Err(_flow.Error), _flow.Stage == TopUpStage.Expired ? Duo.Tone.Neutral : Duo.Tone.Danger));
                    _topUp.Add(SheetButton(MoneyStrings.T("topup.newTopup"), () => _flow.Reset()));
                    break;
            }
        }

        private void AddSignature()
        {
            if (!string.IsNullOrEmpty(_flow.Signature))
                _topUp.Add(Duo.Footnote(MoneyStrings.F("topup.signature", MoneyFormat.Short(_flow.Signature, 6, 6))));
        }

        private void RenderChoose()
        {
            _topUp.Add(Duo.Footnote(MoneyStrings.T("topup.note"), "duo-topup-note"));
            bool paymentsOn = FeatureFlags.IsAvailable(FeatureFlags.Payments);
            if (!paymentsOn) _topUp.Add(Duo.Notice(MoneyStrings.T("topup.paymentsOff"), Duo.Tone.Warning));
            if (!WalletAvailable) _topUp.Add(Duo.Notice(MoneyStrings.T("topup.walletOff"), Duo.Tone.Warning));
            if (_flow.Stage == TopUpStage.Error && _flow.Error != null)
            {
                _topUp.Add(Duo.Notice(MoneyStrings.Err(_flow.Error), Duo.Tone.Danger));
                if (_flow.Error.OutcomeUnknown && _flow.Sku != null)
                    _topUp.Add(El.Div("duo-topup-actions", Duo.Pill(MoneyStrings.T("common.retry"), () => Create(_flow.Sku, _flow.Currency))));
            }

            _topUp.Add(Duo.GroupHeader(MoneyStrings.T("topup.currency")));
            var chips = El.Div("duo-topup-currencies");
            foreach (var code in TopUpCatalog.CurrencyOrder)
            {
                var c = code;
                var access = _catalog.Availability(c, _cluster);
                var chip = Duo.Pill(c, () => { _currency = c; _ack = false; RenderTopUp(); }, c == _currency ? Duo.PillKind.Primary : Duo.PillKind.Plain);
                chip.name = "topup-currency-" + c;
                chip.SetEnabled(access.CanPay);
                chips.Add(chip);
            }
            _topUp.Add(chips);
            var avail = _catalog.Availability(_currency, _cluster);
            var bonus = _catalog.Currencies.TryGetValue(_currency, out var cur) ? cur.BonusBps : 0;
            _topUp.Add(Duo.Footnote(MoneyStrings.T(avail.ReasonKey) + (bonus > 0 ? "  " + MoneyStrings.F("topup.bonus", MoneyFormat.Bps(bonus, false)) : ""), "duo-topup-note"));

            if (!_catalog.Loaded)
            {
                _topUp.Add(Duo.Notice(MoneyStrings.T("err.invalidResponse"), Duo.Tone.Danger));
                return;
            }
            var packs = El.Div("duo-list");
            foreach (var pack in _catalog.Packs)
            {
                var p = pack;
                var price = _currency == "USDC" ? MoneyStrings.F("topup.packPrice", p.PriceUsdc) : MoneyStrings.F("topup.packQuote", p.PriceUsdc, _currency);
                var button = Duo.Pill(MoneyStrings.T("topup.choose"), () => Create(p.Id, _currency), Duo.PillKind.Primary);
                button.name = "topup-pack-" + p.Id;
                // Creating an intent moves nothing, so it is allowed without a wallet (editor verification against a local
                // backend); "Pay with wallet" stays disabled until MWA is available.
                button.SetEnabled(paymentsOn && avail.CanPay && _flow.CanStartNew);
                var row = new DuoRow().Leading(Duo.IconSquare("cash", p.BestValue ? "orange" : "green"))
                    .Title(p.Label + (p.BestValue ? " · " + MoneyStrings.T("topup.best") : ""), "duo-row-title-strong")
                    .Subtitle(MoneyStrings.F("topup.get", MoneyFormat.Cash(p.CashCents)) + " · " + price, twoLines: true).Trailing(button);
                packs.Add(row);
            }
            Duo.MarkLast(packs);
            _topUp.Add(packs);
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
            _countdown = El.Text("", "duo-row-value");
            var pairs = El.Div("duo-list duo-topup-review",
                Pair(MoneyStrings.T("topup.pay"), amount),
                Pair(MoneyStrings.T("topup.receive"), intent.CashCents.HasValue ? MoneyFormat.Cash(intent.CashCents.Value) : MoneyStrings.T("topup.notShown")),
                intent.BonusBps.HasValue && intent.BonusBps.Value > 0 ? Pair(MoneyStrings.T("topup.bonusLine"), MoneyFormat.Bps(intent.BonusBps.Value)) : null,
                Pair(MoneyStrings.T("topup.to"), MoneyFormat.Short(intent.Recipient)),
                Pair(MoneyStrings.T("topup.expires"), null, _countdown));
            Duo.MarkLast(pairs);
            _topUp.Add(pairs);
            if (!access.NeedsMainnetConfirm) _topUp.Add(Duo.Footnote(MoneyStrings.T("topup.devnet")));
            Tick();

            if (_flow.Error != null) _topUp.Add(Duo.Notice(MoneyStrings.Err(_flow.Error), Duo.Tone.Danger));
            if (!intent.HasTx && !string.IsNullOrEmpty(intent.SolanaPayUrl))
            {
                var copy = Duo.Pill(MoneyStrings.T("topup.copyLink"), () => MoneyUi.Copy(intent.SolanaPayUrl));
                var link = new DuoRow().Title(MoneyStrings.T("topup.solanaPay")).Subtitle(MoneyFormat.Short(intent.SolanaPayUrl, 24, 8)).Trailing(copy);
                _topUp.Add(El.Div("duo-list", link));
            }

            var pay = SheetButton(MoneyStrings.T("topup.payWallet"), () => _ = PayAsync(), name: "topup-pay");
            if (access.NeedsMainnetConfirm)
            {
                _topUp.Add(Duo.Notice(MoneyStrings.T("topup.mainnetTitle") + ". " + MoneyStrings.F("topup.mainnetBody", currency), Duo.Tone.Warning));
                var ack = new DuoToggle(_ack, on => { _ack = on; pay.SetEnabled(CanPay(intent, access)); }) { name = "topup-mainnet-ack" };
                _topUp.Add(El.Div("duo-list", ack.InRow(MoneyStrings.F("topup.mainnetAck", currency))));
            }
            pay.SetEnabled(CanPay(intent, access));
            _topUp.Add(pay);
            _topUp.Add(SheetButton(MoneyStrings.T("common.cancel"), () => _flow.Reset(), primary: false, name: "topup-cancel"));
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
