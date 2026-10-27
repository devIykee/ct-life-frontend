using CTLife.World;
using NUnit.Framework;

namespace CTLife.Tests
{
    /// <summary>Motion + camera pass (2026-10-26): the boom spring eases, respects solid surfaces and never snaps.</summary>
    public class CameraSpringTests
    {
        private const float Dt = 1f / 30f;

        [Test]
        public void SolidPullInIsFastButNeverDeeperThanTheLensRadius()
        {
            float boom = 4.2f, v = 0f;
            for (int i = 0; i < 3; i++)
            {
                boom = CameraRig.BoomSpring(boom, 1.5f, ref v, true, 1.5f, Dt);
                Assert.LessOrEqual(boom, 1.5f + CameraRig.CollisionRadius + 1e-4f, "the lens stays outside the wall");
            }
        }

        [Test]
        public void ADoorIntoARoomEasesInWithoutAJump()
        {
            float boom = CameraRig.OutdoorDistance, v = 0f, last = boom, maxStep = 0f;
            for (int i = 0; i < 60; i++)
            {
                boom = CameraRig.BoomSpring(boom, CameraRig.InteriorDistance, ref v, false, 10f, Dt);
                maxStep = System.Math.Max(maxStep, last - boom);
                last = boom;
            }
            Assert.AreEqual(CameraRig.InteriorDistance, boom, 0.02f, "it arrives");
            Assert.LessOrEqual(maxStep, CameraRig.BoomInSpeed * Dt + 1e-4f, "no frame moves more than the pull-in speed allows");
        }

        [Test]
        public void ComingBackOutIsSlowAndCalm()
        {
            float boom = 1.5f, v = 0f;
            boom = CameraRig.BoomSpring(boom, 4.2f, ref v, false, 4.2f, Dt);
            Assert.LessOrEqual(boom - 1.5f, CameraRig.BoomOutSpeed * Dt + 1e-4f);
        }
    
        [Test]
        public void BodyDistanceMeasuresFromTheBodyAxis()
        {
            var feet = UnityEngine.Vector3.zero;
            Assert.AreEqual(0f, CameraRig.BodyDistance(new UnityEngine.Vector3(0, 1.6f, 0), feet), 1e-4f, "inside the head");
            Assert.AreEqual(1f, CameraRig.BodyDistance(new UnityEngine.Vector3(1, 1.0f, 0), feet), 1e-4f);
            Assert.AreEqual(0.5f, CameraRig.BodyDistance(new UnityEngine.Vector3(0, CameraRig.BodyTop + 0.5f, 0), feet), 1e-4f, "above the head");
        }

        [Test]
        public void RoomFramingStartsWhenTheBoomIsSqueezedAndIsFullAgainstTheWall()
        {
            Assert.AreEqual(0f, CameraRig.RoomFraming(CameraRig.InteriorDistance, CameraRig.InteriorDistance), 1e-5f, "a free room view is the normal view");
            Assert.AreEqual(0f, CameraRig.RoomFraming(CameraRig.OutdoorDistance, CameraRig.OutdoorDistance), 1e-5f, "outdoors in the open too");
            Assert.AreEqual(1f, CameraRig.RoomFraming(0.7f, CameraRig.InteriorDistance), 1e-5f, "backed against the counter wall: fully framed");
            float half = CameraRig.RoomFraming((CameraRig.FrameFrom + CameraRig.FrameFull) * 0.5f, CameraRig.OutdoorDistance);
            Assert.That(half, Is.InRange(0.4f, 0.6f), "it blends in between");
            Assert.AreEqual(0f, CameraRig.RoomFraming(0.5f, 1.0f), 1e-5f, "a boom that only wants to be short (zoomed in) is not squeezed");
        }
    }
}
