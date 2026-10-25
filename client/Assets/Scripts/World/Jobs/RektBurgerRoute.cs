#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Dev builds only (2026-10-25): Rekt Burger's capture route, launch extra <c>islandRoom=rekt…</c>:
    /// - <c>rekt</c>: arrive in front of the restaurant on the Coast Highway (exterior, 8 s, label rb_ext), walk in through the
    ///   door (rb_walk) to the counter and stand at its prompt (logged), then a loop of the dining room (rb_floor) and a 30 s
    ///   position watch;
    /// - <c>rekt_in</c>: arrive, walk straight in, the dining loop, the watch;
    /// - <c>rekt_shift</c>: arrive, walk to the counter and press its prompt (RektBurgerHooks.Open("shift")), then 60 s of
    ///   the clock-in loop (rb_shift: the avatar at the stations, the customers; clocks in locally when offline).
    /// Online the player WALKS from the plaza spawn along the roads (Consensus Boulevard, River Road, Coast Highway): a SnapTo
    /// outruns the street room's walking rule and the server would correct the position back (see CasinoRoute). Offline it
    /// snaps. Every step logs "CTLIFE [rekt-route] ...".
    /// </summary>
    public static class RektBurgerRoute
    {
        private static void Mark(string what) => Debug.Log("CTLIFE [rekt-route] " + what);
        private static string Fmt(Vector3 p) => $"({p.x:0.0}, {p.z:0.0})";

        /// <summary>The plaza (Ledger Street corner) to the Coast Highway in front of Rekt Burger (roads.json path), island m.</summary>
        private static readonly (float, float)[] PlazaToTrenches =
        {
            (-49.7f, -43.4f), (-69.7f, -61.3f), (-90.8f, -80.2f), (-101.3f, -89.6f), (-112.3f, -105.8f), (-113.5f, -120.0f), (-130.3f, -126.1f),
            (-144.6f, -128.5f), (-156.3f, -139.0f), (-168.1f, -149.5f), (-188.3f, -179.4f), (-186.8f, -193.3f), (-185.3f, -207.3f),
            (-195.9f, -233.7f), (-221.7f, -245.0f), (-247.4f, -256.2f), (-273.0f, -267.9f), (-297.9f, -280.8f), (-306.5f, -286.5f),
        };

        /// <summary>The dining loop in the room's source frame (x right seen from the door, y in): the aisles between the table
        /// columns, along the storefront, past the booths and back down the middle (RektBurgerCheck walks the same points).</summary>
        public static readonly (float, float)[] Loop = { (2.05f, 5.6f), (2.05f, 0.95f), (-1.6f, 0.95f), (-1.6f, 4.9f), (-0.3f, 5.3f), (-0.3f, 1.0f) };

        public static async Task Run(VenueSession s, Func<bool> alive, CancellationToken ct, string mode)
        {
            PerfSegments.Ensure();
            var me = s.LocalPlayer;
            var rooms = IslandInteriors.Current;
            if (rooms == null || !rooms.TryGetRoomDoor(RektBurgerHooks.RoomId, out _, out var door, out var rot)) { Mark("no rekt_burger door"); return; }
            var fwd = rot * Vector3.forward;                                   // out of the restaurant, towards the highway
            var outside = door + fwd * (mode == "rekt" ? 12f : 5f) + rot * Vector3.right * (mode == "rekt" ? 3f : 0f);
            await s.Island.EnsureCells(outside, 200f, ct);
            if (s.IsOnline)
            {
                Mark("walking from the plaza at " + Fmt(me.transform.position));
                PerfSegments.Label = "rb_approach";
                var path = new List<Vector3>();
                foreach (var (x, z) in PlazaToTrenches) path.Add(new Vector3(x, 0f, z));
                path.Add(outside);
                await Walk(me, alive, ct, path.ToArray());
                Mark("outside Rekt Burger at " + Fmt(me.transform.position));
            }
            else
                me.SnapTo(new Vector3(outside.x, IslandStreamer.GroundHeight(outside.x, outside.z) + 0.05f, outside.z),
                          Mathf.Atan2(door.x - outside.x, door.z - outside.z));
            CameraRig.Instance?.SettleBehind();
            for (int i = 0; i < 60 && alive() && (rooms.ActiveRoom == null || rooms.ActiveRoom.roomId != RektBurgerHooks.RoomId); i++)
                await Awaitable.WaitForSecondsAsync(0.25f, ct);
            var room = rooms.ActiveRoom;
            if (room == null || room.roomId != RektBurgerHooks.RoomId) { Mark("room not streamed in"); return; }
            var t = room.transform;
            Vector3 P(float bx, float by) => t.TransformPoint(room.FromSource(bx, by, 0f));
            if (mode == "rekt")
            {
                await Awaitable.WaitForSecondsAsync(2f, ct);
                PerfSegments.Label = "rb_ext"; Mark("exterior");
                await Awaitable.WaitForSecondsAsync(8f, ct);
            }
            PerfSegments.Label = "rb_walk"; Mark("walk in");
            await Walk(me, alive, ct, door + fwd * 1.2f, door - fwd * 0.6f, P(0.3f, 1.6f));
            Mark("inside at " + Fmt(me.transform.position));
            if (mode == "rekt" || mode == "rekt_shift")
            {
                InteriorRoom.Spot spot = default;
                foreach (var sp in room.spots) if (sp.game == RektBurgerHooks.Shift) spot = sp;
                await Walk(me, alive, ct, t.TransformPoint(spot.position));
                await Awaitable.WaitForSecondsAsync(1.2f, ct);
                var ic = InteractionController.Current;
                var offer = ic != null ? ic.ExtOffer : null;
                Mark($"at the counter: offered {(offer != null ? offer.id + " (" + offer.verbKey + ")" : "nothing")}");
                // rekt_shift holds at the prompt for 4 s so the Clock in prompt can be captured before the press
                if (mode == "rekt_shift") { await Awaitable.WaitForSecondsAsync(4f, ct); offer = ic != null ? ic.ExtOffer : null; }
                if (mode == "rekt_shift" && offer != null) { ic.Perform("ext", "ext:" + offer.id); Mark("pressed " + offer.id); }
                PerfSegments.Label = null;
                if (mode == "rekt_shift") { await Shift(me, alive, ct, room); return; }
                await Awaitable.WaitForSecondsAsync(3f, ct);
            }
            // a loop of the dining room: the tables, the booths, back to the door
            PerfSegments.Label = "rb_floor"; Mark("floor loop");
            var loop = new Vector3[Loop.Length];
            for (int i = 0; i < Loop.Length; i++) loop[i] = P(Loop[i].Item1, Loop[i].Item2);
            await Walk(me, alive, ct, loop);
            PerfSegments.Label = null;
            Mark("done");
            await Watch(me, alive, ct);
        }

        /// <summary>The clock-in loop: City Life sets OnShift from the shift it starts; with no City Life answer (offline,
        /// mock) the route clocks in itself, then watches the work loop and the customers for 60 s (label rb_shift).</summary>
        private static async Task Shift(LocalPlayerController me, Func<bool> alive, CancellationToken ct, InteriorRoom room)
        {
            for (int i = 0; i < 12 && alive() && !RektBurgerHooks.OnShift; i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
            if (!RektBurgerHooks.OnShift) { Mark("no shift from City Life: clocking in locally (dev)"); RektBurgerHooks.SetOnShift(true); }
            PerfSegments.Label = "rb_shift";
            var work = room.GetComponent<RektBurgerWork>();
            var people = room.GetComponent<RektBurgerCustomers>();
            for (int i = 0; i < 30 && alive(); i++)
            {
                await Awaitable.WaitForSecondsAsync(3f, ct);
                Mark($"shift {Fmt(me.transform.position)} station {(work != null && work.Current >= 0 ? RektBurgerWork.Stations[work.Current].id : "-")} " +
                     $"worked {(work != null ? work.Worked : 0)} customers {(people != null ? people.Active : 0)} served {(people != null ? people.Served : 0)}");
            }
            PerfSegments.Label = null;
            Mark("done");
        }

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
