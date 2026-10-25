using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.UI.Kit;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife.RektBurger
{
    /// <summary>
    /// The approved Rekt Burger clock-in UI (ct-life-evidence/rektburger-20261025/ui-refs/BRIEF.md, user OK 2026-10-25):
    /// the counter's Clock in prompt (<see cref="RektCounterPrompt"/>), the ON SHIFT chip under Cash (<see cref="ShiftChip"/>),
    /// and the GTA-style SHIFT DONE band (<see cref="ShiftBand"/>). The server is the truth for every amount; the chip only
    /// interpolates between reads at the server's rate, and the band shows only server-confirmed pay.
    /// </summary>
    public static class RektShiftFormat
    {
        /// <summary>"$30" for whole dollars, else "$1.85".</summary>
        public static string Dollars(long cents) => cents % 100 == 0 ? "$" + (cents / 100).ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : RbText.Money(cents);

        /// <summary>"7h 12m", "12m", "45s".</summary>
        public static string Span(double ms)
        {
            long s = (long)Math.Max(0, Math.Ceiling(ms / 1000.0));
            if (s >= 3600) return (s / 3600) + "h " + (s % 3600 / 60) + "m";
            if (s >= 60) return (s / 60) + "m";
            return s + "s";
        }

        /// <summary>"0:12:34" (hours always shown, as a shift clock).</summary>
        public static string Clock(double seconds)
        {
            long s = (long)Math.Max(0, Math.Floor(seconds));
            return (s / 3600) + ":" + (s % 3600 / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        /// <summary>"4:59" for a countdown under an hour.</summary>
        public static string Short(double seconds)
        {
            long s = (long)Math.Max(0, Math.Ceiling(seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        /// <summary>Fraction of today's cap the chip's ring shows: paid today before this shift plus this shift's accrual.</summary>
        public static float CapFraction(long dailyCapCents, long capRemainingCents, long accruedCents)
        {
            if (dailyCapCents <= 0) return 0f;
            long before = Math.Max(0, dailyCapCents - capRemainingCents);
            return Mathf.Clamp01((before + Math.Max(0, accruedCents)) / (float)dailyCapCents);
        }

        /// <summary>Near the cap: 85% of it, or under 15 minutes of pay left.</summary>
        public static bool NearCap(float fraction, long leftCents, long ratePerMinuteCents) =>
            fraction >= 0.85f || (ratePerMinuteCents > 0 && leftCents < ratePerMinuteCents * 15);
    }

    /// <summary>The counter prompt's state (UI brief §1), read by the World's counter offer every scan.</summary>
    public static class RektCounterPrompt
    {
        public static CounterPrompt? Build()
        {
            var svc = RektBurgerService.Current;
            var s = svc?.State;
            if (s == null) return null;
            var p = new CounterPrompt { style = "job", icon = "clock", verbKey = "rekt.shift" };
            if (!s.Unlocked)
            {
                p.verbKey = "rekt.getjob"; p.icon = "jobs"; p.style = "jobquiet"; p.locked = true;
                return p;
            }
            var shift = svc.Shift;
            if (shift != null && shift.Active)
            {
                // clocking out pays you: a neutral glass key, not a brand moment; the pay so far under it in green
                p.verbKey = "rekt.clockout"; p.icon = "exit"; p.style = "jobquiet";
                long so = shift.AccruedNow(Time.realtimeSinceStartup);
                p.sub = RbText.F("prompt.soFar", RbText.Money(so)); p.subTone = "good";
                p.busy = svc.Busy;
                return p;
            }
            if (svc.Busy) { p.verbKey = "rekt.clocking"; p.busy = true; return p; }
            if (svc.Offline)
            {
                p.disabled = true; p.sub = RbText.T("prompt.offline"); p.subTone = "bad";
                return p;
            }
            if (s.CapReached)
            {
                p.verbKey = "rekt.capped"; p.disabled = true;
                p.sub = s.ResetsAtMs > 0 ? RbText.F("prompt.back", RektShiftFormat.Span(s.ResetsAtMs - ServerClock.NowMs)) : RbText.T("clock.cap");
                return p;
            }
            long rate = s.RatePerMinuteCents;
            if (rate > 0 && s.RemainingTodayCents < rate * 60)
            {
                p.sub = RbText.F("prompt.only", RbText.Money(s.RemainingTodayCents)); p.subTone = "warn";
            }
            else p.sub = RbText.F("prompt.ready", RektShiftFormat.Dollars(rate * 60), RbText.Money(s.RemainingTodayCents));
            return p;
        }

        /// <summary>The counter's press, by state: the Jobs card while locked, Clock out on shift, a reason while disabled.</summary>
        public static void Press()
        {
            var svc = RektBurgerService.Current;
            if (svc == null) return;
            var s = svc.State;
            if (s != null && !s.Unlocked) { RetentionService.Opener?.Invoke("jobs"); return; }
            if (svc.Shift != null && svc.Shift.Active) { _ = svc.ClockOut(); return; }
            if (svc.Offline) { Bus.Publish(ToastSignal.Text(RbText.T("prompt.offline"), ToastKind.Bad)); return; }
            if (s != null && s.CapReached) { Bus.Publish(ToastSignal.Text(RbText.T("clock.cap"), ToastKind.Bad)); return; }
            _ = svc.ClockIn();
        }
    }

    /// <summary>A thick progress ring drawn once per value change (Painter2D, no per-frame redraw when idle).</summary>
    public sealed class CapRing : VisualElement
    {
        private readonly float _stroke;
        private float _drawn, _target;
        private Color _fill = new Color(0.949f, 0.408f, 0.173f);
        private static readonly Color Track = new Color(1f, 1f, 1f, 0.18f);
        private IVisualElementScheduledItem _step;

        public CapRing(float size, float stroke)
        {
            _stroke = stroke;
            style.width = size; style.height = size;
            style.position = Position.Absolute; style.left = 0; style.top = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Set(float fraction, Color fill, bool instant = false)
        {
            fraction = Mathf.Clamp01(fraction);
            bool recolor = fill != _fill;
            _fill = fill;
            if (instant) { _target = _drawn = fraction; MarkDirtyRepaint(); return; }
            if (Mathf.Abs(fraction - _target) < 0.005f) { if (recolor) MarkDirtyRepaint(); return; }
            _target = fraction;
            // step toward the target over ~300 ms at 30 Hz, then stop
            _step?.Pause();
            float from = _drawn, t0 = Time.unscaledTime;
            _step = schedule.Execute(() =>
            {
                float k = Mathf.Clamp01((Time.unscaledTime - t0) / 0.3f);
                _drawn = Mathf.Lerp(from, _target, 1f - (1f - k) * (1f - k));
                MarkDirtyRepaint();
                if (k >= 1f) _step.Pause();
            }).Every(33);
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width <= 0) return;
            var p = ctx.painter2D;
            var c = r.center;
            float radius = Mathf.Min(r.width, r.height) * 0.5f - _stroke * 0.5f;
            p.lineWidth = _stroke;
            p.lineCap = LineCap.Round;
            p.strokeColor = Track;
            p.BeginPath(); p.Arc(c, radius, 0f, 360f); p.Stroke();
            if (_drawn <= 0.001f) return;
            p.strokeColor = _fill;
            p.BeginPath(); p.Arc(c, radius, -90f, -90f + 360f * _drawn); p.Stroke();
        }
    }

    /// <summary>
    /// The ON SHIFT chip (UI brief §2): a live-activity chip under the Cash pill with the Rekt badge in today's-cap ring, the
    /// elapsed time, pay so far and Clock out (two taps). Compact while driving. Ticks at 4 Hz; the server value wins.
    /// </summary>
    public sealed class ShiftChip : IDisposable
    {
        public static readonly Color Orange = new Color(0.949f, 0.408f, 0.173f);
        public static readonly Color Yellow = new Color(1f, 0.824f, 0.247f);
        public static readonly Color Green = new Color(0.239f, 0.863f, 0.518f);

        public VisualElement Element { get; }
        private readonly RektBurgerService _svc;
        private readonly VisualElement _expanded, _compact, _pip, _outBtn;
        private readonly Label _caption, _timer, _pay, _left, _outLabel, _cTimer, _cPay;
        private readonly CapRing _ring, _cRing;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly IVisualElementScheduledItem _tick, _pulse;
        private double _shownPay = -1;
        private long _lastDollar = -1;
        private bool _confirm, _driving, _peek, _wasNear, _disposed;
        private float _peekUntil, _confirmUntil;
        private bool? _isCompact;

        public ShiftChip(RektBurgerService svc)
        {
            _svc = svc;
            Element = El.Div("hud-shift hud-shift--off");
            Element.name = "rekt-shift-chip";
            Element.pickingMode = PickingMode.Ignore;
            var sheet = Resources.Load<StyleSheet>("UI/RektBurger/rektburger");
            if (sheet != null) Element.styleSheets.Add(sheet);

            _ring = new CapRing(76, 7);
            var ringWrap = El.Div("hud-shift-ringwrap", _ring, Badge("hud-shift-badge"));
            ringWrap.pickingMode = PickingMode.Ignore;
            _pip = El.Div("hud-shift-pip");
            _caption = El.Text(RbText.T("chip.caption"), "hud-shift-caption");
            _timer = El.Text("0:00:00", "hud-shift-time");
            var col = El.Div("hud-shift-col", El.Div("hud-shift-caprow", _pip, _caption), _timer);
            _pay = El.Text("+$0.00", "hud-shift-pay");
            _left = El.Text("", "hud-shift-left");
            var payCol = El.Div("hud-shift-paycol", _pay, _left);
            _outLabel = El.Text(RbText.T("chip.out"), "hud-shift-out-label");
            _outBtn = El.Btn("", PressOut, "hud-shift-out");
            _outBtn.RemoveFromClassList("unity-button");
            _outBtn.name = "rekt-shift-clockout";
            _outLabel.pickingMode = PickingMode.Ignore;
            _outBtn.Add(_outLabel);
            _expanded = El.Div("hud-shift-row", ringWrap, col, payCol, _outBtn);
            _expanded.pickingMode = PickingMode.Ignore;
            foreach (var e in new VisualElement[] { col, payCol, _pip, _caption, _timer, _pay, _left }) e.pickingMode = PickingMode.Ignore;

            _cRing = new CapRing(48, 5);
            var cWrap = El.Div("hud-shift-ringwrap hud-shift-ringwrap--compact", _cRing, Badge("hud-shift-badge hud-shift-badge--compact"));
            _cTimer = El.Text("0:00:00", "hud-shift-time hud-shift-time--compact");
            _cPay = El.Text("+$0.00", "hud-shift-pay hud-shift-pay--compact");
            _compact = El.Btn("", () => { _peek = true; _peekUntil = Time.unscaledTime + 4f; Layout(); }, "hud-shift-compact");
            _compact.RemoveFromClassList("unity-button");
            _compact.name = "rekt-shift-compact";
            foreach (var e in new VisualElement[] { cWrap, _cRing, _cTimer, _cPay }) e.pickingMode = PickingMode.Ignore;
            _compact.Add(cWrap); _compact.Add(_cTimer); _compact.Add(_cPay);
            _compact.Show(false);

            Element.Add(_expanded);
            Element.Add(_compact);

            _subs.On<CTLife.World.Vehicles.VehicleChangedSignal>(s => { _driving = s.vehicle != null; Layout(); });
            _driving = CTLife.World.Vehicles.VehicleSystem.Current != null && CTLife.World.Vehicles.VehicleSystem.Current.Driving != null;
            _tick = Element.schedule.Execute(Tick).Every(250);
            _pulse = Element.schedule.Execute(() => _pip.ToggleInClassList("hud-shift-pip--dim")).Every(800);
            Layout();
            Tick();
            // enter: slide down from under Cash one frame after attach
            Element.schedule.Execute(() => Element.RemoveFromClassList("hud-shift--off")).StartingIn(16);
        }

        private static VisualElement Badge(string cls)
        {
            var b = El.Div(cls);
            b.pickingMode = PickingMode.Ignore;
            var tex = Resources.Load<Texture2D>("UI/RektBurger/logo_badge");
            if (tex != null) b.style.backgroundImage = new StyleBackground(tex);
            return b;
        }

        private void Layout()
        {
            bool compact = _driving && !(_peek && Time.unscaledTime < _peekUntil);
            if (_isCompact.HasValue && _isCompact.Value == compact) return;
            _isCompact = compact;
            // cross-fade two pre-built children (opacity only; the width never animates)
            var show = compact ? _compact : _expanded;
            var hide = compact ? _expanded : _compact;
            hide.Show(false);
            show.AddToClassList("hud-shift-fadein");
            show.Show(true);
            show.schedule.Execute(() => show.RemoveFromClassList("hud-shift-fadein")).StartingIn(16);
        }

        private void PressOut()
        {
            if (_disposed || _svc.Offline || _svc.Busy) return;
            if (!_confirm)
            {
                _confirm = true; _confirmUntil = Time.unscaledTime + 3f;
                _outLabel.text = RbText.T("chip.confirm");
                _outBtn.AddToClassList("hud-shift-out--confirm");
                return;
            }
            ResetConfirm();
            Log.Info("rektburger", "chip: clock out confirmed");
            _ = _svc.ClockOut();
        }

        private void ResetConfirm()
        {
            _confirm = false;
            _outLabel.text = RbText.T("chip.out");
            _outBtn.RemoveFromClassList("hud-shift-out--confirm");
        }

        private void Tick()
        {
            if (_disposed) return;
            var st = _svc.Shift;
            if (st == null || !st.Active) return;
            if (_confirm && Time.unscaledTime > _confirmUntil) ResetConfirm();
            if (_peek && Time.unscaledTime >= _peekUntil) { _peek = false; Layout(); }
            float now = Time.realtimeSinceStartup;
            bool offline = _svc.Offline;
            long daily = _svc.State?.DailyCapCents ?? 4500;
            long rate = st.RatePerMinuteCents;

            // pay: the server's accrual, interpolated at its rate between reads (frozen offline), never counting down
            long target = offline ? st.EarnedCents : st.AccruedNow(now);
            if (_shownPay < 0) _shownPay = target;
            else if (target > _shownPay) _shownPay = target - _shownPay > rate ? _shownPay + (target - _shownPay) * 0.6 : target;
            long shown = (long)Math.Round(_shownPay);
            string payText = "+" + RbText.Money(shown);
            if (_pay.text != payText) { _pay.text = payText; _cPay.text = payText; }
            long dollar = shown / 100;
            if (_lastDollar >= 0 && dollar > _lastDollar)
            {
                _pay.AddToClassList("hud-shift-pay--pulse");
                _pay.schedule.Execute(() => _pay.RemoveFromClassList("hud-shift-pay--pulse")).StartingIn(110);
            }
            _lastDollar = dollar;

            long left = Math.Max(0, st.CapCents - shown);
            bool capped = shown >= st.CapCents;
            float frac = RektShiftFormat.CapFraction(daily, st.CapCents, shown);
            bool near = !capped && RektShiftFormat.NearCap(frac, left, rate);
            var fill = capped ? Green : near ? Yellow : Orange;
            if (capped) frac = Math.Max(frac, 1f);
            _ring.Set(frac, fill); _cRing.Set(frac, fill);
            if (near && !_wasNear)
            {
                _ring.AddToClassList("hud-shift-ring--pulse");
                _ring.schedule.Execute(() => _ring.RemoveFromClassList("hud-shift-ring--pulse")).StartingIn(120);
            }
            _wasNear = near;

            // time: elapsed from the server's start; a countdown in the last 5 minutes before the server closes the shift
            double nowMs = st.NowMs(now);
            double endsIn = st.EndsAtMs > 0 ? (st.EndsAtMs - nowMs) / 1000.0 : double.MaxValue;
            bool ending = endsIn < 300 && endsIn > 0;
            string timeText = ending ? RbText.F("chip.ends", RektShiftFormat.Short(endsIn)) : RektShiftFormat.Clock(st.ElapsedSeconds(now));
            if (_timer.text != timeText) { _timer.text = timeText; _cTimer.text = timeText; }
            _timer.EnableInClassList("hud-shift-time--warn", ending);
            _cTimer.EnableInClassList("hud-shift-time--warn", ending);

            _caption.text = offline ? RbText.T("chip.offline") : capped ? RbText.T("chip.cap") : RbText.T("chip.caption");
            _caption.EnableInClassList("hud-shift-caption--cap", capped && !offline);
            _left.text = offline ? RbText.T("chip.needsNet") : capped ? RbText.T("chip.capLeft") : RbText.F("chip.left", RbText.Money(left));
            _left.EnableInClassList("hud-shift-left--warn", near && !offline);
            Element.EnableInClassList("hud-shift--offline", offline);
            _outBtn.SetEnabled(!offline);
        }

        /// <summary>Slide back up under Cash, then remove.</summary>
        public void Close()
        {
            if (_disposed) return;
            Element.AddToClassList("hud-shift--off");
            Element.schedule.Execute(() => { Element.RemoveFromHierarchy(); Dispose(); }).StartingIn(180);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tick.Pause(); _pulse.Pause();
            _subs.Dispose();
        }
    }

    /// <summary>
    /// The shift summary as a GTA "mission passed" band over the running world (UI brief §3): kicker, SHIFT DONE, Minutes /
    /// Cash earned (the one solid orange tile) / Career XP (when the shift earned any), the rank bar from the player's ladder
    /// (GET /careers?view=ladders), today's cap, Collect (the Cash flies into the Cash pill) and Clock in again.
    /// </summary>
    public static class ShiftBand
    {
        public static bool IsOpen => _root != null;
        private static VisualElement _root;
        private static readonly List<CareerPromotion> Queued = new List<CareerPromotion>();

        public static void QueuePromotion(CareerPromotion p) { if (p != null) Queued.Add(p); }

        /// <summary>Show a summary; one "while you were away" waits until the city HUD is up and the player is on foot.</summary>
        public static void Show(ShiftSummary sum, RektBurgerService svc)
        {
            if (sum == null || UiRoot.Instance == null || IsOpen) return;
            var layer = UiRoot.Instance.ModalLayer;
            if (sum.WhileAway && !Ready())
            {
                layer.schedule.Execute(() => Show(sum, svc)).StartingIn(600);
                return;
            }
            Build(sum, svc);
        }

        private static bool Ready()
        {
            var hud = Hud.TopSlot;
            if (hud == null || hud.panel == null || UiRoot.Instance.HudLayer.resolvedStyle.display == DisplayStyle.None) return false;
            var vs = CTLife.World.Vehicles.VehicleSystem.Current;
            return vs == null || vs.Driving == null;
        }

        private sealed class Counter
        {
            public Label label; public double to; public float start, dur; public Func<double, string> fmt; public bool done;
        }

        private static void Build(ShiftSummary sum, RektBurgerService svc)
        {
            var ui = UiRoot.Instance;
            var root = El.Div("rb-bandroot");
            root.name = "rekt-shift-summary";
            root.pickingMode = PickingMode.Position;
            var sheet = Resources.Load<StyleSheet>("UI/RektBurger/rektburger");
            if (sheet != null) root.styleSheets.Add(sheet);
            var band = El.Div("rb-band");
            band.name = "rekt-shift-band";
            root.Add(band);

            var state = svc?.State;
            bool capped = sum.CapKnown ? sum.CapRemainingCents <= 0 : (state != null && state.CapReached);
            string kicker = sum.WhileAway ? RbText.T("band.away")
                : capped ? RbText.T("band.capKicker")
                : sum.EndReason == "auto" ? RbText.T("band.timedOut")
                : RbText.T("band.clockedOut");
            var head = El.Div("rb-band-head",
                El.Text(kicker, "rb-band-kicker"),
                El.Text(RbText.T(capped ? "band.maxed" : "band.title"), "rb-band-title"),
                El.Text(RbText.T("band.where"), "rb-band-sub"));
            band.Add(head);
            bool tiny = sum.PaidCents <= 0 || sum.DurationSec < 60;
            if (sum.EndReason == "auto" && !sum.WhileAway) band.Add(El.Text(RbText.T("band.fullLength"), "rb-band-note"));
            if (tiny) band.Add(El.Text(RbText.T("band.tiny"), "rb-band-note"));

            // tiles: Minutes · CASH EARNED (solid orange: earned) · Career XP (only when the shift earned XP)
            var counters = new List<Counter>();
            var tiles = El.Div("rb-tiles");
            Label Tile(string cls, string label, string icon = null)
            {
                var n = El.Text("0", "rb-tile-n");
                var row = El.Div("rb-tile-row", n);
                if (icon != null) { var ic = new UiIcon(icon); ic.AddToClassList("rb-tile-coin"); row.Add(ic); }
                var t = El.Div("rb-tile " + cls, row, El.Text(label, "rb-tile-label"));
                tiles.Add(t);
                return n;
            }
            var minutes = Tile("", RbText.T("band.minutes"));
            counters.Add(new Counter { label = minutes, to = sum.Minutes, start = 0.42f, dur = 0.5f, fmt = v => ((long)Math.Round(v)).ToString() });
            var cash = Tile(tiny ? "rb-tile--wide" : "rb-tile--cash rb-tile--wide", RbText.T("band.cash"), "cash");
            counters.Add(new Counter { label = cash, to = sum.PaidCents, start = 0.42f, dur = 0.7f, fmt = v => "+" + RbText.Money((long)Math.Round(v)) });
            if (sum.CareerXp > 0)
            {
                var xp = Tile("", RbText.T("band.xp"));
                counters.Add(new Counter { label = xp, to = sum.CareerXp, start = 0.42f, dur = 0.5f, fmt = v => "+" + (long)Math.Round(v) });
            }
            band.Add(tiles);
            int i = 0;
            foreach (var t in tiles.Children()) { t.style.transitionDelay = new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(0.30f + 0.06f * i++, TimeUnit.Second) }); }

            // the rank bar (filled once the ladder arrives) and today's cap
            var rank = El.Div("rb-rank");
            rank.Show(false);
            band.Add(rank);
            var capRow = El.Div("rb-caprow");
            long daily = state?.DailyCapCents ?? 4500;
            long remaining = Math.Max(0, sum.CapKnown ? sum.CapRemainingCents : state?.RemainingTodayCents ?? 0);
            float capFrac = daily > 0 ? Mathf.Clamp01(1f - remaining / (float)daily) : 0f;
            var capFill = El.Div("rb-bar-fill rb-bar-fill--cap" + (remaining <= 0 ? " rb-bar-fill--full" : capFrac >= 0.85f ? " rb-bar-fill--near" : ""));
            capFill.style.scale = new Scale(new Vector3(0f, 1f, 1f));
            string resets = state != null && state.ResetsAtMs > 0 ? RektShiftFormat.Span(state.ResetsAtMs - ServerClock.NowMs) : null;
            string capText = remaining <= 0
                ? (resets != null ? RbText.F("band.capBack", resets) : RbText.T("band.capHit"))
                : resets != null ? RbText.F("band.capLeft", RbText.Money(remaining), resets) : RbText.F("band.capLeftNoReset", RbText.Money(remaining));
            capRow.Add(El.Text(RbText.T("band.capLabel"), "rb-caprow-label"));
            capRow.Add(El.Div("rb-bar rb-bar--cap", capFill));
            capRow.Add(El.Text(capText, "rb-caprow-text"));
            band.Add(capRow);

            if (sum.UniformGranted) band.Add(El.Div("rb-unlock", new UiIcon("shop"), El.Text(RbText.T("band.uniform"), "rb-unlock-text")));

            // buttons
            var actions = El.Div("rb-band-actions");
            Button collect = null;
            collect = Btn(RbText.T("band.collect"), () => Collect(root, band, cash, sum, svc), "rb-collect", "rekt-shift-collect");
            actions.Add(collect);
            bool offline = svc != null && svc.Offline;
            if (capped) actions.Add(Btn(RbText.T("band.jobs"), () => { Close(root, band, svc); RetentionService.Opener?.Invoke("jobs"); }, "rb-again", "rekt-shift-jobs"));
            else if (!offline && RektBurgerHooks.OnFloor)
                actions.Add(Btn(RbText.T("band.again"), () => { Close(root, band, svc); _ = svc?.ClockIn(); }, "rb-again", "rekt-shift-again"));
            band.Add(actions);

            // HUD to 40% under the band (the world stays visible; no scrim)
            var hud = ui.HudLayer;
            hud.AddToClassList("rb-hud-dim");
            if (sheet != null && !hud.styleSheets.Contains(sheet)) hud.styleSheets.Add(sheet);

            ui.ModalLayer.Add(root);
            _root = root;
            Cues.Play("sfx_ui_success");
            DeviceFeel.Confirm();

            // entrance timeline (~1.7 s), skippable by tapping anywhere
            float t0 = Time.unscaledTime;
            bool finished = false;
            void Finish()
            {
                if (finished) return;
                finished = true;
                foreach (var c in counters) { c.label.text = c.fmt(c.to); c.done = true; }
                root.AddToClassList("rb-in"); root.AddToClassList("rb-in-tiles"); root.AddToClassList("rb-in-actions");
                capFill.style.scale = new Scale(new Vector3(capFrac, 1f, 1f));
                FillRank(rank, true);
            }
            root.schedule.Execute(() => root.AddToClassList("rb-in")).StartingIn(16);
            root.schedule.Execute(() => root.AddToClassList("rb-in-tiles")).StartingIn(300);
            var anim = root.schedule.Execute(() =>
            {
                if (finished) return;
                float t = Time.unscaledTime - t0;
                foreach (var c in counters)
                {
                    if (c.done) continue;
                    float k = Mathf.Clamp01((t - c.start) / c.dur);
                    float e = 1f - Mathf.Pow(1f - k, 4f);
                    c.label.text = c.fmt(c.to * e);
                    if (k >= 1f) c.done = true;
                }
                if (t >= 1.12f && capFill.style.scale.value.value.x <= 0f)
                {
                    capFill.AddToClassList("rb-bar-fill--anim");
                    capFill.style.scale = new Scale(new Vector3(capFrac, 1f, 1f));
                    FillRank(rank, false);
                }
                if (t >= 1.72f) { root.AddToClassList("rb-in-actions"); finished = true; }
            }).Every(35);
            root.RegisterCallback<PointerDownEvent>(e => { if (!finished && !(e.target is Button)) Finish(); });

            // the rank teaser: the player's ladder after this shift's XP
            if (sum.CareerXp > 0) _ = LoadRank(rank, sum.CareerXp);
        }

        private static async Task LoadRank(VisualElement row, int gained)
        {
            if (Queued.Count > 0) { RankPromoted(row); return; }
            try
            {
                var board = await new CTLife.Careers.CareersApi(Services.Get<IRestClient>()).Ladders(default);
                var mine = board?.Mine;
                if (mine == null || row.panel == null) return;
                var cur = mine.Ranks.Find(r => r.Rank == mine.Rank);
                var nxt = mine.Ranks.Find(r => r.Rank == mine.Rank + 1);
                row.Clear();
                if (nxt == null || cur == null)
                {
                    row.Add(El.Text(RbText.F("band.rankTop", mine.Title), "rb-rank-line"));
                    row.Show(true);
                    return;
                }
                float span = Math.Max(1, nxt.XpRequired - cur.XpRequired);
                float to = Mathf.Clamp01((mine.Xp - cur.XpRequired) / span);
                float from = Mathf.Clamp01((mine.Xp - gained - cur.XpRequired) / span);
                var gain = El.Div("rb-bar-fill rb-bar-fill--gain");
                var old = El.Div("rb-bar-fill rb-bar-fill--rank");
                gain.style.scale = new Scale(new Vector3(from, 1f, 1f));
                old.style.scale = new Scale(new Vector3(from, 1f, 1f));
                gain.userData = to;
                var bar = El.Div("rb-bar rb-bar--rank", gain, old);
                var chip = El.Text("+" + gained + " XP", "rb-xpchip");
                bar.Add(chip);
                chip.style.left = Length.Percent(to * 100f);
                row.Add(El.Div("rb-rank-bar", El.Text(cur.Title, "rb-rank-name"), bar, El.Text(nxt.Title, "rb-rank-name")));
                int need = Math.Max(0, nxt.XpRequired - mine.Xp);
                row.Add(El.Text(RbText.F("band.rankXp", mine.Xp - cur.XpRequired, nxt.XpRequired - cur.XpRequired, need), "rb-rank-line"));
                row.Show(true);
                row.userData = "ready";
                Log.Info("rektburger", $"summary rank {mine.Id} r{mine.Rank} xp {mine.Xp} (+{gained}) next {nxt.XpRequired}");
            }
            catch (Exception e) { Log.Warn("rektburger", "ladders for the summary failed: " + CityErrors.From(e, false).Code); }
        }

        private static void RankPromoted(VisualElement row)
        {
            row.Clear();
            row.Add(El.Text(RbText.T("band.promoted"), "rb-rank-line rb-rank-line--promo"));
            row.Show(true);
        }

        /// <summary>Animate the gain part of the rank bar to its new value (once).</summary>
        private static void FillRank(VisualElement row, bool instant)
        {
            if (row == null || !"ready".Equals(row.userData)) { if (row != null) row.schedule.Execute(() => { if ("ready".Equals(row.userData)) FillRank(row, instant); }).StartingIn(250); return; }
            row.userData = "filled";
            var gain = row.Q(className: "rb-bar-fill--gain");
            var chip = row.Q(className: "rb-xpchip");
            if (gain?.userData is float to)
            {
                if (!instant) gain.AddToClassList("rb-bar-fill--anim");
                gain.style.scale = new Scale(new Vector3(to, 1f, 1f));
            }
            chip?.AddToClassList("rb-xpchip--in");
        }

        private static Button Btn(string text, Action click, string cls, string name)
        {
            var b = El.Btn("", click, "rb-bandbtn " + cls);
            b.RemoveFromClassList("unity-button");
            b.name = name;
            var l = El.Text(text, "rb-bandbtn-label"); l.pickingMode = PickingMode.Ignore;
            b.Add(l);
            return b;
        }

        /// <summary>Collect: the Cash number travels to the top-centre Cash pill, the pill bumps, the band collapses.</summary>
        private static void Collect(VisualElement root, VisualElement band, Label cash, ShiftSummary sum, RektBurgerService svc)
        {
            if (root.ClassListContains("rb-collecting")) return;
            root.AddToClassList("rb-collecting");
            var pill = UiRoot.Instance.HudLayer.Q("hud-cash");
            if (pill == null || cash.panel == null || sum.PaidCents <= 0) { Close(root, band, svc); return; }
            var from = root.WorldToLocal(cash.worldBound.center);
            var to = root.WorldToLocal(pill.worldBound.center);
            var fly = El.Text(cash.text, "rb-fly");
            fly.pickingMode = PickingMode.Ignore;
            root.Add(fly);
            fly.style.left = from.x; fly.style.top = from.y;
            fly.schedule.Execute(() => { fly.AddToClassList("rb-fly--go"); fly.style.translate = new Translate(to.x - from.x, to.y - from.y); }).StartingIn(16);
            Cues.Play("sfx_cash_in");
            root.schedule.Execute(() =>
            {
                fly.RemoveFromHierarchy();
                pill.AddToClassList("rb-pill-bump");
                pill.schedule.Execute(() => pill.RemoveFromClassList("rb-pill-bump")).StartingIn(140);
                Close(root, band, svc);
            }).StartingIn(470);
        }

        private static void Close(VisualElement root, VisualElement band, RektBurgerService svc)
        {
            if (root.ClassListContains("rb-closing")) return;
            root.AddToClassList("rb-closing");
            svc?.SummarySeen();
            UiRoot.Instance.HudLayer.RemoveFromClassList("rb-hud-dim");
            root.schedule.Execute(() =>
            {
                root.RemoveFromHierarchy();
                if (ReferenceEquals(_root, root)) _root = null;
                var q = new List<CareerPromotion>(Queued);
                Queued.Clear();
                foreach (var p in q) PromotionCard.Show(p);
            }).StartingIn(190);
        }
    }

    /// <summary>Owns the chip and the band for one service: the chip while on shift, the band for each new summary.</summary>
    public sealed class RektShiftUi : IDisposable
    {
        private readonly RektBurgerService _svc;
        private ShiftChip _chip;
        private bool _disposed;

        public RektShiftUi(RektBurgerService svc)
        {
            _svc = svc;
            _svc.ShiftChanged += Sync;
            _svc.SummaryReady += OnSummary;
            Hud.TopSlotChanged += Sync;
            Sync();
            if (_svc.PendingSummary != null) OnSummary(_svc.PendingSummary);
        }

        private void OnSummary(ShiftSummary s) { if (!_disposed) ShiftBand.Show(s, _svc); }

        private void Sync()
        {
            if (_disposed) return;
            bool on = _svc.Shift != null && _svc.Shift.Active;
            var slot = Hud.TopSlot;
            if (on && slot != null)
            {
                if (_chip == null || _chip.Element.parent != slot)
                {
                    _chip?.Element.RemoveFromHierarchy(); _chip?.Dispose();
                    _chip = new ShiftChip(_svc);
                    slot.Add(_chip.Element);
                }
            }
            else if (_chip != null) { _chip.Close(); _chip = null; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _svc.ShiftChanged -= Sync;
            _svc.SummaryReady -= OnSummary;
            Hud.TopSlotChanged -= Sync;
            if (_chip != null) { _chip.Element.RemoveFromHierarchy(); _chip.Dispose(); _chip = null; }
        }
    }
}
