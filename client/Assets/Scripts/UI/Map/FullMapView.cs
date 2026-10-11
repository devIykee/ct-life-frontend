using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;
using CTLife.UI.Kit;
using CTLife.Net;

namespace CTLife.UI.Map
{
    /// <summary>
    /// The full map (one component for the Duo Map app and the in-city full-screen map): CT Island tilted back like a painted
    /// relief (ref 8), every venue's baked map in its real place, upright pill labels, pinch/drag/wheel with clamping, a zoom
    /// rail, recentre, your live position (pulsing dot + heading) and the other players in your room. Tap a venue (or its label)
    /// to fly to it and open its card; Go travels with the game's existing travel.
    /// </summary>
    public sealed class FullMapView : VisualElement
    {
        public enum Look { Duo, City }

        public Action<string> Travel;          // venue id
        public Action OpenHome;                // the player's own home (Home app)
        public Func<HashSet<string>> Available; // venues that can be travelled to now
        public Action Close;
        public const float Tilt = 0.5f;         // ~29 degrees

        private readonly Look _look;
        private readonly VisualElement _viewport, _labels, _markers, _me, _meCone, _card, _railThumb, _rail, _results;
        private readonly MapSurface _surface;
        private MapView _view;
        private bool _ready;
        private float _minPpm = 1f, _maxPpm = 26f;
        private readonly Dictionary<int, Vector2> _pointers = new Dictionary<int, Vector2>();
        private Vector2 _downAt; private float _moved; private bool _pinched;
        private IVisualElementScheduledItem _fly, _tick;
        private sealed class Pin { public VisualElement E; public Vector2 Atlas; public float Min, Max; public int Priority; public bool Centered; public Vector2 Size; }
        private readonly List<Pin> _pins = new List<Pin>();
        private readonly List<Rect> _shown = new List<Rect>();
        /// <summary>Venue labels take over from district labels at this zoom (pixels per metre).</summary>
        public const float VenueLabelsFrom = 2.6f;
        private readonly List<MapMarker> _live = new List<MapMarker>();
        private readonly List<VisualElement> _livePool = new List<VisualElement>();
        private string _selected;

        public MapView View => _view;
        public string Selected => _selected;

        public FullMapView(Look look)
        {
            _look = look;
            AddToClassList("map-full");
            AddToClassList(look == Look.Duo ? "map-full--duo" : "map-full--city");
            name = "map-full";
            _viewport = new VisualElement { name = "map-viewport" };
            _viewport.AddToClassList("map-viewport");
            _surface = new MapSurface { Project = p => _view.Project(p) };
            _surface.AddToClassList("map-fill");
            _markers = new VisualElement { pickingMode = PickingMode.Ignore };
            _markers.AddToClassList("map-fill");
            _labels = new VisualElement { pickingMode = PickingMode.Ignore };
            _labels.AddToClassList("map-fill");
            _meCone = new VisualElement { pickingMode = PickingMode.Ignore, name = "map-me-cone" };
            _meCone.AddToClassList("map-full-cone");
            _meCone.generateVisualContent += DrawCone;
            _me = new VisualElement { pickingMode = PickingMode.Ignore, name = "map-me" };
            _me.AddToClassList("map-full-me");
            _me.Add(new VisualElement { pickingMode = PickingMode.Ignore }.With("map-full-me-pulse"));
            _me.Add(new VisualElement { pickingMode = PickingMode.Ignore }.With("map-full-me-dot"));
            _viewport.Add(_surface);
            _viewport.Add(_markers);
            _viewport.Add(_meCone);
            _viewport.Add(_me);
            _viewport.Add(_labels);
            Add(_viewport);

            // Controls: zoom rail (ref 6) with + / -, recentre; the search (Duo) or title + close (city).
            var plus = Control("map-zoom-in", "+", () => ZoomBy(1.7f));
            var minus = Control("map-zoom-out", "−", () => ZoomBy(1f / 1.7f));
            _railThumb = new VisualElement { pickingMode = PickingMode.Ignore }.With("map-rail-thumb");
            _rail = new VisualElement { name = "map-rail" }.With("map-rail");
            _rail.Add(_railThumb);
            _rail.RegisterCallback<PointerDownEvent>(e => { RailTo(e.localPosition.y); _rail.CapturePointer(e.pointerId); e.StopPropagation(); });
            _rail.RegisterCallback<PointerMoveEvent>(e => { if (_rail.HasPointerCapture(e.pointerId)) { RailTo(e.localPosition.y); e.StopPropagation(); } });
            _rail.RegisterCallback<PointerUpEvent>(e => { if (_rail.HasPointerCapture(e.pointerId)) _rail.ReleasePointer(e.pointerId); });
            var zoom = new VisualElement().With("map-zoom");
            zoom.Add(plus); zoom.Add(_rail); zoom.Add(minus);
            var recentre = Control("map-recentre", null, Recentre, "center-direction");
            var side = new VisualElement { pickingMode = PickingMode.Ignore }.With("map-side");
            side.Add(zoom);
            side.Add(recentre);
            Add(side);

            var top = new VisualElement { pickingMode = PickingMode.Ignore }.With("map-top");
            if (look == Look.Duo)
            {
                var search = new DuoSearchField(Strings.T("map.search"), Search);
                search.name = "map-search";
                search.AddToClassList("map-search");
                top.Add(search);
                _results = new VisualElement { name = "map-results" }.With("map-results");
                _results.style.display = DisplayStyle.None;
                top.Add(_results);
            }
            else
            {
                var title = LobbyArt.Outlined(MapCatalog.Atlas.Name ?? "CT Island", "map-city-title");
                top.Add(title);
                var close = Control("map-close", null, () => Close?.Invoke(), "multiply");
                close.AddToClassList("map-close");
                Add(close);
            }
            Add(top);

            _card = new VisualElement { name = "map-card" }.With("map-card");
            _card.style.display = DisplayStyle.None;
            Add(_card);

            _viewport.RegisterCallback<PointerDownEvent>(OnDown);
            _viewport.RegisterCallback<PointerMoveEvent>(OnMove);
            _viewport.RegisterCallback<PointerUpEvent>(OnUp);
            _viewport.RegisterCallback<PointerCancelEvent>(e => { _pointers.Remove(e.pointerId); });
            _viewport.RegisterCallback<WheelEvent>(e => { ZoomAround(e.localMousePosition, Mathf.Pow(1.0015f, -e.delta.y * 40f)); e.StopPropagation(); });
            _viewport.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            RegisterCallback<AttachToPanelEvent>(_ => { _tick ??= schedule.Execute(Tick).Every(0); });
            BuildLayers();
            BuildPins();
        }

        // ------------------------------------------------------------------ data

        private void BuildLayers()
        {
            var atlas = MapCatalog.Atlas;
            var layers = new List<MapSurface.Layer>();
            var tex = MapCatalog.Texture(atlas.Texture);
            if (tex != null) layers.Add(new MapSurface.Layer { Texture = tex, AtlasRect = atlas.Rect, Alpha = 1f, Grid = 32 });
            foreach (var p in atlas.Placements.Values)
                if (MapCatalog.Areas.TryGetValue(p.AreaId, out var a))
                    layers.Add(new MapSurface.Layer { Texture = MapCatalog.Texture(a.Texture), AtlasRect = MapSurface.AreaAtlasRect(a, p), Alpha = 1f, Grid = 8 });
            _surface.SetLayers(layers);
        }

        private void BuildPins()
        {
            var atlas = MapCatalog.Atlas;
            foreach (var plot in atlas.Plots)
            {
                var pill = Pill(plot.Reserved ? Strings.T("map.comingSoon") : plot.Name, plot.Reserved ? "crane" : null, "map-pill--district");
                pill.name = "map-plot-" + plot.Id;
                var id = plot.Id;
                pill.clicked += () => SelectPlot(id);
                _labels.Add(pill);
                _pins.Add(new Pin { E = pill, Atlas = plot.Center, Min = 0f, Max = plot.Reserved ? 99f : VenueLabelsFrom, Priority = plot.Reserved ? 2 : 0, Centered = true });
            }
            foreach (var p in atlas.Placements.Values)
            {
                if (!MapCatalog.Areas.TryGetValue(p.AreaId, out var a)) continue;
                var pill = Pill(Title(a), a.IsHome ? "home-page" : "place-marker", "map-pill--venue");
                pill.name = "map-venue-" + a.Id;
                var id = a.Id;
                pill.clicked += () => Select(id, fly: true);
                _labels.Add(pill);
                var r = MapSurface.AreaAtlasRect(a, p);
                _pins.Add(new Pin { E = pill, Atlas = new Vector2(r.center.x, r.yMax - 1f), Min = VenueLabelsFrom, Max = 99f, Priority = 1 });
                // The door of every venue (registry spots) once you are close enough to read it.
                if (!a.IsHome && MapMarkers.RegistryDoor(a.VenueId, p, out var door))
                {
                    var d = new VisualElement { pickingMode = PickingMode.Ignore }.With("map-marker").With("map-marker--door");
                    Minimap.Dress(d, MarkerKind.Door);
                    _markers.Add(d);
                    _pins.Add(new Pin { E = d, Atlas = door, Min = 7f, Max = 99f, Priority = 3, Centered = true });
                }
            }
        }

        private static string Title(MapArea a)
        {
            if (a.IsHome) return HomeTitle(a.HomeId);
            if (Services.TryGet<ConfigStore>(out var cfg)) { var v = cfg.Registries.Venue(a.VenueId); if (v != null && !string.IsNullOrEmpty(v.name)) return v.name; }
            return a.Title ?? a.Id;
        }

        private static string HomeTitle(string homeId)
        {
            if (Services.TryGet<ConfigStore>(out var cfg)) { var h = cfg.Registries.Homes.FirstOrDefault(x => x.id == homeId); if (h != null) return h.name; }
            return homeId;
        }

        private static string OwnHome() => Services.TryGet<PlayerProfile>(out var p) ? p.Me?.HomeId : null;

        // ------------------------------------------------------------------ view

        private void Layout()
        {
            var size = _viewport.contentRect.size;
            if (size.x < 10 || size.y < 10) return;
            _view.Viewport = size;
            _view.Tilt = Tilt;
            var atlas = MapCatalog.Atlas;
            _minPpm = Mathf.Min(size.x / (atlas.Size.x * 0.86f), size.y / (atlas.Size.y * 0.78f));
            _maxPpm = 26f;
            if (!_ready)
            {
                _ready = true;
                if (TryPlayer(out var me, out _)) { _view.Center = me; _view.PixelsPerMeter = 12f; }
                else { _view.Center = atlas.Rect.center; _view.PixelsPerMeter = _minPpm; }
            }
            ApplyView();
        }

        /// <summary>Captures and tests: set the view directly.</summary>
        public void SetView(Vector2 center, float ppm) { _view.Center = center; _view.PixelsPerMeter = ppm; ApplyView(); }
        public float MinPpm => _minPpm;
        public float MaxPpm => _maxPpm;

        private void ApplyView()
        {
            var atlas = MapCatalog.Atlas;
            var island = new Rect(atlas.Min + atlas.Size * 0.08f, atlas.Size * 0.84f);
            _view.Clamp(island, _minPpm, _maxPpm);
            _surface.Refresh();
            // Place pins, then declutter greedily by priority (districts, venues, reserved lots, doors): a label that would
            // overlap one already shown is hidden, so names never pile up.
            _shown.Clear();
            foreach (var pin in _pins.OrderBy(p => p.Priority))
            {
                bool on = _view.PixelsPerMeter >= pin.Min && _view.PixelsPerMeter < pin.Max;
                var px = _view.Project(pin.Atlas);
                bool inside = px.x > -200 && px.y > -200 && px.x < _view.Viewport.x + 200 && px.y < _view.Viewport.y + 200;
                if (on && inside)
                {
                    if (pin.E.layout.width > 1f) pin.Size = pin.E.layout.size; // hidden pins keep their last measured size
                    var size = pin.Size;
                    if (size.x > 1f)
                    {
                        var rect = pin.Centered ? new Rect(px - size / 2f, size) : new Rect(px.x - size.x / 2f, px.y - size.y, size.x, size.y);
                        var pad = new Rect(rect.x - 6f, rect.y - 4f, rect.width + 12f, rect.height + 8f);
                        if (_shown.Any(r => r.Overlaps(pad))) on = false;
                        else _shown.Add(pad);
                    }
                }
                pin.E.style.display = on && inside ? DisplayStyle.Flex : DisplayStyle.None;
                if (on && inside) { pin.E.style.left = px.x; pin.E.style.top = px.y; } // USS translate centres each pin on its anchor
            }
            float t = Mathf.InverseLerp(Mathf.Log(_minPpm), Mathf.Log(_maxPpm), Mathf.Log(_view.PixelsPerMeter));
            _railThumb.style.top = Length.Percent((1f - t) * 100f);
            PlaceLive();
        }

        private void Tick()
        {
            if (!_ready || panel == null) return;
            PlaceLive();
            float pulse = 1f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f));
            var ring = _me.ElementAt(0);
            ring.style.scale = new Scale(new Vector3(pulse, pulse, 1f));
            ring.style.opacity = 1.4f - pulse;
        }

        private bool TryPlayer(out Vector2 atlas, out Vector2 headingDir)
        {
            atlas = default; headingDir = Vector2.up;
            if (PlayerOverride != null) { var o = PlayerOverride(); if (o == null) return false; atlas = o.Value.atlas; headingDir = o.Value.dir; return true; }
            var s = VenueSession.Current;
            if (s == null || s.Venue == null || s.LocalPlayer == null) return false;
            var area = MapCatalog.ForVisit(s.VenueId, s.HomeId);
            var p = MapCatalog.PlacementOf(area);
            if (area == null || p == null) return false;
            var t = s.LocalPlayer.transform;
            atlas = MapMath.AtlasFromLocal(p, s.Venue.ToLocal(t.position));
            var f = s.Venue.transform.InverseTransformDirection(t.forward);
            headingDir = MapMath.Rotate(new Vector2(f.x, f.z).normalized, p.RotationDeg * Mathf.Deg2Rad);
            return true;
        }

        /// <summary>Captures: a driven player position (atlas metres) and facing.</summary>
        public Func<(Vector2 atlas, Vector2 dir)?> PlayerOverride;

        private void PlaceLive()
        {
            bool here = TryPlayer(out var me, out var dir);
            _me.style.display = _meCone.style.display = here ? DisplayStyle.Flex : DisplayStyle.None;
            if (here)
            {
                var px = _view.Project(me);
                _me.style.translate = new Translate(px.x, px.y);
                var ahead = _view.Project(me + dir * 3f) - px;
                _meCone.style.translate = new Translate(px.x, px.y);
                _meCone.style.rotate = new Rotate(Mathf.Atan2(ahead.x, -ahead.y) * Mathf.Rad2Deg);
            }
            // Other players in your room as small avatars (only at venue zoom, where they are distinguishable).
            var s = VenueSession.Current;
            var area = s != null ? MapCatalog.ForVisit(s.VenueId, s.HomeId) : null;
            MapMarkers.Live(s, area, MapCatalog.PlacementOf(area), _live);
            int used = 0;
            if (_view.PixelsPerMeter >= 5f)
                foreach (var m in _live)
                {
                    if (m.Kind != MarkerKind.Player && m.Kind != MarkerKind.Friend) continue;
                    while (_livePool.Count <= used)
                    {
                        var e = new Label { pickingMode = PickingMode.Ignore };
                        e.AddToClassList("map-avatar");
                        _markers.Add(e);
                        _livePool.Add(e);
                    }
                    var av = (Label)_livePool[used++];
                    av.style.display = DisplayStyle.Flex;
                    av.EnableInClassList("map-avatar--friend", m.Kind == MarkerKind.Friend);
                    av.text = string.IsNullOrEmpty(m.Label) ? "?" : System.Globalization.StringInfo.GetNextTextElement(m.Label.Trim().Length > 0 ? m.Label.Trim() : "?").ToUpperInvariant();
                    var px = _view.Project(m.Atlas);
                    av.style.translate = new Translate(px.x, px.y);
                }
            for (int i = used; i < _livePool.Count; i++) _livePool[i].style.display = DisplayStyle.None;
        }

        // ------------------------------------------------------------------ gestures

        private void OnDown(PointerDownEvent e)
        {
            _pointers[e.pointerId] = e.localPosition;
            _viewport.CapturePointer(e.pointerId);
            if (_pointers.Count == 1) { _downAt = e.localPosition; _moved = 0f; _pinched = false; }
            StopFly();
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (!_pointers.TryGetValue(e.pointerId, out var prev)) return;
            Vector2 cur = e.localPosition;
            if (_pointers.Count == 1)
            {
                var a = _view.Unproject(prev); var b = _view.Unproject(cur);
                _view.Center += a - b;
                _moved += (cur - prev).magnitude;
                _pointers[e.pointerId] = cur;
                ApplyView();
                return;
            }
            // Pinch: zoom about the midpoint so the map under the fingers stays put.
            var other = _pointers.First(kv => kv.Key != e.pointerId).Value;
            float before = (prev - other).magnitude, after = (cur - other).magnitude;
            _pointers[e.pointerId] = cur;
            _pinched = true;
            if (before > 4f) ZoomAround((cur + other) / 2f, after / before);
        }

        private void OnUp(PointerUpEvent e)
        {
            if (!_pointers.Remove(e.pointerId)) return;
            if (_viewport.HasPointerCapture(e.pointerId)) _viewport.ReleasePointer(e.pointerId);
            if (_pointers.Count == 0 && !_pinched && _moved < 14f) Tap(e.localPosition);
        }

        /// <summary>A tap on the map: the venue under it opens its card; a plot flies in.</summary>
        public void Tap(Vector2 local)
        {
            var at = _view.Unproject(local);
            foreach (var p in MapCatalog.Atlas.Placements.Values)
                if (MapCatalog.Areas.TryGetValue(p.AreaId, out var a) && MapSurface.AreaAtlasRect(a, p).Contains(at)) { Select(a.Id, fly: _view.PixelsPerMeter < 8f); return; }
            var plot = MapCatalog.Atlas.Plots.FirstOrDefault(pl => Mathf.Abs(at.x - pl.Center.x) < MapCatalog.Atlas.PlotSize / 2f && Mathf.Abs(at.y - pl.Center.y) < MapCatalog.Atlas.PlotSize / 2f);
            if (plot != null) SelectPlot(plot.Id);
            else HideCard();
        }

        private void ZoomBy(float k) => ZoomAround(_view.Viewport / 2f, k);

        private void ZoomAround(Vector2 local, float k)
        {
            var a = _view.Unproject(local);
            _view.PixelsPerMeter = Mathf.Clamp(_view.PixelsPerMeter * k, _minPpm, _maxPpm);
            var b = _view.Unproject(local);
            _view.Center += a - b;
            ApplyView();
        }

        private void RailTo(float y)
        {
            float h = Mathf.Max(1f, _rail.contentRect.height);
            float t = 1f - Mathf.Clamp01(y / h);
            _view.PixelsPerMeter = Mathf.Exp(Mathf.Lerp(Mathf.Log(_minPpm), Mathf.Log(_maxPpm), t));
            ApplyView();
        }

        public void Recentre()
        {
            if (TryPlayer(out var me, out _)) FlyTo(me, Mathf.Max(_view.PixelsPerMeter, 12f));
            else FlyTo(MapCatalog.Atlas.Rect.center, _minPpm);
        }

        public void FlyTo(Vector2 center, float ppm, float seconds = 0.45f)
        {
            StopFly();
            var c0 = _view.Center; float z0 = Mathf.Log(_view.PixelsPerMeter), z1 = Mathf.Log(Mathf.Clamp(ppm, _minPpm, _maxPpm));
            float start = Time.unscaledTime;
            _fly = schedule.Execute(() =>
            {
                float t = Mathf.Clamp01((Time.unscaledTime - start) / seconds);
                float k = t * t * (3 - 2 * t);
                _view.Center = Vector2.Lerp(c0, center, k);
                _view.PixelsPerMeter = Mathf.Exp(Mathf.Lerp(z0, z1, k));
                ApplyView();
                if (t >= 1f) StopFly();
            }).Every(0);
        }

        private void StopFly() { _fly?.Pause(); _fly = null; }

        // ------------------------------------------------------------------ selection card

        public void Select(string areaId, bool fly)
        {
            if (!MapCatalog.Areas.TryGetValue(areaId, out var a)) return;
            var p = MapCatalog.PlacementOf(a);
            if (p == null) return;
            _selected = areaId;
            var r = MapSurface.AreaAtlasRect(a, p);
            if (fly) FlyTo(r.center, Mathf.Min(_view.Viewport.y * 0.55f / r.height, 14f));
            var s = VenueSession.Current;
            bool here = s != null && MapCatalog.ForVisit(s.VenueId, s.HomeId) == a;
            string own = OwnHome();
            var plot = MapCatalog.Atlas.Plots.FirstOrDefault(x => x.Id == p.Plot);
            string status;
            Action act = null; string actText = null;
            if (here) status = Strings.T("map.youAreHere");
            else if (a.IsHome)
            {
                bool mine = a.HomeId == own;
                status = Strings.T(mine ? "map.yourHome" : "map.privateHome");
                if (mine && OpenHome != null) { act = OpenHome; actText = Strings.T("map.openHome"); }
            }
            else if (Available?.Invoke()?.Contains(a.VenueId) == true)
            {
                status = Strings.T("map.openRow");
                var id = a.VenueId;
                act = () => Travel?.Invoke(id); actText = Strings.T("map.go");
            }
            else status = Strings.T("map.closed");
            ShowCard(Title(a), (plot?.Name ?? "") + " · " + status, act, actText, a.IsHome ? "home-page" : "place-marker");
        }

        private void SelectPlot(string plotId)
        {
            var plot = MapCatalog.Atlas.Plots.FirstOrDefault(x => x.Id == plotId);
            if (plot == null) return;
            _selected = null;
            if (plot.Reserved) { ShowCard(Strings.T("map.comingSoon"), Strings.T("map.comingSoonBody"), null, null, "crane"); return; }
            FlyTo(plot.Center, Mathf.Min(_view.Viewport.y * 0.8f / MapCatalog.Atlas.PlotSize, 9f));
            HideCard();
        }

        private void ShowCard(string title, string subtitle, Action act, string actText, string icon)
        {
            _card.Clear();
            _card.Add(LobbyArt.Icon(icon, "map-card-icon"));
            var copy = new VisualElement().With("map-card-copy");
            var t = new Label(title) { enableRichText = false }.With("map-card-title");
            var st = new Label(subtitle) { enableRichText = false }.With("map-card-sub");
            copy.Add(t); copy.Add(st);
            _card.Add(copy);
            if (act != null)
            {
                Button go;
                if (_look == Look.Duo) go = Duo.Pill(actText, act, Duo.PillKind.Primary);
                else { go = new Button(act) { text = actText }; go.AddToClassList("map-card-go"); }
                go.name = "map-go";
                _card.Add(go);
            }
            var close = new Button(HideCard) { name = "map-card-close", tooltip = Strings.T("common.close") };
            close.AddToClassList("map-card-close");
            close.Add(LobbyArt.Icon("multiply", "map-card-close-icon"));
            _card.Add(close);
            _card.style.display = DisplayStyle.Flex;
        }

        public void HideCard() { _card.style.display = DisplayStyle.None; _selected = null; }

        // ------------------------------------------------------------------ search (Duo)

        private void Search(string q)
        {
            if (_results == null) return;
            _results.Clear();
            q = (q ?? "").Trim();
            if (q.Length == 0) { _results.style.display = DisplayStyle.None; return; }
            var hits = MapCatalog.Atlas.Placements.Values
                .Select(p => MapCatalog.Areas.TryGetValue(p.AreaId, out var a) ? a : null)
                .Where(a => a != null && Matches(q, Title(a), MapCatalog.Atlas.Plots.FirstOrDefault(x => x.Id == MapCatalog.PlacementOf(a)?.Plot)?.Name))
                .Take(5).ToList();
            foreach (var a in hits)
            {
                var id = a.Id;
                var row = new Button(() => { _results.style.display = DisplayStyle.None; Select(id, fly: true); }) { name = "map-result-" + a.Id };
                row.AddToClassList("map-result");
                row.Add(LobbyArt.Icon(a.IsHome ? "home-page" : "place-marker", "map-result-icon"));
                row.Add(new Label(Title(a)) { enableRichText = false }.With("map-result-title"));
                _results.Add(row);
            }
            if (hits.Count == 0) _results.Add(new Label(Strings.T("map.noResults")).With("map-result-none"));
            _results.style.display = DisplayStyle.Flex;
        }

        /// <summary>Search match ignoring case and accents ("cafe" finds "Terminal Café"), on the name or the district.</summary>
        public static bool Matches(string query, params string[] fields)
        {
            var q = Fold(query);
            if (q.Length == 0) return false;
            foreach (var f in fields) if (!string.IsNullOrEmpty(f) && Fold(f).Contains(q)) return true;
            return false;
        }

        private static string Fold(string s)
        {
            var d = (s ?? "").Trim().Normalize(System.Text.NormalizationForm.FormD);
            var b = new System.Text.StringBuilder(d.Length);
            foreach (var ch in d)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark) b.Append(char.ToLowerInvariant(ch));
            return b.ToString();
        }

        // ------------------------------------------------------------------ pieces

        private static Button Pill(string text, string icon, string cls)
        {
            var b = new Button { tooltip = text };
            b.AddToClassList("map-pill");
            b.AddToClassList(cls);
            if (icon != null) b.Add(LobbyArt.Icon(icon, "map-pill-icon"));
            b.Add(new Label(text) { enableRichText = false, pickingMode = PickingMode.Ignore }.With("map-pill-text"));
            return b;
        }

        private Button Control(string name, string text, Action onClick, string icon = null)
        {
            var b = new Button(() => { DeviceFeel.Tick(); onClick(); }) { name = name, tooltip = name };
            b.AddToClassList("map-control");
            b.AddToClassList(_look == Look.Duo ? "map-control--glass" : "map-control--arena");
            if (icon != null) b.Add(LobbyArt.Icon(icon, "map-control-icon"));
            if (text != null) b.Add(new Label(text) { pickingMode = PickingMode.Ignore }.With("map-control-text"));
            return b;
        }

        private static void DrawCone(MeshGenerationContext ctx)
        {
            var r = ctx.visualElement.contentRect;
            var p = ctx.painter2D;
            var c = new Vector2(r.width / 2f, r.height / 2f);
            p.fillColor = new Color(0.24f, 0.86f, 0.52f, 0.32f);
            p.BeginPath();
            p.MoveTo(c);
            p.Arc(c, r.height / 2f, Angle.Degrees(270f - 32f), Angle.Degrees(270f + 32f));
            p.ClosePath();
            p.Fill();
        }
    }
}
