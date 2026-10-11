using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Prop collision for every venue. A prop is a floor-standing object (sourced model or registered procedural piece)
    /// whose footprint touches the walkable area. Each prop gets a BoxCollider fitted to its oriented bounds (tree:
    /// trunk only) and, unless it is seat furniture, a not-walkable NavMesh volume so agents path around it. Seat
    /// furniture (its footprint holds a claimable spot) keeps the anchor and approach untouched: collider only.
    /// <see cref="Validate"/> fails the build when a prop has no collider or a non-seat prop is still on the NavMesh.
    /// </summary>
    public static class VenueCollision
    {
        public sealed class PropBox
        {
            public string name;
            public Vector3 center;      // venue-local, centre of the collision box
            public Vector3 size;
            public float yaw;
            public bool seat, tree;
        }

        private const float MinHeight = 0.15f, MaxBottom = 0.45f, MaxFootprint = 6f, ColliderTop = 2.2f;
        private static readonly string[] TreeWords = { "tree", "palm" };
        // Thin poles with overhead parts (canopy, lamp head, softbox): collide with the pole, not the head.
        private static readonly string[] PoleWords = { "parasol", "lamp", "light_stand", "mic_stand", "iv_stand", "studio_light", "light_floor" };

        /// <summary>Oriented bounds of a model's LOD0 renderers under <paramref name="m"/> (venue-local).</summary>
        public static PropBox FromRenderers(string name, IEnumerable<Renderer> renderers, Matrix4x4 rootToVenue, Matrix4x4 venueFromRoot)
        {
            var list = renderers.Where(r => r != null && VB.LodIndex(r.name) == 0).ToList();
            if (list.Count == 0) return null;
            bool any = false;
            var lb = new Bounds();
            foreach (var r in list)
            {
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var toRoot = rootToVenue.inverse * r.localToWorldMatrix; // renderer -> prop root space
                var b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = toRoot.MultiplyPoint3x4(c);
                    if (!any) { lb = new Bounds(p, Vector3.zero); any = true; } else lb.Encapsulate(p);
                }
            }
            if (!any) return null;
            return FromLocalBounds(name, lb, venueFromRoot);
        }

        /// <summary>Box from bounds in a prop's own space and the prop-to-venue matrix (yaw + scale only).</summary>
        public static PropBox FromLocalBounds(string name, Bounds lb, Matrix4x4 m)
        {
            var scale = new Vector3(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude, m.GetColumn(2).magnitude);
            float yaw = Mathf.Atan2(m.m02, m.m22) * Mathf.Rad2Deg;
            return new PropBox { name = name, center = m.MultiplyPoint3x4(lb.center), size = Vector3.Scale(lb.size, scale), yaw = yaw };
        }

        /// <summary>Filter raw boxes to floor props near the walkable area; mark seats and trees; clamp heights.</summary>
        public static List<PropBox> Classify(IEnumerable<PropBox> raw, VenueSpots spots, Rect walk)
        {
            var claimable = spots.spots.Values.Distinct().Where(s => s.hasPosition && s.Claimable).ToList();
            var outList = new List<PropBox>();
            var near = new Rect(walk.xMin - 0.3f, walk.yMin - 0.3f, walk.width + 0.6f, walk.height + 0.6f);
            foreach (var p in raw)
            {
                if (p == null) continue;
                float bottom = p.center.y - p.size.y * .5f, top = p.center.y + p.size.y * .5f;
                if (top - Mathf.Max(bottom, 0f) < MinHeight || bottom > MaxBottom) continue;
                if (Mathf.Max(p.size.x, p.size.z) > MaxFootprint) continue;
                string lower = p.name.ToLowerInvariant();
                p.tree = TreeWords.Any(lower.Contains) && p.size.y > 2.5f;
                bool pole = !p.tree && PoleWords.Any(lower.Contains) && p.size.y > 1.2f;
                if (p.tree || pole)
                {
                    // Trunk / pole only; the canopy or head is above head height.
                    float w = p.tree ? 0.5f : 0.35f, h = Mathf.Min(ColliderTop, top);
                    p.size = new Vector3(w, h, w);
                    p.center = new Vector3(p.center.x, h * .5f, p.center.z);
                    p.yaw = 0f;
                }
                else
                {
                    float h = Mathf.Min(top, ColliderTop) - Mathf.Max(bottom, 0f);
                    p.size = new Vector3(Mathf.Max(p.size.x, 0.12f), h, Mathf.Max(p.size.z, 0.12f));
                    p.center = new Vector3(p.center.x, Mathf.Max(bottom, 0f) + h * .5f, p.center.z);
                }
                if (!Intersects(p, near)) continue;
                p.seat = !p.tree && claimable.Any(s => Contains(p, new Vector3(s.x, 0, s.z), 0.35f));
                outList.Add(p);
            }
            return outList;
        }

        public static bool Contains(PropBox p, Vector3 point, float margin)
        {
            var local = Quaternion.Euler(0, -p.yaw, 0) * (new Vector3(point.x, 0, point.z) - new Vector3(p.center.x, 0, p.center.z));
            return Mathf.Abs(local.x) <= p.size.x * .5f + margin && Mathf.Abs(local.z) <= p.size.z * .5f + margin;
        }

        private static bool Intersects(PropBox p, Rect r)
        {
            // Conservative: the box's axis-aligned footprint against the rect.
            var q = Quaternion.Euler(0, p.yaw, 0);
            float ex = Mathf.Abs((q * new Vector3(p.size.x * .5f, 0, 0)).x) + Mathf.Abs((q * new Vector3(0, 0, p.size.z * .5f)).x);
            float ez = Mathf.Abs((q * new Vector3(p.size.x * .5f, 0, 0)).z) + Mathf.Abs((q * new Vector3(0, 0, p.size.z * .5f)).z);
            return p.center.x + ex >= r.xMin && p.center.x - ex <= r.xMax && p.center.z + ez >= r.yMin && p.center.z - ez <= r.yMax;
        }

        /// <summary>One BoxCollider per prop under <paramref name="parent"/>, skipping props existing colliders already cover.</summary>
        public static int AddColliders(Transform parent, GameObject root, List<PropBox> props)
        {
            int added = 0;
            foreach (var p in props)
            {
                if (Coverage(root, p) >= 0.6f) continue;
                var go = new GameObject("Prop collider " + p.name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = p.center;
                go.transform.localRotation = Quaternion.Euler(0, p.yaw, 0);
                go.AddComponent<BoxCollider>().size = p.size;
                added++;
            }
            return added;
        }

        public static IEnumerable<NavMeshBuildSource> Obstacles(List<PropBox> props) =>
            props.Where(p => !p.seat).Select(p => new NavMeshBuildSource
            {
                // Reaches 0.5 m below the floor so the walkable surface voxels are always inside the modifier.
                shape = NavMeshBuildSourceShape.ModifierBox,
                transform = Matrix4x4.TRS(new Vector3(p.center.x, (Mathf.Max(p.size.y, 1f) - 0.5f) * .5f, p.center.z), Quaternion.Euler(0, p.yaw, 0), Vector3.one),
                size = new Vector3(p.size.x, Mathf.Max(p.size.y, 1f) + 0.5f, p.size.z), area = 1,
            });

        /// <summary>Fraction of the prop's footprint (3×3 samples at knee height) inside some non-trigger collider.</summary>
        public static float Coverage(GameObject root, PropBox p)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToList();
            float y = Mathf.Min(p.center.y - p.size.y * .5f + 0.3f, p.center.y);
            int inside = 0, total = 0;
            var q = Quaternion.Euler(0, p.yaw, 0);
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    var local = new Vector3(i * p.size.x * .35f, 0, j * p.size.z * .35f);
                    var pt = root.transform.TransformPoint(new Vector3(p.center.x, y, p.center.z) + q * local);
                    total++;
                    if (colliders.Any(c => Inside(c, pt))) inside++;
                }
            return inside / (float)total;
        }

        private static bool Inside(Collider c, Vector3 worldPoint)
        {
            if (c is BoxCollider b)
            {
                var l = b.transform.InverseTransformPoint(worldPoint) - b.center;
                var e = b.size * .5f + Vector3.one * 0.02f;
                return Mathf.Abs(l.x) <= e.x && Mathf.Abs(l.y) <= e.y && Mathf.Abs(l.z) <= e.z;
            }
            if (c is MeshCollider mc && mc.sharedMesh != null)
            {
                var l = mc.transform.InverseTransformPoint(worldPoint);
                var bb = mc.sharedMesh.bounds;
                bb.Expand(0.04f);
                return bb.Contains(l);
            }
            return c.bounds.Contains(worldPoint);
        }

        /// <summary>
        /// Build gate: every prop must be covered by a collider (≥ 60% of its footprint samples) and every non-seat prop
        /// must be off the NavMesh. Throws with the full list of offenders.
        /// </summary>
        public static JObject Validate(string id, GameObject root, NavMeshData navigation, List<PropBox> props)
        {
            var missing = new List<string>();
            var walkThrough = new List<string>();
            NavMeshDataInstance instance = default;
            if (navigation != null) instance = NavMesh.AddNavMeshData(navigation, root.transform.position, root.transform.rotation);
            try
            {
                foreach (var p in props)
                {
                    if (Coverage(root, p) < 0.6f) missing.Add(p.name + "@" + p.center.ToString("F1"));
                    if (!p.seat && navigation != null)
                    {
                        var c = root.transform.TransformPoint(new Vector3(p.center.x, 0, p.center.z));
                        if (NavMesh.SamplePosition(c, out _, 0.2f, NavMesh.AllAreas)) walkThrough.Add(p.name + "@" + p.center.ToString("F1"));
                    }
                }
            }
            finally
            {
                if (instance.valid) NavMesh.RemoveNavMeshData(instance);
            }
            if (missing.Count > 0 || walkThrough.Count > 0)
                throw new InvalidDataException($"{id}: prop collision gate failed. Without collider: [{string.Join(", ", missing)}]. " +
                                               $"Walk-through (on NavMesh): [{string.Join(", ", walkThrough)}].");
            return new JObject
            {
                ["props"] = props.Count, ["seatProps"] = props.Count(p => p.seat), ["trees"] = props.Count(p => p.tree),
                ["obstacles"] = props.Count(p => !p.seat),
            };
        }

        // ------------------------------------------------------------------ props from an authored (repack source) prefab

        private static HashSet<string> _manifestIds;

        /// <summary>Environment manifest model ids (props are instances of these; merged shells/joinery are not).</summary>
        public static bool IsManifestModel(string objectName)
        {
            if (_manifestIds == null)
                _manifestIds = new HashSet<string>(Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Assets/Art/environment/manifest.json"))
                    .Properties().Select(p => p.Name));
            string n = objectName.StartsWith("Direction_", StringComparison.Ordinal) ? objectName.Substring(10) : objectName;
            int lod = n.LastIndexOf("_LOD", StringComparison.Ordinal);
            if (lod > 0) n = n.Substring(0, lod);
            return _manifestIds.Contains(n);
        }

        /// <summary>
        /// Prop roots of an instantiated source prefab: LODGroup owners, else renderer objects (LOD suffix stripped),
        /// limited to instances of manifest models (merged walls, floors and joinery keep their own colliders).
        /// </summary>
        public static List<PropBox> FromSourceInstance(GameObject root)
        {
            var groups = new Dictionary<Transform, List<Renderer>>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var owner = r.GetComponentInParent<LODGroup>(true)?.transform;
                if (owner == null) owner = r.name.Contains("_LOD") && r.transform.parent != null && r.transform.parent != root.transform ? r.transform.parent : r.transform;
                if (!groups.TryGetValue(owner, out var list)) groups[owner] = list = new List<Renderer>();
                list.Add(r);
            }
            var toVenue = root.transform.worldToLocalMatrix;
            return groups.Where(g => IsManifestModel(g.Key.name)).Select(g => FromRenderers(g.Key.name, g.Value, g.Key.localToWorldMatrix, toVenue * g.Key.localToWorldMatrix))
                .Where(p => p != null).ToList();
        }
    }
}
