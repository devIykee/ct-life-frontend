using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI.Kit;
using CTLife.World;
using UnityEngine;

namespace CTLife.CityLife.Casino
{
    /// <summary>
    /// The Rug Room for one signed-in player (backend PR #12):
    /// - `GET /casino/state` on entry (the 18+ gate, the self-exclusion lock, today's caps, the paytables) and after every bet
    ///   that changes the caps;
    /// - the meta on every reply: the disclaimer (shown on every screen) and the session reminder (the break popup, once per 30
    ///   minutes of continuous play);
    /// - head-to-head challenges, polled while the casino is open or the player is on the casino floor (there is no realtime
    ///   push for them): a new incoming one raises <see cref="Incoming"/> and a toast;
    /// - Cash after each bet goes straight into the profile so the HUD pill follows.
    /// Owned by <see cref="CasinoOverlays"/>; rebinds on sign-in and sign-out.
    /// </summary>
    public sealed class CasinoService : IDisposable
    {
        public static CasinoService Current { get; private set; }

        public readonly CasinoApi Api;
        public string UserId { get; }
        public CasinoState State { get; private set; }
        public string StateError { get; private set; }
        public string Disclaimer { get; private set; } = CasinoGames.FallbackDisclaimer;
        public int SessionMinutes { get; private set; }
        public List<CasinoChallenge> Challenges { get; private set; } = new List<CasinoChallenge>();
        public bool Disposed { get; private set; }

        public event Action StateChanged, ChallengesChanged;
        /// <summary>The server says 30+ minutes of continuous play (once per half hour).</summary>
        public event Action<int> SessionReminder;
        public event Action<CasinoChallenge> Incoming;

        /// <summary>The casino screens are open (the overlay sets it): poll challenges faster.</summary>
        public bool ScreensOpen { get; set; }

        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly HashSet<string> _seenIncoming = new HashSet<string>();
        private bool _stateLoading, _challengesLoading, _firstChallenges = true;
        private int _remindedBlock;
        private float _stateAt = -1e9f, _challengesAt = -1e9f;

        public CasinoService(string userId)
        {
            UserId = userId;
            Api = new CasinoApi(Services.Get<IRestClient>());
            Current = this;
            CasinoHooks.FloorChanged += OnFloor;
            _ = Loop();
        }

        public CancellationToken Life => _life.Token;

        public async Task<CasinoState> RefreshState(bool force = true)
        {
            if (Disposed || RuntimeMode.IsOfflineDemo || _stateLoading) return State;
            if (!force && Time.realtimeSinceStartup - _stateAt < 4f) return State;
            _stateLoading = true;
            try
            {
                var s = await Api.State(_life.Token);
                if (Disposed) return s;
                State = s; StateError = null; _stateAt = Time.realtimeSinceStartup;
                Apply(s);
                Log.Info("casino", $"state age {s.AgeConfirmed} excluded {(s.SelfExcludedUntilMs.HasValue ? CasinoText.Date(s.SelfExcludedUntilMs.Value) : "-")} stake left {s.StakeRemainingCents} loss left {s.LossRemainingCents} session {s.SessionMinutes}m");
                StateChanged?.Invoke();
                return s;
            }
            catch (OperationCanceledException) { return State; }
            catch (Exception e)
            {
                var err = CityErrors.From(e, false);
                StateError = CasinoText.Error(err);
                Log.Warn("casino", "GET /casino/state failed: " + err.Code);
                StateChanged?.Invoke();
                return State;
            }
            finally { _stateLoading = false; }
        }

        /// <summary>Every reply's meta: disclaimer, session minutes, and the break popup once per 30-minute block.</summary>
        public void Apply(CasinoMeta m)
        {
            if (m == null || Disposed) return;
            if (!string.IsNullOrEmpty(m.Disclaimer)) Disclaimer = m.Disclaimer;
            SessionMinutes = m.SessionMinutes;
            if (!m.SessionReminder) { if (m.SessionMinutes < 30) _remindedBlock = 0; return; }
            int block = Math.Max(1, m.SessionMinutes / 30);
            if (block <= _remindedBlock) return;
            _remindedBlock = block;
            Log.Info("casino", "session reminder at " + m.SessionMinutes + " min");
            SessionReminder?.Invoke(m.SessionMinutes);
        }

        /// <summary>After a bet: Cash into the profile (the HUD pill), the meta, and the caps re-read in the background.</summary>
        public void AfterBet(CasinoMeta meta, long cashCents)
        {
            Apply(meta);
            if (cashCents >= 0 && Services.TryGet<PlayerProfile>(out var p)) p.SetCash(cashCents);
            _ = RefreshState();
        }

        /// <summary>A refused stake: AGE_REQUIRED / SELF_EXCLUDED / *_CAP_REACHED change the gates, so re-read the state now.</summary>
        public void AfterRefusal(CityError err)
        {
            if (err == null) return;
            if (err.Is("AGE_REQUIRED") || err.Is("SELF_EXCLUDED") || err.Is("STAKE_CAP_REACHED") || err.Is("LOSS_CAP_REACHED") || err.Is("INSUFFICIENT_CASH"))
            {
                _ = RefreshState();
                if (Services.TryGet<PlayerProfile>(out var p)) _ = p.Refresh().ContinueWith(_ => { });
            }
        }

        public async Task<List<CasinoChallenge>> RefreshChallenges()
        {
            if (Disposed || RuntimeMode.IsOfflineDemo || _challengesLoading) return Challenges;
            _challengesLoading = true;
            try
            {
                var (meta, list) = await Api.Challenges(_life.Token);
                if (Disposed) return list;
                _challengesAt = Time.realtimeSinceStartup;
                Challenges = list;
                if (!string.IsNullOrEmpty(meta.Disclaimer)) Disclaimer = meta.Disclaimer;
                foreach (var c in list)
                {
                    if (!c.Incoming || !c.Open || _seenIncoming.Contains(c.ChallengeId)) continue;
                    _seenIncoming.Add(c.ChallengeId);
                    if (_firstChallenges) continue;
                    Log.Info("casino", $"incoming challenge {c.ChallengeId} from {c.ChallengerAlias} {c.StakeCents}c");
                    Cues.Play("sfx_chips", 0.8f);
                    if (!ScreensOpen)
                        Bus.Publish(ToastSignal.Text(CasinoText.F("flip.toast", string.IsNullOrEmpty(c.ChallengerAlias) ? CasinoText.T("flip.anon") : c.ChallengerAlias, CasinoText.Money(c.StakeCents)), ToastKind.Event));
                    Incoming?.Invoke(c);
                }
                _firstChallenges = false;
                ChallengesChanged?.Invoke();
                return list;
            }
            catch (OperationCanceledException) { return Challenges; }
            catch (Exception e) { Log.Warn("casino", "GET challenges failed: " + CityErrors.From(e, false).Code); return Challenges; }
            finally { _challengesLoading = false; }
        }

        /// <summary>Replace one challenge in the list after an answer (the next poll confirms it).</summary>
        public void Upsert(CasinoChallenge c)
        {
            if (c == null) return;
            int i = Challenges.FindIndex(x => x.ChallengeId == c.ChallengeId);
            if (i >= 0) Challenges[i] = c; else Challenges.Insert(0, c);
            ChallengesChanged?.Invoke();
        }

        private void OnFloor(bool on)
        {
            if (Disposed) return;
            if (on) { _ = RefreshState(force: false); _ = RefreshChallenges(); }
        }

        private async Task Loop()
        {
            while (!Disposed)
            {
                try { await Task.Delay(1000, _life.Token); }
                catch (OperationCanceledException) { return; }
                if (Disposed) return;
                bool active = ScreensOpen || CasinoHooks.OnFloor;
                if (!active) continue;
                float every = ScreensOpen ? 3f : 6f;
                if (Time.realtimeSinceStartup - _challengesAt >= every) await RefreshChallenges();
                // the day can roll over while the casino is open: re-read the caps at the reset
                var s = State;
                if (s != null && s.ResetsAtMs > 0 && ServerClock.NowMs > s.ResetsAtMs + 2000 && Time.realtimeSinceStartup - _stateAt > 10f) await RefreshState();
            }
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            CasinoHooks.FloorChanged -= OnFloor;
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
