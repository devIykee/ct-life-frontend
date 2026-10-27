using CTLife.Audio;
using CTLife.Core;
using CTLife.World.Vehicles;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// The on-foot camera (Feel lane F1, 2026-10-17, "it doesn't feel GTA like"): third person behind the player at shoulder
    /// height. A drag on the world (the right half; the left half is the stick) orbits it, a pinch zooms; after a moment
    /// without orbiting it swings behind intentional path movement. Direct thumb movement retains the chosen view.
    /// It pulls in when something solid comes between it
    /// and the player and sits closer indoors. Input arrives from the UI layer (UI Toolkit owns touch), so this class has no
    /// input-system dependency.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public static CameraRig Instance { get; private set; }

        /// <summary>Degrees down from horizontal, and the yaw the camera looks along (the stick is relative to it).</summary>
        public float pitch = DefaultPitch;
        public float yaw;
        /// <summary>Boom length outdoors (m); pinch scales it between <see cref="minDistance"/> and <see cref="maxDistance"/>.</summary>
        public float distance = OutdoorDistance;
        public float minDistance = 2.2f;
        public float maxDistance = 7f;
        public float followSharpness = 14f;
        /// <summary>Legacy (the old view's pan limit); unused by the follow camera.</summary>
        public float maxPan = 8f;

        public const float DefaultPitch = 14f, MinPitch = -30f, MaxPitch = 65f;
        public const float OutdoorDistance = 4.2f, InteriorDistance = 2.7f;
        public const float PivotHeight = 1.5f, LookAbove = 0.12f;
        public const float FieldOfView = 52f;
        /// <summary>Orbit gain (degrees per screen pixel of drag).</summary>
        public const float YawPerPixel = 0.14f, PitchPerPixel = 0.1f;
        /// <summary>After this long without orbiting, a moving player's camera swings back behind them.</summary>
        public const float RecenterDelay = 1.2f, RecenterRate = 1.8f;
        public const float CollisionRadius = 0.22f;

        private Transform _target;
        private AvatarView _targetAvatar;
        private LocalPlayerController _targetPlayer;
        private Renderer[] _targetRenderers = System.Array.Empty<Renderer>();
        private bool _targetHidden;
        public Transform Target
        {
            get => _target;
            set
            {
                ShowTarget(true);
                _target = value;
                _targetAvatar = value != null ? value.GetComponent<AvatarView>() : null;
                _targetPlayer = value != null ? value.GetComponent<LocalPlayerController>() : null;
                _targetRenderers = value != null ? value.GetComponentsInChildren<Renderer>(true) : System.Array.Empty<Renderer>();
            }
        }
        /// <summary>Closer than this to the pivot (backed against a wall) the camera rises over the head and the player hides.</summary>
        public const float CloseBoom = 1.1f, MaxLift = 0.7f, HideWithin = 0.8f;
        private Vector3 TargetPosition => _targetAvatar != null ? _targetAvatar.PresentationPosition : Target.position;
        /// <summary>Legacy venue camera bounds (kept for venues that still declare them; the follow camera does not clamp).</summary>
        public Rect Bounds { get; set; } = new Rect(-30, -30, 60, 60);
        public Camera Camera { get; private set; }

        private Vector3 _pivot, _lastTarget, _pivotVel, _ahead, _aheadVel;
        private float _boomVel, _occludedFor, _bigOccludedFor, _riseLift, _riseVel, _climb, _climbVel, _diagAt;
        private float _lastOrbitAt = -10f, _zoom = 1f, _boom = OutdoorDistance;
        private bool _interiorFlag, _ceiling;
        private float _ceilingCheckAt;

        public static int CollisionMask => CharacterLayers.BodyMask & ~((1 << 13) | (1 << 14)); // not props or vegetation (World v2 13, 14)
        /// <summary>World v2 Building 12, Prop 13, Vegetation 14 (tree trunks): the boom keeps a thin margin from these too.</summary>
        public static int ThinMask => (1 << 12) | (1 << 13) | (1 << 14);
        public const float ThinRadius = 0.1f;

        /// <summary>
        /// Boom clearance: solid world with the full camera radius, plus a thin cast against buildings, props and trunks, so
        /// lamp posts, benches and hedges with colliders no longer end up inside the lens. <paramref name="thinOnly"/>: only the
        /// thin cast is in the way (a pole passing between), which the callers ease in on instead of snapping.
        /// </summary>
        public static float Clearance(Vector3 pivot, Vector3 back, float want, int mask, out bool thinOnly)
        {
            float main = ClearDistance(pivot, back, want, CollisionRadius, mask);
            float thin = ClearDistance(pivot, back, want, ThinRadius, ThinMask);
            thinOnly = thin < main - 0.05f;
            return Mathf.Min(main, thin);
        }

        /// <summary>What may hide the player from the lens: the world, props and hedges; not vehicles or people passing by,
        /// and not the invisible coast wall.</summary>
        public static int OcclusionMask => (CharacterLayers.BodyMask | ThinMask) &
            ~((1 << CharacterLayers.Vehicle) | (1 << CharacterLayers.VehicleFlight) | (1 << CharacterLayers.Characters) | (1 << CharacterLayers.Ragdoll) |
              (1 << CharacterLayers.CoastWall));
        /// <summary>Heights on the body (from the feet) the lens must see at least one of.</summary>
        public static readonly float[] BodyChecks = { 0.55f, 1.15f };
        public const float NearBody = 0.7f, MinSeenBoom = 1.3f, BodySideCheck = 0.3f;
        private static readonly float[] BodySideOffsets = { 0f, -1f, 1f };

        /// <summary>
        /// The longest boom (at most <paramref name="boom"/>) from which some of the player's body is in sight: the boom cast
        /// only keeps the line pivot-to-lens clear, so a planter or a wall between the lens and the body (seen from a low
        /// angle) hid the player behind it. When every body point is blocked, the lens moves in front of the nearest blocker.
        /// </summary>
        public static float VisibleBoom(Vector3 pivot, Vector3 back, float boom, Vector3 feet, int mask) =>
            VisibleBoom(pivot, back, boom, feet, mask, out _);

        /// <param name="tooClose">Every body point is hidden and in front of the blocker would put the lens in the player's
        /// face: the caller rises over it instead (the boom stays).</param>
        public static float VisibleBoom(Vector3 pivot, Vector3 back, float boom, Vector3 feet, int mask, out bool tooClose)
        {
            tooClose = false;
            float b = boom;
            for (int iter = 0; iter < 4; iter++)
            {
                var lens = pivot + back * b;
                float nearest = 1f;   // fraction of body-to-lens where the blocker nearest the lens sits
                bool seen = false;
                var side = Vector3.Cross(Vector3.up, back); side.y = 0f;
                side = side.sqrMagnitude > 1e-6f ? side.normalized * BodySideCheck : Vector3.zero;
                foreach (float h in BodyChecks)
                foreach (float o in BodySideOffsets)
                {
                    // a lamp post or a sign pole hides a sliver of the player, not the player: any of three points
                    // across the body in sight counts as seen
                    var p = feet + Vector3.up * h + side * o;
                    var d = lens - p;
                    float l = d.magnitude;
                    if (l < 0.05f) { seen = true; break; }
                    // what the player leans on or sits in (a bench back, the wall at their shoulder) is not in the way
                    float first = float.MaxValue;
                    foreach (var hit in Physics.RaycastAll(p, d / l, l, mask, QueryTriggerInteraction.Ignore))
                        if (hit.distance > NearBody && hit.distance < first) first = hit.distance;
                    if (first == float.MaxValue) { seen = true; break; }
                    nearest = Mathf.Min(nearest, first / l);
                }
                if (seen) return b;
                b = b * nearest - 0.25f;
                // in front of it would put the lens in the player's face: keep the view (the lift looks over instead)
                if (b < MinSeenBoom) { tooClose = true; return boom; }
            }
            return b;
        }

        /// <summary>
        /// Motion + camera pass (2026-10-26): the on-foot boom is a critically damped spring (SmoothDamp) toward its target,
        /// with speed limits. Solid geometry pulls in fast (and the lens never sits more than its radius inside it); a thin
        /// pole or a change of desired distance (a door into a room) eases in; coming back out is slow and calm.
        /// </summary>
        public static float BoomSpring(float boom, float target, ref float velocity, bool solid, float clear, float dt)
        {
            if (dt <= 0f) return boom;
            float next = target < boom
                ? Mathf.SmoothDamp(boom, target, ref velocity, solid ? BoomInSolidTime : BoomInTime, solid ? BoomInSolidSpeed : BoomInSpeed, dt)
                : Mathf.SmoothDamp(boom, target, ref velocity, BoomOutTime, BoomOutSpeed, dt);
            // never deeper into a solid surface than the lens radius (the cast already keeps that margin)
            if (solid && next > clear + CollisionRadius) { next = clear + CollisionRadius; velocity = Mathf.Min(velocity, 0f); }
            return next;
        }

        public const float BoomInSolidTime = 0.07f, BoomInSolidSpeed = 16f, BoomInTime = 0.18f, BoomInSpeed = 6f, BoomOutTime = 0.45f, BoomOutSpeed = 3f;
        /// <summary>Something hiding the player is only acted on after this long (a lamp post or a passer-by never moves the lens).</summary>
        public const float OcclusionGrace = 0.35f;
        /// <summary>Over a large blocker (a kiosk, the plaza's snowman) the lens rises by up to this much instead of diving in.</summary>
        public const float OcclusionRise = 1.4f, RiseTime = 0.35f;
        /// <summary>Boom shorter than this behind a solid: the lens climbs (steeper pitch, in steps, up to ClimbMaxPitch).</summary>
        public const float ClimbBelow = 2.0f, ClimbStep = 6f, ClimbMaxPitch = 62f, ClimbTime = 0.15f;
        /// <summary>Pivot follow: horizontal / vertical smoothing and a short look-ahead along the travel direction.</summary>
        public const float PivotTime = 0.06f, PivotTimeY = 0.12f, LookAheadSeconds = 0.18f, LookAheadMax = 0.6f, LookAheadTime = 0.35f;

        /// <summary>The boom length this frame: solid surfaces pull it in at once, thin ones quickly but smoothly; it eases out.</summary>
        private static float Boom(float boom, float clear, bool thinOnly, float outRate, float dt)
        {
            if (clear >= boom) return Mathf.Lerp(boom, clear, 1f - Mathf.Exp(-outRate * dt));
            return thinOnly ? Mathf.Lerp(boom, clear, 1f - Mathf.Exp(-22f * dt)) : clear;
        }

        public void ConfigureVenue(VenueSceneRoot venue)
        {
            Bounds = venue.cameraBounds;
            if (Camera != null)
            {
                Camera.fieldOfView = FieldOfView;
                Camera.backgroundColor = new Color(0.81f, 0.83f, 0.80f);
            }
        }

        public static CameraRig Ensure()
        {
            if (Instance != null) { Instance.RegisterAudioListener(); return Instance; }
            var cam = Camera.main;
            GameObject go;
            if (cam == null)
            {
                go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            else go = cam.gameObject;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            cam.fieldOfView = FieldOfView;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.78f, 0.92f);
            var rig = go.GetComponent<CameraRig>();
            if (rig == null) rig = go.AddComponent<CameraRig>();
            rig.Camera = cam;
            rig.RegisterAudioListener();
            return rig;
        }

        private void RegisterAudioListener()
        {
            var listener = GetComponent<AudioListener>();
            if (listener == null) listener = gameObject.AddComponent<AudioListener>();
            AudioManager.Instance?.RegisterCameraListener(listener);
        }

        private void Awake()
        {
            Instance = this;
            Camera = GetComponent<Camera>();
            // The first camera can be created while an additive venue is active. Own it independently
            // so leaving that venue does not remove the game camera or its AudioListener.
            if (Application.isPlaying)
            {
                transform.SetParent(null, true);
                DontDestroyOnLoad(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Indoors (a home, or a ceiling overhead): the boom shortens.</summary>
        public void SetInterior(bool interior) => _interiorFlag = interior;
        public bool Interior => _interiorFlag || _ceiling;

        private bool _settleBehind;
        /// <summary>Ease round behind the target even though it is not moving (it just sat down: look where it looks).</summary>
        public void SettleBehind() { _settleBehind = true; _lastOrbitAt = -10f; }

        // ------------------------------------------------------------------ seated view (world polish, 2026-10-23)
        // Sitting (a sofa, a chair, a bench, any engaged spot) the boom no longer stays at the back of the head: it blends in
        // SeatBlend seconds to a raised three-quarter view of the sitter and the room (the character in the lower third,
        // like The Sims / a GTA idle), picked among a ring of angles round the seat for the one the walls leave the most room
        // (in a small room that is the diagonal towards the far corner). A drag still orbits; standing up blends back to the
        // walking camera behind the player.
        public const float SeatBlend = 0.45f;
        public const float SeatPivotHeight = 1.15f, SeatPitch = 21f, SeatPitchIndoor = 17f, SeatDistance = 3.9f, SeatDistanceIndoor = 3.3f;
        /// <summary>Degrees the lens tilts up from the pivot line: the sitter drops to the lower third, the room fills the rest.</summary>
        public const float SeatLookUp = 9f;
        /// <summary>Yaw offsets from the sitter's facing, best first: front three-quarters, then the sides, the rear quarters.</summary>
        public static readonly float[] SeatAngles = { 145f, -145f, 120f, -120f, 165f, -165f, 95f, -95f, 60f, -60f, 30f, -30f };
        private bool _seated, _seatOrbited;
        private float _seatT, _steerT = 1f, _steerFromYaw, _steerToYaw, _steerFromPitch, _steerToPitch, _seatFacing;
        public bool Seated => _seated;
        /// <summary>0 walking .. 1 seated (eased).</summary>
        public float SeatAmount => Mathf.SmoothStep(0f, 1f, _seatT);

        private bool TargetSeated => _targetPlayer != null && !string.IsNullOrEmpty(_targetPlayer.SeatedSpot) && _targetPlayer.enabled &&
                                     !_targetPlayer.EngagedStanding;

        private void BeginSeat(bool seated)
        {
            _seated = seated;
            _seatOrbited = false;
            _settleBehind = false;
            _steerT = 0f;
            _steerFromYaw = yaw; _steerFromPitch = pitch;
            if (seated)
            {
                _seatFacing = Target.eulerAngles.y;
                (_steerToYaw, _steerToPitch) = PickSeatView(TargetPosition, _seatFacing, yaw);
            }
            else
            {
                // back to the walking camera: behind the way the sitter faced as they rose
                _steerToYaw = _seatFacing; _steerToPitch = DefaultPitch;
                _lastOrbitAt = -10f;
            }
        }

        /// <summary>
        /// The seated view: the angle round the seat whose boom the walls leave the most room (ties go to the earlier,
        /// nicer angles and to the side nearer the current view), and its pitch. Indoors a lower pitch (the ceiling).
        /// </summary>
        public (float yaw, float pitch) PickSeatView(Vector3 seatFeet, float facing, float currentYaw)
        {
            bool indoor = Interior;
            float pitchDeg = indoor ? SeatPitchIndoor : SeatPitch;
            float want = (indoor ? SeatDistanceIndoor : SeatDistance) * _zoom;
            var pivot = seatFeet + Vector3.up * SeatPivotHeight;
            float bestScore = float.MinValue, bestYaw = facing + SeatAngles[0];
            for (int i = 0; i < SeatAngles.Length; i++)
            {
                float y = facing + SeatAngles[i];
                var back = Quaternion.Euler(pitchDeg, y, 0f) * Vector3.back;
                float clear = Clearance(pivot, back, want, CollisionMask, out _);
                clear = Mathf.Min(clear, VisibleBoom(pivot, back, clear, seatFeet, OcclusionMask));
                // room first (a boom 30 % short reads as cramped), then the angle's rank, then the side the camera is on
                float side = Mathf.Abs(Mathf.DeltaAngle(currentYaw, y)) / 180f;
                float score = Mathf.Min(clear / want, 1f) * 4f - i * 0.04f - side * 0.06f;
                if (clear < want * 0.7f) score -= 1f;
                if (score > bestScore) { bestScore = score; bestYaw = y; }
            }
            return (Mathf.Repeat(bestYaw, 360f), pitchDeg);
        }

        /// <summary>One frame of the seat blend: the eased amount and the steered yaw / pitch (until a drag takes over).</summary>
        private void UpdateSeat(float dt)
        {
            bool now = TargetSeated;
            if (now != _seated) BeginSeat(now);
            _seatT = Mathf.MoveTowards(_seatT, _seated ? 1f : 0f, dt / SeatBlend);
            if (_steerT < 1f)
            {
                _steerT = Mathf.Min(1f, _steerT + dt / SeatBlend);
                if (!_seatOrbited)
                {
                    float u = Mathf.SmoothStep(0f, 1f, _steerT);
                    yaw = Mathf.Repeat(_steerFromYaw + Mathf.DeltaAngle(_steerFromYaw, _steerToYaw) * u, 360f);
                    pitch = Mathf.Lerp(_steerFromPitch, _steerToPitch, u);
                }
            }
        }

        /// <summary>Jump behind the target (spawn, fast travel, teleports).</summary>
        public void SnapToTarget()
        {
            if (Target != null)
            {
                _pivot = TargetPosition + Vector3.up * PivotHeight;
                _lastTarget = TargetPosition;
                yaw = Target.eulerAngles.y;
            }
            pitch = DefaultPitch;
            _seated = false; _seatT = 0f; _steerT = 1f;
            _boom = DesiredDistance();
            ResetSprings();
            _lastOrbitAt = -10f;
            LateUpdate();
        }

        /// <summary>Pinch: positive delta zooms in. Units are screen pixels of finger-distance change.</summary>
        public void Zoom(float pixelDelta)
        {
            _zoom = Mathf.Clamp(_zoom - pixelDelta * 0.0025f, minDistance / OutdoorDistance, maxDistance / OutdoorDistance);
            distance = OutdoorDistance * _zoom;
        }

        /// <summary>One-finger drag in screen pixels (right = orbit right, up = look up).</summary>
        public void Orbit(Vector2 pixelDelta)
        {
            _settleBehind = false;
            _seatOrbited = true;
            yaw = Mathf.Repeat(yaw + pixelDelta.x * YawPerPixel, 360f);
            pitch = Mathf.Clamp(pitch - pixelDelta.y * PitchPerPixel, MinPitch, MaxPitch);
            _lastOrbitAt = Time.time;
        }

        /// <summary>Legacy name of <see cref="Orbit"/> (the old view panned).</summary>
        public void Pan(Vector2 pixelDelta) => Orbit(pixelDelta);

        private float DesiredDistance()
        {
            float d = distance > 0f ? distance : OutdoorDistance;
            return Interior ? Mathf.Min(d, InteriorDistance * _zoom) : d;
        }

        // ------------------------------------------------------------------ vehicles (Feel lane F2)

        private Vehicle _vehicle;
        private float _shake, _vehicleYaw;
        private Transform _footTarget;

        /// <summary>Chase a vehicle (the driver's avatar rides inside it).</summary>
        public void FollowVehicle(Vehicle v)
        {
            if (v == null) return;
            if (_vehicle == null) _footTarget = Target;
            _vehicle = v;
            _vehicleYaw = v.transform.eulerAngles.y;
            yaw = _vehicleYaw;
            pitch = v.Spec != null ? v.Spec.camPitch : DefaultPitch;
            _lastOrbitAt = -10f;
            ShowTarget(true);
            // Start at the new chase pivot. Easing from the on-foot pivot put the first drone frame inside the avatar.
            _pivot = v.transform.position + Vector3.up * (v.Spec != null ? v.Spec.camHeight : 2f);
            _boom = v.Spec != null ? v.Spec.camDistance : 6f;
            ChaseVehicle(0.1f);
        }

        /// <summary>Back on foot behind the avatar.</summary>
        public void FollowAvatar(Transform avatar)
        {
            bool fromVehicle = _vehicle != null;
            _vehicle = null;
            if (Camera != null) Camera.fieldOfView = FieldOfView;
            Target = avatar != null ? avatar : _footTarget;
            if (!fromVehicle || Target == null) { SnapToTarget(); return; }
            // out of a vehicle: keep looking the way the chase camera did, so the car stays in view beside the player
            // (snapping behind a player who faces away from the car would put the camera against the car's side)
            _pivot = TargetPosition + Vector3.up * PivotHeight;
            _lastTarget = TargetPosition;
            pitch = DefaultPitch;
            _boom = DesiredDistance();
            ResetSprings();
            _lastOrbitAt = Time.time;   // like a manual orbit: recentring behind the player waits a moment
        }

        public Vehicle FollowingVehicle => _vehicle;

        /// <summary>A collision jolt (0..1).</summary>
        public void Shake(float amount) => _shake = Mathf.Max(_shake, Mathf.Clamp01(amount));

        private static int VehicleCameraMask => CollisionMask & ~((1 << CharacterLayers.Vehicle) | (1 << CharacterLayers.VehicleFlight) | (1 << CharacterLayers.Characters));

        private void ChaseVehicle(float dt)
        {
            var v = _vehicle;
            var spec = v.Spec ?? new VehicleSpec();
            var rb = v.Body;
            var vel = rb != null ? rb.linearVelocity : Vector3.zero;
            float speed = vel.magnitude;
            bool flying = v.Flying || VehicleCatalog.Flies(v.Kind);
            // Simple flight takes world movement from this camera's yaw. Following the nose would rotate that
            // input frame again as the craft turns, so held backward/sideways input would spiral. Follow position
            // and retain the player's look direction; Pro and Roadstar still use vehicle-relative controls.
            bool cameraRelative = VehicleCatalog.Flies(v.Kind) && !FlightPhysics.ProControls;
            // the boom swings behind the direction of travel (the nose when slow, flying or reversing)
            float fwdSpeed = Vector3.Dot(vel, v.transform.forward);
            var flat = new Vector3(vel.x, 0f, vel.z);
            float heading = !flying && fwdSpeed > 4f && flat.sqrMagnitude > 1f ? Quaternion.LookRotation(flat).eulerAngles.y : v.transform.eulerAngles.y;
            float idle = Time.time - _lastOrbitAt;
            if (!cameraRelative && idle > (flying ? 1f : 1.4f))
            {
                yaw = Mathf.Repeat(yaw + Mathf.DeltaAngle(yaw, heading) * (1f - Mathf.Exp(-(flying ? 2.2f : 3.4f) * dt)), 360f);
                pitch = Mathf.Lerp(pitch, spec.camPitch + (flying ? 4f : 0f), 1f - Mathf.Exp(-1.5f * dt));
            }
            float top = Mathf.Max(5f, flying ? spec.flightTopSpeed : spec.topSpeed);
            float k = Mathf.Clamp01(speed / top);
            if (Camera != null) Camera.fieldOfView = Mathf.Lerp(Camera.fieldOfView, FieldOfView + 12f * k, 1f - Mathf.Exp(-2.5f * dt));
            var pos = v.transform.position;
            // a tight follow (the car stays big on a phone screen at speed); the yaw lag and FOV carry the sense of speed
            var pivotTarget = pos + Vector3.up * spec.camHeight + Vector3.ClampMagnitude(vel, 40f) * 0.015f;
            _pivot = Vector3.Lerp(_pivot, pivotTarget, 1f - Mathf.Exp(-26f * dt));
            if ((_pivot - pivotTarget).sqrMagnitude > 400f) _pivot = pivotTarget;   // a respawn: no swoop
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            float want = spec.camDistance * (1f + 0.18f * k) * _zoom;
            // (buildings and terrain only: at driving speed a row of lamp posts would make the boom bob in and out)
            float clear = ClearDistance(_pivot, rot * Vector3.back, want, CollisionRadius, VehicleCameraMask);
            _boom = Boom(_boom, clear, false, 2.5f, dt);
            var position = _pivot + rot * Vector3.back * _boom;
            if (_shake > 0f)
            {
                position += new Vector3(Mathf.PerlinNoise(Time.time * 23f, 0.1f) - 0.5f, Mathf.PerlinNoise(0.3f, Time.time * 23f) - 0.5f, 0f) * (_shake * 0.35f);
                _shake = Mathf.MoveTowards(_shake, 0f, dt * 1.6f);
            }
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(_pivot + Vector3.up * 0.2f - position));
            if (v.Kind == VehicleKind.Drone && _footTarget != null)
                ShowTarget(Vector3.Distance(position, _footTarget.position + Vector3.up) > 1f);
        }

        private void LateUpdate()
        {
            if (_vehicle != null) { ChaseVehicle(Mathf.Min(Time.deltaTime, 0.1f)); return; }
            // Game time, like the body it follows (the body moves on Time.deltaTime; captures step both at a fixed rate).
            FollowOnFoot(Mathf.Min(Time.deltaTime, 0.1f));
        }

        private void FollowOnFoot(float dt)
        {
            if (Target == null) return;
            var target = TargetPosition;
            var travel = target - _lastTarget; travel.y = 0f;
            float speed = dt > 1e-5f ? travel.magnitude / dt : 0f;
            _lastTarget = target;
            UpdateSeat(dt);
            float seat = SeatAmount;
            float pivotHeight = Mathf.Lerp(PivotHeight, SeatPivotHeight, seat);
            // a short look-ahead along the travel (smoothed: the view leads a running player a little, never jerks)
            var aheadWant = speed > 0.3f && speed <= 12f ? Vector3.ClampMagnitude(travel / Mathf.Max(dt, 1e-4f) * LookAheadSeconds, LookAheadMax) : Vector3.zero;
            if (seat > 0f) aheadWant = Vector3.zero;
            _ahead = Vector3.SmoothDamp(_ahead, aheadWant, ref _aheadVel, LookAheadTime, 4f, dt);
            var pivotWant = target + Vector3.up * pivotHeight + _ahead;
            bool teleported = speed > 12f;
            if (teleported) { _pivot = pivotWant; _pivotVel = Vector3.zero; } // a teleport: no swoop across the map
            else
            {
                // critically damped follow (horizontal tight, vertical a little softer so steps and stairs do not bob the view)
                float px = Mathf.SmoothDamp(_pivot.x, pivotWant.x, ref _pivotVel.x, PivotTime, Mathf.Infinity, dt);
                float pz = Mathf.SmoothDamp(_pivot.z, pivotWant.z, ref _pivotVel.z, PivotTime, Mathf.Infinity, dt);
                float py = Mathf.SmoothDamp(_pivot.y, pivotWant.y, ref _pivotVel.y, PivotTimeY, Mathf.Infinity, dt);
                _pivot = new Vector3(px, py, pz);
            }
            if (Time.time >= _ceilingCheckAt)
            {
                _ceilingCheckAt = Time.time + 0.25f;
                _ceiling = Physics.Raycast(target + Vector3.up * 1.0f, Vector3.up, 3.5f, CollisionMask, QueryTriggerInteraction.Ignore);
            }
            // Direct movement uses this yaw as its input frame. Following the actor's resulting turn would
            // rotate that same held input again, curving backward/sideways travel without a camera gesture.
            // DirectInputActive also includes release braking; explicit Orbit remains free to change the view.
            bool direct = _targetPlayer != null && _targetPlayer.DirectInputActive;
            float idle = Time.time - _lastOrbitAt;
            if (direct) _settleBehind = false; // A thumb taking over cancels a pending seat-camera settle.
            else if (_seated) _settleBehind = false;   // the seated view owns the angle (UpdateSeat)
            else if (_settleBehind)
            {
                float delta = Mathf.DeltaAngle(yaw, Target.eulerAngles.y);
                yaw = Mathf.Repeat(yaw + delta * (1f - Mathf.Exp(-3f * dt)), 360f);
                if (Mathf.Abs(delta) < 1.5f || idle < RecenterDelay) _settleBehind = false;
            }
            else yaw = Recenter(yaw, Target.eulerAngles.y, speed, idle, dt);
            if (!direct && idle > RecenterDelay + 0.8f && speed > 0.5f) pitch = Mathf.Lerp(pitch, DefaultPitch, 1f - Mathf.Exp(-1.2f * dt));
            // A big solid thing right behind the player (a kiosk, the plaza's snowman, a doorway's wall): climb over it like
            // GTA's camera, a few degrees steeper, instead of diving into the player's face. Eased; returns when clear.
            float wantClimb = 0f;
            if (seat <= 0f)
            {
                float desired = DesiredDistance();
                // look along the whole boom, so the climb starts before the lens reaches the blocker (not once it is there)
                float reach = Mathf.Max(ClimbBelow, desired * 0.8f);
                if (desired > ClimbBelow && BigBlocker(_pivot, Quaternion.Euler(pitch, yaw, 0f) * Vector3.back, reach))
                    for (float extra = ClimbStep; pitch + extra <= ClimbMaxPitch; extra += ClimbStep)
                        if (!BigBlocker(_pivot, Quaternion.Euler(pitch + extra, yaw, 0f) * Vector3.back, reach))
                        { wantClimb = extra; break; }
            }
            _climb = Mathf.SmoothDamp(_climb, wantClimb, ref _climbVel, ClimbTime, 90f, dt);
            float want = Mathf.Lerp(DesiredDistance(), (Interior ? SeatDistanceIndoor : SeatDistance) * _zoom, seat);
            // Room framing: when the room squeezes the boom (a counter, a booth, a small shop), frame the player instead of
            // hugging the back of the head: the lens goes higher (steeper) and over the right shoulder, so the head sits low
            // and to one side and the room ahead stays visible. Driven by the clearance along the plain view (not the framed
            // one, which would feed back) and eased in and out.
            float frameWant = 0f;
            if (seat <= 0f)
            {
                float plain = Clearance(_pivot, Quaternion.Euler(pitch + _climb, yaw, 0f) * Vector3.back, want, CollisionMask, out _);
                frameWant = RoomFraming(plain, want);
            }
            if (teleported) { _frame = frameWant; _frameVel = 0f; }
            else _frame = Mathf.SmoothDamp(_frame, frameWant, ref _frameVel, FrameTime, 4f, dt);
            var rot = Quaternion.Euler(pitch + _climb + FramePitch * _frame, yaw, 0f);
            var pivot = _pivot;
            if (_frame > 0.001f)
            {
                var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                float side = FrameShoulder * _frame;
                if (Physics.SphereCast(_pivot, CollisionRadius, right, out var wall, side + 0.05f, CollisionMask, QueryTriggerInteraction.Ignore))
                    side = Mathf.Max(0f, wall.distance - 0.05f);
                pivot = _pivot + right * side;
            }
            float clear = Clearance(pivot, rot * Vector3.back, want, CollisionMask, out bool thinOnly);
            // Something between the lens and the body (not the pivot): after a short grace, come in front of it, smoothly;
            // a large blocker the lens cannot get in front of makes the lens rise and look over it instead.
            float seen = VisibleBoom(pivot, rot * Vector3.back, clear, target, OcclusionMask, out bool tooClose);
            bool occluded = seen < clear - 0.05f;
            _occludedFor = occluded ? _occludedFor + dt : 0f;
            _bigOccludedFor = tooClose ? _bigOccludedFor + dt : 0f;
            float goal = occluded && _occludedFor >= OcclusionGrace ? seen : clear;
            bool solid = !thinOnly && clear < _boom - 0.02f;
            // (the seat blend eases the boom in step with the angle: it keeps the older, quicker exponential)
            if (seat > 0f && seat < 1f || _steerT < 1f) { _boom = Boom(_boom, goal, thinOnly || occluded, 9f, dt); _boomVel = 0f; }
            else if (teleported) { _boom = goal; _boomVel = 0f; _climb = _climbVel = 0f; } // a teleport (a door into a room) is a cut: frame it at once
            else _boom = BoomSpring(_boom, goal, ref _boomVel, solid, clear, dt);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            // camera pass diagnostics: what pulls the boom in (dev builds; no positions logged)
            if (_boom < 1.6f && Time.unscaledTime >= _diagAt)
            {
                _diagAt = Time.unscaledTime + 0.5f;
                string what = "-";
                if (Physics.SphereCast(pivot, CollisionRadius, rot * Vector3.back, out var dh, want, CollisionMask, QueryTriggerInteraction.Ignore))
                    what = dh.collider.name + "@" + LayerMask.LayerToName(dh.collider.gameObject.layer) + " d=" + dh.distance.ToString("0.00");
                Debug.Log($"CTLIFE [cam] close boom={_boom:0.00} want={want:0.00} clear={clear:0.00} seen={seen:0.00} occl={occluded} big={tooClose} solid={solid} thin={thinOnly} climb={_climb:0.0} hit={what}");
            }
#endif
            float riseWant = _bigOccludedFor >= OcclusionGrace ? OcclusionRise : 0f;
            _riseLift = Mathf.SmoothDamp(_riseLift, riseWant, ref _riseVel, RiseTime, 4f, dt);
            // Backed against something: rise over the head and look past it (seeing the inside of the head is not a view).
            float closeLift = MaxLift * Mathf.Clamp01((CloseBoom - _boom) / (CloseBoom - 0.35f));
            float lift = Mathf.Max(closeLift, _riseLift);
            if (lift > 0f && Physics.SphereCast(pivot, CollisionRadius, Vector3.up, out var roof, lift + 0.1f, CollisionMask, QueryTriggerInteraction.Ignore))
                lift = Mathf.Max(0f, roof.distance - 0.1f);
            var position = pivot + rot * Vector3.back * _boom + Vector3.up * lift;
            // never inside the player's own body: a lens closer than MinBodyDistance to the body's capsule (walking in through
            // a door toward the lens, a tight room) rises clear of it; only if that is impossible does the body hide
            float bodyDist = BodyDistance(position, target);
            if (bodyDist < MinBodyDistance)
            {
                float up = MinBodyDistance - bodyDist + 0.05f;
                if (Physics.SphereCast(position, CollisionRadius, Vector3.up, out var head, up + 0.05f, CollisionMask, QueryTriggerInteraction.Ignore))
                    up = Mathf.Max(0f, head.distance - 0.05f);
                position += Vector3.up * up;
                bodyDist = BodyDistance(position, target);
            }
            // backed against a wall the lens looks past the head; rising over a blocker it keeps looking at the player
            var look = pivot + Vector3.up * LookAbove + (rot * Vector3.forward) * (Mathf.Min(lift, closeLift) * 1.5f);
            var lookRot = Quaternion.LookRotation(look - position);
            // seated: tilt the lens up so the sitter sits in the lower third and the room fills the frame
            if (seat > 0f) lookRot = lookRot * Quaternion.Euler(-SeatLookUp * seat, 0f, 0f);
            transform.SetPositionAndRotation(position, lookRot);
            // The lift must not make a collision-shortened boom count as clear of the body again.
            // the lens keeps clear of the body (above), so the player only hides when even that was impossible
            ShowTarget(bodyDist > HideBodyWithin);
        }

        /// <summary>Room framing: below FrameFrom metres of clear boom it starts, at FrameFull it is complete (FramePitch degrees
        /// steeper, FrameShoulder metres over the right shoulder), eased over FrameTime seconds.</summary>
        public const float FrameFrom = 2.4f, FrameFull = 1.2f, FramePitch = 14f, FrameShoulder = 0.42f, FrameTime = 0.35f;
        private float _frame, _frameVel;
        /// <summary>Framing amount for a plain-view clearance (0 = the normal follow view, 1 = full room framing). Pure.</summary>
        public static float RoomFraming(float clearance, float want)
        {
            float from = Mathf.Min(FrameFrom, want - 0.05f);
            if (from <= FrameFull) return 0f;
            return Mathf.Clamp01((from - clearance) / (from - FrameFull));
        }

        /// <summary>The lens keeps this far from the player's body capsule (rising if needed); closer than HideBodyWithin the body hides.</summary>
        public const float MinBodyDistance = 0.6f, HideBodyWithin = 0.35f, BodyBottom = 0.3f, BodyTop = 1.75f;

        /// <summary>Distance from a point to the player's body capsule axis (feet + BodyBottom .. feet + BodyTop). Pure.</summary>
        public static float BodyDistance(Vector3 point, Vector3 feet)
        {
            float y = Mathf.Clamp(point.y, feet.y + BodyBottom, feet.y + BodyTop);
            return Vector3.Distance(point, new Vector3(feet.x, y, feet.z));
        }

        /// <summary>
        /// Is something big (a wall, a kiosk, the snowman: not a pole or a bench) closer than <paramref name="within"/> along
        /// the boom? Props count when their bounds are at least BigBlockerSize across; buildings and solid ground always.
        /// </summary>
        public static bool BigBlocker(Vector3 pivot, Vector3 back, float within)
        {
            if (!Physics.SphereCast(pivot, ThinRadius, back, out var hit, within, CollisionMask | ThinMask, QueryTriggerInteraction.Ignore)) return false;
            if (((1 << hit.collider.gameObject.layer) & CollisionMask) != 0 && ((1 << hit.collider.gameObject.layer) & ThinMask) == 0) return true;
            var b = hit.collider.bounds.size;
            return Mathf.Max(b.x, b.z) >= BigBlockerSize && b.y >= 1.0f;
        }

        public const float BigBlockerSize = 1.2f;

        private void ResetSprings()
        {
            _boomVel = _riseLift = _riseVel = _occludedFor = _bigOccludedFor = _climb = _climbVel = _frame = _frameVel = 0f;
            _pivotVel = _ahead = _aheadVel = Vector3.zero;
        }

        private void ShowTarget(bool show)
        {
            if (_targetHidden == !show) return;
            _targetHidden = !show;
            foreach (var r in _targetRenderers) if (r != null) r.forceRenderingOff = !show;
        }

        /// <summary>
        /// The yaw after one frame of auto-recentring: toward <paramref name="heading"/> (the player's facing) once
        /// <paramref name="idle"/> exceeds the delay and the player moves; faster the faster they go. Pure (EditMode-tested).
        /// </summary>
        public static float Recenter(float yaw, float heading, float speed, float idle, float dt)
        {
            if (idle < RecenterDelay || speed < 0.5f) return yaw;
            float delta = Mathf.DeltaAngle(yaw, heading);
            float rate = RecenterRate * Mathf.Clamp01(speed / DirectMovementInput.JogSpeed);
            return Mathf.Repeat(yaw + delta * (1f - Mathf.Exp(-rate * dt)), 360f);
        }

        /// <summary>How far the boom can reach from the pivot before a solid surface (minus a margin), at most <paramref name="want"/>.</summary>
        public static float ClearDistance(Vector3 pivot, Vector3 back, float want, float radius, int mask)
        {
            if (Physics.SphereCast(pivot, radius, back, out var hit, want, mask, QueryTriggerInteraction.Ignore))
                return Mathf.Max(0.35f, hit.distance - 0.05f);
            return want;
        }

        /// <summary>
        /// Legacy diorama-view helper (the old high 3/4 camera): the look-ahead that puts a ground target at a viewport
        /// height. Kept for editor captures that still frame venues that way.
        /// </summary>
        public static float LookAheadForViewport(float distance, float pitchDegrees, float fovDegrees, float viewportY, float focusHeight)
        {
            float angle = pitchDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
            float q = (2f * viewportY - 1f) * Mathf.Tan(fovDegrees * Mathf.Deg2Rad * 0.5f);
            return -(q * distance + focusHeight * (cos + q * sin)) / (sin - q * cos);
        }
    }
}
