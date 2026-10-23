using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using Newtonsoft.Json.Linq;

namespace CTLife.Net
{
    /// <summary>
    /// Fills <see cref="LocalEntitlements"/> from GET /v1/me/entitlements (`{ items: string[], updatedAt }`,
    /// docs/fe-contract-proposals/entitlements-20261022.md) once the player is signed in, and again on later profile
    /// refreshes (at most once a minute; a purchase receipt can call <see cref="Refresh"/>). A 404 means the backend has not
    /// deployed it yet: the local stand-in stays as it is. Any other failure also keeps the last known set.
    /// </summary>
    public sealed class EntitlementsSync : IDisposable
    {
        private readonly IRestClient _rest;
        private readonly LocalEntitlements _target;
        private readonly Subscriptions _subs = new Subscriptions();
        private DateTime _lastFetch = DateTime.MinValue;
        private bool _missing, _busy, _disposed;

        public EntitlementsSync(IRestClient rest, LocalEntitlements target)
        {
            _rest = rest; _target = target;
            _subs.On<ProfileChangedSignal>(s => { if ((DateTime.UtcNow - _lastFetch).TotalSeconds >= 60) _ = Refresh(); });
        }

        public async Task Refresh(CancellationToken ct = default)
        {
            if (_disposed || _missing || _busy || _rest == null || _target == null) return;
            if (!Services.TryGet<PlayerProfile>(out var profile) || profile.Me == null) return;   // not signed in yet
            _busy = true; _lastFetch = DateTime.UtcNow;
            try
            {
                var body = await _rest.Get<JToken>("/me/entitlements", ct);
                if (_disposed) return;
                var items = body?["items"] as JArray;
                if (items == null) return;
                var owned = new System.Collections.Generic.List<string>(items.Count);
                foreach (var i in items) if (i.Type == JTokenType.String) owned.Add((string)i);
                _target.Set(owned);
            }
            catch (ApiException e) when (e.Status == 404)
            {
                _missing = true;   // not deployed yet: keep the local stand-in, don't ask again this session
                Log.Info("entitlements", "GET /me/entitlements is not deployed (404); keeping the local stand-in.");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("entitlements", "Entitlements fetch failed: " + e.Message); }
            finally { _busy = false; }
        }

        public void Dispose() { _disposed = true; _subs.Dispose(); }
    }
}
