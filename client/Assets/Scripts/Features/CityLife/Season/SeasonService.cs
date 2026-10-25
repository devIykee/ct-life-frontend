using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI.Kit;
using UnityEngine;

namespace CTLife.CityLife.Season
{
    /// <summary>
    /// The season and the nods book for one signed-in player (backend #13):
    /// - `GET /season` (both tracks, my XP and tier) and `POST /season/tiers/{n}/claim`;
    /// - `GET /nods` (the book; which nods are found, so the world hides them) and `POST /nods/{id}/collect` from the world's
    ///   Collect prompt: the server decides from its own fresh street position within 15 m (409 NOD_TOO_FAR / POSITION_UNKNOWN,
    ///   429 on probing), so the client only sends the nod id and where it thinks the player is;
    /// - `GET /nods/leaderboard` (the Hunter board).
    /// The season UI (pending the user's OK on the reference brief) reads this; the world's nods read <see cref="IsFound"/> and
    /// call <see cref="Collect"/>.
    /// </summary>
    public sealed class SeasonService : IDisposable
    {
        public static SeasonService Current { get; private set; }
        public readonly SeasonApi Api;
        public string UserId { get; }
        public SeasonView Season { get; private set; }
        public NodsBook Book { get; private set; }
        public bool Disposed { get; private set; }
        public event Action SeasonChanged, BookChanged;
        /// <summary>A new find (not a repeat): the world plays the pickup and hides the nod; the UI toasts the XP / set reward.</summary>
        public event Action<NodCollect> Collected;

        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly HashSet<string> _collecting = new HashSet<string>();
        private readonly Dictionary<string, string> _collectKeys = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _claimKeys = new Dictionary<string, string>();

        public SeasonService(string userId)
        {
            UserId = userId;
            Api = new SeasonApi(Services.Get<IRestClient>());
            Current = this;
            _ = RefreshBook();
            _ = RefreshSeason();
        }

        public CancellationToken Life => _life.Token;
        public bool IsFound(string nodId) => Book != null && Book.IsFound(nodId);

        public async Task<NodsBook> RefreshBook()
        {
            if (Disposed || RuntimeMode.IsOfflineDemo) return Book;
            try
            {
                var b = await Api.Book(_life.Token);
                if (Disposed) return b;
                Book = b;
                Log.Info("season", $"nods book {b.Collected}/{b.Total}");
                BookChanged?.Invoke();
                CTLife.World.NodHooks.NotifyFoundRefreshed();
                return b;
            }
            catch (OperationCanceledException) { return Book; }
            catch (Exception e) { Log.Warn("season", "GET /nods failed: " + CityErrors.From(e, false).Code); return Book; }
        }

        public async Task<SeasonView> RefreshSeason()
        {
            if (Disposed || RuntimeMode.IsOfflineDemo) return Season;
            try
            {
                var s = await Api.Season(_life.Token);
                if (Disposed) return s;
                Season = s;
                Log.Info("season", $"season {s.SeasonId} xp {s.Xp} tier {s.Tier} pass {s.HasPass} claimable {s.ClaimableCount}");
                SeasonChanged?.Invoke();
                return s;
            }
            catch (OperationCanceledException) { return Season; }
            catch (Exception e) { Log.Warn("season", "GET /season failed: " + CityErrors.From(e, false).Code); return Season; }
        }

        /// <summary>The world's Collect prompt. Returns the find, or null on a refusal (a friendly toast says why).</summary>
        public async Task<NodCollect> Collect(string nodId, Vector3 playerPos)
        {
            if (Disposed || string.IsNullOrEmpty(nodId) || _collecting.Contains(nodId)) return null;
            _collecting.Add(nodId);
            if (!_collectKeys.TryGetValue(nodId, out var key)) _collectKeys[nodId] = key = Guid.NewGuid().ToString();
            try
            {
                var r = await Api.Collect(nodId, playerPos.x, playerPos.z, key, _life.Token);
                _collectKeys.Remove(nodId);
                Log.Info("season", $"nod {nodId} collected (repeat {r.AlreadyCollected}) xp {r.Xp} set {r.SetId} {r.SetCollected}/{r.SetTotal}{(r.SetRewardCosmeticId != null ? " reward " + r.SetRewardCosmeticId : "")}");
                if (!r.AlreadyCollected)
                {
                    Book?.MarkFound(nodId, (long)ServerClock.NowMs);
                    BookChanged?.Invoke();
                    CTLife.World.NodHooks.NotifyCollected(nodId);
                    Collected?.Invoke(r);
                    _ = RefreshSeason();
                }
                else CTLife.World.NodHooks.NotifyCollected(nodId);   // a repeat: the world hides it too
                return r;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception e)
            {
                var err = CityErrors.From(e, true);
                if (!err.OutcomeUnknown) _collectKeys.Remove(nodId);
                Log.Warn("season", $"nod {nodId} collect refused: {err.Code}" + (err.Code == "ERROR" ? " (" + e.GetType().Name + ": " + e.Message + ")" : ""));
                string msg = err.Is("NOD_TOO_FAR") ? SeasonText.T("nod.tooFar")
                    : err.Is("POSITION_UNKNOWN") ? SeasonText.T("nod.position")
                    : err.Is("RATE_LIMITED") ? SeasonText.T("nod.slow") : err.Message;
                Bus.Publish(ToastSignal.Text(msg, ToastKind.Warning));
                return null;
            }
            finally { _collecting.Remove(nodId); }
        }

        public async Task<SeasonClaim> Claim(int tier, string track)
        {
            string k = track + ":" + tier;
            if (!_claimKeys.TryGetValue(k, out var key)) _claimKeys[k] = key = Guid.NewGuid().ToString();
            try
            {
                var c = await Api.Claim(tier, track, key, _life.Token);
                _claimKeys.Remove(k);
                Log.Info("season", $"claimed {track} tier {tier} ({c.Reward.Kind}) already {c.AlreadyClaimed}");
                _ = RefreshSeason();
                return c;
            }
            catch (Exception e)
            {
                var err = CityErrors.From(e, true);
                if (!err.OutcomeUnknown) _claimKeys.Remove(k);
                throw;
            }
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }

    public static class SeasonText
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["nod.tooFar"] = "Get a little closer to the nod.",
            ["nod.position"] = "Hold on, the city hasn't seen you arrive yet. Try again in a moment.",
            ["nod.slow"] = "Easy, one nod at a time.",
            ["nod.found"] = "Nod found: {0} · +{1} Season XP",
            ["nod.set"] = "Set complete: {0}! Reward in your wardrobe.",
        };
        public static string T(string key) => En.TryGetValue(key, out var v) ? v : key;
        public static string F(string key, params object[] a) => string.Format(System.Globalization.CultureInfo.InvariantCulture, T(key), a);
    }

    /// <summary>One season service per signed-in player (rebinds on sign-in / sign-out). Created by FlowController.</summary>
    public sealed class SeasonOverlays : IDisposable
    {
        private readonly Session _session;
        private SeasonService _svc;
        private bool _disposed;

        public SeasonOverlays()
        {
            _session = Services.Get<Session>();
            _session.Changed += Bind;
            Bind();
        }

        private void Bind()
        {
            if (_disposed) return;
            var user = _session.IsSignedIn ? _session.UserId : null;
            if (_svc != null && !_svc.Disposed && _svc.UserId == user) return;
            if (_svc != null) _svc.Collected -= Toast;
            _svc?.Dispose();
            _svc = string.IsNullOrEmpty(user) ? null : new SeasonService(user);
            if (_svc != null) _svc.Collected += Toast;
            var svc = _svc;
            CTLife.World.NodHooks.IsFound = svc == null ? (Func<string, bool>)null : id => svc.IsFound(id);
            CTLife.World.NodHooks.TryCollect = svc == null ? (Action<string>)null : id =>
            {
                var me = CTLife.World.VenueSession.Current?.LocalPlayer;
                _ = svc.Collect(id, me != null ? me.transform.position : Vector3.zero);
            };
            CTLife.World.NodHooks.NotifyFoundRefreshed();
        }

        private static void Toast(NodCollect r)
        {
            Bus.Publish(ToastSignal.Text(SeasonText.F("nod.found", r.NodId, r.Xp), ToastKind.Good));
            if (r.SetRewardCosmeticId != null) Bus.Publish(ToastSignal.Text(SeasonText.F("nod.set", r.SetId), ToastKind.Event));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Changed -= Bind;
            if (_svc != null) _svc.Collected -= Toast;
            _svc?.Dispose();
            _svc = null;
            CTLife.World.NodHooks.IsFound = null;
            CTLife.World.NodHooks.TryCollect = null;
        }
    }
}
