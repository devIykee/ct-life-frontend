using System.Collections.Generic;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// CT Island v2 waterfront (World lane): stone quay walls with a cap and a paved quay strip along the marina basin and
    /// the Trenches harbour (plan.json quays), the crescent bay boardwalk (timber deck on posts with a railing and steps
    /// down to the beach, plan.json boardwalk), and the lighthouse on the south-east headland (procedural: banded tower,
    /// gallery, lantern room, cap).
    /// </summary>
    public static class IslandV2Coast
    {
        private static Vector3 Up(float y) => new Vector3(0f, y, 0f);

        public static void Build(JObject plan, IslandHeightfield h, CellSet cells, JObject receipt)
        {
            int quayM = 0, deckM = 0;
            if (plan["quays"] is JArray quays) foreach (var q in quays) quayM += Quay((JObject)q, h, cells);
            if (plan["boardwalk"] is JArray bws) foreach (var b in bws) deckM += Boardwalk((JObject)b, h, cells);
            var lh = ((JArray)plan["landmarks"])?.FirstOrDefault(l => (string)l["id"] == "lighthouse");
            if (lh != null) Lighthouse(IslandV2Paths.V2(lh["at"]), h, cells);
            receipt["quayMetres"] = quayM; receipt["boardwalkMetres"] = deckM; receipt["lighthouse"] = lh != null;
        }

        private static Vector3[] Line(JToken pts, IList<float> ys) =>
            ((JArray)pts).Select((p, i) => new Vector3((float)p[0], ys[i], (float)p[1])).ToArray();

        private static float[] Floats(JToken a) => ((JArray)a).Select(v => (float)v).ToArray();

        // ------------------------------------------------------------------ quays

        private static int Quay(JObject q, IslandHeightfield h, CellSet cells)
        {
            var top = Floats(q["top"]);
            var innerTop = Floats(q["innerTop"]);
            float bottom = (float)q["bottom"];
            int n = top.Length;
            var wallTop = Line(q["wall"], top.Select(y => y + 0.32f).ToList());
            var wallBottom = Line(q["wall"], Enumerable.Repeat(bottom, n).ToList());
            var edge = Line(q["edge"], top.Select(y => y + 0.32f).ToList());
            var edgeLow = Line(q["edge"], top.ToList());
            var inner = Line(q["inner"], innerTop.ToList());
            var along = new float[n];
            for (int i = 1; i < n; i++) along[i] = along[i - 1] + Vector3.Distance(wallTop[i], wallTop[i - 1]);
            // pieces of ~32 m, each in the cell of its middle
            for (int a = 0; a < n - 1; a += 16)
            {
                int b = Mathf.Min(n - 1, a + 16);
                var cell = cells.At(wallTop[(a + b) / 2]);
                T[] S<T>(T[] arr) => arr.Skip(a).Take(b - a + 1).ToArray();
                var al = S(along);
                var wall = cell.V(Mat.Quay);
                // the sea face (the outline runs counter-clockwise, so the sea is on the right; Strip(top, bottom) faces right)
                wall.Strip(S(wallTop), S(wallBottom), al, 0, 0);
                // the cap from the coast line out to the face, then the paved quay (left edge first: they face up)
                wall.Strip(S(edge), S(wallTop), al, 0, 0);
                cell.V(Mat.Plaza).Strip(S(inner), S(edgeLow), al, 0, 0);
                cell.Col(Layers.Road).Strip(S(inner), S(edgeLow), al, 0, 0);
            }
            return Mathf.RoundToInt(along[n - 1]);
        }

        // ------------------------------------------------------------------ boardwalk

        private static int Boardwalk(JObject b, IslandHeightfield h, CellSet cells)
        {
            var y = Floats(b["y"]);
            var ground = Floats(b["ground"]);
            var steps = new HashSet<int>(((JArray)b["steps"] ?? new JArray()).Select(v => (int)v));
            int n = y.Length;
            var inner = Line(b["inner"], y.ToList());
            var outer = Line(b["outer"], y.ToList());
            var along = new float[n];
            for (int i = 1; i < n; i++) along[i] = along[i - 1] + Vector3.Distance(outer[i], outer[i - 1]);
            const float fascia = 0.32f, railH = 1.0f;
            for (int a = 0; a < n - 1; a += 16)
            {
                int e = Mathf.Min(n - 1, a + 16);
                var cell = cells.At(outer[(a + e) / 2]);
                T[] S<T>(T[] arr) => arr.Skip(a).Take(e - a + 1).ToArray();
                var al = S(along);
                var deck = cell.V(Mat.Boardwalk);
                var wood = cell.V(Mat.Timber);
                // inner -> outer: the deck faces up when inner is on the left of travel; sort that out per run
                var dirs = S(outer);
                bool leftIsInner = Vector3.Cross(dirs[dirs.Length - 1] - dirs[0], S(inner)[0] - dirs[0]).y < 0f;   // y < 0: on the left
                if (leftIsInner) deck.Strip(S(inner), S(outer), al, 0f, 6f); else deck.Strip(S(outer), S(inner), al, 6f, 0f);
                var col = cell.Col(Layers.Road);
                if (leftIsInner) col.Strip(S(inner), S(outer), al, 0f, 0f); else col.Strip(S(outer), S(inner), al, 0f, 0f);
                var o = S(outer); var g = S(ground);
                var low = o.Select(p => p - Up(fascia)).ToArray();
                if (leftIsInner) wood.Strip(o, low, al, 0, 0); else wood.Strip(low, o, al, 0, 0);
                // posts down to the sand, the railing on the sea side (gaps at the steps)
                for (int i = 0; i < o.Length; i++)
                {
                    int k = a + i;
                    var p = o[i];
                    var t = (k < n - 1 ? outer[k + 1] - outer[k] : outer[k] - outer[k - 1]).normalized;
                    var rn = Vector3.Cross(Vector3.up, t).normalized;
                    float drop = Mathf.Max(0.4f, p.y - g[i] + 0.3f);
                    if (k % 2 == 0) wood.Box(p - Up(fascia + drop * 0.5f) - rn * 0.15f, rn, Vector3.up, t, new Vector3(0.12f, drop * 0.5f, 0.12f));
                    bool gap = steps.Any(s => Mathf.Abs(k - s) <= 1);
                    if (!gap && k < n - 1)
                    {
                        var q2 = outer[k + 1];
                        var mid = (p + q2) * 0.5f;
                        float len = Vector3.Distance(p, q2) * 0.5f + 0.02f;
                        wood.Box(mid + Up(railH), rn, Vector3.up, t, new Vector3(0.05f, 0.05f, len));
                        wood.Box(mid + Up(railH * 0.55f), rn, Vector3.up, t, new Vector3(0.035f, 0.035f, len));
                        wood.Box(p + Up(railH * 0.5f), rn, Vector3.up, t, new Vector3(0.05f, railH * 0.5f, 0.05f));
                        cell.Boxes.Add(new CellGeo.BoxCol { Center = mid + Up(railH * 0.5f), Rotation = Quaternion.LookRotation(t, Vector3.up), Size = new Vector3(0.12f, railH, len * 2f), Layer = Layers.Prop, Name = "railing" });
                    }
                }
            }
            // steps down to the beach where the beach paths begin
            foreach (int s in steps)
            {
                if (s <= 0 || s >= n - 1) continue;
                var p = outer[s];
                var t = (outer[s + 1] - outer[s - 1]).normalized;
                var seaDir = (outer[s] - inner[s]).normalized;
                float drop = Mathf.Max(0.3f, p.y - ground[s]);
                int count = Mathf.Clamp(Mathf.CeilToInt(drop / 0.18f), 2, 10);
                var cell = cells.At(p);
                var wood = cell.V(Mat.Timber);
                for (int i = 0; i < count; i++)
                {
                    float top = p.y - (i + 1) * drop / count;
                    var c = p + seaDir * (0.3f + i * 0.32f + 0.16f);
                    float hh = Mathf.Max(0.05f, top - ground[s] + 0.3f);
                    wood.Box(new Vector3(c.x, top - hh * 0.5f, c.z), t, Vector3.up, seaDir, new Vector3(1.6f, hh * 0.5f, 0.16f));
                    cell.Boxes.Add(new CellGeo.BoxCol { Center = new Vector3(c.x, top - hh * 0.5f, c.z), Rotation = Quaternion.LookRotation(seaDir, Vector3.up), Size = new Vector3(3.2f, hh, 0.32f), Layer = Layers.Road, Name = "step" });
                }
            }
            return Mathf.RoundToInt(along[n - 1]);
        }

        // ------------------------------------------------------------------ lighthouse

        private static void Lighthouse(Vector2 at, IslandHeightfield h, CellSet cells)
        {
            float y0 = float.MaxValue;
            for (int i = 0; i < 12; i++) { float a = i * Mathf.PI / 6f; y0 = Mathf.Min(y0, h.Sample(at.x + Mathf.Cos(a) * 4f, at.y + Mathf.Sin(a) * 4f)); }
            y0 -= 0.3f;
            var basePos = new Vector3(at.x, y0, at.y);
            var cell = cells.At(basePos);
            var tower = cell.V(Mat.Lighthouse, color: true);
            const int sides = 10;
            const float height = 19f, r0 = 3.3f, r1 = 2.2f, band = 3.2f;
            var red = new Color(0.82f, 0.18f, 0.16f); var white = new Color(0.96f, 0.95f, 0.92f);
            // banded tapered tower: rings every 0.8 m, colour by band
            int rings = Mathf.CeilToInt(height / 0.8f);
            for (int k = 0; k < rings; k++)
            {
                float ya = k * height / rings, yb = (k + 1) * height / rings;
                float ra = Mathf.Lerp(r0, r1, ya / height), rb = Mathf.Lerp(r0, r1, yb / height);
                var col = Mathf.FloorToInt((ya + 0.4f) / band) % 2 == 0 ? white : red;
                for (int s = 0; s < sides; s++)
                {
                    float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                    Vector3 P(float a, float r, float yy) => basePos + new Vector3(Mathf.Cos(a) * r, yy, Mathf.Sin(a) * r);
                    tower.FlatQuad(P(a0, ra, ya), P(a0, rb, yb), P(a1, rb, yb), P(a1, ra, ya), default, col);
                }
            }
            // gallery deck and railing, lantern room, cap
            float gy = height;
            Ring(tower, basePos + Up(gy), 3.4f, 0.35f, sides, new Color(0.25f, 0.26f, 0.28f));
            for (int s = 0; s < 16; s++)
            {
                float a = s * Mathf.PI * 2f / 16f;
                var p = basePos + new Vector3(Mathf.Cos(a) * 3.25f, gy + 0.85f, Mathf.Sin(a) * 3.25f);
                tower.Box(p, Vector3.right, Vector3.up, Vector3.forward, new Vector3(0.05f, 0.5f, 0.05f), new Color(0.2f, 0.2f, 0.22f));
            }
            var glass = cell.V(Mat.LighthouseLamp);
            for (int s = 0; s < 8; s++)
            {
                float a0 = s * Mathf.PI / 4f, a1 = (s + 1) * Mathf.PI / 4f;
                Vector3 P(float a, float yy) => basePos + new Vector3(Mathf.Cos(a) * 1.7f, yy, Mathf.Sin(a) * 1.7f);
                glass.FlatQuad(P(a0, gy + 0.35f), P(a0, gy + 2.6f), P(a1, gy + 2.6f), P(a1, gy + 0.35f));
            }
            // red cap: an eight-sided cone with a ball on top
            for (int s = 0; s < 8; s++)
            {
                float a0 = s * Mathf.PI / 4f, a1 = (s + 1) * Mathf.PI / 4f;
                var c0 = basePos + new Vector3(Mathf.Cos(a0) * 2.1f, gy + 2.6f, Mathf.Sin(a0) * 2.1f);
                var c1 = basePos + new Vector3(Mathf.Cos(a1) * 2.1f, gy + 2.6f, Mathf.Sin(a1) * 2.1f);
                var apex = basePos + Up(gy + 4.4f);
                int i0 = tower.Add(c0, Vector3.up, Vector2.zero, default, default, red);
                int i1 = tower.Add(apex, Vector3.up, Vector2.zero, default, default, red);
                int i2 = tower.Add(c1, Vector3.up, Vector2.zero, default, default, red);
                var nrm = Vector3.Cross(apex - c0, c1 - c0).normalized;
                tower.N[i0] = nrm; tower.N[i1] = nrm; tower.N[i2] = nrm;
                tower.Tri(i0, i1, i2);
            }
            tower.Box(basePos + Up(gy + 4.6f), Vector3.right, Vector3.up, Vector3.forward, new Vector3(0.25f, 0.25f, 0.25f), red);
            cell.Boxes.Add(new CellGeo.BoxCol { Center = basePos + Up(height * 0.5f), Rotation = Quaternion.identity, Size = new Vector3(r0 * 1.7f, height, r0 * 1.7f), Layer = Layers.Building, Name = "lighthouse" });
        }

        /// <summary>A flat ring platform (annulus edge strip plus top) at y, radius r, thickness t.</summary>
        private static void Ring(MeshBuilder b, Vector3 c, float r, float t, int sides, Color col)
        {
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                var p0 = c + new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r);
                var p1 = c + new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
                b.FlatQuad(p0 - Up(t), p0, p1, p1 - Up(t), default, col);
                int i0 = b.Add(c, Vector3.up, Vector2.zero, default, default, col);
                int i1 = b.Add(p1, Vector3.up, Vector2.zero, default, default, col);
                int i2 = b.Add(p0, Vector3.up, Vector2.zero, default, default, col);
                b.Tri(i0, i1, i2);
            }
        }
    }
}
