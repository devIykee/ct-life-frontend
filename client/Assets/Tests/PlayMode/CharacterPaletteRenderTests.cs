#if UNITY_INCLUDE_TESTS && UNITY_EDITOR && !CTLIFE_PROD
using System.Collections;
using System.Linq;
using CTLife.App;
using CTLife.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace CTLife.Tests
{
    public sealed class CharacterPaletteRenderTests
    {
        [SetUp] public void RequireEmptyEditorScene()
        {
            Assert.IsTrue(Application.isPlaying);
            Assert.IsNull(Object.FindAnyObjectByType<FlowController>(), "Do not run over the user's game.");
        }

        [UnityTest] public IEnumerator PreviewAndWorldUseTheSameSavedPaletteWithoutMutatingTheirSource()
        {
            var catalog = CharacterPaletteCatalog.Load(); Assert.IsNotNull(catalog);
            foreach (string id in new[] { "head_ubc_m", "head_ubc_f", "head_snow", "head_rain" })
            {
                var binding = catalog.FindBase(id);
                Assert.IsTrue(CharacterPaletteResolver.TryChange(binding.sourceLook, "skin", "skin_10", out var look));
                Assert.IsTrue(CharacterPaletteResolver.TryChange(look, "top", "purple", out var dressed));
                var world = AvatarFactory.Create("palette_world", dressed, false);
                var preview = AvatarFactory.Create("palette_preview", dressed, false, forPreview: true);
                Material worldMaterial = null, previewMaterial = null;
                try
                {
                    worldMaterial = world.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
                    previewMaterial = preview.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
                    Assert.That(worldMaterial.shader.name, Is.EqualTo(CharacterPaletteResolver.ShaderName));
                    Assert.That(previewMaterial.shader, Is.SameAs(worldMaterial.shader));
                    Assert.That(worldMaterial.GetVector("_SkinTint"), Is.EqualTo(previewMaterial.GetVector("_SkinTint")));
                    Assert.That(worldMaterial.GetVector("_TopTint").w, Is.EqualTo(1f));
                    Assert.That(worldMaterial.GetVector("_BottomTint").w, Is.EqualTo(0f), "Unselected regions retain authored color.");
                    Assert.IsTrue(world.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.sharedMaterial == worldMaterial),
                        "Every crowd LOD must carry the selected palette.");
                    Assert.AreNotSame(worldMaterial, previewMaterial, "Different actors must not overwrite one another's colors.");
                    Assert.That(binding.maskedMaterial.GetVector("_SkinTint"), Is.EqualTo(Vector4.zero));
                    Assert.That(binding.sourceMaterial.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
                }
                finally { Object.Destroy(world.gameObject); Object.Destroy(preview.gameObject); }
                yield return null;
                Assert.IsTrue(worldMaterial == null && previewMaterial == null, "Instance materials must be released with their actors.");
            }
        }

        [UnityTest] public IEnumerator ActualShaderChangesOnlyTheSelectedMaskChannel()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            var shader = Shader.Find(CharacterPaletteResolver.ShaderName); Assert.IsNotNull(shader); Assert.IsTrue(shader.isSupported);
            var root = new GameObject("palette_shader_fixture"); root.transform.position = new Vector3(9000, -500, 0);
            var mesh = new Mesh(); var material = new Material(shader);
            var albedo = Texture(Enumerable.Repeat(new Color(.6f, .6f, .6f, 1f), 4).ToArray(), false);
            var mask0 = Texture(new[] { Color.red, Color.green, Color.blue, new Color(0, 0, 0, 1) }, true);
            // RGB Color constants have A=1; masks need independent channels, including alpha.
            mask0.SetPixels(new[] { new Color(1,0,0,0), new Color(0,1,0,0), new Color(0,0,1,0), new Color(0,0,0,1) }); mask0.Apply();
            var mask1 = Texture(Enumerable.Repeat(Color.clear, 4).ToArray(), true);
            var target = new RenderTexture(128, 128, 16, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            var previousSun = RenderSettings.sun; var previousTarget = RenderTexture.active;
            try
            {
                var shape = new GameObject("quad") { layer = 31 }; shape.transform.SetParent(root.transform, false);
                mesh.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(.5f,.5f,0), new Vector3(-.5f,.5f,0) };
                mesh.normals = Enumerable.Repeat(Vector3.forward, 4).ToArray();
                mesh.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
                mesh.triangles = new[] { 0,1,2,0,2,3 }; mesh.RecalculateBounds();
                shape.AddComponent<MeshFilter>().sharedMesh = mesh; shape.AddComponent<MeshRenderer>().sharedMaterial = material;
                material.SetTexture("_BaseMap", albedo); material.SetTexture("_TintMask0", mask0); material.SetTexture("_TintMask1", mask1);
                foreach (string region in new[] { "Skin", "Hair", "Top", "Bottom" })
                    material.SetVector("_" + region + "Reference", new Vector4(.3185f,.3185f,.3185f,1f));
                var camera = new GameObject("camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform, false);
                camera.transform.localPosition = new Vector3(0,0,2); camera.transform.localRotation = Quaternion.Euler(0,180,0);
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .6f; camera.aspect = 1;
                camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
                camera.allowHDR = false; camera.allowMSAA = false; camera.targetTexture = target; target.Create();
                var light = new GameObject("key").AddComponent<Light>(); light.transform.SetParent(root.transform, false);
                light.transform.localRotation = Quaternion.Euler(0,180,0); light.type = LightType.Directional; light.intensity = 1f;
                light.cullingMask = 1 << 31; RenderSettings.sun = light;
                yield return null;
                Color[] Read()
                {
                    camera.Render(); RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0,0,128,128),0,0); readback.Apply();
                    // Project the four actual UV-island centres; camera-facing rotation can mirror screen X.
                    return new[] { new Vector3(-.25f,-.25f,0), new Vector3(.25f,-.25f,0),
                        new Vector3(-.25f,.25f,0), new Vector3(.25f,.25f,0) }.Select(point =>
                    {
                        var uv = camera.WorldToViewportPoint(shape.transform.TransformPoint(point));
                        return readback.GetPixel(Mathf.RoundToInt(uv.x * 127), Mathf.RoundToInt(uv.y * 127));
                    }).ToArray();
                }
                var before = Read();
                Assert.That(before[0].a, Is.GreaterThan(.9f), "The real shader fixture must render.");
                var properties = new[] { "_SkinTint", "_HairTint", "_TopTint", "_BottomTint" };
                for (int region = 0; region < properties.Length; region++)
                {
                    material.SetVector(properties[region], new Vector4(.7f,.04f,.18f,1));
                    var after = Read();
                    for (int tile = 0; tile < 4; tile++)
                    {
                        float delta = Vector3.Distance(new Vector3(before[tile].r,before[tile].g,before[tile].b),
                            new Vector3(after[tile].r,after[tile].g,after[tile].b));
                        if (tile == region) Assert.That(delta, Is.GreaterThan(.08f), properties[region] + " must change its actual region.");
                        else Assert.That(delta, Is.LessThan(.012f), properties[region] + " must leave other regions intact.");
                    }
                    material.SetVector(properties[region], Vector4.zero);
                }
            }
            finally
            {
                RenderSettings.sun = previousSun; RenderTexture.active = previousTarget;
                Object.Destroy(root); Object.Destroy(mesh); Object.Destroy(material); Object.Destroy(albedo);
                Object.Destroy(mask0); Object.Destroy(mask1); Object.Destroy(readback); target.Release(); Object.Destroy(target);
            }
            yield return null;
        }

        private static Texture2D Texture(Color[] pixels, bool linear)
        {
            var texture = new Texture2D(2,2,TextureFormat.RGBA32,false,linear) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }
    }
}
#endif
