using System;
using CTLife.CityLife.RektBurger;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// The guided first session over the game (queue 17, approved brief: ct-life-evidence/onboarding-17/BRIEF.md):
    /// - first steps: a pulse ring on the joystick only, no text, until the player has walked a few metres (W2);
    /// - one objective subtitle at a time, bottom centre, keywords yellow (the heat work's GTA line), for the next
    ///   unfinished beat; it comes back on a beat change, on entering the world and every so often;
    /// - the first shift: a Duo DM with a deep link to Jobs (Trenchers: "Shift starts now. Clock in at the counter." and
    ///   the Rekt Burger counter's own prompt; everyone else: their tribe's career shift);
    /// - the first trade (only while Pulse is open): one coach ring on Pulse's Buy, then on Place in the ticket (W4);
    /// - "Skip intro ›" under the minimap; FIRST DAY DONE when every beat is in.
    /// Also owns the dream ladder (<see cref="DreamLadderPresenter"/>). Lives for the game session; rebinds per user.
    /// </summary>
    public sealed class FirstSessionOverlays : IDisposable
    {
        /// <summary>The flow: true while the player is in the world (not the lobby, not travelling).</summary>
        public static Func<bool> InWorld;
        /// <summary>The flow: open a Duo app by id.</summary>
        public static Action<string> OpenApp;
        /// <summary>The flow: fast travel to an island venue (the map's Go).</summary>
        public static Action<string> TravelTo;
        /// <summary>The island venue whose door is nearest the Rekt Burger counter (about 100 m up Harbour Road).</summary>
        public const string CounterVenue = "terminal_cafe";

        private const float MovedMetres = 4f;
        private const int ObjectiveMs = 8000, RemindEverySec = 75;

        private readonly VisualElement _layer;
        private readonly Session _session;
        private FirstSessionService _svc;
        private DreamLadderPresenter _ladder;
        private VisualElement _subtitle, _skip, _joyPulse, _coached;
        private Label _subtitleText;
        private bool _wasNear, _celebrate, _celebrated;
        private IVisualElementScheduledItem _tick;
        private FirstBeat? _shown;
        private float _shownAt = -999f;
        private bool _wasInWorld, _disposed;
        private Vector3? _last;
        private float _walked;
        private LocalPlayerController _originPlayer;
        private float _inWorldSince;
        private static StyleSheet _style;

        public FirstSessionOverlays(VisualElement modalLayer)
        {
            _layer = modalLayer;
            _session = Services.Get<Session>();
            _session.Changed += Bind;
            var root = UiRoot.Instance != null ? UiRoot.Instance.Root : null;
            _style ??= Resources.Load<StyleSheet>("UI/first-session");
            if (root != null && _style != null && !root.styleSheets.Contains(_style)) root.styleSheets.Add(_style);
            _tick = _layer.schedule.Execute(Tick).Every(400);
            Bind();
        }

        private void Bind()
        {
            if (_disposed) return;
            var user = _session.IsSignedIn ? _session.UserId : null;
            if (_svc != null && !_svc.Disposed && _svc.UserId == user) return;
            Unbind();
            if (string.IsNullOrEmpty(user)) return;
            _svc = new FirstSessionService(user);
            _svc.Changed += OnChanged;
            _svc.BeatDone += OnBeat;
            _ladder = new DreamLadderPresenter(_layer, user);
        }

        private void Unbind()
        {
            if (_svc != null) { _svc.Changed -= OnChanged; _svc.BeatDone -= OnBeat; _svc.Dispose(); }
            _svc = null;
            _ladder?.Dispose(); _ladder = null;
            ClearGuidance();
            _last = null; _walked = 0f; _originPlayer = null; _shown = null; _celebrate = _celebrated = false;
        }

        private void OnChanged()
        {
            if (_svc == null) return;
            if (!_svc.Guiding) ClearGuidance();   // the tick shows the line for a new next beat
        }

        private void OnBeat(FirstBeat b)
        {
            if (_svc == null) return;
            // the last beat in: the MISSION PASSED line, once, as soon as the player is back in the world
            if (_svc.State.Completed && !_svc.State.Skipped && !_celebrated) _celebrate = true;
        }

        /// <summary>Another feature's card is up on the modal layer (the push pre-prompt after the first chest): wait for it.</summary>
        private bool ModalBusy()
        {
            foreach (var c in _layer.Children())
            {
                if (c.name != null && c.name.StartsWith("fs-", StringComparison.Ordinal)) continue;
                if (c.ClassListContains("fs-passed") || c.ClassListContains("fs-subtitle") || c.pickingMode == PickingMode.Ignore) continue;
                if (c.resolvedStyle.display != DisplayStyle.None && c.worldBound.width > 1f) return true;
            }
            return false;
        }

        private static bool InWorldNow() => InWorld != null && InWorld() && VenueSession.Current != null && VenueSession.Current.LocalPlayer != null;

        // ------------------------------------------------------------------ the loop

        private void Tick()
        {
            if (_svc == null || _svc.Disposed) return;
            bool inWorld = InWorldNow();
            if (!inWorld) { _wasInWorld = false; HideInWorld(); return; }
            if (!_wasInWorld) { _wasInWorld = true; _shown = null; _inWorldSince = Time.realtimeSinceStartup; _last = null; }

            _ladder?.Tick();
            // home: the first time the player stands in the city after the lobby
            if (!_svc.State.IsDone(FirstBeat.Home)) _svc.Mark(FirstBeat.Home);
            TrackMove();
            if (_celebrate && !_celebrated && !ModalBusy()) { _celebrate = false; _celebrated = true; Passed(FirstText.T("done.word"), FirstText.T("done.line"), 4200); }
            if (!_svc.Guiding) { ClearGuidance(); return; }

            ShowSkip();
            var next = _svc.Next;
            ShowJoyPulse(next == FirstBeat.Moved);
            if (next == FirstBeat.Shift) SendShiftDm();
            Coach(next == FirstBeat.Trade);
            // the shift line counts down the metres; arriving at the counter brings it back as "Clock in"
            bool near = next == FirstBeat.Shift && IsTrencher() && CounterMetres() is float m && m <= (RektBurgerService.Current?.State?.CounterRadiusM ?? 25);
            if (next != _shown || near != _wasNear || Time.realtimeSinceStartup - _shownAt > RemindEverySec) { _wasNear = near; ShowObjective(next); }
            else if (_subtitle != null && _subtitleText != null && next.HasValue && Line(next.Value) is string live && live != _subtitleText.text) _subtitleText.text = live;
        }

        /// <summary>
        /// Moved: a few metres walked by the player (positions are compared tick to tick, never logged or kept). Only steps
        /// while the player is moving count, so the arrival's settling, the street room's position correction, a fast travel
        /// or a recovery jump never does.
        /// </summary>
        private void TrackMove()
        {
            if (_svc.State.IsDone(FirstBeat.Moved)) return;
            var p = VenueSession.Current.LocalPlayer;
            var at = p.transform.position;
            if (p != _originPlayer || !_last.HasValue) { _originPlayer = p; _last = at; _walked = 0f; return; }
            float step = Flat(at - _last.Value);
            _last = at;
            // the thumb on the stick (or keys); a tap-to-walk counts once the arrival's own walk-out is over
            bool walking = p.DirectInputActive || (p.IsMoving && Time.realtimeSinceStartup - _inWorldSince > 10f);
            if (step < 6f && walking) _walked += step;
            if (_walked > MovedMetres) _svc.Mark(FirstBeat.Moved);
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        // ------------------------------------------------------------------ the objective line

        private void ShowObjective(FirstBeat? next)
        {
            _shown = next;
            _shownAt = Time.realtimeSinceStartup;
            string text = next.HasValue ? Line(next.Value) : null;
            if (string.IsNullOrEmpty(text)) { HideSubtitle(); return; }
            Subtitle(text, ObjectiveMs);
        }

        private string Line(FirstBeat b)
        {
            switch (b)
            {
                case FirstBeat.Shift:
                    if (!IsTrencher()) return FirstText.F("obj.shift.jobs", FirstText.Y(FirstText.T("kw.jobs")), FirstText.Y(FirstText.T("kw.shift")));
                    var m = CounterMetres();
                    var st = RektBurgerService.Current?.State;
                    if (m.HasValue && st != null && m.Value <= st.CounterRadiusM) return FirstText.F("obj.shift.rektNear", FirstText.Y(FirstText.T("kw.clockIn")));
                    return FirstText.F("obj.shift.rekt", FirstText.Y(FirstText.T("kw.rekt")), m.HasValue ? FirstText.Y(Mathf.RoundToInt(m.Value) + " m") : "");
                case FirstBeat.Trade: return FirstText.F("obj.trade", FirstText.Y(FirstText.T("kw.one")), FirstText.Y(FirstText.T("kw.pulse")));
                case FirstBeat.Quest: return FirstText.F("obj.quest", FirstText.Y(FirstText.T("kw.quests")));
                case FirstBeat.Chest: return FirstText.F("obj.chest", FirstText.Y(FirstText.T("kw.chest")));
                default: return null;   // moved: the joystick ring says it; origin / character / home are screens
            }
        }

        /// <summary>Trenchers start at Rekt Burger (the server unlocks it for them); everyone else takes a career shift.</summary>
        private static bool IsTrencher()
        {
            var st = RektBurgerService.Current?.State;
            if (st != null && st.Unlocked && st.Reason == "trencher") return true;
            return Services.TryGet<PlayerProfile>(out var p) && p.Me?.Character?.TribeId == "trencher";
        }

        private static float? CounterMetres()
        {
            var v = VenueSession.Current;
            var st = RektBurgerService.Current?.State;
            if (v == null || !v.IsIsland || v.LocalPlayer == null) return null;
            double x = st?.CounterX ?? -301.8, z = st?.CounterZ ?? -306.7;
            var p = v.LocalPlayer.transform.position;
            return Mathf.Sqrt((float)((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z)));
        }

        private void Subtitle(string richText, int ms)
        {
            var old = _subtitle;
            if (old != null) { _subtitle = null; old.RemoveFromHierarchy(); }
            var label = El.Text(richText, "fs-subtitle-text");
            label.enableRichText = true;
            _subtitleText = label;
            var b = El.Div("fs-subtitle", label);
            b.name = "fs-objective";
            b.pickingMode = PickingMode.Ignore;
            label.pickingMode = PickingMode.Ignore;
            _subtitle = b;
            _layer.Add(b);
            b.schedule.Execute(() => b.AddToClassList("fs-in")).ExecuteLater(16);
            b.schedule.Execute(() => { if (_subtitle == b) HideSubtitle(); }).ExecuteLater(ms);
        }

        private void HideSubtitle()
        {
            var b = _subtitle;
            _subtitle = null;
            if (b == null) return;
            b.RemoveFromClassList("fs-in");
            b.schedule.Execute(() => b.RemoveFromHierarchy()).ExecuteLater(260);
        }

        private void Passed(string word, string line, int ms = 2600)
        {
            var p = El.Div("fs-passed", El.Text(word, "fs-passed-word"), El.Text(line, "fs-passed-sub"));
            p.pickingMode = PickingMode.Ignore;
            _layer.Add(p);
            Bus.Publish(new SfxSignal("sfx_take_profit"));
            p.schedule.Execute(() => p.AddToClassList("fs-in")).ExecuteLater(20);
            p.schedule.Execute(() => p.RemoveFromClassList("fs-in")).ExecuteLater(ms);
            p.schedule.Execute(() => p.RemoveFromHierarchy()).ExecuteLater(ms + 400);
        }

        // ------------------------------------------------------------------ skip, joystick ring, coach ring, the DM

        private void ShowSkip()
        {
            if (_skip != null) { PlaceSkip(); return; }
            _skip = El.Btn(FirstText.T("skip"), () => { Log.Info("first", "skip tapped"); _svc?.Skip(); }, "fs-skip");
            _skip.name = "fs-skip";
            _layer.Add(_skip);
            _skip.schedule.Execute(() => _skip?.AddToClassList("fs-in")).ExecuteLater(16);
        }

        /// <summary>Just under the minimap (its place line included), right-aligned with it.</summary>
        private void PlaceSkip()
        {
            var map = UiRoot.Instance?.Root?.Q("hud-map");
            if (map == null || map.panel == null || map.worldBound.height < 1f) return;
            var below = _layer.WorldToLocal(new Vector2(map.worldBound.xMax, map.worldBound.yMax));
            _skip.style.top = below.y + 12f;
            _skip.style.right = Mathf.Max(16f, _layer.layout.width - below.x);
        }

        /// <summary>A pulsing ring over the stick's resting base (drawn on our layer: the idle stick itself fades out).</summary>
        private void ShowJoyPulse(bool on)
        {
            if (!on) { _joyPulse?.RemoveFromHierarchy(); _joyPulse = null; return; }
            // the HUD's own stick (another screen may have a ring too); when the idle base is hidden, its rest spot
            var controls = UiRoot.Instance?.HudLayer?.Q(className: "ct-move-controls");
            if (controls == null || float.IsNaN(controls.worldBound.width) || controls.worldBound.width < 1f) return;
            var ring = controls.Q(className: "ct-move-ring");
            Rect r;
            if (ring != null && ring.worldBound.width > 1f && ring.resolvedStyle.display != DisplayStyle.None) r = ring.worldBound;
            else
            {
                var c = new Vector2(controls.worldBound.xMin + MovementControls.RestCenter.x, controls.worldBound.yMax - MovementControls.RestCenter.y);
                r = new Rect(c.x - 130f, c.y - 130f, 260f, 260f);
            }
            var min = _layer.WorldToLocal(r.min);
            var max = _layer.WorldToLocal(r.max);
            if (_joyPulse != null && _joyPulse.panel != null) { Place(_joyPulse, min, max); return; }
            _joyPulse = El.Div("fs-joy-pulse");
            _joyPulse.name = "fs-joy-pulse";
            _joyPulse.pickingMode = PickingMode.Ignore;
            Place(_joyPulse, min, max);
            _layer.Add(_joyPulse);
            Log.Info("first", "stick pulse on (" + Mathf.RoundToInt(max.x - min.x) + " px)");
            var pulse = _joyPulse;
            bool big = false;
            pulse.schedule.Execute(() => { big = !big; pulse.EnableInClassList("fs-joy-pulse--out", big); }).Every(700);
        }

        private static void Place(VisualElement e, Vector2 min, Vector2 max)
        {
            e.style.left = min.x; e.style.top = min.y;
            e.style.width = max.x - min.x; e.style.height = max.y - min.y;
        }

        /// <summary>The one coach ring: Pulse's Buy on a coin page, then Place in the order ticket.</summary>
        private void Coach(bool on)
        {
            VisualElement target = null;
            if (on)
            {
                var root = UiRoot.Instance?.Root;
                var place = root?.Q("pulse-place");
                var buy = root?.Q("pulse-buy");
                target = Visible(place) ? place : Visible(buy) ? buy : null;
            }
            if (target == _coached) return;
            if (_coached != null) { _coached.RemoveFromClassList("fs-coach"); Ring(_coached, false); }
            _coached = target;
            if (_coached != null) { _coached.AddToClassList("fs-coach"); Ring(_coached, true); }
        }

        /// <summary>The ring is inline: the app's own button rules are more specific than any class we could add.</summary>
        private static void Ring(VisualElement e, bool on)
        {
            var w = on ? new StyleFloat(5f) : new StyleFloat(StyleKeyword.Null);
            var c = on ? new StyleColor(new Color(1f, 0.824f, 0.247f)) : new StyleColor(StyleKeyword.Null);
            e.style.borderTopWidth = w; e.style.borderBottomWidth = w; e.style.borderLeftWidth = w; e.style.borderRightWidth = w;
            e.style.borderTopColor = c; e.style.borderBottomColor = c; e.style.borderLeftColor = c; e.style.borderRightColor = c;
        }

        private static bool Visible(VisualElement e) =>
            e != null && e.panel != null && e.resolvedStyle.display != DisplayStyle.None && e.worldBound.width > 1f && e.resolvedStyle.opacity > 0.05f;

        /// <summary>The shift DM, once per player (a replay sends it again).</summary>
        private void SendShiftDm()
        {
            var key = "first.dm." + _svc.UserId;
            try { if (PlayerPrefs.GetInt(key, 0) == 1) return; PlayerPrefs.SetInt(key, 1); PlayerPrefs.Save(); } catch (Exception) { return; }
            bool rekt = IsTrencher();
            DuoNotificationCenter.Post(new DuoNotification
            {
                AppId = "jobs",
                ThreadKey = "first-shift",
                Title = FirstText.T(rekt ? "dm.rekt.title" : "dm.jobs.title"),
                Body = FirstText.T(rekt ? "dm.rekt.body" : "dm.jobs.body"),
                // Trenchers: the tap takes them to the Trenches (the counter is a short walk, the line counts the metres);
                // everyone else: the Jobs app, where their tribe's career comes first
                Open = () =>
                {
                    float? m = CounterMetres();
                    if (rekt && TravelTo != null && (!m.HasValue || m.Value > 160f)) TravelTo(CounterVenue);
                    else OpenApp?.Invoke("jobs");
                },
            });
        }

        // ------------------------------------------------------------------ teardown

        private void HideInWorld()
        {
            _skip?.RemoveFromHierarchy(); _skip = null;
            _joyPulse?.RemoveFromHierarchy(); _joyPulse = null;
            HideSubtitle();
            // the coach ring stays on Pulse while the phone covers the world
            if (_svc != null && _svc.Guiding && _svc.Next == FirstBeat.Trade) Coach(true); else Coach(false);
        }

        private void ClearGuidance()
        {
            _skip?.RemoveFromHierarchy(); _skip = null;
            _joyPulse?.RemoveFromHierarchy(); _joyPulse = null;
            HideSubtitle();
            Coach(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Changed -= Bind;
            _tick?.Pause(); _tick = null;
            Unbind();
        }
    }
}
