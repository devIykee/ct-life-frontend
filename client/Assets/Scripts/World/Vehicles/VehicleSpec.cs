using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    public enum VehicleKind { Car, Scooter, Roadstar, Helicopter, Drone }

    /// <summary>One frame of driver input, from the HUD (or a test): all axes -1..1 / 0..1, already dead-zoned.</summary>
    public struct VehicleInput
    {
        /// <summary>Ground: steer left/right. Flight: roll (right stick x).</summary>
        public float steer;
        public float throttle, brake;
        public bool handbrake;
        /// <summary>Flight: climb/descend (left stick y), yaw (left stick x), pitch (right stick y, + = nose down/forward).</summary>
        public float lift, yaw, pitch;
        /// <summary>Simple flight: camera-relative horizontal intent, already transformed to world space.</summary>
        public bool simpleFlight;
        public Vector3 move;
        public float heading;

        public static VehicleInput None => default;
    }

    /// <summary>
    /// Handling and presentation numbers per vehicle (Feel lane F2). Real-size bodies; arcade tyres (grip cancels sideways
    /// slip within a few frames up to mu * load), so cars feel planted until the handbrake breaks the rear loose.
    /// </summary>
    public sealed class VehicleSpec
    {
        public string id, nameKey;
        public VehicleKind kind;
        /// <summary>World v2 parking classes (compact | suv | sports | moped | roadstar).</summary>
        public string spawnClass;
        public float mass = 1200f;
        public Vector3 centerOfMass = new Vector3(0f, 0.42f, 0.05f);
        // ground
        public float topSpeed = 34f, reverseSpeed = 8f, driveForce = 7800f, brakeForce = 11000f;
        public float grip = 1.15f, rearGripHandbrake = 0.32f, maxSteer = 34f, highSpeedSteer = 9f;
        public bool frontDrive, rearDrive = true;
        public float suspensionRest = 0.30f, suspensionHz = 1.7f, suspensionDamping = 0.36f, antiRoll = 9000f, downforce = 1.4f;
        public float leanMax;                 // scooters lean into turns (visual)
        // flight
        public float flightTopSpeed = 55f, climbRate = 12f, maxTilt = 28f, yawRate = 75f, hoverHeight;
        // camera
        public float camDistance = 6.2f, camHeight = 1.9f, camPitch = 9f;
        // audio set (Resources/Vehicles/Audio/<prefix>_*.wav)
        public string engineSound = "engine_car", startSound = "engine_start";
        public float enginePitchIdle = 0.75f, enginePitchMax = 1.9f;
        // paint (v3 models: the Paint material's default tint and clear coat; traffic recolours it, VehiclePaint)
        public Color paint = new Color(0.62f, 0.64f, 0.66f);
        public float paintMetallic = 0.45f, paintSmoothness = 0.8f;
    }

    public static class VehicleCatalog
    {
        public static readonly Dictionary<string, VehicleSpec> Specs = new Dictionary<string, VehicleSpec>
        {
            ["compact"] = new VehicleSpec
            {
                id = "compact", nameKey = "vehicle.compact", kind = VehicleKind.Car, spawnClass = "compact", mass = 1150f,
                topSpeed = 36f, driveForce = 6900f, frontDrive = true, rearDrive = false, grip = 1.12f, camDistance = 6.0f,
                paint = new Color(0.06f, 0.16f, 0.42f),
            },
            ["sedan"] = new VehicleSpec
            {
                id = "sedan", nameKey = "vehicle.sedan", kind = VehicleKind.Car, spawnClass = "compact", mass = 1420f,
                centerOfMass = new Vector3(0f, 0.44f, 0.08f), topSpeed = 40f, driveForce = 8400f, brakeForce = 12500f,
                frontDrive = true, rearDrive = false, grip = 1.12f, suspensionHz = 1.6f, antiRoll = 10000f, camDistance = 6.4f,
                paint = new Color(0.62f, 0.64f, 0.66f), enginePitchIdle = 0.72f, enginePitchMax = 1.8f,
            },
            ["taxi"] = new VehicleSpec
            {
                id = "taxi", nameKey = "vehicle.taxi", kind = VehicleKind.Car, spawnClass = "compact", mass = 1600f,
                centerOfMass = new Vector3(0f, 0.46f, 0.08f), topSpeed = 38f, driveForce = 9200f, brakeForce = 13500f,
                grip = 1.1f, suspensionHz = 1.5f, antiRoll = 10500f, camDistance = 6.8f, camHeight = 2.0f,
                enginePitchIdle = 0.68f, enginePitchMax = 1.7f,
            },
            ["police"] = new VehicleSpec
            {
                id = "police", nameKey = "vehicle.police", kind = VehicleKind.Car, spawnClass = "compact", mass = 1750f,
                centerOfMass = new Vector3(0f, 0.44f, 0.05f), topSpeed = 48f, driveForce = 12500f, brakeForce = 16000f,
                frontDrive = false, rearDrive = true, grip = 1.2f, suspensionHz = 1.8f, antiRoll = 13000f, camDistance = 6.8f,
                camHeight = 2.0f, enginePitchIdle = 0.7f, enginePitchMax = 1.9f,
            },
            ["suv"] = new VehicleSpec
            {
                id = "suv", nameKey = "vehicle.suv", kind = VehicleKind.Car, spawnClass = "suv", mass = 1750f,
                centerOfMass = new Vector3(0f, 0.55f, 0.05f), topSpeed = 37f, driveForce = 10800f, brakeForce = 15500f,
                frontDrive = true, rearDrive = true, grip = 1.08f, suspensionRest = 0.36f, suspensionHz = 1.5f, antiRoll = 14000f,
                camDistance = 6.9f, camHeight = 2.2f, enginePitchIdle = 0.65f, enginePitchMax = 1.6f,
            },
            // v3 batch 2 (2026-10-22): Lightbody '90 pickup and Shvan '92 van (parked in SUV bays), the cafe racer bike
            ["pickup"] = new VehicleSpec
            {
                id = "pickup", nameKey = "vehicle.pickup", kind = VehicleKind.Car, spawnClass = "suv", mass = 1900f,
                centerOfMass = new Vector3(0f, 0.58f, 0.15f), topSpeed = 36f, driveForce = 11200f, brakeForce = 16000f,
                frontDrive = false, rearDrive = true, grip = 1.06f, suspensionRest = 0.36f, suspensionHz = 1.45f, antiRoll = 13000f,
                camDistance = 7.4f, camHeight = 2.3f, enginePitchIdle = 0.6f, enginePitchMax = 1.55f,
            },
            ["van"] = new VehicleSpec
            {
                id = "van", nameKey = "vehicle.van", kind = VehicleKind.Car, spawnClass = "suv", mass = 2100f,
                centerOfMass = new Vector3(0f, 0.62f, 0.05f), topSpeed = 33f, driveForce = 11500f, brakeForce = 16500f,
                frontDrive = true, rearDrive = false, grip = 1.04f, maxSteer = 33f, suspensionRest = 0.34f, suspensionHz = 1.4f,
                antiRoll = 15000f, camDistance = 7.6f, camHeight = 2.6f, enginePitchIdle = 0.62f, enginePitchMax = 1.5f,
            },
            ["bike"] = new VehicleSpec
            {
                id = "bike", nameKey = "vehicle.bike", kind = VehicleKind.Scooter, spawnClass = "moped", mass = 180f,
                centerOfMass = new Vector3(0f, 0.42f, 0f), topSpeed = 30f, reverseSpeed = 3f, driveForce = 1650f, brakeForce = 2300f,
                frontDrive = false, rearDrive = true, grip = 1.2f, maxSteer = 28f, highSpeedSteer = 8f, suspensionRest = 0.18f,
                suspensionHz = 2.3f, antiRoll = 4500f, downforce = 0.35f, leanMax = 30f, camDistance = 4.6f, camHeight = 1.6f,
                engineSound = "engine_scooter", startSound = "scooter_start", enginePitchIdle = 0.7f, enginePitchMax = 2.1f,
            },
            ["sports"] = new VehicleSpec
            {
                id = "sports", nameKey = "vehicle.sports", kind = VehicleKind.Car, spawnClass = "sports", mass = 1300f,
                centerOfMass = new Vector3(0f, 0.36f, 0f), topSpeed = 52f, driveForce = 11500f, brakeForce = 14000f,
                grip = 1.25f, maxSteer = 32f, highSpeedSteer = 7f, suspensionRest = 0.26f, suspensionHz = 2.1f, antiRoll = 12000f,
                downforce = 2.2f, camDistance = 6.3f, camHeight = 1.7f, enginePitchIdle = 0.85f, enginePitchMax = 2.2f,
                paint = new Color(0.62f, 0.05f, 0.06f), paintMetallic = 0.5f, paintSmoothness = 0.84f,
            },
            ["roadstar"] = new VehicleSpec
            {
                id = "roadstar", nameKey = "vehicle.roadstar", kind = VehicleKind.Roadstar, spawnClass = "roadstar", mass = 1350f,
                centerOfMass = new Vector3(0f, 0.36f, 0f), topSpeed = 58f, driveForce = 13500f, brakeForce = 14500f,
                grip = 1.25f, maxSteer = 32f, highSpeedSteer = 7f, suspensionRest = 0.26f, suspensionHz = 2.0f, antiRoll = 12000f,
                downforce = 2.0f, flightTopSpeed = 62f, climbRate = 14f, maxTilt = 26f, yawRate = 70f, hoverHeight = 1.6f,
                camDistance = 6.6f, camHeight = 1.8f, engineSound = "engine_ev", startSound = null, enginePitchIdle = 0.8f, enginePitchMax = 1.7f,
                // the v3 hovercar's look: deep metallic blue with a clear coat (2026-10-22: the gunmetal still read as one
                // black shape from the chase camera; the island now has a real HDRI sky for the paint to reflect)
                paint = new Color(0.20f, 0.36f, 0.66f), paintMetallic = 0.7f, paintSmoothness = 0.88f,
            },
            ["scooter"] = new VehicleSpec
            {
                id = "scooter", nameKey = "vehicle.scooter", kind = VehicleKind.Scooter, spawnClass = "moped", mass = 190f,
                centerOfMass = new Vector3(0f, 0.38f, 0.05f), topSpeed = 19f, reverseSpeed = 3f, driveForce = 1150f, brakeForce = 1900f,
                frontDrive = false, rearDrive = true, grip = 1.15f, maxSteer = 30f, highSpeedSteer = 10f, suspensionRest = 0.18f,
                suspensionHz = 2.2f, antiRoll = 4500f, downforce = 0.3f, leanMax = 24f, camDistance = 4.4f, camHeight = 1.6f,
                engineSound = "engine_scooter", startSound = "scooter_start", enginePitchIdle = 0.8f, enginePitchMax = 2.0f,
            },
            ["heli"] = new VehicleSpec
            {
                id = "heli", nameKey = "vehicle.heli", kind = VehicleKind.Helicopter, spawnClass = "heli", mass = 1450f,
                centerOfMass = new Vector3(0f, 0.9f, 0f), flightTopSpeed = 55f, climbRate = 10f, maxTilt = 30f, yawRate = 65f,
                camDistance = 14f, camHeight = 5f, camPitch = 12f, engineSound = "rotor_heli", startSound = null,
                enginePitchIdle = 0.55f, enginePitchMax = 1.15f,
            },
            ["drone"] = new VehicleSpec
            {
                id = "drone", nameKey = "vehicle.drone", kind = VehicleKind.Drone, spawnClass = "drone", mass = 1.6f,
                centerOfMass = Vector3.zero, flightTopSpeed = 17f, climbRate = 6f, maxTilt = 32f, yawRate = 120f,
                camDistance = 2.4f, camHeight = 0.55f, camPitch = 8f, engineSound = "rotor_drone", startSound = null,
                enginePitchIdle = 0.8f, enginePitchMax = 1.45f,
            },
        };

        public static VehicleSpec Get(string id) => id != null && Specs.TryGetValue(id, out var s) ? s : null;

        public static bool Flies(VehicleKind k) => k == VehicleKind.Helicopter || k == VehicleKind.Drone;
    }
}
