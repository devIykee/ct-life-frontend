using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.Tests
{
    /// <summary>Resolution/assembly invariants, not a visual or imported UBC acceptance test.</summary>
    public sealed class ExactModularWorldTests
    {
        private readonly List<Object> _owned = new List<Object>();
        private ExactModularCatalog _catalog;
        private ModularAvatarRequest _request;
        private Transform _bone;
        private Material _material;
        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        [SetUp] public void Setup()
        {
            var registry = CharacterRegistry.Instance;
            Assert.That(registry, Is.Not.Null, "Existing production registry required; do not substitute a synthetic invalid Avatar.");
            var prefab = registry.defaultPlayer;
            Assert.That(prefab, Is.Not.Null);
            var originalAnimator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(originalAnimator, Is.Not.Null);
            Assert.That(originalAnimator.avatar != null && originalAnimator.avatar.isHuman && originalAnimator.avatar.isValid, Is.True);
            var rig = Own(Object.Instantiate(originalAnimator.gameObject));
            rig.name = "exact_fixture_rig"; rig.SetActive(false);
            rig.transform.SetParent(null); rig.transform.localPosition = Vector3.zero; rig.transform.localRotation = Quaternion.identity; rig.transform.localScale = Vector3.one;
            foreach (var c in rig.GetComponentsInChildren<Component>(true))
                if (!(c is Transform) && !(c is Animator && c.gameObject == rig)) Object.DestroyImmediate(c);
            _bone = rig.transform.GetComponentsInChildren<Transform>(true).First(t => t != rig.transform);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            _material = Own(new Material(shader));
            _catalog = Own(ScriptableObject.CreateInstance<ExactModularCatalog>());
            _catalog.baseId = "fixture-local"; _catalog.rigPrefab = rig; _catalog.avatar = originalAnimator.avatar;
            _catalog.controller = registry.controller;
            Assert.That(_catalog.controller, Is.Not.Null);
            _catalog.requiredSlots = new[] { ModularSlot.Head, ModularSlot.Hair, ModularSlot.Top, ModularSlot.Bottom, ModularSlot.Shoes };
            _catalog.requiredRoles = ModularRole.Head | ModularRole.Eyes | ModularRole.Brows | ModularRole.Hair | ModularRole.Top | ModularRole.Bottom | ModularRole.Feet;
            _catalog.parts = new[]
            {
                Part(ModularSlot.Head, "head", ModularRole.Head, ModularRole.Eyes, ModularRole.Brows),
                Part(ModularSlot.Hair, "hair", ModularRole.Hair), Part(ModularSlot.Top, "top", ModularRole.Top),
                Part(ModularSlot.Bottom, "bottom", ModularRole.Bottom), Part(ModularSlot.Shoes, "feet", ModularRole.Feet),
            };
            _request = new ModularAvatarRequest { baseId = _catalog.baseId };
            foreach (var p in _catalog.parts) _request.parts.Add(p.slot, p.id);
        }
        [TearDown] public void Cleanup()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }
        private ModularPart Part(ModularSlot slot, string id, params ModularRole[] roles)
        {
            var p = new ModularPart { baseId = _catalog.baseId, slot = slot, id = id };
            p.renderers = roles.Select(role => new ModularRenderer { roles = role, prefab = Piece(role.ToString()) }).ToArray(); return p;
        }
        private GameObject Piece(string id)
        {
            var root = Own(Object.Instantiate(_catalog.rigPrefab)); root.name = id; root.SetActive(false);
            Object.DestroyImmediate(root.GetComponent<Animator>());
            var path = Relative(_catalog.rigPrefab.transform, _bone);
            var bone = root.transform.Find(path);
            var body = new GameObject("skin"); body.transform.SetParent(root.transform, false);
            var mesh = Own(new Mesh { name = id + "_triangle" });
            mesh.vertices = new[] { new Vector3(-.01f, .01f, 0), new Vector3(.01f, .01f, 0), new Vector3(0, .03f, 0) };
            mesh.triangles = new[] { 0, 1, 2 }; mesh.RecalculateBounds(); mesh.RecalculateNormals();
            mesh.bindposes = new[] { bone.worldToLocalMatrix * body.transform.localToWorldMatrix };
            mesh.boneWeights = Enumerable.Range(0, 3).Select(_ => new BoneWeight { boneIndex0 = 0, weight0 = 1 }).ToArray();
            var smr = body.AddComponent<SkinnedMeshRenderer>(); smr.sharedMesh = mesh; smr.bones = new[] { bone }; smr.rootBone = bone; smr.sharedMaterial = _material;
            return root;
        }
        private static string Relative(Transform root, Transform t)
        {
            var names = new List<string>(); for (; t != root; t = t.parent) names.Add(t.name); names.Reverse(); return string.Join("/", names);
        }
        private void Reject(ModularAvatarError code)
        {
            Assert.That(ExactModularAvatar.TryResolve(_catalog, _request, out var plan, out var failure), Is.False);
            Assert.That(plan, Is.Null); Assert.That(failure.code, Is.EqualTo(code), failure.ToString());
        }

        [Test] public void CompleteCompositeHeadAndBodyResolve()
        {
            Assert.That(ExactModularAvatar.TryResolve(_catalog, _request, out var plan, out var failure), Is.True, failure.ToString());
            Assert.That(plan.RendererCount, Is.EqualTo(7)); Assert.That(plan.TriangleCount, Is.EqualTo(7));
            Assert.That(plan.Roles, Is.EqualTo(_catalog.requiredRoles));
        }
        [Test] public void CompleteAssemblyBindsEveryPartAndOwnsMaterialClones()
        {
            var parent = Own(new GameObject("complete_parent"));
            Assert.That(ExactModularAvatar.TryCreate("complete", _catalog, _request, parent.transform, true, out var view, out var failure), Is.True, failure.ToString());
            Own(view.gameObject);
            var skins = view.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(skins.Length, Is.EqualTo(7));
            Assert.That(view.Animator.avatar, Is.SameAs(_catalog.avatar));
            Assert.That(view.Animator.runtimeAnimatorController, Is.SameAs(_catalog.controller));
            Assert.That(AvatarView.All.Contains(view), Is.False);
            foreach (var skin in skins)
            {
                Assert.That(skin.bones.All(b => b != null && b.IsChildOf(view.Animator.transform)), Is.True);
                Assert.That(skin.sharedMaterials.Length, Is.EqualTo(skin.sharedMesh.subMeshCount));
                foreach (var material in skin.sharedMaterials) { Assert.That(material, Is.Not.SameAs(_material)); Own(material); }
            }
            var original = _material.GetColor("_BaseColor");
            skins[0].sharedMaterial.SetColor("_BaseColor", Color.magenta);
            Assert.That(_material.GetColor("_BaseColor"), Is.EqualTo(original));
        }
        [Test] public void ExplicitWholeMaterialTintAppliesOnlyToItsOwnedClone()
        {
            _catalog.palettes = new[] { new ModularPalette { region = "Hair", swatches = new[] { new ModularSwatch { id = "red", color = Color.red } } } };
            _catalog.parts[1].renderers[0].tints = new[] { new ModularTintBinding { region = "Hair", shader = _material.shader, colorProperty = "_BaseColor", wholeMaterial = true } };
            _request.tints["Hair"] = "red";
            var original = _material.GetColor("_BaseColor");
            Assert.That(ExactModularAvatar.TryCreate("tinted", _catalog, _request, null, true, out var view, out var failure), Is.True, failure.ToString());
            Own(view.gameObject);
            var skins = view.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int red = 0;
            foreach (var skin in skins)
                foreach (var material in skin.sharedMaterials) { Own(material); if (material.GetColor("_BaseColor") == Color.red) red++; }
            Assert.That(red, Is.EqualTo(1)); Assert.That(_material.GetColor("_BaseColor"), Is.EqualTo(original));
        }
        [Test] public void UnknownSwatchCannotUseTheDefaultColor()
        {
            _catalog.palettes = new[] { new ModularPalette { region = "Hair", swatches = new[] { new ModularSwatch { id = "red", color = Color.red } } } };
            _request.tints["Hair"] = "not-declared"; Reject(ModularAvatarError.UnsupportedTint);
        }
        [Test] public void MissingRequiredSelectionIsNotPartialSuccess() { _request.parts.Remove(ModularSlot.Shoes); Reject(ModularAvatarError.MissingPart); }
        [Test] public void UnknownSelectedPartIsNotSkipped() { _request.parts[ModularSlot.Top] = "not-imported"; Reject(ModularAvatarError.MissingPart); }
        [Test] public void CatalogMembershipWithoutPrefabFails() { _catalog.parts[1].renderers[0].prefab = null; Reject(ModularAvatarError.MissingGeometry); }
        [Test] public void HeadWithoutEyesAndBrowsFails() { _catalog.parts[0].renderers = new[] { _catalog.parts[0].renderers[0] }; Reject(ModularAvatarError.MissingGeometry); }
        [Test] public void WrongProportionFamilyFails() { _catalog.parts[1].baseId = "other-base"; Reject(ModularAvatarError.IncompatibleBase); }
        [Test] public void UnsupportedTintIsNotSilentlyDiscarded() { _request.tints["Skin"] = "skin_01"; Reject(ModularAvatarError.UnsupportedTint); }
        [Test] public void UnsupportedAccessoryIsNotSilentlyDiscarded() { _request.accessories["Head"] = "helmet"; Reject(ModularAvatarError.UnsupportedAccessory); }
        [Test] public void MissingBoneDoesNotFallBackToRoot()
        {
            var smr = _catalog.parts[1].renderers[0].prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            smr.bones[0].name = "wrong-bone"; Reject(ModularAvatarError.MissingBone);
        }
        [Test] public void SameBoneNameWithDifferentRestProportionsFails()
        {
            var smr = _catalog.parts[1].renderers[0].prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            smr.bones[0].localPosition += Vector3.up * .03f; Reject(ModularAvatarError.BindPoseMismatch);
        }
        [Test] public void DifferentBindMatrixFails()
        {
            var mesh = _catalog.parts[1].renderers[0].prefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
            var poses = mesh.bindposes; poses[0].m03 += .02f; mesh.bindposes = poses; Reject(ModularAvatarError.BindPoseMismatch);
        }
        [Test] public void MissingSubmeshMaterialFails()
        {
            _catalog.parts[1].renderers[0].prefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials = Array.Empty<Material>();
            Reject(ModularAvatarError.InvalidMaterial);
        }
        [Test] public void MergedDefaultCannotDuplicateSelectedGeometry()
        {
            _catalog.parts[1].renderers[0].roles |= ModularRole.Head; Reject(ModularAvatarError.MissingGeometry);
        }
        [Test] public void PaletteMembershipWithoutRendererTintCapabilityFails()
        {
            _catalog.palettes = new[] { new ModularPalette { region = "Skin", swatches = new[] { new ModularSwatch { id = "red", color = Color.red } } } };
            _request.tints["Skin"] = "red"; Reject(ModularAvatarError.UnsupportedTint);
        }
        [Test] public void TintPropertyWithoutMaskOrWholeMaterialContractFails()
        {
            _catalog.parts[1].renderers[0].tints = new[] { new ModularTintBinding { region = "Hair", shader = _material.shader, colorProperty = "_BaseColor" } };
            Reject(ModularAvatarError.InvalidMaterial);
        }
        [Test] public void LegacyFallbackCannotMaskExactFailureOrCreateActor()
        {
            var registry = CharacterRegistry.Instance;
            Assert.That(registry.ForLook(new[] { "unknown" }).prefab, Is.Not.Null, "Negative control must have an available legacy fallback.");
            _request.parts.Remove(ModularSlot.Shoes);
            var parent = Own(new GameObject("exact_failure_parent"));
            int all = AvatarView.All.Count;
            Assert.That(ExactModularAvatar.TryCreate("no_partial", _catalog, _request, parent.transform, true, out var view, out var failure), Is.False);
            Assert.That(failure.code, Is.EqualTo(ModularAvatarError.MissingPart)); Assert.That(view, Is.Null);
            Assert.That(parent.transform.childCount, Is.Zero); Assert.That(AvatarView.All.Count, Is.EqualTo(all));
        }
        [Test] public void OwnerReleaseDestroysOnlyClonesAndIsIdempotent()
        {
            Assert.That(ExactModularAvatar.TryCreate("owned_release", _catalog, _request, null, true, out var view, out var failure), Is.True, failure.ToString());
            Own(view.gameObject);
            var skins = view.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var clones = skins.SelectMany(r => r.sharedMaterials).ToArray();
            var sharedMeshes = skins.Select(r => r.sharedMesh).ToArray();
            foreach (var material in clones) Own(material); // Failure-safe test cleanup, not the acceptance action.
            var owner = view.GetComponent<ModularAvatarResources>();
            Assert.That(owner, Is.Not.Null);
            // Exercise the explicit ownership cleanup used on failed creation. This does not claim Play-mode
            // deferred destruction/OnDestroy timing from an Edit-mode test.
            var release = typeof(ModularAvatarResources).GetMethod("Release", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(release, Is.Not.Null);
            release.Invoke(owner, null);
            Assert.That(clones.All(m => m == null), Is.True, "Each allocated material clone must be destroyed.");
            Assert.That(_material != null && sharedMeshes.All(m => m != null) && _catalog.avatar != null, Is.True,
                "Imported/shared material, mesh and Avatar ownership must remain external.");
            Assert.DoesNotThrow(() => release.Invoke(owner, null), "Repeated cleanup must be harmless.");
        }
    }
}
