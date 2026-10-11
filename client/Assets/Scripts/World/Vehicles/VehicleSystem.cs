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
    /// from the sea, and the pocket camera drone. Vehicles are this client's only for now (no room message exists for them;
    /// see the server asks), so a driven car is not seen by other players yet.
    /// </summary>
    public sealed class VehicleSystem : MonoBehaviour
    {
        public static VehicleSystem Current { get; private set; }
        public static event Action<Vehicle> DrivingChanged;

        public const float OfferRange = 3.2f, ExitMaxSpeed = 3f;
        /// <summary>A script (the dev demo, editor captures) drives: the HUD must not overwrite the vehicle's input.</summary>
        public static bool ScriptedInput;

        public Vehicle Driving { get; private set; }
        public Vehicle Offered { get; private set; }
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

        public static VehicleSystem Ensure(LocalPlayerController player)
        {
            if (Current != null && Current._player == player) return Current;
            if (Current != null) Destroy(Current.gameObject);
            var go = new GameObject("Vehicles");
            go.transform.SetParent(player.transform.parent, false);
            var s = go.AddComponent<VehicleSystem>();
            s._player = player;
            s._view = player.GetComponent<AvatarView>();
            Current = s;
            // AI traffic on the island's roads (it waits for World's RoadNetwork to be loaded)
            TrafficSystem.Ensure(player, player.transform.parent);
            // and people on its sidewalks (World W4's walkable network)
            CTLife.World.Pedestrians.PedestrianSystem.Ensure(player, player.transform.parent);
            return s;
        }

        private readonly ParkedCars _parked = new ParkedCars();
        public ParkedCars Parked => _parked;
        private float _cleanAt;

        private void OnDestroy()
        {
            _parked.Clear(this);
            if (Current == this) Current = null;
        }

        private IEnumerator Start()
        {
            // Let the island settle under the arrival, then park the fleet around it.
            for (int i = 0; i < 20; i++) yield return null;
            if (_player != null) VehicleSpawner.SpawnShowcase(this, _player.transform.position, _player.transform.forward);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            // dev builds: `vehicleDemo=1` drives the fleet on its own for Seeker clips and frame-rate numbers
            if (LaunchArgs.Get("vehicleDemo") is string demo && demo.Length > 0 && _player != null) StartCoroutine(VehicleDemo.Run(this, _player, demo));
#endif
        }

        public Vehicle Spawn(string specId, Vector3 position, Quaternion rotation)
        {
            var prefab = Resources.Load<GameObject>("Vehicles/" + specId);
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
            if (Driving != null)
            {
                if (Time.time >= _prefetchAt && Driving.Body != null)
                {
                    _prefetchAt = Time.time + 1f;
                    VehicleWorld.Prefetch(Driving.transform.position, Driving.Body.linearVelocity, Driving.Kind == VehicleKind.Car ? 4f : 6f);
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
                if (v.Occupied || v == _drone || (v.transform.position - me).sqrMagnitude < 320f * 320f) continue;
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
                if (v == null || v.Occupied || v.Kind == VehicleKind.Drone || v.Speed > 1f) continue;
                var d = Vector3.Distance(me, v.EntryPoint);
                float near = Mathf.Min(d, Vector3.Distance(me, v.transform.position) - 1.2f);
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
            if (v == null || Busy || Driving != null || v.Occupied || _player == null) return false;
            StartCoroutine(EnterRoutine(v));
            return true;
        }

        public bool Exit()
        {
            if (Driving == null || Busy) return false;
            // in the air (the helicopter, the Roadstar in flight): land first
            if ((Driving.Kind == VehicleKind.Helicopter || Driving.Flying) && Driving.GetComponent<FlightPhysics>() is FlightPhysics f && (f.Altitude > 1.5f || f.OverWater))
            { Bus.Publish(new InteractionToastSignal { key = f.OverWater ? "vehicle.noWaterLanding" : "vehicle.landFirst" }); return false; }
            StartCoroutine(ExitRoutine());
            return true;
        }

        private IEnumerator EnterRoutine(Vehicle v)
        {
            Busy = true;
            bool hasDoor = v.DoorLeft != null;
            // 1. walk to the driver's door
            var entry = v.EntryPoint;
            if (Vector3.Distance(_player.transform.position, entry) > 0.5f && _player.MoveTo(entry))
            {
                float t0 = Time.time;
                while (_player.HasDestination && Time.time - t0 < 4f && v != null && !v.Occupied) yield return null;
            }
            if (v == null || v.Occupied) { Busy = false; yield break; }
            // 2. face the seat, open the door
            _player.EnterVehicleMode();
            _avatarParent = _player.transform.parent;
            _view.SetSpeed(0f);
            if (hasDoor) { v.SetDoor(true); AudioFor(v)?.Door(true); yield return new WaitForSeconds(0.32f); }
            // 3. sit in: the seated pose plays, the hips settle onto the seat. The avatar's own colliders (tap capsule, body
            // capsule) go quiet: parented under the car they would join its body or shove it from inside.
            MuteColliders(true);
            _player.transform.SetParent(v.transform, true);
            _view.SetBase("act_drive");
            var from = _player.transform.position; var fromRot = _player.transform.rotation;
            _seatOffset = MeasureSeatOffset();
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.5f)
            {
                if (v == null) break;
                _seatOffset = Vector3.Lerp(_seatOffset, MeasureSeatOffset(), 0.25f);
                float e = t * t * (3f - 2f * t);
                var to = v.Seat != null ? v.Seat.position - v.transform.rotation * _seatOffset : v.transform.position;
                _player.transform.SetPositionAndRotation(Vector3.Lerp(from, to, e), Quaternion.Slerp(fromRot, v.transform.rotation, e));
                yield return null;
            }
            if (v == null)
            {
                // the car went away mid-entry: back on foot where we are
                _player.transform.SetParent(_avatarParent, true);
                MuteColliders(false);
                _player.ExitVehicleMode(_player.transform.position, _player.transform.eulerAngles.y);
                Busy = false; yield break;
            }
            // 4. shut the door, start up, take control
            v.Driver = _view;
            v.EngineOn = true;
            if (v.Body != null) { v.Body.isKinematic = false; v.Body.WakeUp(); }
            if (hasDoor) { v.SetDoor(false); yield return new WaitForSeconds(0.25f); AudioFor(v)?.Door(false); }
            AudioFor(v)?.StartEngine();
            Driving = v;
            _lastSafe = v.transform.position; _lastSafeRot = v.transform.rotation; _safeAt = Time.time;
            CameraRig.Instance?.FollowVehicle(v);
            Busy = false;
            DrivingChanged?.Invoke(v);
            Bus.Publish(new VehicleChangedSignal { vehicle = v });
        }

        /// <summary>Hips position in the avatar root's frame for the current pose (the seat is where the hips go).</summary>
        private Vector3 MeasureSeatOffset()
        {
            var a = _view != null ? _view.Animator : null;
            var hips = a != null && a.isHuman ? a.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips == null) return new Vector3(0f, 0.5f, 0.05f);
            var t = _player.transform;
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
            v.Input = new VehicleInput { brake = 1f, handbrake = true };
            if (v.GetComponent<FlightPhysics>() is FlightPhysics f && v.Kind == VehicleKind.Roadstar) f.SetFlight(false);
            bool hasDoor = v.DoorLeft != null;
            if (hasDoor) { v.SetDoor(true); AudioFor(v)?.Door(true); yield return new WaitForSeconds(0.3f); }
            // out beside the door, facing away from the car
            var exit = v.EntryPoint;
            if (!FootPlanner.Free(exit)) exit = v.transform.position + v.transform.right * -(v.Kind == VehicleKind.Helicopter ? 2.6f : 1.8f);
            float yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(exit - v.transform.position, Vector3.up).normalized).eulerAngles.y;
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
            if (hasDoor) { yield return new WaitForSeconds(0.35f); if (v != null) { v.SetDoor(false); AudioFor(v)?.Door(false); } }
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

        // ------------------------------------------------------------------ the pocket drone

        /// <summary>Launch (or re-launch) the camera drone from the player's hands; the player stands holding the controller.</summary>
        public bool DeployDrone()
        {
            if (Busy || Driving != null || _player == null || _player.InVehicle) return false;
            var t = _player.transform;
            var at = t.position + t.forward * 0.9f + Vector3.up * 1.1f;
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
            Exit();
        }

        // ------------------------------------------------------------------ recovery

        private void RecoverFromWater()
        {
            var v = Driving;
            if (v == null || v.Body == null || v.Kind == VehicleKind.Drone) return;
            var p = v.transform.position;
            bool grounded = v.GetComponent<CarPhysics>() is CarPhysics cp ? cp.Grounded : v.GetComponent<FlightPhysics>()?.Grounded == true;
            if (grounded && v.transform.up.y > 0.8f && Time.time - _safeAt > 2f) { _lastSafe = p; _lastSafeRot = v.transform.rotation; _safeAt = Time.time; }
            float sea = VehicleWorld.SeaLevel;
            bool sunk = (!float.IsInfinity(sea) && p.y < sea - 0.6f && !v.Flying && v.Kind != VehicleKind.Helicopter) || p.y < -200f;
            if (!sunk) return;
            v.Body.linearVelocity = Vector3.zero; v.Body.angularVelocity = Vector3.zero;
            v.Body.position = _lastSafe + Vector3.up * 0.6f; v.Body.rotation = _lastSafeRot;
            Bus.Publish(new InteractionToastSignal { key = "vehicle.recovered" });
        }
    }
}
