using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Short walks around props for the on-foot body (Feel lane F1, 2026-10-17). A seat's route can cross a bench or a
    /// planter that no NavMesh carved (the body now collides with every prop); when a leg of the route is blocked for a
    /// body-sized capsule, this finds a way round on a small grid (A* over capsule checks, evaluated lazily, 0.2 m cells,
    /// a window around the leg) and string-pulls it to a few corners. No NavMesh involved.
    /// </summary>
    public static class FootPlanner
    {
        public const float Cell = 0.2f, Margin = 1.6f;
        public const int MaxCells = 64;

        private static Vector3 Bottom(Vector3 p) => p + Vector3.up * (CharacterBody.StepOffset + CharacterBody.Radius);
        private static Vector3 Top(Vector3 p) => p + Vector3.up * (CharacterBody.Height - CharacterBody.Radius);

        /// <summary>
        /// Should a route walk straight from <paramref name="a"/> to <paramref name="b"/>? A planning capsule (kerb-high floor,
        /// a little clearance) finds nothing in the way, so the body neither bumps a prop nor rides up a planter rim.
        /// </summary>
        public static bool Clear(Vector3 a, Vector3 b)
        {
            var d = b - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 1e-3f) return true;
            float r = CharacterBody.Radius + Clearance;
            return !Physics.CapsuleCast(a + Vector3.up * (PlanFloor + r), Top(a), r, d / len, len, CharacterBody.Mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Is there room for the body standing at <paramref name="p"/> (above kerb height)?</summary>
        public static bool Free(Vector3 p) =>
            !Physics.CheckCapsule(Bottom(p), Top(p), CharacterBody.Radius, CharacterBody.Mask, QueryTriggerInteraction.Ignore);

        /// <summary>
        /// Planning clearance: routes keep a hand's width off props and treat anything above a kerb as in the way (the body
        /// could step onto a 0.3 m planter rim, but a route should not ask it to).
        /// </summary>
        public const float Clearance = 0.08f, PlanFloor = 0.18f;
        private static bool Roomy(Vector3 p)
        {
            float r = CharacterBody.Radius + Clearance;
            return !Physics.CheckCapsule(p + Vector3.up * (PlanFloor + r), Top(p), r, CharacterBody.Mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Corners from <paramref name="a"/> to <paramref name="b"/> (b last) round whatever blocks the straight line, or null
        /// when the window holds no way (or is too big to search).
        /// </summary>
        public static List<Vector3> Route(Vector3 a, Vector3 b)
        {
            if (Clear(a, b)) return new List<Vector3> { b };
            var min = new Vector2(Mathf.Min(a.x, b.x) - Margin, Mathf.Min(a.z, b.z) - Margin);
            var max = new Vector2(Mathf.Max(a.x, b.x) + Margin, Mathf.Max(a.z, b.z) + Margin);
            int nx = Mathf.CeilToInt((max.x - min.x) / Cell) + 1, nz = Mathf.CeilToInt((max.y - min.y) / Cell) + 1;
            if (nx > MaxCells || nz > MaxCells) return null;
            float y = Mathf.Max(a.y, b.y);
            Vector3 At(int i, int j) => new Vector3(min.x + i * Cell, y, min.y + j * Cell);
            (int, int) CellOf(Vector3 p) => (Mathf.Clamp(Mathf.RoundToInt((p.x - min.x) / Cell), 0, nx - 1), Mathf.Clamp(Mathf.RoundToInt((p.z - min.y) / Cell), 0, nz - 1));
            var state = new sbyte[nx * nz]; // 0 unknown, 1 free, -1 blocked
            bool Open(int i, int j)
            {
                int k = j * nx + i;
                if (state[k] == 0) state[k] = (sbyte)(Roomy(At(i, j)) ? 1 : -1);
                return state[k] > 0;
            }
            var (si, sj) = CellOf(a);
            var (gi, gj) = CellOf(b);
            // The goal (a seat's approach) may touch the furniture: aim for the free cell nearest to it.
            if (!Open(gi, gj))
            {
                float best = float.MaxValue; int bi = -1, bj = -1;
                for (int dj = -3; dj <= 3; dj++)
                    for (int di = -3; di <= 3; di++)
                    {
                        int i = gi + di, j = gj + dj;
                        if (i < 0 || j < 0 || i >= nx || j >= nz || !Open(i, j)) continue;
                        float dd = di * di + dj * dj;
                        if (dd < best) { best = dd; bi = i; bj = j; }
                    }
                if (bi < 0) return null;
                gi = bi; gj = bj;
            }
            var g = new float[nx * nz];
            var from = new int[nx * nz];
            for (int k = 0; k < g.Length; k++) { g[k] = float.MaxValue; from[k] = -1; }
            var open = new List<int>();
            int start = sj * nx + si, goal = gj * nx + gi;
            g[start] = 0f; open.Add(start);
            float H(int k) { int i = k % nx, j = k / nx; return Mathf.Sqrt((i - gi) * (i - gi) + (j - gj) * (j - gj)); }
            int expanded = 0;
            while (open.Count > 0 && expanded < nx * nz)
            {
                int bestAt = 0; float bestF = float.MaxValue;
                for (int q = 0; q < open.Count; q++) { float f = g[open[q]] + H(open[q]); if (f < bestF) { bestF = f; bestAt = q; } }
                int cur = open[bestAt]; open.RemoveAt(bestAt); expanded++;
                if (cur == goal) break;
                int ci = cur % nx, cj = cur / nx;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int i = ci + di, j = cj + dj;
                        if (i < 0 || j < 0 || i >= nx || j >= nz) continue;
                        int k = j * nx + i;
                        if (k != start && !Open(i, j)) continue;
                        if (di != 0 && dj != 0 && (!Open(ci + di, cj) || !Open(ci, cj + dj))) continue; // no corner cutting
                        float cost = g[cur] + (di != 0 && dj != 0 ? 1.4142f : 1f);
                        if (cost >= g[k]) continue;
                        if (g[k] == float.MaxValue) open.Add(k);
                        g[k] = cost; from[k] = cur;
                    }
            }
            if (from[goal] < 0 && goal != start) return null;
            var cells = new List<Vector3>();
            for (int k = goal; k >= 0 && k != start; k = from[k]) cells.Add(At(k % nx, k / nx));
            cells.Reverse();
            // String-pull: keep a corner only where the straight leg past it is blocked.
            var route = new List<Vector3>();
            var anchor = a;
            for (int q = 0; q < cells.Count; q++)
            {
                var next = q + 1 < cells.Count ? cells[q + 1] : b;
                if (!Clear(anchor, next)) { route.Add(cells[q]); anchor = cells[q]; }
            }
            route.Add(b);
            return route;
        }
    }
}
