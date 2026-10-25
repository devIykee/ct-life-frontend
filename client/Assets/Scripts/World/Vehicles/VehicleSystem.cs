using System;
using System.Collections;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>The local player got into a vehicle (or out of it: null).</summary>
    public struct VehicleChangedSignal { public Vehicle vehicle; }

    /// <summary>
    /// The local player and vehicles on CT Island (Feel lane F2): parked vehicles near the player, the "Drive / Ride / Fly"
    /// offer beside a free one, the GTA-style way in (walk to the driver's door, it opens, sit in, it shuts) and out, the
    /// driver in the seat with hands on the wheel, the chase camera, streaming prefetch ahead of fast travel, recovery
    /// from water, and the pocket camera drone. Public road vehicles use acknowledged server leases online;
    /// offline showcase craft remain local. Online recovery moves the person, never a server-owned car.
    /// </summary>
    public sealed class VehicleSystem : MonoBehaviour
    {
        public static VehicleSystem Current { get; private set; }
        public static event Action<Vehicle> DrivingChanged;
        public static event Action AccessChanged;
        /// <summary>Bridge-owned server acknowledgement before the physical entry animation.</summary>
        public static Func<Vehicle, System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>> AuthorizeEnter;
        public static event Action<Vehicle> EntryAborted;
        private sealed class EntryTransition
        {
            public Vehicle Vehicle;
            public System.Threading.CancellationTokenSource Owner;
            public VehicleNetworkBridge Authority;
            public bool RequiresAuthority, Physical, Completed, Finished;
            public Transform Parent;
            public Vector3 OnFootPosition;
            public float OnFootYaw;
        }
        private EntryTransition _entry;
        /// <summary>The owned flyer: since v3 (2026-10-21) the hovercar (the Roadstar slot) is what "drone" means to the
        /// player; the quadcopter code stays but is off the default HUD. One entitlement unlocks both.</summary>
        private bool _ownsDrone;
        public bool CanDeployDrone => _ownsDrone || (ShowcaseEnabled && RuntimeMode.IsOfflineIsland);
        public bool CanUseKind(VehicleKind kind) => AccessAllowed(kind, _ownsDrone, ShowcaseEnabled && RuntimeMode.IsOfflineIsland);
        public static bool AccessAllowed(VehicleKind kind, bool ownsDrone, bool offlineShowcase) =>
            kind == VehicleKind.Car || kind == VehicleKind.Scooter || ((kind == VehicleKind.Drone || kind == VehicleKind.Roadstar) && ownsDrone) || offlineShowcase;
        public void SetDroneEntitlement(bool owned) { if (_ownsDrone == owned) return; _ownsDrone = owned; AccessChanged?.Invoke(); }
        public static bool ShowcaseEnabled
        {
            get
            {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                return LaunchArgs.Get("showcaseFleet") == "1";
#else
                return false;
#endif
            }
        }

        public const float OfferRange = 3.2f, ExitMaxSpeed = 3f;
        /// <summary>The network bridge explained a refused entry itself (steal prompt, jail, taken): skip the generic toast once.</summary>
        public static bool SuppressEntryToast;
        /// <summary>A script (the dev demo, editor captures) drives: the HUD must not overwrite the vehicle's input.</summary>
        public static bool ScriptedInput;

        public Vehicle Driving { get; private set; }
        public Vehicle Offered { get; private set; }
        /// <summary>A hovercar beside the player that they do not own: the prompt offers the Duo Shop, never entry.</summary>
        public Vehicle LockedOffer { get; private set; }
        public bool Busy { get; private set; }
        public readonly List<Vehicle> Vehicles = new List<Vehicle>();

        private LocalPlayerController _player;
        private AvatarView _view;
        private Transform _avatarParent;
        private Vector3 _seatOffset;
        private Vector3 _lastSafe;
        private Quaternion _lastSafeRot;
        private float _safeAt, _prefetchAt;
        private Vehicle _drone;
        private bool _leaseRecovery;

        public static VehicleSystem Ensure(LocalPlayerController player)
        {
            if (Current != null && Current._player == player) return Current;
            if (Current != null) Destroy(Current.gameObject);
            var go = new GameObject("Vehicles");
            go.transform.SetParent(player.transform.parent, false);
            var s = go.AddComponent<VehicleSystem>();
            s._player = player;
            s._view = player.GetComponent<AvatarView>();
            // the hovercar is a bought asset: the flyer button and the summon follow the player's entitlement
            if (Services.TryGet<IEntitlements>(out var owned))
            {
                s._entitlements = owned;
                owned.Changed += s.OnEntitlements;
                s._ownsDrone = owned.Owns(Entitlement.Hovercar);
            }
            Current = s;
            AccessChanged?.Invoke();
            // AI traffic on the island's roads (it waits for World's RoadNetwork to be loaded)
            TrafficSystem.Ensure(player, player.transform.parent);
            // and people on its sidewalks (World W4's walkable network)
            CTLife.World.Pedestrians.PedestrianSystem.Ensure(player, player.transform.parent);
            return s;
        }

        private readonly ParkedCars _parked = new ParkedCars();
        public ParkedCars Parked => _parked;
        private float _cleanAt;

        private IEntitlements _entitlements;
        private void OnEntitlements() => SetDroneEntitlement(_entitlements != null && _entitlements.Owns(Entitlement.Hovercar));

        private void OnDestroy()
        {
            if (_entitlements != null) _entitlements.Changed -= OnEntitlements;
            FinishEntry(_entry);
            _parked.Clear(this);
            if (Current == this) { Current = null; AccessChanged?.Invoke(); }
        }

        private void OnDisable() => FinishEntry(_entry);

        private IEnumerator Start()
        {
            // Let the island settle under the arrival, then park the fleet around it.
            for (int i = 0; i < 20; i++) yield return null;
            if (_player != null && ShowcaseEnabled) VehicleSpawner.SpawnShowcase(this, _player.transform.position, _player.transform.forward);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            // dev builds: `vehicleDemo=1` drives the fleet on its own for Seeker clips and frame-rate numbers
            if (LaunchArgs.Get("vehicleDemo") is string demo && demo.Length > 0 && _player != null) StartCoroutine(VehicleDemo.Run(this, _player, demo));
#endif
        }

        public Vehicle Spawn(string specId, Vector3 position, Quaternion rotation)
        {
            var spec = VehicleCatalog.Get(specId);
            // The hovercar may be parked locked (shown, never entered: Enter and FindOffer check CanUseKind).
            if (spec == null || (!CanUseKind(spec.kind) && spec.kind != VehicleKind.Roadstar)) return null;
            var prefab = VehiclePrefabs.Get(specId, true);   // usually preloaded with the traffic kinds
            if (prefab == null) { Log.Warn("vehicles", "missing prefab Vehicles/" + specId); return null; }
            var go = Instantiate(prefab, position, rotation, transform);
            go.name = specId;
            var v = go.GetComponent<Vehicle>();
            if (v != null) Vehicles.Add(v);
            return v;
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (_player == null) { Destroy(gameObject); return; }
            var me = Driving != null ? Driving.transform.position : _player.transform.position;
            _parked.Update(this, me);
            if (Time.time >= _cleanAt) { _cleanAt = Time.time + 2f; Abandoned(me); }
            Offered = Driving == null && !Busy ? FindOffer() : null;
            LockedOffer = Driving == null && !Busy && Offered == null ? FindLocked() : null;
            if (Driving != null)
            {
                if (Time.time >= _prefetchAt && Driving.Body != null)
                {
                    _prefetchAt = Time.time + 1f;
                    VehicleWorld.Prefetch(Driving.transform.position, Driving.Body.linearVelocity, Mathf.Clamp(250f / Mathf.Max(1f, Driving.Speed), 4f, 10f));
                }
                RecoverFromWater();
                if (Driving == _drone && _drone != null && _drone.GetComponent<FlightPhysics>() is FlightPhysics f)
                {
                    if (f.AtLeash && !_droneRangeToast) { _droneRangeToast = true; Bus.Publish(new InteractionToastSignal { key = "drone.range" }); }
                    else if (!f.AtLeash) _droneRangeToast = false;
                }
                if (Driving != null && Driving.Kind == VehicleKind.Roadstar && Driving.GetComponent<FlightPhysics>() is FlightPhysics rf)
                {
                    bool blocked = rf.LandingBlockedByWater;
                    if (blocked && !_waterToast) Bus.Publish(new InteractionToastSignal { key = "vehicle.noWaterLanding" });
                    _waterToast = blocked;
                }
            }
        }

        private bool _droneRangeToast, _waterToast;

        /// <summary>Cars left far behind (driven off and dropped somewhere) go once they are out of sight.</summary>
        private void Abandoned(Vector3 me)
        {
            var cam = Camera.main;
            for (int i = Vehicles.Count - 1; i >= 0; i--)
            {
                var v = Vehicles[i];
                if (v == null) { Vehicles.RemoveAt(i); continue; }
                if (v.Occupied || v == _drone || v == _hovercar || v.Kind == VehicleKind.Helicopter || v.Kind == VehicleKind.Roadstar || (v.transform.position - me).sqrMagnitude < 320f * 320f) continue;
                if (cam != null)
                {
                    var vp = cam.WorldToViewportPoint(v.transform.position);
                    if (vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f) continue;
                }
                Vehicles.RemoveAt(i);
                Destroy(v.gameObject);
            }
        }

        private Vehicle FindOffer()
        {
            if (_player.InVehicle || !string.IsNullOrEmpty(_player.SeatedSpot) || _player.Stunned || (_view != null && _view.SeatPresentationActive)) return null;
            var me = _player.transform.position;
            Vehicle best = null; float bestD = OfferRange;
            foreach (var v in Vehicles)
            {
                if (v == null || !CanUseKind(v.Kind) || v.Occupied || v.Kind == VehicleKind.Drone || v.Speed > 1f) continue;
                var d = Vector3.Distance(me, v.EntryPoint);
                float near = Mathf.Min(d, Vector3.Distance(me, v.transform.position) - 1.2f);
                if (near < bestD) { bestD = near; best = v; }
            }
            return best;
        }

        private Vehicle FindLocked()
        {
            if (_player.InVehicle || !string.IsNullOrEmpty(_player.SeatedSpot) || _player.Stunned || (_view != null && _view.SeatPresentationActive)) return null;
            var me = _player.transform.position;
            Vehicle best = null; float bestD = OfferRange;
            foreach (var v in Vehicles)
            {
                if (v == null || v.Kind != VehicleKind.Roadstar || CanUseKind(v.Kind) || v.Occupied) continue;
                float near = Mathf.Min(Vector3.Distance(me, v.EntryPoint), Vector3.Distance(me, v.transform.position) - 1.2f);
                if (near < bestD) { bestD = near; best = v; }
            }
            return best;
        }

        /// <summary>The CTA verb for a vehicle (UI Strings key) and its icon.</summary>
        public static (string key, string icon) Verb(Vehicle v) => v == null ? ("vehicle.drive", "drive") : v.Kind switch
        {
            VehicleKind.Scooter => ("vehicle.ride", "moped"),
            VehicleKind.Helicopter => ("vehicle.fly", "fly"),
            _ => ("vehicle.drive", "drive"),
        };

        private void LateUpdate()
        {
            // the driver: hips on the seat, hands on the wheel (or the bars), following the steering
            if (Driving == null || _view == null || Busy) return;
            HoldSeat(Driving, 1f);
            Grip(Driving);
        }

        private void HoldSeat(Vehicle v, float blend)
        {
            if (v.Seat == null) return;
            var t = _player.transform;
            var targetPos = v.Seat.position - v.transform.rotation * _seatOffset;
            t.SetPositionAndRotation(Vector3.Lerp(t.position, targetPos, blend), Quaternion.Slerp(t.rotation, v.transform.rotation, blend));
        }

        private void Grip(Vehicle v) => GripWheel(v, _view);

        /// <summary>Hands on the wheel (or the bars) of <paramref name="v"/> for a seated avatar; the player and NPC drivers.</summary>
        public static void GripWheel(Vehicle v, AvatarView view)
        {
            var ik = view != null && view.Animator != null ? view.Animator.GetComponent<CharacterIK>() : null;
            if (ik == null || v == null) return;
            var right = v.transform.right; var up = v.transform.up;
            if (v.Kind == VehicleKind.Scooter)
            {
                var bar = v.Handlebar;
                if (bar == null) return;
                ik.Grip(bar.position - right * 0.32f + up * 0.02f, bar.position + right * 0.32f + up * 0.02f, right);
                return;
            }
            if (v.SteeringCenter == null || v.Kind == VehicleKind.Helicopter) return;
            float steer = v.GetComponent<CarPhysics>() is CarPhysics cp ? cp.SteerAngle : 0f;
            var column = (v.SteeringCenter.position - v.Seat.position).normalized;
            var turn = Quaternion.AngleAxis(-steer * 6f, column);
            var c = v.SteeringCenter.position;
            ik.Grip(c + turn * (-right * 0.16f + up * 0.05f), c + turn * (right * 0.16f + up * 0.05f), right);
        }

        // ------------------------------------------------------------------ in and out

        public bool Enter(Vehicle v)
        {
            if (v == null || !CanUseKind(v.Kind) || Busy || Driving != null || v.Occupied || _player == null || !_player.OpenWorld || _player.Stunned || _player.Recovering) return false;
            StartCoroutine(EnterRoutine(v));
            return true;
        }

        public bool Exit()
        {
            if (Driving == null || Busy) return false;
            // in the air (the helicopter, the Roadstar in flight): land first
            if ((Driving.Kind == VehicleKind.Helicopter || Driving.Flying) && Driving.GetComponent<FlightPhysics>() is FlightPhysics f && !f.CanExit)
            { Bus.Publish(new InteractionToastSignal { key = f.OverWater ? "vehicle.noWaterLanding" : "vehicle.landFirst" }); return false; }
            StartCoroutine(ExitRoutine());
            return true;
        }

        /// <summary>Lost online authority: recover the person without moving or simulating the server's vehicle.</summary>
        public bool RecoverLostNetworkLease()
        {
            if (_leaseRecovery || Busy || Driving == null || _player == null) return false;
            var v = Driving;
            var safety = _player.GetComponent<PlayerSafety>();
            if (safety == null) return false;
            _leaseRecovery = true; Busy = true; v.Input = VehicleInput.None;
            bool scheduled = safety.RecoverDriver(v.transform.position, () =>
            {
                if (_player == null) return;
                _player.transform.SetParent(_avatarParent, true);
                MuteColliders(false);
                if (v != null) { v.Driver = null; if (!v.RemoteOccupied) v.EngineOn = false; }
                Driving = null;
                CameraRig.Instance?.FollowAvatar(_player.transform);
                DrivingChanged?.Invoke(null);
                Bus.Publish(new VehicleChangedSignal { vehicle = null });
            }, () => { _leaseRecovery = false; Busy = false; });
            if (!scheduled) { _leaseRecovery = false; Busy = false; }
            return scheduled;
        }

        private bool EntryCurrent(EntryTransition entry) => entry != null && !entry.Finished &&
            ReferenceEquals(_entry, entry) && !entry.Owner.IsCancellationRequested && _player != null &&
            _player.OpenWorld && !_player.Stunned && !_player.Recovering && entry.Vehicle != null &&
            !entry.Vehicle.RemoteOccupied && (entry.Vehicle.Driver == null || entry.Vehicle.Driver == _view) &&
            (!entry.RequiresAuthority || (entry.Authority != null && ReferenceEquals(VehicleNetworkBridge.Current, entry.Authority) &&
                entry.Authority.HasEntryAuthority(entry.Vehicle)));

        private IEnumerator EnterRoutine(Vehicle v)
        {
            var authorize = AuthorizeEnter;
            var entry = new EntryTransition
            {
                Vehicle = v, Owner = new System.Threading.CancellationTokenSource(),
                RequiresAuthority = authorize != null || VenueSession.Current?.IsOnline == true, Authority = VehicleNetworkBridge.Current,
                Parent = _player.transform.parent, OnFootPosition = _player.transform.position,
                OnFootYaw = _player.transform.eulerAngles.y
            };
            _entry = entry; Busy = true;
            try
            {
                if (authorize != null)
                {
                    System.Threading.Tasks.Task<bool> permission = null;
                    try { permission = authorize(v, entry.Owner.Token); }
                    catch (Exception e) { Log.Warn("vehicles", "entry unavailable: " + e.GetType().Name); }
                    float until = Time.unscaledTime + 12f;
                    while (!entry.Finished && v != null && !entry.Owner.IsCancellationRequested && permission != null && !permission.IsCompleted && Time.unscaledTime < until)
                        yield return null;
                    if (v == null || permission == null || !permission.IsCompleted || permission.IsFaulted || permission.IsCanceled || !permission.Result)
                    {
                        if (!SuppressEntryToast) Bus.Publish(new InteractionToastSignal { key = "vehicle.entryUnavailable" });
                        SuppressEntryToast = false;
                        yield break;
                    }
                }
                if (!EntryCurrent(entry)) yield break;
                bool hasDoor = v.DoorLeft != null;
                // 1. Walk to the driver's door. The accepted lease must survive the approach as well as the animation.
                var approach = v.EntryPoint;
                if (Vector3.Distance(_player.transform.position, approach) > 0.5f && _player.MoveTo(approach))
                {
                    float t0 = Time.time;
                    while (_player.HasDestination && Time.time - t0 < 4f && EntryCurrent(entry)) yield return null;
                }
                if (!EntryCurrent(entry)) yield break;
                // Keep the last actual on-foot pose, before suspending or parenting the player to the car.
                entry.Parent = _player.transform.parent;
                entry.OnFootPosition = _player.transform.position; entry.OnFootYaw = _player.transform.eulerAngles.y;
                _avatarParent = entry.Parent;
                // 2. Face the seat and open the door.
                entry.Physical = true;
                _player.EnterVehicleMode();
                _view.SetSpeed(0f);
                if (hasDoor) { v.SetDoor(true); AudioFor(v)?.Door(true); yield return new WaitForSeconds(0.32f); }
                if (!EntryCurrent(entry)) yield break;
                // 3. Settle onto the seat. Check authority each frame, including while avatar colliders are muted.
                MuteColliders(true);
                _player.transform.SetParent(v.transform, true);
                _view.SetBase("act_drive");
                var from = _player.transform.position; var fromRot = _player.transform.rotation;
                _seatOffset = MeasureSeatOffset(_view);
                // The hovercar has no doors and a closed canopy: a half-second slide through its hull reads as clipping, so the
                // driver steps in at once (one settle frame, straight onto the seat).
                bool hopIn = !hasDoor && v.Kind == VehicleKind.Roadstar;
                for (float t = hopIn ? 0.999f : 0f; t < 1f; t += Time.deltaTime / 0.5f)
                {
                    if (!EntryCurrent(entry)) yield break;
                    _seatOffset = Vector3.Lerp(_seatOffset, MeasureSeatOffset(_view), 0.25f);
                    float e = t * t * (3f - 2f * t);
                    var to = v.Seat != null ? v.Seat.position - v.transform.rotation * _seatOffset : v.transform.position;
                    _player.transform.SetPositionAndRotation(Vector3.Lerp(from, to, e), Quaternion.Slerp(fromRot, v.transform.rotation, e));
                    yield return null;
                }
                if (!EntryCurrent(entry)) yield break;
                if (hasDoor) { v.SetDoor(false); yield return new WaitForSeconds(0.25f); }
                if (!EntryCurrent(entry)) yield break;
                // 4. Publish control only after the last physical boundary still has the same live lease.
                v.Driver = _view; v.EngineOn = true;
                if (v.Body != null) { v.Body.isKinematic = false; v.Body.WakeUp(); }
                if (hasDoor) AudioFor(v)?.Door(false);
                AudioFor(v)?.StartEngine();
                Driving = v; entry.Completed = true;
                _lastSafe = v.transform.position; _lastSafeRot = v.transform.rotation; _safeAt = Time.time;
                CameraRig.Instance?.FollowVehicle(v);
                Busy = false;
                DrivingChanged?.Invoke(v);
                Bus.Publish(new VehicleChangedSignal { vehicle = v });
            }
            finally { FinishEntry(entry); }
        }

        internal void CancelPendingEntry(Vehicle vehicle)
        {
            if (_entry != null && ReferenceEquals(_entry.Vehicle, vehicle)) FinishEntry(_entry);
        }

        /// <summary>Idempotent cleanup also runs when the component is disabled/destroyed during an entry yield.</summary>
        private void FinishEntry(EntryTransition entry)
        {
            if (entry == null || entry.Finished) return;
            entry.Finished = true;
            try
            {
                if (entry.Completed) return;
                entry.Owner.Cancel();
                if (_player != null)
                {
                    _player.CancelMovement();
                    if (entry.Physical)
                    {
                        _player.transform.SetParent(entry.Parent != null ? entry.Parent : null, true);
                        MuteColliders(false);
                        _player.ExitVehicleMode(entry.OnFootPosition, entry.OnFootYaw);
                        // Entry gives up its suspension; an overlapping knockdown/recovery still owns control.
                        if (_player.Stunned || _player.Recovering) _player.SetRecoveryPaused(_player.Recovering);
                        CameraRig.Instance?.FollowAvatar(_player.transform);
                    }
                }
                var v = entry.Vehicle;
                if (v != null)
                {
                    v.SetDoor(false);
                    if (v.Driver == _view) v.Driver = null;
                    if (!v.RemoteOccupied) v.EngineOn = false;
                    v.Input = VehicleInput.None;
                }
            }
            finally
            {
                // Release even if physical cleanup fails during scene teardown.
                try { if (!entry.Completed) EntryAborted?.Invoke(entry.Vehicle); }
                finally
                {
                    entry.Owner.Dispose();
                    if (ReferenceEquals(_entry, entry)) { _entry = null; Busy = false; }
                }
            }
        }

        /// <summary>Hips position in the avatar root's frame for the current pose (the seat is where the hips go).</summary>
        public static Vector3 MeasureSeatOffset(AvatarView view)
        {
            var a = view != null ? view.Animator : null;
            var hips = a != null && a.isHuman ? a.GetBoneTransform(HumanBodyBones.Hips) : null;
            // Imported cast rigs expose Hips while retaining their optimized hierarchy. Only non-Humanoid or
            // placeholder actors need the legacy estimate; both traffic and player use this same measured path.
            if (hips == null) return new Vector3(0f, 0.5f, 0.05f);
            var t = view.transform;
            var local = Quaternion.Inverse(t.rotation) * (hips.position - t.position);
            return local;
        }

        private IEnumerator ExitRoutine()
        {
            Busy = true;
            var v = Driving;
            // brake to a near stop first
            float t0 = Time.time;
            while (v != null && v.Speed > ExitMaxSpeed && Time.time - t0 < 2.5f)
            {
                v.Input = new VehicleInput { brake = 1f, handbrake = v.Speed < 8f };
                yield return null;
            }
            if (v == null) { MuteColliders(false); Busy = false; yield break; }
            if (v.Speed > ExitMaxSpeed || !FindSafeExit(v, out var exit, out float yaw))
            { Busy = false; Bus.Publish(new InteractionToastSignal { key = "vehicle.flatExit" }); yield break; }
            v.Input = new VehicleInput { brake = 1f, handbrake = true };
            if (v.GetComponent<FlightPhysics>() is FlightPhysics f && v.Kind == VehicleKind.Roadstar) f.SetFlight(false);
            bool passenger = Vector3.Dot(exit - v.transform.position, v.transform.right) > 0f;
            bool hasDoor = passenger ? v.DoorRight != null : v.DoorLeft != null;
            if (hasDoor) { v.SetDoor(true, passenger); AudioFor(v)?.Door(true); yield return new WaitForSeconds(0.3f); }
            // out beside the door, facing away from the car
            // Re-check after the door animation: another car/person or streaming can change the safe spot.
            if (!FindSafeExit(v, out exit, out yaw))
            { v.SetDoor(false, passenger); Busy = false; Bus.Publish(new InteractionToastSignal { key = "vehicle.flatExit" }); yield break; }
            _player.transform.SetParent(_avatarParent, true);
            MuteColliders(false);
            v.Driver = null;
            v.EngineOn = false;
            v.Input = new VehicleInput { brake = 1f, handbrake = true };
            Driving = null;
            _player.ExitVehicleMode(exit, yaw);
            CameraRig.Instance?.FollowAvatar(_player.transform);
            DrivingChanged?.Invoke(null);
            Bus.Publish(new VehicleChangedSignal { vehicle = null });
            AudioFor(v)?.StopEngine();
            if (hasDoor) { yield return new WaitForSeconds(0.35f); if (v != null) { v.SetDoor(false, passenger); AudioFor(v)?.Door(false); } }
            Busy = false;
        }

        private static VehicleAudio AudioFor(Vehicle v) => v != null ? v.GetComponent<VehicleAudio>() : null;

        private readonly List<Collider> _muted = new List<Collider>();

        /// <summary>Switch the avatar's colliders off while seated (the CharacterController is suspended separately).</summary>
        private void MuteColliders(bool mute)
        {
            if (mute)
            {
                _muted.Clear();
                foreach (var c in _player.GetComponentsInChildren<Collider>(true))
                    if (c.enabled && !(c is CharacterController) && c.gameObject.layer != CharacterLayers.Ragdoll) { c.enabled = false; _muted.Add(c); }
                return;
            }
            foreach (var c in _muted) if (c != null) c.enabled = true;
            _muted.Clear();
        }

        public const float DroneRange = 150f;

        public static bool FindSafeExit(Vehicle v, out Vector3 point, out float yaw)
        {
            point = default; yaw = 0f;
            if (v == null) return false;
            var origin = v.transform.position;
            float width = v.Kind == VehicleKind.Helicopter ? 3f : 1.8f;
            var candidates = new List<Vector3> { v.Entry != null ? v.Entry.position : origin - v.transform.right * width,
                origin + v.transform.right * width, origin - v.transform.forward * 3f, origin + v.transform.forward * 3f };
            foreach (float radius in new[] { 2f, 3f, 4f, 6f })
                for (int i = 0; i < 16; i++) candidates.Add(origin + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * radius);
            bool online = VenueSession.Current?.IsOnline == true;
            foreach (var candidate in candidates)
            {
                // The backend's first on-foot move grant is within 5m X/Z of the final car pose.
                // Keep 1m headroom for timing/pose differences; the wider local-only fallback stays offline.
                if (online && Vector3.ProjectOnPlane(candidate - origin, Vector3.up).sqrMagnitude > 16f) continue;
                var p = candidate; p.y = origin.y;
                if (!PlayerSafety.TrySafeGround(p, 1.2f, out point)) continue;
                if (Physics.CheckCapsule(point + Vector3.up * 0.4f, point + Vector3.up * 1.3f, 0.32f,
                    1 << CharacterLayers.Characters, QueryTriggerInteraction.Collide)) continue;
                if (Physics.Linecast(origin + Vector3.up * 0.6f, point + Vector3.up * 0.6f, CarPhysics.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                var away = Vector3.ProjectOnPlane(point - origin, Vector3.up);
                yaw = away.sqrMagnitude > 0.01f ? Quaternion.LookRotation(away).eulerAngles.y : v.transform.eulerAngles.y;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the hovercar (the owned flyer)

        public const string HovercarId = "roadstar";
        private Vehicle _hovercar;

        /// <summary>
        /// The HUD's flyer button: the player's hovercar comes to them. It sets down on clear ground beside the player (or,
        /// already near and free, stays where it is), then the usual way in follows: walk to its door and sit in.
        /// </summary>
        public bool SummonHovercar()
        {
            if (!CanDeployDrone || Busy || Driving != null || _player == null || _player.InVehicle || _player.Recovering) return false;
            var t = _player.transform;
            if (_hovercar == null || !_hovercar.gameObject.activeInHierarchy)
                foreach (var v in Vehicles) if (v != null && v.specId == HovercarId && !v.Occupied) { _hovercar = v; break; }
            bool near = _hovercar != null && !_hovercar.Occupied && (_hovercar.transform.position - t.position).sqrMagnitude < 14f * 14f;
            if (!near)
            {
                var spec = VehicleCatalog.Get(HovercarId);
                if (!VehicleSpawner.TryClearGround(t.position, t.forward, new Vector3(2.4f, 1.9f, 5.2f), null, 4.5f, 16f, out var pos, out var rot))
                { Bus.Publish(new InteractionToastSignal { key = "drone.clearLaunch" }); return false; }
                if (_hovercar == null || _hovercar.Occupied) _hovercar = Spawn(HovercarId, pos, rot);
                else
                {
                    _hovercar.Body.linearVelocity = Vector3.zero; _hovercar.Body.angularVelocity = Vector3.zero;
                    _hovercar.transform.SetPositionAndRotation(pos, rot);
                    _hovercar.Body.position = pos; _hovercar.Body.rotation = rot;
                }
                if (_hovercar == null || spec == null) return false;
                VehiclePaint.Apply(_hovercar, spec.paint);
            }
            return Enter(_hovercar);
        }

        // ------------------------------------------------------------------ the pocket drone

        /// <summary>Launch (or re-launch) the camera drone from the player's hands; the player stands holding the controller.</summary>
        public bool DeployDrone()
        {
            if (!CanDeployDrone || Busy || Driving != null || _player == null || _player.InVehicle || _player.Recovering) return false;
            var t = _player.transform;
            var at = t.position + t.forward * 2.2f + Vector3.up * 2.6f;
            int launchMask = CharacterBody.Mask & ~((1 << CharacterLayers.Vehicle) | (1 << CharacterLayers.VehicleFlight));
            if (Physics.CheckSphere(at, 0.45f, launchMask, QueryTriggerInteraction.Ignore) ||
                Physics.Linecast(t.position + Vector3.up * 1.5f, at, launchMask, QueryTriggerInteraction.Ignore))
            { Bus.Publish(new InteractionToastSignal { key = "drone.clearLaunch" }); return false; }
            if (_drone == null) _drone = Spawn("drone", at, Quaternion.LookRotation(t.forward));
            else
            {
                // out of the pocket again, from the hands
                _drone.transform.SetPositionAndRotation(at, Quaternion.LookRotation(t.forward));
                _drone.gameObject.SetActive(true);
                _drone.Body.position = at; _drone.Body.rotation = Quaternion.LookRotation(t.forward);
                _drone.Body.linearVelocity = Vector3.zero; _drone.Body.angularVelocity = Vector3.zero;
            }
            if (_drone == null) return false;
            if (_drone.GetComponent<FlightPhysics>() is FlightPhysics leash) { leash.LeashAnchor = t; leash.LeashRadius = DroneRange; }
            _droneRangeToast = false;
            _player.CancelMovement();
            _view.SetBase("act_phone_hold");
            _drone.Driver = _view;
            _drone.EngineOn = true;
            _drone.Body.isKinematic = false;
            Driving = _drone;
            CameraRig.Instance?.FollowVehicle(_drone);
            DrivingChanged?.Invoke(_drone);
            Bus.Publish(new VehicleChangedSignal { vehicle = _drone });
            return true;
        }

        /// <summary>The drone sets down where it is (the camera stays with it), goes back in the pocket, and the view returns.</summary>
        private IEnumerator LandDrone()
        {
            Busy = true;
            var d = _drone;
            float t0 = Time.time;
            while (d != null && d.GetComponent<FlightPhysics>() is FlightPhysics f && !f.Grounded && Time.time - t0 < 7f)
            {
                d.Input = new VehicleInput { lift = f.Altitude > 4f ? -1f : -0.55f };
                yield return null;
            }
            if (d != null)
            {
                d.Input = VehicleInput.None; d.EngineOn = false; d.Driver = null;
                yield return new WaitForSeconds(0.35f);
                if (d != null) d.gameObject.SetActive(false);   // pocketed: no drones left lying about
            }
            Driving = null;
            _view.SetBase("loco_idle");
            CameraRig.Instance?.FollowAvatar(_player.transform);
            DrivingChanged?.Invoke(null);
            Bus.Publish(new VehicleChangedSignal { vehicle = null });
            Busy = false;
        }

        /// <summary>HUD exit for any vehicle: the drone lands, everything else stops and the player gets out.</summary>
        public void ExitOrLand()
        {
            if (Driving != null && Driving.Kind == VehicleKind.Drone) { if (!Busy) StartCoroutine(LandDrone()); return; }
            if (Driving != null && Driving.GetComponent<FlightPhysics>() is FlightPhysics f && (Driving.Kind == VehicleKind.Helicopter || Driving.Flying) && !f.CanExit)
            { f.RequestLanding(); return; }
            Exit();
        }

        // ------------------------------------------------------------------ recovery

        private void RecoverFromWater()
        {
            var v = Driving;
            if (v == null || v.Body == null || v.Kind == VehicleKind.Drone) return;
            var p = v.transform.position;
            bool grounded = v.GetComponent<CarPhysics>() is CarPhysics cp ? cp.Grounded : v.GetComponent<FlightPhysics>()?.Grounded == true;
            if (grounded && VehicleWorld.IsReady(p) && VehicleWorld.Dry(p) && v.transform.up.y > 0.8f && Time.time - _safeAt > 2f) { _lastSafe = p; _lastSafeRot = v.transform.rotation; _safeAt = Time.time; }
            float water = VehicleWorld.WaterSurfaceAt(p);
            bool floor = Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out var floorHit, 3f, CarPhysics.GroundMask, QueryTriggerInteraction.Ignore)
                && floorHit.collider.GetComponentInParent<IslandGroundFallback>() == null;
            bool sunk = (!float.IsNaN(water) && p.y < water - 0.6f) || p.y < -200f ||
                (!floor && VehicleWorld.TryGroundHeight(p.x, p.z, out float ground) && p.y < ground - 2f);
            if (!sunk) return;
            if (VenueSession.Current?.IsOnline == true)
            {
                // The server owns the car's pose. Release only once a nearby person recovery is scheduled;
                // moving the car to a saved point here would be clamped back by vehicle authority.
                if (RecoverLostNetworkLease()) VehicleNetworkBridge.Current?.ReleaseForSafety(v);
                return;
            }
            v.Body.linearVelocity = Vector3.zero; v.Body.angularVelocity = Vector3.zero;
            v.Body.position = _lastSafe + Vector3.up * 0.6f; v.Body.rotation = _lastSafeRot;
            Bus.Publish(new InteractionToastSignal { key = "vehicle.recovered" });
        }
    }
}
