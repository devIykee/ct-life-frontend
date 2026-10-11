using System.Collections.Generic;
using CTLife.UI;
using CTLife.World;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    public class NpcSpawnPlannerTests
    {
        // Timeline Square (spots.json fe.8): entrance (0, -10.9) facing +z; NPC/standing spots.
        private static readonly Vector2 Entrance = new Vector2(0f, -10.9f);
        private static readonly Vector2 Forward = NpcSpawnPlanner.Forward(0f);
        private static List<(string, Vector2)> Timeline() => new List<(string, Vector2)>
        {
            ("crier_1", new Vector2(-8f, 8.6f)), ("crier_2", new Vector2(8f, 8.6f)), ("crier_3", new Vector2(0f, 11f)),
            ("square_center", new Vector2(0f, -7f)), ("pixel_wall", new Vector2(-9.6f, 6.2f)), ("billboard_view", new Vector2(6.2f, -9.6f)),
        };

        [Test]
        public void SpotStraightAheadOfTheSpawnIsNeverAHome()
        {
            // square_center is 3.9 m ahead on the camera's centre line: the spot that hid the player on arrival.
            Assert.IsFalse(NpcSpawnPlanner.Allowed(new Vector2(0f, -7f), Entrance, Forward));
            Assert.IsFalse(NpcSpawnPlanner.Allowed(Entrance + new Vector2(0.5f, 0.5f), Entrance, Forward), "too close");
            Assert.IsTrue(NpcSpawnPlanner.Allowed(new Vector2(6.2f, -9.6f), Entrance, Forward), "beside the spawn, off the line");
            Assert.IsTrue(NpcSpawnPlanner.Allowed(new Vector2(0f, 11f), Entrance, Forward), "far beyond the centre-line strip");
        }

        [Test]
        public void FiveTimelineNpcsGetFiveDistinctAllowedHomes()
        {
            var taken = new HashSet<string>();
            foreach (var npc in new[] { "npc-0-town_crier", "npc-1-giveaway_guy", "npc-2-reply_guy", "npc-3-doomposter", "npc-4-spaces_host" })
            {
                var pick = NpcSpawnPlanner.Pick(npc, Timeline(), Entrance, Forward, taken);
                Assert.IsNotNull(pick, npc);
                Assert.AreNotEqual("square_center", pick);
                Assert.IsTrue(taken.Add(pick), npc + " shares " + pick);
            }
            Assert.IsNull(NpcSpawnPlanner.Pick("npc-5-extra", Timeline(), Entrance, Forward, taken), "all five allowed spots are taken");
        }

        [Test]
        public void ChoiceIsStablePerNpc()
        {
            var a = NpcSpawnPlanner.Pick("npc-1-giveaway_guy", Timeline(), Entrance, Forward, null);
            var shuffled = Timeline();
            shuffled.Reverse();
            Assert.AreEqual(a, NpcSpawnPlanner.Pick("npc-1-giveaway_guy", shuffled, Entrance, Forward, null));
        }

        [Test]
        public void RingFallbackIsBesideTheSpawnNeverOnIt()
        {
            for (int i = 0; i < 8; i++)
            {
                var p = NpcSpawnPlanner.Ring(i, Entrance, Forward);
                Assert.IsTrue(NpcSpawnPlanner.Allowed(p, Entrance, Forward), "ring " + i + " at " + p);
                for (int j = 0; j < i; j++) Assert.Greater((p - NpcSpawnPlanner.Ring(j, Entrance, Forward)).magnitude, 0.9f);
            }
        }
    }

    public class BubbleStackerTests
    {
        private readonly List<Rect> _placed = new List<Rect>();

        [Test]
        public void SeparateBubblesKeepTheirPlace()
        {
            var wanted = new[] { new Rect(0, 100, 120, 40), new Rect(400, 100, 120, 40) };
            BubbleStacker.Resolve(wanted, _placed);
            Assert.AreEqual(wanted[0], _placed[0]);
            Assert.AreEqual(wanted[1], _placed[1]);
        }

        [Test]
        public void TwoSpeakersOnOneSpotStackWithAGap()
        {
            var wanted = new[] { new Rect(100, 300, 200, 50), new Rect(110, 310, 180, 44) };
            BubbleStacker.Resolve(wanted, _placed);
            Assert.AreEqual(wanted[1], _placed[1], "the lower bubble (bottom edge 354) keeps its place");
            Assert.AreEqual(310f - BubbleStacker.Gap - 50f, _placed[0].y, 1e-3);
            Assert.IsFalse(BubbleStacker.Overlaps(_placed[0], _placed[1]));
        }

        [Test]
        public void ThreeOverlappingBubblesNeverTouch()
        {
            var wanted = new[] { new Rect(100, 300, 200, 50), new Rect(150, 310, 200, 50), new Rect(120, 290, 160, 60) };
            BubbleStacker.Resolve(wanted, _placed);
            for (int i = 0; i < 3; i++)
            for (int j = i + 1; j < 3; j++)
                Assert.IsFalse(BubbleStacker.Overlaps(_placed[i], _placed[j]), $"{i} vs {j}: {_placed[i]} {_placed[j]}");
            for (int i = 0; i < 3; i++) Assert.LessOrEqual(_placed[i].y, wanted[i].y, "bubbles only move up");
        }
    }
}
