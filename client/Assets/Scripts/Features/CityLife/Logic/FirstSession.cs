using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CTLife.CityLife
{
    /// <summary>The guided first session's beats (queue 17; brief: ct-life-evidence/onboarding-17/BRIEF.md).</summary>
    public enum FirstBeat { Origin, Character, Home, Moved, Shift, Trade, Quest, Chest }

    /// <summary>
    /// The first-session state machine (lane B, 2026-10-25), pure and EditMode-tested. Beats complete from game events in
    /// <b>any order</b> (a trade before the shift still counts). <see cref="Next"/> is the first unfinished beat in the
    /// story order, so the objective line always shows one thing to do. Skip ends guidance (rewards stay earnable in
    /// Quests); replay starts it again. A beat that is not available in this build (the first trade while markets are
    /// off) is passed over, never blocking. Persisted per user as one short string (PlayerPrefs now; the server
    /// `firstSession` flag later).
    /// </summary>
    public sealed class FirstSessionState
    {
        public static readonly FirstBeat[] Order =
            { FirstBeat.Origin, FirstBeat.Character, FirstBeat.Home, FirstBeat.Moved, FirstBeat.Shift, FirstBeat.Trade, FirstBeat.Quest, FirstBeat.Chest };

        private readonly HashSet<FirstBeat> _done = new HashSet<FirstBeat>();
        public bool Skipped { get; private set; }
        public long StartedAtMs { get; private set; }
        public long CompletedAtMs { get; private set; }

        public IReadOnlyCollection<FirstBeat> Done => _done;
        public bool IsDone(FirstBeat b) => _done.Contains(b);
        public bool Completed => CompletedAtMs > 0;

        /// <summary>Guidance shows while the session has started, is neither finished nor skipped.</summary>
        public bool Active(Func<FirstBeat, bool> available = null) => StartedAtMs > 0 && !Skipped && Next(available).HasValue;

        public void Start(long nowMs) { if (StartedAtMs <= 0) StartedAtMs = nowMs; }

        /// <summary>A beat happened. True if this changed anything (the first time).</summary>
        public bool Mark(FirstBeat b, long nowMs, Func<FirstBeat, bool> available = null)
        {
            if (!_done.Add(b)) return false;
            Start(nowMs);
            if (!Next(available).HasValue && CompletedAtMs <= 0) CompletedAtMs = nowMs;
            return true;
        }

        /// <summary>The first unfinished beat in story order, skipping beats this build does not offer; null when done.</summary>
        public FirstBeat? Next(Func<FirstBeat, bool> available = null)
        {
            foreach (var b in Order)
            {
                if (_done.Contains(b)) continue;
                if (available != null && !available(b)) continue;
                return b;
            }
            return null;
        }

        /// <summary>Done / available beats, for a "2 of 3" starter card.</summary>
        public (int done, int total) Progress(IEnumerable<FirstBeat> beats, Func<FirstBeat, bool> available = null)
        {
            int d = 0, t = 0;
            foreach (var b in beats)
            {
                if (available != null && !available(b)) continue;
                t++; if (_done.Contains(b)) d++;
            }
            return (d, t);
        }

        public void Skip() => Skipped = true;

        /// <summary>Settings → "Replay the first day": guidance again from the first beat after the lobby.</summary>
        public void Replay(long nowMs)
        {
            Skipped = false; CompletedAtMs = 0; StartedAtMs = nowMs;
            foreach (var b in Order) if (b >= FirstBeat.Moved) _done.Remove(b);
        }

        // ------------------------------------------------------------------ persistence: "v1|s|started|completed|Origin,Character"

        public string Serialize() =>
            string.Join("|", "v1", Skipped ? "s" : "-", StartedAtMs.ToString(CultureInfo.InvariantCulture), CompletedAtMs.ToString(CultureInfo.InvariantCulture),
                string.Join(",", Order.Where(_done.Contains)));

        public static FirstSessionState Parse(string s)
        {
            var st = new FirstSessionState();
            if (string.IsNullOrEmpty(s)) return st;
            var p = s.Split('|');
            if (p.Length < 5 || p[0] != "v1") return st;
            st.Skipped = p[1] == "s";
            long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var started);
            long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var completed);
            st.StartedAtMs = Math.Max(0, started); st.CompletedAtMs = Math.Max(0, completed);
            foreach (var name in p[4].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (Enum.TryParse<FirstBeat>(name, false, out var b) && Array.IndexOf(Order, b) >= 0) st._done.Add(b);
            return st;
        }

        /// <summary>
        /// The server's `flags.firstSession` (backend #24): {state: active|skipped|done, startedAt, completedAt, beats[]}.
        /// Beats are lower case on the wire ("moved"); unknown ones are ignored. "done" with no start (an account from before
        /// the onboarding migration) reads as returning.
        /// </summary>
        public static FirstSessionState FromWire(string state, long startedAtMs, long completedAtMs, IEnumerable<string> beats, long nowMs)
        {
            if (state == "done" && startedAtMs <= 0) return ForReturning(nowMs);
            var st = new FirstSessionState { StartedAtMs = Math.Max(0, startedAtMs) };
            if (beats != null)
                foreach (var name in beats)
                    if (TryBeat(name, out var b)) st._done.Add(b);
            st.Skipped = state == "skipped";
            if (state == "done") st.CompletedAtMs = completedAtMs > 0 ? completedAtMs : nowMs;
            if (st.StartedAtMs <= 0 && state == "active") st.StartedAtMs = nowMs;
            return st;
        }

        /// <summary>The wire name of a beat ("moved").</summary>
        public static string Wire(FirstBeat b) => b.ToString().ToLowerInvariant();

        public static bool TryBeat(string wire, out FirstBeat beat)
        {
            beat = default;
            return !string.IsNullOrEmpty(wire) && Enum.TryParse(wire, true, out beat) && Array.IndexOf(Order, beat) >= 0 && !int.TryParse(wire, out _);
        }

        /// <summary>A player who already has a life (a returning account, a reinstall with history) never sees it.</summary>
        public static FirstSessionState ForReturning(long nowMs)
        {
            var st = new FirstSessionState { StartedAtMs = nowMs, CompletedAtMs = nowMs };
            foreach (var b in Order) st._done.Add(b);
            return st;
        }
    }

    /// <summary>Copy keys for the one-line objective per beat (the UI resolves them; ≤ 12 words each).</summary>
    public static class FirstSessionCopy
    {
        public static string ObjectiveKey(FirstBeat b) => b switch
        {
            FirstBeat.Moved => "first.move",
            FirstBeat.Shift => "first.shift",
            FirstBeat.Trade => "first.trade",
            FirstBeat.Quest => "first.quest",
            FirstBeat.Chest => "first.chest",
            _ => null,   // origin, character and home are screens of their own (the lottery, the creator, the lobby)
        };

        /// <summary>The starter card's three steps (the Quests app).</summary>
        public static readonly FirstBeat[] Starter = { FirstBeat.Shift, FirstBeat.Trade, FirstBeat.Chest };
    }
}
