using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.World;
using CTLife.World.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// City HUD v2 (2026-10-22, hud-research-20261022/hud-spec.md): smooth show/hide for HUD groups. Fading in, the element
    /// is displayed with the off class and the class is dropped a frame later, so the USS transition runs; fading out, it
    /// takes the off class and leaves the layout when the transition ends.
    /// </summary>
    public static class HudFade
    {
        public const int Millis = 170;
        private static readonly Dictionary<VisualElement, IVisualElementScheduledItem> Jobs = new Dictionary<VisualElement, IVisualElementScheduledItem>();

        public static void Set(VisualElement e, bool on, string offClass = "hud-off")
        {
            if (e == null) return;
            if (Jobs.TryGetValue(e, out var job)) { job.Pause(); Jobs.Remove(e); }
            bool hidden = e.style.display == DisplayStyle.None;
            if (on)
            {
                e.style.display = DisplayStyle.Flex;
                if (!hidden || e.panel == null) { e.RemoveFromClassList(offClass); return; }
                e.AddToClassList(offClass);
                var item = e.schedule.Execute(() => { e.RemoveFromClassList(offClass); Jobs.Remove(e); });
                item.StartingIn(20);
                Jobs[e] = item;
            }
            else
            {
                if (hidden) return;
                e.AddToClassList(offClass);
                if (e.panel == null) { e.style.display = DisplayStyle.None; return; }
                var item = e.schedule.Execute(() => { e.style.display = DisplayStyle.None; Jobs.Remove(e); });
                item.StartingIn(Millis);
                Jobs[e] = item;
            }
        }
    }

    /// <summary>
    /// The vehicle HUD (City HUD v2, GTA order). Ground: floating steer stick left; a tall Gas pedal in the corner with Brake
    /// beside it; Drift, Horn and Get out as glass rounds above. Helicopter (simple flight): the left stick moves, one vertical
    /// Lift lever feeds the analog lift and springs back to centre (hold altitude), the rotor spin-up rings the knob, and Land
    /// becomes Get out on the ground. Hovercar: the car layout with Fly in the Drift slot; in the air the stick's Y is height,
    /// that slot is Land and Get out hides. Speed (and ALT when flying) sit on plates under the minimap. Pro flight keeps the
    /// two-stick mapping. Every hold captures its pointer so simultaneous thumbs work.
    /// </summary>
    public sealed class VehicleControls : IDisposable
    {
        public VisualElement Element { get; }
        private readonly VisualElement _left, _leftRing, _leftThumb, _right, _rightRing, _rightThumb;
        private readonly VisualElement _gas, _brake, _drift, _horn, _exit, _fly, _land;
        private readonly VisualElement _lever, _leverFill, _leverKnob, _altPlate;
        private readonly RotorRing _rotor;
        private readonly SpeedGauge _gauge;
        private readonly Label _speed, _name, _alt, _altNote, _landCaption, _flyCaption;
        private readonly VisualElement _landIconLand, _landIconExit;
        private Vector2 _leftCenter, _rightCenter, _leftInput, _rightInput;
        private int _leftPointer = -1, _rightPointer = -1, _leverPointer = -1;
        private float _leverValue, _leverY0, _hornAt = -10f;
        private bool _gasDown, _brakeDown, _handDown;
        private bool _simple, _canExitShown;
        private Vector2 _orbitPoint;
        private Vehicle _vehicle;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly UiRoot _ui;
        private bool _disposed, _paused, _focused, _wroteInput;
        private sealed class Hold
        {
            public VisualElement element;
            public Action<bool> set;
            public Action tap;
            public int pointer = -1;
        }
        private readonly List<Hold> _holds = new List<Hold>(8);

        public VehicleControls()
        {
            _leftThumb = El.Div("veh-stick-thumb");
            _leftRing = El.Div("veh-stick-ring",
                Tick("tick-left", "veh-tick veh-tick--left"), Tick("tick-right", "veh-tick veh-tick--right"),
                Tick("tick-up", "veh-tick veh-tick--up"), Tick("tick-down", "veh-tick veh-tick--down"),
                Tick("tick-up2", "veh-tick veh-tick--up2"), Tick("tick-down2", "veh-tick veh-tick--down2"), _leftThumb);
            _left = El.Div("veh-stick-zone veh-stick-zone--left", _leftRing);
            _rightThumb = El.Div("veh-stick-thumb"); _rightRing = El.Div("veh-stick-ring", _rightThumb);
            _right = El.Div("veh-stick-zone veh-stick-zone--right", _rightRing);

            _gas = Pedal("veh-gas", "gas", v => _gasDown = v);
            _brake = Pedal("veh-brake", "brake", v => _brakeDown = v);
            _brake.Add(El.Text("R", "veh-reverse"));
            _drift = Round("veh-drift", "drift", "vehicle.handbrake", v => _handDown = v, null, out _);
            _horn = Round("veh-horn", "horn", "vehicle.horn", v => { if (v) Honk(); }, null, out _);
            _exit = Round("veh-exit", "exit", "vehicle.exit", null, () => { if (ExitAllowed()) VehicleSystem.Current?.ExitOrLand(); }, out _);
            _fly = Round("veh-fly", null, "vehicle.fly", null, ToggleFly, out _flyCaption);
            _fly.Insert(0, Icon("fly", "veh-icon-fly")); _fly.Insert(1, Icon("land", "veh-icon-land"));
            _land = Round("veh-land", null, "vehicle.land", null, () => VehicleSystem.Current?.ExitOrLand(), out _landCaption);
            _landIconLand = Icon("land", "veh-icon-fly"); _landIconExit = Icon("exit", "veh-icon-land");
            _land.Insert(0, _landIconLand); _land.Insert(1, _landIconExit);

            // Lift lever: a glass track, a mint fill from the centre detent to the knob, the knob with the rotor ring.
            _rotor = new RotorRing();
            _leverKnob = El.Div("veh-lever-knob", Icon("lift", "veh-lever-glyph"), _rotor);
            _leverFill = El.Div("veh-lever-fill");
            _lever = El.Div("veh-lever", Tick("tick-up", "veh-lever-end veh-lever-end--up"), Tick("tick-down", "veh-lever-end veh-lever-end--down"),
                _leverFill, _leverKnob, Caption("vehicle.lift", "veh-lever-caption"));
            _lever.name = "veh-lift";
            Lever(_lever);

            var cluster = El.Div("veh-cluster", _brake, _gas, _horn, _exit, _drift, _fly, _land, _lever);
            cluster.pickingMode = PickingMode.Ignore;

            // Plates under the minimap: speed (arc gauge, Lilita numerals, vehicle name) and ALT while flying.
            _gauge = new SpeedGauge();
            _speed = El.Text("0", "veh-speed-value");
            _name = El.Text("", "veh-speed-name");
            var speedPlate = El.Div("veh-plate veh-speed-plate", _gauge, _speed, El.Div("veh-speed-meta", _name, El.Text("km/h", "veh-speed-unit")));
            _alt = El.Text("", "veh-alt-value");
            _altNote = El.Text("", "veh-alt-note");
            _altPlate = El.Div("veh-plate veh-alt-plate", El.Div("veh-alt-row", El.Text(Strings.T("vehicle.alt"), "veh-alt-label"), _alt), _altNote);
            _altPlate.style.display = DisplayStyle.None;
            _altPlate.AddToClassList("hud-off");
            var plates = El.Div("veh-plates", speedPlate, _altPlate);
            foreach (var c in plates.Query<VisualElement>().ToList()) c.pickingMode = PickingMode.Ignore;

            Element = El.Div("veh-controls", _left, _right, cluster, plates);
            Element.pickingMode = PickingMode.Ignore;
            Element.name = "vehicle-controls";
            Stick(_left, _leftRing, _leftThumb, true);
            Stick(_right, _rightRing, _rightThumb, false);
            Element.style.display = DisplayStyle.None;
            Element.AddToClassList("hud-off");
            _ui = UiRoot.Instance;
            _focused = Application.isFocused;
            if (Application.isPlaying) _ = Runner.Instance;
            Runner.ApplicationPaused += OnPaused;
            Application.focusChanged += OnFocusChanged;
            Element.RegisterCallback<DetachFromPanelEvent>(OnDetached);
            _ui?.AddUpdater(Update);
            _subs.On<VehicleChangedSignal>(s => Bind(s.vehicle));
            if (VehicleSystem.Current != null && VehicleSystem.Current.Driving != null) Bind(VehicleSystem.Current.Driving);
        }

        public bool Active => _vehicle != null;
        /// <summary>Editor captures drive the vehicle themselves (the HUD then only shows the controls).</summary>
        public static bool ScriptedInput;

        private static VisualElement Icon(string kind, string cls) { var i = new UiIcon(kind); i.AddToClassList(cls); return i; }
        private static VisualElement Tick(string kind, string cls) => Icon(kind, cls);
        private static Label Caption(string key, string extra = null)
        {
            var l = El.Text(Strings.T(key), extra == null ? "veh-caption" : "veh-caption " + extra);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        private void Bind(Vehicle v)
        {
            ReleaseAll();
            _vehicle = v;
            HudFade.Set(Element, v != null);
            if (v == null) return;
            bool air = VehicleCatalog.Flies(v.Kind);
            _simple = air && !FlightPhysics.ProControls;
            bool hover = v.Kind == VehicleKind.Roadstar;
            Element.EnableInClassList("veh-mode-ground", !air);
            Element.EnableInClassList("veh-mode-heli", air && _simple);
            Element.EnableInClassList("veh-mode-pro", air && !_simple);
            Element.EnableInClassList("veh-mode-hover", hover);
            _right.Show(air);
            _rightRing.Show(!_simple);
            _gas.Show(!air); _brake.Show(!air); _horn.Show(!air);
            _drift.Show(!air && !hover); _fly.Show(hover); _exit.Show(!air);
            _lever.Show(_simple); _land.Show(air);
            _leverValue = 0f; _canExitShown = false;
            _land.EnableInClassList("veh-on", false);
            _landCaption.text = Strings.T("vehicle.land");
            _name.text = (hover ? Strings.T("hud.drone") : v.Spec != null ? Strings.T(v.Spec.nameKey) : "").ToUpperInvariant();
            if (_simple && PlayerPrefs.GetInt("ctlife.flight.coached2", 0) == 0)
            {
                PlayerPrefs.SetInt("ctlife.flight.coached2", 1); PlayerPrefs.Save();
                Bus.Publish(new InteractionToastSignal { key = "vehicle.liftCoach" });
            }
            LayoutLever();
        }

        private VisualElement Pedal(string cls, string icon, Action<bool> set)
        {
            var e = El.Div("veh-pedal " + cls, Icon(icon, "veh-pedal-glyph"));
            e.name = cls;
            Holdable(e, set, null);
            return e;
        }

        private VisualElement Round(string cls, string icon, string captionKey, Action<bool> set, Action tap, out Label caption)
        {
            var e = El.Div("veh-round " + cls);
            if (icon != null) e.Add(Icon(icon, "veh-round-glyph"));
            caption = Caption(captionKey);
            e.Add(caption);
            e.name = cls;
            Holdable(e, set, tap);
            return e;
        }

        /// <summary>Press: captured pointer, pressed art (scale 0.94, brighter). A tap fires on release inside the button.</summary>
        private void Holdable(VisualElement e, Action<bool> set, Action tap)
        {
            var hold = new Hold { element = e, set = set ?? (_ => { }), tap = tap }; _holds.Add(hold);
            e.RegisterCallback<PointerDownEvent>(ev =>
            {
                if (ev.button != 0 || hold.pointer >= 0 || IsBlocked() || !Visible(e) || e.ClassListContains("veh-gone")) return;
                hold.pointer = ev.pointerId;
                e.CapturePointer(ev.pointerId); e.AddToClassList("veh-down"); hold.set(true); ev.StopPropagation();
            });
            e.RegisterCallback<PointerUpEvent>(ev =>
            {
                if (ev.pointerId != hold.pointer) return;
                bool inside = e.worldBound.Contains(ev.position);
                Release(hold);
                if (inside && !IsBlocked()) hold.tap?.Invoke();
            });
            e.RegisterCallback<PointerCancelEvent>(ev => { if (ev.pointerId == hold.pointer) Release(hold); });
            e.RegisterCallback<PointerCaptureOutEvent>(ev => { if (ev.pointerId == hold.pointer) Release(hold); });
        }

        private void Lever(VisualElement track)
        {
            track.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || _leverPointer >= 0 || IsBlocked() || !Visible(track)) return;
                _leverPointer = e.pointerId;
                // Relative: wherever the thumb lands is the current value, so touching never jumps the throttle.
                _leverY0 = e.position.y + _leverValue * LeverTravel();
                track.CapturePointer(e.pointerId);
                track.AddToClassList("veh-down");
                e.StopPropagation();
            });
            track.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != _leverPointer) return;
                if (IsBlocked()) { ReleaseAll(); return; }
                _leverValue = Mathf.Clamp(-(e.position.y - _leverY0) / LeverTravel(), -1f, 1f);
                LayoutLever();
                e.StopPropagation();
            });
            void Up(int id)
            {
                if (id != _leverPointer) return;
                _leverPointer = -1;
                if (track.HasPointerCapture(id)) track.ReleasePointer(id);
                track.RemoveFromClassList("veh-down");
            }
            track.RegisterCallback<PointerUpEvent>(e => Up(e.pointerId));
            track.RegisterCallback<PointerCancelEvent>(e => Up(e.pointerId));
            track.RegisterCallback<PointerCaptureOutEvent>(e => Up(e.pointerId));
        }

        private float LeverTravel()
        {
            float h = _lever.layout.height, k = _leverKnob.layout.height;
            if (float.IsNaN(h) || h <= 0f) h = 464f;
            if (float.IsNaN(k) || k <= 0f) k = 140f;
            return Mathf.Max(40f, h * 0.5f - k * 0.5f - 6f);
        }

        private void LayoutLever()
        {
            float travel = LeverTravel(), d = _leverValue * travel;
            _leverKnob.style.translate = new Translate(0, -d);
            float h = _lever.layout.height; if (float.IsNaN(h) || h <= 0f) h = 464f;
            _leverFill.style.top = h * 0.5f - Mathf.Max(0f, d);
            _leverFill.style.height = Mathf.Abs(d);
            _leverFill.EnableInClassList("veh-lever-fill--down", d < 0f);
        }

        private void Stick(VisualElement zone, VisualElement ring, VisualElement thumb, bool left)
        {
            zone.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || (left ? _leftPointer : _rightPointer) >= 0 || IsBlocked() || !Visible(zone)) return;
                zone.CapturePointer(e.pointerId);
                if (!left && _simple)
                { _rightPointer = e.pointerId; _orbitPoint = e.position; e.StopPropagation(); return; }
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
            zone.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != (left ? _leftPointer : _rightPointer)) return;
                if (IsBlocked()) { ReleaseAll(); return; }
                if (!left && _simple)
                {
                    Vector2 p = e.position;
                    float panelWidth = zone.panel != null ? zone.panel.visualTree.layout.width : Screen.width;
                    CameraRig.Instance?.Orbit(WorldInputLayer.OrbitPixels(p - _orbitPoint, panelWidth, Screen.width));
                    _orbitPoint = p; e.StopPropagation();
                }
                else Move(zone, ring, thumb, left, e.position);
            });
            void Up(int id)
            {
                if (id != (left ? _leftPointer : _rightPointer)) return;
                if (left) { _leftPointer = -1; _leftInput = Vector2.zero; } else { _rightPointer = -1; _rightInput = Vector2.zero; }
                if (zone.HasPointerCapture(id)) zone.ReleasePointer(id);
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

        /// <summary>The player's own horn (traffic already honks through the same VehicleAudio); local only for now.</summary>
        private void Honk()
        {
            if (_vehicle == null || Time.unscaledTime - _hornAt < 0.3f) return;
            _hornAt = Time.unscaledTime;
            _vehicle.GetComponent<VehicleAudio>()?.Horn();
        }

        private bool ExitAllowed() => _vehicle != null && !_exit.ClassListContains("veh-gone");

        private void ReleaseAll()
        {
            // Clear ownership before releasing capture: CaptureOut may be delivered synchronously.
            int left = _leftPointer, right = _rightPointer, lever = _leverPointer;
            _leftPointer = _rightPointer = _leverPointer = -1;
            _leftInput = _rightInput = Vector2.zero;
            _orbitPoint = Vector2.zero;
            _gasDown = _brakeDown = _handDown = false;
            foreach (var hold in _holds) Release(hold);
            ResetStick(_left, _leftRing, _leftThumb, left);
            ResetStick(_right, _rightRing, _rightThumb, right);
            if (lever >= 0 && _lever.HasPointerCapture(lever)) _lever.ReleasePointer(lever);
            _lever.RemoveFromClassList("veh-down");
            // Busy exit/landing and explicit capture demos own their commands, even while HUD binding changes.
            var sys = VehicleSystem.Current;
            if (_wroteInput && _vehicle != null && !ScriptedInput && !VehicleSystem.ScriptedInput && (sys == null || !sys.Busy))
                _vehicle.Input = VehicleInput.None;
            _wroteInput = false;
        }

        private static void Release(Hold hold)
        {
            int pointer = hold.pointer; hold.pointer = -1;
            if (pointer >= 0 || hold.element.ClassListContains("veh-down")) hold.set(false);
            hold.element.RemoveFromClassList("veh-down");
            if (pointer >= 0 && hold.element.HasPointerCapture(pointer)) hold.element.ReleasePointer(pointer);
        }

        private static void ResetStick(VisualElement zone, VisualElement ring, VisualElement thumb, int pointer)
        {
            if (pointer >= 0 && zone.HasPointerCapture(pointer)) zone.ReleasePointer(pointer);
            thumb.style.translate = new Translate(0, 0);
            ring.style.left = StyleKeyword.Null; ring.style.top = StyleKeyword.Null;
            zone.RemoveFromClassList("veh-active");
        }

        private void OnPaused(bool paused) { _paused = paused; if (paused) ReleaseAll(); }
        private void OnFocusChanged(bool focused) { _focused = focused; if (!focused) ReleaseAll(); }
        private void OnDetached(DetachFromPanelEvent e) { if (e.target == Element) ReleaseAll(); }

        private bool IsBlocked()
        {
            if (_disposed || _vehicle == null || _paused || !_focused || Element.panel == null || !Visible(Element)) return true;
            if (_ui == null) return false;
            if (HasOverlay(_ui.ScreenLayer) || HasOverlay(_ui.PhoneLayer) || HasOverlay(_ui.ModalLayer)) return true;
            for (var focus = _ui.Root.focusController?.focusedElement as VisualElement; focus != null; focus = focus.parent)
                if (focus is TextField) return true;
            return false;
        }

        private static bool Visible(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }

        // Match WorldInputLayer: a click-through social container is not a blocking modal.
        private static bool HasOverlay(VisualElement layer)
        {
            if (layer == null || !Visible(layer)) return false;
            foreach (var child in layer.Children())
                if (child.pickingMode == PickingMode.Position && Visible(child)) return true;
            return false;
        }

        /// <summary>The sticks' dead zone: nothing below 0.12, then rescaled so full deflection is still 1.</summary>
        public static float Dead(float v) => Mathf.Abs(v) < 0.12f ? 0f : Mathf.Sign(v) * (Mathf.Abs(v) - 0.12f) / 0.88f;

        /// <summary>The input this frame (public for the editor capture harness).</summary>
        public VehicleInput Compose()
        {
            if (_vehicle == null) return VehicleInput.None;
            bool air = VehicleCatalog.Flies(_vehicle.Kind);
            if (air && _simple)
            {
                float heading = CameraRig.Instance != null ? CameraRig.Instance.yaw : _vehicle.transform.eulerAngles.y;
                var move = Quaternion.Euler(0f, heading, 0f) * new Vector3(Dead(_leftInput.x), 0f, Dead(_leftInput.y));
                return new VehicleInput { simpleFlight = true, move = Vector3.ClampMagnitude(move, 1f), heading = heading,
                    lift = Mathf.Abs(_leverValue) < 0.06f ? 0f : _leverValue };
            }
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
            if (VehicleCatalog.Flies(_vehicle.Kind) && _simple == FlightPhysics.ProControls) Bind(_vehicle);
            bool blocked = IsBlocked();
            if (blocked) ReleaseAll();
            // The lever springs back to the centre detent (hold altitude) when let go.
            if (_leverPointer < 0 && _leverValue != 0f)
            {
                _leverValue = Mathf.MoveTowards(_leverValue, 0f, Time.unscaledDeltaTime * 7f);
                LayoutLever();
            }
            _wroteInput = !blocked && !sys.Busy && !ScriptedInput && !VehicleSystem.ScriptedInput;
            if (_wroteInput) _vehicle.Input = Compose();
            Element.EnableInClassList("veh-busy", sys.Busy);

            var f = _vehicle.GetComponent<FlightPhysics>();
            bool air = VehicleCatalog.Flies(_vehicle.Kind);
            bool flying = _vehicle.Flying || (air && f != null && !f.Grounded);
            float kmh = _vehicle.Speed * 3.6f;
            var spec = _vehicle.Spec;
            float top = spec == null ? 120f : (air || _vehicle.Flying ? spec.flightTopSpeed : spec.topSpeed) * 3.6f;
            _speed.text = Mathf.RoundToInt(kmh).ToString();
            _gauge.Set(Mathf.Clamp01(kmh / Mathf.Max(1f, top)), flying);
            Element.EnableInClassList("veh-flying", flying);
            Element.EnableInClassList("veh-reversing", _vehicle.ForwardSpeed < -0.5f && !air);

            bool showAlt = f != null && (flying || air);
            if (showAlt)
            {
                _alt.text = $"{Mathf.Max(0f, f.Altitude):0} m";
                string note = f.WaitingForGround ? Strings.T("vehicle.loadingArea") : f.LandingBlockedByWater ? Strings.T("vehicle.noWaterLanding") :
                    f.LandingBlockedBySlope ? Strings.T("vehicle.tooSteep") : "";
                _altNote.text = note;
                _altNote.Show(note.Length > 0);
            }
            if (showAlt != (_altPlate.style.display != DisplayStyle.None && !_altPlate.ClassListContains("hud-off"))) HudFade.Set(_altPlate, showAlt);

            if (_simple && f != null) _rotor.Set(f.Rotor01 < 0.9f ? f.Rotor01 / 0.9f : -1f);
            if (air && f != null)
            {
                bool canExit = _vehicle.Kind != VehicleKind.Drone && f.CanExit;
                if (canExit != _canExitShown)
                {
                    _canExitShown = canExit;
                    _land.EnableInClassList("veh-on", canExit);   // Land -> Get out (cross-fade, a small pop)
                    _landCaption.text = Strings.T(canExit ? "vehicle.exit" : "vehicle.land");
                    Pop(_land);
                }
            }
            if (_vehicle.Kind == VehicleKind.Roadstar && f != null)
            {
                bool wants = f.WantsFlight;
                if (wants != _fly.ClassListContains("veh-on"))
                {
                    _fly.EnableInClassList("veh-on", wants);
                    _flyCaption.text = Strings.T(wants ? "vehicle.land" : "vehicle.fly");
                }
                // Get out hides while the hovercar is in the air (it lands first).
                bool gone = _vehicle.Flying || wants;
                if (gone != _exit.ClassListContains("veh-gone"))
                {
                    _exit.EnableInClassList("veh-gone", gone);
                    _exit.pickingMode = gone ? PickingMode.Ignore : PickingMode.Position;
                }
                _fly.EnableInClassList("veh-alert", f.LandingBlockedByWater || f.LandingBlockedBySlope);
            }
        }

        private static void Pop(VisualElement e)
        {
            e.AddToClassList("veh-pop");
            e.schedule.Execute(() => e.RemoveFromClassList("veh-pop")).StartingIn(30);
        }

        public void Dispose()
        {
            if (_disposed) return;
            ReleaseAll();
            _disposed = true;
            Runner.ApplicationPaused -= OnPaused;
            Application.focusChanged -= OnFocusChanged;
            Element.UnregisterCallback<DetachFromPanelEvent>(OnDetached);
            _subs.Dispose();
            _ui?.RemoveUpdater(Update);
        }

        /// <summary>The speed plate's arc: a 270 degree track and a fill (mint on the road, cyan in the air).</summary>
        private sealed class SpeedGauge : VisualElement
        {
            private float _value = -1f;
            private bool _air;
            public SpeedGauge()
            {
                AddToClassList("veh-gauge");
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }
            public void Set(float v, bool air)
            {
                v = Mathf.Round(v * 100f) / 100f;
                if (Mathf.Approximately(v, _value) && air == _air) return;
                _value = v; _air = air;
                MarkDirtyRepaint();
            }
            private void Draw(MeshGenerationContext ctx)
            {
                var r = contentRect; if (r.width <= 0f) return;
                var p = ctx.painter2D;
                var c = r.center; float rad = Mathf.Min(r.width, r.height) * 0.5f - 6f;
                p.lineWidth = 8f; p.lineCap = LineCap.Round;
                p.strokeColor = new Color(1f, 1f, 1f, 0.16f);
                p.BeginPath(); p.Arc(c, rad, 135f, 405f); p.Stroke();
                if (_value <= 0.005f) return;
                p.strokeColor = _air ? new Color(0.42f, 0.89f, 1f) : new Color(0.37f, 0.97f, 0.65f);
                p.BeginPath(); p.Arc(c, rad, 135f, 135f + 270f * Mathf.Clamp01(_value)); p.Stroke();
            }
        }

        /// <summary>The rotor spin-up as a mint progress ring around the lever knob (hidden at full speed).</summary>
        private sealed class RotorRing : VisualElement
        {
            private float _value = -1f;
            public RotorRing()
            {
                AddToClassList("veh-rotor");
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }
            public void Set(float v)
            {
                v = v < 0f ? -1f : Mathf.Round(v * 50f) / 50f;
                if (Mathf.Approximately(v, _value)) return;
                _value = v;
                MarkDirtyRepaint();
            }
            private void Draw(MeshGenerationContext ctx)
            {
                if (_value < 0f) return;
                var r = contentRect; if (r.width <= 0f) return;
                var p = ctx.painter2D;
                var c = r.center; float rad = Mathf.Min(r.width, r.height) * 0.5f - 5f;
                p.lineWidth = 7f; p.lineCap = LineCap.Round;
                p.strokeColor = new Color(0.16f, 0.04f, 0.29f, 0.25f);
                p.BeginPath(); p.Arc(c, rad, 0f, 360f); p.Stroke();
                p.strokeColor = new Color(0.12f, 0.81f, 0.45f);
                p.BeginPath(); p.Arc(c, rad, -90f, -90f + 360f * Mathf.Clamp01(_value)); p.Stroke();
            }
        }
    }
}
