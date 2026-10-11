using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// A drivable or flyable vehicle (Feel lane F2). Holds the rigged parts found by name in the imported model
    /// (art/pipeline/veh_build.py: Body, Wheel_FL/FR/RL/RR or Wheel_F/R, Door_FL/FR, Steering, Rotor_*, and the anchors
    /// Seat_Driver, Entry_Driver, SteeringCenter, Exhaust), the driver, the input and the door animation. The physics
    /// component on the same object (CarPhysics / FlightPhysics) reads <see cref="Input"/> every FixedUpdate.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Vehicle : MonoBehaviour
    {
        public string specId;
        public VehicleSpec Spec { get; private set; }
        public VehicleKind Kind => Spec != null ? Spec.kind : VehicleKind.Car;
        public Rigidbody Body { get; private set; }

        public Transform Model { get; private set; }
        public readonly Transform[] Wheels = new Transform[4];     // FL, FR, RL, RR (scooter: F in 0, R in 2)
        public Transform DoorLeft { get; private set; }            // Unity -X (driver side, right-hand traffic)
        public Transform DoorRight { get; private set; }
        public Transform Steering { get; private set; }
        public Transform Seat { get; private set; }
        public Transform Entry { get; private set; }
        public Transform SteeringCenter { get; private set; }
        public Transform Exhaust { get; private set; }
        public readonly List<Transform> Rotors = new List<Transform>();

        public VehicleInput Input;
        /// <summary>The avatar driving (null when parked).</summary>
        public AvatarView Driver { get; set; }
        public bool Occupied => Driver != null;
        /// <summary>Flying (the Roadstar's flight mode, or any airborne heli/drone).</summary>
        public bool Flying { get; set; }
        /// <summary>Engine on (rotor spin-up, the hum).</summary>
        public bool EngineOn { get; set; }

        public float Speed => Body != null ? Body.linearVelocity.magnitude : 0f;
        public float ForwardSpeed => Body != null ? Vector3.Dot(Body.linearVelocity, transform.forward) : 0f;

        private Collider[] _colliders = System.Array.Empty<Collider>();
        private bool _flightLayer;

        /// <summary>The body's own colliders move to the flight layer in the air (over coast walls) and back on the road.</summary>
        public void SetFlightLayer(bool flight)
        {
            if (_flightLayer == flight && _colliders.Length > 0) return;
            _flightLayer = flight;
            foreach (var c in _colliders) if (c != null) c.gameObject.layer = flight ? CharacterLayers.VehicleFlight : CharacterLayers.Vehicle;
        }

        private Quaternion _doorLeftRest, _doorRightRest;
        private float _doorLeft, _doorRight, _doorLeftTarget, _doorRightTarget;
        public const float DoorAngle = 68f, DoorSpeed = 3.2f;

        private void Awake() => Bind();

        /// <summary>Find the rigged parts (idempotent; the editor prefab builder calls it too).</summary>
        public void Bind()
        {
            Spec = VehicleCatalog.Get(specId);
            Body = GetComponent<Rigidbody>();
            Model = transform.Find("Model") ?? transform;
            var parts = new Dictionary<string, Transform>(System.StringComparer.Ordinal);
            foreach (var t in Model.GetComponentsInChildren<Transform>(true)) if (!parts.ContainsKey(t.name)) parts[t.name] = t;
            Transform P(string n) => parts.TryGetValue(n, out var t) ? t : null;
            Wheels[0] = P("Wheel_FL") ?? P("Wheel_F"); Wheels[1] = P("Wheel_FR");
            Wheels[2] = P("Wheel_RL") ?? P("Wheel_R"); Wheels[3] = P("Wheel_RR");
            DoorLeft = P("Door_FL"); DoorRight = P("Door_FR");
            Steering = P("Steering"); Seat = P("Seat_Driver"); Entry = P("Entry_Driver");
            SteeringCenter = P("SteeringCenter"); Exhaust = P("Exhaust");
            Rotors.Clear();
            foreach (var kv in parts) if (kv.Key.StartsWith("Rotor_", System.StringComparison.Ordinal)) Rotors.Add(kv.Value);
            if (DoorLeft != null) _doorLeftRest = DoorLeft.localRotation;
            if (DoorRight != null) _doorRightRest = DoorRight.localRotation;
            _colliders = GetComponentsInChildren<Collider>(true);
            CharacterLayers.EnsureVehicleRules();
            if (Spec != null && Application.isPlaying) SetFlightLayer(VehicleCatalog.Flies(Spec.kind));
        }

        /// <summary>Open (1) or close (0) the driver's door (the left one); the other door stays shut.</summary>
        public void SetDoor(bool open) => _doorLeftTarget = open ? 1f : 0f;
        public bool DoorMoving => !Mathf.Approximately(_doorLeft, _doorLeftTarget);
        public float DoorOpen => _doorLeft;

        private void Update()
        {
            if (DoorLeft != null && !Mathf.Approximately(_doorLeft, _doorLeftTarget))
            {
                _doorLeft = Mathf.MoveTowards(_doorLeft, _doorLeftTarget, DoorSpeed * Time.deltaTime);
                float e = _doorLeft * _doorLeft * (3f - 2f * _doorLeft);
                // The left door (on -X) hinges at its front edge: positive yaw swings its rear edge out to the left.
                DoorLeft.localRotation = Quaternion.AngleAxis(DoorAngle * e, DoorAxis(DoorLeft)) * _doorLeftRest;
            }
            if (DoorRight != null && !Mathf.Approximately(_doorRight, _doorRightTarget))
            {
                _doorRight = Mathf.MoveTowards(_doorRight, _doorRightTarget, DoorSpeed * Time.deltaTime);
                float e = _doorRight * _doorRight * (3f - 2f * _doorRight);
                DoorRight.localRotation = Quaternion.AngleAxis(-DoorAngle * e, DoorAxis(DoorRight)) * _doorRightRest;
            }
        }

        /// <summary>The vehicle's up axis in the door's parent space (doors turn about the car's vertical).</summary>
        private Vector3 DoorAxis(Transform door) => door.parent != null ? door.parent.InverseTransformDirection(transform.up) : Vector3.up;

        /// <summary>Where the driver stands to get in: beside the driver's door, on the ground.</summary>
        public Vector3 EntryPoint
        {
            get
            {
                var p = Entry != null ? Entry.position : transform.position - transform.right * 1.6f;
                if (CharacterBody.GroundAt(p, out float y)) p.y = y;
                return p;
            }
        }
    }
}
