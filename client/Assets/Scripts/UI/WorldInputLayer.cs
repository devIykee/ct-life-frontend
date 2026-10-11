using System.Collections.Generic;
using CTLife.Core;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Full-screen element behind the HUD that receives touches not consumed by UI and turns them into world input:
    /// tap (moved &lt; 12 px, &lt; 0.35 s) → WorldInput.Tap; one-finger drag → camera pan; two-finger pinch → zoom.
    /// Works for touch and mouse (editor), because UI Toolkit unifies them as pointer events.
    /// </summary>
    public static class WorldInputLayer
    {
        private sealed class PointerInfo
        {
            public Vector2 start, last;
            public float startTime;
        }

        private static readonly Dictionary<int, PointerInfo> _pointers = new Dictionary<int, PointerInfo>();
        private static float _lastPinchDist;
        private static VisualElement _layer;
        private static UiRoot _ui;
        private static bool _paused, _focused;

        public static void Attach(VisualElement layer)
        {
            Detach();
            _layer = layer;
            _ui = UiRoot.Instance;
            _focused = Application.isFocused;
            _paused = false;
            _ = Runner.Instance; // The existing persistent host forwards Android pause/resume.
            Runner.ApplicationPaused += OnPaused;
            Application.focusChanged += OnFocusChanged;
            _ui?.AddUpdater(CheckBlocked);
            layer.RegisterCallback<PointerDownEvent>(OnDown);
            layer.RegisterCallback<PointerMoveEvent>(OnMove);
            layer.RegisterCallback<PointerUpEvent>(OnUp);
            layer.RegisterCallback<PointerCancelEvent>(OnCancel);
            layer.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            layer.RegisterCallback<WheelEvent>(OnWheel);
            layer.RegisterCallback<DetachFromPanelEvent>(OnDetached);
        }

        private static bool IsBlocked()
        {
            if (_layer == null || _layer.panel == null || _paused || !_focused) return true;
            if (_ui == null) return false;
            if (HasOverlay(_ui.ScreenLayer) || HasOverlay(_ui.PhoneLayer) || HasOverlay(_ui.ModalLayer)) return true;
            for (var focus = _ui.Root.focusController?.focusedElement as VisualElement; focus != null; focus = focus.parent)
                if (focus is TextField) return true;
            return false;
        }

        private static bool HasOverlay(VisualElement layer)
        {
            if (layer == null || layer.resolvedStyle.display == DisplayStyle.None) return false;
            foreach (var child in layer.Children())
                if (child.resolvedStyle.display != DisplayStyle.None && child.resolvedStyle.visibility == Visibility.Visible) return true;
            return false;
        }

        private static void OnDown(PointerDownEvent e)
        {
            if (IsBlocked()) { ClearPointers(); return; }
            _pointers[e.pointerId] = new PointerInfo { start = e.position, last = e.position, startTime = Time.unscaledTime };
            _layer.CapturePointer(e.pointerId);
            if (_pointers.Count == 2) _lastPinchDist = PinchDistance();
        }

        private static void OnMove(PointerMoveEvent e)
        {
            if (IsBlocked()) { ClearPointers(); return; }
            if (!_pointers.TryGetValue(e.pointerId, out var p)) return;
            var delta = (Vector2)e.position - p.last;
            p.last = e.position;
            var rig = CameraRig.Instance;
            if (rig == null) return;
            float scale = Screen.width / Mathf.Max(1f, _layer.layout.width);
            if (_pointers.Count >= 2)
            {
                float d = PinchDistance();
                rig.Zoom((d - _lastPinchDist) * scale);
                _lastPinchDist = d;
            }
            else if (((Vector2)e.position - p.start).sqrMagnitude > 144f)
            {
                // Only world pointers are tracked: a second thumb can still pan while the joystick is held.
                rig.Pan(new Vector2(delta.x, -delta.y) * scale);
            }
        }

        private static void OnUp(PointerUpEvent e)
        {
            if (IsBlocked()) { ClearPointers(); return; }
            bool tap = _pointers.TryGetValue(e.pointerId, out var p) && _pointers.Count == 1 &&
                ((Vector2)e.position - p.start).sqrMagnitude < 144f && Time.unscaledTime - p.startTime < 0.35f;
            Release(e.pointerId);
            if (!tap) return;
            var size = _layer.layout.size;
            var screen = new Vector2(e.position.x / size.x * Screen.width, (1f - e.position.y / size.y) * Screen.height);
            WorldInput.Tap(screen);
        }

        private static void OnCancel(PointerCancelEvent e) => Release(e.pointerId);
        private static void OnCaptureOut(PointerCaptureOutEvent e) => Forget(e.pointerId);
        private static void OnWheel(WheelEvent e)
        {
            if (!IsBlocked()) CameraRig.Instance?.Zoom(-e.delta.y * 20f);
        }
        private static void OnPaused(bool paused) { _paused = paused; if (paused) ClearPointers(); }
        private static void OnFocusChanged(bool focused) { _focused = focused; if (!focused) ClearPointers(); }
        private static void CheckBlocked() { if (_pointers.Count > 0 && IsBlocked()) ClearPointers(); }
        private static void OnDetached(DetachFromPanelEvent e) { if (e.target == _layer) Detach(); }

        private static void Forget(int pointerId)
        {
            _pointers.Remove(pointerId);
            _lastPinchDist = _pointers.Count >= 2 ? PinchDistance() : 0f;
        }

        private static void Release(int pointerId)
        {
            Forget(pointerId); // Clear first: releasing capture synchronously sends CaptureOut.
            if (_layer != null && _layer.HasPointerCapture(pointerId)) _layer.ReleasePointer(pointerId);
        }

        private static void ClearPointers()
        {
            if (_pointers.Count == 0) return;
            var pointers = new List<int>(_pointers.Keys);
            _pointers.Clear();
            _lastPinchDist = 0f;
            foreach (int pointer in pointers)
                if (_layer != null && _layer.HasPointerCapture(pointer)) _layer.ReleasePointer(pointer);
        }

        private static void Detach()
        {
            ClearPointers();
            Runner.ApplicationPaused -= OnPaused;
            Application.focusChanged -= OnFocusChanged;
            _ui?.RemoveUpdater(CheckBlocked);
            if (_layer != null)
            {
                _layer.UnregisterCallback<PointerDownEvent>(OnDown);
                _layer.UnregisterCallback<PointerMoveEvent>(OnMove);
                _layer.UnregisterCallback<PointerUpEvent>(OnUp);
                _layer.UnregisterCallback<PointerCancelEvent>(OnCancel);
                _layer.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
                _layer.UnregisterCallback<WheelEvent>(OnWheel);
                _layer.UnregisterCallback<DetachFromPanelEvent>(OnDetached);
            }
            _layer = null;
            _ui = null;
        }

        private static float PinchDistance()
        {
            Vector2? a = null;
            foreach (var p in _pointers.Values)
            {
                if (a == null) { a = p.last; continue; }
                return Vector2.Distance(a.Value, p.last);
            }
            return 0f;
        }
    }
}
