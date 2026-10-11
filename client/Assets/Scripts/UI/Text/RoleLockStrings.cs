using System.Collections.Generic;
using System.Globalization;

namespace CTLife.UI
{
    /// <summary>Original fictional access tasks; no real project, person, post or admin action.</summary>
    public static class RoleLockStrings
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["desk"] = "ACCESS DESK", ["readyOne"] = "1 permission granted",
            ["title"] = "Role Lock", ["practice"] = "LOCAL PRACTICE · NO PAYOUT",
            ["review"] = "REVIEW · SCRIPTED INPUT · NO PAYOUT", ["score"] = "{0} / 100", ["timer"] = "{0}s",
            ["board"] = "Panel {0} / 5", ["instruction"] = "Grant only the listed tasks. Every other permission stays off.",
            ["role"] = "ROLE / TASK", ["off"] = "Off", ["on"] = "Granted", ["extra"] = "Extra", ["missing"] = "Needed",
            ["cell"] = "{0}: {1} - {2}", ["clear"] = "Clear", ["lock"] = "Lock access", ["next"] = "Next panel",
            ["pause"] = "Pause", ["resume"] = "Resume", ["end"] = "End practice", ["paused"] = "Paused",
            ["pauseDetail"] = "Your practice clock is on hold. Return when you’re ready.",
            ["ready"] = "{0} permissions granted", ["readyDetail"] = "You can change any switch before locking.",
            ["correct"] = "Just enough access · +20", ["incorrect"] = "Access needs another look · +0",
            ["mistakes"] = "Extra: {0} · Missing: {1}. Marked switches show the changes needed.",
            ["allLocked"] = "All five panels locked", ["waiting"] = "Review your last panel while the practice clock finishes.",
            ["again"] = "Play again", ["back"] = "Back to city", ["complete"] = "Practice complete",
            ["summary"] = "Exact panels: {0} / 5 · Locked: {1}\nExtra grants: {2} · Missing grants: {3}",
            ["board.0.title"] = "Launch-night access",
            ["board.0.role.0"] = "Host", ["board.0.task.0"] = "Pin posts only.",
            ["board.0.role.1"] = "Mod", ["board.0.task.1"] = "Pin posts + time out.",
            ["board.0.role.2"] = "Guest", ["board.0.task.2"] = "No permissions.",
            ["board.0.permission.0"] = "Pin posts", ["board.0.permission.1"] = "Time out", ["board.0.permission.2"] = "Edit roles",
            ["board.0.joke"] = "The countdown needs a pin. It does not need three new admins.",
            ["board.0.success"] = "Countdown pinned. Nobody got promoted by accident.",
            ["board.1.title"] = "Community call",
            ["board.1.role.0"] = "Host", ["board.1.task.0"] = "Queue guests + edit title.",
            ["board.1.role.1"] = "Stage mod", ["board.1.task.1"] = "Mute mics only.",
            ["board.1.role.2"] = "Listener", ["board.1.task.2"] = "No permissions.",
            ["board.1.permission.0"] = "Queue guests", ["board.1.permission.1"] = "Mute mics", ["board.1.permission.2"] = "Edit title",
            ["board.1.joke"] = "The title says “quick catch-up”. Access cannot enforce that.",
            ["board.1.success"] = "Guests queued. The title survived the warm-up.",
            ["board.2.title"] = "Bot cleanup",
            ["board.2.role.0"] = "Reviewer", ["board.2.task.0"] = "Read logs only.",
            ["board.2.role.1"] = "Sweeper", ["board.2.task.1"] = "Read logs + remove bots.",
            ["board.2.role.2"] = "Greeter", ["board.2.task.2"] = "Create invites only.",
            ["board.2.permission.0"] = "Read logs", ["board.2.permission.1"] = "Remove bots", ["board.2.permission.2"] = "Create invites",
            ["board.2.joke"] = "Forty-seven accounts said GM in the same millisecond.",
            ["board.2.success"] = "Bots out. The audit trail stayed in the room.",
            ["board.3.title"] = "Demo day",
            ["board.3.role.0"] = "Producer", ["board.3.task.0"] = "Edit schedule + post recap.",
            ["board.3.role.1"] = "Builder", ["board.3.task.1"] = "Upload slides only.",
            ["board.3.role.2"] = "Recapper", ["board.3.task.2"] = "Post recap only.",
            ["board.3.permission.0"] = "Edit schedule", ["board.3.permission.1"] = "Upload slides", ["board.3.permission.2"] = "Post recap",
            ["board.3.joke"] = "The demo works locally. The slide permissions should too.",
            ["board.3.success"] = "Slides uploaded. The schedule still has an owner.",
            ["board.4.title"] = "Weekend cover",
            ["board.4.role.0"] = "Support", ["board.4.task.0"] = "Reply tickets + close tickets.",
            ["board.4.role.1"] = "Trainee", ["board.4.task.1"] = "Reply tickets only.",
            ["board.4.role.2"] = "Greeter", ["board.4.task.2"] = "Change badges only.",
            ["board.4.permission.0"] = "Reply tickets", ["board.4.permission.1"] = "Close tickets", ["board.4.permission.2"] = "Change badges",
            ["board.4.joke"] = "A shiny badge is not a support qualification.",
            ["board.4.success"] = "Tickets answered. The badge printer remains supervised."
        };
        public static string T(string key) => En.TryGetValue(key, out var value) ? value : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
        public static string Board(RoleLockBoard board, string key) => T("board." + board.Story + "." + key);
        public static string Summary(RoleLockResult result) => F("summary", result.CorrectBoards, result.LockedBoards, result.ExtraGrants, result.MissingGrants);
    }
}
