using System.Collections.Generic;
using CTLife.Core;
using CTLife.Realtime;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Another player in the venue. Positions come from the server (PlayerState x/z/rotY at 10 Hz) and are rendered
    /// 120 ms in the past with snapshot interpolation; anim, emotes (started at `emoteAt` server time), seat and the
    /// speaking light follow the state. The client never sets `anim` for others.
    /// </summary>
    [RequireComponent(typeof(AvatarView))]
    public sealed class RemotePlayer : MonoBehaviour
    {
        private const float InterpDelay = 0.12f;
        private const float MaxExtrapolate = 0.15f;

        private struct Snap
        {
            public double t;
            public Vector3 pos;
            public float yaw;
        }

        private readonly List<Snap> _buffer = new List<Snap>(16);
        private AvatarView _view;
        private VenueSceneRoot _venue;
        private string _lastEmoteKey;
        private string _lastAppliedSpot; // Colyseus mutates the same PlayerState before OnChange.
        private string _lastAppliedAction, _exitedSeatAction;
        private Vector3 _prevRenderPos;
        private float _speed;

        public PlayerState State { get; private set; }

        public void Init(VenueSceneRoot venue, PlayerState state)
        {
            _venue = venue;
            _view = GetComponent<AvatarView>();
            CTLife.Audio.Footsteps.Attach(gameObject);
            if (venue != null) _view.BindSeatMotionProfile(venue.GetComponent<CafeSeatMotionProfile>());
            State = state;
            var p = venue.ToWorld((float)state.x, (float)state.z);
            transform.SetPositionAndRotation(p, Quaternion.Euler(0, (float)state.rotY * Mathf.Rad2Deg, 0));
            _prevRenderPos = p;
            Apply(state, true);
        }

        /// <summary>Called on every PlayerState change.</summary>
        public void Apply(PlayerState s, bool initial = false)
        {
            string previousSpot = _lastAppliedSpot;
            string previousAction = _lastAppliedAction;
            _lastAppliedSpot = s.spotId; _lastAppliedAction = s.anim;
            bool leavingSeat = !initial && !string.IsNullOrEmpty(previousSpot) && string.IsNullOrEmpty(s.spotId);
            var seatedPosition = _view.PresentationPosition;
            var seatedRotation = _view.Animator != null ? _view.Animator.transform.rotation : transform.rotation;
            State = s;
            var p = _venue.ToWorld((float)s.x, (float)s.z);
            if (leavingSeat)
            {
                _view.BeginSeatExitPresentation(previousSpot, seatedPosition, seatedRotation);
                _exitedSeatAction = previousAction;
                // A seat anchor→walking coordinate is a mode boundary, not a velocity sample to extrapolate.
                _buffer.Clear(); _prevRenderPos = p; _speed = 0f;
            }
            else if (!string.IsNullOrEmpty(s.spotId)) { _view.CancelSeatExitPresentation(false); _exitedSeatAction = null; }
            float yaw = (float)s.rotY * Mathf.Rad2Deg;
            if (_buffer.Count == 0 || (_buffer[_buffer.Count - 1].pos - p).sqrMagnitude > 1e-6f || Mathf.Abs(Mathf.DeltaAngle(_buffer[_buffer.Count - 1].yaw, yaw)) > 0.5f)
            {
                _buffer.Add(new Snap { t = Time.timeAsDouble, pos = p, yaw = yaw });
                if (_buffer.Count > 16) _buffer.RemoveAt(0);
            }

            _view.displayName = s.name;
            _view.isSeeker = s.isSeeker;
            _view.minerTier = s.minerTier;
            _view.tribeId = s.tribeId;
            _view.SetTalking(s.talking);

            if (!string.IsNullOrEmpty(s.spotId) && _venue.TryGetSpot(s.spotId, out var seat, out var seatRot))
            {
                _buffer.Clear();
                _buffer.Add(new Snap { t = Time.timeAsDouble, pos = seat, yaw = seatRot * Mathf.Rad2Deg });
            }
            //A seat release can arrive before its unchanged generic seated anim field.
            //That same old field must not cancel the exit on this or repeated free snapshots.
            bool lingeringSeatAction = _view.SeatExitActive && string.IsNullOrEmpty(s.spotId) &&
                !string.IsNullOrEmpty(_exitedSeatAction) && s.anim == _exitedSeatAction;
            if (lingeringSeatAction) _view.SetBase("loco_idle");
            else
            {
                if (s.anim != _exitedSeatAction) _exitedSeatAction = null;
                if (!string.IsNullOrEmpty(s.anim) && s.anim.StartsWith("act_"))
                    _view.SetBase(OccupiedSpotAnimation.Resolve(_venue, s.spotId, s.anim), s.spotId);
            }

            var emoteKey = s.emoteId + "@" + s.emoteAt;
            if (!string.IsNullOrEmpty(s.emoteId) && emoteKey != _lastEmoteKey)
            {
                _lastEmoteKey = emoteKey;
                float startIn = ServerClock.IsSynced ? ServerClock.SecondsUntil(s.emoteAt) : 0f;
                if (!initial || startIn > -3f) _view.PlayOneShot(s.emoteId, startIn);
            }
        }

        private void Update()
        {
            if (_buffer.Count == 0) return;
            double renderT = Time.timeAsDouble - InterpDelay;
            Vector3 pos;
            float yaw;
            int i = _buffer.Count - 1;
            while (i > 0 && _buffer[i - 1].t > renderT) i--;
            if (i == 0)
            {
                pos = _buffer[0].pos;
                yaw = _buffer[0].yaw;
            }
            else if (_buffer[i].t <= renderT)
            {
                var last = _buffer[_buffer.Count - 1];
                pos = last.pos;
                yaw = last.yaw;
                if (_buffer.Count >= 2 && renderT - last.t < MaxExtrapolate)
                {
                    var prev = _buffer[_buffer.Count - 2];
                    double dt = last.t - prev.t;
                    if (dt > 1e-3) pos += (last.pos - prev.pos) * (float)((renderT - last.t) / dt);
                }
            }
            else
            {
                var a = _buffer[i - 1];
                var b = _buffer[i];
                float f = (float)((renderT - a.t) / (b.t - a.t));
                pos = Vector3.Lerp(a.pos, b.pos, f);
                yaw = Mathf.LerpAngle(a.yaw, b.yaw, f);
            }

            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            _view.ReapplySeatExitPresentation();
            float inst = Time.deltaTime > 0 ? (pos - _prevRenderPos).magnitude / Time.deltaTime : 0f;
            _speed = Mathf.Lerp(_speed, inst, 0.25f);
            _prevRenderPos = pos;
            _view.SetSpeed(_speed);

            var anim = State?.anim;
            if (string.IsNullOrEmpty(anim) || anim.StartsWith("loco_"))
                _view.SetBase(_speed > 3.5f ? "loco_run" : _speed > 0.2f ? "loco_walk" : "loco_idle");
        }
    }
}
