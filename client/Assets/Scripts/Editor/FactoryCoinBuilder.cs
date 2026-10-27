using System.IO;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Builds Resources/World/Factory/FactoryCoin.prefab for the Rugpull Factory's launch desk: Kenney's CC0 coin-gold model
    /// (Assets/Art/Factory/Coin) stood upright and scaled to 0.42 m, a Twemoji face on each side (URP Unlit, transparent; the
    /// runtime swaps the texture per art id) and a short trail for the launch. Run from the menu or batch mode:
    /// <c>-executeMethod CTLife.EditorTools.FactoryCoinBuilder.Build</c>.
    /// </summary>
    public static class FactoryCoinBuilder
    {
        private const string Model = "Assets/Art/Factory/Coin/coin-gold.fbx";
        private const string FaceMat = "Assets/Art/Factory/Coin/factory_coin_face.mat";
        private const string TrailMat = "Assets/Art/Factory/Coin/factory_coin_trail.mat";
        private const string Prefab = "Assets/Resources/World/Factory/FactoryCoin.prefab";
        public const float Diameter = 0.42f, Thickness = 0.07f;

        [MenuItem("CT Life/Build/Factory coin prefab")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if (model == null) { Debug.LogError("CTLIFE [factory] no model at " + Model); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
            var face = Transparent(FaceMat, AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/UI/Factory/Coins/rf_frog.png"), Color.white);
            var trail = Transparent(TrailMat, null, new Color(1f, 0.82f, 0.25f, 0.85f));

            var root = new GameObject("FactoryCoin");
            var body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            // stand the coin up: its thinnest axis faces the camera (local Z)
            var b = Bounds(body);
            var s = b.size;
            if (s.y <= s.x && s.y <= s.z) body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            else if (s.x <= s.y && s.x <= s.z) body.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            b = Bounds(body);
            float scale = Diameter / Mathf.Max(b.size.x, b.size.y);
            body.transform.localScale = Vector3.one * scale;
            b = Bounds(body);
            body.transform.localPosition -= b.center - root.transform.position;
            // Kenney's coin is chunky (thickness ~0.43 of the diameter); flatten it toward a real coin's proportions
            var shape = new GameObject("Shape").transform;
            shape.SetParent(root.transform, false);
            body.transform.SetParent(shape, true);
            b = Bounds(body);
            shape.localScale = new Vector3(1f, 1f, Mathf.Min(1f, Thickness / Mathf.Max(1e-3f, b.size.z)));
            b = Bounds(body);
            float half = b.extents.z;
            Debug.Log($"CTLIFE [factory] coin bounds {b.size} (scale {scale:0.###})");

            Quad(root.transform, "FaceFront", face, new Vector3(0, 0, -half - 0.003f), Quaternion.identity, Diameter * 0.64f);
            Quad(root.transform, "FaceBack", face, new Vector3(0, 0, half + 0.003f), Quaternion.Euler(0, 180f, 0), Diameter * 0.64f);

            var t = root.AddComponent<TrailRenderer>();
            t.sharedMaterial = trail;
            t.time = 0.35f;
            t.startWidth = Diameter * 0.8f;
            t.endWidth = 0f;
            t.minVertexDistance = 0.05f;
            t.emitting = false;
            t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            t.receiveShadows = false;

            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            Directory.CreateDirectory(Path.GetDirectoryName(Prefab));
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("CTLIFE [factory] wrote " + Prefab);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static void Quad(Transform parent, string name, Material m, Vector3 pos, Quaternion rot, float size)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = pos;
            q.transform.localRotation = rot;
            q.transform.localScale = new Vector3(size, size, 1f);
            q.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        private static Material Transparent(string path, Texture2D tex, Color tint)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 2f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
