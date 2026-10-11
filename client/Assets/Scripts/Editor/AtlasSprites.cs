using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Little 3D tree sprites for the CT Island atlas (ref 8/9 look), rendered in-engine from the project's own CC0 low-poly
    /// models (Quaternius Stylized Nature MegaKit / Ultimate Nature, art/LICENSES.md): orthographic, 55 degrees down, looking
    /// north, lit by the same low north-west sun as the venue maps. Each sprite keeps a small mip chain so stamping it at
    /// 15-30 atlas pixels stays clean, and its trunk-base pivot so instances stand where they are placed.
    /// </summary>
    internal static class AtlasSprites
    {
        public sealed class Sprite
        {
            public string Name;
            public float MetersWide;               // horizontal extent the sprite covers, in model metres
            public Vector2 Pivot;                  // trunk base in sprite uv (0..1, v up)
            public readonly List<(int w, int h, Color[] px)> Mips = new List<(int, int, Color[])>();
        }

        public static readonly string[] Models =
        {
            "tree_common_1", "tree_common_2", "tree_common_4", "tree_pine_1", "tree_pine_3", "tree_birch_1",
            "tree_maple_autumn", "tree_blossom_1", "palm_1", "palm_2", "bush_round",
        };

        public static Dictionary<string, Sprite> Render(int size = 192)
        {
            var result = new Dictionary<string, Sprite>();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sunGo = new GameObject("sprite sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(1f, -1.1f, -1f).normalized, Vector3.up);
            sun.color = new Color(1f, 0.92f, 0.8f);
            sun.intensity = 1.45f;
            sun.shadows = LightShadows.None;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.78f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.62f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.35f, 0.3f, 0.26f);
            RenderSettings.fog = false;
            var cam = new GameObject("sprite camera").AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            var rt = new RenderTexture(size * 2, size * 2, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var down = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32);
            rt.Create(); down.Create();
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active;
            try
            {
                foreach (var name in Models)
                {
                    var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/nature/" + name + ".fbx");
                    if (src == null) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(src, scene);
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var rs = go.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) { Object.DestroyImmediate(go); continue; }
                    // The imported materials carry no colour here (white); paint by material name instead.
                    var (leafHex, barkHex) = Palette(name);
                    foreach (var r in rs)
                    {
                        var mats = r.sharedMaterials;
                        for (int m = 0; m < mats.Length; m++)
                        {
                            string mn = (mats[m] != null ? mats[m].name : "").ToLowerInvariant();
                            bool bark = mn.Contains("bark") || mn.Contains("trunk") || mn.Contains("wood") || mn.Contains("brown");
                            mats[m] = Mat(bark ? barkHex : leafHex);
                        }
                        r.sharedMaterials = mats;
                    }
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    // Looking north and 55 degrees down: canopy tops plus a hint of trunk, the painted-map angle.
                    var rot = Quaternion.Euler(55f, 0f, 0f);
                    cam.transform.rotation = rot;
                    cam.transform.position = b.center - rot * Vector3.forward * (b.extents.magnitude * 3f + 5f);
                    float extent = Mathf.Max(b.size.x, b.size.z, b.size.y * 0.9f) * 0.58f;
                    cam.orthographicSize = extent;
                    cam.nearClipPlane = 0.05f;
                    cam.farClipPlane = b.extents.magnitude * 8f + 20f;
                    cam.targetTexture = rt;
                    for (int i = 0; i < 2; i++) cam.Render();
                    cam.targetTexture = null;
                    Graphics.Blit(rt, down);
                    RenderTexture.active = down;
                    tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    tex.Apply();
                    var px = tex.GetPixels();
                    var basePoint = cam.WorldToViewportPoint(new Vector3(b.center.x, b.min.y, b.center.z));
                    var s = new Sprite { Name = name, MetersWide = extent * 2f, Pivot = new Vector2(basePoint.x, basePoint.y) };
                    s.Mips.Add((size, size, px));
                    while (s.Mips[s.Mips.Count - 1].w > 8)
                    {
                        var (w, h, p) = s.Mips[s.Mips.Count - 1];
                        int w2 = w / 2, h2 = h / 2;
                        var q = new Color[w2 * h2];
                        for (int y = 0; y < h2; y++)
                            for (int x = 0; x < w2; x++)
                            {
                                // Premultiplied box filter so transparent edges do not darken.
                                Color a = p[(2 * y) * w + 2 * x], c = p[(2 * y) * w + 2 * x + 1], d = p[(2 * y + 1) * w + 2 * x], e = p[(2 * y + 1) * w + 2 * x + 1];
                                float al = (a.a + c.a + d.a + e.a) / 4f;
                                var rgb = (a * a.a + c * c.a + d * d.a + e * e.a) / Mathf.Max(1e-4f, a.a + c.a + d.a + e.a);
                                q[y * w2 + x] = new Color(rgb.r, rgb.g, rgb.b, al);
                            }
                        s.Mips.Add((w2, h2, q));
                    }
                    result[name] = s;
                    var dbg = Environment.GetEnvironmentVariable("CTLIFE_SPRITE_DEBUG");
                    if (!string.IsNullOrEmpty(dbg)) MapBaker.SavePng(px, size, size, System.IO.Path.Combine(dbg, name + ".png"));
                    Object.DestroyImmediate(go);
                }
            }
            finally
            {
                RenderTexture.active = prev;
                rt.Release(); down.Release();
                Object.DestroyImmediate(rt); Object.DestroyImmediate(down); Object.DestroyImmediate(tex);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            return result;
        }

        private static readonly Dictionary<string, Material> MatCache = new Dictionary<string, Material>();

        private static Material Mat(string hex)
        {
            if (MatCache.TryGetValue(hex, out var m) && m != null) return m;
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            m = new Material(Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Lit")) { name = "atlas " + hex };
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            MatCache[hex] = m;
            return m;
        }

        private static (string leaf, string bark) Palette(string model) => model switch
        {
            "tree_pine_1" => ("2f6e45", "6b4a32"),
            "tree_pine_3" => ("3a7a4a", "6b4a32"),
            "tree_birch_1" => ("8cbf4a", "e8e2d4"),
            "tree_maple_autumn" => ("df8a33", "6b4a32"),
            "tree_blossom_1" => ("f2a3c4", "6b4a32"),
            "palm_1" => ("4f9e3c", "a8835a"),
            "palm_2" => ("5aa845", "a8835a"),
            "bush_round" => ("4c8f3a", "4c8f3a"),
            "tree_common_2" => ("5fa846", "6b4a32"),
            "tree_common_4" => ("4a943e", "6b4a32"),
            _ => ("56a043", "6b4a32"),
        };

        /// <summary>Bilinear sample of the smallest mip that is still at least <paramref name="targetPx"/> wide.</summary>
        public static Color Sample(Sprite s, float u, float v, float targetPx)
        {
            int level = 0;
            while (level + 1 < s.Mips.Count && s.Mips[level + 1].w >= targetPx) level++;
            var (w, h, p) = s.Mips[level];
            float x = u * w - 0.5f, y = v * h - 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            Color At(int xx, int yy) => xx < 0 || yy < 0 || xx >= w || yy >= h ? new Color(0, 0, 0, 0) : p[yy * w + xx];
            var c00 = At(x0, y0); var c10 = At(x0 + 1, y0); var c01 = At(x0, y0 + 1); var c11 = At(x0 + 1, y0 + 1);
            float a = Mathf.Lerp(Mathf.Lerp(c00.a, c10.a, fx), Mathf.Lerp(c01.a, c11.a, fx), fy);
            if (a < 1e-3f) return new Color(0, 0, 0, 0);
            var rgb = (Color.Lerp(Color.Lerp(c00 * c00.a, c10 * c10.a, fx), Color.Lerp(c01 * c01.a, c11 * c11.a, fx), fy)) / a;
            return new Color(rgb.r, rgb.g, rgb.b, a);
        }
    }
}
