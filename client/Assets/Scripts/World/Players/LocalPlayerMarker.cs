using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.World
{
    /// <summary>
    /// A soft mint "you" ring on the floor under the local player (Phase A, 2026-10-17): players could not find themselves in
    /// dark venues where a dark outfit merges with the floor. Unlit, transparent, no shadows, one quad; it pulses for the first
    /// seconds after arriving. Feel lane F1: with the follow camera the player is always centre-screen, so the ring now only
    /// marks an arrival (spawn, fast travel, a correction) and then shrinks away. Lives on its own child so it never touches
    /// the character model.
    /// </summary>
    public sealed class LocalPlayerMarker : MonoBehaviour
    {
        public const float Radius = 0.62f, ArrivalPulseSeconds = 3f, VisibleSeconds = 4f, FadeSeconds = 0.6f;

        /// <summary>Show the arrival pulse again (a teleport).</summary>
        public void Pulse() { _born = Time.time; if (_ring != null) _ring.gameObject.SetActive(true); }
        private static Material _material;
        private Transform _ring;
        private float _born;

        private void Start()
        {
            _born = Time.time;
            var go = new GameObject("you-ring");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.025f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one * Radius * 2f;
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            _ring = go.transform;
        }

        private void LateUpdate()
        {
            if (_ring == null) return;
            // Keep it flat on the floor while the character moves/animates (the parent can tilt slightly in a stumble).
            _ring.rotation = Quaternion.Euler(90f, 0f, 0f);
            float age = Time.time - _born;
            if (age > VisibleSeconds) { if (_ring.gameObject.activeSelf) _ring.gameObject.SetActive(false); return; }
            float pulse = age < ArrivalPulseSeconds ? 1f + 0.35f * Mathf.Sin(age * 6.5f) * (1f - age / ArrivalPulseSeconds) : 1f;
            float fade = Mathf.Clamp01((VisibleSeconds - age) / FadeSeconds);
            _ring.localScale = Vector3.one * Radius * 2f * pulse * fade;
        }

        private static Mesh _quad;
        private static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "you-ring-quad" };
            _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            _quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            _quad.RecalculateBounds();
            return _quad;
        }

        public static Material Material
        {
            get
            {
                if (_material != null) return _material;
                _material = Resources.Load<Material>("World/YouRing");    // shipped asset (keeps the transparent variant)
                if (_material != null) return _material;
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                _material = new Material(shader) { name = "you-ring", renderQueue = (int)RenderQueue.Transparent };
                _material.SetFloat("_Surface", 1f);   // transparent
                _material.SetFloat("_Blend", 0f);     // alpha
                _material.SetFloat("_ZWrite", 0f);
                _material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                _material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                _material.SetOverrideTag("RenderType", "Transparent");
                _material.SetTexture("_BaseMap", RingTexture());
                _material.SetColor("_BaseColor", Color.white);
                return _material;
            }
        }

        /// <summary>Mint ring with a plum hairline and a faint fill (procedural, 128 px).</summary>
        public static Texture2D RingTexture(bool readable = false)
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "you-ring", wrapMode = TextureWrapMode.Clamp };
            var mint = new Color(0.24f, 0.86f, 0.52f); var plum = new Color(0.16f, 0.04f, 0.29f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = new Vector2((x + 0.5f) / N - 0.5f, (y + 0.5f) / N - 0.5f).magnitude * 2f;   // 0 centre .. 1 edge
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.1f);
                    float line = Mathf.Clamp01(1f - Mathf.Abs(d - 0.93f) / 0.035f);
                    float fill = d < 0.78f ? 0.16f * (1f - d) : 0f;
                    var c = Color.Lerp(mint, plum, line * 0.8f);
                    c.a = Mathf.Clamp01(Mathf.Max(ring * 0.9f, Mathf.Max(line * 0.8f, fill)));
                    if (d > 1f) c.a = 0f;
                    t.SetPixel(x, y, c);
                }
            t.Apply(false, !readable);
            return t;
        }
    }
}
