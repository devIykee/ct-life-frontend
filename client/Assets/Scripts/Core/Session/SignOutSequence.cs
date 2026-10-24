using System;
using System.Threading;
using System.Threading.Tasks;

namespace CTLife.Core
{
    /// <summary>
    /// Sign-out order (2026-10-24): an optional pre-step that needs the live session (push unregister) runs first, isolated
    /// with its own timeout and token (never the caller's), and every exception from it, cancellation included, is swallowed
    /// and logged; the session revoke then always runs. Fail-closed: no pre-step outcome can skip the revoke.
    /// </summary>
    public static class SignOutSequence
    {
        public static readonly TimeSpan DefaultPreStepTimeout = TimeSpan.FromSeconds(4);

        /// <param name="preStep">Best-effort work while still signed in; gets a token that fires at the timeout.</param>
        /// <param name="revoke">The revoke (venue exit, realtime disconnect, auth sign-out). Always awaited.</param>
        public static async Task Run(Func<CancellationToken, Task> preStep, Func<Task> revoke, TimeSpan? timeout = null)
        {
            if (revoke == null) throw new ArgumentNullException(nameof(revoke));
            try
            {
                if (preStep != null)
                {
                    using var cts = new CancellationTokenSource(timeout ?? DefaultPreStepTimeout);
                    Task step;
                    try { step = preStep(cts.Token) ?? Task.CompletedTask; }
                    catch (Exception e) { Log.Warn("signout", "pre-step threw: " + e.GetType().Name); step = Task.CompletedTask; }
                    var timer = Task.Delay(timeout ?? DefaultPreStepTimeout);
                    if (await Task.WhenAny(step, timer).ConfigureAwait(true) != step)
                    {
                        cts.Cancel();
                        Log.Warn("signout", "pre-step timed out; revoking anyway");
                        _ = step.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    }
                    else
                    {
                        try { await step.ConfigureAwait(true); }
                        catch (Exception e) { Log.Warn("signout", "pre-step failed (" + e.GetType().Name + "); revoking anyway"); }
                    }
                }
            }
            finally
            {
                await revoke().ConfigureAwait(true);
            }
        }
    }
}
