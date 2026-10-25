using System.Collections.Generic;
using System.IO;
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
            /// <summary>The door gap in the front wall's collider (m); 0 = the shell's default (min(5.2, 0.4 width)).</summary>
            public float DoorW;
            /// <summary>A walk-in landmark (The Rug Room): its room streams in behind the door (IslandInteriors), so no fader.</summary>
            public bool WalkIn;
            /// <summary>Landmark extras (dome, phone, bunting, cairn) in their own renderer, and their materials.</summary>
            public MeshBuilder Extra; public Material ExtraMat;
            /// <summary>World fix-ups: real architecture (art/pipeline/world_venue_shells.py) in place of the procedural walls
            /// and roof, per side (front, back, left, right, roof), in the venue's local frame.</summary>
            public Dictionary<string, ArchPart> Arch;
            /// <summary>The far LOD of the architecture (no frames, mullions, bars, sills or trims), merged outside.</summary>
            public Dictionary<string, ArchPart> ArchLod1;
        }

        /// <summary>One side of a venue's architecture: flat-shaded triangles in submeshes by material slot.</summary>
        public sealed class ArchPart
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<(string mat, List<int> tris)> Sub = new List<(string, List<int>)>();
        }

        /// <summary>The venues with real architecture: their mesh data from art/processed/world_v3/v3_venues/&lt;id&gt;.json.</summary>
        private static JObject ArchJson(string id)
        {
            var path = Path.Combine(IslandV2Paths.Repo, "art", "processed", "world_v3", "v3_venues", id + ".json");
            return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null;
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
                    // R3: a walk-in landmark keeps a door the size of the streamed room's entrance (layout walkin = (w, h))
                    var walkin = lb["walkin"] as JArray;
                    if ((float)lb["h"] > 0f)
                    {
                        def = Shell(v, lb, mats, door: walkin != null);
                        if (walkin != null) { def.DoorW = (float)walkin[0] + 0.1f; def.WalkIn = true; }
                    }
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
            // world fix-ups (2026-10-23): real architecture replaces the procedural walls and roof where it exists; the
            // footprint, door, colliders and fader stay the shell's
            var arch = ArchJson(id);       // venues and (district fix-ups 2) the doorless landmark buildings
            if (arch != null)
            {
                Dictionary<string, ArchPart> Parts(JObject parts)
                {
                    var res = new Dictionary<string, ArchPart>();
                    foreach (var kv in parts)
                    {
                        var ap = new ArchPart();
                        var jv = (JArray)kv.Value["v"]; var jn = (JArray)kv.Value["n"]; var ju = (JArray)kv.Value["uv"];
                        for (int i = 0; i < jv.Count; i++)
                        {
                            ap.V.Add(new Vector3((float)jv[i][0], (float)jv[i][1], (float)jv[i][2]));
                            ap.N.Add(new Vector3((float)jn[i][0], (float)jn[i][1], (float)jn[i][2]));
                            ap.UV.Add(new Vector2((float)ju[i][0], (float)ju[i][1]));
                        }
                        foreach (var sm in (JObject)kv.Value["sub"])
                            ap.Sub.Add((sm.Key, ((JArray)sm.Value).Select(t => (int)t).ToList()));
                        res[kv.Key] = ap;
                    }
                    return res;
                }
                def.Arch = Parts((JObject)arch["parts"]);
                if (arch["lod1"] is JObject l1) def.ArchLod1 = Parts(l1);
                // the name sign where the architecture leaves room for it (the lodge's gable, the hut's roof edge)
                if (arch["sign"] is JObject so)
                {
                    float w = (float)so["w"], hh = w * 0.25f, y = (float)so["y"], z = (float)so["z"];
                    var sg = new MeshBuilder();
                    int t0 = sg.Add(W(doorX - w / 2f, y, z), rot * Vector3.back, new Vector2(0f, 0f));
                    int t1 = sg.Add(W(doorX - w / 2f, y + hh, z), rot * Vector3.back, new Vector2(0f, 1f));
                    int t2 = sg.Add(W(doorX + w / 2f, y + hh, z), rot * Vector3.back, new Vector2(1f, 1f));
                    int t3 = sg.Add(W(doorX + w / 2f, y, z), rot * Vector3.back, new Vector2(1f, 0f));
                    sg.Tri(t0, t1, t2); sg.Tri(t0, t2, t3);
                    def.Sign = sg;
                }
                // R3: neon tubes drawn with the sign's emissive material: each quad samples the sign texture's top rows (pink
                // tube) or bottom rows (gold tube), so a walk-in landmark's neon costs no extra draw
                if (arch["neon"] is JArray neon && neon.Count > 0)
                {
                    var sg = def.Sign ?? new MeshBuilder();
                    foreach (JObject nq in neon)
                    {
                        float v0 = (string)nq["c"] == "gold" ? 0.012f : 0.988f;
                        var q = (JArray)nq["q"];
                        var pts = Enumerable.Range(0, 4).Select(i => W((float)q[i][0], (float)q[i][1], (float)q[i][2])).ToArray();
                        var nrm = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]).normalized;
                        int a0 = sg.Add(pts[0], nrm, new Vector2(0.3f, v0)), a1 = sg.Add(pts[1], nrm, new Vector2(0.3f, v0));
                        int a2 = sg.Add(pts[2], nrm, new Vector2(0.7f, v0)), a3 = sg.Add(pts[3], nrm, new Vector2(0.7f, v0));
                        sg.Tri(a0, a1, a2); sg.Tri(a0, a2, a3); sg.Tri(a0, a2, a1); sg.Tri(a0, a3, a2);
                    }
                    def.Sign = sg;
                }
            }
            return def;
        }

        /// <summary>World fix-ups: the venue architecture's materials on CT Life/World/Kit PBR - CC0 Poly Haven scans
        /// (art/pipeline/world_venue_textures.py, tiled in metres) or flat PBR colours (glass, steel, aluminium, accent).</summary>
        private static readonly Dictionary<string, Material> ArchMats = new Dictionary<string, Material>();
        private static Material ArchMaterial(string slotName) => ArchMaterial(slotName, true);

        /// <summary>fade: the per-side materials carry VenueShell's dither (_VENUE_FADE, a clip()); the merged outside
        /// renderer uses a solid twin without it, so the mobile GPU keeps its early depth test there.</summary>
        private static Material ArchMaterial(string slotName, bool fade)
        {
            string cacheKey = slotName + (fade ? "" : "#solid");
            if (ArchMats.TryGetValue(cacheKey, out var cached) && cached != null) return cached;
            // district fix-ups 2: 'base:rrggbb' tints a base material (multiplies a scan, replaces a flat colour)
            string slot = slotName, hex = null;
            int colon = slotName.IndexOf(':');
            if (colon > 0) { slot = slotName.Substring(0, colon); hex = slotName.Substring(colon + 1); }
            Color? tintHex = null;
            if (hex != null && ColorUtility.TryParseHtmlString("#" + hex, out var th)) tintHex = th;
            var path = IslandV2Paths.Materials + "/venue_" + slot + (hex != null ? "_" + hex : "") + (fade ? "" : "_solid") + ".mat";
            var sh = Shader.Find("CT Life/World/Kit PBR");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh) { name = "venue_" + slot }; AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            void Kw(string k, bool on) { if (on) m.EnableKeyword(k); else m.DisableKeyword(k); }
            // tile metres for the scans
            float tile = slot switch { "concrete" => 2.6f, "timber_dark" => 2.2f, "timber_light" => 2.0f, "wood_post" => 1.4f, "slate" => 2.4f,
                                       "thatch" => 2.4f, "stone" => 2.2f, "paving" => 1.8f, "plaster" => 1.6f, "corrugated" => 2.0f, "siding" => 2.4f, _ => 0f };
            if (slot == "plaster" || slot == "corrugated" || slot == "siding")
            {
                // the street scans (world_street_textures.py, Poly Haven CC0): painted plaster, corrugated iron, plank siding
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(IslandV2Paths.Textures + $"/Street/{slot}_albedo.png"));
                m.SetTextureScale("_BaseMap", new Vector2(1f / tile, 1f / tile));
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(IslandV2Paths.Textures + $"/Street/{slot}_normal.png"));
                m.SetTexture("_MaskMap", null);
                Kw("_NORMALMAP", true); m.SetFloat("_UseNormal", 1f); Kw("_MASKMAP", false); m.SetFloat("_UseMask", 0f);
                m.SetFloat("_Metallic", slot == "corrugated" ? 0.35f : 0f); m.SetFloat("_Smoothness", slot == "corrugated" ? 0.4f : 0.18f);
                m.SetFloat("_BumpScale", 1f);
                // the scans' own brightness: plaster ~0.65, corrugated ~0.32, siding ~0.25 (sRGB); tints multiply on top
                m.SetColor("_BaseColor", slot == "plaster" ? new Color(1.22f, 1.22f, 1.24f) : slot == "corrugated" ? new Color(1.7f, 1.7f, 1.7f) : new Color(1.4f, 1.4f, 1.4f));
            }
            else if (slot == "paving")
            {
                // the convention centre's plaza: the sidewalk stone (Poly Haven concrete_pavement_02, world_street_textures.py)
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(IslandV2Paths.Textures + "/Street/pavers_albedo.png"));
                m.SetTextureScale("_BaseMap", new Vector2(1f / tile, 1f / tile));
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(IslandV2Paths.Textures + "/Street/pavers_normal.png"));
                m.SetTexture("_MaskMap", null);
                Kw("_NORMALMAP", true); m.SetFloat("_UseNormal", 1f); Kw("_MASKMAP", false); m.SetFloat("_UseMask", 0f);
                m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.25f); m.SetFloat("_BumpScale", 1f);
                m.SetColor("_BaseColor", new Color(1.05f, 1.03f, 1.0f));
            }
            else if (tile > 0f)
            {
                string tp = IslandV2Paths.Textures + "/Venues/" + slot;
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tp + "_albedo.png"));
                m.SetTextureScale("_BaseMap", new Vector2(1f / tile, 1f / tile));
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tp + "_normal.png"));
                m.SetTexture("_MaskMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tp + "_mask.png"));
                Kw("_NORMALMAP", true); m.SetFloat("_UseNormal", 1f);
                Kw("_MASKMAP", true); m.SetFloat("_UseMask", 1f);
                m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f); m.SetFloat("_BumpScale", 1f);
                m.SetColor("_BaseColor", slot switch
                {
                    "timber_dark" => new Color(1.0f, 0.93f, 0.86f),        // weathered boards
                    "slate" => new Color(0.86f, 0.88f, 0.92f),
                    "wood_post" => new Color(0.95f, 0.85f, 0.75f),
                    "timber_light" => new Color(1.22f, 1.16f, 1.08f),      // sun-bleached pine
                    "thatch" => new Color(1.35f, 1.28f, 1.12f),            // dry golden reed
                    "concrete" => new Color(1.12f, 1.15f, 1.2f),           // pale cool architectural concrete
                    _ => Color.white,
                });
            }
            else
            {
                m.SetTexture("_BaseMap", null); m.SetTexture("_BumpMap", null); m.SetTexture("_MaskMap", null);
                Kw("_NORMALMAP", false); m.SetFloat("_UseNormal", 0f);
                Kw("_MASKMAP", false); m.SetFloat("_UseMask", 0f);
                var (col, metal, smooth) = slot switch
                {
                    // reflective architectural glass: a tinted, partly metallic coat so the curtain walls carry the sky
                    "glass" => (new Color(0.32f, 0.40f, 0.46f), 0.7f, 0.95f),
                    "steel" => (new Color(0.11f, 0.115f, 0.12f), 0.55f, 0.55f),
                    "alu" => (new Color(0.80f, 0.81f, 0.82f), 0.35f, 0.62f),
                    "accent" => (new Color(0.25f, 0.95f, 0.6f), 0.0f, 0.7f),
                    _ => (new Color(0.86f, 0.82f, 0.76f), 0.0f, 0.2f),     // inner: soffits and linings
                };
                m.SetColor("_BaseColor", col); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            }
            Kw("_GLASS", false); m.SetFloat("_Glass", 0f);
            Kw("_VENUE_FADE", fade);      // VenueShell's dither (only the per-side materials carry the clip)
            if (tintHex is Color tc)
            {
                if (tile > 0f) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * tc);
                else m.SetColor("_BaseColor", tc);
            }
            m.SetFloat("_OcclusionStrength", 1f);
            m.SetFloat("_Reflection", slot == "glass" ? 1.5f : 1f);
            m.SetFloat("_TintStrength", 0f);
            m.SetVector("_GroundAO", new Vector4(2.6f, 0.4f, 0f, 0f));
            m.SetFloat("_Cutoff", 0f); Kw("_ALPHATEST_ON", false);
            m.SetFloat("_Cull", 2f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            ArchMats[cacheKey] = m;
            return m;
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
                case "neon_rug":
                    // The Rug Room's neon board and tubes (white emission keeps the sign art's own pink and gold)
                    if (def.SignMat != null) def.SignMat.SetColor("_Emission", new Color(1.5f, 1.5f, 1.5f));
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

        /// <summary>RecalculateTangents leaves NaN on triangles with no UV area (thin trims, caps); a NaN in the shading reaches
        /// the bloom and blacks out the whole building. Replace them with a tangent along the edge.</summary>
        private static void SanitizeTangents(Mesh m, string what)
        {
            var t = m.tangents;
            int bad = 0;
            for (int i = 0; i < t.Length; i++)
                if (float.IsNaN(t[i].x) || float.IsNaN(t[i].y) || float.IsNaN(t[i].z) || float.IsNaN(t[i].w) || new Vector3(t[i].x, t[i].y, t[i].z).sqrMagnitude < 1e-8f)
                {
                    var n = m.normals[i];
                    var tv = Vector3.Cross(Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right, n).normalized;
                    t[i] = new Vector4(tv.x, tv.y, tv.z, 1f);
                    bad++;
                }
            if (bad > 0) { m.tangents = t; Debug.Log($"CTLIFE [islandv2] tangents fixed {what}: {bad}"); }
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
            MeshRenderer ArchRenderer(string name)
            {
                if (d.Arch == null || !d.Arch.TryGetValue(name, out var ap) || ap.V.Count == 0) return null;
                var mesh = new Mesh { name = $"{cellId}_{d.VenueId}_{name}_arch" };
                mesh.SetVertices(ap.V); mesh.SetNormals(ap.N); mesh.SetUVs(0, ap.UV);
                mesh.subMeshCount = ap.Sub.Count;
                for (int i = 0; i < ap.Sub.Count; i++) mesh.SetTriangles(ap.Sub[i].tris, i, false);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                SanitizeTangents(mesh, d.VenueId + "/" + name);
                meshes.Add(mesh);
                var p = new GameObject(name) { layer = Layers.Building };
                p.transform.SetParent(go.transform, false);
                p.transform.SetPositionAndRotation(d.Origin, Quaternion.Euler(0f, d.Yaw, 0f));
                p.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = p.AddComponent<MeshRenderer>();
                mr.sharedMaterials = ap.Sub.Select(sm => ArchMaterial(sm.mat)).ToArray();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                return mr;
            }
            var front = d.Arch != null ? ArchRenderer("front") : Part("front", d.Front, d.Facade, true);
            var back = d.Arch != null ? ArchRenderer("back") : Part("back", d.Back, d.Facade, true);
            var left = d.Arch != null ? ArchRenderer("left") : Part("left", d.Left, d.Facade, true);
            var right = d.Arch != null ? ArchRenderer("right") : Part("right", d.Right, d.Facade, true);
            var roof = d.Arch != null ? ArchRenderer("roof") : Part("roof", d.Roof, d.RoofMat, true);
            var sign = Part("sign", d.Sign, d.SignMat, false);
            var extra = Part("feature", d.Extra, d.ExtraMat, true);
            if (sign != null && front != null) sign.transform.SetParent(front.transform, true);
            if (d.Front == null) return;   // features only (the summit cairn): no walls, no fader
            var vs = go.AddComponent<VenueShell>();
            vs.venueId = d.VenueId; vs.footprint = d.Footprint; vs.height = d.Height; vs.origin = d.Origin; vs.yaw = d.Yaw;
            vs.sign = sign;
            if (d.Arch != null)
            {
                // world fix-ups 2: the sides and roof merged by material into one renderer for the outside view (one draw per
                // material instead of one per material per side), on solid materials; a far LOD without the line-like
                // members past ~60 m; VenueShell switches to the per-side renderers only while something fades
                MeshRenderer Merged(Dictionary<string, ArchPart> parts, string name, Transform parent)
                {
                    var byMat = new Dictionary<string, List<int>>();
                    var mv = new List<Vector3>(); var mn = new List<Vector3>(); var muv = new List<Vector2>();
                    foreach (var kv in parts)
                    {
                        int b0 = mv.Count;
                        mv.AddRange(kv.Value.V); mn.AddRange(kv.Value.N); muv.AddRange(kv.Value.UV);
                        foreach (var (mat, tris) in kv.Value.Sub)
                        {
                            if (!byMat.TryGetValue(mat, out var l)) byMat[mat] = l = new List<int>();
                            foreach (var t in tris) l.Add(t + b0);
                        }
                    }
                    var wm = new Mesh { name = $"{cellId}_{d.VenueId}_{name}", indexFormat = mv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                    wm.SetVertices(mv); wm.SetNormals(mn); wm.SetUVs(0, muv);
                    var keys = byMat.Keys.ToList();
                    wm.subMeshCount = keys.Count;
                    for (int i = 0; i < keys.Count; i++) wm.SetTriangles(byMat[keys[i]], i, false);
                    wm.RecalculateBounds(); wm.RecalculateTangents();
                    SanitizeTangents(wm, d.VenueId + "/" + name);
                    meshes.Add(wm);
                    var wg = new GameObject(name) { layer = Layers.Building };
                    wg.transform.SetParent(parent, false);
                    wg.AddComponent<MeshFilter>().sharedMesh = wm;
                    var wr = wg.AddComponent<MeshRenderer>();
                    wr.sharedMaterials = keys.Select(k => ArchMaterial(k, false)).ToArray();
                    wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    wr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    wr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    return wr;
                }
                var lodGo = new GameObject("whole") { layer = Layers.Building };
                lodGo.transform.SetParent(go.transform, false);
                lodGo.transform.SetPositionAndRotation(d.Origin, Quaternion.Euler(0f, d.Yaw, 0f));
                var w0 = Merged(d.Arch, "lod0", lodGo.transform);
                vs.whole = w0;
                if (d.ArchLod1 != null && d.ArchLod1.Count > 0)
                {
                    var w1 = Merged(d.ArchLod1, "lod1", lodGo.transform);
                    w1.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var lg = lodGo.AddComponent<LODGroup>();
                    // ~60-70 m for a 26-35 m building: past it the frames and mullions are sub-pixel
                    lg.SetLODs(new[] { new LOD(0.4f, new Renderer[] { w0 }), new LOD(0f, new Renderer[] { w1 }) });
                    lg.fadeMode = LODFadeMode.None;
                    lg.RecalculateBounds();
                }
                foreach (var r in new Renderer[] { front, back, left, right, roof }) if (r != null) r.enabled = false;
            }
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
            float doorW = d.DoorW > 0f ? d.DoorW : Mathf.Min(5.2f, f.width * 0.4f);
            if (d.HasDoor)
            {
                Col(new Vector2(f.xMin, f.yMin), new Vector2(-doorW / 2f, f.yMin));
                Col(new Vector2(doorW / 2f, f.yMin), new Vector2(f.xMax, f.yMin));
            }
            else Col(new Vector2(f.xMin, f.yMin), new Vector2(f.xMax, f.yMin));
            Col(new Vector2(f.xMax, f.yMax), new Vector2(f.xMin, f.yMax));
            Col(new Vector2(f.xMin, f.yMax), new Vector2(f.xMin, f.yMin));
            Col(new Vector2(f.xMax, f.yMin), new Vector2(f.xMax, f.yMax));
            if (d.WalkIn)
            {
                // R3 walk-in landmark (The Rug Room): the streamed room is the inside, so no fader (the merged outside renderer
                // stays on); the marquee's two columns stop bodies too
                Object.DestroyImmediate(vs);
                foreach (var x in new[] { -3.35f, 3.35f })
                {
                    var cgo = new GameObject("column_col") { layer = Layers.Building };
                    cgo.transform.SetParent(go.transform, false);
                    cgo.transform.SetPositionAndRotation(d.Origin + rot * new Vector3(x, 1.7f, f.yMin - 3.4f + 0.31f), rot);
                    cgo.AddComponent<BoxCollider>().size = new Vector3(0.3f, 3.4f, 0.3f);
                }
            }
        }
    }
}
