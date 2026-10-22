using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// Simple helicopter/drone flight uses a bounded horizontal velocity controller, automatic heading and explicit
    /// climb rate/hover. Pro flight retains cyclic/yaw input. Roadstar remains a flying car. Surface queries distinguish
    /// authored water from missing terrain; landing waits for streamed collision and a flat dry floor. Rotor spin-up,
    /// takeoff queuing, flight ceiling, drone leash and Roadstar folding parts share the same physical body.
    /// </summary>
    [RequireComponent(typeof(Vehicle), typeof(Rigidbody))]
    public sealed class FlightPhysics : MonoBehaviour
    {
        private Vehicle _v;
        private Rigidbody _rb;
        private VehicleSpec _s;
        private CarPhysics _car;   // the Roadstar's ground physics (folds its wheels in flight)

        public float Rotor01 { get; private set; }
        public float Thrust01 { get; private set; }
        public bool Grounded { get; private set; }
        public float Altitude { get; private set; }
        /// <summary>Over the sea (its surface is nearer than any ground below): no landing, no getting out.</summary>
        public bool OverWater { get; private set; }
        /// <summary>The Roadstar was told to land but is over water, so it hovers instead (the HUD says why).</summary>
        public bool LandingBlockedByWater => LandingRequested && OverWater;
        public bool LandingBlockedBySlope => LandingRequested && GroundNormal.y < 0.85f;
        public bool WaitingForGround { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public bool CanExit => Grounded && _groundedFor >= 0.5f && _v.Speed < 2f && GroundNormal.y >= 0.85f;
        public bool LandingRequested => IsRoadstar ? !_wantFlight && _v.Flying : _landing;
        private bool _landing;
        private float _queuedClimb, _loadAt;
        private System.Threading.Tasks.Task _loading;
        public static bool ProControls
        {
            get => PlayerPrefs.GetInt("ctlife.flight.pro", 0) == 1;
            set { PlayerPrefs.SetInt("ctlife.flight.pro", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        public void RequestLanding() { if (IsRoadstar) SetFlight(false); else _landing = true; }
        /// <summary>Roadstar: 0 = wheels down (driving), 1 = folded into hover pads.</summary>
        public float Fold01 { get; private set; }
        private bool _wantFlight;
        private float _groundedFor;
        private readonly System.Collections.Generic.List<Transform> _pads = new System.Collections.Generic.List<Transform>();
        private static Material _padMaterial;

        /// <summary>The Roadstar's hover pads: glowing discs that take the wheels' place in flight.</summary>
        private void BuildPads()
        {
            if (_padMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                _padMaterial = new Material(shader) { name = "RoadstarPad" };
                _padMaterial.SetColor("_BaseColor", new Color(0.35f, 0.95f, 1f, 1f));
                _padMaterial.color = new Color(0.35f, 0.95f, 1f, 1f);
            }
            for (int i = 0; i < 4; i++)
            {
                var w = _v.Wheels[i];
                if (w == null) continue;
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pad.name = "HoverPad";
                Destroy(pad.GetComponent<Collider>());
                pad.transform.SetParent(transform, false);
                var local = transform.InverseTransformPoint(w.position);
                pad.transform.localPosition = new Vector3(local.x, 0.18f, local.z);
                pad.transform.localScale = new Vector3(0.62f, 0.012f, 0.62f);
                var r = pad.GetComponent<MeshRenderer>();
                r.sharedMaterial = _padMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                pad.SetActive(false);
                _pads.Add(pad.transform);
            }
        }

        private readonly System.Collections.Generic.List<Transform> _jets = new System.Collections.Generic.List<Transform>();

        /// <summary>The Roadstar's thrusters: two glowing jets at the tail corners that stretch with the throttle in flight.</summary>
        private void BuildFlame()
        {
            var b = GetComponent<BoxCollider>();
            float tail = b != null ? b.center.z - b.size.z * 0.5f : -2.1f;
            float y = b != null ? b.center.y : 0.7f, half = b != null ? b.size.x * 0.28f : 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "Thruster";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(half * side, y, tail);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // the capsule's long axis along the car
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = _padMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
                _jets.Add(go.transform);
            }
            _flameZ = tail;
        }

        private float _jet, _flameZ;

        public bool IsRoadstar => _s != null && _s.kind == VehicleKind.Roadstar;

        private void Awake()
        {
            _v = GetComponent<Vehicle>();
            _rb = GetComponent<Rigidbody>();
            _car = GetComponent<CarPhysics>();
            _s = _v.Spec ?? VehicleCatalog.Get(_v.specId) ?? new VehicleSpec();
            // the v3 hovercar glows through its own emission map; the v1 Roadstar grew pads and jets from primitives
            if (IsRoadstar && _v.Thrusters.Count == 0) { BuildPads(); BuildFlame(); }
            if (IsRoadstar && _v.Thrusters.Count > 0) BindGlow();
            if (_car == null)
            {
                _rb.mass = _s.mass;
                _rb.centerOfMass = _s.centerOfMass;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                _rb.linearDamping = 0.05f;
                _rb.angularDamping = 1.2f;
                _rb.maxAngularVelocity = 6f;
            }
        }

        /// <summary>Roadstar: lift off (wheels fold, hover pads glow) or come down to land.</summary>
        public void SetFlight(bool fly)
        {
            if (!IsRoadstar) return;
            _wantFlight = fly;
            if (fly && !_v.Flying) { _v.Flying = true; _v.SetFlightLayer(true); _rb.AddForce(Vector3.up * 3.5f, ForceMode.VelocityChange); }
        }

        public bool WantsFlight => _wantFlight;

        private bool Active => IsRoadstar ? _v.Flying : true;

        /// <summary>A missing collider never implies water; only an explicit finite water surface does.</summary>
        public static bool WaterIsSurface(float groundY, float waterY) =>
            !float.IsNaN(waterY) && !float.IsInfinity(waterY) && (float.IsNaN(groundY) || waterY > groundY + 0.3f);

        public static Vector3 SimpleAcceleration(Vector3 intent, Vector3 velocity, float topSpeed)
        {
            intent.y = 0f; velocity.y = 0f;
            return Vector3.ClampMagnitude((Vector3.ClampMagnitude(intent, 1f) * topSpeed - velocity) * 1.8f, 6f);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            bool powered = _v.Occupied && _v.EngineOn;
            Rotor01 = Mathf.MoveTowards(Rotor01, powered ? 1f : 0f, dt / (IsRoadstar ? 0.6f : _s.kind == VehicleKind.Drone ? 0.4f : 1.6f));
            bool folded = IsRoadstar && _v.Flying && (_wantFlight || Altitude > 2.4f || OverWater);
            Fold01 = Mathf.MoveTowards(Fold01, folded ? 1f : 0f, dt / 0.8f);
            if (!Active) { Thrust01 = 0f; return; }
            var input = _v.Occupied ? _v.Input : VehicleInput.None;
            var pos = _rb.position;
            var vel = _rb.linearVelocity;
            float g = -Physics.gravity.y;
            bool hitGround = Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out var hit, 600f,
                CarPhysics.GroundMask, QueryTriggerInteraction.Ignore);
            // Valid road/tunnel/bridge collision wins. Terrain is a fallback, not a roof through which to teleport.
            float groundY = hitGround ? hit.point.y : IslandStreamer.GroundHeight(pos.x, pos.z);
            // The always-loaded coarse collider is deliberately below detail. Its hit must not let a craft descend
            // inside the visible mountain while the detailed cell loads. Authored tunnel/bridge floors still win.
            if (hitGround && hit.collider.GetComponentInParent<IslandGroundFallback>() != null &&
                VehicleWorld.TryGroundHeight(pos.x, pos.z, out float terrainY)) groundY = Mathf.Max(groundY, terrainY);
            GroundNormal = hitGround ? hit.normal : Vector3.up;
            float water = VehicleWorld.WaterSurfaceAt(pos);
            OverWater = WaterIsSurface(groundY, water);
            float surface = OverWater ? water : groundY;
            bool known = !float.IsNaN(surface) && !float.IsInfinity(surface);
            Altitude = known ? pos.y - surface : float.PositiveInfinity;
            WaitingForGround = !VehicleWorld.IsReady(pos) || !hitGround;
            Grounded = !WaitingForGround && !OverWater && GroundNormal.y >= 0.85f && Altitude < (IsRoadstar ? 0.55f : 0.3f);
            _groundedFor = Grounded ? _groundedFor + dt : 0f;
            if (WaitingForGround && Time.time >= _loadAt && (_loading == null || _loading.IsCompleted))
            {
                _loadAt = Time.time + 1f;
                if (_loading != null && _loading.IsFaulted) LogLoadingFailure();
                _loading = IslandStreamer.EnsureLoaded(pos, 120f);
            }
            // Clamp an unloaded terrain floor before gravity can sink through its visible mesh. Dry NaN stays unknown.
            float clearance = OverWater || WaitingForGround ? 1.2f : 0f;
            if (known && pos.y < surface + clearance && (OverWater || WaitingForGround))
            {
                pos.y = surface + clearance; _rb.position = pos;
                if (vel.y < 0f) { vel.y = 0f; _rb.linearVelocity = vel; }
                Altitude = clearance;
            }
            if (input.lift > 0.05f) { _landing = false; if (Rotor01 < 0.9f) _queuedClimb = 0.65f; }
            // A rejected landing must not capture the controls: move the stick to choose a flatter/drier spot.
            if (!IsRoadstar && (input.move.sqrMagnitude > 0.03f || Mathf.Abs(input.pitch) > 0.1f || Mathf.Abs(input.steer) > 0.1f)) _landing = false;
            float lift = input.lift;
            if (_queuedClimb > 0f && Rotor01 >= 0.9f) { lift = Mathf.Max(lift, 1f); _queuedClimb -= dt; }
            if (input.lift < -0.05f || !powered) _queuedClimb = 0f;
            float climb = lift * Mathf.Min(6f, _s.climbRate);
            bool landing = LandingRequested;
            bool blocked = OverWater || WaitingForGround || GroundNormal.y < 0.85f;
            if (landing)
                climb = blocked ? (known ? Mathf.Clamp((2f - Altitude) * 1.5f, 0f, 4f) : 0f) : -Mathf.Min(2f, Mathf.Max(0.35f, Altitude * 0.9f));
            else if (IsRoadstar && _wantFlight && Mathf.Abs(input.lift) < 0.05f && Altitude < _s.hoverHeight) climb = 2.5f;
            if ((OverWater || WaitingForGround) && Altitude < 1.5f) climb = Mathf.Max(climb, (1.5f - Altitude) * 2f);
            float ceiling = VehicleWorld.FlightCeiling;
            if (pos.y > ceiling - 12f) climb = Mathf.Min(climb, (ceiling - pos.y) * 0.6f);
            float spin = Rotor01 * Rotor01;
            float upAccel = (g + Mathf.Clamp((climb - vel.y) * 2.8f, -g * 0.9f, g * 1.4f)) * spin;
            if (Grounded && lift <= 0.05f && !IsRoadstar) upAccel = Mathf.Min(upAccel, g * 0.92f * spin);
            float pitch, roll, yawRate;
            bool simple = input.simpleFlight && !IsRoadstar;
            Vector3 horizontal = Vector3.zero;
            if (simple)
            {
                horizontal = SimpleAcceleration(landing ? Vector3.zero : input.move, vel, _s.flightTopSpeed) * spin;
                var local = transform.InverseTransformDirection(horizontal);
                pitch = Mathf.Clamp(Mathf.Atan2(local.z, g) * Mathf.Rad2Deg, -20f, 20f);
                roll = Mathf.Clamp(Mathf.Atan2(local.x, g) * Mathf.Rad2Deg, -20f, 20f);
                float targetYaw = input.move.sqrMagnitude > 0.03f ? Quaternion.LookRotation(input.move).eulerAngles.y : input.heading;
                yawRate = Mathf.Clamp(Mathf.DeltaAngle(transform.eulerAngles.y, targetYaw) * 3f, -90f, 90f);
            }
            else if (IsRoadstar)
            {
                pitch = input.throttle * 8f - input.brake * 6f;
                yawRate = input.steer * _s.yawRate; roll = input.steer * 18f;
            }
            else
            {
                pitch = input.pitch * _s.maxTilt; roll = input.steer * _s.maxTilt; yawRate = input.yaw * _s.yawRate;
                if (Mathf.Abs(input.pitch) < 0.05f && Mathf.Abs(input.steer) < 0.05f)
                {
                    var local = transform.InverseTransformDirection(new Vector3(vel.x, 0f, vel.z));
                    pitch = Mathf.Clamp(-local.z * 3.2f, -_s.maxTilt, _s.maxTilt);
                    roll = Mathf.Clamp(-local.x * 3.2f, -_s.maxTilt, _s.maxTilt);
                }
            }
            if (Grounded && !IsRoadstar && upAccel < g) { pitch = 0f; roll = 0f; horizontal = Vector3.zero; }
            var target = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * Quaternion.Euler(pitch, 0f, -roll);
            var delta = target * Quaternion.Inverse(_rb.rotation);
            delta.ToAngleAxis(out float ang, out var axis);
            if (ang > 180f) ang -= 360f;
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-6f) axis = Vector3.zero;
            var w = _rb.angularVelocity;
            _rb.AddTorque(Vector3.ProjectOnPlane(axis.normalized * (ang * Mathf.Deg2Rad * 9f), Vector3.up) - Vector3.ProjectOnPlane(w, Vector3.up) * 3.2f, ForceMode.Acceleration);
            float yawNow = Vector3.Dot(w, Vector3.up) * Mathf.Rad2Deg;
            _rb.AddTorque(Vector3.up * ((yawRate - yawNow) * Mathf.Deg2Rad * 4f) * Mathf.Max(0.25f, spin), ForceMode.Acceleration);
            if (simple) _rb.AddForce(Vector3.up * upAccel + horizontal, ForceMode.Acceleration);
            else _rb.AddForce(transform.up * (upAccel / Mathf.Max(0.7f, transform.up.y)), ForceMode.Acceleration);
            if (IsRoadstar)
            {
                var flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                _rb.AddForce(flat * (input.throttle * 11f - input.brake * 8f * Mathf.Sign(Vector3.Dot(vel, flat))), ForceMode.Acceleration);
                var side = Vector3.Cross(Vector3.up, flat);
                _rb.AddForce(-side * Vector3.Dot(vel, side) * 1.8f, ForceMode.Acceleration);
            }
            if (!simple)
            {
                var hv = new Vector3(vel.x, 0f, vel.z);
                float top = Mathf.Max(5f, _s.flightTopSpeed);
                float dragK = (IsRoadstar ? 11f : g * Mathf.Tan(_s.maxTilt * Mathf.Deg2Rad)) / (top * top);
                _rb.AddForce(-hv * hv.magnitude * dragK, ForceMode.Acceleration);
            }
            Thrust01 = Mathf.Clamp01(upAccel / (g * 1.6f) + (IsRoadstar ? input.throttle * 0.5f : 0f));
            if (IsRoadstar && !_wantFlight && !blocked && Grounded && _groundedFor > 0.1f)
            { _v.Flying = false; _v.SetFlightLayer(false); }
            Leash(vel);
        }

        private void LogLoadingFailure() => CTLife.Core.Log.Warn("flight", "landing terrain did not load; holding above the heightfield");

        /// <summary>The drone stays within <see cref="LeashRadius"/> (horizontal) of <see cref="LeashAnchor"/>: past it, a pull back.</summary>
        public Transform LeashAnchor { get; set; }
        public float LeashRadius { get; set; } = 150f;
        /// <summary>At (or past) the leash: the HUD shows "out of range".</summary>
        public bool AtLeash { get; private set; }

        private void Leash(Vector3 vel)
        {
            if (LeashAnchor == null) { AtLeash = false; return; }
            var d = transform.position - LeashAnchor.position; d.y = 0f;
            float over = d.magnitude - LeashRadius;
            AtLeash = over > -4f;
            if (over <= 0f) return;
            var n = d / d.magnitude;
            float outward = Mathf.Max(0f, Vector3.Dot(vel, n));
            _rb.AddForce(-n * (over * 2.5f + outward * 4f), ForceMode.Acceleration);
        }

        private void LateUpdate()
        {
            if (IsRoadstar)
            {
                if (_car != null) _car.WheelFold01 = Fold01 * Fold01 * (3f - 2f * Fold01);
                float glow = Fold01 * (0.75f + 0.25f * Mathf.Sin(Time.time * 18f) * Thrust01);
                foreach (var p in _pads)
                {
                    if (p == null) continue;
                    p.gameObject.SetActive(Fold01 > 0.02f);
                    p.localScale = new Vector3(0.62f * glow, 0.012f, 0.62f * glow);
                }
                float jet = Fold01 > 0.5f ? Mathf.Clamp01(_v.Input.throttle) : 0f;
                _jet = Mathf.Lerp(_jet, jet, 1f - Mathf.Exp(-8f * Time.deltaTime));
                foreach (var f in _jets)
                {
                    bool on = _jet > 0.03f;
                    if (f.gameObject.activeSelf != on) f.gameObject.SetActive(on);
                    // flicker a little; the capsule's local y is the jet's length (it lies along the car)
                    float len = (0.2f + 1.0f * _jet) * (0.9f + 0.1f * Mathf.Sin(Time.time * 41f + f.localPosition.x * 7f));
                    var at = f.localPosition;
                    f.localScale = new Vector3(0.15f, len * 0.5f, 0.15f);
                    f.localPosition = new Vector3(at.x, at.y, _flameZ - len * 0.5f + 0.05f);
                }
            }
            if (_glow.Count > 0) Hover();
            if (_v.Rotors.Count == 0) return;
            float rate = (_s.kind == VehicleKind.Drone ? 2200f : 620f) * Rotor01;
            for (int i = 0; i < _v.Rotors.Count; i++)
            {
                var r = _v.Rotors[i];
                bool ccw = r.name.EndsWith("FR") || r.name.EndsWith("BL");
                bool tail = r.name == "TailRotor";   // the tail rotor turns ~4x faster than the main one
                r.localRotation *= Quaternion.AngleAxis((ccw ? -rate : rate) * (tail ? 3.6f : 1f) * Time.deltaTime, _v.RotorAxes[i]);
            }
        }

        // ------------------------------------------------------------------ the v3 hovercar: glow and bob

        private readonly System.Collections.Generic.List<(Renderer r, int slot)> _glow = new System.Collections.Generic.List<(Renderer, int)>();
        private MaterialPropertyBlock _glowBlock;
        private Color _glowBase;
        private Vector3 _modelRest;
        private float _lift01, _glowNow = -1f;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private void BindGlow()
        {
            foreach (var r in _v.Model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].IsKeywordEnabled("_EMISSION")) { _glow.Add((r, i)); _glowBase = mats[i].GetColor(EmissionId); }
            }
            _glowBlock = new MaterialPropertyBlock();
            _modelRest = _v.Model.localPosition;
            // the additive readability glows VehicleImport puts on the hovercar (under the hull, under each thruster)
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
                if (r.name.StartsWith("Glow_", System.StringComparison.Ordinal)) (r.name == "Glow_Under" ? _underGlow : _jetGlow).Add(r);
        }

        private readonly System.Collections.Generic.List<Renderer> _underGlow = new System.Collections.Generic.List<Renderer>();
        private readonly System.Collections.Generic.List<Renderer> _jetGlow = new System.Collections.Generic.List<Renderer>();
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        /// <summary>
        /// Powered, the hovercar lifts off its gear to its hover height and breathes (a slow, small bob), and its
        /// thrusters' emission brightens with lift and throttle; unpowered it settles back onto its gear and dims.
        /// </summary>
        private void Hover()
        {
            float dt = Time.deltaTime;
            _lift01 = Mathf.MoveTowards(_lift01, _v.EngineOn ? 1f : 0f, dt / 0.9f);
            float e = _lift01 * _lift01 * (3f - 2f * _lift01);
            float bob = Mathf.Sin(Time.time * 1.9f) * 0.022f + Mathf.Sin(Time.time * 3.1f + 1.3f) * 0.008f;
            // the model rests below its authored hover height (gear on the road); powered it rises to it, plus the bob
            float rise = _modelRest.y < -0.05f ? -_modelRest.y : 0.14f;
            _v.Model.localPosition = _modelRest + Vector3.up * (e * (rise + bob * (1f - Fold01 * 0.6f)));
            float glow = 0.35f + e * (0.75f + 0.6f * Thrust01 + 0.5f * Mathf.Clamp01(_v.Input.throttle)) + Fold01 * 0.4f;
            glow *= 1f + 0.05f * Mathf.Sin(Time.time * 23f) * e;
            if (Mathf.Abs(glow - _glowNow) < 0.01f) return;
            _glowNow = glow;
            // parked and off: a faint idle glow so the hull still reads; powered: a bright pool under it
            _glowBlock.Clear();
            _glowBlock.SetFloat(IntensityId, 0.25f + 0.9f * e * glow);
            foreach (var r in _underGlow) if (r != null) r.SetPropertyBlock(_glowBlock);
            _glowBlock.SetFloat(IntensityId, 0.35f + 1.6f * e * glow);
            foreach (var r in _jetGlow) if (r != null) r.SetPropertyBlock(_glowBlock);
            _glowBlock.Clear();
            foreach (var (r, slot) in _glow)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_glowBlock, slot);
                _glowBlock.SetColor(EmissionId, _glowBase * glow);
                r.SetPropertyBlock(_glowBlock, slot);
            }
        }
    }
}
