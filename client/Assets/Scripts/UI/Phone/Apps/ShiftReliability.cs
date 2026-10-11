using System;
using System.Threading;
using System.Threading.Tasks;

namespace CTLife.UI
{
    /// <summary>Request deadlines and elapsed-time floor; neither derives or awards a payout.</summary>
    internal static class ShiftReliability
    {
        public static async Task<T> Request<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellation, int timeoutMilliseconds = 60000)
        {
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            cancellation.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var request = operation(timeout.Token);
            var boundary = Task.Delay(timeoutMilliseconds, timeout.Token);
            try
            {
                if (await Task.WhenAny(request, boundary) != request)
                {
                    timeout.Cancel();
                    // The transport aborts cooperatively; an auth refresh may not observe cancellation.
                    // Observe a late fault while keeping the already-visible outcome explicitly unconfirmed.
                    _ = request.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    cancellation.ThrowIfCancellationRequested();
                    // Cancellation does not establish whether a write reached the server. Never retry it here.
                    throw new TimeoutException(ShiftStrings.T("requestTimeout"));
                }
                return await request;
            }
            finally { timeout.Cancel(); }
        }

        public static double SecondsUntilEnd(double now, double minimumEnd, double serverSecondsLeft, bool clockSynced)
        {
            double monotonicLeft = Math.Max(0, minimumEnd - now);
            if (!clockSynced) return monotonicLeft;
            return Math.Max(monotonicLeft, serverSecondsLeft + 0.5);
        }
    }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
    public static class ShiftReliabilityChecks
    {
        public static async Task<string> Run()
        {
            if (ShiftReliability.SecondsUntilEnd(40, 45, -99999, false) != 5 ||
                ShiftReliability.SecondsUntilEnd(45, 45, 99999, false) != 0)
                throw new Exception("Unsynced wall clock changed shift timing.");
            if (ShiftReliability.SecondsUntilEnd(45, 45, 3, true) != 3.5 ||
                ShiftReliability.SecondsUntilEnd(40, 45, -3, true) != 5)
                throw new Exception("Scheduled server end or elapsed-time floor ignored.");
            int calls = 0;
            int result = await ShiftReliability.Request<int>(_ => { calls++; return Task.FromResult(7); }, default, 1000);
            if (result != 7 || calls != 1) throw new Exception("Successful request changed or repeated.");
            calls = 0;
            try
            {
                await ShiftReliability.Request<int>(async ct => { calls++; await Task.Delay(10000, ct); return 1; }, default, 20);
                throw new Exception("Request deadline did not abort the wait.");
            }
            catch (TimeoutException) { if (calls != 1) throw new Exception("Timed-out write was repeated."); }
            var stalled = new TaskCompletionSource<int>();
            try
            {
                await ShiftReliability.Request<int>(_ => stalled.Task, default, 20);
                throw new Exception("A non-cooperative operation blocked the UI deadline.");
            }
            catch (TimeoutException) { stalled.SetException(new Exception("late transport failure")); }
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            calls = 0;
            try
            {
                await ShiftReliability.Request<int>(_ => { calls++; return Task.FromResult(1); }, cancelled.Token);
                throw new Exception("Cancelled session continued a write.");
            }
            catch (OperationCanceledException) { if (calls != 0) throw new Exception("Cancelled write was invoked."); }
            calls = 0;
            try
            {
                await ShiftReliability.Request<int>(_ => { calls++; throw new InvalidOperationException("server rejected"); }, default);
                throw new Exception("Server rejection was swallowed.");
            }
            catch (InvalidOperationException e)
            {
                if (e.Message != "server rejected" || calls != 1) throw new Exception("Server rejection changed or repeated.");
            }
            return "PASS: clock skew floor, bounded request cancellation, session cancellation, no hidden write retries";
        }
    }
#endif
}
