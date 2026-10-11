using System;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Toasts (toasts.json ids, literal text). City incidents have their own readable, bounded presentation.
    /// Liquidations aimed at this player open the REKT card through its separate subscription.
    /// </summary>
    public sealed class ToastLayer : IDisposable
    {
        private const int MaxVisible = 3;
        private readonly UiRoot _ui;
        private readonly IncidentPresentation _incidents;
        private bool _disposed;
        private readonly VisualElement _layer;
        private readonly Queue<(string text, string detail, ToastKind kind)> _queue = new Queue<(string, string, ToastKind)>();
        private readonly List<(VisualElement el, float until)> _live = new List<(VisualElement, float)>();
        private readonly Subscriptions _subs = new Subscriptions();

        public ToastLayer(VisualElement layer)
        {
            _layer = layer;
            _ui = UiRoot.Instance;
            _layer.RegisterCallback<DetachFromPanelEvent>(OnDetached);
            _subs.On<ToastSignal>(OnToast);
            _incidents = new IncidentPresentation(_ui);
            _ui.AddUpdater(Tick);
        }

        private void OnToast(ToastSignal t)
        {
            if (_disposed) return;
            var text = t.literal ?? Strings.Toast(t.textId, t.@params);
            if (string.IsNullOrWhiteSpace(text)) return;
            var kind = t.kind;
            if (t.textId == "toast_drained") kind = ToastKind.Bad;
            Enqueue(text, t.detail, kind);
            Bus.Publish(new SfxSignal(kind == ToastKind.Bad ? "sfx_ui_error" : "sfx_ui_toast"));
        }

        private void Enqueue(string text, string detail, ToastKind kind)
        {
            _queue.Enqueue((text, detail, kind));
            Pump();
        }

        private void Pump()
        {
            while (_live.Count < MaxVisible && _queue.Count > 0)
            {
                var (text, detail, kind) = _queue.Dequeue();
                ToastNotice el = null;
                el = new ToastNotice(text, detail, kind, () => Dismiss(el));
                _layer.Add(el);
                _live.Add((el, Time.unscaledTime + (kind == ToastKind.Event ? 5f : 3.5f)));
                el.Reveal();
            }
        }

        private void Dismiss(VisualElement element)
        {
            if (_disposed) return;
            for (int i = _live.Count - 1; i >= 0; i--)
                if (_live[i].el == element)
                {
                    element.RemoveFromHierarchy();
                    _live.RemoveAt(i);
                    break;
                }
            Pump();
        }

        private void OnDetached(DetachFromPanelEvent e)
        {
            if (e.target == _layer) Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _layer.UnregisterCallback<DetachFromPanelEvent>(OnDetached);
            _subs.Dispose();
            if (_ui != null) _ui.RemoveUpdater(Tick);
            _incidents.Dispose();
            foreach (var toast in _live) toast.el.RemoveFromHierarchy();
            _live.Clear();
            _queue.Clear();
        }

        private void Tick()
        {
            if (_disposed) return;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _live[i].until) continue;
                _live[i].el.RemoveFromHierarchy();
                _live.RemoveAt(i);
            }
            Pump();
        }
    }
}
