using System.Collections.Generic;
using System;
using System.Linq;
using CTLife.Core;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Shared district guide for the standalone screen and the in-game phone.
    /// Venue availability follows the caller's current phase; travel remains owned by the caller.
    /// </summary>
    public sealed class CityMapScreen : UiScreen
    {
        public static int CurrentPhase => Phases.Live;

        private readonly Registries _reg;
        private readonly Action<string> _onTravel;
        private readonly Action _onClose;

        public CityMapScreen(Registries reg, Action<string> onTravel, Action onClose = null)
        {
            _reg = reg;
            _onTravel = onTravel;
            _onClose = onClose;
        }

        protected override VisualElement Build() => BuildGuide(_reg, CurrentPhase, _onTravel, _onClose);

        internal static VisualElement BuildGuide(Registries reg, int phase, Action<string> onTravel, Action onClose = null)
        {
            var scroll = El.VerticalScroll();
            scroll.AddToClassList("ct-scroll");
            scroll.AddToClassList("ct-map-scroll");
            // The rc1 registry lists planned venues without scene keys or phases. A listed ID
            // alone must not advertise an empty greybox as a destination. Later streamed venues
            // need a confirmed content-catalog entry before they become available in this guide.
            var available = AvailableVenues(reg, phase);
            int openCount = available.Count;
            scroll.Add(El.Div("ct-map-intro", El.Text(Strings.T("map.body"), "ct-body ct-muted"),
                El.Text(openCount == 1 ? Strings.T("map.openCountOne") : Strings.F("map.openCount", openCount), "ct-map-count")));
            foreach (var district in reg.Districts)
            {
                var venues = reg.VenueOrder.Where(v => v.districtId == district.Key).ToList();
                if (venues.Count == 0) continue;
                var block = El.Div("ct-district ct-district-" + district.Key,
                    El.Div("ct-district-heading", El.Div("ct-district-symbol", new UiIcon(DistrictIcon(district.Key))),
                        El.Div("ct-grow", El.Text(district.Value, "ct-h2"), El.Text(Strings.DistrictDetail(district.Key), "ct-small ct-muted"))));
                foreach (var v in venues)
                {
                    bool open = available.Contains(v.id);
                    var row = El.Btn("", () => { if (open) onTravel?.Invoke(v.id); }, open ? "ct-venue" : "ct-venue ct-venue-locked", sfx: null);
                    row.name = "map-venue-" + v.id;
                    row.Add(El.Div("ct-grow", El.Text(v.name, "ct-venue-title"),
                        El.Text(Strings.T(open ? (v.isClub ? "map.club" : "map.open") : "map.closed"), "ct-venue-status")));
                    if (open) row.Add(new UiIcon("next"));
                    row.SetEnabled(open);
                    block.Add(row);
                }
                scroll.Add(block);
            }
            if (reg.VenueOrder.Count == 0) scroll.Add(El.Text(Strings.T("map.empty"), "ct-body"));
            var header = El.Div("ct-row ct-header", El.Div("ct-grow", El.Text(Strings.T("map.eyebrow"), "ct-eyebrow"),
                El.Text(Strings.T("map.title"), "ct-h1")));
            if (onClose != null) header.Add(El.IconButton("close", Strings.T("common.close"), onClose, "ct-btn ct-btn-icon ct-btn-ghost"));
            return El.Div("ct-map", header, scroll);
        }

        /// <summary>Venues the player can travel to now: in phase and with content (bundled or a streamable scene).</summary>
        internal static HashSet<string> AvailableVenues(Registries reg, int phase) =>
            reg.VenueOrder.Where(v => v.phase <= phase &&
                (VenueContent.Has(v.id) ||
                 (!string.IsNullOrEmpty(v.sceneKey) && Application.CanStreamedLevelBeLoaded(v.sceneKey))))
                .Select(v => v.id).ToHashSet();

        internal static string DistrictIconFor(string id) => DistrictIcon(id);

        private static string DistrictIcon(string id) => id switch
        {
            "timeline" => "chat",
            "trenches" => "perps",
            "builder_hills" => "projects",
            "airdrop_farm" => "energy",
            "dubai_marina" => "dream",
            _ => "city",
        };
    }
}
