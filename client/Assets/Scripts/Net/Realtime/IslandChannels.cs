using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Colyseus.Schema;
using CTLife.Core;
using CTLife.Realtime;

namespace CTLife.Net
{
    /// <summary>Additive island capability; older test doubles for IRealtimeService remain valid.</summary>
    public interface IIslandRealtimeService
    {
        StreetChannel Street { get; }
        VehicleChannel Vehicles { get; }
        Task<StreetChannel> JoinStreet(string district, CancellationToken owner);
        Task<VehicleChannel> JoinVehicles(CancellationToken owner);
        Task LeaveStreet();
        Task LeaveVehicles();
    }

    public sealed class StreetChannel : RoomChannel<DynamicSchema>
    {
        public StreetChannel(string district) : base("street") { District = district; }
        public string District { get; }
        private DynamicMapMirror<PlayerState> _players;
        public IReadOnlyDictionary<string, PlayerState> Players => _players?.Items ?? Empty<PlayerState>.Map;
        public event Action<string, PlayerState> PlayerAdded, PlayerChanged, PlayerRemoved;
        public event Action<CorrectedMessage> Corrected;
        public event Action<SaidMessage> Said;
        private float _moveAt = -1f, _emoteAt = -10f, _sayAt = -10f;
        protected override void OnJoined()
        {
            On<CorrectedMessage>("corrected", m => Corrected?.Invoke(m));
            On<SaidMessage>("said", m => Said?.Invoke(m));
            // backend #17: a pursuit of a player in this street room (bystanders see the same cruisers); HeatService dedupes
            On<Dictionary<string, object>>("police_pursuit", m => RawRoomMessages.Publish("police_pursuit", m));
            On<Dictionary<string, object>>("police_pursuit_end", m => RawRoomMessages.Publish("police_pursuit_end", m));
            _players = new DynamicMapMirror<PlayerState>();
            _players.Bind(TrackAndReturn, Callbacks.Get(Room), "players", () => new PlayerState(), StateMapper.ApplyPlayer,
                (k, p) => PlayerAdded?.Invoke(k, p), (k, p) => PlayerChanged?.Invoke(k, p), (k, p) => PlayerRemoved?.Invoke(k, p));
        }
        public void Move(float x, float z, float yaw)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!IsJoined || now - _moveAt < 0.1f) return;
            _moveAt = now; _ = Send("move", new MoveMessage { x = x, z = z, rotY = yaw });
        }
        public bool Emote(string id)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!IsJoined || now - _emoteAt < 1f) return false;
            _emoteAt = now; _ = Send("emote", new EmoteMessage { emoteId = id }); return true;
        }
        public float Say(string text)
        {
            if (!IsJoined || string.IsNullOrWhiteSpace(text)) return 0;
            float now = UnityEngine.Time.realtimeSinceStartup, wait = 2f - (now - _sayAt);
            if (wait > 0) return wait;
            _sayAt = now; _ = Send("say", new SayMessage { text = text.Length > 140 ? text.Substring(0, 140) : text }); return 0;
        }
    }

    /// <summary>Driver ownership is acknowledged by state.driver, never inferred from Send completing.</summary>
    public sealed class VehicleChannel : RoomChannel<DynamicSchema>
    {
        private readonly string _user;
        private DynamicMapMirror<VehicleState> _vehicles;
        private readonly HashSet<string> _abandonedClaims = new HashSet<string>();
        private float _moveAt = -1f;
        private bool _dropped;
        public bool CanDrive => IsJoined && !_dropped;
        public VehicleChannel(string user) : base("vehicles") { _user = user; }
        public IReadOnlyDictionary<string, VehicleState> Vehicles => _vehicles?.Items ?? Empty<VehicleState>.Map;
        public event Action<string, VehicleState> VehicleAdded, VehicleChanged, VehicleRemoved;
        public event Action<VehicleCorrectedMessage> Corrected;
        public event Action<VehicleHitRelayMessage> Hit;
        protected override void OnJoined()
        {
            _dropped = false;
            void Drop() => _dropped = true;
            void Reconnect() => _dropped = false;
            Dropped += Drop; Reconnected += Reconnect;
            Track(() => { Dropped -= Drop; Reconnected -= Reconnect; });
            On<VehicleCorrectedMessage>("vehicle_corrected", m => Corrected?.Invoke(m));
            On<VehicleHitRelayMessage>("vehicle_hit", m => Hit?.Invoke(m));
            // backend #17: your parked vehicle was stolen / came back; a thief's car went back (HeatService)
            On<Dictionary<string, object>>("vehicle_stolen", m => RawRoomMessages.Publish("vehicle_stolen", m));
            On<Dictionary<string, object>>("vehicle_returned", m => RawRoomMessages.Publish("vehicle_returned", m));
            _vehicles = new DynamicMapMirror<VehicleState>();
            _vehicles.Bind(TrackAndReturn, Callbacks.Get(Room), "vehicles", () => new VehicleState(), StateMapper.ApplyVehicle,
                (k, v) => { ReleaseAbandoned(v); VehicleAdded?.Invoke(k, v); },
                (k, v) => { ReleaseAbandoned(v); VehicleChanged?.Invoke(k, v); },
                (k, v) => VehicleRemoved?.Invoke(k, v));
        }
        private void ReleaseAbandoned(VehicleState v)
        {
            if (v.driver == _user && _abandonedClaims.Contains(v.id)) Exit(v.id, v.x, v.z, v.yaw);
        }
        /// <summary>The code of the last refused vehicle_enter (STEAL_REQUIRED, VEHICLE_TAKEN, JAILED, ...), null after an accept.</summary>
        public string LastEnterRejection { get; private set; }

        public async Task<bool> Enter(VehicleEnterMessage request, CancellationToken owner, bool steal = false)
        {
            if (!CanDrive || request == null || string.IsNullOrEmpty(request.id)) return false;
            bool rejected = false, accepted = false;
            LastEnterRejection = null;
            void OnRejected(RejectedMessage r)
            { if (r.type == "vehicle_enter" && (string.IsNullOrEmpty(r.id) || r.id == request.id)) { rejected = true; LastEnterRejection = r.code; } }
            _abandonedClaims.Remove(request.id);
            Rejected += OnRejected;
            try
            {
                owner.ThrowIfCancellationRequested();
                // backend #17: `steal` only when taking a car that is not ours (an optional field: never sent as null)
                if (steal)
                {
                    var m = new Dictionary<string, object> { ["id"] = request.id, ["x"] = request.x, ["y"] = request.y, ["z"] = request.z, ["yaw"] = request.yaw, ["steal"] = true };
                    if (!string.IsNullOrEmpty(request.kind)) m["kind"] = request.kind;
                    await Send("vehicle_enter", m);
                }
                else await Send("vehicle_enter", request);
                double until = UnityEngine.Time.realtimeSinceStartupAsDouble + 8;
                while (CanDrive && !rejected && UnityEngine.Time.realtimeSinceStartupAsDouble < until)
                {
                    owner.ThrowIfCancellationRequested();
                    if (Vehicles.TryGetValue(request.id, out var v) && v.driver == _user)
                    { accepted = true; return true; }
                    await Task.Delay(50, owner);
                }
                return false;
            }
            finally
            {
                Rejected -= OnRejected;
                if (!accepted)
                {
                    // Enter validates presence asynchronously on the server. Its late state must release even
                    // when an earlier exit arrived before the pending server enter completed.
                    _abandonedClaims.Add(request.id);
                    if (Vehicles.TryGetValue(request.id, out var v)) ReleaseAbandoned(v);
                }
            }
        }
        public void Move(VehicleMoveMessage pose, bool force = false)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!CanDrive || (!force && now - _moveAt < 0.1f)) return;
            if (!Vehicles.TryGetValue(pose.id, out var v) || v.driver != _user) return;
            _moveAt = now; _ = Send("vehicle_move", pose);
        }
        public void Exit(string id, double x, double z, double yaw) =>
            _ = Send("vehicle_exit", new VehicleExitMessage { id = id, x = x, z = z, yaw = yaw });
    }
}
