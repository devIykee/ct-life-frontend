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

        public Action OpenPhone;
        public Action OpenMap;
        public Action OpenHome;

        public VisualElement Element => _root;

        public Hud()
        {
            _cash = El.Text("$0.00", "ct-cash");
            _online = El.Text(Strings.T("hud.onlineWaiting"), "ct-pill");
            _mood = El.Text(Strings.T("hud.moodWaiting"), "ct-pill");
            _venue = El.Text("", "ct-venue-name");
            _needs = El.Div("ct-needs", El.Text(Strings.T("hud.needs"), "ct-needs-title"));
            _needs.Show(false);
            _needsSummary = El.Text(Strings.T("hud.needsGood"), "ct-needs-summary");
            _needsToggle = El.Btn("", ToggleNeeds, "ct-needs-toggle");
            _needsToggle.tooltip = Strings.T("hud.needsShow");
            _needsToggle.Add(new UiIcon("needs"));
            _needsToggle.Add(El.Div("ct-col", El.Text(Strings.T("hud.needsToggle"), "ct-needs-toggle-title"), _needsSummary));
            foreach (var n in new[] { "energy", "hunger", "fun", "hygiene", "sanity" })
            {
                var bar = El.Div("ct-need-fill");
                bar.name = n;
                var value = El.Text("-", "ct-need-value");
                value.name = n + "-value";
                _needs.Add(El.Div("ct-need", El.Text(Strings.NeedName(n), "ct-need-label"), El.Div("ct-need-track", bar), value));
            }

            _chat = new ChatBar();
            _emotes = new EmotePicker();
            _movement = new MovementControls();
            _mic = El.DockButton("mic", Strings.T("hud.micOff"), ToggleMic);
            _micLabel = _mic.Q<Label>();

            var location = El.Div("ct-hud-location", _venue, El.Div("ct-hud-presence", _online, _mood));
            var cash = El.Div("ct-cash-card", new UiIcon("cash"),
                El.Div("ct-col", El.Text(Strings.T("hud.cash"), "ct-hud-caption"), _cash));
            var top = El.PassThrough(El.Div("ct-hud-top", location, El.Div("ct-grow"), cash));
            El.PassThrough(_needs);
            var social = El.Div("ct-hud-social",
                El.DockButton("chat", Strings.T("hud.chat"), () => { _emotes.Hide(); _chat.Toggle(); }),
                El.DockButton("emote", Strings.T("hud.emote"), () => { _chat.Hide(); _emotes.Toggle(); }), _mic,
                El.DockButton("phone", Strings.T("hud.phone"), () => OpenPhone?.Invoke(), "ct-dock-action ct-dock-primary", null));
            var personal = El.Div("ct-hud-personal",
                El.DockButton("map", Strings.T("hud.map"), () => OpenMap?.Invoke(), sfx: null),
                El.DockButton("lobby", Strings.T("hud.lobby"), () => OpenHome?.Invoke()));
            // Movement owns the lower left. Social/utility actions stay on the right.
            social.pickingMode = personal.pickingMode = PickingMode.Ignore;
            _root = El.Div("ct-hud", top, _needsToggle, _needs, _movement.Element, _emotes.Element, _chat.Element, social, personal);
            _root.pickingMode = PickingMode.Ignore;

            _subs.On<ProfileChangedSignal>(_ => RefreshProfile());
            _subs.On<DegradedChangedSignal>(_ => RefreshDegraded());
            _subs.On<OnlineCountSignal>(s => _online.text = Strings.F("hud.online", s.online));
            _subs.On<MoodSignal>(s =>
            {
                _mood.text = Strings.F("hud.mood", Money.Bps(s.bps));
                _mood.EnableInClassList("ct-green", s.bps > 0);
                _mood.EnableInClassList("ct-red", s.bps < 0);
            });
            _subs.On<VenueChangedSignal>(s =>
            {
                var reg = Services.Get<ConfigStore>().Registries;
                _venue.text = s.venueId != null ? reg.Venue(s.venueId)?.name ?? s.venueId : "";
            });
            RefreshProfile();
            RefreshDegraded();
        }

        public void Show(bool on)
        {
            _root.Show(on);
            _movement.SetEnabled(on);
            if (!on) { _chat.Hide(); _emotes.Hide(); }
        }

        private void RefreshDegraded()
        {
            bool voice = Features.IsAvailable(Features.Voice);
            _mic.SetEnabled(voice && !_micPending && !_disposed);
            bool on = voice && Services.TryGet<VoiceService>(out var service) && service.MicOn;
            _micLabel.text = Strings.T(!voice ? "hud.voiceUnavailable" : on ? "hud.micOn" : "hud.micOff");
            _mic.EnableInClassList("ct-btn-live", on);
        }

        private void RefreshProfile()
        {
            if (!Services.TryGet<PlayerProfile>(out var p) || p.Me == null) return;
            _cash.text = Money.Cash(p.CashCents);
            var n = p.Me.Needs;
            if (n == null) return;
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
        }

        private void ToggleNeeds()
        {
            _needsExpanded = !_needsExpanded;
            _needs.Show(_needsExpanded);
            _needsToggle.tooltip = Strings.T(_needsExpanded ? "hud.needsHide" : "hud.needsShow");
            _needsToggle.EnableInClassList("ct-needs-open", _needsExpanded);
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
            _subs.Dispose();
        }
    }

    /// <summary>"Say something out loud" (feature #6): 140 chars, once per 2 s, bubble over your avatar.</summary>
    public sealed class ChatBar
    {
        public VisualElement Element { get; }
        private readonly TextField _field;

        public ChatBar()
        {
            _field = new TextField { maxLength = 140 };
            _field.AddToClassList("ct-chat-field");
            _field.tooltip = Strings.T("hud.chatPrompt");
            _field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) { Hide(); e.StopImmediatePropagation(); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                { Send(); e.StopImmediatePropagation(); }
            }, TrickleDown.TrickleDown);
            Element = El.Div("ct-chat", El.Text(Strings.T("hud.chatPrompt"), "ct-small ct-muted"),
                El.Div("ct-row", _field, El.Btn(Strings.T("hud.say"), Send, "ct-btn ct-btn-primary")));
            Element.Show(false);
        }

        public void Toggle()
        {
            bool on = Element.resolvedStyle.display == DisplayStyle.None;
            Element.Show(on);
            if (on) _field.Focus();
        }

        public void Hide() => Element.Show(false);

        private void Send()
        {
            var text = _field.value?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            var ch = VenueSession.Current?.Channel;
            if (ch == null) return;
            float wait = ch.Say(text);
            if (wait > 0f)
            {
                Bus.Publish(ToastSignal.Text(Strings.F("hud.wait", wait), ToastKind.Bad));
                return;
            }
            _field.value = "";
            Element.Show(false);
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
                grid.Add(El.Btn(Strings.ReadableId(id.Replace("emote_", "")), () =>
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
