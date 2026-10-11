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
    /// <summary>Native rendering and UI-event review of local practice. Never starts or settles a server shift.</summary>
    public static class ModDeskPresentationReview
    {
        private const int Seed = 20261006;
        private const int Duration = 45;
        private const int ExpectedScore = 8;

        [Serializable] private sealed class Control
        {
            public string stage, text;
            public Rect bounds, contentBounds;
            public Vector2 measuredText;
        }

        [Serializable] private sealed class Receipt
        {
            public string evidence = "Native practice rendering + scripted NavigationSubmit events; not physical touch or server settlement acceptance.";
            public string mode = "PRACTICE_NO_PAYOUT";
            public int seed = Seed, durationSeconds = Duration, screenWidth, screenHeight, localScore;
            public double elapsedSeconds;
            public bool pass;
            public List<string> captures = new List<string>();
            public List<string> actions = new List<string>();
            public List<Control> controls = new List<Control>();
            public string failure;
        }

        public static async Task Run(string directory, CancellationToken ownerCancellation)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A review capture directory is required.");
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Path.IsPathRooted(directory)) directory = Path.Combine(Application.persistentDataPath, directory);
#endif
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            var receipt = new Receipt { screenWidth = Screen.width, screenHeight = Screen.height };
            var ui = UiRoot.Instance;
            if (ui == null) throw new InvalidOperationException("Practice review requires UiRoot.");
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ownerCancellation);
            cancellation.CancelAfter(TimeSpan.FromSeconds(70));
            var token = cancellation.Token;
            VisualElement desk = null, result = null;
            bool success = false;
            try
            {
                Log.Info("mod-desk-review", ModDeskChecks.Run());
                Log.Info("mod-desk-review", await ShiftReliabilityChecks.Run());
                var definition = new MiniGameDef { id = "review_practice_mod", careerId = "review_only", durationSec = Duration, maxScore = 10 };
                double started = Time.realtimeSinceStartupAsDouble;
                Task<int> play = ModDesk.PlayPracticeReview(definition, token, Seed);
                var expected = new ModDeskRound(0, Duration, 10, Seed);
                await Frames(3, token);
                desk = ui.ModalLayer.Q<VisualElement>(className: "ct-desk-overlay");
                Require(desk != null, "Practice overlay is missing.");
                Require(Text(desk, "ct-desk-context") == ShiftStrings.T("practice"), "Practice watermark is missing.");
                var buttons = desk.Query<Button>(className: "ct-desk-choice").ToList();
                Require(buttons.Count == 2, "Expected Ban and Allow buttons.");
                Button ban = buttons.Find(b => b.text == ShiftStrings.T("ban"));
                Button allow = buttons.Find(b => b.text == ShiftStrings.T("allow"));
                Button next = desk.Q<Button>(className: "ct-desk-next");
                Require(ban != null && allow != null && next != null, "A moderator decision control is missing.");
                var scroll = desk.Q<ScrollView>(className: "ct-desk-content");
                Require(scroll != null, "The readable message viewport is missing.");
                Require(!ban.worldBound.Overlaps(allow.worldBound), "Decision controls overlap.");
                CheckButton(ban, ui.ModalLayer.worldBound, "inbox", receipt);
                CheckButton(allow, ui.ModalLayer.worldBound, "inbox", receipt);
                CheckText(desk.Q<Label>(className: "ct-desk-message"), scroll.contentViewport.worldBound, "inbox-message", receipt);
                await Capture(directory, "mod-desk-inbox", receipt, token);

                for (int i = 0; i < ModDeskRound.CaseCount; i++)
                {
                    Require(!play.IsCompleted, "Practice ended before all decisions could be reviewed.");
                    int caseId = expected.CaseId;
                    Require(Text(desk, "ct-desk-case-number") == ShiftStrings.F("case", i + 1, ModDeskRound.CaseCount), "Case progress did not advance once.");
                    Require(Text(desk, "ct-desk-message") == ShiftStrings.T($"c{caseId}.message"), "Seeded practice order does not match the real view.");
                    bool correct = i != 2 && i != 7;
                    bool shouldBan = correct ? caseId % 2 == 0 : caseId % 2 != 0;
                    Button choice = shouldBan ? ban : allow;
                    Submit(choice);
                    // Deliberately dispatch twice, even though the first event hides this button.
                    // The model must reject the duplicate independently of pointer visibility.
                    Submit(choice, requireVisible: false);
                    Require(expected.Answer(shouldBan, i + 1) == correct, "Review oracle did not accept the decision.");
                    await Frames(2, token);
                    Require(Text(desk, "ct-desk-tally") == ShiftStrings.F("correctCount", expected.Correct), "Duplicate event scored or decision did not reach the real view.");
                    Require(Text(desk, "ct-desk-feedback-title") == ShiftStrings.T(correct ? "correct" : "incorrect"), "The real feedback did not match the decision.");
                    Require(!Visible(ban) && !Visible(allow) && Visible(next), "Feedback state exposes actionable answer controls.");
                    receipt.actions.Add($"case={caseId}; choice={(shouldBan ? "Ban" : "Allow")}; correct={correct}; duplicateIgnored=True");
                    if (i == 0 || i == 2)
                    {
                        scroll.ScrollTo(desk.Q<VisualElement>(className: "ct-desk-feedback"));
                        await Frames(3, token);
                        string stage = correct ? "feedback-correct" : "feedback-incorrect";
                        CheckButton(next, ui.ModalLayer.worldBound, stage, receipt);
                        CheckText(desk.Q<Label>(className: "ct-desk-feedback-body"), scroll.contentViewport.worldBound, stage, receipt);
                        await Capture(directory, "mod-desk-" + stage, receipt, token);
                    }
                    await WaitUntil(Time.realtimeSinceStartupAsDouble + 0.3, token);
                    Submit(next);
                    Require(expected.Advance(i + 1), "Review oracle did not advance.");
                    await Frames(2, token);
                    scroll.scrollOffset = Vector2.zero;
                    await Frames(2, token);
                }

                Require(Text(desk, "ct-desk-case-number") == ShiftStrings.T("clear"), "Inbox did not clear after ten decisions.");
                Require(!Visible(ban) && !Visible(allow) && !Visible(next), "A cleared inbox exposes another decision.");
                Require(!play.IsCompleted, "Clearing the inbox incorrectly shortened the 45-second round.");
                await Capture(directory, "mod-desk-inbox-cleared", receipt, token);
                await WaitUntil(started + 42, token);
                Require(!play.IsCompleted, "The real round completed before its deadline.");
                Require(Text(desk, "ct-desk-timer") != ShiftStrings.F("timer", Duration), "The real countdown never advanced.");
                await Capture(directory, "mod-desk-final-seconds", receipt, token);
                while (!play.IsCompleted) await Awaitable.NextFrameAsync(token);
                receipt.localScore = await play;
                receipt.elapsedSeconds = Time.realtimeSinceStartupAsDouble - started;
                Require(receipt.localScore == ExpectedScore, "Actual local result must be 8/10 after the scripted two mistakes.");
                Require(receipt.elapsedSeconds >= Duration && receipt.elapsedSeconds < 65, "Practice did not finish at its bounded real-time deadline.");
                Require(desk.parent == null, "Finished practice left its blocking overlay attached.");
                result = PracticeResult(ui, receipt.localScore, ownerCancellation);
                await Frames(3, token);
                var resultButtons = result.Query<Button>().ToList();
                Require(resultButtons.Count == 2, "Practice result needs Play again and Return to city.");
                Require(!resultButtons[0].worldBound.Overlaps(resultButtons[1].worldBound), "Practice result buttons overlap.");
                foreach (var button in resultButtons) CheckButton(button, ui.ModalLayer.worldBound, "practice-result", receipt);
                CheckText(result.Q<Label>(className: "ct-shift-copy"), result.Q<VisualElement>(className: "ct-desk-panel").worldBound, "practice-result", receipt);
                await Capture(directory, "mod-desk-practice-result", receipt, token);
                receipt.pass = true;
                success = true;
                Log.Info("mod-desk-review", $"PASS localScore={receipt.localScore}/10 elapsed={receipt.elapsedSeconds:0.00}s; ten UI decisions, duplicates ignored, no economy API calls; result remains open.");
            }
            catch (Exception exception)
            {
                receipt.failure = exception is OperationCanceledException ? "Review cancelled or exceeded its 70-second bound." : exception.Message;
                throw;
            }
            finally
            {
                cancellation.Cancel();
                if (!success)
                {
                    desk?.RemoveFromHierarchy();
                    result?.RemoveFromHierarchy();
                }
                File.WriteAllText(Path.Combine(directory, "mod-desk-review.json"), JsonUtility.ToJson(receipt, true));
            }
        }

        private static VisualElement PracticeResult(UiRoot ui, int? score, CancellationToken ownerCancellation)
        {
            Label Copy(string text, string classes)
            {
                var label = El.Text(text, classes);
                label.enableRichText = false;
                return label;
            }
            VisualElement overlay = null;
            Button replay = null, back = null;
            bool replaying = false;
            async void PlayAgain()
            {
                if (replaying || ownerCancellation.IsCancellationRequested) return;
                replaying = true;
                replay.SetEnabled(false);
                back.SetEnabled(false);
                overlay.RemoveFromHierarchy();
                try
                {
                    var definition = new MiniGameDef { id = "review_practice_mod", careerId = "review_only", durationSec = Duration, maxScore = 10 };
                    // No scripted decisions, seeded oracle, screenshot sequence, JobsApp or API path.
                    int localScore = await ModDesk.Play(definition, ownerCancellation, practice: true);
                    if (ownerCancellation.IsCancellationRequested) return;
                    PracticeResult(ui, localScore, ownerCancellation);
                    Log.Info("mod-desk-review", $"Human-controlled practice complete: localScore={localScore}/10; no automated decisions or payout.");
                }
                catch (OperationCanceledException) when (ownerCancellation.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    Log.Exception("mod-desk-review", exception);
                    if (!ownerCancellation.IsCancellationRequested) PracticeResult(ui, null, ownerCancellation);
                }
            }
            replay = El.Btn(ShiftStrings.T("playAgain"), PlayAgain, "ct-btn ct-btn-primary ct-desk-next");
            back = El.Btn(ShiftStrings.T("back"), () => overlay.RemoveFromHierarchy(), "ct-btn ct-desk-next");
            overlay = El.Div("ct-desk-overlay", El.Div("ct-desk-panel ct-shift-panel",
                Copy(ShiftStrings.T("practice"), "ct-desk-eyebrow"),
                Copy(ShiftStrings.T(score.HasValue ? "practiceDone" : "practiceInterrupted"), "ct-shift-heading"),
                Copy(score.HasValue ? ShiftStrings.F("practiceResult", score.Value, ModDeskRound.CaseCount)
                    : ShiftStrings.T("practiceInterruptedDetail"), "ct-shift-copy"), replay, back));
            var sheet = Resources.Load<StyleSheet>("UI/mod-desk");
            Require(sheet != null, "Practice stylesheet is missing.");
            overlay.styleSheets.Add(sheet);
            ui.ModalLayer.Add(overlay);
            return overlay;
        }

        private static void Submit(Button button, bool requireVisible = true)
        {
            Require(button != null && button.enabledInHierarchy, "Cannot submit a missing or disabled decision.");
            if (requireVisible) Require(Visible(button), "Cannot submit a hidden decision.");
            using var evt = NavigationSubmitEvent.GetPooled();
            evt.target = button;
            button.SendEvent(evt);
        }

        private static async Task Frames(int count, CancellationToken token)
        {
            for (int i = 0; i < count; i++) await Awaitable.NextFrameAsync(token);
        }

        private static async Task WaitUntil(double time, CancellationToken token)
        {
            while (Time.realtimeSinceStartupAsDouble < time) await Awaitable.NextFrameAsync(token);
        }

        private static async Task Capture(string directory, string name, Receipt receipt, CancellationToken token)
        {
            await Awaitable.EndOfFrameAsync(token);
            Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
            Require(texture != null, "Native screenshot capture failed.");
            try { File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG()); }
            finally { UnityEngine.Object.Destroy(texture); }
            receipt.captures.Add(name + ".png");
        }

        private static void CheckButton(Button button, Rect safe, string stage, Receipt receipt)
        {
            Require(Visible(button), stage + ": button is hidden.");
            Require(button.worldBound.width >= 123 && button.worldBound.height >= 123, stage + ": button is smaller than 124 reference units.");
            CheckText(button, safe, stage, receipt);
        }

        private static void CheckText(TextElement element, Rect visibleBounds, string stage, Receipt receipt)
        {
            Require(element != null && Visible(element), stage + ": text is missing or hidden.");
            var content = element.contentRect;
            Vector2 measured = element.MeasureTextSize(element.text, content.width, VisualElement.MeasureMode.AtMost, 0, VisualElement.MeasureMode.Undefined);
            receipt.controls.Add(new Control { stage = stage, text = element.text, bounds = element.worldBound, contentBounds = content, measuredText = measured });
            Require(Contains(visibleBounds, element.worldBound), stage + ": text/control lies outside its visible viewport.");
            Require(content.width >= measured.x - 2 && content.height >= measured.y - 2, stage + ": native label text is clipped.");
        }

        private static bool Visible(VisualElement element)
        {
            if (element == null || element.panel == null) return false;
            for (var e = element; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }

        private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 2 && inner.yMin >= outer.yMin - 2 && inner.xMax <= outer.xMax + 2 && inner.yMax <= outer.yMax + 2;
        private static string Text(VisualElement root, string className) => root.Q<Label>(className: className)?.text;
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Mod desk review: " + message); }
    }
}
#endif
