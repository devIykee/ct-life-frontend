using System;
using System.Collections.Generic;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.Map
{
    /// <summary>
    /// The city HUD minimap (CoD-style, top-right): the baked map of the current venue on CT Island, moved and turned every
    /// frame from the local player's transform (player-up, heading cone), with markers from real data, edge arrows for doors and
    /// friends out of view, and a compass "N" on the ring. A texture plus a UV transform (MapSurface quads): no second camera.
    /// Tap opens the full map.
    /// </summary>
    public sealed class Minimap : VisualElement
    {
        public const float Side = 324f;          // panel units (= 360 device px on the Seeker)
        public float Radius = 18f;               // metres from the centre to the edge
        public Action OpenFull;

        private readonly VisualElement _clip, _markers, _arrows, _north;
        private readonly MapSurface _surface;
        private readonly List<MapMarker> _live = new List<MapMarker>();
        private readonly List<VisualElement> _pool = new List<VisualElement>(), _arrowPool = new List<VisualElement>();
        private MapArea _area;
        private MapPlacement _placement;
        private float _heading;
        private bool _hasHeading;
        private Vector2 _center;
        private IVisualElementScheduledItem _tick;

        /// <summary>Tests and captures drive the view without a session.</summary>
        public Func<(MapArea area, Vector2 local, Vector3 localForward)?> Source;

        public Vector2 PlayerAtlas => _center;
        public float HeadingRadians => _heading;
        public MapArea Area => _area;

        public Minimap()
        {
            AddToClassList("map-mini");
            name = "hud-minimap";
            _clip = new VisualElement { pickingMode = PickingMode.Ignore };
            _clip.AddToClassList("map-mini-clip");
            _surface = new MapSurface { Project = ToPanel };
            _surface.AddToClassList("map-fill");
            _markers = new VisualElement { pickingMode = PickingMode.Ignore };
            _markers.AddToClassList("map-fill");
            _clip.Add(_surface);
            _clip.Add(_markers);
            var cone = new VisualElement { pickingMode = PickingMode.Ignore };
            cone.AddToClassList("map-mini-cone");
            cone.generateVisualContent += DrawCone;
            var me = new VisualElement { pickingMode = PickingMode.Ignore };
            me.AddToClassList("map-me");
            me.generateVisualContent += DrawArrow;
            _clip.Add(cone);
            _clip.Add(me);
            Add(_clip);
            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.AddToClassList("map-mini-ring");
            Add(ring);
            _arrows = new VisualElement { pickingMode = PickingMode.Ignore };
            _arrows.AddToClassList("map-fill");
            Add(_arrows);
            _north = new Label("N") { pickingMode = PickingMode.Ignore };
            _north.AddToClassList("map-mini-north");
            Add(_north);
            RegisterCallback<ClickEvent>(_ => OpenFull?.Invoke());
            RegisterCallback<AttachToPanelEvent>(_ => { _tick ??= schedule.Execute(Tick).Every(0); schedule.Execute(MapCatalog.Preload).StartingIn(2500); });
            focusable = true;
            tooltip = Strings.T("hud.map");
        }

        private Vector2 HalfPx => new Vector2(resolvedStyle.width > 0 ? resolvedStyle.width : Side, resolvedStyle.height > 0 ? resolvedStyle.height : Side) / 2f;

        private Vector2 ToPanel(Vector2 atlas)
        {
            var m = MapMath.ToMinimap(atlas, _center, _heading, Radius);
            var h = HalfPx;
            return new Vector2(h.x + m.x * h.x, h.y - m.y * h.y);
        }

        private void Tick()
        {
            (MapArea area, Vector2 local, Vector3 fwd)? src = null;
            var session = VenueSession.Current;
            if (Source != null) src = Source();
            else if (session != null && session.IsIsland && session.LocalPlayer != null)
            {
                // CT Island: world x/z ARE atlas metres; show the island with every venue map.
                IslandTick(session);
                return;
            }
            else if (session != null && session.Venue != null && session.LocalPlayer != null)
            {
                var area = MapCatalog.ForVisit(session.VenueId, session.HomeId);
                var t = session.LocalPlayer.transform;
                var root = session.Venue.transform;
                if (area != null) src = (area, session.Venue.ToLocal(t.position), root.InverseTransformDirection(t.forward));
            }
            if (src == null) { style.display = DisplayStyle.None; return; }
            style.display = DisplayStyle.Flex;
            var (a, local, forward) = src.Value;
            if (a != _area)
            {
                _islandLayers = false;
                _area = a;
                _placement = MapCatalog.PlacementOf(a) ?? new MapPlacement { AreaId = a.Id, Origin = Vector2.zero };
                var atlas = MapCatalog.Atlas;
                var layers = new List<MapSurface.Layer>();
                var atlasTex = MapCatalog.Texture(atlas.Texture);
                if (atlasTex != null && MapCatalog.PlacementOf(a) != null) layers.Add(new MapSurface.Layer { Texture = atlasTex, AtlasRect = atlas.Rect, Alpha = 1f, Grid = 1 });
                layers.Add(MapSurface.AreaLayer(a, _placement, MapCatalog.Texture(a.Texture), 1));
                _surface.SetLayers(layers);
                _hasHeading = false;
            }
            _center = MapMath.AtlasFromLocal(_placement, local);
            float target = forward.sqrMagnitude > 1e-4f ? MapMath.Heading(forward) : _heading;
            _heading = _hasHeading ? MapMath.SmoothAngle(_heading, target, 12f, Mathf.Min(Time.unscaledDeltaTime, 0.1f)) : target;
            _hasHeading = true;
            _surface.Refresh();

            if (Source == null) { MapMarkers.Live(session, _area, _placement, _live); MapMarkers.AddPin(_live); }
            PlaceMarkers();
            PlaceNorth();
        }

        private bool _islandLayers;

        private void IslandTick(VenueSession session)
        {
            style.display = DisplayStyle.Flex;
            if (!_islandLayers)
            {
                _islandLayers = true; _area = null;
                var atlas = MapCatalog.Atlas;
                var layers = new List<MapSurface.Layer>();
                var tex = MapCatalog.Texture(atlas.Texture);
                if (tex != null) layers.Add(new MapSurface.Layer { Texture = tex, AtlasRect = atlas.Rect, Alpha = 1f, Grid = 1 });
                foreach (var p in atlas.Placements.Values)
                    if (MapCatalog.Areas.TryGetValue(p.AreaId, out var a))
                        layers.Add(MapSurface.AreaLayer(a, p, MapCatalog.Texture(a.Texture), 1));
                _surface.SetLayers(layers);
                _hasHeading = false;
            }
            var t = session.LocalPlayer.transform;
            _center = new Vector2(t.position.x, t.position.z);
            var f = t.forward;
            float target = new Vector2(f.x, f.z).sqrMagnitude > 1e-4f ? MapMath.Heading(f) : _heading;
            _heading = _hasHeading ? MapMath.SmoothAngle(_heading, target, 12f, Mathf.Min(Time.unscaledDeltaTime, 0.1f)) : target;
            _hasHeading = true;
            _surface.Refresh();
            // The current zone's markers (its root sits at its atlas origin, so placement math holds).
            var zoneArea = MapCatalog.ForVisit(session.VenueId, null);
            MapMarkers.Live(session, zoneArea, MapCatalog.PlacementOf(zoneArea), _live);
            MapMarkers.AddPin(_live);
            PlaceMarkers();
            PlaceNorth();
        }

        /// <summary>Captures: markers for a driven view.</summary>
        public void SetMarkers(IEnumerable<MapMarker> markers) { _live.Clear(); _live.AddRange(markers); }

        private void PlaceMarkers()
        {
            var h = HalfPx;
            int used = 0, arrows = 0;
            foreach (var m in _live)
            {
                var mini = MapMath.ToMinimap(m.Atlas, _center, _heading, Radius);
                bool clamped = MapMath.ClampToEdge(mini, 0.1f, out var at, out float angle);
                if (clamped && !m.OfInterest) continue;
                var px = new Vector2(h.x + at.x * h.x, h.y - at.y * h.y);
                if (clamped)
                {
                    var arrow = Get(_arrowPool, _arrows, arrows++, "map-edge-arrow");
                    arrow.EnableInClassList("map-edge-arrow--friend", m.Kind == MarkerKind.Friend);
                    arrow.EnableInClassList("map-edge-arrow--pin", m.Kind == MarkerKind.Pin);
                    arrow.style.translate = new Translate(px.x - 18f, px.y - 18f);
                    arrow.style.rotate = new Rotate(angle * Mathf.Rad2Deg);
                    continue;
                }
                var e = Get(_pool, _markers, used++, "map-marker");
                // re-dress only when the pooled element changes kind: dressing every frame re-resolved its styles and
                // reloaded its icon each frame (UI and GC cost on entry phones)
                if (!(e.userData is MarkerKind dressed) || dressed != m.Kind) { Dress(e, m.Kind); e.userData = m.Kind; }
                e.style.translate = new Translate(px.x - 22f, px.y - 22f);
            }
            for (int i = used; i < _pool.Count; i++) _pool[i].style.display = DisplayStyle.None;
            for (int i = arrows; i < _arrowPool.Count; i++) _arrowPool[i].style.display = DisplayStyle.None;
        }

        internal static void Dress(VisualElement e, MarkerKind k)
        {
            foreach (MarkerKind kind in Enum.GetValues(typeof(MarkerKind))) e.EnableInClassList("map-marker--" + kind.ToString().ToLowerInvariant(), kind == k);
            var icon = MapMarkers.Icon(k);
            var tex = icon != null ? Resources.Load<Texture2D>("UI/Lobby/Icons/" + icon) : null;
            e.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
        }

        private static VisualElement Get(List<VisualElement> pool, VisualElement parent, int i, string cls)
        {
            while (pool.Count <= i)
            {
                var e = new VisualElement { pickingMode = PickingMode.Ignore };
                e.AddToClassList(cls);
                parent.Add(e);
                pool.Add(e);
            }
            pool[i].style.display = DisplayStyle.Flex;
            return pool[i];
        }

        private void PlaceNorth()
        {
            // North in minimap space is the world +z axis turned by the heading; the tick rides the ring.
            var dir = MapMath.Rotate(Vector2.up, _heading);
            MapMath.ClampToEdge(dir * 10f, 0.02f, out var at, out _);
            var h = HalfPx;
            _north.style.translate = new Translate(h.x + at.x * h.x - 22f, h.y - at.y * h.y - 22f);
        }

        private static void DrawCone(MeshGenerationContext ctx)
        {
            var r = ctx.visualElement.contentRect;
            var p = ctx.painter2D;
            var c = new Vector2(r.width / 2f, r.height);
            // A soft 70-degree view cone pointing up from the player.
            p.fillColor = new Color(1f, 1f, 1f, 0.22f);
            p.BeginPath();
            p.MoveTo(c);
            p.Arc(c, r.height, Angle.Degrees(270f - 35f), Angle.Degrees(270f + 35f));
            p.ClosePath();
            p.Fill();
        }

        private static void DrawArrow(MeshGenerationContext ctx)
        {
            var r = ctx.visualElement.contentRect;
            var p = ctx.painter2D;
            float w = r.width, h = r.height;
            p.fillColor = new Color32(0x2a, 0x0b, 0x4a, 0xff);
            Tri(p, new Vector2(w / 2, 0), new Vector2(w, h), new Vector2(w / 2, h * 0.74f), new Vector2(0, h));
            p.Fill();
            p.fillColor = new Color32(0x3d, 0xdc, 0x84, 0xff);
            Tri(p, new Vector2(w / 2, h * 0.16f), new Vector2(w * 0.84f, h * 0.88f), new Vector2(w / 2, h * 0.66f), new Vector2(w * 0.16f, h * 0.88f));
            p.Fill();
        }

        private static void Tri(Painter2D p, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.LineTo(c); p.LineTo(d); p.ClosePath();
        }
    }
}
