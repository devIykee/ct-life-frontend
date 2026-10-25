using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Rekt Burger's clock-in loop, Sims style (2026-10-25): while <see cref="RektBurgerHooks.OnShift"/> and the player is in
    /// the restaurant, their avatar works the line: it walks (the real body, <see cref="LocalPlayerController.MoveTo"/>, so
    /// it moves as any walk does) through the staff way in by the right wall to the flat-top, the fryers, the prep fridge
    /// and the pass, and at each one plays a real mocap work clip on the spot (a local engagement, never sent). The stick
    /// takes over at any time; once the player is back in the room and idle for a moment the loop picks up where it was.
    /// Nothing plays off shift. Created by <see cref="RektBurgerFloor"/>; <see cref="RektBurgerCustomers"/> hears the pass.
    /// </summary>
    [RequireComponent(typeof(InteriorRoom))]
    public sealed class RektBurgerWork : MonoBehaviour
    {
        /// <summary>A station in the room's source frame (x right seen from the door, y in), the way the worker faces
        /// (+1 the back wall, -1 the dining room), its clip and how long it is held (one-shots repeat inside that time).</summary>
        public struct Station
        {
            public string id, clip; public float x, y, face, hold; public bool oneShot;
        }

        public static readonly Station[] Stations =
        {
            new Station { id = "grill", x = -4.15f, y = 10.85f, face = 1f, clip = "act_rb_grill", hold = 4.5f },
            new Station { id = "fryer", x = -2.55f, y = 10.85f, face = 1f, clip = "act_rb_fryer", hold = 3.7f, oneShot = true },
            new Station { id = "prep", x = -0.4f, y = 10.85f, face = 1f, clip = "act_rb_prep", hold = 4.27f },
            new Station { id = "pass", x = -0.3f, y = 7.25f, face = -1f, clip = "act_rb_hand_over", hold = 4.2f, oneShot = true },
        };

        /// <summary>From the dining room to the kitchen: up the middle aisle, along the counter's front (between the chair backs
        /// and the counter), through the staff way in by the right wall, then behind the counter. RektBurgerCheck walks it.</summary>
        public static readonly Vector2[] WayIn = { new Vector2(0f, 5.6f), new Vector2(4.45f, 5.6f), new Vector2(4.45f, 7.5f), new Vector2(2.6f, 8.4f) };

        public const float ResumeAfter = 2.5f, Reach = 0.22f;
        private const string EngageKey = "rekt:station";

        /// <summary>The station being worked (-1: walking or idle) and how many have been worked (captures, the dev route).</summary>
        public int Current { get; private set; } = -1;
        public int Worked { get; private set; }
        public bool Running => _phase != Phase.Idle;

        /// <summary>The pass handed an order over (the front customer takes their bag).</summary>
        public static event System.Action Served;

        private enum Phase { Idle, WayIn, ToStation, AtStation }

        private InteriorRoom _room;
        private Phase _phase;
        private int _next, _way;
        private float _until, _quietSince, _nextShot, _settleAt, _checkAt;
        private bool _inKitchen;

        private void Awake() => _room = GetComponent<InteriorRoom>();

        private void OnDisable() => Stop("room off");

        private LocalPlayerController Me()
        {
            var s = VenueSession.Current;
            return s != null ? s.LocalPlayer : null;
        }

        private bool PlayerHere()
        {
            var ii = IslandInteriors.Current;
            return ii != null && ii.PlayerInside && ii.ActiveRoom == _room;
        }

        private Vector3 P(float x, float y) => transform.TransformPoint(_room.FromSource(x, y, 0f));

        private float Yaw(float face)
        {
            var d = transform.TransformDirection(_room.FromSource(0f, face, 0f));
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        private void Update()
        {
            var me = Me();
            if (me == null) return;
            bool want = RektBurgerHooks.OnShift && PlayerHere();
            if (!want) { if (_phase != Phase.Idle) Stop(RektBurgerHooks.OnShift ? "left the room" : "off shift"); return; }
            // the player's own thumb (or a seat of their own) always wins; the loop waits until they are idle again
            bool ours = me.SeatedSpot == EngageKey;
            bool busy = me.DirectInputActive || (!ours && !string.IsNullOrEmpty(me.SeatedSpot)) || me.InVehicle;
            if (busy)
            {
                _quietSince = Time.time;
                if (_phase != Phase.Idle) Stop("the player took over");
                return;
            }
            if (_phase == Phase.Idle)
            {
                if (Time.time - _quietSince < ResumeAfter || me.IsMoving) return;
                var local = transform.InverseTransformPoint(me.transform.position);
                _inKitchen = SourceY(local) > 6.85f;                     // behind the counter already
                _way = 0;
                _phase = _inKitchen ? Phase.ToStation : Phase.WayIn;
                Debug.Log($"CTLIFE [rekt-work] working ({(_inKitchen ? "in the kitchen" : "from the dining room")}), next {Stations[_next].id}");
                Go(me);
                return;
            }
            switch (_phase)
            {
                case Phase.WayIn:
                case Phase.ToStation:
                    // the camera swings round behind the walk (prep to pass heads straight at a lens left behind at the prep)
                    if (Time.time >= _settleAt) { CameraRig.Instance?.SettleBehind(); _settleAt = Time.time + 0.5f; }
                    if (me.IsMoving) return;
                    if (_phase == Phase.WayIn && ++_way < WayIn.Length) { Go(me); return; }
                    if (_phase == Phase.WayIn) { _phase = Phase.ToStation; _inKitchen = true; Go(me); return; }
                    Arrive(me);
                    return;
                case Phase.AtStation:
                    var st = Stations[_next];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (_checkAt > 0f && Time.time >= _checkAt) { _checkAt = 0f; LogFacing(me, st); }
#endif
                    if (st.oneShot && Time.time >= _nextShot && Time.time < _until - 0.5f) Shot(me, st);
                    if (Time.time < _until) return;
                    if (me.SeatedSpot == EngageKey) me.StandIfSeated();
                    Current = -1;
                    _next = (_next + 1) % Stations.Length;
                    _phase = Phase.ToStation;
                    Go(me);
                    return;
            }
        }

        private float SourceY(Vector3 local) => local.z * _room.axisZ;

        private void Go(LocalPlayerController me)
        {
            var target = _phase == Phase.WayIn ? P(WayIn[_way].x, WayIn[_way].y) : P(Stations[_next].x, Stations[_next].y);
            if (!me.MoveTo(target)) { Stop("MoveTo refused"); _quietSince = Time.time; return; }
            _settleAt = Time.time + 0.35f;                               // once the body has turned toward the way
        }

        private void Arrive(LocalPlayerController me)
        {
            var st = Stations[_next];
            // the walk stops short of the equipment's colliders; the held pose stands up to it (hands over the grill)
            var at = P(st.x, st.y + st.face * Reach);
            at.y = me.transform.position.y;
            if (!me.EngageLocal(EngageKey, at, Yaw(st.face), st.oneShot ? "loco_idle" : st.clip, null, 0f, standing: true))
            {
                Stop("engage refused"); _quietSince = Time.time; return;
            }
            Current = _next;
            Worked++;
            _phase = Phase.AtStation;
            _until = Time.time + st.hold;
            _nextShot = Time.time + 0.35f;
            _checkAt = Time.time + 1.0f;
            Debug.Log($"CTLIFE [rekt-work] at the {st.id} ({st.clip}, {st.hold:0.0} s)");
        }

        /// <summary>Dev check: the body (shoulder line) faces the station's way, not just the actor's root.</summary>
        private void LogFacing(LocalPlayerController me, Station st)
        {
            var view = me.GetComponent<AvatarView>();
            var a = view != null ? view.Animator : null;
            var l = a != null ? a.GetBoneTransform(HumanBodyBones.LeftUpperArm) : null;
            var r = a != null ? a.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
            if (l == null || r == null) return;
            var f = Vector3.Cross(r.position - l.position, Vector3.up);
            float body = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            Debug.Log($"CTLIFE [rekt-work] facing at the {st.id}: wanted {Yaw(st.face):0}, root {me.transform.eulerAngles.y:0}, " +
                      $"body {body:0} (off {Mathf.DeltaAngle(Yaw(st.face), body):0}), view root {view.transform.eulerAngles.y:0}");
        }

        private void Shot(LocalPlayerController me, Station st)
        {
            var view = me.GetComponent<AvatarView>();
            if (view == null) return;
            view.PlayOneShot(st.clip);
            _nextShot = Time.time + (st.id == "pass" ? st.hold : 1.85f);
            if (st.id == "pass") Served?.Invoke();
        }

        private void Stop(string why)
        {
            if (_phase == Phase.Idle) return;
            var me = Me();
            if (me != null && me.SeatedSpot == EngageKey) me.StandIfSeated();
            else if (me != null && (_phase == Phase.WayIn || _phase == Phase.ToStation) && me.IsMoving && !me.DirectInputActive) me.MoveTo(me.transform.position);
            _phase = Phase.Idle;
            Current = -1;
            Debug.Log("CTLIFE [rekt-work] paused: " + why);
        }
    }
}
