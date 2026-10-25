using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using CTLife.World;
using UnityEngine;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// Water meshes (sea in 256 m chunks with an outer horizon ring, the river ribbon, lakes) and the barriers around the
    /// land: coast walls (on the cliff and quay edge, or a few metres out into the shallows on beaches), walls along the
    /// causeway and round the islet, and the flight ceiling.
    /// </summary>
    public static class IslandV2Water
    {
        public sealed class Piece { public string Name; public Mesh Mesh; public Vector3 Origin; }

        /// <summary>Sea chunks (16 m grid) over the generator domain where they are not fully inland, plus the horizon ring.</summary>
        public static List<Piece> Sea(IslandHeightfield h)
        {
            var o = new List<Piece>();
            int count = IslandV2Paths.CellCount(h);
            const int Q = 16;   // quads per 256 m chunk side
            for (int cz = 0; cz < count; cz++)
                for (int cx = 0; cx < count; cx++)
                {
                    var rect = IslandV2Paths.CellRect(new Vector2Int(cx, cz), h);
                    bool wet = false;
                    for (int j = 0; j <= Q * 2 && !wet; j++)
                        for (int i = 0; i <= Q * 2; i++)
                            if (h.Sample(rect.xMin + rect.width * i / (Q * 2f), rect.yMin + rect.height * j / (Q * 2f)) < h.Sea + 0.6f) { wet = true; break; }
                    if (!wet) continue;
                    var b = new MeshBuilder();
                    var origin = new Vector3(rect.xMin, h.Sea, rect.yMin);
                    for (int j = 0; j <= Q; j++)
                        for (int i = 0; i <= Q; i++)
                            b.Add(new Vector3(rect.width * i / Q, 0f, rect.height * j / Q), Vector3.up, Vector2.zero);
                    for (int j = 0; j < Q; j++)
                        for (int i = 0; i < Q; i++)
                        {
                            int a = j * (Q + 1) + i, bb = a + 1, c = a + Q + 1, d = c + 1;
                            b.Tri(a, c, bb); b.Tri(bb, c, d);
                        }
                    o.Add(new Piece { Name = $"sea_{cx}_{cz}", Mesh = b.ToMesh($"sea_{cx}_{cz}"), Origin = origin });
                }
            // horizon ring: eight big quads out to 5 km round the domain
            var ring = new MeshBuilder();
            float d0 = h.Size * 0.5f, d1 = 5000f;
            var c0 = new Vector3(h.MinX + d0, 0f, h.MinZ + d0);
            void Q4(Vector2 a, Vector2 b2, Vector2 c, Vector2 d)
            {
                int i0 = ring.Add(new Vector3(a.x, 0, a.y), Vector3.up, Vector2.zero);
                int i1 = ring.Add(new Vector3(b2.x, 0, b2.y), Vector3.up, Vector2.zero);
                int i2 = ring.Add(new Vector3(c.x, 0, c.y), Vector3.up, Vector2.zero);
                int i3 = ring.Add(new Vector3(d.x, 0, d.y), Vector3.up, Vector2.zero);
                ring.Tri(i0, i1, i2); ring.Tri(i0, i2, i3);
            }
            // each quad listed clockwise seen from above
            Q4(new Vector2(-d1, d1), new Vector2(d1, d1), new Vector2(d1, d0), new Vector2(-d1, d0));      // north band
            Q4(new Vector2(-d1, -d0), new Vector2(d1, -d0), new Vector2(d1, -d1), new Vector2(-d1, -d1));  // south band
            Q4(new Vector2(-d1, d0), new Vector2(-d0, d0), new Vector2(-d0, -d0), new Vector2(-d1, -d0));  // west band
            Q4(new Vector2(d0, d0), new Vector2(d1, d0), new Vector2(d1, -d0), new Vector2(d0, -d0));      // east band
            o.Add(new Piece { Name = "sea_horizon", Mesh = ring.ToMesh("sea_horizon"), Origin = new Vector3(c0.x, h.Sea - 0.05f, c0.z) });
            return o;
        }

        /// <summary>The river surface: a ribbon over the channel at the local water level, flow along it in uv1 (m/s).</summary>
        public static Mesh River(JObject plan, IslandHeightfield h)
        {
            var pts = ((JArray)plan["river"]["points"]).Select(IslandV2Paths.V3).ToList();
            var hw = ((JArray)plan["river"]["halfWidth"]).Select(t => (float)t).ToList();
            var b = new MeshBuilder { HasUV1 = true };
            int n = pts.Count;
            int last = n - 1;
            for (int k = 0; k < n; k++)
                if (h.Sample(pts[k].x, pts[k].z) < h.Sea - 1.2f && pts[k].y <= h.Sea + 0.05f) { last = k; break; }   // the sea takes over
            var L = new List<Vector3>(); var R = new List<Vector3>(); var along = new List<float>(); var flow = new List<Vector2>();
            float a = 0f;
            for (int k = 0; k <= last; k++)
            {
                var p = pts[k];
                var t = pts[Mathf.Min(n - 1, k + 1)] - pts[Mathf.Max(0, k - 1)];
                var t2 = new Vector2(t.x, t.z).normalized;
                var r = new Vector3(t2.y, 0f, -t2.x);
                float w = hw[k] + 3.5f;
                float y = p.y + 0.05f;
                L.Add(new Vector3(p.x, y, p.z) - r * w); R.Add(new Vector3(p.x, y, p.z) + r * w);
                if (k > 0) a += Vector3.Distance(pts[k - 1], p);
                along.Add(a);
                float speed = Mathf.Lerp(1.1f, 0.25f, Mathf.InverseLerp(5f, 40f, hw[k]));
                flow.Add(t2 * speed);
            }
            for (int k = 0; k < L.Count; k++)
            {
                b.Add(L[k], Vector3.up, new Vector2(0, along[k]), flow[k]);
                b.Add(R[k], Vector3.up, new Vector2(1, along[k]), flow[k]);
            }
            for (int k = 0; k < L.Count - 1; k++)
            {
                int i0 = 2 * k, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                b.Tri(i0, i2, i1); b.Tri(i1, i2, i3);
            }
            return b.ToMesh("river");
        }

        public static List<Piece> Lakes(JObject plan)
        {
            var o = new List<Piece>();
            int k = 0;
            foreach (var lake in (JArray)plan["lakes"])
            {
                var c = IslandV2Paths.V2(lake["center"]);
                float r = (float)lake["radius"] + 4f, y = (float)lake["level"];
                var b = new MeshBuilder();
                int ci = b.Add(Vector3.zero, Vector3.up, Vector2.zero);
                const int S = 36;
                for (int i = 0; i <= S; i++)
                {
                    float a = -i / (float)S * Mathf.PI * 2f;
                    b.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), Vector3.up, Vector2.zero);
                }
                for (int i = 0; i < S; i++) b.Tri(ci, 1 + i, 2 + i);
                o.Add(new Piece { Name = "lake_" + k, Mesh = b.ToMesh("lake_" + k), Origin = new Vector3(c.x, y + 0.05f, c.y) });
                k++;
            }
            return o;
        }

        // ------------------------------------------------------------------ barriers

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Wall segments (centre, rotation, size) along the coast: at the edge of cliffs and quays, 5 m out on beaches.</summary>
        public static List<CellGeo.BoxCol> CoastWalls(JObject plan, IslandHeightfield h)
        {
            var coast = ((JArray)plan["coast"]).Select(IslandV2Paths.V3).ToList();
            var o = new List<CellGeo.BoxCol>();
            int n = coast.Count;
            var line = new List<Vector3>(n);
            var top = new List<float>(n);
            for (int i = 0; i < n; i++)
            {
                var p = coast[i];
                var t = coast[(i + 1) % n] - coast[(i - 1 + n) % n];
                var t2 = new Vector2(t.x, t.z).normalized;
                var seaward = new Vector3(t2.y, 0f, -t2.x);         // right of the counter-clockwise outline = the sea
                float inland = h.Sample(p.x - seaward.x * 4f, p.z - seaward.z * 4f);
                bool edge = inland > h.Sea + 1.5f;                   // cliff, rock or quay: wall right at the edge
                var q = edge ? p + seaward * 0.6f : p + seaward * 5f;
                line.Add(new Vector3(q.x, 0f, q.z));
                top.Add(edge ? Mathf.Max(inland + 2.6f, h.Sea + 5f) : h.Sea + 4.5f);
            }
            var cpath = ((JArray)plan["causeway"]["path"]).Select(IslandV2Paths.V2).ToList();
            float cgap = (float)plan["causeway"]["half_width"] + 4f;
            // R3: a walk-in landmark on a pad pushed out over the water (The Rug Room on the pier): the natural coast runs
            // through it, so its walls would stand inside the casino floor (a body caught in one was pushed out of the room on
            // the Seeker). No coast wall within the pad; the pad's own sea edges get walls instead (below).
            var pads = new List<(Vector2 c, Vector2 half, float yaw)>();
            if (plan["landmarkBuildings"] is JArray lbs)
                foreach (JObject lb in lbs)
                    if (lb["walkin"] != null && lb["fix_y"] != null)
                        pads.Add((IslandV2Paths.V2(lb["at"]), IslandV2Paths.V2(lb["size"]) * 0.5f + new Vector2(3f, 3f), (float)lb["yaw"]));
            bool InPad(Vector2 q, float margin)
            {
                foreach (var (c, half, yaw) in pads)
                {
                    var l = Quaternion.Euler(0f, -yaw, 0f) * new Vector3(q.x - c.x, 0f, q.y - c.y);
                    if (Mathf.Abs(l.x) < half.x + margin && Mathf.Abs(l.z) < half.y + margin) return true;
                }
                return false;
            }
            for (int i = 0; i < n; i++)
            {
                var a = line[i]; var b = line[(i + 1) % n];
                var d = b - a;
                if (d.sqrMagnitude < 0.25f) continue;
                var m2 = new Vector2((a.x + b.x) * 0.5f, (a.z + b.z) * 0.5f);
                if (cpath.Count > 1 && SegDist(m2, cpath[0], cpath[1]) < cgap) continue;   // the causeway leaves the beach here
                if (InPad(new Vector2(a.x, a.z), 0.5f) || InPad(new Vector2(b.x, b.z), 0.5f) || InPad(m2, 0.5f)) continue;
                float y0 = h.Sea - 6f, y1 = Mathf.Max(top[i], top[(i + 1) % n]);
                o.Add(new CellGeo.BoxCol
                {
                    Center = (a + b) * 0.5f + new Vector3(0f, (y0 + y1) * 0.5f, 0f), Rotation = Quaternion.LookRotation(d.normalized, Vector3.up),
                    Size = new Vector3(0.6f, y1 - y0, d.magnitude + 0.6f), Layer = Layers.CoastWall, Name = "coast",
                });
            }
            // the walk-in pads' sea edges: the pad's outline in 2 m pieces, a wall piece wherever the natural coast says sea
            // (exactly where the coast walls were taken out above), so the pier stays closed to the water and open to the beach
            bool Land(float x, float z)
            {
                bool inside = false;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    var pi = coast[i]; var pj = coast[j];
                    if ((pi.z > z) != (pj.z > z) && x < (pj.x - pi.x) * (z - pi.z) / (pj.z - pi.z + 1e-6f) + pi.x) inside = !inside;
                }
                return inside;
            }
            foreach (var (c, half, yaw) in pads)
            {
                var r = Quaternion.Euler(0f, yaw, 0f);
                var corners = new[] { new Vector3(-half.x, 0f, -half.y), new Vector3(half.x, 0f, -half.y), new Vector3(half.x, 0f, half.y), new Vector3(-half.x, 0f, half.y) };
                var centre = new Vector3(c.x, 0f, c.y);
                for (int k = 0; k < 4; k++)
                {
                    var a = centre + r * corners[k]; var b = centre + r * corners[(k + 1) % 4];
                    var outward = (((a + b) * 0.5f) - centre).normalized;
                    int pieces = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 2f));
                    for (int q = 0; q < pieces; q++)
                    {
                        var p0 = Vector3.Lerp(a, b, q / (float)pieces) + outward * 1f; var p1 = Vector3.Lerp(a, b, (q + 1) / (float)pieces) + outward * 1f;
                        var mid = (p0 + p1) * 0.5f;
                        if (Land(mid.x, mid.z)) continue;
                        var d = p1 - p0;
                        float y0 = h.Sea - 6f, y1 = Mathf.Max(h.Sample(mid.x, mid.z), h.Sea) + 2.8f;
                        o.Add(new CellGeo.BoxCol
                        {
                            Center = mid + new Vector3(0f, (y0 + y1) * 0.5f, 0f), Rotation = Quaternion.LookRotation(d.normalized, Vector3.up),
                            Size = new Vector3(0.6f, y1 - y0, d.magnitude + 0.3f), Layer = Layers.CoastWall, Name = "coast_pad",
                        });
                    }
                }
            }
            // islet ring and causeway sides
            var ic = IslandV2Paths.V2(plan["islet"]["center"]);
            float ir = (float)plan["islet"]["radius"] + 6f;
            const int S = 24;
            for (int i = 0; i < S; i++)
            {
                float a0 = i / (float)S * Mathf.PI * 2f, a1 = (i + 1) / (float)S * Mathf.PI * 2f;
                var p0 = new Vector3(ic.x + Mathf.Cos(a0) * ir, 0, ic.y + Mathf.Sin(a0) * ir);
                var p1 = new Vector3(ic.x + Mathf.Cos(a1) * ir, 0, ic.y + Mathf.Sin(a1) * ir);
                var d = p1 - p0;
                // leave the causeway mouth open (north side)
                var mid = (p0 + p1) * 0.5f;
                if (mid.z > ic.y + ir * 0.85f && Mathf.Abs(mid.x - ic.x) < 14f) continue;
                o.Add(new CellGeo.BoxCol { Center = mid + new Vector3(0, h.Sea - 0.5f, 0), Rotation = Quaternion.LookRotation(d.normalized, Vector3.up),
                    Size = new Vector3(0.6f, 11f, d.magnitude + 0.6f), Layer = Layers.CoastWall, Name = "islet" });
            }
            var path = ((JArray)plan["causeway"]["path"]).Select(IslandV2Paths.V2).ToList();
            float hwc = (float)plan["causeway"]["half_width"] + 1.5f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                var a = new Vector3(path[i].x, 0, path[i].y); var b = new Vector3(path[i + 1].x, 0, path[i + 1].y);
                var d = (b - a);
                var r = new Vector3(d.normalized.z, 0, -d.normalized.x);
                foreach (float s in new[] { 1f, -1f })
                    o.Add(new CellGeo.BoxCol { Center = (a + b) * 0.5f + r * (s * hwc) + new Vector3(0, h.Sea - 0.5f, 0), Rotation = Quaternion.LookRotation(d.normalized, Vector3.up),
                        Size = new Vector3(0.6f, 11f, d.magnitude + 2f), Layer = Layers.CoastWall, Name = "causeway" });
            }
            return o;
        }
    }
}
