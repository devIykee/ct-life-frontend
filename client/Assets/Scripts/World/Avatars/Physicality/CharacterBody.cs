using System;
using UnityEngine;
using UnityEngine.AI;

namespace CTLife.World
{
    /// <summary>
    /// The local player's physical body: a CharacterController that collides with every solid collider (chairs, tables,
    /// benches, plants, people) while the NavMeshAgent keeps the walkable-area authority and path following.
    /// - Direct control: the controller moves (slides along props, climbs ≤0.3 m steps); the agent follows via
    ///   nextPosition (updatePosition off), so the server-facing position stays on the NavMesh.
    /// - Airborne: no ground under the feet (stepping off a ledge, a drop): gravity, then a landing reaction scaled by the
    ///   fall height, then the agent re-binds where it lands.
    /// - Impacts: running into something solid is reported (approach speed, obstacle height) for stumbles/falls.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class CharacterBody : MonoBehaviour
    {
        public const float Radius = 0.28f, Height = 1.72f, StepOffset = 0.3f;
        public const float LedgeDrop = 0.45f;      // a drop deeper than this beyond a NavMesh edge is a ledge
        public const float HardLanding = 1.6f;     // falls from higher than this land hard

        public struct ImpactInfo
        {
            public Vector3 normal, point;
            public float speed;        // m/s into the obstacle
            public float obstacleHeight;
            public Collider collider;
            public AvatarView other;   // a person we ran into, if any
        }

        public event Action<ImpactInfo> Impact;
        /// <summary>Fall height (m) when the body lands after being airborne.</summary>
        public event Action<float> Landed;

        public CharacterController Controller { get; private set; }
        public bool Direct { get; private set; }
        public bool Airborne { get; private set; }
        public float AirborneSince { get; private set; }

        private NavMeshAgent _agent;
        private Vector3 _lastVelocity, _airVelocity;
        private float _vertical, _peakY, _nextImpactAt;

        public bool Ready => Controller != null && _agent != null;
        /// <summary>Real horizontal velocity of the last direct move (displacement over that frame's delta time).</summary>
        public Vector3 Velocity { get; private set; }
        public int VelocityFrame { get; private set; } = -1;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            Controller = GetComponent<CharacterController>();
            if (Controller == null) Controller = gameObject.AddComponent<CharacterController>();
            Controller.radius = Radius;
            Controller.height = Height;
            Controller.center = new Vector3(0f, Height * 0.5f + 0.02f, 0f);
            Controller.stepOffset = StepOffset;
            Controller.slopeLimit = 50f;
            Controller.skinWidth = 0.03f;
            Controller.minMoveDistance = 0f;
            Controller.enabled = false; // only while this body moves (direct control / airborne)
            gameObject.layer = CharacterLayers.Characters;
        }

        /// <summary>Direct control starts: the controller moves the actor, the agent follows.</summary>
        public void BeginDirect()
        {
            if (!Ready || Direct || Airborne) return;
            Direct = true;
            _agent.updatePosition = false;
            Controller.enabled = true;
            _vertical = 0f;
        }

        /// <summary>Direct control ends: hand the position back to the agent without a jump.</summary>
        public void EndDirect()
        {
            if (!Direct) return;
            Direct = false;
            Controller.enabled = false;
            if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            {
                _agent.nextPosition = transform.position;
                _agent.updatePosition = true;
            }
            else if (_agent != null) _agent.updatePosition = true;
        }

        /// <summary>One frame of direct movement (velocity already constrained to the walkable area).</summary>
        public void MoveDirect(Vector3 velocity, float dt)
        {
            if (!Direct) BeginDirect();
            if (!Direct) return;
            _lastVelocity = velocity;
            var before = transform.position;
            _vertical = Controller.isGrounded ? -1.5f : _vertical + Physics.gravity.y * dt;
            Controller.Move(velocity * dt + Vector3.up * (_vertical * dt));
            if (_agent.isActiveAndEnabled && _agent.isOnNavMesh)
            {
                _agent.nextPosition = transform.position;
                // The agent clamps to the NavMesh; never let the body drift off the walkable area sideways.
                var nav = _agent.nextPosition;
                var p = transform.position;
                if (new Vector2(nav.x - p.x, nav.z - p.z).sqrMagnitude > 0.0025f)
                {
                    Controller.enabled = false;
                    transform.position = new Vector3(nav.x, p.y, nav.z);
                    Controller.enabled = true;
                }
            }
            var moved = transform.position - before; moved.y = 0f;
            Velocity = dt > 1e-5f ? moved / dt : Vector3.zero;
            VelocityFrame = Time.frameCount;
        }

        /// <summary>
        /// Is there a drop beyond this NavMesh edge (no wall, ground at least <see cref="LedgeDrop"/> lower)? Then the
        /// body may step off it.
        /// </summary>
        public bool IsLedge(Vector3 edgePoint, Vector3 direction, out float drop) => IsLedge(edgePoint, direction, out drop, out _);

        /// <summary>
        /// Marches outward from the NavMesh edge (which sits an agent radius inside the real rim) to the first point with
        /// ground at least <see cref="LedgeDrop"/> lower and nothing solid at body height; that point is where to step.
        /// </summary>
        public bool IsLedge(Vector3 edgePoint, Vector3 direction, out float drop, out Vector3 stepTo)
        {
            drop = 0f; stepTo = edgePoint;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f) return false;
            direction.Normalize();
            float feet = transform.position.y;
            for (float d = 0.2f; d <= 1.2f; d += 0.1f)
            {
                var beyond = edgePoint + direction * d;
                // Something solid at body height (a wall, railing, counter) is not a ledge.
                if (Physics.CheckCapsule(beyond + Vector3.up * (StepOffset + Radius), beyond + Vector3.up * (Height - Radius), Radius * 0.9f,
                        CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore)) return false;
                if (!Physics.Raycast(beyond + Vector3.up * 0.2f, Vector3.down, out var hit, 6f, CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore))
                    return false; // a void: never step into it
                drop = feet - hit.point.y;
                if (drop >= LedgeDrop) { stepTo = beyond + direction * (Radius + 0.12f); _ledgeDirection = direction; return true; }
                if (drop < -0.05f) return false; // rising ground beyond: not a ledge
            }
            return false;
        }

        /// <summary>Leave the NavMesh and fall (ledge, drop, knock off a stage).</summary>
        public void BeginAirborne(Vector3 velocity, Vector3? stepOffTo = null)
        {
            if (!Ready || Airborne) return;
            if (Direct) { Direct = false; }
            Airborne = true;
            AirborneSince = Time.time;
            _airVelocity = new Vector3(velocity.x, Mathf.Max(0f, velocity.y), velocity.z);
            _peakY = transform.position.y;
            _stepOff = stepOffTo;
            if (_stepOff.HasValue)
            {
                // Carry the body over the rim at walking pace before gravity takes it (no landing on the ledge itself).
                var flat = new Vector3(_airVelocity.x, 0f, _airVelocity.z);
                if (flat.magnitude < 1.4f) { flat = (_stepOff.Value - transform.position); flat.y = 0f; flat = flat.normalized * 1.4f; }
                _airVelocity = new Vector3(flat.x, 0f, flat.z);
            }
            if (_agent.isActiveAndEnabled) _agent.enabled = false;
            Controller.enabled = true;
        }

        private Vector3? _stepOff;
        private Vector3 _ledgeDirection;

        private void Update()
        {
            if (!Airborne) return;
            float dt = Time.deltaTime;
            if (_stepOff.HasValue)
            {
                var to = _stepOff.Value - transform.position; to.y = 0f;
                if (to.magnitude > 0.05f && Vector3.Dot(to, new Vector3(_airVelocity.x, 0f, _airVelocity.z)) > 0f && Time.time - AirborneSince < 1f)
                {
                    Controller.Move(new Vector3(_airVelocity.x, 0f, _airVelocity.z) * dt);
                    return;
                }
                _stepOff = null; // over the rim: fall
            }
            _airVelocity += Physics.gravity * dt;
            _airVelocity.x = Mathf.MoveTowards(_airVelocity.x, 0f, 1.2f * dt);
            _airVelocity.z = Mathf.MoveTowards(_airVelocity.z, 0f, 1.2f * dt);
            var flags = Controller.Move(_airVelocity * dt);
            _peakY = Mathf.Max(_peakY, transform.position.y);
            if ((flags & CollisionFlags.Below) != 0 && _airVelocity.y < 0f)
            {
                // Ground under the body centre = landed; only the capsule's edge on a rim = slide off it.
                if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, 0.5f, CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore)) Land();
                else
                {
                    var off = _ledgeDirection.sqrMagnitude > 0.01f ? _ledgeDirection : new Vector3(_airVelocity.x, 0f, _airVelocity.z).normalized;
                    _airVelocity.x = off.x * 1.2f; _airVelocity.z = off.z * 1.2f;
                }
            }
            else if (Time.time - AirborneSince > 6f) Land(); // safety: never fall forever
        }

        private void Land()
        {
            Airborne = false;
            float height = Mathf.Max(0f, _peakY - transform.position.y);
            Controller.enabled = false;
            _agent.enabled = true;
            if (NavMesh.SamplePosition(transform.position, out var hit, 1.5f, NavMesh.AllAreas)) _agent.Warp(hit.position);
            _agent.updatePosition = true;
            Landed?.Invoke(height);
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (Airborne || hit.collider == null || hit.collider.isTrigger || hit.normal.y > 0.55f) return;
            if (hit.collider.transform.IsChildOf(transform)) return;
            var flat = new Vector3(_lastVelocity.x, 0f, _lastVelocity.z);
            var n = new Vector3(hit.normal.x, 0f, hit.normal.z);
            if (n.sqrMagnitude < 1e-4f) return;
            n.Normalize();
            float into = Vector3.Dot(flat, -n);
            if (into < 1.5f || Time.time < _nextImpactAt) return;
            _nextImpactAt = Time.time + 0.6f;
            Impact?.Invoke(new ImpactInfo
            {
                normal = n, point = hit.point, speed = into, collider = hit.collider,
                obstacleHeight = hit.collider.bounds.max.y - transform.position.y,
                other = hit.collider.GetComponentInParent<AvatarView>(),
            });
        }
    }
}
