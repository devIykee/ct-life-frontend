using System;
using CTLife.Core;
using CTLife.World;

namespace CTLife.Ventures
{
    /// <summary>
    /// Opens the right phone app when the player taps a Ventures spot in a venue (VC Row pitch room, Pixel Wall, Conference Hall expo,
    /// Builder Hills boards). Created once by the composition root (hook in docs/progress/requests.md) with a callback that opens a
    /// phone app by id. Safe before sign-in: it only reacts to taps.
    /// </summary>
    public sealed class VenturesSpotLinks : IDisposable
    {
        private readonly Action<string> _openApp;
        private bool _disposed;

        public VenturesSpotLinks(Action<string> openApp)
        {
            _openApp = openApp ?? throw new ArgumentNullException(nameof(openApp));
            WorldInput.SpotTapped += OnSpotTapped;
        }

        private void OnSpotTapped(SpotAnchor spot)
        {
            if (_disposed || spot == null || !Phases.IsOpen(5)) return;
            var venue = VenueSession.Current != null && VenueSession.Current.IsActive ? VenueSession.Current.VenueId : null;
            var app = VenturesSpots.AppFor(venue, spot.spotId);
            if (app != null) _openApp(app);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            WorldInput.SpotTapped -= OnSpotTapped;
        }
    }
}
