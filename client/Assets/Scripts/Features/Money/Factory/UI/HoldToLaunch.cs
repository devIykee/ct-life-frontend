using System;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// "HOLD TO LAUNCH" (brief beat 3): a red pill whose rim fills clockwise over <see cref="HoldSeconds"/> while it is held;
    /// releasing early cancels and the ring drains. It spends up to $1,005 at once and is final, so a tap never fires.
    /// </summary>
    public sealed class HoldToLaunch : VisualElement
    {
        public const float HoldSeconds = 0.8f;
        private readonly Label _label;
        private readonly VisualElement _ring;
        private readonly Action _fired;
        private float _progress;
        private bool _down, _fired1;
        private int _pointer = -1;
        private IVisualElementScheduledItem _tick;
        private double _last;

        public bool Locked { get; set; }

        public HoldToLaunch(string label, Action fired)
        {
            _fired = fired;
            AddToClassList("rf-hold");
            name = "factory-hold";
            _ring = new VisualElement { pickingMode = PickingMode.Ignore };
            _ring.AddToClassList("rf-hold-ring");
            _ring.generateVisualContent += Draw;
            Add(_ring);
            _label = El.Text(label); _label.pickingMode = PickingMode.Ignore;
            _label.AddToClassList("rf-hold-label");
            Add(_label);
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCancelEvent>(_ => Release());
            RegisterCallback<PointerCaptureOutEvent>(_ => Release());
            _tick = schedule.Execute(Step).Every(16);
            _tick.Pause();
        }

        public float Progress => _progress;

        /// <summary>Dev device route: press and hold as a finger would (the ring fills on screen, then it fires).</summary>
        public void SimulateHold()
        {
            if (Locked || _fired1 || !enabledInHierarchy) return;
            _down = true;
            AddToClassList("rf-hold--down");
            _last = Time.realtimeSinceStartupAsDouble;
            _tick.Resume();
        }

        public void Reset()
        {
            _progress = 0f; _down = false; _fired1 = false;
            RemoveFromClassList("rf-hold--down");
            _ring.MarkDirtyRepaint();
        }

        private void OnDown(PointerDownEvent e)
        {
            if (Locked || _fired1 || !enabledInHierarchy) return;
            _down = true; _pointer = e.pointerId;
            this.CapturePointer(e.pointerId);
            AddToClassList("rf-hold--down");
            _last = Time.realtimeSinceStartupAsDouble;
            _tick.Resume();
            Bus.Publish(new SfxSignal("sfx_ui_tap"));
            e.StopPropagation();
        }

        private void OnUp(PointerUpEvent e)
        {
            if (e.pointerId == _pointer && this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            Release();
        }

        private void Release()
        {
            _down = false;
            RemoveFromClassList("rf-hold--down");
            if (!_fired1) { _last = Time.realtimeSinceStartupAsDouble; _tick.Resume(); }
        }

        private void Step()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float dt = (float)Math.Min(0.1, now - _last);
            _last = now;
            if (_fired1) { _tick.Pause(); return; }
            _progress = _down ? Mathf.Min(1f, _progress + dt / HoldSeconds) : Mathf.Max(0f, _progress - dt / (HoldSeconds * 0.5f));
            _ring.MarkDirtyRepaint();
            if (_progress >= 1f && _down)
            {
                _fired1 = true; _down = false;
                RemoveFromClassList("rf-hold--down");
                _tick.Pause();
                _fired?.Invoke();
            }
            else if (!_down && _progress <= 0f) _tick.Pause();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            if (_progress <= 0f) return;
            var r = _ring.contentRect;
            if (r.width <= 0 || r.height <= 0) return;
            var p = ctx.painter2D;
            float w = 7f, rad = r.height * 0.5f - w * 0.5f;
            p.lineWidth = w;
            p.strokeColor = new Color(1f, 0.82f, 0.25f, 1f);
            p.lineCap = LineCap.Round;
            // a stadium outline traced from the top centre, clockwise, cut at the progress
            float straight = r.width - 2f * (rad + w * 0.5f);
            float perimeter = 2f * straight + 2f * Mathf.PI * rad;
            float left = _progress * perimeter;
            float cx0 = r.x + w * 0.5f + rad, cx1 = cx0 + straight, top = r.y + w * 0.5f, bottom = r.yMax - w * 0.5f, cy = r.center.y;
            p.BeginPath();
            float mid = r.center.x;
            p.MoveTo(new Vector2(mid, top));
            left = Seg(p, new Vector2(mid, top), new Vector2(cx1, top), left);
            if (left > 0) left = Arc(p, new Vector2(cx1, cy), rad, -90f, 90f, left);
            if (left > 0) left = Seg(p, new Vector2(cx1, bottom), new Vector2(cx0, bottom), left);
            if (left > 0) left = Arc(p, new Vector2(cx0, cy), rad, 90f, 270f, left);
            if (left > 0) Seg(p, new Vector2(cx0, top), new Vector2(mid, top), left);
            p.Stroke();
        }

        private static float Seg(Painter2D p, Vector2 a, Vector2 b, float left)
        {
            float len = Vector2.Distance(a, b);
            if (left >= len) { p.LineTo(b); return left - len; }
            p.LineTo(Vector2.Lerp(a, b, left / Mathf.Max(1e-3f, len)));
            return 0f;
        }

        private static float Arc(Painter2D p, Vector2 c, float rad, float from, float to, float left)
        {
            float len = Mathf.Deg2Rad * (to - from) * rad;
            float end = left >= len ? to : from + (to - from) * left / len;
            p.Arc(c, rad, Angle.Degrees(from), Angle.Degrees(end));
            return Mathf.Max(0f, left - len);
        }
    }
}
