using System;
using System.Linq;
using CTLife.Core;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// CT City overview (feature #4): five districts, their venues, travel by tap. Venues of later phases are
    /// listed with the phase that opens them. [Inference] This 2D map stands in for the low-detail 3D map scene
    /// until the environment art exists; both call the same travel callback.
    /// </summary>
    public sealed class CityMapScreen : UiScreen
    {
        public const int CurrentPhase = 1;

        private readonly Registries _reg;
        private readonly Action<string> _onTravel;
        private readonly Action _onClose;

        public CityMapScreen(Registries reg, Action<string> onTravel, Action onClose = null)
        {
            _reg = reg;
            _onTravel = onTravel;
            _onClose = onClose;
        }

        protected override VisualElement Build()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("ct-scroll");
            foreach (var district in _reg.Districts)
            {
                var venues = _reg.VenueOrder.Where(v => v.districtId == district.Key).ToList();
                if (venues.Count == 0) continue;
                var block = El.Div("ct-district ct-district-" + district.Key, El.Text(district.Value, "ct-h2"));
                foreach (var v in venues)
                {
                    bool open = v.phase <= CurrentPhase || v.id == "ore_refinery" && CurrentPhase >= 2;
                    var row = El.Btn(v.name, () => { if (open) _onTravel?.Invoke(v.id); }, open ? "ct-venue" : "ct-venue ct-venue-locked");
                    row.Add(El.Text(open ? (v.isClub ? "Club" : "Open") : $"Opens in Phase {v.phase}", "ct-small"));
                    row.SetEnabled(open);
                    block.Add(row);
                }
                scroll.Add(block);
            }
            var header = El.Div("ct-row ct-header", El.Text("CT City", "ct-h1"), El.Div("ct-grow"));
            if (_onClose != null) header.Add(El.Btn("Close", _onClose, "ct-btn ct-btn-ghost"));
            return El.Div("ct-map", header, scroll);
        }
    }
}
