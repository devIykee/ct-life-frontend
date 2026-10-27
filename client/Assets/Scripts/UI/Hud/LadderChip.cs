using System;
using CTLife.Core;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// The always-visible dream ladder chip (queue 18, approved brief: ct-life-evidence/onboarding-17), under the player
    /// plate top left: the current rung → the next and a thin progress bar (Clash Royale's Trophy Road "you are here",
    /// in our HUD tokens). It pulses when progress grows, and tapping it opens the ladder. Hidden until feature code sets
    /// a real state (the HUD shows only real values).
    /// </summary>
    public sealed class LadderChip
    {
        public static LadderChip Current { get; private set; }
        /// <summary>Tap: feature code opens the ladder sheet.</summary>
        public static event Action Tapped;

        public VisualElement Element { get; }
        private readonly Label _from, _to;
        private readonly VisualElement _fill;
        private float _last = -1f;
        private static (string from, string to, float fraction, bool top)? _state;

        public LadderChip()
        {
            _from = El.Text("", "hud-ladder-from ds-t-hud-caption");
            _to = El.Text("", "hud-ladder-to ds-t-hud-caption");
            _fill = El.Div("hud-ladder-fill ds-bar-fill");
            var b = new Button(() => Tapped?.Invoke()) { text = "" };
            b.AddToClassList("hud-ladder"); b.AddToClassList("ds-hud-pill"); b.AddToClassList("ds-hud-pill--stack");
            b.name = "hud-ladder";
            b.Add(El.PassThrough(El.Div("hud-ladder-row", CTLife.UI.DS.Ds.Icon("house-line", 28, "white"), _from, El.Text("→", "hud-ladder-arrow ds-t-hud-caption"), _to)));
            b.Add(El.PassThrough(El.Div("hud-ladder-track ds-bar", _fill)));
            Element = b;
            Element.style.display = DisplayStyle.None;
            Current = this;
            if (_state is var (f, t, fr, top)) Set(f, t, fr, top);
        }

        /// <summary>The current rung's name, the next rung's name (or the goal word at the top) and progress 0..1.</summary>
        public void Set(string from, string to, float fraction, bool top)
        {
            _state = (from, to, fraction, top);
            _from.text = from ?? "";
            _to.text = to ?? "";
            _fill.style.width = Length.Percent(Math.Max(0f, Math.Min(1f, fraction)) * 100f);
            Element.EnableInClassList("hud-ladder--top", top);
            if (_last >= 0f && fraction > _last + 0.005f) Pulse();
            _last = fraction;
            HudFade.Set(Element, !string.IsNullOrEmpty(from));
        }

        /// <summary>Feature code: the ladder's state, kept for a HUD built later (the chip replays it).</summary>
        public static void Show(string from, string to, float fraction, bool top)
        {
            _state = (from, to, fraction, top);
            Current?.Set(from, to, fraction, top);
        }

        public static void Hide() { _state = null; if (Current != null) HudFade.Set(Current.Element, false); }

        private void Pulse()
        {
            Element.AddToClassList("hud-ladder--pulse");
            Element.schedule.Execute(() => Element.RemoveFromClassList("hud-ladder--pulse")).ExecuteLater(420);
        }
    }
}
