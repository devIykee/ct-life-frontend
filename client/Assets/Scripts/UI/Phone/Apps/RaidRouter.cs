using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;
namespace CTLife.UI
{
    /// <summary>Practice-only arcade. No registry assignment, REST, score submission or payout.</summary>
    public static class RaidRouter
    {
        // Original geometric shield. The established palette supplies its colour.
        private sealed class ShieldGlyph : VisualElement
        {
            public ShieldGlyph()
            {
                AddToClassList("ct-router-shield-glyph");pickingMode=PickingMode.Ignore;
                Add(new UiIcon("energy"));
                generateVisualContent += context =>
                {
                    var p=context.painter2D;float w=contentRect.width,h=contentRect.height;
                    if(w<=0||h<=0)return;
                    p.fillColor=resolvedStyle.color;p.BeginPath();
                    p.MoveTo(new Vector2(w*.5f,0));p.LineTo(new Vector2(w*.94f,h*.16f));
                    p.LineTo(new Vector2(w*.88f,h*.62f));p.LineTo(new Vector2(w*.72f,h*.84f));
                    p.LineTo(new Vector2(w*.5f,h));p.LineTo(new Vector2(w*.28f,h*.84f));
                    p.LineTo(new Vector2(w*.12f,h*.62f));p.LineTo(new Vector2(w*.06f,h*.16f));
                    p.ClosePath();p.Fill();
                };
            }
        }
        private static VisualElement _active;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        private static Action<bool> _reviewFocus;
        private static Func<double> _reviewElapsed;
        public static double ReviewElapsed=>_reviewElapsed?.Invoke()??-1;
        public static Task<RaidRouterResult> PlayReview(CancellationToken cancellation,int seed)
            => PlayCore(cancellation,seed,false,true);
        public static void ReviewFocus(bool focused)=>_reviewFocus?.Invoke(focused);
#endif
        public static Task<RaidRouterResult> Play(CancellationToken cancellation=default)
            => PlayCore(cancellation,Environment.TickCount,true,false);
        private static Task<RaidRouterResult> PlayCore(CancellationToken cancellation,int seed,bool observeFocus,bool review)
        {
            cancellation.ThrowIfCancellationRequested();
            var ui=UiRoot.Instance;
            if(ui==null)throw new InvalidOperationException("Raid Router requires the game UI.");
            if(_active!=null)throw new InvalidOperationException("A Raid Router is already running.");
            var sheet=Resources.Load<StyleSheet>("UI/raid-router");
            if(sheet==null)throw new InvalidOperationException("Raid Router stylesheet is missing.");
            var round=new RaidRouterRound(Time.realtimeSinceStartupAsDouble,seed);
            var completion=new TaskCompletionSource<RaidRouterResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool closed=false;int lastHandled=0;double feedbackUntil=0,impactUntil=0;int impactLane=-1;bool impactCorrect=false;
            VisualElement root=null,paused=null;Action tick=null;Button park=null,pause=null;
            Label Copy(string key,string classes)=>ModDesk.Plain(RaidRouterStrings.T(key),classes);
            var timer=ModDesk.Plain("","ct-desk-timer");
            var tally=ModDesk.Plain("","ct-router-stat");var combo=ModDesk.Plain("","ct-router-stat");
            var score=ModDesk.Plain("","ct-router-score");var wave=ModDesk.Plain("","ct-router-wave");
            var feedback=Copy("waiting","ct-router-feedback");
            var progress=El.Div("ct-desk-progress-fill");
            var board=El.Div("ct-router-board");
            var tracks=new VisualElement[3];var laneButtons=new Button[3];var statuses=new Label[3];var shields=new VisualElement[3];
            var tiles=new VisualElement[RaidRouterRound.PacketCount];
            double Now()=>Time.realtimeSinceStartupAsDouble;
            void Move(int lane)
            {
                if(closed||cancellation.IsCancellationRequested||!round.SetShield(lane,Now()))return;
                Bus.Publish(new SfxSignal("sfx_ui_tap"));Render();
            }
            for(int i=0;i<3;i++)
            {
                int lane=i;var button=El.Btn("",()=>Move(lane),"ct-btn ct-router-lane",sfx:null);button.name="router-lane-"+lane;
                statuses[i]=Copy("move","ct-router-lane-status");
                var name=Copy("lane."+lane,"ct-router-lane-name");
                var socket=El.Div("ct-router-socket",new UiIcon("chat"));
                var laneCopy=El.Div("ct-router-lane-copy",name,statuses[i]);
                button.Add(El.PassThrough(El.Div("ct-router-lane-label",socket,laneCopy)));laneButtons[i]=button;
                var gate=El.Div("ct-router-gate");gate.pickingMode=PickingMode.Ignore;
                var guide=El.Div("ct-router-guide");guide.pickingMode=PickingMode.Ignore;
                shields[i]=El.Div("ct-router-shield",new ShieldGlyph());shields[i].name="router-shield-"+lane;
                tracks[i]=El.Div("ct-router-track",guide,gate,shields[i]);tracks[i].name="router-track-"+lane;
                // One native Button owns the entire lane, including the visible track.
                button.Add(El.PassThrough(tracks[i]));board.Add(El.Div("ct-router-row",button));
            }
            foreach(var packet in round.Packets)
            {
                var tile=El.Div("ct-router-packet",Copy(packet.IsRaid?"raid":"chat","ct-router-kind"),
                    Copy((packet.IsRaid?"raid.":"chat.")+packet.Copy,"ct-router-message"));
                tile.name="router-packet-"+packet.Index;tile.AddToClassList(packet.IsRaid?"ct-router-raid":"ct-router-chat");
                tile.pickingMode=PickingMode.Ignore;tiles[packet.Index]=tile;
            }
            park=El.Btn("",()=>Move(-1),"ct-btn ct-router-control ct-router-park",sfx:null);park.name="router-park";
            park.Add(El.PassThrough(new ShieldGlyph()));park.Add(Copy("park","ct-router-control-copy"));
            pause=El.Btn(RaidRouterStrings.T("pause"),()=>Pause(),"ct-btn ct-router-control",sfx:null);pause.name="router-pause";
            var end=El.Btn(RaidRouterStrings.T("end"),()=>Close(true),"ct-btn ct-router-control ct-router-end",sfx:null);end.name="router-end";
            var resume=El.Btn(RaidRouterStrings.T("resume"),()=>
            {if(!closed&&!cancellation.IsCancellationRequested&&round.Resume(Now()))Render();},"ct-btn ct-router-control ct-router-resume",sfx:null);resume.name="router-resume";
            paused=El.Div("ct-router-paused",El.Div("ct-router-pause-card",Copy("paused","ct-desk-title"),Copy("pauseDetail","ct-router-brief"),resume));paused.name="router-paused";
            root=El.Div("ct-desk-overlay ct-router-overlay",El.Div("ct-desk-panel ct-router-panel",
                El.Div("ct-desk-header",El.Div("ct-desk-heading",Copy("eyebrow","ct-desk-eyebrow"),Copy("title","ct-router-title")),
                    El.Div("ct-router-scoreboard",Copy("scoreLabel","ct-router-score-label"),score),timer),
                El.Div("ct-desk-progress",progress),Copy("brief","ct-router-brief"),Copy("rule","ct-router-rule"),
                El.Div("ct-router-meta",wave,tally,combo),board,feedback,
                El.Div("ct-router-controls",park,El.Div("ct-router-control-spacer"),pause,end),Copy(review?"review":"practice","ct-router-footnote"),paused));
            root.name="raid-router";ModDesk.ApplySheet(root);root.styleSheets.Add(sheet);
            void Close(bool cancelled,bool detach=true)
            {
                if(closed)return;closed=true;
                if(observeFocus)Application.focusChanged-=Focus;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                if(ReferenceEquals(_active,root)){_reviewFocus=null;_reviewElapsed=null;}
#endif
                ui.RemoveUpdater(tick);if(ReferenceEquals(_active,root))_active=null;
                if(detach)root.RemoveFromHierarchy();
                if(cancelled)completion.TrySetCanceled();else completion.TrySetResult(round.Result);
            }
            void Pause()
            {if(!closed&&!cancellation.IsCancellationRequested&&round.Pause(Now()))Render();}
            void Focus(bool focused){if(!focused)Pause();}
            void Render()
            {
                timer.text=RaidRouterStrings.F("timer",(int)Math.Ceiling(round.Remaining));
                timer.EnableInClassList("ct-router-urgent",round.Remaining<=10);
                wave.text=RaidRouterStrings.F("wave",round.Wave);
                tally.text=RaidRouterStrings.F("tally",round.RaidsStopped);combo.text=RaidRouterStrings.F("combo",round.Combo);
                score.text=RaidRouterStrings.F("score",round.Result.Score);
                progress.style.width=Length.Percent((float)(100*round.Remaining/RaidRouterRound.Duration));
                paused.Show(round.Paused);park.SetEnabled(!round.Paused);pause.SetEnabled(!round.Paused);
                park.EnableInClassList("ct-router-selected",round.ShieldLane<0);
                for(int i=0;i<3;i++)
                {
                    bool selected=round.ShieldLane==i;
                    laneButtons[i].EnableInClassList("ct-router-selected",selected);laneButtons[i].SetEnabled(!round.Paused);
                    statuses[i].text=RaidRouterStrings.T(selected?"shield":"move");
                    tracks[i].EnableInClassList("ct-router-protected",selected);shields[i].Show(selected);
                    bool impact=impactLane==i&&round.Elapsed<impactUntil;
                    tracks[i].EnableInClassList("ct-router-impact-good",impact&&impactCorrect);
                    tracks[i].EnableInClassList("ct-router-impact-bad",impact&&!impactCorrect);
                }
                foreach(var packet in round.Packets)
                {
                    var tile=tiles[packet.Index];double position=(round.Elapsed-(packet.Arrival-RaidRouterRound.ApproachSeconds))/RaidRouterRound.ApproachSeconds;
                    bool visible=!packet.Handled&&position>=0&&position<=1;
                    if(!visible){if(tile.parent!=null)tile.RemoveFromHierarchy();continue;}
                    var track=tracks[packet.Lane];if(tile.parent==null){track.Add(tile);shields[packet.Lane].BringToFront();}
                    float width=track.contentRect.width;
                    // The tile is completely inside the visible track at both spawn and interception.
                    if(width>432)tile.style.left=(float)(80+(width-352-80)*(1-position));
                }
                if(round.Handled!=lastHandled)
                {
                    lastHandled=round.Handled;var packet=round.LastPacket;
                    feedback.text=RaidRouterStrings.T(packet.CloseCall?"closeCall":packet.IsRaid?(packet.Correct?"stopped":"leaked"):(packet.Correct?"passed":"blocked"));
                    feedback.EnableInClassList("ct-router-mistake",!packet.Correct);
                    feedbackUntil=round.Elapsed+1.1;impactUntil=round.Elapsed+.36;impactLane=packet.Lane;impactCorrect=packet.Correct;
                    Bus.Publish(new SfxSignal(packet.Correct?(packet.IsRaid?"sfx_ui_success":"sfx_ui_tap"):"sfx_ui_error"));
                }
                else if(round.Elapsed>=feedbackUntil)
                {
                    feedback.text=RaidRouterStrings.T(round.Handled==RaidRouterRound.PacketCount?"clear":"waiting");
                    feedback.EnableInClassList("ct-router-mistake",false);
                }
            }
            tick=()=>
            {
                if(closed)return;
                if(cancellation.IsCancellationRequested){Close(true);return;}
                round.Step(Now());Render();if(round.Finished)Close(false);
            };
            root.RegisterCallback<DetachFromPanelEvent>(e=>{if(ReferenceEquals(e.target,root))Close(true,false);});
            _active=root;
            try
            {
                ui.ModalLayer.Add(root);ui.AddUpdater(tick);
                if(observeFocus){Application.focusChanged+=Focus;if(!Application.isFocused)Pause();}
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                if(review){_reviewFocus=Focus;_reviewElapsed=()=>round.Elapsed;}
#endif
                Render();tick();
            }
            catch{Close(true);throw;}
            return completion.Task;
        }
    }
}
