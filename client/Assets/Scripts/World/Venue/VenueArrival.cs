using System;
using UnityEngine;
using UnityEngine.AI;

namespace CTLife.World
{
    /// <summary>
    /// Where the local player appears on entering a venue (Phase A fix, 2026-10-17). The registry "entrance" spot sits on the
    /// exit mat, inside the door's interaction range and at the bottom edge of the camera frame: arriving there put the door's
    /// "Leave" prompt over the character at once, and in dark venues players could not find themselves. The arrival point is
    /// the entrance stepped into the venue along its facing until it clears the door's range, snapped to the navmesh.
    /// The server's join placement is not used: it puts every joiner on the walkable rect's first corner (venue.ts onJoin,
    /// "B10"), never a real previous position; the client publishes the arrival point with its first move.
    /// </summary>
    public static class VenueArrival
    {
        public const float DoorClearance = 0.6f;     // beyond the door's range
        public const float MaxStep = 5f;

        /// <summary>Pure placement: entrance + facing * d, the smallest d (0.5 m steps) that is clear of every door point and
        /// accepted by <paramref name="valid"/>; the entrance itself when nothing qualifies.</summary>
        public static Vector3 Step(Vector3 entrance, Vector3 inward, Vector3[] doors, float doorRange, Func<Vector3, Vector3?> valid)
        {
            inward.y = 0f;
            if (inward.sqrMagnitude < 1e-6f) inward = Vector3.forward;
            inward.Normalize();
            for (float d = 0f; d <= MaxStep + 1e-3f; d += 0.5f)
            {
                var c = entrance + inward * d;
                var snapped = valid != null ? valid(c) : c;
                if (snapped == null) continue;
                bool clear = true;
                foreach (var door in doors)
                {
                    var flat = snapped.Value - door; flat.y = 0f;
                    if (flat.magnitude < doorRange + DoorClearance) { clear = false; break; }
                }
                if (clear) return snapped.Value;
            }
            var e = valid != null ? valid(entrance) : entrance;
            return e ?? entrance;
        }

        /// <summary>The arrival point and facing for <paramref name="venue"/> (world space).</summary>
        public static Vector3 For(VenueSceneRoot venue, out float rotYRad)
        {
            venue.TryGetSpot("entrance", out var entrance, out rotYRad);   // world yaw (anchors and registry spots alike)
            var inward = new Vector3(Mathf.Sin(rotYRad), 0f, Mathf.Cos(rotYRad));
            // Every exit spot is a door ("exit" opens the map; "entrance" shares the mat).
            var doors = new System.Collections.Generic.List<Vector3>();
            if (venue.TryGetSpot("exit", out var exit, out _)) doors.Add(exit);
            doors.Add(entrance);
            float range = InteractableCatalog.RangeFor(InteractionKind.Door);
            var point = Step(entrance, inward, doors.ToArray(), range, p =>
                NavMesh.SamplePosition(p, out var hit, 0.35f, NavMesh.AllAreas) ? hit.position : (Vector3?)null);
            rotYRad = Mathf.Atan2(inward.x, inward.z);
            return point;
        }
    }
}
