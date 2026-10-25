using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;
using Motion = CTLife.UI.Kit.Motion;

namespace CTLife.CityLife.Casino
{
    /// <summary>
    /// Moon or Rug (crash, `POST /casino/crash/bet` and `/crash/cashout`): a rocket rides the multiplier line m(t) = e^(g t),
    /// drawn from the round's `params.startedAtMs` and `growthPerSecond` on the server clock. TAKE PROFIT sends the cash-out;
    /// the reply decides (the server's clock alone): `outcome.crashed` plays the rug pull (the rug yanked from under the rocket,
    /// the rocket tumbling, a red flash), a win shows where it was taken and the line flies on, dimmed, to where it would have
    /// rugged. With auto take-profit the round settles at the bet and the client plays the flight it was given. The client
    /// never knows the crash point of an open round; it polls the round and, failing that, settles at the 100x ceiling.
    /// </summary>
    public sealed class CrashPanel : RugPanel
    {
        private enum Mode { Idle, Launching, Flying, Cashing, Ghost, Rugged }
        private static readonly double[] Presets = { 1.5, 2, 3, 5, 10 };

        private readonly Chart _chart;
        private readonly VisualElement _chartWrap, _rocket, _rug, _flash, _multBox, _history;
        private readonly Label _mult, _multSub;
        private readonly StakeChips _chips;
        private readonly Button _cta, _toggle;
        private readonly Label _ctaLabel, _ctaSub, _autoValue;
        private readonly List<Button> _presetBtns = new List<Button>();
        private readonly List<double> _hist = new List<double>();
        private readonly List<Label> _yLabels = new List<Label>();

        private Mode _mode = Mode.Idle;
        private CasinoRound _round;           // the round in play
        private CasinoBet _settled;           // the settling reply (cash-out or auto bet)
        private bool _auto;
        private double _autoAt = 2.0;
        private string _betKey, _cashKey;
        private double _localStart;           // auto rounds: local flight start (server-clock ms)
        private double _g = 0.07;
        private double _t, _shownM = 1, _crashT = -1, _tookAt = -1;
        private float _rugAt = -1, _pollAt, _clock;
        private bool _polling, _historyLoaded, _resumeChecked;

        public override bool Busy => _mode == Mode.Launching || _mode == Mode.Flying || _mode == Mode.Cashing;

        public CrashPanel(RugRoomSheet sheet) : base(sheet)
        {
            name = "rr-crash";
            _chartWrap = El.Div("rr-chart-wrap");
            _chart = new Chart(this);
            _chart.AddToClassList("rr-chart");
            _chartWrap.Add(_chart);
            for (int i = 0; i < 6; i++)
            {
                var l = El.Text("", "rr-hist-label");
                l.style.position = Position.Absolute;
                l.pickingMode = PickingMode.Ignore;
                l.style.opacity = 0.5f;
                _yLabels.Add(l);
                _chartWrap.Add(l);
            }
            _history = El.Div("rr-history");
            _chartWrap.Add(_history);
            _rug = El.Div("rr-rug");
            var rt = RugRoomSheet.Art("UI/Casino/crash_rug");
            if (rt != null) _rug.style.backgroundImage = new StyleBackground(rt);
            _chartWrap.Add(_rug);
            _rocket = El.Div("rr-rocket");
            _rocket.name = "rr-rocket";
            var rk = RugRoomSheet.Art("UI/Casino/crash_rocket");
            if (rk != null) _rocket.style.backgroundImage = new StyleBackground(rk);
            _chartWrap.Add(_rocket);
            _mult = El.Text("1.00×", "rr-mult-label");
            _mult.name = "rr-mult";
            _multSub = El.Text(CasinoText.T("crash.hint"), "rr-mult-sub");
            _multBox = El.Div("rr-mult", _mult, _multSub);
            _multBox.pickingMode = PickingMode.Ignore;
            _chartWrap.Add(_multBox);
            _flash = El.Div("rr-flash");
            _flash.pickingMode = PickingMode.Ignore;
            _chartWrap.Add(_flash);
            Add(_chartWrap);

            var side = El.Div("rr-side");
            var card = El.Div("rr-card");
            card.Add(El.Text(CasinoText.T("crash.title"), "rr-h"));
            card.Add(El.Text(CasinoText.T("crash.hint"), "rr-note"));
            card.style.marginBottom = 18;
            side.Add(card);
            var bet = El.Div("rr-card");
            _chips = new StakeChips(sheet);
            _chips.Changed += Refresh;
            bet.Add(_chips);
            // auto take-profit
            _toggle = RugRoomSheet.Btn("", () => { _auto = !_auto; Cues.Select(); Refresh(); }, "rr-toggle");
            _toggle.name = "rr-auto-toggle";
            _toggle.Add(El.Div("rr-toggle-knob"));
            _autoValue = El.Text("", "rr-auto-value");
            var minus = RugRoomSheet.Btn("", () => StepAuto(-1), "rr-step"); minus.Add(El.Text("−", "rr-step-label"));
            var plus = RugRoomSheet.Btn("", () => StepAuto(+1), "rr-step"); plus.Add(El.Text("+", "rr-step-label"));
            bet.Add(El.Div("rr-auto", _toggle, El.Text(CasinoText.T("crash.auto"), "rr-note").With("rr-grow"), minus, _autoValue, plus));
            var presets = El.Div("rr-presets");
            foreach (var p in Presets)
            {
                double v = p;
                var b = RugRoomSheet.Btn("", () => { _autoAt = v; _auto = true; Cues.Select(); Refresh(); }, "rr-preset");
                b.Add(El.Text(CasinoText.X(v), "rr-preset-label"));
                _presetBtns.Add(b);
                presets.Add(b);
            }
            bet.Add(presets);
            _cta = RugRoomSheet.Btn("", Cta, "rr-cta");
            _cta.name = "rr-crash-cta";
            _ctaLabel = El.Text(CasinoText.T("crash.launch"), "rr-cta-label");
            _ctaSub = El.Text("", "rr-cta-sub");
            _cta.Add(_ctaLabel); _cta.Add(_ctaSub);
            foreach (var e in _cta.Children()) e.pickingMode = PickingMode.Ignore;
            bet.Add(_cta);
            side.Add(bet);
            Add(side);
            Refresh();
        }

        // ------------------------------------------------------------------ controls

        private void StepAuto(int dir)
        {
            double step = _autoAt < 3 || (_autoAt <= 3 && dir < 0) ? 0.1 : _autoAt < 10 || (_autoAt <= 10 && dir < 0) ? 0.5 : 1;
            double min = State?.MinAutoCashout ?? 1.01, max = State?.MaxMultiplier ?? 100;
            _autoAt = Math.Round(Math.Max(min, Math.Min(max, _autoAt + dir * step)), 2);
            _auto = true;
            Cues.Hop(dir > 0 ? 2 : 0);
            Refresh();
        }

        private void Refresh()
        {
            _toggle.EnableInClassList("rr-toggle--on", _auto);
            _autoValue.text = CasinoText.X(_autoAt);
            for (int i = 0; i < Presets.Length; i++) _presetBtns[i].EnableInClassList("rr-preset--on", _auto && Math.Abs(Presets[i] - _autoAt) < 0.001);
            bool flying = _mode == Mode.Flying || _mode == Mode.Cashing;
            _cta.EnableInClassList("rr-cta--gold", flying);
            _cta.EnableInClassList("rr-cta--busy", _mode == Mode.Launching || _mode == Mode.Cashing);
            bool autoFlight = flying && _round != null && _round.Settled;
            _cta.EnableInClassList("rr-cta--busy", _mode == Mode.Launching || _mode == Mode.Cashing || autoFlight);
            if (autoFlight) _ctaLabel.text = CasinoText.F("crash.autoAt", CasinoText.X(_round.AutoCashout ?? _autoAt)).ToUpperInvariant();
            else if (flying) _ctaLabel.text = CasinoText.T("crash.take");
            else if (_mode == Mode.Launching) _ctaLabel.text = CasinoText.T("crash.waiting");
            else _ctaLabel.text = CasinoText.T("crash.launch");
            if (!flying) _ctaSub.text = CasinoText.Money(Sheet.StakeCents) + (_auto ? "  ·  " + CasinoText.F("crash.autoAt", CasinoText.X(_autoAt)) : "");
        }

        public override void StateChanged()
        {
            _chips.Refresh();
            if (State != null) _g = State.GrowthPerSecond;
            Refresh();
        }

        public override void Shown()
        {
            if (!_historyLoaded || !_resumeChecked) LoadHistoryAndResume();
        }

        private async void LoadHistoryAndResume()
        {
            try
            {
                var rounds = await Svc.Api.Rounds(20, Svc.Life);
                _hist.Clear();
                CasinoRound open = null;
                foreach (var r in rounds)
                {
                    if (r.Game != CasinoGames.Crash) continue;
                    if (r.Open) { open ??= r; continue; }
                    if (r.Outcome != null && _hist.Count < 8) _hist.Add(r.CrashPoint);
                }
                _hist.Reverse();
                _historyLoaded = true; _resumeChecked = true;
                DrawHistory();
                if (open != null && _mode == Mode.Idle)
                {
                    // the app went away mid-flight: the rocket is still up (or already rugged; the next poll says)
                    Log.Info("casino", "crash resume " + open.RoundId);
                    _round = open; _g = open.GrowthPerSecond; _crashT = -1; _tookAt = -1;
                    _mode = Mode.Flying;
                    Sheet.Toast(CasinoText.T("crash.resume"), good: true);
                    ResetVisuals();
                    Refresh();
                }
            }
            catch (Exception e) { Log.Warn("casino", "crash history failed: " + CityErrors.From(e, false).Code); }
        }

        private void DrawHistory()
        {
            _history.Clear();
            foreach (var h in _hist)
            {
                var chip = El.Div("rr-hist" + (h >= 2 ? " rr-hist--good" : ""), El.Text(CasinoText.X(h), "rr-hist-label"));
                _history.Add(chip);
            }
        }

        private void PushHistory(double crash)
        {
            _hist.Add(crash);
            while (_hist.Count > 8) _hist.RemoveAt(0);
            DrawHistory();
        }

        private void Cta()
        {
            if (_mode == Mode.Flying) { CashOut(); return; }
            if (_mode == Mode.Launching || _mode == Mode.Cashing) return;
            Launch();
        }

        // ------------------------------------------------------------------ launch

        private async void Launch()
        {
            var s = State;
            if (s == null || !s.AgeConfirmed || s.Excluded(ServerClock.NowMs)) { Cues.Nope(); return; }
            long stake = Sheet.StakeCents;
            if (stake > s.StakeRoomCents) { Sheet.Toast(CasinoText.T(s.StakeRemainingCents < stake ? "err.STAKE_CAP_REACHED" : "err.LOSS_CAP_REACHED")); Cues.Nope(); return; }
            _mode = Mode.Launching; _settled = null; _round = null; _crashT = -1; _tookAt = -1; _rugAt = -1;
            ResetVisuals();
            Refresh();
            _betKey ??= Guid.NewGuid().ToString();
            double? auto = _auto ? Math.Round(_autoAt, 2) : (double?)null;
            Cues.Play("sfx_chips", 0.7f);
            Log.Info("casino", $"crash bet {stake} auto {auto?.ToString("0.00") ?? "-"}");
            try
            {
                var bet = await Svc.Api.CrashBet(stake, auto, _betKey, Svc.Life);
                _betKey = null;
                Svc.Apply(bet);
                _round = bet.Round;
                _g = _round.GrowthPerSecond;
                if (_round.Settled)
                {
                    // auto take-profit: settled at the bet; play the flight it was given from now
                    _settled = bet;
                    _localStart = ServerClock.NowMs;
                    _crashT = CasinoMath.SecondsTo(_round.CrashPoint, _g);
                    _tookAt = _round.CashedOutAt.HasValue && !_round.Crashed ? _round.CashedOutAt.Value : -1;
                    Log.Info("casino", $"crash auto {_round.RoundId} crash {_round.CrashPoint:0.00} took {_tookAt:0.00}");
                }
                else
                {
                    // the stake is out now; an open round reveals nothing, so Cash can follow at once
                    Svc.AfterBet(bet, bet.CashCents);
                    Log.Info("casino", $"crash open {_round.RoundId} startedAt {_round.StartedAtMs}");
                }
                _mode = Mode.Flying;
                _pollAt = _clock + 1f;
                Cues.Play("sfx_slot_lever", 0.6f);
                DeviceFeel.Confirm();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                var err = CityErrors.From(e, true);
                if (!err.OutcomeUnknown) _betKey = null;
                _mode = Mode.Idle;
                if (err.Is("ROUND_IN_PROGRESS")) { _resumeChecked = false; LoadHistoryAndResume(); }
                Sheet.Fail(err);
            }
            Refresh();
        }

        // ------------------------------------------------------------------ take profit

        private async void CashOut()
        {
            if (_round == null || !_round.Open) return;
            _mode = Mode.Cashing;
            Refresh();
            _cashKey ??= Guid.NewGuid().ToString();
            DeviceFeel.Tick();
            try
            {
                var bet = await Svc.Api.CrashCashout(_round.RoundId, _cashKey, Svc.Life);
                _cashKey = null;
                Settle(bet);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                var err = CityErrors.From(e, true);
                if (err.Is("ROUND_SETTLED"))
                {
                    _cashKey = null;
                    _ = PollRound(force: true);
                    return;
                }
                if (!err.OutcomeUnknown) _cashKey = null;
                _mode = Mode.Flying;
                Sheet.Fail(err);
                Refresh();
            }
        }

        /// <summary>The server settled the open round (cash-out reply or a poll): trust it.</summary>
        private void Settle(CasinoBet bet)
        {
            _settled = bet;
            _round = bet.Round;
            _crashT = CasinoMath.SecondsTo(_round.CrashPoint, _g);
            if (_round.Crashed || (_round.PayoutCents ?? 0) == 0)
            {
                _tookAt = -1;
                Log.Info("casino", $"crash rugged {_round.RoundId} at {_round.CrashPoint:0.00}");
                if (_t > _crashT) _t = _crashT;
                if (_mode == Mode.Cashing) Sheet.Toast(CasinoText.T("crash.late"));
                Rug(primary: true);
            }
            else
            {
                _tookAt = _round.CashedOutAt ?? _shownM;
                Log.Info("casino", $"crash took {_round.RoundId} at {_tookAt:0.00} payout {_round.PayoutCents} crash {_round.CrashPoint:0.00}");
                Took();
            }
        }

        private void Took()
        {
            _mode = Mode.Ghost;
            Svc.AfterBet(_settled, _settled.CashCents);
            _multBox.AddToClassList("rr-mult--took");
            _mult.text = CasinoText.X(_tookAt);
            _multSub.text = CasinoText.F("crash.took", CasinoText.X(_tookAt)) + "  ·  +" + CasinoText.Money(_round.PayoutCents ?? 0);
            Cues.Play("sfx_cashout_ok");
            DeviceFeel.Confirm();
            Motion.Pop(_multBox, 1.12f, 0.3f);
            Refresh();
        }

        private void Rug(bool primary)
        {
            _mode = Mode.Rugged;
            _rugAt = _clock;
            _chart.Rugged = true;
            if (primary)
            {
                Svc.AfterBet(_settled, _settled.CashCents);
                _multBox.AddToClassList("rr-mult--rug");
                _mult.text = CasinoText.T("crash.rugged");
                _multSub.text = CasinoText.F("crash.ruggedAt", CasinoText.X(_round.CrashPoint)) + "  ·  −" + CasinoText.Money(_round.StakeCents);
                _flash.style.opacity = 0.55f;
                Tween.Run(_flash, 0.5f, k => _flash.style.opacity = 0.55f * (1 - k), Ease.OutCubic, skipWhenReduced: false);
                Motion.Shake(_chartWrap, 26f, 0.4f);
                DeviceFeel.Reject();
            }
            else
            {
                _multSub.text = CasinoText.F("crash.took", CasinoText.X(_tookAt)) + "  ·  " + CasinoText.F("crash.wouldRug", CasinoText.X(_round.CrashPoint));
            }
            Cues.Play("sfx_rug_pull", primary ? 1f : 0.6f);
            PushHistory(_round.CrashPoint);
            Refresh();
        }

        private async System.Threading.Tasks.Task PollRound(bool force = false)
        {
            if (_polling || _round == null || !_round.Open) return;
            _polling = true;
            try
            {
                var r = await Svc.Api.Round(_round.RoundId, Svc.Life);
                if (r.Settled && (_mode == Mode.Flying || _mode == Mode.Cashing) && _round != null && r.RoundId == _round.RoundId)
                    Settle(new CasinoBet { Round = r, CashCents = -1, Disclaimer = Svc.Disclaimer });
            }
            catch (Exception e) { if (force) Log.Warn("casino", "crash poll failed: " + CityErrors.From(e, false).Code); }
            finally { _polling = false; }
        }

        // ------------------------------------------------------------------ per frame

        public override void Tick(float dt)
        {
            _clock += dt;
            double now = ServerClock.NowMs;
            switch (_mode)
            {
                case Mode.Flying:
                case Mode.Cashing:
                {
                    if (_round == null) break;
                    if (_settled != null && _round.Settled)
                    {
                        // auto round: local flight to the decisive moment
                        _t = Math.Max(0, (now - _localStart) / 1000.0);
                        if (_tookAt > 0 && CasinoMath.CrashMultiplier(_t, _g) >= _tookAt) { _t = CasinoMath.SecondsTo(_tookAt, _g); Took(); }
                        else if (_tookAt <= 0 && _t >= _crashT) { _t = _crashT; Rug(primary: true); }
                    }
                    else
                    {
                        _t = Math.Max(0, (now - _round.StartedAtMs) / 1000.0);
                        if (_clock >= _pollAt) { _pollAt = _clock + 1f; _ = PollRound(); }
                        // nothing can fly past the ceiling: settle it (the reply says rugged)
                        double max = State?.MaxMultiplier ?? 100;
                        if (_mode == Mode.Flying && CasinoMath.CrashMultiplier(_t, _g) >= max + 0.5) CashOut();
                    }
                    break;
                }
                case Mode.Ghost:
                {
                    // after taking profit the line flies on, dimmed, to where it would have rugged
                    double start = _settled != null && _round.AutoCashout.HasValue ? _localStart : _round.StartedAtMs;
                    _t = Math.Max(0, (now - start) / 1000.0);
                    if (_t >= _crashT) { _t = _crashT; Rug(primary: false); }
                    break;
                }
            }
            double m = CasinoMath.CrashMultiplier(_t, _g);
            _shownM = CasinoMath.Floor2(m);
            if (_mode == Mode.Flying || _mode == Mode.Cashing)
            {
                _mult.text = CasinoText.X(_shownM);
                long value = CasinoMath.Payout(_round?.StakeCents ?? Sheet.StakeCents, _shownM);
                _ctaSub.text = _round != null && _round.Settled ? CasinoText.T("crash.autoSet") : CasinoText.F("crash.takeAt", CasinoText.Money(value));
                _multSub.text = _round != null && _round.Settled ? CasinoText.F("crash.autoAt", CasinoText.X(_round.AutoCashout ?? _autoAt)) : "";
            }
            else if (_mode == Mode.Ghost) _mult.text = CasinoText.X(_tookAt);
            _chart.Set(_t, _g, _mode == Mode.Ghost || (_mode == Mode.Rugged && _tookAt > 0), _tookAt > 0 ? CasinoMath.SecondsTo(_tookAt, _g) : -1);
            PlaceRocket(dt);
        }

        private void ResetVisuals()
        {
            _t = 0; _shownM = 1;
            _chart.Rugged = false;
            _multBox.RemoveFromClassList("rr-mult--rug"); _multBox.RemoveFromClassList("rr-mult--took");
            _mult.text = "1.00×"; _multSub.text = "";
            _rocket.style.opacity = 1f; _rocket.style.rotate = new Rotate(0);
            _rug.style.opacity = 0f;
        }

        private float _rockY, _rockRot;
        private void PlaceRocket(float dt)
        {
            var (tip, angle) = _chart.Tip();
            if (float.IsNaN(tip.x)) return;
            float rot = 45f - angle;
            if (_mode == Mode.Rugged && _rugAt >= 0 && _tookAt <= 0)
            {
                // the rug is yanked out from under the rocket; the rocket tumbles down
                float e = _clock - _rugAt;
                float slide = Ease.OutCubic(Mathf.Clamp01(e / 0.35f));
                float yank = Mathf.Clamp01((e - 0.35f) / 0.3f);
                _rug.style.opacity = e < 0.9f ? 1f : Mathf.Clamp01(1f - (e - 0.9f) / 0.4f);
                _rug.style.left = Mathf.Lerp(-560f, tip.x - 300f, slide) + 900f * Ease.InCubic(yank);
                _rug.style.top = tip.y - 160f;
                _rug.style.rotate = new Rotate(-12f + 30f * yank);
                float fall = Mathf.Clamp01((e - 0.4f) / 0.9f);
                _rockY = 900f * Ease.InCubic(fall);
                _rockRot = rot + 220f * Ease.InCubic(fall);
                _rocket.style.left = tip.x - 75f + 120f * fall;
                _rocket.style.top = tip.y - 75f + _rockY;
                _rocket.style.rotate = new Rotate(_rockRot);
                _rocket.style.opacity = 1f - Mathf.Clamp01((e - 1.0f) / 0.4f);
                return;
            }
            if (_mode == Mode.Rugged && _tookAt > 0)
            {
                float e = _clock - _rugAt;
                _rocket.style.opacity = Mathf.Clamp01(0.45f - e);
            }
            else if (_mode == Mode.Ghost) _rocket.style.opacity = 0.45f;
            else _rocket.style.opacity = 1f;
            float wob = (_mode == Mode.Flying || _mode == Mode.Cashing) ? Mathf.Sin(_clock * 23f) * 1.5f : 0f;
            _rocket.style.left = tip.x - 75f;
            _rocket.style.top = tip.y - 75f + wob;
            _rocket.style.rotate = new Rotate(rot);
        }

        public override void Disposed() { }

        // ------------------------------------------------------------------ the chart

        private sealed class Chart : VisualElement
        {
            private const float L = 110f, R = 70f, T = 230f, B = 70f;
            private readonly CrashPanel _p;
            private double _t, _g = 0.07, _tookT = -1;
            private bool _ghost;
            public bool Rugged;

            public Chart(CrashPanel p) { _p = p; pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }

            private bool _lastRugged;

            public void Set(double t, double g, bool ghost, double tookT)
            {
                // repaint and re-lay the labels only when the line moved (an idle chart costs nothing per frame)
                if (t == _t && g == _g && ghost == _ghost && tookT == _tookT && Rugged == _lastRugged && _t > 0) return;
                bool idleSame = t == 0 && _t == 0 && Rugged == _lastRugged && _laidOut;
                _t = t; _g = g; _ghost = ghost; _tookT = tookT; _lastRugged = Rugged;
                if (idleSame) return;
                MarkDirtyRepaint();
                LayoutLabels();
            }
            private bool _laidOut;

            private double XMax => Math.Max(8.0, _t * 1.18);
            private double YMax => Math.Max(2.0, CasinoMath.CrashMultiplier(_t, _g) * 1.18);

            private Vector2 P(double t, double m)
            {
                var r = contentRect;
                float x = L + (float)(t / XMax) * (r.width - L - R);
                float y = (r.height - B) - (float)((m - 1.0) / (YMax - 1.0)) * (r.height - B - T);
                return new Vector2(x, y);
            }

            public (Vector2 tip, float angleDeg) Tip()
            {
                var r = contentRect;
                if (r.width <= 0 || float.IsNaN(r.width)) return (new Vector2(float.NaN, float.NaN), 0);
                var a = P(_t, CasinoMath.CrashMultiplier(_t, _g));
                var b = P(Math.Max(0, _t - 0.25), CasinoMath.CrashMultiplier(Math.Max(0, _t - 0.25), _g));
                var d = a - b;
                float ang = d.sqrMagnitude < 0.01f ? 20f : Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;
                return (a, ang);   // the chart fills the wrap, so its frame is the rocket's
            }

            private static double Nice(double range, int lines)
            {
                double raw = range / lines;
                foreach (var s in new[] { 0.1, 0.2, 0.25, 0.5, 1, 2, 5, 10, 20, 25, 50 }) if (s >= raw) return s;
                return 100;
            }

            private void LayoutLabels()
            {
                var r = contentRect;
                if (r.width <= 0 || float.IsNaN(r.width)) return;
                double step = Nice(YMax - 1.0, 4);
                int i = 0;
                for (double m = 1.0; m <= YMax && i < _p._yLabels.Count; m += step, i++)
                {
                    var l = _p._yLabels[i];
                    var pt = P(0, m);
                    l.style.left = 24; l.style.top = pt.y - 12;
                    l.text = CasinoText.X(m);
                    l.style.display = DisplayStyle.Flex;
                }
                for (; i < _p._yLabels.Count; i++) _p._yLabels[i].style.display = DisplayStyle.None;
                _laidOut = true;
            }

            private void Draw(MeshGenerationContext ctx)
            {
                var r = contentRect;
                if (r.width <= 0 || float.IsNaN(r.width)) return;
                var p = ctx.painter2D;
                // grid
                p.lineWidth = 1.5f;
                p.strokeColor = new Color(1f, 0.9f, 0.7f, 0.06f);
                double ystep = Nice(YMax - 1.0, 4);
                for (double m = 1.0; m <= YMax; m += ystep)
                {
                    var a = P(0, m); p.BeginPath(); p.MoveTo(a); p.LineTo(new Vector2(r.width - R + 20, a.y)); p.Stroke();
                }
                double xstep = Nice(XMax, 6);
                for (double t = 0; t <= XMax; t += xstep)
                {
                    var a = P(t, 1); p.BeginPath(); p.MoveTo(a); p.LineTo(new Vector2(a.x, T - 30)); p.Stroke();
                }
                if (_t <= 0.0001) return;
                // the line: an area under it, then a glow and the stroke
                const int N = 90;
                var pts = new Vector2[N + 1];
                for (int i = 0; i <= N; i++) { double t = _t * i / N; pts[i] = P(t, CasinoMath.CrashMultiplier(t, _g)); }
                Color line = Rugged && _tookT < 0 ? new Color(1f, 0.30f, 0.43f) : new Color(0.24f, 0.86f, 0.52f);
                p.fillColor = new Color(line.r, line.g, line.b, 0.10f);
                p.BeginPath();
                p.MoveTo(P(0, 1));
                foreach (var v in pts) p.LineTo(v);
                p.LineTo(new Vector2(pts[N].x, P(0, 1).y));
                p.ClosePath();
                p.Fill();
                p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;
                foreach (var (w, a) in new[] { (22f, 0.07f), (12f, 0.16f), (6f, 1f) })
                {
                    p.lineWidth = w;
                    if (_tookT > 0)
                    {
                        // solid up to where profit was taken, dimmed after
                        int cut = Mathf.Clamp((int)Math.Round(N * _tookT / Math.Max(0.0001, _t)), 1, N);
                        p.strokeColor = new Color(0.96f, 0.79f, 0.36f, a);
                        p.BeginPath(); p.MoveTo(pts[0]); for (int i = 1; i <= cut; i++) p.LineTo(pts[i]); p.Stroke();
                        p.strokeColor = new Color(1f, 1f, 1f, a * 0.25f);
                        p.BeginPath(); p.MoveTo(pts[cut]); for (int i = cut + 1; i <= N; i++) p.LineTo(pts[i]); p.Stroke();
                    }
                    else
                    {
                        p.strokeColor = new Color(line.r, line.g, line.b, a);
                        p.BeginPath(); p.MoveTo(pts[0]); for (int i = 1; i <= N; i++) p.LineTo(pts[i]); p.Stroke();
                    }
                }
                if (_tookT > 0)
                {
                    var tp = P(_tookT, CasinoMath.CrashMultiplier(_tookT, _g));
                    p.fillColor = new Color(0.96f, 0.79f, 0.36f, 1f);
                    p.BeginPath(); p.Arc(tp, 11f, 0f, 360f); p.Fill();
                }
            }
        }
    }
}
