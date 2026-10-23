using System;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Same-frame CPU occlusion for the kit renderer (perf lane, 2026-10-22). Opaque boxes known to be inside drawn geometry
    /// (the v3 buildings' backing boxes) are rasterised into a small depth buffer, then instance bounds are tested against
    /// it. Everything errs towards "visible": occluders cover a pixel only when the whole pixel is inside them and write the
    /// farthest depth they reach within it, faces are clipped at the near plane, and a tested box with any corner behind the
    /// camera, or off the buffer, counts as visible. It runs on this frame's camera, so nothing lags or pops.
    /// </summary>
    public sealed class IslandOcclusion
    {
        public const int Width = 192;
        public int Height { get; private set; }
        private float[] _z = Array.Empty<float>();      // per pixel: nearest occluder's conservative (farthest) view depth
        private Matrix4x4 _vp;
        private Vector3 _eye;
        private bool _any;
        public int Occluders { get; private set; }

        private const float NearW = 0.05f;
        private readonly Vector4[] _clip = new Vector4[8];
        private readonly Vector3[] _world = new Vector3[8];
        private readonly Vector4[] _polyA = new Vector4[8], _polyB = new Vector4[8];
        private readonly Vector3[] _scr = new Vector3[8];     // x, y in pixels, z = 1/w

        // box faces as corner indices (corner bit 0 = +x, bit 1 = +y, bit 2 = +z), each wound the same way seen from outside
        private static readonly int[][] Faces =
        {
            new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 },   // -x, +x
            new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 },   // -y, +y
            new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 },   // -z, +z
        };
        private static readonly Vector3[] Normals = { Vector3.left, Vector3.right, Vector3.down, Vector3.up, Vector3.back, Vector3.forward };

        public void Begin(Camera cam)
        {
            Height = Mathf.Clamp(Mathf.RoundToInt(Width / Mathf.Max(0.3f, cam.aspect)), 32, 256);
            int n = Width * Height;
            if (_z.Length != n) _z = new float[n];
            for (int i = 0; i < n; i++) _z[i] = float.PositiveInfinity;
            _vp = cam.projectionMatrix * cam.worldToCameraMatrix;
            _eye = cam.transform.position;
            _any = false;
            Occluders = 0;
        }

        /// <summary>Rasterises the box [min, max] in the space of <paramref name="m"/> (a TRS matrix) as an occluder.</summary>
        public void AddBox(in Matrix4x4 m, Vector3 min, Vector3 max)
        {
            for (int c = 0; c < 8; c++)
            {
                var p = new Vector3((c & 1) != 0 ? max.x : min.x, (c & 2) != 0 ? max.y : min.y, (c & 4) != 0 ? max.z : min.z);
                var w = m.MultiplyPoint3x4(p);
                _world[c] = w;
                _clip[c] = _vp * new Vector4(w.x, w.y, w.z, 1f);
            }
            bool drew = false;
            for (int f = 0; f < 6; f++)
            {
                var fi = Faces[f];
                // front faces only (the box is convex and closed: a front face is always nearer than the back face behind it)
                var nrm = m.MultiplyVector(Normals[f]);
                if (Vector3.Dot(nrm, _eye - _world[fi[0]]) <= 0f) continue;
                int count = 4;
                for (int k = 0; k < 4; k++) _polyA[k] = _clip[fi[k]];
                count = ClipNear(_polyA, count, _polyB);
                if (count < 3) continue;
                drew |= Raster(_polyB, count);
            }
            if (drew) { _any = true; Occluders++; }
        }

        /// <summary>False only when the box is certainly hidden behind occluders.</summary>
        public bool Visible(in Bounds b) => Test(b, false) != 0;

        /// <summary>0 hidden, 1 (partly) visible, 2 nothing in front of it anywhere on its rectangle (so nothing inside it is hidden).</summary>
        public int Classify(in Bounds b) => Test(b, true);

        private int Test(in Bounds b, bool classify)
        {
            if (!_any) return 2;
            // clip-space centre and half-axes (plain floats: eight corners by addition, not eight matrix products)
            var c = b.center; var e = b.extents;
            var m = _vp;
            float cx = m.m00 * c.x + m.m01 * c.y + m.m02 * c.z + m.m03, cy = m.m10 * c.x + m.m11 * c.y + m.m12 * c.z + m.m13;
            float cw = m.m30 * c.x + m.m31 * c.y + m.m32 * c.z + m.m33;
            float ax = m.m00 * e.x, ay = m.m10 * e.x, aw = m.m30 * e.x;
            float bx = m.m01 * e.y, by = m.m11 * e.y, bw = m.m31 * e.y;
            float dx = m.m02 * e.z, dy = m.m12 * e.z, dw = m.m32 * e.z;
            // nearest depth of the box (w is view depth); any corner at or behind the near plane: visible
            float spread = Mathf.Abs(aw) + Mathf.Abs(bw) + Mathf.Abs(dw);
            float near = cw - spread, far = cw + spread;
            if (near <= NearW) return 1;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                float sa = (k & 1) != 0 ? 1f : -1f, sb = (k & 2) != 0 ? 1f : -1f, sd = (k & 4) != 0 ? 1f : -1f;
                float w = cw + sa * aw + sb * bw + sd * dw;
                float iw = 1f / w;
                float sx = ((cx + sa * ax + sb * bx + sd * dx) * iw * 0.5f + 0.5f) * Width;
                float sy = ((cy + sa * ay + sb * by + sd * dy) * iw * 0.5f + 0.5f) * Height;
                if (sx < x0) x0 = sx; if (sx > x1) x1 = sx; if (sy < y0) y0 = sy; if (sy > y1) y1 = sy;
            }
            int px0 = Mathf.Max(0, (int)Mathf.Floor(x0)), px1 = Mathf.Min(Width - 1, (int)Mathf.Ceil(x1) - 1);
            int py0 = Mathf.Max(0, (int)Mathf.Floor(y0)), py1 = Mathf.Min(Height - 1, (int)Mathf.Ceil(y1) - 1);
            if (px0 > px1 || py0 > py1) return 1;                                    // off screen: the frustum test owns it
            var z = _z;
            if (classify)
            {
                // every pixel's occluder (if any) behind the box's far depth: nothing within the box can be hidden
                bool clear = true, seen = false;
                for (int y = py0; y <= py1; y++)
                {
                    int row = y * Width;
                    for (int x = px0; x <= px1; x++)
                    {
                        float v = z[row + x];
                        if (v >= near) seen = true;
                        if (v < far) clear = false;
                        if (seen && !clear) return 1;
                    }
                }
                return clear ? 2 : seen ? 1 : 0;
            }
            for (int y = py0; y <= py1; y++)
            {
                int row = y * Width;
                for (int x = px0; x <= px1; x++) if (z[row + x] >= near) return 1;
            }
            return 0;
        }

        /// <summary>Sutherland-Hodgman against w &gt; NearW (clip space).</summary>
        private static int ClipNear(Vector4[] src, int n, Vector4[] dst)
        {
            int o = 0;
            for (int i = 0; i < n; i++)
            {
                var a = src[i]; var b = src[(i + 1) % n];
                bool ina = a.w > NearW, inb = b.w > NearW;
                if (ina) dst[o++] = a;
                if (ina != inb)
                {
                    float t = (NearW - a.w) / (b.w - a.w);
                    dst[o++] = a + (b - a) * t;
                }
            }
            return o;
        }

        /// <summary>
        /// Inner-conservative convex polygon fill: a pixel is covered only if its whole square is inside every edge; its depth
        /// is the farthest the face reaches within the pixel (1/w is affine in screen space, so its minimum is at a corner).
        /// </summary>
        private bool Raster(Vector4[] poly, int n)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var v = poly[i];
                float iw = 1f / v.w;
                var s = new Vector3((v.x * iw * 0.5f + 0.5f) * Width, (v.y * iw * 0.5f + 0.5f) * Height, iw);
                _scr[i] = s;
                if (s.x < minX) minX = s.x; if (s.x > maxX) maxX = s.x; if (s.y < minY) minY = s.y; if (s.y > maxY) maxY = s.y;
            }
            // signed area: orientation of the projected polygon
            float area = 0f;
            for (int i = 0; i < n; i++) { var a = _scr[i]; var b = _scr[(i + 1) % n]; area += a.x * b.y - b.x * a.y; }
            if (Mathf.Abs(area) < 2f) return false;                                   // under ~one pixel: skip
            float sign = area > 0f ? 1f : -1f;
            // depth plane 1/w = A x + B y + C from the first three non-collinear vertices
            var p0 = _scr[0]; var p1 = _scr[1]; var p2 = _scr[2];
            for (int k = 2; k < n; k++)
            {
                p2 = _scr[k];
                float cr = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
                if (Mathf.Abs(cr) > 1e-3f) break;
            }
            float det = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
            if (Mathf.Abs(det) < 1e-4f) return false;
            float A = ((p1.z - p0.z) * (p2.y - p0.y) - (p2.z - p0.z) * (p1.y - p0.y)) / det;
            float B = ((p2.z - p0.z) * (p1.x - p0.x) - (p1.z - p0.z) * (p2.x - p0.x)) / det;
            float C = p0.z - A * p0.x - B * p0.y;
            float depthSlack = 0.5f * (Mathf.Abs(A) + Mathf.Abs(B));
            int x0 = Mathf.Max(0, Mathf.CeilToInt(minX)), x1 = Mathf.Min(Width - 1, Mathf.FloorToInt(maxX) - 1);
            int y0 = Mathf.Max(0, Mathf.CeilToInt(minY)), y1 = Mathf.Min(Height - 1, Mathf.FloorToInt(maxY) - 1);
            if (x0 > x1 || y0 > y1) return false;
            bool any = false;
            for (int y = y0; y <= y1; y++)
            {
                float cy = y + 0.5f;
                int row = y * Width;
                for (int x = x0; x <= x1; x++)
                {
                    float cx = x + 0.5f;
                    bool inside = true;
                    for (int i = 0; i < n && inside; i++)
                    {
                        var a = _scr[i]; var b = _scr[(i + 1) % n];
                        float ex = b.x - a.x, ey = b.y - a.y;
                        float e = sign * (ex * (cy - a.y) - ey * (cx - a.x));
                        if (e < 0.5f * (Mathf.Abs(ex) + Mathf.Abs(ey))) inside = false;
                    }
                    if (!inside) continue;
                    float iw = A * cx + B * cy + C - depthSlack;
                    float z = iw > 1e-6f ? 1f / iw : float.PositiveInfinity;
                    if (z < _z[row + x]) _z[row + x] = z;
                    any = true;
                }
            }
            return any;
        }
    }
}
