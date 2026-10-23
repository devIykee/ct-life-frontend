using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Colyseus;
using Colyseus.Schema;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.World;
using CTLife.World.Vehicles;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.Tests
{
    /// <summary>Drives the production entry iterator and loss handler around actual parenting/body/collider transitions.</summary>
    public sealed class VehicleEntryAuthorityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> _objects = new List<GameObject>();
        private VehicleSystem _system, _previousSystem;
        private CameraRig _previousCamera;
        private VenueSession _visit, _previousVisit;
        private VehicleNetworkBridge _bridge, _previousBridge;
        private VehicleChannel _channel;
        private VehicleState _state;
        private Room<DynamicSchema> _room;
        private LocalPlayerController _player;
        private AvatarView _view;
        private Vehicle _car;
        private Collider _tapCollider;
        private Transform _parent;
        private IEnumerator _entry;
        private Task<bool> _permission;
        private CancellationTokenSource _visitOwner;
        private Func<Vehicle, CancellationToken, Task<bool>> _previousGate;
        private Action<Vehicle> _aborted, _drivingChanged;
        private int _abortCount;

        private GameObject Own(string name) { var go = new GameObject(name); _objects.Add(go); return go; }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
        private static void Current(System.Type type, object value) => type.GetProperty("Current").GetSetMethod(true).Invoke(null, new[] { value });
        private static void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);

        [SetUp]
        public void Setup()
        {
            // Other EditMode fixtures may explicitly Awake a runtime-only rig without receiving its OnDestroy.
            // This fixture owns no camera: isolate the singleton, preserving only a still-live external owner.
            _previousCamera = CameraRig.Instance != null ? CameraRig.Instance : null;
            typeof(CameraRig).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { null });
            _previousSystem = VehicleSystem.Current; _previousVisit = VenueSession.Current; _previousGate = VehicleSystem.AuthorizeEnter; _previousBridge = VehicleNetworkBridge.Current;
            _abortCount = 0;
            _parent = Own("entry-test-avatar-parent").transform;
            var avatar = Own("entry-test-player"); avatar.transform.SetParent(_parent, false);
            _view = avatar.AddComponent<AvatarView>();
            _player = avatar.AddComponent<LocalPlayerController>();
            _player.Init(null, null); _player.OpenWorld = true; _player.enabled = false;
            if (!_player.Body.Ready) Call(_player.Body, "Awake");
            Assert.IsTrue(_player.Body.Ready, "fixture requires the real CharacterController");
            _tapCollider = avatar.AddComponent<BoxCollider>(); _tapCollider.isTrigger = true;
            _system = Own("entry-test-system").AddComponent<VehicleSystem>(); _system.enabled = false;
            Set(_system, "_player", _player); Set(_system, "_view", _view); Current(typeof(VehicleSystem), _system);
            _visit = Own("entry-test-visit").AddComponent<VenueSession>(); _visit.enabled = false;
            _visitOwner = new CancellationTokenSource(); Set(_visit, "_visit", _visitOwner); Set(_visit, "_visitToken", _visitOwner.Token);
            Set(_visit, "_ready", true); Set(_visit, "_online", true); Current(typeof(VenueSession), _visit);
            _bridge = Own("entry-test-bridge").AddComponent<VehicleNetworkBridge>(); _bridge.enabled = false;
            Set(_bridge, "_visit", _visit); Set(_bridge, "_user", "entry-test-user"); Set(_bridge, "_joining", true);
            Current(typeof(VehicleNetworkBridge), _bridge);
            _channel = new VehicleChannel("entry-test-user");
            _room = new Room<DynamicSchema>("vehicles");
            var connection = new Connection("ws://127.0.0.1:9", null) { IsOpen = true, Transmit = _ => Task.CompletedTask };
            _room.SetConnection(connection); // No socket is opened; the real channel can serialize releases locally.
            typeof(RoomChannel<DynamicSchema>).GetProperty("Room", Private).SetValue(_channel, _room);
            var mirrorType = typeof(VehicleChannel).GetField("_vehicles", Private).FieldType;
            var mirror = Activator.CreateInstance(mirrorType, true);
            _state = new VehicleState { id = "entry-test-car", kind = "compact", driver = "entry-test-user" };
            ((IDictionary)Get(mirror, "_items"))[_state.id] = _state;
            Set(_channel, "_vehicles", mirror); Set(_bridge, "_channel", _channel);
            _aborted = v => { _abortCount++; typeof(VehicleNetworkBridge).GetMethod("EntryAborted", Private).Invoke(_bridge, new object[] { v }); };
            VehicleSystem.EntryAborted += _aborted;
            _drivingChanged = v => typeof(VehicleNetworkBridge).GetMethod("DrivingChanged", Private).Invoke(_bridge, new object[] { v });
            VehicleSystem.DrivingChanged += _drivingChanged;
            var car = Own("entry-test-car"); car.AddComponent<Rigidbody>().useGravity = false;
            foreach (string name in new[] { "Entry_Driver", "Seat_Driver", "Door_FL" })
            {
                var anchor = new GameObject(name); anchor.transform.SetParent(car.transform, false);
                anchor.transform.localPosition = name == "Entry_Driver" ? Vector3.left * 1.8f : name == "Seat_Driver" ? Vector3.up * 0.7f : Vector3.left;
            }
            _car = car.AddComponent<Vehicle>(); _car.specId = "compact"; _car.Bind(); _system.Vehicles.Add(_car);
            var identity = car.AddComponent<VehicleNetworkIdentity>(); identity.Id = _state.id; identity.Kind = _state.kind;
            _player.transform.position = _car.EntryPoint;
        }

        private void AcceptedGate()
        {
            // The actual bridge/channel acknowledgment reads the seeded authoritative map. Only socket transmission
            // is replaced; claim publication and simulation gating run exactly as they do for a received server state.
            VehicleSystem.AuthorizeEnter = (car, owner) => _permission = (Task<bool>)typeof(VehicleNetworkBridge)
                .GetMethod("Authorize", Private).Invoke(_bridge, new object[] { car, owner });
        }

        private void Begin() => _entry = (IEnumerator)typeof(VehicleSystem).GetMethod("EnterRoutine", Private).Invoke(_system, new object[] { _car });

        [TestCase("walk")]
        [TestCase("door")]
        [TestCase("seat")]
        [TestCase("close")]
        public void LostLeaseAfterAcceptanceAbortsTheActualEntryPhase(string phase)
        {
            AcceptedGate();
            if (phase == "walk") _player.transform.position += Vector3.back * 2f;
            var onFootPosition = _player.transform.position;
            Begin(); Assert.IsTrue(_entry.MoveNext());
            Assert.IsTrue(_system.Busy); Assert.IsNull(_system.Driving);
            if (phase == "walk") Assert.IsTrue(_player.HasDestination);
            else Assert.IsTrue(_player.InVehicle, "physical entry has started");
            if (phase == "seat" || phase == "close")
            {
                Assert.IsTrue(_entry.MoveNext(), "advance past the door wait into the actual seat interpolation");
                Assert.AreSame(_car.transform, _player.transform.parent); Assert.IsFalse(_tapCollider.enabled);
            }
            if (phase == "close") AdvanceToCloseWait();
            // Drop the real channel after acknowledgment. The loss handler must cancel the suspended iterator now.
            Set(_channel, "_dropped", true);
            Call(_bridge, "Update");
            Assert.IsTrue(_car.Body.isKinematic, "authority loss must freeze the car before Driving is assigned");
            for (int i = 0; i < 4 && _entry.MoveNext(); i++) { }
            AssertOnFoot(onFootPosition);
            Assert.IsFalse(_system.Busy); Assert.IsNull(_system.Driving);
            Assert.AreEqual(1, _abortCount); Assert.IsNull(Get(_bridge, "_claimId"));
        }

        private void AdvanceToCloseWait()
        {
            for (int i = 0; i < 10000; i++)
            {
                Assert.IsTrue(_entry.MoveNext(), "must reach the final door-close wait before publishing Driving");
                if (_entry.Current is WaitForSeconds) return;
            }
            Assert.Fail("seat interpolation never reached door-close wait");
        }

        private void AssertOnFoot(Vector3 onFootPosition)
        {
            Assert.IsNull(_car.Driver);
            Assert.IsFalse(_player.InVehicle); Assert.IsFalse(_player.Body.Suspended); Assert.IsTrue(_player.Body.Controller.enabled);
            Assert.AreSame(_parent, _player.transform.parent); Assert.IsTrue(_tapCollider.enabled);
            Assert.IsFalse(_player.HasDestination); Assert.IsFalse(_car.EngineOn);
            Assert.AreEqual(0f, (float)Get(_car, "_doorLeftTarget"));
            Assert.Less(Vector3.Distance(onFootPosition, _player.transform.position), .01f, "return to the entry's last on-foot pose, not the car's seat");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UninterruptedEntryKeepsTheNormalOfflineAndAcknowledgedPath(bool online)
        {
            Set(_visit, "_online", online);
            if (online) AcceptedGate(); else VehicleSystem.AuthorizeEnter = null;
            Begin();
            // Exercise the real iterator. WaitForSeconds yields are presentation delays; advancing them does not bypass any body transition.
            int steps = 0;
            while (_entry.MoveNext() && ++steps < 10000) { }
            Assert.Less(steps, 10000, "entry must terminate; this fixture requires nonzero Unity deltaTime");
            Assert.AreSame(_car, _system.Driving); Assert.AreSame(_view, _car.Driver); Assert.IsFalse(_system.Busy);
            Assert.IsTrue(_player.InVehicle); Assert.IsTrue(_player.Body.Suspended); Assert.IsFalse(_tapCollider.enabled);
            Assert.AreSame(_car.transform, _player.transform.parent); Assert.IsFalse(_car.Body.isKinematic);
            Assert.AreEqual(0, _abortCount); Assert.AreEqual(0f, (float)Get(_car, "_doorLeftTarget"));
        }

        [Test]
        public void RevocationAtFinalDoorBoundaryAbortsWithoutWaitingForBridgeUpdate()
        {
            AcceptedGate(); var onFoot = _player.transform.position;
            Begin(); Assert.IsTrue(_entry.MoveNext()); Assert.IsTrue(_entry.MoveNext()); AdvanceToCloseWait();
            _state.driver = "another-user";
            Assert.IsFalse(_entry.MoveNext());
            AssertOnFoot(onFoot); Assert.IsNull(_system.Driving); Assert.IsFalse(_system.Busy);
            Assert.IsTrue(_car.Body.isKinematic); Assert.AreEqual(1, _abortCount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitDisableOrDestroyHandlerWhileSeatedRestoresOnFootStateAndCancelsTheEntry(bool destroy)
        {
            AcceptedGate(); var onFoot = _player.transform.position;
            Begin(); Assert.IsTrue(_entry.MoveNext()); Assert.IsTrue(_entry.MoveNext());
            Assert.AreSame(_car.transform, _player.transform.parent);
            Assert.IsFalse(_tapCollider.enabled);
            // This disabled EditMode component does not receive runtime lifecycle callbacks automatically.
            // Invoke the production handler explicitly; this asserts its behavior, not Unity's callback scheduling.
            if (destroy) { Call(_system, "OnDestroy"); Object.DestroyImmediate(_system); }
            else Call(_system, "OnDisable");
            AssertOnFoot(onFoot); Assert.IsTrue(_car.Body.isKinematic); Assert.AreEqual(1, _abortCount);
            Assert.IsFalse(_entry.MoveNext(), "an old suspended iterator must never complete after teardown");
            Assert.AreEqual(1, _abortCount, "cleanup is idempotent when the old iterator finally unwinds");
            Assert.IsNull(Get(_bridge, "_claimId"));
        }

        [Test]
        public void BridgeTeardownWhileSeatedDetachesTheAvatarBeforeReleasingItsCar()
        {
            AcceptedGate(); var onFoot = _player.transform.position;
            Begin(); Assert.IsTrue(_entry.MoveNext()); Assert.IsTrue(_entry.MoveNext());
            Call(_bridge, "OnDestroy");
            AssertOnFoot(onFoot); Assert.IsTrue(_car.Body.isKinematic);
            Assert.IsFalse(_entry.MoveNext()); Assert.AreEqual(1, _abortCount);
            Assert.IsNull(Get(_bridge, "_claimId"));
        }

        [Test]
        public void ReconnectionDuringDoorWaitDoesNotReviveCancelledEntry()
        {
            AcceptedGate(); var onFoot = _player.transform.position;
            Begin(); Assert.IsTrue(_entry.MoveNext());
            Set(_channel, "_dropped", true); Call(_bridge, "Update");
            Set(_channel, "_dropped", false);
            Assert.IsFalse(_entry.MoveNext());
            AssertOnFoot(onFoot); Assert.IsNull(_system.Driving); Assert.AreEqual(1, _abortCount);
        }

        [Test]
        public void AbortFromUnrelatedOldVehicleDoesNotReleaseCurrentClaim()
        {
            AcceptedGate(); Begin(); Assert.IsTrue(_entry.MoveNext());
            var old = Own("old-cancelled-vehicle").AddComponent<Vehicle>();
            typeof(VehicleNetworkBridge).GetMethod("EntryAborted", Private).Invoke(_bridge, new object[] { old });
            Assert.AreSame(_car, Get(_bridge, "_claim")); Assert.AreEqual("entry-test-car", Get(_bridge, "_claimId"));
            int steps = 0; while (_entry.MoveNext() && ++steps < 10000) { }
            Assert.Less(steps, 10000); Assert.AreSame(_car, _system.Driving);
        }

        [Test]
        public async Task CancelledPendingAcknowledgmentCannotPublishAClaim()
        {
            var sent = new TaskCompletionSource<bool>(); int writes = 0;
            _room.Connection.Transmit = _ => ++writes == 1 ? sent.Task : Task.CompletedTask;
            using var owner = new CancellationTokenSource();
            var authorize = (Task<bool>)typeof(VehicleNetworkBridge).GetMethod("Authorize", Private)
                .Invoke(_bridge, new object[] { _car, owner.Token });
            Assert.IsFalse(authorize.IsCompleted);
            owner.Cancel(); sent.SetResult(true);
            try { await authorize; Assert.Fail("cancelled entry must not accept the pending acknowledgment"); }
            catch (OperationCanceledException) { }
            Assert.IsNull(Get(_bridge, "_claimId")); Assert.IsNull(_system.Driving); Assert.IsFalse(_player.InVehicle);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LateAcknowledgmentDoesNotStealKnockdownOrRecoveryControl(bool recovery)
        {
            AcceptedGate(); var onFoot = _player.transform.position;
            var sent = new TaskCompletionSource<bool>(); int writes = 0;
            _room.Connection.Transmit = _ => ++writes == 1 ? sent.Task : Task.CompletedTask;
            Begin(); Assert.IsTrue(_entry.MoveNext());
            Assert.IsFalse(_permission.IsCompleted); Assert.IsFalse(_player.InVehicle);
            Assert.IsNull(Get(_bridge, "_claimId"), "the player can still be hit before acknowledgment");
            if (recovery) _player.SetRecoveryPaused(true);
            else ((IKnockdownOwner)_player).SetControl(false);
            sent.SetResult(true); Assert.IsTrue(await _permission, "the server acknowledged after another controller took ownership");
            Assert.IsFalse(_entry.MoveNext(), "late acknowledgment must not start physical entry while another controller owns the person");
            Assert.IsFalse(_system.Busy); Assert.IsNull(_system.Driving); Assert.IsFalse(_player.InVehicle);
            Assert.AreEqual(recovery, _player.Recovering); Assert.AreEqual(!recovery, _player.Stunned);
            Assert.IsTrue(_player.Body.Suspended); Assert.IsFalse(_player.Body.Controller.enabled);
            Assert.AreSame(_parent, _player.transform.parent); Assert.IsTrue(_tapCollider.enabled);
            Assert.AreEqual(onFoot, _player.transform.position); Assert.IsNull(Get(_bridge, "_claimId"));
            Assert.IsTrue(_car.Body.isKinematic); Assert.AreEqual(1, _abortCount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompetingControlDuringPhysicalEntryRemainsSuspendedAfterAbort(bool recovery)
        {
            AcceptedGate(); var onFoot = _player.transform.position;
            Begin(); Assert.IsTrue(_entry.MoveNext()); Assert.IsTrue(_entry.MoveNext());
            Assert.AreSame(_car.transform, _player.transform.parent); Assert.IsFalse(_tapCollider.enabled);
            if (recovery) _player.SetRecoveryPaused(true);
            else ((IKnockdownOwner)_player).SetControl(false);
            Assert.IsFalse(_entry.MoveNext());
            Assert.IsFalse(_system.Busy); Assert.IsNull(_system.Driving); Assert.IsFalse(_player.InVehicle);
            Assert.AreEqual(recovery, _player.Recovering); Assert.AreEqual(!recovery, _player.Stunned);
            Assert.IsTrue(_player.Body.Suspended); Assert.IsFalse(_player.Body.Controller.enabled);
            Assert.AreSame(_parent, _player.transform.parent); Assert.IsTrue(_tapCollider.enabled);
            Assert.AreEqual(onFoot, _player.transform.position); Assert.IsNull(Get(_bridge, "_claimId"));
            Assert.IsTrue(_car.Body.isKinematic); Assert.AreEqual(1, _abortCount);
        }

        [Test]
        public void LeavingOpenWorldDuringPhysicalEntryAbortsEvenBeforeVisitTeardown()
        {
            AcceptedGate(); var onFoot = _player.transform.position;
            Begin(); Assert.IsTrue(_entry.MoveNext()); Assert.IsTrue(_entry.MoveNext());
            _player.OpenWorld = false;
            Assert.IsFalse(_entry.MoveNext());
            AssertOnFoot(onFoot); Assert.IsNull(_system.Driving); Assert.IsFalse(_system.Busy);
            Assert.IsTrue(_car.Body.isKinematic); Assert.IsNull(Get(_bridge, "_claimId")); Assert.AreEqual(1, _abortCount);
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void OnlySixMetreFloorIsAvailableOnlyToOfflineExit(bool online, bool expected)
        {
            Set(_visit, "_online", online);
            var floor = Own("six-metre-exit-floor"); floor.layer = 10; floor.transform.position = new Vector3(0, -.1f, 6f);
            floor.AddComponent<BoxCollider>().size = new Vector3(1f, .2f, 1f);
            Physics.SyncTransforms();
            bool found = VehicleSystem.FindSafeExit(_car, out var point, out _);
            Assert.AreEqual(expected, found);
            if (found) Assert.That(Vector2.Distance(new Vector2(point.x, point.z), Vector2.zero), Is.InRange(5.9f, 6.1f));
        }

        [TearDown]
        public void Cleanup()
        {
            (_entry as IDisposable)?.Dispose(); _entry = null;
            VehicleSystem.EntryAborted -= _aborted; VehicleSystem.DrivingChanged -= _drivingChanged;
            if (_bridge != null) Set(_bridge, "_channel", null);
            if (_visit != null) { Set(_visit, "_visit", null); Set(_visit, "_ready", false); }
            for (int i = _objects.Count - 1; i >= 0; i--) if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear(); _visitOwner?.Dispose();
            Current(typeof(VehicleSystem), _previousSystem); Current(typeof(VenueSession), _previousVisit); Current(typeof(VehicleNetworkBridge), _previousBridge);
            VehicleSystem.AuthorizeEnter = _previousGate;
            typeof(CameraRig).GetProperty("Instance").GetSetMethod(true)
                .Invoke(null, new object[] { _previousCamera != null ? _previousCamera : null });
        }
    }
}
