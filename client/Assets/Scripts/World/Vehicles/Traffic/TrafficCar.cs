using CTLife.World.Roads;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// One AI car (Feel lane F3), on a pooled vehicle prefab. On rails it is a kinematic body that
    /// <see cref="TrafficSystem"/> moves along its <see cref="Route"/>: cheap, smooth, exactly in lane. Hit by something
    /// physical (the player's car, a crashed car), it is knocked: it becomes a real rigidbody that slides to a stop, its
    /// driver honks, and the system takes it away once nobody is looking.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public sealed class TrafficCar : MonoBehaviour
    {
        public Vehicle Vehicle { get; private set; }
        public Rigidbody Body { get; private set; }
        public CarPhysics Car { get; private set; }
        public VehicleAudio Audio { get; private set; }
        public readonly TrafficRoute Route = new TrafficRoute();
        public Collider[] Colliders { get; private set; }

        /// <summary>Speed along the route (m/s).</summary>
        public float Speed;
        public float HalfLength = 2.2f, HalfWidth = 0.95f;
        /// <summary>This driver's pace against the limit (0.85 .. 1.05).</summary>
        public float Pace = 1f;
        /// <summary>Sideways offset from the lane (m, + = right) while changing lanes; eases back to 0.</summary>
        public float Lateral;
        public bool Knocked { get; private set; }
        public float KnockedAt { get; private set; }
        public float SpawnedAt;
        public AvatarView Driver;

        // what the system worked out last step (for the review capture and the HUD-less debug overlay)
        public float LastAccel, LastGap = float.PositiveInfinity;
        public string LastReason = "";

        // junction state
        internal RoadIntersection StoppedAt;      // the stop sign it has come to rest at
        internal float StoppedFor;
        internal bool StopDone;
        internal float WaitingSince = -1f;
        internal bool Yielding;
        internal RoadIntersection Inside;         // the junction it is crossing now
        // reacting to the player
        internal float BlockedFor, HonkAt, LaneChangeAt;
        internal float SensorGap = float.PositiveInfinity, SensorSpeed, SensorAt = -10f;
        internal bool SensorPlayer;
        internal int Slot;

        public string Kind => Vehicle != null ? Vehicle.specId : "";

        public void Bind()
        {
            Vehicle = GetComponent<Vehicle>();
            Body = GetComponent<Rigidbody>();
            Car = GetComponent<CarPhysics>();
            Audio = GetComponent<VehicleAudio>();
            Colliders = GetComponentsInChildren<Collider>(true);
            var box = GetComponent<BoxCollider>();
            if (box != null) { HalfLength = box.size.z * 0.5f; HalfWidth = box.size.x * 0.5f; }
        }

        /// <summary>Back on rails (a fresh spawn or out of the pool).</summary>
        public void ToRails()
        {
            Knocked = false;
            if (Body != null)
            {
                if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
                Body.isKinematic = true;
                Body.interpolation = RigidbodyInterpolation.Interpolate;
            }
            if (Car != null) Car.SetRails(true);
            if (Vehicle != null) { Vehicle.EngineOn = true; Vehicle.Input = VehicleInput.None; }
            Lateral = 0f; BlockedFor = 0f; StoppedAt = null; StopDone = false; Inside = null; WaitingSince = -1f;
            SensorGap = float.PositiveInfinity; SensorAt = -10f; SensorPlayer = false;
        }

        /// <summary>Something physical hit it: a real rigidbody from now on, carrying on at its rail speed.</summary>
        public void Knock()
        {
            if (Knocked || Body == null) return;
            Knocked = true; KnockedAt = Time.time;
            var v = transform.forward * Speed;
            Body.isKinematic = false;
            Body.linearVelocity = v;
            if (Car != null) Car.SetRails(false);
            if (Vehicle != null) { Vehicle.EngineOn = false; Vehicle.Input = VehicleInput.None; }
            Speed = 0f;
            HonkAt = Time.time + 0.7f;   // the driver leans on the horn a moment later
        }

        private void OnCollisionEnter(Collision c)
        {
            if (Knocked || c.rigidbody == null || c.rigidbody.isKinematic) return;
            if (c.relativeVelocity.magnitude > 2.2f) Knock();
        }
    }
}
