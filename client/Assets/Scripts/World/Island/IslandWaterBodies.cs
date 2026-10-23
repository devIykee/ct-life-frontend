using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>Query geometry copied from the rendered lake/river triangles. Sea uses the terrain shoreline.</summary>
    [CreateAssetMenu(menuName = "CT Life/World/Island Water Bodies")]
    public sealed class IslandWaterBodies : ScriptableObject
    {
        [Serializable] public struct Triangle { public Vector3 a, b, c; }
        public Triangle[] triangles = Array.Empty<Triangle>();
        // Bucket candidates, then interpolate the actual triangle: no shoreline raster rounding.
        private const float BucketSize = 32f;
        private Dictionary<Vector2Int, List<int>> _buckets;

        public void Invalidate() => _buckets = null;

        public float SurfaceAt(float x, float z, IslandHeightfield ground, float sea)
        {
            if (ground == null || float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z)) return float.NaN;
            float terrain = ground.Sample(x, z);
            float surface = terrain < sea ? sea : float.NaN;
            if (_buckets == null) Index();
            if (!_buckets.TryGetValue(Key(x, z), out var candidates)) return surface;
            foreach (int i in candidates)
            {
                var t = triangles[i];
                if (!HeightInTriangle(x, z, t, out float y) || terrain >= y) continue;
                if (float.IsNaN(surface) || y > surface) surface = y;
            }
            return surface;
        }

        public static bool HeightInTriangle(float x, float z, Triangle t, out float y)
        {
            float ax = t.a.x - t.c.x, az = t.a.z - t.c.z;
            float bx = t.b.x - t.c.x, bz = t.b.z - t.c.z;
            float det = ax * bz - bx * az;
            y = float.NaN;
            if (Mathf.Abs(det) < 1e-8f) return false;
            float px = x - t.c.x, pz = z - t.c.z;
            float u = (px * bz - bx * pz) / det, v = (ax * pz - px * az) / det;
            const float edge = 1e-5f;
            if (u < -edge || v < -edge || u + v > 1f + edge) return false;
            y = u * t.a.y + v * t.b.y + (1f - u - v) * t.c.y;
            return true;
        }

        public static Triangle[] Capture(Transform waterRoot)
        {
            var result = new List<Triangle>();
            if (waterRoot == null) return result.ToArray();
            foreach (var f in waterRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                // Sea meshes intentionally overlap inland ground. The heightfield defines their wet extent.
                if (f.name.StartsWith("sea_", StringComparison.Ordinal)) continue;
                var mesh = f.sharedMesh;
                if (mesh == null || !mesh.isReadable) throw new InvalidOperationException("Water query mesh is unreadable: " + f.name);
                var v = mesh.vertices; var indices = mesh.triangles;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                    result.Add(new Triangle { a = f.transform.TransformPoint(v[indices[i]]),
                        b = f.transform.TransformPoint(v[indices[i + 1]]), c = f.transform.TransformPoint(v[indices[i + 2]]) });
            }
            return result.ToArray();
        }

        private static Vector2Int Key(float x, float z) => new Vector2Int(Mathf.FloorToInt(x / BucketSize), Mathf.FloorToInt(z / BucketSize));
        private void Index()
        {
            _buckets = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < triangles.Length; i++)
            {
                var t = triangles[i];
                var min = Key(Mathf.Min(t.a.x, t.b.x, t.c.x), Mathf.Min(t.a.z, t.b.z, t.c.z));
                var max = Key(Mathf.Max(t.a.x, t.b.x, t.c.x), Mathf.Max(t.a.z, t.b.z, t.c.z));
                for (int z = min.y; z <= max.y; z++) for (int x = min.x; x <= max.x; x++)
                {
                    var key = new Vector2Int(x, z);
                    if (!_buckets.TryGetValue(key, out var list)) _buckets[key] = list = new List<int>();
                    list.Add(i);
                }
            }
        }
    }
}
