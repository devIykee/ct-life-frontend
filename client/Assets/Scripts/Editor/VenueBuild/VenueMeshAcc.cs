using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Accumulates flat-shaded geometry for one material. Positions are in venue-root space. Used both for
    /// procedural blocks (palette UVs) and for merging sourced CC0 model submeshes, so a venue renders as a few
    /// large static meshes instead of hundreds of tiny draws (runtime-instantiated prefabs get no static batching).
    /// </summary>
    public sealed class VenueMeshAcc
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Indices = new List<int>();

        public int Triangles => Indices.Count / 3;
        public bool Empty => Indices.Count == 0;

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = Positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(Positions);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            // a-b-c-d run clockwise as seen from the front (Unity's front-face winding): bottom-left,
            // top-left, top-right, bottom-right. Cross(up, right) gives the outward normal in Unity's left-handed space.
            var n = Vector3.Cross(b - a, d - a).normalized;
            int i = Positions.Count;
            Positions.Add(a); Positions.Add(b); Positions.Add(c); Positions.Add(d);
            Normals.Add(n); Normals.Add(n); Normals.Add(n); Normals.Add(n);
            Uvs.Add(ua); Uvs.Add(ub); Uvs.Add(uc); Uvs.Add(ud);
            Indices.Add(i); Indices.Add(i + 1); Indices.Add(i + 2);
            Indices.Add(i); Indices.Add(i + 2); Indices.Add(i + 3);
        }

        private void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 uv)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            int i = Positions.Count;
            Positions.Add(a); Positions.Add(b); Positions.Add(c);
            Normals.Add(n); Normals.Add(n); Normals.Add(n);
            Uvs.Add(uv); Uvs.Add(uv); Uvs.Add(uv);
            Indices.Add(i); Indices.Add(i + 1); Indices.Add(i + 2);
        }

        /// <summary>Oriented box. <paramref name="m"/> maps the unit cube (-0.5..0.5) to venue space.</summary>
        public void Box(Matrix4x4 m, Vector2 uv, bool bottom = false)
        {
            Vector3 P(float x, float y, float z) => m.MultiplyPoint3x4(new Vector3(x, y, z));
            var p000 = P(-.5f, -.5f, -.5f); var p100 = P(.5f, -.5f, -.5f); var p110 = P(.5f, .5f, -.5f); var p010 = P(-.5f, .5f, -.5f);
            var p001 = P(-.5f, -.5f, .5f); var p101 = P(.5f, -.5f, .5f); var p111 = P(.5f, .5f, .5f); var p011 = P(-.5f, .5f, .5f);
            // Front (-z), back (+z), left (-x), right (+x), top (+y), bottom (-y): outward normals.
            Quad(p000, p010, p110, p100, uv, uv, uv, uv);
            Quad(p101, p111, p011, p001, uv, uv, uv, uv);
            Quad(p001, p011, p010, p000, uv, uv, uv, uv);
            Quad(p100, p110, p111, p101, uv, uv, uv, uv);
            Quad(p010, p011, p111, p110, uv, uv, uv, uv);
            if (bottom) Quad(p000, p100, p101, p001, uv, uv, uv, uv);
        }

        /// <summary>Upright prism (cylinder) of <paramref name="sides"/> sides, centred on its base.</summary>
        public void Cylinder(Matrix4x4 m, float radiusBottom, float radiusTop, float height, int sides, Vector2 uv, bool caps = true)
        {
            Vector3 P(Vector3 p) => m.MultiplyPoint3x4(p);
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                var b0 = new Vector3(Mathf.Cos(a0) * radiusBottom, 0, Mathf.Sin(a0) * radiusBottom);
                var b1 = new Vector3(Mathf.Cos(a1) * radiusBottom, 0, Mathf.Sin(a1) * radiusBottom);
                var t0 = new Vector3(Mathf.Cos(a0) * radiusTop, height, Mathf.Sin(a0) * radiusTop);
                var t1 = new Vector3(Mathf.Cos(a1) * radiusTop, height, Mathf.Sin(a1) * radiusTop);
                if (radiusTop > 0.0001f) Quad(P(b0), P(t0), P(t1), P(b1), uv, uv, uv, uv);
                else Tri(P(b0), P(t0), P(b1), uv);
                if (caps && radiusTop > 0.0001f) Tri(P(new Vector3(0, height, 0)), P(t1), P(t0), uv);
            }
        }

        /// <summary>
        /// Annular sector block (curved riser, stage step): radii r0..r1, angles a0..a1 in degrees (0 = +z, 90 = +x),
        /// from y0 to y1 around <paramref name="c"/>. Top, inner and outer faces and both end caps (optionally the bottom).
        /// </summary>
        public void Arc(Vector3 c, float r0, float r1, float a0, float a1, float y0, float y1, int segments, Vector2 uv, bool bottom = false)
        {
            Vector3 P(float r, float a, float y) => new Vector3(c.x + Mathf.Sin(a * Mathf.Deg2Rad) * r, y, c.z + Mathf.Cos(a * Mathf.Deg2Rad) * r);
            for (int i = 0; i < segments; i++)
            {
                float t0 = Mathf.Lerp(a0, a1, i / (float)segments), t1 = Mathf.Lerp(a0, a1, (i + 1) / (float)segments);
                Quad(P(r0, t0, y1), P(r1, t0, y1), P(r1, t1, y1), P(r0, t1, y1), uv, uv, uv, uv);              // top (+y)
                Quad(P(r1, t1, y0), P(r1, t1, y1), P(r1, t0, y1), P(r1, t0, y0), uv, uv, uv, uv);              // outer
                if (r0 > 0.001f) Quad(P(r0, t0, y0), P(r0, t0, y1), P(r0, t1, y1), P(r0, t1, y0), uv, uv, uv, uv); // inner
                if (bottom) Quad(P(r0, t0, y0), P(r0, t1, y0), P(r1, t1, y0), P(r1, t0, y0), uv, uv, uv, uv);
            }
            Quad(P(r1, a0, y0), P(r1, a0, y1), P(r0, a0, y1), P(r0, a0, y0), uv, uv, uv, uv);
            Quad(P(r0, a1, y0), P(r0, a1, y1), P(r1, a1, y1), P(r1, a1, y0), uv, uv, uv, uv);
        }

        /// <summary>Single triangle (pennant, gable end); optionally two-sided.</summary>
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector2 uv, bool twoSided = true)
        {
            Tri(a, b, c, uv);
            if (twoSided) Tri(a, c, b, uv);
        }

        /// <summary>Low-poly UV sphere.</summary>
        public void Sphere(Matrix4x4 m, float radius, int rings, int segments, Vector2 uv)
        {
            Vector3 S(int r, int s)
            {
                float phi = Mathf.PI * r / rings, theta = 2f * Mathf.PI * s / segments;
                return m.MultiplyPoint3x4(new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)) * radius);
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    var a = S(r, s); var b = S(r, s + 1); var c = S(r + 1, s + 1); var d = S(r + 1, s);
                    if (r == 0) Tri(a, c, d, uv);
                    else if (r == rings - 1) Tri(a, b, d, uv);
                    else Quad(a, b, c, d, uv, uv, uv, uv);
                }
        }

        /// <summary>Single-sided rectangle facing local -Z (towards a viewer at negative z), with a UV rect.</summary>
        public void Panel(Matrix4x4 m, float width, float height, Rect uvRect)
        {
            float w = width * .5f;
            var a = m.MultiplyPoint3x4(new Vector3(-w, 0, 0));
            var b = m.MultiplyPoint3x4(new Vector3(-w, height, 0));
            var c = m.MultiplyPoint3x4(new Vector3(w, height, 0));
            var d = m.MultiplyPoint3x4(new Vector3(w, 0, 0));
            Quad(a, b, c, d, new Vector2(uvRect.xMin, uvRect.yMin), new Vector2(uvRect.xMin, uvRect.yMax),
                new Vector2(uvRect.xMax, uvRect.yMax), new Vector2(uvRect.xMax, uvRect.yMin));
        }

        /// <summary>Flat horizontal rectangle (top facing +Y) centred at the matrix origin.</summary>
        public void Flat(Matrix4x4 m, float width, float depth, Rect uvRect)
        {
            float w = width * .5f, d = depth * .5f;
            var a = m.MultiplyPoint3x4(new Vector3(-w, 0, -d));
            var b = m.MultiplyPoint3x4(new Vector3(-w, 0, d));
            var c = m.MultiplyPoint3x4(new Vector3(w, 0, d));
            var e = m.MultiplyPoint3x4(new Vector3(w, 0, -d));
            Quad(a, b, c, e, new Vector2(uvRect.xMin, uvRect.yMin), new Vector2(uvRect.xMin, uvRect.yMax),
                new Vector2(uvRect.xMax, uvRect.yMax), new Vector2(uvRect.xMax, uvRect.yMin));
        }

        /// <summary>Copy one submesh of a sourced mesh (only the vertices it uses), transformed into venue space.</summary>
        public void AppendSubmesh(Mesh source, int submesh, Matrix4x4 m, Rect? uvRemap = null,
            Dictionary<Mesh, MeshCache> cache = null, Vector4? uvST = null)
        {
            var data = MeshCache.Get(source, cache);
            var tris = data.Submeshes[submesh];
            var normalMatrix = m.inverse.transpose;
            bool mirrored = m.determinant < 0f;
            var remap = new Dictionary<int, int>();
            foreach (int src in tris)
            {
                if (remap.ContainsKey(src)) continue;
                remap[src] = Positions.Count;
                Positions.Add(m.MultiplyPoint3x4(data.Vertices[src]));
                var n = data.Normals.Length > src ? normalMatrix.MultiplyVector(data.Normals[src]).normalized : Vector3.up;
                Normals.Add(n);
                var uv = data.Uv.Length > src ? data.Uv[src] : Vector2.zero;
                if (uvST.HasValue) uv = new Vector2(uv.x * uvST.Value.x + uvST.Value.z, uv.y * uvST.Value.y + uvST.Value.w);
                if (uvRemap.HasValue)
                {
                    var r = uvRemap.Value;
                    uv = new Vector2(r.xMin + Mathf.Clamp01(uv.x) * r.width, r.yMin + Mathf.Clamp01(uv.y) * r.height);
                }
                Uvs.Add(uv);
            }
            for (int i = 0; i < tris.Length; i += 3)
            {
                if (mirrored)
                {
                    Indices.Add(remap[tris[i]]); Indices.Add(remap[tris[i + 2]]); Indices.Add(remap[tris[i + 1]]);
                }
                else
                {
                    Indices.Add(remap[tris[i]]); Indices.Add(remap[tris[i + 1]]); Indices.Add(remap[tris[i + 2]]);
                }
            }
        }

        public sealed class MeshCache
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector2[] Uv;
            public int[][] Submeshes;

            public static MeshCache Get(Mesh mesh, Dictionary<Mesh, MeshCache> cache)
            {
                if (cache != null && cache.TryGetValue(mesh, out var hit)) return hit;
                var c = new MeshCache { Vertices = mesh.vertices, Normals = mesh.normals, Uv = mesh.uv, Submeshes = new int[mesh.subMeshCount][] };
                for (int s = 0; s < mesh.subMeshCount; s++) c.Submeshes[s] = mesh.GetTriangles(s);
                if (cache != null) cache[mesh] = c;
                return c;
            }
        }
    }
}
