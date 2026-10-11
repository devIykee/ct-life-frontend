using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>Accumulates triangles with the vertex streams the world shaders read (uv0..uv2, colour).</summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV0 = new List<Vector2>();
        public readonly List<Vector2> UV1 = new List<Vector2>();
        public readonly List<Vector2> UV2 = new List<Vector2>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<int> T = new List<int>();
        public bool HasUV1, HasUV2, HasColor;

        public int Count => V.Count;
        public bool Empty => T.Count == 0;

        public int Add(Vector3 p, Vector3 n, Vector2 uv0, Vector2 uv1 = default, Vector2 uv2 = default, Color c = default)
        {
            V.Add(p); N.Add(n); UV0.Add(uv0); UV1.Add(uv1); UV2.Add(uv2);
            C.Add(HasColor ? c : Color.white);
            return V.Count - 1;
        }

        public void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        /// <summary>
        /// A flat-shaded quad from four points listed CLOCKWISE as seen from its front (Unity's front-face winding).
        /// uv: a = (0, 0), b = (|ab|, 0), c = (|ab|, |bc|), d = (0, |bc|) in metres times uvScale.
        /// </summary>
        public void FlatQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uvScale = default, Color col = default)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            if (uvScale == default) uvScale = Vector2.one;
            float w = Vector3.Distance(a, b) * uvScale.x, h = Vector3.Distance(b, c) * uvScale.y;
            int i0 = Add(a, n, new Vector2(0, 0), default, default, col);
            int i1 = Add(b, n, new Vector2(w, 0), default, default, col);
            int i2 = Add(c, n, new Vector2(w, h), default, default, col);
            int i3 = Add(d, n, new Vector2(0, h), default, default, col);
            Tri(i0, i1, i2); Tri(i0, i2, i3);
        }

        /// <summary>
        /// Strip between two polylines with the same point count (the 'left' and 'right' edges of a ribbon whose front faces
        /// up when 'left' is on the left of the travel direction). uv0.x = lateral metres given per edge, uv0.y = along.
        /// </summary>
        public void Strip(IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right, IReadOnlyList<float> along, float uLeft, float uRight,
            Vector3? upOverride = null, Vector2 uv1 = default, Vector2 uv2 = default, Color col = default)
        {
            int n = left.Count;
            int start = V.Count;
            for (int k = 0; k < n; k++)
            {
                var fwd = (k < n - 1 ? left[k + 1] - left[k] : left[k] - left[k - 1]);
                var across = right[k] - left[k];
                var nrm = upOverride ?? Vector3.Cross(fwd, across).normalized;
                if (!upOverride.HasValue && nrm.y < 0f && Mathf.Abs(across.y) < across.magnitude * 0.7f) nrm = -nrm;
                Add(left[k], nrm, new Vector2(uLeft, along[k]), uv1, uv2, col);
                Add(right[k], nrm, new Vector2(uRight, along[k]), uv1, uv2, col);
            }
            for (int k = 0; k < n - 1; k++)
            {
                int a = start + 2 * k, b = a + 1, c = a + 2, d = a + 3;      // a = L0, b = R0, c = L1, d = R1
                Tri(a, c, b); Tri(b, c, d);
            }
        }

        /// <summary>Axis-aligned or oriented box (centre, half extents along the given right/up/forward axes).</summary>
        public void Box(Vector3 center, Vector3 right, Vector3 up, Vector3 fwd, Vector3 half, Color col = default)
        {
            Vector3 X = right.normalized * half.x, Y = up.normalized * half.y, Z = fwd.normalized * half.z;
            Vector3 P(int sx, int sy, int sz) => center + X * sx + Y * sy + Z * sz;
            FlatQuad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), default, col);     // +Z
            FlatQuad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), default, col); // -Z
            FlatQuad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), default, col);     // +X
            FlatQuad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), default, col); // -X
            FlatQuad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), default, col);     // +Y
            FlatQuad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), default, col); // -Y
        }

        public void Append(MeshBuilder o)
        {
            int b = V.Count;
            V.AddRange(o.V); N.AddRange(o.N); UV0.AddRange(o.UV0); UV1.AddRange(o.UV1); UV2.AddRange(o.UV2); C.AddRange(o.C);
            foreach (var t in o.T) T.Add(t + b);
            HasUV1 |= o.HasUV1; HasUV2 |= o.HasUV2; HasColor |= o.HasColor;
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(V);
            m.SetNormals(N);
            m.SetUVs(0, UV0);
            if (HasUV1) m.SetUVs(1, UV1);
            if (HasUV2) m.SetUVs(2, UV2);
            if (HasColor) m.SetColors(C);
            m.SetTriangles(T, 0, true);
            m.RecalculateBounds();
            return m;
        }
    }
}
