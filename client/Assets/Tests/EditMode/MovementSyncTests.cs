using CTLife.Core;
using CTLife.UI;
using CTLife.World;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>
    /// Seeker on Railway (2026-10-10): bunched run steps were refused as SPEED_EXCEEDED and the avatar snapped back, and the
    /// arrow-mode caption covered the down arrow. The venue server keeps the last accepted position after a refusal, so the
    /// client walks its published position back up in steps under the 6 m/s cap (0.6 m per 100 ms).
    /// </summary>
    public class MovementSyncTests
    {
        [Test]
        public void CatchUpStep_LimitsEachStepAndArrives()
        {
            var from = Vector2.zero;
            var to = new Vector2(1.2f, 0f);
            var step = LocalPlayerController.CatchUpStep(from, to, LocalPlayerController.CatchUpStepMeters, out bool reached);
            Assert.IsFalse(reached);
            Assert.AreEqual(LocalPlayerController.CatchUpStepMeters, step.magnitude, 1e-4f);
            step = LocalPlayerController.CatchUpStep(step, to, LocalPlayerController.CatchUpStepMeters, out reached);
            Assert.IsFalse(reached);
            step = LocalPlayerController.CatchUpStep(step, to, LocalPlayerController.CatchUpStepMeters, out reached);
            Assert.IsTrue(reached);
            Assert.AreEqual(to, step);
        }

        [Test]
        public void CatchUpStep_StaysUnderServerSpeedCap()
        {
            // Server: 6 m/s, measured over at least the 90 ms move spacing it accepts.
            Assert.Less(LocalPlayerController.CatchUpStepMeters / 0.09f, 6f);
        }

        [Test]
        public void CatchUpStep_ZeroGapIsReached()
        {
            var p = new Vector2(3f, -2f);
            var step = LocalPlayerController.CatchUpStep(p, p, LocalPlayerController.CatchUpStepMeters, out bool reached);
            Assert.IsTrue(reached);
            Assert.AreEqual(p, step);
        }

        [Test]
        public void ArrowMode_MarksControlsSoCaptionClearsDownArrow()
        {
            var previous = ControlSettings.MovementMode;
            var controls = new MovementControls();
            try
            {
                ControlSettings.MovementMode = MovementControlMode.Arrows;
                Assert.IsTrue(controls.Element.ClassListContains("ct-move-arrow-mode"));
                ControlSettings.MovementMode = MovementControlMode.Analog;
                Assert.IsFalse(controls.Element.ClassListContains("ct-move-arrow-mode"));
            }
            finally
            {
                controls.Dispose();
                ControlSettings.MovementMode = previous;
            }
        }
    }
}
