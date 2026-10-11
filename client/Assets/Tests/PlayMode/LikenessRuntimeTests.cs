#if UNITY_INCLUDE_TESTS && UNITY_EDITOR && !CTLIFE_PROD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.App;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.World;
using CTLife.World.Likeness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CTLife.Tests
{
    public sealed class LikenessRuntimeTests
    {
        private const string DefaultFixture = "/Users/mac/Documents/codes/projects/ct-life-evidence/likeness-trial-20261011/synthetic-human1-mixamo/rigged-unapproved.glb";
        [UnityTest] public IEnumerator PersonalReviewFramesAnimatedHeadAndFeetAfterInitialPoseSettles()
        {
            Assert.IsNull(Object.FindAnyObjectByType<FlowController>(), "Use an empty PlayMode test scene, not the active game.");
            string path = Environment.GetEnvironmentVariable("CTLIFE_LIKENESS_FIXTURE") ?? DefaultFixture;
            if (!File.Exists(path)) Assert.Ignore("Set CTLIFE_LIKENESS_FIXTURE to the approved-budget Mixamo trial GLB.");
            byte[] bytes = File.ReadAllBytes(path);
            var preview = new LikenessModelPreview();
            LikenessLoadedAvatar loaded = null;
            try
            {
                // Match the phone flow: import directly below the isolated, translated review root.
                var task = LikenessAvatarLoader.Load(bytes, FixtureManifest(bytes), preview.Parent);
                while (!task.IsCompleted) yield return null;
                if (task.IsFaulted) Assert.Fail(task.Exception.ToString());
                loaded = task.Result;
                preview.Present(loaded);
                var camera = (Camera)typeof(LikenessModelPreview).GetField("_camera",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(preview);
                for (int frame = 0; frame < 3; frame++)
                {
                    yield return null;
                    camera.Render();
                }
                AssertReviewMeshFits(loaded, camera, "settled idle");
                preview.Spin(300f); // A quarter-turn also needs its actual posed geometry framed.
                yield return null;
                camera.Render();
                AssertReviewMeshFits(loaded, camera, "quarter-turn");
            }
            finally
            {
                // The panel owns imported mesh/material resources; the preview owns only its scene/camera.
                loaded?.Dispose();
                preview.Dispose();
            }
            yield return null;
        }
        private static void AssertReviewMeshFits(LikenessLoadedAvatar loaded, Camera camera, string phase)
        {
            bool found = false;
            var bounds = new Bounds();
            var baked = new Mesh();
            try
            {
                foreach (var skin in loaded.Instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skin.BakeMesh(baked, true);
                    foreach (var vertex in baked.vertices)
                    {
                        var world = skin.transform.TransformPoint(vertex);
                        Assert.That(float.IsFinite(world.x) && float.IsFinite(world.y) && float.IsFinite(world.z), Is.True, phase);
                        if (!found) { bounds = new Bounds(world, Vector3.zero); found = true; }
                        else bounds.Encapsulate(world);
                    }
                    baked.Clear();
                }
                Assert.IsTrue(found, "Measure the real imported skin, not a placeholder or cached rest-pose bound.");
                Assert.That(bounds.size.y, Is.InRange(1.3f, 2.1f), phase + " remains human-sized");
                foreach (float x in new[] { bounds.min.x, bounds.max.x })
                    foreach (float y in new[] { bounds.min.y, bounds.max.y })
                        foreach (float z in new[] { bounds.min.z, bounds.max.z })
                        {
                            Vector3 viewport = camera.WorldToViewportPoint(new Vector3(x, y, z));
                            Assert.That(viewport.x, Is.InRange(.06f, .94f), phase + " horizontal crop");
                            Assert.That(viewport.y, Is.InRange(.06f, .94f), phase + " head/feet crop");
                            Assert.That(viewport.z, Is.InRange(camera.nearClipPlane, camera.farClipPlane), phase + " depth crop");
                        }
            }
            finally { Object.Destroy(baked); }
        }
        [UnityTest] public IEnumerator CorrectiveRigLoadsRetargetsAndSpawnsWithoutStockReplacement()
        {
            Assert.IsNull(Object.FindAnyObjectByType<FlowController>(), "Use an empty PlayMode test scene, not the active game.");
            string path = Environment.GetEnvironmentVariable("CTLIFE_LIKENESS_FIXTURE") ?? DefaultFixture;
            if (!File.Exists(path)) Assert.Ignore("Set CTLIFE_LIKENESS_FIXTURE to the approved-budget Mixamo trial GLB.");
            byte[] bytes = File.ReadAllBytes(path);
            var manifest = FixtureManifest(bytes);
            var task = LikenessAvatarLoader.Load(bytes, manifest);
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) Assert.Fail(task.Exception.ToString());
            var loaded = task.Result;
            try
            {
                Assert.IsTrue(loaded.CanEquip);
                Assert.That(loaded.Bounds.size.y, Is.InRange(1.65f, 1.85f));
                var body = loaded.Instance.GetComponentsInChildren<SkinnedMeshRenderer>().Single();
                Assert.That(body.sharedMesh.triangles.Length / 3, Is.EqualTo(8286));
                Assert.That(body.sharedMaterial.mainTexture, Is.Not.Null);
                var animator = loaded.Animator;
                Assert.IsNotNull(animator.GetBoneTransform(HumanBodyBones.Head));
                Vector3[] idle = null, wave = null;
                foreach (var state in new[] { "loco_idle", "loco_walk", "loco_run", "emote_wave" })
                {
                    Assert.IsTrue(animator.HasState(0, Animator.StringToHash(state)), state);
                    animator.Play(state, 0, 0.35f); animator.Update(0);
                    yield return null;
                    var mesh = new Mesh();
                    try
                    {
                        body.BakeMesh(mesh, true);
                        var vertices = mesh.vertices.Select(body.transform.TransformPoint).ToArray();
                        Assert.That(vertices.Max(v => v.y) - vertices.Min(v => v.y), Is.InRange(1.3f, 2.1f), state + " must stay human-sized");
                        Assert.That(vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), Is.True);
                        if (state == "loco_idle") idle = vertices;
                        if (state == "emote_wave") wave = vertices;
                    }
                    finally { Object.Destroy(mesh); }
                }
                Assert.That(idle.Zip(wave, Vector3.Distance).Max(), Is.GreaterThan(.05f), "The real skin must deform under the game's Humanoid clips.");
            }
            finally { loaded.Dispose(); }
            yield return null;

            Services.TryGet<Session>(out var prior);
            var session = new Session();
            typeof(Session).GetProperty("UserId").SetValue(session, "likeness-runtime-qa");
            Services.Register(session);
            var prepare = LikenessAvatarCache.PrepareAccepted(manifest, bytes);
            while (!prepare.IsCompleted) yield return null;
            if (prepare.IsFaulted) Assert.Fail(prepare.Exception.ToString());
            AvatarView actor = null; AvatarPreview preview = null; DashboardScreen dashboard = null;
            try
            {
                Assert.IsTrue(AvatarFactory.TryCreateLikeness("runtime-human", manifest.jobId, null, false, out actor, out var error), error);
                Assert.That(actor.Animator.transform.parent, Is.EqualTo(actor.transform));
                Assert.That(actor.GetComponentsInChildren<SkinnedMeshRenderer>().Single().sharedMesh.triangles.Length / 3, Is.EqualTo(8286));
                preview = new AvatarPreview(320, 400, transparent: true);
                Assert.IsTrue(preview.TrySetLikeness(manifest.jobId, out var failure), failure.ToString());
                preview.Camera.Render();
                var old = RenderTexture.active; var image = new Texture2D(320, 400, TextureFormat.RGBA32, false);
                try
                {
                    RenderTexture.active = preview.Texture; image.ReadPixels(new Rect(0, 0, 320, 400), 0, 0); image.Apply();
                    Assert.That(image.GetPixels32().Count(p => p.a > 128), Is.GreaterThan(3000), "The accepted mesh must actually render.");
                }
                finally { RenderTexture.active = old; Object.Destroy(image); }

                // Exercise the real home selection path, including a change with the same stock fallback look.
                // The dashboard used to ignore the equipped job and always rebuild that stock body.
                const System.Reflection.BindingFlags privateInstance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                dashboard = new DashboardScreen(null, null);
                typeof(DashboardScreen).GetMethod("BuildStage", privateInstance).Invoke(dashboard, null);
                var homePreview = (AvatarPreview)typeof(DashboardScreen).GetField("_preview", privateInstance).GetValue(dashboard);
                var loadHome = typeof(DashboardScreen).GetMethod("LoadCharacter", privateInstance);
                var profile = new PlayerProfile(null);
                loadHome.Invoke(dashboard, new object[] { profile });
                var stockActor = (AvatarView)typeof(AvatarPreview).GetField("_avatar", privateInstance).GetValue(homePreview);
                Assert.IsNotNull(stockActor);
                typeof(PlayerProfile).GetProperty("LikenessJobId").SetValue(profile, manifest.jobId);
                loadHome.Invoke(dashboard, new object[] { profile });
                var homeActor = (AvatarView)typeof(AvatarPreview).GetField("_avatar", privateInstance).GetValue(homePreview);
                Assert.AreNotSame(stockActor, homeActor, "A personal job changes the home appearance even when its stock fallback does not change.");
                Assert.That(homeActor.GetComponentsInChildren<SkinnedMeshRenderer>().Single().sharedMesh.triangles.Length / 3, Is.EqualTo(8286));
                Assert.That(homeActor.Animator.GetBoneTransform(HumanBodyBones.Head).name, Is.EqualTo("mixamorig:Head"));
                var homeAnimator = homeActor.Animator;
                Vector3 footForward = homeAnimator.GetBoneTransform(HumanBodyBones.LeftToes).position -
                    homeAnimator.GetBoneTransform(HumanBodyBones.LeftFoot).position +
                    homeAnimator.GetBoneTransform(HumanBodyBones.RightToes).position -
                    homeAnimator.GetBoneTransform(HumanBodyBones.RightFoot).position;
                var towardCamera = Vector3.ProjectOnPlane(homePreview.Camera.transform.position - homeActor.transform.position, Vector3.up).normalized;
                Assert.That(Vector3.Dot(Vector3.ProjectOnPlane(footForward, Vector3.up).normalized, towardCamera), Is.GreaterThan(.7f),
                    "The real rig must face the home preview camera, not show the back of the accepted character.");
                loadHome.Invoke(dashboard, new object[] { profile });
                Assert.AreSame(homeActor, typeof(AvatarPreview).GetField("_avatar", privateInstance).GetValue(homePreview), "Unchanged profile refreshes retain the equipped mesh.");
                ClearFixtureOwner(session);
                Assert.IsFalse(LikenessAvatarCache.IsPrepared(manifest.jobId), "Changing account revokes cache lookup.");
                Assert.IsNotNull(actor.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh, "Live actor lease keeps its resources until destruction.");
            }
            finally
            {
                dashboard?.Close(); preview?.Dispose(); if (actor != null) Object.Destroy(actor.gameObject);
                ClearFixtureOwner(session); if (prior != null) Services.Register(prior); else Services.Unregister<Session>();
            }
            yield return null;
        }
        private static void ClearFixtureOwner(Session session)
        {
            typeof(Session).GetProperty("UserId").SetValue(session, null);
            var changed = (Action)typeof(Session).GetField("Changed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(session);
            changed?.Invoke();
        }
        public static LikenessAssetManifest FixtureManifest(byte[] bytes)
        {
            var bones = new Dictionary<string, string> { ["Hips"] = "mixamorig:Hips", ["Spine"] = "mixamorig:Spine", ["Chest"] = "mixamorig:Spine1", ["UpperChest"] = "mixamorig:Spine2", ["Neck"] = "mixamorig:Neck", ["Head"] = "mixamorig:Head" };
            foreach (var side in new[] { "Left", "Right" })
                foreach (var pair in new[] { ("Shoulder", "Shoulder"), ("UpperArm", "Arm"), ("LowerArm", "ForeArm"), ("Hand", "Hand"), ("UpperLeg", "UpLeg"), ("LowerLeg", "Leg"), ("Foot", "Foot"), ("Toes", "ToeBase") })
                    bones[side + pair.Item1] = "mixamorig:" + side + pair.Item2;
            return new LikenessAssetManifest { jobId = "eeeeeeee-1111-4111-8111-eeeeeeeeeeee", modelId = "head_snow", bytes = bytes.Length, sha256 = LikenessClient.Sha256(bytes), humanoidBones = bones, heightMeters = 1.75f, triangles = 8286, joints = 23, validatorVersion = "editor-runtime-fixture-v1" };
        }
    }
}
#endif
