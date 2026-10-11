using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Economy;
using CTLife.Net;
using CTLife.Realtime;
using UnityEngine;

namespace CTLife.Ventures
{
    /// <summary>
    /// Puts a project HQ room on screen. Registered by the composition root (hook in docs/progress/requests.md); without one the
    /// HQ screen still shows the project and says the 3D room is not wired in this build.
    /// </summary>
    public interface IProjectHqPresenter
    {
        Task Enter(RoomTicket ticket, string projectId, string projectName, CancellationToken ct);
        Task Leave();
    }

    /// <summary>
    /// Shared Phase 5 state while the phone exists: both API wrappers, the "my bookings" board, the ad payment flow (Money's
    /// <see cref="TopUpFlow"/>), and the realtime handling that must work while the apps are closed (ad status toasts, VC offers,
    /// VC dumps, hire offers). Reference-counted by the apps; the last release disposes it.
    /// </summary>
    public sealed class VenturesHub : IDisposable
    {
        private static VenturesHub _shared;
        private int _refs;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly string _user;
        private double _lastProfileRefresh = -10;
        private bool _boardLoading;

        public readonly AdsApi Ads;
        public readonly ProjectsApi Projects;
        public readonly AdBoard Board = new AdBoard();
        public readonly TopUpFlow Payment;
        public readonly AdIntentGateway PaymentIntents;
        public readonly CreativeLimits Limits;
        public readonly IProjectHqPresenter Hq;
        public VError BoardError { get; private set; }
        public bool BoardLoaded { get; private set; }
        public bool Disposed { get; private set; }
        public CancellationToken Life => _life.Token;
        public double NowMs => ServerClock.NowMs;
        public string UserId => _user;

        /// <summary>The board, a booking or a pushed offer changed (UI re-renders).</summary>
        public event Action Changed;
        public event Action<AdsChangedSignal> AdsChanged;
        public event Action<VcOfferSignal> VcOffered;
        public event Action<HireOfferSignal> HireOffered;
        public event Action<ProjectEventSignal> ProjectEvented;
        public event Action<VcDumpSignal> VcDumped;

        public static VenturesHub Current => _shared != null && !_shared.Disposed ? _shared : null;

        public static VenturesHub Acquire()
        {
            var session = Services.Get<Session>();
            if (_shared == null || _shared.Disposed || _shared._user != session.UserId) _shared = new VenturesHub(session.UserId);
            _shared._refs++;
            return _shared;
        }

        public void Release()
        {
            if (Disposed) return;
            if (--_refs <= 0) Dispose();
        }

        private VenturesHub(string userId)
        {
            _user = userId;
            var rest = Services.Get<IRestClient>();
            Ads = new AdsApi(rest, Services.TryGet<IMultipartClient>(out var mp) ? mp : new UnityMultipartClient());
            Projects = new ProjectsApi(rest);
            Hq = Services.TryGet<IProjectHqPresenter>(out var hq) ? hq : null;
            Limits = CreativeLimits.FromConfig(Services.TryGet<ConfigStore>(out var cfg) ? cfg.Config("ads") : null);
            PaymentIntents = new AdIntentGateway(new MoneyApi(rest));
            Services.TryGet<ITransactionSigner>(out var signer);
            // A separate store key from the Bank app's top-up: a restored ad payment must never be shown as a Cash top-up.
            Payment = new TopUpFlow(PaymentIntents, signer, new UnityMoneyClock(), string.IsNullOrEmpty(userId) ? null : new PrefsPendingIntentStore(userId + ".ads"));
            _subs.On<AdBookingUpdateSignal>(OnBookingUpdate);
            _subs.On<AdsChangedSignal>(s => { if (!Disposed) AdsChanged?.Invoke(s); });
            _subs.On<VcOfferSignal>(OnVcOffer);
            _subs.On<HireOfferSignal>(OnHireOffer);
            _subs.On<ProjectEventSignal>(OnProjectEvent);
            _subs.On<EventMessage>(OnCityEvent);
            _subs.On<PaymentConfirmedMessage>(m => { if (!Disposed && m != null && Payment.OnPaymentConfirmed(m.intentId, m.cashCents)) { _ = RefreshBoard(); Changed?.Invoke(); } });
            Payment.Resume();
        }

        // ---------------------------------------------------------------- bookings board

        /// <summary>GET /ads/bookings (first page). Errors are kept on <see cref="BoardError"/>; a missing route is UNAVAILABLE.</summary>
        public async Task RefreshBoard()
        {
            if (Disposed || _boardLoading) return;
            _boardLoading = true;
            try
            {
                var page = await Ads.Bookings(null, _life.Token);
                Board.Replace(page);
                BoardError = null;
                BoardLoaded = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                BoardError = VErrors.From(e, false);
                Log.Warn("ventures", "GET /ads/bookings failed: " + BoardError.Code);
            }
            finally { _boardLoading = false; }
            if (!Disposed) Changed?.Invoke();
        }

        public async Task LoadMoreBoard()
        {
            if (Disposed || _boardLoading || string.IsNullOrEmpty(Board.NextCursor)) return;
            _boardLoading = true;
            try
            {
                Board.Append(await Ads.Bookings(Board.NextCursor, _life.Token));
                BoardError = null;
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { BoardError = VErrors.From(e, false); }
            finally { _boardLoading = false; }
            if (!Disposed) Changed?.Invoke();
        }

        /// <summary>A booking was created or cancelled by this client: put the server's copy on the board.</summary>
        public void Upsert(AdBooking b)
        {
            if (b == null) return;
            Board.Upsert(b);
            Changed?.Invoke();
        }

        private async void OnBookingUpdate(AdBookingUpdateSignal s)
        {
            if (Disposed) return;
            var result = Board.Apply(s.bookingId, s.status, s.reasonId);
            Bus.Publish(ToastSignal.Text(VStrings.F("toast.ad." + AdStatuses.Wire(s.status), ReasonText(s.reasonId)), ToastToneOf(s.status)));
            Bus.Publish(new SfxSignal(s.status == AdStatus.Rejected ? "sfx_ui_error" : "sfx_ui_success"));
            Changed?.Invoke();
            if (result == AdBoard.UpdateResult.Unknown || result == AdBoard.UpdateResult.Illegal)
            {
                // The local copy is stale (a booking made on another device, or a missed step): re-read, do not guess.
                try { await RefreshBoard(); }
                catch (Exception e) { Log.Warn("ventures", "Resync failed: " + e.GetType().Name); }
            }
            else if (s.status == AdStatus.Rejected || s.status == AdStatus.Refunded)
            {
                // The refund detail (status, amount) is not in the push; one cheap read fills it in.
                try { Upsert(await Ads.Booking(s.bookingId, _life.Token)); }
                catch (OperationCanceledException) { }
                catch (Exception e) { Log.Warn("ventures", "Booking read failed: " + e.GetType().Name); }
            }
        }

        public static ToastKind ToastToneOf(AdStatus s) =>
            s == AdStatus.Rejected ? ToastKind.Bad : s == AdStatus.Live || s == AdStatus.Scheduled || s == AdStatus.Refunded ? ToastKind.Good : ToastKind.Info;

        /// <summary>Label for a rejection reason id: the registry's label when the server's registries carry it, else the id made readable.</summary>
        public static string ReasonText(string reasonId)
        {
            if (string.IsNullOrEmpty(reasonId)) return "";
            if (Services.TryGet<ConfigStore>(out var cfg))
            {
                var label = VenturesRegistry.ReasonLabel(cfg.Ids("ad_rejection_reasons"), reasonId);
                if (label != null) return label;
            }
            return VWire.Readable(reasonId.StartsWith("ad_", StringComparison.Ordinal) ? reasonId.Substring(3) : reasonId);
        }

        // ---------------------------------------------------------------- pushes

        private void OnVcOffer(VcOfferSignal s)
        {
            if (Disposed) return;
            Bus.Publish(ToastSignal.Text(VStrings.F("toast.vcOffer", VStrings.VcName(s.offer.VcArchetypeId), Money.Cash(s.offer.InvestCents), ProjectRules.EquityText(s.offer.EquityBps)), ToastKind.Event));
            VcOffered?.Invoke(s);
        }

        private void OnHireOffer(HireOfferSignal s)
        {
            if (Disposed) return;
            Bus.Publish(ToastSignal.Text(VStrings.F("toast.hireOffer", s.offer.ProjectName, VStrings.Role(s.offer.Role)), ToastKind.Event));
            HireOffered?.Invoke(s);
        }

        private void OnProjectEvent(ProjectEventSignal s)
        {
            if (Disposed) return;
            ProjectEvented?.Invoke(s);
            Bus.Publish(ToastSignal.Text(VStrings.ProjectEventLine(s.projectEvent), s.projectEvent.CashDeltaCents < 0 ? ToastKind.Bad : ToastKind.Info));
            RefreshProfile();
        }

        private void OnCityEvent(EventMessage m)
        {
            if (Disposed || m == null) return;
            if (VenturesRealtime.TryParseVcDump(m.eventId, m.@params, out var dump))
            {
                Bus.Publish(ToastSignal.Text(VStrings.F("toast.vcDump", string.IsNullOrEmpty(dump.name) ? VStrings.T("project.someProject") : dump.name), ToastKind.Event));
                VcDumped?.Invoke(dump);
            }
        }

        /// <summary>GET /me after Cash moved. Coalesced to one call per 2 s.</summary>
        public async void RefreshProfile(bool force = false)
        {
            if (Disposed || !Services.TryGet<PlayerProfile>(out var profile)) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!force && now - _lastProfileRefresh < 2) return;
            _lastProfileRefresh = now;
            try { await profile.Refresh(_life.Token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("ventures", "Profile refresh failed: " + (e is ApiException a ? a.Code : e.GetType().Name)); }
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            _subs.Dispose();
            if (ReferenceEquals(_shared, this)) _shared = null;
        }
    }
}
