using System.Collections.Generic;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// Venue building shells (World lane): for every venue with a plan.json shell, four walls round its room (the front
    /// one with the door), a flat roof with a parapet or a gable roof, and the name sign over the door, in the venue's
    /// own colours on the procedural facade shader. Built in the venue's cell; VenueShell fades the roof and the
    /// camera-side walls while the player is inside.
    /// </summary>
    public static class IslandV2Shells
    {
        public sealed class Def
        {
            public string VenueId;
            public MeshBuilder Front, Back, Left, Right, Roof, Sign;
            public Material Facade, RoofMat, SignMat;
            public Vector3 Origin; public float Yaw; public Rect Footprint; public float Height;
            public Vector3[] Normals, Centres;
            public bool HasDoor = true;
            /// <summary>Landmark extras (dome, phone, bunting, cairn) in their own renderer, and their materials.</summary>
            public MeshBuilder Extra; public Material ExtraMat;
        }

        private static readonly Dictionary<string, int> StyleIds = new Dictionary<string, int>
        {
            { "plaster", 0 }, { "glass", 1 }, { "industrial", 2 }, { "timber", 3 }, { "arena", 4 }, { "arcade", 5 },
        };

        public static int Build(JObject plan, CellSet cells, Dictionary<string, Material> mats, JObject receipt)
        {
            int n = 0;
            foreach (JObject v in (JArray)plan["venues"])
            {
                if (!(v["shell"] is JObject sh)) continue;
                var def = Shell(v, sh, mats);
                cells.At(def.Origin).Shells.Add(def);
                n++;
            }
            receipt["venueShells"] = n;
            // W3 landmark buildings: the same building kit on their own pads, no door (no interior), plus their feature
            int lm = 0;
            if (plan["landmarkBuildings"] is JArray lbs)
                foreach (JObject lb in lbs)
                {
                    var at = IslandV2Paths.V2(lb["at"]); var size = IslandV2Paths.V2(lb["size"]);
                    float y = (float)lb["y"];
                    var v = new JObject
                    {
                        ["id"] = (string)lb["id"], ["origin"] = new JArray(at.x, y, at.y), ["yaw"] = (float)lb["yaw"],
                        ["frame_min"] = new JArray(-size.x / 2f, -size.y / 2f), ["frame_size"] = new JArray(size.x, size.y),
                        ["walk"] = new JArray(-size.x / 2f + 1f, -size.y / 2f + 0.7f, size.x / 2f - 1f, size.y / 2f - 1f),
                    };
                    Def def;
                    if ((float)lb["h"] > 0f) { def = Shell(v, lb, mats, door: false); }
                    else def = new Def { VenueId = (string)lb["id"], Origin = new Vector3(at.x, y, at.y), Yaw = (float)lb["yaw"], Footprint = new Rect(-size / 2f, size), HasDoor = false };
                    Extras(def, lb, mats);
                    cells.At(def.Origin).Shells.Add(def);
                    lm++;
                }
            receipt["landmarkBuildings"] = lm;
            return n;
        }

        private static Color C(JToken t) => new Color((float)t[0], (float)t[1], (float)t[2]);

        private static Def Shell(JObject v, JObject sh, Dictionary<string, Material> mats, bool door = true)
        {
            var id = (string)v["id"];
            var o = IslandV2Paths.V3(v["origin"]);
            float yaw = (float)v["yaw"];
            var mn = IslandV2Paths.V2(v["frame_min"]); var sz = IslandV2Paths.V2(v["frame_size"]);
            var walk = (JArray)v["walk"];
            float h = (float)sh["h"];
            string roofKind = (string)sh["roof"] ?? "flat";
            var wall = C(sh["wall"]); var trim = C(sh["trim"]); var accent = C(sh["accent"]);
            // footprint: the frame's sides and back, the front at the room's front edge (the frame's apron stays outside)
            float x0 = mn.x - 0.3f, x1 = mn.x + sz.x + 0.3f, z0 = (float)walk[1] - 0.7f, z1 = mn.y + sz.y + 0.3f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 W(float lx, float ly, float lz) => o + rot * new Vector3(lx, ly, lz);
            var def = new Def
            {
                VenueId = id, Origin = o, Yaw = yaw, Footprint = Rect.MinMaxRect(x0, z0, x1, z1), Height = h,
                Front = new MeshBuilder { HasUV1 = true, HasColor = true }, Back = new MeshBuilder { HasUV1 = true, HasColor = true },
                Left = new MeshBuilder { HasUV1 = true, HasColor = true }, Right = new MeshBuilder { HasUV1 = true, HasColor = true },
                Roof = new MeshBuilder { HasColor = true }, Sign = new MeshBuilder(),
            };
            const float baseY = -0.4f;
            float pitch = sh["pitch"] != null ? (float)sh["pitch"] : 28f;
            float gableRise = roofKind == "gable" ? Mathf.Min(x1 - x0, z1 - z0) * 0.5f * Mathf.Tan(pitch * Mathf.Deg2Rad) : 0f;
            // a wall from local (ax, az) to (bx, bz), outward normal on its right seen from outside: quads in u (along) and
            // v (up) with uv0 metres and uv1 = (length, height); holes: the door on the front wall
            void Wall(MeshBuilder b, Vector2 a, Vector2 c, float top, (float u0, float u1, float v1)? hole, float gable)
            {
                float len = Vector2.Distance(a, c);
                var dir = (c - a) / len;
                Vector3 P(float u, float y) { var q = a + dir * u; return W(q.x, y, q.y); }
                void Q(float u0, float u1, float v0, float v1)
                {
                    if (u1 - u0 < 0.01f || v1 - v0 < 0.01f) return;
                    var p0 = P(u0, v0); var p1 = P(u0, v1); var p2 = P(u1, v1); var p3 = P(u1, v0);
                    var nrm = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                    var uv1 = new Vector2(len, top);
                    int i0 = b.Add(p0, nrm, new Vector2(u0, v0), uv1, default, wall);
                    int i1 = b.Add(p1, nrm, new Vector2(u0, v1), uv1, default, wall);
                    int i2 = b.Add(p2, nrm, new Vector2(u1, v1), uv1, default, wall);
                    int i3 = b.Add(p3, nrm, new Vector2(u1, v0), uv1, default, wall);
                    b.Tri(i0, i1, i2); b.Tri(i0, i2, i3);
                }
                if (hole is (float hu0, float hu1, float hv1))
                {
                    Q(0f, hu0, baseY, top); Q(hu1, len, baseY, top); Q(hu0, hu1, hv1, top);
                }
                else Q(0f, len, baseY, top);
                if (gable > 0f)
                {
                    // the gable triangle above the wall
                    var p0 = P(0f, top); var p1 = P(len * 0.5f, top + gable); var p2 = P(len, top);
                    var nrm = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                    var uv1 = new Vector2(len, top + gable);
                    int i0 = b.Add(p0, nrm, new Vector2(0f, top), uv1, default, wall);
                    int i1 = b.Add(p1, nrm, new Vector2(len * 0.5f, top + gable), uv1, default, wall);
                    int i2 = b.Add(p2, nrm, new Vector2(len, top), uv1, default, wall);
                    b.Tri(i0, i1, i2);
                }
            }
            float doorX = 0f, doorW = Mathf.Min(5.2f, (x1 - x0) * 0.4f), doorH = Mathf.Min(4.4f, h - 1.2f);
            // seen from outside, each wall runs left to right: front (local -z side) from x1 to x0? No: the viewer outside
            // the front looks along +z, their right is +x, so the front wall runs from x0 to x1 and faces -z.
            bool gableAlongX = (x1 - x0) >= (z1 - z0);       // the ridge runs along the long side
            float gFront = roofKind == "gable" && !gableAlongX ? gableRise : 0f, gSide = roofKind == "gable" && gableAlongX ? gableRise : 0f;
            def.HasDoor = door;
            Wall(def.Front, new Vector2(x0, z0), new Vector2(x1, z0), h, door ? (doorX - doorW / 2f - x0, doorX + doorW / 2f - x0, doorH) : ((float, float, float)?)null, gFront);
            Wall(def.Back, new Vector2(x1, z1), new Vector2(x0, z1), h, null, gFront);
            Wall(def.Left, new Vector2(x0, z1), new Vector2(x0, z0), h, null, gSide);
            Wall(def.Right, new Vector2(x1, z0), new Vector2(x1, z1), h, null, gSide);
            def.Normals = new[] { rot * Vector3.back, rot * Vector3.forward, rot * Vector3.left, rot * Vector3.right };
            float cy = o.y + h * 0.5f;
            def.Centres = new[] { W((x0 + x1) / 2f, h / 2f, z0), W((x0 + x1) / 2f, h / 2f, z1), W(x0, h / 2f, (z0 + z1) / 2f), W(x1, h / 2f, (z0 + z1) / 2f) };
            // roof
            var roofCol = sh["roof_col"] != null ? C(sh["roof_col"]) : roofKind == "gable" ? new Color(0.36f, 0.22f, 0.18f) : new Color(0.50f, 0.50f, 0.52f);
            // world districts 2: the roofs are clay-tile scans now (shell_roof, Venue Reskin), which multiply the colour by the
            // tiles' own light-dark; the old flat colour read salmon-pink there, so gables take a deeper, browner red
            if (roofKind == "gable") roofCol = new Color(roofCol.r * 0.7f, roofCol.g * 0.75f, roofCol.b * 0.8f);
            if (roofKind == "flat")
            {
                var r = def.Roof;
                r.FlatQuad(W(x0, h, z0), W(x0, h, z1), W(x1, h, z1), W(x1, h, z0), default, roofCol);           // clockwise from above
                // parapet: outer faces up to h + 0.9, the top, the inner faces down to the roof
                float ph = h + 0.9f, t = 0.35f;
                var ring = new[] { new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0) };
                for (int i = 0; i < 4; i++)
                {
                    var a = ring[i]; var c = ring[(i + 1) % 4];
                    var inward = new Vector2((x0 + x1) / 2f, (z0 + z1) / 2f);
                    var na = a + (inward - a).normalized * t * 1.414f; var nc = c + (inward - c).normalized * t * 1.414f;
                    // the ring runs clockwise seen from above: outer faces go c -> a, inner faces a -> c (clockwise from the front)
                    r.FlatQuad(W(c.x, h, c.y), W(c.x, ph, c.y), W(a.x, ph, a.y), W(a.x, h, a.y), default, trim);        // outer
                    r.FlatQuad(W(a.x, ph, a.y), W(c.x, ph, c.y), W(nc.x, ph, nc.y), W(na.x, ph, na.y), default, trim);  // top
                    r.FlatQuad(W(na.x, h, na.y), W(na.x, ph, na.y), W(nc.x, ph, nc.y), W(nc.x, h, nc.y), default, trim);  // inner
                }
            }
            else if (roofKind == "gable")
            {
                var r = def.Roof;
                float ov = 0.6f;
                if (gableAlongX)
                {
                    float zm = (z0 + z1) / 2f;
                    r.FlatQuad(W(x0 - ov, h - 0.3f, z0 - ov), W(x0 - ov, h + gableRise, zm), W(x1 + ov, h + gableRise, zm), W(x1 + ov, h - 0.3f, z0 - ov), default, roofCol);
                    r.FlatQuad(W(x1 + ov, h - 0.3f, z1 + ov), W(x1 + ov, h + gableRise, zm), W(x0 - ov, h + gableRise, zm), W(x0 - ov, h - 0.3f, z1 + ov), default, roofCol);
                }
                else
                {
                    float xm = (x0 + x1) / 2f;
                    r.FlatQuad(W(x0 - ov, h - 0.3f, z1 + ov), W(xm, h + gableRise, z1 + ov), W(xm, h + gableRise, z0 - ov), W(x0 - ov, h - 0.3f, z0 - ov), default, roofCol);
                    r.FlatQuad(W(x1 + ov, h - 0.3f, z0 - ov), W(xm, h + gableRise, z0 - ov), W(xm, h + gableRise, z1 + ov), W(x1 + ov, h - 0.3f, z1 + ov), default, roofCol);
                }
            }
            // the name sign over the door, 8 x 2 m (narrower walls get a smaller board)
            float sw = Mathf.Min(8f, (x1 - x0) - 2f), shh = sw * 0.25f;
            float sy = Mathf.Min(doorH + 0.5f, h - shh - 0.6f);
            var sgn = def.Sign;
            float sz0 = z0 - 0.12f;
            int s0 = sgn.Add(W(doorX - sw / 2f, sy, sz0), rot * Vector3.back, new Vector2(0f, 0f));
            int s1 = sgn.Add(W(doorX - sw / 2f, sy + shh, sz0), rot * Vector3.back, new Vector2(0f, 1f));
            int s2 = sgn.Add(W(doorX + sw / 2f, sy + shh, sz0), rot * Vector3.back, new Vector2(1f, 1f));
            int s3 = sgn.Add(W(doorX + sw / 2f, sy, sz0), rot * Vector3.back, new Vector2(1f, 0f));
            sgn.Tri(s0, s1, s2); sgn.Tri(s0, s2, s3);
            // materials: one facade material per venue (style and colours), the roof, the sign texture
            def.Facade = FacadeMaterial(id, StyleIds.TryGetValue((string)sh["style"] ?? "plaster", out var st) ? st : 0, trim, accent, (string)sh["style"]);
            def.RoofMat = mats.TryGetValue("shell_roof", out var rm) ? rm : mats[Mat.Concrete];
            def.SignMat = SignMaterial(id);
            return def;
        }

        /// <summary>A landmark's feature: the observatory dome, the phone on the shop roof, Builder HQ bunting, the summit cairn.</summary>
        private static void Extras(Def def, JObject lb, Dictionary<string, Material> mats)
        {
            string kind = (string)lb["extra"];
            if (string.IsNullOrEmpty(kind)) return;
            var b = new MeshBuilder { HasColor = true };
            var rot = Quaternion.Euler(0f, def.Yaw, 0f);
            var o = def.Origin;
            float h = def.Height;
            var accent = C(lb["accent"]); var trim = C(lb["trim"]);
            Vector3 W(float x, float y, float z) => o + rot * new Vector3(x, y, z);
            switch (kind)
            {
                case "dome":
                {
                    float r = Mathf.Min(def.Footprint.width, def.Footprint.height) * 0.42f;
                    const int seg = 16, rings = 8;
                    var white = new Color(0.96f, 0.96f, 0.95f); var slit = new Color(0.18f, 0.20f, 0.26f);
                    for (int j = 0; j < rings; j++)
                        for (int i = 0; i < seg; i++)
                        {
                            float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                            float t0 = j * Mathf.PI * 0.5f / rings, t1 = (j + 1) * Mathf.PI * 0.5f / rings;
                            Vector3 P(float a, float t) => W(Mathf.Cos(a) * Mathf.Cos(t) * r, h + 0.9f + Mathf.Sin(t) * r, Mathf.Sin(a) * Mathf.Cos(t) * r);
                            var col = (i == 3 || i == 4) && j < rings - 1 ? slit : white;    // the observing slit
                            b.FlatQuad(P(a0, t0), P(a0, t1), P(a1, t1), P(a1, t0), default, col);
                        }
                    break;
                }
                case "phone":
                {
                    // a giant phone propped on the roof, back to the street: black slab, three lens dots, a small shield
                    var c = W(0f, h + 3.4f, 1.0f);
                    b.Box(c, rot * Vector3.right, Vector3.up, rot * Vector3.forward, new Vector3(1.7f, 3.2f, 0.28f), new Color(0.06f, 0.06f, 0.07f));
                    for (int k = 0; k < 3; k++)
                        b.Box(W(-0.9f + k * 0.0f, h + 5.6f - k * 0.62f, 0.68f), rot * Vector3.right, Vector3.up, rot * Vector3.forward, new Vector3(0.24f, 0.24f, 0.06f), new Color(0.32f, 0.34f, 0.40f));
                    b.Box(W(0.5f, h + 2.6f, 0.68f), rot * Vector3.right, Vector3.up, rot * Vector3.forward, new Vector3(0.3f, 0.36f, 0.04f), accent);
                    b.Box(W(0f, h + 0.25f, 1.4f), rot * Vector3.right, Vector3.up, rot * Vector3.forward, new Vector3(1.9f, 0.25f, 0.6f), new Color(0.3f, 0.3f, 0.32f));
                    break;
                }
                case "bunting":
                {
                    // pennant strings from the roof corners to the plaza, alternating the two colours
                    var f = def.Footprint;
                    var corners = new[] { new Vector2(f.xMin, f.yMin), new Vector2(f.xMax, f.yMin), new Vector2(f.xMax, f.yMax), new Vector2(f.xMin, f.yMax) };
                    for (int c = 0; c < 4; c++)
                    {
                        var a = W(corners[c].x, h + 0.8f, corners[c].y);
                        var dir = new Vector3(corners[c].x, 0f, corners[c].y).normalized;
                        var e = W(corners[c].x + dir.x * 14f, 4.2f, corners[c].y + dir.z * 14f);
                        e.y = o.y + 4.2f;
                        for (int k = 0; k < 18; k++)
                        {
                            float t = (k + 0.5f) / 18f;
                            var p = Vector3.Lerp(a, e, t) - Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.4f;
                            var along = (e - a).normalized;
                            var col = k % 2 == 0 ? accent : C(lb["wall"]);
                            var p0 = p - along * 0.3f; var p1 = p + along * 0.3f; var p2 = p - Vector3.up * 0.7f;
                            int i0 = b.Add(p0, Vector3.Cross(along, Vector3.up), Vector2.zero, default, default, col);
                            int i1 = b.Add(p1, Vector3.Cross(along, Vector3.up), Vector2.zero, default, default, col);
                            int i2 = b.Add(p2, Vector3.Cross(along, Vector3.up), Vector2.zero, default, default, col);
                            b.Tri(i0, i1, i2); b.Tri(i0, i2, i1);
                        }
                    }
                    break;
                }
                case "cairn":
                {
                    // the MtnDAO summit: a stone cairn with three tall flag poles (red, white, the accent), a bench
                    var stone = new Color(0.52f, 0.50f, 0.48f);
                    float y = 0f;
                    for (int k = 0; k < 6; k++)
                    {
                        float s = 1.5f - k * 0.2f, hh = 0.45f;
                        b.Box(W(Mathf.Sin(k * 1.7f) * 0.15f, y + hh * 0.5f, Mathf.Cos(k * 2.3f) * 0.15f), Quaternion.Euler(0f, k * 23f, 0f) * (rot * Vector3.right), Vector3.up,
                              Quaternion.Euler(0f, k * 23f, 0f) * (rot * Vector3.forward), new Vector3(s * 0.5f, hh * 0.5f, s * 0.5f), stone * (0.9f + 0.05f * k));
                        y += hh;
                    }
                    var flags = new[] { accent, new Color(0.96f, 0.96f, 0.94f), new Color(0.20f, 0.30f, 0.62f) };
                    for (int k = 0; k < 3; k++)
                    {
                        var foot = W(-2.2f + k * 2.2f, 0f, 2.2f);
                        b.Box(foot + Vector3.up * 3.5f, rot * Vector3.right, Vector3.up, rot * Vector3.forward, new Vector3(0.05f, 3.5f, 0.05f), new Color(0.8f, 0.8f, 0.82f));
                        var top = foot + Vector3.up * 6.6f;
                        var fx = rot * Vector3.right;
                        int i0 = b.Add(top, rot * Vector3.back, Vector2.zero, default, default, flags[k]);
                        int i1 = b.Add(top + fx * 1.8f - Vector3.up * 0.2f, rot * Vector3.back, Vector2.zero, default, default, flags[k]);
                        int i2 = b.Add(top - Vector3.up * 1.1f, rot * Vector3.back, Vector2.zero, default, default, flags[k]);
                        int i3 = b.Add(top + fx * 1.7f - Vector3.up * 1.3f, rot * Vector3.back, Vector2.zero, default, default, flags[k]);
                        b.Tri(i0, i1, i2); b.Tri(i1, i3, i2); b.Tri(i0, i2, i1); b.Tri(i1, i2, i3);
                    }
                    break;
                }
                case "neon":
                    if (def.SignMat != null) def.SignMat.SetColor("_Emission", new Color(1.6f, 0.55f, 0.3f));
                    break;
            }
            if (!b.Empty)
            {
                def.Extra = b;
                def.ExtraMat = mats.TryGetValue(Mat.Lighthouse, out var lm) ? lm : mats[Mat.Concrete];   // white, vertex-coloured
            }
        }

        private static Material FacadeMaterial(string id, int style, Color trim, Color accent, string styleName)
        {
            var path = IslandV2Paths.Materials + "/facade_" + id + ".mat";
            var sh = Shader.Find("CT Life/World/Facade");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh) { name = "facade_" + id }; AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetFloat("_Style", style);
            m.SetColor("_Trim", trim);
            m.SetColor("_Accent", accent);
            m.SetColor("_Glass", style == 1 ? new Color(0.46f, 0.66f, 0.80f) : new Color(0.36f, 0.52f, 0.62f));
            m.SetColor("_Frame", new Color(0.22f, 0.23f, 0.27f));
            m.SetVector("_Floors", style == 1 ? new Vector4(4.6f, 3.6f, 2.8f, 0f) : style == 3 ? new Vector4(3.2f, 3.0f, 3.6f, 0f) : new Vector4(4.6f, 3.6f, 3.4f, 0f));
            // world districts 2 (2026-10-23): a CC0 scan under each style (art/pipeline/world_street_textures.py)
            var (scan, mid, tile, k) = style switch
            {
                1 => ("concrete", new Color(0.088f, 0.09f, 0.085f), 2.0f, 0.6f),
                2 => ("corrugated", new Color(0.083f, 0.081f, 0.069f), 2.0f, 0.9f),
                3 => ("siding", new Color(0.066f, 0.043f, 0.029f), 2.4f, 1.0f),
                4 => ("concrete", new Color(0.088f, 0.09f, 0.085f), 2.5f, 0.8f),
                _ => ("plaster", new Color(0.383f, 0.345f, 0.331f), 1.6f, 0.9f),
            };
            m.SetTexture("_Wall", AssetDatabase.LoadAssetAtPath<Texture2D>(IslandV2Paths.Textures + $"/Street/{scan}_albedo.png"));
            m.SetTexture("_WallNormal", AssetDatabase.LoadAssetAtPath<Texture2D>(IslandV2Paths.Textures + $"/Street/{scan}_normal.png"));
            m.SetColor("_WallMid", mid);
            m.SetVector("_Scan", new Vector4(tile, k, 0.8f, 0f));
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material SignMaterial(string id)
        {
            var path = IslandV2Paths.Materials + "/sign_" + id + ".mat";
            var sh = Shader.Find("CT Life/World/Toon");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh) { name = "sign_" + id }; AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IslandV2Paths.Textures + "/Signs/sign_" + id + ".png");
            m.SetTexture("_BaseMap", tex != null ? tex : Texture2D.whiteTexture);
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_Emission", new Color(0.35f, 0.35f, 0.35f));
            m.SetFloat("_Toon", 0.1f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>WriteCell: the shell's GameObjects (renderers per wall and roof for fading) and its VenueShell.</summary>
        public static void Write(Def d, Transform parent, List<Mesh> meshes, string cellId)
        {
            var go = new GameObject("Shell_" + d.VenueId) { layer = Layers.Building };
            go.transform.SetParent(parent, false);
            MeshRenderer Part(string name, MeshBuilder b, Material mat, bool shadows)
            {
                if (b == null || b.Empty) return null;
                var mesh = b.ToMesh($"{cellId}_{d.VenueId}_{name}");
                meshes.Add(mesh);
                var p = new GameObject(name) { layer = Layers.Building };
                p.transform.SetParent(go.transform, false);
                p.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = p.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                return mr;
            }
            var front = Part("front", d.Front, d.Facade, true);
            var back = Part("back", d.Back, d.Facade, true);
            var left = Part("left", d.Left, d.Facade, true);
            var right = Part("right", d.Right, d.Facade, true);
            var roof = Part("roof", d.Roof, d.RoofMat, true);
            var sign = Part("sign", d.Sign, d.SignMat, false);
            var extra = Part("feature", d.Extra, d.ExtraMat, true);
            if (sign != null && front != null) sign.transform.SetParent(front.transform, true);
            if (d.Front == null) return;   // features only (the summit cairn): no walls, no fader
            var vs = go.AddComponent<VenueShell>();
            vs.venueId = d.VenueId; vs.footprint = d.Footprint; vs.height = d.Height; vs.origin = d.Origin; vs.yaw = d.Yaw;
            vs.roof = roof;
            vs.walls = new Renderer[] { front, back, left, right, sign };
            vs.normals = new[] { d.Normals[0], d.Normals[1], d.Normals[2], d.Normals[3], d.Normals[0] };
            vs.centres = new[] { d.Centres[0], d.Centres[1], d.Centres[2], d.Centres[3], d.Centres[0] };
            // outside walls stop cars and people (thin boxes; the front leaves the door open)
            var rot = Quaternion.Euler(0f, d.Yaw, 0f);
            var f = d.Footprint;
            void Col(Vector2 a, Vector2 b)
            {
                var mid = (a + b) * 0.5f;
                var len = Vector2.Distance(a, b);
                if (len < 0.2f) return;
                var c = d.Origin + rot * new Vector3(mid.x, d.Height * 0.5f, mid.y);
                var dir = rot * new Vector3((b - a).x, 0f, (b - a).y).normalized;
                var cgo = new GameObject("wall_col") { layer = Layers.Building };
                cgo.transform.SetParent(go.transform, false);
                cgo.transform.SetPositionAndRotation(c, Quaternion.LookRotation(dir, Vector3.up));
                cgo.AddComponent<BoxCollider>().size = new Vector3(0.4f, d.Height + 0.8f, len);
            }
            float doorW = Mathf.Min(5.2f, f.width * 0.4f);
            if (d.HasDoor)
            {
                Col(new Vector2(f.xMin, f.yMin), new Vector2(-doorW / 2f, f.yMin));
                Col(new Vector2(doorW / 2f, f.yMin), new Vector2(f.xMax, f.yMin));
            }
            else Col(new Vector2(f.xMin, f.yMin), new Vector2(f.xMax, f.yMin));
            Col(new Vector2(f.xMax, f.yMax), new Vector2(f.xMin, f.yMax));
            Col(new Vector2(f.xMin, f.yMax), new Vector2(f.xMin, f.yMin));
            Col(new Vector2(f.xMax, f.yMin), new Vector2(f.xMax, f.yMax));
        }
    }
}
