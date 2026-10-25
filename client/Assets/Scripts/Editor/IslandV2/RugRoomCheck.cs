using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace CTLife.EditorTools.IslandV2
{
/// <summary>
/// R3 (2026-10-24): a collision check of The Rug Room in the built island, edit mode: outside colliders crossing the floor,
/// floor coverage, a character body walked along the dev route (door, each game's spot, the floor loop, out) with gravity,
/// and leak walks at every wall. Batch: -executeMethod CTLife.EditorTools.IslandV2.RugRoomCheck.Run (log lines RUGDIAG).
/// </summary>
public static class RugRoomCheck
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
        }
        var doors = (JArray)JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(IslandV2Interiors.DoorsAsset).text)["doors"];
        var d = (JObject)doors.First(x => (string)x["room"] == "rug_room");
        var pos = new Vector3((float)d["p"][0], (float)d["floor"], (float)d["p"][2]); var rot = Quaternion.Euler(0f, (float)d["yaw"], 0f);
        var rp = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Interiors.PrefabDir + "/rug_room.prefab");
        var room = (GameObject)PrefabUtility.InstantiatePrefab(rp);
        room.transform.SetPositionAndRotation(pos + rot * new Vector3(0f, 0f, -0.04f), rot);
        Physics.SyncTransforms();
        var t = room.transform;
        var hits = Physics.OverlapBox(t.TransformPoint(new Vector3(0f, 1.0f, -8f)), new Vector3(6.3f, 0.8f, 7.9f), rot, ~0, QueryTriggerInteraction.Collide);
        foreach (var h in hits.Where(h => !h.transform.IsChildOf(t)))
            Debug.Log($"RUGDIAG foreign collider {Path(h.transform)} layer {h.gameObject.layer} trigger {h.isTrigger} bounds {h.bounds.center} {h.bounds.size}");
        // floor coverage
        int miss = 0; string first = null;
        for (float x = -6.2f; x <= 6.2f; x += 0.4f)
            for (float z = -15.8f; z <= -0.3f; z += 0.4f)
            {
                var p = t.TransformPoint(new Vector3(x, 1.0f, z));
                if (!Physics.Raycast(p, Vector3.down, out var hit, 3f, ~((1 << 8) | (1 << 9)), QueryTriggerInteraction.Ignore) || Mathf.Abs(hit.point.y - pos.y) > 0.25f)
                { miss++; if (first == null) first = $"{x:0.0},{z:0.0} -> {(hit.collider ? Path(hit.collider.transform) + " y " + hit.point.y : "none")}"; }
            }
        Debug.Log($"RUGDIAG floor misses {miss}; first {first}");
        var room2 = room.GetComponent<InteriorRoom>();
        foreach (var s in room2.spots) Debug.Log($"RUGDIAG spot {s.game} at {s.position} world {t.TransformPoint(s.position)}");
        Debug.Log($"RUGDIAG water at spot: {CTLife.World.Vehicles.VehicleWorld.WaterSurfaceAt(t.TransformPoint(new Vector3(3.6f,0,-6f)))}");
        // walk a character body along the route (entrance -> each spot -> the floor loop) with gravity, as the device does
        var body = new GameObject("body") { layer = 8 };
        var cc = body.AddComponent<CharacterController>();
        cc.radius = 0.28f; cc.height = 1.72f; cc.center = new Vector3(0f, 0.86f, 0f); cc.stepOffset = 0.32f; cc.slopeLimit = 45f; cc.skinWidth = 0.04f;
        var start = pos + rot * Vector3.forward * 6f;
        body.transform.position = start + Vector3.up * 0.3f;
        Physics.SyncTransforms();
        Vector3 R(float x, float z) => t.TransformPoint(new Vector3(x, 0f, z));
        var legs = new (string, Vector3[])[]
        {
            ("walk_in", new[] { pos + rot * Vector3.forward * 1.2f, pos - rot * Vector3.forward * 0.6f, R(0.4f, -2.5f) }),
            ("slots", new[] { t.TransformPoint(room2.spots[0].position) }),
            ("loop", new[] { R(3.6f, -6f), R(1.6f, -11f), R(-1.6f, -13.6f), R(-3.8f, -9f), R(-3.2f, -4f), R(0.3f, -2.2f) }),
            ("coinflip", new[] { t.TransformPoint(room2.spots[2].position) }),
            ("crash", new[] { t.TransformPoint(room2.spots[1].position) }),
            ("walk_out", new[] { R(0f, -1.0f), pos + rot * Vector3.forward * 3f }),
        };
        float vy = 0f, minY = 99f, maxY = -99f;
        foreach (var (name, pts) in legs)
            foreach (var target in pts)
            {
                bool reached = false;
                for (int f = 0; f < 600; f++)
                {
                    var dd = target - body.transform.position; dd.y = 0f;
                    if (dd.magnitude < 0.4f) { reached = true; break; }
                    vy = cc.isGrounded ? -1f : vy - 9.8f / 30f;
                    cc.Move(dd.normalized * (3.4f / 30f) + Vector3.up * vy / 30f);
                    minY = Mathf.Min(minY, body.transform.position.y); maxY = Mathf.Max(maxY, body.transform.position.y);
                }
                Debug.Log($"RUGDIAG leg {name} -> {target}: {(reached ? "reached" : "BLOCKED at " + body.transform.position)} y {body.transform.position.y:0.00}");
            }
        // leak test: from the middle of the floor straight at points 12 m beyond each wall and the corners
        foreach (var dir in new[] { new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 0, -1), new Vector3(0.7f, 0, 0.7f), new Vector3(-0.7f, 0, 0.7f), new Vector3(0.7f, 0, -0.7f), new Vector3(-0.7f, 0, -0.7f), new Vector3(0.25f, 0, 1f) })
        {
            body.transform.position = R(0.3f, -6f) + Vector3.up * 0.1f; Physics.SyncTransforms();
            var target = R(0.3f, -6f) + t.rotation * dir.normalized * 14f;
            for (int f = 0; f < 300; f++) { var dd = target - body.transform.position; dd.y = 0; vy = cc.isGrounded ? -1f : vy - 0.33f; cc.Move(dd.normalized * 0.113f + Vector3.up * vy / 30f); }
            var l = t.InverseTransformPoint(body.transform.position);
            Debug.Log($"RUGDIAG leak {dir}: ends room-local {l} {(Mathf.Abs(l.x) < 6.4f && l.z < 0f && l.z > -16f && l.y > -0.5f ? "INSIDE" : "OUTSIDE")}");
        }
        foreach (var c in Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
            if (c.name == "coast_pad") Debug.Log($"RUGDIAG pad wall {c.bounds.center} {c.bounds.size}");
        Debug.Log($"RUGDIAG body y range {minY:0.00}..{maxY:0.00} (floor {pos.y:0.00})");
        EditorApplication.Exit(0);
    }
    static string Path(Transform x) { var s = x.name; while (x.parent != null) { x = x.parent; s = x.name + "/" + s; } return s; }
}
}
