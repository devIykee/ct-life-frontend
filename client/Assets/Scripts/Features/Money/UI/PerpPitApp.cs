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
    /// </summary>
    public sealed class PerpPitApp : MoneyApp
    {
        public override string Id => "perps";
        public override string Title => MoneyStrings.T("perps.title");
        public override string Color => "#eb3b5a";
        public override int Phase => 2;
        public override string Feature => FeatureFlags.Markets;

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
        private readonly ChipTabs _tabs;
        private readonly VisualElement _body = El.Div("ct-col");
        private readonly Label _cash = MoneyUi.Small("");
        private readonly VisualElement _liqNotice = El.Div("ct-col");

        // Trade
        private string _coinId, _side = "long", _marginInput = "";
        private int _leverage = 1;
        private int? _cap;
        private bool _opening;
        private readonly RetainedWrite _openWrite = new RetainedWrite();
        private VisualElement _tradeControls, _tradeStatus, _coinList;
        private double _coinListRenderedAt;

        // Lists
        private Rows<PerpRow> _open, _history;
        private MoneyError _openError, _historyError;
        private readonly PollGate _openGate = new PollGate(5, 30), _historyGate = new PollGate(20, 60), _rektGate = new PollGate(30, 120);
        private readonly Dictionary<string, RetainedWrite> _closeWrites = new Dictionary<string, RetainedWrite>();
        private readonly HashSet<string> _closing = new HashSet<string>();
        private string _confirmClose;
        private readonly Dictionary<string, string> _closeResult = new Dictionary<string, string>();
        private Rows<TombstoneRow> _tombstones;
        private MoneyError _rektError;

        public PerpPitView(MoneyHub hub, string tab) : base(hub)
        {
            _tabs = new ChipTabs(_ => Render())
                .Add("trade", MoneyStrings.T("perps.tab.trade"))
                .Add("open", MoneyStrings.T("perps.tab.open"))
                .Add("history", MoneyStrings.T("perps.tab.history"))
                .Add("rekt", MoneyStrings.T("perps.tab.rekt"));
            Root.Add(MoneyUi.Row(MoneyUi.Text(MoneyStrings.T("perps.title"), "ct-h1"), El.Div("ct-grow"), _cash));
            Root.Add(_liqNotice);
            Root.Add(_tabs.Root);
            Root.Add(_body);
            Subs.On<ProfileChangedSignal>(_ => { RenderCash(); ComputeCap(); });
            Subs.On<DegradedChangedSignal>(_ => Render());
            Subs.On<LiquidatedMessage>(OnLiquidated);
            Hub.Market.Changed += MarketChanged;
            RenderCash();
            ComputeCap();
            _tabs.Select(tab, notify: false);
            Render();
        }

        public override void Dispose()
        {
            Hub.Market.Changed -= MarketChanged;
            base.Dispose();
        }

        private static long Cash => Services.TryGet<PlayerProfile>(out var p) ? p.CashCents : 0;
        private static bool MarketsOn => FeatureFlags.IsAvailable(FeatureFlags.Markets);
        private void RenderCash() => _cash.text = MoneyStrings.F("common.cash", MoneyFormat.Cash(Cash));

        private void ComputeCap()
        {
            var me = Services.TryGet<PlayerProfile>(out var p) ? p.Me : null;
            var skills = Services.TryGet<ConfigStore>(out var cfg) ? cfg.Config("skills") : null;
            _cap = LeverageCap.Compute(skills, me?.Skills != null ? me.Skills.Trading : (int?)null, me?.Character?.TribeId);
            int max = Math.Min(Tickets.ContractMaxLeverage, _cap ?? Tickets.ContractMaxLeverage);
            if (_leverage > max) _leverage = max;
            if (_tabs?.Current == "trade") RenderTradeControls();
        }

        private void MarketChanged()
        {
            if (_tabs.Current == "trade" && Now - _coinListRenderedAt > 2) RenderCoinList();
        }

        private void OnLiquidated(LiquidatedMessage m)
        {
            var model = RektCardModel.From(m.perpId, m.coinId, m.lossCents, Hub.Market.Ticker);
            _liqNotice.Clear();
            _liqNotice.Add(MoneyUi.Notice(MoneyStrings.T("perps.liquidated") + (model.Ticker != null ? " $" + model.Ticker : "") + " " + model.LossText, "error"));
            _openGate.Kick();
            _historyGate.Kick();
            _rektGate.Kick();
        }

        protected override void Tick()
        {
            double now = Now;
            switch (_tabs.Current)
            {
                case "trade":
                    Hub.Market.Pump(new string[] { null }, Life);
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
            _tradeControls = _tradeStatus = _coinList = null;
            if (!MarketsOn) _body.Add(MoneyUi.Notice(MoneyStrings.T("common.marketsOff"), "warn"));
            switch (_tabs.Current)
            {
                case "trade": RenderTrade(); break;
                case "open": RenderOpen(); break;
                case "history": RenderHistory(); break;
                case "rekt": RenderRekt(); break;
            }
        }

        // ------------------------------------------------------------------ trade

        private void RenderTrade()
        {
            _body.Add(MoneyUi.Text(MoneyStrings.T("perps.pickCoin"), "ct-section-title"));
            _coinList = El.Div("ct-app-tabs ct-money-tabs");
            _body.Add(_coinList);
            RenderCoinList();
            _tradeControls = El.Div("ct-col");
            _body.Add(_tradeControls);
            RenderTradeControls();
            _tradeStatus = El.Div("ct-col");
            _body.Add(_tradeStatus);
        }

        private void RenderCoinList()
        {
            if (_coinList == null) return;
            _coinListRenderedAt = Now;
            _coinList.Clear();
            var coins = Hub.Market.List(null).OrderByDescending(c => c.McapCents).Take(16).ToList();
            if (coins.Count == 0) _coinList.Add(MoneyUi.Small(Hub.Market.LastError != null ? MoneyStrings.T(Hub.Market.LastError) : MoneyStrings.T("common.loading")));
            foreach (var c in coins)
            {
                var coin = c;
                _coinList.Add(MoneyUi.Chip("$" + c.Ticker + "  " + MoneyFormat.Micros(c.PriceMicros), () => { _coinId = coin.CoinId; RenderCoinList(); }, c.CoinId == _coinId));
            }
        }

        private void RenderTradeControls()
        {
            if (_tradeControls == null) return;
            _tradeControls.Clear();
            var sides = El.Div("ct-app-tabs ct-money-tabs");
            sides.Add(MoneyUi.Chip(MoneyStrings.T("perps.long"), () => { _side = "long"; RenderTradeControls(); }, _side == "long"));
            sides.Add(MoneyUi.Chip(MoneyStrings.T("perps.short"), () => { _side = "short"; RenderTradeControls(); }, _side == "short"));
            _tradeControls.Add(sides);

            int max = Math.Min(Tickets.ContractMaxLeverage, _cap ?? Tickets.ContractMaxLeverage);
            var minus = MoneyUi.Btn("−", () => { _leverage = Math.Max(1, _leverage - 1); RenderTradeControls(); }, "ct-btn ct-btn-ghost");
            var plus = MoneyUi.Btn("+", () => { _leverage = Math.Min(max, _leverage + 1); RenderTradeControls(); }, "ct-btn ct-btn-ghost");
            minus.SetEnabled(_leverage > 1);
            plus.SetEnabled(_leverage < max);
            _tradeControls.Add(MoneyUi.Row(minus, MoneyUi.Text(MoneyStrings.F("perps.leverage", _leverage), "ct-h2 ct-money-lev"), plus));
            _tradeControls.Add(MoneyUi.Small(_cap.HasValue ? MoneyStrings.F("perps.cap", _cap.Value) : MoneyStrings.T("perps.capUnknown")));

            var field = MoneyUi.Field(MoneyStrings.T("perps.margin"), _marginInput, v => _marginInput = v);
            _tradeControls.Add(field);
            var quick = El.Div("ct-app-tabs ct-money-tabs");
            foreach (var dollars in new[] { "10", "50", "100", "500" })
            {
                var d = dollars;
                quick.Add(MoneyUi.Chip("$" + d, () => { _marginInput = d; field.SetValueWithoutNotify(d); }, false));
            }
            _tradeControls.Add(quick);
            var open = MoneyUi.Btn(MoneyStrings.T(_opening ? "perps.opening" : "perps.open"), OpenPerp);
            open.SetEnabled(!_opening && MarketsOn);
            _tradeControls.Add(open);
        }

        private void OpenPerp()
        {
            if (_opening || !MarketsOn) return;
            var ticket = Tickets.OpenPerp(_coinId, _side, _leverage, _cap, _marginInput, Cash);
            _tradeStatus?.Clear();
            if (!ticket.Ok)
            {
                _tradeStatus?.Add(MoneyUi.Notice(MoneyStrings.F(ticket.ErrorKey, ticket.Args), "error"));
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
                _tradeStatus?.Add(MoneyUi.Card(
                    MoneyUi.Text(MoneyStrings.T("perps.opened"), "ct-h2 ct-money-up"),
                    MoneyUi.Text("$" + TickerOf(perp) + " · " + MoneyStrings.T(perp.Side == "short" ? "perps.short" : "perps.long") + " · " + perp.Leverage + "x"),
                    MoneyUi.Pair(MoneyStrings.T("label.margin"), MoneyFormat.Cash(perp.MarginCents)),
                    MoneyUi.Pair(MoneyStrings.T("label.entry"), MoneyFormat.Micros(perp.EntryMicros)),
                    MoneyUi.Pair(MoneyStrings.T("label.liq"), MoneyFormat.Micros(perp.LiqMicros), "ct-money-down")));
                Hub.RefreshProfile();
                _openGate.Kick();
                RenderTradeControls();
            }, err =>
            {
                _opening = false;
                if (!err.OutcomeUnknown) _openWrite.Complete();
                _tradeStatus?.Clear();
                _tradeStatus?.Add(MoneyUi.Notice(MoneyStrings.Err(err), "error"));
                if (err.OutcomeUnknown)
                    _tradeStatus?.Add(MoneyUi.Row(MoneyUi.Btn(MoneyStrings.T("common.retry"), OpenPerp),
                        MoneyUi.Btn(MoneyStrings.T("perps.tab.open"), () => { _openWrite.Complete(); _tabs.Select("open"); }, "ct-btn ct-btn-ghost")));
                Bus.Publish(new SfxSignal("sfx_ui_error"));
                RenderTradeControls();
            }, write: true);
        }

        // ------------------------------------------------------------------ open positions

        private void RenderOpen()
        {
            _body.Add(MoneyUi.Small(MoneyUi.Age(_openGate.Age(Now))));
            if (_openError != null) _body.Add(MoneyUi.Notice(MoneyStrings.Err(_openError), "error"));
            if (_open == null)
            {
                if (_openError == null) _body.Add(MoneyUi.Small(MoneyStrings.T("common.loading")));
                return;
            }
            if (_open.Skipped > 0) _body.Add(MoneyUi.Small(MoneyStrings.F("common.skipped", _open.Skipped)));
            var rows = _open.Items.Where(p => p.Status == "open").ToList();
            if (rows.Count == 0) _body.Add(MoneyUi.Text(MoneyStrings.T("perps.openEmpty"), "ct-body ct-muted"));
            foreach (var p in rows) _body.Add(PerpCard(p, closable: true));
            foreach (var kv in _closeResult) _body.Add(MoneyUi.Notice(kv.Value));
        }

        private VisualElement PerpCard(PerpRow p, bool closable)
        {
            var perp = p;
            var mark = Hub.Market.Find(p.CoinId);
            var card = MoneyUi.Card(
                MoneyUi.Row(MoneyUi.Text("$" + TickerOf(p), "ct-money-title"), El.Div("ct-grow"),
                    MoneyUi.Text(MoneyStrings.T(p.Side == "short" ? "perps.short" : "perps.long") + " " + p.Leverage + "x", "ct-badge")),
                MoneyUi.Pair(MoneyStrings.T("label.margin"), MoneyFormat.Cash(p.MarginCents)),
                MoneyUi.Pair(MoneyStrings.T("label.entry"), MoneyFormat.Micros(p.EntryMicros)),
                MoneyUi.Pair(MoneyStrings.T("label.liq"), MoneyFormat.Micros(p.LiqMicros), "ct-money-down"),
                mark != null && p.Status == "open" ? MoneyUi.Pair(MoneyStrings.T("label.mark"), MoneyFormat.Micros(mark.PriceMicros)) : null,
                p.RealizedCents.HasValue ? El.Div("ct-row ct-money-pair", MoneyUi.Text(MoneyStrings.T("label.realized"), "ct-money-key"), El.Div("ct-grow"), MoneyUi.Pnl(p.RealizedCents.Value)) : null,
                p.Status == "liquidated" ? MoneyUi.Text(MoneyStrings.T("perps.liquidated"), "ct-money-value ct-money-down") : null);
            if (!closable) return card;
            if (_closing.Contains(p.PerpId)) card.Add(MoneyUi.Small(MoneyStrings.T("perps.closing")));
            else if (_confirmClose == p.PerpId)
                card.Add(MoneyUi.Row(
                    MoneyUi.Text(MoneyStrings.T("perps.closeConfirm")),
                    El.Div("ct-grow"),
                    MoneyUi.Btn(MoneyStrings.T("perps.close"), () => ClosePerp(perp), "ct-btn ct-btn-danger"),
                    MoneyUi.Btn(MoneyStrings.T("common.cancel"), () => { _confirmClose = null; Render(); }, "ct-btn ct-btn-ghost")));
            else
            {
                var close = MoneyUi.Btn(MoneyStrings.T("perps.close"), () => { _confirmClose = perp.PerpId; Render(); }, "ct-btn ct-btn-ghost");
                close.SetEnabled(MarketsOn);
                card.Add(close);
            }
            return card;
        }

        private void ClosePerp(PerpRow p)
        {
            if (_closing.Contains(p.PerpId) || !MarketsOn) return;
            if (!_closeWrites.TryGetValue(p.PerpId, out var write)) _closeWrites[p.PerpId] = write = new RetainedWrite();
            var key = write.KeyFor("/perps/" + p.PerpId + "/close", "{}");
            _closing.Add(p.PerpId);
            _confirmClose = null;
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
            _body.Add(MoneyUi.Small(MoneyUi.Age(_historyGate.Age(Now))));
            if (_historyError != null) _body.Add(MoneyUi.Notice(MoneyStrings.Err(_historyError), "error"));
            if (_history == null)
            {
                if (_historyError == null) _body.Add(MoneyUi.Small(MoneyStrings.T("common.loading")));
                return;
            }
            var rows = _history.Items.Where(p => p.Status != "open").OrderByDescending(p => p.ClosedAtMs ?? 0).ToList();
            if (_history.Skipped > 0) _body.Add(MoneyUi.Small(MoneyStrings.F("common.skipped", _history.Skipped)));
            if (rows.Count == 0) _body.Add(MoneyUi.Text(MoneyStrings.T("perps.historyEmpty"), "ct-body ct-muted"));
            foreach (var p in rows) _body.Add(PerpCard(p, closable: false));
        }

        private void RenderRekt()
        {
            _body.Add(MoneyUi.Small(MoneyStrings.T("perps.rektIntro")));
            _body.Add(MoneyUi.Row(MoneyUi.Small(MoneyUi.Age(_rektGate.Age(Now))), El.Div("ct-grow"),
                MoneyUi.Btn(MoneyStrings.T("common.refresh"), () => { _rektGate.Kick(); }, "ct-btn ct-btn-ghost")));
            if (_rektError != null) _body.Add(MoneyUi.Notice(MoneyStrings.Err(_rektError), "error"));
            if (_tombstones == null)
            {
                if (_rektError == null) _body.Add(MoneyUi.Small(MoneyStrings.T("common.loading")));
                return;
            }
            if (_tombstones.Skipped > 0) _body.Add(MoneyUi.Small(MoneyStrings.F("common.skipped", _tombstones.Skipped)));
            if (_tombstones.Items.Count == 0) _body.Add(MoneyUi.Text(MoneyStrings.T("perps.rektEmpty"), "ct-body ct-muted"));
            foreach (var t in _tombstones.Items)
            {
                var ticker = !string.IsNullOrEmpty(t.Ticker) ? t.Ticker : Hub.Market.Ticker(t.CoinId) ?? "?";
                var when = t.AtMs.HasValue ? MoneyFormat.Ago((ServerClock.NowMs - t.AtMs.Value) / 1000.0) : "";
                _body.Add(El.Div("ct-lb-row",
                    El.Div("ct-col ct-grow",
                        MoneyUi.Text(MoneyStrings.F("perps.rektRow", t.Alias, MoneyFormat.Cash(t.LossCents), "$" + ticker), "ct-money-title"),
                        MoneyUi.Small(when + (string.IsNullOrEmpty(t.Reason) ? "" : " · " + t.Reason))),
                    MoneyUi.Text("-" + MoneyFormat.Cash(t.LossCents), "ct-money-value ct-money-down")));
            }
        }

        // ------------------------------------------------------------------ fetches

        private void FetchOpen()
        {
            _openGate.Start();
            Run(ct => Hub.Api.Perps("open", ct), rows =>
            {
                _openGate.Succeeded(Now);
                _open = rows;
                _openError = null;
                foreach (var p in rows.Items) Hub.Market.Remember(p.CoinId, p.Ticker);
                if (_tabs.Current == "open") Render();
            }, err =>
            {
                _openGate.Failed(Now);
                _openError = err;
                if (_tabs.Current == "open") Render();
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
                if (_tabs.Current == "history") Render();
            }, err =>
            {
                _historyGate.Failed(Now);
                _historyError = err;
                if (_tabs.Current == "history") Render();
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
                if (_tabs.Current == "rekt") Render();
            }, err =>
            {
                _rektGate.Failed(Now);
                _rektError = err;
                if (_tabs.Current == "rekt") Render();
            }, write: false);
        }

        private string TickerOf(PerpRow p) => !string.IsNullOrEmpty(p.Ticker) ? p.Ticker : Hub.Market.Ticker(p.CoinId) ?? MoneyFormat.Short(p.CoinId, 4, 0).TrimEnd('…');
    }
}
