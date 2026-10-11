using System.Linq;
using CTLife.UI;
using CTLife.UI.Map;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>Map/HUD job (2026-10-17): the world-to-map chain, minimap rotation and clamping, the tilted view, baked data.</summary>
    public class MapTests
    {
        private const float Eps = 1e-4f;

        private static MapArea Area(float rot = 0f) => new MapArea
        {
            Id = "t", Min = new Vector2(-16.5f, -17.2f), Size = new Vector2(33f, 34.4f), RotationDeg = rot, Pixels = new Vector2Int(660, 688),
        };

        private static void Near(Vector2 a, Vector2 b, float eps = Eps, string msg = null) =>
            Assert.Less((a - b).magnitude, eps, (msg ?? "") + " expected " + b + " got " + a);

        [Test]
        public void AreaUv_RoundTrips_WithAndWithoutRotation()
        {
            foreach (var rot in new[] { 0f, 30f, -90f })
            {
                var a = Area(rot);
                foreach (var p in new[] { new Vector2(0, 0), new Vector2(3.2f, -11.9f), new Vector2(-16.5f, -17.2f), new Vector2(9.1f, 8.6f) })
                    Near(MapMath.LocalFromAreaUv(a, MapMath.AreaUv(a, p)), p, Eps, "rot " + rot);
            }
            var flat = Area();
            Near(MapMath.AreaUv(flat, flat.Min), Vector2.zero);
            Near(MapMath.AreaUv(flat, flat.Min + flat.Size), Vector2.one);
            Near(MapMath.AreaUv(flat, flat.Center), new Vector2(0.5f, 0.5f));
        }

        [Test]
        public void Atlas_RoundTrips_AndPlacesTheAreaFrame()
        {
            var p = new MapPlacement { Origin = new Vector2(12f, -150f), RotationDeg = 0f, Scale = 1f };
            Near(MapMath.AtlasFromLocal(p, new Vector2(1, 2)), new Vector2(13, -148));
            var q = new MapPlacement { Origin = new Vector2(-40f, 25f), RotationDeg = 90f, Scale = 2f };
            Near(MapMath.AtlasFromLocal(q, new Vector2(1, 0)), new Vector2(-40f, 27f), 1e-3f);
            foreach (var l in new[] { Vector2.zero, new Vector2(-7.5f, 3.25f), new Vector2(10f, -12f) })
                Near(MapMath.LocalFromAtlas(q, MapMath.AtlasFromLocal(q, l)), l, 1e-3f);
            var a = Area();
            var r = MapSurface.AreaAtlasRect(a, p);
            Near(r.min, p.Origin + a.Min); Near(r.size, a.Size);
        }

        // ------------------------------------------------------------------ rotated venues (atlas.json v2, World lane 2026-10-17)

        private static readonly string RotatedAreas = new JObject
        {
            ["areas"] = new JArray(new JObject
            {
                ["id"] = "rot", ["venueId"] = "rot", ["texture"] = "x", ["min"] = new JArray(-20f, -10f), ["size"] = new JArray(40f, 30f),
                ["pixels"] = new JArray(400, 300),
            }),
        }.ToString();

        private static Vector2Int Pixel(Vector2 uv, MapArea a) => new Vector2Int(Mathf.FloorToInt(uv.x * a.Pixels.x), Mathf.FloorToInt(uv.y * a.Pixels.y));

        [Test]
        public void RotatedVenue_MarkerLandsOnItsTexturePixel()
        {
            foreach (var yaw in new[] { 30f, 90f, -47.5f, 180f })
                foreach (var legacyOnly in new[] { false, true })
                {
                    var origin = new Vector2(212f, -64f);
                    var root = new GameObject("venue-root");   // the venue root as World places it: Unity yaw, clockwise seen from above
                    try
                    {
                        root.transform.SetPositionAndRotation(new Vector3(origin.x, 3f, origin.y), Quaternion.Euler(0f, yaw, 0f));
                        var centre = root.transform.TransformPoint(new Vector3(0f, 0f, 5f));   // the area centre, local (0, 5)
                        var entry = new JObject { ["id"] = "rot", ["origin"] = new JArray(origin.x, origin.y), ["rotation"] = -yaw, ["scale"] = 1f };
                        if (!legacyOnly) { entry["rotationDeg"] = yaw; entry["center"] = new JArray(centre.x, centre.z); }
                        MapCatalog.LoadFrom(RotatedAreas, new JObject
                        {
                            ["min"] = new JArray(-800f, -600f), ["size"] = new JArray(1600f, 1200f), ["areas"] = new JArray(entry),
                        }.ToString());
                        var a = MapCatalog.Areas["rot"];
                        var p = MapCatalog.PlacementOf(a);
                        Assert.AreEqual(legacyOnly, p.Center == null);
                        if (p.Center != null) Near(MapMath.AtlasFromLocal(p, a.Center), p.Center.Value, 1e-3f, "declared centre, yaw " + yaw);
                        var layer = MapSurface.AreaLayer(a, p, null, 1);
                        Near(layer.AtlasRect.center, new Vector2(centre.x, centre.z), 1e-3f, "quad turns about the area centre, yaw " + yaw);
                        foreach (var local in new[] { new Vector2(12.34f, -4.13f), new Vector2(-19.46f, 19.41f), new Vector2(0.37f, 5.21f), new Vector2(19.87f, -9.93f) })
                        {
                            // A marker: world position -> venue-local (VenueSceneRoot.ToLocal) -> atlas (MapMarkers.Live).
                            var world = root.transform.TransformPoint(new Vector3(local.x, 0f, local.y));
                            var atlas = MapMath.AtlasFromLocal(p, MapMath.LocalFromWorld(world, root.transform.position, root.transform.rotation));
                            Near(atlas, new Vector2(world.x, world.z), 1e-3f, "atlas metres are island metres, yaw " + yaw);
                            // The pixel the drawn quad puts under the marker is the pixel the bake drew that spot on.
                            var drawn = MapSurface.LayerFromAtlas(layer, atlas);
                            var baked = MapMath.AreaUv(a, local);
                            Near(drawn, baked, 1e-4f, "uv, yaw " + yaw);
                            Assert.AreEqual(Pixel(baked, a), Pixel(drawn, a), "pixel, yaw " + yaw + " at " + local);
                            Near(MapSurface.AtlasFromLayer(layer, baked), atlas, 1e-3f, "vertex math, yaw " + yaw);
                            Assert.IsTrue(MapSurface.AreaContains(a, p, atlas));
                        }
                    }
                    finally { Object.DestroyImmediate(root); MapCatalog.Reset(); }
                }
        }

        [Test]
        public void RotatedVenue_TapsBoundsAndLabelsFollowTheTurnedQuad()
        {
            var a = new MapArea { Id = "rot", Min = new Vector2(-20f, -10f), Size = new Vector2(40f, 30f), Pixels = new Vector2Int(400, 300) };
            var p = new MapPlacement { AreaId = "rot", Origin = new Vector2(100f, 50f), RotationDeg = -90f };   // yaw 90: local north faces east
            var r = MapSurface.AreaAtlasRect(a, p);
            Near(r.center, MapMath.AtlasFromLocal(p, a.Center), 1e-3f);
            Near(r.size, a.Size);
            var b = MapSurface.AreaAtlasBounds(a, p);
            Near(b.size, new Vector2(30f, 40f), 1e-3f, "a quarter turn swaps the footprint");
            Near(b.center, r.center, 1e-3f);
            Assert.IsTrue(MapSurface.AreaContains(a, p, MapMath.AtlasFromLocal(p, new Vector2(19f, 19f))), "a corner of the turned quad");
            Assert.IsFalse(MapSurface.AreaContains(a, p, r.min + new Vector2(0.5f, 0.5f)), "the unturned frame's corner is off the venue");
            Near(MapSurface.AreaNorthEdge(a, p), r.center + new Vector2(0f, 20f), 1e-3f, "the name pill rides the turned north edge");
            var corners = MapSurface.AreaCorners(a, p);
            Near(corners[0], MapMath.AtlasFromLocal(p, a.Min), 1e-3f, "texture bottom-left is the local Min corner");
            Near(corners[2], MapMath.AtlasFromLocal(p, a.Min + a.Size), 1e-3f, "texture top-right is the local Max corner");
            var flat = new MapPlacement { AreaId = "rot", Origin = new Vector2(100f, 50f) };
            var fr = MapSurface.AreaAtlasRect(a, flat);
            Near(fr.min, flat.Origin + a.Min, 1e-4f, "unrotated venues keep their frame");
            Near(MapSurface.AreaNorthEdge(a, flat), new Vector2(fr.center.x, fr.yMax), 1e-4f);
            Assert.AreEqual(0f, MapSurface.AreaLayer(a, flat, null, 1).RotationDeg);
        }

        /// <summary>Separating-axis test for two convex quads given as ordered corners (touching edges do not overlap).</summary>
        private static bool QuadsOverlap(Vector2[] a, Vector2[] b)
        {
            foreach (var poly in new[] { a, b })
                for (int i = 0; i < poly.Length; i++)
                {
                    var e = poly[(i + 1) % poly.Length] - poly[i];
                    var n = new Vector2(-e.y, e.x);
                    float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                    foreach (var v in a) { float d = Vector2.Dot(v, n); aMin = Mathf.Min(aMin, d); aMax = Mathf.Max(aMax, d); }
                    foreach (var v in b) { float d = Vector2.Dot(v, n); bMin = Mathf.Min(bMin, d); bMax = Mathf.Max(bMax, d); }
                    if (aMax <= bMin + 1e-3f * n.magnitude || bMax <= aMin + 1e-3f * n.magnitude) return false;
                }
            return true;
        }

        [Test]
        public void LocalFromWorld_MatchesTheVenueRootTransform()
        {
            var go = new GameObject("root");
            try
            {
                go.transform.SetPositionAndRotation(new Vector3(5, 0, -3), Quaternion.Euler(0, 37, 0));
                var world = new Vector3(2.5f, 1.2f, 4f);
                var l = go.transform.InverseTransformPoint(world);
                Near(MapMath.LocalFromWorld(world, go.transform.position, go.transform.rotation), new Vector2(l.x, l.z));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Minimap_IsPlayerUp()
        {
            var c = new Vector2(10, 10);
            // Facing north: north is up, east is right.
            Near(MapMath.ToMinimap(c + new Vector2(0, 5), c, 0f, 10f), new Vector2(0, 0.5f));
            Near(MapMath.ToMinimap(c + new Vector2(5, 0), c, 0f, 10f), new Vector2(0.5f, 0));
            // Facing east: the east is straight up, north is on the left.
            float east = Mathf.PI / 2f;
            Near(MapMath.ToMinimap(c + new Vector2(5, 0), c, east, 10f), new Vector2(0, 0.5f), 1e-3f);
            Near(MapMath.ToMinimap(c + new Vector2(0, 5), c, east, 10f), new Vector2(-0.5f, 0), 1e-3f);
            // Heading from a forward vector, and the inverse.
            Assert.AreEqual(east, MapMath.Heading(Vector3.right), Eps);
            Assert.AreEqual(0f, MapMath.Heading(Vector3.forward), Eps);
            Assert.AreEqual(Mathf.PI, Mathf.Abs(MapMath.Heading(Vector3.back)), Eps);
            var p = new Vector2(3.3f, -8.1f);
            Near(MapMath.FromMinimap(MapMath.ToMinimap(p, c, 1.1f, 16f), c, 1.1f, 16f), p, 1e-3f);
        }

        [Test]
        public void EdgeClamp_PullsOffscreenTargetsOntoTheRing()
        {
            Assert.IsFalse(MapMath.ClampToEdge(new Vector2(0.3f, -0.4f), 0.1f, out var inside, out _));
            Near(inside, new Vector2(0.3f, -0.4f));
            Assert.IsTrue(MapMath.ClampToEdge(new Vector2(3f, 1.5f), 0.1f, out var at, out float angle));
            Assert.AreEqual(0.9f, Mathf.Max(Mathf.Abs(at.x), Mathf.Abs(at.y)), Eps, "on the inset edge");
            Assert.AreEqual(Mathf.Atan2(3f, 1.5f), angle, Eps, "keeps the direction");
            Near(at.normalized, new Vector2(3f, 1.5f).normalized, 1e-4f);
        }

        [Test]
        public void SmoothAngle_TakesTheShortWayRound()
        {
            float a = 179f * Mathf.Deg2Rad, b = -179f * Mathf.Deg2Rad;
            float next = MapMath.SmoothAngle(a, b, 12f, 1f / 60f);
            Assert.Greater(next, a, "moves forward through 180 rather than back through 0");
            Assert.Less(next - a, 2f * Mathf.Deg2Rad);
        }

        [Test]
        public void TiltedView_ProjectsAndUnprojectsExactly()
        {
            var v = new MapView { Center = new Vector2(20, -30), PixelsPerMeter = 6f, Tilt = FullMapView.Tilt, Viewport = new Vector2(2400, 1080) };
            Near(v.Project(v.Center), v.Pivot, 1e-3f, "centre sits at the pivot");
            foreach (var p in new[] { new Vector2(20, -30), new Vector2(80, 40), new Vector2(-90, -120), new Vector2(150, 70) })
                Near(v.Unproject(v.Project(p)), p, 1e-2f);
            // Perspective: north (far) recedes, south (near) grows.
            Assert.Less(v.ScaleAt(v.Center + new Vector2(0, 60)), v.ScaleAt(v.Center));
            Assert.Greater(v.ScaleAt(v.Center + new Vector2(0, -60)), v.ScaleAt(v.Center));
            Assert.Less(v.Project(v.Center + new Vector2(0, 10)).y, v.Pivot.y, "north is up on screen");
            v.Clamp(new Rect(-50, -50, 100, 100), 1f, 26f);
            v.Center = new Vector2(500, -500); v.PixelsPerMeter = 99f;
            v.Clamp(new Rect(-50, -50, 100, 100), 1f, 26f);
            Near(v.Center, new Vector2(50, -50)); Assert.AreEqual(26f, v.PixelsPerMeter);
        }

        [Test]
        public void BakedData_PlacesEveryShippedVenueOnTheIsland()
        {
            MapCatalog.Reset();
            var areas = MapCatalog.Areas;
            var atlas = MapCatalog.Atlas;
            Assert.GreaterOrEqual(areas.Count, 20, "every shipped venue and home type has a baked map");
            foreach (var id in new[] { "timeline_square", "terminal_cafe", "perp_pit", "spaces_studio", "home_studio" })
                Assert.IsTrue(areas.ContainsKey(id), id);
            // Venues may turn to face their streets (atlas.json v2): bounds and overlaps use the turned quads.
            var rects = atlas.Placements.Values.Select(p => (p.AreaId, p, r: MapSurface.AreaAtlasBounds(areas[p.AreaId], p), q: MapSurface.AreaCorners(areas[p.AreaId], p))).ToList();
            foreach (var (id, p, r, _) in rects)
            {
                Assert.IsTrue(atlas.Rect.Contains(r.min) && atlas.Rect.Contains(r.max), id + " inside the atlas");
                Assert.IsNotNull(Resources.Load<Texture2D>(areas[id].Texture), id + " texture");
                var a = areas[id];
                Assert.AreEqual(a.Pixels.x / a.Size.x, a.Pixels.y / a.Size.y, 0.01f, id + ": square pixels (one transform both ways)");
                if (p.Center != null) Near(MapMath.AtlasFromLocal(p, a.Center), p.Center.Value, 0.05f, id + ": the declared centre is where the math puts it");
            }
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    Assert.IsFalse(QuadsOverlap(rects[i].q, rects[j].q), rects[i].AreaId + " / " + rects[j].AreaId + " do not overlap");
            Assert.IsNotNull(Resources.Load<Texture2D>(atlas.Texture), "atlas texture");
            // atlas.json v2 (World lane): the plots are the island's districts, each with a name pill and a framing size.
            Assert.GreaterOrEqual(atlas.Plots.Count, 9, "every district with venues is a plot");
            Assert.IsTrue(atlas.Plots.All(p => p.Kind == "district" && !p.Reserved), "districts, no reserved lots");
            Assert.Greater(atlas.PlotSize, 100f, "picking a district frames a district-sized view");
        }

        [Test]
        public void Markers_LandInsideTheirArea_FromRegistrySpots()
        {
            MapCatalog.Reset();
            var snapshot = JObject.Parse(Resources.Load<TextAsset>("Contract/config_snapshot").text);
            var venues = (JObject)snapshot["ids"]["spots"]["venues"];
            int checkedDoors = 0;
            foreach (var a in MapCatalog.Areas.Values.Where(a => !a.IsHome))
            {
                var spots = venues[a.VenueId]?["spots"] as JArray;
                if (spots == null) continue;
                foreach (var s in spots.Where(s => (string)s["kind"] == "exit" || (string)s["kind"] == "terminal"))
                {
                    var uv = MapMath.AreaUv(a, new Vector2((float)s["x"], (float)s["z"]));
                    Assert.IsTrue(uv.x > 0 && uv.x < 1 && uv.y > 0 && uv.y < 1, a.Id + " " + s["id"] + " uv " + uv);
                    checkedDoors++;
                }
            }
            Assert.Greater(checkedDoors, 30);
            Assert.AreEqual(MarkerKind.Door, MapMarkers.KindFor("exit", "exit"));
            Assert.IsNull(MapMarkers.KindFor("exit", "entrance"), "the entrance is not a second door");
            Assert.AreEqual(MarkerKind.Desk, MapMarkers.KindFor("terminal", "terminal_1"));
            Assert.AreEqual(MarkerKind.Mic, MapMarkers.KindFor("stage", "center"));
            Assert.IsNull(MapMarkers.KindFor("seat", "bench_1"));
        }

        [Test]
        public void Search_IgnoresCaseAndAccents()
        {
            Assert.IsTrue(FullMapView.Matches("cafe", "Terminal Café"));
            Assert.IsTrue(FullMapView.Matches("TRENCH", "Perp Pit", "The Trenches"));
            Assert.IsFalse(FullMapView.Matches("yacht", "Perp Pit", "The Trenches"));
            Assert.IsFalse(FullMapView.Matches("  ", "Anything"));
        }
    }
}
