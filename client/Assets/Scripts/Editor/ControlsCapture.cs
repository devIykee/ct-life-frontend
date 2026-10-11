using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Feel lane F1 (2026-10-17): the on-foot controls on CT Island, measured on the real LocalPlayerController and
    /// CharacterBody at a fixed 60 fps step. A test course is built 60 m above Timeline Square (stairs, a kerb, a knee-high
    /// bench, 0.45 / 1.5 / 4 m ledges, a wall, a 30° and a 55° ramp, a roofed room) so each check runs on known geometry:
    /// gait speeds, stop distance, steps and kerbs with no fall, real falls with landings, no trips or knockdowns from
    /// static geometry, wall slides, slopes, and the third-person camera (behind, recentre, wall pull-in, indoors).
    /// captureSet "controls"; writes receipt["controls"] and shots controls-*.
    /// </summary>
    internal static class ControlsCapture
    {
        private sealed class Track
        {
            public float maxSpeed, steadySpeed, reach90 = -1f, maxY = float.MinValue, minY = float.MaxValue, travelled;
            public int frames, airborneFrames, stunnedFrames;
            public readonly List<float> landed = new List<float>();
            public readonly HashSet<string> states = new HashSet<string>();
            public Vector3 end;
            public JObject Json(Vector3 origin) => new JObject
            {
                ["frames"] = frames, ["maxSpeed"] = R(maxSpeed), ["steadySpeed"] = R(steadySpeed), ["reach90s"] = R(reach90),
                ["airborneFrames"] = airborneFrames, ["stunnedFrames"] = stunnedFrames, ["landed"] = new JArray(landed.ConvertAll(h => (object)R(h)).ToArray()),
                ["maxY"] = R(maxY - origin.y), ["minY"] = R(minY - origin.y), ["end"] = new JArray(R(end.x - origin.x), R(end.y - origin.y), R(end.z - origin.z)),
                ["travelled"] = R(travelled), ["states"] = new JArray(new List<string>(states).ToArray()),
            };
        }

        private static double R(float v) => Math.Round(v, 3);

        private static readonly string[] Watched = { "loco_idle", "loco_walk", "loco_run", "jump_air", "jump_land", "react_roll", "react_trip",
            "react_stagger", "react_fall", "react_liquidated", "loco_run_stop" };

        public static async Task<VenueSession> Run(VenueSession visit, Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, JObject receipt)
        {
            var o = new JObject(); receipt["controls"] = o;
            await visit.Exit();
            visit = await VenueSession.EnterIsland("timeline_square", joinRoom: false);
            await wait(1.5f);
            var p = visit.LocalPlayer;
            var view = p.GetComponent<AvatarView>();
            var rig = CameraRig.Instance;
            var origin = p.transform.position + Vector3.up * 60f;
            var course = BuildCourse(origin);
            Physics.SyncTransforms();
            int prevCapture = Time.captureFramerate;
            Time.captureFramerate = 60;
            var landings = new List<float>();
            p.Body.Landed += h => landings.Add(h);
            try
            {
                async Task Place(Vector3 rel, float yawDeg)
                {
                    p.StopDirectInput(); p.CancelMovement();
                    p.SnapTo(origin + rel, yawDeg * Mathf.Deg2Rad);
                    rig.SnapToTarget();
                    for (int i = 0; i < 30; i++) await Awaitable.NextFrameAsync();
                    landings.Clear();
                }
                Vector2 Stick(Vector3 worldDir, float strength)
                {
                    var local = Quaternion.Euler(0f, -rig.yaw, 0f) * worldDir.normalized;
                    return new Vector2(local.x, local.z) * strength;
                }
                async Task<Track> Drive(float seconds, Vector3 dir, float strength, float target = 0f, string shotName = null, float shotAt = -1f)
                {
                    var tr = new Track();
                    float t = 0f; var last = p.transform.position; bool shotTaken = false;
                    var steady = new List<float>();
                    while (t < seconds)
                    {
                        p.SetMoveInput(strength > 0f ? Stick(dir, strength) : Vector2.zero);
                        await Awaitable.NextFrameAsync();
                        t += Time.deltaTime;
                        float speed = p.ActualVelocity.magnitude;
                        var pos = p.transform.position;
                        tr.frames++;
                        tr.maxSpeed = Mathf.Max(tr.maxSpeed, speed);
                        if (target > 0f && tr.reach90 < 0f && speed >= target * 0.9f) tr.reach90 = t;
                        if (t > seconds - 0.6f) steady.Add(speed);
                        tr.maxY = Mathf.Max(tr.maxY, pos.y); tr.minY = Mathf.Min(tr.minY, pos.y);
                        var d = pos - last; d.y = 0f; tr.travelled += d.magnitude; last = pos;
                        if (p.Body.Airborne) tr.airborneFrames++;
                        if (p.Stunned) tr.stunnedFrames++;
                        var info = view.Animator.GetCurrentAnimatorStateInfo(0);
                        foreach (var s in Watched) if (info.shortNameHash == Animator.StringToHash(s)) tr.states.Add(s);
                        if (shotName != null && !shotTaken && t >= shotAt) { shotTaken = true; await shot(shotName, .02f); }
                    }
                    tr.steadySpeed = steady.Count > 0 ? Average(steady) : 0f;
                    tr.end = p.transform.position;
                    tr.landed.AddRange(landings);
                    return tr;
                }

                var east = Vector3.right; var north = Vector3.forward;
                // 1) Gaits on the open lane, and the stop from a sprint.
                await Place(new Vector3(-35f, 0f, -25f), 90f);
                await shot("controls-01-idle-behind", .05f);
                var walk = await Drive(3f, east, 0.56f, 1.6f, "controls-02-walk", 2f);
                await Place(new Vector3(-35f, 0f, -25f), 90f);
                var jog = await Drive(3f, east, 0.75f, 3.5f, "controls-03-jog", 2f);
                await Place(new Vector3(-35f, 0f, -25f), 90f);
                var sprint = await Drive(4f, east, 1f, 6f, "controls-04-sprint", 3f);
                var stopFrom = p.transform.position;
                var stop = await Drive(1.2f, east, 0f);
                float stopDistance = Vector3.Distance(Flat(stopFrom), Flat(p.transform.position));
                o["walk"] = walk.Json(origin); o["jog"] = jog.Json(origin); o["sprint"] = sprint.Json(origin);
                o["sprintStopMetres"] = R(stopDistance); o["stop"] = stop.Json(origin);
                expect(Mathf.Abs(walk.steadySpeed - 1.6f) < 0.15f, $"walk settles at 1.6 m/s ({walk.steadySpeed:F2})");
                expect(Mathf.Abs(jog.steadySpeed - 3.5f) < 0.2f, $"jog settles at 3.5 m/s ({jog.steadySpeed:F2})");
                expect(Mathf.Abs(sprint.steadySpeed - 6f) < 0.3f, $"sprint at the stick's edge settles at 6 m/s ({sprint.steadySpeed:F2})");
                expect(stopDistance > 0.4f && stopDistance < 1.6f, $"a sprint coasts to a stop ({stopDistance:F2} m)");

                // 2) Stairs up and down, and a kerb: stepped, never airborne.
                await Place(new Vector3(-33f, 0f, -15f), 90f);
                var stairs = await Drive(5f, east, 0.75f, 0f, "controls-05-stairs", 1.6f);
                o["stairs"] = stairs.Json(origin);
                expect(stairs.maxY - origin.y > 1.0f && stairs.end.x - origin.x > -23f, "jogged up and down the stairs");
                expect(stairs.airborneFrames == 0 && stairs.landed.Count == 0, $"stairs never fall ({stairs.airborneFrames} airborne frames)");
                await Place(new Vector3(-24f, 0f, -8f), 90f);
                var kerb = await Drive(3f, east, 1f);
                o["kerb"] = kerb.Json(origin);
                expect(kerb.end.x - origin.x > -16f && kerb.airborneFrames == 0 && kerb.stunnedFrames == 0, "sprinted over a 0.25 m kerb: up, down, no fall, no trip");

                // 3) Ledges: 0.45 m is a step; 1.5 m is a fall that the jog carries through; 4 m rolls. Nothing knocks down.
                await Place(new Vector3(-31f, 0.45f, 0f), 90f);
                var low = await Drive(2f, east, 0.45f);
                o["ledge045"] = low.Json(origin);
                expect(low.airborneFrames == 0 && low.landed.Count == 0, "walked off a 0.45 m ledge as a step");
                // Into the knee-high bench at a sprint: blocked, never tripped or knocked down.
                await Place(new Vector3(-27f, 0f, 0f), 90f);
                var bench = await Drive(2.5f, east, 1f);
                o["bench"] = bench.Json(origin);
                expect(bench.stunnedFrames == 0 && !bench.states.Contains("react_trip") && !bench.states.Contains("react_fall"), "a sprint into a bench neither trips nor falls");
                expect(bench.end.x - origin.x < -22.2f, "the bench blocks");
                await Place(new Vector3(-31f, 1.5f, 8f), 90f);
                var mid = await Drive(2.5f, east, 0.75f, 0f, "controls-06-ledge-fall", 0.55f);
                o["ledge150"] = mid.Json(origin);
                expect(mid.landed.Count == 1 && Mathf.Abs(mid.landed[0] - 1.5f) < 0.2f, "a 1.5 m drop is one real fall and one landing");
                expect(mid.stunnedFrames == 0, "a 1.5 m drop does not knock down");
                await Place(new Vector3(-31f, 4f, 16f), 90f);
                var high = await Drive(3f, east, 1f);
                o["ledge400"] = high.Json(origin);
                expect(high.landed.Count == 1 && high.landed[0] > 3.5f && high.stunnedFrames == 0, "a 4 m drop lands (rolls) without a knockdown");

                // 4) Walls: head-on stops, a glancing sprint slides along; no knockdown.
                await Place(new Vector3(24f, 0f, 0f), 90f);
                var wall = await Drive(2.5f, east, 1f);
                o["wallHeadOn"] = wall.Json(origin);
                expect(wall.stunnedFrames == 0 && wall.end.x - origin.x < 29.8f, "a sprint into a wall stops at it, upright");
                await Place(new Vector3(24f, 0f, -10f), 90f);
                var glance = await Drive(2.5f, (east + north).normalized, 1f, 0f, "controls-07-wall-slide", 1.6f);
                o["wallGlance"] = glance.Json(origin);
                expect(glance.stunnedFrames == 0 && glance.end.z - origin.z > -5f, "a glancing sprint slides along the wall");

                // 5) Slopes: 30° is walkable, 55° is not.
                await Place(new Vector3(-4f, 0f, 24f), 90f);
                var ramp30 = await Drive(4f, east, 0.75f);
                o["ramp30"] = ramp30.Json(origin);
                expect(ramp30.maxY - origin.y > 3f, "jogged up a 30° ramp");
                await Place(new Vector3(-4f, 0f, 8f), 90f);
                var ramp55 = await Drive(3f, east, 0.75f);
                o["ramp55"] = ramp55.Json(origin);
                expect(ramp55.maxY - origin.y < 1f, "a 55° slope is not climbable");

                // 6) Camera: behind and recentred while running, pulled in against a wall, closer indoors, orbit.
                await Place(new Vector3(-35f, 0f, -25f), 90f);
                rig.Orbit(new Vector2(-900f, 0f)); // look away to the left
                float yawBefore = Mathf.DeltaAngle(rig.yaw, p.transform.eulerAngles.y);
                await Drive(3.5f, east, 0.75f, 0f, "controls-08-recentre", 3.2f);
                float yawAfter = Mathf.DeltaAngle(rig.yaw, p.transform.eulerAngles.y);
                o["recentre"] = new JObject { ["offsetBeforeDeg"] = R(yawBefore), ["offsetAfterDeg"] = R(yawAfter) };
                expect(Mathf.Abs(yawAfter) < 15f && Mathf.Abs(yawBefore) > 60f, $"the camera swings back behind a jog ({yawBefore:F0}° → {yawAfter:F0}°)");
                await Place(new Vector3(29.4f, 0f, 0f), -90f); // back to the wall: the boom would be inside it
                for (int i = 0; i < 20; i++) await Awaitable.NextFrameAsync();
                float boom = Vector3.Distance(rig.transform.position, p.transform.position + Vector3.up * CameraRig.PivotHeight);
                o["cameraAtWallMetres"] = R(boom);
                var rel = rig.transform.position - p.transform.position;
                o["cameraAtWall"] = new JObject { ["playerYaw"] = R(p.transform.eulerAngles.y), ["rigYaw"] = R(rig.yaw),
                    ["cameraOffset"] = new JArray(R(rel.x), R(rel.y), R(rel.z)), ["lookAlongPlayer"] = R(Vector3.Dot(rig.transform.forward, p.transform.forward)) };
                expect(boom < 1.2f && rig.transform.position.x - origin.x < 29.8f, $"against a wall the camera pulls in on this side ({boom:F2} m)");
                await shot("controls-09-camera-wall", .05f);
                await Place(new Vector3(14f, 0f, -20f), 90f);
                for (int i = 0; i < 40; i++) await Awaitable.NextFrameAsync();
                float inside = Vector3.Distance(rig.transform.position, p.transform.position + Vector3.up * CameraRig.PivotHeight);
                o["cameraIndoorsMetres"] = R(inside); o["interior"] = rig.Interior;
                expect(rig.Interior && inside < CameraRig.OutdoorDistance - 0.5f, $"under a roof the camera sits closer ({inside:F2} m)");
                await shot("controls-10-indoors", .05f);
                await Place(new Vector3(-35f, 0f, -25f), 90f);
                rig.Orbit(new Vector2(500f, -400f));
                for (int i = 0; i < 10; i++) await Awaitable.NextFrameAsync();
                o["orbit"] = new JObject { ["yawOffsetDeg"] = R(Mathf.DeltaAngle(p.transform.eulerAngles.y, rig.yaw)), ["pitch"] = R(rig.pitch) };
                await shot("controls-11-orbit", .05f);
                p.StopDirectInput();

                // Optional film strip for the review (no device attached): 30 fps frames of a walk → jog → sprint → stop
                // with the follow camera, then an orbit drag. -ctlife-controlsClip 1; frames controls-clip-NNNN.
                if (Environment.GetCommandLineArgs().Contains("-ctlife-controlsClip"))
                {
                    await Place(new Vector3(-35f, 0f, -25f), 90f);
                    int n = 0;
                    async Task Film(float seconds, Vector3 dir, float strength, Vector2 orbit = default)
                    {
                        for (float t = 0f; t < seconds; t += 1f / 60f)
                        {
                            p.SetMoveInput(strength > 0f ? Stick(dir, strength) : Vector2.zero);
                            if (orbit != default) rig.Orbit(orbit / 60f);
                            await Awaitable.NextFrameAsync();
                            if ((n++ & 1) == 0) OnboardingCapture.FilmFrame($"controls-clip-{n / 2:0000}");   // no extra frames: real-time timing
                        }
                    }
                    await Film(0.6f, east, 0f);
                    await Film(1.6f, east, 0.56f);
                    await Film(1.6f, east, 0.75f);
                    await Film(2.2f, (east + north * 0.35f).normalized, 1f);
                    await Film(1.0f, east, 0f);
                    await Film(1.6f, east, 0f, new Vector2(-520f, 120f));
                    await Film(2.0f, east, 0.75f);
                    o["clipFrames"] = n / 2;
                }

                // 7) A seat on the plaza: walk to its approach (NavMesh corners where there is one), sit, stand with the stick.
                await visit.FastTravel("timeline_square");
                for (int i = 0; i < 60; i++) await Awaitable.NextFrameAsync();
                var venue = visit.Venue;
                var spots = CTLife.Core.Services.Get<CTLife.Core.ConfigStore>().Registries.SpotsFor("timeline_square");
                string seatId = null;
                if (spots != null)
                    foreach (var sp in spots.spots.Values)
                        if (sp.kind == "seat" && venue.TryGetSpotApproach(sp.id, out _)) { seatId = sp.id; break; }
                if (seatId != null && venue.TryGetSpotApproach(seatId, out var approach) && venue.TryGetSpot(seatId, out var seatPos, out var seatRot))
                {
                    await Place(approach + new Vector3(5f, 0f, 3f) - origin, 0f);
                    var commands = new JArray();
                    void Observe(string c) { if (commands.Count < 12) commands.Add(c); }
                    p.ReviewCommandObserved += Observe;
                    var route = LocalPlayerController.Walkable(p.transform.position, LocalPlayerController.PlanRoute(p.transform.position, approach));
                    var corners = new JArray(); foreach (var c in route) corners.Add(new JArray(R(c.x - approach.x), R(c.y - approach.y), R(c.z - approach.z)));
                    bool ok = p.MoveTo(approach);
                    float t0 = Time.time;
                    while (p.HasDestination && Time.time - t0 < 12f) await Awaitable.NextFrameAsync();
                    float off = Vector3.Distance(Flat(p.transform.position), Flat(approach));
                    p.ReviewCommandObserved -= Observe;
                    var endRel = p.transform.position - approach;
                    var toApproach = Flat(approach - p.transform.position);
                    var b0 = p.transform.position + Vector3.up * (CharacterBody.Radius + 0.05f);
                    var b1 = p.transform.position + Vector3.up * (CharacterBody.Height - CharacterBody.Radius);
                    var near = new JArray();
                    foreach (var c in Physics.OverlapSphere(approach + Vector3.up * 0.8f, 2.5f, CharacterBody.Mask, QueryTriggerInteraction.Ignore))
                    {
                        var bb = c.bounds;
                        if (bb.size.x > 20f || bb.size.z > 20f) continue;
                        near.Add(c.name + " min(" + R(bb.min.x - approach.x) + "," + R(bb.min.z - approach.z) + ") max(" + R(bb.max.x - approach.x) + "," + R(bb.max.z - approach.z) + ") top " + R(bb.max.y - approach.y));
                    }
                    o["seatNear"] = near;
                    o["seatPos"] = new JArray(R(seatPos.x - approach.x), R(seatPos.z - approach.z), R(seatRot * Mathf.Rad2Deg));
                    if (toApproach.sqrMagnitude > 1e-4f && Physics.CapsuleCast(b0, b1, CharacterBody.Radius, toApproach.normalized, out var block, toApproach.magnitude, CharacterBody.Mask, QueryTriggerInteraction.Ignore))
                        o["seatBlockedBy"] = new JObject { ["collider"] = block.collider.name + " (" + block.collider.GetType().Name + ", layer " + block.collider.gameObject.layer + ")",
                            ["path"] = Path(block.collider.transform), ["distance"] = R(block.distance), ["point"] = new JArray(R(block.point.x - approach.x), R(block.point.y - approach.y), R(block.point.z - approach.z)) };
                    o["seatWalk"] = new JObject { ["seat"] = seatId, ["accepted"] = ok, ["arrivedMetres"] = R(off), ["seconds"] = R(Time.time - t0),
                        ["routeFromApproach"] = corners, ["endFromApproach"] = new JArray(R(endRel.x), R(endRel.y), R(endRel.z)), ["commands"] = commands };
                    expect(ok && off < 0.45f, $"walked to {seatId}'s approach ({off:F2} m off)");
                    bool sat = p.EngageLocal(seatId, seatPos, seatRot * Mathf.Rad2Deg, "act_sit", "act_sit_down");
                    for (int i = 0; i < 150; i++) await Awaitable.NextFrameAsync();
                    await shot("controls-12-seated", .05f);
                    var away = await Drive(2f, (Flat(approach - seatPos).sqrMagnitude > 0.01f ? Flat(approach - seatPos) : Vector3.back), 0.6f);
                    o["seatStand"] = new JObject { ["sat"] = sat, ["standing"] = p.SeatedSpot == null, ["moved"] = R(away.travelled) };
                    expect(sat && p.SeatedSpot == null && away.travelled > 0.5f, "sat down, then the stick stood up and walked away");
                }
                else expect(false, "a Timeline Square seat with an approach point");
                p.StopDirectInput();
            }
            finally
            {
                Time.captureFramerate = prevCapture;
                Object.Destroy(course);
            }
            return visit;
        }

        private static string Path(Transform t) { var n = t.name; for (var q = t.parent; q != null && n.Length < 160; q = q.parent) n = q.name + "/" + n; return n; }
        private static float Average(List<float> v) { float s = 0f; foreach (var x in v) s += x; return s / Mathf.Max(1, v.Count); }
        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>The course, relative to <paramref name="o"/> (x east, z north, platform top at o.y).</summary>
        internal static GameObject BuildCourse(Vector3 o)
        {
            var root = new GameObject("controls-course");
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = new Color(0.78f, 0.8f, 0.84f) };
            var accent = new Material(mat) { color = new Color(0.42f, 0.66f, 0.95f) };
            void Box(string name, Vector3 min, Vector3 max, Material m = null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name; go.transform.SetParent(root.transform, false);
                go.transform.position = o + (min + max) * 0.5f;
                go.transform.localScale = max - min;
                go.GetComponent<Renderer>().sharedMaterial = m ?? mat;
            }
            void Ramp(string name, float x0, float run, float angle, float z0, float z1)
            {
                // A slab tilted about z, its top surface rising from (x0, 0) to (x0 + run, run * tan(angle)).
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name; go.transform.SetParent(root.transform, false);
                float len = run / Mathf.Cos(angle * Mathf.Deg2Rad), thick = 0.4f;
                go.transform.localScale = new Vector3(len, thick, z1 - z0);
                var up = Quaternion.Euler(0f, 0f, angle);
                var mid = new Vector3(x0 + run * 0.5f, run * Mathf.Tan(angle * Mathf.Deg2Rad) * 0.5f, (z0 + z1) * 0.5f);
                go.transform.SetPositionAndRotation(o + mid - up * Vector3.up * (thick * 0.5f), up);
                go.GetComponent<Renderer>().sharedMaterial = accent;
            }
            Box("platform", new Vector3(-40f, -1f, -30f), new Vector3(40f, 0f, 30f));
            // Stairs: six 0.18 m risers up, a landing, six down.
            for (int i = 0; i < 6; i++)   // up: x -30..-28.4 in 0.32 m treads, landing -28.4..-26.08 at 1.08 m
                Box("stair-up-" + i, new Vector3(-30f + i * 0.32f, 0f, -16.25f), new Vector3(-26.08f, 0.18f * (i + 1), -13.75f), accent);
            for (int j = 0; j < 6; j++)   // down: x -26.08..-24.16
                Box("stair-down-" + j, new Vector3(-26.08f, 0f, -16.25f), new Vector3(-26.08f + 0.32f * (j + 1), 0.18f * (6 - j), -13.75f), accent);
            Box("kerb", new Vector3(-20f, 0f, -9.5f), new Vector3(-18.8f, 0.25f, -6.5f), accent);
            Box("ledge-045", new Vector3(-32f, 0f, -1.5f), new Vector3(-29f, 0.45f, 1.5f), accent);
            Box("bench", new Vector3(-22f, 0f, -1f), new Vector3(-21.5f, 0.45f, 1f), accent);
            Box("ledge-150", new Vector3(-32f, 0f, 6.5f), new Vector3(-28f, 1.5f, 9.5f), accent);
            Box("ledge-400", new Vector3(-32f, 0f, 14.5f), new Vector3(-28f, 4f, 17.5f), accent);
            Box("wall", new Vector3(30f, 0f, -30f), new Vector3(30.4f, 3f, 30f));
            Ramp("ramp-30", 0f, 6f, 30f, 22.5f, 25.5f);
            Ramp("ramp-55", 0f, 2.5f, 55f, 6.5f, 9.5f);
            // A roofed room open to the west: x 10..18, z -24..-16, roof at 3 m.
            Box("room-n", new Vector3(10f, 0f, -16.2f), new Vector3(18f, 3f, -16f));
            Box("room-s", new Vector3(10f, 0f, -24f), new Vector3(18f, 3f, -23.8f));
            Box("room-e", new Vector3(17.8f, 0f, -24f), new Vector3(18f, 3f, -16f));
            Box("room-roof", new Vector3(10f, 3f, -24f), new Vector3(18f, 3.2f, -16f));
            return root;
        }
    }
}
