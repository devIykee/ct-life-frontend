using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Realtime;
using CTLife.UI;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Phase A feel pass: measures locomotion on the real LocalPlayerController in the shipped Timeline Square at a fixed
    /// 60 fps step (Time.captureFramerate), so before/after numbers compare like for like. Scenarios drive the virtual stick
    /// exactly as MovementControls does (SetMoveInput every frame). Writes receipt["feel"]; never fails a capture by itself
    /// (thresholds live in the caller), so the same probe reports the "before" and the "after".
    /// </summary>
    internal static class FeelProbe
    {
        private sealed class Frame { public float t, speed, yaw, gap, footL, footR; public Vector3 pos, cam, toeL, toeR, hips; public string state; public Vector3[] verts; }

        public static async Task<(JObject, VenueSession)> Run(VenueSession visit, Camera camera, Func<string, float, Task> shot)
        {
            var o = new JObject();
            int prevCapture = Time.captureFramerate;
            Time.captureFramerate = 60;
            try
            {
                LocalPlayerController p = visit.LocalPlayer;
                AvatarView view = p.GetComponent<AvatarView>();
                Animator anim = view.Animator;
                VenueSceneRoot root = visit.Venue;
                async Task<List<Frame>> Drive(float seconds, Func<float, Vector2> stick)
                {
                    var frames = new List<Frame>();
                    float t = 0f;
                    while (t < seconds)
                    {
                        var s = stick(t);
                        p.SetMoveInput(s); // zero = the joystick's own release (MovementControls.FeedInput)
                        await Awaitable.NextFrameAsync();
                        t += Time.deltaTime;
                        frames.Add(Sample(t, p, view.Animator, camera));
                    }
                    return frames;
                }
                async Task Reset(Vector2 local, float yawDeg)
                {
                    p.StopDirectInput(); p.CancelMovement();
                    p.SnapTo(root.ToWorld(local.x, local.y), yawDeg * Mathf.Deg2Rad);
                    CameraRig.Instance?.SnapToTarget();   // a teleport must not leave the camera settling into the next scenario
                    for (int i = 0; i < 90; i++) await Awaitable.NextFrameAsync();
                }
                // CameraRig yaw: the stick is camera-relative; express directions in world terms through it.
                float camYaw = CameraRig.Instance != null ? CameraRig.Instance.yaw : 0f;
                Vector2 Stick(Vector3 worldDir, float strength)
                {
                    var local = Quaternion.Euler(0f, -camYaw, 0f) * worldDir.normalized;
                    return new Vector2(local.x, local.z) * strength;
                }

                // 0) Steps (Timeline Square): walk north from the fountain's north side up the town hall steps.
                await Reset(new Vector2(0f, 8.6f), 0f);
                var steps = await Drive(2.6f, t => Stick(root.transform.forward, 0.6f));
                o["steps"] = Ground(steps);
                o["stepsPath"] = steps.Count > 0 ? $"{root.ToLocal(steps[0].pos)} -> {root.ToLocal(steps[steps.Count - 1].pos)} y {steps[0].pos.y:0.00}->{steps[steps.Count - 1].pos.y:0.00}" : "none";
                // The flat scenarios run on Airdrop Fields' open lawn (no fountain or benches in the lanes).
                await visit.Exit();
                visit = await VenueSession.Enter("airdrop_fields", joinRoom: false, voice: false);
                for (int i = 0; i < 60; i++) await Awaitable.NextFrameAsync();
                p = visit.LocalPlayer; view = p.GetComponent<AvatarView>(); anim = view.Animator; root = visit.Venue;
                camera = CameraRig.Instance.Camera;
                camYaw = CameraRig.Instance.yaw;
                var east = root.transform.right;
                // 1) Start / stop: full stick east for 2.5 s, then release.
                await Reset(new Vector2(-6f, -7.5f), 90f);
                SampleFeet = true;
                var run = await Drive(3.5f, t => t < 2.5f ? Stick(east, 1f) : Vector2.zero);
                o["startStop"] = StartStop(run, 2.5f);
                // 2) Walk (half stick) for foot slide and cadence.
                await Reset(new Vector2(-6f, -7.5f), 90f);
                var walk = await Drive(4f, t => Stick(east, 0.5f));
                SampleFeet = false;
                o["walkSlide"] = Slide(walk.Skip(60).ToList());
                o["runSlide"] = Slide(run.Where(f => f.t > 1.2f && f.t < 2.5f).ToList());
                // 3) Reversal and a full turn circle.
                await Reset(new Vector2(-3f, -7.5f), 90f);
                var rev = await Drive(3f, t => Stick(t < 1.2f ? east : -east, 1f));
                o["reverse"] = Turns(rev);
                await Reset(new Vector2(5f, -4f), 0f);
                var circle = await Drive(4f, t => Stick(Quaternion.Euler(0f, t * 120f, 0f) * root.transform.forward, 0.6f));
                o["circle"] = Turns(circle);
                // 4) Wall slide: run at 30 degrees into the venue's east edge and keep pushing (should glide north along it).
                await Reset(new Vector2(-9.5f, -6f), 300f);
                var wall = await Drive(2.2f, t => Stick(Quaternion.Euler(0f, -60f, 0f) * root.transform.forward, 1f));
                o["wallSlide"] = Wall(wall);
                o["wallPath"] = new JArray(wall.Where((x, i) => i % 15 == 0).Select(x => $"{root.ToLocal(x.pos).x:0.00},{root.ToLocal(x.pos).y:0.00} walk={root.IsWalkable(x.pos)}"));
                // 5) Camera follow over the run and the circle (separately: resets snap the camera).
                // Camera, in REAL time (CameraRig follows on unscaledDeltaTime; a captured clock mixes two clocks):
                // the same run ended the old way (instant StopDirectInput) and the new way (stick release = braking).
                Time.captureFramerate = 0;
                async Task<List<Frame>> RealRun(bool legacyStop)
                {
                    await Reset(new Vector2(-6f, -7.5f), 90f);
                    var frames = new List<Frame>(); float t0 = Time.unscaledTime;
                    while (Time.unscaledTime - t0 < 3.4f)
                    {
                        float t = Time.unscaledTime - t0;
                        if (t < 2.2f) p.SetMoveInput(Stick(east, 1f));
                        else if (legacyStop) p.StopDirectInput(); else p.SetMoveInput(Vector2.zero);
                        await Awaitable.NextFrameAsync();
                        frames.Add(Sample(Time.unscaledTime - t0, p, view.Animator, camera));
                    }
                    return frames;
                }
                var legacy = await RealRun(true);
                var braked = await RealRun(false);
                o["cameraRealTime"] = new JObject
                {
                    ["instantStop_start"] = Cam(Resample(legacy, 0f, 0.8f), p.transform, camera),
                    ["instantStop_stop"] = Cam(Resample(legacy, 2.1f, 3.3f), p.transform, camera),
                    ["braking_start"] = Cam(Resample(braked, 0f, 0.8f), p.transform, camera),
                    ["braking_stop"] = Cam(Resample(braked, 2.1f, 3.3f), p.transform, camera),
                };
                Time.captureFramerate = 60;

                await shot("feel-probe-end", .05f);
                o["remote"] = await Remote(visit, camera);
                await Reset(new Vector2(0f, -3f), 180f);
                o["fingers"] = await Fingers(view);
            }
            finally { Time.captureFramerate = prevCapture; }
            return (o, visit);
        }

        /// <summary>Linear resample of position and camera to a fixed 60 Hz grid (finite differences need even steps).</summary>
        private static List<Frame> Resample(List<Frame> f, float from, float to)
        {
            var o = new List<Frame>();
            int j = 0;
            for (float t = from; t <= to; t += 1f / 60f)
            {
                while (j < f.Count - 2 && f[j + 1].t < t) j++;
                var a = f[j]; var b = f[Mathf.Min(j + 1, f.Count - 1)];
                float k = b.t > a.t ? Mathf.Clamp01((t - a.t) / (b.t - a.t)) : 0f;
                o.Add(new Frame { t = t, pos = Vector3.Lerp(a.pos, b.pos, k), cam = Vector3.Lerp(a.cam, b.cam, k), yaw = a.yaw, state = a.state });
            }
            return o;
        }

        private static Frame Sample(float t, LocalPlayerController p, Animator anim, Camera cam)
        {
            var f = new Frame { t = t, pos = p.transform.position, yaw = p.transform.eulerAngles.y, cam = cam.transform.position };
            f.state = anim != null ? StateName(anim) : "";
            // The shipped rigs use Optimize Game Objects (no bone transforms): read the feet off the skinned mesh itself.
            if (SampleFeet && Feet(p.transform, out var l, out var r, out var top)) { f.toeL = l; f.toeR = r; f.hips = top; f.verts = LastVerts.ToArray(); }
            if (Physics.Raycast(f.pos + Vector3.up * 0.6f, Vector3.down, out var hit, 1.5f, CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore))
                f.gap = f.pos.y - hit.point.y;
            else f.gap = float.NaN;
            return f;
        }

        public static bool SampleFeet = false;
        public static bool OnlyVisible = false, StandStrip = false;
        private static readonly Mesh Baked = new Mesh();
        private static readonly List<Vector3> Verts = new List<Vector3>();
        private static readonly List<Vector3> LastVerts = new List<Vector3>();

        /// <summary>Per side, the centroid of the 24 lowest baked vertices (the sole); <paramref name="top"/> = mesh top (head).</summary>
        private static bool Feet(Transform root, out Vector3 left, out Vector3 right, out Vector3 top)
        {
            left = right = top = Vector3.zero;
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>();
            var lw = new List<Vector3>(); var rw = new List<Vector3>(); float maxY = float.MinValue;
            LastVerts.Clear();
            foreach (var smr in smrs)
            {
                if (!smr.enabled || smr.sharedMesh == null || smr.sharedMesh.vertexCount < 200) continue;
                if (OnlyVisible && !smr.isVisible) continue;   // hidden LOD meshes keep a stale skin pose
                smr.BakeMesh(Baked, true);
                Baked.GetVertices(Verts);
                var m = smr.transform.localToWorldMatrix;
                for (int i = 0; i < Verts.Count; i += 2)
                {
                    var w = m.MultiplyPoint3x4(Verts[i]);
                    LastVerts.Add(w);
                    maxY = Mathf.Max(maxY, w.y);
                    float side = Vector3.Dot(w - root.position, root.right);
                    if (side < 0) lw.Add(w); else rw.Add(w);
                }
            }
            if (lw.Count < 24 || rw.Count < 24) return false;
            left = Low(lw); right = Low(rw); top = new Vector3(root.position.x, maxY, root.position.z);
            return true;
        }

        private static Vector3 Low(List<Vector3> v)
        {
            v.Sort((a, b) => a.y.CompareTo(b.y));
            var c = Vector3.zero; for (int i = 0; i < 24; i++) c += v[i];
            return c / 24f;
        }

        private static string StateName(Animator a)
        {
            var info = a.GetCurrentAnimatorStateInfo(0);
            foreach (var id in new[] { "loco_idle", "loco_walk", "loco_run", "jump_air", "act_sit" })
                if (info.IsName(id)) return id;
            return info.shortNameHash.ToString();
        }

        private static List<float> Speeds(List<Frame> f)
        {
            var s = new List<float> { 0f };
            for (int i = 1; i < f.Count; i++)
            {
                var d = f[i].pos - f[i - 1].pos; d.y = 0f;
                s.Add(d.magnitude / Mathf.Max(1e-4f, f[i].t - f[i - 1].t));
            }
            return s;
        }

        private static JObject StartStop(List<Frame> f, float release)
        {
            var s = Speeds(f);
            float top = s.Where((v, i) => f[i].t > 1.4f && f[i].t < release).DefaultIfEmpty(0f).Average();
            float t90 = f.Where((x, i) => s[i] >= top * 0.9f).Select(x => x.t).DefaultIfEmpty(-1f).First();
            int r = f.FindIndex(x => x.t >= release);
            float stopT = -1f, slide = 0f;
            for (int i = r; i < f.Count && i >= 0; i++)
            {
                if (i > r) { var d = f[i].pos - f[i - 1].pos; d.y = 0f; slide += d.magnitude; }
                if (s[i] < 0.1f) { stopT = f[i].t - release; break; }
            }
            float maxAccel = 0f;
            for (int i = 2; i < s.Count; i++) maxAccel = Mathf.Max(maxAccel, Mathf.Abs(s[i] - s[i - 1]) / Mathf.Max(1e-4f, f[i].t - f[i - 1].t));
            // Animation state changes per second while at steady top speed (flicker at a threshold).
            int flips = 0; for (int i = 1; i < f.Count; i++) if (f[i].state != f[i - 1].state) flips++;
            return new JObject { ["topSpeed"] = R(top), ["t90"] = R(t90), ["stopTime"] = R(stopT), ["stopSlideM"] = R(slide), ["maxAccel"] = R(maxAccel),
                ["stateChanges"] = flips, ["states"] = string.Join(">", f.Select(x => x.state).Where((x, i) => i == 0 || x != f[i - 1].state)) };
        }

        /// <summary>
        /// Planted-foot slide on the real contact: in each frame the lowest vertex on each side starts or continues a stance;
        /// the SAME vertex is followed while it stays within 1.5 cm of its contact height, and its horizontal speed is the slide
        /// (a centroid would drift heel-to-toe as the foot rolls).
        /// </summary>
        private static JObject Slide(List<Frame> f)
        {
            var slides = new List<float>(); var signed = new List<float>(); var soles = new List<float>();
            var frames = f.Where(x => x.verts != null && x.verts.Length > 0).ToList();
            foreach (var fr in frames) soles.Add(fr.verts.Min(x => x.y) - fr.pos.y);
            foreach (int sideSign in new[] { -1, 1 })
            {
                int tracked = -1; float contactY = 0f;
                for (int i = 0; i < frames.Count; i++)
                {
                    var v = frames[i].verts; var root = frames[i].pos;
                    var right = Quaternion.Euler(0f, frames[i].yaw, 0f) * Vector3.right;
                    if (tracked >= 0 && tracked < v.Length && v[tracked].y < contactY + 0.008f && i > 0)
                    {
                        var prev = frames[i - 1].verts;
                        float dt = Mathf.Max(1e-4f, frames[i].t - frames[i - 1].t);
                        if (tracked < prev.Length && Mathf.Abs(v[tracked].y - prev[tracked].y) / dt < 0.1f)
                        {
                            var d = v[tracked] - prev[tracked]; d.y = 0f;
                            slides.Add(d.magnitude / dt);
                            var travel = frames[i].pos - frames[i - 1].pos; travel.y = 0f;
                            if (travel.sqrMagnitude > 1e-8f) signed.Add(Vector3.Dot(d, travel.normalized) / dt);
                            continue;
                        }
                    }
                    // New contact: the lowest vertex on this side, if it is at the floor (within 2 cm of the root height).
                    int best = -1; float by = float.MaxValue;
                    for (int k = 0; k < v.Length; k++)
                        if (Mathf.Sign(Vector3.Dot(v[k] - root, right)) == sideSign && v[k].y < by) { by = v[k].y; best = k; }
                    tracked = best >= 0 && by < root.y + 0.03f ? best : -1;
                    contactY = by;
                }
            }
            slides.Sort();
            var s = Speeds(f);
            return new JObject
            {
                ["bodySpeed"] = R(s.Skip(1).DefaultIfEmpty(0f).Average()),
                ["plantedSamples"] = slides.Count,
                ["slideMedian"] = R(slides.Count > 0 ? slides[slides.Count / 2] : -1f),
                ["slideP90"] = R(slides.Count > 0 ? slides[(int)(slides.Count * 0.9f)] : -1f),
                // + = the planted foot drifts forward with the body (the clip plays too slowly for the speed), - = backward.
                ["signedSlideMean"] = R(signed.Count > 0 ? signed.Average() : 0f),
                ["lowestSoleAboveRoot"] = R(soles.Count > 0 ? soles.Min() : -1f),
                ["meanLowestSole"] = R(soles.Count > 0 ? soles.Average() : -1f),
            };
        }

        private static JObject Turns(List<Frame> f)
        {
            float maxRate = 0f, maxStep = 0f; var rates = new List<float>();
            for (int i = 1; i < f.Count; i++)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(f[i - 1].yaw, f[i].yaw));
                float dt = Mathf.Max(1e-4f, f[i].t - f[i - 1].t);
                maxStep = Mathf.Max(maxStep, d); maxRate = Mathf.Max(maxRate, d / dt); rates.Add(d / dt);
            }
            // Angular jerk: frame-to-frame change of the turn rate (snaps show as spikes).
            float jerk = 0f; for (int i = 1; i < rates.Count; i++) jerk = Mathf.Max(jerk, Mathf.Abs(rates[i] - rates[i - 1]));
            var s = Speeds(f);
            return new JObject { ["maxTurnDegPerS"] = R(maxRate), ["maxYawStepDeg"] = R(maxStep), ["maxTurnRateChange"] = R(jerk),
                ["minSpeedDuring"] = R(s.Skip(10).DefaultIfEmpty(0f).Min()) };
        }

        private static JObject Wall(List<Frame> f)
        {
            // Contact = the first frame the westward motion stops; glide = the speed over the next second.
            var sp = Speeds(f);
            int contact = -1;
            for (int i = 5; i < f.Count; i++) { var d = f[i].pos - f[i - 1].pos; if (Mathf.Abs(d.x) < 0.002f) { contact = i; break; } }
            float glide = contact >= 0 ? sp.Skip(contact + 3).Take(60).DefaultIfEmpty(0f).Average() : -1f;
            // Jitter: direction reversals of horizontal motion between consecutive frames while pushing into the wall.
            int reversals = 0, stalls = 0; float maxPerp = 0f;
            for (int i = 2; i < f.Count; i++)
            {
                var a = f[i - 1].pos - f[i - 2].pos; var b = f[i].pos - f[i - 1].pos; a.y = b.y = 0f;
                if (a.sqrMagnitude > 1e-6f && b.sqrMagnitude > 1e-6f && Vector3.Dot(a.normalized, b.normalized) < 0.2f) reversals++;
                if (b.sqrMagnitude < 1e-8f) stalls++;
                maxPerp = Mathf.Max(maxPerp, Mathf.Abs(Vector3.Cross(a.normalized, b.normalized).y));
            }
            var s = Speeds(f);
            return new JObject { ["directionReversals"] = reversals, ["stalledFrames"] = stalls, ["frames"] = f.Count,
                ["contactAt"] = contact >= 0 ? R(f[contact].t) : -1f, ["glideSpeedAfterContact"] = R(glide),
                ["slideSpeed"] = R(s.Skip(f.Count / 2).DefaultIfEmpty(0f).Average()) };
        }

        private static JObject Ground(List<Frame> f)
        {
            var gaps = f.Where(x => !float.IsNaN(x.gap)).Select(x => Mathf.Abs(x.gap)).ToList();
            float maxDy = 0f, maxHipsDy = 0f;
            for (int i = 1; i < f.Count; i++)
            {
                maxDy = Mathf.Max(maxDy, Mathf.Abs(f[i].pos.y - f[i - 1].pos.y));
                if (f[i].hips != Vector3.zero && f[i - 1].hips != Vector3.zero) maxHipsDy = Mathf.Max(maxHipsDy, Mathf.Abs(f[i].hips.y - f[i - 1].hips.y));
            }
            return new JObject { ["maxFloorGap"] = R(gaps.DefaultIfEmpty(0f).Max()), ["meanFloorGap"] = R(gaps.DefaultIfEmpty(0f).Average()),
                ["maxRootStepY"] = R(maxDy), ["maxHipsStepY"] = R(maxHipsDy), ["climbedY"] = R(f.Count > 0 ? f[f.Count - 1].pos.y - f[0].pos.y : 0f) };
        }

        private static JObject Cam(List<Frame> f, Transform target, Camera cam)
        {
            float maxJerk = 0f, maxAcc = 0f;
            var acc = new List<Vector3>();
            for (int i = 2; i < f.Count; i++)
            {
                float dt = Mathf.Max(1e-4f, f[i].t - f[i - 1].t);
                var a = (f[i].cam - 2f * f[i - 1].cam + f[i - 2].cam) / (dt * dt);
                acc.Add(a); maxAcc = Mathf.Max(maxAcc, a.magnitude);
            }
            for (int i = 1; i < acc.Count; i++) maxJerk = Mathf.Max(maxJerk, (acc[i] - acc[i - 1]).magnitude / Mathf.Max(1e-3f, f[i + 2].t - f[i + 1].t));
            int peak = acc.Count > 0 ? acc.IndexOf(acc.OrderByDescending(x => x.magnitude).First()) + 2 : 0;
            // Collision: frames where solid world geometry sits between the camera and the player's chest.
            int occluded = 0;
            foreach (var fr in f)
                if (Physics.Linecast(fr.pos + Vector3.up * 1.3f, fr.cam, out var hit, CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore) && hit.distance > 0.4f) occluded++;
            // Follow lag: horizontal distance between where the camera looks and the player, while moving.
            return new JObject { ["maxCamAccel"] = R(maxAcc), ["maxCamJerk"] = R(maxJerk), ["occludedFrames"] = occluded, ["frames"] = f.Count, ["peakAt"] = f.Count > peak ? R(f[peak].t) : -1f };
        }

        /// <summary>A remote player fed 10 Hz states with network jitter: per-frame steps vs the true speed show pops and stalls.</summary>
        private static async Task<JObject> Remote(VenueSession visit, Camera camera)
        {
            var o = new JObject();
            foreach (var (name, jitter, loss) in new[] { ("jitter35", 0.035f, 0f), ("jitter70_loss10", 0.07f, 0.1f), ("jitter100_loss20", 0.1f, 0.2f) })
                o[name] = await RemoteCase(visit, jitter, loss);
            return o;
        }

        private static async Task<JObject> RemoteCase(VenueSession visit, float jitter, float loss)
        {
            var view = AvatarFactory.Create("remote-probe", null, false, null, visit.transform);
            var rp = view.gameObject.AddComponent<RemotePlayer>();
            var st = new PlayerState { userId = "probe", name = "probe", x = 0, z = -4, rotY = 0, anim = "loco_walk" };
            rp.Init(visit.Venue, st);
            var rng = new System.Random(7);
            float speed = 3f, t = 0f, nextSend = 0f;
            var rendered = new List<(float t, Vector3 p)>();
            while (t < 6f)
            {
                if (t >= nextSend)
                {
                    float a = t * speed / 4f;   // a 4 m radius circle at 3 m/s
                    st.x = Mathf.Sin(a) * 4f; st.z = -4f + 4f - Mathf.Cos(a) * 4f; st.rotY = a;
                    if (rng.NextDouble() >= loss) rp.Apply(st);
                    nextSend += 0.1f + (float)(rng.NextDouble() - 0.5) * 2f * jitter;   // 10 Hz with network jitter and loss
                }
                await Awaitable.NextFrameAsync();
                t += Time.deltaTime;
                if (t > 0.6f) rendered.Add((t, view.transform.position));
            }
            float expected = speed / 60f, maxStep = 0f; int stalls = 0, pops = 0;
            for (int i = 1; i < rendered.Count; i++)
            {
                var d = rendered[i].p - rendered[i - 1].p; d.y = 0f;
                float m = d.magnitude; maxStep = Mathf.Max(maxStep, m);
                if (m < expected * 0.25f) stalls++;
                if (m > expected * 2f) pops++;
            }
            var seat = new JObject();
            if (jitter < 0.04f) seat = await SeatTiming(visit, rp, view, st);
            UnityEngine.Object.Destroy(view.gameObject);
            return new JObject { ["expectedStep"] = R(expected), ["maxStep"] = R(maxStep), ["popFrames"] = pops, ["stallFrames"] = stalls, ["frames"] = rendered.Count, ["seat"] = seat };
        }

        /// <summary>The shared seat presentation (remote path): time to sit and to stand, and the largest top-of-body jump.</summary>
        private static async Task<JObject> SeatTiming(VenueSession visit, RemotePlayer rp, AvatarView view, PlayerState st)
        {
            // The seat nearest the local player (= on camera): an off-camera Animator (CullCompletely) freezes and then jumps.
            var me = visit.LocalPlayer.transform.position;
            var bench = visit.Venue.Anchors.Where(a => a != null && a.kind == "seat").OrderBy(a => (a.transform.position - me).sqrMagnitude).FirstOrDefault();
            if (bench == null) return new JObject { ["seat"] = "none" };
            // Stand the local player (= the camera target) 2.5 m in front of the bench so the remote is on screen, with the
            // game's own Animator culling (AvatarLod) left as is.
            visit.LocalPlayer.StopDirectInput();
            visit.LocalPlayer.SnapTo(bench.transform.position + bench.transform.forward * 2.5f, 0f);
            CameraRig.Instance?.SnapToTarget();
            for (int i = 0; i < 30; i++) await Awaitable.NextFrameAsync();
            var local = visit.Venue.ToLocal(bench.transform.position);
            st.x = local.x; st.z = local.y - 0.8f; st.spotId = ""; st.anim = "loco_idle"; rp.Apply(st);
            for (int i = 0; i < 30; i++) await Awaitable.NextFrameAsync();
            var trace = new JArray(); float prevModelY = 0f; string prevState = ""; string lastLine = "";
            async Task<(float secs, float jump)> Measure(Func<bool> active)
            {
                float t = 0f, jump = 0f; float prevTop = float.NaN;
                while (t < 4f)
                {
                    await Awaitable.NextFrameAsync(); t += Time.deltaTime;
                    if (Feet(view.transform, out _, out _, out var top))
                    {
                        if (!float.IsNaN(prevTop)) jump = Mathf.Max(jump, Mathf.Abs(top.y - prevTop));
                        float my = view.Animator.transform.position.y;
                        if (!float.IsNaN(prevTop) && Mathf.Abs(top.y - prevTop) > 0.1f)
                            trace.Add($"t={t:0.000} top {prevTop:0.00}->{top.y:0.00} model y {prevModelY:0.000}->{my:0.000} state {prevState}->{StateName(view.Animator)} norm={view.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime:0.00} inTrans={view.Animator.IsInTransition(0)} trans={view.SeatTransitionActive} culled={view.Culled} lod={view.LodTier} animEnabled={view.Animator.enabled} cull={view.Animator.cullingMode} visible={view.GetComponentInChildren<SkinnedMeshRenderer>()?.isVisible}");
                        prevModelY = my; prevState = StateName(view.Animator);
                        prevTop = top.y;
                    }
                    var seatField = typeof(AvatarView).GetField("_seat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(view);
                    var stage = seatField?.GetType().GetProperty("Current")?.GetValue(seatField)?.ToString();
                    string now = StateName(view.Animator) + (view.Animator.IsInTransition(0) ? "~" + view.Animator.GetNextAnimatorStateInfo(0).shortNameHash : "") + " stage=" + stage;
                    if (now != lastLine) { trace.Add($"t={t:0.000} {now}"); lastLine = now; }
                    if (StandStrip && t >= 1.3f && t <= 1.7f) Portrait(view.transform, $"stand-strip-{t:0.000}");
                    if (t > 0.1f && !active()) break;
                }
                return (t, jump);
            }
            OnlyVisible = true;
            st.spotId = bench.spotId; st.anim = string.IsNullOrEmpty(bench.anim) ? "act_sit" : bench.anim; rp.Apply(st);
            var sit = await Measure(() => view.SeatTransitionActive);
            for (int i = 0; i < 60; i++) await Awaitable.NextFrameAsync();
            st.spotId = ""; st.anim = "loco_idle"; rp.Apply(st);
            trace.Add("---- stand ----");
            StandStrip = true;
            var stand = await Measure(() => view.SeatExitActive || view.SeatTransitionActive);
            OnlyVisible = false; StandStrip = false;
            int visibleMeshes = view.GetComponentsInChildren<SkinnedMeshRenderer>().Count(r => r.isVisible), allMeshes = view.GetComponentsInChildren<SkinnedMeshRenderer>().Length;
            trace.Add($"skinned meshes: {allMeshes}, visible {visibleMeshes}");
            return new JObject { ["trace"] = trace, ["seat"] = bench.spotId, ["sitSeconds"] = R(sit.secs), ["sitMaxTopJump"] = R(sit.jump), ["standSeconds"] = R(stand.secs), ["standMaxTopJump"] = R(stand.jump) };
        }

        /// <summary>Finger curl during an emote: proximal/intermediate index and middle joints vs the bind pose.</summary>
        private static async Task<JObject> Fingers(AvatarView view)
        {
            var a = view.Animator;
            if (a == null || !a.isHuman) return new JObject { ["humanoid"] = false };
            // Which finger joints the rig maps at all (Optimize Game Objects hides the transforms, not the mapping).
            var mapped = a.avatar.humanDescription.human.Select(h => h.humanName).Where(n => n.Contains("Index") || n.Contains("Middle") || n.Contains("Thumb") || n.Contains("Ring") || n.Contains("Little")).ToList();
            var desc = new JObject { ["avatar"] = a.avatar.name, ["mappedFingerJoints"] = mapped.Count, ["sample"] = string.Join(",", mapped.Take(6)) };
            if (mapped.Count == 0) return desc;
            var bones = new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal,
                HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.RightIndexIntermediate };
            var present = bones.Where(b => a.GetBoneTransform(b) != null).ToArray();
            // Do the emote and locomotion clips animate the fingers at all? (humanoid finger muscle curves per clip)
            var clips = new JObject();
            foreach (var clip in a.runtimeAnimatorController.animationClips.Distinct())
            {
                if (clip.name.StartsWith("act_sit_") && clip.name.Length > 14) continue; // the fitted seat variants (all animate fingers)
                var fb = UnityEditor.AnimationUtility.GetCurveBindings(clip).Where(b => b.propertyName.Contains("Index.") || b.propertyName.Contains("Middle.") ||
                    b.propertyName.Contains("Thumb.") || b.propertyName.Contains("Ring.") || b.propertyName.Contains("Little.")).ToList();
                // Curl: the "Stretched" muscles (-1 fully curled .. +1 straight); report the most-curled average and the motion range.
                float minAvg = 0f, range = 0f;
                var stretched = fb.Where(b => b.propertyName.Contains("Stretched") && !b.propertyName.Contains("Thumb")).ToList();
                if (stretched.Count > 0)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (float t = 0f; t <= clip.length + 1e-3f; t += Mathf.Max(0.05f, clip.length / 20f))
                    {
                        float avg = stretched.Average(b => UnityEditor.AnimationUtility.GetEditorCurve(clip, b).Evaluate(t));
                        lo = Mathf.Min(lo, avg); hi = Mathf.Max(hi, avg);
                    }
                    minAvg = lo; range = hi - lo;
                }
                var key = System.IO.Path.GetFileNameWithoutExtension(UnityEditor.AssetDatabase.GetAssetPath(clip));
                if (key.StartsWith("emote") || key.StartsWith("loco")) clips[key] = $"curves {fb.Count} curl {R(minAvg)} range {R(range)}";
            }
            desc["fingerCurvesPerClip"] = clips;
            var o = desc; o["exposedFingerBones"] = present.Length;
            foreach (var emote in new[] { "emote_wave", "emote_point", "emote_thumbs_up", "emote_wagmi_fist_pump", "emote_clap" })
            {
                view.PlayOneShot(emote);
                for (int i = 0; i < 24; i++) await Awaitable.NextFrameAsync();
                o["handsLayerWeight_" + emote] = R(view.Hands.Weight);
                Portrait(view.transform, "hands-" + emote);
                float maxCurl = 0f;
                for (int i = 0; i < 60; i++)
                {
                    await Awaitable.NextFrameAsync();
                    foreach (var b in present)
                    {
                        var tr = a.GetBoneTransform(b);
                        if (tr != null) maxCurl = Mathf.Max(maxCurl, Quaternion.Angle(Quaternion.identity, tr.localRotation));
                    }
                }
                o[emote] = R(maxCurl);
                for (int i = 0; i < 90; i++) await Awaitable.NextFrameAsync();
            }
            return o;
        }

        public static string OutDir;

        /// <summary>A front close-up of the upper body (hands at chest height) straight to a PNG.</summary>
        private static void Portrait(Transform actor, string name)
        {
            if (string.IsNullOrEmpty(OutDir)) return;
            var go = new GameObject("hands-cam"); var cam = go.AddComponent<Camera>();
            try
            {
                cam.fieldOfView = 34f; cam.nearClipPlane = 0.05f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.85f, 0.88f, 0.9f);
                bool full = name.StartsWith("stand-strip");
                var target = actor.position + Vector3.up * (full ? 0.9f : 1.25f);
                go.transform.position = full ? target + actor.right * 3.2f + Vector3.up * 0.3f : target + actor.forward * 1.7f + actor.right * 0.15f;
                go.transform.LookAt(target);
                var rt = new RenderTexture(720, 720, 24); cam.targetTexture = rt; cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(720, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 720, 720), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex); cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static float R(float v) => (float)Math.Round(v, 3);
    }
}
