using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.World.Vehicles;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>Island-only room lifecycle. Never installed for the offline demo or offline island mode.</summary>
    public sealed class StreetNetworkBridge : MonoBehaviour
    {
        public static StreetNetworkBridge Current { get; private set; }
        private VenueSession _visit;
        private IIslandRealtimeService _rt;
        private StreetChannel _channel;
        private bool _joining, _resetting;
        private float _retryAt;
        private readonly Dictionary<string, StreetRemoteAvatar> _actors = new Dictionary<string, StreetRemoteAvatar>();
        public StreetChannel Channel => _channel;

        public void Init(VenueSession visit)
        {
            if (RuntimeMode.IsOfflineIsland || !visit.IsOnline || !(Services.Get<IRealtimeService>() is IIslandRealtimeService rt))
            { Destroy(this); return; }
            _visit = visit; _rt = rt; Current = this;
            gameObject.AddComponent<VehicleNetworkBridge>().Init(visit, rt);
        }

        private void Update()
        {
            if (_visit == null || !_visit.IsActive || _visit.LocalPlayer == null || _visit.LocalPlayer.Recovering || _resetting || RuntimeMode.IsOfflineIsland) return;
            var world = IslandWorld.Current;
            if (world == null) return;
            var p = _visit.LocalPlayer.transform.position;
            string district = world.DistrictAt(p);
            if (!_joining && Time.unscaledTime >= _retryAt &&
                (_channel == null || !_channel.IsJoined || _channel.District != district)) _ = Join(district);
            if (_channel != null && _channel.IsJoined && VehicleSystem.Current?.Driving == null && !VehicleNetworkBridge.HasClaim)
                _channel.Move(p.x, p.z, _visit.LocalPlayer.transform.eulerAngles.y * Mathf.Deg2Rad);
            // A venue and street can overlap: show one avatar per identity. Driving is shown by the vehicle map.
            foreach (var actor in _actors.Values)
            {
                if (actor == null) continue;
                bool inVenue = false;
                foreach (var remote in _visit.Players.Values)
                    if (remote != null && remote.State?.userId == actor.UserId) { inVenue = true; break; }
                actor.gameObject.SetActive(!inVenue && !VehicleNetworkBridge.IsDriving(actor.UserId));
            }
            foreach (var remote in _visit.Players.Values)
                if (remote != null) remote.gameObject.SetActive(!VehicleNetworkBridge.IsDriving(remote.State?.userId));
        }

        private async Task Join(string district)
        {
            _joining = true; _retryAt = Time.unscaledTime + 2f;
            try
            {
                if (!IslandProtocol.IsDistrict(district))
                {
                    // Countryside outside the published district polygons has no invented room or fallback bounds.
                    if (_channel != null) { Detach(); await _rt.LeaveStreet(); }
                    return;
                }
                var next = await _rt.JoinStreet(district, _visit.LifetimeToken);
                if (this == null || _visit == null || _visit.LifetimeToken.IsCancellationRequested) return;
                Detach(); _channel = next;
                next.PlayerAdded += Add; next.PlayerChanged += Change; next.PlayerRemoved += Remove;
                next.Corrected += Corrected; next.Said += Said;
                foreach (var kv in next.Players) Add(kv.Key, kv.Value);
                Log.Info("street", "Joined district=" + district + " players=" + next.Players.Count);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("street", "Join failed: " + e.GetType().Name); }
            finally { _joining = false; }
        }

        public async Task ResetAfterTravel()
        {
            _resetting = true;
            try
            {
                Detach();
                if (_rt != null) await _rt.LeaveStreet();
            }
            finally { _retryAt = Time.unscaledTime + 0.2f; _resetting = false; }
        }

        /// <summary>Nearby rescue spends the normal walking budget; it is not a teleport authorization.</summary>
        public async Task ResetForNearbyRecovery(Vector3 destination)
        {
            await ResetAfterTravel();
            _resetting = true;
            try
            {
                string district = IslandWorld.Current?.DistrictAt(destination);
                if (!IslandProtocol.IsDistrict(district)) throw new InvalidOperationException("No street room covers the recovery destination.");
                await Join(district);
                if (_channel == null || !_channel.IsJoined) throw new InvalidOperationException("Recovery street presence is unavailable.");
                await Task.Delay(1000, _visit.LifetimeToken);
            }
            finally { _resetting = false; }
        }

        private void Add(string key, PlayerState p)
        {
            if (_visit == null || p.userId == Services.Get<Session>().UserId || _actors.ContainsKey(key)) return;
            string[] look = new string[7];
            for (int i = 0; p.look != null && i < p.look.Count && i < 7; i++) look[i] = p.look[i];
            var view = AvatarFactory.Create(string.IsNullOrEmpty(p.name) ? "player" : p.name, look, false, null, transform);
            view.userId = p.userId; view.sessionId = key;
            var actor = view.gameObject.AddComponent<StreetRemoteAvatar>(); actor.Apply(p, true); _actors[key] = actor;
        }
        private void Change(string key, PlayerState p) { if (_actors.TryGetValue(key, out var a) && a != null) a.Apply(p); else Add(key, p); }
        private void Remove(string key, PlayerState p) { if (_actors.TryGetValue(key, out var a) && a != null) Destroy(a.gameObject); _actors.Remove(key); }
        private void Corrected(CorrectedMessage m)
        {
            if (_visit?.LocalPlayer == null || _visit.LocalPlayer.Recovering || _resetting || VehicleSystem.Current?.Driving != null || VehicleNetworkBridge.HasClaim) return;
            // The venue owns its correction while inside; street coordinates always remain island metres.
            if (_visit.Channel != null && _visit.Venue != null && _visit.Venue.IsWalkable(_visit.LocalPlayer.transform.position)) return;
            var p = new Vector3((float)m.x, 0, (float)m.z); p.y = IslandStreamer.GroundHeight(p.x, p.z);
            _visit.LocalPlayer.SnapTo(p, _visit.LocalPlayer.transform.eulerAngles.y * Mathf.Deg2Rad);
        }
        private void Said(SaidMessage m)
        {
            if (_actors.TryGetValue(m.sessionId ?? "", out var a) && a != null)
                Bus.Publish(new BubbleSignal { anchor = a.GetComponent<AvatarView>().HeadAnchor, text = m.text, seconds = 4.5f });
        }
        private void Detach()
        {
            if (_channel != null)
            { _channel.PlayerAdded -= Add; _channel.PlayerChanged -= Change; _channel.PlayerRemoved -= Remove; _channel.Corrected -= Corrected; _channel.Said -= Said; }
            _channel = null;
            foreach (var a in _actors.Values) if (a != null) Destroy(a.gameObject);
            _actors.Clear();
        }
        private void OnDestroy()
        {
            Detach();
            if (ReferenceEquals(Current, this)) Current = null;
            // Joined slots are leased to this visit token; cancellation drains only this visit's handles.
        }
    }

    /// <summary>Street poses use island height, not a venue-local y=0 plane.</summary>
    public sealed class StreetRemoteAvatar : MonoBehaviour
    {
        private AvatarView _view;
        private Vector3 _target;
        private float _yaw;
        private string _emote;
        public string UserId { get; private set; }
        public void Apply(PlayerState p, bool initial = false)
        {
            if (_view == null) { _view = GetComponent<AvatarView>(); AvatarFactory.AddBodyCollider(gameObject); CTLife.Audio.Footsteps.Attach(gameObject); }
            UserId = p.userId;
            float ground = IslandStreamer.GroundHeight((float)p.x, (float)p.z);
            if (float.IsNaN(ground) || float.IsInfinity(ground)) ground = transform.position.y;
            _target = new Vector3((float)p.x, ground, (float)p.z);
            if (CharacterBody.GroundAt(_target, out float floor)) _target.y = floor;
            _yaw = (float)p.rotY * Mathf.Rad2Deg;
            _view.displayName = p.name; _view.isSeeker = p.isSeeker; _view.minerTier = p.minerTier; _view.tribeId = p.tribeId; _view.SetTalking(p.talking);
            _view.accId = p.look != null && p.look.Count > 6 ? p.look[6] : null;
            if (initial) transform.SetPositionAndRotation(_target, Quaternion.Euler(0, _yaw, 0));
            string emote = p.emoteId + "@" + p.emoteAt;
            if (!string.IsNullOrEmpty(p.emoteId) && emote != _emote && (!initial || ServerClock.SecondsUntil(p.emoteAt) > -3))
                _view.PlayOneShot(p.emoteId, ServerClock.IsSynced ? ServerClock.SecondsUntil(p.emoteAt) : 0);
            _emote = emote;
        }
        private void Update()
        {
            var previous = transform.position;
            var p = Vector3.Lerp(previous, _target, 1f - Mathf.Exp(-12f * Time.deltaTime));
            float ground = IslandStreamer.GroundHeight(p.x, p.z);
            if (float.IsNaN(ground) || float.IsInfinity(ground)) ground = previous.y;
            // Loaded authored floors take precedence near the last pose; height is only a fallback.
            var probe = new Vector3(p.x, previous.y, p.z);
            p.y = CharacterBody.GroundAt(probe, out float floor) ? floor : ground;
            transform.SetPositionAndRotation(p, Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, _yaw, 0), 1f - Mathf.Exp(-12f * Time.deltaTime)));
            float speed = Time.deltaTime > 0 ? Vector3.Distance(p, previous) / Time.deltaTime : 0;
            _view.SetSpeed(speed); _view.SetBase(speed > 3.5f ? "loco_run" : speed > 0.15f ? "loco_walk" : "loco_idle");
        }
    }
}
