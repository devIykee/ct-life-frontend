using System;
using CTLife.Core;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// Homes behind flat doors and Duo Shop deliveries while signed in (2026-10-23): the <see cref="HomeShopService"/> (door
    /// gate, home room, orders), the <see cref="DeliveryDirector"/> (courier van, parcels, locker), <see cref="PlaceMode"/>
    /// (furniture in the flats) and the locked door's "Knock" prompt. Created by FlowController next to the City Life
    /// overlays; rebinds on sign-in and sign-out.
    /// </summary>
    public sealed class HomeShopOverlays : IDisposable
    {
        private readonly VisualElement _layer;
        private readonly Session _session;
        private readonly Subscriptions _subs = new Subscriptions();
        private HomeShopService _svc;
        private DeliveryDirector _delivery;
        private PlaceMode _place;
        private readonly Func<Vector3, WorldOffer> _knock;
        private bool _disposed;

        public HomeShopOverlays(VisualElement modalLayer)
        {
            _layer = modalLayer;
            _session = Services.Get<Session>();
            _session.Changed += Bind;
            _knock = Knock;
            WorldOffers.Add(_knock);
            Bind();
        }

        private void Bind()
        {
            if (_disposed) return;
            var user = _session.IsSignedIn ? _session.UserId : null;
            if (_svc != null && !_svc.Disposed && _svc.UserId == user) return;
            Unbind();
            if (string.IsNullOrEmpty(user)) return;
            _svc = new HomeShopService(user);
            _delivery = DeliveryDirector.Create(_svc);
            _place = PlaceMode.Create(_svc, _layer);
        }

        private void Unbind()
        {
            if (_place != null) UnityEngine.Object.Destroy(_place.gameObject);
            if (_delivery != null) UnityEngine.Object.Destroy(_delivery.gameObject);
            _place = null; _delivery = null;
            _svc?.Dispose();
            _svc = null;
        }

        /// <summary>A stranger's flat: the leaf stays shut and the door offers Knock (it sends nothing yet).</summary>
        private WorldOffer Knock(Vector3 player)
        {
            var rooms = IslandInteriors.Current;
            if (rooms == null || _svc == null) return null;
            for (int i = 0; i < rooms.DoorCount; i++)
            {
                if (!rooms.TryGetDoor(i, out var door, out var rot)) continue;
                var front = door + rot * Vector3.forward * 1.1f;
                if (InteractionFinder.Flat(player, front) > 2.4f || !rooms.IsLocked(i)) continue;
                return new WorldOffer
                {
                    id = "knock:" + i, verbKey = "interact.knock", icon = "door", tagKey = "interact.tag.flat", tagLocked = true, primary = false,
                    position = door + rot * Vector3.forward * 0.2f + Vector3.up * 1.7f, range = 2.4f,
                    perform = () => Bus.Publish(ToastSignal.Text(ShopText.T("knock"))),
                };
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Changed -= Bind;
            WorldOffers.Remove(_knock);
            _subs.Dispose();
            Unbind();
        }
    }
}
