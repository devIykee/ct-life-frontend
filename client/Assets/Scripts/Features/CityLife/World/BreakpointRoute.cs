#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.World;
using UnityEngine;

namespace CTLife.CityLife
{
    /// <summary>
    /// Dev builds only (lane B, 2026-10-24): launch extra <c>bp=&lt;mode&gt;</c> drives Breakpoint 2026 for Seeker captures,
    /// logging "CTLIFE [bp-route] ..." marks to time screenshots and screen recordings against:
    /// - <c>plaza</c>: the London-skinned plaza from the street, then from the entrance;
    /// - <c>walk</c>: walks in from the street, visits booth 2, then spots nod 1 (the screen-recording leg);
    /// - <c>booth</c>: walks up to booths 1..3 and visits them;
    /// - <c>trivia</c>: the kiosk by the keynote stage, the trivia sheet, four answers (one wrong on purpose);
    /// - <c>nod</c>: spots nods 1..5 (photo moment each);
    /// - <c>claim</c>: finishes whatever is left of the chain; the claim moment shows, then "Wear the lanyard";
    /// - <c>upcoming</c>: the countdown totem on the plaza outside the event window, then the Events app;
    /// - <c>app</c>: the Events app (poster, quest chain, calendar).
    /// </summary>
    public static class BreakpointRoute
    {
        private static void Mark(string what) => Debug.Log("CTLIFE [bp-route] " + what);

        public static async Task Run(VenueSession s, string mode, Func<bool> alive, CancellationToken ct)
        {
            var svc = EventsService.Current;
            if (svc == null) { Mark("no service"); return; }
            for (int i = 0; i < 60 && !svc.Loaded; i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
            var hall = s.Island.Find(EventIds.Venue);
            if (hall == null) { Mark("no hall"); return; }
            Mark($"start {mode}: breakpoint {(svc.Breakpoint == null ? "none" : svc.BreakpointLive ? "live" : "upcoming")}");
            // stand by the hall first so the skin streams in
            await StandAt(s, hall, new Vector3(3f, 0f, -30f), 0f, ct);
            for (int i = 0; i < 80 && (BreakpointVenue.Current == null || BreakpointVenue.Current.Skin == null); i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
            await Awaitable.WaitForSecondsAsync(2.5f, ct);
            switch (mode)
            {
                case "plaza": await Plaza(s, hall, ct); break;
                case "walk": await Walk(s, hall, alive, ct); break;
                case "booth": await Booths(s, alive, ct, 0, 1, 2); break;
                case "trivia": await Trivia(s, hall, alive, ct, true); break;
                case "nod": await Nods(s, alive, ct, 5); break;
                case "claim": await Claim(s, hall, alive, ct); break;
                case "upcoming": await Upcoming(s, hall, ct); break;
                case "app": await App(ct); break;
                case "irl": await Irl(s, hall, ct); break;
                case "retry": await Retry(ct); break;
                case "perf": await PlazaPerf(s, hall, alive, ct); break;
                case "followups": await Followups(ct); break;
                default: Mark("unknown mode " + mode); break;
            }
            Mark("done " + mode);
        }

        private static Vector3 W(IslandVenue hall, Vector3 local) => hall.Position + hall.Rotation * local;

        private static async Task StandAt(VenueSession s, IslandVenue hall, Vector3 local, float localYaw, CancellationToken ct)
        {
            var at = W(hall, local);
            await s.Island.EnsureCells(at, 200f, ct);
            float g = IslandStreamer.GroundHeight(at.x, at.z);
            if (CharacterBody.GroundAt(at + Vector3.up * 3f, out float floor)) g = Mathf.Max(float.IsNaN(g) ? floor : g, floor);
            s.LocalPlayer.SnapTo(new Vector3(at.x, (float.IsNaN(g) ? hall.elevation : g) + 0.05f, at.z), (hall.yaw + localYaw) * Mathf.Deg2Rad);
            CameraRig.Instance?.SettleBehind();
            await Awaitable.WaitForSecondsAsync(1.5f, ct);
        }

        private static async Task WalkTo(VenueSession s, Vector3 target, Func<bool> alive, CancellationToken ct, float speed = 0.7f, float within = 0.45f)
        {
            var me = s.LocalPlayer;
            float t0 = Time.realtimeSinceStartup;
            while (alive() && Time.realtimeSinceStartup - t0 < 14f)
            {
                var d = target - me.transform.position; d.y = 0f;
                if (d.magnitude < within) break;
                var cam = Camera.main;
                float cy = cam != null ? cam.transform.eulerAngles.y : 0f;
                var local = Quaternion.Euler(0f, -cy, 0f) * d.normalized;
                me.SetMoveInput(new Vector2(local.x, local.z) * speed);
                await Awaitable.NextFrameAsync(ct);
            }
            me.SetMoveInput(Vector2.zero);
        }

        private static async Task Plaza(VenueSession s, IslandVenue hall, CancellationToken ct)
        {
            await StandAt(s, hall, new Vector3(-4f, 0f, -31f), 8f, ct);
            await Awaitable.WaitForSecondsAsync(3f, ct);
            Mark("shot plaza street");
            await Awaitable.WaitForSecondsAsync(3f, ct);
            await StandAt(s, hall, new Vector3(9f, 0f, -24f), -32f, ct);
            await Awaitable.WaitForSecondsAsync(3f, ct);
            Mark("shot plaza bus");
            await Awaitable.WaitForSecondsAsync(3f, ct);
            await StandAt(s, hall, new Vector3(-1f, 0f, -21f), 0f, ct);
            await Awaitable.WaitForSecondsAsync(3f, ct);
            Mark("shot plaza entrance");
            await Awaitable.WaitForSecondsAsync(3f, ct);
        }

        /// <summary>Perf segments at the Breakpoint plaza (PerfSegments lines "seg=plaza" standing, "seg=plazaWalk" walking).</summary>
        private static async Task PlazaPerf(VenueSession s, IslandVenue hall, Func<bool> alive, CancellationToken ct)
        {
            PerfSegments.Ensure();
            await StandAt(s, hall, new Vector3(-4f, 0f, -31f), 8f, ct);
            await Awaitable.WaitForSecondsAsync(4f, ct);
            PerfSegments.Label = "plaza";
            await Awaitable.WaitForSecondsAsync(20f, ct);
            PerfSegments.Label = "plazaWalk";
            await WalkTo(s, W(hall, new Vector3(-1.2f, 0f, -22f)), alive, ct);
            await WalkTo(s, W(hall, new Vector3(-12f, 0f, -18f)), alive, ct);
            await WalkTo(s, W(hall, new Vector3(12f, 0f, -24f)), alive, ct);
            await WalkTo(s, W(hall, new Vector3(-2f, 0f, -33f)), alive, ct);
            PerfSegments.Label = null;
            await Awaitable.WaitForSecondsAsync(1f, ct);
        }

        private static async Task Walk(VenueSession s, IslandVenue hall, Func<bool> alive, CancellationToken ct)
        {
            await StandAt(s, hall, new Vector3(-2f, 0f, -33f), 0f, ct);
            await Awaitable.WaitForSecondsAsync(2f, ct);
            Mark("walk start");
            await WalkTo(s, W(hall, new Vector3(-1.2f, 0f, -22f)), alive, ct);
            await WalkTo(s, W(hall, new Vector3(-5.5f, 0f, -16.5f)), alive, ct);
            var v = BreakpointVenue.Current;
            var booth = v?.Booth(1);
            if (booth != null) await WalkTo(s, booth.position, alive, ct, 0.5f, 0.6f);
            await Awaitable.WaitForSecondsAsync(1f, ct);
            Mark("booth prompt");
            EventsOverlaysAccess()?.VisitBooth(1);
            await Awaitable.WaitForSecondsAsync(4f, ct);
            await WalkTo(s, W(hall, new Vector3(-12f, 0f, -17f)), alive, ct);
            var nod = v?.Nod(0);
            if (nod != null) await WalkTo(s, nod.position + (nod.position - s.LocalPlayer.transform.position).normalized * -1.8f, alive, ct, 0.55f, 0.6f);
            await Awaitable.WaitForSecondsAsync(1.2f, ct);
            Mark("nod prompt");
            await SpotWithBudget(0, ct);
            Mark("walk end");
        }

        private static EventsOverlays EventsOverlaysAccess() => BreakpointVenue.Current != null ? BreakpointVenue.Current.Ui : null;

        private static async Task SpotWithBudget(int i, CancellationToken ct)
        {
            var v = BreakpointVenue.Current;
            EventsOverlaysAccess()?.SpotNod(i, v?.NodDisc(i));
            await Awaitable.WaitForSecondsAsync(1.1f, ct);
            Mark("shot nod " + (i + 1) + " photo");
            await Awaitable.WaitForSecondsAsync(4.2f, ct);
        }

        private static async Task Booths(VenueSession s, Func<bool> alive, CancellationToken ct, params int[] which)
        {
            var v = BreakpointVenue.Current;
            foreach (int i in which)
            {
                var b = v?.Booth(i);
                if (b == null) continue;
                var stand = b.position - b.forward * 1.6f;
                s.LocalPlayer.SnapTo(new Vector3(stand.x, b.position.y + 0.05f, stand.z), Mathf.Atan2(b.forward.x, b.forward.z));
                CameraRig.Instance?.SettleBehind();
                await Awaitable.WaitForSecondsAsync(1.5f, ct);
                await WalkTo(s, b.position, alive, ct, 0.45f, 0.5f);
                await Awaitable.WaitForSecondsAsync(1.2f, ct);
                Mark("shot booth " + (i + 1) + " prompt");
                EventsOverlaysAccess()?.VisitBooth(i);
                await Awaitable.WaitForSecondsAsync(1.4f, ct);
                Mark("shot booth " + (i + 1) + " visited");
                await Awaitable.WaitForSecondsAsync(2.6f, ct);
            }
        }

        private static async Task Trivia(VenueSession s, IslandVenue hall, Func<bool> alive, CancellationToken ct, bool oneWrong)
        {
            var v = BreakpointVenue.Current;
            var k = v?.Skin?.kiosk;
            if (k != null)
            {
                var stand = k.position - k.forward * 1.2f;
                s.LocalPlayer.SnapTo(new Vector3(stand.x, k.position.y + 0.05f, stand.z), Mathf.Atan2(k.forward.x, k.forward.z));
                CameraRig.Instance?.SettleBehind();
                await Awaitable.WaitForSecondsAsync(1.5f, ct);
                // entering the hall puts the player at its door: walk across to the kiosk
                await WalkTo(s, stand, () => true, ct, 0.7f, 0.5f);
                await WalkTo(s, k.position, () => true, ct, 0.4f, 0.45f);
                CameraRig.Instance?.SettleBehind();
                await Awaitable.WaitForSecondsAsync(2f, ct);
                Mark("shot kiosk");
                await Awaitable.WaitForSecondsAsync(2f, ct);
            }
            EventsOverlaysAccess()?.OpenTrivia();
            await Awaitable.WaitForSecondsAsync(3f, ct);
            Mark("shot trivia open");
            var answers = new[] { ("bp26.q_city", "a"), ("bp26.q_venue", "b"), ("bp26.q_month", oneWrong ? "a" : "c"), ("bp26.q_days", "c") };
            var svc = EventsService.Current;
            int n = 0;
            foreach (var (q, a) in answers)
            {
                n++;
                if (svc.HasDone(q)) continue;
                if (svc.Breakpoint?.Quest?.Find(EventIds.StepTrivia)?.Complete == true) break;
                EventsApp.DevAnswer?.Invoke(q, a);     // taps the choice in the open app (right / wrong colours)
                await Awaitable.WaitForSecondsAsync(1.6f, ct);
                Mark("shot trivia q" + n);
                await Awaitable.WaitForSecondsAsync(1.0f, ct);
            }
            Mark("shot trivia answered");
            await Awaitable.WaitForSecondsAsync(3f, ct);
        }

        private static async Task Nods(VenueSession s, Func<bool> alive, CancellationToken ct, int count)
        {
            var v = BreakpointVenue.Current;
            int spotted = 0;
            for (int i = 0; i < EventIds.NodCount && spotted < count; i++)
            {
                if (EventsService.Current.HasDone(EventIds.Nod(i))) { spotted++; continue; }
                var n = v?.Nod(i);
                if (n == null) continue;
                var from = n.position + (Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward) * 2.4f;
                await s.Island.EnsureCells(from, 120f, ct);
                float g = IslandStreamer.GroundHeight(from.x, from.z);
                var look = n.position - from;
                s.LocalPlayer.SnapTo(new Vector3(from.x, (float.IsNaN(g) ? n.position.y : g) + 0.05f, from.z), Mathf.Atan2(look.x, look.z));
                CameraRig.Instance?.SettleBehind();
                await Awaitable.WaitForSecondsAsync(2.2f, ct);
                Mark("shot nod " + (i + 1) + " prompt");
                await SpotWithBudget(i, ct);
                spotted++;
            }
        }

        private static async Task Claim(VenueSession s, IslandVenue hall, Func<bool> alive, CancellationToken ct)
        {
            var svc = EventsService.Current;
            var q = svc.Breakpoint?.Quest;
            if (q?.Find(EventIds.StepBooths)?.Complete != true) await Booths(s, alive, ct, Enumerable.Range(0, EventIds.BoothCount).Where(i => !svc.HasDone(EventIds.Booth(i))).Take(3).ToArray());
            if (svc.Breakpoint?.Quest?.Find(EventIds.StepTrivia)?.Complete != true) { await Trivia(s, hall, alive, ct, false); EventsService.Closer?.Invoke(); await Awaitable.WaitForSecondsAsync(1f, ct); }
            await Nods(s, alive, ct, 5);
            await Awaitable.WaitForSecondsAsync(2.5f, ct);
            Mark("shot claim");
            await Awaitable.WaitForSecondsAsync(3f, ct);
            var ok = await EventsOverlays.WearLanyard(EventIds.Lanyard);
            Mark("wear lanyard " + ok);
            await Awaitable.WaitForSecondsAsync(2f, ct);
            Mark("shot claim worn");
            await Awaitable.WaitForSecondsAsync(3f, ct);
        }

        private static async Task Upcoming(VenueSession s, IslandVenue hall, CancellationToken ct)
        {
            await StandAt(s, hall, new Vector3(-7.4f, 0f, -24.8f), 12f, ct);
            await Awaitable.WaitForSecondsAsync(3f, ct);
            Mark("shot upcoming totem");
            await Awaitable.WaitForSecondsAsync(3f, ct);
            await App(ct);
        }

        /// <summary>The IRL check-in from the Events app (the system permission dialog is answered by the capture script),
        /// then the board toggle, then the IRL HERE board on the plaza.</summary>
        private static async Task Irl(VenueSession s, IslandVenue hall, CancellationToken ct)
        {
            EventsService.Opener?.Invoke(EventsService.AppId);
            await Awaitable.WaitForSecondsAsync(3f, ct);
            EventsApp.DevScrollTo?.Invoke("evt-irl");
            await Awaitable.WaitForSecondsAsync(1f, ct);
            Mark("shot irl card");
            EventsApp.DevCheckIn?.Invoke();
            Mark("irl checking");
            var svc = EventsService.Current;
            for (int i = 0; i < 120 && !svc.IrlCheckedIn; i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
            await Awaitable.WaitForSecondsAsync(2.5f, ct);
            if (!svc.IrlCheckedIn) EventsApp.DevScrollTo?.Invoke("evt-irl");
            await Awaitable.WaitForSecondsAsync(0.8f, ct);
            Mark("shot irl result " + (svc.IrlCheckedIn ? "checked-in" : "refused"));
            await Awaitable.WaitForSecondsAsync(3f, ct);
            if (!svc.IrlCheckedIn) return;
            Mark("shot irl claim");
            await Awaitable.WaitForSecondsAsync(2f, ct);
            BreakpointVenue.Current?.Ui?.CloseAllForDev();
            if (!svc.IrlVisible) { await svc.SetIrlVisible(true); }
            await svc.LoadIrlBoard(force: true);
            EventsService.Closer?.Invoke();
            await Awaitable.WaitForSecondsAsync(1f, ct);
            EventsService.Opener?.Invoke(EventsService.AppId);
            await Awaitable.WaitForSecondsAsync(3f, ct);
            EventsApp.DevScrollTo?.Invoke("evt-irl");
            await Awaitable.WaitForSecondsAsync(1f, ct);
            Mark("shot irl visible");
            EventsApp.DevScrollTo?.Invoke("evt-cal");
            await Awaitable.WaitForSecondsAsync(1f, ct);
            Mark("shot irl attendees");
            EventsService.Closer?.Invoke();
            await StandAt(s, hall, new Vector3(4.6f, 0f, -24.2f), -16f, ct);
            await Awaitable.WaitForSecondsAsync(3f, ct);
            Mark("shot irl board");
            await Awaitable.WaitForSecondsAsync(2f, ct);
        }

        /// <summary>PR #18 trivia: a wrong answer greys that choice and starts the question's cooldown; the other
        /// questions stay open; when the countdown ends the question opens again and the right answer lands.</summary>
        private static async Task Retry(CancellationToken ct)
        {
            var svc = EventsService.Current;
            EventsOverlaysAccess()?.OpenTrivia();
            await Awaitable.WaitForSecondsAsync(3f, ct);
            Mark("shot trivia open");
            EventsApp.DevAnswer?.Invoke("bp26.q_month", "a");     // wrong
            await Awaitable.WaitForSecondsAsync(2f, ct);
            Mark("shot trivia wrong cooldown");
            EventsApp.DevAnswer?.Invoke("bp26.q_month", "a");     // same choice again (the screen greys it; the server refuses)
            await Awaitable.WaitForSecondsAsync(1.5f, ct);
            EventsApp.DevAnswer?.Invoke("bp26.q_city", "a");      // another question is still open
            await Awaitable.WaitForSecondsAsync(2f, ct);
            Mark("shot trivia other question");
            var q = svc.Breakpoint?.Quest?.Find(EventIds.StepTrivia)?.Questions?.Find(x => x.Id == "bp26.q_month");
            Mark("retryAt in " + ((q?.RetryAtMs ?? 0) - svc.NowMs) / 1000 + "s tried=" + string.Join(",", q?.Tried ?? new System.Collections.Generic.HashSet<string>()));
            for (int i = 0; i < 140 && q != null && q.CoolingDown(svc.NowMs); i++) await Awaitable.WaitForSecondsAsync(0.5f, ct);
            await Awaitable.WaitForSecondsAsync(1.5f, ct);
            Mark("shot trivia reopened");
            EventsApp.DevAnswer?.Invoke("bp26.q_month", "c");     // right
            await Awaitable.WaitForSecondsAsync(2f, ct);
            Mark("shot trivia retried right");
            await Awaitable.WaitForSecondsAsync(2f, ct);
        }

        /// <summary>PR #18 for a checked-in player: the restored state, "Say hi" on the IRL board, and wearing the lanyard.</summary>
        private static async Task Followups(CancellationToken ct)
        {
            var svc = EventsService.Current;
            for (int i = 0; i < 40 && !svc.Loaded; i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
            Mark("restored done=" + svc.Breakpoint?.Quest?.Steps?.Sum(x => x.Done) + " irl=" + svc.IrlCheckedIn + " visible=" + svc.IrlVisible
                 + " local booths=" + Enumerable.Range(0, EventIds.BoothCount).Count(i => svc.HasDone(EventIds.Booth(i)))
                 + " nods=" + Enumerable.Range(0, 8).Count(i => svc.HasDone(EventIds.Nod(i))));
            EventsService.Opener?.Invoke(EventsService.AppId);
            await Awaitable.WaitForSecondsAsync(3f, ct);
            Mark("shot app restored");
            EventsApp.DevScrollTo?.Invoke("evt-step");
            await Awaitable.WaitForSecondsAsync(1.5f, ct);
            EventsApp.DevScrollTo?.Invoke("evt-step");
            await Awaitable.WaitForSecondsAsync(1f, ct);
            Mark("shot steps restored");
            await svc.LoadIrlBoard(force: true);
            EventsService.Closer?.Invoke();
            await Awaitable.WaitForSecondsAsync(0.8f, ct);
            EventsService.Opener?.Invoke(EventsService.AppId);
            await Awaitable.WaitForSecondsAsync(2.5f, ct);
            EventsApp.DevScrollTo?.Invoke("evt-cal");
            await Awaitable.WaitForSecondsAsync(1f, ct);
            Mark("shot irl rows");
            await Awaitable.WaitForSecondsAsync(2f, ct);
            EventsApp.DevSayHi?.Invoke();
            await Awaitable.WaitForSecondsAsync(2.5f, ct);
            Mark("shot irl hi sent");
            EventsService.Closer?.Invoke();
            await Awaitable.WaitForSecondsAsync(1f, ct);
            var ok = await EventsOverlays.WearLanyard(EventIds.Lanyard);
            Mark("wear lanyard " + ok);
            await Awaitable.WaitForSecondsAsync(2.5f, ct);
            Mark("shot lanyard worn");
            await Awaitable.WaitForSecondsAsync(1.5f, ct);
        }

        private static async Task App(CancellationToken ct)
        {
            EventsService.Opener?.Invoke(EventsService.AppId);
            await Awaitable.WaitForSecondsAsync(3.5f, ct);
            Mark("shot app");
            await Awaitable.WaitForSecondsAsync(4f, ct);
        }
    }
}
#endif
