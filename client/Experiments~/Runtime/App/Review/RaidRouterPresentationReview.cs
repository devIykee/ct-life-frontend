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
    public static class RaidRouterPresentationReview
    {
        private const int Seed=20261007;
        [Serializable] private sealed class Receipt
        {
            public bool pass;public int score,raidsStopped,chatsBlocked,bestCombo,closeCalls;
            public double elapsedSeconds,pauseSeconds;
            public string failure;
            public string limits="Native local practice/UI callbacks. Scripted review disables automatic OS focus observation and separately invokes the real focus-loss handler. Actual Android touch/background behavior, retention and paid server routing are not certified. No API, microphone or payout.";
            public List<string> checks=new List<string>(),captures=new List<string>();
        }
        public static async Task Run(string directory,CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!Path.IsPathRooted(directory))directory=Path.Combine(Application.persistentDataPath,directory);
#endif
            directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(owner);timeout.CancelAfter(TimeSpan.FromSeconds(75));
            var ct=timeout.Token;var receipt=new Receipt();Task<RaidRouterResult> play=null;
            try
            {
                using(var pre=new CancellationTokenSource())
                {pre.Cancel();bool rejected=false;try{_=RaidRouter.PlayReview(pre.Token,Seed);}catch(OperationCanceledException){rejected=true;}Need(rejected,"Pre-cancelled round mounted");}
                using(var attempt=CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var cancelled=RaidRouter.PlayReview(attempt.Token,Seed);await Frames(3,ct);
                    UiRoot.Instance.ModalLayer.Q("raid-router").RemoveFromHierarchy();await Cancelled(cancelled);
                }
                using(var attempt=CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var cancelled=RaidRouter.PlayReview(attempt.Token,Seed);await Frames(3,ct);attempt.Cancel();await Frames(3,ct);await Cancelled(cancelled);
                }
                receipt.checks.Add("PASS synchronous pre-cancellation, root detach and token cancellation return no score; subsequent attempt mounts.");
                double started=Time.realtimeSinceStartupAsDouble;play=RaidRouter.PlayReview(ct,Seed);
                bool duplicate=false;try{_=RaidRouter.PlayReview(ct,Seed);}catch(InvalidOperationException){duplicate=true;}
                Need(duplicate,"Concurrent round accepted");await Frames(4,ct);
                var desk=UiRoot.Instance.ModalLayer.Q("raid-router");Inspect(desk);await Capture(directory,"opening",receipt,ct);
                double clockBeforePause=RaidRouter.ReviewElapsed,wallBeforePause=Time.realtimeSinceStartupAsDouble;
                await Activate(desk,"router-pause",ct);double pausedAt=RaidRouter.ReviewElapsed;
                string timer=desk.Q<Label>(className:"ct-desk-timer").text;
                await WaitSeconds(.5,ct);Need(RaidRouter.ReviewElapsed==pausedAt&&desk.Q<Label>(className:"ct-desk-timer").text==timer,"Pause advanced practice clock");
                Need(!desk.Q<Button>("router-lane-0").enabledInHierarchy,"Paused lane remains interactive");
                await Capture(directory,"paused",receipt,ct);
                await Activate(desk,"router-resume",ct);RaidRouter.ReviewFocus(false);double lostAt=RaidRouter.ReviewElapsed;
                await WaitSeconds(.5,ct);Need(RaidRouter.ReviewElapsed==lostAt,"Focus loss did not pause");RaidRouter.ReviewFocus(true);
                Need(desk.Q("router-paused").resolvedStyle.display!=DisplayStyle.None,"Focus gain silently resumed clock");
                await Activate(desk,"router-resume",ct);
                receipt.pauseSeconds=(Time.realtimeSinceStartupAsDouble-wallBeforePause)-(RaidRouter.ReviewElapsed-clockBeforePause);receipt.checks.Add("PASS real Pause/Resume controls, frozen timer/input, focus-loss handler, explicit resume on return.");
                var deck=new RaidRouterRound(0,Seed);int next=0,capturedWave=0;bool capturedShield=false;var firstRaid=deck.Packets.First(packet=>packet.IsRaid&&packet.Index!=5);
                while(!play.IsCompleted)
                {
                    ct.ThrowIfCancellationRequested();double elapsed=RaidRouter.ReviewElapsed;
                    if(next<deck.Packets.Count&&elapsed>=deck.Packets[next].Arrival-.3)
                    {
                        var packet=deck.Packets[next];bool block=packet.IsRaid;if(next==5)block=!block;
                        await Activate(desk,block?"router-lane-"+packet.Lane:"router-park",ct);next++;
                    }
                    if(!capturedShield&&next==firstRaid.Index+1&&elapsed>=firstRaid.Arrival+.05)
                    {
                        Inspect(desk);var activeShield=desk.Q("router-shield-"+firstRaid.Lane);
                        Need(activeShield!=null&&Visible(activeShield),"Selected lane has no visible physical shield");
                        await Capture(directory,"shield-impact",receipt,ct);capturedShield=true;
                    }
                    int captureWave=elapsed>=35?3:elapsed>=23?2:elapsed>=7?1:0;
                    if(captureWave>capturedWave)
                    {Inspect(desk);await Capture(directory,"wave-"+captureWave,receipt,ct);capturedWave=captureWave;}
                    await Awaitable.NextFrameAsync(ct);
                }
                var result=await play;receipt.elapsedSeconds=Time.realtimeSinceStartupAsDouble-started;
                receipt.score=result.Score;receipt.raidsStopped=result.RaidsStopped;receipt.chatsBlocked=result.ChatsBlocked;
                receipt.bestCombo=result.BestCombo;receipt.closeCalls=result.CloseCalls;
                Need(result.Score==92&&receipt.elapsedSeconds>=46&&receipt.elapsedSeconds<65,"Wrong score or active duration");
                Need(next==24&&desk.parent==null,"Round ended before finite packets or retained overlay");
                receipt.checks.Add("PASS24 actual shield/park callbacks through full45 active seconds; one intentional mistake gives92/100.");
                var resultRoot=Result(result,owner);await Frames(4,ct);Inspect(resultRoot);await Capture(directory,"result",receipt,ct);
                await Activate(resultRoot,"router-again",ct);await Frames(3,ct);
                var replay=UiRoot.Instance.ModalLayer.Q("raid-router");Need(replay!=null,"Replay callback did not launch unscripted practice");
                // A normal replay may start paused when the Editor is unfocused; ending is a root-owned action.
                replay.RemoveFromHierarchy();await Frames(4,ct);
                Need(UiRoot.Instance.ModalLayer.Q("router-result")!=null,"Cancelled replay lost its previous completed result");
                receipt.checks.Add("PASS immediate unscripted Play again callback; detached replay returns no new score and restores last completed result.");
                receipt.pass=true;Log.Info("raid-router-review","PASS local45second arcade/input/pause/replay checks; no API/payout.");
            }
            catch(Exception error)
            {
                receipt.failure=error.Message;try{await Capture(directory,"failure",receipt,CancellationToken.None);}catch{}
                UiRoot.Instance?.ModalLayer.Q("raid-router")?.RemoveFromHierarchy();
                if(play!=null){try{await play;}catch(OperationCanceledException){}}
                throw;
            }
            finally{File.WriteAllText(Path.Combine(directory,"raid-router-review.json"),JsonUtility.ToJson(receipt,true));}
        }
        private static VisualElement Result(RaidRouterResult result,CancellationToken owner)
        {
            var root=El.Div("ct-desk-overlay");root.name="router-result";bool busy=false;
            async void Again()
            {
                if(busy||owner.IsCancellationRequested||root.parent==null)return;busy=true;root.RemoveFromHierarchy();
                using var attempt=CancellationTokenSource.CreateLinkedTokenSource(owner);
                try{var next=await RaidRouter.Play(attempt.Token);if(!owner.IsCancellationRequested)Result(next,owner);}
                catch(OperationCanceledException){if(!owner.IsCancellationRequested&&UiRoot.Instance!=null)Result(result,owner);}
                catch(Exception error){Log.Warn("router-practice","Replay interrupted: "+error.GetType().Name);if(!owner.IsCancellationRequested&&UiRoot.Instance!=null)Result(result,owner);}
                finally{attempt.Cancel();busy=false;}
            }
            var again=El.Btn(RaidRouterStrings.T("again"),Again,"ct-btn ct-desk-next");again.name="router-again";
            var back=El.Btn(RaidRouterStrings.T("back"),()=>root.RemoveFromHierarchy(),"ct-btn ct-desk-next");back.name="router-back";
            root.Add(El.Div("ct-desk-panel ct-shift-panel",Plain(RaidRouterStrings.T("practice"),"ct-desk-eyebrow"),
                Plain(RaidRouterStrings.T("done"),"ct-shift-heading"),Plain(RaidRouterStrings.F("score",result.Score),"ct-shift-pay"),
                Plain(RaidRouterStrings.Summary(result),"ct-shift-copy"),again,back));
            var sheet=Resources.Load<StyleSheet>("UI/mod-desk");if(sheet!=null)root.styleSheets.Add(sheet);
            UiRoot.Instance.ModalLayer.Add(root);return root;
        }
        private static Label Plain(string text,string classes){var label=El.Text(text,classes);label.enableRichText=false;return label;}
        private static async Task Activate(VisualElement root,string name,CancellationToken ct)
        {
            var button=root.Q<Button>(name);Need(button!=null&&button.enabledInHierarchy&&Visible(button),"Unavailable control "+name);
            Touch(button);using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=button;button.SendEvent(submit);}await Frames(2,ct);
        }
        private static void Touch(Button button)
        {
            float scale=Screen.height/UiRoot.Instance.Root.worldBound.height;
            Need(button.resolvedStyle.minHeight.value>=124&&button.worldBound.height*scale+.01f>=Mathf.Floor(124*scale)&&button.worldBound.width*scale>=Mathf.Floor(124*scale),
                "Undersized control "+button.name+" bounds="+button.worldBound+" scale="+scale);
        }
        private static void Inspect(VisualElement root)
        {
            Need(root!=null&&root.panel!=null,"View missing");
            var board=root.Q<VisualElement>(className:"ct-router-board");
            var feedback=root.Q<Label>(className:"ct-router-feedback");
            if(board!=null&&feedback!=null)
            {
                for(int lane=0;lane<3;lane++)
                {
                    var track=root.Q("router-track-"+lane);var shield=root.Q("router-shield-"+lane);var button=root.Q<Button>("router-lane-"+lane);
                    Need(track!=null&&track.worldBound.height>=124&&track.worldBound.width>=432,"Lane track collapsed: "+track?.worldBound);
                    Need(Visible(shield)==button.ClassListContains("ct-router-selected"),"Shield visibility differs from selected lane");
                    if(Visible(shield))Need(shield.worldBound.height>=88&&track.worldBound.Contains(shield.worldBound.center),"Shield geometry outside lane");
                }
                foreach(var row in board.Children())
                    Need(row.worldBound.yMax<=feedback.worldBound.yMin+2,"Lane extends into feedback area: "+row.worldBound);
            }
            foreach(var button in root.Query<Button>().ToList())if(Visible(button))Touch(button);
            foreach(var label in root.Query<Label>().ToList())
            {
                if(!Visible(label)||string.IsNullOrEmpty(label.text))continue;
                var size=label.MeasureTextSize(label.text,label.contentRect.width,VisualElement.MeasureMode.AtMost,0,VisualElement.MeasureMode.Undefined);
                Need(size.x<=label.contentRect.width+2&&size.y<=label.contentRect.height+2,"Clipped text '"+label.text+"' content="+label.contentRect+" measured="+size);
                // Text can fit its own flex box yet spill beyond the enclosing packet.
                for(var container=label.parent;container!=null&&container!=root;container=container.parent)
                {
                    if(!container.ClassListContains("ct-router-packet"))continue;
                    var content=container.LocalToWorld(container.contentRect);
                    Need(label.worldBound.xMin>=content.xMin-2&&label.worldBound.xMax<=content.xMax+2&&
                        label.worldBound.yMin>=content.yMin-2&&label.worldBound.yMax<=content.yMax+2,
                        "Text outside packet content: "+label.text+" label="+label.worldBound+" content="+content);
                }
                Need(label.worldBound.xMin>=root.worldBound.xMin-2&&label.worldBound.xMax<=root.worldBound.xMax+2&&label.worldBound.yMin>=root.worldBound.yMin-2&&label.worldBound.yMax<=root.worldBound.yMax+2,
                    "Text outside viewport: "+label.text+" "+label.worldBound);
            }
        }
        private static bool Visible(VisualElement element){for(var p=element;p!=null;p=p.parent)if(p.resolvedStyle.display==DisplayStyle.None||p.resolvedStyle.visibility==Visibility.Hidden)return false;return element.panel!=null;}
        private static async Task Cancelled(Task task){try{await task;throw new InvalidOperationException("Cancelled round returned a score");}catch(OperationCanceledException){}}
        private static async Task Frames(int count,CancellationToken ct){for(int i=0;i<count;i++)await Awaitable.NextFrameAsync(ct);}
        private static async Task WaitSeconds(double seconds,CancellationToken ct){double end=Time.realtimeSinceStartupAsDouble+seconds;while(Time.realtimeSinceStartupAsDouble<end)await Awaitable.NextFrameAsync(ct);}
        private static async Task Capture(string directory,string name,Receipt receipt,CancellationToken ct)
        {await Awaitable.EndOfFrameAsync(ct);var image=ScreenCapture.CaptureScreenshotAsTexture();Need(image!=null,"Capture failed");try{File.WriteAllBytes(Path.Combine(directory,name+".png"),image.EncodeToPNG());}finally{UnityEngine.Object.Destroy(image);}receipt.captures.Add(name+".png");}
        private static void Need(bool value,string message){if(!value)throw new InvalidOperationException("Raid Router review: "+message);}
    }
}
#endif
