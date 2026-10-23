using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>Carry each moved building's authored facade panels with it, including already-baked combined meshes.</summary>
    public static class IslandV2FacadeRepair
    {
        private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.0004f;

        public static void Annotate(JArray moves, JObject dress, IslandKit kit)
        {
            foreach (JObject move in moves)
            {
                int index = kit.IndexOf((string)move["proto"]);
                if (index < 0 || kit.protos[index].kind != "building" || move["facades"] != null) continue;
                var from = IslandV2Paths.V3(move["from"]); var to = IslandV2Paths.V3(move["to"]);
                var group = ((JArray)dress["instances"]).First(g => (string)g["proto"] == (string)move["proto"]);
                var d = (JArray)group["d"]; Matrix4x4 inverse = default; bool found = false;
                for (int i = 0; i < d.Count; i += 10)
                {
                    var p = new Vector3((float)d[i], (float)d[i + 1], (float)d[i + 2]);
                    if (!Near(p, from) && !Near(p, to)) continue;
                    inverse = Matrix4x4.TRS(from, Quaternion.Euler(0, (float)d[i + 3], 0), new Vector3((float)d[i + 4], (float)d[i + 5], (float)d[i + 4])).inverse;
                    found = true; break;
                }
                if (!found) throw new InvalidDataException("Missing facade owner " + move["proto"]);
                var bounds = kit.protos[index].bounds; bounds.Expand(0.8f);
                var panels = new JArray();
                foreach (JArray row in (JArray)dress["wallDecor"])
                {
                    var p = IslandV2Paths.V3(row); var local = inverse.MultiplyPoint3x4(p);
                    if (local.x < bounds.min.x || local.x > bounds.max.x || local.z < bounds.min.z || local.z > bounds.max.z || Mathf.Abs(p.y - from.y) > 0.05f) continue;
                    var destination = (JArray)row.DeepClone(); var shift = to - from;
                    destination[0] = p.x + shift.x; destination[1] = p.y + shift.y; destination[2] = p.z + shift.z;
                    panels.Add(new JObject { ["from"] = row.DeepClone(), ["to"] = destination });
                }
                move["facades"] = panels;
            }
        }

        public static int ApplyDocument(JObject dress, JArray moves)
        {
            int count = 0;
            foreach (var move in moves) if (move["facades"] is JArray panels)
                foreach (var panel in panels)
                {
                    var from = (JArray)panel["from"]; var to = (JArray)panel["to"];
                    foreach (JArray row in (JArray)dress["wallDecor"])
                        if (Near(IslandV2Paths.V3(row), IslandV2Paths.V3(from)) && Mathf.Abs((float)row[3] - (float)from[3]) < 0.01f)
                        { row[0] = to[0].DeepClone(); row[1] = to[1].DeepClone(); row[2] = to[2].DeepClone(); count++; }
                }
            return count;
        }

        private static void AnnotateShopfronts(JArray moves, JObject dress, IslandKit kit)
        {
            foreach (JObject move in moves.ToArray())
            {
                int index = kit.IndexOf((string)move["proto"]);
                if (index < 0 || kit.protos[index].kind != "building" || move["shopSigns"] != null) continue;
                var from = IslandV2Paths.V3(move["from"]); var to = IslandV2Paths.V3(move["to"]); var delta = to - from;
                var group = ((JArray)dress["instances"]).First(g => (string)g["proto"] == (string)move["proto"]);
                var d = (JArray)group["d"]; float yaw = 0, reach = 0;
                for (int i = 0; i < d.Count; i += 10)
                {
                    var p = new Vector3((float)d[i], (float)d[i + 1], (float)d[i + 2]);
                    if (!Near(p, from) && !Near(p, to)) continue;
                    yaw = (float)d[i + 3]; reach = new Vector2(kit.protos[index].bounds.extents.x, kit.protos[index].bounds.extents.z).magnitude * (float)d[i + 4] + 2;
                    break;
                }
                bool Matches(Vector3 p, float direction, float height) => Mathf.Abs(Mathf.DeltaAngle(direction, yaw)) < 0.05f &&
                    Mathf.Abs(p.y - from.y - height) < 0.05f && new Vector2(p.x - from.x, p.z - from.z).magnitude <= reach;
                var signs = new JArray();
                foreach (JArray row in (JArray)dress["shopSigns"])
                {
                    var p = IslandV2Paths.V3(row); if (!Matches(p, (float)row[3], 4.1f)) continue;
                    var destination = (JArray)row.DeepClone(); destination[0] = p.x + delta.x; destination[1] = p.y + delta.y; destination[2] = p.z + delta.z;
                    signs.Add(new JObject { ["from"] = row.DeepClone(), ["to"] = destination });
                }
                move["shopSigns"] = signs;
                foreach (var g in (JArray)dress["instances"])
                {
                    string proto = (string)g["proto"]; if (proto != "kc_detail_awning" && proto != "kc_detail_awning_wide") continue;
                    var rows = (JArray)g["d"];
                    for (int i = 0; i < rows.Count; i += 10)
                    {
                        var p = new Vector3((float)rows[i], (float)rows[i + 1], (float)rows[i + 2]);
                        if (!Matches(p, (float)rows[i + 3], 0) || moves.Any(m => (string)m["proto"] == proto && Near(IslandV2Paths.V3(m["from"]), p))) continue;
                        var destination = p + delta;
                        moves.Add(new JObject { ["cell"] = move["cell"].DeepClone(), ["proto"] = proto, ["ownerProto"] = move["proto"].DeepClone(),
                            ["from"] = new JArray(p.x, p.y, p.z), ["to"] = new JArray(destination.x, destination.y, destination.z),
                            ["reason"] = "attached awning followed moved building", ["distance"] = delta.magnitude });
                    }
                }
            }
        }

        public static int ApplyShopSignsDocument(JObject dress, JArray moves)
        {
            int count = 0;
            foreach (var move in moves) if (move["shopSigns"] is JArray panels)
                foreach (var panel in panels) foreach (JArray row in (JArray)dress["shopSigns"])
                    if (Near(IslandV2Paths.V3(row), IslandV2Paths.V3(panel["from"])) && Mathf.Abs((float)row[3] - (float)panel["from"][3]) < 0.01f)
                    { for (int i = 0; i < 3; i++) row[i] = panel["to"][i].DeepClone(); count++; }
            return count;
        }

        private static int ExpectedVertices(JArray row)
        {
            float len = (float)row[4], height = (float)row[5]; int kind = (int)row[6];
            if (kind == 0) return Mathf.Max(1, Mathf.RoundToInt((height - 0.9f) / 3.7f)) * Mathf.Max(1, Mathf.FloorToInt((len - 1.4f) / 2.8f)) * 4;
            if (kind == 1 && Mathf.Min(len - 1.2f, height - 2.2f, 14) < 3) return 0;
            return 4;
        }

        private static Vector3[] PanelVertices(JArray row)
        {
            // Exact counterpart of IslandV2Dress.BuildW4/Panel; bounding planes can catch unrelated nearby facade vertices.
            var c = IslandV2Paths.V3(row); float length = (float)row[4], height = (float)row[5]; int kind = (int)row[6];
            var normal = Quaternion.Euler(0, (float)row[3], 0) * Vector3.forward;
            var right = Vector3.Cross(Vector3.up, -normal);
            var result = new List<Vector3>();
            void Panel(Vector3 center, float halfWidth, float halfHeight, float lift)
            {
                var q = center + normal * lift;
                result.Add(q - right * halfWidth - Vector3.up * halfHeight);
                result.Add(q - right * halfWidth + Vector3.up * halfHeight);
                result.Add(q + right * halfWidth + Vector3.up * halfHeight);
                result.Add(q + right * halfWidth - Vector3.up * halfHeight);
            }
            if (kind == 0)
            {
                int floors = Mathf.Max(1, Mathf.RoundToInt((height - 0.9f) / 3.7f)); float fh = (height - 0.9f) / floors;
                int cols = Mathf.Max(1, Mathf.FloorToInt((length - 1.4f) / 2.8f)); float span = cols * 2.8f;
                for (int f = 0; f < floors; f++) for (int k = 0; k < cols; k++)
                    Panel(c + right * (-span / 2f + 1.4f + k * 2.8f) + Vector3.up * (0.9f + f * fh + fh * 0.5f), 0.85f, 0.85f, 0.06f);
            }
            else if (kind == 1)
            {
                float side = Mathf.Min(length - 1.2f, height - 2.2f, 14);
                if (side >= 3) Panel(c + Vector3.up * (1.2f + side / 2f), side / 2f, side / 2f, 0.06f);
            }
            else
            {
                float side = Mathf.Min(length - 0.4f, 9, height * 0.9f);
                Panel(c + Vector3.up * (side / 2f), side / 2f, side / 2f, 0.07f);
            }
            return result.ToArray();
        }

        private static int[] ExactIndices(Vector3[] vertices, Transform transform, JArray row) => ExactIndices(vertices, transform, PanelVertices(row));

        private static int[] ExactIndices(Vector3[] vertices, Transform transform, Vector3[] expected)
        {
            var result = new List<int>();
            foreach (var points in expected.GroupBy(p => p))
            {
                var found = new List<int>();
                for (int i = 0; i < vertices.Length; i++)
                {
                    if ((transform.TransformPoint(vertices[i]) - points.Key).sqrMagnitude <= 0.000004f) found.Add(i);
                }
                if (found.Count != 0 && found.Count != points.Count()) throw new InvalidDataException("Unexpected facade vertex multiplicity; refusing art mutation");
                result.AddRange(found);
            }
            return result.ToArray();
        }

        private static Vector3[] ShopSignVertices(JArray row, bool bracket)
        {
            var c = IslandV2Paths.V3(row); var rot = Quaternion.Euler(0, (float)row[3], 0);
            var r = rot * Vector3.right; var o = rot * Vector3.forward; var u = Vector3.up;
            var result = new List<Vector3>();
            if (bracket)
            {
                var center = c + o * ((0.68f - 0.62f) * 0.5f) + u * (0.34f + 0.06f);
                var x = r.normalized * 0.03f; var y = u * 0.03f; var z = o.normalized * ((0.68f + 0.62f) * 0.5f);
                // MeshBuilder.Box duplicates each corner once for each of its three incident faces.
                for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
                    for (int k = 0; k < 3; k++) result.Add(center + x * sx + y * sy + z * sz);
            }
            else for (int face = -1; face <= 1; face += 2)
            {
                var center = c + r * (face * 0.025f);
                for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2)
                    result.Add(center + o * (face * sx * 0.68f) + u * (sy * 0.34f));
            }
            return result.ToArray();
        }

        private static JArray ApplyShopSignMeshes(GameObject cell, JArray moves)
        {
            var records = new JArray();
            foreach (var move in moves.Where(m => (string)m["cell"] == cell.name)) if (move["shopSigns"] is JArray signs)
                foreach (var sign in signs) foreach (bool bracket in new[] { false, true })
                {
                    string kind = bracket ? "rail" : IslandV2Dress.ShopSigns;
                    var filter = cell.transform.Find("Roads/" + kind)?.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) throw new InvalidDataException("Missing shopfront mesh " + cell.name + "/" + kind);
                    var mesh = filter.sharedMesh; var vertices = mesh.vertices;
                    var from = (JArray)sign["from"]; var to = (JArray)sign["to"];
                    var expected = ShopSignVertices(from, bracket); var indices = ExactIndices(vertices, filter.transform, expected);
                    int already = 0;
                    if (indices.Length == 0)
                    {
                        already = ExactIndices(vertices, filter.transform, ShopSignVertices(to, bracket)).Length;
                        if (already != expected.Length) throw new InvalidDataException($"Shopfront destination mismatch: {cell.name}/{kind}, expected {expected.Length}, got {already}");
                    }
                    else
                    {
                        if (indices.Length != expected.Length) throw new InvalidDataException($"Shopfront selection mismatch: {cell.name}/{kind}, expected {expected.Length}, got {indices.Length}");
                        var delta = filter.transform.InverseTransformVector(IslandV2Paths.V3(to) - IslandV2Paths.V3(from));
                        foreach (int i in indices) vertices[i] += delta;
                        mesh.vertices = vertices; mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
                    }
                    records.Add(new JObject { ["cell"] = cell.name, ["proto"] = move["proto"].DeepClone(), ["kind"] = kind,
                        ["from"] = from.DeepClone(), ["to"] = to.DeepClone(), ["expectedVertices"] = expected.Length, ["movedVertices"] = indices.Length, ["alreadyMovedVertices"] = already });
                }
            return records;
        }

        public static JArray ApplyMesh(GameObject cell, JArray moves)
        {
            var records = ApplyShopSignMeshes(cell, moves);
            var relevant = moves.Where(m => (string)m["cell"] == cell.name && m["facades"] is JArray ps && ps.Count > 0).ToArray();
            if (relevant.Length == 0) return records;
            var filter = cell.transform.Find("Roads/" + IslandV2Dress.WallDecor)?.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) throw new InvalidDataException("Missing facade mesh in " + cell.name);
            var mesh = filter.sharedMesh; var vertices = mesh.vertices; bool changed = false;
            foreach (var move in relevant) foreach (var panel in (JArray)move["facades"])
            {
                var from = (JArray)panel["from"]; var to = (JArray)panel["to"]; int expected = ExpectedVertices(from);
                var indices = ExactIndices(vertices, filter.transform, from);
                int already = 0;
                if (indices.Length == 0)
                {
                    already = ExactIndices(vertices, filter.transform, to).Length;
                    if (already != expected) throw new InvalidDataException($"Facade replay mismatch in {cell.name}: expected {expected}, destination {already}");
                }
                else
                {
                    if (indices.Length != expected) throw new InvalidDataException($"Facade selection mismatch in {cell.name}: expected {expected}, original {indices.Length}; refusing partial art mutation");
                    var delta = filter.transform.InverseTransformVector(IslandV2Paths.V3(to) - IslandV2Paths.V3(from));
                    foreach (int i in indices) vertices[i] += delta;
                    changed = true;
                }
                records.Add(new JObject { ["cell"] = cell.name, ["proto"] = move["proto"].DeepClone(), ["from"] = from.DeepClone(), ["to"] = to.DeepClone(),
                    ["expectedVertices"] = expected, ["movedVertices"] = indices.Length, ["alreadyMovedVertices"] = already });
            }
            if (changed) { mesh.vertices = vertices; mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); }
            return records;
        }

        [MenuItem("Tools/CT Life/Audit/Catch Up Moved Building Facades")]
        public static void CatchUpAndVerify()
        {
            string path = IslandV2PlacementRepair.OverridesPath;
            var journal = JObject.Parse(File.ReadAllText(path)); var moves = (JArray)journal["moves"];
            string dressPath = Path.Combine(IslandV2Paths.Out, "dress.json"); var dress = JObject.Parse(File.ReadAllText(dressPath));
            Annotate(moves, dress, AssetDatabase.LoadAssetAtPath<IslandKit>(IslandV2Kit.KitAsset));
            AnnotateShopfronts(moves, dress, AssetDatabase.LoadAssetAtPath<IslandKit>(IslandV2Kit.KitAsset));
            File.WriteAllText(path, journal.ToString()); // journal first: old/destination vertex checks make interrupted replay safe
            var records = new JArray();
            foreach (string id in moves.Where(m => (m["facades"] is JArray ps && ps.Count > 0) || (m["shopSigns"] is JArray ss && ss.Count > 0)).Select(m => (string)m["cell"]).Distinct())
            {
                var cell = PrefabUtility.LoadPrefabContents(IslandV2Paths.CellsDir + "/" + id + ".prefab");
                try
                {
                    foreach (var r in ApplyMesh(cell, moves)) records.Add(r);
                    var set = cell.GetComponentInChildren<IslandInstances>(true);
                    IslandV2PlacementRepair.ApplySavedOverrides(cell, set);
                    PrefabUtility.SaveAsPrefabAsset(cell, IslandV2Paths.CellsDir + "/" + id + ".prefab");
                }
                finally { PrefabUtility.UnloadPrefabContents(cell); }
            }
            int sourceRows = IslandV2PlacementRepair.ApplyDressOverrides(dress);
            if (sourceRows > 0) File.WriteAllText(dressPath, dress.ToString(Newtonsoft.Json.Formatting.None));
            string output = Path.Combine(IslandV2Paths.Repo, "..", "ct-life-evidence/audit-20261019/world-facade-receipt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, new JObject { ["sourceRowsUpdated"] = sourceRows, ["panels"] = records }.ToString());
            Debug.Log($"CTLIFE [world-facades] verified {records.Count} panels; {sourceRows} source rows updated. {output}");
        }
    }
}
