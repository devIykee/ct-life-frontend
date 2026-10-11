using System.Linq;
using CTLife.World;
using CTLife.Core;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>Phase A feel pass (2026-10-17): locomotion curves, remote jitter buffer, emote hand poses.</summary>
    public class FeelTests
    {
        [Test]
        public void Brake_SettlesARunInAboutAQuarterSecond_WithoutAStep()
        {
            float v = DirectMovementInput.RunSpeed, t = 0f, d = 0f, maxDecel = 0f;
            while (v > Locomotion.StopBelow && t < 2f)
            {
                float next = Locomotion.Brake(v, 1f / 60f);
                maxDecel = Mathf.Max(maxDecel, (v - next) * 60f);
                d += v / 60f; v = next; t += 1f / 60f;
            }
            Assert.That(t, Is.InRange(0.15f, 0.35f), "stop time");
            Assert.That(d, Is.InRange(0.3f, 0.8f), "coast distance");
            Assert.Less(maxDecel, 60f, "no instant stop (the old controller: 270 m/s^2)");
        }

        [Test]
        public void Accelerate_RampsUpAndDown()
        {
            Assert.AreEqual(14f / 60f, Locomotion.Accelerate(0f, 4.5f, 1f / 60f), 1e-4f);
            Assert.AreEqual(4.5f - 18f / 60f, Locomotion.Accelerate(4.5f, 2.2f, 1f / 60f), 1e-4f);
            Assert.AreEqual(2.2f, Locomotion.Accelerate(2.21f, 2.2f, 1f / 60f), 1e-4f);
        }

        [Test]
        public void SlideAlongEdge_KeepsTheAllowedTangent()
        {
            // Walkable is x <= 0 (a wall at x = 0): pushing north-east glides north.
            var v = Locomotion.SlideAlongEdge(new Vector3(3f, 0f, 2f), d => d.x <= 0f);
            Assert.AreEqual(new Vector3(0f, 0f, 2f), v);
            // A corner blocks both.
            Assert.AreEqual(Vector3.zero, Locomotion.SlideAlongEdge(new Vector3(3f, 0f, 2f), d => false));
            // Both allowed: the larger component wins (the main direction of travel).
            Assert.AreEqual(new Vector3(3f, 0f, 0f), Locomotion.SlideAlongEdge(new Vector3(3f, 0f, 2f), d => true));
        }

        [Test]
        public void TurnEasing_RampsInsteadOfSnapping()
        {
            float yaw = 0f, vel = 0f, prevRate = 0f, maxRateChange = 0f;
            for (int i = 0; i < 30; i++)
            {
                float next = Mathf.SmoothDampAngle(yaw, 180f, ref vel, Locomotion.TurnSmoothTime, Locomotion.MaxTurnRate, 1f / 60f);
                float rate = Mathf.Abs(Mathf.DeltaAngle(yaw, next)) * 60f;
                maxRateChange = Mathf.Max(maxRateChange, Mathf.Abs(rate - prevRate));
                prevRate = rate; yaw = next;
            }
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)) < 2f ? 1 : 0, 0, "reaches the target within half a second");
            Assert.LessOrEqual(prevRate, Locomotion.MaxTurnRate + 1f);
        }

        [Test]
        public void RemoteTimeline_DelayAdaptsToJitter()
        {
            var calm = new RemoteTimeline();
            for (int i = 0; i < 60; i++) calm.Stamp(i * 0.1);
            Assert.AreEqual(RemoteTimeline.MinDelay, calm.Delay, 0.02f, "steady 10 Hz keeps the minimum delay");
            var rough = new RemoteTimeline(); var rng = new System.Random(3); double t = 0;
            for (int i = 0; i < 120; i++) { t += 0.1 + (rng.NextDouble() - 0.5) * 0.2; rough.Stamp(t); }
            Assert.Greater(rough.Delay, calm.Delay + 0.03f, "jitter raises the delay");
            Assert.LessOrEqual(rough.Delay, RemoteTimeline.MaxDelay);
        }

        [Test]
        public void RemoteFollow_BoundsSpeed_ButSnapsTeleports()
        {
            var p = RemoteTimeline.Follow(Vector3.zero, new Vector3(1f, 0f, 0f), 3f, 1f / 60f);
            Assert.Less(p.x, 0.1f, "a 1 m jump at 3 m/s is spread over frames");
            Assert.Greater(p.x, 0.05f);
            Assert.AreEqual(new Vector3(9f, 0f, 0f), RemoteTimeline.Follow(Vector3.zero, new Vector3(9f, 0f, 0f), 3f, 1f / 60f), "a teleport snaps");
            Assert.AreEqual(new Vector3(0.02f, 0f, 0f), RemoteTimeline.Follow(Vector3.zero, new Vector3(0.02f, 0f, 0f), 3f, 1f / 60f));
        }

        [Test]
        public void HandPoses_CoverTheGesturesAndShipInTheController()
        {
            Assert.IsTrue(HandPoses.TryState("emote_thumbs_up", out var s) && s == "hand_relaxed_thumbs");
            Assert.IsTrue(HandPoses.TryState("emote_point", out s) && s == "hand_relaxed_point");
            Assert.IsTrue(HandPoses.TryState("emote_wagmi_fist_pump", out s) && s == "hand_fist_fist");
            Assert.IsFalse(HandPoses.TryState("emote_nod", out _), "head-only emotes keep the clip's hands");
            var controller = Resources.Load<RuntimeAnimatorController>("Characters/AvatarController") as UnityEditor.Animations.AnimatorController;
            Assert.IsNotNull(controller);
            var layer = controller.layers.FirstOrDefault(l => l.name == HandPoses.Layer);
            Assert.IsNotNull(layer, "Hands layer");
            Assert.AreEqual(0f, layer.defaultWeight, "off unless an emote asks for it");
            Assert.IsNotNull(layer.avatarMask);
            foreach (var combo in HandPoses.Map.Values.Distinct())
                Assert.IsTrue(layer.stateMachine.states.Any(x => x.state.name == HandPoses.StateName(combo.left, combo.right)), combo.ToString());
        }
    }
}
