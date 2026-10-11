using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Touch/mouse direct locomotion. All visual sizes and colors belong to the shared stylesheet.</summary>
    public sealed class MovementControls : IDisposable
    {
        private sealed class HeldArrow
        {
            public VisualElement owner;
            public Vector2 direction, origin;
            public float strength;
        }

        public VisualElement Element { get; }
        private readonly VisualElement _zone, _ring, _thumb, _arrows;
        private readonly Label _hint;
        private readonly UiRoot _ui;
        private readonly Dictionary<int, HeldArrow> _heldArrows = new Dictionary<int, HeldArrow>();
        private readonly Subscriptions _subscriptions = new Subscriptions();
        private LocalPlayerController _player;
        private Vector2 _analogInput;
        private int _analogPointer = -1, _epoch;
        private bool _enabled = true, _blocked, _disposed;


#if UNITY_EDITOR && !CTLIFE_PROD
        private readonly bool _reviewInputTrace = LaunchArgs.Flag("review") && LaunchArgs.Flag("reviewInputTrace");
        private int _reviewLines, _reviewMoves, _reviewTicks;
        private double _reviewNextTick;

        // Target-level diagnostics: Unity skips ancestor callbacks once this element captures the pointer.
        // Fixed control/state data only. No chat, user IDs or device/account contents are recorded.
        private void ReviewTrace(string phase, IPointerEvent pointer = null, int capturedId = -1)
        {
            if (!_reviewInputTrace || _reviewLines >= 256) return;
            bool moving = phase == "analog-move" || phase == "arrow-move";
            if (moving && ++_reviewMoves > 8) return;
            _reviewLines++;
            var player = _player;
            if (player == null) player = VenueSession.Current?.LocalPlayer;
            int id = pointer != null ? pointer.pointerId : capturedId >= 0 ? capturedId : _analogPointer;
            var agent = player != null ? player.GetComponent<UnityEngine.AI.NavMeshAgent>() : null;
            bool ready = agent != null && agent.enabled && agent.isOnNavMesh;
            bool arrowCapture = id >= 0 && _heldArrows.TryGetValue(id, out var held) && held.owner.HasPointerCapture(id);
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "CTLIFE [review-movement] {0}", $"phase={phase}; frame={Time.frameCount}; t={Time.realtimeSinceStartupAsDouble:F6}; " +
                $"eventMs={(pointer is EventBase evt ? evt.timestamp : -1)}; id={id}; button={pointer?.button ?? -1}; mask={pointer?.pressedButtons ?? -1}; " +
                $"position={(pointer != null ? pointer.position : Vector3.zero):F2}; local={(pointer != null ? pointer.localPosition : Vector3.zero):F2}; " +
                $"analogPointer={_analogPointer}; zoneCapture={(id >= 0 && _zone.HasPointerCapture(id))}; arrowCapture={arrowCapture}; arrows={_heldArrows.Count}; " +
                $"vector={_analogInput:F3}; epoch={_epoch}; currentEpoch={(player != null ? player.InputEpoch : -1)}; " +
                $"blocked={ReviewBlockReason()}; focused={Application.isFocused}; agentReady={ready}; agentStopped={(ready && agent.isStopped)}; " +
                $"player={(player != null ? player.ReviewSnapshot() : "absent")}");
            if (_reviewLines == 256) Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "CTLIFE [review-movement] Trace reached 256-line session cap; further movement diagnostics disabled.");
        }

        private string ReviewBlockReason()
        {
            if (_disposed) return "disposed";
            if (!_enabled) return "disabled";
            if (Element.panel == null) return "detached";
            for (var ancestor = Element.parent; ancestor != null; ancestor = ancestor.parent)
                if (ancestor.resolvedStyle.display == DisplayStyle.None || ancestor.resolvedStyle.visibility == Visibility.Hidden) return "hidden-ancestor";
            if (_ui != null)
            {
                if (HasOverlay(_ui.PhoneLayer)) return "phone";
                if (HasOverlay(_ui.ScreenLayer)) return "screen";
                if (HasOverlay(_ui.ModalLayer)) return "modal";
                for (var focus = _ui.Root.focusController?.focusedElement as VisualElement; focus != null; focus = focus.parent)
                    if (focus is TextField) return "text-focus";
            }
            return VenueSession.Current?.LocalPlayer == null ? "no-player" : "none";
        }
#endif

        public MovementControls()
        {
            _thumb = El.Div("ct-move-thumb");
            _ring = El.Div("ct-move-ring", _thumb);
            _zone = El.Div("ct-move-zone", _ring);
            _ring.pickingMode = _thumb.pickingMode = PickingMode.Ignore;
            _arrows = El.Div("ct-move-arrows");
            AddArrow("up", Vector2.up);
            AddArrow("down", Vector2.down);
            AddArrow("left", Vector2.left);
            AddArrow("right", Vector2.right);
            _hint = El.Text("", "ct-move-hint");
            _hint.pickingMode = PickingMode.Ignore;
            Element = El.Div("ct-move-controls", _zone, _arrows, _hint);
            Element.pickingMode = PickingMode.Ignore;
#if UNITY_EDITOR && !CTLIFE_PROD
            _zone.RegisterCallback<PointerCaptureEvent>(e => ReviewTrace("analog-captured", null, e.pointerId));
#endif
            _zone.RegisterCallback<PointerDownEvent>(AnalogDown);
            _zone.RegisterCallback<PointerMoveEvent>(AnalogMove);
            _zone.RegisterCallback<PointerUpEvent>(e =>
            {
#if UNITY_EDITOR && !CTLIFE_PROD
                ReviewTrace("analog-up", e, e.pointerId);
#endif
                ReleaseAnalog(e.pointerId);
            });
            _zone.RegisterCallback<PointerCancelEvent>(e =>
            {
#if UNITY_EDITOR && !CTLIFE_PROD
                ReviewTrace("analog-pointer-cancel", e, e.pointerId);
#endif
                ReleaseAnalog(e.pointerId);
            });
            _zone.RegisterCallback<PointerCaptureOutEvent>(e =>
            {
#if UNITY_EDITOR && !CTLIFE_PROD
                ReviewTrace("analog-capture-out", null, e.pointerId);
#endif
                ReleaseAnalog(e.pointerId);
            });
            Element.RegisterCallback<DetachFromPanelEvent>(_ => Cancel(true));
            ControlSettings.Changed += RefreshMode;
            _subscriptions.On<VenueChangedSignal>(_ => Cancel(true));
            _ui = UiRoot.Instance;
            if (_ui != null)
            {
                _ui.AddUpdater(Update);
                _ui.Root.RegisterCallback<PointerDownEvent>(OtherUiPressed, TrickleDown.TrickleDown);
            }
            RefreshMode();
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!enabled) Cancel(true);
            Update();
        }

        private void AddArrow(string id, Vector2 direction)
        {
            // A held directional control is not a Clickable button: release must stop movement, not emit a tap.
            var icon = new UiIcon("back");
            icon.AddToClassList("ct-move-arrow-glyph");
            var arrow = El.Div("ct-move-arrow ct-move-" + id, icon);
            arrow.tooltip = Strings.T("controls." + id);
            _arrows.Add(arrow);
#if UNITY_EDITOR && !CTLIFE_PROD
            arrow.RegisterCallback<PointerCaptureEvent>(e => ReviewTrace("arrow-captured", null, e.pointerId));
#endif
            arrow.RegisterCallback<PointerDownEvent>(e =>
            {
#if UNITY_EDITOR && !CTLIFE_PROD
                _reviewMoves = _reviewTicks = 0; _reviewNextTick = Time.realtimeSinceStartupAsDouble;
                ReviewTrace("arrow-down", e);
#endif
                if (e.button != 0 || !BeginInput()) return;
                _heldArrows[e.pointerId] = new HeldArrow
                {
                    owner = arrow, direction = direction, origin = e.position,
                    strength = DirectMovementInput.ArrowWalkStrength,
                };
                arrow.CapturePointer(e.pointerId);
#if UNITY_EDITOR && !CTLIFE_PROD
                ReviewTrace("arrow-capture-requested", e);
#endif
                arrow.AddToClassList("ct-move-active");
                e.StopPropagation();
                FeedInput();
            });
            arrow.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_heldArrows.TryGetValue(e.pointerId, out var held)) return;
                var delta = (Vector2)e.position - held.origin;
                float outward = Vector2.Dot(new Vector2(delta.x, -delta.y), held.direction);
                float travel = Mathf.Max(1f, Mathf.Max(arrow.layout.width, arrow.layout.height));
                held.strength = Mathf.Lerp(DirectMovementInput.ArrowWalkStrength, 1f, Mathf.Clamp01(outward / travel));
#if UNITY_EDITOR && !CTLIFE_PROD
                ReviewTrace("arrow-move", e);
#endif
                e.StopPropagation();
                FeedInput();
            });
            arrow.RegisterCallback<PointerUpEvent>(e =>
            {
#if UNITY_EDITOR && !CTLIFE_PROD
                ReviewTrace("arrow-up", e, e.pointerId);
#endif
                ReleaseArrow(e.pointerId);
            });
            arrow.RegisterCallback<PointerCancelEvent>(e =>
            {
#if UNITY_EDITOR && !CTLIFE_PROD
                ReviewTrace("arrow-pointer-cancel", e, e.pointerId);
#endif
                ReleaseArrow(e.pointerId);
            });
            arrow.RegisterCallback<PointerCaptureOutEvent>(e =>
            {
#if UNITY_EDITOR && !CTLIFE_PROD
                ReviewTrace("arrow-capture-out", null, e.pointerId);
#endif
                ReleaseArrow(e.pointerId);
            });
        }

        private bool BeginInput()
        {
            if (_disposed || !_enabled || IsBlocked())
            {
#if UNITY_EDITOR && !CTLIFE_PROD
                ReviewTrace("begin-rejected-ui");
#endif
                return false;
            }
            var player = VenueSession.Current?.LocalPlayer;
            if (player == null)
            {
#if UNITY_EDITOR && !CTLIFE_PROD
                ReviewTrace("begin-rejected-no-player");
#endif
                return false;
            }
            if (_analogPointer < 0 && _heldArrows.Count == 0)
            {
                player.CancelMovement();
                _player = player;
                _epoch = player.InputEpoch;
            }
#if UNITY_EDITOR && !CTLIFE_PROD
            ReviewTrace("begin-accepted");
#endif
            return true;
        }

        private void AnalogDown(PointerDownEvent e)
        {
#if UNITY_EDITOR && !CTLIFE_PROD
            _reviewMoves = _reviewTicks = 0; _reviewNextTick = Time.realtimeSinceStartupAsDouble;
            ReviewTrace("analog-down", e);
#endif
            if (e.button != 0 || _analogPointer >= 0 || !BeginInput()) return;
            _analogPointer = e.pointerId;
            _zone.CapturePointer(e.pointerId);
            Element.AddToClassList("ct-move-active");
            SetAnalog(e.position);
#if UNITY_EDITOR && !CTLIFE_PROD
            ReviewTrace("analog-capture-requested", e);
#endif
            e.StopPropagation();
        }

        private void AnalogMove(PointerMoveEvent e)
        {
            if (e.pointerId != _analogPointer) return;
            SetAnalog(e.position);
#if UNITY_EDITOR && !CTLIFE_PROD
            ReviewTrace("analog-move", e);
#endif
            e.StopPropagation();
        }

        private void SetAnalog(Vector2 panelPosition)
        {
            var local = _ring.WorldToLocal(panelPosition) - _ring.contentRect.center;
            float radius = Mathf.Max(1f, (_ring.contentRect.width - _thumb.layout.width) * 0.5f);
            var delta = Vector2.ClampMagnitude(local, radius);
            _analogInput = new Vector2(delta.x, -delta.y) / radius;
            _thumb.style.translate = new Translate(delta.x, delta.y);
            FeedInput();
        }

        private void ReleaseAnalog(int pointerId)
        {
#if UNITY_EDITOR && !CTLIFE_PROD
            ReviewTrace("analog-release-request", null, pointerId);
#endif
            if (pointerId != _analogPointer) return;
            _analogPointer = -1;
            _analogInput = Vector2.zero;
            _thumb.style.translate = new Translate(0f, 0f);
            Element.RemoveFromClassList("ct-move-active");
            if (_zone.HasPointerCapture(pointerId)) _zone.ReleasePointer(pointerId);
            FeedInput();
#if UNITY_EDITOR && !CTLIFE_PROD
            ReviewTrace("analog-released", null, pointerId);
#endif
        }

        private void ReleaseArrow(int pointerId)
        {
            if (!_heldArrows.TryGetValue(pointerId, out var held)) return;
#if UNITY_EDITOR && !CTLIFE_PROD
            ReviewTrace("arrow-release", null, pointerId);
#endif
            _heldArrows.Remove(pointerId);
            held.owner.RemoveFromClassList("ct-move-active");
            if (held.owner.HasPointerCapture(pointerId)) held.owner.ReleasePointer(pointerId);
            FeedInput();
#if UNITY_EDITOR && !CTLIFE_PROD
            ReviewTrace("arrow-released", null, pointerId);
#endif
        }

        private void FeedInput()
        {
            if (_player == null) return;
            var input = _analogInput;
            if (ControlSettings.MovementMode == MovementControlMode.Arrows)
            {
                input = Vector2.zero;
                float strength = 0f;
                foreach (var arrow in _heldArrows.Values)
                {
                    input += arrow.direction;
                    strength = Mathf.Max(strength, arrow.strength);
                }
                input = input.normalized * strength; // Diagonals do not move faster; opposites cancel.
            }
            _player.SetMoveInput(input);
            if (_analogPointer < 0 && _heldArrows.Count == 0) _player = null;
        }

        private void OtherUiPressed(PointerDownEvent e)
        {
            if (!(e.target is VisualElement target) || target == Element || Element.Contains(target)) return;
            if (target == _ui.WorldLayer || _ui.WorldLayer.Contains(target)) return; // Second thumb can pan the camera.
#if UNITY_EDITOR && !CTLIFE_PROD
            ReviewTrace("other-ui-pressed", e);
#endif
            Cancel(true);
        }

        private bool IsBlocked()
        {
            if (Element.panel == null) return true;
            for (var ancestor = Element.parent; ancestor != null; ancestor = ancestor.parent)
                if (ancestor.resolvedStyle.display == DisplayStyle.None || ancestor.resolvedStyle.visibility == Visibility.Hidden) return true;
            if (_ui == null) return false;
            if (HasOverlay(_ui.PhoneLayer) || HasOverlay(_ui.ScreenLayer) || HasOverlay(_ui.ModalLayer)) return true;
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

        private void Update()
        {
            if (_disposed) return;
            bool blocked = !_enabled || IsBlocked() || VenueSession.Current?.LocalPlayer == null;
            if (blocked && !_blocked) Cancel(true);
            else if (_player != null &&
                (_player != VenueSession.Current?.LocalPlayer || _epoch != _player.InputEpoch))
                Cancel(false); // Release the old gesture; do not undo a newly requested seat path/correction.
            _blocked = blocked;
            Element.Show(!blocked);
            if (blocked) return;
            if (_analogPointer >= 0 || _heldArrows.Count > 0) FeedInput();
#if UNITY_EDITOR && !CTLIFE_PROD
            if (_reviewInputTrace && (_analogPointer >= 0 || _heldArrows.Count > 0) && _reviewTicks < 4 && Time.realtimeSinceStartupAsDouble >= _reviewNextTick)
            {
                _reviewTicks++; _reviewNextTick = Time.realtimeSinceStartupAsDouble + .25;
                ReviewTrace("held-tick");
            }
#endif
            bool running = _player != null && _player.IsRunning;
            Element.EnableInClassList("ct-move-running", running);
            _hint.text = Strings.T(running ? "controls.run" : "controls.walk");
        }

        private void RefreshMode()
        {
            Cancel(true);
            bool analog = ControlSettings.MovementMode == MovementControlMode.Analog;
            _zone.Show(analog);
            _arrows.Show(!analog);
            Element.tooltip = Strings.T(analog ? "controls.analogHelp" : "controls.arrowsHelp");
        }

        private void Cancel(bool allMovement)
        {
#if UNITY_EDITOR && !CTLIFE_PROD
            if (_analogPointer >= 0 || _heldArrows.Count > 0 || _player != null)
                ReviewTrace(allMovement ? "cancel-all" : "cancel-stale-owner-or-epoch");
#endif
            // Prefer the actor that owns the gesture while it is still alive.
            // A destroyed Unity wrapper must not suppress the current-player fallback.
            var player = _player;
            if (player == null)
            {
                var venue = VenueSession.Current;
                if (venue != null) player = venue.LocalPlayer;
            }
            int pointer = _analogPointer;
            _analogPointer = -1;
            _analogInput = Vector2.zero;
            if (pointer >= 0 && _zone.HasPointerCapture(pointer)) _zone.ReleasePointer(pointer);
            var arrows = new List<KeyValuePair<int, HeldArrow>>(_heldArrows);
            _heldArrows.Clear();
            foreach (var pair in arrows)
            {
                pair.Value.owner.RemoveFromClassList("ct-move-active");
                if (pair.Value.owner.HasPointerCapture(pair.Key)) pair.Value.owner.ReleasePointer(pair.Key);
            }
            _thumb.style.translate = new Translate(0f, 0f);
            Element.RemoveFromClassList("ct-move-active");
            Element.RemoveFromClassList("ct-move-running");
            if (player != null)
            {
                if (allMovement) player.CancelMovement();
                else player.StopDirectInput();
            }
            _player = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Cancel(true);
            _disposed = true;
            ControlSettings.Changed -= RefreshMode;
            _subscriptions.Dispose();
            if (_ui != null)
            {
                _ui.RemoveUpdater(Update);
                _ui.Root.UnregisterCallback<PointerDownEvent>(OtherUiPressed, TrickleDown.TrickleDown);
            }
        }
    }
}
