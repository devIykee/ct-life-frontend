using System;
using System.Linq;
using System.Threading;
using CTLife.Core;
using CTLife.Net;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// In-world HUD: Cash, the five needs (feature #8), players online and Market Mood from the city room,
    /// the venue name, and the action bar (chat, emotes, mic, map, phone).
    /// </summary>
    public sealed class Hud
    {
        private readonly VisualElement _root;
        private readonly Label _cash, _online, _mood, _venue;
        private readonly VisualElement _needs;
        private readonly Button _needsToggle;
        private readonly Label _needsSummary;
        private readonly Label _plateName, _modalCash;
        private readonly VisualElement _modalCashPill;
        private bool _needsExpanded, _micPending, _disposed;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly Button _mic;
        private readonly Label _micLabel;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly ChatBar _chat;
        private readonly EmotePicker _emotes;
        private readonly MovementControls _movement;
        private readonly VehicleControls _vehicle;
        private VisualElement _droneAction, _lockBadge, _jump;
        private readonly HeatPlate _heat = new HeatPlate();
        private LadderChip _ladder;
        private VisualElement _recoveryVeil;
        private IVisualElementScheduledItem _recoveryFade;
        private bool _shown = true;
        private readonly InteractionPrompt _interact;
        private readonly WaypointGuide _guide = new WaypointGuide();
        private readonly Func<IVenueMic> _venueMic;
        private readonly Map.Minimap _minimap;
        private readonly VisualElement _avatar, _energyFill, _sanityFill;
        private readonly Button _walletPill;
        private readonly Label _walletValue;
        private WalletBalanceService _wallet;
        private string _avatarHead;

        public Action OpenPhone;
        public Action OpenMap;
        public Action OpenHome;
        /// <summary>Opens a phone app by id (the wallet pill); set by the game.</summary>
        public Action<string> OpenApp;
        public Map.Minimap Minimap => _minimap;

        public VisualElement Element => _root;

        /// <summary>The newest city HUD's slot under Cash for live feature chips (null before a HUD exists).</summary>
        public static VisualElement TopSlot { get; private set; }
        public static event Action TopSlotChanged;

        /// <param name="venueMic">The venue microphone; defaults to the registered VoiceService (tests pass a fake).</param>
        public Hud(Func<IVenueMic> venueMic = null)
        {
            _venueMic = venueMic ?? (() => Services.TryGet<VoiceService>(out var service) ? service : null);
            _cash = El.Text("$0.00", "ct-cash");
            _online = El.Text(Strings.T("hud.onlineWaiting"), "ct-pill");
            _mood = El.Text(Strings.T("hud.moodWaiting"), "ct-pill");
            _venue = El.Text("", "ct-venue-name");
            _needs = El.Div("ct-needs", El.Text(Strings.T("hud.needs"), "ct-needs-title"));
            _needs.Show(false);
            _needsSummary = El.Text(Strings.T("hud.needsGood"), "ct-needs-summary");
            _needsToggle = El.Btn("", ToggleNeeds, "ct-needs-toggle");
            _needsToggle.name = "hud-needs";
            _needsToggle.tooltip = Strings.T("hud.needsShow");
            foreach (var n in new[] { "energy", "hunger", "fun", "hygiene", "sanity" })
            {
                var bar = El.Div("ct-need-fill");
                bar.name = n;
                var value = El.Text("—", "ct-need-value");
                value.name = n + "-value";
                _needs.Add(El.Div("ct-need", El.Text(Strings.NeedName(n), "ct-need-label"), El.Div("ct-need-track", bar), value));
            }

            _chat = new ChatBar();
            _emotes = new EmotePicker();
            _movement = new MovementControls();
            _vehicle = new VehicleControls();
            _interact = new InteractionPrompt();
            _mic = HudAction("mic", Strings.T("hud.micOff"), ToggleMic, "microphone");
            _micLabel = _mic.Q<Label>();

            // Top-left: compact player plate (avatar + Energy/Sanity) opening the needs card; Lobby beside it.
            // Design system (2026-10-26, mockup 01a): the plate is a HUD pill, avatar + name over one caption; the needs card
            // opens on tap. The Energy/Sanity meters live in that card now (one fact per pill).
            // v2 (mockup d): an avatar tile, then the name on a chunky glass pill; the needs line shows only when one is low.
            _needsToggle.AddToClassList("hud-plate");
            _needsToggle.AddToClassList("ds-bare");
            _needsToggle.Clear();
            _avatar = El.Div("hud-avatar ds-hud-avatar-art");
            _avatar.pickingMode = PickingMode.Ignore;
            _energyFill = El.Div("hud-meter-fill hud-meter-fill--energy");
            _sanityFill = El.Div("hud-meter-fill hud-meter-fill--sanity");
            _plateName = CTLife.UI.DS.Ds.Text("", "ds-t-hud");
            _plateName.name = "hud-plate-name";
            _needsSummary.AddToClassList("ds-t-hud-caption");
            _needsToggle.Add(El.PassThrough(El.Div("ds-hud-avatar hud-avatar-tile", _avatar)));
            _needsToggle.Add(El.PassThrough(El.Div("ds-hud-pill hud-plate-pill", El.PassThrough(El.Div("ds-hud-pill-col", _plateName, _needsSummary)))));
            var lobby = HudAction("lobby", Strings.T("hud.lobby"), () => OpenUtility(OpenHome), "house-line");
            lobby.AddToClassList("hud-small-action");
            lobby.AddToClassList("hud-fade");
            var plateRow = El.Div("hud-plate-row-top", _needsToggle, lobby);
            plateRow.pickingMode = PickingMode.Ignore;
            // the dream ladder chip under the plate (queue 18): set by City Life from the rented home and Cash
            _ladder = new LadderChip();
            var plate = El.Div("hud-plate-row", plateRow, _ladder.Element);
            plate.pickingMode = PickingMode.Ignore;

            // Top-centre: Cash and wallet pills, small.
            var coin = El.Div("ds-hud-pill-art hud-coin");
            coin.pickingMode = PickingMode.Ignore;
            if (CTLife.UI.DS.Ds.IconTexture(CTLife.UI.DS.Ds.Coin) is Texture2D coinArt) coin.style.backgroundImage = Background.FromTexture2D(coinArt);
            _cash.AddToClassList("ds-t-hud");
            var cash = El.Div("hud-pill hud-pill-cash ds-hud-pill ds-hud-pill--art", coin, _cash);
            cash.name = "hud-cash";
            El.PassThrough(cash);
            // v2: while a card is up the HUD steps back and this mirror of the Cash pill stays above the scrim (top-right),
            // so the collect coin still has a target
            var modalCoin = El.Div("ds-hud-pill-art");
            modalCoin.pickingMode = PickingMode.Ignore;
            if (CTLife.UI.DS.Ds.IconTexture(CTLife.UI.DS.Ds.Coin) is Texture2D coinArt2) modalCoin.style.backgroundImage = Background.FromTexture2D(coinArt2);
            _modalCash = CTLife.UI.DS.Ds.Text("$0.00", "ds-t-hud");
            _modalCashPill = El.Div("ds-hud-pill ds-hud-pill--art", modalCoin, _modalCash);
            _modalCashPill.name = "hud-cash-modal";
            _modalCashPill.pickingMode = PickingMode.Ignore;
            CTLife.UI.DS.DsModalHost.Current?.CashSlot.Add(_modalCashPill);
            _walletValue = El.Text("", "hud-pill-value ds-t-hud");
            _walletPill = new Button(() => OpenUtility(() => OpenApp?.Invoke("wallet"))) { text = "" };
            _walletPill.AddToClassList("hud-pill"); _walletPill.AddToClassList("hud-pill-wallet"); _walletPill.AddToClassList("ds-hud-pill");
            _walletPill.name = "hud-wallet";
            _walletPill.tooltip = Strings.T("lobby.wallet.title");
            _walletPill.Add(El.PassThrough(El.Div("hud-sol-mark")));
            _walletPill.Add(_walletValue);
            _walletPill.style.display = DisplayStyle.None;
            // Feature chips under the currencies (Rekt Burger's ON SHIFT chip): attached by the feature through TopSlot.
            var slot = El.Div("hud-top-slot");
            slot.name = "hud-top-slot";
            slot.pickingMode = PickingMode.Ignore;
            var top = El.Div("ct-hud-top hud-currencies", El.Div("hud-currency-row", cash, _walletPill), _heat.Element, slot);
            top.pickingMode = PickingMode.Ignore;
            TopSlot = slot;
            TopSlotChanged?.Invoke();
            if (CTLife.UI.DS.DsToastHost.Current != null) CTLife.UI.DS.DsToastHost.Current.Anchor = top;

            // Top-right: the live minimap (tap = full map) with the place and presence under it, the Duo tucked beside it.
            _minimap = new Map.Minimap();
            var location = El.Btn("", () => OpenUtility(OpenMap), "ct-hud-location hud-map-button ds-bare");
            location.name = "hud-map";
            location.tooltip = Strings.T("hud.map");
            _minimap.AddToClassList("ds-minimap-frame");
            location.Add(El.PassThrough(El.Div("ds-minimap-card hud-minimap-card", _minimap)));
            // the area name sits on a glass chip (text never sits on the world); the presence chips moved off the HUD
            _venue.AddToClassList("hud-venue-text");
            var presence = El.Div("ct-hud-presence", _online, _mood);
            presence.style.display = DisplayStyle.None;
            var place = El.Div("hud-place hud-fade", El.Div("ds-hud-caption-chip", _venue), presence);
            location.Add(El.PassThrough(place));
            var phone = PhoneAction();
            phone.AddToClassList("hud-small-action");
            phone.AddToClassList("ds-hud-round--square");
            var personal = El.Div("ct-hud-personal hud-corner", phone, location);
            personal.pickingMode = PickingMode.Ignore;

            // Bottom-right: Jump (a glass round in the corner), then the round, icon-led context actions. Bottom-left: the joystick.
            _jump = JumpButton();
            var social = El.Div("ct-hud-social hud-fade",
                HudAction("chat", Strings.T("hud.chat"), () => { CollapseNeeds(); _emotes.Hide(); _chat.Toggle(); }, "chat-circle-dots"),
                HudAction("emotes", Strings.T("hud.emote"), () => { CollapseNeeds(); _chat.Hide(); _emotes.Toggle(); }, "smiley"), _mic);
            // The owned flyer (CT Island only): since vehicles v3 the hovercar comes to the player and they get in. The quadcopter
            // (VehicleSystem.DeployDrone) is no longer on the default HUD.
            // The hovercar is a bought asset (a scarce right-to-drive NFT, real money only): without it the button stays, locked
            // (dimmed, "Buy in Duo Shop"), and a tap says where to get it.
            _droneAction = HudAction("hovercar", Strings.T("hud.drone"), () =>
            {
                var vs = CTLife.World.Vehicles.VehicleSystem.Current;
                if (vs != null && vs.CanDeployDrone) vs.SummonHovercar();
                else OpenShop();   // locked: real money only, sold in the Duo Shop (never a Cash price)
            }, "rocket-launch");
            _lockBadge = El.PassThrough(El.Div("hud-lock-badge", new UiIcon("locked")));
            _droneAction.Add(_lockBadge);
            _droneAction.style.display = DisplayStyle.None;
            social.Insert(0, _droneAction);
            social.pickingMode = PickingMode.Ignore;
            El.PassThrough(_needs);
            // The stick's touch area is the whole lower-left of the screen (a floating stick), so it goes first: every
            // other control sits above it and keeps its taps.
            // speech bubbles keep out of the top HUD rows (WorldLabels)
            WorldLabels.SafeZones.Clear();
            WorldLabels.SafeZones.Add(plate); WorldLabels.SafeZones.Add(top); WorldLabels.SafeZones.Add(personal);
            _root = El.Div("ct-hud hud-arena ds-hud-back", _movement.Element, _vehicle.Element, plate, top, _needs, _emotes.Element, _chat.Element, social, _jump, personal, _guide.Element, _interact.Element);
            _root.pickingMode = PickingMode.Ignore;
            _recoveryVeil = El.Div("hud-recovery-veil");
            _recoveryVeil.name = "hud-recovery-veil";
            _recoveryVeil.pickingMode = PickingMode.Ignore;
            _root.Add(_recoveryVeil);
            _subs.On<PlayerRecoveryFadeSignal>(FadeRecovery);
            OnboardingFrame.AddSheet(_root, "UI/map");
            _interact.SetObstacles(plate, top, _needs, _movement.Footprint, social, _jump, personal, _chat.Element, _emotes.Element);
            if (Services.TryGet<WalletBalanceService>(out var wallet)) { _wallet = wallet; _wallet.Changed += RefreshWallet; RefreshWallet(); }

            _subs.On<ProfileChangedSignal>(_ => RefreshProfile());
            _subs.On<CTLife.UI.DS.DsModalChangedSignal>(s => _root?.EnableInClassList("ds-hud-away", s.open));
            _subs.On<CTLife.World.Vehicles.VehicleChangedSignal>(s =>
            {
                // driving or flying: the vehicle controls replace the walking stick and the social buttons
                bool driving = s.vehicle != null;
                _movement.SetEnabled(_shown && !driving);
                // City HUD v2: the on-foot groups (dock, Jump, Lobby, place chips) fade out while driving and back after.
                HudFade.Set(social, !driving);
                HudFade.Set(_jump, !driving);
                HudFade.Set(lobby, !driving);
                HudFade.Set(place, !driving);
                if (driving) { _chat.Hide(); _emotes.Hide(); }
            });
            _subs.On<OpenDuoAppRequestSignal>(s => { if (s.app == "shop") OpenShop(); else if (!string.IsNullOrEmpty(s.app)) OpenUtility(() => OpenApp?.Invoke(s.app)); });
            _subs.On<OpenMapRequestSignal>(_ => OpenUtility(OpenMap)); // the door interaction
            _subs.On<DegradedChangedSignal>(_ => RefreshDegraded());
            _subs.On<OnlineCountSignal>(s => _online.text = Strings.F("hud.online", s.online));
            // The count only changes on joins/leaves; show the value the city room already has.
            if (Services.TryGet<IRealtimeService>(out var rt) && rt.City != null && rt.City.Online >= 0)
                _online.text = Strings.F("hud.online", rt.City.Online);
            _subs.On<MoodSignal>(s =>
            {
                _mood.text = Strings.F("hud.mood", Money.Bps(s.bps));
                _mood.EnableInClassList("ct-green", s.bps > 0);
                _mood.EnableInClassList("ct-red", s.bps < 0);
            });
            _subs.On<IslandPlaceSignal>(s =>
            {
                // On CT Island's streets the label is the district (a venue's own name arrives with VenueChangedSignal).
                if (s.venueId != null) return;
                var island = VenueSession.Current != null ? VenueSession.Current.Island : null;
                _venue.text = island != null ? island.DistrictName(s.districtId) : "";
            });
            _subs.On<VenueChangedSignal>(s =>
            {
                var reg = Services.Get<ConfigStore>().Registries;
                var visit = VenueSession.Current;
                _venue.text = visit != null && visit.IsOwnHome
                    ? reg.Homes.FirstOrDefault(h => h.id == visit.HomeId)?.name ?? Strings.T("app.home")
                    : s.venueId != null ? reg.Venue(s.venueId)?.name ?? s.venueId : "";
                RefreshDegraded();
                RefreshVehicleAccess();
            });
            CTLife.World.Vehicles.VehicleSystem.AccessChanged += RefreshVehicleAccess;
            RefreshVehicleAccess();
            RefreshProfile();
            RefreshDegraded();
        }

        private Label _phoneBadge;

        private void FadeRecovery(PlayerRecoveryFadeSignal signal)
        {
            _recoveryFade?.Pause();
            if (_disposed || !_shown || signal.seconds <= 0f)
            {
                _recoveryVeil.style.opacity = 0f;
                _recoveryVeil.pickingMode = PickingMode.Ignore;
                return;
            }
            float start = Time.realtimeSinceStartup;
            float from = _recoveryVeil.resolvedStyle.opacity;
            if (float.IsNaN(from)) from = 0f;
            float target = signal.dim ? 1f : 0f;
            _recoveryVeil.pickingMode = PickingMode.Position;
            _recoveryFade = _recoveryVeil.schedule.Execute(() =>
            {
                float t = Mathf.Clamp01((Time.realtimeSinceStartup - start) / signal.seconds);
                _recoveryVeil.style.opacity = Mathf.Lerp(from, target, t);
                if (t < 1f) return;
                _recoveryFade?.Pause();
                if (!signal.dim) _recoveryVeil.pickingMode = PickingMode.Ignore;
            }).Every(16);
        }

        private void RefreshVehicleAccess()
        {
            if (_disposed) return;
            bool island = VenueSession.Current?.IsIsland == true && CTLife.World.Vehicles.VehicleSystem.Current != null;
            bool owned = island && CTLife.World.Vehicles.VehicleSystem.Current.CanDeployDrone;
            _droneAction.style.display = island ? DisplayStyle.Flex : DisplayStyle.None;
            _droneAction.style.opacity = owned ? 1f : 0.6f;           // locked: not owned yet (Duo Shop)
            bool wasLocked = _droneAction.ClassListContains("hud-locked");
            _droneAction.EnableInClassList("hud-locked", island && !owned);
            if (wasLocked && owned)
            {
                // Bought: the lock pops off (1 -> 1.3 -> 0) and the button is the summon again, no restart.
                _lockBadge.AddToClassList("hud-lock-pop");
                _lockBadge.schedule.Execute(() => _lockBadge.RemoveFromClassList("hud-lock-pop")).StartingIn(200);
            }
            // locked: the label says where it is sold (an NFT right-to-drive, real money only; Cash never unlocks it)
            if (_droneAction.Q<Label>(className: "ct-dock-label") is Label label)
            {
                label.text = owned ? Strings.T("hud.drone") : Strings.T("hud.hovercarLocked");
                label.style.whiteSpace = owned ? StyleKeyword.Null : WhiteSpace.Normal;     // "Buy in / Duo Shop" on two lines
                label.style.unityTextAlign = TextAnchor.UpperCenter;
            }
        }

        private void OpenShop() => OpenUtility(() =>
        {
            if (OpenApp != null) OpenApp("shop");
            else Bus.Publish(ToastSignal.Text(Strings.T("hud.hovercarLockedToast")));
        });

        /// <summary>Jump: a glass round in the bottom-right corner; a small hop on the local player.</summary>
        private VisualElement JumpButton()
        {
            var b = El.Div("hud-jump hud-fade ds-hud-round ds-hud-round--big", CTLife.UI.DS.Ds.Icon("arrow-fat-up", 64, "white"));
            b.name = "hud-jump";
            b.tooltip = Strings.T("hud.jump");
            b.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                b.AddToClassList("veh-down");
                VenueSession.Current?.LocalPlayer?.RequestJump();
                e.StopPropagation();
            });
            b.RegisterCallback<PointerUpEvent>(_ => b.RemoveFromClassList("veh-down"));
            b.RegisterCallback<PointerLeaveEvent>(_ => b.RemoveFromClassList("veh-down"));
            b.RegisterCallback<PointerCancelEvent>(_ => b.RemoveFromClassList("veh-down"));
            return b;
        }

        /// <summary>The phone action carries the Duo's total unread count (red badge, cleared when read).</summary>
        private Button PhoneAction()
        {
            var button = HudAction("phone", Strings.T("lobby.duo"), () => OpenUtility(OpenPhone), "device-mobile", primary: true);
            CTLife.UI.DS.Ds.SetBadge(button, DuoNotificationCenter.TotalBadge);
            _subs.On<DuoBadgesChangedSignal>(s => CTLife.UI.DS.Ds.SetBadge(button, s.total));
            return button;
        }

        /// <summary>A HUD round (design system 3.11): glass, one Phosphor icon, no text label (the name is the tooltip; the
        /// hidden label keeps the accessible name).</summary>
        private static Button HudAction(string id, string label, Action action, string icon = null, bool primary = false)
        {
            var button = CTLife.UI.DS.Ds.HudRound(icon ?? id, action, name: "hud-" + id);
            button.AddToClassList("hud-round");   // layout only (the old ct-dock-action look is global in ctlife.uss)
            button.tooltip = label;
            var hidden = El.Text(label, "ct-dock-label");
            hidden.style.display = DisplayStyle.None;
            button.Add(hidden);
            return button;
        }

        private static VisualElement Meter(string icon, VisualElement fill)
        {
            var m = El.Div("hud-meter", LobbyArt.Icon(icon, "hud-meter-icon"), El.Div("hud-meter-track", fill));
            m.pickingMode = PickingMode.Ignore;
            return m;
        }

        private void RefreshWallet()
        {
            var state = _wallet?.State;
            var sol = state?.Token("SOL");
            bool show = state != null && state.Status != WalletBalanceStatus.Disconnected && sol != null && OpenApp != null;
            _walletPill.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            bool stale = sol.IsStale || sol.State == WalletTokenState.Unavailable;
            _walletValue.text = (sol.Amount >= 10000m ? LobbyAmounts.Compact(sol.Amount.Value) : sol.Amount != null ? sol.FormatAmount(3) : "\u2014") + " SOL";
            _walletPill.EnableInClassList("hud-pill--dim", stale);
            _walletPill.tooltip = stale ? Strings.F("wallet.stale", Services.TryGet<WalletBalanceService>(out var wb) ? wb.ClusterLabel : "MAINNET") : Strings.T("lobby.wallet.title");
        }

        private void RefreshAvatar(PlayerProfile p)
        {
            string head = p?.LookArray() is string[] look && look.Length > 1 ? look[1] : null;
            if (head == _avatarHead) return;
            _avatarHead = head;
            var portrait = string.IsNullOrEmpty(head) ? null : Resources.Load<Texture2D>("UI/Customizer/Portraits/" + head);
            _avatar.Clear();
            _avatar.EnableInClassList("ds-hud-pill-art--letter", portrait == null);
            if (portrait != null) _avatar.style.backgroundImage = new StyleBackground(portrait);
            else
            {
                _avatar.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                var alias = (p?.Alias ?? "?").Trim();
                _avatar.Add(El.Text(System.Globalization.StringInfo.GetNextTextElement(alias.Length > 0 ? alias : "?").ToUpperInvariant(), "hud-avatar-letter"));
            }
        }

        public void Show(bool on)
        {
            _root.Show(on);
            _shown = on;
            _movement.SetEnabled(on && !_vehicle.Active);
            if (!on)
            {
                FadeRecovery(new PlayerRecoveryFadeSignal());
                _chat.Hide(); _emotes.Hide();
                if (_needsExpanded) ToggleNeeds();
                TurnMicOff();
            }
        }

        /// <summary>
        /// The HUD hides only for the Lobby (the phone hides its layer instead), and the Lobby has no mic control. A live venue
        /// mic is turned off there rather than left recording out of sight (Seeker: still recording 8+ s on the Lobby).
        /// No owner token: turning a mic off can never act wrongly for a successor visit.
        /// </summary>
        private async void TurnMicOff()
        {
            var mic = _venueMic();
            if (_disposed || mic == null || !mic.MicOn) return;
            try { await mic.SetMic(false, CancellationToken.None, _life.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Log.Warn("voice", "Microphone off failed: " + error.GetType().Name); }
            finally { if (!_disposed) RefreshDegraded(); }
        }

        private void RefreshDegraded()
        {
            bool voice = !RuntimeMode.IsOfflineDemo && Features.IsAvailable(Features.Voice);
            _mic.SetEnabled(voice && !(VenueSession.Current?.IsOwnHome ?? false) && !_micPending && !_disposed);
            bool on = voice && _venueMic()?.MicOn == true;
            _micLabel.text = Strings.T(!voice ? "hud.voiceUnavailable" : on ? "hud.micOn" : "hud.micOff");
            _mic.EnableInClassList("ct-btn-live", on);
            _mic.EnableInClassList("ds-hud-round--live", on);
            _mic.tooltip = _micLabel.text;
        }

        private void RefreshProfile()
        {
            if (!Services.TryGet<PlayerProfile>(out var p) || p.Me == null) return;
            decimal cash = p.CashCents / 100m;
            _cash.text = Math.Abs(cash) >= 10000m ? "$" + LobbyAmounts.Compact(cash) : Money.Cash(p.CashCents);
            _cash.tooltip = Money.Cash(p.CashCents);
            RefreshAvatar(p);
            CTLife.UI.DS.Ds.SetText(_plateName, p.Alias);
            _modalCash.text = _cash.text;
            if (_wallet != null) RefreshWallet();
            var n = p.Me.Needs;
            if (n == null) return;
            _energyFill.style.width = Length.Percent(Mathf.Clamp(n.Energy, 0, 100));
            _sanityFill.style.width = Length.Percent(Mathf.Clamp(n.Sanity, 0, 100));
            _energyFill.EnableInClassList("hud-meter-fill--low", n.Energy < 25);
            _sanityFill.EnableInClassList("hud-meter-fill--low", n.Sanity < 25);
            SetNeed("energy", n.Energy);
            SetNeed("hunger", n.Hunger);
            SetNeed("fun", n.Fun);
            SetNeed("hygiene", n.Hygiene);
            SetNeed("sanity", n.Sanity);
            var lowest = new[] { ("energy", n.Energy), ("hunger", n.Hunger), ("fun", n.Fun), ("hygiene", n.Hygiene), ("sanity", n.Sanity) }
                .OrderBy(need => need.Item2).First();
            bool low = lowest.Item2 < 25;
            _needsSummary.text = low ? Strings.F("hud.needsLow", Strings.NeedName(lowest.Item1), Mathf.Clamp(lowest.Item2, 0, 100)) : Strings.T("hud.needsGood");
            _needsToggle.EnableInClassList("ct-needs-alert", low);
            _needsSummary.style.display = low ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ToggleNeeds()
        {
            if (!_needsExpanded) { _chat.Hide(); _emotes.Hide(); }
            _needsExpanded = !_needsExpanded;
            _needs.Show(_needsExpanded);
            _needsToggle.tooltip = Strings.T(_needsExpanded ? "hud.needsHide" : "hud.needsShow");
            _needsToggle.EnableInClassList("ct-needs-open", _needsExpanded);
        }

        private void CollapseNeeds() { if (_needsExpanded) ToggleNeeds(); }

        private void OpenUtility(Action open)
        {
            CollapseNeeds(); _chat.Hide(); _emotes.Hide();
            open?.Invoke();
        }

        private void SetNeed(string id, int value)
        {
            var fill = _needs.Q(id);
            if (fill == null) return;
            fill.style.width = Length.Percent(Mathf.Clamp(value, 0, 100));
            fill.EnableInClassList("ct-need-low", value < 25);
            var valueLabel = _needs.Q<Label>(id + "-value");
            if (valueLabel != null)
            {
                valueLabel.text = Mathf.Clamp(value, 0, 100).ToString();
                valueLabel.EnableInClassList("ct-red", value < 25);
            }
        }

        private async void ToggleMic()
        {
            var visit = VenueSession.Current;
            if (_disposed || _micPending || visit == null || !visit.IsActive || !Services.TryGet<VoiceService>(out var voice)) return;
            if (!Features.IsAvailable(Features.Voice))
            {
                Bus.Publish(ToastSignal.Text(Strings.T("hud.voiceError"), ToastKind.Bad));
                return;
            }
            bool desired = !voice.MicOn; // Capture the explicit gesture before any permission/connection await.
            bool OwnsVisit() => !_disposed && visit != null && visit.IsActive && ReferenceEquals(VenueSession.Current, visit)
                && Services.TryGet<VoiceService>(out var current) && ReferenceEquals(current, voice);
            using var gesture = CancellationTokenSource.CreateLinkedTokenSource(_life.Token, visit.LifetimeToken);
            _micPending = true; RefreshDegraded();
            try
            {
                if (!voice.IsConnected && !await visit.ConnectVoice())
                {
                    if (OwnsVisit()) Bus.Publish(ToastSignal.Text(Strings.T("hud.voiceHere"), ToastKind.Bad));
                    return;
                }
                if (!OwnsVisit() || !Features.IsAvailable(Features.Voice)) return;
                bool ok = await voice.SetMic(desired, visit.LifetimeToken, gesture.Token);
                if (!OwnsVisit()) return;
                if (!ok) Bus.Publish(ToastSignal.Text(Strings.T("hud.voiceHere"), ToastKind.Bad));
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                Log.Warn("voice", "Microphone action failed: " + error.GetType().Name);
                if (OwnsVisit()) Bus.Publish(ToastSignal.Text(Strings.T("hud.voiceError"), ToastKind.Bad));
            }
            finally
            {
                _micPending = false;
                if (!_disposed) RefreshDegraded();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            FadeRecovery(new PlayerRecoveryFadeSignal());
            CTLife.World.Vehicles.VehicleSystem.AccessChanged -= RefreshVehicleAccess;
            _life.Cancel(); _life.Dispose();
            _movement.Dispose();
            _vehicle.Dispose();
            _chat.Dispose();
            _interact.Dispose();
            _guide.Dispose();
            _subs.Dispose();
            if (_wallet != null) _wallet.Changed -= RefreshWallet;
        }
    }

    /// <summary>The room the chat bar speaks in: the current venue visit in the game, a fake in EditMode tests.</summary>
    public interface IChatRoom
    {
        /// <summary>Our session in the room; a `said` carrying it is our own line coming back.</summary>
        string SessionId { get; }
        /// <summary>Sends one line: 0 when sent, else the seconds to wait.</summary>
        float Say(string text);
        /// <summary>The local avatar's head, where our own speech bubble sits; null without an avatar.</summary>
        Transform OwnBubbleAnchor { get; }
    }

    /// <summary>"Say something out loud" (feature #6): 140 chars, once per 2 s, bubble over your avatar.</summary>
    public sealed class ChatBar
    {
        public VisualElement Element { get; }
        private readonly TextField _field;
        private readonly Func<IChatRoom> _room;

        /// <param name="room">The room to speak in; defaults to the current venue visit (tests pass a fake).</param>
        public ChatBar(Func<IChatRoom> room = null)
        {
            _room = room ?? VenueChatRoom.Current;
            _field = new TextField { maxLength = 140 };
            _field.AddToClassList("ct-chat-field");
            // Device QA: the keyboard covered the chat input. Keep the OS input line visible above the keyboard,
            // and dock the panel to the top of the screen while the keyboard is up.
            _field.textEdition.hideMobileInput = false;
            _field.tooltip = Strings.T("hud.chatPrompt");
            _field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) { Hide(); e.StopImmediatePropagation(); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                { Send(); e.StopImmediatePropagation(); }
            }, TrickleDown.TrickleDown);
            _history = El.Div("ct-chat-history");
            var say = El.Btn(Strings.T("hud.say"), Send, "ct-btn ct-btn-primary");
            // Pressing "Say it" must not take focus from the field. Unity 6000.3 (TouchScreenTextEditor.OnFocusOutEvent) only
            // partially dismisses the soft keyboard when focus moves to a TextElement with the field's keyboard settings, and
            // a Button is one, so nothing closed it after the send (Seeker). Send blurs the field instead, which closes it.
            say.RegisterCallback<PointerDownEvent>(e => say.focusController?.IgnoreEvent(e), TrickleDown.TrickleDown);
            // Input first: while the keyboard is up the panel docks at the top and its lower part is covered.
            Element = El.Div("ct-chat", El.Div("ct-row", _field, say),
                El.Text(Strings.T("hud.chatPrompt"), "ct-small ct-muted"), _history);
            Element.Show(false);
            // The Android keyboard's Done/Enter does not reach UI Toolkit as a KeyDown; send when it reports Done.
            _field.RegisterCallback<NavigationSubmitEvent>(_ => Send(), TrickleDown.TrickleDown);
            Element.schedule.Execute(() =>
            {
                bool open = Element.resolvedStyle.display != DisplayStyle.None;
                Element.EnableInClassList("ct-chat-keyboard", open && TouchScreenKeyboard.visible);
                var kb = _field.textEdition.touchScreenKeyboard;
                if (open && kb != null && kb.status == TouchScreenKeyboard.Status.Done && !_doneHandled) { _doneHandled = true; Send(); }
                if (kb == null || kb.status == TouchScreenKeyboard.Status.Visible) _doneHandled = false;
            }).Every(150);
            _subs.On<CTLife.Realtime.SaidMessage>(OnSaid);
        }

        private readonly VisualElement _history;
        private bool _doneHandled;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly System.Collections.Generic.List<string> _lines = new System.Collections.Generic.List<string>();
        public const int HistoryLines = 4;

        private void OnSaid(CTLife.Realtime.SaidMessage m)
        {
            var room = _room();
            // Our own line was shown when it was sent; a server that echoes it to the sender must not show it twice.
            if (room != null && !string.IsNullOrEmpty(room.SessionId) && m.sessionId == room.SessionId) return;
            var visit = VenueSession.Current;
            string who = "Someone";
            if (visit != null && visit.Players.TryGetValue(m.sessionId ?? "", out var rp) && rp != null)
                who = rp.GetComponent<AvatarView>()?.displayName ?? who;
            else who = CTLife.World.StreetNetworkBridge.Current?.NameOf(m.sessionId) ?? who;
            AddLine(who + ": " + m.text);
        }

        public void AddLine(string line)
        {
            _lines.Add(line);
            while (_lines.Count > HistoryLines) _lines.RemoveAt(0);
            _history.Clear();
            foreach (var l in _lines) _history.Add(El.Text(l, "ct-small ct-chat-line"));
        }

        public void Dispose() => _subs.Dispose();

        public void Toggle()
        {
            if (Element.resolvedStyle.display != DisplayStyle.None) { Hide(); return; }
            Element.Show(true);
            _field.Focus();
        }

        /// <summary>Hides the bar and releases the field, so the soft keyboard closes with it.</summary>
        public void Hide()
        {
            _field.Blur();
            Element.Show(false);
        }

        private void Send()
        {
            var text = _field.value?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            var room = _room();
            if (room == null) { Bus.Publish(ToastSignal.Text(Strings.T("hud.chatOffline"), ToastKind.Warning)); return; }
            float wait = room.Say(text);
            if (wait > 0f)
            {
                Bus.Publish(ToastSignal.Text(Strings.F("hud.wait", wait), ToastKind.Bad));
                return;
            }
            _field.value = "";
            AddLine(Strings.T("hud.you") + ": " + text);
            // The room's `said` goes to everyone except the sender (backend venue.ts `except: client`): our bubble is ours to show.
            var head = room.OwnBubbleAnchor;
            if (head != null) Bus.Publish(new BubbleSignal { anchor = head, text = text, isNpc = false, seconds = 4.5f });
            Hide();
        }
    }

    /// <summary>The current VenueSession's joined room as the chat bar's room; null without one.</summary>
    internal sealed class VenueChatRoom : IChatRoom
    {
        private readonly VenueSession _visit;
        private readonly VenueChannel _channel;

        private VenueChatRoom(VenueSession visit, VenueChannel channel) { _visit = visit; _channel = channel; }

        public static IChatRoom Current()
        {
            var visit = VenueSession.Current;
            var channel = visit != null ? visit.Channel : null;
            if (channel != null && channel.IsJoined) return new VenueChatRoom(visit, channel);
            // On the streets (and in rooms without a venue channel) the district's street room carries the chat.
            var street = CTLife.World.StreetNetworkBridge.Current?.Channel;
            if (street != null && street.IsJoined) return new StreetChatRoom(visit, street);
            return channel != null ? new VenueChatRoom(visit, channel) : null;
        }

        public string SessionId => _channel.SessionId;
        public float Say(string text) => _channel.Say(text);
        public Transform OwnBubbleAnchor
        {
            get
            {
                var local = _visit != null ? _visit.LocalPlayer : null;
                var view = local != null ? local.GetComponent<AvatarView>() : null;
                return view != null ? view.HeadAnchor : null;
            }
        }
    }

    /// <summary>The district's street room as the chat bar's room (open-world streets).</summary>
    internal sealed class StreetChatRoom : IChatRoom
    {
        private readonly VenueSession _visit;
        private readonly CTLife.Net.StreetChannel _channel;

        public StreetChatRoom(VenueSession visit, CTLife.Net.StreetChannel channel) { _visit = visit; _channel = channel; }

        public string SessionId => _channel.SessionId;
        public float Say(string text) => _channel.Say(text);
        public Transform OwnBubbleAnchor
        {
            get
            {
                var local = _visit != null ? _visit.LocalPlayer : null;
                var view = local != null ? local.GetComponent<AvatarView>() : null;
                return view != null ? view.HeadAnchor : null;
            }
        }
    }

    /// <summary>Emote grid from anims.json (kind "emote"); the server accepts one per second.</summary>
    public sealed class EmotePicker
    {
        public VisualElement Element { get; }

        public EmotePicker()
        {
            Element = El.Div("ct-emotes");
            Element.Show(false);
        }

        public void Toggle()
        {
            bool on = Element.resolvedStyle.display == DisplayStyle.None;
            if (on && Element.childCount == 0) Populate();
            Element.Show(on);
        }

        public void Hide() => Element.Show(false);

        private void Populate()
        {
            Element.Add(El.Text(Strings.T("hud.emotesTitle"), "ct-section-title"));
            var grid = El.Div("ct-chips");
            var scroll = El.VerticalScroll("ct-emote-scroll");
            scroll.Add(grid);
            Element.Add(scroll);
            var reg = Services.Get<ConfigStore>().Registries;
            foreach (var a in reg.Anims.Values.Where(a => a.kind == AnimKind.Emote))
            {
                var id = a.id;
                var label = Strings.T("emote." + id);
                if (label == "emote." + id) label = Strings.ReadableId(id.Replace("emote_", ""));
                grid.Add(El.Btn(label, () =>
                {
                    var s = VenueSession.Current;
                    if (RuntimeMode.IsOfflineIsland)
                    {
                        s?.LocalPlayer?.GetComponent<AvatarView>()?.PlayOneShot(id);
                        Element.Show(false);
                        return;
                    }
                    if (s?.Channel == null) return;
                    if (s.Channel.Emote(id)) s.LocalPlayer?.GetComponent<AvatarView>()?.PlayOneShot(id);
                    Element.Show(false);
                }, "ct-chip", "sfx_ui_tap"));
            }
            if (grid.childCount == 0) grid.Add(El.Text(Strings.T("hud.emotesEmpty"), "ct-small ct-muted"));
        }
    }
}
