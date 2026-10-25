using System;
using System.Collections.Generic;

namespace CTLife.CityLife
{
    /// <summary>The always-visible dream ladder's rungs (queue 18; brief: ct-life-evidence/onboarding-17/BRIEF.md).</summary>
    public enum LadderRung { Basement, Studio, Flat, Penthouse, Hovercar }

    /// <summary>Where the player is on the ladder and how far to the next rung.</summary>
    public sealed class LadderState
    {
        public LadderRung Current;
        /// <summary>Null at the top.</summary>
        public LadderRung? Next;
        /// <summary>The next rung's goal in Cash cents (weeks × its weekly rent); 0 when the next rung is not bought with Cash.</summary>
        public long GoalCents;
        public long ProgressCents;
        /// <summary>The hovercar is an entitlement (NFT / ops grant), never Cash: the UI links to the Duo Shop instead.</summary>
        public bool NextIsEntitlement;
        public float Fraction => GoalCents <= 0 ? (Next.HasValue ? 0f : 1f) : Math.Min(1f, Math.Max(0f, ProgressCents / (float)GoalCents));
        public bool CanReachNext => Next.HasValue && !NextIsEntitlement && GoalCents > 0 && ProgressCents >= GoalCents;
    }

    /// <summary>
    /// The ladder model (lane B, 2026-10-25), pure and EditMode-tested. Rungs follow the homes that exist (rented weekly,
    /// homes.json / home_units.json) plus the hovercar at the top. The current rung is the home rented now; the next
    /// rung's goal is <see cref="WeeksOfRent"/> weeks of its rent, in the Cash the player already earns (no new currency).
    /// The mapping of home kinds to rungs is one table, so the user's decision on studio vs flat / villa vs hovercar
    /// (brief, decisions 1-2) changes data, not code.
    /// </summary>
    public static class DreamLadder
    {
        public const int WeeksOfRent = 4;

        /// <summary>Home kind → rung (default: the queue's names; the walk-in flat doors are the Flat rung).</summary>
        public static readonly Dictionary<string, LadderRung> RungByHome = new Dictionary<string, LadderRung>(StringComparer.Ordinal)
        {
            ["parents_basement"] = LadderRung.Basement,
            ["hacker_house"] = LadderRung.Studio,
            ["studio"] = LadderRung.Studio,
            ["flat"] = LadderRung.Flat,
            ["penthouse"] = LadderRung.Penthouse,
            ["marina_villa"] = LadderRung.Penthouse,
        };

        /// <summary>The home whose rent prices each Cash rung (the cheapest home on it).</summary>
        public static readonly Dictionary<LadderRung, string> PriceHome = new Dictionary<LadderRung, string>
        {
            [LadderRung.Basement] = "parents_basement",
            [LadderRung.Studio] = "studio",
            [LadderRung.Flat] = "flat",
            [LadderRung.Penthouse] = "penthouse",
        };

        public static LadderRung RungOf(string homeKind, bool inFlatUnit) =>
            inFlatUnit ? LadderRung.Flat : homeKind != null && RungByHome.TryGetValue(homeKind, out var r) ? r : LadderRung.Basement;

        /// <param name="homeKind">The rented home's kind (`/me home`), null when homeless (the bottom rung).</param>
        /// <param name="inFlatUnit">The player holds a walk-in flat door.</param>
        /// <param name="cashCents">Cash now (debt does not count against the goal: rent is what the player pays from Cash).</param>
        /// <param name="hasHovercar">The `vehicle.hovercar` entitlement.</param>
        /// <param name="rentCentsByHome">Weekly rent per home kind (economy / ids.homes; the flat doors as "flat").</param>
        public static LadderState Compute(string homeKind, bool inFlatUnit, long cashCents, bool hasHovercar, IReadOnlyDictionary<string, long> rentCentsByHome)
        {
            var current = RungOf(homeKind, inFlatUnit);
            if (hasHovercar) current = LadderRung.Hovercar;
            var s = new LadderState { Current = current, ProgressCents = Math.Max(0, cashCents) };
            if (current == LadderRung.Hovercar) return s;
            var next = (LadderRung)((int)current + 1);
            s.Next = next;
            if (next == LadderRung.Hovercar) { s.NextIsEntitlement = true; return s; }
            if (PriceHome.TryGetValue(next, out var home) && rentCentsByHome != null && rentCentsByHome.TryGetValue(home, out var rent) && rent > 0)
                s.GoalCents = rent * WeeksOfRent;
            return s;
        }
    }
}
