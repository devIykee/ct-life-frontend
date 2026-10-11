using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// Arcade raycast car (Feel lane F2): four suspension rays (spring-damper at ~1.7 Hz), tyres that cancel sideways slip up
    /// to mu * load (so grip is planted and breaks away progressively, the handbrake drops the rear's grip to drift),
    /// drive by axle with a top-speed taper, brake-to-reverse, speed-dependent steering, anti-roll bars, downforce, air
    /// levelling (no flips off ramps) and auto-righting when stuck on its side. Scooters use the same model with the two
    /// visual wheels on four close contact points and a lean into turns. Wheels spin, steer and travel with the suspension.
    /// </summary>
    [RequireComponent(typeof(Vehicle), typeof(Rigidbody))]
    public class CarPhysics : MonoBehaviour
    {
        protected Vehicle _v;
        protected Rigidbody _rb;
        protected VehicleSpec _s;

        protected sealed class Wheel
        {
            public Transform visual;
            public Vector3 mount;        // local: the top of the suspension travel
            public Vector3 visualRest;   // local position of the visual at rest
            public Quaternion visualRot; // local rotation of the visual at rest
            public float radius, compression, lastCompression, spin;
            public bool front, driven, left, grounded;
            public Vector3 point, normal;
            public float slip;           // sideways slip speed (m/s), for skid audio/marks
        }

        protected Wheel[] _wheels;
        private float _steer;            // current steer angle (degrees)
        private float _uprightTimer, _rearLoose;
        public float SkidAmount { get; private set; }
        /// <summary>0 = wheels down; 1 = folded flat under the body (the Roadstar in flight).</summary>
        public float WheelFold01 { get; set; }
        public bool Grounded { get; private set; }
        public float Rpm01 { get; private set; }
        public event System.Action<float, Vector3> Impact;   // impulse (N·s), point

        public static int GroundMask => CharacterLayers.BodyMask & ~(1 << CharacterLayers.Vehicle) & ~(1 << CharacterLayers.VehicleFlight) & ~(1 << CharacterLayers.Characters);

        protected virtual void Awake()
        {
            _v = GetComponent<Vehicle>();
            _rb = GetComponent<Rigidbody>();
            _s = _v.Spec ?? VehicleCatalog.Get(_v.specId) ?? new VehicleSpec();
            _rb.mass = _s.mass;
            _rb.centerOfMass = _s.centerOfMass;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.maxAngularVelocity = 7f;
            _rb.linearDamping = 0.02f;
            _rb.angularDamping = 0.6f;
            _rb.solverIterations = 12;
            BuildWheels();
        }

        private void BuildWheels()
        {
            bool scooter = _s.kind == VehicleKind.Scooter;
            var list = new System.Collections.Generic.List<Wheel>();
            for (int i = 0; i < 4; i++)
            {
                var w = _v.Wheels[i];
                if (w == null) continue;
                var r = Mathf.Max(0.12f, RadiusOf(w));
                bool front = i < 2;
                Vector3 local = transform.InverseTransformPoint(w.position);
                if (scooter)
                {
                    // Two wheels -> four contact points 0.14 m apart: stable like a narrow car; the lean is visual.
                    for (int side = -1; side <= 1; side += 2)
                        list.Add(MakeWheel(w, local + Vector3.right * 0.14f * side, r, front, side < 0, side < 0));
                }
                else list.Add(MakeWheel(w, local, r, front, local.x < 0f, true));
            }
            _wheels = list.ToArray();
        }

        private Wheel MakeWheel(Transform visual, Vector3 local, float r, bool front, bool left, bool ownsVisual)
        {
            float staticCompression = _s.mass * 9.81f / 4f / SpringRate();
            return new Wheel
            {
                visual = ownsVisual ? visual : null, radius = r, front = front, left = left,
                driven = front ? _s.frontDrive : _s.rearDrive,
                mount = local + Vector3.up * (_s.suspensionRest - staticCompression),
                visualRest = visual.localPosition, visualRot = visual.localRotation,
                compression = staticCompression, lastCompression = staticCompression,
            };
        }

        private static float RadiusOf(Transform w)
        {
            var mr = w.GetComponentInChildren<MeshRenderer>();
            return mr != null ? mr.bounds.extents.y : 0.3f;
        }

        protected float SpringRate()
        {
            float corner = _s.mass / 4f, omega = 2f * Mathf.PI * _s.suspensionHz;
            return corner * omega * omega;
        }

        protected float DamperRate() => 2f * _s.suspensionDamping * Mathf.Sqrt(SpringRate() * _s.mass / 4f);

        /// <summary>
        /// Traffic on rails (Feel lane F3): a <c>TrafficCar</c> moves the kinematic body along its lane, so no forces here;
        /// the wheels spin and steer from <see cref="RailSpeed"/> and <see cref="RailSteer"/> instead.
        /// </summary>
        public bool Rails { get; private set; }
        public float RailSpeed, RailSteer;

        public void SetRails(bool on)
        {
            Rails = on;
            if (on) { SkidAmount = 0f; Grounded = true; }
        }

        protected virtual void FixedUpdate()
        {
            if (Rails)
            {
                Rpm01 = Mathf.Clamp01(Mathf.Abs(RailSpeed) / Mathf.Max(1f, _s.topSpeed) * 1.15f + 0.1f);
                return;
            }
            if (_wheels == null || _wheels.Length == 0 || _v.Flying) return;
            if (!_v.Occupied && _rb.IsSleeping()) return;   // parked and settled: no rays
            float dt = Time.fixedDeltaTime;
            bool parked = !_v.Occupied;   // parked: in gear with the handbrake on, every wheel holds
            var input = parked ? new VehicleInput { handbrake = true } : _v.Input;
            var up = transform.up;
            var vel = _rb.linearVelocity;
            float fwdSpeed = Vector3.Dot(vel, transform.forward);
            float speed = vel.magnitude;
            // steering: less lock at speed, eased
            float lockAngle = Mathf.Lerp(_s.maxSteer, _s.highSpeedSteer, Mathf.Clamp01(speed / Mathf.Max(1f, _s.topSpeed * 0.75f)));
            _steer = Mathf.MoveTowards(_steer, input.steer * lockAngle, 160f * dt);
            // brake pedal at a standstill = reverse (GTA style); throttle while rolling backwards brakes first
            // (not with the handbrake on: braking to a stop while getting out must not turn into reversing)
            float drive = input.throttle, brake = input.brake;
            if (brake > 0.05f && fwdSpeed < 0.8f && !input.handbrake) { drive = -brake * 0.75f; brake = 0f; }
            else if (drive > 0.05f && fwdSpeed < -0.8f) { brake = drive; drive = 0f; }
            bool drifting = !parked && input.handbrake;
            _rearLoose = Mathf.MoveTowards(_rearLoose, drifting ? 1f : 0f, dt * (drifting ? 6f : 1.25f));
            int drivenCount = 0, groundedCount = 0;
            foreach (var w in _wheels) if (w.driven) drivenCount++;
            float k = SpringRate(), c = DamperRate();
            float skid = 0f;
            float corner = _s.mass / _wheels.Length;
            foreach (var w in _wheels)
            {
                var origin = transform.TransformPoint(w.mount);
                float reach = _s.suspensionRest + w.radius;
                w.lastCompression = w.compression;
                if (Physics.SphereCast(origin + up * 0.05f, w.radius * 0.35f, -up, out var hit, reach - w.radius * 0.35f + 0.05f, GroundMask, QueryTriggerInteraction.Ignore))
                {
                    float dist = hit.distance + w.radius * 0.35f - 0.05f;   // mount to ground
                    w.compression = Mathf.Clamp(reach - dist, 0f, _s.suspensionRest);
                    w.grounded = true; w.point = hit.point; w.normal = hit.normal;
                }
                else { w.compression = 0f; w.grounded = false; }
                if (!w.grounded) continue;
                groundedCount++;
                float compVel = (w.compression - w.lastCompression) / dt;
                float load = Mathf.Max(0f, k * w.compression + c * compVel);
                _rb.AddForceAtPosition(up * load, origin);
                // tyre frame on the ground
                var fwd = transform.forward;
                if (w.front) fwd = Quaternion.AngleAxis(_steer, up) * fwd;
                fwd = Vector3.ProjectOnPlane(fwd, w.normal).normalized;
                var side = Vector3.Cross(w.normal, fwd);
                var pv = _rb.GetPointVelocity(w.point);
                float vf = Vector3.Dot(pv, fwd), vs = Vector3.Dot(pv, side);
                // the rear's grip drops while the handbrake is held and comes back over ~0.8 s after it (drift exits)
                float mu = _s.grip * (w.front ? 1f : Mathf.Lerp(1f, _s.rearGripHandbrake, _rearLoose));
                bool locked = parked || (input.handbrake && !w.front);
                float driveF = 0f;
                if (w.driven && drivenCount > 0)
                {
                    float top = drive >= 0f ? _s.topSpeed : _s.reverseSpeed;
                    float r = Mathf.Abs(fwdSpeed) / Mathf.Max(1f, top);
                    driveF = drive * _s.driveForce * Mathf.Clamp01(1f - r * r) / drivenCount;
                }
                var (lat, lon, over) = Tyre(new TyreIn
                {
                    vf = vf, vs = vs, load = load, mu = mu, corner = corner, dt = dt, drive = driveF,
                    brake = brake * _s.brakeForce / _wheels.Length, locked = locked,
                    coast = Mathf.Abs(drive) < 0.02f && brake < 0.02f,
                    tcs = _rearLoose < 0.05f,   // traction control on the road; off while the rear is loose, so the throttle can hold a drift
                });
                w.slip = Mathf.Abs(vs) * (over ? 1f : 0.3f);
                float sliding = locked ? Mathf.Sqrt(vf * vf + vs * vs) : Mathf.Abs(vs);
                skid = Mathf.Max(skid, Mathf.Clamp01((sliding - 2.2f) / 5f) * (locked || over ? 1f : 0.4f));
                // apply a little above the contact so cornering does not roll the body over
                var at = w.point + up * (_s.centerOfMass.y * 0.6f);
                _rb.AddForceAtPosition(side * lat + fwd * lon, at);
                if (!locked) w.spin += vf / w.radius * Mathf.Rad2Deg * dt;
            }
            Grounded = groundedCount > 0;
            AntiRoll();
            // downforce, drag
            if (Grounded) _rb.AddForce(-up * _s.downforce * speed * speed);
            _rb.AddForce(-vel * speed * 0.38f);
            if (!Grounded) AirLevel(dt);
            // on its side or roof (wheels in the air, so "grounded" says nothing) and nearly still: back on its wheels
            bool upset = up.y < 0.35f && speed < 2.5f && _rb.angularVelocity.magnitude < 1.5f;
            _uprightTimer = upset ? _uprightTimer + dt : 0f;
            if (_uprightTimer > 1.2f) Upright();
            GuardPeople(_v.Occupied || speed > 4f ? speed : 0f);
            SkidAmount = Mathf.MoveTowards(SkidAmount, skid, 4f * dt);
            Rpm01 = Mathf.Clamp01(Mathf.Abs(fwdSpeed) / Mathf.Max(1f, _s.topSpeed) * 1.15f + Mathf.Abs(drive) * 0.12f);
        }

        /// <summary>One tyre's inputs, in its own ground frame (vf along, vs sideways, m/s; forces in N).</summary>
        public struct TyreIn
        {
            public float vf, vs, load, mu, corner, dt, drive, brake;
            public bool locked, coast, tcs;
        }

        /// <summary>
        /// The arcade tyre (pure, tested): sideways it cancels this corner's slip within ~2 physics steps; along it drives
        /// (traction-controlled when <c>tcs</c>), brakes (never past a stop) and rolls; a locked tyre instead slides, its
        /// friction opposing the slide, so a handbraked rear holds little sideways and steps out. Everything stays inside
        /// the friction circle mu * load. Returns (lateral, longitudinal, saturated).
        /// </summary>
        public static (float lat, float lon, bool over) Tyre(in TyreIn t)
        {
            float maxF = Mathf.Max(0f, t.mu * t.load);
            float lat = -t.vs * t.corner / t.dt * 0.5f;
            float lon = 0f;
            if (!t.locked)
            {
                lon = t.drive;
                if (t.tcs) lon = Mathf.Clamp(lon, -maxF * 0.92f, maxF * 0.92f);
            }
            float stop = Mathf.Abs(t.vf) * t.corner / t.dt;   // never more braking than it takes to stop this step
            if (t.brake > 0f) lon -= Mathf.Sign(t.vf) * Mathf.Min(t.brake, stop);
            if (t.coast && !t.locked) lon -= Mathf.Sign(t.vf) * Mathf.Min(t.load * 0.025f + 60f, stop);
            float slide = Mathf.Sqrt(t.vf * t.vf + t.vs * t.vs);
            if (t.locked && slide > 0.6f) { lon = -t.vf / slide * maxF; lat = -t.vs / slide * maxF; }
            else if (t.locked) lon -= Mathf.Sign(t.vf) * Mathf.Min(maxF, stop);
            float total = Mathf.Sqrt(lat * lat + lon * lon);
            bool over = total > maxF;
            if (over && total > 1e-3f) { float k = maxF / total; lat *= k; lon *= k; }
            return (lat, lon, over);
        }

        private void AntiRoll()
        {
            for (int a = 0; a + 1 < _wheels.Length; a += 2)
            {
                var l = _wheels[a].left ? _wheels[a] : _wheels[a + 1];
                var r = _wheels[a].left ? _wheels[a + 1] : _wheels[a];
                if (!l.grounded && !r.grounded) continue;
                float diff = (l.compression - r.compression) * _s.antiRoll;
                var up = transform.up;
                if (l.grounded) _rb.AddForceAtPosition(up * diff, transform.TransformPoint(l.mount));
                if (r.grounded) _rb.AddForceAtPosition(-up * diff, transform.TransformPoint(r.mount));
            }
        }

        /// <summary>All wheels off the ground: ease pitch and roll toward level so ramps never become flips.</summary>
        private void AirLevel(float dt)
        {
            var up = transform.up;
            var axis = Vector3.Cross(up, Vector3.up);
            _rb.AddTorque(axis * 2.8f * _rb.mass - Vector3.ProjectOnPlane(_rb.angularVelocity, Vector3.up) * 0.6f * _rb.mass, ForceMode.Force);
        }

        /// <summary>Stuck on its side or roof and nearly still: set it back on its wheels, facing the same way.</summary>
        protected void Upright()
        {
            _uprightTimer = 0f;
            var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-3f) fwd = Vector3.ProjectOnPlane(-transform.up, Vector3.up);   // nose straight down/up
            if (fwd.sqrMagnitude < 1e-3f) fwd = Vector3.forward;
            // set down on the ground under the body (the car itself is not ground)
            var p = _rb.position;
            float y = p.y;
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 9f, GroundMask, QueryTriggerInteraction.Ignore)) y = hit.point.y;
            _rb.position = new Vector3(p.x, y + 0.3f, p.z);
            _rb.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero;
            Righted?.Invoke();
        }

        /// <summary>The car was set back on its wheels (a toast, a sound).</summary>
        public event System.Action Righted;

        // ------------------------------------------------------------------ people in the way

        private readonly Collider[] _near = new Collider[8];
        private readonly System.Collections.Generic.List<(Collider c, float until)> _passed = new System.Collections.Generic.List<(Collider, float)>();
        private Collider[] _own;
        private BoxCollider _hull;

        /// <summary>
        /// People are not walls: a car moving faster than a walk knocks a pedestrian down (NPCs and the local player ragdoll
        /// through <see cref="Knockdown"/>) and carries on, instead of stopping dead against their kinematic capsule. Other
        /// players are only passed through (their client owns their body; a hit message is a server ask).
        /// </summary>
        private void GuardPeople(float speed) => GuardPeople(speed, _rb.isKinematic ? transform.forward * speed : _rb.linearVelocity);

        /// <summary>See <see cref="GuardPeople(float)"/>; traffic on rails passes its own velocity.</summary>
        public void GuardPeople(float speed, Vector3 velocity)
        {
            for (int i = _passed.Count - 1; i >= 0; i--)
            {
                var (c, until) = _passed[i];
                if (c != null && Time.time < until) continue;
                if (c != null) foreach (var o in _own) if (o != null) Physics.IgnoreCollision(o, c, false);
                _passed.RemoveAt(i);
            }
            if (speed < 2.5f) return;
            if (_own == null)
            {
                // the body's own solid colliders (not a driver's, who rides parented inside)
                var all = GetComponentsInChildren<Collider>(true);
                var mine = new System.Collections.Generic.List<Collider>();
                foreach (var c in all) if (!c.isTrigger && c.GetComponentInParent<AvatarView>() == null) mine.Add(c);
                _own = mine.ToArray();
                _hull = GetComponent<BoxCollider>();
            }
            if (_hull == null) return;
            float dt = Time.fixedDeltaTime;
            var center = transform.TransformPoint(_hull.center) + velocity * dt;
            var half = Vector3.Scale(_hull.size, transform.lossyScale) * 0.5f + new Vector3(0.2f, 0.1f, 0.3f + speed * dt);
            int n = Physics.OverlapBoxNonAlloc(center, half, _near, transform.rotation, 1 << CharacterLayers.Characters, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = _near[i];
                if (c == null || c.transform.IsChildOf(transform)) continue;
                bool known = false;
                foreach (var p in _passed) if (p.c == c) { known = true; break; }
                if (known) continue;
                foreach (var o in _own) if (o != null) Physics.IgnoreCollision(o, c, true);
                _passed.Add((c, Time.time + 2.5f));
                var view = c.GetComponentInParent<AvatarView>();
                if (view == null) continue;
                bool npc = view.GetComponent<NpcController>() != null;
                if (!npc && !view.isLocal) continue;
                var vel = velocity;
                Knockdown.For(view).Fall(vel * 0.75f + Vector3.up * Mathf.Min(4f, speed * 0.25f), vel * (8f + speed));
                if (!_rb.isKinematic) _rb.linearVelocity = vel * 0.94f;
                PersonHit?.Invoke(view, speed);
            }
        }

        /// <summary>Someone was knocked down by this car (who, at what speed).</summary>
        public event System.Action<AvatarView, float> PersonHit;

        protected virtual void LateUpdate()
        {
            if (_wheels == null) return;
            if (Rails)
            {
                _steer = Mathf.MoveTowards(_steer, RailSteer, 140f * Time.deltaTime);
                float rest = _s.mass * 9.81f / 4f / SpringRate();
                foreach (var w in _wheels) { w.compression = rest; w.spin += RailSpeed / w.radius * Mathf.Rad2Deg * Time.deltaTime; }
            }
            foreach (var w in _wheels)
            {
                if (w.visual == null) continue;
                // suspension travel: the visual drops to the ground contact (or full droop in the air)
                float travel = w.compression - (_s.mass * 9.81f / 4f / SpringRate());
                var parentUp = w.visual.parent != null ? w.visual.parent.InverseTransformDirection(transform.up) : Vector3.up;
                var pos = w.visualRest + parentUp * (travel * (1f - WheelFold01) + w.radius * 0.45f * WheelFold01);
                w.visual.localPosition = pos;
                var steer = w.front ? Quaternion.AngleAxis(_steer * (1f - WheelFold01), parentUp) : Quaternion.identity;
                var axle = w.visual.parent != null ? w.visual.parent.InverseTransformDirection(transform.right) : Vector3.right;
                var nose = w.visual.parent != null ? w.visual.parent.InverseTransformDirection(transform.forward) : Vector3.forward;
                // Roadstar flight: the wheels fold flat under the body into hover pads
                var fold = WheelFold01 > 0f ? Quaternion.AngleAxis(WheelFold01 * 90f * (w.left ? 1f : -1f), nose) : Quaternion.identity;
                w.visual.localRotation = fold * steer * Quaternion.AngleAxis(w.spin * (1f - WheelFold01), axle) * w.visualRot;
            }
            if (_v.Steering != null)
            {
                if (!_steerRestSet) { _steerRest = _v.Steering.localRotation; _steerRestSet = true; }
                var axis = _v.Steering.parent != null && _v.SteeringCenter != null
                    ? _v.Steering.parent.InverseTransformDirection((_v.SteeringCenter.position - _v.Seat.position).normalized) : Vector3.forward;
                _v.Steering.localRotation = Quaternion.AngleAxis(-_steer * 6f, axis) * _steerRest;
            }
            if (_s.leanMax > 0f && _v.Model != null)
            {
                float lat = Vector3.Dot(_rb.linearVelocity, transform.right);
                float yawRate = Vector3.Dot(_rb.angularVelocity, transform.up);
                float lean = Mathf.Clamp(-yawRate * _rb.linearVelocity.magnitude * 1.2f, -_s.leanMax, _s.leanMax);
                _lean = Mathf.Lerp(_lean, lean, 1f - Mathf.Exp(-6f * Time.deltaTime));
                _v.Model.localRotation = Quaternion.AngleAxis(_lean, Vector3.forward);
            }
        }

        private Quaternion _steerRest; private bool _steerRestSet; private float _lean;
        /// <summary>Steering angle now (degrees, + = right): the driver's hands follow it.</summary>
        public float SteerAngle => _steer;

        private void OnCollisionEnter(Collision c)
        {
            float impulse = c.impulse.magnitude;
            if (impulse > _s.mass * 2.5f && c.contactCount > 0) Impact?.Invoke(impulse, c.GetContact(0).point);
        }
    }
}
