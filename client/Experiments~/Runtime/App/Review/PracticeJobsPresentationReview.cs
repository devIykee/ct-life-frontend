#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.App.Review
{
    /// <summary>Real phone tile/card callbacks, timed zero-answer completion, replay and cancellation. No answer automation.</summary>
    public static class PracticeJobsPresentationReview
    {
        public static async Task Run(PhoneShell phone, PracticeJobsApp app, CancellationToken owner)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner);
            deadline.CancelAfter(TimeSpan.FromSeconds(135));
            var ct = deadline.Token;
            var ui = UiRoot.Instance;
            Need(ui != null && app != null, "Practice review requires actual review phone/app.");
            var failures = new List<string>();
            void OnLog(string message, string stack, LogType type)
            { if (type == LogType.Error || type == LogType.Exception) failures.Add(message); }
            Application.logMessageReceived += OnLog;
            try
            {
                phone.Open(); await Frames(3, ct);
                var jobs = ui.PhoneLayer.Query<Button>(className: "ct-app").ToList()
                    .Where(b => b.Q<Label>(className: "ct-app-label")?.text == PracticeJobsStrings.T("title")).ToList();
                Need(jobs.Count == 1, "Phone must expose exactly one Jobs icon.");
                Submit(jobs[0]); await Frames(3, ct);
                CheckBoard(ui);
                int started = app.RoundsStarted;
                var first = ui.PhoneLayer.Q<Button>("practice-play-mod");
                double began = Time.realtimeSinceStartupAsDouble;
                Submit(first); Submit(first); // actual same callback, duplicate must not mount another game
                await Frames(3, ct);
                Need(!phone.IsOpen && app.IsRunning && app.RoundsStarted == started + 1, "Phone close canceled the round or duplicate started a second one.");
                Need(ui.ModalLayer.Children().Count(x => x.ClassListContains("ct-desk-overlay")) == 1, "Expected exactly one game overlay.");
                Need(app.ActiveGame != null && app.ActiveGame.Q<Label>(className: "ct-desk-context")?.text == ShiftStrings.T("practice"), "Real Mod Desk practice view missing.");
                CheckEnd(app.ActiveGame, ui);
                var timer = app.ActiveGame.Q<Label>(className: "ct-desk-timer");
                string initial = timer.text;
                double timerCheck = Time.realtimeSinceStartupAsDouble;
                while (Time.realtimeSinceStartupAsDouble - timerCheck < 1.2) await Awaitable.NextFrameAsync(ct);
                Need(timer.text != initial && app.IsRunning, "Practice timer did not advance while phone was closed.");
                // Let its real contract deadline expire without answering anything; do not shorten or synthesize a score.
                while (app.Result == null) await Awaitable.NextFrameAsync(ct);
                Need(Time.realtimeSinceStartupAsDouble - began >= 45, "The configured 45-second round was shortened.");
                Need(!app.IsRunning && app.ActiveGame == null && !phone.IsOpen, "Completed game retained an active round/phone.");
                Need(app.Result.Query<Label>().ToList().Any(l => l.text == PracticeJobsStrings.F("score", 0, 10)), "Unanswered round must show its real zero score.");
                Submit(app.Result.Q<Button>("practice-again")); await Frames(3, ct);
                Need(app.RoundsStarted == started + 2 && app.ActiveGame != null && app.Result == null, "Again did not launch a fresh real round.");
                while (app.Result == null) await Awaitable.NextFrameAsync(ct);
                Submit(app.Result.Q<Button>("practice-return")); await Frames(3, ct);
                Need(phone.IsOpen && !app.IsRunning && app.Result == null && app.ActiveGame == null, "Result Return did not open Jobs cleanly.");
                Submit(ui.PhoneLayer.Q<Button>("practice-play-mod")); await Frames(3, ct);
                Submit(app.ActiveGame.Q<Button>("practice-end")); await Frames(4, ct);
                Need(!app.IsRunning && app.ActiveGame == null && app.Result == null && phone.IsOpen, "End practice did not cleanly return to Jobs.");
                CheckBoard(ui);
                foreach (string key in new[] { "earnings", "thread" })
                {
                    var board = ui.PhoneLayer.Q<ScrollView>("practice-jobs");
                    var button = board.Q<Button>("practice-play-" + key);
                    board.ScrollTo(button); await Frames(3, ct); Submit(button); await Frames(3, ct);
                    Need(!phone.IsOpen && app.IsRunning && app.ActiveGame != null, key + " did not open from actual Jobs card.");
                    Need(app.ActiveGame.name == (key == "earnings" ? "earnings-desk" : "thread-workshop"), "Wrong real game opened.");
                    CheckEnd(app.ActiveGame, ui);
                    Submit(app.ActiveGame.Q<Button>("practice-end")); await Frames(4, ct);
                    Need(phone.IsOpen && !app.IsRunning && app.ActiveGame == null && app.Result == null, key + " cancellation leaked UI.");
                }
                // External root detach settles inside panel dispatch; next view must mount on a later frame.
                Submit(ui.PhoneLayer.Q<Button>("practice-play-mod")); await Frames(3, ct);
                app.ActiveGame.RemoveFromHierarchy(); await Frames(4, ct);
                Need(phone.IsOpen && !app.IsRunning && app.ActiveGame == null, "External detach did not recover to Jobs.");
                // Owner teardown must clean immediately without depending on another UiRoot update.
                Submit(ui.PhoneLayer.Q<Button>("practice-play-mod")); await Frames(3, ct);
                app.Dispose();
                Need(ui.ModalLayer.Children().All(x => !x.ClassListContains("ct-desk-overlay")), "Owner disposal left a desk overlay.");
                await Frames(3, ct);
                Need(!phone.IsOpen && app.Result == null && app.ActiveGame == null, "Disposed owner reopened a view.");
                Need(string.IsNullOrEmpty(app.LastFailure), "Practice recorded an unexpected failure: " + app.LastFailure);
                Need(failures.Count == 0, "Practice flow logged errors: " + string.Join(" | ", failures));
                Log.Info("practice-jobs-review", "PASS actual Phone→Jobs tile; exactly three cards; all three real games started; no answer automation; real 45s zero-score completion and replay; duplicate gate; Again; result Return; End; external detach; owner cleanup; no error/exception logs. No connected shift or payout acceptance claimed.");
            }
            finally { Application.logMessageReceived -= OnLog; app.Dispose(); phone.Close(); }
        }
        private static void CheckBoard(UiRoot ui)
        {
            var board = ui.PhoneLayer.Q<ScrollView>("practice-jobs");
            Need(board != null && board.Query<Button>().ToList().Count(b => b.name.StartsWith("practice-play-", StringComparison.Ordinal)) == 3,
                "Expected three playable practice cards.");
            Need(board.Query<Label>().ToList().Any(l => l.text == PracticeJobsStrings.T("practice")), "Practice/no-payout label missing.");
        }
        private static void CheckEnd(VisualElement game, UiRoot ui)
        {
            var button = game.Q<Button>("practice-end");
            Need(button != null && button.enabledInHierarchy && button.worldBound.width > 0 && button.worldBound.height > 0,
                "End practice button is missing or has no native layout.");
            var bounds = ui.ModalLayer.worldBound;
            Need(bounds.Contains(button.worldBound.min) && bounds.Contains(button.worldBound.max), "End practice is clipped outside safe modal bounds.");
        }
        private static void Submit(Button button)
        {
            Need(button != null, "Missing real callback button.");
            using var e = NavigationSubmitEvent.GetPooled(); e.target = button; button.SendEvent(e);
        }
        private static async Task Frames(int count, CancellationToken ct) { for (int i = 0; i < count; i++) await Awaitable.NextFrameAsync(ct); }
        private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
#endif
