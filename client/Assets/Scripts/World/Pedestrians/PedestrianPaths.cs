using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.World.Pedestrians
{
    /// <summary>
    /// World W4's walkable network (Resources/World/Island/pedestrian_paths.json, island metres, y at the sidewalk surface):
    /// sidewalk centrelines and junction corners as one graph (their ends snap together within <see cref="SnapRadius"/>:
    /// in the W4 data most meet within 1.5-2 m, a few within 3),
    /// zebra crossings linking a point on one sidewalk to a point across the road, and open areas (plazas, parks, the
    /// boardwalk) people stroll about in. Pure data; parsed once per island.
    /// </summary>
    public sealed class PedestrianPaths
    {
        public const string ResourcePath = "World/Island/pedestrian_paths";
        public const float SnapRadius = 3.0f;

        public sealed class Path
        {
            public string id, segment;
            public bool corner;
            public float width;
            public Vector3[] points;
            public float[] cum;
            public float Length => cum[cum.Length - 1];
            public int startNode, endNode;
            /// <summary>Crossings that leave from this path, by distance along it.</summary>
            public readonly List<(int crossing, float s, bool atA)> exits = new List<(int, float, bool)>();

            public Vector3 Sample(float s, out Vector3 dir)
            {
                s = Mathf.Clamp(s, 0f, Length);
                int i = Array.BinarySearch(cum, s);
                if (i < 0) i = ~i - 1;
                i = Mathf.Clamp(i, 0, points.Length - 2);
                float seg = cum[i + 1] - cum[i];
                float t = seg > 1e-5f ? (s - cum[i]) / seg : 0f;
                var d = points[i + 1] - points[i];
                d.y = 0f;
                dir = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
                return Vector3.Lerp(points[i], points[i + 1], t);
            }

            /// <summary>Distance along the path of the point nearest <paramref name="p"/> (x/z), and how far off it is.</summary>
            public float Locate(Vector3 p, out float distance)
            {
                float best = float.MaxValue, at = 0f;
                for (int i = 0; i < points.Length - 1; i++)
                {
                    var a = points[i]; var b = points[i + 1];
                    var ab = new Vector2(b.x - a.x, b.z - a.z);
                    float l2 = Mathf.Max(ab.sqrMagnitude, 1e-9f);
                    float t = Mathf.Clamp01(((p.x - a.x) * ab.x + (p.z - a.z) * ab.y) / l2);
                    float d = new Vector2(p.x - (a.x + ab.x * t), p.z - (a.z + ab.y * t)).magnitude;
                    if (d < best) { best = d; at = cum[i] + (cum[i + 1] - cum[i]) * t; }
                }
                distance = best;
                return at;
            }
        }

        public sealed class Crossing
        {
            public string id, junction, segment;
            public bool signal;
            public float width;
            public Vector3 a, b;
            public int pathA = -1, pathB = -1;
            public float sA, sB;
        }

        public sealed class Area
        {
            public string id, kind;
            public float? y;
            public Vector2[] poly;

            public bool Contains(float x, float z)
            {
                bool inside = false;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    var a = poly[i]; var b = poly[j];
                    if ((a.y > z) != (b.y > z) && x < (b.x - a.x) * (z - a.y) / (b.y - a.y + 1e-9f) + a.x) inside = !inside;
                }
                return inside;
            }

            public Rect Bounds
            {
                get
                {
                    float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                    foreach (var p in poly) { x0 = Mathf.Min(x0, p.x); z0 = Mathf.Min(z0, p.y); x1 = Mathf.Max(x1, p.x); z1 = Mathf.Max(z1, p.y); }
                    return Rect.MinMaxRect(x0, z0, x1, z1);
                }
            }
        }

        public readonly List<Path> Paths = new List<Path>();
        public readonly List<Crossing> Crossings = new List<Crossing>();
        public readonly List<Area> Areas = new List<Area>();
        /// <summary>Per node: the paths that start or end there.</summary>
        public readonly List<List<int>> NodePaths = new List<List<int>>();
        public readonly List<Vector3> Nodes = new List<Vector3>();

        public float TotalLength { get; private set; }

        private static PedestrianPaths _island;

        /// <summary>The island's network (parsed on first use; null when the asset is missing).</summary>
        public static PedestrianPaths Island
        {
            get
            {
                if (_island != null) return _island;
                var ta = Resources.Load<TextAsset>(ResourcePath);
                if (ta == null) return null;
                _island = Parse(ta.text);
                return _island;
            }
        }

        public static PedestrianPaths Parse(string json)
        {
            var root = JObject.Parse(json);
            var net = new PedestrianPaths();
            void AddPaths(JToken list, bool corner)
            {
                if (!(list is JArray arr)) return;
                foreach (var t in arr)
                {
                    var pts = Points(t["points"]);
                    if (pts.Length < 2) continue;
                    var p = new Path { id = (string)t["id"], segment = (string)t["segment"], corner = corner, width = (float?)t["width"] ?? 3f, points = pts };
                    p.cum = new float[pts.Length];
                    for (int i = 1; i < pts.Length; i++) p.cum[i] = p.cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
                    if (p.Length < 0.5f) continue;
                    net.Paths.Add(p);
                    net.TotalLength += p.Length;
                }
            }
            AddPaths(root["sidewalks"], false);
            AddPaths(root["corners"], true);
            net.Snap();
            if (root["crossings"] is JArray xs)
                foreach (var t in xs)
                {
                    var c = new Crossing
                    {
                        id = (string)t["id"], junction = (string)t["junction"], segment = (string)t["segment"],
                        signal = (bool?)t["signal"] ?? false, width = (float?)t["width"] ?? 3.2f, a = V3(t["a"]), b = V3(t["b"]),
                    };
                    net.Attach(c);
                    if (c.pathA < 0 || c.pathB < 0) continue;
                    int index = net.Crossings.Count;
                    net.Crossings.Add(c);
                    net.Paths[c.pathA].exits.Add((index, c.sA, true));
                    net.Paths[c.pathB].exits.Add((index, c.sB, false));
                }
            if (root["areas"] is JArray areas)
                foreach (var t in areas)
                {
                    if (!(t["poly"] is JArray poly) || poly.Count < 3) continue;
                    var a = new Area { id = (string)t["id"], kind = (string)t["kind"], y = t["y"] != null && t["y"].Type != JTokenType.Null ? (float?)t["y"] : null, poly = new Vector2[poly.Count] };
                    for (int i = 0; i < poly.Count; i++) a.poly[i] = new Vector2((float)poly[i][0], (float)poly[i][1]);
                    net.Areas.Add(a);
                }
            return net;
        }

        private static Vector3 V3(JToken t) => t is JArray a && a.Count >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : Vector3.zero;

        private static Vector3[] Points(JToken t)
        {
            if (!(t is JArray arr)) return Array.Empty<Vector3>();
            var list = new List<Vector3>(arr.Count);
            foreach (var p in arr) list.Add(V3(p));
            return list.ToArray();
        }

        /// <summary>Path ends within <see cref="SnapRadius"/> of each other become one node (corners meet sidewalks within ~1.5 m).</summary>
        private void Snap()
        {
            int NodeFor(Vector3 p)
            {
                int best = -1; float bestD = SnapRadius * SnapRadius;
                for (int i = 0; i < Nodes.Count; i++)
                {
                    var d = Nodes[i] - p; d.y = 0f;
                    if (d.sqrMagnitude <= bestD) { bestD = d.sqrMagnitude; best = i; }
                }
                if (best >= 0) return best;
                Nodes.Add(p);
                NodePaths.Add(new List<int>());
                return Nodes.Count - 1;
            }
            for (int i = 0; i < Paths.Count; i++)
            {
                var p = Paths[i];
                p.startNode = NodeFor(p.points[0]);
                p.endNode = NodeFor(p.points[p.points.Length - 1]);
                NodePaths[p.startNode].Add(i);
                if (p.endNode != p.startNode) NodePaths[p.endNode].Add(i);
            }
        }

        /// <summary>A crossing's ends on the nearest sidewalk (not a corner) within 3 m.</summary>
        private void Attach(Crossing c)
        {
            (c.pathA, c.sA) = Nearest(c.a, 3f);
            (c.pathB, c.sB) = Nearest(c.b, 3f);
            if (c.pathA == c.pathB) c.pathB = -1;
        }

        public (int path, float s) Nearest(Vector3 p, float within, bool sidewalksOnly = true)
        {
            int best = -1; float bestD = within, bestS = 0f;
            for (int i = 0; i < Paths.Count; i++)
            {
                var path = Paths[i];
                if (sidewalksOnly && path.corner) continue;
                float s = path.Locate(p, out float d);
                if (d < bestD) { bestD = d; best = i; bestS = s; }
            }
            return (best, bestS);
        }

        /// <summary>The other paths at <paramref name="node"/> (the same one back only at a dead end).</summary>
        public int NextPath(int node, int from, System.Random rng)
        {
            var list = NodePaths[node];
            int options = 0;
            foreach (int p in list) if (p != from) options++;
            if (options == 0) return from;
            int pick = rng.Next(options);
            foreach (int p in list)
            {
                if (p == from) continue;
                if (pick-- == 0) return p;
            }
            return from;
        }
    }
}
