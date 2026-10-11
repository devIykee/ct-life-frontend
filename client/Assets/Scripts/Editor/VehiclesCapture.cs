using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTLife.UI;
using CTLife.World;
using CTLife.World.Vehicles;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Feel lane F2: every vehicle on a proving ground 60 m above Timeline Square (a 360 m strip with a crash wall), driven by
    /// scripted input on the real Vehicle / CarPhysics / FlightPhysics / VehicleSystem at a fixed 60 fps: the GTA-style way
    /// in and out, 0-100 km/h, top speed, braking, a handbrake drift, a crash, a roll-over recovery, the scooter, the
    /// Roadstar driving, lifting off, flying and landing, the helicopter taking off, flying, hovering on its own and landing,
    /// the pocket drone. captureSet "vehicles"; writes receipt["vehicles"] and shots vehicles-*; with -ctlife-vehiclesClip 1
    /// also 30 fps half-size film frames vehicles-clip-NNNN.jpg (taken without waiting frames, so timing is unchanged).
    /// </summary>
    internal static class VehiclesCapture
    {
        private static double R(float v) => Math.Round(v, 3);

        public static async Task<VenueSession> Run(VenueSession visit, Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, JObject receipt)
        {
            var o = new JObject(); receipt["vehicles"] = o;
            await visit.Exit();
            visit = await VenueSession.EnterIsland("timeline_square", joinRoom: false);
            await wait(2f);
            var p = visit.LocalPlayer;
            var rig = CameraRig.Instance;
            var sys = VehicleSystem.Current;
            expect(sys != null, "the island session has a VehicleSystem");
            if (sys == null) return visit;
            var origin = p.transform.position + Vector3.up * 60f;
            var ground = BuildGround(origin);
            Physics.SyncTransforms();
            int prevCapture = Time.captureFramerate;
            Time.captureFramerate = 60;
            VehicleControls.ScriptedInput = true;
            bool film = Environment.GetCommandLineArgs().Contains("-ctlife-vehiclesClip");
            int frame = 0;
            var marks = new JObject(); o["clipMarks"] = marks;   // film frame (30 fps) where each part starts
            void Mark(string name) => marks[name] = frame / 2;
            async Task Step(VehicleInput input, Vehicle v)
            {
                if (v != null) v.Input = input;
                await Awaitable.NextFrameAsync();
                if (film && (frame++ & 1) == 0) OnboardingCapture.FilmFrame($"vehicles-clip-{frame / 2:0000}");
            }
            async Task Hold(float seconds, VehicleInput input, Vehicle v, Action<float> each = null)
            {
                for (float t = 0f; t < seconds; t += 1f / 60f) { await Step(input, v); each?.Invoke(t); }
            }
            async Task Place(Vector3 rel, float yaw)
            {
                p.SnapTo(origin + rel, yaw * Mathf.Deg2Rad);
                rig.FollowAvatar(p.transform);
                for (int i = 0; i < 20; i++) await Awaitable.NextFrameAsync();
            }
            // put a vehicle somewhere, still (rigidbody and transform together, so interpolation cannot show the old place)
            async Task Teleport(Vehicle v, Vector3 rel, float yaw)
            {
                var at = origin + rel; var rot = Quaternion.Euler(0f, yaw, 0f);
                v.Body.linearVelocity = Vector3.zero; v.Body.angularVelocity = Vector3.zero;
                v.Body.position = at; v.Body.rotation = rot;
                v.transform.SetPositionAndRotation(at, rot);
                Physics.SyncTransforms();
                for (int i = 0; i < 20; i++) await Step(VehicleInput.None, v);
            }
            async Task<bool> EnterAndWait(Vehicle v, string shotName)
            {
                bool ok = sys.Enter(v);
                float t0 = Time.time; bool shotTaken = false;
                while (sys.Driving != v && Time.time - t0 < 8f)
                {
                    await Awaitable.NextFrameAsync();
                    if (!shotTaken && v.DoorOpen > 0.6f) { shotTaken = true; await shot(shotName, 0f); }
                    if (film && (frame++ & 1) == 0) OnboardingCapture.FilmFrame($"vehicles-clip-{frame / 2:0000}");
                }
                return ok && sys.Driving == v;
            }
            async Task<bool> ExitAndWait(string shotName)
            {
                var v = sys.Driving;
                bool ok = sys.Exit();
                float t0 = Time.time; bool shotTaken = false;
                while ((sys.Driving != null || sys.Busy) && Time.time - t0 < 6f)
                {
                    await Awaitable.NextFrameAsync();
                    if (!shotTaken && v != null && v.DoorOpen > 0.6f && !p.InVehicle) { shotTaken = true; await shot(shotName, 0f); }
                    if (film && (frame++ & 1) == 0) OnboardingCapture.FilmFrame($"vehicles-clip-{frame / 2:0000}");
                }
                for (int i = 0; i < 40; i++)
                {
                    await Awaitable.NextFrameAsync();
                    if (film && (frame++ & 1) == 0) OnboardingCapture.FilmFrame($"vehicles-clip-{frame / 2:0000}");
                }
                return ok && !p.InVehicle;
            }
            try
            {
                // ---------------------------------------------------------------- the sports car
                Mark("enter");
                var car = sys.Spawn("sports", origin + new Vector3(0f, 0.1f, -150f), Quaternion.identity);
                for (int i = 0; i < 60; i++) await Awaitable.NextFrameAsync();   // settle on the springs
                float rest = car.transform.position.y - origin.y;
                await Place(new Vector3(-4.5f, 0f, -150f), 90f);
                await shot("vehicles-01-parked", .05f);
                bool entered = await EnterAndWait(car, "vehicles-02-door-open");
                expect(entered && p.InVehicle && p.transform.IsChildOf(car.transform), "walked to the door, got in, door shut");
                await shot("vehicles-03-in-the-seat", .05f);
                Mark("drive");
                float t100 = -1f, top = 0f;
                await Hold(9f, new VehicleInput { throttle = 1f }, car, t => { float kmh = car.Speed * 3.6f; top = Mathf.Max(top, kmh); if (t100 < 0f && kmh >= 100f) t100 = t; });
                await shot("vehicles-04-full-speed", .02f);
                var brakeFrom = car.transform.position;
                float vBrake = car.Speed;
                await Hold(5f, new VehicleInput { brake = 1f }, car);
                float brakeDist = Vector3.Distance(Flat(brakeFrom), Flat(car.transform.position));
                o["sports"] = new JObject { ["restHeight"] = R(rest), ["zeroTo100s"] = R(t100), ["topKmhIn9s"] = R(top), ["brakeFromKmh"] = R(vBrake * 3.6f), ["brakeMetres"] = R(brakeDist) };
                expect(t100 > 2.5f && t100 < 9f, $"0-100 km/h in {t100:F1} s");
                expect(brakeDist > 10f && brakeDist < 90f, $"stops from {vBrake * 3.6f:F0} km/h in {brakeDist:F0} m");
                Mark("drift");
                // handbrake drift: back to the start of the strip, build speed, then turn with the handbrake
                await Teleport(car, new Vector3(0f, 0.2f, -120f), 0f);
                await Hold(3.2f, new VehicleInput { throttle = 1f }, car);
                float maxSlip = 0f, maxYaw = 0f, maxSkid = 0f, driftFrom = car.Speed * 3.6f;
                var cp = car.GetComponent<CarPhysics>();
                void Drift(float t)
                {
                    var v = car.Body.linearVelocity; var f = car.transform.forward;
                    if (v.magnitude > 3f) maxSlip = Mathf.Max(maxSlip, Vector3.Angle(Vector3.ProjectOnPlane(v, Vector3.up), Vector3.ProjectOnPlane(f, Vector3.up)));
                    maxYaw = Mathf.Max(maxYaw, Mathf.Abs(car.Body.angularVelocity.y) * Mathf.Rad2Deg);
                    maxSkid = Mathf.Max(maxSkid, cp.SkidAmount);
                }
                var driftInput = new VehicleInput { throttle = 0.6f, steer = 1f, handbrake = true };
                await Hold(0.7f, driftInput, car, Drift);
                await shot("vehicles-05-drift", .02f);   // mid-slide
                await Hold(0.9f, driftInput, car, Drift);
                float driftTo = car.Speed * 3.6f;
                await Hold(1.2f, new VehicleInput { throttle = 0.5f, steer = -0.4f }, car);
                await Hold(2.5f, new VehicleInput { brake = 1f }, car);
                o["drift"] = new JObject { ["fromKmh"] = R(driftFrom), ["toKmh"] = R(driftTo), ["maxSlipDeg"] = R(maxSlip), ["maxYawRateDeg"] = R(maxYaw), ["maxSkid"] = R(maxSkid) };
                expect(maxSlip > 15f && maxSkid > 0.3f, $"the handbrake breaks the rear loose (slip {maxSlip:F0} deg, skid {maxSkid:F2}, {driftFrom:F0} -> {driftTo:F0} km/h)");
                Mark("crash");
                // crash into the wall at speed (through a person-sized kinematic capsule first: people are not walls)
                await Teleport(car, new Vector3(0f, 0.2f, 100f), 0f);
                var person = new GameObject("capture-pedestrian") { layer = CharacterLayers.Characters };
                person.transform.SetParent(ground.transform, false);
                person.transform.position = origin + new Vector3(0f, 0f, 128f);
                var pc = person.AddComponent<CapsuleCollider>(); pc.center = new Vector3(0f, 0.88f, 0f); pc.height = 1.72f; pc.radius = 0.24f;
                var prb = person.AddComponent<Rigidbody>(); prb.isKinematic = true; prb.useGravity = false;
                float impulse = 0f, beforePerson = -1f, afterPerson = -1f;
                void OnImpact(float i, Vector3 _) => impulse = Mathf.Max(impulse, i);
                car.GetComponent<CarPhysics>().Impact += OnImpact;
                await Hold(5f, new VehicleInput { throttle = 1f }, car, _ =>
                {
                    float z = car.transform.position.z - origin.z;
                    if (beforePerson < 0f && z > 122f) beforePerson = car.Speed;
                    if (afterPerson < 0f && z > 133f) afterPerson = car.Speed;
                });
                await shot("vehicles-06-crash", .02f);
                car.GetComponent<CarPhysics>().Impact -= OnImpact;
                await Hold(2f, new VehicleInput { brake = 1f }, car);
                o["crash"] = new JObject { ["impulse"] = R(impulse), ["upright"] = R(car.transform.up.y), ["kmhBeforePerson"] = R(beforePerson * 3.6f), ["kmhAfterPerson"] = R(afterPerson * 3.6f) };
                expect(impulse > 0f, $"hitting the wall registers ({impulse:F0} N·s)");
                expect(beforePerson > 5f && afterPerson > beforePerson * 0.8f, $"a person in the road is not a wall ({beforePerson * 3.6f:F0} -> {afterPerson * 3.6f:F0} km/h)");
                Mark("rollover");
                // roll it over: it rights itself
                car.Body.rotation = Quaternion.Euler(0f, 0f, 175f); car.Body.position += Vector3.up * 1.5f;
                float rightedAt = -1f, rollT0 = Time.time;
                await Hold(5f, VehicleInput.None, car, _ => { if (rightedAt < 0f && car.transform.up.y > 0.9f && Time.time - rollT0 > 0.5f) rightedAt = Time.time - rollT0; });
                o["rollover"] = new JObject { ["uprightAfter"] = R(car.transform.up.y), ["rightedInS"] = R(rightedAt) };
                expect(car.transform.up.y > 0.9f, $"an upside-down car sets itself back on its wheels ({rightedAt:F1} s)");
                await Teleport(car, new Vector3(-8f, 0.2f, 60f), 20f);   // out in the open (not against the crash wall)
                Mark("exit");
                bool exited = await ExitAndWait("vehicles-07-getting-out");
                expect(exited && !p.InVehicle && p.Body.Grounded, "got out beside the car, on foot");
                // parked: it holds still (no creeping in reverse)
                var parkedAt = car.transform.position;
                for (int i = 0; i < 120; i++) await Awaitable.NextFrameAsync();
                float creep = Vector3.Distance(parkedAt, car.transform.position);
                o["parked"] = new JObject { ["creepMetres"] = R(creep) };
                expect(creep < 0.08f, $"a parked car stays put ({creep:F3} m in 2 s)");
                Mark("scooter");
                // ---------------------------------------------------------------- the scooter
                var scooter = sys.Spawn("scooter", origin + new Vector3(20f, 0.1f, -150f), Quaternion.identity);
                for (int i = 0; i < 40; i++) await Awaitable.NextFrameAsync();
                await Place(new Vector3(18.6f, 0f, -150f), 90f);
                bool rode = await EnterAndWait(scooter, "vehicles-08-scooter-mount");
                float scooterTop = 0f;
                await Hold(5f, new VehicleInput { throttle = 1f }, scooter, _ => scooterTop = Mathf.Max(scooterTop, scooter.Speed * 3.6f));
                await Hold(1.5f, new VehicleInput { throttle = 1f, steer = 0.8f }, scooter);
                await shot("vehicles-09-scooter-lean", .02f);
                await Hold(2f, new VehicleInput { brake = 1f }, scooter);
                o["scooter"] = new JObject { ["rode"] = rode, ["topKmh"] = R(scooterTop), ["upright"] = R(scooter.transform.up.y) };
                expect(rode && scooterTop > 40f && scooter.transform.up.y > 0.9f, $"the scooter rides ({scooterTop:F0} km/h) and stays up");
                await ExitAndWait("vehicles-10-scooter-off");
                Mark("roadstar");
                // ---------------------------------------------------------------- the Roadstar
                var star = sys.Spawn("roadstar", origin + new Vector3(-20f, 0.1f, -150f), Quaternion.identity);
                for (int i = 0; i < 40; i++) await Awaitable.NextFrameAsync();
                await Place(new Vector3(-24.5f, 0f, -150f), 90f);
                await EnterAndWait(star, "vehicles-11-roadstar-door");
                await Hold(2.5f, new VehicleInput { throttle = 1f }, star);
                var flight = star.GetComponent<FlightPhysics>();
                Mark("roadstar-takeoff");
                flight.SetFlight(true);
                float liftStart = star.transform.position.y;
                await Hold(1.2f, VehicleInput.None, star);
                await shot("vehicles-12-roadstar-liftoff", .02f);
                await Hold(3f, new VehicleInput { lift = 1f }, star);
                float climbed = star.transform.position.y - liftStart;
                float flyTop = 0f;
                await Hold(4f, new VehicleInput { throttle = 1f, lift = 0.2f }, star, _ => flyTop = Mathf.Max(flyTop, star.Speed * 3.6f));
                await shot("vehicles-13-roadstar-flying", .02f);
                await Hold(2f, new VehicleInput { throttle = 0.6f, steer = 1f }, star);
                Mark("roadstar-land");
                flight.SetFlight(false);
                float t0 = Time.time, landedIn = -1f;
                await Hold(9f, new VehicleInput { brake = 0.5f }, star, _ => { if (landedIn < 0f && !star.Flying) landedIn = Time.time - t0; });
                bool landed = !star.Flying;
                await shot("vehicles-14-roadstar-landed", .02f);
                o["roadstar"] = new JObject { ["climbedMetres"] = R(climbed), ["flyTopKmh"] = R(flyTop), ["landed"] = landed, ["landedInS"] = R(landedIn), ["fold"] = R(flight.Fold01) };
                expect(climbed > 15f && flyTop > 120f && landed, $"the Roadstar lifts off (+{climbed:F0} m), flies ({flyTop:F0} km/h) and lands");
                await ExitAndWait("vehicles-15-roadstar-out");
                Mark("heli");
                // ---------------------------------------------------------------- the helicopter
                var heli = sys.Spawn("heli", origin + new Vector3(35f, 0.2f, 0f), Quaternion.identity);
                for (int i = 0; i < 40; i++) await Awaitable.NextFrameAsync();
                await Place(new Vector3(31.5f, 0f, -2f), 90f);
                await EnterAndWait(heli, "vehicles-16-heli-board");
                var hf = heli.GetComponent<FlightPhysics>();
                await Hold(3.5f, VehicleInput.None, heli);   // rotor spin-up
                float hy0 = heli.transform.position.y;
                Mark("heli-takeoff");
                await Hold(5f, new VehicleInput { lift = 1f }, heli);
                await shot("vehicles-17-heli-takeoff", .02f);
                float hClimb = heli.transform.position.y - hy0;
                float heliTop = 0f;
                await Hold(6f, new VehicleInput { pitch = 1f }, heli, _ => heliTop = Mathf.Max(heliTop, heli.Speed * 3.6f));
                await shot("vehicles-18-heli-flying", .02f);
                await Hold(1.5f, new VehicleInput { yaw = 1f }, heli);
                float before = heli.Speed;
                await Hold(5f, VehicleInput.None, heli);   // hands off: it brakes itself into a hover
                float after = heli.Speed;
                Mark("heli-land");
                float descendStart = Time.time;
                await Hold(14f, new VehicleInput { lift = -0.8f }, heli);
                await shot("vehicles-19-heli-landed", .02f);
                o["heli"] = new JObject { ["climbedMetres"] = R(hClimb), ["topKmh"] = R(heliTop), ["hoverFrom"] = R(before), ["hoverTo"] = R(after), ["grounded"] = hf.Grounded };
                expect(hClimb > 20f && heliTop > 75f && after < before * 0.4f && hf.Grounded, $"helicopter: +{hClimb:F0} m, {heliTop:F0} km/h, hands-off hover {before:F1} -> {after:F1} m/s, landed");
                await ExitAndWait("vehicles-20-heli-out");
                Mark("drone");
                // ---------------------------------------------------------------- the drone
                bool launched = sys.DeployDrone();
                var drone = sys.Driving;
                await Hold(2.5f, new VehicleInput { lift = 1f }, drone);
                await Hold(3f, new VehicleInput { pitch = 1f, yaw = 0.3f }, drone);
                await shot("vehicles-21-drone", .02f);
                float droneTop = drone != null ? drone.Speed * 3.6f : 0f;
                sys.ExitOrLand();
                float landT0 = Time.time, droneBackIn = -1f;
                await Hold(9f, VehicleInput.None, null, _ => { if (droneBackIn < 0f && sys.Driving == null) droneBackIn = Time.time - landT0; });
                bool pocketed = drone != null && !drone.gameObject.activeSelf;
                o["drone"] = new JObject { ["launched"] = launched, ["kmh"] = R(droneTop), ["back"] = sys.Driving == null, ["backInS"] = R(droneBackIn), ["pocketed"] = pocketed };
                expect(launched && droneTop > 25f && sys.Driving == null && pocketed, $"the drone launches, flies ({droneTop:F0} km/h), lands and goes back in the pocket ({droneBackIn:F1} s)");
            }
            finally
            {
                VehicleControls.ScriptedInput = false;
                Time.captureFramerate = prevCapture;
                Object.Destroy(ground);
            }
            return visit;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>A 120 x 360 m proving ground with a crash wall at its north end.</summary>
        private static GameObject BuildGround(Vector3 o)
        {
            var root = new GameObject("vehicles-ground");
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = new Color(0.42f, 0.44f, 0.47f) };
            var line = new Material(mat) { color = new Color(0.95f, 0.85f, 0.35f) };
            void Box(string name, Vector3 min, Vector3 max, Material m)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name; go.transform.SetParent(root.transform, false);
                go.transform.position = o + (min + max) * 0.5f; go.transform.localScale = max - min;
                go.GetComponent<Renderer>().sharedMaterial = m;
            }
            Box("strip", new Vector3(-60f, -1f, -180f), new Vector3(60f, 0f, 180f), mat);
            for (int i = 0; i < 36; i++) Box("dash" + i, new Vector3(-0.15f, 0f, -180f + i * 10f), new Vector3(0.15f, 0.02f, -175f + i * 10f), line);
            Box("crash-wall", new Vector3(-12f, 0f, 150f), new Vector3(12f, 2.5f, 151f), mat);
            return root;
        }
    }
}
