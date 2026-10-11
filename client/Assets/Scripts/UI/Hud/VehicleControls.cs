using System;
using CTLife.Core;
using CTLife.World;
using CTLife.World.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Touch controls while driving or flying (Feel lane F2, GTA-mobile style). On the road: a floating steer stick on the
    /// left (its x steers), GAS and BRAKE / REVERSE pedals and a HANDBRAKE on the right, the Roadstar's FLY / LAND toggle,
    /// EXIT. In the air: two floating sticks, left = climb/descend and yaw, right = pitch and roll (helicopter, drone); the
    /// flying Roadstar keeps the road layout (stick turns and climbs, gas thrusts). A speed readout sits at the bottom.
    /// Hold-buttons capture their pointer, so several thumbs work at once.
    /// </summary>
    public sealed class VehicleControls : IDisposable
    {
        public VisualElement Element { get; }
        private readonly VisualElement _left, _leftRing, _leftThumb, _right, _rightRing, _rightThumb, _pedals;
        private readonly Button _exit, _fly;
        private readonly Label _speed, _unit, _alt, _exitLabel, _flyLabel;
        private readonly VisualElement _gas, _brake, _hand;
        private Vector2 _leftCenter, _rightCenter, _leftInput, _rightInput;
        private int _leftPointer = -1, _rightPointer = -1;
        private bool _gasDown, _brakeDown, _handDown;
        private Vehicle _vehicle;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly UiRoot _ui;
        private bool _disposed;

        public VehicleControls()
        {
            _leftThumb = El.Div("veh-stick-thumb"); _leftRing = El.Div("veh-stick-ring", _leftThumb);
            _left = El.Div("veh-stick-zone veh-stick-zone--left", _leftRing);
            _rightThumb = El.Div("veh-stick-thumb"); _rightRing = El.Div("veh-stick-ring", _rightThumb);
            _right = El.Div("veh-stick-zone veh-stick-zone--right", _rightRing);
            _gas = Pedal("veh-gas", "vehicle.gas", "up", v => _gasDown = v);
            _brake = Pedal("veh-brake", "vehicle.brake", "down", v => _brakeDown = v);
            _hand = Pedal("veh-handbrake", "vehicle.handbrake", null, v => _handDown = v);
            _pedals = El.Div("veh-pedals", _hand, _brake, _gas);
            _exit = El.Btn("", () => VehicleSystem.Current?.ExitOrLand(), "veh-exit");
            _exitLabel = El.Text(Strings.T("vehicle.exit"), "veh-label");
            _exit.Add(new UiIcon("exit")); _exit.Add(_exitLabel);
            _exit.name = "veh-exit";
            _fly = El.Btn("", ToggleFly, "veh-fly");
            _flyLabel = El.Text(Strings.T("vehicle.fly"), "veh-label");
            _fly.Add(new UiIcon("fly")); _fly.Add(_flyLabel);
            _fly.name = "veh-fly";
            _speed = El.Text("0", "veh-speed-value");
            _unit = El.Text("km/h", "veh-speed-unit");
            _alt = El.Text("", "veh-alt");
            var readout = El.Div("veh-readout", El.Div("veh-speed-row", _speed, _unit), _alt);
            readout.pickingMode = PickingMode.Ignore;
            foreach (var c in readout.Query<VisualElement>().ToList()) c.pickingMode = PickingMode.Ignore;
            Element = El.Div("veh-controls", _left, _right, _pedals, _exit, _fly, readout);
            Element.pickingMode = PickingMode.Ignore;
            Element.name = "vehicle-controls";
            Stick(_left, _leftRing, _leftThumb, true);
            Stick(_right, _rightRing, _rightThumb, false);
            Element.Show(false);
            _ui = UiRoot.Instance;
            _ui?.AddUpdater(Update);
            _subs.On<VehicleChangedSignal>(s => Bind(s.vehicle));
            if (VehicleSystem.Current != null && VehicleSystem.Current.Driving != null) Bind(VehicleSystem.Current.Driving);
        }

        public bool Active => _vehicle != null;
        /// <summary>Editor captures drive the vehicle themselves (the HUD then only shows the controls).</summary>
        public static bool ScriptedInput;

        private void Bind(Vehicle v)
        {
            _vehicle = v;
            ReleaseAll();
            Element.Show(v != null);
            if (v == null) return;
            bool air = VehicleCatalog.Flies(v.Kind);
            _right.Show(air);
            _pedals.Show(!air);
            _fly.Show(v.Kind == VehicleKind.Roadstar);
            Element.EnableInClassList("veh-air", air);
            _exitLabel.text = Strings.T(v.Kind == VehicleKind.Drone ? "vehicle.land" : "vehicle.exit");
        }

        private VisualElement Pedal(string cls, string labelKey, string icon, Action<bool> set)
        {
            var e = El.Div("veh-pedal " + cls);
            if (icon != null) e.Add(new UiIcon(icon));
            e.Add(El.Text(Strings.T(labelKey), "veh-label"));
            e.name = cls;
            e.RegisterCallback<PointerDownEvent>(ev => { e.CapturePointer(ev.pointerId); e.AddToClassList("veh-down"); set(true); ev.StopPropagation(); });
            void Up(int id) { if (e.HasPointerCapture(id)) e.ReleasePointer(id); e.RemoveFromClassList("veh-down"); set(false); }
            e.RegisterCallback<PointerUpEvent>(ev => Up(ev.pointerId));
            e.RegisterCallback<PointerCancelEvent>(ev => Up(ev.pointerId));
            e.RegisterCallback<PointerCaptureOutEvent>(ev => { e.RemoveFromClassList("veh-down"); set(false); });
            return e;
        }

        private void Stick(VisualElement zone, VisualElement ring, VisualElement thumb, bool left)
        {
            zone.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || (left ? _leftPointer : _rightPointer) >= 0) return;
                zone.CapturePointer(e.pointerId);
                var p = zone.WorldToLocal(e.position);
                float r = Mathf.Max(1f, ring.layout.width * 0.5f);
                var size = zone.layout.size;
                p = new Vector2(Mathf.Clamp(p.x, r, Mathf.Max(r, size.x - r)), Mathf.Clamp(p.y, r, Mathf.Max(r, size.y - r)));
                if (left) { _leftPointer = e.pointerId; _leftCenter = p; } else { _rightPointer = e.pointerId; _rightCenter = p; }
                ring.style.left = p.x - r; ring.style.top = p.y - r;
                zone.AddToClassList("veh-active");
                Move(zone, ring, thumb, left, e.position);
                e.StopPropagation();
            });
            zone.RegisterCallback<PointerMoveEvent>(e => { if (e.pointerId == (left ? _leftPointer : _rightPointer)) Move(zone, ring, thumb, left, e.position); });
            void Up(int id)
            {
                if (id != (left ? _leftPointer : _rightPointer)) return;
                if (zone.HasPointerCapture(id)) zone.ReleasePointer(id);
                if (left) { _leftPointer = -1; _leftInput = Vector2.zero; } else { _rightPointer = -1; _rightInput = Vector2.zero; }
                thumb.style.translate = new Translate(0, 0);
                ring.style.left = StyleKeyword.Null; ring.style.top = StyleKeyword.Null;   // back to its resting place
                zone.RemoveFromClassList("veh-active");
            }
            zone.RegisterCallback<PointerUpEvent>(e => Up(e.pointerId));
            zone.RegisterCallback<PointerCancelEvent>(e => Up(e.pointerId));
            zone.RegisterCallback<PointerCaptureOutEvent>(e => Up(e.pointerId));
        }

        private void Move(VisualElement zone, VisualElement ring, VisualElement thumb, bool left, Vector2 panel)
        {
            float r = Mathf.Max(1f, ring.layout.width * 0.5f);
            var c = left ? _leftCenter : _rightCenter;
            var (center, input, knob) = MovementControls.FloatingStick(c, zone.WorldToLocal(panel), r, MovementControls.FollowAt);
            if (center != c) { ring.style.left = center.x - r; ring.style.top = center.y - r; if (left) _leftCenter = center; else _rightCenter = center; }
            thumb.style.translate = new Translate(knob.x, knob.y);
            if (left) _leftInput = input; else _rightInput = input;
        }

        private void ToggleFly()
        {
            var f = _vehicle != null ? _vehicle.GetComponent<FlightPhysics>() : null;
            if (f == null) return;
            f.SetFlight(!f.WantsFlight);
        }

        private void ReleaseAll()
        {
            _leftPointer = _rightPointer = -1;
            _leftInput = _rightInput = Vector2.zero;
            _gasDown = _brakeDown = _handDown = false;
        }

        /// <summary>The sticks' dead zone: nothing below 0.12, then rescaled so full deflection is still 1.</summary>
        public static float Dead(float v) => Mathf.Abs(v) < 0.12f ? 0f : Mathf.Sign(v) * (Mathf.Abs(v) - 0.12f) / 0.88f;

        /// <summary>The input this frame (public for the editor capture harness).</summary>
        public VehicleInput Compose()
        {
            if (_vehicle == null) return VehicleInput.None;
            bool air = VehicleCatalog.Flies(_vehicle.Kind);
            if (air)
                return new VehicleInput { lift = Dead(_leftInput.y), yaw = Dead(_leftInput.x), pitch = Dead(_rightInput.y), steer = Dead(_rightInput.x) };
            var input = new VehicleInput { steer = Dead(_leftInput.x), throttle = _gasDown ? 1f : 0f, brake = _brakeDown ? 1f : 0f, handbrake = _handDown };
            if (_vehicle.Flying) input.lift = Dead(_leftInput.y);
            return input;
        }

        private void Update()
        {
            if (_disposed || _vehicle == null) return;
            var sys = VehicleSystem.Current;
            if (sys == null || sys.Driving != _vehicle) { Bind(sys != null ? sys.Driving : null); return; }
            if (!sys.Busy && !ScriptedInput && !VehicleSystem.ScriptedInput) _vehicle.Input = Compose();
            float kmh = _vehicle.Speed * 3.6f;
            var f = _vehicle.GetComponent<FlightPhysics>();
            bool showAlt = (_vehicle.Flying || VehicleCatalog.Flies(_vehicle.Kind)) && f != null;
            _speed.text = Mathf.RoundToInt(kmh).ToString();
            _alt.text = showAlt ? $"{Mathf.Max(0f, f.Altitude):0} m" : "";
            _alt.Show(showAlt);
            Element.EnableInClassList("veh-reversing", _vehicle.ForwardSpeed < -0.5f && _brakeDown);
            _hand.Show(!_vehicle.Flying);   // no drifting in the air
            if (_vehicle.Kind == VehicleKind.Roadstar && f != null)
            {
                _flyLabel.text = Strings.T(f.WantsFlight ? "vehicle.land" : "vehicle.fly");
                _fly.EnableInClassList("veh-on", f.WantsFlight);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subs.Dispose();
            _ui?.RemoveUpdater(Update);
        }
    }
}
