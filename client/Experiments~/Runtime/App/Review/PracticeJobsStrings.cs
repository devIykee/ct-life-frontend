#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System.Collections.Generic;
using System.Globalization;

namespace CTLife.App.Review
{
    /// <summary>Localizable copy for the development practice board.</summary>
    internal static class PracticeJobsStrings
    {
        private static readonly Dictionary<string, string> Copy = new Dictionary<string, string>
        {
            ["title"] = "Jobs", ["heading"] = "Try a shift",
            ["practice"] = "PRACTICE · NO PAYOUT",
            ["intro"] = "Find your desk. These practice rounds award no Game Cash or XP.",
            ["noRewards"] = "Practice only. No Game Cash or XP awarded.",
            ["mod.title"] = "Mod Desk", ["mod.body"] = "Keep the chat alive. Spot scams, spare the bad jokes.",
            ["earnings.title"] = "Earnings Desk", ["earnings.body"] = "Read the numbers before the victory lap.",
            ["thread.title"] = "Thread Workshop", ["thread.body"] = "Three fragments. One story. Put the timeline back together.",
            ["router.title"] = "Raid Router", ["router.body"] = "One shield. Three chats. Keep the raid out and the conversation moving.",
            ["receipt.title"] = "Receipt Match",
            ["receipt.body"] = "Match paper receipts to statement entries. Amounts repeat; check the reference and date. Swap before you file.",
            ["role.title"] = "Role Lock", ["role.body"] = "Give each role only the access its task needs. Lock it before the next shift.",
            ["cues.title"] = "Live Cues", ["cues.body"] = "Run a fictional show. Hit Camera, Caption or Applause inside the gold timing window.",
            ["runway.title"] = "Runway Plan", ["runway.body"] = "Three changes. Keep the budget inside its limit without losing the service the team needs.",
            ["clip.title"] = "Clip Cut", ["clip.body"] = "Trim an original storyboard from Setup to Payoff. Preview your edit, then make the cut.",
            ["details"] = "{0}s · score out of {1}", ["play"] = "Play", ["busy"] = "Round in progress",
            ["end"] = "End practice", ["done"] = "Practice complete", ["score"] = "{0} / {1} points",
            ["again"] = "Play again", ["return"] = "Return to Jobs",
            ["interrupted"] = "Round interrupted", ["recovery"] = "Your practice round stopped. Try again when you’re ready.",
            ["unavailable"] = "This desk isn’t ready yet. Try another round.",
        };
        public static string T(string key) => Copy.TryGetValue(key, out string value) ? value : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
    }
}
#endif
