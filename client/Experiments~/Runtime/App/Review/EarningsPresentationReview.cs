#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.App.Review
{
    public static class EarningsPresentationReview
    {
        private const int Seed = 20261006, Duration = 60;
        [Serializable] private sealed class Receipt
        {
            public string evidence = "Native UI practice and NavigationSubmit callbacks. No backend requests, payout or physical touch acceptance.";
            public bool pass;
            public int localScore;
            public double elapsedSeconds;
            public string failure;
            public List<string> captures = new List<string>();
            public List<string> decisions = new List<string>();
        }
        public static async Task Run(string directory, CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Path.IsPathRooted(directory)) directory = Path.Combine(Application.persistentDataPath, directory);
#endif
            directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            var receipt = new Receipt();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner);
            timeout.CancelAfter(TimeSpan.FromSeconds(85));
            var ct = timeout.Token;
            var ui = UiRoot.Instance;
            VisualElement desk = null;
            try
            {
                var definition = new MiniGameDef { id = "review_earnings", careerId = "review_only", durationSec = Duration, maxScore = 100 };
                double start = Time.realtimeSinceStartupAsDouble;
                var play = EarningsDesk.PlayPracticeReview(definition, ct, Seed);
                var expected = new EarningsRound(0, Duration, 100, Seed);
                await Frames(4, ct);
                desk = ui.ModalLayer.Q<VisualElement>("earnings-desk");
                Need(desk != null, "Missing earnings desk");
                Need(desk.Q<Label>(className: "ct-desk-context").text == EarningsStrings.T("practice"), "Missing practice watermark");
                var evidence = desk.Q<ScrollView>(className: "ct-earn-evidence");
                var decision = desk.Q<ScrollView>(className: "ct-earn-decision");
                var next = desk.Q<Button>("earnings-next");
                for (int i = 0; i < EarningsRound.ReportCount; i++)
                {
                    var report = expected.Current;
                    Need(!play.IsCompleted, "Round finished early");
                    Need(desk.Q<Label>(className: "ct-earn-company").text == EarningsStrings.T(report.Kind + ".company"), "Wrong report order");
                    CheckText(desk.Q<Label>(className: "ct-earn-headline"), evidence.contentViewport.worldBound);
                    CheckText(desk.Q<Label>(className: "ct-earn-note"), evidence.contentViewport.worldBound);
                    CheckText(desk.Q<Label>(className: "ct-earn-question"), decision.contentViewport.worldBound);
                    for (int j = 0; j < 3; j++) CheckButton(desk.Q<Button>("earnings-answer-" + j), decision.contentViewport.worldBound);
                    await Capture(directory, "report-" + report.Kind, receipt, ct);
                    int chosen = i == 2 ? (report.CorrectOption + 1) % 3 : report.CorrectOption;
                    var answer = desk.Q<Button>("earnings-answer-" + chosen);
                    Submit(answer); Submit(answer);
                    bool right = i != 2;
                    Need(expected.Answer(chosen, i + 1) == right, "Bad review oracle");
                    await Frames(3, ct);
                    Need(desk.Q<Label>(className: "ct-desk-tally").text == EarningsStrings.F("correctCount", expected.Correct, EarningsRound.ReportCount), "Duplicate scored or wrong tally");
                    Need(desk.Q<Label>(className: "ct-desk-feedback-title").text == EarningsStrings.T(right ? "correct" : "incorrect"), "Wrong feedback");
                    CheckText(desk.Q<Label>(className: "ct-desk-feedback-body"), decision.contentViewport.worldBound);
                    CheckButton(next, decision.contentViewport.worldBound);
                    receipt.decisions.Add($"kind={report.Kind}; selected={chosen}; correct={right}; duplicateIgnored=True");
                    if (i == 0 || i == 2) await Capture(directory, right ? "feedback-correct" : "feedback-incorrect", receipt, ct);
                    Submit(next); Need(expected.Advance(i + 1), "Could not advance oracle");
                    await Frames(3, ct);
                }
                Need(!play.IsCompleted, "Reports cleared before the full shift duration");
                await Capture(directory, "reports-filed", receipt, ct);
                while (!play.IsCompleted) await Awaitable.NextFrameAsync(ct);
                receipt.localScore = await play;
                receipt.elapsedSeconds = Time.realtimeSinceStartupAsDouble - start;
                Need(receipt.localScore == 83 && receipt.elapsedSeconds >= Duration && receipt.elapsedSeconds < 80, "Wrong score or duration");
                Need(desk.parent == null, "Completed desk did not detach");
                PracticeResult(receipt.localScore, owner);
                await Frames(3, ct); await Capture(directory, "practice-result", receipt, ct);
                receipt.pass = true;
                Log.Info("earnings-review", $"PASS score={receipt.localScore}/100 elapsed={receipt.elapsedSeconds:0.00}s; six native report decisions, duplicates ignored, no economy requests.");
            }
            catch (Exception e) { receipt.failure = e.Message; desk?.RemoveFromHierarchy(); throw; }
            finally { File.WriteAllText(Path.Combine(directory, "earnings-review.json"), JsonUtility.ToJson(receipt, true)); }
        }
        private static VisualElement PracticeResult(int? score, CancellationToken owner, bool interrupted = false,
            Func<MiniGameDef, CancellationToken, Task<int>> replay = null)
        {
            var root = El.Div("ct-desk-overlay");
            root.name = "earnings-practice-result";
            bool playing = false;
            async void Again()
            {
                if (playing || owner.IsCancellationRequested) return;
                playing = true;
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(owner);
                root.RemoveFromHierarchy();
                try
                {
                    var def = new MiniGameDef { id = "review_earnings", careerId = "review_only", durationSec = Duration, maxScore = 100 };
                    int result = await (replay != null ? replay(def, attempt.Token) : EarningsDesk.Play(def, attempt.Token, true));
                    if (!owner.IsCancellationRequested) PracticeResult(result, owner);
                }
                catch (OperationCanceledException) { }
                catch (Exception error)
                {
                    attempt.Cancel(); // An already-mounted practice desk observes this and removes its updater/overlay.
                    Log.Warn("earnings-practice", "Replay interrupted: " + error.GetType().Name);
                    if (!owner.IsCancellationRequested && UiRoot.Instance != null)
                        PracticeResult(null, owner, interrupted: true);
                }
                finally { attempt.Cancel(); playing = false; }
            }
            var again = El.Btn(EarningsStrings.T("again"), Again, "ct-btn ct-btn-primary ct-desk-next");
            again.name = "earnings-practice-again";
            var back = El.Btn(EarningsStrings.T("back"), () => root.RemoveFromHierarchy(), "ct-btn ct-desk-next");
            back.name = "earnings-practice-back";
            root.Add(El.Div("ct-desk-panel ct-shift-panel",
                Plain(EarningsStrings.T("practice"), "ct-desk-eyebrow"),
                Plain(EarningsStrings.T(interrupted ? "interrupted" : "done"), "ct-shift-heading"),
                Plain(interrupted ? EarningsStrings.T("interruptedDetail") : EarningsStrings.F("result", score.GetValueOrDefault(), 100), "ct-shift-copy"),
                again, back));
            var sheet = Resources.Load<StyleSheet>("UI/mod-desk");
            if (sheet != null) root.styleSheets.Add(sheet);
            UiRoot.Instance.ModalLayer.Add(root);
            return root;
        }

        /// <summary>Guarded native fault injection: starts a real practice desk, fails its caller, verifies cleanup and honest recovery.</summary>
        public static async Task<string> CheckPracticeReplayFailure(CancellationToken owner)
        {
            var ui = UiRoot.Instance;
            Need(ui != null && ui.ModalLayer.Q<VisualElement>("earnings-desk") == null, "Failure review needs an idle earnings UI");
            Task<int> abandonedPractice = null;
            var previous = PracticeResult(83, owner, replay: (definition, cancellation) =>
            {
                abandonedPractice = EarningsDesk.Play(definition, cancellation, practice: true);
                return Task.FromException<int>(new InvalidOperationException("Explicit native review fault"));
            });
            VisualElement recovery = null;
            try
            {
                await Frames(3, owner);
                Submit(previous.Q<Button>("earnings-practice-again"));
                await Frames(4, owner);
                Need(previous.parent == null, "Previous receipt stayed attached");
                Need(abandonedPractice != null && abandonedPractice.IsCanceled, "Failed replay left its real desk running or returned a score");
                try { await abandonedPractice; } catch (OperationCanceledException) { }
                Need(ui.ModalLayer.Q<VisualElement>("earnings-desk") == null, "Failed replay left its blocking desk attached");
                recovery = ui.ModalLayer.Q<VisualElement>("earnings-practice-result");
                Need(recovery != null && recovery != previous, "Failure did not show recovery");
                Need(recovery.Q<Label>(className: "ct-shift-heading").text == EarningsStrings.T("interrupted"), "Failure displayed a success heading");
                Need(recovery.Q<Label>(className: "ct-shift-copy").text == EarningsStrings.T("interruptedDetail"), "Failure copy did not state no payout");
                CheckButton(recovery.Q<Button>("earnings-practice-again"), ui.ModalLayer.worldBound);
                CheckButton(recovery.Q<Button>("earnings-practice-back"), ui.ModalLayer.worldBound);
                CheckText(recovery.Q<Label>(className: "ct-shift-copy"), ui.ModalLayer.worldBound);
                Submit(recovery.Q<Button>("earnings-practice-back"));
                Need(recovery.parent == null, "Recovery Return to city failed to dismiss");
                return "PASS earnings failed-replay recovery: real desk cancelled, no score/payout, honest recovery and both native buttons, return-to-city callback.";
            }
            finally { previous.RemoveFromHierarchy(); recovery?.RemoveFromHierarchy(); }
        }
        private static Label Plain(string text, string classes) { var label = El.Text(text, classes); label.enableRichText = false; return label; }
        private static async Task Frames(int count, CancellationToken ct) { for (int i = 0; i < count; i++) await Awaitable.NextFrameAsync(ct); }
        private static void Submit(Button button)
        {
            Need(button != null && button.enabledInHierarchy, "Cannot submit absent/disabled button");
            using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
        }
        private static void CheckButton(Button button, Rect viewport)
        {
            Need(button.worldBound.width >= 123 && button.worldBound.height >= 123, "Touch target below124 units");
            CheckText(button, viewport);
        }
        private static void CheckText(TextElement text, Rect viewport)
        {
            Need(text != null && text.panel != null, "Missing text");
            var b = text.worldBound;
            Need(b.xMin >= viewport.xMin - 2 && b.yMin >= viewport.yMin - 2 && b.xMax <= viewport.xMax + 2 && b.yMax <= viewport.yMax + 2, "Clipped viewport: " + text.text);
            var size = text.MeasureTextSize(text.text, text.contentRect.width, VisualElement.MeasureMode.AtMost, 0, VisualElement.MeasureMode.Undefined);
            Need(size.y <= text.contentRect.height + 2 && size.x <= text.contentRect.width + 2, "Clipped glyphs: " + text.text);
        }
        private static async Task Capture(string directory, string name, Receipt receipt, CancellationToken ct)
        {
            await Awaitable.EndOfFrameAsync(ct);
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            Need(texture != null, "Capture failed");
            try { File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG()); }
            finally { UnityEngine.Object.Destroy(texture); }
            receipt.captures.Add(name);
        }
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException("Earnings review: " + message); }
    }
}
#endif
