using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// CT Island art pass (2026-10-17, ref 8): the painted island around the baked venue maps. Procedural and deterministic:
    /// an SDF coastline with bays, a peninsula, an offshore islet and the Marina harbour; a terrain heightfield (rolling hills,
    /// low mountains at the edge) with painted hill shading and long soft shadows from one warm north-west light; a canal from
    /// the hills to the sea; a ring boulevard, gently curved avenues, a roundabout and road markings; distinct district grounds,
    /// parks in empty slots, construction lots with cranes; boats; trees stamped from sprites rendered off the project's CC0
    /// models; brush and paper texture and a sea vignette. Venue rects and plot coordinates are inputs only: nothing here moves
    /// them, and <see cref="Validate"/> refuses an island where a venue would touch water.
    /// </summary>
    internal static class AtlasPainter
    {
        private const int N = AtlasBaker.Size;
        private const float Half = AtlasBaker.Half, Pitch = AtlasBaker.Pitch, PlotHalf = AtlasBaker.Plot / 2f;
        private static float Mpp => 2f * Half / N;
        private static readonly Vector2 Roundabout = new Vector2(Pitch / 2f, -Pitch / 2f);
        private static readonly Vector2 Harbour = new Vector2(0f, -258f);
        private const float HarbourR = 26f;

        // ------------------------------------------------------------------ shapes (metres; negative = inside)

        private static float SMin(float a, float b, float k) { float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k); return Mathf.Lerp(b, a, h) - k * h * (1 - h); }
        private static float SMax(float a, float b, float k) => -SMin(-a, -b, k);
        private static float Ellipse(Vector2 p, Vector2 r) => (new Vector2(p.x / r.x, p.y / r.y).magnitude - 1f) * Mathf.Min(r.x, r.y);
        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }
        public static float RoundRect(Vector2 p, Vector2 half, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x) - half.x + r, Mathf.Abs(p.y) - half.y + r);
            return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r;
        }

        /// <summary>Signed distance to the coastline (metres, negative inland).</summary>
        public static float Coast(Vector2 p)
        {
            // Wild far out, calm next to the plot ring (so the town keeps its ground).
            float amp = Mathf.Lerp(0.4f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(222f, 262f, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)))));
            var q = p + (new Vector2(N_(p.x / 110f + 3.1f, p.y / 110f) - 0.5f, N_(p.x / 110f - 5.7f, p.y / 110f + 1.3f) - 0.5f) * 30f
                      + new Vector2(N_(p.x / 45f + 8.1f, p.y / 45f) - 0.5f, N_(p.x / 45f - 2.7f, p.y / 45f + 6.3f) - 0.5f) * 12f) * amp;
            float land = RoundRect(q, new Vector2(232f, 232f), 70f);
            land = SMin(land, Ellipse(q - new Vector2(-50f, 244f), new Vector2(200f, 38f)), 30f);        // northern hills
            land = SMin(land, Ellipse(q - new Vector2(-244f, 30f), new Vector2(40f, 215f)), 30f);        // western shore
            land = SMin(land, Ellipse(q - new Vector2(246f, -72f), new Vector2(34f, 110f)), 24f);        // eastern shore
            land = SMin(land, Ellipse(q - new Vector2(0f, -250f), new Vector2(112f, 34f)), 24f);         // marina headlands
            land = SMin(land, Segment(q, new Vector2(170f, 200f), new Vector2(258f, 252f)) - 22f, 26f);  // NE peninsula
            land = SMax(land, -((q - new Vector2(272f, 52f)).magnitude - 30f), 12f);                      // east bay
            land = SMax(land, -((q - new Vector2(-200f, -262f)).magnitude - 36f), 12f);                   // south-west cove
            land = SMax(land, -((q - new Vector2(-276f, -128f)).magnitude - 24f), 10f);                   // west cove
            land = SMax(land, -((q - new Vector2(96f, 270f)).magnitude - 26f), 10f);                      // north cove
            land = SMax(land, -((q - new Vector2(262f, -178f)).magnitude - 22f), 10f);                    // east cove
            land = SMin(land, Ellipse(q - new Vector2(176f, -262f), new Vector2(58f, 20f)), 18f);         // south-east cape
            land = SMin(land, Ellipse(q - new Vector2(-150f, -256f), new Vector2(30f, 22f)), 16f);        // south-west cape
            // Marina harbour: a round basin with a channel to the open sea (exact edges, no warp).
            float basin = Mathf.Min((p - Harbour).magnitude - HarbourR, Segment(p, Harbour, Harbour + new Vector2(0f, -60f)) - 11f);
            land = SMax(land, -basin, 6f);
            float islet = (q - new Vector2(258f, -252f)).magnitude - 19f;
            land = Mathf.Min(land, islet);
            return land + (N_(p.x / 13f, p.y / 13f) - 0.5f) * 3.5f;
        }

        // The canal: from the northern hills, round the north-west corner, down the west shore to the sea.
        private static readonly Vector2[] Canal = Smooth(new[]
        {
            new Vector2(-140f, 266f), new Vector2(-180f, 250f), new Vector2(-214f, 228f), new Vector2(-236f, 196f),
            new Vector2(-244f, 130f), new Vector2(-240f, 60f), new Vector2(-248f, 6f), new Vector2(-272f, -14f), new Vector2(-312f, -22f),
        }, 10);

        private static Vector2[] Smooth(Vector2[] pts, int sub)
        {
            var o = new List<Vector2>();
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector2 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Length - 1, i + 2)];
                for (int s = 0; s < sub; s++)
                {
                    float t = s / (float)sub, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            o.Add(pts[pts.Length - 1]);
            return o.ToArray();
        }

        /// <summary>Distance to the canal centreline and how far along it (0 source .. 1 mouth).</summary>
        internal static float CanalDist(Vector2 p, out float along)
        {
            float best = float.MaxValue; along = 0f;
            for (int i = 0; i < Canal.Length - 1; i++)
            {
                var a = Canal[i]; var b = Canal[i + 1];
                if (Mathf.Abs(p.x - a.x) > 60f && Mathf.Abs(p.x - b.x) > 60f) continue;
                if (Mathf.Abs(p.y - a.y) > 60f && Mathf.Abs(p.y - b.y) > 60f) continue;
                var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                float d = (p - (a + ab * t)).magnitude;
                if (d < best) { best = d; along = (i + t) / (Canal.Length - 1); }
            }
            return best;
        }

        internal static float CanalHalfWidth(float along) => Mathf.Lerp(3.2f, 7.5f, along * along);

        // ------------------------------------------------------------------ terrain

        internal static float Mountains(Vector2 p)
        {
            float north = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(222f, 252f, p.y)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-262f, -200f, p.x)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(110f, 170f, p.x)));
            float ne = Mathf.Clamp01(1f - Segment(p, new Vector2(190f, 214f), new Vector2(250f, 246f)) / 24f);
            float west = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-262f, -288f, p.x)) * Mathf.Clamp01(1f - Mathf.Abs(p.y - 60f) / 130f);
            return Mathf.Max(north, Mathf.Max(ne, west));
        }

        internal static float Height(Vector2 p, float coast, float flat)
        {
            float hills = Fbm(p.x / 120f, p.y / 120f) * 7f + Fbm(p.x / 38f + 9f, p.y / 38f) * 2.2f;
            float m = Mountains(p);
            float ridge = 0f;
            if (m > 0f)
            {
                float r = 0f, amp = 1f, f = 1f / 34f, n = 0f;
                for (int i = 0; i < 4; i++) { float v = 1f - Mathf.Abs(N_(p.x * f + 17f, p.y * f - 4f) * 2f - 1f); r += v * v * amp; n += amp; amp *= 0.5f; f *= 2.05f; }
                ridge = r / n * 30f * m;
            }
            float h = (hills * (1f - flat * 0.75f) + ridge) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(-coast / 14f));
            return h;
        }

        // ------------------------------------------------------------------ town layout (art only)

        /// <summary>Distance to the avenues (gently curved, between the plots) and the ring boulevard; <paramref name="along"/> drives dashes.</summary>
        internal static float RoadDist(Vector2 p, out float along, out bool ring)
        {
            along = 0f; ring = false;
            float best = float.MaxValue;
            foreach (float g in new[] { -Pitch / 2f, Pitch / 2f })
            {
                if (Mathf.Abs(p.y) < 216f)
                {
                    float x = g + Mathf.Sin(p.y / 47f + g) * 2.4f;
                    float d = Mathf.Abs(p.x - x);
                    if (d < best) { best = d; along = p.y; }
                }
                if (Mathf.Abs(p.x) < 216f)
                {
                    float y = g + Mathf.Sin(p.x / 53f - g) * 2.4f;
                    float d = Mathf.Abs(p.y - y);
                    if (d < best) { best = d; along = p.x; }
                }
            }
            float rr = Mathf.Abs(RoundRect(p + new Vector2(Mathf.Sin(p.y / 61f) * 2f, Mathf.Sin(p.x / 59f) * 2f), new Vector2(216f, 216f), 64f));
            if (rr < best) { best = rr; ring = true; along = Mathf.Atan2(p.y, p.x) * 216f; }
            return best;
        }

        private struct Park { public Rect R; public string Kind; }

        private static List<Park> Parks(List<(Rect r, string plot, int slot)> venues)
        {
            var parks = new List<Park>();
            foreach (var plot in AtlasBaker.Plots)
            {
                if (plot.kind == "reserved") continue;
                int used = venues.Count(v => v.plot == plot.id);
                for (int s = used; s < AtlasBaker.Slots.Length; s++)
                {
                    var c = plot.Center + AtlasBaker.Slots[s];
                    parks.Add(new Park { R = new Rect(c.x - 15f, c.y - 11f, 30f, 22f), Kind = plot.id });
                }
            }
            return parks;
        }

        private static float PathDist(Vector2 p, List<Rect> footprints)
        {
            float best = float.MaxValue;
            foreach (var r in footprints)
            {
                float plotY = Mathf.Round(r.center.y / Pitch) * Pitch;
                float x = r.center.x, y0 = r.yMin, y1 = r.center.y > plotY ? plotY : plotY - Pitch / 2f;
                if (p.y > y0 + 0.5f || p.y < y1) continue;
                best = Mathf.Min(best, Mathf.Abs(p.x - x));
            }
            foreach (var r in footprints)
            {
                float plotX = Mathf.Round(r.center.x / Pitch) * Pitch, plotY = Mathf.Round(r.center.y / Pitch) * Pitch;
                if (Mathf.Abs(p.x - plotX) <= Pitch / 2f - 12f) best = Mathf.Min(best, Mathf.Abs(p.y - plotY));
            }
            return best;
        }

        private static AtlasBaker.PlotDef Nearest(Vector2 p)
        {
            AtlasBaker.PlotDef best = null; float bd = float.MaxValue;
            foreach (var pl in AtlasBaker.Plots) { float d = (p - pl.Center).sqrMagnitude; if (d < bd) { bd = d; best = pl; } }
            return best;
        }

        private static float RectDist(Vector2 p, Rect r) => RoundRect(p - r.center, r.size / 2f, 0.01f);

        internal static float PlotDist(Vector2 p, AtlasBaker.PlotDef plot) =>
            RoundRect(p - plot.Center, new Vector2(PlotHalf, PlotHalf), 26f) + (N_(p.x / 22f + 11f, p.y / 22f) - 0.5f) * 8f;

        /// <summary>No venue may touch water or the canal, and every plot stays on land.</summary>
        public static void Validate(List<Rect> footprints)
        {
            foreach (var r in footprints)
                for (int i = 0; i <= 8; i++)
                    for (int j = 0; j <= 8; j++)
                    {
                        var p = new Vector2(r.xMin + r.width * i / 8f, r.yMin + r.height * j / 8f);
                        if (Coast(p) > -10f) throw new InvalidOperationException($"venue rect {r} reaches the coast at {p}");
                        if (CanalDist(p, out _) < 12f) throw new InvalidOperationException($"venue rect {r} touches the canal at {p}");
                    }
            foreach (var plot in AtlasBaker.Plots)
                for (int i = 0; i <= 12; i++)
                    for (int j = 0; j <= 12; j++)
                    {
                        var p = plot.Center + new Vector2(-PlotHalf + i * 10f, -PlotHalf + j * 10f);
                        if (RoundRect(p - plot.Center, new Vector2(PlotHalf, PlotHalf), 26f) > 0f) continue; // plots are drawn rounded
                        if (Coast(p) > -6f) throw new InvalidOperationException($"plot {plot.id} reaches the coast at {p}");
                    }
        }

        // ------------------------------------------------------------------ paint

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Color> HexCache = new System.Collections.Concurrent.ConcurrentDictionary<string, Color>();
        /// <summary>Pure C# hex colour (thread-safe for the parallel passes), cached.</summary>
        private static Color Hex(string h) => HexCache.GetOrAdd(h, k => new Color(Convert.ToInt32(k.Substring(0, 2), 16) / 255f,
            Convert.ToInt32(k.Substring(2, 2), 16) / 255f, Convert.ToInt32(k.Substring(4, 2), 16) / 255f, 1f));
        private static Vector2 At(int x, int y) => new Vector2(-Half + (x + 0.5f) * Mpp, -Half + (y + 0.5f) * Mpp);

        public static Color[] Paint(List<(Rect r, string plot, int slot)> venues, Dictionary<string, AtlasSprites.Sprite> sprites)
        {
            var footprints = venues.Select(v => v.r).ToList();
            Validate(footprints);
            var parks = Parks(venues);
            var coast = new float[N * N];
            var height = new float[N * N];
            var plotK = new float[N * N];
            Parallel.For(0, N, y =>
            {
                for (int x = 0; x < N; x++)
                {
                    var p = At(x, y);
                    int i = y * N + x;
                    float c = Coast(p);
                    coast[i] = c;
                    float k = 0f;
                    foreach (var plot in AtlasBaker.Plots) k = Mathf.Max(k, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(14f, -8f, PlotDist(p, plot))));
                    plotK[i] = k;
                    height[i] = c < 0 ? Height(p, c, k) : 0f;
                }
            });

            // Long soft shadows from the terrain (low north-west sun, ~30 degrees).
            var shadow = new float[N * N];
            float tan = Mathf.Tan(30f * Mathf.Deg2Rad), step = Mpp * 1.41421f * 2f;
            Parallel.For(0, N, y =>
            {
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    float h = height[i];
                    if (coast[i] > 0) continue;
                    float best = 0f;
                    for (int s = 1; s < 70; s++)
                    {
                        int nx = x - s * 2, ny = y + s * 2;
                        if (nx < 0 || ny >= N) break;
                        float above = height[ny * N + nx] - (h + s * step * tan);
                        if (above > 0f) { best = Mathf.Max(best, Mathf.Clamp01(above / 3f)); if (best >= 1f) break; }
                    }
                    shadow[i] = best;
                }
            });
            shadow = Blur(shadow, N, N, 3);

            var o = new Color[N * N];
            var sunDir = new Vector3(-0.55f, 0.55f, 0.63f).normalized;   // from the north-west, x east, y north, z up
            Color deep = Hex("2f78b8"), mid = Hex("3e98cc"), shallow = Hex("67c9d6"), turquoise = Hex("8fe0d8"), foam = Hex("f1fbf6");
            Color sand = Hex("efdcaa"), wetSand = Hex("d6bf8d");
            Color grassLo = Hex("97c95c"), grassMid = Hex("7cb54c"), grassHi = Hex("5f9a43"), rock = Hex("9c8a72"), rockHi = Hex("cdbd9e");
            Color road = Hex("a59d93"), kerb = Hex("efe4ca"), mark = Hex("fffaf0");
            Parallel.For(0, N, y =>
            {
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    var p = At(x, y);
                    float c = coast[i];
                    Color col;
                    if (c > 0f)
                    {
                        // Sea: turquoise shallows to deep blue, two broken foam lines, gentle ripples, vignette to the edge.
                        float t = Mathf.Clamp01(c / 70f);
                        col = c < 6f ? Color.Lerp(turquoise, shallow, c / 6f) : t < 0.35f ? Color.Lerp(shallow, mid, (t - 0.086f) / 0.264f) : Color.Lerp(mid, deep, (t - 0.35f) / 0.65f);
                        float brk = N_(p.x / 9f, p.y / 9f);
                        if (Mathf.Abs(c - 1.2f) < 0.9f) col = Color.Lerp(col, foam, 0.85f * (1f - Mathf.Abs(c - 1.2f) / 0.9f));
                        if (Mathf.Abs(c - 6.5f) < 0.55f && brk > 0.45f) col = Color.Lerp(col, foam, 0.45f);
                        if (Mathf.Abs(c - 13f) < 0.45f && brk > 0.62f) col = Color.Lerp(col, foam, 0.25f);
                        float rip = Mathf.Sin((p.x * 0.7f + p.y * 0.3f) * 0.45f + N_(p.x / 20f, p.y / 20f) * 9f);
                        if (rip > 0.94f && c > 10f) col = Color.Lerp(col, Color.white, 0.12f);
                        float edge = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) / Half;
                        col *= Mathf.Lerp(1f, 0.72f, Mathf.SmoothStep(0.72f, 1.02f, edge));
                        col.a = 1f;
                    }
                    else
                    {
                        float inland = -c;
                        float h = height[i];
                        // Painted hill shading from heightfield normals, softly banded.
                        float hx = height[y * N + Mathf.Min(N - 1, x + 1)] - height[y * N + Mathf.Max(0, x - 1)];
                        float hy = height[Mathf.Min(N - 1, y + 1) * N + x] - height[Mathf.Max(0, y - 1) * N + x];
                        var n = new Vector3(-hx / (2f * Mpp), -hy / (2f * Mpp), 1f).normalized;
                        float lit = Vector3.Dot(n, sunDir) / sunDir.z;
                        lit = Mathf.Lerp(lit, Mathf.Round(lit * 7f) / 7f, 0.35f);
                        float m = Mountains(p);
                        col = Color.Lerp(grassLo, grassMid, Mathf.SmoothStep(0.25f, 0.8f, N_(p.x / 55f, p.y / 55f)));
                        col = Color.Lerp(col, grassHi, Mathf.Clamp01(h / 10f));
                        if (m > 0f)
                        {
                            float steep = Mathf.Clamp01((new Vector2(hx, hy).magnitude / (2f * Mpp)) * 0.9f);
                            col = Color.Lerp(col, rock, Mathf.Clamp01(m * (h - 6f) / 12f) * (0.4f + steep * 0.6f));
                            col = Color.Lerp(col, rockHi, Mathf.Clamp01((h - 18f) / 10f) * m);
                        }
                        col *= Mathf.Clamp(lit, 0.68f, 1.2f);
                        if (inland < 10f) col = Color.Lerp(inland < 3f ? wetSand : sand, col, Mathf.SmoothStep(6f, 10f, inland));

                        // Districts.
                        float k = plotK[i];
                        if (k > 0.001f)
                        {
                            var plot = Nearest(p);
                            col = Color.Lerp(col, Ground(plot, p, h, plot.ground), k);
                        }
                        // Parks in empty slots.
                        foreach (var park in parks)
                        {
                            float pd = RoundRect(p - park.R.center, park.R.size / 2f, 6f);
                            if (pd < 1.5f) col = Color.Lerp(col, ParkColor(park, p, col), Mathf.SmoothStep(1.5f, -0.5f, pd));
                        }
                        // Canal with stone banks and a light centre.
                        float cd = CanalDist(p, out float along);
                        float hw = CanalHalfWidth(along);
                        if (cd < hw + 1.4f)
                        {
                            if (cd < hw)
                            {
                                var water = Color.Lerp(Hex("5fb7dc"), Hex("3f93c7"), cd / hw);
                                if (cd < hw * 0.25f && N_(p.x / 4f, p.y / 4f) > 0.6f) water = Color.Lerp(water, Color.white, 0.18f);
                                col = water;
                            }
                            else col = Color.Lerp(Hex("cdbd9c"), col, (cd - hw) / 1.4f);
                        }
                        // Terrain shadow (mountains and hills cast long and soft).
                        col = Color.Lerp(col, new Color(col.r * 0.6f, col.g * 0.6f, col.b * 0.78f), shadow[i] * 0.55f);

                        // Footpaths from each door (tiny dashed), roads with kerbs and markings, bridges over the canal.
                        float pdist = PathDist(p, footprints);
                        if (pdist < 1.6f)
                        {
                            col = Color.Lerp(col, Hex("ead6ad"), Mathf.SmoothStep(1.6f, 1.0f, pdist));
                            if (pdist < 0.35f && Mathf.Repeat(p.x + p.y, 2.4f) < 1.1f) col = Color.Lerp(col, Hex("a88c66"), 0.55f);
                        }
                        float rdist = RoadDist(p, out float ra, out bool ring);
                        float rb = (p - Roundabout).magnitude;
                        if (rb < 15f)
                        {
                            rdist = Mathf.Abs(rb - 10.5f) - 0.3f;        // the ring of the roundabout
                            ra = Mathf.Atan2(p.y - Roundabout.y, p.x - Roundabout.x) * 10.5f;
                        }
                        float rhw = ring ? 5f : 4.6f;
                        if (rdist < rhw + 1.3f)
                        {
                            bool overCanal = cd < hw + 1.4f;
                            var surface = overCanal ? Hex("b8ab98") : road;
                            col = rdist < rhw ? surface : Color.Lerp(kerb, col, (rdist - rhw) / 1.3f);
                            if (rdist < rhw && rdist > rhw - 0.7f) col = Color.Lerp(col, kerb, 0.7f);                       // kerb edge
                            if (rdist < 0.22f && Mathf.Repeat(ra, 7f) < 3.5f) col = mark;                                    // centre dashes
                            if (Mathf.Abs(rdist - (rhw - 1.3f)) < 0.12f) col = Color.Lerp(col, mark, 0.6f);                 // edge lines
                            if (overCanal && rdist > rhw - 0.5f) col = Color.Lerp(col, Hex("5a5048"), 0.6f);                // bridge rails
                        }
                        if (rb < 6.8f)
                        {
                            col = rb < 3.2f ? Color.Lerp(Hex("6cc6e6"), Color.white, rb < 1.2f ? 0.5f : 0f) : rb < 3.9f ? Hex("efe6d2") : Hex("86c45a");
                        }
                        // Pads under each venue map: a soft drop shadow to the south-east and a light apron.
                        foreach (var r in footprints)
                        {
                            float ds = RectDist(p - new Vector2(2.4f, -2.4f), r);
                            if (ds < 7f) col = Color.Lerp(col, col * new Color(0.6f, 0.58f, 0.72f, 1f), Mathf.SmoothStep(7f, 0f, ds) * 0.5f);
                            float da = RectDist(p, r);
                            if (da < 3f && da > -0.5f) col = Color.Lerp(col, Hex("f2e6c8"), Mathf.SmoothStep(3f, 1f, da) * 0.85f);
                        }
                        // A whisper of darkening at the island's rim (the land vignette).
                        col *= Mathf.Lerp(0.9f, 1f, Mathf.SmoothStep(0f, 26f, inland));
                        col.a = 1f;
                    }
                    o[i] = col;
                }
            });

            Decorate(o, coast, venues, parks, sprites);

            // Brush strokes and paper grain over everything (one direction, like a painted relief).
            Parallel.For(0, N, y =>
            {
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    var p = At(x, y);
                    float u = (p.x * 0.8f + p.y * 0.6f), v = (-p.x * 0.6f + p.y * 0.8f);
                    float brush = (N_(u / 9f, v / 2.2f) - 0.5f) * 0.05f;
                    float grain = Hash(x, y) * 0.028f - 0.014f + (N_(x / 23f, y / 23f) - 0.5f) * 0.03f;
                    var c = o[i];
                    float g = brush + grain;
                    o[i] = new Color(Mathf.Clamp01(c.r + g), Mathf.Clamp01(c.g + g), Mathf.Clamp01(c.b + g * 0.8f), 1f);
                }
            });
            return o;
        }

        private static Color Ground(AtlasBaker.PlotDef plot, Vector2 p, float h, Color g)
        {
            switch (plot.id)
            {
                case "timeline":
                {
                    float cx = Mathf.Repeat(p.x / 2.2f + (Mathf.FloorToInt(p.y / 1.6f) & 1) * 0.5f, 1f), cy = Mathf.Repeat(p.y / 1.6f, 1f);
                    return cx < 0.08f || cy < 0.1f ? g * 0.9f : Color.Lerp(g, Color.white, (Hash(Mathf.FloorToInt(p.x / 2.2f), Mathf.FloorToInt(p.y / 1.6f)) - 0.5f) * 0.12f);
                }
                case "trenches":
                {
                    // Gritty asphalt: cracks, oil stains, and graffiti tones along the edges.
                    var c = Hex("8f8784");
                    c = Color.Lerp(c, Hex("7a726e"), N_(p.x / 7f, p.y / 7f) * 0.6f);
                    float crack = 1f - Mathf.Abs(N_(p.x / 11f + 4f, p.y / 11f) * 2f - 1f);
                    if (crack > 0.93f) c *= 0.78f;
                    if (N_(p.x / 5f - 3f, p.y / 5f + 8f) > 0.8f) c = Color.Lerp(c, Hex("4a4446"), 0.5f);
                    float edge = -PlotDist(p, plot);
                    if (edge < 12f && edge > 1f)
                    {
                        float tag = N_(p.x / 3.2f, p.y / 3.2f);
                        if (tag > 0.68f)
                        {
                            Color[] spray = { Hex("e0509c"), Hex("3fc5e0"), Hex("f2c14e"), Hex("8f6fe0") };
                            c = Color.Lerp(c, spray[Mathf.Abs(Mathf.FloorToInt(p.x / 9f) * 3 + Mathf.FloorToInt(p.y / 7f)) % 4], 0.55f);
                        }
                    }
                    return c;
                }
                case "airdrop_farm":
                {
                    int patch = Mathf.FloorToInt((p.x + 1000f) / 31f) * 7 + Mathf.FloorToInt((p.y + 1000f) / 25f);
                    Color[] crops = { Hex("e6cf6c"), Hex("a6c85a"), Hex("d2ad5c"), Hex("bcd46a"), Hex("92b84e") };
                    var c = crops[Mathf.Abs(patch) % crops.Length];
                    bool rows = Mathf.Repeat(((patch & 1) == 0 ? p.x : p.y) / 2.2f, 1f) < 0.3f;
                    if (rows) c *= 0.88f;
                    float bx = Mathf.Repeat((p.x + 1000f) / 31f, 1f), by = Mathf.Repeat((p.y + 1000f) / 25f, 1f);
                    if (bx < 0.04f || by < 0.05f) c = Hex("c8a878");   // farm tracks between fields
                    return c;
                }
                case "builder_hills":
                {
                    // Terraced green: contour bands and soft risers.
                    var c = Color.Lerp(Hex("a8d07a"), Hex("86bb5c"), Mathf.Repeat(h * 0.9f + p.y / 30f, 1f) < 0.5f ? 0.0f : 0.55f);
                    float ring = Mathf.Repeat(h * 0.9f + p.y / 30f, 1f);
                    if (ring < 0.06f) c *= 0.82f;
                    return c;
                }
                case "dubai_marina":
                {
                    var c = Hex("f0dfb2");
                    if (p.y < plot.Center.y - 20f)
                    {
                        // Boardwalk planks towards the harbour.
                        bool gap = Mathf.Repeat(p.x / 1.1f, 1f) < 0.12f;
                        c = gap ? Hex("a77f55") : Color.Lerp(Hex("c99a66"), Hex("b98a58"), Hash(Mathf.FloorToInt(p.x / 1.1f), Mathf.FloorToInt(p.y / 6f)));
                    }
                    return c;
                }
                case "residences":
                {
                    // Leafy suburb: lawns, hedge lines and garden paths.
                    var c = Color.Lerp(Hex("a4d374"), Hex("8dc461"), N_(p.x / 14f, p.y / 14f));
                    float gx = Mathf.Repeat(p.x / 14f, 1f), gy = Mathf.Repeat(p.y / 12f, 1f);
                    if ((gx < 0.045f || gy < 0.05f) && N_(p.x / 3f, p.y / 3f) > 0.45f) c = Color.Lerp(c, Hex("5a9a40"), 0.7f);   // broken hedges
                    float lane = Mathf.Repeat(p.y / 36f, 1f);
                    if (lane < 0.05f && N_(p.x / 40f, 3f) > 0.3f) c = Hex("e8d9b6");                                          // a few garden lanes
                    return c;
                }
                default:
                {
                    // Construction lot: dirt, tyre tracks, gravel, a fence at the edge.
                    var c = Color.Lerp(Hex("b9946a"), Hex("a6835c"), N_(p.x / 9f, p.y / 9f));
                    float track = Mathf.Abs(Mathf.Sin(p.x / 9f + Mathf.Sin(p.y / 13f) * 2f));
                    if (track < 0.06f) c *= 0.84f;
                    if (Hash(Mathf.FloorToInt(p.x / 0.6f), Mathf.FloorToInt(p.y / 0.6f)) > 0.93f) c = Color.Lerp(c, Hex("e2d2b4"), 0.5f);
                    float edge = -PlotDist(p, plot);
                    if (edge > 1.2f && edge < 2.6f)
                    {
                        float post = Mathf.Repeat((p.x + p.y) / 3f, 1f);
                        c = post < 0.18f ? Hex("4b3a2c") : Color.Lerp(c, Hex("d9d9d2"), 0.65f);
                    }
                    return c;
                }
            }
        }

        private static Color ParkColor(Park park, Vector2 p, Color under)
        {
            var local = p - park.R.center;
            var lawn = Color.Lerp(Hex("8fd062"), Hex("7cc152"), N_(p.x / 8f, p.y / 8f));
            float loop = Mathf.Abs(new Vector2(local.x / 11f, local.y / 7.5f).magnitude - 1f);
            switch (park.Kind)
            {
                case "timeline":
                {
                    float r = local.magnitude;
                    if (r < 2.6f) return r < 1.9f ? Hex("6cc6e6") : Hex("f6efe0");       // fountain
                    if (r < 9.5f) return Color.Lerp(Hex("eadbbd"), Hex("dccaa4"), Mathf.Repeat(r, 1.6f) < 0.3f ? 1f : 0f);
                    return lawn;
                }
                case "trenches":
                {
                    var c = Hex("cfc9bf");
                    float r = (local - new Vector2(-5f, 0f)).magnitude, r2 = (local - new Vector2(7f, 1f)).magnitude;
                    if (r < 4.5f) c = Color.Lerp(Hex("a8a29a"), Hex("cfc9bf"), r / 4.5f);
                    if (r2 < 3.4f) c = Color.Lerp(Hex("a8a29a"), Hex("cfc9bf"), r2 / 3.4f);
                    if (Mathf.Abs(r - 4.5f) < 0.25f || Mathf.Abs(r2 - 3.4f) < 0.25f) c = Hex("f2efe8");
                    return c;
                }
                case "dubai_marina":
                    if (Mathf.Abs(local.x) < 6f && Mathf.Abs(local.y) < 3.2f) return Mathf.Abs(local.x) > 5.4f || Mathf.Abs(local.y) > 2.6f ? Hex("f6f1e4") : Hex("5fd3e0");
                    return loop < 0.06f ? Hex("efe2c2") : Color.Lerp(Hex("f0dfb2"), lawn, 0.4f);
                case "airdrop_farm":
                    return Mathf.Repeat(local.y / 3.6f, 1f) < 0.2f ? Hex("c8a878") : lawn;
                default:
                {
                    var pond = new Vector2(local.x - 4f, local.y + 1f);
                    float pr = new Vector2(pond.x / 5f, pond.y / 3.2f).magnitude;
                    if (park.Kind == "residences" && pr < 1f) return pr > 0.85f ? Hex("d9cba2") : Color.Lerp(Hex("6ec7df"), Hex("4aa8d0"), pr);
                    return loop < 0.07f ? Hex("efe2c2") : lawn;
                }
            }
        }

        // ------------------------------------------------------------------ objects (sprites and small painted props)

        private struct Tree { public Vector2 P; public string Sprite; public float Size; public float Tint; }

        private static void Decorate(Color[] o, float[] coast, List<(Rect r, string plot, int slot)> venues, List<Park> parks,
            Dictionary<string, AtlasSprites.Sprite> sprites)
        {
            var footprints = venues.Select(v => v.r).ToList();
            var rng = new System.Random(20261017);
            var trees = new List<Tree>();
            // Mostly greens; a birch now and then; autumn maple and blossom as rare accents (ref 8's warm spots).
            string[] mixed = { "tree_common_1", "tree_common_2", "tree_common_4", "tree_common_1", "tree_common_2", "tree_common_4", "tree_common_1",
                "tree_common_4", "tree_birch_1", "tree_common_2", "tree_maple_autumn", "tree_common_1", "tree_blossom_1", "tree_common_4" };
            string[] conifer = { "tree_pine_1", "tree_pine_3" };
            float Coast(Vector2 p) => AtlasPainter.Coast(p);
            bool Clear(Vector2 p, float pad)
            {
                if (Coast(p) > -6f - pad) return false;
                if (RoadDist(p, out _, out _) < 7.5f + pad) return false;
                if ((p - Roundabout).magnitude < 16f) return false;
                if (CanalDist(p, out float al) < CanalHalfWidth(al) + 2.5f + pad) return false;
                if (PathDist(p, footprints) < 2.6f + pad) return false;
                foreach (var r in footprints) if (RectDist(p, r) < 4f + pad) return false;
                return true;
            }
            int PlotAt(Vector2 p, out AtlasBaker.PlotDef plot)
            {
                plot = Nearest(p);
                return PlotDist(p, plot) < -2f ? 1 : 0;
            }
            // Forest clusters and scattered trees over open country (jittered grid).
            for (float gy = -Half; gy < Half; gy += 4.6f)
                for (float gx = -Half; gx < Half; gx += 4.6f)
                {
                    var p = new Vector2(gx + (float)rng.NextDouble() * 3.8f, gy + (float)rng.NextDouble() * 3.8f);
                    if (Coast(p) > -8f) continue;
                    float forest = Fbm(p.x / 46f + 2f, p.y / 46f - 7f);
                    bool inPlot = PlotAt(p, out var plot) == 1;
                    float m = Mountains(p);
                    double chance;
                    if (inPlot)
                    {
                        chance = plot.id == "residences" ? 0.22 : plot.id == "builder_hills" ? (forest > 0.55f ? 0.35 : 0.06) : plot.id == "dubai_marina" ? 0.05 : 0;
                    }
                    else chance = m > 0.3f ? 0.45 : forest > 0.52f ? 0.95 : forest > 0.47f ? 0.45 : 0.05;
                    if (rng.NextDouble() > chance) continue;
                    if (!Clear(p, 0f)) continue;
                    if (parks.Any(pk => pk.R.Contains(p))) continue;
                    string s = inPlot && plot.id == "dubai_marina" ? (rng.Next(2) == 0 ? "palm_1" : "palm_2")
                        : m > 0.3f || (forest > 0.62f && rng.NextDouble() < 0.5) ? conifer[rng.Next(conifer.Length)]
                        : rng.NextDouble() < 0.12 ? "bush_round" : mixed[rng.Next(mixed.Length)];
                    // Open-country canopies are drawn a touch large (stylised, like ref 8); town trees stay closer to life size.
                    float size = s == "bush_round" ? 2.6f + (float)rng.NextDouble() : inPlot ? 4.2f + (float)rng.NextDouble() * 1.8f : 6.0f + (float)rng.NextDouble() * 3.5f;
                    trees.Add(new Tree { P = p, Sprite = s, Size = size, Tint = (float)rng.NextDouble() });
                }
            // Parks: trees round the edge; orchard rows on the farm; palms in the Marina; the islet gets palms.
            foreach (var park in parks)
            {
                if (park.Kind == "trenches") continue;
                int count = park.Kind == "airdrop_farm" ? 0 : 9;
                for (int k = 0; k < count; k++)
                {
                    float a = k / (float)count * Mathf.PI * 2f + (float)rng.NextDouble() * 0.3f;
                    var p = park.R.center + new Vector2(Mathf.Cos(a) * 13f, Mathf.Sin(a) * 9f);
                    string s = park.Kind == "dubai_marina" ? (k % 2 == 0 ? "palm_1" : "palm_2") : k % 3 == 0 ? "tree_blossom_1" : mixed[rng.Next(mixed.Length)];
                    trees.Add(new Tree { P = p, Sprite = s, Size = 4.2f + (float)rng.NextDouble() * 1.6f, Tint = (float)rng.NextDouble() });
                }
                if (park.Kind == "airdrop_farm")
                    for (float yy = park.R.yMin + 2f; yy < park.R.yMax; yy += 3.6f)
                        for (float xx = park.R.xMin + 2f; xx < park.R.xMax; xx += 3.4f)
                            trees.Add(new Tree { P = new Vector2(xx, yy), Sprite = "tree_common_2", Size = 2.8f, Tint = (float)rng.NextDouble() });
            }
            foreach (var v in new[] { new Vector2(252f, -246f), new Vector2(262f, -256f), new Vector2(250f, -259f) })
                trees.Add(new Tree { P = v, Sprite = "palm_2", Size = 5f, Tint = 0.5f });

            // Construction lots: a tower crane, material piles, cabins and cones.
            foreach (var plot in AtlasBaker.Plots.Where(pl => pl.kind == "reserved"))
            {
                var c = plot.Center;
                var rnd = new System.Random(plot.id.GetHashCode());
                for (int k = 0; k < 7; k++)
                {
                    var at = c + new Vector2((float)rnd.NextDouble() * 70f - 35f, (float)rnd.NextDouble() * 70f - 35f);
                    PaintBox(o, at, new Vector2(4f + (float)rnd.NextDouble() * 5f, 2.5f + (float)rnd.NextDouble() * 3f), k % 3 == 0 ? Hex("5a86c8") : k % 3 == 1 ? Hex("c8b08a") : Hex("8b6b4a"), 1.2f);
                }
                for (int k = 0; k < 10; k++) PaintDisc(o, c + new Vector2((float)rnd.NextDouble() * 90f - 45f, -40f + (float)rnd.NextDouble() * 6f), 0.7f, Hex("f07a2a"));
                // Foundations: poured slabs with a rebar grid, a dug pit, stacked materials, a row of site cabins.
                for (int k = 0; k < 2; k++)
                {
                    var at = c + new Vector2(k == 0 ? -22f : 20f, k == 0 ? 18f : -20f);
                    var size = new Vector2(30f, 22f);
                    Fill(o, at, size, (px, d) => d < 0.6f ? Color.Lerp(Hex("c9c3b6"), MapBaker.Ink, 0.6f) : Hex("c9c3b6"));
                    for (float gx = -size.x / 2f + 3f; gx < size.x / 2f; gx += 3f) PaintLine(o, at + new Vector2(gx, -size.y / 2f + 1f), at + new Vector2(gx, size.y / 2f - 1f), 0.12f, Hex("8a6a4a"), 0.8f);
                    for (float gy = -size.y / 2f + 3f; gy < size.y / 2f; gy += 3f) PaintLine(o, at + new Vector2(-size.x / 2f + 1f, gy), at + new Vector2(size.x / 2f - 1f, gy), 0.12f, Hex("8a6a4a"), 0.8f);
                }
                Fill(o, c + new Vector2(24f, 26f), new Vector2(18f, 12f), (px, d) => Color.Lerp(Hex("7d5f42"), Hex("5f4630"), Mathf.Clamp01(d / 4f)));   // the dug pit
                for (int k = 0; k < 5; k++) PaintBox(o, c + new Vector2(-40f + k * 5.2f, -40f), new Vector2(4.4f, 2.6f), Hex("e8e2d4"), 1.4f);           // site cabins
                for (int k = 0; k < 6; k++) PaintBox(o, c + new Vector2(30f + (k % 3) * 3.2f, -34f + (k / 3) * 2.6f), new Vector2(2.8f, 2.2f), Hex("9a7a52"), 0.9f); // timber stacks
                PaintCrane(o, c + new Vector2(4f, 2f), (float)(rnd.NextDouble() * Mathf.PI * 2f));
            }
            // Boats in the Marina harbour and the east bay, with soft wakes.
            var boats = new[] { (Harbour + new Vector2(-8f, 9f), -1.57f, 7f), (Harbour + new Vector2(7f, 8f), -1.6f, 6.5f), (Harbour + new Vector2(-15f, -4f), 0.3f, 8f),
                (Harbour + new Vector2(12f, -10f), 2.4f, 7f), (Harbour + new Vector2(2f, -44f), -1.5f, 10f), (new Vector2(282f, 58f), -1.1f, 8f), (new Vector2(-196f, -270f), 0.9f, 7f) };
            // Piers into the basin from the quay, then the boats.
            foreach (float px in new[] { -14f, 0f, 14f }) PaintBox(o, new Vector2(px, Harbour.y + 16f), new Vector2(2f, 14f), Hex("b98a58"), 0.6f);
            foreach (var (pos, ang, len) in boats) PaintBoat(o, pos, ang, len);

            // Tree shadows first (long, soft, to the south-east), then trees from north to south so nearer ones overlap.
            foreach (var t in trees) StampShadow(o, sprites, t);
            foreach (var t in trees.OrderByDescending(t => t.P.y)) StampTree(o, sprites, t);
        }

        private static void StampTree(Color[] o, Dictionary<string, AtlasSprites.Sprite> sprites, Tree t)
        {
            if (!sprites.TryGetValue(t.Sprite, out var s)) return;
            float widthM = t.Size * 1.16f; // the sprite frames the model with ~16% margin, so the canopy reads at Size
            float px = widthM / Mpp;
            if (px < 3f) return;
            var basePx = new Vector2((t.P.x + Half) / Mpp, (t.P.y + Half) / Mpp);
            float x0 = basePx.x - s.Pivot.x * px, y0 = basePx.y - s.Pivot.y * px;
            var tint = Color.Lerp(new Color(0.94f, 0.98f, 0.92f), new Color(1.06f, 1.02f, 0.94f), t.Tint);
            for (int yy = Mathf.FloorToInt(y0); yy < Mathf.CeilToInt(y0 + px); yy++)
                for (int xx = Mathf.FloorToInt(x0); xx < Mathf.CeilToInt(x0 + px); xx++)
                {
                    if (xx < 0 || yy < 0 || xx >= N || yy >= N) continue;
                    var c = AtlasSprites.Sample(s, (xx + 0.5f - x0) / px, (yy + 0.5f - y0) / px, px);
                    if (c.a < 0.02f) continue;
                    int i = yy * N + xx;
                    var rgb = new Color(Mathf.Clamp01(c.r * tint.r * 1.08f), Mathf.Clamp01(c.g * tint.g * 1.06f), Mathf.Clamp01(c.b * tint.b), 1f);
                    o[i] = Color.Lerp(o[i], rgb, c.a);
                }
        }

        private static void StampShadow(Color[] o, Dictionary<string, AtlasSprites.Sprite> sprites, Tree t)
        {
            // A soft elliptical shadow stretched along the sun direction (to the south-east), longer for taller trees.
            float r = t.Size * 0.5f, len = t.Size * (t.Sprite.StartsWith("tree_pine") ? 1.25f : 0.95f);
            var c = t.P + new Vector2(0.55f, -0.55f) * len;
            int cx = Mathf.RoundToInt((c.x + Half) / Mpp), cy = Mathf.RoundToInt((c.y + Half) / Mpp);
            int rad = Mathf.CeilToInt((len + r) / Mpp);
            var axis = new Vector2(0.7071f, -0.7071f);
            for (int yy = cy - rad; yy <= cy + rad; yy++)
                for (int xx = cx - rad; xx <= cx + rad; xx++)
                {
                    if (xx < 0 || yy < 0 || xx >= N || yy >= N) continue;
                    var p = At(xx, yy) - c;
                    float a = Vector2.Dot(p, axis), b = Vector2.Dot(p, new Vector2(axis.y, -axis.x));
                    float d = new Vector2(a / (len * 0.75f + r * 0.4f), b / (r * 0.95f)).magnitude;
                    if (d >= 1f) continue;
                    int i = yy * N + xx;
                    var col = o[i];
                    o[i] = Color.Lerp(col, new Color(col.r * 0.55f, col.g * 0.56f, col.b * 0.72f, 1f), (1f - d * d) * 0.5f);
                }
        }

        private static void PaintBox(Color[] o, Vector2 center, Vector2 size, Color c, float height)
        {
            // Shadow to the south-east, then the top face lit from the north-west with a thin ink outline.
            Fill(o, center + new Vector2(0.6f, -0.6f) * height, size, (px, d) => Color.Lerp(px, px * new Color(0.6f, 0.58f, 0.72f, 1f), 0.5f));
            Fill(o, center, size, (px, d) => d < 0.35f ? Color.Lerp(c, MapBaker.Ink, 0.55f) : c);
        }

        private static void Fill(Color[] o, Vector2 center, Vector2 size, Func<Color, float, Color> f)
        {
            int x0 = Mathf.FloorToInt((center.x - size.x / 2f + Half) / Mpp), x1 = Mathf.CeilToInt((center.x + size.x / 2f + Half) / Mpp);
            int y0 = Mathf.FloorToInt((center.y - size.y / 2f + Half) / Mpp), y1 = Mathf.CeilToInt((center.y + size.y / 2f + Half) / Mpp);
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(N, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(N, x1); x++)
                {
                    var p = At(x, y) - center;
                    float d = Mathf.Min(size.x / 2f - Mathf.Abs(p.x), size.y / 2f - Mathf.Abs(p.y));
                    if (d < 0) continue;
                    o[y * N + x] = f(o[y * N + x], d);
                }
        }

        private static void PaintDisc(Color[] o, Vector2 center, float r, Color c)
        {
            int cx = Mathf.RoundToInt((center.x + Half) / Mpp), cy = Mathf.RoundToInt((center.y + Half) / Mpp), rad = Mathf.CeilToInt(r / Mpp) + 1;
            for (int y = cy - rad; y <= cy + rad; y++)
                for (int x = cx - rad; x <= cx + rad; x++)
                {
                    if (x < 0 || y < 0 || x >= N || y >= N) continue;
                    float d = (At(x, y) - center).magnitude;
                    if (d < r) o[y * N + x] = Color.Lerp(o[y * N + x], c, Mathf.Clamp01((r - d) / Mpp));
                }
        }

        private static void PaintLine(Color[] o, Vector2 a, Vector2 b, float w, Color c, float alpha = 1f)
        {
            var mn = Vector2.Min(a, b) - Vector2.one * w; var mx = Vector2.Max(a, b) + Vector2.one * w;
            for (int y = Mathf.Max(0, Mathf.FloorToInt((mn.y + Half) / Mpp)); y < Mathf.Min(N, Mathf.CeilToInt((mx.y + Half) / Mpp)); y++)
                for (int x = Mathf.Max(0, Mathf.FloorToInt((mn.x + Half) / Mpp)); x < Mathf.Min(N, Mathf.CeilToInt((mx.x + Half) / Mpp)); x++)
                {
                    float d = Segment(At(x, y), a, b);
                    if (d < w) o[y * N + x] = Color.Lerp(o[y * N + x], c, alpha * Mathf.Clamp01((w - d) / Mpp));
                }
        }

        private static void PaintCrane(Color[] o, Vector2 mast, float angle)
        {
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var tip = mast + dir * 34f; var back = mast - dir * 11f;
            var sh = new Vector2(5f, -5f);   // the crane is tall: its shadow falls well to the south-east
            Color shade = new Color(0.25f, 0.2f, 0.3f);
            PaintLine(o, back + sh, tip + sh, 0.9f, shade, 0.3f);
            PaintLine(o, back, tip, 0.95f, MapBaker.Ink);
            PaintLine(o, back, tip, 0.6f, Hex("f2c230"));
            for (float t = 0.05f; t < 1f; t += 0.07f) PaintDisc(o, Vector2.Lerp(back, tip, t), 0.28f, Hex("b08a1a"));
            PaintBox(o, back, new Vector2(3.2f, 3.2f), Hex("9a9a9a"), 0.5f);        // counterweight
            PaintBox(o, mast, new Vector2(2.6f, 2.6f), Hex("e0a91c"), 4f);          // mast top
            PaintLine(o, Vector2.Lerp(mast, tip, 0.7f), Vector2.Lerp(mast, tip, 0.7f) + new Vector2(0f, -3f), 0.12f, Hex("3a3a3a"));
            PaintBox(o, Vector2.Lerp(mast, tip, 0.7f) + new Vector2(0f, -3.6f), new Vector2(1.6f, 1.2f), Hex("c85a3a"), 0.6f);
        }

        private static void PaintBoat(Color[] o, Vector2 pos, float angle, float len)
        {
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); var side = new Vector2(-dir.y, dir.x);
            float w = len * 0.32f;
            int rad = Mathf.CeilToInt(len * 1.6f / Mpp);
            int cx = Mathf.RoundToInt((pos.x + Half) / Mpp), cy = Mathf.RoundToInt((pos.y + Half) / Mpp);
            for (int y = cy - rad; y <= cy + rad; y++)
                for (int x = cx - rad; x <= cx + rad; x++)
                {
                    if (x < 0 || y < 0 || x >= N || y >= N) continue;
                    var p = At(x, y) - pos;
                    float a = Vector2.Dot(p, dir), b = Vector2.Dot(p, side);
                    int i = y * N + x;
                    // Wake behind the stern.
                    if (a < -len * 0.4f && a > -len * 1.6f && Mathf.Abs(b) < w * (0.6f + (-a - len * 0.4f) / len * 0.6f))
                        o[i] = Color.Lerp(o[i], Color.white, 0.18f * (1f + a / (len * 1.6f)));
                    // Shadow, hull (pointed bow), deck and a little cabin.
                    float ah = a / (len * 0.5f), bow = ah > 0.3f ? 1f - (ah - 0.3f) / 0.7f : 1f;
                    float sa = Vector2.Dot(p - new Vector2(0.5f, -0.5f), dir), sb = Vector2.Dot(p - new Vector2(0.5f, -0.5f), side);
                    float sah = sa / (len * 0.5f), sbow = sah > 0.3f ? 1f - (sah - 0.3f) / 0.7f : 1f;
                    if (Mathf.Abs(sah) < 1f && Mathf.Abs(sb) < w / 2f * Mathf.Max(0, sbow)) o[i] = Color.Lerp(o[i], o[i] * 0.7f, 0.5f);
                    if (Mathf.Abs(ah) < 1f && Mathf.Abs(b) < w / 2f * Mathf.Max(0, bow))
                    {
                        bool rim = Mathf.Abs(b) > w / 2f * Mathf.Max(0, bow) - 0.25f || Mathf.Abs(ah) > 0.95f;
                        bool cabin = ah > -0.45f && ah < 0.15f && Mathf.Abs(b) < w * 0.28f;
                        o[i] = rim ? MapBaker.Ink : cabin ? Hex("8fd0e8") : Hex("fbfaf6");
                    }
                }
        }

        // ------------------------------------------------------------------ noise

        private static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }

        internal static float N_(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float tx = x - xi, ty = y - yi;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        internal static float Fbm(float x, float y)
        {
            float s = 0, a = 0.5f, f = 1, n = 0;
            for (int i = 0; i < 4; i++) { s += N_(x * f, y * f) * a; n += a; a *= 0.5f; f *= 2.03f; }
            return s / n;
        }

        private static float[] Blur(float[] a, int W, int H, int r)
        {
            var t = new float[W * H]; var o = new float[W * H];
            Parallel.For(0, H, y =>
            {
                float sum = 0; int n = 0;
                for (int x = -r; x < W + r; x++)
                {
                    if (x + r < W) { sum += a[y * W + x + r]; n++; }
                    if (x - r - 1 >= 0) { sum -= a[y * W + x - r - 1]; n--; }
                    if (x >= 0 && x < W) t[y * W + x] = sum / Mathf.Max(1, n);
                }
            });
            Parallel.For(0, W, x =>
            {
                float sum = 0; int n = 0;
                for (int y = -r; y < H + r; y++)
                {
                    if (y + r < H) { sum += t[(y + r) * W + x]; n++; }
                    if (y - r - 1 >= 0) { sum -= t[(y - r - 1) * W + x]; n--; }
                    if (y >= 0 && y < H) o[y * W + x] = sum / Mathf.Max(1, n);
                }
            });
            return o;
        }
    }
}
