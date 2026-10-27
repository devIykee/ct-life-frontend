using System.Collections.Generic;
using System.Globalization;

namespace CTLife.CityLife
{
    /// <summary>Copy for the first session and the dream ladder (queue 17 + 18). Objective lines stay at 12 words or fewer;
    /// {Y:…} spans are the yellow keywords (rich text built by the overlays).</summary>
    public static class FirstText
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            // objective subtitles, one at a time
            ["obj.shift.rekt"] = "Head to {0} on Coast Highway: they're hiring. {1}",
            ["obj.shift.rektNear"] = "{0} at the Rekt Burger counter.",
            ["obj.shift.jobs"] = "Open {0} on your Duo and start your first {1}.",
            ["obj.trade"] = "Your first trade: put {0} on a coin in {1}. It's game Cash.",
            ["obj.quest"] = "Open {0} on your Duo: your starter quests pay.",
            ["obj.chest"] = "Open your {0} in Quests.",
            ["kw.rekt"] = "Rekt Burger", ["kw.clockIn"] = "Clock in", ["kw.jobs"] = "Jobs", ["kw.shift"] = "shift",
            ["kw.one"] = "$1", ["kw.pulse"] = "Pulse", ["kw.quests"] = "Quests", ["kw.chest"] = "GM chest",
            ["skip"] = "Skip intro ›",
            ["done.word"] = "FIRST DAY DONE", ["done.line"] = "Keep climbing: tap your ladder.",
            // the shift DM
            ["dm.rekt.title"] = "Rekt Burger", ["dm.rekt.body"] = "Shift starts now. Clock in at the counter.",
            ["dm.jobs.title"] = "Jobs", ["dm.jobs.body"] = "Your first shift is open. Pick one that fits your tribe.",
            // the starter card (client checklist on an older backend)
            ["starter.title"] = "STARTER · {0}/{1}",
            ["starter.shift"] = "Work a shift", ["starter.shift.desc"] = "Clock in at Rekt Burger or start a Jobs shift.",
            ["starter.trade"] = "Make a trade", ["starter.trade.desc"] = "Buy $1 of any coin in Pulse.",
            ["starter.chest"] = "Open your daily chest", ["starter.chest.desc"] = "The GM chest pays every day you show up.",
            // the ladder
            ["ladder.dream"] = "YOUR DREAM", ["ladder.noDream"] = "Pick a dream in your profile",
            ["ladder.title"] = "Dream ladder",
            ["ladder.you"] = "• you", ["ladder.next"] = "NEXT · save {0}", ["ladder.rent"] = "{0}/wk", ["ladder.done"] = "✓",
            ["ladder.phase"] = "{0}/wk · Phase {1}", ["ladder.shop"] = "Duo Shop ›", ["ladder.dreamBadge"] = "DREAM",
            ["ladder.owned"] = "Yours",
            ["ladder.progress"] = "{0} of {1} ({2} weeks of rent)",
            ["ladder.ready"] = "You have {0}: enough for {1}.",
            ["ladder.hovercarLine"] = "The hovercar is a right-to-drive pass from the Duo Shop. Cash never buys it.",
            ["ladder.topLine"] = "Penthouse and the hovercar: you made it.",
            ["ladder.earn"] = "How to earn →", ["ladder.moveIn"] = "Move in ›",
            ["ladder.earn.jobs"] = "Jobs", ["ladder.earn.pulse"] = "Pulse", ["ladder.earn.quests"] = "Quests",
            ["ladder.unlocked"] = "{0} UNLOCKED", ["ladder.unlockedLine"] = "You can afford the move. Open Home.",
            ["ladder.dmTitle"] = "Home", ["ladder.dmBody"] = "You can afford {0} now. Move in?",
            ["chip.dream"] = "Dream",
        };

        public static string T(string key) => En.TryGetValue(key, out var v) ? v : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);
        /// <summary>A yellow keyword (the GTA objective style).</summary>
        public static string Y(string s) => "<color=#FFD23F>" + s + "</color>";
    }
}
