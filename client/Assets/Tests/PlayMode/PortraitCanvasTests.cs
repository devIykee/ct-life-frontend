#if UNITY_INCLUDE_TESTS && UNITY_EDITOR && !CTLIFE_PROD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CTLife.UI;
using CTLife.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CTLife.Tests
{
    /// <summary>Project the actual animated surfaces through both cameras; preserve the chosen close-up scale.</summary>
    public sealed class PortraitCanvasTests
    {
        [UnityTest]
        public IEnumerator ExpandedCanvasPreservesThePortraitAndRendersPastItsOldEdges()
        {
            Assert.IsNull(UiRoot.Instance);
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            var canvas = new Vector2(2400, 1080);
            var window = new Rect(-368, 112, 1120, 1120);
            int totalVisibleBeyondOldWindow = 0;
            foreach (string id in new[] { "rain", "ubc_m", "pm_shark" })
            {
                var look = CreatorLookCatalog.Available().First(entry => entry.look[1] == "head_" + id).look;
                var preview = new AvatarPreview(512, 512, transparent: true);
                Texture2D image = null;
                var squareImage = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                var previousTarget = RenderTexture.active;
                try
                {
                    preview.SetVisible(false); preview.SetPodium(true); preview.SetLook(look); preview.SetYaw(-18f);
                    preview.SetPortrait(true);
                    var texture = preview.Texture;
                    var avatar = preview.Camera.transform.parent.GetComponentInChildren<AvatarView>(); avatar.enabled = false;
                    var animator = avatar.Animator; animator.speed = 0;
                    AnimatorUtility.DeoptimizeTransformHierarchy(animator.gameObject);
                    var head = animator.GetBoneTransform(HumanBodyBones.Head);
                    var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                    Assert.IsNotNull(head, id); Assert.IsNotNull(neck, id);
                    foreach (string state in new[] { "idle_fidget_stretch", "emote_wave" })
                    foreach (float phase in new[] { .15f, .3f, .52f, .72f })
                    {
                        preview.SetPortraitCanvas(Vector2.zero, default);
                        animator.Play(state, 0, phase); animator.Update(0); yield return null;
                        var surface = WorldSurface(avatar);
                        Assert.That(surface.Count, Is.GreaterThan(1000), id + " must use its actual skinned mesh.");
                        var original = surface.Select(point => IntoWindow(preview.Camera.WorldToViewportPoint(point), window)).ToArray();
                        Vector2 originalHead = IntoWindow(preview.Camera.WorldToViewportPoint(head.position), window);
                        Vector2 originalNeck = IntoWindow(preview.Camera.WorldToViewportPoint(neck.position), window);
                        Color32[] squarePixels = null;
                        if (state == "emote_wave" && Mathf.Abs(phase - .3f) < .001f)
                        {
                            preview.Camera.Render(); RenderTexture.active = preview.Texture;
                            squareImage.ReadPixels(new Rect(0, 0, 512, 512), 0, 0, false); squareImage.Apply(false);
                            squarePixels = squareImage.GetPixels32();
                        }
                        preview.SetPortraitCanvas(canvas, window);
                        Assert.That(preview.Texture, Is.SameAs(texture), "UI bindings must survive expansion.");
                        Assert.That(preview.Texture.width, Is.LessThanOrEqualTo(1536), "The full-screen image must retain the bounded mobile render target.");
                        float maximumVertexError = 0f;
                        for (int i = 0; i < surface.Count; i++)
                        {
                            Vector2 expanded = IntoCanvas(preview.Camera.WorldToViewportPoint(surface[i]), canvas);
                            maximumVertexError = Mathf.Max(maximumVertexError, Vector2.Distance(original[i], expanded));
                        }
                        // The preview is deliberately far from the city (1000m+). Single-precision view/projection
                        // multiplication can differ by a fraction of a UI pixel after the shifted frustum transform.
                        // Report the observed maximum over EVERY vertex and require less than one screen pixel.
                        Assert.That(maximumVertexError, Is.LessThan(1f), id + " " + state + " moved the actual surface by a visible UI pixel.");
                        float expandedFaceHeight = Vector2.Distance(IntoCanvas(preview.Camera.WorldToViewportPoint(head.position), canvas),
                            IntoCanvas(preview.Camera.WorldToViewportPoint(neck.position), canvas));
                        float faceHeightError = Mathf.Abs(expandedFaceHeight - Vector2.Distance(originalHead, originalNeck));
                        Assert.That(faceHeightError, Is.LessThan(1f),
                            "Giving a hand more canvas must not shrink the chosen face.");
                        Debug.Log($"CTLIFE [portrait-canvas] {id} {state} {phase:F2}: {surface.Count} vertices, max UI error {maximumVertexError:F5}px; face height error {faceHeightError:F5}px");

                        preview.Camera.Render(); RenderTexture.active = preview.Texture;
                        if (image == null) image = new Texture2D(preview.Texture.width, preview.Texture.height, TextureFormat.RGBA32, false);
                        image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0, false); image.Apply(false);
                        var pixels = image.GetPixels32();
                        if (squarePixels != null)
                        {
                            int sampled = 0, missing = 0;
                            for (int sy = 2; sy < 510; sy += 3)
                            for (int sx = 2; sx < 510; sx += 3)
                            {
                                int index = sy * 512 + sx;
                                // Ignore silhouette edges, where the two render resolutions sample different coverage.
                                if (new[] { index, index - 2, index + 2, index - 1024, index + 1024 }.Any(i => squarePixels[i].a < 250)) continue;
                                Vector2 screen = IntoWindow(new Vector3((sx + .5f) / 512f, (sy + .5f) / 512f, 0f), window);
                                if (screen.x < 1 || screen.y < 1 || screen.x >= canvas.x - 1 || screen.y >= canvas.y - 1) continue;
                                int tx = Mathf.Clamp(Mathf.RoundToInt(screen.x / canvas.x * image.width - .5f), 0, image.width - 1);
                                int ty = Mathf.Clamp(Mathf.RoundToInt((1 - screen.y / canvas.y) * image.height - .5f), 0, image.height - 1);
                                sampled++; if (pixels[ty * image.width + tx].a < 250) missing++;
                            }
                            Debug.Log($"CTLIFE [portrait-canvas-pixels] {id}: {sampled} original interior pixels projected into canvas; {missing} missing");
                            Assert.That(sampled, Is.GreaterThan(200), "Compare actual rendered character pixels, not an empty image.");
                            Assert.That(missing, Is.LessThanOrEqualTo(Mathf.Max(2, sampled * .003f)),
                                "The expanded render target must retain original opaque interior pixels at their original screen positions.");
                        }
                        for (int y = 0; y < image.height; y++)
                        for (int x = 0; x < image.width; x++)
                        {
                            if (pixels[y * image.width + x].a < 250) continue;
                            Vector2 screen = new Vector2((x + .5f) / image.width * canvas.x, (1f - (y + .5f) / image.height) * canvas.y);
                            if (!window.Contains(screen)) totalVisibleBeyondOldWindow++;
                        }
                        Assert.IsFalse(preview.Camera.transform.parent.Find("creator_podium").gameObject.activeSelf,
                            "A question portrait never renders the podium.");
                        preview.SetPortraitCanvas(Vector2.zero, default);
                        Assert.That(preview.Texture.width, Is.EqualTo(512)); Assert.That(preview.Texture.height, Is.EqualTo(512));
                        for (int i = 0; i < surface.Count; i += 37)
                            Assert.That(Vector2.Distance(original[i], IntoWindow(preview.Camera.WorldToViewportPoint(surface[i]), window)),
                                Is.LessThan(1f), "Returning to a normal portrait must restore its projection.");
                    }
                }
                finally
                {
                    RenderTexture.active = previousTarget; preview.Dispose(); if (image != null) Object.Destroy(image);
                    Object.Destroy(squareImage);
                }
                yield return null;
            }
            Assert.That(totalVisibleBeyondOldWindow, Is.GreaterThan(100),
                "The actual raised/waving meshes must paint outside the former square, not merely change projection numbers.");
        }

        private static Vector2 IntoWindow(Vector3 viewport, Rect window) =>
            new Vector2(window.x + viewport.x * window.width, window.y + (1f - viewport.y) * window.height);
        private static Vector2 IntoCanvas(Vector3 viewport, Vector2 size) => new Vector2(viewport.x * size.x, (1f - viewport.y) * size.y);

        private static List<Vector3> WorldSurface(AvatarView avatar)
        {
            var result = new List<Vector3>(); var baked = new Mesh();
            try
            {
                foreach (var skin in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                    skin.BakeMesh(baked, true);
                    result.AddRange(baked.vertices.Select(skin.transform.TransformPoint));
                }
                return result;
            }
            finally { Object.Destroy(baked); }
        }
    }
}
#endif
