using System.Collections.Generic;
using System.Globalization;

namespace CTLife.Careers
{
    /// <summary>All careers-lane UI copy in one place (game content copy lives with each game).</summary>
    public static class CareersStrings
    {
        private static readonly Dictionary<string, string> Copy = new Dictionary<string, string>
        {
            // jobs app
            ["app.title"] = "Jobs", ["tab.careers"] = "Careers", ["tab.gigs"] = "Side gigs",
            ["pill.work"] = "Work", ["pill.busy"] = "Busy", ["pill.level"] = "Lv {0}", ["pill.soon"] = "Soon",
            ["best"] = "Best pay right now", ["rowPay"] = "Up to {0} a shift · {1}s", ["heldLine"] = "Level {0} · {1}",
            ["ladderPay"] = "Up to {0} · {1}s · {2} XP",
            ["loading"] = "Checking the jobs board…", ["empty"] = "No careers are on the board right now.",
            ["retry"] = "Try again", ["back"] = "Back to jobs", ["timeout"] = "The jobs board didn't answer in time.",
            ["status.hired"] = "Your job", ["status.apply"] = "Hiring", ["status.gated"] = "Needs {0} {1} (you: {2})",
            ["status.phase"] = "Opens in phase {0}", ["status.notOffered"] = "Not hiring right now",
            ["level"] = "Level {0} · {1}", ["xp"] = "{0} XP", ["pay"] = "Up to {0} a shift · {1}s",
            ["payUnknown"] = "Pay set by the server", ["payNote"] = "Pay scales with your score. The server confirms every payout.",
            ["work"] = "Work a shift", ["apply"] = "Apply", ["details"] = "Details", ["practice"] = "Practice",
            ["busy"] = "Shift in progress", ["hired"] = "Hired: {0}", ["applyFailed"] = "Application not confirmed: {0}",
            ["ladder"] = "Career ladder", ["ladderRow"] = "L{0} · {1} · up to {2} · {3}s · {4} XP",
            ["ladderNow"] = "you are here", ["games"] = "Shift games", ["gameAssigned"] = "On your shift at this level",
            ["gameProposed"] = "Practice now; paid shifts once the server adds it",
            ["topLevel"] = "Top of the ladder.", ["next"] = "Next level pays up to {0}.",
            ["promotionNote"] = "Promotions are decided by the server from your shift results.",
            ["venue"] = "Works at {0}",
            // shift
            ["shift.eyebrow"] = "CAREER SHIFT", ["shift.starting"] = "Clocking you in…", ["shift.startingDetail"] = "Waiting for your shift assignment.",
            ["shift.game"] = "Game {0} of {1}", ["shift.waiting"] = "Shift closes in {0}s", ["shift.waitingDetail"] = "Your scores are ready. The server settles the shift after its scheduled end.",
            ["shift.settling"] = "Sending your shift results…", ["shift.settlingDetail"] = "Pay is confirmed by the server.",
            ["shift.paid"] = "Shift settled", ["shift.paidAmount"] = "{0} Game Cash", ["shift.xp"] = "{0} XP", ["shift.promoted"] = "Promoted!",
            ["shift.scores"] = "Scores sent", ["shift.done"] = "Return to city",
            ["shift.unconfirmed"] = "Not confirmed yet", ["shift.unconfirmedDetail"] = "The server didn't answer in time. Your shift may already be paid. Check again: it can't pay twice.",
            ["shift.checkAgain"] = "Check again", ["shift.giveUp"] = "Close",
            ["shift.failed"] = "Shift not confirmed", ["shift.failedDetail"] = "No payout is shown until the server confirms it.",
            ["shift.invalidAssignment"] = "This shift assignment looks broken. Nothing was played or submitted.",
            ["shift.unknownGame"] = "This shift uses a game this app version doesn't have. Update the app. Nothing was submitted.",
            ["shift.crossCareer"] = "The server assigned another career's game. Nothing was submitted.",
            ["shift.invalidReceipt"] = "The server sent an incomplete receipt. No payout is shown.",
            ["shift.abandoned"] = "Shift abandoned. Nothing was submitted.",
            ["shift.alreadySettled"] = "The server says this shift was already paid by an earlier attempt. Your balance is refreshed.",
            ["shift.balanceStale"] = "Pay is confirmed. Your balance display may be out of date.",
            ["shift.requestTimeout"] = "The server didn't answer in time.",
            // mini-game host
            ["mg.practice"] = "PRACTICE · NO PAY", ["mg.shift"] = "SHIFT · PAYS GAME CASH", ["mg.howTo"] = "How to play", ["mg.scoring"] = "Scoring",
            ["mg.basedOn"] = "Based on: {0}", ["mg.limits"] = "{0}s · max score {1}", ["mg.start"] = "Start", ["mg.quit"] = "Quit",
            ["mg.time"] = "{0}s", ["mg.score"] = "Score {0}", ["mg.pause"] = "Pause", ["mg.resume"] = "Resume",
            ["mg.paused"] = "Paused", ["mg.pausedPlayer"] = "The clock is stopped and the board is hidden.",
            ["mg.pausedInterrupted"] = "You left the game, so we paused it. The clock is stopped.",
            ["mg.pauseLimit"] = "Pause allowance left: {0}s", ["mg.endRound"] = "End round and keep score", ["mg.abandon"] = "Abandon",
            ["mg.abandonShift"] = "Abandon shift (nothing is submitted)",
            ["mg.results"] = "Results", ["mg.finalScore"] = "{0} / {1}", ["mg.continue"] = "Continue", ["mg.again"] = "Play again",
            ["mg.end.complete"] = "All done before the clock.", ["mg.end.time"] = "Time's up.", ["mg.end.player"] = "You ended the round.",
            ["mg.end.pauseLimit"] = "Paused too long, so the round closed with your score so far.",
            ["mg.file"] = "File", ["mg.clear"] = "Clear", ["mg.correct"] = "Right call", ["mg.wrong"] = "Wrong call", ["mg.answer"] = "Answer: {0}",
            ["mg.flags"] = "{0} flagged", ["mg.boardOf"] = "{0} of {1}", ["mg.yourOrder"] = "Your order", ["mg.tapToAdd"] = "Tap items in order",
            ["mg.selectLeft"] = "Tap a card on the left, then its match", ["mg.pairedWith"] = "→ {0}", ["mg.pick"] = "Pick one",
            ["mg.total"] = "Total {0} {1}", ["mg.band"] = "Target {0}–{1} {2}", ["mg.pass"] = "Passed", ["mg.fail"] = "Missed",
            ["mg.waitingNext"] = "Next one coming…", ["mg.missedWindow"] = "Window closed", ["mg.passedRight"] = "Right to pass: never worth it.",
            ["mg.captured"] = "Captured {0}% of the best moment", ["mg.loss"] = "That was a loss.",
            ["mg.cue"] = "{0} · {1}s", ["mg.cooldown"] = "{0} ({1}s)", ["mg.inBand"] = "In the green", ["mg.outBand"] = "Out of the green",
            ["mg.unavailable"] = "This game can't start right now.",
            // grades and stats
            ["grade.clean"] = "We're so back", ["grade.solid"] = "Solid shift", ["grade.mid"] = "Mid", ["grade.rough"] = "Skill issue",
            ["stat.correct"] = "Right", ["stat.wrong"] = "Wrong", ["stat.unanswered"] = "Unanswered", ["stat.bestStreak"] = "Best streak",
            ["stat.found"] = "Found", ["stat.falseFlags"] = "False flags", ["stat.missed"] = "Missed", ["stat.boards"] = "Filed",
            ["stat.inPlace"] = "In place", ["stat.perfectBoards"] = "Perfect boards", ["stat.pairs"] = "Correct pairs",
            ["stat.passed"] = "Passed", ["stat.failed"] = "Failed", ["stat.sharp"] = "Sharp calls", ["stat.losses"] = "Losses",
            ["stat.passes"] = "Smart passes", ["stat.windows"] = "Windows", ["stat.inBand"] = "In the green", ["stat.costs"] = "Fees paid",
            ["stat.answered"] = "Handled", ["stat.misfires"] = "Misfires",
            // gigs
            ["gigs.loading"] = "Finding side gigs…", ["gigs.empty"] = "No side gigs right now.", ["gigs.run"] = "Do it",
            ["gigs.cooldown"] = "Ready in {0}", ["gigs.pay"] = "Pays {0}", ["gigs.duration"] = "~{0}s", ["gigs.every"] = "Once every {0}",
            ["gigs.paid"] = "+{0} Game Cash", ["gigs.unconfirmed"] = "Not confirmed. Tap again to check: it can't pay twice.",
            ["gigs.rateLimited"] = "Too soon. Try again in {0}.", ["gigs.failed"] = "Gig not confirmed: {0}", ["gigs.phase"] = "Side gigs open in phase {0}.",
            ["gigs.note"] = "Pay is set and confirmed by the server.",
        };

        public static string T(string key) => key != null && Copy.TryGetValue(key, out var v) ? v : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);

        public static string Duration(double seconds)
        {
            if (seconds < 60) return ((int)System.Math.Ceiling(seconds)).ToString(CultureInfo.InvariantCulture) + "s";
            if (seconds < 3600) return ((int)System.Math.Ceiling(seconds / 60)).ToString(CultureInfo.InvariantCulture) + " min";
            return (seconds / 3600).ToString("0.#", CultureInfo.InvariantCulture) + " h";
        }

        public static string SkillName(string skill) => string.IsNullOrEmpty(skill) ? skill : char.ToUpperInvariant(skill[0]) + skill.Substring(1);
    }
}
