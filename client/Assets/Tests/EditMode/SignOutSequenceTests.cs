using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace CTLife.Tests
{
    /// <summary>Sign-out is fail-closed: whatever the push pre-step does, the session revoke runs (SignOutSequence).</summary>
    public sealed class SignOutSequenceTests
    {
        private bool _revoked;

        // Continuations come back on the editor's main thread (as in the game), so the tests yield frames instead of blocking.
        private IEnumerator Run(Func<CancellationToken, Task> pre, TimeSpan? timeout = null)
        {
            _revoked = false;
            LogAssert.ignoreFailingMessages = true;
            var t = SignOutSequence.Run(pre, () => { _revoked = true; return Task.CompletedTask; }, timeout ?? TimeSpan.FromMilliseconds(200));
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!t.IsCompleted && DateTime.UtcNow < until) yield return null;
            Assert.IsTrue(t.IsCompleted, "the sequence finished");
            Assert.IsFalse(t.IsFaulted, "the sequence never throws a pre-step error");
        }

        [UnityTest]
        public IEnumerator Revokes_WhenPreStepSucceeds()
        {
            bool ran = false;
            yield return Run(_ => { ran = true; return Task.CompletedTask; });
            Assert.IsTrue(ran); Assert.IsTrue(_revoked);
        }

        [UnityTest]
        public IEnumerator Revokes_WhenPreStepThrowsSynchronously()
        {
            yield return Run(_ => throw new InvalidOperationException("boom"));
            Assert.IsTrue(_revoked);
        }

        [UnityTest]
        public IEnumerator Revokes_WhenPreStepFaults()
        {
            yield return Run(async _ => { await Task.Yield(); throw new Exception("network"); });
            Assert.IsTrue(_revoked);
        }

        [UnityTest]
        public IEnumerator Revokes_WhenPreStepIsCancelled()
        {
            yield return Run(_ => Task.FromCanceled(new CancellationToken(true)));
            Assert.IsTrue(_revoked);
            yield return Run(async _ => { await Task.Yield(); throw new OperationCanceledException(); });
            Assert.IsTrue(_revoked);
        }

        [UnityTest]
        public IEnumerator Revokes_WhenPreStepNeverFinishes()
        {
            var never = new TaskCompletionSource<bool>();
            yield return Run(_ => never.Task, TimeSpan.FromMilliseconds(100));
            Assert.IsTrue(_revoked, "a hung push step times out and the revoke still runs");
        }

        [UnityTest]
        public IEnumerator PreStepTokenFiresAtTheTimeout()
        {
            bool cancelled = false;
            yield return Run(async ct => { try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { cancelled = true; throw; } },
                TimeSpan.FromMilliseconds(100));
            Assert.IsTrue(_revoked);
            var until = DateTime.UtcNow.AddSeconds(2);
            while (!cancelled && DateTime.UtcNow < until) yield return null;
            Assert.IsTrue(cancelled, "the pre-step's own token fires at the timeout");
        }
    }
}
