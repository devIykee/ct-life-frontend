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

        /// <summary>Open on this tab next time ("launch" from the Launchpad Tower desk until the Factory screens exist).</summary>
        public static string PendingTab { get; set; }
        /// <summary>Open this coin's page next time (the Factory's "Open in Pulse").</summary>
        public static string PendingCoinNext { get; set; }

        internal override MoneyView CreateView(MoneyHub hub)
        {
            var v = new PulseView(hub, PendingCoinId ?? PendingCoinNext, PendingTab);
            PendingCoinId = null;
            PendingCoinNext = null;
            PendingTab = null;
            return v;
        }
    }

    internal sealed class PulseView : MoneyView
    {
        private const string TabMarket = "market", TabLaunch = "launch", TabPositions = "positions", TabTape = "tape", TabSmart = "smart", TabAlpha = "alpha";
        private static readonly string[] ListStages = { "new", "final", "graduated" };

        private readonly ChipTabs _tabs;
        private readonly VisualElement _body = El.Div("duo-comp-body");
        private readonly Label _cash = El.Text("", "duo-nav-note");
        private readonly DuoPage _page;
        private readonly VisualElement _bar;
        private DuoSheet _orderSheet;
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
        private bool _placing, _posting, _rugConfirmed;
        private readonly RetainedWrite _tradeWrite = new RetainedWrite();
        private readonly RetainedWrite _callWrite = new RetainedWrite();
        private Button _placeButton;
        private VisualElement _sideRow, _amountBox, _slipRow;

        // Lists
        private Rows<PositionRow> _positions;
        private MoneyError _positionsError;
        private readonly PollGate _positionsGate = new PollGate(5, 30);
        private Rows<SmartMoneyRow> _smart;
        private MoneyError _smartError;
        private readonly PollGate _smartGate = new PollGate(15, 60);
        private AlphaCallsResult _calls;
        private MoneyError _callsError;
        private readonly PollGate _callsGate = new PollGate(30, 60);
        private string _callsUser, _callsAlias;

        public PulseView(MoneyHub hub, string coinId) : this(hub, coinId, null) { }

        public PulseView(MoneyHub hub, string coinId, string tab) : base(hub)
        {
            _marketConfig = Services.TryGet<ConfigStore>(out var cfg) ? cfg.Config("market") : null;
            _tabs = new ChipTabs(Select)
                .Add(TabMarket, MoneyStrings.T("pulse.tab.market"))
                .Add(TabLaunch, MoneyStrings.T("pulse.tab.launch"))
                .Add(TabPositions, MoneyStrings.T("pulse.tab.positions"))
                .Add(TabTape, MoneyStrings.T("pulse.tab.tape"))
                .Add(TabSmart, MoneyStrings.T("pulse.tab.smart"))
                .Add(TabAlpha, MoneyStrings.T("pulse.tab.alpha"));
            _page = Framed(MoneyStrings.T("pulse.title"), "pulse-page");
            _page.AddToClassList("duo-pulse");
            _cash.name = "pulse-cash";
            _page.Nav.Right.Add(_cash);
            _tabs.Root.name = "pulse-tabs";
            _page.Body.Add(_tabs.Root);
            _page.Body.Add(_body);
            // Coin page: Robinhood-style glass Buy / Sell bar; the ticket opens in a sheet.
            var buy = El.Btn(MoneyStrings.T("pulse.buy"), () => OpenOrder("buy"), "duo-pulse-buy");
            buy.name = "pulse-buy";
            var sell = El.Btn(MoneyStrings.T("pulse.sell"), () => OpenOrder("sell"), "duo-pulse-sell");
            sell.name = "pulse-sell";
            _bar = Duo.BottomBar(buy, sell);
            _bar.name = "pulse-bar";
            _bar.Show(false);
            _page.Add(_bar);
            Hub.Market.Changed += MarketChanged;
            Hub.Market.TapeAdded += TapeAdded;
            FactoryArt.EnsureStyles(_page);
            _factory = FactoryService.Current;
            if (_factory != null) _factory.KnownChanged += FactoryKnown;
            Subs.On<ProfileChangedSignal>(_ => RenderCash());
            Subs.On<DegradedChangedSignal>(_ => Render());
            RenderCash();
            _tabs.Select(tab == TabLaunch ? TabLaunch : TabMarket, notify: false);
            if (!string.IsNullOrEmpty(coinId)) OpenCoin(coinId, null);
            else Render();
        }

        public override void Dispose()
        {
            Hub.Market.Changed -= MarketChanged;
            Hub.Market.TapeAdded -= TapeAdded;
            if (_factory != null) _factory.KnownChanged -= FactoryKnown;
            _orderSheet?.Close();
            base.Dispose();
        }

        private FactoryService _factory;

        /// <summary>A player coin's Factory facts arrived: redraw the lists (throttled) or the open coin page.</summary>
        private void FactoryKnown(string coinId)
        {
            if (!Alive) return;
            if (_coinId == coinId) RenderDetailMarket();
            else _listDirty = true;
        }

        private static long Cash => Services.TryGet<PlayerProfile>(out var p) ? p.CashCents : 0;
        private static bool MarketsOn => FeatureFlags.IsAvailable(FeatureFlags.Markets);

        private void RenderCash() => _cash.text = MoneyFormat.Cash(Cash);

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
            if (double.IsNaN(age)) return MoneyStrings.T("common.loading");
            var refreshed = age < 5 ? MoneyStrings.T("pulse.refreshedNow") : MoneyStrings.F("pulse.refreshedAgo", MoneyFormat.Ago(age));
            return MoneyStrings.F(Hub.Market.RoomJoined ? "pulse.joinedEmpty" : "pulse.polled", refreshed);
        }

        // ------------------------------------------------------------------ rendering

        private void Render()
        {
            if (!Alive) return;
            _body.Clear();
            _age = null;
            bool coin = _coinId != null;
            _tabs.Root.Show(!coin);
            _bar.Show(coin);
            _page.EnableInClassList("duo-pulse-coin", coin);
            _page.Title?.Show(!coin);
            _page.Nav.SetTitle(coin ? "$" + TickerOf(_coinId, null) : MoneyStrings.T("pulse.title"));
            if (coin) _page.Nav.SetCollapsed(true, instant: true);
            if (!MarketsOn) _body.Add(MoneyUi.Notice(MoneyStrings.T("common.marketsOff"), "warn"));
            if (coin)
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

        private static VisualElement Quote(string price, long bps) =>
            El.Div("duo-perp-quote", El.Text(price, "duo-row-value duo-perp-price"),
                El.Text(MoneyFormat.Bps(bps), "duo-perp-change " + (bps >= 0 ? "duo-value-positive" : "duo-value-negative")));

        private static VisualElement Empty(string text) => Duo.Empty(text);

        private void RenderMarket()
        {
            _listDirty = false;
            _lastListRender = Now;
            var stages = El.Div("duo-chip-row duo-pulse-stages");
            foreach (var s in ListStages)
            {
                var stage = s;
                stages.Add(MoneyUi.Chip(MoneyStrings.T("pulse.stage." + stage), () => { _stage = stage; Hub.Market.Kick(); Render(); }, _stage == stage));
            }
            _body.Add(stages);
            AddListHeader();
            var coins = Hub.Market.List(_stage);
            if (coins.Count == 0) { _body.Add(Empty(MoneyStrings.T("pulse.empty"))); return; }
            var list = El.Div("duo-list");
            foreach (var c in coins) list.Add(CoinRow(c, null));
            Duo.MarkLast(list);
            _body.Add(list);
        }

        private VisualElement CoinRow(MarketCoin c, VisualElement extra)
        {
            var coin = c;
            bool player = FactoryPulse.IsPlayer(c);
            if (player && extra == null) extra = FactoryPulse.RowExtra(c);
            var row = new DuoRow(() => OpenCoin(coin.CoinId, null)).Leading(player ? FactoryPulse.Leading(c) : Duo.Avatar(c.Ticker, c.CoinId, "duo-avatar-s"))
                .Title("$" + c.Ticker, "duo-row-title-strong")
                .Subtitle(c.Name + " · " + MoneyStrings.F("pulse.mcap", MoneyFormat.Cash(c.McapCents, true)) + " · " + MoneyStrings.F("pulse.holders", c.Holders), twoLines: true);
            row.name = "pulse-coin-" + c.CoinId;
            if (extra != null) row.TextColumn.Add(extra);
            row.Trailing(Quote(MoneyFormat.Micros(c.PriceMicros), c.Change5mBps));
            return row;
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
            if (coins.Count == 0) { _body.Add(Empty(MoneyStrings.T("pulse.empty"))); return; }
            var list = El.Div("duo-list");
            foreach (var c in coins)
            {
                VisualElement extra = null;
                if (FactoryPulse.IsPlayer(c)) extra = FactoryPulse.RowExtra(c);   // player coins aim at 3x, not graduation
                else if (graduate > 0)
                {
                    // Display ratio of two server values (mcap vs the config threshold); not a money outcome.
                    float pct = Math.Max(0f, Math.Min(1f, (float)((double)c.McapCents / graduate)));
                    var fill = El.Div("duo-meter-fill");
                    fill.style.width = new Length(pct * 100f, LengthUnit.Percent);
                    var launched = c.LaunchedAtMs.HasValue ? MoneyStrings.F("pulse.launched", MoneyFormat.Ago((ServerClock.NowMs - c.LaunchedAtMs.Value) / 1000.0)) + " · " : "";
                    extra = El.Div("duo-pulse-launch", El.Div("duo-meter duo-meter-green", fill),
                        El.Text(launched + MoneyStrings.F("pulse.launchProgress", MoneyFormat.Cash(Math.Max(0, graduate - c.McapCents), true)), "duo-row-subtitle"));
                }
                list.Add(CoinRow(c, extra));
            }
            Duo.MarkLast(list);
            _body.Add(list);
        }

        private void RenderPositions()
        {
            if (_positionsError != null) _body.Add(MoneyUi.Notice(MoneyStrings.Err(_positionsError), "error"));
            if (_positions == null)
            {
                if (_positionsError == null) _body.Add(MoneyUi.Small(MoneyStrings.T("common.loading")));
                return;
            }
            // Hero: total unrealized PnL across the server's open positions.
            long total = _positions.Items.Sum(p => p.UnrealizedCents);
            if (_positions.Items.Count > 0)
            {
                var hero = Duo.Hero(MoneyStrings.T("pulse.unrealizedLabel"), MoneyFormat.SignedCash(total), null, null, MoneyStrings.F("pulse.positionsCount", _positions.Items.Count));
                hero.name = "pulse-positions-hero";
                hero.Q<Label>(className: "duo-hero-value")?.AddToClassList(MoneyUi.PnlClass(total));
                _body.Add(hero);
            }
            _body.Add(MoneyUi.Small(MoneyUi.Age(_positionsGate.Age(Now))));
            if (_positions.Skipped > 0) _body.Add(MoneyUi.Small(MoneyStrings.F("common.skipped", _positions.Skipped)));
            if (_positions.Items.Count == 0) { _body.Add(Empty(MoneyStrings.T("pulse.posEmpty"))); return; }
            var list = El.Div("duo-list");
            foreach (var p in _positions.Items)
            {
                var pos = p;
                var ticker = TickerOf(p.CoinId, p.Ticker);
                var row = new DuoRow(() => PnlSheet(PnlCardData.From(pos, ticker), pos.CoinId)).Leading(Duo.Avatar(ticker, p.CoinId, "duo-avatar-s"))
                    .Title("$" + ticker, "duo-row-title-strong")
                    .Subtitle(MoneyStrings.F("pulse.held", MoneyFormat.CoinUnits(p.Units)) + " · " + MoneyStrings.F("pulse.cost", MoneyFormat.Cash(p.CostBasisCents)), twoLines: true);
                row.name = "pulse-position-" + p.CoinId;
                row.Trailing(MoneyUi.Pnl(p.UnrealizedCents));
                list.Add(row);
            }
            Duo.MarkLast(list);
            _body.Add(list);
        }

        private void PnlSheet(PnlCardData d, string coinId)
        {
            var sheet = new DuoSheet("$" + d.Ticker, MoneyStrings.T("pulse.pnlCard"));
            sheet.name = "pulse-pnl-sheet";
            var hero = Duo.Hero(MoneyStrings.T("pulse.unrealizedLabel"), MoneyFormat.SignedCash(d.UnrealizedCents),
                d.ReturnBps.HasValue ? MoneyFormat.Bps(d.ReturnBps.Value) : null, d.UnrealizedCents >= 0);
            hero.Q<Label>(className: "duo-hero-value")?.AddToClassList(MoneyUi.PnlClass(d.UnrealizedCents));
            sheet.AddContent(hero);
            sheet.AddContent(MoneyUi.Pair(MoneyStrings.T("label.cost"), MoneyFormat.Cash(d.CostBasisCents)));
            sheet.AddContent(MoneyUi.Pair(MoneyStrings.T("pulse.heldRow"), d.UnitsText));
            sheet.AddContent(MoneyUi.Row(MoneyUi.Btn(MoneyStrings.T("pulse.copySummary"), () => MoneyUi.Copy(d.ShareText()), MoneyUi.Plain)));
            sheet.AddPrimary(MoneyStrings.T("pulse.sell"), () => OpenCoin(coinId, "sell"), destructive: true, name: "pulse-pnl-sell");
            sheet.AddSecondary(MoneyStrings.T("common.done"));
            ShowSheet(sheet);
        }

        private void RenderTape()
        {
            long threshold = Wire.LongOr(_marketConfig?["tapeThresholdCents"], 100000);
            if (!Hub.Market.RoomJoined) _body.Add(MoneyUi.Notice(MoneyStrings.T("pulse.tapeOffline"), "warn"));
            var rows = Hub.Market.Tape.ToList();
            if (rows.Count == 0) { _body.Add(Empty(MoneyStrings.F("pulse.tapeEmpty", MoneyFormat.Cash(threshold)))); return; }
            var list = El.Div("duo-list");
            foreach (var t in rows)
            {
                var row = t;
                var ticker = TickerOf(row.CoinId, null);
                var r = new DuoRow(() => OpenCoin(row.CoinId, null)).Leading(Duo.Avatar(row.Name, row.Name, "duo-avatar-s"))
                    .Title(row.Name + " " + (row.Side == "sell" ? MoneyStrings.T("pulse.sell") : MoneyStrings.T("pulse.buy")).ToLowerInvariant() + " $" + ticker, "duo-row-title-strong")
                    .Subtitle(MoneyFormat.Ago(Now - row.ReceivedAt));
                r.Value(MoneyFormat.Cash(row.CashCents), row.Side == "sell" ? "duo-value-negative" : "duo-value-positive");
                list.Add(r);
            }
            Duo.MarkLast(list);
            _body.Add(list);
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
            if (_smart.Items.Count == 0) { _body.Add(Empty(MoneyStrings.T("pulse.smartEmpty"))); return; }
            var list = El.Div("duo-list");
            foreach (var s in _smart.Items)
            {
                var row = s;
                var age = s.AtMs.HasValue ? " · " + MoneyFormat.Ago((ServerClock.NowMs - s.AtMs.Value) / 1000.0) : "";
                var r = new DuoRow(() => ShowCalls(row.UserId, row.Alias)).Leading(Duo.Avatar(s.Alias, s.UserId, "duo-avatar-s"))
                    .Title(s.Alias, "duo-row-title-strong")
                    .Subtitle((s.Side == "sell" ? MoneyStrings.T("pulse.sell") : MoneyStrings.T("pulse.buy")) + " $" + TickerOf(s.CoinId, s.Ticker) + " · " + MoneyFormat.Cash(s.CashCents) + age);
                r.name = "pulse-smart-" + s.UserId;
                r.Trailing(Duo.Pill(MoneyStrings.T("pulse.copy"), () => OpenCoin(row.CoinId, row.Side), Duo.PillKind.Primary));
                list.Add(r);
            }
            Duo.MarkLast(list);
            _body.Add(list);
            _body.Add(Duo.Footnote(MoneyStrings.T("pulse.smartTrustHint")));
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
                _body.Add(Duo.SectionHeader(MoneyStrings.F("pulse.callsOf", _callsAlias), MoneyStrings.T("pulse.callsMine"), () => { _callsUser = null; _calls = null; _callsGate.Kick(); Render(); }));
            else _body.Add(Duo.SectionHeader(MoneyStrings.T("pulse.callsMine")));
            _body.Add(MoneyUi.Small(MoneyStrings.T("pulse.callsImmutable")));
            if (_callsError != null) _body.Add(MoneyUi.Notice(MoneyStrings.Err(_callsError), "error"));
            if (_calls == null)
            {
                if (_callsError == null) _body.Add(MoneyUi.Small(MoneyStrings.T("common.loading")));
                return;
            }
            var t = _calls.Trust;
            if (t != null)
            {
                _body.Add(MoneyUi.Label(MoneyStrings.T("pulse.trust")));
                _body.Add(El.Div("duo-kv-grid",
                    Kv(MoneyStrings.T("label.hitRate"), MoneyFormat.Ratio(t.HitRate)),
                    Kv(MoneyStrings.T("label.avgReturn"), MoneyFormat.Bps(t.AvgReturnBps)),
                    Kv(MoneyStrings.T("label.drawdown"), MoneyFormat.Bps(t.MaxDrawdownBps, false)),
                    Kv(MoneyStrings.T("label.heldBag"), t.HeldBagPct + "%")));
            }
            if (_calls.Skipped > 0) _body.Add(MoneyUi.Small(MoneyStrings.F("common.skipped", _calls.Skipped)));
            if (_calls.Calls.Count == 0) { _body.Add(Empty(MoneyStrings.T("pulse.callsEmpty"))); return; }
            var list = El.Div("duo-list");
            foreach (var c in _calls.Calls)
            {
                var call = c;
                var when = c.AtMs.HasValue ? MoneyFormat.Ago((ServerClock.NowMs - c.AtMs.Value) / 1000.0) : "";
                var ticker = TickerOf(c.CoinId, null);
                var r = new DuoRow(() => OpenCoin(call.CoinId, null)).Leading(Duo.Avatar(ticker, c.CoinId, "duo-avatar-s")).Title("$" + ticker, "duo-row-title-strong")
                    .Subtitle(MoneyStrings.F("pulse.callLine", MoneyFormat.Micros(c.EntryMicros), MoneyFormat.Micros(c.TargetMicros), MoneyFormat.Micros(c.StopMicros)) + (when.Length > 0 ? "\n" + when : ""), twoLines: true);
                if (!string.IsNullOrEmpty(c.Status)) r.Trailing(El.Text(c.Status, "duo-tag"));
                r.Chevron();
                list.Add(r);
            }
            Duo.MarkLast(list);
            _body.Add(list);
        }

        private static VisualElement Kv(string key, string value, string valueClass = null) =>
            El.Div("duo-kv", El.Text(key, "duo-kv-key"), El.Text(value, "duo-kv-value" + (valueClass != null ? " " + valueClass : "")));

        // ------------------------------------------------------------------ coin page

        protected override bool Back()
        {
            if (_coinId == null) return false;
            _orderSheet?.Close();
            _coinId = null;
            Render();
            return true;
        }

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
            Root.scrollOffset = UnityEngine.Vector2.zero;
            Render();
            // A "Copy" or "Sell" deep link lands on the ticket for that side.
            if (side == "buy" || side == "sell") Root.schedule.Execute(() => { if (Alive && _coinId == coinId) OpenOrder(_side); });
        }

        private PositionRow Held => _positions?.Items.FirstOrDefault(p => p.CoinId == _coinId);

        private void RenderCoin()
        {
            _detailMarket = El.Div("duo-comp-body");
            _body.Add(_detailMarket);
            RenderDetailMarket();
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
            _page.Nav.SetTitle("$" + coin.Ticker);
            var asset = El.Div("duo-perp-asset", Duo.Avatar(coin.Ticker, coin.CoinId, "duo-avatar-s"),
                El.Text("$" + coin.Ticker + " · " + coin.Name, "duo-perp-asset-name"),
                El.Text(MoneyStrings.T("pulse.stage." + coin.Stage), "duo-tag duo-tag-blue"));
            if (FactoryPulse.IsPlayer(Hub.Market.Find(coin.CoinId) ?? coin))
            {
                var live = Hub.Market.Find(coin.CoinId);
                if (live != null && live.TemplateId != null && coin.TemplateId == null) { coin.TemplateId = live.TemplateId; coin.PlayerLaunched = true; }
                _detailMarket.Add(FactoryPulse.PageHeader(coin));
            }
            else _detailMarket.Add(asset);
            var hero = Duo.Hero("", MoneyFormat.Micros(coin.PriceMicros), MoneyFormat.Bps(coin.Change5mBps), coin.Change5mBps >= 0, MoneyStrings.T("perps.change5m"));
            hero.name = "pulse-hero";
            hero.AddToClassList("duo-pulse-hero");
            _detailMarket.Add(hero);
            // Real 1-minute candles only; nothing drawn without them.
            if (_detail != null && _detail.Candles.Count >= 2)
            {
                var chart = new DuoLineChart();
                chart.name = "pulse-chart";
                chart.SetData(_detail.Candles.Select(c => (double)c.Close));
                _detailMarket.Add(chart);
                _detailMarket.Add(Duo.Footnote(MoneyStrings.T("pulse.chart")));
            }
            else _detailMarket.Add(Duo.Footnote(_detail == null ? MoneyStrings.T("common.loading") : MoneyStrings.T("pulse.noChart")));
            _detailMarket.Add(El.Div("duo-kv-grid",
                Kv(MoneyStrings.T("label.mcap"), MoneyFormat.Cash(coin.McapCents, true)),
                Kv(MoneyStrings.T("label.vol"), MoneyFormat.Cash(coin.Vol5mCents, true)),
                Kv(MoneyStrings.T("label.holders"), coin.Holders.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Kv(MoneyStrings.T("pulse.heldRow"), Held != null ? MoneyFormat.CoinUnits(Held.Units) : "—")));
            if (_detailError != null) _detailMarket.Add(MoneyUi.Notice(MoneyStrings.Err(_detailError), "error"));
            if (_detail == null) return;
            if (!string.IsNullOrEmpty(_detail.RiskHint))
            {
                _detailMarket.Add(MoneyUi.Label(MoneyStrings.T("pulse.risk")));
                _detailMarket.Add(MoneyUi.Notice(_detail.RiskHint, "warn"));
            }
            _detailMarket.Add(Duo.SectionHeader(MoneyStrings.T("pulse.holdersTitle")));
            if (_detail.Holders.Count == 0) _detailMarket.Add(MoneyUi.Small(MoneyStrings.T("pulse.noHolders")));
            var holders = El.Div("duo-list");
            int rank = 1;
            foreach (var h in _detail.Holders.Take(10))
                holders.Add(DuoBoard.Row(new DuoRank(rank++, h.Alias, h.Alias, MoneyFormat.CoinUnits(h.Units), false)));
            Duo.MarkLast(holders);
            _detailMarket.Add(holders);
        }

        /// <summary>The order ticket in a sheet: side, amount (Cash or % of the holding), slippage, place, status and receipt.</summary>
        private void OpenOrder(string side)
        {
            if (_coinId == null) return;
            _side = side == "sell" ? "sell" : "buy";
            _orderSheet?.Close();
            var sheet = new DuoSheet(MoneyStrings.F("pulse.ticketTitle", TickerOf(_coinId, null)));
            sheet.name = "pulse-order-sheet";
            var seg = new DuoSegmented(new[] { MoneyStrings.T("pulse.buy"), MoneyStrings.T("pulse.sell") }, _side == "sell" ? 1 : 0, i => { _side = i == 1 ? "sell" : "buy"; RenderOrderControls(); });
            seg.name = "pulse-order-side";
            _sideRow = El.Div("duo-perp-side", seg);
            _amountBox = El.Div("duo-comp-body");
            _slipRow = El.Div("duo-chip-row");
            _orderStatus = El.Div("duo-comp-body");
            _orderReceipt = El.Div("duo-comp-body");
            _placeButton = MoneyUi.Btn(MoneyStrings.T("pulse.place"), PlaceOrder);
            _placeButton.name = "pulse-place";
            _placeButton.AddToClassList("duo-pulse-place");
            sheet.AddContent(_sideRow);
            sheet.AddContent(_amountBox);
            sheet.AddContent(MoneyUi.Label(MoneyStrings.T("pulse.slippage")));
            sheet.AddContent(_slipRow);
            sheet.AddContent(_orderStatus);
            sheet.AddContent(_orderReceipt);
            sheet.AddContent(_placeButton);
            sheet.AddSecondary(MoneyStrings.T("common.done"));
            sheet.Closed += () => { if (_orderSheet == sheet) _orderSheet = null; };
            _orderSheet = sheet;
            sheet.Scrolling().Show(Container);
            RenderOrderControls();
        }

        private void RenderOrderControls()
        {
            if (_amountBox == null) return;
            _amountBox.Clear();
            _sellInfo = null;
            if (_side == "buy")
            {
                var field = MoneyUi.Field(MoneyStrings.T("pulse.spend"), _cashInput, v => _cashInput = v);
                field.name = "pulse-spend";
                _amountBox.Add(field);
                var quick = El.Div("duo-chip-row");
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
                var pcts = El.Div("duo-chip-row");
                foreach (var pct in new[] { 25, 50, 100 })
                {
                    var p = pct;
                    pcts.Add(MoneyUi.Chip(MoneyStrings.F("pulse.sellPct", p), () => { _sellPct = p; RenderOrderControls(); }, _sellPct == p));
                }
                _amountBox.Add(pcts);
                _sellInfo = El.Div("duo-comp-body");
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
            _placeButton.EnableInClassList("duo-comp-btn-danger", _side == "sell");
            _placeButton.EnableInClassList("duo-comp-btn-primary", _side != "sell");
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
            // Rugpull Factory (brief beat 6): selling 30 %+ of your own coin's bag always gets the rug warning first
            if (ticket.Ok && _side == "sell" && !_rugConfirmed && FactoryLaunchFlow.NeedsRugWarning(_factory != null && _factory.IsMine(_coinId), _sellPct))
            {
                var ticker = TickerOf(_coinId, null);
                FactoryLaunchFlow.WarnRug(ticker, () => { _rugConfirmed = true; PlaceOrder(); }, () => { });
                return;
            }
            _rugConfirmed = false;
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
                if (side != "sell") Bus.Publish(new GameplayEventSignal { kind = GameplayEvents.TradeOpen, id = "pulse" });   // the first session
                _orderReceipt.Clear();
                _orderReceipt.Add(MoneyUi.Notice(MoneyStrings.T("pulse.receipt"), "good"));
                _orderReceipt.Add(MoneyUi.Pair(MoneyStrings.T("pulse.unitsRow"), MoneyFormat.CoinUnits(receipt.Units)));
                _orderReceipt.Add(MoneyUi.Pair(MoneyStrings.T("pulse.priceRow"), MoneyFormat.Micros(receipt.PriceMicros)));
                _orderReceipt.Add(MoneyUi.Pair(MoneyStrings.T("pulse.cashRow"), MoneyFormat.Cash(receipt.CashCents)));
                if (receipt.FeeCents.HasValue) _orderReceipt.Add(MoneyUi.Pair(MoneyStrings.T("pulse.feeRow"), MoneyFormat.Cash(receipt.FeeCents.Value)));
                if (receipt.RealizedCents.HasValue && side == "sell")
                    _orderReceipt.Add(MoneyUi.Pair(MoneyStrings.T("pulse.realizedRow"), MoneyFormat.SignedCash(receipt.RealizedCents.Value), MoneyUi.PnlClass(receipt.RealizedCents.Value)));
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
                        MoneyUi.Btn(MoneyStrings.T("pulse.checkPositions"), () => { _tradeWrite.Complete(); _orderSheet?.Close(); _tabs.Select(TabPositions); }, MoneyUi.Plain)));
                Bus.Publish(new SfxSignal("sfx_ui_error"));
                RenderOrderControls();
            }, write: true);
        }

        private void BuildCallPanel()
        {
            _callStatus = El.Div("duo-comp-body");
            _body.Add(Duo.SectionHeader(MoneyStrings.T("pulse.callsSection"), MoneyStrings.T("pulse.postCallShort"), CallSheet));
            _body.Add(MoneyUi.Small(MoneyStrings.T("pulse.callsImmutable")));
            _body.Add(_callStatus);
            RenderCallEntry();
        }

        private void CallSheet()
        {
            var sheet = new DuoSheet(MoneyStrings.T("pulse.postCall"), MoneyStrings.T("pulse.callsImmutable"));
            sheet.name = "pulse-call-sheet";
            var entry = MoneyUi.Small("");
            var price = _detail?.Coin?.PriceMicros;
            entry.text = price.HasValue ? MoneyStrings.F("pulse.callEntry", MoneyFormat.Micros(price.Value)) : MoneyStrings.T("common.loading");
            sheet.AddContent(entry);
            sheet.AddContent(MoneyUi.Field(MoneyStrings.T("pulse.callTarget"), _targetInput, v => _targetInput = v));
            sheet.AddContent(MoneyUi.Field(MoneyStrings.T("pulse.callStop"), _stopInput, v => _stopInput = v));
            var post = MoneyUi.Btn(MoneyStrings.T("pulse.postCall"), () => { PostCall(); });
            post.name = "pulse-call-post";
            sheet.AddContent(_callStatus);
            sheet.AddContent(post);
            sheet.AddSecondary(MoneyStrings.T("common.done"));
            sheet.Closed += () => { if (_coinId != null && _body.panel != null) { _body.Add(_callStatus); } };
            ShowSheet(sheet);
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
                _callStatus.Add(MoneyUi.Notice(MoneyStrings.T("pulse.callPosted"), "good"));
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
                if (_coinId != null) { RenderSellInfo(); RenderDetailMarket(); }
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
