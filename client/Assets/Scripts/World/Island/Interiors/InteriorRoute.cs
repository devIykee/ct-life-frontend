#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Dev builds only (World v3 districts, 2026-10-23): launch extra <c>islandRoom=1</c>. Stands the player at eye level in
    /// The Trenches, Residences and Dubai Marina (10 s each, a PerfSegments label per spot), then 9 m in front of the first
    /// walk-in door, waits for its room, walks in on the stick (through the door, down the hall, to the sofa), presses the
    /// "Sit" prompt and holds 10 s seated (labels room_walk, room_sit). Each step logs "CTLIFE [room-route] ..." so device
    /// screenshots and screen recordings can be timed against logcat.
    /// </summary>
    public static class InteriorRoute
    {
        private static readonly (string name, Vector3 at, float yaw)[] Spots =
        {
            ("trenches", new Vector3(-332.5f, 0f, -279.0f), 101f),
            ("residences", new Vector3(-493.6f, 0f, 50.7f), 3f),
            ("marina", new Vector3(690f, 0f, -335f), -12f),
        };

        private static void Mark(string what) => Debug.Log("CTLIFE [room-route] " + what);

        public static async Task Run(VenueSession s, Func<bool> alive, CancellationToken ct, bool districts = true)
        {
            PerfSegments.Ensure();
            var me = s.LocalPlayer;
            if (districts)
                foreach (var (name, at, yaw) in Spots)
                {
                    float y = IslandStreamer.GroundHeight(at.x, at.z);
                    await s.Island.EnsureCells(at, 200f, ct);
                    me.SnapTo(new Vector3(at.x, IslandStreamer.GroundHeight(at.x, at.z) + 0.05f, at.z), yaw * Mathf.Deg2Rad);
                    CameraRig.Instance?.SettleBehind();
                    await Awaitable.WaitForSecondsAsync(3f, ct);
                    PerfSegments.Label = name;
                    Mark("shot " + name);
                    await Awaitable.WaitForSecondsAsync(10f, ct);
                    PerfSegments.Label = null;
                }
            var rooms = IslandInteriors.Current;
            if (rooms == null || !rooms.TryGetDoor(0, out var door, out var rot)) { Mark("no walk-in door"); return; }
            var fwd = rot * Vector3.forward;                           // out of the building
            var start = door + fwd * 9f;
            await s.Island.EnsureCells(start, 200f, ct);
            me.SnapTo(new Vector3(start.x, IslandStreamer.GroundHeight(start.x, start.z) + 0.05f, start.z), (rot.eulerAngles.y + 180f) * Mathf.Deg2Rad);
            CameraRig.Instance?.SettleBehind();
            for (int i = 0; i < 40 && alive() && (rooms.ActiveRoom == null || !rooms.ActiveRoom.gameObject.activeSelf); i++)
                await Awaitable.WaitForSecondsAsync(0.25f, ct);
            await Awaitable.WaitForSecondsAsync(3f, ct);
            Mark($"walk start (room {(rooms.ActiveRoom != null ? rooms.ActiveRoom.roomId : "none")})");
            PerfSegments.Label = "room_walk";
            var room = rooms.ActiveRoom;
            if (room == null) { Mark("room not streamed in"); return; }
            var t = room.transform;
            // through the door, down the hall, round the back of the L-sofa (the coffee table closes its front) to its end;
            // the source model's coordinates (world_v3_interior.py's room space)
            Vector3 P(float x, float y) => t.TransformPoint(room.FromSource(x, y, 0f));
            Vector3[] path = { door + fwd * 1.0f, P(0.2f, 1.2f), P(0.25f, 3.85f), P(3.3f, 3.9f), P(3.25f, 5.0f) };
            foreach (var target in path)
            {
                float t0 = Time.realtimeSinceStartup;
                while (alive() && Time.realtimeSinceStartup - t0 < 8f)
                {
                    var d = target - me.transform.position; d.y = 0f;
                    if (d.magnitude < 0.35f) break;
                    var cam = Camera.main;
                    float cy = cam != null ? cam.transform.eulerAngles.y : 0f;
                    var local = Quaternion.Euler(0f, -cy, 0f) * d.normalized;
                    me.SetMoveInput(new Vector2(local.x, local.z) * 0.62f);
                    await Awaitable.NextFrameAsync(ct);
                }
            }
            me.SetMoveInput(Vector2.zero);
            Mark("at the sofa");
            await Awaitable.WaitForSecondsAsync(1.2f, ct);
            var ic = InteractionController.Current;
            Mark($"offered {(ic != null && ic.Offered != null ? ic.Offered.id + " " + ic.Offered.kind : "nothing")}");
            if (ic != null && ic.Offered != null) ic.Perform("use");
            PerfSegments.Label = "room_sit";
            await Awaitable.WaitForSecondsAsync(10f, ct);
            PerfSegments.Label = null;
            Mark("done");
        }
    }
}
#endif
