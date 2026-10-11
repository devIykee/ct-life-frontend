using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;
using Motion = CTLife.UI.Kit.Motion;

namespace CTLife.UI
{
    /// <summary>
    /// Signed-in home, the "Arena" lobby (approved mock lobby-v1, 2026-10-16): the live player character on a glowing mint
    /// podium in the real plaza render (soft purple vignette), a player plate (avatar, alias, tribe, Energy / Sanity meters),
    /// a skewed currency strip (Cash with top-up, debt, connected wallet), Messages and This week buttons, a sticker rail of
    /// the real destinations, angled event tiles (offline, market, today's clue), the Duo sticker, one "Invite fren" slot,
    /// The Dream as a pass bar and HOME + ENTER THE CITY. Every value is the server's or the wallet RPC's; an element hides
    /// when its data does not exist. Rules: HomeModel. Presentation: Resources/UI/home.uss. Shapes: LobbyArt.
    /// </summary>
    public sealed class DashboardScreen : UiScreen
    {
        private static readonly string[] TapEmotes = { "emote_thumbs_up", "emote_laugh", "emote_clap", "emote_point" };
        private const int TapLines = 3;

        private readonly Action _enterCity;
        private readonly Action<string> _openApp;
        private readonly bool _firstArrival;
        private VisualElement _root, _bg, _stage, _model, _today, _avatar, _meters;
        private WalletBalanceService _wallet;
        private Label _walletValue, _walletStatus;
        private Label _greeting, _eyebrow, _error, _marketValue, _clueText, _cashValue, _debtValue, _dreamTitle, _homeName;
        private SkewBox _enter, _cash, _debt, _walletPill, _dreamCard, _homeCard;
        private VisualElement _dreamFill, _dreamTrack, _energy, _sanity, _energyFill, _sanityFill;
        private Label _dreamProgress;
        private Button _marketChip, _clueChip, _offlineChip, _invite;
        private SpeechBubble _bubble;
        private Popover _popover;
        private AvatarPreview _preview;
        private Subscriptions _subs;
        private Label _phoneBadge, _messagesBadge;
        private ConfigStore _config;
        private HomeState _state;
        private CancellationTokenSource _life;
        private IVisualElementScheduledItem _poll;
        private int? _mood;
        private int _taps, _dragPointer = -1;
        private float _dragDistance;
        private bool _busy, _greeted, _clueRequested;
        private string _lookKey, _avatarHead;

        public DashboardScreen(Action enterCity, Action<string> openApp, bool firstArrival = false)
        {
            _enterCity = enterCity;
            _openApp = openApp;
            _firstArrival = firstArrival;
        }

        /// <summary>
        /// Today's daily clue teaser (GET /daily-clue, wired by the FlowController through the City Life lane), or null when
        /// there is none or the route is not live. The tile only appears when this returns text.
        /// </summary>
        public Func<CancellationToken, Task<string>> DailyClue { get; set; }

        /// <summary>Captures and tests: force the offline state (null = measured: no network, or the city room not joined).</summary>
        public bool? OfflineOverride { get; set; }

        public HomeState State => _state;
        public bool FirstArrival => _firstArrival;

        /// <summary>
        /// "Invite fren" opens Duo Calls: its Favourites are the people you follow, tap to add them, then call (the game has no
        /// party or referral system; a call is the real way to bring a followed player in).
        /// </summary>
        public const string InviteApp = "calls";

        protected override VisualElement Build()
        {
            _bg = El.Div("home-bg");
            _bg.pickingMode = PickingMode.Ignore;
            var art = Resources.Load<Texture2D>("UI/Lobby/lobby-plaza") ?? OnboardingArt.Backdrop;
            if (art != null) _bg.style.backgroundImage = new StyleBackground(art);
            var tint = El.Div("home-tint");
            tint.pickingMode = PickingMode.Ignore;
            tint.style.backgroundImage = new StyleBackground(LobbyArt.Vignette);

            BuildStage();
            _bubble = new SpeechBubble(SpeechBubble.Tail.BottomLeft);
            _bubble.AddToClassList("home-bubble");

            var content = El.Div("home-content", _stage, BuildInvite(), BuildPlate(), BuildCurrencies(), BuildCorner(), BuildRail(), BuildRight(),
                BuildDream(), BuildCta(), _bubble);
            content.pickingMode = PickingMode.Ignore;
            _popover = new Popover();
            _root = El.Div("ct-lobby home-screen", _bg, tint, content, _popover);
            OnboardingFrame.AddSheet(_root, "UI/kit");
            OnboardingFrame.AddSheet(_root, "UI/home");
            SafeAreaBleed.Attach(_root, _bg);
            SafeAreaBleed.Attach(_root, tint);
            return _root;
        }

        // ------------------------------------------------------------------ top-left plate

        private VisualElement BuildPlate()
        {
            _avatar = El.Div("home-avatar"); // taps on the round avatar bubble up to the identity button
            _greeting = El.Text("", "home-greeting");
            _greeting.enableRichText = false;
            _eyebrow = El.Text("", "home-eyebrow");
            var bar = new SkewBox("home-namebar") { Fill = LobbyArt.Plum }.Shape(0f, 26f);
            bar.pickingMode = PickingMode.Ignore;
            bar.Add(_greeting);
            bar.Add(_eyebrow);
            var identity = El.Btn("", () => OpenApp("profile"), "home-identity");
            identity.name = "home-identity";
            identity.Add(bar);
            identity.Add(_avatar); // drawn over the name bar, like the mock

            (_energy, _energyFill) = Meter("energy", "lightning-bolt", "home-meter-fill--energy");
            (_sanity, _sanityFill) = Meter("sanity", "brain", "home-meter-fill--sanity");
            _meters = El.Div("home-meters", _energy, _sanity);
            _meters.pickingMode = PickingMode.Ignore;
            var plate = El.Div("home-plate", identity, _meters);
            plate.pickingMode = PickingMode.Ignore;
            return plate;
        }

        private (Button, VisualElement) Meter(string id, string icon, string fillClass)
        {
            var fill = El.Div("home-meter-fill " + fillClass);
            fill.pickingMode = PickingMode.Ignore;
            var track = El.Div("home-meter-track", fill);
            track.pickingMode = PickingMode.Ignore;
            var b = El.Btn("", () => ExplainStat(id), "home-meter");
            b.name = "home-chip-" + id;
            b.tooltip = Strings.T("home." + id);
            b.Add(LobbyArt.Icon(icon, "home-meter-icon"));
            b.Add(track);
            return (b, fill);
        }

        private void RefreshAvatar(PlayerProfile profile)
        {
            string head = profile?.LookArray() is string[] look && look.Length > 1 ? look[1] : null;
            if (head == _avatarHead) return;
            _avatarHead = head;
            var portrait = string.IsNullOrEmpty(head) ? null : Resources.Load<Texture2D>("UI/Customizer/Portraits/" + head);
            _avatar.Clear();
            if (portrait != null) _avatar.style.backgroundImage = new StyleBackground(portrait);
            else
            {
                _avatar.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                var alias = _state?.Alias ?? profile?.Alias ?? "?";
                _avatar.Add(El.Text(System.Globalization.StringInfo.GetNextTextElement(alias.Trim().Length > 0 ? alias.Trim() : "?").ToUpperInvariant(), "home-avatar-letter"));
            }
        }

        // ------------------------------------------------------------------ currencies, corner

        private VisualElement BuildCurrencies()
        {
            _cashValue = El.Text("", "home-pill-value");
            _cash = Pill("cash", "coins", _cashValue, () => ExplainStat("cash"));
            var plus = new Button(() => { DeviceFeel.Tick(); Cues.Play(Cues.SfxTap); OpenApp("topup"); }) { name = "home-topup", tooltip = Strings.T("bank.topup") };
            plus.AddToClassList("home-plus");
            plus.Add(El.Text("+", "home-plus-label"));
            _cash.Add(plus);
            _debtValue = El.Text("", "home-pill-value");
            _debt = Pill("debt", "bank", _debtValue, () => ExplainStat("debt"));
            _debt.Fill = new Color(120f / 255f, 10f / 255f, 40f / 255f, 0.82f);
            _walletValue = El.Text("", "home-pill-value");
            _walletStatus = El.Text("", "home-pill-status");
            _walletPill = Pill("wallet", null, _walletValue, ExplainWallet);
            _walletPill.name = "home-wallet";
            var mark = El.Div("home-sol-mark");
            mark.pickingMode = PickingMode.Ignore;
            _walletPill.Insert(0, mark);
            _walletPill.Add(_walletStatus);
            var strip = El.Div("home-currencies", _cash, _debt, _walletPill);
            strip.pickingMode = PickingMode.Ignore;
            return strip;
        }

        private SkewBox Pill(string id, string icon, Label value, Action onTap)
        {
            var pill = new SkewBox("home-pill") { Fill = LobbyArt.Dark, Border = new Color(1f, 1f, 1f, 0.12f), BorderWidth = 3.6f }.Shape(14f, 14f);
            pill.name = "home-chip-" + id;
            pill.focusable = true;
            if (icon != null) pill.Add(LobbyArt.Icon(icon, "home-pill-icon"));
            value.pickingMode = PickingMode.Ignore;
            pill.Add(value);
            pill.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.target is VisualElement t && t.GetFirstAncestorOfType<Button>() is Button b && pill.Contains(b)) return; // the + button
                if (_busy) return;
                DeviceFeel.Tick();
                Cues.Play(Cues.SfxTap);
                onTap?.Invoke();
            });
            return pill;
        }

        private VisualElement BuildCorner()
        {
            var messages = CornerButton("chat", "messages", Strings.T("lobby.messages"));
            messages.name = "home-messages";
            _messagesBadge = Duo.Badge(DuoNotificationCenter.Badge("messages"), "lobby-badge home-corner-badge");
            messages.Add(_messagesBadge);
            var week = CornerButton("calendar", "week", Strings.T("lobby.week"));
            week.name = "home-week";
            var corner = El.Div("home-corner", messages, week);
            corner.pickingMode = PickingMode.Ignore;
            return corner;
        }

        private Button CornerButton(string icon, string app, string tooltip)
        {
            var b = new Button(() => { DeviceFeel.Tick(); Cues.Play(Cues.SfxTap); OpenApp(app); }) { tooltip = tooltip };
            b.AddToClassList("home-corner-button");
            b.Add(LobbyArt.Icon(icon, "home-corner-icon"));
            return b;
        }

        // ------------------------------------------------------------------ rail, right column

        private VisualElement BuildRail()
        {
            var rail = El.Div("home-rail",
                Sticker("treasure-map", "lobby.map", "map", -4f),
                Sticker("briefcase", "lobby.jobs", "jobs", 3f),
                Sticker("bank", "lobby.bank", "bank", -4f),
                Sticker("shop", "lobby.shop", "shop", 3f));
            rail.pickingMode = PickingMode.Ignore;
            return rail;
        }

        private LobbySticker Sticker(string icon, string labelKey, string app, float tilt)
        {
            var s = new LobbySticker(icon, Strings.T(labelKey), () => OpenApp(app), tilt);
            s.name = "home-tile-" + app;
            return s;
        }

        private VisualElement BuildRight()
        {
            _marketValue = El.Text("", "home-today-value");
            _marketChip = EventTile("coins", "home.today.marketLive", _marketValue, new Color32(0x3d, 0xdc, 0x84, 0xff), ExplainMarket);
            _marketChip.name = "home-today-market";
            _clueText = El.Text("", "home-today-value");
            _clueChip = EventTile("calendar", "home.today.clue", _clueText, new Color32(0xff, 0xd2, 0x3f, 0xff), () => OpenApp("week"));
            _clueChip.name = "home-today-clue";
            _offlineChip = EventTile("lightning-bolt", "home.today.offline", El.Text(Strings.T("home.today.offlineValue"), "home-today-value"),
                new Color32(0xff, 0x3b, 0x5c, 0xff), null);
            _offlineChip.name = "home-today-offline";
            _marketChip.style.display = DisplayStyle.None;
            _clueChip.style.display = DisplayStyle.None;
            _offlineChip.style.display = DisplayStyle.None;
            _today = El.Div("home-today", _offlineChip, _marketChip, _clueChip);
            _today.pickingMode = PickingMode.Ignore;

            var duo = new LobbySticker("smartphone", Strings.T("lobby.duo"), () => OpenApp(null), 3f, dark: true);
            duo.name = "home-tile-phone";
            _phoneBadge = duo.Badge;
            _phoneBadge.name = "home-phone-badge";
            Duo.SetBadge(_phoneBadge, DuoNotificationCenter.TotalBadge);
            var right = El.Div("home-right", _today, duo);
            right.pickingMode = PickingMode.Ignore;
            return right;
        }

        private Button EventTile(string icon, string captionKey, Label value, Color edge, Action onTap)
        {
            var b = new Button(() => { if (onTap == null) return; DeviceFeel.Tick(); Cues.Play(Cues.SfxTap); onTap(); });
            b.AddToClassList("home-today-chip");
            var box = new SkewBox("home-today-box") { Fill = LobbyArt.Dark, Edge = edge }.Shape(0f, 18f);
            box.pickingMode = PickingMode.Ignore;
            var cap = El.Text(Strings.T(captionKey).ToUpperInvariant(), "home-today-caption");
            cap.style.color = edge;
            cap.pickingMode = PickingMode.Ignore;
            value.pickingMode = PickingMode.Ignore;
            value.enableRichText = false;
            var col = El.Div("home-today-copy", cap, value);
            col.pickingMode = PickingMode.Ignore;
            box.Add(LobbyArt.Icon(icon, "home-today-icon"));
            box.Add(col);
            b.Add(box);
            if (onTap == null) b.focusable = false;
            return b;
        }

        // ------------------------------------------------------------------ invite, dream, home + enter

        private VisualElement BuildInvite()
        {
            _invite = new Button(() => { DeviceFeel.Tick(); Cues.Play(Cues.SfxTap); OpenApp(InviteApp); }) { name = "home-invite", tooltip = Strings.T("lobby.invite") };
            _invite.AddToClassList("home-invite");
            var sil = El.Div("home-invite-silhouette");
            sil.pickingMode = PickingMode.Ignore;
            var add = El.Div("home-invite-add", El.Text("+", "home-invite-plus"));
            add.pickingMode = PickingMode.Ignore;
            _invite.Add(sil);
            _invite.Add(add);
            _invite.Add(LobbyArt.Outlined(Strings.T("lobby.invite"), "home-invite-caption"));
            return _invite;
        }

        private VisualElement BuildDream()
        {
            var badge = El.Div("home-dream-badge", LobbyArt.Icon("crown", "home-dream-crown"));
            badge.style.backgroundImage = new StyleBackground(LobbyArt.Gold);
            badge.pickingMode = PickingMode.Ignore;
            _dreamTitle = El.Text("", "home-dream-title");
            _dreamTitle.enableRichText = false;
            _dreamProgress = El.Text("", "home-dream-progress");
            _dreamFill = El.Div("home-dream-fill");
            _dreamTrack = El.Div("home-dream-bar", _dreamFill);
            var row = El.Div("home-dream-row", _dreamTitle, _dreamProgress);
            var body = new SkewBox("home-dream-body") { Fill = new Color(20f / 255f, 6f / 255f, 44f / 255f, 0.82f) }.Shape(0f, 22f);
            body.Add(El.Text(Strings.T("home.dream").ToUpperInvariant(), "home-dream-caption"));
            body.Add(row);
            body.Add(_dreamTrack);
            foreach (var e in body.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
            _dreamCard = body;
            var pass = El.Btn("", () => OpenApp("profile"), "home-card home-dream");
            pass.name = "home-dream";
            pass.Add(body);
            pass.Add(badge);
            return pass;
        }

        private VisualElement BuildCta()
        {
            _homeName = El.Text("", "home-ribbon-name");
            _homeName.enableRichText = false;
            var tag = El.Text(Strings.T("home.home").ToUpperInvariant(), "home-ribbon-tag");
            var ribbon = new SkewBox("home-ribbon") { Fill = new Color32(0xff, 0xd2, 0x3f, 0xff), Drop = LobbyArt.Plum, DropHeight = 7f }.Shape(14f, 14f);
            ribbon.Add(tag);
            ribbon.Add(_homeName);
            foreach (var e in ribbon.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
            _homeCard = ribbon;
            var home = El.Btn("", () => OpenApp("home"), "home-card home-home");
            home.name = "home-home";
            home.Add(ribbon);

            _error = El.Text("", "home-error");
            _error.style.display = DisplayStyle.None;
            _enter = new SkewBox("home-enter-face")
            {
                Fill = new Color32(0x5f, 0xf7, 0xa6, 0xff), FillBottom = new Color32(0x1f, 0xcf, 0x72, 0xff),
                Lip = new Color32(0x12, 0x8a, 0x49, 0xff), LipHeight = 16f, Drop = LobbyArt.Plum, DropHeight = 14f,
            }.Shape(32f, 32f);
            _enter.pickingMode = PickingMode.Ignore;
            _enterLabel = El.Text(Strings.T("lobby.enter").ToUpperInvariant(), "home-enter-label");
            _enter.Add(_enterLabel);
            _enter.Add(El.Text("›", "home-enter-chevron"));
            foreach (var e in _enter.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
            _enterButton = new Button(OnEnterTap) { name = "home-enter" };
            _enterButton.AddToClassList("home-enter");
            _enterButton.Add(_enter);
            var zone = El.Div("home-cta-zone", _error, home, _enterButton);
            zone.pickingMode = PickingMode.Ignore;
            return zone;
        }

        private Button _enterButton;
        private Label _enterLabel;

        private void OnEnterTap()
        {
            if (_busy || !_enterButton.enabledSelf) return;
            DeviceFeel.Confirm();
            OnEnter();
        }

        // ------------------------------------------------------------------ the character

        private void BuildStage()
        {
            _preview = new AvatarPreview(1080, 1080, transparent: true);
            _preview.SetFullBodyReach(true);
            var glow = El.Div("home-stage-glow");
            glow.style.backgroundImage = new StyleBackground(LobbyArt.Podium);
            var shadow = El.Div("home-stage-shadow");
            _model = El.Div("home-stage-model");
            _model.style.backgroundImage = Background.FromRenderTexture(_preview.Texture);
            _stage = El.Div("home-stage", glow, shadow, _model);
            _stage.pickingMode = PickingMode.Ignore;
            glow.pickingMode = PickingMode.Ignore;
            shadow.pickingMode = PickingMode.Ignore;

            // Drag turns the character; a tap (no real drag) makes it react.
            _model.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_dragPointer >= 0 || e.button != 0 || _lookKey == null) return;
                _dragPointer = e.pointerId;
                _dragDistance = 0f;
                _model.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            _model.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_dragPointer != e.pointerId) return;
                _dragDistance += Mathf.Abs(e.deltaPosition.x) + Mathf.Abs(e.deltaPosition.y);
                _preview.Spin(e.deltaPosition.x);
                e.StopPropagation();
            });
            _model.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_dragPointer != e.pointerId) return;
                _dragPointer = -1;
                _model.ReleasePointer(e.pointerId);
                if (_dragDistance < 14f) TapCharacter();
            });
            _model.RegisterCallback<PointerCancelEvent>(e => { if (_dragPointer == e.pointerId) { _dragPointer = -1; _model.ReleasePointer(e.pointerId); } });
            _model.RegisterCallback<PointerCaptureOutEvent>(_ => _dragPointer = -1);
        }

        /// <summary>Captures and automation: the same as tapping the character.</summary>
        public void TapCharacter()
        {
            DeviceFeel.Tick();
            _taps++;
            React(OnboardingCopy.Variant("home.say.tap", _taps, TapLines), TapEmotes[_taps % TapEmotes.Length]);
        }

        private void React(string line, params string[] emotes)
        {
            if (!string.IsNullOrEmpty(line)) _bubble.Say(line, 3.5f);
            _preview?.PlayReaction(emotes);
        }

        private void LoadCharacter(PlayerProfile profile)
        {
            if (_preview == null) return;
            string personalJob = profile?.LikenessJobId;
            if (!string.IsNullOrEmpty(personalJob))
            {
                string personalKey = "personal:" + personalJob;
                if (_lookKey == personalKey) return;
                _preview.SetIdle("loco_idle");
                if (!_preview.TrySetLikeness(personalJob, out var failure))
                {
                    // EnterGame prepares the equipped asset before opening home. If that preparation is lost,
                    // do not present the stock look stored alongside it as the player's personal character.
                    _preview.SetVisible(false);
                    _lookKey = null;
                    ShowError(Strings.T("likeness.badModel"));
                    Log.Warn("home", "Equipped personal character could not be presented: " + failure);
                    return;
                }
                _preview.SetYaw(0f); // Normalized +Z faces the preview camera at +Z.
                _preview.SetVisible(true);
                _lookKey = personalKey;
                return;
            }
            string[] look = profile?.LookArray();
            bool complete = look != null && look.Length == 7 && Array.TrueForAll(look, id => !string.IsNullOrEmpty(id));
            if (!complete) look = CreatorLookCatalog.FallbackLook(profile?.UserId ?? profile?.Alias);
            var key = look == null ? null : string.Join("|", look);
            if (key == null || key == _lookKey) return;
            _lookKey = key;
            _preview.SetIdle("loco_idle");
            _preview.SetLook(look);
            _preview.SetYaw(CreatorLookCatalog.PreviewYawFor(look));
            _preview.SetVisible(true);
        }

        // ------------------------------------------------------------------ actions

        private void OpenApp(string app)
        {
            if (_busy) return;
            _popover?.Hide();
            _openApp?.Invoke(app);
        }

        private void OnEnter()
        {
            if (_busy) return;
            ShowError("");
            _popover?.Hide();
            Cues.Step();
            _enterCity?.Invoke();
        }

        /// <summary>Captures and automation: ENTER THE CITY, as a tap.</summary>
        public void PressEnter() => OnEnterTap();

        /// <summary>Captures and automation: taps the status element with this id ("cash", "debt", "energy", "sanity").</summary>
        public void TapChip(string id) => ExplainStat(id);

        /// <summary>Captures and automation: taps "Invite fren".</summary>
        public void PressInvite() => OpenApp(InviteApp);

        private void RefreshWallet()
        {
            if (_walletPill == null) return;
            var state = _wallet?.State;
            var sol = state?.Token("SOL");
            bool disconnected = state == null || state.Status == WalletBalanceStatus.Disconnected;
            bool stale = sol?.IsStale == true;
            bool failed = sol?.State == WalletTokenState.Unavailable;
            _walletValue.text = disconnected ? Strings.T("lobby.wallet.none")
                : (sol?.Amount >= 10000m ? LobbyAmounts.Compact(sol.Amount.Value) : sol?.Amount != null ? sol.FormatAmount(3) : "—") + " SOL";
            string status = disconnected ? null : stale ? "wallet.stale" : state.IsRefreshing && sol?.Amount == null ? "wallet.refreshing" : failed ? "wallet.unavailable" : null;
            _walletStatus.text = status != null ? Strings.T(status) : "";
            _walletStatus.style.display = status != null ? DisplayStyle.Flex : DisplayStyle.None;
            _walletPill.EnableInClassList("home-pill--dim", disconnected || stale || failed);
        }

        /// <summary>The wallet pill opens a popover with every token (SOL, USDC, SKR, with their states) and Open wallet.</summary>
        private void ExplainWallet()
        {
            var state = _wallet?.State;
            string body;
            if (state == null || state.Status == WalletBalanceStatus.Disconnected) body = Strings.T("wallet.disconnected");
            else
            {
                var lines = new System.Collections.Generic.List<string>();
                foreach (var symbol in new[] { "SOL", "USDC", "SKR" })
                {
                    var t = state.Token(symbol);
                    if (t == null) continue;
                    string v = t.Amount.HasValue ? (t.Amount >= 10000m ? LobbyAmounts.Compact(t.Amount.Value) : t.FormatAmount(symbol == "SOL" ? 4 : 2)) : "—";
                    lines.Add(v + " " + symbol + (t.IsStale ? " · " + Strings.T("wallet.stale") : t.State == WalletTokenState.Unavailable ? " · " + Strings.T("wallet.unavailable") : ""));
                }
                body = string.Join("\n", lines);
            }
            _popover.Show(_walletPill.worldBound, Strings.T("lobby.wallet.title"), body, Strings.T("lobby.wallet.open"), () => OpenApp("wallet"));
        }

        private void ExplainStat(string id)
        {
            if (_busy) return;
            var stat = _state?.Stats.Find(s => s.id == id);
            if (stat == null) return;
            var anchor = id == "cash" ? _cash : id == "debt" ? _debt : id == "energy" ? _energy : (VisualElement)_sanity;
            _popover.Show(anchor.worldBound, Strings.T(stat.explainTitleKey), stat.explainBody,
                stat.actionApp != null && _openApp != null ? Strings.T(stat.actionKey) : null,
                stat.actionApp != null ? () => OpenApp(stat.actionApp) : (Action)null);
        }

        private void ExplainMarket()
        {
            if (!_mood.HasValue) return;
            _popover.Show(_marketChip.worldBound, Strings.T("home.explain.market"),
                Strings.F("home.explain.marketBody", Money.Bps(_mood.Value)));
        }

        // ------------------------------------------------------------------ lifecycle

        protected override void OnShown()
        {
            if (_subs == null)
            {
                _subs = new Subscriptions();
                _subs.On<ProfileChangedSignal>(_ => Refresh());
                _subs.On<MoodSignal>(m => { _mood = m.bps; Refresh(); });
                _subs.On<DuoBadgesChangedSignal>(s => { Duo.SetBadge(_phoneBadge, s.total); Duo.SetBadge(_messagesBadge, DuoNotificationCenter.Badge("messages")); });
            }
            Duo.SetBadge(_phoneBadge, DuoNotificationCenter.TotalBadge);
            Duo.SetBadge(_messagesBadge, DuoNotificationCenter.Badge("messages"));
            if (_config == null && Services.TryGet<ConfigStore>(out var config))
            {
                _config = config;
                _config.Changed += Refresh;
            }
            if (!_mood.HasValue && Services.TryGet<IRealtimeService>(out var rt) && rt.City != null && rt.City.IsJoined) _mood = rt.City.Mood;
            _life ??= new CancellationTokenSource();
            if (_wallet == null && Services.TryGet<WalletBalanceService>(out var wallet))
            { _wallet = wallet; _wallet.Changed += RefreshWallet; }
            RefreshWallet();
            if (_wallet != null) _ = _wallet.Refresh();
            _preview?.SetVisible(true);
            Refresh();
            // Re-check connectivity every 2 s (the city room retries on its own).
            _poll ??= _root.schedule.Execute(RefreshOffline).Every(2000);
            if (!_greeted)
            {
                _greeted = true;
                _root.schedule.Execute(() => { if (_state != null) _preview?.PlayReaction(_state.ReactionEmotes); }).StartingIn(500);
            }
            LoadClue();
        }

        private async void LoadClue()
        {
            if (_clueRequested || DailyClue == null) return;
            _clueRequested = true;
            try
            {
                var text = await DailyClue(_life.Token);
                if (_root?.panel == null || string.IsNullOrWhiteSpace(text)) return;
                _clueText.text = text.Length > 30 ? text.Substring(0, 28).TrimEnd() + "…" : text;
                _clueChip.tooltip = text;
                _clueChip.style.display = DisplayStyle.Flex;
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Debug("home", "daily clue unavailable: " + e.Message); }
        }

        public void SetBusy(bool busy)
        {
            _busy = busy;
            if (busy) ShowError("");
            Refresh();
        }

        /// <summary>An inline error above ENTER THE CITY (the toast still shows): the character reacts too.</summary>
        public void ShowError(string text)
        {
            if (_error == null) return;
            _error.text = text ?? "";
            bool on = !string.IsNullOrEmpty(text);
            _error.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            Cues.Nope();
            Motion.Shake(_error);
            React(Strings.T("home.say.error"), "emote_facepalm", "emote_shrug");
        }

        private bool Offline
        {
            get
            {
                if (OfflineOverride.HasValue) return OfflineOverride.Value;
                if (Application.internetReachability == NetworkReachability.NotReachable) return true;
                return Services.TryGet<IRealtimeService>(out var rt) && (rt.City == null || !rt.City.IsJoined);
            }
        }

        private void RefreshOffline()
        {
            if (_wallet != null) { RefreshWallet(); _ = _wallet.Refresh(); }
            if (_offlineChip == null) return;
            bool off = Offline || _state == null || !_state.HasProfile;
            _offlineChip.style.display = off ? DisplayStyle.Flex : DisplayStyle.None;
            _marketChip.style.display = !off && _mood.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Refresh()
        {
            if (_root == null) return;
            Services.TryGet<PlayerProfile>(out var profile);
            var me = profile?.Me;
            var reg = _config?.Registries;
            _state = HomeModel.Build(me, reg, _config?.Config("needs"), _config?.Config("lottery"), _mood, _firstArrival);
            LoadCharacter(profile);
            RefreshAvatar(profile);

            _greeting.text = _state.Alias ?? profile?.Alias ?? Strings.T("phone.greetingDefault");
            _eyebrow.text = string.IsNullOrEmpty(_state.TribeName) ? Strings.T("lobby.noTribe").ToUpperInvariant() : _state.TribeName.ToUpperInvariant();

            // Cash (always with a profile), debt only when owed, Energy / Sanity only when the server sent needs.
            var cash = _state.Stats.Find(x => x.id == "cash");
            _cash.style.display = cash != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (cash != null) _cashValue.text = profile != null ? LobbyAmounts.Cash(profile.CashCents) : cash.value;
            var debt = _state.Stats.Find(x => x.id == "debt");
            _debt.style.display = debt != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (debt != null) _debtValue.text = Strings.F("lobby.owe", profile != null ? LobbyAmounts.Cash(profile.DebtCents) : debt.value);
            SetMeter(_energy, _energyFill, _state.Stats.Find(x => x.id == "energy"));
            SetMeter(_sanity, _sanityFill, _state.Stats.Find(x => x.id == "sanity"));
            _meters.style.display = _energy.style.display == DisplayStyle.None && _sanity.style.display == DisplayStyle.None ? DisplayStyle.None : DisplayStyle.Flex;

            _dreamTitle.text = _state.DreamName ?? Strings.T("lobby.noDream");
            bool hasProgress = _state.Dream != null;
            _dreamProgress.text = hasProgress ? _state.Dream.Text() : "";
            _dreamProgress.style.display = hasProgress ? DisplayStyle.Flex : DisplayStyle.None;
            _dreamTrack.style.display = hasProgress ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasProgress) _dreamFill.style.width = Length.Percent(Mathf.Max(_state.Dream.Fraction, 0.02f) * 100f);
            _dreamCard.parent?.SetEnabled(_openApp != null && !_busy);
            _homeName.text = _state.HomeName ?? Strings.T("lobby.noHome");
            _homeCard.parent?.SetEnabled(_openApp != null && !_busy);

            if (_mood.HasValue)
            {
                _marketValue.text = Strings.F("lobby.marketToday", Money.Bps(_mood.Value));
                _marketChip.EnableInClassList("home-today--up", _mood.Value > 0);
                _marketChip.EnableInClassList("home-today--down", _mood.Value < 0);
            }
            _enterLabel.text = (_busy ? Strings.T("lobby.enterBusy") : Strings.T("lobby.enter")).ToUpperInvariant();
            _enterButton.SetEnabled(!_busy && profile?.HasCharacter == true && _enterCity != null);
            _enterButton.EnableInClassList("home-enter--first", _firstArrival);
            RefreshOffline();
        }

        private static void SetMeter(VisualElement meter, VisualElement fill, HomeStat stat)
        {
            meter.style.display = stat != null && stat.fill >= 0f ? DisplayStyle.Flex : DisplayStyle.None;
            if (stat == null || stat.fill < 0f) return;
            fill.style.width = Length.Percent(Mathf.Clamp01(stat.fill) * 100f);
            meter.EnableInClassList("home-meter--low", stat.level == StatLevel.Low);
            meter.EnableInClassList("home-meter--critical", stat.level == StatLevel.Critical);
            meter.tooltip = Strings.T(stat.captionKey) + " " + stat.value;
        }

        public override void Close()
        {
            _subs?.Dispose();
            if (_wallet != null) _wallet.Changed -= RefreshWallet;
            _wallet = null;
            _subs = null;
            if (_config != null) _config.Changed -= Refresh;
            _config = null;
            _poll?.Pause();
            _poll = null;
            _life?.Cancel();
            _life?.Dispose();
            _life = null;
            base.Close();
            _preview?.Dispose();
            _preview = null;
        }
    }
}
