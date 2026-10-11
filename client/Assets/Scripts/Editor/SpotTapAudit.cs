using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CTLife.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// For each venue: does every claimable spot (seat, stage, terminal) sit on visible furniture, and does tapping it from
    /// the venue's own game camera focus it? Taps aim at the seat (anchor + 0.45 m) from the venue camera yaw/pitch/distance
    /// and three more yaws (90° steps), and run the shipping resolver `WorldInput.ResolveSpotTap` next to the old rule
    /// (first solid hit, 3D ≤ 0.9 m). Batch: `-executeMethod CTLife.EditorTools.SpotTapAudit.RunBatch -ctlifeOut <report.md>`.
    /// </summary>
    public static class SpotTapAudit
    {
        private static readonly string[] Venues = { "terminal_cafe", "spaces_studio", "timeline_square" };

        public static void RunBatch()
        {
            int code = 0;
            try
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-ctlifeOut");
                string outPath = i >= 0 && i + 1 < args.Length ? args[i + 1] : "spot-tap-audit.md";
                File.WriteAllText(outPath, Run());
                Debug.Log("CTLIFE [spot-audit] wrote " + outPath);
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        public static string Run()
        {
            var md = new StringBuilder("# Spot tap audit\n\n");
            foreach (var venueId in Venues)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var prefab = VenueContent.Find(venueId);
                if (prefab == null) { md.Append($"## {venueId}\nno content\n\n"); continue; }
                var root = UnityEngine.Object.Instantiate(prefab);
                Physics.SyncTransforms();
                var anchors = root.GetComponentsInChildren<SpotAnchor>(true).Where(a => !string.IsNullOrEmpty(a.anim)).ToList();
                var all = anchors.ToArray();
                md.Append($"## {venueId} - {anchors.Count} claimable spots (camera yaw {root.cameraYaw:0}°, pitch {root.cameraPitch:0}°, distance {(root.cameraDistance > 0 ? root.cameraDistance : 15f):0.0} m)\n\n");
                md.Append("| spot | kind | anim | furniture at anchor (≤0.45 m) | nearest prop (m) | old tap (4 yaws) | new tap (4 yaws) |\n|---|---|---|---|---|---|---|\n");
                int oldOk = 0, newOk = 0, total = 0, offFurniture = 0, defaultOld = 0, defaultNew = 0;
                var misses = new StringBuilder();
                foreach (var a in anchors)
                {
                    var (onFurniture, nearestName, nearestD) = Furniture(a.transform.position);
                    bool standing = a.anim.StartsWith("loco_");
                    if (!onFurniture && !standing) offFurniture++;
                    int o = 0, nw = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        // A seat is tapped on the seat; a standing spot (the host's mic) on the prop beside it (desk / mic).
                        var aim = standing && NearestProp(a.transform.position) is Collider prop ? prop.bounds.center : a.transform.position + Vector3.up * 0.45f;
                        var hits = TapHits(root, aim, k * 90f);
                        if (OldRule(hits, all) == a) { o++; if (k == 0) defaultOld++; }
                        var got = WorldInput.ResolveSpotTap(hits, hits.Length, all);
                        if (got == a) { nw++; if (k == 0) defaultNew++; }
                        else if (k == 0)
                            misses.Append($"- {a.spotId}: picked {(got != null ? got.spotId : "nothing")}; first hits: " +
                                string.Join(", ", hits.Where(h => !h.collider.isTrigger).Take(3).Select(h => $"{Short(h.collider.name)}" +
                                $"{(h.collider.GetComponentInParent<SpotAnchor>() is SpotAnchor sa ? "[" + sa.spotId + "]" : "")} @{h.distance:0.0}m")) + "\n");
                    }
                    oldOk += o; newOk += nw; total += 4;
                    string furniture = standing ? "(standing spot)" : onFurniture ? "yes" : "**no**";
                    md.Append($"| {a.spotId} | {a.kind} | {a.anim} | {furniture} | {nearestName} {nearestD:0.00} | {o}/4 | {nw}/4 |\n");
                }
                md.Append($"\nTaps that focus the right spot: old rule **{oldOk}/{total}**, new rule **{newOk}/{total}** (4 yaws); from the venue's default camera yaw: old **{defaultOld}/{anchors.Count}**, new **{defaultNew}/{anchors.Count}**. Seats/terminals not on furniture: **{offFurniture}**.\n\n");
                if (misses.Length > 0) md.Append("Misses from the default yaw:\n").Append(misses).Append("\n");
                UnityEngine.Object.DestroyImmediate(root.gameObject);
            }
            return md.ToString();
        }

        /// <summary>Is there a furniture-sized solid collider around the anchor (sit height), and the nearest prop?</summary>
        private static (bool on, string name, float d) Furniture(Vector3 p)
        {
            var near = Physics.OverlapSphere(p + Vector3.up * 0.45f, 1.6f, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => c.bounds.size.x <= WorldInput.FurnitureMaxSize && c.bounds.size.z <= WorldInput.FurnitureMaxSize)
                .Select(c => (c, d: Vector2.Distance(new Vector2(c.ClosestPoint(p + Vector3.up * 0.45f).x, c.ClosestPoint(p + Vector3.up * 0.45f).z), new Vector2(p.x, p.z))))
                .OrderBy(t => t.d).ToList();
            if (near.Count == 0) return (false, "none", float.NaN);
            return (near[0].d <= 0.45f, Short(near[0].c.name), near[0].d);
        }

        private static Collider NearestProp(Vector3 p) =>
            Physics.OverlapSphere(p + Vector3.up * 0.45f, 1.6f, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => c.bounds.size.x <= WorldInput.FurnitureMaxSize && c.bounds.size.z <= WorldInput.FurnitureMaxSize)
                .OrderBy(c => Vector3.Distance(c.ClosestPoint(p + Vector3.up * 0.45f), p + Vector3.up * 0.45f)).FirstOrDefault();

        private static string Short(string s) => s.Length > 28 ? s.Substring(0, 28) : s;

        private static RaycastHit[] TapHits(VenueSceneRoot root, Vector3 aim, float extraYaw)
        {
            float dist = root.cameraDistance > 0f ? root.cameraDistance : 15f;
            float pitch = root.cameraDistance > 0f ? root.cameraPitch : 50f;
            float yaw = (root.cameraDistance > 0f ? root.cameraYaw : 45f) + extraYaw;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var camPos = aim - rot * Vector3.forward * dist;
            var hits = Physics.RaycastAll(new Ray(camPos, (aim - camPos).normalized), 200f, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            return hits;
        }

        /// <summary>The rule before 2026-10-08: the first solid hit only; its own anchor, else an anchor ≤ 0.9 m in 3D.</summary>
        private static SpotAnchor OldRule(RaycastHit[] hits, SpotAnchor[] anchors)
        {
            foreach (var h in hits)
            {
                if (h.collider.isTrigger) continue;
                var tagged = h.collider.GetComponentInParent<SpotAnchor>();
                if (tagged != null) return string.IsNullOrEmpty(tagged.anim) ? null : tagged;
                SpotAnchor best = null; float bestD = 0.81f;
                foreach (var s in anchors) { float d = (s.transform.position - h.point).sqrMagnitude; if (d < bestD) { bestD = d; best = s; } }
                return best;
            }
            return null;
        }
    }
}
