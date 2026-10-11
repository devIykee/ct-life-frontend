using CTLife.Audio;
using CTLife.Core;
using CTLife.World.Vehicles;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// The on-foot camera (Feel lane F1, 2026-10-17, "it doesn't feel GTA like"): third person behind the player at shoulder
    /// height. A drag on the world (the right half; the left half is the stick) orbits it, a pinch zooms; after a moment
    /// without orbiting it swings back behind the direction of travel. It pulls in when something solid comes between it
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
                _targetRenderers = value != null ? value.GetComponentsInChildren<Renderer>(true) : System.Array.Empty<Renderer>();
            }
        }
        /// <summary>Closer than this to the pivot (backed against a wall) the camera rises over the head and the player hides.</summary>
        public const float CloseBoom = 1.1f, MaxLift = 0.7f, HideWithin = 0.8f;
        private Vector3 TargetPosition => _targetAvatar != null ? _targetAvatar.PresentationPosition : Target.position;
        /// <summary>Legacy venue camera bounds (kept for venues that still declare them; the follow camera does not clamp).</summary>
        public Rect Bounds { get; set; } = new Rect(-30, -30, 60, 60);
        public Camera Camera { get; private set; }

        private Vector3 _pivot, _lastTarget;
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
            _boom = DesiredDistance();
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
            // the boom swings behind the direction of travel (the nose when slow, flying or reversing)
            float fwdSpeed = Vector3.Dot(vel, v.transform.forward);
            var flat = new Vector3(vel.x, 0f, vel.z);
            float heading = !flying && fwdSpeed > 4f && flat.sqrMagnitude > 1f ? Quaternion.LookRotation(flat).eulerAngles.y : v.transform.eulerAngles.y;
            float idle = Time.time - _lastOrbitAt;
            if (idle > 1.4f)
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
        }

        private void LateUpdate()
        {
            if (_vehicle != null) { ChaseVehicle(Mathf.Min(Time.deltaTime, 0.1f)); return; }
            if (Target == null) return;
            // Game time, like the body it follows (the body moves on Time.deltaTime; captures step both at a fixed rate).
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            var target = TargetPosition;
            var travel = target - _lastTarget; travel.y = 0f;
            float speed = dt > 1e-5f ? travel.magnitude / dt : 0f;
            _lastTarget = target;
            if (speed > 12f) _pivot = target + Vector3.up * PivotHeight; // a teleport: no swoop across the map
            _pivot = Vector3.Lerp(_pivot, target + Vector3.up * PivotHeight, 1f - Mathf.Exp(-followSharpness * dt));
            if (Time.time >= _ceilingCheckAt)
            {
                _ceilingCheckAt = Time.time + 0.25f;
                _ceiling = Physics.Raycast(target + Vector3.up * 1.0f, Vector3.up, 3.5f, CollisionMask, QueryTriggerInteraction.Ignore);
            }
            // Swing back behind the direction of travel once the player stops steering the view.
            float idle = Time.time - _lastOrbitAt;
            if (_settleBehind)
            {
                float delta = Mathf.DeltaAngle(yaw, Target.eulerAngles.y);
                yaw = Mathf.Repeat(yaw + delta * (1f - Mathf.Exp(-3f * dt)), 360f);
                if (Mathf.Abs(delta) < 1.5f || idle < RecenterDelay) _settleBehind = false;
            }
            else yaw = Recenter(yaw, Target.eulerAngles.y, speed, idle, dt);
            if (idle > RecenterDelay + 0.8f && speed > 0.5f) pitch = Mathf.Lerp(pitch, DefaultPitch, 1f - Mathf.Exp(-1.2f * dt));
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            float want = DesiredDistance();
            float clear = Clearance(_pivot, rot * Vector3.back, want, CollisionMask, out bool thinOnly);
            // Pull in at once when something solid comes between (quickly but smoothly for a pole or a trunk); ease back out.
            _boom = Boom(_boom, clear, thinOnly, 3f, dt);
            // Backed against something: rise over the head and look past it (seeing the inside of the head is not a view).
            float lift = MaxLift * Mathf.Clamp01((CloseBoom - _boom) / (CloseBoom - 0.35f));
            if (lift > 0f && Physics.SphereCast(_pivot, CollisionRadius, Vector3.up, out var roof, lift + 0.1f, CollisionMask, QueryTriggerInteraction.Ignore))
                lift = Mathf.Max(0f, roof.distance - 0.1f);
            var position = _pivot + rot * Vector3.back * _boom + Vector3.up * lift;
            var look = _pivot + Vector3.up * LookAbove + (rot * Vector3.forward) * (lift * 1.5f);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(look - position));
            ShowTarget(Vector3.Distance(position, _pivot) > HideWithin);
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
