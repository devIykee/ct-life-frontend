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
    /// </summary>
    public sealed class RealtimeService : IRealtimeService
    {
        private readonly Session _session;
        private Client _client;
        private RoomJoinAttempt _busyOwner;
        private readonly RoomJoinSlot<CityChannel> _city = new RoomJoinSlot<CityChannel>(c => c.IsJoined, c => c.Leave());
        private readonly RoomJoinSlot<MarketChannel> _market = new RoomJoinSlot<MarketChannel>(c => c.IsJoined, c => c.Leave());
        private readonly RoomJoinSlot<VenueChannel> _venue = new RoomJoinSlot<VenueChannel>(c => c.IsJoined, c => c.Leave());
        public CityChannel City => _city.Current;
        public MarketChannel Market => _market.Current;
        public VenueChannel Venue => _venue.Current;
        public event Action<VenueChannel> VenueJoined;
        public event Action VenueLeft;

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
        private Dictionary<string, object> Options(string venueId = null)
        {
            var o = new Dictionary<string, object> { ["token"] = _session.AccessToken ?? "" };
            if (venueId != null) o["venueId"] = venueId;
            return o;
        }
        private bool IdentityCurrent(string user) => _session.IsSignedIn && _session.UserId == user;

        public async Task ConnectCity(CancellationToken ct = default)
        {
            string user = _session.UserId;
            await _city.Join(user, ct, () => IdentityCurrent(user), true, () => new CityChannel(),
                (channel, attempt) => JoinWithBusyRetry(() => channel.Join(Client, Options(), attempt), attempt));
        }
        public async Task ConnectMarket(CancellationToken ct = default)
        {
            string user = _session.UserId;
            await _market.Join(user, ct, () => IdentityCurrent(user), true, () => new MarketChannel(),
                (channel, attempt) => JoinWithBusyRetry(() => channel.Join(Client, Options(), attempt), attempt));
        }

        /// <summary>Existing SERVER_BUSY backoff; cancellation stops its timer/retry. SDK joins drain if already started.</summary>
        private async Task JoinWithBusyRetry(Func<Task> join, RoomJoinAttempt owner)
        {
            try
            {
                for (int attempt = 1; ; attempt++)
                {
                    owner.ThrowIfObsolete();
                    try
                    {
                        await join();
                        owner.ThrowIfObsolete();
                        return;
                    }
                    catch (Exception e) when (IsBusy(e))
                    {
                        owner.ThrowIfObsolete();
                        float wait = RestClient.RetryAfterSeconds(null, attempt);
                        Log.Warn("rt", $"SERVER_BUSY on join, retry {attempt} in {wait:0}s");
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
                (candidate, attempt) => JoinWithBusyRetry(() => candidate.Join(Client, Options(venueId), attempt), attempt),
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
}
