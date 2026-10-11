using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
namespace CTLife.World
{
    /// <summary>Stops a visit's wait, while observing the non-cooperative operation it already started.</summary>
    internal static class VenueWait
    {
        public static async Task<T> Run<T>(Task<T> operation,CancellationToken owner)
        {
            var cancelled=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration=owner.Register(()=>cancelled.TrySetResult(true));
            if(await Task.WhenAny(operation,cancelled.Task)!=operation||owner.IsCancellationRequested)
            {
                Observe(operation);owner.ThrowIfCancellationRequested();
            }
            var result=await operation;owner.ThrowIfCancellationRequested();return result;
        }
        private static async void Observe(Task task)
        {
            try{await task;}catch(OperationCanceledException){}
            catch(Exception error){Log.Warn("venue","Late visit operation failed: "+error.GetType().Name);}
        }
    }
}
