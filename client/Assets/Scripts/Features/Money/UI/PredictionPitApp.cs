using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using FeatureFlags = CTLife.Core.Features;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>Prediction Pit (feature #19): open markets from GET /predictions?status=open and Cash bets on an outcome.</summary>
    public sealed class PredictionPitApp : MoneyApp
    {
        public override string Id => "predict";
        public override string Title => MoneyStrings.T("predict.title");
        public override string Color => "#3867d6";
        public override int Phase => 2;
        public override string Feature => FeatureFlags.Markets;

        internal override MoneyView CreateView(MoneyHub hub) => new PredictionView(hub);
    }

    internal sealed class PredictionView : MoneyView
    {
        private readonly VisualElement _list = El.Div("ct-col"), _mine = El.Div("ct-col");
        private readonly Label _cash = MoneyUi.Small(""), _age = MoneyUi.Small("");
        private readonly PollGate _gate = new PollGate(15, 60);
        private readonly Dictionary<string, Label> _countdowns = new Dictionary<string, Label>();
        private readonly Dictionary<string, RetainedWrite> _writes = new Dictionary<string, RetainedWrite>();
        private Rows<PredictionRow> _markets;
        private MoneyError _error;
        private string _selectedMarket, _selectedOutcome, _stakeInput = "", _betting;
        private VisualElement _betStatus;

        public PredictionView(MoneyHub hub) : base(hub)
        {
            Root.Add(MoneyUi.Row(MoneyUi.Text(MoneyStrings.T("predict.title"), "ct-h1"), El.Div("ct-grow"), _cash));
            Root.Add(MoneyUi.Row(_age, El.Div("ct-grow"), MoneyUi.Btn(MoneyStrings.T("common.refresh"), () => _gate.Kick(), "ct-btn ct-btn-ghost")));
            Root.Add(_list);
            Root.Add(_mine);
            Subs.On<ProfileChangedSignal>(_ => RenderCash());
            Subs.On<DegradedChangedSignal>(_ => Render());
            RenderCash();
            Render();
            RenderMine();
        }

        private static long Cash => Services.TryGet<PlayerProfile>(out var p) ? p.CashCents : 0;
        private static bool MarketsOn => FeatureFlags.IsAvailable(FeatureFlags.Markets);
        private void RenderCash() => _cash.text = MoneyStrings.F("common.cash", MoneyFormat.Cash(Cash));

        protected override void Tick()
        {
            double now = Now;
            _age.text = MoneyUi.Age(_gate.Age(now));
            if (_gate.Due(now) && _betting == null) Fetch();
            if (_markets == null) return;
            foreach (var m in _markets.Items)
                if (m.ClosesAtMs.HasValue && _countdowns.TryGetValue(m.Id, out var l))
                {
                    var left = (m.ClosesAtMs.Value - ServerClock.NowMs) / 1000.0;
                    l.text = left > 0 ? MoneyStrings.F("predict.closes", MoneyFormat.Countdown(left)) : MoneyStrings.T("predict.closed");
                }
        }

        private void Fetch()
        {
            _gate.Start();
            Run(ct => Hub.Api.Predictions(ct), rows =>
            {
                _gate.Succeeded(Now);
                _markets = rows;
                _error = null;
                Render();
            }, err =>
            {
                _gate.Failed(Now);
                _error = err;
                Render();
            }, write: false);
        }

        private void Render()
        {
            if (!Alive) return;
            _list.Clear();
            _countdowns.Clear();
            _betStatus = null;
            if (!MarketsOn) _list.Add(MoneyUi.Notice(MoneyStrings.T("common.marketsOff"), "warn"));
            if (_error != null) _list.Add(MoneyUi.Notice(MoneyStrings.Err(_error), "error"));
            if (_markets == null)
            {
                if (_error == null) _list.Add(MoneyUi.Small(MoneyStrings.T("common.loading")));
                return;
            }
            if (_markets.Skipped > 0) _list.Add(MoneyUi.Small(MoneyStrings.F("common.skipped", _markets.Skipped)));
            var open = _markets.Items.Where(m => m.Status == "open").ToList();
            if (open.Count == 0) _list.Add(MoneyUi.Text(MoneyStrings.T("predict.empty"), "ct-body ct-muted"));
            foreach (var m in open)
            {
                var market = m;
                var countdown = MoneyUi.Small("");
                _countdowns[m.Id] = countdown;
                var outcomes = El.Div("ct-app-tabs ct-money-tabs");
                foreach (var o in m.Outcomes)
                {
                    var outcome = o;
                    outcomes.Add(MoneyUi.Chip(o.Label, () => { _selectedMarket = market.Id; _selectedOutcome = outcome.Id; Render(); },
                        _selectedMarket == m.Id && _selectedOutcome == o.Id));
                }
                var card = MoneyUi.Card(MoneyUi.Text(m.Question, "ct-h2"), countdown, outcomes);
                if (_selectedMarket == m.Id && _selectedOutcome != null)
                {
                    var field = MoneyUi.Field(MoneyStrings.T("predict.stake"), _stakeInput, v => _stakeInput = v);
                    card.Add(field);
                    var bet = MoneyUi.Btn(MoneyStrings.T(_betting == m.Id ? "predict.placing" : "predict.bet"), () => PlaceBet(market));
                    bet.SetEnabled(_betting == null && MarketsOn);
                    card.Add(bet);
                    _betStatus = El.Div("ct-col");
                    card.Add(_betStatus);
                }
                _list.Add(card);
            }
            Tick();
        }

        private void PlaceBet(PredictionRow market)
        {
            if (_betting != null || !MarketsOn) return;
            var ticket = Tickets.Bet(market.Id, _selectedOutcome, _stakeInput, Cash);
            if (!ticket.Ok)
            {
                _betStatus?.Clear();
                _betStatus?.Add(MoneyUi.Notice(MoneyStrings.F(ticket.ErrorKey, ticket.Args), "error"));
                return;
            }
            if (!_writes.TryGetValue(market.Id, out var write)) _writes[market.Id] = write = new RetainedWrite();
            var body = ticket.Body;
            var key = write.KeyFor("/predictions/" + market.Id + "/bets", RetainedWrite.Body(body));
            var label = market.Outcomes.FirstOrDefault(o => o.Id == _selectedOutcome)?.Label ?? _selectedOutcome;
            _betting = market.Id;
            Render();
            Run(ct => Hub.Api.Bet(market.Id, body, key, ct), receipt =>
            {
                write.Complete();
                _betting = null;
                Hub.Bets.Insert(0, receipt);
                Bus.Publish(new SfxSignal("sfx_buy"));
                Hub.RefreshProfile();
                _stakeInput = "";
                Render();
                _betStatus?.Add(MoneyUi.Notice(MoneyStrings.F("predict.placed", MoneyFormat.Cash(receipt.StakeCents), label)));
                RenderMine();
            }, err =>
            {
                if (!err.OutcomeUnknown) write.Complete();
                _betting = null;
                Render();
                _betStatus?.Add(MoneyUi.Notice(MoneyStrings.Err(err), "error"));
            }, write: true);
        }

        private void RenderMine()
        {
            _mine.Clear();
            if (Hub.Bets.Count == 0) return;
            _mine.Add(MoneyUi.Text(MoneyStrings.T("predict.yours"), "ct-h2"));
            foreach (var b in Hub.Bets.Take(20))
                _mine.Add(MoneyUi.Pair(b.OutcomeId ?? "?", MoneyFormat.Cash(b.StakeCents)));
        }
    }
}
