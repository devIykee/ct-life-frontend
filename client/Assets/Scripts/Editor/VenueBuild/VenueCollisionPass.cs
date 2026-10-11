using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Adds prop collision to already built (and baked) venue prefabs without touching meshes or lightmaps:
    ///   -executeMethod CTLife.EditorTools.VenueBuild.VenueCollisionPass.Run [-ctVenues a,b]
    /// Layout venues: the venue script runs again in DryRun mode to recover its placements, explicit colliders and
    /// NavMesh sources; the prefab's Colliders group and NavMesh are regenerated with prop colliders and obstacles.
    /// Repacked venues: props come from the authored source prefab; existing colliders stay, a "Prop colliders" group
    /// is added and the NavMesh is rebuilt from the walkable rectangles, solid colliders and prop obstacles.
    /// Both paths end in the same gates as a full build (spots reachable, every prop collided, no walk-through).
    /// </summary>
    public static class VenueCollisionPass
    {
        public static void Run()
        {
            int failures = 0;
            var table = new JArray();
            foreach (var spec in VenueBuildPipeline.Selected())
            {
                try { table.Add(spec.repackSource != null ? PatchRepack(spec) : PatchLayout(spec)); }
                catch (Exception e)
                {
                    failures++;
                    Debug.LogError($"CTLIFE [collision] {spec.id} FAILED: {e}");
                    table.Add(new JObject { ["id"] = spec.id, ["ok"] = false, ["error"] = e.Message });
                }
            }
            File.WriteAllText(Path.Combine(VenueBuildPipeline.EvidenceDir, "last-collision.json"), table.ToString());
            Debug.Log($"CTLIFE [collision] finished: {table.Count - failures} ok, {failures} failed.");
            if (Application.isBatchMode && failures > 0) EditorApplication.Exit(1);
        }

        private static JObject PatchLayout(VenueSpec spec)
        {
            VenuePalette.Reset();
            VenueSpotsFile.Reset();
            var spots = VenueSpotsFile.Load(spec.spotsKey);
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var vb = new VB(spec) { DryRun = true };
                vb.Begin(preview, spots);
                spec.build(vb);
                var props = VenueCollision.Classify(vb.RawProps, spots, vb.Walk);
                return Patch(spec, spots, root =>
                {
                    var old = root.transform.Find("Colliders");
                    int index = old != null ? old.GetSiblingIndex() : 0;
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                    var group = VB.Child("Colliders", root.transform);
                    group.SetSiblingIndex(index);
                    foreach (var box in vb.CollidersRoot.GetComponentsInChildren<BoxCollider>(true))
                    {
                        var go = VB.Child(box.name, group);
                        go.localPosition = box.transform.localPosition;
                        go.localRotation = box.transform.localRotation;
                        var c = go.gameObject.AddComponent<BoxCollider>();
                        c.center = box.center;
                        c.size = box.size;
                    }
                    VenueCollision.AddColliders(group, root, props);
                    return (props, vb.NavSources.Concat(VenueCollision.Obstacles(props)).ToList(), vb.Walk);
                });
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static JObject PatchRepack(VenueSpec spec)
        {
            VenueSpotsFile.Reset();
            var spots = VenueSpotsFile.Load(spec.spotsKey);
            var props = SourceProps(spec, spots);
            return Patch(spec, spots, root =>
            {
                var sources = RepackCollision(root, props, spots);
                return (props, sources, root.GetComponent<VenueSceneRoot>().cameraBounds);
            });
        }

        /// <summary>Props of a repack source prefab, classified against the venue's walkable area.</summary>
        internal static List<VenueCollision.PropBox> SourceProps(VenueSpec spec, VenueSpots spots)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(spec.repackSource) ?? throw new FileNotFoundException(spec.repackSource);
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = Object.Instantiate(src);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, preview);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                spec.dress?.Invoke(go);
                return VenueCollision.Classify(VenueCollision.FromSourceInstance(go), spots, go.GetComponent<VenueSceneRoot>().cameraBounds);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        /// <summary>
        /// Repacked venue collision: a fresh "Prop colliders" group, and NavMesh sources = walkable rectangles + solid
        /// existing colliders (never over a claimable spot that has no approach point) + prop obstacles.
        /// </summary>
        internal static List<NavMeshBuildSource> RepackCollision(GameObject root, List<VenueCollision.PropBox> props, VenueSpots spots)
        {
            var old = root.transform.Find("Prop colliders");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var sources = new List<NavMeshBuildSource>();
            foreach (var r in spots.walkable)
            {
                var center = new Vector3((r.x0 + r.x1) * .5f, -0.1f, (r.z0 + r.z1) * .5f);
                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box, transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one),
                    size = new Vector3(Mathf.Abs(r.x1 - r.x0), 0.2f, Mathf.Abs(r.z1 - r.z0)), area = 0,
                });
            }
            var anchors = root.GetComponentsInChildren<SpotAnchor>(true);
            var exposedSeats = anchors.Where(a => !string.IsNullOrEmpty(a.anim) && a.approach == null).Select(a => a.transform.position).ToList();
            int solids = 0;
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || c.GetComponentInParent<SpotAnchor>() != null) continue;
                var b = c.bounds;
                if (b.max.y <= 0.3f) continue; // floors
                if (exposedSeats.Any(p => new Rect(b.min.x - 0.3f, b.min.z - 0.3f, b.size.x + 0.6f, b.size.z + 0.6f).Contains(new Vector2(p.x, p.z)))) continue;
                if (c is BoxCollider box)
                {
                    var size = Vector3.Scale(box.size, box.transform.lossyScale);
                    var center = box.transform.TransformPoint(box.center);
                    float bottom = Mathf.Min(center.y - size.y * .5f, -0.5f), top = center.y + size.y * .5f;
                    sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.ModifierBox,
                        transform = Matrix4x4.TRS(new Vector3(center.x, (bottom + top) * .5f, center.z), box.transform.rotation, Vector3.one),
                        size = new Vector3(size.x, top - bottom, size.z), area = 1,
                    });
                }
                else
                {
                    float bottom = Mathf.Min(b.min.y, -0.5f);
                    sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.ModifierBox, transform = Matrix4x4.TRS(new Vector3(b.center.x, (bottom + b.max.y) * .5f, b.center.z), Quaternion.identity, Vector3.one),
                        size = new Vector3(b.size.x, b.max.y - bottom, b.size.z), area = 1,
                    });
                }
                solids++;
            }
            var group = VB.Child("Prop colliders", root.transform);
            VenueCollision.AddColliders(group, root, props);
            sources.AddRange(VenueCollision.Obstacles(props));
            Debug.Log($"CTLIFE [collision] {root.name}: {solids} existing solid colliders as obstacles, {props.Count} props " +
                      $"({props.Count(p => p.seat)} seats, {props.Count(p => p.tree)} trees), {group.childCount} new prop colliders.");
            return sources;
        }

        private static JObject Patch(VenueSpec spec, VenueSpots spots,
            Func<GameObject, (List<VenueCollision.PropBox> props, List<NavMeshBuildSource> sources, Rect walk)> apply)
        {
            var root = PrefabUtility.LoadPrefabContents(spec.PrefabPath);
            try
            {
                var venue = root.GetComponent<VenueSceneRoot>() ?? throw new InvalidDataException(spec.id + ": no VenueSceneRoot");
                var (props, sources, walk) = apply(root);
                var data = VB.BuildNavMeshData(spec.id, sources, walk);
                var navPath = venue.navigation != null ? AssetDatabase.GetAssetPath(venue.navigation) : spec.Folder + "/" + spec.id + "_NavMesh.asset";
                var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath);
                if (existing == null) { AssetDatabase.CreateAsset(data, navPath); existing = data; }
                else { EditorUtility.CopySerialized(data, existing); Object.DestroyImmediate(data); EditorUtility.SetDirty(existing); }
                AssetDatabase.SaveAssetIfDirty(existing);
                venue.navigation = existing;
                var nav = VenueBuildPipeline.ValidateBuild(spec, venue, spots);
                var collision = VenueCollision.Validate(spec.id, root, existing, props);
                if (PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath) == null) throw new IOException("Saving " + spec.PrefabPath + " failed.");
                int colliders = root.GetComponentsInChildren<Collider>(true).Count(c => !c.isTrigger);
                Debug.Log($"CTLIFE [collision] {spec.id}: {collision["props"]} props ({collision["seatProps"]} seats, {collision["obstacles"]} obstacles), " +
                          $"{colliders} colliders; {nav.Count} spots reachable.");
                collision["id"] = spec.id;
                collision["ok"] = true;
                collision["colliders"] = colliders;
                collision["spots"] = nav.Count;
                File.WriteAllText(Path.Combine(VenueBuildPipeline.EvidenceDir, "receipts", spec.id + "-collision.json"),
                    new JObject { ["collision"] = collision, ["navigation"] = nav }.ToString());
                return collision;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
