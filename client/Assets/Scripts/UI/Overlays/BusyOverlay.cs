using System;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// "City is busy, retrying in Ns" (additive contract change: SERVER_BUSY + Retry-After). The network layer
    /// retries on its own; this only shows the countdown and disappears when a request gets through.
    /// </summary>
    public sealed class BusyOverlay : IDisposable
    {
        private readonly VisualElement _root;
        private readonly UiRoot _ui;
        private bool _disposed;
        private readonly Label _label;
        private readonly Subscriptions _subs = new Subscriptions();
        private float _retryAt;
        private bool _active;

        public BusyOverlay(VisualElement layer)
        {
            _ui = UiRoot.Instance;
            _label = El.Text("", "ct-body");
            _root = El.Div("ct-modal-backdrop ct-busy",
                El.Div("ct-card ct-busy-card", new UiIcon("city"), El.Text(Strings.T("busy.title"), "ct-h1"), _label,
                    El.Text(Strings.T("busy.body"), "ct-small ct-muted")));
            _root.pickingMode = PickingMode.Position;
            _root.Show(false);
            layer.Add(_root);
            _root.RegisterCallback<DetachFromPanelEvent>(OnDetached);
            _subs.On<ServerBusySignal>(OnBusy);
            _ui.AddUpdater(Tick);
        }

        private void OnBusy(ServerBusySignal s)
        {
            if (_disposed) return;
            _active = s.active;
            _retryAt = Time.unscaledTime + s.retryInSeconds;
            _root.Show(s.active);
            if (s.active) _root.BringToFront();
            Tick();
        }

        private void OnDetached(DetachFromPanelEvent e)
        {
            if (e.target == _root) Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _active = false;
            _subs.Dispose();
            if (_ui != null) _ui.RemoveUpdater(Tick);
            _root.UnregisterCallback<DetachFromPanelEvent>(OnDetached);
            _root.RemoveFromHierarchy();
        }

        private void Tick()
        {
            if (_disposed || !_active) return;
            int secs = Mathf.Max(0, Mathf.CeilToInt(_retryAt - Time.unscaledTime));
            _label.text = secs > 0 ? Strings.F("busy.retry", secs) : Strings.T("busy.retrying");
        }
    }
}
