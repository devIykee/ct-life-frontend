using System;
using CTLife.Core;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// iOS-style notification banners, top-centre of the game screen above the HUD and the Duo (toast layer). Renders
    /// <see cref="DuoNotificationCenter.Queue"/>: the newest banner in front with one card peeking behind. Tap opens the
    /// Duo at that conversation; swipe up dismisses; each banner leaves after 4 s at the front.
    /// </summary>
    public sealed class DuoBannerLayer : IDisposable
    {
        private const float SwipeDismiss = 46f, TapSlop = 16f;
        private readonly VisualElement _host;
        private readonly DuoNotificationQueue _queue;
        private readonly IVisualElementScheduledItem _tick;
        private VisualElement _front, _peek;
        private int _frontId, _peekId, _revision;
        private Tween _enter;
        private int _pointer = -1;
        private Vector2 _start;
        private float _dy;
        private bool _moved, _disposed;

        public VisualElement Host => _host;

        public DuoBannerLayer(VisualElement toastLayer, DuoNotificationQueue queue = null)
        {
            _queue = queue ?? DuoNotificationCenter.Queue;
            _host = El.Div("duo-banner-host");
            _host.name = "duo-banners";
            _host.pickingMode = PickingMode.Ignore;
            toastLayer.Insert(0, _host);
            _queue.Changed += Render;
            _tick = _host.schedule.Execute(() => { ShiftReadyNotice.Poll((long)ServerClock.NowMs); _queue.Tick(Time.unscaledTime); }).Every(100);
            Render();
        }

        private void Render()
        {
            if (_disposed) return;
            var stack = _queue.Stack;
            int front = stack.Count > 0 ? stack[0].Id : 0, peek = stack.Count > 1 ? stack[1].Id : 0;
            if (front == _frontId && peek == _peekId && _queue.Revision == _revision) return;
            bool newFront = front != 0 && front != _frontId;
            _frontId = front; _peekId = peek; _revision = _queue.Revision;
            _enter?.Cancel();
            _front?.RemoveFromHierarchy(); _peek?.RemoveFromHierarchy();
            _front = _peek = null;
            _pointer = -1;
            _host.Show(front != 0);
            if (front == 0) return;
            if (peek != 0) { _peek = Card(stack[1], true); _host.Add(_peek); }
            _front = Card(stack[0], false);
            _host.Add(_front);
            if (newFront)
            {
                var card = _front;
                _enter = Tween.Run(card, .28f, t =>
                {
                    card.style.opacity = t;
                    card.style.translate = new Translate(0, -60f * (1f - t));
                }, Ease.OutCubic);
            }
        }

        private VisualElement Card(DuoNotification n, bool peeking)
        {
            var card = El.Div("duo-banner" + (peeking ? " duo-banner-peek" : ""));
            card.name = peeking ? "duo-banner-peek" : "duo-banner";
            Duo.AddShadow(card, "banner");
            var icon = El.Div("duo-banner-icon", new PhoneAppIcon(n.AppId));
            var head = El.Div("duo-banner-head", El.Text(n.Title ?? "", "duo-banner-title"));
            if (!string.IsNullOrEmpty(n.Detail)) head.Add(El.Text(n.Detail, "duo-banner-detail"));
            head.Add(El.Text(Strings.T("duo.banner.now"), "duo-banner-when"));
            var copy = El.Div("duo-banner-copy", head);
            if (!string.IsNullOrEmpty(n.Body)) copy.Add(El.Div("duo-banner-body-clip", El.Text(n.Body, "duo-banner-body")));
            var face = El.Div("duo-banner-face", icon, copy);
            card.Add(face);
            El.PassThrough(face);
            if (peeking) { card.pickingMode = PickingMode.Ignore; foreach (var c in card.Children()) El.PassThrough(c); return card; }
            card.pickingMode = PickingMode.Position;
            foreach (var c in card.Children()) El.PassThrough(c);
            card.tooltip = n.Title;
            int id = n.Id;
            card.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_pointer >= 0 || e.button != 0) return;
                _pointer = e.pointerId; _start = e.position; _dy = 0; _moved = false;
                card.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            card.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != _pointer) return;
                Vector2 d = (Vector2)e.position - _start;
                if (d.magnitude > TapSlop) _moved = true;
                _dy = Mathf.Min(0f, d.y) + Mathf.Max(0f, d.y) * .2f; // follows up, resists down
                card.style.translate = new Translate(0, _dy);
                e.StopPropagation();
            });
            card.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != _pointer) return;
                _pointer = -1;
                if (card.HasPointerCapture(e.pointerId)) card.ReleasePointer(e.pointerId);
                e.StopPropagation();
                if (_dy < -SwipeDismiss) { Leave(card, id); return; }
                if (!_moved) { Tap(n); return; }
                card.style.translate = new Translate(0, 0);
            });
            card.RegisterCallback<PointerCancelEvent>(e =>
            {
                if (e.pointerId != _pointer) return;
                _pointer = -1;
                card.style.translate = new Translate(0, 0);
            });
            return card;
        }

        private void Tap(DuoNotification n)
        {
            Bus.Publish(new SfxSignal("sfx_ui_tap"));
            _queue.Dismiss(n.Id, Time.unscaledTime);
            try { n.Open?.Invoke(); }
            catch (Exception e) { Log.Exception("notify", e); }
        }

        private void Leave(VisualElement card, int id)
        {
            _enter?.Cancel();
            float from = _dy;
            _enter = Tween.Run(card, .16f, t =>
            {
                card.style.translate = new Translate(0, Mathf.Lerp(from, -260f, t));
                card.style.opacity = 1f - t;
                if (t >= 1f) _queue.Dismiss(id, Time.unscaledTime);
            }, Ease.OutCubic, skipWhenReduced: false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _queue.Changed -= Render;
            _tick?.Pause();
            _enter?.Cancel();
            _host.RemoveFromHierarchy();
        }
    }
}
