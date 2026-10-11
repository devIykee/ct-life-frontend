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

    /// <summary>Five needs bars projected between server reads, "hits zero in" hints, and the needs actions.</summary>
    internal sealed class NeedsPanel
    {
        public readonly VisualElement Root;
        private readonly CityLifeHub _hub;
        private readonly NeedsActionRunner _runner;
        private readonly VisualElement[] _fills = new VisualElement[5];
        private readonly Label[] _values = new Label[5];
        private readonly Label _hint, _status;
        private readonly List<(string id, Button button)> _buttons = new List<(string, Button)>();
        private readonly Func<bool> _alive;
        private readonly CancellationToken _life;

        public NeedsPanel(CityLifeHub hub, Func<bool> alive, CancellationToken life)
        {
            _hub = hub;
            _alive = alive;
            _life = life;
            _runner = new NeedsActionRunner(hub);
            Root = CityUi.Card(CityUi.H2(CityLifeStrings.T("needs.title")));
            for (int i = 0; i < 5; i++)
            {
                var bar = CityUi.Bar(out _fills[i]);
                _values[i] = CityUi.Text("-", "ct-small");
                var label = CityUi.Text(CityLifeStrings.NeedName(NeedsConfig.Ids[i]), "ct-small");
                label.style.width = 80;
                _values[i].style.width = 36;
                Root.Add(CityUi.Row(label, bar, _values[i]));
            }
            _hint = CityUi.Small("");
            Root.Add(_hint);
            var cfg = hub.Needs.Config;
            if (cfg == null) Root.Add(CityUi.Error(CityLifeStrings.T("needs.unavailable")));
            else
            {
                var row = CityUi.Row();
                foreach (var a in cfg.Actions)
                {
                    var id = a.Id;
                    var label = CityLifeStrings.ActionLabel(id) + (a.CostCents > 0 ? " (" + Money.Cash(a.CostCents) + ")" : "");
                    var b = CityUi.Btn(label, () => Perform(id), "ct-btn");
                    b.name = "citylife-needs-" + id;
                    _buttons.Add((id, b));
                    row.Add(b);
                }
                Root.Add(row);
            }
            _status = CityUi.Small("");
            Root.Add(_status);
            Root.Add(CityUi.Small(CityLifeStrings.T("needs.hint")));
            Refresh();
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
            if (cfg == null || p == null) { _hint.text = ""; return; }
            for (int i = 0; i < 5; i++)
            {
                var level = cfg.LevelOf(p[i]);
                CityUi.SetBar(_fills[i], (p[i] - cfg.Min) / (cfg.Max - cfg.Min), level);
                _values[i].text = ((int)Math.Floor(p[i])).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
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
                b.EnableInClassList("ct-app-locked", club);
                b.SetEnabled(!_runner.Busy);
            }
        }

        public NeedsActionRunner Runner => _runner;
    }
}
