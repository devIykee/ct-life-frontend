using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// A whole-look outfit worn over an actor's own look (2026-10-25: the Rekt Burger crew uniform, cast looks
    /// player_m/f_rekt_crew). The outfit's prefab becomes the visual model under the same actor; the actor's own model stays
    /// as a hidden sibling so taking the outfit off is instant (no rebuild, the palette material kept).
    /// </summary>
    public sealed class AvatarOutfit : MonoBehaviour
    {
        public string Key { get; private set; }
        private GameObject _own, _worn;

        public static AvatarOutfit Of(AvatarView view) => view != null ? view.GetComponent<AvatarOutfit>() : null;
        public static string Worn(AvatarView view) => Of(view)?.Key;

        /// <summary>Wear the registry entry's prefab. False for actors without a separate visual model (likeness, legacy).</summary>
        public static bool Wear(AvatarView view, CharacterRegistry.Entry entry, System.Action<GameObject> dress = null)
        {
            if (view == null || entry?.prefab == null || view.Animator == null) return false;
            var outfit = view.GetComponent<AvatarOutfit>() ?? view.gameObject.AddComponent<AvatarOutfit>();
            if (outfit.Key == entry.key && outfit._worn != null) return true;
            if (outfit._worn != null) outfit.TakeOff(view, rebind: true);
            var own = view.Animator.gameObject;
            if (own.transform.parent != view.transform) return false;
            var model = Instantiate(entry.prefab, view.transform, false);
            model.name = entry.prefab.name;
            model.transform.localPosition = own.transform.localPosition;
            model.transform.localRotation = own.transform.localRotation;
            var animator = model.GetComponent<Animator>() ?? model.GetComponentInChildren<Animator>();
            if (animator == null) { Destroy(model); return false; }
            var ctrl = entry.controllerOverride != null ? entry.controllerOverride
                : CharacterRegistry.Instance != null && CharacterRegistry.Instance.controller != null ? CharacterRegistry.Instance.controller : ItemCatalog.AvatarController;
            if (ctrl != null) animator.runtimeAnimatorController = ctrl;
            animator.applyRootMotion = false;
            animator.cullingMode = view.Animator.cullingMode;
            bool hadIk = own.GetComponent<CharacterIK>() != null;
            own.SetActive(false);
            if (hadIk && animator.avatar != null && animator.avatar.isHuman) animator.gameObject.AddComponent<CharacterIK>();
            var updateOffscreen = view.Body != null && view.Body.updateWhenOffscreen;
            SkinnedMeshRenderer body = null; int verts = -1;
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.updateWhenOffscreen = updateOffscreen;
                int v = r.sharedMesh != null ? r.sharedMesh.vertexCount : 0;
                if (v > verts) { verts = v; body = r; }
            }
            dress?.Invoke(model);
            outfit._own = own; outfit._worn = model; outfit.Key = entry.key;
            view.ReplaceModel(animator, body);
            Debug.Log("CTLIFE [outfit] " + view.name + " wears " + entry.key);
            return true;
        }

        /// <summary>Back to the actor's own look.</summary>
        public static void Restore(AvatarView view) => Of(view)?.TakeOff(view, rebind: true);

        private void TakeOff(AvatarView view, bool rebind)
        {
            if (_worn == null) { Key = null; return; }
            var own = _own;
            Destroy(_worn);
            _worn = null; Key = null; _own = null;
            if (own == null) return;
            own.SetActive(true);
            if (rebind)
            {
                var animator = own.GetComponent<Animator>();
                SkinnedMeshRenderer body = null; int verts = -1;
                foreach (var r in own.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    int v = r.sharedMesh != null ? r.sharedMesh.vertexCount : 0;
                    if (v > verts) { verts = v; body = r; }
                }
                view.ReplaceModel(animator, body);
                Debug.Log("CTLIFE [outfit] " + view.name + " back in its own look");
            }
        }
    }
}
