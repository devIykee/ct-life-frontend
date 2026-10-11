using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Sit-down / stand-up presentation for any actor (local player, remote players, NPCs) on the same seats. The actor's
    /// logical root keeps its authoritative pose (server spot / NavMesh); only the visual model (Animator child), head
    /// anchor, voice, blob shadow, tap capsule and LOD reference move, the pattern CafeSeatExitPresentation uses.
    /// Measured clips (CharacterFeelController): act_sit_down keeps the feet on the seat root while the hips travel
    /// 0.25 m back / 0.33 m down into the seat (1.30 s); act_stand_up reverses it (1.03 s). So: enter = step and turn
    /// onto the seat root, then the clip; leave = the clip, then step from the seat root to the standing point.
    /// </summary>
    internal sealed class SeatPresenter
    {
        public enum Stage { None, Align, SitDown, StandUp, Step }
        private const float AlignSpeed = 1.1f;      // m/s while stepping onto / off the seat root
        private const float TurnRate = 300f;        // deg/s

        private readonly AvatarView _view;
        private Transform _model, _head, _voice, _shadow;
        private CapsuleCollider _hit;
        private LODGroup _lod;
        private Vector3 _modelLocal, _headLocal, _voiceLocal, _shadowLocal, _hitCenter, _lodReference;
        private Quaternion _modelRotation, _shadowRotation;
        private bool _bound;

        public Stage Current { get; private set; }
        public bool Active => Current != Stage.None;
        public bool Entering { get; private set; }
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; }
        public float Speed { get; private set; }
        /// <summary>Seat fit offset (seat space) added to the model while presenting; AvatarView ramps it.</summary>
        public Vector3 Offset { get; set; }

        private Vector3 _from, _to;
        private Quaternion _fromRot, _toRot;
        private float _t, _clip, _alignTime;

        public SeatPresenter(AvatarView view) { _view = view; }

        private bool Bind()
        {
            if (_bound) return _model != null;
            _bound = true;
            if (_view.Animator == null) return false;
            _model = _view.Animator.transform;
            if (_model == _view.transform || _model.parent != _view.transform) { _model = null; return false; }
            _modelLocal = _model.localPosition; _modelRotation = _model.localRotation;
            _head = _view.HeadAnchor; if (_head != null) _headLocal = _head.localPosition;
            _voice = _view.Voice != null ? _view.Voice.transform : null; if (_voice != null) _voiceLocal = _voice.localPosition;
            _shadow = _view.transform.Find("blob_shadow");
            if (_shadow != null) { _shadowLocal = _shadow.localPosition; _shadowRotation = _shadow.localRotation; }
            _hit = _view.GetComponent<CapsuleCollider>(); if (_hit != null) _hitCenter = _hit.center;
            _lod = _view.GetComponentInChildren<LODGroup>(); if (_lod != null) _lodReference = _lod.localReferencePoint;
            return true;
        }

        public bool BeginEnter(Vector3 from, Quaternion fromRot, Vector3 seat, Quaternion seatRot, float sitClipLength)
        {
            if (!Bind()) return false;
            _from = from; _fromRot = fromRot; _to = seat; _toRot = seatRot;
            float d = Flat(from, seat);
            _alignTime = Mathf.Clamp(d / AlignSpeed, 0.2f, 1.2f);
            float turn = Quaternion.Angle(fromRot, seatRot) / TurnRate;
            _alignTime = Mathf.Max(_alignTime, Mathf.Min(turn, 0.7f));
            _clip = sitClipLength <= 0f ? 0.01f : Mathf.Max(0.3f, sitClipLength);
            _t = 0f; Entering = true;
            Current = d < 0.05f && Quaternion.Angle(fromRot, seatRot) < 10f ? Stage.SitDown : Stage.Align;
            Position = from; Rotation = fromRot;
            return true;
        }

        public bool BeginLeave(Vector3 seat, Quaternion seatRot, Vector3 stand, Quaternion standRot, float standClipLength)
        {
            if (!Bind()) return false;
            _from = seat; _fromRot = seatRot; _to = stand; _toRot = standRot;
            _clip = standClipLength <= 0f ? 0.01f : Mathf.Max(0.3f, standClipLength);
            _alignTime = Mathf.Clamp(Flat(seat, stand) / AlignSpeed, 0.15f, 1.0f);
            _t = 0f; Entering = false;
            Current = Stage.StandUp;
            Position = seat; Rotation = seatRot;
            return true;
        }

        /// <summary>Advances; returns the stage that just began (for the animation request), else None.</summary>
        public Stage Tick(float dt)
        {
            if (!Active) return Stage.None;
            _t += dt;
            switch (Current)
            {
                case Stage.Align:
                {
                    float k = Smooth(Mathf.Clamp01(_t / _alignTime));
                    Position = Vector3.Lerp(_from, _to, k);
                    Rotation = Quaternion.Slerp(_fromRot, _toRot, Smooth(Mathf.Clamp01(_t / (_alignTime * 0.8f))));
                    Speed = Flat(_from, _to) > 0.05f ? AlignSpeed * Mathf.Sin(k * Mathf.PI) : 0f;
                    if (_t >= _alignTime) { _t = 0f; Current = Stage.SitDown; Position = _to; Rotation = _toRot; Speed = 0f; return Stage.SitDown; }
                    return Stage.None;
                }
                case Stage.SitDown:
                    Position = _to; Rotation = _toRot; Speed = 0f;
                    if (_t >= _clip) { Finish(); return Stage.None; }
                    return Stage.None;
                case Stage.StandUp:
                    Position = _from; Rotation = _fromRot; Speed = 0f;
                    if (_t >= _clip)
                    {
                        _t = 0f;
                        if (Flat(_from, _to) < 0.05f && Quaternion.Angle(_fromRot, _toRot) < 10f) { Finish(); return Stage.None; }
                        Current = Stage.Step;
                        return Stage.Step;
                    }
                    return Stage.None;
                case Stage.Step:
                {
                    float k = Smooth(Mathf.Clamp01(_t / _alignTime));
                    Position = Vector3.Lerp(_from, _to, k);
                    Rotation = Quaternion.Slerp(_fromRot, _toRot, k);
                    Speed = AlignSpeed * Mathf.Sin(k * Mathf.PI);
                    if (_t >= _alignTime) { Finish(); return Stage.None; }
                    return Stage.None;
                }
            }
            return Stage.None;
        }

        /// <summary>LateUpdate: pin the visual model (after the Animator, after any root writes this frame).</summary>
        public void Apply()
        {
            if (!Active || _model == null) return;
            _model.SetPositionAndRotation(Position + Rotation * Offset, Rotation * _modelRotation);
            if (_head != null) _head.position = Position + Rotation * _headLocal;
            if (_voice != null) _voice.position = Position + Rotation * _voiceLocal;
            if (_shadow != null) _shadow.SetPositionAndRotation(Position + Rotation * _shadowLocal, Rotation * _shadowRotation);
            if (_hit != null) _hit.center = _view.transform.InverseTransformPoint(Position + Rotation * _hitCenter);
            if (_lod != null) _lod.localReferencePoint = _view.transform.InverseTransformPoint(Position + Rotation * _lodReference);
        }

        public void Cancel()
        {
            if (!Active) return;
            Finish();
        }

        private void Finish()
        {
            Current = Stage.None; Speed = 0f;
            if (_model != null) { _model.localPosition = _modelLocal; _model.localRotation = _modelRotation; }
            if (_head != null) _head.localPosition = _headLocal;
            if (_voice != null) _voice.localPosition = _voiceLocal;
            if (_shadow != null) { _shadow.localPosition = _shadowLocal; _shadow.localRotation = _shadowRotation; }
            if (_hit != null) _hit.center = _hitCenter;
            if (_lod != null) _lod.localReferencePoint = _lodReference;
        }

        private static float Smooth(float x) => x * x * (3f - 2f * x);
        private static float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }
    }
}
