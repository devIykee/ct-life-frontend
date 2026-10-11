using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World.Roads
{
    /// <summary>Contract road kinds (world-v2-contract): highway | avenue | street | mountain | service | bridge | tunnel.</summary>
    public enum RoadKind { Highway, Avenue, Street, Mountain, Service, Bridge, Tunnel }

    public enum IntersectionKind { Signal, Stop, Roundabout, Merge }

    /// <summary>Traffic light state for one approach (see <see cref="RoadNetwork.Signal"/>).</summary>
    public enum SignalState { Green, Amber, Red }

    [Serializable]
    public struct RoadNode
    {
        public string id;
        public Vector3 position;
    }

    /// <summary>A lane of a segment: lanes 0..F-1 run from -> to (right of the centreline, 0 innermost), F..F+B-1 run back.</summary>
    [Serializable]
    public struct LaneRef : IEquatable<LaneRef>
    {
        public string segmentId;
        public int lane;
        public LaneRef(string segmentId, int lane) { this.segmentId = segmentId; this.lane = lane; }
        public bool Equals(LaneRef o) => lane == o.lane && segmentId == o.segmentId;
        public override bool Equals(object o) => o is LaneRef r && Equals(r);
        public override int GetHashCode() => (segmentId?.GetHashCode() ?? 0) * 31 + lane;
        public override string ToString() => segmentId + "#" + lane;
    }

    [Serializable]
    public sealed class RoadSegment
    {
        public string id, name, route;
        public string fromNode, toNode;
        [Tooltip("The authored spline: centripetal Catmull-Rom through these points (y = road surface).")]
        public Vector3[] controlPoints;
        [Tooltip("Dense centreline (about 2 m apart) from -> to, trimmed where the junction surface begins.")]
        public Vector3[] samples;
        public int lanesForward, lanesBack;
        public float laneWidth = 3.3f;
        [Tooltip("Metres per second.")]
        public float speedLimit;
        public RoadKind kind;
        public bool oneWay;
        public float median;
        // Cross-section (what the meshes were built with): carriageway half-widths include parking and shoulders.
        public float widthRight, widthLeft, sidewalk, parking, shoulder;
        public bool kerb;
        public string surface, markings, baseKind;
        [Tooltip("Per sample: true where the road runs inside the tunnel (no sky above).")]
        public bool[] underground;

        public int LaneCount => lanesForward + lanesBack;
        public bool IsForward(int lane) => lane < lanesForward;

        /// <summary>Signed offset of a lane to the RIGHT of the from -> to centreline.</summary>
        public float LaneOffset(int lane)
        {
            if (oneWay) return (lane + 0.5f) * laneWidth - lanesForward * laneWidth * 0.5f;
            if (lane < lanesForward) return median * 0.5f + (lane + 0.5f) * laneWidth;
            return -(median * 0.5f + (lane - lanesForward + 0.5f) * laneWidth);
        }
    }

    [Serializable]
    public sealed class RoadIntersection
    {
        public string id;
        public string[] nodeIds;
        public IntersectionKind kind;
        public Vector3 center;
        [Tooltip("Roundabout radius (0 for other kinds).")]
        public float radius;
        [Tooltip("Segments that touch this intersection, in counter-clockwise order (signal phases alternate by axis).")]
        public string[] approaches;
        [Tooltip("Signal cycle in seconds (green + amber + all-red per phase, two phases).")]
        public float cycle = 36f;
        public float offset;
    }

    [Serializable]
    public sealed class LaneLinks
    {
        public string segmentId;
        public int lane;
        public LaneRef[] next;
    }

    /// <summary>A point on a lane (<see cref="RoadNetwork.NearestLanePoint"/>).</summary>
    public struct LanePoint
    {
        public string segmentId;
        public int lane;
        public Vector3 position;
        /// <summary>Unit travel direction of the lane here (x/z plane, y follows the road grade).</summary>
        public Vector3 direction;
        /// <summary>Metres along the lane centreline in its travel direction.</summary>
        public float along;
        public float distance;
        public bool valid;
    }

    /// <summary>
    /// CT Island v2 road graph (World lane; built by IslandV2Builder from tools/world/islandgen/out/roads.json).
    /// Nodes, segments (dense trimmed centrelines plus the authored control points), intersections and lane links.
    /// Runtime API per the world-v2 contract: <see cref="NearestLanePoint"/>, <see cref="LaneCenterline"/>,
    /// <see cref="NextSegments"/>, <see cref="SpeedLimitAt"/>; plus signal phases, connector curves and lookups.
    /// Right-hand traffic. Positions are island metres (y = road surface).
    /// </summary>
    [CreateAssetMenu(menuName = "CT Life/World/Road Network")]
    public sealed class RoadNetwork : ScriptableObject
    {
        public const string AssetPath = "Assets/World/Island/Roads/RoadNetwork.asset";
        public float seaLevel = -6f;
        public float laneWidth = 3.3f;
        public List<RoadNode> nodes = new List<RoadNode>();
        public List<RoadSegment> segments = new List<RoadSegment>();
        public List<RoadIntersection> intersections = new List<RoadIntersection>();
        public List<LaneLinks> lanes = new List<LaneLinks>();

        /// <summary>The network of the loaded island (set by IslandWorld; null when no island is loaded).</summary>
        public static RoadNetwork Active { get; set; }

        private Dictionary<string, int> _segIndex;
        private Dictionary<string, int> _nodeIndex;
        private Dictionary<string, RoadIntersection> _byNode;
        private Dictionary<LaneRef, LaneRef[]> _next;
        private readonly Dictionary<LaneRef, Vector3[]> _centrelines = new Dictionary<LaneRef, Vector3[]>();
        private Dictionary<long, List<(int seg, int lane, int index)>> _grid;
        private const float GridCell = 24f;
        private static readonly LaneRef[] None = Array.Empty<LaneRef>();

        private void OnEnable() => _segIndex = null;

        private void Index()
        {
            if (_segIndex != null) return;
            _segIndex = new Dictionary<string, int>(segments.Count, StringComparer.Ordinal);
            for (int i = 0; i < segments.Count; i++) _segIndex[segments[i].id] = i;
            _nodeIndex = new Dictionary<string, int>(nodes.Count, StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++) _nodeIndex[nodes[i].id] = i;
            _byNode = new Dictionary<string, RoadIntersection>(StringComparer.Ordinal);
            foreach (var x in intersections) foreach (var n in x.nodeIds) _byNode[n] = x;
            _next = new Dictionary<LaneRef, LaneRef[]>();
            foreach (var l in lanes) _next[new LaneRef(l.segmentId, l.lane)] = l.next ?? None;
        }

        public RoadSegment Segment(string segmentId)
        {
            Index();
            return segmentId != null && _segIndex.TryGetValue(segmentId, out var i) ? segments[i] : null;
        }

        public bool TryGetNode(string nodeId, out RoadNode node)
        {
            Index();
            if (nodeId != null && _nodeIndex.TryGetValue(nodeId, out var i)) { node = nodes[i]; return true; }
            node = default;
            return false;
        }

        /// <summary>The intersection a node belongs to (roundabouts group several nodes), or null for plain road joins.</summary>
        public RoadIntersection IntersectionAt(string nodeId)
        {
            Index();
            return nodeId != null && _byNode.TryGetValue(nodeId, out var x) ? x : null;
        }

        /// <summary>Speed limit of a segment in m/s (0 for an unknown id).</summary>
        public float SpeedLimitAt(string segmentId) => Segment(segmentId)?.speedLimit ?? 0f;

        /// <summary>Lanes a vehicle on this lane may continue into at the end of the segment (through the junction).</summary>
        public IReadOnlyList<LaneRef> NextSegments(string segmentId, int laneIndex)
        {
            Index();
            return _next.TryGetValue(new LaneRef(segmentId, laneIndex), out var n) ? n : None;
        }

        /// <summary>The node a lane ends at (travel direction).</summary>
        public string LaneEndNode(string segmentId, int laneIndex)
        {
            var s = Segment(segmentId);
            return s == null ? null : s.IsForward(laneIndex) ? s.toNode : s.fromNode;
        }

        public string LaneStartNode(string segmentId, int laneIndex)
        {
            var s = Segment(segmentId);
            return s == null ? null : s.IsForward(laneIndex) ? s.fromNode : s.toNode;
        }

        /// <summary>Lane centreline in its travel direction (cached; do not modify the returned array).</summary>
        public Vector3[] LaneCenterline(string segmentId, int laneIndex)
        {
            var key = new LaneRef(segmentId, laneIndex);
            if (_centrelines.TryGetValue(key, out var pts)) return pts;
            var s = Segment(segmentId);
            if (s == null || laneIndex < 0 || laneIndex >= s.LaneCount || s.samples == null || s.samples.Length < 2) return Array.Empty<Vector3>();
            pts = Offset(s.samples, s.LaneOffset(laneIndex));
            if (!s.IsForward(laneIndex)) Array.Reverse(pts);
            _centrelines[key] = pts;
            return pts;
        }

        /// <summary>The polyline offset to the right by <paramref name="offset"/> metres in the x/z plane (y kept).</summary>
        public static Vector3[] Offset(Vector3[] c, float offset)
        {
            var o = new Vector3[c.Length];
            for (int i = 0; i < c.Length; i++)
            {
                var a = c[Mathf.Max(0, i - 1)];
                var b = c[Mathf.Min(c.Length - 1, i + 1)];
                var t = new Vector2(b.x - a.x, b.z - a.z);
                if (t.sqrMagnitude < 1e-12f) t = new Vector2(0f, 1f);
                t.Normalize();
                o[i] = new Vector3(c[i].x + t.y * offset, c[i].y, c[i].z - t.x * offset);
            }
            return o;
        }

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        private void BuildGrid()
        {
            if (_grid != null) return;
            Index();
            _grid = new Dictionary<long, List<(int, int, int)>>();
            for (int si = 0; si < segments.Count; si++)
            {
                var s = segments[si];
                for (int lane = 0; lane < s.LaneCount; lane++)
                {
                    var pts = LaneCenterline(s.id, lane);
                    for (int i = 0; i < pts.Length; i++)
                    {
                        var k = Key(Mathf.FloorToInt(pts[i].x / GridCell), Mathf.FloorToInt(pts[i].z / GridCell));
                        if (!_grid.TryGetValue(k, out var list)) _grid[k] = list = new List<(int, int, int)>();
                        list.Add((si, lane, i));
                    }
                }
            }
        }

        /// <summary>
        /// Nearest point on any lane centreline to <paramref name="position"/> (x/z distance, then height), searching up to
        /// <paramref name="maxDistance"/> metres. <c>valid</c> is false when nothing is in range.
        /// </summary>
        public LanePoint NearestLanePoint(Vector3 position, float maxDistance = 60f, Func<RoadSegment, bool> filter = null)
        {
            BuildGrid();
            var best = new LanePoint { distance = float.MaxValue };
            int r = Mathf.CeilToInt(maxDistance / GridCell);
            int cx = Mathf.FloorToInt(position.x / GridCell), cz = Mathf.FloorToInt(position.z / GridCell);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (!_grid.TryGetValue(Key(cx + dx, cz + dz), out var list)) continue;
                    foreach (var (si, lane, i) in list)
                    {
                        var s = segments[si];
                        if (filter != null && !filter(s)) continue;
                        var pts = LaneCenterline(s.id, lane);
                        // test the two polyline pieces around sample i
                        for (int j = Mathf.Max(0, i - 1); j <= Mathf.Min(pts.Length - 2, i); j++)
                        {
                            var a = pts[j];
                            var b = pts[j + 1];
                            var ab = new Vector2(b.x - a.x, b.z - a.z);
                            float l2 = Mathf.Max(ab.sqrMagnitude, 1e-9f);
                            float t = Mathf.Clamp01(((position.x - a.x) * ab.x + (position.z - a.z) * ab.y) / l2);
                            var q = Vector3.Lerp(a, b, t);
                            float d = new Vector2(position.x - q.x, position.z - q.z).magnitude + Mathf.Abs(position.y - q.y) * 0.25f;
                            if (d >= best.distance || d > maxDistance) continue;
                            float along = 0f;
                            for (int k = 0; k < j; k++) along += Vector3.Distance(pts[k], pts[k + 1]);
                            along += Vector3.Distance(a, q);
                            var dir = (b - a);
                            best = new LanePoint
                            {
                                segmentId = s.id, lane = lane, position = q, direction = dir.sqrMagnitude > 1e-9f ? dir.normalized : Vector3.forward,
                                along = along, distance = d, valid = true,
                            };
                        }
                    }
                }
            if (!best.valid) best.distance = float.PositiveInfinity;
            return best;
        }

        /// <summary>
        /// Path through a junction from the end of one lane to the start of the next (cubic Bezier, <paramref name="steps"/>
        /// points including both ends). Empty when the lanes do not exist.
        /// </summary>
        public Vector3[] Connector(LaneRef from, LaneRef to, int steps = 8)
        {
            var a = LaneCenterline(from.segmentId, from.lane);
            var b = LaneCenterline(to.segmentId, to.lane);
            if (a.Length < 2 || b.Length < 2) return Array.Empty<Vector3>();
            var p0 = a[a.Length - 1];
            var d0 = (p0 - a[a.Length - 2]).normalized;
            var p3 = b[0];
            var d3 = (b[1] - p3).normalized;
            float k = Vector3.Distance(p0, p3) * 0.42f;
            var p1 = p0 + d0 * k;
            var p2 = p3 - d3 * k;
            var o = new Vector3[Mathf.Max(2, steps)];
            for (int i = 0; i < o.Length; i++)
            {
                float t = i / (float)(o.Length - 1), u = 1f - t;
                o[i] = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
            }
            return o;
        }

        /// <summary>
        /// Traffic light for an approach at a signalled intersection at <paramref name="timeSeconds"/> (any shared clock, e.g.
        /// server time or Time.time). Two phases alternate between approaches on the two road axes: 15 s green, 3 s amber,
        /// 1 s all-red each. Non-signal intersections are always green (stop/merge rules are the driver's job).
        /// </summary>
        public SignalState Signal(string intersectionId, string approachSegmentId, double timeSeconds)
        {
            Index();
            RoadIntersection x = null;
            foreach (var i in intersections) if (i.id == intersectionId) { x = i; break; }
            if (x == null || x.kind != IntersectionKind.Signal || x.approaches == null || x.approaches.Length == 0) return SignalState.Green;
            int idx = Array.IndexOf(x.approaches, approachSegmentId);
            if (idx < 0) return SignalState.Green;
            int phase = PhaseOf(x, idx);
            double t = (timeSeconds + x.offset) % x.cycle;
            if (t < 0) t += x.cycle;
            double half = x.cycle * 0.5;
            double local = phase == 0 ? t : (t + half) % x.cycle;   // phase 1 runs half a cycle later
            if (local < half - 4.0) return SignalState.Green;
            if (local < half - 1.0) return SignalState.Amber;
            return SignalState.Red;
        }

        /// <summary>Phase of an approach: approaches whose direction is near the first approach's axis share phase 0.</summary>
        public int PhaseOf(RoadIntersection x, int approachIndex)
        {
            Vector2 Dir(int i)
            {
                var s = Segment(x.approaches[i]);
                if (s == null || s.samples == null || s.samples.Length < 2) return Vector2.up;
                bool atFrom = Array.IndexOf(x.nodeIds, s.fromNode) >= 0;
                var a = atFrom ? s.samples[0] : s.samples[s.samples.Length - 1];
                var b = atFrom ? s.samples[Mathf.Min(3, s.samples.Length - 1)] : s.samples[Mathf.Max(0, s.samples.Length - 4)];
                return new Vector2(b.x - a.x, b.z - a.z).normalized;
            }
            var d0 = Dir(0);
            var d = Dir(approachIndex);
            return Mathf.Abs(Vector2.Dot(d0, d)) > 0.7071f ? 0 : 1;
        }
    }
}
