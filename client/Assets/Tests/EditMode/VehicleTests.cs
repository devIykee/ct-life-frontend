using System.Collections.Generic;
using CTLife.UI;
using CTLife.World;
using CTLife.World.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>Feel lane F2 (2026-10-17): the arcade tyre, the vehicle roster, parking and the HUD sticks.</summary>
    public class VehicleTests
    {
        private static CarPhysics.TyreIn Tyre(float vf, float vs, float mu = 1.25f, bool locked = false, float drive = 0f, float brake = 0f, bool tcs = true) => new CarPhysics.TyreIn
        {
            vf = vf, vs = vs, load = 3400f, mu = mu, corner = 325f, dt = 0.02f, drive = drive, brake = brake, locked = locked,
            coast = drive == 0f && brake == 0f, tcs = tcs,
        };

        [Test]
        public void Tyre_RollingTyre_CancelsASmallSlipWithinItsGrip()
        {
            var (lat, lon, over) = CarPhysics.Tyre(Tyre(20f, 0.2f));
            Assert.IsFalse(over, "a small slip is held");
            Assert.AreEqual(-0.2f * 325f / 0.02f * 0.5f, lat, 1f, "pushes back against the slip");
            Assert.Less(lon, 0f, "coasting rolls the speed off a little");
        }

        [Test]
        public void Tyre_LockedRear_SlidesSoItHoldsLittleSideways()
        {
            // the handbrake: the rear at its drift grip, sliding mostly forward (5 degrees of slip)
            float vf = 20f, vs = 20f * Mathf.Tan(5f * Mathf.Deg2Rad), maxF = 0.4f * 3400f;
            var (lat, lon, over) = CarPhysics.Tyre(Tyre(vf, vs, mu: 0.4f, locked: true));
            Assert.AreEqual(maxF, Mathf.Sqrt(lat * lat + lon * lon), 1f, "a sliding tyre uses all its friction");
            Assert.Less(Mathf.Abs(lat), maxF * 0.1f, "barely any of it sideways, so the rear steps out");
            Assert.Less(lon, -maxF * 0.9f, "the rest brakes against the slide");
            var rolling = CarPhysics.Tyre(Tyre(vf, vs));
            Assert.Greater(Mathf.Abs(rolling.lat), Mathf.Abs(lat) * 10f, "a rolling rear holds the same slip ten times harder");
        }

        [Test]
        public void Tyre_TractionControl_KeepsTheDriveInsideTheGrip()
        {
            float maxF = 1.25f * 3400f;
            var (_, lon, _) = CarPhysics.Tyre(Tyre(5f, 0f, drive: 9000f));
            Assert.LessOrEqual(lon, maxF * 0.92f + 1e-3f, "traction control: no wheelspin on the road");
            var loose = CarPhysics.Tyre(Tyre(5f, 0.6f, drive: 9000f, tcs: false));
            Assert.IsTrue(loose.over, "without it (drifting) the throttle saturates the tyre: power oversteer");
        }

        [Test]
        public void Tyre_Brakes_NeverPushPastAStop()
        {
            var (_, lon, _) = CarPhysics.Tyre(Tyre(0.1f, 0f, brake: 5000f));
            Assert.LessOrEqual(Mathf.Abs(lon), 0.1f * 325f / 0.02f + 1e-3f, "no more braking than stopping this step takes");
            Assert.LessOrEqual(lon, 0f, "and never backwards");
        }

        [Test]
        public void Tyre_ForcesAlwaysStayInsideTheFrictionCircle()
        {
            var rng = new System.Random(7);
            for (int i = 0; i < 2000; i++)
            {
                float R(float a) => (float)(rng.NextDouble() * 2 - 1) * a;
                bool locked = rng.Next(3) == 0;
                float mu = 0.3f + (float)rng.NextDouble();
                var (lat, lon, _) = CarPhysics.Tyre(Tyre(R(60f), R(15f), mu, locked, R(9000f), Mathf.Abs(R(5000f)), rng.Next(2) == 0));
                Assert.LessOrEqual(Mathf.Sqrt(lat * lat + lon * lon), mu * 3400f + 0.5f);
            }
        }

        [Test]
        public void Catalog_EveryVehicleHasAPrefabWithTheRightParts()
        {
            foreach (var kv in VehicleCatalog.Specs)
            {
                var s = kv.Value;
                Assert.AreEqual(kv.Key, s.id);
                Assert.Greater(s.mass, 0f, s.id);
                var prefab = Resources.Load<GameObject>("Vehicles/" + s.id);
                Assert.IsNotNull(prefab, "Resources/Vehicles/" + s.id);
                var v = prefab.GetComponent<Vehicle>();
                Assert.IsNotNull(v, s.id + " Vehicle");
                Assert.AreEqual(s.id, v.specId);
                Assert.IsNotNull(prefab.GetComponent<Rigidbody>(), s.id + " Rigidbody");
                Assert.IsNotNull(prefab.GetComponent<VehicleAudio>(), s.id + " audio");
                bool flies = VehicleCatalog.Flies(s.kind) || s.kind == VehicleKind.Roadstar;
                Assert.AreEqual(flies, prefab.GetComponent<FlightPhysics>() != null, s.id + " flight physics");
                Assert.AreEqual(!VehicleCatalog.Flies(s.kind), prefab.GetComponent<CarPhysics>() != null, s.id + " road physics");
                var names = new HashSet<string>();
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) names.Add(t.name);
                if (s.kind == VehicleKind.Car || s.kind == VehicleKind.Roadstar)
                    foreach (var part in new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR", "Door_FL", "Seat_Driver", "Entry_Driver", "SteeringCenter" })
                        Assert.IsTrue(names.Contains(part), s.id + " " + part);
                if (s.kind == VehicleKind.Scooter) foreach (var part in new[] { "Wheel_F", "Wheel_R", "Seat_Driver", "Handlebar" }) Assert.IsTrue(names.Contains(part), s.id + " " + part);
                if (s.kind == VehicleKind.Helicopter) Assert.IsTrue(names.Contains("Rotor_Main") && names.Contains("Seat_Driver"), s.id);
                if (s.kind == VehicleKind.Drone) Assert.IsTrue(names.Contains("Rotor_FL") && names.Contains("Rotor_BR"), s.id);
                foreach (var clip in new[] { s.engineSound, s.startSound })
                    if (!string.IsNullOrEmpty(clip)) Assert.IsNotNull(Resources.Load<AudioClip>("Vehicles/Audio/" + clip), s.id + " sound " + clip);
                // the body sits on the vehicle layers, so wheel rays and the camera can tell it from the world
                int layer = prefab.GetComponent<Collider>().gameObject.layer;
                Assert.IsTrue(layer == CharacterLayers.Vehicle || layer == CharacterLayers.VehicleFlight, s.id + " layer");
            }
        }

        [Test]
        public void ClassBits_MatchTheWorldParkingContract()
        {
            // World v2 contract: VehicleSpawn bays carry a class mask compact | suv | sports | moped | roadstar
            Assert.AreEqual(1, VehicleWorld.ClassBit("compact"));
            Assert.AreEqual(2, VehicleWorld.ClassBit("suv"));
            Assert.AreEqual(4, VehicleWorld.ClassBit("sports"));
            Assert.AreEqual(8, VehicleWorld.ClassBit("moped"));
            Assert.AreEqual(16, VehicleWorld.ClassBit("roadstar"));
            foreach (var s in VehicleCatalog.Specs.Values)
                if (!VehicleCatalog.Flies(s.kind)) Assert.Greater(VehicleWorld.ClassBit(s.spawnClass), 0, s.id + " parks in a bay class");
        }

        [Test]
        public void Spawner_ParksTheDriversDoorTowardThePlayer()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(200f, 1f, 200f);
                Physics.SyncTransforms();
                var around = Vector3.zero;
                bool found = VehicleSpawner.TryClearGround(around, Vector3.forward, new Vector3(2.5f, 1.9f, 5f), new List<Vector3>(), 6f, 34f, out var pos, out var rot);
                Assert.IsTrue(found, "flat open ground has a spot");
                var driverSide = rot * Vector3.left;   // the left (-X) door
                var toPlayer = Vector3.ProjectOnPlane(around - pos, Vector3.up).normalized;
                Assert.Greater(Vector3.Dot(driverSide, toPlayer), 0.7f, "the driver's door faces the player");
            }
            finally { Object.DestroyImmediate(ground); }
        }

        [Test]
        public void Sticks_DeadZoneThenFullRange()
        {
            Assert.AreEqual(0f, VehicleControls.Dead(0.1f));
            Assert.AreEqual(0f, VehicleControls.Dead(-0.12f), 1e-6f);
            Assert.AreEqual(1f, VehicleControls.Dead(1f), 1e-6f);
            Assert.AreEqual(-1f, VehicleControls.Dead(-1f), 1e-6f);
            Assert.AreEqual(0.5f, VehicleControls.Dead(0.56f), 1e-4f);
        }
    }
}
