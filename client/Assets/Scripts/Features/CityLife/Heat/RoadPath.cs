using System.Collections.Generic;
using CTLife.World.Roads;
using UnityEngine;

namespace CTLife.CityLife.Heat
{
    /// <summary>
    /// Shortest drive over the island's road graph (lane B, 2026-10-25), for the police cruisers: A* over the segment graph
    /// (nodes, segments both ways, lengths from the segment samples), returned as a polyline in the right-hand lane
    /// (segment samples offset half a lane to the right of travel). Built once per <see cref="RoadNetwork"/>.
    /// </summary>
    public sealed class RoadPath
    {
        private struct Edge { public string to; public RoadSegment seg; public bool forward; public float length; }

        private static RoadNetwork _builtFor;
        private static readonly Dictionary<string, Vector3> Nodes = new Dictionary<string, Vector3>();
        private static readonly Dictionary<string, List<Edge>> Adj = new Dictionary<string, List<Edge>>();

        private static void Build(RoadNetwork net)
        {
            if (_builtFor == net) return;
            _builtFor = net;
            Nodes.Clear(); Adj.Clear();
            foreach (var n in net.nodes) Nodes[n.id] = n.position;
            foreach (var s in net.segments)
            {
                if (s == null || s.samples == null || s.samples.Length < 2 || s.fromNode == null || s.toNode == null) continue;
                if (s.underground != null && System.Array.IndexOf(s.underground, true) >= 0) continue;   // no tunnels for the chase
                float len = 0f;
                for (int i = 1; i < s.samples.Length; i++) len += Vector3.Distance(s.samples[i - 1], s.samples[i]);
                Add(s.fromNode, new Edge { to = s.toNode, seg = s, forward = true, length = len });
                Add(s.toNode, new Edge { to = s.fromNode, seg = s, forward = false, length = len });
            }
        }

        private static void Add(string from, Edge e)
        {
            if (!Adj.TryGetValue(from, out var list)) Adj[from] = list = new List<Edge>();
            list.Add(e);
        }

        private static string NearestNode(Vector3 p, Vector3 preferDir)
        {
            string best = null; float bestScore = float.MaxValue;
            foreach (var kv in Nodes)
            {
                if (!Adj.ContainsKey(kv.Key)) continue;
                var d = kv.Value - p; d.y = 0f;
                float dist = d.magnitude;
                if (dist > 400f) continue;
                // a node ahead is cheaper to reach than one behind (no U-turn first)
                float score = dist + (preferDir.sqrMagnitude > 0.01f && Vector3.Dot(d, preferDir) < 0f ? 25f : 0f);
                if (score < bestScore) { bestScore = score; best = kv.Key; }
            }
            return best;
        }

        /// <summary>The drive from <paramref name="from"/> (facing <paramref name="forward"/>) to near <paramref name="to"/>,
        /// ending at <paramref name="to"/>; null if the graph has no route.</summary>
        public static List<Vector3> Find(RoadNetwork net, Vector3 from, Vector3 forward, Vector3 to)
        {
            if (net == null) return null;
            Build(net);
            forward.y = 0f;
            var start = NearestNode(from, forward.normalized);
            var goal = NearestNode(to, Vector3.zero);
            if (start == null || goal == null) return null;
            var path = new List<Vector3>();
            if (start == goal) { path.Add(Nodes[goal]); path.Add(to); return path; }
            // A*
            var open = new List<string> { start };
            var g = new Dictionary<string, float> { [start] = 0f };
            var came = new Dictionary<string, Edge>();
            var cameFrom = new Dictionary<string, string>();
            var closed = new HashSet<string>();
            var goalPos = Nodes[goal];
            int guard = 0;
            while (open.Count > 0 && guard++ < 4000)
            {
                int bi = 0; float bf = float.MaxValue;
                for (int i = 0; i < open.Count; i++)
                {
                    float f = g[open[i]] + Vector3.Distance(Nodes[open[i]], goalPos);
                    if (f < bf) { bf = f; bi = i; }
                }
                var cur = open[bi]; open.RemoveAt(bi);
                if (cur == goal) break;
                closed.Add(cur);
                if (!Adj.TryGetValue(cur, out var edges)) continue;
                foreach (var e in edges)
                {
                    if (closed.Contains(e.to) || !Nodes.ContainsKey(e.to)) continue;
                    float ng = g[cur] + e.length;
                    if (g.TryGetValue(e.to, out var old) && old <= ng) continue;
                    g[e.to] = ng; came[e.to] = e; cameFrom[e.to] = cur;
                    if (!open.Contains(e.to)) open.Add(e.to);
                }
            }
            if (!cameFrom.ContainsKey(goal)) return null;
            // walk back, then emit the segment samples in travel order, offset into the right-hand lane
            var chain = new List<Edge>();
            for (var n = goal; n != start; n = cameFrom[n]) chain.Add(came[n]);
            chain.Reverse();
            path.Add(Nodes[start]);
            foreach (var e in chain)
            {
                var s = e.seg.samples;
                float off = (e.seg.laneWidth > 0f ? e.seg.laneWidth : 3.3f) * 0.5f;
                int n = s.Length;
                for (int k = 1; k < n; k++)
                {
                    int i = e.forward ? k : n - 1 - k;
                    int j = e.forward ? k - 1 : n - k;
                    var dir = s[i] - s[j]; dir.y = 0f;
                    var right = dir.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, dir.normalized) : Vector3.zero;
                    path.Add(s[i] + (e.seg.oneWay ? Vector3.zero : right * off));
                }
            }
            path.Add(to);
            return path;
        }
    }
}
