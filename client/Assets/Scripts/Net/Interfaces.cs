using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Rest.Model;

namespace CTLife.Net
{
    // The network layer sits behind these interfaces so the transport and contract bindings can be swapped
    // (the backend developer's imported contract-v1.0.0-rc1; see contract/SOURCE.md)
    // without touching World/UI code. Consumers resolve them with Services.Get<IRestClient>() etc.

    /// <summary>HTTP transport: auth header, version headers, Idempotency-Key, retries, error mapping.</summary>
    public interface IRestClient
    {
        Func<Task<bool>> RefreshHandler { get; set; }
        Task<T> Get<T>(string path, CancellationToken ct = default);
        Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default);
        Task<T> Put<T>(string path, object body, CancellationToken ct = default);
        Task<T> Patch<T>(string path, object body, CancellationToken ct = default);
        Task<T> Delete<T>(string path, CancellationToken ct = default);
        Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default);
        Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct);
    }

    /// <summary>Sign-in, session refresh and sign-out (contract section 3).</summary>
    public interface IAuthService
    {
        bool DevAuthAllowed { get; }
        Task<AuthResult> SignInWithWallet(IWalletSignIn wallet, CancellationToken ct = default);
        Task<AuthResult> SignInDev(string wallet, bool isSeeker = false, CancellationToken ct = default);
        Task<bool> TryResume(CancellationToken ct = default);
        Task<bool> Refresh();
        Task EnsureFresh(CancellationToken ct = default);
        Task SignOut(IWalletSignIn wallet);
    }

    /// <summary>Realtime rooms: one city, one market, one venue at a time (contract section 5).</summary>
    public interface IRealtimeService
    {
        CityChannel City { get; }
        MarketChannel Market { get; }
        VenueChannel Venue { get; }
        event Action<VenueChannel> VenueJoined;
        /// <summary>Keep city and market joined (retry/rejoin with backoff) until the owner token ends.</summary>
        void KeepPersistentRoomsAlive(CancellationToken owner);
        event Action VenueLeft;
        Task ConnectCity(CancellationToken ct = default);
        Task ConnectMarket(CancellationToken ct = default);
        Task<VenueChannel> JoinVenue(string venueId, CancellationToken ct = default);
        /// <summary>
        /// Open-world handoff (Phase B): join <paramref name="venueId"/>'s room while the current one stays joined, then make
        /// the new room current and leave the old one, so the player never drops out of presence between zones.
        /// </summary>
        Task<VenueChannel> JoinVenueHandoff(string venueId, CancellationToken ct = default);
        Task LeaveVenue();
        Task DisconnectAll();
    }
}
