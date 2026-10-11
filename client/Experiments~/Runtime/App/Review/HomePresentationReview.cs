#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.App.Review
{
    /// <summary>Actual Home/phone UI against an in-memory transport and isolated non-persisted identity.</summary>
    public static class HomePresentationReview
    {
        private const string PlayerA="00000000-0000-4000-8000-000000000071", PlayerB="00000000-0000-4000-8000-000000000072";
        [Serializable] private sealed class Receipt
        {
            public bool pass, servicesRestored;
            public string phase, failure;
            public int requests, runtimeErrors;
            public string touchGate="Authored resolved min-height >=124 panel units; rendered width/height >=floor(124*observed screen/panel scale) physical pixels, epsilon0.001px. Text clipping gate unchanged.";
            public double timeoutSeconds;
            public string limits="Watermarked actual Home/phone UI and NavigationSubmit callbacks with controlled IRestClient only. No HTTP, economic writes, session persistence, wallet or microphone. Real 30s client cancellation is measured; this is not server settlement, network abort, physical touch or Android lifecycle certification.";
            public List<string> checks=new List<string>(), captures=new List<string>(), geometry=new List<string>();
        }
        private sealed class ReviewApp:PhoneApp
        {
            private readonly HomeApp inner=new HomeApp();
            public VisualElement Page;
            public override string Id=>"home-native-review";
            public override string Title=>Strings.T("app.home");
            public override VisualElement Open()
            {
                Page=inner.Open();
                var watermark=El.Text("REVIEW · LOCAL FIXTURES · NO PAYOUT", "ct-small ct-muted");
                watermark.name="home-review-watermark";watermark.enableRichText=false;Page.Insert(0,watermark);
                return Page;
            }
            public override void Closed()=>inner.Closed();
        }
        private sealed class Saved<T>:IDisposable where T:class
        {
            private readonly T original;private readonly bool existed;
            public Saved(T replacement){existed=Services.TryGet(out original);Services.Register(replacement);}
            public void Dispose(){if(existed)Services.Register(original);else Services.Unregister<T>();}
            public bool Restored(){bool present=Services.TryGet<T>(out var current);return existed?present&&ReferenceEquals(original,current):!present;}
        }
        private sealed class Pending:IDisposable
        {
            public readonly CancellationToken Token;
            public readonly string Action;
            public readonly TaskCompletionSource<PostNeedsAction200Response> Completion=new TaskCompletionSource<PostNeedsAction200Response>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly CancellationTokenRegistration registration;
            public Pending(string action,CancellationToken token,bool honorCancellation)
            {
                Action=action;Token=token;
                if(honorCancellation)registration=token.Register(()=>Completion.TrySetCanceled());
            }
            public void Succeed(int hunger,long cash)=>Completion.TrySetResult(new PostNeedsAction200Response(new Needs(50,hunger,50,50,50),cash));
            public void Dispose(){Completion.TrySetCanceled();registration.Dispose();}
        }
        private sealed class Transport:IRestClient,IDisposable
        {
            public Func<Task<bool>> RefreshHandler{get;set;}
            public Me Profile;
            public Transport(Me template) { Profile=ProfileFor(PlayerA,template); }
            public readonly List<Pending> Calls=new List<Pending>();
            public bool HonorCancellation;
            public Task<T> Get<T>(string path,CancellationToken ct=default)
            {
                ct.ThrowIfCancellationRequested();
                if(path!="/me"||typeof(T)!=typeof(Me))throw new InvalidOperationException("Unexpected fixture read.");
                return Task.FromResult((T)(object)Profile);
            }
            public Task<T> Post<T>(string path,object body=null,bool auth=true,string idempotencyKey=null,CancellationToken ct=default)
            {
                ct.ThrowIfCancellationRequested();
                if(path!="/needs/actions"||typeof(T)!=typeof(PostNeedsAction200Response))throw new InvalidOperationException("Unexpected fixture mutation.");
                var json=JObject.Parse(body as string??throw new InvalidOperationException("Unexpected needs body."));
                var call=new Pending(json.Value<string>("actionId"),ct,HonorCancellation);Calls.Add(call);
                return Cast<T>(call.Completion.Task);
            }
            private static async Task<T> Cast<T>(Task<PostNeedsAction200Response> task)=>(T)(object)await task;
            private static Task<T> Unexpected<T>()=>Task.FromException<T>(new InvalidOperationException("Transport operation not permitted by this review."));
            public Task<T> Put<T>(string path,object body,CancellationToken ct=default)=>Unexpected<T>();
            public Task<T> Patch<T>(string path,object body,CancellationToken ct=default)=>Unexpected<T>();
            public Task<T> Delete<T>(string path,CancellationToken ct=default)=>Unexpected<T>();
            public Task<T> Send<T>(string method,string path,object body=null,bool auth=true,string idempotencyKey=null,CancellationToken ct=default)=>Unexpected<T>();
            public Task<RawResponse> SendRaw(string method,string path,string jsonBody,bool auth,string idempotencyKey,string ifNoneMatch,CancellationToken ct)=>Unexpected<RawResponse>();
            public void Dispose(){foreach(var call in Calls)call.Dispose();}
        }
        public static async Task Run(PhoneShell originalPhone,string directory,CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!Path.IsPathRooted(directory))directory=Path.Combine(Application.persistentDataPath,directory);
#endif
            directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(owner);timeout.CancelAfter(TimeSpan.FromSeconds(90));
            var ct=timeout.Token;var receipt=new Receipt();Transport transport=null;PhoneShell phone=null;ReviewApp app=null;
            Saved<Session> savedSession=null;Saved<ConfigStore> savedConfig=null;Saved<Api> savedApi=null;Saved<PlayerProfile> savedProfile=null;
            void OnLog(string message,string stack,LogType kind){if(kind==LogType.Exception||kind==LogType.Error||kind==LogType.Assert)receipt.runtimeErrors++;}
            Application.logMessageReceived+=OnLog;
            try
            {
                originalPhone.Close();
                receipt.phase="fixture-construction";
                var profileTemplate=Services.Get<PlayerProfile>().Me;
                transport=new Transport(profileTemplate);
                var config=new ConfigStore();var snapshot=Resources.Load<TextAsset>("Contract/config_snapshot");Need(snapshot!=null,"Bundled configuration missing");
                config.Apply(snapshot.text,null,persist:false);
                var session=new Session();SetIdentityWithoutPersistence(session,PlayerA);
                var api=new Api(transport,config);var profile=new PlayerProfile(api);
                savedSession=new Saved<Session>(session);savedConfig=new Saved<ConfigStore>(config);savedApi=new Saved<Api>(api);savedProfile=new Saved<PlayerProfile>(profile);
                await profile.Refresh(ct);
                int initialSubscribers=SubscriberCount();phone=new PhoneShell(UiRoot.Instance.PhoneLayer);app=new ReviewApp();phone.Register(app);
                Need(SubscriberCount()==initialSubscribers+1,"Native shell subscription missing");
                receipt.phase="actual-config";phone.Open(app.Id);await Frames(5,ct);Inspect(app.Page,receipt);
                Need(app.Page.Query<Button>().ToList().Count==4,"Actual configuration did not render four actions");
                Need(app.Page.Query<Button>().ToList().All(x=>x.enabledInHierarchy),"Isolated owner controls disabled");
                await Capture(directory,"home-ready",receipt,ct);
                receipt.phase="duplicate-and-child-detach";
                await Activate(app.Page,"home-action-eat",ct);Need(transport.Calls.Count==1,"First actual callback missing");
                Submit(app.Page.Q<Button>("home-action-eat"));Submit(app.Page.Q<Button>("home-action-sleep"));await Frames(3,ct);
                Need(transport.Calls.Count==1,"Duplicate callback sent another fixture request");
                var child=El.Text("temporary child", "ct-small");app.Page.Add(child);await Frames(2,ct);child.RemoveFromHierarchy();await Frames(2,ct);
                Need(!transport.Calls[0].Token.IsCancellationRequested,"Child detach cancelled Home owner");
                transport.Calls[0].Succeed(71,12345);await Until(()=>app.Page.Q<Button>("home-action-eat").enabledInHierarchy,3,ct);
                Need(profile.Me.Needs.Hunger==71&&profile.CashCents==12345,"Confirmed fixture receipt not applied");
                Inspect(app.Page,receipt);await Capture(directory,"home-confirmed",receipt,ct);
                receipt.checks.Add("PASS actual snapshot, four native controls/text, real action callbacks, duplicate input gate, child detach and confirmed generated-model receipt.");

                receipt.phase="close-reopen-late";await Activate(app.Page,"home-action-eat",ct);var late=transport.Calls.Last();phone.Close();
                Need(late.Token.IsCancellationRequested,"Phone close did not cancel request token");phone.Open(app.Id);await Frames(3,ct);
                Need(!app.Page.Q<Button>("home-action-eat").enabledInHierarchy,"Reopen overlapped cancelled pending request");
                late.Succeed(99,99999);await Until(()=>app.Page.Q<Button>("home-action-eat").enabledInHierarchy,3,ct);
                Need(profile.Me.Needs.Hunger==71&&profile.CashCents==12345,"Closed response changed shared profile");

                receipt.phase="identity-change-late";await Activate(app.Page,"home-action-eat",ct);late=transport.Calls.Last();
                SetIdentityWithoutPersistence(session,PlayerB);NotifyIdentityChanged(session);Need(late.Token.IsCancellationRequested,"Identity change left request active");
                transport.Profile=ProfileFor(PlayerB,profileTemplate);await profile.Refresh(ct);late.Succeed(99,99999);await Frames(4,ct);
                Need(profile.UserId==PlayerB&&profile.CashCents==1000&&profile.Me.Needs.Hunger==50,"Old player receipt changed new player");
                phone.Open(app.Id);await Frames(3,ct);Need(app.Page.Q<Button>("home-action-eat").enabledInHierarchy,"New identity cannot recover view");
                receipt.checks.Add("PASS close/reopen while pending; late non-cooperative response dropped; identity change cancels and old response cannot alter new profile.");

                receipt.phase="root-detach";await Activate(app.Page,"home-action-eat",ct);late=transport.Calls.Last();app.Page.RemoveFromHierarchy();
                Need(late.Token.IsCancellationRequested,"External root detach left request active");late.Succeed(99,99999);await Frames(4,ct);
                Need(profile.CashCents==1000&&profile.Me.Needs.Hunger==50,"Root-detached response changed profile");phone.Open(app.Id);await Frames(3,ct);

                receipt.phase="actual-30-second-timeout";transport.HonorCancellation=true;double started=Time.realtimeSinceStartupAsDouble;
                await Activate(app.Page,"home-action-eat",ct);var timed=transport.Calls.Last();int calls=transport.Calls.Count;
                await Until(()=>timed.Token.IsCancellationRequested&&app.Page.Q<Button>("home-action-eat").enabledInHierarchy,40,ct);
                receipt.timeoutSeconds=Time.realtimeSinceStartupAsDouble-started;
                Need(receipt.timeoutSeconds>=29&&receipt.timeoutSeconds<40,"Actual timeout outside bounded window");
                Need(transport.Calls.Count==calls&&profile.CashCents==1000,"Timeout repeated mutation or applied unconfirmed cash");
                Need(app.Page.Query<Label>().ToList().Any(x=>x.text=="That action wasn't confirmed. Your needs may have changed."),"Timeout falsely claimed known failure/success");
                ((ScrollView)app.Page).ScrollTo(app.Page.Children().Last());await Frames(4,ct);
                Inspect(app.Page,receipt);await Capture(directory,"home-unconfirmed-timeout",receipt,ct);
                receipt.checks.Add("PASS actual30second Home request cancellation, controls recover, uncertainty copy, zero automatic UI replay.");

                receipt.phase="malformed-config";phone.Close();var malformed=JObject.Parse(snapshot.text);((JArray)malformed["config"]["needs"]["actions"]).RemoveAt(0);
                config.Apply(malformed.ToString(),null,persist:false);phone.Open(app.Id);await Frames(4,ct);Inspect(app.Page,receipt);
                Need(app.Page.Query<Button>().ToList().Count==0,"Missing required action fabricated clickable controls");await Capture(directory,"home-config-unavailable",receipt,ct);
                config.Apply(snapshot.text,null,persist:false);phone.Open(app.Id);await Frames(4,ct);Need(app.Page.Query<Button>().ToList().Count==4,"Valid configuration reopen failed");
                phone.Dispose();Need(SubscriberCount()==initialSubscribers,"Disposed native phone retained subscription");phone.Dispose();
                for(int cycle=0;cycle<3;cycle++)
                {
                    phone=new PhoneShell(UiRoot.Instance.PhoneLayer);app=new ReviewApp();phone.Register(app);
                    phone.Open(app.Id);await Frames(3,ct);Need(phone.IsOpen,"Replacement shell failed to open");
                    phone.Close();phone.Open(app.Id);await Frames(3,ct);Need(phone.IsOpen,"Reusable native Close/reopen failed");
                    phone.Dispose();phone.Dispose();phone.Open(app.Id);
                    Need(!phone.IsOpen&&SubscriberCount()==initialSubscribers,"Disposed native shell reopened or retained a subscription");
                }
                receipt.checks.Add("PASS missing-entry unavailable view and valid-config reopen; three real-shell replace/close/reopen/repeated-dispose cycles restore subscription counts and reject disposed reopen.");
                Need(transport.Calls.Count==5,"Unexpected fixture request count");
                Need(receipt.runtimeErrors==0,"Unexpected native runtime error logged");receipt.pass=true;
                Log.Info("home-review","PASS native Home input/lifecycle/30second timeout and phone disposal checks; isolated fixtures only.");
            }
            catch(Exception error)
            {
                receipt.failure=error.Message;
                try{await Capture(directory,"home-failure-"+receipt.phase,receipt,CancellationToken.None);}catch{}
                throw;
            }
            finally
            {
                try{phone?.Dispose();}finally
                {
                    transport?.Dispose();savedProfile?.Dispose();savedApi?.Dispose();savedConfig?.Dispose();savedSession?.Dispose();
                    receipt.servicesRestored=(savedProfile?.Restored()??true)&&(savedApi?.Restored()??true)
                        &&(savedConfig?.Restored()??true)&&(savedSession?.Restored()??true);
                    if(!receipt.servicesRestored){receipt.pass=false;receipt.failure="Original services were not restored.";}
                    receipt.requests=transport?.Calls.Count??0;
                    Application.logMessageReceived-=OnLog;
                    Bus.Publish(new ProfileChangedSignal());
                    File.WriteAllText(Path.Combine(directory,"home-review.json"),JsonUtility.ToJson(receipt,true));
                    Need(receipt.servicesRestored,"Original services were not restored");
                }
            }
        }
        // Only private auto-property setters are used; Session.SetTokens/Clear would write PlayerPrefs.
        private static void SetIdentityWithoutPersistence(Session session,string id)
        {
            typeof(Session).GetProperty(nameof(Session.UserId)).SetValue(session,id);
            typeof(Session).GetProperty(nameof(Session.AccessToken)).SetValue(session,"isolated-review-not-a-token");
        }
        private static void NotifyIdentityChanged(Session session)
        {
            var field=typeof(Session).GetField("Changed",BindingFlags.Instance|BindingFlags.NonPublic);
            Need(field!=null,"Session event backing field unavailable");((Action)field.GetValue(session))?.Invoke();
        }
        private static Me ProfileFor(string id,Me template)
        {
            if(template?.Character?.Look==null||template.HomeId==null)throw new InvalidOperationException("Complete local review profile template required.");
            var source=template.Character;var look=source.Look;
            return new Me(new User(Guid.Parse(id),"isolated-review",false,true),
                new Character("Home review",new Look(look.SkinTone,look.HeadId,look.HairId,look.TopId,look.BottomId,look.ShoesId,look.AccId),
                    source.TribeId,new List<string>(source.TraitIds),source.DreamId),
                new Needs(50,50,50,50,50),new Skills(1,1,1,1,1,1),1000,0,template.HomeId,
                flags:new Dictionary<string,object>{{"lotteryRolled",true}});
        }
        private static int SubscriberCount()
        {
            var field=typeof(Bus).GetField("_handlers",BindingFlags.Static|BindingFlags.NonPublic);
            var handlers=(Dictionary<Type,Delegate>)field.GetValue(null);
            return handlers.TryGetValue(typeof(DegradedChangedSignal),out var handler)?handler.GetInvocationList().Length:0;
        }
        private static async Task Activate(VisualElement page,string name,CancellationToken ct)
        {
            var button=page.Q<Button>(name);Need(button!=null&&button.enabledInHierarchy,"Missing/disabled action "+name);
            var scroll=(ScrollView)page;scroll.ScrollTo(button);await Frames(4,ct);var bounds=button.worldBound;var area=scroll.contentViewport.worldBound;
            Need(TouchSize(button)&&bounds.xMin>=area.xMin-2&&bounds.xMax<=area.xMax+2&&bounds.yMin>=area.yMin-2&&bounds.yMax<=area.yMax+2,"Clipped/undersized action "+Geometry(button)+" viewport="+area);
            Submit(button);await Frames(3,ct);
        }
        private static void Submit(Button button){using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
        private static void Inspect(VisualElement page,Receipt receipt)
        {
            Need(page?.panel!=null&&page.Q("home-review-watermark")!=null,"Home fixture detached/unmarked");
            foreach(var button in page.Query<Button>().ToList())
            {
                string detail=Geometry(button);receipt.geometry.Add(receipt.phase+": "+detail);
                Need(TouchSize(button),"Undersized Home action "+detail);
                var size=button.MeasureTextSize(button.text,button.contentRect.width,VisualElement.MeasureMode.AtMost,0,VisualElement.MeasureMode.Undefined);
                Need(size.x<=button.contentRect.width+2&&size.y<=button.contentRect.height+2,"Clipped Home button "+button.name+" content="+button.contentRect+" measured="+size);
            }
            foreach(var label in page.Query<Label>().ToList())
            {
                if(string.IsNullOrEmpty(label.text))continue;
                var measured=label.MeasureTextSize(label.text,label.contentRect.width,VisualElement.MeasureMode.AtMost,0,VisualElement.MeasureMode.Undefined);
                Need(measured.x<=label.contentRect.width+2&&measured.y<=label.contentRect.height+2,"Clipped Home label "+label.text+" content="+label.contentRect+" measured="+measured);
            }
        }
        // Native v3 measured min-height124
        // as137px at1.11180556px/unit (ideal137.864px). Accept only the floored physical target;
        // retain the authored minimum and a0.001physical-pixel numerical epsilon, not a layout allowance.
        private static bool TouchSize(Button button)
        {
            float scale=Screen.width/button.panel.visualTree.worldBound.width;
            return HomeTouchTarget.Meets(button.worldBound.width,button.worldBound.height,button.resolvedStyle.minHeight.value,scale);
        }
        private static string Geometry(Button button)
        {
            var rect=button.worldBound;var content=button.contentRect;var style=button.resolvedStyle;
            var doc=UiRoot.Instance.GetComponent<UIDocument>();var settings=doc!=null?doc.panelSettings:null;
            float panelWidth=button.panel.visualTree.worldBound.width;
            return button.name+" worldWidth="+rect.width.ToString("R")+" worldHeight="+rect.height.ToString("R")
                +" layoutHeight="+button.layout.height.ToString("R")+" resolvedHeight="+style.height.ToString("R")
                +" resolvedMinHeight="+style.minHeight.ToString()+" contentWidth="+content.width.ToString("R")
                +" contentHeight="+content.height.ToString("R")+" panelWidth="+panelWidth.ToString("R")
                +" screen="+Screen.width+"x"+Screen.height+" screenToPanel="+(Screen.width/panelWidth).ToString("R")
                +" actualWidthPx="+(rect.width*Screen.width/panelWidth).ToString("R")
                +" actualHeightPx="+(rect.height*Screen.width/panelWidth).ToString("R")
                +" minimumPixels="+Math.Floor(124d*Screen.width/panelWidth).ToString("R")
                +" settingsScale="+(settings!=null?settings.scale.ToString("R"):"missing")
                +" reference="+(settings!=null?settings.referenceResolution.ToString():"missing");
        }
        private static async Task Until(Func<bool> predicate,double seconds,CancellationToken ct)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+seconds;
            while(!predicate()){if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("Home native predicate timed out");await Awaitable.NextFrameAsync(ct);}
        }
        private static async Task Frames(int count,CancellationToken ct){for(int i=0;i<count;i++)await Awaitable.NextFrameAsync(ct);}
        private static async Task Capture(string directory,string name,Receipt receipt,CancellationToken ct)
        {
            await Awaitable.EndOfFrameAsync(ct);var texture=ScreenCapture.CaptureScreenshotAsTexture();Need(texture!=null,"Capture missing");
            try{File.WriteAllBytes(Path.Combine(directory,name+".png"),texture.EncodeToPNG());receipt.captures.Add(name+".png");}finally{UnityEngine.Object.Destroy(texture);}
        }
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException("Home review: "+message);}
    }
}
#endif
