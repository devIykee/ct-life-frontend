using System;
using System.Linq;
using System.Threading.Tasks;
using CTLife.UI;
using CTLife.World;
using CTLife.World.Roads;
using CTLife.World.Vehicles;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Feel lane F3: AI traffic on the real CT Island v2 at a fixed 60 fps. Traffic builds up round Timeline Square, then:
    /// a signalled junction seen from its corner, a car stopping for the player standing in its lane (and honking), a car
    /// pulling round a stopped car, the player ramming one (it is knocked about), NPC drivers at the wheel, and a parked
    /// decor car swapped for a drivable one. captureSet "traffic"; writes receipt["traffic"] and shots traffic-*; with
    /// -ctlife-trafficClip 1 also real-time half-size film frames traffic-clip-NNNN.jpg.
    /// </summary>
    internal static class TrafficCapture
    {
        private static double R(float v) => Math.Round(v, 3);

        public static async Task<VenueSession> Run(VenueSession visit, Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, JObject receipt)
        {
            var o = new JObject(); receipt["traffic"] = o;
            await visit.Exit();
            visit = await VenueSession.EnterIsland("timeline_square", joinRoom: false);
            await wait(2f);
            var p = visit.LocalPlayer;
            var rig = CameraRig.Instance;
            var sys = VehicleSystem.Current;
            var traffic = TrafficSystem.Current;
            var net = RoadNetwork.Active;
            expect(sys != null && traffic != null && net != null, "the island session runs traffic on World's road network");
            if (sys == null || traffic == null || net == null) return visit;
            int prevCapture = Time.captureFramerate;
            Time.captureFramerate = 60;
            VehicleControls.ScriptedInput = true;
            bool film = Environment.GetCommandLineArgs().Contains("-ctlife-trafficClip");
            int frame = 0;
            var marks = new JObject(); o["clipMarks"] = marks;
            void Mark(string name) => marks[name] = frame / 2;
            // hold the view (the follow camera would swing back behind the player)
            float holdYaw = float.NaN, holdPitch = 0f;
            int maxSeated = 0, overlaps = 0; float deepest = 0f;
            async Task Frames(float seconds, Action<float> each = null)
            {
                for (float t = 0f; t < seconds; t += 1f / 60f)
                {
                    if (!float.IsNaN(holdYaw)) { rig.yaw = holdYaw; rig.pitch = holdPitch; rig.Orbit(Vector2.zero); }
                    await Awaitable.NextFrameAsync();
                    if (film && (frame++ & 1) == 0) OnboardingCapture.FilmFrame($"traffic-clip-{frame / 2:0000}");
                    each?.Invoke(t);
                    int seated = 0;
                    foreach (var c in traffic.Cars) if (c != null && c.Driver != null) seated++;
                    maxSeated = Math.Max(maxSeated, seated);
                    if ((Time.frameCount % 10) == 0) Overlaps(traffic, ref overlaps, ref deepest);
                }
            }
            void Look(Vector3 from, Vector3 at, float pitch)
            {
                var d = at - from; d.y = 0f;
                holdYaw = d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d).eulerAngles.y : rig.yaw; holdPitch = pitch;
            }
            var arrival = p.transform.position;
            var decor = (GameObject)null;
            try
            {
                // ---------------------------------------------------------------- build-up and flow
                Mark("build");
                float moving = 0f, samples = 0f, speedSum = 0f;
                await Frames(22f, t =>
                {
                    if (t < 10f) return;
                    foreach (var c in traffic.Cars) { if (c == null || c.Knocked) continue; samples++; speedSum += c.Speed; if (c.Speed > 1f) moving++; }
                });
                int count = traffic.Cars.Count;
                o["flow"] = new JObject
                {
                    ["cars"] = count, ["spawned"] = traffic.Spawned, ["maxCars"] = traffic.MaxCars,
                    ["meanKmh"] = R(samples > 0 ? speedSum / samples * 3.6f : 0f), ["movingShare"] = R(samples > 0 ? moving / samples : 0f),
                    ["density"] = R(VehicleWorld.TrafficDensityAt(arrival)), ["stepMs"] = R(traffic.StepMs),
                };
                expect(count >= 4, $"traffic builds up round the arrival ({count} cars, {traffic.Spawned} spawned)");
                expect(samples > 0 && moving / samples > 0.45f, $"traffic flows ({(samples > 0 ? moving / samples * 100f : 0f):F0}% of samples moving, {(samples > 0 ? speedSum / samples * 3.6f : 0f):F0} km/h mean)");

                // ---------------------------------------------------------------- a signalled junction from its corner
                RoadIntersection junction = null; float jd = float.MaxValue;
                foreach (var x in net.intersections)
                {
                    if (x == null || x.kind != IntersectionKind.Signal) continue;
                    float d = Vector3.Distance(x.center, arrival);
                    if (d < jd) { jd = d; junction = x; }
                }
                if (junction != null)
                {
                    Mark("junction");
                    var corner = TrafficSystem.Corner(net, junction);
                    p.SnapTo(corner, 0f);
                    rig.FollowAvatar(p.transform);
                    Look(corner, junction.center, 24f);
                    rig.Zoom(-900f);
                    int reds0 = traffic.RedStops;
                    await Frames(1.5f);
                    await shot("traffic-01-junction", .05f);
                    await Frames(14f);
                    await shot("traffic-02-junction-later", .05f);
                    o["junction"] = new JObject { ["id"] = junction.id, ["fromArrivalM"] = R(jd), ["redStops"] = traffic.RedStops - reds0 };
                    expect(traffic.RedStops - reds0 > 0 || traffic.RedStops > 0, $"cars stop at a red light ({traffic.RedStops - reds0} at {junction.id} in 15 s)");
                    rig.Zoom(900f);
                }

                // ---------------------------------------------------------------- a roundabout and a stop sign, from beside them
                foreach (var kind in new[] { IntersectionKind.Roundabout, IntersectionKind.Stop })
                {
                    RoadIntersection near = null; float nd = float.MaxValue;
                    foreach (var x in net.intersections)
                    {
                        if (x == null || x.kind != kind) continue;
                        float d = Vector3.Distance(x.center, arrival);
                        if (d < nd) { nd = d; near = x; }
                    }
                    if (near == null) continue;
                    string name = kind == IntersectionKind.Roundabout ? "roundabout" : "stop";
                    Mark(name);
                    var corner = TrafficSystem.Corner(net, near);
                    p.SnapTo(corner, 0f);
                    rig.FollowAvatar(p.transform);
                    Look(corner, near.center, kind == IntersectionKind.Roundabout ? 30f : 22f);
                    rig.Zoom(-900f);
                    int y0 = traffic.Yields, s0 = traffic.StopSignStops;
                    await Frames(16f);
                    await shot($"traffic-02-{name}", .05f);
                    await Frames(6f);
                    o[name] = new JObject { ["id"] = near.id, ["fromArrivalM"] = R(nd), ["yields"] = traffic.Yields - y0, ["stopSignStops"] = traffic.StopSignStops - s0 };
                    rig.Zoom(900f);
                }
                p.SnapTo(arrival, 0f); rig.FollowAvatar(p.transform); holdYaw = float.NaN;
                await Frames(3f);

                // ---------------------------------------------------------------- the player stands in a lane
                Mark("stand");
                var victim = await Find(traffic, 30f, false, 20f, t => Frames(t));
                if (victim != null)
                {
                    var ahead = victim.Route.At(victim.HalfLength + 34f, out var dir);
                    var side = Vector3.Cross(Vector3.up, dir).normalized;
                    p.SnapTo(ahead, Mathf.Atan2(-dir.x, -dir.z));
                    rig.FollowAvatar(p.transform);
                    Look(ahead, ahead - dir * 10f + side * 1.5f, 10f);
                    int honks0 = traffic.Honks;
                    float closest = float.MaxValue;
                    await Frames(9f, _ =>
                    {
                        if (victim == null) return;
                        float d = Vector3.Distance(Flat(victim.transform.position), Flat(p.transform.position)) - victim.HalfLength;
                        closest = Mathf.Min(closest, d);
                    });
                    await shot("traffic-03-stops-for-the-player", .05f);
                    bool down = p.Stunned;
                    o["stand"] = new JObject { ["closestM"] = R(closest), ["honks"] = traffic.Honks - honks0, ["knockedDown"] = down, ["driver"] = victim != null && victim.Driver != null };
                    expect(closest > 0.6f && !down, $"a car stops for the player in its lane ({closest:F1} m short)");
                    expect(traffic.Honks - honks0 > 0, $"and honks ({traffic.Honks - honks0})");
                }
                else expect(false, "no traffic car on a straight stretch to test stopping for the player");
                p.SnapTo(arrival, 0f); rig.FollowAvatar(p.transform); holdYaw = float.NaN;
                await Frames(1f);

                // ---------------------------------------------------------------- round a stopped car
                Mark("around");
                var mover = await Find(traffic, 30f, true, 30f, t => Frames(t));
                if (mover != null)
                {
                    var at = mover.Route.At(mover.HalfLength + 36f, out var dir);
                    var block = sys.Spawn("compact", at + Vector3.up * 0.05f, Quaternion.LookRotation(Flat(dir).normalized));
                    var watch = at - Flat(dir).normalized * 22f + Vector3.Cross(Vector3.up, dir).normalized * 9f;
                    p.SnapTo(watch, 0f);
                    rig.FollowAvatar(p.transform);
                    Look(watch, at, 14f);
                    int changes0 = traffic.LaneChanges;
                    float closest = float.MaxValue;
                    await Frames(14f, _ =>
                    {
                        if (mover == null || block == null) return;
                        closest = Mathf.Min(closest, Vector3.Distance(mover.transform.position, block.transform.position) - 4.2f);
                    });
                    await shot("traffic-04-round-a-stopped-car", .05f);
                    o["around"] = new JObject { ["laneChanges"] = traffic.LaneChanges - changes0, ["closestM"] = R(closest), ["moverKnocked"] = mover != null && mover.Knocked };
                    expect(traffic.LaneChanges - changes0 > 0, $"a car pulls round a stopped car in its lane ({traffic.LaneChanges - changes0} lane changes)");
                    if (block != null) { sys.Vehicles.Remove(block); Object.Destroy(block.gameObject); }
                }
                else o["around"] = "no multi-lane stretch with traffic in range";
                p.SnapTo(arrival, 0f); rig.FollowAvatar(p.transform); holdYaw = float.NaN;
                await Frames(1f);

                // ---------------------------------------------------------------- ram one
                Mark("ram");
                var target = await Find(traffic, 25f, false, 20f, t => Frames(t));
                if (target != null)
                {
                    // the sports car 26 m behind it in the same lane, the player at its door
                    var lane = target.Route.Current.Lane;
                    var pts = net.LaneCenterline(lane.segmentId, lane.lane);
                    var piece = RoutePiece.Make(pts);
                    float along = piece.Locate(target.transform.position, out _, out _);
                    var start = piece.Sample(Mathf.Max(0f, along - 26f), out var dir);
                    var car = sys.Spawn("sports", start + Vector3.up * 0.1f, Quaternion.LookRotation(Flat(dir).normalized));
                    await Frames(0.5f);
                    p.SnapTo(car.EntryPoint, 0f);
                    await Frames(0.3f);
                    bool inCar = sys.Enter(car);
                    float t0 = Time.time;
                    while (sys.Driving != car && Time.time - t0 < 8f) await Frames(1f / 60f);
                    holdYaw = float.NaN;
                    float impulse = 0f;
                    void Hit(float i, Vector3 _) => impulse = Mathf.Max(impulse, i);
                    car.GetComponent<CarPhysics>().Impact += Hit;
                    var knockedAt = Vector3.zero; bool seen = false;
                    for (float t = 0f; t < 6f && !(target != null && target.Knocked && Time.time - target.KnockedAt > 1.5f); t += 1f / 60f)
                    {
                        car.Input = new VehicleInput { throttle = 1f, steer = Mathf.Clamp(Steer(car, target) / 25f, -1f, 1f) };
                        await Frames(1f / 60f);
                        if (!seen && target != null && target.Knocked) { seen = true; knockedAt = target.transform.position; }
                    }
                    await shot("traffic-05-knocked", .05f);
                    car.GetComponent<CarPhysics>().Impact -= Hit;
                    car.Input = new VehicleInput { brake = 1f, handbrake = true };
                    await Frames(2f);
                    float moved = target != null ? Vector3.Distance(knockedAt, target.transform.position) : 0f;
                    o["ram"] = new JObject { ["boarded"] = inCar && sys.Driving == car, ["knocked"] = target != null && target.Knocked, ["impulse"] = R(impulse), ["pushedM"] = R(moved) };
                    expect(target != null && target.Knocked, $"rammed from behind, a traffic car is knocked about (pushed {moved:F1} m, {impulse:F0} N·s)");
                    sys.Exit();
                    t0 = Time.time;
                    while ((sys.Driving != null || sys.Busy) && Time.time - t0 < 6f) await Frames(1f / 60f);
                }
                else expect(false, "no traffic car to ram");

                // ---------------------------------------------------------------- NPC drivers
                o["drivers"] = new JObject { ["maxSeated"] = maxSeated };
                expect(maxSeated > 0, $"NPC drivers sit at the wheel of the near cars (up to {maxSeated} at once)");

                // ---------------------------------------------------------------- take a parked car
                Mark("parked");
                p.SnapTo(arrival, 0f); rig.FollowAvatar(p.transform);
                await Frames(1f);
                var bay = IslandMarkers.SpawnsNear(arrival, 120f).FirstOrDefault(b => b != null && !Physics.CheckSphere(b.transform.position + Vector3.up, 2f, 1 << CharacterLayers.Vehicle));
                if (bay != null)
                {
                    // a real World ParkedCar in the bay (pose = the bay, forward = the parked heading), a box collider on Prop
                    decor = new GameObject("decor-parked-car") { layer = 13 };
                    decor.transform.SetPositionAndRotation(bay.transform.position, bay.transform.rotation);
                    var box = decor.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.75f, 0f); box.size = new Vector3(1.9f, 1.4f, 4.2f);
                    var stand = decor.AddComponent<ParkedCar>();
                    stand.vehicleClass = VehicleClass.Compact; stand.model = "kit_compact";
                    var far = bay.transform.position + bay.transform.right * 30f;
                    p.SnapTo(far, 0f);
                    await Frames(1.2f);
                    bool before = !box.enabled;
                    p.SnapTo(bay.transform.position + bay.transform.right * -5f, 0f);
                    rig.FollowAvatar(p.transform);
                    await Frames(1.5f);
                    bool swapped = !box.enabled && sys.Parked.IsSwapped(stand);
                    await shot("traffic-06-parked-car-swapped", .05f);
                    p.SnapTo(far + bay.transform.right * 30f, 0f);
                    await Frames(1.5f);
                    bool restored = box.enabled && !sys.Parked.IsSwapped(stand);
                    o["parked"] = new JObject { ["hiddenBefore"] = before, ["swappedNear"] = swapped, ["restoredFar"] = restored, ["realDecorCars"] = ParkedCar.All.Count };
                    expect(!before && swapped && restored, "a World ParkedCar turns drivable as the player walks up, and back when they leave");
                }
                else o["parked"] = "no free bay near the arrival";
                // and one of World's own decor cars on the island
                ParkedCar real = null; float rd = float.MaxValue;
                foreach (var pc in ParkedCar.All)
                {
                    if (pc == null || (decor != null && pc.gameObject == decor)) continue;
                    float d = (pc.transform.position - arrival).sqrMagnitude;
                    if (d < rd) { rd = d; real = pc; }
                }
                if (real != null)
                {
                    p.SnapTo(real.transform.position - real.transform.right * 4f, 0f);
                    rig.FollowAvatar(p.transform);
                    await Frames(1.5f);
                    bool swappedReal = sys.Parked.IsSwapped(real);
                    await shot("traffic-07-world-parked-car", .05f);
                    o["parkedWorld"] = new JObject { ["model"] = real.model, ["class"] = real.vehicleClass.ToString(), ["fromArrivalM"] = R(Mathf.Sqrt(rd)), ["swapped"] = swappedReal };
                    expect(swappedReal, $"one of World's parked cars ({real.model}) is drivable when the player walks up");
                }
                else o["parkedWorld"] = "no ParkedCar streamed in";

                o["end"] = new JObject
                {
                    ["spawned"] = traffic.Spawned, ["despawned"] = traffic.Despawned, ["honks"] = traffic.Honks, ["laneChanges"] = traffic.LaneChanges,
                    ["redStops"] = traffic.RedStops, ["stopSignStops"] = traffic.StopSignStops, ["yields"] = traffic.Yields,
                    ["closestGapM"] = R(traffic.ClosestGap), ["stepMs"] = R(traffic.StepMs),
                };
                o["end"]["bodyOverlaps"] = overlaps; o["end"]["deepestOverlapM"] = R(deepest);
                expect(overlaps == 0, $"no two traffic cars ever drive into each other ({overlaps} overlaps sampled, deepest {deepest:F2} m; closest lane gap {traffic.ClosestGap:F2} m)");
                expect(traffic.StopSignStops > 0, $"cars come to a full stop at stop signs ({traffic.StopSignStops})");
            }
            finally
            {
                VehicleControls.ScriptedInput = false;
                Time.captureFramerate = prevCapture;
                if (decor != null) Object.Destroy(decor);
            }
            return visit;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Rail cars whose bodies interpenetrate by more than 0.15 m right now (kinematic bodies pass through each other).</summary>
        private static void Overlaps(TrafficSystem traffic, ref int count, ref float deepest)
        {
            var cars = traffic.Cars;
            for (int i = 0; i < cars.Count; i++)
                for (int j = i + 1; j < cars.Count; j++)
                {
                    var a = cars[i]; var b = cars[j];
                    if (a == null || b == null || a.Knocked || b.Knocked) continue;
                    if ((a.transform.position - b.transform.position).sqrMagnitude > 36f) continue;
                    var ca = a.GetComponent<BoxCollider>(); var cb = b.GetComponent<BoxCollider>();
                    if (ca == null || cb == null) continue;
                    if (Physics.ComputePenetration(ca, a.transform.position, a.transform.rotation, cb, b.transform.position, b.transform.rotation, out _, out float d) && d > 0.15f)
                    { count++; deepest = Mathf.Max(deepest, d); }
                }
        }

        /// <summary>Wait (up to <paramref name="seconds"/>) for a car that fits; the traffic keeps moving meanwhile.</summary>
        private static async Task<TrafficCar> Find(TrafficSystem traffic, float minKmh, bool multiLane, float seconds, Func<float, Task> frames)
        {
            for (float t = 0f; t < seconds; t += 0.5f)
            {
                var c = Approaching(traffic, minKmh, multiLane);
                if (c != null) return c;
                await frames(0.5f);
            }
            return null;
        }

        /// <summary>A traffic car moving at least <paramref name="minKmh"/> with a fairly straight 45 m of its lane ahead.</summary>
        private static TrafficCar Approaching(TrafficSystem traffic, float minKmh, bool multiLane = false)
        {
            var net = RoadNetwork.Active;
            TrafficCar best = null; float bestScore = float.MaxValue;
            var cam = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            foreach (var c in traffic.Cars)
            {
                if (c == null || c.Knocked || c.Speed * 3.6f < minKmh * 0.5f || c.Route.Current == null || c.Route.Current.Connector) continue;
                if (c.Route.Current.Length - c.Route.S < 45f) continue;
                if (Mathf.Abs(c.Route.Curvature(18f, 12f)) > 0.02f) continue;
                if (multiLane)
                {
                    var seg = net.Segment(c.Route.Current.Lane.segmentId);
                    if (seg == null || (seg.IsForward(c.Route.Current.Lane.lane) ? seg.lanesForward : seg.lanesBack) < 2) continue;
                }
                float score = (c.transform.position - cam).sqrMagnitude;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        private static float Steer(Vehicle car, TrafficCar target)
        {
            var to = Flat(target.transform.position - car.transform.position);
            return Vector3.SignedAngle(Flat(car.transform.forward), to, Vector3.up);
        }
    }
}
