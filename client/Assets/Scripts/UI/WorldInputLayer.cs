using System.Collections.Generic;
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

        public static void Attach(VisualElement layer)
        {
            layer.RegisterCallback<PointerDownEvent>(e =>
            {
                _pointers[e.pointerId] = new PointerInfo { start = e.position, last = e.position, startTime = Time.unscaledTime };
                layer.CapturePointer(e.pointerId);
                if (_pointers.Count == 2) _lastPinchDist = PinchDistance();
            });
            layer.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_pointers.TryGetValue(e.pointerId, out var p)) return;
                var delta = (Vector2)e.position - p.last;
                p.last = e.position;
                var rig = CameraRig.Instance;
                if (rig == null) return;
                float scale = Screen.width / Mathf.Max(1f, layer.layout.width);
                if (_pointers.Count >= 2)
                {
                    float d = PinchDistance();
                    rig.Zoom((d - _lastPinchDist) * scale);
                    _lastPinchDist = d;
                }
                else if (((Vector2)e.position - p.start).sqrMagnitude > 144f)
                {
                    // Panel y grows downward; screen y grows upward.
                    rig.Pan(new Vector2(delta.x, -delta.y) * scale);
                }
            });
            layer.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_pointers.TryGetValue(e.pointerId, out var p))
                {
                    bool tap = _pointers.Count == 1 && ((Vector2)e.position - p.start).sqrMagnitude < 144f && Time.unscaledTime - p.startTime < 0.35f;
                    _pointers.Remove(e.pointerId);
                    if (tap)
                    {
                        var size = layer.layout.size;
                        var screen = new Vector2(e.position.x / size.x * Screen.width, (1f - e.position.y / size.y) * Screen.height);
                        WorldInput.Tap(screen);
                    }
                }
                if (layer.HasPointerCapture(e.pointerId)) layer.ReleasePointer(e.pointerId);
            });
            layer.RegisterCallback<PointerCancelEvent>(e => _pointers.Remove(e.pointerId));
            layer.RegisterCallback<WheelEvent>(e => CameraRig.Instance?.Zoom(-e.delta.y * 20f));
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
