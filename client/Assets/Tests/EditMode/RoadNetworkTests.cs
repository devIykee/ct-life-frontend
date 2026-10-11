using System.Collections.Generic;
using System.Linq;
using CTLife.World.Roads;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>CT Island v2 road graph (World lane): contract shape, lane continuity, runtime API.</summary>
    public class RoadNetworkTests
    {
        private static RoadNetwork _net;
        private static RoadNetwork Net => _net != null ? _net : (_net = AssetDatabase.LoadAssetAtPath<RoadNetwork>(RoadNetwork.AssetPath));

        [Test]
        public void Network_HasTheContractShape()
        {
            Assert.IsNotNull(Net, "RoadNetwork.asset is built");
            Assert.Greater(Net.nodes.Count, 50);
            Assert.Greater(Net.segments.Count, 100);
            Assert.Greater(Net.intersections.Count, 40);
            var nodes = new HashSet<string>(Net.nodes.Select(n => n.id));
            foreach (var s in Net.segments)
            {
                Assert.GreaterOrEqual(s.samples.Length, 2, s.id);
                Assert.GreaterOrEqual(s.controlPoints.Length, 2, s.id);
                Assert.IsTrue(nodes.Contains(s.fromNode) && nodes.Contains(s.toNode), s.id + " ends on known nodes");
                Assert.Greater(s.speedLimit, 0f, s.id);
                Assert.AreEqual(3.3f, s.laneWidth, 1e-3f, s.id);
                Assert.Greater(s.LaneCount, 0, s.id);
                Assert.AreEqual(s.oneWay, s.lanesBack == 0, s.id);
            }
            foreach (var k in new[] { RoadKind.Highway, RoadKind.Avenue, RoadKind.Street, RoadKind.Mountain, RoadKind.Service, RoadKind.Bridge, RoadKind.Tunnel })
                Assert.IsTrue(Net.segments.Any(s => s.kind == k), "has a " + k + " segment");
            Assert.IsTrue(Net.intersections.Any(x => x.kind == IntersectionKind.Signal));
            Assert.IsTrue(Net.intersections.Any(x => x.kind == IntersectionKind.Roundabout && x.nodeIds.Length >= 3));
        }

        [Test]
        public void EveryLane_ContinuesFromTheNodeItEndsAt()
        {
            int lanes = 0, links = 0;
            foreach (var s in Net.segments)
                for (int lane = 0; lane < s.LaneCount; lane++)
                {
                    lanes++;
                    var next = Net.NextSegments(s.id, lane);
                    Assert.IsNotEmpty(next, $"{s.id}#{lane} ({s.name}) has somewhere to go");
                    var line = Net.LaneCenterline(s.id, lane);
                    var end = line[line.Length - 1];
                    var endDir = (line[line.Length - 1] - line[line.Length - 2]);
                    string endNode = Net.LaneEndNode(s.id, lane);
                    var x = Net.IntersectionAt(endNode);
                    foreach (var n in next)
                    {
                        links++;
                        var ns = Net.Segment(n.segmentId);
                        Assert.IsNotNull(ns, n.ToString());
                        Assert.That(n.lane, Is.InRange(0, ns.LaneCount - 1), n.ToString());
                        string startNode = Net.LaneStartNode(n.segmentId, n.lane);
                        bool sameJunction = startNode == endNode || (x != null && x.nodeIds.Contains(startNode));
                        Assert.IsTrue(sameJunction, $"{s.id}#{lane} ends at {endNode}; {n} starts at {startNode}");
                        var start = Net.LaneCenterline(n.segmentId, n.lane)[0];
                        float gap = new Vector2(start.x - end.x, start.z - end.z).magnitude;
                        Assert.Less(gap, 75f, $"{s.id}#{lane} -> {n}: {gap:0.0} m through the junction");
                        Assert.Less(Mathf.Abs(start.y - end.y), 3.5f, $"{s.id}#{lane} -> {n}: height step");
                        bool uTurn = n.segmentId == s.id;
                        if (!uTurn)
                        {
                            var nl = Net.LaneCenterline(n.segmentId, n.lane);
                            var sd = nl[1] - nl[0];
                            float turn = Vector2.Angle(new Vector2(endDir.x, endDir.z), new Vector2(sd.x, sd.z));
                            Assert.Less(turn, 170f, $"{s.id}#{lane} -> {n} doubles back");
                        }
                    }
                }
            Assert.Greater(lanes, 300);
            Assert.Greater(links, lanes);
        }

        [Test]
        public void LaneCenterlines_StayOnTheirCarriageway()
        {
            foreach (var s in Net.segments)
                for (int lane = 0; lane < s.LaneCount; lane++)
                {
                    float off = s.LaneOffset(lane);
                    Assert.Less(off + s.laneWidth * 0.5f, s.widthRight + 1e-3f, $"{s.id}#{lane} fits on the right");
                    Assert.Greater(off - s.laneWidth * 0.5f, -s.widthLeft - 1e-3f, $"{s.id}#{lane} fits on the left");
                    var line = Net.LaneCenterline(s.id, lane);
                    Assert.AreEqual(s.samples.Length, line.Length);
                    for (int i = 1; i < line.Length; i++)
                    {
                        Assert.IsFalse(float.IsNaN(line[i].x) || float.IsNaN(line[i].z), $"{s.id}#{lane} NaN");
                        Assert.Less(Vector3.Distance(line[i - 1], line[i]), 6f, $"{s.id}#{lane} continuous at {i}");
                    }
                }
        }

        [Test]
        public void NearestLanePoint_FindsTheLaneUnderACar()
        {
            var rng = new System.Random(7);
            int tested = 0;
            foreach (var s in Net.segments.Where(s => s.kind != RoadKind.Tunnel).OrderBy(_ => rng.Next()).Take(60))
            {
                int lane = rng.Next(s.LaneCount);
                var line = Net.LaneCenterline(s.id, lane);
                if (line.Length < 4) continue;
                var p = line[line.Length / 2];
                var hit = Net.NearestLanePoint(p + new Vector3(0.3f, 0.5f, 0.2f), 20f);
                Assert.IsTrue(hit.valid, s.id);
                Assert.Less(new Vector2(hit.position.x - p.x, hit.position.z - p.z).magnitude, 1.2f, $"{s.id}#{lane} -> {hit.segmentId}#{hit.lane}");
                Assert.AreEqual(1f, hit.direction.magnitude, 1e-3f);
                Assert.Greater(Net.SpeedLimitAt(hit.segmentId), 0f);
                tested++;
            }
            Assert.Greater(tested, 40);
            Assert.IsFalse(Net.NearestLanePoint(new Vector3(0, 0, -900), 30f).valid, "nothing in the open sea");
        }

        [Test]
        public void LaneGraph_ReachesAlmostEveryLaneFromDowntown()
        {
            var start = Net.segments.First(s => s.name == "Genesis Avenue" && s.kind == RoadKind.Avenue);
            var seen = new HashSet<LaneRef>();
            var q = new Queue<LaneRef>();
            q.Enqueue(new LaneRef(start.id, 0)); seen.Add(q.Peek());
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var n in Net.NextSegments(c.segmentId, c.lane)) if (seen.Add(n)) q.Enqueue(n);
            }
            int total = Net.segments.Sum(s => s.LaneCount);
            Assert.Greater(seen.Count, total * 0.95f, $"reached {seen.Count} of {total} lanes");
        }

        [Test]
        public void Signals_AlternateBetweenTheTwoAxes()
        {
            var x = Net.intersections.First(i => i.kind == IntersectionKind.Signal && i.approaches.Length >= 4);
            var phase0 = x.approaches.Where((a, i) => Net.PhaseOf(x, i) == 0).ToArray();
            var phase1 = x.approaches.Where((a, i) => Net.PhaseOf(x, i) == 1).ToArray();
            Assert.IsNotEmpty(phase0); Assert.IsNotEmpty(phase1);
            bool sawGreenRed = false, sawRedGreen = false;
            for (double t = 0; t < x.cycle; t += 0.5)
            {
                var a = Net.Signal(x.id, phase0[0], t); var b = Net.Signal(x.id, phase1[0], t);
                Assert.IsFalse(a == SignalState.Green && b == SignalState.Green, $"both axes green at {t}");
                sawGreenRed |= a == SignalState.Green && b == SignalState.Red;
                sawRedGreen |= a == SignalState.Red && b == SignalState.Green;
            }
            Assert.IsTrue(sawGreenRed && sawRedGreen);
        }
    }
}
