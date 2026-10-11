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
        public sealed class Part { public string name; public int frames; public float time, worst, topKmh; }

        /// <summary>The parts driven so far (frame rate and top speed each).</summary>
        public static readonly List<Part> Parts = new List<Part>();
        public static bool Running { get; private set; }
        /// <summary>The part being driven now ("sports drive", "heli fly", ...), for captures that shoot each part.</summary>
        public static string Current { get; private set; }

        public static IEnumerator Run(VehicleSystem sys, LocalPlayerController player, string which, float settleSeconds = 8f)
        {
            Running = true;
            try { yield return RunParts(sys, player, which, settleSeconds); }
            finally { Running = false; Current = null; VehicleSystem.ScriptedInput = false; }
        }

        private static IEnumerator RunParts(VehicleSystem sys, LocalPlayerController player, string which, float settleSeconds)
        {
            yield return new WaitForSeconds(settleSeconds);   // the fleet parks 20 frames in; let streaming and the HUD settle
            string[] ids = which == "1" || which == "all" ? new[] { "sports", "roadstar", "heli", "drone" } : which.Split(',');
            Parts.Clear();
            _home = player.transform.position;
            VehicleSystem.ScriptedInput = true;
            foreach (var id in ids)
            {
                if (sys == null || player == null) yield break;
                if (id == "drone") { yield return Drone(sys); continue; }
                var v = Nearest(sys, id, player.transform.position);
                if (v == null) { Log.Warn("vehicles", $"demo: no {id} parked near the player"); continue; }
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
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (v == null || v.Driver == null) break;
                v.Input = pilot(v, t);
                yield return null;
                p.frames++; p.time += Time.unscaledDeltaTime; p.worst = Mathf.Max(p.worst, Time.unscaledDeltaTime);
                p.topKmh = Mathf.Max(p.topKmh, v.Speed * 3.6f);
                if (until != null && until()) break;
            }
            Parts.Add(p);
            Log.Info("vehicles", $"demo {p.name}: {(p.time > 0f ? p.frames / p.time : 0f):F1} fps avg, {(p.worst > 0f ? 1f / p.worst : 0f):F1} min, {p.topKmh:F0} km/h top");
        }

        private static IEnumerator Car(Vehicle v)
        {
            yield return Drive(v, "drive", 8f, (c, t) => Auto(c, 1f));
            float side = Open(v, 50f) >= Open(v, -50f) ? 1f : -1f;
            yield return Drive(v, "drift", 1.5f, (c, t) => new VehicleInput { throttle = 0.6f, steer = side, handbrake = true });
            yield return Drive(v, "drive2", 4f, (c, t) => Auto(c, 1f));
            yield return Drive(v, "brake", 3f, (c, t) => new VehicleInput { brake = 1f, handbrake = c.Speed < 6f }, () => v.Speed < 0.5f);
        }

        private static IEnumerator Roadstar(Vehicle v)
        {
            var f = v.GetComponent<FlightPhysics>();
            yield return Drive(v, "road", 3f, (c, t) => Auto(c, 1f));
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
