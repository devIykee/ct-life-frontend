using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using CTLife.World.Vehicles;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Vehicles v3 (2026-10-21): the studio-quality Sketchfab set from art/processed/vehicles_v3/&lt;name&gt;/
    /// (art/pipeline/veh_v3_process.py: &lt;name&gt;.fbx, textures/, manifest.json). Every folder whose name has a
    /// <see cref="VehicleCatalog"/> spec replaces that slot's v1 model (the hovercar takes the Roadstar's slot and wire kind);
    /// the v1 build keeps the slots v3 has not filled yet. Per vehicle: URP Lit materials built from BaseColor / Normal /
    /// MaskMap (R metallic, G occlusion, B paint mask, A smoothness) / Emission, a tintable Paint material (traffic recolours
    /// it with a property block, <see cref="VehiclePaint"/>), a LODGroup over the LOD1_/LOD2_ children, and a compound box
    /// collider fitted to the hull's vertices. Rerun the same menu item when suv / pickup / van / bike land.
    /// </summary>
    public static partial class VehicleImport
    {
        private const string SourceV3 = "../art/processed/vehicles_v3";
        public const string V3Root = Root + "/v3";
        /// <summary>
        /// LOD transitions (screen-relative height; Android's Medium quality multiplies by its lodBias 0.7). With the 52 deg
        /// camera a 4.5 m car keeps LOD0 to ~21 m, LOD1 (~9.7k tris) to ~65 m, LOD2 (~5k, same atlas and silhouette) to
        /// ~400 m, cross-faded (dithered) so no switch pops (2026-10-22: the old 2.4k LOD2 from ~80 m read as another car).
        /// </summary>
        public static readonly float[] LodHeights = { 0.15f, 0.05f, 0.008f };

        /// <summary>The spec a v3 folder builds: the hovercar is the player's flying car, the Roadstar's slot.</summary>
        public static string SpecIdFor(string v3Name) => v3Name == "hovercar" ? "roadstar" : v3Name;

        private static SortedDictionary<string, (string dir, JObject manifest)> V3Sources()
        {
            var found = new SortedDictionary<string, (string, JObject)>(StringComparer.Ordinal);
            string src = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SourceV3));
            if (!Directory.Exists(src)) return found;
            foreach (var dir in Directory.GetDirectories(src))
            {
                string name = Path.GetFileName(dir);
                string manifest = Path.Combine(dir, "manifest.json"), fbx = Path.Combine(dir, name + ".fbx");
                if (!File.Exists(manifest) || !File.Exists(fbx)) continue;
                if (VehicleCatalog.Get(SpecIdFor(name)) == null)
                {
                    Debug.LogWarning($"CTLIFE [vehicles] v3 {name}: no VehicleCatalog spec for '{SpecIdFor(name)}' yet (add one in VehicleSpec.cs); skipped");
                    continue;
                }
                found[name] = (dir, JObject.Parse(File.ReadAllText(manifest)));
            }
            return found;
        }

        private static void RunV3(SortedDictionary<string, (string dir, JObject manifest)> v3, Material glass)
        {
            if (v3.Count == 0) return;
            Directory.CreateDirectory(V3Root);
            // textures first (V3 import rules: VehiclePostprocessor), then the models next to the v1 ones (same file name keeps
            // the GUID of a replaced v1 model, so nothing else that points at Art/vehicles/<id>.fbx breaks)
            foreach (var kv in v3)
                foreach (var f in Directory.GetFiles(Path.Combine(kv.Value.dir, "textures"), "*.png"))
                    CopyIfChanged(f, $"{V3Root}/{Path.GetFileName(f)}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var kv in v3) CopyIfChanged(Path.Combine(kv.Value.dir, kv.Key + ".fbx"), $"{Root}/{kv.Key}.fbx");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var kv in v3) BuildPrefabV3(kv.Key, VehicleCatalog.Get(SpecIdFor(kv.Key)), kv.Value.manifest, glass);
        }

        // ------------------------------------------------------------------ materials

        private static Material LitAt(string path, string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.shaderKeywords = Array.Empty<string>();
            return m;
        }

        /// <summary>Body: the atlas as authored (metallic + smoothness from the MaskMap). Paint: the same atlas, tinted, with a
        /// clear-coat metallic/smoothness from the spec (the mask's values under the paint read as plastic on the hovercar).</summary>
        private static Dictionary<string, Material> V3Materials(string name, VehicleSpec spec)
        {
            Texture2D Tex(string kind) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{V3Root}/{name}_{kind}.png");
            Texture2D albedo = Tex("BaseColor"), normal = Tex("Normal"), mask = Tex("MaskMap"), emission = Tex("Emission");
            if (albedo == null || mask == null) throw new FileNotFoundException($"{name}: BaseColor / MaskMap missing under {V3Root}");
            void Common(Material m, Color tint)
            {
                m.SetTexture("_BaseMap", albedo); m.SetTexture("_MainTex", albedo);
                m.SetColor("_BaseColor", tint); m.SetColor("_Color", tint);
                if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
                m.SetTexture("_OcclusionMap", mask); m.SetFloat("_OcclusionStrength", 0.85f); m.EnableKeyword("_OCCLUSIONMAP");
                m.SetFloat("_EnvironmentReflections", 1f); m.SetFloat("_SpecularHighlights", 1f);
                m.SetFloat("_WorkflowMode", 1f);   // metallic
            }
            var body = LitAt($"{V3Root}/{name}_Body.mat", $"{name}_Body");
            Common(body, Color.white);
            body.SetTexture("_MetallicGlossMap", mask);
            body.SetFloat("_SmoothnessTextureChannel", 0f);   // smoothness from the metallic map's alpha
            body.SetFloat("_Smoothness", 1f); body.SetFloat("_Metallic", 1f);
            body.EnableKeyword("_METALLICSPECGLOSSMAP");
            if (emission != null)
            {
                body.SetTexture("_EmissionMap", emission);
                body.SetColor("_EmissionColor", Color.white * 1.6f);
                body.EnableKeyword("_EMISSION");
                body.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else { body.SetColor("_EmissionColor", Color.black); body.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; }
            var paint = LitAt($"{V3Root}/{name}_Paint.mat", $"{name}_Paint");
            Common(paint, spec.paint);
            paint.SetTexture("_MetallicGlossMap", null);
            paint.SetFloat("_Metallic", spec.paintMetallic); paint.SetFloat("_Smoothness", spec.paintSmoothness);
            paint.SetFloat("_SmoothnessTextureChannel", 0f);
            paint.SetColor("_EmissionColor", Color.black);
            paint.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(body); EditorUtility.SetDirty(paint);
            return new Dictionary<string, Material>(StringComparer.Ordinal) { ["Body"] = body, ["Paint"] = paint };
        }

        // ------------------------------------------------------------------ the prefab

        private static void BuildPrefabV3(string name, VehicleSpec spec, JObject manifest, Material glass)
        {
            string id = spec.id;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{name}.fbx") ?? throw new FileNotFoundException(name);
            var mats = V3Materials(name, spec);
            var root = new GameObject(id);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                inst.name = "Model";
                // the hovercar's hull is authored floating (manifest hover_height): parked, it rests on its gear; powered,
                // FlightPhysics lifts the model back up by the same amount
                float hover = (float?)manifest["hover_height"] ?? 0f;
                inst.transform.localPosition = Vector3.down * hover; inst.transform.localRotation = Quaternion.identity;
                Transform Find(string n) => inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                // Orientation: front must be +Z (the FBX is exported -Z forward; a wrong axis setting would flip it)
                // (no wheels or lights: the steering wheel is ahead of the seat; the helicopter's seat is ahead of its tail rotor)
                var front = Find("Wheel_FL") ?? Find("Wheel_F") ?? Find("Light_Head_L") ?? Find("SteeringCenter") ?? Find("Seat_Driver");
                var rear = Find("Wheel_RL") ?? Find("Wheel_R") ?? Find("Light_Tail_L") ??
                           (Find("SteeringCenter") != null ? Find("Seat_Driver") : Find("TailRotor"));
                if (front != null && rear != null && Vector3.Dot(front.position - rear.position, Vector3.forward) < 0f)
                {
                    inst.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    Debug.Log($"CTLIFE [vehicles] {id}: v3 model turned 180 deg to face +Z");
                }
                // Materials (LOD children too): Body / Paint by name, Windows* -> tinted glass
                var renderers = inst.GetComponentsInChildren<MeshRenderer>(true);
                var unknown = new HashSet<string>();
                foreach (var r in renderers)
                {
                    var slots = r.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        string mn = slots[i] != null ? slots[i].name : "";
                        if (mn.StartsWith("Windows", StringComparison.Ordinal)) slots[i] = glass;
                        else if (mn.StartsWith("Paint", StringComparison.Ordinal)) slots[i] = mats["Paint"];
                        else if (mn.StartsWith("Body", StringComparison.Ordinal)) slots[i] = mats["Body"];
                        else { unknown.Add(mn); slots[i] = mats["Body"]; }
                    }
                    r.sharedMaterials = slots;
                    int lod = LodOf(r.name);
                    r.shadowCastingMode = lod >= 2 ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    r.receiveShadows = true;
                    r.lightProbeUsage = LightProbeUsage.BlendProbes;
                    r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                }
                if (unknown.Count > 0) Debug.LogWarning($"CTLIFE [vehicles] {id}: unexpected material slots {string.Join(", ", unknown)} drawn with Body");
                var lodGroup = root.AddComponent<LODGroup>();
                var levels = new List<LOD>();
                for (int i = 0; i < 3; i++)
                {
                    var rs = renderers.Where(r => LodOf(r.name) == i).Cast<Renderer>().ToArray();
                    if (rs.Length > 0) levels.Add(new LOD(LodHeights[i], rs));
                }
                lodGroup.SetLODs(levels.ToArray());
                lodGroup.fadeMode = LODFadeMode.CrossFade;
                lodGroup.animateCrossFading = true;          // a short timed dither between LODs (URP asset: LOD cross-fade on)
                lodGroup.RecalculateBounds();
                // Physics body: compound boxes fitted to the hull (the first box is the full-length lower hull: traffic and
                // CarPhysics read it as the car's footprint)
                var rb = root.AddComponent<Rigidbody>();
                rb.mass = spec.mass;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                var pts = HullPoints(root.transform, inst);
                float wheelR = WheelRadius(inst);
                if (spec.kind == VehicleKind.Roadstar && Find("Wheel_FL") == null) wheelR = AddHoverContacts(inst, pts);
                string boxes = Colliders(root, spec, pts, wheelR, manifest);
                var veh = root.AddComponent<Vehicle>();
                veh.specId = id;
                switch (spec.kind)
                {
                    case VehicleKind.Car:
                    case VehicleKind.Scooter: root.AddComponent<CarPhysics>(); break;
                    case VehicleKind.Roadstar: root.AddComponent<CarPhysics>(); root.AddComponent<FlightPhysics>(); break;
                    default: root.AddComponent<FlightPhysics>(); break;
                }
                root.AddComponent<VehicleAudio>();
                if (spec.kind == VehicleKind.Roadstar) HoverGlows(root, inst);
                CharacterLayers.SetLayerRecursively(root, VehicleCatalog.Flies(spec.kind) ? CharacterLayers.VehicleFlight : CharacterLayers.Vehicle);
                // where the anchors landed (root space): seat, entry, wheel, lights must sit on the new body
                var anchors = new[] { "Seat_Driver", "Entry_Driver", "SteeringCenter", "Steering", "Door_FL", "Handlebar", "Wheel_FL", "Wheel_F",
                        "Rotor_Main", "TailRotor", "Light_Head_L", "Light_Tail_L", "Exhaust", "Thruster_0" }
                    .Select(n => (n, t: Find(n))).Where(a => a.t != null)
                    .Select(a => $"{a.n} {root.transform.InverseTransformPoint(a.t.position):F2}");
                var path = $"{PrefabRoot}/{id}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                var tris = levels.Select(l => l.renderers.Sum(r => r.GetComponent<MeshFilter>()?.sharedMesh is Mesh m ? (int)Enumerable.Range(0, m.subMeshCount).Sum(s => (long)m.GetIndexCount(s)) / 3 : 0));
                Debug.Log($"CTLIFE [vehicles] {id} (v3 {name}): prefab {path}, LOD tris {string.Join("/", tris)}, wheel r {wheelR:F3}, {boxes}; {string.Join(", ", anchors)}");
            }
            finally { Object.DestroyImmediate(root); }
        }

        public const string HoverGlowPath = "Assets/Resources/Vehicles/HoverGlow.mat";

        /// <summary>
        /// The hovercar's readability glows (2026-10-22: from the chase camera it read as one black shape): a soft pool of
        /// cyan light on the street under the hull (on the root, so it stays on the ground while the hull lifts) and a
        /// glowing disc under each thruster (on the model). Additive quads on CT Life/Vehicles/Glow; FlightPhysics drives
        /// their intensity with lift and throttle. No collider, no shadows.
        /// </summary>
        private static void HoverGlows(GameObject root, GameObject inst)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(HoverGlowPath);
            var sh = Shader.Find("CT Life/Vehicles/Glow") ?? throw new InvalidOperationException("CT Life/Vehicles/Glow shader missing");
            if (mat == null) { mat = new Material(sh) { name = "HoverGlow" }; AssetDatabase.CreateAsset(mat, HoverGlowPath); }
            mat.shader = sh;
            mat.SetColor("_Color", new Color(0.30f, 0.75f, 1.6f, 1f));
            mat.SetFloat("_Intensity", 1f); mat.SetFloat("_Falloff", 2.2f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            var quad = AssetDatabase.GetBuiltinExtraResource<Mesh>("Quad.fbx");
            GameObject Glow(string name, Transform parent, Vector3 localPos, Vector2 size)
            {
                var g = new GameObject(name);
                g.transform.SetParent(parent, false);
                g.transform.localPosition = localPos;
                g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);      // the quad lies flat (Cull Off: seen from above and below)
                g.transform.localScale = new Vector3(size.x, size.y, 1f);
                g.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                return g;
            }
            Glow("Glow_Under", root.transform, new Vector3(0f, 0.05f, 0f), new Vector2(3.4f, 5.6f));
            foreach (var t in inst.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Thruster_", StringComparison.Ordinal)).ToList())
                Glow("Glow_" + t.name, t, Vector3.down * 0.02f, new Vector2(0.75f, 0.75f));
        }

        private static int LodOf(string n) => n.StartsWith("LOD2_", StringComparison.Ordinal) ? 2 : n.StartsWith("LOD1_", StringComparison.Ordinal) ? 1 : 0;

        /// <summary>The hull's vertices in root space: the coarsest LOD of Body and the doors (wheels and rotors excluded).</summary>
        private static List<Vector3> HullPoints(Transform root, GameObject inst)
        {
            var pts = new List<Vector3>();
            foreach (int want in new[] { 2, 0 })
            {
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (LodOf(mf.name) != want || mf.sharedMesh == null) continue;
                    string part = want == 0 ? mf.name : mf.name.Substring(5);
                    if (part != "Body" && !part.StartsWith("Door_", StringComparison.Ordinal)) continue;
                    foreach (var v in mf.sharedMesh.vertices) pts.Add(root.InverseTransformPoint(mf.transform.TransformPoint(v)));
                }
                if (pts.Count > 0) break;
            }
            if (pts.Count == 0) throw new InvalidOperationException("no readable hull vertices under " + inst.name);
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            Debug.Log($"CTLIFE [vehicles] {root.name}: {pts.Count} hull points, bounds {b.min:F2} .. {b.max:F2}");
            return pts;
        }

        /// <summary>A box round the points with y in [lo, hi]: x/z trimmed to the <paramref name="keep"/> quantile (mirrors,
        /// aerials and spoilers do not widen it), y from lo to the highest point (or hi).</summary>
        private static BoxCollider FitBox(GameObject root, List<Vector3> pts, float lo, float hi, float keep = 0.995f, Func<Vector3, bool> where = null)
        {
            var sel = pts.Where(p => p.y >= lo && p.y <= hi && (where == null || where(p))).ToList();
            if (sel.Count < 8) return null;
            float Q(IEnumerable<float> xs, float q) { var a = xs.OrderBy(x => x).ToArray(); return a[Mathf.Clamp(Mathf.RoundToInt(q * (a.Length - 1)), 0, a.Length - 1)]; }
            float x0 = Q(sel.Select(p => p.x), 1f - keep), x1 = Q(sel.Select(p => p.x), keep);
            float z0 = Q(sel.Select(p => p.z), 1f - keep), z1 = Q(sel.Select(p => p.z), keep);
            float top = Mathf.Min(hi, sel.Max(p => p.y));
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(x1 - x0, Mathf.Max(0.1f, top - lo), z1 - z0);
            box.center = new Vector3((x0 + x1) * 0.5f, (lo + top) * 0.5f, (z0 + z1) * 0.5f);
            return box;
        }

        private static string Colliders(GameObject root, VehicleSpec spec, List<Vector3> pts, float wheelR, JObject manifest)
        {
            float top = pts.Max(p => p.y), minZ = pts.Min(p => p.z), maxZ = pts.Max(p => p.z);
            var made = new List<BoxCollider>();
            switch (spec.kind)
            {
                case VehicleKind.Helicopter:
                {
                    // cabin and skids at the front (+Z) down to the ground (it lands on this box); a slim tail boom and fin behind
                    float split = minZ + (maxZ - minZ) * 0.45f;
                    made.Add(FitBox(root, pts, pts.Min(p => p.y), top, 0.99f, p => p.z > split));
                    var tail = FitBox(root, pts, 0.6f, top, 0.995f, p => p.z <= split);
                    if (tail != null) { var s = tail.size; s.x = Mathf.Min(s.x, 0.7f); tail.size = s; tail.center = new Vector3(0f, tail.center.y, tail.center.z); made.Add(tail); }
                    break;
                }
                case VehicleKind.Scooter:
                    made.Add(FitBox(root, pts, wheelR * 1.15f, top, 0.95f));
                    break;
                default:
                {
                    // cars and the hovercar: a full-length lower hull up to the belt line, a narrower greenhouse above it
                    // (the hovercar: above its gear, which stands in the parked model's lowest 0.4 m)
                    float bottom = (float?)manifest["hover_height"] > 0f ? 0.4f : wheelR * 1.05f;
                    float belt = bottom + (top - bottom) * 0.5f;
                    made.Add(FitBox(root, pts, bottom, belt));
                    var cabin = FitBox(root, pts, belt, top, 0.97f, p => p.y > belt + (top - belt) * 0.3f);
                    if (cabin != null)
                    {
                        // FitBox measured the greenhouse (above the fenders and mirrors); it stands on the lower hull
                        var s = cabin.size; s.y = top - belt; cabin.size = s;
                        cabin.center = new Vector3(cabin.center.x, belt + s.y * 0.5f, cabin.center.z);
                        made.Add(cabin);
                    }
                    break;
                }
            }
            made.RemoveAll(b => b == null);
            if (made.Count == 0) throw new InvalidOperationException(spec.id + ": no collider fitted");
            return "boxes " + string.Join(" + ", made.Select(b => $"{b.size:F2}@{b.center:F2}"));
        }

        /// <summary>
        /// The hovercar has no wheels: four empty "Wheel_*" contacts under the hull corners let CarPhysics' suspension rays
        /// carry it as a hover cushion on the road (its gear sits on the ground, origin at ground level); FlightPhysics takes
        /// over in the air as for the Roadstar. Returns the contact radius (the hub height).
        /// </summary>
        private static float AddHoverContacts(GameObject inst, List<Vector3> pts)
        {
            const float r = 0.3f;
            float minX = pts.Min(p => p.x), maxX = pts.Max(p => p.x), minZ = pts.Min(p => p.z), maxZ = pts.Max(p => p.z);
            float x = (maxX - minX) * 0.34f, z = (maxZ - minZ) * 0.31f, cx = (minX + maxX) * 0.5f, cz = (minZ + maxZ) * 0.5f;
            var parent = inst.transform;
            foreach (var (n, sx, sz) in new[] { ("Wheel_FL", -1f, 1f), ("Wheel_FR", 1f, 1f), ("Wheel_RL", -1f, -1f), ("Wheel_RR", 1f, -1f) })
            {
                var go = new GameObject(n);
                go.transform.SetParent(parent, false);
                go.transform.position = parent.parent.TransformPoint(new Vector3(cx + x * sx, r, cz + z * sz));
            }
            return r;
        }
    }
}
