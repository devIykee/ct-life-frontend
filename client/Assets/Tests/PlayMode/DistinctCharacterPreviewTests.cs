#if UNITY_INCLUDE_TESTS && UNITY_EDITOR && !CTLIFE_PROD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CTLife.App;
using CTLife.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace CTLife.Tests
{
    /// <summary>Real imported human/creature meshes, controller and preview camera; no network or saved account state.</summary>
    public sealed class DistinctCharacterPreviewTests
    {
        private static readonly string[] Ids = { "ubc_m", "pm_shark", "pm_froggy" };
        private static readonly float[] Heights = { 1.84f, 2.2088f, 1.5656f };

        [SetUp]
        public void RequireEmptyEditorPlayModeScene()
        {
            Assert.IsTrue(Application.isPlaying, "Run in Editor PlayMode.");
            Assert.IsNull(UiRoot.Instance, "Do not run over the user's game.");
            Assert.IsNull(Object.FindAnyObjectByType<FlowController>(), "A game flow is already running.");
        }

        [UnityTest]
        public IEnumerator WorldPrefabsStayAtAuthoredMetreScale()
        {
            var controller = Resources.Load<RuntimeAnimatorController>("Characters/AvatarController");
            Assert.IsNotNull(controller);
            for (int i = 0; i < Ids.Length; i++)
            {
                string id = Ids[i];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Art/characters/distinct/prefabs/" + id + ".prefab");
                Assert.IsNotNull(prefab, id + " world prefab must be imported before running this fixture.");
                var actor = Object.Instantiate(prefab);
                try
                {
                    var animator = actor.GetComponent<Animator>();
                    Assert.IsNotNull(animator);
                    Assert.IsTrue(animator.isHuman);
                    animator.runtimeAnimatorController = controller;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.Rebind();
                    animator.Play("loco_idle", 0, .2f);
                    animator.Update(0f);
                    yield return null;
                    var body = actor.GetComponentsInChildren<SkinnedMeshRenderer>()
                        .OrderByDescending(r => r.sharedMesh.vertexCount).First();
                    var vertices = WorldSurface(body);
                    float height = vertices.Max(v => v.y) - vertices.Min(v => v.y);
                    Assert.That(height, Is.InRange(Heights[i] * .8f, Heights[i] * 1.15f),
                        id + " must remain at the authored metre scale after Humanoid retargeting.");
                }
                finally { Object.Destroy(actor); }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator HumanAndCreaturePreviewsRenderBodiesAndUncroppedHeads()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "This regression needs the graphics-enabled Editor batch used for portrait baking.");
            foreach (string id in Ids)
            {
                var preview = new AvatarPreview(320, 400, transparent: true);
                try
                {
                    preview.SetVisible(false);
                    preview.SetIdle("idle_fidget_foldarms");
                    preview.SetLook(new[] { "skin_tone3", "head_" + id, "hair_" + id,
                        "top_" + id, "bottom_" + id, "shoes_" + id, "acc_none" });
                    preview.SetYaw(-18f);
                    yield return null;
                    var animator = preview.Camera.transform.parent.GetComponentInChildren<Animator>();
                    Assert.IsNotNull(animator, id + " must resolve its actual hero.");
                    var body = animator.GetComponentsInChildren<SkinnedMeshRenderer>().Single();
                    StringAssert.StartsWith(id, body.sharedMaterial.name, "A fallback avatar must not satisfy this test.");
                    var head = animator.GetBoneTransform(HumanBodyBones.Head);
                    Assert.IsNotNull(head);
                    Assert.That(Vector3.Distance(preview.Camera.transform.position, head.position), Is.LessThan(12f),
                        id + " camera must frame the actual actor, not a second 100x TransformPoint scale.");
                    AssertVisiblePixels(preview, id + " whole body");

                    preview.SetPortrait(true);
                    foreach (var point in WorldSurface(body).Where(p => p.y >= head.position.y))
                    {
                        var screen = preview.Camera.WorldToViewportPoint(point);
                        Assert.That(screen.z, Is.GreaterThan(preview.Camera.nearClipPlane), id + " head is behind the camera.");
                        Assert.That(screen.x, Is.InRange(.01f, .99f), id + " head silhouette is cropped horizontally.");
                        Assert.That(screen.y, Is.InRange(.01f, .99f), id + " head silhouette is cropped vertically.");
                    }
                    AssertVisiblePixels(preview, id + " head portrait");
                }
                finally { preview.Dispose(); }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator HumanTorsoRendersOpaqueWhileThePreviewBackgroundStaysTransparent()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            foreach (string id in new[] { "head_snow", "head_rain" })
            {
                var look = CreatorLookCatalog.Available(true).First(e => e.look[1] == id).look;
                var preview = new AvatarPreview(400, 400, transparent: true);
                var image = new Texture2D(400, 400, TextureFormat.RGBA32, false);
                var previous = RenderTexture.active;
                try
                {
                    preview.SetVisible(false);
                    preview.SetLook(look);
                    yield return null;
                    var animator = preview.Camera.transform.parent.GetComponentInChildren<Animator>();
                    // Optimized authored rigs do not expose their transforms until the measurement harness requests them.
                    AnimatorUtility.DeoptimizeTransformHierarchy(animator.gameObject);
                    var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    var chest = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
                    Assert.IsNotNull(hips, id + " hips"); Assert.IsNotNull(chest, id + " torso anchor");
                    foreach (string state in new[] { "loco_idle", "emote_wave" })
                    {
                        animator.Play(state, 0, .35f); animator.Update(0f);
                        foreach (float yaw in new[] { -18f, 72f, 162f, 252f })
                        {
                            preview.SetYaw(yaw);
                            preview.Camera.Render();
                            RenderTexture.active = preview.Texture;
                            image.ReadPixels(new Rect(0, 0, 400, 400), 0, 0, false); image.Apply(false);
                            // Test the enclosed torso, not hair edges: authored hair/cutout alpha must remain valid.
                            var centre = preview.Camera.WorldToViewportPoint(Vector3.Lerp(hips.position, chest.position, .65f));
                            Assert.That(centre.x, Is.InRange(.05f, .95f));
                            Assert.That(centre.y, Is.InRange(.05f, .95f));
                            int x = Mathf.RoundToInt(centre.x * 399), y = Mathf.RoundToInt(centre.y * 399);
                            for (int dx = -1; dx <= 1; dx++)
                                for (int dy = -1; dy <= 1; dy++)
                                    Assert.That(image.GetPixel(x + dx, y + dy).a, Is.GreaterThan(.98f),
                                        id + " " + state + " yaw " + yaw + " torso must not inherit material/RT transparency.");
                            Assert.That(image.GetPixel(2, 2).a, Is.LessThan(.01f), "The environment must still show around the silhouette.");
                        }
                    }
                }
                finally { RenderTexture.active = previous; preview.Dispose(); Object.Destroy(image); }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator MissingPersonalAssetKeepsTheCurrentPreviewAndDoesNotCreateAFallback()
        {
            var preview = new AvatarPreview(320, 400, transparent: true);
            var parent = new GameObject("missing_personal_asset_world");
            try
            {
                preview.SetVisible(false); preview.SetPodium(true); preview.SetLook(Look("ubc_m"));
                yield return null;
                var actor = preview.Camera.transform.parent.GetComponentInChildren<Animator>();
                var cameraPosition = preview.Camera.transform.position;
                Assert.IsFalse(preview.TrySetLikeness("9b87bb74-10e6-4b9e-aa8c-7867df0aad1f", out var failure));
                Assert.IsFalse(string.IsNullOrEmpty(failure.detail));
                Assert.That(preview.Camera.transform.position, Is.EqualTo(cameraPosition));
                Assert.IsFalse(CTLife.World.AvatarFactory.TryCreateLikeness("missing", "9b87bb74-10e6-4b9e-aa8c-7867df0aad1f",
                    parent.transform, false, out var replacement, out _));
                Assert.IsNull(replacement);
                yield return null;
                Assert.That(preview.Camera.transform.parent.GetComponentInChildren<Animator>(), Is.SameAs(actor));
                Assert.That(parent.transform.childCount, Is.EqualTo(0), "A missing personal model must not leave a stock actor or partial clone.");
            }
            finally { preview.Dispose(); Object.Destroy(parent); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator CreatorPodiumPlantsTheFeetAndKeepsRealReactionsInsideTheViewport()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            foreach (string id in Ids)
            {
                var preview = new AvatarPreview(400, 400, transparent: true);
                try
                {
                    preview.SetVisible(false);
                    preview.SetPodium(true);
                    preview.SetLook(Look(id));
                    preview.SetYaw(-18f);
                    yield return null;
                    var root = preview.Camera.transform.parent;
                    var podium = root.Find("creator_podium");
                    Assert.IsNotNull(podium, id + " creator stage must be actual scene geometry.");
                    var stage = podium.GetComponent<MeshRenderer>();
                    var animator = root.GetComponentInChildren<Animator>();
                    var body = animator.GetComponentsInChildren<SkinnedMeshRenderer>().Single();
                    Assert.IsTrue(body.updateWhenOffscreen, "Imported bind-pose culling must not remove the moving hands.");
                    animator.Play("loco_idle", 0, 0f); animator.Update(0f);
                    float sole = WorldSurface(body).Min(p => p.y);
                    Assert.That(sole, Is.EqualTo(stage.bounds.max.y).Within(.025f), id + " soles must meet the plinth.");
                    AssertViewportContains(preview, MeshWorldSurface(podium.GetComponent<MeshFilter>()), id + " podium");
                    AssertPodiumRendered(preview, id);

                    foreach (string state in new[] { "emote_wave", "idle_fidget_stretch", "emote_moon_jump" })
                    {
                        Assert.That(preview.PlayReaction(state), Is.EqualTo(state), "Use the shipped controller state.");
                        // Sample at different phases from the production envelope, including between its samples.
                        for (int sample = 0; sample <= 17; sample++)
                        {
                            animator.Play(state, 0, sample / 17f); animator.Update(0f);
                            foreach (float yaw in new[] { -18f, 72f, 162f, 252f })
                            {
                                preview.SetYaw(yaw);
                                AssertViewportContains(preview, WorldSurface(body), id + " " + state + " " + sample);
                            }
                        }
                    }
                    preview.SetPortrait(true);
                    Assert.IsFalse(podium.gameObject.activeSelf, "The ID portrait must never contain a podium.");
                    preview.SetPortrait(false);
                    Assert.IsTrue(podium.gameObject.activeSelf);
                }
                finally { preview.Dispose(); }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator PodiumIsOptInAndDisablingItRestoresTheExistingBodyCamera()
        {
            var preview = new AvatarPreview(320, 400, transparent: true);
            Transform podium = null;
            try
            {
                preview.SetVisible(false);
                preview.SetLook(Look("ubc_m"));
                preview.SetYaw(-18f);
                yield return null;
                var root = preview.Camera.transform.parent;
                Assert.IsFalse(preview.Podium);
                Assert.IsNull(root.Find("creator_podium"), "Home and non-creator previews must not allocate a podium.");
                var position = preview.Camera.transform.position;
                var rotation = preview.Camera.transform.rotation;
                preview.SetPodium(true);
                podium = root.Find("creator_podium");
                Assert.IsNotNull(podium);
                preview.SetPodium(false);
                Assert.IsFalse(podium.gameObject.activeSelf);
                Assert.That(Vector3.Distance(position, preview.Camera.transform.position), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(rotation, preview.Camera.transform.rotation), Is.LessThan(.01f));
            }
            finally { preview.Dispose(); }
            yield return null;
            Assert.IsTrue(podium == null, "Disposal must release the stage along with its private actor.");
        }

        private static string[] Look(string id) => new[] { "skin_tone3", "head_" + id, "hair_" + id,
            "top_" + id, "bottom_" + id, "shoes_" + id, "acc_none" };

        private static IEnumerable<Vector3> MeshWorldSurface(MeshFilter mesh) =>
            mesh.sharedMesh.vertices.Select(mesh.transform.TransformPoint);

        private static void AssertViewportContains(AvatarPreview preview, IEnumerable<Vector3> points, string label)
        {
            Vector3 min = Vector3.one * float.PositiveInfinity, max = Vector3.one * float.NegativeInfinity;
            foreach (var point in points)
            {
                var pixel = preview.Camera.WorldToViewportPoint(point);
                min = Vector3.Min(min, pixel); max = Vector3.Max(max, pixel);
            }
            Assert.That(min.z, Is.GreaterThan(preview.Camera.nearClipPlane), label + " is behind the camera.");
            Assert.That(min.x, Is.GreaterThanOrEqualTo(.003f), label + " is cropped left.");
            Assert.That(max.x, Is.LessThanOrEqualTo(.997f), label + " is cropped right.");
            Assert.That(min.y, Is.GreaterThanOrEqualTo(.003f), label + " is cropped below.");
            Assert.That(max.y, Is.LessThanOrEqualTo(.997f), label + " is cropped above.");
        }

        private static void AssertPodiumRendered(AvatarPreview preview, string label)
        {
            var previous = RenderTexture.active;
            int width = preview.Texture.width, height = preview.Texture.height;
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                preview.Camera.Render();
                RenderTexture.active = preview.Texture;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); image.Apply(false);
                var pixels = image.GetPixels32();
                int gold = 0, purple = 0;
                for (int i = 0; i < width * height / 2; i++)
                {
                    var p = pixels[i];
                    if (p.a < 240) continue;
                    if (p.r > p.b * 1.3f && p.g > p.b * 1.25f) gold++;
                    if (p.r > p.g * 1.15f && p.b > p.g * 1.2f) purple++;
                }
                Assert.That(gold, Is.GreaterThan(100), label + " must render the gold top and bevel.");
                Assert.That(purple, Is.GreaterThan(30), label + " must render the purple band.");
            }
            finally { RenderTexture.active = previous; Object.Destroy(image); }
        }

        private static List<Vector3> WorldSurface(SkinnedMeshRenderer renderer)
        {
            var mesh = new Mesh();
            try
            {
                // Test against the actual animated surface, independent of the preview's private framing bounds.
                renderer.BakeMesh(mesh, true);
                return mesh.vertices.Select(renderer.transform.TransformPoint).ToList();
            }
            finally { Object.Destroy(mesh); }
        }

        private static void AssertVisiblePixels(AvatarPreview preview, string label)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(320, 400, TextureFormat.RGBA32, false);
            try
            {
                preview.Camera.Render();
                RenderTexture.active = preview.Texture;
                image.ReadPixels(new Rect(0, 0, 320, 400), 0, 0, false);
                image.Apply(false);
                Assert.That(image.GetPixels32().Count(p => p.a > 16), Is.GreaterThan(800), label + " rendered empty.");
            }
            finally { RenderTexture.active = previous; Object.Destroy(image); }
        }
    }
}
#endif
