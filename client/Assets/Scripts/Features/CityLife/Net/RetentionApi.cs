using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    /// <summary>
    /// gm streak, daily chest, quests and push routes (backend PR #9, contract/CHANGELOG.md 2026-10-23). Every write takes the
    /// caller's retained Idempotency-Key; the server reads no time or amount from the client.
    /// </summary>
    public sealed class RetentionApi
    {
        private readonly IRestClient _rest;
        public RetentionApi(IRestClient rest) { _rest = rest ?? throw new ArgumentNullException(nameof(rest)); }

        public static readonly string[] Routes =
        {
            "GET /daily/status", "POST /daily/claim", "POST /daily/freeze", "GET /quests", "POST /quests/{id}/claim",
            "POST /push/register", "DELETE /push/register", "GET /push/prefs", "PUT /push/prefs",
        };

        private Task<JToken> Write(string method, string path, JObject body, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            return _rest.Send<JToken>(method, path, (body ?? new JObject()).ToString(Formatting.None), true, key, ct);
        }

        public async Task<DailyStatus> Status(CancellationToken ct) => DailyStatus.Parse(await _rest.Get<JToken>("/daily/status", ct));
        public async Task<DailyClaim> Claim(string key, CancellationToken ct) => DailyClaim.Parse(await Write("POST", "/daily/claim", null, key, ct));
        public async Task<DailyStatus> BuyFreeze(string key, CancellationToken ct) => DailyStatus.Parse(await Write("POST", "/daily/freeze", null, key, ct));

        public async Task<QuestBoard> Quests(CancellationToken ct) => QuestBoard.Parse(await _rest.Get<JToken>("/quests", ct));

        /// <summary>Pays a finished quest; returns (alreadyClaimed, cash, xp).</summary>
        public async Task<(bool already, long cashCents, int xp)> ClaimQuest(string questId, string key, CancellationToken ct)
        {
            var r = await Write("POST", "/quests/" + CityWire.Seg(questId) + "/claim", null, key, ct);
            return (CityWire.BoolOr(r?["alreadyClaimed"], false), CityWire.LongOr(r?["reward"]?["cashCents"], 0), CityWire.IntOr(r?["reward"]?["seasonXp"], 0));
        }

        public Task<JToken> RegisterPush(string fcmToken, string key, CancellationToken ct) =>
            Write("POST", "/push/register", new JObject { ["platform"] = "android", ["fcmToken"] = fcmToken }, key, ct);

        /// <summary>DELETE /push/register: this token stops receiving the signed-in account's pushes (sign-out).</summary>
        public Task<JToken> UnregisterPush(string fcmToken, string key, CancellationToken ct) =>
            Write("DELETE", "/push/register", new JObject { ["fcmToken"] = fcmToken }, key, ct);

        public Task<JToken> Prefs(CancellationToken ct) => _rest.Get<JToken>("/push/prefs", ct);

        /// <summary>PUT /push/prefs: the server's current prefs with the device's UTC offset (minutes east of UTC) set.</summary>
        public Task<JToken> PutPrefs(JObject prefs, string key, CancellationToken ct) => Write("PUT", "/push/prefs", prefs, key, ct);
    }
}
