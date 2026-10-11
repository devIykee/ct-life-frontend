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
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.App.Review
{
    /// <summary>Read-only catalog review. The transport rejects all mutations; no Apply/Work button is activated.</summary>
    public static class JobsCatalogPresentationReview
    {
        private const string PlayerA="00000000-0000-4000-8000-000000000073",PlayerB="00000000-0000-4000-8000-000000000074";
        private const string ExpectedFailure="JOBS_REVIEW_EXPECTED_READ_FAILURE";
        [Serializable] private sealed class Receipt
        {
            public bool pass,servicesRestored;
            public string phase,failure;
            public int reads,mutationAttempts,expectedFixtureErrors,expectedFixtureErrorSummaries,expectedFixtureExceptions,unexpectedRuntimeErrors;
            public double timeoutSeconds;
            public string limits="Watermarked actual registered Jobs/Phone UI and NavigationSubmit Retry callbacks using a local controlled IRestClient. No HTTP, wallet, persisted session, Apply/Work or economic calls. Actual30s catalog deadline is measured; real network cancellation, backend availability and physical touch remain separate acceptance.";
            public List<string> checks=new List<string>(),captures=new List<string>();
        }
        private sealed class Saved<T>:IDisposable where T:class
        {
            private readonly T original;private readonly bool existed;
            public Saved(T replacement){existed=Services.TryGet(out original);Services.Register(replacement);}
            public void Dispose(){if(existed)Services.Register(original);else Services.Unregister<T>();}
            public bool Restored(){bool present=Services.TryGet<T>(out var current);return existed?present&&ReferenceEquals(original,current):!present;}
        }
        private sealed class Pending
        {
            public readonly CancellationToken Token;
            // Deliberately non-cooperative: validates the UI deadline and stale-response boundary.
            public readonly TaskCompletionSource<List<GetCareers200ResponseInner>> Completion=new TaskCompletionSource<List<GetCareers200ResponseInner>>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Pending(CancellationToken token){Token=token;}
            public void Empty()=>Completion.TrySetResult(new List<GetCareers200ResponseInner>());
            public void Career(string name)=>Completion.TrySetResult(new List<GetCareers200ResponseInner>{new GetCareers200ResponseInner("discord_mod",name,1,0,false)});
        }
        private sealed class Transport:IRestClient,IDisposable
        {
            public Func<Task<bool>> RefreshHandler{get;set;}
            public readonly List<Pending> Calls=new List<Pending>();public int Mutations;
            public Task<T> Get<T>(string path,CancellationToken ct=default)
            {
                ct.ThrowIfCancellationRequested();
                if(path!="/careers"||typeof(T)!=typeof(List<GetCareers200ResponseInner>))throw new InvalidOperationException("Unexpected Jobs fixture read.");
                var pending=new Pending(ct);Calls.Add(pending);return Cast<T>(pending.Completion.Task);
            }
            private static async Task<T> Cast<T>(Task<List<GetCareers200ResponseInner>> task)=>(T)(object)await task;
            private Task<T> Reject<T>(){Mutations++;return Task.FromException<T>(new InvalidOperationException("Jobs catalog review forbids economic/transport writes."));}
            public Task<T> Post<T>(string path,object body=null,bool auth=true,string idempotencyKey=null,CancellationToken ct=default)=>Reject<T>();
            public Task<T> Put<T>(string path,object body,CancellationToken ct=default)=>Reject<T>();
            public Task<T> Patch<T>(string path,object body,CancellationToken ct=default)=>Reject<T>();
            public Task<T> Delete<T>(string path,CancellationToken ct=default)=>Reject<T>();
            public Task<T> Send<T>(string method,string path,object body=null,bool auth=true,string idempotencyKey=null,CancellationToken ct=default)=>Reject<T>();
            public Task<RawResponse> SendRaw(string method,string path,string jsonBody,bool auth,string idempotencyKey,string ifNoneMatch,CancellationToken ct)=>Reject<RawResponse>();
            public void Dispose(){foreach(var call in Calls)call.Completion.TrySetCanceled();}
        }
        public static async Task Run(PhoneShell originalPhone,string directory,CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!Path.IsPathRooted(directory))directory=Path.Combine(Application.persistentDataPath,directory);
#endif
            directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(owner);timeout.CancelAfter(TimeSpan.FromSeconds(80));
            var ct=timeout.Token;var receipt=new Receipt();Transport transport=null;PhoneShell phone=null;JobsApp app=null;VisualElement page=null;
            Saved<Session> savedSession=null;Saved<ConfigStore> savedConfig=null;Saved<Api> savedApi=null;
            void OnLog(string message,string stack,LogType kind)
            {
                if(kind!=LogType.Exception&&kind!=LogType.Error&&kind!=LogType.Assert)return;
                if(receipt.phase=="finite-error"&&message.Contains(ExpectedFailure))
                {
                    // Core.Log.Exception emits one summary Error plus one Exception for a single failure.
                    if(kind==LogType.Error){receipt.expectedFixtureErrorSummaries++;receipt.expectedFixtureErrors++;}
                    else if(kind==LogType.Exception){receipt.expectedFixtureExceptions++;receipt.expectedFixtureErrors++;}
                    else receipt.unexpectedRuntimeErrors++;
                }
                else receipt.unexpectedRuntimeErrors++;
            }
            Application.logMessageReceived+=OnLog;
            try
            {
                originalPhone.Close();receipt.phase="fixture-construction";
                var snapshot=Resources.Load<TextAsset>("Contract/config_snapshot");Need(snapshot!=null,"Bundled config missing");
                var config=new ConfigStore();config.Apply(snapshot.text,null,persist:false);
                var session=new Session();Identity(session,PlayerA);transport=new Transport();var api=new Api(transport,config);
                savedSession=new Saved<Session>(session);savedConfig=new Saved<ConfigStore>(config);savedApi=new Saved<Api>(api);
                phone=new PhoneShell(UiRoot.Instance.PhoneLayer);app=new JobsApp();phone.Register(app);
                int sessionBaseline=SubscriberCount(session);
                VisualElement Open()
                {
                    phone.Open(app.Id);var view=UiRoot.Instance.PhoneLayer.Q(className:"ct-app-page");Need(view!=null,"Jobs page missing");
                    var watermark=El.Text("REVIEW · LOCAL CATALOG · NO ORDERS", "ct-small ct-muted");watermark.enableRichText=false;watermark.name="jobs-review-watermark";view.Insert(0,watermark);return view;
                }
                receipt.phase="loading-and-child-detach";page=Open();await Frames(4,ct);
                Need(transport.Calls.Count==1&&SubscriberCount(session)==sessionBaseline+1,"Catalog read/subscription missing");
                Need(page.Query<Button>().ToList().Count==0,"Loading exposed an economic action");
                var first=transport.Calls[0];var child=El.Text("temporary child","ct-small");page.Add(child);await Frames(2,ct);child.RemoveFromHierarchy();await Frames(2,ct);
                Need(!first.Token.IsCancellationRequested,"Child detach cancelled catalog");Inspect(page);await Capture(directory,"jobs-loading",receipt,ct);
                receipt.phase="close-reopen-late";phone.Close();Need(first.Token.IsCancellationRequested,"Close did not cancel catalog token");
                page=Open();await Frames(3,ct);Need(transport.Calls.Count==2,"Reopen did not start one new read");
                first.Career("STALE CATALOG MUST NOT APPEAR");await Frames(4,ct);
                Need(!page.Query<Label>().ToList().Any(x=>x.text.Contains("STALE CATALOG")),"Late closed response wrote reopened page");
                transport.Calls[1].Empty();await Until(()=>SubscriberCount(session)==sessionBaseline,3,ct,"empty completion cleanup");await Frames(3,ct);
                Need(page.Query<Button>().ToList().Count==0,"Empty response fabricated a career");Inspect(page);await Capture(directory,"jobs-empty",receipt,ct);
                receipt.checks.Add("PASS actual Jobs loading/empty UI; child detach preserved read; close/reopen cancelled old token; late response ignored; session subscriber baseline restored.");

                receipt.phase="root-detach";page=Open();await Frames(3,ct);var detached=transport.Calls.Last();page.RemoveFromHierarchy();
                Need(detached.Token.IsCancellationRequested,"Root detach did not cancel catalog");detached.Empty();await Until(()=>SubscriberCount(session)==sessionBaseline,3,ct,"root detach cleanup");
                receipt.phase="identity-change";page=Open();await Frames(3,ct);var previous=transport.Calls.Last();Identity(session,PlayerB);NotifyIdentity(session);
                Need(previous.Token.IsCancellationRequested,"Identity change did not cancel catalog");previous.Career("OLD IDENTITY CATALOG");await Frames(4,ct);
                Need(!page.Query<Label>().ToList().Any(x=>x.text.Contains("OLD IDENTITY")),"Changed identity response wrote page");
                await Until(()=>SubscriberCount(session)==sessionBaseline,3,ct,"identity cleanup");
                receipt.checks.Add("PASS actual root detach and changed identity cancel catalog; late response cannot populate obsolete view.");

                receipt.phase="actual30second-deadline";double started=Time.realtimeSinceStartupAsDouble;page=Open();await Frames(3,ct);var stalled=transport.Calls.Last();int readCount=transport.Calls.Count;
                await Until(()=>page.Q<Button>("jobs-catalog-retry")!=null,40,ct,"catalog30second timeout");
                receipt.timeoutSeconds=Time.realtimeSinceStartupAsDouble-started;
                Need(receipt.timeoutSeconds>=29&&receipt.timeoutSeconds<40&&stalled.Token.IsCancellationRequested,"Catalog deadline/token boundary incorrect");
                Need(transport.Calls.Count==readCount,"Catalog timeout silently retried");Need(page.Query<Label>().ToList().Any(x=>x.text=="Jobs are taking too long to load. Try again."),"Timeout recovery copy missing");
                Need(SubscriberCount(session)==sessionBaseline,"Timed-out catalog retained identity subscription");Inspect(page);await Capture(directory,"jobs-timeout",receipt,ct);
                var staleRetry=page.Q<Button>("jobs-catalog-retry");await Activate(page,staleRetry,ct);Submit(staleRetry);await Frames(3,ct);
                Need(transport.Calls.Count==readCount+1,"Retry/repeated stale callback started multiple reads");
                stalled.Career("TIMED OUT CATALOG");await Frames(3,ct);Need(!page.Query<Label>().ToList().Any(x=>x.text.Contains("TIMED OUT")),"Timed-out response replaced retry");
                transport.Calls.Last().Empty();await Until(()=>SubscriberCount(session)==sessionBaseline,3,ct,"retry completion");
                receipt.checks.Add("PASS actual30second deadline for non-cooperative GET; cancellation, explicit Retry, no automatic replay, old response ignored.");

                receipt.phase="finite-error";page=Open();await Frames(3,ct);transport.Calls.Last().Completion.TrySetException(new ApiException(0,"NETWORK_ERROR",ExpectedFailure,isNetwork:true));
                await Until(()=>page.Q<Button>("jobs-catalog-retry")!=null,3,ct,"finite API error recovery");await Frames(3,ct);
                Need(receipt.expectedFixtureErrorSummaries==1&&receipt.expectedFixtureExceptions==1&&receipt.expectedFixtureErrors==2,"Expected one API failure summary Error and one Exception; summaries="+receipt.expectedFixtureErrorSummaries+" exceptions="+receipt.expectedFixtureExceptions);Inspect(page);await Capture(directory,"jobs-read-error",receipt,ct);
                await Activate(page,page.Q<Button>("jobs-catalog-retry"),ct);receipt.phase="recovered-catalog";
                transport.Calls.Last().Career("Review moderation desk");await Until(()=>page.Query<Label>().ToList().Any(x=>x.text=="Review moderation desk"),3,ct,"successful retry display");await Frames(3,ct);Inspect(page);await Capture(directory,"jobs-recovered",receipt,ct);
                Need(page.Query<Button>().ToList().Count==1,"Expected one catalog card action");
                // Inspecting this button does not invoke Apply/Work. Every transport mutation remains forbidden.
                receipt.checks.Add("PASS finite read error is visibly recoverable through actual Retry; its existing expected error log is retained/countable; catalog card rendered without invoking Apply or Work.");
                phone.Dispose();phone.Dispose();Need(SubscriberCount(session)==sessionBaseline,"Disposed catalog owner retained identity subscription");
                Need(transport.Mutations==0&&receipt.unexpectedRuntimeErrors==0,"Unexpected mutation/native error");receipt.pass=true;
                Log.Info("jobs-catalog-review","PASS local catalog lifecycle/actual30second timeout/Retry; zero economic calls.");
            }
            catch(Exception error)
            {
                receipt.failure=error.Message;try{await Capture(directory,"jobs-failure-"+receipt.phase,receipt,CancellationToken.None);}catch{}throw;
            }
            finally
            {
                try{phone?.Dispose();}finally
                {
                    transport?.Dispose();savedApi?.Dispose();savedConfig?.Dispose();savedSession?.Dispose();
                    receipt.servicesRestored=(savedApi?.Restored()??true)&&(savedConfig?.Restored()??true)&&(savedSession?.Restored()??true);
                    receipt.reads=transport?.Calls.Count??0;receipt.mutationAttempts=transport?.Mutations??0;
                    if(!receipt.servicesRestored){receipt.pass=false;receipt.failure="Original services not restored.";}
                    Application.logMessageReceived-=OnLog;
                    File.WriteAllText(Path.Combine(directory,"jobs-catalog-review.json"),JsonUtility.ToJson(receipt,true));
                    Need(receipt.servicesRestored,"Original services not restored");
                }
            }
        }
        private static void Identity(Session session,string id)
        {
            // Private in-memory setters only. SetTokens/Clear would persist PlayerPrefs and are forbidden here.
            typeof(Session).GetProperty(nameof(Session.UserId)).SetValue(session,id);
            typeof(Session).GetProperty(nameof(Session.AccessToken)).SetValue(session,"isolated-review-not-a-token");
        }
        private static void NotifyIdentity(Session session)=>((Action)typeof(Session).GetField("Changed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session))?.Invoke();
        private static int SubscriberCount(Session session)=>((Action)typeof(Session).GetField("Changed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session))?.GetInvocationList().Length??0;
        private static void Submit(Button button){using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
        private static async Task Activate(VisualElement page,Button button,CancellationToken ct)
        {
            Need(button!=null&&button.enabledInHierarchy,"Retry missing/disabled");var scroll=(ScrollView)page;scroll.ScrollTo(button);await Frames(4,ct);
            var b=button.worldBound;var area=scroll.contentViewport.worldBound;
            Need(b.xMin>=area.xMin-2&&b.xMax<=area.xMax+2&&b.yMin>=area.yMin-2&&b.yMax<=area.yMax+2,"Retry outside viewport");
            Submit(button);await Frames(3,ct);
        }
        private static void Inspect(VisualElement page)
        {
            Need(page?.panel!=null&&page.Q("jobs-review-watermark")!=null,"Detached/unmarked Jobs fixture");
            float scale=Screen.width/page.panel.visualTree.worldBound.width;
            foreach(var button in page.Query<Button>().ToList())
            {
                var b=button.worldBound;Need(HomeTouchTarget.Meets(b.width,b.height,button.resolvedStyle.minHeight.value,scale),"Jobs control under authored/physical touch minimum: "+b+" min="+button.resolvedStyle.minHeight+" scale="+scale);
                var m=button.MeasureTextSize(button.text,button.contentRect.width,VisualElement.MeasureMode.AtMost,0,VisualElement.MeasureMode.Undefined);
                Need(m.x<=button.contentRect.width+2&&m.y<=button.contentRect.height+2,"Clipped Jobs button: "+button.text+" content="+button.contentRect+" measured="+m);
            }
            foreach(var label in page.Query<Label>().ToList())
            {
                if(string.IsNullOrEmpty(label.text))continue;
                var m=label.MeasureTextSize(label.text,label.contentRect.width,VisualElement.MeasureMode.AtMost,0,VisualElement.MeasureMode.Undefined);
                Need(m.x<=label.contentRect.width+2&&m.y<=label.contentRect.height+2,"Clipped Jobs label: "+label.text+" content="+label.contentRect+" measured="+m);
            }
        }
        private static async Task Until(Func<bool> predicate,double seconds,CancellationToken ct,string phase)
        {double end=Time.realtimeSinceStartupAsDouble+seconds;while(!predicate()){if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException("Jobs review timed out: "+phase);await Awaitable.NextFrameAsync(ct);}}
        private static async Task Frames(int count,CancellationToken ct){for(int i=0;i<count;i++)await Awaitable.NextFrameAsync(ct);}
        private static async Task Capture(string directory,string name,Receipt receipt,CancellationToken ct)
        {await Awaitable.EndOfFrameAsync(ct);var texture=ScreenCapture.CaptureScreenshotAsTexture();Need(texture!=null,"Native capture missing");try{File.WriteAllBytes(Path.Combine(directory,name+".png"),texture.EncodeToPNG());receipt.captures.Add(name+".png");}finally{UnityEngine.Object.Destroy(texture);}}
        private static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException("Jobs catalog review: "+message);}
    }
}
#endif
