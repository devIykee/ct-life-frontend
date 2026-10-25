using System.Collections.Generic;
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
/// The community nods checked in the built island, edit mode (R4, 2026-10-25), as RektBurgerCheck does for the restaurant:
/// for every nod in Resources/World/Island/nods.json, the ground under the disc is the island's Ground or Road (not a roof,
/// a prop or the water) at the exported height; nothing solid stands in it (buildings, props, interiors, coast walls,
/// vehicle blockers); it is inside its own district's polygon (contract/ids/districts.json vs contract/ids/nods.json) and
/// 2 m clear of every walk-in room; a character body walks to the spot in front of the disc from the nearest walkway
/// (Resources/World/Island/pedestrian_paths.json). With -ctlife-captureOut it renders a few nods in context with the real
/// prop. Batch: -executeMethod CTLife.EditorTools.IslandV2.NodCheck.Run (log lines NODDIAG; "NODDIAG PASS" or "NODDIAG FAIL").
/// </summary>
public static class NodCheck
{
    private static readonly string[] Shots = { "ansem", "phantom", "orca", "magicblock", "bonk", "colosseum" };

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Paths.BasePrefab));
        var world = root.GetComponent<IslandWorld>();
        var hf = world.LoadHeightfield();
        root.GetComponent<IslandTerrainFar>()?.Build(hf);
        foreach (var c in world.cells)
        {
            var cp = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Paths.CellsDir + "/" + c.id + ".prefab");
            if (cp == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(cp);
            go.transform.SetParent(root.transform, false);
            go.GetComponentInChildren<IslandTerrainCell>(true)?.BuildNow(hf);
            if (c.far != null) root.transform.Find("Far/" + c.id + "_far")?.gameObject.SetActive(false);
        }
        Physics.SyncTransforms();
        var repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        var registry = JObject.Parse(File.ReadAllText(Path.Combine(repo, "contract/ids/nods.json")));
        var districts = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(repo, "contract/ids/districts.json")))["districts"];
        var regById = registry["nods"].ToDictionary(n => (string)n["nodId"], n => (JObject)n);
        var peds = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/World/Island/pedestrian_paths.json").text);
        var walk = new List<Vector3>();
        foreach (var s in ((JArray)peds["sidewalks"]).Concat((JArray)peds["corners"]))
            foreach (var p in (JArray)s["points"]) walk.Add(new Vector3((float)p[0], (float)p[1], (float)p[2]));
        var doors = (JArray)JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(IslandV2Interiors.DoorsAsset).text)["doors"];
        var nods = NodField.Load();
        int fails = 0;
        var body = new GameObject("body") { layer = CharacterLayers.Characters };
        var cc = body.AddComponent<CharacterController>();
        cc.radius = 0.28f; cc.height = 1.72f; cc.center = new Vector3(0f, 0.86f, 0f); cc.stepOffset = 0.32f; cc.slopeLimit = 45f; cc.skinWidth = 0.04f;
        int solid = ~((1 << 10) | (1 << 11) | (1 << CharacterLayers.Characters) | (1 << 2));
        foreach (var n in nods)
        {
            var why = new List<string>();
            var reg = regById.TryGetValue(n.nodId, out var r) ? r : null;
            if (reg == null) why.Add("not in the registry");
            else if (Mathf.Abs((float)reg["x"] - n.x) > 0.01f || Mathf.Abs((float)reg["z"] - n.z) > 0.01f) why.Add("registry x/z differ");
            // the ground
            var hits = Physics.RaycastAll(new Vector3(n.x, n.y + 6f, n.z), Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
            var top = hits.Length > 0 ? hits[0] : default;
            if (hits.Length == 0) why.Add("no ground");
            else if (top.collider.gameObject.layer != 10 && top.collider.gameObject.layer != 11) why.Add($"stands on {Name(top.collider.transform)} (layer {top.collider.gameObject.layer})");
            else if (Mathf.Abs(top.point.y - n.y) > 1.5f) why.Add($"ground {top.point.y:0.00} vs exported {n.y:0.00}");
            // (a raised surface the heightfield does not know, the Timeline plaza's 0.26 m: NodField settles the disc on the
            // collider under it at runtime, within 1.5 m)
            else if (Mathf.Abs(top.point.y - n.y) > 0.3f) Debug.Log($"NODDIAG {n.nodId}: settles {top.point.y - n.y:+0.00;-0.00} m onto {Name(top.collider.transform)}");
            float gy = hits.Length > 0 ? top.point.y : n.y;
            // anything solid in it
            foreach (var h in Physics.OverlapCapsule(new Vector3(n.x, gy + 0.25f, n.z), new Vector3(n.x, gy + 1.3f, n.z), 0.3f, solid, QueryTriggerInteraction.Ignore))
                why.Add($"in {Name(h.transform)} (layer {h.gameObject.layer})");
            // the district
            string inside = null;
            foreach (var d in districts.Properties())
                if (InPoly(n.x, n.z, (JArray)d.Value)) { inside = d.Name; break; }
            if (reg != null && inside != (string)reg["district"]) why.Add($"in {inside ?? "no district"}, registry says {reg["district"]}");
            // the walk-in rooms
            foreach (var dr in doors)
            {
                var p = new Vector3((float)dr["p"][0], 0f, (float)dr["p"][2]);
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(n.x, n.z)) < 3f) why.Add("at a walk-in door " + dr["room"]);
            }
            // walk to the front of the disc from the nearest walkway
            var front = new Vector3(n.x, gy, n.z) + Quaternion.Euler(0f, n.yaw, 0f) * Vector3.forward * 1.3f;
            var from = walk.OrderBy(w => (w.x - front.x) * (w.x - front.x) + (w.z - front.z) * (w.z - front.z)).First();
            if (Vector2.Distance(new Vector2(from.x, from.z), new Vector2(front.x, front.z)) > 14f) from = front + (front - new Vector3(n.x, gy, n.z)).normalized * 6f;
            // (a nod on the boardwalk deck or in a park faces along it, away from a street behind a railing: then the open
            // ground 4 m in front of it, which its own deck or park joins to the walkways, is the start)
            var ahead = front + Quaternion.Euler(0f, n.yaw, 0f) * Vector3.forward * 4f;
            bool reached = Walk(cc, from, front) || Walk(cc, ahead, front);
            if (!reached) why.Add($"unreachable on foot from the walkway at ({from.x:0.0}, {from.z:0.0}) or from 4 m in front, stopped at ({body.transform.position.x:0.0}, {body.transform.position.z:0.0})");
            if (why.Count > 0) { fails++; Debug.Log($"NODDIAG {n.nodId} ({n.x:0.0}, {n.z:0.0}) FAIL: {string.Join("; ", why)}"); }
        }
        Debug.Log($"NODDIAG checked {nods.Length}, failures {fails}");
        var outDir = Arg("-ctlife-captureOut");
        if (outDir != null) Capture(nods, outDir);
        Debug.Log("NODDIAG " + (fails == 0 && nods.Length == 80 ? "PASS" : "FAIL"));
        EditorApplication.Exit(fails == 0 && nods.Length == 80 ? 0 : 1);
    }

    /// <summary>The nods drawn as plain objects with the real mesh and materials, a few shot in context.</summary>
    private static void Capture(NodField.Nod[] nods, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var mesh = NodField.BuildMesh();
        var mats = NodField.BuildMaterials(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/World/Island/nod_atlas.png"));
        foreach (var n in nods)
        {
            var go = new GameObject("nod_" + n.nodId);
            go.transform.SetPositionAndRotation(new Vector3(n.x, Ground(n), n.z), Quaternion.Euler(0f, n.yaw, 0f));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mats[Mathf.Max(0, System.Array.IndexOf(NodField.Sets, n.setId))];
        }
        var root = Object.FindFirstObjectByType<IslandWorld>();
        root?.GetComponent<IslandAtmosphere>()?.Apply();
        // kit instances (trees, the hero statues, street furniture): edit mode has no LateUpdate, so decode and draw them here
        foreach (var set in Object.FindObjectsByType<IslandInstances>(FindObjectsSortMode.None)) { set.Decode(); IslandInstanceRenderer.Register(set); }
        var kit = root != null ? root.GetComponent<IslandInstanceRenderer>() : null;
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.nearClipPlane = 0.05f; cam.farClipPlane = 2000f;
        var rt = new RenderTexture(1600, 900, 24);
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        foreach (var id in Shots)
        {
            var n = nods.FirstOrDefault(x => x.nodId == id);
            if (n == null) continue;
            var at = new Vector3(n.x, Ground(n) + NodField.DiscHeight, n.z);
            var face = Quaternion.Euler(0f, n.yaw, 0f) * Vector3.forward;
            foreach (var (tag, dist, side, fov) in new[] { ("close", 2.6f, 0.9f, 50f), ("context", 9f, 3.5f, 60f) })
            {
                var eye = at + face * dist + Quaternion.Euler(0f, n.yaw + 90f, 0f) * Vector3.forward * side + Vector3.up * (tag == "close" ? 0.55f : 1.6f);
                cam.fieldOfView = fov;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
                cam.targetTexture = rt;
                if (kit != null) { IslandInstanceRenderer.TargetCamera = cam; kit.Render(cam); }
                cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(System.IO.Path.Combine(outDir, $"nod_{id}_{tag}.png"), tex.EncodeToPNG());
            }
            Debug.Log($"NODDIAG captured {id} ({n.setId})");
        }
        cam.targetTexture = null;
    }

    private static bool Walk(CharacterController cc, Vector3 from, Vector3 to)
    {
        cc.transform.position = from + Vector3.up * 0.4f;
        Physics.SyncTransforms();
        float vy = 0f;
        for (int f = 0; f < 500; f++)
        {
            var dd = to - cc.transform.position; dd.y = 0f;
            if (dd.magnitude < 0.5f) return true;
            vy = cc.isGrounded ? -1f : vy - 9.8f / 30f;
            cc.Move(dd.normalized * (3.4f / 30f) + Vector3.up * vy / 30f);
        }
        return false;
    }

    private static float Ground(NodField.Nod n) =>
        Physics.Raycast(new Vector3(n.x, n.y + 6f, n.z), Vector3.down, out var hit, 12f, (1 << 10) | (1 << 11), QueryTriggerInteraction.Ignore) ? hit.point.y : n.y;

    private static bool InPoly(float x, float z, JArray poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            float xi = (float)poly[i][0], zi = (float)poly[i][1], xj = (float)poly[j][0], zj = (float)poly[j][1];
            if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) inside = !inside;
        }
        return inside;
    }

    private static string Arg(string name)
    {
        var a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
    static string Name(Transform x) { var s = x.name; while (x.parent != null) { x = x.parent; s = x.name + "/" + s; } return s; }
}
}
