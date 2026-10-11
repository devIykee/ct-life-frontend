using System;
using System.Collections.Generic;
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

        public CityChannel City { get; private set; }
        public MarketChannel Market { get; private set; }
        public VenueChannel Venue { get; private set; }

        public event Action<VenueChannel> VenueJoined;
        public event Action VenueLeft;

        public RealtimeService(Session session)
        {
            _session = session;
        }

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

        public async Task ConnectCity()
        {
            if (City != null && City.IsJoined) return;
            var c = new CityChannel();
            await JoinWithBusyRetry(() => c.Join(Client, Options()));
            City = c;
        }

        public async Task ConnectMarket()
        {
            if (Market != null && Market.IsJoined) return;
            var m = new MarketChannel();
            await JoinWithBusyRetry(() => m.Join(Client, Options()));
            Market = m;
        }

        /// <summary>
        /// Matchmaking can answer SERVER_BUSY (additive contract change). The SDK only gives us the message, so
        /// back off 2, 4, 8… s (max 30 s) and retry until it clears; other errors propagate.
        /// </summary>
        private static async Task JoinWithBusyRetry(Func<Task> join)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await join();
                    if (attempt > 1) Bus.Publish(new ServerBusySignal { active = false, source = "realtime" });
                    return;
                }
                catch (Exception e) when (IsBusy(e))
                {
                    float wait = RestClient.RetryAfterSeconds(null, attempt);
                    Log.Warn("rt", $"SERVER_BUSY on join, retry {attempt} in {wait:0}s");
                    Bus.Publish(new ServerBusySignal { active = true, retryInSeconds = wait, attempt = attempt, source = "realtime" });
                    await UnityEngine.Awaitable.WaitForSecondsAsync(wait);
                }
            }
        }

        private static bool IsBusy(Exception e) =>
            e.Message != null && (e.Message.Contains("SERVER_BUSY") || e.Message.Contains("503"));

        /// <summary>Leaves the current venue (if any) and joins `venueId`.</summary>
        public async Task<VenueChannel> JoinVenue(string venueId)
        {
            await LeaveVenue();
            var v = new VenueChannel();
            await JoinWithBusyRetry(() => v.Join(Client, Options(venueId)));
            Venue = v;
            VenueJoined?.Invoke(v);
            return v;
        }

        public async Task LeaveVenue()
        {
            if (Venue == null) return;
            var v = Venue;
            Venue = null;
            await v.Leave();
            VenueLeft?.Invoke();
        }

        public async Task DisconnectAll()
        {
            await LeaveVenue();
            if (Market != null) { await Market.Leave(); Market = null; }
            if (City != null) { await City.Leave(); City = null; }
            _client = null;
        }
    }
}
