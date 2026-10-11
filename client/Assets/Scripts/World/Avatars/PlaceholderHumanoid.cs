using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Procedural skinned humanoid used until the art pipeline's characters land (and by the engine spike).
    /// Real human proportions (~1.75 m), the same 30 bone names as art/pipeline/ctl_config.py (Unreal-mannequin style,
    /// matching the Quaternius animation library), one SkinnedMeshRenderer, one shared palette material
    /// (colours are UV cells of a 16×16 palette texture), three LOD meshes (~3.9k / ~1.9k / ~0.5k triangles).
    /// </summary>
    public static class PlaceholderHumanoid
    {
        public static readonly string[] BoneNames =
        {
            "root", "pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "Head", "jaw", "eye_l", "eye_r",
            "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l", "thumb_01_l", "index_01_l",
            "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r", "thumb_01_r", "index_01_r",
            "thigh_l", "calf_l", "foot_l", "ball_l", "thigh_r", "calf_r", "foot_r", "ball_r",
        };

        private static readonly string[] Parents =
        {
            null, "root", "pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "Head", "Head", "Head",
            "spine_03", "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l", "hand_l",
            "spine_03", "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r", "hand_r",
            "pelvis", "thigh_l", "calf_l", "foot_l", "pelvis", "thigh_r", "calf_r", "foot_r",
        };

        // Rest pose (A-pose), metres, model space.
        private static readonly Dictionary<string, Vector3> Rest = new Dictionary<string, Vector3>
        {
            ["root"] = new Vector3(0, 0, 0), ["pelvis"] = new Vector3(0, 0.95f, 0),
            ["spine_01"] = new Vector3(0, 1.05f, 0), ["spine_02"] = new Vector3(0, 1.18f, 0), ["spine_03"] = new Vector3(0, 1.32f, 0),
            ["neck_01"] = new Vector3(0, 1.48f, 0), ["Head"] = new Vector3(0, 1.56f, 0), ["jaw"] = new Vector3(0, 1.58f, 0.05f),
            ["eye_l"] = new Vector3(0.035f, 1.67f, 0.09f), ["eye_r"] = new Vector3(-0.035f, 1.67f, 0.09f),
            ["clavicle_l"] = new Vector3(0.05f, 1.43f, 0), ["upperarm_l"] = new Vector3(0.19f, 1.42f, 0),
            ["lowerarm_l"] = new Vector3(0.25f, 1.15f, 0), ["hand_l"] = new Vector3(0.28f, 0.91f, 0.02f),
            ["thumb_01_l"] = new Vector3(0.28f, 0.88f, 0.05f), ["index_01_l"] = new Vector3(0.29f, 0.84f, 0.02f),
            ["clavicle_r"] = new Vector3(-0.05f, 1.43f, 0), ["upperarm_r"] = new Vector3(-0.19f, 1.42f, 0),
            ["lowerarm_r"] = new Vector3(-0.25f, 1.15f, 0), ["hand_r"] = new Vector3(-0.28f, 0.91f, 0.02f),
            ["thumb_01_r"] = new Vector3(-0.28f, 0.88f, 0.05f), ["index_01_r"] = new Vector3(-0.29f, 0.84f, 0.02f),
            ["thigh_l"] = new Vector3(0.1f, 0.92f, 0), ["calf_l"] = new Vector3(0.1f, 0.50f, 0.01f),
            ["foot_l"] = new Vector3(0.1f, 0.09f, -0.01f), ["ball_l"] = new Vector3(0.1f, 0.02f, 0.13f),
            ["thigh_r"] = new Vector3(-0.1f, 0.92f, 0), ["calf_r"] = new Vector3(-0.1f, 0.50f, 0.01f),
            ["foot_r"] = new Vector3(-0.1f, 0.09f, -0.01f), ["ball_r"] = new Vector3(-0.1f, 0.02f, 0.13f),
        };

        public struct Palette
        {
            public int skin, top, bottom, shoes, hair, eyes;
        }

        private static Texture2D _paletteTex;
        private static Material _material;
        private static readonly Dictionary<string, Mesh[]> _meshCache = new Dictionary<string, Mesh[]>();

        private static readonly Color[] SkinTones =
        {
            new Color(0.98f, 0.86f, 0.76f), new Color(0.94f, 0.78f, 0.66f), new Color(0.87f, 0.69f, 0.55f), new Color(0.78f, 0.58f, 0.44f),
            new Color(0.66f, 0.47f, 0.34f), new Color(0.54f, 0.37f, 0.26f), new Color(0.42f, 0.28f, 0.20f), new Color(0.32f, 0.21f, 0.15f),
        };

        /// <summary>Shared 16×16 palette: row 0 skin tones, rows 1–15 saturated Sims-like clothing colours.</summary>
        public static Texture2D PaletteTexture
        {
            get
            {
                if (_paletteTex != null) return _paletteTex;
                var t = new Texture2D(16, 16, TextureFormat.RGBA32, false) { name = "avatar_palette", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                    {
                        Color c;
                        if (y == 0) c = SkinTones[x % SkinTones.Length];
                        else if (y == 15) c = Color.Lerp(new Color(0.08f, 0.08f, 0.1f), new Color(0.95f, 0.95f, 0.95f), x / 15f);
                        else c = Color.HSVToRGB(x / 16f, Mathf.Lerp(0.35f, 0.85f, (y % 4) / 3f), Mathf.Lerp(0.95f, 0.35f, (y - 1) / 13f));
                        t.SetPixel(x, y, c);
                    }
                t.Apply(false, true);
                _paletteTex = t;
                return t;
            }
        }

        public static Material SharedMaterial
        {
            get
            {
                if (_material == null)
                {
                    var asset = Resources.Load<Material>("Materials/AvatarPalette");
                    _material = asset != null ? new Material(asset) : Mats.Textured(PaletteTexture);
                    _material.name = "AvatarPalette";
                    if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", PaletteTexture);
                    _material.mainTexture = PaletteTexture;
                    _material.enableInstancing = true;
                }
                return _material;
            }
        }

        /// <summary>Deterministic palette cells from the look string[7] (skinTone, head, hair, top, bottom, shoes, acc).</summary>
        public static Palette PaletteFor(string[] look)
        {
            int Cell(string id, int salt) => string.IsNullOrEmpty(id) ? 15 * 16 + 4 : 16 + (int)((uint)(id.GetHashCode() * 31 + salt) % 224u);
            int skin = 0;
            if (look != null && look.Length > 0 && !string.IsNullOrEmpty(look[0]))
            {
                var digits = new string(System.Array.FindAll(look[0].ToCharArray(), char.IsDigit));
                if (int.TryParse(digits, out var n)) skin = Mathf.Clamp(n - 1, 0, 7);
                else skin = (int)((uint)look[0].GetHashCode() % 8u);
            }
            return new Palette
            {
                skin = skin,
                hair = look != null && look.Length > 2 ? 15 * 16 + (int)((uint)(look[2] ?? "").GetHashCode() % 6u) : 15 * 16,
                top = Cell(look != null && look.Length > 3 ? look[3] : null, 3),
                bottom = Cell(look != null && look.Length > 4 ? look[4] : null, 4),
                shoes = Cell(look != null && look.Length > 5 ? look[5] : null, 5),
                eyes = 15 * 16,
            };
        }

        /// <summary>Creates a placeholder avatar GameObject with skeleton, SMR and LOD meshes.</summary>
        public static GameObject Create(string name, Palette palette, out SkinnedMeshRenderer smr, out Transform[] bones, out Mesh[] lods)
        {
            var go = new GameObject(name);
            bones = new Transform[BoneNames.Length];
            var byName = new Dictionary<string, Transform>();
            for (int i = 0; i < BoneNames.Length; i++)
            {
                var b = new GameObject(BoneNames[i]).transform;
                byName[BoneNames[i]] = b;
                bones[i] = b;
            }
            for (int i = 0; i < BoneNames.Length; i++)
            {
                var parent = Parents[i] == null ? go.transform : byName[Parents[i]];
                bones[i].SetParent(parent, false);
                bones[i].position = Rest[BoneNames[i]];
            }

            var bindposes = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) bindposes[i] = bones[i].worldToLocalMatrix * go.transform.localToWorldMatrix;

            string key = $"{palette.skin}_{palette.top}_{palette.bottom}_{palette.shoes}_{palette.hair}";
            if (!_meshCache.TryGetValue(key, out lods))
            {
                lods = new[] { BuildMesh(palette, 16, 7, bindposes), BuildMesh(palette, 11, 4, bindposes), BuildMesh(palette, 6, 2, bindposes) };
                _meshCache[key] = lods;
            }

            var meshGo = new GameObject("body");
            meshGo.transform.SetParent(go.transform, false);
            smr = meshGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = lods[0];
            smr.bones = bones;
            smr.rootBone = byName["pelvis"];
            smr.sharedMaterial = SharedMaterial;
            smr.quality = SkinQuality.Bone4;
            smr.updateWhenOffscreen = false;
            smr.localBounds = new Bounds(new Vector3(0, -0.1f, 0), new Vector3(1.6f, 2.4f, 1.2f));
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.receiveShadows = false;
            return go;
        }

        // ---------------------------------------------------------------- mesh building

        private sealed class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<BoneWeight> w = new List<BoneWeight>();
            public readonly List<int> t = new List<int>();
        }

        private static int Bone(string name) => System.Array.IndexOf(BoneNames, name);

        private static Vector2 CellUv(int cell) => new Vector2(((cell % 16) + 0.5f) / 16f, ((cell / 16) + 0.5f) / 16f);

        private static BoneWeight Weight(int a, int b, float wb)
        {
            if (b < 0 || wb <= 0f) return new BoneWeight { boneIndex0 = a, weight0 = 1f };
            return new BoneWeight { boneIndex0 = a, weight0 = 1f - wb, boneIndex1 = b, weight1 = wb };
        }

        private static void Tube(Builder m, Vector3 a, Vector3 b, float ra, float rb, string bone, string parentBone, int radial, int rings, int cell, float flatten = 1f)
        {
            int bi = Bone(bone), pi = parentBone != null ? Bone(parentBone) : -1;
            var axis = (b - a).normalized;
            var side = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.forward)) > 0.9f ? Vector3.right : Vector3.forward).normalized;
            var up = Vector3.Cross(side, axis).normalized;
            var uv = CellUv(cell);
            int start = m.v.Count;
            for (int r = 0; r <= rings; r++)
            {
                float f = r / (float)rings;
                var c = Vector3.Lerp(a, b, f);
                float rad = Mathf.Lerp(ra, rb, f) * (1f + 0.08f * Mathf.Sin(f * Mathf.PI));
                float blend = pi >= 0 ? Mathf.Clamp01(0.5f - f * 2.5f) : 0f;
                for (int s = 0; s < radial; s++)
                {
                    float ang = s / (float)radial * Mathf.PI * 2f;
                    var dir = side * Mathf.Cos(ang) + up * Mathf.Sin(ang) * flatten;
                    m.v.Add(c + dir * rad);
                    m.n.Add(dir.normalized);
                    m.uv.Add(uv);
                    m.w.Add(Weight(bi, pi, blend));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < radial; s++)
                {
                    int i0 = start + r * radial + s, i1 = start + r * radial + (s + 1) % radial;
                    int i2 = i0 + radial, i3 = i1 + radial;
                    m.t.Add(i0); m.t.Add(i2); m.t.Add(i1);
                    m.t.Add(i1); m.t.Add(i2); m.t.Add(i3);
                }
            Cap(m, start, radial, a, -axis, bi, uv, false);
            Cap(m, start + rings * radial, radial, b, axis, bi, uv, true);
        }

        private static void Cap(Builder m, int ringStart, int radial, Vector3 center, Vector3 normal, int bone, Vector2 uv, bool forward)
        {
            int c = m.v.Count;
            m.v.Add(center);
            m.n.Add(normal);
            m.uv.Add(uv);
            m.w.Add(Weight(bone, -1, 0));
            for (int s = 0; s < radial; s++)
            {
                int a = ringStart + s, b = ringStart + (s + 1) % radial;
                if (forward) { m.t.Add(c); m.t.Add(a); m.t.Add(b); }
                else { m.t.Add(c); m.t.Add(b); m.t.Add(a); }
            }
        }

        private static void Ellipsoid(Builder m, Vector3 center, Vector3 radii, string bone, int lat, int lon, int cell, int topCell = -1, float topFrom = 2f)
        {
            int bi = Bone(bone);
            int start = m.v.Count;
            for (int y = 0; y <= lat; y++)
            {
                float v = y / (float)lat;
                float phi = v * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float th = x / (float)lon * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    m.v.Add(center + Vector3.Scale(dir, radii));
                    m.n.Add(dir);
                    m.uv.Add(CellUv(topCell >= 0 && dir.y > topFrom ? topCell : cell));
                    m.w.Add(Weight(bi, -1, 0));
                }
            }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int i0 = start + y * (lon + 1) + x, i1 = i0 + 1, i2 = i0 + lon + 1, i3 = i2 + 1;
                    m.t.Add(i0); m.t.Add(i1); m.t.Add(i2);
                    m.t.Add(i1); m.t.Add(i3); m.t.Add(i2);
                }
        }

        private static Mesh BuildMesh(Palette p, int radial, int rings, Matrix4x4[] bindposes)
        {
            var m = new Builder();
            int skin = p.skin, top = p.top, bottom = p.bottom, shoes = p.shoes;
            int lat = Mathf.Max(4, radial * 3 / 4), lon = radial;

            // Torso: hips (bottom colour) → chest (top colour).
            Tube(m, new Vector3(0, 0.86f, 0), new Vector3(0, 1.06f, 0), 0.15f, 0.14f, "pelvis", null, radial, Mathf.Max(1, rings / 2), bottom, 0.7f);
            Tube(m, new Vector3(0, 1.04f, 0), new Vector3(0, 1.20f, 0), 0.135f, 0.15f, "spine_01", "pelvis", radial, Mathf.Max(1, rings / 2), top, 0.7f);
            Tube(m, new Vector3(0, 1.18f, 0), new Vector3(0, 1.32f, 0), 0.15f, 0.17f, "spine_02", "spine_01", radial, Mathf.Max(1, rings / 2), top, 0.65f);
            Tube(m, new Vector3(0, 1.30f, 0), new Vector3(0, 1.46f, 0), 0.17f, 0.12f, "spine_03", "spine_02", radial, Mathf.Max(1, rings / 2), top, 0.6f);
            Tube(m, new Vector3(0, 1.45f, 0), new Vector3(0, 1.57f, 0), 0.05f, 0.05f, "neck_01", "spine_03", Mathf.Max(5, radial / 2), 1, skin);
            // Head with hair cap; eyes.
            Ellipsoid(m, new Vector3(0, 1.67f, 0.01f), new Vector3(0.095f, 0.115f, 0.105f), "Head", lat, lon, skin, p.hair, 0.25f);
            if (radial >= 10)
            {
                Ellipsoid(m, Rest["eye_l"], Vector3.one * 0.013f, "Head", 4, 6, p.eyes);
                Ellipsoid(m, Rest["eye_r"], Vector3.one * 0.013f, "Head", 4, 6, p.eyes);
            }
            foreach (var s in new[] { "l", "r" })
            {
                float sx = s == "l" ? 1f : -1f;
                // Arms: sleeves in the top colour, forearms skin.
                Tube(m, Rest["upperarm_" + s], Rest["lowerarm_" + s], 0.055f, 0.045f, "upperarm_" + s, "clavicle_" + s, radial, rings, top);
                Tube(m, Rest["lowerarm_" + s], Rest["hand_" + s], 0.042f, 0.034f, "lowerarm_" + s, "upperarm_" + s, radial, rings, skin);
                Ellipsoid(m, Rest["hand_" + s] + new Vector3(0.005f * sx, -0.05f, 0), new Vector3(0.03f, 0.06f, 0.045f), "hand_" + s, Mathf.Max(3, lat / 2), Mathf.Max(4, lon / 2), skin);
                // Legs: bottoms, shoes.
                Tube(m, Rest["thigh_" + s], Rest["calf_" + s], 0.08f, 0.06f, "thigh_" + s, "pelvis", radial, rings, bottom);
                Tube(m, Rest["calf_" + s], Rest["foot_" + s] + Vector3.up * 0.02f, 0.058f, 0.045f, "calf_" + s, "thigh_" + s, radial, rings, bottom);
                Tube(m, Rest["foot_" + s] + new Vector3(0, -0.03f, -0.05f), Rest["ball_" + s] + new Vector3(0, 0.01f, 0.06f), 0.045f, 0.04f, "foot_" + s, null, Mathf.Max(5, radial / 2), 1, shoes, 0.6f);
            }

            var mesh = new Mesh { name = $"placeholder_r{radial}" };
            mesh.SetVertices(m.v);
            mesh.SetNormals(m.n);
            mesh.SetUVs(0, m.uv);
            mesh.boneWeights = m.w.ToArray();
            mesh.bindposes = bindposes;
            mesh.SetTriangles(m.t, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }
    }
}
