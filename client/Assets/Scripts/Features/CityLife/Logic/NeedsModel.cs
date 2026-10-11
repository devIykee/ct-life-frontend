using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    /// <summary>One `needs.json` action row (contract config).</summary>
    public sealed class NeedsAction
    {
        public string Id, Raises, Where;
        public int Amount, CooldownSec, DurationSec;
        public long CostCents;
        public readonly List<string> Venues = new List<string>();
    }

    public enum NeedLevel { Ok = 0, Low = 1, Critical = 2, Empty = 3 }

    /// <summary>
    /// `config.needs` (contract/config/needs.json): decay per real hour, bounds, actions, club venues. Two optional
    /// fields come from the FE proposal (docs/fe-contract-proposals/city-life-homes-needs.md): `warnings {low, critical}`
    /// and `consequences [{need, below, textId}]`. Until the server serves them the FE presentation defaults below apply
    /// (they only colour bars and choose warning copy; no rule is enforced client-side).
    /// </summary>
    public sealed class NeedsConfig
    {
        public static readonly string[] Ids = { "energy", "hunger", "fun", "hygiene", "sanity" };

        /// <summary>FE presentation defaults (the HUD already uses 25 as "low"). Proposed for needs.json.</summary>
        public const int DefaultLow = 25, DefaultCritical = 10;

        public readonly Dictionary<string, double> DecayPerHour = new Dictionary<string, double>(StringComparer.Ordinal);
        public readonly List<NeedsAction> Actions = new List<NeedsAction>();
        public readonly List<string> ClubVenues = new List<string>();
        public readonly List<(string need, int below, string textId)> Consequences = new List<(string, int, string)>();
        public int Min = 0, Max = 100, Low = DefaultLow, Critical = DefaultCritical;
        public bool WarningsFromServer;

        public static bool TryParse(JToken cfg, out NeedsConfig result, out string reason)
        {
            result = null;
            reason = "needs_missing";
            if (!(cfg is JObject o)) return false;
            var c = new NeedsConfig();
            reason = "decay_invalid";
            if (!(o["decayPerHour"] is JObject decay)) return false;
            foreach (var id in Ids)
            {
                var rate = CityWire.DoubleOr(decay[id], double.NaN);
                if (double.IsNaN(rate) || rate < 0) return false;
                c.DecayPerHour[id] = rate;
            }
            c.Min = CityWire.IntOr(o["minValue"], 0);
            c.Max = CityWire.IntOr(o["maxValue"], 100);
            if (c.Max <= c.Min) return false;
            if (o["actions"] is JArray actions)
            {
                foreach (var row in actions)
                {
                    var id = CityWire.Str(row?["id"]);
                    if (string.IsNullOrEmpty(id)) continue;
                    var a = new NeedsAction
                    {
                        Id = id,
                        Raises = CityWire.Str(row["raises"]),
                        Where = CityWire.Str(row["where"]) ?? "anywhere",
                        Amount = CityWire.IntOr(row["amount"], 0),
                        CooldownSec = CityWire.IntOr(row["cooldownSec"], 0),
                        DurationSec = CityWire.IntOr(row["durationSec"], 0),
                        CostCents = CityWire.LongOr(row["costCents"], 0),
                    };
                    if ((row["clubVenueIds"] ?? row["venues"]) is JArray venues)
                        foreach (var v in venues) { var s = CityWire.Str(v); if (!string.IsNullOrEmpty(s)) a.Venues.Add(s); }
                    c.Actions.Add(a);
                }
            }
            if (o["clubVenues"] is JArray clubs)
                foreach (var v in clubs) { var s = CityWire.Str(v); if (!string.IsNullOrEmpty(s)) c.ClubVenues.Add(s); }
            if (o["warnings"] is JObject w)
            {
                int low = CityWire.IntOr(w["low"], DefaultLow), critical = CityWire.IntOr(w["critical"], DefaultCritical);
                if (critical >= c.Min && low > critical && low <= c.Max) { c.Low = low; c.Critical = critical; c.WarningsFromServer = true; }
            }
            if (o["consequences"] is JArray cons)
                foreach (var row in cons)
                {
                    var need = CityWire.Str(row?["need"]);
                    var text = CityWire.Str(row?["textId"]);
                    if (Array.IndexOf(Ids, need) >= 0 && !string.IsNullOrEmpty(text)) c.Consequences.Add((need, CityWire.IntOr(row["below"], 0), text));
                }
            result = c;
            reason = null;
            return true;
        }

        public NeedsAction Action(string id) => Actions.Find(a => a.Id == id);

        public double Rate(string need) => DecayPerHour.TryGetValue(need, out var r) ? r : 0;

        public NeedLevel LevelOf(double value)
        {
            if (value <= Min) return NeedLevel.Empty;
            if (value < Critical) return NeedLevel.Critical;
            if (value < Low) return NeedLevel.Low;
            return NeedLevel.Ok;
        }
    }

    /// <summary>
    /// The last server reading of the five needs and when it was taken. Server-authoritative: the backend decays needs
    /// lazily on read ("Decay is computed on read by GET /v1/me", ct-life-backend application/needs.ts). Between reads
    /// the client projects the same linear decay from config for display only and refreshes /me periodically.
    /// </summary>
    public sealed class NeedsSnapshot
    {
        public readonly int[] Values = new int[5];
        public readonly double AtMs;

        public NeedsSnapshot(int energy, int hunger, int fun, int hygiene, int sanity, double atMs)
        {
            Values[0] = energy; Values[1] = hunger; Values[2] = fun; Values[3] = hygiene; Values[4] = sanity;
            AtMs = atMs;
        }

        public bool SameValues(NeedsSnapshot other)
        {
            if (other == null) return false;
            for (int i = 0; i < 5; i++) if (Values[i] != other.Values[i]) return false;
            return true;
        }
    }

    /// <summary>Display projection of needs between server reads. Pure.</summary>
    public static class NeedsProjection
    {
        /// <summary>value − decayPerHour × hoursElapsed, clamped to [min, max]. Never extrapolates into the past.</summary>
        public static double[] Project(NeedsSnapshot s, NeedsConfig cfg, double nowMs)
        {
            var r = new double[5];
            if (s == null || cfg == null) return r;
            double hours = Math.Max(0, (nowMs - s.AtMs) / 3_600_000.0);
            for (int i = 0; i < 5; i++)
            {
                double v = s.Values[i] - cfg.Rate(NeedsConfig.Ids[i]) * hours;
                r[i] = Math.Max(cfg.Min, Math.Min(cfg.Max, v));
            }
            return r;
        }

        /// <summary>Hours until <paramref name="value"/> decays to <paramref name="threshold"/> (0 if already at or below; +∞ if it never decays).</summary>
        public static double HoursUntil(double value, double ratePerHour, double threshold)
        {
            if (value <= threshold) return 0;
            if (ratePerHour <= 0) return double.PositiveInfinity;
            return (value - threshold) / ratePerHour;
        }

        /// <summary>The most urgent need: lowest projected value; ties go to the faster-decaying need.</summary>
        public static int MostUrgent(double[] projected, NeedsConfig cfg)
        {
            int best = 0;
            for (int i = 1; i < 5; i++)
            {
                if (projected[i] < projected[best] - 1e-9) best = i;
                else if (Math.Abs(projected[i] - projected[best]) < 1e-9 && cfg.Rate(NeedsConfig.Ids[i]) > cfg.Rate(NeedsConfig.Ids[best])) best = i;
            }
            return best;
        }

        /// <summary>Which action raises <paramref name="need"/> (first match in config order), or null.</summary>
        public static NeedsAction Remedy(string need, NeedsConfig cfg) => cfg?.Actions.Find(a => a.Raises == need);
    }

    /// <summary>
    /// Emits a warning when a need gets worse than the last level already warned about, and re-arms once the need
    /// recovers to Ok, so a decaying bar does not toast every second. Pure; tested.
    /// </summary>
    public sealed class NeedsWarningTracker
    {
        private readonly NeedLevel[] _warned = new NeedLevel[5];

        public List<(string need, NeedLevel level)> Update(double[] projected, NeedsConfig cfg)
        {
            var fresh = new List<(string, NeedLevel)>();
            if (projected == null || cfg == null) return fresh;
            for (int i = 0; i < 5; i++)
            {
                var level = cfg.LevelOf(projected[i]);
                if (level == NeedLevel.Ok) { _warned[i] = NeedLevel.Ok; continue; }
                if (level > _warned[i])
                {
                    _warned[i] = level;
                    fresh.Add((NeedsConfig.Ids[i], level));
                }
            }
            return fresh;
        }

        public void Reset() { for (int i = 0; i < 5; i++) _warned[i] = NeedLevel.Ok; }
    }

    public enum ActionGateResult { Allowed, ClubOnly, UnknownAction, Unavailable }

    /// <summary>Where an action may run (needs.json `where`). UI pre-check only; the server decides (VALIDATION_FAILED).</summary>
    public static class NeedsActionGate
    {
        public static ActionGateResult Check(NeedsConfig cfg, string actionId, string venueId)
        {
            if (cfg == null) return ActionGateResult.Unavailable;
            var a = cfg.Action(actionId);
            if (a == null) return ActionGateResult.UnknownAction;
            if (a.Where == "club_venues" || a.Where == "clubs")
            {
                var clubs = a.Venues.Count > 0 ? a.Venues : cfg.ClubVenues;
                return !string.IsNullOrEmpty(venueId) && clubs.Contains(venueId) ? ActionGateResult.Allowed : ActionGateResult.ClubOnly;
            }
            return ActionGateResult.Allowed;
        }

        public static IReadOnlyList<string> ClubsFor(NeedsConfig cfg, string actionId)
        {
            var a = cfg?.Action(actionId);
            return a != null && a.Venues.Count > 0 ? a.Venues : (IReadOnlyList<string>)(cfg?.ClubVenues ?? new List<string>());
        }
    }
}
