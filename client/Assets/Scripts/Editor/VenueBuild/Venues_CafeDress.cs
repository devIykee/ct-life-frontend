using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Terminal Café look-dev dressing (round 3). The café stays a repack of its authored prefab, because the
    /// character-feel lane fitted the typing and social sit motions to that furniture (CafeSeatMotionProfile); this
    /// adds dressing around it before the repack merges, atlases and bakes: warm pendant lamps over the social tables and
    /// the terminal rows (baked lights), plants, bookshelves in the lounge, framed pictures, pastries and cups on the
    /// counter, a back-bar shelf. Nothing moves or replaces a seat, desk, approach or exit route. Floor props are manifest
    /// models named Direction_*, so the collision pass collides them like the authored ones.
    /// </summary>
    public static class VenuesCafeDress
    {
        private static JObject _manifest;

        public static void Dress(GameObject root)
        {
            // The two authored chandeliers float with no ceiling and their baked sun shadows read as stains on the floor;
            // their warm lights stay (the "Cafe pendant" lights).
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == "Direction_home_light_chandelier") Object.DestroyImmediate(t.gameObject);
            var dress = new GameObject("Dressing").transform;
            dress.SetParent(root.transform, false);
            var lit = new VenueMeshAcc();
            var glow = new VenueMeshAcc();

            // Warm light sources that exist in the room (no ceiling to hang pendants from): the desk lamps are lit,
            // each social table gets a small glass table lamp, brass wall sconces line the walls.
            foreach (float z in new[] { 4.0f, 8.0f })
                foreach (float x in new[] { -8.1f, -3.1f, 1.9f, 6.9f })
                {
                    glow.Sphere(Matrix4x4.Translate(new Vector3(x, 1.17f, z - 0.06f)), 0.045f, 4, 8, VenuePalette.Uv("glowamber"));
                    Light(dress, new Vector3(x, 1.25f, z - 0.25f), new Color(1f, 0.78f, 0.5f), 0.55f, 2.6f);
                }
            foreach (float x in new[] { -6f, -2f, 2f, 6f })
            {
                var t = new Vector3(x + 0.25f, 0.76f, -2.85f);
                lit.Cylinder(Matrix4x4.Translate(t), 0.06f, 0.05f, 0.02f, 10, VenuePalette.Uv("brass"));
                lit.Cylinder(Matrix4x4.Translate(t + Vector3.up * 0.02f), 0.012f, 0.012f, 0.12f, 6, VenuePalette.Uv("brass"));
                glow.Cylinder(Matrix4x4.Translate(t + Vector3.up * 0.13f), 0.07f, 0.04f, 0.1f, 10, VenuePalette.Uv("glowamber"));
                Light(dress, t + Vector3.up * 0.4f, new Color(1f, 0.74f, 0.45f), 0.6f, 2.8f);
            }
            foreach (var (p, yaw) in new[] { (new Vector3(-5.2f, 2.35f, 8.95f), 180f), (new Vector3(5.2f, 2.35f, 8.95f), 180f), (new Vector3(-9.6f, 2.35f, 8.95f), 180f),
                                             (new Vector3(9.6f, 2.35f, 8.95f), 180f), (new Vector3(11.97f, 2.35f, -3.6f), -90f), (new Vector3(11.97f, 2.35f, 3.6f), -90f),
                                             (new Vector3(-11.97f, 2.35f, 3.6f), 90f) })
                Sconce(lit, glow, dress, p, yaw);
            Light(dress, new Vector3(-9.6f, 2.6f, -4.7f), new Color(1f, 0.74f, 0.45f), 1.1f, 5f);
            Light(dress, new Vector3(0f, 3.2f, 7.4f), new Color(1f, 0.8f, 0.55f), 0.9f, 6f);
            // Deep green wainscot with an oak cap rail on the back and side walls.
            Wainscot(lit, new Vector3(0f, 0f, 8.96f), 23.9f, 0f);
            Wainscot(lit, new Vector3(11.96f, 0f, 0f), 17.9f, 90f);
            Wainscot(lit, new Vector3(-11.96f, 0f, 3.2f), 11.5f, 90f);

            // Plants: corners, the door, between the terminal rows and the walls.
            Model(dress, "houseplant_bush", new Vector3(11.1f, 0, -8.3f), 0f, 1.0f);
            Model(dress, "houseplant_tall", new Vector3(8.7f, 0, -8.55f), 0f, 1.0f);
            Model(dress, "houseplant_bush", new Vector3(-11.0f, 0, 4.2f), 90f, 0.9f);
            Model(dress, "houseplant_bush", new Vector3(11.0f, 0, 4.2f), -90f, 0.9f);
            Model(dress, "potted_plant", new Vector3(-2.6f, 0, -8.6f), 0f, 1.2f);
            Model(dress, "potted_plant", new Vector3(2.6f, 0, -8.6f), 0f, 1.2f);
            Model(dress, "home_houseplant_5", new Vector3(0f, 0, 5.6f), 0f, 1.0f);
            Model(dress, "home_houseplant_5", new Vector3(-10.4f, 0, -0.2f), 0f, 0.9f);
            // Lounge: bookshelves and standing lamps beside the sofas.
            Model(dress, "home_bookshelf", new Vector3(-8.2f, 0, -8.7f), 0f, 1.0f);
            Model(dress, "home_bookshelf", new Vector3(8.2f, 0, -8.7f), 0f, 1.0f);
            // Framed pictures on the side walls, between the shelf stacks and the terminals.
            Model(dress, "home_pictureframe_large_a", new Vector3(11.9f, 1.5f, -4.6f), -90f, 1.0f);
            Model(dress, "home_pictureframe_large_a", new Vector3(11.9f, 1.5f, 5.6f), -90f, 1.0f);
            Model(dress, "home_pictureframe_large_a", new Vector3(-11.9f, 1.5f, 5.6f), 90f, 1.0f);
            // Counter: a cake stand, doughnuts, stacked mugs; a back-bar shelf with cups.
            Model(dress, "cake", new Vector3(-8.85f, 1.01f, -6.85f), 90f, 1.4f);
            Model(dress, "donut", new Vector3(-8.85f, 1.01f, -3.3f), 0f, 1.6f);
            Model(dress, "donut", new Vector3(-8.75f, 1.01f, -3.05f), 40f, 1.6f);
            foreach (float z in new[] { -5.3f, -5.15f })
                for (int k = 0; k < 3; k++) Model(dress, "mug", new Vector3(-8.95f, 1.01f + k * 0.1f, z), 90f, 1.0f);
            Model(dress, "cafe_shelf", new Vector3(-11.75f, 0f, -5.4f), 90f, 1.0f);

            Emit(dress, "Dressing lit", lit, VenuePalette.Lit);
            Emit(dress, "Dressing glow", glow, VenuePalette.Glow);
        }

        private static void Sconce(VenueMeshAcc lit, VenueMeshAcc glow, Transform parent, Vector3 p, float yaw)
        {
            var q = Quaternion.Euler(0, yaw, 0);
            var f = q * Vector3.forward; // into the room
            lit.Box(Matrix4x4.TRS(p, q, new Vector3(0.14f, 0.22f, 0.03f)), VenuePalette.Uv("brass"));
            lit.Box(Matrix4x4.TRS(p + f * 0.1f, q, new Vector3(0.025f, 0.025f, 0.2f)), VenuePalette.Uv("brass"));
            glow.Sphere(Matrix4x4.Translate(p + f * 0.22f + Vector3.up * 0.06f), 0.08f, 5, 9, VenuePalette.Uv("glowamber"));
            Light(parent, p + f * 0.45f, new Color(1f, 0.76f, 0.48f), 0.6f, 3.4f);
        }

        private static void Wainscot(VenueMeshAcc lit, Vector3 baseCenter, float length, float yaw)
        {
            var q = Quaternion.Euler(0, yaw, 0);
            lit.Box(Matrix4x4.TRS(baseCenter + Vector3.up * 0.55f, q, new Vector3(length, 1.1f, 0.03f)), VenuePalette.Uv("forest"));
            lit.Box(Matrix4x4.TRS(baseCenter + Vector3.up * 1.12f, q, new Vector3(length, 0.05f, 0.07f)), VenuePalette.Uv("oak"));
            lit.Box(Matrix4x4.TRS(baseCenter + Vector3.up * 0.06f, q, new Vector3(length, 0.12f, 0.05f)), VenuePalette.Uv("darkwood"));
            for (float u = -length * .5f + 0.6f; u < length * .5f; u += 1.2f)
                lit.Box(Matrix4x4.TRS(baseCenter + q * new Vector3(u, 0.6f, 0.018f), q, new Vector3(0.03f, 0.9f, 0.01f)), VenuePalette.Uv("leafdark"));
        }

        private static void Light(Transform parent, Vector3 p, Color c, float intensity, float range)
        {
            var go = new GameObject("Dressing light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = p;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.lightmapBakeType = LightmapBakeType.Baked;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.Soft;
        }

        private static void Model(Transform parent, string id, Vector3 p, float facing, float scale)
        {
            _manifest ??= JObject.Parse(File.ReadAllText("Assets/Art/environment/manifest.json"));
            var row = _manifest[id] ?? throw new InvalidDataException("Unknown environment id " + id);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/" + (string)row["fbx"]) ?? throw new FileNotFoundException(id);
            var go = Object.Instantiate(src, parent);
            go.name = "Direction_" + id;
            go.transform.localPosition = p;
            go.transform.localRotation = Quaternion.Euler(0f, facing + 180f, 0f);
            go.transform.localScale = Vector3.one * scale;
        }

        private static void Emit(Transform parent, string name, VenueMeshAcc acc, Material material)
        {
            if (acc.Empty) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = acc.ToMesh(name);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
