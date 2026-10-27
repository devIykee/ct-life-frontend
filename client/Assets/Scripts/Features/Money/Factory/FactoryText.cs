using CTLife.Core;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace CTLife.Economy
{
    /// <summary>
    /// Rugpull Factory words for the service layer (pre-checks, refusals, the Launchpad Tower prompt captions). The
    /// Factory screens themselves wait for the approved UI brief (ct-life-evidence/factory-r5/ui-refs/BRIEF.md).
    /// Play money only: never "token", "invest" or a real launchpad's name.
    /// </summary>
    public static class FactoryText
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["check.nameLength"] = "Coin names are {0} to {1} characters.",
            ["check.nameChars"] = "Use letters, numbers, spaces and . ' ! _ - (start with a letter or number).",
            ["check.tickerLength"] = "Tickers are {0} to {1} letters or numbers.",
            ["check.tickerChars"] = "Tickers use letters and numbers only.",
            ["check.art"] = "Pick the coin's art.",
            ["check.liquidity"] = "Liquidity is {0} to {1}.",
            ["check.dev"] = "Your dev bag can be at most {0}.",
            ["err.name"] = "That name isn't allowed. Try another.",
            ["err.taken"] = "That ticker is taken. Try another.",
            ["err.cooldown"] = "The press is cooling down. Next launch in {0}.",
            ["err.limit"] = "Daily launch limit reached. Next one in {0}.",
            ["err.limitNoTime"] = "Daily launch limit reached. Come back tomorrow.",
            ["err.cash"] = "You need {0} more Cash.",
            ["err.jailed"] = "You can't launch from CT Jail.",
            ["err.validation"] = "Something in the launch form isn't right. Check it and try again.",
            ["launched"] = "${0} is live on the Pulse board.",
            ["ui.title"] = "LAUNCH A COIN",
            ["ui.step.art"] = "ART", ["ui.step.name"] = "NAME", ["ui.step.bag"] = "BAG",
            ["ui.next"] = "Next", ["ui.review"] = "Review",
            ["ui.name"] = "NAME", ["ui.ticker"] = "TICKER",
            ["ui.nameNote"] = "Letters, numbers and . ' ! _ -. Real people, brands and links get refused.",
            ["ui.liquidity"] = "LIQUIDITY", ["ui.bag"] = "YOUR BAG",
            ["ui.you"] = "YOU {0} %", ["ui.float"] = "FLOAT {0} %",
            ["ui.ifSell"] = "IF YOU SELL YOUR BAG EARLY", ["ui.rug"] = "RUG",
            ["ui.heatRange"] = "+{0} to +{1} heat",
            ["ui.noBag"] = "No bag, nothing to dump. You earn only from fees after 3x.",
            ["ui.bagPublic"] = "Everyone sees \"DEV BAG {0} %\" on your coin.",
            ["ui.total"] = "Total {0}", ["ui.totalDetail"] = "({0} liquidity + {1} fee)",
            ["ui.reviewArt"] = "Art: {0} \u00b7 Liquidity {1} \u00b7 Your bag {2} %",
            ["ui.reviewTotal"] = "Total {0} \u00b7 Launch {1} of {2} today",
            ["ui.hold"] = "HOLD TO LAUNCH", ["ui.holdHint"] = "Hold until the ring closes. Let go to cancel.",
            ["band.title"] = "COIN LAUNCHED", ["band.sub"] = "${0} IS LIVE ON PULSE", ["band.pulse"] = "Open in Pulse", ["band.done"] = "Done",
            ["rules.body"] = "Your coin's price is up to the market, and it's play Cash only. Keep a bag and you can sell it, but selling 30 %+ of the coin within an hour of people buying is a rug. Rugs get you stars and a Sleuth post. Reach 3x and you earn half of everyone's fees.",
            ["rules.ok"] = "Got it",
            ["rug.body"] = "Selling now counts as a rug if people bought your ${0} this hour. The Sleuth will post about you and you'll get +{1} to +{2} heat.",
            ["rug.keep"] = "Keep it", ["rug.sell"] = "Sell anyway",
            ["pulse.player"] = "PLAYER", ["pulse.rugged"] = "RUGGED", ["pulse.by"] = "by {0}",
            ["pulse.devBag"] = "DEV BAG {0} %", ["pulse.toMoon"] = "3x", ["pulse.moonHit"] = "3x hit. Creator fees on.",
            ["pulse.moonGoal"] = "3x = creator fees on", ["pulse.liquidity"] = "liquidity {0}", ["pulse.yourFees"] = "Your fees {0}",
            ["board.king"] = "KING OF THE TOWER", ["board.empty"] = "No player coins yet. Launch one at the desk.",
            ["board.mc"] = "MC {0}", ["board.new"] = "NEW \u00b7 ${0} by {1}",
            ["desk.sub"] = "{0} + liquidity \u00b7 {1} of {2} left today",
            ["desk.cooldown"] = "Next launch in {0}",
            ["desk.capped"] = "Daily limit · back in {0}",
            ["desk.cash"] = "You need {0} (fee + {1} minimum)",
            ["desk.soon"] = "Opens with the markets",
            ["desk.offline"] = "Offline",
        };

        public static string T(string key) => En.TryGetValue(key, out var v) ? NameTable.Apply(v) : key;
        public static string F(string key, params object[] a) => string.Format(CultureInfo.InvariantCulture, T(key), a ?? Array.Empty<object>());

        /// <summary>A countdown: "9:05" under an hour, "3h 12m" above.</summary>
        public static string Clock(long ms)
        {
            if (ms < 0) ms = 0;
            long s = (ms + 999) / 1000;
            if (s < 3600) return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
            long m = (s + 59) / 60;
            return (m / 60).ToString(CultureInfo.InvariantCulture) + "h " + (m % 60).ToString(CultureInfo.InvariantCulture) + "m";
        }
    }
}
