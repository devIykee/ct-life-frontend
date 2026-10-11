using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Explicit object actions beside the selected scene target. Tracks the camera after its LateUpdate; live HUD
    /// bounds reserve touch controls. No target selection, movement or seat claim happens during presentation.
    /// </summary>
    public sealed class InteractionPrompt : IDisposable
    {
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly VisualElement _actions, _pointer;
        private readonly List<Rect> _blocked = new List<Rect>();
        private VisualElement[] _obstacles = Array.Empty<VisualElement>();
        private InteractionPromptSignal _state;
        private Vector2? _previousOffset;
        private Vector2? _translation, _pointerTranslation;
        private int _contentFrame = -1;
        private bool _disposed, _presented;
        public VisualElement Element { get; }

        public InteractionPrompt()
        {
            _actions = El.Div("ct-interact-actions");
            _pointer = El.Div("ct-interact-pointer");
            Element = El.Div("ct-hud-interact", _pointer, _actions);
            Element.name = "scene-interaction";
            Element.pickingMode = _pointer.pickingMode = _actions.pickingMode = PickingMode.Ignore;
            Element.style.position = Position.Absolute;
            Element.style.left = 0;
            Element.style.top = 0;
            Element.style.right = StyleKeyword.Auto;
            Element.style.bottom = StyleKeyword.Auto;
            Element.style.visibility = Visibility.Hidden;
            _pointer.style.left = 0;
            _pointer.style.top = 0;
            Element.Show(false);
            _subs.On<InteractionPromptSignal>(Render);
            _subs.On<InteractionToastSignal>(s => Bus.Publish(ToastSignal.Text(Strings.T(s.key), ToastKind.Bad)));
            InteractionController.PresentationUpdated += UpdatePosition;
            Element.RegisterCallback<AttachToPanelEvent>(OnAttach);
            if (InteractionController.Current != null) Render(InteractionController.Current.GetPrompt());
        }

        public void SetObstacles(params VisualElement[] obstacles) => _obstacles = obstacles ?? Array.Empty<VisualElement>();

        private void OnAttach(AttachToPanelEvent _)
        {
            if (InteractionController.Current != null) Render(InteractionController.Current.GetPrompt());
        }

        private void Render(InteractionPromptSignal s)
        {
            if (_disposed) return;
            if (_state.targetId != s.targetId || _state.owner != s.owner || _state.anchor.followsActor != s.anchor.followsActor)
                _previousOffset = null;
            _state = s;
            _contentFrame = Time.frameCount;
            _actions.Clear();
            bool any = s.actions != null && s.actions.Count > 0;
            Element.Show(any);
            SetPresented(false); // Wait for a measured action row; never flash at layout origin.
            if (!any) return;
            foreach (var a in s.actions)
            {
                var id = a.id;
                var label = Strings.T(a.labelKey);
                var owner = s.owner;
                var target = s.targetId;
                var button = El.Btn("", () =>
                    { if (!_disposed && owner != null && InteractionController.Current == owner) owner.Perform(id, target); },
                    a.primary ? "ct-interact ct-interact-primary" : "ct-interact ct-interact-secondary");
                button.name = "scene-action-" + id.Replace(':', '-');
                button.tooltip = label;
                button.Add(new UiIcon(a.icon));
                button.Add(El.Text(label, "ct-interact-label"));
                _actions.Add(button);
            }
            Element.EnableInClassList("ct-interact-busy", s.busy);
        }

        /// <summary>Public for the Editor capture harness; production is driven after CameraRig's LateUpdate.</summary>
        public void UpdatePosition()
        {
            if (_disposed || Time.frameCount <= _contentFrame || Element.panel == null || Element.parent == null || _state.actions == null || _state.actions.Count == 0) return;
            var parent = Element.parent;
            for (var e = parent; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden) return;
            var cam = CameraRig.Instance != null ? CameraRig.Instance.Camera : Camera.main;
            if (cam == null || !_state.anchor.TryGetPosition(out var world) || cam.WorldToScreenPoint(world).z <= cam.nearClipPlane)
            { SetPresented(false); return; }

            var point = parent.WorldToLocal(RuntimePanelUtils.CameraTransformWorldToPanel(Element.panel, world, cam));
            var bounds = parent.contentRect;
            var safe = Rect.MinMaxRect(bounds.xMin + ContextPromptPlacement.EdgeInset, bounds.yMin + ContextPromptPlacement.EdgeInset,
                bounds.xMax - ContextPromptPlacement.EdgeInset, bounds.yMax - ContextPromptPlacement.EdgeInset);
            _blocked.Clear();
            foreach (var obstacle in _obstacles)
            {
                if (obstacle == null || !Visible(obstacle) || obstacle.layout.width <= 0 || obstacle.layout.height <= 0) continue;
                var r = obstacle.worldBound;
                var min = parent.WorldToLocal(r.min); var max = parent.WorldToLocal(r.max);
                _blocked.Add(Rect.MinMaxRect(min.x, min.y, max.x, max.y));
            }
            if (!ContextPromptPlacement.TryPlace(point, Element.layout.size, safe, _blocked, out var placed, _previousOffset))
            { SetPresented(false); return; }
            // Keep the intrinsic row layout at the origin. Moving left/top changes the available width near an
            // edge, and doing that from GeometryChanged can repeatedly invalidate the same layout pass (Panel.Pick
            // forces it during QA/input). A transform moves the hit target without recomputing its flex layout.
            Move(Element, placed.position - Element.layout.position, ref _translation);
            SetPresented(true);
            _previousOffset = placed.position - point;
            Element.EnableInClassList("ct-interact-clamped", Mathf.Abs(placed.center.x - point.x) > 1 || Mathf.Abs(placed.yMax + ContextPromptPlacement.Gap - point.y) > 1);

            // Attach to the actual button row, not its outer reserved tail padding. USS may hide displaced tails.
            float pointerSize = _pointer.layout.width > 0 ? _pointer.layout.width : 16f;
            float x = Mathf.Clamp(point.x - placed.x, pointerSize, Mathf.Max(pointerSize, placed.width - pointerSize));
            bool below = point.y < placed.center.y;
            var tail = new Vector2(x - pointerSize * .5f, (below ? _actions.layout.yMin : _actions.layout.yMax) - pointerSize * .5f);
            Move(_pointer, tail - _pointer.layout.position, ref _pointerTranslation);
            Element.EnableInClassList("ct-interact-below", below);
        }

        private static void Move(VisualElement element, Vector2 position, ref Vector2? previous)
        {
            if (previous.HasValue && (previous.Value - position).sqrMagnitude < .0001f) return;
            element.style.translate = new Translate(position.x, position.y);
            previous = position;
        }

        private void SetPresented(bool visible)
        {
            if (_presented == visible) return;
            _presented = visible;
            Element.style.visibility = visible ? Visibility.Visible : Visibility.Hidden;
        }

        private static bool Visible(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            InteractionController.PresentationUpdated -= UpdatePosition;
            Element.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            _subs.Dispose();
            _obstacles = Array.Empty<VisualElement>();
            Element.Show(false);
        }
    }
}
