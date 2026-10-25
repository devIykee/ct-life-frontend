using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace CTLife.EditorTools.IslandV2
{
/// <summary>
/// Rekt Burger (2026-10-25): the walk-in checked in the built island, edit mode, as RugRoomCheck does for the casino: the
/// colliders round the door, a character body walked along the dev route (outside, the door, the counter spot, the dining
/// loop, out) with gravity, the counter spot inside the room and in reach, and the follow camera's boom (CameraRig's own
/// clearance and sight maths) behind the body at each stop, rendered to -ctlife-captureOut when given.
/// Batch: -executeMethod CTLife.EditorTools.IslandV2.RektBurgerCheck.Run (log lines RBDIAG; "RBDIAG PASS" or "RBDIAG FAIL").
/// </summary>
public static class RektBurgerCheck
{
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Paths.BasePrefab);
        var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var world = root.GetComponent<IslandWorld>();
        var hf = world.LoadHeightfield();
        foreach (var c in world.cells)
        {
            var cp = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Paths.CellsDir + "/" + c.id + ".prefab");
            if (cp == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(cp);
            go.transform.SetParent(root.transform, false);
            go.GetComponentInChildren<IslandTerrainCell>(true)?.BuildNow(hf);
            if (c.far != null) root.transform.Find("Far/" + c.id + "_far")?.gameObject.SetActive(false);
        }
        var doors = (JArray)JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(IslandV2Interiors.DoorsAsset).text)["doors"];
        var d = (JObject)doors.First(x => (string)x["room"] == RektBurgerHooks.RoomId);
        var pos = new Vector3((float)d["p"][0], (float)d["floor"], (float)d["p"][2]); var rot = Quaternion.Euler(0f, (float)d["yaw"], 0f);
        var fwd = rot * Vector3.forward;
        var rp = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Interiors.PrefabDir + "/" + RektBurgerHooks.RoomId + ".prefab");
        var room = (GameObject)PrefabUtility.InstantiatePrefab(rp);
        room.transform.SetPositionAndRotation(pos + rot * new Vector3(0f, 0f, -0.04f), rot);
        Physics.SyncTransforms();
        var t = room.transform;
        var ir = room.GetComponent<InteriorRoom>();
        bool ok = true;
        Debug.Log($"RBDIAG door {pos} yaw {(float)d["yaw"]} room bounds {ir.boundsMin}..{ir.boundsMax}");
        // what stands in and round the doorway (2 m either side of the facade plane)
        foreach (var h in Physics.OverlapBox(pos + Vector3.up * 1.0f, new Vector3(1.2f, 0.9f, 2.0f), rot, ~0, QueryTriggerInteraction.Ignore))
            Debug.Log($"RBDIAG near door: {Path(h.transform)} layer {h.gameObject.layer} {(h.transform.IsChildOf(t) ? "room" : "OUTSIDE")} bounds {h.bounds.center} {h.bounds.size}");
        foreach (var h in Physics.OverlapBox(t.TransformPoint(new Vector3(0f, 1.0f, -6.1f)), new Vector3(5.0f, 0.8f, 5.6f), rot, ~0, QueryTriggerInteraction.Ignore).Where(h => !h.transform.IsChildOf(t)))
            Debug.Log($"RBDIAG foreign collider in the room: {Path(h.transform)} layer {h.gameObject.layer} bounds {h.bounds.center} {h.bounds.size}");
        var spot = ir.spots.FirstOrDefault(s => s.game == RektBurgerHooks.Shift);
        var spotW = t.TransformPoint(spot.position);
        bool spotIn = ir.Contains(spot.position, 0.05f);
        Debug.Log($"RBDIAG spot {spot.game} local {spot.position} world {spotW} inside {spotIn} range {spot.range}");
        ok &= spotIn && spot.game == RektBurgerHooks.Shift;
        ok &= room.GetComponent<RektBurgerFloor>() != null;
        // a body along the route (RektBurgerRoute: the door, P(0.3, 1.6), the counter spot, the dining loop), gravity on
        var body = new GameObject("body") { layer = CharacterLayers.Characters };
        var cc = body.AddComponent<CharacterController>();
        cc.radius = 0.28f; cc.height = 1.72f; cc.center = new Vector3(0f, 0.86f, 0f); cc.stepOffset = 0.32f; cc.slopeLimit = 45f; cc.skinWidth = 0.04f;
        body.transform.position = pos + fwd * 5f + Vector3.up * 0.3f;
        Physics.SyncTransforms();
        Vector3 P(float bx, float by) => t.TransformPoint(ir.FromSource(bx, by, 0f));
        var legs = new (string, Vector3[])[]
        {
            ("walk_in", new[] { pos + fwd * 1.2f, pos - fwd * 0.6f, P(0.3f, 1.6f) }),
            ("counter", new[] { spotW }),
            ("loop", RektBurgerRoute.Loop.Select(q => P(q.Item1, q.Item2)).ToArray()),
            // the clock-in loop: through the staff way in (right wall) to every station, round once, back out
            ("kitchen", RektBurgerWork.WayIn.Select(w => P(w.x, w.y)).Concat(RektBurgerWork.Stations.Select(st => P(st.x, st.y)))
                .Concat(new[] { P(RektBurgerWork.Stations[0].x, RektBurgerWork.Stations[0].y) })
                .Concat(RektBurgerWork.WayIn.Reverse().Select(w => P(w.x, w.y))).Concat(new[] { P(0f, 1.4f) }).ToArray()),
            ("walk_out", new[] { pos - fwd * 0.6f, pos + fwd * 3f }),
        };
        float vy = 0f;
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f;
        var outDir = Arg("-ctlife-captureOut");
        foreach (var (name, pts) in legs)
        {
            foreach (var target in pts)
            {
                bool reached = false;
                for (int f = 0; f < 600; f++)
                {
                    var dd = target - body.transform.position; dd.y = 0f;
                    if (dd.magnitude < 0.4f) { reached = true; break; }
                    vy = cc.isGrounded ? -1f : vy - 9.8f / 30f;
                    cc.Move(dd.normalized * (3.4f / 30f) + Vector3.up * vy / 30f);
                }
                var bp = body.transform.position;
                Debug.Log($"RBDIAG leg {name} -> {target}: {(reached ? "reached" : "BLOCKED at " + bp)} y {bp.y:0.00} room-local {t.InverseTransformPoint(bp)}");
                if (!reached)
                {
                    ok = false;
                    var dir = target - bp; dir.y = 0f; dir.Normalize();
                    foreach (var h in Physics.OverlapCapsule(bp + Vector3.up * 0.3f + dir * 0.15f, bp + Vector3.up * 1.45f + dir * 0.15f, 0.34f, ~(1 << CharacterLayers.Characters), QueryTriggerInteraction.Ignore))
                        Debug.Log($"RBDIAG   blocker {Path(h.transform)} layer {h.gameObject.layer} bounds {h.bounds.center} {h.bounds.size}");
                }
            }
            // the follow camera behind the body, facing where it walked: CameraRig's boom clearance and body sight
            var face = pts.Length > 0 ? pts[pts.Length - 1] - (pts.Length > 1 ? pts[pts.Length - 2] : pos + fwd * 5f) : Vector3.forward;
            face.y = 0f;
            if (face.sqrMagnitude < 1e-4f) face = -fwd;
            var feet = body.transform.position;
            var pivot = feet + Vector3.up * 1.55f;
            var camRot = Quaternion.Euler(14f, Quaternion.LookRotation(face).eulerAngles.y, 0f);
            float clear = CameraRig.Clearance(pivot, camRot * Vector3.back, 4.2f, CameraRig.CollisionMask, out _);
            float seen = CameraRig.VisibleBoom(pivot, camRot * Vector3.back, clear, feet, CameraRig.OcclusionMask);
            float boom = Mathf.Min(clear, seen);
            var lens = pivot + camRot * Vector3.back * boom;
            bool lensIn = ir.Contains(t.InverseTransformPoint(lens), 0.0f);
            Debug.Log($"RBDIAG camera after {name}: boom {boom:0.00} (clear {clear:0.00}, sight {seen:0.00}) lens room-local {t.InverseTransformPoint(lens)} in room {lensIn}");
            if (name == "counter" || name == "loop" || name == "kitchen") ok &= lensIn && boom > 1.2f;
            if (outDir != null)
            {
                cam.transform.SetPositionAndRotation(lens, Quaternion.LookRotation(pivot - lens));
                var rt = new RenderTexture(1280, 720, 24);
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                Directory.CreateDirectory(outDir);
                File.WriteAllBytes(System.IO.Path.Combine(outDir, "rbcheck-" + name + ".png"), tex.EncodeToPNG());
                RenderTexture.active = null; cam.targetTexture = null;
                Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
        }
        Debug.Log("RBDIAG " + (ok ? "PASS" : "FAIL"));
        EditorApplication.Exit(ok ? 0 : 1);
    }
    static string Arg(string name)
    {
        var a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
    static string Path(Transform x) { var s = x.name; while (x.parent != null) { x = x.parent; s = x.name + "/" + s; } return s; }
}
}
