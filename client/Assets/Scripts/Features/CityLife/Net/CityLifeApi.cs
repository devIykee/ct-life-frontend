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
    /// Thin HTTP wrapper for the Phase 4 City Life endpoints in contract/openapi.yaml (homes, rent, visit, needs actions,
    /// daily clue, meta, events, shop, inventory, look, payment intents) plus the clearly separated proposal routes
    /// (season pass status, home settings). Transport policy (auth refresh, version headers, 429/503, network retries
    /// with the same Idempotency-Key) is RestClient's. Every write takes the caller's retained Idempotency-Key.
    /// </summary>
    public sealed class CityLifeApi
    {
        private readonly IRestClient _rest;

        public CityLifeApi(IRestClient rest) { _rest = rest ?? throw new ArgumentNullException(nameof(rest)); }

        /// <summary>Operations from contract/openapi.yaml this lane calls (for the progress log and the coverage test).</summary>
        public static readonly string[] ContractCovered =
        {
            "GET /homes", "POST /homes/{homeId}/rent", "POST /homes/{ownerId}/visit", "POST /needs/actions", "GET /me",
            "GET /daily-clue", "POST /daily-clue/answer", "GET /meta/current", "GET /events/active",
            "GET /shop", "POST /shop/purchase", "GET /inventory", "PATCH /characters/me/look",
            "POST /payments/intents", "GET /payments/intents/{id}",
        };

        /// <summary>Routes this lane calls that are proposals, not contract (docs/fe-contract-proposals/city-life-*.md).</summary>
        public static readonly string[] ProposalRoutes =
        {
            "GET /season-pass/me", "GET /homes/me/settings", "PUT /homes/me/settings", "GET /businesses", "POST /businesses",
            "POST /businesses/{id}/collect", "POST /businesses/{id}/sell", "GET /nfts/me", "GET /homes/{ownerId}/gallery", "PUT /homes/me/gallery",
        };

        private Task<JToken> Get(string path, CancellationToken ct) => _rest.Get<JToken>(path, ct);

        private Task<JToken> Write(string method, string path, JObject body, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            return _rest.Send<JToken>(method, path, (body ?? new JObject()).ToString(Formatting.None), true, key, ct);
        }

        // ---------------------------------------------------------------- homes (Ph 4)

        public Task<JToken> Homes(CancellationToken ct) => Get("/homes", ct);

        public async Task<LeaseReceipt> Rent(string homeId, string key, CancellationToken ct) =>
            LeaseReceipt.Parse(await Write("POST", "/homes/" + CityWire.Seg(homeId) + "/rent", null, key, ct));

        /// <summary>Contract `{roomId}`; null when the server answers without a room (backend checkout).</summary>
        public async Task<VisitTicket> Visit(string ownerId, string key, CancellationToken ct) =>
            VisitTicket.Parse(ownerId, await Write("POST", "/homes/" + CityWire.Seg(ownerId) + "/visit", null, key, ct));

        /// <summary>Proposal: `{visitors: open|followers|closed}`.</summary>
        public async Task<VisitorPolicy> HomeSettings(CancellationToken ct) => VisitorPolicies.Parse(await Get("/homes/me/settings", ct));

        public Task<JToken> SaveHomeSettings(VisitorPolicy policy, string key, CancellationToken ct) =>
            Write("PUT", "/homes/me/settings", new JObject { ["visitors"] = VisitorPolicies.Wire(policy) }, key, ct);

        // ---------------------------------------------------------------- needs (Ph 1, used by furniture)

        /// <summary>`{needs, cashCents}`; the generated `Needs` model is filled from the reply.</summary>
        public async Task<(CTLife.Rest.Model.Needs needs, long cashCents)> NeedsAction(string actionId, string key, CancellationToken ct)
        {
            var r = await Write("POST", "/needs/actions", new JObject { ["actionId"] = actionId }, key, ct);
            var n = r?["needs"] as JObject ?? throw new FormatException("Missing needs");
            int Read(string f)
            {
                var v = CityWire.Long(n[f]);
                if (v < 0 || v > 100) throw new FormatException("Need out of range");
                return (int)v;
            }
            var needs = new CTLife.Rest.Model.Needs(Read("energy"), Read("hunger"), Read("fun"), Read("hygiene"), Read("sanity"));
            return (needs, CityWire.Long(r["cashCents"]));
        }

        // ---------------------------------------------------------------- daily clue, meta, events (Ph 4 / Ph 1)

        public async Task<DailyClue> DailyClue(CancellationToken ct) => CTLife.CityLife.DailyClue.Parse(await Get("/daily-clue", ct));

        public async Task<ClueResult> AnswerClue(string normalized, string key, CancellationToken ct) =>
            ClueResult.Parse(await Write("POST", "/daily-clue/answer", DailyClueFlow.Body(normalized), key, ct));

        public async Task<MetaView> Meta(CancellationToken ct) => MetaView.Parse(await Get("/meta/current", ct));

        /// <summary>`ServerEvent[]` read tolerantly (unknown event ids are kept; the generated enum would reject them).</summary>
        public async Task<List<(string id, long? atMs, JObject @params)>> ActiveEvents(CancellationToken ct)
        {
            var list = new List<(string, long?, JObject)>();
            foreach (var e in CityWire.List(await Get("/events/active", ct), "events", "items"))
            {
                var id = CityWire.Str(e?["eventId"]);
                if (CityWire.IsSafeId(id)) list.Add((id, CityWire.TimeMsOrNull(e["at"]), e["params"] as JObject));
            }
            return list;
        }

        // ---------------------------------------------------------------- shop, inventory, look (Ph 4)

        public async Task<List<ShopRow>> Shop(CancellationToken ct) => ShopRow.ParseList(await Get("/shop", ct));

        /// <summary>Contract replies `Inventory`; the backend replies the one purchased row. Both parse to an InventoryView.</summary>
        public async Task<InventoryView> Purchase(string purchaseId, string key, CancellationToken ct) =>
            InventoryView.Parse(await Write("POST", "/shop/purchase", new JObject { ["itemId"] = purchaseId }, key, ct));

        public async Task<InventoryView> Inventory(CancellationToken ct) => InventoryView.Parse(await Get("/inventory", ct));

        /// <summary>`PATCH /characters/me/look` with the exact body the EquipFlow retained. Returns the server's look echo (or null).</summary>
        public async Task<string[]> PatchLook(string body, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            var r = await _rest.Send<JToken>("PATCH", "/characters/me/look", body, true, key, ct);
            return LookDraft.LookFrom(r);
        }

        // ---------------------------------------------------------------- season pass (contract intent + proposal status)

        public async Task<SeasonPassView> SeasonPass(double nowMs, CancellationToken ct) => SeasonPassView.Parse(await Get("/season-pass/me", ct), nowMs);

        public static JObject IntentBody(string kind, string sku, string currency) =>
            new JObject { ["kind"] = kind, ["sku"] = sku, ["currency"] = currency };

        public async Task<PurchaseIntent> CreateIntent(JObject body, string key, CancellationToken ct) =>
            PurchaseIntent.Parse(await Write("POST", "/payments/intents", body, key, ct));

        /// <summary>`{status: pending|confirmed|expired|failed}`. A 410 PAYMENT_EXPIRED is reported as "expired".</summary>
        public async Task<string> IntentStatus(string intentId, CancellationToken ct)
        {
            try
            {
                var r = await Get("/payments/intents/" + CityWire.Seg(intentId), ct);
                return CityWire.Str(r?["status"]) ?? "pending";
            }
            catch (ApiException e) when (e.Is("PAYMENT_EXPIRED")) { return "expired"; }
        }

        // ---------------------------------------------------------------- gateways for proposal surfaces

        public IRestClient Rest => _rest;
    }

    /// <summary>Businesses over the proposal routes (docs/fe-contract-proposals/city-life-businesses.md).</summary>
    public sealed class RestBusinessGateway : IBusinessGateway
    {
        private readonly IRestClient _rest;
        public RestBusinessGateway(IRestClient rest) { _rest = rest; }
        public bool IsStub => false;

        private Task<JToken> Write(string path, JObject body, string key, CancellationToken ct) =>
            _rest.Send<JToken>("POST", path, (body ?? new JObject()).ToString(Formatting.None), true, key, ct);

        public async Task<List<OwnedBusiness>> Owned(CancellationToken ct) => OwnedBusiness.ParseList(await _rest.Get<JToken>("/businesses", ct));

        public async Task<OwnedBusiness> Buy(string kindId, string key, CancellationToken ct) =>
            OwnedBusiness.Parse(await Write("/businesses", new JObject { ["kindId"] = kindId }, key, ct));

        public async Task<CollectReceipt> Collect(string businessId, string key, CancellationToken ct) =>
            CollectReceipt.Parse(await Write("/businesses/" + CityWire.Seg(businessId) + "/collect", null, key, ct));

        public async Task<long?> SellBack(string businessId, string key, CancellationToken ct)
        {
            var r = await Write("/businesses/" + CityWire.Seg(businessId) + "/sell", null, key, ct);
            return CityWire.OptLong(r?["refundCents"]);
        }
    }

    /// <summary>The NFT gallery over the proposal routes (docs/fe-contract-proposals/city-life-nft-gallery.md).</summary>
    public sealed class RestNftGateway : INftGateway
    {
        private readonly IRestClient _rest;
        public RestNftGateway(IRestClient rest) { _rest = rest; }
        public bool IsStub => false;

        public async Task<List<NftItem>> Mine(CancellationToken ct) => NftItem.ParseList(await _rest.Get<JToken>("/nfts/me?limit=100", ct));

        public async Task<(List<FramePlacement> frames, List<NftItem> items)> Gallery(string ownerId, CancellationToken ct)
        {
            var r = await _rest.Get<JToken>("/homes/" + CityWire.Seg(ownerId) + "/gallery", ct);
            List<NftItem> items;
            try { items = NftItem.ParseList(r?["items"]); } catch (FormatException) { items = new List<NftItem>(); }
            return (FramePlacement.ParseList(r), items);
        }

        public Task SaveGallery(List<FramePlacement> frames, string key, CancellationToken ct) =>
            _rest.Send<JToken>("PUT", "/homes/me/gallery", FramePlacement.ToJson(frames).ToString(Formatting.None), true, key, ct);
    }
}
