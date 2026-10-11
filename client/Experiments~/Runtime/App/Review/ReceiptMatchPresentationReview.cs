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
    public static class ReceiptMatchPresentationReview
    {
        private const int Seed = 20261007;
        [Serializable] private sealed class Receipt
        {
            public bool pass;
            public int score, filed, correct, wrong, unpaired;
            public double wallSeconds, pausedSeconds;
            public string failure;
            public List<string> checks = new List<string>(), captures = new List<string>();
            public string limits = "Native local fictional reconciliation layout/callback review. Main scripted run disables OS focus observation and separately invokes real focus/pause handlers. Programmatic NavigationSubmit invokes actual buttons; physical Android touches/background delivery, fun/retention and paid routing remain unverified. No API, mic, real account action or payout.";
        }
        public static async Task Run(PhoneShell phone, PracticeJobsApp practice, string directory, CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Path.IsPathRooted(directory)) directory = Path.Combine(Application.persistentDataPath, directory);
#endif
            directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner); timeout.CancelAfter(TimeSpan.FromSeconds(80));
            var ct = timeout.Token; var receipt = new Receipt(); Task<ReceiptMatchResult> play = null;
            try
            {
                Need(practice != null, "Practice hub missing");
                int starts = practice.RoundsStarted;
                phone.Open("jobs"); await Frames(4, ct);
                var page = UiRoot.Instance.PhoneLayer.Q<ScrollView>("practice-jobs");
                var entry = page?.Q<Button>("practice-play-receipt"); Need(entry != null, "Sixth practice card missing");
                page.ScrollTo(entry); await Frames(4, ct);
                Within(entry.worldBound, page.contentViewport.worldBound, "Practice entry outside its actual viewport");
                await Capture(directory, "practice-entry", receipt, ct);
                await Activate(page, "practice-play-receipt", ct);
                Need(practice.IsRunning && practice.RoundsStarted == starts + 1, "Actual sixth-card callback did not enter shared flight");
                var live = UiRoot.Instance.ModalLayer.Q("receipt-match-game"); Need(live != null, "Practice callback did not mount actual game");
                var pausing = live.Q("receipt-paused");
                await Activate(live, pausing.resolvedStyle.display != DisplayStyle.None ? "receipt-paused-end" : "receipt-end", ct);
                await Until(() => !practice.IsRunning, 3, ct);
                Need(UiRoot.Instance.PhoneLayer.Q("practice-jobs") != null && practice.Result == null, "Cancelled hub attempt produced a score or failed to reopen Jobs");
                phone.Close(); await Frames(3, ct);
                receipt.checks.Add("PASS actual sixth Jobs card, shared single-flight ownership, unscripted game entry and End practice; cancellation reopens Jobs with no result.");

                using (var pre = new CancellationTokenSource())
                { pre.Cancel(); bool denied = false; try { _ = ReceiptMatch.PlayReview(pre.Token, Seed); } catch (OperationCanceledException) { denied = true; } Need(denied, "Pre-cancelled play mounted"); }
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var operation = ReceiptMatch.PlayReview(attempt.Token, Seed); await Frames(3, ct);
                    UiRoot.Instance.ModalLayer.Q("receipt-match-game").RemoveFromHierarchy(); await Cancelled(operation);
                }
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var operation = ReceiptMatch.PlayReview(attempt.Token, Seed); await Frames(3, ct);
                    attempt.Cancel(); await Frames(3, ct); await Cancelled(operation);
                }
                receipt.checks.Add("PASS pre-cancel, attached cancellation and root detach return no score; subsequent attempts mount.");

                double started = Time.realtimeSinceStartupAsDouble;
                play = ReceiptMatch.PlayReview(ct, Seed); bool concurrent = false;
                try { _ = ReceiptMatch.PlayReview(ct, Seed); } catch (InvalidOperationException) { concurrent = true; }
                Need(concurrent, "Concurrent round accepted"); await Frames(4, ct);
                var root = UiRoot.Instance.ModalLayer.Q("receipt-match-game"); Inspect(root);
                var child = new VisualElement(); root.Add(child); child.RemoveFromHierarchy();
                Need(!play.IsCompleted, "Child detach cancelled whole game");
                double wallPause = Time.realtimeSinceStartupAsDouble, elapsedPause = ReceiptMatch.ReviewElapsed;
                await Activate(root, "receipt-pause", ct); double pausedAt = ReceiptMatch.ReviewElapsed;
                await Wait(.35, ct); Need(ReceiptMatch.ReviewElapsed == pausedAt && !root.Q<Button>("receipt-slip-0").enabledInHierarchy, "Paused clock or inputs remained active");
                Inspect(root); await Capture(directory, "paused", receipt, ct); await Activate(root, "receipt-resume", ct);
                ReceiptMatch.ReviewFocus(false); pausedAt = ReceiptMatch.ReviewElapsed; await Wait(.25, ct);
                ReceiptMatch.ReviewFocus(true); Need(ReceiptMatch.ReviewElapsed == pausedAt && Visible(root.Q("receipt-paused")), "Focus return resumed without gesture");
                await Activate(root, "receipt-resume", ct); ReceiptMatch.ReviewPause(true); pausedAt = ReceiptMatch.ReviewElapsed; await Wait(.25, ct);
                ReceiptMatch.ReviewPause(false); Need(ReceiptMatch.ReviewElapsed == pausedAt && Visible(root.Q("receipt-paused")), "App resume silently advanced practice");
                await Activate(root, "receipt-resume", ct);
                receipt.pausedSeconds = Time.realtimeSinceStartupAsDouble - wallPause - (ReceiptMatch.ReviewElapsed - elapsedPause);
                receipt.checks.Add("PASS real Pause/Resume callbacks, focus and app-pause handlers, frozen time/input and explicit Resume.");

                var deck = new ReceiptMatchRound(0, Seed); var seen = new HashSet<int>();
                for (int board = 0; board < ReceiptMatchRound.BoardCount; board++)
                {
                    var current = deck.Current; Need(seen.Add(current.Story), "Repeated authored board");
                    Need(root.Q<Label>(className: "ct-receipt-board-title").text == ReceiptMatchStrings.Board(current, "title"), "Native board differs from deterministic sequence");
                    Inspect(root); await Capture(directory, "board-" + board + "-open", receipt, ct);
                    Need(!root.Q<Button>("receipt-file").enabledInHierarchy && !root.Q<Button>("receipt-row-0").enabledInHierarchy, "Empty unselected board exposes no-op controls");
                    if (board == 0)
                    {
                        float selectedTop=root.Q<Button>("receipt-slip-0").worldBound.yMin;
                        await Activate(root, "receipt-slip-0", ct); Need(SlipState(root,0)==ReceiptMatchStrings.T("held"), "Slip selection missing");
                        Need(Mathf.Abs(root.Q<Button>("receipt-slip-0").worldBound.yMin-selectedTop)<.5f,"Selected/active paper moved vertically into adjacent hit space");
                        Inspect(root); await Capture(directory,"receipt-selected",receipt,ct);
                        await Activate(root, "receipt-slip-0", ct); Need(SlipState(root,0)==ReceiptMatchStrings.T("loose"), "Selection could not be reversed");
                        await Pair(root,0,0,ct); await Pair(root,1,1,ct); await Pair(root,0,1,ct);
                        Need(RowState(root,0)==ReceiptMatchStrings.F("paired","B") && RowState(root,1)==ReceiptMatchStrings.F("paired","A"), "Occupied pair failed to swap");
                        Inspect(root); await Capture(directory,"pairs-swapped",receipt,ct);
                        await Pair(root,1,1,ct); Need(RowState(root,0)==ReceiptMatchStrings.F("paired","A"), "Swap was not reversible");
                        await Activate(root,"receipt-row-1",ct); Need(RowState(root,1)==ReceiptMatchStrings.T("unpaired") && SlipState(root,1)==ReceiptMatchStrings.T("loose"), "Paired row did not undo");
                        await Activate(root,"receipt-clear",ct);
                        Need(root.Query<Label>(className:"ct-receipt-row-state").ToList().All(label=>label.text==ReceiptMatchStrings.T("unpaired")), "Clear did not release pairs");
                    }
                    for (int row = 0; row < 4; row++) await Pair(root,CorrectReceipt(current,row),row,ct);
                    if (board == 2) await Pair(root,CorrectReceipt(current,0),1,ct); // Swap two correct pairs: two wrong, ten points lost.
                    Inspect(root); await Capture(directory,"board-"+board+"-paired",receipt,ct);
                    while (ReceiptMatch.ReviewElapsed < (board + 1) * 6) await Awaitable.NextFrameAsync(ct);
                    double submittedAt = Time.realtimeSinceStartupAsDouble;
                    await Activate(root,"receipt-file",ct);
                    if(board==4)
                    {
                        Need(play.IsCompleted && Time.realtimeSinceStartupAsDouble-submittedAt<.5,"Fifth File did not finish immediately"); break;
                    }
                    Need(!root.Q<Button>("receipt-slip-0").enabledInHierarchy && !Visible(root.Q("receipt-file")),"Filed board remained editable");
                    Need(root.Q<Label>(className:"ct-receipt-instruction").text.StartsWith(ReceiptMatchStrings.F("filed",board==2?2:4,board==2?10:20),StringComparison.Ordinal),"Submission outcome is not explicit");
                    if(board==2) Need(RowState(root,0)==ReceiptMatchStrings.F("correctReceipt",ReceiptMatchStrings.Letter(CorrectReceipt(current,0))),"Wrong pair lacks the correct receipt reference");
                    Inspect(root); await Capture(directory,"board-"+board+"-filed",receipt,ct);
                    await Activate(root,"receipt-next",ct); deck.File(board,0); deck.Next(board,0);
                }
                Need(seen.Count==5 && play.IsCompleted,"Fifth board did not settle");
                var result=await play; receipt.wallSeconds=Time.realtimeSinceStartupAsDouble-started;
                receipt.score=result.Score;receipt.filed=result.FiledBoards;receipt.correct=result.CorrectPairs;receipt.wrong=result.WrongPairs;receipt.unpaired=result.UnpairedRows;
                Need(result.Score==90&&result.FiledBoards==5&&result.CorrectPairs==18&&result.WrongPairs==2&&result.UnpairedRows==0,"Finite pair score or summary wrong");
                Need(receipt.wallSeconds-receipt.pausedSeconds>=30&&receipt.wallSeconds-receipt.pausedSeconds<40&&root.parent==null,"Wrong active ceiling/early completion or retained overlay");
                receipt.checks.Add("PASS all five authored boards; real selection, reverse selection, pairing, occupied swap/reverse, unpair, Clear, File and Next; two explicit wrong pairs; strict bounds and immediate fifth File at about30 active seconds.");
                var resultRoot = Result(result, owner); await Frames(4, ct); Inspect(resultRoot); await Capture(directory, "result", receipt, ct);
                await Activate(resultRoot, "receipt-again", ct); await Frames(3, ct);
                var replay = UiRoot.Instance.ModalLayer.Q("receipt-match-game"); Need(replay != null, "Play again did not start human-controlled replay");
                replay.RemoveFromHierarchy(); await Frames(4, ct);
                Need(UiRoot.Instance.ModalLayer.Q("receipt-result") != null, "Cancelled replay lost completed result");
                receipt.checks.Add("PASS immediate fifth File at about30 active seconds (50second ceiling),90/100 for two intentionally swapped pairs, immediate unscripted replay and cancellation without a new score.");
                receipt.pass = true; Log.Info("receipt-match-review", "PASS local receipt puzzle; no admin API/payout.");
            }
            catch (Exception error)
            {
                receipt.failure = error.Message; try { await Capture(directory, "failure", receipt, CancellationToken.None); } catch { }
                UiRoot.Instance?.ModalLayer.Q("receipt-match-game")?.RemoveFromHierarchy();
                if (play != null) { try { await play; } catch (OperationCanceledException) { } }
                throw;
            }
            finally { File.WriteAllText(Path.Combine(directory, "receipt-match-review.json"), JsonUtility.ToJson(receipt, true)); }
        }
        private static string RowState(VisualElement root,int row)=>root.Q<Button>("receipt-row-"+row).Q<Label>(className:"ct-receipt-row-state").text;
        private static string SlipState(VisualElement root,int receipt)=>root.Q<Button>("receipt-slip-"+receipt).Q<Label>(className:"ct-receipt-slip-state").text;
        private static async Task Pair(VisualElement root,int receipt,int row,CancellationToken ct)
        {await Activate(root,"receipt-slip-"+receipt,ct);await Activate(root,"receipt-row-"+row,ct);}
        private static int CorrectReceipt(ReceiptMatchBoard board,int row)
        {
            var entry=board.Row(row);int found=-1;
            for(int receipt=0;receipt<4;receipt++)
            {var slip=board.Receipt(receipt);if(entry.Amount==slip.Amount&&entry.Day==slip.Day&&entry.Reference==slip.Reference){Need(found<0,"Duplicate visible receipt tuple");found=receipt;}}
            Need(found>=0,"No receipt for visible entry");return found;
        }
        private static VisualElement Result(ReceiptMatchResult result, CancellationToken owner)
        {
            var root = El.Div("ct-desk-overlay"); root.name = "receipt-result"; bool busy = false;
            async void Again()
            {
                if (busy || owner.IsCancellationRequested || root.parent == null) return;
                busy = true; root.RemoveFromHierarchy(); using var attempt = CancellationTokenSource.CreateLinkedTokenSource(owner);
                try { var next = await ReceiptMatch.Play(attempt.Token); if (!owner.IsCancellationRequested) Result(next, owner); }
                catch (OperationCanceledException) { if (!owner.IsCancellationRequested && UiRoot.Instance != null) Result(result, owner); }
                catch (Exception error) { Log.Warn("receipt-practice", "Replay interrupted: " + error.GetType().Name); if (!owner.IsCancellationRequested && UiRoot.Instance != null) Result(result, owner); }
                finally { attempt.Cancel(); busy = false; }
            }
            var again = El.Btn(ReceiptMatchStrings.T("again"), Again, "ct-btn ct-desk-next"); again.name = "receipt-again";
            var back = El.Btn(ReceiptMatchStrings.T("back"), () => root.RemoveFromHierarchy(), "ct-btn ct-desk-next"); back.name = "receipt-back";
            root.Add(El.Div("ct-desk-panel ct-shift-panel", ModDeskCopy(ReceiptMatchStrings.T("practice"), "ct-desk-eyebrow"),
                ModDeskCopy(ReceiptMatchStrings.T("complete"), "ct-desk-title"), ModDeskCopy(ReceiptMatchStrings.F("score", result.Score), "ct-shift-pay"),
                ModDeskCopy(ReceiptMatchStrings.Summary(result), "ct-shift-copy"), again, back));
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
            var statement=root.Q("receipt-statement");var tray=root.Q("receipt-tray");
            if(statement!=null&&tray!=null)
            {
                Need(statement.worldBound.xMax<=tray.worldBound.xMin+2,"Statement overlaps receipt tray");
                foreach(var column in new[]{statement,tray})
                {
                    var entries=column.Children().OfType<Button>().ToArray();Need(entries.Length==4,"Column lacks four entries");
                    for(int i=0;i<4;i++)
                    {
                        Within(entries[i].worldBound,column.worldBound,"Entry outside its sheet/tray");
                        if(i>0)Need(entries[i-1].worldBound.yMax<=entries[i].worldBound.yMin+2,"Entry hit regions overlap: "+entries[i-1].name+" "+entries[i-1].worldBound+" translate="+entries[i-1].resolvedStyle.translate+" selected="+entries[i-1].ClassListContains("ct-receipt-selected")+" next="+entries[i].name+" "+entries[i].worldBound);
                    }
                    Need(column.Q<Label>(className:"ct-receipt-section").worldBound.yMax<=entries[0].worldBound.yMin+2,"Column heading overlaps first entry");
                }
            }
            foreach (var button in root.Query<Button>().ToList()) if (Visible(button)) { Touch(button); Within(button.worldBound,root.worldBound,"Control outside viewport: "+button.name); }
            foreach (var label in root.Query<Label>().ToList())
            {
                if (!Visible(label) || string.IsNullOrEmpty(label.text)) continue;
                var measured = label.MeasureTextSize(label.text,label.contentRect.width,VisualElement.MeasureMode.AtMost,0,VisualElement.MeasureMode.Undefined);
                Need(measured.x <= label.contentRect.width + 2 && measured.y <= label.contentRect.height + 2,
                    "Clipped text '"+label.text+"' content="+label.contentRect+" measured="+measured);
                Within(label.worldBound,root.worldBound,"Text outside viewport: "+label.text);
                var button=label.GetFirstAncestorOfType<Button>();
                if(button!=null)Within(label.worldBound,button.LocalToWorld(button.contentRect),"Slip/row text outside its hit region");
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
        private static void Need(bool condition,string message) { if(!condition)throw new InvalidOperationException("Receipt Match review: "+message); }
    }
}
#endif
