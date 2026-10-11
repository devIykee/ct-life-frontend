using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// CT Island: the overworld map (map/HUD job, 2026-10-17). The city's venues are separate dioramas reached by travel, so the
    /// atlas places them as neighbourhoods of one bounded island. Each venue's baked map (MapBaker) sits at true scale in its
    /// district plot; coast, water, streets, trees and plot grounds are painted here (procedural, deterministic, no imagery).
    ///
    /// Layout decision (scales like a GTA-style expansion without becoming endless): a centre-anchored grid of 120 m district
    /// plots with 24 m streets. Today 3 x 3: Timeline downtown in the middle, Builder Hills north, The Trenches west, Airdrop
    /// Farm east, Dubai Marina on the south coast, private homes in the south-west Residences plot, and three reserved plots
    /// (NW, NE, SE) shown as fenced "coming soon" lots. New districts fill reserved plots first; after that the island grows
    /// by one ring (5 x 5), re-baked, while every existing plot keeps its coordinates (the grid is anchored at the centre).
    /// Each plot holds up to six venues (3 x 2 slots, 38 m apart).
    ///
    /// Output: Assets/Resources/UI/Map/Atlas/ct-island.png and Assets/Resources/UI/Map/atlas.json. Batch entry:
    ///   Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.AtlasBaker.Run
    /// </summary>
    public static class AtlasBaker
    {
        public const string AtlasPng = MapBaker.Folder + "/Atlas/ct-island.png";
        public const string AtlasJson = MapBaker.Folder + "/atlas.json";
        public const float Plot = 120f, Street = 24f, Pitch = Plot + Street;
        public const int Size = 2048;
        public const float Half = 300f;          // the atlas covers [-300, 300] m both ways (island plus surrounding sea)
        public const float CoastHalf = 258f;     // island half-extent before the coastline noise

        /// <summary>A district plot on the grid: (col, row) from the centre, east and north positive.</summary>
        public sealed class PlotDef
        {
            public string id, name, kind;   // kind: district | residences | reserved
            public int col, row;
            public Color ground;
            public string[] venues = Array.Empty<string>();
            public Vector2 Center => new Vector2(col * Pitch, row * Pitch);
        }

        public static readonly PlotDef[] Plots =
        {
            new PlotDef { id = "timeline", name = "Timeline District", kind = "district", col = 0, row = 0, ground = Hex("ead6b4"),
                venues = new[] { "timeline_square", "spaces_studio", "discord_heights", "ratio_arena", "ct_jail" } },
            new PlotDef { id = "builder_hills", name = "Builder Hills", kind = "district", col = 0, row = 1, ground = Hex("a9cf7c"),
                venues = new[] { "conference_hall", "hackathon_house", "audit_row", "vc_row" } },
            new PlotDef { id = "trenches", name = "The Trenches", kind = "district", col = -1, row = 0, ground = Hex("c7b095"),
                venues = new[] { "terminal_cafe", "perp_pit", "rekt_clinic", "launchpad_tower", "prediction_pit" } },
            new PlotDef { id = "airdrop_farm", name = "Airdrop Farm", kind = "district", col = 1, row = 0, ground = Hex("d9c873"),
                venues = new[] { "airdrop_fields", "ore_refinery", "sybil_barn", "checker_kiosk" } },
            new PlotDef { id = "dubai_marina", name = "Dubai Marina", kind = "district", col = 0, row = -1, ground = Hex("efdcae"),
                venues = new[] { "yacht_club", "cabal_lounge", "penthouse_row" } },
            new PlotDef { id = "residences", name = "Residences", kind = "residences", col = -1, row = -1, ground = Hex("b6d88a"),
                venues = new[] { "home_studio", "home_hacker_house", "home_penthouse", "home_marina_villa" } },
            new PlotDef { id = "reserved_nw", name = "Coming soon", kind = "reserved", col = -1, row = 1, ground = Hex("d6c29a") },
            new PlotDef { id = "reserved_ne", name = "Coming soon", kind = "reserved", col = 1, row = 1, ground = Hex("d6c29a") },
            new PlotDef { id = "reserved_se", name = "Coming soon", kind = "reserved", col = 1, row = -1, ground = Hex("d6c29a") },
        };

        /// <summary>Slot centres inside a plot (metres from the plot centre): 3 columns x 2 rows, the first slot in the middle top.</summary>
        public static readonly Vector2[] Slots =
        {
            new Vector2(0, 24), new Vector2(-38, 24), new Vector2(38, 24), new Vector2(0, -26), new Vector2(-38, -26), new Vector2(38, -26),
        };

        private static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

        public static void Run()
        {
            string fatal = null;
            try { Bake(); }
            catch (Exception e) { fatal = e.Message; Debug.LogException(e); }
            Debug.Log($"CTLIFE [atlasbake] {(fatal == null ? "PASS" : "FAIL " + fatal)}");
            if (Application.isBatchMode) EditorApplication.Exit(fatal == null ? 0 : 1);
        }

        public static void Bake()
        {
            var areasDoc = JObject.Parse(File.ReadAllText(MapBaker.AreasJson));
            var areas = ((JArray)areasDoc["areas"]).ToDictionary(a => (string)a["id"], a => (JObject)a);
            var placements = new JArray();
            var footprints = new List<Rect>();
            foreach (var plot in Plots)
            {
                int s = 0;
                foreach (var id in plot.venues)
                {
                    if (!areas.TryGetValue(id, out var a)) continue;   // a venue without shipped content has no map yet
                    var min = new Vector2((float)a["min"][0], (float)a["min"][1]);
                    var size = new Vector2((float)a["size"][0], (float)a["size"][1]);
                    var slot = plot.Center + Slots[s++];
                    var origin = slot - (min + size / 2f);                // atlas = origin + local
                    placements.Add(new JObject
                    {
                        ["id"] = id, ["plot"] = plot.id, ["origin"] = new JArray(Math.Round(origin.x, 3), Math.Round(origin.y, 3)),
                        ["rotation"] = 0, ["scale"] = 1,
                    });
                    footprints.Add(new Rect(origin + min, size));
                }
            }

            var px = Paint(footprints);
            MapBaker.SavePng(px, Size, Size, AtlasPng);
            var doc = new JObject
            {
                ["version"] = 1,
                ["name"] = "CT Island",
                ["note"] = "Generated by AtlasBaker. Atlas metres: x east, y north. uv = (p - min) / size. An area's venue-local point " +
                           "(x, z) maps to atlas origin + (x, z) (rotation 0, scale 1). Plots: centre-anchored grid (pitch 144 m).",
                ["texture"] = "UI/Map/Atlas/ct-island",
                ["min"] = new JArray(-Half, -Half), ["size"] = new JArray(2 * Half, 2 * Half), ["pixels"] = new JArray(Size, Size),
                ["plotSize"] = Plot, ["pitch"] = Pitch,
                ["expansion"] = "Fill reserved plots (NW, NE, SE) first; then grow one ring to 5 x 5 and re-bake. Existing plots never move.",
                ["plots"] = new JArray(Plots.Select(p => new JObject
                {
                    ["id"] = p.id, ["name"] = p.name, ["kind"] = p.kind, ["col"] = p.col, ["row"] = p.row,
                    ["center"] = new JArray(p.Center.x, p.Center.y),
                })),
                ["areas"] = placements,
            };
            File.WriteAllText(AtlasJson, doc.ToString());
            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------------ painting

        private static Color[] Paint(List<Rect> footprints)
        {
            var o = new Color[Size * Size];
            float mpp = 2 * Half / Size;            // metres per pixel
            Color deep = Hex("3f8fc6"), shallow = Hex("7cc8e2"), foam = Hex("eaf6f2"), sand = Hex("ecd8a3"), sandWet = Hex("d9c08a");
            Color grassA = Hex("8fc45a"), grassB = Hex("74ad4a"), road = Hex("f2e4c4"), roadEdge = Hex("b99c76"), lane = Hex("fffaf0");
            var trees = Grid(Trees(footprints));
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2(-Half + (x + 0.5f) * mpp, -Half + (y + 0.5f) * mpp);
                    float coast = Coast(p);                    // < 0 inside the island (metres)
                    Color c;
                    if (coast > 0f)
                    {
                        float t = Mathf.Clamp01(coast / 55f);
                        c = Color.Lerp(shallow, deep, Mathf.SmoothStep(0, 1, t));
                        float ripple = Mathf.Sin(coast * 0.55f + Noise(p.x / 40f, p.y / 40f) * 6f);
                        if (coast < 30f && ripple > 0.92f) c = Color.Lerp(c, foam, 0.35f * (1f - coast / 30f));
                        if (coast < 2.2f) c = Color.Lerp(c, foam, 0.8f * (1f - coast / 2.2f));
                    }
                    else
                    {
                        float inland = -coast;
                        float n = Fbm(p.x / 60f, p.y / 60f);
                        c = Color.Lerp(grassA, grassB, Mathf.SmoothStep(0.35f, 0.75f, n));
                        // Gentle hills (shaded from the north-west like the venue maps).
                        float hx = Fbm((p.x + 3f) / 90f, p.y / 90f) - Fbm((p.x - 3f) / 90f, p.y / 90f);
                        float hy = Fbm(p.x / 90f, (p.y + 3f) / 90f) - Fbm(p.x / 90f, (p.y - 3f) / 90f);
                        float shade = 1f + (-hx + hy) * 1.6f;
                        c *= Mathf.Clamp(shade, 0.86f, 1.12f);
                        if (inland < 12f) c = Color.Lerp(inland < 4f ? sandWet : sand, c, Mathf.SmoothStep(8f, 12f, inland));

                        foreach (var plot in Plots)
                        {
                            float d = RoundRectDist(p - plot.Center, new Vector2(Plot / 2f, Plot / 2f), 26f) + (Fbm(p.x / 22f + 11f, p.y / 22f) - 0.5f) * 9f;
                            if (d > 2f) continue;
                            float k = Mathf.SmoothStep(2f, -4f, d);
                            Color g = plot.ground;
                            if (plot.id == "airdrop_farm") g = Fields(p, g);
                            if (plot.id == "timeline") g = Cobble(p, g);
                            if (plot.id == "trenches") g = Color.Lerp(g, g * 0.86f, Mathf.SmoothStep(0.55f, 0.7f, Fbm(p.x / 9f, p.y / 9f)));
                            if (plot.id == "dubai_marina") g = Color.Lerp(g, Hex("d8bf8c"), Mathf.Repeat(p.y / 1.6f, 1f) < 0.12f && Mathf.Abs(Mathf.Repeat(p.x / 14f, 1f) - 0.5f) < 0.48f ? 0.35f : 0f);
                            if (plot.id == "builder_hills") g = Color.Lerp(g, grassB, Fbm(p.x / 25f, p.y / 25f) * 0.5f);
                            if (plot.kind == "reserved")
                            {
                                bool hatch = Mathf.Repeat((p.x + p.y) / 6f, 1f) < 0.12f;
                                if (hatch) g = Color.Lerp(g, roadEdge, 0.35f);
                                if (d > -2.2f && d < -0.6f && Mathf.Repeat((p.x - p.y) / 5f, 1f) < 0.55f) g = Hex("7a5a3a"); // dashed fence
                            }
                            c = Color.Lerp(c, g, k * (plot.kind == "reserved" ? 0.85f : 1f));
                        }
                        // Marina harbour: water cuts into the south of the marina plot.
                        // Streets: the grid gaps across the island plus a ring road, cream with soft edges and a dashed centre.
                        float sd = StreetDist(p);
                        if (sd < 7.5f)
                        {
                            c = Color.Lerp(c, roadEdge, Mathf.SmoothStep(7.5f, 6.0f, sd));
                            c = Color.Lerp(c, road, Mathf.SmoothStep(6.0f, 5.0f, sd));
                            if (sd < 0.45f && Mathf.Repeat((p.x + p.y) / 7f, 1f) < 0.5f) c = Color.Lerp(c, lane, 0.8f);
                        }
                        // Footpaths: from each venue's door (its south edge, where every venue's entrance spot is) to the street.
                        float pd = PathDist(p, footprints);
                        if (pd < 2.6f)
                        {
                            c = Color.Lerp(c, Hex("e9d5ac"), Mathf.SmoothStep(2.6f, 1.6f, pd));
                            if (pd < 0.5f && Mathf.Repeat(p.y / 3f, 1f) < 0.4f) c = Color.Lerp(c, roadEdge, 0.35f);
                        }
                        // Pads under each venue map: a soft drop shadow toward the south-east and a light paved apron.
                        foreach (var r in footprints)
                        {
                            float ds = RectDist(p - new Vector2(1.8f, -1.8f), r);
                            if (ds < 6f) c = Color.Lerp(c, c * new Color(0.62f, 0.6f, 0.72f, 1f), Mathf.SmoothStep(6f, 0f, ds) * 0.55f);
                            float da = RectDist(p, r);
                            if (da < 3f && da > -0.5f) c = Color.Lerp(c, road, Mathf.SmoothStep(3f, 1f, da) * 0.8f);
                        }
                    }
                    c = TreeOver(c, p, trees);
                    // Paper grain over everything.
                    float grain = Hash(x, y) * 0.03f - 0.015f + (Noise(x / 29f, y / 29f) - 0.5f) * 0.035f;
                    o[y * Size + x] = new Color(Mathf.Clamp01(c.r + grain), Mathf.Clamp01(c.g + grain), Mathf.Clamp01(c.b + grain * 0.8f), 1f);
                }
            return o;
        }

        private static Color Fields(Vector2 p, Color g)
        {
            // Crop strips in patches: yellow wheat, green rows, ochre stubble.
            int patch = Mathf.FloorToInt((p.x + 1000f) / 37f) * 7 + Mathf.FloorToInt((p.y + 1000f) / 29f);
            Color[] crops = { Hex("e2cf72"), Hex("a8c85e"), Hex("cfae62"), Hex("bcd36d") };
            var c = crops[Mathf.Abs(patch) % crops.Length];
            bool row = Mathf.Repeat(((patch & 1) == 0 ? p.x : p.y) / 2.4f, 1f) < 0.32f;
            return row ? c * 0.9f : c;
        }

        private static Color Cobble(Vector2 p, Color g)
        {
            // Downtown paving: soft stone cells with mortar lines.
            float cx = Mathf.Repeat(p.x / 2.2f + (Mathf.FloorToInt(p.y / 1.6f) & 1) * 0.5f, 1f), cy = Mathf.Repeat(p.y / 1.6f, 1f);
            bool mortar = cx < 0.08f || cy < 0.1f;
            return mortar ? g * 0.9f : Color.Lerp(g, Color.white, (Hash(Mathf.FloorToInt(p.x / 2.2f), Mathf.FloorToInt(p.y / 1.6f)) - 0.5f) * 0.12f);
        }

        private static float PathDist(Vector2 p, List<Rect> footprints)
        {
            float best = float.MaxValue;
            foreach (var r in footprints)
            {
                // From the south edge centre (the door) straight down: top-row venues to the plot's middle lane, bottom-row
                // venues to the street below.
                float plotY = Mathf.Round(r.center.y / Pitch) * Pitch;
                float x = r.center.x, y0 = r.yMin, y1 = r.center.y > plotY ? plotY : plotY - Pitch / 2f;
                if (p.y > y0 + 0.5f || p.y < y1) continue;
                best = Mathf.Min(best, Mathf.Abs(p.x - x));
            }
            // The middle lane of every plot that has venues, joining the side streets.
            foreach (var r in footprints)
            {
                float plotX = Mathf.Round(r.center.x / Pitch) * Pitch, plotY = Mathf.Round(r.center.y / Pitch) * Pitch;
                if (Mathf.Abs(p.x - plotX) <= Pitch / 2f) best = Mathf.Min(best, Mathf.Abs(p.y - plotY));
            }
            return best;
        }

        /// <summary>Signed distance to the coastline (metres; negative inland): a rounded square with low-frequency wobble.</summary>
        public static float Coast(Vector2 p)
        {
            float d = RoundRectDist(p, new Vector2(CoastHalf, CoastHalf), 110f);
            d += (Fbm(p.x / 140f + 7.3f, p.y / 140f - 2.1f) - 0.5f) * 46f + (Noise(p.x / 35f, p.y / 35f) - 0.5f) * 8f;
            // Marina harbour inlet on the south coast, below the marina plot.
            var h = p - new Vector2(10f, -Pitch - Plot / 2f - 2f);
            float inlet = RoundRectDist(h, new Vector2(30f, 24f), 22f) + (Noise(p.x / 12f, p.y / 12f) - 0.5f) * 5f;
            return Mathf.Max(d, -inlet);
        }

        private static float StreetDist(Vector2 p)
        {
            // A gentle meander keeps the grid from reading as a board game.
            p += new Vector2(Mathf.Sin(p.y / 41f) * 2.2f, Mathf.Sin(p.x / 37f) * 2.2f);
            float best = float.MaxValue;
            foreach (float g in new[] { -Pitch / 2f, Pitch / 2f, -Pitch * 1.5f, Pitch * 1.5f })
            {
                if (Mathf.Abs(p.y) < Pitch * 1.5f + 4f) best = Mathf.Min(best, Mathf.Abs(p.x - g));
                if (Mathf.Abs(p.x) < Pitch * 1.5f + 4f) best = Mathf.Min(best, Mathf.Abs(p.y - g));
            }
            return best;
        }

        private static float RoundRectDist(Vector2 p, Vector2 half, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x) - half.x + r, Mathf.Abs(p.y) - half.y + r);
            return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r;
        }

        private static float RectDist(Vector2 p, Rect r)
        {
            var c = r.center; var half = r.size / 2f;
            return RoundRectDist(p - c, half, 0.01f);
        }

        // ------------------------------------------------------------------ trees (little painted 3D objects, ref 8/9)

        private struct Tree { public Vector2 p; public float r; public Color c; }

        private static List<Tree> Trees(List<Rect> footprints)
        {
            var list = new List<Tree>();
            var rng = new System.Random(20261017);
            Color[] leaf = { Hex("3f8a3c"), Hex("4e9a3f"), Hex("2f7a4a"), Hex("5aa648"), Hex("d98a3a"), Hex("e3b23c") };
            for (int i = 0; i < 40000 && list.Count < 3200; i++)
            {
                var p = new Vector2((float)(rng.NextDouble() * 2 - 1) * CoastHalf * 1.05f, (float)(rng.NextDouble() * 2 - 1) * CoastHalf * 1.05f);
                float coast = Coast(p);
                if (coast > -16f) continue;
                if (StreetDist(p) < 11f) continue;
                bool inPlot = Plots.Any(pl => RoundRectDist(p - pl.Center, new Vector2(Plot / 2f, Plot / 2f), 26f) < 6f);
                if (inPlot && !(Fbm(p.x / 30f, p.y / 30f) > 0.58f && Plots.Any(pl => (pl.id == "builder_hills" || pl.id == "residences") && RoundRectDist(p - pl.Center, new Vector2(Plot / 2f, Plot / 2f), 26f) < 0f))) continue;
                if (footprints.Any(f => RectDist(p, f) < 6f) || PathDist(p, footprints) < 4f) continue;
                // Clumps: denser where the woodland noise is high (the ref's forests), sparse elsewhere.
                if (Fbm(p.x / 55f + 3f, p.y / 55f) < 0.44f && rng.NextDouble() > 0.12) continue;
                float r = 2.6f + (float)rng.NextDouble() * 2.6f;
                if (list.Any(t => (t.p - p).sqrMagnitude < (t.r + r) * (t.r + r) * 0.55f)) continue;
                int ci = rng.NextDouble() < 0.08 ? 4 + rng.Next(2) : rng.Next(4);
                list.Add(new Tree { p = p, r = r, c = leaf[ci] });
            }
            return list;
        }

        private const float Cell = 16f;

        private static Dictionary<(int, int), List<Tree>> Grid(List<Tree> trees)
        {
            var g = new Dictionary<(int, int), List<Tree>>();
            foreach (var t in trees)
            {
                var k = (Mathf.FloorToInt(t.p.x / Cell), Mathf.FloorToInt(t.p.y / Cell));
                if (!g.TryGetValue(k, out var l)) g[k] = l = new List<Tree>();
                l.Add(t);
            }
            return g;
        }

        private static readonly List<Tree> Near = new List<Tree>();

        private static Color TreeOver(Color c, Vector2 p, Dictionary<(int, int), List<Tree>> grid)
        {
            Near.Clear();
            int cx = Mathf.FloorToInt(p.x / Cell), cy = Mathf.FloorToInt(p.y / Cell);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (grid.TryGetValue((cx + dx, cy + dy), out var l)) Near.AddRange(l);
            if (Near.Count == 0) return c;
            var trees = Near;
            foreach (var t in trees)
            {
                var d = p - t.p;
                if (Mathf.Abs(d.x) > t.r * 2.2f || Mathf.Abs(d.y) > t.r * 2.2f) continue;
                // Long soft shadow to the south-east.
                var s = d - new Vector2(t.r * 0.9f, -t.r * 0.9f);
                float sd = s.magnitude / (t.r * 1.05f);
                if (sd < 1f) c = Color.Lerp(c, c * new Color(0.6f, 0.6f, 0.74f, 1f), (1f - sd) * 0.7f);
            }
            foreach (var t in trees)
            {
                var d = p - t.p;
                float dist = d.magnitude / t.r;
                if (dist > 1f) continue;
                // Canopy: lit from the north-west, darker rim, a thin ink outline.
                var nrm = d / t.r;
                float lit = Mathf.Clamp01(0.62f + (-nrm.x + nrm.y) * 0.32f - dist * dist * 0.25f);
                var leaf = Color.Lerp(t.c * 0.72f, Color.Lerp(t.c, Color.white, 0.18f), lit);
                if (dist > 0.88f) leaf = Color.Lerp(leaf, MapBaker.Ink, 0.55f);
                c = leaf;
            }
            return c;
        }

        // ------------------------------------------------------------------ noise

        private static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }

        private static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float tx = x - xi, ty = y - yi;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        private static float Fbm(float x, float y)
        {
            float s = 0, a = 0.5f, f = 1, n = 0;
            for (int i = 0; i < 4; i++) { s += Noise(x * f, y * f) * a; n += a; a *= 0.5f; f *= 2.03f; }
            return s / n;
        }
    }
}
