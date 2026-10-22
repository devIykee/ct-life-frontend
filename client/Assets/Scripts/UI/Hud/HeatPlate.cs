using CTLife.Core;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// "Sleuth heat" (City HUD v2, hud-spec section 7): a dark red plate with five magnifying-glass pips under Cash. Built
    /// but hidden: there is no heat system yet, and the HUD shows only real values, so it appears only once
    /// <see cref="Set"/> receives a real level above zero.
    /// </summary>
    public sealed class HeatPlate
    {
        public const int MaxPips = 5;
        public VisualElement Element { get; }
        private readonly VisualElement[] _pips = new VisualElement[MaxPips];

        public HeatPlate()
        {
            var row = El.Div("hud-heat-pips");
            for (int i = 0; i < MaxPips; i++) { _pips[i] = new UiIcon("heat"); _pips[i].AddToClassList("hud-heat-pip"); row.Add(_pips[i]); }
            Element = El.Div("hud-heat", El.Text(Strings.T("hud.heat").ToUpperInvariant(), "hud-heat-label"), row);
            Element.name = "hud-heat";
            Element.pickingMode = PickingMode.Ignore;
            foreach (var c in Element.Query<VisualElement>().ToList()) c.pickingMode = PickingMode.Ignore;
            Element.style.display = DisplayStyle.None;
        }

        /// <summary>A real heat level (0..5) from the server; null or 0 hides the plate.</summary>
        public void Set(int? level)
        {
            int n = level ?? 0;
            for (int i = 0; i < MaxPips; i++) _pips[i].EnableInClassList("hud-heat-pip--lit", i < n);
            HudFade.Set(Element, n > 0);
        }
    }
}
