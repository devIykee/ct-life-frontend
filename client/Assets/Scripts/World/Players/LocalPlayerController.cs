using CTLife.Core;
using CTLife.Net;
using UnityEngine;
using UnityEngine.AI;

namespace CTLife.World
{
    /// <summary>
    /// The local player's avatar: tap-to-move on the venue NavMesh, sends `move {x, z, rotY}` at most 10 times a
    /// second (contract section 5) while moving plus one final update on arrival. Positions are venue-local metres
    /// and rotY is the yaw in radians. Walk 2.2 m/s, run 4.5 m/s for long trips (server max 6 m/s; it derives the
    /// anim from speed: walk above 0.2, run above 3.5).
    /// </summary>
    [RequireComponent(typeof(AvatarView))]
    public sealed class LocalPlayerController : MonoBehaviour
    {
        public const float WalkSpeed = 2.2f;
        public const float RunSpeed = 4.5f;
        public const float RunIfFartherThan = 9f;

        private NavMeshAgent _agent;
        private AvatarView _view;
        private VenueSceneRoot _venue;
        private VenueChannel _channel;
        private Vector3 _lastSentPos;
        private float _lastSentRot;
        private bool _dirty;
        private string _pendingSpot;
        private string _seatedSpot;
        private bool _hasDestination;

        public bool IsMoving => _agent != null && _agent.enabled && _agent.isOnNavMesh && _agent.velocity.sqrMagnitude > 0.01f;
        public string SeatedSpot => _seatedSpot;

        public void Init(VenueSceneRoot venue, VenueChannel channel)
        {
            _venue = venue;
            _channel = channel;
            _view = GetComponent<AvatarView>();
            _view.isLocal = true;
            _agent = gameObject.GetComponent<NavMeshAgent>() ?? gameObject.AddComponent<NavMeshAgent>();
            _agent.speed = WalkSpeed;
            _agent.acceleration = 14f;
            _agent.angularSpeed = 720f;
            _agent.stoppingDistance = 0.05f;
            _agent.radius = 0.3f;
            _agent.height = 1.75f;
            _agent.autoBraking = true;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas)) _agent.Warp(hit.position);
            _lastSentPos = transform.position;
            _lastSentRot = transform.eulerAngles.y;
        }

        /// <summary>Walk to a world point if it is on the NavMesh and inside the venue's walkable rectangles.</summary>
        public bool MoveTo(Vector3 worldPoint, string thenSitAt = null)
        {
            if (_agent == null || !_agent.isOnNavMesh) return false;
            if (!NavMesh.SamplePosition(worldPoint, out var hit, 1.5f, NavMesh.AllAreas)) return false;
            if (_venue != null && !_venue.IsWalkable(hit.position)) return false;
            StandIfSeated();
            _pendingSpot = thenSitAt;
            float dist = Vector3.Distance(transform.position, hit.position);
            _agent.speed = dist > RunIfFartherThan ? RunSpeed : WalkSpeed;
            _agent.isStopped = false;
            _agent.SetDestination(hit.position);
            _hasDestination = true;
            return true;
        }

        /// <summary>Walk to a claimable spot, then send `sit {spotId}`.</summary>
        public bool GoSit(string spotId)
        {
            if (_venue == null || !_venue.TryGetSpot(spotId, out var pos, out _)) return false;
            return MoveTo(pos, spotId);
        }

        public void StandIfSeated()
        {
            if (_seatedSpot == null) return;
            _seatedSpot = null;
            _channel?.Stand();
            _view.SetBase("loco_idle");
        }

        /// <summary>Server rejected a move (outside walkable rects or too fast): snap back to the server position.</summary>
        public void SnapTo(Vector3 worldPos, float rotYRad)
        {
            if (_agent != null && _agent.isOnNavMesh) _agent.Warp(worldPos);
            else transform.position = worldPos;
            transform.rotation = Quaternion.Euler(0, rotYRad * Mathf.Rad2Deg, 0);
            _hasDestination = false;
            _lastSentPos = worldPos;
        }

        /// <summary>Seated state confirmed by the server (PlayerState.spotId/anim).</summary>
        public void OnServerSeat(string spotId, string anim)
        {
            if (string.IsNullOrEmpty(spotId)) return;
            _seatedSpot = spotId;
            if (_venue != null && _venue.TryGetSpot(spotId, out var p, out var rot))
            {
                if (_agent != null && _agent.isOnNavMesh) _agent.Warp(p);
                transform.rotation = Quaternion.Euler(0, rot * Mathf.Rad2Deg, 0);
            }
            _view.SetBase(string.IsNullOrEmpty(anim) ? "act_sit" : anim);
        }

        private void Update()
        {
            if (_agent == null || _channel == null) return;
            float speed = _agent.velocity.magnitude;
            _view.SetSpeed(speed);
            if (_seatedSpot == null) _view.SetBase(speed > 3.5f ? "loco_run" : speed > 0.2f ? "loco_walk" : "loco_idle");

            var pos = transform.position;
            float yaw = transform.eulerAngles.y;
            if ((pos - _lastSentPos).sqrMagnitude > 0.0004f || Mathf.Abs(Mathf.DeltaAngle(yaw, _lastSentRot)) > 2f) _dirty = true;

            if (_dirty && _venue != null)
            {
                var local = _venue.ToLocal(pos);
                if (_channel.Move(local.x, local.y, Mathf.Repeat(yaw, 360f) * Mathf.Deg2Rad))
                {
                    _lastSentPos = pos;
                    _lastSentRot = yaw;
                    _dirty = false;
                }
            }

            if (_hasDestination && !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.02f)
            {
                _hasDestination = false;
                if (_pendingSpot != null)
                {
                    var spot = _pendingSpot;
                    _pendingSpot = null;
                    _channel.Sit(_venue != null ? _venue.WireSpotId(spot) : spot);
                }
            }
        }
    }
}
