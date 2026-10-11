using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>Builds player and NPC avatars: assembled parts when the art exists, the placeholder otherwise.</summary>
    public static class AvatarFactory
    {
        private static Sprite _blob;

        public static AvatarView Create(string name, string[] look, bool isNpc, string archetypeId = null, Transform parent = null, bool forPreview = false)
            => CreateResolved(name, look, isNpc, archetypeId, parent, forPreview, null);

        /// <summary>Server role/identity selects only explicitly curated FE presentation. Missing bindings keep the legacy route.</summary>
        public static AvatarView CreateNpc(string name, string npcId, string archetypeId, Transform parent = null,
            NpcCastCatalog catalog = null)
        {
            var registry = CharacterRegistry.Instance;
            // An explicit catalog supports a real-path development fixture without changing any resource asset.
            if (catalog == null) catalog = NpcCastCatalog.Instance;
            CharacterRegistry.Entry entry = null;
            string reason = "no-cast-catalog";
            if (catalog != null) catalog.TryResolve(npcId, archetypeId, registry, out entry, out reason);
            var view = CreateResolved(name, null, true, archetypeId, parent, false, entry);
            view.npcId = npcId;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            CTLife.Core.Log.Info("npc-cast", $"role={archetypeId}; selection={entry?.key ?? "legacy-fallback"}; reason={reason}");
#endif
            return view;
        }

        private static AvatarView CreateResolved(string name, string[] look, bool isNpc, string archetypeId,
            Transform parent, bool forPreview, CharacterRegistry.Entry selectedEntry)
        {
            GameObject go;
            Animator animator = null;
            ProceduralAnimator proc = null;
            SkinnedMeshRenderer body;
            Mesh[] lods = null;

            var resolvedLook = isNpc ? NpcLook(archetypeId) : look;
            float height = 1.75f;
            var reg = CharacterRegistry.Instance;
            var entry = selectedEntry ?? (reg == null ? null : isNpc ? reg.ForArchetype(archetypeId) : reg.ForLook(look));

            if (entry?.prefab != null)
            {
                // 1) Any Humanoid prefab from the registry (single mesh or pre-assembled).
                var source = forPreview && entry.previewPrefab != null ? entry.previewPrefab : entry.prefab;
                if (!forPreview && source.GetComponent<Animator>() != null)
                {
                    // Logical root (server/NavMesh-owned position, colliders, voice, name tag) above the visual model
                    // (Animator): seat/fall presentation moves only the model, never the actor (as the authored Snow world
                    // prefab is built; CafeSeatExitPresentation and SeatPresenter rely on it).
                    go = new GameObject(name);
                    if (parent != null) go.transform.SetParent(parent, false);
                    var model = Object.Instantiate(source, go.transform, false);
                    model.name = source.name;
                }
                else
                {
                    go = Object.Instantiate(source, parent);
                    go.name = name;
                }
                animator = go.GetComponentInChildren<Animator>();
                if (animator == null) animator = go.AddComponent<Animator>();
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                // Humanoid clips cannot pose a body without a valid Humanoid Avatar: it renders in its bind (T) pose.
                // CastValidation blocks such prefabs at build time; this makes any that slip through visible in logs.
                if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                    CTLife.Core.Log.Warn("avatar", $"'{entry.key}' ({entry.prefab.name}) has no valid Humanoid Avatar; it will T-pose");
#endif
                var ctrl = entry.controllerOverride != null ? entry.controllerOverride :
                    reg.controller != null ? reg.controller : ItemCatalog.AvatarController;
                if (ctrl != null) animator.runtimeAnimatorController = ctrl;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
                // Grounding/attention IK (feet on the real ground, look-at, hands on tables); near characters only.
                if (!forPreview && animator.avatar != null && animator.avatar.isHuman && animator.GetComponent<CharacterIK>() == null)
                    animator.gameObject.AddComponent<CharacterIK>();
                body = LargestSkinnedRenderer(go);
                height = entry.height > 0 ? entry.height : MeasureHeight(go, 1.75f);
            }
            else if (ItemCatalog.TryResolve(resolvedLook, out var basePrefab, out var partPrefabs))
            {
                // 2) Optional modular parts merged into one SkinnedMeshRenderer.
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
                // 3) Procedural placeholder (always available).
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
            view.Init(animator, proc, body, lods, height);
            if(!forPreview && entry?.prefab != null)
            {
                var runProfile=Resources.Load<SnowRunCorrectionProfile>(SnowRunCorrectionProfile.ResourcePath);
                if(runProfile!=null)view.BindRunCorrection(runProfile,entry.prefab);
            }
            view.BabblePitch = isNpc ? 0.85f + (Mathf.Abs((archetypeId ?? "").GetHashCode()) % 40) / 100f : 1f;
            return view;
        }

        private static SkinnedMeshRenderer LargestSkinnedRenderer(GameObject go)
        {
            SkinnedMeshRenderer best = null;
            int bestVerts = -1;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int v = r.sharedMesh != null ? r.sharedMesh.vertexCount : 0;
                if (v > bestVerts) { best = r; bestVerts = v; }
            }
            return best;
        }

        private static float MeasureHeight(GameObject go, float fallback)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return fallback;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float h = b.max.y - go.transform.position.y;
            return h > 0.5f && h < 4f ? h : fallback;
        }

        /// <summary>
        /// A solid capsule (Characters layer, kinematic) so the local player's CharacterController bumps into other
        /// people instead of walking through them. The root's trigger capsule stays the tap target.
        /// </summary>
        public static void AddBodyCollider(GameObject root)
        {
            if (root == null || root.transform.Find("body_collider") != null) return;
            var go = new GameObject("body_collider") { layer = CharacterLayers.Characters };
            go.transform.SetParent(root.transform, false);
            var c = go.AddComponent<CapsuleCollider>();
            c.center = new Vector3(0f, 0.88f, 0f);
            c.height = 1.72f;
            c.radius = 0.24f;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
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
                        tex.SetPixel(x, y, new Color(0, 0, 0, a * 0.50f));
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
