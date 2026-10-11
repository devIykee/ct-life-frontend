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
        private bool _needsExpanded, _micPending, _disposed;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly Button _mic;
        private readonly Label _micLabel;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly ChatBar _chat;
        private readonly EmotePicker _emotes;
        private readonly MovementControls _movement;
        private readonly InteractionPrompt _interact;
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
            _interact = new InteractionPrompt();
            _mic = HudAction("mic", Strings.T("hud.micOff"), ToggleMic, "microphone");
            _micLabel = _mic.Q<Label>();

            // Top-left: compact player plate (avatar + Energy/Sanity) opening the needs card; Lobby beside it.
            _needsToggle.AddToClassList("hud-plate");
            _needsToggle.Clear();
            _avatar = El.Div("hud-avatar");
            _avatar.pickingMode = PickingMode.Ignore;
            _energyFill = El.Div("hud-meter-fill hud-meter-fill--energy");
            _sanityFill = El.Div("hud-meter-fill hud-meter-fill--sanity");
            var meters = El.Div("hud-meters", Meter("lightning-bolt", _energyFill), Meter("brain", _sanityFill));
            _needsSummary.AddToClassList("hud-needs-summary");
            _needsToggle.Add(_avatar);
            _needsToggle.Add(El.PassThrough(El.Div("hud-plate-copy", meters, _needsSummary)));
            var lobby = HudAction("lobby", Strings.T("hud.lobby"), () => OpenUtility(OpenHome), "home-page");
            lobby.AddToClassList("hud-small-action");
            var plate = El.Div("hud-plate-row", _needsToggle, lobby);
            plate.pickingMode = PickingMode.Ignore;

            // Top-centre: Cash and wallet pills, small.
            var cash = El.Div("hud-pill hud-pill-cash", LobbyArt.Icon("coins", "hud-pill-icon"), _cash);
            cash.name = "hud-cash";
            El.PassThrough(cash);
            _walletValue = El.Text("", "hud-pill-value");
            _walletPill = El.Btn("", () => OpenUtility(() => OpenApp?.Invoke("wallet")), "hud-pill hud-pill-wallet");
            _walletPill.name = "hud-wallet";
            _walletPill.tooltip = Strings.T("lobby.wallet.title");
            _walletPill.Add(El.PassThrough(El.Div("hud-sol-mark")));
            _walletPill.Add(_walletValue);
            _walletPill.style.display = DisplayStyle.None;
            var top = El.Div("ct-hud-top hud-currencies", cash, _walletPill);
            top.pickingMode = PickingMode.Ignore;

            // Top-right: the live minimap (tap = full map) with the place and presence under it, the Duo tucked beside it.
            _minimap = new Map.Minimap();
            var location = El.Btn("", () => OpenUtility(OpenMap), "ct-hud-location hud-map-button");
            location.name = "hud-map";
            location.tooltip = Strings.T("hud.map");
            location.Add(_minimap);
            var place = El.Div("hud-place", _venue, El.Div("ct-hud-presence", _online, _mood));
            location.Add(El.PassThrough(place));
            var phone = PhoneAction();
            phone.AddToClassList("hud-small-action");
            var personal = El.Div("ct-hud-personal hud-corner", phone, location);
            personal.pickingMode = PickingMode.Ignore;

            // Bottom-right: round, icon-led context actions. Bottom-left: the joystick (MovementControls).
            var social = El.Div("ct-hud-social",
                HudAction("chat", Strings.T("hud.chat"), () => { CollapseNeeds(); _emotes.Hide(); _chat.Toggle(); }, "speech-bubble"),
                HudAction("emotes", Strings.T("hud.emote"), () => { CollapseNeeds(); _chat.Hide(); _emotes.Toggle(); }, "smiling"), _mic);
            social.pickingMode = PickingMode.Ignore;
            El.PassThrough(_needs);
            // The stick's touch area is the whole lower-left of the screen (a floating stick), so it goes first: every
            // other control sits above it and keeps its taps.
            _root = El.Div("ct-hud hud-arena", _movement.Element, plate, top, _needs, _emotes.Element, _chat.Element, social, personal, _interact.Element);
            _root.pickingMode = PickingMode.Ignore;
            OnboardingFrame.AddSheet(_root, "UI/map");
            _interact.SetObstacles(plate, top, _needs, _movement.Footprint, social, personal, _chat.Element, _emotes.Element);
            if (Services.TryGet<WalletBalanceService>(out var wallet)) { _wallet = wallet; _wallet.Changed += RefreshWallet; RefreshWallet(); }

            _subs.On<ProfileChangedSignal>(_ => RefreshProfile());
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
            });
            RefreshProfile();
            RefreshDegraded();
        }

        private Label _phoneBadge;

        /// <summary>The phone action carries the Duo's total unread count (red badge, cleared when read).</summary>
        private Button PhoneAction()
        {
            var button = HudAction("phone", Strings.T("lobby.duo"), () => OpenUtility(OpenPhone), "smartphone", primary: true); // the device is the Solana Duo (as in the lobby)
            _phoneBadge = Duo.Badge(DuoNotificationCenter.TotalBadge, "ct-hud-badge");
            _phoneBadge.name = "hud-phone-badge";
            button.Add(_phoneBadge);
            _subs.On<DuoBadgesChangedSignal>(s => Duo.SetBadge(_phoneBadge, s.total));
            return button;
        }

        private static Button HudAction(string id, string label, Action action, string icon = null, bool primary = false)
        {
            var button = El.Btn("", action, primary ? "ct-dock-action ct-dock-primary" : "ct-dock-action");
            button.name = "hud-" + id;
            button.tooltip = label;
            button.Add(El.PassThrough(El.Div("ct-hud-keycap", LobbyArt.Icon(icon ?? id, "hud-icon"))));
            button.Add(El.PassThrough(El.Text(label, "ct-dock-label")));
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
            _walletPill.tooltip = stale ? Strings.T("wallet.stale") : Strings.T("lobby.wallet.title");
        }

        private void RefreshAvatar(PlayerProfile p)
        {
            string head = p?.LookArray() is string[] look && look.Length > 1 ? look[1] : null;
            if (head == _avatarHead) return;
            _avatarHead = head;
            var portrait = string.IsNullOrEmpty(head) ? null : Resources.Load<Texture2D>("UI/Customizer/Portraits/" + head);
            _avatar.Clear();
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
            _movement.SetEnabled(on);
            if (!on)
            {
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
            bool voice = Features.IsAvailable(Features.Voice);
            _mic.SetEnabled(voice && !(VenueSession.Current?.IsOwnHome ?? false) && !_micPending && !_disposed);
            bool on = voice && _venueMic()?.MicOn == true;
            _micLabel.text = Strings.T(!voice ? "hud.voiceUnavailable" : on ? "hud.micOn" : "hud.micOff");
            _mic.EnableInClassList("ct-btn-live", on);
        }

        private void RefreshProfile()
        {
            if (!Services.TryGet<PlayerProfile>(out var p) || p.Me == null) return;
            decimal cash = p.CashCents / 100m;
            _cash.text = Math.Abs(cash) >= 10000m ? "$" + LobbyAmounts.Compact(cash) : Money.Cash(p.CashCents);
            _cash.tooltip = Money.Cash(p.CashCents);
            RefreshAvatar(p);
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
            _life.Cancel(); _life.Dispose();
            _movement.Dispose();
            _chat.Dispose();
            _interact.Dispose();
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
            if (room == null) return;
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
                    if (s?.Channel == null) return;
                    if (s.Channel.Emote(id)) s.LocalPlayer?.GetComponent<AvatarView>()?.PlayOneShot(id);
                    Element.Show(false);
                }, "ct-chip", "sfx_ui_tap"));
            }
            if (grid.childCount == 0) grid.Add(El.Text(Strings.T("hud.emotesEmpty"), "ct-small ct-muted"));
        }
    }
}
