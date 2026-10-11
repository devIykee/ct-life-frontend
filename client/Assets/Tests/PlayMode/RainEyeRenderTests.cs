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
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CTLife.Tests
{
    /// <summary>Close-up evidence separates actual eye surfaces from the head behind a missing pupil.</summary>
    public sealed class RainEyeRenderTests
    {
        [UnityTest]
        public IEnumerator RainPupilsRemainSolidAcrossClosePortraitPoses() => Review(null, true);

        [UnityTest]
        public IEnumerator CaptureActualEyesAcrossPortraitPoses()
        {
            string directory = Environment.GetEnvironmentVariable("CTLIFE_EYE_REVIEW");
            if (string.IsNullOrEmpty(directory)) Assert.Ignore("Set CTLIFE_EYE_REVIEW to an external evidence directory.");
            return Review(directory, false);
        }

        private static IEnumerator Review(string directory, bool requireSolid)
        {
            Assert.IsNull(UiRoot.Instance, "Do not run over the user's game.");
            if (directory != null) Directory.CreateDirectory(directory);
            string Evidence(string name) => directory == null ? null : Path.Combine(directory, name);
            var look = CreatorLookCatalog.Available().First(e => e.look[1] == "head_rain").look;
            var preview = new AvatarPreview(960, 640, transparent: true);
            var image = new Texture2D(960, 640, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            var meshes = new List<Mesh>();
            var report = new JArray();
            var white = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            white.SetColor("_BaseColor", Color.white); white.SetFloat("_Cull", 0); white.SetFloat("_Surface", 0); white.SetFloat("_ZWrite", 1);
            try
            {
                preview.SetVisible(false); preview.SetLook(look); preview.SetPortrait(true);
                var avatar = preview.Camera.transform.parent.GetComponentInChildren<AvatarView>(); avatar.enabled = false;
                var animator = avatar.Animator; AnimatorUtility.DeoptimizeTransformHierarchy(animator.gameObject); animator.speed = 0;
                var left = animator.GetBoneTransform(HumanBodyBones.LeftEye); var right = animator.GetBoneTransform(HumanBodyBones.RightEye);
                Assert.IsNotNull(left); Assert.IsNotNull(right);
                var skins = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
                var uvReference = JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "Tests/PlayMode/Fixtures/RainEyeUvReference.json")))["uv"]
                    .Select(v => new Vector2((float)v[0], (float)v[1])).ToArray();
                var eyeMeshes = new Mesh[skins.Length]; var eyeRenderers = new MeshRenderer[skins.Length];
                var eyeTriangles = new int[skins.Length][];
                int totalEyeTriangles = 0;
                for (int s = 0; s < skins.Length; s++)
                {
                    var skin = skins[s]; var baked = new Mesh(); meshes.Add(baked);
                    skin.BakeMesh(baked, true);
                    var uv = baked.uv;
                    // The cast atlas has non-overlapping islands with padding. Keep exact eye-owned UV vertices,
                    // allowing the documented FBX import's UV quantization; no region of the face is guessed by color.
                    bool Eye(int index) => uvReference.Any(reference => (uv[index] - reference).sqrMagnitude < .000001f);
                    var triangles = baked.triangles; var selected = new List<int>();
                    for (int i = 0; i < triangles.Length; i += 3)
                        if (Eye(triangles[i]) && Eye(triangles[i + 1]) && Eye(triangles[i + 2]))
                        { selected.Add(triangles[i]); selected.Add(triangles[i + 1]); selected.Add(triangles[i + 2]); }
                    totalEyeTriangles += selected.Count / 3;
                    eyeTriangles[s] = selected.ToArray(); eyeMeshes[s] = baked;
                    var go = new GameObject("rain_eye_surface_diagnostic"); go.layer = skin.gameObject.layer;
                    go.transform.SetParent(skin.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = baked;
                    var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = white; renderer.enabled = false;
                    eyeRenderers[s] = renderer;
                }
                Assert.That(totalEyeTriangles, Is.InRange(450, 470), "The exact Rain eye surface must be identified; the original export has 455 triangles.");
                foreach (string state in new[] { "loco_idle", "idle_fidget_foldarms", "idle_fidget_stretch", "emote_wave" })
                foreach (float phase in new[] { .3f, .52f })
                foreach (float yaw in new[] { -30f, 0f, 30f })
                {
                    preview.SetYaw(yaw); animator.Play(state, 0, phase); animator.Update(0); yield return null;
                    Vector3 centre = (left.position + right.position) * .5f;
                    preview.Camera.transform.position = centre + Vector3.forward * .38f;
                    preview.Camera.transform.LookAt(centre, Vector3.up);
                    string name = state + "-" + phase.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "-" + yaw;
                    Capture(preview, image, Evidence(name + "-actual.png"));
                    for (int s = 0; s < skins.Length; s++)
                    {
                        skins[s].BakeMesh(eyeMeshes[s], true); eyeMeshes[s].subMeshCount = 1;
                        eyeMeshes[s].SetTriangles(eyeTriangles[s], 0, false);
                        skins[s].enabled = false; eyeRenderers[s].enabled = true;
                    }
                    var pixels = Capture(preview, image, Evidence(name + "-eye-geometry.png"));
                    int holes = InteriorTransparentPixels(pixels, image.width, image.height);
                    int partlyTransparent = pixels.Count(p => p.a > 4 && p.a < 250);
                    Assert.That(pixels.Count(p => p.a > 250), Is.GreaterThan(15000), "Both real eye surfaces must be in the close camera.");
                    if (requireSolid) Assert.That(holes, Is.LessThanOrEqualTo(2), name + ": the pupil must cover the iris opening; whole-face alpha can conceal a missing pupil behind opaque head geometry.");
                    report.Add(new JObject { ["capture"] = name, ["eyeTriangles"] = totalEyeTriangles,
                        ["enclosedMissingEyePixels"] = holes, ["partlyTransparentPixels"] = partlyTransparent,
                        ["leftEyeLocalRotation"] = left.localRotation.ToString("F5"), ["rightEyeLocalRotation"] = right.localRotation.ToString("F5") });
                    for (int s = 0; s < skins.Length; s++) { skins[s].enabled = true; eyeRenderers[s].enabled = false; }
                    yield return null;
                }
                if (directory != null) File.WriteAllText(Path.Combine(directory, "eye-measurements.json"), report.ToString());
            }
            finally
            {
                RenderTexture.active = previous; preview.Dispose(); Object.Destroy(image); Object.Destroy(white);
                foreach (var mesh in meshes) Object.Destroy(mesh);
            }
        }

        private static Color32[] Capture(AvatarPreview preview, Texture2D image, string path)
        {
            var previous = RenderTexture.active;
            try
            {
                preview.Camera.Render(); RenderTexture.active = preview.Texture;
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0, false); image.Apply(false);
                if (path != null) File.WriteAllBytes(path, image.EncodeToPNG()); return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; }
        }

        private static int InteriorTransparentPixels(Color32[] pixels, int width, int height)
        {
            // Flood the exterior. Transparent components surrounded by actual eye triangles are openings,
            // even when an opaque face behind them would hide the hole in a whole-character alpha test.
            var visited = new bool[pixels.Length]; var queue = new Queue<int>();
            void Visit(int i) { if (!visited[i] && pixels[i].a < 5) { visited[i] = true; queue.Enqueue(i); } }
            for (int x = 0; x < width; x++) { Visit(x); Visit((height - 1) * width + x); }
            for (int y = 0; y < height; y++) { Visit(y * width); Visit(y * width + width - 1); }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % width, y = i / width;
                if (x > 0) Visit(i - 1); if (x + 1 < width) Visit(i + 1);
                if (y > 0) Visit(i - width); if (y + 1 < height) Visit(i + width);
            }
            int holes = 0; for (int i = 0; i < pixels.Length; i++) if (!visited[i] && pixels[i].a < 5) holes++;
            return holes;
        }
    }
}
#endif
