using System.Collections.Generic;
using CTLife.World;
using UnityEngine;

namespace CTLife.WorldLife
{
    /// <summary>
    /// Puts an archetype's props on an NPC (archetype_looks.json): a prefab from Resources/WorldLife/Props/&lt;id&gt; when
    /// one exists, otherwise a placeholder primitive. Props follow the avatar's culling (AvatarView.Culled) and the
    /// drone body mode hides the humanoid mesh. One material per colour, shared; no colliders; no shadows.
    /// </summary>
    public sealed class PropDresser : MonoBehaviour
    {
        private static readonly Dictionary<int, Material> Materials = new Dictionary<int, Material>();
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Transform> _floating = new List<Transform>();
        private readonly List<Vector3> _floatBase = new List<Vector3>();
        private AvatarView _view;
        private bool _shown = true;
        private float _nextCullCheck;
        public string ArchetypeId { get; private set; }

        public static PropDresser Dress(AvatarView view, ArchetypeLook look)
        {
            if (view == null || look == null) return null;
            var d = view.GetComponent<PropDresser>();
            if (d != null && d.ArchetypeId == look.Id) return d;
            if (d == null) d = view.gameObject.AddComponent<PropDresser>();
            d.Apply(view, look);
            return d;
        }

        private void Apply(AvatarView view, ArchetypeLook look)
        {
            Clear();
            _view = view;
            ArchetypeId = look.Id;
            if (!string.IsNullOrEmpty(look.Name) && (string.IsNullOrEmpty(view.displayName) || view.displayName == look.Id || view.displayName == view.archetypeId))
                view.displayName = look.Name;
            if (look.Body == "drone" || look.Body == "hidden")
                foreach (var r in view.GetComponentsInChildren<Renderer>(true))
                    if (r is SkinnedMeshRenderer || r.gameObject.name == "blob_shadow" && look.Body == "hidden") r.forceRenderingOff = true;
            if (look.BodyScale > 0.1f && look.BodyScale < 3f && Mathf.Abs(look.BodyScale - 1f) > 0.01f && look.Body == "humanoid")
                view.transform.localScale = Vector3.one * look.BodyScale;
            foreach (var spec in look.Props) Attach(view, spec, look.Body == "drone");
        }

        private void Attach(AvatarView view, PropSpec spec, bool drone)
        {
            var anchor = AnchorFor(view, spec.Attach, out var fallbackOffset);
            GameObject go;
            var prefab = string.IsNullOrEmpty(spec.Id) ? null : Resources.Load<GameObject>("WorldLife/Props/" + spec.Id);
            if (prefab != null) go = Instantiate(prefab);
            else
            {
                go = GameObject.CreatePrimitive(Shape(spec.Shape));
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = Material(spec.Color, spec.Emissive);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            go.name = "prop_" + (spec.Id ?? "x");
            var root = view.transform;
            // Offsets are authored in avatar space (x right, y up, z forward); convert into the anchor's space.
            var worldPos = anchor.position + root.rotation * (fallbackOffset + new Vector3(spec.OX, spec.OY, spec.OZ));
            go.transform.SetParent(anchor, true);
            go.transform.position = worldPos;
            go.transform.rotation = root.rotation;
            var parentScale = anchor.lossyScale;
            var size = prefab != null ? Vector3.one : new Vector3(spec.SX, spec.SY, spec.SZ);
            go.transform.localScale = new Vector3(size.x / Mathf.Max(0.0001f, parentScale.x), size.y / Mathf.Max(0.0001f, parentScale.y),
                size.z / Mathf.Max(0.0001f, parentScale.z));
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) _renderers.Add(r);
            if (drone && spec.Attach == "above")
            {
                _floating.Add(go.transform);
                _floatBase.Add(go.transform.localPosition);
            }
        }

        private static Transform AnchorFor(AvatarView view, string attach, out Vector3 fallbackOffset)
        {
            fallbackOffset = Vector3.zero;
            var anim = view.Animator;
            Transform bone = null;
            bool humanoid = anim != null && anim.isHuman && anim.avatar != null;
            switch (attach)
            {
                case "head":
                    bone = humanoid ? anim.GetBoneTransform(HumanBodyBones.Head) : Find(view.transform, "Head");
                    if (bone == null) fallbackOffset = new Vector3(0, 1.62f, 0);
                    break;
                case "hand_r":
                    bone = humanoid ? anim.GetBoneTransform(HumanBodyBones.RightHand) : Find(view.transform, "hand_r");
                    if (bone == null) fallbackOffset = new Vector3(-0.28f, 0.9f, 0.05f);
                    break;
                case "hand_l":
                    bone = humanoid ? anim.GetBoneTransform(HumanBodyBones.LeftHand) : Find(view.transform, "hand_l");
                    if (bone == null) fallbackOffset = new Vector3(0.28f, 0.9f, 0.05f);
                    break;
                case "chest":
                case "back":
                    if (humanoid)
                    {
                        bone = anim.GetBoneTransform(HumanBodyBones.UpperChest);
                        if (bone == null) bone = anim.GetBoneTransform(HumanBodyBones.Chest);
                    }
                    else bone = Find(view.transform, "spine_03");
                    if (bone == null) fallbackOffset = new Vector3(0, 1.3f, 0);
                    break;
                default: // above, floor
                    return view.transform;
            }
            return bone != null ? bone : view.transform;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static PrimitiveType Shape(string s)
        {
            switch (s)
            {
                case "cylinder": return PrimitiveType.Cylinder;
                case "sphere": return PrimitiveType.Sphere;
                case "capsule": return PrimitiveType.Capsule;
                case "quad": return PrimitiveType.Quad;
                default: return PrimitiveType.Cube;
            }
        }

        public static Material Material(Rgb rgb, bool emissive)
        {
            var c = new Color(rgb.R, rgb.G, rgb.B, 1f);
            int key = ((int)(c.r * 255) << 16) | ((int)(c.g * 255) << 8) | (int)(c.b * 255) | (emissive ? 1 << 24 : 0);
            if (Materials.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Mats.LitShader) { name = "wl_prop_" + key.ToString("X7"), enableInstancing = true };
            if (m.HasProperty(BaseColor)) m.SetColor(BaseColor, c); else m.color = c;
            if (emissive && m.HasProperty(EmissionColor))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor(EmissionColor, c * 1.5f);
            }
            Materials[key] = m;
            return m;
        }

        private void LateUpdate()
        {
            if (_view == null) return;
            float now = Time.unscaledTime;
            if (now >= _nextCullCheck)
            {
                _nextCullCheck = now + 0.25f;
                bool show = !_view.Culled;
                if (show != _shown)
                {
                    _shown = show;
                    foreach (var r in _renderers) if (r != null) r.enabled = show;
                }
            }
            if (!_shown) return;
            for (int i = 0; i < _floating.Count; i++)
                if (_floating[i] != null)
                    _floating[i].localPosition = _floatBase[i] + Vector3.up * (Mathf.Sin(now * 3.1f + i) * 0.03f);
        }

        private void Clear()
        {
            foreach (var r in _renderers) if (r != null) Destroy(r.gameObject);
            _renderers.Clear();
            _floating.Clear();
            _floatBase.Clear();
        }
    }
}
