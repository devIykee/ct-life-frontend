using System.Reflection;
using CTLife.UI;
using CTLife.World;
using CTLife.World.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    public class FlightCameraTests
    {
        private static readonly MethodInfo Chase = typeof(CameraRig).GetMethod("ChaseVehicle", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo LastOrbit = typeof(CameraRig).GetField("_lastOrbitAt", BindingFlags.Instance | BindingFlags.NonPublic);

        [Test]
        public void FlightOrbit_UsesTheSameScreenDirectionAndGainAtDifferentPanelScales()
        {
            var oneToOne = WorldInputLayer.OrbitPixels(new Vector2(100f, -50f), 2400f, 2400f);
            var scaled = WorldInputLayer.OrbitPixels(new Vector2(50f, -25f), 1200f, 2400f);
            Assert.AreEqual(new Vector2(100f, 50f), oneToOne, "dragging up supplies positive camera-up pixels");
            Assert.AreEqual(oneToOne, scaled, "equal physical drags orbit by equal angles");
        }

        [TestCase("heli", 0f, -1f)]
        [TestCase("heli", 1f, 0f)]
        [TestCase("drone", 0f, -1f)]
        [TestCase("drone", -1f, 0f)]
        public void SimpleFlight_HeldDirectionDoesNotRotateItsOwnCameraFrame(string id, float x, float z)
        {
            WithCamera(id, false, (rig, vehicle) =>
            {
                const float initialYaw = 37f;
                rig.yaw = initialYaw;
                var stick = new Vector3(x, 0f, z);
                var intended = Quaternion.Euler(0f, initialYaw, 0f) * stick;
                // The aircraft turns toward the requested motion. Its chase camera must not then rotate the
                // camera-relative input on the next frame: holding backward/sideways should keep one heading.
                for (int i = 0; i < 180; i++)
                {
                    var movement = Quaternion.Euler(0f, rig.yaw, 0f) * stick;
                    vehicle.transform.rotation = Quaternion.RotateTowards(vehicle.transform.rotation,
                        Quaternion.LookRotation(movement), 90f / 60f);
                    Chase.Invoke(rig, new object[] { 1f / 60f });
                    Assert.Greater(Vector3.Dot(intended, Quaternion.Euler(0f, rig.yaw, 0f) * stick), .999f,
                        "the held stick changed world direction without a camera gesture at frame " + i);
                }
                rig.Orbit(new Vector2(100f, 50f));
                float manualYaw = rig.yaw, manualPitch = rig.pitch;
                LastOrbit.SetValue(rig, Time.time - 10f);
                for (int i = 0; i < 180; i++) Chase.Invoke(rig, new object[] { 1f / 60f });
                Assert.AreEqual(manualYaw, rig.yaw, .001f, "manual orbit remains the input frame after the idle delay");
                Assert.AreEqual(manualPitch, rig.pitch, .001f, "manual flight look is retained");
            });
        }

        [TestCase("heli", true)]
        [TestCase("drone", true)]
        [TestCase("roadstar", false)]
        [TestCase("compact", false)]
        public void VehicleRelativeControls_StillRecenterBehindTheVehicle(string id, bool pro)
        {
            WithCamera(id, pro, (rig, vehicle) =>
            {
                rig.yaw = 0f;
                vehicle.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                for (int i = 0; i < 180; i++) Chase.Invoke(rig, new object[] { 1f / 60f });
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(rig.yaw, 90f)), 1f);
            });
        }

        private static void WithCamera(string id, bool pro, System.Action<CameraRig, Vehicle> test)
        {
            const string key = "ctlife.flight.pro";
            bool had = PlayerPrefs.HasKey(key); int old = PlayerPrefs.GetInt(key);
            var actor = new GameObject("flight-camera-test");
            var cameraObject = new GameObject("flight-camera-test-camera");
            try
            {
                PlayerPrefs.SetInt(key, pro ? 1 : 0);
                actor.transform.position = new Vector3(10000f, 100f, 10000f);
                var vehicle = actor.AddComponent<Vehicle>(); vehicle.specId = id; vehicle.Bind();
                vehicle.Flying = id == "roadstar";
                var rig = cameraObject.AddComponent<CameraRig>(); rig.FollowVehicle(vehicle);
                LastOrbit.SetValue(rig, Time.time - 10f);
                test(rig, vehicle);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(actor);
                if (had) PlayerPrefs.SetInt(key, old); else PlayerPrefs.DeleteKey(key);
            }
        }
    }
}
