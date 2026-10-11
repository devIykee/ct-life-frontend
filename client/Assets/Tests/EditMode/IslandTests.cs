using System.Collections.Generic;
using System.Linq;
using CTLife.EditorTools;
using CTLife.EditorTools.IslandV2;
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
        public void ZoneAt_RotatedVenue_UsesItsOwnFrame()
        {
            // a venue turned to face east (yaw 270: local -z, its door side, points to +x)
            var v = new IslandVenue { venueId = "r", origin = new Vector2(100, 0), yaw = 270f, localWalk = new Rect(-12, -10, 24, 20), localFrame = new Rect(-13, -15, 26, 26) };
            var list = new List<IslandVenue> { v };
            Assert.AreEqual("r", IslandStreaming.ZoneAt(new Vector2(100, 0), list)?.venueId);
            Assert.AreEqual("r", IslandStreaming.ZoneAt(new Vector2(109.5f, 0), list)?.venueId, "local z -9.5 lies east of the origin");
            Assert.IsNull(IslandStreaming.ZoneAt(new Vector2(111f, 0), list), "past the door line (local z -10)");
            Assert.AreEqual("r", IslandStreaming.ZoneAt(new Vector2(100, 11.5f), list)?.venueId, "local x spans 12 m north and south");
            Assert.IsNull(IslandStreaming.ZoneAt(new Vector2(100, 13f), list));
            var door = v.ToIsland(new Vector2(0, -10));
            Assert.AreEqual(110f, door.x, 1e-3f); Assert.AreEqual(0f, door.y, 1e-3f);
            Assert.Less(Vector2.Distance(new Vector2(0, -10), v.ToLocal(door)), 1e-3f);
            Assert.AreEqual(IslandStreaming.RoomAction.Leave, IslandStreaming.Decide(new Vector2(125, 0), null, v), "15 m out of the door, past the 8 m street margin");
            Assert.AreEqual(IslandStreaming.RoomAction.None, IslandStreaming.Decide(new Vector2(114, 0), null, v));
        }

        [Test]
        public void CellPlan_LoadsAroundThePlayerAndAlongThePrefetchPath()
        {
            var cells = new List<Rect> { new Rect(0, 0, 256, 256), new Rect(256, 0, 256, 256), new Rect(640, 0, 256, 256) };
            var (load, unload) = IslandStreaming.PlanCells(new Vector2(10, 10), cells, new HashSet<int>(), 300f, 420f);
            CollectionAssert.AreEqual(new[] { 0, 1 }, load);
            var path = IslandStreaming.PredictPath(new Vector3(10, 0, 10), new Vector3(40, 0, 0), 10f);
            Assert.AreEqual(10, path.Count);
            (load, _) = IslandStreaming.PlanCells(new Vector2(10, 10), cells, new HashSet<int> { 0, 1 }, 300f, 420f, path);
            CollectionAssert.Contains(load, 2, "a fast car heading east prefetches the far cell");
            (_, unload) = IslandStreaming.PlanCells(new Vector2(2000, 10), cells, new HashSet<int> { 0, 1 }, 300f, 420f);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, unload);
        }

        private static IslandHeightfield _h;
        private static IslandHeightfield H => _h ??= IslandV2Height.Load(IslandV2Paths.Out);

        [Test]
        public void Terrain_PlazaAtOrigin_RealSummit_SeaBelowSeaLevel()
        {
            Assert.AreEqual(-0.12f, H.Sample(0f, 0f), 0.15f, "the plaza pad sits at the origin height (diorama floor at y = 0)");
            Assert.AreEqual(-6f, H.Sea, 1e-3f);
            Assert.Greater(H.Sample(-180f, 495f), 150f, "the MtnDAO summit");
            Assert.Less(H.Sample(0f, -600f), H.Sea, "the sea south of the bay");
            Assert.Less(H.Sample(510f, -400f), H.Sea - 3f, "the marina basin holds deep water");
            Assert.Greater(H.Sample(-780f, 250f), H.Sea + 15f, "west cliffs");
        }

        [Test]
        public void BuiltIsland_PlacesEveryPublicVenue_OnItsPad_WithRoadsAndNavigation()
        {
            var prefab = Resources.Load<GameObject>(IslandWorld.ResourcePath);
            Assert.IsNotNull(prefab, "CT Island prefab is built");
            var w = prefab.GetComponent<IslandWorld>();
            Assert.IsNotNull(w.navigation, "the pedestrian NavMesh is referenced");
            Assert.IsNotNull(w.roads, "the road network is referenced");
            Assert.AreEqual(21, w.venues.Count);
            Assert.GreaterOrEqual(w.districts.Count, 9);
            Assert.Greater(w.cells.Count, 20);
            Assert.IsTrue(IslandWorld.Places("timeline_square") && IslandWorld.Places("terminal_cafe") && IslandWorld.Places("hackathon_house"));
            Assert.IsFalse(IslandWorld.Places("home"), "private homes stay separate visits");
            foreach (var v in w.venues)
            {
                Assert.IsTrue(v.HasLocal, v.venueId);
                Assert.IsTrue(v.localFrame.Contains(v.localWalk.center) && v.localFrame.Contains(v.ToLocal(v.exit)), v.venueId);
                Assert.AreEqual(v.elevation - 0.12f, H.Sample(v.origin.x, v.origin.y), 0.35f, v.venueId + " sits on its pad");
            }
            var ts = w.Find("timeline_square");
            Assert.AreEqual(Vector2.zero, ts.origin);
            Assert.AreEqual(0f, ts.elevation, 1e-3f);
            int cellsWithGround = 0;
            foreach (var c in w.cells)
            {
                var cp = Resources.Load<GameObject>(IslandWorld.CellFolder + "/" + c.id);
                Assert.IsNotNull(cp, c.id);
                var ground = cp.GetComponentInChildren<IslandTerrainCell>(true);
                if (ground != null && ground.chunks.Length > 0 && ground.material != null) cellsWithGround++;
                Assert.IsTrue(cp.GetComponentsInChildren<MeshFilter>().All(f => f.sharedMesh != null), c.id + " meshes are saved assets");
            }
            Assert.Greater(cellsWithGround, 20, "land cells generate their ground (and its colliders) on load");
        }
    }
}
