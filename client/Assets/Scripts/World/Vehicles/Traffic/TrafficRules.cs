using CTLife.World.Roads;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// How AI drivers pick a speed (Feel lane F3), pure and tested: the Intelligent Driver Model (Treiber et al.) for
    /// following and stopping smoothly behind anything ahead (a car, the player, a red light's stop line), a bend speed from
    /// the road's curvature, and the amber-light dilemma.
    /// </summary>
    public static class TrafficRules
    {
        public const float MaxAccel = 2.2f;       // m/s^2, an unhurried city start
        public const float ComfortBrake = 3.2f;   // m/s^2
        public const float MaxBrake = 8.5f;       // m/s^2, an emergency stop
        public const float MinGap = 2.4f;         // m, bumper to bumper when stopped
        public const float Headway = 1.25f;       // s
        public const float LateralAccel = 3.0f;   // m/s^2 in bends

        /// <summary>
        /// IDM acceleration at speed <paramref name="v"/> toward <paramref name="v0"/>, with something
        /// <paramref name="gap"/> metres ahead (bumper to bumper) moving at <paramref name="vLead"/>; infinity = clear road.
        /// </summary>
        public static float Idm(float v, float v0, float gap, float vLead)
        {
            v = Mathf.Max(0f, v);
            v0 = Mathf.Max(0.5f, v0);
            float r = v / v0;
            float free = 1f - r * r * r * r;
            float interaction = 0f;
            if (!float.IsInfinity(gap) && !float.IsNaN(gap))
            {
                float dv = v - vLead;
                float wanted = MinGap + Mathf.Max(0f, v * Headway + v * dv / (2f * Mathf.Sqrt(MaxAccel * ComfortBrake)));
                float g = Mathf.Max(0.05f, gap);
                interaction = (wanted / g) * (wanted / g);
            }
            return Mathf.Clamp(MaxAccel * (free - interaction), -MaxBrake, MaxAccel);
        }

        /// <summary>The speed a bend of curvature <paramref name="kappa"/> (1/m) allows.</summary>
        public static float CurveSpeed(float kappa) => Mathf.Abs(kappa) < 1e-4f ? 99f : Mathf.Sqrt(LateralAccel / Mathf.Abs(kappa));

        /// <summary>The fastest speed now that still lets the car slow to <paramref name="vAt"/> in <paramref name="distance"/> metres.</summary>
        public static float ApproachSpeed(float vAt, float distance) => Mathf.Sqrt(vAt * vAt + 2f * ComfortBrake * Mathf.Max(0f, distance));

        /// <summary>Signed curvature through three points in the x/z plane (1/m, + = a right-hand bend).</summary>
        public static float Curvature(Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = new Vector2(b.x - a.x, b.z - a.z);
            var bc = new Vector2(c.x - b.x, c.z - b.z);
            var ac = new Vector2(c.x - a.x, c.z - a.z);
            float cross = ab.x * bc.y - ab.y * bc.x;
            float den = ab.magnitude * bc.magnitude * ac.magnitude;
            if (den < 1e-6f) return 0f;
            return -2f * cross / den;   // left-handed (Unity, y up): a clockwise turn seen from above is to the right
        }

        /// <summary>
        /// Should a car stop at the line for this light? Red: yes. Amber: only if it can stop comfortably before the line
        /// (otherwise it is too late and it goes). Green: no.
        /// </summary>
        public static bool StopForSignal(SignalState s, float distanceToLine, float v)
        {
            if (s == SignalState.Red) return true;
            if (s == SignalState.Amber) return v * v / (2f * ComfortBrake) < distanceToLine + 0.5f;
            return false;
        }
    }
}
