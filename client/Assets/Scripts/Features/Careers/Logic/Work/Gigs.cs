using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// GET /gigs item. Contract `Gig {id, name, durationSec, payCents, cooldownSec}`. The backend checkout returns
    /// `{gigs: [{gigId, name, description, minPayCents, maxPayCents}]}`; both are read. Pay is always a server value.
    /// </summary>
    public sealed class GigRow
    {
        public string Id, Name, Description;
        public int? DurationSec, CooldownSec;
        public long? PayCents, MinPayCents, MaxPayCents;

        public static List<GigRow> ParseList(JToken body, out int malformed)
        {
            malformed = 0;
            var list = new List<GigRow>();
            var arr = CareersWire.List(body, "gigs");
            if (arr == null) { malformed = 1; return list; }
            foreach (var g in arr)
            {
                var id = CareersWire.Str(g, "id", "gigId");
                if (!CareersWire.IsSafeId(id) || list.Any(x => x.Id == id)) { malformed++; continue; }
                var row = new GigRow
                {
                    Id = id, Name = CareersWire.Str(g, "name") ?? id, Description = CareersWire.Str(g, "description"),
                    DurationSec = CareersWire.Int(g, "durationSec"), CooldownSec = CareersWire.Int(g, "cooldownSec"),
                    PayCents = CareersWire.Long(g, "payCents"), MinPayCents = CareersWire.Long(g, "minPayCents"), MaxPayCents = CareersWire.Long(g, "maxPayCents"),
                };
                if (row.PayCents < 0 || row.MinPayCents < 0 || row.MaxPayCents < 0 || row.CooldownSec < 0 || row.DurationSec < 0) { malformed++; continue; }
                list.Add(row);
            }
            return list;
        }

        /// <summary>Display pay: exact when the server gives payCents, a range for min/max, null when unknown.</summary>
        public string PayText()
        {
            if (PayCents.HasValue) return CareersWire.Cash(PayCents.Value);
            if (MinPayCents.HasValue && MaxPayCents.HasValue)
                return MinPayCents == MaxPayCents ? CareersWire.Cash(MinPayCents.Value) : CareersWire.Cash(MinPayCents.Value) + "–" + CareersWire.Cash(MaxPayCents.Value);
            return null;
        }
    }

    /// <summary>POST /gigs/{id}/run → `{payCents, outcomeId}` (backend adds runId, gigId, startedAt, endedAt).</summary>
    public sealed class GigReceipt
    {
        public string GigId, OutcomeId, RunId;
        public long PayCents;

        public static GigReceipt Parse(JToken body, string gigId)
        {
            if (!(body is JObject o)) return null;
            var pay = CareersWire.Long(o, "payCents");
            if (!pay.HasValue || pay.Value < 0) return null;
            var returned = CareersWire.Str(o, "gigId");
            if (!string.IsNullOrEmpty(returned) && returned != gigId) return null;
            return new GigReceipt { GigId = gigId, PayCents = pay.Value, OutcomeId = CareersWire.Str(o, "outcomeId"), RunId = CareersWire.Str(o, "runId") };
        }
    }

    /// <summary>
    /// Client-side cooldown display and write retention for gigs. The server enforces cooldowns (429 RATE_LIMITED with
    /// `details.retryAfterMs`); this only disables the button. Each run attempt carries one Idempotency-Key; an attempt
    /// with an unknown outcome keeps its key so "Check again" cannot pay twice.
    /// </summary>
    public sealed class GigLedger
    {
        private readonly Dictionary<string, double> _readyAt = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _pendingKey = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Func<string> _newKey;

        public GigLedger(Func<string> newKey = null) { _newKey = newKey ?? (() => Guid.NewGuid().ToString("D")); }

        public double SecondsLeft(string gigId, double now) => gigId != null && _readyAt.TryGetValue(gigId, out var t) ? Math.Max(0, t - now) : 0;
        public bool Ready(string gigId, double now) => SecondsLeft(gigId, now) <= 0;
        public bool HasUnconfirmed(string gigId) => gigId != null && _pendingKey.ContainsKey(gigId);

        /// <summary>The key for the next attempt: the retained one after an unknown outcome, else a fresh one.</summary>
        public string KeyFor(string gigId)
        {
            if (_pendingKey.TryGetValue(gigId, out var k)) return k;
            k = _newKey();
            _pendingKey[gigId] = k;
            return k;
        }

        /// <summary>A definitive answer (success or explicit rejection): the key is spent.</summary>
        public void Resolved(string gigId) => _pendingKey.Remove(gigId);

        public void StartCooldown(string gigId, double now, double seconds)
        {
            if (seconds <= 0) return;
            double until = now + Math.Min(seconds, 24 * 3600);
            if (!_readyAt.TryGetValue(gigId, out var existing) || existing < until) _readyAt[gigId] = until;
        }

        /// <summary>Reads `details.retryAfterMs` (contract example) or `retryAfterSec`.</summary>
        public static double? RetryAfterSeconds(JToken details)
        {
            var ms = CareersWire.Long(details, "retryAfterMs");
            if (ms.HasValue && ms.Value >= 0) return ms.Value / 1000.0;
            var s = CareersWire.Long(details, "retryAfterSec", "retryAfter");
            if (s.HasValue && s.Value >= 0) return s.Value;
            return null;
        }
    }
}
