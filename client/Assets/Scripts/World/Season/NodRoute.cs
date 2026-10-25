#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Dev builds only (2026-10-25): the community nods' route, launch extra <c>islandRoom=nods</c>: from where the player is,
    /// to the three nearest nods still standing, one after another: online it WALKS (LocalPlayerController.MoveTo, the island
    /// NavMesh; a SnapTo would outrun the street room's walking rule and the server would put the player back), offline it
    /// snaps; at each it stands in front of the disc, waits for the "Collect" prompt and presses it (NodHooks.TryCollect),
    /// then waits for NodHooks.Collected. With no City Life (offline) it plays the pickup locally. Logs "CTLIFE [nod-route]".
    /// </summary>
    public static class NodRoute
    {
        private static void Mark(string what) => Debug.Log("CTLIFE [nod-route] " + what);
        private static string Fmt(Vector3 p) => $"({p.x:0.0}, {p.z:0.0})";

        public static async Task Run(VenueSession s, Func<bool> alive, CancellationToken ct, int count = 3)
        {
            PerfSegments.Ensure();
            var me = s.LocalPlayer;
            NodField field = null;
            for (int i = 0; i < 40 && alive() && (field = NodField.Current) == null; i++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
            if (field == null || me == null) { Mark("no nod field"); return; }
            var start = me.transform.position;
            var order = Enumerable.Range(0, field.Nods.Count).Where(field.Standing)
                .OrderBy(i => Flat(new Vector3(field.Nods[i].x, 0f, field.Nods[i].z) - start).sqrMagnitude).Take(count).ToList();
            Mark($"from {Fmt(start)}: {string.Join(", ", order.Select(i => field.Nods[i].nodId))}");
#if UNITY_EDITOR
            void OnReview(string c) { if (c.StartsWith("path-", StringComparison.Ordinal)) Mark("  " + c); }
            me.ReviewCommandObserved += OnReview;
            try {
#endif
            int got = 0;
            foreach (int i in order)
            {
                if (!alive()) return;
                var n = field.Nods[i];
                var front = new Vector3(n.x, n.y, n.z) + Quaternion.Euler(0f, n.yaw, 0f) * Vector3.forward * 1.3f;
                await s.Island.EnsureCells(front, 120f, ct);
                PerfSegments.Label = "nod_walk";
                if (s.IsOnline) await WalkTo(me, alive, ct, front);
                else me.SnapTo(new Vector3(front.x, IslandStreamer.GroundHeight(front.x, front.z) + 0.05f, front.z), (n.yaw + 180f) * Mathf.Deg2Rad);
                PerfSegments.Label = null;
                float d = Flat(me.transform.position - new Vector3(n.x, 0f, n.z)).magnitude;
                Mark($"at {n.nodId} ({n.setId}) {Fmt(me.transform.position)}, {d:0.0} m from the disc");
                bool collected = false;
                void OnCollected(string id) { if (id == n.nodId) collected = true; }
                NodHooks.Collected += OnCollected;
                try
                {
                    await Awaitable.WaitForSecondsAsync(1.0f, ct);
                    var ic = InteractionController.Current;
                    var offer = ic != null ? ic.ExtOffer : null;
                    Mark($"prompt: {(offer != null ? offer.id + " (" + offer.verbKey + ")" : "none")}");
                    if (offer == null || offer.id != "nod:" + n.nodId) continue;
                    ic.Perform("ext", "ext:" + offer.id);
                    if (NodHooks.TryCollect == null) { Mark("no City Life: playing the pickup locally (dev)"); NodHooks.NotifyCollected(n.nodId); }
                    for (int k = 0; k < 24 && alive() && !collected; k++) await Awaitable.WaitForSecondsAsync(0.25f, ct);
                    Mark(collected ? $"collected {n.nodId}" : $"no answer for {n.nodId}");
                    if (collected) got++;
                    await Awaitable.WaitForSecondsAsync(1.5f, ct);
                }
                finally { NodHooks.Collected -= OnCollected; }
            }
            Mark($"done: {got}/{order.Count} collected");
#if UNITY_EDITOR
            } finally { me.ReviewCommandObserved -= OnReview; }
#endif
        }

        private static async Task WalkTo(LocalPlayerController me, Func<bool> alive, CancellationToken ct, Vector3 target)
        {
            for (int attempt = 0; attempt < 4 && alive(); attempt++)
            {
                if (!me.MoveTo(target)) { Mark("MoveTo refused at " + Fmt(me.transform.position)); return; }
                float t0 = Time.realtimeSinceStartup;
                while (alive() && me.IsMoving && Time.realtimeSinceStartup - t0 < 150f) await Awaitable.WaitForSecondsAsync(0.25f, ct);
                if (Flat(me.transform.position - target).magnitude < 1.2f) return;
                Mark($"stopped {Flat(me.transform.position - target).magnitude:0.0} m short at {Fmt(me.transform.position)}; round it");
                // something the planner did not get round (a bus shelter, a bench run): a clear step to one side first (back
                // a little, out, or out and on; the first whose whole way a body capsule passes), then the target again
                var pos = me.transform.position;
                var dir = Flat(target - pos).normalized;
                var side = Vector3.Cross(Vector3.up, dir);
                const int solid = (1 << 12) | (1 << 13) | (1 << 15) | (1 << 17);
                foreach (var (s, back, on) in new[] { (1f, 1f, 0f), (-1f, 1f, 0f), (1f, 0f, 2f), (-1f, 0f, 2f), (1f, 2.5f, 0f), (-1f, 2.5f, 0f) })
                {
                    var step = pos + side * (3.5f * s) - dir * back + dir * on;
                    var way = step - pos;
                    if (Physics.CapsuleCast(pos + Vector3.up * 0.45f, pos + Vector3.up * 1.5f, 0.32f, way.normalized, way.magnitude, solid, QueryTriggerInteraction.Ignore)) continue;
                    if (!me.MoveTo(step)) continue;
                    float t1 = Time.realtimeSinceStartup;
                    while (alive() && me.IsMoving && Time.realtimeSinceStartup - t1 < 20f) await Awaitable.WaitForSecondsAsync(0.25f, ct);
                    Mark($"  stepped round to {Fmt(me.transform.position)}");
                    break;
                }
            }
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
#endif
