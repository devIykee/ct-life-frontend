using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.UI.DS;
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
        private readonly VisualElement _actions, _pointer, _where, _meta;
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
            // Design system (SYSTEM.md 3.10, mockup 01a): a where-chip, the main orb + label pill (+ meta chip under it),
            // at most one secondary glass orb. The pointer tail is kept for the capture harness but not drawn.
            _actions = El.Div("ct-interact-actions ds-prompt-acts");
            _pointer = El.Div("ct-interact-pointer");
            _tag = El.Text("", "ct-interact-tag");
            _tag.pickingMode = PickingMode.Ignore;
            _where = El.PassThrough(El.Div("ds-prompt-where", _tag));
            _where.Show(false);
            _sub = El.Text("", "ct-interact-sub");
            _sub.pickingMode = PickingMode.Ignore;
            _meta = El.PassThrough(El.Div("ds-prompt-meta", _sub));
            _meta.Show(false);
            Element = El.Div("ct-hud-interact ds-prompt ds-prompt-pop", _pointer, _where, _actions);
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
            key.Add(OrbIcon("check", true, false));
            primary.Q(className: "ds-orb")?.AddToClassList("ds-orb--held");
            primary.AddToClassList("ct-interact-held");
            _holdUntil = Time.unscaledTime + 0.32f;
            Element.schedule.Execute(() => Element.AddToClassList("ds-prompt-out")).StartingIn(200);
            Element.schedule.Execute(() =>
            {
                Element.RemoveFromClassList("ds-prompt-out");
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
            Element.RemoveFromClassList("ds-in");
            _sub.text = s.sub ?? "";
            _meta.Show(any && !string.IsNullOrEmpty(s.sub));
            foreach (var tone in new[] { "good", "warn", "bad" }) _meta.EnableInClassList("ds-prompt-meta--" + tone, s.subTone == tone);
            if (!any) return;
            // The object's name on the where-chip (UPPERCASE in code: USS has no text-transform)
            string tag = string.IsNullOrEmpty(s.tagKey) ? "" : Strings.T(s.tagKey);
            if (tag == s.tagKey) tag = "";   // no string for this object: no chip rather than a raw key
            if (tag.Length > 0 && s.tagLocked) tag += " \u00b7 " + Strings.T("vehicle.locked");
            _tag.text = tag.ToUpperInvariant();
            _where.Show(tag.Length > 0);
            // Sleuth heat (backend #17): a steal's heat chip reads in the danger colour, like the wanted stars
            _where.EnableInClassList("ds-prompt-where--heat", s.tagKey != null && s.tagKey.StartsWith("heat.", System.StringComparison.Ordinal));
            bool mainPlaced = false;
            foreach (var a in s.actions)
            {
                // SYSTEM.md 3.10: one main orb. [Inference] Objects offering 3+ verbs keep them all (dropping one would
                // remove a feature); they render as extra glass orbs until those offers are trimmed at the source.
                bool main = a.primary && !mainPlaced;
                var id = a.id;
                var label = Strings.T(a.labelKey);
                var owner = s.owner;
                var target = s.targetId;
                var button = new Button(() =>
                    { if (!_disposed && owner != null && InteractionController.Current == owner) owner.Perform(id, target); })
                    { text = "", enableRichText = false };
                bool locked = id == "buy" || (id == "ext" && s.tagLocked);
                bool quiet = main && s.style == "jobquiet";
                bool danger = main && s.style == "jobred";
                bool off = main && s.disabled;
                button.AddToClassList("ct-interact");
                button.AddToClassList(main ? "ct-interact-primary" : "ct-interact-secondary");
                button.AddToClassList("ds-orb");
                button.AddToClassList(off ? "ds-orb--off" : danger ? "ds-orb--danger" : quiet ? "ds-orb--quiet" : main ? "ds-orb--main" : "ds-orb--alt");
                if (off) button.AddToClassList("ct-interact-disabled");
                if (locked) button.AddToClassList("ct-interact-locked");
                button.name = "scene-action-" + id.Replace(':', '-');
                button.tooltip = label;
                if (main && !off && !danger && !quiet) button.Add(El.PassThrough(El.Div("ds-orb-ring")));
                var key = El.PassThrough(El.Div("ct-interact-key ds-orb-key", OrbIcon(a.icon, main && !off && !danger && !quiet, off, main ? 60 : 44)));
                button.Add(key);
                if (locked) button.Add(El.PassThrough(El.Div("ct-interact-lock ds-orb-lock", OrbIcon("locked", false, false, 24))));
                var verb = El.Text(label, "ct-interact-label");
                var col = El.PassThrough(El.Div("ds-prompt-act", button, El.PassThrough(El.Div("ds-prompt-label", verb))));
                if (main) { col.Add(_meta); _actions.Insert(0, col); mainPlaced = true; }
                else _actions.Add(col);
                if (main && s.busy && job) { _busyLabel = verb; _busyText = label; _dots = 0; _busyPulse.Resume(); }
            }
            if (!mainPlaced) Element.Add(_meta);
            Element.EnableInClassList("ct-interact-busy", s.busy);
            Element.EnableInClassList("ds-prompt-busy", s.busy);
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
            // every prompt pops in (opacity + scale 160 ms, USS transition) one frame after it becomes visible
            if (visible) Element.schedule.Execute(() => Element.AddToClassList("ds-in"));
            else Element.RemoveFromClassList("ds-in");
        }

        /// <summary>An action glyph (UiIcon's kind names) as a design-system icon: 60 on the main orb, 44 on the secondary.</summary>
        private static VisualElement OrbIcon(string kind, bool onAccent, bool off, int size = 0)
        {
            var e = Ds.Icon(null, size > 0 ? size : onAccent || off ? 60 : 44, off ? "ink2" : onAccent ? "on-accent" : "white");
            var t = UiIcon.TextureFor(kind);
            if (t != null) e.style.backgroundImage = Background.FromTexture2D(t);
            return e;
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
