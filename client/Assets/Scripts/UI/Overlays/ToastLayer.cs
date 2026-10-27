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
        private readonly UiRoot _ui;
        private readonly IncidentPresentation _incidents;
        private readonly DuoBannerLayer _banners;
        private bool _disposed;
        private readonly VisualElement _layer;
        private readonly Subscriptions _subs = new Subscriptions();

        public ToastLayer(VisualElement layer)
        {
            _layer = layer;
            _ui = UiRoot.Instance;
            _layer.RegisterCallback<DetachFromPanelEvent>(OnDetached);
            _subs.On<ToastSignal>(OnToast);
            _incidents = new IncidentPresentation(_ui);
            // Notification banners share this layer and material, above toasts, so both read as one system.
            _banners = new DuoBannerLayer(_layer);
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
            // Design system: one channel (SYSTEM.md 3.9), the DS toast host under the Cash pill, one at a time.
            var host = _ui != null ? _ui.Toasts : CTLife.UI.DS.DsToastHost.Current;
            if (host == null) return;
            host.Show(Spec(text, detail, kind));
        }

        /// <summary>Pure mapping from the game's toast kinds to a design-system toast (tone + icon + time).</summary>
        public static CTLife.UI.DS.DsToastSpec Spec(string text, string detail, ToastKind kind)
        {
            var (icon, tone) = kind switch
            {
                ToastKind.Good => ("check", CTLife.UI.DS.Tone.Accent),
                ToastKind.Bad => ("x", CTLife.UI.DS.Tone.Danger),
                ToastKind.Warning => ("info", CTLife.UI.DS.Tone.Warn),
                ToastKind.Event => ("bell", CTLife.UI.DS.Tone.Accent),
                _ => ("info", CTLife.UI.DS.Tone.Neutral),
            };
            return new CTLife.UI.DS.DsToastSpec { Icon = icon, Text = text, Detail = detail, Tone = tone, Seconds = kind == ToastKind.Event ? 5f : 3.5f };
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
            _incidents.Dispose();
            _banners.Dispose();
        }
    }
}
