using System;
using CTLife.Core;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Leads the player to their map pin (<see cref="Waypoint"/>): an arrow floating over their head that turns toward the
    /// pin relative to the camera (straight up = ahead of the camera), the distance under it, and a pin standing over the
    /// destination while it is on screen. Clears the pin on arrival. CT Island only (map x/z are world x/z there).
    /// Driven after the camera moves (InteractionController.PresentationUpdated), so it never lags the view.
    /// </summary>
    public sealed class WaypointGuide : IDisposable
    {
        public VisualElement Element { get; }
        private readonly VisualElement _over, _arrow, _target;
        private readonly Label _distance, _targetDistance;
        private bool _disposed;

        /// <summary>Metres above the player's feet the arrow floats (clear of the name tag).</summary>
        public const float OverheadHeight = 2.55f;

        public WaypointGuide()
        {
            Element = new VisualElement { name = "waypoint-guide", pickingMode = PickingMode.Ignore };
            Element.AddToClassList("ct-guide");
            _arrow = new VisualElement { pickingMode = PickingMode.Ignore };
            _arrow.AddToClassList("ct-guide-arrow");
            _arrow.generateVisualContent += DrawArrow;
            _distance = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            _distance.AddToClassList("ct-guide-distance");
            _over = new VisualElement { pickingMode = PickingMode.Ignore };
            _over.AddToClassList("ct-guide-over");
            _over.Add(_arrow);
            _over.Add(_distance);
            _target = new VisualElement { pickingMode = PickingMode.Ignore };
            _target.AddToClassList("ct-guide-target");
            var pin = new VisualElement { pickingMode = PickingMode.Ignore };
            pin.AddToClassList("ct-guide-pin");
            if (Resources.Load<Texture2D>("UI/Lobby/Icons/place-marker") is Texture2D icon) pin.style.backgroundImage = Background.FromTexture2D(icon);
            _targetDistance = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            _targetDistance.AddToClassList("ct-guide-target-distance");
            _target.Add(pin);
            _target.Add(_targetDistance);
            Element.Add(_target);
            Element.Add(_over);
            Hide();
            InteractionController.PresentationUpdated += Update;
            Waypoint.Changed += OnChanged;
        }

        private void OnChanged() { if (!Waypoint.Active) Hide(); }

        private void Hide()
        {
            _over.style.display = DisplayStyle.None;
            _target.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            if (_disposed || Element.panel == null || Element.parent == null) return;
            var session = VenueSession.Current;
            var me = session != null && session.IsIsland ? session.LocalPlayer : null;
            var cam = CameraRig.Instance != null ? CameraRig.Instance.Camera : Camera.main;
            if (!Waypoint.Active || me == null || cam == null) { Hide(); return; }

            var feet = me.transform.position;
            float distance = Waypoint.DistanceFrom(feet);
            if (distance <= Waypoint.ArriveRadius)
            {
                string label = Waypoint.Label;
                Waypoint.Clear();
                Bus.Publish(ToastSignal.Text(string.IsNullOrEmpty(label) ? Strings.T("hud.arrived") : Strings.F("hud.arrivedAt", label), ToastKind.Good));
                Hide();
                return;
            }
            string text = Format(distance);

            // The overhead arrow: its angle is the pin's bearing relative to where the camera looks, so "up" means ahead.
            var head = feet + Vector3.up * OverheadHeight;
            if (TryPanel(cam, head, out var headPx))
            {
                var camFwd = cam.transform.forward; camFwd.y = 0f;
                var toPin = new Vector3(Waypoint.Atlas.x - feet.x, 0f, Waypoint.Atlas.y - feet.z);
                float bearing = camFwd.sqrMagnitude > 1e-4f && toPin.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(camFwd, toPin, Vector3.up) : 0f;
                _over.style.display = DisplayStyle.Flex;
                _over.style.translate = new Translate(headPx.x, headPx.y);
                _arrow.style.rotate = new Rotate(bearing);
                _distance.text = text;
            }
            else _over.style.display = DisplayStyle.None;

            // The pin over the destination, while it is in front of the camera and on screen.
            float ground = IslandWorld.GroundHeight(Waypoint.Atlas.x, Waypoint.Atlas.y);
            var spot = new Vector3(Waypoint.Atlas.x, float.IsNaN(ground) ? feet.y : ground, Waypoint.Atlas.y) + Vector3.up * 1.6f;
            var bounds = Element.parent.contentRect;
            if (TryPanel(cam, spot, out var spotPx) && bounds.Contains(spotPx))
            {
                _target.style.display = DisplayStyle.Flex;
                _target.style.translate = new Translate(spotPx.x, spotPx.y);
                _targetDistance.text = string.IsNullOrEmpty(Waypoint.Label) ? text : Waypoint.Label + " · " + text;
            }
            else _target.style.display = DisplayStyle.None;
        }

        private bool TryPanel(Camera cam, Vector3 world, out Vector2 local)
        {
            local = default;
            if (cam.WorldToScreenPoint(world).z <= cam.nearClipPlane) return false;
            local = Element.parent.WorldToLocal(RuntimePanelUtils.CameraTransformWorldToPanel(Element.panel, world, cam));
            return true;
        }

        /// <summary>Pure: "45 m", "950 m", "1.2 km".</summary>
        public static string Format(float metres) =>
            metres < 1000f ? Strings.F("hud.distanceM", Mathf.RoundToInt(metres / 5f) * 5) : Strings.F("hud.distanceKm", metres / 1000f);

        /// <summary>A chunky up-pointing arrow (the element rotates it).</summary>
        private static void DrawArrow(MeshGenerationContext ctx)
        {
            var r = ctx.visualElement.contentRect;
            float w = r.width, h = r.height;
            var p = ctx.painter2D;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(new Vector2(w * 0.5f, h * 0.06f));
            p.LineTo(new Vector2(w * 0.94f, h * 0.56f));
            p.LineTo(new Vector2(w * 0.64f, h * 0.56f));
            p.LineTo(new Vector2(w * 0.64f, h * 0.94f));
            p.LineTo(new Vector2(w * 0.36f, h * 0.94f));
            p.LineTo(new Vector2(w * 0.36f, h * 0.56f));
            p.LineTo(new Vector2(w * 0.06f, h * 0.56f));
            p.ClosePath();
            p.fillColor = new Color(0.24f, 0.86f, 0.52f, 1f);
            p.Fill();
            p.strokeColor = new Color(0.16f, 0.04f, 0.29f, 1f);
            p.lineWidth = 6f;
            p.Stroke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            InteractionController.PresentationUpdated -= Update;
            Waypoint.Changed -= OnChanged;
            Hide();
        }
    }
}
