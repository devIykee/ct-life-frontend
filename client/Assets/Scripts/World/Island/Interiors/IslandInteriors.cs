using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Walk-in rooms behind real doors on the island (World v3, 2026-10-23). dress.py marks the enterable doors
    /// (Resources/World/Island/interior_doors.json: the door's floor centre, the facade's yaw, the room id and the building's
    /// box); this streams the room in behind the door the player approaches, with no fade and no loading screen:
    /// - every enterable door within <see cref="LeafRadius"/> gets a door leaf (the facade's opening is real, so a closed
    ///   leaf stands in it until the room behind is there); the room prefab is loaded ahead (async) and placed when the
    ///   player comes within <see cref="LoadRadius"/>, pooled, and put away beyond <see cref="UnloadRadius"/>;
    /// - the leaf swings open while the player is within <see cref="OpenRadius"/> of the door (either side) and the room is in;
    /// - inside the doorway and the room the player stops colliding with the building's own footprint collider (the room's
    ///   walls hold them instead); the room's sofas and chairs join the interaction catalog (local "Sit");
    /// - with the camera inside and the leaf shut nothing outside can be seen: the island's kit and ground layers are not
    ///   drawn; the characters get an indoor light (dimmer sun without shadows, warm ambient) while they are inside.
    /// </summary>
    public sealed class IslandInteriors : MonoBehaviour
    {
        public const string DoorsPath = "World/Island/interior_doors";
        public const string RoomsPath = "World/Interiors/";
        public const float LeafRadius = 70f, PreloadRadius = 90f, LoadRadius = 20f, UnloadRadius = 36f, OpenRadius = 3.4f, Every = 0.2f;

        [Serializable] private sealed class DoorRow { public float[] p; public float yaw; public float floor; public string room; public string building; public float[] box; }
        [Serializable] private sealed class DoorFile { public DoorRow[] doors; }

        private sealed class Door
        {
            public int index;
            public Vector3 pos;            // floor centre of the opening (the room's floor height)
            public Quaternion rot;         // +Z out of the building
            public string room;
            public Vector3 boxCenter, boxSize;
            public Leaf leaf;
        }

        private sealed class Leaf { public GameObject go; public Transform hinge; public float angle; public Door door; }

        private sealed class Active
        {
            public Door door;
            public InteriorRoom room;
            public readonly List<Collider> building = new List<Collider>();
            public bool ignoring;
            public CharacterController body;
        }

        public static IslandInteriors Current { get; private set; }
        /// <summary>The room the player is in now (null outside every room) and its door index (perf harness, captures).</summary>
        public InteriorRoom ActiveRoom => _active?.room;
        public bool PlayerInside { get; private set; }
        public bool WorldHidden { get; private set; }
        public int DoorCount => _doors.Length;

        /// <summary>A walk-in door: its floor centre and facing (+Z out of the building).</summary>
        public bool TryGetDoor(int index, out Vector3 position, out Quaternion rotation)
        {
            bool ok = index >= 0 && index < _doors.Length;
            position = ok ? _doors[index].pos : default; rotation = ok ? _doors[index].rot : Quaternion.identity;
            return ok;
        }

        private Door[] _doors = Array.Empty<Door>();
        private readonly Dictionary<string, ResourceRequest> _loads = new Dictionary<string, ResourceRequest>();
        private readonly Dictionary<string, InteriorRoom> _pool = new Dictionary<string, InteriorRoom>();
        private readonly List<Leaf> _leaves = new List<Leaf>();
        private readonly List<Leaf> _spare = new List<Leaf>();
        private Active _active;
        private float _next;
        private Material _leafMat, _handleMat;
        private Camera _maskedCam;
        private int _savedMask;
        private float _light;                                   // 0 outdoors .. 1 deep inside
        private float _sunIntensity; private LightShadows _sunShadows; private Light _sun;
        private Color _ambSky, _ambEq, _ambGround; private bool _lightSaved;

        private static readonly int HiddenLayers = (1 << 4) | (1 << 10) | (1 << 11) | (1 << 12) | (1 << 13) | (1 << 14) | (1 << 15) | (1 << 16);

        public static IslandInteriors Create(Transform parent)
        {
            var go = new GameObject("Island interiors");
            go.transform.SetParent(parent, false);
            return go.AddComponent<IslandInteriors>();
        }

        private void Awake()
        {
            Current = this;
            var ta = Resources.Load<TextAsset>(DoorsPath);
            if (ta == null) { Debug.Log("CTLIFE [interiors] no interior_doors (none on this island build)"); return; }
            var file = JsonUtility.FromJson<DoorFile>(ta.text);
            var list = new List<Door>();
            if (file?.doors != null)
                foreach (var r in file.doors)
                {
                    if (r?.p == null || r.p.Length < 3 || string.IsNullOrEmpty(r.room)) continue;
                    var d = new Door
                    {
                        index = list.Count, room = r.room,
                        pos = new Vector3(r.p[0], r.floor > 0f || r.floor < 0f ? r.floor : r.p[1], r.p[2]),
                        rot = Quaternion.Euler(0f, r.yaw, 0f),
                    };
                    if (r.box != null && r.box.Length >= 7)
                    {
                        d.boxCenter = new Vector3(r.box[0], r.box[1], r.box[2]);
                        d.boxSize = new Vector3(r.box[3], r.box[4], r.box[5]);
                    }
                    list.Add(d);
                }
            _doors = list.ToArray();
            Debug.Log($"CTLIFE [interiors] {_doors.Length} walk-in doors");
        }

        private void OnDestroy()
        {
            RestoreView();
            ApplyLight(0f, force: true);
            if (_active != null) Deactivate();
            if (ReferenceEquals(Current, this)) Current = null;
            if (_leafMat != null) Destroy(_leafMat);
            if (_handleMat != null) Destroy(_handleMat);
        }

        private Transform Player()
        {
            var me = VenueSession.Current != null ? VenueSession.Current.LocalPlayer : null;
            return me != null ? me.transform : null;
        }

        private void Update()
        {
            if (_doors.Length == 0) return;
            var player = Player();
            if (player == null) return;
            var p = player.position;
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + Every;
                Plan(p, player);
            }
            AnimateLeaves(p);
            UpdateInside(p);
        }

        // ------------------------------------------------------------------ streaming

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        private void Plan(Vector3 p, Transform player)
        {
            Door nearest = null;
            float best = float.MaxValue;
            // leaves round every door near enough to see its opening; rooms loaded ahead
            for (int i = 0; i < _leaves.Count; i++)
            {
                var l = _leaves[i];
                if (Flat(l.door.pos, p) > LeafRadius + 10f) { l.door.leaf = null; l.go.SetActive(false); _spare.Add(l); _leaves.RemoveAt(i--); }
            }
            foreach (var d in _doors)
            {
                float dist = Flat(d.pos, p);
                if (dist < PreloadRadius && !_loads.ContainsKey(d.room)) _loads[d.room] = Resources.LoadAsync<GameObject>(RoomsPath + d.room);
                if (dist < LeafRadius && d.leaf == null) d.leaf = TakeLeaf(d);
                if (dist < best) { best = dist; nearest = d; }
            }
            if (_active != null && (Flat(_active.door.pos, p) > UnloadRadius || (nearest != _active.door && best < LoadRadius && !PlayerInside)))
                Deactivate();
            if (_active == null && nearest != null && best < LoadRadius) Activate(nearest, player);
        }

        private void Activate(Door d, Transform player)
        {
            if (!_loads.TryGetValue(d.room, out var req)) { _loads[d.room] = Resources.LoadAsync<GameObject>(RoomsPath + d.room); return; }
            if (!req.isDone) return;
            if (!_pool.TryGetValue(d.room, out var room) || room == null)
            {
                var prefab = req.asset as GameObject;
                if (prefab == null) { Debug.LogWarning("CTLIFE [interiors] room prefab missing: " + d.room); return; }
                var go = Instantiate(prefab, transform);
                go.name = "room_" + d.room;
                room = go.GetComponent<InteriorRoom>();
                _pool[d.room] = room;
            }
            // a few centimetres behind the facade's plane, so the room's own front wall never fights the building's
            room.transform.SetPositionAndRotation(d.pos + d.rot * new Vector3(0f, 0f, -0.04f), d.rot);
            room.gameObject.SetActive(true);
            var a = new Active { door = d, room = room, body = player.GetComponent<CharacterController>() };
            if (d.boxSize != Vector3.zero)
            {
                var hits = Physics.OverlapBox(d.boxCenter, d.boxSize * 0.5f + Vector3.one * 0.5f, d.rot, 1 << 12, QueryTriggerInteraction.Ignore);
                foreach (var c in hits)
                {
                    var t = c.transform.position;
                    if (Mathf.Abs(t.x - d.boxCenter.x) < 0.8f && Mathf.Abs(t.z - d.boxCenter.z) < 0.8f) a.building.Add(c);
                }
            }
            _active = a;
            var ic = player.GetComponent<InteractionController>();
            if (ic != null) ic.SetExtraInteractables(SeatsFor(d, room));
            Debug.Log($"CTLIFE [interiors] room {d.room} at door {d.index} ({a.building.Count} building colliders)");
        }

        private void Deactivate()
        {
            var a = _active;
            _active = null;
            if (a == null) return;
            SetIgnore(a, false);
            if (a.room != null) a.room.gameObject.SetActive(false);
            var player = Player();
            var ic = player != null ? player.GetComponent<InteractionController>() : null;
            if (ic != null) ic.SetExtraInteractables(null);
            PlayerInside = false;
        }

        private List<Interactable> SeatsFor(Door d, InteriorRoom room)
        {
            var list = new List<Interactable>();
            if (room.seats == null) return list;
            var t = room.transform;
            foreach (var s in room.seats)
            {
                var pos = t.TransformPoint(s.position);
                float yaw = t.eulerAngles.y + s.yaw;
                bool sofa = s.kind == "sofa";
                list.Add(new Interactable
                {
                    id = $"room{d.index}.{s.id}", wireId = null, kind = sofa ? InteractionKind.Sofa : InteractionKind.Seat,
                    anim = sofa ? "act_sit_lounge" : "act_sit", position = pos, approach = t.TransformPoint(s.approach), yaw = yaw, range = 1.9f,
                });
            }
            return list;
        }

        // ------------------------------------------------------------------ the door leaf

        private Leaf TakeLeaf(Door d)
        {
            Leaf l;
            if (_spare.Count > 0) { l = _spare[_spare.Count - 1]; _spare.RemoveAt(_spare.Count - 1); }
            else l = MakeLeaf();
            l.door = d; l.angle = 0f;
            // hinge on the left jamb (seen from the street), mid-way through the room's 0.2 m front wall
            l.go.transform.SetPositionAndRotation(d.pos + d.rot * new Vector3(-0.55f, 0f, -0.12f), d.rot);
            l.hinge.localRotation = Quaternion.identity;
            l.go.SetActive(true);
            _leaves.Add(l);
            return l;
        }

        private Leaf MakeLeaf()
        {
            if (_leafMat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
                _leafMat = new Material(sh) { name = "door_leaf", enableInstancing = true };
                _leafMat.SetColor("_BaseColor", new Color(0.13f, 0.125f, 0.12f));
                _leafMat.SetFloat("_Smoothness", 0.42f);
                _handleMat = new Material(sh) { name = "door_handle", enableInstancing = true };
                _handleMat.SetColor("_BaseColor", new Color(0.72f, 0.72f, 0.70f));
                _handleMat.SetFloat("_Metallic", 0.9f); _handleMat.SetFloat("_Smoothness", 0.6f);
            }
            var root = new GameObject("door_leaf") { layer = 17 };
            root.transform.SetParent(transform, false);
            var hinge = new GameObject("hinge").transform;
            hinge.SetParent(root.transform, false);
            GameObject Part(PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
            {
                var g = GameObject.CreatePrimitive(type);
                Destroy(g.GetComponent<Collider>());
                g.layer = 17;
                g.transform.SetParent(hinge, false);
                g.transform.localPosition = pos; g.transform.localScale = scale;
                var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return g;
            }
            // a flush slab door (1.08 x 2.18 x 0.05 m) with a brushed lever on both faces
            Part(PrimitiveType.Cube, new Vector3(0.55f, 1.09f, 0f), new Vector3(1.08f, 2.18f, 0.05f), _leafMat);
            Part(PrimitiveType.Cube, new Vector3(0.98f, 1.02f, 0.05f), new Vector3(0.14f, 0.025f, 0.025f), _handleMat);
            Part(PrimitiveType.Cube, new Vector3(0.98f, 1.02f, -0.05f), new Vector3(0.14f, 0.025f, 0.025f), _handleMat);
            return new Leaf { go = root, hinge = hinge };
        }

        private void AnimateLeaves(Vector3 p)
        {
            float dt = Time.deltaTime;
            foreach (var l in _leaves)
            {
                bool ready = _active != null && _active.door == l.door && _active.room != null && _active.room.gameObject.activeSelf;
                bool open = ready && Flat(l.door.pos, p) < OpenRadius;
                float target = open ? 1f : 0f;
                if (Mathf.Approximately(l.angle, target)) continue;
                l.angle = Mathf.MoveTowards(l.angle, target, dt / 0.55f);
                float e = l.angle * l.angle * (3f - 2f * l.angle);
                l.hinge.localRotation = Quaternion.Euler(0f, 100f * e, 0f);       // swings into the room
            }
        }

        // ------------------------------------------------------------------ inside

        private void UpdateInside(Vector3 p)
        {
            var a = _active;
            if (a == null || a.room == null) { PlayerInside = false; SetHidden(false); ApplyLight(0f); return; }
            var t = a.room.transform;
            var local = t.InverseTransformPoint(p);
            bool inside = a.room.Contains(local, 0.05f);
            bool doorway = Mathf.Abs(local.x) < 0.9f && local.z > -0.6f && local.z < 2.4f && local.y > -1f && local.y < 2.5f;
            PlayerInside = inside;
            SetIgnore(a, inside || doorway);
            // the world outside: hidden only while the camera is in the room and its door is shut
            var cam = Camera.main;
            bool camIn = cam != null && a.room.Contains(t.InverseTransformPoint(cam.transform.position), 0.0f);
            bool shut = a.door.leaf == null || a.door.leaf.angle <= 0.001f;
            SetHidden(camIn && shut);
            float depth = inside ? Mathf.Clamp01((-local.z - 1.0f) / 2.0f) : 0f;
            _light = Mathf.MoveTowards(_light, depth, Time.deltaTime * 2.5f);
            ApplyLight(_light);
        }

        private void SetIgnore(Active a, bool on)
        {
            if (a.ignoring == on || a.body == null) return;
            foreach (var c in a.building) if (c != null) Physics.IgnoreCollision(a.body, c, on);
            a.ignoring = on;
        }

        private void SetHidden(bool hide)
        {
            if (WorldHidden == hide) return;
            WorldHidden = hide;
            IslandInstanceRenderer.HideWorld = hide;
            var cam = Camera.main;
            if (hide && cam != null) { _maskedCam = cam; _savedMask = cam.cullingMask; cam.cullingMask = _savedMask & ~HiddenLayers; }
            else RestoreView();
        }

        private void RestoreView()
        {
            IslandInstanceRenderer.HideWorld = false;
            if (_maskedCam != null) { _maskedCam.cullingMask = _savedMask; _maskedCam = null; }
        }

        /// <summary>Indoor light for the characters (the room itself is baked): a dimmer sun without shadows, warm ambient.</summary>
        private void ApplyLight(float k, bool force = false)
        {
            if (k <= 0.001f)
            {
                if (!_lightSaved) return;
                if (_sun != null) { _sun.intensity = _sunIntensity; _sun.shadows = _sunShadows; }
                RenderSettings.ambientSkyColor = _ambSky; RenderSettings.ambientEquatorColor = _ambEq; RenderSettings.ambientGroundColor = _ambGround;
                _lightSaved = false;
                return;
            }
            if (force) return;
            if (!_lightSaved)
            {
                _sun = RenderSettings.sun;
                if (_sun != null) { _sunIntensity = _sun.intensity; _sunShadows = _sun.shadows; }
                _ambSky = RenderSettings.ambientSkyColor; _ambEq = RenderSettings.ambientEquatorColor; _ambGround = RenderSettings.ambientGroundColor;
                _lightSaved = true;
            }
            if (_sun != null)
            {
                _sun.intensity = Mathf.Lerp(_sunIntensity, _sunIntensity * 0.3f, k);
                _sun.shadows = k > 0.5f ? LightShadows.None : _sunShadows;
            }
            RenderSettings.ambientSkyColor = Color.Lerp(_ambSky, new Color(0.86f, 0.80f, 0.72f), k);
            RenderSettings.ambientEquatorColor = Color.Lerp(_ambEq, new Color(0.70f, 0.63f, 0.56f), k);
            RenderSettings.ambientGroundColor = Color.Lerp(_ambGround, new Color(0.36f, 0.30f, 0.26f), k);
        }
    }
}
