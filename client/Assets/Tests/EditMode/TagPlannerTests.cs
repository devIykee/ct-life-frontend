using System.Collections.Generic;
using System.Linq;
using CTLife.UI;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    public class TagPlannerTests
    {
        private static readonly Vector2 Panel = new Vector2(2400, 1080);
        private static readonly Vector2 Size = new Vector2(160, 34);

        private static TagCandidate C(int key, float x, float y, float dist, bool npc = false, bool friend = false, bool talking = false,
            bool faced = false, bool local = false) =>
            new TagCandidate { Key = key, Anchor = new Vector2(x, y), Distance = dist, Size = Size, OnScreen = true, IsNpc = npc,
                IsFriend = friend, Talking = talking, Faced = faced, IsLocal = local };

        private static List<TagPlacement> Plan(params TagCandidate[] c)
        {
            var output = new List<TagPlacement>();
            TagPlanner.Plan(c, Panel, output);
            return output;
        }

        private static HashSet<int> Visible(List<TagPlacement> p) => new HashSet<int>(p.Where(x => x.Visible).Select(x => x.Key));

        /// <summary>A spread of positions that never overlap each other or the centre (left and right strips, rows apart).</summary>
        private static Vector2 Spot(int i) => new Vector2(i % 2 == 0 ? 300 + (i / 2) * 200 : 1700 + (i / 2) * 120, 200 + (i % 5) * 120);

        [Test]
        public void OwnTagIsNeverShown()
        {
            var p = Plan(C(1, 300, 300, 0f, local: true), C(2, 600, 300, 3f));
            Assert.IsFalse(Visible(p).Contains(1));
            Assert.IsTrue(Visible(p).Contains(2));
        }

        [Test]
        public void OnlyTheNearestSixPlusSpeakersFriendsAndFacedNpcs()
        {
            var list = new List<TagCandidate>();
            for (int i = 0; i < 12; i++) { var s = Spot(i); list.Add(C(10 + i, s.x, s.y, 2f + i)); }
            var far = Spot(12); list.Add(C(100, far.x, far.y + 40, 15f, talking: true));
            var friend = Spot(13); list.Add(C(101, friend.x, friend.y + 40, 14f, friend: true));
            var npc = Spot(14); list.Add(C(102, npc.x, npc.y + 40, 13f, npc: true, faced: true));
            var notFaced = Spot(15); list.Add(C(103, notFaced.x, notFaced.y + 40, 12.5f, npc: true));
            var v = Visible(Plan(list.ToArray()));
            CollectionAssert.IsSubsetOf(new[] { 100, 101, 102 }, v);
            Assert.IsFalse(v.Contains(103), "an NPC you are not facing counts as a nearest-N candidate, and it is not among the 6 nearest");
            Assert.AreEqual(6, v.Count(k => k >= 10 && k < 22), "exactly the six nearest ordinary players");
            CollectionAssert.IsSubsetOf(new[] { 10, 11, 12, 13, 14, 15 }, v);
        }

        [Test]
        public void TagsBeyondRangeOrOffScreenAreHidden()
        {
            var off = C(2, 300, 300, 3f); off.OnScreen = false;
            var v = Visible(Plan(C(1, 300, 300, TagPlanner.MaxDistance + 0.5f), off));
            Assert.IsEmpty(v);
        }

        [Test]
        public void OverlappingTagIsPushedUpOnceThenTheLowerPriorityIsHidden()
        {
            var p = Plan(C(1, 400, 400, 5f), C(2, 410, 402, 6f), C(3, 405, 401, 7f), C(4, 400, 400, 8f, talking: true));
            var v = Visible(p);
            Assert.IsTrue(v.Contains(4), "the speaker wins the spot");
            Assert.IsTrue(v.Contains(1), "the next is pushed up one row");
            Assert.IsFalse(v.Contains(2) || v.Contains(3), "no room left: lower priority is hidden");
            var speaker = p.First(x => x.Key == 4); var pushed = p.First(x => x.Key == 1);
            Assert.Less(pushed.TopLeft.y, speaker.TopLeft.y);
            Assert.Less(pushed.Alpha, 1f);
        }

        [Test]
        public void TheCentreOfTheScreenIsNeverCovered()
        {
            var zone = TagPlanner.CenterZone(Panel);
            var mid = new Vector2(Panel.x * 0.5f, Panel.y * 0.55f);
            var p = Plan(C(1, mid.x, mid.y, 2f), C(2, mid.x - 40, mid.y, 3f, talking: true));
            Assert.IsFalse(p.First(x => x.Key == 1).Visible, "an ordinary tag over the centre is hidden");
            var speaker = p.First(x => x.Key == 2);
            Assert.IsTrue(speaker.Visible, "a speaker's tag is pushed out of the centre instead");
            Assert.IsFalse(new Rect(speaker.TopLeft, Size).Overlaps(zone));
        }

        [Test]
        public void AlphaFadesWithDistance()
        {
            Assert.AreEqual(1f, TagPlanner.AlphaFor(4f));
            Assert.AreEqual(TagPlanner.MinAlpha, TagPlanner.AlphaFor(TagPlanner.MaxDistance), 1e-4);
            float mid = TagPlanner.AlphaFor(11f);
            Assert.Less(mid, 1f);
            Assert.Greater(mid, TagPlanner.MinAlpha);
        }

        [Test]
        public void PriorityOrderIsSpeakerFriendFacedNpcThenEveryoneElse()
        {
            Assert.AreEqual(0, TagPlanner.Priority(C(1, 0, 0, 1, talking: true, friend: true)));
            Assert.AreEqual(1, TagPlanner.Priority(C(1, 0, 0, 1, friend: true)));
            Assert.AreEqual(2, TagPlanner.Priority(C(1, 0, 0, 1, npc: true, faced: true)));
            Assert.AreEqual(3, TagPlanner.Priority(C(1, 0, 0, 1, npc: true)));
            Assert.AreEqual(3, TagPlanner.Priority(C(1, 0, 0, 1)));
        }
    }
}
