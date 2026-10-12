using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using FeatureFlags = CTLife.Core.Features;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>Personal prediction history from the backend. New bets require a server market catalog and resolver.</summary>
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
        private readonly VisualElement _list = El.Div("ct-col");
        private readonly Label _cash = MoneyUi.Small(""), _age = MoneyUi.Small("");
        private readonly PollGate _gate = new PollGate(15, 60);
        private readonly Dictionary<string, Label> _countdowns = new Dictionary<string, Label>();
        private Rows<BetReceipt> _history;
        private MoneyError _error;

        public PredictionView(MoneyHub hub) : base(hub)
        {
            Root.Add(MoneyUi.Row(MoneyUi.Text(MoneyStrings.T("predict.title"), "ct-h1"), El.Div("ct-grow"), _cash));
            Root.Add(MoneyUi.Row(_age, El.Div("ct-grow"), MoneyUi.Btn(MoneyStrings.T("common.refresh"), () => _gate.Kick(), "ct-btn ct-btn-ghost")));
            Root.Add(MoneyUi.Notice(MoneyStrings.T("predict.unavailable"), "warn"));
            Root.Add(_list);
            Subs.On<ProfileChangedSignal>(_ => RenderCash());
            RenderCash();
            Render();
        }

        private void RenderCash() => _cash.text = MoneyStrings.F("common.cash", MoneyFormat.Cash(
            Services.TryGet<PlayerProfile>(out var p) ? p.CashCents : 0));

        protected override void Tick()
        {
            _age.text = MoneyUi.Age(_gate.Age(Now));
            if (_gate.Due(Now)) Fetch();
            if (_history == null) return;
            foreach (var bet in _history.Items)
                if (bet.Status == "open" && bet.ClosesAtMs.HasValue && _countdowns.TryGetValue(bet.PredictionId, out var label))
                {
                    var left = (bet.ClosesAtMs.Value - ServerClock.NowMs) / 1000.0;
                    label.text = left > 0 ? MoneyStrings.F("predict.closes", MoneyFormat.Countdown(left)) : MoneyStrings.T("predict.closed");
                }
        }

        private void Fetch()
        {
            _gate.Start();
            Run(ct => Hub.Api.PredictionHistory(ct), rows =>
            {
                var previouslyOpen = new HashSet<string>(_history?.Items.Where(b => b.Status == "open").Select(b => b.PredictionId)
                    ?? Enumerable.Empty<string>());
                if (rows.Items.Any(b => b.Status != "open" && previouslyOpen.Contains(b.PredictionId))) Hub.RefreshProfile();
                _gate.Succeeded(Now);
                _history = rows;
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
            if (_error != null) _list.Add(MoneyUi.Notice(MoneyStrings.Err(_error), "error"));
            if (_history == null)
            {
                if (_error == null) _list.Add(MoneyUi.Small(MoneyStrings.T("common.loading")));
                return;
            }
            _list.Add(MoneyUi.Text(MoneyStrings.T("predict.yours"), "ct-h2"));
            if (_history.Skipped > 0) _list.Add(MoneyUi.Small(MoneyStrings.F("common.skipped", _history.Skipped)));
            if (_history.Items.Count == 0 && _history.Skipped == 0)
                _list.Add(MoneyUi.Small(MoneyStrings.T("predict.noHistory")));
            foreach (var bet in _history.Items)
            {
                var card = MoneyUi.Card(
                    MoneyUi.Pair(bet.OutcomeId, MoneyFormat.Cash(bet.StakeCents)),
                    MoneyUi.Small(MoneyStrings.T("predict.status." + bet.Status) + (string.IsNullOrEmpty(bet.Horizon) ? "" : " · " + bet.Horizon)));
                if (bet.Status == "open" && bet.ClosesAtMs.HasValue)
                {
                    var countdown = MoneyUi.Small("");
                    _countdowns[bet.PredictionId] = countdown;
                    card.Add(countdown);
                }
                _list.Add(card);
            }
        }
    }
}
