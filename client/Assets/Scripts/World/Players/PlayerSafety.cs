using System.Collections;
using CTLife.Core;
using CTLife.World.Vehicles;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>UI owns the visual overlay; zero seconds means clear immediately on cancellation/scene teardown.</summary>
    public struct PlayerRecoveryFadeSignal { public bool dim; public float seconds; }
    /// <summary>Timed hazards; transient splashes and ordinary jumps do not trigger a rescue.</summary>
    public struct PlayerSafetyState
    {
        public float WetFor, StuckFor, SteepFor;
        public bool Step(bool submerged, bool belowWorld, bool grounded, bool barelyMoved, bool steep, float dt)
        {
            WetFor = submerged ? WetFor + dt : 0f;
            StuckFor = !grounded && barelyMoved ? StuckFor + dt : 0f;
            SteepFor = steep && barelyMoved ? SteepFor + dt : 0f;
            return belowWorld || WetFor >= 1.2f || StuckFor >= 4f || SteepFor >= 3f;
        }
    }

    /// <summary>Island-only rescue to an observed, dry, walkable floor. Does not require a NavMesh on the island.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSafety : MonoBehaviour
    {
        private LocalPlayerController _player;
        private PlayerSafetyState _state;
        private Vector3 _lastSafe, _windowPosition;
        private bool _hasSafe, _recovering;
        private bool _fadeActive;
        private float _sampleAt, _windowAt, _retryAt;

        public bool CanRecoverToSafety => !_recovering && _player != null && _player.OpenWorld && !_player.InVehicle && !_player.Stunned;

        public void Init(LocalPlayerController player)
        {
            _player = player; _hasSafe = false; _state = default;
            _windowPosition = transform.position; _windowAt = Time.time;
            RememberSafe();
        }

        /// <summary>Collision-first floor check. Tunnel/bridge floors win over the terrain heightfield.</summary>
        public static bool TrySafeGround(Vector3 candidate, float maxDelta, out Vector3 point)
        {
            point = default;
            if (!VehicleWorld.IsReady(candidate)) return false;
            int mask = CharacterBody.Mask & ~((1 << CharacterLayers.Vehicle) | (1 << CharacterLayers.VehicleFlight));
            if (!Physics.Raycast(candidate + Vector3.up * 2f, Vector3.down, out var hit, 6f, mask, QueryTriggerInteraction.Ignore)) return false;
            if (hit.normal.y < 0.82f || Mathf.Abs(hit.point.y - candidate.y) > maxDelta || !VehicleWorld.Dry(hit.point)) return false;
            if (!FootPlanner.Free(hit.point + Vector3.up * 0.04f)) return false;
            point = hit.point;
            return true;
        }

        private void RememberSafe()
        {
            if (TrySafeGround(transform.position, 0.5f, out var p)) { _lastSafe = p; _hasSafe = true; }
        }

        private void LateUpdate()
        {
            if (_player == null || !_player.OpenWorld || _recovering || Time.time < _retryAt) return;
            if (_player.InVehicle || _player.Stunned || !string.IsNullOrEmpty(_player.SeatedSpot) || _player.Body == null || _player.Body.Suspended)
            { _state = default; _windowPosition = transform.position; _windowAt = Time.time; return; }
            var p = transform.position;
            float water = VehicleWorld.WaterSurfaceAt(p);
            bool wet = !float.IsNaN(water) && p.y < water - 0.5f;
            // A valid floor nearby is legal even below terrain (tunnel). Missing floor plus below heightfield is not.
            bool floor = Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out var floorHit, 2f, CharacterBody.Mask, QueryTriggerInteraction.Ignore)
                && floorHit.collider.GetComponentInParent<IslandGroundFallback>() == null;
            bool below = p.y < -200f || (!floor && VehicleWorld.TryGroundHeight(p.x, p.z, out var ground) && p.y < ground - 2f);
            bool barelyMoved = (p - _windowPosition).sqrMagnitude < 0.09f;
            if (!barelyMoved || Time.time - _windowAt > 4.5f) { _windowPosition = p; _windowAt = Time.time; }
            bool steep = Physics.Raycast(p + Vector3.up * 0.4f, Vector3.down, out var slope, 1f, CharacterBody.Mask, QueryTriggerInteraction.Ignore) && slope.normal.y < 0.7f;
            if (_state.Step(wet, below, _player.Body.Grounded, barelyMoved, steep, Time.deltaTime))
            { RecoverToSafety(true); return; }
            if (Time.time >= _sampleAt && _player.Body.Grounded && !wet)
            { _sampleAt = Time.time + 0.5f; RememberSafe(); }
        }

        /// <summary>Settings action and hazard recovery. Returns true when a recovery was scheduled.</summary>
        public bool RecoverToSafety(bool notify = true)
        {
            if (!CanRecoverToSafety) return false;
            Vector3 target = _lastSafe;
            bool online = VenueSession.Current?.IsOnline == true;
            Vector3? nearbyOrigin = online ? transform.position : (Vector3?)null;
            bool found = online ? FindNearby(transform.position, out target, 4f) : _hasSafe || FindNearby(transform.position, out target);
            if (!found)
            { _retryAt = Time.time + 1f; if (notify) Bus.Publish(new InteractionToastSignal { key = "player.noSafeGround" }); return false; }
            _recovering = true;
            StartCoroutine(Recover(target, notify, _state.WetFor >= 1.2f, nearbyOrigin: nearbyOrigin));
            return true;
        }

        private static bool FindNearby(Vector3 from, out Vector3 target, float radius = 80f)
        {
            for (float r = 0f; r <= radius; r += 2f)
                for (int i = 0; i < (r == 0f ? 1 : 24); i++)
                {
                    var p = from + Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward * r;
                    // Try the existing road/bridge/tunnel elevation before consulting terrain above it.
                    if (TrySafeGround(p, 1.2f, out target)) return true;
                    if (VehicleWorld.TryGroundHeight(p.x, p.z, out float y)) p.y = y;
                    if (TrySafeGround(p, 1.2f, out target)) return true;
                }
            target = default; return false;
        }

        /// <summary>Lease loss: keep the seated body suspended until a safe destination is ready, then detach atomically.</summary>
        public bool RecoverDriver(Vector3 vehiclePosition, System.Action detach, System.Action finished)
        {
            if (_recovering || Time.time < _retryAt || _player == null || !_player.OpenWorld || !_player.InVehicle || detach == null) return false;
            // Prefer the current vehicle's vicinity. A saved on-foot point can be kilometres behind the trip.
            // The online exit grant only covers nearby ground; rejoining does not authorize a farther rescue.
            bool online = VenueSession.Current?.IsOnline == true;
            if (!TryDriverRecoveryTarget(vehiclePosition, _hasSafe, _lastSafe, out var target, !online))
            { _retryAt = Time.time + 1f; return false; }
            _recovering = true;
            StartCoroutine(Recover(target, true, false, detach, finished, online ? vehiclePosition : (Vector3?)null));
            return true;
        }

        public static bool TryDriverRecoveryTarget(Vector3 vehiclePosition, bool hasSaved, Vector3 saved, out Vector3 target, bool permitSavedFallback = true)
        {
            if (FindNearby(vehiclePosition, out target, 4f)) return true;
            target = saved;
            return hasSaved && permitSavedFallback;
        }

        private static bool ResolveRecoveryFloor(Vector3 target, Vector3? nearbyOrigin, out Vector3 safe)
        {
            var origin = nearbyOrigin ?? target;
            bool inRange = !nearbyOrigin.HasValue || Vector3.ProjectOnPlane(target - origin, Vector3.up).sqrMagnitude <= 16.01f;
            if (inRange && TrySafeGround(target, 1.2f, out safe)) return true;
            return FindNearby(origin, out safe, nearbyOrigin.HasValue ? 4f : 80f);
        }

        private IEnumerator Recover(Vector3 target, bool notify, bool water, System.Action detach = null, System.Action finished = null, Vector3? nearbyOrigin = null)
        {
            try
            {
                _player.CancelMovement();
                var loading = IslandStreamer.EnsureLoaded(target, 120f);
                while (!loading.IsCompleted) yield return null;
                if (!loading.IsFaulted && !loading.IsCanceled && ResolveRecoveryFloor(target, nearbyOrigin, out var safe))
                {
                    _player.StandIfSeated();
                    _player.SetRecoveryPaused(true);
                    _fadeActive = true;
                    Bus.Publish(new PlayerRecoveryFadeSignal { dim = true, seconds = 0.2f });
                    yield return new WaitForSecondsRealtime(0.2f);
                    if (_player == null || !_player.OpenWorld || (_player.InVehicle && detach == null) || _player.Stunned) yield break;
                    // A vehicle or prop may have moved into the destination during the fade.
                    if (!ResolveRecoveryFloor(safe, nearbyOrigin, out safe)) yield break;
                    // Rejoining retains recent presence. Join the nearby destination, then spend the normal
                    // walking budget while paused; this does not authorize a distant teleport.
                    var street = StreetNetworkBridge.Current;
                    if (street != null)
                    {
                        var reset = street.ResetForNearbyRecovery(safe);
                        while (!reset.IsCompleted) yield return null;
                        if (reset.IsFaulted || reset.IsCanceled) yield break;
                        if (_player == null || !_player.OpenWorld || !TrySafeGround(safe, 1.2f, out safe)) yield break;
                    }
                    detach?.Invoke();
                    _player.ExitVehicleMode(safe, transform.eulerAngles.y);
                    CameraRig.Instance?.SnapToTarget();
                    _lastSafe = safe; _hasSafe = true;
                    if (notify) Bus.Publish(new InteractionToastSignal { key = water ? "player.washedAshore" : "player.recovered" });
                }
                else if (notify) Bus.Publish(new InteractionToastSignal { key = "player.noSafeGround" });
            }
            finally
            {
                EndFade(0.2f);
                _state = default; _retryAt = Time.time + 1f; _recovering = false;
                finished?.Invoke();
            }
        }

        private void EndFade(float seconds)
        {
            if (_player != null && _player.Recovering) _player.SetRecoveryPaused(false);
            if (!_fadeActive) return;
            _fadeActive = false;
            Bus.Publish(new PlayerRecoveryFadeSignal { dim = false, seconds = seconds });
        }

        private void OnDisable()
        {
            EndFade(0f);
            StopAllCoroutines();
            _recovering = false; _state = default;
        }

        private void OnDestroy() => EndFade(0f);
    }
}
