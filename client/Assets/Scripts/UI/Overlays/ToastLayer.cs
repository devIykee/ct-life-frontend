using System.Collections.Generic;
using CTLife.Core;
using CTLife.Realtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Toasts (toasts.json ids, literal text) and server events (feature #10, city `event`): "JUST IN" banners
    /// with a stinger. Liquidations aimed at this player open the REKT card instead.
    /// </summary>
    public sealed class ToastLayer
    {
        private const int MaxVisible = 3;
        private readonly VisualElement _layer;
        private readonly Queue<(string text, ToastKind kind)> _queue = new Queue<(string, ToastKind)>();
        private readonly List<(VisualElement el, float until)> _live = new List<(VisualElement, float)>();
        private readonly Subscriptions _subs = new Subscriptions();

        public ToastLayer(VisualElement layer)
        {
            _layer = layer;
            _subs.On<ToastSignal>(OnToast);
            _subs.On<EventMessage>(OnEvent);
            UiRoot.Instance.AddUpdater(Tick);
        }

        private void OnToast(ToastSignal t)
        {
            var text = t.literal ?? Strings.Toast(t.textId, t.@params);
            var kind = t.kind;
            if (t.textId == "toast_drained") kind = ToastKind.Bad;
            Enqueue(text, kind);
            Bus.Publish(new SfxSignal(kind == ToastKind.Bad ? "sfx_ui_error" : "sfx_ui_toast"));
        }

        private void OnEvent(EventMessage e)
        {
            var p = e.@params ?? new Dictionary<string, string>();
            string P(string k) => p.TryGetValue(k, out var v) ? v : "?";
            string Cents(string k) => p.TryGetValue(k, out var v) && Money.TryParseCents(v, out var c) ? Money.Cash(c, true) : "?";
            string text;
            string sfx = "sfx_ui_toast";
            switch (e.eventId)
            {
                case "evt_liquidation": text = $"JUST IN: {P("alias")} got liquidated on ${P("ticker")} at {P("leverage")}x, {Cents("lossCents")} gone"; sfx = "sfx_liquidation"; break;
                case "evt_rug": text = $"JUST IN: ${P("ticker")} rugged. Peak mcap {Cents("peakMcapCents")}"; sfx = "sfx_rug"; break;
                case "evt_coin_launch": text = $"New launch: ${P("ticker")} ({P("name")})"; sfx = "sfx_new_coin"; break;
                case "evt_coin_graduated": text = $"${P("ticker")} graduated at {P("multiple")}x"; sfx = "sfx_graduation"; break;
                case "evt_big_win": text = $"{P("alias")} flipped {Cents("fromCents")} into {Cents("toCents")} on ${P("ticker")}"; sfx = "sfx_whale_horn"; break;
                case "evt_mood_change": text = $"Market Mood {(P("direction") == "up" ? "turned green" : "turned red")} ({P("moodBps")} bps)"; break;
                case "evt_exploit": text = $"JUST IN: exploit on {P("target")}"; sfx = "sfx_rug"; break;
                case "evt_ore_motherlode": text = $"JUST IN: ORE motherlode hit, {P("amountOre")} ORE"; sfx = "sfx_moon_horn"; break;
                case "evt_sunday_unlock": text = "Sunday Unlock: rent and gas are due"; sfx = "sfx_sunday_bell"; break;
                case "evt_meta_rotated": text = "The Weekly Meta rotated"; break;
                default:
                    Log.UnknownId("events", e.eventId);
                    text = "Something happened in CT City";
                    break;
            }
            Enqueue(text, ToastKind.Event);
            Bus.Publish(new SfxSignal(sfx));
        }

        private void Enqueue(string text, ToastKind kind)
        {
            _queue.Enqueue((text, kind));
            Pump();
        }

        private void Pump()
        {
            while (_live.Count < MaxVisible && _queue.Count > 0)
            {
                var (text, kind) = _queue.Dequeue();
                var el = El.Div("ct-toast ct-toast-" + kind.ToString().ToLowerInvariant(), El.Text(text, "ct-toast-text"));
                _layer.Add(el);
                _live.Add((el, Time.unscaledTime + (kind == ToastKind.Event ? 5f : 3.5f)));
            }
        }

        private void Tick()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _live[i].until) continue;
                _live[i].el.RemoveFromHierarchy();
                _live.RemoveAt(i);
            }
            Pump();
        }
    }
}
