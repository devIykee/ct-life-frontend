using System.Reflection;
using CTLife.Core;
using CTLife.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.Tests
{
    public sealed class OnFootCameraTests
    {
        private const float Dt = 1f / 60f;
        private static readonly MethodInfo DirectTick = typeof(LocalPlayerController).GetMethod("UpdateDirectMovement", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo PathTick = typeof(LocalPlayerController).GetMethod("UpdatePathMovement", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo CameraTick = typeof(CameraRig).GetMethod("FollowOnFoot", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo LastOrbit = typeof(CameraRig).GetField("_lastOrbitAt", BindingFlags.Instance | BindingFlags.NonPublic);
        private GameObject _floor, _actor, _camera;
        private LocalPlayerController _player;
        private CameraRig _rig;

        [SetUp]
        public void Setup()
        {
            var origin = new Vector3(1000f, 20f, 1000f);
            _floor = new GameObject("on-foot-camera-floor");
            _floor.transform.position = origin - Vector3.up * .1f;
            _floor.AddComponent<BoxCollider>().size = new Vector3(100f, .2f, 100f);
            Physics.SyncTransforms();
            _actor = new GameObject("on-foot-camera-player");
            _actor.transform.SetPositionAndRotation(origin, Quaternion.Euler(0, 37f, 0));
            _actor.AddComponent<AvatarView>();
            _player = _actor.AddComponent<LocalPlayerController>();
            _player.Init(null, null);
            if (!_player.Body.Ready)
                typeof(CharacterBody).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_player.Body, null);
            _player.Body.Teleport(origin, _actor.transform.rotation);
            Assert.IsTrue(_player.Body.Ready, "fixture requires the real initialized CharacterController");
            _player.OpenWorld = true; // Set after Init: this isolated movement fixture does not create the vehicle system.
            _camera = new GameObject("on-foot-camera");
            _camera.AddComponent<Camera>();
            _rig = _camera.AddComponent<CameraRig>();
            if (CameraRig.Instance != _rig)
                typeof(CameraRig).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_rig, null);
            Assert.AreSame(_rig, CameraRig.Instance, "direct movement must use this fixture's camera");
            _rig.Target = _actor.transform;
            _rig.SnapToTarget();
            LastOrbit.SetValue(_rig, Time.time - 10f);
        }

        [TearDown]
        public void Cleanup()
        {
            if (_camera != null) Object.DestroyImmediate(_camera);
            if (_actor != null) Object.DestroyImmediate(_actor);
            if (_floor != null) Object.DestroyImmediate(_floor);
        }

        [TestCase(0f, -1f, DirectMovementInput.ArrowWalkStrength)]
        [TestCase(-1f, 0f, DirectMovementInput.ArrowWalkStrength)]
        [TestCase(1f, 0f, DirectMovementInput.ArrowWalkStrength)]
        [TestCase(0f, -1f, .8f)]
        [TestCase(-1f, 0f, .8f)]
        [TestCase(1f, 0f, .8f)]
        public void HeldCameraRelativeDirection_KeepsRealBodyOnOneWorldHeading(float x, float z, float strength)
        {
            float yaw = _rig.yaw;
            _rig.pitch = 23f;
            var input = new Vector2(x, z) * strength;
            var expected = Quaternion.Euler(0, yaw, 0) * new Vector3(x, 0, z);
            var side = Vector3.Cross(Vector3.up, expected);
            var start = _actor.transform.position;
            float maxSide = 0;
            for (int i = 0; i < 180; i++)
            {
                HeldStep(input);
                var displacement = _actor.transform.position - start;
                maxSide = Mathf.Max(maxSide, Mathf.Abs(Vector3.Dot(displacement, side)));
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(yaw, _rig.yaw)), Is.LessThan(.001f), "automatic camera rotation changed the held input frame at tick " + i);
                Assert.That(_rig.pitch, Is.EqualTo(23f).Within(.001f), "retain the player's chosen look during direct movement");
            }
            Assert.Greater(Vector3.Dot(_actor.transform.position - start, expected), 2f, "the real CharacterController must travel, not pass by standing still");
            Assert.Less(maxSide, .03f, "holding one direction must not curve across an empty floor");
            Assert.IsTrue(_player.Body.Grounded);
        }

        [Test]
        public void ManualOrbitWhileHeld_ChangesTheInputFrame_ThenKeepsTheNewLook()
        {
            for (int i = 0; i < 60; i++) HeldStep(Vector2.up * .8f);
            _rig.Orbit(new Vector2(300f, 40f));
            float yaw = _rig.yaw, pitch = _rig.pitch;
            LastOrbit.SetValue(_rig, Time.time - 10f); // Continue beyond the automatic-recenter delay.
            var expected = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            var start = _actor.transform.position;
            for (int i = 0; i < 180; i++) HeldStep(Vector2.up * .8f);
            var moved = _actor.transform.position - start; moved.y = 0;
            Assert.Greater(Vector3.Dot(moved.normalized, expected), .999f, "explicit orbit must steer camera-relative input");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(yaw, _rig.yaw)), Is.LessThan(.001f));
            Assert.That(_rig.pitch, Is.EqualTo(pitch).Within(.001f));
        }

        [Test]
        public void ReleaseBraking_DoesNotSwingCameraDuringTheFinalCoast()
        {
            LastOrbit.SetValue(_rig, Time.time); // Prepare a sideways jog before the recenter delay expires.
            for (int i = 0; i < 90; i++) HeldStep(Vector2.right * .8f);
            float yaw = _rig.yaw;
            _player.SetMoveInput(Vector2.zero);
            Assert.IsTrue(_player.Braking);
            LastOrbit.SetValue(_rig, Time.time - 10f);
            for (int i = 0; i < 90 && _player.DirectInputActive; i++)
            {
                DirectTick.Invoke(_player, new object[] { Dt });
                CameraTick.Invoke(_rig, new object[] { Dt });
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(yaw, _rig.yaw)), Is.LessThan(.001f), "braking is still part of direct control");
            }
            Assert.IsFalse(_player.DirectInputActive, "coast must finish normally");
        }

        [Test]
        public void IntentionalPathMovement_StillRecentersBehindTheActor()
        {
            _rig.yaw = -53f;
            var start = _actor.transform.position;
            Assert.IsTrue(_player.MoveTo(start + _actor.transform.forward * 7f));
            Assert.IsFalse(_player.DirectInputActive);
            for (int i = 0; i < 180; i++)
            {
                PathTick.Invoke(_player, new object[] { Dt });
                CameraTick.Invoke(_rig, new object[] { Dt });
            }
            Assert.Greater(Vector3.Distance(start, _actor.transform.position), 2f);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(_rig.yaw, _actor.transform.eulerAngles.y)), 12f);
        }

        [Test]
        public void ExplicitSeatSettle_StillRecentersWithoutDirectInput()
        {
            _rig.yaw = -53f;
            _rig.SettleBehind();
            for (int i = 0; i < 180; i++) CameraTick.Invoke(_rig, new object[] { Dt });
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(_rig.yaw, _actor.transform.eulerAngles.y)), 1.6f);
        }

        [Test]
        public void DirectInputTakingOverSeatSettle_DoesNotResumeTheOldCameraTurnAfterRelease()
        {
            float yaw = _rig.yaw;
            _rig.SettleBehind();
            for (int i = 0; i < 90; i++) HeldStep(Vector2.right * .8f);
            _player.SetMoveInput(Vector2.zero);
            for (int i = 0; i < 90 && _player.DirectInputActive; i++)
            {
                DirectTick.Invoke(_player, new object[] { Dt });
                CameraTick.Invoke(_rig, new object[] { Dt });
            }
            Assert.IsFalse(_player.DirectInputActive);
            for (int i = 0; i < 180; i++) CameraTick.Invoke(_rig, new object[] { Dt });
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(yaw, _rig.yaw)), Is.LessThan(.001f), "the completed thumb gesture must not revive the earlier seat settle");
        }

        [Test]
        public void CloseCollisionLift_HidesAvatarUntilTheBoomRecovers()
        {
            var visibleBody = _actor.AddComponent<MeshRenderer>();
            _rig.Target = _actor.transform; // Include the real renderer in the camera's visibility ownership.
            var wall = new GameObject("on-foot-camera-close-wall");
            try
            {
                var pivot = _actor.transform.position + Vector3.up * CameraRig.PivotHeight;
                var heading = Quaternion.Euler(0f, _rig.yaw, 0f);
                wall.transform.SetPositionAndRotation(pivot + heading * Vector3.back * .65f, heading);
                wall.AddComponent<BoxCollider>().size = new Vector3(4f, 4f, .2f);
                Physics.SyncTransforms();

                CameraTick.Invoke(_rig, new object[] { Dt });

                var cameraOffset = _camera.transform.position - pivot;
                Assert.Greater(cameraOffset.y, .6f, "the existing close-obstacle lift must still operate");
                Assert.Greater(cameraOffset.magnitude, CameraRig.HideWithin,
                    "lift places the lens outside the old pivot-distance hide test despite the shortened boom");
                Assert.IsTrue(visibleBody.forceRenderingOff, "a lifted close-up must not show the inside of the avatar");

                Object.DestroyImmediate(wall);
                Physics.SyncTransforms();
                for (int i = 0; i < 180; i++) CameraTick.Invoke(_rig, new object[] { Dt });
                Assert.IsFalse(visibleBody.forceRenderingOff, "restore the avatar when obstacle clearance returns");
                Assert.Greater(Vector3.Distance(_camera.transform.position, pivot), 3f);
            }
            finally { if (wall != null) Object.DestroyImmediate(wall); }
        }

        private void HeldStep(Vector2 input)
        {
            _player.SetMoveInput(input);
            DirectTick.Invoke(_player, new object[] { Dt });
            CameraTick.Invoke(_rig, new object[] { Dt });
        }
    }
}
