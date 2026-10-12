using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.Rest.Model;
using UnityEngine;

namespace CTLife.CityLife
{
    /// <summary>
    /// Server needs readings and their display projection. A new snapshot is taken whenever the profile's `Me` or its
    /// `Needs` object is replaced (GET /me, POST /needs/actions); Cash-only updates keep the previous reading time.
    /// </summary>
    public sealed class NeedsClock
    {
        private object _meRef, _needsRef;
        private NeedsConfig _config;
        private string _configReason;
        private object _configRoot;
        private readonly NeedsWarningTracker _tracker = new NeedsWarningTracker();

        public NeedsSnapshot Snapshot { get; private set; }

        public NeedsConfig Config
        {
            get
            {
                if (!Services.TryGet<ConfigStore>(out var store)) return null;
                if (_config == null || !ReferenceEquals(_configRoot, store.Root))
                {
                    _configRoot = store.Root;
                    if (!NeedsConfig.TryParse(store.Config("needs"), out _config, out _configReason))
                        Log.Warn("citylife", "needs config unavailable: " + _configReason);
                }
                return _config;
            }
        }

        public bool Observe(Me me, double nowMs)
        {
            if (me?.Needs == null) return false;
            if (ReferenceEquals(me, _meRef) && ReferenceEquals(me.Needs, _needsRef)) return false;
            _meRef = me;
            _needsRef = me.Needs;
            var n = me.Needs;
            Snapshot = new NeedsSnapshot(n.Energy, n.Hunger, n.Fun, n.Hygiene, n.Sanity, nowMs);
            return true;
        }

        public double[] Projected(double nowMs) => Snapshot == null || Config == null ? null : NeedsProjection.Project(Snapshot, Config, nowMs);

        public List<(string need, NeedLevel level)> NewWarnings(double nowMs)
        {
            var p = Projected(nowMs);
            return p == null ? new List<(string, NeedLevel)>() : _tracker.Update(p, Config);
        }

        public void Reset()
        {
            Snapshot = null;
            _meRef = _needsRef = null;
            _tracker.Reset();
        }
    }

    /// <summary>
    /// Shared City Life state while the phone exists: API, needs clock, home list cache, rent watcher, the visit flow and
    /// its room, and proposal gateways (stubs until the server ships them). Reference-counted by the apps and overlays
    /// that use it; the last release disposes it.
    /// </summary>
    public sealed class CityLifeHub : IDisposable
    {
        /// <summary>Re-read GET /me at most this often for needs decay (≤ 1 point of drift at the fastest rate in needs.json).</summary>
        public const double NeedsRefreshSeconds = 600;
        public const double HomesRefreshSeconds = 600;

        private static CityLifeHub _shared;
        private int _refs;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly string _user;
        private double _lastProfileRefresh = -1e9, _lastHomesFetch = -1e9;
        private bool _profileRefreshQueued, _homesLoading;
        private RentView? _lastRent;
        private long _lastDebt = -1;
        private CancellationTokenSource _visitOp;

        public readonly CityLifeApi Api;
        public readonly NeedsClock Needs = new NeedsClock();
        public readonly HomeVisitFlow Visit = new HomeVisitFlow();
        public readonly IBusinessGateway Businesses;
        public readonly INftGateway Nfts;
        public readonly NftImageCache Images;
        public IHomeVisitPresenter Presenter { get; private set; }
        public List<HomeOffer> Homes { get; private set; } = new List<HomeOffer>();
        public CityError HomesError { get; private set; }
        public int MoodBps { get; private set; }
        public bool MoodKnown { get; private set; }
        public bool Disposed { get; private set; }
        public CancellationToken Life => _life.Token;
        public event Action HomesChanged;
        public event Action<string> PaymentConfirmed;

        public static CityLifeHub Current => _shared != null && !_shared.Disposed ? _shared : null;

        public static CityLifeHub Acquire()
        {
            var session = Services.Get<Session>();
            if (_shared == null || _shared.Disposed || _shared._user != session.UserId) _shared = new CityLifeHub(session.UserId);
            _shared._refs++;
            return _shared;
        }

        public void Release()
        {
            if (Disposed) return;
            if (--_refs <= 0) Dispose();
        }

        private CityLifeHub(string userId)
        {
            _user = userId;
            var rest = Services.Get<IRestClient>();
            Api = new CityLifeApi(rest);
            // Proposal surfaces: REST against the proposed routes; a missing route answers 404 without a contract body,
            // which CityErrors reports as UNAVAILABLE, and the UI falls back to read-only.
            Businesses = Services.TryGet<IBusinessGateway>(out var b) ? b : new RestBusinessGateway(rest);
            Nfts = Services.TryGet<INftGateway>(out var n) ? n : new RestNftGateway(rest);
            Presenter = Services.TryGet<IHomeVisitPresenter>(out var p) ? p : new RoomOnlyHomeVisitPresenter();
            Presenter.PresenceChanged += OnPresence;
            Presenter.Closed += OnVisitClosed;
            Images = new NftImageCache();
            _subs.On<ProfileChangedSignal>(_ => OnProfileChanged());
            _subs.On<MoodSignal>(m => { MoodBps = m.bps; MoodKnown = true; });
            _subs.On<PaymentConfirmedMessage>(m => { if (m != null) PaymentConfirmed?.Invoke(m.intentId); });
            if (Services.TryGet<IRealtimeService>(out var rt) && rt.City != null && rt.City.IsJoined) { MoodBps = rt.City.Mood; MoodKnown = true; }
            OnProfileChanged();
            _ = Loop();
        }

        public PlayerProfile Profile => Services.TryGet<PlayerProfile>(out var p) ? p : null;
        public string UserId => _user;
        public double NowMs => ServerClock.NowMs;

        /// <summary>A zero-valued first city snapshot need not emit MoodSignal. Reconcile it, and clear knowledge on disconnect.</summary>
        public void RefreshMood()
        {
            if (Services.TryGet<IRealtimeService>(out var rt) && rt.City != null && rt.City.IsJoined)
            {
                MoodBps = rt.City.Mood;
                MoodKnown = true;
            }
            else MoodKnown = false;
        }

        // ---------------------------------------------------------------- profile / needs / rent

        private void OnProfileChanged()
        {
            var me = Profile?.Me;
            if (me == null || Disposed) return;
            Needs.Observe(me, NowMs);
            CheckRent(me);
        }

        public RentView RentNow()
        {
            var me = Profile?.Me;
            return me == null ? new RentView(RentState.Unknown, false, 0, -1, 0, -1) : RentFor(me);
        }

        private RentView RentFor(Me me)
        {
            var home = Homes.Find(h => h.Id == me.HomeId);
            var economy = Services.TryGet<ConfigStore>(out var cfg) ? cfg.Config("economy") : null;
            var unlock = WeekClock.Next(WeekClock.ParseOr(CityWire.Str(economy?["sundayUnlockCron"]), WeekClock.ContractUnlock), DateTimeOffset.FromUnixTimeMilliseconds((long)NowMs));
            var inputs = new RentInputs
            {
                HomeId = me.HomeId, RentCents = home?.RentCents ?? -1, NextDueAtMs = home?.NextDueAtMs,
                CashCents = me.CashCents, DebtCents = me.DebtCents, ServerStatus = home?.Status, MissedWeeks = home?.MissedWeeks ?? -1,
            };
            return RentLedger.Evaluate(inputs, RentPolicy.Parse(economy), NowMs, unlock.ToUnixTimeMilliseconds());
        }

        private void CheckRent(Me me)
        {
            // Birth-lottery debt is the opening balance, not a missed rent payment. Overlays subscribe before the
            // creator finishes, so ignore those readings and establish a new baseline after character creation.
            if (!Onboarding.HasCharacter(me))
            {
                _lastRent = null;
                _lastDebt = -1;
                return;
            }
            if (Homes.Count == 0) return;
            var view = RentFor(me);
            var msg = RentLedger.Transition(_lastRent, view, _lastDebt, me.DebtCents);
            _lastRent = view;
            _lastDebt = me.DebtCents;
            if (msg != null && Phases.IsOpen(4))
                Bus.Publish(ToastSignal.Text(CityLifeStrings.T(msg), msg == "rent.toast.settled" ? ToastKind.Good : ToastKind.Bad));
        }

        /// <summary>GET /me (server-authoritative Cash, needs, home). Coalesced to one call per 2 s.</summary>
        public async void RefreshProfile(bool force = false)
        {
            var profile = Profile;
            if (Disposed || profile == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!force && now - _lastProfileRefresh < 2)
            {
                if (_profileRefreshQueued) return;
                _profileRefreshQueued = true;
                try { await Awaitable.WaitForSecondsAsync(2f, _life.Token); }
                catch (OperationCanceledException) { return; }
                finally { _profileRefreshQueued = false; }
            }
            _lastProfileRefresh = Time.realtimeSinceStartupAsDouble;
            try { await profile.Refresh(_life.Token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("citylife", "Profile refresh failed: " + (e is ApiException a ? a.Code : e.GetType().Name)); }
        }

        /// <summary>GET /homes merged over the registry. Errors keep the registry rows and are exposed for the UI.</summary>
        public async Task RefreshHomes(bool force = false)
        {
            if (Disposed || _homesLoading) return;
            if (!force && Time.realtimeSinceStartupAsDouble - _lastHomesFetch < HomesRefreshSeconds && Homes.Count > 0) return;
            _homesLoading = true;
            try
            {
                var cfg = Services.Get<ConfigStore>();
                var registry = HomeCatalog.FromRegistry(cfg.Ids("homes"), cfg.Config("economy"));
                if (!Phases.IsOpen(4))
                {
                    // GET /homes is x-phase 4: before then the registry rows are all there is.
                    Homes = registry;
                    HomesError = null;
                    _lastHomesFetch = Time.realtimeSinceStartupAsDouble;
                    HomesChanged?.Invoke();
                    return;
                }
                try
                {
                    var rows = await Api.Homes(_life.Token);
                    Homes = HomeCatalog.Merge(registry, rows);
                    HomesError = null;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    HomesError = CityErrors.From(e, false);
                    Homes = registry;
                    Log.Warn("citylife", "GET /homes failed: " + HomesError.Code);
                }
                _lastHomesFetch = Time.realtimeSinceStartupAsDouble;
                if (Profile?.Me != null) CheckRent(Profile.Me);
                HomesChanged?.Invoke();
            }
            catch (OperationCanceledException) { }
            finally { _homesLoading = false; }
        }

        private async Task Loop()
        {
            try
            {
                await RefreshHomes(true);
                while (!Disposed)
                {
                    await Awaitable.WaitForSecondsAsync(1f, _life.Token);
                    if (Disposed) return;
                    if (Time.realtimeSinceStartupAsDouble - _lastProfileRefresh > NeedsRefreshSeconds && Profile?.Me != null)
                    {
                        _lastProfileRefresh = Time.realtimeSinceStartupAsDouble;
                        RefreshProfile(force: true);
                    }
                    if (Time.realtimeSinceStartupAsDouble - _lastHomesFetch > HomesRefreshSeconds) _ = RefreshHomes();
                    foreach (var (need, level) in Needs.NewWarnings(NowMs)) WarnNeed(need, level);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Exception("citylife", e); }
        }

        private void WarnNeed(string need, NeedLevel level)
        {
            var remedy = NeedsProjection.Remedy(need, Needs.Config);
            var fix = remedy != null ? CityLifeStrings.F("needs.remedy", CityLifeStrings.ActionLabel(remedy.Id)) : "";
            string text = level == NeedLevel.Empty ? CityLifeStrings.F("needs.empty", CityLifeStrings.NeedName(need), fix)
                : level == NeedLevel.Critical ? CityLifeStrings.F("needs.critical", CityLifeStrings.NeedName(need), Needs.Config.Critical, fix)
                : CityLifeStrings.T("needs.low." + need);
            Bus.Publish(ToastSignal.Text(text, ToastKind.Bad));
        }

        // ---------------------------------------------------------------- visits

        /// <summary>Knock on <paramref name="ownerId"/>'s door: POST visit → joinById → present. Errors land in <see cref="Visit"/>.</summary>
        public async Task RequestVisit(string ownerId, string ownerName)
        {
            if (Disposed) return;
            var key = Visit.Request(ownerId, ownerName, _user);
            if (key == null) return;
            _visitOp?.Cancel();
            var op = _visitOp = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
            try
            {
                VisitTicket ticket;
                try { ticket = await Api.Visit(ownerId, key, op.Token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { Visit.RequestFailed(CityErrors.From(e, true)); return; }
                Visit.Granted(ticket);
                if (ticket == null) return;
                try
                {
                    int n = await Presenter.Enter(ticket, op.Token);
                    Visit.Joined(n);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    var err = e.Message != null && e.Message.Contains("VENUE_FULL") ? new CityError { Code = "VENUE_FULL", Key = "err.homeFull" }
                        : new CityError { Code = "JOIN_FAILED", Key = "visit.joinFailed", ServerMessage = e.Message };
                    Log.Warn("citylife", "Home room join failed: " + e.Message);
                    Visit.JoinFailed(err);
                }
            }
            catch (OperationCanceledException)
            {
                if (Visit.Stage == VisitStage.Requesting) Visit.RequestFailed(new CityError { Code = "CANCELLED", Key = "err.unknownOutcome", OutcomeUnknown = true, Retryable = true });
                else if (Visit.Stage == VisitStage.Joining) Visit.JoinFailed(null);
            }
        }

        public async Task LeaveVisit()
        {
            if (!Visit.BeginLeave()) return;
            _visitOp?.Cancel();
            try { await Presenter.Leave(); }
            catch (Exception e) { Log.Warn("citylife", "Leave visit: " + e.Message); }
            Visit.Left(unexpected: false);
        }

        private void OnPresence(int n) { if (!Disposed) Visit.PresenceChanged(n); }

        private void OnVisitClosed() { if (!Disposed) Visit.Left(unexpected: true); }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            _visitOp?.Cancel();
            _subs.Dispose();
            Presenter.PresenceChanged -= OnPresence;
            Presenter.Closed -= OnVisitClosed;
            if (Visit.IsInside || Visit.Busy) _ = Presenter.Leave();
            Images.Clear();
            if (ReferenceEquals(_shared, this)) _shared = null;
        }
    }
}
