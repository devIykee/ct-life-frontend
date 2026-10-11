using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.World
{
    /// <summary>
    /// The CT Island v2 heightfield (tools/world/islandgen): N x N samples, Cell metres apart, row 0 = south, stored as
    /// little-endian uint16 between HMin and HMax. Holes (tunnel portals) are a packed bit set. The same code builds the
    /// terrain chunk meshes in the editor and at runtime, so the ground is generated on load instead of shipped as meshes.
    /// </summary>
    public sealed class IslandHeightfield
    {
        public int N;
        public float Cell, MinX, MinZ, Size, HMin, HMax, Sea, FlightCeiling;
        public float[] H;          // [iz * N + ix]
        public bool[] Hole;

        public float At(int ix, int iz) => H[Mathf.Clamp(iz, 0, N - 1) * N + Mathf.Clamp(ix, 0, N - 1)];
        public bool HoleAt(int ix, int iz) => Hole != null && ix >= 0 && iz >= 0 && ix < N && iz < N && Hole[iz * N + ix];
        public float X(int ix) => MinX + ix * Cell;
        public float Z(int iz) => MinZ + iz * Cell;

        /// <summary>Bilinear ground height at island x/z (y metres).</summary>
        public float Sample(float x, float z)
        {
            float fx = (x - MinX) / Cell, fz = (z - MinZ) / Cell;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            float tx = fx - ix, tz = fz - iz;
            float a = At(ix, iz), b = At(ix + 1, iz), c = At(ix, iz + 1), d = At(ix + 1, iz + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        public Vector3 Normal(int ix, int iz)
        {
            float dx = (At(ix + 1, iz) - At(ix - 1, iz)) / (2f * Cell);
            float dz = (At(ix, iz + 1) - At(ix, iz - 1)) / (2f * Cell);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        public static IslandHeightfield FromBytes(byte[] r16, byte[] holeBits, int n, float cell, float minX, float minZ, float hmin, float hmax, float sea, float ceiling)
        {
            var h = new IslandHeightfield { N = n, Cell = cell, MinX = minX, MinZ = minZ, Size = (n - 1) * cell, HMin = hmin, HMax = hmax, Sea = sea, FlightCeiling = ceiling };
            if (r16 == null || r16.Length != n * n * 2) throw new System.IO.InvalidDataException($"heightfield is {r16?.Length ?? 0} bytes, expected {n * n * 2}");
            h.H = new float[n * n];
            float range = hmax - hmin;
            for (int i = 0; i < h.H.Length; i++) h.H[i] = hmin + (r16[2 * i] | (r16[2 * i + 1] << 8)) / 65535f * range;
            if (holeBits != null && holeBits.Length * 8 >= n * n)
            {
                h.Hole = new bool[n * n];
                for (int i = 0; i < h.Hole.Length; i++) h.Hole[i] = (holeBits[i >> 3] & (1 << (i & 7))) != 0;
            }
            return h;
        }

        public static byte[] PackHoles(bool[] holes)
        {
            var b = new byte[(holes.Length + 7) / 8];
            for (int i = 0; i < holes.Length; i++) if (holes[i]) b[i >> 3] |= (byte)(1 << (i & 7));
            return b;
        }

        public const float SkirtDepth = 2.5f;

        /// <summary>
        /// A grid mesh over cells [x0, x0 + cells] x [z0, z0 + cells] every <paramref name="stride"/> samples, vertices relative
        /// to <paramref name="origin"/>. Skirts (both windings) hide LOD seams; hole cells are left open.
        /// </summary>
        public Mesh Grid(int x0, int z0, int cells, int stride, bool skirt, string name, Vector3 origin, float yOffset = 0f, bool holes = true)
        {
            int n = cells / stride;
            int quads = N - 1;
            var v = new List<Vector3>((n + 1) * (n + 1) + 4 * (n + 1));
            var nr = new List<Vector3>(v.Capacity);
            var t = new List<int>(n * n * 6 + (skirt ? n * 48 : 0));
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    int gx = Mathf.Min(x0 + i * stride, quads), gz = Mathf.Min(z0 + j * stride, quads);
                    v.Add(new Vector3(X(gx) - origin.x, At(gx, gz) + yOffset, Z(gz) - origin.z));
                    nr.Add(Normal(gx, gz));
                }
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    if (holes && Hole != null && HoleQuad(x0 + i * stride, z0 + j * stride, stride)) continue;
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    t.Add(a); t.Add(c); t.Add(b);
                    t.Add(b); t.Add(c); t.Add(d);
                }
            if (skirt)
            {
                void Edge(System.Func<int, int> idx)
                {
                    int first = v.Count;
                    for (int k = 0; k <= n; k++)
                    {
                        var p = v[idx(k)];
                        v.Add(new Vector3(p.x, p.y - SkirtDepth, p.z));
                        nr.Add(nr[idx(k)]);
                    }
                    for (int k = 0; k < n; k++)
                    {
                        int t0 = idx(k), t1 = idx(k + 1), b0 = first + k, b1 = first + k + 1;
                        t.Add(t0); t.Add(t1); t.Add(b1); t.Add(t0); t.Add(b1); t.Add(b0);
                        t.Add(t1); t.Add(t0); t.Add(b0); t.Add(t1); t.Add(b0); t.Add(b1);
                    }
                }
                Edge(k => k);
                Edge(k => n * (n + 1) + k);
                Edge(k => k * (n + 1));
                Edge(k => k * (n + 1) + n);
            }
            var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v);
            m.SetNormals(nr);
            m.SetTriangles(t, 0, true);
            m.RecalculateBounds();
            return m;
        }

        private bool HoleQuad(int gx, int gz, int stride)
        {
            for (int dz = 0; dz <= stride; dz++)
                for (int dx = 0; dx <= stride; dx++)
                    if (HoleAt(gx + dx, gz + dz)) return true;
            return false;
        }

        /// <summary>Highest and lowest sample over a cell block (to skip deep-sea chunks).</summary>
        public void Range(int x0, int z0, int cells, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            int quads = N - 1;
            for (int z = z0; z <= Mathf.Min(z0 + cells, quads); z++)
                for (int x = x0; x <= Mathf.Min(x0 + cells, quads); x++)
                {
                    float y = At(x, z);
                    if (y < min) min = y;
                    if (y > max) max = y;
                }
        }
    }
}
