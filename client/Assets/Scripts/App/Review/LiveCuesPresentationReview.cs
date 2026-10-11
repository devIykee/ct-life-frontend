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
    public static class LiveCuesPresentationReview
    {
        private const int Seed = 20261007;
        [Serializable] private sealed class InputWitness
        {
            public int cue;
            public string action, expected, feedbackBefore, feedbackAfter;
            public double sampledBefore, consumedAt, wallBefore, wallAfter, opens, closes;
            public bool dispatched;
        }
        [Serializable] private sealed class Receipt
        {
            public bool pass;
            public int score, hits, early, wrong, missed, bestCombo;
            public double wallSeconds, pausedSeconds;
            public string failure;
            public List<string> checks = new List<string>(), captures = new List<string>(), cueInputs = new List<string>();
            public List<InputWitness> inputs = new List<InputWitness>();
            public string limits = "Native local fictional show layout/callback review. Main scripted run disables OS focus observation and separately invokes actual focus/pause handlers. NavigationSubmit invokes real buttons; physical Android timing/background delivery, human readability, fun/retention and paid routing remain unverified. No API, microphone, external post, audience or payout.";
        }
        public static async Task Run(PhoneShell phone, PracticeJobsApp practice, string directory, CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Path.IsPathRooted(directory)) directory = Path.Combine(Application.persistentDataPath, directory);
#endif
            directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner); timeout.CancelAfter(TimeSpan.FromSeconds(80));
            var ct = timeout.Token; var receipt = new Receipt(); Task<LiveCuesResult> play = null;
            try
            {
                Need(practice != null, "Practice hub missing"); int starts = practice.RoundsStarted;
                phone.Open("jobs"); await Frames(4, ct);
                var page = UiRoot.Instance.PhoneLayer.Q<ScrollView>("practice-jobs");
                var entry = page?.Q<Button>("practice-play-cues"); Need(entry != null, "Seventh practice card missing");
                page.ScrollTo(entry); await Frames(4, ct); Within(entry.worldBound, page.contentViewport.worldBound, "Practice entry outside its viewport");
                await Capture(directory, "practice-entry", receipt, ct); await Activate(page, "practice-play-cues", ct);
                Need(practice.IsRunning && practice.RoundsStarted == starts + 1, "Actual seventh-card callback did not enter shared flight");
                var live = UiRoot.Instance.ModalLayer.Q("live-cues-game"); Need(live != null, "Practice callback did not mount game");
                await Activate(live, Visible(live.Q("cues-paused")) ? "cues-paused-end" : "cues-end", ct);
                await Until(() => !practice.IsRunning, 3, ct);
                Need(UiRoot.Instance.PhoneLayer.Q("practice-jobs") != null && practice.Result == null, "Cancelled hub attempt produced a result or failed to reopen Jobs");
                phone.Close(); await Frames(3, ct);
                receipt.checks.Add("PASS actual seventh Jobs card, shared flight, unscripted entry and End; cancellation reopens Jobs with no score.");
                using (var pre = new CancellationTokenSource())
                { pre.Cancel(); bool denied = false; try { _ = LiveCues.PlayReview(pre.Token, Seed); } catch (OperationCanceledException) { denied = true; } Need(denied, "Pre-cancelled play mounted"); }
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct))
                { var operation = LiveCues.PlayReview(attempt.Token, Seed); await Frames(3, ct); UiRoot.Instance.ModalLayer.Q("live-cues-game").RemoveFromHierarchy(); await Cancelled(operation); }
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct))
                { var operation = LiveCues.PlayReview(attempt.Token, Seed); await Frames(3, ct); attempt.Cancel(); await Frames(3, ct); await Cancelled(operation); }
                receipt.checks.Add("PASS pre-cancel, token cancellation and root detach return no score; next attempts mount.");
                double started = Time.realtimeSinceStartupAsDouble;
                play = LiveCues.PlayReview(ct, Seed); bool concurrent = false;
                try { _ = LiveCues.PlayReview(ct, Seed); } catch (InvalidOperationException) { concurrent = true; }
                Need(concurrent, "Concurrent show accepted"); await Frames(4, ct);
                var root = UiRoot.Instance.ModalLayer.Q("live-cues-game"); Inspect(root);
                Need(root.Q<Label>("cues-air-copy").text==LiveCuesStrings.T("onAir"), "Active fictional show indicator missing");
                var child = new VisualElement(); root.Add(child); child.RemoveFromHierarchy(); Need(!play.IsCompleted, "Child detach cancelled game");
                double wallPause = Time.realtimeSinceStartupAsDouble, elapsedPause = LiveCues.ReviewElapsed;
                await Activate(root, "cues-pause", ct); double pausedAt = LiveCues.ReviewElapsed;
                Need(root.Q<Label>("cues-air-copy").text==LiveCuesStrings.T("onHold"), "Paused fictional show still claims ON AIR");
                await Wait(.35, ct); Need(LiveCues.ReviewElapsed == pausedAt && !root.Q<Button>("cues-pad-0").enabledInHierarchy, "Paused clock/input advanced");
                Inspect(root); await Capture(directory, "paused", receipt, ct); await Activate(root, "cues-resume", ct);
                LiveCues.ReviewFocus(false); pausedAt = LiveCues.ReviewElapsed; await Wait(.25, ct);
                LiveCues.ReviewFocus(true); Need(LiveCues.ReviewElapsed == pausedAt && Visible(root.Q("cues-paused")), "Focus return resumed without gesture");
                await Activate(root, "cues-resume", ct); LiveCues.ReviewPause(true); pausedAt = LiveCues.ReviewElapsed; await Wait(.25, ct);
                LiveCues.ReviewPause(false); Need(LiveCues.ReviewElapsed == pausedAt && Visible(root.Q("cues-paused")), "App resume advanced practice");
                await Activate(root, "cues-resume", ct);
                receipt.pausedSeconds = Time.realtimeSinceStartupAsDouble - wallPause - (LiveCues.ReviewElapsed - elapsedPause);
                receipt.checks.Add("PASS actual Pause/Resume buttons, focus/app-pause handlers, frozen clock/input and explicit Resume.");
                var deck = new LiveCuesRound(0, Seed); var actions = new HashSet<LiveCueAction>();
                for (int i = 0; i < 20; i++)
                {
                    var cue = deck.Cues[i];
                    while (LiveCues.ReviewElapsed < cue.Approach + .15) await Awaitable.NextFrameAsync(ct);
                    Need(root.Q<Label>(className:"ct-cues-script").text == LiveCuesStrings.Cue(cue), "Cue copy differs from deterministic show");
                    Need(root.Q<Label>(className:"ct-cues-action").text == LiveCuesStrings.Action(cue.Action), "Requested action missing");
                    if (i < 19) Need(root.Q<Label>(className:"ct-cues-next-copy").text == LiveCuesStrings.Cue(deck.Cues[i+1]), "Upcoming cue preview missing");
                    Inspect(root);
                    // Screenshot encoding can block the Editor long enough to cross Opens.
                    // Keep the deliberately early action free of capture/file work before input.
                    if(i!=1)await Capture(directory, "cue-"+i.ToString("00")+"-approach", receipt, ct);
                    if (i == 11)
                    {
                        while (LiveCues.ReviewElapsed <= cue.Closes + .05) await Awaitable.NextFrameAsync(ct);
                        Need(root.Q<Label>(className:"ct-cues-feedback").text == LiveCuesStrings.F("missed",LiveCuesStrings.Action(cue.Action)), "Expired cue lacks explicit missed feedback");
                        receipt.cueInputs.Add(i+": missed by no input"); await Capture(directory,"missed",receipt,ct); continue;
                    }
                    double target = i == 1 ? cue.Approach + .20 : cue.Target;
                    while (LiveCues.ReviewElapsed < target) await Awaitable.NextFrameAsync(ct);
                    Need(LiveCues.ReviewElapsed <= cue.Closes, "Native scheduling missed the cue before input");
                    if (i != 1)
                    {
                        var marker=root.Q("cues-needle"); var hitWindow=root.Q("cues-window");
                        Need(marker.worldBound.center.x>=hitWindow.worldBound.xMin&&marker.worldBound.center.x<=hitWindow.worldBound.xMax,"On-time marker is outside the declared window");
                        Need(root.Q<Label>(className:"ct-cues-status").text==LiveCuesStrings.T("go"),"Open window is not labelled");
                    }
                    var action = i == 5 ? (LiveCueAction)(((int)cue.Action+1)%3) : cue.Action;
                    string expected = i==1?LiveCuesStrings.T("early"):i==5?LiveCuesStrings.F("wrong",LiveCuesStrings.Action(cue.Action)):LiveCuesStrings.T("hit");
                    var witness=new InputWitness { cue=i,action=action.ToString(),expected=expected,
                        sampledBefore=LiveCues.ReviewElapsed,wallBefore=Time.realtimeSinceStartupAsDouble,opens=cue.Opens,closes=cue.Closes,
                        feedbackBefore=root.Q<Label>(className:"ct-cues-feedback").text };
                    receipt.inputs.Add(witness); // Retain the failed boundary, even if the precondition rejects dispatch.
                    if(i==1)
                    {
                        Need(witness.sampledBefore>=cue.Approach&&witness.sampledBefore<cue.Opens-.25,"Native scheduling missed the early-input safety margin before dispatch");
                        Need(root.Q("cues-needle").worldBound.center.x<root.Q("cues-window").worldBound.xMin,"Early marker is not before the declared window");
                        Need(root.Q<Label>(className:"ct-cues-status").text==LiveCuesStrings.T("standby"),"Early cue was already labelled open");
                    }
                    Submit(root,"cues-pad-"+(int)action);witness.dispatched=true;
                    // Press synchronously steps the model using its real clock. No await or capture occurs between these samples.
                    witness.consumedAt=LiveCues.ReviewElapsed;witness.wallAfter=Time.realtimeSinceStartupAsDouble;
                    witness.feedbackAfter=root.Q<Label>(className:"ct-cues-feedback").text;actions.Add(action);
                    if(i==1)Need(witness.consumedAt<cue.Opens,"Early callback actually crossed Opens: "+JsonUtility.ToJson(witness));
                    else Need(witness.consumedAt>=cue.Opens&&witness.consumedAt<=cue.Closes,"On-time callback crossed its window: "+JsonUtility.ToJson(witness));
                    Need(witness.feedbackAfter==expected,"Wrong immediate timing feedback: "+JsonUtility.ToJson(witness));
                    receipt.cueInputs.Add(i+": "+action+" at active "+witness.consumedAt.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+"; "+expected);
                    await Frames(2,ct);
                    if (i==0||i==1||i==5) { Inspect(root); await Capture(directory,i==0?"hit":i==1?"early":"wrong-control",receipt,ct); }
                }
                await Until(()=>play.IsCompleted,2,ct);var result=await play;
                receipt.wallSeconds=Time.realtimeSinceStartupAsDouble-started;receipt.score=result.Score;receipt.hits=result.Hits;
                receipt.early=result.Early;receipt.wrong=result.Wrong;receipt.missed=result.Missed;receipt.bestCombo=result.BestCombo;
                Need(actions.Count==3&&result.Score==85&&result.Hits==17&&result.Early==1&&result.Wrong==1&&result.Missed==1,"Finite native cue result differs");
                Need(receipt.wallSeconds-receipt.pausedSeconds>=45&&receipt.wallSeconds-receipt.pausedSeconds<48&&root.parent==null,"45-active-second boundary or cleanup failed");
                receipt.checks.Add("PASS 20 visible cue previews, all 3 real action controls, moving marker/window geometry, one early/one wrong/one expired cue, 85/100 over 45 active seconds and strict text/touch/viewport bounds.");
                var resultRoot=Result(result,owner);await Frames(4,ct);Inspect(resultRoot);await Capture(directory,"result",receipt,ct);
                await Activate(resultRoot,"cues-again",ct);await Frames(3,ct);var replay=UiRoot.Instance.ModalLayer.Q("live-cues-game");Need(replay!=null,"Immediate human-controlled replay did not mount");
                replay.RemoveFromHierarchy();await Frames(4,ct);Need(UiRoot.Instance.ModalLayer.Q("cues-result")!=null,"Cancelled replay lost completed result");
                receipt.checks.Add("PASS immediate unscripted Play again; replay cancellation returns prior result without new score.");
                receipt.pass=true;Log.Info("live-cues-review","PASS local fictional timing show; no mic/network/payout.");
            }
            catch(Exception error)
            {
                receipt.failure=error.Message;try{await Capture(directory,"failure",receipt,CancellationToken.None);}catch{}
                UiRoot.Instance?.ModalLayer.Q("live-cues-game")?.RemoveFromHierarchy();
                if(play!=null){try{await play;}catch(OperationCanceledException){}}
                throw;
            }
            finally{File.WriteAllText(Path.Combine(directory,"live-cues-review.json"),JsonUtility.ToJson(receipt,true));}
        }
        private static VisualElement Result(LiveCuesResult result, CancellationToken owner)
        {
            var root = El.Div("ct-desk-overlay"); root.name = "cues-result"; bool busy = false;
            async void Again()
            {
                if (busy || owner.IsCancellationRequested || root.parent == null) return;
                busy = true; root.RemoveFromHierarchy(); using var attempt = CancellationTokenSource.CreateLinkedTokenSource(owner);
                try { var next = await LiveCues.Play(attempt.Token); if (!owner.IsCancellationRequested) Result(next, owner); }
                catch (OperationCanceledException) { if (!owner.IsCancellationRequested && UiRoot.Instance != null) Result(result, owner); }
                catch (Exception error) { Log.Warn("cues-practice", "Replay interrupted: " + error.GetType().Name); if (!owner.IsCancellationRequested && UiRoot.Instance != null) Result(result, owner); }
                finally { attempt.Cancel(); busy = false; }
            }
            var again = El.Btn(LiveCuesStrings.T("again"), Again, "ct-btn ct-desk-next"); again.name = "cues-again";
            var back = El.Btn(LiveCuesStrings.T("back"), () => root.RemoveFromHierarchy(), "ct-btn ct-desk-next"); back.name = "cues-back";
            root.Add(El.Div("ct-desk-panel ct-shift-panel", ModDeskCopy(LiveCuesStrings.T("practice"), "ct-desk-eyebrow"),
                ModDeskCopy(LiveCuesStrings.T("complete"), "ct-desk-title"), ModDeskCopy(LiveCuesStrings.F("score", result.Score), "ct-shift-pay"),
                ModDeskCopy(LiveCuesStrings.Summary(result), "ct-shift-copy"), again, back));
            var sheet = Resources.Load<StyleSheet>("UI/mod-desk"); if (sheet != null) root.styleSheets.Add(sheet);
            UiRoot.Instance.ModalLayer.Add(root); return root;
        }
        private static Label ModDeskCopy(string text,string classes) { var label=El.Text(text,classes);label.enableRichText=false;return label; }
        private static async Task Activate(VisualElement root, string name, CancellationToken ct)
        {
            Submit(root,name); await Frames(2,ct);
        }
        private static void Submit(VisualElement root,string name)
        {
            var button = root.Q<Button>(name); Need(button != null && Visible(button) && button.enabledInHierarchy, "Unavailable control " + name);
            Touch(button); using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
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
            var console=root.Q("cues-console");
            if(console!=null)
            {
                var pads=console.Children().OfType<Button>().ToArray();Need(pads.Length==3,"Console lacks three controls");
                for(int i=0;i<3;i++)
                {
                    Within(pads[i].worldBound,console.worldBound,"Pad outside console");
                    if(i>0)Need(pads[i-1].worldBound.xMax<=pads[i].worldBound.xMin+2,"Pad hit regions overlap");
                }
                var stage=root.Q("cues-stage");var track=root.Q("cues-track");var window=root.Q("cues-window");
                Need(stage.worldBound.yMax<=track.worldBound.yMin+2&&track.worldBound.yMax<=console.worldBound.yMin+2,"Stage, timing rail or pads overlap");
                Within(window.worldBound,track.worldBound,"Timing window outside rail");
                Need(Mathf.Abs(window.layout.width/track.contentRect.width-(float)(LiveCuesRound.HalfWindow/LiveCuesRound.ApproachSeconds))<.003f,"Rendered window is not 800 ms of the timing rail");
            }
            var panel=root.Q(className:"ct-cues-panel");
            foreach (var button in root.Query<Button>().ToList()) if (Visible(button))
            {
                Touch(button); Within(button.worldBound,root.worldBound,"Control outside viewport: "+button.name);
                if(panel!=null)Within(button.worldBound,panel.LocalToWorld(panel.contentRect),"Control outside panel content: "+button.name);
            }
            foreach (var label in root.Query<Label>().ToList())
            {
                if (!Visible(label) || string.IsNullOrEmpty(label.text)) continue;
                if(panel!=null) Need(label.resolvedStyle.fontSize >= 32, "Broadcast text below the measured phone typography floor: " + label.text);
                var measured = label.MeasureTextSize(label.text,label.contentRect.width,VisualElement.MeasureMode.AtMost,0,VisualElement.MeasureMode.Undefined);
                Need(measured.x <= label.contentRect.width + 2 && measured.y <= label.contentRect.height + 2,
                    "Clipped text '"+label.text+"' content="+label.contentRect+" measured="+measured);
                Within(label.worldBound,root.worldBound,"Text outside viewport: "+label.text);
                if(panel!=null)Within(label.worldBound,panel.LocalToWorld(panel.contentRect),"Text outside panel content: "+label.text);
                var button=label.GetFirstAncestorOfType<Button>();
                if(button!=null)Within(label.worldBound,button.LocalToWorld(button.contentRect),"Pad text outside its hit region");
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
        private static void Need(bool condition,string message) { if(!condition)throw new InvalidOperationException("Live Cues review: "+message); }
    }
}
#endif
