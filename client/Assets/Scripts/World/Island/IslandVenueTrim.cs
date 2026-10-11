using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// A venue diorama on the island keeps only what lies inside its own frame. Standing alone, a diorama paints its
    /// surroundings: sky cards, far hills, skylines and a sea plane, some of them merged into its sign mesh. On the
    /// island the real world is around it, so anything beyond the frame (plus a margin) is removed. Renderers entirely
    /// outside are switched off; merged meshes that straddle the edge are rebuilt without the outside triangles, once
    /// per source mesh (cached).
    /// </summary>
    public static class IslandVenueTrim
    {
        private static readonly Dictionary<Mesh, Mesh> Trimmed = new Dictionary<Mesh, Mesh>();

        public static int Trim(Transform root, Rect localFrame, float margin = 4f, float maxHeight = 400f)
        {
            int changed = 0;
            var keep = new Rect(localFrame.xMin - margin, localFrame.yMin - margin, localFrame.width + 2f * margin, localFrame.height + 2f * margin);
            var toLocal = root.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mesh == null || mr == null) continue;
                var m = toLocal * mf.transform.localToWorldMatrix;
                // bounds in venue-local space
                var b = mesh.bounds;
                Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    var c = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1)));
                    mn = Vector3.Min(mn, c); mx = Vector3.Max(mx, c);
                }
                bool inside = mn.x >= keep.xMin && mx.x <= keep.xMax && mn.z >= keep.yMin && mx.z <= keep.yMax && mx.y <= maxHeight;
                if (inside) continue;
                bool outside = mx.x < keep.xMin || mn.x > keep.xMax || mx.z < keep.yMin || mn.z > keep.yMax || mn.y > maxHeight;
                if (outside || !mesh.isReadable) { mr.enabled = false; changed++; continue; }
                if (!Trimmed.TryGetValue(mesh, out var t)) Trimmed[mesh] = t = Cut(mesh, m, keep, maxHeight);
                if (t == null) { mr.enabled = false; changed++; continue; }
                mf.sharedMesh = t;
                changed++;
            }
            return changed;
        }

        private static Mesh Cut(Mesh src, Matrix4x4 toLocal, Rect keep, float maxHeight)
        {
            var v = src.vertices;
            var o = Object.Instantiate(src);
            o.name = src.name + " (island)";
            bool any = false;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tris = src.GetTriangles(s);
                var kept = new List<int>(tris.Length);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var c = (toLocal.MultiplyPoint3x4(v[tris[i]]) + toLocal.MultiplyPoint3x4(v[tris[i + 1]]) + toLocal.MultiplyPoint3x4(v[tris[i + 2]])) / 3f;
                    if (c.x < keep.xMin || c.x > keep.xMax || c.z < keep.yMin || c.z > keep.yMax || c.y > maxHeight) continue;
                    kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]);
                }
                if (kept.Count > 0) any = true;
                o.SetTriangles(kept, s, false);
            }
            o.RecalculateBounds();
            return any ? o : null;
        }
    }
}
