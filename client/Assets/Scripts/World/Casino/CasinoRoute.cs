#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Dev builds only (R3, 2026-10-24): The Rug Room's capture route, launch extra <c>islandRoom=casino…</c>:
    /// - <c>casino</c>: the player 15 m out on the beach facing the casino (8 s, label rr_ext), then walks in through the door
    ///   (rr_walk) to the slot bank and stands at its prompt ("Play slots", logged), then a loop of the floor (rr_floor);
    /// - <c>casino_in</c>: straight onto the floor inside the entrance (the room streamed in first), then the floor loop;
    /// - <c>casino_play</c> / <c>casino_crash</c> / <c>casino_coinflip</c>: onto the floor, walk to that game's spot and press its
    ///   prompt, which opens the casino screen through CasinoHooks.Open.
    /// Every step logs "CTLIFE [casino-route] ..." for timing recordings against logcat.
    /// </summary>
    public static class CasinoRoute
    {
        private static void Mark(string what) => Debug.Log("CTLIFE [casino-route] " + what);
        private static string Fmt(Vector3 p) => $"({p.x:0.0}, {p.z:0.0})";

        /// <summary>Plaza (Ledger Street corner) to the beach east of the central beach path, island metres (x, z).</summary>
        private static readonly (float, float)[] PlazaToBeach =
        {
            (-49.7f, -43.4f), (-47.9f, -61f), (-45f, -88.5f), (-41.1f, -125.9f), (-37.1f, -163.5f), (-35.7f, -177.2f), (-20.3f, -200.3f),
            (3.7f, -199.7f), (27.8f, -199.8f), (51.8f, -200.4f), (54f, -214f), (56f, -230f), (75f, -236f), (95f, -238f), (110f, -237f),
        };

        public static async Task Run(VenueSession s, Func<bool> alive, CancellationToken ct, string mode)
        {
            PerfSegments.Ensure();
            var me = s.LocalPlayer;
            var rooms = IslandInteriors.Current;
            if (rooms == null || !rooms.TryGetRoomDoor(CasinoHooks.RoomId, out _, out var door, out var rot)) { Mark("no rug_room door"); return; }
            var fwd = rot * Vector3.forward;                                   // out of the casino, onto the beach
            float inYaw = (rot.eulerAngles.y + 180f) * Mathf.Deg2Rad;         // facing the door
            var outside = door + fwd * (mode == "casino" ? 15f : 6f) + rot * Vector3.right * (mode == "casino" ? -4f : 0f);
            await s.Island.EnsureCells(outside, 200f, ct);
            bool walkedIn = false;
            if (s.IsOnline)
            {
                // Online the server owns the street position, so get there like a player: walk. A SnapTo outruns the street
                // room's walking rule (its anchor trails behind and `corrected` snaps you back), and a street rejoin restores the
                // retained anchor (2026-10-25, device: "teleported downtown"). From the plaza spawn: Ledger Street south, Ocean
                // Drive east, the central beach path down to the sand, along the beach to the casino door (roads.json path).
                Mark("walking from the plaza at " + Fmt(me.transform.position));
                PerfSegments.Label = "rr_approach";
                var path = new List<Vector3>();
                foreach (var (x, z) in PlazaToBeach) path.Add(new Vector3(x, 0f, z));
                path.Add(outside);
                await Walk(me, alive, ct, path.ToArray());
                Mark("outside the casino at " + Fmt(me.transform.position));
                walkedIn = true;
            }
            else
                me.SnapTo(new Vector3(outside.x, IslandStreamer.GroundHeight(outside.x, outside.z) + 0.05f, outside.z),
                          Mathf.Atan2(door.x - outside.x, door.z - outside.z));
            CameraRig.Instance?.SettleBehind();
            for (int i = 0; i < 60 && alive() && (rooms.ActiveRoom == null || rooms.ActiveRoom.roomId != CasinoHooks.RoomId); i++)
                await Awaitable.WaitForSecondsAsync(0.25f, ct);
            var room = rooms.ActiveRoom;
            if (room == null || room.roomId != CasinoHooks.RoomId) { Mark("room not streamed in"); return; }
            var t = room.transform;
            if (mode == "casino")
            {
                await Awaitable.WaitForSecondsAsync(2f, ct);
                PerfSegments.Label = "rr_ext"; Mark("exterior");
                await Awaitable.WaitForSecondsAsync(8f, ct);
                PerfSegments.Label = "rr_walk"; Mark("walk in");
                await Walk(me, alive, ct, door + fwd * 1.2f, door - fwd * 0.6f, t.TransformPoint(new Vector3(0.4f, 0f, -2.5f)));
            }
            else if (walkedIn)
            {
                await Walk(me, alive, ct, door + fwd * 1.2f, door - fwd * 0.6f, t.TransformPoint(new Vector3(0.3f, 0f, -2.2f)));
                Mark("on the floor (walked in) at " + Fmt(me.transform.position));
            }
            else
            {
                var p = t.TransformPoint(new Vector3(0.3f, 0f, -2.2f));
                me.SnapTo(new Vector3(p.x, t.position.y + 0.05f, p.z), inYaw);
                CameraRig.Instance?.SettleBehind();
                await Awaitable.WaitForSecondsAsync(2.5f, ct);
                Mark("on the floor");
            }
            string game = mode == "casino_crash" ? CasinoHooks.Crash : mode == "casino_coinflip" ? CasinoHooks.Coinflip : CasinoHooks.Slots;
            InteriorRoom.Spot spot = default;
            foreach (var sp in room.spots) if (sp.game == game) spot = sp;
            if (mode == "casino" || mode == "casino_play" || mode == "casino_crash" || mode == "casino_coinflip")
            {
                PerfSegments.Label = "rr_walk";
                await Walk(me, alive, ct, t.TransformPoint(spot.position));
                await Awaitable.WaitForSecondsAsync(1.2f, ct);
                var ic = InteractionController.Current;
                var offer = ic != null ? ic.ExtOffer : null;
                Mark($"at {game}: offered {(offer != null ? offer.id + " (" + offer.verbKey + ")" : "nothing")}");
                if (mode != "casino" && offer != null) { ic.Perform("ext", "ext:" + offer.id); Mark("pressed " + offer.id); }
                PerfSegments.Label = null;
                if (mode != "casino") { await Watch(me, alive, ct); return; }
                await Awaitable.WaitForSecondsAsync(3f, ct);
            }
            // a loop of the floor: the slot bank, the coinflip table, the back tables, the screen, the entrance
            PerfSegments.Label = "rr_floor"; Mark("floor loop");
            await Walk(me, alive, ct, t.TransformPoint(new Vector3(3.6f, 0f, -6f)), t.TransformPoint(new Vector3(1.6f, 0f, -11f)),
                       t.TransformPoint(new Vector3(-1.6f, 0f, -13.6f)), t.TransformPoint(new Vector3(-3.8f, 0f, -9f)),
                       t.TransformPoint(new Vector3(-3.2f, 0f, -4f)), t.TransformPoint(new Vector3(0.3f, 0f, -2.2f)));
            PerfSegments.Label = null;
            Mark("done");
            await Watch(me, alive, ct);
        }

        /// <summary>30 s of positions after the route (a server correction would show as a jump), inside or not.</summary>
        private static async Task Watch(LocalPlayerController me, Func<bool> alive, CancellationToken ct)
        {
            for (int i = 0; i < 10 && alive(); i++)
            {
                await Awaitable.WaitForSecondsAsync(3f, ct);
                var ii = IslandInteriors.Current;
                Mark($"watch {Fmt(me.transform.position)} inside {(ii != null && ii.PlayerInside)}");
            }
        }

        private static async Task Walk(LocalPlayerController me, Func<bool> alive, CancellationToken ct, params Vector3[] path)
        {
            foreach (var target in path)
            {
                float t0 = Time.realtimeSinceStartup;
                while (alive() && Time.realtimeSinceStartup - t0 < 25f)
                {
                    var d = target - me.transform.position; d.y = 0f;
                    if (d.magnitude < 0.4f) break;
                    var cam = Camera.main;
                    float cy = cam != null ? cam.transform.eulerAngles.y : 0f;
                    var local = Quaternion.Euler(0f, -cy, 0f) * d.normalized;
                    me.SetMoveInput(new Vector2(local.x, local.z) * 0.62f);
                    await Awaitable.NextFrameAsync(ct);
                }
            }
            me.SetMoveInput(Vector2.zero);
        }
    }
}
#endif
