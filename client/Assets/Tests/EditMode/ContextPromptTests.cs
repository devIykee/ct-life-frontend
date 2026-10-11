using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.UI;
using CTLife.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    public class ContextPromptTests
    {
        private static readonly Rect Safe = new Rect(18, 18, 2364, 1044);
        private static readonly Vector2 Size = new Vector2(280, 124);
        private static readonly Rect[] Controls =
        {
            new Rect(40, 28, 2320, 128), // header
            new Rect(40, 176, 280, 144), // explore / lobby
            new Rect(40, 696, 368, 368), // movement
            new Rect(1750, 848, 610, 192), // social
            new Rect(2010, 166, 350, 124), // needs
        };

        [Test]
        public void ClearObjectGetsPromptDirectlyAboveIt()
        {
            var anchor = new Vector2(1100, 600);
            Assert.IsTrue(ContextPromptPlacement.TryPlace(anchor, Size, Safe, Controls, out var r));
            Assert.AreEqual(anchor.x, r.center.x, .001f);
            Assert.AreEqual(anchor.y - ContextPromptPlacement.Gap, r.yMax, .001f);
            ClearAndContained(r, Controls);
        }

        [TestCase(42, 960)]
        [TestCase(2250, 990)]
        [TestCase(2320, 210)]
        [TestCase(180, 210)]
        [TestCase(-80, 550)]
        [TestCase(2480, 550)]
        public void EdgeObjectsAvoidMovementSocialHeaderAndNeeds(float x, float y)
        {
            Assert.IsTrue(ContextPromptPlacement.TryPlace(new Vector2(x, y), Size, Safe, Controls, out var r));
            ClearAndContained(r, Controls);
        }

        [Test]
        public void TopEdgePlacesNearbyBelowInsteadOfCoveringTheObjectOrJumpingToBottom()
        {
            var anchor = new Vector2(1200, 45);
            Assert.IsTrue(ContextPromptPlacement.TryPlace(anchor, Size, Safe, Array.Empty<Rect>(), out var r));
            Assert.AreEqual(anchor.y + ContextPromptPlacement.Gap, r.yMin, .001f);
            Assert.AreEqual(anchor.x, r.center.x, .001f);
            Assert.IsFalse(r.Contains(anchor));
        }

        [Test]
        public void OpenChatAndMicSecondaryActionsDoNotCoverExistingControls()
        {
            var obstacles = new List<Rect>(Controls) { new Rect(1450, 360, 900, 450) };
            Assert.IsTrue(ContextPromptPlacement.TryPlace(new Vector2(1700, 650), new Vector2(850, 124), Safe, obstacles, out var r));
            ClearAndContained(r, obstacles);
        }

        [Test]
        public void CameraPanChangesPlacementContinuouslyInUnobstructedScene()
        {
            Vector2? offset = null;
            Rect previous = default;
            for (int x = 900; x <= 1150; x++)
            {
                var anchor = new Vector2(x, 600);
                Assert.IsTrue(ContextPromptPlacement.TryPlace(anchor, Size, Safe, Controls, out var r, offset));
                if (offset.HasValue) Assert.AreEqual(1f, r.x - previous.x, .001f);
                ClearAndContained(r, Controls);
                offset = r.position - anchor; previous = r;
            }
        }

        [Test]
        public void NoFreeSpaceAndInvalidLayoutsHideInsteadOfCoveringButtons()
        {
            Assert.IsFalse(ContextPromptPlacement.TryPlace(new Vector2(1200, 500), Size, Safe, new[] { Safe }, out _));
            Assert.IsFalse(ContextPromptPlacement.TryPlace(new Vector2(float.NaN, 500), Size, Safe, Controls, out _));
            Assert.IsFalse(ContextPromptPlacement.TryPlace(new Vector2(1200, 500), Vector2.zero, Safe, Controls, out _));
            Assert.IsFalse(ContextPromptPlacement.TryPlace(new Vector2(1200, 500), new Vector2(3000, 124), Safe, Controls, out _));
        }

        [Test]
        public void AnchorStaysOnSelectedObjectUntilAuthoritativeSeatThenFollowsActorRoot()
        {
            var actor = new GameObject("prompt-test-actor");
            try
            {
                var m = new InteractionMachine();
                var seat = new Interactable { id = "bench", wireId = "square.bench", kind = InteractionKind.Bench,
                    position = new Vector3(4, 0, 6), approach = new Vector3(4, 0, 7) };
                var nearby = new Interactable { id = "other", position = new Vector3(20, 0, 20) };
                var offer = InteractionPromptAnchor.For(m, seat, actor.transform);
                Assert.IsTrue(offer.TryGetPosition(out var initial));
                Assert.AreEqual(InteractionPhase.Idle, m.Phase, "Constructing/projecting a prompt cannot use the seat.");
                Assert.IsFalse(offer.followsActor);
                Assert.AreEqual(seat.position + Vector3.up * .8f, initial);
                m.Use(seat, true, 0); m.Arrived(1);
                var pending = InteractionPromptAnchor.For(m, nearby, actor.transform);
                Assert.IsTrue(pending.TryGetPosition(out var requested));
                Assert.AreEqual(initial, requested, "Pending claim remains anchored to the chosen seat, not a new offer.");
                Assert.IsFalse(pending.followsActor);
                m.ServerSpot(seat.wireId, _ => seat);
                var engaged = InteractionPromptAnchor.For(m, nearby, actor.transform);
                Assert.IsTrue(engaged.followsActor);
                actor.transform.position = new Vector3(8, 0, 4);
                Assert.IsTrue(engaged.TryGetPosition(out var moved));
                Assert.AreEqual(actor.transform.position + Vector3.up * 1.65f, moved);
                UnityEngine.Object.DestroyImmediate(actor);
                Assert.IsFalse(engaged.TryGetPosition(out _), "Scene unload must not leave a stale stand button at world origin.");
            }
            finally { if (actor != null) UnityEngine.Object.DestroyImmediate(actor); }
        }

        [Test]
        public void StaleTargetOrPhaseCannotTriggerAnotherSeatOrReleaseANewClaim()
        {
            var m = new InteractionMachine();
            var a = new Interactable { id = "a", kind = InteractionKind.Seat, wireId = "v.a" };
            var b = new Interactable { id = "b", kind = InteractionKind.Seat, wireId = "v.b" };
            Assert.IsTrue(m.AcceptsPrompt("use", "a", a));
            Assert.IsFalse(m.AcceptsPrompt("use", "a", b));
            m.Use(a, true, 0);
            Assert.IsFalse(m.AcceptsPrompt("use", "a", a));
            Assert.IsTrue(m.AcceptsPrompt("cancel", "a", b));
            m.Arrived(1); m.ServerSpot(a.wireId, _ => a);
            Assert.IsTrue(m.AcceptsPrompt("stand", "a", b));
            Assert.IsFalse(m.AcceptsPrompt("cancel", "a", a));
            Assert.IsFalse(m.AcceptsPrompt("emote:anything", "a", a));
            m.StandUp(); m.Use(b, true, 2);
            Assert.IsFalse(m.AcceptsPrompt("stand", "a", b));
        }

        [Test]
        public void DisposalRemovesWorldFrameSubscriptionAndSignalUpdates()
        {
            var eventField = typeof(InteractionController).GetField("PresentationUpdated", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            int Count() => (eventField.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
            int before = Count();
            var prompt = new InteractionPrompt();
            try
            {
                Assert.AreEqual(before + 1, Count());
                prompt.Dispose(); prompt.Dispose();
                Assert.AreEqual(before, Count());
                Bus.Publish(new InteractionPromptSignal
                {
                    actions = new List<InteractionAction> { new InteractionAction("use", "interact.sit", "seat", true) },
                    anchor = new InteractionPromptAnchor(Vector3.zero), targetId = "a",
                });
                Assert.AreEqual(DisplayStyle.None, prompt.Element.style.display.value);
                Assert.IsNull(prompt.Element.Q<Button>("scene-action-use"));
            }
            finally { prompt.Dispose(); }
        }

        private static void ClearAndContained(Rect r, IReadOnlyList<Rect> obstacles)
        {
            Assert.GreaterOrEqual(r.xMin, Safe.xMin); Assert.GreaterOrEqual(r.yMin, Safe.yMin);
            Assert.LessOrEqual(r.xMax, Safe.xMax); Assert.LessOrEqual(r.yMax, Safe.yMax);
            foreach (var obstacle in obstacles)
                Assert.IsFalse(r.Overlaps(ContextPromptPlacement.Inflate(obstacle, ContextPromptPlacement.Clearance)), $"{r} overlaps {obstacle}");
        }
    }
}
