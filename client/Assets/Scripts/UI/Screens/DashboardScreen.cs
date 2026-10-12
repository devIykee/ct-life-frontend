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
    /// Signed-in home: a live character on the illustrated city courtyard, compact identity and balances,
    /// edge navigation, real daily information and a single city-entry action. Wallet holdings are distinct
    /// from server-ledgered Cash. Rules: HomeModel. Presentation: Resources/UI/home.uss.
    /// </summary>
    public sealed class DashboardScreen : UiScreen
    {
        private static readonly string[] TapEmotes = { "emote_thumbs_up", "emote_laugh", "emote_clap", "emote_point" };
        private const int TapLines = 3;

        private readonly Action _enterCity;
        private readonly Action<string> _openApp;
        private readonly bool _firstArrival;
        private VisualElement _root, _bg, _stage, _model, _chips, _needs, _today, _cards, _tribeMark;
        private WalletBalanceService _wallet;
        private Label _walletSol, _walletUsdc, _walletStatus;
        private Label _eyebrow, _greeting, _error, _marketValue, _clueText;
        private LippedButton _enter, _settings;
        private ChoiceCard _dreamCard, _homeCard;
        private VisualElement _dreamBar, _dreamFill;
        private Label _dreamProgress;
        private Button _marketChip, _clueChip, _offlineChip;
        private SpeechBubble _bubble;
        private Popover _popover;
        private AvatarPreview _preview;
        private Subscriptions _subs;
        private ConfigStore _config;
        private HomeState _state;
        private CancellationTokenSource _life;
        private IVisualElementScheduledItem _poll;
        private int? _mood;
        private int _taps, _dragPointer = -1;
        private float _dragDistance;
        private bool _busy, _greeted, _clueRequested;
        private string _lookKey;

        public DashboardScreen(Action enterCity, Action<string> openApp, bool firstArrival = false)
        {
            _enterCity = enterCity;
            _openApp = openApp;
            _firstArrival = firstArrival;
        }

        /// <summary>
        /// Today's daily clue teaser (GET /daily-clue, wired by the FlowController through the City Life lane), or null when
        /// there is none or the route is not live. The chip only appears when this returns text.
        /// </summary>
        public Func<CancellationToken, Task<string>> DailyClue { get; set; }

        /// <summary>Captures and tests: force the offline state (null = measured: no network, or the city room not joined).</summary>
        public bool? OfflineOverride { get; set; }

        public HomeState State => _state;
        public bool FirstArrival => _firstArrival;

        protected override VisualElement Build()
        {
            _bg = El.Div("home-bg");
            _bg.pickingMode = PickingMode.Ignore;
            var art = Resources.Load<Texture2D>("UI/Onboarding/lobby-rooftop") ?? OnboardingArt.Backdrop;
            if (art != null) _bg.style.backgroundImage = new StyleBackground(art);

            BuildStage();
            _bubble = new SpeechBubble(SpeechBubble.Tail.BottomLeft);
            _bubble.AddToClassList("home-bubble");

            _chips = El.Div("home-chips");
            _chips.pickingMode = PickingMode.Ignore;
            _needs = El.Div("home-needs");
            _needs.pickingMode = PickingMode.Ignore;
            _settings = new LippedButton("", () => OpenApp("settings"), LippedButton.Tone.White, LippedButton.Size.Round).WithIconArt("settings", "settings");
            _settings.tooltip = Strings.T("app.settings");
            _settings.AddToClassList("home-settings");
            var top = El.Div("home-top", _chips, BuildWallet(), _settings);
            top.pickingMode = PickingMode.Ignore;

            var rail = El.Div("home-rail",
                Tile("map", "lobby.map", "cyan", "map"),
                Tile("jobs", "lobby.jobs", "coral", "jobs"),
                Tile("bank", "lobby.bank", "gold", "bank"),
                Tile("phone", "lobby.phone", "green", null));
            rail.pickingMode = PickingMode.Ignore;

            _tribeMark = El.Div("home-tribe-mark");
            _tribeMark.pickingMode = PickingMode.Ignore;
            _eyebrow = El.Text("", "home-eyebrow");
            var eyebrowRow = El.Div("home-eyebrow-row", _tribeMark, _eyebrow);
            eyebrowRow.pickingMode = PickingMode.Ignore;
            _greeting = El.Text("", "home-greeting");

            _marketValue = El.Text("", "home-today-value");
            _marketChip = TodayChip("perps", "home.today.market", _marketValue, ExplainMarket);
            _clueText = El.Text("", "home-today-value home-today-value--clue");
            _clueChip = TodayChip("dream", "home.today.clue", _clueText, () => OpenApp("week"));
            _offlineChip = TodayChip("close", "home.today.offline", El.Text(Strings.T("home.today.offlineValue"), "home-today-value"), null);
            _offlineChip.AddToClassList("home-today--offline");
            _marketChip.style.display = DisplayStyle.None;
            _clueChip.style.display = DisplayStyle.None;
            _offlineChip.style.display = DisplayStyle.None;
            _today = El.Div("home-today", _offlineChip, _marketChip, _clueChip);
            _today.pickingMode = PickingMode.Ignore;

            _dreamCard = new ChoiceCard("dream", "", null, _ => OpenApp("profile"), ChoiceCard.Layout.Horizontal, null, "dream", "gold", Strings.T("home.dream"));
            _dreamCard.AddToClassList("home-card");
            _dreamCard.AddToClassList("home-card--dream");
            _dreamProgress = El.Text("", "home-dream-progress");
            _dreamBar = El.Div("home-dream-bar");
            _dreamFill = El.Div("home-dream-fill");
            _dreamBar.Add(_dreamFill);
            El.PassThrough(_dreamBar);
            _dreamProgress.pickingMode = PickingMode.Ignore;
            var copy = _dreamCard.Face.Q(className: "kit-card__copy");
            copy?.Add(_dreamProgress);
            copy?.Add(_dreamBar);
            _homeCard = new ChoiceCard("home", "", null, _ => OpenApp("home"), ChoiceCard.Layout.Horizontal, null, "home", "green", Strings.T("home.home"));
            _homeCard.AddToClassList("home-card");
            _homeCard.AddToClassList("home-card--home");
            _cards = El.Div("home-cards", _dreamCard, _homeCard);
            _cards.pickingMode = PickingMode.Ignore;

            _error = El.Text("", "home-error");
            _error.style.display = DisplayStyle.None;
            _enter = new LippedButton(Strings.T("lobby.enter"), OnEnter, LippedButton.Tone.Green, LippedButton.Size.Large);
            _enter.name = "home-enter";
            _enter.AddToClassList("home-enter");
            _enter.WithIcon("next");

            var identity = El.Btn("", () => OpenApp("profile"), "home-identity");
            identity.name = "home-identity";
            identity.Add(El.Div("home-identity-copy", _greeting, eyebrowRow));
            var right = El.Div("home-right", _today, _cards);
            right.pickingMode = PickingMode.Ignore;
            var ctaZone = El.Div("home-cta-zone", _error, _enter);
            ctaZone.pickingMode = PickingMode.Ignore;

            var content = El.Div("home-content", _stage, rail, right, _bubble, ctaZone, top, identity, _needs);
            content.pickingMode = PickingMode.Ignore;
            _popover = new Popover();
            _root = El.Div("ct-lobby home-screen", _bg, content, _popover);
            OnboardingFrame.AddSheet(_root, "UI/kit");
            OnboardingFrame.AddSheet(_root, "UI/home");
            SafeAreaBleed.Attach(_root, _bg);
            // The character and its ground contact must stay aligned with the illustrated stage.
            return _root;
        }

        // ------------------------------------------------------------------ the character

        private void BuildStage()
        {
            _preview = new AvatarPreview(1080, 1080, transparent: true);
            _preview.SetFullBodyReach(true);
            var glow = El.Div("home-stage-glow");
            glow.style.backgroundImage = new StyleBackground(OnboardingArt.Glow);
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

        // ------------------------------------------------------------------ pieces

        private IconTile Tile(string icon, string labelKey, string tone, string app)
        {
            var t = new IconTile(icon, Strings.T(labelKey), tone, () => OpenApp(app), art: KitIcons.ForRoute(app));
            t.name = "home-tile-" + (app ?? "phone");
            return t;
        }

        private Button TodayChip(string icon, string captionKey, Label value, Action onTap)
        {
            var b = new Button(() => { if (onTap == null) return; Cues.Play(Cues.SfxTap); onTap(); }) { text = "", enableRichText = false };
            b.AddToClassList("home-today-chip");
            var badge = new IconBadge(icon, captionKey == "home.today.offline" ? "plum" : captionKey == "home.today.clue" ? "gold" : "cyan");
            var cap = El.Text(Strings.T(captionKey), "home-today-caption");
            cap.pickingMode = PickingMode.Ignore;
            value.pickingMode = PickingMode.Ignore;
            var col = El.Div("home-today-copy", cap, value);
            col.pickingMode = PickingMode.Ignore;
            b.Add(badge);
            b.Add(col);
            if (onTap == null) b.focusable = false;
            return b;
        }

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
        public void PressEnter() => _enter?.Press();

        /// <summary>Captures and automation: taps the status chip with this id ("cash", "debt", "energy", "sanity").</summary>
        public void TapChip(string id) => _root?.Query<StatChip>().Where(c => c.Id == id).First()?.Press();

        private Button BuildWallet()
        {
            _walletSol = El.Text("— SOL", "home-wallet-amount");
            _walletUsdc = El.Text("— USDC", "home-wallet-amount");
            _walletStatus = El.Text(Strings.T("wallet.mainnet"), "home-wallet-caption");
            var button = El.Btn("", () => OpenApp("wallet"), "home-wallet");
            button.name = "home-wallet";
            button.Add(_walletStatus);
            button.Add(El.Div("home-wallet-values", _walletSol, _walletUsdc));
            return button;
        }

        private void RefreshWallet()
        {
            var state = _wallet?.State;
            var sol = state?.Token("SOL"); var usdc = state?.Token("USDC");
            _walletSol.text = (sol?.Amount >= 10000m ? LobbyAmounts.Compact(sol.Amount.Value) : sol?.FormatAmount(4) ?? "—") + " SOL";
            _walletUsdc.text = (usdc?.Amount >= 10000m ? LobbyAmounts.Compact(usdc.Amount.Value) : usdc?.FormatAmount(2) ?? "—") + " USDC";
            bool stale = sol?.IsStale == true || usdc?.IsStale == true;
            bool failed = sol?.State == WalletTokenState.Unavailable || usdc?.State == WalletTokenState.Unavailable;
            bool partial = failed && (sol?.State == WalletTokenState.Ready || usdc?.State == WalletTokenState.Ready);
            _walletStatus.text = Strings.T(state == null || state.Status == WalletBalanceStatus.Disconnected ? "wallet.disconnected" : stale ? "wallet.stale" : state.IsRefreshing ? "wallet.refreshing" : partial ? "wallet.partial" : failed ? "wallet.unavailable" : "wallet.mainnet");
        }

        private void Explain(StatChip chip)
        {
            var stat = _state?.Stats.Find(s => s.id == chip.Id);
            if (stat == null) return;
            _popover.Show(chip.worldBound, Strings.T(stat.explainTitleKey), stat.explainBody,
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
            }
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
                _clueText.text = text.Length > 54 ? text.Substring(0, 52).TrimEnd() + "…" : text;
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

            var alias = _state.Alias ?? profile?.Alias ?? Strings.T("phone.greetingDefault");
            _greeting.text = alias;
            _eyebrow.text = string.IsNullOrEmpty(_state.TribeName) ? Strings.T("lobby.noTribe").ToUpperInvariant() : _state.TribeName.ToUpperInvariant();
            var badge = OnboardingArt.TribeBadge(_state.TribeId);
            _tribeMark.style.backgroundImage = badge != null ? new StyleBackground(badge) : new StyleBackground(StyleKeyword.None);
            _tribeMark.style.display = badge != null ? DisplayStyle.Flex : DisplayStyle.None;

            RefreshChips();
            _dreamCard.Title = _state.DreamName ?? Strings.T("lobby.noDream");
            bool hasProgress = _state.Dream != null;
            _dreamProgress.text = hasProgress ? _state.Dream.Text() : "";
            _dreamProgress.style.display = hasProgress ? DisplayStyle.Flex : DisplayStyle.None;
            _dreamBar.style.display = hasProgress ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasProgress) _dreamFill.style.width = Length.Percent(_state.Dream.Fraction * 100f);
            _homeCard.Title = _state.HomeName ?? Strings.T("lobby.noHome");
            _dreamCard.SetEnabled(_openApp != null && !_busy);
            _homeCard.SetEnabled(_openApp != null && !_busy);

            if (_mood.HasValue)
            {
                _marketValue.text = Money.Bps(_mood.Value);
                _marketChip.EnableInClassList("home-today--up", _mood.Value > 0);
                _marketChip.EnableInClassList("home-today--down", _mood.Value < 0);
            }
            _enter.SetBusy(_busy, Strings.T("lobby.enterBusy"));
            if (!_busy) _enter.SetEnabled(profile?.HasCharacter == true && _enterCity != null);
            _enter.EnableInClassList("home-enter--first", _firstArrival);
            RefreshOffline();
        }

        private void RefreshChips()
        {
            // Rebuilt only when the set of chips changes (debt appears or clears); values update in place.
            var existing = _root.Query<StatChip>().ToList();
            existing.Sort((a, b) => _state.Stats.FindIndex(s => s.id == a.Id).CompareTo(_state.Stats.FindIndex(s => s.id == b.Id)));
            bool same = existing.Count == Math.Max(1, _state.Stats.Count);
            for (int i = 0; same && i < _state.Stats.Count; i++) same = existing[i].Id == _state.Stats[i].id;
            if (!same)
            {
                _chips.Clear();
                _needs.Clear();
                if (_state.Stats.Count == 0)
                {
                    var placeholder = new StatChip("cash", "cash", "gold", Strings.T("home.cash"), null);
                    placeholder.SetEnabled(false);
                    _chips.Add(placeholder);
                }
                foreach (var stat in _state.Stats)
                {
                    var chip = new StatChip(stat.id, stat.icon, stat.badgeTone, Strings.T(stat.captionKey), Explain, stat.fill >= 0f);
                    chip.name = "home-chip-" + stat.id;
                    (stat.id == "energy" || stat.id == "sanity" ? _needs : _chips).Add(chip);
                }
                existing = _root.Query<StatChip>().ToList();
                existing.Sort((a, b) => _state.Stats.FindIndex(s => s.id == a.Id).CompareTo(_state.Stats.FindIndex(s => s.id == b.Id)));
            }
            for (int i = 0; i < _state.Stats.Count && i < existing.Count; i++)
            {
                var stat = _state.Stats[i];
                var tone = stat.id == "debt" ? StatChip.Tone.Debt
                    : stat.level == StatLevel.Critical ? StatChip.Tone.Bad : stat.level == StatLevel.Low ? StatChip.Tone.Warn : StatChip.Tone.Normal;
                string value = stat.value;
                if (Services.TryGet<PlayerProfile>(out var profile) && (stat.id == "cash" || stat.id == "debt"))
                {
                    decimal amount = (stat.id == "cash" ? profile.CashCents : profile.DebtCents) / 100m;
                    if (Math.Abs(amount) >= 10000m) value = "$" + LobbyAmounts.Compact(amount);
                }
                existing[i].Set(value, tone, stat.fill);
                existing[i].SetEnabled(!_busy);
            }
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
