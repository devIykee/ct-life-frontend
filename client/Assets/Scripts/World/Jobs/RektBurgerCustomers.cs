using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Rekt Burger's customers while the player works a shift (2026-10-25): street people (the pedestrian looks and walk,
    /// ambient: no tag, no shadows, the avatar LOD) come in through the door, queue at the counter, say their order (an
    /// original CT order line in a bubble), wait for the pass (<see cref="RektBurgerWork.Served"/>, or a while), take a bag
    /// and walk out. One to three at a time, pooled. Only while <see cref="RektBurgerHooks.OnShift"/> and the player is in
    /// the room; otherwise there is nobody. Local scenery: nothing is sent.
    /// </summary>
    [RequireComponent(typeof(InteriorRoom))]
    public sealed class RektBurgerCustomers : MonoBehaviour
    {
        public static readonly string[] Lines =
        {
            "One Rug Burger, extra cope.",
            "Fries please. I got liquidated.",
            "Whatever's cheapest, I'm down 90%.",
            "Diamond Hands Shake. Holding it till it melts.",
            "Exit Liquidity Combo, to go. Fast.",
            "Paper Hands Nuggets. I'll sell them halfway.",
            "Can I pay in airdrop points?",
            "Two Rekt Melts. One's for my bags.",
            "Ser, this is a Rekt Burger. Large fries.",
            "Bag Holder Wrap. Obviously.",
            "Gas Fee Soda. Why is it more today?",
            "Just a water. Shorted the top again.",
        };

        public const int MaxCustomers = 3;
        public const float Walk = 1.25f;

        /// <summary>Customers in the room now and served so far (captures, the dev route).</summary>
        public int Active => _people.Count;
        public int Served { get; private set; }

        // room source frame (x right seen from the door, y in): the street outside, the doorway, the queue line, the way out
        private static readonly Vector2 Street = new Vector2(0.4f, -3.2f), Outside = new Vector2(0f, -0.7f), Inside = new Vector2(0f, 0.8f);
        private static readonly Vector2[] Queue = { new Vector2(-0.6f, 5.2f), new Vector2(-0.6f, 4.3f), new Vector2(-0.6f, 3.4f) };
        private static readonly Vector2[] WayOut = { new Vector2(-1.6f, 4.6f), new Vector2(-1.6f, 1.4f), new Vector2(0f, 0.8f), new Vector2(0f, -0.7f), new Vector2(-0.6f, -3.4f) };

        private enum Step { In, Queue, Waiting, Out }

        private sealed class Person
        {
            public AvatarView view; public GameObject bag;
            public readonly List<Vector3> path = new List<Vector3>();
            public Step step; public int slot = -1; public float waitUntil; public Vector3 face; public bool said;
        }

        private InteriorRoom _room;
        private readonly List<Person> _people = new List<Person>();
        private readonly List<AvatarView> _pool = new List<AvatarView>();
        private string[] _looks;
        private int _nextLook, _nextLine, _target = 2;
        private float _spawnAt;
        private bool _served;
        private System.Random _rng;
        private Material _bagMat, _bandMat;

        private void Awake()
        {
            _room = GetComponent<InteriorRoom>();
            _rng = new System.Random(System.Environment.TickCount);
        }

        private void OnEnable() => RektBurgerWork.Served += OnServed;

        private void OnDisable()
        {
            RektBurgerWork.Served -= OnServed;
            Clear();
        }

        private void OnDestroy()
        {
            foreach (var v in _pool) if (v != null) Destroy(v.gameObject);
            if (_bagMat != null) Destroy(_bagMat);
            if (_bandMat != null) Destroy(_bandMat);
        }

        private void OnServed() => _served = true;

        private Vector3 P(Vector2 s) => transform.TransformPoint(_room.FromSource(s.x, s.y, 0f));

        private bool Running()
        {
            var ii = IslandInteriors.Current;
            return RektBurgerHooks.OnShift && ii != null && ii.PlayerInside && ii.ActiveRoom == _room;
        }

        private void Update()
        {
            if (!Running()) { if (_people.Count > 0) Clear(); _spawnAt = Time.time + 2f; return; }
            float dt = Time.deltaTime;
            if (_people.Count < _target && Time.time >= _spawnAt) Spawn();
            bool nearDoor = false;
            for (int i = _people.Count - 1; i >= 0; i--)
            {
                var p = _people[i];
                bool arrived = Move(p, dt);
                var local = transform.InverseTransformPoint(p.view.transform.position);
                if (Mathf.Abs(local.z) < 3.0f && Mathf.Abs(local.x) < 1.6f) nearDoor = true;
                switch (p.step)
                {
                    case Step.In:
                        if (arrived) { p.step = Step.Queue; p.face = transform.TransformDirection(_room.FromSource(0f, 1f, 0f)); }
                        break;
                    case Step.Queue:
                        if (p.slot == 0 && arrived)
                        {
                            p.step = Step.Waiting;
                            p.waitUntil = Time.time + 14f;
                            _served = false;
                            Say(p);
                        }
                        break;
                    case Step.Waiting:
                        if (_served || Time.time >= p.waitUntil)
                        {
                            _served = false;
                            Served++;
                            GiveBag(p);
                            Leave(p);
                            Advance();
                        }
                        break;
                    case Step.Out:
                        if (arrived) { Recycle(p); _people.RemoveAt(i); }
                        break;
                }
            }
            if (nearDoor) IslandInteriors.Current?.HoldDoorOpen(1.0f);
        }

        // ------------------------------------------------------------------ people

        private void Spawn()
        {
            _spawnAt = Time.time + 6f + (float)_rng.NextDouble() * 5f;
            int slot = 0;
            foreach (var q in _people) if (q.step != Step.Out) slot++;
            if (slot >= Queue.Length) return;
            var view = Take();
            if (view == null) return;
            var p = new Person { view = view, step = Step.In, slot = slot };
            view.transform.SetPositionAndRotation(P(Street), Quaternion.LookRotation(Flat(P(Outside) - P(Street))));
            view.gameObject.SetActive(true);
            view.SetBase("loco_idle");
            p.path.Add(P(Outside)); p.path.Add(P(Inside)); p.path.Add(P(new Vector2(-0.6f, 1.4f)));
            for (int k = Queue.Length - 1; k >= slot; k--) p.path.Add(P(Queue[k]));
            _people.Add(p);
            _target = 1 + _rng.Next(MaxCustomers);
            Debug.Log($"CTLIFE [rekt-customers] in ({_people.Count} in the room, slot {slot})");
        }

        private void Say(Person p)
        {
            if (p.said) return;
            p.said = true;
            string line = Lines[_nextLine++ % Lines.Length];
            if (p.view.HeadAnchor != null) Bus.Publish(new BubbleSignal { anchor = p.view.HeadAnchor, text = line, isNpc = true, seconds = 4.5f });
            Debug.Log("CTLIFE [rekt-customers] order: " + line);
        }

        private void Leave(Person p)
        {
            p.step = Step.Out;
            p.slot = -1;
            p.path.Clear();
            foreach (var w in WayOut) p.path.Add(P(w));
        }

        private void Advance()
        {
            foreach (var q in _people)
            {
                if (q.slot <= 0 || q.step == Step.Out) continue;
                q.slot--;
                q.path.Add(P(Queue[q.slot]));          // on along the queue line (whoever is still coming in keeps their way in)
            }
        }

        /// <summary>One step along the person's path: turn toward the next point first, walk only when facing it (no
        /// sideways or backwards drift), the walk clip at the speed actually moved. True once the path is done.</summary>
        private bool Move(Person p, float dt)
        {
            var t = p.view.transform;
            float speed = 0f;
            while (p.path.Count > 0 && Flat(p.path[0] - t.position).magnitude < 0.12f) p.path.RemoveAt(0);
            if (p.path.Count > 0)
            {
                var to = Flat(p.path[0] - t.position);
                var want = Quaternion.LookRotation(to.normalized);
                t.rotation = Quaternion.RotateTowards(t.rotation, want, 360f * dt);
                if (Quaternion.Angle(t.rotation, want) < 35f)
                {
                    speed = Walk;
                    var pos = t.position + t.forward * Mathf.Min(Walk * dt, to.magnitude);
                    pos.y = p.path[0].y;
                    t.position = pos;
                }
            }
            else if (p.face.sqrMagnitude > 0.01f) t.rotation = Quaternion.RotateTowards(t.rotation, Quaternion.LookRotation(Flat(p.face)), 240f * dt);
            p.view.SetSpeed(speed);
            return p.path.Count == 0;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-6f ? Vector3.forward : v; }

        // ------------------------------------------------------------------ bodies

        private AvatarView Take()
        {
            for (int i = 0; i < _pool.Count; i++)
                if (_pool[i] != null && !_pool[i].gameObject.activeSelf) { var v = _pool[i]; return v; }
            if (_pool.Count >= MaxCustomers) return null;
            try
            {
                var view = AvatarFactory.Create("rekt_customer", null, true, NextLook(), transform.parent);
                if (view == null) return null;
                view.ambient = true;
                view.cullDistance = 30f;
                foreach (var r in view.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                view.gameObject.SetActive(false);
                _pool.Add(view);
                return view;
            }
            catch (System.Exception e)
            {
                Log.Warn("rekt", "customer avatar: " + e.Message);
                return null;
            }
        }

        private string NextLook()
        {
            if (_looks == null)
            {
                var keys = new List<string>();
                var reg = CharacterRegistry.Instance;
                if (reg != null) foreach (var e in reg.archetypes) if (e != null && e.prefab != null && !string.IsNullOrEmpty(e.key)) keys.Add(e.key);
                for (int i = keys.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); (keys[i], keys[j]) = (keys[j], keys[i]); }
                _looks = keys.ToArray();
            }
            return _looks.Length == 0 ? null : _looks[_nextLook++ % _looks.Length];
        }

        private void Recycle(Person p)
        {
            if (p.bag != null) p.bag.SetActive(false);
            p.view.SetSpeed(0f);
            p.view.gameObject.SetActive(false);
        }

        private void Clear()
        {
            foreach (var p in _people) if (p.view != null) Recycle(p);
            _people.Clear();
        }

        /// <summary>A Rekt Burger takeaway bag in the right hand: cream paper, the orange band.</summary>
        private void GiveBag(Person p)
        {
            var hand = p.view.Animator != null ? p.view.Animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (hand == null) return;
            var bag = hand.Find("rb_bag")?.gameObject;
            if (bag == null)
            {
                if (_bagMat == null)
                {
                    var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
                    _bagMat = new Material(sh) { name = "rb_bag", enableInstancing = true };
                    _bagMat.SetColor("_BaseColor", new Color(0.93f, 0.86f, 0.72f));
                    _bagMat.SetFloat("_Smoothness", 0.15f);
                    _bandMat = new Material(sh) { name = "rb_bag_band", enableInstancing = true };
                    _bandMat.SetColor("_BaseColor", new Color(1f, 0.42f, 0.17f));
                    _bandMat.SetFloat("_Smoothness", 0.2f);
                }
                bag = new GameObject("rb_bag");
                bag.transform.SetParent(hand, false);
                Part(bag.transform, new Vector3(0f, -0.16f, 0f), new Vector3(0.2f, 0.24f, 0.12f), _bagMat);
                Part(bag.transform, new Vector3(0f, -0.1f, 0f), new Vector3(0.205f, 0.05f, 0.125f), _bandMat);
            }
            // hang it below the hand whatever the bone's own axes: world down, the bag's face along the body
            bag.transform.rotation = Quaternion.LookRotation(p.view.transform.forward, Vector3.up);
            bag.SetActive(true);
        }

        private static void Part(Transform parent, Vector3 pos, Vector3 size, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localScale = size;
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
