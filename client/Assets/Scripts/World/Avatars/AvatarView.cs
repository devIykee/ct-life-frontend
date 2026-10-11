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
        /// <summary>Sit-down / stand-up presentation (SeatPresenter) for any look and any seat.</summary>
        public bool SeatTransitionActive => _seat != null && _seat.Active;
        /// <summary>Either seat presentation owns the visual model (input is queued until it ends).</summary>
        public bool SeatPresentationActive => SeatExitActive || SeatTransitionActive;
        /// <summary>A knockdown owns the Animator (ragdoll/fall/get-up): base, one-shot and LOD requests wait.</summary>
        public bool Suppressed { get; set; }

        /// <summary>Plays the latest base action again (after a knockdown, or a re-initialized Animator).</summary>
        public void RestoreBaseAnimation()
        {
            if (Animator != null && Animator.runtimeAnimatorController != null) { _rateFor = null; SetSpeed(_speed); }
            ReplayBase();
        }
        public Vector3 PresentationPosition => SeatExitActive ? _seatExit.Position : SeatTransitionActive ? _seat.Position : transform.position;
        public string SeatExitStage => _seatExit != null ? _seatExit.Stage : "None";
        private string _seatBridgeTarget;
        private bool _seatBridgeEntering;
        private string _overlayAnim;
        private float _overlayEndsAt;
        private float _pendingOverlayAt = -1f;
        private string _pendingOverlay;
        private SeatPresenter _seat;
        // Seat fit (SeatPlacement): the seated model offset ramps to its target through sit-down / stand-up.
        private static readonly int SeatHeightParam = Animator.StringToHash("seatHeight");
        private Vector3 _seatTarget, _seatApplied, _modelBaseLocal;
        private bool _hasModelBase;
        public Vector3 SeatOffset => _seatApplied;
        /// <summary>
        /// Keep the visual model where it is while the root jumps by <paramref name="worldDelta"/> (it eases back onto the
        /// root with the seat-offset ramp, τ 0.3 s): a hand-back that cannot put the root exactly under the body.
        /// </summary>
        public void CarryModelOffset(Vector3 worldDelta) => _seatApplied += transform.InverseTransformVector(worldDelta);

        /// <summary>The seat fit of the current seated state (invalid when not on a measured seat).</summary>
        public SeatPlacement.Result SeatFit { get; private set; }
        /// <summary>Seated and settled: the model rests at its fitted offset (no sit/stand transition or offset ramp).</summary>
        public bool SeatFitSettled => SeatFit.valid && !SeatPresentationActive && (_seatApplied - _seatTarget).sqrMagnitude < 1e-6f;
        // Speed-matched locomotion (LocomotionTuning) and presentation-only idle life.
        private static readonly int LocoRateParam = Animator.StringToHash("locoRate");
        private RuntimeAnimatorController _rateFor;
        private bool _hasLocoRate;
        private float _walkNative = 1f, _runNative = 5.7f;
        private float _speed;
        private float _nextIdleVariation;
        private float _lastYaw, _turnShuffleUntil;
        // Subtle variations only (Feel lane F1): the overhead stretch read as the character randomly throwing its arms up
        // ("when it raises its hands it feels very weird"); it stays a state for anything that asks for it explicitly.
        private static readonly string[] IdleVariations = { "idle_fidget_lookaround", "idle_fidget_foldarms" };
        public static System.Collections.Generic.IReadOnlyList<string> IdleVariationIds => IdleVariations;

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
            _seat = new SeatPresenter(this);
            if (animator != null && animator.transform.parent == transform) { _modelBaseLocal = animator.transform.localPosition; _hasModelBase = true; }
            _lastYaw = transform.eulerAngles.y;
            _nextIdleVariation = Time.time + Random.Range(7f, 15f);
        }

        /// <summary>
        /// Sit down onto a confirmed seat from where the actor stands now: step/turn onto the seat root, then the measured
        /// sit-down clip, then the seat's loop (the base anim set meanwhile). False when this look cannot present it.
        /// </summary>
        public bool BeginSeatEnter(Vector3 fromPosition, Quaternion fromRotation, Vector3 seatPosition, Quaternion seatRotation,
            string transitionState = "act_sit_down")
        {
            if (_seat == null || Animator == null || SeatExitActive) return false;
            if (transitionState != null && !Animator.HasState(0, AnimationLibrary.Hash(transitionState))) return false;
            float clip = transitionState == null ? 0f : LocomotionTuning.Instance != null ? LocomotionTuning.Instance.LengthOf(transitionState, 1.3f) : 1.3f;
            if (!_seat.BeginEnter(fromPosition, fromRotation, seatPosition, seatRotation, clip)) return false;
            _enterState = transitionState;
            _overlayAnim = null; _pendingOverlay = null; _seatBridgeTarget = null;
            if (_seat.Current == SeatPresenter.Stage.Align) AnimationLibrary.TryPlay(Animator, "loco_walk", 0.15f);
            else if (transitionState != null) AnimationLibrary.TryPlay(Animator, transitionState, 0.15f);
            return true;
        }
        private string _enterState = "act_sit_down";

        /// <summary>Stand up from a seat: the measured stand-up clip at the seat, then a step to the standing point.</summary>
        public bool BeginSeatLeave(Vector3 seatPosition, Quaternion seatRotation, Vector3 standPosition, Quaternion standRotation,
            string transitionState = "act_stand_up")
        {
            if (_seat == null || Animator == null || SeatExitActive) return false;
            if (transitionState != null && !Animator.HasState(0, AnimationLibrary.Hash(transitionState))) return false;
            float clip = transitionState == null ? 0f : LocomotionTuning.Instance != null ? LocomotionTuning.Instance.LengthOf(transitionState, 1.0f) : 1.0f;
            if (!_seat.BeginLeave(seatPosition, seatRotation, standPosition, standRotation, clip)) return false;
            _overlayAnim = null; _pendingOverlay = null; _seatBridgeTarget = null;
            if (transitionState != null) AnimationLibrary.TryPlay(Animator, transitionState, 0.12f);
            else AnimationLibrary.TryPlay(Animator, "loco_walk", 0.15f);
            return true;
        }

        public void CancelSeatTransition()
        {
            if (!SeatTransitionActive) return;
            _seat.Cancel();
            ReplayBase();
        }

        private void ReplayBase()
        {
            var action = _baseAnim; var spot = _baseSpot; _baseAnim = null;
            SetBase(action, spot);
        }

        private void OnEnable() { HasLodAssignment = false; All.Add(this); }
        private void OnDisable() { _runCorrection?.BeforeAnimation(); CancelSeatExitPresentation(false); All.Remove(this); }
        private void LateUpdate()
        {
            _runCorrection?.AfterAnimation();
            _seatExit?.ApplyPose();
            // Ramp the seat fit (τ 0.3 s): lands as the sit-down clip settles; eases out through the stand-up.
            _seatApplied = Vector3.Lerp(_seatApplied, _seatTarget, 1f - Mathf.Exp(-Time.deltaTime / 0.3f));
            if (_seat != null) _seat.Offset = _seatApplied;
            _seat?.Apply();
            if (_hasModelBase && !SeatExitActive && !SeatTransitionActive && Animator != null)
                Animator.transform.localPosition = _modelBaseLocal + _seatApplied;
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
            if (controller != baseline) Animator.runtimeAnimatorController = controller; // cast looks keep the shared one
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
            // Start from where the body actually sits (the fitted pose's measured offset on this look).
            return _seatExit.Begin(seatedPosition + seatedRotation * _seatApplied, seatedRotation, route, motion);
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
                // The sole settle is inspected on the original three-skin Snow body only; the cast looks (one skin per LOD,
                // optimized bones) take the same authored exit without that 1.5 cm sole correction instead of no exit.
                try { _exitSettleContact = new CafeExitSettleContact(Animator, GetComponentsInChildren<SkinnedMeshRenderer>(true)); }
                catch (System.ArgumentException) { _exitSettleEnabled = false; }
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
            // Measured seat: the pose for its kind (or the fitted café state), its blend height and the body offset.
            if (!state.StartsWith("loco_", System.StringComparison.Ordinal) && !string.IsNullOrEmpty(spotId))
            {
                var place = SeatPlacement.Resolve(Animator, animId, spotId, state != animId ? state : null);
                if (place.valid)
                {
                    if (Animator.HasState(0, AnimationLibrary.Hash(place.state))) state = place.state;
                    _seatTarget = place.offset;
                    Animator.SetFloat(SeatHeightParam, place.seatHeight);
                }
                else _seatTarget = Vector3.zero;
                SeatFit = place;
            }
            else { _seatTarget = Vector3.zero; SeatFit = default; }
            if (Suppressed) { _baseAnim = animId; _baseState = state; _baseSpot = spotId; return; } // replayed after the get-up
            // Store the canonical server action and latest seat even when two seats share a state.
            // Repeated updates must not restart the loop; an emote returns to this exact context.
            _baseSpot = spotId;
            if (animId == _baseAnim && state == _baseState) return;
            _baseAnim = animId;
            _baseState = state;
            if (SeatTransitionActive)
            {
                // Sitting down defers the seat loop; standing up defers locomotion. Anything else (a server stand while
                // sitting down, a new seat while standing up) ends the presentation and plays now.
                bool loco = state.StartsWith("loco_", System.StringComparison.Ordinal);
                if (_seat.Entering ? !loco : loco) return;
                _seat.Cancel();
            }
            if (SeatExitActive)
            {
                if (state.StartsWith("loco_", System.StringComparison.Ordinal)) return;
                _seatExit.Cancel(false); // A new authoritative action/seat owns presentation.
                _exitDeferredOverlay = null;
            }
            if (RouteFittedTypingTransition(state)) return;
            if (state == "loco_idle" && _runStopPendingAt >= 0f)
            {
                bool due = Time.time - _runStopPendingAt < 0.3f;
                _runStopPendingAt = -1f;
                if (due && RunStopAllowed()) { PlayOneShot("loco_run_stop"); return; } // returns to this idle when it ends
            }
            // loco_walk and loco_run play the same speed-blended Locomotion tree: switching between them must not
            // restart its cycle (a crossfade restarts the target at phase 0 and freezes the legs near the crossover).
            if (SameLocomotionTree(state)) return;
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
            else if (SeatTransitionActive) metresPerSecond = _seat.Speed;
            if (Time.time < _turnShuffleUntil && metresPerSecond < TurnShuffleSpeed) metresPerSecond = TurnShuffleSpeed;
            RunStop(metresPerSecond);
            _speed = metresPerSecond;
            if (Animator != null && Animator.runtimeAnimatorController != null)
            {
                if (_rateFor != Animator.runtimeAnimatorController) ResolveLocoRate();
                if (_hasLocoRate)
                {
                    // Per-frame displacement speed is noisy; damp what the blend sees (0.1 s), then match the cadence to it.
                    Animator.SetFloat(AnimationLibrary.SpeedParam, metresPerSecond, 0.1f, Mathf.Max(Time.deltaTime, 1e-4f));
                    float blended = Animator.GetFloat(AnimationLibrary.SpeedParam);
                    // Feet planted: the Locomotion tree plays at speed / native ground speed (LocomotionTuning).
                    Animator.SetFloat(LocoRateParam, LocomotionTuning.Rate(blended, _walkNative, _runNative));
                }
                else Animator.SetFloat(AnimationLibrary.SpeedParam, metresPerSecond);
            }
            if (Procedural != null) Procedural.SetSpeed(metresPerSecond);
        }

        private static readonly int WalkHash = Animator.StringToHash("loco_walk"), RunHash = Animator.StringToHash("loco_run");
        private static readonly int RunStopHash = Animator.StringToHash("loco_run_stop");
        private const float RunStopFrom = 3.4f;
        private float _lastRunAt = -1f;

        private float _runStopPendingAt = -1f;

        /// <summary>
        /// A run that ends within ~0.45 s plays the decelerating stop steps once (CMU JogStop), then the idle. Owners set
        /// the speed and the idle base in either order: whichever comes second starts the stop.
        /// </summary>
        private void RunStop(float metresPerSecond)
        {
            if (metresPerSecond >= RunStopFrom) { _lastRunAt = Time.time; return; }
            if (_lastRunAt < 0f || metresPerSecond > 0.15f) return;
            bool recent = Time.time - _lastRunAt < 0.45f;
            _lastRunAt = -1f;
            if (!recent || !RunStopAllowed()) return;
            if (_baseState == "loco_idle") PlayOneShot("loco_run_stop");
            else _runStopPendingAt = Time.time; // SetBase("loco_idle") follows this frame
        }

        private bool RunStopAllowed() => !Suppressed && !SeatPresentationActive && _overlayAnim == null && !Culled && Animator != null &&
                                         Animator.runtimeAnimatorController != null && Animator.HasState(0, RunStopHash);

        private bool SameLocomotionTree(string state)
        {
            if (!_hasLocoRate || Animator == null || !Animator.isActiveAndEnabled || (state != "loco_walk" && state != "loco_run")) return false;
            var info = Animator.IsInTransition(0) ? Animator.GetNextAnimatorStateInfo(0) : Animator.GetCurrentAnimatorStateInfo(0);
            return info.shortNameHash == WalkHash || info.shortNameHash == RunHash;
        }

        private void ResolveLocoRate()
        {
            _rateFor = Animator.runtimeAnimatorController;
            _hasLocoRate = false;
            foreach (var p in Animator.parameters) if (p.nameHash == LocoRateParam) { _hasLocoRate = true; break; }
            var tuning = LocomotionTuning.Instance;
            if (tuning != null) (_walkNative, _runNative) = tuning.NativeFor(_rateFor);
        }

        /// <summary>One-shot emote/react. startInSeconds lets emotes start at the server's `emoteAt`.</summary>
        public void PlayOneShot(string animId, float startInSeconds = 0f)
        {
            if (string.IsNullOrEmpty(animId) || Suppressed) return;
            if (SeatTransitionActive)
            {
                _pendingOverlay = animId; _pendingOverlayAt = Time.time + startInSeconds;
                return; // played once the actor has sat down / stood up
            }
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
            // Registry anims carry their duration; FE presentation states use their measured clip length.
            float duration = def != null ? def.durationMs / 1000f : LocomotionTuning.Instance != null ? LocomotionTuning.Instance.LengthOf(animId, 2f) : 2f;
            // Late joiners: skip the part that already happened.
            if (startInSeconds < 0f) duration += startInSeconds;
            if (duration <= 0.05f) return;
            _seatBridgeTarget = null; // Cancel queued presentation; later changed base requests retain their existing interrupt behavior.
            _overlayAnim = animId;
            _overlayEndsAt = Time.time + duration;
            if (AnimationLibrary.TryPlay(Animator, animId, 0.1f)) { Hands.Begin(Animator, animId); return; }
            Procedural?.PlayOverlay(animId, duration, false);
        }

        /// <summary>Emote finger poses (HandPoses "Hands" layer).</summary>
        public readonly HandPoses.Driver Hands = new HandPoses.Driver();

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
            TickSeatTransition();
            TickIdleLife();
            if (_pendingOverlay != null && Time.time >= _pendingOverlayAt && !SeatTransitionActive)
            {
                var id = _pendingOverlay;
                _pendingOverlay = null;
                PlayOneShot(id);
            }
            Hands.Tick(Animator, Time.deltaTime);
            if (_overlayAnim != null && Time.time >= _overlayEndsAt)
            {
                _overlayAnim = null;
                Hands.End();
                // Return the Animator to the base state after a one-shot.
                var b = _baseAnim;
                var spot = _baseSpot;
                _baseAnim = null;
                SetBase(b, spot);
            }
        }

        private void TickSeatTransition()
        {
            if (!SeatTransitionActive) return;
            var began = _seat.Tick(Time.deltaTime);
            if (began == SeatPresenter.Stage.SitDown && _enterState != null) AnimationLibrary.TryPlay(Animator, _enterState, 0.15f);
            else if (began == SeatPresenter.Stage.Step) AnimationLibrary.TryPlay(Animator, "loco_walk", 0.18f);
            if (SeatTransitionActive) SetSpeed(_seat.Speed);
            else ReplayBase(); // finished: the deferred seat loop / locomotion takes over
        }

        private const float TurnShuffleSpeed = 0.7f;

        /// <summary>
        /// Presentation-only life: an occasional idle variation while standing idle (LOD0, not talking), and stepping
        /// feet while turning on the spot (a quick yaw change at standstill shuffles at walk cadence instead of skating).
        /// </summary>
        private void TickIdleLife()
        {
            float yaw = transform.eulerAngles.y;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float yawRate = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw)) / dt;
            _lastYaw = yaw;
            if (Animator == null || Animator.runtimeAnimatorController == null || Culled || SeatExitActive || SeatTransitionActive || Suppressed) return;
            bool idleBase = _baseState == "loco_idle" && _overlayAnim == null;
            if (idleBase && yawRate > 150f && _speed < TurnShuffleSpeed)
            {
                if (Time.time >= _turnShuffleUntil) AnimationLibrary.TryPlay(Animator, "loco_walk", 0.1f);
                _turnShuffleUntil = Time.time + 0.18f;
                SetSpeed(_speed);
            }
            else if (_turnShuffleUntil > 0f && Time.time >= _turnShuffleUntil)
            {
                _turnShuffleUntil = 0f;
                if (idleBase) AnimationLibrary.TryPlay(Animator, "loco_idle", 0.2f);
            }
            if (!idleBase || Talking || LodTier > 0 || _speed > 0.1f || Time.time < _nextIdleVariation) return;
            _nextIdleVariation = Time.time + Random.Range(14f, 28f);
            var id = IdleVariations[Random.Range(0, IdleVariations.Length)];
            if (!Animator.HasState(0, AnimationLibrary.Hash(id))) return;
            float length = LocomotionTuning.Instance != null ? LocomotionTuning.Instance.LengthOf(id, 3f) : 3f;
            _overlayAnim = id;
            _overlayEndsAt = Time.time + Mathf.Min(length, 6f) - 0.25f;
            AnimationLibrary.TryPlay(Animator, id, 0.35f);
        }

        private LODGroup _lodGroup;
        private bool _lodGroupChecked;

        public void ApplyLod(int tier, bool culled, float thermalScale)
        {
            HasLodAssignment = true;
            LodTier = tier;
            Culled = culled;
            if (culled && SeatTransitionActive) CancelSeatTransition(); // hidden actors take their final pose now
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = !culled && _rendererEnabledAtInit[i];
            if (PlaceholderLods != null && Body != null && tier < PlaceholderLods.Length && Body.sharedMesh != PlaceholderLods[tier])
                Body.sharedMesh = PlaceholderLods[tier];
            // Cast prefabs carry an LODGroup (LOD0/1/2): the crowd rank picks the tier, not screen size.
            if (_lodGroup == null && !_lodGroupChecked) { _lodGroupChecked = true; _lodGroup = GetComponentInChildren<LODGroup>(); }
            if (_lodGroup != null) _lodGroup.ForceLOD(culled ? -1 : Mathf.Min(tier, _lodGroup.lodCount - 1));
            int interval = tier == 0 ? 1 : tier == 1 ? 2 : 4;
            if (thermalScale < 1f) interval *= 2;
            if (Procedural != null)
            {
                Procedural.enabled = !culled;
                Procedural.UpdateInterval = interval;
            }
            if (Animator != null && !Suppressed) // a ragdoll keeps its Animator off until the get-up
            {
                Animator.cullingMode = AnimatorCullingMode.CullCompletely;
                Animator.enabled = !culled;
                Animator.speed = 1f;
            }
        }
    }
}
