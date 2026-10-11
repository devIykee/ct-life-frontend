using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Colyseus;
using CTLife.Core;

namespace CTLife.Net
{
    /// <summary>
    /// Owns the Colyseus client. Every client joins `city` and `market` after sign-in and one `venue` at a time
    /// (contract section 5). Join options always carry `{token}`; the venue join adds `venueId`.
    /// Before every join the access token is refreshed if it is near expiry (rooms verify the JWT at join time),
    /// and `city`/`market` are kept joined for the whole session (<see cref="KeepPersistentRoomsAlive"/>).
    /// </summary>
    public sealed class RealtimeService : IRealtimeService
    {
        /// <summary>Maximum SERVER_BUSY retries for one join before the join fails.</summary>
        public const int MaxBusyJoinAttempts = 5;

        private readonly Session _session;
        private Client _client;
        private RoomJoinAttempt _busyOwner;
        private readonly RoomJoinSlot<CityChannel> _city = new RoomJoinSlot<CityChannel>(c => c.IsJoined, c => c.Leave());
        private readonly RoomJoinSlot<MarketChannel> _market = new RoomJoinSlot<MarketChannel>(c => c.IsJoined, c => c.Leave());
        private readonly RoomJoinSlot<VenueChannel> _venue = new RoomJoinSlot<VenueChannel>(c => c.IsJoined, c => c.Leave());
        private CancellationTokenSource _keepAlive;
        private Action _wake;
        public CityChannel City => _city.Current;
        public MarketChannel Market => _market.Current;
        public VenueChannel Venue => _venue.Current;
        public event Action<VenueChannel> VenueJoined;
        public event Action VenueLeft;
        /// <summary>Raised after city or market was rejoined following a drop.</summary>
        public event Action<string> PersistentRoomRejoined;

        /// <summary>Refreshes the access token when stale. Set by the composition root (AuthService.EnsureFresh).</summary>
        public Func<CancellationToken, Task> EnsureFreshToken { get; set; }

        /// <summary>
        /// Extra join options (display name, look, tribe...). The contract's join options are `{token, venueId}`;
        /// backend main @ 9f3e5ef reads these profile fields from the join options instead of the database
        /// (feedback item 38), so they are sent too. A compliant server ignores unknown options.
        /// </summary>
        public Func<IDictionary<string, object>> ExtraJoinOptions { get; set; }

        public RealtimeService(Session session) { _session = session; }

        private Client Client
        {
            get
            {
                if (_client == null)
                {
                    _client = new Client(Env.Current.realtimeUrl);
                    Log.Info("rt", "Colyseus endpoint " + Env.Current.realtimeUrl);
                }
                return _client;
            }
        }

        private async Task<Dictionary<string, object>> Options(CancellationToken ct, string venueId = null)
        {
            if (EnsureFreshToken != null) await EnsureFreshToken(ct);
            ct.ThrowIfCancellationRequested();
            var o = new Dictionary<string, object>();
            try
            {
                var extra = ExtraJoinOptions?.Invoke();
                if (extra != null) foreach (var kv in extra) if (kv.Value != null) o[kv.Key] = kv.Value;
            }
            catch (Exception e) { Log.Warn("rt", "Join options from profile failed: " + e.Message); }
            o["token"] = _session.AccessToken ?? "";
            if (venueId != null) o["venueId"] = venueId;
            return o;
        }

        private bool IdentityCurrent(string user) => _session.IsSignedIn && _session.UserId == user;

        public async Task ConnectCity(CancellationToken ct = default)
        {
            string user = _session.UserId;
            var channel = await _city.Join(user, ct, () => IdentityCurrent(user), true, () => new CityChannel(),
                (candidate, attempt) => JoinWithBusyRetry(async () => candidate.Join(Client, await Options(attempt.Token), attempt), attempt));
            WatchPersistent(channel);
        }

        public async Task ConnectMarket(CancellationToken ct = default)
        {
            string user = _session.UserId;
            var channel = await _market.Join(user, ct, () => IdentityCurrent(user), true, () => new MarketChannel(),
                (candidate, attempt) => JoinWithBusyRetry(async () => candidate.Join(Client, await Options(attempt.Token), attempt), attempt));
            WatchPersistent(channel);
        }

        private void WatchPersistent<T>(RoomChannel<T> channel) where T : Colyseus.Schema.Schema
        {
            if (channel == null) return;
            channel.LostUnexpectedly -= OnPersistentLost;
            channel.LostUnexpectedly += OnPersistentLost;
        }

        private void OnPersistentLost(int code)
        {
            Log.Warn("rt", $"Persistent room lost ({code}); rejoining");
            _wake?.Invoke();
        }

        /// <summary>
        /// Keeps `city` and `market` joined until <paramref name="owner"/> ends or the identity changes: retries a
        /// failed initial join and rejoins after a drop the SDK could not recover, with capped backoff.
        /// </summary>
        public void KeepPersistentRoomsAlive(CancellationToken owner)
        {
            _keepAlive?.Cancel();
            _keepAlive = CancellationTokenSource.CreateLinkedTokenSource(owner);
            _ = KeepAliveLoop(_session.UserId, owner, _keepAlive.Token);
        }

        private async Task KeepAliveLoop(string user, CancellationToken owner, CancellationToken token)
        {
            int failures = 0;
            bool wasMissing = false;
            while (!token.IsCancellationRequested && IdentityCurrent(user))
            {
                bool cityOk = City != null && City.IsJoined, marketOk = Market != null && Market.IsJoined;
                if (cityOk && marketOk)
                {
                    failures = 0;
                    await Sleep(ReconnectBackoff.HealthyCheckSeconds, token);
                    continue;
                }
                try
                {
                    if (!cityOk) await ConnectCity(owner);
                    if (!marketOk) await ConnectMarket(owner);
                    if (wasMissing || failures > 0) PersistentRoomRejoined?.Invoke(!cityOk ? "city" : "market");
                    failures = 0;
                    wasMissing = false;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception e)
                {
                    failures++;
                    wasMissing = true;
                    float wait = ReconnectBackoff.DelaySeconds(failures);
                    Log.Warn("rt", $"Persistent room join failed ({e.Message}); retry {failures} in {wait:0}s");
                    await Sleep(wait, token);
                }
            }
        }

        /// <summary>Waits, but returns early when a persistent room is lost.</summary>
        private async Task Sleep(float seconds, CancellationToken token)
        {
            var woke = new TaskCompletionSource<bool>();
            _wake = () => woke.TrySetResult(true);
            try
            {
                var timer = UnityEngine.Awaitable.WaitForSecondsAsync(seconds, token);
                await Task.WhenAny(woke.Task, AwaitableTask(timer));
            }
            catch (OperationCanceledException) { }
            finally { _wake = null; }
        }

        private static async Task AwaitableTask(UnityEngine.Awaitable awaitable)
        {
            try { await awaitable; } catch (OperationCanceledException) { }
        }

        /// <summary>SERVER_BUSY backoff, capped; cancellation stops its timer/retry. SDK joins drain if already started.</summary>
        private async Task JoinWithBusyRetry(Func<Task<Task>> join, RoomJoinAttempt owner)
        {
            try
            {
                for (int attempt = 1; ; attempt++)
                {
                    owner.ThrowIfObsolete();
                    try
                    {
                        await await join();
                        owner.ThrowIfObsolete();
                        return;
                    }
                    catch (Exception e) when (IsBusy(e) && attempt < MaxBusyJoinAttempts)
                    {
                        owner.ThrowIfObsolete();
                        float wait = RestClient.RetryAfterSeconds(null, attempt);
                        Log.Warn("rt", $"SERVER_BUSY on join, retry {attempt}/{MaxBusyJoinAttempts - 1} in {wait:0}s");
                        _busyOwner = owner;
                        Bus.Publish(new ServerBusySignal { active = true, retryInSeconds = wait, attempt = attempt, source = "realtime" });
                        await UnityEngine.Awaitable.WaitForSecondsAsync(wait, owner.Token);
                    }
                }
            }
            finally
            {
                // A cancelled old retry must not hide a newer realtime attempt's busy indication.
                if (ReferenceEquals(_busyOwner, owner))
                {
                    _busyOwner = null;
                    Bus.Publish(new ServerBusySignal { active = false, source = "realtime" });
                }
            }
        }

        private static bool IsBusy(Exception e) => e.Message != null && (e.Message.Contains("SERVER_BUSY") || e.Message.Contains("503"));

        /// <summary>Replaces only the captured venue room; newer attempts supersede older unfinished joins.</summary>
        public async Task<VenueChannel> JoinVenue(string venueId, CancellationToken ct = default)
        {
            string user = _session.UserId;
            var channel = await _venue.Join(user, ct, () => IdentityCurrent(user), false, () => new VenueChannel(),
                (candidate, attempt) => JoinWithBusyRetry(async () => candidate.Join(Client, await Options(attempt.Token, venueId), attempt), attempt),
                () => VenueLeft?.Invoke());
            if (ReferenceEquals(_venue.Current, channel)) VenueJoined?.Invoke(channel);
            return channel;
        }

        public async Task LeaveVenue()
        {
            var previous = _venue.Detach();
            long revision = _venue.Revision;
            if (previous == null) return;
            await previous.Leave();
            if (_venue.Revision == revision && _venue.Current == null) VenueLeft?.Invoke();
        }

        public async Task DisconnectAll()
        {
            _keepAlive?.Cancel();
            _keepAlive = null;
            // Invalidate every pending slot and capture every published handle before the first asynchronous leave.
            var venue = _venue.Detach();
            long venueRevision = _venue.Revision;
            var market = _market.Detach();
            var city = _city.Detach();
            var retiredVenue = _venue.RetiredCleanup;
            var retiredMarket = _market.RetiredCleanup;
            var retiredCity = _city.RetiredCleanup;
            _client = null;
            if (venue != null)
            {
                await venue.Leave();
                if (_venue.Revision == venueRevision && _venue.Current == null) VenueLeft?.Invoke();
            }
            if (market != null) await market.Leave();
            if (city != null) await city.Leave();
            await Task.WhenAll(retiredVenue, retiredMarket, retiredCity);
        }
    }

    /// <summary>Capped exponential backoff for rejoining persistent rooms (2, 4, 8, 16, 32, then 60 s).</summary>
    public static class ReconnectBackoff
    {
        public const float HealthyCheckSeconds = 5f;
        public const float MaxSeconds = 60f;

        public static float DelaySeconds(int failures)
        {
            if (failures < 1) return 0f;
            double d = 2.0 * Math.Pow(2.0, Math.Min(failures - 1, 10));
            return (float)Math.Min(MaxSeconds, d);
        }
    }

    /// <summary>Profile fields sent with room joins (see <see cref="RealtimeService.ExtraJoinOptions"/>).</summary>
    public static class RealtimeJoinOptions
    {
        public static IDictionary<string, object> FromProfile(PlayerProfile profile)
        {
            var me = profile?.Me;
            if (me == null) return null;
            var o = new Dictionary<string, object>
            {
                ["name"] = me.Character?.Alias ?? "",
                ["tribeId"] = me.Character?.TribeId ?? "",
                ["isSeeker"] = me.User != null && me.User.IsSeeker,
                ["minerTier"] = me.MinerTier.ToString().ToLowerInvariant(),
                ["look"] = profile.LookArray(),
            };
            if (!string.IsNullOrEmpty(me.User?.SkrName)) o["skrName"] = me.User.SkrName;
            return o;
        }
    }
}
