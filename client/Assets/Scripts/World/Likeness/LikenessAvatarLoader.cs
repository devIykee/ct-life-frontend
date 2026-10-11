using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CTLife.World.Likeness
{
    /// <summary>Owns imported GPU/CPU resources. Instances must be destroyed before disposing their import.</summary>
    public sealed class LikenessLoadedAvatar : IDisposable
    {
        internal GltfImport importer;
        internal Avatar humanoid;
        public GameObject Instance { get; internal set; }
        public Animator Animator { get; internal set; }
        public Bounds Bounds { get; internal set; }
        public LikenessAssetManifest Manifest { get; internal set; }
        public bool CanEquip => Animator != null && Animator.avatar != null && Animator.avatar.isValid && Animator.avatar.isHuman;
        public void Dispose()
        {
            if (Instance != null) { Instance.SetActive(false); Object.Destroy(Instance); Instance = null; }
            if (humanoid != null) { Object.Destroy(humanoid); humanoid = null; }
            importer?.Dispose(); importer = null;
        }
    }

    public static class LikenessAvatarLoader
    {
        public static readonly string[] RequiredBones = { "Hips", "Spine", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand",
            "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };

        public static async Task<LikenessLoadedAvatar> Load(byte[] bytes, LikenessAssetManifest manifest, Transform parent = null,
            bool requireHumanoid = true, CancellationToken ct = default)
        {
            ValidateGlb(bytes);
            if (manifest == null || manifest.bytes != bytes.Length || !string.Equals(manifest.sha256, LikenessClient.Sha256(bytes), StringComparison.Ordinal))
                throw new InvalidDataException("Generated asset does not match its manifest");
            if (requireHumanoid && string.IsNullOrWhiteSpace(manifest.validatorVersion)) throw new InvalidDataException("Asset has not passed trusted validation");
            var result = new LikenessLoadedAvatar { Manifest = manifest, Instance = new GameObject("Likeness-" + manifest.jobId) };
            result.Instance.SetActive(false);
            if (parent != null) result.Instance.transform.SetParent(parent, false);
            var logger = new CollectingLogger();
            result.importer = new GltfImport(materialGenerator: new LikenessMaterials(), logger: logger);
            try
            {
                bool loaded = await result.importer.Load(bytes, importSettings: new ImportSettings {
                    NodeNameMethod = NameImportMethod.OriginalUnique, AnimationMethod = AnimationMethod.None }, cancellationToken: ct);
                if (!loaded || logger.Items?.Any(x => x.Type == LogType.Error || x.Type == LogType.Exception) == true)
                    throw new InvalidDataException("Generated mesh or texture import failed");
                if (!await result.importer.InstantiateMainSceneAsync(result.Instance.transform, ct)) throw new InvalidDataException("Generated scene import failed");
                ct.ThrowIfCancellationRequested();
                if (requireHumanoid) AlignHumanForward(result.Instance, manifest.humanoidBones);
                var renderers = result.Instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0 || renderers.Length > 8) throw new InvalidDataException("Generated renderer budget exceeded");
                long triangles = 0;
                var textures = new HashSet<Texture>();
                foreach (var renderer in renderers)
                {
                    var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) throw new InvalidDataException("Generated renderer has no mesh");
                    for (int i = 0; i < mesh.subMeshCount; i++) triangles += mesh.GetIndexCount(i) / 3;
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || material.shader == null || material.shader.name.Contains("InternalError")) throw new InvalidDataException("Generated material failed");
                        foreach (var property in material.GetTexturePropertyNames()) { var texture = material.GetTexture(property); if (texture != null) textures.Add(texture); }
                    }
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
                }
                if (triangles < 100 || triangles > 12000) throw new InvalidDataException("Generated triangle budget exceeded");
                if (textures.Any(t => t.width > 2048 || t.height > 2048) || textures.Sum(t => (long)t.width * t.height) > 16 * 1024 * 1024)
                    throw new InvalidDataException("Generated texture budget exceeded");
                var bounds = Measure(result.Instance);
                if (bounds.size.y < 0.2f || bounds.size.y > 10f || !Finite(bounds.center) || !Finite(bounds.size)) throw new InvalidDataException("Generated bounds invalid");
                float targetHeight = manifest.heightMeters >= 1.2f && manifest.heightMeters <= 2.3f ? manifest.heightMeters : 1.75f;
                result.Instance.transform.localScale *= targetHeight / bounds.size.y;
                bounds = Measure(result.Instance);
                // A child visual root lets grounding occur without moving its owning scene/actor parent.
                foreach (Transform child in result.Instance.transform) child.localPosition -= result.Instance.transform.InverseTransformVector(new Vector3(bounds.center.x - result.Instance.transform.position.x, bounds.min.y - result.Instance.transform.position.y, bounds.center.z - result.Instance.transform.position.z));
                result.Bounds = Measure(result.Instance);
                if (requireHumanoid) BuildHumanoid(result);
                result.Instance.SetActive(true);
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        private static void AlignHumanForward(GameObject root, Dictionary<string, string> bones)
        {
            // Provider exports can face sideways. The foot→toe vectors establish authored forward without
            // guessing from misleading bone labels or changing the stock character's turntable convention.
            if (bones == null) return;
            var all = root.GetComponentsInChildren<Transform>(true);
            Vector3 forward = Vector3.zero;
            foreach (var side in new[] { "Left", "Right" })
            {
                if (!bones.TryGetValue(side + "Foot", out var footName) || !bones.TryGetValue(side + "Toes", out var toeName)) continue;
                var foot = all.SingleOrDefault(t => t.name == footName); var toe = all.SingleOrDefault(t => t.name == toeName);
                if (foot != null && toe != null) forward += root.transform.InverseTransformVector(toe.position - foot.position);
            }
            forward.y = 0;
            if (forward.sqrMagnitude < .0001f) return;
            var rotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward);
            foreach (Transform child in root.transform) { child.localPosition = rotation * child.localPosition; child.localRotation = rotation * child.localRotation; }
        }

        private static void BuildHumanoid(LikenessLoadedAvatar result)
        {
            var map = result.Manifest.humanoidBones;
            if (map == null || RequiredBones.Any(k => !map.ContainsKey(k))) throw new InvalidDataException("Generated rig is missing required human bones");
            var all = result.Instance.GetComponentsInChildren<Transform>(true);
            var names = all.GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.ToArray());
            var human = new List<HumanBone>();
            var assigned = new HashSet<string>();
            foreach (var pair in map)
            {
                if (!Enum.TryParse<HumanBodyBones>(pair.Key, out var bone) || bone == HumanBodyBones.LastBone || !names.TryGetValue(pair.Value, out var matches) || matches.Length != 1 || !assigned.Add(pair.Value))
                    throw new InvalidDataException("Generated human bone mapping is invalid");
                human.Add(new HumanBone { boneName = pair.Value, humanName = HumanTrait.BoneName[(int)bone], limit = new HumanLimit { useDefaultValues = true } });
            }
            Transform Joint(string name) => names[map[name]][0];
            var head = Joint("Head"); var hips = Joint("Hips");
            if (!head.IsChildOf(hips) || head.position.y <= hips.position.y + result.Bounds.size.y * 0.15f)
                throw new InvalidDataException("Generated head is not above hips in the torso hierarchy");
            foreach (var side in new[] { "Left", "Right" })
            {
                if (!Joint(side + "LowerArm").IsChildOf(Joint(side + "UpperArm")) || !Joint(side + "Hand").IsChildOf(Joint(side + "LowerArm")) ||
                    !Joint(side + "LowerLeg").IsChildOf(Joint(side + "UpperLeg")) || !Joint(side + "Foot").IsChildOf(Joint(side + "LowerLeg")))
                    throw new InvalidDataException("Generated limb hierarchy is invalid");
                if (head.IsChildOf(Joint(side + "UpperArm"))) throw new InvalidDataException("Generated head maps to an arm");
            }
            var desc = new HumanDescription { human = human.ToArray(), skeleton = all.Select(t => new SkeletonBone {
                name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0, hasTranslationDoF = false };
            result.humanoid = AvatarBuilder.BuildHumanAvatar(result.Instance, desc);
            if (result.humanoid == null || !result.humanoid.isValid || !result.humanoid.isHuman) throw new InvalidDataException("Generated rig cannot animate as a human");
            result.Animator = result.Instance.AddComponent<Animator>();
            result.Animator.avatar = result.humanoid;
            result.Animator.runtimeAnimatorController = CharacterRegistry.Instance?.controller ?? ItemCatalog.AvatarController;
            result.Animator.applyRootMotion = false;
            result.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            result.Animator.Rebind();
        }

        public static Bounds Measure(GameObject root)
        {
            bool set = false; var bounds = new Bounds();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                Bounds b;
                if (r is SkinnedMeshRenderer s)
                {
                    var mesh = new Mesh();
                    try { s.BakeMesh(mesh, true); b = WorldBounds(mesh.bounds, s.transform); }
                    finally { Object.Destroy(mesh); }
                }
                else b = r.bounds;
                if (!set) { bounds = b; set = true; } else bounds.Encapsulate(b);
            }
            return bounds;
        }
        private static Bounds WorldBounds(Bounds b, Transform t)
        {
            var result = new Bounds(t.TransformPoint(b.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                result.Encapsulate(t.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))));
            return result;
        }
        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        public static JObject ValidateGlb(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 28 || bytes.Length > LikenessClient.MaxModelBytes || BitConverter.ToUInt32(bytes, 0) != 0x46546c67 || BitConverter.ToUInt32(bytes, 4) != 2 || BitConverter.ToUInt32(bytes, 8) != bytes.Length)
                throw new InvalidDataException("Invalid GLB container");
            int length = checked((int)BitConverter.ToUInt32(bytes, 12));
            if (length < 2 || length > 1024 * 1024 || length + 20L > bytes.Length || BitConverter.ToUInt32(bytes, 16) != 0x4e4f534a) throw new InvalidDataException("Invalid GLB JSON");
            var json = JObject.Parse(Encoding.UTF8.GetString(bytes, 20, length));
            if (json.Descendants().OfType<JProperty>().Any(p => p.Name == "uri")) throw new InvalidDataException("External resources are not accepted");
            if ((json["nodes"] as JArray)?.Count > 200 || (json["images"] as JArray)?.Count > 8 || (json["materials"] as JArray)?.Count > 8 ||
                json["extensionsRequired"] is JArray required && required.Count > 0 || json["animations"] is JArray animations && animations.Count > 0)
                throw new InvalidDataException("Unsupported GLB features or budgets");
            int binaryStart = 20 + length + 8;
            if (binaryStart > bytes.Length || BitConverter.ToUInt32(bytes, 20 + length + 4) != 0x004e4942) throw new InvalidDataException("Missing GLB binary data");
            long pixels = 0;
            foreach (var image in (json["images"] as JArray ?? new JArray()).OfType<JObject>())
            {
                int index = image.Value<int?>("bufferView") ?? -1;
                var views = json["bufferViews"] as JArray;
                if (views == null || index < 0 || index >= views.Count) throw new InvalidDataException("Invalid GLB image buffer");
                var view = json["bufferViews"][index];
                int start = checked(binaryStart + (view.Value<int?>("byteOffset") ?? 0)); int count = view.Value<int>("byteLength");
                LikenessImageHeader.Size(bytes, start, count, out int width, out int height);
                if (width > 2048 || height > 2048) throw new InvalidDataException("Generated image dimensions exceed budget");
                pixels += (long)width * height;
            }
            if (pixels > 16 * 1024 * 1024) throw new InvalidDataException("Generated image memory exceeds budget");
            return json;
        }
    }

    /// <summary>Uses the game's shipped matte material, retaining actual generated base-color texture. No runtime shader stripping surprise.</summary>
    internal sealed class LikenessMaterials : IMaterialGenerator
    {
        public void SetLogger(ICodeLogger logger) { }
        public Material GetDefaultMaterial(bool pointsSupport = false) => throw new InvalidDataException("Generated mesh is missing its material");
        public Material GenerateMaterial(GLTFast.Schema.MaterialBase source, IGltfReadable gltf, bool pointsSupport = false)
        {
            var template = Resources.Load<Material>("Materials/GreyboxLit");
            if (template == null) throw new InvalidDataException("Character material shader is unavailable");
            var pbr = source.PbrMetallicRoughness;
            if (pbr?.BaseColorTexture == null) throw new InvalidDataException("Generated character is missing its color texture");
            var texture = gltf.GetTexture(pbr.BaseColorTexture.index);
            if (texture == null) throw new InvalidDataException("Generated color texture failed to load");
            var material = new Material(template) { name = "Likeness matte" };
            material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", pbr.BaseColor);
            material.SetFloat("_Metallic", 0f); material.SetFloat("_Smoothness", 0.18f); material.SetFloat("_Cull", source.doubleSided ? 0f : 2f);
            return material;
        }
    }
}
