#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// Dev builds only (lane B, 2026-10-26, motion + camera quality pass): launch extra <c>motion=1</c> drives the local player
    /// through the locomotion set with the thumb input a player uses (walk, jog, sprint, stops, turns in place, a circle, a
    /// 180 reversal), then walks into Rekt Burger and clocks in so the counter's work loop plays. A small label in the top
    /// left names each segment in the screen recording, so frames can be cut per segment ("CTLIFE [bp-route] seg …" too).
    /// </summary>
    public static class MotionRoute
    {
        private static Label _label;
        private static void Mark(string what) => Debug.Log("CTLIFE [bp-route] " + what);

        public static async Task Run(VenueSession s, Func<bool> alive, CancellationToken ct)
        {
            _label = new Label("") { pickingMode = PickingMode.Ignore };
            _label.style.position = Position.Absolute; _label.style.left = 20; _label.style.top = 150;
            _label.style.fontSize = 26; _label.style.color = Color.white; _label.style.backgroundColor = new Color(0, 0, 0, 0.6f);
            _label.style.paddingLeft = 10; _label.style.paddingRight = 10;
            UiRoot.Instance?.ToastLayer?.Add(_label);
            FirstSessionService.Current?.Skip();
            PerfSegments.Ensure();   // a fresh capture account: no first-day guidance over the motion
            try
            {
                await Seg("idle", 3f, ct);
                await Stick(s, "walk", new Vector2(0f, 0.45f), 4f, alive, ct);
                await Seg("stop-walk", 2.5f, ct);
                await Stick(s, "jog", new Vector2(0f, 0.8f), 4f, alive, ct);
                await Seg("stop-jog", 2.5f, ct);
                await Stick(s, "sprint", new Vector2(0f, 1f), 3f, alive, ct);
                await Seg("stop-sprint", 2.5f, ct);
                await Stick(s, "turn-left-90", new Vector2(-1f, 0.05f) * 0.3f, 0.6f, alive, ct);
                await Seg("settle", 1.5f, ct);
                await Stick(s, "walk-back-180", new Vector2(0f, -0.45f), 3f, alive, ct);
                await Seg("settle2", 1.5f, ct);
                await Circle(s, "walk-circle", 0.45f, 6f, alive, ct);
                await Circle(s, "jog-circle", 0.8f, 5f, alive, ct);
                await Stick(s, "jog-reverse-a", new Vector2(0f, 0.8f), 2f, alive, ct);
                await Stick(s, "jog-reverse-b", new Vector2(0f, -0.8f), 2.5f, alive, ct);
                await Seg("idle-end", 3f, ct);
                // the work loop at the counter: fast travel to the Trenches, walk in, clock in
                Mark("seg travel");
                Label("travel");
                FirstSessionOverlays.TravelTo?.Invoke(FirstSessionOverlays.CounterVenue);
                await Wait(3f, ct);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 40f && (VenueSession.Current == null || VenueSession.Current.LocalPlayer == null || FirstSessionOverlays.InWorld == null || !FirstSessionOverlays.InWorld()))
                    await Wait(0.3f, ct);
                await Wait(3f, ct);
                await Counter(VenueSession.Current, alive, ct);
            }
            finally { _label?.RemoveFromHierarchy(); _label = null; PerfSegments.Label = null; }
            Mark("done motion");
        }

        private static void Label(string seg)
        {
            if (_label != null) _label.text = "SEG " + seg;
            PerfSegments.Label = "motion:" + seg;   // per-segment frame timings and CPU markers in the log (perf pass)
        }

        private static async Task Seg(string name, float seconds, CancellationToken ct)
        {
            Mark("seg " + name); Label(name);
            await Wait(seconds, ct);
        }

        private static async Task Stick(VenueSession s, string name, Vector2 input, float seconds, Func<bool> alive, CancellationToken ct)
        {
            Mark("seg " + name); Label(name);
            float t0 = Time.realtimeSinceStartup;
            while (alive() && Time.realtimeSinceStartup - t0 < seconds)
            {
                s.LocalPlayer.SetMoveInput(input);
                await Awaitable.NextFrameAsync(ct);
            }
            s.LocalPlayer.SetMoveInput(Vector2.zero);
        }

        /// <summary>The thumb sweeps round the stick: a steady circle at that strength.</summary>
        private static async Task Circle(VenueSession s, string name, float strength, float seconds, Func<bool> alive, CancellationToken ct)
        {
            Mark("seg " + name); Label(name);
            float t0 = Time.realtimeSinceStartup;
            while (alive() && Time.realtimeSinceStartup - t0 < seconds)
            {
                float a = (Time.realtimeSinceStartup - t0) / seconds * Mathf.PI * 2f;
                s.LocalPlayer.SetMoveInput(new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * strength);
                await Awaitable.NextFrameAsync(ct);
            }
            s.LocalPlayer.SetMoveInput(Vector2.zero);
        }

        private static async Task Counter(VenueSession s, Func<bool> alive, CancellationToken ct)
        {
            var ii = IslandInteriors.Current;
            if (s == null || ii == null || !ii.TryGetRoomDoor(RektBurgerHooks.RoomId, out _, out var door, out var rot)) { Mark("no counter"); return; }
            Mark("seg walk-to-door"); Label("walk-to-door");
            var outside = door + rot * Vector3.forward * 2.6f;
            float t0 = Time.realtimeSinceStartup;
            while (alive() && Time.realtimeSinceStartup - t0 < 90f)
            {
                var d = outside - s.LocalPlayer.transform.position; d.y = 0f;
                if (d.magnitude < 1.4f) break;
                if (!s.LocalPlayer.IsMoving) s.LocalPlayer.MoveTo(outside);
                await Wait(0.5f, ct);
            }
            Mark("seg enter"); Label("enter");
            await StickTo(s, door - rot * Vector3.forward * 2.5f, () => ii.PlayerInside && ii.ActiveRoom != null, 8f, alive, ct);
            await Wait(1.5f, ct);
            var room = ii.ActiveRoom;
            Vector3? spot = null;
            if (room != null && room.spots != null)
                foreach (var sp in room.spots)
                    if (string.IsNullOrEmpty(sp.game) || sp.game == RektBurgerHooks.Shift) { spot = room.transform.TransformPoint(sp.position); break; }
            if (!spot.HasValue) { Mark("no spot"); return; }
            Mark("seg walk-to-counter"); Label("walk-to-counter");
            WorldOffer offer = null;
            await StickTo(s, spot.Value, () => (offer = WorldOffers.Best(s.LocalPlayer.transform.position)) != null && offer.id != null && offer.id.StartsWith("rekt:", StringComparison.Ordinal), 10f, alive, ct);
            await Wait(1f, ct);
            offer = WorldOffers.Best(s.LocalPlayer.transform.position);
            if (offer?.perform != null && offer.id != null && offer.id.StartsWith("rekt:", StringComparison.Ordinal)) offer.perform();
            await Seg("work", 20f, ct);
        }

        private static async Task StickTo(VenueSession s, Vector3 target, Func<bool> done, float seconds, Func<bool> alive, CancellationToken ct)
        {
            var me = s.LocalPlayer;
            float t0 = Time.realtimeSinceStartup;
            while (alive() && !done() && Time.realtimeSinceStartup - t0 < seconds)
            {
                var d = target - me.transform.position; d.y = 0f;
                if (d.magnitude < 0.35f) break;
                var cam = Camera.main;
                float cy = cam != null ? cam.transform.eulerAngles.y : 0f;
                var local = Quaternion.Euler(0f, -cy, 0f) * d.normalized;
                me.SetMoveInput(new Vector2(local.x, local.z) * 0.5f);
                await Awaitable.NextFrameAsync(ct);
            }
            me.SetMoveInput(Vector2.zero);
        }

        private static async Task Wait(float s, CancellationToken ct) => await Awaitable.WaitForSecondsAsync(s, ct);
    }
}
#endif
