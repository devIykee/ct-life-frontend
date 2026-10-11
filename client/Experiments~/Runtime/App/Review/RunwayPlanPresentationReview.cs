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
    public static class RunwayPlanPresentationReview
    {
        private const int Seed = 20261007;
        [Serializable] private sealed class Receipt
        {
            public bool pass;
            public int score, filed, valid;
            public double wallSeconds, pausedSeconds;
            public string failure;
            public List<string> checks = new List<string>(), captures = new List<string>();
            public string limits = "Native local fictional operating-budget layout/callback review. Main scripted run disables OS focus observation and separately invokes real focus/pause handlers. Programmatic NavigationSubmit invokes actual buttons; physical Android touches/background delivery, fun/retention and paid routing remain unverified. No API, mic, real account action or payout.";
        }
        public static async Task Run(PhoneShell phone, PracticeJobsApp practice, string directory, CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Path.IsPathRooted(directory)) directory = Path.Combine(Application.persistentDataPath, directory);
#endif
            directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner); timeout.CancelAfter(TimeSpan.FromSeconds(80));
            var ct = timeout.Token; var receipt = new Receipt(); Task<RunwayPlanResult> play = null;
            try
            {
                Need(practice != null, "Practice hub missing");
                int starts = practice.RoundsStarted;
                phone.Open("jobs"); await Frames(4, ct);
                var page = UiRoot.Instance.PhoneLayer.Q<ScrollView>("practice-jobs");
                var entry = page?.Q<Button>("practice-play-runway"); Need(entry != null, "Eighth practice card missing");
                page.ScrollTo(entry); await Frames(4, ct);
                Within(entry.worldBound, page.contentViewport.worldBound, "Practice entry outside its actual viewport");
                await Capture(directory, "practice-entry", receipt, ct);
                await Activate(page, "practice-play-runway", ct);
                Need(practice.IsRunning && practice.RoundsStarted == starts + 1, "Actual eighth-card callback did not enter shared flight");
                var live = UiRoot.Instance.ModalLayer.Q("runway-plan-game"); Need(live != null, "Practice callback did not mount actual game");
                var pausing = live.Q("runway-paused");
                await Activate(live, pausing.resolvedStyle.display != DisplayStyle.None ? "runway-paused-end" : "runway-end", ct);
                await Until(() => !practice.IsRunning, 3, ct);
                Need(UiRoot.Instance.PhoneLayer.Q("practice-jobs") != null && practice.Result == null, "Cancelled hub attempt produced a score or failed to reopen Jobs");
                phone.Close(); await Frames(3, ct);
                receipt.checks.Add("PASS actual eighth Jobs card, shared single-flight ownership, unscripted game entry and End practice; cancellation reopens Jobs with no result.");

                using (var pre = new CancellationTokenSource())
                { pre.Cancel(); bool denied = false; try { _ = RunwayPlan.PlayReview(pre.Token, Seed); } catch (OperationCanceledException) { denied = true; } Need(denied, "Pre-cancelled play mounted"); }
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var operation = RunwayPlan.PlayReview(attempt.Token, Seed); await Frames(3, ct);
                    UiRoot.Instance.ModalLayer.Q("runway-plan-game").RemoveFromHierarchy(); await Cancelled(operation);
                }
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var operation = RunwayPlan.PlayReview(attempt.Token, Seed); await Frames(3, ct);
                    attempt.Cancel(); await Frames(3, ct); await Cancelled(operation);
                }
                receipt.checks.Add("PASS pre-cancel, attached cancellation and root detach return no score; subsequent attempts mount.");

                double started = Time.realtimeSinceStartupAsDouble;
                play = RunwayPlan.PlayReview(ct, Seed); bool concurrent = false;
                try { _ = RunwayPlan.PlayReview(ct, Seed); } catch (InvalidOperationException) { concurrent = true; }
                Need(concurrent, "Concurrent round accepted"); await Frames(4, ct);
                var root = UiRoot.Instance.ModalLayer.Q("runway-plan-game"); Inspect(root);
                var child = new VisualElement(); root.Add(child); child.RemoveFromHierarchy();
                Need(!play.IsCompleted, "Child detach cancelled whole game");
                double wallPause = Time.realtimeSinceStartupAsDouble, elapsedPause = RunwayPlan.ReviewElapsed;
                await Activate(root, "runway-pause", ct); double pausedAt = RunwayPlan.ReviewElapsed;
                await Wait(.35, ct); Need(RunwayPlan.ReviewElapsed == pausedAt && !root.Q<Button>("runway-preset-0-1").enabledInHierarchy, "Paused clock or inputs remained active");
                Inspect(root); await Capture(directory, "paused", receipt, ct); await Activate(root, "runway-resume", ct);
                RunwayPlan.ReviewFocus(false); pausedAt = RunwayPlan.ReviewElapsed; await Wait(.25, ct);
                RunwayPlan.ReviewFocus(true); Need(RunwayPlan.ReviewElapsed == pausedAt && Visible(root.Q("runway-paused")), "Focus return resumed without gesture");
                await Activate(root, "runway-resume", ct); RunwayPlan.ReviewPause(true); pausedAt = RunwayPlan.ReviewElapsed; await Wait(.25, ct);
                RunwayPlan.ReviewPause(false); Need(RunwayPlan.ReviewElapsed == pausedAt && Visible(root.Q("runway-paused")), "App resume silently advanced practice");
                await Activate(root, "runway-resume", ct);
                receipt.pausedSeconds = Time.realtimeSinceStartupAsDouble - wallPause - (RunwayPlan.ReviewElapsed - elapsedPause);
                receipt.checks.Add("PASS real Pause/Resume callbacks, focus and app-pause handlers, frozen time/input and explicit Resume.");

                var deck = new RunwayPlanRound(0,Seed);var seen = new HashSet<int>();
                for(int boardIndex=0;boardIndex<4;boardIndex++)
                {
                    var current=deck.Current;Need(seen.Add(current.Story),"Repeated authored board");
                    Need(root.Q<Label>(className:"ct-runway-board-title").text==RunwayPlanStrings.Board(current,"title"),"Native budget differs from seeded sequence");
                    Inspect(root);await Capture(directory,"board-"+boardIndex+"-open",receipt,ct);
                    Need(!root.Q<Button>("runway-file").enabledInHierarchy&&!root.Q<Button>("runway-undo").enabledInHierarchy,"Empty plan exposes no-op controls");
                    if(boardIndex==0)
                    {
                        foreach(int level in new[]{1,2,0})await Activate(root,"runway-preset-0-"+level,ct);
                        Need(root.Q<Label>(className:"ct-runway-edits").text==RunwayPlanStrings.F("edits",3),"Edit allowance not spent");
                        Need(root.Query<Button>(className:"ct-runway-preset").ToList().All(b=>!b.enabledInHierarchy),"Fourth edit still enabled");
                        Inspect(root);await Capture(directory,"three-changes",receipt,ct);
                        foreach(int restored in new[]{2,1,0})
                        {
                            await Activate(root,"runway-undo",ct);
                            Need(root.Q<Button>("runway-preset-0-"+restored).ClassListContains("ct-runway-selected"),"Undo restored wrong preset");
                        }
                        Need(root.Q<Label>(className:"ct-runway-edits").text==RunwayPlanStrings.F("edits",0),"Undo did not restore edit allowance");
                        Inspect(root);await Capture(directory,"undo-restored",receipt,ct);
                    }
                    if(boardIndex==2)
                    {
                        int service=Enumerable.Range(0,5).First(i=>current.Line(i).Metric==0);
                        await Activate(root,"runway-preset-"+service+"-2",ct);
                    }
                    else
                    {
                        var solution=Solve(current);
                        for(int line=0;line<5;line++)if(solution[line]>0)await Activate(root,"runway-preset-"+line+"-"+solution[line],ct);
                        Need(root.Q<Label>(className:"ct-runway-feedback").text==RunwayPlanStrings.T("ready"),"Valid plan does not show readiness");
                    }
                    Inspect(root);await Capture(directory,"board-"+boardIndex+"-prepared",receipt,ct);
                    while(RunwayPlan.ReviewElapsed<(boardIndex+1)*7)await Awaitable.NextFrameAsync(ct);
                    double submitted=Time.realtimeSinceStartupAsDouble;await Activate(root,"runway-file",ct);
                    if(boardIndex==3){Need(play.IsCompleted&&Time.realtimeSinceStartupAsDouble-submitted<.5,"Fourth File did not finish immediately");break;}
                    Need(root.Query<Button>(className:"ct-runway-preset").ToList().All(b=>!b.enabledInHierarchy)&&!Visible(root.Q("runway-file")),"Filed plan remained editable");
                    string outcome=RunwayPlanStrings.T(boardIndex==2?"rejected":"filed");
                    Need(root.Q<Label>(className:"ct-runway-feedback").text.StartsWith(outcome,StringComparison.Ordinal),"Filed outcome not explicit");
                    if(boardIndex==2)Need(root.Q<Label>(className:"ct-runway-feedback").text.Contains(RunwayPlanStrings.F("serviceFail",RunwayPlanStrings.Board(current,"a"),current.MinimumA)),"Failed service requirement not explained");
                    Inspect(root);await Capture(directory,"board-"+boardIndex+"-filed",receipt,ct);
                    await Activate(root,"runway-next",ct);deck.File(boardIndex,0);deck.Next(boardIndex,0);
                }
                Need(play.IsCompleted&&seen.Count==4,"All authored plans did not finish");var result=await play;
                receipt.wallSeconds=Time.realtimeSinceStartupAsDouble-started;receipt.score=result.Score;receipt.filed=result.Filed;receipt.valid=result.Valid;
                Need(result.Score==75&&result.Filed==4&&result.Valid==3,"Finite constraint score differs");
                Need(receipt.wallSeconds-receipt.pausedSeconds>=28&&receipt.wallSeconds-receipt.pausedSeconds<40&&root.parent==null,"Early completion or cleanup failed");
                receipt.checks.Add("PASS all four authored budgets, actual Full/Lean/Basic changes, edit exhaustion, exact Undo chain, File/Next and service-failure explanation;75/100 from one invalid plan;fourth File finishes immediately after about28 active seconds.");
                var resultRoot=Result(result,owner);await Frames(4,ct);Inspect(resultRoot);await Capture(directory,"result",receipt,ct);
                await Activate(resultRoot,"runway-again",ct);await Frames(3,ct);var replay=UiRoot.Instance.ModalLayer.Q("runway-plan-game");Need(replay!=null,"Human-controlled replay did not mount");
                replay.RemoveFromHierarchy();await Frames(4,ct);Need(UiRoot.Instance.ModalLayer.Q("runway-result")!=null,"Cancelled replay lost completed result");
                receipt.checks.Add("PASS unscripted Play again and cancellation preserving prior completed result; no new score.");
                receipt.pass=true;Log.Info("runway-plan-review","PASS local fictional budget puzzle; no account or payout.");
            }
            catch(Exception error)
            {
                receipt.failure=error.Message;try{await Capture(directory,"failure",receipt,CancellationToken.None);}catch{}
                UiRoot.Instance?.ModalLayer.Q("runway-plan-game")?.RemoveFromHierarchy();if(play!=null){try{await play;}catch(OperationCanceledException){}}
                throw;
            }
            finally{File.WriteAllText(Path.Combine(directory,"runway-plan-review.json"),JsonUtility.ToJson(receipt,true));}
        }
        private static int[] Solve(RunwayBoard board)
        {
            for(int code=0;code<243;code++)
            {
                int value=code,cost=0,a=0,b=0,edits=0;var levels=new int[5];
                for(int i=0;i<5;i++)
                {
                    levels[i]=value%3;value/=3;if(levels[i]!=0)edits++;
                    var line=board.Line(i);cost+=line.Cost(levels[i]);if(line.Metric==0)a+=line.Capacity(levels[i]);if(line.Metric==1)b+=line.Capacity(levels[i]);
                }
                if(edits<=3&&cost<=board.Ceiling&&a>=board.MinimumA&&b>=board.MinimumB)return levels;
            }
            throw new InvalidOperationException("Authored budget has no valid plan.");
        }
        private static VisualElement Result(RunwayPlanResult result, CancellationToken owner)
        {
            var root = El.Div("ct-desk-overlay"); root.name = "runway-result"; bool busy = false;
            async void Again()
            {
                if (busy || owner.IsCancellationRequested || root.parent == null) return;
                busy = true; root.RemoveFromHierarchy(); using var attempt = CancellationTokenSource.CreateLinkedTokenSource(owner);
                try { var next = await RunwayPlan.Play(attempt.Token); if (!owner.IsCancellationRequested) Result(next, owner); }
                catch (OperationCanceledException) { if (!owner.IsCancellationRequested && UiRoot.Instance != null) Result(result, owner); }
                catch (Exception error) { Log.Warn("runway-practice", "Replay interrupted: " + error.GetType().Name); if (!owner.IsCancellationRequested && UiRoot.Instance != null) Result(result, owner); }
                finally { attempt.Cancel(); busy = false; }
            }
            var again = El.Btn(RunwayPlanStrings.T("again"), Again, "ct-btn ct-desk-next"); again.name = "runway-again";
            var back = El.Btn(RunwayPlanStrings.T("back"), () => root.RemoveFromHierarchy(), "ct-btn ct-desk-next"); back.name = "runway-back";
            root.Add(El.Div("ct-desk-panel ct-shift-panel", ModDeskCopy(RunwayPlanStrings.T("practice"), "ct-desk-eyebrow"),
                ModDeskCopy(RunwayPlanStrings.T("complete"), "ct-desk-title"), ModDeskCopy(RunwayPlanStrings.F("score", result.Score), "ct-shift-pay"),
                ModDeskCopy(RunwayPlanStrings.Summary(result), "ct-shift-copy"), again, back));
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
            var board=root.Q("runway-board");
            if(board!=null)
            {
                var strips=board.Children().ToArray();Need(strips.Length==5,"Budget console lacks five expense strips");
                for(int line=0;line<5;line++)
                {
                    Within(strips[line].worldBound,board.worldBound,"Expense strip outside console");
                    if(line>0)Need(strips[line-1].worldBound.xMax<=strips[line].worldBound.xMin+2,"Expense strips overlap");
                    var presets=strips[line].Children().OfType<Button>().ToArray();Need(presets.Length==3,"Missing cost/capacity preset");
                    for(int i=0;i<3;i++)
                    {
                        Within(presets[i].worldBound,strips[line].worldBound,"Preset outside strip");
                        if(i>0)Need(presets[i-1].worldBound.yMax<=presets[i].worldBound.yMin+2,"Preset touch targets overlap");
                    }
                }
                Need(root.Q<Label>(className:"ct-runway-feedback").worldBound.yMin>=board.worldBound.yMax-2,"Console overlaps outcome feedback");
            }
            var panel=root.Q(className:"ct-runway-panel");
            foreach (var button in root.Query<Button>().ToList()) if (Visible(button))
            {
                Touch(button); Within(button.worldBound,root.worldBound,"Control outside viewport: "+button.name);
                if(panel!=null)Within(button.worldBound,panel.LocalToWorld(panel.contentRect),"Control outside panel content: "+button.name);
            }
            foreach (var label in root.Query<Label>().ToList())
            {
                if (!Visible(label) || string.IsNullOrEmpty(label.text)) continue;
                var measured = label.MeasureTextSize(label.text,label.contentRect.width,VisualElement.MeasureMode.AtMost,0,VisualElement.MeasureMode.Undefined);
                Need(measured.x <= label.contentRect.width + 2 && measured.y <= label.contentRect.height + 2,
                    "Clipped text '"+label.text+"' content="+label.contentRect+" measured="+measured+" world="+label.worldBound+" parent="+label.parent?.worldBound);
                Within(label.worldBound,root.worldBound,"Text outside viewport: "+label.text);
                if(panel!=null)Within(label.worldBound,panel.LocalToWorld(panel.contentRect),"Text outside panel content: "+label.text);
                var button=label.GetFirstAncestorOfType<Button>();
                if(button!=null)Within(label.worldBound,button.LocalToWorld(button.contentRect),"Preset text outside its hit region");
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
        private static void Need(bool condition,string message) { if(!condition)throw new InvalidOperationException("Runway Plan review: "+message); }
    }
}
#endif
