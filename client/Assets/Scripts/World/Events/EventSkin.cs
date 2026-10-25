using System;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// A real-world event's dressing on an island venue (lane B, 2026-10-24: Breakpoint 2026, skin `london_2026` on the
    /// Conference Center). The prefab is built in the editor (CTLife.EditorTools.Events.BreakpointSkinBuilder) in the venue's
    /// local frame and placed at the venue's pose; City Life (BreakpointVenue) switches it between <see cref="live"/> (the
    /// London skin, booths, the keynote stage, the nods) and <see cref="upcoming"/> (the countdown sign) and reads the anchors.
    /// Outdoor pieces in <see cref="grounded"/> drop onto the streamed ground (terrain, kerbs, paving) once it is there.
    /// </summary>
    public sealed class EventSkin : MonoBehaviour
    {
        public const string PrefabResource = "World/Events/bp26_london";
        public const string PrefabAssetPath = "Assets/Resources/" + PrefabResource + ".prefab";

        public string skinId = "london_2026";
        public string venueId = "conference_hall";
        public GameObject live, upcoming;
        [Tooltip("Interactable stands, in proof-id order (bp26.booth_1..6): the prompt floats at the anchor.")]
        public Transform[] booths = new Transform[0];
        [Tooltip("Hidden event nods around the park (bp26.nod_1..8).")]
        public Transform[] nods = new Transform[0];
        public Transform kiosk, streamPost, stageScreen;
        public TextMesh countdown, countdownCaption, upcomingCountdown, upcomingCaption;
        [Tooltip("The IRL attendee board on the plaza (live): its prompt anchor and the names (one per line).")]
        public Transform irlBoard;
        public TextMesh irlLines;
        [Tooltip("Outdoor pieces whose origin should sit on the ground under them.")]
        public Transform[] grounded = new Transform[0];
        [Tooltip("Per grounded piece: has it found the ground yet?")]
        public bool[] snapped = new bool[0];

        public bool IsLive { get; private set; }

        public void SetLive(bool on)
        {
            IsLive = on;
            if (live != null && live.activeSelf != on) live.SetActive(on);
            if (upcoming != null && upcoming.activeSelf == on) upcoming.SetActive(!on);
        }

        /// <summary>Drops every grounded piece that has not landed yet; returns how many stand on ground now.</summary>
        public int SnapAll(Func<float, float, float> fallbackHeight = null)
        {
            if (snapped.Length != grounded.Length) snapped = new bool[grounded.Length];
            int n = 0;
            for (int i = 0; i < grounded.Length; i++)
            {
                var t = grounded[i];
                if (t == null) continue;
                if (!snapped[i]) snapped[i] = Snap(t, fallbackHeight);
                if (snapped[i]) n++;
            }
            return n;
        }

        /// <summary>The island's everyday CHECKPOINT 26 dressing in front of the hall (cell meshes under "Roads"), hidden
        /// while the skin is live so the two sets of banners never stand in each other.</summary>
        public static readonly string[] EverydayDressing = { "event_banners", "sign_post_checkpoint", "timber", "lighthouse" };

        /// <summary>
        /// Hides the everyday dressing near <paramref name="hall"/> in the loaded cells under <paramref name="island"/> and the
        /// venue's own name sign; adds what it hid to <paramref name="hidden"/> (skips what is already there).
        /// </summary>
        public static int HideEveryday(Transform island, Vector3 hall, string venueId, System.Collections.Generic.List<GameObject> hidden, float radius = 32f)
        {
            if (island == null) return 0;
            int n = 0;
            for (int i = 0; i < island.childCount; i++)
            {
                var roads = island.GetChild(i).Find("Roads");
                if (roads == null) continue;
                foreach (var name in EverydayDressing)
                {
                    var t = roads.Find(name);
                    if (t == null || !t.gameObject.activeSelf) continue;
                    var r = t.GetComponent<Renderer>();
                    if (r == null) continue;
                    var b = r.bounds;
                    // only pieces that are all near the hall (a cell-wide merged mesh would be much bigger)
                    if (b.size.magnitude > 30f || Vector2.Distance(new Vector2(b.center.x, b.center.z), new Vector2(hall.x, hall.z)) > radius) continue;
                    t.gameObject.SetActive(false);
                    hidden.Add(t.gameObject);
                    n++;
                }
            }
            foreach (var shell in island.GetComponentsInChildren<VenueShell>(false))
            {
                if (shell.venueId != venueId || shell.sign == null || !shell.sign.gameObject.activeSelf) continue;
                shell.sign.gameObject.SetActive(false);
                hidden.Add(shell.sign.gameObject);
                n++;
            }
            return n;
        }

        public static void Restore(System.Collections.Generic.List<GameObject> hidden)
        {
            foreach (var g in hidden) if (g != null) g.SetActive(true);
            hidden.Clear();
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        /// <summary>The highest ground under the piece (not the skin itself, not triggers), within 25 m above / 40 m below.</summary>
        public bool Snap(Transform t, Func<float, float, float> fallbackHeight = null)
        {
            var p = t.position;
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 25f, p.z), Vector3.down, Hits, 65f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var c = Hits[i].collider;
                if (c == null || c.transform.IsChildOf(transform) || c.attachedRigidbody != null) continue;
                if (Hits[i].point.y > best) best = Hits[i].point.y;
            }
            if (float.IsNegativeInfinity(best) && fallbackHeight != null)
            {
                float h = fallbackHeight(p.x, p.z);
                if (!float.IsNaN(h)) best = h;
            }
            if (float.IsNegativeInfinity(best)) return false;
            t.position = new Vector3(p.x, best, p.z);
            return true;
        }
    }
}
