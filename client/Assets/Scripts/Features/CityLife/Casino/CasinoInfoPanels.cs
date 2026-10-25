using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife.Casino
{
    /// <summary>
    /// Fairness (`GET /casino/rounds?mine`, docs/CASINO.md "Fairness: commit and reveal"): how the commit-reveal works in four
    /// steps, and your rounds; open one to see the seed hash published before the round and the seed revealed after, and verify
    /// it on the phone: SHA-256(seed) against the hash, then the outcome recomputed from the seed (<see cref="CasinoMath.Verify"/>).
    /// </summary>
    public sealed class FairnessPanel : RugPanel
    {
        private readonly ScrollView _rounds;
        private string _expanded;
        private List<CasinoRound> _list = new List<CasinoRound>();

        public FairnessPanel(RugRoomSheet sheet) : base(sheet)
        {
            name = "rr-fair";
            var steps = El.Div("rr-card rr-fair-steps");
            steps.Add(El.Text(CasinoText.T("fair.title"), "rr-h"));
            steps.Add(Cap(CasinoText.T("fair.how").ToUpperInvariant()));
            for (int i = 1; i <= 4; i++)
                steps.Add(El.Div("rr-fair-step", El.Div("rr-fair-n", El.Text(i.ToString(), "rr-fair-n-label")), El.Text(CasinoText.T("fair.step" + i), "rr-note")));
            Add(steps);
            var right = El.Div("rr-card rr-rounds");
            right.Add(Cap(CasinoText.T("fair.rounds")));
            _rounds = new ScrollView(ScrollViewMode.Vertical);
            _rounds.name = "rr-rounds";
            _rounds.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _rounds.style.flexGrow = 1;
            right.Add(_rounds);
            Add(right);
        }

        public override void Shown() => Load();

        private async void Load()
        {
            try
            {
                _list = await Svc.Api.Rounds(30, Svc.Life);
                Render();
            }
            catch (Exception e)
            {
                _rounds.Clear();
                _rounds.Add(El.Text(CasinoText.Error(CityErrors.From(e, false)), "rr-note"));
            }
        }

        private void Render()
        {
            _rounds.Clear();
            if (_list.Count == 0) { _rounds.Add(El.Text(CasinoText.T("fair.none"), "rr-note")); return; }
            foreach (var r in _list) _rounds.Add(Row(r));
        }

        private VisualElement Row(CasinoRound r)
        {
            var row = El.Div("rr-round");
            row.name = "rr-round-" + r.RoundId;
            var when = r.CreatedAtMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(r.CreatedAtMs).ToLocalTime().ToString("HH:mm:ss") : "";
            var net = El.Text(r.Open ? "…" : (r.NetCents >= 0 ? "+" : "−") + CasinoText.Money(Math.Abs(r.NetCents)), "rr-round-net");
            net.EnableInClassList("rr-round-net--up", r.NetCents > 0);
            var head = RugRoomSheet.Btn("", () => { _expanded = _expanded == r.RoundId ? null : r.RoundId; Render(); }, "rr-row");
            head.name = "rr-roundhead-" + r.RoundId;
            head.Add(El.Text(CasinoText.GameName(r.Game) + "  ·  " + CasinoText.Money(r.StakeCents) + "  ·  " + Describe(r) + "  ·  " + when, "rr-round-title").With("rr-grow"));
            head.Add(net);
            foreach (var e in head.Children()) e.pickingMode = PickingMode.Ignore;
            row.Add(head);
            if (_expanded == r.RoundId) row.Add(Detail(r));
            return row;
        }

        private static string Describe(CasinoRound r)
        {
            if (r.Open || r.Outcome == null) return CasinoText.T("fair.open");
            switch (r.Game)
            {
                case CasinoGames.Slots: return string.Join(" ", r.Reels ?? new string[0]).Replace('_', ' ') + (r.Multiplier > 0 ? "  " + r.Multiplier.ToString("0.##") + "×" : "");
                case CasinoGames.Crash: return r.Crashed || r.CashedOutAt == null ? "rugged " + CasinoText.X(r.CrashPoint) : "took " + CasinoText.X(r.CashedOutAt.Value) + " / rug " + CasinoText.X(r.CrashPoint);
                default: return CasinoText.Side(r.Result) + (r.Won ? " · won" : "");
            }
        }

        private VisualElement Detail(CasinoRound r)
        {
            var d = El.Div("rr-round-detail");
            d.Add(Cap(CasinoText.T("fair.hash")));
            d.Add(El.Text(r.SeedHash, "rr-mono"));
            d.Add(Cap(CasinoText.T("fair.seed")));
            d.Add(El.Text(string.IsNullOrEmpty(r.Seed) ? CasinoText.T("fair.open") : r.Seed, "rr-mono"));
            d.Add(Cap(CasinoText.T("fair.outcome")));
            d.Add(El.Text(r.Outcome?.ToString(Newtonsoft.Json.Formatting.None) ?? "-", "rr-mono"));
            var verdict = El.Text("", "rr-verdict");
            verdict.name = "rr-verdict";
            var verify = RugRoomSheet.Plain(CasinoText.T("fair.verify"), () =>
            {
                var s = State;
                var v = CasinoMath.Verify(r, s?.ReelWeights ?? new Dictionary<string, int>(), 0.95, s?.MaxMultiplier ?? 100);
                verdict.EnableInClassList("rr-verdict--bad", !v.Ok);
                verdict.text = v.Ok ? CasinoText.F("fair.verified", v.Recomputed) : CasinoText.F("fair.failed", v.HashOk ? "ok" : "mismatch", v.OutcomeOk ? "ok" : "mismatch (" + v.Recomputed + ")");
                Log.Info("casino", $"verify {r.RoundId}: hash {v.HashOk} outcome {v.OutcomeOk} ({v.Recomputed})");
                if (v.Ok) { Cues.Play("sfx_cashout_ok", 0.6f); DeviceFeel.Confirm(); } else Cues.Nope();
            }, "mint");
            verify.name = "rr-verify";
            verify.SetEnabled(!string.IsNullOrEmpty(r.Seed));
            var copy = RugRoomSheet.Plain(CasinoText.T("fair.copy"), () => CityUi.Copy("seedHash " + r.SeedHash + "\nseed " + r.Seed + "\nround " + r.RoundId), "");
            var actions = El.Div("rr-row", verify, copy);
            copy.style.marginLeft = 12;
            d.Add(actions);
            d.Add(verdict);
            return d;
        }
    }

    /// <summary>
    /// Limits and breaks: today's stake and net loss against the caps with the reset countdown, this session's minutes, and the
    /// self-exclusion (`POST /casino/self-exclude {days}`, 1 to 365): pick the days, then a hold-to-confirm, because it can be
    /// extended but never shortened or lifted early.
    /// </summary>
    public sealed class LimitsPanel : RugPanel
    {
        private static readonly int[] Days = { 1, 3, 7, 30, 90, 180, 365 };
        private readonly VisualElement _stakeFill, _lossFill;
        private readonly Label _stakeVal, _lossVal, _resets, _session, _status;
        private readonly List<(int d, Button b)> _dayBtns = new List<(int, Button)>();
        private readonly Button _exclude;
        private int _days = 7;
        private string _key;

        public LimitsPanel(RugRoomSheet sheet) : base(sheet)
        {
            name = "rr-limits";
            var left = El.Div("rr-card");
            left.style.width = 900; left.style.marginRight = 28;
            left.Add(El.Text(CasinoText.T("lim.title"), "rr-h"));
            left.Add(Cap(CasinoText.T("lim.stake").ToUpperInvariant()));
            _stakeVal = El.Text("", "rr-meter-value");
            left.Add(_stakeVal);
            _stakeFill = El.Div("rr-meter-fill");
            left.Add(El.Div("rr-meter", _stakeFill));
            left.Add(Cap(CasinoText.T("lim.loss").ToUpperInvariant()));
            _lossVal = El.Text("", "rr-meter-value");
            left.Add(_lossVal);
            _lossFill = El.Div("rr-meter-fill");
            left.Add(El.Div("rr-meter", _lossFill));
            _resets = El.Text("", "rr-note");
            _session = El.Text("", "rr-note");
            left.Add(_resets); left.Add(_session);
            Add(left);

            var right = El.Div("rr-card rr-grow");
            right.Add(El.Text(CasinoText.T("lim.exclude"), "rr-h"));
            right.Add(El.Text(CasinoText.T("lim.excludeBody"), "rr-note"));
            var days = El.Div("rr-days");
            foreach (var d in Days)
            {
                int v = d;
                var b = RugRoomSheet.Btn("", () => { _days = v; Cues.Select(); Refresh(); }, "rr-day");
                b.name = "rr-day-" + d;
                b.Add(El.Text(d == 1 ? CasinoText.T("lim.day") : CasinoText.F("lim.days", d), "rr-day-label"));
                _dayBtns.Add((d, b));
                days.Add(b);
            }
            right.Add(days);
            var minus = RugRoomSheet.Btn("", () => { _days = Math.Max(1, _days - 1); Refresh(); }, "rr-step"); minus.Add(El.Text("−", "rr-step-label"));
            var plus = RugRoomSheet.Btn("", () => { _days = Math.Min(365, _days + 1); Refresh(); }, "rr-step"); plus.Add(El.Text("+", "rr-step-label"));
            var val = El.Text("", "rr-auto-value");
            val.name = "rr-days-value";
            val.style.width = 220;
            right.Add(El.Div("rr-auto", minus, val, plus));
            _exclude = RugRoomSheet.Btn("", Confirm, "rr-cta rr-cta--red");
            _exclude.name = "rr-exclude";
            _exclude.Add(El.Text("", "rr-cta-label"));
            foreach (var e in _exclude.Children()) e.pickingMode = PickingMode.Ignore;
            right.Add(_exclude);
            _status = El.Text("", "rr-note");
            _status.style.marginTop = 12;
            right.Add(_status);
            Add(right);
            Refresh();
        }

        public override void Shown() { _ = Svc.RefreshState(); Refresh(); Notes(); }
        public override void StateChanged() => Refresh();

        public override void Tick(float dt)
        {
            if (Time.frameCount % 30 == 0) Notes();
        }

        private void Notes()
        {
            var s = State;
            if (s != null) _resets.text = CasinoText.F("lim.resets", CasinoText.Countdown((s.ResetsAtMs - ServerClock.NowMs) / 1000.0));
            _session.text = CasinoText.F("lim.session", Svc.SessionMinutes);
        }

        private void Refresh()
        {
            var s = State;
            foreach (var (d, b) in _dayBtns) b.EnableInClassList("rr-day--on", d == _days);
            var val = this.Q<Label>("rr-days-value");
            if (val != null) val.text = _days == 1 ? CasinoText.T("lim.day") : CasinoText.F("lim.days", _days);
            var lbl = _exclude.Q<Label>(className: "rr-cta-label");
            lbl.text = CasinoText.F("lim.excludeBtn", _days == 1 ? CasinoText.T("lim.day") : CasinoText.F("lim.days", _days));
            lbl.style.fontSize = 40;
            if (s == null) return;
            _stakeVal.text = CasinoText.F("lim.of", CasinoText.Money(s.StakedCents), CasinoText.Money(s.DailyStakeCapCents));
            _lossVal.text = CasinoText.F("lim.of", CasinoText.Money(Math.Max(0, s.NetLossCents)), CasinoText.Money(s.DailyNetLossCapCents));
            float sf = s.DailyStakeCapCents > 0 ? Mathf.Clamp01(s.StakedCents / (float)s.DailyStakeCapCents) : 0;
            float lf = s.DailyNetLossCapCents > 0 ? Mathf.Clamp01(Math.Max(0, s.NetLossCents) / (float)s.DailyNetLossCapCents) : 0;
            _stakeFill.style.width = Length.Percent(sf * 100); _stakeFill.EnableInClassList("rr-meter-fill--hot", sf > 0.85f);
            _lossFill.style.width = Length.Percent(lf * 100); _lossFill.EnableInClassList("rr-meter-fill--hot", lf > 0.85f);
            _status.text = s.Excluded(ServerClock.NowMs) ? CasinoText.F("lim.done", CasinoText.Date(s.SelfExcludedUntilMs.Value)) : "";
        }

        private void Confirm()
        {
            long until = (long)ServerClock.NowMs + _days * 86400000L;
            var s = State;
            if (s != null && s.SelfExcludedUntilMs.HasValue && s.SelfExcludedUntilMs.Value > until) until = s.SelfExcludedUntilMs.Value;
            var card = El.Div("rr-modal-card");
            card.name = "rr-exclude-confirm";
            card.Add(El.Text(CasinoText.F("lim.confirmTitle", CasinoText.Date(until)), "rr-modal-title"));
            card.Add(El.Text(CasinoText.T("lim.confirmBody"), "rr-modal-body"));
            var fill = El.Div("rr-hold-fill");
            var hold = El.Div("rr-hold", fill, El.Text(CasinoText.T("lim.confirm"), "rr-hold-label"));
            hold.name = "rr-hold";
            VisualElement modal = null;
            float down = -1f; bool fired = false;
            hold.RegisterCallback<PointerDownEvent>(_ => { down = Time.unscaledTime; DeviceFeel.Tick(); });
            hold.RegisterCallback<PointerUpEvent>(_ => { if (!fired) { down = -1f; fill.style.width = Length.Percent(0); } });
            hold.RegisterCallback<PointerLeaveEvent>(_ => { if (!fired) { down = -1f; fill.style.width = Length.Percent(0); } });
            hold.schedule.Execute(() =>
            {
                if (down < 0 || fired) return;
                float k = Mathf.Clamp01((Time.unscaledTime - down) / 1.4f);
                fill.style.width = Length.Percent(k * 100);
                if (k >= 1f) { fired = true; Exclude(modal); }
            }).Every(16);
            card.Add(hold);
            card.Add(El.Div("rr-modal-actions", RugRoomSheet.Plain(CasinoText.T("lim.keep"), () => Sheet.CloseModal(modal), "")));
            modal = Sheet.ShowModal(card);
        }

        private async void Exclude(VisualElement modal)
        {
            _key ??= Guid.NewGuid().ToString();
            Log.Info("casino", "self-exclude " + _days + " days");
            try
            {
                var until = await Svc.Api.SelfExclude(_days, _key, Svc.Life);
                _key = null;
                Sheet.CloseModal(modal);
                DeviceFeel.Confirm();
                Sheet.Toast(CasinoText.F("lim.done", until.HasValue ? CasinoText.Date(until.Value) : ""), good: true);
                await Svc.RefreshState();
            }
            catch (Exception e)
            {
                var err = CityErrors.From(e, true);
                if (!err.OutcomeUnknown) _key = null;
                Sheet.CloseModal(modal);
                Sheet.Fail(err);
            }
        }
    }
}
