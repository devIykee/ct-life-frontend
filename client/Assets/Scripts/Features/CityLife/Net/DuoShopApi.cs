using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    /// <summary>
    /// Duo Shop, delivery and home-door routes (backend PRs #7 homes/flat doors and #8 Duo Shop, contract/CHANGELOG.md
    /// 2026-10-23). Transport policy is RestClient's; every write takes the caller's retained Idempotency-Key.
    /// </summary>
    public sealed class DuoShopApi
    {
        private readonly IRestClient _rest;
        public DuoShopApi(IRestClient rest) { _rest = rest ?? throw new ArgumentNullException(nameof(rest)); }

        public static readonly string[] Routes =
        {
            "GET /shop/catalog", "POST /shop/orders", "GET /shop/orders", "GET /shop/orders/{id}", "POST /shop/orders/{id}/cancel",
            "GET /me/items", "GET /homes/{homeId}/placements", "PUT /homes/{homeId}/placements", "GET /units/{unitId}/access", "GET /me",
        };

        private Task<JToken> Write(string method, string path, JObject body, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            return _rest.Send<JToken>(method, path, (body ?? new JObject()).ToString(Formatting.None), true, key, ct);
        }

        public async Task<ShopCatalog> Catalog(CancellationToken ct) => ShopCatalog.Parse(await _rest.Get<JToken>("/shop/catalog", ct));

        public async Task<ShopOrder> Order(ShopCart cart, string window, string destination, string key, CancellationToken ct) =>
            ShopOrder.Parse(await Write("POST", "/shop/orders", cart.Body(window, destination), key, ct));

        public async Task<List<ShopOrder>> Orders(CancellationToken ct) => ShopOrder.ParseList(await _rest.Get<JToken>("/shop/orders", ct));

        public async Task<ShopOrder> OrderById(string orderId, CancellationToken ct) =>
            ShopOrder.Parse(await _rest.Get<JToken>("/shop/orders/" + CityWire.Seg(orderId), ct));

        public async Task<ShopOrder> Cancel(string orderId, string key, CancellationToken ct) =>
            ShopOrder.Parse(await Write("POST", "/shop/orders/" + CityWire.Seg(orderId) + "/cancel", null, key, ct));

        public async Task<List<OwnedItem>> MyItems(CancellationToken ct) => OwnedItem.ParseList(await _rest.Get<JToken>("/me/items", ct));

        public async Task<List<Placement>> Placements(string homeId, CancellationToken ct) =>
            Placement.ParseList(await _rest.Get<JToken>("/homes/" + CityWire.Seg(homeId) + "/placements", ct));

        public async Task<List<Placement>> SavePlacements(string homeId, IEnumerable<Placement> placements, string key, CancellationToken ct) =>
            Placement.ParseList(await Write("PUT", "/homes/" + CityWire.Seg(homeId) + "/placements", Placement.Body(placements), key, ct));

        public async Task<UnitAccess> Access(string unitId, CancellationToken ct) =>
            UnitAccess.Parse(await _rest.Get<JToken>("/units/" + CityWire.Seg(unitId) + "/access", ct));

        /// <summary>`GET /me` read raw for its `home` object (the generated Me model predates it).</summary>
        public async Task<(MyHome home, long cashCents)> Me(CancellationToken ct)
        {
            var me = await _rest.Get<JToken>("/me", ct);
            return (MyHome.Parse(me), CityWire.LongOr(me?["cashCents"], -1));
        }
    }
}
