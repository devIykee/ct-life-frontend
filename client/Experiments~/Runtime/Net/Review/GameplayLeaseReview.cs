#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
namespace CTLife.Net.Review
{
    /// <summary>Actual slot ownership on Unity's synchronization context; synthetic handles, no SDK connection.</summary>
    public static class GameplayLeaseReview
    {
        private sealed class Handle { public int leaves,leaveThread; }
        public static async Task Run(Action<string> record,CancellationToken ct)
        {
            int main=Thread.CurrentThread.ManagedThreadId;
            Need(SynchronizationContext.Current!=null,"Unity synchronization context missing");
            using var oldOwner=new CancellationTokenSource();using var nextOwner=new CancellationTokenSource();
            var gate=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var slot=new RoomJoinSlot<Handle>(_=>true,h=>{h.leaves++;h.leaveThread=Thread.CurrentThread.ManagedThreadId;return gate.Task;});
            var handle=new Handle();await slot.Join("local-review",oldOwner.Token,()=>true,true,()=>handle,(_,__)=>Task.CompletedTask);
            var worker=new Thread(()=>oldOwner.Cancel());worker.Start();Need(worker.Join(1000),"Worker cancellation did not return");
            Need(handle.leaves==0,"Worker called Leave before the owning Unity context resumed");
            await slot.Join("local-review",nextOwner.Token,()=>true,true,()=>new Handle(),(_,__)=>Task.CompletedTask);
            for(int i=0;i<3;i++)await Awaitable.NextFrameAsync(ct);
            Need(handle.leaves==0&&ReferenceEquals(slot.Current,handle),"Old posted cancellation touched a transferred lease");
            worker=new Thread(()=>nextOwner.Cancel());worker.Start();Need(worker.Join(1000),"Second worker cancellation did not return");
            double end=Time.realtimeSinceStartupAsDouble+3;
            while(handle.leaves==0){ct.ThrowIfCancellationRequested();Need(Time.realtimeSinceStartupAsDouble<end,"Owning Unity context never drained cancellation");await Awaitable.NextFrameAsync(ct);}
            Need(handle.leaves==1&&handle.leaveThread==main&&slot.Current==null,"Accepted handle was not retired exactly once on Unity main thread");
            var retirement=slot.RetiredCleanup;Need(!retirement.IsCompleted,"Retirement stopped awaiting the owned Leave task");
            slot.Detach();nextOwner.Cancel();gate.TrySetResult(true);await retirement;
            Need(handle.leaves==1,"Repeat cancel/detach repeated Leave");
            var replacementGate=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var replacingSlot=new RoomJoinSlot<Handle>(_=>true,h=>{h.leaves++;return replacementGate.Task;});
            var first=new Handle();await replacingSlot.Join("local-review",CancellationToken.None,()=>true,false,()=>first,(_,__)=>Task.CompletedTask);
            bool created=false;
            var replacing=replacingSlot.Join("local-review",CancellationToken.None,()=>true,false,()=>{created=true;return new Handle();},(_,__)=>Task.CompletedTask);
            replacingSlot.Detach();var draining=replacingSlot.RetiredCleanup;
            Need(!draining.IsCompleted&&first.leaves==1,"Replacement accepted retirement not awaited");
            replacementGate.TrySetResult(true);await draining;
            try{await replacing;throw new InvalidOperationException("Invalidated replacement completed");}catch(OperationCanceledException){}
            Need(!created&&first.leaves==1,"Cancelled replacement created successor or repeated Leave");
            record("PASS actual RoomJoinSlot nonreuse replacement: held accepted Leave is visible to retirement drain; cancellation while detached waits for that exact Leave and never creates the next handle.");
            record("PASS actual RoomJoinSlot on UnitySynchronizationContext: worker cancellation posts to main thread, reuse transfers exact lease, obsolete posted callback ignored, one accepted retirement awaited, repeated cancellation/detach safe. Synthetic handles only.");
        }
        private static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    }
}
#endif
