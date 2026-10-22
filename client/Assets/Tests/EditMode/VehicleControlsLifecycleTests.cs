using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CTLife.Core;
using CTLife.UI;
using CTLife.World.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace CTLife.Tests
{
    /// <summary>Actual pointer delivery and capture on an attached panel, with the shipping control composer.</summary>
    public sealed class VehicleControlsLifecycleTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<GameObject> _objects = new List<GameObject>();
        private ControlsWindow _window;
        private UiRoot _ui, _previousUi;
        private Runner _runner, _previousRunner;
        private VehicleSystem _system, _previousSystem;
        private VehicleControls _controls;
        private Vehicle _car;
        private VisualElement _hud;
        private bool _oldScripted, _oldSystemScripted, _hadCoached, _hadPro;
        private int _coached, _pro;
        private VisualElement Gas => _controls.Element.Q("veh-gas");
        private VisualElement Brake => _controls.Element.Q("veh-brake");
        private VisualElement Left => _controls.Element.Q(className: "veh-stick-zone--left");

        private GameObject Own(string name)
        {
            var go = new GameObject(name); go.SetActive(false); _objects.Add(go); return go;
        }

        private static void Property(Type type, string name, object owner, object value) =>
            type.GetProperty(name, Hidden | BindingFlags.Static).GetSetMethod(true).Invoke(owner, new[] { value });

        [SetUp]
        public void SetUp()
        {
            _previousUi = UiRoot.Instance; _previousSystem = VehicleSystem.Current;
            _previousRunner = (Runner)typeof(Runner).GetField("_instance", Static).GetValue(null);
            _oldScripted = VehicleControls.ScriptedInput; _oldSystemScripted = VehicleSystem.ScriptedInput;
            VehicleControls.ScriptedInput = VehicleSystem.ScriptedInput = false;
            _hadCoached = PlayerPrefs.HasKey("ctlife.flight.coached"); _coached = PlayerPrefs.GetInt("ctlife.flight.coached");
            _hadPro = PlayerPrefs.HasKey("ctlife.flight.pro"); _pro = PlayerPrefs.GetInt("ctlife.flight.pro");
            PlayerPrefs.SetInt("ctlife.flight.coached", 1); PlayerPrefs.SetInt("ctlife.flight.pro", 0);
            _window = ScriptableObject.CreateInstance<ControlsWindow>();
            _window.position = new Rect(20, 20, 1000, 600); _window.Show();
            var root = _window.rootVisualElement;
            root.styleSheets.Add(Resources.Load<StyleSheet>("UI/city-hud"));
            _ui = Own("vehicle-controls-ui-fixture").AddComponent<UiRoot>();
            Property(typeof(UiRoot), "Instance", null, _ui);
            Property(typeof(UiRoot), "Root", _ui, root);
            foreach (string name in new[] { "ScreenLayer", "PhoneLayer", "ModalLayer" })
            {
                var layer = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
                root.Add(layer); Property(typeof(UiRoot), name, _ui, layer);
            }
            _runner = Own("vehicle-controls-runner-fixture").AddComponent<Runner>();
            typeof(Runner).GetField("_instance", Static).SetValue(null, _runner);
            _system = Own("vehicle-controls-system-fixture").AddComponent<VehicleSystem>();
            Property(typeof(VehicleSystem), "Current", null, _system);
            _car = MakeVehicle("compact");
            Property(typeof(VehicleSystem), "Driving", _system, _car);
            _controls = new VehicleControls();
            _hud = new VisualElement(); _hud.AddToClassList("ct-hud");
            _hud.style.position = Position.Absolute; _hud.style.width = 1000; _hud.style.height = 600;
            root.Insert(0, _hud); _hud.Add(_controls.Element);
            Focus(true);
        }

        private Vehicle MakeVehicle(string id)
        {
            var go = Own("vehicle-controls-" + id); go.AddComponent<Rigidbody>().useGravity = false;
            var vehicle = go.AddComponent<Vehicle>(); vehicle.specId = id; vehicle.Bind(); return vehicle;
        }

        [TearDown]
        public void TearDown()
        {
            _controls?.Dispose();
            // These publishers are process-wide; other fixtures must not inherit our simulated interruption.
            Focus(Application.isFocused); Pause(false);
            if (_window != null) _window.Close();
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
            Property(typeof(UiRoot), "Instance", null, _previousUi != null ? _previousUi : null);
            Property(typeof(VehicleSystem), "Current", null, _previousSystem != null ? _previousSystem : null);
            typeof(Runner).GetField("_instance", Static).SetValue(null, _previousRunner != null ? _previousRunner : null);
            VehicleControls.ScriptedInput = _oldScripted; VehicleSystem.ScriptedInput = _oldSystemScripted;
            if (_hadCoached) PlayerPrefs.SetInt("ctlife.flight.coached", _coached); else PlayerPrefs.DeleteKey("ctlife.flight.coached");
            if (_hadPro) PlayerPrefs.SetInt("ctlife.flight.pro", _pro); else PlayerPrefs.DeleteKey("ctlife.flight.pro");
        }

        private void Tick() => typeof(VehicleControls).GetMethod("Update", Hidden).Invoke(_controls, null);
        private static void Focus(bool focused)
        {
            var field = typeof(Application).GetField("focusChanged", Static);
            Assert.IsNotNull(field, "fixture must raise Unity's existing focus event");
            (field.GetValue(null) as Action<bool>)?.Invoke(focused);
        }
        private void Pause(bool paused) => typeof(Runner).GetMethod("OnApplicationPause", Hidden).Invoke(_runner, new object[] { paused });

        private static void Pointer(VisualElement target, EventType type, int id, Vector2 point)
        {
            var source = new Event { type = type, button = 0, mousePosition = point };
            if (type == EventType.MouseDown)
            {
                using var e = PointerDownEvent.GetPooled(source);
                typeof(PointerDownEvent).GetProperty("pointerId").SetValue(e, id); e.target = target; target.SendEvent(e);
            }
            else if (type == EventType.MouseUp)
            {
                using var e = PointerUpEvent.GetPooled(source);
                typeof(PointerUpEvent).GetProperty("pointerId").SetValue(e, id); e.target = target; target.SendEvent(e);
            }
            else
            {
                using var e = PointerMoveEvent.GetPooled(source);
                typeof(PointerMoveEvent).GetProperty("pointerId").SetValue(e, id); e.target = target; target.SendEvent(e);
            }
        }
        private static void Down(VisualElement target, int id) => Pointer(target, EventType.MouseDown, id, target.worldBound.center);
        private static void Up(VisualElement target, int id) => Pointer(target, EventType.MouseUp, id, target.worldBound.center);
        private void Steer(int id)
        {
            Down(Left, id);
            Pointer(Left, EventType.MouseMove, id, Left.worldBound.center + Vector2.right * 65f);
        }
        private void Neutral()
        {
            var input = _controls.Compose();
            Assert.AreEqual(0f, input.throttle); Assert.AreEqual(0f, input.brake); Assert.AreEqual(0f, input.steer);
            Assert.AreEqual(0f, input.lift); Assert.AreEqual(Vector3.zero, input.move); Assert.IsFalse(input.handbrake);
            Assert.IsFalse(Gas.ClassListContains("veh-down")); Assert.IsFalse(Brake.ClassListContains("veh-down"));
            Assert.IsFalse(Left.ClassListContains("veh-active"));
            Assert.IsFalse(Gas.HasPointerCapture(12)); Assert.IsFalse(Brake.HasPointerCapture(12));
            Assert.IsFalse(Left.HasPointerCapture(13));
        }

        [UnityTest]
        public IEnumerator VehicleRebindClearsPressedStyleCaptureAndOldOutput()
        {
            yield return null; Down(Brake, 12); Tick();
            Assert.AreEqual(1f, _car.Input.brake); Assert.IsTrue(Brake.HasPointerCapture(12));
            var heli = MakeVehicle("heli"); Property(typeof(VehicleSystem), "Driving", _system, heli);
            Bus.Publish(new VehicleChangedSignal { vehicle = heli });
            Neutral(); Assert.AreEqual(0f, _car.Input.brake, "old vehicle must not retain HUD-owned braking");
            Assert.AreEqual(0f, _controls.Compose().lift, "stale car brake must not descend the helicopter");
        }

        [UnityTest]
        public IEnumerator FocusLossClearsBothHoldsAndDoesNotResumeThem()
        {
            yield return null; Down(Gas, 12); Steer(13); Tick();
            Assert.AreEqual(1f, _car.Input.throttle); Assert.Greater(_car.Input.steer, .1f);
            Focus(false); Neutral(); Assert.AreEqual(0f, _car.Input.throttle);
            Focus(true); Tick(); Neutral();
        }

        [UnityTest]
        public IEnumerator ApplicationPauseClearsBothHoldsAndDoesNotResumeThem()
        {
            yield return null; Down(Gas, 12); Steer(13); Tick();
            Pause(true); Neutral(); Assert.AreEqual(0f, _car.Input.throttle);
            Pause(false); Tick(); Neutral();
        }

        [UnityTest]
        public IEnumerator ModalCancelsHeldInputRejectsNewDownAndRequiresFreshGestureAfterClosing()
        {
            yield return null; Down(Gas, 12); Steer(13); Tick();
            var modal = new VisualElement { pickingMode = PickingMode.Position }; _ui.ModalLayer.Add(modal);
            yield return null; Tick(); Neutral(); Assert.AreEqual(0f, _car.Input.throttle);
            Down(Gas, 12); Tick(); Neutral();
            modal.RemoveFromHierarchy(); yield return null; Tick(); Neutral();
            Down(Gas, 12); Tick(); Assert.AreEqual(1f, _car.Input.throttle, "fresh input works after the modal closes");
        }

        [UnityTest]
        public IEnumerator HiddenHudClearsHeldInputAndDoesNotResumeItOnShow()
        {
            yield return null; Down(Gas, 12); Tick();
            _hud.style.display = DisplayStyle.None; yield return null; Tick(); Neutral();
            Assert.AreEqual(0f, _car.Input.throttle);
            _hud.style.display = DisplayStyle.Flex; yield return null; Tick(); Neutral();
        }

        [UnityTest]
        public IEnumerator DetachClearsOutputBeforeTheControlCanBeReattached()
        {
            yield return null; Down(Gas, 12); Steer(13); Tick();
            _controls.Element.RemoveFromHierarchy(); Neutral(); Assert.AreEqual(0f, _car.Input.throttle);
            _hud.Add(_controls.Element); yield return null; Tick(); Neutral();
        }

        [UnityTest]
        public IEnumerator DisposeReleasesCaptureAndTheLastHudOutput()
        {
            yield return null; Down(Gas, 12); Steer(13); Tick();
            _controls.Dispose(); Neutral(); Assert.AreEqual(0f, _car.Input.throttle);
            Focus(false); Pause(true); // Unsubscribed controls must remain safe to dispose again in TearDown.
        }

        [UnityTest]
        public IEnumerator OldPointerReleaseCannotCancelAnotherPointersPedalHold()
        {
            yield return null; Down(Gas, 12); Up(Gas, 12); Down(Gas, 13); Tick();
            Assert.AreEqual(1f, _car.Input.throttle);
            Up(Gas, 12); Tick();
            Assert.AreEqual(1f, _car.Input.throttle, "late release from pointer12 does not own pointer13's hold");
            Assert.IsTrue(Gas.HasPointerCapture(13)); Up(Gas, 13); Tick(); Assert.AreEqual(0f, _car.Input.throttle);
        }

        [UnityTest]
        public IEnumerator StationaryGasAndSteeringHoldRemainIndependentUntilTheirOwnRelease()
        {
            yield return null; Down(Gas, 12); Steer(13); Tick();
            for (int i = 0; i < 5; i++) { yield return null; Tick(); }
            Assert.AreEqual(1f, _car.Input.throttle); Assert.Greater(_car.Input.steer, .1f);
            Up(Gas, 12); Tick(); Assert.AreEqual(0f, _car.Input.throttle); Assert.Greater(_car.Input.steer, .1f);
            Up(Left, 13); Tick(); Neutral();
        }

        [UnityTest]
        public IEnumerator RoadstarHidingHandbrakeReleasesOnlyThatGesture()
        {
            yield return null;
            var roadstar = MakeVehicle("roadstar"); Property(typeof(VehicleSystem), "Driving", _system, roadstar);
            Bus.Publish(new VehicleChangedSignal { vehicle = roadstar }); yield return null;
            var hand = _controls.Element.Q("veh-handbrake");
            Down(Gas, 12); Steer(13); Down(hand, 14); Tick();
            Assert.IsTrue(roadstar.Input.handbrake); Assert.IsTrue(hand.HasPointerCapture(14));
            roadstar.Flying = true; Tick();
            Assert.IsFalse(_controls.Compose().handbrake, "a hidden handbrake cannot remain latched in flight");
            Assert.IsFalse(hand.HasPointerCapture(14)); Assert.IsFalse(hand.ClassListContains("veh-down"));
            Assert.AreEqual(1f, roadstar.Input.throttle); Assert.Greater(roadstar.Input.steer, .1f);
            roadstar.Flying = false; Tick(); Assert.IsFalse(roadstar.Input.handbrake, "landing must not revive the old hold");
        }

        [UnityTest]
        public IEnumerator LifecycleCleanupPreservesBusyAndScriptedVehicleCommands()
        {
            yield return null; Down(Gas, 12); Tick();
            Property(typeof(VehicleSystem), "Busy", _system, true);
            _car.Input = new VehicleInput { brake = 1f, handbrake = true };
            Pause(true); Neutral();
            Assert.AreEqual(1f, _car.Input.brake); Assert.IsTrue(_car.Input.handbrake, "exit braking belongs to VehicleSystem");
            // ExitRoutine publishes null while Busy is still true and its parked brake is already applied.
            Property(typeof(VehicleSystem), "Driving", _system, null);
            Bus.Publish(new VehicleChangedSignal { vehicle = null });
            Assert.AreEqual(1f, _car.Input.brake); Assert.IsTrue(_car.Input.handbrake);
            Pause(false); Property(typeof(VehicleSystem), "Busy", _system, false);
            Property(typeof(VehicleSystem), "Driving", _system, _car);
            Bus.Publish(new VehicleChangedSignal { vehicle = _car });
            VehicleSystem.ScriptedInput = true; _car.Input = new VehicleInput { lift = -.7f };
            Focus(false); Tick(); Assert.AreEqual(-.7f, _car.Input.lift, "scripted flight remains the current input owner");
            _car.gameObject.SetActive(true); _car.Body.linearVelocity = Vector3.right * 5f; Tick();
            Assert.AreEqual("18", _controls.Element.Q<Label>(className: "veh-speed-value").text,
                "scripted capture still updates telemetry while human input is blocked");
            Assert.AreEqual(-.7f, _car.Input.lift);
        }

        [UnityTest]
        public IEnumerator ClickThroughSocialContainerDoesNotCancelStationaryControls()
        {
            yield return null; Down(Gas, 12); Steer(13); Tick();
            var social = new VisualElement { pickingMode = PickingMode.Ignore };
            social.Add(new Button()); _ui.ModalLayer.Add(social);
            yield return null; Tick();
            Assert.AreEqual(1f, _car.Input.throttle); Assert.Greater(_car.Input.steer, .1f);
            Assert.IsTrue(Gas.HasPointerCapture(12)); Assert.IsTrue(Left.HasPointerCapture(13));
        }

        [UnityTest]
        public IEnumerator FocusedTextEntryCancelsHeldDrivingUntilANewGesture()
        {
            yield return null; Down(Gas, 12); Tick();
            var text = new TextField(); _hud.Add(text); yield return null;
            text.Focus(); yield return null;
            Assert.IsTrue(text.Contains(text.focusController.focusedElement as VisualElement) || text.focusController.focusedElement == text,
                "fixture must focus the real text input");
            Tick(); Neutral(); Assert.AreEqual(0f, _car.Input.throttle);
            text.Blur(); text.RemoveFromHierarchy(); yield return null; Tick(); Neutral();
        }

        private sealed class ControlsWindow : EditorWindow { }
    }
}
