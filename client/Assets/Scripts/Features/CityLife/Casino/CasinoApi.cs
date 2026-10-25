using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife.Casino
{
    /// <summary>
    /// The Rug Room routes (backend PR #12, docs/CASINO.md). Every bet carries the caller's retained Idempotency-Key, so a retry
    /// after a lost reply replays the first answer and charges once. The client sends no seed, outcome or multiplier.
    /// </summary>
    public sealed class CasinoApi
    {
        private readonly IRestClient _rest;
        public CasinoApi(IRestClient rest) { _rest = rest ?? throw new ArgumentNullException(nameof(rest)); }

        public static readonly string[] Routes =
        {
            "GET /casino/state", "POST /casino/slots/spin", "POST /casino/crash/bet", "POST /casino/crash/cashout",
            "POST /casino/coinflip/bet", "POST /casino/coinflip/challenges", "GET /casino/coinflip/challenges",
            "POST /casino/coinflip/challenges/{id}/accept", "POST /casino/coinflip/challenges/{id}/decline",
            "POST /casino/coinflip/challenges/{id}/cancel", "GET /casino/rounds?mine", "GET /casino/rounds/{id}",
            "POST /casino/self-exclude", "GET /dms",
        };

        private Task<JToken> Write(string path, JObject body, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            return _rest.Send<JToken>("POST", path, (body ?? new JObject()).ToString(Formatting.None), true, key, ct);
        }

        public async Task<CasinoState> State(CancellationToken ct) => CasinoState.Parse(await _rest.Get<JToken>("/casino/state", ct));

        public async Task<CasinoBet> Spin(long stakeCents, string key, CancellationToken ct) =>
            CasinoBet.Parse(await Write("/casino/slots/spin", new JObject { ["stakeCents"] = stakeCents }, key, ct));

        public async Task<CasinoBet> CrashBet(long stakeCents, double? autoCashout, string key, CancellationToken ct)
        {
            var body = new JObject { ["stakeCents"] = stakeCents };
            if (autoCashout.HasValue) body["autoCashout"] = Math.Round(autoCashout.Value, 2);
            return CasinoBet.Parse(await Write("/casino/crash/bet", body, key, ct));
        }

        public async Task<CasinoBet> CrashCashout(string roundId, string key, CancellationToken ct) =>
            CasinoBet.Parse(await Write("/casino/crash/cashout", new JObject { ["roundId"] = roundId }, key, ct));

        public async Task<CasinoBet> Flip(long stakeCents, string side, string key, CancellationToken ct) =>
            CasinoBet.Parse(await Write("/casino/coinflip/bet", new JObject { ["stakeCents"] = stakeCents, ["side"] = side }, key, ct));

        public async Task<CasinoChallengeReply> Challenge(string opponentUserId, long stakeCents, string side, string key, CancellationToken ct) =>
            CasinoChallengeReply.Parse(await Write("/casino/coinflip/challenges",
                new JObject { ["opponentUserId"] = opponentUserId, ["stakeCents"] = stakeCents, ["side"] = side }, key, ct));

        public async Task<(CasinoMeta meta, List<CasinoChallenge> list)> Challenges(CancellationToken ct)
        {
            var t = await _rest.Get<JToken>("/casino/coinflip/challenges", ct);
            return (CasinoMeta.ParseMeta(t), CasinoChallenge.ParseList(t));
        }

        /// <summary>accept | decline | cancel.</summary>
        public async Task<CasinoChallengeReply> Answer(string challengeId, string verb, string key, CancellationToken ct) =>
            CasinoChallengeReply.Parse(await Write("/casino/coinflip/challenges/" + CityWire.Seg(challengeId) + "/" + verb, null, key, ct));

        public async Task<List<CasinoRound>> Rounds(int limit, CancellationToken ct) =>
            CasinoRound.ParseList(await _rest.Get<JToken>("/casino/rounds?mine=true&limit=" + limit.ToString(CultureInfo.InvariantCulture), ct));

        public async Task<CasinoRound> Round(string roundId, CancellationToken ct) =>
            CasinoRound.Parse((await _rest.Get<JToken>("/casino/rounds/" + CityWire.Seg(roundId), ct))?["round"]);

        public async Task<long?> SelfExclude(int days, string key, CancellationToken ct) =>
            CityWire.TimeMsOrNull((await Write("/casino/self-exclude", new JObject { ["days"] = days }, key, ct))?["selfExcludedUntil"]);

        /// <summary>People to challenge: the DM inbox's peers (there is no friends list in the contract).</summary>
        public async Task<List<CasinoPeer>> Peers(CancellationToken ct)
        {
            var list = new List<CasinoPeer>();
            foreach (var row in CityWire.List(await _rest.Get<JToken>("/dms", ct), "threads"))
            {
                var id = CityWire.Str(row?["userId"]) ?? CityWire.Str(row?["peerUserId"]);
                if (string.IsNullOrEmpty(id) || !CityWire.IsSafeId(id)) continue;
                list.Add(new CasinoPeer { UserId = id, Alias = CityWire.Plain(row["alias"], 24) });
            }
            return list;
        }
    }
}
