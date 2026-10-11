using System;
using System.Threading;
using System.Threading.Tasks;

namespace CTLife.App
{
    /// <summary>Owns controller waits, not the network resources created by an uncooperative service.</summary>
    internal sealed class FlowLifetime : IDisposable
    {
        private readonly CancellationTokenSource source;
        private readonly Action<Exception> lateFailure;
        private bool disposed;
        public CancellationToken Token { get; }
        public bool IsAlive => !disposed && !Token.IsCancellationRequested;

        public FlowLifetime(CancellationToken owner, Action<Exception> lateFailure = null)
        {
            source = CancellationTokenSource.CreateLinkedTokenSource(owner);
            Token = source.Token;
            this.lateFailure = lateFailure;
        }

        public void ThrowIfClosed()
        {
            if (!IsAlive) throw new OperationCanceledException(Token);
        }

        public async Task Wait(Task operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            // Observe any operation already started by the caller even if teardown won the race.
            if (!IsAlive) { ObserveLate(operation); ThrowIfClosed(); }
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = Token.Register(() => cancelled.TrySetResult(true));
            await Task.WhenAny(operation, cancelled.Task);
            if (!IsAlive) { ObserveLate(operation); ThrowIfClosed(); }
            await operation;
            ThrowIfClosed();
        }

        public async Task<T> Wait<T>(Task<T> operation)
        {
            await Wait((Task)operation);
            return operation.GetAwaiter().GetResult();
        }

        private void ObserveLate(Task operation)
        {
            _ = operation.ContinueWith(task =>
            {
                var failure = task.Exception;
                if (failure != null) lateFailure?.Invoke(failure.GetBaseException());
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { source.Cancel(); }
            finally { source.Dispose(); }
        }
    }
}
