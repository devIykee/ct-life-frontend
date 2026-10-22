using System;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// When a body is really falling, as opposed to stepping down a kerb or a stair (Feel lane F1, 2026-10-17: "the way the
    /// model is just falling anyhow can be very annoying"). A fall starts only after the feet have had no ground for longer
    /// than <see cref="CoyoteTime"/> AND are more than <see cref="FallDrop"/> below the last ground; anything shorter is a
    /// step. Pure state, EditMode-tested.
    /// </summary>
    public struct FallTracker
    {
        public const float CoyoteTime = 0.2f, FallDrop = 0.5f;
        public float UngroundedFor, LastGroundY, PeakY;
        public bool Falling, Airborne;

        /// <summary>One step. Returns the fall height when the body touches down after a real fall, else -1.</summary>
        public float Step(bool grounded, float feetY, float dt)
        {
            if (grounded)
            {
                float landed = Falling ? Mathf.Max(0f, PeakY - feetY) : -1f;
                UngroundedFor = 0f; LastGroundY = feetY; PeakY = feetY; Falling = false; Airborne = false;
                return landed;
            }
            if (!Airborne) { Airborne = true; PeakY = LastGroundY; }
            UngroundedFor += dt;
            PeakY = Mathf.Max(PeakY, feetY);
            if (!Falling && UngroundedFor > CoyoteTime && LastGroundY - feetY > FallDrop) Falling = true;
            return -1f;
        }
    }

    /// <summary>
    /// The local player's body (Feel lane F1): a CharacterController on whatever solid collision the world has, with no
    /// NavMesh anywhere in it. Kerbs and stairs are stepped (0.32 m up, snapped down within 0.45 m), slopes up to 45°
    /// are walkable, a real drop falls with gravity (<see cref="FallTracker"/>), and walking into walls, kerbs or props
    /// only slides along them: nothing static trips or knocks the player down any more. People it walks into are
    /// reported (<see cref="BumpedPerson"/>) so the owner can nudge them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterBody : MonoBehaviour
    {
        public const float Radius = 0.28f, Height = 1.72f, StepOffset = 0.32f, SlopeLimit = 45f, SkinWidth = 0.04f;
        /// <summary>Ground this far below the feet keeps the body grounded (it is pulled down onto it: stairs, kerb drops).</summary>
        public const float SnapDistance = 0.45f;
        /// <summary>Step-down pull (m/s): a 0.3 m kerb takes three frames, not one.</summary>
        public const float SnapSpeed = 6f;
        /// <summary>Landings from higher than this roll; from higher than <see cref="KnockdownLanding"/> they knock down.</summary>
        public const float HardLanding = 2.5f, KnockdownLanding = 7f;
        /// <summary>Mid-air steering (1/s, exponential toward the stick).</summary>
        public const float AirControl = 1.5f;
        private const float TerminalSpeed = 30f;

        /// <summary>Fall height (m) when the body lands after a real fall.</summary>
        public event Action<float> Landed;
        /// <summary>Walked into a person (them, approach speed m/s).</summary>
        public event Action<AvatarView, float> BumpedPerson;

        public CharacterController Controller { get; private set; }
        public bool Ready => Controller != null;
        /// <summary>Seated, engaged or knocked down: no collision, no gravity; the owner places the root.</summary>
        public bool Suspended { get; private set; }
        public bool Grounded { get; private set; } = true;
        /// <summary>A real fall in progress (the owner shows the fall pose).</summary>
        public bool Airborne => _fall.Falling || Jumping;
        /// <summary>A hop is in the air (from <see cref="Jump"/> until touch-down).</summary>
        public bool Jumping { get; private set; }

        /// <summary>A small hop from the ground (City HUD v2 Jump). False when not standing on something.</summary>
        public bool Jump(float speed)
        {
            if (Suspended || Controller == null || !Grounded || Jumping) return false;
            _vy = speed;
            _air = _lastIntent;
            Jumping = true;
            AirborneSince = UnityEngine.Time.time;
            return true;
        }
        public float AirborneSince { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        /// <summary>Real horizontal velocity of the last move (displacement over that frame's delta time).</summary>
        public Vector3 Velocity { get; private set; }
        public int VelocityFrame { get; private set; } = -1;

        /// <summary>What the body stands on and collides with: the world, never people, ragdolls or vehicle-only blockers.</summary>
        public static int Mask => CharacterLayers.BodyMask;

        private FallTracker _fall;
        private float _vy, _nextBumpAt;
        private Vector3 _air, _lastIntent;
        private static bool _layerRules;

        private void Awake()
        {
            Controller = GetComponent<CharacterController>();
            if (Controller == null) Controller = gameObject.AddComponent<CharacterController>();
            Controller.radius = Radius;
            Controller.height = Height;
            Controller.center = new Vector3(0f, Height * 0.5f + SkinWidth, 0f);
            Controller.stepOffset = StepOffset;
            Controller.slopeLimit = SlopeLimit;
            Controller.skinWidth = SkinWidth;
            Controller.minMoveDistance = 0f;
            gameObject.layer = CharacterLayers.Characters;
            if (!_layerRules)
            {
                // World v2 contract: doorway blockers stop vehicles, not people.
                Physics.IgnoreLayerCollision(CharacterLayers.Characters, CharacterLayers.VehicleBlocker, true);
                Physics.IgnoreLayerCollision(CharacterLayers.Characters, CharacterLayers.TapTarget, true);
                _layerRules = true;
            }
            _fall.LastGroundY = _fall.PeakY = transform.position.y;
        }

        public void Suspend(bool suspended)
        {
            Suspended = suspended;
            if (Controller != null) Controller.enabled = !suspended;
            _vy = 0f; _air = Vector3.zero; Jumping = false;
            _fall = new FallTracker { LastGroundY = transform.position.y, PeakY = transform.position.y };
            Velocity = Vector3.zero;
            Grounded = true;
        }

        /// <summary>Place the body (spawn, correction, stand-up, get-up): on the ground under the point when there is one.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            bool enabled = Controller != null && Controller.enabled;
            if (Controller != null) Controller.enabled = false;
            if (GroundAt(position, out float y)) position.y = y;
            transform.SetPositionAndRotation(position, rotation);
            if (Controller != null) Controller.enabled = enabled && !Suspended;
            _vy = 0f; _air = Vector3.zero; Jumping = false;
            _fall = new FallTracker { LastGroundY = position.y, PeakY = position.y };
            Grounded = true;
            Velocity = Vector3.zero;
        }

        /// <summary>The walkable ground at a point (from 1.2 m above it down to 3 m below).</summary>
        public static bool GroundAt(Vector3 p, out float y)
        {
            y = p.y;
            if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var hit, 4.2f, Mask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.5f)
                return false;
            y = hit.point.y;
            return true;
        }

        /// <summary>One frame: horizontal intent (m/s), gravity, kerb/stair step-down and real falls.</summary>
        public void Move(Vector3 intent, float dt)
        {
            if (Suspended || Controller == null || dt <= 0f) return;
            if (!Controller.enabled) Controller.enabled = true;
            intent.y = 0f;
            _lastIntent = intent;
            var before = transform.position;
            bool probe = Probe(out float gap, out var normal);
            bool walkable = probe && normal.y >= Mathf.Cos(SlopeLimit * Mathf.Deg2Rad);
            bool grounded = _vy <= 0.5f && ((Controller.isGrounded && (!probe || gap <= SnapDistance)) || (Grounded && walkable && gap <= SnapDistance));
            Vector3 motion;
            if (grounded)
            {
                _vy = -2f;
                _air = intent;
                GroundNormal = walkable ? normal : Vector3.up;
                motion = intent * dt;
                // Stay on the stairs and kerbs going down instead of walking off them into the air.
                float pull = probe && gap > SkinWidth ? Mathf.Min(gap, SnapSpeed * dt) : 0f;
                motion.y = -Mathf.Max(pull, 2f * dt);
            }
            else
            {
                _vy = Mathf.Max(_vy + Physics.gravity.y * dt, -TerminalSpeed);
                _air = Vector3.Lerp(_air, intent, 1f - Mathf.Exp(-AirControl * dt));
                motion = _air * dt + Vector3.up * (_vy * dt);
                GroundNormal = Vector3.up;
            }
            var flags = Controller.Move(motion);
            if ((flags & CollisionFlags.Above) != 0 && _vy > 0f) _vy = 0f;
            bool now = grounded || Controller.isGrounded;
            bool wasFalling = _fall.Falling;
            float landed = _fall.Step(now, transform.position.y, dt);
            if (_fall.Falling && !wasFalling) AirborneSince = Time.time;
            Grounded = now;
            var moved = transform.position - before; moved.y = 0f;
            Velocity = moved / dt;
            VelocityFrame = Time.frameCount;
            if (Jumping && now && _vy <= 0f)
            {
                // Touch-down after a hop: a soft landing (a hop never counts as a fall).
                Jumping = false;
                if (landed < 0f) landed = 0.6f;
            }
            if (landed >= 0f) Landed?.Invoke(landed);
        }

        private bool Probe(out float gap, out Vector3 normal)
        {
            var origin = transform.position + Vector3.up * (Radius + 0.1f);
            if (Physics.SphereCast(origin, Radius * 0.85f, Vector3.down, out var hit, 0.25f + SnapDistance, Mask, QueryTriggerInteraction.Ignore))
            {
                gap = Mathf.Max(0f, transform.position.y - hit.point.y);
                normal = hit.normal;
                return true;
            }
            gap = float.MaxValue;
            normal = Vector3.up;
            return false;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.collider == null || hit.collider.isTrigger || hit.normal.y > 0.7f) return; // the ground: nothing to report
            if (hit.collider.GetComponentInParent<SpotAnchor>() != null)
            {
                // A seat's tap box that streamed in unmarked: step through it from now on (taps still find it).
                hit.collider.gameObject.layer = CharacterLayers.TapTarget;
                return;
            }
            if (Time.time < _nextBumpAt) return;
            var other = hit.collider.GetComponentInParent<AvatarView>();
            if (other == null || other.transform == transform) return;
            var n = new Vector3(hit.normal.x, 0f, hit.normal.z);
            if (n.sqrMagnitude < 1e-4f) return;
            float into = Vector3.Dot(_lastIntent, -n.normalized);
            if (into < 1f) return;
            _nextBumpAt = Time.time + 0.6f;
            BumpedPerson?.Invoke(other, into);
        }
    }
}
