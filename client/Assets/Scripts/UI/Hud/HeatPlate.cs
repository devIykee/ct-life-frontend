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
        private readonly UiIcon[] _pips = new UiIcon[MaxPips];
        private readonly Label _label;
        private IVisualElementScheduledItem _flash;
        private bool _flashOn;
        private int _stars;
        // the last real state: a HUD rebuilt on a venue change shows it at once (not only on the next server read)
        private static int _lastStars, _lastTier;
        private static string _lastWord;
        private static bool _lastPursuit;

        public HeatPlate()
        {
            var row = El.Div("hud-heat-pips");
            for (int i = 0; i < MaxPips; i++) { _pips[i] = new UiIcon("star-outline"); _pips[i].AddToClassList("hud-heat-pip"); row.Add(_pips[i]); }
            _label = El.Text(Strings.T("hud.heat").ToUpperInvariant(), "hud-heat-label");
            _label.style.display = DisplayStyle.None;   // GTA-plain (approved brief 2026-10-25): stars only
            Element = El.Div("hud-heat", _label, row);
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
            for (int i = 0; i < MaxPips; i++)
            {
                bool lit = i < n;
                _pips[i].EnableInClassList("hud-heat-pip--lit", lit);
                _pips[i].SetIcon(lit ? "star" : "star-outline");
                // a new star pops in (the GTA "star gained" beat)
                if (lit && i >= _stars && _stars > 0) Pop(_pips[i]);
            }
            _stars = n;
            if (!string.IsNullOrEmpty(word)) _label.text = word;
            Element.EnableInClassList("hud-heat--warm", tier == 1);
            Element.EnableInClassList("hud-heat--hot", tier == 2);
            Element.EnableInClassList("hud-heat--wanted", tier >= 3);
            Flash(pursuit && n > 0);
            HudFade.Set(Element, n > 0 || pursuit);
        }

        private void Flash(bool on)
        {
            if (on == (_flash != null)) return;
            // the lit stars flash red / blue (GTA's chase stars), not the pill
            if (on) _flash = Element.schedule.Execute(() => { _flashOn = !_flashOn; Element.EnableInClassList("hud-heat--red", _flashOn); Element.EnableInClassList("hud-heat--blue", !_flashOn); }).Every(320);
            else
            {
                _flash.Pause(); _flash = null;
                Element.RemoveFromClassList("hud-heat--red"); Element.RemoveFromClassList("hud-heat--blue");
            }
        }

        private static void Pop(VisualElement e)
        {
            e.AddToClassList("hud-heat-pip--pop");
            e.schedule.Execute(() => e.RemoveFromClassList("hud-heat-pip--pop")).ExecuteLater(220);
        }
    }
}
