using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.UI.Kit;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;
using Motion = CTLife.UI.Kit.Motion;

namespace CTLife.CityLife.Casino
{
    /// <summary>
    /// The Rug Room's screens (backend PR #12): a full-screen casino over the game, opened from the casino floor's machines
    /// (<see cref="CasinoHooks.Open"/>) or the Duo phone's Rug Room app. Velvet and brass: the neon wordmark, the game tabs
    /// (Slots, Moon or Rug, Coinflip), the Cash pill and today's room under the caps, Fairness and Limits, and the server's
    /// disclaimer on a bar along the bottom of every screen. `GET /casino/state` decides what shows first: the 18+ gate, the
    /// self-exclusion lock, the daily-limit stop, or the tables. The session reminder pops over everything.
    /// </summary>
    public sealed class RugRoomSheet : VisualElement
    {
        private static RugRoomSheet _open;
        public static bool IsOpen => _open != null;
        public static readonly string[] Prewarm =
        {
            "UI/Casino/rugroom_logo", "UI/Casino/backdrop", "UI/Casino/coin_heads", "UI/Casino/coin_tails", "UI/Casino/crash_rocket", "UI/Casino/crash_rug",
            "UI/Casino/Symbols/rocket", "UI/Casino/Symbols/diamond_hands", "UI/Casino/Symbols/ape", "UI/Casino/Symbols/laser_eyes",
            "UI/Casino/Symbols/rug", "UI/Casino/Symbols/wild",
        };
        private static readonly string[] Sounds =
        {
            "sfx_slot_lever", "sfx_slot_spin", "sfx_slot_stop", "sfx_slot_win", "sfx_slot_bigwin", "sfx_chips", "sfx_coin_flip",
            "sfx_coin_land", "sfx_rug_pull", "sfx_cashout_ok",
        };

        /// <summary>Starts the async loads (textures, sounds, the style sheet) so opening never pays for a synchronous load.</summary>
        public static void PrewarmAssets()
        {
            foreach (var p in Prewarm) Resources.LoadAsync<Texture2D>(p);
            foreach (var s in Sounds) Resources.LoadAsync<AudioClip>("Audio/sfx/" + s);
            Resources.LoadAsync<StyleSheet>("UI/Casino/casino");
        }

        private static readonly Dictionary<string, Texture2D> Tex = new Dictionary<string, Texture2D>();
        public static Texture2D Art(string path)
        {
            if (Tex.TryGetValue(path, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>(path);
            Tex[path] = t;
            return t;
        }
        public static Texture2D Symbol(string id) => Art("UI/Casino/Symbols/" + id);

        public static void Show(string game)
        {
            var svc = CasinoService.Current;
            if (svc == null || UiRoot.Instance == null) { Bus.Publish(ToastSignal.Text(CasinoText.T("gate.offline"), ToastKind.Warning)); return; }
            if (_open != null) { _open.Select(game); return; }
            var sheet = new RugRoomSheet(svc, game);
            UiRoot.Instance.ModalLayer.Add(sheet);
            _open = sheet;
        }

        public static void CloseOpen() => _open?.Close();

        // ------------------------------------------------------------------ instance

        public readonly CasinoService Svc;
        public long StakeCents = 500;

        private readonly VisualElement _body, _gateHost, _modalHost, _toastHost;
        private readonly Label _cash, _left, _disclaimer, _session;
        private readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();
        private readonly Button _fairBtn, _limitsBtn;
        private readonly Dictionary<string, RugPanel> _panels = new Dictionary<string, RugPanel>();
        private readonly IVisualElementScheduledItem _tick;
        private RugPanel _current;
        private string _currentId;
        private float _last;
        private VisualElement _toast;
        private Camera _cam;
        private int _camMask;
        private Rect _camRect;

        public const string Fair = "fair", Limits = "limits";

        private RugRoomSheet(CasinoService svc, string game)
        {
            Svc = svc;
            name = "rug-room";
            AddToClassList("rr-sheet");
            pickingMode = PickingMode.Position;
            foreach (var s in new[] { "UI/kit", "UI/Casino/casino" })
            {
                var sheet = Resources.Load<StyleSheet>(s);
                if (sheet != null) styleSheets.Add(sheet);
            }

            // ---- backdrop: velvet, three soft glows and an Art-Deco sunburst
            // one opaque layer, baked (art/source/casino/backdrop.html): three full-screen alpha glows and a Painter2D
            // sunburst cost the Infinix its fill rate (2026-10-25)
            var bg = El.Div("rr-bg");
            var bt = Art("UI/Casino/backdrop");
            if (bt != null) bg.style.backgroundImage = new StyleBackground(bt);
            Add(bg);

            // ---- top bar
            var top = El.Div("rr-top");
            var logo = El.Div("rr-logo");
            var lt = Art("UI/Casino/rugroom_logo");
            if (lt != null) logo.style.backgroundImage = new StyleBackground(lt);
            top.Add(logo);
            var tabs = El.Div("rr-tabs");
            foreach (var (id, key) in new[] { (CasinoHooks.Slots, "tab.slots"), (CasinoHooks.Crash, "tab.crash"), (CasinoHooks.Coinflip, "tab.coinflip") })
            {
                var b = RugRoomSheet.Btn("", () => Select(id), "rr-tab");
                b.name = "rr-tab-" + id;
                b.Add(El.Text(CasinoText.T(key), "rr-tab-label"));
                _tabs[id] = b;
                tabs.Add(b);
            }
            top.Add(tabs);
            _cash = El.Text("", "rr-pill-value");
            _left = El.Text("", "rr-pill-cap");
            top.Add(El.Div("rr-pill", El.Div("rr-pill-coin"), El.Div("rr-pill-col", El.Text(CasinoText.T("hud.cash"), "rr-pill-cap"), _cash)));
            var leftPill = El.Div("rr-pill", El.Div("rr-pill-col", El.Text(CasinoText.T("app.today").ToUpperInvariant(), "rr-pill-cap"), _left));
            leftPill.name = "rr-today";
            top.Add(leftPill);
            _fairBtn = IconBtn(CasinoText.T("tab.fair"), () => Select(Fair), "rr-fair");
            _limitsBtn = IconBtn(CasinoText.T("tab.limits"), () => Select(Limits), "rr-limits");
            _fairBtn.tooltip = CasinoText.T("tab.fair"); _limitsBtn.tooltip = CasinoText.T("tab.limits");
            top.Add(_fairBtn); top.Add(_limitsBtn);
            var close = RugRoomSheet.Btn("", Close, "rr-close");
            close.name = "rr-close";
            close.Add(El.Text("×", "rr-close-x"));
            top.Add(close);
            Add(top);

            // ---- body, gate, footer, modal, toast
            _body = El.Div("rr-body");
            Add(_body);
            _gateHost = El.Div("rr-body");
            _gateHost.pickingMode = PickingMode.Ignore;
            Add(_gateHost);
            _disclaimer = El.Text(svc.Disclaimer, "rr-disclaimer");
            _disclaimer.name = "rr-disclaimer";
            _session = El.Text("", "rr-foot-note");
            Add(El.Div("rr-foot", _disclaimer, El.Text("·", "rr-foot-sep"), _session));
            _toastHost = El.Div();
            _toastHost.pickingMode = PickingMode.Ignore;
            _toastHost.style.position = Position.Absolute; _toastHost.style.left = 0; _toastHost.style.right = 0; _toastHost.style.top = 0; _toastHost.style.bottom = 0;
            Add(_toastHost);
            _modalHost = El.Div();
            _modalHost.pickingMode = PickingMode.Ignore;
            _modalHost.style.position = Position.Absolute; _modalHost.style.left = 0; _modalHost.style.right = 0; _modalHost.style.top = 0; _modalHost.style.bottom = 0;
            Add(_modalHost);

            // the casino is opaque and full screen: the world camera stops drawing behind it (perf: the Seeker spent ~28 ms
            // of GPU on the hidden floor), and gets its layers back on close
            // Infinix (2026-10-25): culling alone still left the camera's full-screen passes (skybox clear, URP post) at
            // ~15 fps, so its viewport shrinks to a few pixels too while the casino covers the screen
            _cam = Camera.main;
            if (_cam != null) { _camMask = _cam.cullingMask; _camRect = _cam.rect; _cam.cullingMask = 0; _cam.rect = new Rect(0f, 0f, 0.01f, 0.01f); }
            PauseWorld(true);
            svc.ScreensOpen = true;
            svc.StateChanged += OnState;
            svc.SessionReminder += OnReminder;
            Select(string.IsNullOrEmpty(game) || game == CasinoHooks.Lobby ? CasinoHooks.Slots : game);
            OnState();
            _ = svc.RefreshState();
            _ = svc.RefreshChallenges();
            Cues.Play("sfx_chips", 0.7f);
            style.opacity = 0f;
            Tween.Run(this, 0.25f, t => style.opacity = t, Ease.OutCubic, skipWhenReduced: false);
            _last = Time.unscaledTime;
            _tick = schedule.Execute(Tick).Every(0);
            RegisterCallback<DetachFromPanelEvent>(_ => Teardown());
            Log.Info("casino", "Rug Room open at " + _currentId);
        }

        private Button IconBtn(string glyph, Action click, string nm)
        {
            var b = RugRoomSheet.Btn("", click, "rr-icon-btn");
            b.name = nm;
            b.Add(El.Text(glyph, "rr-icon-btn-label"));
            return b;
        }

        public string CurrentId => _currentId;

        public void Select(string id)
        {
            if (id == _currentId) return;
            if (_current != null && _current.Busy) { Toast(CasinoText.T("err.ROUND_IN_PROGRESS")); return; }
            _current?.Hidden();
            _current?.RemoveFromHierarchy();
            if (!_panels.TryGetValue(id, out var p))
            {
                p = id switch
                {
                    CasinoHooks.Slots => new SlotsPanel(this),
                    CasinoHooks.Crash => new CrashPanel(this),
                    CasinoHooks.Coinflip => new CoinflipPanel(this),
                    Fair => new FairnessPanel(this),
                    Limits => new LimitsPanel(this),
                    _ => (RugPanel)new SlotsPanel(this),
                };
                _panels[id] = p;
            }
            _current = p; _currentId = id;
            _body.Add(p);
            p.Shown();
            foreach (var kv in _tabs) kv.Value.EnableInClassList("rr-tab--on", kv.Key == id);
            _fairBtn.EnableInClassList("rr-icon-btn--on", id == Fair);
            _limitsBtn.EnableInClassList("rr-icon-btn--on", id == Limits);
            p.style.opacity = 0f;
            Tween.Run(p, 0.2f, t => p.style.opacity = t, Ease.OutCubic, skipWhenReduced: false);
            Gate();
        }

        // ------------------------------------------------------------------ state, gates

        private void OnState()
        {
            var s = Svc.State;
            _disclaimer.text = Svc.Disclaimer;
            if (s != null)
            {
                _left.text = CasinoText.Money(Math.Max(0, Math.Min(s.StakeRemainingCents, s.LossRemainingCents)));
                if (StakeCents > s.MaxStakeCents) StakeCents = s.MaxStakeCents;
                if (StakeCents < s.MinStakeCents) StakeCents = s.MinStakeCents;
            }
            foreach (var p in _panels.Values) p.StateChanged();
            Gate();
        }

        /// <summary>What may show: the tables, or a gate over them (18+, self-excluded, today's limit, loading, offline).</summary>
        private void Gate()
        {
            var s = Svc.State;
            string kind = null;
            if (s == null) kind = Svc.StateError != null ? "offline" : "loading";
            else if (!s.AgeConfirmed) kind = "age";
            else if (s.Excluded(ServerClock.NowMs)) kind = "excluded";
            else if (s.CapReached) kind = "cap";
            // fairness and limits stay readable behind every gate but the 18+ one (the rounds are the player's own)
            bool tables = _currentId == CasinoHooks.Slots || _currentId == CasinoHooks.Crash || _currentId == CasinoHooks.Coinflip;
            if (kind == "excluded" || kind == "cap") { if (!tables) kind = null; }
            // an open crash round can still be finished while excluded or capped
            if ((kind == "excluded" || kind == "cap") && _current != null && _current.Busy) kind = null;
            string key = kind == null ? null : kind + (kind == "excluded" ? s.SelfExcludedUntilMs.ToString() : "");
            if (key == (string)_gateHost.userData) return;
            _gateHost.userData = key;
            _gateHost.Clear();
            _gateHost.pickingMode = kind == null ? PickingMode.Ignore : PickingMode.Position;
            if (kind == null) return;
            Log.Info("casino", "gate " + kind);
            _gateHost.Add(BuildGate(kind, s));
        }

        private VisualElement BuildGate(string kind, CasinoState s)
        {
            var g = El.Div("rr-gate");
            g.name = "rr-gate-" + kind;
            switch (kind)
            {
                case "loading":
                    g.Add(El.Text(CasinoText.T("gate.loading"), "rr-gate-title"));
                    break;
                case "offline":
                    g.Add(Badge("!", false));
                    g.Add(El.Text(Svc.StateError ?? CasinoText.T("gate.offline"), "rr-gate-body"));
                    g.Add(El.Div("rr-gate-actions", Plain(CasinoText.T("rem.keep"), () => { _ = Svc.RefreshState(); }, "")));
                    break;
                case "age":
                    g.Add(Badge("18+", false));
                    g.Add(El.Text(CasinoText.T("gate.ageTitle"), "rr-gate-title"));
                    g.Add(El.Text(CasinoText.T("gate.ageBody"), "rr-gate-body"));
                    g.Add(El.Text(CasinoText.T("gate.ageNote"), "rr-gate-note"));
                    g.Add(El.Div("rr-gate-actions", Plain(CasinoText.T("rem.leave"), Close, "")));
                    break;
                case "excluded":
                {
                    long until = s.SelfExcludedUntilMs ?? 0;
                    g.Add(Badge("II", true));
                    g.Add(El.Text(CasinoText.T("gate.exTitle"), "rr-gate-title"));
                    g.Add(El.Text(CasinoText.F("gate.exBody", CasinoText.Date(until)), "rr-gate-body"));
                    var left = El.Text("", "rr-gate-note");
                    left.schedule.Execute(() => left.text = CasinoText.F("gate.exLeft", CasinoText.Countdown((until - ServerClock.NowMs) / 1000.0))).Every(1000);
                    left.text = CasinoText.F("gate.exLeft", CasinoText.Countdown((until - ServerClock.NowMs) / 1000.0));
                    g.Add(left);
                    g.Add(El.Div("rr-gate-actions", Plain(CasinoText.T("tab.fair"), () => Select(Fair), ""), Plain(CasinoText.T("rem.leave"), Close, "")));
                    break;
                }
                case "cap":
                {
                    g.Add(Badge("%", true));
                    g.Add(El.Text(CasinoText.T("gate.capTitle"), "rr-gate-title"));
                    g.Add(El.Text(CasinoText.T(s.StakeCapHit ? "gate.capStake" : "gate.capLoss"), "rr-gate-body"));
                    var back = El.Text("", "rr-gate-note");
                    back.schedule.Execute(() => back.text = CasinoText.F("gate.capBack", CasinoText.Countdown((s.ResetsAtMs - ServerClock.NowMs) / 1000.0))).Every(1000);
                    back.text = CasinoText.F("gate.capBack", CasinoText.Countdown((s.ResetsAtMs - ServerClock.NowMs) / 1000.0));
                    g.Add(back);
                    g.Add(El.Div("rr-gate-actions", Plain(CasinoText.T("tab.limits"), () => Select(Limits), ""), Plain(CasinoText.T("rem.leave"), Close, "")));
                    break;
                }
            }
            g.style.opacity = 0f;
            Tween.Run(g, 0.25f, t => g.style.opacity = t, Ease.OutCubic, skipWhenReduced: false);
            return g;
        }

        private static VisualElement Badge(string text, bool calm)
        {
            var b = El.Div("rr-gate-badge" + (calm ? " rr-gate-badge--calm" : ""), El.Text(text, "rr-gate-badge-label"));
            return b;
        }

        /// <summary>A button without the theme's `unity-button` class, so the casino's own styles are the only ones (pressed and
        /// hovered states of the default theme would otherwise repaint it grey).</summary>
        public static Button Btn(string text, Action click, string cls)
        {
            var b = El.Btn(text, click, cls);
            b.RemoveFromClassList("unity-button");
            return b;
        }

        public static Button Plain(string text, Action click, string kind)
        {
            var b = RugRoomSheet.Btn("", click, "rr-plain" + (string.IsNullOrEmpty(kind) ? "" : " rr-plain--" + kind));
            var l = El.Text(text, "rr-plain-label");
            l.pickingMode = PickingMode.Ignore;
            b.Add(l);
            return b;
        }

        // ------------------------------------------------------------------ session reminder, toasts, modals

        private void OnReminder(int minutes)
        {
            var card = El.Div("rr-modal-card");
            card.Add(El.Text(CasinoText.T("rem.title"), "rr-modal-title"));
            card.Add(El.Text(CasinoText.F("rem.body", minutes), "rr-modal-body"));
            card.Add(El.Text(Svc.Disclaimer, "rr-disclaimer"));
            VisualElement modal = null;
            card.Add(El.Div("rr-modal-actions",
                Plain(CasinoText.T("rem.limits"), () => { CloseModal(modal); Select(Limits); }, ""),
                Plain(CasinoText.T("rem.leave"), () => { CloseModal(modal); Close(); }, ""),
                Plain(CasinoText.T("rem.keep"), () => CloseModal(modal), "mint")));
            modal = ShowModal(card);
            modal.name = "rr-reminder";
        }

        public VisualElement ShowModal(VisualElement card)
        {
            var m = El.Div("rr-modal", card);
            m.pickingMode = PickingMode.Position;
            _modalHost.Add(m);
            m.style.opacity = 0f;
            Tween.Run(m, 0.2f, t => m.style.opacity = t, Ease.OutCubic, skipWhenReduced: false);
            Motion.Enter(card, 0f, 40f, 0.28f);
            Cues.Play("sfx_ui_open", 0.7f);
            return m;
        }

        public void CloseModal(VisualElement m) { if (m != null) m.RemoveFromHierarchy(); }

        public void Toast(string text, bool good = false)
        {
            _toast?.RemoveFromHierarchy();
            var t = El.Div("rr-toast" + (good ? " rr-toast--good" : ""), El.Text(text, "rr-toast-label"));
            t.name = "rr-toast";
            t.pickingMode = PickingMode.Ignore;
            _toastHost.Add(t);
            _toast = t;
            Motion.Enter(t, 0f, -30f, 0.25f);
            t.schedule.Execute(() => { if (_toast == t) Tween.Run(t, 0.3f, k => t.style.opacity = 1f - k, Ease.OutCubic, skipWhenReduced: false); }).StartingIn(2600);
            t.schedule.Execute(() => t.RemoveFromHierarchy()).StartingIn(3000);
        }

        /// <summary>A refused or failed casino call: a friendly line, the gates re-read.</summary>
        public void Fail(CityError err)
        {
            Toast(CasinoText.Error(err));
            Cues.Nope();
            Svc.AfterRefusal(err);
        }

        // ------------------------------------------------------------------ per frame

        private void Tick()
        {
            if (panel == null) return;
            float now = Time.unscaledTime, dt = Mathf.Min(0.1f, now - _last);
            _last = now;
            if (Services.TryGet<PlayerProfile>(out var p) && Time.frameCount % 6 == 0) _cash.text = CasinoText.Money(p.CashCents);
            if (Time.frameCount % 30 == 0) _session.text = CasinoText.F("hud.session", Math.Max(0, Svc.SessionMinutes));
            _current?.Tick(dt);
        }

        public void Close()
        {
            if (_current != null && _current.Busy) { Toast(CasinoText.T("err.ROUND_IN_PROGRESS")); return; }
            Cues.Play("sfx_ui_close");
            RemoveFromHierarchy();
        }

        // Infinix (2026-10-25): with the casino covering the screen the main thread still spent ~45 ms a frame on the world
        // behind it (traffic, pedestrians, the instance renderer's submissions, the HUD and 20+ name tags re-laid out every
        // frame). None of it can be seen, so it rests while the casino is open and resumes as it was on close.
        private readonly List<(VisualElement el, StyleEnum<DisplayStyle> was)> _hiddenLayers = new List<(VisualElement, StyleEnum<DisplayStyle>)>();
        private bool _hidWorld, _pausedTraffic, _stoppedPeds;

        private void PauseWorld(bool pause)
        {
            var ui = UiRoot.Instance;
            if (pause)
            {
                if (ui != null)
                    foreach (var layer in new[] { ui.WorldLayer, ui.LabelLayer, ui.HudLayer })
                        if (layer != null) { _hiddenLayers.Add((layer, layer.style.display)); layer.style.display = DisplayStyle.None; }
                if (!IslandInstanceRenderer.HideWorld) { IslandInstanceRenderer.HideWorld = true; _hidWorld = true; }
                var traffic = CTLife.World.Vehicles.TrafficSystem.Current;
                if (traffic != null && !traffic.Paused) { traffic.Paused = true; _pausedTraffic = true; }
                var peds = CTLife.World.Pedestrians.PedestrianSystem.Current;
                if (peds != null && peds.enabled) { peds.enabled = false; _stoppedPeds = true; }
                return;
            }
            foreach (var (el, was) in _hiddenLayers) el.style.display = was;
            _hiddenLayers.Clear();
            if (_hidWorld) { IslandInstanceRenderer.HideWorld = IslandInteriors.Current != null && IslandInteriors.Current.WorldHidden; _hidWorld = false; }
            if (_pausedTraffic) { var t = CTLife.World.Vehicles.TrafficSystem.Current; if (t != null) t.Paused = false; _pausedTraffic = false; }
            if (_stoppedPeds) { var p = CTLife.World.Pedestrians.PedestrianSystem.Current; if (p != null) p.enabled = true; _stoppedPeds = false; }
        }

        private void Teardown()
        {
            _tick?.Pause();
            if (_cam != null)
            {
                if (_cam.cullingMask == 0) _cam.cullingMask = _camMask;
                _cam.rect = _camRect;
            }
            PauseWorld(false);
            Svc.ScreensOpen = false;
            Svc.StateChanged -= OnState;
            Svc.SessionReminder -= OnReminder;
            foreach (var p in _panels.Values) p.Disposed();
            if (ReferenceEquals(_open, this)) _open = null;
            Log.Info("casino", "Rug Room closed");
        }
    }

    /// <summary>One screen of the Rug Room.</summary>
    public abstract class RugPanel : VisualElement
    {
        protected readonly RugRoomSheet Sheet;
        protected CasinoService Svc => Sheet.Svc;
        protected CasinoState State => Sheet.Svc.State;

        protected RugPanel(RugRoomSheet sheet)
        {
            Sheet = sheet;
            AddToClassList("rr-panel");
        }

        /// <summary>A round is in flight: tabs and close wait.</summary>
        public virtual bool Busy => false;
        public virtual void Shown() { }
        public virtual void Hidden() { }
        public virtual void StateChanged() { }
        public virtual void Tick(float dt) { }
        public virtual void Disposed() { }

        protected static Label Cap(string text) => El.Text(text, "rr-cap");
    }

    /// <summary>
    /// The stake picker: real casino chips (Painter2D: a coloured disc, six edge inserts, a brass inner ring) from $1 to $100
    /// within the table limits; a chip above today's room under the caps is dimmed.
    /// </summary>
    public sealed class StakeChips : VisualElement
    {
        private static readonly long[] Values = { 100, 200, 500, 1000, 2500, 5000, 10000 };
        private static readonly Color[] Colors =
        {
            new Color32(0xf2, 0xf2, 0xf2, 0xff), new Color32(0xff, 0x5a, 0x7a, 0xff), new Color32(0xe8, 0x36, 0x4f, 0xff), new Color32(0x2f, 0x7b, 0xff, 0xff),
            new Color32(0x22, 0xb5, 0x73, 0xff), new Color32(0x24, 0x24, 0x2a, 0xff), new Color32(0x8a, 0x4d, 0xff, 0xff),
        };
        private readonly RugRoomSheet _sheet;
        private readonly List<(long v, Chip chip)> _chips = new List<(long, Chip)>();
        private readonly Label _max;
        public event Action Changed;

        public StakeChips(RugRoomSheet sheet)
        {
            _sheet = sheet;
            Add(El.Text(CasinoText.T("stake"), "rr-cap"));
            var row = El.Div("rr-chips");
            for (int i = 0; i < Values.Length; i++)
            {
                long v = Values[i];
                var chip = new Chip(Colors[i], CasinoText.Chip(v));
                chip.name = "rr-chip-" + v;
                chip.RegisterCallback<PointerUpEvent>(_ => Pick(v));
                _chips.Add((v, chip));
                row.Add(chip);
            }
            Add(row);
            _max = El.Text("", "rr-stake-max");
            Add(_max);
            Refresh();
        }

        private void Pick(long v)
        {
            var s = _sheet.Svc.State;
            if (s != null && (v > s.StakeRoomCents || v < s.MinStakeCents || v > s.MaxStakeCents)) { Cues.Nope(); return; }
            _sheet.StakeCents = v;
            Cues.Play("sfx_chips", 0.6f);
            DeviceFeel.Tick();
            Refresh();
            Changed?.Invoke();
        }

        public void Refresh()
        {
            var s = _sheet.Svc.State;
            foreach (var (v, chip) in _chips)
            {
                bool allowed = s == null || (v >= s.MinStakeCents && v <= s.MaxStakeCents);
                bool room = s == null || v <= s.StakeRoomCents;
                chip.Show(allowed);
                chip.EnableInClassList("rr-chip--off", !room);
                chip.Selected = v == _sheet.StakeCents;
            }
            if (s != null && _sheet.StakeCents > s.StakeRoomCents && s.StakeRoomCents >= s.MinStakeCents)
            {
                // drop to the biggest chip that still fits today
                for (int i = _chips.Count - 1; i >= 0; i--)
                    if (_chips[i].v <= s.StakeRoomCents && _chips[i].v >= s.MinStakeCents) { _sheet.StakeCents = _chips[i].v; break; }
                foreach (var (v, chip) in _chips) chip.Selected = v == _sheet.StakeCents;
            }
            _max.text = s == null ? "" : CasinoText.F("stake.max", CasinoText.Money(Math.Max(0, s.StakeRoomCents)));
        }

        private sealed class Chip : VisualElement
        {
            private readonly Color _color;
            private bool _selected;
            public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; MarkDirtyRepaint(); style.translate = new Translate(0, value ? -8 : 0); } }

            public Chip(Color color, string label)
            {
                _color = color;
                AddToClassList("rr-chip");
                generateVisualContent += Draw;
                var l = El.Text(label, "rr-chip-label");
                l.pickingMode = PickingMode.Ignore;
                Add(l);
            }

            private void Draw(MeshGenerationContext ctx)
            {
                var r = contentRect;
                if (r.width <= 0 || float.IsNaN(r.width)) return;
                var p = ctx.painter2D;
                var c = r.center; float R = Mathf.Min(r.width, r.height) * 0.5f - 2f;
                if (_selected)
                {
                    p.fillColor = new Color(1f, 0.85f, 0.4f, 0.35f);
                    p.BeginPath(); p.Arc(c, R + 2f, 0f, 360f); p.Fill();
                }
                p.fillColor = new Color(0, 0, 0, 0.35f);
                p.BeginPath(); p.Arc(c + new Vector2(0, 4f), R - 1f, 0f, 360f); p.Fill();
                p.fillColor = _color;
                p.BeginPath(); p.Arc(c, R - 2f, 0f, 360f); p.Fill();
                // six edge inserts
                p.fillColor = new Color(1f, 1f, 1f, _color.grayscale > 0.8f ? 0.0f : 0.92f);
                if (_color.grayscale > 0.8f) p.fillColor = new Color(0.85f, 0.2f, 0.3f, 0.9f);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 60f;
                    p.BeginPath();
                    p.Arc(c, R - 2f, a - 9f, a + 9f);
                    p.Arc(c, R * 0.72f, a + 9f, a - 9f, ArcDirection.CounterClockwise);
                    p.ClosePath();
                    p.Fill();
                }
                // brass inner ring and face
                p.strokeColor = new Color(0.96f, 0.80f, 0.40f, 1f);
                p.lineWidth = _selected ? 4f : 3f;
                p.BeginPath(); p.Arc(c, R * 0.62f, 0f, 360f); p.Stroke();
                p.fillColor = Color.Lerp(_color, Color.black, 0.18f);
                p.BeginPath(); p.Arc(c, R * 0.58f, 0f, 360f); p.Fill();
            }
        }
    }

    /// <summary>A countdown ring (Painter2D), for head-to-head expiry.</summary>
    public sealed class Ring : VisualElement
    {
        private float _value;
        private readonly Label _label;
        public Ring()
        {
            AddToClassList("rr-ch-ring");
            generateVisualContent += Draw;
            _label = El.Text("", "rr-ch-ring-label");
            Add(_label);
        }

        public void Set(float fraction, string text)
        {
            fraction = Mathf.Clamp01(fraction);
            if (Mathf.Abs(fraction - _value) > 0.002f) { _value = fraction; MarkDirtyRepaint(); }
            if (_label.text != text) _label.text = text;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width <= 0 || float.IsNaN(r.width)) return;
            var p = ctx.painter2D;
            var c = r.center; float R = Mathf.Min(r.width, r.height) * 0.5f - 4f;
            p.lineWidth = 5f;
            p.strokeColor = new Color(1f, 1f, 1f, 0.12f);
            p.BeginPath(); p.Arc(c, R, 0f, 360f); p.Stroke();
            if (_value <= 0f) return;
            p.strokeColor = _value < 0.25f ? new Color(1f, 0.3f, 0.43f) : new Color(0.96f, 0.79f, 0.36f);
            p.lineCap = LineCap.Round;
            p.BeginPath(); p.Arc(c, R, -90f, -90f + 360f * _value); p.Stroke();
        }
    }
}
