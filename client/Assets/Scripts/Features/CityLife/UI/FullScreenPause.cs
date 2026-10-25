using System.Collections.Generic;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// The world rests behind an opaque full-screen game panel (the Rug Room, the Rekt Burger shift). Measured on the Infinix
    /// (2026-10-25): the hidden world cost ~45 ms of main thread a frame (traffic, pedestrians, the instance renderer's
    /// submissions, the HUD and 20+ name tags re-laid out) and the camera's full-screen passes. While a panel holds it: the
    /// main camera culls nothing into a 1% viewport, the HUD / label / world UI layers are hidden, the island instances are
    /// not submitted, traffic is paused and the pedestrian system is off. <see cref="End"/> restores exactly what it changed.
    /// </summary>
    public sealed class FullScreenPause
    {
        private Camera _cam;
        private int _camMask;
        private Rect _camRect;
        private readonly List<(VisualElement el, StyleEnum<DisplayStyle> was)> _hiddenLayers = new List<(VisualElement, StyleEnum<DisplayStyle>)>();
        private bool _active, _hidWorld, _pausedTraffic, _stoppedPeds;

        public void Begin()
        {
            if (_active) return;
            _active = true;
            _cam = Camera.main;
            if (_cam != null) { _camMask = _cam.cullingMask; _camRect = _cam.rect; _cam.cullingMask = 0; _cam.rect = new Rect(0f, 0f, 0.01f, 0.01f); }
            var ui = UiRoot.Instance;
            if (ui != null)
                foreach (var layer in new[] { ui.WorldLayer, ui.LabelLayer, ui.HudLayer })
                    if (layer != null) { _hiddenLayers.Add((layer, layer.style.display)); layer.style.display = DisplayStyle.None; }
            if (!IslandInstanceRenderer.HideWorld) { IslandInstanceRenderer.HideWorld = true; _hidWorld = true; }
            var traffic = CTLife.World.Vehicles.TrafficSystem.Current;
            if (traffic != null && !traffic.Paused) { traffic.Paused = true; _pausedTraffic = true; }
            var peds = CTLife.World.Pedestrians.PedestrianSystem.Current;
            if (peds != null && peds.enabled) { peds.enabled = false; _stoppedPeds = true; }
        }

        public void End()
        {
            if (!_active) return;
            _active = false;
            if (_cam != null)
            {
                if (_cam.cullingMask == 0) _cam.cullingMask = _camMask;
                _cam.rect = _camRect;
                _cam = null;
            }
            foreach (var (el, was) in _hiddenLayers) el.style.display = was;
            _hiddenLayers.Clear();
            if (_hidWorld) { IslandInstanceRenderer.HideWorld = IslandInteriors.Current != null && IslandInteriors.Current.WorldHidden; _hidWorld = false; }
            if (_pausedTraffic) { var t = CTLife.World.Vehicles.TrafficSystem.Current; if (t != null) t.Paused = false; _pausedTraffic = false; }
            if (_stoppedPeds) { var p = CTLife.World.Pedestrians.PedestrianSystem.Current; if (p != null) p.enabled = true; _stoppedPeds = false; }
        }
    }
}
