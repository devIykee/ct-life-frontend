using CTLife.Core;
using CTLife.World;
using UnityEngine;

namespace CTLife.App
{
    /// <summary>Spike crowd: walks between random points inside the venue's walkable rects and emotes now and then.</summary>
    public sealed class CrowdWalker : MonoBehaviour
    {
        private AvatarView _view;
        private VenueSceneRoot _venue;
        private VenueSpots _spots;
        private System.Random _rng;
        private string[] _emotes;
        private Vector3 _target;
        private float _waitUntil;
        private float _speed;

        public void Init(VenueSceneRoot venue, VenueSpots spots, int seed, string[] emotes)
        {
            _view = GetComponent<AvatarView>();
            _venue = venue;
            _spots = spots;
            _rng = new System.Random(seed);
            _emotes = emotes;
            transform.position = RandomPoint();
            PickTarget();
        }

        private Vector3 RandomPoint()
        {
            if (_spots == null || _spots.walkable.Count == 0) return _venue.ToWorld((float)(_rng.NextDouble() * 20 - 10), (float)(_rng.NextDouble() * 16 - 8));
            var r = _spots.walkable[_rng.Next(_spots.walkable.Count)];
            float x = Mathf.Lerp(Mathf.Min(r.x0, r.x1) + 0.5f, Mathf.Max(r.x0, r.x1) - 0.5f, (float)_rng.NextDouble());
            float z = Mathf.Lerp(Mathf.Min(r.z0, r.z1) + 0.5f, Mathf.Max(r.z0, r.z1) - 0.5f, (float)_rng.NextDouble());
            return _venue.ToWorld(x, z);
        }

        private void PickTarget()
        {
            _target = RandomPoint();
            _speed = _rng.NextDouble() < 0.15 ? 4f : 1.5f + (float)_rng.NextDouble() * 0.6f;
        }

        private void Update()
        {
            if (Time.time < _waitUntil)
            {
                _view.SetSpeed(0f);
                _view.SetBase("loco_idle");
                return;
            }
            var to = _target - transform.position;
            to.y = 0;
            if (to.magnitude < 0.2f)
            {
                _waitUntil = Time.time + 1f + (float)_rng.NextDouble() * 4f;
                if (_rng.NextDouble() < 0.4) _view.PlayOneShot(_emotes[_rng.Next(_emotes.Length)]);
                PickTarget();
                return;
            }
            var step = to.normalized * _speed * Time.deltaTime;
            transform.position += Vector3.ClampMagnitude(step, to.magnitude);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 8f);
            _view.SetSpeed(_speed);
            _view.SetBase(_speed > 3.5f ? "loco_run" : "loco_walk");
        }
    }
}
