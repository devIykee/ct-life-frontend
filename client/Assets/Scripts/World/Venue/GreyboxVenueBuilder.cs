using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;
using UnityEngine.AI;

namespace CTLife.World
{
    /// <summary>
    /// Builds a playable greybox diorama for a venue straight from contract/ids/spots.json: ground from the walkable
    /// rectangles, props at seats/terminals/stages, an entrance arch, and a runtime NavMesh. Every venue works
    /// before its art lands, and the server's movement validation (walkable rects) matches the ground exactly.
    /// </summary>
    public static class GreyboxVenueBuilder
    {
        private static readonly Color Ground = new Color(0.86f, 0.84f, 0.80f);
        private static readonly Color Path = new Color(0.78f, 0.74f, 0.70f);
        private static readonly Color Wood = new Color(0.62f, 0.44f, 0.30f);
        private static readonly Color Screen = new Color(0.25f, 0.95f, 0.55f);
        private static readonly Color Stage = new Color(0.30f, 0.30f, 0.38f);
        private static readonly Color Grass = new Color(0.45f, 0.70f, 0.38f);
        private static readonly Color Blossom = new Color(0.96f, 0.68f, 0.80f);

        public static VenueSceneRoot Build(string venueId, VenueSpots spots, out NavMeshDataInstance navMesh)
        {
            var root = new GameObject("Venue_" + venueId + "_greybox");
            var venue = root.AddComponent<VenueSceneRoot>();
            venue.venueId = venueId;
            venue.IsGreybox = true;

            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            var rects = spots?.walkable ?? new List<WalkRect>();
            if (rects.Count == 0) rects = new List<WalkRect> { new WalkRect { x0 = -12, z0 = -10, x1 = 12, z1 = 10 } };

            int i = 0;
            foreach (var r in rects)
            {
                float minX = Mathf.Min(r.x0, r.x1), maxX = Mathf.Max(r.x0, r.x1);
                float minZ = Mathf.Min(r.z0, r.z1), maxZ = Mathf.Max(r.z0, r.z1);
                var center = new Vector3((minX + maxX) / 2f, -0.05f, (minZ + maxZ) / 2f);
                var size = new Vector3(maxX - minX, 0.1f, maxZ - minZ);
                var g = Box(root.transform, "ground_" + i, center, size, i == 0 ? Ground : Path);
                g.layer = 0;
                var b = new Bounds(center, size);
                if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
                i++;
            }
            venue.cameraBounds = new Rect(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z);

            // Grass border and a few blossom trees so it reads as a place, not a test plane.
            var border = Box(root.transform, "grass", new Vector3(bounds.center.x, -0.12f, bounds.center.z),
                new Vector3(bounds.size.x + 16f, 0.1f, bounds.size.z + 16f), Grass);
            Object.DestroyImmediate(border.GetComponent<Collider>());
            var rng = new System.Random(venueId.GetHashCode());
            for (int t = 0; t < 8; t++)
            {
                float side = t % 2 == 0 ? 1 : -1;
                var pos = new Vector3(
                    (t < 4 ? side * (bounds.extents.x + 3f) : (float)(rng.NextDouble() * 2 - 1) * bounds.extents.x) + bounds.center.x,
                    0f,
                    (t >= 4 ? side * (bounds.extents.z + 3f) : (float)(rng.NextDouble() * 2 - 1) * bounds.extents.z) + bounds.center.z);
                Tree(root.transform, pos, t % 3 == 0 ? Blossom : new Color(0.35f, 0.62f, 0.32f));
            }

            if (spots != null)
            {
                foreach (var s in System.Linq.Enumerable.Distinct(spots.spots.Values))
                {
                    if (!s.hasPosition) continue;
                    var p = new Vector3(s.x, 0f, s.z);
                    var rot = Quaternion.Euler(0f, s.rotY * Mathf.Rad2Deg, 0f);
                    GameObject prop = null;
                    switch (s.kind)
                    {
                        case "seat":
                            prop = Box(root.transform, "bench_" + s.id, p - rot * Vector3.forward * 0.15f + Vector3.up * 0.22f, new Vector3(1.4f, 0.45f, 0.5f), Wood);
                            break;
                        case "terminal":
                            prop = Box(root.transform, "terminal_" + s.id, p + rot * Vector3.forward * 0.7f + Vector3.up * 0.5f, new Vector3(1.2f, 1.0f, 0.5f), Stage);
                            var scr = Box(root.transform, "screen_" + s.id, p + rot * new Vector3(0f, 1.25f, 0.45f), new Vector3(1.0f, 0.6f, 0.05f), Screen);
                            scr.transform.rotation = rot;
                            scr.GetComponent<Renderer>().sharedMaterial = Mats.Emissive(Screen);
                            Object.DestroyImmediate(scr.GetComponent<Collider>());
                            break;
                        case "stage":
                            prop = Box(root.transform, "stage_" + s.id, p + Vector3.up * 0.1f, new Vector3(1.6f, 0.2f, 1.6f), Stage);
                            Object.DestroyImmediate(prop.GetComponent<Collider>());
                            break;
                        case "bed":
                            prop = Box(root.transform, "bed_" + s.id, p + Vector3.up * 0.3f, new Vector3(1.0f, 0.6f, 2.0f), new Color(0.9f, 0.9f, 0.95f));
                            break;
                        case "exit":
                            Arch(root.transform, p, rot);
                            break;
                    }
                    if (prop != null) prop.transform.rotation = rot;

                    var anchorGo = new GameObject("spot_" + s.id);
                    anchorGo.transform.SetParent(root.transform, false);
                    anchorGo.transform.localPosition = p;
                    anchorGo.transform.localRotation = rot;
                    var anchor = anchorGo.AddComponent<SpotAnchor>();
                    anchor.spotId = s.id;
                    anchor.kind = s.kind;
                    anchor.anim = s.anim ?? (s.kind == "seat" ? "act_sit" : s.kind == "terminal" ? "act_type" : "");
                }
            }

            EnsureLight(root.transform);
            venue.Init(spots);
            navMesh = BakeNavMesh(bounds);
            Log.Info("venue", $"Greybox {venueId}: {rects.Count} walkable rects, {spots?.spots.Count ?? 0} spots");
            return venue;
        }

        private static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = Mats.Color(color);
            go.isStatic = true;
            return go;
        }

        private static void Tree(Transform parent, Vector3 pos, Color leaves)
        {
            var trunk = Box(parent, "trunk", pos + Vector3.up * 1.2f, new Vector3(0.35f, 2.4f, 0.35f), new Color(0.45f, 0.32f, 0.22f));
            Object.DestroyImmediate(trunk.GetComponent<Collider>());
            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.name = "crown";
            crown.transform.SetParent(parent, false);
            crown.transform.localPosition = pos + Vector3.up * 3.1f;
            crown.transform.localScale = new Vector3(2.8f, 2.2f, 2.8f);
            crown.GetComponent<Renderer>().sharedMaterial = Mats.Color(leaves);
            Object.DestroyImmediate(crown.GetComponent<Collider>());
        }

        private static void Arch(Transform parent, Vector3 p, Quaternion rot)
        {
            var c = new Color(0.95f, 0.45f, 0.25f);
            var l = Box(parent, "arch_l", p + rot * new Vector3(-1.4f, 1.3f, 0), new Vector3(0.3f, 2.6f, 0.3f), c);
            var r = Box(parent, "arch_r", p + rot * new Vector3(1.4f, 1.3f, 0), new Vector3(0.3f, 2.6f, 0.3f), c);
            var t = Box(parent, "arch_t", p + Vector3.up * 2.7f, new Vector3(3.1f, 0.3f, 0.3f), c);
            t.transform.rotation = rot;
            foreach (var go in new[] { l, r, t }) Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        private static void EnsureLight(Transform parent)
        {
            if (Object.FindAnyObjectByType<Light>() != null) return;
            var go = new GameObject("sun");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.92f, 0.80f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.None; // Spec: no realtime shadows; blob shadows instead.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.85f);
            RenderSettings.ambientEquatorColor = new Color(0.75f, 0.68f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.35f, 0.30f, 0.28f);
        }

        /// <summary>Runtime NavMesh from colliders inside the venue bounds (AIModule NavMeshBuilder).</summary>
        public static NavMeshDataInstance BakeNavMesh(Bounds bounds)
        {
            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            var b = bounds;
            b.Expand(new Vector3(2f, 6f, 2f));
            NavMeshBuilder.CollectSources(b, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            var settings = NavMesh.GetSettingsByID(0);
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, b, Vector3.zero, Quaternion.identity);
            if (data == null)
            {
                Log.Warn("venue", "NavMesh bake returned null");
                return default;
            }
            return NavMesh.AddNavMeshData(data);
        }
    }
}
