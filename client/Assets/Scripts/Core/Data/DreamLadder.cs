using System;
using System.Collections.Generic;

namespace CTLife.Core
{
    /// <summary>
    /// The always-visible dream ladder's rungs (queue 18, approved 2026-10-25: ct-life-evidence/onboarding-17/BRIEF.md).
    /// The real rent steps are Basement $20 → Hacker House $60 → Studio $180 → Penthouse $1,500 a week. Penthouse is the
    /// top Cash rung. The hovercar sits above it as a locked "Dream" badge: an entitlement that Cash never buys.
    /// </summary>
    public enum LadderRung { Basement, HackerHouse, Studio, Penthouse, Hovercar }

    /// <summary>Where the player is on the ladder and how far to the next rung.</summary>
    public sealed class LadderState
    {
        public LadderRung Current;
        /// <summary>Null at the top.</summary>
        public LadderRung? Next;
        /// <summary>The next rung's goal in Cash cents (weeks × its weekly rent); 0 when the next rung is not bought with Cash.</summary>
        public long GoalCents;
        public long ProgressCents;
        /// <summary>The next rung is the hovercar: an entitlement (NFT / ops grant), never Cash. The UI opens its Duo Shop page.</summary>
        public bool NextIsEntitlement;
        public bool HasHovercar;
        public float Fraction => GoalCents <= 0 ? (Next.HasValue ? 0f : 1f) : Math.Min(1f, Math.Max(0f, ProgressCents / (float)GoalCents));
        public bool CanReachNext => Next.HasValue && !NextIsEntitlement && GoalCents > 0 && ProgressCents >= GoalCents;
    }

    /// <summary>
    /// The ladder model (lane B), pure and EditMode-tested. The current rung is the home rented now (`/me home`). The next
    /// rung's goal is <see cref="WeeksOfRent"/> weeks of its rent, in the Cash the player already earns (no new
    /// currency). A walk-in flat door rents at the Studio's $180, so it sits on the Studio rung. Marina Villa is above
    /// Penthouse in price but on its rung (the Cash top).
    /// </summary>
    public static class DreamLadder
    {
        public const int WeeksOfRent = 4;

        public static readonly LadderRung[] Cash = { LadderRung.Basement, LadderRung.HackerHouse, LadderRung.Studio, LadderRung.Penthouse };

        /// <summary>Home kind → rung.</summary>
        public static readonly Dictionary<string, LadderRung> RungByHome = new Dictionary<string, LadderRung>(StringComparer.Ordinal)
        {
            ["parents_basement"] = LadderRung.Basement,
            ["hacker_house"] = LadderRung.HackerHouse,
            ["studio"] = LadderRung.Studio,
            ["flat"] = LadderRung.Studio,
            ["penthouse"] = LadderRung.Penthouse,
            ["marina_villa"] = LadderRung.Penthouse,
        };

        /// <summary>The home that is each Cash rung (its rent prices the goal; moving there is the rung).</summary>
        public static readonly Dictionary<LadderRung, string> HomeOf = new Dictionary<LadderRung, string>
        {
            [LadderRung.Basement] = "parents_basement",
            [LadderRung.HackerHouse] = "hacker_house",
            [LadderRung.Studio] = "studio",
            [LadderRung.Penthouse] = "penthouse",
        };

        public static LadderRung RungOf(string homeKind, bool inFlatUnit) =>
            inFlatUnit ? LadderRung.Studio : homeKind != null && RungByHome.TryGetValue(homeKind, out var r) ? r : LadderRung.Basement;

        /// <param name="homeKind">The rented home's kind (`/me home`), null when homeless (the bottom rung).</param>
        /// <param name="inFlatUnit">The player holds a walk-in flat door.</param>
        /// <param name="cashCents">Cash now (debt is not progress: the goal is Cash to pay rent with).</param>
        /// <param name="hasHovercar">The `vehicle.hovercar` entitlement: the badge lights, the ladder is complete.</param>
        /// <param name="rentCentsByHome">Weekly rent per home kind (economy.rentCentsPerWeek / ids.homes).</param>
        public static LadderState Compute(string homeKind, bool inFlatUnit, long cashCents, bool hasHovercar, IReadOnlyDictionary<string, long> rentCentsByHome)
        {
            var current = RungOf(homeKind, inFlatUnit);
            var s = new LadderState { Current = current, ProgressCents = Math.Max(0, cashCents), HasHovercar = hasHovercar };
            if (current == LadderRung.Penthouse)
            {
                if (hasHovercar) return s;   // the top: penthouse and the dream badge
                s.Next = LadderRung.Hovercar; s.NextIsEntitlement = true;
                return s;
            }
            var next = (LadderRung)((int)current + 1);
            s.Next = next;
            if (HomeOf.TryGetValue(next, out var home) && rentCentsByHome != null && rentCentsByHome.TryGetValue(home, out var rent) && rent > 0)
                s.GoalCents = rent * WeeksOfRent;
            return s;
        }

        /// <summary>The starting rung for a birth-lottery home (the lottery card's "you start here").</summary>
        public static int StartIndex(string homeKind) => (int)RungOf(homeKind, false);
    }
}
