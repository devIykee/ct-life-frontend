using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>Builds player and NPC avatars: assembled parts when the art exists, the placeholder otherwise.</summary>
    public static class AvatarFactory
    {
        private static Sprite _blob;

        public static AvatarView Create(string name, string[] look, bool isNpc, string archetypeId = null, Transform parent = null)
        {
            GameObject go;
            Animator animator = null;
            ProceduralAnimator proc = null;
            SkinnedMeshRenderer body;
            Mesh[] lods = null;

            var resolvedLook = isNpc ? NpcLook(archetypeId) : look;
            if (ItemCatalog.TryResolve(resolvedLook, out var basePrefab, out var partPrefabs))
            {
                go = Object.Instantiate(basePrefab, parent);
                go.name = name;
                var instances = new List<GameObject>();
                var smrs = new List<SkinnedMeshRenderer>();
                foreach (var p in partPrefabs)
                {
                    var inst = Object.Instantiate(p, go.transform);
                    instances.Add(inst);
                    smrs.AddRange(inst.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                }
                body = CharacterAssembler.Merge(go.transform, smrs);
                foreach (var inst in instances) Object.Destroy(inst);
                animator = go.GetComponentInChildren<Animator>();
                if (animator != null && animator.runtimeAnimatorController == null) animator.runtimeAnimatorController = ItemCatalog.AvatarController;
                if (animator != null) animator.cullingMode = AnimatorCullingMode.CullCompletely;
            }
            else
            {
                var palette = PlaceholderHumanoid.PaletteFor(resolvedLook);
                go = PlaceholderHumanoid.Create(name, palette, out body, out var bones, out lods);
                if (parent != null) go.transform.SetParent(parent, false);
                proc = go.AddComponent<ProceduralAnimator>();
                proc.Bind(bones);
            }

            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.9f, 0);
            col.height = 1.8f;
            col.radius = 0.35f;
            col.isTrigger = true;
            AddBlobShadow(go.transform);

            var view = go.AddComponent<AvatarView>();
            view.isNpc = isNpc;
            view.archetypeId = archetypeId;
            view.displayName = name;
            view.Init(animator, proc, body, lods, 1.75f);
            view.BabblePitch = isNpc ? 0.85f + (Mathf.Abs((archetypeId ?? "").GetHashCode()) % 40) / 100f : 1f;
            return view;
        }

        /// <summary>Deterministic NPC outfit from the archetype id until NPC prefabs exist.</summary>
        public static string[] NpcLook(string archetypeId)
        {
            var a = archetypeId ?? "npc";
            int h = Mathf.Abs(a.GetHashCode());
            return new[] { "skin_0" + (1 + h % 8), "head_01", "hair_" + a, "top_" + a, "bottom_" + (h % 3), "shoes_" + a, "acc_none" };
        }

        /// <summary>Blob shadow: a flat radial-gradient sprite (Build Spec: no realtime shadows, blob shadows).</summary>
        public static void AddBlobShadow(Transform parent, float size = 0.9f)
        {
            if (_blob == null)
            {
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "blob", wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        tex.SetPixel(x, y, new Color(0, 0, 0, a * a * 0.45f));
                    }
                tex.Apply(false, true);
                _blob = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / 1f);
            }
            var go = new GameObject("blob_shadow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, 0.02f, 0);
            go.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            go.transform.localScale = Vector3.one * size;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _blob;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
        }
    }
}
