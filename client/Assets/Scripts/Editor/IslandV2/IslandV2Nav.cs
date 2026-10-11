using System.Collections.Generic;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// The island-wide pedestrian NavMesh (NPCs path on it permanently; the player moves on colliders). Sources: the
    /// ground of every district (2 m grid, dry and not too steep, cut out under venue rooms), every road surface, and each
    /// venue's own baked interior at its pose. Walls round each venue room (open at its door) keep NPCs to the doorway.
    /// No door corridors.
    /// </summary>
    public static class IslandV2Nav
    {
        public static NavMeshData Build(IslandHeightfield h, JObject plan, List<IslandVenue> venues, IEnumerable<Mesh> roadSurfaces, JObject receipt)
        {
            var sources = new List<NavMeshBuildSource>();
            var temp = new List<Object>();
            // 1) district ground
            var polys = ((JArray)plan["districts"]).Where(d => (string)d["id"] != "mountain")
                .Select(d => ((JArray)d["poly"]).Select(IslandV2Paths.V2).ToArray()).ToList();
            var lodge = venues.FirstOrDefault(v => v.venueId == "hackathon_house");
            var verts = new List<Vector3>(); var tris = new List<int>();
            float G = 2f;
            var bmin = new Vector2(float.MaxValue, float.MaxValue); var bmax = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in polys) foreach (var q in p) { bmin = Vector2.Min(bmin, q); bmax = Vector2.Max(bmax, q); }
            bool Wanted(Vector2 c)
            {
                foreach (var p in polys) if (IslandWorld.Inside(c, p)) return true;
                return lodge != null && Vector2.Distance(c, lodge.origin) < 60f;
            }
            bool UnderRoom(Vector2 c)
            {
                foreach (var v in venues)
                {
                    var l = v.ToLocal(c);
                    var w = v.localWalk;
                    if (l.x > w.xMin - 0.4f && l.x < w.xMax + 0.4f && l.y > w.yMin - 0.4f && l.y < w.yMax + 0.4f) return true;
                }
                return false;
            }
            for (float z = bmin.y; z < bmax.y; z += G)
                for (float x = bmin.x; x < bmax.x; x += G)
                {
                    var c = new Vector2(x + G * 0.5f, z + G * 0.5f);
                    if (!Wanted(c) || UnderRoom(c)) continue;
                    float h00 = h.Sample(x, z), h10 = h.Sample(x + G, z), h01 = h.Sample(x, z + G), h11 = h.Sample(x + G, z + G);
                    if (Mathf.Min(Mathf.Min(h00, h10), Mathf.Min(h01, h11)) < h.Sea + 0.15f) continue;
                    if (Mathf.Max(Mathf.Max(h00, h10), Mathf.Max(h01, h11)) - Mathf.Min(Mathf.Min(h00, h10), Mathf.Min(h01, h11)) > 1.5f) continue;
                    int b = verts.Count;
                    verts.Add(new Vector3(x, h00, z)); verts.Add(new Vector3(x + G, h10, z)); verts.Add(new Vector3(x, h01, z + G)); verts.Add(new Vector3(x + G, h11, z + G));
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                }
            var ground = new Mesh { name = "nav-ground", indexFormat = IndexFormat.UInt32 };
            ground.SetVertices(verts); ground.SetTriangles(tris, 0);
            temp.Add(ground);
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = ground, transform = Matrix4x4.identity, area = 0 });
            receipt["navGroundQuads"] = tris.Count / 6;
            // 2) road surfaces
            int roads = 0;
            foreach (var m in roadSurfaces)
            {
                if (m == null) continue;
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = m, transform = Matrix4x4.identity, area = 0 });
                roads++;
            }
            receipt["navRoadMeshes"] = roads;
            // 3) venue interiors at their poses, 4) room walls with the doorway left open
            NavMesh.RemoveAllNavMeshData();
            var instances = new List<NavMeshDataInstance>();
            var entries = AssetDatabase.FindAssets("t:VenueContentEntry").Select(g => AssetDatabase.LoadAssetAtPath<VenueContentEntry>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(e => e != null && !e.isHome && e.prefab != null).ToList();
            int rooms = 0;
            foreach (var v in venues)
            {
                var e = entries.FirstOrDefault(x => x.contentId == v.venueId);
                if (e == null || e.prefab.navigation == null) continue;
                instances.Add(NavMesh.AddNavMeshData(e.prefab.navigation, v.Position, v.Rotation));
                rooms++;
                var w = v.localWalk;
                float t = 0.5f, hgt = 2.0f;
                var rot = v.Rotation;
                Vector3 W(float lx, float lz) => v.Position + rot * new Vector3(lx, 0f, lz);
                void Wall(float x0, float z0, float x1, float z1)
                {
                    var a = W(x0, z0); var b = W(x1, z1);
                    var d = b - a;
                    if (d.magnitude < 0.2f) return;
                    var m = Matrix4x4.TRS((a + b) * 0.5f + Vector3.up * (hgt * 0.5f - 0.3f), Quaternion.LookRotation(d.normalized, Vector3.up), Vector3.one);
                    sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(t, hgt, d.magnitude + t), transform = m, area = 1 });
                }
                float ex = v.ToLocal(v.exit).x, door = 1.9f;
                Wall(w.xMin - t, w.yMax + t, w.xMax + t, w.yMax + t);
                Wall(w.xMin - t, w.yMin - t, w.xMin - t, w.yMax + t);
                Wall(w.xMax + t, w.yMin - t, w.xMax + t, w.yMax + t);
                Wall(w.xMin - t, w.yMin - t, ex - door, w.yMin - t);
                Wall(ex + door, w.yMin - t, w.xMax + t, w.yMin - t);
            }
            var tri = NavMesh.CalculateTriangulation();
            foreach (var i in instances) NavMesh.RemoveNavMeshData(i);
            var rooms_ = new Mesh { name = "nav-rooms", indexFormat = IndexFormat.UInt32 };
            rooms_.SetVertices(tri.vertices); rooms_.SetTriangles(tri.indices, 0);
            temp.Add(rooms_);
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = rooms_, transform = Matrix4x4.identity, area = 0 });
            receipt["navRooms"] = rooms;

            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.28f; settings.agentHeight = 1.8f; settings.agentSlope = 40f; settings.agentClimb = 0.45f;
            settings.overrideVoxelSize = true; settings.voxelSize = 0.22f;
            settings.overrideTileSize = true; settings.tileSize = 192;
            var size = bmax - bmin + new Vector2(140f, 140f);
            var bounds = new Bounds(new Vector3((bmin.x + bmax.x) * 0.5f, 60f, (bmin.y + bmax.y) * 0.5f), new Vector3(size.x, 220f, size.y));
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            foreach (var o in temp) Object.DestroyImmediate(o);
            if (data != null) data.name = "CTIsland_NavMesh";
            return data;
        }
    }
}
