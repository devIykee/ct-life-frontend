#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.World;
using CTLife.World.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife.Heat
{
    /// <summary>
    /// Dev builds only (lane B, 2026-10-25): launch extra <c>heat=&lt;mode&gt;</c> drives backend #17 for device captures, with
    /// the same "CTLIFE [bp-route] ..." marks the lane's capture runner screenshots on:
    /// - <c>steal</c>: next to a stopped NPC traffic car, the Steal prompt, the steal, a short drive, the heat plate;
    /// - <c>chase</c>: (heat set to wanted beforehand) waits for the pursuit, the cruisers arriving, stands still until they
    ///   make contact, BUSTED, CT Jail, pays bail;
    /// - <c>jail</c>: an existing jail stay: the panel, then bail.
    /// </summary>
    public static class HeatRoute
    {
        private static void Mark(string what) => Debug.Log("CTLIFE [bp-route] " + what);

        public static async Task Run(VenueSession s, string mode, Func<bool> alive, CancellationToken ct)
        {
            var svc = HeatService.Current;
            for (int i = 0; i < 40 && svc == null; i++) { await Awaitable.WaitForSecondsAsync(0.25f, ct); svc = HeatService.Current; }
            if (svc == null) { Mark("no heat service"); Mark("done " + mode); return; }
            await Awaitable.WaitForSecondsAsync(2f, ct);
            Mark($"start {mode}: heat {(svc.Me != null ? svc.Me.Heat : -1):0} jailed {svc.Jailed}");
            switch (mode)
            {
                case "steal": await Steal(s, svc, ct); break;
                case "chase": await Chase(s, svc, ct); break;
                case "jail": await Jail(svc, ct); break;
            }
            await Awaitable.WaitForSecondsAsync(2f, ct);   // let the runner take the last shot
            Mark("done " + mode);
        }

        private static async Task Steal(VenueSession s, HeatService svc, CancellationToken ct)
        {
            float heat0 = svc.Me != null ? (float)svc.Me.Heat : 0f;
            WorldOffer offer = null;
            bool shot = false;
            for (float t = 0; t < 150f && offer == null; t += 0.5f)
            {
                var traffic = TrafficSystem.Current;
                var me = s.LocalPlayer.transform.position;
                TrafficCar car = null;
                if (traffic != null)
                {
                    float best = 160f;
                    foreach (var c in traffic.Cars)
                    {
                        if (c == null || c.Knocked || c.Speed > 0.2f || c.Vehicle == null) continue;
                        float d = Vector3.Distance(me, c.transform.position);
                        if (d < best) { best = d; car = c; }
                    }
                }
                if (car == null) { await Awaitable.WaitForSecondsAsync(0.5f, ct); continue; }
                // stand at its driver's door (a car waiting at a junction)
                var door = car.Vehicle.EntryPoint;
                var side = door - car.transform.position; side.y = 0f;
                var stand = door + side.normalized * 0.9f;
                float g = IslandStreamer.GroundHeight(stand.x, stand.z);
                // face the way the car faces: the camera behind the player sees the car beside them and the prompt
                var fwd = car.transform.forward;
                s.LocalPlayer.SnapTo(new Vector3(stand.x, (float.IsNaN(g) ? car.transform.position.y : g) + 0.05f, stand.z), Mathf.Atan2(fwd.x, fwd.z));
                CameraRig.Instance?.SettleBehind();
                await Awaitable.WaitForSecondsAsync(0.4f, ct);
                var o = WorldOffers.Best(s.LocalPlayer.transform.position);
                // a parked car beside it would take the prompt (Drive outranks Steal): look for a car standing alone
                if (VehicleSystem.Current != null && VehicleSystem.Current.Offered != null) o = null;
                if (o != null && o.id != null && o.id.StartsWith("steal:", StringComparison.Ordinal))
                {
                    if (!shot) { shot = true; await Awaitable.WaitForSecondsAsync(0.3f, ct); Mark("shot steal prompt"); await Awaitable.WaitForSecondsAsync(1.5f, ct); }
                    o = WorldOffers.Best(s.LocalPlayer.transform.position);
                    if (o != null && o.id != null && o.id.StartsWith("steal:", StringComparison.Ordinal)) offer = o;
                }
            }
            if (offer == null) { Mark("no steal offer"); return; }
            // the help box a player's parked car asks with, on foot beside it (look check)
            var near = offer.follow != null ? (offer.follow.GetComponent<TrafficCar>()?.Vehicle ?? offer.follow.GetComponentInChildren<Vehicle>()) : null;
            Mark("help box car: " + (near != null ? near.specId : "none"));
            if (near != null)
            {
                HeatOverlays.Current?.DevHelpBox(near);
                await Awaitable.WaitForSecondsAsync(1.2f, ct);
                Mark("shot steal help box");
                await Awaitable.WaitForSecondsAsync(4f, ct);
                HeatOverlays.Current?.DevCloseHelpBox();
                await Awaitable.WaitForSecondsAsync(0.5f, ct);
                offer = WorldOffers.Best(s.LocalPlayer.transform.position);
                if (offer == null || offer.id == null || !offer.id.StartsWith("steal:", StringComparison.Ordinal)) { Mark("the car moved off"); return; }
            }
            offer.perform();
            for (int i = 0; i < 60 && VehicleSystem.Current?.Driving == null; i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
            var v = VehicleSystem.Current?.Driving;
            Mark("stolen " + (v != null ? v.specId : "no car"));
            if (v == null) return;
            await Awaitable.WaitForSecondsAsync(1f, ct);
            Mark("shot in the stolen car");
            VehicleSystem.ScriptedInput = true;
            try
            {
                for (float t = 0; t < 4f; t += Time.deltaTime) { v.Input = new VehicleInput { throttle = 0.55f }; await Awaitable.NextFrameAsync(ct); }
                v.Input = new VehicleInput { brake = 1f };
                await Awaitable.WaitForSecondsAsync(1.5f, ct);
            }
            finally { VehicleSystem.ScriptedInput = false; }
            svc.Refresh();
            await Awaitable.WaitForSecondsAsync(2f, ct);
            Mark($"heat {heat0:0} -> {(svc.Me != null ? svc.Me.Heat : -1):0} stars {(svc.Me != null ? svc.Me.Stars : -1)}");
            Mark("shot heat plate");

        }

        private static async Task Chase(VenueSession s, HeatService svc, CancellationToken ct)
        {
            await OpenStreet(s, ct);
            PursuitInfo p = null;
            for (float t = 0; t < 90f && (p = svc.MyPursuit) == null; t += 0.5f) await Awaitable.WaitForSecondsAsync(0.5f, ct);
            if (p == null) { Mark("no pursuit (heat " + (svc.Me != null ? svc.Me.Heat : -1) + ")"); return; }
            Mark("pursuit " + p.Units + " units");
            await Awaitable.WaitForSecondsAsync(2.5f, ct);
            Mark("shot pursuit banner");
            var police = PolicePursuits.Current;
            float closest = float.MaxValue;
            float faceAt = 0f;
            for (float t = 0; t < 60f; t += 0.25f)
            {
                closest = float.MaxValue;
                Vehicle lead = null;
                if (police != null) foreach (var c in police.Cruisers) { float d = Vector3.Distance(c.transform.position, s.LocalPlayer.transform.position); if (d < closest) { closest = d; lead = c; } }
                // turn to face the cruiser coming in, so the camera (behind the player) films it arriving
                if (lead != null && closest < 140f && t >= faceAt)
                {
                    faceAt = t + 1.5f;
                    var to = lead.transform.position - s.LocalPlayer.transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 1f) { s.LocalPlayer.SnapTo(s.LocalPlayer.transform.position, Mathf.Atan2(to.x, to.z)); CameraRig.Instance?.SettleBehind(); }
                }
                if (closest < 12f) break;
                await Awaitable.WaitForSecondsAsync(0.25f, ct);
            }
            Mark($"cruisers {police?.CruiserCount ?? 0} closest {closest:0} m");
            Mark("shot cruisers arriving");
            for (float t = 0; t < 150f && svc.MyPursuit != null && !svc.Jailed; t += 0.25f) await Awaitable.WaitForSecondsAsync(0.25f, ct);
            await Awaitable.WaitForSecondsAsync(0.6f, ct);
            Mark("pursuit over: jailed " + svc.Jailed);
            Mark("shot busted");
            for (int i = 0; i < 40 && !svc.Jailed; i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
            await Awaitable.WaitForSecondsAsync(3f, ct);
            if (svc.Jailed) await Jail(svc, ct);
        }

        /// <summary>Walks out of the venue by its door to the road outside and stands on it by the kerb (no teleport: the street
        /// room corrects a jump back towards the last position it accepted).</summary>
        private static async Task OpenStreet(VenueSession s, CancellationToken ct)
        {
            var net = CTLife.World.Roads.RoadNetwork.Active;
            if (net == null) { Mark("no road network"); return; }
            var zone = s.Island.ZoneAt(s.LocalPlayer.transform.position);
            if (zone != null)
            {
                var door = new Vector3(zone.exit.x, s.LocalPlayer.transform.position.y, zone.exit.y);
                await WalkTo(s, door, 25f, ct);
            }
            var lp = net.NearestLanePoint(s.LocalPlayer.transform.position, 60f);
            if (!lp.valid) { Mark("no road near"); return; }
            var dir = lp.direction; dir.y = 0f; dir.Normalize();
            var right = Vector3.Cross(Vector3.up, dir);
            var at = lp.position + right * 2.6f;   // on the road by the kerb: the cruisers can pull right up
            await WalkTo(s, at, 25f, ct);
            await Awaitable.WaitForSecondsAsync(1f, ct);
            CameraRig.Instance?.SettleBehind();
            var p = s.LocalPlayer.transform.position;
            Mark($"on the street at {p.x:0},{p.z:0} (segment {lp.segmentId}, {Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(at.x, 0, at.z)):0.0} m off)");
        }

        private static async Task WalkTo(VenueSession s, Vector3 to, float seconds, CancellationToken ct)
        {
            if (!s.LocalPlayer.MoveTo(to)) return;
            for (float t = 0; t < seconds && s.LocalPlayer.HasDestination; t += 0.25f) await Awaitable.WaitForSecondsAsync(0.25f, ct);
        }

        private static async Task Jail(HeatService svc, CancellationToken ct)
        {
            if (!svc.Jailed) { Mark("not jailed"); return; }
            Mark($"jail: {(svc.Jail.UntilMs - svc.NowMs) / 1000}s left, bail {svc.Jail.BailCents}");
            await Awaitable.WaitForSecondsAsync(4.5f, ct);
            Mark("shot jail panel");   // v2: the bail card (mockup c) over the jail
            await Awaitable.WaitForSecondsAsync(2f, ct);
            // press the card's real Pay button (falls back to the service when the card is not up)
            var pay = CTLife.UI.UiRoot.Instance?.Modals?.Top?.Q<UnityEngine.UIElements.Button>("heat-bail-pay");
            if (pay != null)
            {
                using (var e = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { e.target = pay; pay.SendEvent(e); }
                for (int i = 0; i < 40 && svc.Jailed; i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
                Mark(svc.Jailed ? "bail failed (card)" : "bail paid (card)");
            }
            else
            {
                try { await svc.PayBail(); Mark("bail paid"); }
                catch (Exception e) { Mark("bail failed: " + e.Message); }
            }
            await Awaitable.WaitForSecondsAsync(2f, ct);
            Mark("shot released");
        }
    }
}
#endif
