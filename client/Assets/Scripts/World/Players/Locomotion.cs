using System;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Direct-control locomotion curves (Phase A feel pass, 2026-10-17), measured with the FeelProbe before tuning:
    /// the old controller stopped dead in two frames (270 m/s^2), turned at a flat 720 deg/s from the first frame, and froze
    /// against the venue's walkable edge (128 of 181 frames stalled). Pure functions, EditMode-tested.
    /// </summary>
    public static class Locomotion
    {
        /// <summary>Speeding up (m/s^2): 0 to run in ~0.3 s, unchanged from the accepted start.</summary>
        public const float Acceleration = 14f;
        /// <summary>Slowing to a lower target while still holding the stick.</summary>
        public const float Deceleration = 18f;
        /// <summary>Release braking: friction-like, proportional plus a floor, so a run settles in ~0.22 s over ~0.5 m.</summary>
        public const float BrakeRate = 9f, BrakeFloor = 6f, StopBelow = 0.08f;
        public const float TurnSmoothTime = 0.055f, MaxTurnRate = 900f;

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
