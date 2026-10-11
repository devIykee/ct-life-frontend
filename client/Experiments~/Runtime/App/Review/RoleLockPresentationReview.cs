#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.App.Review
{
    public static class RoleLockPresentationReview
    {
        private const int Seed = 20261007;
        [Serializable] private sealed class Receipt
        {
            public bool pass;
            public int score, locked, correct, extra, missing;
            public double wallSeconds, pausedSeconds;
            public float sealTravelPanelUnits;
            public string failure;
            public List<string> checks = new List<string>(), captures = new List<string>();
            public string limits = "Native local practice layout/callback review. Main scripted run disables OS focus observation and separately invokes real focus/pause handlers. Programmatic NavigationSubmit invokes actual buttons; physical Android touches/background delivery, fun/retention and paid routing remain unverified. No API, mic, real admin action or payout.";
        }
        public static async Task Run(PhoneShell phone, PracticeJobsApp practice, string directory, CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Path.IsPathRooted(directory)) directory = Path.Combine(Application.persistentDataPath, directory);
#endif
            directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner); timeout.CancelAfter(TimeSpan.FromSeconds(80));
            var ct = timeout.Token; var receipt = new Receipt(); Task<RoleLockResult> play = null;
            try
            {
                Need(practice != null, "Practice hub missing");
                int starts = practice.RoundsStarted;
                phone.Open("jobs"); await Frames(4, ct);
                var page = UiRoot.Instance.PhoneLayer.Q<ScrollView>("practice-jobs");
                var entry = page?.Q<Button>("practice-play-role"); Need(entry != null, "Fifth practice card missing");
                page.ScrollTo(entry); await Frames(4, ct);
                Within(entry.worldBound, page.contentViewport.worldBound, "Practice entry outside its actual viewport");
                await Capture(directory, "practice-entry", receipt, ct);
                await Activate(page, "practice-play-role", ct);
                Need(practice.IsRunning && practice.RoundsStarted == starts + 1, "Actual fifth-card callback did not enter shared flight");
                var live = UiRoot.Instance.ModalLayer.Q("role-lock-game"); Need(live != null, "Practice callback did not mount actual game");
                var pausing = live.Q("role-paused");
                await Activate(live, pausing.resolvedStyle.display != DisplayStyle.None ? "role-paused-end" : "role-end", ct);
                await Until(() => !practice.IsRunning, 3, ct);
                Need(UiRoot.Instance.PhoneLayer.Q("practice-jobs") != null && practice.Result == null, "Cancelled hub attempt produced a score or failed to reopen Jobs");
                phone.Close(); await Frames(3, ct);
                receipt.checks.Add("PASS actual fifth Jobs card, shared single-flight ownership, unscripted game entry and End practice; cancellation reopens Jobs with no result.");

                using (var pre = new CancellationTokenSource())
                { pre.Cancel(); bool denied = false; try { _ = RoleLock.PlayReview(pre.Token, Seed); } catch (OperationCanceledException) { denied = true; } Need(denied, "Pre-cancelled play mounted"); }
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var operation = RoleLock.PlayReview(attempt.Token, Seed); await Frames(3, ct);
                    UiRoot.Instance.ModalLayer.Q("role-lock-game").RemoveFromHierarchy(); await Cancelled(operation);
                }
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var operation = RoleLock.PlayReview(attempt.Token, Seed); await Frames(3, ct);
                    attempt.Cancel(); await Frames(3, ct); await Cancelled(operation);
                }
                receipt.checks.Add("PASS pre-cancel, attached cancellation and root detach return no score; subsequent attempts mount.");

                double started = Time.realtimeSinceStartupAsDouble;
                play = RoleLock.PlayReview(ct, Seed); bool concurrent = false;
                try { _ = RoleLock.PlayReview(ct, Seed); } catch (InvalidOperationException) { concurrent = true; }
                Need(concurrent, "Concurrent round accepted"); await Frames(4, ct);
                var root = UiRoot.Instance.ModalLayer.Q("role-lock-game"); Inspect(root);
                var child = new VisualElement(); root.Add(child); child.RemoveFromHierarchy();
                Need(!play.IsCompleted, "Child detach cancelled whole game");
                double wallPause = Time.realtimeSinceStartupAsDouble, elapsedPause = RoleLock.ReviewElapsed;
                await Activate(root, "role-pause", ct); double pausedAt = RoleLock.ReviewElapsed;
                await Wait(.35, ct); Need(RoleLock.ReviewElapsed == pausedAt && !root.Q<Button>("role-cell-0-0").enabledInHierarchy, "Paused clock or inputs remained active");
                Inspect(root); await Capture(directory, "paused", receipt, ct); await Activate(root, "role-resume", ct);
                RoleLock.ReviewFocus(false); pausedAt = RoleLock.ReviewElapsed; await Wait(.25, ct);
                RoleLock.ReviewFocus(true); Need(RoleLock.ReviewElapsed == pausedAt && Visible(root.Q("role-paused")), "Focus return resumed without gesture");
                await Activate(root, "role-resume", ct); RoleLock.ReviewPause(true); pausedAt = RoleLock.ReviewElapsed; await Wait(.25, ct);
                RoleLock.ReviewPause(false); Need(RoleLock.ReviewElapsed == pausedAt && Visible(root.Q("role-paused")), "App resume silently advanced practice");
                await Activate(root, "role-resume", ct);
                receipt.pausedSeconds = Time.realtimeSinceStartupAsDouble - wallPause - (RoleLock.ReviewElapsed - elapsedPause);
                receipt.checks.Add("PASS real Pause/Resume callbacks, focus and app-pause handlers, frozen time/input and explicit Resume.");

                var deck = new RoleLockRound(0, Seed); var seen = new HashSet<int>();
                for (int board = 0; board < RoleLockRound.BoardCount; board++)
                {
                    var current = deck.Current; Need(seen.Add(current.Story), "Repeated authored board");
                    Need(root.Q<Label>(className: "ct-role-board-title").text == RoleLockStrings.Board(current, "title"), "Native board differs from deterministic sequence");
                    Inspect(root); await Capture(directory, "board-" + board + "-open", receipt, ct);
                    if (board == 0)
                    {
                        var firstSwitch = root.Q<Button>("role-cell-0-0");
                        float offThumb = firstSwitch.Q(className: "ct-role-switch-thumb").worldBound.center.x;
                        var offLight = firstSwitch.Q(className: "ct-role-indicator").resolvedStyle.backgroundColor;
                        await Activate(root, "role-cell-0-0", ct); Need(State(root,0,0) == RoleLockStrings.T("on"), "Toggle on failed");
                        await Wait(.18, ct);
                        Need(firstSwitch.Q(className: "ct-role-switch-thumb").worldBound.center.x > offThumb + 60, "Selected switch did not visibly travel");
                        Need(firstSwitch.Q(className: "ct-role-indicator").resolvedStyle.backgroundColor != offLight, "Grant indicator did not respond to actual toggle");
                        Inspect(root); await Capture(directory, "switch-granted", receipt, ct);
                        await Activate(root, "role-cell-0-0", ct); Need(State(root,0,0) == RoleLockStrings.T("off"), "Toggle off failed");
                        await Activate(root, "role-cell-1-1", ct); await Activate(root, "role-clear", ct);
                        Need(root.Query<Label>(className: "ct-role-cell-state").ToList().All(label => label.text == RoleLockStrings.T("off")), "Clear did not reset grid");
                    }
                    for (int row = 0; row < 3; row++) for (int column = 0; column < 3; column++)
                    {
                        bool grant = current.Requires(row, column);
                        if (board == 2 && row == 0 && column == 0) grant = !grant; // One intentional extra or missing permission.
                        if (grant) await Activate(root, "role-cell-" + row + "-" + column, ct);
                    }
                    while (RoleLock.ReviewElapsed < (board + 1) * 6) await Awaitable.NextFrameAsync(ct);
                    var openSeal = root.Q("role-seal-shackle").worldBound.center;
                    double submittedAt = Time.realtimeSinceStartupAsDouble;
                    await Activate(root, "role-lock", ct);
                    if (board == 4)
                    {
                        Need(play.IsCompleted && Time.realtimeSinceStartupAsDouble - submittedAt < .5, "Fifth Lock did not finish immediately");
                        break;
                    }
                    Need(!root.Q<Button>("role-cell-0-0").enabledInHierarchy && !Visible(root.Q("role-lock")), "Locked board remained editable");
                    Need(root.Q(className: "ct-role-feedback-area").ClassListContains("ct-role-sealed"), "Locked panel has no seal feedback");
                    if (board == 0)
                    {
                        await Capture(directory, "seal-closing", receipt, ct); await Wait(.18, ct);
                        receipt.sealTravelPanelUnits = Vector2.Distance(openSeal, root.Q("role-seal-shackle").worldBound.center);
                        Need(receipt.sealTravelPanelUnits > 4, "Lock seal did not move after actual submission");
                        await Capture(directory, "seal-closed", receipt, ct);
                        receipt.checks.Add("PASS actual mechanical thumb travel, grant indicator change and moving lock seal; existing touch/text/viewport gates retained.");
                    }
                    if (board == 2)
                        Need(State(root,0,0) == RoleLockStrings.T(current.Requires(0,0) ? "missing" : "extra"), "Wrong cell lacks precise textual feedback");
                    Inspect(root); await Capture(directory, "board-" + board + "-locked", receipt, ct);
                    if (board < 4) { await Activate(root, "role-next", ct); deck.Lock(board,0); deck.Next(board,0); }
                }
                Need(seen.Count == 5 && play.IsCompleted, "Fifth board failed to complete");
                receipt.checks.Add("PASS all five actual authored boards; real toggle/reverse/Clear/Lock/Next callbacks; one marked error, no editing after lock, strict native bounds.");
                while (!play.IsCompleted) { ct.ThrowIfCancellationRequested(); await Awaitable.NextFrameAsync(ct); }
                var result = await play; receipt.wallSeconds = Time.realtimeSinceStartupAsDouble - started;
                receipt.score = result.Score; receipt.locked = result.LockedBoards; receipt.correct = result.CorrectBoards; receipt.extra = result.ExtraGrants; receipt.missing = result.MissingGrants;
                Need(result.Score == 80 && result.LockedBoards == 5 && result.CorrectBoards == 4 && result.ExtraGrants + result.MissingGrants == 1, "Wrong finite score or discrepancy count");
                Need(receipt.wallSeconds - receipt.pausedSeconds >= 30 && receipt.wallSeconds - receipt.pausedSeconds < 40 && root.parent == null, "Wrong active duration or retained overlay");
                var resultRoot = Result(result, owner); await Frames(4, ct); Inspect(resultRoot); await Capture(directory, "result", receipt, ct);
                await Activate(resultRoot, "role-again", ct); await Frames(3, ct);
                var replay = UiRoot.Instance.ModalLayer.Q("role-lock-game"); Need(replay != null, "Play again did not start human-controlled replay");
                replay.RemoveFromHierarchy(); await Frames(4, ct);
                Need(UiRoot.Instance.ModalLayer.Q("role-result") != null, "Cancelled replay lost completed result");
                receipt.checks.Add("PASS immediate fifth Lock at about30 active seconds (50second ceiling),80/100 for one intentional wrong board, immediate unscripted replay and cancellation without a new score.");
                receipt.pass = true; Log.Info("role-lock-review", "PASS local permissions puzzle; no admin API/payout.");
            }
            catch (Exception error)
            {
                receipt.failure = error.Message; try { await Capture(directory, "failure", receipt, CancellationToken.None); } catch { }
                UiRoot.Instance?.ModalLayer.Q("role-lock-game")?.RemoveFromHierarchy();
                if (play != null) { try { await play; } catch (OperationCanceledException) { } }
                throw;
            }
            finally { File.WriteAllText(Path.Combine(directory, "role-lock-review.json"), JsonUtility.ToJson(receipt, true)); }
        }
        private static string State(VisualElement root,int row,int col) => root.Q<Button>("role-cell-"+row+"-"+col).Q<Label>(className:"ct-role-cell-state").text;
        private static VisualElement Result(RoleLockResult result, CancellationToken owner)
        {
            var root = El.Div("ct-desk-overlay"); root.name = "role-result"; bool busy = false;
            async void Again()
            {
                if (busy || owner.IsCancellationRequested || root.parent == null) return;
                busy = true; root.RemoveFromHierarchy(); using var attempt = CancellationTokenSource.CreateLinkedTokenSource(owner);
                try { var next = await RoleLock.Play(attempt.Token); if (!owner.IsCancellationRequested) Result(next, owner); }
                catch (OperationCanceledException) { if (!owner.IsCancellationRequested && UiRoot.Instance != null) Result(result, owner); }
                catch (Exception error) { Log.Warn("role-practice", "Replay interrupted: " + error.GetType().Name); if (!owner.IsCancellationRequested && UiRoot.Instance != null) Result(result, owner); }
                finally { attempt.Cancel(); busy = false; }
            }
            var again = El.Btn(RoleLockStrings.T("again"), Again, "ct-btn ct-desk-next"); again.name = "role-again";
            var back = El.Btn(RoleLockStrings.T("back"), () => root.RemoveFromHierarchy(), "ct-btn ct-desk-next"); back.name = "role-back";
            root.Add(El.Div("ct-desk-panel ct-shift-panel", ModDeskCopy(RoleLockStrings.T("practice"), "ct-desk-eyebrow"),
                ModDeskCopy(RoleLockStrings.T("complete"), "ct-desk-title"), ModDeskCopy(RoleLockStrings.F("score", result.Score), "ct-shift-pay"),
                ModDeskCopy(RoleLockStrings.Summary(result), "ct-shift-copy"), again, back));
            var sheet = Resources.Load<StyleSheet>("UI/mod-desk"); if (sheet != null) root.styleSheets.Add(sheet);
            UiRoot.Instance.ModalLayer.Add(root); return root;
        }
        private static Label ModDeskCopy(string text,string classes) { var label=El.Text(text,classes);label.enableRichText=false;return label; }
        private static async Task Activate(VisualElement root, string name, CancellationToken ct)
        {
            var button = root.Q<Button>(name); Need(button != null && Visible(button) && button.enabledInHierarchy, "Unavailable control " + name);
            Touch(button); using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); } await Frames(2, ct);
        }
        private static void Touch(Button button)
        {
            float scale = Screen.height / UiRoot.Instance.Root.worldBound.height;
            Need(button.resolvedStyle.minHeight.value >= 124 && button.worldBound.height * scale + .01f >= Mathf.Floor(124 * scale) && button.worldBound.width * scale + .01f >= Mathf.Floor(124 * scale),
                "Undersized control " + button.name + " bounds=" + button.worldBound + " scale=" + scale);
        }
        private static void Inspect(VisualElement root)
        {
            Need(root != null && root.panel != null, "View missing");
            var grid = root.Q("role-grid"); var feedback = root.Q<VisualElement>(className:"ct-role-feedback-area");
            if (grid != null && feedback != null) foreach (var row in grid.Children())
                Need(row.worldBound.yMax <= feedback.worldBound.yMin + 2, "Grid extends into feedback");
            foreach (var button in root.Query<Button>().ToList()) if (Visible(button)) { Touch(button); Within(button.worldBound,root.worldBound,"Control outside viewport: "+button.name); }
            foreach (var rail in root.Query<VisualElement>(className:"ct-role-switch").ToList())
            {
                Within(rail.worldBound,rail.parent.LocalToWorld(rail.parent.contentRect),"Switch rail outside its actual hit region");
                Within(rail.Q(className:"ct-role-switch-thumb").worldBound,rail.worldBound,"Thumb outside rail");
            }
            foreach (var label in root.Query<Label>().ToList())
            {
                if (!Visible(label) || string.IsNullOrEmpty(label.text)) continue;
                var measured = label.MeasureTextSize(label.text,label.contentRect.width,VisualElement.MeasureMode.AtMost,0,VisualElement.MeasureMode.Undefined);
                Need(measured.x <= label.contentRect.width + 2 && measured.y <= label.contentRect.height + 2,
                    "Clipped text '"+label.text+"' content="+label.contentRect+" measured="+measured);
                Within(label.worldBound,root.worldBound,"Text outside viewport: "+label.text);
                if(label.ClassListContains("ct-role-cell-state"))Within(label.worldBound,label.parent.LocalToWorld(label.parent.contentRect),"Cell text outside button");
            }
        }
        private static void Within(Rect child,Rect parent,string why) => Need(child.xMin>=parent.xMin-2&&child.xMax<=parent.xMax+2&&child.yMin>=parent.yMin-2&&child.yMax<=parent.yMax+2,why+" child="+child+" parent="+parent);
        private static bool Visible(VisualElement element) { for(var p=element;p!=null;p=p.parent)if(p.resolvedStyle.display==DisplayStyle.None||p.resolvedStyle.visibility==Visibility.Hidden)return false;return element?.panel!=null; }
        private static async Task Cancelled(Task task) { try { await task; throw new InvalidOperationException("Cancelled practice returned a score"); } catch(OperationCanceledException){} }
        private static async Task Frames(int count,CancellationToken ct) { for(int i=0;i<count;i++)await Awaitable.NextFrameAsync(ct); }
        private static async Task Wait(double seconds,CancellationToken ct) { double until=Time.realtimeSinceStartupAsDouble+seconds;while(Time.realtimeSinceStartupAsDouble<until)await Awaitable.NextFrameAsync(ct); }
        private static async Task Until(Func<bool> done,double seconds,CancellationToken ct) { double until=Time.realtimeSinceStartupAsDouble+seconds;while(!done()){Need(Time.realtimeSinceStartupAsDouble<until,"Lifecycle predicate timed out");await Awaitable.NextFrameAsync(ct);} }
        private static async Task Capture(string directory,string name,Receipt receipt,CancellationToken ct)
        {await Awaitable.EndOfFrameAsync(ct);var image=ScreenCapture.CaptureScreenshotAsTexture();Need(image!=null,"Capture failed");try{File.WriteAllBytes(Path.Combine(directory,name+".png"),image.EncodeToPNG());}finally{UnityEngine.Object.Destroy(image);}receipt.captures.Add(name+".png");}
        private static void Need(bool condition,string message) { if(!condition)throw new InvalidOperationException("Role Lock review: "+message); }
    }
}
#endif
