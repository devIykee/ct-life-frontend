using System;
using System.Linq;
using System.Threading.Tasks;
using CTLife.World;
using CTLife.World.Pedestrians;
using CTLife.World.Roads;
using CTLife.World.Vehicles;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Feel lane F6: street pedestrians on the real CT Island v2 at a fixed 60 fps. The crowd builds up round Timeline
    /// Square while traffic runs; then: a signalled crossing watched from its corner (people cross only on red for the
    /// cars), someone walking into the player (they step aside), a bench sitter and a phone user close up. captureSet
    /// "peds"; writes receipt["peds"] and shots peds-*; with -ctlife-pedsClip 1 also film frames peds-clip-NNNN.jpg.
    /// </summary>
    internal static class PedsCapture
    {
        private static double R(float v) => Math.Round(v, 3);

        public static async Task<VenueSession> Run(VenueSession visit, Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, JObject receipt)
        {
            var o = new JObject(); receipt["peds"] = o;
            await visit.Exit();
            visit = await VenueSession.EnterIsland("timeline_square", joinRoom: false);
            await wait(2f);
            var p = visit.LocalPlayer;
            var rig = CameraRig.Instance;
            var peds = PedestrianSystem.Current;
            var net = RoadNetwork.Active;
            expect(peds != null && net != null, "the island session runs pedestrians");
            if (peds == null || net == null) return visit;
            int prevCapture = Time.captureFramerate;
            Time.captureFramerate = 60;
            bool film = Environment.GetCommandLineArgs().Contains("-ctlife-pedsClip");
            int frame = 0;
            float holdYaw = float.NaN, holdPitch = 0f;
            int maxCount = 0, inRoad = 0, unsafeStarts = 0, crossings = 0, maxSit = 0, maxPhone = 0, maxPause = 0, maxWait = 0, maxCross = 0;
            float worstStep = 0f;
            var roadSamples = new JArray(); o["inRoad"] = roadSamples;
            async Task Frames(float seconds, Action<float> each = null)
            {
                for (float t = 0f; t < seconds; t += 1f / 60f)
                {
                    if (!float.IsNaN(holdYaw)) { rig.yaw = holdYaw; rig.pitch = holdPitch; rig.Orbit(Vector2.zero); }
                    await Awaitable.NextFrameAsync();
                    if (film && (frame++ & 1) == 0) OnboardingCapture.FilmFrame($"peds-clip-{frame / 2:0000}");
                    each?.Invoke(t);
                    maxCount = Math.Max(maxCount, peds.People.Count);
                    maxSit = Math.Max(maxSit, peds.CountIn(Pedestrian.Mode.Sit));
                    maxPhone = Math.Max(maxPhone, peds.CountIn(Pedestrian.Mode.Phone));
                    maxPause = Math.Max(maxPause, peds.CountIn(Pedestrian.Mode.Pause));
                    maxWait = Math.Max(maxWait, peds.CountIn(Pedestrian.Mode.Wait));
                    maxCross = Math.Max(maxCross, peds.CountIn(Pedestrian.Mode.Cross));
                    worstStep = Mathf.Max(worstStep, peds.StepMs);
                    if ((Time.frameCount % 15) == 0)
                        foreach (var q in peds.People)
                        {
                            if (q.State == Pedestrian.Mode.Cross || q.State == Pedestrian.Mode.Down) continue;
                            var lp = net.NearestLanePoint(q.transform.position, 6f);
                            if (lp.valid && lp.distance < 1.2f)
                            {
                                inRoad++;
                                if (roadSamples.Count < 12)
                                    roadSamples.Add($"{q.State} path {(q.PathIndex >= 0 ? PedestrianPaths.Island.Paths[q.PathIndex].id : "-")} s {q.Along:F1} lat {q.Lateral:F2} at {q.transform.position:F1} lane {lp.segmentId}#{lp.lane} {lp.distance:F2} m");
                            }
                        }
                }
            }
            void Look(Vector3 from, Vector3 at, float pitch)
            {
                var d = at - from; d.y = 0f;
                holdYaw = d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d).eulerAngles.y : rig.yaw; holdPitch = pitch;
            }
            var arrival = p.transform.position;
            try
            {
                // ---------------------------------------------------------------- the crowd builds up
                await Frames(40f);
                var paths = PedestrianPaths.Island;
                o["network"] = new JObject { ["paths"] = paths.Paths.Count, ["km"] = R(paths.TotalLength / 1000f), ["nodes"] = paths.Nodes.Count, ["crossings"] = paths.Crossings.Count, ["benchesNear"] = peds.Benches };
                o["crowd"] = new JObject { ["now"] = peds.People.Count, ["target"] = peds.Target(p.transform.position), ["spawned"] = peds.Spawned, ["looks"] = peds.People.Select(q => q.Archetype).Distinct().Count(),
                    ["paceMin"] = R(peds.People.Count > 0 ? peds.People.Min(q => q.Pace) : 0f), ["paceMax"] = R(peds.People.Count > 0 ? peds.People.Max(q => q.Pace) : 0f) };
                expect(peds.People.Count >= 6, $"people on the streets round Timeline Square ({peds.People.Count})");
                expect(peds.People.Select(q => q.Archetype).Distinct().Count() >= 4, "varied looks");
                await shot("peds-01-square", .2f);

                // ---------------------------------------------------------------- a signalled crossing from its corner
                PedestrianPaths.Crossing watch = null; float best = float.MaxValue;
                foreach (var c in paths.Crossings)
                {
                    if (!c.signal) continue;
                    float d = Vector3.Distance(c.a, arrival);
                    if (d < best) { best = d; watch = c; }
                }
                if (watch != null)
                {
                    var mid = (watch.a + watch.b) * 0.5f;
                    var across = (watch.b - watch.a); across.y = 0f; across.Normalize();
                    var side = new Vector3(across.z, 0f, -across.x);
                    var stand = watch.a - across * 2f + side * 7f;
                    stand.y = watch.a.y;
                    p.SnapTo(stand, 0f); rig.FollowAvatar(p.transform);
                    Look(stand, mid, 14f);
                    // nudge walkers onto this crossing so the clip shows it used
                    peds.CrossChance = 0.9f;
                    await Frames(30f);
                    peds.CrossChance = 0.35f;
                    await shot("peds-02-crossing", .1f);
                    o["crossing"] = new JObject { ["id"] = watch.id, ["junction"] = watch.junction, ["fromArrivalM"] = R(best) };
                }

                // ---------------------------------------------------------------- walking into the player
                holdYaw = float.NaN;
                Pedestrian walker = null;
                foreach (var q in peds.People)
                {
                    if (q.State != Pedestrian.Mode.Walk || q.PathIndex < 0 || (q.transform.position - p.transform.position).sqrMagnitude > 80f * 80f) continue;
                    var qp = paths.Paths[q.PathIndex];
                    float ahead = q.Direction > 0 ? qp.Length - q.Along : q.Along;
                    if (ahead < 12f || qp.exits.Any(e => Mathf.Abs(e.s - q.Along) < 10f)) continue;   // a straight stretch, no crossing to turn off at
                    walker = q; break;
                }
                if (walker != null)
                {
                    var path = paths.Paths[walker.PathIndex];
                    float s = Mathf.Clamp(walker.Along + walker.Direction * 6f, 0f, path.Length);
                    var block = path.Sample(s, out var dir);
                    var heading = dir * walker.Direction;
                    var right = new Vector3(heading.z, 0f, -heading.x);
                    var me = block + right * walker.Lateral;   // right in their way
                    p.SnapTo(me, Mathf.Atan2(-heading.x, -heading.z)); rig.FollowAvatar(p.transform);
                    Look(me, me - heading, 10f);
                    float closest = float.MaxValue, lateralFrom = walker.Lateral, lateralMost = 0f;
                    await Frames(4f, t =>
                    {
                        var d = walker.transform.position - p.transform.position; d.y = 0f;
                        closest = Mathf.Min(closest, d.magnitude);
                        lateralMost = Mathf.Max(lateralMost, Mathf.Abs(walker.Lateral - lateralFrom));
                    });
                    await shot("peds-03-step-aside", .1f);
                    o["stepAside"] = new JObject { ["closestM"] = R(closest), ["sidestepM"] = R(lateralMost), ["state"] = walker.State.ToString() };
                    expect(closest > 0.45f, $"a walker steps round the player (closest {closest:F2} m)");
                    expect(lateralMost > 0.4f, $"they move over to pass ({lateralMost:F2} m)");
                }

                // ---------------------------------------------------------------- close-ups: a bench, a phone
                holdYaw = float.NaN;
                p.SnapTo(arrival, 0f); rig.FollowAvatar(p.transform);
                await Frames(25f);
                foreach (var mode in new[] { Pedestrian.Mode.Sit, Pedestrian.Mode.Phone, Pedestrian.Mode.Pause })
                {
                    var q = peds.People.Where(x => x.State == mode).OrderBy(x => (x.transform.position - p.transform.position).sqrMagnitude).FirstOrDefault();
                    if (q == null) continue;
                    var at = q.transform.position + q.transform.forward * 3.4f + q.transform.right * 2.4f;   // the player off to one side
                    at.y = q.transform.position.y;
                    p.SnapTo(at, 0f); rig.FollowAvatar(p.transform);
                    Look(at, q.transform.position, 6f);
                    await Frames(0.6f);
                    await shot("peds-04-" + mode.ToString().ToLowerInvariant(), .05f);
                }
                holdYaw = float.NaN;

                foreach (var q in peds.People) { unsafeStarts += q.UnsafeStarts; crossings += q.Crossed; }
                o["behaviour"] = new JObject
                {
                    ["maxCount"] = maxCount, ["maxSitting"] = maxSit, ["maxOnPhone"] = maxPhone, ["maxAtShops"] = maxPause,
                    ["maxWaitingToCross"] = maxWait, ["maxCrossing"] = maxCross, ["crossedNow"] = crossings, ["unsafeStarts"] = unsafeStarts,
                    ["inRoadSamples"] = inRoad, ["despawned"] = peds.Despawned,
                };
                o["perf"] = new JObject { ["stepMs"] = R(peds.StepMs), ["worstStepMs"] = R(worstStep) };
                expect(maxCross > 0, "people cross the road at crossings");
                expect(unsafeStarts == 0, $"nobody steps out against the lights or in front of a car ({unsafeStarts})");
                expect(inRoad == 0, $"nobody walks in the road off a crossing ({inRoad} samples)");
                expect(maxSit > 0 || peds.Benches == 0, $"someone sits on a bench ({maxSit}, {peds.Benches} bench seats near)");
                expect(maxPhone > 0 && maxPause > 0, "people stop on the phone and at shop fronts");
            }
            finally
            {
                Time.captureFramerate = prevCapture;
            }
            return visit;
        }
    }
}
