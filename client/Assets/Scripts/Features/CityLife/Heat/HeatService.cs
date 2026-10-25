using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.CityLife.Heat
{
    /// <summary>
    /// Sleuth heat, police pursuits and CT Jail for the signed-in player (lane B, 2026-10-25, backend #17, docs/HEAT.md):
    /// - GET /v1/heat/me on start and on every `police_pursuit`, `jailed` and `released`; every 30 s while wanted, chased
    ///   or jailed, otherwise every 2 minutes (heat halves daily, so the plate drifts down on its own);
    /// - pursuits are kept by `pursuitId`: the city room (to the target) and the street room (to everyone near the target)
    ///   both send them, so a pursuit starts once and ends on `police_pursuit_end`;
    /// - POST /v1/heat/caught when a cruiser holds contact (the server checks it against its own position data);
    ///   there is no escape report: the server decides;
    /// - POST /v1/heat/bail (Idempotency-Key) pays out of CT Jail.
    /// One per signed-in user, owned by <see cref="HeatOverlays"/>.
    /// </summary>
    public sealed class HeatService : IDisposable
    {
        public static HeatService Current { get; private set; }

        public string UserId { get; }
        public HeatMe Me { get; private set; }
        public JailInfo Jail { get; private set; }
        public bool Disposed { get; private set; }
        public bool Jailed => Jail != null && Jail.UntilMs > NowMs;
        public long NowMs => (long)ServerClock.NowMs;

        /// <summary>Active pursuits by id (the player's own and bystander ones).</summary>
        public readonly Dictionary<string, PursuitInfo> Pursuits = new Dictionary<string, PursuitInfo>();

        public event Action Changed;
        public event Action<PursuitInfo> PursuitStarted;
        public event Action<string, string> PursuitEnded;          // pursuitId, outcome
        public event Action<JailInfo> JailedNow;
        public event Action<string> Released;                       // reason
        public event Action<HeatSignal> VehicleSignal;              // vehicle_stolen / vehicle_returned

        private readonly IRestClient _rest;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly HashSet<string> _ended = new HashSet<string>();
        private bool _loading, _again;
        private string _bailKey;

        public HeatService(string userId)
        {
            UserId = userId;
            _rest = Services.Get<IRestClient>();
            Current = this;
            RawRoomMessages.Received += OnRaw;
            Refresh();
            _ = Loop();
        }

        public CancellationToken Life => _life.Token;

        public PursuitInfo MyPursuit
        {
            get
            {
                foreach (var p in Pursuits.Values) if (p.TargetUserId == UserId) return p;
                return null;
            }
        }

        // ------------------------------------------------------------------ reads

        public async void Refresh()
        {
            if (Disposed) return;
            if (_loading) { _again = true; return; }
            _loading = true;
            try
            {
                do
                {
                    _again = false;
                    var me = HeatMe.Parse(await _rest.Send<JToken>("GET", "/heat/me", null, true, null, _life.Token));
                    if (Disposed || me == null) return;
                    Apply(me);
                } while (_again && !Disposed);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Log.Warn("heat", "heat/me failed: " + (e is ApiException a ? a.Code : e.GetType().Name));
                // not read yet (a token still refreshing at start, a blip): try again soon, backing off to a minute
                if (Me == null && !Disposed) { _retry = Math.Min(60f, _retry * 2f); _ = RetryLater(_retry); }
            }
            finally { _loading = false; }
        }

        private float _retry = 2.5f;

        private async Task RetryLater(float seconds)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), _life.Token); } catch (OperationCanceledException) { return; }
            if (!Disposed && Me == null) Refresh();
        }

        private void Apply(HeatMe me)
        {
            bool wasJailed = Jailed;
            Me = me;
            if (me.Pursuit != null && !_ended.Contains(me.Pursuit.PursuitId))
            {
                me.Pursuit.TargetUserId = UserId;
                StartPursuit(me.Pursuit);
            }
            // a pursuit of mine the server no longer lists has ended (a missed end message)
            var mine = MyPursuit;
            if (mine != null && (me.Pursuit == null || me.Pursuit.PursuitId != mine.PursuitId)) EndPursuit(mine.PursuitId, me.Jail != null ? "caught" : "escaped");
            Jail = me.Jail;
            if (!wasJailed && Jailed) Raise(() => JailedNow?.Invoke(Jail));
            else if (wasJailed && !Jailed && me.Jail == null) Raise(() => Released?.Invoke("timer"));
            Log.Info("heat", $"heat {me.Heat:0} {me.Level} stars {me.Stars}{(me.Pursuit != null ? " pursuit " + me.Pursuit.Units + " units" : "")}{(Jail != null ? " jailed" : "")}");
            Raise(() => Changed?.Invoke());
        }

        private async Task Loop()
        {
            try
            {
                while (!Disposed)
                {
                    bool hot = Me != null && (Me.Level == HeatLevel.Wanted || MyPursuit != null) || Jail != null;
                    await Task.Delay(TimeSpan.FromSeconds(hot ? 30 : 120), _life.Token);
                    Refresh();
                }
            }
            catch (OperationCanceledException) { }
        }

        // ------------------------------------------------------------------ realtime

        private void OnRaw(string type, object payload)
        {
            if (Disposed) return;
            var s = HeatSignal.From(type, payload);
            if (s == null) return;
            switch (s.Type)
            {
                case "police_pursuit":
                    if (_ended.Contains(s.PursuitId)) return;
                    StartPursuit(s.Pursuit);
                    if (s.Pursuit.TargetUserId == UserId) Refresh();
                    break;
                case "police_pursuit_end":
                    EndPursuit(s.PursuitId, s.Outcome);
                    break;
                case "jailed":
                    Jail = s.Jail;
                    Log.Info("heat", "jailed until " + s.Jail.UntilMs + " bail " + s.Jail.BailCents);
                    Raise(() => JailedNow?.Invoke(s.Jail));
                    Raise(() => Changed?.Invoke());
                    Refresh();
                    break;
                case "released":
                    if (Jail == null) return;   // already out (our own bail reply came first)
                    Jail = null;
                    Log.Info("heat", "released: " + s.Reason);
                    Raise(() => Released?.Invoke(s.Reason ?? "timer"));
                    Raise(() => Changed?.Invoke());
                    Refresh();
                    break;
                default:
                    Raise(() => VehicleSignal?.Invoke(s));
                    break;
            }
        }

        private void StartPursuit(PursuitInfo p)
        {
            if (p == null || string.IsNullOrEmpty(p.PursuitId) || Pursuits.ContainsKey(p.PursuitId) || _ended.Contains(p.PursuitId)) return;
            Pursuits[p.PursuitId] = p;
            Log.Info("heat", $"pursuit {p.PursuitId.Substring(0, Math.Min(8, p.PursuitId.Length))} {(p.TargetUserId == UserId ? "of me" : "nearby")} units {p.Units}");
            Raise(() => PursuitStarted?.Invoke(p));
            Raise(() => Changed?.Invoke());
        }

        private void EndPursuit(string id, string outcome)
        {
            if (string.IsNullOrEmpty(id)) return;
            _ended.Add(id);
            if (!Pursuits.Remove(id)) return;
            Log.Info("heat", "pursuit end " + outcome);
            if (outcome == "caught") Refresh();   // the jail stay (if the `jailed` message is late or lost)
            Raise(() => PursuitEnded?.Invoke(id, outcome));
            Raise(() => Changed?.Invoke());
        }

        // ------------------------------------------------------------------ writes

        /// <summary>A cruiser held contact: tell the server (it believes it only if its own position data agrees).</summary>
        public async Task<bool> ReportCaught(string pursuitId, Vector3 at)
        {
            if (Disposed || !HeatRules.SafeId(pursuitId)) return false;
            var body = new JObject { ["pursuitId"] = pursuitId, ["x"] = Math.Round(at.x, 2), ["z"] = Math.Round(at.z, 2) }.ToString(Formatting.None);
            try
            {
                var me = HeatMe.Parse(await _rest.Send<JToken>("POST", "/heat/caught", body, true, Guid.NewGuid().ToString("N"), _life.Token));
                Log.Info("heat", "catch report accepted");
                if (me != null) Apply(me);
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception e)
            {
                Log.Info("heat", "catch report not taken: " + (e is ApiException a ? a.Code : e.GetType().Name));
                return false;
            }
        }

        /// <summary>POST /heat/bail: Cash out of CT Jail. Throws ApiException (INSUFFICIENT_CASH, NOT_JAILED).</summary>
        public async Task PayBail()
        {
            if (Disposed) throw new ObjectDisposedException(nameof(HeatService));
            _bailKey ??= Guid.NewGuid().ToString("N");
            try
            {
                await _rest.Send<JToken>("POST", "/heat/bail", "{}", true, _bailKey, _life.Token);
                _bailKey = null;
                Jail = null;
                Log.Info("heat", "bail paid");
                Raise(() => Released?.Invoke("bail"));
                Raise(() => Changed?.Invoke());
                if (Services.TryGet<PlayerProfile>(out var profile)) { try { await profile.Refresh(); } catch (Exception) { } }
                Refresh();
            }
            catch (ApiException e) when (e.Code == "NOT_JAILED")
            {
                _bailKey = null; Jail = null;
                Raise(() => Released?.Invoke("timer")); Raise(() => Changed?.Invoke());
            }
            catch (ApiException e) when (!e.IsNetwork && e.Status >= 400 && e.Status < 500) { _bailKey = null; throw; }
        }

        /// <summary>A 403 JAILED from any call: we are in CT Jail (the panel opens if it was not already).</summary>
        public void NoteJailed(JailInfo j)
        {
            if (Disposed || j == null) return;
            bool was = Jailed;
            Jail = new JailInfo { VenueId = HeatRules.JailVenue, UntilMs = j.UntilMs, BailCents = j.BailCents > 0 ? j.BailCents : Jail?.BailCents ?? 0 };
            if (!was && Jailed) Raise(() => JailedNow?.Invoke(Jail));
            Raise(() => Changed?.Invoke());
        }

        public static string Explain(Exception e)
        {
            if (e is ApiException a)
            {
                if (a.Code == "INSUFFICIENT_CASH") return HeatText.T("bail.short");
                if (a.Status == 429 || a.Code == "RATE_LIMITED") return HeatText.T("err.rate");
                return HeatText.T("err.generic");
            }
            return HeatText.T("err.generic");
        }

        private static void Raise(Action a)
        {
            try { a(); } catch (Exception e) { Log.Exception("heat", e); }
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            RawRoomMessages.Received -= OnRaw;
            _life.Cancel();
            _life.Dispose();
            if (Current == this) Current = null;
        }
    }

    /// <summary>Player-facing copy for heat, steals, pursuits and jail.</summary>
    public static class HeatText
    {
        private static readonly Dictionary<string, string> S = new Dictionary<string, string>
        {
            ["level.cool"] = "COOL", ["level.warm"] = "WARM", ["level.hot"] = "HOT", ["level.wanted"] = "WANTED",
            ["steal.verb"] = "Steal", ["steal.tag"] = "+{0} heat",
            ["steal.confirmTitle"] = "Not your ride", ["steal.confirmBody"] = "This {0} belongs to another player. Taking it adds {1} Sleuth heat, and it goes back to its owner when you get out.",
            ["steal.confirm"] = "Steal it", ["steal.cancel"] = "Leave it",
            ["steal.notStealable"] = "Hovercars can't be stolen.", ["steal.taken"] = "Someone's driving that.", ["steal.rate"] = "Lay low a few seconds before the next one.",
            ["steal.jailed"] = "No driving from CT Jail.", ["steal.far"] = "Get closer to the car.",
            ["stolen.toast"] = "Someone took your {0}. It'll be back where you left it.", ["returned.toast"] = "Your {0} is back where you parked it.",
            ["returned.thief"] = "The owner's car went back. You're on foot.",
            ["pursuit.start"] = "Lose {0}: get {1} away for {2}, or get home.",
            ["pursuit.escaped"] = "Heat cooled.", ["pursuit.caught"] = "BUSTED",
            ["passed.escaped"] = "LOST THEM", ["passed.free"] = "FREE",
            ["pursuit.nearby"] = "Police chase nearby",
            ["jail.title"] = "Compliance Jail", ["jail.sub"] = "Booked by The Sleuth", ["jail.left"] = "Release in", ["jail.bail"] = "PAY BAIL · {0}",
            ["jail.rules"] = "You're in {0}. No launches, buys, perps, bets, cars or other venues until you're out. Selling still works.",
            ["steal.help"] = "This {0} belongs to another player. Steal it? {1}",
            ["jail.body"] = "No coin launches, buys, new perps, casino bets, cars or other venues until you're out. Selling and closing positions still work.",
            ["jail.paying"] = "PAYING…", ["jail.free"] = "Out of Compliance Jail. Watch your heat.", ["jail.blocked"] = "You're in CT Jail ({0} left). Pay bail to get out.",
            ["bail.short"] = "Not enough Cash for bail. Wait it out.", ["err.rate"] = "Too fast. Try again in a moment.", ["err.generic"] = "Something went wrong.",
            ["explain.title"] = "Sleuth heat", ["explain.body"] = "Stealing cars and rugging coins heats you up. When you hit WANTED, The Sleuth sends cruisers. Get away, or do time in Compliance Jail.",
        };

        public static string T(string key) => S.TryGetValue(key, out var v) ? v : key;
        public static string F(string key, params object[] args) => string.Format(System.Globalization.CultureInfo.InvariantCulture, T(key), args);
    }
}
