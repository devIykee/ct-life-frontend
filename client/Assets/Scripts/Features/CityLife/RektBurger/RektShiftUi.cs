using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.UI.DS;
using CTLife.UI.Kit;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife.RektBurger
{
    /// <summary>
    /// The job's clock-in UI in the design system (ct-life-evidence/design-system, mockups 01a/01b): the counter's Clock in
    /// prompt (<see cref="RektCounterPrompt"/>, drawn by the world prompt), the on-shift pill under Cash (<see cref="ShiftChip"/>,
    /// tap → <see cref="ShiftCard"/>) and the shift summary dialog (<see cref="ShiftBand"/>). The server is the truth for every amount; the chip only
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
            // v2 (mockup a): the counter opens the job card; its Clock in starts the shift (ClockIn guards its own busy state)
            ClockInCard.Show(svc);
        }
    }

    /// <summary>
    /// The on-shift HUD pill (design system v2): the McDonald's mark tile, pay so far (Lilita) and the shift clock, in the HUD
    /// top slot. Tapping it opens the shift card (<see cref="ShiftCard"/>) with Clock out. Ticks at 4 Hz; the server value wins.
    /// </summary>
    public sealed class ShiftChip : IDisposable
    {
        public VisualElement Element { get; }
        private readonly RektBurgerService _svc;
        private readonly Label _pay, _caption;
        private readonly IVisualElementScheduledItem _tick;
        private double _shownPay = -1;
        private long _lastDollar = -1;
        private bool _disposed;

        public ShiftChip(RektBurgerService svc)
        {
            _svc = svc;
            var pill = new Button(() => ShiftCard.Show(_svc)) { text = "", name = "rekt-shift-chip", tooltip = RbText.T("card.shift") };
            pill.AddToClassList("ds-hud-pill");
            pill.AddToClassList("ds-onshift");
            pill.AddToClassList("ds-onshift--off");
            pill.AddToClassList(BrandTable.ClassFor(BrandTable.McDonalds));
            var mark = El.Div("ds-hud-mark");
            mark.pickingMode = PickingMode.Ignore;
            if (BrandTable.Mark(BrandTable.McDonalds) is VisualElementMark m) mark.Add(Ds.MarkElement(m, true));
            var live = El.Div("ds-live-dot");
            live.pickingMode = PickingMode.Ignore;
            mark.Add(live);
            _pay = Ds.Text("+$0.00", "ds-t-hud ds-onshift-pay");
            _pay.name = "rekt-shift-pay";
            // one clear row (v2 mockup d's objective pill): the mark, pay so far, then the shift clock in the brand colour
            _caption = Ds.Text(RbText.T("chip.caption"), "ds-onshift-trail");
            _caption.name = "rekt-shift-caption";
            var col = El.Div("ds-onshift-row", _pay, _caption);
            col.pickingMode = PickingMode.Ignore;
            pill.Add(mark);
            pill.Add(col);
            Element = pill;
            _tick = Element.schedule.Execute(Tick).Every(250);
            Tick();
            Element.schedule.Execute(() => Element.RemoveFromClassList("ds-onshift--off")).StartingIn(16);
        }

        private void Tick()
        {
            if (_disposed) return;
            var st = _svc.Shift;
            if (st == null || !st.Active) return;
            float now = Time.realtimeSinceStartup;
            bool offline = _svc.Offline;
            long rate = st.RatePerMinuteCents;
            // pay: the server's accrual, interpolated at its rate between reads (frozen offline), never counting down
            long target = offline ? st.EarnedCents : st.AccruedNow(now);
            if (_shownPay < 0) _shownPay = target;
            else if (target > _shownPay) _shownPay = target - _shownPay > rate ? _shownPay + (target - _shownPay) * 0.6 : target;
            long shown = (long)Math.Round(_shownPay);
            string payText = "+" + RbText.Money(shown);
            if (_pay.text != payText) _pay.text = payText;
            long dollar = shown / 100;
            if (_lastDollar >= 0 && dollar > _lastDollar)
            {
                Element.AddToClassList("ds-bump");
                Element.schedule.Execute(() => Element.RemoveFromClassList("ds-bump")).StartingIn(140);
            }
            _lastDollar = dollar;
            bool capped = shown >= st.CapCents;
            double nowMs = st.NowMs(now);
            double endsIn = st.EndsAtMs > 0 ? (st.EndsAtMs - nowMs) / 1000.0 : double.MaxValue;
            bool ending = endsIn < 300 && endsIn > 0;
            string caption = offline ? RbText.T("chip.offline")
                : capped ? RbText.T("chip.cap")
                : ending ? RbText.F("chip.ends", RektShiftFormat.Short(endsIn))
                : RbText.F("chip.clock", RektShiftFormat.Clock(st.ElapsedSeconds(now)));
            if (_caption.text != caption) _caption.text = caption;
            _caption.EnableInClassList("ds-ink-warn", ending || offline);
            Element.EnableInClassList("ds-onshift--offline", offline);
        }

        /// <summary>Slide back up under Cash, then remove.</summary>
        public void Close()
        {
            if (_disposed) return;
            ShiftCard.CloseIfOpen();
            Element.AddToClassList("ds-onshift--off");
            Element.schedule.Execute(() => { Element.RemoveFromHierarchy(); Dispose(); }).StartingIn(180);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tick.Pause();
        }
    }

    /// <summary>
    /// The career rank row shared by the job cards (v2 "reward row"): the crew-uniform render, the next rank's title in the
    /// brand colour, the XP bar and count, under a divider ("EARN 40 XP TO GET PROMOTED"). Read from the player's ladder
    /// (GET /careers?view=ladders); hidden until it answers, and left hidden when the player is on no ladder.
    /// </summary>
    public static class RektRank
    {
        public const string Pic = "UI/RektBurger/crew-head";
        private static CTLife.Careers.Ladder _cached;
        private static float _cachedAt = -1000f;

        public struct View { public string Divider, Title, Count, Of; public float From, To; }

        /// <summary>Pure: what the row says for a ladder, given XP just earned (0 = none).</summary>
        public static View Describe(CTLife.Careers.Ladder mine, int gained)
        {
            var cur = mine.Ranks.Find(r => r.Rank == mine.Rank);
            var nxt = mine.Ranks.Find(r => r.Rank == mine.Rank + 1);
            if (nxt == null || cur == null)
                return new View { Divider = RbText.T("rank.top"), Title = mine.Title, Count = "", Of = null, From = 1f, To = 1f };
            float span = Math.Max(1, nxt.XpRequired - cur.XpRequired);
            int inRank = Math.Max(0, mine.Xp - cur.XpRequired);
            return new View
            {
                Divider = RbText.F("rank.divider", Math.Max(0, nxt.XpRequired - mine.Xp)),
                Title = nxt.Title,
                Count = inRank.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Of = ((int)span).ToString(System.Globalization.CultureInfo.InvariantCulture),
                From = Mathf.Clamp01((inRank - gained) / span),
                To = Mathf.Clamp01(inRank / span),
            };
        }

        /// <summary>Fills a dialog's hidden divider + row once the ladder answers (fresh read when XP was just earned).</summary>
        public static async void Fill(VisualElement dialog, DsProgressRow row, int gained, float delaySeconds = 0f)
        {
            try
            {
                CTLife.Careers.Ladder mine = _cached;
                if (mine == null || gained > 0 || Time.unscaledTime - _cachedAt > 60f)
                {
                    var board = await new CTLife.Careers.CareersApi(Services.Get<IRestClient>()).Ladders(default);
                    mine = board?.Mine;
                    _cached = mine; _cachedAt = Time.unscaledTime;
                }
                if (mine == null || dialog.panel == null) return;
                var v = Describe(mine, gained);
                var divider = dialog.Q(className: "ds-divider");
                if (divider?.Q<Label>() is Label l) l.text = v.Divider.ToUpperInvariant();
                row.SetTitle(v.Title);
                row.SetCount(v.Count, v.Of);
                row.SetValue(v.From);
                if (divider != null) divider.style.display = DisplayStyle.Flex;
                row.style.display = DisplayStyle.Flex;
                // the fill moves from the old to the new value after the count-up
                if (v.To > v.From) row.schedule.Execute(() => row.SetValue(v.To)).StartingIn((long)(Mathf.Max(0.05f, delaySeconds) * 1000));
                Log.Info("rektburger", $"rank row {mine.Id} r{mine.Rank} xp {mine.Xp} (+{gained})");
            }
            catch (Exception e) { Log.Warn("rektburger", "ladders for the job card failed: " + CityErrors.From(e, false).Code); }
        }

        /// <summary>A hidden rank row + its divider label for a DialogSpec (filled by <see cref="Fill"/>).</summary>
        public static DsProgressRow Row(DialogSpec spec)
        {
            var row = Ds.Progress("trophy", " ", null, 0f, "", null);
            row.name = "rekt-rank-row";
            row.style.display = DisplayStyle.None;
            spec.Progress = row;
            spec.ProgressLabel = " ";
            return row;
        }

        /// <summary>
        /// The crew uniform row (until the player has it): the server grants the uniform for the first shift of at least
        /// 2 minutes (careers.json rektBurger.shift.minCountedSec 120), so the row is real progress toward a real reward.
        /// </summary>
        public static DsProgressRow UniformRow(DialogSpec spec, int minutesWorked, bool granted)
        {
            int need = 2;
            int m = granted ? need : Mathf.Clamp(minutesWorked, 0, need);
            var row = Ds.Progress(Pic, NameTable.Get("item.rekt_burger_uniform", "Crew uniform"), null, 0f, m.ToString(System.Globalization.CultureInfo.InvariantCulture), need.ToString(System.Globalization.CultureInfo.InvariantCulture));
            row.name = "rekt-uniform-row";
            row.schedule.Execute(() => row.SetValue(m / (float)need)).StartingIn(granted ? 850 : 60);
            spec.Progress = row;
            spec.ProgressLabel = granted ? RbText.T("uniform.got") : RbText.T("uniform.divider");
            return row;
        }

        /// <summary>The job cards' progress row: the uniform while the player doesn't have it, else their ladder's next rank.</summary>
        public static DsProgressRow For(DialogSpec spec, RbState s, int minutesWorked, bool grantedNow, out bool ladder)
        {
            ladder = !(grantedNow || (s != null && !s.HasUniform));
            return ladder ? Row(spec) : UniformRow(spec, minutesWorked, grantedNow);
        }

        /// <summary>After Ds.Dialog: the divider stays hidden until the ladder answers.</summary>
        public static void HideDivider(VisualElement dialog)
        {
            var d = dialog.Q(className: "ds-divider");
            if (d != null) d.style.display = DisplayStyle.None;
        }
    }

    /// <summary>The counter's Clock in card (v2 mockup a): the brand hero, what the job is, Pay / Left today, the rank row
    /// and one big Clock in.</summary>
    public static class ClockInCard
    {
        private static VisualElement _open;
        public static bool IsOpen => _open != null && _open.panel != null;

        public static void Show(RektBurgerService svc)
        {
            var ui = UiRoot.Instance;
            var s = svc?.State;
            if (ui?.Modals == null || s == null || IsOpen) return;
            long rate = s.RatePerMinuteCents;
            VisualElement dialog = null;
            var spec = new DialogSpec
            {
                Name = "rekt-clockin-card",
                Brand = BrandTable.McDonalds,
                Hero = Ds.HeroBrand(BrandTable.McDonalds),
                Title = NameTable.Get("venue.rekt_burger", "McDonald's"),
                Subtitle = RbText.T("clockin.sub"),
                Tiles = { Ds.Stat(RbText.T("clockin.pay"), RbText.F("clockin.rate", RektShiftFormat.Dollars(rate * 60)), Ds.Coin, name: "rekt-clockin-pay"),
                          Ds.Stat(RbText.T("clockin.left"), RbText.Money(s.RemainingTodayCents), Ds.Coin, name: "rekt-clockin-left") },
                Primary = new DsAction { Label = RbText.T("card.work"), Name = "rekt-clockin", OnClick = () =>
                {
                    ui.Modals.Close(dialog); _open = null;
                    Log.Info("rektburger", "clock-in card: clock in");
                    _ = svc.ClockIn();
                } },
            };
            var row = RektRank.For(spec, s, 0, false, out bool ladder);
            dialog = Ds.Dialog(spec);
            if (ladder) RektRank.HideDivider(dialog);
            _open = dialog;
            ui.Modals.Show(dialog, DismissMode.Back, () => _open = null, "rekt-clockin-card");
            if (ladder) RektRank.Fill(dialog, row, 0);
        }
    }

    /// <summary>The shift card (tap on the on-shift pill): time and pay so far, what's left today, the rank row, Clock out.</summary>
    public static class ShiftCard
    {
        private static VisualElement _open;
        public static bool IsOpen => _open != null && _open.panel != null;

        public static void CloseIfOpen()
        {
            if (_open != null) UiRoot.Instance?.Modals?.Close(_open);
            _open = null;
        }

        public static void Show(RektBurgerService svc)
        {
            var ui = UiRoot.Instance;
            var st = svc?.Shift;
            if (ui?.Modals == null || st == null || !st.Active || IsOpen) return;
            float now = Time.realtimeSinceStartup;
            var time = Ds.Stat(RbText.T("dlg.time"), RektShiftFormat.Clock(st.ElapsedSeconds(now)), "clock", name: "rekt-card-time");
            var pay = Ds.Stat(RbText.T("dlg.soFar"), "+" + RbText.Money(st.AccruedNow(now)), Ds.Coin, name: "rekt-card-pay");
            bool offline = svc.Offline;
            VisualElement dialog = null;
            var spec = new DialogSpec
            {
                Name = "rekt-shift-card",
                Brand = BrandTable.McDonalds,
                Hero = Ds.HeroBrand(BrandTable.McDonalds),
                Title = RbText.T("dlg.onShift"),
                Subtitle = RbText.F("chip.left", RbText.Money(Math.Max(0, st.CapCents - st.AccruedNow(now)))),
                Tiles = { time, pay },
                Primary = new DsAction { Label = RbText.T("chip.out"), Name = "rekt-shift-clockout", Disabled = offline, OnClick = () =>
                {
                    ui.Modals.Close(dialog); _open = null;
                    Log.Info("rektburger", "card: clock out");
                    _ = svc.ClockOut();
                } },
                Secondary = new DsAction { Label = RbText.T("dlg.keep"), Kind = BtnKind.Text, Name = "rekt-shift-keep", OnClick = () => { ui.Modals.Close(dialog); _open = null; } },
            };
            var row = RektRank.For(spec, svc.State, (int)(st.ElapsedSeconds(now) / 60), false, out bool ladder);
            dialog = Ds.Dialog(spec);
            if (ladder) RektRank.HideDivider(dialog);
            if (offline) Ds.SetError(dialog, RbText.T("chip.needsNet"));
            var sub = dialog.Q<Label>(className: "ds-t-body");
            void Refresh()
            {
                var s = svc.Shift;
                if (s == null || !s.Active) return;
                float n = Time.realtimeSinceStartup;
                long shown = svc.Offline ? s.EarnedCents : s.AccruedNow(n);
                time.Q<Label>("ds-tile-value").text = RektShiftFormat.Clock(s.ElapsedSeconds(n));
                pay.Q<Label>("ds-tile-value").text = "+" + RbText.Money(shown);
                long left = Math.Max(0, s.CapCents - shown);
                if (sub != null) sub.text = left > 0 ? RbText.F("chip.left", RbText.Money(left)) : RbText.T("chip.capLeft");
            }
            Refresh();
            dialog.schedule.Execute(Refresh).Every(250);
            _open = dialog;
            ui.Modals.Show(dialog, DismissMode.Back, () => _open = null, "rekt-shift-card");
            if (ladder) RektRank.Fill(dialog, row, 0);
        }
    }

    /// <summary>
    /// The shift summary (v2 mockup b): the brand hero, "Shift done!", what's left today, Time worked / Earned (counting up),
    /// the rank row (the fill moves after the count-up), Collect (the coin flies into the Cash pill, which bumps) and Clock in
    /// again / Jobs. The server is the truth for every amount shown.
    /// </summary>
    public static class ShiftBand
    {
        public static bool IsOpen => _open != null;
        private static VisualElement _open;
        private static readonly List<CareerPromotion> Queued = new List<CareerPromotion>();

        public static void QueuePromotion(CareerPromotion p) { if (p != null) Queued.Add(p); }

        /// <summary>Show a summary; one "while you were away" waits until the city HUD is up and the player is on foot.</summary>
        public static void Show(ShiftSummary sum, RektBurgerService svc)
        {
            if (sum == null || UiRoot.Instance == null || UiRoot.Instance.Modals == null || IsOpen) return;
            if (sum.WhileAway && !Ready())
            {
                UiRoot.Instance.ModalLayer.schedule.Execute(() => Show(sum, svc)).StartingIn(600);
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

        /// <summary>"1 min", "12 min", "1h 5m" (whole minutes worked, from the server).</summary>
        public static string Worked(int minutes) => minutes >= 60 ? (minutes / 60) + "h " + (minutes % 60) + "m" : Math.Max(0, minutes) + " min";

        /// <summary>Pure: the summary's one plain line under the title.</summary>
        public static string Subtitle(ShiftSummary sum, long remainingCents, string resetsIn)
        {
            bool capped = remainingCents <= 0;
            if (sum.PaidCents <= 0 || sum.DurationSec < 60) return RbText.T("band.tiny");
            if (capped) return resetsIn != null ? RbText.F("dlg.capBack", resetsIn) : RbText.T("band.capHit");
            string left = RbText.F("dlg.left", RbText.Money(remainingCents));
            if (sum.WhileAway) return RbText.T("dlg.away") + ". " + left;
            if (sum.EndReason == "auto") return RbText.T("dlg.fullLength") + ". " + left;
            return RbText.T("dlg.nice") + " " + left;
        }

        private static void Build(ShiftSummary sum, RektBurgerService svc)
        {
            var ui = UiRoot.Instance;
            var state = svc?.State;
            long remaining = Math.Max(0, sum.CapKnown ? sum.CapRemainingCents : state?.RemainingTodayCents ?? 0);
            bool capped = sum.CapKnown ? sum.CapRemainingCents <= 0 : (state != null && state.CapReached);
            string resets = state != null && state.ResetsAtMs > 0 ? RektShiftFormat.Span(state.ResetsAtMs - ServerClock.NowMs) : null;

            var time = Ds.Stat(RbText.T("dlg.time"), Worked(sum.Minutes), "clock", name: "rekt-summary-time");
            var earned = Ds.Stat(RbText.T("dlg.earned"), "+" + RbText.Money(0), Ds.Coin, name: "rekt-summary-earned");
            var spec = new DialogSpec
            {
                Name = "rekt-shift-summary",
                Brand = BrandTable.McDonalds,
                Hero = Ds.HeroBrand(BrandTable.McDonalds),
                Title = RbText.T(capped ? "dlg.maxed" : "dlg.done"),
                Subtitle = Subtitle(sum, capped ? 0 : remaining, resets),
                Tiles = { time, earned },
            };
            var row = RektRank.For(spec, state, sum.Minutes, sum.UniformGranted, out bool ladder);

            VisualElement dialog = null;
            spec.Primary = new DsAction
            {
                Label = sum.PaidCents > 0 ? RbText.F("dlg.collect", RbText.Money(sum.PaidCents)) : RbText.T("dlg.ok"),
                Icon = sum.PaidCents > 0 ? Ds.Coin : null,
                Name = "rekt-shift-collect",
                OnClick = () => Collect(dialog, earned, sum, svc),
            };
            bool offline = svc != null && svc.Offline;
            if (capped) spec.Secondary = new DsAction { Label = RbText.T("band.jobs"), Kind = BtnKind.Text, Name = "rekt-shift-jobs", OnClick = () => { Close(dialog, svc); RetentionService.Opener?.Invoke("jobs"); } };
            else if (!offline && RektBurgerHooks.OnFloor)
                spec.Secondary = new DsAction { Label = RbText.T("band.again"), Kind = BtnKind.Text, Name = "rekt-shift-again", OnClick = () => { Close(dialog, svc); _ = svc?.ClockIn(); } };
            dialog = Ds.Dialog(spec);
            if (ladder) RektRank.HideDivider(dialog);
            _open = dialog;
            ui.Modals.Show(dialog, DismissMode.Back, () => Close(null, svc), "rekt-shift-summary");
            Cues.Play("sfx_ui_success");
            DeviceFeel.Confirm();

            // motion: Earned counts up (600 ms, ease-out) after the card pops in; the rank fill moves after the count-up
            var value = earned.Q<Label>("ds-tile-value");
            float t0 = Time.unscaledTime + 0.2f;
            IVisualElementScheduledItem anim = null;
            anim = dialog.schedule.Execute(() =>
            {
                float k = Mathf.Clamp01((Time.unscaledTime - t0) / 0.6f);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                value.text = "+" + RbText.Money((long)Math.Round(sum.PaidCents * e));
                if (k >= 1f) anim.Pause();
            }).Every(33);
            if (ladder) RektRank.Fill(dialog, row, sum.CareerXp, 0.85f);
        }

        /// <summary>Collect: a coin flies from the Earned tile into the Cash pill (kept above the scrim while the card is up), the
        /// pill bumps, then the card leaves.</summary>
        private static void Collect(VisualElement dialog, VisualElement earned, ShiftSummary sum, RektBurgerService svc)
        {
            if (dialog == null || dialog.ClassListContains("rb-collecting")) return;
            dialog.AddToClassList("rb-collecting");
            var ui = UiRoot.Instance;
            var pill = ui.DsModalLayer.Q("hud-cash-modal") ?? ui.HudLayer.Q("hud-cash");
            var layer = ui.ToastLayer;
            if (pill == null || earned.panel == null || sum.PaidCents <= 0 || pill.worldBound.width <= 0) { Close(dialog, svc); return; }
            var from = layer.WorldToLocal(earned.worldBound.center);
            var to = layer.WorldToLocal(pill.worldBound.min + new Vector2(22f, pill.worldBound.height * 0.5f));
            var coin = Ds.Icon(Ds.Coin, 64);
            coin.AddToClassList("ds-fly");
            coin.style.left = from.x - 32; coin.style.top = from.y - 32;
            layer.Add(coin);
            coin.schedule.Execute(() => { coin.AddToClassList("ds-fly--go"); coin.style.translate = new Translate(to.x - from.x, to.y - from.y); }).StartingIn(16);
            Cues.Play("sfx_cash_in");
            coin.schedule.Execute(() =>
            {
                coin.RemoveFromHierarchy();
                pill.AddToClassList("ds-bump");
                pill.schedule.Execute(() => pill.RemoveFromClassList("ds-bump")).StartingIn(140);
            }).StartingIn(470);
            dialog.schedule.Execute(() => Close(dialog, svc)).StartingIn(620);
        }

        private static void Close(VisualElement dialog, RektBurgerService svc)
        {
            if (_open == null) return;
            var open = _open;
            _open = null;
            svc?.SummarySeen();
            if (dialog != null) UiRoot.Instance.Modals.Close(open);
            var q = new List<CareerPromotion>(Queued);
            Queued.Clear();
            foreach (var p in q) PromotionCard.Show(p);
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
