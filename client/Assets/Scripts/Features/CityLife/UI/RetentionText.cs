using System.Collections.Generic;
using System.Globalization;
using CTLife.Core;

namespace CTLife.CityLife
{
    /// <summary>Copy for the gm streak, the daily chest, Quests and the push pre-prompt (English).</summary>
    public static class RetentionText
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["tile.caption"] = "GM STREAK",
            ["tile.day"] = "Day {0}",
            ["tile.ready"] = "Open chest",
            ["tile.next"] = "Next chest {0}",
            ["tile.milestone"] = "MILESTONE",
            ["tile.access"] = "gm streak, day {0}. {1}",

            ["chest.kicker"] = "GM STREAK",
            ["chest.day"] = "DAY {0}",
            ["chest.milestone"] = "DAY {0} · MILESTONE CHEST",
            ["chest.tap"] = "Tap the chest",
            ["chest.opening"] = "Opening…",
            ["chest.collect"] = "COLLECT",
            ["chest.close"] = "Close",
            ["chest.cash"] = "CASH",
            ["chest.wardrobe"] = "In your wardrobe",
            ["chest.claimed"] = "Today's chest is yours",
            ["chest.claimedBody"] = "Come back after the reset for Day {0}.",
            ["chest.resetIn"] = "Resets in {0}",
            ["chest.reset"] = "You missed your gm, so the streak starts again at Day 1.",
            ["chest.saved"] = "{0} saved your streak.",
            ["chest.saved1"] = "A streak freeze",
            ["chest.savedN"] = "{0} streak freezes",
            ["chest.freezes"] = "Streak freezes {0}/{1}",
            ["chest.freezeHint"] = "A freeze covers one missed day.",
            ["chest.buyFreeze"] = "Buy one · {0}",
            ["chest.freezeFull"] = "Holding the max",
            ["chest.next"] = "{0} days to the Day {1} chest",
            ["chest.nextOne"] = "1 day to the Day {0} chest",
            ["chest.error"] = "The chest didn't open. {0}",

            ["push.title"] = "Keep your streak alive",
            ["push.body"] = "Turn on notifications and the city pings you before your gm streak resets, when today's clue drops, and when someone visits your place.",
            ["push.small"] = "4 a day at most. Never between 10 pm and 8 am.",
            ["push.on"] = "TURN ON",
            ["push.later"] = "Not now",

            ["app.title"] = "Quests",
            ["app.today"] = "Today",
            ["app.week"] = "This week",
            ["app.dailySub"] = "{0} of 3 done · new quests in {1}",
            ["app.weekSub"] = "Step {0} of {1} · resets in {2}",
            ["app.weekDone"] = "Chain complete · resets in {0}",
            ["app.claim"] = "Claim",
            ["app.claimed"] = "Claimed",
            ["app.go"] = "Go",
            ["app.locked"] = "Locked",
            ["app.reward"] = "{0} · {1} XP",
            ["app.empty"] = "Quests are loading.",
            ["app.paid"] = "+{0} · +{1} XP",
            ["app.streak"] = "Day {0}",
            ["app.streakSub"] = "gm streak · {0}",

            ["toast.daily"] = "Quest {0}/{1}: {2}",
            ["toast.weekly"] = "Weekly {0}/{1}: {2}",
            ["toast.progress"] = "{0} {1}/{2}",
        };

        public static string T(string key) => En.TryGetValue(key, out var v) ? NameTable.Apply(v) : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);
        public static string Money(long cents) => CTLife.Core.Money.Cash(cents);
    }
}
