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
                venues = new[] { "home_studio", "home_hacker_house", "home_penthouse", "home_marina_villa", "home_parents_basement" } },
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
            var venues = new List<(Rect r, string plot, int slot)>();
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
                    venues.Add((new Rect(origin + min, size), plot.id, s - 1));
                }
            }

            // Art pass (2026-10-17): the painted island lives in AtlasPainter; the layout above is unchanged, so venue rects stay put.
            var sprites = AtlasSprites.Render();
            var px = AtlasPainter.Paint(venues, sprites);
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
