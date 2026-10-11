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
        private readonly bool _topUpTile, _repay;

        /// <param name="repay">Registered as id "repay": the Bank page with the Repay sheet open (the Wallet's Loan card uses it).</param>
        public MoneyBankApp(bool topUpTile = false, bool repay = false)
        {
            _topUpTile = topUpTile;
            _repay = repay && !topUpTile;
            _id = topUpTile ? "topup" : _repay ? "repay" : "bank";
        }

        public override string Id => _id;
        public override string Title => _topUpTile ? "Top up" : MoneyStrings.T("bank.title");
        public override string Color => "#f7b731";
        public override int Phase => _topUpTile ? 2 : 1;
        public override string Feature => _topUpTile ? FeatureFlags.Payments : null;
        public override DuoChrome Chrome => DuoChrome.Plain;
        public override bool ShowOnHome => !_repay;

        internal override MoneyView CreateView(MoneyHub hub) =>
            new BankView(hub, Shell, Title, showTopUp: _topUpTile || (Shell?.CurrentPhase ?? 1) >= 2, openTopUp: _topUpTile, openRepay: _repay);
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

        public BankView(MoneyHub hub, PhoneShell phone, string title, bool showTopUp, bool openTopUp, bool openRepay = false) : base(hub)
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
            _stack = new DuoWalletStack(DuoWalletStack.Card.Cash, _showTopUp ? OpenTopUp : (Action)null, OpenRepay);
            _page.Body.Add(_stack);
            _page.Body.Add(_latest);
            Subs.On<ProfileChangedSignal>(_ => { if (_repaySheet != null) RenderRepay(); });
            Subs.On<DegradedChangedSignal>(_ => RenderTopUp());
            _flow.Changed += FlowChanged;
            Hub.RefreshProfile(force: true);
            RenderTopUp();
            RenderLatest();
            LoadLedger();
            if (_flow.Stage == TopUpStage.Pending) _ = Check();
            if (openTopUp && _showTopUp) _page.schedule.Execute(OpenTopUp);
            else if (openRepay) _page.schedule.Execute(() => { _stack.Show(DuoWalletStack.Card.Loan); OpenRepay(); });
        }

        public override void Dispose()
        {
            _flow.Changed -= FlowChanged;
            _sheet?.Close();
            _repaySheet?.Close();
            _ledgerSheet?.Close();
            _stack?.Dispose();
            base.Dispose();
        }

        private void FlowChanged()
        {
            if (!Alive) return;
            if (_flow.Stage == TopUpStage.Confirmed) { Hub.RefreshProfile(force: true); LoadLedger(); }
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

        /// <summary>
        /// Latest transactions: a top-up still in flight this session (its live stage), then the newest Cash ledger rows
        /// (GET /ledger) with "See all" for the paged history.
        /// </summary>
        private void RenderLatest()
        {
            _latest.Clear();
            bool topUp = _showTopUp && !(_flow.Stage == TopUpStage.Choose || (_flow.Stage == TopUpStage.Error && _flow.Intent == null));
            bool rows = _ledger != null && _ledger.Rows.Items.Count > 0;
            if (!topUp && !rows && !_ledgerFailed) return;
            _latest.Add(Duo.SectionHeader(Strings.T("wallet.latest"), rows ? MoneyStrings.T("ledger.seeAll") : null, rows ? OpenLedger : (Action)null));
            var list = El.Div("duo-list");
            list.name = "bank-latest";
            if (topUp) list.Add(TopUpRow());
            if (rows) for (int i = 0; i < _ledger.Rows.Items.Count && i < LatestCount; i++) list.Add(LedgerUi.Row(_ledger.Rows.Items[i]));
            if (_ledgerFailed && !rows) list.Add(new DuoRow(LoadLedger).Title(MoneyStrings.T("ledger.unavailable")).Subtitle(MoneyStrings.T("common.retry")));
            Duo.MarkLast(list);
            _latest.Add(list);
        }

        private const int LatestCount = 5;

        private VisualElement TopUpRow()
        {
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
            return row;
        }

        // ------------------------------------------------------------------ the Cash ledger

        private LedgerPage _ledger;
        private bool _ledgerFailed;
        private int _ledgerGen;

        private async void LoadLedger()
        {
            int gen = ++_ledgerGen;
            try
            {
                var page = await Hub.Api.Ledger(null, LatestCount, Hub.Life);
                if (!Alive || gen != _ledgerGen) return;
                _ledger = page; _ledgerFailed = false;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                Log.Exception("money", e);
                if (!Alive || gen != _ledgerGen) return;
                _ledgerFailed = true;
            }
            RenderLatest();
        }

        private DuoSheet _ledgerSheet;

        private void OpenLedger()
        {
            if (!Alive) return;
            _ledgerSheet?.Close();
            _ledgerSheet = LedgerUi.OpenAll(Hub, _page);
        }

        // ------------------------------------------------------------------ repay

        private DuoSheet _repaySheet;
        private readonly VisualElement _repay = El.Div("duo-repay");
        private readonly RetainedWrite _repayWrite = new RetainedWrite();
        private long _repayAmount;
        private bool _repaying;
        private MoneyError _repayError;
        private RepayReceipt _repayDone;

        private void OpenRepay()
        {
            if (!Alive) return;
            _repaySheet?.Close();
            _repay.RemoveFromHierarchy();
            _repayError = null; _repayDone = null; _repayAmount = 0;
            var scroll = El.VerticalScroll("duo-sheet-scroll");
            scroll.Add(_repay);
            var sheet = new DuoSheet(MoneyStrings.T("repay.title"));
            sheet.name = "bank-repay-sheet";
            sheet.AddContent(scroll);
            _repaySheet = sheet;
            sheet.Closed += () => { if (_repaySheet == sheet) _repaySheet = null; };
            RenderRepay();
            sheet.Show(_page);
        }

        private void RenderRepay()
        {
            if (!Alive) return;
            _repay.Clear();
            Services.TryGet<PlayerProfile>(out var p);
            long debt = p?.DebtCents ?? 0, cash = p?.CashCents ?? 0;
            if (_repayDone != null)
            {
                _repay.Add(Duo.Empty(_repayDone.DebtCents == 0 ? MoneyStrings.T("repay.cleared") : MoneyStrings.F("repay.paid", MoneyFormat.Cash(_repayDone.RepaidCents)),
                    _repayDone.DebtCents == 0 ? null : MoneyStrings.F("repay.left", MoneyFormat.Cash(_repayDone.DebtCents))));
                _repay.Add(SheetButton(MoneyStrings.T("common.done"), () => _repaySheet?.Close(), name: "repay-done"));
                return;
            }
            if (debt <= 0)
            {
                _repay.Add(Duo.Empty(MoneyStrings.T("err.nothingToRepay")));
                _repay.Add(SheetButton(MoneyStrings.T("common.done"), () => _repaySheet?.Close()));
                return;
            }
            var summary = El.Div("duo-list",
                Pair(MoneyStrings.T("repay.owed"), MoneyFormat.Cash(debt)),
                Pair(MoneyStrings.T("repay.cash"), MoneyFormat.Cash(cash)));
            var due = DebtDue.Line(p.DebtWeeklyDueCents, p.DebtDueAtMs, ServerClock.NowMs);
            if (due != null) summary.Add(Pair(MoneyStrings.T("repay.thisWeek"), due));
            Duo.MarkLast(summary);
            _repay.Add(summary);
            var chips = RepayChips.For(debt, p.DebtWeeklyDueCents, cash);
            if (_repayAmount <= 0 || !chips.Exists(c => c.AmountCents == _repayAmount)) _repayAmount = RepayChips.Default(chips)?.AmountCents ?? 0;
            _repay.Add(Duo.GroupHeader(MoneyStrings.T("repay.amount")));
            var row = El.Div("duo-topup-currencies duo-repay-chips");
            foreach (var chip in chips)
            {
                var c = chip;
                string label = c.Kind == RepayChips.Kind.All ? MoneyStrings.F("repay.all", MoneyFormat.Cash(c.AmountCents))
                    : c.Kind == RepayChips.Kind.Weekly ? MoneyStrings.F("repay.weekly", MoneyFormat.Cash(c.AmountCents)) : MoneyFormat.Cash(c.AmountCents);
                var pill = Duo.Pill(label, () => { _repayAmount = c.AmountCents; _repayError = null; RenderRepay(); }, c.AmountCents == _repayAmount ? Duo.PillKind.Primary : Duo.PillKind.Plain);
                pill.name = "repay-chip-" + c.AmountCents;
                pill.SetEnabled(c.Affordable && !_repaying);
                row.Add(pill);
            }
            _repay.Add(row);
            if (!chips.Exists(c => c.Affordable)) _repay.Add(Duo.Notice(MoneyStrings.T("repay.noCash"), Duo.Tone.Warning));
            if (_repayError != null) _repay.Add(Duo.Notice(MoneyStrings.Err(_repayError), _repayError.OutcomeUnknown ? Duo.Tone.Warning : Duo.Tone.Danger));
            var pay = SheetButton(_repaying ? MoneyStrings.T("repay.paying") : MoneyStrings.F("repay.pay", MoneyFormat.Cash(Math.Max(0, _repayAmount))), () => _ = RepayAsync(), name: "repay-pay");
            pay.SetEnabled(!_repaying && _repayAmount > 0 && _repayAmount <= cash);
            _repay.Add(pay);
            _repay.Add(Duo.Footnote(MoneyStrings.T("repay.note")));
        }

        private async System.Threading.Tasks.Task RepayAsync()
        {
            if (_repaying || _repayAmount <= 0) return;
            long amount = _repayAmount;
            var body = new Newtonsoft.Json.Linq.JObject { ["amountCents"] = amount };
            var key = _repayWrite.KeyFor("/bank/repay", RetainedWrite.Body(body));
            _repaying = true; _repayError = null;
            RenderRepay();
            try
            {
                var receipt = await Hub.Api.Repay(amount, key, Hub.Life);
                _repayWrite.Complete();
                if (Services.TryGet<PlayerProfile>(out var p)) p.ApplyRepay(receipt.CashCents, receipt.DebtCents);
                _repayDone = receipt;
                Bus.Publish(new SfxSignal("sfx_ui_success"));
                Hub.RefreshProfile(force: true);   // the new instalment (amount and due date) comes from the server
                LoadLedger();
            }
            catch (OperationCanceledException) { _repaying = false; return; }
            catch (Exception e)
            {
                var err = MoneyErrors.From(e, write: true);
                // a definite answer frees the key; an unknown outcome keeps it so a retry can never pay twice
                if (!err.OutcomeUnknown) _repayWrite.Complete();
                if (err.Is("NOTHING_TO_REPAY")) Hub.RefreshProfile(force: true);
                _repayError = err;
            }
            _repaying = false;
            if (Alive) RenderRepay();
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
