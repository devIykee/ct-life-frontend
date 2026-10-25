using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.World;
using CTLife.World.Roads;
using CTLife.World.Vehicles;
using UnityEngine;

namespace CTLife.CityLife.Heat
{
    /// <summary>
    /// The police cruisers of backend #17's pursuits (lane B, 2026-10-25). The server cannot see them: it decides caught or
    /// escaped from its own position data and timers. These are client AI: one set of `units` cruisers per `pursuitId`
    /// (HeatService dedupes the city and street copies), spawned on a road out of sight behind the target, chasing it with
    /// pure-pursuit steering, sirens (a real CC0 recording) and a red / blue light bar. For the player's own pursuit, a cruiser
    /// holding contact for <see cref="HeatRules.ContactSeconds"/> reports the catch (the server checks it); on
    /// `police_pursuit_end` the cruisers kill the siren, slow down and are removed once out of sight.
    /// Bystanders in the same street room see the same chase (no reports).
    /// The cruisers use the v3 fleet's police model; the lead one carries "THE SLEUTH" (our original parody character).
    /// </summary>
    public sealed class PolicePursuits : MonoBehaviour
    {
        public static PolicePursuits Current { get; private set; }

        private sealed class Cruiser
        {
            public Vehicle Car;
            public AudioSource Siren;
            public Renderer Red, Blue;
            public Light Flash;
            public float Stuck, BackFor, Phase;
            public float Best = float.MaxValue, BestAt;
            public float LogAt, PlanAt, Held;
            public List<Vector3> Path;
            public int PathIdx;
            public Vector3 PlanTarget;
            public TextMesh Label;
        }

        private sealed class Chase
        {
            public PursuitInfo Info;
            public readonly List<Cruiser> Cars = new List<Cruiser>();
            public bool Ending;
            public float EndedAt, Contact, ReportAt, SpawnRetryAt;
        }

        private HeatService _svc;
        private readonly Dictionary<string, Chase> _chases = new Dictionary<string, Chase>();
        private static AudioClip _yelp, _wail;
        private static Material _red, _blue;
        private readonly System.Random _rng = new System.Random();

        /// <summary>How many cruisers are on the island now (for the dev routes and the census).</summary>
        public int CruiserCount { get { int n = 0; foreach (var c in _chases.Values) n += c.Cars.Count; return n; } }
        public IEnumerable<Vehicle> Cruisers { get { foreach (var c in _chases.Values) foreach (var k in c.Cars) if (k.Car != null) yield return k.Car; } }

        public static PolicePursuits Create(HeatService svc)
        {
            var go = new GameObject("PolicePursuits");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<PolicePursuits>();
            p._svc = svc;
            svc.PursuitStarted += p.Begin;
            svc.PursuitEnded += p.End;
            foreach (var info in svc.Pursuits.Values) p.Begin(info);
            Current = p;
            return p;
        }

        private void OnDestroy()
        {
            if (_svc != null) { _svc.PursuitStarted -= Begin; _svc.PursuitEnded -= End; }
            foreach (var c in _chases.Values) foreach (var k in c.Cars) if (k.Car != null) Destroy(k.Car.gameObject);
            _chases.Clear();
            if (Current == this) Current = null;
        }

        private void Begin(PursuitInfo info)
        {
            if (info == null || _chases.ContainsKey(info.PursuitId)) return;
            _chases[info.PursuitId] = new Chase { Info = info };
            VehiclePrefabs.Preload(new[] { "police" });
        }

        private void End(string id, string outcome)
        {
            if (!_chases.TryGetValue(id, out var c) || c.Ending) return;
            c.Ending = true; c.EndedAt = Time.time;
            foreach (var k in c.Cars) { if (k.Siren != null) k.Siren.Stop(); if (k.Flash != null) k.Flash.enabled = false; }
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (_chases.Count == 0) return;
            List<string> done = null;
            foreach (var kv in _chases)
            {
                var c = kv.Value;
                var target = Target(c.Info.TargetUserId, out var targetVel);
                if (c.Ending)
                {
                    bool gone = true;
                    foreach (var k in c.Cars)
                    {
                        if (k.Car == null) continue;
                        k.Car.Input = CTLife.World.Vehicles.VehicleInput.None;
                        k.Car.Input.brake = 1f;
                        if (Time.time - c.EndedAt < 20f && Visible(k.Car.transform.position)) gone = false;
                        else Destroy(k.Car.gameObject);
                    }
                    if (gone) (done ??= new List<string>()).Add(kv.Key);
                    continue;
                }
                if (target == null) continue;
                if (c.Cars.Count < c.Info.Units && Time.time >= c.SpawnRetryAt)
                {
                    c.SpawnRetryAt = Time.time + 0.5f;
                    var k = SpawnCruiser(target.Value, c.Cars.Count, c.Cars.Count == 0);
                    if (k != null) c.Cars.Add(k);
                }
                bool mine = c.Info.TargetUserId == _svc.UserId;
                bool contact = false;
                for (int i = 0; i < c.Cars.Count; i++)
                {
                    var k = c.Cars[i];
                    if (k.Car == null) continue;
                    Drive(k, target.Value, targetVel, i);
                    Lights(k);
                    float d = Flat(k.Car.transform.position - target.Value).magnitude;
                    // alongside the target; or pulled up close behind something in the way (traffic stopped at the
                    // target, a kerb post): the officer steps out and makes the arrest on foot
                    k.Held = d <= HeatRules.FootArrestMetres && k.Car.Speed < 1.2f ? k.Held + Time.deltaTime : 0f;
                    if (d <= HeatRules.ContactMetres + (k.Car.Speed < 3f ? 0.5f : 0f) || k.Held >= 1.5f) contact = true;
                }
                if (!mine) continue;
                c.Contact = contact ? c.Contact + Time.deltaTime : Mathf.Max(0f, c.Contact - Time.deltaTime * 0.5f);
                double age = (_svc.NowMs - c.Info.StartedAtMs) / 1000.0;
                if (c.Contact >= HeatRules.ContactSeconds && age >= HeatRules.CatchMinAgeSeconds && Time.time >= c.ReportAt)
                {
                    c.ReportAt = Time.time + 6f;
                    Log.Info("heat", "cruiser contact: reporting the catch");
                    _ = _svc.ReportCaught(c.Info.PursuitId, target.Value);
                }
            }
            if (done != null) foreach (var id in done) _chases.Remove(id);
        }

        /// <summary>The chased player's position (and velocity): the local player or a remote avatar by user id.</summary>
        private Vector3? Target(string userId, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (string.IsNullOrEmpty(userId)) return null;
            if (userId == _svc.UserId)
            {
                var driving = VehicleSystem.Current != null ? VehicleSystem.Current.Driving : null;
                if (driving != null) { if (driving.Body != null) velocity = driving.Body.linearVelocity; return driving.transform.position; }
                var lp = VenueSession.Current != null ? VenueSession.Current.LocalPlayer : null;
                return lp != null ? lp.transform.position : (Vector3?)null;
            }
            foreach (var a in AvatarView.All)
                if (a != null && a.userId == userId) return a.transform.position;
            return null;
        }

        // ------------------------------------------------------------------ spawning

        private Cruiser SpawnCruiser(Vector3 target, int index, bool lead)
        {
            if (!VehiclePrefabs.Ready("police")) return null;
            var prefab = VehiclePrefabs.Get("police", false);
            if (prefab == null) return null;
            if (!SpawnPose(target, index, out var pos, out var rot)) return null;
            var go = Instantiate(prefab, pos, rot, transform);
            go.name = "police-cruiser";
            var v = go.GetComponent<Vehicle>();
            if (v == null) { Destroy(go); return null; }
            // AI-driven: occupied (CarPhysics takes its Input), engine on, never offered to the player
            v.RemoteOccupied = true;
            v.EngineOn = true;
            var k = new Cruiser { Car = v, Phase = (float)_rng.NextDouble(), BestAt = Time.time };
            Siren(k, lead);
            LightBar(k);
            if (lead) k.Label = SleuthLabel(v);
            Log.Info("heat", $"cruiser {index + 1} spawned {Flat(pos - target).magnitude:0} m from the target");
            return k;
        }

        /// <summary>A lane point 70-120 m from the target, out of view if possible, facing the target.</summary>
        private bool SpawnPose(Vector3 target, int index, out Vector3 pos, out Quaternion rot, bool near = false)
        {
            pos = default; rot = Quaternion.identity;
            var net = RoadNetwork.Active;
            var cam = Camera.main;
            Vector3 back = cam != null ? -Flat(cam.transform.forward).normalized : Vector3.back;
            for (int attempt = 0; attempt < 14; attempt++)
            {
                float ang = (attempt == 0 ? 0f : (attempt % 2 == 0 ? 1 : -1) * (attempt * 22f)) + index * 25f;
                float dist = near ? 38f + (attempt % 3) * 9f : 75f + (attempt % 3) * 18f;
                var probe = target + Quaternion.Euler(0f, ang, 0f) * back * dist;
                Vector3 at; Vector3 dir;
                if (net != null)
                {
                    var lp = net.NearestLanePoint(probe, 45f);
                    if (!lp.valid) continue;
                    at = lp.position; dir = lp.direction;
                }
                else
                {
                    float g = IslandStreamer.GroundHeight(probe.x, probe.z);
                    if (float.IsNaN(g)) continue;
                    at = new Vector3(probe.x, g, probe.z); dir = target - at;
                }
                if (Visible(at) && attempt < 10) continue;
                if (Physics.CheckBox(at + Vector3.up * 1.2f, new Vector3(1.4f, 0.8f, 2.8f), Quaternion.LookRotation(Flat(dir).sqrMagnitude > 0.01f ? Flat(dir) : Vector3.forward), 1 << CharacterLayers.Vehicle, QueryTriggerInteraction.Ignore)) continue;
                var toTarget = Flat(target - at);
                var face = Flat(dir);
                if (face.sqrMagnitude < 0.01f || Vector3.Dot(face, toTarget) < 0f) face = toTarget;
                pos = at + Vector3.up * 0.5f;
                rot = Quaternion.LookRotation(face.normalized, Vector3.up);
                return true;
            }
            return false;
        }

        private static void Siren(Cruiser k, bool lead)
        {
            _yelp ??= Resources.Load<AudioClip>("Vehicles/Audio/siren_yelp");
            _wail ??= Resources.Load<AudioClip>("Vehicles/Audio/siren_wail");
            var s = k.Car.gameObject.AddComponent<AudioSource>();
            s.clip = lead ? _yelp : _wail;
            s.loop = true; s.playOnAwake = false; s.spatialBlend = 1f; s.dopplerLevel = 0.6f;
            s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 10f; s.maxDistance = 220f; s.volume = 0.8f;
            s.time = UnityEngine.Random.value * (s.clip != null ? s.clip.length : 0f);
            if (s.clip != null) s.Play();
            k.Siren = s;
        }

        /// <summary>Red and blue bars on the roof (two unlit boxes that alternate), and one small flashing light on High.</summary>
        private static void LightBar(Cruiser k)
        {
            var t = k.Car.transform;
            var b = RendererBounds(k.Car.gameObject);
            float top = b.max.y - t.position.y;
            _red ??= Unlit(new Color(1f, 0.12f, 0.16f));
            _blue ??= Unlit(new Color(0.15f, 0.35f, 1f));
            k.Red = Bar(t, new Vector3(-0.32f, top + 0.06f, -0.15f), _red);
            k.Blue = Bar(t, new Vector3(0.32f, top + 0.06f, -0.15f), _blue);
            if (!QualityTier.IsLow)
            {
                var lg = new GameObject("flash");
                lg.transform.SetParent(t, false);
                lg.transform.localPosition = new Vector3(0f, top + 0.4f, -0.15f);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point; l.range = 9f; l.intensity = 2.2f; l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForceVertex;
                k.Flash = l;
            }
        }

        private static Renderer Bar(Transform parent, Vector3 local, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "lightbar";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = new Vector3(0.5f, 0.12f, 0.22f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private static Material Unlit(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh) { name = "police_lightbar", hideFlags = HideFlags.DontSave };
            m.SetColor("_BaseColor", c);
            return m;
        }

        private static TextMesh SleuthLabel(Vehicle v)
        {
            var b = RendererBounds(v.gameObject);
            var go = new GameObject("sleuth-label");
            go.transform.SetParent(v.transform, false);
            go.transform.localPosition = new Vector3(0f, b.max.y - v.transform.position.y + 0.95f, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.text = "THE SLEUTH";
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.fontSize = 48; tm.characterSize = 0.06f; tm.fontStyle = FontStyle.Bold;
            tm.color = new Color(1f, 0.82f, 0.25f);
            return tm;
        }

        private void Lights(Cruiser k)
        {
            float t = Time.time * 5.5f + k.Phase * 3f;
            bool redOn = Mathf.Repeat(t, 2f) < 1f;
            if (k.Red != null) k.Red.enabled = redOn;
            if (k.Blue != null) k.Blue.enabled = !redOn;
            if (k.Flash != null) k.Flash.color = redOn ? new Color(1f, 0.15f, 0.2f) : new Color(0.2f, 0.4f, 1f);
            if (k.Label != null && Camera.main != null)
                k.Label.transform.rotation = Quaternion.LookRotation(k.Label.transform.position - Camera.main.transform.position);
        }

        // ------------------------------------------------------------------ driving

        private void Drive(Cruiser k, Vector3 target, Vector3 targetVel, int index)
        {
            var v = k.Car;
            var pos = v.transform.position;
            var to = Flat(target - pos);
            float dist = to.magnitude;
            // aim ahead of a moving target; the followers take a slot to either side
            var aim = target + targetVel * Mathf.Clamp(dist / 25f, 0.2f, 1.2f);
            // far off: drive the road graph (A*, RoadPath) to the target, replanned every few seconds; straight in when close
            var net = RoadNetwork.Active;
            bool onPath = false;
            if (net != null && dist > 35f)
            {
                if (k.Path == null || Time.time >= k.PlanAt || Flat(target - k.PlanTarget).magnitude > 30f)
                {
                    k.PlanAt = Time.time + 5f; k.PlanTarget = target;
                    k.Path = RoadPath.Find(net, pos, v.transform.forward, target);
                    k.PathIdx = 0;
                    if (k.Path != null) k.PathIdx = Closest(k.Path, pos, 0, k.Path.Count);
                }
                if (k.Path != null && k.Path.Count > 1)
                {
                    // where on the route we are now (the closest point a little way on), then look ahead from there
                    k.PathIdx = Closest(k.Path, pos, k.PathIdx, Mathf.Min(k.Path.Count, k.PathIdx + 20));
                    while (k.PathIdx < k.Path.Count - 1 && Flat(k.Path[k.PathIdx] - pos).magnitude < 7f) k.PathIdx++;
                    float look = 6f + v.Speed * 0.5f;
                    int j = k.PathIdx;
                    while (j < k.Path.Count - 1 && Flat(k.Path[j] - pos).magnitude < look) j++;
                    aim = k.Path[j];
                    onPath = true;
                }
            }
            // close in: pull up on the road beside the target (straight across kerbs and planters gets wedged), then the last metres
            if (!onPath && net != null && dist > 9f)
            {
                var curb = net.NearestLanePoint(target, 14f);
                if (curb.valid && Flat(curb.position - pos).magnitude > 4f) aim = curb.position;
            }
            float speed = v.Speed;
            float steer = PursuitSteer(pos, v.transform.forward, aim, speed, v.Spec);
            // pure pursuit fades out for a point right behind: turn hard towards its side instead
            var aimDir = Flat(aim - pos);
            if (aimDir.sqrMagnitude > 1f && Vector3.Dot(Flat(v.transform.forward).normalized, aimDir.normalized) < -0.2f)
                steer = Vector3.SignedAngle(Flat(v.transform.forward), aimDir, Vector3.up) >= 0f ? 1f : -1f;
            float top = v.Spec != null ? v.Spec.topSpeed : 40f;
            float targetSpeed = Flat(targetVel).magnitude;
            float want = dist > 45f ? top * 0.85f : dist > 15f ? Mathf.Max(18f, targetSpeed + 6f) : Mathf.Max(6f, targetSpeed + 2f);
            // slow into the turn towards where we are steering (the next road bend, or the target)
            var toAim = Flat(aim - pos);
            float facing = Vector3.Dot(Flat(v.transform.forward).normalized, toAim.sqrMagnitude > 0.01f ? toAim.normalized : Vector3.forward);
            if (facing < 0.2f) want = Mathf.Min(want, 7f);
            else if (facing < 0.8f) want = Mathf.Min(want, 12f);
            if (onPath) want = Mathf.Min(want, 26f);
            // wedged against something: back off and swing round
            if (k.BackFor > 0f)
            {
                k.BackFor -= Time.deltaTime;
                v.Input = new CTLife.World.Vehicles.VehicleInput { brake = 0.7f, steer = -steer };
                return;
            }
            if (speed < 1f && want > 4f) { k.Stuck += Time.deltaTime; if (k.Stuck > 2.2f) { k.Stuck = 0f; k.BackFor = 1.3f; } }
            else k.Stuck = Mathf.Max(0f, k.Stuck - Time.deltaTime);
            // progress watchdog: not closing in for 8 s (walled in, the target off the road network) and out of sight:
            // come round onto a road near the target; far behind (lost in the streets): the same
            if (dist < k.Best - 4f) { k.Best = dist; k.BestAt = Time.time; }
            bool stalled = Time.time - k.BestAt > (k.Path != null ? 15f : 8f) && dist > 20f;
            if ((dist > 260f || stalled) && !Visible(pos) && SpawnPose(target, index, out var p, out var r, stalled))
            {
                k.Best = float.MaxValue; k.BestAt = Time.time;
                Log.Info("heat", $"cruiser repositioned ({(stalled ? "stalled" : "far")}, {dist:0} m)");
                if (v.Body != null) { v.Body.linearVelocity = Vector3.zero; v.Body.angularVelocity = Vector3.zero; v.Body.position = p; v.Body.rotation = r; }
                v.transform.SetPositionAndRotation(p, r);
            }
            v.Input = Pedals(speed, want, steer);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (index == 0 && Time.time >= k.LogAt)
            {
                k.LogAt = Time.time + 3f;
                var cp = v.GetComponent<CarPhysics>();
                Log.Info("heat", FormattableString.Invariant($"cruiser dist {dist:0} speed {speed:0.0} want {want:0} thr {v.Input.throttle:0.00} brk {v.Input.brake:0.00} steer {v.Input.steer:0.00} facing {facing:0.00} kin {(v.Body != null && v.Body.isKinematic)} car {(cp != null && cp.enabled)} rails {(cp != null && cp.Rails)} y {pos.y:0.0} aimDist {Flat(aim - pos).magnitude:0} path {(k.Path != null ? k.PathIdx + "/" + k.Path.Count : "none")}"));
            }
#endif
        }

        private static int Closest(List<Vector3> path, Vector3 pos, int from, int to)
        {
            int best = from; float bd = float.MaxValue;
            for (int i = from; i < to; i++) { float d = Flat(path[i] - pos).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        /// <summary>Pure-pursuit steering towards <paramref name="target"/> (the dev demo's road pilot, same maths).</summary>
        public static float PursuitSteer(Vector3 pos, Vector3 forward, Vector3 target, float speed, VehicleSpec spec)
        {
            const float wheelbase = 2.6f;
            var to = target - pos; to.y = 0f;
            var f = Vector3.ProjectOnPlane(forward, Vector3.up);
            float ld = Mathf.Max(1f, to.magnitude);
            float alpha = Vector3.SignedAngle(f, to, Vector3.up) * Mathf.Deg2Rad;
            float wheel = Mathf.Atan2(2f * wheelbase * Mathf.Sin(alpha), ld) * Mathf.Rad2Deg;
            float maxSteer = spec != null ? spec.maxSteer : 32f, highSpeed = spec != null ? spec.highSpeedSteer : 9f, top = spec != null ? spec.topSpeed : 40f;
            float lockAngle = Mathf.Lerp(maxSteer, highSpeed, Mathf.Clamp01(speed / Mathf.Max(1f, top * 0.75f)));
            return Mathf.Clamp(wheel / Mathf.Max(1f, lockAngle), -1f, 1f);
        }

        /// <summary>Throttle or brake towards <paramref name="want"/> m/s.</summary>
        public static CTLife.World.Vehicles.VehicleInput Pedals(float speed, float want, float steer)
        {
            var input = new CTLife.World.Vehicles.VehicleInput { steer = steer };
            float err = want - speed;
            if (want <= 0.1f) { input.brake = 1f; input.handbrake = speed < 1f; return input; }
            if (err > 0f) input.throttle = Mathf.Clamp01(err * 0.35f + 0.3f);
            else if (err < -1f) input.brake = Mathf.Clamp01(-err * 0.25f);
            return input;
        }

        // ------------------------------------------------------------------ helpers

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private static bool Visible(Vector3 p)
        {
            var cam = Camera.main;
            if (cam == null) return false;
            var vp = cam.WorldToViewportPoint(p);
            return vp.z > 0f && vp.z < 260f && vp.x > -0.15f && vp.x < 1.15f && vp.y > -0.15f && vp.y < 1.15f;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = new Bounds(go.transform.position, Vector3.one);
            bool any = false;
            foreach (var r in rs)
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }
    }
}
