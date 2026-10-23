using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using CTLife.World.Roads;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>Small, persisted corrections to existing instances; never regenerates artwork or the island.</summary>
    public static class IslandV2PlacementRepair
    {
        public static string OverridesPath => Path.Combine(IslandV2Paths.Repo, "tools/world/islandgen/placement_repairs.json");
        private static JArray ReadOverrides() => File.Exists(OverridesPath) ? (JArray)JObject.Parse(File.ReadAllText(OverridesPath))["moves"] : new JArray();
        private static JArray V(Vector3 p) => new JArray(p.x, p.y, p.z);
        private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.0004f;

        /// <summary>Exact original-coordinate matching makes replay idempotent. Updated generator outputs already at 'to' stay there.</summary>
        public static Vector3 ApplyPosition(string proto, Vector3 position, JArray moves)
        {
            foreach (var m in moves)
                if ((string)m["proto"] == proto && Near(position, IslandV2Paths.V3(m["from"]))) return IslandV2Paths.V3(m["to"]);
            return position;
        }

        private sealed class Row
        {
            public int Offset, Proto;
            public Vector3 Position;
        }

        private static List<Row> Rows(byte[] bytes)
        {
            var rows = new List<Row>();
            using (var s = new MemoryStream(bytes)) using (var r = new BinaryReader(s))
            {
                if (r.ReadInt32() != IslandInstances.Magic) throw new InvalidDataException("Bad island instances");
                int groups = r.ReadInt32();
                for (int g = 0; g < groups; g++)
                {
                    int proto = r.ReadInt32(), n = r.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        var row = new Row { Offset = (int)s.Position, Proto = proto, Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) };
                        s.Position += 16; rows.Add(row);
                    }
                }
            }
            return rows;
        }

        private static void SetPosition(byte[] bytes, Row row, Vector3 position)
        {
            using (var s = new MemoryStream(bytes)) using (var w = new BinaryWriter(s))
            { s.Position = row.Offset; w.Write(position.x); w.Write(position.y); w.Write(position.z); }
            row.Position = position;
        }

        private static void SaveData(IslandInstances set, byte[] bytes)
        {
            string path = AssetDatabase.GetAssetPath(set.data);
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            set.data = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }

        public static void ApplySavedOverrides(GameObject cell, IslandInstances set)
        {
            var moves = ReadOverrides(); if (moves.Count == 0) return;
            byte[] bytes = set.data.bytes; bool changed = false;
            foreach (var row in Rows(bytes))
            {
                string proto = set.kit.protos[row.Proto].id;
                var to = ApplyPosition(proto, row.Position, moves);
                if (Near(to, row.Position)) continue;
                foreach (var car in cell.GetComponentsInChildren<ParkedCar>(true))
                    if (car.model == proto && Near(car.transform.position + Vector3.up * 0.02f, row.Position))
                        car.transform.position += to - row.Position;
                SetPosition(bytes, row, to); changed = true;
            }
            if (changed) SaveData(set, bytes);
            // A reviewed move whose building the dressing no longer places (world v3 re-dressed Timeline Downtown: the old
            // building, its wall decor and shop signs are gone) has nothing left to carry; replay only the live ones.
            var rows = Rows(bytes).ToList();
            var live = new JArray(moves.Where(m => (string)m["cell"] != cell.name || rows.Any(r => set.kit.protos[r.Proto].id == (string)m["proto"] &&
                (Near(r.Position, IslandV2Paths.V3(m["to"])) || Near(r.Position, IslandV2Paths.V3(m["from"]))))));
            IslandV2FacadeRepair.ApplyMesh(cell, live);
        }

        [MenuItem("Tools/CT Life/Audit/Repair World Placements And Refresh Safety")]
        public static void RepairAndRefresh()
        {
            if (File.Exists(OverridesPath)) IslandV2FacadeRepair.CatchUpAndVerify();
            var kit = AssetDatabase.LoadAssetAtPath<IslandKit>(IslandV2Kit.KitAsset);
            if (kit == null) throw new InvalidOperationException("Missing island kit");
            IslandV2Collision.BakeKit(kit);
            var world = AssetDatabase.LoadAssetAtPath<GameObject>(IslandV2Paths.BasePrefab).GetComponent<IslandWorld>();
            var moves = ReadOverrides(); var applied = new JArray(); var unresolved = new JArray();
            foreach (string path in Directory.GetFiles(IslandV2Paths.CellsDir, "*.prefab").OrderBy(p => p))
            {
                var cell = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    IslandV2Collision.ReplaceCellColliders(cell);
                    var proxies = IslandV2Collision.AddAuditProxies(cell);
                    try { RepairCell(cell, world, moves, applied, unresolved); }
                    finally { foreach (var proxy in proxies) UnityEngine.Object.DestroyImmediate(proxy); }
                    // Save the journal before the asset: a repeated run replays an interrupted cell exactly once.
                    File.WriteAllText(OverridesPath, new JObject { ["version"] = 1, ["matching"] = "proto plus original xyz within 2cm; exact destination; no cumulative delta", ["moves"] = moves }.ToString());
                    PrefabUtility.SaveAsPrefabAsset(cell, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(cell); }
            }
            int sourceUpdates = UpdateDressSource(moves);
            IslandV2FacadeRepair.CatchUpAndVerify();
            moves = ReadOverrides();
            var receipt = new JObject { ["kind"] = "world-placement-repair", ["newMoves"] = applied, ["unresolved"] = unresolved,
                ["savedMoves"] = moves.Count, ["dressSourceUpdates"] = sourceUpdates,
                ["scope"] = "Same-cell minimum-displacement search. Original yaw/scale/tint/art preserved. Terrain, dry-water, road-lane, junction, cell-border and collider checks; no intentional rail/shell joins or rock clusters moved." };
            string output = Path.Combine(IslandV2Paths.Repo, "..", "ct-life-evidence/audit-20261019/world-placement-receipt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllText(output, receipt.ToString());
            IslandV2Collision.RefreshWorldSafety();
            Debug.Log($"CTLIFE [world-placement] {applied.Count} relocations; {unresolved.Count} unresolved; {moves.Count} persisted source overrides. {output}");
            if (unresolved.Count > 0) Debug.LogWarning("CTLIFE [world-placement] unresolved placements require review; not a clearance pass");
        }

        private static bool StreetProp(string id) => id == "pp_scooter" || id == "pp_moped" || id == "pp_bicycle" || id == "planter_round" || id == "kyc_trash_b";
        private static bool Penetrates(Collider a, Collider b, float threshold)
        {
            return a != b && a.gameObject != b.gameObject && a.enabled && b.enabled && !a.isTrigger && !b.isTrigger &&
                a.bounds.Intersects(b.bounds) && Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                    b, b.transform.position, b.transform.rotation, out _, out float d) && d > threshold;
        }

        private static void RepairCell(GameObject cell, IslandWorld world, JArray moves, JArray applied, JArray unresolved)
        {
            var set = cell.GetComponentInChildren<IslandInstances>(true); if (set == null) return;
            byte[] bytes = set.data.bytes; var rows = Rows(bytes); bool changed = false;
            Physics.SyncTransforms();
            var colliders = cell.GetComponentsInChildren<Collider>(true).Where(c => IslandV2Collision.IsAuditShape(c) && c.enabled && !c.isTrigger).ToArray();
            var movable = colliders.Select(c => c.transform).Distinct().Where(t =>
                (t.parent != null && t.parent.name == "AuditColliders" && (StreetProp(t.name) || t.gameObject.layer == Layers.Building || t.name.Contains("rock"))) || t.GetComponent<ParkedCar>() != null)
                .OrderBy(t => t.gameObject.layer == Layers.Building ? 0 : t.GetComponent<ParkedCar>() != null ? 1 : 2).ToArray();
            var h = world.LoadHeightfield();
            var rect = world.cells.First(c => c.id == cell.name).rect;
            foreach (var t in movable)
            {
                var own = t.GetComponents<Collider>().Where(IslandV2Collision.IsAuditShape).ToArray();
                bool building = t.gameObject.layer == Layers.Building, rock = t.name.Contains("rock");
                var car = t.GetComponent<ParkedCar>();
                var conflicts = colliders.Where(c => (c.gameObject.layer == Layers.Building || c.gameObject.layer == Layers.Prop) && own.Any(o => Penetrates(o, c, 0.15f))).ToArray();
                bool roundabout = building && conflicts.Any(c => c.name == "roundabout");
                bool selected = building ? roundabout || conflicts.Any(c => c.gameObject.layer == Layers.Building) : rock ? conflicts.Any(c => c.gameObject.layer == Layers.Building) :
                    car != null ? conflicts.Any(c => c.name == "wall_col") : StreetProp(t.name) && conflicts.Length > 0;
                if (!selected) continue;
                string proto = car != null ? car.model : t.name;
                var from = t.position; var instanceFrom = from + (car != null ? Vector3.up * 0.02f : Vector3.zero);
                var row = rows.SingleOrDefault(r => set.kit.protos[r.Proto].id == proto && Near(r.Position, instanceFrom));
                if (row == null) throw new InvalidDataException("Cannot identify exact source row for " + cell.name + "/" + t.name);
                string reason = building ? (roundabout ? "building occupied roundabout island" : "neighboring building facades overlapped") : rock ? "rock crossed building facade" : car != null ? "parked car crossed venue wall" : "street prop crossed solid furniture/vehicle/post";
                var shape = own[0].bounds; foreach (var c in own.Skip(1)) shape.Encapsulate(c.bounds);
                var near = colliders.Where(c => c.transform != t && (c.gameObject.layer == Layers.Building || c.gameObject.layer == Layers.Prop || c.gameObject.layer == Layers.Vegetation)).ToArray();
                float maximum = roundabout ? 80 : rock ? 14 : 8, step = roundabout ? 2 : 0.5f;
                float oldFloor = Floor(from, colliders, h, building || rock);
                bool found = false;
                for (float radius = step; radius <= maximum && !found; radius += step)
                {
                    int directions = Mathf.Max(16, Mathf.CeilToInt(radius * Mathf.PI * 2 / step));
                    for (int angle = 0; angle < directions; angle++)
                    {
                        float a = angle * Mathf.PI * 2 / directions;
                        var candidate = from + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                        candidate.y = from.y + Floor(candidate, colliders, h, building || rock) - oldFloor;
                        var bounds = new Bounds(shape.center + candidate - from, shape.size);
                        if (!SiteClear(bounds, world, rect, building, rock, car != null)) continue;
                        t.position = candidate; Physics.SyncTransforms();
                        if (own.Any(o => near.Any(c => Penetrates(o, c, 0.02f)))) continue;
                        found = true; break;
                    }
                }
                if (!found)
                {
                    t.position = from; Physics.SyncTransforms();
                    unresolved.Add(new JObject { ["cell"] = cell.name, ["proto"] = proto, ["position"] = V(from), ["reason"] = reason });
                    continue;
                }
                var instanceTo = row.Position + t.position - from;
                var move = new JObject { ["cell"] = cell.name, ["proto"] = proto, ["from"] = V(row.Position), ["to"] = V(instanceTo),
                    ["reason"] = reason, ["distance"] = Vector3.Distance(from, t.position), ["previousConflicts"] = new JArray(conflicts.Select(c => c.name)) };
                moves.Add(move); applied.Add(move.DeepClone()); SetPosition(bytes, row, instanceTo); changed = true;
            }
            if (changed)
            {
                var dress = JObject.Parse(File.ReadAllText(Path.Combine(IslandV2Paths.Out, "dress.json")));
                IslandV2FacadeRepair.Annotate(moves, dress, set.kit);
                File.WriteAllText(OverridesPath, new JObject { ["version"] = 1, ["moves"] = moves }.ToString());
                IslandV2FacadeRepair.ApplyMesh(cell, moves);
                SaveData(set, bytes);
            }
        }

        private static float Floor(Vector3 p, Collider[] all, IslandHeightfield h, bool groundOnly)
        {
            float floor = h.Sample(p.x, p.z); if (groundOnly) return floor;
            var ray = new Ray(new Vector3(p.x, floor + 4, p.z), Vector3.down);
            foreach (var c in all)
                if (c.gameObject.layer == Layers.Road && c.Raycast(ray, out var hit, 5) && hit.point.y > floor) floor = hit.point.y;
            return floor;
        }

        private static bool SiteClear(Bounds b, IslandWorld world, Rect rect, bool building, bool rock, bool car)
        {
            // Keep the whole footprint inside its current streaming cell, with a seam margin.
            if (b.min.x < rect.xMin + 2 || b.max.x > rect.xMax - 2 || b.min.z < rect.yMin + 2 || b.max.z > rect.yMax - 2) return false;
            var h = world.LoadHeightfield(); float low = float.PositiveInfinity, high = float.NegativeInfinity;
            float pitch = building ? 2 : 1;
            int nx = Mathf.Max(1, Mathf.CeilToInt(b.size.x / pitch)), nz = Mathf.Max(1, Mathf.CeilToInt(b.size.z / pitch));
            for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++)
            {
                float px = Mathf.Lerp(b.min.x, b.max.x, x / (float)nx), pz = Mathf.Lerp(b.min.z, b.max.z, z / (float)nz);
                float y = h.Sample(px, pz); low = Mathf.Min(low, y); high = Mathf.Max(high, y);
                if (!float.IsNaN(world.WaterSurfaceAt(px, pz)) || h.HoleAt(Mathf.RoundToInt((px - h.MinX) / h.Cell), Mathf.RoundToInt((pz - h.MinZ) / h.Cell))) return false;
                var lane = world.roads.NearestLanePoint(new Vector3(px, y, pz), 10);
                if (lane.valid && Mathf.Abs(lane.position.y - y) < 3)
                {
                    float horizontal = new Vector2(px - lane.position.x, pz - lane.position.z).magnitude;
                    float clearance = world.roads.Segment(lane.segmentId).laneWidth * 0.5f + (building ? 1 : 0.25f);
                    if (horizontal < clearance) return false;
                }
                foreach (var junction in world.roads.intersections)
                    if (junction.kind == IntersectionKind.Roundabout && Vector2.Distance(new Vector2(px, pz), new Vector2(junction.center.x, junction.center.z)) < junction.radius + 1) return false;
            }
            return high - low <= (building ? 1.2f : rock ? 8 : car ? 0.7f : 0.55f);
        }

        private static int UpdateDressSource(JArray moves)
        {
            string path = Path.Combine(IslandV2Paths.Out, "dress.json"); var doc = JObject.Parse(File.ReadAllText(path));
            int count = ApplyDressOverrides(doc, moves);
            if (count > 0) File.WriteAllText(path, doc.ToString(Newtonsoft.Json.Formatting.None));
            return count;
        }

        public static int ApplyDressOverrides(JObject doc) => ApplyDressOverrides(doc, ReadOverrides());

        private static int ApplyDressOverrides(JObject doc, JArray moves)
        {
            int count = 0;
            foreach (var g in (JArray)doc["instances"])
            {
                var d = (JArray)g["d"];
                for (int i = 0; i < d.Count; i += 10)
                {
                    var from = new Vector3((float)d[i], (float)d[i + 1], (float)d[i + 2]); var to = ApplyPosition((string)g["proto"], from, moves);
                    if (Near(from, to)) continue;
                    foreach (JArray box in (JArray)doc["buildingBoxes"])
                        if (Mathf.Abs((float)box[0] - from.x) < 0.02f && Mathf.Abs((float)box[2] - from.z) < 0.02f)
                        { box[0] = (float)box[0] + to.x - from.x; box[1] = (float)box[1] + to.y - from.y; box[2] = (float)box[2] + to.z - from.z; }
                    d[i] = to.x; d[i + 1] = to.y; d[i + 2] = to.z; count++;
                }
            }
            return count + IslandV2FacadeRepair.ApplyDocument(doc, moves) + IslandV2FacadeRepair.ApplyShopSignsDocument(doc, moves);
        }
    }
}
