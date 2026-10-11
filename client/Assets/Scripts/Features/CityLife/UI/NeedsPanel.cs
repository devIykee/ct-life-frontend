using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.World;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// Runs one needs action against the server (`POST /needs/actions`) with a retained Idempotency-Key, applies the
    /// server's needs and Cash to the profile, and reconciles with GET /me when the outcome is unknown. One request at a
    /// time per runner. Shared by the Home app and the furniture sheet.
    /// </summary>
    internal sealed class NeedsActionRunner
    {
        private readonly CityLifeHub _hub;
        private readonly Dictionary<string, RetainedWriteFlow> _flows = new Dictionary<string, RetainedWriteFlow>(StringComparer.Ordinal);
        public bool Busy { get; private set; }

        public NeedsActionRunner(CityLifeHub hub) { _hub = hub; }

        public static string CurrentVenueId()
        {
            var v = VenueSession.Current;
            if (v == null) return null;
            return v.IsOwnHome ? "home" : v.VenueId;
        }

        public ActionGateResult Gate(string actionId) => NeedsActionGate.Check(_hub.Needs.Config, actionId, CurrentVenueId());

        public string GateText(string actionId)
        {
            switch (Gate(actionId))
            {
                case ActionGateResult.ClubOnly:
                    var reg = Services.Get<ConfigStore>().Registries;
                    var names = new List<string>();
                    foreach (var id in NeedsActionGate.ClubsFor(_hub.Needs.Config, actionId)) names.Add(reg.Venue(id)?.name ?? CityLifeStrings.Readable(id));
                    return CityLifeStrings.F("needs.clubOnly", string.Join(", ", names));
                case ActionGateResult.Allowed: return null;
                default: return CityLifeStrings.T("needs.unavailable");
            }
        }

        /// <summary>Returns a status message for the UI (null while nothing to say).</summary>
        public async Task<string> Run(string actionId, CancellationToken ct)
        {
            if (Busy || _hub.Disposed) return null;
            if (!_flows.TryGetValue(actionId, out var flow)) _flows[actionId] = flow = new RetainedWriteFlow();
            var key = flow.Begin("POST", "/needs/actions", actionId);
            if (key == null) return null;
            Busy = true;
            try
            {
                var (needs, cash) = await _hub.Api.NeedsAction(actionId, key, ct);
                flow.Succeeded();
                var profile = _hub.Profile;
                profile?.SetNeeds(needs);
                profile?.SetCash(cash);
                Bus.Publish(new SfxSignal("sfx_ui_success"));
                return CityLifeStrings.T("needs.done");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                flow.Failed(new CityError { Code = "CANCELLED", Key = "needs.unconfirmed", OutcomeUnknown = true });
                _hub.RefreshProfile(force: true);
                return CityLifeStrings.T("needs.unconfirmed");
            }
            catch (Exception e)
            {
                var err = CityErrors.From(e, true);
                flow.Failed(err);
                if (err.OutcomeUnknown) { _hub.RefreshProfile(force: true); return CityLifeStrings.T("needs.unconfirmed"); }
                Bus.Publish(new SfxSignal("sfx_ui_error"));
                return err.Message;
            }
            finally { Busy = false; }
        }
    }

    /// <summary>
    /// The Home app's needs, Health-app style (spec "Home (needs)"): a summary at the top built from the five projected needs
    /// (how many are fine, a mini column per need, the most urgent hint), then each need as a row with a small meter, its value
    /// and an action pill. Values are projected between server reads with the server's decay rates; actions go to the server.
    /// </summary>
    internal sealed class NeedsPanel
    {
        private static readonly string[] Icons = { "energy", "counter", "emote", "profile", "needs" };
        private static readonly string[] Tints = { "orange", "green", "pink", "teal", "purple" };

        public readonly VisualElement Root;
        private readonly CityLifeHub _hub;
        private readonly NeedsActionRunner _runner;
        private readonly VisualElement[] _fills = new VisualElement[5], _columns = new VisualElement[5];
        private readonly Label[] _values = new Label[5], _columnValues = new Label[5];
        private readonly Label _summary, _hint, _status;
        private readonly List<(string id, Button button)> _buttons = new List<(string, Button)>();
        private readonly Func<bool> _alive;
        private readonly CancellationToken _life;
        private DuoSheet _sheet;

        public NeedsPanel(CityLifeHub hub, Func<bool> alive, CancellationToken life)
        {
            _hub = hub;
            _alive = alive;
            _life = life;
            _runner = new NeedsActionRunner(hub);
            Root = El.Div("duo-needs");
            Root.name = "citylife-needs";

            // Summary (Health "Summary" card): count of needs that are fine, five mini columns, the most urgent line.
            var summary = El.Div("duo-group duo-group-padded duo-needs-summary");
            summary.name = "citylife-needs-summary";
            summary.Add(El.Text(CityLifeStrings.T("needs.title").ToUpperInvariant(), "duo-needs-kicker"));
            _summary = CityUi.Text("—", "duo-needs-headline");
            summary.Add(_summary);
            var columns = El.Div("duo-needs-columns");
            for (int i = 0; i < 5; i++)
            {
                _columns[i] = El.Div("duo-needs-column-fill");
                _columnValues[i] = El.Text("—", "duo-needs-column-value");
                columns.Add(El.Div("duo-needs-column duo-needs-" + NeedsConfig.Ids[i],
                    _columnValues[i], El.Div("duo-needs-column-track", _columns[i]),
                    El.Text(CityLifeStrings.NeedName(NeedsConfig.Ids[i]), "duo-needs-column-name")));
            }
            summary.Add(columns);
            _hint = CityUi.Text("", "duo-needs-hint");
            summary.Add(_hint);
            Root.Add(summary);

            var cfg = hub.Needs.Config;
            var list = El.Div("duo-group duo-needs-list");
            for (int i = 0; i < 5; i++)
            {
                var need = NeedsConfig.Ids[i];
                var bar = CityUi.Bar(out _fills[i]);
                var row = new DuoRow(() => NeedSheet(need)).Leading(Duo.IconSquare(Icons[i], Tints[i])).Title(CityLifeStrings.NeedName(need), "duo-row-title-strong");
                row.name = "citylife-need-" + need;
                row.TextColumn.Add(bar);
                _values[i] = El.Text("—", "duo-needs-value");
                row.Trailing(_values[i]);
                var action = cfg?.Actions.Find(a => a.Raises == need);
                if (action != null)
                {
                    var id = action.Id;
                    var b = Duo.Pill(CityLifeStrings.ActionLabel(id), () => Perform(id));
                    b.name = "citylife-needs-" + id;
                    b.tooltip = ActionLine(action);
                    _buttons.Add((id, b));
                    row.Trailing(b);
                }
                list.Add(row);
            }
            Duo.MarkLast(list);
            Root.Add(list);
            if (cfg == null) Root.Add(Duo.Notice(CityLifeStrings.T("needs.unavailable"), Duo.Tone.Warning));
            _status = CityUi.Text("", "duo-group-footer duo-needs-status");
            Root.Add(_status);
            Root.Add(Duo.GroupFooter(CityLifeStrings.T("needs.hint")));
            Refresh();
        }

        private static string ActionLine(NeedsAction a) =>
            CityLifeStrings.ActionLabel(a.Id) + (a.CostCents > 0 ? " · " + Money.Cash(a.CostCents) : "") + (a.Amount > 0 ? " · +" + a.Amount : "");

        /// <summary>Every action that raises one need, with cost and where it works, in a sheet.</summary>
        private void NeedSheet(string need)
        {
            var cfg = _hub.Needs.Config;
            if (cfg == null || Root.panel == null) return;
            _sheet?.Close();
            int i = Array.IndexOf(NeedsConfig.Ids, need);
            var p = _hub.Needs.Projected(_hub.NowMs);
            string value = p != null && i >= 0 ? ((int)Math.Floor(p[i])).ToString(System.Globalization.CultureInfo.InvariantCulture) : "—";
            var sheet = new DuoSheet(CityLifeStrings.NeedName(need), CityLifeStrings.F("needs.sheetValue", value, cfg.Max));
            sheet.name = "citylife-need-sheet";
            if (p != null && i >= 0 && cfg.LevelOf(p[i]) != NeedLevel.Ok) sheet.AddContent(Duo.Notice(CityLifeStrings.T("needs.low." + need), Duo.Tone.Warning));
            var actions = cfg.Actions.FindAll(a => a.Raises == need);
            if (actions.Count == 0) sheet.AddContent(CityUi.Small(CityLifeStrings.T("needs.noAction")));
            var rows = El.Div("duo-list");
            foreach (var a in actions)
            {
                var id = a.Id;
                var gate = _runner.GateText(id);
                var row = new DuoRow().Title(CityLifeStrings.ActionLabel(id), "duo-row-title-strong")
                    .Subtitle((a.CostCents > 0 ? Money.Cash(a.CostCents) : CityLifeStrings.T("needs.free")) + (a.Amount > 0 ? " · +" + a.Amount : "") + (gate != null ? "\n" + gate : ""), twoLines: true);
                row.AddToClassList("duo-row-static");
                var b = Duo.Pill(CityLifeStrings.T("needs.do"), () => { sheet.Close(); Perform(id); }, Duo.PillKind.Primary);
                b.name = "citylife-need-sheet-" + id;
                b.SetEnabled(!_runner.Busy && gate == null);
                row.Trailing(b);
                rows.Add(row);
            }
            Duo.MarkLast(rows);
            sheet.AddContent(rows);
            sheet.AddSecondary(CityLifeStrings.T("common.close"));
            _sheet = sheet;
            sheet.Scrolling().Show(Root);
        }

        private async void Perform(string id)
        {
            if (!_alive()) return;
            var gate = _runner.GateText(id);
            if (gate != null) { _status.text = gate; return; }
            _status.text = CityLifeStrings.T("needs.working");
            SetEnabled(false);
            string msg;
            try { msg = await _runner.Run(id, _life); }
            catch (Exception e) { Log.Exception("citylife", e); msg = CityLifeStrings.T("err.generic"); }
            if (!_alive()) return;
            if (msg != null) _status.text = msg;
            SetEnabled(true);
            Refresh();
        }

        private void SetEnabled(bool on)
        {
            foreach (var (id, b) in _buttons) b.SetEnabled(on && !_runner.Busy);
        }

        public void Refresh()
        {
            var cfg = _hub.Needs.Config;
            var p = _hub.Needs.Projected(_hub.NowMs);
            if (cfg == null || p == null) { _hint.text = ""; _summary.text = CityLifeStrings.T("needs.noReading"); return; }
            int fine = 0;
            for (int i = 0; i < 5; i++)
            {
                var level = cfg.LevelOf(p[i]);
                if (level == NeedLevel.Ok) fine++;
                double fraction = (p[i] - cfg.Min) / (cfg.Max - cfg.Min);
                CityUi.SetBar(_fills[i], fraction, level);
                string v = ((int)Math.Floor(p[i])).ToString(System.Globalization.CultureInfo.InvariantCulture);
                _values[i].text = v;
                _columnValues[i].text = v;
                _columns[i].style.height = Length.Percent((float)(Math.Max(0, Math.Min(1, fraction)) * 100));
                var column = _columns[i].parent?.parent;
                if (column != null)
                {
                    column.EnableInClassList("duo-needs-ok", level == NeedLevel.Ok);
                    column.EnableInClassList("duo-needs-low", level == NeedLevel.Low);
                    column.EnableInClassList("duo-needs-critical", level != NeedLevel.Ok && level != NeedLevel.Low);
                }
                _values[i].EnableInClassList("duo-needs-value-low", level != NeedLevel.Ok);
            }
            _summary.text = fine == 5 ? CityLifeStrings.T("needs.summaryAll") : CityLifeStrings.F("needs.summary", fine);
            int u = NeedsProjection.MostUrgent(p, cfg);
            var need = NeedsConfig.Ids[u];
            var level0 = cfg.LevelOf(p[u]);
            if (level0 == NeedLevel.Ok)
            {
                double hours = NeedsProjection.HoursUntil(p[u], cfg.Rate(need), cfg.Min);
                _hint.text = double.IsInfinity(hours) ? CityLifeStrings.T("needs.allGood")
                    : CityLifeStrings.F("needs.emptyIn", CityLifeStrings.NeedName(need), WeekClock.Countdown(hours * 3_600_000.0));
            }
            else _hint.text = CityLifeStrings.T("needs.low." + need);
            foreach (var (id, b) in _buttons)
            {
                bool club = _runner.Gate(id) == ActionGateResult.ClubOnly;
                b.EnableInClassList("duo-pill-locked", club);
                b.SetEnabled(!_runner.Busy);
            }
        }

        public void Close() => _sheet?.Close();

        public NeedsActionRunner Runner => _runner;
    }
}
