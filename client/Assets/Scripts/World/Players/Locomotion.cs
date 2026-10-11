using System;
using CTLife.Core;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Direct-control locomotion curves. Phase A (2026-10-17) measured the old controller with the FeelProbe: it stopped dead
    /// in two frames (270 m/s^2), turned at a flat 720 deg/s from the first frame, and froze against the venue's walkable
    /// edge. Feel lane F1 retuned them for human speeds (walk 1.6, jog 3.5, sprint 6 m/s) with a little weight: a jog builds
    /// in ~0.4 s, a sprint in another ~0.3 s, a sprint coasts ~1 m to a stop, and fast turns are wider. Pure functions,
    /// EditMode-tested.
    /// </summary>
    public static class Locomotion
    {
        /// <summary>Speeding up (m/s^2).</summary>
        public const float Acceleration = 9f;
        /// <summary>Slowing to a lower target while still holding the stick.</summary>
        public const float Deceleration = 12f;
        /// <summary>Release braking: friction-like, proportional plus a floor.</summary>
        public const float BrakeRate = 5f, BrakeFloor = 4f, StopBelow = 0.08f;
        public const float TurnSmoothTime = 0.08f, MaxTurnRate = 720f, SprintTurnRate = 400f;

        /// <summary>Turn-rate cap at a speed: full at a jog or slower, wider arcs at a sprint.</summary>
        public static float TurnRateAt(float speed) =>
            Mathf.Lerp(MaxTurnRate, SprintTurnRate, Mathf.InverseLerp(DirectMovementInput.JogSpeed, DirectMovementInput.SprintSpeed, speed));

        public static float Accelerate(float speed, float target, float dt) =>
            Mathf.MoveTowards(speed, target, (target > speed ? Acceleration : Deceleration) * dt);

        public static float Brake(float speed, float dt)
        {
            float next = speed - (speed * BrakeRate + BrakeFloor) * dt;
            return next < 0f ? 0f : next;
        }

        /// <summary>
        /// At a blocked edge: try the velocity's two axis components (an axis-aligned walkable rect's tangent) and keep the
        /// larger one that is allowed; zero when both are blocked (a corner).
        /// </summary>
        public static Vector3 SlideAlongEdge(Vector3 velocity, Func<Vector3, bool> allowed)
        {
            var x = new Vector3(velocity.x, 0f, 0f); var z = new Vector3(0f, 0f, velocity.z);
            bool okX = x.sqrMagnitude > 1e-6f && allowed(x), okZ = z.sqrMagnitude > 1e-6f && allowed(z);
            if (okX && okZ) return x.sqrMagnitude >= z.sqrMagnitude ? x : z;
            if (okX) return x;
            if (okZ) return z;
            return Vector3.zero;
        }
    }
}
