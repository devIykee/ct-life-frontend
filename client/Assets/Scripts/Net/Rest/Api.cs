using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Rest.Model;
using Newtonsoft.Json.Linq;

namespace CTLife.Net
{
    /// <summary>
    /// Typed endpoints from the backend developer's contract (contract/openapi.yaml, 1.0.0-rc1;
    /// paths are relative to the `servers` base, which already includes /v1), grouped by area. Phase 1 endpoints first; later phases are
    /// added as their screens are built. Bodies are the generated CTLife.Rest.Model types where they exist.
    /// </summary>
    public sealed class Api
    {
        private readonly IRestClient _rest;
        private readonly ConfigStore _config;

        public Api(IRestClient rest, ConfigStore config)
        {
            _rest = rest;
            _config = config;
        }

        public IRestClient Rest => _rest;
        public PerpsApi Perps => new PerpsApi(_rest);
        public SpacesApi Spaces => new SpacesApi(_rest);

        // ---------------------------------------------------------------- config (Ph 1)

        /// <summary>GET /v1/config with If-None-Match. Returns true when a new payload was applied.</summary>
        public async Task<bool> RefreshConfig(CancellationToken ct = default)
        {
            // A cached ETag must not produce a cold-start 304 before raw server item IDs have been confirmed.
            var res = await _rest.SendRaw("GET", "/config", null, false, null,
                _config.HasNetworkValidation ? _config.ETag : null, ct);
            if (res.status == 304) return false;
            _config.Apply(res.body, res.etag);
            if (Semver.IsOutdated(_config.MinClientVersion))
                Bus.Publish(new UpdateRequiredSignal { minClientVersion = _config.MinClientVersion });
            return true;
        }

        // ---------------------------------------------------------------- player and life (Ph 1)

        public Task<Me> GetMe(CancellationToken ct = default) => _rest.Get<Me>("/me", ct);

        /// <summary>
        /// Raw body on purpose: the generated CreateCharacterRequest maps `ageConfirmed: const true` to an enum
        /// that would serialize as a string (openapi-generator 7.25.0 bug; see client/Tools~/patch_rest_codegen.py).
        /// </summary>
        public Task<Character> CreateCharacter(string alias, string[] look, IList<string> traitIds, string dreamId, string tribeId,
            bool ageConfirmed, CancellationToken ct = default)
        {
            var body = new JObject
            {
                ["alias"] = alias,
                ["look"] = new JObject
                {
                    ["skinTone"] = look[0], ["headId"] = look[1], ["hairId"] = look[2], ["topId"] = look[3],
                    ["bottomId"] = look[4], ["shoesId"] = look[5], ["accId"] = look[6],
                },
                ["traitIds"] = new JArray(traitIds),
                ["dreamId"] = dreamId,
                ["tribeId"] = tribeId,
                ["ageConfirmed"] = ageConfirmed,
            };
            return _rest.Post<Character>("/characters", body.ToString(), ct: ct);
        }

        public Task<PostLotteryRoll200Response> RollLottery(CancellationToken ct = default) =>
            _rest.Post<PostLotteryRoll200Response>("/lottery/roll", null, ct: ct);

        public Task<PostNeedsAction200Response> NeedsAction(string actionId, CancellationToken ct = default) =>
            _rest.Post<PostNeedsAction200Response>("/needs/actions", new JObject { ["actionId"] = actionId }.ToString(), ct: ct);

        // ---------------------------------------------------------------- work (Ph 1)

        /// <summary>`{id, name, level, xp, active}` per career; pay and level tables come from config `careers`.</summary>
        public Task<List<GetCareers200ResponseInner>> Careers(CancellationToken ct = default) =>
            _rest.Get<List<GetCareers200ResponseInner>>("/careers", ct);

        public Task<Job> ApplyCareer(string careerId, CancellationToken ct = default) =>
            _rest.Post<Job>($"/careers/{careerId}/apply", null, ct: ct);

        public Task<Shift> StartShift(string careerId, CancellationToken ct = default) =>
            _rest.Post<Shift>("/shifts", new JObject { ["careerId"] = careerId }.ToString(), ct: ct);

        public Task<PostShiftsShiftIdComplete200Response> CompleteShift(string shiftId, List<PostShiftsShiftIdCompleteRequestResultsInner> results,
            CancellationToken ct = default) =>
            _rest.Post<PostShiftsShiftIdComplete200Response>($"/shifts/{shiftId}/complete", new PostShiftsShiftIdCompleteRequest(results), ct: ct);

        // ---------------------------------------------------------------- social and boards (Ph 1)

        public Task<PublicProfile> GetUser(string userId, CancellationToken ct = default) =>
            _rest.Get<PublicProfile>($"/users/{userId}", ct);

        public Task Block(string userId, CancellationToken ct = default) => _rest.Post<NoContent>($"/blocks/{userId}", null, ct: ct);

        public Task Unblock(string userId, CancellationToken ct = default) => _rest.Delete<NoContent>($"/blocks/{userId}", ct);

        /// <summary>targetType: user | message | space | call | post | ad. Chat targetId is the `said` messageId.</summary>
        public Task Report(string targetType, string targetId, string reasonId, string note = null, CancellationToken ct = default)
        {
            var body = new JObject { ["targetType"] = targetType, ["targetId"] = targetId, ["reasonId"] = reasonId };
            if (!string.IsNullOrEmpty(note)) body["note"] = note;
            return _rest.Post<NoContent>("/reports", body.ToString(), ct: ct);
        }

        public Task<GetLeaderboard200Response> Leaderboard(string boardId, string scope = "global", string cursor = null, CancellationToken ct = default)
        {
            var q = $"/leaderboards/{boardId}?scope={scope}";
            if (!string.IsNullOrEmpty(cursor)) q += "&cursor=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(cursor);
            return _rest.Get<GetLeaderboard200Response>(q, ct);
        }

        // ---------------------------------------------------------------- live-ops (Ph 1)

        public Task<List<ServerEvent>> ActiveEvents(CancellationToken ct = default) => _rest.Get<List<ServerEvent>>("/events/active", ct);

        // ---------------------------------------------------------------- voice (Ph 3, used by the engine spike)

        /// <summary>kind: venue | space | call; id: venue instanceId, spaceId or callId.</summary>
        public Task<PostRtcToken200Response> RtcToken(string kind, string id, CancellationToken ct = default) =>
            _rest.Post<PostRtcToken200Response>("/rtc/token", new JObject { ["kind"] = kind, ["id"] = id }.ToString(), ct: ct);

        // ---------------------------------------------------------------- markets (Ph 2)

        public Task<JToken> Coins(string stage, CancellationToken ct = default) => _rest.Get<JToken>($"/market/coins?stage={stage}", ct);

        public Task<Trade> Trade(string coinId, string side, long? cashCents, long? units, int maxSlippageBps, CancellationToken ct = default)
        {
            var body = new JObject { ["coinId"] = coinId, ["side"] = side, ["maxSlippageBps"] = maxSlippageBps };
            if (cashCents.HasValue) body["cashCents"] = cashCents.Value;
            if (units.HasValue) body["units"] = units.Value;
            return _rest.Post<Trade>("/market/trades", body.ToString(), ct: ct);
        }

        public Task<JToken> Tombstones(int limit = 50, CancellationToken ct = default) => _rest.Get<JToken>($"/rekt/tombstones?limit={limit}", ct);
    }
}
