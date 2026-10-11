using System.Collections.Generic;
using CTLife.World.Roads;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>One stretch of an AI car's route: a lane centreline, or the connector curve through a junction.</summary>
    public sealed class RoutePiece
    {
        public Vector3[] Points;
        public float[] Cum;
        public float Length;
        /// <summary>Lane pieces: their lane. Connectors: the lane they lead into.</summary>
        public LaneRef Lane;
        public bool Connector;
        /// <summary>Lane pieces: where along the full lane centreline this piece starts (spawns start mid-lane).</summary>
        public float StartAlong;
        /// <summary>Lane pieces: the junction at their end (null: a plain join). Connectors: the junction they cross.</summary>
        public RoadIntersection Junction;
        /// <summary>Lane pieces: a roundabout entry from outside the ring (it yields to the ring).</summary>
        public bool Entry;
        /// <summary>Lane pieces: the end node joins the ring but this lane is on it (ring traffic has priority).</summary>
        public bool Ring;
        public float SpeedLimit;
        /// <summary>Connectors only: the lane piece they come from (the signal approach).</summary>
        public string FromSegment;

        public static RoutePiece Make(IReadOnlyList<Vector3> pts, int from = 0)
        {
            int n = Mathf.Max(0, pts.Count - from);
            var p = new RoutePiece { Points = new Vector3[n], Cum = new float[n] };
            for (int i = 0; i < n; i++)
            {
                p.Points[i] = pts[from + i];
                p.Cum[i] = i == 0 ? 0f : p.Cum[i - 1] + Vector3.Distance(p.Points[i - 1], p.Points[i]);
            }
            p.Length = n > 0 ? p.Cum[n - 1] : 0f;
            return p;
        }

        /// <summary>The point <paramref name="s"/> metres along (clamped), and the travel direction there.</summary>
        public Vector3 Sample(float s, out Vector3 dir)
        {
            int n = Points.Length;
            if (n == 0) { dir = Vector3.forward; return Vector3.zero; }
            if (n == 1) { dir = Vector3.forward; return Points[0]; }
            s = Mathf.Clamp(s, 0f, Length);
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (Cum[mid] <= s) lo = mid; else hi = mid; }
            var a = Points[lo]; var b = Points[hi];
            float seg = Cum[hi] - Cum[lo];
            dir = seg > 1e-5f ? (b - a) / seg : Vector3.forward;
            return seg > 1e-5f ? Vector3.Lerp(a, b, (s - Cum[lo]) / seg) : a;
        }

        /// <summary>The distance along this piece nearest to <paramref name="p"/> (x/z), the signed sideways offset (+ = right).</summary>
        public float Locate(Vector3 p, out float lateral, out float distance)
        {
            float bestD = float.MaxValue, bestS = 0f; lateral = 0f;
            for (int i = 0; i + 1 < Points.Length; i++)
            {
                var a = Points[i]; var b = Points[i + 1];
                var ab = new Vector2(b.x - a.x, b.z - a.z);
                float l2 = Mathf.Max(ab.sqrMagnitude, 1e-9f);
                var ap = new Vector2(p.x - a.x, p.z - a.z);
                float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / l2);
                var q = new Vector2(a.x, a.z) + ab * t;
                float d = (new Vector2(p.x, p.z) - q).magnitude;
                if (d < bestD)
                {
                    bestD = d; bestS = Cum[i] + t * (Cum[i + 1] - Cum[i]);
                    // right of the travel direction is positive: cross(ab, ap) < 0 in x/z (y up, left-handed Unity)
                    lateral = (ab.x * ap.y - ab.y * ap.x) < 0f ? d : -d;
                }
            }
            distance = bestD;
            return bestS;
        }
    }

    /// <summary>
    /// The route ahead of an AI car (Feel lane F3): lane centrelines from World's <see cref="RoadNetwork"/> joined by junction
    /// connectors, chosen a junction at a time (mostly straight on, sometimes a turn, never back the way it came), sampled
    /// by distance. Pure apart from the network it reads, so it is tested on its own.
    /// </summary>
    public sealed class TrafficRoute
    {
        public readonly List<RoutePiece> Pieces = new List<RoutePiece>();
        /// <summary>Metres along <c>Pieces[0]</c>.</summary>
        public float S;
        private RoadNetwork _net;
        private System.Random _rng;
        public bool DeadEnd { get; private set; }

        public RoutePiece Current => Pieces.Count > 0 ? Pieces[0] : null;

        /// <summary>Start on a lane at <paramref name="along"/> metres from its beginning. False when the lane does not exist.</summary>
        public bool StartAt(RoadNetwork net, LaneRef lane, float along, System.Random rng)
        {
            _net = net; _rng = rng; Pieces.Clear(); S = 0f; DeadEnd = false;
            var pts = net.LaneCenterline(lane.segmentId, lane.lane);
            if (pts.Length < 2) return false;
            // the first point at or after `along`, plus the exact start point
            var full = RoutePiece.Make(pts);
            var start = full.Sample(along, out _);
            int first = 0;
            while (first < full.Cum.Length && full.Cum[first] <= along) first++;
            var list = new List<Vector3>(pts.Length - first + 1) { start };
            for (int i = first; i < pts.Length; i++) list.Add(pts[i]);
            if (list.Count < 2) list.Add(pts[pts.Length - 1] + (pts[pts.Length - 1] - pts[pts.Length - 2]).normalized * 0.5f);
            var piece = RoutePiece.Make(list);
            piece.StartAlong = Mathf.Min(along, full.Length);
            Describe(piece, lane);
            Pieces.Add(piece);
            return true;
        }

        private void Describe(RoutePiece p, LaneRef lane)
        {
            p.Lane = lane;
            p.SpeedLimit = _net.SpeedLimitAt(lane.segmentId);
            var end = _net.LaneEndNode(lane.segmentId, lane.lane);
            var start = _net.LaneStartNode(lane.segmentId, lane.lane);
            p.Junction = _net.IntersectionAt(end);
            if (p.Junction != null && p.Junction.kind == IntersectionKind.Roundabout)
            {
                bool startsOnRing = _net.IntersectionAt(start) == p.Junction;
                p.Ring = startsOnRing;
                p.Entry = !startsOnRing;
            }
        }

        /// <summary>Metres of route ahead of the car.</summary>
        public float Remaining
        {
            get
            {
                float r = -S;
                foreach (var p in Pieces) r += p.Length;
                return r;
            }
        }

        /// <summary>Keep at least <paramref name="metres"/> of route ahead: append the next lane (and its connector) at each lane end.</summary>
        public void Extend(float metres)
        {
            int guard = 0;
            while (!DeadEnd && Remaining < metres && guard++ < 8)
            {
                var last = LastLanePiece();
                if (last == null) { DeadEnd = true; return; }
                var next = Choose(last.Lane);
                if (next.segmentId == null) { DeadEnd = true; return; }
                var conn = _net.Connector(last.Lane, next, 10);
                if (conn.Length >= 2 && Vector3.Distance(conn[0], conn[conn.Length - 1]) > 0.4f)
                {
                    var c = RoutePiece.Make(conn);
                    c.Connector = true; c.Lane = next; c.Junction = last.Junction; c.FromSegment = last.Lane.segmentId;
                    c.SpeedLimit = Mathf.Min(last.SpeedLimit, _net.SpeedLimitAt(next.segmentId));
                    Pieces.Add(c);
                }
                var pts = _net.LaneCenterline(next.segmentId, next.lane);
                if (pts.Length < 2) { DeadEnd = true; return; }
                var piece = RoutePiece.Make(pts);
                Describe(piece, next);
                Pieces.Add(piece);
            }
        }

        private RoutePiece LastLanePiece()
        {
            for (int i = Pieces.Count - 1; i >= 0; i--) if (!Pieces[i].Connector) return Pieces[i];
            return null;
        }

        /// <summary>The next lane: weighted toward straight on, never the same road back (unless that is all there is).</summary>
        private LaneRef Choose(LaneRef from)
        {
            var options = _net.NextSegments(from.segmentId, from.lane);
            if (options == null || options.Count == 0) return default;
            var a = _net.LaneCenterline(from.segmentId, from.lane);
            var heading = a.Length >= 2 ? Flat(a[a.Length - 1] - a[Mathf.Max(0, a.Length - 4)]) : Vector3.forward;
            float total = 0f;
            var weights = new float[options.Count];
            for (int i = 0; i < options.Count; i++)
            {
                var o = options[i];
                if (o.segmentId == from.segmentId && options.Count > 1) continue;   // no U-turns
                var b = _net.LaneCenterline(o.segmentId, o.lane);
                var h = b.Length >= 2 ? Flat(b[Mathf.Min(3, b.Length - 1)] - b[0]) : heading;
                float straight = Vector3.Dot(heading, h);
                weights[i] = 0.35f + 1.6f * Mathf.Max(0f, straight);
                total += weights[i];
            }
            if (total <= 0f) return options[0];
            double r = _rng.NextDouble() * total;
            for (int i = 0; i < options.Count; i++) { r -= weights[i]; if (r <= 0) return options[i]; }
            return options[options.Count - 1];
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward; }

        /// <summary>Move <paramref name="ds"/> metres on; finished pieces drop off the front. False at the end of a dead end.</summary>
        public bool Advance(float ds)
        {
            S += ds;
            while (Pieces.Count > 1 && S >= Pieces[0].Length) { S -= Pieces[0].Length; Pieces.RemoveAt(0); }
            if (Pieces.Count == 1 && S >= Pieces[0].Length) { S = Pieces[0].Length; return false; }
            return true;
        }

        /// <summary>The point <paramref name="ahead"/> metres along the route from the car (clamped to its end).</summary>
        public Vector3 At(float ahead, out Vector3 dir)
        {
            float s = S + ahead;
            for (int i = 0; i < Pieces.Count; i++)
            {
                var p = Pieces[i];
                if (s <= p.Length || i == Pieces.Count - 1) return p.Sample(s, out dir);
                s -= p.Length;
            }
            dir = Vector3.forward;
            return Vector3.zero;
        }

        /// <summary>Distance from the car to the start of piece <paramref name="index"/> (negative for the current one).</summary>
        public float ToStart(int index)
        {
            float d = -S;
            for (int i = 0; i < index && i < Pieces.Count; i++) d += Pieces[i].Length;
            return d;
        }

        /// <summary>Signed curvature of the route around <paramref name="ahead"/> metres on (1/m, + = turning right).</summary>
        public float Curvature(float ahead, float span = 5f)
        {
            var a = At(Mathf.Max(0f, ahead - span), out _);
            var b = At(ahead, out _);
            var c = At(ahead + span, out _);
            return TrafficRules.Curvature(a, b, c);
        }

        /// <summary>
        /// Where a point lies along the route within <paramref name="maxAhead"/> metres: the distance from the car and the
        /// sideways offset. False when it is not beside the route ahead.
        /// </summary>
        public bool Project(Vector3 p, float maxAhead, out float ahead, out float lateral)
        {
            ahead = 0f; lateral = 0f;
            float best = float.MaxValue; bool found = false;
            for (int i = 0; i < Pieces.Count; i++)
            {
                float start = ToStart(i);
                if (start > maxAhead) break;
                float s = Pieces[i].Locate(p, out float lat, out float d);
                float along = start + s;
                if (along < -1f || along > maxAhead) continue;
                if (d < best) { best = d; ahead = along; lateral = lat; found = true; }
            }
            return found;
        }
    }
}
