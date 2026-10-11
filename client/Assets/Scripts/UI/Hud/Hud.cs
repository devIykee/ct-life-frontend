using System;
using System.Linq;
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
        private readonly Button _mic;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly ChatBar _chat;
        private readonly EmotePicker _emotes;

        public Action OpenPhone;
        public Action OpenMap;

        public VisualElement Element => _root;

        public Hud()
        {
            _cash = El.Text("$0.00", "ct-cash");
            _online = El.Text("● 0 online", "ct-pill");
            _mood = El.Text("Mood —", "ct-pill");
            _venue = El.Text("", "ct-venue-name");
            _needs = El.Div("ct-needs");
            foreach (var n in new[] { "energy", "hunger", "fun", "hygiene", "sanity" })
            {
                var bar = El.Div("ct-need-fill");
                bar.name = n;
                _needs.Add(El.Div("ct-need", El.Text(Strings.NeedName(n).Substring(0, 1), "ct-need-label"), El.Div("ct-need-track", bar)));
            }

            _chat = new ChatBar();
            _emotes = new EmotePicker();
            _mic = El.Btn("Mic off", ToggleMic, "ct-btn ct-btn-round");

            var top = El.Div("ct-hud-top", El.Div("ct-col", _cash, _venue), El.Div("ct-grow"), El.Div("ct-col ct-right", _online, _mood));
            var bottom = El.Div("ct-hud-bottom",
                El.Btn("Chat", () => _chat.Toggle(), "ct-btn ct-btn-round"),
                El.Btn("Emote", () => _emotes.Toggle(), "ct-btn ct-btn-round"),
                _mic,
                El.Div("ct-grow"),
                El.Btn("Map", () => OpenMap?.Invoke(), "ct-btn ct-btn-round"),
                El.Btn("Phone", () => OpenPhone?.Invoke(), "ct-btn ct-btn-round ct-btn-primary"));
            _root = El.Div("ct-hud", top, _needs, El.Div("ct-grow"), _emotes.Element, _chat.Element, bottom);
            _root.pickingMode = PickingMode.Ignore;
            foreach (var c in _root.Children()) if (c.ClassListContains("ct-grow")) c.pickingMode = PickingMode.Ignore;

            _subs.On<ProfileChangedSignal>(_ => RefreshProfile());
            _subs.On<DegradedChangedSignal>(_ => RefreshDegraded());
            _subs.On<OnlineCountSignal>(s => _online.text = $"● {s.online:N0} online");
            _subs.On<MoodSignal>(s =>
            {
                _mood.text = "Mood " + Money.Bps(s.bps);
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

        public void Show(bool on) => _root.Show(on);

        private void RefreshDegraded()
        {
            bool voice = Features.IsAvailable(Features.Voice);
            _mic.SetEnabled(voice);
            if (!voice) _mic.text = "Voice unavailable";
            else if (_mic.text == "Voice unavailable") _mic.text = "Mic off";
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
        }

        private void SetNeed(string id, int value)
        {
            var fill = _needs.Q(id);
            if (fill == null) return;
            fill.style.width = Length.Percent(Mathf.Clamp(value, 0, 100));
            fill.EnableInClassList("ct-need-low", value < 25);
        }

        private async void ToggleMic()
        {
            if (!Services.TryGet<VoiceService>(out var voice)) return;
            if (!Features.IsAvailable(Features.Voice))
            {
                Bus.Publish(ToastSignal.Text("Voice is temporarily unavailable", ToastKind.Bad));
                return;
            }
            if (!voice.IsConnected && VenueSession.Current != null) await VenueSession.Current.ConnectVoice();
            bool ok = await voice.SetMic(!voice.MicOn);
            _mic.text = voice.MicOn ? "Mic on" : "Mic off";
            _mic.EnableInClassList("ct-btn-live", voice.MicOn);
            if (!ok) Bus.Publish(ToastSignal.Text("Voice isn't available here yet", ToastKind.Bad));
        }

        public void Dispose() => _subs.Dispose();
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
            _field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return) Send(); });
            Element = El.Div("ct-chat", _field, El.Btn("Say", Send, "ct-btn ct-btn-primary"));
            Element.Show(false);
        }

        public void Toggle()
        {
            bool on = Element.resolvedStyle.display == DisplayStyle.None;
            Element.Show(on);
            if (on) _field.Focus();
        }

        private void Send()
        {
            var text = _field.value?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            var ch = VenueSession.Current?.Channel;
            if (ch == null) return;
            float wait = ch.Say(text);
            if (wait > 0f)
            {
                Bus.Publish(ToastSignal.Text($"Wait {wait:0.0}s", ToastKind.Bad));
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

        private void Populate()
        {
            var reg = Services.Get<ConfigStore>().Registries;
            foreach (var a in reg.Anims.Values.Where(a => a.kind == AnimKind.Emote))
            {
                var id = a.id;
                Element.Add(El.Btn(id.Replace("emote_", "").Replace('_', ' '), () =>
                {
                    var s = VenueSession.Current;
                    if (s?.Channel == null) return;
                    if (s.Channel.Emote(id)) s.LocalPlayer?.GetComponent<AvatarView>()?.PlayOneShot(id);
                    Element.Show(false);
                }, "ct-chip", "sfx_ui_tap"));
            }
        }
    }
}
