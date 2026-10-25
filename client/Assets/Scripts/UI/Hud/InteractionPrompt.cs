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
        private readonly Label _tag, _sub;
        private Label _busyLabel;
        private string _busyText;
        private int _dots;
        private IVisualElementScheduledItem _busyPulse;
        private float _holdUntil;
        private bool _pendingRender;
        private readonly List<Rect> _blocked = new List<Rect>();
        private VisualElement[] _obstacles = Array.Empty<VisualElement>();
        private InteractionPromptSignal _state;
        private Vector2? _previousOffset;
        private Vector2? _translation, _pointerTranslation;
        private int _contentFrame = -1;
        private bool _disposed, _presented;
        private float _quietUntil;
        public VisualElement Element { get; }

        public InteractionPrompt()
        {
            _actions = El.Div("ct-interact-actions");
            _pointer = El.Div("ct-interact-pointer");
            _tag = El.Text("", "ct-interact-tag");
            _tag.pickingMode = PickingMode.Ignore;
            _sub = El.Text("", "ct-interact-sub");
            _sub.pickingMode = PickingMode.Ignore;
            _sub.Show(false);
            Element = El.Div("ct-hud-interact", _pointer, _tag, _actions, _sub);
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
            // Just out of a vehicle: no prompt for half a second, so the car you left doesn't flash "Drive".
            _subs.On<CTLife.World.Vehicles.VehicleChangedSignal>(s => { if (s.vehicle == null) _quietUntil = Time.unscaledTime + 0.5f; });
            _subs.On<InteractionToastSignal>(s => Bus.Publish(ToastSignal.Text(Strings.T(s.key), ToastKind.Bad)));
            _subs.On<InteractionDoneSignal>(_ => FlashDone());
            _busyPulse = Element.schedule.Execute(PulseDots).Every(320);
            _busyPulse.Pause();
            InteractionController.PresentationUpdated += UpdatePosition;
            Element.RegisterCallback<AttachToPanelEvent>(OnAttach);
            if (InteractionController.Current != null) Render(InteractionController.Current.GetPrompt());
        }

        public void SetObstacles(params VisualElement[] obstacles) => _obstacles = obstacles ?? Array.Empty<VisualElement>();

        private void OnAttach(AttachToPanelEvent _)
        {
            if (InteractionController.Current != null) Render(InteractionController.Current.GetPrompt());
        }

        /// <summary>A job action succeeded (Rekt Burger's Clock in): the primary key holds pressed with a check for 200 ms,
        /// then the prompt fades out over 120 ms before the next state renders.</summary>
        private void FlashDone()
        {
            if (_disposed || _actions.childCount == 0) return;
            var primary = _actions[0];
            var key = primary.Q(className: "ct-interact-key");
            if (key == null) return;
            key.Clear();
            key.Add(new UiIcon("check"));
            primary.AddToClassList("ct-interact-held");
            _holdUntil = Time.unscaledTime + 0.32f;
            Element.schedule.Execute(() => Element.AddToClassList("ct-interact-out")).StartingIn(200);
            Element.schedule.Execute(() =>
            {
                Element.RemoveFromClassList("ct-interact-out");
                if (_pendingRender) { _pendingRender = false; Render(_state); }
            }).StartingIn(330);
        }

        private void PulseDots()
        {
            if (_busyLabel == null || _busyText == null) return;
            _dots = (_dots + 1) % 4;
            _busyLabel.text = _busyText + new string('.', _dots);
        }

        private void Render(InteractionPromptSignal s)
        {
            if (_disposed) return;
            if (Time.unscaledTime < _holdUntil) { _state = s; _pendingRender = true; return; }
            if (_state.targetId != s.targetId || _state.owner != s.owner || _state.anchor.followsActor != s.anchor.followsActor)
                _previousOffset = null;
            _state = s;
            _contentFrame = Time.frameCount;
            _actions.Clear();
            _busyLabel = null; _busyText = null; _busyPulse.Pause();
            bool any = s.actions != null && s.actions.Count > 0;
            Element.Show(any);
            SetPresented(false); // Wait for a measured action row; never flash at layout origin.
            bool job = s.style != null && s.style.StartsWith("job", StringComparison.Ordinal);
            Element.EnableInClassList("ct-interact-jobprompt", job);
            Element.RemoveFromClassList("ct-interact-in");
            _sub.text = s.sub ?? "";
            _sub.Show(any && !string.IsNullOrEmpty(s.sub));
            foreach (var tone in new[] { "good", "warn", "bad" }) _sub.EnableInClassList("ct-interact-sub--" + tone, s.subTone == tone);
            if (!any) return;
            // City HUD v2: the object's name on a chip, then each verb as a squircle with one glyph and the verb under it.
            string tag = string.IsNullOrEmpty(s.tagKey) ? "" : Strings.T(s.tagKey);
            if (tag == s.tagKey) tag = "";   // no string for this object: no chip rather than a raw key
            if (tag.Length > 0 && s.tagLocked) tag += " \u00b7 " + Strings.T("vehicle.locked");
            _tag.text = tag.ToUpperInvariant();
            _tag.Show(tag.Length > 0);
            // Sleuth heat (backend #17): a steal's heat chip is red, like the wanted stars' alarm colour
            _tag.EnableInClassList("ct-interact-tag--heat", s.tagKey != null && s.tagKey.StartsWith("heat.", System.StringComparison.Ordinal));
            foreach (var a in s.actions)
            {
                var id = a.id;
                var label = Strings.T(a.labelKey);
                var owner = s.owner;
                var target = s.targetId;
                var button = El.Btn("", () =>
                    { if (!_disposed && owner != null && InteractionController.Current == owner) owner.Perform(id, target); },
                    a.primary ? "ct-interact ct-interact-primary" : "ct-interact ct-interact-secondary");
                if (id == "buy" || (id == "ext" && s.tagLocked)) button.AddToClassList("ct-interact-locked");
                if (a.primary && job) button.AddToClassList(s.style == "jobquiet" ? "ct-interact-quiet" : "ct-interact-job");
                if (a.primary && s.disabled) button.AddToClassList("ct-interact-disabled");
                button.name = "scene-action-" + id.Replace(':', '-');
                button.tooltip = label;
                var key = El.PassThrough(El.Div("ct-interact-key", new UiIcon(a.icon)));
                if (id == "buy" || (id == "ext" && s.tagLocked)) key.Add(El.PassThrough(El.Div("ct-interact-lock", new UiIcon("locked"))));
                button.Add(key);
                var verb = El.Text(label, "ct-interact-label");
                button.Add(El.PassThrough(verb));
                _actions.Add(button);
                if (a.primary && s.busy && job) { _busyLabel = verb; _busyText = label; _dots = 0; _busyPulse.Resume(); }
            }
            Element.EnableInClassList("ct-interact-busy", s.busy);
        }

        /// <summary>Public for the Editor capture harness; production is driven after CameraRig's LateUpdate.</summary>
        public void UpdatePosition()
        {
            if (!_disposed && Time.unscaledTime < _quietUntil) { SetPresented(false); return; }
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
            // The local player is an obstacle too: an object's action (the door's Leave, a seat's Sit) never covers the
            // character it is offered to (it covered the player on arrival in dark venues). Prompts that follow the actor
            // already sit above the head.
            if (!_state.anchor.followsActor && LocalPlayerRect(parent, cam, out var me)) _blocked.Add(me);
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

        /// <summary>The local player's on-screen box (feet to just above the head), in <paramref name="parent"/> space.</summary>
        public static bool LocalPlayerRect(VisualElement parent, Camera cam, out Rect rect)
        {
            rect = default;
            var me = VenueSession.Current != null ? VenueSession.Current.LocalPlayer : null;
            if (me == null || cam == null) return false;
            var feet = me.transform.position;
            if (cam.WorldToScreenPoint(feet).z <= cam.nearClipPlane) return false;
            var f = parent.WorldToLocal(RuntimePanelUtils.CameraTransformWorldToPanel(parent.panel, feet, cam));
            var h = parent.WorldToLocal(RuntimePanelUtils.CameraTransformWorldToPanel(parent.panel, feet + Vector3.up * 1.9f, cam));
            rect = PlayerBox(f, h);
            return true;
        }

        /// <summary>Pure: a box from projected feet and head points (width ~0.55 of the height).</summary>
        public static Rect PlayerBox(Vector2 feet, Vector2 head)
        {
            float height = Mathf.Max(8f, Mathf.Abs(feet.y - head.y));
            float cx = (feet.x + head.x) * 0.5f, half = height * 0.28f;
            return Rect.MinMaxRect(cx - half, Mathf.Min(head.y, feet.y), cx + half, Mathf.Max(head.y, feet.y));
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
            // the job prompt pops in (opacity + scale, USS transition) one frame after it becomes visible
            if (visible && Element.ClassListContains("ct-interact-jobprompt"))
                Element.schedule.Execute(() => Element.AddToClassList("ct-interact-in"));
            else if (!visible) Element.RemoveFromClassList("ct-interact-in");
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
