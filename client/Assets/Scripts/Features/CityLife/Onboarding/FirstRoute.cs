#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Linq;
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
    /// Dev builds only (lane B, 2026-10-25): launch extra <c>first=1</c> plays a fresh account's first session the way a
    /// player would, for the device recording, with the lane's "CTLIFE [bp-route] shot …" marks for screenshots:
    /// the first steps (thumb on the stick), the shift DM and its tap (fast travel to the Trenches), the walk into Rekt
    /// Burger and Clock in, the first trade on Pulse (when markets are open), Quests' starter card and the GM chest, then
    /// the dream ladder. Every step goes through the real UI or the same calls the UI makes.
    /// </summary>
    public static class FirstRoute
    {
        private static void Mark(string what) => Debug.Log("CTLIFE [bp-route] " + what);

        public static async Task Run(VenueSession s, Action<string> openApp, Action closePhone, Func<bool> alive, CancellationToken ct)
        {
            var svc = FirstSessionService.Current;
            for (int i = 0; i < 40 && svc == null; i++) { await Wait(0.25f, ct); svc = FirstSessionService.Current; }
            if (svc == null) { Mark("no first-session service"); Mark("done first"); return; }
            Mark($"start first: guiding {svc.Guiding} server {svc.ServerBacked} next {svc.Next}");
            await Wait(3f, ct);
            Mark("shot world first steps");
            await Wait(1.6f, ct);   // the runner's screenshot lands before the thumb goes down

            // first steps: a thumb on the stick for a couple of seconds
            await Stick(s, Vector3.forward, 2.6f, alive, ct);
            await Until(() => svc.State.IsDone(FirstBeat.Moved), 6f, ct);
            // the shift DM arrives with the objective line
            DuoNotification dm = null;
            await Until(() => (dm = DuoNotificationCenter.Queue.Stack.FirstOrDefault(n => n.ThreadKey == "first-shift")) != null, 8f, ct);
            await Wait(1.0f, ct);
            Mark("shot objective and shift dm");
            await Wait(2.5f, ct);
            if (dm?.Open != null) dm.Open(); else FirstSessionOverlays.TravelTo?.Invoke(FirstSessionOverlays.CounterVenue);
            await Wait(2f, ct);
            await Until(() => FirstSessionOverlays.InWorld != null && FirstSessionOverlays.InWorld() && VenueSession.Current != null && VenueSession.Current.LocalPlayer != null, 40f, ct);
            s = VenueSession.Current;
            await Wait(4f, ct);
            Mark("shot trenches objective");

            await ClockIn(s, svc, alive, ct);

            if (svc.Available(FirstBeat.Trade) && !svc.State.IsDone(FirstBeat.Trade)) await Trade(svc, openApp, closePhone, ct);
            await QuestsAndChest(svc, openApp, closePhone, ct);

            // the push pre-prompt follows the first chest: a player answers it first ("Not now")
            var later = UiRoot.Instance?.Root?.Q<Button>("push-not-now");
            if (later != null) { await Wait(1.5f, ct); Mark("shot push preprompt"); await Wait(1f, ct); Click(later); }
            await Wait(1.3f, ct);
            Mark("shot first day done");
            await Wait(3f, ct);
            var chip = UiRoot.Instance?.Root?.Q<Button>("hud-ladder");
            if (chip != null) { Click(chip); await Wait(1.6f, ct); Mark("shot ladder sheet"); }
            var earn = UiRoot.Instance?.Root?.Q<Button>("fs-ladder-earn");
            if (earn != null) { Click(earn); await Wait(1.2f, ct); Mark("shot ladder earn"); }
            await Wait(2f, ct);
            Mark($"end first: completed {svc.State.Completed} skipped {svc.State.Skipped} beats {string.Join(",", svc.State.Done)}");
            Mark("done first");
        }

        private static async Task ClockIn(VenueSession s, FirstSessionService svc, Func<bool> alive, CancellationToken ct)
        {
            var ii = IslandInteriors.Current;
            if (ii == null || !ii.TryGetRoomDoor(CTLife.World.RektBurgerHooks.RoomId, out _, out var door, out var rot)) { Mark("no rekt burger door"); return; }
            var outside = door + rot * Vector3.forward * 2.6f;
            // walk up Harbour Road to the door (the same planned walk a tap on the ground makes)
            float t0 = Time.realtimeSinceStartup;
            while (alive() && Time.realtimeSinceStartup - t0 < 90f)
            {
                var me = s.LocalPlayer;
                var d = outside - me.transform.position; d.y = 0f;
                if (d.magnitude < 1.4f) break;
                if (!me.IsMoving) me.MoveTo(outside);
                await Wait(0.5f, ct);
            }
            Mark("shot rekt burger door");
            // in through the door with the stick
            var inside = door - rot * Vector3.forward * 2.5f;
            await StickTo(s, inside, () => ii.PlayerInside && ii.ActiveRoom != null, 8f, alive, ct);
            await Wait(1.2f, ct);
            // up to the counter: the room's shift spot
            var room = ii.ActiveRoom;
            Vector3? counter = null;
            if (room != null && room.spots != null)
                foreach (var sp in room.spots)
                    if (string.IsNullOrEmpty(sp.game) || sp.game == CTLife.World.RektBurgerHooks.Shift) { counter = room.transform.TransformPoint(sp.position); break; }
            if (!counter.HasValue) { Mark("no counter spot"); return; }
            WorldOffer offer = null;
            await StickTo(s, counter.Value, () => (offer = WorldOffers.Best(s.LocalPlayer.transform.position)) != null && offer.id != null && offer.id.StartsWith("rekt:", StringComparison.Ordinal), 10f, alive, ct);
            await Wait(1.5f, ct);
            Mark("shot clock in prompt");
            offer = WorldOffers.Best(s.LocalPlayer.transform.position);
            if (offer?.perform != null && offer.id != null && offer.id.StartsWith("rekt:", StringComparison.Ordinal)) offer.perform();
            await Until(() => svc.State.IsDone(FirstBeat.Shift), 12f, ct);
            await Wait(2.5f, ct);
            Mark("shot on shift");
            await Wait(2f, ct);
        }

        private static async Task Trade(FirstSessionService svc, Action<string> openApp, Action closePhone, CancellationToken ct)
        {
            openApp("pulse");
            await Wait(3f, ct);
            var root = UiRoot.Instance?.Root;
            var row = root?.Query<Button>().Where(b => b.name != null && b.name.StartsWith("pulse-coin-", StringComparison.Ordinal)).First();
            if (row == null) { Mark("no pulse coin"); closePhone(); return; }
            Click(row);
            await Wait(2.2f, ct);
            Mark("shot coach buy");
            var buy = root.Q<Button>("pulse-buy");
            if (buy != null) Click(buy);
            await Wait(1.6f, ct);
            var spend = root.Q("pulse-spend");
            var field = spend as TextField ?? spend?.Q<TextField>();
            if (field != null) field.value = "1";
            await Wait(0.8f, ct);
            Mark("shot coach place");
            var place = root.Q<Button>("pulse-place");
            if (place != null) Click(place);
            await Until(() => svc.State.IsDone(FirstBeat.Trade), 12f, ct);
            await Wait(2f, ct);
            Mark("shot traded");
            var done = root.Q("pulse-order-sheet")?.Query<Button>().Where(b => b.text == "Done").First();
            if (done != null) Click(done);
            await Wait(1f, ct);
            closePhone();
            await Wait(2f, ct);
        }

        private static async Task QuestsAndChest(FirstSessionService svc, Action<string> openApp, Action closePhone, CancellationToken ct)
        {
            openApp(DeepLinks.QuestsApp);
            await Wait(3.5f, ct);
            Mark("shot starter card");
            var root = UiRoot.Instance?.Root;
            // claim what the starter card has ready (the server pays); the card re-renders after each claim
            for (int k = 0; k < 3; k++)
            {
                var b = root.Query<Button>().Where(x => x.name != null && x.name.StartsWith("quest-action-q_starter", StringComparison.Ordinal) && x.enabledInHierarchy && x.ClassListContains("quests-claim")).First();
                if (b == null) break;
                Click(b);
                await Wait(2.4f, ct);
            }
            Mark("shot starter claimed");
            var streak = root.Q<Button>("quests-streak");
            if (streak != null) Click(streak);
            await Wait(2.5f, ct);
            Mark("shot gm chest");
            // tap the chest (the sheet listens for the pointer coming up on it), then Collect
            var stage = root.Q(className: "gm-stage");
            if (stage != null)
            {
                var ev = new Event { type = EventType.MouseUp, mousePosition = stage.worldBound.center, button = 0 };
                using (var up = PointerUpEvent.GetPooled(ev)) { up.target = stage; stage.SendEvent(up); }
            }
            await Until(() => svc.State.IsDone(FirstBeat.Chest), 10f, ct);
            await Wait(3.5f, ct);
            Mark("shot chest opened");
            var collect = root.Q<Button>("gm-collect");
            if (collect != null && collect.enabledInHierarchy) Click(collect);
            await Wait(2.5f, ct);
            var close = root.Q<Button>("gm-close");
            if (close != null) Click(close);
            await Wait(1.5f, ct);
            closePhone();
        }

        // ------------------------------------------------------------------ helpers

        private static void Click(Button b) { using var e = NavigationSubmitEvent.GetPooled(); e.target = b; b.SendEvent(e); }

        private static async Task Wait(float s, CancellationToken ct) => await Awaitable.WaitForSecondsAsync(s, ct);

        private static async Task Until(Func<bool> done, float seconds, CancellationToken ct)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < seconds) await Awaitable.WaitForSecondsAsync(0.2f, ct);
        }

        /// <summary>A thumb on the stick, in camera space (forward = away from the camera).</summary>
        private static async Task Stick(VenueSession s, Vector3 camDir, float seconds, Func<bool> alive, CancellationToken ct)
        {
            float t0 = Time.realtimeSinceStartup;
            while (alive() && Time.realtimeSinceStartup - t0 < seconds)
            {
                s.LocalPlayer.SetMoveInput(new Vector2(camDir.x, camDir.z) * 0.8f);
                await Awaitable.NextFrameAsync(ct);
            }
            s.LocalPlayer.SetMoveInput(Vector2.zero);
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
                me.SetMoveInput(new Vector2(local.x, local.z) * 0.7f);
                await Awaitable.NextFrameAsync(ct);
            }
            me.SetMoveInput(Vector2.zero);
        }
    }
}
#endif
