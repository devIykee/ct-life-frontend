using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.UI;
using FeatureFlags = CTLife.Core.Features;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// Perp Pit (feature #16) and Rekt Clinic tombstones (#17): open and close leveraged positions on fictional coins with
    /// the leverage cap from config `skills` (UI pre-check; the server enforces it), server entry and liquidation prices,
    /// realized PnL on close, and the city `liquidated` message.
    /// Duo layout (board #6): asset line, hero price and its real 5m change, an edge-to-edge chart of the coin's real
    /// 1-minute candles (GET /market/coins/:id; no data, no chart), timeframe pills only for windows the data covers,
    /// "Your position" for the selected coin, a glass Long/Short bar and order entry in a sheet.
    /// </summary>
    public sealed class PerpPitApp : MoneyApp
    {
        public override string Id => "perps";
        public override string Title => MoneyStrings.T("perps.title");
        public override string Color => "#eb3b5a";
        public override int Phase => 2;
        public override string Feature => FeatureFlags.Markets;
        public override DuoChrome Chrome => DuoChrome.Plain;

        private static bool _openRektClinic;

        /// <summary>Open on the Rekt Clinic tab the next time the app opens (REKT card button, a Rekt Clinic venue terminal).</summary>
        public static void RequestRektClinic() => _openRektClinic = true;

        internal override MoneyView CreateView(MoneyHub hub)
        {
            var v = new PerpPitView(hub, _openRektClinic ? "rekt" : "trade");
            _openRektClinic = false;
            return v;
        }
    }

    internal sealed class PerpPitView : MoneyView
    {
        private static readonly string[] Tabs = { "trade", "open", "history", "rekt" };

        private readonly DuoPage _page;
        private readonly DuoSegmented _tabs;
        private string _tab = "trade";
        private readonly VisualElement _body = El.Div("duo-perp-body");
        private readonly VisualElement _liqNotice = El.Div("ct-col");
        private readonly VisualElement _bar;
        private readonly Button _longButton, _shortButton;

        // Trade
        private string _coinId, _side = "long", _marginInput = "", _timeframe = "All";
        private int _leverage = 1;
        private int? _cap;
        private bool _opening;
        private readonly RetainedWrite _openWrite = new RetainedWrite();
        private VisualElement _tradeControls, _tradeStatus, _coinList, _hero, _position;
        private Button _openButton;
        private DuoSheet _orderSheet;

        // Selected coin's real price history
        private CoinDetail _detail;
        private string _detailCoin;
        private readonly PollGate _detailGate = new PollGate(15, 60);

        // Lists
        private Rows<PerpRow> _open, _history;
        private MoneyError _openError, _historyError;
        private readonly PollGate _openGate = new PollGate(5, 30), _historyGate = new PollGate(20, 60), _rektGate = new PollGate(30, 120);
        private readonly Dictionary<string, RetainedWrite> _closeWrites = new Dictionary<string, RetainedWrite>();
        private readonly HashSet<string> _closing = new HashSet<string>();
        private readonly Dictionary<string, string> _closeResult = new Dictionary<string, string>();
        private Rows<TombstoneRow> _tombstones;
        private MoneyError _rektError;

        public override VisualElement Container => _page;

        public PerpPitView(MoneyHub hub, string tab) : base(hub)
        {
            _page = new DuoPage(Root, MoneyStrings.T("perps.title"), largeTitle: false);
            _page.name = "perps-page";
            _page.AddToClassList("duo-perps");
            _page.Nav.AddLeft(DuoGlyph.Kind.Back, Strings.T("common.back"), () => Phone?.Back(), "perps-back");
            _tabs = new DuoSegmented(Tabs.Select(t => MoneyStrings.T("perps.tab." + t)).ToList(), 0, i => { _tab = Tabs[i]; Render(); });
            _tabs.name = "perps-tabs";
            _page.Body.Add(_liqNotice);
            _page.Body.Add(El.Div("duo-perp-tabs", _tabs));
            _page.Body.Add(_body);
            // Glass Long/Short bar: opens order entry for the selected coin.
            _longButton = El.Btn(MoneyStrings.T("perps.long"), () => OpenOrder("long"), "duo-perp-long");
            _longButton.name = "perps-long";
            _shortButton = El.Btn(MoneyStrings.T("perps.short"), () => OpenOrder("short"), "duo-perp-short");
            _shortButton.name = "perps-short";
            _bar = Duo.BottomBar(_longButton, _shortButton);
            _bar.name = "perps-bar";
            _page.Add(_bar);
            Subs.On<ProfileChangedSignal>(_ => { ComputeCap(); RenderTradeControls(); });
            Subs.On<DegradedChangedSignal>(_ => Render());
            Subs.On<LiquidatedMessage>(OnLiquidated);
            Hub.Market.Changed += MarketChanged;
            ComputeCap();
            int index = Array.IndexOf(Tabs, tab);
            _tab = index >= 0 ? tab : "trade";
            _tabs.Select(Math.Max(0, index), false);
            Render();
        }

        public override void Dispose()
        {
            Hub.Market.Changed -= MarketChanged;
            _orderSheet?.Close();
            base.Dispose();
        }

        private static long Cash => Services.TryGet<PlayerProfile>(out var p) ? p.CashCents : 0;
        private static bool MarketsOn => FeatureFlags.IsAvailable(FeatureFlags.Markets);
        private int MaxLeverage => Math.Min(Tickets.ContractMaxLeverage, _cap ?? Tickets.ContractMaxLeverage);
        private MarketCoin Selected => _coinId == null ? null : Hub.Market.List(null).FirstOrDefault(c => c.CoinId == _coinId);

        private void ComputeCap()
        {
            var me = Services.TryGet<PlayerProfile>(out var p) ? p.Me : null;
            var skills = Services.TryGet<ConfigStore>(out var cfg) ? cfg.Config("skills") : null;
            _cap = LeverageCap.Compute(skills, me?.Skills != null ? me.Skills.Trading : (int?)null, me?.Character?.TribeId);
            if (_leverage > MaxLeverage) _leverage = MaxLeverage;
        }

        private void MarketChanged()
        {
            if (_tab != "trade") return;
            RenderCoinList();
            RenderHero();
            SyncBar();
        }

        private void OnLiquidated(LiquidatedMessage m)
        {
            var model = RektCardModel.From(m.perpId, m.coinId, m.lossCents, Hub.Market.Ticker);
            _liqNotice.Clear();
            _liqNotice.Add(Duo.Notice(MoneyStrings.T("perps.liquidated") + (model.Ticker != null ? " $" + model.Ticker : "") + " " + model.LossText, Duo.Tone.Danger));
            _openGate.Kick();
            _historyGate.Kick();
            _rektGate.Kick();
        }

        protected override void Tick()
        {
            double now = Now;
            switch (_tab)
            {
                case "trade":
                    Hub.Market.Pump(new string[] { null }, Life);
                    if (_openGate.Due(now)) FetchOpen(); // "Your position" for the selected coin
                    if (_coinId != null && _detailGate.Due(now)) FetchDetail(_coinId);
                    break;
                case "open":
                    Hub.Market.Pump(new string[] { null }, Life);
                    if (_openGate.Due(now)) FetchOpen();
                    break;
                case "history":
                    if (_historyGate.Due(now)) FetchHistory();
                    break;
                case "rekt":
                    if (_rektGate.Due(now)) FetchRekt();
                    break;
            }
        }

        private void Render()
        {
            if (!Alive) return;
            _body.Clear();
            _coinList = _hero = _position = null;
            if (!MarketsOn) _body.Add(Duo.Notice(MoneyStrings.T("common.marketsOff"), Duo.Tone.Warning));
            _bar.Show(_tab == "trade");
            _page.EnableInClassList("duo-perps-trade", _tab == "trade");
            switch (_tab)
            {
                case "trade": RenderTrade(); break;
                case "open": RenderOpen(); break;
                case "history": RenderHistory(); break;
                case "rekt": RenderRekt(); break;
            }
            SyncBar();
        }

        private void SyncBar()
        {
            bool can = !_opening && MarketsOn && Selected != null;
            _longButton.SetEnabled(can);
            _shortButton.SetEnabled(can);
            _openButton?.SetEnabled(can);
        }

        // ------------------------------------------------------------------ trade

        private void RenderTrade()
        {
            _hero = El.Div("duo-perp-hero");
            _body.Add(_hero);
            RenderHero();
            _position = El.Div("duo-perp-position");
            _body.Add(_position);
            RenderPosition();
            _body.Add(Duo.SectionHeader(MoneyStrings.T("perps.markets")));
            _coinList = El.Div("duo-list duo-perp-markets");
            _body.Add(_coinList);
            RenderCoinList();
        }

        private void RenderHero()
        {
            if (_hero == null) return;
            _hero.Clear();
            var coin = Selected;
            if (coin == null)
            {
                if (Hub.Market.List(null).Any()) _hero.Add(Duo.Footnote(MoneyStrings.T("perps.pickHint"), "duo-perp-pick"));
                return;
            }
            var asset = El.Div("duo-perp-asset", El.Div("duo-perp-coin"),
                El.Text(_cap.HasValue ? MoneyStrings.F("perps.asset", coin.Ticker, MaxLeverage) : MoneyStrings.F("perps.assetNoCap", coin.Ticker), "duo-perp-asset-name"));
            _hero.Add(asset);
            _hero.Add(Duo.Hero("", MoneyFormat.Micros(coin.PriceMicros), MoneyFormat.Bps(coin.Change5mBps), coin.Change5mBps >= 0, MoneyStrings.T("perps.change5m")));
            // Real 1-minute candles for this coin; nothing drawn without them.
            var candles = _detailCoin == coin.CoinId ? _detail?.Candles : null;
            if (candles != null && candles.Count >= 2)
            {
                var frames = DuoTimeframes.Available(candles[0].OpenMs, candles[candles.Count - 1].OpenMs, candles.Count);
                if (!frames.Contains(_timeframe)) _timeframe = "All";
                long from = _timeframe == "All" ? long.MinValue : candles[candles.Count - 1].OpenMs - DuoTimeframes.WindowMs(_timeframe);
                var chart = new DuoLineChart();
                chart.name = "perps-chart";
                chart.SetData(candles.Where(c => c.OpenMs >= from).Select(c => (double)c.Close));
                _hero.Add(chart);
                if (frames.Count > 0)
                {
                    var pills = El.Div("duo-perp-timeframes");
                    foreach (var f in frames)
                    {
                        var frame = f;
                        var b = El.Btn(f, () => { _timeframe = frame; RenderHero(); }, "duo-timeframe" + (f == _timeframe ? " duo-timeframe-on" : ""));
                        b.name = "perps-timeframe-" + f;
                        pills.Add(b);
                    }
                    _hero.Add(pills);
                }
            }
            else if (_detailCoin == coin.CoinId && _detail != null) _hero.Add(Duo.Footnote(MoneyStrings.T("perps.noHistory")));
        }

        private void RenderPosition()
        {
            if (_position == null) return;
            _position.Clear();
            var coin = Selected;
            if (coin == null || _open == null) return;
            var mine = _open.Items.Where(p => p.Status == "open" && p.CoinId == coin.CoinId).ToList();
            _position.Add(Duo.SectionHeader(MoneyStrings.T("perps.position")));
            if (mine.Count == 0) { _position.Add(Duo.Footnote(MoneyStrings.F("perps.noPosition", coin.Ticker), "duo-perp-none")); return; }
            foreach (var p in mine)
            {
                // Server values only; size is margin × leverage (both from the server row).
                var grid = El.Div("duo-kv-grid",
                    Kv(MoneyStrings.T("perps.side"), MoneyStrings.T(p.Side == "short" ? "perps.short" : "perps.long") + " " + p.Leverage + "×", p.Side == "short" ? "duo-value-negative" : "duo-value-positive"),
                    Kv(MoneyStrings.T("perps.size"), MoneyFormat.Cash(p.MarginCents * Math.Max(1, p.Leverage))),
                    Kv(MoneyStrings.T("label.entry"), MoneyFormat.Micros(p.EntryMicros)),
                    Kv(MoneyStrings.T("label.mark"), MoneyFormat.Micros(coin.PriceMicros)),
                    Kv(MoneyStrings.T("label.margin"), MoneyFormat.Cash(p.MarginCents)),
                    Kv(MoneyStrings.T("label.liq"), MoneyFormat.Micros(p.LiqMicros), "duo-value-negative"));
                var perp = p;
                var close = Duo.Pill(MoneyStrings.T("perps.close"), () => ConfirmClose(perp), Duo.PillKind.Destructive);
                close.name = "perps-close-" + p.PerpId;
                close.SetEnabled(MarketsOn && !_closing.Contains(p.PerpId));
                _position.Add(grid);
                _position.Add(El.Div("duo-perp-position-actions", _closing.Contains(p.PerpId) ? Duo.Footnote(MoneyStrings.T("perps.closing")) : (VisualElement)close));
            }
            foreach (var kv in _closeResult) _position.Add(Duo.Notice(kv.Value));
        }

        private static VisualElement Kv(string key, string value, string valueClass = null) =>
            El.Div("duo-kv", El.Text(key, "duo-kv-key"), El.Text(value, "duo-kv-value" + (valueClass != null ? " " + valueClass : "")));

        private void RenderCoinList()
        {
            if (_coinList == null) return;
            _coinList.Clear();
            var coins = Hub.Market.List(null).OrderByDescending(c => c.McapCents).Take(16).ToList();
            if (coins.Count == 0)
                _coinList.Add(Duo.Footnote(Hub.Market.LastError != null ? MoneyStrings.T(Hub.Market.LastError) :
                    MoneyStrings.T(double.IsNaN(Hub.Market.Age(null)) ? "common.loading" : "perps.coinsEmpty")));
            if (_coinId != null && coins.All(c => c.CoinId != _coinId)) { _coinId = null; RenderHero(); RenderPosition(); }
            foreach (var c in coins)
            {
                var coin = c;
                var row = new DuoRow(() => SelectCoin(coin.CoinId))
                    .Leading(Duo.Avatar(c.Ticker, c.CoinId, "duo-avatar-s"))
                    .Title("$" + c.Ticker, "duo-row-title-strong").Subtitle(c.Name);
                row.name = "perps-coin-" + c.CoinId;
                row.EnableInClassList("duo-row-selected", c.CoinId == _coinId);
                row.Trailing(El.Div("duo-perp-quote", El.Text(MoneyFormat.Micros(c.PriceMicros), "duo-row-value duo-perp-price"),
                    El.Text(MoneyFormat.Bps(c.Change5mBps), "duo-perp-change " + (c.Change5mBps >= 0 ? "duo-value-positive" : "duo-value-negative"))));
                _coinList.Add(row);
            }
            Duo.MarkLast(_coinList);
            SyncBar();
        }

        private void SelectCoin(string coinId)
        {
            _coinId = coinId;
            _timeframe = "All";
            RenderHero();
            RenderPosition();
            RenderCoinList();
            _detailGate.Kick();
            Root.scrollOffset = UnityEngine.Vector2.zero;
        }

        private void OpenOrder(string side)
        {
            if (Selected == null) return;
            _side = side;
            _orderSheet?.Close();
            _tradeControls = El.Div("duo-perp-order");
            _tradeStatus = El.Div("ct-col");
            var scroll = El.VerticalScroll("duo-sheet-scroll");
            scroll.Add(_tradeControls);
            scroll.Add(_tradeStatus);
            _orderSheet = new DuoSheet(MoneyStrings.T("perps.newPosition"));
            _orderSheet.name = "perps-order-sheet";
            _orderSheet.AddContent(scroll);
            var sheet = _orderSheet;
            _orderSheet.Closed += () => { if (_orderSheet == sheet) { _orderSheet = null; _openButton = null; _tradeControls = _tradeStatus = null; } };
            RenderTradeControls();
            _orderSheet.Show(_page);
        }

        private void RenderTradeControls()
        {
            if (_tradeControls == null) return;
            _tradeControls.Clear();
            var coin = Selected;
            var sides = new DuoSegmented(new[] { MoneyStrings.T("perps.long"), MoneyStrings.T("perps.short") }, _side == "short" ? 1 : 0,
                i => { _side = i == 1 ? "short" : "long"; RenderTradeControls(); });
            sides.name = "perps-side";
            _tradeControls.Add(El.Div("duo-perp-side", sides));
            if (coin != null) _tradeControls.Add(Duo.Footnote("$" + coin.Ticker + " · " + MoneyFormat.Micros(coin.PriceMicros), "duo-perp-order-coin"));

            int max = MaxLeverage;
            var minus = Duo.Pill("−", () => { _leverage = Math.Max(1, _leverage - 1); RenderTradeControls(); });
            var plus = Duo.Pill("+", () => { _leverage = Math.Min(max, _leverage + 1); RenderTradeControls(); });
            minus.name = "perps-lev-down"; plus.name = "perps-lev-up";
            minus.AddToClassList("duo-stepper-button"); plus.AddToClassList("duo-stepper-button");
            minus.SetEnabled(_leverage > 1);
            plus.SetEnabled(_leverage < max);
            var lev = new DuoRow().Title(MoneyStrings.T("label.leverage")).Trailing(El.Div("duo-stepper", minus, El.Text(_leverage + "×", "duo-stepper-value"), plus));
            var levList = El.Div("duo-list", lev);
            Duo.MarkLast(levList);
            _tradeControls.Add(levList);
            _tradeControls.Add(Duo.Footnote(_cap.HasValue ? MoneyStrings.F("perps.cap", _cap.Value) : MoneyStrings.T("perps.capUnknown"), "duo-topup-note"));

            var field = new TextField { value = _marginInput ?? "", isDelayed = false, name = "perps-margin" };
            field.AddToClassList("duo-field");
            field.textEdition.placeholder = MoneyStrings.T("perps.margin");
            field.RegisterValueChangedCallback(e => _marginInput = e.newValue);
            _tradeControls.Add(Duo.GroupHeader(MoneyStrings.T("perps.margin")));
            _tradeControls.Add(field);
            var quick = El.Div("duo-topup-currencies");
            foreach (var dollars in new[] { "10", "50", "100", "500" })
            {
                var d = dollars;
                quick.Add(Duo.Pill("$" + d, () => { _marginInput = d; field.SetValueWithoutNotify(d); }));
            }
            _tradeControls.Add(quick);
            _tradeControls.Add(Duo.Footnote(MoneyStrings.F("perps.available", MoneyFormat.Cash(Cash)), "duo-topup-note"));
            var open = _openButton = El.Btn(MoneyStrings.T(_opening ? "perps.opening" : "perps.open"), OpenPerp,
                "duo-sheet-primary " + (_side == "short" ? "duo-perp-primary-short" : "duo-perp-primary-long"));
            open.name = "perps-open";
            _tradeControls.Add(open);
            SyncBar();
        }

        private void OpenPerp()
        {
            if (_opening || !MarketsOn) return;
            var ticket = Tickets.OpenPerp(_coinId, _side, _leverage, _cap, _marginInput, Cash);
            _tradeStatus?.Clear();
            if (!ticket.Ok)
            {
                _tradeStatus?.Add(Duo.Notice(MoneyStrings.F(ticket.ErrorKey, ticket.Args), Duo.Tone.Danger));
                return;
            }
            var body = ticket.Body;
            var key = _openWrite.KeyFor("/perps", RetainedWrite.Body(body));
            _opening = true;
            RenderTradeControls();
            Run(ct => Hub.Api.OpenPerp(body, key, ct), perp =>
            {
                _openWrite.Complete();
                _opening = false;
                Hub.Market.Remember(perp.CoinId, perp.Ticker);
                Bus.Publish(new SfxSignal("sfx_buy"));
                _tradeStatus?.Clear();
                _tradeStatus?.Add(Duo.Notice(MoneyStrings.T("perps.opened") + " · $" + TickerOf(perp) + " · " +
                    MoneyStrings.T(perp.Side == "short" ? "perps.short" : "perps.long") + " " + perp.Leverage + "×", Duo.Tone.Info));
                _tradeStatus?.Add(El.Div("duo-kv-grid",
                    Kv(MoneyStrings.T("label.margin"), MoneyFormat.Cash(perp.MarginCents)),
                    Kv(MoneyStrings.T("label.entry"), MoneyFormat.Micros(perp.EntryMicros)),
                    Kv(MoneyStrings.T("label.liq"), MoneyFormat.Micros(perp.LiqMicros), "duo-value-negative")));
                Hub.RefreshProfile();
                _openGate.Kick();
                RenderTradeControls();
            }, err =>
            {
                _opening = false;
                if (!err.OutcomeUnknown) _openWrite.Complete();
                _tradeStatus?.Clear();
                _tradeStatus?.Add(Duo.Notice(MoneyStrings.Err(err), Duo.Tone.Danger));
                if (err.OutcomeUnknown)
                    _tradeStatus?.Add(El.Div("duo-topup-actions", Duo.Pill(MoneyStrings.T("common.retry"), OpenPerp, Duo.PillKind.Primary),
                        Duo.Pill(MoneyStrings.T("perps.tab.open"), () => { _openWrite.Complete(); _orderSheet?.Close(); ShowTab("open"); })));
                Bus.Publish(new SfxSignal("sfx_ui_error"));
                RenderTradeControls();
            }, write: true);
        }

        private void ShowTab(string tab)
        {
            int i = Array.IndexOf(Tabs, tab);
            if (i < 0) return;
            _tab = tab;
            _tabs.Select(i, false);
            Render();
        }

        // ------------------------------------------------------------------ open positions

        private void RenderOpen()
        {
            _body.Add(Duo.Footnote(MoneyUi.Age(_openGate.Age(Now))));
            if (_openError != null) _body.Add(Duo.Notice(MoneyStrings.Err(_openError), Duo.Tone.Danger));
            if (_open == null)
            {
                if (_openError == null) _body.Add(Duo.Footnote(MoneyStrings.T("common.loading")));
                return;
            }
            if (_open.Skipped > 0) _body.Add(Duo.Footnote(MoneyStrings.F("common.skipped", _open.Skipped)));
            var rows = _open.Items.Where(p => p.Status == "open").ToList();
            if (rows.Count == 0) _body.Add(Duo.Empty(MoneyStrings.T("perps.openEmpty")));
            var list = El.Div("duo-list");
            foreach (var p in rows) list.Add(PerpRowView(p, closable: true));
            Duo.MarkLast(list);
            _body.Add(list);
            foreach (var kv in _closeResult) _body.Add(Duo.Notice(kv.Value));
        }

        private DuoRow PerpRowView(PerpRow p, bool closable)
        {
            var perp = p;
            var mark = Hub.Market.Find(p.CoinId);
            string sub = MoneyStrings.F("perps.marginLine", MoneyFormat.Cash(p.MarginCents)) + " · " + MoneyStrings.F("perps.entry", MoneyFormat.Micros(p.EntryMicros))
                + " · " + MoneyStrings.F("perps.liq", MoneyFormat.Micros(p.LiqMicros));
            var row = new DuoRow(closable ? () => ConfirmClose(perp) : (Action)null)
                .Leading(Duo.Avatar(TickerOf(p), p.CoinId, "duo-avatar-s"))
                .Title(MoneyStrings.F("perps.positionTitle", TickerOf(p), MoneyStrings.T(p.Side == "short" ? "perps.short" : "perps.long"), p.Leverage), "duo-row-title-strong")
                .Subtitle(sub, twoLines: true);
            row.name = "perps-row-" + p.PerpId;
            if (p.Status == "liquidated") row.Value(MoneyStrings.T("perps.liquidated"), "duo-value-negative");
            else if (p.RealizedCents.HasValue) row.Value(MoneyFormat.SignedCash(p.RealizedCents.Value), p.RealizedCents.Value >= 0 ? "duo-value-positive" : "duo-value-negative");
            else if (mark != null && p.Status == "open") row.Value(MoneyStrings.F("perps.mark", MoneyFormat.Micros(mark.PriceMicros)));
            if (closable)
            {
                if (_closing.Contains(p.PerpId)) row.Value(MoneyStrings.T("perps.closing"));
                else row.Chevron();
                row.SetEnabled(MarketsOn || _closing.Contains(p.PerpId));
            }
            return row;
        }

        /// <summary>Close is confirmed in a sheet (was an inline "Close at market?" row).</summary>
        private void ConfirmClose(PerpRow p)
        {
            if (_closing.Contains(p.PerpId) || !MarketsOn) return;
            var sheet = new DuoSheet(MoneyStrings.T("perps.closeConfirm"),
                MoneyStrings.F("perps.positionTitle", TickerOf(p), MoneyStrings.T(p.Side == "short" ? "perps.short" : "perps.long"), p.Leverage)
                + " · " + MoneyStrings.F("perps.marginLine", MoneyFormat.Cash(p.MarginCents)));
            sheet.name = "perps-close-sheet";
            sheet.AddPrimary(MoneyStrings.T("perps.close"), () => ClosePerp(p), destructive: true, name: "perps-close-confirm");
            sheet.AddSecondary(MoneyStrings.T("common.cancel"));
            sheet.Show(_page);
        }

        private void ClosePerp(PerpRow p)
        {
            if (_closing.Contains(p.PerpId) || !MarketsOn) return;
            if (!_closeWrites.TryGetValue(p.PerpId, out var write)) _closeWrites[p.PerpId] = write = new RetainedWrite();
            var key = write.KeyFor("/perps/" + p.PerpId + "/close", "{}");
            _closing.Add(p.PerpId);
            Render();
            var id = p.PerpId;
            Run(ct => Hub.Api.ClosePerp(id, key, ct), closed =>
            {
                write.Complete();
                _closing.Remove(id);
                _closeResult[id] = "$" + TickerOf(closed) + ": " + MoneyStrings.F("perps.closed", closed.RealizedCents.HasValue ? MoneyFormat.SignedCash(closed.RealizedCents.Value) : "—");
                Bus.Publish(new SfxSignal("sfx_sell"));
                Hub.RefreshProfile();
                _openGate.Kick();
                _historyGate.Kick();
                Render();
            }, err =>
            {
                if (!err.OutcomeUnknown) write.Complete();
                _closing.Remove(id);
                _closeResult[id] = MoneyStrings.Err(err);
                _openGate.Kick();
                Render();
            }, write: true);
        }

        // ------------------------------------------------------------------ history and Rekt Clinic

        private void RenderHistory()
        {
            _body.Add(Duo.Footnote(MoneyUi.Age(_historyGate.Age(Now))));
            if (_historyError != null) _body.Add(Duo.Notice(MoneyStrings.Err(_historyError), Duo.Tone.Danger));
            if (_history == null)
            {
                if (_historyError == null) _body.Add(Duo.Footnote(MoneyStrings.T("common.loading")));
                return;
            }
            var rows = _history.Items.Where(p => p.Status != "open").OrderByDescending(p => p.ClosedAtMs ?? 0).ToList();
            if (_history.Skipped > 0) _body.Add(Duo.Footnote(MoneyStrings.F("common.skipped", _history.Skipped)));
            if (rows.Count == 0) _body.Add(Duo.Empty(MoneyStrings.T("perps.historyEmpty")));
            var list = El.Div("duo-list");
            foreach (var p in rows) list.Add(PerpRowView(p, closable: false));
            Duo.MarkLast(list);
            _body.Add(list);
        }

        private void RenderRekt()
        {
            var refresh = Duo.Pill(MoneyStrings.T("common.refresh"), () => { _rektGate.Kick(); });
            refresh.name = "perps-rekt-refresh";
            _body.Add(El.Div("duo-perp-rekt-head", El.Text(MoneyStrings.T("perps.rektIntro"), "duo-perp-rekt-intro"), refresh));
            _body.Add(Duo.Footnote(MoneyUi.Age(_rektGate.Age(Now))));
            if (_rektError != null) _body.Add(Duo.Notice(MoneyStrings.Err(_rektError), Duo.Tone.Danger));
            if (_tombstones == null)
            {
                if (_rektError == null) _body.Add(Duo.Footnote(MoneyStrings.T("common.loading")));
                return;
            }
            if (_tombstones.Skipped > 0) _body.Add(Duo.Footnote(MoneyStrings.F("common.skipped", _tombstones.Skipped)));
            if (_tombstones.Items.Count == 0) _body.Add(Duo.Empty(MoneyStrings.T("perps.rektEmpty")));
            var list = El.Div("duo-list");
            foreach (var t in _tombstones.Items)
            {
                var ticker = !string.IsNullOrEmpty(t.Ticker) ? t.Ticker : Hub.Market.Ticker(t.CoinId) ?? "?";
                var when = t.AtMs.HasValue ? MoneyFormat.Ago((ServerClock.NowMs - t.AtMs.Value) / 1000.0) : "";
                list.Add(new DuoRow().Leading(Duo.Avatar(t.Alias, t.Alias, "duo-avatar-s"))
                    .Title(MoneyStrings.F("perps.rektRow", t.Alias, MoneyFormat.Cash(t.LossCents), "$" + ticker))
                    .Subtitle(when + (string.IsNullOrEmpty(t.Reason) ? "" : " · " + t.Reason))
                    .Value("-" + MoneyFormat.Cash(t.LossCents), "duo-value-negative"));
            }
            Duo.MarkLast(list);
            _body.Add(list);
        }

        // ------------------------------------------------------------------ fetches

        private void FetchDetail(string coinId)
        {
            _detailGate.Start();
            Run(ct => Hub.Api.Coin(coinId, ct), detail =>
            {
                _detailGate.Succeeded(Now);
                _detail = detail;
                _detailCoin = coinId;
                if (_tab == "trade" && coinId == _coinId) RenderHero();
            }, err =>
            {
                _detailGate.Failed(Now); // the price line stays; no chart without data
            }, write: false);
        }

        private void FetchOpen()
        {
            _openGate.Start();
            Run(ct => Hub.Api.Perps("open", ct), rows =>
            {
                _openGate.Succeeded(Now);
                _open = rows;
                _openError = null;
                foreach (var p in rows.Items) Hub.Market.Remember(p.CoinId, p.Ticker);
                if (_tab == "open") Render();
                else if (_tab == "trade") RenderPosition();
            }, err =>
            {
                _openGate.Failed(Now);
                _openError = err;
                if (_tab == "open") Render();
            }, write: false);
        }

        private void FetchHistory()
        {
            _historyGate.Start();
            // Unfiltered: the contract's `status` filter is open|closed, and liquidated perps carry status `liquidated`.
            Run(ct => Hub.Api.Perps(null, ct), rows =>
            {
                _historyGate.Succeeded(Now);
                _history = rows;
                _historyError = null;
                if (_tab == "history") Render();
            }, err =>
            {
                _historyGate.Failed(Now);
                _historyError = err;
                if (_tab == "history") Render();
            }, write: false);
        }

        private void FetchRekt()
        {
            _rektGate.Start();
            Run(ct => Hub.Api.Tombstones(50, ct), rows =>
            {
                _rektGate.Succeeded(Now);
                _tombstones = rows;
                _rektError = null;
                if (_tab == "rekt") Render();
            }, err =>
            {
                _rektGate.Failed(Now);
                _rektError = err;
                if (_tab == "rekt") Render();
            }, write: false);
        }

        private string TickerOf(PerpRow p) => !string.IsNullOrEmpty(p.Ticker) ? p.Ticker : Hub.Market.Ticker(p.CoinId) ?? MoneyFormat.Short(p.CoinId, 4, 0).TrimEnd('…');
    }
}
