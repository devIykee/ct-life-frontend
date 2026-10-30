using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>Stable public parking identity, retained when the car moves away from its original bay.</summary>
    public sealed class VehicleNetworkIdentity : MonoBehaviour
    {
        public string Id;
        public string Kind;
        /// <summary>Backend #17: the next entry takes the car (`steal: true`): a hijacked traffic car, or the player said so.</summary>
        public bool Steal;
    }

    /// <summary>Island vehicle ownership is granted by the server before the local entry animation.</summary>
    public sealed class VehicleNetworkBridge : MonoBehaviour
    {
        public static VehicleNetworkBridge Current { get; private set; }
        public static bool HasClaim => Current != null && !string.IsNullOrEmpty(Current._claimId);
        public static bool IsRegisteredBay(string id) => Current != null && Current._known.Contains(id);
        /// <summary>Backend #17: entering another player's parked vehicle answered STEAL_REQUIRED. Feature code asks the
        /// player; on yes it arms the identity's Steal and enters again.</summary>
        public static Action<Vehicle, string> StealRequired;
        /// <summary>Any other refused entry (VEHICLE_TAKEN, VEHICLE_NOT_STEALABLE, JAILED, RATE_LIMITED, ...): code, vehicle.</summary>
        public static Action<string, Vehicle> EnterRefused;
        public static bool IsDriving(string user)
        {
            if (string.IsNullOrEmpty(user) || Current?._channel == null) return false;
            foreach (var v in Current._channel.Vehicles.Values) if (v.driver == user) return true;
            return false;
        }
        private VenueSession _visit;
        private IIslandRealtimeService _rt;
        private VehicleChannel _channel;
        private string _user, _claimId;
        private Vehicle _claim;
        private bool _joining;
        private float _retryAt;
        private readonly Dictionary<string, Vehicle> _actors = new Dictionary<string, Vehicle>();
        private readonly HashSet<string> _known = new HashSet<string>();
        private Func<Vehicle, CancellationToken, Task<bool>> _gate;
        public void Init(VenueSession visit, IIslandRealtimeService realtime)
        {
            if (RuntimeMode.IsOfflineIsland || !visit.IsOnline) { Destroy(this); return; }
            _visit = visit; _rt = realtime; _user = Services.Get<Session>().UserId; Current = this;
            _gate = Authorize; VehicleSystem.AuthorizeEnter = _gate;
            VehicleSystem.DrivingChanged += DrivingChanged; VehicleSystem.EntryAborted += EntryAborted;
        }
        private async Task Join()
        {
            _joining = true; _retryAt = Time.unscaledTime + 2;
            try
            {
                var next = await _rt.JoinVehicles(_visit.LifetimeToken);
                if (this == null || _visit == null || _visit.LifetimeToken.IsCancellationRequested) return;
                if (!ReferenceEquals(next, _channel))
                {
                    // A replacement room starts with a new filtered map; the old mirror's Clear has no remove events.
                    foreach (var id in new List<string>(_actors.Keys))
                    {
                        var actor = _actors[id];
                        if (actor != null && (actor == _claim || actor == VehicleSystem.Current?.Driving)) continue;
                        if (actor != null) { VehicleSystem.Current?.Vehicles.Remove(actor); Destroy(actor.gameObject); }
                        _actors.Remove(id);
                    }
                }
                Detach(); _channel = next;
                next.VehicleAdded += Changed; next.VehicleChanged += Changed; next.VehicleRemoved += Removed;
                next.Corrected += Corrected; next.Hit += Hit;
                foreach (var p in next.Vehicles) Changed(p.Key, p.Value);
                Log.Info("vehicles", "Joined visible=" + next.Vehicles.Count);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("vehicles", "Join failed: " + e.GetType().Name); }
            finally { _joining = false; }
        }
        /// <summary>The same accepted vehicle is still owned on the live channel and visit.</summary>
        public bool HasEntryAuthority(Vehicle vehicle) => vehicle != null && vehicle == _claim &&
            _visit != null && _visit.IsActive && !RuntimeMode.IsOfflineIsland && _channel != null && _channel.CanDrive &&
            !string.IsNullOrEmpty(_claimId) && _channel.Vehicles.TryGetValue(_claimId, out var own) && own.driver == _user;

        private void Update()
        {
            var system = VehicleSystem.Current;
            var claim = _claim;
            if (claim != null && !HasEntryAuthority(claim))
            {
                // Loss applies from acknowledgment onward, including door/seat yields before Driving is published.
                // Cancel immediately so a reconnect during the animation cannot revive the old entry.
                SetLocal(claim, false);
                if (system?.Driving == claim) system.RecoverLostNetworkLease();
                else system?.CancelPendingEntry(claim);
            }
            if (_visit == null || !_visit.IsActive || RuntimeMode.IsOfflineIsland) return;
            if ((_channel == null || !_channel.IsJoined) && !_joining && Time.unscaledTime >= _retryAt) _ = Join();
            if (_claim != null && system?.Driving == _claim && HasEntryAuthority(_claim))
            {
                // A short reconnect can restore the same lease while safe-floor recovery was unavailable.
                // Resume the driver only after any entry/recovery transition has finished.
                if (ShouldRestoreSimulation(true, true, _claim.Body != null && _claim.Body.isKinematic,
                    system.Busy, _visit.LocalPlayer == null || _visit.LocalPlayer.Recovering)) SetLocal(_claim, true);
                _channel.Move(Pose(_claim, _claimId));
            }
        }
        public static bool ShouldRestoreSimulation(bool connected, bool ownsLease, bool bodyKinematic, bool busy, bool recovering) =>
            connected && ownsLease && bodyKinematic && !busy && !recovering;

        private void FixedUpdate()
        {
            if (_channel == null || !_channel.IsJoined) return;
            var driving = VehicleSystem.Current?.Driving;
            foreach (var entry in _channel.Vehicles)
            {
                if (!_actors.TryGetValue(entry.Key, out var actor) || actor == null) { Changed(entry.Key, entry.Value); continue; }
                if (actor == _claim || actor == driving) continue;
                var state = entry.Value;
                var position = Vector3.Lerp(actor.transform.position, Position(state), 1 - Mathf.Exp(-14 * Time.fixedDeltaTime));
                var rotation = Quaternion.Slerp(actor.transform.rotation, Rotation(state), 1 - Mathf.Exp(-14 * Time.fixedDeltaTime));
                if (actor.Body != null) { actor.Body.MovePosition(position); actor.Body.MoveRotation(rotation); }
                else actor.transform.SetPositionAndRotation(position, rotation);
            }
        }
        private async Task<bool> Authorize(Vehicle vehicle, CancellationToken cancellation)
        {
            if (vehicle == null || _channel == null || !_channel.CanDrive || RuntimeMode.IsOfflineIsland) return false;
            string kind = IslandProtocol.WireKind(vehicle.specId);
            if (!IslandProtocol.IsPublicVehicle(kind)) return false;
            var identity = vehicle.GetComponent<VehicleNetworkIdentity>() ?? vehicle.gameObject.AddComponent<VehicleNetworkIdentity>();
            if (string.IsNullOrEmpty(identity.Id)) identity.Id = IslandProtocol.ParkedId(kind, vehicle.transform.position.x, vehicle.transform.position.z);
            identity.Kind = kind;
            var channel = _channel;
            using (var owner = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _visit.LifetimeToken))
            {
                var p = vehicle.transform.position;
                var request = new VehicleEnterMessage { id = identity.Id, kind = kind, x = p.x, y = p.y, z = p.z, yaw = vehicle.transform.eulerAngles.y * Mathf.Deg2Rad };
                bool accepted = await channel.Enter(request, owner.Token, identity.Steal);
                // The server checks entry against the street position it last stored (written every 250 ms). Right after a
                // join, a district change or a sprint up to the car that position can be missing or behind: let the
                // street room catch up once and try again.
                if (!accepted && (channel.LastEnterRejection == "POSITION_UNKNOWN" || channel.LastEnterRejection == "VEHICLE_TOO_FAR") &&
                    !owner.IsCancellationRequested && channel == _channel && channel.CanDrive)
                {
                    await Task.Delay(600, owner.Token);
                    accepted = await channel.Enter(request, owner.Token, identity.Steal);
                }
                if (!accepted)
                {
                    var code = channel.LastEnterRejection;
                    if (!string.IsNullOrEmpty(code))
                    {
                        Log.Info("vehicles", "Entry refused: " + code);
                        VehicleSystem.SuppressEntryToast = true;   // the feature's own copy says why
                        if (code == "STEAL_REQUIRED" && !identity.Steal && StealRequired != null) StealRequired(vehicle, kind);
                        else EnterRefused?.Invoke(code, vehicle);
                    }
                    return false;
                }
                if (owner.IsCancellationRequested || this == null || vehicle == null || channel != _channel || !channel.CanDrive ||
                    _visit == null || !_visit.IsActive || !channel.Vehicles.TryGetValue(identity.Id, out var acceptedState) || acceptedState.driver != _user)
                { channel.Exit(identity.Id, p.x, p.z, vehicle != null ? vehicle.transform.eulerAngles.y * Mathf.Deg2Rad : 0); return false; }
                _claim = vehicle; _claimId = identity.Id; _actors[_claimId] = vehicle; _known.Add(_claimId);
                vehicle.RemoteOccupied = false; SetLocal(vehicle, false); // Keep the car frozen until physical entry commits.
                Log.Info("vehicles", "Entry acknowledged kind=" + kind);
                return true;
            }
        }
        /// <summary>Release a scheduled online safety rescue without relocating the server-owned vehicle.</summary>
        public void ReleaseForSafety(Vehicle vehicle)
        {
            if (vehicle != null && vehicle == _claim) Release();
        }

        private void EntryAborted(Vehicle vehicle)
        {
            // An old canceled iterator may finish after another entry. It owns only its captured claim.
            if (ReferenceEquals(vehicle, _claim)) Release();
        }
        private void DrivingChanged(Vehicle driving)
        {
            if (driving != null)
            {
                if (driving == _claim) SetLocal(driving, HasEntryAuthority(driving));
                return;
            }
            Release();
            // Safety can release a claim before the seated person has detached. Dispose obsolete cars only afterward.
            foreach (var id in new List<string>(_actors.Keys))
            {
                if (_channel != null && _channel.Vehicles.ContainsKey(id)) continue;
                var actor = _actors[id]; _actors.Remove(id);
                if (actor != null) { VehicleSystem.Current?.Vehicles.Remove(actor); Destroy(actor.gameObject); }
            }
        }
        private void Release()
        {
            if (string.IsNullOrEmpty(_claimId)) return;
            if (_claim != null)
            {
                var p = _claim.transform.position;
                if (_channel != null && _channel.Vehicles.TryGetValue(_claimId, out var owned) && owned.driver == _user)
                {
                    _channel.Move(Pose(_claim, _claimId), true);
                    _channel.Exit(_claimId, p.x, p.z, _claim.transform.eulerAngles.y * Mathf.Deg2Rad);
                }
                SetLocal(_claim, false);
            }
            else if (_channel != null && _channel.Vehicles.TryGetValue(_claimId, out var state) && state.driver == _user) _channel.Exit(_claimId, state.x, state.z, state.yaw);
            if (_claim != null && _claim != VehicleSystem.Current?.Driving && (_channel == null || !_channel.Vehicles.ContainsKey(_claimId)))
            {
                VehicleSystem.Current?.Vehicles.Remove(_claim);
                _actors.Remove(_claimId); Destroy(_claim.gameObject);
            }
            _claim = null; _claimId = null;
            Log.Info("vehicles", "Released driver claim");
        }
        private void Changed(string id, VehicleState state)
        {
            _known.Add(id);
            if (!IslandProtocol.IsPublicVehicle(state.kind) || VehicleSystem.Current == null) return;
            if (!_actors.TryGetValue(id, out var actor) || actor == null)
            {
                foreach (var v in VehicleSystem.Current.Vehicles)
                    if (v != null && v.GetComponent<VehicleNetworkIdentity>()?.Id == id) { actor = v; break; }
                if (actor == null) actor = VehicleSystem.Current.Spawn(IslandProtocol.SpecKind(state.kind), Position(state), Rotation(state));
                if (actor == null) return;
                var identity = actor.GetComponent<VehicleNetworkIdentity>() ?? actor.gameObject.AddComponent<VehicleNetworkIdentity>();
                identity.Id = id; identity.Kind = state.kind; _actors[id] = actor;
            }
            actor.RemoteOccupied = !string.IsNullOrEmpty(state.driver) && state.driver != _user;
            actor.EngineOn = !string.IsNullOrEmpty(state.driver);
            if (actor != _claim && actor != VehicleSystem.Current.Driving) SetLocal(actor, false);
        }
        private void Removed(string id, VehicleState state)
        {
            if (_actors.TryGetValue(id, out var actor) && actor != null && actor != _claim && actor != VehicleSystem.Current?.Driving)
            { VehicleSystem.Current?.Vehicles.Remove(actor); Destroy(actor.gameObject); }
            _actors.Remove(id);
        }
        private void Corrected(VehicleCorrectedMessage state)
        {
            if (_claim == null || state.id != _claimId) return;
            var p = new Vector3((float)state.x, (float)state.y, (float)state.z);
            _claim.transform.position = p;
            if (_claim.Body != null) { _claim.Body.position = p; _claim.Body.linearVelocity = Vector3.zero; _claim.Body.angularVelocity = Vector3.zero; }
        }
        private void Hit(VehicleHitRelayMessage hit)
        {
            if (hit.victim != _user || _visit?.LocalPlayer == null || hit.velocity == null || HasClaim || VehicleSystem.Current?.Driving != null) return;
            var velocity = new Vector3((float)hit.velocity.x, (float)hit.velocity.y, (float)hit.velocity.z);
            var view = _visit.LocalPlayer.GetComponent<AvatarView>();
            if (view != null) Knockdown.For(view).Fall(velocity, velocity * 8);
        }
        private static Vector3 Position(VehicleState v) => new Vector3((float)v.x, (float)v.y, (float)v.z);
        private static Quaternion Rotation(VehicleState v) => Quaternion.Euler((float)v.pitch * Mathf.Rad2Deg, (float)v.yaw * Mathf.Rad2Deg, (float)v.roll * Mathf.Rad2Deg);
        private static VehicleMoveMessage Pose(Vehicle v, string id)
        {
            var p = v.transform.position; var r = v.transform.eulerAngles;
            return new VehicleMoveMessage { id = id, x = p.x, y = p.y, z = p.z, yaw = r.y * Mathf.Deg2Rad, pitch = Mathf.DeltaAngle(0, r.x) * Mathf.Deg2Rad, roll = Mathf.DeltaAngle(0, r.z) * Mathf.Deg2Rad, speed = v.Speed, flags = 0 };
        }
        private static void SetLocal(Vehicle v, bool local)
        {
            if (v == null) return;
            if (v.TryGetComponent<CarPhysics>(out var car)) car.enabled = local;
            if (v.TryGetComponent<FlightPhysics>(out var flight)) flight.enabled = local;
            if (v.Body == null) return;
            if (!local && !v.Body.isKinematic) { v.Body.linearVelocity = Vector3.zero; v.Body.angularVelocity = Vector3.zero; }
            if (local)
            {
                v.Body.isKinematic = false;
                v.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
            else
            {
                v.Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                v.Body.isKinematic = true;
                v.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
        }
        private void Detach()
        {
            if (_channel != null)
            { _channel.VehicleAdded -= Changed; _channel.VehicleChanged -= Changed; _channel.VehicleRemoved -= Removed; _channel.Corrected -= Corrected; _channel.Hit -= Hit; }
            _channel = null;
        }
        private void OnDestroy()
        {
            // Detach an in-progress passenger before Release can dispose its obsolete car actor.
            VehicleSystem.Current?.CancelPendingEntry(_claim);
            Release(); Detach();
            if (VehicleSystem.AuthorizeEnter == _gate) VehicleSystem.AuthorizeEnter = null;
            VehicleSystem.DrivingChanged -= DrivingChanged; VehicleSystem.EntryAborted -= EntryAborted;
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
