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
        public override string HomeLabel => MoneyStrings.T("predict.homeLabel");
        public override string Color => "#3867d6";
        public override int Phase => 2;
        public override string Feature => FeatureFlags.Markets;
        internal override MoneyView CreateView(MoneyHub hub) => new PredictionView(hub);
    }

    /// <summary>
    /// Duo layout (spec "Predict", Polymarket-like rows): a hero for what is in play, then each bet as a question row with the
    /// side taken, stake and a status tag; details open in a sheet. The backend serves no market catalog, odds or volume, so
    /// no Yes/No percentages or new-bet sheet are shown (New bets stay unavailable, as before).
    /// </summary>
    internal sealed class PredictionView : MoneyView
    {
        private readonly DuoPage _page;
        private readonly VisualElement _list = El.Div("duo-predict-body");
        private readonly Label _age = MoneyUi.Small("");
        private readonly PollGate _gate = new PollGate(15, 60);
        private readonly Dictionary<string, Label> _countdowns = new Dictionary<string, Label>();
        private Rows<BetReceipt> _history;
        private MoneyError _error;

        public PredictionView(MoneyHub hub) : base(hub)
        {
            _page = Framed(MoneyStrings.T("predict.title"), "predict-page");
            _page.AddToClassList("duo-predict");
            _page.Body.Add(_list);
            _page.Body.Add(_age);
            _age.AddToClassList("duo-footnote");
            Subs.On<ProfileChangedSignal>(_ => Render());
            Render();
        }

        private static string Question(string outcomeId) => MoneyStrings.PredictionQuestion(outcomeId);

        protected override void Tick()
        {
            _age.text = MoneyUi.Age(_gate.Age(Now));
            if (_gate.Due(Now)) Fetch();
            if (_history == null) return;
            foreach (var bet in _history.Items)
                if (bet.Status == "open" && bet.ClosesAtMs.HasValue && _countdowns.TryGetValue(bet.PredictionId, out var label))
                    label.text = CloseLine(bet);
        }

        private static string CloseLine(BetReceipt bet)
        {
            if (!bet.ClosesAtMs.HasValue) return "";
            var left = (bet.ClosesAtMs.Value - ServerClock.NowMs) / 1000.0;
            return left > 0 ? MoneyStrings.F("predict.closes", MoneyFormat.Countdown(left)) : MoneyStrings.T("predict.closed");
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

        private static string Stamp(long ms) =>
            System.DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("ddd d MMM, HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        private static string Tone(string status) => status == "won" ? "green" : status == "lost" ? "red" : status == "open" ? "blue" : "gray";

        private void Render()
        {
            if (!Alive) return;
            _list.Clear();
            _countdowns.Clear();
            if (_error != null) _list.Add(Duo.Notice(MoneyStrings.Err(_error), Duo.Tone.Danger));
            if (_history == null)
            {
                if (_error == null) _list.Add(MoneyUi.Small(MoneyStrings.T("common.loading")));
                return;
            }
            var open = _history.Items.Where(b => b.Status == "open").ToList();
            long inPlay = open.Sum(b => b.StakeCents);
            var hero = Duo.Hero(MoneyStrings.T("predict.inPlay"), MoneyFormat.Cash(inPlay), null, null, MoneyStrings.F("predict.openCount", open.Count));
            hero.name = "predict-hero";
            _list.Add(hero);
            _list.Add(Duo.Notice(MoneyStrings.T("predict.unavailable"), Duo.Tone.Warning));
            var header = Duo.SectionHeader(MoneyStrings.T("predict.yours"), MoneyStrings.T("common.refresh"), () => _gate.Kick());
            header.name = "predict-header";
            _list.Add(header);
            if (_history.Skipped > 0) _list.Add(MoneyUi.Small(MoneyStrings.F("common.skipped", _history.Skipped)));
            if (_history.Items.Count == 0 && _history.Skipped == 0) { _list.Add(Duo.Empty(MoneyStrings.T("predict.noHistory"))); return; }
            var list = El.Div("duo-list");
            foreach (var bet in _history.Items)
            {
                var b = bet;
                var row = new DuoRow(() => Details(b)).Leading(El.PassThrough(El.Div("duo-app-tile duo-tint-indigo", new UiIcon("predict"))))
                    .Title(Question(bet.OutcomeId), "duo-row-title-strong")
                    .Subtitle(MoneyStrings.F("predict.rowLine", MoneyFormat.Cash(bet.StakeCents), string.IsNullOrEmpty(bet.Horizon) ? "—" : bet.Horizon));
                row.name = "predict-bet-" + bet.PredictionId;
                if (bet.Status == "open" && bet.ClosesAtMs.HasValue)
                {
                    var countdown = El.Text(CloseLine(bet), "duo-row-subtitle");
                    _countdowns[bet.PredictionId] = countdown;
                    row.TextColumn.Add(countdown);
                }
                row.Trailing(El.Text(MoneyStrings.T("predict.status." + bet.Status), "duo-tag duo-tag-" + Tone(bet.Status)));
                row.Chevron();
                list.Add(row);
            }
            Duo.MarkLast(list);
            _list.Add(list);
        }

        private void Details(BetReceipt bet)
        {
            var sheet = new DuoSheet(Question(bet.OutcomeId), MoneyStrings.T("predict.status." + bet.Status));
            sheet.name = "predict-bet-sheet";
            sheet.AddContent(MoneyUi.Pair(MoneyStrings.T("predict.stakeRow"), MoneyFormat.Cash(bet.StakeCents)));
            sheet.AddContent(MoneyUi.Pair(MoneyStrings.T("predict.outcomeRow"), bet.OutcomeId));
            if (!string.IsNullOrEmpty(bet.Horizon)) sheet.AddContent(MoneyUi.Pair(MoneyStrings.T("predict.horizonRow"), bet.Horizon));
            if (bet.ClosesAtMs.HasValue) sheet.AddContent(MoneyUi.Pair(MoneyStrings.T("predict.closesRow"), Stamp(bet.ClosesAtMs.Value)));
            if (bet.CreatedAtMs.HasValue) sheet.AddContent(MoneyUi.Pair(MoneyStrings.T("predict.placedRow"), Stamp(bet.CreatedAtMs.Value)));
            sheet.AddSecondary(MoneyStrings.T("common.done"));
            ShowSheet(sheet);
        }
    }
}
