using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Keeps the fitted seat presentation (CafeSeatMotionProfile, authored by the character-feel lane) on venues the
    /// builder rebuilds from scratch. The animator bindings are copied verbatim from the authored source prefab; the
    /// seat exit routes are regenerated from the new layout: one point per seat, at its NavMesh approach (the side step
    /// the exit motion is fitted to: right of seat_*/table_*, left of the Studio's stage_*), at seat height.
    /// The runtime accepts a route only if every point is at the seat's height (±2 cm), it ends on the approach and it is
    /// at most 2.5 m long; the build fails here first if a generated route would be rejected.
    /// </summary>
    public static class VenueSeatMotion
    {
        public static void Bind(VenueSpec spec, GameObject root)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(spec.seatMotionSource)?.GetComponent<CafeSeatMotionProfile>()
                      ?? throw new FileNotFoundException(spec.id + ": no CafeSeatMotionProfile on " + spec.seatMotionSource);
            var dst = root.AddComponent<CafeSeatMotionProfile>();
            EditorUtility.CopySerialized(src, dst);
            var srcRoutes = new SerializedObject(src).FindProperty("seatExits");
            var wanted = new List<string>();
            for (int i = 0; i < srcRoutes.arraySize; i++) wanted.Add(srcRoutes.GetArrayElementAtIndex(i).FindPropertyRelative("spotId").stringValue);
            var anchors = root.GetComponentsInChildren<SpotAnchor>(true).ToDictionary(a => LocalId(a.spotId), a => a);
            var so = new SerializedObject(dst);
            var routes = so.FindProperty("seatExits");
            routes.arraySize = 0;
            int n = 0;
            foreach (var id in wanted)
            {
                if (!anchors.TryGetValue(id, out var anchor) || anchor.approach == null)
                    throw new InvalidDataException(spec.id + ": seat motion route for " + id + " has no anchor/approach in the new layout.");
                var seat = root.transform.InverseTransformPoint(anchor.transform.position);
                var end = root.transform.InverseTransformPoint(anchor.approach.position);
                if (Mathf.Abs(end.y - seat.y) > 0.02f || Vector3.Distance(seat, end) > 2.5f)
                    throw new InvalidDataException($"{spec.id}: seat exit for {id} would be rejected at runtime (seat y {seat.y:F2}, approach y {end.y:F2}, " +
                                                   $"length {Vector3.Distance(seat, end):F2}).");
                routes.arraySize = n + 1;
                var row = routes.GetArrayElementAtIndex(n++);
                row.FindPropertyRelative("spotId").stringValue = id;
                var pts = row.FindPropertyRelative("points");
                pts.arraySize = 1;
                pts.GetArrayElementAtIndex(0).vector3Value = new Vector3(end.x, seat.y, end.z);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"CTLIFE [venues] {spec.id}: seat motion profile bound from {spec.seatMotionSource} ({n} exit routes).");
        }

        private static string LocalId(string id)
        {
            int dot = id.IndexOf('.');
            return dot >= 0 ? id.Substring(dot + 1) : id;
        }
    }
}
