using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.Careers
{
    /// <summary>POST /shifts → `Shift {shiftId, miniGameIds[], endsAt}`.</summary>
    public sealed class ShiftTicket
    {
        public string ShiftId;
        public List<string> MiniGameIds = new List<string>();
        public DateTime? EndsAtUtc;

        public static ShiftTicket Parse(JToken body)
        {
            if (!(body is JObject o)) return null;
            return new ShiftTicket
            {
                ShiftId = CareersWire.Str(o, "shiftId", "id"),
                MiniGameIds = CareersWire.Strings(o["miniGameIds"]),
                EndsAtUtc = CareersWire.Time(o["endsAt"]),
            };
        }
    }

    /// <summary>POST /shifts/{id}/complete → `{payCents, xp, promoted}`.</summary>
    public sealed class ShiftReceipt
    {
        public long PayCents;
        public int Xp;
        public bool Promoted;

        public static ShiftReceipt Parse(JToken body)
        {
            if (!(body is JObject o)) return null;
            var pay = CareersWire.Long(o, "payCents");
            var xp = CareersWire.Int(o, "xp");
            if (!pay.HasValue || pay.Value < 0 || !xp.HasValue || xp.Value < 0) return null;
            return new ShiftReceipt { PayCents = pay.Value, Xp = xp.Value, Promoted = CareersWire.Bool(o, "promoted") ?? false };
        }
    }

    /// <summary>A registry entry from GET /config `ids.mini_games` (contract: `{id, careerId, maxScore, durationSec}`).</summary>
    public sealed class RegistryGame
    {
        public string Id, CareerId;
        public int MaxScore, DurationSec;
    }

    /// <summary>The validated, resolved assignment for one shift.</summary>
    public sealed class ShiftPlan
    {
        public string ShiftId, CareerId;
        public DateTime EndsAtUtc;
        public List<ResolvedGame> Games = new List<ResolvedGame>();

        /// <summary>
        /// Validates a server assignment. Rules: a shift id safe for a URL path, an end time, 1-5 distinct ids, each
        /// resolvable (registry caps win; FE proposal defaults otherwise), and none belonging to another career.
        /// An id unknown to both registry and catalog fails the plan: nothing is played and nothing is submitted.
        /// </summary>
        public static ShiftPlan Build(ShiftTicket ticket, string careerId, Func<string, RegistryGame> registry, MiniGameCatalog catalog, out string errorKey)
        {
            errorKey = null;
            if (ticket == null || !CareersWire.IsSafeId(ticket.ShiftId) || ticket.MiniGameIds == null || ticket.MiniGameIds.Count == 0 ||
                ticket.MiniGameIds.Count > 5 || !ticket.EndsAtUtc.HasValue)
            { errorKey = "shift.invalidAssignment"; return null; }
            if (ticket.MiniGameIds.Distinct(StringComparer.Ordinal).Count() != ticket.MiniGameIds.Count) { errorKey = "shift.invalidAssignment"; return null; }
            var plan = new ShiftPlan { ShiftId = ticket.ShiftId, CareerId = careerId, EndsAtUtc = ticket.EndsAtUtc.Value };
            foreach (var id in ticket.MiniGameIds)
            {
                if (!CareersWire.IsSafeId(id)) { errorKey = "shift.invalidAssignment"; return null; }
                var reg = registry?.Invoke(id);
                var resolved = catalog.Resolve(id, reg?.MaxScore, reg?.DurationSec, reg?.CareerId);
                if (resolved == null) { errorKey = "shift.unknownGame"; return null; }
                if (!string.IsNullOrEmpty(resolved.CareerId) && resolved.CareerId != "*" && resolved.CareerId != careerId)
                { errorKey = "shift.crossCareer"; return null; }
                plan.Games.Add(resolved);
            }
            return plan;
        }

        public double TotalDuration => Games.Sum(g => g.Duration);
    }

    /// <summary>Shift clock: never settle before both the server's endsAt and a local elapsed-time floor.</summary>
    public static class ShiftTiming
    {
        /// <summary>
        /// Seconds left before completion may be sent. Without a synced server clock only the monotonic floor counts
        /// (an unsynced wall clock must not add hours of waiting or settle early); the server still decides readiness.
        /// </summary>
        public static double SecondsUntilEnd(double now, double minimumEnd, double serverSecondsLeft, bool clockSynced)
        {
            double monotonicLeft = Math.Max(0, minimumEnd - now);
            if (!clockSynced) return monotonicLeft;
            return Math.Max(monotonicLeft, serverSecondsLeft + 0.5);
        }

        /// <summary>The earliest local time (monotonic seconds) at which completion is sensible.</summary>
        public static double MinimumEnd(double assignedAt, int configuredShiftSec, double playedSeconds) =>
            assignedAt + Math.Max(Math.Max(0, configuredShiftSec), Math.Max(0, playedSeconds)) + 0.5;
    }

    public enum ShiftStage { Idle, Starting, Playing, Waiting, Settling, Settled, Unconfirmed, Failed, Cancelled }

    /// <summary>
    /// Shift state machine (pure). Idle → Starting → Playing (one game at a time) → Waiting (for endsAt) → Settling →
    /// Settled. Settling can also go to Waiting (explicit SHIFT_NOT_FINISHED: safe to send again, with a new key),
    /// Unconfirmed (timeout/network/5xx: the write may have landed; retries reuse the same Idempotency-Key so the
    /// server replays instead of paying twice) or Failed (a definitive rejection). Cancelled from any live stage
    /// submits nothing.
    /// </summary>
    public sealed class ShiftFlow
    {
        private readonly Func<string> _newKey;
        private readonly List<int> _scores = new List<int>();

        public ShiftFlow(string careerId, Func<string> newKey = null)
        {
            CareerId = careerId;
            _newKey = newKey ?? (() => Guid.NewGuid().ToString("D"));
        }

        public string CareerId { get; }
        public ShiftStage Stage { get; private set; } = ShiftStage.Idle;
        public ShiftPlan Plan { get; private set; }
        public ShiftReceipt Receipt { get; private set; }
        public string ErrorKey { get; private set; }
        public string ErrorDetail { get; private set; }
        public string StartKey { get; private set; }
        public string CompletionKey { get; private set; }
        public int SettleAttempts { get; private set; }
        public int GameIndex => _scores.Count;
        public ResolvedGame CurrentGame => Plan != null && Stage == ShiftStage.Playing && _scores.Count < Plan.Games.Count ? Plan.Games[_scores.Count] : null;
        public IReadOnlyList<int> Scores => _scores;
        public bool IsTerminal => Stage == ShiftStage.Settled || Stage == ShiftStage.Failed || Stage == ShiftStage.Cancelled;
        public event Action<ShiftStage> StageChanged;

        public bool BeginStart()
        {
            if (Stage != ShiftStage.Idle) return false;
            StartKey = _newKey();
            Set(ShiftStage.Starting);
            return true;
        }

        public bool Assigned(ShiftPlan plan)
        {
            if (Stage != ShiftStage.Starting || plan == null || plan.Games.Count == 0) return false;
            Plan = plan;
            Set(ShiftStage.Playing);
            return true;
        }

        /// <summary>Record the score of the current game (clamped to its cap). After the last game → Waiting.</summary>
        public bool RecordScore(int score)
        {
            var game = CurrentGame;
            if (game == null) return false;
            _scores.Add(Scoring.Clamp(score, game.MaxScore));
            if (_scores.Count == Plan.Games.Count) Set(ShiftStage.Waiting);
            return true;
        }

        /// <summary>The completion body: exactly the assigned games, in order, once each, capped.</summary>
        public JObject CompletionBody()
        {
            if (Plan == null || _scores.Count != Plan.Games.Count) return null;
            var results = new JArray();
            for (int i = 0; i < Plan.Games.Count; i++)
                results.Add(new JObject { ["miniGameId"] = Plan.Games[i].Id, ["score"] = Scoring.Clamp(_scores[i], Plan.Games[i].MaxScore) });
            return new JObject { ["results"] = results };
        }

        public bool BeginSettle()
        {
            if (Stage != ShiftStage.Waiting && Stage != ShiftStage.Unconfirmed) return false;
            // A retry after an unknown outcome reuses the key; a first attempt (or one after an explicit "not yet") gets a new one.
            if (Stage == ShiftStage.Waiting || CompletionKey == null) CompletionKey = _newKey();
            SettleAttempts++;
            Set(ShiftStage.Settling);
            return true;
        }

        public bool Settled(ShiftReceipt receipt)
        {
            if (Stage != ShiftStage.Settling || receipt == null) return false;
            Receipt = receipt;
            Set(ShiftStage.Settled);
            return true;
        }

        /// <summary>SHIFT_NOT_FINISHED: the server said "not yet" and recorded nothing. Wait again; next send gets a new key.</summary>
        public bool NotFinishedYet()
        {
            if (Stage != ShiftStage.Settling) return false;
            CompletionKey = null;
            Set(ShiftStage.Waiting);
            return true;
        }

        public bool Unconfirmed(string errorKey, string detail = null)
        {
            if (Stage != ShiftStage.Settling) return false;
            ErrorKey = errorKey;
            ErrorDetail = detail;
            Set(ShiftStage.Unconfirmed);
            return true;
        }

        public bool Fail(string errorKey, string detail = null)
        {
            if (IsTerminal || Stage == ShiftStage.Idle) return false;
            ErrorKey = errorKey;
            ErrorDetail = detail;
            Set(ShiftStage.Failed);
            return true;
        }

        public bool Cancel()
        {
            if (IsTerminal || Stage == ShiftStage.Settling) return false; // an in-flight settle must resolve, not vanish
            Set(ShiftStage.Cancelled);
            return true;
        }

        private void Set(ShiftStage stage)
        {
            if (Stage == stage) return;
            Stage = stage;
            StageChanged?.Invoke(stage);
        }
    }
}
