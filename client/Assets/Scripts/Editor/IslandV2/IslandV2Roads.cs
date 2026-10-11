using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.World.Roads;
using Newtonsoft.Json.Linq;
using CTLife.World;
using UnityEngine;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// Roads of CT Island v2 from tools/world/islandgen/out/roads.json: the RoadNetwork asset (contract data + runtime API)
    /// and the meshes. Segments are flat-crowned ribbons whose markings the road shader draws from vertex data. Kerbed
    /// roads get kerbs and paver sidewalks, others get shoulders and skirts. Junctions are filled fans with corner sidewalks,
    /// zebra crossings and stop lines. Bridges get decks, parapets and piers; tunnels get walls, an arched roof and portal
    /// frames; drops get guard rails. Everything is cut into 64 m pieces and filed into its 256 m streaming cell.
    /// </summary>
    public static class IslandV2Roads
    {
        public const float Kerb = 0.15f, KerbTop = 0.25f, DeckDepth = 1.4f, ParapetHeight = 0.95f, ParapetWidth = 0.35f;
        public const float TunnelWall = 4.2f, TunnelApex = 6.6f;

        public sealed class Seg
        {
            public string Id, Name, Route, From, To, Kind, BaseKind, Surface, Markings, Ring;
            public int F, B;
            public float LaneWidth, Speed, Median, WR, WL, Sidewalk, Parking, Shoulder;
            public bool KerbOn, OneWay;
            public Vector3[] S, Ctrl;
            public bool[] Underground;
            public float[] Along;
            public Vector3[] Right;
            public bool Bridge => Kind == "bridge";
            public bool Tunnel => Kind == "tunnel";

            public float MarkKind => Markings == "double_yellow" ? 1f : Markings == "dashed" ? 0.5f : 0f;
            public Color Attr(bool lines) => new Color(OneWay ? 1f : 0f, MarkKind, Parking / 4f, lines ? 1f : 0f);
        }

        public static List<Seg> Parse(JObject roads)
        {
            var o = new List<Seg>();
            foreach (var j in (JArray)roads["segments"])
            {
                var s = new Seg
                {
                    Id = (string)j["id"], Name = (string)j["name"], Route = (string)j["route"], From = (string)j["from"], To = (string)j["to"],
                    Kind = (string)j["kind"], BaseKind = (string)j["base"], Surface = (string)j["surface"], Markings = (string)j["markings"],
                    Ring = (string)j["ring"], F = (int)j["lanesForward"], B = (int)j["lanesBack"], LaneWidth = (float)j["laneWidth"],
                    Speed = (float)j["speedLimit"], Median = (float)j["median"], WR = (float)j["widthRight"], WL = (float)j["widthLeft"],
                    Sidewalk = (float)j["sidewalk"], Parking = (float)j["parking"], Shoulder = (float)j["shoulder"], KerbOn = (bool)j["kerb"],
                    OneWay = (bool)j["oneWay"],
                    S = ((JArray)j["samples"]).Select(IslandV2Paths.V3).ToArray(),
                    Ctrl = ((JArray)j["controlPoints"]).Select(IslandV2Paths.V3).ToArray(),
                    Underground = j["underground"] is JArray u ? u.Select(x => (bool)x).ToArray() : null,
                };
                int n = s.S.Length;
                s.Along = new float[n];
                s.Right = new Vector3[n];
                for (int k = 1; k < n; k++) s.Along[k] = s.Along[k - 1] + Vector3.Distance(s.S[k - 1], s.S[k]);
                for (int k = 0; k < n; k++)
                {
                    var a = s.S[Mathf.Max(0, k - 1)];
                    var b = s.S[Mathf.Min(n - 1, k + 1)];
                    var t = new Vector2(b.x - a.x, b.z - a.z);
                    if (t.sqrMagnitude < 1e-9f) t = Vector2.up;
                    t.Normalize();
                    s.Right[k] = new Vector3(t.y, 0f, -t.x);
                }
                o.Add(s);
            }
            return o;
        }

        // ------------------------------------------------------------------ network asset

        public static RoadKind KindOf(string k)
        {
            switch (k)
            {
                case "highway": return RoadKind.Highway;
                case "avenue": return RoadKind.Avenue;
                case "mountain": return RoadKind.Mountain;
                case "bridge": return RoadKind.Bridge;
                case "tunnel": return RoadKind.Tunnel;
                case "service": case "dirt": return RoadKind.Service;
                default: return RoadKind.Street;      // street, suburban
            }
        }

        public static void FillNetwork(RoadNetwork net, JObject roads, List<Seg> segs)
        {
            net.seaLevel = (float)roads["seaLevel"];
            net.laneWidth = (float)roads["laneWidth"];
            net.nodes = ((JArray)roads["nodes"]).Select(n => new RoadNode { id = (string)n["id"], position = IslandV2Paths.V3(n["p"]) }).ToList();
            net.segments = segs.Select(s => new RoadSegment
            {
                id = s.Id, name = s.Name, route = s.Route, fromNode = s.From, toNode = s.To, controlPoints = s.Ctrl, samples = s.S,
                lanesForward = s.F, lanesBack = s.B, laneWidth = s.LaneWidth, speedLimit = s.Speed, kind = KindOf(s.Kind), oneWay = s.OneWay,
                median = s.Median, widthRight = s.WR, widthLeft = s.WL, sidewalk = s.Sidewalk, parking = s.Parking, shoulder = s.Shoulder,
                kerb = s.KerbOn, surface = s.Surface, markings = s.Markings, baseKind = s.BaseKind, underground = s.Underground,
            }).ToList();
            net.intersections = new List<RoadIntersection>();
            int k = 0;
            foreach (var x in (JArray)roads["intersections"])
            {
                string kind = (string)x["kind"];
                var approaches = new List<string>();
                if (x["approaches"] is JArray ap) foreach (var a in ap) approaches.Add((string)a["segment"]);
                if (x["parts"] is JArray parts) foreach (var p in parts) foreach (var a in (JArray)p["approaches"]) approaches.Add((string)a["segment"]);
                net.intersections.Add(new RoadIntersection
                {
                    id = (string)x["id"], nodeIds = ((JArray)x["nodeIds"]).Select(t => (string)t).ToArray(),
                    kind = kind == "signal" ? IntersectionKind.Signal : kind == "roundabout" ? IntersectionKind.Roundabout : kind == "merge" ? IntersectionKind.Merge : IntersectionKind.Stop,
                    center = IslandV2Paths.V3(x["center"]), radius = x["radius"] != null ? (float)x["radius"] : 0f,
                    approaches = approaches.Distinct().ToArray(), cycle = 38f, offset = (k++ * 7.3f) % 38f,
                });
            }
            net.lanes = ((JArray)roads["lanes"]).Select(l => new LaneLinks
            {
                segmentId = (string)l["segment"], lane = (int)l["lane"],
                next = ((JArray)l["next"]).Select(n => new LaneRef((string)n["segment"], (int)n["lane"])).ToArray(),
            }).ToList();
        }

        // ------------------------------------------------------------------ geometry

        public sealed class Context
        {
            public IslandHeightfield H;
            public CellSet Cells;
            public List<Vector3> Doors = new List<Vector3>();
            public int Pieces, Bays, RailMetres, Crosswalks;
            public float WallMetres;
        }

        public static void Build(List<Seg> segs, JObject roads, Context ctx)
        {
            foreach (var s in segs) Segment(s, ctx);
            foreach (var x in (JArray)roads["intersections"])
            {
                if (x["parts"] is JArray parts)
                {
                    foreach (var p in parts) Junction((JArray)p["polygon"], (JArray)p["fillets"], (JArray)p["approaches"], "roundabout", segs, ctx);
                    RoundaboutIsland(x, segs, ctx);
                }
                else Junction((JArray)x["polygon"], (JArray)x["fillets"], (JArray)x["approaches"], (string)x["kind"], segs, ctx);
            }
            if (roads["walls"] is JArray walls) foreach (var w in walls) Wall(w, ctx);
        }

        /// <summary>
        /// A retaining wall along a road edge (islandgen terrain.retaining_walls): a stone face from its bottom to its top
        /// height, 0.5 m thick with a cap. Cut walls hold the slope above the road; fill walls hold the road above the
        /// falling ground and stand 0.9 m above it as a parapet (they collide, so cars stay on the road).
        /// </summary>
        private static void Wall(JToken w, Context ctx)
        {
            var pts = ((JArray)w["points"]).Select(p => (JArray)p).ToArray();
            if (pts.Length < 2) return;
            bool right = (string)w["side"] == "right";
            bool fill = (string)w["kind"] == "fill";
            const float thick = 0.5f;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                var p0 = new Vector3((float)a[0], 0f, (float)a[2]); var p1 = new Vector3((float)b[0], 0f, (float)b[2]);
                var t = p1 - p0; if (t.sqrMagnitude < 0.01f) continue;
                var rn = new Vector3(t.z, 0f, -t.x).normalized;          // right of travel
                var toRoad = right ? -rn : rn;
                var back = -toRoad * thick;
                float b0 = (float)a[1], t0 = (float)a[3], b1 = (float)b[1], t1 = (float)b[3];
                var cell = ctx.Cells.At((p0 + p1) * 0.5f);
                var v = cell.V(Mat.Wall);
                Vector3 B0 = p0 + Vector3.up * b0, T0 = p0 + Vector3.up * t0, B1 = p1 + Vector3.up * b1, T1 = p1 + Vector3.up * t1;
                var uv = new Vector2(0.5f, 0.5f);
                // front (faces the road), back, cap: FlatQuad wants clockwise seen from the front
                if (right) { v.FlatQuad(B1, T1, T0, B0, uv); v.FlatQuad(B0 + back, T0 + back, T1 + back, B1 + back, uv); }
                else { v.FlatQuad(B0, T0, T1, B1, uv); v.FlatQuad(B1 + back, T1 + back, T0 + back, B0 + back, uv); }
                if (right) v.FlatQuad(T0, T1, T1 + back, T0 + back, uv); else v.FlatQuad(T1, T0, T0 + back, T1 + back, uv);
                if (fill)
                {
                    var col = cell.Col(Layers.Road);
                    if (right) { col.FlatQuad(B1, T1, T0, B0); col.FlatQuad(B0 + back, T0 + back, T1 + back, B1 + back); }
                    else { col.FlatQuad(B0, T0, T1, B1); col.FlatQuad(B1 + back, T1 + back, T0 + back, B0 + back); }
                }
                ctx.WallMetres += Vector3.Distance(p0, p1);
            }
        }

        private static Vector3 Up(float y) => new Vector3(0f, y, 0f);

        private static void Segment(Seg s, Context ctx)
        {
            int n = s.S.Length;
            if (n < 2) return;
            const int Piece = 32;
            for (int k0 = 0; k0 < n - 1; k0 += Piece)
            {
                int k1 = Mathf.Min(n - 1, k0 + Piece);
                Piece_(s, k0, k1, ctx);
                ctx.Pieces++;
            }
            if (s.Parking > 0.1f) Parking(s, ctx);
        }

        private static void Piece_(Seg s, int k0, int k1, Context ctx)
        {
            var cell = ctx.Cells.At(s.S[(k0 + k1) / 2]);
            int m = k1 - k0 + 1;
            var C = new Vector3[m]; var R = new Vector3[m]; var along = new float[m];
            var ug = new bool[m];
            for (int i = 0; i < m; i++)
            {
                C[i] = s.S[k0 + i]; R[i] = s.Right[k0 + i]; along[i] = s.Along[k0 + i];
                ug[i] = s.Underground != null && s.Underground[k0 + i];
            }
            Vector3[] Off(float u, float dy = 0f) { var o = new Vector3[m]; for (int i = 0; i < m; i++) o[i] = C[i] + R[i] * u + Up(dy); return o; }
            var Lp = Off(-s.WL); var Rp = Off(s.WR);
            // carriageway: one ribbon, markings from vertex data
            var road = cell.V(s.Surface == "dirt" ? Mat.Dirt : Mat.Road, true, true, true);
            road.Strip(Lp, Rp, along, -s.WL, s.WR, null, new Vector2(s.LaneWidth, s.Median), new Vector2(s.F, s.B), s.Attr(true));
            var col = cell.Col(Layers.Road);
            col.Strip(Lp, Rp, along, 0, 0);
            if (s.KerbOn && s.Sidewalk > 0f && !s.Bridge)
            {
                KerbSide(cell, Rp, R, along, s.Sidewalk, +1f);
                KerbSide(cell, Lp, R, along, s.Sidewalk, -1f);
            }
            else if (!s.Bridge)
            {
                // shoulders end in a skirt into the ground (hidden gaps where the carve blends out)
                var c = cell.V(Mat.Concrete);
                c.Strip(Rp, Off(s.WR, -1.2f), along, 0, 0);                 // right edge, faces right
                c.Strip(Off(-s.WL, -1.2f), Lp, along, 0, 0);                // left edge, faces left
            }
            if (s.Bridge) BridgePiece(s, cell, C, R, along, ctx);
            if (s.Tunnel) TunnelPiece(s, cell, C, R, along, ug, ctx);
            if (!s.Tunnel && !s.Bridge && (s.Kind == "mountain" || s.Kind == "highway" || s.BaseKind == "mountain")) Rails(s, cell, C, R, ug, ctx);
        }

        /// <summary>Kerb face, kerb top, paver sidewalk and the outer skirt on one side (side = +1 right, -1 left).</summary>
        private static void KerbSide(CellGeo cell, Vector3[] edge, Vector3[] right, float[] along, float width, float side)
        {
            int m = edge.Length;
            Vector3[] P(float u, float dy) { var o = new Vector3[m]; for (int i = 0; i < m; i++) o[i] = edge[i] + right[i] * (u * side) + Up(dy); return o; }
            var kerb = cell.V(Mat.Kerb);
            var walk = cell.V(Mat.Sidewalk);
            var colL = cell.Col(Layers.Road);
            var bottom = P(0f, 0f); var top = P(0f, Kerb); var kerbOut = P(KerbTop, Kerb); var outer = P(width, Kerb); var outerLow = P(width, -1.0f);
            var slopeOut = P(0.35f, Kerb);
            if (side > 0)
            {
                kerb.Strip(bottom, top, along, 0, 0);          // face looks back at the road (left)
                kerb.Strip(top, kerbOut, along, 0, 0);
                walk.Strip(kerbOut, outer, along, 0, 0);
                kerb.Strip(outer, outerLow, along, 0, 0);      // outer skirt looks outwards (right)
                colL.Strip(bottom, slopeOut, along, 0, 0);
                colL.Strip(slopeOut, outer, along, 0, 0);
            }
            else
            {
                kerb.Strip(top, bottom, along, 0, 0);
                kerb.Strip(kerbOut, top, along, 0, 0);
                walk.Strip(outer, kerbOut, along, 0, 0);
                kerb.Strip(outerLow, outer, along, 0, 0);
                colL.Strip(slopeOut, bottom, along, 0, 0);
                colL.Strip(outer, slopeOut, along, 0, 0);
            }
        }

        private static void BridgePiece(Seg s, CellGeo cell, Vector3[] C, Vector3[] R, float[] along, Context ctx)
        {
            int m = C.Length;
            float wr = s.WR + (s.Sidewalk > 0 ? s.Sidewalk : 0f), wl = s.WL + (s.Sidewalk > 0 ? s.Sidewalk : 0f);
            Vector3[] P(float u, float dy) { var o = new Vector3[m]; for (int i = 0; i < m; i++) o[i] = C[i] + R[i] * u + Up(dy); return o; }
            var con = cell.V(Mat.Concrete);
            var col = cell.Col(Layers.Road);
            if (s.Sidewalk > 0)
            {
                // raised walkways on the deck, flush with the parapets
                var walk = cell.V(Mat.Sidewalk);
                walk.Strip(P(s.WR, Kerb), P(wr, Kerb), along, 0, 0);
                walk.Strip(P(-wl, Kerb), P(-s.WL, Kerb), along, 0, 0);
                cell.V(Mat.Kerb).Strip(P(s.WR, 0), P(s.WR, Kerb), along, 0, 0);
                cell.V(Mat.Kerb).Strip(P(-s.WL, Kerb), P(-s.WL, 0), along, 0, 0);
                col.Strip(P(s.WR, Kerb), P(wr, Kerb), along, 0, 0);
                col.Strip(P(-wl, Kerb), P(-s.WL, Kerb), along, 0, 0);
            }
            float baseY = s.Sidewalk > 0 ? Kerb : 0f;
            // deck underside and fascia
            con.Strip(P(wr + ParapetWidth, -DeckDepth), P(-wl - ParapetWidth, -DeckDepth), along, 0, 0);
            // parapets (inner face, top, outer face down to the deck bottom)
            con.Strip(P(wr, baseY), P(wr, ParapetHeight), along, 0, 0);
            con.Strip(P(wr, ParapetHeight), P(wr + ParapetWidth, ParapetHeight), along, 0, 0);
            con.Strip(P(wr + ParapetWidth, ParapetHeight), P(wr + ParapetWidth, -DeckDepth), along, 0, 0);
            con.Strip(P(-wl, ParapetHeight), P(-wl, baseY), along, 0, 0);
            con.Strip(P(-wl - ParapetWidth, ParapetHeight), P(-wl, ParapetHeight), along, 0, 0);
            con.Strip(P(-wl - ParapetWidth, -DeckDepth), P(-wl - ParapetWidth, ParapetHeight), along, 0, 0);
            col.Strip(P(wr, 0), P(wr, ParapetHeight + 0.2f), along, 0, 0);
            col.Strip(P(-wl, ParapetHeight + 0.2f), P(-wl, 0), along, 0, 0);
            // piers every ~24 m (not at the abutments)
            float total = s.Along[s.Along.Length - 1];
            for (int i = 0; i < m; i++)
            {
                float a = along[i];
                if (a < 10f || a > total - 10f) continue;
                if (Mathf.Repeat(a, 24f) > Vector3.Distance(C[Mathf.Max(0, i - 1)], C[i]) + 0.01f && i > 0) continue;
                var c = C[i];
                float ground = ctx.H.Sample(c.x, c.z);
                float top = c.y - DeckDepth;
                if (top - ground < 1.5f) continue;
                var fwd = Vector3.Cross(R[i], Vector3.up).normalized;
                float span = (wr + wl) * 0.5f;
                var centre = c + R[i] * ((wr - wl) * 0.5f);
                // a pier cap across the deck, two columns down to the bed
                con.Box(centre + Up(-DeckDepth - 0.5f), R[i], Vector3.up, fwd, new Vector3(span + 0.3f, 0.5f, 0.9f));
                foreach (float u in new[] { -span * 0.6f, span * 0.6f })
                {
                    float h = (top - 1f - ground) * 0.5f + 0.5f;
                    var cc = centre + R[i] * u;
                    cc.y = ground - 1f + h;
                    con.Box(cc, R[i], Vector3.up, fwd, new Vector3(0.8f, h, 0.8f));
                }
            }
        }

        private static void TunnelPiece(Seg s, CellGeo cell, Vector3[] C, Vector3[] R, float[] along, bool[] ug, Context ctx)
        {
            int m = C.Length;
            var t = cell.V(Mat.Tunnel);
            var col = cell.Col(Layers.Road);
            float wr = s.WR + 0.6f, wl = s.WL + 0.6f;
            // runs of underground samples: walls, arched roof (inward faces)
            int k = 0;
            while (k < m)
            {
                if (!ug[k]) { k++; continue; }
                int a = k;
                while (k < m && ug[k]) k++;
                int b = Mathf.Min(m - 1, k);
                int len = b - a + 1;
                if (len < 2) continue;
                var arcs = new List<Vector3[]>();
                const int Arc = 7;
                for (int q = 0; q <= Arc; q++)
                {
                    float f = q / (float)Arc;                     // 0 = right wall foot ... 1 = left wall foot
                    var o = new Vector3[len];
                    for (int i = 0; i < len; i++)
                    {
                        var c = C[a + i]; var r = R[a + i];
                        float ang = f * Mathf.PI;
                        float u = Mathf.Lerp(wr, -wl, f);
                        float y = q == 0 || q == Arc ? 0f : TunnelWall + Mathf.Sin(ang) * (TunnelApex - TunnelWall);
                        if (q == 1) { u = wr; y = TunnelWall; }
                        if (q == Arc - 1) { u = -wl; y = TunnelWall; }
                        o[i] = c + r * u + Up(y);
                    }
                    arcs.Add(o);
                }
                var al = new float[len];
                for (int i = 0; i < len; i++) al[i] = along[a + i];
                for (int q = 0; q < Arc; q++)
                {
                    // arc q -> q+1 runs right wall foot -> roof -> left wall foot: cross(along, across) points into the tube
                    t.Strip(arcs[q], arcs[q + 1], al, 0, 0);
                    col.Strip(arcs[q], arcs[q + 1], al, 0, 0);
                }
            }
            // portal frames where the road enters the hill
            for (int i = 1; i < m; i++)
            {
                if (ug[i] == ug[i - 1]) continue;
                int at = ug[i] ? i : i - 1;
                var c = C[at]; var r = R[at];
                var fwd = Vector3.Cross(r, Vector3.up).normalized;      // along the road (from -> to)
                var outward = ug[i] ? -fwd : fwd;
                var face = c + outward * 0.2f;
                float w0 = wl + 7f, w1 = wr + 7f;
                var con = cell.V(Mat.Concrete);
                // left pier, right pier, lintel: boxes 1.2 m deep
                var half = new Vector3(0f, 0f, 0.6f);
                void Slab(float u0, float u1, float y0, float y1)
                {
                    var cc = face + r * ((u0 + u1) * 0.5f) + Up((y0 + y1) * 0.5f) - outward * 0.6f;
                    con.Box(cc, r, Vector3.up, outward, new Vector3(Mathf.Abs(u1 - u0) * 0.5f, (y1 - y0) * 0.5f, 0.6f));
                    ctx.Cells.At(cc).Boxes.Add(new CellGeo.BoxCol { Center = cc, Rotation = Quaternion.LookRotation(outward, Vector3.up),
                        Size = new Vector3(Mathf.Abs(u1 - u0), y1 - y0, 1.2f), Layer = Layers.Road, Name = "portal" });
                }
                Slab(-w0, -wl, -1.5f, TunnelApex + 4.5f);
                Slab(wr, w1, -1.5f, TunnelApex + 4.5f);
                Slab(-wl, wr, TunnelApex, TunnelApex + 4.5f);
            }
        }

        private static void Rails(Seg s, CellGeo cell, Vector3[] C, Vector3[] R, bool[] ug, Context ctx)
        {
            int m = C.Length;
            foreach (float side in new[] { 1f, -1f })
            {
                float u = side > 0 ? s.WR + 0.25f : -(s.WL + 0.25f);
                var need = new bool[m];
                for (int i = 0; i < m; i++)
                {
                    var p = C[i] + R[i] * (u + side * 2.2f);
                    float g = ctx.H.Sample(p.x, p.z);
                    need[i] = !ug[i] && C[i].y - g > 1.4f;
                }
                // close small gaps, drop tiny runs
                for (int i = 1; i < m - 1; i++) if (!need[i] && need[i - 1] && need[i + 1]) need[i] = true;
                var rail = cell.V(Mat.Rail);
                int k = 0;
                while (k < m)
                {
                    if (!need[k]) { k++; continue; }
                    int a = k;
                    while (k < m && need[k]) k++;
                    int b = k - 1;
                    if (b - a < 2) continue;
                    var lo = new Vector3[b - a + 1]; var hi = new Vector3[b - a + 1]; var al = new float[b - a + 1];
                    for (int i = a; i <= b; i++)
                    {
                        var p = C[i] + R[i] * u;
                        lo[i - a] = p + Up(0.52f); hi[i - a] = p + Up(0.86f); al[i - a] = s.Along[0] + i;
                        if ((i - a) % 2 == 0)
                        {
                            var fwd = Vector3.Cross(R[i], Vector3.up).normalized;
                            rail.Box(p + Up(0.42f), R[i], Vector3.up, fwd, new Vector3(0.06f, 0.42f, 0.06f));
                        }
                    }
                    // two-sided beam
                    rail.Strip(lo, hi, al, 0, 0);
                    rail.Strip(hi, lo, al, 0, 0);
                    for (int i = a; i < b; i += 3)
                    {
                        int j = Mathf.Min(b, i + 3);
                        var p0 = C[i] + R[i] * u; var p1 = C[j] + R[j] * u;
                        var mid = (p0 + p1) * 0.5f + Up(0.6f);
                        var dir = p1 - p0;
                        if (dir.sqrMagnitude < 0.01f) continue;
                        cell.Boxes.Add(new CellGeo.BoxCol { Center = mid, Rotation = Quaternion.LookRotation(dir.normalized, Vector3.up),
                            Size = new Vector3(0.3f, 1.2f, dir.magnitude + 0.2f), Layer = Layers.Prop, Name = "rail" });
                        ctx.RailMetres += Mathf.RoundToInt(dir.magnitude);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ junctions

        private static void Junction(JArray polygon, JArray fillets, JArray approaches, string kind, List<Seg> segs, Context ctx)
        {
            if (polygon == null || polygon.Count < 3) return;
            var P = polygon.Select(IslandV2Paths.V3).ToList();
            var centre = Vector3.zero;
            foreach (var p in P) centre += p;
            centre /= P.Count;
            var cell = ctx.Cells.At(centre);
            var road = cell.V(Mat.Road, true, true, true);
            var col = cell.Col(Layers.Road);
            var attr = new Color(0, 0, 0, 0);
            int ci = road.Add(centre, Vector3.up, Vector2.zero, new Vector2(3.3f, 0), Vector2.zero, attr);
            int cc = col.Add(centre, Vector3.up, Vector2.zero);
            int baseIdx = road.Count, baseCol = col.Count;
            foreach (var p in P) { road.Add(p, Vector3.up, Vector2.zero, new Vector2(3.3f, 0), Vector2.zero, attr); col.Add(p, Vector3.up, Vector2.zero); }
            for (int i = 0; i < P.Count; i++)
            {
                int a = baseIdx + i, b = baseIdx + (i + 1) % P.Count;
                road.Tri(ci, b, a);
                col.Tri(cc, baseCol + (i + 1) % P.Count, baseCol + i);
            }
            // corner sidewalks along the kerb fillets
            foreach (var f in fillets)
            {
                float sw = (float)f["sidewalk"];
                if (sw <= 0f || (bool)f["straight"]) continue;
                var F = ((JArray)f["points"]).Select(IslandV2Paths.V3).ToArray();
                if (F.Length < 2) continue;
                int m = F.Length;
                var outward = new Vector3[m];
                for (int i = 0; i < m; i++)
                {
                    var t = F[Mathf.Min(m - 1, i + 1)] - F[Mathf.Max(0, i - 1)];
                    var nrm = new Vector3(t.z, 0f, -t.x).normalized;    // right of the CCW travel = away from the junction
                    if (Vector3.Dot(nrm, F[i] - centre) < 0f) nrm = -nrm;
                    outward[i] = nrm;
                }
                var al = new float[m];
                for (int i = 1; i < m; i++) al[i] = al[i - 1] + Vector3.Distance(F[i - 1], F[i]);
                KerbSide(cell, F, outward, al, sw, +1f);
            }
            // zebra crossings and stop lines on kerbed approaches of signal / stop junctions
            if (kind != "signal" && kind != "stop") return;
            foreach (var a in approaches)
            {
                var seg = segs.FirstOrDefault(s => s.Id == (string)a["segment"]);
                if (seg == null || seg.Sidewalk <= 0f || seg.Bridge) continue;
                var cut = IslandV2Paths.V3(a["cut"]);
                var dir = new Vector3((float)a["dir"][0], 0f, (float)a["dir"][1]).normalized;
                var rgt = new Vector3(dir.z, 0f, -dir.x);                 // right of the outward direction
                var rightPt = IslandV2Paths.V3(a["right"]); var leftPt = IslandV2Paths.V3(a["left"]);
                float uR = Vector3.Dot(rightPt - cut, rgt), uL = Vector3.Dot(leftPt - cut, rgt);
                var paint = cell.V(Mat.Paint);
                float parkR = seg.Parking, y = cut.y + 0.02f;
                for (float u = uL + 0.5f + parkR; u <= uR - 0.5f - parkR; u += 1.2f)
                {
                    var p0 = cut + rgt * u + dir * 1.2f; var p1 = p0 + dir * 3.2f;
                    var a0 = p0 - rgt * 0.3f; var b0 = p0 + rgt * 0.3f; var a1 = p1 - rgt * 0.3f; var b1 = p1 + rgt * 0.3f;
                    a0.y = b0.y = a1.y = b1.y = y;
                    paint.FlatQuad(a1, b1, b0, a0);
                }
                ctx.Crosswalks++;
                // stop line across the incoming lanes (left of the outward direction for right-hand traffic)
                bool toEnd = (string)a["end"] == "to";
                int incoming = seg.OneWay ? (toEnd ? seg.F : 0) : (toEnd ? seg.F : seg.B);
                if (incoming <= 0) continue;
                float uFrom = seg.OneWay ? uL + parkR : -(seg.Median * 0.5f + incoming * seg.LaneWidth);
                float uTo = seg.OneWay ? uR - parkR : -seg.Median * 0.5f - 0.05f;
                var s0 = cut + dir * 5.0f; var s1 = s0 + dir * 0.45f;
                var q0 = s0 + rgt * uFrom; var q1 = s0 + rgt * uTo; var q2 = s1 + rgt * uTo; var q3 = s1 + rgt * uFrom;
                q0.y = q1.y = q2.y = q3.y = y;
                paint.FlatQuad(q3, q2, q1, q0);
            }
        }

        private static void RoundaboutIsland(JToken x, List<Seg> segs, Context ctx)
        {
            var c = IslandV2Paths.V3(x["center"]);
            float R = (float)x["radius"];
            var ring = segs.FirstOrDefault(s => s.Ring != null && ("rb_" + s.Ring) == (string)x["id"]);
            float cw = ring != null ? ring.WR : 2.5f;
            float r = Mathf.Max(3f, R - cw - 0.4f);
            var cell = ctx.Cells.At(c);
            var kerb = cell.V(Mat.Kerb);
            var grass = cell.V(Mat.Grass);
            const int Seg = 40;
            var lo = new Vector3[Seg + 1]; var hi = new Vector3[Seg + 1]; var al = new float[Seg + 1];
            for (int i = 0; i <= Seg; i++)
            {
                float a = -i / (float)Seg * Mathf.PI * 2f;          // clockwise so the kerb faces outwards
                var p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                lo[i] = p; hi[i] = p + Up(0.3f); al[i] = i;
            }
            kerb.Strip(lo, hi, al, 0, 0);
            int ci = grass.Add(c + Up(0.32f), Vector3.up, Vector2.zero);
            int b0 = grass.Count;
            for (int i = 0; i <= Seg; i++) grass.Add(hi[i] + Up(0.02f), Vector3.up, Vector2.zero);
            for (int i = 0; i < Seg; i++) grass.Tri(ci, b0 + i, b0 + i + 1);
            cell.Boxes.Add(new CellGeo.BoxCol { Center = c + Up(0.6f), Rotation = Quaternion.identity, Size = new Vector3(r * 1.4f, 1.2f, r * 1.4f), Layer = Layers.Prop, Name = "roundabout" });
        }

        // ------------------------------------------------------------------ parking bays (VehicleSpawn markers)

        private static void Parking(Seg s, Context ctx)
        {
            float total = s.Along[s.Along.Length - 1];
            foreach (float side in new[] { 1f, -1f })
            {
                int lanes = s.OneWay ? s.F : side > 0 ? s.F : s.B;
                float u = s.OneWay ? side * (s.F * s.LaneWidth * 0.5f + s.Parking * 0.5f)
                                   : side * (s.Median * 0.5f + lanes * s.LaneWidth + s.Parking * 0.5f);
                for (float a = 10f; a < total - 10f; a += 6f)
                {
                    int k = Array.FindIndex(s.Along, v => v >= a);
                    if (k < 1) continue;
                    float t = Mathf.InverseLerp(s.Along[k - 1], s.Along[k], a);
                    var c = Vector3.Lerp(s.S[k - 1], s.S[k], t);
                    var r = Vector3.Lerp(s.Right[k - 1], s.Right[k], t).normalized;
                    var p = c + r * u + Up(0.02f);
                    if (ctx.Doors.Any(d => (new Vector2(d.x - p.x, d.z - p.z)).sqrMagnitude < 14f * 14f)) continue;
                    var fwd = Vector3.Cross(r, Vector3.up).normalized * (side > 0 || s.OneWay ? 1f : -1f);
                    ctx.Cells.At(p).Markers.Add(new CellGeo.Marker
                    {
                        Kind = "vehicle", Id = $"{s.Id}_{(side > 0 ? "r" : "l")}{Mathf.RoundToInt(a)}", Segment = s.Id, Position = p,
                        Rotation = Quaternion.LookRotation(fwd, Vector3.up), Size = new Vector2(2.4f, 6f),
                    });
                    ctx.Bays++;
                }
            }
        }
    }
}
