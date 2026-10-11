using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Realtime;
using CTLife.UI;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Priority 2 (feel) captures: every behaviour through the production components, from the game camera with the real
    /// HUD composited (UI Toolkit rendered into a texture). -ctlife-captureSet p2 → ct-life-evidence/character-feel/p2/.
    /// </summary>
    public static partial class CharacterFeelCapture
    {
        private static RenderTexture _uiTarget;
        private static Hud _hud;
        private static JObject _p2;
        private static string _p2Dir;
        private static readonly List<InteractionPromptSignal> Prompts = new List<InteractionPromptSignal>();

        private static async Task CaptureFeel()
        {
            _p2 = new JObject();
            Receipt["p2"] = _p2;
            _p2Dir = Path.Combine(_out, "p2");
            Directory.CreateDirectory(_p2Dir);
            Bus.Subscribe<InteractionPromptSignal>(s => { Prompts.Add(s); });
            EnsureHud();
            await Scenario("interaction_sit_cafe", InteractionSitCafe);
            await Scenario("remote_and_npc_seats", RemoteAndNpcSeats);
            await Scenario("collision_props", CollisionProps);
            await Scenario("stumble_fall_getup", StumbleFall);
            await Scenario("ledge_fall_landing", LedgeFall);
            await Scenario("ik_grounding_attention", IkGrounding);
            await Scenario("gait_idle_turn", GaitIdleTurn);
            await Scenario("probe_ik_goals", ProbeIkGoals);
        }

        private static async Task Scenario(string name, Func<JObject, Task> body)
        {
            var only = Arg("-ctlife-scenario");
            if (!string.IsNullOrEmpty(only) && !name.Contains(only)) return;
            var o = new JObject();
            _p2[name] = o;
            Step("p2 " + name);
            try { await body(o); o["ok"] = true; }
            catch (Exception e) { o["ok"] = false; o["error"] = e.GetType().Name + ": " + e.Message; Failures.Add($"p2/{name}: {e.Message}"); Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ scaffolding

        private sealed class Stage : IDisposable
        {
            public VenueLoader loader; public VenueSceneRoot venue; public Transform root; public CameraRig rig;
            public readonly List<GameObject> spawned = new List<GameObject>();
            public void Dispose() { foreach (var g in spawned) if (g != null) Object.Destroy(g); if (root != null) Object.Destroy(root.gameObject); }
        }

        private static async Task<Stage> Open(string venueId)
        {
            var st = new Stage { loader = new VenueLoader() };
            st.venue = await st.loader.Load(venueId);
            AvatarLod.Ensure();
            st.root = new GameObject("FeelAvatars").transform;
            st.rig = CameraRig.Ensure();
            StartPump(st.rig.Camera);
            Bus.Publish(new VenueChangedSignal { venueId = venueId });
            return st;
        }

        private static async Task Close(Stage st)
        {
            st.Dispose();
            await st.loader.Unload();
            Prompts.Clear();
        }

        private static (AvatarView view, LocalPlayerController local, InteractionController interact) Player(Stage st, Vector3 at, float yaw, string look = "player_m_kol")
        {
            var view = AvatarFactory.Create("you", LookFor(CharacterRegistry.Instance, look), false, null, st.root);
            view.transform.SetPositionAndRotation(Sample(at), Quaternion.Euler(0, yaw, 0));
            var local = view.gameObject.AddComponent<LocalPlayerController>();
            local.Init(st.venue, null);
            var interact = view.gameObject.AddComponent<InteractionController>();
            interact.Init(st.venue, null, null);
            st.spawned.Add(view.gameObject);
            st.rig.Target = view.transform;
            st.rig.SnapToTarget();
            return (view, local, interact);
        }

        private static AvatarView Npc(Stage st, string role, Vector3 at, float yaw)
        {
            var v = AvatarFactory.CreateNpc("npc:" + role, "feel-" + role, role, st.root);
            v.transform.SetPositionAndRotation(Sample(at), Quaternion.Euler(0, yaw, 0));
            v.SetBase("loco_idle");
            st.spawned.Add(v.gameObject);
            return v;
        }

        private static void EnsureHud()
        {
            if (_hud != null) return;
            _uiTarget = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "feel-ui" };
            var go = new GameObject("CTLife.UI.Capture");
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            var ps = Object.Instantiate(Resources.Load<PanelSettings>("UI/PanelSettings"));
            ps.name = "capture-panel";
            ps.targetTexture = _uiTarget;
            ps.clearColor = true;
            ps.colorClearValue = new Color(0, 0, 0, 0);
            doc.panelSettings = ps;
            go.AddComponent<UiRoot>();
            go.SetActive(true);
            UiRoot.Instance.Root.EnableInClassList("ct-landscape", true);
            _hud = new Hud();
            UiRoot.Instance.HudLayer.Add(_hud.Element);
            _hud.Show(true);
        }

        /// <summary>Camera frame with the HUD composited (alpha over).</summary>
        private static Texture2D FrameWithUi(Camera cam)
        {
            var frame = Render(cam);
            if (_uiTarget == null) return frame;
            var active = RenderTexture.active;
            RenderTexture.active = _uiTarget;
            var ui = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            ui.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            ui.Apply();
            RenderTexture.active = active;
            var a = frame.GetPixels(); var b = ui.GetPixels();
            for (int i = 0; i < a.Length; i++) { float al = b[i].a; if (al > 0.001f) a[i] = Color.Lerp(a[i], new Color(b[i].r, b[i].g, b[i].b, 1f), al); }
            frame.SetPixels(a); frame.Apply();
            Object.Destroy(ui);
            return frame;
        }

        private static readonly Dictionary<string, List<Texture2D>> Strips = new Dictionary<string, List<Texture2D>>();

        private static async Task Snap(string strip, Camera cam, bool ui = true)
        {
            await Frame();
            var tex = ui ? FrameWithUi(cam) : Render(cam);
            if (!Strips.TryGetValue(strip, out var list)) Strips[strip] = list = new List<Texture2D>();
            list.Add(tex);
        }

        private static string Flush(string strip, JObject o)
        {
            if (!Strips.TryGetValue(strip, out var list) || list.Count == 0) return null;
            var file = "p2-" + strip + (list.Count > 1 ? "-strip" : "") + ".png";
            Texture2D outTex;
            if (list.Count == 1) outTex = list[0];
            else
            {
                int cols = Mathf.Min(3, list.Count), rows = Mathf.CeilToInt(list.Count / (float)cols);
                int w = Width / 2, h = Height / 2;
                outTex = new Texture2D(w * cols, h * rows, TextureFormat.RGB24, false);
                var fill = new Color[w * cols * h * rows];
                for (int i = 0; i < fill.Length; i++) fill[i] = Color.black;
                outTex.SetPixels(fill);
                for (int i = 0; i < list.Count; i++)
                {
                    int cx = i % cols, cy = rows - 1 - i / cols;
                    var src = list[i];
                    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) outTex.SetPixel(cx * w + x, cy * h + y, src.GetPixel(x * 2, y * 2));
                }
                outTex.Apply();
            }
            File.WriteAllBytes(Path.Combine(_p2Dir, file), outTex.EncodeToPNG());
            foreach (var t in list) Object.Destroy(t);
            if (list.Count > 1) Object.Destroy(outTex);
            Strips.Remove(strip);
            var files = (JArray)(o["files"] ?? (o["files"] = new JArray()));
            files.Add(file);
            return file;
        }

        private static async Task<bool> Until(Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) { if (condition()) return true; await Frame(); }
            return condition();
        }

        /// <summary>A close detail camera (IK evidence: feet on a step, head turn, hands) next to the game-camera shots.</summary>
        private static Camera DetailCam(Stage st, Vector3 position, Vector3 lookAt)
        {
            var go = new GameObject("detail_camera");
            st.spawned.Add(go);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 35f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.78f, 0.92f);
            cam.targetTexture = _target; cam.aspect = (float)Width / Height;
            cam.enabled = false; // rendered on demand; the game camera keeps driving visibility/culling
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
            return cam;
        }

        private static string Cta() =>
            Prompts.Count == 0 ? "-" : string.Join("+", Prompts[Prompts.Count - 1].actions.Select(a => a.id + ":" + a.labelKey));

        private static void Zoom(CameraRig rig, float distance) { rig.distance = distance; rig.SnapToTarget(); }

        // ------------------------------------------------------------------ 1) CTA → approach → sit → stand (local)

        private static async Task InteractionSitCafe(JObject o)
        {
            var st = await Open("terminal_cafe");
            try
            {
                var seat = InteractableCatalog.Build(st.venue).First(i => i.id == "table_2");
                o["catalog"] = new JArray(InteractableCatalog.Build(st.venue).Select(i => $"{i.kind}:{i.id}{(i.Networked ? "@wire" : "@local")}"));
                var (view, local, interact) = Player(st, seat.approach + Vector3.right * 1.6f + Vector3.back * 0.4f, -60f);
                Zoom(st.rig, st.rig.minDistance);
                await Seconds(2.5f);
                o["offered"] = interact.Offered?.id;
                o["cta_offer"] = Cta();
                await Snap("cta", st.rig.Camera);
                if (interact.Offered == null) throw new InvalidOperationException("no CTA near table_2");
                interact.Perform("use");
                o["after_press"] = interact.Machine.Phase.ToString();
                await Seconds(0.35f); await Snap("approach_sit", st.rig.Camera);
                await Until(() => interact.Machine.Phase == InteractionPhase.Engaged, 6f);
                o["engaged"] = interact.Machine.Phase.ToString();
                o["local_only"] = interact.Machine.LocalOnly;
                await Seconds(0.25f); await Snap("approach_sit", st.rig.Camera);
                await Seconds(0.55f); await Snap("approach_sit", st.rig.Camera);
                await Until(() => !view.SeatTransitionActive, 3f);
                await Seconds(0.6f); await Snap("approach_sit", st.rig.Camera);
                o["cta_seated"] = Cta();
                o["seated_state"] = StateOf(view);
                Flush("cta", o); Flush("approach_sit", o);
                interact.Perform("stand");
                await Seconds(0.3f); await Snap("stand_up", st.rig.Camera);
                await Seconds(0.5f); await Snap("stand_up", st.rig.Camera);
                await Until(() => !view.SeatPresentationActive, 3f);
                await Seconds(0.3f); await Snap("stand_up", st.rig.Camera);
                o["after_stand"] = interact.Machine.Phase.ToString();
                o["cta_after_stand"] = Cta();
                Flush("stand_up", o);
                // A seat tapped from across the room is offered (not sat in) until the button is pressed.
                var far = st.venue.Anchors.First(a => a.spotId == "table_4");
                interact.Focus(far);
                await Seconds(0.3f);
                o["focus_offered"] = interact.Offered?.id;
                o["focus_phase"] = interact.Machine.Phase.ToString();
            }
            finally { await Close(st); }
        }

        // ------------------------------------------------------------------ 2) others see it: a remote player and an NPC

        private static async Task RemoteAndNpcSeats(JObject o)
        {
            var st = await Open("terminal_cafe");
            try
            {
                var cat = InteractableCatalog.Build(st.venue);
                var t3 = cat.First(i => i.id == "table_3");
                var t4 = cat.First(i => i.id == "table_4");
                var (view, local, interact) = Player(st, t3.approach + Vector3.left * 2.2f, 90f, "player_f_kol");
                Zoom(st.rig, st.rig.minDistance + 1f);
                // Remote player: the server's PlayerState (spotId/anim) drives the same sit-down presentation.
                var rv = AvatarFactory.Create("remote", LookFor(CharacterRegistry.Instance, "player_m_trencher"), false, null, st.root);
                st.spawned.Add(rv.gameObject);
                var local3 = st.venue.ToLocal(t3.approach);
                var ps = new PlayerState { name = "remote", x = local3.x, z = local3.y, rotY = 0, anim = "loco_idle" };
                var remote = rv.gameObject.AddComponent<RemotePlayer>();
                remote.Init(st.venue, ps);
                await Seconds(1.2f);
                await Snap("remote_sit", st.rig.Camera);
                var seated = new PlayerState { name = "remote", x = local3.x, z = local3.y, rotY = 0, anim = "act_sit", spotId = st.venue.WireSpotId("table_3") };
                remote.Apply(seated);
                await Seconds(0.45f); await Snap("remote_sit", st.rig.Camera);
                await Seconds(0.6f); await Snap("remote_sit", st.rig.Camera);
                await Until(() => !rv.SeatTransitionActive, 3f);
                await Seconds(0.5f); await Snap("remote_sit", st.rig.Camera);
                o["remote_state_seated"] = StateOf(rv);
                Flush("remote_sit", o);
                // NPC: the server sends act at table_4; it walks to the approach and sits down.
                var npcView = AvatarFactory.CreateNpc("npc:sleuth", "feel-sleuth", "sleuth", st.root);
                st.spawned.Add(npcView.gameObject);
                npcView.transform.position = Sample(t4.approach + Vector3.right * 3f);
                var npc = npcView.gameObject.AddComponent<NpcController>();
                npc.Init("feel-sleuth", new NpcState { archetypeId = "sleuth", state = "act", spotId = st.venue.WireSpotId("table_4"), animId = "act_sit" }, st.venue, _ => null);
                interact.Occupancy.Set("npc:feel-sleuth", "table_4"); // VenueSession mirrors NPC states into the CTA availability
                await Seconds(0.8f); await Snap("npc_sit", st.rig.Camera);
                await Until(() => npcView.SeatTransitionActive, 8f);
                await Seconds(0.5f); await Snap("npc_sit", st.rig.Camera);
                await Until(() => !npcView.SeatTransitionActive, 4f);
                await Seconds(0.6f); await Snap("npc_sit", st.rig.Camera);
                o["npc_state_seated"] = StateOf(npcView);
                // The player standing next to the NPC's table is not offered it.
                view.GetComponent<LocalPlayerController>().SnapTo(t4.approach + Vector3.left * 0.6f, 0f * Mathf.Deg2Rad);
                await Seconds(0.5f);
                o["offered_next_to_npc_seat"] = interact.Offered?.id ?? "none";
                Flush("npc_sit", o);
            }
            finally { await Close(st); }
        }

        // ------------------------------------------------------------------ 3) collision with props (no pass-through)

        private static async Task CollisionProps(JObject o)
        {
            var st = await Open("terminal_cafe");
            try
            {
                // Walk straight at a café table: the body stops against it (the NavMesh alone would let it through).
                var table = st.venue.Anchors.First(a => a.spotId == "table_1");
                var tableCentre = table.transform.position + table.transform.forward * 1.0f;
                var start = tableCentre + Vector3.back * 2.6f;
                var (view, local, _) = Player(st, start, 0f);
                Zoom(st.rig, st.rig.minDistance);
                var cols = Physics.OverlapSphere(tableCentre + Vector3.up * 0.4f, 1.6f, CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore)
                    .Where(c => c.bounds.size.y < 2.5f && c.bounds.size.y > 0.3f).ToArray();
                int overlapFrames = 0, frames = 0;
                var inner = new Collider[8];
                float end = Time.realtimeSinceStartup + 2.4f;
                int frame = 0;
                while (Time.realtimeSinceStartup < end)
                {
                    var dir = (tableCentre - view.transform.position); dir.y = 0;
                    local.SetMoveInput(new Vector2(0f, 0.7f));
                    view.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 0.01f ? dir : Vector3.forward);
                    // Penetration = a prop inside the body's capsule shrunk by 6 cm (contact itself is fine).
                    var b = view.transform.position;
                    float r = CharacterBody.Radius - 0.06f;
                    int hits = Physics.OverlapCapsuleNonAlloc(b + Vector3.up * (CharacterBody.StepOffset + r), b + Vector3.up * (CharacterBody.Height - r), r, inner,
                        CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < hits; h++) if (cols.Contains(inner[h])) { overlapFrames++; break; }
                    frames++;
                    if (frame++ % 40 == 20) await Snap("walk_into_table", st.rig.Camera, ui: false);
                    await Frame();
                }
                local.StopDirectInput();
                await Snap("walk_into_table", st.rig.Camera, ui: false);
                o["table_colliders"] = cols.Length;
                o["frames"] = frames;
                o["frames_with_prop_inside_body"] = overlapFrames;
                o["stopped_at_m_from_table_centre"] = Math.Round(Vector3.Distance(Flatten(view.transform.position), Flatten(tableCentre)), 2);
                o["body_direct"] = local.Body != null;
                if (overlapFrames > 0) Failures.Add($"p2/collision: a prop was inside the body in {overlapFrames} frames");
                Flush("walk_into_table", o);
            }
            finally { await Close(st); }
        }

        // ------------------------------------------------------------------ 4) sprint into a bench: ragdoll fall, get-up

        private static async Task StumbleFall(JObject o)
        {
            var st = await Open("timeline_square");
            try
            {
                // A solid obstacle for the test (the square's benches have no colliders yet: VenueFeelAudit list).
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "test_planter";
                st.spawned.Add(wall);
                var at = st.venue.ToWorld(0f, -4.6f);
                wall.transform.SetPositionAndRotation(at + Vector3.up * 0.35f, Quaternion.identity);
                wall.transform.localScale = new Vector3(2.4f, 0.7f, 0.5f); // knee-high: the runner trips over it
                var (view, local, _) = Player(st, at + Vector3.back * 6.5f, 0f);
                Zoom(st.rig, st.rig.minDistance + 2f);
                var phases = new JArray();
                var kd = Knockdown.For(view);
                bool fell = false, ragdoll = false;
                Knockdown.Fell += (v, r) => { if (v == view) { fell = true; ragdoll = r; } };
                float end = Time.realtimeSinceStartup + 3f;
                int shots = 0; float peak = 0f;
                while (Time.realtimeSinceStartup < end && !kd.Busy)
                {
                    local.SetMoveInput(new Vector2(0f, 1f)); // full drag: run
                    peak = Mathf.Max(peak, local.ActualVelocity.magnitude);
                    await Frame();
                }
                o["peak_speed_before_impact"] = Math.Round(peak, 2);
                o["fell"] = fell; o["ragdoll"] = ragdoll; o["ragdolls_active"] = RagdollRig.Active;
                float t0 = Time.realtimeSinceStartup;
                string last = null;
                while (kd.Busy && Time.realtimeSinceStartup - t0 < 9f)
                {
                    var phase = kd.Current.ToString();
                    if (phase != last) { phases.Add($"{Time.realtimeSinceStartup - t0:F2}s {phase}"); last = phase; }
                    if (shots < 6 && (Time.realtimeSinceStartup - t0) > shots * 0.55f) { await Snap("sprint_fall_getup", st.rig.Camera, ui: false); shots++; }
                    await Frame();
                }
                await Seconds(0.4f); await Snap("sprint_fall_getup", st.rig.Camera, ui: false);
                o["phases"] = phases;
                o["get_up"] = kd.LastGetUp;
                o["control_back_after_s"] = Math.Round(Time.realtimeSinceStartup - t0, 2);
                o["stunned_after"] = local.Stunned;
                o["ragdolls_active_after"] = RagdollRig.Active;
                o["state_after"] = StateOf(view);
                Flush("sprint_fall_getup", o);
                if (!fell) Failures.Add("p2/stumble: sprinting into the obstacle did not knock the player down");

                // Scripted "rekt" on NPCs, three at once: two ragdolls (budget), the third falls animated.
                var npcs = new List<AvatarView>();
                for (int i = 0; i < 3; i++) npcs.Add(Npc(st, new[] { "rekt_guy", "perp_degen_npc", "doomposter" }[i], view.transform.position + new Vector3(-1.6f + 1.6f * i, 0, 2.2f), 180f));
                await Seconds(0.8f);
                var kinds = new JArray();
                Knockdown.Fell += (v, r) => { if (npcs.Contains(v)) kinds.Add(v.name + (r ? ":ragdoll" : ":animated")); };
                foreach (var n in npcs) Knockdown.For(n).Fall(Vector3.zero, -n.transform.forward * 140f + Vector3.up * 20f);
                o["rekt_max_ragdolls_active"] = RagdollRig.Active;
                await Seconds(0.5f); await Snap("rekt_budget", st.rig.Camera, ui: false);
                await Seconds(1.2f); await Snap("rekt_budget", st.rig.Camera, ui: false);
                await Until(() => npcs.All(n => !Knockdown.For(n).Busy), 9f);
                await Snap("rekt_budget", st.rig.Camera, ui: false);
                o["rekt"] = kinds;
                o["ragdolls_active_end"] = RagdollRig.Active;
                if (RagdollRig.Active != 0) Failures.Add("p2/rekt: ragdoll budget leaked");
                Flush("rekt_budget", o);
            }
            finally { await Close(st); }
        }

        // ------------------------------------------------------------------ 5) ledge: walk off, fall, land

        private static async Task LedgeFall(JObject o)
        {
            var st = await Open("timeline_square");
            NavMeshDataInstance navInst = default;
            try
            {
                var at = st.venue.ToWorld(-2f, 3.5f);
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "test_ledge";
                st.spawned.Add(box);
                const float height = 1.9f;
                box.transform.SetPositionAndRotation(at + Vector3.up * (height * 0.5f), Quaternion.identity);
                box.transform.localScale = new Vector3(3f, height, 3f);
                // Runtime NavMesh on the platform top (the venue floor already has one).
                var src = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(3f, height, 3f), transform = Matrix4x4.TRS(at + Vector3.up * (height * 0.5f), Quaternion.identity, Vector3.one) };
                var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource> { src },
                    new Bounds(at + Vector3.up * height, new Vector3(4f, 3f, 4f)), Vector3.zero, Quaternion.identity);
                navInst = NavMesh.AddNavMeshData(data);
                var (view, local, _) = Player(st, at + Vector3.up * height, 0f);
                local.SnapTo(at + Vector3.up * height, 0f);
                Zoom(st.rig, st.rig.minDistance + 1f);
                await Seconds(1f);
                await Snap("ledge", st.rig.Camera, ui: false);
                float landedHeight = -1f;
                local.Body.Landed += h => landedHeight = h;
                float end = Time.realtimeSinceStartup + 4f;
                bool airborne = false; int shots = 0;
                while (Time.realtimeSinceStartup < end && landedHeight < 0f)
                {
                    if (!local.Body.Airborne) local.SetMoveInput(new Vector2(0f, 0.6f));
                    airborne |= local.Body.Airborne;
                    if (local.Body.Airborne && shots < 2 && Time.time - local.Body.AirborneSince > shots * 0.2f) { await Snap("ledge", st.rig.Camera, ui: false); shots++; }
                    await Frame();
                }
                await Seconds(0.25f); await Snap("ledge", st.rig.Camera, ui: false);
                await Seconds(0.8f); await Snap("ledge", st.rig.Camera, ui: false);
                o["went_airborne"] = airborne;
                o["landed_height_m"] = Math.Round(landedHeight, 2);
                o["reaction"] = landedHeight > CharacterBody.HardLanding + 1f ? "knockdown" : landedHeight > CharacterBody.HardLanding ? "react_roll" : "jump_land";
                o["grounded_after"] = local.Body.Grounded;
                if (!airborne) Failures.Add("p2/ledge: the player did not step off the ledge");
                Flush("ledge", o);
            }
            finally { if (navInst.valid) NavMesh.RemoveNavMeshData(navInst); await Close(st); }
        }

        // ------------------------------------------------------------------ 6) IK: feet on a step, look-at, hands on table

        private static async Task IkGrounding(JObject o)
        {
            var st = await Open("terminal_cafe");
            try
            {
                var at = st.venue.ToWorld(0f, -1.5f);
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = "test_step";
                st.spawned.Add(step);
                step.transform.SetPositionAndRotation(at + new Vector3(-0.3f, 0.09f, 0f), Quaternion.identity);
                step.transform.localScale = new Vector3(0.5f, 0.18f, 1.2f);
                var (view, local, _) = Player(st, at, 0f);
                view.transform.rotation = Quaternion.Euler(0, 0, 0);
                Zoom(st.rig, st.rig.minDistance - 1.5f);
                var feet = new JObject();
                CharacterIK.FootProbe = (v, l, r) => { if (v == view) feet["last"] = $"L {l.y:F3} R {r.y:F3}"; };
                var side = DetailCam(st, at + view.transform.right * -1.8f + Vector3.up * 0.45f + view.transform.forward * 0.2f, at + Vector3.up * 0.3f);
                CharacterIK.Enabled = false; await Seconds(0.8f); await Snap("feet_step_ik_off_on", st.rig.Camera, ui: false); await Snap("feet_step_ik_off_on", side, ui: false);
                CharacterIK.Enabled = true; await Seconds(0.8f); await Snap("feet_step_ik_off_on", st.rig.Camera, ui: false); await Snap("feet_step_ik_off_on", side, ui: false);
                o["foot_probe"] = feet["last"];
                Flush("feet_step_ik_off_on", o);
                // Look-at: a talking NPC beside the player.
                var talker = Npc(st, "spaces_host", at + new Vector3(1.4f, 0, 0.9f), -120f);
                talker.SetTalking(true);
                var face = DetailCam(st, at + view.transform.forward * 1.9f + Vector3.up * 1.6f, at + Vector3.up * 1.5f);
                CharacterIK.Enabled = false; await Seconds(0.8f); await Snap("look_at_speaker_off_on", st.rig.Camera, ui: false); await Snap("look_at_speaker_off_on", face, ui: false);
                CharacterIK.Enabled = true; await Seconds(1.2f); await Snap("look_at_speaker_off_on", st.rig.Camera, ui: false); await Snap("look_at_speaker_off_on", face, ui: false);
                Flush("look_at_speaker_off_on", o);
                // Hands on the table when seated (local-only sit at table_1).
                var table = InteractableCatalog.Build(st.venue).First(i => i.id == "table_1");
                local.SnapTo(table.approach, 0f * Mathf.Deg2Rad);
                // The café's chairs sit 0.67 m from the table edge (out of reach: hands stay in the lap, as authored). For the
                // evidence, pull the seat in to a normal dining distance so the reach rule applies.
                var pulledIn = table.position + Quaternion.Euler(0, table.yaw, 0) * Vector3.forward * 0.3f;
                local.EngageLocal(table.id, pulledIn, table.yaw, "act_sit", "act_sit_down");
                await Until(() => !view.SeatTransitionActive, 4f);
                var seatRight = Quaternion.Euler(0, table.yaw, 0) * Vector3.right;
                var hands = DetailCam(st, table.position + seatRight * 1.6f + Vector3.up * 1.1f + Quaternion.Euler(0, table.yaw, 0) * Vector3.forward * 0.4f, table.position + Vector3.up * 0.8f + Quaternion.Euler(0, table.yaw, 0) * Vector3.forward * 0.3f);
                CharacterIK.Enabled = false; await Seconds(0.8f); await Snap("hands_on_table_off_on", st.rig.Camera, ui: false); await Snap("hands_on_table_off_on", hands, ui: false);
                CharacterIK.Enabled = true; await Seconds(1.2f); await Snap("hands_on_table_off_on", st.rig.Camera, ui: false); await Snap("hands_on_table_off_on", hands, ui: false);
                Flush("hands_on_table_off_on", o);
                CharacterIK.FootProbe = null;
            }
            finally { CharacterIK.Enabled = true; CharacterIK.FootProbe = null; await Close(st); }
        }

        // ------------------------------------------------------------------ 7) gait: speed-matched blend, idle variations, turn in place

        private static async Task GaitIdleTurn(JObject o)
        {
            var st = await Open("timeline_square");
            try
            {
                var at = st.venue.ToWorld(-6f, 4f);
                var (view, local, _) = Player(st, at, 90f);
                // Ground truth for this one test character: real toe bones (de-optimized; production stays optimized).
                AnimatorUtility.DeoptimizeTransformHierarchy(view.Animator.gameObject);
                view.RestoreBaseAnimation();
                var lt = view.Animator.GetBoneTransform(HumanBodyBones.LeftToes);
                var rt = view.Animator.GetBoneTransform(HumanBodyBones.RightToes);
                var lf = view.Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Zoom(st.rig, st.rig.minDistance);
                var stick = Quaternion.Inverse(Quaternion.Euler(0, st.rig.yaw, 0)) * Vector3.right;
                var east = new Vector2(stick.x, stick.z).normalized;
                var gait = new JObject();
                var tuning = LocomotionTuning.Instance;
                var savedWalk = tuning.families.Select(f => f.walk).ToArray();
                foreach (var mode in new[] { "codex_walk_ref_1.0", "raw", "ik" })
                foreach (var strength in new[] { 0.35f, 0.75f, 1.0f })
                {
                    if (mode.StartsWith("codex") && strength >= 1f) continue;
                    bool ik = mode == "ik";
                    CharacterIK.Enabled = ik;
                    // Baseline: the previous walk cadence (speed / 1.0 m/s) on the same clip, for a like-for-like slide.
                    for (int f = 0; f < tuning.families.Count; f++) tuning.families[f].walk = mode.StartsWith("codex") ? 1.0f : savedWalk[f];
                    view.RestoreBaseAnimation();
                    local.SnapTo(at, 90f * Mathf.Deg2Rad);
                    await Seconds(0.3f);
                    var toes = new List<(float t, Vector3 l, Vector3 r)>();
                    var goalErr = new List<float>();
                    Vector3 goal = Vector3.zero; bool haveGoal = false;
                    CharacterIK.FootProbe = (v, l, r) => { if (v == view) { goal = l; haveGoal = true; } };
                    float start = Time.realtimeSinceStartup, end = start + 2.6f, lastT = -1f, rate = 0f;
                    float lastNorm = 0f, normSpan = 0f; int lastHash = 0, restarts = 0, switches = 0, transitions = 0;
                    var p0 = view.transform.position; float t0 = 0f;
                    while (Time.realtimeSinceStartup < end)
                    {
                        local.SetMoveInput(east * strength);
                        float el = Time.realtimeSinceStartup - start;
                        if (el > 1.0f && Time.time > lastT)
                        {
                            if (t0 == 0f) { t0 = Time.time; p0 = view.transform.position; }
                            lastT = Time.time;
                            toes.Add((Time.time, lt.position, rt.position)); // every frame: stance windows need resolution
                            if (haveGoal && lf != null) goalErr.Add(Vector3.Distance(goal, lf.position));
                            rate = view.Animator.GetFloat("locoRate");
                            var info = view.Animator.GetCurrentAnimatorStateInfo(0);
                            if (info.normalizedTime < lastNorm - 0.02f && info.shortNameHash == lastHash) restarts++;
                            if (lastHash != 0 && info.shortNameHash != lastHash) switches++;
                            if (view.Animator.IsInTransition(0)) transitions++;
                            lastNorm = info.normalizedTime; lastHash = info.shortNameHash;
                            normSpan = info.normalizedTime;
                        }
                        await Frame();
                    }
                    float avgSpeed = t0 > 0f ? FlatLen(view.transform.position - p0) / Mathf.Max(0.01f, Time.time - t0) : 0f;
                    if (ik && strength >= 1f) await Snap("gait_speeds", st.rig.Camera, ui: false);
                    if (ik && strength < 0.5f) await Snap("gait_speeds", st.rig.Camera, ui: false);
                    local.StopDirectInput();
                    CharacterIK.FootProbe = null;
                    gait[$"{mode}_{strength:F2}"] = $"avg speed {avgSpeed:F2} m/s, locoRate {rate:F2}, planted-toe slide {StanceSlide(toes, strength >= 1f):F3} m/s, " +
                        $"IK goal vs foot bone {(goalErr.Count > 0 ? goalErr.Average() : -1f):F3} m; normalizedTime end {normSpan:F2}, restarts {restarts}, state switches {switches}, in-transition samples {transitions}, state {StateOf(view)}";
                    await Seconds(0.4f);
                }
                for (int f = 0; f < tuning.families.Count; f++) tuning.families[f].walk = savedWalk[f];
                CharacterIK.Enabled = true;
                o["gait"] = gait;
                Flush("gait_speeds", o);
                // Idle variation (what TickIdleLife plays every 9–18 s while idle).
                view.PlayOneShot("idle_fidget_lookaround");
                await Seconds(1.0f); await Snap("idle_variation", st.rig.Camera, ui: false);
                await Seconds(1.0f); await Snap("idle_variation", st.rig.Camera, ui: false);
                o["idle_variation_state"] = StateOf(view);
                Flush("idle_variation", o);
                // Turn in place: reverse the stick at a light touch; the body turns first (720°/s) and the feet step.
                await Seconds(2.5f);
                bool shuffled = false;
                var back = -(Quaternion.Inverse(Quaternion.Euler(0, st.rig.yaw, 0)) * view.transform.forward);
                var input = new Vector2(back.x, back.z).normalized * 0.3f;
                for (int i = 0; i < 16; i++)
                {
                    local.SetMoveInput(input);
                    shuffled |= StateOf(view) == "loco_walk" && local.ActualVelocity.magnitude < 0.3f;
                    if (i % 5 == 2) await Snap("turn_in_place", st.rig.Camera, ui: false);
                    await Frame();
                }
                local.StopDirectInput();
                o["turn_shuffle_used_walk"] = shuffled;
                Flush("turn_in_place", o);
            }
            finally { CharacterIK.Enabled = true; CharacterIK.FootProbe = null; await Close(st); }
        }

        /// <summary>
        /// Validates the foot signal: root held still, the Locomotion tree driven by the speed parameter in loco_walk and
        /// loco_run; compares the IK-goal foot track (OnAnimatorIK GetIKPosition) with ground truth from the skinned mesh
        /// (root-space Z range of the lowest baked vertices = the stride the feet really sweep).
        /// </summary>
        private static async Task ProbeIkGoals(JObject o)
        {
            var st = await Open("timeline_square");
            try
            {
                var at = st.venue.ToWorld(-10f, 3.5f);
                var (view, local, _) = Player(st, at, 90f);
                local.enabled = false; // root still; we drive the animator directly
                Zoom(st.rig, st.rig.minDistance);
                foreach (var (state, speed) in new[] { ("loco_idle", 0f), ("loco_walk", 1.8f), ("loco_run", 4.4f) })
                {
                    view.SetBase(state);
                    var goalZ = new List<float>(); var meshZ = new List<float>();
                    float last = -1f;
                    CharacterIK.FootProbe = (v, l, r) =>
                    {
                        if (v != view || Time.time - last < 1f / 30f) return;
                        last = Time.time;
                        goalZ.Add(Vector3.Dot(l - v.transform.position, v.transform.forward));
                    };
                    float end = Time.realtimeSinceStartup + 2.2f;
                    var baked = new Mesh();
                    float lastBake = -1f;
                    while (Time.realtimeSinceStartup < end)
                    {
                        view.SetSpeed(speed);
                        if (Time.time - lastBake > 1f / 15f && view.Body != null)
                        {
                            lastBake = Time.time;
                            var body = view.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.isVisible) ?? view.Body;
                            body.BakeMesh(baked, true);
                            var m = body.transform.localToWorldMatrix;
                            float minY = float.MaxValue; float zAtMin = 0f;
                            foreach (var p in baked.vertices)
                            {
                                var w = m.MultiplyPoint3x4(p);
                                if (w.y < minY) { minY = w.y; zAtMin = Vector3.Dot(w - view.transform.position, view.transform.forward); }
                            }
                            meshZ.Add(zAtMin);
                        }
                        await Frame();
                    }
                    Object.Destroy(baked);
                    CharacterIK.FootProbe = null;
                    o[state] = $"state {StateOf(view)}; IK-goal foot Z range {(goalZ.Count > 0 ? goalZ.Max() - goalZ.Min() : -1):F2} m ({goalZ.Count}); " +
                               $"mesh contact Z range {(meshZ.Count > 0 ? meshZ.Max() - meshZ.Min() : -1):F2} m ({meshZ.Count}); locoRate {view.Animator.GetFloat("locoRate"):F2}";
                }
            }
            finally { CharacterIK.FootProbe = null; await Close(st); }
        }

        /// <summary>
        /// Foot slide over real stance windows: per foot, contact = within 4 cm of its lowest height and moving vertically
        /// slower than 0.15 m/s for at least 40 ms; slide = horizontal world displacement across each window / its
        /// duration, duration-weighted over all windows (0 = the planted foot does not move).
        /// </summary>
        private static float StanceSlide(List<(float t, Vector3 l, Vector3 r)> s, bool run = false)
        {
            float vyMax = run ? 0.6f : 0.3f, minDur = run ? 0.02f : 0.03f; // a jog's contacts are short
            if (s.Count < 20) return float.NaN;
            double dist = 0, time = 0;
            foreach (var left in new[] { true, false })
            {
                float min = s.Min(x => (left ? x.l : x.r).y);
                int start = -1;
                for (int i = 1; i <= s.Count; i++)
                {
                    bool contact = i < s.Count && (left ? s[i].l : s[i].r).y - min < 0.04f &&
                        Mathf.Abs(((left ? s[i].l : s[i].r).y - (left ? s[i - 1].l : s[i - 1].r).y) / Mathf.Max(1e-4f, s[i].t - s[i - 1].t)) < vyMax;
                    if (contact && start < 0) start = i;
                    if (!contact && start >= 0)
                    {
                        int end = i - 1;
                        float dt = s[end].t - s[start].t;
                        if (dt >= minDur)
                        {
                            var a = left ? s[start].l : s[start].r; var b = left ? s[end].l : s[end].r;
                            dist += FlatLen(b - a); time += dt;
                        }
                        start = -1;
                    }
                }
            }
            return time > 0 ? (float)(dist / time) : float.NaN;
        }

        /// <summary>Mean horizontal speed of the lower foot while it is planted (within 3 cm of its lowest height).</summary>
        private static float Slide(List<(float t, Vector3 l, Vector3 r)> s, float planted)
        {
            if (s.Count < 10) return float.NaN;
            float minL = s.Min(x => x.l.y), minR = s.Min(x => x.r.y);
            double sum = 0; int n = 0;
            for (int i = 1; i < s.Count; i++)
            {
                float dt = s[i].t - s[i - 1].t;
                if (dt <= 1e-4f) continue;
                if (s[i].l.y - minL < planted && s[i - 1].l.y - minL < planted) { sum += FlatLen(s[i].l - s[i - 1].l) / dt; n++; }
                if (s[i].r.y - minR < planted && s[i - 1].r.y - minR < planted) { sum += FlatLen(s[i].r - s[i - 1].r) / dt; n++; }
            }
            return n > 0 ? (float)(sum / n) : float.NaN;
        }

        private static float FlatLen(Vector3 v) { v.y = 0; return v.magnitude; }
        private static Vector3 Flatten(Vector3 v) { v.y = 0; return v; }

        private static string StateOf(AvatarView v)
        {
            var a = v != null ? v.Animator : null;
            if (a == null || a.runtimeAnimatorController == null || !a.isActiveAndEnabled) return "-";
            var info = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : a.GetCurrentAnimatorStateInfo(0);
            foreach (var id in new[] { "loco_idle", "loco_walk", "loco_run", "act_sit", "act_type", "act_sit_down", "act_stand_up", "act_get_up", "react_fall",
                         "react_stagger", "react_hit_chest", "jump_air", "jump_land", "react_roll", "idle_fidget_lookaround", "idle_lean_rail", "act_host_mic" })
                if (info.IsName(id)) return id;
            return "other";
        }
    }
}
