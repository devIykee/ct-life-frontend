using System.Collections.Generic;
using CTLife.World.Vehicles;
using UnityEngine;

namespace CTLife.CityLife
{
    /// <summary>
    /// The Duo Shop's world props (2026-10-23): furniture prefabs by modelKey (Resources/World/Furniture, CC0 Poly Haven
    /// models processed by art/pipeline/world_v3_furniture.py), the parcel (a kraft box on an ambientCG CC0 cardboard scan,
    /// tape and a blank shipping label), the Residences parcel locker, and the courier livery for the v3 van (our own
    /// "CT Parcel" mark; no real brand).
    /// </summary>
    public static class ShopProps
    {
        public const string FurniturePath = "World/Furniture/";
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        private static Material _kraft, _tape, _label, _steel, _seam, _screenOff, _screenOn, _panel, _ink;

        public static GameObject Furniture(string modelKey)
        {
            if (string.IsNullOrEmpty(modelKey)) return null;
            if (Prefabs.TryGetValue(modelKey, out var p)) return p;
            p = Resources.Load<GameObject>(FurniturePath + modelKey);
            if (p == null) Debug.LogWarning("CTLIFE [shop] no furniture prefab " + modelKey);
            Prefabs[modelKey] = p;
            return p;
        }

        private static readonly Dictionary<string, ResourceRequest> Pending = new Dictionary<string, ResourceRequest>();

        /// <summary>True once the prefab is in memory; until then starts (once) an async load, so the meshes and textures
        /// upload over frames instead of in one (the Decorate tray and the catalogue rows ask for many at once).</summary>
        public static bool FurnitureReady(string modelKey)
        {
            if (string.IsNullOrEmpty(modelKey) || Prefabs.ContainsKey(modelKey)) return true;
            if (!Pending.TryGetValue(modelKey, out var req)) Pending[modelKey] = req = Resources.LoadAsync<GameObject>(FurniturePath + modelKey);
            if (!req.isDone) return false;
            Pending.Remove(modelKey);
            var p = req.asset as GameObject;
            if (p == null) Debug.LogWarning("CTLIFE [shop] no furniture prefab " + modelKey);
            Prefabs[modelKey] = p;
            return true;
        }

        /// <summary>A furniture instance with no colliders of its own (place mode adds the footprint box).</summary>
        public static GameObject SpawnFurniture(string modelKey, Transform parent)
        {
            var prefab = Furniture(modelKey);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = modelKey;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            return go;
        }

        private static Material Lit(string name, Color c, float smooth, float metal = 0f, Texture tex = null, Texture nrm = null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            var m = new Material(sh) { name = name, enableInstancing = true };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
            return m;
        }

        private static void Materials()
        {
            if (_kraft != null) return;
            var tex = Resources.Load<Texture2D>("World/Furniture/Textures/parcel_kraft_BaseColor");
            var nrm = Resources.Load<Texture2D>("World/Furniture/Textures/parcel_kraft_Normal");
            _kraft = Lit("parcel_kraft", new Color(0.86f, 0.70f, 0.52f), 0.18f, 0f, tex, nrm);
            _kraft.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.5f));
            _tape = Lit("parcel_tape", new Color(0.66f, 0.50f, 0.32f), 0.62f);
            _label = Lit("parcel_label", new Color(0.95f, 0.95f, 0.93f), 0.25f);
            _steel = Lit("locker_steel", new Color(0.23f, 0.25f, 0.27f), 0.42f, 0.55f);
            _seam = Lit("locker_seam", new Color(0.08f, 0.085f, 0.09f), 0.2f);
            _screenOff = Lit("locker_screen", new Color(0.05f, 0.06f, 0.07f), 0.85f);
            _screenOn = Lit("locker_screen_on", new Color(0.2f, 0.9f, 0.5f), 0.85f);
            _screenOn.EnableKeyword("_EMISSION");
            _screenOn.SetColor("_EmissionColor", new Color(0.25f, 1.3f, 0.6f));
            _panel = Lit("livery_panel", new Color(0.96f, 0.97f, 0.98f), 0.55f);
            _ink = Lit("livery_ink", new Color(0.04f, 0.33f, 0.86f), 0.45f);
        }

        private static GameObject Cube(Transform parent, string name, Vector3 pos, Vector3 size, Material m, bool shadows = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localScale = size;
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        /// <summary>A taped kraft parcel, 0.46 x 0.30 x 0.34 m, its origin on the floor under its centre.</summary>
        public static GameObject Parcel(Transform parent)
        {
            Materials();
            var root = new GameObject("parcel");
            root.transform.SetParent(parent, false);
            const float w = 0.46f, h = 0.30f, d = 0.34f;
            Cube(root.transform, "box", new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), _kraft);
            Cube(root.transform, "tape_top", new Vector3(0f, h + 0.0015f, 0f), new Vector3(w + 0.004f, 0.003f, 0.05f), _tape, false);
            Cube(root.transform, "tape_front", new Vector3(0f, h - 0.05f, d * 0.5f + 0.0015f), new Vector3(0.05f, 0.1f, 0.003f), _tape, false);
            Cube(root.transform, "tape_back", new Vector3(0f, h - 0.05f, -d * 0.5f - 0.0015f), new Vector3(0.05f, 0.1f, 0.003f), _tape, false);
            Cube(root.transform, "label", new Vector3(0.12f, h + 0.0035f, 0.06f), new Vector3(0.14f, 0.002f, 0.10f), _label, false);
            return root;
        }

        /// <summary>A wall-mounted steel parcel locker bank (1.5 x 1.9 x 0.45 m) with a status screen; origin on the floor at
        /// its front-bottom centre, facing +Z.</summary>
        public static GameObject Locker(Transform parent, out Renderer screen)
        {
            Materials();
            var root = new GameObject("parcel_locker");
            root.transform.SetParent(parent, false);
            const float w = 1.5f, h = 1.9f, d = 0.45f;
            Cube(root.transform, "body", new Vector3(0f, h * 0.5f, -d * 0.5f), new Vector3(w, h, d), _steel);
            // door seams: 3 columns x 5 rows, the middle column holds the screen
            for (int c = 1; c < 3; c++) Cube(root.transform, "seam_v", new Vector3(-w * 0.5f + c * w / 3f, h * 0.5f, 0.002f), new Vector3(0.008f, h - 0.08f, 0.004f), _seam, false);
            for (int r = 1; r < 5; r++) Cube(root.transform, "seam_h", new Vector3(0f, r * h / 5f, 0.002f), new Vector3(w - 0.08f, 0.008f, 0.004f), _seam, false);
            for (int c = 0; c < 3; c++)
                for (int r = 0; r < 5; r++)
                    Cube(root.transform, "handle", new Vector3(-w * 0.5f + (c + 0.85f) * w / 3f, (r + 0.5f) * h / 5f, 0.012f), new Vector3(0.012f, 0.09f, 0.016f), _seam, false);
            var s = Cube(root.transform, "screen", new Vector3(0f, h * 0.5f + 0.06f, 0.006f), new Vector3(0.36f, 0.22f, 0.01f), _screenOff, false);
            screen = s.GetComponent<Renderer>();
            return root;
        }

        public static void SetLockerLit(Renderer screen, bool on)
        {
            Materials();
            if (screen != null) screen.sharedMaterial = on ? _screenOn : _screenOff;
        }

        /// <summary>The courier livery on a v3 van: white paint and a "CT Parcel" panel on both flanks (our own mark).</summary>
        public static void CourierLivery(Vehicle van)
        {
            if (van == null) return;
            Materials();
            VehiclePaint.Apply(van, new Color(0.93f, 0.94f, 0.95f));
            var box = van.GetComponent<BoxCollider>();
            Vector3 size = box != null ? box.size : new Vector3(2f, 2.2f, 5.2f), c = box != null ? box.center : new Vector3(0f, 1.1f, 0f);
            foreach (var side in new[] { -1f, 1f })
            {
                var panel = new GameObject("livery_" + (side < 0 ? "L" : "R")).transform;
                panel.SetParent(van.transform, false);
                // on the windowless cargo flank, clear of the cab's glass (seen through the windscreen it read mirrored)
                panel.localPosition = new Vector3(side * (size.x * 0.5f + 0.004f), c.y - size.y * 0.02f, c.z - size.z * 0.18f - 0.35f);
                panel.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
                Cube(panel, "stripe", new Vector3(0f, -0.42f, 0f), new Vector3(size.z * 0.62f, 0.07f, 0.004f), _ink, false);
                Text(panel, "CT Parcel", new Vector3(-0.42f, 0.02f, 0.004f), 0.36f, new Color(0.04f, 0.33f, 0.86f));
                // the mark: a parcel glyph (two squares, a band) beside the word
                Cube(panel, "mark", new Vector3(0.88f, 0.02f, 0.003f), new Vector3(0.30f, 0.30f, 0.003f), _ink, false);
                Cube(panel, "mark_band", new Vector3(0.88f, 0.02f, 0.006f), new Vector3(0.06f, 0.30f, 0.003f), _panel, false);
                Cube(panel, "mark_lid", new Vector3(0.88f, 0.10f, 0.006f), new Vector3(0.30f, 0.025f, 0.003f), _panel, false);
                Text(panel, "Same-day home delivery", new Vector3(-0.42f, -0.25f, 0.004f), 0.13f, new Color(0.25f, 0.27f, 0.3f));
            }
        }

        private static Font _font;
        private static Material _textMat;

        /// <summary>Flat painted text (a TextMesh on the Inter font; mirrored per flank so it always reads left to right).</summary>
        private static void Text(Transform parent, string text, Vector3 pos, float height, Color color)
        {
            _font ??= Resources.Load<Font>("UI/Fonts/Manrope-Semibold") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var g = new GameObject("text");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tm = g.AddComponent<TextMesh>();
            tm.font = _font; tm.text = text; tm.fontSize = 96; tm.characterSize = 0.1f;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = color;
            var r = g.GetComponent<MeshRenderer>();
            if (_textMat == null)
            {
                var sh = Resources.Load<Shader>("World/Furniture/Shaders/PaintedText");
                _textMat = sh != null ? new Material(sh) { name = "painted_text", mainTexture = _font.material.mainTexture } : _font.material;
            }
            r.sharedMaterial = _textMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.AddComponent<FitTextHeight>().Height = height;
        }

        /// <summary>Scales a TextMesh so its cap height is <see cref="Height"/> metres once its mesh exists (TextMesh sizes
        /// depend on the font's metrics, so measure rather than guess).</summary>
        private sealed class FitTextHeight : MonoBehaviour
        {
            public float Height;
            private void LateUpdate()
            {
                var r = GetComponent<Renderer>();
                float h = r != null ? r.localBounds.size.y : 0f;
                if (h <= 1e-5f) return;
                transform.localScale *= Height / h;
                enabled = false;
            }
        }
    }
}
