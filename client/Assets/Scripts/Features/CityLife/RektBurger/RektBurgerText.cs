using System.Collections.Generic;
using System.Globalization;

namespace CTLife.CityLife.RektBurger
{
    /// <summary>Copy for Rekt Burger and the career cards (English). Our own parody brand and jokes; no real chain's name or slogans.</summary>
    public static class RbText
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["name"] = "Rekt Burger",
            ["tagline"] = "Blew your port? Flip burgers, rebuild the stack.",
            ["card.kicker"] = "FALLBACK JOB",
            ["card.unlocked"] = "Hiring · {0}/hour · {1} left today",
            ["card.locked"] = "Hiring people who blew their port",
            ["card.lockedHint"] = "Opens when your Cash hits zero with debt, or a liquidation leaves you under {0}. Trenchers start here.",
            ["card.cap"] = "Today's pay is maxed. Back after the reset ({0}).",
            ["card.work"] = "Clock in",
            ["card.clockout"] = "Clock out",
            ["card.where"] = "On the Coast Highway in The Trenches. Clock in at the counter, or here.",
            ["card.leave"] = "Hang up the apron",
            ["card.employed"] = "On the crew · {0} shifts this stint",
            ["reason.trencher"] = "Trencher's safety net",
            ["reason.broke"] = "You hit zero with debt",
            ["reason.liquidated"] = "Liquidated",

            ["clock.tooFar"] = "Clock in at the Rekt Burger counter (Coast Highway, The Trenches).",
            ["clock.position"] = "Walk up to the counter, then clock in.",
            ["clock.cap"] = "You've earned today's maximum here. Back after the reset.",
            ["nudge.title"] = "Blew your port?",
            ["nudge.body"] = "Rekt Burger is hiring. Short shifts, real Cash, zero judgement.",
            ["unlocked.title"] = "Rekt Burger is hiring",
            ["unlocked.body"] = "A job opened for you: {0}. Tap to clock in.",


            ["promo.kicker"] = "PROMOTED",
            ["promo.rank"] = "Rank {0} · {1}",
            ["promo.bonus"] = "+{0}% pay on {1} shifts",
            ["promo.access"] = "Opens: {0}",
            ["promo.cosmetic"] = "New wardrobe item",
            ["promo.ok"] = "Nice",
            ["promo.view"] = "View career",

            // the clock-in UI (brief 2026-10-25)
            ["prompt.ready"] = "{0}/hr \u00b7 {1} left today",
            ["prompt.only"] = "Only {0} left today",
            ["prompt.back"] = "Back in {0}",
            ["prompt.offline"] = "Offline \u00b7 clock in when you're back",
            ["prompt.soFar"] = "+{0} so far",
            ["chip.caption"] = "ON SHIFT \u00b7 REKT BURGER",
            ["chip.cap"] = "CAP HIT",
            ["chip.offline"] = "OFFLINE \u00b7 PAY SYNCS LATER",
            ["chip.left"] = "{0} left today",
            ["chip.capLeft"] = "Today's pay is maxed",
            ["chip.needsNet"] = "Needs a connection",
            ["chip.ends"] = "ends {0}",
            ["chip.out"] = "Clock out",
            ["chip.confirm"] = "Tap to confirm",
            ["band.clockedOut"] = "CLOCKED OUT",
            ["band.capKicker"] = "DAILY CAP HIT",
            ["band.timedOut"] = "SHIFT TIMED OUT",
            ["band.away"] = "WHILE YOU WERE AWAY",
            ["band.title"] = "SHIFT DONE",
            ["band.maxed"] = "MAXED OUT",
            ["band.where"] = "Rekt Burger \u00b7 The Trenches",
            ["band.fullLength"] = "The shift ran its full length.",
            ["band.tiny"] = "Work at least a minute to get paid.",
            ["band.minutes"] = "MINUTES",
            ["band.cash"] = "CASH EARNED",
            ["band.xp"] = "CAREER XP",
            ["band.capLabel"] = "Today's cap",
            ["band.capLeft"] = "{0} left \u00b7 resets in {1}",
            ["band.capLeftNoReset"] = "{0} left",
            ["band.capBack"] = "Come back in {0}",
            ["band.capHit"] = "Maxed for today",
            ["band.rankXp"] = "{0} / {1} XP \u00b7 {2} XP to the next rank",
            ["band.rankTop"] = "{0} \u00b7 top rank",
            ["band.promoted"] = "Promoted!",
            ["band.uniform"] = "New: Rekt Burger uniform",
            ["band.collect"] = "Collect",
            ["band.again"] = "Clock in again",
            ["band.jobs"] = "Jobs",

            ["leave.glow"] = "Glow-up! Posted to your Timeline.",
            ["leave.done"] = "Apron hung up.",
        };

        public static string T(string key) => En.TryGetValue(key, out var v) ? v : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);
        public static string Money(long cents) => CTLife.Core.Money.Cash(cents);
    }
}
