using System.Collections.Generic;
using System.Globalization;

namespace CTLife.UI
{
    /// <summary>Localizable career/shift copy, including the original fictional moderation cases.</summary>
    public static class ShiftStrings
    {
        private static readonly Dictionary<string, string> Copy = new Dictionary<string, string>
        {
            ["jobs"] = "Jobs", ["careers"] = "Find your shift", ["loading"] = "Checking the jobs board…",
            ["offer"] = "Up to {0} Game Cash · {1}s", ["level"] = "Level {0}",
            ["work"] = "Work a shift", ["apply"] = "Apply", ["hired"] = "Hired: {0}",
            ["busy"] = "Shift in progress", ["empty"] = "No careers are available right now.",
            ["starting"] = "Clocking you in…", ["startingDetail"] = "Waiting for your shift assignment.",
            ["settling"] = "Sending your shift results…",
            ["settlingDetail"] = "Your score is ready. Pay is confirmed by the server.",
            ["waiting"] = "Shift closes in {0}s", ["waitingDetail"] = "Results are ready. Waiting for the shift's scheduled finish.",
            ["paid"] = "Shift settled", ["paidAmount"] = "{0} Game Cash", ["xp"] = "{0} XP",
            ["promoted"] = "Promotion confirmed", ["receipt"] = "SHIFT RECEIPT", ["shiftStatus"] = "CAREER SHIFT", ["back"] = "Return to city",
            ["confirmed"] = "Confirmed by the game server.",
            ["refreshFailed"] = "Pay is confirmed. The balance display could not refresh and may be out of date.",
            ["failed"] = "Shift not confirmed", ["failureDetail"] = "No payout is shown until the server confirms it. Check your balance before starting another shift.",
            ["requestTimeout"] = "The server did not confirm in time. This request may still have been processed.",
            ["waitingForServer"] = "The server is still running your shift…",
            ["waitTimeout"] = "The scheduled shift finish could not be verified. No results were submitted during this wait.",
            ["invalidReceipt"] = "The server returned an incomplete shift receipt. No payout is shown.",
            ["invalidAssignment"] = "This shift has an unsupported mini-game. No score was submitted.",
            ["invalidJob"] = "The server returned an incomplete job. Please try again later.",
            ["desk"] = "MOD DESK", ["title"] = "Keep the chat alive.",
            ["rules"] = "Ban seed-phrase requests, impersonation, repeated spam and threats. Allow questions, criticism and bad jokes.",
            ["practice"] = "PRACTICE · NO PAYOUT", ["live"] = "CAREER SHIFT · FICTIONAL COMMUNITY",
            ["case"] = "MESSAGE {0} / {1}", ["timer"] = "{0}s left", ["correctCount"] = "{0} correct",
            ["ban"] = "Ban", ["allow"] = "Allow", ["next"] = "Next message", ["finishReview"] = "Finish review",
            ["correct"] = "Good call.", ["incorrect"] = "That one slipped through.",
            ["clear"] = "Inbox cleared.", ["clearDetail"] = "{0} of {1} decisions correct. Stay on shift until the clock runs out.",
            ["clockNote"] = "The shift clock continues while you are away.",
            ["practiceDone"] = "Practice complete", ["practiceResult"] = "{0} / {1} correct · no Game Cash or XP awarded",
            ["playAgain"] = "Play again", ["practiceInterrupted"] = "Practice interrupted",
            ["practiceInterruptedDetail"] = "No Game Cash or XP awarded. You can try another practice round.",
            ["c0.author"] = "Wallet support", ["c0.message"] = "I can fix the missing airdrop. DM your seed phrase so I can resync it.",
            ["c0.reason"] = "A seed phrase is never a support ticket. Ban the request.",
            ["c1.author"] = "Chart auntie", ["c1.message"] = "The chart looks rough. Where can I read the team's original roadmap?",
            ["c1.reason"] = "A sceptical question is still a question. Criticism is allowed.",
            ["c2.author"] = "RocketCopy", ["c2.message"] = "BUY MY COIN. BUY MY COIN. BUY MY COIN. Same post, seventeenth time.",
            ["c2.reason"] = "Repeated promotion floods the conversation. Ban the spam.",
            ["c3.author"] = "Lunch break", ["c3.message"] = "Closed the trading tab. Opened a sandwich. Strong fundamentals.",
            ["c3.reason"] = "A bad market joke is not a moderation offence. Let it live.",
            ["c4.author"] = "Admin, apparently", ["c4.message"] = "I'm the founder's backup account. Send me SOL privately to unlock your role.",
            ["c4.reason"] = "An impersonated role plus a private payment demand is a ban.",
            ["c5.author"] = "Quiet builder", ["c5.message"] = "The demo crashed on my phone. I saved the error. Where should I report it?",
            ["c5.reason"] = "A useful bug report belongs in the conversation.",
            ["c6.author"] = "Caps lock", ["c6.message"] = "Disagree with my trade again and I'll find you and hurt you.",
            ["c6.reason"] = "A threat crosses the line. Ban it, regardless of the trade.",
            ["c7.author"] = "Zero leverage", ["c7.message"] = "I disagree with the bullish take. Revenue is down in the published report.",
            ["c7.reason"] = "Evidence-based disagreement is allowed. Moderation is not price support.",
            ["c8.author"] = "Recovery desk", ["c8.message"] = "Lost funds? Paste your wallet recovery words here and I'll restore them.",
            ["c8.reason"] = "Recovery words grant wallet access. Ban the request.",
            ["c9.author"] = "New tab", ["c9.message"] = "gm. First day here. Is there a channel for beginner questions?",
            ["c9.reason"] = "Welcome the newcomer. Asking for help is allowed.",
        };

        public static string T(string key) => Copy.TryGetValue(key, out var value) ? value : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
    }
}
