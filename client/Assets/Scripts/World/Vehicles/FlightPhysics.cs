using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// Assisted flight (Feel lane F2) for the helicopter, the camera drone and the Roadstar's flight mode. Built to be easy
    /// on a phone: the lift input sets a climb rate (altitude holds with the stick centred), tilt targets come from the
    /// sticks and level out on release, and with no input the craft brakes itself into a hover. The thrust vector is tilted
    /// with the body, so a helicopter or drone moves by leaning (collective, cyclic, tail-rotor yaw); the Roadstar flies
    /// like a hover car (gas = forward thrust, the steer stick turns and climbs). Ground effect cushions low hovers, the
    /// flight ceiling (World: IslandStreamer.FlightCeiling, 214 m) caps the climb, and the rotor spins up and down.
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
        public bool LandingBlockedByWater => IsRoadstar && !_wantFlight && _v.Flying && OverWater;
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
            if (IsRoadstar) { BuildPads(); BuildFlame(); }
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

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            bool powered = _v.Occupied && _v.EngineOn;
            Rotor01 = Mathf.MoveTowards(Rotor01, powered ? 1f : 0f, dt / (IsRoadstar ? 0.6f : _s.kind == VehicleKind.Drone ? 0.4f : 3.2f));
            // the Roadstar's wheels fold away in flight and come back down in the last couple of metres of a landing
            bool folded = IsRoadstar && _v.Flying && (_wantFlight || Altitude > 2.4f || OverWater);
            Fold01 = Mathf.MoveTowards(Fold01, folded ? 1f : 0f, dt / 0.8f);
            if (!Active) { Thrust01 = 0f; return; }
            var input = _v.Occupied ? _v.Input : VehicleInput.None;
            float g = -Physics.gravity.y;
            var vel = _rb.linearVelocity;
            // ground below (for ground effect, landing and altitude)
            Grounded = Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out var hit, 600f, CarPhysics.GroundMask, QueryTriggerInteraction.Ignore)
                       && hit.distance < 0.5f + (IsRoadstar ? 0.55f : 0.25f);
            float groundAlt = hit.collider != null ? hit.distance - 0.5f : 600f;
            // the sea counts as a surface (it has no collider): fly over it, never "land" in it
            float sea = VehicleWorld.SeaLevel;
            float seaAlt = float.IsInfinity(sea) ? 600f : transform.position.y - sea;
            OverWater = seaAlt < groundAlt - 0.3f;
            if (OverWater) Grounded = false;
            Altitude = Mathf.Min(groundAlt, seaAlt);
            float ceiling = VehicleWorld.FlightCeiling;
            // vertical: the stick sets a climb rate; centred holds altitude
            float climb = input.lift * _s.climbRate;
            if (IsRoadstar)
            {
                if (!_wantFlight && OverWater) climb = (_s.hoverHeight + 1f - Altitude) * 1.5f;   // no landing on water: hover over it
                else if (!_wantFlight) climb = -Mathf.Clamp(Altitude * 0.9f, 1.4f, 8f);        // coming down to land
                else if (Mathf.Abs(input.lift) < 0.05f && Altitude < _s.hoverHeight) climb = 2.5f; // lift-off to hover height
            }
            else if (OverWater && Altitude < 1.2f) climb = Mathf.Max(climb, (1.2f - Altitude) * 2f);   // skim the water, never sink
            if (transform.position.y > ceiling - 12f) climb = Mathf.Min(climb, (ceiling - transform.position.y) * 0.6f);
            float spin = Rotor01 * Rotor01;
            float effect = 1f + (_s.kind == VehicleKind.Helicopter ? 0.12f * Mathf.Clamp01(1f - Altitude / 7f) : 0f);  // ground effect
            float upAccel = (g + Mathf.Clamp((climb - vel.y) * 2.8f, -g * 0.9f, g * 1.4f)) * spin * effect;
            if (Grounded && input.lift <= 0.05f && !IsRoadstar) upAccel = Mathf.Min(upAccel, g * 0.92f * spin);   // settle on the skids
            // attitude targets: pitch (+ = nose down, forward), roll from the right stick (or a bank into the Roadstar's turns)
            float pitch, roll, yawRate;
            if (IsRoadstar)
            {
                pitch = input.throttle * 8f - input.brake * 6f;
                yawRate = input.steer * _s.yawRate;
                roll = input.steer * 18f;
            }
            else
            {
                pitch = input.pitch * _s.maxTilt;
                roll = input.steer * _s.maxTilt;
                yawRate = input.yaw * _s.yawRate;
                // auto-hover: with the cyclic centred, lean against the drift until it stops
                if (Mathf.Abs(input.pitch) < 0.05f && Mathf.Abs(input.steer) < 0.05f)
                {
                    var local = transform.InverseTransformDirection(new Vector3(vel.x, 0f, vel.z));
                    pitch = Mathf.Clamp(-local.z * 3.2f, -_s.maxTilt, _s.maxTilt);
                    roll = Mathf.Clamp(-local.x * 3.2f, -_s.maxTilt, _s.maxTilt);
                }
            }
            if (Grounded && !IsRoadstar && upAccel < g) { pitch = 0f; roll = 0f; }
            float yaw = transform.eulerAngles.y;
            var target = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(pitch, 0f, -roll);
            var delta = target * Quaternion.Inverse(_rb.rotation);
            delta.ToAngleAxis(out float ang, out var axis);
            if (ang > 180f) ang -= 360f;
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-6f) axis = Vector3.zero;
            var levelTorque = axis.normalized * (ang * Mathf.Deg2Rad * 9f);
            var w = _rb.angularVelocity;
            var wh = Vector3.ProjectOnPlane(w, Vector3.up);
            _rb.AddTorque(Vector3.ProjectOnPlane(levelTorque, Vector3.up) - wh * 3.2f, ForceMode.Acceleration);
            float yawNow = Vector3.Dot(w, Vector3.up) * Mathf.Rad2Deg;
            _rb.AddTorque(Vector3.up * ((yawRate - yawNow) * Mathf.Deg2Rad * 4f) * Mathf.Max(0.25f, spin), ForceMode.Acceleration);
            // thrust along the body's up (helicopter / drone tilt to travel); the Roadstar adds forward thrust
            var bodyUp = transform.up;
            float tilt = Mathf.Max(0.35f, bodyUp.y);
            _rb.AddForce(bodyUp * (upAccel / tilt), ForceMode.Acceleration);
            if (IsRoadstar)
            {
                var flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                _rb.AddForce(flat * (input.throttle * 11f - input.brake * 8f * Mathf.Sign(Vector3.Dot(vel, flat))) , ForceMode.Acceleration);
                // turn the velocity with the nose (a hover car carves rather than skating sideways)
                var side = Vector3.Cross(Vector3.up, flat);
                _rb.AddForce(-side * Vector3.Dot(vel, side) * 1.8f, ForceMode.Acceleration);
            }
            // drag sized so the tilt (or thrust) limit gives the top speed
            var hv = new Vector3(vel.x, 0f, vel.z);
            float top = Mathf.Max(5f, _s.flightTopSpeed);
            float dragK = (IsRoadstar ? 11f : g * Mathf.Tan(_s.maxTilt * Mathf.Deg2Rad)) / (top * top);
            _rb.AddForce(-hv * hv.magnitude * dragK, ForceMode.Acceleration);
            Thrust01 = Mathf.Clamp01(upAccel / (g * 1.6f) + (IsRoadstar ? input.throttle * 0.5f : 0f));
            // the Roadstar lands: once it is within reach of its springs it is a car again (at any speed: it can touch
            // down on a road like a plane and drive on)
            if (IsRoadstar)
            {
                _groundedFor = Grounded ? _groundedFor + dt : 0f;
                bool low = Altitude < 0.9f && vel.y < 0.5f;
                if (!_wantFlight && !OverWater && (low || _groundedFor > 0.1f)) { _v.Flying = false; _v.SetFlightLayer(false); }
            }
            Leash(vel);
        }

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
            if (_v.Rotors.Count == 0) return;
            float rate = (_s.kind == VehicleKind.Drone ? 2200f : 620f) * Rotor01;
            foreach (var r in _v.Rotors)
            {
                var axis = r.parent != null ? r.parent.InverseTransformDirection(transform.up) : Vector3.up;
                bool ccw = r.name.EndsWith("FR") || r.name.EndsWith("BL");
                r.localRotation = Quaternion.AngleAxis((ccw ? -rate : rate) * Time.deltaTime, axis) * r.localRotation;
            }
        }
    }
}
