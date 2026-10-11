using System.Linq;
using CTLife.World.Pedestrians;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>Feel lane F6 (2026-10-18): street pedestrians on World W4's walkable network.</summary>
    public class PedestrianTests
    {
        private const string Tiny = @"{
          ""sidewalks"": [
            { ""id"": ""s1_R"", ""segment"": ""s1"", ""side"": ""right"", ""width"": 4, ""points"": [[0,0,0],[10,0,0],[20,0,0]] },
            { ""id"": ""s1_L"", ""segment"": ""s1"", ""side"": ""left"",  ""width"": 4, ""points"": [[0,0,12],[10,0,12],[20,0,12]] },
            { ""id"": ""s2_R"", ""segment"": ""s2"", ""side"": ""right"", ""width"": 3, ""points"": [[24,0,4],[24,0,20]] }
          ],
          ""corners"": [ { ""id"": ""j1_c0"", ""junction"": ""j1"", ""width"": 3, ""points"": [[21.2,0,0.5],[23,0,1.5],[23.6,0,3.0]] } ],
          ""crossings"": [ { ""id"": ""x1"", ""junction"": ""j1"", ""segment"": ""s1"", ""signal"": true, ""width"": 3.2, ""a"": [10,0,0.2], ""b"": [10,0,11.8] } ],
          ""areas"": [ { ""id"": ""plaza"", ""kind"": ""plaza"", ""y"": null, ""poly"": [[0,30],[20,30],[20,50],[0,50]] } ]
        }";

        [Test]
        public void Paths_SnapCornersToSidewalksAndAttachCrossings()
        {
            var net = PedestrianPaths.Parse(Tiny);
            Assert.AreEqual(4, net.Paths.Count);
            var s1 = net.Paths.First(p => p.id == "s1_R");
            var corner = net.Paths.First(p => p.id == "j1_c0");
            var s2 = net.Paths.First(p => p.id == "s2_R");
            Assert.AreEqual(s1.endNode, corner.startNode, "a corner end 1.3 m off the sidewalk end joins it");
            Assert.AreEqual(corner.endNode, s2.startNode);
            Assert.AreEqual(20f, s1.Length, 1e-3f);
            var x = net.Crossings.Single();
            Assert.AreEqual(net.Paths.IndexOf(s1), x.pathA);
            Assert.AreEqual(net.Paths.IndexOf(net.Paths.First(p => p.id == "s1_L")), x.pathB);
            Assert.AreEqual(10f, x.sA, 0.05f);
            Assert.IsTrue(s1.exits.Any(e => e.crossing == 0 && e.atA), "the crossing leaves from both sidewalks");
            // a walker at s1's far end turns the corner (the only other path there)
            var rng = new System.Random(1);
            Assert.AreEqual(net.Paths.IndexOf(corner), net.NextPath(s1.endNode, net.Paths.IndexOf(s1), rng));
            Assert.AreEqual(net.Paths.IndexOf(s2), net.NextPath(s2.endNode, net.Paths.IndexOf(s2), rng), "a dead end turns back");
            var plaza = net.Areas.Single();
            Assert.IsNull(plaza.y, "null y: sample the ground");
            Assert.IsTrue(plaza.Contains(10f, 40f)); Assert.IsFalse(plaza.Contains(25f, 40f));
            var mid = s1.Sample(15f, out var dir);
            Assert.AreEqual(new Vector3(15f, 0f, 0f), mid); Assert.AreEqual(Vector3.right, dir);
        }

        [Test]
        public void Paths_TheIslandNetworkIsConnected()
        {
            var net = PedestrianPaths.Island;
            Assert.IsNotNull(net, "World W4's pedestrian_paths.json");
            Assert.AreEqual(222 + 161, net.Paths.Count, "every sidewalk and corner");
            Assert.Greater(net.TotalLength, 15000f);
            Assert.Greater(net.Crossings.Count, 150, "crossings attach to sidewalks at both ends");
            int joined = net.NodePaths.Count(n => n.Count >= 2);
            Assert.Greater(joined / (float)net.Nodes.Count, 0.8f, "most path ends meet another path");
            Assert.AreEqual(10, net.Areas.Count);
        }

        [Test]
        public void System_HowManyAndWhere()
        {
            Assert.AreEqual(0, PedestrianSystem.TargetFor(10, 14), "a lane in the hills: nobody");
            Assert.AreEqual(14, PedestrianSystem.TargetFor(1000, 14), "downtown: the cap");
            Assert.AreEqual(6, PedestrianSystem.TargetFor(90, 24));
            Assert.IsTrue(PedestrianSystem.SpawnDistanceOk(35f, false), "behind the camera");
            Assert.IsFalse(PedestrianSystem.SpawnDistanceOk(35f, true), "no popping in view");
            Assert.IsFalse(PedestrianSystem.SpawnDistanceOk(15f, false), "never right beside the player");
            Assert.IsTrue(PedestrianSystem.SpawnDistanceOk(80f, true));
            Assert.Less(PedestrianSystem.SpawnMax, PedestrianSystem.DespawnBeyond);
        }
    }
}
