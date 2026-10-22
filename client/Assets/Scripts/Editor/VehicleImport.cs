using System;
using System.IO;
using System.Linq;
using CTLife.World;
using CTLife.World.Vehicles;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Feel lane F2: the rigged vehicles from art/processed/vehicles (art/pipeline/veh_build.py) into the game. Copies the
    /// FBX files to Assets/Art/vehicles, builds Resources/Vehicles/&lt;id&gt;.prefab (root: Rigidbody, Vehicle, physics,
    /// audio, a body box collider on the Vehicle layer; child "Model": the FBX), swaps the window material for tinted
    /// glass so drivers show, and adds the act_drive seated pose (UAL1 Driving_Loop, CC0) to the shared controller.
    /// Batch: -executeMethod CTLife.EditorTools.VehicleImport.RunBatch
    /// </summary>
    public static partial class VehicleImport
    {
        public const string Root = "Assets/Art/vehicles";
        public const string PrefabRoot = "Assets/Resources/Vehicles";
        private const string Source = "../art/processed/vehicles";
        private const string GlassPath = Root + "/VehicleGlass.mat";

        [MenuItem("CT Life/Vehicles/Import and build prefabs")]
        public static void Run()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(PrefabRoot);
            string src = Path.GetFullPath(Path.Combine(Application.dataPath, "..", Source));
            var index = JObject.Parse(File.ReadAllText(Path.Combine(src, "vehicles.json")));
            // v3 studio models take over every slot they fill; the v1 build keeps the rest (until suv/pickup/van/bike land)
            var v3 = V3Sources();
            foreach (var id in v3.Keys.Select(SpecIdFor).ToList()) index.Remove(id);
            // textures first (the FBX files reference them as textures/<id>_<name>.png), then the models
            string texSrc = Path.Combine(src, "textures");
            if (Directory.Exists(texSrc))
            {
                Directory.CreateDirectory(TextureRoot);
                foreach (var f in Directory.GetFiles(texSrc, "*.png"))
                    if (index.Properties().Any(p => Path.GetFileName(f).StartsWith(p.Name + "_", StringComparison.Ordinal)))
                        CopyIfChanged(f, $"{TextureRoot}/{Path.GetFileName(f)}");
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var p in index.Properties()) CopyIfChanged(Path.Combine(src, p.Name + ".fbx"), $"{Root}/{p.Name}.fbx");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            // a model imported before its textures existed keeps blank materials: import each again now they are there
            foreach (var p in index.Properties()) AssetDatabase.ImportAsset($"{Root}/{p.Name}.fbx", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var glass = Glass();
            foreach (var p in index.Properties())
            {
                var spec = VehicleCatalog.Get(p.Name);
                if (spec == null) { Debug.LogWarning("CTLIFE [vehicles] no spec for " + p.Name); continue; }
                BuildPrefab(p.Name, spec, (JObject)p.Value, glass);
            }
            RunV3(v3, glass);
            AddDriveState();
            AssetDatabase.SaveAssets();
        }

        public const string TextureRoot = Root + "/textures";

        private static void CopyIfChanged(string from, string to)
        {
            if (!File.Exists(to) || !File.ReadAllBytes(to).SequenceEqual(File.ReadAllBytes(from))) File.Copy(from, to, true);
        }

        public static void RunBatch()
        {
            int code = 0;
            try { Run(); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static Material Glass()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(GlassPath);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "VehicleGlass" };
                AssetDatabase.CreateAsset(m, GlassPath);
            }
            // URP Lit, transparent alpha blend: dark tinted glass you can see the driver through
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", new Color(0.11f, 0.15f, 0.2f, 0.52f));
            m.SetFloat("_Smoothness", 0.9f);
            m.SetFloat("_Metallic", 0.1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void BuildPrefab(string id, VehicleSpec spec, JObject rec, Material glass)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{id}.fbx") ?? throw new FileNotFoundException(id);
            var root = new GameObject(id);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                inst.name = "Model";
                inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
                Transform Find(string n) => inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                // Orientation: the build puts the front at -Y in Blender; it must face +Z here, its left side at -X.
                var fl = Find("Wheel_FL") ?? Find("Wheel_F") ?? Find("Rotor_FL") ?? Find("Seat_Driver");
                var rl = Find("Wheel_RL") ?? Find("Wheel_R") ?? Find("Rotor_BL") ?? Find("TailRotor");
                if (fl != null && rl != null && Vector3.Dot(fl.position - rl.position, Vector3.forward) < 0f)
                {
                    inst.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    Debug.Log($"CTLIFE [vehicles] {id}: model turned 180 deg to face +Z");
                }
                var left = Find("Wheel_FL") ?? Find("Rotor_FL") ?? Find("Seat_Driver");
                string handed = left != null ? (left.position.x < 0f ? "left at -X (ok)" : "left at +X (mirrored?)") : "n/a";
                // the lights say where the nose really is (the wheel names only repeat the build's assumption)
                float head = LightsZ(inst, "Headlight"), rearLights = LightsZ(inst, "TailLight");
                if (float.IsNaN(rearLights)) rearLights = LightsZ(inst, "BrakeLight");
                if (!float.IsNaN(head)) handed += head > 0f ? $", headlights at z {head:F2} (front ok)" : $", headlights at z {head:F2} (BACKWARDS?)";
                if (!float.IsNaN(rearLights)) handed += $", rear lights at z {rearLights:F2}";
                if (!float.IsNaN(head) && head < 0f) Debug.LogWarning($"CTLIFE [vehicles] {id}: the headlights are behind the centre; the model may face backwards");
                // Glass
                foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && mats[i].name.StartsWith("Windows", StringComparison.Ordinal)) { mats[i] = glass; changed = true; }
                    if (changed) r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                // Physics body
                var rb = root.AddComponent<Rigidbody>();
                rb.mass = spec.mass;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                var body = Find("Body");
                var bodyRenderer = body != null ? body.GetComponent<MeshRenderer>() : null;
                var b = bodyRenderer != null ? bodyRenderer.bounds : new Bounds(Vector3.up, Vector3.one);
                var box = root.AddComponent<BoxCollider>();
                float wheelR = WheelRadius(inst);
                float bottom = spec.kind == VehicleKind.Car || spec.kind == VehicleKind.Roadstar ? wheelR * 1.05f
                    : spec.kind == VehicleKind.Scooter ? wheelR * 1.3f : b.min.y + 0.02f;
                var size = new Vector3(b.size.x * 0.96f, Mathf.Max(0.1f, b.max.y - bottom), b.size.z * 0.98f);
                if (spec.kind == VehicleKind.Helicopter) size = new Vector3(b.size.x * 0.9f, b.size.y * 0.85f, b.size.z * 0.62f);
                box.size = size;
                // (the helicopter's cabin is at the front, +Z, its tail boom behind)
                box.center = new Vector3(b.center.x, bottom + size.y * 0.5f, spec.kind == VehicleKind.Helicopter ? b.max.z - size.z * 0.5f - 0.2f : b.center.z);
                if (spec.kind == VehicleKind.Helicopter)
                {
                    var tail = root.AddComponent<BoxCollider>();
                    tail.size = new Vector3(0.5f, 0.8f, b.size.z * 0.42f);
                    tail.center = new Vector3(0f, b.center.y + 0.2f, b.min.z + tail.size.z * 0.5f);
                }
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
                CharacterLayers.SetLayerRecursively(root, VehicleCatalog.Flies(spec.kind) ? CharacterLayers.VehicleFlight : CharacterLayers.Vehicle);
                // what each part renders with (a source with a texture must not come out flat white again)
                var looks = inst.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct()
                    .Select(m => $"{m.name}:{(m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap").name : "-")}");
                Debug.Log($"CTLIFE [vehicles] {id}: materials {string.Join(", ", looks)}");
                foreach (var t in rec["textures"] as JArray ?? new JArray())
                {
                    string file = Path.GetFileNameWithoutExtension((string)t["file"]);
                    bool used = inst.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials)
                        .Any(m => m != null && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null && m.GetTexture("_BaseMap").name == file);
                    if (!used && !file.EndsWith("_Emission", StringComparison.Ordinal)) Debug.LogWarning($"CTLIFE [vehicles] {id}: texture {file} is not on any material");
                }
                var path = $"{PrefabRoot}/{id}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"CTLIFE [vehicles] {id}: prefab {path}, body box {box.size} at {box.center}, wheel r {wheelR:F3}, {handed}");
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>Mean z (root space) of the submeshes drawn with a material named <paramref name="prefix"/>*, NaN if none.</summary>
        private static float LightsZ(GameObject inst, string prefix)
        {
            float sum = 0f; int n = 0;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
                {
                    if (mats[i] == null || !mats[i].name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var c = r.transform.TransformPoint(mesh.GetSubMesh(i).bounds.center);
                    sum += inst.transform.parent != null ? inst.transform.parent.InverseTransformPoint(c).z : c.z; n++;
                }
            }
            return n > 0 ? sum / n : float.NaN;
        }

        private static float WheelRadius(GameObject inst)
        {
            var w = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Wheel_", StringComparison.Ordinal));
            var r = w != null ? w.GetComponent<MeshRenderer>() : null;
            return r != null ? r.bounds.extents.y : 0.3f;
        }

        /// <summary>The seated driving pose (UAL1 Driving_Loop, CC0, retargeted to CTL: art/processed/animations/act_drive.fbx).</summary>
        private static void AddDriveState()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterFeelController.ControllerPath);
            var clip = CharacterFeelController.Clip("Assets/Art/animations/act_drive.fbx");
            if (ctrl == null || clip == null) { Debug.LogWarning("CTLIFE [vehicles] act_drive not wired (controller or clip missing)"); return; }
            var sm = ctrl.layers[0].stateMachine;
            var st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "act_drive") ?? sm.AddState("act_drive");
            st.motion = clip;
            st.writeDefaultValues = true;
            st.iKOnFeet = false;
            EditorUtility.SetDirty(ctrl);
            Debug.Log("CTLIFE [vehicles] act_drive state -> " + clip.name);
        }
    }

    /// <summary>Import settings for Assets/Art/vehicles (after ContentImport's Art defaults): no lightmap UVs, real scale.</summary>
    public sealed class VehiclePostprocessor : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 10;
        public override uint GetVersion() => 4;

        /// <summary>Palette / atlas textures: flat colour cells, so no mipmaps (they would bleed the cells together) and ASTC 4x4.</summary>
        private void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(VehicleImport.V3Root + "/", StringComparison.Ordinal)) { PreprocessV3(); return; }
            if (!assetPath.StartsWith(VehicleImport.TextureRoot + "/", StringComparison.Ordinal)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = 512;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 512, format = TextureImporterFormat.ASTC_4x4,
                compressionQuality = 100,
            });
        }

        /// <summary>
        /// v3 baked atlases: mipmapped, ASTC 6x6 on Android. BaseColor and the MaskMap (its alpha is smoothness) at 2048, the
        /// near-flat normal and emission maps at 1024. Normal maps as normal-map type; the MaskMap linear.
        /// </summary>
        private void PreprocessV3()
        {
            var ti = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool normal = file.EndsWith("_Normal", StringComparison.Ordinal), mask = file.EndsWith("_MaskMap", StringComparison.Ordinal);
            bool emission = file.EndsWith("_Emission", StringComparison.Ordinal);
            int size = normal || emission ? 1024 : 2048;
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal && !mask;
            ti.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.alphaIsTransparency = false;
            ti.mipmapEnabled = true;
            ti.streamingMipmaps = false;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 2;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = size;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = size, format = TextureImporterFormat.ASTC_6x6,
                compressionQuality = 100,
            });
        }

        /// <summary>
        /// v3 models keep their source UV layers in front of the baked one (veh_v3_bake.py adds 'UVAtlas' last and the FBX
        /// carries all of them), so Unity's UV0 is the original, unbaked layout. Move the last UV channel (the atlas) to UV0,
        /// drop the rest, and rebuild the tangents the normal map needs from it.
        /// </summary>
        private void OnPostprocessModel(GameObject g)
        {
            if (!assetPath.StartsWith(VehicleImport.Root + "/", StringComparison.Ordinal)) return;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (!System.IO.File.Exists($"{VehicleImport.V3Root}/{name}_BaseColor.png")) return;
            var uvs = new System.Collections.Generic.List<Vector2>();
            int moved = 0;
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                int atlas = -1;
                for (int ch = 7; ch >= 0 && atlas < 0; ch--) { mesh.GetUVs(ch, uvs); if (uvs.Count > 0 && uvs.Count == mesh.vertexCount) atlas = ch; }
                if (atlas <= 0) continue;
                mesh.GetUVs(atlas, uvs);
                mesh.SetUVs(0, uvs);
                mesh.uv2 = null; mesh.uv3 = null; mesh.uv4 = null; mesh.uv5 = null; mesh.uv6 = null; mesh.uv7 = null; mesh.uv8 = null;
                mesh.RecalculateTangents();
                moved++;
            }
            if (moved > 0) Debug.Log($"CTLIFE [vehicles] {name}: atlas UVs moved to UV0 on {moved} meshes");
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(VehicleImport.Root + "/", StringComparison.Ordinal)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.generateSecondaryUV = false;
            mi.isReadable = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.addCollider = false;
        }
    }
}
