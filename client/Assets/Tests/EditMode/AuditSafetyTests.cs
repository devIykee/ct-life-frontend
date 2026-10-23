using CTLife.World;
using CTLife.World.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    public class AuditSafetyTests
    {
        [Test] public void Access_FreshPlayerCanUsePublicRoadVehiclesOnly()
        {
            Assert.IsTrue(VehicleSystem.AccessAllowed(VehicleKind.Car, false, false));
            Assert.IsTrue(VehicleSystem.AccessAllowed(VehicleKind.Scooter, false, false));
            Assert.IsFalse(VehicleSystem.AccessAllowed(VehicleKind.Roadstar, false, false));
            Assert.IsFalse(VehicleSystem.AccessAllowed(VehicleKind.Helicopter, false, false));
            Assert.IsFalse(VehicleSystem.AccessAllowed(VehicleKind.Drone, false, false));
        }

        [Test] public void Access_FlyerEntitlementUnlocksTheHovercarNotTheHelicopter()
        {
            Assert.IsTrue(VehicleSystem.AccessAllowed(VehicleKind.Drone, true, false));
            // the owned flyer is the hovercar (Roadstar slot, 2026-10-21); the helicopter stays premium
            Assert.IsTrue(VehicleSystem.AccessAllowed(VehicleKind.Roadstar, true, false));
            Assert.IsFalse(VehicleSystem.AccessAllowed(VehicleKind.Helicopter, true, false));
        }

        [Test] public void Access_ExplicitOfflineShowcaseAllowsTheTestFleet()
        {
            foreach (VehicleKind kind in System.Enum.GetValues(typeof(VehicleKind)))
                Assert.IsTrue(VehicleSystem.AccessAllowed(kind, false, true));
        }

        [Test] public void ReconnectedLease_RestoresFrozenCarAfterFailedRescue()
        {
            Assert.IsFalse(VehicleNetworkBridge.ShouldRestoreSimulation(false, true, true, false, false));
            Assert.IsTrue(VehicleNetworkBridge.ShouldRestoreSimulation(true, true, true, false, false));
        }

        [Test] public void ReconnectedLease_KeepsCarFrozenThroughoutEntryAndRecovery()
        {
            Assert.IsFalse(VehicleNetworkBridge.ShouldRestoreSimulation(true, true, true, true, false));
            Assert.IsFalse(VehicleNetworkBridge.ShouldRestoreSimulation(true, true, true, false, true));
            Assert.IsFalse(VehicleNetworkBridge.ShouldRestoreSimulation(true, true, true, true, true));
        }

        [Test] public void ReconnectedLease_CannotRestoreAClaimNowOwnedBySomeoneElse()
        {
            Assert.IsFalse(VehicleNetworkBridge.ShouldRestoreSimulation(true, false, true, false, false));
            Assert.IsFalse(VehicleNetworkBridge.ShouldRestoreSimulation(false, false, true, false, false));
        }

        private GameObject _floor, _obstacle;
        [TearDown] public void Cleanup()
        {
            if (_floor != null) Object.DestroyImmediate(_floor);
            if (_obstacle != null) Object.DestroyImmediate(_obstacle);
        }

        private void Floor(float angle = 0f)
        {
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.transform.localScale = new Vector3(20f, 0.2f, 20f);
            _floor.transform.SetPositionAndRotation(new Vector3(0f, -0.1f, 0f), Quaternion.Euler(0f, 0f, angle));
            Physics.SyncTransforms();
        }

        [Test] public void Exit_EmptyAirIsNotSafe()
        { Assert.IsFalse(PlayerSafety.TrySafeGround(new Vector3(10000f, 10000f, 10000f), 1.2f, out _)); }

        [Test] public void Exit_FlatDryFloorWithHeadroomIsSafe()
        { Floor(); Assert.IsTrue(PlayerSafety.TrySafeGround(Vector3.zero, 1.2f, out var p)); Assert.AreEqual(0f, p.y, 0.02f); }

        [Test] public void Exit_SteepFaceIsRefused()
        { Floor(48f); Assert.IsFalse(PlayerSafety.TrySafeGround(Vector3.zero, 1.2f, out _)); }

        [Test] public void Exit_FloorTooFarBelowVehicleIsRefused()
        { Floor(); Assert.IsFalse(PlayerSafety.TrySafeGround(Vector3.up * 2f, 1.2f, out _)); }

        [Test] public void Exit_HeadroomObstructionIsRefused()
        {
            Floor(); _obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _obstacle.transform.position = new Vector3(0f, 1.3f, 0f);
            _obstacle.transform.localScale = new Vector3(2f, 0.2f, 2f);
            Physics.SyncTransforms();
            Assert.IsFalse(PlayerSafety.TrySafeGround(Vector3.zero, 1.2f, out _));
        }

        [Test] public void DriverRecovery_UsesCurrentDryFloorBeforeDistantSavedPoint()
        {
            Floor();
            Assert.IsTrue(PlayerSafety.TryDriverRecoveryTarget(Vector3.zero, true, new Vector3(1000f, 0f, 0f), out var target));
            Assert.That(Vector3.Distance(target, Vector3.zero), Is.LessThan(0.05f));
        }

        [Test] public void DriverRecovery_DoesNotInventGroundWhenNoNearbyOrSavedFloorExists()
        {
            Assert.IsFalse(PlayerSafety.TryDriverRecoveryTarget(new Vector3(10000f, 10000f, 10000f), false, Vector3.zero, out _));
        }

        [Test] public void DriverRecovery_OnlinePolicyRefusesDistantSavedFallback()
        {
            Floor();
            Assert.IsFalse(PlayerSafety.TryDriverRecoveryTarget(new Vector3(10000f, 10000f, 10000f), true, Vector3.zero, out _, false));
        }

        [Test] public void Flight_DryMissingColliderNeverBecomesWater()
        { Assert.IsFalse(FlightPhysics.WaterIsSurface(float.NaN, float.NaN)); Assert.IsFalse(FlightPhysics.WaterIsSurface(80f, float.NaN)); }

        [Test] public void Flight_LakeWinsOverBedButBridgeWinsOverLake()
        { Assert.IsTrue(FlightPhysics.WaterIsSurface(20f, 25f)); Assert.IsFalse(FlightPhysics.WaterIsSurface(27f, 25f)); }

        [Test] public void SimpleFlight_ReleasedStickBrakesAndRestHovers()
        {
            var a = FlightPhysics.SimpleAcceleration(Vector3.zero, new Vector3(6f, 0f, 0f), 20f);
            Assert.Less(a.x, 0f); Assert.AreEqual(0f, a.y);
            Assert.AreEqual(Vector3.zero, FlightPhysics.SimpleAcceleration(Vector3.zero, Vector3.zero, 20f));
        }

        [Test] public void SimpleFlight_DesiredMovementIsWorldRelativeAndAccelerationBounded()
        {
            var a = FlightPhysics.SimpleAcceleration(Vector3.right, Vector3.zero, 30f);
            Assert.Greater(a.x, 0f); Assert.AreEqual(0f, a.z); Assert.LessOrEqual(a.magnitude, 6.001f);
        }

        [Test] public void Rescue_SplashDoesNotRescueButSustainedSubmersionDoes()
        {
            var state = new PlayerSafetyState();
            Assert.IsFalse(state.Step(true, false, true, true, false, 0.6f));
            Assert.IsFalse(state.Step(false, false, true, true, false, 0.1f));
            Assert.IsFalse(state.Step(true, false, true, true, false, 0.7f));
            Assert.IsTrue(state.Step(true, false, true, true, false, 0.6f));
        }

        [Test] public void Rescue_FallThroughIsImmediateButMovingFallIsNotStuck()
        {
            var state = new PlayerSafetyState();
            Assert.IsFalse(state.Step(false, false, false, false, false, 8f));
            Assert.IsTrue(state.Step(false, true, false, false, false, 0.01f));
        }

        [Test] public void Rescue_SuspendedStationaryFallEventuallyRecovers()
        {
            var state = new PlayerSafetyState();
            Assert.IsFalse(state.Step(false, false, false, true, false, 3f));
            Assert.IsTrue(state.Step(false, false, false, true, false, 1.1f));
        }

        [Test] public void Traffic_StaticPropStopsSweptKinematicCar()
        {
            _obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _obstacle.layer = 13;
            _obstacle.transform.position = new Vector3(0f, 1f, 4f);
            _obstacle.transform.localScale = new Vector3(2f, 2f, 2f);
            Physics.SyncTransforms();
            float gap = TrafficSystem.StaticClearance(Vector3.zero, Quaternion.identity, 0.9f, 2f, 10f);
            Assert.That(gap, Is.InRange(0.5f, 1.5f));
            Assert.IsTrue(float.IsPositiveInfinity(TrafficSystem.StaticClearance(new Vector3(5f, 0f, 0f), Quaternion.identity, 0.9f, 2f, 10f)));
        }
    }
}
