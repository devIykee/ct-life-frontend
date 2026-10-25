using System;
using System.Collections.Generic;
using CTLife.Audio;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// The 80 community nods on CT Island (R4, 2026-10-25): small painted discs on a stake (one shared mesh, the set's
    /// tile of Resources/World/Island/nod_atlas.png), GPU-instanced per set (a draw per set with nods in view), with a
    /// slow bob and sway and now and then a glint. Within <see cref="Reach"/> of a nod not yet found the HUD offers
    /// "Collect" (<see cref="NodHooks.TryCollect"/>); <see cref="NodHooks.Collected"/> plays the pickup (the disc pops up
    /// and spins away in a burst of sparks, a real bell-tree chime) and the nod is gone. Found nods
    /// (<see cref="NodHooks.IsFound"/>) are never drawn. Positions: Resources/World/Island/nods.json from
    /// tools/world/islandgen/nods.py (the same x/z as contract/ids/nods.json, which the server checks).
    /// </summary>
    public sealed class NodField : MonoBehaviour
    {
        public const string DataPath = "World/Island/nods", AtlasPath = "World/Island/nod_atlas";
        public const float Reach = 2.5f, DrawWithin = 60f, DiscHeight = 0.92f, DiscRadius = 0.26f;
        /// <summary>The atlas's tiles, in order (art/source/nods/build.py).</summary>
        public static readonly string[] Sets = { "memecoin_row", "nft_gallery", "builders_row", "defi_district", "superteam_corner", "event_hall" };

        [Serializable] public sealed class Nod { public string nodId, setId; public float x, y, z, yaw; [NonSerialized] public int set; [NonSerialized] public bool grounded; }
        [Serializable] private sealed class NodFile { public Nod[] nods; }

        public static NodField Current { get; private set; }
        public IReadOnlyList<Nod> Nods => _nods;
        /// <summary>Instanced draws submitted last frame (captures, perf).</summary>
        public int LastDraws { get; private set; }
        public int LastDrawn { get; private set; }

        private Nod[] _nods = Array.Empty<Nod>();
        private bool[] _found = Array.Empty<bool>();
        private Mesh _mesh, _spark;
        private Material[] _mats;
        private readonly List<Matrix4x4>[] _batch = new List<Matrix4x4>[6];
        private readonly Matrix4x4[] _buf = new Matrix4x4[128];
        private readonly Dictionary<string, float> _pending = new Dictionary<string, float>();
        private readonly Dictionary<int, float> _popping = new Dictionary<int, float>();
        private Func<Vector3, WorldOffer> _source;
        private readonly WorldOffer _offer = new WorldOffer();
        private int _near = -1;
        private float _foundAt;
        private Color[] _baseColor;

        public static NodField Create(Transform parent)
        {
            var go = new GameObject("Community nods");
            go.transform.SetParent(parent, false);
            return go.AddComponent<NodField>();
        }

        public static Nod[] Load()
        {
            var ta = Resources.Load<TextAsset>(DataPath);
            if (ta == null) return Array.Empty<Nod>();
            var file = JsonUtility.FromJson<NodFile>(ta.text);
            var nods = file?.nods ?? Array.Empty<Nod>();
            foreach (var n in nods) n.set = Mathf.Max(0, Array.IndexOf(Sets, n.setId));
            return nods;
        }

        private void Awake()
        {
            Current = this;
            _nods = Load();
            _found = new bool[_nods.Length];
            _mesh = BuildMesh();
            _spark = BuildSpark();
            _mats = BuildMaterials(Resources.Load<Texture2D>(AtlasPath));
            _baseColor = new Color[_mats.Length];
            for (int i = 0; i < _mats.Length; i++) _baseColor[i] = _mats[i].GetColor("_BaseColor");
            for (int i = 0; i < _batch.Length; i++) _batch[i] = new List<Matrix4x4>(32);
            _source = Offer;
            Debug.Log($"CTLIFE [nods] {_nods.Length} community nods");
        }

        private void OnEnable()
        {
            WorldOffers.Add(_source);
            NodHooks.Collected += OnCollected;
            NodHooks.FoundRefreshed += RefreshFound;
            RefreshFound();
        }

        private void OnDisable()
        {
            WorldOffers.Remove(_source);
            NodHooks.Collected -= OnCollected;
            NodHooks.FoundRefreshed -= RefreshFound;
            if (_near >= 0) { NodHooks.NotifyNear(_nods[_near].nodId, false); _near = -1; }
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Current, this)) Current = null;
            if (_mats != null) foreach (var m in _mats) if (m != null) Destroy(m);
            if (_mesh != null) Destroy(_mesh);
            if (_spark != null) Destroy(_spark);
        }

        /// <summary>Is this nod still there (not found, not mid-pickup)?</summary>
        public bool Standing(int i) => !_found[i] && !_popping.ContainsKey(i);

        public int IndexOf(string nodId)
        {
            for (int i = 0; i < _nods.Length; i++) if (_nods[i].nodId == nodId) return i;
            return -1;
        }

        private void RefreshFound()
        {
            var f = NodHooks.IsFound;
            for (int i = 0; i < _nods.Length; i++)
            {
                bool found = false;
                if (f != null) { try { found = f(_nods[i].nodId); } catch (Exception e) { Debug.LogException(e); } }
                _found[i] = _found[i] || found;
            }
            _foundAt = Time.unscaledTime;
        }

        private static bool Hidden() => IslandInteriors.Current != null && IslandInteriors.Current.WorldHidden;
        private static bool Indoors() => IslandInteriors.Current != null && IslandInteriors.Current.PlayerInside;

        private Transform Player()
        {
            var s = VenueSession.Current;
            return s != null && s.LocalPlayer != null ? s.LocalPlayer.transform : null;
        }

        private void Update()
        {
            if (Time.unscaledTime - _foundAt > 2f) RefreshFound();
            var player = Player();
            UpdateNear(player);
            Draw();
        }

        // ------------------------------------------------------------------ the prompt

        private void UpdateNear(Transform player)
        {
            int near = -1;
            if (player != null && !Indoors())
            {
                var p = player.position;
                float best = Reach;
                for (int i = 0; i < _nods.Length; i++)
                {
                    if (!Standing(i)) continue;
                    var n = _nods[i];
                    float d = Mathf.Sqrt((n.x - p.x) * (n.x - p.x) + (n.z - p.z) * (n.z - p.z));
                    if (d < best && Mathf.Abs(n.y - p.y) < 3f) { best = d; near = i; }
                }
            }
            if (near == _near) return;
            if (_near >= 0) NodHooks.NotifyNear(_nods[_near].nodId, false);
            _near = near;
            if (_near >= 0) NodHooks.NotifyNear(_nods[_near].nodId, true);
        }

        private WorldOffer Offer(Vector3 player)
        {
            if (_near < 0) return null;
            var n = _nods[_near];
            if (_pending.TryGetValue(n.nodId, out float until) && Time.unscaledTime < until) return null;
            _offer.id = "nod:" + n.nodId;
            _offer.verbKey = "nod.collect";
            _offer.icon = "league";
            _offer.tagKey = "nod.tag";
            _offer.tagLocked = false;
            _offer.primary = true;
            _offer.position = new Vector3(n.x, n.y + DiscHeight + 0.55f, n.z);
            _offer.follow = null;
            _offer.range = Reach;
            string id = n.nodId;
            _offer.perform = () => Collect(id);
            return _offer;
        }

        /// <summary>Ask City Life to collect a nod (the prompt, the dev route).</summary>
        public void Collect(string nodId)
        {
            _pending[nodId] = Time.unscaledTime + 5f;           // no second press while the server answers
            Debug.Log("CTLIFE [nods] collect " + nodId + (NodHooks.TryCollect == null ? " (no City Life: nothing sent)" : ""));
            NodHooks.TryCollect?.Invoke(nodId);
        }

        private void OnCollected(string nodId)
        {
            int i = IndexOf(nodId);
            if (i < 0 || _found[i] || _popping.ContainsKey(i)) return;
            _pending.Remove(nodId);
            _popping[i] = Time.time;
            var n = _nods[i];
            AudioManager.Instance?.PlayAt("sfx_nod_collect", new Vector3(n.x, n.y + DiscHeight, n.z));
            Debug.Log("CTLIFE [nods] collected " + nodId);
        }

        // ------------------------------------------------------------------ drawing

        private void Draw()
        {
            for (int s = 0; s < _batch.Length; s++) _batch[s].Clear();
            LastDraws = 0; LastDrawn = 0;
            var cam = Camera.main;
            if (cam == null || _mats == null || Hidden()) return;
            var c = cam.transform.position;
            float t = Time.time;
            for (int i = 0; i < _nods.Length; i++)
            {
                if (_found[i]) continue;
                var n = _nods[i];
                float dx = n.x - c.x, dz = n.z - c.z;
                if (dx * dx + dz * dz > DrawWithin * DrawWithin) continue;
                if (!n.grounded) Ground(n);
                float phase = i * 1.7f;
                var pos = new Vector3(n.x, n.y + 0.035f * Mathf.Sin(t * 1.6f + phase), n.z);
                var rot = Quaternion.Euler(0f, n.yaw + 10f * Mathf.Sin(t * 0.7f + phase), 0f);
                float scale = 1f;
                if (_popping.TryGetValue(i, out float t0))
                {
                    float k = (t - t0) / 0.7f;
                    if (k >= 1f) { _popping.Remove(i); _found[i] = true; continue; }
                    // up and round, a bit bigger, then gone
                    pos.y += 0.6f * Mathf.Sin(k * Mathf.PI * 0.5f);
                    rot = Quaternion.Euler(0f, n.yaw + 900f * k * k, 0f);
                    scale = k < 0.55f ? 1f + 0.5f * (k / 0.55f) : 1.5f * (1f - (k - 0.55f) / 0.45f);
                    Sparks(n, pos, k);
                }
                _batch[n.set].Add(Matrix4x4.TRS(pos, rot, Vector3.one * scale));
                LastDrawn++;
            }
            for (int s = 0; s < _batch.Length; s++)
            {
                // the glint: a short brightening now and then, each set on its own beat
                float g = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 1.25f + s * 1.1f)), 24f);
                _mats[s].SetColor("_BaseColor", _baseColor[s] * (1f + 0.45f * g));
                Submit(_mesh, _mats[s], _batch[s]);
            }
        }

        private readonly List<Matrix4x4> _sparks = new List<Matrix4x4>(16);

        private void Sparks(Nod n, Vector3 disc, float k)
        {
            _sparks.Clear();
            for (int j = 0; j < 12; j++)
            {
                float a = j * Mathf.PI * 2f / 12f + n.yaw * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0.9f + 0.35f * ((j * 7) % 3), Mathf.Sin(a));
                var p = disc + Vector3.up * DiscHeight + dir * (1.1f * k) + Vector3.down * (2.2f * k * k);
                _sparks.Add(Matrix4x4.TRS(p, Quaternion.Euler(45f * j, 600f * k, 0f), Vector3.one * (0.07f * (1f - k))));
            }
            Submit(_spark, _mats[n.set], _sparks);
        }

        private void Submit(Mesh mesh, Material mat, List<Matrix4x4> list)
        {
            if (list.Count == 0) return;
            var rp = new RenderParams(mat)
            {
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = true, layer = 13,
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 4000f),
            };
            for (int start = 0; start < list.Count; start += _buf.Length)
            {
                int count = Mathf.Min(_buf.Length, list.Count - start);
                list.CopyTo(start, _buf, 0, count);
                Graphics.RenderMeshInstanced(rp, mesh, 0, _buf, count);
                LastDraws++;
            }
        }

        /// <summary>Settle a nod on what is really under it (the street cell's colliders, once streamed in).</summary>
        private static void Ground(Nod n)
        {
            const int mask = (1 << 10) | (1 << 11);              // Ground, Road
            if (Physics.Raycast(new Vector3(n.x, n.y + 4f, n.z), Vector3.down, out var hit, 8f, mask, QueryTriggerInteraction.Ignore))
            {
                if (Mathf.Abs(hit.point.y - n.y) < 1.5f) n.y = hit.point.y;
                n.grounded = true;
            }
        }

        // ------------------------------------------------------------------ the prop

        /// <summary>A disc (front and back faces the set's painting, the edge cream) on a wooden stake; local +Z is the face.</summary>
        public static Mesh BuildMesh()
        {
            const int seg = 28;
            const float r = DiscRadius, half = 0.022f, uvR = 236f / 512f;
            var v = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            var cream = new Vector2(0.07f, 0.93f);
            var centre = new Vector3(0f, DiscHeight, 0f);
            for (int face = 0; face < 2; face++)
            {
                float z = face == 0 ? half : -half;
                var n = face == 0 ? Vector3.forward : Vector3.back;
                int c0 = v.Count;
                v.Add(centre + new Vector3(0f, 0f, z)); nrm.Add(n); uv.Add(new Vector2(0.5f, 0.5f));
                for (int k = 0; k <= seg; k++)
                {
                    float a = k * Mathf.PI * 2f / seg;
                    float x = Mathf.Cos(a), y = Mathf.Sin(a);
                    v.Add(centre + new Vector3(x * r, y * r, z)); nrm.Add(n);
                    // the back mirrors u so its painting reads the right way round from behind
                    uv.Add(new Vector2(0.5f + (face == 0 ? -x : x) * uvR, 0.5f + y * uvR));
                }
                for (int k = 0; k < seg; k++)
                {
                    if (face == 0) { tri.Add(c0); tri.Add(c0 + 1 + k); tri.Add(c0 + 2 + k); }
                    else { tri.Add(c0); tri.Add(c0 + 2 + k); tri.Add(c0 + 1 + k); }
                }
            }
            int e0 = v.Count;
            for (int k = 0; k <= seg; k++)
            {
                float a = k * Mathf.PI * 2f / seg;
                var o = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(centre + o * r + Vector3.forward * half); nrm.Add(o); uv.Add(cream);
                v.Add(centre + o * r - Vector3.forward * half); nrm.Add(o); uv.Add(cream);
            }
            for (int k = 0; k < seg; k++)
            {
                int a = e0 + 2 * k;
                tri.Add(a); tri.Add(a + 1); tri.Add(a + 2);
                tri.Add(a + 1); tri.Add(a + 3); tri.Add(a + 2);
            }
            Box(v, nrm, uv, tri, new Vector3(-0.018f, 0f, -0.014f), new Vector3(0.018f, DiscHeight - r + 0.04f, 0.014f), new Vector2(0.05f, 0.05f));
            Box(v, nrm, uv, tri, new Vector3(-0.07f, 0f, -0.05f), new Vector3(0.07f, 0.035f, 0.05f), new Vector2(0.05f, 0.08f));
            var m = new Mesh { name = "nod" };
            m.SetVertices(v); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        private static void Box(List<Vector3> v, List<Vector3> nrm, List<Vector2> uv, List<int> tri, Vector3 a, Vector3 b, Vector2 at)
        {
            var dirs = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var c = (a + b) * 0.5f; var h = (b - a) * 0.5f;
            foreach (var d in dirs)
            {
                var u = d.y != 0f ? Vector3.right : Vector3.up;
                var w = Vector3.Cross(d, u);
                int i0 = v.Count;
                foreach (var (su, sw) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    v.Add(c + Vector3.Scale(d + u * su + w * sw, h)); nrm.Add(d); uv.Add(at);
                }
                tri.Add(i0); tri.Add(i0 + 1); tri.Add(i0 + 2);
                tri.Add(i0); tri.Add(i0 + 2); tri.Add(i0 + 3);
            }
        }

        /// <summary>A small diamond for the pickup's sparks.</summary>
        public static Mesh BuildSpark()
        {
            var v = new List<Vector3> { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            var tri = new List<int> { 0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4, 1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2 };
            var m = new Mesh { name = "nod_spark" };
            m.SetVertices(v); m.SetTriangles(tri, 0);
            m.SetUVs(0, new List<Vector2> { new Vector2(0.07f, 0.93f), new Vector2(0.07f, 0.93f), new Vector2(0.07f, 0.93f), new Vector2(0.07f, 0.93f), new Vector2(0.07f, 0.93f), new Vector2(0.07f, 0.93f) });
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>One instanced Lit material per set: the atlas with that set's tile.</summary>
        public static Material[] BuildMaterials(Texture2D atlas)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            var mats = new Material[Sets.Length];
            for (int s = 0; s < Sets.Length; s++)
            {
                var m = new Material(sh) { name = "nod_" + Sets[s], enableInstancing = true };
                if (atlas != null) m.SetTexture("_BaseMap", atlas);
                m.SetTextureScale("_BaseMap", new Vector2(1f / 3f, 1f / 2f));
                m.SetTextureOffset("_BaseMap", new Vector2((s % 3) / 3f, s < 3 ? 0.5f : 0f));
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Smoothness", 0.38f);
                mats[s] = m;
            }
            return mats;
        }
    }
}
