using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.UI.Map
{
    /// <summary>
    /// One baked area map (MapBaker): a venue diorama rendered from straight above. Coordinates are venue-local metres
    /// (x east, z north, as <see cref="CTLife.World.VenueSceneRoot.ToLocal"/> returns them); uv = (local - Min) / Size after
    /// undoing <see cref="RotationDeg"/> about the centre.
    /// </summary>
    public sealed class MapArea
    {
        public string Id, VenueId, HomeId, District, Title, Texture;
        public Vector2 Min, Size;
        public float RotationDeg;
        public Vector2Int Pixels;
        public Rect Walkable;
        public Vector2 Center => Min + Size / 2f;
        public Rect Rect => new Rect(Min, Size);
        public bool IsHome => !string.IsNullOrEmpty(HomeId);
    }

    /// <summary>
    /// Where an area sits on CT Island: atlas = Origin + R(rotation) * local * Scale, rotation counter-clockwise about the venue
    /// root (Origin). atlas.json v2 (World lane, 2026-10-17) names the venue yaw <c>rotationDeg</c> (clockwise seen from above,
    /// Unity's +Y, the root's eulerAngles.y) and keeps the legacy <c>rotation</c> = -rotationDeg; both give the same placement.
    /// </summary>
    public sealed class MapPlacement
    {
        public string AreaId, Plot;
        public Vector2 Origin;
        public float RotationDeg, Scale = 1f;
        /// <summary>v2 convenience: where the area centre lands on the island (derived by the math; tests check they agree).</summary>
        public Vector2? Center;
    }

    public sealed class MapPlot
    {
        public string Id, Name, Kind;
        public Vector2 Center;
        public bool Reserved => Kind == "reserved";
    }

    /// <summary>The CT Island overworld (AtlasBaker): the painted island plus where every area sits on it.</summary>
    public sealed class MapAtlas
    {
        public string Name, Texture;
        public Vector2 Min, Size;
        public float PlotSize;
        public readonly List<MapPlot> Plots = new List<MapPlot>();
        public readonly Dictionary<string, MapPlacement> Placements = new Dictionary<string, MapPlacement>();
        public Rect Rect => new Rect(Min, Size);
    }

    /// <summary>Loads the baked map data from Resources/UI/Map (areas.json, atlas.json) once; textures load on demand.</summary>
    public static class MapCatalog
    {
        private static Dictionary<string, MapArea> _areas;
        private static MapAtlas _atlas;
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

        public static IReadOnlyDictionary<string, MapArea> Areas { get { Load(); return _areas; } }
        public static MapAtlas Atlas { get { Load(); return _atlas; } }

        /// <summary>Tests: parse from strings instead of Resources.</summary>
        public static void LoadFrom(string areasJson, string atlasJson)
        {
            _areas = ParseAreas(areasJson);
            _atlas = ParseAtlas(atlasJson);
        }

        public static void Reset() { _areas = null; _atlas = null; }

        private static void Load()
        {
            if (_areas != null) return;
            var a = Resources.Load<TextAsset>("UI/Map/areas");
            var b = Resources.Load<TextAsset>("UI/Map/atlas");
            _areas = a != null ? ParseAreas(a.text) : new Dictionary<string, MapArea>();
            _atlas = b != null ? ParseAtlas(b.text) : new MapAtlas { Min = new Vector2(-300, -300), Size = new Vector2(600, 600) };
        }

        /// <summary>
        /// Warm every map texture in the background (Resources.LoadAsync), so the first full-map open does not load ~25
        /// textures in one frame (the Seeker showed one 12 fps window without it). Safe to call more than once.
        /// </summary>
        public static void Preload()
        {
            if (_preloading) return;
            _preloading = true;
            var paths = new List<string> { Atlas.Texture };
            foreach (var a in Areas.Values) paths.Add(a.Texture);
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path) || Textures.ContainsKey(path)) continue;
                var p = path;
                var req = Resources.LoadAsync<Texture2D>(p);
                req.completed += _ => { if (req.asset is Texture2D t && !Textures.ContainsKey(p)) Textures[p] = t; };
            }
        }

        private static bool _preloading;

        public static Texture2D Texture(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Textures.TryGetValue(path, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>(path);
            Textures[path] = t;
            return t;
        }

        /// <summary>The area for a visit: a public venue by id, or the player's own home by its home type.</summary>
        public static MapArea ForVisit(string venueId, string homeId)
        {
            if (!string.IsNullOrEmpty(homeId) && Areas.TryGetValue("home_" + homeId, out var home)) return home;
            return !string.IsNullOrEmpty(venueId) && Areas.TryGetValue(venueId, out var a) ? a : null;
        }

        public static MapPlacement PlacementOf(MapArea a) => a != null && Atlas.Placements.TryGetValue(a.Id, out var p) ? p : null;

        private static Vector2 V(JToken t) => t is JArray a && a.Count >= 2 ? new Vector2((float)a[0], (float)a[1]) : Vector2.zero;

        private static Dictionary<string, MapArea> ParseAreas(string json)
        {
            var d = new Dictionary<string, MapArea>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(json)) return d;
            foreach (var t in (JArray)JObject.Parse(json)["areas"] ?? new JArray())
            {
                var w = t["walkable"] as JArray;
                var a = new MapArea
                {
                    Id = (string)t["id"], VenueId = (string)t["venueId"], HomeId = (string)t["homeId"], District = (string)t["district"],
                    Title = (string)t["title"], Texture = (string)t["texture"], Min = V(t["min"]), Size = V(t["size"]),
                    RotationDeg = (float?)t["rotation"] ?? 0f,
                    Pixels = new Vector2Int((int?)t["pixels"]?[0] ?? 0, (int?)t["pixels"]?[1] ?? 0),
                    Walkable = w != null && w.Count == 4 ? Rect.MinMaxRect((float)w[0], (float)w[1], (float)w[2], (float)w[3]) : default,
                };
                if (!string.IsNullOrEmpty(a.Id)) d[a.Id] = a;
            }
            return d;
        }

        private static MapAtlas ParseAtlas(string json)
        {
            var at = new MapAtlas();
            if (string.IsNullOrEmpty(json)) return at;
            var o = JObject.Parse(json);
            at.Name = (string)o["name"]; at.Texture = (string)o["texture"];
            at.Min = V(o["min"]); at.Size = V(o["size"]); at.PlotSize = (float?)o["plotSize"] ?? 120f;
            foreach (var p in (JArray)o["plots"] ?? new JArray())
                at.Plots.Add(new MapPlot { Id = (string)p["id"], Name = (string)p["name"], Kind = (string)p["kind"], Center = V(p["center"]) });
            foreach (var p in (JArray)o["areas"] ?? new JArray())
            {
                var pl = new MapPlacement { AreaId = (string)p["id"], Plot = (string)p["plot"], Origin = V(p["origin"]),
                    RotationDeg = p["rotationDeg"] != null ? -(float)p["rotationDeg"] : (float?)p["rotation"] ?? 0f, Scale = (float?)p["scale"] ?? 1f,
                    Center = p["center"] is JArray c && c.Count >= 2 ? V(c) : (Vector2?)null };
                at.Placements[pl.AreaId] = pl;
            }
            return at;
        }
    }
}
