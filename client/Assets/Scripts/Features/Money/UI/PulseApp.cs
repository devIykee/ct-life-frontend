using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using FeatureFlags = CTLife.Core.Features;
using CTLife.Net;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// Pulse, the Terminal Café trading terminal (feature #14) with the Launchpad Tower board (#15), Alpha Calls and
    /// Trust Score (#20) and Smart Money (#21). Fictional Cash only. Every price, fill and PnL is the server's.
    /// </summary>
    public sealed class PulseApp : MoneyApp
    {
        public override string Id => "pulse";
        public override string Title => MoneyStrings.T("pulse.title");
        public override string Color => "#20bf6b";
        public override int Phase => 2;
        public override string Feature => FeatureFlags.Markets;

        /// <summary>Optional deep link (a venue terminal or the Smart Money "copy"): open this coin's page next time.</summary>
        public string PendingCoinId { get; set; }

        internal override MoneyView CreateView(MoneyHub hub)
        {
            var v = new PulseView(hub, PendingCoinId);
            PendingCoinId = null;
            return v;
        }
    }

    internal sealed class PulseView : MoneyView
    {
        private const string TabMarket = "market", TabLaunch = "launch", TabPositions = "positions", TabTape = "tape", TabSmart = "smart", TabAlpha = "alpha";
        private static readonly string[] ListStages = { "new", "final", "graduated" };

        private readonly ChipTabs _tabs;
        private readonly VisualElement _body = El.Div("ct-col");
        private readonly Label _cash = MoneyUi.Small("");
        private readonly JToken _marketConfig;
        private string _stage = "new";
        private bool _listDirty = true;
        private double _lastListRender;
        private Label _age;

        // Coin page
        private string _coinId;
        private CoinDetail _detail;
        private MoneyError _detailError;
        private readonly PollGate _detailGate = new PollGate(5, 30);
        private VisualElement _detailMarket, _orderStatus, _orderReceipt, _sellInfo, _callStatus;
        private string _side = "buy", _cashInput = "", _sellUnits, _targetInput = "", _stopInput = "";
        private int _slippageBps = Tickets.DefaultSlippageBps, _sellPct;
        private bool _placing, _posting;
        private readonly RetainedWrite _tradeWrite = new RetainedWrite();
        private readonly RetainedWrite _callWrite = new RetainedWrite();
        private Button _placeButton;
        private VisualElement _sideRow, _amountBox, _slipRow;

        // Lists
        private Rows<PositionRow> _positions;
        private MoneyError _positionsError;
        private readonly PollGate _positionsGate = new PollGate(5, 30);
        private string _openCard;
        private Rows<SmartMoneyRow> _smart;
        private MoneyError _smartError;
        private readonly PollGate _smartGate = new PollGate(15, 60);
        private AlphaCallsResult _calls;
        private MoneyError _callsError;
        private readonly PollGate _callsGate = new PollGate(30, 60);
        private string _callsUser, _callsAlias;

        public PulseView(MoneyHub hub, string coinId) : base(hub)
        {
            _marketConfig = Services.TryGet<ConfigStore>(out var cfg) ? cfg.Config("market") : null;
            _tabs = new ChipTabs(Select)
                .Add(TabMarket, MoneyStrings.T("pulse.tab.market"))
                .Add(TabLaunch, MoneyStrings.T("pulse.tab.launch"))
                .Add(TabPositions, MoneyStrings.T("pulse.tab.positions"))
                .Add(TabTape, MoneyStrings.T("pulse.tab.tape"))
                .Add(TabSmart, MoneyStrings.T("pulse.tab.smart"))
                .Add(TabAlpha, MoneyStrings.T("pulse.tab.alpha"));
            Root.Add(MoneyUi.Row(MoneyUi.Text(MoneyStrings.T("pulse.title"), "ct-h1"), El.Div("ct-grow"), _cash));
            Root.Add(_tabs.Root);
            Root.Add(_body);
            Hub.Market.Changed += MarketChanged;
            Hub.Market.TapeAdded += TapeAdded;
            Subs.On<ProfileChangedSignal>(_ => RenderCash());
            Subs.On<DegradedChangedSignal>(_ => Render());
            RenderCash();
            _tabs.Select(TabMarket, notify: false);
            if (!string.IsNullOrEmpty(coinId)) OpenCoin(coinId, null);
            else Render();
        }

        public override void Dispose()
        {
            Hub.Market.Changed -= MarketChanged;
            Hub.Market.TapeAdded -= TapeAdded;
            base.Dispose();
        }

        private static long Cash => Services.TryGet<PlayerProfile>(out var p) ? p.CashCents : 0;
        private static bool MarketsOn => FeatureFlags.IsAvailable(FeatureFlags.Markets);

        private void RenderCash() => _cash.text = MoneyStrings.F("common.cash", MoneyFormat.Cash(Cash));

        private void MarketChanged()
        {
            _listDirty = true;
            // Live PnL: when the market room ticks (2 s), refetch the server's unrealized PnL for the open positions view.
            if (Hub.Market.RoomLive && (_coinId != null || _tabs.Current == TabPositions) && _positionsGate.Age(Now) >= 2) _positionsGate.Kick();
        }

        private void TapeAdded(TapeRow row)
        {
            if (_coinId == null && _tabs.Current == TabTape) Render();
        }

        private void Select(string tab)
        {
            _coinId = null;
            _detail = null;
            if (tab == TabSmart) _smartGate.Kick();
            if (tab == TabAlpha) _callsGate.Kick();
            if (tab == TabPositions) _positionsGate.Kick();
            Render();
        }

        protected override void Tick()
        {
            var tab = _tabs.Current;
            IEnumerable<string> stages = _coinId != null ? Array.Empty<string>() :
                tab == TabMarket ? new[] { _stage } : tab == TabLaunch ? new[] { "new", "final" } : Array.Empty<string>();
            Hub.Market.Pump(stages, Life);
            double now = Now;
            if (_coinId != null)
            {
                if (_detailGate.Due(now)) FetchDetail();
                if (_positionsGate.Due(now)) FetchPositions();
                return;
            }
            switch (tab)
            {
                case TabMarket:
                case TabLaunch:
                    if (_listDirty && now - _lastListRender >= 1) Render();
                    if (_age != null) _age.text = AgeText();
                    break;
                case TabPositions:
                    if (_positionsGate.Due(now)) FetchPositions();
                    break;
                case TabSmart:
                    if (_smartGate.Due(now) && FeatureFlags.IsAvailable(FeatureFlags.SmartMoney)) FetchSmart();
                    break;
                case TabAlpha:
                    if (_callsGate.Due(now)) FetchCalls();
                    break;
            }
        }

        private string AgeText()
        {
            var stage = _tabs.Current == TabLaunch ? "new" : _stage;
            if (Hub.Market.RoomLive) return MoneyStrings.T("pulse.live");
            var age = Hub.Market.Age(stage);
            return double.IsNaN(age) ? MoneyStrings.T("common.loading") : MoneyStrings.F("pulse.polled", MoneyFormat.Ago(age));
        }

        // ------------------------------------------------------------------ rendering

        private void Render()
        {
            if (!Alive) return;
            _body.Clear();
            _age = null;
            if (!MarketsOn) _body.Add(MoneyUi.Notice(MoneyStrings.T("common.marketsOff"), "warn"));
            if (_coinId != null)
            {
                RenderCoin();
                return;
            }
            switch (_tabs.Current)
            {
                case TabMarket: RenderMarket(); break;
                case TabLaunch: RenderLaunch(); break;
                case TabPositions: RenderPositions(); break;
                case TabTape: RenderTape(); break;
                case TabSmart: RenderSmart(); break;
                case TabAlpha: RenderCalls(); break;
            }
        }

        private void AddListHeader()
        {
            _age = MoneyUi.Small(AgeText());
            _body.Add(_age);
            if (Hub.Market.LastError != null && !Hub.Market.RoomLive) _body.Add(MoneyUi.Notice(MoneyStrings.T(Hub.Market.LastError), "error"));
        }

        private void RenderMarket()
        {
            _listDirty = false;
            _lastListRender = Now;
            var stages = El.Div("ct-app-tabs ct-money-tabs");
            foreach (var s in ListStages)
            {
                var stage = s;
                stages.Add(MoneyUi.Chip(MoneyStrings.T("pulse.stage." + stage), () => { _stage = stage; Hub.Market.Kick(); Render(); }, _stage == stage));
            }
            _body.Add(stages);
            AddListHeader();
            var coins = Hub.Market.List(_stage);
            if (coins.Count == 0) _body.Add(MoneyUi.Text(MoneyStrings.T("pulse.empty"), "ct-body ct-muted"));
            foreach (var c in coins) _body.Add(CoinRow(c, null));
        }

        private VisualElement CoinRow(MarketCoin c, VisualElement extra)
        {
            var coin = c;
            var change = MoneyUi.Text(MoneyFormat.Bps(c.Change5mBps), "ct-money-value " + MoneyUi.PnlClass(c.Change5mBps));
            var b = El.Btn("", () => OpenCoin(coin.CoinId, null), "ct-row-btn ct-money-coin");
            b.Add(El.Div("ct-col ct-grow",
                MoneyUi.Text("$" + c.Ticker + "  " + c.Name, "ct-money-title"),
                MoneyUi.Small(MoneyStrings.F("pulse.mcap", MoneyFormat.Cash(c.McapCents, true)) + " · " + MoneyStrings.F("pulse.holders", c.Holders)),
                extra));
            b.Add(El.Div("ct-col ct-money-right", MoneyUi.Text(MoneyFormat.Micros(c.PriceMicros), "ct-money-value"), change));
            return b;
        }

        private void RenderLaunch()
        {
            _listDirty = false;
            _lastListRender = Now;
            long graduate = Wire.LongOr(_marketConfig?["curve"]?["graduateMcapCents"], 0);
            long interval = Wire.LongOr(_marketConfig?["launchIntervalSec"], 0);
            if (graduate > 0 && interval > 0)
                _body.Add(MoneyUi.Small(MoneyStrings.F("pulse.launchIntro", Math.Max(1, interval / 60), MoneyFormat.Cash(graduate, true))));
            AddListHeader();
            var coins = Hub.Market.List("new").Concat(Hub.Market.List("final")).OrderByDescending(c => c.LaunchedAtMs ?? 0).ToList();
            if (coins.Count == 0) _body.Add(MoneyUi.Text(MoneyStrings.T("pulse.empty"), "ct-body ct-muted"));
            foreach (var c in coins)
            {
                VisualElement extra = null;
                if (graduate > 0)
                {
                    // Display ratio of two server values (mcap vs the config threshold); not a money outcome.
                    float pct = Math.Max(0f, Math.Min(1f, (float)((double)c.McapCents / graduate)));
                    var fill = El.Div("ct-money-bar-fill");
                    fill.style.width = new Length(pct * 100f, LengthUnit.Percent);
                    var launched = c.LaunchedAtMs.HasValue ? MoneyStrings.F("pulse.launched", MoneyFormat.Ago((ServerClock.NowMs - c.LaunchedAtMs.Value) / 1000.0)) + " · " : "";
                    extra = El.Div("ct-col", El.Div("ct-money-bar", fill),
                        MoneyUi.Small(launched + MoneyStrings.F("pulse.launchProgress", MoneyFormat.Cash(Math.Max(0, graduate - c.McapCents), true))));
                }
                _body.Add(CoinRow(c, extra));
            }
        }

        private void RenderPositions()
        {
            _body.Add(MoneyUi.Small(MoneyUi.Age(_positionsGate.Age(Now))));
            if (_positionsError != null) _body.Add(MoneyUi.Notice(MoneyStrings.Err(_positionsError), "error"));
            if (_positions == null)
            {
                if (_positionsError == null) _body.Add(MoneyUi.Small(MoneyStrings.T("common.loading")));
                return;
            }
            if (_positions.Skipped > 0) _body.Add(MoneyUi.Small(MoneyStrings.F("common.skipped", _positions.Skipped)));
            if (_positions.Items.Count == 0) _body.Add(MoneyUi.Text(MoneyStrings.T("pulse.posEmpty"), "ct-body ct-muted"));
            foreach (var p in _positions.Items)
            {
                var pos = p;
                var ticker = TickerOf(p.CoinId, p.Ticker);
                var row = El.Btn("", () => { _openCard = _openCard == pos.CoinId ? null : pos.CoinId; Render(); }, "ct-row-btn ct-money-coin");
                row.Add(El.Div("ct-col ct-grow",
                    MoneyUi.Text("$" + ticker, "ct-money-title"),
                    MoneyUi.Small(MoneyStrings.F("pulse.held", MoneyFormat.CoinUnits(p.Units)) + " · " + MoneyStrings.F("pulse.cost", MoneyFormat.Cash(p.CostBasisCents)))));
                row.Add(MoneyUi.Pnl(p.UnrealizedCents));
                _body.Add(row);
                if (_openCard == p.CoinId) _body.Add(PnlCard(PnlCardData.From(p, ticker), pos.CoinId));
            }
        }

        private VisualElement PnlCard(PnlCardData d, string coinId)
        {
            return MoneyUi.Card(
                MoneyUi.Text(MoneyStrings.T("pulse.pnlCard"), "ct-eyebrow"),
                MoneyUi.Text("$" + d.Ticker, "ct-h2"),
                MoneyUi.Text(MoneyFormat.SignedCash(d.UnrealizedCents), "ct-big-number " + MoneyUi.PnlClass(d.UnrealizedCents)),
                d.ReturnBps.HasValue ? MoneyUi.Text(MoneyFormat.Bps(d.ReturnBps.Value), "ct-money-value " + MoneyUi.PnlClass(d.UnrealizedCents)) : null,
                MoneyUi.Pair(MoneyStrings.T("label.cost"), MoneyFormat.Cash(d.CostBasisCents)),
                MoneyUi.Small(MoneyStrings.F("pulse.held", d.UnitsText)),
                MoneyUi.Row(
                    MoneyUi.Btn(MoneyStrings.T("pulse.copySummary"), () => MoneyUi.Copy(d.ShareText()), "ct-btn ct-btn-ghost"),
                    MoneyUi.Btn(MoneyStrings.T("pulse.sell"), () => OpenCoin(coinId, "sell"))));
        }

        private void RenderTape()
        {
            long threshold = Wire.LongOr(_marketConfig?["tapeThresholdCents"], 100000);
            if (!Hub.Market.RoomJoined) _body.Add(MoneyUi.Notice(MoneyStrings.T("pulse.tapeOffline"), "warn"));
            var rows = Hub.Market.Tape.ToList();
            if (rows.Count == 0) _body.Add(MoneyUi.Text(MoneyStrings.F("pulse.tapeEmpty", MoneyFormat.Cash(threshold)), "ct-body ct-muted"));
            foreach (var t in rows)
            {
                var row = t;
                var b = El.Btn("", () => OpenCoin(row.CoinId, null), "ct-row-btn ct-money-coin");
                b.Add(El.Div("ct-col ct-grow",
                    MoneyUi.Text(row.Name + " " + (row.Side == "sell" ? MoneyStrings.T("pulse.sell") : MoneyStrings.T("pulse.buy")).ToLowerInvariant() + " $" + TickerOf(row.CoinId, null), "ct-money-title"),
                    MoneyUi.Small(MoneyFormat.Ago(Now - row.ReceivedAt))));
                b.Add(MoneyUi.Text(MoneyFormat.Cash(row.CashCents), "ct-money-value " + (row.Side == "sell" ? "ct-money-down" : "ct-money-up")));
                _body.Add(b);
            }
        }

        private void RenderSmart()
        {
            if (!FeatureFlags.IsAvailable(FeatureFlags.SmartMoney))
            {
                _body.Add(MoneyUi.Notice(MoneyStrings.T("pulse.smartOff"), "warn"));
                return;
            }
            _body.Add(MoneyUi.Small(MoneyStrings.T("pulse.smartIntro")));
            _body.Add(MoneyUi.Small(MoneyUi.Age(_smartGate.Age(Now))));
            if (_smartError != null) _body.Add(MoneyUi.Notice(MoneyStrings.Err(_smartError), "error"));
            if (_smart == null) return;
            if (_smart.Skipped > 0) _body.Add(MoneyUi.Small(MoneyStrings.F("common.skipped", _smart.Skipped)));
            if (_smart.Items.Count == 0) _body.Add(MoneyUi.Text(MoneyStrings.T("pulse.smartEmpty"), "ct-body ct-muted"));
            foreach (var s in _smart.Items)
            {
                var row = s;
                var age = s.AtMs.HasValue ? " · " + MoneyFormat.Ago((ServerClock.NowMs - s.AtMs.Value) / 1000.0) : "";
                _body.Add(MoneyUi.Card(
                    MoneyUi.Row(MoneyUi.Text(s.Alias, "ct-money-title"), El.Div("ct-grow"),
                        MoneyUi.Text(MoneyFormat.Cash(s.CashCents), "ct-money-value " + (s.Side == "sell" ? "ct-money-down" : "ct-money-up"))),
                    MoneyUi.Small((s.Side == "sell" ? MoneyStrings.T("pulse.sell") : MoneyStrings.T("pulse.buy")) + " $" + TickerOf(s.CoinId, s.Ticker) + age),
                    MoneyUi.Row(
                        MoneyUi.Btn(MoneyStrings.T("pulse.copy"), () => OpenCoin(row.CoinId, row.Side)),
                        MoneyUi.Btn(MoneyStrings.T("pulse.trust"), () => ShowCalls(row.UserId, row.Alias), "ct-btn ct-btn-ghost"))));
            }
        }

        private void ShowCalls(string userId, string alias)
        {
            _callsUser = userId;
            _callsAlias = alias;
            _calls = null;
            _callsError = null;
            _callsGate.Kick();
            _tabs.Select(TabAlpha);
        }

        private void RenderCalls()
        {
            if (_callsUser != null)
            {
                _body.Add(MoneyUi.Text(MoneyStrings.F("pulse.callsOf", _callsAlias), "ct-h2"));
                _body.Add(MoneyUi.Btn(MoneyStrings.T("pulse.callsMine"), () => { _callsUser = null; _calls = null; _callsGate.Kick(); Render(); }, "ct-btn ct-btn-ghost"));
            }
            else _body.Add(MoneyUi.Text(MoneyStrings.T("pulse.callsMine"), "ct-h2"));
            _body.Add(MoneyUi.Small(MoneyStrings.T("pulse.callsImmutable")));
            if (_callsError != null) _body.Add(MoneyUi.Notice(MoneyStrings.Err(_callsError), "error"));
            if (_calls == null)
            {
                if (_callsError == null) _body.Add(MoneyUi.Small(MoneyStrings.T("common.loading")));
                return;
            }
            var t = _calls.Trust;
            if (t != null)
                _body.Add(MoneyUi.Card(
                    MoneyUi.Text(MoneyStrings.T("pulse.trust"), "ct-eyebrow"),
                    MoneyUi.Pair(MoneyStrings.T("label.hitRate"), MoneyFormat.Ratio(t.HitRate)),
                    MoneyUi.Pair(MoneyStrings.T("label.avgReturn"), MoneyFormat.Bps(t.AvgReturnBps)),
                    MoneyUi.Pair(MoneyStrings.T("label.drawdown"), MoneyFormat.Bps(t.MaxDrawdownBps, false)),
                    MoneyUi.Pair(MoneyStrings.T("label.heldBag"), t.HeldBagPct + "%")));
            if (_calls.Skipped > 0) _body.Add(MoneyUi.Small(MoneyStrings.F("common.skipped", _calls.Skipped)));
            if (_calls.Calls.Count == 0) _body.Add(MoneyUi.Text(MoneyStrings.T("pulse.callsEmpty"), "ct-body ct-muted"));
            foreach (var c in _calls.Calls)
            {
                var call = c;
                var when = c.AtMs.HasValue ? MoneyFormat.Ago((ServerClock.NowMs - c.AtMs.Value) / 1000.0) : "";
                var b = El.Btn("", () => OpenCoin(call.CoinId, null), "ct-row-btn ct-money-coin");
                b.Add(El.Div("ct-col ct-grow",
                    MoneyUi.Text("$" + TickerOf(c.CoinId, null) + (string.IsNullOrEmpty(c.Status) ? "" : " · " + c.Status), "ct-money-title"),
                    MoneyUi.Small(MoneyStrings.F("pulse.callLine", MoneyFormat.Micros(c.EntryMicros), MoneyFormat.Micros(c.TargetMicros), MoneyFormat.Micros(c.StopMicros))),
                    MoneyUi.Small(when)));
                _body.Add(b);
            }
        }

        // ------------------------------------------------------------------ coin page

        private void OpenCoin(string coinId, string side)
        {
            if (!Wire.IsSafeId(coinId)) return;
            _coinId = coinId;
            _detail = null;
            _detailError = null;
            _side = side == "sell" ? "sell" : "buy";
            _cashInput = "";
            _sellPct = 0;
            _sellUnits = null;
            _targetInput = "";
            _stopInput = "";
            _tradeWrite.Complete();
            _callWrite.Complete();
            _detailGate.Kick();
            _positionsGate.Kick();
            Render();
        }

        private PositionRow Held => _positions?.Items.FirstOrDefault(p => p.CoinId == _coinId);

        private void RenderCoin()
        {
            _body.Add(MoneyUi.Btn(MoneyStrings.T("common.back"), () => { _coinId = null; Render(); }, "ct-btn ct-btn-ghost"));
            _detailMarket = El.Div("ct-col");
            _body.Add(_detailMarket);
            RenderDetailMarket();
            BuildOrderPanel();
            BuildCallPanel();
        }

        private void RenderDetailMarket()
        {
            if (_detailMarket == null) return;
            _detailMarket.Clear();
            var coin = _detail?.Coin ?? Hub.Market.Find(_coinId);
            if (coin == null)
            {
                _detailMarket.Add(_detailError != null ? MoneyUi.Notice(MoneyStrings.Err(_detailError), "error") : MoneyUi.Small(MoneyStrings.T("common.loading")));
                return;
            }
            var chart = new Sparkline();
            chart.SetData(_detail?.Candles);
            _detailMarket.Add(MoneyUi.Card(
                MoneyUi.Row(MoneyUi.Text("$" + coin.Ticker, "ct-h1"), El.Div("ct-grow"), MoneyUi.Text(MoneyStrings.T("pulse.stage." + coin.Stage), "ct-badge")),
                MoneyUi.Small(coin.Name),
                MoneyUi.Row(MoneyUi.Text(MoneyFormat.Micros(coin.PriceMicros), "ct-big-number"), El.Div("ct-grow"),
                    MoneyUi.Text(MoneyFormat.Bps(coin.Change5mBps), "ct-money-value " + MoneyUi.PnlClass(coin.Change5mBps))),
                MoneyUi.Small(MoneyStrings.T("pulse.chart")),
                _detail != null && _detail.Candles.Count >= 2 ? (VisualElement)chart : MoneyUi.Small(MoneyStrings.T("pulse.noChart")),
                MoneyUi.Pair(MoneyStrings.T("label.mcap"), MoneyFormat.Cash(coin.McapCents, true)),
                MoneyUi.Pair(MoneyStrings.T("label.vol"), MoneyFormat.Cash(coin.Vol5mCents, true)),
                MoneyUi.Pair(MoneyStrings.T("label.holders"), coin.Holders.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                _detailError != null ? MoneyUi.Notice(MoneyStrings.Err(_detailError), "error") : null));
            if (_detail == null) return;
            if (!string.IsNullOrEmpty(_detail.RiskHint))
                _detailMarket.Add(MoneyUi.Card(MoneyUi.Text(MoneyStrings.T("pulse.risk"), "ct-eyebrow"), MoneyUi.Text(_detail.RiskHint)));
            var holders = MoneyUi.Card(MoneyUi.Text(MoneyStrings.T("pulse.holdersTitle"), "ct-eyebrow"));
            if (_detail.Holders.Count == 0) holders.Add(MoneyUi.Small(MoneyStrings.T("pulse.noHolders")));
            foreach (var h in _detail.Holders.Take(10))
                holders.Add(MoneyUi.Pair(h.Alias, MoneyFormat.CoinUnits(h.Units)));
            _detailMarket.Add(holders);
        }

        private void BuildOrderPanel()
        {
            var card = MoneyUi.Card();
            _sideRow = El.Div("ct-app-tabs ct-money-tabs");
            _amountBox = El.Div("ct-col");
            _slipRow = El.Div("ct-app-tabs ct-money-tabs");
            _orderStatus = El.Div("ct-col");
            _orderReceipt = El.Div("ct-col");
            _placeButton = MoneyUi.Btn(MoneyStrings.T("pulse.place"), PlaceOrder);
            card.Add(_sideRow);
            card.Add(_amountBox);
            card.Add(MoneyUi.Small(MoneyStrings.T("pulse.slippage")));
            card.Add(_slipRow);
            card.Add(_placeButton);
            card.Add(_orderStatus);
            card.Add(_orderReceipt);
            _body.Add(card);
            RenderOrderControls();
        }

        private void RenderOrderControls()
        {
            if (_sideRow == null) return;
            _sideRow.Clear();
            _sideRow.Add(MoneyUi.Chip(MoneyStrings.T("pulse.buy"), () => { _side = "buy"; RenderOrderControls(); }, _side == "buy"));
            _sideRow.Add(MoneyUi.Chip(MoneyStrings.T("pulse.sell"), () => { _side = "sell"; RenderOrderControls(); }, _side == "sell"));
            _amountBox.Clear();
            _sellInfo = null;
            if (_side == "buy")
            {
                var field = MoneyUi.Field(MoneyStrings.T("pulse.spend"), _cashInput, v => _cashInput = v);
                _amountBox.Add(field);
                var quick = El.Div("ct-app-tabs ct-money-tabs");
                foreach (var dollars in new[] { "10", "50", "100", "500" })
                {
                    var d = dollars;
                    quick.Add(MoneyUi.Chip("$" + d, () => { _cashInput = d; field.SetValueWithoutNotify(d); }, false));
                }
                _amountBox.Add(quick);
                _amountBox.Add(MoneyUi.Small(MoneyStrings.F("common.cash", MoneyFormat.Cash(Cash))));
            }
            else
            {
                var pcts = El.Div("ct-app-tabs ct-money-tabs");
                foreach (var pct in new[] { 25, 50, 100 })
                {
                    var p = pct;
                    pcts.Add(MoneyUi.Chip(MoneyStrings.F("pulse.sellPct", p), () => { _sellPct = p; RenderOrderControls(); }, _sellPct == p));
                }
                _amountBox.Add(pcts);
                _sellInfo = El.Div("ct-col");
                _amountBox.Add(_sellInfo);
                RenderSellInfo();
            }
            _slipRow.Clear();
            foreach (var bps in Tickets.SlippagePresetsBps)
            {
                var b = bps;
                _slipRow.Add(MoneyUi.Chip(MoneyFormat.Bps(b, false), () => { _slippageBps = b; RenderOrderControls(); }, _slippageBps == b));
            }
            _placeButton.SetEnabled(!_placing && MarketsOn);
            _placeButton.text = MoneyStrings.T(_placing ? "pulse.placing" : "pulse.place");
        }

        private void RenderSellInfo()
        {
            if (_sellInfo == null) return;
            _sellInfo.Clear();
            var held = Held;
            if (held == null)
            {
                _sellUnits = null;
                _sellInfo.Add(MoneyUi.Small(_positions == null ? MoneyStrings.T("common.loading") : MoneyStrings.T("pulse.notHeld")));
                return;
            }
            _sellUnits = _sellPct > 0 ? TokenUnits.Fraction(held.Units, _sellPct) : null;
            _sellInfo.Add(MoneyUi.Small(MoneyStrings.F("pulse.held", MoneyFormat.CoinUnits(held.Units))));
            if (_sellUnits != null) _sellInfo.Add(MoneyUi.Text(MoneyStrings.F("pulse.receiptUnits", MoneyFormat.CoinUnits(_sellUnits))));
        }

        private void PlaceOrder()
        {
            if (_placing || _coinId == null || !MarketsOn) return;
            long minTrade = Wire.LongOr(_marketConfig?["minTradeCents"], 0);
            var ticket = _side == "buy"
                ? Tickets.Buy(_coinId, _cashInput, Cash, minTrade, _slippageBps)
                : Tickets.Sell(_coinId, _sellUnits, Held?.Units, _slippageBps);
            _orderStatus.Clear();
            if (!ticket.Ok)
            {
                _orderStatus.Add(MoneyUi.Notice(MoneyStrings.F(ticket.ErrorKey, ticket.Args), "error"));
                return;
            }
            var body = ticket.Body;
            var key = _tradeWrite.KeyFor("/market/trades", RetainedWrite.Body(body));
            var side = _side;
            _placing = true;
            RenderOrderControls();
            Run(ct => Hub.Api.Trade(body, key, ct), receipt =>
            {
                _tradeWrite.Complete();
                _placing = false;
                Bus.Publish(new SfxSignal(side == "sell" ? "sfx_sell" : "sfx_buy"));
                _orderReceipt.Clear();
                _orderReceipt.Add(MoneyUi.Card(
                    MoneyUi.Text(MoneyStrings.T("pulse.receipt"), "ct-h2 ct-money-up"),
                    MoneyUi.Small(MoneyStrings.F("pulse.receiptUnits", MoneyFormat.CoinUnits(receipt.Units))),
                    MoneyUi.Small(MoneyStrings.F("pulse.receiptPrice", MoneyFormat.Micros(receipt.PriceMicros))),
                    MoneyUi.Small(MoneyStrings.F("pulse.receiptCash", MoneyFormat.Cash(receipt.CashCents))),
                    receipt.FeeCents.HasValue ? MoneyUi.Small(MoneyStrings.F("pulse.receiptFee", MoneyFormat.Cash(receipt.FeeCents.Value))) : null,
                    receipt.RealizedCents.HasValue && side == "sell" ? MoneyUi.Small(MoneyStrings.F("pulse.receiptRealized", MoneyFormat.SignedCash(receipt.RealizedCents.Value))) : null));
                _sellPct = 0;
                Hub.RefreshProfile();
                Hub.Market.Kick();
                _positionsGate.Kick();
                _detailGate.Kick();
                RenderOrderControls();
            }, err =>
            {
                _placing = false;
                if (!err.OutcomeUnknown) _tradeWrite.Complete();
                _orderStatus.Clear();
                _orderStatus.Add(MoneyUi.Notice(MoneyStrings.Err(err), "error"));
                if (err.OutcomeUnknown)
                    _orderStatus.Add(MoneyUi.Row(
                        MoneyUi.Btn(MoneyStrings.T("common.retry"), PlaceOrder),
                        MoneyUi.Btn(MoneyStrings.T("pulse.checkPositions"), () => { _tradeWrite.Complete(); _tabs.Select(TabPositions); }, "ct-btn ct-btn-ghost")));
                Bus.Publish(new SfxSignal("sfx_ui_error"));
                RenderOrderControls();
            }, write: true);
        }

        private void BuildCallPanel()
        {
            _callStatus = El.Div("ct-col");
            var card = MoneyUi.Card(
                MoneyUi.Text(MoneyStrings.T("pulse.postCall"), "ct-h2"),
                MoneyUi.Small(MoneyStrings.T("pulse.callsImmutable")),
                MoneyUi.Field(MoneyStrings.T("pulse.callTarget"), _targetInput, v => _targetInput = v),
                MoneyUi.Field(MoneyStrings.T("pulse.callStop"), _stopInput, v => _stopInput = v),
                MoneyUi.Btn(MoneyStrings.T("pulse.postCall"), PostCall, "ct-btn ct-btn-ghost"),
                _callStatus);
            _body.Add(card);
            RenderCallEntry();
        }

        private void RenderCallEntry()
        {
            if (_callStatus == null || _posting) return;
            _callStatus.Clear();
            var price = _detail?.Coin?.PriceMicros;
            _callStatus.Add(MoneyUi.Small(price.HasValue ? MoneyStrings.F("pulse.callEntry", MoneyFormat.Micros(price.Value)) : MoneyStrings.T("common.loading")));
        }

        private void PostCall()
        {
            if (_posting || _detail?.Coin == null) return;
            // The entry is the server's current price at the moment of posting.
            var ticket = Tickets.AlphaCall(_coinId, _detail.Coin.PriceMicros, _targetInput, _stopInput);
            _callStatus.Clear();
            if (!ticket.Ok)
            {
                _callStatus.Add(MoneyUi.Notice(MoneyStrings.F(ticket.ErrorKey, ticket.Args), "error"));
                return;
            }
            var body = ticket.Body;
            var key = _callWrite.KeyFor("/alpha-calls", RetainedWrite.Body(body));
            _posting = true;
            _callStatus.Add(MoneyUi.Small(MoneyStrings.T("common.loading")));
            Run(ct => Hub.Api.PostAlphaCall(body, key, ct), call =>
            {
                _posting = false;
                _callWrite.Complete();
                _callStatus.Clear();
                _callStatus.Add(MoneyUi.Notice(MoneyStrings.T("pulse.callPosted")));
                _callsGate.Kick();
            }, err =>
            {
                _posting = false;
                if (!err.OutcomeUnknown) _callWrite.Complete();
                _callStatus.Clear();
                _callStatus.Add(MoneyUi.Notice(MoneyStrings.Err(err), "error"));
            }, write: true);
        }

        // ------------------------------------------------------------------ fetches

        private void FetchDetail()
        {
            var id = _coinId;
            _detailGate.Start();
            Run(ct => Hub.Api.Coin(id, ct), d =>
            {
                _detailGate.Succeeded(Now);
                if (id != _coinId) return;
                _detail = d;
                _detailError = null;
                Hub.Market.Remember(d.Coin);
                RenderDetailMarket();
                RenderCallEntry();
            }, err =>
            {
                _detailGate.Failed(Now);
                if (id != _coinId) return;
                _detailError = err;
                RenderDetailMarket();
            }, write: false);
        }

        private void FetchPositions()
        {
            _positionsGate.Start();
            Run(ct => Hub.Api.Positions(ct), rows =>
            {
                _positionsGate.Succeeded(Now);
                _positions = rows;
                _positionsError = null;
                foreach (var p in rows.Items) Hub.Market.Remember(p.CoinId, p.Ticker);
                if (_coinId != null) RenderSellInfo();
                else if (_tabs.Current == TabPositions) Render();
            }, err =>
            {
                _positionsGate.Failed(Now);
                _positionsError = err;
                if (_coinId == null && _tabs.Current == TabPositions) Render();
            }, write: false);
        }

        private void FetchSmart()
        {
            _smartGate.Start();
            Run(ct => Hub.Api.SmartMoney(ct), rows =>
            {
                _smartGate.Succeeded(Now);
                _smart = rows;
                _smartError = null;
                foreach (var s in rows.Items) Hub.Market.Remember(s.CoinId, s.Ticker);
                if (_coinId == null && _tabs.Current == TabSmart) Render();
            }, err =>
            {
                _smartGate.Failed(Now);
                _smartError = err;
                if (_coinId == null && _tabs.Current == TabSmart) Render();
            }, write: false);
        }

        private void FetchCalls()
        {
            var user = _callsUser;
            _callsGate.Start();
            Run(ct => Hub.Api.AlphaCalls(user, ct), res =>
            {
                _callsGate.Succeeded(Now);
                if (user != _callsUser) return;
                _calls = res;
                _callsError = null;
                if (_coinId == null && _tabs.Current == TabAlpha) Render();
            }, err =>
            {
                _callsGate.Failed(Now);
                if (user != _callsUser) return;
                _callsError = err;
                if (_coinId == null && _tabs.Current == TabAlpha) Render();
            }, write: false);
        }

        private string TickerOf(string coinId, string fallback)
        {
            var t = Hub.Market.Ticker(coinId);
            if (!string.IsNullOrEmpty(t)) return t;
            return string.IsNullOrEmpty(fallback) ? MoneyFormat.Short(coinId, 4, 0).TrimEnd('…') : fallback;
        }
    }
}
