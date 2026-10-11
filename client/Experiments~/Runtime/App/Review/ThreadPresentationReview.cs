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
    public static class ThreadPresentationReview
    {
        private const int Seed = 20261006, Duration = 50;
        [Serializable] private sealed class Receipt
        {
            public bool pass;
            public int score;
            public double elapsedSeconds;
            public string failure;
            public string limits = "Native practice UI and real NavigationSubmit callbacks; no external posting/economy calls. Physical touch/drag/IME, server settlement and live players are not certified.";
            public List<string> checks = new List<string>(), captures = new List<string>();
        }
        private static MiniGameDef Definition() => new MiniGameDef { id="review_thread", careerId="review_only", durationSec=Duration, maxScore=50 };
        public static async Task Run(string directory, CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Path.IsPathRooted(directory)) directory = Path.Combine(Application.persistentDataPath, directory);
#endif
            directory=Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(owner); timeout.CancelAfter(TimeSpan.FromSeconds(75));
            var ct=timeout.Token; var receipt=new Receipt(); VisualElement desk=null;
            try
            {
                await CheckCancellation(ct); receipt.checks.Add("PASS pre-cancelled, detached and cancelled attempts return no score; subsequent attempt starts.");
                double started=Time.realtimeSinceStartupAsDouble;
                var play=ThreadWorkshop.PlayPracticeReview(Definition(),ct,Seed);
                bool duplicateBlocked=false;
                // This factory must reject synchronously, before returning a task or mounting a second view.
                try { _ = ThreadWorkshop.PlayPracticeReview(Definition(),ct,Seed); }
                catch(InvalidOperationException) { duplicateBlocked=true; }
                Need(duplicateBlocked,"Second concurrent workshop accepted");
                var order=ThreadRound.Review(0,Duration,50,Seed);
                await Frames(4,ct); desk=UiRoot.Instance.ModalLayer.Q("thread-workshop");
                Need(desk!=null && desk.Q<Label>(className:"ct-desk-context").text==ThreadStrings.T("practice"),"Practice label missing");
                for(int i=0;i<ThreadRound.ThreadCount;i++)
                {
                    Need(!play.IsCompleted,"Workshop completed before full duration");
                    Need(desk.Q<Label>(className:"ct-thread-story").text==ThreadStrings.T(order.Current.Story+".title"),"Wrong story order");
                    Inspect(desk); CheckFirstViewport(desk); await Capture(directory,"draft-"+order.Current.Story,receipt,ct);
                    if(i==0)
                    {
                        await Activate(desk,"thread-piece-1",ct); await Activate(desk,"thread-undo",ct);
                        Need(desk.Q("thread-slot-0")==null,"Undo insertion retained a line");
                        foreach(int piece in new[]{1,2,0}) await Activate(desk,"thread-piece-"+piece,ct);
                        await Activate(desk,"thread-up",ct); await Activate(desk,"thread-up",ct);
                        await Activate(desk,"thread-down",ct); await Activate(desk,"thread-undo",ct);
                        await Activate(desk,"thread-slot-1",ct); await Activate(desk,"thread-remove",ct); await Activate(desk,"thread-undo",ct);
                        CheckCompose(desk,order.Current.Story,new[]{0,1,2});
                        receipt.checks.Add("PASS real add, undo-add, up/up/down, undo-reorder, row-select/remove/undo callbacks preserve exact prose order.");
                        await Capture(directory,"draft-edited",receipt,ct);
                    }
                    else foreach(int piece in (i==2?new[]{0,2,1}:new[]{0,1,2})) await Activate(desk,"thread-piece-"+piece,ct);
                    foreach(int piece in (i==2?new[]{0,2,1}:new[]{0,1,2})) order.Add(piece,i+1);
                    await Activate(desk,"thread-publish",ct);
                    Need(order.Publish(i+1)==(i!=2),"Bad narrative oracle");
                    Need(desk.Q<Label>(className:"ct-desk-tally").text==ThreadStrings.F("tally",order.Correct,5),"Wrong score tally");
                    Need(!desk.Q<Button>("thread-undo").enabledInHierarchy,"Filed draft can still be edited");
                    Inspect(desk); CheckFeedbackViewport(desk);
                    if(i==0 || i==2) await Capture(directory,i==2?"feedback-order-wrong":"feedback-order-right",receipt,ct);
                    await Activate(desk,"thread-next",ct); Need(order.Next(i+1),"Oracle next failed");
                }
                Need(!play.IsCompleted,"All drafts skipped remaining shift time");
                Inspect(desk); await Capture(directory,"drafts-filed",receipt,ct);
                while(!play.IsCompleted) await Awaitable.NextFrameAsync(ct);
                receipt.score=await play; receipt.elapsedSeconds=Time.realtimeSinceStartupAsDouble-started;
                Need(receipt.score==40 && receipt.elapsedSeconds>=Duration && receipt.elapsedSeconds<70,"Wrong score or duration");
                Need(desk.parent==null,"Completed workshop not detached");
                var result=PracticeResult(receipt.score,owner); await Frames(4,ct); Inspect(result);
                await Capture(directory,"practice-result",receipt,ct);
                receipt.pass=true;
                Log.Info("thread-review",$"PASS five drafts, actual edit/file callbacks,40/50, elapsed={receipt.elapsedSeconds:0.00}s, cancellation/one-flight checks; no posting/economy calls.");
            }
            catch(Exception error)
            {
                receipt.failure=error.Message;
                try { await Capture(directory,"failure",receipt,CancellationToken.None); } catch { }
                UiRoot.Instance?.ModalLayer.Q("thread-workshop")?.RemoveFromHierarchy(); throw;
            }
            finally { File.WriteAllText(Path.Combine(directory,"thread-review.json"),JsonUtility.ToJson(receipt,true)); }
        }
        [Serializable] private sealed class PoolReceipt
        {
            public bool pass;
            public int stories, currentStory = -1;
            public string failure;
            public string limits = "Thirty authored content previews using the actual normal-pool renderer. Each attempt is explicitly cancelled after text/input/layout checks; this does not shorten a live shift or certify server settlement. No economy calls or external posts. The separate full50second review remains required.";
            public List<string> checks = new List<string>(), captures = new List<string>();
        }
        public static async Task RunPool(string directory, CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Path.IsPathRooted(directory)) directory = Path.Combine(Application.persistentDataPath, directory);
#endif
            directory=Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(owner); timeout.CancelAfter(TimeSpan.FromSeconds(90));
            var ct=timeout.Token; var receipt=new PoolReceipt(); var images=new Receipt();
            var seeds=new Dictionary<int,int>();
            for(int seed=0;seed<4096 && seeds.Count<ThreadRound.StoryCount;seed++)
            {
                int story=new ThreadRound(0,Duration,50,seed).Current.Story;
                if(!seeds.ContainsKey(story))seeds[story]=seed;
            }
            Need(seeds.Count==ThreadRound.StoryCount,"Content probe did not find every normal-pool story");
            try
            {
                for(int story=0;story<ThreadRound.StoryCount;story++)
                {
                    receipt.currentStory=story;
                    using var attempt=CancellationTokenSource.CreateLinkedTokenSource(ct);
                    Task<int> play=null;
                    try
                    {
                        play=ThreadWorkshop.PlayPoolReview(Definition(),attempt.Token,seeds[story]);
                        await Frames(4,ct); var desk=UiRoot.Instance.ModalLayer.Q("thread-workshop");
                        Need(desk!=null && desk.Q<Label>(className:"ct-thread-story").text==ThreadStrings.T(story+".title"),"Wrong pool preview story");
                        Need(desk.Q<Label>(className:"ct-desk-context").text==ThreadStrings.T("practice"),"Pool preview not labeled practice");
                        Inspect(desk); CheckFirstViewport(desk);
                        await Capture(directory,"pool-"+story.ToString("00"),images,ct);
                        foreach(int piece in new[]{0,1,2})await Activate(desk,"thread-piece-"+piece,ct);
                        Inspect(desk); CheckCompose(desk,story,new[]{0,1,2}); CheckFirstViewport(desk);
                        if(story%5==0)await Capture(directory,"pool-composed-"+story.ToString("00"),images,ct);
                        await Activate(desk,"thread-publish",ct); Inspect(desk); CheckFeedbackViewport(desk);
                        Need(desk.Q<Label>(className:"ct-thread-feedback-body").text==ThreadStrings.T(story+".reason"),"Wrong authored feedback");
                        Need(!play.IsCompleted,"Content probe unexpectedly consumed full shift");
                    }
                    catch
                    {
                        try { await Capture(directory,"pool-failure-"+story,images,CancellationToken.None); } catch { }
                        throw;
                    }
                    finally
                    {
                        attempt.Cancel();
                        if(play!=null)
                        {
                            try { await play; throw new InvalidOperationException("Content probe returned a score instead of cancellation"); }
                            catch(OperationCanceledException) { }
                        }
                    }
                    Need(UiRoot.Instance.ModalLayer.Q("thread-workshop")==null,"Pool preview retained overlay");
                    receipt.stories++;
                }
                receipt.checks.Add("PASS thirty initial fragment/slot viewports, complete composed prose and editorial-feedback text bounds; actual add/publish callbacks; every preview cancelled with no score.");
                receipt.pass=true;
                Log.Info("thread-pool-review","PASS30original story layouts and real composition callbacks; previews cancelled, no economic operations.");
            }
            catch(Exception error)
            {
                receipt.failure=error.Message;
                if(!images.captures.Contains("pool-failure-"+receipt.currentStory+".png"))
                    try { await Capture(directory,"pool-failure-"+receipt.currentStory,images,CancellationToken.None); } catch { }
                throw;
            }
            finally
            {
                receipt.captures=images.captures;
                File.WriteAllText(Path.Combine(directory,"thread-pool-review.json"),JsonUtility.ToJson(receipt,true));
            }
        }
        private static async Task CheckCancellation(CancellationToken owner)
        {
            using(var cancelled=new CancellationTokenSource())
            {
                cancelled.Cancel(); bool rejected=false;
                // Pre-cancellation is also a synchronous factory contract, not an awaited layout check.
                try { _ = ThreadWorkshop.PlayPracticeReview(Definition(),cancelled.Token,Seed); } catch(OperationCanceledException) { rejected=true; }
                Need(rejected,"Precancelled practice mounted");
            }
            using(var attempt=CancellationTokenSource.CreateLinkedTokenSource(owner))
            {
                var play=ThreadWorkshop.PlayPracticeReview(Definition(),attempt.Token,Seed); await Frames(3,owner);
                UiRoot.Instance.ModalLayer.Q("thread-workshop").RemoveFromHierarchy(); await Frames(2,owner);
                Need(play.IsCanceled,"Detached practice returned a score or remained pending");
            }
            using(var attempt=CancellationTokenSource.CreateLinkedTokenSource(owner))
            {
                var play=ThreadWorkshop.PlayPracticeReview(Definition(),attempt.Token,Seed); await Frames(3,owner); attempt.Cancel(); await Frames(3,owner);
                Need(play.IsCanceled && UiRoot.Instance.ModalLayer.Q("thread-workshop")==null,"Cancelled practice retained overlay or score");
            }
        }
        private static VisualElement PracticeResult(int? score,CancellationToken owner,bool interrupted=false)
        {
            var root=El.Div("ct-desk-overlay"); root.name="thread-practice-result"; bool busy=false;
            async void Again()
            {
                if(busy || owner.IsCancellationRequested)return; busy=true;
                using var attempt=CancellationTokenSource.CreateLinkedTokenSource(owner); root.RemoveFromHierarchy();
                try { int result=await ThreadWorkshop.Play(Definition(),attempt.Token,true); if(!owner.IsCancellationRequested)PracticeResult(result,owner); }
                catch(OperationCanceledException) { }
                catch(Exception error)
                {
                    attempt.Cancel(); Log.Warn("thread-practice","Replay interrupted: "+error.GetType().Name);
                    if(!owner.IsCancellationRequested && UiRoot.Instance!=null)PracticeResult(null,owner,true);
                }
                finally { attempt.Cancel(); busy=false; }
            }
            var again=El.Btn(ThreadStrings.T("again"),Again,"ct-btn ct-desk-next"); again.name="thread-practice-again";
            var back=El.Btn(ThreadStrings.T("back"),()=>root.RemoveFromHierarchy(),"ct-btn ct-desk-next"); back.name="thread-practice-back";
            root.Add(El.Div("ct-desk-panel ct-shift-panel",Plain(ThreadStrings.T("practice"),"ct-desk-eyebrow"),
                Plain(ThreadStrings.T(interrupted?"interrupted":"done"),"ct-shift-heading"),
                Plain(interrupted?ThreadStrings.T("interruptedDetail"):ThreadStrings.F("result",score.GetValueOrDefault(),50),"ct-shift-copy"),again,back));
            var sheet=Resources.Load<StyleSheet>("UI/mod-desk"); if(sheet!=null)root.styleSheets.Add(sheet);
            UiRoot.Instance.ModalLayer.Add(root); return root;
        }
        private static Label Plain(string text,string classes) { var label=El.Text(text,classes); label.enableRichText=false; return label; }
        private static void CheckFirstViewport(VisualElement root)
        {
            foreach(string className in new[]{"ct-thread-tray","ct-thread-compose"})
            {
                var scroll=root.Q<ScrollView>(className:className); var area=scroll.contentViewport.worldBound;
                var pieces=className=="ct-thread-tray" ? root.Q(className:"ct-thread-fragments").Children() : root.Q(className:"ct-thread-slots").Children();
                foreach(var piece in pieces)
                {
                    var b=piece.worldBound;
                    Need(b.xMin>=area.xMin-2 && b.xMax<=area.xMax+2 && b.yMin>=area.yMin-2 && b.yMax<=area.yMax+2,
                        "Draft piece not initially visible: "+className+" bounds="+b+" viewport="+area);
                }
            }
        }
        private static void CheckCompose(VisualElement root,int story,int[] pieces)
        {
            for(int i=0;i<3;i++)Need(root.Q("thread-slot-"+i).Q<Label>(className:"ct-thread-slot-copy").text==ThreadStrings.Fragment(story,pieces[i]),"Editing changed/misordered prose");
        }
        private static async Task Activate(VisualElement root,string name,CancellationToken ct)
        {
            var button=root.Q<Button>(name); Need(button!=null && button.enabledInHierarchy,"Missing/disabled control "+name);
            var scroll=button.GetFirstAncestorOfType<ScrollView>();
            if(scroll!=null) { scroll.ScrollTo(button); await Frames(3,ct); }
            var area=scroll!=null?scroll.contentViewport.worldBound:root.worldBound;
            var b=button.worldBound; Need(b.width>=124 && b.height>=124 && b.xMin>=area.xMin-2 && b.xMax<=area.xMax+2 && b.yMin>=area.yMin-2 && b.yMax<=area.yMax+2,"Clipped touch target "+name+":"+b+" viewport="+area);
            using(var e=NavigationSubmitEvent.GetPooled()) { e.target=button; button.SendEvent(e); }
            await Frames(3,ct);
        }
        private static void CheckFeedbackViewport(VisualElement root)
        {
            var feedback=root.Q(className:"ct-thread-feedback");
            var scroll=feedback?.GetFirstAncestorOfType<ScrollView>();
            Need(feedback!=null && scroll!=null && Visible(feedback),"Published feedback missing");
            var area=scroll.contentViewport.worldBound;
            foreach(var label in feedback.Query<Label>().ToList())
            {
                var b=label.worldBound;
                Need(b.width>0 && b.height>0 && b.xMin>=area.xMin-2 && b.xMax<=area.xMax+2 &&
                    b.yMin>=area.yMin-2 && b.yMax<=area.yMax+2,
                    "Published feedback outside composer viewport: "+label.text+" bounds="+b+" viewport="+area);
            }
        }
        private static void Inspect(VisualElement root)
        {
            foreach(var button in root.Query<Button>().ToList())
            {
                if(!Visible(button))continue;
                Need(button.worldBound.width>=124 && button.worldBound.height>=124,"Undersized control "+button.name+":"+button.worldBound);
                if(!string.IsNullOrEmpty(button.text))
                {
                    var size=button.MeasureTextSize(button.text,button.contentRect.width,VisualElement.MeasureMode.Exactly,0,VisualElement.MeasureMode.Undefined);
                    Need(size.y<=button.contentRect.height+2,"Clipped button "+button.name+":"+button.text);
                }
            }
            foreach(var label in root.Query<Label>().ToList())
            {
                if(!Visible(label) || string.IsNullOrEmpty(label.text))continue;
                var size=label.MeasureTextSize(label.text,label.contentRect.width,VisualElement.MeasureMode.AtMost,0,VisualElement.MeasureMode.Undefined);
                Need(size.x<=label.contentRect.width+2 && size.y<=label.contentRect.height+2,"Clipped label "+label.text+" content="+label.contentRect+" measured="+size);
            }
        }
        private static bool Visible(VisualElement element)
        {
            for(var p=element;p!=null;p=p.parent)if(p.resolvedStyle.display==DisplayStyle.None || p.resolvedStyle.visibility==Visibility.Hidden)return false;
            return element.panel!=null;
        }
        private static async Task Frames(int count,CancellationToken ct) { for(int i=0;i<count;i++)await Awaitable.NextFrameAsync(ct); }
        private static async Task Capture(string directory,string name,Receipt receipt,CancellationToken ct)
        {
            await Awaitable.EndOfFrameAsync(ct); var texture=ScreenCapture.CaptureScreenshotAsTexture(); Need(texture!=null,"Native capture failed");
            try { File.WriteAllBytes(Path.Combine(directory,name+".png"),texture.EncodeToPNG()); } finally { UnityEngine.Object.Destroy(texture); }
            receipt.captures.Add(name+".png");
        }
        private static void Need(bool value,string message) { if(!value)throw new InvalidOperationException("Thread review: "+message); }
    }
}
#endif
