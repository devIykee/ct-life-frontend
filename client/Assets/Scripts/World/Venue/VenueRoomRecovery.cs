using System;
using System.Threading;
using System.Threading.Tasks;

namespace CTLife.World
{
    /// <summary>Serializes a visit's same-zone reconnects, cancelling obsolete targets and backing off failed joins.</summary>
    public sealed class VenueRoomRecovery : IDisposable
    {
        private readonly Func<double> _clock;
        private CancellationTokenSource _attempt;
        private string _target;
        private long _revision;
        private int _failures;
        private double _retryAt;
        private bool _disposed;
        public bool Busy => _attempt != null;

        public VenueRoomRecovery(Func<double> clock) { _clock = clock; }

        public void SetTarget(string target)
        {
            if (_target == target) return;
            _target = target; _revision++; _failures = 0; _retryAt = 0;
            _attempt?.Cancel();
        }

        public Task TryRun(CancellationToken owner, Func<CancellationToken, Task> join, Action<Exception> failed = null)
        {
            if (_disposed || _target == null || owner.IsCancellationRequested || Busy || _clock() < _retryAt)
                return Task.CompletedTask;
            var attempt = CancellationTokenSource.CreateLinkedTokenSource(owner);
            _attempt = attempt;
            return Run(attempt, _revision, join, failed);
        }

        private async Task Run(CancellationTokenSource attempt, long revision, Func<CancellationToken, Task> join, Action<Exception> failed)
        {
            try
            {
                await join(attempt.Token);
                attempt.Token.ThrowIfCancellationRequested();
                if (revision == _revision) { _failures = 0; _retryAt = 0; }
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (revision == _revision && !attempt.IsCancellationRequested)
                {
                    _failures++;
                    _retryAt = _clock() + Math.Min(32, Math.Pow(2, Math.Min(5, _failures)));
                    failed?.Invoke(error);
                }
            }
            finally
            {
                if (ReferenceEquals(_attempt, attempt)) _attempt = null;
                attempt.Dispose();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            SetTarget(null);
        }
    }
}
