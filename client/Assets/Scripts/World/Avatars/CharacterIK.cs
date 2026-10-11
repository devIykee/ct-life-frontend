using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Grounding and attention on the Humanoid IK pass (works on optimized hierarchies), near characters only:
    /// - Feet: planted feet sit on the real ground (steps, slopes, kerbs) and never below it (also fixes clips whose soles
    ///   dip under the floor); the pelvis drops for a lower foot so the leg is not stretched.
    /// - Look-at: head/eyes toward the nearest speaker; seated listeners toward a host on stage; the local player glances
    ///   at the camera when idle. Smoothed, clamped, never while a knockdown or seat transition owns the body.
    /// - Hands: resting on the table top when seated at a table (not while typing, which is authored).
    /// - Seated feet: on IK-goal seated states (Codex's café fits, typing), soles that would sink under the floor are lifted;
    ///   on the fitted seat-height blends, the pose's measured soles go onto the real surface (rugs, trims) or a stool's bar.
    /// Cost: ≤4 raycasts per character per frame (feet), attention re-picked every 0.3 s; disabled beyond LOD0.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class CharacterIK : MonoBehaviour
    {
        private const float AnkleHeight = 0.075f, ProbeUp = 0.45f, ProbeDown = 0.9f;
        private Animator _animator;
        private AvatarView _view;
        private float _pelvisOffset, _lookWeight, _nextPick, _nextHands;
        private Vector3 _lookTarget;
        private bool _hasLook, _handsOnTable;
        private Vector3 _leftHand, _rightHand, _leftElbow, _rightElbow;
        private float _handWeight;
        private Transform _cam;

        public static bool Enabled = true;
        /// <summary>Diagnostics: animated (pre-IK) left/right foot positions each IK pass. Null in normal play (no cost).</summary>
        public static System.Action<AvatarView, Vector3, Vector3> FootProbe;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _view = GetComponentInParent<AvatarView>();
        }

        private bool Allowed => Enabled && _view != null && _view.Animator == _animator && !_view.Culled && _view.LodTier == 0 && !_view.Suppressed;

        /// <summary>The animated body (centre of mass) position of the last IK pass: readable only inside the pass.</summary>
        public Vector3 BodyPosition { get; private set; }
        public int BodyFrame { get; private set; } = -1;

        private void OnAnimatorIK(int layer)
        {
            if (_animator != null) { BodyPosition = _animator.bodyPosition; BodyFrame = Time.frameCount; }
            if (_view == null) _view = GetComponentInParent<AvatarView>(); // added before the AvatarView at spawn
            if (FootProbe != null && _view != null && _animator != null)
                FootProbe(_view, _animator.GetIKPosition(AvatarIKGoal.LeftFoot), _animator.GetIKPosition(AvatarIKGoal.RightFoot));
            if (!Allowed || _animator == null) return;
            if (_cam == null && Camera.main != null) _cam = Camera.main.transform;
            bool seated = _view.SeatPresentationActive || IsSeatedState();
            if (!seated && FootGoalsValid()) Feet(); else _pelvisOffset = 0f;
            if (seated && !_view.SeatPresentationActive && SeatedGoalsValid()) SeatedFeet();
            else if (seated && _view.SeatFitSettled && IsFittedState()) SeatFitFeet();
            if (seated && _view.SeatFitSettled) TypingHands();
            Attention(seated);
            Hands(seated);
        }

        private static readonly int[] FootIkStates =
        {
            Animator.StringToHash("loco_idle"), Animator.StringToHash("loco_walk"), Animator.StringToHash("loco_run"),
            Animator.StringToHash("idle_fidget_lookaround"), Animator.StringToHash("idle_fidget_stretch"),
            Animator.StringToHash("idle_fidget_foldarms"), Animator.StringToHash("idle_fidget_talk"), Animator.StringToHash("idle_lean_rail"),
        };

        /// <summary>
        /// The IK pass only receives goals that follow the animated feet in states with Humanoid Foot IK (the builder
        /// enables it on locomotion and standing idles); elsewhere the goals are stale and must not be placed.
        /// </summary>
        private bool FootGoalsValid()
        {
            bool Ok(int h) { foreach (var s in FootIkStates) if (s == h) return true; return false; }
            var cur = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            if (!Ok(cur)) return false;
            return !_animator.IsInTransition(0) || Ok(_animator.GetNextAnimatorStateInfo(0).shortNameHash);
        }

        // Seated states with Humanoid Foot IK (their goals follow the animated feet): Codex's fitted café sit and typing,
        // the authored typing loops. The fitted seat-height blends solve their soles onto the support in FK instead.
        private static readonly int[] SeatedIkStates =
        {
            Animator.StringToHash(CafeSeatMotionProfile.SocialState), Animator.StringToHash(CafeSeatMotionProfile.TypingState),
            Animator.StringToHash("act_type"), Animator.StringToHash("act_frantic_type"), Animator.StringToHash("act_sit_talk"),
        };

        private bool SeatedGoalsValid()
        {
            if (_animator.IsInTransition(0)) return false;
            var cur = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            foreach (var s in SeatedIkStates) if (s == cur) return true;
            return false;
        }

        /// <summary>
        /// Seated on a fitted seat the hips rest on the seat (AvatarView's seat offset), so a pose authored for another
        /// seat height can put the soles under the floor: lift those feet onto it (knees bend a little more). Never lowers.
        /// </summary>
        private void SeatedFeet()
        {
            SeatedFoot(AvatarIKGoal.LeftFoot, _animator.leftFeetBottomHeight);
            SeatedFoot(AvatarIKGoal.RightFoot, _animator.rightFeetBottomHeight);
        }

        private void SeatedFoot(AvatarIKGoal goal, float bottom)
        {
            var pos = _animator.GetIKPosition(goal);
            if (!Physics.Raycast(pos + Vector3.up * 0.3f, Vector3.down, out var hit, 0.6f, CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore) ||
                hit.normal.y < 0.6f) return;
            float minAnkle = hit.point.y + bottom;
            if (pos.y >= minAnkle) return;
            _animator.SetIKPositionWeight(goal, 1f);
            _animator.SetIKPosition(goal, new Vector3(pos.x, minAnkle, pos.z));
        }

        private static readonly int[] FittedStates =
        {
            Animator.StringToHash("act_sit"), Animator.StringToHash("act_sit_lounge"), Animator.StringToHash("act_sit_beanbag"),
            Animator.StringToHash("act_sit_stool"),
        };

        private bool IsFittedState()
        {
            if (_animator.IsInTransition(0)) return false;
            var cur = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            foreach (var s in FittedStates) if (s == cur) return true;
            return false;
        }

        /// <summary>
        /// The fitted seat-height blends put the soles on a flat support at the measured floor (or footrest) height; real
        /// venues add rugs, trims and thin stool bars. The pose's own sole and ankle points (SeatFitCatalog metric, no
        /// stale IK goals involved) are re-targeted: onto the surface under each sole (±8 cm), or onto the stool's bar.
        /// </summary>
        private void SeatFitFeet()
        {
            var fit = _view.SeatFit;
            var m = fit.metric;
            if (m == null || fit.seat == null) return;
            var model = _animator.transform;
            var seatFrame = model.parent != null ? model.parent : model;
            bool bar = fit.seat.kind == SeatKind.Stool && fit.seat.footrest > 0f;
            SeatFitFoot(AvatarIKGoal.LeftFoot, model.TransformPoint(m.leftAnkle), model.TransformPoint(m.leftFoot), seatFrame, bar, fit.seat);
            SeatFitFoot(AvatarIKGoal.RightFoot, model.TransformPoint(m.rightAnkle), model.TransformPoint(m.rightFoot), seatFrame, bar, fit.seat);
        }

        private void SeatFitFoot(AvatarIKGoal goal, Vector3 ankle, Vector3 sole, Transform seatFrame, bool bar, SeatFitCatalog.Seat seat)
        {
            if (ankle == sole) return; // metric without ankles (older catalog)
            Vector3 target;
            if (bar)
            {
                var local = seatFrame.InverseTransformPoint(sole);
                // Within the bar's sure width (SeatFitBuilder requires bar under |x| = 6 cm on both sides).
                target = seatFrame.TransformPoint(new Vector3(Mathf.Clamp(local.x, -0.06f, 0.06f), seat.footrest, seat.footrestForward));
            }
            else
            {
                if (!Physics.Raycast(sole + Vector3.up * 0.12f, Vector3.down, out var hit, 0.2f, CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore) ||
                    hit.normal.y < 0.6f) return;
                target = new Vector3(sole.x, hit.point.y, sole.z);
            }
            var delta = target - sole;
            if (delta.sqrMagnitude < 0.004f * 0.004f || delta.sqrMagnitude > 0.1f * 0.1f) return;
            _animator.SetIKPositionWeight(goal, 1f);
            _animator.SetIKPosition(goal, ankle + delta);
        }

        private static readonly int[] TypingStates =
        {
            Animator.StringToHash("act_type"), Animator.StringToHash("act_frantic_type"), Animator.StringToHash(CafeSeatMotionProfile.TypingState),
        };

        /// <summary>
        /// Typing at a measured work surface: the clip types wherever its own desk was; put both wrists on this seat's
        /// laptop/keyboard (SeatFitCatalog: device centre and top). The fingers keep the clip's typing; the arms reach
        /// as far as they can (a device beyond reach is a venue layout item: see requests.md).
        /// </summary>
        private void TypingHands()
        {
            var fit = _view.SeatFit;
            if (fit.seat == null || fit.seat.workZ <= 0f || _animator.IsInTransition(0)) return;
            int cur = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            bool typing = false;
            foreach (var s in TypingStates) if (s == cur) typing = true;
            if (!typing) return;
            var model = _animator.transform;
            var seatFrame = model.parent != null ? model.parent : model;
            float y = fit.seat.workDeviceTop + 0.025f, z = fit.seat.workZ - 0.05f;
            foreach (var (goal, x) in new[] { (AvatarIKGoal.LeftHand, -0.11f), (AvatarIKGoal.RightHand, 0.11f) })
            {
                _animator.SetIKPositionWeight(goal, 1f);
                _animator.SetIKPosition(goal, seatFrame.TransformPoint(new Vector3(x, y, z)));
            }
        }

        private bool IsSeatedState()
        {
            var info = _animator.GetCurrentAnimatorStateInfo(0);
            return info.IsName("act_sit") || info.IsName("act_type") || info.IsName("act_sit_talk") || info.IsName("act_frantic_type") ||
                   info.IsName("act_sit_lounge") || info.IsName("act_sit_beanbag") || info.IsName("act_sit_stool") ||
                   info.IsName(CafeSeatMotionProfile.SocialState) || info.IsName(CafeSeatMotionProfile.TypingState) ||
                   info.IsName("act_lean_back") || info.IsName("act_head_in_hands") || info.IsName("act_sit_down") || info.IsName("act_stand_up");
        }

        // ------------------------------------------------------------------ feet

        private RuntimeAnimatorController _liftFor;
        private float _walkLift, _runLift, _soleLift;
        private static readonly int WalkState = Animator.StringToHash("loco_walk"), RunState = Animator.StringToHash("loco_run");

        /// <summary>
        /// The family's planted-sole lift for the locomotion tree at this speed (LocomotionTuning, measured offline per
        /// family: the cast-wide replacement for Codex's Snow-only sprint table). Standing idles have their own contact.
        /// </summary>
        private float SoleLift()
        {
            var ctrl = _animator.runtimeAnimatorController;
            if (ctrl != _liftFor)
            {
                _liftFor = ctrl;
                (_walkLift, _runLift) = LocomotionTuning.Instance != null ? LocomotionTuning.Instance.SoleLiftFor(ctrl) : (0f, 0f);
            }
            int cur = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            if (cur != WalkState && cur != RunState) return 0f;
            float k = Mathf.InverseLerp(LocomotionTuning.BlendWalk, LocomotionTuning.BlendRun, _animator.GetFloat(AnimationLibrary.SpeedParam));
            return Mathf.Lerp(_walkLift, _runLift, k);
        }

        private void Feet()
        {
            float root = transform.position.y;
            _soleLift = SoleLift();
            float lOffset = Foot(AvatarIKGoal.LeftFoot, root, out float lw);
            float rOffset = Foot(AvatarIKGoal.RightFoot, root, out float rw);
            // Drop the pelvis for the lower planted foot (steps down); never raise it (that is the feet's job).
            float target = Mathf.Min(0f, Mathf.Min(lw > 0.5f ? lOffset : 0f, rw > 0.5f ? rOffset : 0f));
            _pelvisOffset = Mathf.Lerp(_pelvisOffset, Mathf.Max(target, -0.35f), 1f - Mathf.Exp(-12f * Time.deltaTime));
            if (Mathf.Abs(_pelvisOffset) > 0.002f) _animator.bodyPosition += Vector3.up * _pelvisOffset;
        }

        /// <summary>Places one foot; returns its vertical offset from the root plane, and the weight used.</summary>
        private float Foot(AvatarIKGoal goal, float rootY, out float weight)
        {
            weight = 0f;
            var pos = _animator.GetIKPosition(goal);
            float lift = pos.y - rootY - AnkleHeight - _soleLift;        // how far the clip lifts this foot above its contact
            float w = 1f - Mathf.Clamp01((lift - 0.04f) / 0.14f);       // planted → 1, swinging → 0
            if (!Physics.Raycast(new Vector3(pos.x, rootY + ProbeUp, pos.z), Vector3.down, out var hit, ProbeUp + ProbeDown,
                    CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.6f)
                return 0f;
            float ground = hit.point.y - rootY;
            // Planted feet: down by the family's measured sole lift (onto the ground, not hovering above it).
            var target = pos + Vector3.up * (ground - _soleLift * (1f - Mathf.Clamp01((lift - 0.04f) / 0.14f)));
            // Never below the floor, whatever the clip does: a flat foot's ankle stays its own height above the ground.
            float bottom = goal == AvatarIKGoal.LeftFoot ? _animator.leftFeetBottomHeight : _animator.rightFeetBottomHeight;
            float minAnkle = hit.point.y + Mathf.Max(AnkleHeight, bottom);
            if (target.y < minAnkle) { target.y = minAnkle; w = Mathf.Max(w, 0.85f); }
            if (w <= 0.01f) return ground;
            weight = w;
            _animator.SetIKPositionWeight(goal, w);
            _animator.SetIKRotationWeight(goal, w * 0.8f);
            _animator.SetIKPosition(goal, target);
            _animator.SetIKRotation(goal, Quaternion.FromToRotation(Vector3.up, hit.normal) * _animator.GetIKRotation(goal));
            return ground;
        }

        // ------------------------------------------------------------------ look-at

        private void Attention(bool seated)
        {
            if (Time.time >= _nextPick) { _nextPick = Time.time + 0.3f; Pick(seated); }
            float want = _hasLook ? (seated ? 0.75f : 0.65f) : 0f;
            _lookWeight = Mathf.MoveTowards(_lookWeight, want, 1.6f * Time.deltaTime);
            if (_lookWeight <= 0.01f) return;
            _animator.SetLookAtWeight(_lookWeight, 0.12f, 0.85f, 1f, 0.6f);
            _animator.SetLookAtPosition(_lookTarget);
        }

        private void Pick(bool seated)
        {
            var me = _view;
            var head = me.HeadAnchor != null ? me.HeadAnchor.position : transform.position + Vector3.up * 1.6f;
            var forward = transform.forward;
            AvatarView best = null;
            float bestScore = float.MaxValue;
            foreach (var other in AvatarView.All)
            {
                if (other == null || other == me || other.Culled) continue;
                var to = other.PresentationPosition - transform.position;
                float d = to.magnitude;
                if (d > 7f || d < 0.3f) continue;
                if (Vector3.Dot(forward, to / d) < -0.1f) continue; // behind: do not twist round
                // Speakers win; a nearby face is the fallback for idling NPCs.
                float score = other.Talking ? d * 0.4f : d + (me.isNpc ? 0f : 3f);
                if (score < bestScore) { bestScore = score; best = other; }
            }
            if (best != null && (best.Talking || me.isNpc || seated))
            {
                _lookTarget = best.HeadAnchor != null ? best.HeadAnchor.position - Vector3.up * 0.2f : best.PresentationPosition + Vector3.up * 1.55f;
                _hasLook = true;
                return;
            }
            // The local player, idle: an occasional glance at the camera (a premium life-sim touch), else straight ahead.
            if (me.isLocal && _cam != null && _view.Animator != null && _animator.GetFloat(AnimationLibrary.SpeedParam) < 0.1f)
            {
                var toCam = _cam.position - head;
                _hasLook = Vector3.Dot(forward, toCam.normalized) > 0.2f;
                _lookTarget = _cam.position;
                return;
            }
            _hasLook = false;
        }

        // ------------------------------------------------------------------ hands

        private void Hands(bool seated)
        {
            var info = _animator.GetCurrentAnimatorStateInfo(0);
            bool sitting = seated && info.IsName("act_sit");
            if (sitting && Time.time >= _nextHands) { _nextHands = Time.time + 0.5f; FindTable(); }
            float want = sitting && _handsOnTable ? 0.7f : 0f;
            _handWeight = Mathf.MoveTowards(_handWeight, want, 1.5f * Time.deltaTime);
            if (_handWeight <= 0.01f) return;
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, _handWeight);
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, _handWeight);
            _animator.SetIKPosition(AvatarIKGoal.LeftHand, _leftHand);
            _animator.SetIKPosition(AvatarIKGoal.RightHand, _rightHand);
            // Relaxed elbows: bent down and slightly out, not locked straight.
            _animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, _handWeight);
            _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, _handWeight);
            _animator.SetIKHintPosition(AvatarIKHint.LeftElbow, _leftElbow);
            _animator.SetIKHintPosition(AvatarIKHint.RightElbow, _rightElbow);
        }

        /// <summary>
        /// Hands rest on a table only when its near edge is within easy reach of the seated body (≤0.45 m from the hips,
        /// which sit ~0.3 m behind the seat root in act_sit); otherwise the authored hands-in-lap pose stays.
        /// </summary>
        private void FindTable()
        {
            _handsOnTable = false;
            var root = transform.position; var f = transform.forward; var r = transform.right;
            var hips = root - f * 0.3f;
            bool Probe(Vector3 p, out Vector3 at)
            {
                at = default;
                if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var hit, 0.9f, CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore)) return false;
                float h = hit.point.y - root.y;
                if (h < 0.55f || h > 0.9f || hit.normal.y < 0.8f) return false;
                at = hit.point + Vector3.up * 0.025f;
                return true;
            }
            // Walk forward from the hips to the table's near edge, then rest the hands just inside it.
            for (float d = 0.2f; d <= 0.45f; d += 0.05f)
            {
                if (!Probe(hips + f * d, out _)) continue;
                if (Probe(hips + f * (d + 0.08f) - r * 0.17f, out var l) && Probe(hips + f * (d + 0.08f) + r * 0.17f, out var rr))
                {
                    _leftHand = l; _rightHand = rr;
                    _leftElbow = hips - r * 0.35f + Vector3.up * 0.75f + f * 0.05f;
                    _rightElbow = hips + r * 0.35f + Vector3.up * 0.75f + f * 0.05f;
                    _handsOnTable = true;
                }
                return;
            }
        }
    }
}
