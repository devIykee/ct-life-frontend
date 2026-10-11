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
        public void Brake_CoastsASprintToAStopInUnderHalfASecond_WithoutAStep()
        {
            float v = DirectMovementInput.SprintSpeed, t = 0f, d = 0f, maxDecel = 0f;
            while (v > Locomotion.StopBelow && t < 2f)
            {
                float next = Locomotion.Brake(v, 1f / 60f);
                maxDecel = Mathf.Max(maxDecel, (v - next) * 60f);
                d += v / 60f; v = next; t += 1f / 60f;
            }
            Assert.That(t, Is.InRange(0.3f, 0.55f), "stop time");
            Assert.That(d, Is.InRange(0.6f, 1.2f), "a sprint coasts about a metre");
            Assert.Less(maxDecel, 60f, "no instant stop (the old controller: 270 m/s^2)");
        }

        [Test]
        public void Accelerate_RampsUpAndDown()
        {
            Assert.AreEqual(9f / 60f, Locomotion.Accelerate(0f, 6f, 1f / 60f), 1e-4f);
            Assert.AreEqual(6f - 12f / 60f, Locomotion.Accelerate(6f, 3.5f, 1f / 60f), 1e-4f);
            Assert.AreEqual(1.6f, Locomotion.Accelerate(1.61f, 1.6f, 1f / 60f), 1e-4f);
            Assert.AreEqual(Locomotion.MaxTurnRate, Locomotion.TurnRateAt(DirectMovementInput.WalkSpeed));
            Assert.AreEqual(Locomotion.SprintTurnRate, Locomotion.TurnRateAt(DirectMovementInput.SprintSpeed));
        }

        [Test]
        public void Gaits_WalkJogAndSprintAtTheEdge_WithHysteresis()
        {
            var g = Gait.Stop;
            Assert.AreEqual(0f, DirectMovementInput.Speed(0.1f, ref g)); Assert.AreEqual(Gait.Stop, g);
            float walk = DirectMovementInput.Speed(0.3f, ref g);
            Assert.AreEqual(Gait.Walk, g); Assert.That(walk, Is.InRange(DirectMovementInput.MinWalkSpeed, DirectMovementInput.WalkSpeed));
            Assert.AreEqual(DirectMovementInput.WalkSpeed, DirectMovementInput.Speed(0.6f, ref g), 1e-4f, "the walk tops out at 1.6 m/s");
            Assert.AreEqual(Gait.Walk, g, "0.6 is still under the jog entry");
            Assert.AreEqual(DirectMovementInput.JogSpeed, DirectMovementInput.Speed(0.7f, ref g)); Assert.AreEqual(Gait.Jog, g);
            Assert.AreEqual(DirectMovementInput.JogSpeed, DirectMovementInput.Speed(0.55f, ref g), 1e-4f, "jog holds down to its exit");
            Assert.AreEqual(DirectMovementInput.SprintSpeed, DirectMovementInput.Speed(1f, ref g)); Assert.AreEqual(Gait.Sprint, g);
            Assert.AreEqual(DirectMovementInput.SprintSpeed, DirectMovementInput.Speed(0.9f, ref g), "sprint holds while near the edge");
            Assert.AreEqual(DirectMovementInput.JogSpeed, DirectMovementInput.Speed(0.8f, ref g)); Assert.AreEqual(Gait.Jog, g);
            Assert.Less(DirectMovementInput.Speed(0.4f, ref g), DirectMovementInput.WalkSpeed + 1e-4f); Assert.AreEqual(Gait.Walk, g);
            Assert.AreEqual(1.6f, DirectMovementInput.WalkSpeed); Assert.AreEqual(3.5f, DirectMovementInput.JogSpeed); Assert.AreEqual(6f, DirectMovementInput.SprintSpeed);
        }

        [Test]
        public void Falls_OnlyStartOnARealDrop_AfterCoyoteTime()
        {
            const float dt = 1f / 60f;
            // A 0.3 m kerb stepped off and back onto ground: never a fall.
            var f = new FallTracker(); f.Step(true, 1f, dt);
            float y = 1f, landed = -1f;
            for (int i = 0; i < 6; i++) { y -= 0.05f; landed = Mathf.Max(landed, f.Step(false, y, dt)); Assert.IsFalse(f.Falling, "a kerb is a step"); }
            Assert.AreEqual(-1f, f.Step(true, y, dt), "no landing reaction after a step");
            // A 0.6 m drop that touches down inside the coyote time is not a fall either.
            f = new FallTracker(); f.Step(true, 1f, dt);
            for (int i = 0; i < 10; i++) f.Step(false, 1f - 0.06f * (i + 1), dt);
            Assert.IsFalse(f.Falling, "0.17 s off the ground");
            Assert.AreEqual(-1f, f.Step(true, 0.4f, dt));
            // A 2 m ledge: falling after 0.2 s once 0.5 m down, landing reports the height.
            f = new FallTracker(); f.Step(true, 2f, dt);
            float v = 0f; y = 2f; float t = 0f; bool fell = false; float fellAt = -1f;
            while (y > 0f) { v += 9.81f * dt; y = Mathf.Max(0f, y - v * dt); t += dt; f.Step(false, y, dt); if (f.Falling && !fell) { fell = true; fellAt = t; } }
            Assert.IsTrue(fell);
            Assert.Greater(fellAt, FallTracker.CoyoteTime);
            Assert.AreEqual(2f, f.Step(true, 0f, dt), 1e-3f, "landing height");
            Assert.IsFalse(f.Falling);
        }

        [Test]
        public void FloatingStick_BaseFollowsTheThumb_AndTheEdgeIsTheSprintZone()
        {
            var c = new Vector2(100f, 100f);
            var (c1, input, knob) = CTLife.UI.MovementControls.FloatingStick(c, new Vector2(150f, 100f), 100f, 1.4f);
            Assert.AreEqual(c, c1); Assert.AreEqual(new Vector2(0.5f, 0f), input); Assert.AreEqual(new Vector2(50f, 0f), knob);
            (c1, input, _) = CTLife.UI.MovementControls.FloatingStick(c, new Vector2(100f, 0f), 100f, 1.4f);
            Assert.AreEqual(new Vector2(0f, 1f), input, "up on screen (y down) is forward");
            (c1, input, knob) = CTLife.UI.MovementControls.FloatingStick(c, new Vector2(350f, 100f), 100f, 1.4f);
            Assert.AreEqual(new Vector2(210f, 100f), c1, "the base is dragged along 1.4 radii behind the thumb");
            Assert.AreEqual(1f, input.magnitude, 1e-5f, "pushed past the edge = full = sprint");
            var g = Gait.Stop;
            Assert.AreEqual(DirectMovementInput.SprintSpeed, DirectMovementInput.Speed(input.magnitude, ref g));
        }

        [Test]
        public void ServerPace_NeverOutrunsTheRoomsSpeedCheck()
        {
            var last = new Vector3(0f, 0f, 0f);
            var paced = LocalPlayerController.PacedTowards(last, new Vector3(2f, 0.3f, 0f), 0.1f);
            Assert.AreEqual(LocalPlayerController.ServerPace * 0.1f, paced.x, 1e-4f);
            Assert.AreEqual(0.3f, paced.y, 1e-4f, "keeps the real height");
            Assert.AreEqual(new Vector3(0.3f, 0f, 0f), LocalPlayerController.PacedTowards(last, new Vector3(0.3f, 0f, 0f), 0.1f), "walking is sent as is");
            Assert.Less(LocalPlayerController.ServerPace, 6f, "the room rejects 6 m/s");
        }

        [Test]
        public void FootPlanner_WalksRoundAPropTheRouteRanInto()
        {
            // A 2 m bench-like box across the straight line, on a floor: the route goes round it, every leg walkable.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var bench = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var at = new Vector3(500f, 0f, 500f);
                floor.transform.position = at + new Vector3(0f, -0.5f, 0f); floor.transform.localScale = new Vector3(20f, 1f, 20f);
                bench.transform.position = at + new Vector3(0f, 0.45f, 0f); bench.transform.localScale = new Vector3(2f, 0.9f, 0.5f);
                Physics.SyncTransforms();
                var a = at + new Vector3(0f, 0f, -2f); var b = at + new Vector3(0f, 0f, 2f);
                Assert.IsFalse(FootPlanner.Clear(a, b), "the bench blocks the straight line");
                var route = FootPlanner.Route(a, b);
                Assert.IsNotNull(route, "a way round");
                Assert.AreEqual(b, route[route.Count - 1]);
                Assert.Greater(route.Count, 1, "at least one corner beside the bench");
                var prev = a;
                foreach (var c in route) { Assert.IsTrue(FootPlanner.Clear(prev, c), "leg " + prev + " → " + c); prev = c; }
                Assert.IsTrue(FootPlanner.Clear(a + Vector3.right * 3f, b + Vector3.right * 3f), "beside it is open");
                // The controller drops a NavMesh corner that sits inside the prop and plans round it.
                var walkable = LocalPlayerController.Walkable(a, new System.Collections.Generic.List<Vector3> { at + new Vector3(0f, 0f, 0f), b });
                Assert.IsFalse(walkable.Contains(at), "a corner inside the bench is skipped");
                prev = a;
                foreach (var c in walkable) { Assert.IsTrue(FootPlanner.Clear(prev, c)); prev = c; }
            }
            finally { Object.DestroyImmediate(floor); Object.DestroyImmediate(bench); }
        }

        [Test]
        public void Camera_RecentresBehindAMovingPlayer_OnlyAfterAPause()
        {
            Assert.AreEqual(10f, CameraRig.Recenter(10f, 90f, 3f, 0.5f, 1f / 60f), "the player is still steering the view");
            Assert.AreEqual(10f, CameraRig.Recenter(10f, 90f, 0.2f, 5f, 1f / 60f), "standing still: no swing");
            float y = 10f;
            for (int i = 0; i < 180; i++) y = CameraRig.Recenter(y, 90f, 3.5f, 5f, 1f / 60f);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(y, 90f)), 3f, "behind the jog within three seconds");
            float slow = CameraRig.Recenter(10f, 90f, 1f, 5f, 1f / 60f), fast = CameraRig.Recenter(10f, 90f, 3.5f, 5f, 1f / 60f);
            Assert.Greater(fast - 10f, slow - 10f, "faster travel swings faster");
            Assert.AreEqual(CameraRig.InteriorDistance < CameraRig.OutdoorDistance, true, "closer indoors");
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
            Assert.IsTrue(HandPoses.TryState("emote_wagmi", out s) && s == "hand_fist_fist", "keyed by the state AvatarView plays");
            Assert.IsFalse(HandPoses.TryState("emote_nod", out _), "head-only emotes keep the clip's hands");
            var controller = Resources.Load<RuntimeAnimatorController>("Characters/AvatarController") as UnityEditor.Animations.AnimatorController;
            Assert.IsNotNull(controller);
            var layer = controller.layers.FirstOrDefault(l => l.name == HandPoses.Layer);
            Assert.IsNotNull(layer, "Hands layer");
            Assert.AreEqual(0f, layer.defaultWeight, "off unless an emote asks for it");
            Assert.IsNotNull(layer.avatarMask);
            foreach (var combo in HandPoses.Map.Values.Distinct())
                Assert.IsTrue(layer.stateMachine.states.Any(x => x.state.name == HandPoses.StateName(combo.left, combo.right)), combo.ToString());
            var played = controller.layers[0].stateMachine.states.Select(x => x.state.name).ToHashSet();
            foreach (var id in HandPoses.Map.Keys)
                Assert.IsTrue(played.Contains(id), id + ": a hand pose must be keyed by a base-layer state (the id PlayOneShot plays)");
        }

        [Test]
        public void Emotes_StandOnTheGameIdle_AndTheIdleNeverRaisesTheArms()
        {
            // Feel lane F1: the baked emotes take root and legs from the relaxed idle, the gesture from the upper body.
            Assert.AreEqual(CTLife.EditorTools.EmoteBake.Group.Root, CTLife.EditorTools.EmoteBake.GroupOf("RootQ.w"));
            Assert.AreEqual(CTLife.EditorTools.EmoteBake.Group.Legs, CTLife.EditorTools.EmoteBake.GroupOf("Left Upper Leg In-Out"));
            Assert.AreEqual(CTLife.EditorTools.EmoteBake.Group.Legs, CTLife.EditorTools.EmoteBake.GroupOf("RightFootQ.y"));
            Assert.AreEqual(CTLife.EditorTools.EmoteBake.Group.Legs, CTLife.EditorTools.EmoteBake.GroupOf("Right Toes Up-Down"));
            Assert.AreEqual(CTLife.EditorTools.EmoteBake.Group.Fingers, CTLife.EditorTools.EmoteBake.GroupOf("LeftHand.Thumb.1 Stretched"));
            Assert.AreEqual(CTLife.EditorTools.EmoteBake.Group.Upper, CTLife.EditorTools.EmoteBake.GroupOf("Left Hand Down-Up"), "the wrist is the gesture's");
            Assert.AreEqual(CTLife.EditorTools.EmoteBake.Group.Upper, CTLife.EditorTools.EmoteBake.GroupOf("Right Arm Down-Up"));
            Assert.AreEqual(CTLife.EditorTools.EmoteBake.Group.Upper, CTLife.EditorTools.EmoteBake.GroupOf("Spine Front-Back"));
            Assert.AreEqual(0f, CTLife.EditorTools.EmoteBake.Weight(0f, 2f, 0.3f, 0.35f), 1e-5f);
            Assert.AreEqual(1f, CTLife.EditorTools.EmoteBake.Weight(1f, 2f, 0.3f, 0.35f), 1e-5f);
            Assert.AreEqual(0f, CTLife.EditorTools.EmoteBake.Weight(2f, 2f, 0.3f, 0.35f), 1e-5f);
            var controller = Resources.Load<RuntimeAnimatorController>("Characters/AvatarController") as UnityEditor.Animations.AnimatorController;
            var states = controller.layers[0].stateMachine.states.ToDictionary(x => x.state.name, x => x.state);
            foreach (var spec in CTLife.EditorTools.EmoteBake.Specs)
            {
                var clip = states[spec.State].motion as AnimationClip;
                Assert.IsNotNull(clip, spec.State);
                StringAssert.StartsWith(CTLife.EditorTools.EmoteBake.Folder, UnityEditor.AssetDatabase.GetAssetPath(clip), spec.State + " plays its baked clip");
                Assert.IsTrue(clip.humanMotion, spec.State + " is a Humanoid muscle clip");
            }
            foreach (var file in CTLife.EditorTools.MocapImport.GestureSources)
            {
                var path = CTLife.EditorTools.MocapImport.PathOf(file);
                var avatar = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<Animator>()?.avatar;
                Assert.IsTrue(avatar != null && avatar.isValid && avatar.isHuman, path + " has its own Humanoid avatar");
                Assert.IsTrue(CTLife.EditorTools.MocapImport.Clip(path).isHumanMotion, path);
            }
            Assert.IsFalse(CTLife.World.AvatarView.IdleVariationIds.Contains("idle_fidget_stretch"),
                "standing still must not throw both arms overhead every few seconds");
        }
    }
}
