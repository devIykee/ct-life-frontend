using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;

namespace CTLife.CityLife.RektBurger
{
    /// <summary>
    /// The clock-in shift (user decision 2026-10-25: Rekt Burger is a simple Sims-style job). The server times it and pays;
    /// the player clocks in at the counter, can walk away or close the app while it runs, and gets a summary on clock-out or
    /// on return. The backend endpoints (start / end / current, timed pay, a position gate at the counter) are being rebuilt,
    /// so the client talks to this interface; <see cref="FakeRektShiftService"/> stands in until the contract lands.
    /// </summary>
    public sealed class ShiftState
    {
        public string ShiftId;
        public bool Active;
        public bool Resumed;
        public string PostId;
        public long StartedAtMs, EndsAtMs;      // EndsAtMs: the server closes and pays the shift here (maxEndsAt)
        public long EarnedCents;               // accruedCents at ServerNowMs (after the daily cap)
        public long CapCents;                  // capRemainingCents: what is left of today's cap
        public long RatePerMinuteCents;
        public long ServerNowMs;               // the server's clock when this state was read
        public float ReadAtRealtime;           // Time.realtimeSinceStartup when it was read (for local ticking)

        /// <summary>Server-clock "now" extrapolated from the last read (the timer never trusts the device clock).</summary>
        public double NowMs(float realtime) => ServerNowMs + Math.Max(0, realtime - ReadAtRealtime) * 1000.0;
        public double ElapsedSeconds(float realtime) => Active ? Math.Max(0, (NowMs(realtime) - StartedAtMs) / 1000.0) : 0;
        /// <summary>Accrued so far, ticking at the rate between reads, never past the cap (display only; the server pays).</summary>
        public long AccruedNow(float realtime)
        {
            double extra = Math.Max(0, realtime - ReadAtRealtime) / 60.0 * RatePerMinuteCents;
            return Math.Min(EarnedCents + (long)Math.Floor(extra), Math.Max(EarnedCents, CapCents));
        }

        public static ShiftState Parse(Newtonsoft.Json.Linq.JToken t)
        {
            if (t == null || t.Type != Newtonsoft.Json.Linq.JTokenType.Object) return null;
            return new ShiftState
            {
                ShiftId = CityWire.ReqStr(t["shiftId"], "shiftId"),
                Active = true,
                StartedAtMs = CityWire.TimeMsOrNull(t["startedAt"]) ?? 0,
                EndsAtMs = CityWire.TimeMsOrNull(t["maxEndsAt"]) ?? 0,
                EarnedCents = CityWire.LongOr(t["accruedCents"], 0),
                CapCents = CityWire.LongOr(t["capRemainingCents"], 0),
                RatePerMinuteCents = CityWire.LongOr(t["ratePerMinuteCents"], 50),
                ServerNowMs = CityWire.TimeMsOrNull(t["serverNow"]) ?? (long)ServerClock.NowMs,
                Resumed = CityWire.BoolOr(t["resumed"], false),
                PostId = CityWire.Str(t["postId"]),
                ReadAtRealtime = Time.realtimeSinceStartup,
            };
        }
    }

    public sealed class ShiftSummary
    {
        public string ShiftId;
        public long PaidCents, CappedCents, CapRemainingCents, EndedAtMs;
        public int Minutes, DurationSec, CareerXp;   // backend #21: durationSec (total) + minutes = floor(durationSec / 60)
        public bool UniformGranted;
        /// <summary>CapRemainingCents came from the server (POST …/shift/end); the return summary has no cap figure.</summary>
        public bool CapKnown;
        public string EndReason;               // clock_out | auto | left
        /// <summary>Found on return (GET …/shift/current lastShift), not from a clock-out just now.</summary>
        public bool WhileAway;

        /// <summary>POST …/shift/end.</summary>
        public static ShiftSummary ParseEnd(Newtonsoft.Json.Linq.JToken t) => t == null ? null : new ShiftSummary
        {
            ShiftId = CityWire.Str(t["shiftId"]),
            PaidCents = CityWire.LongOr(t["paidCents"], 0),
            DurationSec = CityWire.IntOr(t["durationSec"], 0),
            Minutes = CityWire.IntOr(t["minutes"], CityWire.IntOr(t["durationSec"], 0) / 60),
            CareerXp = CityWire.IntOr(t["careerXp"], 0),
            CappedCents = CityWire.LongOr(t["cappedCents"], 0),
            CapRemainingCents = CityWire.LongOr(t["capRemainingCents"], 0),
            UniformGranted = CityWire.BoolOr(t["uniformGranted"], false),
            CapKnown = t["capRemainingCents"] != null && t["capRemainingCents"].Type != Newtonsoft.Json.Linq.JTokenType.Null,
            EndReason = CityWire.Str(t["endReason"]) ?? "clock_out",
            EndedAtMs = (long)ServerClock.NowMs,
        };

        /// <summary>GET …/shift/current lastShift.</summary>
        public static ShiftSummary ParseLast(Newtonsoft.Json.Linq.JToken t) =>
            t == null || t.Type != Newtonsoft.Json.Linq.JTokenType.Object ? null : new ShiftSummary
            {
                ShiftId = CityWire.Str(t["shiftId"]),
                EndedAtMs = CityWire.TimeMsOrNull(t["endedAt"]) ?? 0,
                PaidCents = CityWire.LongOr(t["paidCents"], 0),
                DurationSec = CityWire.IntOr(t["durationSec"], 0),
                Minutes = CityWire.IntOr(t["minutes"], CityWire.IntOr(t["durationSec"], 0) / 60),
                CareerXp = CityWire.IntOr(t["careerXp"], 0),
                EndReason = CityWire.Str(t["endReason"]),
                WhileAway = true,
            };
    }

    public interface IRektShiftService
    {
        /// <summary>GET …/shift/current: the open shift, or an ended one the player has not seen yet (summary on return).</summary>
        Task<(ShiftState state, ShiftSummary unseen)> Current(CancellationToken ct);
        /// <summary>Clock in at the counter (the server checks the player stands there).</summary>
        Task<ShiftState> Start(string key, CancellationToken ct);
        /// <summary>Clock out: the server stops the clock and pays.</summary>
        Task<ShiftSummary> End(string key, CancellationToken ct);
    }

    /// <summary>
    /// Local stand-in until the backend's clock-in endpoints land (dev builds; never pays real Cash: the summary is display
    /// only). Pay follows backend #21: 50 cents a minute, capped by today's remaining cap. Survives an app
    /// restart (PlayerPrefs) so the return summary can be exercised.
    /// </summary>
    public sealed class FakeRektShiftService : IRektShiftService
    {
        private readonly string _user;
        private readonly Func<long> _capToday;
        public const long RatePerMinute = 50;
        private string Key => "ctlife.rektburger.fakeshift." + _user;

        public FakeRektShiftService(string userId, Func<long> capToday) { _user = userId; _capToday = capToday; }

        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private ShiftState Load()
        {
            var raw = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(raw)) return null;
            var parts = raw.Split('|');
            if (parts.Length < 2 || !long.TryParse(parts[1], out var started)) return null;
            return Make(parts[0], started);
        }

        private ShiftState Make(string id, long started)
        {
            long cap = Math.Max(0, _capToday());
            long earned = Math.Min(cap, (long)Math.Floor(RatePerMinute * (Now - started) / 60000.0));
            return new ShiftState { ShiftId = id, Active = true, StartedAtMs = started, EndsAtMs = started + 3600000, EarnedCents = earned, CapCents = cap,
                RatePerMinuteCents = RatePerMinute, ServerNowMs = Now, ReadAtRealtime = Time.realtimeSinceStartup };
        }

        public Task<(ShiftState, ShiftSummary)> Current(CancellationToken ct) => Task.FromResult<(ShiftState, ShiftSummary)>((Load(), null));

        public Task<ShiftState> Start(string key, CancellationToken ct)
        {
            var s = Load();
            if (s != null) return Task.FromResult(s);
            string id = Guid.NewGuid().ToString();
            PlayerPrefs.SetString(Key, id + "|" + Now); PlayerPrefs.Save();
            Log.Info("rektburger", "fake shift started " + id);
            return Task.FromResult(Load());
        }

        public Task<ShiftSummary> End(string key, CancellationToken ct)
        {
            var s = Load();
            PlayerPrefs.DeleteKey(Key); PlayerPrefs.Save();
            if (s == null) return Task.FromResult<ShiftSummary>(null);
            long secs = Math.Max(0, (Now - s.StartedAtMs) / 1000);
            var sum = new ShiftSummary { ShiftId = s.ShiftId, EndedAtMs = Now, PaidCents = s.EarnedCents, Minutes = (int)(secs / 60), DurationSec = (int)secs, EndReason = "clock_out" };
            Log.Info("rektburger", $"fake shift ended {s.ShiftId}: {sum.Minutes} min, display-only pay {sum.PaidCents}");
            return Task.FromResult(sum);
        }
    }

    /// <summary>
    /// Backend #21 (devIykee/ct-life-backend, feat/rekt-burger-clockin): POST …/shift/start (no body; the server checks a fresh
    /// street position within the counter radius: 409 POSITION_UNKNOWN / COUNTER_TOO_FAR), POST …/shift/end, GET …/shift/current
    /// ({shift, lastShift}). `lastShift` becomes the "while you were away" summary once per shift (the last seen id is kept
    /// per player on the device).
    /// </summary>
    public sealed class RektShiftApiService : IRektShiftService
    {
        private readonly CTLife.Net.IRestClient _rest;
        private readonly string _user;
        private string SeenKey => "ctlife.rektburger.seenshift." + _user;

        public RektShiftApiService(CTLife.Net.IRestClient rest, string userId) { _rest = rest; _user = userId; }

        private Task<Newtonsoft.Json.Linq.JToken> Post(string path, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            return _rest.Send<Newtonsoft.Json.Linq.JToken>("POST", path, "{}", true, key, ct);
        }

        public async Task<(ShiftState state, ShiftSummary unseen)> Current(CancellationToken ct)
        {
            var t = await _rest.Get<Newtonsoft.Json.Linq.JToken>("/jobs/rekt-burger/shift/current", ct);
            var state = ShiftState.Parse(t?["shift"]);
            var last = ShiftSummary.ParseLast(t?["lastShift"]);
            if (last != null && !string.IsNullOrEmpty(last.ShiftId) && PlayerPrefs.GetString(SeenKey, "") == last.ShiftId) last = null;
            return (state, last);
        }

        public async Task<ShiftState> Start(string key, CancellationToken ct) => ShiftState.Parse(await Post("/jobs/rekt-burger/shift/start", key, ct));

        public async Task<ShiftSummary> End(string key, CancellationToken ct)
        {
            var sum = ShiftSummary.ParseEnd(await Post("/jobs/rekt-burger/shift/end", key, ct));
            if (sum?.ShiftId != null) MarkSeen(sum.ShiftId);
            return sum;
        }

        public void MarkSeen(string shiftId) { if (string.IsNullOrEmpty(shiftId)) return; PlayerPrefs.SetString(SeenKey, shiftId); PlayerPrefs.Save(); }
    }
}
