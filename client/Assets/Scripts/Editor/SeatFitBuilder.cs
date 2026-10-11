using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Measures every claimable seat in the shipped venues/homes against the RENDERED geometry (scratch mesh colliders
    /// from the LOD0 render meshes): a height profile along the seat's forward axis gives the seat plateau (top and
    /// extent), the backrest behind it, the front edge, the floor under the feet and a footrest (stools). The seat kind
    /// comes from that geometry (height, backrest, softness hints from the venue's own prop vocabulary). Writes
    /// Resources/Characters/SeatFits.asset (seats + seated-pose metrics per family, see <see cref="MeasurePoses"/>).
    /// Batch: -executeMethod CTLife.EditorTools.SeatFitBuilder.BuildBatch [-ctlife-seatOut /abs/report.json]
    /// </summary>
    public static class SeatFitBuilder
    {
        public const string CatalogPath = "Assets/Resources/Characters/SeatFits.asset";
        private const int Scratch = 31;
        private const float Step = 0.02f;

        public static void BuildBatch()
        {
            int code = 0;
            try
            {
                var report = Build();
                var outPath = Arg("-ctlife-seatOut");
                if (!string.IsNullOrEmpty(outPath)) { Directory.CreateDirectory(Path.GetDirectoryName(outPath)); File.WriteAllText(outPath, report.ToString()); }
                Debug.Log("CTLIFE [seat-fit] " + report["summary"]);
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        [MenuItem("CT Life/Characters/Measure seats (SeatFits)")]
        public static JObject Build()
        {
            var report = new JObject();
            var venues = new JObject();
            report["venues"] = venues;
            var seats = new List<SeatFitCatalog.Seat>();
            var ids = VenueContent.VenueIds.Select(v => (v, false)).Concat(VenueContent.BuiltHomes.Select(h => (h, true))).ToList();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var (id, home) in ids)
            {
                var prefab = home ? VenueContent.FindHome(id) : VenueContent.Find(id);
                if (prefab == null) continue;
                var root = (VenueSceneRoot)PrefabUtility.InstantiatePrefab(prefab, scene);
                var scratch = ScratchColliders(root);
                try
                {
                    Physics.SyncTransforms();
                    var rows = new JArray();
                    string venueKey = home ? "home:" + id : id;
                    foreach (var a in root.GetComponentsInChildren<SpotAnchor>(true))
                    {
                        if (!IsSeat(a)) continue;
                        var seat = Measure(root, a, out var detail);
                        seat.venueId = home ? "home:" + id : id;
                        seats.Add(seat);
                        detail["spot"] = a.spotId;
                        rows.Add(detail);
                    }
                    if (rows.Count > 0) venues[venueKey] = rows;
                }
                finally
                {
                    foreach (var go in scratch) Object.DestroyImmediate(go);
                    Object.DestroyImmediate(root.gameObject);
                }
            }
            var catalog = AssetDatabase.LoadAssetAtPath<SeatFitCatalog>(CatalogPath);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<SeatFitCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            catalog.seats = seats;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            var kinds = seats.GroupBy(s => s.kind).Select(g => $"{g.Key} {g.Count()}");
            report["summary"] = $"{seats.Count} seats in {venues.Count} venues/homes: {string.Join(", ", kinds)}";
            return report;
        }

        public static bool IsSeat(SpotAnchor a)
        {
            if (a == null || string.IsNullOrEmpty(a.anim)) return false;
            if (a.kind == "exit" || a.kind == "npc" || a.kind == "stand" || a.kind == "standing") return false;
            return a.anim == "act_sit" || a.anim == "act_type" || a.anim == "act_frantic_type" || a.anim == "act_lean_back";
        }

        private static List<GameObject> ScratchColliders(VenueSceneRoot root)
        {
            var list = new List<GameObject>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.name.Contains("_LOD1") || mf.name.Contains("_LOD2")) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled) continue;
                var go = new GameObject("seatfit_" + mf.name) { layer = Scratch, hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                go.transform.localScale = mf.transform.lossyScale;
                go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                list.Add(go);
            }
            return list;
        }

        /// <summary>Top surface height (relative to y0) of the first rendered surface below `from`, or NaN.</summary>
        private static float Top(Vector3 from, float y0, float maxDrop = 2.2f)
        {
            return Physics.Raycast(from, Vector3.down, out var hit, maxDrop, 1 << Scratch, QueryTriggerInteraction.Collide) && hit.normal.y > 0.3f
                ? hit.point.y - y0 : float.NaN;
        }

        public static SeatFitCatalog.Seat Measure(VenueSceneRoot venue, SpotAnchor a, out JObject detail)
        {
            var o = a.transform.position;
            var f = a.transform.forward; f.y = 0; f.Normalize();
            var r = Vector3.Cross(Vector3.up, f);
            float y0 = o.y;
            detail = new JObject { ["kind_spot"] = a.kind, ["anim"] = a.anim };
            // 1) Top-envelope height profile along the forward axis: max of the rendered top over a ±3 cm z window and
            //    ±10 cm laterally (bridges bench slats and ladder backs; stays inside armrests).
            var zs = new List<float>(); var raw = new List<float>();
            for (float z = -0.9f; z <= 0.6f + 1e-4f; z += Step)
            {
                var lat = new List<float>();
                foreach (float x in new[] { -0.1f, 0f, 0.1f })
                {
                    float h = Top(o + f * z + r * x + Vector3.up * 1.6f, y0, 1.9f);
                    if (!float.IsNaN(h)) lat.Add(h);
                }
                zs.Add(z); raw.Add(lat.Count >= 2 ? lat.OrderBy(v => v).ElementAt(lat.Count / 2) : float.NaN); // lateral median: ignores side pillows
            }
            var hs = new List<float>();
            for (int i = 0; i < raw.Count; i++)
            {
                float m = float.NaN;
                for (int j = Math.Max(0, i - 1); j <= Math.Min(raw.Count - 1, i + 1); j++)
                    if (!float.IsNaN(raw[j]) && raw[j] < 1.05f && (float.IsNaN(m) || raw[j] > m)) m = raw[j];
                hs.Add(m);
            }
            var seat = new SeatFitCatalog.Seat { spotId = a.spotId };
            float floor = FloorAt(o, f, r, y0, a.approach != null ? a.approach.position.y - y0 : 0f);
            seat.floor = floor;
            // 2) Seat plateau: the longest run within ±3 cm between 0.15 and 0.95 m above the floor, near the hips.
            int bestA = -1, bestB = -1; float bestScore = float.MinValue;
            for (int i = 0; i < hs.Count; i++)
            {
                if (float.IsNaN(hs[i]) || hs[i] - floor < 0.15f || hs[i] - floor > 0.95f) continue;
                int j = i; float lo = hs[i], hi = hs[i];
                while (j + 1 < hs.Count && !float.IsNaN(hs[j + 1]))
                {
                    float nlo = Mathf.Min(lo, hs[j + 1]), nhi = Mathf.Max(hi, hs[j + 1]);
                    if (nhi - nlo > 0.06f) break;
                    lo = nlo; hi = nhi; j++;
                }
                float length = zs[j] - zs[i];
                float centre = (zs[i] + zs[j]) * 0.5f;
                float score = length - Mathf.Abs(centre + 0.15f) * 0.6f;
                if (score > bestScore) { bestScore = score; bestA = i; bestB = j; }
            }
            if (bestA < 0)
            {
                seat.kind = SeatKind.Chair; seat.seatTop = floor + 0.46f; seat.note = "no seat geometry found; chair default";
                detail["profile"] = Profile(zs, hs); return seat;
            }
            float top = Median(hs.Skip(bestA).Take(bestB - bestA + 1));
            float zBack = zs[bestA], zFront = zs[bestB];
            seat.seatTop = top;
            seat.frontEdge = zFront;
            seat.rearEdge = zBack;
            float above = top - floor;
            // 3) Backrest: horizontal rays backward at several heights above the seat (ladder backs: several laterals).
            float Back(float h)
            {
                float nearest = float.NaN;
                foreach (float x in new[] { -0.17f, -0.12f, -0.06f, 0f, 0.06f, 0.12f, 0.17f })
                {
                    var start = o + f * zFront + r * x + Vector3.up * (top + h);
                    if (Physics.Raycast(start, -f, out var hit, (zFront - zBack) + 0.45f, 1 << Scratch, QueryTriggerInteraction.Collide))
                    {
                        float d = -Vector3.Dot(hit.point - o, f);
                        if (float.IsNaN(nearest) || d < nearest) nearest = d;
                    }
                }
                return nearest; // distance behind the anchor
            }
            // Lumbar band (12–25 cm above the seat: where a seated back rests); ladder backs: any hit in the band.
            var lumbar = new List<float>(); var anyBack = new List<float>();
            for (float h = 0.08f; h <= 0.5f; h += 0.03f)
            {
                float d = Back(h);
                if (float.IsNaN(d)) continue;
                anyBack.Add(d);
                if (h >= 0.12f && h <= 0.25f) lumbar.Add(d);
            }
            float upper = Back(0.48f);
            if (lumbar.Count > 0 || anyBack.Count > 0)
            {
                var band = lumbar.Count > 0 ? lumbar : anyBack;
                seat.backDistance = band.OrderBy(v => v).ElementAt(band.Count / 2);
                detail["recline"] = float.IsNaN(upper) ? "-" : Math.Round(upper - seat.backDistance, 3).ToString();
            }
            // 4) Solid below the seat (upholstered lumps: bean bags, poufs, sofas) vs legs (chairs, stools, benches).
            int solid = 0, solidTotal = 0;
            foreach (float h in new[] { 0.08f, 0.2f })
                foreach (float x in new[] { -0.15f, 0f, 0.15f })
                {
                    if (h > above - 0.05f) continue;
                    solidTotal++;
                    var start = o + f * (zFront + 0.7f) + r * x + Vector3.up * (floor + h);
                    if (Physics.Raycast(start, -f, out var hit, 0.7f + (zFront - zBack) * 0.5f, 1 << Scratch, QueryTriggerInteraction.Collide)) solid++;
                }
            // Just under the seat: upholstery is solid across, a chair/stool/office base is not (a pole at most).
            int high = 0;
            foreach (float x in new[] { -0.15f, 0f, 0.15f })
            {
                var start = o + f * (zFront + 0.7f) + r * x + Vector3.up * (floor + Mathf.Max(0.06f, above - 0.12f));
                if (Physics.Raycast(start, -f, out var hit, 0.7f + (zFront - zBack) * 0.6f, 1 << Scratch, QueryTriggerInteraction.Collide)) high++;
            }
            bool solidBelow = high >= 2 && solid * 2 >= solidTotal;
            detail["solid_high"] = $"{high}/3";
            // 5) Footrest (stools): a surface between floor+0.12 and top-0.2 under/just in front of the seat.
            if (above > 0.6f)
            {
                // Rungs and rings under the seat: a top-surface map between floor+12 cm and seat-20 cm. The foot target is
                // the centre of the front-most bar under both feet (|x| 6–16 cm each side), where a perched sitter rests them.
                var map = new System.Text.StringBuilder();
                var rows = new List<(float z, bool feet, bool centre, float h)>();
                for (float z = zBack - 0.06f; z <= zFront + 0.14f + 1e-4f; z += 0.02f)
                {
                    bool left = false, right = false, centre = false; float rowH = 0f; int n = 0;
                    var row = new System.Text.StringBuilder();
                    for (float x = -0.2f; x <= 0.2f + 1e-4f; x += 0.02f)
                    {
                        float hh = Top(o + f * z + r * x + Vector3.up * (top - 0.18f), y0, top - floor);
                        bool bar = !float.IsNaN(hh) && hh > floor + 0.12f && hh < top - 0.2f;
                        row.Append(bar ? '#' : '.');
                        if (!bar) continue;
                        float ax = Mathf.Abs(x);
                        if (ax >= 0.06f && ax <= 0.16f) { if (x < 0) left = true; else right = true; }
                        if (ax <= 0.021f) centre = true;
                        rowH += hh; n++;
                    }
                    map.Append($"{z:+0.00;-0.00} {row}\n");
                    rows.Add((z, left && right, centre, n > 0 ? rowH / n : 0f));
                }
                detail["footrest_map"] = map.ToString();
                // The front-most row under both feet; if it is a cross bar (also under the centre), its full depth.
                int bi = rows.FindLastIndex(x => x.feet);
                if (bi >= 0)
                {
                    int si = bi;
                    if (rows[bi].centre) while (si > 0 && rows[si - 1].feet && rows[si - 1].centre) si--;
                    seat.footrest = rows[bi].h;
                    seat.footrestForward = (rows[si].z + rows[bi].z) * 0.5f;
                }
            }
            // 6) Work surface in front (tables, desks) and the device on it (a laptop/keyboard is a 0.8–6 cm bump on the top).
            {
                var tops = new List<(float z, float h)>();
                for (float z = 0.05f; z <= 1.0f + 1e-4f; z += 0.02f)
                {
                    var lat = new List<float>();
                    foreach (float x in new[] { -0.12f, 0f, 0.12f })
                    {
                        float h = Top(o + f * z + r * x + Vector3.up * 1.6f, y0, 1.9f);
                        if (!float.IsNaN(h) && h > top + 0.15f && h < floor + 1.15f) lat.Add(h);
                    }
                    if (lat.Count >= 2) tops.Add((z, lat.OrderBy(v => v).ElementAt(lat.Count / 2)));
                }
                if (tops.Count >= 3)
                {
                    float desk = tops.Select(t => t.h).OrderBy(v => v).ElementAt(tops.Count / 3); // the plain top (devices sit above it)
                    seat.workTop = desk;
                    seat.workNear = tops[0].z;
                    var device = tops.Where(t => t.h > desk + 0.008f && t.h < desk + 0.06f).ToList();
                    if (device.Count > 0) { seat.workZ = device.Average(t => t.z); seat.workDeviceTop = device.Max(t => t.h); }
                    detail["work"] = $"top {desk:F3} near {seat.workNear:F2} device {(device.Count > 0 ? $"{seat.workZ:F2} top {seat.workDeviceTop:F3}" : "-")}";
                }
            }
            // 7) Kind.
            bool back = seat.backDistance > 0f && seat.backDistance - (-zBack) < 0.3f;
            if (!back) seat.backDistance = 0f;
            float depth = zFront - zBack;
            // A seat pulled up to a table (top 20–55 cm above the seat, near edge within 45 cm) is sat on upright.
            bool atTable = seat.workTop > 0f && seat.workNear < 0.45f && seat.workTop - top > 0.2f && seat.workTop - top < 0.55f;
            detail["at_table"] = atTable;
            if (a.anim == "act_type" || a.anim == "act_frantic_type") seat.kind = SeatKind.Office;
            else if (atTable) seat.kind = back ? SeatKind.Chair : SeatKind.Pouf;
            else if (above >= 0.6f && !solidBelow) seat.kind = SeatKind.Stool; // bar stools and high bar chairs: perch
            else if (solidBelow && back && above >= 0.55f) seat.kind = SeatKind.BeanBag;
            else if (solidBelow && back) seat.kind = SeatKind.Lounge;
            else if (solidBelow) seat.kind = SeatKind.Pouf;
            else if (back && a.spotId.Contains("bench")) seat.kind = SeatKind.Bench;
            else if (back) seat.kind = SeatKind.Chair;
            else if (a.spotId.Contains("bench")) seat.kind = SeatKind.Bench;
            else seat.kind = above >= 0.62f ? SeatKind.Stool : SeatKind.Pouf;
            seat.hipTarget = new Vector3(0f, top, back ? -seat.backDistance + 0.1f : (zBack + zFront) * 0.5f - 0.02f);
            detail["kind"] = seat.kind.ToString();
            detail["above_floor"] = Math.Round(above, 3);
            detail["seat_top"] = Math.Round(top, 3);
            detail["plateau_z"] = $"{zBack:F2}..{zFront:F2}";
            detail["depth"] = Math.Round(depth, 3);
            detail["back_distance"] = Math.Round(seat.backDistance, 3);
            detail["solid_below"] = $"{solid}/{solidTotal}";
            detail["floor"] = Math.Round(floor, 3);
            detail["footrest"] = seat.footrest > 0 ? Math.Round(seat.footrest, 3) : 0;
            detail["profile"] = Profile(zs, hs);
            return seat;
        }

        /// <summary>
        /// The floor where the feet go: the lowest surface over a few points ahead of the seat (a table's base plate or a
        /// rug edge under one of them is not the floor), within 0.4 m of the approach's height.
        /// </summary>
        private static float FloorAt(Vector3 o, Vector3 f, Vector3 r, float y0, float fallback)
        {
            float best = float.NaN;
            foreach (float z in new[] { 0.35f, 0.45f, 0.55f })
                foreach (float x in new[] { -0.15f, 0f, 0.15f })
                {
                    float h = Top(o + f * z + r * x + Vector3.up * 0.35f, y0, 1.2f);
                    if (float.IsNaN(h) || Mathf.Abs(h - fallback) > 0.4f) continue;
                    if (float.IsNaN(best) || h < best) best = h;
                }
            return float.IsNaN(best) ? fallback : best;
        }

        private static float Median(IEnumerable<float> v) { var l = v.Where(x => !float.IsNaN(x)).OrderBy(x => x).ToList(); return l.Count == 0 ? float.NaN : l[l.Count / 2]; }

        private static string Profile(List<float> zs, List<float> hs)
        {
            var parts = new List<string>();
            for (int i = 0; i < zs.Count; i += 3) parts.Add(float.IsNaN(hs[i]) ? "-" : hs[i].ToString("F2"));
            return string.Join(" ", parts);
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
