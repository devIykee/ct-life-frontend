using CTLife.Core;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// "Sleuth heat" (City HUD v2, hud-spec section 7; backend #17 heat, lane B 2026-10-25): the wanted meter under Cash, as
    /// in GTA V/SA (stars under the money, top right) and the GTA VI wanted row (references and sources in
    /// ct-life-evidence/heat-police-20261025/refs). It shows a level word and five stars, filled and outlined. Its tint
    /// follows the level (warm white, hot amber, wanted red), and it flashes red / blue while a pursuit runs. It stays
    /// hidden at zero: the HUD shows only real values from GET /v1/heat/me.
    /// </summary>
    public sealed class HeatPlate
    {
        public const int MaxPips = 5;
        /// <summary>The live plate (Hud builds one); feature code sets it from the server's heat.</summary>
        public static HeatPlate Current { get; private set; }

        public VisualElement Element { get; }
        private readonly VisualElement _stars;
        private readonly Label _label;
        private IVisualElementScheduledItem _flash;
        private bool _flashOn;
        private int _shown, _logged = -1;
        // the last real state: a HUD rebuilt on a venue change shows it at once (not only on the next server read)
        private static int _lastStars, _lastTier;
        private static string _lastWord;
        private static bool _lastPursuit;

        public HeatPlate()
        {
            // design system v2: the wanted-stars HUD pill (five stars, a danger edge while wanted, red / blue in a chase)
            _stars = CTLife.UI.DS.Ds.Stars(0, MaxPips);
            _stars.name = "hud-heat-stars";
            _label = El.Text(Strings.T("hud.heat").ToUpperInvariant(), "hud-heat-label");
            _label.style.display = DisplayStyle.None;   // GTA-plain (approved brief 2026-10-25): stars only
            Element = El.Div("hud-heat ds-hud-pill ds-hud-stars", _label, _stars);
            Element.name = "hud-heat";
            Element.pickingMode = PickingMode.Ignore;
            foreach (var c in Element.Query<VisualElement>().ToList()) c.pickingMode = PickingMode.Ignore;
            Element.style.display = DisplayStyle.None;
            Current = this;
            if (_lastStars > 0 || _lastPursuit) Set(_lastStars, _lastWord, _lastTier, _lastPursuit);
        }

        /// <summary>A real heat level (0..5 stars) from the server; null or 0 hides the plate.</summary>
        public void Set(int? level) => Set(level ?? 0, null, 0, false);

        /// <summary>Stars (0..5), the level word ("WARM", "HOT", "WANTED"), its tier (1 warm, 2 hot, 3 wanted) and a running pursuit.</summary>
        public void Set(int stars, string word, int tier, bool pursuit)
        {
            int n = System.Math.Max(0, System.Math.Min(MaxPips, stars));
            _lastStars = n; _lastWord = word; _lastTier = tier; _lastPursuit = pursuit;
            CTLife.UI.DS.Ds.SetStars(_stars, n, MaxPips);
            // a new star pops in (the GTA "star gained" beat)
            for (int i = _shown; i < n && _shown > 0; i++) Pop(_stars[i]);
            _shown = n;
            if (!string.IsNullOrEmpty(word)) _label.text = word;
            Element.EnableInClassList("hud-heat--warm", tier == 1);
            Element.EnableInClassList("hud-heat--hot", tier == 2);
            Element.EnableInClassList("hud-heat--wanted", tier >= 3);
            Element.EnableInClassList("ds-hud-pill--danger", tier >= 3 || pursuit);
            Flash(pursuit && n > 0);
            HudFade.Set(Element, n > 0 || pursuit);
            if (n != _logged) { _logged = n; CTLife.Core.Log.Info("heat", $"plate {n} stars tier {tier} pursuit {pursuit} attached {Element.panel != null}"); }
        }

        private void Flash(bool on)
        {
            if (on == (_flash != null)) return;
            // the lit stars flash red / blue (GTA's chase stars), not the pill
            if (on) _flash = Element.schedule.Execute(() => { _flashOn = !_flashOn; _stars.EnableInClassList("ds-stars--red", _flashOn); _stars.EnableInClassList("ds-stars--blue", !_flashOn); }).Every(320);
            else
            {
                _flash.Pause(); _flash = null;
                _stars.RemoveFromClassList("ds-stars--red"); _stars.RemoveFromClassList("ds-stars--blue");
            }
        }

        private static void Pop(VisualElement e)
        {
            e.AddToClassList("ds-star--pop");
            e.schedule.Execute(() => e.RemoveFromClassList("ds-star--pop")).ExecuteLater(220);
        }
    }
}
