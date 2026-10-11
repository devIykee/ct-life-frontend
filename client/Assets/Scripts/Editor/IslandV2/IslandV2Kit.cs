using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// The island kit (World lane): copies the processed world kit (art/processed/world, made by the env pipeline with
    /// art/pipeline/world_config.py) into Assets/World/Island/Kit, sets lean import settings (no materials, no
    /// animation, no tangents), makes one CT Life/World/Kit material per texture (palette facades tintable, foliage
    /// with wind and cut-out) and writes the IslandKit asset with each model's LODs, cull distances and layer. Models
    /// already in the project from the venue kit (art/processed/environment -> Assets/Art) are referenced in place.
    /// Only models the dressing uses are included, so unused kit models never reach the APK.
    /// </summary>
    public static class IslandV2Kit
    {
        public const string KitDir = "Assets/World/Island/Kit";
        public const string KitAsset = KitDir + "/IslandKit.asset";

        private static string WorldRoot => Path.Combine(IslandV2Paths.Repo, "art", "processed", "world");
        private static string EnvManifest => Path.Combine(IslandV2Paths.Repo, "art", "processed", "environment", "manifest.json");

        /// <summary>Distances (m) where LOD0, LOD1 and LOD2 stop drawing, shadow range, layer, by kind.</summary>
        private static (float[] ends, float shadow, int layer, string kind) Rules(string id, JObject item)
        {
            var tags = (item["tags"] as JArray)?.Select(t => (string)t).ToHashSet() ?? new HashSet<string>();
            if (id.StartsWith("hero_")) return (new[] { 70f, 240f, 900f }, 80f, Layers.Prop, "hero");
            if (id.StartsWith("kw_ship")) return (new[] { 260f, 900f, 3600f }, 0f, Layers.Prop, "ship");
            if (id.StartsWith("kw_")) return (new[] { 55f, 170f, 800f }, 45f, Layers.Prop, "boat");
            if (id.StartsWith("kc_tower")) return (new[] { 120f, 420f, 2400f }, 160f, Layers.Building, "building");
            // Quaternius town buildings: 3.5k-tri LOD0 (W2 perf note), so LOD1 from 45 m and shadows to 90 m
            if (id.StartsWith("qtb_")) return (new[] { 45f, 150f, 1100f }, 90f, Layers.Building, "building");
            if (id.StartsWith("pp_")) return (new[] { 35f, 90f, 200f }, 35f, Layers.Prop, "prop");
            if (id.StartsWith("kc_detail")) return (new[] { 40f, 90f, 160f }, 30f, Layers.Prop, "prop");
            if (tags.Contains("building") || id.StartsWith("shop_") || id.StartsWith("tower_glass")) return (new[] { 80f, 260f, 1300f }, 130f, Layers.Building, "building");
            if (id.StartsWith("w_tree") || id.StartsWith("w_pine")) return (new[] { 45f, 110f, 900f }, 70f, Layers.Vegetation, "tree");
            if (id.StartsWith("kf_tree") || id.StartsWith("palm") || id.StartsWith("tree_")) return (new[] { 55f, 170f, 900f }, 70f, Layers.Vegetation, "tree");
            // eye-level ground detail (grass clumps, clover, ferns, flowers, pebbles): only near the camera
            if (tags.Contains("ground_detail")) return (new[] { 14f, 26f, 38f }, 0f, Layers.Vegetation, tags.Contains("pebble") ? "prop" : "grass");
            if (id.StartsWith("kf_bush")) return (new[] { 30f, 80f, 200f }, 25f, Layers.Vegetation, "bush");
            if (id.StartsWith("kf_rock")) return (new[] { 45f, 140f, 420f }, 40f, Layers.Prop, "rock");
            if (id.StartsWith("kf_grass")) return (new[] { 25f, 45f, 60f }, 0f, Layers.Vegetation, "grass");
            if (id.StartsWith("kn_crop")) return (new[] { 40f, 110f, 260f }, 0f, Layers.Vegetation, "crop");
            if (id.StartsWith("car_")) return (new[] { 35f, 110f, 320f }, 45f, Layers.Prop, "vehicle");
            if (id.StartsWith("kr_electricity")) return (new[] { 70f, 220f, 600f }, 40f, Layers.Prop, "pole");
            if (id.StartsWith("kr_light") || id.StartsWith("street_lamp") || id.StartsWith("kr_traffic")) return (new[] { 45f, 140f, 380f }, 35f, Layers.Prop, "lamp");
            if (id.StartsWith("ki_") || id.StartsWith("billboard")) return (new[] { 70f, 220f, 800f }, 60f, Layers.Prop, "large_prop");
            return (new[] { 25f, 60f, 130f }, 15f, Layers.Prop, "prop");
        }

        public static IslandKit Build(HashSet<string> used, JObject receipt)
        {
            Directory.CreateDirectory(KitDir);
            Directory.CreateDirectory(KitDir + "/Models");
            Directory.CreateDirectory(KitDir + "/Textures");
            Directory.CreateDirectory(KitDir + "/Materials");
            var world = File.Exists(Path.Combine(WorldRoot, "manifest.json")) ? JObject.Parse(File.ReadAllText(Path.Combine(WorldRoot, "manifest.json"))) : new JObject();
            var env = File.Exists(EnvManifest) ? JObject.Parse(File.ReadAllText(EnvManifest)) : new JObject();
            // 1. copy the world models and textures the dressing uses
            var paths = new Dictionary<string, string>();          // id -> asset path of the FBX
            var texFiles = new HashSet<string>();
            int copied = 0;
            foreach (var id in used.OrderBy(x => x))
            {
                if (world[id] is JObject wi)
                {
                    var rel = (string)wi["fbx"];                         // e.g. world/buildings/kc_building_a.fbx
                    var src = Path.Combine(IslandV2Paths.Repo, "art", "processed", rel);
                    var sub = Path.GetFileName(Path.GetDirectoryName(rel));
                    var dst = $"{KitDir}/Models/{sub}/{id}.fbx";
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (CopyIfChanged(src, dst)) copied++;
                    paths[id] = dst;
                    foreach (var t in (JArray)wi["textures"]) texFiles.Add((string)t);
                }
                else if (env[id] is JObject ei)
                {
                    var p = "Assets/Art/" + (string)ei["fbx"];
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(p) != null) paths[id] = p;
                }
            }
            CopyIfChanged(Path.Combine(WorldRoot, "_shared", "world_palette.png"), KitDir + "/Textures/world_palette.png");
            foreach (var t in texFiles) CopyIfChanged(Path.Combine(IslandV2Paths.Repo, "art", "processed", t), KitDir + "/Textures/" + Path.GetFileName(t));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var kv in paths.Where(kv => kv.Value.StartsWith(KitDir))) ModelImport(kv.Value);
            foreach (var f in Directory.GetFiles(KitDir + "/Textures", "*.png")) TextureImport(f.Replace('\\', '/'));
            // 2. materials and prototypes
            var kit = AssetDatabase.LoadAssetAtPath<IslandKit>(KitAsset);
            if (kit == null) { kit = ScriptableObject.CreateInstance<IslandKit>(); AssetDatabase.CreateAsset(kit, KitAsset); }
            var protos = new List<IslandKit.Proto>();
            var missing = new List<string>();
            foreach (var id in used.OrderBy(x => x))
            {
                if (!paths.TryGetValue(id, out var fbx)) { missing.Add(id); continue; }
                var item = (world[id] ?? env[id]) as JObject;
                var meshes = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>()
                    .OrderBy(m => LodIndex(m.name)).ToArray();
                if (meshes.Length == 0) { missing.Add(id); continue; }
                var lods = meshes.Where(m => LodIndex(m.name) < 9).ToArray();
                if (lods.Length == 0) lods = new[] { meshes[0] };
                var (ends, shadow, layer, kind) = Rules(id, item);
                var mats = ((JArray)item["materials"]).Select(m => (string)m).ToArray();
                var proto = new IslandKit.Proto
                {
                    id = id, lods = lods, lodEnd = ends.Take(Math.Max(1, lods.Length)).ToArray(), shadowRange = shadow, layer = layer, kind = kind,
                    bounds = lods[0].bounds,
                };
                if (proto.lodEnd.Length < lods.Length) proto.lodEnd = ends;
                if (lods.Length < ends.Length) proto.lodEnd[proto.lodEnd.Length - 1] = ends[ends.Length - 1];   // last LOD lives to the cull distance
                proto.materials = mats.Select(m => Material(m, kind, id)).ToArray();
                proto.material = proto.materials.FirstOrDefault();
                protos.Add(proto);
            }
            // W4: trees get a far billboard LOD (two crossed quads with a baked front/side view), so a 13k-tree forest
            // with 3-6k-triangle trees stays cheap beyond ~200 m
            int imp = 0;
            foreach (var proto in protos.Where(p => p.kind == "tree"))
            {
                var (im, imat) = BakeImpostor(proto);
                if (im == null) continue;
                proto.lods = proto.lods.Concat(new[] { im }).ToArray();
                float cull = proto.lodEnd[proto.lodEnd.Length - 1];
                var ends = proto.lodEnd.ToList();
                // detailed LODs to ~200 m, then the billboard to the old cull distance
                for (int i = 0; i < ends.Count; i++) ends[i] = Mathf.Min(ends[i], i == ends.Count - 1 ? 200f : ends[i]);
                ends.Add(cull);
                proto.lodEnd = ends.ToArray();
                proto.lodMaterials = new Material[proto.lods.Length];
                proto.lodMaterials[proto.lods.Length - 1] = imat;
                imp++;
            }
            receipt["kitImpostors"] = imp;
            kit.protos = protos.ToArray();
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssetIfDirty(kit);
            receipt["kitModels"] = protos.Count;
            receipt["kitCopied"] = copied;
            if (missing.Count > 0) { receipt["kitMissing"] = new JArray(missing); Debug.LogWarning("CTLIFE [islandv2] kit models missing: " + string.Join(", ", missing)); }
            return kit;
        }

        private static int LodIndex(string name)
        {
            int k = name.LastIndexOf("_LOD", StringComparison.Ordinal);
            return k >= 0 && int.TryParse(name.Substring(k + 4), out var i) ? i : 0;
        }

        private static bool CopyIfChanged(string src, string dst)
        {
            if (!File.Exists(src)) { Debug.LogWarning("CTLIFE [islandv2] kit source missing " + src); return false; }
            if (File.Exists(dst) && new FileInfo(dst).Length == new FileInfo(src).Length && File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(src))) return false;
            File.Copy(src, dst, true);
            return true;
        }

        private static void ModelImport(string path)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) return;
            bool dirty = false;
            void Set<T>(T cur, T want, Action<T> set) { if (!EqualityComparer<T>.Default.Equals(cur, want)) { set(want); dirty = true; } }
            Set(mi.materialImportMode, ModelImporterMaterialImportMode.None, v => mi.materialImportMode = v);
            Set(mi.importAnimation, false, v => mi.importAnimation = v);
            Set(mi.animationType, ModelImporterAnimationType.None, v => mi.animationType = v);
            Set(mi.importBlendShapes, false, v => mi.importBlendShapes = v);
            Set(mi.importCameras, false, v => mi.importCameras = v);
            Set(mi.importLights, false, v => mi.importLights = v);
            Set(mi.isReadable, false, v => mi.isReadable = v);
            Set(mi.importTangents, ModelImporterTangents.None, v => mi.importTangents = v);
            Set(mi.meshCompression, ModelImporterMeshCompression.Low, v => mi.meshCompression = v);
            Set(mi.addCollider, false, v => mi.addCollider = v);
            if (dirty) mi.SaveAndReimport();
        }

        private static void TextureImport(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            bool palette = path.Contains("palette") || path.Contains("colormap") || path.Contains("citybits");
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = !palette;
            ti.filterMode = palette ? FilterMode.Point : FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            var a = ti.GetPlatformTextureSettings("Android");
            // Tripo statue textures: painted, noisy detail where ASTC 6x6 is indistinguishable at statue distance and 56 % smaller
            bool tripo = Path.GetFileName(path).StartsWith("tripo__", StringComparison.Ordinal);
            a.overridden = true; a.maxTextureSize = 1024; a.format = tripo ? TextureImporterFormat.ASTC_6x6 : TextureImporterFormat.ASTC_4x4;
            ti.SetPlatformTextureSettings(a);
            ti.SaveAndReimport();
        }

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        /// <summary>The kit material for an FBX material name and a kind (billboard frames, other one-off props).</summary>
        public static Material KitMaterial(string fbxMaterial, string kind, string id) => Material(fbxMaterial, kind, id);

        /// <summary>One kit material per (texture, role): palette (tintable facades), foliage (wind, cut-out), plain.</summary>
        private static Material Material(string fbxMaterial, string kind, string id)
        {
            string tex = fbxMaterial.StartsWith("M_") ? fbxMaterial.Substring(2) : fbxMaterial;
            bool foliage = kind == "tree" || kind == "bush" || kind == "grass" || kind == "crop";
            bool leaves = tex.IndexOf("leaves", StringComparison.OrdinalIgnoreCase) >= 0 || tex.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0;
            string role = foliage ? (kind == "tree" ? (leaves ? "leaves" : "tree") : kind) : kind == "hero" ? "hero" : "plain";
            string key = tex + "__" + role;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var path = $"{KitDir}/Materials/{key}.mat";
            var sh = Shader.Find("CT Life/World/Kit") ?? throw new InvalidOperationException("CT Life/World/Kit shader missing");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(sh) { name = key }; AssetDatabase.CreateAsset(mat, path); }
            mat.shader = sh;
            mat.SetTexture("_BaseMap", FindTexture(tex));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Toon", 0.4f);
            bool tint = tex == "world_palette";
            if (tint) mat.EnableKeyword("_TINTPALETTE"); else mat.DisableKeyword("_TINTPALETTE");
            mat.SetFloat("_TintPalette", tint ? 1f : 0f);
            bool tintAll = foliage && (role == "tree" || role == "bush" || role == "leaves") || kind == "hero";
            if (tintAll) mat.EnableKeyword("_TINTALL"); else mat.DisableKeyword("_TINTALL");
            mat.SetFloat("_TintAll", tintAll ? 1f : 0f);
            var wind = role switch
            {
                "tree" => new Vector4(0.10f, 1.1f, 9f, 0), "leaves" => new Vector4(0.22f, 1.3f, 8f, 0), "bush" => new Vector4(0.05f, 1.6f, 1.4f, 0),
                "grass" => new Vector4(0.06f, 2.2f, 0.6f, 0), "crop" => new Vector4(0.08f, 1.8f, 1.6f, 0), _ => Vector4.zero,
            };
            mat.SetVector("_Wind", wind);
            mat.SetVector("_GroundAO", kind == "building" ? new Vector4(2.6f, 0.5f, 0, 0) : new Vector4(1.0f, 0.36f, 0, 0));   // contact darkening at the base
            mat.SetFloat("_Cutoff", leaves || role == "grass" ? 0.45f : 0f);
            mat.SetFloat("_Cull", leaves || role == "grass" || role == "crop" ? 0f : 2f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            Cache[key] = mat;
            return mat;
        }

        /// <summary>
        /// Renders a tree's LOD0 (albedo only: unlit copies of its materials, alpha cut-out) from the front and the side
        /// into one 512 x 512 atlas (Kit/Textures/imp_&lt;id&gt;.png, colours dilated into the clear texels so mips keep no
        /// dark fringe) and returns two crossed quads (normals up, so the kit shader lights them like the canopy top)
        /// and a cut-out, double-sided, tintable kit material for them.
        /// </summary>
        private static (Mesh, Material) BakeImpostor(IslandKit.Proto p)
        {
            var mesh0 = p.lods[0];
            if (mesh0 == null) return (null, null);
            var b = mesh0.bounds;
            float h = b.max.y, wv = Mathf.Max(b.size.x, b.size.z) * 1.04f;
            float size = Mathf.Max(h * 1.02f / 2f, wv);           // ortho half-height; the view is 0.5 wide so its width is 'size'
            const int RW = 256, RH = 512, L = 31;
            var go = new GameObject("impostor_bake") { layer = L, hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<MeshFilter>().sharedMesh = mesh0;
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var mats = new Material[mesh0.subMeshCount];
            for (int i = 0; i < mats.Length; i++)
            {
                var src = i < p.materials.Length && p.materials[i] != null ? p.materials[i] : p.material;
                var m = new Material(unlit);
                m.SetTexture("_BaseMap", src != null ? src.GetTexture("_BaseMap") : Texture2D.whiteTexture);
                m.SetColor("_BaseColor", Color.white);
                bool clip = src != null && src.GetFloat("_Cutoff") > 0f;
                m.SetFloat("_AlphaClip", clip ? 1f : 0f); m.SetFloat("_Cutoff", 0.45f);
                if (clip) m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f);
                mats[i] = m;
            }
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            var camGo = new GameObject("impostor_cam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = size; cam.aspect = 0.5f;
            cam.cullingMask = 1 << L; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
            var data = cam.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.renderShadows = false;
            var rt = new RenderTexture(RW, RH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var atlas = new Texture2D(RW * 2, RH, TextureFormat.RGBA32, false);
            for (int v = 0; v < 2; v++)
            {
                var dir = v == 0 ? Vector3.forward : Vector3.right;   // front view looks along +z, side view along +x
                cam.transform.position = new Vector3(0f, size, 0f) - dir * 60f;
                cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                atlas.ReadPixels(new Rect(0, 0, RW, RH), v * RW, 0);
                RenderTexture.active = prev;
            }
            atlas.Apply();
            Object.DestroyImmediate(camGo); Object.DestroyImmediate(go); rt.Release(); Object.DestroyImmediate(rt);
            foreach (var m in mats) Object.DestroyImmediate(m);
            Dilate(atlas, 8);
            var texPath = $"{KitDir}/Textures/imp_{p.id}.png";
            File.WriteAllBytes(texPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.alphaIsTransparency = true;
            ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.45f; ti.wrapMode = TextureWrapMode.Clamp;
            var a = ti.GetPlatformTextureSettings("Android"); a.overridden = true; a.maxTextureSize = 512; a.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(a); ti.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            // the mesh: quad 0 in the XY plane (front view), quad 1 in the ZY plane (side view), each 'size' wide, 2*size tall
            var mb = new MeshBuilder();
            float hw = size * 0.5f, top = size * 2f;
            void Quad(Vector3 right, float u0)
            {
                int i0 = mb.Add(-right * hw, Vector3.up, new Vector2(u0, 0f)), i1 = mb.Add(-right * hw + Vector3.up * top, Vector3.up, new Vector2(u0, 1f));
                int i2 = mb.Add(right * hw + Vector3.up * top, Vector3.up, new Vector2(u0 + 0.5f, 1f)), i3 = mb.Add(right * hw, Vector3.up, new Vector2(u0 + 0.5f, 0f));
                mb.Tri(i0, i1, i2); mb.Tri(i0, i2, i3);
            }
            Quad(Vector3.right, 0f);
            Quad(Vector3.back, 0.5f);     // seen from -x looking +x, its right is -z
            var mesh = mb.ToMesh("imp_" + p.id);
            var meshPath = $"{KitDir}/Models/imp_{p.id}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (old != null) { EditorUtility.CopySerialized(mesh, old); mesh = old; } else AssetDatabase.CreateAsset(mesh, meshPath);
            var matPath = $"{KitDir}/Materials/imp_{p.id}.mat";
            var sh = Shader.Find("CT Life/World/Kit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = sh;
            mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", Color.white); mat.SetFloat("_Toon", 0.4f);
            mat.EnableKeyword("_TINTALL"); mat.SetFloat("_TintAll", 1f); mat.DisableKeyword("_TINTPALETTE"); mat.SetFloat("_TintPalette", 0f);
            mat.SetVector("_Wind", Vector4.zero); mat.SetVector("_GroundAO", new Vector4(1.0f, 0.3f, 0, 0));
            mat.SetFloat("_Cutoff", 0.45f); mat.SetFloat("_Cull", 0f); mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return (mesh, mat);
        }

        /// <summary>Pushes opaque colours outwards into transparent texels (alpha stays 0), n rings.</summary>
        private static void Dilate(Texture2D t, int n)
        {
            int w = t.width, h = t.height;
            var px = t.GetPixels32();
            var filled = px.Select(c => c.a > 8).ToArray();
            for (int it = 0; it < n; it++)
            {
                var next = (bool[])filled.Clone();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (filled[i]) continue;
                        int r = 0, g = 0, bl = 0, k = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                                int j = yy * w + xx;
                                if (!filled[j]) continue;
                                r += px[j].r; g += px[j].g; bl += px[j].b; k++;
                            }
                        if (k == 0) continue;
                        px[i] = new Color32((byte)(r / k), (byte)(g / k), (byte)(bl / k), 0);
                        next[i] = true;
                    }
                filled = next;
            }
            t.SetPixels32(px); t.Apply();
        }

        private static Texture2D FindTexture(string tex)
        {
            foreach (var p in new[] { $"{KitDir}/Textures/{tex}.png", $"Assets/Art/environment/_shared/textures/{tex}.png", $"Assets/Art/environment/_shared/{tex}.png" })
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (t != null) return t;
            }
            Debug.LogWarning("CTLIFE [islandv2] kit texture not found: " + tex);
            return Texture2D.whiteTexture;
        }
    }
}
