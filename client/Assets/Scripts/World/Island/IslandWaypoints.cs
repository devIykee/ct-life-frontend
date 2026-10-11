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

        public sealed class Waypoint
        {
            public int shot; public string id, title; public float x, z, yaw; public float? y;
            public Vector3 subject; public float camDist = 4.5f, camSide, camHeight = 2f, fov = 52f;
        }

        public static List<Waypoint> Load()
        {
            var ta = Resources.Load<TextAsset>(ResourcePath);
            if (ta == null) return new List<Waypoint>();
            return ((JArray)JObject.Parse(ta.text)["waypoints"]).Select(w => new Waypoint
            {
                shot = (int)w["shot"], id = (string)w["id"], title = (string)w["title"], x = (float)w["x"], z = (float)w["z"], yaw = (float)w["yaw"],
                y = w["y"] == null || w["y"].Type == JTokenType.Null ? (float?)null : (float)w["y"],
                subject = w["subject"] is JArray sj ? new Vector3((float)sj[0], (float)sj[1], (float)sj[2]) : new Vector3((float)w["x"], 2f, (float)w["z"]),
                camDist = (float?)w["camera"]?["dist"] ?? 4.5f, camSide = (float?)w["camera"]?["side"] ?? 0f,
                camHeight = (float?)w["camera"]?["height"] ?? 2f, fov = (float?)w["camera"]?["fov"] ?? 52f,
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

        /// <summary>
        /// The shot's camera: 'camDist' m behind the player on the line to the subject, 'camSide' m to the right,
        /// 'camHeight' m above the feet, aimed at the subject (shared by the device shot hold and the editor capture).
        /// </summary>
        public static (Vector3 pos, Quaternion rot, float fov) CameraPose(Waypoint w, Vector3 feet)
        {
            var flat = new Vector3(w.subject.x - feet.x, 0f, w.subject.z - feet.z);
            var fwd = flat.sqrMagnitude > 0.01f ? flat.normalized : Quaternion.Euler(0f, w.yaw, 0f) * Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            var pos = feet - fwd * w.camDist + right * w.camSide + Vector3.up * w.camHeight;
            return (pos, Quaternion.LookRotation(w.subject - pos, Vector3.up), w.fov);
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

        /// <summary>
        /// `islandShot=&lt;shot|id&gt;`: the player on the shot's spot and the camera on its authored pose, held there until the
        /// player moves a step (then Feel's camera rig takes over from the same heading), so each demo shot is one tap.
        /// </summary>
        public static async Task Shot(VenueSession s, string arg, CancellationToken ct)
        {
            var list = Select(Load(), arg);
            if (list.Count == 0) { Log.Warn("island", "islandShot: no shot " + arg); return; }
            bool tour = list.Count > 1;                    // `all` or a list: each shot held 8 s (screenshots, frame rate)
            foreach (var w in list)
            {
                await Jump(s, w, ct);
                await Awaitable.WaitForSecondsAsync(0.4f, ct);
                var rig = CameraRig.Instance;
                if (rig == null || rig.Camera == null || s.LocalPlayer == null) return;
                var hold = rig.gameObject.GetComponent<ShotHold>() ?? rig.gameObject.AddComponent<ShotHold>();
                hold.Begin(rig, s.LocalPlayer.transform, w);
                Log.Info("island", $"islandShot {w.shot} {w.id}: camera held (fov {w.fov:0})");
                if (!tour) return;
                await Awaitable.WaitForSecondsAsync(8f, ct);
                hold.Stop();
            }
            Log.Info("island", "islandShot tour done");
        }

        /// <summary>Holds the camera on a shot pose; hands back to the rig once the player walks off the spot.</summary>
        public sealed class ShotHold : MonoBehaviour
        {
            private CameraRig _rig; private Transform _player; private Waypoint _w; private Vector3 _spot; private float _oldFov;

            public void Begin(CameraRig rig, Transform player, Waypoint w)
            {
                _rig = rig; _player = player; _w = w; _spot = player.position; _oldFov = rig.Camera.fieldOfView;
                rig.enabled = false;
                enabled = true;
            }

            private void LateUpdate()
            {
                if (_rig == null || _player == null) { enabled = false; return; }
                var (pos, rot, fov) = CameraPose(_w, _spot);
                var cam = _rig.Camera.transform;
                cam.SetPositionAndRotation(pos, rot);
                _rig.Camera.fieldOfView = fov;
                var moved = _player.position - _spot; moved.y = 0f;
                if (moved.magnitude > 1.0f) Release();
            }

            public void Stop() { if (enabled) Release(); }

            private void Release()
            {
                _rig.Camera.fieldOfView = _oldFov;
                _rig.yaw = _rig.Camera.transform.eulerAngles.y;
                _rig.enabled = true;
                _rig.SnapToTarget();
                enabled = false;
                Log.Info("island", $"islandShot {_w.id}: camera back to the rig");
            }
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
