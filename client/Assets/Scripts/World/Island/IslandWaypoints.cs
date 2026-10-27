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

        /// <summary>
        /// `signTour=<venueId,...|all>` (dev builds, 2026-10-26 names pass): for each venue in Resources/World/Island/sign_route.json
        /// (written by tools/world/islandgen/signs.py), loads the cells round it, stands the player 9 m in front of its shell's
        /// sign board and holds the camera on the board for 6 s. Log: <c>CTLIFE [island] signTour perp_pit held</c>.
        /// </summary>
        /// <summary>How far in front of a board the tour stands (m): the distance a player passes a building at.</summary>
        public static float Dist = 9f;

        public static async Task SignTour(VenueSession s, string arg, CancellationToken ct)
        {
            var ta = Resources.Load<TextAsset>("World/Island/sign_route");
            if (ta == null) { Log.Warn("island", "signTour: sign_route missing"); return; }
            var want = string.IsNullOrEmpty(arg) || arg == "all" ? null : new HashSet<string>(arg.Split(','));
            foreach (var v in (JArray)JObject.Parse(ta.text)["venues"])
            {
                var id = (string)v["id"];
                if (want != null && !want.Contains(id)) continue;
                var at = new Vector3((float)v["x"], 0f, (float)v["z"]);
                await IslandStreamer.EnsureLoaded(at, 160f, ct);
                Renderer sign = null; VenueShell shell = null;
                for (int tries = 0; tries < 20 && sign == null; tries++)     // a far cell can take a few seconds to stream in
                {
                    await Awaitable.WaitForSecondsAsync(0.5f, ct);
                    shell = UnityEngine.Object.FindObjectsByType<VenueShell>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .Where(x => x.venueId == id && x.sign != null)
                        .OrderBy(x => (new Vector2(x.origin.x, x.origin.z) - new Vector2(at.x, at.z)).sqrMagnitude).FirstOrDefault();
                    // landmark shells: the board is the renderer drawn with the sign_<id> material
                    sign = shell != null ? shell.sign : UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("sign_" + id, StringComparison.Ordinal));
                    if (sign == null && tries == 6 && s.LocalPlayer != null)    // still nothing: stand near it so the streamer keeps its cell
                        { float gh = IslandStreamer.GroundHeight(at.x, at.z); s.LocalPlayer.SnapTo(new Vector3(at.x, float.IsNaN(gh) ? 2f : gh + 0.3f, at.z), 0f); }
                }
                var mf = sign != null ? sign.GetComponent<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null || s.LocalPlayer == null) { Log.Warn("island", "signTour " + id + ": no shell sign loaded"); continue; }
                // the mesh's own bounds (a faded or culled renderer reports empty world bounds)
                var b = new Bounds(sign.transform.TransformPoint(mf.sharedMesh.bounds.center), Vector3.one);
                var n = mf.sharedMesh.normals.Length > 0 ? sign.transform.TransformDirection(mf.sharedMesh.normals[0]) : Vector3.forward;
                n.y = 0f; n = n.sqrMagnitude > 0.01f ? n.normalized : Vector3.forward;
                // face out of the building: some boards are wound inward (the tour then framed the street behind it)
                var hub = shell != null ? shell.origin : new Vector3(at.x, b.center.y, at.z);
                var outv = b.center - hub; outv.y = 0f;
                if (outv.sqrMagnitude > 0.25f && Vector3.Dot(n, outv) < 0f) n = -n;
                var feet = b.center + n * Dist;
                float ground = IslandStreamer.GroundHeight(feet.x, feet.z);
                var w = new Waypoint
                {
                    shot = 0, id = "sign_" + id, title = id, x = feet.x, z = feet.z, y = float.IsNaN(ground) ? (float?)null : ground + 0.3f,
                    yaw = Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg, subject = b.center, camDist = 1.5f, camSide = 1.2f, camHeight = 1.7f, fov = 50f,
                };
                await Jump(s, w, ct);
                // the spot may sit inside another venue's room (the boards round the Timeline plaza face it): let the room
                // handoff settle, then stand there again
                await Awaitable.WaitForSecondsAsync(3.0f, ct);
                if (VenueSession.Current != null) s = VenueSession.Current;
                await Jump(s, w, ct);
                await Awaitable.WaitForSecondsAsync(0.4f, ct);
                var rig = CameraRig.Instance;
                if (rig == null || rig.Camera == null || s.LocalPlayer == null) return;
                var hold = rig.gameObject.GetComponent<ShotHold>() ?? rig.gameObject.AddComponent<ShotHold>();
                hold.Begin(rig, s.LocalPlayer.transform, w);
                Log.Info("island", $"signTour {id} held (board {b.center.x:0},{b.center.z:0} facing {n.x:0.0},{n.z:0.0}; route {at.x:0},{at.z:0})");
                await Awaitable.WaitForSecondsAsync(6f, ct);
                hold.Stop();
            }
            Log.Info("island", "signTour done");
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
