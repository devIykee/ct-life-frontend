using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Root of every player or NPC avatar. Plays anims.json ids on a real Animator when its controller has a state
    /// with that name, else on the <see cref="ProceduralAnimator"/> placeholder. Holds the voice/babble AudioSource,
    /// the head anchor for bubbles and name tags, and the speaking indicator. Registered in <see cref="All"/> so the
    /// UI overlay can draw name tags without per-avatar canvases.
    /// </summary>
    public sealed class AvatarView : MonoBehaviour
    {
        public static readonly List<AvatarView> All = new List<AvatarView>();

        public string userId;
        public string sessionId;
        public string npcId;
        public string archetypeId;
        public string displayName;
        public bool isNpc;
        public bool isLocal;
        public bool isSeeker;
        public string minerTier = "none";
        public string tribeId;

        public Transform HeadAnchor { get; private set; }
        public AudioSource Voice { get; private set; }
        public Animator Animator { get; private set; }
        public ProceduralAnimator Procedural { get; private set; }
        public SkinnedMeshRenderer Body { get; private set; }
        public Mesh[] PlaceholderLods { get; private set; }
        public bool Talking { get; private set; }
        public int LodTier { get; set; }
        public bool Culled { get; set; }
        public float BabblePitch { get; set; } = 1f;

        private string _baseAnim = "loco_idle";
        private string _overlayAnim;
        private float _overlayEndsAt;
        private float _pendingOverlayAt = -1f;
        private string _pendingOverlay;

        public void Init(Animator animator, ProceduralAnimator procedural, SkinnedMeshRenderer body, Mesh[] lods, float height)
        {
            Animator = animator;
            Procedural = procedural;
            Body = body;
            PlaceholderLods = lods;

            HeadAnchor = new GameObject("head_anchor").transform;
            HeadAnchor.SetParent(transform, false);
            HeadAnchor.localPosition = new Vector3(0, height + 0.25f, 0);

            var voiceGo = new GameObject("voice");
            voiceGo.transform.SetParent(transform, false);
            voiceGo.transform.localPosition = new Vector3(0, height - 0.15f, 0);
            Voice = voiceGo.AddComponent<AudioSource>();
            Voice.playOnAwake = false;
            Voice.spatialBlend = 1f;
            Voice.rolloffMode = AudioRolloffMode.Linear;
            Voice.minDistance = 1.5f;
            Voice.maxDistance = 8f; // Build Spec: hear players within 8 m, quieter with distance.
            Voice.dopplerLevel = 0f;
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        /// <summary>Locomotion or a looping act (server-set PlayerState.anim, NPC act).</summary>
        public void SetBase(string animId)
        {
            if (string.IsNullOrEmpty(animId)) animId = "loco_idle";
            if (animId == _baseAnim) return;
            _baseAnim = animId;
            if (AnimationLibrary.TryPlay(Animator, animId, 0.2f)) return;
            if (Procedural != null)
            {
                var def = AnimationLibrary.Def(animId);
                if (def == null || def.kind == AnimKind.Loco || IsSeatedAct(animId)) Procedural.SetBase(animId);
                else
                {
                    Procedural.SetBase("loco_idle");
                    Procedural.PlayOverlay(animId, 0f, true);
                }
            }
        }

        private static bool IsSeatedAct(string id) =>
            id == "act_sit" || id == "act_type" || id == "act_frantic_type" || id == "act_lean_back" || id == "act_head_in_hands";

        public void SetSpeed(float metresPerSecond)
        {
            if (Animator != null && Animator.runtimeAnimatorController != null) Animator.SetFloat(AnimationLibrary.SpeedParam, metresPerSecond);
            if (Procedural != null) Procedural.SetSpeed(metresPerSecond);
        }

        /// <summary>One-shot emote/react. startInSeconds lets emotes start at the server's `emoteAt`.</summary>
        public void PlayOneShot(string animId, float startInSeconds = 0f)
        {
            if (string.IsNullOrEmpty(animId)) return;
            if (startInSeconds > 0.02f)
            {
                _pendingOverlay = animId;
                _pendingOverlayAt = Time.time + startInSeconds;
                return;
            }
            var def = AnimationLibrary.Def(animId);
            float duration = def != null ? def.durationMs / 1000f : 2f;
            // Late joiners: skip the part that already happened.
            if (startInSeconds < 0f) duration += startInSeconds;
            if (duration <= 0.05f) return;
            _overlayAnim = animId;
            _overlayEndsAt = Time.time + duration;
            if (AnimationLibrary.TryPlay(Animator, animId, 0.1f)) return;
            Procedural?.PlayOverlay(animId, duration, false);
        }

        public void SetTalking(bool on)
        {
            Talking = on;
        }

        private void Update()
        {
            if (_pendingOverlay != null && Time.time >= _pendingOverlayAt)
            {
                var id = _pendingOverlay;
                _pendingOverlay = null;
                PlayOneShot(id);
            }
            if (_overlayAnim != null && Time.time >= _overlayEndsAt)
            {
                _overlayAnim = null;
                // Return the Animator to the base state after a one-shot.
                var b = _baseAnim;
                _baseAnim = null;
                SetBase(b);
            }
        }

        public void ApplyLod(int tier, bool culled, float thermalScale)
        {
            LodTier = tier;
            Culled = culled;
            if (Body != null) Body.enabled = !culled;
            if (PlaceholderLods != null && Body != null && tier < PlaceholderLods.Length && Body.sharedMesh != PlaceholderLods[tier])
                Body.sharedMesh = PlaceholderLods[tier];
            int interval = tier == 0 ? 1 : tier == 1 ? 2 : 4;
            if (thermalScale < 1f) interval *= 2;
            if (Procedural != null)
            {
                Procedural.enabled = !culled;
                Procedural.UpdateInterval = interval;
            }
            if (Animator != null)
            {
                Animator.cullingMode = AnimatorCullingMode.CullCompletely;
                Animator.enabled = !culled;
                Animator.speed = 1f;
            }
        }
    }
}
