using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.World;
using UnityEngine;

namespace CTLife.CityLife.RektBurger
{
    /// <summary>
    /// Rekt Burger and the career events for one signed-in player (backend PR #16):
    /// - `GET /jobs/rekt-burger` (unlocked?, pay, today's cap, the open shift), refreshed on sign-in, on walking into the
    ///   restaurant, when a job opens (`job_unlocked`) and at most every 2 minutes while Cash moves;
    /// - the counter's prompt (RektBurgerHooks.Unlocked) and its press (RektBurgerHooks.Open -> the shift);
    /// - the "blew your port" nudge: once a UTC day, when Cash drops near zero and the job is open (or about to be), a Duo
    ///   banner points to Rekt Burger;
    /// - `career_promoted`: the promotion card over the game.
    /// Owned by <see cref="RektBurgerOverlays"/>; rebinds on sign-in and sign-out.
    /// </summary>
    public sealed class RektBurgerService : IDisposable
    {
        public const long NudgeCashBelowCents = 2000;
        public static RektBurgerService Current { get; private set; }

        public readonly RektBurgerApi Api;
        public string UserId { get; }
        public RbState State { get; private set; }
        public bool Disposed { get; private set; }
        public event Action StateChanged;

        /// <summary>The clock-in shift (interface: the backend's start/end/current are being rebuilt; a fake until then).</summary>
        public IRektShiftService Shifts { get; set; }
        public ShiftState Shift { get; private set; }
        /// <summary>An ended shift the player has not seen (clock-out, or found on return); the summary card shows it.</summary>
        public ShiftSummary PendingSummary { get; private set; }
        public event Action ShiftChanged;
        public event Action<ShiftSummary> SummaryReady;
        /// <summary>The last shift read failed on the network (the HUD chip freezes pay and disables Clock out).</summary>
        public bool Offline => _netDown || Application.internetReachability == NetworkReachability.NotReachable;
        private bool _netDown;
        /// <summary>A Clock in / Clock out request is in flight (the counter prompt pulses).</summary>
        public bool Busy => _shiftBusy;
        private string _startKey, _endKey;
        private bool _shiftBusy;

        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly Subscriptions _subs = new Subscriptions();
        private bool _loading;
        private float _stateAt = -1e9f;

        public RektBurgerService(string userId)
        {
            UserId = userId;
            Api = new RektBurgerApi(Services.Get<IRestClient>());
            Current = this;
            RawRoomMessages.Received += OnRaw;
            RektBurgerHooks.FloorChanged += OnFloor;
            _subs.On<ProfileChangedSignal>(_ => OnProfile());
            Shifts = new RektShiftApiService(Services.Get<IRestClient>(), userId);
            _ = Refresh();
            _ = RestoreShift();
            _ = ShiftLoop();
        }

        // ------------------------------------------------------------------ clock in / out

        /// <summary>On sign-in (or app return): an open shift resumes (the working loop plays again), an unseen ended one
        /// becomes the summary.</summary>
        public async Task RestoreShift()
        {
            if (Disposed || Shifts == null) return;
            try
            {
                var (state, unseen) = await Shifts.Current(_life.Token);
                if (Disposed) return;
                SetNet(true);
                SetShift(state);
                if (unseen != null) Summarize(unseen);
                Log.Info("rektburger", "shift restore: " + (state != null && state.Active ? "on shift since " + state.StartedAtMs : "off") + (unseen != null ? ", unseen summary" : ""));
            }
            catch (Exception e) { var err = CityErrors.From(e, false); NetFailed(err); Log.Warn("rektburger", "shift current failed: " + err.Code); }
        }

        private void SetNet(bool ok) { if (_netDown == !ok) return; _netDown = !ok; ShiftChanged?.Invoke(); }
        private void NetFailed(CityError err) { if (err.Is("TIMEOUT") || err.Is("NETWORK_ERROR")) SetNet(false); }

        public async Task<bool> ClockIn()
        {
            if (Disposed || _shiftBusy || Shifts == null) return false;
            if (State == null || !State.Unlocked) { Bus.Publish(ToastSignal.Text(RbText.T("card.locked"), ToastKind.Warning)); return false; }
            if (Shift != null && Shift.Active) return true;
            _shiftBusy = true;
            ShiftChanged?.Invoke();
            _startKey ??= Guid.NewGuid().ToString();
            try
            {
                var s = await Shifts.Start(_startKey, _life.Token);
                _startKey = null;
                SetNet(true);
                Bus.Publish(new InteractionDoneSignal());
                SetShift(s);
                Log.Info("rektburger", "clocked in " + s?.ShiftId);
                return true;
            }
            catch (Exception e)
            {
                var err = CityErrors.From(e, true);
                if (!err.OutcomeUnknown) _startKey = null;
                Log.Warn("rektburger", "clock in failed: " + err.Code);
                string msg = err.Is("COUNTER_TOO_FAR") ? RbText.T("clock.tooFar")
                    : err.Is("POSITION_UNKNOWN") ? RbText.T("clock.position")
                    : err.Is("JOB_LOCKED") ? RbText.T("card.locked")
                    : err.Is("JOB_DAILY_CAP") ? RbText.T("clock.cap") : err.Message;
                NetFailed(err);
                Bus.Publish(ToastSignal.Text(msg, ToastKind.Warning));
                return false;
            }
            finally { _shiftBusy = false; ShiftChanged?.Invoke(); }
        }

        public async Task<ShiftSummary> ClockOut()
        {
            if (Disposed || _shiftBusy || Shifts == null || Shift == null || !Shift.Active) return null;
            _shiftBusy = true;
            _endKey ??= Guid.NewGuid().ToString();
            try
            {
                var sum = await Shifts.End(_endKey, _life.Token);
                _endKey = null;
                SetShift(null);
                if (sum != null) Summarize(sum);
                if (Services.TryGet<PlayerProfile>(out var p)) _ = p.Refresh().ContinueWith(_ => { });
                _ = Refresh();
                return sum;
            }
            catch (Exception e)
            {
                var err = CityErrors.From(e, true);
                if (!err.OutcomeUnknown) _endKey = null;
                Log.Warn("rektburger", "clock out failed: " + err.Code);
                Bus.Publish(ToastSignal.Text(err.Message, ToastKind.Bad));
                return null;
            }
            finally { _shiftBusy = false; }
        }

        /// <summary>The summary was shown: forget it (and never show that shift again on this device).</summary>
        public void SummarySeen()
        {
            if (PendingSummary != null && Shifts is RektShiftApiService api) api.MarkSeen(PendingSummary.ShiftId);
            PendingSummary = null;
        }

        private string _summarized;

        private void Summarize(ShiftSummary s)
        {
            // once per shift (the poll returns the same unseen lastShift until it is shown)
            if (!string.IsNullOrEmpty(s.ShiftId) && s.ShiftId == _summarized) return;
            _summarized = s.ShiftId;
            // an older unseen shift found while a new one runs: the new shift's own summary will follow; drop the old one
            if (s.WhileAway && Shift != null && Shift.Active)
            {
                if (Shifts is RektShiftApiService seen) seen.MarkSeen(s.ShiftId);
                Log.Info("rektburger", "older unseen shift " + s.ShiftId + " dropped (on shift)");
                return;
            }
            PendingSummary = s;
            Log.Info("rektburger", $"shift summary {s.ShiftId}: {s.Minutes} min paid {s.PaidCents} xp {s.CareerXp} ({s.EndReason}{(s.WhileAway ? ", while away" : "")})");
            SummaryReady?.Invoke(s);
        }

        private void SetShift(ShiftState s)
        {
            Shift = s != null && s.Active ? s : null;
            RektBurgerHooks.SetOnShift(Shift != null);
            ShiftChanged?.Invoke();
        }

        /// <summary>While on shift: re-read the server's earnings every 30 s (the HUD chip ticks locally in between).</summary>
        private async Task ShiftLoop()
        {
            while (!Disposed)
            {
                try { await Task.Delay(_netDown ? 10000 : 30000, _life.Token); }
                catch (OperationCanceledException) { return; }
                if (Disposed || Shift == null || Shifts == null) continue;
                try
                {
                    var (state, unseen) = await Shifts.Current(_life.Token);
                    if (Disposed) return;
                    SetNet(true);
                    SetShift(state);
                    if (unseen != null) Summarize(unseen);
                }
                catch (Exception e) { var err = CityErrors.From(e, false); NetFailed(err); Log.Warn("rektburger", "shift poll failed: " + err.Code); }
            }
        }

        public CancellationToken Life => _life.Token;

        public async Task<RbState> Refresh(bool force = true)
        {
            if (Disposed || RuntimeMode.IsOfflineDemo || _loading) return State;
            if (!force && Time.realtimeSinceStartup - _stateAt < 120f) return State;
            _loading = true;
            try
            {
                var s = await Api.State(_life.Token);
                if (Disposed) return s;
                bool was = State?.Unlocked ?? false;
                State = s; _stateAt = Time.realtimeSinceStartup;
                RektBurgerHooks.Unlocked = s.Unlocked;
                Log.Info("rektburger", $"state unlocked {s.Unlocked} ({s.Reason ?? "-"}) employed {s.Employed} today {s.EarnedTodayCents}/{s.DailyCapCents}");
                StateChanged?.Invoke();
                if (!was && s.Unlocked) MaybeNudge();
                return s;
            }
            catch (OperationCanceledException) { return State; }
            catch (Exception e) { Log.Warn("rektburger", "GET /jobs/rekt-burger failed: " + CityErrors.From(e, false).Code); return State; }
            finally { _loading = false; }
        }

        private void OnFloor(bool on) { if (on && !Disposed) _ = Refresh(); }

        private void OnProfile()
        {
            if (Disposed || !Services.TryGet<PlayerProfile>(out var p)) return;
            if (p.CashCents < NudgeCashBelowCents) { _ = Refresh(force: false); MaybeNudge(); }
        }

        /// <summary>The "blew your port" Duo banner: once per UTC day, when Cash is near zero and the job is (or just got) open.</summary>
        private void MaybeNudge()
        {
            if (Disposed || State == null || !State.Unlocked || State.CapReached) return;
            if (!Services.TryGet<PlayerProfile>(out var p) || p.CashCents >= NudgeCashBelowCents) return;
            string key = "ctlife.rektburger.nudge." + UserId;
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (PlayerPrefs.GetString(key, "") == today) return;
            PlayerPrefs.SetString(key, today); PlayerPrefs.Save();
            Log.Info("rektburger", "nudge: cash " + p.CashCents + " -> Rekt Burger");
            DuoNotificationCenter.Post(new DuoNotification
            {
                AppId = "jobs",
                ThreadKey = "rektburger:nudge",
                Title = RbText.T("nudge.title"),
                Body = RbText.T("nudge.body"),
                Open = () => RetentionService.Opener?.Invoke("jobs"),
            });
        }

        private void OnRaw(string type, object payload)
        {
            if (Disposed) return;
            if (type == "job_unlocked")
            {
                var t = payload != null ? Newtonsoft.Json.Linq.JObject.FromObject(payload) : null;
                string reason = CityWire.Str(t?["reason"]);
                Log.Info("rektburger", "job_unlocked " + CityWire.Str(t?["jobId"]) + " (" + reason + ")");
                _ = Refresh();
                DuoNotificationCenter.Post(new DuoNotification
                {
                    AppId = "jobs",
                    ThreadKey = "rektburger:unlocked",
                    Title = RbText.T("unlocked.title"),
                    Body = RbText.F("unlocked.body", RbText.T("reason." + (reason ?? "broke")).ToLowerInvariant()),
                    Open = () => RetentionService.Opener?.Invoke("jobs"),
                });
            }
            else if (type == "career_promoted")
            {
                var p = CareerPromotion.From(payload);
                if (p == null) return;
                Log.Info("careers", $"career_promoted {p.LadderId} {p.FromRank}->{p.ToRank} {p.Title}");
                // after the shift summary's Collect, never on top of it (UI brief §3)
                if (ShiftBand.IsOpen) ShiftBand.QueuePromotion(p); else PromotionCard.Show(p);
            }
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            _subs.Dispose();
            RawRoomMessages.Received -= OnRaw;
            RektBurgerHooks.FloorChanged -= OnFloor;
            RektBurgerHooks.Unlocked = false;
            RektBurgerHooks.SetOnShift(false);
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }

    /// <summary>One service per signed-in player and the counter's hook. Created by FlowController next to the other overlays.</summary>
    public sealed class RektBurgerOverlays : IDisposable
    {
        private readonly Session _session;
        private RektBurgerService _svc;
        private RektShiftUi _ui;
        private bool _disposed;

        public RektBurgerOverlays()
        {
            _session = Services.Get<Session>();
            _session.Changed += Bind;
            // the counter: the approved clock-in prompt (pay facts, cap, on shift, offline) and its press; Job info opens the
            // Jobs app. The ON SHIFT chip and the SHIFT DONE band follow the shift state (RektShiftUi).
            RektBurgerHooks.Open = what => { if (!_disposed) RektCounterPrompt.Press(); };
            RektBurgerHooks.Prompt = RektCounterPrompt.Build;
            RektBurgerHooks.JobInfo = () => RetentionService.Opener?.Invoke("jobs");
            Bind();
        }

        private void Bind()
        {
            if (_disposed) return;
            var user = _session.IsSignedIn ? _session.UserId : null;
            if (_svc != null && !_svc.Disposed && _svc.UserId == user) return;
            _ui?.Dispose(); _ui = null;
            _svc?.Dispose();
            _svc = string.IsNullOrEmpty(user) ? null : new RektBurgerService(user);
            if (_svc != null) _ui = new RektShiftUi(_svc);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Changed -= Bind;
            RektBurgerHooks.Open = null;
            RektBurgerHooks.Prompt = null;
            RektBurgerHooks.JobInfo = null;
            _ui?.Dispose(); _ui = null;
            _svc?.Dispose();
            _svc = null;
        }
    }
}
