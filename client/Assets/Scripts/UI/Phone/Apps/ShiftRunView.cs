using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Rest.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Explicit start / scheduled finish / settlement / server receipt states for one shift.</summary>
    internal sealed class ShiftRunView : IDisposable
    {
        private readonly UiRoot _ui;
        private readonly VisualElement _root;
        private readonly Label _eyebrow, _heading, _copy, _pay, _xp;
        private readonly Button _done;
        private readonly TaskCompletionSource<bool> _dismissed = new TaskCompletionSource<bool>();
        private readonly Action _cancel;
        private bool _disposed;
        private Action _waitTick;

        public ShiftRunView(Action cancel)
        {
            _ui = UiRoot.Instance;
            _cancel = cancel;
            _heading = ModDesk.Plain("", "ct-shift-heading");
            _copy = ModDesk.Plain("", "ct-shift-copy");
            _pay = ModDesk.Plain("", "ct-shift-pay");
            _xp = ModDesk.Plain("", "ct-shift-xp");
            _done = El.Btn(ShiftStrings.T("back"), () => _dismissed.TrySetResult(true), "ct-btn ct-btn-primary ct-desk-next");
            _done.Show(false);
            var content = El.VerticalScroll("ct-desk-content");
            _eyebrow = ModDesk.Plain(ShiftStrings.T("shiftStatus"), "ct-desk-eyebrow");
            content.Add(_eyebrow);
            content.Add(_heading);
            content.Add(_copy);
            content.Add(_pay);
            content.Add(_xp);
            _root = El.Div("ct-desk-overlay", El.Div("ct-desk-panel ct-shift-panel", content, _done));
            ModDesk.ApplySheet(_root);
            _root.RegisterCallback<DetachFromPanelEvent>(_ => { if (!_disposed) { _cancel?.Invoke(); _dismissed.TrySetCanceled(); } });
            _ui.ModalLayer.Add(_root);
            Status("starting", "startingDetail");
        }

        public void Show(bool on) => _root.Show(on);

        public void Status(string headingKey, string copyKey)
        {
            _root.Show(true);
            _heading.text = ShiftStrings.T(headingKey);
            _copy.text = ShiftStrings.T(copyKey);
            _pay.text = _xp.text = "";
            _done.Show(false);
        }

        public Task WaitForEnd(DateTime endsAt, double minimumEnd, CancellationToken cancellation)
        {
            var completion = new TaskCompletionSource<bool>();
            double endsMs = new DateTimeOffset(endsAt.ToUniversalTime()).ToUnixTimeMilliseconds();
            double maxWaitEnds = Time.realtimeSinceStartupAsDouble + 600;
            Status("settling", "waitingDetail");
            void Finish(bool cancel)
            {
                if (_waitTick != null) _ui.RemoveUpdater(_waitTick);
                _waitTick = null;
                if (cancel) completion.TrySetCanceled(); else completion.TrySetResult(true);
            }
            _waitTick = () =>
            {
                if (cancellation.IsCancellationRequested || _disposed) { Finish(true); return; }
                // A wall clock that has not been synced must not add hours of waiting or settle early.
                // Config/assignment elapsed time is the conservative floor; the server still decides readiness.
                double left = ShiftReliability.SecondsUntilEnd(Time.realtimeSinceStartupAsDouble,
                    minimumEnd, ServerClock.SecondsUntil(endsMs), ServerClock.IsSynced);
                if (Time.realtimeSinceStartupAsDouble >= maxWaitEnds)
                {
                    _ui.RemoveUpdater(_waitTick);
                    _waitTick = null;
                    completion.TrySetException(new TimeoutException(ShiftStrings.T("waitTimeout")));
                    return;
                }
                _heading.text = ShiftStrings.F("waiting", Math.Max(0, (int)Math.Ceiling(left)));
                if (left <= 0) Finish(false);
            };
            _ui.AddUpdater(_waitTick);
            _waitTick();
            return completion.Task;
        }

        public void Receipt(PostShiftsShiftIdComplete200Response result)
        {
            if (result == null || result.PayCents < 0 || result.Xp < 0)
                throw new InvalidOperationException(ShiftStrings.T("invalidReceipt"));
            Status("paid", "confirmed");
            _eyebrow.text = ShiftStrings.T("receipt");
            _pay.text = ShiftStrings.F("paidAmount", Money.Cash(result.PayCents));
            _xp.text = ShiftStrings.F("xp", result.Xp) + (result.Promoted ? " · " + ShiftStrings.T("promoted") : "");
        }

        public void BalanceRefreshFailed() => _copy.text = ShiftStrings.T("refreshFailed");

        public void Failure(string reason)
        {
            Status("failed", "failureDetail");
            _pay.text = "";
            _xp.text = reason;
        }

        public async Task Dismiss(CancellationToken cancellation)
        {
            _done.Show(true);
            using (cancellation.Register(() => _dismissed.TrySetCanceled())) await _dismissed.Task;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_waitTick != null) _ui.RemoveUpdater(_waitTick);
            _root.RemoveFromHierarchy();
            _dismissed.TrySetCanceled();
        }
    }
}
