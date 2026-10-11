using System;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// NPC drivers for traffic (Feel lane F3): a few cast avatars (made one at a time, lazily, then reused) sit at the wheel of
    /// the nearest cars the camera can see, in the seated driving pose with both hands on the wheel (or the bars). Cars
    /// farther away drive with an empty seat behind their tinted glass, which nobody can tell.
    /// </summary>
    public sealed class TrafficDrivers : IDisposable
    {
        public const int Max = 5;
        public const float AttachWithin = 55f, DetachBeyond = 70f;

        private readonly TrafficSystem _sys;
        private readonly List<AvatarView> _free = new List<AvatarView>();
        private readonly Dictionary<TrafficCar, AvatarView> _seated = new Dictionary<TrafficCar, AvatarView>();
        private readonly List<AvatarView> _all = new List<AvatarView>();
        private float _createAt, _scanAt;
        private string[] _archetypes;
        private int _next;
        private readonly List<TrafficCar> _detach = new List<TrafficCar>();

        public TrafficDrivers(TrafficSystem sys) { _sys = sys; }

        public int Seated => _seated.Count;
        /// <summary>From the last scan: cars within <see cref="AttachWithin"/> and in view, and how many of those have no driver seat.</summary>
        public int NearInView { get; private set; }
        public int NearWithoutSeat { get; private set; }

        private string NextArchetype()
        {
            if (_archetypes == null)
            {
                var keys = new List<string>();
                var reg = CharacterRegistry.Instance;
                if (reg != null) foreach (var e in reg.archetypes) if (e != null && e.prefab != null && !string.IsNullOrEmpty(e.key)) keys.Add(e.key);
                _archetypes = keys.ToArray();
            }
            if (_archetypes.Length == 0) return null;
            return _archetypes[(_next++ * 7 + 3) % _archetypes.Length];
        }

        private AvatarView Create()
        {
            try
            {
                var view = AvatarFactory.Create("traffic-driver", null, true, NextArchetype(), _sys.transform);
                if (view == null) return null;
                // a passenger in a moving car: no bodies to bump, no shadow blob on the road under the car
                foreach (var c in view.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                var blob = view.transform.Find("blob_shadow");
                if (blob != null) blob.gameObject.SetActive(false);
                view.gameObject.SetActive(false);
                _all.Add(view);
                return view;
            }
            catch (Exception e)
            {
                Log.Warn("traffic", "driver avatar: " + e.Message);
                return null;
            }
        }

        public void Update(List<TrafficCar> cars)
        {
            if (Time.time < _scanAt) return;
            _scanAt = Time.time + 0.4f;
            var cam = Camera.main;
            if (cam == null) return;
            var eye = cam.transform.position;
            // off the cars that went away or out of range
            _detach.Clear();
            foreach (var kv in _seated)
                if (kv.Key == null || !kv.Key.gameObject.activeInHierarchy || (kv.Key.transform.position - eye).sqrMagnitude > DetachBeyond * DetachBeyond)
                    _detach.Add(kv.Key);
            foreach (var c in _detach) Detach(c);
            // onto the nearest seen cars without one
            TrafficCar best = null; float bestD = AttachWithin * AttachWithin;
            int near = 0, noSeat = 0;
            foreach (var c in cars)
            {
                if (c == null) continue;
                float d = (c.transform.position - eye).sqrMagnitude;
                if (d >= AttachWithin * AttachWithin) continue;
                var vp = cam.WorldToViewportPoint(c.transform.position);
                if (vp.z < 0f || vp.x < -0.2f || vp.x > 1.2f || vp.y < -0.2f || vp.y > 1.2f) continue;
                near++;
                if (c.Vehicle == null || c.Vehicle.Seat == null) { noSeat++; continue; }
                if (_seated.ContainsKey(c) || d >= bestD) continue;
                best = c; bestD = d;
            }
            NearInView = near; NearWithoutSeat = noSeat;
            if (best == null) return;
            AvatarView view = null;
            if (_free.Count > 0) { view = _free[_free.Count - 1]; _free.RemoveAt(_free.Count - 1); }
            else if (_all.Count < Max && Time.time >= _createAt) { _createAt = Time.time + 0.6f; view = Create(); }
            if (view == null) return;
            Seat(best, view);
        }

        private void Seat(TrafficCar car, AvatarView view)
        {
            var seat = car.Vehicle.Seat;
            view.transform.SetParent(car.transform, false);
            view.transform.localRotation = Quaternion.identity;
            view.gameObject.SetActive(true);
            view.SetBase("act_drive");
            var a = view.Animator;
            Vector3 hips = new Vector3(0f, 0.5f, 0.05f);
            if (a != null)
            {
                a.Update(0.6f);   // into the seated pose now, so the hips can be measured
                var h = a.isHuman ? a.GetBoneTransform(HumanBodyBones.Hips) : null;
                if (h != null) hips = Quaternion.Inverse(view.transform.rotation) * (h.position - view.transform.position);
            }
            view.transform.position = seat.position - car.transform.rotation * hips;
            car.Driver = view;
            _seated[car] = view;
        }

        public void Detach(TrafficCar car)
        {
            if (car == null || !_seated.TryGetValue(car, out var view)) return;
            _seated.Remove(car);
            car.Driver = null;
            if (view == null) return;
            view.gameObject.SetActive(false);
            view.transform.SetParent(_sys.transform, false);
            _free.Add(view);
        }

        /// <summary>Hands follow the wheel as the car steers.</summary>
        public void LateUpdate()
        {
            foreach (var kv in _seated) if (kv.Key != null && kv.Value != null) VehicleSystem.GripWheel(kv.Key.Vehicle, kv.Value);
        }

        public void Dispose()
        {
            foreach (var v in _all) if (v != null) Object.Destroy(v.gameObject);
            _all.Clear(); _free.Clear(); _seated.Clear();
        }
    }
}
