using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;
namespace CTLife.UI
{
    public static class RunwayPlan
    {
        private static VisualElement _active;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        private static Action<bool> _reviewFocus, _reviewPause;
        private static Func<double> _reviewElapsed;
        public static double ReviewElapsed => _reviewElapsed?.Invoke() ?? -1;
        public static Task<RunwayPlanResult> PlayReview(CancellationToken cancellation, int seed) => PlayCore(cancellation,seed,false,true);
        public static void ReviewFocus(bool focused)=>_reviewFocus?.Invoke(focused);
        public static void ReviewPause(bool paused)=>_reviewPause?.Invoke(paused);
#endif
        public static Task<RunwayPlanResult> Play(CancellationToken cancellation=default)=>PlayCore(cancellation,Environment.TickCount,true,false);
        /// <summary>Deterministic entry point for the Careers lane (same seed → same round).</summary>
        public static Task<RunwayPlanResult> PlaySeeded(int seed, CancellationToken cancellation = default) => PlayCore(cancellation, seed, true, false);
        private static Task<RunwayPlanResult> PlayCore(CancellationToken cancellation,int seed,bool observeFocus,bool review)
        {
            cancellation.ThrowIfCancellationRequested();var ui=UiRoot.Instance;
            if(ui==null)throw new InvalidOperationException("Runway Plan requires the game UI.");
            if(_active!=null)throw new InvalidOperationException("A Runway Plan is already running.");
            var sheet=Resources.Load<StyleSheet>("UI/runway-plan");if(sheet==null)throw new InvalidOperationException("Runway Plan stylesheet is missing.");
            var round=new RunwayPlanRound(Time.realtimeSinceStartupAsDouble,seed);
            var completion=new TaskCompletionSource<RunwayPlanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool closed=false;int rendered=-1;Action tick=null;VisualElement root=null,paused=null;
            Button undo=null,file=null,next=null,pause=null;var presets=new Button[5,3];
            Label Copy(string key,string classes)=>ModDesk.Plain(RunwayPlanStrings.T(key),classes);
            var title=ModDesk.Plain("","ct-runway-board-title");var counter=ModDesk.Plain("","ct-runway-counter");
            var timer=ModDesk.Plain("","ct-desk-timer");var score=ModDesk.Plain("","ct-runway-score");
            var edits=ModDesk.Plain("","ct-runway-edits");var feedback=ModDesk.Plain("","ct-runway-feedback");
            var meterLabels=new[]{Copy("cashLabel","ct-runway-meter-label"),ModDesk.Plain("","ct-runway-meter-label"),ModDesk.Plain("","ct-runway-meter-label")};
            var values=new Label[3];var meters=El.Div("ct-runway-meters");
            for(int i=0;i<3;i++){values[i]=ModDesk.Plain("","ct-runway-meter-value");meters.Add(El.Div("ct-runway-meter",meterLabels[i],values[i]));}
            var board=El.Div("ct-runway-board");board.name="runway-board";var progress=El.Div("ct-desk-progress-fill");
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
            void Focus(bool focused){if(!focused)Pause();}
            void ApplicationPause(bool value){if(value)Pause();}
            void Sound()=>Bus.Publish(new SfxSignal("sfx_ui_tap"));
            void Replace(ref Button previous,string name,string key,Action action,string extra)
            {
                var parent=previous.parent;int index=parent.IndexOf(previous);
                var button=El.Btn(RunwayPlanStrings.T(key),action,"ct-btn ct-runway-control "+extra,sfx:null);button.name=name;
                previous.RemoveFromHierarchy();parent.Insert(index,button);previous=button;
            }
            void BuildBoard()
            {
                rendered=round.Index;int expected=rendered;var current=round.Current;board.Clear();
                for(int i=0;i<5;i++)
                {
                    int line=i;var data=current.Line(i);var strip=El.Div("ct-runway-strip");strip.name="runway-strip-"+i;
                    if(i==4)strip.AddToClassList("ct-runway-last");
                    strip.Add(ModDesk.Plain(RunwayPlanStrings.Board(current,"line."+data.Source),"ct-runway-line-title"));
                    for(int j=0;j<3;j++)
                    {
                        int preset=j;var button=El.Btn("",()=>{
                            if(closed||cancellation.IsCancellationRequested||!round.Select(expected,line,preset,Now()))return;
                            Sound();Render();
                        },"ct-btn ct-runway-preset",sfx:null);button.name="runway-preset-"+i+"-"+j;
                        var capacity=data.Metric<0?RunwayPlanStrings.T("optional"):RunwayPlanStrings.F("service",data.Capacity(j),RunwayPlanStrings.Board(current,data.Metric==0?"a":"b"));
                        button.Add(El.PassThrough(El.Div("ct-runway-preset-face",El.Div("ct-runway-preset-top",
                            ModDesk.Plain(RunwayPlanStrings.Level(j),"ct-runway-level"),
                            ModDesk.Plain(RunwayPlanStrings.F("presetCost",data.Cost(j)),"ct-runway-cost"),El.Div("ct-runway-lamp")),
                            ModDesk.Plain(capacity,"ct-runway-service"))));
                        strip.Add(button);presets[i,j]=button;
                    }
                    board.Add(strip);
                }
                Replace(ref undo,"runway-undo","undo",()=>{if(!closed&&!cancellation.IsCancellationRequested&&round.Undo(expected,Now())){Sound();Render();}},"");
                Replace(ref file,"runway-file","file",()=>{
                    if(closed||cancellation.IsCancellationRequested||!round.File(expected,Now()))return;
                    Bus.Publish(new SfxSignal(round.BoardValid?"sfx_ui_success":"sfx_ui_error"));Render();if(round.Finished)Close(false);
                },"ct-runway-primary");
                Replace(ref next,"runway-next","next",()=>{if(!closed&&!cancellation.IsCancellationRequested&&round.Next(expected,Now())){Sound();Render();}},"ct-runway-primary");
            }
            undo=El.Btn(RunwayPlanStrings.T("undo"),null,"ct-btn ct-runway-control",sfx:null);undo.name="runway-undo";
            file=El.Btn(RunwayPlanStrings.T("file"),null,"ct-btn ct-runway-control ct-runway-primary",sfx:null);file.name="runway-file";
            next=El.Btn(RunwayPlanStrings.T("next"),null,"ct-btn ct-runway-control ct-runway-primary",sfx:null);next.name="runway-next";
            pause=El.Btn(RunwayPlanStrings.T("pause"),Pause,"ct-btn ct-runway-control",sfx:null);pause.name="runway-pause";
            var end=El.Btn(RunwayPlanStrings.T("end"),()=>Close(true),"ct-btn ct-runway-control ct-runway-end",sfx:null);end.name="runway-end";
            var resume=El.Btn(RunwayPlanStrings.T("resume"),Resume,"ct-btn ct-runway-control ct-runway-primary",sfx:null);resume.name="runway-resume";
            var pauseEnd=El.Btn(RunwayPlanStrings.T("end"),()=>Close(true),"ct-btn ct-runway-control ct-runway-end",sfx:null);pauseEnd.name="runway-paused-end";
            paused=El.Div("ct-runway-paused",El.Div("ct-runway-pause-panel",Copy("paused","ct-desk-title"),Copy("pauseDetail","ct-runway-pause-copy"),El.Div("ct-runway-controls",resume,pauseEnd)));paused.name="runway-paused";
            root=El.Div("ct-desk-overlay ct-runway-overlay",El.Div("ct-desk-panel ct-runway-panel",
                El.Div("ct-desk-header",El.Div("ct-desk-heading",Copy("title","ct-runway-title")),score,timer),
                El.Div("ct-desk-progress",progress),El.Div("ct-runway-meta",title,counter),Copy("instruction","ct-runway-instruction"),meters,board,
                El.Div("ct-runway-feedback-row",feedback,edits),El.Div("ct-runway-controls",undo,pause,end,
                    Copy(review?"review":"practice","ct-runway-footnote"),file,next),paused));
            root.name="runway-plan-game";ModDesk.ApplySheet(root);root.styleSheets.Add(sheet);
            void Render()
            {
                if(rendered!=round.Index)BuildBoard();var current=round.Current;
                timer.text=RunwayPlanStrings.F("timer",(int)Math.Ceiling(round.Remaining));score.text=RunwayPlanStrings.F("score",round.Score);
                title.text=RunwayPlanStrings.Board(current,"title");counter.text=RunwayPlanStrings.F("board",round.Index+1);edits.text=RunwayPlanStrings.F("edits",round.Edits);
                progress.style.width=Length.Percent((float)(round.Remaining*100/RunwayPlanRound.Duration));
                meterLabels[1].text=RunwayPlanStrings.Board(current,"a");meterLabels[2].text=RunwayPlanStrings.Board(current,"b");
                values[0].text=RunwayPlanStrings.F("cash",round.Cost,current.Ceiling);values[1].text=RunwayPlanStrings.F("capacity",round.Capacity(0),current.MinimumA);values[2].text=RunwayPlanStrings.F("capacity",round.Capacity(1),current.MinimumB);
                for(int i=0;i<3;i++)values[i].EnableInClassList("ct-runway-met",i==0?round.MeetsBudget:i==1?round.MeetsA:round.MeetsB);
                string status=!round.MeetsA?RunwayPlanStrings.F("serviceFail",RunwayPlanStrings.Board(current,"a"),current.MinimumA)
                    :!round.MeetsB?RunwayPlanStrings.F("serviceFail",RunwayPlanStrings.Board(current,"b"),current.MinimumB)
                    :!round.MeetsBudget?RunwayPlanStrings.F("budgetFail",round.Cost-current.Ceiling):RunwayPlanStrings.T("ready");
                feedback.text=round.Filed?RunwayPlanStrings.T(round.BoardValid?"filed":"rejected")+" · "+(round.BoardValid?RunwayPlanStrings.Board(current,"joke"):status):status;
                feedback.EnableInClassList("ct-runway-mistake",round.Filed&&!round.BoardValid);
                paused.Show(round.Paused);pause.SetEnabled(!round.Paused);undo.SetEnabled(!round.Paused&&!round.Filed&&round.Edits>0);
                file.Show(!round.Filed);file.SetEnabled(!round.Paused&&!round.Finished&&round.Edits>0);next.Show(round.Filed&&!round.AllFiled);next.SetEnabled(!round.Paused&&!round.Finished);
                for(int i=0;i<5;i++)for(int j=0;j<3;j++)
                {bool selected=round.Preset(i)==j;presets[i,j].EnableInClassList("ct-runway-selected",selected);presets[i,j].SetEnabled(!round.Paused&&!round.Filed&&!round.Finished&&!selected&&round.Edits<3);}
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
