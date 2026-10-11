#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.Rest.Model;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
namespace CTLife.App.Review
{
    /// <summary>Real Flow/PlayerProfile/UI lifecycle with isolated services and delayed local completions.</summary>
    public static class GameplayGenerationReview
    {
        [Serializable] private sealed class Receipt
        {
            public bool pass,servicesRestored,venueRestored;
            public string phase,failure;
            public int runtimeErrors,reads,mutations,disconnects,walletCallbacks,nativeDestroys;
            public List<string> checks=new List<string>();
            public string limits="Actual Flow Awake/OnDestroy, same-owner SignOut, real UI callback, PlayerProfile and room-slot ownership on Unity's context. Start is disabled. Isolated API/auth/realtime doubles; no HTTP, SDK connection, persisted tokens, wallet signing, microphone or economic writes. Current venue reference is temporarily isolated and restored: this does not certify VenueSession/Loader/Voice lifecycle, native socket closure, Android process lifecycle or physical touch.";
        }
        private sealed class Saved<T>:IDisposable where T:class
        {
            private readonly T original;private readonly bool existed;
            public Saved(T replacement){existed=Services.TryGet(out original);Services.Register(replacement);}
            public void Dispose(){if(existed)Services.Register(original);else Services.Unregister<T>();}
            public bool Restored()=>Services.TryGet<T>(out var current)?existed&&ReferenceEquals(current,original):!existed;
        }
        private sealed class Transport:IRestClient
        {
            public Func<Task<bool>> RefreshHandler{get;set;}
            public Task<Me> Response;public CancellationToken LastToken;public int Reads,Mutations;
            public async Task<T> Get<T>(string path,CancellationToken ct=default){Need(path=="/me"&&typeof(T)==typeof(Me),"Unexpected local read");Reads++;LastToken=ct;return (T)(object)await Response;}
            private Task<T> Reject<T>(){Mutations++;return Task.FromException<T>(new InvalidOperationException("Gameplay fixture forbids transport mutations"));}
            public Task<T> Post<T>(string path,object body=null,bool auth=true,string idempotencyKey=null,CancellationToken ct=default)=>Reject<T>();
            public Task<T> Put<T>(string path,object body,CancellationToken ct=default)=>Reject<T>();
            public Task<T> Patch<T>(string path,object body,CancellationToken ct=default)=>Reject<T>();
            public Task<T> Delete<T>(string path,CancellationToken ct=default)=>Reject<T>();
            public Task<T> Send<T>(string method,string path,object body=null,bool auth=true,string idempotencyKey=null,CancellationToken ct=default)=>Reject<T>();
            public Task<RawResponse> SendRaw(string method,string path,string jsonBody,bool auth,string idempotencyKey,string ifNoneMatch,CancellationToken ct)=>Reject<RawResponse>();
        }
        private sealed class Realtime:IRealtimeService
        {
            public CityChannel City{get;}=new CityChannel();public MarketChannel Market=>null;public VenueChannel Venue=>null;
            public event Action<VenueChannel> VenueJoined{add{}remove{}}public event Action VenueLeft{add{}remove{}}
            public Task MarketResponse=Task.CompletedTask;public CancellationToken CityToken,MarketToken;public int MarketCalls,Disconnects;
            public Task ConnectCity(CancellationToken ct=default){CityToken=ct;return Task.CompletedTask;}
            public Task ConnectMarket(CancellationToken ct=default){MarketToken=ct;MarketCalls++;return MarketResponse;}
            public Task<VenueChannel> JoinVenue(string id,CancellationToken ct=default)=>throw new InvalidOperationException("Fixture forbids venue joins");
            public Task LeaveVenue()=>throw new InvalidOperationException("Fixture forbids venue exits");
            public Task DisconnectAll(){Disconnects++;return Task.CompletedTask;}
        }
        private sealed class Auth:IAuthService
        {
            public bool DevAuthAllowed=>false;public int WalletCallbacks,SignOuts;
            public Task<AuthVerifyResponse> SignInWithWallet(IWalletSignIn wallet,CancellationToken ct=default){WalletCallbacks++;return Task.FromResult<AuthVerifyResponse>(null);}
            public Task<AuthVerifyResponse> SignInDev(string wallet,bool isSeeker=false,CancellationToken ct=default)=>throw new InvalidOperationException("Dev auth forbidden in fixture");
            public Task<bool> TryResume(CancellationToken ct=default)=>throw new InvalidOperationException("Resume forbidden in fixture");
            public Task<bool> Refresh()=>throw new InvalidOperationException("Auth refresh forbidden in fixture");
            public Task SignOut(IWalletSignIn wallet){SignOuts++;return Task.CompletedTask;}
        }
        private sealed class Wallet:IWalletSignIn
        {
            public string Name=>"isolated-review";public bool IsAvailable=>true;
            public Task<SiwsResponse> SignIn(SiwsRequest request,CancellationToken ct)=>throw new InvalidOperationException("Wallet signing forbidden in fixture");
            public Task SignOut()=>throw new InvalidOperationException("Wallet revocation forbidden in fixture");
        }
        public static async Task Run(string directory,CancellationToken owner)
        {
            directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(owner);timeout.CancelAfter(TimeSpan.FromSeconds(30));var ct=timeout.Token;
            var receipt=new Receipt();var saves=new List<IDisposable>();var restored=new List<Func<bool>>();
            GameObject root=null;FlowController flow=null;Task entering=null,logout=null;
            var originalVenue=VenueSession.Current;bool isolated=false;
            Transport transport=null;Realtime realtime=null;Auth auth=null;
            void Save<T>(T replacement)where T:class{var saved=new Saved<T>(replacement);saves.Add(saved);restored.Add(saved.Restored);}
            void OnLog(string message,string stack,LogType kind){if(kind==LogType.Error||kind==LogType.Exception||kind==LogType.Assert)receipt.runtimeErrors++;}
            Application.logMessageReceived+=OnLog;
            try
            {
                receipt.phase="fixture-setup";
                var template=Services.Get<PlayerProfile>().Me;Need(template?.Character?.Look!=null,"Complete local preview profile is required");
                var profileData=Clone(template);transport=new Transport{Response=Task.FromResult(profileData)};
                var config=Services.Get<ConfigStore>();var api=new Api(transport,config);var profile=new PlayerProfile(api);
                var session=new Session();typeof(Session).GetProperty(nameof(Session.UserId)).SetValue(session,profileData.User.Id.ToString());
                typeof(Session).GetProperty(nameof(Session.AccessToken)).SetValue(session,"local-review-placeholder");
                realtime=new Realtime();auth=new Auth();Save(session);Save(api);Save(profile);Save<IRealtimeService>(realtime);Save<IAuthService>(auth);Save<IWalletSignIn>(new Wallet());
                typeof(VenueSession).GetProperty(nameof(VenueSession.Current)).SetValue(null,null);isolated=true;
                receipt.phase="room-lease-context";await CTLife.Net.Review.GameplayLeaseReview.Run(receipt.checks.Add,ct);
                receipt.phase="kick-during-market";
                var market=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);realtime.MarketResponse=market.Task;
                root=new GameObject("ReviewGameplayGeneration");flow=root.AddComponent<FlowController>();flow.enabled=false;
                entering=(Task)Invoke(flow,"EnterGame");await Until(()=>realtime.MarketCalls==1,ct);
                var oldKick=Kick(realtime.City);Need(oldKick!=null,"Startup did not bind actual city kick event");oldKick(new KickedMessage{reasonId="local_review"});
                await Until(()=>Field<UiScreen>(flow,"_flowScreen") is SignInScreen,ct);
                Need(realtime.CityToken.IsCancellationRequested&&realtime.MarketToken.IsCancellationRequested,"Same-owner sign-out left gameplay tokens live");
                Need(Field<Hud>(flow,"_hud")==null&&Kick(realtime.City)==null,"Cancelled startup built HUD or retained kick");
                market.TrySetResult(true);await entering;await Frames(3,ct);
                Need(Field<Hud>(flow,"_hud")==null,"Late market completion published a HUD");
                receipt.checks.Add("PASS same still-live Flow signs out during held market join; both room tokens cancelled; kick removed; late successful completion cannot build HUD.");
                receipt.phase="wallet-callback-successor";realtime.MarketResponse=Task.CompletedTask;
                var signin=(SignInScreen)Field<UiScreen>(flow,"_flowScreen");Field<Action>(signin,"_onWallet")();
                await Until(()=>Field<Hud>(flow,"_hud")!=null,ct);var successorHud=Field<Hud>(flow,"_hud");var successorPhone=Field<PhoneShell>(flow,"_phone");
                oldKick(new KickedMessage{reasonId="stale_review"});await Frames(3,ct);
                Need(realtime.Disconnects==1&&auth.WalletCallbacks==1&&!realtime.CityToken.IsCancellationRequested,"Old kick touched successor or wallet callback repeated");
                var nativeToken=flow.destroyCancellationToken;UnityEngine.Object.Destroy(root);root=null;
                await Until(()=>nativeToken.IsCancellationRequested&&flow==null,ct);receipt.nativeDestroys++;
                Need(realtime.CityToken.IsCancellationRequested&&realtime.MarketToken.IsCancellationRequested&&successorHud.Element.parent==null,"Native destruction did not cancel gameplay/remove HUD");
                successorPhone.Open();Need(!successorPhone.IsOpen,"Destroyed flow phone reopened");flow=null;entering=null;
                receipt.checks.Add("PASS actual sign-in callback starts one fresh gameplay generation; captured old kick is inert; real Destroy cancels accepted generation and removes its phone/HUD.");
                receipt.phase="late-shared-profile";
                var originalMe=profile.Me;var pendingProfile=new TaskCompletionSource<Me>(TaskCreationOptions.RunContinuationsAsynchronously);transport.Response=pendingProfile.Task;
                root=new GameObject("ReviewGameplayProfile");flow=root.AddComponent<FlowController>();flow.enabled=false;
                entering=(Task)Invoke(flow,"EnterGame");await Frames(2,ct);var readToken=transport.LastToken;
                logout=(Task)Invoke(flow,"SignOut");await Until(()=>Field<UiScreen>(flow,"_flowScreen") is SignInScreen,ct);
                Need(readToken.IsCancellationRequested,"Profile request did not inherit gameplay cancellation");
                pendingProfile.TrySetResult(Clone(profileData));await entering;await Frames(3,ct);
                Need(ReferenceEquals(profile.Me,originalMe)&&Field<Hud>(flow,"_hud")==null,"Late cancelled profile replaced shared state or built HUD");
                nativeToken=flow.destroyCancellationToken;UnityEngine.Object.Destroy(root);root=null;
                await Until(()=>nativeToken.IsCancellationRequested&&flow==null,ct);receipt.nativeDestroys++;
                try{await logout;throw new InvalidOperationException("Destroyed sign-in wait did not cancel");}catch(OperationCanceledException){}
                flow=null;logout=null;entering=null;
                receipt.checks.Add("PASS real PlayerProfile rejects a successful held GET after same-owner logout; actual Flow destruction cancels waiting sign-in.");
                Need(transport.Mutations==0&&receipt.runtimeErrors==0,"Unexpected mutation or runtime error");receipt.pass=true;
            }
            catch(Exception error){receipt.failure=error.GetType().Name+": "+error.Message;throw;}
            finally
            {
                if(root!=null){UnityEngine.Object.Destroy(root);await Frames(2,CancellationToken.None);}
                if(entering!=null)Observe(entering);if(logout!=null)Observe(logout);
                if(isolated)typeof(VenueSession).GetProperty(nameof(VenueSession.Current)).SetValue(null,originalVenue);
                for(int i=saves.Count-1;i>=0;i--)saves[i].Dispose();
                receipt.servicesRestored=restored.TrueForAll(check=>check());receipt.venueRestored=ReferenceEquals(VenueSession.Current,originalVenue);
                receipt.reads=transport?.Reads??0;receipt.mutations=transport?.Mutations??0;receipt.disconnects=realtime?.Disconnects??0;receipt.walletCallbacks=auth?.WalletCallbacks??0;
                Application.logMessageReceived-=OnLog;Bus.Publish(new ProfileChangedSignal());
                if(!receipt.servicesRestored||!receipt.venueRestored){receipt.pass=false;receipt.failure="Review failed to restore original services/venue";}
                File.WriteAllText(Path.Combine(directory,"gameplay-generation-review.json"),JsonUtility.ToJson(receipt,true));
                Need(receipt.servicesRestored&&receipt.venueRestored,"Review restoration failed");
            }
        }
        private static Me Clone(Me t)
        {
            var c=t.Character;var l=c.Look;
            return new Me(new User(Guid.Parse("00000000-0000-4000-8000-000000000083"),"local-review",false,true),
                new Character("Gameplay review",new Look(l.SkinTone,l.HeadId,l.HairId,l.TopId,l.BottomId,l.ShoesId,l.AccId),c.TribeId,new List<string>(c.TraitIds),c.DreamId),
                new Needs(70,70,70,70,70),new Skills(1,1,1,1,1,1),1000,0,t.HomeId,flags:new Dictionary<string,object>{{"lotteryRolled",true}});
        }
        private static Action<KickedMessage> Kick(CityChannel city)=>(Action<KickedMessage>)typeof(CityChannel).GetField("Kicked",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(city);
        private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
        private static object Invoke(object owner,string name)=>owner.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,null);
        private static async Task Until(Func<bool> ready,CancellationToken ct){double end=Time.realtimeSinceStartupAsDouble+5;while(!ready()){ct.ThrowIfCancellationRequested();Need(Time.realtimeSinceStartupAsDouble<end,"Bounded native predicate timed out");await Awaitable.NextFrameAsync(ct);}}
        private static async Task Frames(int count,CancellationToken ct){for(int i=0;i<count;i++)await Awaitable.NextFrameAsync(ct);}
        private static void Observe(Task task){_ = task.ContinueWith(t=>{_ = t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        private static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    }
}
#endif
