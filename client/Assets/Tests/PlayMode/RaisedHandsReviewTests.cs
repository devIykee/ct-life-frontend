#if UNITY_INCLUDE_TESTS && UNITY_EDITOR && !CTLIFE_PROD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.UI;
using CTLife.World;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CTLife.Tests
{
    /// <summary>Opt-in evidence capture against the actual shipped rigs, with no persistent source mutations.</summary>
    public sealed class RaisedHandsReviewTests
    {
        [UnityTest]
        public IEnumerator RaisedHandsRetainTheAuthoredFingerPoseOnAllFourHumanRigs()
        {
            Assert.IsNull(UiRoot.Instance);
            foreach (string id in new[] { "snow", "rain", "ubc_m", "ubc_f" })
            {
                var look = CreatorLookCatalog.Available().First(entry => entry.look[1] == "head_" + id).look;
                var preview = new AvatarPreview(400, 400, transparent: true);
                try
                {
                    preview.SetVisible(false); preview.SetLook(look);
                    var avatar = preview.Camera.transform.parent.GetComponentInChildren<AvatarView>(); avatar.enabled = false;
                    var animator = avatar.Animator;
                    AnimatorUtility.DeoptimizeTransformHierarchy(animator.gameObject); animator.speed = 0;
                    animator.Play("loco_idle", 0, .3f); animator.Update(0); yield return null;
                    var idle = new HumanPose(); var stretch = new HumanPose();
                    using (var handler = new HumanPoseHandler(animator.avatar, animator.transform))
                    {
                        handler.GetHumanPose(ref idle);
                        animator.Play("idle_fidget_stretch", 0, .52f); animator.Update(0); yield return null;
                        handler.GetHumanPose(ref stretch);
                    }
                    float head = animator.GetBoneTransform(HumanBodyBones.Head).position.y;
                    Assert.That(animator.GetBoneTransform(HumanBodyBones.LeftHand).position.y, Is.GreaterThan(head + .03f), id + " left arm must actually be raised.");
                    Assert.That(animator.GetBoneTransform(HumanBodyBones.RightHand).position.y, Is.GreaterThan(head + .03f), id + " right arm must actually be raised.");
                    int compared = 0;
                    int deliberatelyPosed = 0;
                    for (int i = 0; i < HumanTrait.MuscleCount; i++)
                    {
                        string name = HumanTrait.MuscleName[i];
                        if (!new[] { "Thumb", "Index", "Middle", "Ring", "Little" }.Any(name.Contains)) continue;
                        Assert.That(stretch.muscles[i], Is.EqualTo(idle.muscles[i]).Within(.015f), id + " " + name + " must not reset to the legacy default while raised.");
                        if (Mathf.Abs(idle.muscles[i]) > .025f) deliberatelyPosed++;
                        compared++;
                    }
                    Assert.That(compared, Is.EqualTo(40));
                    Assert.That(deliberatelyPosed, Is.GreaterThan(30), "An unanimated zero/default hand cannot satisfy the pose regression.");
                }
                finally { preview.Dispose(); }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator CaptureRaisedHandsAndSurfaceCoverage()
        {
            string directory = Environment.GetEnvironmentVariable("CTLIFE_HAND_REVIEW");
            if (string.IsNullOrEmpty(directory)) Assert.Ignore("Set CTLIFE_HAND_REVIEW to an external evidence directory.");
            Assert.IsNull(UiRoot.Instance, "Do not run over the user's game.");
            Directory.CreateDirectory(directory);
            var report = new JArray();
            foreach (string id in new[] { "snow", "rain", "ubc_m", "ubc_f" })
            {
                var look = CreatorLookCatalog.Available().First(e => e.look[1] == "head_" + id).look;
                var preview = new AvatarPreview(1000, 1000, transparent: true);
                var image = new Texture2D(1000, 1000, TextureFormat.RGBA32, false);
                var previous = RenderTexture.active;
                var ownedMaterials = new List<Material>();
                AnimatorOverrideController originalMotion = null;
                try
                {
                    preview.SetVisible(false); preview.SetPodium(true); preview.SetLook(look);
                    yield return null;
                    var avatar = preview.Camera.transform.parent.GetComponentInChildren<AvatarView>();
                    avatar.enabled = false;
                    var animator = avatar.Animator;
                    if (Environment.GetEnvironmentVariable("CTLIFE_HAND_REVIEW_ORIGINAL") == "1")
                    {
                        // The same before-state can be rendered again even after generated clips are installed.
                        var controller = animator.runtimeAnimatorController as AnimatorOverrideController;
                        originalMotion = new AnimatorOverrideController(controller != null ? controller.runtimeAnimatorController : animator.runtimeAnimatorController);
                        var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                        if (controller != null) controller.GetOverrides(clips); else originalMotion.GetOverrides(clips);
                        var originalStretch = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/animations/idle_fidget_stretch.fbx")
                            .OfType<AnimationClip>().Single(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
                        originalMotion.ApplyOverrides(clips.Select(pair => new KeyValuePair<AnimationClip, AnimationClip>(pair.Key,
                            pair.Key.name == "idle_fidget_stretch" || pair.Key.name == "Snow_RelaxedHandsStretch" ? originalStretch : pair.Value)).ToList());
                        animator.runtimeAnimatorController = originalMotion; animator.Rebind();
                    }
                    AnimatorUtility.DeoptimizeTransformHierarchy(animator.gameObject);
                    animator.speed = 0;
                    foreach (string state in new[] { "loco_idle", "idle_fidget_stretch", "emote_wave" })
                    {
                        preview.PlayReaction(state);
                        foreach (float phase in state == "loco_idle" ? new[] { .3f } : new[] { .28f, .52f, .72f })
                        {
                            animator.Play(state, 0, phase); animator.Update(0);
                            // Camera.Render uses the renderer's uploaded skin matrices, not HumanPose's immediate
                            // CPU pose. Let one frame upload the manually sampled pose before recording evidence.
                            yield return null;
                            var pose = new HumanPose();
                            using (var handler = new HumanPoseHandler(animator.avatar, animator.transform)) handler.GetHumanPose(ref pose);
                            var muscles = new JObject();
                            for (int i = 0; i < HumanTrait.MuscleCount; i++)
                                if (HumanTrait.MuscleName[i].Contains("Finger") || HumanTrait.MuscleName[i].Contains("Thumb") ||
                                    HumanTrait.MuscleName[i].Contains("Index") || HumanTrait.MuscleName[i].Contains("Middle") ||
                                    HumanTrait.MuscleName[i].Contains("Ring") || HumanTrait.MuscleName[i].Contains("Little") ||
                                    HumanTrait.MuscleName[i].Contains("Hand")) muscles[HumanTrait.MuscleName[i]] = pose.muscles[i];
                            foreach (float yaw in new[] { -18f, 72f, 162f })
                            {
                                preview.SetYaw(yaw);
                                string name = id + "-" + state + "-" + phase.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "-" + yaw;
                                var original = Capture(preview, image, Path.Combine(directory, name + ".png"));
                                if (yaw == -18f && (state == "loco_idle" || Mathf.Abs(phase - .52f) < .001f))
                                {
                                    CaptureHand(preview, animator, image, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleDistal,
                                        Path.Combine(directory, name + "-left-hand.png"));
                                    CaptureHand(preview, animator, image, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleDistal,
                                        Path.Combine(directory, name + "-right-hand.png"));
                                }
                                var skins = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
                                var materials = skins.Select(s => s.sharedMaterials).ToArray();
                                for (int i = 0; i < skins.Length; i++)
                                    skins[i].sharedMaterials = materials[i].Select(source =>
                                    {
                                        // Compare the exact mesh with opaque, two-sided URP shading. The saved source is never edited.
                                        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                                        ownedMaterials.Add(m); m.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                                        m.SetFloat("_Cull", 0); m.SetFloat("_Surface", 0); m.SetFloat("_ZWrite", 1);
                                        return m;
                                    }).ToArray();
                                var twoSided = Capture(preview, image, Path.Combine(directory, name + "-two-sided.png"));
                                int filledByBackfaces = 0, partlyTransparent = 0;
                                for (int i = 0; i < original.Length; i++)
                                {
                                    if (original[i].a == 0 && twoSided[i].a > 250) filledByBackfaces++;
                                    if (original[i].a > 4 && original[i].a < 250) partlyTransparent++;
                                }
                                for (int i = 0; i < skins.Length; i++) skins[i].sharedMaterials = materials[i];
                                if (id == "rain" && Environment.GetEnvironmentVariable("CTLIFE_HAND_REVIEW_ORIGINAL") != "1")
                                    Assert.That(filledByBackfaces, Is.LessThanOrEqualTo(5), name + ": Rain's actual garment must cover the same silhouette as the opaque two-sided reference.");
                                report.Add(new JObject { ["capture"] = name, ["holesFilledByBackfaces"] = filledByBackfaces,
                                    ["partlyTransparentPixels"] = partlyTransparent, ["muscles"] = muscles.DeepClone() });
                            }
                        }
                    }
                }
                finally
                {
                    RenderTexture.active = previous; preview.Dispose(); Object.Destroy(image);
                    foreach (var material in ownedMaterials) Object.Destroy(material);
                    if (originalMotion != null) Object.Destroy(originalMotion);
                }
                yield return null;
            }
            File.WriteAllText(Path.Combine(directory, "measurements.json"), report.ToString());
        }

        private static Color32[] Capture(AvatarPreview preview, Texture2D image, string path)
        {
            preview.Camera.Render(); RenderTexture.active = preview.Texture;
            image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0, false); image.Apply(false);
            File.WriteAllBytes(path, image.EncodeToPNG()); return image.GetPixels32();
        }

        private static void CaptureHand(AvatarPreview preview, Animator animator, Texture2D image,
            HumanBodyBones wristBone, HumanBodyBones tipBone, string path)
        {
            var wrist = animator.GetBoneTransform(wristBone); var tip = animator.GetBoneTransform(tipBone);
            Assert.IsNotNull(wrist); Assert.IsNotNull(tip);
            var camera = preview.Camera; var position = camera.transform.position; var rotation = camera.transform.rotation;
            try
            {
                Vector3 centre = (wrist.position + tip.position) * .5f;
                Vector3 viewDirection = (position - centre).normalized;
                camera.transform.position = centre + viewDirection * .60f;
                camera.transform.LookAt(centre);
                Capture(preview, image, path);
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); }
        }
    }
}
#endif
