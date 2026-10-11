using System.Collections.Generic;
using System.Linq;
using CTLife.EditorTools;
using CTLife.World;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>Phase B open world (2026-10-17): zones, streaming, room handoff, terrain relief, the built island.</summary>
    public class IslandTests
    {
        private static IslandVenue V(string id, float x, float z) => new IslandVenue
        {
            venueId = id, origin = new Vector2(x, z), frame = new Rect(x - 16, z - 14, 32, 28), walk = new Rect(x - 12, z - 10, 24, 20), exit = new Vector2(x, z - 9),
        };

        private static readonly List<IslandVenue> Two = new List<IslandVenue> { V("a", 0, 0), V("b", 38, 0) };

        [Test]
        public void ZoneAt_IsTheVenueUnderFoot_AndNullOnTheStreet()
        {
            Assert.AreEqual("a", IslandStreaming.ZoneAt(new Vector2(0, 0), Two).venueId);
            Assert.AreEqual("b", IslandStreaming.ZoneAt(new Vector2(40, 5), Two).venueId);
            Assert.IsNull(IslandStreaming.ZoneAt(new Vector2(19, 0), Two), "between the venues is the street");
            Assert.AreEqual("a", IslandStreaming.ZoneAt(new Vector2(0, -10.2f), Two).venueId, "the margin keeps the door mat in the venue");
        }

        [Test]
        public void Streaming_LoadsNearestFirst_WithHysteresis()
        {
            var venues = new List<IslandVenue> { V("a", 0, 0), V("b", 50, 0), V("c", 200, 0) };
            var (load, unload) = IslandStreaming.Plan(new Vector2(0, 0), venues, new HashSet<string>());
            CollectionAssert.AreEqual(new[] { "a", "b" }, load.Select(v => v.venueId), "near ones, nearest first; the far one waits");
            CollectionAssert.IsEmpty(unload);
            // b loaded, player walks off: kept inside the unload radius, released beyond it.
            Assert.IsEmpty(IslandStreaming.Plan(new Vector2(-10, 0), venues, new HashSet<string> { "a", "b" }).unload);
            CollectionAssert.AreEqual(new[] { "b" }, IslandStreaming.Plan(new Vector2(-50, 0), venues, new HashSet<string> { "a", "b" }).unload);
        }

        [Test]
        public void Handoff_JoinsOnEntering_AndLeavesOnlyFarOnTheStreet()
        {
            var a = Two[0]; var b = Two[1];
            Assert.AreEqual(IslandStreaming.RoomAction.Join, IslandStreaming.Decide(new Vector2(38, 0), b, a), "walked into b: join b (a leaves after)");
            Assert.AreEqual(IslandStreaming.RoomAction.None, IslandStreaming.Decide(new Vector2(0, 0), a, a));
            Assert.AreEqual(IslandStreaming.RoomAction.None, IslandStreaming.Decide(new Vector2(0, -14), null, a), "a step outside keeps the room");
            Assert.AreEqual(IslandStreaming.RoomAction.Leave, IslandStreaming.Decide(new Vector2(0, -30), null, a), "far down the street: leave");
            Assert.AreEqual(IslandStreaming.RoomAction.None, IslandStreaming.Decide(new Vector2(0, -30), null, null));
        }

        [Test]
        public void Terrain_TownIsFlat_VenuesSitFlush_HillsAndSeaAreReal()
        {
            var frames = new List<Rect> { new Rect(-16, -14, 32, 28) };
            Assert.AreEqual(0f, IslandBuilder.GroundHeight(new Vector2(72, 72), frames), 0.01f, "the town is level");
            Assert.AreEqual(-0.12f, IslandBuilder.GroundHeight(new Vector2(0, 0), frames), 0.01f, "a venue frame is sunk so its own floor sits flush");
            float north = Enumerable.Range(-20, 30).Select(i => IslandBuilder.GroundHeight(new Vector2(i * 8f, 262f), frames)).Max();
            Assert.Greater(north, 10f, "mountains on the north edge");
            Assert.Less(IslandBuilder.GroundHeight(new Vector2(0, 299), frames), IslandBuilder.Water, "the sea is below the water line");
        }

        [Test]
        public void BuiltIsland_PlacesEveryPublicVenue_WithNavigation()
        {
            var prefab = Resources.Load<GameObject>(IslandWorld.ResourcePath);
            Assert.IsNotNull(prefab, "CT Island prefab is built");
            var w = prefab.GetComponent<IslandWorld>();
            Assert.IsNotNull(w.navigation, "the island NavMesh is referenced (not an unsaved object)");
            Assert.AreEqual(21, w.venues.Count);
            Assert.AreEqual(9, w.districts.Count);
            Assert.IsTrue(IslandWorld.Places("timeline_square") && IslandWorld.Places("terminal_cafe"));
            Assert.IsFalse(IslandWorld.Places("home"), "private homes stay separate visits");
            foreach (var v in w.venues) Assert.IsTrue(v.frame.Contains(v.walk.center) && v.frame.Contains(v.exit), v.venueId);
            Assert.IsNotNull(prefab.GetComponentInChildren<Terrain>().terrainData, "terrain data is a saved asset");
        }
    }
}
