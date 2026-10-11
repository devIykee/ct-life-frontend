using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;
namespace CTLife.UI
{
    public static class ClipCut
    {
        private static VisualElement _active;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        private static Action<bool> _reviewFocus,_reviewPause;private static Func<double> _reviewElapsed;
        public static double ReviewElapsed=>_reviewElapsed?.Invoke()??-1;
        public static Task<ClipCutResult> PlayReview(CancellationToken cancellation,int seed)=>PlayCore(cancellation,seed,false,true);
        public static void ReviewFocus(bool focused)=>_reviewFocus?.Invoke(focused);
        public static void ReviewPause(bool paused)=>_reviewPause?.Invoke(paused);
#endif
        public static Task<ClipCutResult> Play(CancellationToken cancellation=default)=>PlayCore(cancellation,Environment.TickCount,true,false);
        private static Task<ClipCutResult> PlayCore(CancellationToken cancellation,int seed,bool observeFocus,bool review)
        {
            cancellation.ThrowIfCancellationRequested();var ui=UiRoot.Instance;
            if(ui==null)throw new InvalidOperationException("Clip Cut requires the game UI.");
            if(_active!=null)throw new InvalidOperationException("A Clip Cut is already running.");
            var sheet=Resources.Load<StyleSheet>("UI/clip-cut");if(sheet==null)throw new InvalidOperationException("Clip Cut stylesheet is missing.");
            var storyboards=ClipCutStoryboard.Load();
            var round=new ClipCutRound(Time.realtimeSinceStartupAsDouble,seed);
            var completion=new TaskCompletionSource<ClipCutResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool closed=false,moveIn=true,previewing=false;int rendered=-1;double previewStart=0;
            VisualElement root=null,paused=null;Action tick=null;Button setIn=null,setOut=null,reset=null,cut=null,next=null,pause=null,preview=null;
            var frames=new Button[8];var markers=new Label[8];var kinds=new Label[8];
            var monitor=new Image { name="clip-monitor", scaleMode=ScaleMode.ScaleToFit, pickingMode=PickingMode.Ignore };monitor.AddToClassList("ct-clip-monitor-image");
            int shownStory=-1,shownFrame=-1;
            Label Copy(string key,string classes)=>ModDesk.Plain(ClipCutStrings.T(key),classes);
            var timer=ModDesk.Plain("","ct-desk-timer");var score=ModDesk.Plain("","ct-clip-score");var title=ModDesk.Plain("","ct-clip-reel-title");var counter=ModDesk.Plain("","ct-clip-counter");
            var range=ModDesk.Plain("","ct-clip-range");var kept=ModDesk.Plain("","ct-clip-kept");var instruction=Copy("placingIn","ct-clip-instruction");
            var feedback=ModDesk.Plain("","ct-clip-feedback");var previewFrame=ModDesk.Plain("","ct-clip-preview-frame");var previewCopy=ModDesk.Plain("","ct-clip-preview-copy");
            var strip=El.Div("ct-clip-strip");strip.name="clip-strip";var progress=El.Div("ct-desk-progress-fill");
            double Now()=>Time.realtimeSinceStartupAsDouble;
            void Close(bool cancel,bool detach=true)
            {
                if(closed)return;closed=true;
                if(observeFocus){Application.focusChanged-=Focus;Runner.ApplicationPaused-=ApplicationPause;}
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                if(ReferenceEquals(_active,root)){_reviewFocus=null;_reviewPause=null;_reviewElapsed=null;}
#endif
                ui.RemoveUpdater(tick);if(ReferenceEquals(_active,root))_active=null;
                if(cancel)round.Cancel();if(detach)root.RemoveFromHierarchy();
                if(cancel)completion.TrySetCanceled();else completion.TrySetResult(round.Result);
            }
            void Pause(){if(!closed&&!cancellation.IsCancellationRequested&&round.Pause(Now()))Render();}
            void Resume(){if(!closed&&!cancellation.IsCancellationRequested&&round.Resume(Now()))Render();}
            void Focus(bool focused){if(!focused)Pause();}void ApplicationPause(bool value){if(value)Pause();}
            void Sound()=>Bus.Publish(new SfxSignal("sfx_ui_tap"));
            void Replace(ref Button previous,string name,string key,Action action,string extra)
            {
                var parent=previous.parent;int index=parent.IndexOf(previous);var button=El.Btn(ClipCutStrings.T(key),action,"ct-btn ct-clip-control "+extra,sfx:null);button.name=name;
                previous.RemoveFromHierarchy();parent.Insert(index,button);previous=button;
            }
            void BuildReel()
            {
                rendered=round.Index;int expected=rendered;moveIn=true;previewing=false;previewStart=round.Elapsed;strip.Clear();var reel=round.Current;
                for(int i=0;i<8;i++)
                {
                    int frame=i;var button=El.Btn("",()=>{
                        if(closed||cancellation.IsCancellationRequested)return;
                        bool changed=moveIn?round.SetIn(expected,frame,Now()):round.SetOut(expected,frame,Now());
                        if(changed){previewing=false;Sound();Render();}
                    },"ct-btn ct-clip-frame",sfx:null);button.name="clip-frame-"+i;if(i==7)button.AddToClassList("ct-clip-last");
                    markers[i]=ModDesk.Plain("","ct-clip-marker");
                    var thumbnail=new Image { name="clip-thumbnail-"+i,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore };thumbnail.AddToClassList("ct-clip-thumbnail");
                    ClipCutStoryboard.Show(thumbnail,storyboards[reel.Story],reel.Story,i);
                    button.tooltip=ClipCutStrings.F("frameCaption",i+1,ClipCutStrings.Frame(reel,i));
                    kinds[i]=ModDesk.Plain(ClipCutStrings.Kind(reel.Kind(i)),"ct-clip-frame-kind");
                    var face=El.Div("ct-clip-frame-face",El.Div("ct-clip-frame-meta",ModDesk.Plain(ClipCutStrings.F("frame",i+1),"ct-clip-frame-number"),kinds[i],markers[i]),
                        thumbnail,ModDesk.Plain(ClipCutStrings.Frame(reel,i),"ct-clip-frame-copy"));
                    button.Add(El.PassThrough(face));frames[i]=button;strip.Add(button);
                }
                void Mode(bool value){if(closed||cancellation.IsCancellationRequested||round.Index!=expected||round.Paused||round.Cut||round.Finished)return;moveIn=value;Sound();Render();}
                Replace(ref setIn,"clip-in","moveIn",()=>Mode(true),"ct-clip-handle");
                Replace(ref setOut,"clip-out","moveOut",()=>Mode(false),"ct-clip-handle");
                Replace(ref reset,"clip-reset","reset",()=>{if(!closed&&!cancellation.IsCancellationRequested&&round.Reset(expected,Now())){previewing=false;Sound();Render();}},"");
                Replace(ref cut,"clip-cut","cut",()=>{
                    if(closed||cancellation.IsCancellationRequested||!round.Submit(expected,Now()))return;
                    previewing=false;Bus.Publish(new SfxSignal(round.CleanCut?"sfx_ui_success":"sfx_ui_error"));Render();if(round.Finished)Close(false);
                },"ct-clip-primary");
                Replace(ref next,"clip-next","next",()=>{if(!closed&&!cancellation.IsCancellationRequested&&round.Next(expected,Now())){Sound();Render();}},"ct-clip-primary");
                Replace(ref preview,"clip-preview","preview",()=>{
                    if(closed||cancellation.IsCancellationRequested||round.Index!=expected)return;
                    round.Step(Now());if(round.Paused||round.Finished)return;previewing=!previewing;previewStart=round.Elapsed;Sound();Render();
                },"ct-clip-preview-button");
            }
            setIn=El.Btn(ClipCutStrings.T("moveIn"),null,"ct-btn ct-clip-control ct-clip-handle",sfx:null);setIn.name="clip-in";
            setOut=El.Btn(ClipCutStrings.T("moveOut"),null,"ct-btn ct-clip-control ct-clip-handle",sfx:null);setOut.name="clip-out";
            reset=El.Btn(ClipCutStrings.T("reset"),null,"ct-btn ct-clip-control",sfx:null);reset.name="clip-reset";
            cut=El.Btn(ClipCutStrings.T("cut"),null,"ct-btn ct-clip-control ct-clip-primary",sfx:null);cut.name="clip-cut";
            next=El.Btn(ClipCutStrings.T("next"),null,"ct-btn ct-clip-control ct-clip-primary",sfx:null);next.name="clip-next";
            preview=El.Btn(ClipCutStrings.T("preview"),null,"ct-btn ct-clip-control ct-clip-preview-button",sfx:null);preview.name="clip-preview";
            pause=El.Btn(ClipCutStrings.T("pause"),Pause,"ct-btn ct-clip-control",sfx:null);pause.name="clip-pause";
            var end=El.Btn(ClipCutStrings.T("end"),()=>Close(true),"ct-btn ct-clip-control ct-clip-end",sfx:null);end.name="clip-end";
            var resume=El.Btn(ClipCutStrings.T("resume"),Resume,"ct-btn ct-clip-control ct-clip-primary",sfx:null);resume.name="clip-resume";
            var pauseEnd=El.Btn(ClipCutStrings.T("end"),()=>Close(true),"ct-btn ct-clip-control ct-clip-end",sfx:null);pauseEnd.name="clip-paused-end";
            paused=El.Div("ct-clip-paused",El.Div("ct-clip-pause-panel",Copy("paused","ct-desk-title"),Copy("pauseDetail","ct-clip-pause-copy"),El.Div("ct-clip-controls",resume,pauseEnd)));paused.name="clip-paused";
            root=El.Div("ct-desk-overlay ct-clip-overlay",El.Div("ct-desk-panel ct-clip-panel",
                El.Div("ct-desk-header",El.Div("ct-desk-heading",Copy("title","ct-clip-title")),score,timer),El.Div("ct-desk-progress",progress),
                El.Div("ct-clip-bench",El.Div("ct-clip-monitor",monitor),
                    El.Div("ct-clip-preview-copy-area",counter,title,previewFrame,previewCopy,El.Div("ct-clip-spacer"),feedback)),strip,
                El.Div("ct-clip-handles",setIn,setOut,El.Div("ct-clip-range-info",Copy("brief","ct-clip-brief"),range,kept,instruction)),
                El.Div("ct-clip-controls",reset,preview,pause,end,El.Div("ct-clip-spacer"),cut,next),Copy(review?"review":"practice","ct-clip-footnote"),paused));
            root.name="clip-cut-game";ModDesk.ApplySheet(root);root.styleSheets.Add(sheet);
            void Render()
            {
                if(rendered!=round.Index)BuildReel();var reel=round.Current;
                timer.text=ClipCutStrings.F("timer",(int)Math.Ceiling(round.Remaining));score.text=ClipCutStrings.F("score",round.Score);
                title.text=ClipCutStrings.Title(reel);counter.text=ClipCutStrings.F("reel",round.Index+1);range.text=ClipCutStrings.F("range",round.In+1,round.Out+1);kept.text=ClipCutStrings.F("kept",round.Kept);
                instruction.text=round.Cut?ClipCutStrings.F("answer",reel.Setup+1,reel.Payoff+1):ClipCutStrings.T(moveIn?"placingIn":"placingOut");
                progress.style.width=Length.Percent((float)(round.Remaining*100/ClipCutRound.Duration));
                int cursor=previewing?round.In+(int)((round.Elapsed-previewStart)/.65)%round.Kept:(moveIn?round.In:round.Out);
                previewFrame.text=round.Cut?ClipCutStrings.F("previewFrame",cursor+1,ClipCutStrings.Kind(reel.Kind(cursor))):ClipCutStrings.F("previewNumber",cursor+1);previewCopy.text=ClipCutStrings.Frame(reel,cursor);
                if(shownStory!=reel.Story||shownFrame!=cursor)
                {
                    ClipCutStoryboard.Show(monitor,storyboards[reel.Story],reel.Story,cursor);
                    monitor.tooltip=ClipCutStrings.F("frameCaption",cursor+1,ClipCutStrings.Frame(reel,cursor));shownStory=reel.Story;shownFrame=cursor;
                }
                preview.text=ClipCutStrings.T(previewing?"stop":"preview");preview.SetEnabled(!round.Paused&&!round.Finished);
                string reason=round.In>reel.Setup?ClipCutStrings.T("missingSetup"):round.Out<reel.Payoff?ClipCutStrings.T("missingPayoff"):ClipCutStrings.T("extra");
                feedback.text=!round.Cut?"":round.CleanCut?ClipCutStrings.T("clean"):ClipCutStrings.T("wrong")+" · "+reason;
                feedback.EnableInClassList("ct-clip-mistake",round.Cut&&!round.CleanCut);
                paused.Show(round.Paused);pause.SetEnabled(!round.Paused);reset.SetEnabled(!round.Paused&&!round.Cut&&(round.In!=0||round.Out!=7));
                setIn.EnableInClassList("ct-clip-active-handle",moveIn);setOut.EnableInClassList("ct-clip-active-handle",!moveIn);
                setIn.SetEnabled(!round.Paused&&!round.Cut&&!moveIn);setOut.SetEnabled(!round.Paused&&!round.Cut&&moveIn);
                cut.Show(!round.Cut);cut.SetEnabled(!round.Paused&&!round.Finished);next.Show(round.Cut&&!round.AllCut);next.SetEnabled(!round.Paused&&!round.Finished);
                for(int i=0;i<8;i++)
                {
                    kinds[i].Show(round.Cut);
                    bool included=i>=round.In&&i<=round.Out;frames[i].EnableInClassList("ct-clip-included",included);frames[i].EnableInClassList("ct-clip-excluded",!included);
                    frames[i].EnableInClassList("ct-clip-previewing",previewing&&cursor==i);frames[i].EnableInClassList("ct-clip-boundary",i==round.In||i==round.Out);
                    markers[i].text=i==round.In&&i==round.Out?ClipCutStrings.T("both"):i==round.In?ClipCutStrings.T("in"):i==round.Out?ClipCutStrings.T("out"):"";
                    frames[i].SetEnabled(!round.Paused&&!round.Cut&&!round.Finished&&i!=(moveIn?round.In:round.Out));
                }
            }
            tick=()=>{if(closed)return;if(cancellation.IsCancellationRequested){Close(true);return;}round.Step(Now());Render();if(round.Finished)Close(false);};
            root.RegisterCallback<DetachFromPanelEvent>(e=>{if(ReferenceEquals(e.target,root))Close(true,false);});_active=root;
            try
            {
                ui.ModalLayer.Add(root);ui.AddUpdater(tick);
                if(observeFocus){_=Runner.Instance;Application.focusChanged+=Focus;Runner.ApplicationPaused+=ApplicationPause;if(!Application.isFocused)Pause();}
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                if(review){_reviewFocus=Focus;_reviewPause=ApplicationPause;_reviewElapsed=()=>round.Elapsed;}
#endif
                Render();tick();
            }
            catch{Close(true);throw;}
            return completion.Task;
        }
    }
}
