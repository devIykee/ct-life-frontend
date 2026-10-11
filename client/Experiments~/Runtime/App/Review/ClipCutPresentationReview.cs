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
    public static class ClipCutPresentationReview
    {
        private const int Seed = 20261007;
        [Serializable] private sealed class Receipt
        {
            public bool pass;
            public string revision="visual-editing-bench-v1-post-cut-reveal";
            public System.Collections.Generic.List<string> imageWitnesses=new System.Collections.Generic.List<string>();
            public int score, cuts, clean;
            public double wallSeconds, pausedSeconds;
            public string failure;
            public List<string> checks = new List<string>(), captures = new List<string>();
            public string limits = "Native local generated storyboard art, measured atlas crop, hidden-before-Cut semantic labels, layout and callback review. Images are original fictional scenes based on credited Snow/Rain references. Main scripted run disables OS focus observation and separately invokes real focus/pause handlers. Programmatic NavigationSubmit invokes actual buttons; physical Android touches/background delivery, fun/retention and paid routing remain unverified. No API, mic, real account action or payout.";
        }
        public static async Task Run(PhoneShell phone, PracticeJobsApp practice, string directory, CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Path.IsPathRooted(directory)) directory = Path.Combine(Application.persistentDataPath, directory);
#endif
            directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner); timeout.CancelAfter(TimeSpan.FromSeconds(80));
            var ct = timeout.Token; var receipt = new Receipt(); Task<ClipCutResult> play = null;
            try
            {
                Need(practice != null, "Practice hub missing");
                int starts = practice.RoundsStarted;
                phone.Open("jobs"); await Frames(4, ct);
                var page = UiRoot.Instance.PhoneLayer.Q<ScrollView>("practice-jobs");
                var entry = page?.Q<Button>("practice-play-clip"); Need(entry != null, "Ninth practice card missing");
                page.ScrollTo(entry); await Frames(4, ct);
                Within(entry.worldBound, page.contentViewport.worldBound, "Practice entry outside its actual viewport");
                await Capture(directory, "practice-entry", receipt, ct);
                await Activate(page, "practice-play-clip", ct);
                Need(practice.IsRunning && practice.RoundsStarted == starts + 1, "Actual ninth-card callback did not enter shared flight");
                var live = UiRoot.Instance.ModalLayer.Q("clip-cut-game"); Need(live != null, "Practice callback did not mount actual game");
                var pausing = live.Q("clip-paused");
                await Activate(live, pausing.resolvedStyle.display != DisplayStyle.None ? "clip-paused-end" : "clip-end", ct);
                await Until(() => !practice.IsRunning, 3, ct);
                Need(UiRoot.Instance.PhoneLayer.Q("practice-jobs") != null && practice.Result == null, "Cancelled hub attempt produced a score or failed to reopen Jobs");
                phone.Close(); await Frames(3, ct);
                receipt.checks.Add("PASS actual ninth Jobs card, shared single-flight ownership, unscripted game entry and End practice; cancellation reopens Jobs with no result.");

                using (var pre = new CancellationTokenSource())
                { pre.Cancel(); bool denied = false; try { _ = ClipCut.PlayReview(pre.Token, Seed); } catch (OperationCanceledException) { denied = true; } Need(denied, "Pre-cancelled play mounted"); }
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var operation = ClipCut.PlayReview(attempt.Token, Seed); await Frames(3, ct);
                    UiRoot.Instance.ModalLayer.Q("clip-cut-game").RemoveFromHierarchy(); await Cancelled(operation);
                }
                using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var operation = ClipCut.PlayReview(attempt.Token, Seed); await Frames(3, ct);
                    attempt.Cancel(); await Frames(3, ct); await Cancelled(operation);
                }
                receipt.checks.Add("PASS pre-cancel, attached cancellation and root detach return no score; subsequent attempts mount.");

                double started = Time.realtimeSinceStartupAsDouble;
                play = ClipCut.PlayReview(ct, Seed); bool concurrent = false;
                try { _ = ClipCut.PlayReview(ct, Seed); } catch (InvalidOperationException) { concurrent = true; }
                Need(concurrent, "Concurrent round accepted"); await Frames(4, ct);
                var root = UiRoot.Instance.ModalLayer.Q("clip-cut-game"); Inspect(root);
                var child = new VisualElement(); root.Add(child); child.RemoveFromHierarchy();
                Need(!play.IsCompleted, "Child detach cancelled whole game");
                double wallPause = Time.realtimeSinceStartupAsDouble, elapsedPause = ClipCut.ReviewElapsed;
                await Activate(root, "clip-pause", ct); double pausedAt = ClipCut.ReviewElapsed;
                await Wait(.35, ct); Need(ClipCut.ReviewElapsed == pausedAt && !root.Q<Button>("clip-frame-1").enabledInHierarchy, "Paused clock or inputs remained active");
                Inspect(root); await Capture(directory, "paused", receipt, ct); await Activate(root, "clip-resume", ct);
                ClipCut.ReviewFocus(false); pausedAt = ClipCut.ReviewElapsed; await Wait(.25, ct);
                ClipCut.ReviewFocus(true); Need(ClipCut.ReviewElapsed == pausedAt && Visible(root.Q("clip-paused")), "Focus return resumed without gesture");
                await Activate(root, "clip-resume", ct); ClipCut.ReviewPause(true); pausedAt = ClipCut.ReviewElapsed; await Wait(.25, ct);
                ClipCut.ReviewPause(false); Need(ClipCut.ReviewElapsed == pausedAt && Visible(root.Q("clip-paused")), "App resume silently advanced practice");
                await Activate(root, "clip-resume", ct);
                receipt.pausedSeconds = Time.realtimeSinceStartupAsDouble - wallPause - (ClipCut.ReviewElapsed - elapsedPause);
                receipt.checks.Add("PASS real Pause/Resume callbacks, focus and app-pause handlers, frozen time/input and explicit Resume.");

                var deck=new ClipCutRound(0,Seed);var seen=new HashSet<int>();
                for(int reelIndex=0;reelIndex<5;reelIndex++)
                {
                    var reel=deck.Current;Need(seen.Add(reel.Story),"Repeated authored reel");
                    Need(root.Q<Label>(className:"ct-clip-reel-title").text==ClipCutStrings.Title(reel),"Native reel differs from seeded sequence");
                    Inspect(root);InspectStory(root,reel,false,receipt);await Capture(directory,"reel-"+reelIndex+"-open",receipt,ct);
                    if(reelIndex==0)
                    {
                        await Move(root,false,2,ct);await Move(root,true,6,ct);
                        Need(root.Q<Label>(className:"ct-clip-range").text==ClipCutStrings.F("range",7,7),"Crossed IN did not carry OUT");
                        Need(root.Q<Button>("clip-frame-6").Q<Label>(className:"ct-clip-marker").text==ClipCutStrings.T("both"),"Collapsed interval lacks IN/OUT marker");
                        Inspect(root);await Capture(directory,"handles-crossed",receipt,ct);
                        await Move(root,false,1,ct);await Activate(root,"clip-reset",ct);
                        Need(root.Q<Label>(className:"ct-clip-range").text==ClipCutStrings.F("range",1,8),"Reset did not restore source");
                    }
                    int first=Enumerable.Range(0,8).Single(i=>reel.Kind(i)==ClipBeatKind.Setup),last=Enumerable.Range(0,8).Single(i=>reel.Kind(i)==ClipBeatKind.Payoff);
                    await Move(root,true,first,ct);await Move(root,false,last,ct);
                    if(reelIndex==0)
                    {
                        await Activate(root,"clip-preview",ct);string before=root.Q<Label>(className:"ct-clip-preview-frame").text;await Wait(.75,ct);
                        Need(root.Q<Label>(className:"ct-clip-preview-frame").text!=before,"Storyboard preview did not advance");
                        var currentPreview=root.Query<Button>(className:"ct-clip-previewing").ToList();Need(currentPreview.Count==1,"Preview lacks one visible cursor");
                        int previewIndex=int.Parse(currentPreview[0].name.Substring("clip-frame-".Length),System.Globalization.CultureInfo.InvariantCulture);
                        Need(previewIndex>=first&&previewIndex<=last&&root.Q<Label>(className:"ct-clip-preview-copy").text==ClipCutStrings.Frame(reel,previewIndex),"Preview left the selected interval or displayed the wrong frame");
                        Inspect(root);await Capture(directory,"storyboard-playing",receipt,ct);
                        double pauseWall=Time.realtimeSinceStartupAsDouble,pauseElapsed=ClipCut.ReviewElapsed;
                        await Activate(root,"clip-pause",ct);before=root.Q<Label>(className:"ct-clip-preview-frame").text;await Wait(.25,ct);
                        Need(root.Q<Label>(className:"ct-clip-preview-frame").text==before,"Paused preview advanced");
                        await Activate(root,"clip-resume",ct);receipt.pausedSeconds+=Time.realtimeSinceStartupAsDouble-pauseWall-(ClipCut.ReviewElapsed-pauseElapsed);
                        await Activate(root,"clip-preview",ct);Need(root.Query<Button>(className:"ct-clip-previewing").ToList().Count==0,"Stop preview did not clear playback cursor");
                    }
                    if(reelIndex==2){if(last<7)await Move(root,false,last+1,ct);else await Move(root,true,first-1,ct);}
                    Inspect(root);await Capture(directory,"reel-"+reelIndex+"-trimmed",receipt,ct);
                    while(ClipCut.ReviewElapsed<(reelIndex+1)*5)await Awaitable.NextFrameAsync(ct);
                    double submitted=Time.realtimeSinceStartupAsDouble;await Activate(root,"clip-cut",ct);
                    if(reelIndex==4){Need(play.IsCompleted&&Time.realtimeSinceStartupAsDouble-submitted<.5,"Fifth Cut did not finish immediately");break;}
                    Need(root.Query<Button>(className:"ct-clip-frame").ToList().All(b=>!b.enabledInHierarchy)&&!Visible(root.Q("clip-cut")),"Submitted reel remained editable");
                    string expected=reelIndex==2?ClipCutStrings.T("wrong")+" · "+ClipCutStrings.T("extra"):ClipCutStrings.T("clean");
                    Need(root.Q<Label>(className:"ct-clip-feedback").text==expected,"Cut outcome or extra-frame explanation missing");
                    Need(root.Q<Label>(className:"ct-clip-instruction").text==ClipCutStrings.F("answer",first+1,last+1),"Submitted reel does not show concise bounds");
                    Inspect(root);InspectStory(root,reel,true,receipt);await Capture(directory,"reel-"+reelIndex+"-cut",receipt,ct);
                    await Activate(root,"clip-next",ct);deck.Submit(reelIndex,0);deck.Next(reelIndex,0);
                }
                Need(play.IsCompleted&&seen.Count==5,"All reels did not settle");var result=await play;
                receipt.wallSeconds=Time.realtimeSinceStartupAsDouble-started;receipt.score=result.Score;receipt.cuts=result.Cuts;receipt.clean=result.Clean;
                Need(result.Score==80&&result.Cuts==5&&result.Clean==4,"Finite trim score differs");
                Need(receipt.wallSeconds-receipt.pausedSeconds>=25&&receipt.wallSeconds-receipt.pausedSeconds<38&&root.parent==null,"Early completion or cleanup failed");
                receipt.checks.Add("PASS all five original reels, real IN/OUT handles and frame selection, crossing/reset, advancing/paused/stopped storyboard preview, one extra-frame error, 80/100 and immediate fifth Cut after about 25 active seconds.");
                var resultRoot=Result(result,owner);await Frames(4,ct);Inspect(resultRoot);await Capture(directory,"result",receipt,ct);
                await Activate(resultRoot,"clip-again",ct);await Frames(3,ct);var replay=UiRoot.Instance.ModalLayer.Q("clip-cut-game");Need(replay!=null,"Human-controlled replay did not mount");
                replay.RemoveFromHierarchy();await Frames(4,ct);Need(UiRoot.Instance.ModalLayer.Q("clip-result")!=null,"Cancelled replay lost prior result");
                receipt.checks.Add("PASS unscripted immediate replay and cancellation without a new score or external post.");
                receipt.pass=true;Log.Info("clip-cut-review","PASS original local storyboards; nothing recorded, acquired or posted.");
            }
            catch(Exception error)
            {
                receipt.failure=error.Message;try{await Capture(directory,"failure",receipt,CancellationToken.None);}catch{}
                UiRoot.Instance?.ModalLayer.Q("clip-cut-game")?.RemoveFromHierarchy();if(play!=null){try{await play;}catch(OperationCanceledException){}}
                throw;
            }
            finally{File.WriteAllText(Path.Combine(directory,"clip-cut-review.json"),JsonUtility.ToJson(receipt,true));}
        }
        private static void InspectStory(VisualElement root,ClipReel reel,bool revealed,Receipt receipt)
        {
            var atlasNames=new HashSet<string>();var rects=new List<Rect>();
            for(int i=0;i<8;i++)
            {
                var button=root.Q<Button>("clip-frame-"+i);var image=button.Q<Image>("clip-thumbnail-"+i);
                Need(image!=null&&image.image is Texture2D&&image.scaleMode==ScaleMode.ScaleToFit,"Missing or stretched original frame "+i);
                Need(image.image.name=="story-"+reel.Story,"Image belongs to a different reel");
                var r=image.sourceRect;Need(r.width>100&&r.height>100&&r.xMin>=0&&r.yMin>=0&&r.xMax<=image.image.width&&r.yMax<=image.image.height,"Invalid source rectangle");
                foreach(var previous in rects)Need(!previous.Overlaps(r),"Storyboard rectangles overlap");rects.Add(r);atlasNames.Add(image.image.name);
                Need(Visible(button.Q<Label>(className:"ct-clip-frame-kind"))==revealed,"Answer label revealed at the wrong time");
                Need(button.Q<Label>(className:"ct-clip-frame-copy").text==ClipCutStrings.Frame(reel,i),"Localized narrative caption missing");
                Need(button.tooltip==ClipCutStrings.F("frameCaption",i+1,ClipCutStrings.Frame(reel,i)),"Accessible narrative tooltip missing");
                Within(image.worldBound,button.LocalToWorld(button.contentRect),"Thumbnail escapes hit region");
                receipt.imageWitnesses.Add(reel.Story+":"+i+" rect="+r+" texture="+image.image.width+"x"+image.image.height+" reveal="+revealed);
            }
            Need(atlasNames.Count==1,"Film strip loaded unrelated atlases");
            var monitor=root.Q<Image>("clip-monitor");Need(monitor!=null&&monitor.image!=null&&monitor.scaleMode==ScaleMode.ScaleToFit,"Scene monitor missing or stretched");
            Need(rects.Contains(monitor.sourceRect),"Monitor frame is not one of the measured source shots");
            Need(monitor.worldBound.height>=200&&monitor.worldBound.width>=400,"Scene monitor is not readable at this layout");
            if(!revealed)Need(root.Q<Label>(className:"ct-clip-preview-frame").text==ClipCutStrings.F("previewNumber",rects.IndexOf(monitor.sourceRect)+1),"Monitor leaks semantic answer before Cut");
        }
        private static async Task Move(VisualElement root,bool start,int frame,CancellationToken ct)
        {
            var handle=root.Q<Button>(start?"clip-in":"clip-out");if(handle.enabledInHierarchy)await Activate(root,handle.name,ct);
            var target=root.Q<Button>("clip-frame-"+frame);if(target.enabledInHierarchy)await Activate(root,target.name,ct);
        }
        private static VisualElement Result(ClipCutResult result, CancellationToken owner)
        {
            var root = El.Div("ct-desk-overlay"); root.name = "clip-result"; bool busy = false;
            async void Again()
            {
                if (busy || owner.IsCancellationRequested || root.parent == null) return;
                busy = true; root.RemoveFromHierarchy(); using var attempt = CancellationTokenSource.CreateLinkedTokenSource(owner);
                try { var next = await ClipCut.Play(attempt.Token); if (!owner.IsCancellationRequested) Result(next, owner); }
                catch (OperationCanceledException) { if (!owner.IsCancellationRequested && UiRoot.Instance != null) Result(result, owner); }
                catch (Exception error) { Log.Warn("clip-practice", "Replay interrupted: " + error.GetType().Name); if (!owner.IsCancellationRequested && UiRoot.Instance != null) Result(result, owner); }
                finally { attempt.Cancel(); busy = false; }
            }
            var again = El.Btn(ClipCutStrings.T("again"), Again, "ct-btn ct-desk-next"); again.name = "clip-again";
            var back = El.Btn(ClipCutStrings.T("back"), () => root.RemoveFromHierarchy(), "ct-btn ct-desk-next"); back.name = "clip-back";
            root.Add(El.Div("ct-desk-panel ct-shift-panel", ModDeskCopy(ClipCutStrings.T("practice"), "ct-desk-eyebrow"),
                ModDeskCopy(ClipCutStrings.T("complete"), "ct-desk-title"), ModDeskCopy(ClipCutStrings.F("score", result.Score), "ct-shift-pay"),
                ModDeskCopy(ClipCutStrings.Summary(result), "ct-shift-copy"), again, back));
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
            var strip=root.Q("clip-strip");
            if(strip!=null)
            {
                var frames=strip.Children().OfType<Button>().ToArray();Need(frames.Length==8,"Source lacks eight storyboard frames");
                for(int i=0;i<8;i++)
                {
                    Within(frames[i].worldBound,strip.worldBound,"Frame outside film strip");
                    if(i>0)Need(frames[i-1].worldBound.xMax<=frames[i].worldBound.xMin+2,"Frame touch regions overlap");
                }
                Need(root.Q<Button>("clip-in").worldBound.yMin>=strip.worldBound.yMax-2,"Handles overlap source frames");
            }
            var panel=root.Q(className:"ct-clip-panel");
            foreach (var button in root.Query<Button>().ToList()) if (Visible(button)) { Touch(button); Within(button.worldBound,root.worldBound,"Control outside viewport: "+button.name); if(panel!=null)Within(button.worldBound,panel.LocalToWorld(panel.contentRect),"Control outside panel: "+button.name); }
            foreach (var label in root.Query<Label>().ToList())
            {
                if (!Visible(label) || string.IsNullOrEmpty(label.text)) continue;
                var measured = label.MeasureTextSize(label.text,label.contentRect.width,VisualElement.MeasureMode.AtMost,0,VisualElement.MeasureMode.Undefined);
                Need(measured.x <= label.contentRect.width + 2 && measured.y <= label.contentRect.height + 2,
                    "Clipped text '"+label.text+"' content="+label.contentRect+" measured="+measured);
                Within(label.worldBound,root.worldBound,"Text outside viewport: "+label.text);
                if(panel!=null)Within(label.worldBound,panel.LocalToWorld(panel.contentRect),"Text outside panel: "+label.text);
                var button=label.GetFirstAncestorOfType<Button>();
                if(button!=null)Within(label.worldBound,button.LocalToWorld(button.contentRect),"Storyboard text outside its hit region");
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
        private static void Need(bool condition,string message) { if(!condition)throw new InvalidOperationException("Clip Cut review: "+message); }
    }
}
#endif
