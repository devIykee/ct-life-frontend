using CTLife.World.Roads;
using CTLife.World.Vehicles;
using UnityEngine;

namespace CTLife.World.Pedestrians
{
    /// <summary>
    /// One ambient person on the street (Feel lane F6), driven by <see cref="PedestrianSystem"/> (no NavMesh, no physics
    /// body: a point moving along the walkable network, the avatar on it). Walks a sidewalk keeping to its right, turns at
    /// corners, now and then stops at a shop front or on the phone, sits on a bench, and crosses the road only at a
    /// crossing: on a signal, when the cars on that road have red; at a zebra, when no car is about to arrive. Steps
    /// aside for the player, and a car that hits them knocks them down (<see cref="IKnockdownOwner"/>); they get up and
    /// carry on from the nearest path.
    /// </summary>
    public sealed class Pedestrian : MonoBehaviour, IKnockdownOwner
    {
        public enum Mode { Walk, Pause, Phone, ToBench, Sit, FromBench, Wait, Cross, Stroll, Down }

        public AvatarView View { get; private set; }
        public Mode State { get; private set; }
        public string Archetype { get; private set; }
        /// <summary>Walking pace (m/s), set at spawn: 1.0 dawdle to 1.6 brisk.</summary>
        public float Pace { get; private set; }
        /// <summary>Crossings started while the cars on that road did not have red (signals) or a car was close (zebra).</summary>
        public int UnsafeStarts { get; private set; }
        public int Crossed { get; private set; }
        /// <summary>Where they are on the network (captures): the path, the distance along it and the way they walk it.</summary>
        public int PathIndex => _path;
        public float Along => _s;
        public int Direction => _dirSign;
        public Vector3 Heading => _face;
        public float Lateral => _lateral;

        private PedestrianSystem _sys;
        private PedestrianPaths _net;
        private System.Random _rng;
        private int _path = -1, _dirSign = 1, _crossing = -1, _area = -1;
        private float _s, _lateral, _speed, _timer, _nextStop, _waitStarted;
        private Vector3 _pos, _face, _target, _resume;
        private bool _crossFromA;
        private PedestrianSystem.BenchSeat _bench;
        private string _base;
        private float _blockedFor;

        public void Init(PedestrianSystem sys, AvatarView view, string archetype)
        {
            _sys = sys; View = view; Archetype = archetype;
        }

        /// <summary>Starts walking on <paramref name="path"/> at <paramref name="s"/>.</summary>
        public void Begin(PedestrianPaths net, int path, float s, System.Random rng)
        {
            _net = net; _rng = rng; _path = path; _s = s; _area = -1; _crossing = -1;
            _dirSign = rng.Next(2) == 0 ? 1 : -1;
            Pace = 1.0f + (float)rng.NextDouble() * 0.6f;
            _speed = Pace;
            _lateral = 0f;
            _nextStop = 6f + (float)rng.NextDouble() * 24f;
            State = Mode.Walk;
            UnsafeStarts = 0; Crossed = 0;
            var p = net.Paths[path];
            _pos = p.Sample(s, out var dir);
            _face = dir * _dirSign;
            transform.SetPositionAndRotation(_pos, Quaternion.LookRotation(_face));
            Play("loco_walk");
        }

        /// <summary>Starts strolling inside open area <paramref name="area"/> from <paramref name="at"/>.</summary>
        public void BeginStroll(PedestrianPaths net, int area, Vector3 at, System.Random rng)
        {
            _net = net; _rng = rng; _area = area; _path = -1; _crossing = -1;
            Pace = 0.9f + (float)rng.NextDouble() * 0.45f;
            _pos = at;
            _face = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * Vector3.forward;
            transform.SetPositionAndRotation(_pos, Quaternion.LookRotation(_face));
            _nextStop = 10f + (float)rng.NextDouble() * 20f;
            UnsafeStarts = 0; Crossed = 0;
            PickStrollTarget();
        }

        public void ReleaseBench()
        {
            if (_bench != null) { _bench.taken = false; _bench = null; }
        }

        private void Play(string anim)
        {
            if (_base == anim || View == null) return;
            _base = anim;
            View.SetBase(anim);
        }

        /// <summary>One step (the system calls it; <paramref name="player"/> is the local player's feet).</summary>
        public void Tick(float dt, Vector3 player, bool playerNear)
        {
            if (State == Mode.Down || _net == null) return;
            var before = _pos;
            float previousS = _s;
            int previousPath = _path;
            float want = 0f;
            switch (State)
            {
                case Mode.Walk: want = TickWalk(dt, player, playerNear); break;
                case Mode.Pause:
                case Mode.Phone:
                    _timer -= dt;
                    if (_timer <= 0f) { if (_area >= 0) PickStrollTarget(); else { State = Mode.Walk; Play("loco_walk"); } }
                    break;
                case Mode.ToBench: want = TickToward(dt, _target, Pace * 0.85f, () => { State = Mode.Sit; _timer = 10f + (float)_rng.NextDouble() * 18f; _face = _bench.forward; _pos = _bench.position; Play("act_sit"); }); break;
                case Mode.Sit:
                    _timer -= dt;
                    if (_timer <= 0f) { ReleaseBench(); State = Mode.FromBench; Play("loco_walk"); }
                    break;
                case Mode.FromBench: want = TickToward(dt, _resume, Pace * 0.85f, () => { State = _area >= 0 ? Mode.Stroll : Mode.Walk; if (_area >= 0) PickStrollTarget(); }); break;
                case Mode.Wait: want = TickWait(dt); break;
                case Mode.Cross: want = TickCross(dt); break;
                case Mode.Stroll: want = TickStroll(dt); break;
            }
            if ((State == Mode.Walk || State == Mode.Stroll || State == Mode.Cross) &&
                (!FootPlanner.Clear(before, _pos) || !VehicleWorld.Dry(_pos)))
            {
                _pos = before; want = 0f;
                if (_path == previousPath) _s = previousS;
                _blockedFor += dt;
                if (_blockedFor > 3f)
                {
                    if (State == Mode.Walk) _dirSign = -_dirSign;
                    else if (State == Mode.Stroll) PickStrollTarget();
                    _blockedFor = 0f;
                }
            }
            else _blockedFor = 0f;
            _speed = Mathf.MoveTowards(_speed, want, 2.5f * dt);
            if (View != null) View.SetSpeed(State == Mode.Sit || State == Mode.Pause || State == Mode.Phone ? 0f : _speed);
            var rot = Quaternion.LookRotation(_face.sqrMagnitude > 1e-6f ? _face : transform.forward);
            transform.SetPositionAndRotation(_pos, Quaternion.RotateTowards(transform.rotation, rot, 300f * dt));
        }

        // ------------------------------------------------------------------ sidewalks

        private float TickWalk(float dt, Vector3 player, bool playerNear)
        {
            var path = _net.Paths[_path];
            path.Sample(_s, out var dir);
            var heading = dir * _dirSign;
            var right = new Vector3(heading.z, 0f, -heading.x);
            float half = Mathf.Max(0.2f, path.width * 0.5f - 0.4f);
            // keep right (people walking the other way pass on the left), step aside for the player
            float lane = Mathf.Min(0.55f, half);
            float pace = Pace;
            foreach (var other in _sys.People)
            {
                if (other == null || other == this || other.State == Mode.Down) continue;
                var to = other.transform.position - _pos; to.y = 0f;
                float ahead = Vector3.Dot(to, heading), side = Vector3.Dot(to, right);
                if (ahead < 0f || ahead > 2f || Mathf.Abs(side) > 0.85f) continue;
                // Both walkers keep to their own right; a slower person ahead is yielded to, not walked through.
                lane = half;
                if (ahead < 0.85f && Mathf.Abs(side) < 0.65f) pace *= 0.15f;
            }
            if (playerNear)
            {
                var to = player - _pos; to.y = 0f;
                float ahead = Vector3.Dot(to, heading), side = Vector3.Dot(to, right);
                if (ahead > -0.5f && ahead < 2.6f && Mathf.Abs(side) < 1.4f)
                {
                    lane = side > 0f ? -half : half;          // away from the player's side
                    pace *= ahead < 1.0f && Mathf.Abs(side - _lateral) < 0.7f ? 0.15f : 0.7f;
                }
            }
            _lateral = Mathf.MoveTowards(_lateral, Mathf.Clamp(lane, -half, half), 1.2f * dt);
            float before = _s;
            _s += _dirSign * pace * dt;
            // a crossing passed on the way: sometimes take it
            foreach (var (x, at, atA) in path.exits)
            {
                bool passed = _dirSign > 0 ? before < at && _s >= at : before > at && _s <= at;
                if (!passed || _rng.NextDouble() > _sys.CrossChance) continue;
                _crossing = x; _crossFromA = atA; _s = at;
                var c = _net.Crossings[x];
                _target = atA ? c.b : c.a;
                _resume = atA ? c.a : c.b;   // the kerb end of the crossing: walk there, then wait
                State = Mode.Wait; _waitStarted = Time.time;
                return pace;
            }
            // the end of this path: on to another at the node
            if (_s < 0f || _s > path.Length)
            {
                int node = _s < 0f ? path.startNode : path.endNode;
                int next = _net.NextPath(node, _path, _rng);
                var np = _net.Paths[next];
                bool fromStart = np.startNode == node && (np.endNode != node || _rng.Next(2) == 0);
                if (next == _path) { _dirSign = -_dirSign; _s = Mathf.Clamp(_s, 0f, path.Length); }
                else { _path = next; _dirSign = fromStart ? 1 : -1; _s = fromStart ? 0f : np.Length; }
                path = np;
            }
            var centre = path.Sample(_s, out dir);
            heading = dir * _dirSign;
            right = new Vector3(heading.z, 0f, -heading.x);
            _pos = centre + right * _lateral;
            _face = heading;
            // now and then: a shop window, the phone, a bench
            _nextStop -= dt;
            if (_nextStop <= 0f) StopSomewhere(path, centre, right, half);
            return pace;
        }

        private void StopSomewhere(PedestrianPaths.Path path, Vector3 centre, Vector3 right, float half)
        {
            _nextStop = 18f + (float)_rng.NextDouble() * 32f;
            double r = _rng.NextDouble();
            if (r < 0.35 && _sys.TakeBench(_pos, 16f, out var bench))
            {
                _bench = bench; _resume = _pos; _target = bench.position;
                State = Mode.ToBench; Play("loco_walk");
                return;
            }
            if (r < 0.65 && !path.corner)
            {
                // the shop side: away from the road
                var outward = _sys.AwayFromRoad(centre);
                if (outward.sqrMagnitude > 0.5f)
                {
                    _lateral = Mathf.Clamp(Vector3.Dot(outward, right), -1f, 1f) * half * 0.9f;   // no jump when walking on
                    _pos = centre + right * _lateral;
                    _face = outward;
                    State = Mode.Pause; _timer = 3f + (float)_rng.NextDouble() * 6f;
                    Play("loco_idle");
                    return;
                }
            }
            if (r < 0.9)
            {
                _lateral = half;
                _pos = centre + right * half;
                State = Mode.Phone; _timer = 5f + (float)_rng.NextDouble() * 9f;
                Play("act_phone_scroll");
            }
        }

        // ------------------------------------------------------------------ crossings

        private float TickWait(float dt)
        {
            var kerb = _resume - _pos; kerb.y = 0f;
            if (kerb.sqrMagnitude > 0.04f) return TickToward(dt, _resume, Pace, () => { });
            var across = _target - _pos; across.y = 0f;
            if (across.sqrMagnitude > 1e-4f) _face = across.normalized;
            Play("loco_idle");
            var c = _net.Crossings[_crossing];
            bool clear = _sys.MayCross(c, out bool safe);
            float waited = Time.time - _waitStarted;
            // a zebra nobody stops for: after a while they step out anyway, and the traffic brakes for people
            if (!clear && !c.signal && waited > 10f) clear = true;
            if (!clear) return 0f;
            if (!safe) UnsafeStarts++;
            State = Mode.Cross;
            Play("loco_walk");
            return 0f;
        }

        private float TickCross(float dt)
        {
            var to = _target - _pos; float flat = new Vector2(to.x, to.z).magnitude;
            float pace = Pace * 1.12f;
            if (flat <= pace * dt + 0.05f)
            {
                var c = _net.Crossings[_crossing];
                _path = _crossFromA ? c.pathB : c.pathA;
                _s = _crossFromA ? c.sB : c.sA;
                _dirSign = _rng.Next(2) == 0 ? 1 : -1;
                _lateral = 0f;
                _crossing = -1;
                Crossed++;
                State = Mode.Walk;
                return pace;
            }
            _face = new Vector3(to.x, 0f, to.z) / flat;
            _pos += _face * pace * dt;
            _pos.y = Mathf.Lerp(_pos.y, _target.y, Mathf.Clamp01(dt * 2f));
            return pace;
        }

        // ------------------------------------------------------------------ open areas

        private void PickStrollTarget()
        {
            var a = _net.Areas[_area];
            var b = a.Bounds;
            for (int i = 0; i < 12; i++)
            {
                float x = b.xMin + (float)_rng.NextDouble() * b.width, z = b.yMin + (float)_rng.NextDouble() * b.height;
                if (!a.Contains(x, z)) continue;
                var p = new Vector3(x, _pos.y, z);
                if ((p - _pos).sqrMagnitude > 30f * 30f) p = _pos + (p - _pos).normalized * 30f;
                if (!a.Contains(p.x, p.z) || !_sys.ClearOfRoad(_pos, p)) continue;
                _target = p;
                State = Mode.Stroll;
                Play("loco_walk");
                return;
            }
            _target = _pos;
            State = Mode.Stroll;
        }

        private float TickStroll(float dt)
        {
            _nextStop -= dt;
            return TickToward(dt, _target, Pace, () =>
            {
                if (_nextStop <= 0f)
                {
                    _nextStop = 12f + (float)_rng.NextDouble() * 25f;
                    if (_rng.NextDouble() < 0.5 && _sys.TakeBench(_pos, 18f, out var bench))
                    {
                        _bench = bench; _resume = _pos; _target = bench.position;
                        State = Mode.ToBench; Play("loco_walk");
                        return;
                    }
                    State = _rng.NextDouble() < 0.5 ? Mode.Phone : Mode.Pause;
                    _timer = 4f + (float)_rng.NextDouble() * 8f;
                    Play(State == Mode.Phone ? "act_phone_scroll" : "loco_idle");
                    _resume = _pos;
                    return;
                }
                PickStrollTarget();
            });
        }

        /// <summary>Straight to <paramref name="target"/> on the ground (areas, benches); <paramref name="arrived"/> on reaching it.</summary>
        private float TickToward(float dt, Vector3 target, float pace, System.Action arrived)
        {
            var to = target - _pos; to.y = 0f;
            float d = to.magnitude;
            if (d <= pace * dt + 0.05f)
            {
                _pos = new Vector3(target.x, _pos.y, target.z);
                arrived();
                return 0f;
            }
            _face = to / d;
            _pos += _face * pace * dt;
            _pos.y = _sys.GroundY(_pos, _pos.y);
            if (State == Mode.Pause || State == Mode.Phone) return 0f;
            return pace;
        }

        // ------------------------------------------------------------------ knocked down by a car

        void IKnockdownOwner.PlaceRoot(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            _pos = position;
        }

        void IKnockdownOwner.SetControl(bool enabled)
        {
            if (!enabled)
            {
                ReleaseBench();
                State = Mode.Down;
                return;
            }
            // up again: on with the walk from the nearest sidewalk (or the area they were in)
            _pos = transform.position;
            _base = null;
            if (_area >= 0 && _net.Areas[_area].Contains(_pos.x, _pos.z)) { PickStrollTarget(); return; }
            var (path, s) = _net.Nearest(_pos, 12f, false);
            if (path < 0) { _sys.Retire(this); return; }
            _area = -1; _path = path; _s = s; _crossing = -1;
            _resume = _net.Paths[path].Sample(s, out _);
            State = Mode.FromBench;   // walk back onto it, then on along it
            Play("loco_walk");
        }
    }
}
