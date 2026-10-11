using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Launcher shortcuts backed by local time and the same district registry as MapApp. Refresh is owned by
    /// PhoneShell's clock; no subscriptions, timers, network calls or stored-player mutations are made here.
    /// </summary>
    public sealed class PhoneWidgets : VisualElement
    {
        private readonly Button _calendar, _map;
        private readonly Label _weekday, _day, _month, _time, _districtCount;
        private readonly ClockFace _clock;
        private readonly DistrictSketch _sketch;
        private readonly List<string> _districtIds = new List<string>();
        private Registries _registry;
        private DateTime _date;
        private int _minute = -1;
        private string _activeDistrict;

        public PhoneWidgets(Action<string> launch)
        {
            AddToClassList("ct-phone-widgets");
            pickingMode = PickingMode.Ignore;
            _calendar = Widget("week", launch, "ct-phone-widget-calendar");
            _map = Widget("map", launch, "ct-phone-widget-map");
            _weekday = Copy("ct-phone-widget-weekday");
            _day = Copy("ct-phone-widget-day");
            _month = Copy("ct-phone-widget-month");
            _time = Copy("ct-phone-widget-time");
            _clock = new ClockFace();
            _calendar.Add(_weekday); _calendar.Add(_day); _calendar.Add(_month);
            _calendar.Add(_time); _calendar.Add(_clock);
            _sketch = new DistrictSketch();
            _map.Add(_sketch);
            var title = Copy("ct-phone-widget-map-title");
            title.text = Strings.T("phone.widget.city");
            _districtCount = Copy("ct-phone-widget-map-meta");
            _map.Add(title); _map.Add(_districtCount);
            Add(_calendar); Add(_map);
            Refresh();
        }

        public void Refresh()
        {
            var now = DateTime.Now;
            bool dateChanged = now.Date != _date;
            if (dateChanged)
            {
                _date = now.Date;
                _weekday.text = Strings.F("phone.widget.weekday", now);
                _day.text = Strings.F("phone.widget.day", now);
                _month.text = Strings.F("phone.widget.month", now);
            }
            int minute = now.Hour * 60 + now.Minute;
            if (dateChanged || minute != _minute)
            {
                _minute = minute;
                _time.text = Strings.F("phone.widget.time", now);
                _calendar.tooltip = Strings.F("phone.widget.dateAccess", now);
            }
            _clock.SetTime(now);

            var reg = Services.TryGet<ConfigStore>(out var config) ? config.Registries : null;
            if (!ReferenceEquals(reg, _registry))
            {
                _registry = reg;
                _districtIds.Clear();
                if (reg != null)
                    foreach (var district in reg.Districts)
                    {
                        // Match the map guide: only districts containing authored venue entries are shown.
                        foreach (var venue in reg.VenueOrder)
                            if (venue.districtId == district.Key)
                            { _districtIds.Add(district.Key); break; }
                    }
                _sketch.SetDistricts(_districtIds);
            }
            _districtCount.text = _districtIds.Count == 0 ? Strings.T("phone.widget.cityUnavailable")
                : _districtIds.Count == 1 ? Strings.T("phone.widget.districtOne")
                : Strings.F("phone.widget.districts", _districtIds.Count);

            // Use a confirmed active visit, never a saved/default venue or invented GPS position.
            var visit = VenueSession.Current;
            string districtId = null;
            string venueName = null;
            if (visit != null && visit.IsActive && reg != null && reg.Venues.TryGetValue(visit.VenueId ?? "", out var current))
            { districtId = current.districtId; venueName = current.name; }
            if (_activeDistrict != districtId)
            {
                _activeDistrict = districtId;
                _sketch.SetActive(districtId);
            }
            _map.tooltip = string.IsNullOrEmpty(venueName) ? Strings.T("phone.widget.cityAccess")
                : Strings.T("phone.widget.cityAccess") + " · " + venueName;
        }

        private static Button Widget(string route, Action<string> launch, string css)
        {
            // Native buttons without game-button depth layers; the whole widget is the tap target.
            var button = new Button(() => launch?.Invoke(route)) { text = "", enableRichText = false, name = "phone-widget-" + route };
            button.AddToClassList("ct-phone-widget"); button.AddToClassList(css);
            return button;
        }

        private static Label Copy(string css)
        {
            var label = El.Text("", css);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private abstract class WidgetDrawing : VisualElement
        {
            private static readonly CustomStyleProperty<Color> InkProperty = new("--ct-widget-ink");
            private static readonly CustomStyleProperty<Color> CardProperty = new("--ct-widget-paper");
            private static readonly CustomStyleProperty<Color> AccentProperty = new("--ct-widget-accent");
            private static readonly CustomStyleProperty<Color> GreenProperty = new("--ct-widget-green");
            private static readonly CustomStyleProperty<Color> SkyProperty = new("--ct-widget-blue");
            protected Color Ink = Color.black, Card = Color.white, Accent = Color.black, Green = Color.white, Sky = Color.white;

            protected WidgetDrawing(string css)
            {
                AddToClassList(css); pickingMode = PickingMode.Ignore;
                RegisterCallback<CustomStyleResolvedEvent>(e =>
                {
                    if (e.customStyle.TryGetValue(InkProperty, out var c)) Ink = c;
                    if (e.customStyle.TryGetValue(CardProperty, out c)) Card = c;
                    if (e.customStyle.TryGetValue(AccentProperty, out c)) Accent = c;
                    if (e.customStyle.TryGetValue(GreenProperty, out c)) Green = c;
                    if (e.customStyle.TryGetValue(SkyProperty, out c)) Sky = c;
                    MarkDirtyRepaint();
                });
                generateVisualContent += Draw;
            }

            protected abstract void Draw(MeshGenerationContext context);

            protected static void Disc(Painter2D p, Vector2 centre, float radius, Color fill)
            {
                p.fillColor = fill; p.BeginPath();
                for (int i = 0; i < 32; i++)
                {
                    float a = i * Mathf.PI / 16f;
                    var point = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                    if (i == 0) p.MoveTo(point); else p.LineTo(point);
                }
                p.ClosePath(); p.Fill();
            }

            protected static void Line(Painter2D p, Vector2 from, Vector2 to, float width, Color color)
            {
                p.lineCap = LineCap.Round; p.lineWidth = width; p.strokeColor = color;
                p.BeginPath(); p.MoveTo(from); p.LineTo(to); p.Stroke();
            }
        }

        private sealed class ClockFace : WidgetDrawing
        {
            private DateTime _now;
            public ClockFace() : base("ct-phone-widget-clock") { }
            public void SetTime(DateTime now)
            {
                if (_now.Ticks / TimeSpan.TicksPerSecond == now.Ticks / TimeSpan.TicksPerSecond) return;
                _now = now; MarkDirtyRepaint();
            }

            protected override void Draw(MeshGenerationContext context)
            {
                float radius = Mathf.Min(contentRect.width, contentRect.height) * .48f;
                if (radius <= 0 || float.IsNaN(radius)) return;
                var c = contentRect.center; var p = context.painter2D;
                Disc(p, c, radius, Color.Lerp(Card, Ink, .04f));
                for (int i = 0; i < 12; i++)
                {
                    var direction = Direction(i / 12f);
                    Line(p, c + direction * radius * .79f, c + direction * radius * .88f, i % 3 == 0 ? 2f : 1f, Ink);
                }
                Line(p, c, c + Direction((_now.Hour % 12 + _now.Minute / 60f) / 12f) * radius * .44f, 3f, Ink);
                Line(p, c, c + Direction((_now.Minute + _now.Second / 60f) / 60f) * radius * .68f, 2f, Ink);
                Line(p, c - Direction(_now.Second / 60f) * radius * .16f, c + Direction(_now.Second / 60f) * radius * .71f, 1f, Accent);
                Disc(p, c, 2.5f, Accent);
            }

            private static Vector2 Direction(float turn)
            {
                float angle = turn * Mathf.PI * 2f - Mathf.PI * .5f;
                return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
        }

        /// <summary>Abstract district diagram. Positions are decorative layout, not geographic coordinates or routes.</summary>
        private sealed class DistrictSketch : WidgetDrawing
        {
            private readonly List<string> _ids = new List<string>();
            private string _active;
            public DistrictSketch() : base("ct-phone-widget-map-art") { }
            public void SetDistricts(List<string> ids) { _ids.Clear(); _ids.AddRange(ids); MarkDirtyRepaint(); }
            public void SetActive(string id) { _active = id; MarkDirtyRepaint(); }

            protected override void Draw(MeshGenerationContext context)
            {
                float w = contentRect.width, h = contentRect.height;
                if (w <= 0 || h <= 0 || float.IsNaN(w) || float.IsNaN(h)) return;
                var p = context.painter2D;
                // Schematic cartography, not geographic coordinates. Buildings, park and water are illustration;
                // the small district markers alone correspond to registry entries and the active visit.
                Vector2 V(float x, float y) => new Vector2(x * w, y * h);
                void Polygon(Color color, params Vector2[] points)
                {
                    p.fillColor = color; p.BeginPath(); p.MoveTo(points[0]);
                    for (int n = 1; n < points.Length; n++) p.LineTo(points[n]);
                    p.ClosePath(); p.Fill();
                }
                void Road(Vector2 a, Vector2 b, float width)
                {
                    Line(p, a, b, width + 2f, Color.Lerp(Card, Ink, .12f));
                    Line(p, a, b, width, Card);
                }
                var paper = Color.Lerp(Card, Green, .045f);
                Polygon(paper, V(0, 0), V(1, 0), V(1, 1), V(0, 1));

                // Fine, staggered building footprints leave streets between blocks. No card-like district boxes.
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 7; col++)
                    {
                        float x = -.05f + col * .148f + row * .035f;
                        float y = .27f + row * .16f;
                        if (x < .45f && y < .34f) continue; // Clear paper behind "Your city".
                        float bw = (col + row) % 3 == 0 ? .074f : .099f;
                        var color = Color.Lerp(Card, (col + row) % 4 == 0 ? Green : Ink, (col + row) % 4 == 0 ? .16f : .075f);
                        Polygon(color, V(x, y), V(x + bw, y - .035f), V(x + bw + .02f, y + .07f), V(x + .02f, y + .105f));
                    }

                // A small park with an open walking path and a few tree canopies.
                p.fillColor = Color.Lerp(Card, Green, .47f); p.BeginPath(); p.MoveTo(V(-.05f, .71f));
                p.BezierCurveTo(V(.06f, .52f), V(.18f, .58f), V(.27f, .75f));
                p.BezierCurveTo(V(.33f, .86f), V(.25f, 1.08f), V(.12f, 1.1f));
                p.LineTo(V(-.06f, 1.1f)); p.ClosePath(); p.Fill();
                Line(p, V(-.02f, .93f), V(.24f, .73f), 3f, Color.Lerp(Card, Green, .14f));
                Disc(p, V(.06f, .72f), w * .023f, Color.Lerp(Card, Green, .68f));
                Disc(p, V(.13f, .82f), w * .028f, Color.Lerp(Card, Green, .59f));
                Disc(p, V(.22f, .92f), w * .021f, Color.Lerp(Card, Green, .67f));

                // Diagonal streets, with a quiet grey curb and a white carriageway at intersections.
                Road(V(-.10f, .68f), V(.88f, .22f), w * .027f);
                Road(V(.08f, 1.12f), V(1.05f, .65f), w * .024f);
                Road(V(.25f, .24f), V(.56f, 1.12f), w * .024f);
                Road(V(.53f, .13f), V(.86f, 1.13f), w * .019f);
                Road(V(-.06f, .43f), V(.74f, .08f), w * .014f);

                // The blue waterway cuts through the street grid; a single small bridge crosses it.
                p.lineCap = LineCap.Round; p.lineWidth = w * .145f;
                p.strokeColor = Color.Lerp(Card, Sky, .25f); p.BeginPath(); p.MoveTo(V(1.02f, -.08f));
                p.BezierCurveTo(V(.68f, .30f), V(.97f, .55f), V(.72f, 1.1f)); p.Stroke();
                p.lineWidth = w * .112f; p.strokeColor = Color.Lerp(Card, Sky, .68f);
                p.BeginPath(); p.MoveTo(V(1.02f, -.08f));
                p.BezierCurveTo(V(.68f, .30f), V(.97f, .55f), V(.72f, 1.1f)); p.Stroke();
                Road(V(.70f, .62f), V(.99f, .48f), w * .027f);

                // The heading stays readable without a separate opaque label card.
                Polygon(paper, V(0, 0), V(.63f, 0), V(.52f, .31f), V(0, .31f));
                for (int i = 0; i < _ids.Count; i++)
                {
                    float turn = i * Mathf.PI * 2f / Mathf.Max(1, _ids.Count) - .8f;
                    var center = V(.45f + Mathf.Cos(turn) * .23f, .64f + Mathf.Sin(turn) * .23f);
                    bool active = _ids[i] == _active;
                    if (active) Disc(p, center, 14f, Color.Lerp(Card, Sky, .19f));
                    Disc(p, center, active ? 8f : 4f, Card);
                    Disc(p, center, active ? 5f : 2f, active ? Sky : Color.Lerp(Ink, Card, .38f));
                }
            }
        }
    }
}
