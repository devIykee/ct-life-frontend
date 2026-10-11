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
    public static class SpacesPresentationReview
    {
        private sealed class ReviewApp : PhoneApp
        {
            public string Scenario;
            public VisualElement Current;
            public override string Id => "spaces-review";
            public override string Title => SpacesStrings.T("title");
            public override VisualElement Open() => Current = SpacesApp.BuildReview(Scenario, Phone.Close);
        }
        [Serializable] private sealed class Receipt
        {
            public bool pass;
            public string scenario, failure;
            public string limits = "Watermarked simulated rooms. Real UI disclosure/edit/mute/leave callbacks only. No API/session/audio object is resolved by the fixture. No microphone permission/capture, game routes, server authorization, live listeners or physical touch acceptance.";
            public List<string> checks = new List<string>(), captures = new List<string>();
        }
        public static async Task Run(PhoneShell phone, string directory, CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!Path.IsPathRooted(directory)) directory=Path.Combine(Application.persistentDataPath,directory);
#endif
            directory=Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(owner); timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var ct=timeout.Token; var receipt=new Receipt(); var app=new ReviewApp(); phone.Register(app);
            try
            {
                foreach(string scenario in new[]{"list","unavailable","listening","host","role-change","pending","error","ended"})
                {
                    receipt.scenario=scenario; var previous=app.Current; app.Scenario=scenario; phone.Open(app.Id); await Settle(app.Current,ct);
                    if(previous!=null)Need(!SpacesApp.IsIsolatedReview(previous),"Previous fixture view was retained after phone navigation");
                    Inspect(app.Current,scenario); await Capture(directory,"spaces-"+scenario,receipt,ct);
                    if(scenario=="list")
                    {
                        Need(app.Current.Q<TextField>("spaces-topic")==null,"Host form crowds initial room list");
                        await Activate(app.Current,"spaces-hostRoom",ct);
                        var input=app.Current.Q<TextField>("spaces-topic"); Need(input!=null,"Host composer did not open");
                        input.value="A quiet builder table"; await Frames(2,ct);
                        await Activate(app.Current,"spaces-closeCompose",ct); await Activate(app.Current,"spaces-hostRoom",ct);
                        Need(app.Current.Q<TextField>("spaces-topic").value=="A quiet builder table","Real topic callback lost draft across disclosure");
                        var scroll=(ScrollView)app.Current; scroll.ScrollTo(app.Current.Q<TextField>("spaces-topic")); await Settle(app.Current,ct);
                        Inspect(app.Current,scenario); await Capture(directory,"spaces-host-draft",receipt,ct);
                        receipt.checks.Add("PASS real host disclosure and TextField draft retention; create disabled.");
                    }
                    if(scenario=="unavailable")
                    {
                        await Activate(app.Current,"spaces-city",ct);
                        Need(!phone.IsOpen && !SpacesApp.IsIsolatedReview(app.Current),"Return to city did not close/dispose fixture");
                        receipt.checks.Add("PASS actual Return to city closes phone and disposes old view.");
                    }
                    if(scenario=="listening")
                    {
                        Need(app.Current.Q<Button>("spaces-mic")==null,"Listener sees a microphone publishing control");
                        await Activate(app.Current,"spaces-mute",ct);
                        Need(app.Current.Q<Label>(className:"ct-space-audio").text==SpacesStrings.T("muted"),"Mute presentation did not change");
                        await Activate(app.Current,"spaces-mute",ct);
                        Need(app.Current.Q<Label>(className:"ct-space-audio").text==SpacesStrings.T("connected"),"Unmute presentation did not restore");
                        await Activate(app.Current,"spaces-report",ct);
                        Need(app.Current.Q("spaces-report-panel")!=null,"Report disclosure missing");
                        ((ScrollView)app.Current).ScrollTo(app.Current.Q("spaces-report-panel")); await Settle(app.Current,ct);
                        Inspect(app.Current,scenario); await Capture(directory,"spaces-report-disclosure",receipt,ct);
                        await Activate(app.Current,"spaces-report",ct); Need(app.Current.Q("spaces-report-panel")==null,"Report disclosure did not close");
                        await Activate(app.Current,"spaces-leave",ct);
                        Need(app.Current.Q<Label>(className:"ct-space-audio")==null,"Leave retained room state");
                        receipt.checks.Add("PASS mute/unmute presentation, report disclosure and leave-to-list callbacks; no audio/network operations.");
                    }
                    if(scenario=="host")
                    {
                        Need(app.Current.Q<Button>("spaces-mic")?.enabledInHierarchy==false,"Fixture enabled microphone");
                        await Activate(app.Current,"spaces-end",ct);
                        Need(app.Current.Q<Button>("spaces-confirmEnd")!=null && !app.Current.Q<Button>("spaces-confirmEnd").enabledInHierarchy,"End confirmation missing or enabled write");
                        ((ScrollView)app.Current).ScrollTo(app.Current.Q<Button>("spaces-confirmEnd")); await Settle(app.Current,ct);
                        Inspect(app.Current,scenario); await Capture(directory,"spaces-end-confirmation",receipt,ct);
                        await Activate(app.Current,"spaces-cancel",ct); Need(app.Current.Q<Button>("spaces-confirmEnd")==null,"End cancel failed");
                        receipt.checks.Add("PASS host end confirmation/cancel and disabled mic/role writes.");
                    }
                    if(scenario=="role-change")Need(app.Current.Q<Button>("spaces-mic")==null,"Role removal retained microphone action");
                    if(scenario=="pending")Need(app.Current.Q<TextField>("spaces-topic")?.enabledInHierarchy==false,"Unconfirmed room request can change topic");
                    receipt.checks.Add("PASS native text/control geometry "+scenario);
                }
                receipt.pass=true;
                Log.Info("spaces-ui-review","PASS eight watermarked states; actual local UI callbacks, controls/text measured; no game API calls or microphone use.");
            }
            catch(Exception error)
            {
                receipt.failure=error.Message;
                try { await Capture(directory,"spaces-failure-"+receipt.scenario,receipt,CancellationToken.None); } catch { }
                throw;
            }
            finally { File.WriteAllText(Path.Combine(directory,"spaces-ui-review.json"),JsonUtility.ToJson(receipt,true)); }
        }
        private static async Task Activate(VisualElement root,string name,CancellationToken ct)
        {
            var button=root.Q<Button>(name); Need(button!=null && button.enabledInHierarchy,"Missing/disabled safe control "+name);
            var scroll=(ScrollView)root; scroll.ScrollTo(button); await Settle(root,ct);
            var b=button.worldBound; var v=scroll.contentViewport.worldBound;
            Need(b.xMin>=v.xMin-2 && b.xMax<=v.xMax+2 && b.yMin>=v.yMin-2 && b.yMax<=v.yMax+2,"Control outside viewport: "+name+" bounds="+b+" viewport="+v);
            using(var e=NavigationSubmitEvent.GetPooled()) { e.target=button; button.SendEvent(e); }
            await Settle(root,ct);
        }
        private static void Inspect(VisualElement root,string scenario)
        {
            Need(root.panel!=null && root.worldBound.width>0,"Fixture is not mounted");
            Need(SpacesApp.IsIsolatedReview(root),"Fixture constructed transport/session/audio or disposed during child rerender");
            Need(root.Q<Label>(className:"ct-space-review")?.text==SpacesStrings.T("review"),"Fixture watermark missing");
            foreach(var button in root.Query<Button>().ToList())
            {
                if(!Visible(button))continue;
                Need(button.worldBound.width>=124 && button.worldBound.height>=124,$"Undersized target scenario={scenario} name={button.name} bounds={button.worldBound}");
                if(!string.IsNullOrEmpty(button.text))
                {
                    var size=button.MeasureTextSize(button.text,button.contentRect.width,VisualElement.MeasureMode.Exactly,0,VisualElement.MeasureMode.Undefined);
                    Need(size.y<=button.contentRect.height+2,$"Clipped button scenario={scenario} name={button.name} text={button.text} content={button.contentRect} measured={size}");
                }
            }
            foreach(var label in root.Query<Label>().ToList())
            {
                if(!Visible(label)||string.IsNullOrEmpty(label.text))continue;
                var size=label.MeasureTextSize(label.text,label.contentRect.width,VisualElement.MeasureMode.AtMost,0,VisualElement.MeasureMode.Undefined);
                Need(size.x<=label.contentRect.width+2&&size.y<=label.contentRect.height+2,$"Clipped label scenario={scenario} text={label.text} content={label.contentRect} measured={size}");
            }
            foreach(string name in new[]{"create","createRetry","listen","raise","lower","invite","audience","confirmEnd","reconnect","mic","refresh"})
                foreach(var control in root.Query<Button>("spaces-"+name).ToList())Need(!control.enabledInHierarchy,"Fixture enabled unsafe action "+name);
        }
        private static bool Visible(VisualElement element)
        {
            for(var p=element;p!=null;p=p.parent)if(p.resolvedStyle.display==DisplayStyle.None || p.resolvedStyle.visibility==Visibility.Hidden)return false;
            return element.panel!=null;
        }
        private static async Task Settle(VisualElement root,CancellationToken ct)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+2; string previous=null; int stable=0;
            do
            {
                await Awaitable.EndOfFrameAsync(ct); await Awaitable.NextFrameAsync(ct);
                string sample=root.worldBound+"|"+string.Join("|",root.Query<Button>().ToList().Select(b=>b.worldBound.ToString()));
                stable=sample==previous?stable+1:0; previous=sample; if(stable>=2)return;
            }while(Time.realtimeSinceStartupAsDouble<deadline);
            throw new InvalidOperationException("Spaces review layout did not settle within two seconds");
        }
        private static async Task Frames(int count,CancellationToken ct) { for(int i=0;i<count;i++)await Awaitable.NextFrameAsync(ct); }
        private static async Task Capture(string directory,string name,Receipt receipt,CancellationToken ct)
        {
            await Awaitable.EndOfFrameAsync(ct); var texture=ScreenCapture.CaptureScreenshotAsTexture(); Need(texture!=null,"Native capture failed");
            try { File.WriteAllBytes(Path.Combine(directory,name+".png"),texture.EncodeToPNG()); } finally { UnityEngine.Object.Destroy(texture); }
            receipt.captures.Add(name+".png");
        }
        private static void Need(bool value,string message) { if(!value)throw new InvalidOperationException("Spaces UI review: "+message); }
    }
}
#endif
