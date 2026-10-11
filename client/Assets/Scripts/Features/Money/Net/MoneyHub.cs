using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using UnityEngine;

namespace CTLife.Economy
{
    /// <summary>ServerClock time and Unity-thread delays.</summary>
    public sealed class UnityMoneyClock : IMoneyClock
    {
        public double NowMs => ServerClock.NowMs;
        public async Task Delay(int milliseconds, CancellationToken ct) => await Awaitable.WaitForSecondsAsync(milliseconds / 1000f, ct);
    }

    /// <summary>The in-flight top-up per environment and user, in PlayerPrefs (ids and display values only; no tokens, no tx).</summary>
    public sealed class PrefsPendingIntentStore : IPendingIntentStore
    {
        private readonly string _key;

        public PrefsPendingIntentStore(string userId)
        {
            _key = $"ctlife.{Env.Current.id}.topupIntent.{userId}";
        }

        public string Load() => PlayerPrefs.GetString(_key, "");

        public void Save(string json)
        {
            PlayerPrefs.SetString(_key, json ?? "");
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(_key);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Shared Phase 2 state for the money apps while the phone exists: the API wrapper, the market feed, the top-up flow
    /// and the city-room `payment_confirmed` handling (which must work while the Bank app is closed). Reference-counted by
    /// the apps that use it; the last release disposes it (PhoneShell disposes IDisposable apps on teardown).
    /// </summary>
    public sealed class MoneyHub : IDisposable
    {
        private static MoneyHub _shared;
        private int _refs;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly string _user;
        private double _lastProfileRefresh = -10;
        private bool _profileRefreshQueued;

        public readonly MoneyApi Api;
        public readonly MarketFeed Market;
        public readonly TopUpFlow TopUp;
        public CancellationToken Life => _life.Token;
        public bool Disposed { get; private set; }

        /// <summary>The live hub, or null before the phone is built / after sign-out.</summary>
        public static MoneyHub Current => _shared != null && !_shared.Disposed ? _shared : null;

        public static MoneyHub Acquire()
        {
            var session = Services.Get<Session>();
            if (_shared == null || _shared.Disposed || _shared._user != session.UserId) _shared = new MoneyHub(session.UserId);
            _shared._refs++;
            return _shared;
        }

        public void Release()
        {
            if (Disposed) return;
            if (--_refs <= 0) Dispose();
        }

        private MoneyHub(string userId)
        {
            _user = userId;
            Api = new MoneyApi(Services.Get<IRestClient>());
            Market = new MarketFeed(Api);
            Services.TryGet<ITransactionSigner>(out var signer);
            TopUp = new TopUpFlow(Api, signer, new UnityMoneyClock(), string.IsNullOrEmpty(userId) ? null : new PrefsPendingIntentStore(userId));
            _subs.On<PaymentConfirmedMessage>(OnPaymentConfirmed);
            _subs.On<LiquidatedMessage>(_ => RefreshProfile());
            TopUp.Resume();
        }

        private void OnPaymentConfirmed(PaymentConfirmedMessage m)
        {
            if (Disposed || m == null) return;
            TopUp.OnPaymentConfirmed(m.intentId, m.cashCents);
            Bus.Publish(ToastSignal.Id("toast_payment_confirmed", new Dictionary<string, string> { ["cashCents"] = m.cashCents.ToString(System.Globalization.CultureInfo.InvariantCulture) }, ToastKind.Good));
            Bus.Publish(new SfxSignal("sfx_ui_success"));
            RefreshProfile(force: true);
        }

        /// <summary>GET /me after money moved (Cash is server-authoritative). Coalesced to one call per 2 s.</summary>
        public async void RefreshProfile(bool force = false)
        {
            if (Disposed || !Services.TryGet<PlayerProfile>(out var profile)) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!force && now - _lastProfileRefresh < 2)
            {
                if (_profileRefreshQueued) return;
                _profileRefreshQueued = true;
                try { await Awaitable.WaitForSecondsAsync(2f, _life.Token); }
                catch (OperationCanceledException) { return; }
                _profileRefreshQueued = false;
            }
            _lastProfileRefresh = Time.realtimeSinceStartupAsDouble;
            try { await profile.Refresh(_life.Token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("money", "Profile refresh failed: " + (e is ApiException a ? a.Code : e.GetType().Name)); }
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            _subs.Dispose();
            Market.Dispose();
            if (ReferenceEquals(_shared, this)) _shared = null;
        }
    }
}
