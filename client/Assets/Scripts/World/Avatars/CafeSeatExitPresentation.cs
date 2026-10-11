using System;
using UnityEngine;
using UnityEngine.AI;

namespace CTLife.World
{
    // A bounded presentation owner for inspected Café exits. The agent/server root is never written.
    // Routes are explicit profile data, not a generic permission to walk through furniture.
    internal sealed class CafeSeatExitPresentation
    {
        internal enum Phase { None, AuthoredExit, Turn, Step, Align, CatchUp }
        private readonly AvatarView _view;
        private readonly Transform _model, _head, _voice, _shadow;
        private readonly Vector3 _modelLocal, _headLocal, _voiceLocal, _shadowLocal;
        private readonly Quaternion _modelRotation, _shadowRotation;
        private readonly CapsuleCollider _hit;
        private readonly Vector3 _hitCenter;
        private readonly LODGroup _lod;
        private readonly Vector3 _lodReference;
        private readonly NavMeshPath _catchPath = new NavMeshPath();
        private Vector3[] _route, _catchCorners;
        private CafeWorkExitMotion _motion;
        private Vector3 _position, _catchGoal, _seat;
        private Quaternion _seatYaw;
        private float _previousClipTime;
        private Quaternion _rotation;
        private int _waypoint, _catchCorner;
        private float _speed, _nextCatchPathAt;
        private Phase _phase;

        internal bool Active => _phase != Phase.None;
        internal Vector3 Position => Active ? _position : _view.transform.position;
        internal string Stage => _phase.ToString();
        internal float Speed => _phase == Phase.Step || _phase == Phase.CatchUp ? _speed : 0f;

        internal CafeSeatExitPresentation(AvatarView view)
        {
            _view = view;
            _model = view.Animator.transform;
            _modelLocal = _model.localPosition; _modelRotation = _model.localRotation;
            _head = view.HeadAnchor; _voice = view.Voice != null ? view.Voice.transform : null;
            _shadow = view.transform.Find("blob_shadow");
            _headLocal = _head != null ? _head.localPosition : Vector3.zero;
            _voiceLocal = _voice != null ? _voice.localPosition : Vector3.zero;
            _shadowLocal = _shadow != null ? _shadow.localPosition : Vector3.zero;
            _shadowRotation = _shadow != null ? _shadow.localRotation : Quaternion.identity;
            _hit = view.GetComponent<CapsuleCollider>(); _hitCenter = _hit != null ? _hit.center : Vector3.zero;
            _lod = view.GetComponent<LODGroup>(); _lodReference = _lod != null ? _lod.localReferencePoint : Vector3.zero;
        }

        internal bool Begin(Vector3 seat, Quaternion yaw, Vector3[] route, CafeWorkExitMotion motion)
        {
            // These two registered bodies have an identity model child; other hierarchies stay on their baseline.
            if (motion == null || !motion.Valid || route == null || route.Length == 0 || _model == _view.transform || _model.parent != _view.transform ||
                _modelLocal.sqrMagnitude > .00000001f || Quaternion.Angle(_modelRotation, Quaternion.identity) > .01f)
                return false;
            if (_view.isLocal)
            {
                //Arrival stops within the authored approach tolerance; restore the exact
                //already validated logical NavMesh point, never snap to the nominal anchor.
                if (Vector3.Distance(route[route.Length - 1], _view.transform.position) > .15f) return false;
                route = (Vector3[])route.Clone(); route[route.Length - 1] = _view.transform.position;
            }
            Cancel(false);
            _route = route; _waypoint = 0; _position = seat; _rotation = yaw; _speed = 0f;
            if (!_view.RequestAuthoredWorkExit(seat, yaw, motion)) return false;
            _motion = motion; _seat = seat; _seatYaw = yaw; _previousClipTime = 0f;
            _phase = Phase.AuthoredExit;
            ApplyPose(); // Pin in the same call that restored the logical standing root.
            return true;
        }

        internal void Tick(float dt)
        {
            if (!Active) return;
            if (_view.Culled || _view.Animator == null || !_view.Animator.enabled)
            { Cancel(true); return; } // Hidden resumptions use the latest state, never an old visible exit.
            dt = Mathf.Max(0f, dt);
            switch (_phase)
            {
                case Phase.AuthoredExit:
                    _view.AdvanceAuthoredExit();
                    if (!_view.ReadAuthoredExitTime(out var clipTime, out var completed)) { Cancel(true); break; }
                    //Cue actual authored foot landings; no distance-based cues during the rise.
                    if (_view.isLocal)
                    {
                        if ((_previousClipTime < 1.33f && clipTime >= 1.33f) || (_previousClipTime < 1.61f && clipTime >= 1.61f) ||
                            (_previousClipTime < 1.82f && clipTime >= 1.82f) || (_previousClipTime < 2.03f && clipTime >= 2.03f))
                            { var audio = CTLife.Audio.AudioManager.Instance; if (audio != null) audio.PlayFootstep("concrete", _position); }
                    }
                    _previousClipTime = clipTime;
                    SetAuthoredPosition(clipTime);
                    if (completed)
                    {
                        _view.EndAuthoredExit();
                        _phase = Phase.Turn;
                        _view.PlaySeatExitState("loco_idle", .08f);
                    }
                    break;
                case Phase.Turn:
                    if (TurnTo(_route[_waypoint] - _position, dt))
                    { _phase = Phase.Step; _view.PlaySeatExitState("loco_walk", .12f); }
                    break;
                case Phase.Step:
                    _speed = Mathf.MoveTowards(_speed, LocalPlayerController.WalkSpeed, 14f * dt);
                    MoveTo(_route[_waypoint], _speed, dt);
                    if (Vector3.Distance(_position, _route[_waypoint]) < .0001f)
                    {
                        if (++_waypoint < _route.Length)
                        { _speed = 0f; _phase = Phase.Turn; _view.PlaySeatExitState("loco_idle", .1f); }
                        else
                        { _speed = 0f; _phase = Phase.Align; _view.PlaySeatExitState("loco_idle", .1f); }
                    }
                    break;
                case Phase.Align:
                    // A remote may have received later walking snapshots while displaying its seat exit.
                    // Catch up only after upright traversal reaches a valid standing point, on a complete NavMesh path.
                    if (Vector3.Distance(_position, _view.transform.position) > .015f)
                    {
                        if (_view.isLocal) { Cancel(true); break; } // A local authoritative correction owns position.
                        _phase = Phase.CatchUp; _nextCatchPathAt = 0f;
                        _view.PlaySeatExitState("loco_run", .12f);
                        break;
                    }
                    _rotation = Quaternion.RotateTowards(_rotation, _view.transform.rotation, 540f * dt);
                    if (Quaternion.Angle(_rotation, _view.transform.rotation) < .1f && !_view.Animator.IsInTransition(0))
                        Cancel(true);
                    break;
                case Phase.CatchUp:
                    TickCatchUp(dt);
                    break;
            }
        }

        private void SetAuthoredPosition(float seconds)
        {
            float u = Mathf.Clamp01((seconds - 1.05f) / .56f);
            u = u * u * (3f - 2f * u);
            float v = Mathf.Clamp01((seconds - 1.61f) / .42f);
            v = v * v * (3f - 2f * v);
            _position = _seat + _seatYaw * (_motion.firstStep * u + _motion.secondStep * v);
            _rotation = _seatYaw;
        }

        private bool TurnTo(Vector3 direction, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < .000001f) return true;
            var target = Quaternion.LookRotation(direction);
            _rotation = Quaternion.RotateTowards(_rotation, target, 540f * dt);
            return Quaternion.Angle(_rotation, target) < 2f;
        }

        private void MoveTo(Vector3 point, float speed, float dt)
        {
            var before = _position;
            _position = Vector3.MoveTowards(_position, point, speed * dt);
            if (dt > .00001f) _speed = Vector3.Distance(_position, before) / dt;
        }

        private void TickCatchUp(float dt)
        {
            var target = _view.transform.position;
            if (Vector3.Distance(_position, target) < .015f)
            { _phase = Phase.Align; _speed = 0f; _view.PlaySeatExitState("loco_idle", .1f); return; }
            // Large teleports/venue corrections are outside this seat presentation path.
            if (Vector3.Distance(_position, target) > 5f) { Cancel(true); return; }
            if (Time.unscaledTime >= _nextCatchPathAt &&
                (_catchCorners == null || Vector3.Distance(_catchGoal, target) > .1f))
            {
                _nextCatchPathAt = Time.unscaledTime + .25f;
                if (!NavMesh.CalculatePath(_position, target, NavMesh.AllAreas, _catchPath) ||
                    _catchPath.status != NavMeshPathStatus.PathComplete)
                { Cancel(true); return; }
                _catchCorners = _catchPath.corners; _catchCorner = _catchCorners.Length > 1 ? 1 : 0; _catchGoal = target;
            }
            if (_catchCorners == null || _catchCorners.Length == 0) return;
            var point = _catchCorners[_catchCorner];
            TurnTo(point - _position, dt);
            _speed = Mathf.MoveTowards(_speed, 6f, 14f * dt);
            MoveTo(point, _speed, dt);
            if (Vector3.Distance(_position, point) < .015f && _catchCorner + 1 < _catchCorners.Length) _catchCorner++;
        }

        internal void ApplyPose()
        {
            if (!Active) return;
            //Animator advances between Update and LateUpdate. Match the presentation curve
            //to the rendered clip phase, not the prior frame's time.
            if (_phase == Phase.AuthoredExit && _view.ReadAuthoredExitTime(out var seconds, out _)) SetAuthoredPosition(seconds);
            _model.SetPositionAndRotation(_phase == Phase.AuthoredExit ? _seat : _position, _rotation);
            if (_head != null) _head.position = _position + _rotation * _headLocal;
            if (_voice != null) _voice.position = _position + _rotation * _voiceLocal;
            if (_shadow != null) _shadow.SetPositionAndRotation(_position + _rotation * _shadowLocal, _rotation * _shadowRotation);
            if (_hit != null) _hit.center = _view.transform.InverseTransformPoint(_position + _rotation * _hitCenter);
            if (_lod != null) _lod.localReferencePoint = _view.transform.InverseTransformPoint(_position + _rotation * _lodReference);
        }

        internal void Cancel(bool restoreLatestAnimation)
        {
            if (!Active) return;
            _view.EndAuthoredExit();
            _phase = Phase.None; _route = null; _catchCorners = null; _motion = null; _speed = 0f;
            if (_model != null) { _model.localPosition = _modelLocal; _model.localRotation = _modelRotation; }
            if (_head != null) _head.localPosition = _headLocal;
            if (_voice != null) _voice.localPosition = _voiceLocal;
            if (_shadow != null) { _shadow.localPosition = _shadowLocal; _shadow.localRotation = _shadowRotation; }
            if (_hit != null) _hit.center = _hitCenter;
            if (_lod != null) _lod.localReferencePoint = _lodReference;
            if (restoreLatestAnimation) _view.SeatExitPresentationFinished();
        }
    }
}
