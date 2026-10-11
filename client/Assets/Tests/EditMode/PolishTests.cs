using CTLife.World;
using CTLife.World.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>Feel lane F5 (2026-10-18): the camera sees past a planter, traffic near the player, the demo's road pilot.</summary>
    public class PolishTests
    {
        [Test]
        public void Camera_ComesInFrontOfAPlanterThatHidesTheBody()
        {
            // The player on open ground, a waist-high planter 2 m behind them, the lens low behind it (the F1 device clip).
            var planter = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                planter.layer = 13;   // World v2 Prop
                var feet = new Vector3(800f, 0f, 800f);
                planter.transform.position = feet + new Vector3(0f, 0.6f, -2.2f);
                planter.transform.localScale = new Vector3(4f, 1.6f, 0.6f);
                Physics.SyncTransforms();
                var pivot = feet + Vector3.up * CameraRig.PivotHeight;
                var back = Quaternion.Euler(-12f, 0f, 0f) * Vector3.back;   // looking up at the player from below the pivot
                float boom = CameraRig.VisibleBoom(pivot, back, 5f, feet, CameraRig.OcclusionMask);
                Assert.Less(boom, 2.2f, "the lens moves in front of the planter");
                Assert.Greater(boom, 0.4f);
                var lens = pivot + back * boom;
                var chest = feet + Vector3.up * 1.15f;
                Assert.IsFalse(Physics.Linecast(chest, lens, CameraRig.OcclusionMask, QueryTriggerInteraction.Ignore), "the body is in sight from there");
                // From above the planter the body is in sight already: the boom stays.
                var high = Quaternion.Euler(30f, 0f, 0f) * Vector3.back;
                Assert.AreEqual(5f, CameraRig.VisibleBoom(pivot, high, 5f, feet, CameraRig.OcclusionMask), 1e-4f);
            }
            finally { Object.DestroyImmediate(planter); }
        }

        [Test]
        public void Traffic_AppearsCloseOnlyOutOfView()
        {
            Assert.IsTrue(TrafficSystem.SpawnDistanceOk(40f, false), "behind the camera, close by");
            Assert.IsFalse(TrafficSystem.SpawnDistanceOk(20f, false), "never right beside the player");
            Assert.IsFalse(TrafficSystem.SpawnDistanceOk(90f, true), "no pop-in in view");
            Assert.IsTrue(TrafficSystem.SpawnDistanceOk(160f, true), "far in view is fine");
            Assert.IsFalse(TrafficSystem.SpawnDistanceOk(TrafficSystem.SpawnMax + 1f, false));
            Assert.Greater(TrafficDrivers.AttachWithin, TrafficSystem.SpawnHiddenMin, "cars that appear near can get a driver");
        }

        [Test]
        public void RoadPilot_SteersTowardTheLaneAndHoldsItsSpeed()
        {
            var spec = new VehicleSpec();
            Assert.AreEqual(0f, VehicleDemo.RoadPilot.PursuitSteer(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 12f), 10f, spec), 1e-4f);
            Assert.Greater(VehicleDemo.RoadPilot.PursuitSteer(Vector3.zero, Vector3.forward, new Vector3(3f, 0f, 10f), 10f, spec), 0f, "target right: steer right");
            Assert.Less(VehicleDemo.RoadPilot.PursuitSteer(Vector3.zero, Vector3.forward, new Vector3(-3f, 0f, 10f), 10f, spec), 0f);
            Assert.AreEqual(1f, VehicleDemo.RoadPilot.PursuitSteer(Vector3.zero, Vector3.forward, new Vector3(8f, 0f, 0.5f), 2f, spec), 1e-4f, "a sharp turn: full lock");
            var go = VehicleDemo.RoadPilot.Pedals(5f, 15f, 0f);
            Assert.Greater(go.throttle, 0f); Assert.AreEqual(0f, go.brake);
            var slow = VehicleDemo.RoadPilot.Pedals(18f, 8f, 0f);
            Assert.AreEqual(0f, slow.throttle); Assert.Greater(slow.brake, 0f);
            var stop = VehicleDemo.RoadPilot.Pedals(0.5f, 0f, 0f);
            Assert.AreEqual(1f, stop.brake); Assert.IsTrue(stop.handbrake, "held at a standstill");
        }
    }
}
