#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System.Collections;
using System.Collections.Generic;
using System.Text;
using CTLife.Core;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// Dev builds only: a scripted run of the parked fleet on the real island, for Seeker clips and frame-rate numbers
    /// (Feel lane F2). Launch with the intent extra <c>vehicleDemo=1</c> (or a list: <c>vehicleDemo=sports,roadstar,heli,drone</c>;
    /// editor: <c>-ctlife-vehicleDemo 1</c>). Each vehicle is boarded the normal way and driven by a tiny autopilot that
    /// steers toward the most open direction; each part logs its frame rate:
    /// <c>CTLIFE [vehicles] demo sports drive: 58.9 fps avg, 41.2 min, 132 km/h top</c>, then one summary line.
    /// </summary>
    public static class VehicleDemo
    {
        public sealed class Part { public string name; public int frames, impacts; public float time, worst, topKmh, hardestImpact; }

        /// <summary>The parts driven so far (frame rate and top speed each).</summary>
        public static readonly List<Part> Parts = new List<Part>();
        public static bool Running { get; private set; }
        /// <summary>The part being driven now ("sports drive", "heli fly", ...), for captures that shoot each part.</summary>
        public static string Current { get; private set; }
        /// <summary>Knocks taken by the demo so far (captures shoot each one).</summary>
        public static int Impacts { get; private set; }

        public static IEnumerator Run(VehicleSystem sys, LocalPlayerController player, string which, float settleSeconds = 8f)
        {
            Running = true;
            try { yield return RunParts(sys, player, which, settleSeconds); }
            finally { Running = false; Current = null; VehicleSystem.ScriptedInput = false; }
        }

        private static IEnumerator RunParts(VehicleSystem sys, LocalPlayerController player, string which, float settleSeconds)
        {
            yield return new WaitForSeconds(settleSeconds);   // the fleet parks 20 frames in; let streaming and the HUD settle
            string[] ids = which == "1" || which == "all" ? new[] { "traffic", "sports", "roadstar", "heli", "drone" } : which.Split(',');
            Parts.Clear();
            _home = player.transform.position;
            VehicleSystem.ScriptedInput = true;
            foreach (var id in ids)
            {
                if (sys == null || player == null) yield break;
                if (id == "drone") { yield return Drone(sys); continue; }
                if (id == "traffic") { yield return TrafficWatch(player); continue; }
                var v = Nearest(sys, id, player.transform.position);
                if (v == null) { Log.Warn("vehicles", $"demo: no {id} parked near the player"); continue; }
                // cars start on the street, not nose to tail in a bay (the pilot clipped the car parked in front pulling out)
                if (v.Kind != VehicleKind.Helicopter && RoadPilot.PlaceOnLane(v)) yield return new WaitForSeconds(0.5f);
                // stand by its door, then get in the normal way
                var door = v.EntryPoint - v.transform.right * 0.6f;
                player.SnapTo(door, Mathf.Atan2(v.transform.right.x, v.transform.right.z));
                yield return new WaitForSeconds(0.8f);
                sys.Enter(v);
                float t0 = Time.time;
                while (sys.Driving != v && Time.time - t0 < 8f) yield return null;
                if (sys.Driving != v) { Log.Warn("vehicles", $"demo: could not board {id}"); continue; }
                yield return new WaitForSeconds(0.6f);
                switch (v.Kind)
                {
                    case VehicleKind.Helicopter: yield return Heli(v); break;
                    case VehicleKind.Roadstar: yield return Roadstar(v); break;
                    default: yield return Car(v); break;
                }
                sys.Exit();
                t0 = Time.time;
                while ((sys.Driving != null || sys.Busy) && Time.time - t0 < 8f) yield return null;
                yield return new WaitForSeconds(2f);
            }
            VehicleSystem.ScriptedInput = false;
            var sb = new StringBuilder("demo summary");
            foreach (var p in Parts) sb.Append($" | {p.name}: {(p.time > 0f ? p.frames / p.time : 0f):F1} avg {(p.worst > 0f ? 1f / p.worst : 0f):F1} min {p.topKmh:F0} km/h");
            Log.Info("vehicles", sb.ToString());
        }

        private static Vehicle Nearest(VehicleSystem sys, string id, Vector3 from)
        {
            Vehicle best = null; float d = float.MaxValue;
            foreach (var v in sys.Vehicles)
            {
                if (v == null || v.specId != id || v.Occupied) continue;
                float dd = (v.transform.position - from).sqrMagnitude;
                if (dd < d) { d = dd; best = v; }
            }
            return best;
        }

        // ------------------------------------------------------------------ parts

        private delegate VehicleInput Pilot(Vehicle v, float t);

        private static IEnumerator Drive(Vehicle v, string part, float seconds, Pilot pilot, System.Func<bool> until = null)
        {
            var p = new Part { name = $"{v.specId} {part}" };
            Current = p.name;
            var car = v.GetComponent<CarPhysics>();
            System.Action<float, Vector3> knock = (impulse, at) =>
            {
                p.impacts++; Impacts++; p.hardestImpact = Mathf.Max(p.hardestImpact, impulse);
                string what = "?";
                foreach (var c in Physics.OverlapSphere(at, 0.6f, ~0, QueryTriggerInteraction.Ignore))
                    if (c.attachedRigidbody != v.Body) { what = c.name + "@" + c.gameObject.layer; break; }
                var local = v.transform.InverseTransformPoint(at);
                var lp = CTLife.World.Roads.RoadNetwork.Active?.NearestLanePoint(v.transform.position, 30f);
                Log.Info("vehicles", $"demo {p.name}: impact {impulse:F0} N·s with {what} at {v.Speed * 3.6f:F0} km/h, local {local:F1}, " +
                    $"car at {v.transform.position:F1} yaw {v.transform.eulerAngles.y:F0}, nearest lane {(lp.HasValue && lp.Value.valid ? lp.Value.segmentId + "#" + lp.Value.lane + " " + lp.Value.distance.ToString("F1") + " m" : "none")}");
            };
            if (car != null) car.Impact += knock;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (v == null || v.Driver == null) break;
                v.Input = pilot(v, t);
                yield return null;
                p.frames++; p.time += Time.unscaledDeltaTime; p.worst = Mathf.Max(p.worst, Time.unscaledDeltaTime);
                p.topKmh = Mathf.Max(p.topKmh, v.Speed * 3.6f);
                if (until != null && until()) break;
            }
            if (car != null) car.Impact -= knock;
            Parts.Add(p);
            Log.Info("vehicles", $"demo {p.name}: {(p.time > 0f ? p.frames / p.time : 0f):F1} fps avg, {(p.worst > 0f ? 1f / p.worst : 0f):F1} min, {p.topKmh:F0} km/h top, {p.impacts} impacts");
        }

        private static IEnumerator Car(Vehicle v)
        {
            // on the road network (the wall-avoiding wanderer drove into buildings at speed on the Seeker run, 2026-10-18)
            var road = RoadPilot.Start(v);
            yield return Drive(v, "drive", 12f, (c, t) => road != null ? road.Steer(c) : Auto(c, 1f));
            float side = Open(v, 50f) >= Open(v, -50f) ? 1f : -1f;
            yield return Drive(v, "drift", 1.5f, (c, t) => new VehicleInput { throttle = 0.6f, steer = side, handbrake = true });
            road = RoadPilot.Start(v);
            yield return Drive(v, "drive2", 6f, (c, t) => road != null ? road.Steer(c) : Auto(c, 1f));
            yield return Drive(v, "brake", 3f, (c, t) => new VehicleInput { brake = 1f, handbrake = c.Speed < 6f }, () => v.Speed < 0.5f);
        }

        private static IEnumerator Roadstar(Vehicle v)
        {
            var f = v.GetComponent<FlightPhysics>();
            var road = RoadPilot.Start(v);
            yield return Drive(v, "road", 3f, (c, t) => road != null ? road.Steer(c) : Auto(c, 1f));
            f.SetFlight(true);
            yield return Drive(v, "takeoff", 4f, (c, t) => new VehicleInput { lift = 1f });
            yield return Drive(v, "fly", 8f, (c, t) => new VehicleInput { throttle = 0.75f, lift = 0.1f, steer = Mathf.Clamp(HomeTurn(c, 140f) / 30f, -1f, 1f) });
            f.SetFlight(false);
            yield return Drive(v, "land", 18f, (c, t) => new VehicleInput { brake = 0.4f, throttle = f.OverWater ? 0.6f : 0f, steer = Mathf.Clamp(HomeTurn(c, 40f) / 30f, -1f, 1f) }, () => !v.Flying);
            yield return Drive(v, "stop", 3f, (c, t) => new VehicleInput { brake = 1f, handbrake = true }, () => v.Speed < 0.5f);
        }

        private static IEnumerator Heli(Vehicle v)
        {
            var f = v.GetComponent<FlightPhysics>();
            yield return Drive(v, "spinup", 3.5f, (c, t) => VehicleInput.None);
            yield return Drive(v, "takeoff", 5f, (c, t) => new VehicleInput { lift = 1f });
            yield return Drive(v, "fly", 7f, (c, t) => new VehicleInput { pitch = 1f, yaw = Mathf.Clamp(HomeTurn(c, 120f) / 30f, -1f, 1f) });
            yield return Drive(v, "hover", 4f, (c, t) => VehicleInput.None);
            yield return Drive(v, "land", 25f, (c, t) => new VehicleInput { lift = f.Altitude > 6f ? -1f : -0.5f }, () => f.Grounded);
            yield return Drive(v, "settle", 1.5f, (c, t) => VehicleInput.None);
        }

        private static IEnumerator Drone(VehicleSystem sys)
        {
            if (!sys.DeployDrone()) { Log.Warn("vehicles", "demo: the drone would not launch"); yield break; }
            var d = sys.Driving;
            yield return Drive(d, "climb", 2.5f, (c, t) => new VehicleInput { lift = 1f });
            yield return Drive(d, "fly", 5f, (c, t) => new VehicleInput { pitch = 1f, yaw = 0.35f });
            sys.ExitOrLand();
            float t0 = Time.time;
            while (sys.Driving != null && Time.time - t0 < 10f) yield return null;
        }

        /// <summary>Stand at the nearest traffic-light junction for 25 s: the frame rate with traffic and NPC drivers in view.</summary>
        private static IEnumerator TrafficWatch(LocalPlayerController player)
        {
            var net = CTLife.World.Roads.RoadNetwork.Active;
            var traffic = TrafficSystem.Current;
            var x = TrafficSystem.Nearest(net, player.transform.position, CTLife.World.Roads.IntersectionKind.Signal);
            if (traffic == null || x == null) { Log.Warn("vehicles", "demo: no traffic or no signalled junction"); yield break; }
            var corner = TrafficSystem.Corner(net, x);
            var face = x.center - corner;
            player.SnapTo(corner, Mathf.Atan2(face.x, face.z));
            Current = "traffic watch";
            var p = new Part { name = "traffic watch" };
            int reds = traffic.RedStops, honks = traffic.Honks, seatedMax = 0, nearMax = 0, noSeatMax = 0;
            for (float t = 0f; t < 25f; t += Time.deltaTime)
            {
                yield return null;
                p.frames++; p.time += Time.unscaledDeltaTime; p.worst = Mathf.Max(p.worst, Time.unscaledDeltaTime);
                int seated = 0;
                foreach (var c in traffic.Cars) if (c != null && c.Driver != null) seated++;
                seatedMax = Mathf.Max(seatedMax, seated);
                if (traffic.Drivers != null) { nearMax = Mathf.Max(nearMax, traffic.Drivers.NearInView); noSeatMax = Mathf.Max(noSeatMax, traffic.Drivers.NearWithoutSeat); }
            }
            Parts.Add(p);
            Log.Info("vehicles", $"demo traffic watch at {x.id}: {(p.time > 0f ? p.frames / p.time : 0f):F1} fps avg, {(p.worst > 0f ? 1f / p.worst : 0f):F1} min; " +
                                 $"{traffic.Cars.Count} cars, up to {seatedMax} drivers ({nearMax} near cars in view, {noSeatMax} without a seat), step {traffic.StepMs:F2} ms, {traffic.RedStops - reds} red stops, {traffic.Honks - honks} honks");
        }

        // ------------------------------------------------------------------ the autopilot

        private static Vector3 _home;

        /// <summary>Flight: wander freely near home, turn back toward it beyond <paramref name="radius"/> (keeps the run over land).</summary>
        private static float HomeTurn(Vehicle v, float radius)
        {
            var to = _home - v.transform.position; to.y = 0f;
            if (to.magnitude < radius) return 0f;
            var fwd = Vector3.ProjectOnPlane(v.transform.forward, Vector3.up);
            return Vector3.SignedAngle(fwd, to, Vector3.up);
        }

        /// <summary>
        /// Follows a lane of the road network (the one beside the car that runs its way), through the junctions the route
        /// picks: pure-pursuit steering, the speed limit (at most <see cref="Cruise"/>), slowing for the curves ahead and
        /// braking for anything close in front. Signals are ignored: it is a demo, and the junction traffic yields on rails.
        /// </summary>
        public sealed class RoadPilot
        {
            public const float Cruise = 17f, Wheelbase = 2.6f;
            private readonly TrafficRoute _route = new TrafficRoute();
            private static readonly System.Random Rng = new System.Random(7);

            public static RoadPilot Start(Vehicle v)
            {
                var net = CTLife.World.Roads.RoadNetwork.Active;
                if (net == null || v == null) return null;
                var lp = net.NearestLanePoint(v.transform.position, 40f);
                if (!lp.valid) return null;
                var seg = net.Segment(lp.segmentId);
                if (seg == null) return null;
                var fwd = Vector3.ProjectOnPlane(v.transform.forward, Vector3.up).normalized;
                // the lane of this road that runs the way the car faces
                int best = -1; float bestScore = float.MinValue, bestAlong = 0f;
                for (int lane = 0; lane < seg.LaneCount; lane++)
                {
                    var pts = net.LaneCenterline(seg.id, lane);
                    if (pts.Length < 2) continue;
                    var piece = RoutePiece.Make(pts);
                    float along = piece.Locate(v.transform.position, out _, out float d);
                    piece.Sample(along, out var dir);
                    float score = Vector3.Dot(Vector3.ProjectOnPlane(dir, Vector3.up).normalized, fwd) * 10f - d;
                    if (score > bestScore) { bestScore = score; best = lane; bestAlong = along; }
                }
                if (best < 0) return null;
                var p = new RoadPilot();
                p.Measure(v);
                if (!p._route.StartAt(net, new CTLife.World.Roads.LaneRef(seg.id, best), bestAlong + 4f, Rng)) return null;
                p._route.Extend(90f);
                Log.Info("vehicles", $"demo road pilot: lane {seg.id}#{best} ({lp.distance:F1} m off), limit {p._route.Current?.SpeedLimit:F1} m/s");
                return p;
            }

            private float _backFor = -1f, _stuck;

            /// <summary>Moves a parked car onto the nearest free stretch of a travel lane, facing along it (demo set-up).</summary>
            public static bool PlaceOnLane(Vehicle v)
            {
                var net = CTLife.World.Roads.RoadNetwork.Active;
                if (net == null || v == null || v.Body == null) return false;
                var start = v.transform.position;
                for (float r = 0f; r <= 60f; r += 12f)
                {
                    var probe = start + Quaternion.Euler(0f, r * 6f, 0f) * Vector3.forward * r;
                    var lp = net.NearestLanePoint(probe, 40f);
                    if (!lp.valid) continue;
                    var seg = net.Segment(lp.segmentId);
                    if (seg == null) continue;
                    var pts = net.LaneCenterline(seg.id, lp.lane);
                    if (pts.Length < 2) continue;
                    var piece = RoutePiece.Make(pts);
                    if (piece.Length < 30f) continue;
                    float along = Mathf.Clamp(lp.along, 8f, piece.Length - 20f);
                    var at = piece.Sample(along, out var dir);
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-4f) continue;
                    var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    // nothing on or right beside that stretch (traffic, another parked car)
                    if (Physics.CheckBox(at + Vector3.up * 1.2f + dir.normalized * 3f, new Vector3(1.3f, 0.8f, 7f), rot, 1 << CharacterLayers.Vehicle, QueryTriggerInteraction.Ignore))
                    {
                        bool onlySelf = true;
                        foreach (var c in Physics.OverlapBox(at + Vector3.up * 1.2f + dir.normalized * 3f, new Vector3(1.3f, 0.8f, 7f), rot, 1 << CharacterLayers.Vehicle, QueryTriggerInteraction.Ignore))
                            if (c.attachedRigidbody != v.Body) { onlySelf = false; break; }
                        if (!onlySelf) continue;
                    }
                    v.Body.linearVelocity = Vector3.zero; v.Body.angularVelocity = Vector3.zero;
                    v.Body.position = at + Vector3.up * 0.4f; v.Body.rotation = rot;
                    v.transform.SetPositionAndRotation(v.Body.position, rot);
                    Log.Info("vehicles", $"demo: {v.specId} set on lane {seg.id}#{lp.lane} at {at:F1}");
                    return true;
                }
                return false;
            }

            public VehicleInput Steer(Vehicle v)
            {
                var pos = v.transform.position;
                float lateral = 0f;
                if (_route.Project(pos, 30f, out float ahead, out lateral) && ahead > 0f) _route.Advance(ahead);
                _route.Extend(90f);
                float speed = v.Speed;
                // pulling out of a bay: aim close, so the nose swings out before it reaches the car parked in front
                var target = _route.At(Mathf.Abs(lateral) > 1f ? 3f : 5f + speed * 0.55f, out _);
                float steer = PursuitSteer(pos, v.transform.forward, target, speed, v.Spec);
                // Parked nose to tail: back out of the bay a little first (brake at a standstill reverses).
                if (_backFor < 0f) _backFor = Gap(v, 1f) < 4.5f && Gap(v, -1f) > 1.2f ? 2.5f : 0f;
                if (_backFor > 0f)
                {
                    _backFor -= Time.deltaTime;
                    if (Gap(v, 1f) >= 4.5f || Gap(v, -1f) < 0.7f) _backFor = 0f;
                    return new VehicleInput { brake = 0.55f, steer = -steer };
                }
                // Wedged against something while trying to go (a kerb post, the car in front): back off and retry.
                if (speed < 0.4f && Gap(v, 1f) < 1.2f) { _stuck += Time.deltaTime; if (_stuck > 1f) { _stuck = 0f; _backFor = 1.2f; } }
                else _stuck = 0f;
                // the slowest the road ahead allows, braking comfortably from here
                float want = Mathf.Min(Cruise, Mathf.Max(6f, _route.Current != null ? _route.Current.SpeedLimit * 1.15f : Cruise));
                for (float s = 6f; s <= 50f; s += 4f)
                    want = Mathf.Min(want, TrafficRules.ApproachSpeed(TrafficRules.CurveSpeed(_route.Curvature(s)), s - 4f));
                // something solid on the lane ahead (not the cars parked beside it): stop short of it
                float blocked = Blocked(v, target);
                if (blocked < float.MaxValue) want = Mathf.Min(want, Mathf.Max(0f, blocked - 5f) * 0.8f);
                // and whatever is right in front of the bumper, on the lane or not (the car parked ahead, a kerb post)
                float front = Gap(v, 1f);
                want = Mathf.Min(want, Mathf.Max(0f, front - 1.2f) * 1.3f);
                return Pedals(speed, want, steer);
            }

            /// <summary>Free metres past the nose (<paramref name="way"/> 1) or the tail (-1) to anything solid, at bumper and
            /// bonnet height across the car's width (the sphere casts miss what already touches the bumper).</summary>
            private float Gap(Vehicle v, float way)
            {
                var t = v.transform;
                var dir = t.forward * way;
                int mask = CarPhysics.GroundMask | (1 << CharacterLayers.Vehicle);
                float best = 20f;
                foreach (float side in Sides)
                    foreach (float h in Heights)
                    {
                        var from = t.position + t.up * h + t.right * side;
                        foreach (var hit in Physics.RaycastAll(from, dir, 20f, mask, QueryTriggerInteraction.Ignore))
                            if (hit.collider.attachedRigidbody != v.Body && hit.normal.y < 0.6f) best = Mathf.Min(best, hit.distance);
                    }
                return Mathf.Max(0f, best - (way > 0f ? _front : _back));   // from the centre: take off the car itself
            }

            private static readonly float[] Sides = { -0.95f, -0.45f, 0f, 0.45f, 0.95f }, Heights = { 0.45f, 0.85f };
            private float _front = 2.1f, _back = 2.1f;

            private void Measure(Vehicle v)
            {
                var t = v.transform;
                float f = 0f, b = 0f;
                foreach (var c in v.GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger || c is WheelCollider) continue;
                    var bb = c.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vector3((i & 1) == 0 ? bb.min.x : bb.max.x, (i & 2) == 0 ? bb.min.y : bb.max.y, (i & 4) == 0 ? bb.min.z : bb.max.z);
                        float along = Vector3.Dot(corner - t.position, t.forward);
                        f = Mathf.Max(f, along); b = Mathf.Max(b, -along);
                    }
                }
                if (f > 0.5f) _front = f;
                if (b > 0.5f) _back = b;
            }

            /// <summary>Distance to the first solid thing on the lane toward <paramref name="target"/> within 40 m (MaxValue when clear).</summary>
            private float Blocked(Vehicle v, Vector3 target)
            {
                var from = v.transform.position + Vector3.up * 0.9f;
                var dir = target - v.transform.position; dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.ProjectOnPlane(v.transform.forward, Vector3.up);
                dir.Normalize();
                int mask = CarPhysics.GroundMask | (1 << CharacterLayers.Vehicle);
                var hits = Physics.SphereCastAll(from + dir * 2.6f, 0.7f, dir, 40f, mask, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                foreach (var h in hits)
                {
                    if (h.collider == null || h.collider.attachedRigidbody == v.Body || h.distance <= 0f || h.normal.y > 0.6f) continue;   // not the road rising
                    // off the travel lane (a parked car, a kerbside post): not in the way
                    if (_route.Project(h.point, 45f, out _, out float lateral) && Mathf.Abs(lateral) > 1.7f) continue;
                    best = Mathf.Min(best, h.distance + 2.6f);
                }
                return best;
            }

            /// <summary>Pure pursuit: the steer input (-1..1) that bends the car's path through <paramref name="target"/>. Pure, EditMode-tested.</summary>
            public static float PursuitSteer(Vector3 pos, Vector3 forward, Vector3 target, float speed, VehicleSpec spec)
            {
                var to = target - pos; to.y = 0f;
                var f = Vector3.ProjectOnPlane(forward, Vector3.up);
                float ld = Mathf.Max(1f, to.magnitude);
                float alpha = Vector3.SignedAngle(f, to, Vector3.up) * Mathf.Deg2Rad;
                float wheel = Mathf.Atan2(2f * Wheelbase * Mathf.Sin(alpha), ld) * Mathf.Rad2Deg;
                float maxSteer = spec != null ? spec.maxSteer : 32f, highSpeed = spec != null ? spec.highSpeedSteer : 9f, top = spec != null ? spec.topSpeed : 40f;
                float lockAngle = Mathf.Lerp(maxSteer, highSpeed, Mathf.Clamp01(speed / Mathf.Max(1f, top * 0.75f)));
                return Mathf.Clamp(wheel / Mathf.Max(1f, lockAngle), -1f, 1f);
            }

            /// <summary>Throttle or brake toward <paramref name="want"/> m/s. Pure, EditMode-tested.</summary>
            public static VehicleInput Pedals(float speed, float want, float steer)
            {
                float err = want - speed;
                var input = new VehicleInput { steer = steer };
                if (want <= 0.1f) { input.brake = 1f; input.handbrake = speed < 1f; return input; }
                if (err > 0f) input.throttle = Mathf.Clamp01(err * 0.35f + 0.25f);
                else if (err < -0.8f) input.brake = Mathf.Clamp01(-err * 0.25f);
                return input;
            }
        }

        /// <summary>Gas, steering toward the most open heading within +-60 degrees; brakes for a wall close ahead.</summary>
        private static VehicleInput Auto(Vehicle v, float throttle)
        {
            float turn = Turn(v);
            float ahead = Open(v, 0f);
            bool wall = ahead < 10f + v.Speed * 0.6f && v.Speed > 9f;
            return new VehicleInput { throttle = wall ? 0f : throttle, brake = wall ? 0.7f : 0f, steer = Mathf.Clamp(turn / 25f, -1f, 1f) };
        }

        private static float Turn(Vehicle v)
        {
            float best = 0f, score = float.MinValue;
            for (float a = -60f; a <= 60f; a += 15f)
            {
                float s = Open(v, a) - Mathf.Abs(a) * 0.35f;
                if (s > score) { score = s; best = a; }
            }
            return best;
        }

        /// <summary>
        /// Free distance (to 120 m) at <paramref name="yaw"/> degrees off the nose, at bumper height, and only as far as there
        /// is dry ground under it (the most open way is often the sea).
        /// </summary>
        private static float Open(Vehicle v, float yaw)
        {
            var dir = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.ProjectOnPlane(v.transform.forward, Vector3.up).normalized;
            var from = v.transform.position + Vector3.up * 0.9f + dir * 2.6f;
            int mask = CarPhysics.GroundMask | (1 << CharacterLayers.Vehicle);
            float free = Physics.SphereCast(from, 0.7f, dir, out var hit, 120f, mask, QueryTriggerInteraction.Ignore) ? hit.distance : 120f;
            float sea = VehicleWorld.SeaLevel;
            for (float d = 8f; d < free; d += 8f)
            {
                var p = from + dir * d;
                bool land = Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var g, 9f, CarPhysics.GroundMask, QueryTriggerInteraction.Ignore)
                            && (float.IsInfinity(sea) || g.point.y > sea + 0.1f);
                if (!land) return d;
            }
            return free;
        }
    }
}
#endif
