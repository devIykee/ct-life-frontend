using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// "City is busy, retrying in Ns" (additive contract change: SERVER_BUSY + Retry-After). The network layer
    /// retries on its own; this only shows the countdown and disappears when a request gets through.
    /// </summary>
    public sealed class BusyOverlay
    {
        private readonly VisualElement _root;
        private readonly Label _label;
        private readonly Subscriptions _subs = new Subscriptions();
        private float _retryAt;
        private bool _active;

        public BusyOverlay(VisualElement layer)
        {
            _label = El.Text("", "ct-body");
            _root = El.Div("ct-modal-backdrop ct-busy",
                El.Div("ct-card ct-busy-card", El.Text("City is busy", "ct-h1"), _label,
                    El.Text("Lots of people just showed up. You'll get in automatically.", "ct-small ct-muted")));
            _root.pickingMode = PickingMode.Position;
            _root.Show(false);
            layer.Add(_root);
            _subs.On<ServerBusySignal>(OnBusy);
            UiRoot.Instance.AddUpdater(Tick);
        }

        private void OnBusy(ServerBusySignal s)
        {
            _active = s.active;
            _retryAt = Time.unscaledTime + s.retryInSeconds;
            _root.Show(s.active);
            if (s.active) _root.BringToFront();
            Tick();
        }

        private void Tick()
        {
            if (!_active) return;
            int secs = Mathf.Max(0, Mathf.CeilToInt(_retryAt - Time.unscaledTime));
            _label.text = secs > 0 ? $"Retrying in {secs}s" : "Retrying…";
        }
    }
}
