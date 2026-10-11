using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.Ventures
{
    /// <summary>
    /// Player projects, hiring, launch, HQ and VC Row over REST (docs/fe-contract-proposals/phase4-6). Every write takes the
    /// caller's retained Idempotency-Key. Routes the server does not have answer 404/501 and surface as UNAVAILABLE.
    /// </summary>
    public sealed class ProjectsApi
    {
        public const int PageSize = 20;
        private readonly IRestClient _rest;

        public ProjectsApi(IRestClient rest) { _rest = rest ?? throw new ArgumentNullException(nameof(rest)); }

        public static readonly string[] Routes =
        {
            "GET /projects", "POST /projects", "GET /projects/{id}", "POST /projects/{id}/fund", "POST /projects/{id}/offers",
            "GET /me/project-offers", "POST /project-offers/{offerId}/accept", "POST /project-offers/{offerId}/decline",
            "DELETE /projects/{id}/members/{userId}", "GET /projects/{id}/launch-quote", "POST /projects/{id}/launch",
            "POST /projects/{id}/hq/visit", "GET /projects/{id}/vc-offers", "POST /vc-offers/{offerId}/accept",
            "POST /vc-offers/{offerId}/decline", "GET /vc-row/deals",
        };

        private Task<JToken> Get(string path, CancellationToken ct) => _rest.Get<JToken>(path, ct);

        private Task<JToken> Write(string method, string path, JObject body, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            return _rest.Send<JToken>(method, path, (body ?? new JObject()).ToString(Formatting.None), true, key, ct);
        }

        private static string Query(string cursor) =>
            "limit=" + PageSize + (string.IsNullOrEmpty(cursor) ? "" : "&cursor=" + Uri.EscapeDataString(cursor));

        public async Task<Page<ProjectSummary>> List(string scope, string cursor, CancellationToken ct)
        {
            if (Array.IndexOf(ProjectKinds.Scopes, scope) < 0) throw new ArgumentException("Unknown scope", nameof(scope));
            return ProjectParse.SummaryPage(await Get("/projects?scope=" + scope + "&" + Query(cursor), ct));
        }

        public async Task<ProjectDetail> Detail(string id, CancellationToken ct) => ProjectParse.Detail(await Get("/projects/" + VWire.Seg(id), ct));

        public static JObject CreateBody(string kind, string name, string ticker)
        {
            var b = new JObject { ["kind"] = kind, ["name"] = ProjectRules.CleanName(name) };
            if (!string.IsNullOrWhiteSpace(ticker)) b["ticker"] = ProjectRules.CleanTicker(ticker);
            return b;
        }

        public async Task<ProjectSummary> Create(JObject body, string key, CancellationToken ct) =>
            ProjectParse.Summary(await Write("POST", "/projects", body, key, ct));

        public static JObject FundBody(long cents) => new JObject { ["cashCents"] = cents };

        /// <summary>The reply shape of `fund` is not in the proposal's description; the updated project is read when present, else null.</summary>
        public async Task<ProjectSummary> Fund(string id, JObject body, string key, CancellationToken ct)
        {
            var r = await Write("POST", "/projects/" + VWire.Seg(id) + "/fund", body, key, ct);
            try { return ProjectParse.Summary(r?["project"] ?? r); }
            catch (FormatException) { return null; }
        }

        public static JObject OfferBody(string userId, string role, long salaryCentsPerWeek) =>
            new JObject { ["userId"] = userId, ["role"] = role, ["salaryCentsPerWeek"] = salaryCentsPerWeek };

        public async Task<HireOffer> Offer(string id, JObject body, string key, CancellationToken ct) =>
            ProjectParse.Offer(await Write("POST", "/projects/" + VWire.Seg(id) + "/offers", body, key, ct));

        public async Task<Page<HireOffer>> MyOffers(string cursor, CancellationToken ct) =>
            ProjectParse.OfferPage(await Get("/me/project-offers?" + Query(cursor), ct));

        public async Task<HireOffer> AcceptOffer(string offerId, string key, CancellationToken ct) =>
            ProjectParse.Offer(await Write("POST", "/project-offers/" + VWire.Seg(offerId) + "/accept", null, key, ct));

        public async Task<HireOffer> DeclineOffer(string offerId, string key, CancellationToken ct) =>
            ProjectParse.Offer(await Write("POST", "/project-offers/" + VWire.Seg(offerId) + "/decline", null, key, ct));

        public Task Fire(string id, string userId, string key, CancellationToken ct) =>
            Write("DELETE", "/projects/" + VWire.Seg(id) + "/members/" + VWire.Seg(userId), null, key, ct);

        public async Task<LaunchQuote> Quote(string id, CancellationToken ct) => ProjectParse.Quote(await Get("/projects/" + VWire.Seg(id) + "/launch-quote", ct));

        public static JObject LaunchBody(string ticker, long quotedCostCents) =>
            new JObject { ["ticker"] = ProjectRules.CleanTicker(ticker), ["quotedCostCents"] = quotedCostCents };

        public async Task<ProjectSummary> Launch(string id, JObject body, string key, CancellationToken ct) =>
            ProjectParse.Summary(await Write("POST", "/projects/" + VWire.Seg(id) + "/launch", body, key, ct));

        public async Task<RoomTicket> HqVisit(string id, string key, CancellationToken ct) =>
            ProjectParse.Ticket(await Write("POST", "/projects/" + VWire.Seg(id) + "/hq/visit", null, key, ct));

        public async Task<List<VcOffer>> VcOffers(string id, CancellationToken ct) =>
            ProjectParse.VcOffers(await Get("/projects/" + VWire.Seg(id) + "/vc-offers", ct), out _);

        public async Task<VcOffer> AcceptVc(string offerId, string key, CancellationToken ct) =>
            ProjectParse.VcTerm(await Write("POST", "/vc-offers/" + VWire.Seg(offerId) + "/accept", null, key, ct));

        public async Task<VcOffer> DeclineVc(string offerId, string key, CancellationToken ct) =>
            ProjectParse.VcTerm(await Write("POST", "/vc-offers/" + VWire.Seg(offerId) + "/decline", null, key, ct));

        public async Task<Page<VcDeal>> VcDeals(string cursor, CancellationToken ct) =>
            ProjectParse.VcDeals(await Get("/vc-row/deals?" + Query(cursor), ct));
    }
}
