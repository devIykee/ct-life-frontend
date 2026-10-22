using System.Collections.Generic;
using CTLife.World;
using CTLife.World.Roads;
using CTLife.World.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>Feel lane F3 (2026-10-17): how AI drivers pick a speed, read lights and follow routes; the parked-car swap's class pick.</summary>
    public class TrafficTests
    {
        private const float Dt = 0.02f;

        [Test]
        public void Idm_FreeRoad_PullsAwayGentlyAndSettlesAtTheLimit()
        {
            Assert.AreEqual(TrafficRules.MaxAccel, TrafficRules.Idm(0f, 13.9f, float.PositiveInfinity, 0f), 1e-3f, "a calm start");
            float v = 0f;
            for (float t = 0f; t < 40f; t += Dt) v += TrafficRules.Idm(v, 13.9f, float.PositiveInfinity, 0f) * Dt;
            Assert.AreEqual(13.9f, v, 0.4f, "settles at the limit");
            Assert.Less(TrafficRules.Idm(16f, 13.9f, float.PositiveInfinity, 0f), 0f, "over the limit it eases off");
        }

        [Test]
        public void Idm_StopsBehindSomethingStandingInTheRoad_WithoutTouchingIt()
        {
            float v = 13.9f, gap = 80f, worst = 0f, least = gap;
            for (float t = 0f; t < 40f; t += Dt)
            {
                float a = TrafficRules.Idm(v, 13.9f, gap, 0f);
                worst = Mathf.Min(worst, a);
                v = Mathf.Max(0f, v + a * Dt);
                gap -= v * Dt;
                least = Mathf.Min(least, gap);
            }
            Assert.Less(v, 0.05f, "stopped");
            Assert.Greater(least, 1.2f, "short of it");
            Assert.Less(least, TrafficRules.MinGap + 1.5f, "but not miles short");
            Assert.GreaterOrEqual(worst, -TrafficRules.MaxBrake, "within the brakes");
        }

        [Test]
        public void Idm_FollowsAMovingCarAtAboutOneAndAQuarterSeconds()
        {
            float v = 13.9f, gap = 40f, lead = 10f;
            for (float t = 0f; t < 60f; t += Dt)
            {
                v = Mathf.Max(0f, v + TrafficRules.Idm(v, 13.9f, gap, lead) * Dt);
                gap += (lead - v) * Dt;
            }
            Assert.AreEqual(lead, v, 0.3f, "matches the leader's speed");
            float wanted = TrafficRules.MinGap + lead * TrafficRules.Headway;
            Assert.AreEqual(wanted, gap, 3f, "and keeps the gap");
        }

        [Test]
        public void Signals_RedStops_AmberOnlyWhenThereIsRoomToStop()
        {
            Assert.IsTrue(TrafficRules.StopForSignal(SignalState.Red, 30f, 10f));
            Assert.IsFalse(TrafficRules.StopForSignal(SignalState.Green, 3f, 10f));
            Assert.IsTrue(TrafficRules.StopForSignal(SignalState.Amber, 40f, 10f), "far enough: stop");
            Assert.IsFalse(TrafficRules.StopForSignal(SignalState.Amber, 4f, 10f), "too late to stop: go");
        }

        [Test]
        public void Curvature_SignAndRadius_AndTheBendSpeed()
        {
            // a right-hand bend of radius 20 m (heading north, curving east), a left one, and a straight
            Vector3 P(float deg, float r, float sign) { float a = deg * Mathf.Deg2Rad; return new Vector3(sign * (r - r * Mathf.Cos(a)), 0f, r * Mathf.Sin(a)); }
            float right = TrafficRules.Curvature(P(0, 20, 1), P(10, 20, 1), P(20, 20, 1));
            float left = TrafficRules.Curvature(P(0, 20, -1), P(10, 20, -1), P(20, 20, -1));
            Assert.AreEqual(1f / 20f, right, 0.002f, "right is positive");
            Assert.AreEqual(-1f / 20f, left, 0.002f, "left is negative");
            Assert.AreEqual(0f, TrafficRules.Curvature(Vector3.zero, Vector3.forward * 5f, Vector3.forward * 10f), 1e-5f);
            Assert.AreEqual(Mathf.Sqrt(TrafficRules.LateralAccel * 20f), TrafficRules.CurveSpeed(1f / 20f), 1e-3f);
        }

        // A plus-shaped bit of road: south arm A->B (north), then B->C (north, on), B->D (east, a right turn); signal at B.
        private static RoadNetwork Cross()
        {
            var net = ScriptableObject.CreateInstance<RoadNetwork>();
            net.nodes.Add(new RoadNode { id = "A", position = new Vector3(0f, 0f, 0f) });
            net.nodes.Add(new RoadNode { id = "B", position = new Vector3(0f, 0f, 100f) });
            net.nodes.Add(new RoadNode { id = "C", position = new Vector3(0f, 0f, 200f) });
            net.nodes.Add(new RoadNode { id = "D", position = new Vector3(100f, 0f, 100f) });
            RoadSegment Seg(string id, string from, string to, Vector3 a, Vector3 b, float trim)
            {
                var pts = new List<Vector3>();
                var dir = (b - a).normalized;
                float len = Vector3.Distance(a, b);
                for (float s = trim; s <= len - trim + 1e-3f; s += 2f) pts.Add(a + dir * s);
                return new RoadSegment { id = id, fromNode = from, toNode = to, samples = pts.ToArray(), lanesForward = 1, lanesBack = 1, laneWidth = 3.3f, speedLimit = 13.9f };
            }
            net.segments.Add(Seg("s1", "A", "B", new Vector3(0, 0, 0), new Vector3(0, 0, 100), 0f));
            net.segments[0].samples = TrimEnd(net.segments[0].samples, 8f);
            net.segments.Add(Seg("s2", "B", "C", new Vector3(0, 0, 100), new Vector3(0, 0, 200), 8f));
            net.segments.Add(Seg("s3", "B", "D", new Vector3(0, 0, 100), new Vector3(100, 0, 100), 8f));
            net.lanes.Add(new LaneLinks { segmentId = "s1", lane = 0, next = new[] { new LaneRef("s2", 0), new LaneRef("s3", 0), new LaneRef("s1", 1) } });
            net.lanes.Add(new LaneLinks { segmentId = "s2", lane = 0, next = new LaneRef[0] });
            net.lanes.Add(new LaneLinks { segmentId = "s3", lane = 0, next = new LaneRef[0] });
            net.intersections.Add(new RoadIntersection { id = "x", nodeIds = new[] { "B" }, kind = IntersectionKind.Signal, center = new Vector3(0, 0, 100), approaches = new[] { "s1", "s2", "s3" } });
            return net;
        }

        private static Vector3[] TrimEnd(Vector3[] pts, float metres)
        {
            var list = new List<Vector3>(pts);
            float cut = 0f;
            while (list.Count > 2 && cut < metres) { cut += Vector3.Distance(list[list.Count - 1], list[list.Count - 2]); list.RemoveAt(list.Count - 1); }
            return list.ToArray();
        }

        [Test]
        public void Route_RunsDownItsLane_ThroughTheJunction_NeverBackTheWayItCame()
        {
            var net = Cross();
            try
            {
                for (int seed = 0; seed < 20; seed++)
                {
                    var route = new TrafficRoute();
                    Assert.IsTrue(route.StartAt(net, new LaneRef("s1", 0), 10f, new System.Random(seed)));
                    var at = route.At(0f, out var dir);
                    Assert.AreEqual(1.65f, at.x, 0.05f, "lane 0 runs right of the centreline (right-hand traffic)");
                    Assert.AreEqual(10f, at.z, 0.05f, "where it was put");
                    Assert.Greater(dir.z, 0.99f, "heading north");
                    Assert.AreSame(net.intersections[0], route.Current.Junction, "the lane ends at the signal");
                    route.Extend(150f);
                    Assert.GreaterOrEqual(route.Pieces.Count, 3, "lane, connector, next lane");
                    Assert.IsTrue(route.Pieces[1].Connector);
                    Assert.AreEqual("s1", route.Pieces[1].FromSegment);
                    var next = route.Pieces[2].Lane;
                    Assert.AreNotEqual("s1", next.segmentId, "no U-turn back down s1");
                    // the joins are continuous: the connector starts where the lane ends and ends where the next lane starts
                    Assert.Less(Vector3.Distance(route.Pieces[0].Points[route.Pieces[0].Points.Length - 1], route.Pieces[1].Points[0]), 0.01f);
                    Assert.Less(Vector3.Distance(route.Pieces[1].Points[route.Pieces[1].Points.Length - 1], route.Pieces[2].Points[0]), 0.01f);
                    // a point 30 m on, in the lane, projects 30 m ahead and centred
                    Assert.IsTrue(route.Project(new Vector3(1.65f, 0f, 40f), 60f, out float ahead, out float lateral));
                    Assert.AreEqual(30f, ahead, 0.1f);
                    Assert.AreEqual(0f, lateral, 0.05f);
                    Assert.IsTrue(route.Project(new Vector3(3.65f, 0f, 40f), 60f, out _, out lateral));
                    Assert.AreEqual(2f, lateral, 0.05f, "to the right is positive");
                    // driving on drops finished pieces off the front
                    float total = route.Remaining;
                    route.Advance(90f);
                    Assert.AreEqual(total - 90f, route.Remaining, 0.05f);
                    Assert.IsFalse(!route.Current.Connector && route.Current.Lane.segmentId == "s1", "past the first lane");
                }
            }
            finally { Object.DestroyImmediate(net); }
        }

        [Test]
        public void Route_DeadEndStopsAtTheEnd()
        {
            var net = Cross();
            try
            {
                var route = new TrafficRoute();
                Assert.IsTrue(route.StartAt(net, new LaneRef("s2", 0), 0f, new System.Random(1)));
                route.Extend(200f);
                Assert.IsTrue(route.DeadEnd, "s2 leads nowhere");
                Assert.IsFalse(route.Advance(500f), "it stops at its end");
                Assert.AreEqual(0f, route.Remaining, 1e-3f);
            }
            finally { Object.DestroyImmediate(net); }
        }

        [Test]
        public void ParkedCar_SwapPicksTheKitModelThenTheClass()
        {
            Assert.AreEqual("sedan", ParkedCars.SpecFor(VehicleClass.Compact, "kit_suv_blue", 0), "no v3 SUV yet: the sedan stands in");
            Assert.AreEqual("scooter", ParkedCars.SpecFor(VehicleClass.Moped, null, 0));
            Assert.AreEqual("roadstar", ParkedCars.SpecFor(VehicleClass.Roadstar, "", 0));
            Assert.AreEqual("sedan", ParkedCars.SpecFor(VehicleClass.Compact, "sedan_kit_03", 5));
            Assert.AreEqual("compact", ParkedCars.SpecFor(VehicleClass.Compact, "hatch_kit_03", 4));
            var seen = new HashSet<string>();
            for (int i = 0; i < 6; i++) seen.Add(ParkedCars.SpecFor(VehicleClass.Cars, null, i));
            CollectionAssert.AreEquivalent(new[] { "compact", "sedan", "sports" }, seen, "any car class: a mix (the sedan stands in for the SUV)");
            Assert.AreEqual("police", ParkedCars.SpecFor(VehicleClass.Compact, "kit_compact", 3 << 4), "one car bay in ten parks a police cruiser");
            Assert.AreEqual("scooter", ParkedCars.SpecFor(VehicleClass.Moped, null, 3 << 4), "never on a moped bay");
        }
    }
}
