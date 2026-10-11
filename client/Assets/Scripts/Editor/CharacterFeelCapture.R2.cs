using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Realtime;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Round 2 seat-fit verification: every seat kind in Spaces Studio v4, Terminal Café and Timeline Square, both families
    /// (and a 0.88-scale look), seated through the production path (RemotePlayer + PlayerState with a spot), then measured
    /// on the baked visible skin against the venue's RENDERED geometry:
    ///   pelvis gap  = pelvis vertices to the seat surface under them (negative = sunk in; allowed down to the soft sink),
    ///   sole gap    = lowest sole vertices to the floor / footrest under them (feet planted),
    ///   back clip   = lumbar/chest vertices behind the backrest surface.
    /// Close-up renders per case. -ctlife-captureSet r2seats -ctlife-captureOut …/character-feel/round2 → round2/seats/.
    /// </summary>
    public static partial class CharacterFeelCapture
    {
        private const int SeatScratch = 30;

        private static readonly (string venue, string spot)[] SeatCases =
        {
            ("spaces_studio", "stage_4"), ("spaces_studio", "stage_1"), ("spaces_studio", "seat_1"), ("spaces_studio", "seat_11"),
            ("spaces_studio", "seat_16"), ("spaces_studio", "greenroom_1"),
            ("terminal_cafe", "table_1"), ("terminal_cafe", "terminal_1"), ("terminal_cafe", "terminal_3"), ("terminal_cafe", "terminal_5"),
            ("timeline_square", "bench_1"), ("timeline_square", "bench_4"),
        };
        // m and f references (trousers), plus a dress look: same f skeleton and placement, its skirt is skinned to the pelvis.
        private static readonly string[] SeatLooks = { "player_m_kol", "player_f_builder", "player_f_kol" };
        private static readonly HashSet<string> SkirtLooks = new HashSet<string> { "player_f_kol", "player_f_coat", "player_f_lagos_ankara", "player_f_lagos_kaftan" };
        private static readonly Dictionary<string, Vector3> HipsAt = new Dictionary<string, Vector3>();

        /// <param name="all">Every measured seat of the three venues, m and f references, numbers only (no renders).</param>
        private static async Task CaptureSeats(bool all)
        {
            var dir = Path.Combine(_out, "seats");
            Directory.CreateDirectory(dir);
            var results = new JObject();
            Receipt["seats"] = results;
            foreach (var venueId in SeatCases.Select(c => c.venue).Distinct())
            {
                Step("seats " + venueId);
                var st = await Open(venueId);
                var scratch = new List<GameObject>();
                try
                {
                    foreach (var mf in st.venue.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf.sharedMesh == null || mf.name.Contains("_LOD1") || mf.name.Contains("_LOD2")) continue;
                        var r = mf.GetComponent<MeshRenderer>(); if (r == null || !r.enabled) continue;
                        var go = new GameObject("seatcheck") { layer = SeatScratch, hideFlags = HideFlags.HideAndDontSave };
                        go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                        go.transform.localScale = mf.transform.lossyScale;
                        go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                        scratch.Add(go);
                    }
                    Physics.SyncTransforms();
                    var spots = all
                        ? st.venue.Anchors.Where(a => SeatFitCatalog.Instance.TryGet(venueId, a.spotId, out _)).Select(a => a.spotId).OrderBy(x => x).ToList()
                        : SeatCases.Where(c => c.venue == venueId).Select(c => c.spot).ToList();
                    foreach (var spot in spots)
                    {
                        var anchor = st.venue.Anchors.FirstOrDefault(a => a.spotId == spot);
                        if (anchor == null) { Failures.Add($"seats/{venueId}/{spot}: no anchor"); continue; }
                        foreach (var look in all ? SeatLooks.Where(l => !SkirtLooks.Contains(l)) : SeatLooks)
                        {
                            var key = $"{venueId}/{spot}/{look}";
                            var only = Arg("-ctlife-seatOnly");
                            if (!string.IsNullOrEmpty(only) && !key.Contains(only)) continue;
                            try { results[key] = await SeatCase(st, anchor, look, all ? null : dir, key); }
                            catch (Exception e) { Failures.Add($"seats/{key}: {e.Message}"); Debug.LogException(e); }
                        }
                    }
                }
                finally { foreach (var g in scratch) Object.DestroyImmediate(g); await Close(st); }
            }
        }

        private static async Task<JObject> SeatCase(Stage st, SpotAnchor anchor, string look, string dir, string key)
        {
            var o = new JObject();
            var view = AvatarFactory.Create("seat:" + look, LookFor(CharacterRegistry.Instance, look), false, null, st.root);
            st.spawned.Add(view.gameObject);
            // Measurement only: expose the bone transforms (optimized imports hide them) so skin vertices map to bones.
            // The pose is identical; only the transform write-back cost changes.
            AnimatorUtility.DeoptimizeTransformHierarchy(view.Animator.gameObject);
            // Production path: a remote player standing at the seat's approach, then the server's seated state (spot +
            // anim) arrives: sit-down presentation, fitted state, seat height and model offset all come from AvatarView.
            var standAt = st.venue.TryGetSpotApproach(anchor.spotId, out var ap) ? ap : anchor.transform.position - anchor.transform.forward * 0.6f;
            var standLocal = st.venue.ToLocal(standAt);
            var local = st.venue.ToLocal(anchor.transform.position);
            var remote = view.gameObject.AddComponent<RemotePlayer>();
            remote.Init(st.venue, new PlayerState { name = look, x = standLocal.x, z = standLocal.y, rotY = anchor.RotY, anim = "loco_idle" });
            await Seconds(0.4f);
            var ps = new PlayerState
            {
                name = look, x = local.x, z = local.y, rotY = anchor.RotY, anim = anchor.anim,
                spotId = st.venue.WireSpotId(anchor.spotId),
            };
            remote.Apply(ps);
            var f = anchor.transform.forward; f.y = 0; f.Normalize();
            var right = Vector3.Cross(Vector3.up, f);
            st.rig.Target = view.transform; st.rig.SnapToTarget();
            // Sit-down presentation, then the offset ramp (time constant 0.3 s).
            for (float t = 0f; t < 6f && (view.SeatTransitionActive || view.Animator.IsInTransition(0) || StateOf(view) == "act_sit_down"); t += 0.1f)
                await Seconds(0.1f);
            await Seconds(1.4f);
            var place = SeatPlacement.Resolve(view.Animator, anchor.anim, ps.spotId, null);
            o["state"] = StateOf(view) == "other" ? ShortState(view.Animator) : StateOf(view);
            o["seat_kind"] = place.seat != null ? place.seat.kind.ToString() : "unmeasured";
            o["seat_height_param"] = Math.Round(view.Animator.GetFloat("seatHeight"), 3);
            o["offset"] = $"{view.SeatOffset.x:F3},{view.SeatOffset.y:F3},{view.SeatOffset.z:F3}";
            o["controller"] = view.Animator.runtimeAnimatorController != null ? view.Animator.runtimeAnimatorController.name : "-";
            o["family"] = SeatPlacement.Family(view.Animator);
            o["clips"] = string.Join(", ", view.Animator.GetCurrentAnimatorClipInfo(0).Select(c => $"{c.clip.name}:{c.weight:F2}"));
            var hipsBone = view.Animator.GetBoneTransform(HumanBodyBones.Hips);
            var kneeL = view.Animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var thighL = view.Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var footL = view.Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            o["knee_angle"] = Math.Round(Vector3.Angle(thighL.position - kneeL.position, footL.position - kneeL.position), 1);
            // Seat space (anchor: x right, y up, z forward): where the joints landed.
            string L(HumanBodyBones bb) { var t = view.Animator.GetBoneTransform(bb); var q = anchor.transform.InverseTransformPoint(t.position); return $"({q.y:F3},{q.z:F3})"; }
            o["seat_space_yz"] = $"hips {L(HumanBodyBones.Hips)} kneeL {L(HumanBodyBones.LeftLowerLeg)} ankleL {L(HumanBodyBones.LeftFoot)} toesL {L(HumanBodyBones.LeftToes)} ankleR {L(HumanBodyBones.RightFoot)} toesR {L(HumanBodyBones.RightToes)}";
            if (place.seat != null && place.seat.workTop > 0f)
            {
                // Hands vs the work surface: height above what is under each hand, and forward distance to the device.
                string Hand(HumanBodyBones hb)
                {
                    var hp = view.Animator.GetBoneTransform(hb).position;
                    var q = anchor.transform.InverseTransformPoint(hp);
                    float under = Physics.Raycast(hp + Vector3.up * 0.2f, Vector3.down, out var hh, 0.6f, 1 << SeatScratch) ? hp.y - hh.point.y : float.NaN;
                    return $"y {q.y:F3} z {q.z:F3} above-surface {under:F3}";
                }
                o["hands"] = $"L {Hand(HumanBodyBones.LeftHand)} | R {Hand(HumanBodyBones.RightHand)}";
                o["work"] = $"top {place.seat.workTop:F3} near {place.seat.workNear:F2} device z {place.seat.workZ:F2} top {place.seat.workDeviceTop:F3}";
            }
            if (place.seat != null) o["seat_geometry"] = $"top {place.seat.seatTop:F3} back {place.seat.backDistance:F3} rear {place.seat.rearEdge:F3} front {place.seat.frontEdge:F3} floor {place.seat.floor:F3} footrest {place.seat.footrest:F3}@{place.seat.footrestForward:F3}";
            bool skirt = SkirtLooks.Contains(look);
            if (Arg("-ctlife-seatDiag") == "1")
            {
                var an = view.Animator;
                var hp = new HumanPose();
                using (var h = new HumanPoseHandler(an.avatar, an.transform)) h.GetHumanPose(ref hp);
                string[] legs = { "Left Upper Leg Front-Back", "Left Lower Leg Stretch", "Left Foot Up-Down", "Spine Front-Back" };
                int I(string n) => Array.IndexOf(HumanTrait.MuscleName, n);
                var expect = new Dictionary<string, float>();
                float nt = an.GetCurrentAnimatorStateInfo(0).normalizedTime;
                foreach (var ci in an.GetCurrentAnimatorClipInfo(0))
                {
                    var b = AnimationUtility.GetCurveBindings(ci.clip).ToDictionary(x => x.propertyName, x => x);
                    foreach (var n in legs)
                        if (b.TryGetValue(n, out var bb)) expect[n] = expect.GetValueOrDefault(n) + ci.weight * AnimationUtility.GetEditorCurve(ci.clip, bb).Evaluate((nt % 1f) * ci.clip.length);
                }
                o["diag_avatar"] = $"{an.avatar.name} human {an.isHuman} scale {an.humanScale:F3} lossy {an.transform.lossyScale.x:F3} speed {an.speed:F2} applyRoot {an.applyRootMotion} nt {nt:F2}";
                string B(HumanBodyBones bb) { var t = an.GetBoneTransform(bb); return t == null ? "-" : an.transform.InverseTransformPoint(t.position).ToString("F3"); }
                o["diag_bones"] = $"hipJoint {B(HumanBodyBones.LeftUpperLeg)} knee {B(HumanBodyBones.LeftLowerLeg)} ankle {B(HumanBodyBones.LeftFoot)} toes {B(HumanBodyBones.LeftToes)}";
                o["diag_muscles"] = string.Join(" ", legs.Select(n => $"{n}={hp.muscles[I(n)]:F3}/{expect.GetValueOrDefault(n, float.NaN):F3}"));
            }
            Measure(view, anchor, place.seat, skirt, o);
            // A dress look shares the f skeleton, state and placement: its hips joint must land where the trousered f's did.
            string seatKey = key.Substring(0, key.LastIndexOf('/')) + "/" + o["family"];
            if (!skirt) HipsAt[seatKey] = hipsBone.position;
            else if (HipsAt.TryGetValue(seatKey, out var twin)) o["hips_vs_trousered_twin"] = Math.Round((hipsBone.position - twin).magnitude, 4);
            // Renders: a 3/4 view from the front-right and a straight side view (contacts: hips, back, feet).
            await Frame();
            var focus = anchor.transform.position + Vector3.up * 0.5f;
            if (dir != null)
            {
            var shots = new List<Texture2D>();
            // Side view from whichever side has a clear line to the seat (venue props sit close to some seats).
            var sideAt = focus + f * 0.25f;
            var side = sideAt + right * 2.1f + Vector3.up * 0.05f;
            if (Physics.Linecast(sideAt + right * 0.45f, side, 1 << SeatScratch)) side = sideAt - right * 2.1f + Vector3.up * 0.05f;
            foreach (var (pos, at) in new[]
                     {
                         (focus + f * 1.55f + right * 1.05f + Vector3.up * 0.55f, focus),
                         (side, sideAt),
                     })
            {
                var cam = DetailCam(st, pos, at);
                shots.Add(Render(cam));
                Object.Destroy(cam.gameObject);
            }
            var strip = Stitch(shots);
            File.WriteAllBytes(Path.Combine(dir, key.Replace('/', '_') + ".png"), strip.EncodeToPNG());
            foreach (var t in shots) Object.Destroy(t);
            Object.Destroy(strip);
            o["file"] = key.Replace('/', '_') + ".png";
            }
            // Stand-up presentation (exit strip) on the soft and high seats and Codex's café chair.
            if (dir != null && look != "player_f_kol" && (anchor.spotId == "seat_1" || anchor.spotId == "stage_1" || anchor.spotId == "table_1" || anchor.spotId == "terminal_5"))
            {
                var exitCam = DetailCam(st, focus + f * 1.9f + right * 1.4f + Vector3.up * 0.7f, focus + f * 0.3f);
                var leave = new PlayerState { name = look, x = standLocal.x, z = standLocal.y, rotY = anchor.RotY, anim = "loco_idle" };
                remote.Apply(leave);
                var frames = new List<Texture2D>();
                var exitRows = new JArray();
                for (int i = 0; i < 6; i++)
                {
                    await Frame();
                    frames.Add(Render(exitCam));
                    var hp = view.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    var q = anchor.transform.InverseTransformPoint(hp);
                    exitRows.Add($"t{i * 0.3f:F1} {StateOf(view)} hips ({q.y:F2},{q.z:F2}) offset {view.SeatOffset.y:F3}");
                    await Seconds(0.3f);
                }
                var exitStrip = Stitch(frames);
                File.WriteAllBytes(Path.Combine(dir, key.Replace('/', '_') + "_exit.png"), exitStrip.EncodeToPNG());
                foreach (var t in frames) Object.Destroy(t);
                Object.Destroy(exitStrip);
                Object.Destroy(exitCam.gameObject);
                o["exit"] = exitRows;
            }
            // Pass rule (fitted contact): pelvis on the seat within the soft sink, soles on the support, no back clipping.
            float sink = place.seat != null ? SeatFitCatalog.Sink(place.seat.kind) : 0f;
            bool reach = (float)o["sole_reachable"] > 0.5f;
            var issues = new List<string>();
            if (!skirt)
            {
                float pg = (float)o["pelvis_gap"];
                if ((int)o["pelvis_vertices"] == 0) issues.Add("no pelvis vertices (measurement)");
                else if (pg > 0.015f) issues.Add($"floating {pg:F3}");
                else if (pg < -(sink + 0.025f)) issues.Add($"sunk {pg:F3} (sink {sink:F2})");
            }
            else if (o["hips_vs_trousered_twin"] != null && (float)o["hips_vs_trousered_twin"] > 0.005f) issues.Add($"hips {(float)o["hips_vs_trousered_twin"]:F3} from the trousered f");
            if (reach && (float)o["sole_gap"] > 0.02f) issues.Add($"feet hover {(float)o["sole_gap"]:F3}");
            if ((float)o["sole_min"] < -0.025f) issues.Add($"feet sunk {(float)o["sole_min"]:F3}");
            float backAllow = place.seat != null && place.seat.kind == SeatKind.BeanBag ? sink : 0f;
            if ((float)o["back_clip"] > backAllow + 0.025f) issues.Add($"back clip {(float)o["back_clip"]:F3}");
            o["issues"] = new JArray(issues);
            if (issues.Count > 0) Failures.Add($"seats/{key}: {string.Join(", ", issues)}");
            Object.Destroy(view.gameObject);
            st.spawned.Remove(view.gameObject);
            return o;
        }

        private static string ShortState(Animator a)
        {
            var i = a.GetCurrentAnimatorStateInfo(0);
            foreach (var s in new[] { "act_sit_lounge", "act_sit_beanbag", "act_sit_stool", CafeSeatMotionProfile.SocialState, CafeSeatMotionProfile.TypingState })
                if (i.IsName(s)) return s;
            return "other";
        }

        /// <summary>Contacts on the baked visible skin against the scratch colliders of the venue's render meshes.</summary>
        private static void Measure(AvatarView view, SpotAnchor anchor, SeatFitCatalog.Seat seat, bool skirt, JObject o)
        {
            var a = view.Animator;
            var smr = view.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.isVisible) ?? view.Body;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var v = baked.vertices; var m = smr.transform.localToWorldMatrix;
            var w = smr.sharedMesh.boneWeights;
            int Idx(HumanBodyBones b) { var t = a.GetBoneTransform(b); return t == null ? -2 : Array.IndexOf(smr.bones, t); }
            int hips = Idx(HumanBodyBones.Hips);
            var hipsJoint = a.GetBoneTransform(HumanBodyBones.Hips).position;
            var spine = new HashSet<int> { Idx(HumanBodyBones.Spine), Idx(HumanBodyBones.Chest), Idx(HumanBodyBones.UpperChest) };
            var feet = new HashSet<int> { Idx(HumanBodyBones.LeftFoot), Idx(HumanBodyBones.RightFoot), Idx(HumanBodyBones.LeftToes), Idx(HumanBodyBones.RightToes) };
            int Dom(BoneWeight x) => x.weight0 >= x.weight1 && x.weight0 >= x.weight2 && x.weight0 >= x.weight3 ? x.boneIndex0 : x.weight1 >= x.weight2 && x.weight1 >= x.weight3 ? x.boneIndex1 : x.weight2 >= x.weight3 ? x.boneIndex2 : x.boneIndex3;
            var world = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) world[i] = m.MultiplyPoint3x4(v[i]);
            Object.Destroy(baked);
            float Below(Vector3 p, float up)
            {
                return Physics.Raycast(p + Vector3.up * up, Vector3.down, out var hit, up + 1.5f, 1 << SeatScratch, QueryTriggerInteraction.Collide) ? p.y - hit.point.y : float.NaN;
            }
            var pelvisAll = Enumerable.Range(0, v.Length).Where(i => Dom(w[i]) == hips).ToList();
            // Buttocks: the lowest pelvis vertices near the joint (a trousered look's lowest pelvis vertices are the seat).
            var pelvis = pelvisAll.Where(i => (world[i] - hipsJoint).sqrMagnitude < 0.2f * 0.2f).OrderBy(i => world[i].y).Take(12).ToList();
            var pg = pelvis.Select(i => Below(world[i], 0.3f)).Where(g => !float.IsNaN(g)).ToList();
            o["pelvis_gap"] = pg.Count > 0 ? Math.Round(pg.Average(), 3) : 0;
            o["pelvis_vertices"] = pg.Count;
            if (skirt) o["pelvis_gap_note"] = "skirt skinned to the pelvis: the hem is measured, not the seat contact (see hips_vs_trousered_twin)";
            float hipContactY = pelvis.Count > 0 ? pelvis.Average(i => world[i].y) : hipsJoint.y - 0.08f;
            if (skirt)
            {
                // The skirt: how deep pelvis-skinned vertices go under the surface below them (asset weighting, not placement).
                float deepest = 0f;
                foreach (var i in pelvisAll) { float g = Below(world[i], 0.5f); if (!float.IsNaN(g) && -g > deepest) deepest = -g; }
                o["skirt_clip"] = Math.Round(deepest, 3);
            }
            // Soles: per side, the lowest 8 sole vertices; gap to whatever supports them (floor or footrest).
            var right = view.transform.right;
            float SoleGap(bool left)
            {
                var c = view.transform.position;
                var side = Enumerable.Range(0, v.Length).Where(i => feet.Contains(Dom(w[i])) && (Vector3.Dot(world[i] - c, right) < 0) == left)
                    .OrderBy(i => world[i].y).Take(8).Select(i => Below(world[i], 0.12f)).Where(g => !float.IsNaN(g)).ToList();
                return side.Count > 0 ? side.Min() : float.NaN;
            }
            float sl = SoleGap(true), sr = SoleGap(false);
            o["sole_gap_l"] = float.IsNaN(sl) ? null : Math.Round(sl, 3);
            o["sole_gap_r"] = float.IsNaN(sr) ? null : Math.Round(sr, 3);
            o["sole_gap"] = float.IsNaN(sl) && float.IsNaN(sr) ? 0 : Math.Round(Mathf.Max(float.IsNaN(sl) ? -9f : sl, float.IsNaN(sr) ? -9f : sr), 3);
            o["sole_min"] = float.IsNaN(sl) && float.IsNaN(sr) ? 0 : Math.Round(Mathf.Min(float.IsNaN(sl) ? 9f : sl, float.IsNaN(sr) ? 9f : sr), 3);
            // Reachable: the fitted blend covers this height (very high seats without a footrest: feet dangle by design).
            o["sole_reachable"] = seat == null ? 0 : seat.kind == SeatKind.Stool && seat.footrest <= 0f ? 0 : 1;
            // Back: lumbar/chest vertices (12+ cm above the hip contact) behind the backrest's front surface.
            var f = anchor.transform.forward; f.y = 0; f.Normalize();
            float clip = 0f;
            foreach (var i in Enumerable.Range(0, v.Length).Where(i => spine.Contains(Dom(w[i])) && world[i].y > hipContactY + 0.12f))
            {
                var p = world[i];
                // From 10 cm in front of the vertex (inside the torso) back to it: a backrest surface crossed is clipping.
                if (Physics.Raycast(p + f * 0.1f, -f, out var hit, 0.1f, 1 << SeatScratch, QueryTriggerInteraction.Collide))
                {
                    float depth = 0.1f - hit.distance; // how far the vertex is past the surface
                    if (depth > clip && Vector3.Dot(hit.normal, f) > 0.3f) clip = depth;
                }
            }
            o["back_clip"] = Math.Round(clip, 3);
        }

        // ------------------------------------------------------------------ round 2 clips (CMU mocap one-shots)

        /// <summary>
        /// -ctlife-captureSet r2clips: the new one-shots through the production triggers on the local player, with a
        /// detail-camera strip and a hips track (largest frame-to-frame jump = pops/slides): a sprint into a knee-high
        /// obstacle (ragdoll, face-down get-up), a run into a kerb-high one (trip), a run released to a stop.
        /// </summary>
        private static async Task CaptureClips()
        {
            var dir = Path.Combine(_out, "clips");
            Directory.CreateDirectory(dir);
            var results = new JObject();
            Receipt["clips"] = results;
            foreach (var look in new[] { "player_m_kol", "player_f_builder" })
            {
                // Where the ragdoll lands is physics: sprint until one lands face down (the new clip), at most 5 tries.
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    var r = await ClipCase(results, dir, look, "getup", 0.7f, 1f, 9f, attempt);
                    if (r == null || ((string)r["get_up"] ?? "").Contains("face-down")) break;
                }
                await ClipCase(results, dir, look, "trip", 0.4f, 1f, 3f);
                await ClipCase(results, dir, look, "runstop", 0f, 1f, 3f);
            }
        }

        /// <summary>A 3/4 close view of the body, from the side of the run (the obstacle stays out of the way).</summary>
        private static void Aim(Camera cam, Vector3 body)
        {
            var focus = new Vector3(body.x, Mathf.Max(body.y, 0.5f), body.z);
            cam.transform.SetPositionAndRotation(focus + new Vector3(2.4f, 0.9f, 0.6f), Quaternion.LookRotation(-new Vector3(2.4f, 0.9f, 0.6f)));
        }

        private static async Task<JObject> ClipCase(JObject results, string dir, string look, string kind, float obstacleHeight, float input, float watch, int attempt = 0)
        {
            var onlyClip = Arg("-ctlife-clipOnly");
            if (!string.IsNullOrEmpty(onlyClip) && !(look + "/" + kind).Contains(onlyClip)) return null;
            var o = new JObject();
            string caseKey = look + "/" + kind + (attempt > 0 ? "#" + attempt : "");
            results[caseKey] = o;
            var st = await Open("timeline_square");
            try
            {
                var at = st.venue.ToWorld(0f, -4.6f);
                if (obstacleHeight > 0f)
                {
                    var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    box.name = "test_obstacle";
                    st.spawned.Add(box);
                    box.transform.SetPositionAndRotation(at + Vector3.up * obstacleHeight * 0.5f, Quaternion.identity);
                    box.transform.localScale = new Vector3(2.4f, obstacleHeight, 0.4f);
                }
                var (view, local, _) = Player(st, at + Vector3.back * 6.5f, 0f, look);
                AnimatorUtility.DeoptimizeTransformHierarchy(view.Animator.gameObject); // hips track (measurement only)
                var kd = Knockdown.For(view);
                var hips = view.Animator.GetBoneTransform(HumanBodyBones.Hips);
                var cam = DetailCam(st, at + new Vector3(3.2f, 1.4f, -1.2f), at + new Vector3(0f, 0.6f, -0.6f));
                float peak = 0f, impactSpeed = 0f;
                // Drive: into the obstacle (getup/trip) or 1.6 s of running then let go (runstop).
                float driveEnd = Time.realtimeSinceStartup + (kind == "runstop" ? 1.6f : 4f);
                while (Time.realtimeSinceStartup < driveEnd && !kd.Busy)
                {
                    local.SetMoveInput(new Vector2(0f, input));
                    impactSpeed = local.ActualVelocity.magnitude;
                    peak = Mathf.Max(peak, impactSpeed);
                    await Frame();
                }
                if (kind == "runstop") local.StopDirectInput();
                o["peak_speed"] = Math.Round(peak, 2);
                var frames = new List<Texture2D>();
                var states = new JArray();
                var track = new List<Vector3>();
                var roots = new List<string>();
                var falling = new List<bool>(); // the ragdoll in flight moves fast between samples: not a pop
                float t0 = Time.realtimeSinceStartup, nextShot = 0f;
                string lastState = null;
                bool sawState = false;
                string want = kind == "trip" ? "react_trip" : kind == "runstop" ? "loco_run_stop" : null;
                while (Time.realtimeSinceStartup - t0 < watch && (kind == "runstop" || kd.Busy || Time.realtimeSinceStartup - t0 < 0.3f))
                {
                    if (kind == "getup") want = (kd.LastGetUp ?? "").Contains("act_get_up_front") ? "act_get_up_front" : "act_get_up";
                    var info = view.Animator.GetCurrentAnimatorStateInfo(0);
                    string state = info.IsName(want) ? want : StateOf(view);
                    if (info.IsName(want) || (view.Animator.IsInTransition(0) && view.Animator.GetNextAnimatorStateInfo(0).IsName(want))) sawState = true;
                    if (state != lastState) { states.Add($"{Time.realtimeSinceStartup - t0:F2}s {state} {kd.Current}"); lastState = state; }
                    track.Add(hips.position);
                    falling.Add(kd.Current == Knockdown.Phase.Falling || kd.Current == Knockdown.Phase.Rolling);
                    var ikc = view.Animator.GetComponent<CharacterIK>();
                    roots.Add($"{track.Count - 1}: root {view.transform.position:F2} hips {hips.position:F2} ikBody {(ikc != null ? ikc.BodyPosition : Vector3.zero):F2}@{(ikc != null ? Time.frameCount - ikc.BodyFrame : -1)} {state} {kd.Current}");
                    if (Time.realtimeSinceStartup - t0 >= nextShot && frames.Count < 8)
                    {
                        Aim(cam, hips.position);
                        frames.Add(Render(cam)); nextShot += kind == "getup" ? 0.9f : 0.35f;
                    }
                    await Frame();
                }
                for (int i = 0; i < 20; i++) { track.Add(hips.position); roots.Add($"{track.Count - 1}: root {view.transform.position:F2} hips {hips.position:F2} {StateOf(view)}"); await Frame(); } // the hand-back to locomotion
                Aim(cam, hips.position);
                frames.Add(Render(cam));
                float jump = 0f; int jumpAt = 0;
                // Sampled between Update and LateUpdate (Task continuations): a one-sample spike that the next sample
                // undoes is the mid-frame state (e.g. the get-up's first Animator pose before Knockdown's blend), not a pop.
                for (int i = 1; i + 1 < track.Count; i++)
                    if ((track[i] - track[i - 1]).magnitude > 0.1f && (track[i + 1] - track[i - 1]).magnitude < 0.05f) track[i] = track[i - 1];
                for (int i = 1; i < track.Count; i++)
                {
                    if (i < falling.Count && falling[i]) continue;
                    float d = (track[i] - track[i - 1]).magnitude;
                    if (d > jump) { jump = d; jumpAt = i; }
                }
                o["max_hips_step_frame"] = $"{jumpAt}/{track.Count}";
                o["around_jump"] = new JArray(roots.Skip(Math.Max(0, jumpAt - 4)).Take(8));
                int handBack = roots.FindIndex(r => r.EndsWith(" None") || !r.Contains("GettingUp") && roots.IndexOf(r) > roots.FindIndex(x => x.Contains("GettingUp")) && roots.FindIndex(x => x.Contains("GettingUp")) >= 0);
                if (kind == "getup" && handBack > 0) o["around_end"] = new JArray(roots.Skip(Math.Max(0, handBack - 3)).Take(6));
                o["states"] = states;
                o["played"] = sawState;
                o["get_up"] = kd.LastGetUp;
                o["max_hips_step_per_frame"] = Math.Round(jump, 3);
                o["state_after"] = StateOf(view);
                var strip = Stitch(frames);
                File.WriteAllBytes(Path.Combine(dir, $"{look}_{kind}{(attempt > 0 ? "_" + attempt : "")}.png"), strip.EncodeToPNG());
                foreach (var f in frames) Object.Destroy(f);
                Object.Destroy(strip);
                if (!sawState) Failures.Add($"clips/{caseKey}: {want} did not play ({string.Join(" | ", states)})");
                // A pop: the hips jumping more than a running stride's frame step (5 m/s at ~60 fps ≈ 0.09 m); the
                // first frames are the impact itself.
                if (jump > 0.12f && jumpAt > 3) Failures.Add($"clips/{caseKey}: hips jumped {jump:F2} m in one frame");
            }
            catch (Exception e) { Failures.Add($"clips/{caseKey}: {e.Message}"); Debug.LogException(e); }
            finally { await Close(st); }
            return o;
        }

        // ------------------------------------------------------------------ crowd budget (round 2)

        /// <summary>
        /// -ctlife-captureSet r2perf: 35 actors in Spaces Studio (the seat-heaviest venue): A) 32 seated on every seat
        /// (fitted seat-height blends, seat offsets, seated foot IK on LOD0) + 3 standing; B) the same 35 standing idle.
        /// Main-thread animation/IK markers (ProfilerRecorder, editor batchmode: relative numbers, not a device budget).
        /// </summary>
        private static async Task CapturePerf()
        {
            var o = new JObject();
            Receipt["perf"] = o;
            var st = await Open("spaces_studio");
            try
            {
                string[] roles = { "rekt_guy", "giveaway_guy", "spaces_host", "perp_degen_npc", "doomposter", "alpha_caller" };
                var seats = st.venue.Anchors.Where(a => SeatFitCatalog.Instance.TryGet("spaces_studio", a.spotId, out _)).OrderBy(a => a.spotId).ToList();
                var actors = new List<(AvatarView v, SpotAnchor seat)>();
                for (int i = 0; i < 35; i++)
                {
                    var seat = i < seats.Count ? seats[i] : null;
                    var at = seat != null ? seat.transform.position : st.venue.transform.position + new Vector3(i - 33, 0f, 2f);
                    var v = Npc(st, roles[i % roles.Length], at, seat != null ? seat.transform.eulerAngles.y : 180f);
                    actors.Add((v, seat));
                }
                var focus = seats[0].transform.position;
                var player = Npc(st, "rekt_guy", focus + Vector3.back * 1.5f, 0f);
                st.rig.Target = player.transform; st.rig.SnapToTarget();
                var markers = Markers();
                o["markers"] = new JArray(markers);
                async Task<JObject> Sample(string label)
                {
                    await Seconds(1.5f);
                    var recs = markers.Select(m => Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Animation, m, 240)).ToList();
                    var all = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Internal, "Main Thread", 240);
                    var frameMs = new List<double>();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int f = 0; f < 120; f++) { await Frame(); frameMs.Add(sw.Elapsed.TotalMilliseconds); sw.Restart(); }
                    var r = new JObject();
                    for (int k = 0; k < markers.Count; k++)
                    {
                        var rec = recs[k];
                        double sum = 0; int n = rec.Count;
                        for (int j = 0; j < n; j++) sum += rec.GetSample(j).Value;
                        if (n > 0) r[markers[k]] = Math.Round(sum / n / 1e6, 3); // ns → ms per frame
                        rec.Dispose();
                    }
                    double mt = 0; for (int j = 0; j < all.Count; j++) mt += all.GetSample(j).Value;
                    r["main_thread_ms"] = all.Count > 0 ? Math.Round(mt / all.Count / 1e6, 3) : -1;
                    all.Dispose();
                    r["lod0"] = actors.Count(a => a.v.LodTier == 0 && !a.v.Culled);
                    r["culled"] = actors.Count(a => a.v.Culled);
                    r["seated_fits"] = actors.Count(a => a.v.SeatFit.valid);
                    Step("perf " + label + " " + r);
                    return r;
                }
                foreach (var (v, seat) in actors)
                    if (seat != null) v.SetBase(seat.anim, seat.spotId);
                o["A_seated_32_standing_3"] = await Sample("seated");
                foreach (var (v, seat) in actors)
                {
                    if (seat == null) continue;
                    v.SetBase("loco_idle");
                    var approach = st.venue.TryGetSpotApproach(seat.spotId, out var ap) ? ap : seat.transform.position;
                    v.transform.position = approach;
                }
                o["B_standing_35"] = await Sample("standing");
            }
            catch (Exception e) { Failures.Add("perf: " + e.Message); Debug.LogException(e); }
            finally { await Close(st); }
        }

        /// <summary>Animation markers this editor exposes (names vary by version: discovered, not assumed).</summary>
        private static List<string> Markers()
        {
            var handles = new List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
            Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
            return handles.Select(h => Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h))
                .Where(d => d.Category == Unity.Profiling.ProfilerCategory.Animation)
                .Select(d => d.Name).Distinct().OrderBy(n => n).Take(40).ToList();
        }

        // ------------------------------------------------------------------ run band soles (round 2)

        /// <summary>
        /// -ctlife-captureSet r2run: Codex's run correction was a Snow-only delta table for the UAL sprint; the run band
        /// now plays the UAL Jog on every look. This measures the soles on flat ground while running (local player, 2 s),
        /// per look, with CharacterIK on and off: lowest sole vertex vs the floor each frame (min = sinking, and the
        /// planted-foot height per stride).
        /// </summary>
        private static async Task CaptureRun()
        {
            var o = new JObject();
            Receipt["run"] = o;
            foreach (var look in new[] { "player_m_kol", "player_f_builder", "player_f_kol" })
                foreach (bool ik in new[] { true, false })
                {
                    var st = await Open("timeline_square");
                    try
                    {
                        CharacterIK.Enabled = ik;
                        var at = st.venue.ToWorld(0f, -4.6f);
                        var (view, local, _) = Player(st, at + Vector3.back * 6.5f, 0f, look);
                        var smr = view.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r => r.sharedMesh.vertexCount).First();
                        var mesh = new Mesh();
                        var lows = new List<float>();
                        float peak = 0f;
                        float t0 = Time.realtimeSinceStartup;
                        while (Time.realtimeSinceStartup - t0 < 1.9f)
                        {
                            local.SetMoveInput(new Vector2(0f, 1f));
                            await Frame();
                            peak = Mathf.Max(peak, local.ActualVelocity.magnitude);
                            if (Time.realtimeSinceStartup - t0 < 0.5f || local.ActualVelocity.magnitude < 3.6f) continue; // in the run band
                            smr.BakeMesh(mesh, true);
                            var m = smr.transform.localToWorldMatrix;
                            float low = float.MaxValue;
                            foreach (var v in mesh.vertices) low = Mathf.Min(low, m.MultiplyPoint3x4(v).y);
                            float floor = Physics.Raycast(view.transform.position + Vector3.up, Vector3.down, out var hit, 2f, CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore) ? hit.point.y : view.transform.position.y;
                            lows.Add(low - floor);
                        }
                        Object.Destroy(mesh);
                        local.StopDirectInput();
                        o[$"{look}/ik_{(ik ? "on" : "off")}"] = lows.Count == 0 ? $"no run-band frames (peak {peak:F2} m/s)" : new JObject
                        {
                            ["frames"] = lows.Count,
                            ["lowest_sole_min"] = Math.Round(lows.Min(), 3),
                            ["lowest_sole_p10"] = Math.Round(lows.OrderBy(x => x).ElementAt(lows.Count / 10), 3),
                            ["lowest_sole_median"] = Math.Round(lows.OrderBy(x => x).ElementAt(lows.Count / 2), 3),
                        };
                    }
                    catch (Exception e) { Failures.Add($"run/{look}: {e.Message}"); Debug.LogException(e); }
                    finally { CharacterIK.Enabled = true; await Close(st); }
                }
        }
    }
}
