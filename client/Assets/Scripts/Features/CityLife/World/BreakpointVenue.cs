using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.World;
using UnityEngine;

namespace CTLife.CityLife
{
    /// <summary>
    /// The Breakpoint 2026 London skin on the Conference Center at runtime (lane B, 2026-10-24, backend PR #11):
    /// - while <see cref="EventsService.SkinEvent"/> is Breakpoint (live or upcoming) and CT Island is loaded, the skin prefab
    ///   (<see cref="EventSkin"/>, built by BreakpointSkinBuilder) loads asynchronously and stands at the hall's pose; it is
    ///   live (booths, banners, bus, phone box, keynote stage, nods) during the event and shows the countdown totem before;
    ///   it goes on with `event_live` / a live GET /events/current and off with `event_ended`;
    /// - while live, the island's everyday CHECKPOINT 26 dressing and the hall's name sign are hidden (and given back after);
    /// - outdoor pieces drop onto the streamed ground; the countdowns tick once a second from the server's clock;
    /// - the World offers: Visit booth (bp26.booth_1..6), Spot it (bp26.nod_1..8, the photo moment), Play trivia (the kiosk
    ///   opens the Events app), Watch stream (only while `streamUrl` is set; it opens the browser, never an embed).
    /// One per signed-in player, owned by <see cref="EventsOverlays"/>; nothing runs when the player is far away.
    /// </summary>
    public sealed class BreakpointVenue : MonoBehaviour
    {
        public const float LoadDistance = 650f, ActiveDistance = 700f, TickDistance = 160f, NodAnimDistance = 90f;
        public const float BoothRange = 2.6f, NodRange = 3.2f, TotemRange = 2.4f;

        public static BreakpointVenue Current { get; private set; }
        /// <summary>A nod held still for the photo (it faces the photo camera instead of spinning).</summary>
        public static Transform Frozen;

        public EventSkin Skin => _skin;
        public EventsOverlays Ui => _ui;
        public bool Live => _skin != null && _skin.IsLive;

        private EventsService _svc;
        private EventsOverlays _ui;
        private EventSkin _skin;
        private bool _loading;
        private IslandWorld _world;
        private readonly List<GameObject> _hidden = new List<GameObject>();
        private Func<Vector3, WorldOffer> _offers;
        private float _nextTick, _nextHide, _nextSnap, _nextShell;
        /// <summary>The skin's TextMesh fonts (Geist, Geist Mono) and the depth-tested material for each.</summary>
        private readonly Dictionary<Font, Material> _textMats = new Dictionary<Font, Material>();
        private bool _fontHooked;
        private VenueShell _shell;
        private Renderer _facade;
        private readonly Transform[] _discs = new Transform[EventIds.NodCount];

        public static BreakpointVenue Create(EventsService svc, EventsOverlays ui)
        {
            var go = new GameObject("BreakpointVenue");
            DontDestroyOnLoad(go);
            var v = go.AddComponent<BreakpointVenue>();
            v._svc = svc; v._ui = ui;
            v._offers = v.Offer;
            WorldOffers.Add(v._offers);
            Current = v;
            return v;
        }

        private void OnDestroy()
        {
            WorldOffers.Remove(_offers);
            EventSkin.Restore(_hidden);
            if (_fontHooked) Font.textureRebuilt -= OnFontRebuilt;
            if (_skin != null) Destroy(_skin.gameObject);
            if (ReferenceEquals(Current, this)) Current = null;
        }

        private static Vector3 PlayerPos()
        {
            var s = VenueSession.Current;
            if (s != null && s.LocalPlayer != null) return s.LocalPlayer.transform.position;
            var cam = CameraRig.Instance != null ? CameraRig.Instance.Camera : Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        private void Update()
        {
            var world = IslandWorld.Current;
            var ev = _svc != null && !_svc.Disposed ? _svc.SkinEvent : null;
            if (world == null || ev == null || !ReferenceEquals(world, _world))
            {
                if (_skin != null || _hidden.Count > 0) Drop();
                _world = world;
                if (world == null || ev == null) return;
            }
            var hall = world.Find(EventIds.Venue);
            if (hall == null) return;
            var me = PlayerPos();
            float dist = InteractionFinder.Flat(me, hall.Position);
            if (_skin == null)
            {
                if (!_loading && dist < LoadDistance) _ = Load(world, hall);
                return;
            }
            bool active = dist < ActiveDistance;
            if (_skin.gameObject.activeSelf != active) _skin.gameObject.SetActive(active);
            if (!active) return;
            long now = _svc.NowMs;
            bool live = ev.IsLive(now);
            if (live != _skin.IsLive)
            {
                _skin.SetLive(live);
                if (!live) EventSkin.Restore(_hidden);
                _nextHide = 0f; _nextTick = 0f;
                Log.Info("events", "skin " + (live ? "live" : "upcoming") + " at the Conference Center");
            }
            float t = Time.unscaledTime;
            // cells stream in after the skin: hide their everyday dressing as they come, drop pieces onto their ground
            if (live && t >= _nextHide) { _nextHide = t + 2f; EventSkin.HideEveryday(world.transform, hall.Position, hall.venueId, _hidden); }
            if (t >= _nextSnap && _skin.snapped != null && Array.IndexOf(_skin.snapped, false) >= 0)
            {
                _nextSnap = t + 1f;
                _skin.SnapAll((x, z) => IslandStreamer.GroundHeight(x, z));
            }
            if (dist < TickDistance && t >= _nextTick) { _nextTick = t + 1f; TickCountdowns(ev, now, live); }
            if (live && dist < NodAnimDistance) AnimateNods(t);
            // the facade's banners and fascia would stand between the camera and the room while the player is inside
            if (_shell == null && t >= _nextShell) { _nextShell = t + 2f; _shell = FindShell(world); }
            if (_facade != null && _shell != null)
            {
                bool inside = _shell.Contains(me);
                if (_facade.enabled == inside) _facade.enabled = !inside;
            }
        }

        private async Task Load(IslandWorld world, IslandVenue hall)
        {
            _loading = true;
            try
            {
                var req = Resources.LoadAsync<GameObject>(EventSkin.PrefabResource);
                while (!req.isDone) await Awaitable.NextFrameAsync();
                var prefab = req.asset as GameObject;
                if (prefab == null) { Log.Warn("events", "no skin prefab " + EventSkin.PrefabResource); return; }
                if (this == null || IslandWorld.Current != world) return;
                var op = UnityEngine.Object.InstantiateAsync(prefab, world.transform, hall.Position, hall.Rotation);
                while (!op.isDone) await Awaitable.NextFrameAsync();
                var go = op.Result[0];
                if (this == null || IslandWorld.Current != world) { Destroy(go); return; }
                go.name = "Breakpoint_london_2026";
                _skin = go.GetComponent<EventSkin>();
                _skin.SetLive(false);     // Update switches it to live on its first frame when the event is on
                foreach (var tm in go.GetComponentsInChildren<TextMesh>(true))
                {
                    var r = tm.GetComponent<MeshRenderer>();
                    var m = TextMaterial(tm.font);
                    if (m != null && r != null) r.sharedMaterial = m;
                }
                for (int i = 0; i < _skin.nods.Length && i < _discs.Length; i++) _discs[i] = _skin.nods[i] != null ? _skin.nods[i].Find("disc") : null;
                var front = _skin.live.transform.Find("front");
                _facade = front != null ? front.GetComponent<Renderer>() : null;
                _nextHide = 0f; _nextSnap = 0f; _nextTick = 0f;
                Log.Info("events", "skin loaded at the Conference Center " + hall.Position);
                _ui?.PrewarmPhoto(_skin.transform);
            }
            catch (Exception e) { Log.Exception("events", e); }
            finally { _loading = false; }
        }

        private VenueShell FindShell(IslandWorld world)
        {
            foreach (var s in world.GetComponentsInChildren<VenueShell>(false)) if (s.venueId == EventIds.Venue) return s;
            return null;
        }

        /// <summary>The countdowns are TextMeshes on dynamic fonts: every glyph they use is asked for before the first tick so
        /// the font atlas never grows (and re-uploads) mid-play; each font gets the depth-tested painted-text material.</summary>
        private Material TextMaterial(Font font)
        {
            if (font == null) return null;
            if (_textMats.TryGetValue(font, out var m)) return m;
            font.RequestCharactersInTexture("0123456789:d ·LIVEDAYOFNSTRAIGHTBKPUOWCMXabcdefghijklmnopqrstuvwxyz_—", 96, FontStyle.Normal);
            var sh = Resources.Load<Shader>("World/Furniture/Shaders/PaintedText");
            m = sh != null ? new Material(sh) { name = "bp26_text_" + font.name, mainTexture = font.material.mainTexture } : null;
            _textMats[font] = m;
            if (!_fontHooked) { Font.textureRebuilt += OnFontRebuilt; _fontHooked = true; }
            return m;
        }

        private void OnFontRebuilt(Font f)
        {
            if (f != null && _textMats.TryGetValue(f, out var m) && m != null) m.mainTexture = f.material.mainTexture;
        }

        private void TickCountdowns(CalendarEvent ev, long now, bool live)
        {
            if (live)
            {
                var (day, days) = ev.Day(now);
                Set(_skin.countdownCaption, $"LIVE · DAY {day} OF {days} · ENDS IN");
                Set(_skin.countdown, EventWire.Countdown(ev.EndsAtMs - now));
                // the IRL board on the plaza: the newest listed attendees (aliases only)
                if (_skin.irlLines != null && _svc.Breakpoint?.Event?.IrlCheckin == true)
                {
                    _ = _svc.LoadIrlBoard();
                    var names = new System.Text.StringBuilder();
                    int n = 0;
                    foreach (var a in _svc.IrlBoard) { if (n++ >= 6) break; if (names.Length > 0) names.Append('\n'); names.Append(a.Alias); }
                    Set(_skin.irlLines, n == 0 ? "Be the first:\ncheck in from\nthe Events app" : names.ToString());
                }
            }
            else
            {
                Set(_skin.upcomingCaption, "STARTS IN");
                Set(_skin.upcomingCountdown, EventWire.Countdown(ev.StartsAtMs - now));
            }
        }

        private static void Set(TextMesh tm, string s) { if (tm != null && tm.text != s) tm.text = s; }

        private void AnimateNods(float t)
        {
            for (int i = 0; i < _discs.Length; i++)
            {
                var d = _discs[i];
                if (d == null) continue;
                bool spotted = _svc.HasDone(EventIds.Nod(i));
                if (d.gameObject.activeSelf == spotted) d.gameObject.SetActive(!spotted);
                if (spotted || d == Frozen) continue;
                d.localPosition = new Vector3(0f, 1.15f + Mathf.Sin(t * 1.6f + i) * 0.06f, 0f);
                d.localRotation = Quaternion.Euler(0f, t * 40f + i * 47f, 0f);
            }
        }

        // ------------------------------------------------------------------ World offers

        private WorldOffer Offer(Vector3 player)
        {
            if (_skin == null || !_skin.gameObject.activeInHierarchy) return null;
            if (!_skin.IsLive)
            {
                var totem = _skin.upcoming.transform.Find("countdown_totem");
                if (totem == null || InteractionFinder.Flat(player, totem.position) > TotemRange + 1f) return null;
                return new WorldOffer
                {
                    id = "bp26.totem", verbKey = "interact.events", icon = "duo-flag", tagKey = "interact.tag.breakpoint", primary = false,
                    position = totem.position + Vector3.up * 2.9f, range = TotemRange + 1f, perform = () => EventsService.Opener?.Invoke(EventsService.AppId),
                };
            }
            WorldOffer best = null;
            float bestD = float.MaxValue;
            void Consider(WorldOffer o, Vector3 at)
            {
                float d = InteractionFinder.Flat(player, at);
                if (d <= o.range && d < bestD) { best = o; bestD = d; }
            }
            for (int i = 0; i < _skin.booths.Length; i++)
            {
                var b = _skin.booths[i];
                if (b == null) continue;
                int k = i;
                Consider(new WorldOffer
                {
                    id = EventIds.Booth(i), verbKey = "interact.visitBooth", icon = "buy",
                    tagKey = _svc.HasDone(EventIds.Booth(i)) ? "interact.tag.visited" : "interact.tag.booth" + (i + 1), position = b.position + Vector3.up * 1.9f, range = BoothRange,
                    perform = () => _ui.VisitBooth(k),
                }, b.position);
            }
            for (int i = 0; i < _skin.nods.Length; i++)
            {
                var n = _skin.nods[i];
                if (n == null || _svc.HasDone(EventIds.Nod(i))) continue;
                int k = i;
                Consider(new WorldOffer
                {
                    id = EventIds.Nod(i), verbKey = "interact.spotIt", icon = "face", tagKey = "interact.tag.nod",
                    position = n.position + Vector3.up * 1.75f, range = NodRange, perform = () => _ui.SpotNod(k, _discs[k]),
                }, n.position);
            }
            if (_skin.kiosk != null)
                Consider(new WorldOffer
                {
                    id = "bp26.kiosk", verbKey = "interact.playTrivia", icon = "rich_list", tagKey = "interact.tag.trivia",
                    position = _skin.kiosk.position + Vector3.up * 2.1f, range = TotemRange, perform = () => _ui.OpenTrivia(),
                }, _skin.kiosk.position);
            if (_skin.irlBoard != null && _svc.Breakpoint?.Event?.IrlCheckin == true)
                Consider(new WorldOffer
                {
                    id = "bp26.irl", verbKey = "interact.irlBoard", icon = "contacts", tagKey = "interact.tag.irl", primary = false,
                    position = _skin.irlBoard.position + Vector3.up * 3.0f, range = TotemRange + 0.6f, perform = () => EventsService.Opener?.Invoke(EventsService.AppId),
                }, _skin.irlBoard.position);
            var url = _svc.Breakpoint?.Event?.StreamUrl;
            if (_skin.streamPost != null && url != null)
                Consider(new WorldOffer
                {
                    id = "bp26.stream", verbKey = "interact.watchStream", icon = "calls", tagKey = "interact.tag.stream",
                    position = _skin.streamPost.position + Vector3.up * 2.1f, range = TotemRange, perform = () => _ui.OpenStream(url),
                }, _skin.streamPost.position);
            return best;
        }

        // ------------------------------------------------------------------ helpers for the dev route

        public Transform Booth(int i) => _skin != null && i >= 0 && i < _skin.booths.Length ? _skin.booths[i] : null;
        public Transform Nod(int i) => _skin != null && i >= 0 && i < _skin.nods.Length ? _skin.nods[i] : null;
        public Transform NodDisc(int i) => i >= 0 && i < _discs.Length ? _discs[i] : null;

        private void Drop()
        {
            EventSkin.Restore(_hidden);
            if (_skin != null) Destroy(_skin.gameObject);
            _skin = null; _shell = null; _facade = null;
            Array.Clear(_discs, 0, _discs.Length);
        }
    }
}
