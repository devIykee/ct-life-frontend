using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Build context for one venue diorama. Venue scripts call the helpers below in venue-local metres
    /// (x right, z forward, y up; matches spots.json). Sourced CC0 models from the environment manifest are merged
    /// per material into a few LOD-grouped static meshes per 10 m cell; procedural trim uses the shared palette
    /// atlas; signage uses one generated atlas per venue. Runtime hooks (live boards, glow) stay separate renderers.
    /// Nothing here edits source FBX files, the backend contract, the venue registry or another venue's folder.
    /// </summary>
    public sealed class VB
    {
        public const string VenuesRoot = "Assets/World/Venues";
        private const string ManifestPath = "Assets/Art/environment/manifest.json";
        private const float CellSize = 10f;

        public readonly VenueSpec Spec;
        public Transform Root { get; private set; }
        public VenueSpots Spots { get; private set; }
        public Rect Walk { get; private set; }
        /// <summary>Inner wall faces (x0 = west, x1 = east, y0 = south, y1 = north) set by a shell helper.</summary>
        public Rect Inner;
        public readonly List<string> Hooks = new List<string>();
        public readonly List<string> Notes = new List<string>();

        private readonly JObject _manifest;
        private readonly Dictionary<string, GameObject> _models = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private readonly Dictionary<Mesh, VenueMeshAcc.MeshCache> _meshCache = new Dictionary<Mesh, VenueMeshAcc.MeshCache>();
        private readonly VenueMeshAcc _glow = new VenueMeshAcc(), _signs = new VenueMeshAcc();

        /// <summary>Main geometry is lightmapped; backdrop geometry (far ground, water, skyline) stays realtime-lit.</summary>
        private sealed class Layer
        {
            public readonly VenueMeshAcc blocks = new VenueMeshAcc();
            public readonly Dictionary<Material, VenueMeshAcc> shell = new Dictionary<Material, VenueMeshAcc>();
        }
        private readonly Layer _main = new Layer(), _back = new Layer();
        private Layer L => Backdrop ? _back : _main;
        private VenueMeshAcc _blocks => L.blocks;

        /// <summary>
        /// While true, placed geometry goes to the "Backdrop" group: not lightmapped (keeps Simple Lit, lit by the
        /// realtime venue sun), so large far ground/water/skyline does not consume lightmap texels.
        /// </summary>
        public bool Backdrop;
        private readonly List<Placement> _placements = new List<Placement>();
        private readonly List<NavMeshBuildSource> _nav = new List<NavMeshBuildSource>();
        private readonly Dictionary<string, int> _sourceCounts = new Dictionary<string, int>();
        private readonly HashSet<string> _writtenAssets = new HashSet<string>();
        private Transform _colliders, _lights, _hooks, _spots;
        private SignAtlas _atlas;
        private Material _signMaterial;
        private int _hookCount;

        private sealed class Placement
        {
            public string id;
            public Matrix4x4 matrix;
            public string screen;
            public string recolor;      // palette colour forced onto every submesh (null = source materials)
            public string glowColor;    // palette colour for KEEP_neon/KEEP_*glow submeshes
            public Vector3 position;
            public bool backdrop;
        }

        public VB(VenueSpec spec)
        {
            Spec = spec;
            _manifest = JObject.Parse(File.ReadAllText(ManifestPath));
        }

        // ------------------------------------------------------------------ lifecycle

        public GameObject Begin(UnityEngine.SceneManagement.Scene scene, VenueSpots spots)
        {
            Spots = spots;
            if (spots.walkable.Count == 0) throw new InvalidDataException(Spec.id + " has no walkable rectangles.");
            float x0 = spots.walkable.Min(r => Mathf.Min(r.x0, r.x1)), x1 = spots.walkable.Max(r => Mathf.Max(r.x0, r.x1));
            float z0 = spots.walkable.Min(r => Mathf.Min(r.z0, r.z1)), z1 = spots.walkable.Max(r => Mathf.Max(r.z0, r.z1));
            Walk = Rect.MinMaxRect(x0, z0, x1, z1);
            Inner = Walk;
            var go = new GameObject(Spec.id);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            Root = go.transform;
            _colliders = Child("Colliders", Root);
            _lights = Child("BakeLights", Root);
            _hooks = Child("Hooks", Root);
            _spots = Child("Spots", Root);
            foreach (var r in spots.walkable)
            {
                var center = new Vector3((r.x0 + r.x1) * .5f, -0.1f, (r.z0 + r.z1) * .5f);
                var size = new Vector3(Mathf.Abs(r.x1 - r.x0), 0.2f, Mathf.Abs(r.z1 - r.z0));
                var ground = Child("Walkable floor (contract rectangle)", _colliders).gameObject;
                ground.transform.localPosition = center;
                ground.AddComponent<BoxCollider>().size = size;
                _nav.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box, transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one), size = size, area = 0,
                });
            }
            _atlas = SignAtlas.Load(Spec.Folder + "/Textures/" + Spec.id + "_signs.json");
            Directory.CreateDirectory(Spec.Folder + "/Meshes");
            Directory.CreateDirectory(Spec.Folder + "/Materials");
            return go;
        }

        public static Transform Child(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        // ------------------------------------------------------------------ spots

        public SpotDef Spot(string localId) =>
            Spots.spots.TryGetValue(localId, out var s) ? s : throw new KeyNotFoundException(Spec.spotsKey + " has no spot " + localId);

        public bool HasSpot(string localId) => Spots.spots.ContainsKey(localId);

        public IEnumerable<SpotDef> AllSpots => Spots.spots.Values.Distinct().Where(s => s.hasPosition);

        public IEnumerable<SpotDef> SpotsWhere(Func<SpotDef, bool> predicate) => AllSpots.Where(predicate).OrderBy(s => s.id, StringComparer.Ordinal);

        public static Vector3 P(SpotDef s) => new Vector3(s.x, 0f, s.z);

        /// <summary>Facing of a spot in degrees (0 = +z).</summary>
        public static float Deg(SpotDef s) => s.rotY * Mathf.Rad2Deg;

        /// <summary>Point at local offset (right, forward) from the spot in its own facing frame.</summary>
        public static Vector3 Rel(SpotDef s, float right, float forward, float up = 0f) =>
            P(s) + Quaternion.Euler(0, Deg(s), 0) * new Vector3(right, up, forward);

        // ------------------------------------------------------------------ sourced models

        /// <summary>
        /// Place a manifest model. <paramref name="facing"/> is the direction its front looks, in degrees (0 = +z).
        /// Manifest models face -Z in source, so the applied yaw is facing + 180.
        /// </summary>
        public void Model(string id, float x, float z, float facing = 180f, float y = 0f, float scale = 1f, string screen = null,
            string recolor = null, string glow = null, Vector3? stretch = null)
        {
            ModelAt(id, new Vector3(x, y, z), facing, scale, screen, recolor, glow, stretch);
        }

        public void ModelAt(string id, Vector3 position, float facing = 180f, float scale = 1f, string screen = null,
            string recolor = null, string glow = null, Vector3? stretch = null)
        {
            LoadModel(id);
            var s = stretch ?? Vector3.one;
            _placements.Add(new Placement
            {
                id = id, position = position, screen = screen, recolor = recolor, glowColor = glow, backdrop = Backdrop,
                matrix = Matrix4x4.TRS(position, Quaternion.Euler(0f, facing + 180f, 0f), new Vector3(s.x, s.y, s.z) * scale),
            });
            _sourceCounts[id] = _sourceCounts.TryGetValue(id, out int n) ? n + 1 : 1;
        }

        /// <summary>Manifest bounding size (x width, y height, z depth) in metres at scale 1.</summary>
        public Vector3 SizeOf(string id)
        {
            var row = _manifest[id] ?? throw new InvalidDataException("Unknown environment id " + id);
            var s = row["size_m"];
            return new Vector3((float)s[0], (float)s[2], (float)s[1]);
        }

        private GameObject LoadModel(string id)
        {
            if (_models.TryGetValue(id, out var model)) return model;
            var row = _manifest[id] ?? throw new InvalidDataException("Unknown environment id " + id);
            var path = "Assets/Art/" + row["fbx"];
            model = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new FileNotFoundException("Missing imported model " + path);
            _models[id] = model;
            return model;
        }

        /// <summary>Tile a manifest floor module (2 m or 4 m) over an area; top surface at y = 0.</summary>
        public void Floor(string tileId, Rect area, float y = 0f)
        {
            var size = SizeOf(tileId);
            float step = Mathf.Round(size.x);
            for (float x = area.xMin + step * .5f; x < area.xMax; x += step)
                for (float z = area.yMin + step * .5f; z < area.yMax; z += step)
                    ShellModel(tileId, Matrix4x4.TRS(new Vector3(x, y - size.y, z), Quaternion.identity, Vector3.one));
        }

        /// <summary>A run of 2 m manifest wall modules from a to b (both on the wall's centre line).</summary>
        public void WallModules(string moduleId, Vector2 a, Vector2 b, float heightScale = 1f, Func<int, string> moduleAt = null)
        {
            var dir = (b - a);
            float length = dir.magnitude;
            int count = Mathf.Max(1, Mathf.RoundToInt(length / 2f));
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg - 90f;
            for (int i = 0; i < count; i++)
            {
                var p = Vector2.Lerp(a, b, (i + .5f) / count);
                ShellModel(moduleAt?.Invoke(i) ?? moduleId,
                    Matrix4x4.TRS(new Vector3(p.x, 0, p.y), Quaternion.Euler(0, yaw, 0), new Vector3(length / count / 2f, heightScale, 1f)));
            }
        }

        private void ShellModel(string id, Matrix4x4 m)
        {
            var model = LoadModel(id);
            _sourceCounts[id] = _sourceCounts.TryGetValue(id, out int n) ? n + 1 : 1;
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (LodIndex(renderer.name) != 0) continue;
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var local = m * (model.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix);
                var mats = renderer.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    var material = Resolve(mats[s], id, out var route, null);
                    if (route == Route.Glow || route == Route.Palette)
                    {
                        (route == Route.Glow ? _glow : _blocks).AppendSubmesh(mesh, s, local, SwatchRect(route == Route.Glow ? "glowwhite" : "grey"), _meshCache);
                        continue;
                    }
                    if (!L.shell.TryGetValue(material, out var acc)) L.shell[material] = acc = new VenueMeshAcc();
                    acc.AppendSubmesh(mesh, s, local, null, _meshCache);
                }
            }
        }

        // ------------------------------------------------------------------ procedural geometry

        private static Rect SwatchRect(string color)
        {
            var uv = VenuePalette.Uv(color);
            return new Rect(uv.x, uv.y, 0f, 0f);
        }

        private static Matrix4x4 BoxMatrix(Vector3 center, Vector3 size, float yaw) =>
            Matrix4x4.TRS(center, Quaternion.Euler(0, yaw, 0), size);

        /// <summary>Solid colour box centred at <paramref name="center"/>.</summary>
        public void Box(Vector3 center, Vector3 size, string color, float yaw = 0f, bool glow = false, bool solid = false, bool collider = false)
        {
            (glow ? _glow : _blocks).Box(BoxMatrix(center, size, yaw), VenuePalette.Uv(color));
            if (solid) Obstacle(center, size, yaw);
            if (collider || solid) Collider(center, size, yaw);
        }

        /// <summary>Box resting on the floor (y = base height).</summary>
        public void BoxOn(float x, float z, Vector3 size, string color, float yaw = 0f, float y = 0f, bool glow = false, bool solid = false) =>
            Box(new Vector3(x, y + size.y * .5f, z), size, color, yaw, glow, solid);

        public void Cylinder(Vector3 basePoint, float radius, float height, string color, int sides = 12, bool glow = false, bool solid = false, float topRadius = -1f)
        {
            (glow ? _glow : _blocks).Cylinder(Matrix4x4.Translate(basePoint), radius, topRadius < 0 ? radius : topRadius, height, sides, VenuePalette.Uv(color));
            if (solid) { var s = new Vector3(radius * 2, height, radius * 2); Obstacle(basePoint + Vector3.up * height * .5f, s, 0); Collider(basePoint + Vector3.up * height * .5f, s, 0); }
        }

        /// <summary>Horizontal cylinder (pipe, rail) between two points.</summary>
        public void Pipe(Vector3 a, Vector3 b, float radius, string color, int sides = 8, bool glow = false)
        {
            var dir = b - a;
            var rot = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            (glow ? _glow : _blocks).Cylinder(Matrix4x4.TRS(a, rot, Vector3.one), radius, radius, dir.magnitude, sides, VenuePalette.Uv(color), false);
        }

        public void Sphere(Vector3 center, float radius, string color, bool glow = false, int rings = 6, int segments = 10) =>
            (glow ? _glow : _blocks).Sphere(Matrix4x4.Translate(center), radius, rings, segments, VenuePalette.Uv(color));

        /// <summary>Ellipsoid (beanbag, dome, canopy) of the given full size.</summary>
        public void Blob(Vector3 center, Vector3 size, string color, bool glow = false, float yaw = 0f) =>
            (glow ? _glow : _blocks).Sphere(Matrix4x4.TRS(center, Quaternion.Euler(0, yaw, 0), size), 0.5f, 6, 10, VenuePalette.Uv(color));

        /// <summary>Flush floor decal (rug, stripe, ring segment) 3 mm above the floor; does not affect navigation.</summary>
        public void Decal(float x, float z, float width, float depth, string color, float yaw = 0f, float y = 0.003f, bool glow = false) =>
            Box(new Vector3(x, y, z), new Vector3(width, 0.004f, depth), color, yaw, glow);

        /// <summary>Ring of flush decal segments (witness circle, arena ring).</summary>
        public void RingDecal(Vector3 center, float radius, float width, string color, int segments = 40, bool glow = false)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = (i + .5f) * Mathf.PI * 2f / segments;
                float len = 2f * Mathf.PI * radius / segments * 1.04f;
                Box(center + new Vector3(Mathf.Cos(a) * radius, 0.004f, Mathf.Sin(a) * radius), new Vector3(len, 0.004f, width), color,
                    -a * Mathf.Rad2Deg + 90f, glow);
            }
        }

        /// <summary>Straight wall of palette blocks along a→b with given height and thickness.</summary>
        public void Wall(Vector2 a, Vector2 b, float height, string color, float thickness = 0.2f, float y = 0f, bool collide = true)
        {
            var mid = (a + b) * .5f;
            var dir = b - a;
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg - 90f;
            Box(new Vector3(mid.x, y + height * .5f, mid.y), new Vector3(dir.magnitude, height, thickness), color, yaw, collider: collide);
        }

        // ------------------------------------------------------------------ signage

        /// <summary>
        /// Static sign from this venue's generated atlas (tools/venues/make_venue_signs.py). Bottom edge centred at
        /// <paramref name="bottomCenter"/>, facing <paramref name="facing"/> degrees (0 = +z). Height follows the
        /// region aspect unless given. A missing atlas entry falls back to a plain panel so builds stay reproducible.
        /// </summary>
        public void Sign(string signId, Vector3 bottomCenter, float width, float facing, float height = -1f, string frame = null, float frameDepth = 0.08f)
        {
            var rect = SignRect(signId, out float aspect);
            if (height <= 0) height = width / aspect;
            var rot = Quaternion.Euler(0, facing + 180f, 0); // panel front faces local -Z
            var m = Matrix4x4.TRS(bottomCenter, rot, Vector3.one);
            if (rect.HasValue) _signs.Panel(m, width, height, rect.Value);
            else _blocks.Panel(m, width, height, SwatchRect("screenbg"));
            if (frame != null)
            {
                var back = bottomCenter + rot * new Vector3(0, height * .5f, frameDepth * .5f + 0.004f);
                Box(back, new Vector3(width + 0.12f, height + 0.12f, frameDepth), frame, facing + 180f);
            }
        }

        /// <summary>Sign art lying flat on the floor (floor graphic), centred at (x, z); top of the art points to <paramref name="facing"/>.</summary>
        public void FloorSign(string signId, float x, float z, float width, float facing = 0f, float depth = -1f)
        {
            var rect = SignRect(signId, out float aspect);
            if (depth <= 0) depth = width / aspect;
            var m = Matrix4x4.TRS(new Vector3(x, 0.006f, z), Quaternion.Euler(0, facing, 0), Vector3.one);
            if (rect.HasValue) _signs.Flat(m, width, depth, rect.Value);
            else _blocks.Flat(m, width, depth, SwatchRect("darkgrey"));
        }

        /// <summary>
        /// Runtime-updatable surface: its own renderer and unlit material (atlas region via _BaseMap_ST, or a
        /// dedicated texture). Named "Hook_{name}" under Hooks; integration code may swap _BaseMap at runtime.
        /// </summary>
        public GameObject HookPanel(string name, string signId, Vector3 bottomCenter, float width, float facing, float height = -1f,
            Texture2D texture = null, string frame = "charcoal")
        {
            Rect? rect = null;
            float aspect = 2f;
            if (texture == null) rect = SignRect(signId, out aspect);
            else aspect = texture.width / (float)texture.height;
            if (height <= 0) height = width / aspect;
            var rot = Quaternion.Euler(0, facing + 180f, 0);
            var acc = new VenueMeshAcc();
            acc.Panel(Matrix4x4.identity, width, height, new Rect(0, 0, 1, 1));
            var go = new GameObject("Hook_" + name);
            go.transform.SetParent(_hooks, false);
            go.transform.localPosition = bottomCenter + rot * new Vector3(0, 0, -0.01f);
            go.transform.localRotation = rot;
            var mesh = SaveMesh(acc.ToMesh("Hook_" + name), "Hook_" + name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = HookMaterial(name, texture, rect);
            Quiet(r);
            if (frame != null)
            {
                var back = bottomCenter + rot * new Vector3(0, height * .5f, 0.05f);
                Box(back, new Vector3(width + 0.14f, height + 0.14f, 0.08f), frame, facing + 180f);
            }
            Hooks.Add("Hook_" + name + (signId != null ? " (idle art: " + signId + ")" : ""));
            _hookCount++;
            return go;
        }

        /// <summary>
        /// Runtime-tintable glowing volume (furnace mouth, alarm beacon): own renderer, unlit material whose
        /// _BaseColor integration code may modulate (e.g. furnace heat from city-wide staked ORE).
        /// </summary>
        public GameObject HookGlow(string name, Vector3 center, Vector3 size, string color, float yaw = 0f, bool sphere = false)
        {
            var acc = new VenueMeshAcc();
            if (sphere) acc.Sphere(Matrix4x4.Scale(size), 0.5f, 6, 10, new Vector2(.5f, .5f));
            else acc.Box(Matrix4x4.Scale(size), new Vector2(.5f, .5f), true);
            var go = new GameObject("Hook_" + name);
            go.transform.SetParent(_hooks, false);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(acc.ToMesh("Hook_" + name), "Hook_" + name);
            var r = go.AddComponent<MeshRenderer>();
            string path = Spec.Folder + "/Materials/Hook_" + name + ".mat";
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.name = "Hook_" + name;
            m.SetTexture("_BaseMap", null);
            m.SetColor("_BaseColor", VenuePalette.ColorOf(color));
            EditorUtility.SetDirty(m);
            _writtenAssets.Add(path);
            r.sharedMaterial = m;
            Quiet(r);
            Hooks.Add("Hook_" + name + " (tint _BaseColor)");
            _hookCount++;
            return go;
        }

        /// <summary>
        /// ORE 5×5 board surface: 25 cells on one renderer whose UVs each cover one texel of a point-filtered 5×5
        /// texture. Integration code writes a 5×5 Texture2D (one pixel per square, row 0 = bottom) into _BaseMap.
        /// Ships an idle bronze state (no invented round data).
        /// </summary>
        public GameObject OreBoard(Vector3 bottomCenter, float size, float facing)
        {
            var tex = new Texture2D(5, 5, TextureFormat.RGBA32, false);
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 5; x++)
                    tex.SetPixel(x, y, (x + y) % 2 == 0 ? new Color(0.36f, 0.25f, 0.10f) : new Color(0.30f, 0.21f, 0.09f));
            tex.Apply();
            var idle = SaveTexture("OreBoard5x5_idle", tex, true);
            var acc = new VenueMeshAcc();
            float cell = size / 5f, gap = cell * 0.08f;
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                {
                    float x = -size * .5f + c * cell + gap * .5f;
                    float y = r * cell + gap * .5f;
                    var m = Matrix4x4.Translate(new Vector3(x + (cell - gap) * .5f, y, 0));
                    acc.Panel(m, cell - gap, cell - gap, new Rect((c + .25f) / 5f, (r + .25f) / 5f, .5f / 5f, .5f / 5f));
                }
            var rot = Quaternion.Euler(0, facing + 180f, 0);
            var go = new GameObject("Hook_OreBoard5x5");
            go.transform.SetParent(_hooks, false);
            go.transform.localPosition = bottomCenter + rot * new Vector3(0, 0, -0.02f);
            go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(acc.ToMesh("Hook_OreBoard5x5"), "Hook_OreBoard5x5");
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = HookMaterial("OreBoard5x5", idle, null);
            Quiet(renderer);
            Hooks.Add("Hook_OreBoard5x5 (5x5 point-filtered _BaseMap; row 0 = bottom)");
            _hookCount++;
            var back = bottomCenter + rot * new Vector3(0, size * .5f, 0.06f);
            Box(back, new Vector3(size + 0.5f, size + 0.5f, 0.1f), "ink", facing + 180f);
            return go;
        }

        private Material HookMaterial(string name, Texture2D texture, Rect? rect)
        {
            string path = Spec.Folder + "/Materials/Hook_" + name + ".mat";
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.name = "Hook_" + name;
            m.SetColor("_BaseColor", Color.white);
            if (texture != null)
            {
                m.SetTexture("_BaseMap", texture);
                m.SetTextureScale("_BaseMap", Vector2.one);
                m.SetTextureOffset("_BaseMap", Vector2.zero);
            }
            else if (rect.HasValue)
            {
                m.SetTexture("_BaseMap", _atlas.Texture);
                m.SetTextureScale("_BaseMap", new Vector2(rect.Value.width, rect.Value.height));
                m.SetTextureOffset("_BaseMap", new Vector2(rect.Value.xMin, rect.Value.yMin));
            }
            else
            {
                m.SetTexture("_BaseMap", VenuePalette.EnsureTexture());
                var uv = VenuePalette.Uv("screenbg");
                m.SetTextureScale("_BaseMap", Vector2.zero);
                m.SetTextureOffset("_BaseMap", uv);
            }
            EditorUtility.SetDirty(m);
            _writtenAssets.Add(path);
            return m;
        }

        private Rect? SignRect(string signId, out float aspect)
        {
            aspect = 2f;
            if (signId == null || _atlas == null || !_atlas.Regions.TryGetValue(signId, out var region))
            {
                if (signId != null) Notes.Add("sign art missing: " + signId);
                return null;
            }
            aspect = region.aspect;
            return region.uv;
        }

        /// <summary>A dedicated small texture (e.g. the ORE 5×5 idle board) saved in this venue's Textures folder.</summary>
        public Texture2D SaveTexture(string name, Texture2D source, bool point)
        {
            string path = Spec.Folder + "/Textures/" + name + ".png";
            Directory.CreateDirectory(Spec.Folder + "/Textures");
            File.WriteAllBytes(path, source.EncodeToPNG());
            Object.DestroyImmediate(source);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = point ? FilterMode.Point : FilterMode.Bilinear;
            importer.mipmapEnabled = !point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = false;
            importer.SaveAndReimport();
            _writtenAssets.Add(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ navigation, colliders, lights

        /// <summary>Not-walkable volume (Unity built-in area 1). Seat anchors are never covered by callers.</summary>
        public void Obstacle(Vector3 center, Vector3 size, float yaw = 0f)
        {
            _nav.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.ModifierBox,
                transform = Matrix4x4.TRS(center, Quaternion.Euler(0, yaw, 0), Vector3.one),
                size = size, area = 1,
            });
        }

        public void ObstacleOn(float x, float z, float width, float depth, float height = 1.2f, float yaw = 0f) =>
            Obstacle(new Vector3(x, height * .5f, z), new Vector3(width, height, depth), yaw);

        public void Collider(Vector3 center, Vector3 size, float yaw = 0f, Transform parent = null)
        {
            var go = Child("Collider", parent != null ? parent : _colliders);
            go.localPosition = center;
            go.localRotation = Quaternion.Euler(0, yaw, 0);
            go.gameObject.AddComponent<BoxCollider>().size = size;
        }

        public void Solid(float x, float z, float width, float depth, float height = 1.2f, float yaw = 0f)
        {
            ObstacleOn(x, z, width, depth, height, yaw);
            Collider(new Vector3(x, height * .5f, z), new Vector3(width, height, depth), yaw);
        }

        public void PointLight(Vector3 position, string color, float intensity, float range)
        {
            var go = Child("Baked point", _lights);
            go.localPosition = position;
            var l = go.gameObject.AddComponent<Light>();
            l.type = LightType.Point;
            l.lightmapBakeType = LightmapBakeType.Baked;
            l.color = VenuePalette.ColorOf(color);
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.Soft;
        }

        public void SpotLight(Vector3 position, Vector3 target, string color, float intensity, float range, float angle)
        {
            var go = Child("Baked spot", _lights);
            go.localPosition = position;
            go.localRotation = Quaternion.LookRotation(target - position);
            var l = go.gameObject.AddComponent<Light>();
            l.type = LightType.Spot;
            l.lightmapBakeType = LightmapBakeType.Baked;
            l.color = VenuePalette.ColorOf(color);
            l.intensity = intensity;
            l.range = range;
            l.spotAngle = angle;
            l.shadows = LightShadows.Soft;
        }

        /// <summary>Tap target for claimable furniture: a collider parented to the spot anchor (WorldInput resolves it).</summary>
        public void SeatCollider(SpotDef spot, Vector3 localCenter, Vector3 size)
        {
            var anchor = AnchorFor(spot);
            var go = Child("Tap target", anchor);
            go.localPosition = localCenter;
            go.gameObject.AddComponent<BoxCollider>().size = size;
        }

        // ------------------------------------------------------------------ anchors

        private readonly Dictionary<string, Transform> _anchors = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Vector3> _approaches = new Dictionary<string, Vector3>();

        /// <summary>Optional standing point beside furniture (venue-local position).</summary>
        public void Approach(SpotDef spot, Vector3 position) => _approaches[spot.id] = position;

        private Transform AnchorFor(SpotDef spot)
        {
            if (_anchors.TryGetValue(spot.id, out var t)) return t;
            t = Child("spot_" + spot.id, _spots);
            t.localPosition = P(spot);
            t.localRotation = Quaternion.Euler(0, Deg(spot), 0);
            var a = t.gameObject.AddComponent<SpotAnchor>();
            a.spotId = spot.id;
            a.kind = spot.kind;
            a.anim = spot.anim ?? "";
            _anchors[spot.id] = t;
            return t;
        }

        // ------------------------------------------------------------------ finalize

        public sealed class Result
        {
            public GameObject prefab;
            public int sourcedInstances, staticRenderers, lod0Triangles, hooks, materials;
            public Dictionary<string, int> sources;
        }

        public Result Finish()
        {
            foreach (var s in AllSpots) AnchorFor(s);
            foreach (var kv in _approaches)
            {
                var anchor = _anchors[kv.Key];
                var ap = Child("Approach", anchor);
                ap.position = Root.TransformPoint(kv.Value);
                anchor.GetComponent<SpotAnchor>().approach = ap;
            }
            var statics = Child("Static", Root);
            int renderers = 0, lod0 = 0;
            var usedMaterials = new HashSet<Material>();

            // Sourced models: per 10 m cell, per material, LOD0 + LOD1 merged into one LODGroup per cell.
            var cells = new Dictionary<Vector3Int, Dictionary<(Material, int), VenueMeshAcc>>();
            foreach (var p in _placements)
            {
                var cell = new Vector3Int(Mathf.FloorToInt(p.position.x / CellSize), Mathf.FloorToInt(p.position.z / CellSize), p.backdrop ? 1 : 0);
                if (!cells.TryGetValue(cell, out var buckets)) cells[cell] = buckets = new Dictionary<(Material, int), VenueMeshAcc>();
                AppendPlacement(p, buckets);
            }
            // Shell: floors and walls, whole venue, one renderer per material (backdrop layer separately).
            Transform backdrop = null;
            int part = 0;
            foreach (var layer in new[] { _main, _back })
            {
                bool isBack = layer == _back;
                if (isBack && (layer.blocks.Empty && layer.shell.Values.All(a => a.Empty) && !_placements.Any(p => p.backdrop))) continue;
                if (isBack) backdrop = Child("Backdrop", Root);
                foreach (var kv in layer.shell)
                {
                    if (kv.Value.Empty) continue;
                    Emit((isBack ? "Backdrop_" : "Shell_") + Sanitize(kv.Key.name) + "_" + part++, kv.Value, kv.Key, isBack ? backdrop : statics);
                    lod0 += kv.Value.Triangles; renderers++; usedMaterials.Add(kv.Key);
                }
                if (!layer.blocks.Empty)
                {
                    Emit(isBack ? "Backdrop_VB_Palette" : "Blocks_VB_Palette", layer.blocks, VenuePalette.Lit, isBack ? backdrop : statics);
                    renderers++; lod0 += layer.blocks.Triangles; usedMaterials.Add(VenuePalette.Lit);
                }
            }

            foreach (var cell in cells.OrderBy(c => c.Key.z).ThenBy(c => c.Key.x).ThenBy(c => c.Key.y))
            {
                var parent = Child($"{(cell.Key.z == 1 ? "BackCell" : "Cell")}_{cell.Key.x}_{cell.Key.y}", cell.Key.z == 1 ? backdrop : statics);
                var levels = new List<Renderer>[2] { new List<Renderer>(), new List<Renderer>() };
                foreach (var kv in cell.Value.OrderBy(k => k.Key.Item2).ThenBy(k => k.Key.Item1.name, StringComparer.Ordinal))
                {
                    if (kv.Value.Empty) continue;
                    var (material, lod) = kv.Key;
                    var r = Emit($"{parent.name}_{Sanitize(material.name)}_LOD{lod}", kv.Value, material, parent);
                    levels[lod].Add(r);
                    renderers++; usedMaterials.Add(material);
                    if (lod == 0) lod0 += kv.Value.Triangles;
                }
                if (levels[1].Count > 0)
                {
                    var group = parent.gameObject.AddComponent<LODGroup>();
                    group.SetLODs(new[] { new LOD(0.22f, levels[0].ToArray()), new LOD(0.015f, levels[1].ToArray()) });
                    group.fadeMode = LODFadeMode.None;
                    group.RecalculateBounds();
                }
            }

            if (!_glow.Empty) { Emit("Glow_VB_Glow", _glow, VenuePalette.Glow, statics); renderers++; lod0 += _glow.Triangles; usedMaterials.Add(VenuePalette.Glow); }
            if (!_signs.Empty)
            {
                Emit("Signs_" + Spec.id, _signs, SignMaterial(), statics);
                renderers++; lod0 += _signs.Triangles; usedMaterials.Add(SignMaterial());
            }

            var venue = Root.gameObject.AddComponent<VenueSceneRoot>();
            venue.venueId = Spec.venueId;
            venue.districtId = Spec.district;
            venue.cameraBounds = new Rect(Walk.xMin, Walk.yMin, Walk.width, Walk.height);
            venue.cameraDistance = Spec.cameraDistance;
            venue.cameraPitch = Spec.cameraPitch;
            venue.cameraYaw = Spec.cameraYaw;
            venue.cameraFieldOfView = Spec.cameraFov;
            venue.cameraLookAhead = Vector3.zero;
            venue.cameraTargetViewportY = Spec.cameraViewportY;
            venue.cameraFocusHeight = 1f;
            var look = Root.gameObject.AddComponent<VenueContentLighting>();
            look.sunColor = Spec.sunColor; look.sunIntensity = Spec.sunIntensity; look.sunAngles = Spec.sunAngles;
            look.sky = Spec.sky; look.equator = Spec.equator; look.ground = Spec.ground; look.haze = Spec.haze;
            venue.navigation = BuildNavigation();
            venue.Init(Spots);

            var prefabPath = Spec.PrefabPath;
            var prefab = PrefabUtility.SaveAsPrefabAsset(Root.gameObject, prefabPath);
            if (prefab == null) throw new IOException("Saving " + prefabPath + " failed.");
            _writtenAssets.Add(prefabPath);
            CleanStaleMeshes();
            return new Result
            {
                prefab = prefab, sourcedInstances = _sourceCounts.Values.Sum(), staticRenderers = renderers, lod0Triangles = lod0,
                hooks = _hookCount, materials = usedMaterials.Count, sources = new Dictionary<string, int>(_sourceCounts),
            };
        }

        private void AppendPlacement(Placement p, Dictionary<(Material, int), VenueMeshAcc> buckets)
        {
            var blocks = (p.backdrop ? _back : _main).blocks;
            var model = LoadModel(p.id);
            var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
            int maxLod = renderers.Max(r => LodIndex(r.name));
            foreach (var renderer in renderers)
            {
                int lod = LodIndex(renderer.name);
                // Level 0 = source LOD0. Level 1 = source LOD1, or LOD0 when the source has a single level.
                var targets = new List<int>();
                if (lod == 0) targets.Add(0);
                if (lod == 1 || (lod == 0 && maxLod == 0)) targets.Add(1);
                if (targets.Count == 0) continue;
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var local = p.matrix * (model.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix);
                var mats = renderer.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    var material = Resolve(mats[s], p.id, out var route, p.screen);
                    if (p.recolor != null && route != Route.Screen && route != Route.Glow) route = Route.Palette;
                    switch (route)
                    {
                        case Route.Screen:
                            if (lod != 0) break; // display surfaces: one copy, no LOD duplicate
                            var rect = SignRect(p.screen, out _);
                            if (rect.HasValue) _signs.AppendSubmesh(mesh, s, local, rect, _meshCache);
                            else blocks.AppendSubmesh(mesh, s, local, SwatchRect("screenbg"), _meshCache);
                            break;
                        case Route.Glow:
                            if (lod != 0) break;
                            _glow.AppendSubmesh(mesh, s, local, SwatchRect(p.glowColor ?? GlowDefault(mats[s])), _meshCache);
                            break;
                        case Route.Palette:
                            if (lod != 0) break;
                            blocks.AppendSubmesh(mesh, s, local, SwatchRect(p.recolor ?? PaletteDefault(mats[s])), _meshCache);
                            break;
                        default:
                            foreach (int level in targets)
                            {
                                if (!buckets.TryGetValue((material, level), out var acc)) buckets[(material, level)] = acc = new VenueMeshAcc();
                                acc.AppendSubmesh(mesh, s, local, null, _meshCache);
                            }
                            break;
                    }
                }
            }
        }

        private static string GlowDefault(Material source)
        {
            string n = source != null ? source.name : "";
            if (n.Contains("on_air")) return "glowred";
            if (n.Contains("furnace") || n.Contains("ore_glow")) return "oreglow";
            if (n.Contains("softbox")) return "glowwhite";
            return "glowgreen";
        }

        private static string PaletteDefault(Material source)
        {
            string n = source != null ? source.name : "";
            if (n.Contains("water")) return "water";
            if (n.Contains("glass")) return "skyblue";
            if (n.Contains("screen") || n.Contains("ad_slot")) return "screenbg";
            return "grey";
        }

        private enum Route { Lit, Screen, Glow, Palette }

        private Material Resolve(Material source, string modelId, out Route route, string screen)
        {
            string name = source != null ? source.name.Replace(" (Instance)", "") : "M_ctl_palette";
            int dot = name.LastIndexOf('.');
            if (dot >= 0 && int.TryParse(name.Substring(dot + 1), out _)) name = name.Substring(0, dot);
            if (name.StartsWith("KEEP_screen") || name.StartsWith("KEEP_ad_slot"))
            {
                route = screen != null ? Route.Screen : Route.Palette;
                return null;
            }
            if (name.StartsWith("KEEP_neon") || name.StartsWith("KEEP_on_air") || name.StartsWith("KEEP_softbox") ||
                name.StartsWith("KEEP_furnace_glow") || name.StartsWith("KEEP_ore_glow"))
            {
                route = Route.Glow;
                return null;
            }
            if (name.StartsWith("KEEP_"))
            {
                route = Route.Palette;
                return null;
            }
            route = Route.Lit;
            return SharedMaterial(name, modelId);
        }

        /// <summary>Shared lit material for a source material name (textures come from the manifest).</summary>
        private Material SharedMaterial(string name, string modelId)
        {
            if (_materials.TryGetValue(name, out var cached)) return cached;
            string path = VenuePalette.SharedRoot + "/Materials/" + name + ".mat";
            Directory.CreateDirectory(VenuePalette.SharedRoot + "/Materials");
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.enableInstancing = true;
            m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
            Texture2D texture = null;
            if (name == "M_ctl_palette") texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/environment/_shared/ctl_palette.png");
            else if (name.StartsWith("M_"))
            {
                string stem = name.Substring(2);
                var textures = _manifest[modelId]?["textures"] as JArray;
                var texPath = textures?.Values<string>().FirstOrDefault(t => Path.GetFileNameWithoutExtension(t) == stem);
                if (texPath != null) texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/" + texPath);
                if (texture == null) texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/environment/_shared/textures/" + stem + ".png");
            }
            if (texture == null) throw new InvalidDataException("No texture for environment material " + name + " (" + modelId + ")");
            m.SetTexture("_BaseMap", texture);
            string lower = name.ToLowerInvariant();
            if (new[] { "leaves", "leaf_", "flowers", "grass", "bush" }.Any(lower.Contains))
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.45f);
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            EditorUtility.SetDirty(m);
            _materials[name] = m;
            return m;
        }

        private Material SignMaterial()
        {
            if (_signMaterial != null) return _signMaterial;
            string path = Spec.Folder + "/Materials/" + Spec.id + "_Signs.mat";
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.name = Spec.id + "_Signs";
            m.SetTexture("_BaseMap", _atlas != null ? _atlas.Texture : VenuePalette.EnsureTexture());
            m.SetColor("_BaseColor", Color.white);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            _writtenAssets.Add(path);
            return _signMaterial = m;
        }

        private Renderer Emit(string name, VenueMeshAcc acc, Material material, Transform parent)
        {
            var mesh = SaveMesh(acc.ToMesh(name), name);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            Quiet(r);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
            return r;
        }

        private static void Quiet(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = Spec.Folder + "/Meshes/" + Sanitize(name) + ".asset";
            _writtenAssets.Add(path);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private NavMeshData BuildNavigation()
        {
            var settings = NavMesh.GetSettingsByID(0);
            var bounds = new Bounds(new Vector3(Walk.center.x, 1f, Walk.center.y), new Vector3(Walk.width + 4f, 8f, Walk.height + 4f));
            var data = NavMeshBuilder.BuildNavMeshData(settings, _nav, bounds, Vector3.zero, Quaternion.identity);
            if (data == null) throw new InvalidOperationException(Spec.id + " NavMesh build returned null.");
            data.name = Spec.id + "_NavMesh";
            string path = Spec.Folder + "/" + Spec.id + "_NavMesh.asset";
            _writtenAssets.Add(path);
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            if (existing == null) { AssetDatabase.CreateAsset(data, path); return data; }
            EditorUtility.CopySerialized(data, existing);
            Object.DestroyImmediate(data);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private void CleanStaleMeshes()
        {
            foreach (var folder in new[] { Spec.Folder + "/Meshes", Spec.Folder + "/Materials" })
                foreach (var guid in AssetDatabase.FindAssets("", new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetDirectoryName(path)?.Replace('\\', '/') != folder) continue;
                    if (!_writtenAssets.Contains(path)) AssetDatabase.DeleteAsset(path);
                }
        }

        public static int LodIndex(string name)
        {
            int i = name.LastIndexOf("_LOD", StringComparison.Ordinal);
            return i >= 0 && i + 4 < name.Length && char.IsDigit(name[i + 4]) ? name[i + 4] - '0' : 0;
        }

        public static string Sanitize(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
    }

    /// <summary>Per-venue sign atlas produced by tools/venues/make_venue_signs.py (PNG + JSON of pixel rects).</summary>
    public sealed class SignAtlas
    {
        public sealed class Region { public Rect uv; public float aspect; }
        public Texture2D Texture;
        public readonly Dictionary<string, Region> Regions = new Dictionary<string, Region>();

        public static SignAtlas Load(string jsonPath)
        {
            if (!File.Exists(jsonPath)) return null;
            var json = JObject.Parse(File.ReadAllText(jsonPath));
            string png = Path.GetDirectoryName(jsonPath).Replace('\\', '/') + "/" + (string)json["image"];
            AssetDatabase.ImportAsset(png, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(png);
            if (importer == null) throw new FileNotFoundException("Sign atlas image not imported: " + png);
            var android = importer.GetPlatformTextureSettings("Android");
            if (importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize != 2048 || !android.overridden ||
                android.format != TextureImporterFormat.ASTC_6x6 || importer.textureType != TextureImporterType.Default || !importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 2048;
                importer.alphaIsTransparency = false;
                android.overridden = true;
                android.maxTextureSize = 2048;
                android.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
            var atlas = new SignAtlas { Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(png) };
            float w = (float)json["width"], h = (float)json["height"];
            foreach (var p in ((JObject)json["regions"]).Properties())
            {
                var r = (JArray)p.Value;
                float x = (float)r[0], y = (float)r[1], rw = (float)r[2], rh = (float)r[3];
                // JSON rects are top-left pixel space; UVs are bottom-left. Inset half a texel against bleeding.
                atlas.Regions[p.Name] = new Region
                {
                    uv = Rect.MinMaxRect((x + .5f) / w, 1f - (y + rh - .5f) / h, (x + rw - .5f) / w, 1f - (y + .5f) / h),
                    aspect = rw / rh,
                };
            }
            return atlas;
        }
    }
}
