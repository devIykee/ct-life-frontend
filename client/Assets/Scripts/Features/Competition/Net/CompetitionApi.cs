using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.Competition
{
    /// <summary>
    /// Thin HTTP wrapper for the Phase 6 routes in docs/fe-contract-proposals/phase4-6/openapi-additions.yaml (plus rc1
    /// `POST /me/tribe`, `GET /leaderboards/{boardId}` and `POST /reports`). Transport policy (auth refresh, version
    /// headers, 429/503, network retries with the same Idempotency-Key) is RestClient's. Every write takes the caller's
    /// retained Idempotency-Key. Outcomes are never decided here: beef settlement, votes, invite acceptance, gates and LLM
    /// moderation are the server's.
    /// </summary>
    public sealed class CompetitionApi
    {
        private readonly IRestClient _rest;

        public CompetitionApi(IRestClient rest) { _rest = rest ?? throw new ArgumentNullException(nameof(rest)); }

        /// <summary>Operations from the proposal this lane calls, for the progress log and the coverage check.</summary>
        public static readonly string[] ProposalRoutes =
        {
            "GET /tribes", "GET /districts", "GET /districts/{id}/standings", "GET /leagues", "GET /me/league", "GET /me/season-results",
            "GET /beef", "POST /beef", "GET /beef/{id}", "POST /beef/{id}/accept", "POST /beef/{id}/decline", "POST /beef/{id}/cancel",
            "POST /beef/{id}/votes", "GET /beef/elo", "GET /airdrop/farm", "POST /airdrop/claim", "GET /airdrop/claims", "GET /me/sybil-status",
            "POST /me/sybil-status/bail", "GET /sybil-barn", "GET /venues/{venueId}/access", "GET /me/cabal", "GET /cabal/invites",
            "POST /cabal/invites", "POST /cabal/invites/{id}/accept", "POST /cabal/invites/{id}/revoke", "POST /llm/chat", "GET /llm/quota",
            "GET /x/link", "GET /x/link/start", "DELETE /x/link",
        };

        /// <summary>rc1 operations this lane calls.</summary>
        public static readonly string[] ContractCovered = { "POST /me/tribe", "GET /leaderboards/{boardId}", "POST /reports", "GET /users/{id}" };

        private Task<JToken> Get(string path, CancellationToken ct) => _rest.Get<JToken>(path, ct);

        // New, explicit backend opt-in; absent on eefa667. Read the actual network payload,
        // not the merged ConfigStore snapshot, before these otherwise destructive writes.
        private async Task RequireCapability(string capability, CancellationToken ct)
        {
            var raw = await Get("/config", ct);
            var caps = (raw as JObject)?["competitionCapabilities"] as JObject;
            if (caps?["version"]?.Type != JTokenType.Integer || (int)caps["version"] != 1 ||
                caps[capability]?.Type != JTokenType.Boolean || !(bool)caps[capability])
                throw new ApiException(501, "UNAVAILABLE", "This server does not support this action yet.");
        }

        private Task<JToken> Write(string method, string path, JObject body, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            var json = body == null && method == "DELETE" ? null : (body ?? new JObject()).ToString(Formatting.None);
            return _rest.Send<JToken>(method, path, json, true, key, ct);
        }

        /// <summary>rc1 `GET /users/{id}`: only the alias is read.</summary>
        public async Task<string> Alias(string userId, CancellationToken ct)
        {
            var r = await Get("/users/" + CompWire.Seg(userId), ct);
            return CompWire.Plain(r?["alias"], 32);
        }

        // ---------------------------------------------------------------- tribes, districts (#38)

        public async Task<List<TribeInfo>> Tribes(CancellationToken ct) => TribeInfo.ParseList(await Get("/tribes", ct));

        public async Task<List<DistrictInfo>> Districts(CancellationToken ct) => DistrictInfo.ParseList(await Get("/districts", ct));

        public async Task<DistrictStandings> Standings(string districtId, CancellationToken ct) =>
            DistrictStandings.Parse(await Get("/districts/" + CompWire.Seg(districtId) + "/standings", ct));

        /// <summary>rc1 `POST /me/tribe` (once per season; the server refuses with FORBIDDEN or RATE_LIMITED).</summary>
        public Task<JToken> ChangeTribe(string tribeId, string key, CancellationToken ct) =>
            Write("POST", "/me/tribe", new JObject { ["tribeId"] = tribeId }, key, ct);

        // ---------------------------------------------------------------- leagues (#39)

        public async Task<List<LeagueInfo>> Leagues(CancellationToken ct) => LeagueInfo.ParseList(await Get("/leagues", ct));

        public async Task<LeagueMembership> MyLeague(CancellationToken ct) => LeagueMembership.Parse(await Get("/me/league", ct));

        /// <summary>The latest finished season when `seasonId` is null.</summary>
        public async Task<SeasonResult> SeasonResults(string seasonId, CancellationToken ct) =>
            SeasonResult.Parse(await Get("/me/season-results" + (seasonId == null ? "" : "?seasonId=" + Uri.EscapeDataString(seasonId)), ct));

        /// <summary>rc1 board. `scope` is global | friends | league.</summary>
        public async Task<BoardPage> Board(string boardId, string scope, CancellationToken ct) =>
            BoardPage.Parse(await Get("/leaderboards/" + CompWire.Seg(boardId) + (string.IsNullOrEmpty(scope) ? "" : "?scope=" + Uri.EscapeDataString(scope)), ct));

        // ---------------------------------------------------------------- beef (#40)

        /// <summary>`scope` is mine | open | arena per the proposal; null lets the server pick. `status` filters, null = all.</summary>
        public async Task<BeefPage> BeefList(string scope, string status, string cursor, CancellationToken ct)
        {
            var q = new List<string>();
            if (!string.IsNullOrEmpty(scope)) q.Add("scope=" + Uri.EscapeDataString(scope));
            if (!string.IsNullOrEmpty(status)) q.Add("status=" + Uri.EscapeDataString(status));
            if (!string.IsNullOrEmpty(cursor)) q.Add("cursor=" + Uri.EscapeDataString(cursor));
            return BeefPage.Parse(await Get("/beef" + (q.Count == 0 ? "" : "?" + string.Join("&", q)), ct));
        }

        public async Task<BeefMatch> Beef(string matchId, CancellationToken ct) => BeefMatch.Parse(await Get("/beef/" + CompWire.Seg(matchId), ct));

        public static JObject BeefOpenBody(long stakeCents, string topicId, string invitedUserId)
        {
            var b = new JObject { ["stakeCents"] = stakeCents, ["topicId"] = topicId };
            if (!string.IsNullOrEmpty(invitedUserId)) b["invitedUserId"] = invitedUserId;
            return b;
        }

        public async Task<BeefMatch> OpenBeef(JObject body, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            await RequireCapability("beefChallenges", ct);
            var match = BeefMatch.Parse(await Write("POST", "/beef", body, key, ct));
            if (match.TopicId != CompWire.Str(body?["topicId"]) ||
                match.InvitedUserId != CompWire.Str(body?["invitedUserId"]) ||
                match.StakeCents != CompWire.Long(body?["stakeCents"]))
                throw new FormatException("The server did not preserve the challenge terms");
            return match;
        }

        public async Task<BeefMatch> AcceptBeef(string id, string key, CancellationToken ct) =>
            BeefMatch.Parse(await Write("POST", "/beef/" + CompWire.Seg(id) + "/accept", null, key, ct));

        public async Task<BeefMatch> DeclineBeef(string id, string key, CancellationToken ct) =>
            BeefMatch.Parse(await Write("POST", "/beef/" + CompWire.Seg(id) + "/decline", null, key, ct));

        public async Task<BeefMatch> CancelBeef(string id, string key, CancellationToken ct) =>
            BeefMatch.Parse(await Write("POST", "/beef/" + CompWire.Seg(id) + "/cancel", null, key, ct));

        /// <summary>Casts a vote for one participant. The server checks eligibility and refuses participants and jailed players.</summary>
        public Task<JToken> Vote(string id, string votedUserId, string key, CancellationToken ct) =>
            Write("POST", "/beef/" + CompWire.Seg(id) + "/votes", new JObject { ["votedUserId"] = votedUserId }, key, ct);

        public async Task<BeefElo> Elo(string userIdOrNull, CancellationToken ct) =>
            BeefElo.Parse(await Get("/beef/elo" + (userIdOrNull == null ? "" : "?userId=" + Uri.EscapeDataString(userIdOrNull)), ct));

        // ---------------------------------------------------------------- Airdrop Farm, Sybil Barn (#41)

        public async Task<FarmStatus> Farm(CancellationToken ct) => FarmStatus.Parse(await Get("/airdrop/farm", ct));

        public async Task<AirdropClaim> Claim(string key, CancellationToken ct) =>
            AirdropClaim.Parse(await Write("POST", "/airdrop/claim", null, key, ct));

        public async Task<List<AirdropClaim>> Claims(CancellationToken ct) => AirdropClaim.ParseList(await Get("/airdrop/claims", ct));

        public async Task<SybilStatus> Sybil(CancellationToken ct) => SybilStatus.Parse(await Get("/me/sybil-status", ct));

        public async Task<SybilStatus> Bail(string key, CancellationToken ct) =>
            SybilStatus.Parse(await Write("POST", "/me/sybil-status/bail", null, key, ct));

        public async Task<SybilBarn> Barn(CancellationToken ct) => SybilBarn.Parse(await Get("/sybil-barn", ct));

        // ---------------------------------------------------------------- Dubai Marina, Cabal Lounge (#42)

        public async Task<VenueAccess> Access(string venueId, CancellationToken ct) =>
            VenueAccess.Parse(await Get("/venues/" + CompWire.Seg(venueId) + "/access", ct));

        public async Task<CabalMembership> MyCabal(CancellationToken ct) => CabalMembership.Parse(await Get("/me/cabal", ct));

        /// <summary>`role` is host | guest; the server returns only the caller's own invites and never a bearer code.</summary>
        public async Task<List<CabalInvite>> CabalInvites(string role, string userId, CancellationToken ct)
        {
            if (role != "host" && role != "guest") throw new ArgumentException("Expected host or guest", nameof(role));
            if (!CompWire.IsSafeId(userId)) throw new ArgumentException("Expected the signed-in user", nameof(userId));
            // The backend route accepts hostId/guestId, not the proposal's role filter.
            return CabalInvite.ParseList(await Get("/cabal/invites?" + role + "Id=" + Uri.EscapeDataString(userId), ct));
        }

        public async Task<CabalInvite> Invite(string guestUserId, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            await RequireCapability("targetedCabalInvites", ct);
            var invite = CabalInvite.Parse(await Write("POST", "/cabal/invites", new JObject { ["guestUserId"] = guestUserId }, key, ct));
            if (invite.GuestUserId != guestUserId)
                throw new FormatException("The server did not address the invite to the selected player");
            return invite;
        }

        public async Task<CabalInvite> AcceptInvite(string id, string key, CancellationToken ct) =>
            CabalInvite.Parse(await Write("POST", "/cabal/invites/" + CompWire.Seg(id) + "/accept", null, key, ct));

        public async Task<CabalInvite> RevokeInvite(string id, string key, CancellationToken ct) =>
            CabalInvite.Parse(await Write("POST", "/cabal/invites/" + CompWire.Seg(id) + "/revoke", null, key, ct));

        // ---------------------------------------------------------------- LLM chat (#43)

        public async Task<LlmReply> Chat(string archetype, string message, string npcId, string key, CancellationToken ct)
        {
            var body = new JObject { ["archetype"] = archetype, ["message"] = message };
            if (!string.IsNullOrEmpty(npcId) && CompWire.IsSafeId(npcId)) body["npcId"] = npcId;
            return LlmReply.Parse(await Write("POST", "/llm/chat", body, key, ct));
        }

        public async Task<LlmQuota> Quota(CancellationToken ct) => LlmQuota.Parse(await Get("/llm/quota", ct));

        /// <summary>rc1 `POST /reports` with the proposed `npc_reply` target type.</summary>
        public Task<JToken> ReportReply(string replyId, string reasonId, string key, CancellationToken ct) =>
            Write("POST", "/reports", new JObject { ["targetType"] = "npc_reply", ["targetId"] = replyId, ["reasonId"] = reasonId }, key, ct);

        // ---------------------------------------------------------------- verified X link (#44)

        /// <summary>Null when not linked.</summary>
        public async Task<XLinkInfo> XLink(CancellationToken ct) => XLinkInfo.ParseStatus(await Get("/x/link", ct));

        /// <summary>The https authorize URL to open in the system browser. Refused unless <see cref="XLinkRules.IsSafeAuthUrl"/>.</summary>
        public async Task<string> XLinkStart(CancellationToken ct)
        {
            var r = await Get("/x/link/start", ct);
            var url = CompWire.Str(r?["authorizationUrl"]);
            if (!XLinkRules.IsSafeAuthUrl(url)) throw new FormatException("Unsafe authorization URL");
            return url;
        }

        public Task<JToken> XUnlink(string key, CancellationToken ct) => Write("DELETE", "/x/link", null, key, ct);
    }
}
