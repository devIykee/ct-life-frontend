using System.Collections.Generic;
using CTLife.Audio;
using CTLife.Core;
using CTLife.World;
using UnityEngine;

namespace CTLife.WorldLife
{
    /// <summary>
    /// Stages world moments in the current venue: liquidation siren, rug "carpet yank", exploit alarm, sniper drone
    /// swarm on a launch, furnace flash on a motherlode, NPC crowd reactions, Sybil clones. Everything here is cosmetic,
    /// bounded (one effect of each kind at a time, ≤ 10 clones, ≤ 6 drones, ≤ 1 extra light) and cleaned up on venue exit.
    /// </summary>
    public sealed class MomentStage : MonoBehaviour
    {
        private const int MaxDrones = 6;
        private const int MaxClones = 10;

        private sealed class Fx
        {
            public string Kind;
            public GameObject Root;
            public Light Light;
            public float Start, End;
            public Vector3 From, To;
            public List<Transform> Parts;
            public List<Vector3> Anchors;
        }

        private readonly List<Fx> _fx = new List<Fx>();
        private readonly List<(float at, string npcId, string anim)> _crowd = new List<(float, string, string)>();
        private Fx _clones;
        private NpcController _cloneOwner;
        private float _clonesUntil;

        public bool ClonesActive => _clones != null;

        public static MomentStage Create()
        {
            var go = new GameObject("CTLife.WorldLife.Stage");
            DontDestroyOnLoad(go);
            return go.AddComponent<MomentStage>();
        }

        public void Clear()
        {
            foreach (var f in _fx) if (f.Root != null) Destroy(f.Root);
            _fx.Clear();
            _crowd.Clear();
            ClearClones(false);
        }

        // ------------------------------------------------------------------ crowd

        public void QueueCrowd(IEnumerable<CrowdCue> cues)
        {
            float now = Time.unscaledTime;
            foreach (var c in cues) _crowd.Add((now + c.Delay, c.NpcId, c.AnimId));
        }

        // ------------------------------------------------------------------ venue effects

        public Vector3 FxPosition(string fx, VenueSceneRoot venue)
        {
            if (venue == null) return Vector3.zero;
            string[] spots;
            switch (fx)
            {
                case "siren": spots = new[] { "liquidation_zone", "leverage_board", "tombstone_wall", "center" }; break;
                case "sniper_swarm": spots = new[] { "launch_board", "pulse_screen", "center" }; break;
                case "furnace_flash": spots = new[] { "furnace", "ore_board", "center" }; break;
                default: spots = new[] { "center", "square_center", "entrance" }; break;
            }
            foreach (var s in spots)
                if (venue.TryGetSpotQuiet(s, out var p)) return p;
            return venue.transform.position;
        }

        public void Play(string fx, Vector3 at, VenueSceneRoot venue)
        {
            if (string.IsNullOrEmpty(fx)) return;
            for (int i = _fx.Count - 1; i >= 0; i--)
                if (_fx[i].Kind == fx) { if (_fx[i].Root != null) Destroy(_fx[i].Root); _fx.RemoveAt(i); }
            float now = Time.unscaledTime;
            switch (fx)
            {
                case "siren": _fx.Add(Beacon("siren", at, new Color(1f, 0.1f, 0.1f), 6f, now)); break;
                case "alarm": _fx.Add(Beacon("alarm", at, new Color(1f, 0.55f, 0.05f), 8f, now)); break;
                case "furnace_flash": _fx.Add(Flash(at, new Color(1f, 0.55f, 0.15f), 3f, now)); break;
                case "carpet_yank": _fx.Add(Carpet(at, venue, now)); break;
                case "sniper_swarm": _fx.Add(Swarm(at, venue, now)); break;
            }
        }

        private static GameObject Primitive(PrimitiveType type, Transform parent, Color c, bool emissive)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = PropDresser.Material(new Rgb(c.r, c.g, c.b), emissive);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static Fx Beacon(string kind, Vector3 at, Color c, float seconds, float now)
        {
            var root = new GameObject("wl_" + kind);
            root.transform.position = at + Vector3.up * 2.6f;
            var lamp = Primitive(PrimitiveType.Cylinder, root.transform, c, true);
            lamp.transform.localScale = new Vector3(0.35f, 0.18f, 0.35f);
            var lightGo = new GameObject("light");
            lightGo.transform.SetParent(root.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = c;
            light.range = 9f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            return new Fx { Kind = kind, Root = root, Light = light, Start = now, End = now + seconds };
        }

        private static Fx Flash(Vector3 at, Color c, float seconds, float now)
        {
            var root = new GameObject("wl_flash");
            root.transform.position = at + Vector3.up * 1.5f;
            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = c;
            light.range = 10f;
            light.shadows = LightShadows.None;
            return new Fx { Kind = "furnace_flash", Root = root, Light = light, Start = now, End = now + seconds };
        }

        private static Fx Carpet(Vector3 at, VenueSceneRoot venue, float now)
        {
            var root = new GameObject("wl_carpet_yank");
            var rug = Primitive(PrimitiveType.Cube, root.transform, new Color(0.75f, 0.1f, 0.15f), false);
            rug.transform.localScale = new Vector3(3.2f, 0.03f, 2.2f);
            var stripe = Primitive(PrimitiveType.Cube, root.transform, new Color(0.98f, 0.8f, 0.2f), false);
            stripe.transform.localScale = new Vector3(3.0f, 0.035f, 0.18f);
            var forward = venue != null ? venue.transform.right : Vector3.right;
            root.transform.position = at + Vector3.up * 0.02f;
            root.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            return new Fx { Kind = "carpet_yank", Root = root, Start = now, End = now + 2.2f, From = root.transform.position,
                To = root.transform.position + forward * 6f };
        }

        private static Fx Swarm(Vector3 at, VenueSceneRoot venue, float now)
        {
            var root = new GameObject("wl_sniper_swarm");
            var fx = new Fx { Kind = "sniper_swarm", Root = root, Start = now, End = now + 10f, Parts = new List<Transform>(), Anchors = new List<Vector3>() };
            for (int i = 0; i < MaxDrones; i++)
            {
                Vector3 anchor;
                if (venue == null || !venue.TryGetSpotQuiet("swarm_" + (i + 1), out anchor))
                    anchor = at + Quaternion.Euler(0, i * 60f, 0) * Vector3.forward * 1.6f;
                var drone = new GameObject("drone_" + i).transform;
                drone.SetParent(root.transform, false);
                var body = Primitive(PrimitiveType.Cube, drone, new Color(0.18f, 0.2f, 0.21f), false);
                body.transform.localScale = new Vector3(0.3f, 0.1f, 0.3f);
                var eye = Primitive(PrimitiveType.Sphere, drone, new Color(1f, 0.2f, 0.2f), true);
                eye.transform.localScale = Vector3.one * 0.07f;
                eye.transform.localPosition = new Vector3(0, -0.03f, 0.16f);
                drone.position = anchor + Vector3.up * 6f;
                fx.Parts.Add(drone);
                fx.Anchors.Add(anchor + Vector3.up * 2.2f);
            }
            return fx;
        }

        /// <summary>Giveaway Guy drain gag: a few coins arc from the drained player to him.</summary>
        public void CoinBurst(Vector3 from, Vector3 to)
        {
            for (int i = _fx.Count - 1; i >= 0; i--)
                if (_fx[i].Kind == "coins") { if (_fx[i].Root != null) Destroy(_fx[i].Root); _fx.RemoveAt(i); }
            var root = new GameObject("wl_coin_burst");
            var fx = new Fx { Kind = "coins", Root = root, Start = Time.unscaledTime, End = Time.unscaledTime + 1.1f,
                From = from + Vector3.up * 1.1f, To = to + Vector3.up * 1.2f, Parts = new List<Transform>(), Anchors = new List<Vector3>() };
            for (int i = 0; i < 6; i++)
            {
                var coin = Primitive(PrimitiveType.Cylinder, root.transform, new Color(0.98f, 0.79f, 0.14f), true);
                coin.transform.localScale = new Vector3(0.14f, 0.012f, 0.14f);
                coin.transform.position = fx.From;
                fx.Parts.Add(coin.transform);
                fx.Anchors.Add(new Vector3((i - 2.5f) * 0.12f, 0.35f + (i % 3) * 0.15f, 0f));
            }
            _fx.Add(fx);
        }

        // ------------------------------------------------------------------ Sybil clones

        public void SplitClones(NpcController owner, int count, float seconds)
        {
            if (owner == null) return;
            ClearClones(false);
            count = Mathf.Clamp(count, 1, MaxClones);
            var root = new GameObject("wl_sybil_clones");
            var fx = new Fx { Kind = "clones", Root = root, Start = Time.unscaledTime, End = Time.unscaledTime + Mathf.Max(5f, seconds),
                Parts = new List<Transform>(), Anchors = new List<Vector3>() };
            var c = new Color(0.94f, 0.58f, 0.17f);
            for (int i = 0; i < count; i++)
            {
                var clone = new GameObject("clone_" + i).transform;
                clone.SetParent(root.transform, false);
                var body = Primitive(PrimitiveType.Capsule, clone, new Color(0.45f, 0.6f, 0.8f), false);
                body.transform.localScale = new Vector3(0.28f, 0.42f, 0.28f);
                body.transform.localPosition = new Vector3(0, 0.42f, 0);
                var hat = Primitive(PrimitiveType.Cylinder, clone, c, false);
                hat.transform.localScale = new Vector3(0.32f, 0.02f, 0.32f);
                hat.transform.localPosition = new Vector3(0, 0.86f, 0);
                var offset = Quaternion.Euler(0, i * (360f / count), 0) * Vector3.forward * (1.1f + 0.25f * (i % 3));
                clone.position = owner.transform.position + offset;
                fx.Parts.Add(clone);
                fx.Anchors.Add(offset);
            }
            _clones = fx;
            _cloneOwner = owner;
            _clonesUntil = fx.End;
        }

        public void ClearClones(bool poof)
        {
            if (_clones == null) return;
            if (poof && _clones.Root != null)
            {
                _clones.Kind = "clones_poof";
                _clones.Start = Time.unscaledTime;
                _clones.End = Time.unscaledTime + 0.35f;
                _fx.Add(_clones);
            }
            else if (_clones.Root != null) Destroy(_clones.Root);
            _clones = null;
            _cloneOwner = null;
        }

        // ------------------------------------------------------------------ update

        private void Update()
        {
            float now = Time.unscaledTime;
            var session = VenueSession.Current;
            for (int i = _crowd.Count - 1; i >= 0; i--)
            {
                if (now < _crowd[i].at) continue;
                var (_, npcId, anim) = _crowd[i];
                _crowd.RemoveAt(i);
                if (session != null && session.Npcs.TryGetValue(npcId, out var npc) && npc != null && npc.gameObject.activeInHierarchy)
                {
                    var view = npc.GetComponent<AvatarView>();
                    if (view != null) view.PlayOneShot(anim);
                }
            }

            if (_clones != null)
            {
                if (_cloneOwner == null || !_cloneOwner.gameObject.activeInHierarchy || now > _clonesUntil) ClearClones(true);
                else
                    for (int i = 0; i < _clones.Parts.Count; i++)
                    {
                        var t = _clones.Parts[i];
                        var wobble = new Vector3(Mathf.Sin(now * 1.7f + i) * 0.15f, Mathf.Abs(Mathf.Sin(now * 6f + i * 0.7f)) * 0.06f, Mathf.Cos(now * 1.3f + i) * 0.15f);
                        t.position = Vector3.Lerp(t.position, _cloneOwner.transform.position + _clones.Anchors[i] + wobble, 1f - Mathf.Exp(-4f * Time.deltaTime));
                    }
            }

            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                if (f.Root == null || now >= f.End) { if (f.Root != null) Destroy(f.Root); _fx.RemoveAt(i); continue; }
                float t = (now - f.Start) / Mathf.Max(0.01f, f.End - f.Start);
                switch (f.Kind)
                {
                    case "siren":
                    case "alarm":
                        f.Root.transform.Rotate(0f, 540f * Time.deltaTime, 0f);
                        if (f.Light != null) f.Light.intensity = (Mathf.Sin(now * (f.Kind == "siren" ? 14f : 9f)) * 0.5f + 0.5f) * 3.5f * (1f - t * t);
                        break;
                    case "furnace_flash":
                        if (f.Light != null) f.Light.intensity = 6f * Mathf.Exp(-3f * t) * (0.8f + 0.2f * Mathf.Sin(now * 30f));
                        break;
                    case "carpet_yank":
                        // Hold 0.4 s, then yank out over 0.6 s with a flip, then sink.
                        float y = Mathf.Clamp01((now - f.Start - 0.4f) / 0.6f);
                        float e = y * y * (3f - 2f * y);
                        f.Root.transform.position = Vector3.Lerp(f.From, f.To, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.8f;
                        f.Root.transform.Rotate(Vector3.right, (y > 0f && y < 1f ? 360f : 0f) * Time.deltaTime, Space.Self);
                        if (y >= 1f) f.Root.transform.localScale = Vector3.one * Mathf.Max(0.01f, 1f - (t - 0.45f) * 2f);
                        break;
                    case "sniper_swarm":
                        for (int d = 0; d < f.Parts.Count; d++)
                        {
                            var tr = f.Parts[d];
                            float local = now - f.Start;
                            var orbit = Quaternion.Euler(0, local * 90f + d * 60f, 0) * Vector3.forward * 0.6f;
                            var target = f.Anchors[d] + orbit + Vector3.up * Mathf.Sin(local * 4f + d) * 0.15f;
                            if (t > 0.85f) target += Vector3.up * 8f; // leave
                            tr.position = Vector3.Lerp(tr.position, target, 1f - Mathf.Exp(-5f * Time.deltaTime));
                            tr.rotation = Quaternion.Euler(0, local * 200f + d * 40f, 0);
                        }
                        break;
                    case "coins":
                        for (int c = 0; c < f.Parts.Count; c++)
                        {
                            float u = Mathf.Clamp01(t * 1.15f - c * 0.03f);
                            var p = Vector3.Lerp(f.From, f.To, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * (0.6f + f.Anchors[c].y) +
                                    Vector3.right * f.Anchors[c].x * Mathf.Sin(u * Mathf.PI);
                            f.Parts[c].position = p;
                            f.Parts[c].Rotate(720f * Time.deltaTime, 0f, 0f, Space.Self);
                        }
                        break;
                    case "clones_poof":
                        if (f.Root != null) f.Root.transform.localScale = Vector3.one * Mathf.Max(0.01f, 1f - t);
                        break;
                }
            }
        }

        private void OnDestroy() => Clear();
    }

    internal static class VenueSceneRootExtensions
    {
        /// <summary>TryGetSpot without the unknown-id warning (FX probe several candidate spots).</summary>
        public static bool TryGetSpotQuiet(this VenueSceneRoot venue, string spotId, out Vector3 position)
        {
            position = Vector3.zero;
            if (venue == null || string.IsNullOrEmpty(spotId)) return false;
            foreach (var a in venue.Anchors)
                if (a != null && a.spotId == spotId) { position = a.transform.position; return true; }
            var spots = venue.RegistrySpots;
            if (spots != null && spots.spots.TryGetValue(spotId, out var s) && s.hasPosition) { position = venue.ToWorld(s.x, s.z); return true; }
            return false;
        }
    }

    /// <summary>Plays a positional world sound: the audio lane's hook first, else the first known id via AudioManager.</summary>
    internal static class WorldSfx
    {
        public static void PlayAt(string momentId, string fx, IReadOnlyList<string> ids, Vector3 at, float seconds)
        {
            if (ids == null || ids.Count == 0) return;
            if (WorldLifeHooks.Audio != null)
            {
                try { if (WorldLifeHooks.Audio.PlayAt(momentId, fx, ids, at, seconds)) return; }
                catch (System.Exception e) { Log.Exception("world-life", e); }
            }
            var audio = AudioManager.Instance;
            if (audio == null) return;
            foreach (var id in ids)
                if (audio.Map != null && audio.Map.Sfx.ContainsKey(id)) { audio.PlayAt(id, at); return; }
        }
    }
}
