using CTLife.UI;
using CTLife.World;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>Phase A lost-player fix (2026-10-17): arrival clear of the door, prompts never over the player.</summary>
    public class VenueArrivalTests
    {
        [Test]
        public void Arrival_StepsInsideUntilClearOfTheDoor()
        {
            var entrance = new Vector3(0, 0, -9f);
            var exit = new Vector3(0, 0, -11.9f);
            var p = VenueArrival.Step(entrance, Vector3.forward, new[] { exit, entrance }, 2.6f, v => v);
            Assert.GreaterOrEqual(Vector3.Distance(p, entrance), 2.6f + VenueArrival.DoorClearance - 1e-3f, "clear of the entrance door range");
            Assert.Greater(p.z, entrance.z, "inside the venue, along the entrance facing");
            Assert.AreEqual(0f, p.x, 1e-4f);
        }

        [Test]
        public void Arrival_SkipsPointsOffTheNavmesh_AndFallsBackToTheEntrance()
        {
            var entrance = Vector3.zero;
            // Nothing valid beyond 1 m (e.g. a wall right inside): fall back to the entrance itself.
            var p = VenueArrival.Step(entrance, Vector3.right, new[] { entrance }, 2.6f, v => v.x <= 1f ? v : (Vector3?)null);
            Assert.AreEqual(entrance, p);
            // A blocked strip at 3 m is skipped; the next valid clear point wins.
            var q = VenueArrival.Step(entrance, Vector3.right, new[] { entrance }, 2.6f, v => Mathf.Abs(v.x - 3.5f) < 0.01f ? (Vector3?)null : v);
            Assert.AreEqual(4f, q.x, 1e-4f);
            // Navmesh snapping is honoured (the snapped point is returned).
            var r = VenueArrival.Step(entrance, Vector3.forward, new[] { entrance }, 1f, v => v + Vector3.up * 0.2f);
            Assert.AreEqual(0.2f, r.y, 1e-4f);
        }

        [Test]
        public void PlayerBox_CoversFeetToHead()
        {
            var box = InteractionPrompt.PlayerBox(new Vector2(500, 700), new Vector2(504, 500));
            Assert.AreEqual(500f, box.yMin, 1e-3f);
            Assert.AreEqual(700f, box.yMax, 1e-3f);
            Assert.IsTrue(box.Contains(new Vector2(502, 600)));
            Assert.AreEqual(200f * 0.56f, box.width, 0.5f);
            Assert.IsFalse(box.Overlaps(new Rect(600, 550, 80, 40)), "a prompt beside the player is fine");
        }

        [Test]
        public void PromptPlacement_AvoidsThePlayerBox()
        {
            var player = InteractionPrompt.PlayerBox(new Vector2(1200, 760), new Vector2(1200, 520));
            var anchor = new Vector2(1200, 700);     // the door, right behind the player's legs
            Assert.IsTrue(ContextPromptPlacement.TryPlace(anchor, new Vector2(220, 112), new Rect(0, 0, 2400, 1080), new[] { player }, out var placed));
            Assert.IsFalse(placed.Overlaps(ContextPromptPlacement.Inflate(player, ContextPromptPlacement.Clearance - 0.5f)), "the prompt is placed off the character");
        }

        [Test]
        public void YouRing_IsATransparentShippedMaterial()
        {
            var m = Resources.Load<Material>("World/YouRing");
            Assert.IsNotNull(m);
            Assert.IsTrue(m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));
            Assert.AreEqual(3000, m.renderQueue);
        }
    }
}
