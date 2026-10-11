using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// Per environment + user device memory for what the contract cannot read back: follows and blocks (no GET), DM
    /// conversations known to this device and accepted requests (GET /dms may be unavailable), the last creator settings
    /// saved (no GET /creator), and an in-flight paid call (so a call funded while the app was killed in the wallet is
    /// resumed and polled, never re-signed). Ids and display values only; never tokens or transactions.
    /// </summary>
    public sealed class SocialStore
    {
        private readonly string _prefix;

        public SocialStore(string userId) { _prefix = $"ctlife.{Env.Current?.id}.social.{userId}."; }

        public string Get(string key) => PlayerPrefs.GetString(_prefix + key, "");

        public void Set(string key, string value)
        {
            PlayerPrefs.SetString(_prefix + key, value ?? "");
            PlayerPrefs.Save();
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(_prefix + key);
            PlayerPrefs.Save();
        }

        public JObject GetJson(string key)
        {
            try { return JToken.Parse(Get(key)) as JObject; }
            catch { return null; }
        }

        public void SetJson(string key, JObject value) => Set(key, value?.ToString(Newtonsoft.Json.Formatting.None));
    }

    /// <summary>userId → alias via GET /users/{id}, cached for the session; failures fall back to a short id.</summary>
    public sealed class NameCache
    {
        private readonly SocialApi _api;
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _inFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationToken _life;
        public event Action Changed;

        public NameCache(SocialApi api, CancellationToken life) { _api = api; _life = life; }

        public static string Short(string id) => string.IsNullOrEmpty(id) ? "?" : SocialStrings.F("common.player", id.Length > 6 ? id.Substring(0, 6) : id);

        public void Remember(string id, string alias)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(alias)) return;
            _names[id] = alias;
        }

        /// <summary>The alias if known, else a short-id placeholder (and a lookup is started).</summary>
        public string Name(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            if (_names.TryGetValue(id, out var n)) return n;
            Lookup(id);
            return Short(id);
        }

        private async void Lookup(string id)
        {
            if (!SocialWire.IsSafeId(id) || !_inFlight.Add(id)) return;
            try
            {
                var user = await _api.User(id, _life);
                if (!string.IsNullOrWhiteSpace(user.Alias)) { _names[id] = user.Alias; Changed?.Invoke(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("social", "Name lookup failed: " + SocialError.From(e).Code); _names[id] = Short(id); }
            finally { _inFlight.Remove(id); }
        }
    }

    /// <summary>
    /// Shared Phase 3 state while signed in: the API, device memory, names, and the three controllers (Spaces, calls,
    /// messages/timeline/tips). It listens to the city room's `space_update`, `dm` and `call_invite` (published on the Bus
    /// by CTLife.Net.CityChannel) even while every social app is closed, ticks 4 times a second for timers and polling, and
    /// watches connectivity (offline → online, Wi-Fi ↔ mobile) to reconnect media and refetch.
    /// Reference-counted by the apps and the overlay; one per signed-in user.
    /// </summary>
    public sealed class SocialHub : IDisposable
    {
        private static SocialHub _shared;
        private int _refs;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private double _nextTick;
        private NetworkReachability _reach;

        public readonly string Me;
        public readonly SocialApi Api;
        public readonly SocialStore Store;
        public readonly NameCache Names;
        public readonly IdSet Follows = new IdSet();
        public readonly IdSet Blocks = new IdSet();
        public readonly SpacesController Spaces;
        public readonly CallController Calls;
        public readonly MessagesController Messages;
        public readonly TimelineController Timeline;
        public bool Disposed { get; private set; }
        public bool Offline => _reach == NetworkReachability.NotReachable;
        public CancellationToken Life => _life.Token;
        public event Action Changed;

        /// <summary>Monotonic milliseconds for timers (not wall clock).</summary>
        public static double NowMs => Time.realtimeSinceStartupAsDouble * 1000.0;

        public static SocialHub Current => _shared != null && !_shared.Disposed ? _shared : null;

        /// <summary>The hub for the signed-in user (created on first use), or null when signed out.</summary>
        public static SocialHub Acquire()
        {
            if (!Services.TryGet<Session>(out var session) || !session.IsSignedIn || string.IsNullOrEmpty(session.UserId)) return null;
            if (_shared != null && !_shared.Disposed && _shared.Me != session.UserId) _shared.Dispose();
            if (_shared == null || _shared.Disposed) _shared = new SocialHub(session.UserId);
            _shared._refs++;
            return _shared;
        }

        public void Release()
        {
            if (Disposed) return;
            if (--_refs <= 0) Dispose();
        }

        private SocialHub(string me)
        {
            Me = me;
            Api = new SocialApi(Services.Get<IRestClient>());
            Store = new SocialStore(me);
            Names = new NameCache(Api, _life.Token);
            Follows.Load(Store.Get("follows"));
            Blocks.Load(Store.Get("blocks"));
            Follows.Changed += () => Store.Set("follows", Follows.Serialize());
            Blocks.Changed += () => Store.Set("blocks", Blocks.Serialize());
            Names.Changed += () => Changed?.Invoke();
            Spaces = new SpacesController(this);
            Calls = new CallController(this);
            Messages = new MessagesController(this);
            Timeline = new TimelineController(this);
            _subs.On<SpaceUpdateMessage>(m => Guard(() => Spaces.OnSpaceUpdate(m?.spaceId)));
            _subs.On<DmMessage>(m => Guard(() => Messages.OnRealtime(m)));
            _subs.On<CallInviteMessage>(m => Guard(() => Calls.OnInvite(m)));
            _subs.On<DegradedChangedSignal>(_ => Guard(() => { Spaces.OnDegraded(); Calls.OnDegraded(); Changed?.Invoke(); }));
            _reach = Application.internetReachability;
            _ = Runner.Instance;
            Runner.Ticked += Tick;
            if (Services.TryGet<Session>(out var session)) session.Changed += OnSession;
        }

        private void OnSession()
        {
            if (Disposed) return;
            if (!Services.TryGet<Session>(out var s) || !s.IsSignedIn || s.UserId != Me) Dispose();
        }

        private void Tick()
        {
            if (Disposed) return;
            var now = NowMs;
            if (now < _nextTick) return;
            _nextTick = now + 250;
            var reach = Application.internetReachability;
            if (reach != _reach)
            {
                var wasOffline = _reach == NetworkReachability.NotReachable;
                _reach = reach;
                Log.Info("social", "Network changed: " + reach);
                if (reach != NetworkReachability.NotReachable)
                    Guard(() => { Spaces.OnNetworkChanged(now); Calls.OnNetworkChanged(now); if (wasOffline) { Messages.OnOnline(); Timeline.OnOnline(); } });
                Changed?.Invoke();
            }
            Guard(() => Spaces.Tick(now));
            Guard(() => Calls.Tick(now));
            Guard(() => Messages.Tick(now));
            Guard(() => Timeline.Tick(now));
        }

        private static void Guard(Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception("social", e); }
        }

        /// <summary>Runs async work bound to the hub. Errors are classified and passed to <paramref name="fail"/>.</summary>
        internal async void Run(Func<CancellationToken, Task> work, Action<SocialError> fail)
        {
            if (Disposed) return;
            try { await work(_life.Token); }
            catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
            catch (Exception e)
            {
                if (Disposed) return;
                var err = SocialError.From(e);
                Log.Warn("social", "Request failed: " + err.Code + " / " + err.Status);
                try { fail?.Invoke(err); }
                catch (Exception inner) { Log.Exception("social", inner); }
            }
        }

        public void NotifyChanged() { if (!Disposed) Changed?.Invoke(); }

        /// <summary>Blocks on the server (POST /blocks/{userId}) and hides the user everywhere in the social apps.</summary>
        public void Block(string userId, Action<SocialError> done)
        {
            if (string.IsNullOrEmpty(userId) || SocialWire.SameId(userId, Me)) return;
            var key = new RetainedKey();
            Run(async ct =>
            {
                await Api.Block(userId, key, ct);
                Blocks.Set(userId, true);
                Messages.OnBlocked(userId);
                Bus.Publish(ToastSignal.Text(SocialStrings.F("block.done", Names.Name(userId)), ToastKind.Good));
                Changed?.Invoke();
                done?.Invoke(null);
            }, e => done?.Invoke(e));
        }

        public void Report(string targetType, string targetId, string reasonId, Action<SocialError> done)
        {
            var key = new RetainedKey();
            Run(async ct =>
            {
                await Api.Report(targetType, targetId, reasonId, key, ct);
                Bus.Publish(ToastSignal.Text(SocialStrings.T("report.sent"), ToastKind.Good));
                done?.Invoke(null);
            }, e => done?.Invoke(e));
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            Runner.Ticked -= Tick;
            _subs.Dispose();
            if (Services.TryGet<Session>(out var session)) session.Changed -= OnSession;
            Guard(Spaces.Dispose);
            Guard(Calls.Dispose);
            Guard(Messages.Dispose);
            Guard(Timeline.Dispose);
            if (ReferenceEquals(_shared, this)) _shared = null;
            Changed?.Invoke();
        }
    }
}
