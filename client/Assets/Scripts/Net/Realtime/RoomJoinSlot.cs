using System;
using System.Collections.Generic;
using CTLife.Core;
using System.Threading;
using System.Threading.Tasks;

namespace CTLife.Net
{
    /// <summary>Main-thread ownership of one room slot. Superseded joins drain their own result only.</summary>
    internal sealed class RoomJoinSlot<T> where T : class
    {
        private readonly Func<T, bool> _joined;
        private readonly Func<T, Task> _leave;
        private RoomJoinAttempt _pending;
        private string _identity;
        private readonly List<Task> _retired = new List<Task>();
        private CancellationTokenRegistration _leaseCancellation;
        private long _leaseRevision;
        // Callers can capture the cleanup already started by a cancelled accepted lease.
        public Task RetiredCleanup => DrainRetirements();
        public T Current { get; private set; }
        public long Revision { get; private set; }

        public RoomJoinSlot(Func<T, bool> joined, Func<T, Task> leave) { _joined = joined; _leave = leave; }

        public async Task<T> Join(string identity, CancellationToken owner, Func<bool> identityCurrent,
            bool reuse, Func<T> create, Func<T, RoomJoinAttempt, Task> join, Action previousLeft = null)
        {
            owner.ThrowIfCancellationRequested();
            var context = SynchronizationContext.Current;
            int thread = Thread.CurrentThread.ManagedThreadId;
            if (owner.CanBeCanceled && context == null)
                throw new InvalidOperationException("Cancellable room joins require an owning synchronization context.");
            if (!identityCurrent()) throw new OperationCanceledException("Room identity is no longer current.", owner);
            if (reuse && Current != null && _identity == identity && _joined(Current))
            {
                var retained = Current;
                RetainLease(retained, owner, context, thread);
                owner.ThrowIfCancellationRequested();
                return retained;
            }
            ReleaseLeaseRegistration();
            Revision++;
            var previousAttempt = _pending;
            RoomJoinAttempt attempt = null;
            attempt = new RoomJoinAttempt(owner, () => ReferenceEquals(_pending, attempt) && identityCurrent());
            _pending = attempt;
            previousAttempt?.Cancel();
            var previous = Current;
            Current = null;
            _identity = null;
            T candidate = null;
            try
            {
                if (previous != null)
                {
                    await BeginRetirement(previous);
                    attempt.ThrowIfObsolete();
                    previousLeft?.Invoke();
                }
                attempt.ThrowIfObsolete();
                candidate = create();
                await join(candidate, attempt);
                attempt.ThrowIfObsolete();
                Current = candidate;
                _identity = identity;
                RetainLease(candidate, owner, context, thread);
                if (owner.IsCancellationRequested)
                {
                    // Cancellation already detached/retired this accepted handle (or a successor owns it).
                    candidate = null;
                    owner.ThrowIfCancellationRequested();
                }
                return candidate;
            }
            catch
            {
                // Never read the shared Current after an await to choose the resource to close.
                if (candidate != null) await _leave(candidate);
                throw;
            }
            finally
            {
                if (ReferenceEquals(_pending, attempt)) _pending = null;
                attempt.Dispose();
            }
        }

        /// <summary>
        /// Make-before-break: publish an already-joined room as current and retire the previous one (its leave runs after,
        /// so the user is in both rooms for the overlap). Pending joins are superseded.
        /// </summary>
        public Task Adopt(T joined, string identity)
        {
            ReleaseLeaseRegistration();
            Revision++;
            var attempt = _pending; _pending = null; attempt?.Cancel();
            var previous = Current;
            Current = joined;
            _identity = identity;
            return previous != null && !ReferenceEquals(previous, joined) ? BeginRetirement(previous) : Task.CompletedTask;
        }

        /// <summary>Invalidates pending work and captures the currently published room synchronously.</summary>
        public T Detach()
        {
            ReleaseLeaseRegistration();
            Revision++;
            var attempt = _pending;
            _pending = null;
            attempt?.Cancel();
            var previous = Current;
            Current = null;
            _identity = null;
            return previous;
        }

        private void ReleaseLeaseRegistration()
        {
            _leaseRevision++;
            _leaseCancellation.Dispose();
            _leaseCancellation = default;
        }

        private void RetainLease(T channel, CancellationToken owner, SynchronizationContext context, int thread)
        {
            ReleaseLeaseRegistration();
            long revision = _leaseRevision;
            if (!owner.CanBeCanceled) return;
            void Release(object ignored)
            {
                if (revision != _leaseRevision || !ReferenceEquals(Current, channel)) return;
                var captured = Detach();
                _ = ObserveRetirement(BeginRetirement(captured));
            }
            var registration = owner.Register(() =>
            {
                if (Thread.CurrentThread.ManagedThreadId != thread) context.Post(Release, null);
                else Release(null);
            });
            // Register invokes synchronously for an already-cancelled token; do not overwrite successor ownership.
            if (revision == _leaseRevision && ReferenceEquals(Current, channel)) _leaseCancellation = registration;
            else registration.Dispose();
        }

        private Task BeginRetirement(T captured)
        {
            // Publish the retirement before Leave can synchronously invoke callbacks.
            // Keep failed entries until an explicit drain observes them; do not silently lose a leave failure.
            _retired.RemoveAll(task => task.Status == TaskStatus.RanToCompletion);
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _retired.Add(done.Task);
            _ = CompleteRetirement(captured, done);
            return done.Task;
        }

        private async Task CompleteRetirement(T captured, TaskCompletionSource<bool> done)
        {
            try { if (captured != null) await _leave(captured); done.TrySetResult(true); }
            catch (Exception error) { done.TrySetException(error); }
        }

        private static async Task ObserveRetirement(Task retirement)
        {
            try { await retirement; }
            catch (Exception error) { Log.Warn("rt", "Owned room cleanup failed: " + error.GetType().Name); }
        }

        private async Task DrainRetirements()
        {
            var captured = _retired.ToArray();
            try { await Task.WhenAll(captured); }
            finally
            {
                // A later join/cancellation may have added successor cleanup while this drain awaited.
                foreach (var task in captured) if (task.IsCompleted) _retired.Remove(task);
            }
        }

    }

    internal sealed class RoomJoinAttempt : IDisposable
    {
        private readonly CancellationTokenSource _source;
        private readonly Func<bool> _isCurrent;
        private bool _disposed;
        public CancellationToken Token { get; }
        public RoomJoinAttempt(CancellationToken owner, Func<bool> isCurrent)
        {
            _source = CancellationTokenSource.CreateLinkedTokenSource(owner);
            Token = _source.Token;
            _isCurrent = isCurrent;
        }
        public void ThrowIfObsolete()
        {
            Token.ThrowIfCancellationRequested();
            if (_disposed || !_isCurrent()) throw new OperationCanceledException("Room join was superseded.", Token);
        }
        public void Cancel() { if (!_disposed) _source.Cancel(); }
        public void Dispose() { if (_disposed) return; _disposed = true; _source.Dispose(); }
    }
}
