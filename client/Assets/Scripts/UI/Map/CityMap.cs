using System;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine.UIElements;

namespace CTLife.UI.Map
{
    /// <summary>The in-city full-screen map: the shared <see cref="FullMapView"/> in the Arena look over the HUD.</summary>
    public static class CityMap
    {
        public static FullMapView Current { get; private set; }

        public static FullMapView Show(VisualElement layer, Action<string> travel, Action openHome, Func<HashSet<string>> available, Action closed)
        {
            Close();
            var host = new VisualElement { name = "map-city" };
            host.AddToClassList("map-city-layer");
            OnboardingFrame.AddSheet(host, "UI/map");
            var view = new FullMapView(FullMapView.Look.City) { Travel = travel, OpenHome = openHome, Available = available };
            view.Close = () => { Close(); closed?.Invoke(); };
            host.Add(view);
            layer.Add(host);
            Current = view;
            return view;
        }

        public static void Close()
        {
            if (Current == null) return;
            Current.parent?.RemoveFromHierarchy();
            Current = null;
        }

        /// <summary>Venues that can be travelled to in this phase (shipped content), for hosts outside the UI assembly.</summary>
        public static HashSet<string> AvailableVenues(int phase) => CityMapScreen.AvailableVenues(Services.Get<ConfigStore>().Registries, phase);
    }
}
