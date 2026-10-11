using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.World
{
    /// <summary>
    /// A soft ring on the floor under the offered interactable (the in-world half of the CTA), pulsing gently; on the
    /// approach point for seats so the player sees where they will stand. One sprite, no shadows, no new assets.
    /// </summary>
    public sealed class InteractionHighlight : MonoBehaviour
    {
        private static Sprite _ring;
        private SpriteRenderer _renderer;
        private Interactable _target;
        private float _shownAt;

        public static InteractionHighlight Create(Transform parent)
        {
            var go = new GameObject("interaction_highlight");
            if (parent != null) go.transform.SetParent(parent, false);
            var h = go.AddComponent<InteractionHighlight>();
            h._renderer = go.AddComponent<SpriteRenderer>();
            h._renderer.sprite = Ring();
            h._renderer.color = new Color(1f, 0.86f, 0.32f, 0.85f); // brand yellow (Phone dock), warm against floors
            h._renderer.shadowCastingMode = ShadowCastingMode.Off;
            h._renderer.receiveShadows = false;
            h._renderer.sortingOrder = 5;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.SetActive(false);
            return h;
        }

        public void Show(Interactable target)
        {
            if (target == null) { if (gameObject.activeSelf) gameObject.SetActive(false); _target = null; return; }
            if (!ReferenceEquals(target, _target)) { _target = target; _shownAt = Time.time; }
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            var p = target.Sits ? Vector3.Lerp(target.approach, target.position, 0.5f) : target.position;
            transform.position = p + Vector3.up * 0.025f;
        }

        private void LateUpdate()
        {
            if (_target == null) return;
            float t = Time.time - _shownAt;
            float pop = Mathf.Clamp01(t / 0.18f);
            float pulse = 1f + 0.06f * Mathf.Sin(t * 4.2f);
            float size = (_target.kind == InteractionKind.Door ? 1.4f : 1.0f) * pulse * Mathf.SmoothStep(0.6f, 1f, pop);
            transform.localScale = new Vector3(size, size, size);
            var c = _renderer.color; c.a = 0.85f * pop; _renderer.color = c;
        }

        private static Sprite Ring()
        {
            if (_ring != null) return _ring;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "interaction_ring", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // A crisp ring (0.78–0.92) with a soft inner glow.
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.85f) / 0.07f);
                    float glow = Mathf.Clamp01(1f - r / 0.85f) * 0.18f;
                    float a = r > 1f ? 0f : Mathf.Max(ring, glow);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(true, true);
            _ring = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            return _ring;
        }
    }
}
