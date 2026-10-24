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
    /// Dev builds only (2026-10-23): launch extra <c>homeShop=&lt;mode&gt;</c> drives the flat-door / Duo Shop features for
    /// Seeker captures, logging "CTLIFE [homeshop-route] ..." marks to time screenshots and screen recordings against:
    /// - <c>shop</c>: fills the bag and walks the Duo Shop app: catalogue, product (3D), bag, confirm, pay, tracking;
    /// - <c>deliver</c>: stands on the pavement by the player's own door facing the street and waits for the courier
    ///   (a real order's `delivery_out`; <c>order=1</c> also places an ASAP order first); then picks the parcel up;
    /// - <c>place</c>: walks into the player's flat, opens Decorate, picks <c>item=&lt;itemId&gt;</c> (default sofa_linen),
    ///   drags it, turns it and places it (PUT placements);
    /// - <c>locked</c>: walks up to a flat door the player may not enter (the leaf stays shut, the Knock prompt shows);
    /// - <c>visit</c>: <c>peer=&lt;ownerUserId&gt;</c>: POST visit, walk in through their door and stay (a 4403 shows them out).
    /// </summary>
    public static class HomeShopRoute
    {
        private static void Mark(string what) => Debug.Log("CTLIFE [homeshop-route] " + what);

        public static async Task Run(VenueSession s, string mode, Func<bool> alive, CancellationToken ct)
        {
            var svc = HomeShopService.Current;
            if (svc == null) { Mark("no service"); return; }
            for (int i = 0; i < 40 && !svc.HomeKnown; i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
            Mark($"start {mode}: home {svc.MyHome?.HomeId} door {svc.MyHome?.UnitId} status {svc.MyHome?.Status}");
            switch (mode)
            {
                case "shop": await Shop(svc, ct); break;
                case "deliver": await Deliver(s, svc, alive, ct); break;
                case "place": await Place(s, svc, alive, ct); break;
                case "locked": await Locked(s, svc, alive, ct); break;
                case "visit": await Visit(s, svc, alive, ct); break;
                default: Mark("unknown mode " + mode); break;
            }
            Mark("done " + mode);
        }

        private static async Task Shop(HomeShopService svc, CancellationToken ct)
        {
            await svc.LoadCatalog();
            svc.Cart.Clear();
            svc.Cart.Add("sofa_linen"); svc.Cart.Add("lamp_architect"); svc.Cart.Add("plant_zebra");
            HomeShopService.Opener?.Invoke(HomeShopService.AppId);
            await Awaitable.WaitForSecondsAsync(5f, ct);
            Mark("shot catalogue");
            await Awaitable.WaitForSecondsAsync(4f, ct);
            DuoShopApp.Dev("product", "lounge_chair");
            await Awaitable.WaitForSecondsAsync(5f, ct);
            Mark("shot product");
            await Awaitable.WaitForSecondsAsync(4f, ct);
            DuoShopApp.Dev("bag");
            await Awaitable.WaitForSecondsAsync(3f, ct);
            Mark("shot bag");
            await Awaitable.WaitForSecondsAsync(4f, ct);
            DuoShopApp.Dev("confirm");
            await Awaitable.WaitForSecondsAsync(2.5f, ct);
            Mark("shot confirm");
            await Awaitable.WaitForSecondsAsync(4f, ct);
            DuoShopApp.Dev("pay");
            await Awaitable.WaitForSecondsAsync(5f, ct);
            Mark("shot tracking " + svc.Orders.FirstOrDefault()?.OrderId);
            await Awaitable.WaitForSecondsAsync(6f, ct);
        }

        private static async Task<bool> StandAt(VenueSession s, Vector3 at, float yawDeg, CancellationToken ct)
        {
            await s.Island.EnsureCells(at, 200f, ct);
            s.LocalPlayer.SnapTo(new Vector3(at.x, IslandStreamer.GroundHeight(at.x, at.z) + 0.05f, at.z), yawDeg * Mathf.Deg2Rad);
            CameraRig.Instance?.SettleBehind();
            await Awaitable.WaitForSecondsAsync(2f, ct);
            return true;
        }

        private static async Task WalkTo(VenueSession s, Vector3 target, Func<bool> alive, CancellationToken ct, float speed = 0.62f, float within = 0.35f)
        {
            var me = s.LocalPlayer;
            float t0 = Time.realtimeSinceStartup;
            while (alive() && Time.realtimeSinceStartup - t0 < 10f)
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

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        private static bool MyDoor(HomeShopService svc, out Vector3 door, out Quaternion rot)
        {
            door = default; rot = Quaternion.identity;
            var rooms = IslandInteriors.Current;
            return rooms != null && svc.MyHome?.HasDoor == true && rooms.TryGetUnitDoor(svc.MyHome.UnitId, out _, out door, out rot);
        }

        private static async Task Deliver(VenueSession s, HomeShopService svc, Func<bool> alive, CancellationToken ct)
        {
            if (!MyDoor(svc, out var door, out var rot)) { Mark("no own door"); return; }
            var fwd = rot * Vector3.forward;
            // beside the door along the facade, looking across the doorstep to the kerb: the van parks and the courier
            // walks the parcel up in one view
            var right = rot * Vector3.left;
            var standAt = door + fwd * 3.6f - right * 6.5f;
            await StandAt(s, standAt, Quaternion.LookRotation(Flat(door + fwd * 4.5f - standAt)).eulerAngles.y, ct);
            if (LaunchArgs.Flag("order"))
            {
                await svc.LoadCatalog();
                svc.Cart.Clear(); svc.Cart.Add(LaunchArgs.Get("item", "sofa_linen"));
                var o = await svc.Api.Order(svc.Cart, "asap", "door", Guid.NewGuid().ToString("N"), ct);
                svc.Cart.Clear(); svc.Upsert(o);
                Mark($"order placed {o.OrderId} eta {o.EtaAtMs}");
            }
            Mark("waiting for the courier");
            var dd = DeliveryDirector.Current;
            // perf lane 2: `devStage=1` stages the courier client-side in 25 s (no 10-minute ASAP order needed)
            if (LaunchArgs.Flag("devStage") && dd != null) dd.DevStage(svc.MyHome.UnitId, 25f);
            float t0 = Time.realtimeSinceStartup;
            while (alive() && Time.realtimeSinceStartup - t0 < 900f && (dd == null || dd.Active == 0)) await Awaitable.WaitForSecondsAsync(0.5f, ct);
            Mark("courier staged");
            while (alive() && dd != null && dd.Active > 0 && !dd.VanParked) await Awaitable.WaitForSecondsAsync(0.25f, ct);
            Mark("van parked");
            while (alive() && dd != null && dd.Active > 0) await Awaitable.WaitForSecondsAsync(0.5f, ct);
            Mark("courier gone");
            // the parcel is ready once the server says the order arrived (delivery_arrived)
            float tr = Time.realtimeSinceStartup;
            while (alive() && Time.realtimeSinceStartup - tr < 120f && (dd == null || !dd.HasReadyParcel)) await Awaitable.WaitForSecondsAsync(0.5f, ct);
            Mark("parcel ready");
            await Awaitable.WaitForSecondsAsync(1f, ct);
            await WalkTo(s, door + fwd * 1.5f, alive, ct);
            await Awaitable.WaitForSecondsAsync(2.5f, ct);
            var ic = InteractionController.Current;
            Mark("at the parcel");
            if (ic != null) ic.Perform("ext", "ext:" + (WorldOffers.Best(s.LocalPlayer.transform.position)?.id ?? ""));
            await Awaitable.WaitForSecondsAsync(3f, ct);
            Mark($"picked up: {svc.Items.Count} owned");
        }

        private static async Task EnterOwnFlat(VenueSession s, HomeShopService svc, Func<bool> alive, CancellationToken ct)
        {
            if (!MyDoor(svc, out var door, out var rot)) { Mark("no own door"); return; }
            var fwd = rot * Vector3.forward;
            await StandAt(s, door + fwd * 6f, rot.eulerAngles.y + 180f, ct);
            var rooms = IslandInteriors.Current;
            for (int i = 0; i < 40 && alive() && (rooms.ActiveRoom == null || !rooms.ActiveRoom.gameObject.activeSelf); i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
            await Awaitable.WaitForSecondsAsync(1.5f, ct);
            Mark("walk in");
            await WalkTo(s, door + fwd * 0.8f, alive, ct);
            var room = rooms.ActiveRoom;
            if (room != null) await WalkTo(s, room.transform.TransformPoint(new Vector3(0f, 0f, -2.6f)), alive, ct);
            await Awaitable.WaitForSecondsAsync(1f, ct);
            Mark("inside " + (rooms.PlayerInside ? "yes" : "no"));
        }

        private static async Task Place(VenueSession s, HomeShopService svc, Func<bool> alive, CancellationToken ct)
        {
            await EnterOwnFlat(s, svc, alive, ct);
            var pm = PlaceMode.Current;
            if (pm == null) { Mark("no place mode"); return; }
            await Awaitable.WaitForSecondsAsync(2f, ct);
            Mark("decorate");
            await pm.EnterEdit();
            await Awaitable.WaitForSecondsAsync(3f, ct);
            Mark("shot tray");
            if (!pm.DevPick(LaunchArgs.Get("item", "sofa_linen"))) { Mark("nothing to place"); return; }
            Mark("picked");
            await Awaitable.WaitForSecondsAsync(1.5f, ct);
            await pm.DevDragToFit(2.4f);
            await Awaitable.WaitForSecondsAsync(0.8f, ct);
            for (int i = 0; i < 2; i++)
            {
                pm.DevRotate(45f); await Awaitable.WaitForSecondsAsync(0.8f, ct);
                if (!pm.DevFits) { pm.DevRotate(-45f); await Awaitable.WaitForSecondsAsync(0.5f, ct); break; }
            }
            if (!pm.DevFits) await pm.DevDragToFit(1.2f);
            await Awaitable.WaitForSecondsAsync(1f, ct);
            Mark("fits " + pm.DevFits);
            await pm.DevConfirm();
            Mark("placed");
            await Awaitable.WaitForSecondsAsync(2f, ct);
            pm.ExitEdit(save: false);
            await Awaitable.WaitForSecondsAsync(5f, ct);
        }

        private static async Task Locked(VenueSession s, HomeShopService svc, Func<bool> alive, CancellationToken ct)
        {
            var rooms = IslandInteriors.Current;
            int pick = -1;
            string want = LaunchArgs.Get("unit");
            for (int i = 0; i < rooms.DoorCount; i++)
                if ((want != null && rooms.UnitOf(i) == want) || (want == null && rooms.UnitOf(i) != svc.MyHome?.UnitId)) { pick = i; break; }
            if (pick < 0 || !rooms.TryGetDoor(pick, out var door, out var rot)) { Mark("no stranger door"); return; }
            var fwd = rot * Vector3.forward;
            await StandAt(s, door + fwd * 7f, rot.eulerAngles.y + 180f, ct);
            Mark("approach " + rooms.UnitOf(pick));
            await WalkTo(s, door + fwd * 1.0f, alive, ct, 0.5f);
            await Awaitable.WaitForSecondsAsync(1.5f, ct);
            Mark($"at the door: locked {rooms.IsLocked(pick)}");
            // push on into the shut door for a moment: it must hold
            var t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 2f) { s.LocalPlayer.SetMoveInput(new Vector2(0f, 0.6f)); await Awaitable.NextFrameAsync(ct); }
            s.LocalPlayer.SetMoveInput(Vector2.zero);
            Mark($"pushed: inside {rooms.PlayerInside}");
            await Awaitable.WaitForSecondsAsync(6f, ct);
        }

        private static async Task Visit(VenueSession s, HomeShopService svc, Func<bool> alive, CancellationToken ct)
        {
            var owner = LaunchArgs.Get("peer");
            if (string.IsNullOrEmpty(owner)) { Mark("no peer"); return; }
            (string homeId, string unitId) v;
            try { v = await svc.Visit(owner); }
            catch (Exception e) { Mark("visit refused: " + e.Message); return; }
            var rooms = IslandInteriors.Current;
            if (v.unitId == null || !rooms.TryGetUnitDoor(v.unitId, out _, out var door, out var rot)) { Mark("their door is not on this island"); return; }
            var fwd = rot * Vector3.forward;
            await StandAt(s, door + fwd * 6f, rot.eulerAngles.y + 180f, ct);
            for (int i = 0; i < 40 && alive() && (rooms.ActiveRoom == null || !rooms.ActiveRoom.gameObject.activeSelf); i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
            await Awaitable.WaitForSecondsAsync(1.5f, ct);
            await WalkTo(s, door + fwd * 0.8f, alive, ct);
            var room = rooms.ActiveRoom;
            if (room != null) await WalkTo(s, room.transform.TransformPoint(new Vector3(0f, 0f, -2.4f)), alive, ct);
            Mark($"inside {rooms.PlayerInside} (home {svc.InsideHomeId})");
            float t0 = Time.realtimeSinceStartup;
            while (alive() && Time.realtimeSinceStartup - t0 < 240f && rooms.PlayerInside) await Awaitable.WaitForSecondsAsync(0.5f, ct);
            Mark("left the flat: inside " + rooms.PlayerInside);
            await Awaitable.WaitForSecondsAsync(4f, ct);
        }
    }
}
#endif
