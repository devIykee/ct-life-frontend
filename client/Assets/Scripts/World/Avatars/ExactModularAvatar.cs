using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CTLife.World
{
    /// <summary>Validated information for review. Creation revalidates source objects; callers cannot inject a partial plan.</summary>
    public sealed class ModularAvatarPlan
    {
        public int RendererCount => pieces.Count;
        public int TriangleCount { get; internal set; }
        public ModularRole Roles { get; internal set; }
        internal readonly List<Piece> pieces = new List<Piece>();
        internal sealed class Piece
        {
            internal ModularRenderer definition;
            internal SkinnedMeshRenderer source;
            internal string[] bones;
            internal string rootBone;
            internal readonly Dictionary<string, Color> colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        }
    }

    public static class ExactModularAvatar
    {
        // Matrix-entry tolerance in model units. Native importer/pose proof must still measure the actual skin.
        public const float RestMatrixTolerance = 0.0002f;
        private const ModularRole AllRoles = ModularRole.Body | ModularRole.Head | ModularRole.Eyes | ModularRole.Brows |
            ModularRole.Hair | ModularRole.Top | ModularRole.Bottom | ModularRole.Feet;

        public static bool TryResolve(ExactModularCatalog catalog, ModularAvatarRequest request,
            out ModularAvatarPlan plan, out ModularAvatarFailure failure)
        {
            plan = null;
            try { plan = Resolve(catalog, request); failure = default; return true; }
            catch (Rejected e) { failure = new ModularAvatarFailure(e.code, e.Message); return false; }
            catch (Exception e) { failure = new ModularAvatarFailure(ModularAvatarError.InvalidCatalog, e.GetType().Name + ": " + e.Message); return false; }
        }

        private static ModularAvatarPlan Resolve(ExactModularCatalog c, ModularAvatarRequest r)
        {
            Need(r != null && !string.IsNullOrWhiteSpace(r.baseId), ModularAvatarError.InvalidRequest, "An explicit local base ID is required.");
            Need(c != null && !string.IsNullOrWhiteSpace(c.baseId), ModularAvatarError.InvalidCatalog, "No exact modular catalog.");
            Need(c.baseId == r.baseId, ModularAvatarError.IncompatibleBase, "Requested base differs from this catalog.");
            Need(r.accessories.Count == 0, ModularAvatarError.UnsupportedAccessory, "This catalog revision has no validated accessory renderer/socket capability.");
            Need(c.requiredSlots != null && c.requiredSlots.Length > 0, ModularAvatarError.InvalidCatalog, "No required slot contract.");
            var required = new HashSet<ModularSlot>();
            foreach (var slot in c.requiredSlots)
                Need(Enum.IsDefined(typeof(ModularSlot), slot) && required.Add(slot), ModularAvatarError.InvalidCatalog, "Invalid or duplicate required slot.");
            foreach (var slot in required)
                Need(r.parts.TryGetValue(slot, out var id) && !string.IsNullOrWhiteSpace(id), ModularAvatarError.MissingPart, "Missing required slot " + slot + ".");
            foreach (var pair in r.parts)
                Need(required.Contains(pair.Key), ModularAvatarError.UnexpectedPart, "Unsupported slot " + pair.Key + ".");
            var body = ModularRole.Top | ModularRole.Bottom | ModularRole.Feet;
            Need((c.requiredRoles & ~AllRoles) == 0 && (c.requiredRoles & ModularRole.Head) != 0 &&
                 ((c.requiredRoles & ModularRole.Body) != 0 || (c.requiredRoles & body) == body),
                ModularAvatarError.InvalidCatalog, "Required geometry must include a head and complete body coverage.");

            var byId = new Dictionary<string, ModularPart>(StringComparer.Ordinal);
            Need(c.parts != null, ModularAvatarError.InvalidCatalog, "No part records.");
            foreach (var p in c.parts)
            {
                Need(p != null && !string.IsNullOrWhiteSpace(p.id) && !byId.ContainsKey(p.id), ModularAvatarError.InvalidCatalog, "Invalid or duplicate part ID.");
                byId.Add(p.id, p);
            }
            var colors = ResolveColors(c, r);
            ValidateRig(c);
            var rig = IndexHierarchy(c.rigPrefab.transform);
            var result = new ModularAvatarPlan();
            var renderers = new HashSet<GameObject>();
            var addressedRegions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in c.requiredSlots)
            {
                string id = r.parts[slot];
                Need(byId.TryGetValue(id, out var part), ModularAvatarError.MissingPart, "No exact part asset for " + id + ".");
                Need(part.slot == slot && part.baseId == c.baseId, ModularAvatarError.IncompatibleBase, "Part slot/base mismatch: " + id + ".");
                Need(part.renderers != null && part.renderers.Length > 0, ModularAvatarError.MissingGeometry, "Part has no renderers: " + id + ".");
                foreach (var definition in part.renderers)
                {
                    Need(definition != null && definition.prefab != null && renderers.Add(definition.prefab), ModularAvatarError.MissingGeometry, "Missing or duplicate renderer prefab: " + id + ".");
                    Need(definition.roles != ModularRole.None && (definition.roles & ~AllRoles) == 0, ModularAvatarError.MissingGeometry, "Renderer has no explicit geometry role.");
                    Need((result.Roles & definition.roles) == 0, ModularAvatarError.MissingGeometry, "Duplicate body geometry role; merged defaults and parts cannot coexist.");
                    var piece = ValidateRenderer(definition, rig, c.rigPrefab.transform);
                    ValidateTints(piece, colors, addressedRegions);
                    result.pieces.Add(piece);
                    result.Roles |= definition.roles;
                    for (int s = 0; s < piece.source.sharedMesh.subMeshCount; s++)
                        result.TriangleCount += checked((int)piece.source.sharedMesh.GetIndexCount(s) / 3);
                }
            }
            Need((result.Roles & c.requiredRoles) == c.requiredRoles, ModularAvatarError.MissingGeometry, "Assembly is missing required body/head/eyes/brows/hair geometry.");
            foreach (var region in colors.Keys)
                Need(addressedRegions.Contains(region), ModularAvatarError.UnsupportedTint, "No selected renderer implements tint " + region + ".");
            return result;
        }

        private static Dictionary<string, Color> ResolveColors(ExactModularCatalog c, ModularAvatarRequest r)
        {
            var result = new Dictionary<string, Color>(StringComparer.Ordinal);
            var palettes = new Dictionary<string, ModularPalette>(StringComparer.Ordinal);
            foreach (var p in c.palettes ?? Array.Empty<ModularPalette>())
            {
                Need(p != null && !string.IsNullOrWhiteSpace(p.region) && !palettes.ContainsKey(p.region), ModularAvatarError.InvalidCatalog, "Invalid or duplicate palette.");
                palettes.Add(p.region, p);
            }
            foreach (var pair in r.tints)
            {
                Need(!string.IsNullOrWhiteSpace(pair.Key) && palettes.TryGetValue(pair.Key, out _), ModularAvatarError.UnsupportedTint, "Unsupported tint region " + pair.Key + ".");
                var p = palettes[pair.Key];
                ModularSwatch found = null;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var swatch in p.swatches ?? Array.Empty<ModularSwatch>())
                {
                    Need(swatch != null && !string.IsNullOrWhiteSpace(swatch.id) && seen.Add(swatch.id) && Finite(swatch.color), ModularAvatarError.InvalidCatalog, "Invalid or duplicate swatch.");
                    if (swatch.id == pair.Value) found = swatch;
                }
                Need(found != null, ModularAvatarError.UnsupportedTint, "Unknown swatch for " + pair.Key + ": " + pair.Value + ".");
                result.Add(pair.Key, found.color);
            }
            return result;
        }

        private static void ValidateRig(ExactModularCatalog c)
        {
            Need(c.rigPrefab != null && Identity(c.rigPrefab.transform), ModularAvatarError.InvalidRig, "Canonical rig needs an identity root.");
            var animators = c.rigPrefab.GetComponentsInChildren<Animator>(true);
            Need(animators.Length == 1 && animators[0].transform == c.rigPrefab.transform, ModularAvatarError.InvalidRig, "Canonical rig needs one root Animator.");
            Need(c.avatar != null && c.avatar.isValid && c.avatar.isHuman && animators[0].avatar == c.avatar, ModularAvatarError.InvalidRig, "Canonical rig must carry its explicit valid own Humanoid Avatar.");
            Need(c.controller != null && c.height > 0.5f && c.height < 4f, ModularAvatarError.InvalidRig, "Controller/height missing or invalid.");
            Need(c.rigPrefab.GetComponentsInChildren<Renderer>(true).Length == 0, ModularAvatarError.InvalidRig, "Canonical rig contains geometry; it would duplicate the selected body.");
            foreach (var component in c.rigPrefab.GetComponentsInChildren<Component>(true))
                Need(component is Transform || component is Animator, ModularAvatarError.InvalidRig, "Canonical rig may contain only transforms and its root Animator.");
        }

        private static ModularAvatarPlan.Piece ValidateRenderer(ModularRenderer d, Dictionary<string, Transform> rig, Transform canonicalRoot)
        {
            var root = d.prefab.transform;
            Need(Identity(root), ModularAvatarError.InvalidMesh, "Part prefab needs an identity root.");
            IndexHierarchy(root); // Reject ambiguous full paths before doing Transform.Find/rebinding.
            foreach (var component in d.prefab.GetComponentsInChildren<Component>(true))
                Need(component is Transform || component is SkinnedMeshRenderer, ModularAvatarError.InvalidMesh, "Part prefab may contain only transforms and its selected skin.");
            var all = d.prefab.GetComponentsInChildren<Renderer>(true);
            Need(all.Length == 1 && all[0] is SkinnedMeshRenderer, ModularAvatarError.InvalidMesh, "Select exactly one LOD renderer per piece.");
            var smr = (SkinnedMeshRenderer)all[0];
            var mesh = smr.sharedMesh;
            Need(smr.enabled && ActiveBelow(smr.transform, root) && mesh != null && mesh.vertexCount > 0 && mesh.isReadable, ModularAvatarError.InvalidMesh, "Mesh missing, disabled or not readable for exact validation.");
            foreach (var vertex in mesh.vertices) Need(Finite(vertex), ModularAvatarError.InvalidMesh, "Non-finite mesh vertex.");
            Need(Finite(smr.localBounds.center) && Finite(smr.localBounds.extents), ModularAvatarError.InvalidMesh, "Non-finite skin bounds.");
            var bones = smr.bones; var bind = mesh.bindposes;
            Need(bones != null && bones.Length > 0 && bind.Length == bones.Length, ModularAvatarError.InvalidMesh, "Bone/bind-pose counts disagree.");
            var materials = smr.sharedMaterials;
            Need(mesh.subMeshCount > 0 && materials.Length == mesh.subMeshCount, ModularAvatarError.InvalidMaterial, "Every submesh needs exactly one material.");
            foreach (var mat in materials) Need(mat != null && mat.shader != null && mat.shader.isSupported, ModularAvatarError.InvalidMaterial, "Missing/unsupported material shader.");
            for (int s = 0; s < mesh.subMeshCount; s++)
                Need(mesh.GetTopology(s) == MeshTopology.Triangles && mesh.GetIndexCount(s) > 0 && mesh.GetIndexCount(s) % 3 == 0, ModularAvatarError.InvalidMesh, "Empty/non-triangle submesh.");
            // Unity owns this native memory; GetBonesPerVertex is a borrowed view, not a disposable allocation.
            var counts = mesh.GetBonesPerVertex();
            Need(counts.Length == mesh.vertexCount, ModularAvatarError.InvalidMesh, "Missing per-vertex influence counts.");
            foreach (var count in counts) Need(count > 0 && count <= 4, ModularAvatarError.InvalidMesh, "This exact assembler supports one to four influences; no truncation is allowed.");
            var weights = mesh.boneWeights;
            Need(weights.Length == mesh.vertexCount, ModularAvatarError.InvalidMesh, "Every vertex needs supported four-weight skinning.");
            foreach (var w in weights)
            {
                ValidateWeight(w.boneIndex0, w.weight0, bones.Length); ValidateWeight(w.boneIndex1, w.weight1, bones.Length);
                ValidateWeight(w.boneIndex2, w.weight2, bones.Length); ValidateWeight(w.boneIndex3, w.weight3, bones.Length);
                Need(Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1f) < 0.001f, ModularAvatarError.InvalidMesh, "Skin weights do not sum to one.");
            }
            var piece = new ModularAvatarPlan.Piece { definition = d, source = smr, bones = new string[bones.Length] };
            var rendererLocal = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            for (int i = 0; i < bones.Length; i++)
            {
                string path = Path(root, bones[i]);
                Need(path != null && rig.TryGetValue(path, out _), ModularAvatarError.MissingBone, "No exact canonical bone path for index " + i + ".");
                var canonical = rig[path];
                var targetLocal = canonicalRoot.worldToLocalMatrix * canonical.localToWorldMatrix;
                var sourceLocal = root.worldToLocalMatrix * bones[i].localToWorldMatrix;
                Need(MatrixNear(sourceLocal, targetLocal) && MatrixNear(bind[i], targetLocal.inverse * rendererLocal),
                    ModularAvatarError.BindPoseMismatch, "Rest/bind matrix differs for " + path + ".");
                piece.bones[i] = path;
            }
            piece.rootBone = Path(root, smr.rootBone);
            Need(piece.rootBone != null && rig.ContainsKey(piece.rootBone), ModularAvatarError.MissingBone, "No explicit canonical root bone.");
            return piece;
        }

        private static void ValidateTints(ModularAvatarPlan.Piece p, Dictionary<string, Color> colors, HashSet<string> addressed)
        {
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in p.definition.tints ?? Array.Empty<ModularTintBinding>())
            {
                Need(b != null && !string.IsNullOrWhiteSpace(b.region) && b.materialIndex >= 0 && b.materialIndex < p.source.sharedMaterials.Length,
                    ModularAvatarError.InvalidMaterial, "Invalid tint binding.");
                var material = p.source.sharedMaterials[b.materialIndex];
                Need(b.shader != null && material.shader == b.shader && !string.IsNullOrWhiteSpace(b.colorProperty) && material.HasColor(b.colorProperty),
                    ModularAvatarError.InvalidMaterial, "Tint shader/color property does not match the imported material.");
                Need(b.wholeMaterial || (b.mask != null && !string.IsNullOrWhiteSpace(b.maskProperty) && material.HasTexture(b.maskProperty) && material.GetTexture(b.maskProperty) == b.mask),
                    ModularAvatarError.InvalidMaterial, "Tint needs an explicit whole-material region or exact shader/mask binding.");
                Need(occupied.Add(b.materialIndex + ":" + b.colorProperty), ModularAvatarError.InvalidMaterial, "Two tint regions overwrite one material property.");
                if (colors.TryGetValue(b.region, out var color)) { addressed.Add(b.region); p.colors[b.region] = color; }
            }
        }

        /// <summary>No registry/default/procedural fallback. On false, view is null and no partial actor survives.</summary>
        public static bool TryCreate(string name, ExactModularCatalog catalog, ModularAvatarRequest request,
            Transform parent, bool forPreview, out AvatarView view, out ModularAvatarFailure failure)
        {
            view = null;
            if (!TryResolve(catalog, request, out var plan, out failure)) return false;
            GameObject root = null; ModularAvatarResources owner = null;
            try
            {
                root = new GameObject(name); root.SetActive(false);
                if (parent != null) root.transform.SetParent(parent, false);
                owner = root.AddComponent<ModularAvatarResources>();
                var model = Object.Instantiate(catalog.rigPrefab, root.transform, false);
                model.name = catalog.rigPrefab.name; model.SetActive(true);
                var animator = model.GetComponent<Animator>();
                animator.avatar = catalog.avatar; animator.runtimeAnimatorController = catalog.controller;
                animator.applyRootMotion = false;
                animator.cullingMode = forPreview ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullCompletely;
                var bones = IndexHierarchy(model.transform);
                SkinnedMeshRenderer largest = null;
                foreach (var p in plan.pieces)
                {
                    var container = new GameObject("part_" + p.definition.roles); container.transform.SetParent(model.transform, false);
                    var t = CopyRendererChain(p.definition.prefab.transform, p.source.transform, container.transform);
                    var dst = t.gameObject.AddComponent<SkinnedMeshRenderer>();
                    dst.sharedMesh = p.source.sharedMesh; dst.rootBone = bones[p.rootBone];
                    var bound = new Transform[p.bones.Length];
                    for (int i = 0; i < bound.Length; i++) bound[i] = bones[p.bones[i]];
                    dst.bones = bound; dst.localBounds = p.source.localBounds; dst.quality = SkinQuality.Bone4;
                    dst.shadowCastingMode = ShadowCastingMode.Off; dst.receiveShadows = false;
                    var materials = p.source.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) { materials[i] = new Material(materials[i]); owner.Own(materials[i]); }
                    foreach (var b in p.definition.tints ?? Array.Empty<ModularTintBinding>())
                        if (p.colors.TryGetValue(b.region, out var color)) materials[b.materialIndex].SetColor(b.colorProperty, color);
                    dst.sharedMaterials = materials;
                    for (int i = 0; i < dst.sharedMesh.blendShapeCount; i++) dst.SetBlendShapeWeight(i, p.source.GetBlendShapeWeight(i));
                    if (largest == null || dst.sharedMesh.vertexCount > largest.sharedMesh.vertexCount) largest = dst;
                }
                Need(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == plan.RendererCount, ModularAvatarError.AssemblyFailed, "Unexpected renderer count after exact assembly.");
                var collider = root.AddComponent<CapsuleCollider>(); collider.center = new Vector3(0, 0.9f, 0); collider.height = 1.8f; collider.radius = 0.35f; collider.isTrigger = true;
                AvatarFactory.AddBlobShadow(root.transform);
                var result = root.AddComponent<AvatarView>(); result.displayName = name;
                result.Init(animator, null, largest, null, catalog.height);
                root.SetActive(true); if (forPreview) AvatarView.All.Remove(result);
                view = result; failure = default; return true;
            }
            catch (Exception e)
            {
                if (owner != null) owner.Release();
                if (root != null) { root.SetActive(false); GeneratedAvatarResources.DestroyGenerated(root); }
                failure = new ModularAvatarFailure(ModularAvatarError.AssemblyFailed, e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        private static Transform CopyRendererChain(Transform sourceRoot, Transform source, Transform destination)
        {
            var chain = new List<Transform>();
            for (var t = source; t != sourceRoot; t = t.parent) chain.Add(t);
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var copy = new GameObject(chain[i].name).transform; copy.SetParent(destination, false);
                copy.localPosition = chain[i].localPosition; copy.localRotation = chain[i].localRotation; copy.localScale = chain[i].localScale;
                destination = copy;
            }
            return destination;
        }
        private static Dictionary<string, Transform> IndexHierarchy(Transform root)
        {
            var index = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var path = Path(root, t);
                Need(path != null && !index.ContainsKey(path), ModularAvatarError.InvalidRig, "Ambiguous hierarchy path.");
                index.Add(path, t);
                Need(Finite(t.localPosition) && Finite(t.localScale) && Finite(t.localRotation) && Mathf.Abs(t.localScale.x * t.localScale.y * t.localScale.z) > 1e-9f,
                    ModularAvatarError.InvalidRig, "Invalid or singular rig transform.");
            }
            return index;
        }
        private static string Path(Transform root, Transform child)
        {
            if (child == null) return null;
            var names = new List<string>();
            while (child != root)
            {
                if (child == null || child.name.Contains("/")) return null;
                names.Add(child.name); child = child.parent;
            }
            names.Reverse(); return string.Join("/", names);
        }
        private static bool ActiveBelow(Transform t, Transform root) { while (t != root) { if (!t.gameObject.activeSelf) return false; t = t.parent; } return true; }
        private static bool Identity(Transform t) => t.localPosition == Vector3.zero && Quaternion.Angle(t.localRotation, Quaternion.identity) < 0.001f && t.localScale == Vector3.one;
        private static void ValidateWeight(int index, float weight, int count) => Need(Finite(weight) && weight >= 0 && (weight == 0 || index >= 0 && index < count), ModularAvatarError.InvalidMesh, "Invalid skin weight/bone index.");
        private static bool MatrixNear(Matrix4x4 a, Matrix4x4 b) { for (int i = 0; i < 16; i++) if (!Finite(a[i]) || !Finite(b[i]) || Mathf.Abs(a[i] - b[i]) > RestMatrixTolerance) return false; return true; }
        private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        private static bool Finite(Vector3 n) => Finite(n.x) && Finite(n.y) && Finite(n.z);
        private static bool Finite(Quaternion n) => Finite(n.x) && Finite(n.y) && Finite(n.z) && Finite(n.w);
        private static bool Finite(Color n) => Finite(n.r) && Finite(n.g) && Finite(n.b) && Finite(n.a);
        private static void Need(bool value, ModularAvatarError code, string detail) { if (!value) throw new Rejected(code, detail); }
        private sealed class Rejected : Exception { internal readonly ModularAvatarError code; internal Rejected(ModularAvatarError code, string message) : base(message) { this.code = code; } }
    }
}
