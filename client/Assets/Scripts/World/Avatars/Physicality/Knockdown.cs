using System;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>Whoever owns an actor's root (LocalPlayerController, NpcController) lets a knockdown move it and pause control.</summary>
    public interface IKnockdownOwner
    {
        void PlaceRoot(Vector3 position, Quaternion rotation);
        void SetControl(bool enabled);
    }

    /// <summary>
    /// Stumbles and falls with a get-up, GTA-style but mobile-cheap:
    /// - Stagger: a recoil one-shot and ~0.55 s without control (a hard bump, being run into).
    /// - Fall: a physics ragdoll (RagdollRig; LOD0, within 14 m of the camera, at most 2 at once), else the authored
    ///   fall clip. When the body rests: a face-down body rolls onto its back unless a face-down get-up exists; then the
    ///   root is re-placed under the body so the get-up clip's first (lying) pose matches it, every bone blends from the
    ///   ragdoll's rest pose into the clip over 0.45 s, and control returns as the actor stands.
    /// Presentation only: the authoritative position is re-sent by the owner after it moves the root.
    /// </summary>
    public sealed class Knockdown : MonoBehaviour
    {
        public enum Phase { None, Stagger, Falling, Rolling, Down, GettingUp }
        public const float RagdollRange = 14f, BlendTime = 0.45f;
        private const string GetUp = "act_get_up", GetUpFront = "act_get_up_front", FallBack = "react_fall", Knockback = "react_stagger", Recoil = "react_hit_chest";

        public Phase Current { get; private set; }
        public bool Busy => Current != Phase.None;
        public bool Ragdolled => _rig != null;
        /// <summary>How the last get-up began (diagnostics/evidence): face up/down, rolled first, which clip.</summary>
        public string LastGetUp { get; private set; }
        public static event Action<AvatarView, bool> Fell; // (actor, ragdoll)

        private AvatarView _view;
        private IKnockdownOwner _owner;
        private RagdollRig _rig;
        private float _t, _len;
        private string _getUpState;
        private bool _rolled;
        private Transform[] _bones;
        private Quaternion[] _snapshot;
        private Vector3 _snapHips;
        private float _blend = -1f;

        public static Knockdown For(AvatarView view)
        {
            if (view == null) return null;
            var k = view.GetComponent<Knockdown>();
            if (k == null) { k = view.gameObject.AddComponent<Knockdown>(); k._view = view; }
            return k;
        }

        private void Awake() { if (_view == null) _view = GetComponent<AvatarView>(); _owner = GetComponent<IKnockdownOwner>(); }

        public void Stagger(Vector3 away)
        {
            if (Busy || _view == null || _view.Animator == null) return;
            Begin(Phase.Stagger);
            if (away.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(new Vector3(-away.x, 0f, -away.z));
            _len = Length(Recoil, 0.55f) * 0.9f;
            AnimationLibrary.TryPlay(_view.Animator, Recoil, 0.08f);
        }

        /// <summary>Knock the actor down. velocity = body velocity at impact; impulse at the chest (N·s).</summary>
        public void Fall(Vector3 velocity, Vector3 impulse, bool allowRagdoll = true)
        {
            if (_view == null || _view.Animator == null || Current == Phase.Falling || Current == Phase.Rolling || Current == Phase.Down || Current == Phase.GettingUp) return;
            Begin(Phase.Falling);
            bool near = Camera.main != null && Vector3.Distance(Camera.main.transform.position, transform.position) < RagdollRange + 10f &&
                        Vector3.Distance(CameraFocus(), transform.position) < RagdollRange;
            if (allowRagdoll && _view.LodTier == 0 && !_view.Culled && near && RagdollRig.TryAcquire())
            {
                _rig = RagdollRig.Build(_view.Animator);
                if (_rig == null) RagdollRig.ReleaseUnbuilt();
            }
            if (_rig != null)
            {
                _rig.Activate(velocity, impulse, _rig.Chest.position);
                _len = 3.5f;
            }
            else
            {
                // Animated: backward fall when pushed back, knockback otherwise (both end on the back).
                bool backward = Vector3.Dot(impulse.sqrMagnitude > 0.01f ? impulse : velocity, transform.forward) < 0f;
                var clip = backward ? FallBack : Knockback;
                _getUpState = GetUp;
                _len = Length(clip, 1.4f);
                AnimationLibrary.TryPlay(_view.Animator, clip, 0.08f);
                _animatedFall = clip;
            }
            Fell?.Invoke(_view, _rig != null);
        }

        private string _animatedFall;

        private void Begin(Phase phase)
        {
            Current = phase; _t = 0f; _rolled = false; _blend = -1f; _animatedFall = null;
            _view.Suppressed = true;
            _owner?.SetControl(false);
        }

        private void Update()
        {
            if (!Busy) return;
            float dt = Time.deltaTime;
            _t += dt;
            switch (Current)
            {
                case Phase.Stagger:
                    if (_t >= _len) End();
                    break;
                case Phase.Falling:
                    if (_rig != null)
                    {
                        if (_rig.Settled(dt, _t)) { Current = Phase.Down; _t = 0f; }
                    }
                    else if (_t >= _len) { Current = Phase.Down; _t = 0f; }
                    break;
                case Phase.Down:
                    if (_t < 0.5f) break;
                    if (_rig != null && !_rolled && !_rig.FaceUp(out var head) && !_view.Animator.HasState(0, AnimationLibrary.Hash(GetUpFront)))
                    {
                        // No face-down get-up: roll onto the back first (as people do), then get up.
                        _rolled = true;
                        LastGetUp = "face-down → rolled onto the back";
                        var axis = head.sqrMagnitude > 0.01f ? head : transform.forward;
                        _rig.HipsBody.AddTorque(axis * 9f, ForceMode.VelocityChange);
                        Current = Phase.Rolling; _t = 0f;
                        break;
                    }
                    BeginGetUp();
                    break;
                case Phase.Rolling:
                    if (_rig.Settled(dt, _t, 2f)) { Current = Phase.Down; _t = 0.5f; }
                    break;
                case Phase.GettingUp:
                    if (_t >= _len * 0.94f) End();
                    break;
            }
        }

        private void BeginGetUp()
        {
            Current = Phase.GettingUp; _t = 0f;
            var animator = _view.Animator;
            var tuning = LocomotionTuning.Instance;
            if (_rig != null)
            {
                bool up = _rig.FaceUp(out var headDir);
                _getUpState = !up && animator.HasState(0, AnimationLibrary.Hash(GetUpFront)) ? GetUpFront : GetUp;
                LastGetUp = (_rolled ? "rolled, then " : "") + (up ? "face-up" : "face-down") + " → " + _getUpState;
                _rig.Freeze();
                // Snapshot the rest pose (world rotations) before the animator takes the bones back.
                _bones = HumanBones(animator);
                _snapshot = new Quaternion[_bones.Length];
                for (int i = 0; i < _bones.Length; i++) if (_bones[i] != null) _snapshot[i] = _bones[i].rotation;
                _snapHips = _rig.Hips.position;
                // Re-place the root so the clip's lying pose (hips + head direction at t=0) lies on the body.
                if (tuning != null && tuning.TryPose(_getUpState, out var pose))
                {
                    var clipHead = pose.headStart - pose.hipsStart; clipHead.y = 0f;
                    float yaw = clipHead.sqrMagnitude > 1e-4f && headDir.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(clipHead, headDir, Vector3.up) : transform.eulerAngles.y;
                    var rotation = Quaternion.Euler(0f, yaw, 0f);
                    var hipsFlat = rotation * new Vector3(pose.hipsStart.x, 0f, pose.hipsStart.z);
                    var root = new Vector3(_snapHips.x, transform.position.y, _snapHips.z) - hipsFlat;
                    if (Physics.Raycast(root + Vector3.up * 1f, Vector3.down, out var g, 3f, CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore)) root.y = g.point.y;
                    PlaceRoot(root, rotation);
                }
                animator.enabled = true;
                _blend = 0f;
            }
            else
            {
                // Animated fall → get-up: shift the root by the difference between the fall's last and the get-up's first hips.
                _getUpState = GetUp;
                if (tuning != null && _animatedFall != null && tuning.TryPose(_animatedFall, out var fall) && tuning.TryPose(GetUp, out var getUp))
                {
                    var d = fall.hipsEnd - getUp.hipsStart; d.y = 0f;
                    PlaceRoot(transform.position + transform.rotation * d, transform.rotation);
                }
            }
            _len = Length(_getUpState, 1.6f);
            animator.Play(AnimationLibrary.Hash(_getUpState), 0, 0f);
            animator.Update(0f);
        }

        private void LateUpdate()
        {
            if (_blend < 0f || _bones == null) return;
            // The Animator has posed this frame's get-up; ease every bone from the ragdoll's rest pose into it.
            _blend += Time.deltaTime;
            float k = Mathf.Clamp01(_blend / BlendTime);
            k = k * k * (3f - 2f * k);
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) _bones[i].rotation = Quaternion.Slerp(_snapshot[i], _bones[i].rotation, k);
            if (_rig != null && _rig.Hips != null) _rig.Hips.position = Vector3.Lerp(_snapHips, _rig.Hips.position, k);
            if (k >= 1f) _blend = -1f;
        }

        private void PlaceRoot(Vector3 position, Quaternion rotation)
        {
            if (_owner != null) _owner.PlaceRoot(position, rotation);
            else transform.SetPositionAndRotation(position, rotation);
        }

        private void End()
        {
            if (_rig != null) { _rig.Teardown(); _rig = null; }
            _blend = -1f; _bones = null; _snapshot = null;
            Current = Phase.None;
            _view.Suppressed = false;
            _view.RestoreBaseAnimation();
            _owner?.SetControl(true);
        }

        private void OnDisable()
        {
            if (!Busy) return;
            if (_rig != null) { _rig.Teardown(); _rig = null; }
            Current = Phase.None;
            if (_view != null) { _view.Suppressed = false; }
        }

        private Vector3 CameraFocus() => CameraRig.Instance != null && CameraRig.Instance.Target != null ? CameraRig.Instance.Target.position : transform.position;

        private static float Length(string state, float fallback) =>
            LocomotionTuning.Instance != null ? LocomotionTuning.Instance.LengthOf(state, fallback) : fallback;

        private static Transform[] HumanBones(Animator animator)
        {
            var list = new List<Transform>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var t = animator.GetBoneTransform((HumanBodyBones)i);
                if (t != null) list.Add(t);
            }
            return list.ToArray();
        }
    }
}
