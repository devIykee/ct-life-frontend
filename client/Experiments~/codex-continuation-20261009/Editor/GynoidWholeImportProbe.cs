#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    // Candidate-only importer. No registry, Resources catalogue, source FBX or animation assets are edited.
    public static class GynoidWholeImportProbe
    {
        public const string DefaultRoot = "Assets/CTLifeCandidates/GynoidWholeImportProbe20261009A";
        public sealed class Imported
        {
            public string Root;
            public JObject Plan;
            public GameObject Rig;
            public GameObject Merged;
            public Avatar Avatar;
            public Material Material;
            public readonly Dictionary<string, GameObject> Parts = new Dictionary<string, GameObject>();
        }
        public static string Argument(string key, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key);
            if (i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal)) return args[i + 1];
            if (fallback != null) return fallback;
            throw new ArgumentException("Required argument: " + key);
        }
        public static string Sha(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static void Require(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); }
        public static Imported Import(string planPath, string assetRoot, JObject receipt)
        {
            Require(Application.unityVersion == "6000.3.25f1", "Probe is pinned to Unity 6000.3.25f1.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Import probe must run in Edit mode.");
            Require(assetRoot.StartsWith("Assets/CTLifeCandidates/GynoidWholeImportProbe", StringComparison.Ordinal) &&
                !assetRoot.Contains("..") && !assetRoot.Contains("\\") && !assetRoot.EndsWith("/"), "Invalid candidate root.");
            Require(!Directory.Exists(assetRoot) && !File.Exists(assetRoot + ".meta"), "Candidate path already exists; preserve it and choose a new root.");
            var plan = JObject.Parse(File.ReadAllText(planPath));
            Require((string)plan["baseId"] == "hero_gynoid" && (string)plan["tier"] == "LOD0", "Only Gynoid LOD0 is authorized.");
            Require(plan["wire_look"].Type == JTokenType.Null && plan["wire_head_id"].Type == JTokenType.Null, "Probe may not assign wire IDs.");
            var source = Path.GetFullPath((string)plan["sourceRoot"]);
            var files = (JObject)plan["files"];
            Require(files.Count == 6, "Expected exactly 6 pinned inputs.");
            foreach (var f in files.Properties())
            {
                Require(!Path.IsPathRooted(f.Name) && !f.Name.Contains("..") && !f.Name.Contains("\\"), "Invalid input-relative path.");
                Require(Sha(Path.Combine(source, f.Name)) == (string)f.Value, "Source hash mismatch: " + f.Name);
            }
            var output = new Imported { Root = assetRoot, Plan = plan };
            receipt["sourceRoot"] = source; receipt["sourcePlanSHA256"] = Sha(planPath); receipt["sourceHashes"] = files.DeepClone();
            receipt["assetRoot"] = assetRoot; receipt["baseId"] = "hero_gynoid"; receipt["wireAliases"] = JValue.CreateNull();
            receipt["tintStatus"] = "Original maps preserved; no tint-channel capability or visible recolour acceptance";
            receipt["accessoryStatus"] = "No accessories imported or enabled";
            foreach (var f in files.Properties())
            {
                var dest = assetRoot + "/source/" + f.Name;
                EnsureFolder(Path.GetDirectoryName(dest).Replace("\\", "/")); File.Copy(Path.Combine(source, f.Name), dest, false);
                AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceSynchronousImport);
                if (dest.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) ConfigureModel(dest);
                else if (dest.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ConfigureTexture(dest);
            }
            string modelPath = assetRoot + "/source/" + (string)plan["model"];
            var map = JObject.Parse(File.ReadAllText(Path.Combine(source, "cts_humanoid_map.json")));
            ConfigureOwnAvatar(modelPath, map);
            output.Merged = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            Require(output.Merged != null, "Merged model is missing.");
            output.Avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().Single();
            Require(output.Avatar.isHuman && output.Avatar.isValid, "Own Humanoid Avatar is not valid.");
            ValidateHierarchy(output.Merged, map);
            output.Material = BuildMaterial(output, receipt);
            output.Rig = BuildRig(output);
            foreach (var p in ((JObject)plan["parts"]).Properties())
                output.Parts.Add(p.Name, BuildPart(output, p.Name, (JObject)p.Value, receipt));
            Require(output.Parts.Count == 1 && output.Parts.ContainsKey("whole"), "Exactly one complete LOD0 renderer is required.");
            receipt["wholeAvatarOnly"] = true;
            receipt["ownAvatar"] = new JObject { ["path"] = AssetDatabase.GetAssetPath(output.Avatar),
                ["valid"] = output.Avatar.isValid, ["human"] = output.Avatar.isHuman,
                ["copiedFromOtherAvatar"] = false };
            receipt["nativeImportOnly"] = true;
            return output;
        }
        static void ConfigureModel(string path)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            Require(mi != null, "ModelImporter missing: " + path);
            mi.globalScale = 1; mi.useFileScale = true; mi.bakeAxisConversion = true;
            mi.importCameras = false; mi.importLights = false; mi.importAnimation = false;
            mi.importBlendShapes = true; mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            // Keep source deformers and bone weights; None imported these exact parts as rigid MeshRenderers.
            // Parts create only their own Generic Avatar; it is not used by the assembled actor.
            // The merged model gets its separate own Humanoid Avatar below; BuildPart strips part Animators.
            mi.animationType = ModelImporterAnimationType.Generic; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.sourceAvatar = null; mi.optimizeGameObjects = false; mi.skinWeights = ModelImporterSkinWeights.Standard;
            mi.meshCompression = ModelImporterMeshCompression.Off; mi.isReadable = true;
            mi.SaveAndReimport();
        }
        static void ConfigureTexture(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            Require(ti != null, "TextureImporter missing: " + path);
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = path.EndsWith("_Albedo.png", StringComparison.Ordinal) || path.EndsWith("_Emission.png", StringComparison.Ordinal);
            ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
            ti.isReadable = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear; ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Uncompressed; // Diagnostic inputs, not shipping settings.
            var android = ti.GetPlatformTextureSettings("Android"); android.overridden = false; ti.SetPlatformTextureSettings(android);
            ti.SaveAndReimport();
        }
        static void ConfigureOwnAvatar(string path, JObject map)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Require(model != null, "No rest model.");
            var reference = Object.Instantiate(model);
            var human = new HumanDescription();
            try
            {
                reference.name = model.name;
                ValidateHierarchy(reference, map);
                var named = reference.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, StringComparer.Ordinal);
                var humanoid = ((JObject)map["unity_humanoid"]).Properties().ToDictionary(
                    p => (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), p.Name), p => (string)p.Value);
                // Existing cast importer uses this exact Editor-only T-pose operation on the pinned Unity version.
                var setup = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool", true);
                var wrapper = setup.GetNestedType("BoneWrapper", BindingFlags.Public | BindingFlags.NonPublic);
                var enforce = setup.GetMethod("MakePoseValid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                Require(wrapper != null && enforce != null, "Pinned Unity T-pose tooling changed.");
                var bones = Array.CreateInstance(wrapper, HumanTrait.BoneCount);
                for (int i = 0; i < HumanTrait.BoneCount; i++)
                {
                    Transform bone = humanoid.TryGetValue((HumanBodyBones)i, out var name) ? named[name] : null;
                    bones.SetValue(Activator.CreateInstance(wrapper, new object[] { HumanTrait.BoneName[i], bone }), i);
                }
                enforce.Invoke(null, new object[] { bones });
                human.skeleton = reference.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                    { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
                human.human = humanoid.Select(p => new HumanBone { boneName = p.Value,
                    humanName = HumanTrait.BoneName[(int)p.Key], limit = new HumanLimit { useDefaultValues = true } }).ToArray();
            }
            finally { Object.DestroyImmediate(reference); }
            human.upperArmTwist = human.lowerArmTwist = human.upperLegTwist = human.lowerLegTwist = 0.5f;
            human.armStretch = human.legStretch = 0.05f; human.feetSpacing = 0; human.hasTranslationDoF = false;
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            mi.humanDescription = human; mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; mi.sourceAvatar = null;
            mi.userData = "ct-life:ubc-import-probe-own-avatar-20261009"; mi.SaveAndReimport();
        }
        static void ValidateHierarchy(GameObject model, JObject map)
        {
            var all = model.GetComponentsInChildren<Transform>(true);
            foreach (var bone in ((JObject)map["parent"]).Properties())
            {
                var matches = all.Where(t => t.name == bone.Name).ToArray();
                Require(matches.Length == 1, "Missing or ambiguous CTS bone: " + bone.Name);
                if (bone.Value.Type != JTokenType.Null)
                    Require(matches[0].parent != null && matches[0].parent.name == (string)bone.Value, "Wrong CTS parent: " + bone.Name);
            }
        }
        static Material BuildMaterial(Imported input, JObject receipt)
        {
            var textures = (JObject)input.Plan["textures"]; var loaded = new Dictionary<string, Texture2D>(); var rows = new JObject();
            foreach (var t in textures.Properties())
            {
                var path = input.Root + "/source/" + (string)t.Value;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path); var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                Require(tex != null && tex.width == 1024 && tex.height == 1024, "Texture dimensions changed: " + t.Name);
                Require(imp.sRGBTexture == (t.Name == "albedo" || t.Name == "emission"), "Texture colour space mismatch: " + t.Name);
                loaded[t.Name] = tex; rows[t.Name] = new JObject { ["path"] = path, ["width"] = tex.width, ["height"] = tex.height, ["sRGB"] = imp.sRGBTexture };
            }
            // Mask files are preserved, but their UBC skin/hair channel assumptions do not apply to this fixed android outfit.
            // No tint capability is declared or tested by this whole-avatar proof.
            receipt["maskCapability"] = "Original maps imported, no tint binding or channel-coverage acceptance";
            var shader = Shader.Find("Universal Render Pipeline/Lit"); Require(shader != null, "URP/Lit is missing.");
            var material = new Material(shader) { name = "Gynoid original albedo (no tint capability)" };
            material.SetTexture("_BaseMap", loaded["albedo"]); material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", .15f); material.SetTexture("_EmissionMap", loaded["emission"]);
            material.SetColor("_EmissionColor", Color.white); material.EnableKeyword("_EMISSION");
            AssetDatabase.CreateAsset(material, input.Root + "/OriginalAlbedo.mat"); receipt["materialInputs"] = rows;
            return material;
        }
        static GameObject BuildRig(Imported input)
        {
            var rig = Object.Instantiate(input.Merged);
            try
            {
                rig.name = input.Merged.name; Require(MatrixDelta(rig.transform.localToWorldMatrix, Matrix4x4.identity) < .0001f, "Canonical root is not identity."); Identity(rig.transform);
                foreach (var r in rig.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(r);
                foreach (var f in rig.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(f);
                foreach (var lod in rig.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
                var animators = rig.GetComponentsInChildren<Animator>(true);
                Require(animators.Length == 1 && animators[0].gameObject == rig, "Canonical rig requires one root Animator.");
                animators[0].avatar = input.Avatar; animators[0].runtimeAnimatorController = null;
                animators[0].applyRootMotion = false; animators[0].cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var prefab = PrefabUtility.SaveAsPrefabAsset(rig, input.Root + "/CanonicalRig.prefab");
                Require(prefab != null && prefab.GetComponentsInChildren<Renderer>(true).Length == 0, "Rig retained geometry.");
                return prefab;
            }
            finally { Object.DestroyImmediate(rig); }
        }
        static GameObject BuildPart(Imported input, string key, JObject part, JObject receipt)
        {
            string path = input.Root + "/source/" + (string)part["file"];
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path); Require(source != null, "Missing part model: " + key);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var importedRenderers = new JArray();
            foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                var importedSkin = renderer as SkinnedMeshRenderer;
                var filter = renderer.GetComponent<MeshFilter>();
                var importedMesh = importedSkin != null ? importedSkin.sharedMesh : filter != null ? filter.sharedMesh : null;
                importedRenderers.Add(new JObject { ["name"] = renderer.name, ["type"] = renderer.GetType().Name,
                    ["path"] = AnimationUtility.CalculateTransformPath(renderer.transform, source.transform),
                    ["mesh"] = importedMesh != null ? importedMesh.name : null,
                    ["vertices"] = importedMesh != null ? importedMesh.vertexCount : 0,
                    ["triangles"] = importedMesh != null ? importedMesh.triangles.Length / 3 : 0,
                    ["bindposeCount"] = importedMesh != null ? importedMesh.bindposes.Length : 0,
                    ["boneWeightRecords"] = importedMesh != null ? importedMesh.boneWeights.Length : 0,
                    ["boneCount"] = importedSkin != null ? importedSkin.bones.Length : 0 });
            }
            if (!(receipt["partImportReadback"] is JObject)) receipt["partImportReadback"] = new JObject();
            receipt["partImportReadback"][key] = new JObject { ["assetPath"] = path,
                ["animationType"] = importer.animationType.ToString(), ["avatarSetup"] = importer.avatarSetup.ToString(),
                ["sourceAvatar"] = importer.sourceAvatar != null ? AssetDatabase.GetAssetPath(importer.sourceAvatar) : null,
                ["isReadable"] = importer.isReadable, ["optimizeGameObjects"] = importer.optimizeGameObjects,
                ["renderers"] = importedRenderers };
            Require(key == "whole" && (string)part["file"] == (string)input.Plan["model"], "Whole renderer must come from its exact own Humanoid source.");
            Require(importer.animationType == ModelImporterAnimationType.Human && importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel &&
                importer.sourceAvatar == null && importer.isReadable && !importer.optimizeGameObjects, "Part import settings changed: " + key);
            var instance = Object.Instantiate(source);
            try
            {
                instance.name = source.name; Require(MatrixDelta(instance.transform.localToWorldMatrix, Matrix4x4.identity) < .0001f, "Part root is not identity: " + key); Identity(instance.transform);
                var wanted = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.name == (string)part["renderer"]).ToArray();
                Require(wanted.Length == 1, "Expected exact one LOD0 renderer: " + key); var skin = wanted[0];
                foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) if (r != skin) Object.DestroyImmediate(r);
                foreach (var f in instance.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(f);
                foreach (var a in instance.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
                foreach (var lod in instance.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
                var mesh = skin.sharedMesh; Require(mesh != null && mesh.subMeshCount == 1, "Invalid part mesh/material layout: " + key);
                Require(mesh.triangles.Length / 3 == (int)part["triangles"], "Imported part count differs: " + key);
                Require(mesh.bindposes.Length == skin.bones.Length && skin.bones.All(b => b != null), "Invalid bone/bindpose array: " + key);
                Require(mesh.vertices.All(Finite), "Nonfinite vertices: " + key);
                var bonePaths = skin.bones.Select(b => AnimationUtility.CalculateTransformPath(b, instance.transform)).ToArray();
                Require(bonePaths.Distinct(StringComparer.Ordinal).Count() == bonePaths.Length, "Ambiguous bone paths: " + key);
                var canonical = input.Rig.GetComponentsInChildren<Transform>(true).ToDictionary(t => AnimationUtility.CalculateTransformPath(t, input.Rig.transform), StringComparer.Ordinal);
                if (!(receipt["parts"] is JObject)) receipt["parts"] = new JObject();
                var diagnostics = new JObject { ["role"] = part["role"], ["vertices"] = mesh.vertexCount,
                    ["triangles"] = mesh.triangles.Length / 3, ["boneCount"] = skin.bones.Length, ["bonePaths"] = new JArray(bonePaths),
                    ["meshAsset"] = AssetDatabase.GetAssetPath(mesh) };
                receipt["parts"][key] = diagnostics;
                float maxBind = 0, maxRest = 0;
                for (int i = 0; i < skin.bones.Length; i++)
                {
                    Require(canonical.TryGetValue(bonePaths[i], out var bone), "Canonical bone absent: " + key + ":" + bonePaths[i]);
                    var canonicalLocal = input.Rig.transform.worldToLocalMatrix * bone.localToWorldMatrix;
                    var sourceLocal = instance.transform.worldToLocalMatrix * skin.bones[i].localToWorldMatrix;
                    var expected = canonicalLocal.inverse * instance.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                    float bindDelta = MatrixDelta(expected, mesh.bindposes[i]), restDelta = MatrixDelta(sourceLocal, canonicalLocal);
                    maxBind = Mathf.Max(maxBind, bindDelta); maxRest = Mathf.Max(maxRest, restDelta);
                    diagnostics["maxBindMatrixDelta"] = maxBind; diagnostics["maxRestMatrixDelta"] = maxRest;
                    diagnostics["lastCheckedBone"] = bonePaths[i];
                    Require(bindDelta <= .0002f && restDelta <= .0002f, "Rest/bind mismatch: " + key + ":" + bonePaths[i] + "; bind=" + bindDelta + "; rest=" + restDelta);
                }
                var weights = mesh.boneWeights;
                Require(weights.Length == mesh.vertexCount, "Missing skin weights: " + key);
                foreach (var w in weights)
                {
                    float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                    Require(w.weight0 >= 0 && w.weight1 >= 0 && w.weight2 >= 0 && w.weight3 >= 0 && Finite(sum) && Mathf.Abs(sum - 1) < .001f, "Invalid weight total: " + key);
                    Require(w.boneIndex0 >= 0 && w.boneIndex0 < skin.bones.Length &&
                        (w.weight1 == 0 || w.boneIndex1 >= 0 && w.boneIndex1 < skin.bones.Length) &&
                        (w.weight2 == 0 || w.boneIndex2 >= 0 && w.boneIndex2 < skin.bones.Length) &&
                        (w.weight3 == 0 || w.boneIndex3 >= 0 && w.boneIndex3 < skin.bones.Length), "Invalid weight index: " + key);
                }
                skin.sharedMaterial = input.Material; skin.updateWhenOffscreen = true;
                var outPath = input.Root + "/part-" + key + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, outPath); Require(prefab != null, "Part prefab save failed: " + key);
                diagnostics["prefab"] = outPath;
                return prefab;
            }
            finally { Object.DestroyImmediate(instance); }
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace("\\", "/");
            Require(path.StartsWith("Assets/", StringComparison.Ordinal), "Folder outside Assets.");
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        public static float MatrixDelta(Matrix4x4 a, Matrix4x4 b)
        {
            float max = 0; for (int i = 0; i < 16; i++) { if (!Finite(a[i]) || !Finite(b[i])) return float.PositiveInfinity; max = Mathf.Max(max, Mathf.Abs(a[i] - b[i])); } return max;
        }
        public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        public static void Identity(Transform t) { t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one; }
    }
}
#endif
