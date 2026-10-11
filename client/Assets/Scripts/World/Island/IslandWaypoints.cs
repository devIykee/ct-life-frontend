#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Dev builds only (World lane, W3): the demo-route waypoints (docs/design/demo-route.md), one standing spot per shot,
    /// baked by tools/world/islandgen/waypoints.py into Resources/World/Island/demo_route.json. Launch extra
    /// <c>islandWaypoints=4</c> teleports the player to shot 4's spot and leaves them there (to film that shot);
    /// <c>islandWaypoints=all</c> or <c>=1,3,6</c> visits each in turn, 10 s apiece (streaming and frame-rate pass).
    /// Each jump loads the cells round the spot first, then snaps the player and camera; the session's own streaming
    /// and room handoff take over from there. Log: <c>CTLIFE [island] waypoint 4 marina_promenade at (372, -284)</c>.
    /// </summary>
    public static class IslandWaypoints
    {
        public const string ResourcePath = "World/Island/demo_route";

        public sealed class Waypoint { public int shot; public string id, title; public float x, z, yaw; public float? y; }

        public static List<Waypoint> Load()
        {
            var ta = Resources.Load<TextAsset>(ResourcePath);
            if (ta == null) return new List<Waypoint>();
            return ((JArray)JObject.Parse(ta.text)["waypoints"]).Select(w => new Waypoint
            {
                shot = (int)w["shot"], id = (string)w["id"], title = (string)w["title"], x = (float)w["x"], z = (float)w["z"], yaw = (float)w["yaw"],
                y = w["y"] == null || w["y"].Type == JTokenType.Null ? (float?)null : (float)w["y"],
            }).ToList();
        }

        /// <summary>The waypoints an extra names: "all", one shot number or id, or a comma list.</summary>
        public static List<Waypoint> Select(List<Waypoint> all, string arg)
        {
            if (string.IsNullOrEmpty(arg) || arg == "all") return all;
            var picked = new List<Waypoint>();
            foreach (var part in arg.Split(','))
            {
                var t = part.Trim();
                var w = int.TryParse(t, out int n) ? all.FirstOrDefault(q => q.shot == n) : all.FirstOrDefault(q => q.id == t);
                if (w != null) picked.Add(w);
                else Log.Warn("island", "islandWaypoints: no waypoint " + t);
            }
            return picked;
        }

        public static async Task Jump(VenueSession s, Waypoint w, CancellationToken ct)
        {
            float ground = IslandStreamer.GroundHeight(w.x, w.z);
            var p = new Vector3(w.x, w.y ?? (float.IsNaN(ground) ? 0f : ground + 0.3f), w.z);
            await IslandStreamer.EnsureLoaded(p, 160f, ct);
            if (s.LocalPlayer == null) return;
            s.LocalPlayer.StopDirectInput();
            s.LocalPlayer.SnapTo(p, w.yaw * Mathf.Deg2Rad);
            CameraRig.Instance?.SnapToTarget();
            Log.Info("island", $"waypoint {w.shot} {w.id} at ({w.x:0}, {w.z:0}) y {p.y:0.0}");
        }

        public static async Task Run(VenueSession s, string arg, Func<bool> alive, CancellationToken ct)
        {
            var all = Load();
            if (all.Count == 0) { Log.Warn("island", "islandWaypoints: " + ResourcePath + " missing"); return; }
            var list = Select(all, arg);
            bool stay = list.Count == 1;
            foreach (var w in list)
            {
                if (!alive()) return;
                await Jump(s, w, ct);
                if (!stay) await Awaitable.WaitForSecondsAsync(10f, ct);
            }
            Log.Info("island", stay ? "islandWaypoints: holding at shot " + list[0].shot : "islandWaypoints done");
        }
    }
}
#endif
