#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using UnityEngine;

namespace CTLife.App.Review
{
    /// <summary>Real MonoBehaviour destruction + owned poll/Bus checks; deliberately does not execute normal Start.</summary>
    public static class FlowOwnershipReview
    {
        [Serializable] private sealed class Receipt
        {
            public bool pass,servicesRestored;
            public int iterations,apiAttempts,runtimeErrors;
            public string phase,failure;
            public double maximumCancellationSeconds;
            public string limits="Actual Unity FlowController Awake/OnDestroy, cached native destroy token, actual ConfigRefreshLoop cancellation and real Bus cleanup. Controller disabled before Start: overlay creation, normal auth, wallet, room/world ownership, full120s poll elapse and Android pause/process lifecycle are not certified. Local Api trap only; no network/economy calls.";
            public List<string> checks=new List<string>();
        }
        private struct OwnershipProbeSignal { }
        private sealed class ApiTrap:IRestClient
        {
            public Func<Task<bool>> RefreshHandler{get;set;}public int Attempts;
            private Task<T> Reject<T>(){Attempts++;return Task.FromException<T>(new InvalidOperationException("Flow ownership fixture forbids transport operations."));}
            public Task<T> Get<T>(string path,CancellationToken ct=default)=>Reject<T>();
            public Task<T> Post<T>(string path,object body=null,bool auth=true,string idempotencyKey=null,CancellationToken ct=default)=>Reject<T>();
            public Task<T> Put<T>(string path,object body,CancellationToken ct=default)=>Reject<T>();
            public Task<T> Patch<T>(string path,object body,CancellationToken ct=default)=>Reject<T>();
            public Task<T> Delete<T>(string path,CancellationToken ct=default)=>Reject<T>();
            public Task<T> Send<T>(string method,string path,object body=null,bool auth=true,string idempotencyKey=null,CancellationToken ct=default)=>Reject<T>();
            public Task<RawResponse> SendRaw(string method,string path,string jsonBody,bool auth,string idempotencyKey,string ifNoneMatch,CancellationToken ct)=>Reject<RawResponse>();
        }
        public static async Task Run(string directory,CancellationToken owner)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!Path.IsPathRooted(directory))directory=Path.Combine(Application.persistentDataPath,directory);
#endif
            directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(owner);timeout.CancelAfter(TimeSpan.FromSeconds(20));var ct=timeout.Token;
            var receipt=new Receipt();var trap=new ApiTrap();bool hadApi=Services.TryGet<Api>(out var originalApi);
            GameObject root=null;FlowLifetime lifetime=null;Task poll=null,continuation=null;TaskCompletionSource<int> pending=null;int baseline=CountHandlers();
            void OnLog(string message,string stack,LogType kind){if(kind==LogType.Error||kind==LogType.Exception||kind==LogType.Assert)receipt.runtimeErrors++;}
            Application.logMessageReceived+=OnLog;
            try
            {
                Services.Register(new Api(trap,new ConfigStore()));
                for(int iteration=0;iteration<3;iteration++)
                {
                    receipt.phase="awake-"+iteration;
                    root=new GameObject("ReviewFlowOwnership-"+iteration);
                    var flow=root.AddComponent<FlowController>();
                    flow.enabled=false; // Awake already ran synchronously; prevent normal Start and its overlay construction.
                    var nativeToken=flow.destroyCancellationToken;
                    lifetime=Field<FlowLifetime>(flow,"_owner");Need(lifetime!=null&&lifetime.IsAlive,"Awake did not create owner");
                    Need(!nativeToken.IsCancellationRequested,"Fresh Unity destruction token already cancelled");
                    var subscriptions=Field<Subscriptions>(flow,"_subscriptions");int seen=0;
                    subscriptions.On<OwnershipProbeSignal>(_=>seen++);
                    Need(CountHandlers()==baseline+1,"Real Bus subscription was not owned");
                    Bus.Publish(new OwnershipProbeSignal());Need(seen==1,"Real Bus callback did not execute");
                    poll=(Task)Invoke(flow,"ConfigRefreshLoop");
                    pending=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);int lateWrites=0;
                    async Task AwaitAndWrite(){await lifetime.Wait(pending.Task);lateWrites++;}
                    continuation=AwaitAndWrite();await Frames(3,ct);
                    Need(!poll.IsCompleted&&!continuation.IsCompleted,"Poll/controlled operation was not pending");
                    Need(Field<CTLife.UI.UiScreen>(flow,"_flowScreen")==null&&Field<CTLife.UI.PhoneShell>(flow,"_phone")==null,"Disabled Flow unexpectedly ran normal Start");
                    receipt.phase="destroy-"+iteration;double start=Time.realtimeSinceStartupAsDouble;
                    UnityEngine.Object.Destroy(root);root=null;
                    await Until(()=>nativeToken.IsCancellationRequested&&poll.IsCompleted&&continuation.IsCompleted,3,ct);
                    receipt.maximumCancellationSeconds=Math.Max(receipt.maximumCancellationSeconds,Time.realtimeSinceStartupAsDouble-start);
                    Need(flow==null&&!lifetime.IsAlive,"Actual destroyed MonoBehaviour retained live owner");
                    Need(CountHandlers()==baseline,"OnDestroy retained owned Bus subscription");
                    await Cancelled(poll,"scheduled120second poll");await Cancelled(continuation,"non-cooperative controller wait");
                    Bus.Publish(new OwnershipProbeSignal());Need(seen==1,"Destroyed subscription still invoked");
                    if(iteration==1)pending.TrySetException(new InvalidOperationException("Controlled late operation failure"));
                    else pending.TrySetResult(42);
                    await Frames(4,ct);Need(lateWrites==0,"Late operation continued after native destroy");
                    lifetime.Dispose();lifetime.Dispose();Need(CountHandlers()==baseline,"Repeat owner disposal changed unrelated handlers");
                    poll=continuation=null;pending=null;lifetime=null;receipt.iterations++;
                }
                Need(trap.Attempts==0,"Cancelled poll made a transport request");Need(receipt.runtimeErrors==0,"Unexpected runtime error");
                receipt.checks.Add("PASS3 real MonoBehaviour Awake→Destroy cycles; cached native destruction token fires; actual scheduled120second poll cancels promptly; owned real Bus handler1→0; non-cooperative late completion/fault cannot resume UI continuation; repeated disposal.");
                receipt.pass=true;Log.Info("flow-ownership-review","PASS actual Unity destruction/poll/Bus boundaries; normal Start/overlay/network flows intentionally not executed.");
            }
            catch(Exception error){receipt.failure=error.Message;throw;}
            finally
            {
                try
                {
                    // Even on a failed assertion stop the test-owned sleeper before restoring production services.
                    lifetime?.Dispose();pending?.TrySetCanceled();
                    if(root!=null)UnityEngine.Object.Destroy(root);
                    if(poll!=null)Observe(poll);if(continuation!=null)Observe(continuation);
                }
                finally
                {
                    if(hadApi)Services.Register(originalApi);else Services.Unregister<Api>();
                    receipt.servicesRestored=hadApi?Services.TryGet<Api>(out var current)&&ReferenceEquals(current,originalApi):!Services.TryGet<Api>(out _);
                    receipt.apiAttempts=trap.Attempts;if(!receipt.servicesRestored){receipt.pass=false;receipt.failure="Original Api not restored.";}
                    Application.logMessageReceived-=OnLog;File.WriteAllText(Path.Combine(directory,"flow-ownership-review.json"),JsonUtility.ToJson(receipt,true));
                    Need(receipt.servicesRestored,"Original Api not restored");
                }
            }
        }
        private static object Invoke(object target,string name)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
        private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        private static int CountHandlers()
        {
            var handlers=(Dictionary<Type,Delegate>)typeof(Bus).GetField("_handlers",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            return handlers.TryGetValue(typeof(OwnershipProbeSignal),out var handler)?handler.GetInvocationList().Length:0;
        }
        private static async Task Cancelled(Task task,string context)
        {try{await task;throw new InvalidOperationException("Expected cancellation: "+context);}catch(OperationCanceledException){}}
        private static void Observe(Task task)=>_ = task.ContinueWith(t=>{_ = t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        private static async Task Until(Func<bool> predicate,double seconds,CancellationToken ct)
        {double end=Time.realtimeSinceStartupAsDouble+seconds;while(!predicate()){if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException("Native destruction cancellation did not settle");await Awaitable.NextFrameAsync(ct);}}
        private static async Task Frames(int count,CancellationToken ct){for(int i=0;i<count;i++)await Awaitable.NextFrameAsync(ct);}
        private static void Need(bool condition,string text){if(!condition)throw new InvalidOperationException("Flow native review: "+text);}
    }
}
#endif
