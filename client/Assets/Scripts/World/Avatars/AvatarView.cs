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
        public bool HasLodAssignment { get; private set; }
        /// <summary>Enabled renderers, not proof of frustum visibility or submitted draw calls.</summary>
        public int EnabledRendererCount
        {
            get
            {
                int count = 0;
                foreach (var renderer in _renderers)
                    if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy) count++;
                return count;
            }
        }
        public float BabblePitch { get; set; } = 1f;

        private Renderer[] _renderers = new Renderer[0];
        private bool[] _rendererEnabledAtInit = new bool[0];
        private string _baseAnim = "loco_idle";
        private string _baseState = "loco_idle";
        private string _baseSpot;
        private SnowRunCorrection _runCorrection;
        private CafeSeatMotionProfile _seatMotionProfile;
        private CafeSeatExitPresentation _seatExit;
        private CafeWorkExitGoals _workExitGoals;
        private CafeExitSettleContact _exitSettleContact;
        private bool _exitSettleEnabled;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private int _exitSettleApplyCalls;
        private double _exitSettleTotalMilliseconds, _exitSettleMaximumMilliseconds;
#endif
        private bool _workExitIdle;
        private string _exitDeferredOverlay;
        private float _exitDeferredOverlayAt;
        public bool SeatExitActive => _seatExit != null && _seatExit.Active;
        public Vector3 PresentationPosition => SeatExitActive ? _seatExit.Position : transform.position;
        public string SeatExitStage => _seatExit != null ? _seatExit.Stage : "None";
        private string _seatBridgeTarget;
        private bool _seatBridgeEntering;
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
            _renderers = GetComponentsInChildren<Renderer>(true);
            _rendererEnabledAtInit = new bool[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++) _rendererEnabledAtInit[i] = _renderers[i].enabled;

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

        private void OnEnable() { HasLodAssignment = false; All.Add(this); }
        private void OnDisable() { _runCorrection?.BeforeAnimation(); CancelSeatExitPresentation(false); All.Remove(this); }
        private void LateUpdate()
        {
            _runCorrection?.AfterAnimation();
            _seatExit?.ApplyPose();
            if (SeatExitActive && _exitSettleEnabled && _exitSettleContact != null && _exitSettleContact.Active)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                long settleStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                _exitSettleContact.Apply();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                double settleMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - settleStarted) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                _exitSettleApplyCalls++; _exitSettleTotalMilliseconds += settleMilliseconds;
                _exitSettleMaximumMilliseconds = System.Math.Max(_exitSettleMaximumMilliseconds, settleMilliseconds);
#endif
                if (_exitSettleContact.Failure != null)
                {
                    _exitSettleContact.End();
                    _exitSettleEnabled = false; // Do not repeatedly apply a failed bounded solve.
                    Debug.LogWarning("Cafe exit foot settle rejected: " + _exitSettleContact.Failure);
                }
            }
        }
        // A remote logical parent can move during Update. Re-pin before Animator/IK evaluates.
        internal void ReapplySeatExitPresentation() => _seatExit?.ApplyPose();
        private void OnApplicationPause(bool paused) { if (paused) CancelSeatExitPresentation(); }
        private void OnApplicationFocus(bool focused) { if (!focused) CancelSeatExitPresentation(); }

        /// <summary>Exact prefab/Avatar/clip profile; absent data leaves ordinary animation unchanged.</summary>
        public bool BindRunCorrection(SnowRunCorrectionProfile profile,GameObject sourcePrefab)
        {
            if(_runCorrection!=null||Animator==null||profile==null||!profile.Matches(Animator,sourcePrefab))return false;
            var component=Animator.gameObject.AddComponent<SnowRunCorrection>();
            if(!component.Bind(this,profile,sourcePrefab)){Destroy(component);return false;}
            _runCorrection=component;return true;
        }

        /// <summary>Called once during player initialization, before any seated state is selected.</summary>
        public void BindSeatMotionProfile(CafeSeatMotionProfile profile)
        {
            if (profile == null || Animator == null || _seatMotionProfile != null ||
                !profile.TryGetController(Animator, out var controller)) return;
            var baseline = Animator.runtimeAnimatorController;
            Animator.runtimeAnimatorController = controller;
            if (!Animator.HasState(0, AnimationLibrary.Hash(CafeSeatMotionProfile.SocialState)) ||
                !Animator.HasState(0, AnimationLibrary.Hash(CafeSeatMotionProfile.TypingState)))
            {
                Animator.runtimeAnimatorController = baseline;
                return;
            }
            _seatMotionProfile = profile;
            _seatExit = new CafeSeatExitPresentation(this);
        }

        public bool BeginSeatExitPresentation(string spotId, Vector3 seatedPosition, Quaternion seatedRotation)
        {
            if (_seatExit == null || !_seatMotionProfile.TryGetExit(_baseAnim, spotId, Animator, out var route, out var motion)) return false;
            return _seatExit.Begin(seatedPosition, seatedRotation, route, motion);
        }

        public void CancelSeatExitPresentation(bool restoreAnimation = true)
        {
            _exitDeferredOverlay = null;
            _exitSettleContact?.End();
            _seatExit?.Cancel(restoreAnimation);
        }

        internal bool RequestAuthoredWorkExit(Vector3 seat, Quaternion yaw, CafeWorkExitMotion motion)
        {
            if (Animator == null || !Animator.HasState(0, AnimationLibrary.Hash(CafeSeatMotionProfile.WorkExitState)) ||
                !Animator.HasState(0, AnimationLibrary.Hash(CafeSeatMotionProfile.WorkExitIdleState))) return false;
            if (motion == null || !motion.Valid) return false;
            _exitSettleEnabled = motion.settleContact;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _exitSettleApplyCalls = 0; _exitSettleTotalMilliseconds = _exitSettleMaximumMilliseconds = 0;
#endif
            if (_exitSettleEnabled && _exitSettleContact == null)
            {
                try { _exitSettleContact = new CafeExitSettleContact(Animator, GetComponentsInChildren<SkinnedMeshRenderer>(true)); }
                catch (System.ArgumentException) { return false; } // This profile must carry the inspected three-skin body.
            }
            if (_workExitGoals == null) _workExitGoals = Animator.gameObject.GetComponent<CafeWorkExitGoals>();
            if (_workExitGoals == null) _workExitGoals = Animator.gameObject.AddComponent<CafeWorkExitGoals>();
            if (!_workExitGoals.Begin(Animator, motion, seat, yaw)) return false;
            _seatBridgeTarget = null; _workExitIdle = false;
            Animator.CrossFadeInFixedTime(CafeSeatMotionProfile.WorkExitState, .12f / CafeWorkExitMotion.Rate, 0, 0f);
            return true;
        }
        internal bool ReadAuthoredExitTime(out float seconds, out bool complete)
        {
            seconds = _workExitGoals != null ? _workExitGoals.Seconds : 0f;
            complete = _workExitGoals != null && _workExitGoals.CallbackCount > 0 && seconds >= CafeWorkExitMotion.Length;
            return _workExitGoals != null && _workExitGoals.Running;
        }
        internal void AdvanceAuthoredExit()
        {
            if (_workExitGoals != null && _workExitGoals.Running && !_workExitIdle && _workExitGoals.Seconds >= .7f)
            {
                _workExitIdle = true;
                Animator.CrossFadeInFixedTime(CafeSeatMotionProfile.WorkExitIdleState, .16f / CafeWorkExitMotion.Rate, 0, 0f);
            }
        }
        internal void EndAuthoredExit()
        {
            if (_workExitGoals != null) _workExitGoals.End();
            _workExitIdle = false;
        }
        internal void PlaySeatExitState(string state, float fade)
        {
            _seatBridgeTarget = null;
            bool settling = _exitSettleEnabled && _exitSettleContact != null && state == "loco_idle" &&
                Animator.GetCurrentAnimatorStateInfo(0).IsName("loco_walk");
            _exitSettleContact?.End();
            if (AnimationLibrary.TryPlay(Animator, state, fade) && settling)
                _exitSettleContact.Begin(PresentationPosition.y);
        }
        internal void SeatExitPresentationFinished()
        {
            _exitSettleContact?.End();
            var action = _baseAnim; var spot = _baseSpot; _baseAnim = null;
            SetBase(action, spot);
            if (_exitDeferredOverlay != null)
            {
                var emote = _exitDeferredOverlay; float at = _exitDeferredOverlayAt; _exitDeferredOverlay = null;
                PlayOneShot(emote, at - Time.time);
            }
        }

        /// <summary>Locomotion or a looping act; spot context only selects local presentation.</summary>
        public void SetBase(string animId, string spotId = null)
        {
            if (string.IsNullOrEmpty(animId)) animId = "loco_idle";
            string state = _seatMotionProfile != null ? _seatMotionProfile.ResolveState(animId, spotId) : animId;
            // Store the canonical server action and latest seat even when two seats share a state.
            // Repeated updates must not restart the loop; an emote returns to this exact context.
            _baseSpot = spotId;
            if (animId == _baseAnim && state == _baseState) return;
            _baseAnim = animId;
            _baseState = state;
            if (SeatExitActive)
            {
                if (state.StartsWith("loco_", System.StringComparison.Ordinal)) return;
                _seatExit.Cancel(false); // A new authoritative action/seat owns presentation.
                _exitDeferredOverlay = null;
            }
            if (RouteFittedTypingTransition(state)) return;
            if (AnimationLibrary.TryPlay(Animator, state, 0.2f)) return;
            if (state != animId && AnimationLibrary.TryPlay(Animator, animId, 0.2f)) return;
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

        // Only the inspected Café profile uses this route. Direct typing↔standing blending
        // penetrates the floor on both fitted looks; the existing grounded social state bridges it.
        // This controls presentation only: canonical intent, actor position and movement stay owned
        // by LocalPlayerController/RemotePlayer. No per-frame IK, raycast or clip mutation is added.
        private bool RouteFittedTypingTransition(string state)
        {
            if (_seatMotionProfile == null || Animator == null || Animator.runtimeAnimatorController == null)
            { _seatBridgeTarget = null; return false; }
            if (!Animator.HasState(0, AnimationLibrary.Hash(state)))
            { _seatBridgeTarget = null; return false; } // Preserve the existing unknown-state fallback.
            if (Culled || !Animator.enabled)
            {
                _seatBridgeTarget = null;
                Animator.Play(AnimationLibrary.Hash(state), 0, 0f);
                return true; // Hidden poses need the latest state, not an old visible transition on resume.
            }
            bool hadBridge = _seatBridgeTarget != null;
            bool wasEntering = _seatBridgeEntering;
            bool locomotion = state.StartsWith("loco_", System.StringComparison.Ordinal);
            if (hadBridge && !wasEntering && locomotion)
            { _seatBridgeTarget = state; return true; } // Speed/intent changes do not restart a pending stand.
            _seatBridgeTarget = null;
            if (hadBridge && wasEntering && locomotion) return false; // Cancel entry from the current social blend.
            if (hadBridge && state == CafeSeatMotionProfile.SocialState && HeadingToSocial()) return true;
            if (state == CafeSeatMotionProfile.TypingState && !PlayingState(CafeSeatMotionProfile.TypingState))
            {
                if (Animator.GetCurrentAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.SocialState) && !Animator.IsInTransition(0)) return false;
                _seatBridgeTarget = state;
                _seatBridgeEntering = true;
                if (!HeadingToSocial())
                    AnimationLibrary.TryPlay(Animator, CafeSeatMotionProfile.SocialState, 0.2f);
                return true;
            }
            if (locomotion && PlayingState(CafeSeatMotionProfile.TypingState))
            {
                _seatBridgeTarget = state;
                _seatBridgeEntering = false;
                AnimationLibrary.TryPlay(Animator, CafeSeatMotionProfile.SocialState, 0.2f);
                return true;
            }
            return false;
        }

        private bool HeadingToSocial() => Animator.IsInTransition(0)
            ? Animator.GetNextAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.SocialState)
            : Animator.GetCurrentAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.SocialState);

        private bool PlayingState(string state) => Animator.GetCurrentAnimatorStateInfo(0).IsName(state) ||
            (Animator.IsInTransition(0) && Animator.GetNextAnimatorStateInfo(0).IsName(state));

        private void AdvanceSeatBridge()
        {
            if (_seatBridgeTarget == null || Animator == null) return;
            if (Animator.runtimeAnimatorController == null || !Animator.HasState(0, AnimationLibrary.Hash(_seatBridgeTarget)))
            { _seatBridgeTarget = null; return; }
            if (Culled || !Animator.enabled)
            {
                string hiddenTarget = _seatBridgeTarget;
                _seatBridgeTarget = null;
                Animator.Play(AnimationLibrary.Hash(hiddenTarget), 0, 0f);
                return;
            }
            // A just-requested crossfade might not be evaluated until the animation phase later
            // in this frame. Wait for the actual completed social state, never only a wall clock.
            if (Animator.IsInTransition(0) || !Animator.GetCurrentAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.SocialState)) return;
            string target = _seatBridgeTarget;
            _seatBridgeTarget = null;
            AnimationLibrary.TryPlay(Animator, target, 0.2f);
        }

        private static bool IsSeatedAct(string id) =>
            id == "act_sit" || id == "act_type" || id == "act_frantic_type" || id == "act_lean_back" || id == "act_head_in_hands";

        public void SetSpeed(float metresPerSecond)
        {
            if (SeatExitActive) metresPerSecond = _seatExit.Speed;
            if (Animator != null && Animator.runtimeAnimatorController != null) Animator.SetFloat(AnimationLibrary.SpeedParam, metresPerSecond);
            if (Procedural != null) Procedural.SetSpeed(metresPerSecond);
        }

        /// <summary>One-shot emote/react. startInSeconds lets emotes start at the server's `emoteAt`.</summary>
        public void PlayOneShot(string animId, float startInSeconds = 0f)
        {
            if (string.IsNullOrEmpty(animId)) return;
            if (SeatExitActive)
            {
                _exitDeferredOverlay = animId; _exitDeferredOverlayAt = Time.time + startInSeconds;
                return; // Preserve server timing; an expired one-shot is skipped on actual completion.
            }
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
            _seatBridgeTarget = null; // Cancel queued presentation; later changed base requests retain their existing interrupt behavior.
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
            _runCorrection?.BeforeAnimation();
            AdvanceSeatBridge();
            _seatExit?.Tick(Time.deltaTime);
            if (SeatExitActive) SetSpeed(_seatExit.Speed);
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
                var spot = _baseSpot;
                _baseAnim = null;
                SetBase(b, spot);
            }
        }

        public void ApplyLod(int tier, bool culled, float thermalScale)
        {
            HasLodAssignment = true;
            LodTier = tier;
            Culled = culled;
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = !culled && _rendererEnabledAtInit[i];
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
