using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.CityLife
{
    /// <summary>
    /// The guided first session for one signed-in player (queue 17, lane B 2026-10-25; brief:
    /// ct-life-evidence/onboarding-17/BRIEF.md). Holds the <see cref="FirstSessionState"/>:
    /// - from the server's `flags.firstSession` in GET /me when the backend has it (backend #24), else from PlayerPrefs per
    ///   user (an older backend);
    /// - beats complete from real game events (<see cref="GameplayEventSignal"/>: a clock-in or career shift start, a buy or
    ///   perp open, the chest claim, opening Quests), plus "home" and "moved", which the overlays report;
    /// - the client reports home / moved / quest, skip and replay with POST /me/first-session; the server sets origin,
    ///   character, shift, trade and chest itself, so a crash between the act and the report never loses a beat.
    /// A brand-new account is one that needed the character creator this login (<see cref="NewAccount"/>); anyone else
    /// without a server flag or a saved state is a returning player and never sees it.
    /// </summary>
    public sealed class FirstSessionService : IDisposable
    {
        public static FirstSessionService Current { get; private set; }
        /// <summary>This login had to create the character (a brand-new life).</summary>
        public static bool NewAccount { get; private set; }

        /// <summary>The flow: this login needs the character creator. A brand-new life gets the first session even on a
        /// backend without `flags.firstSession`.</summary>
        public static void BeganNewLife()
        {
            NewAccount = true;
            Current?.StartNewLife();
        }

        private void StartNewLife()
        {
            if (Disposed || !_placeholder || ServerBacked) return;
            _placeholder = false;
            State = new FirstSessionState();
            State.Mark(FirstBeat.Origin, Now);
            State.Mark(FirstBeat.Character, Now);
            Save();
            Changed?.Invoke();
        }

        public string UserId { get; }
        public FirstSessionState State { get; private set; }
        /// <summary>The server sent `flags.firstSession` (backend #24 or later).</summary>
        public bool ServerBacked { get; private set; }
        public bool Disposed { get; private set; }
        public event Action Changed;
        public event Action<FirstBeat> BeatDone;

        private readonly Subscriptions _subs = new Subscriptions();
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private bool _endpointMissing, _placeholder;
        private int _inFlight;
        private string Key => "first.session." + UserId;

        public FirstSessionService(string userId)
        {
            UserId = userId;
            Current = this;
            LoadLocal();
            ApplyServer();
            _subs.On<ProfileChangedSignal>(_ => ApplyServer());
            _subs.On<GameplayEventSignal>(OnEvent);
        }

        /// <summary>Guidance shows: started, not skipped, something left that this build offers.</summary>
        public bool Guiding => State != null && State.Active(Available);

        /// <summary>A beat this build offers: the first trade only while Pulse is open and markets are up.</summary>
        public bool Available(FirstBeat b) => b != FirstBeat.Trade || (Phases.IsOpen(2) && Features.IsAvailable(Features.Markets));

        public FirstBeat? Next => State?.Next(Available);

        private static long Now => (long)ServerClock.NowMs;

        // ------------------------------------------------------------------ state sources

        private void LoadLocal()
        {
            string saved = null;
            try { saved = PlayerPrefs.GetString(Key, null); } catch (Exception) { }
            if (!string.IsNullOrEmpty(saved)) { State = FirstSessionState.Parse(saved); return; }
            if (NewAccount)
            {
                State = new FirstSessionState();
                State.Mark(FirstBeat.Origin, Now);
                State.Mark(FirstBeat.Character, Now);
                Save();
                return;
            }
            State = FirstSessionState.ForReturning(Now);
            _placeholder = true;   // not saved: a server flag (a reinstall with a first session in play) replaces it
        }

        /// <summary>GET /me `flags.firstSession`, when present, is the truth; a beat seen here first is reported to it.</summary>
        private void ApplyServer()
        {
            if (Disposed || _inFlight > 0 || !Services.TryGet<PlayerProfile>(out var profile)) return;
            var me = profile.Me;
            if (me?.Flags == null || me.User == null || me.User.Id.ToString() != UserId) return;
            if (!me.Flags.TryGetValue("firstSession", out var raw) || raw == null) return;
            JToken t;
            try { t = raw as JToken ?? JToken.FromObject(raw); } catch (Exception) { return; }
            var server = Read(t);
            if (server == null) return;
            ServerBacked = true;
            Adopt(server);
        }

        private static FirstSessionState Read(JToken t)
        {
            if (t == null || t.Type != JTokenType.Object) return null;
            var state = CityWire.Str(t["state"]);
            if (state != "active" && state != "skipped" && state != "done") return null;
            var beats = new List<string>();
            if (t["beats"] is JArray a) foreach (var b in a) if (b?.Type == JTokenType.String) beats.Add((string)b);
            return FirstSessionState.FromWire(state, CityWire.TimeMs(t["startedAt"]) ?? 0, CityWire.TimeMs(t["completedAt"]) ?? 0, beats, Now);
        }

        private void Adopt(FirstSessionState server)
        {
            var before = State;
            // client beats seen here but not yet on the server (a report still in flight or lost) are kept and re-sent
            // (a /me read in the same moment as a shift or trade may not carry the server's own beat yet: kept locally too)
            var missing = new List<FirstBeat>();
            if (before != null && !_placeholder && !server.Completed && !server.Skipped)
                foreach (var b in FirstSessionState.Order)
                    if (before.IsDone(b) && !server.IsDone(b)) missing.Add(b);
            _placeholder = false;
            State = server;
            foreach (var b in missing)
            {
                State.Mark(b, Now, Available);
                if (b == FirstBeat.Home || b == FirstBeat.Moved || b == FirstBeat.Quest) Post(new JObject { ["beat"] = FirstSessionState.Wire(b) });
            }
            bool changed = before == null || Comparable(before) != Comparable(State);
            if (before != null)
                foreach (var b in FirstSessionState.Order)
                    if (State.IsDone(b) && !before.IsDone(b)) BeatDone?.Invoke(b);
            Save();
            if (changed) Changed?.Invoke();
        }

        /// <summary>What the UI shows (skip, finished, beats), not the clocks, which differ between the server and the device.</summary>
        private static string Comparable(FirstSessionState st) =>
            (st.Skipped ? "s" : "-") + (st.Completed ? "c" : "-") + string.Join(",", st.Done.OrderBy(b => b));

        // ------------------------------------------------------------------ beats

        private void OnEvent(GameplayEventSignal s)
        {
            switch (s.kind)
            {
                case GameplayEvents.ShiftStart: Mark(FirstBeat.Shift); break;
                case GameplayEvents.TradeOpen: Mark(FirstBeat.Trade); break;
                case GameplayEvents.ChestOpen: Mark(FirstBeat.Chest); break;
                case GameplayEvents.QuestsOpen: Mark(FirstBeat.Quest); break;
            }
        }

        /// <summary>A beat happened in the game. Client beats (home, moved, quest) are reported to the server.</summary>
        public void Mark(FirstBeat b)
        {
            if (Disposed || State == null || _placeholder) return;
            if (!State.Mark(b, Now, Available)) return;
            Log.Info("first", "beat " + FirstSessionState.Wire(b));
            Save();
            if (b == FirstBeat.Home || b == FirstBeat.Moved || b == FirstBeat.Quest) Post(new JObject { ["beat"] = FirstSessionState.Wire(b) });
            BeatDone?.Invoke(b);
            Changed?.Invoke();
        }

        /// <summary>"Skip intro ›": guidance ends now and stays off; the starter quests stay earnable.</summary>
        public void Skip()
        {
            if (State == null || State.Skipped) return;
            State.Skip();
            Log.Info("first", "skipped");
            Save();
            Post(new JObject { ["skip"] = true });
            Changed?.Invoke();
        }

        /// <summary>Settings → "Replay the first day": guidance again from the first step in the city.</summary>
        public void Replay()
        {
            if (State == null) return;
            _placeholder = false;
            State.Replay(Now);
            if (!State.IsDone(FirstBeat.Home)) { State.Mark(FirstBeat.Origin, Now); State.Mark(FirstBeat.Character, Now); }
            try { PlayerPrefs.DeleteKey("first.dm." + UserId); } catch (Exception) { }
            Log.Info("first", "replay");
            Save();
            Post(new JObject { ["replay"] = true });
            Changed?.Invoke();
        }

        private void Save()
        {
            if (_placeholder || State == null) return;
            try { PlayerPrefs.SetString(Key, State.Serialize()); PlayerPrefs.Save(); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ POST /me/first-session

        private async void Post(JObject body)
        {
            if (_endpointMissing || RuntimeMode.IsOfflineDemo || !Services.TryGet<IRestClient>(out var rest)) return;
            _inFlight++;
            try
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        var key = Guid.NewGuid().ToString();
                        var r = await rest.Send<JToken>("POST", "/me/first-session", body.ToString(Formatting.None), true, key, _life.Token);
                        if (Disposed) return;
                        var server = Read(r);
                        if (server != null) { ServerBacked = true; Adopt(server); }
                        return;
                    }
                    catch (ApiException e) when (e.Status == 404 || e.Status == 405)
                    {
                        _endpointMissing = true;   // an older backend: PlayerPrefs is the record
                        Log.Info("first", "first-session endpoint not on this server; local state only");
                        return;
                    }
                    catch (ApiException e) when (e.IsNetwork || e.Status >= 500 || e.Status == 429)
                    {
                        await Task.Delay(2000 * (attempt + 1), _life.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("first", "first-session report failed: " + (e is ApiException a ? a.Code : e.GetType().Name)); }
            finally { _inFlight--; }
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            _subs.Dispose();
            if (Current == this) { Current = null; NewAccount = false; }
        }
    }
}
