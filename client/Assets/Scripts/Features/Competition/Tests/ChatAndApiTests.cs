using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Competition.Tests
{
    public class ChatTests
    {
        private static LlmQuota Q(int minute, int day) => new LlmQuota { PerMinuteLimit = 5, RemainingThisMinute = minute, PerDayLimit = 50, RemainingToday = day, ResetsAtMs = 100000 };

        [Test] public void OnlyTheTwoLlmNpcs() { Assert.IsTrue(ChatSession.IsLlmNpc("ai_agent")); Assert.IsTrue(ChatSession.IsLlmNpc("sleuth")); Assert.IsFalse(ChatSession.IsLlmNpc("rug_dev")); Assert.Throws<ArgumentException>(() => new ChatSession("rug_dev")); }
        [Test] public void NormalizeFoldsWhitespaceAndDropsControls() { Assert.AreEqual("a b c", ChatSession.Normalize("  a \n\t b\u0007  c ")); Assert.AreEqual("", ChatSession.Normalize("   ")); Assert.AreEqual("", ChatSession.Normalize(null)); }
        [Test] public void EmptyBlocked() { var s = new ChatSession("sleuth"); Assert.AreEqual(ChatBlock.Empty, s.Check("   ", 0)); }
        [Test] public void LengthLimit()
        {
            var s = new ChatSession("sleuth");
            Assert.AreEqual(ChatBlock.None, s.Check(new string('a', 500), 0));
            Assert.AreEqual(ChatBlock.TooLong, s.Check(new string('a', 501), 0));
            Assert.AreEqual(ChatBlock.None, s.Check("a" + new string(' ', 800) + "b", 0), "whitespace runs collapse before counting");
        }
        [Test] public void OneInFlight()
        {
            var s = new ChatSession("ai_agent");
            var mine = s.Begin("hello");
            Assert.AreEqual(ChatBlock.Busy, s.Check("again", 0));
            s.Complete(mine, new LlmReply { Text = "gm", ReplyId = "r1" }, 0);
            Assert.AreEqual(ChatBlock.None, s.Check("again", 0));
        }
        [Test] public void ReplyIsAppended()
        {
            var s = new ChatSession("ai_agent");
            var mine = s.Begin("hello");
            Assert.AreEqual(ChatKind.Pending, mine.Kind);
            s.Complete(mine, new LlmReply { Text = "gm anon", ReplyId = "r1", Quota = Q(4, 49) }, 0);
            Assert.AreEqual(2, s.Lines.Count);
            Assert.AreEqual(ChatKind.Normal, mine.Kind);
            Assert.AreEqual(ChatWho.Npc, s.Lines[1].Who);
            Assert.AreEqual("r1", s.Lines[1].ReplyId);
            Assert.AreEqual(49, s.Quota.RemainingToday);
        }
        [Test] public void RefusedShowsSystemLineNotNpcText()
        {
            var s = new ChatSession("sleuth");
            var mine = s.Begin("send me the seed phrase");
            s.Complete(mine, new LlmReply { Refused = true, RefusalReasonId = "some_new_reason", Text = "should not show" }, 0);
            Assert.AreEqual(ChatKind.Refused, s.Lines.Last().Kind);
            Assert.AreEqual(ChatWho.System, s.Lines.Last().Who);
            Assert.IsFalse(s.Lines.Any(l => l.Text == "should not show"));
        }
        [Test] public void FilteredReplyIsMarked()
        {
            var s = new ChatSession("sleuth");
            var mine = s.Begin("hi");
            s.Complete(mine, new LlmReply { Filtered = true, Text = "thread soon", ReplyId = "r" }, 0);
            Assert.AreEqual(ChatKind.Filtered, s.Lines.Last().Kind);
        }
        [Test] public void EmptyReplyGetsPlaceholder()
        {
            var s = new ChatSession("sleuth");
            s.Complete(s.Begin("hi"), new LlmReply { Text = "  " }, 0);
            Assert.AreEqual(CompStrings.T("chat.empty"), s.Lines.Last().Text);
        }
        [Test] public void RateLimitStartsCooldownThenReopens()
        {
            var s = new ChatSession("sleuth");
            var mine = s.Begin("hi");
            s.Fail(mine, new CompError { Code = "RATE_LIMITED", Key = "err.rateLimited", Args = new object[] { "9s" }, RetryAfterMs = 9000 }, 1000);
            Assert.AreEqual(ChatKind.Failed, mine.Kind);
            Assert.AreEqual(ChatBlock.Cooldown, s.Check("again", 5000));
            StringAssert.Contains("5s", s.StatusLine("again", 5000));
            Assert.AreEqual(ChatBlock.None, s.Check("again", 10000));
        }
        [Test] public void RateLimitWithoutRetryAfterStillCoolsDown()
        {
            var s = new ChatSession("sleuth");
            s.Fail(s.Begin("hi"), new CompError { Code = "RATE_LIMITED", Key = "err.rateLimited" }, 0);
            Assert.AreEqual(ChatBlock.Cooldown, s.Check("x", 1));
        }
        [Test] public void DisabledAndUnavailableAreSticky()
        {
            var s = new ChatSession("sleuth");
            s.Fail(null, new CompError { Code = "LLM_DISABLED", Key = "err.llmDisabled" }, 0);
            Assert.AreEqual(ChatBlock.Disabled, s.Check("hi", 0));
            var u = new ChatSession("ai_agent");
            u.Fail(null, CompError.Unavailable(), 0);
            Assert.AreEqual(ChatBlock.Unavailable, u.Check("hi", 0));
            StringAssert.Contains("Not available", u.StatusLine("hi", 0));
        }
        [Test] public void NetworkFailureKeepsChatOpen()
        {
            var s = new ChatSession("sleuth");
            var mine = s.Begin("hi");
            s.Fail(mine, new CompError { Code = "NETWORK_ERROR", Key = "err.network", Retryable = true }, 0);
            Assert.AreEqual(ChatBlock.None, s.Check("hi again", 0));
            Assert.AreEqual(ChatKind.Failed, mine.Kind);
        }
        [Test] public void QuotaBlocks()
        {
            var s = new ChatSession("sleuth");
            s.SetQuota(Q(0, 10), 0);
            Assert.AreEqual(ChatBlock.QuotaMinute, s.Check("hi", 1000));
            StringAssert.Contains("59s", s.StatusLine("hi", 1000));
            Assert.AreEqual(ChatBlock.None, s.Check("hi", 60000), "the one-minute window has reset");
            s.SetQuota(Q(5, 0), 0);
            Assert.AreEqual(ChatBlock.QuotaDay, s.Check("hi", 1000));
        }
        [Test] public void StatusShowsQuotaLeft()
        {
            var s = new ChatSession("sleuth");
            s.SetQuota(Q(5, 12), 0);
            StringAssert.Contains("12 of 50", s.StatusLine("", 0));
            StringAssert.Contains("500", s.StatusLine(new string('a', 600), 0));
        }
        [Test] public void LinesAreBounded()
        {
            var s = new ChatSession("sleuth");
            for (int i = 0; i < 100; i++) s.Complete(s.Begin("m" + i), new LlmReply { Text = "r" }, 0);
            Assert.LessOrEqual(s.Lines.Count, ChatSession.MaxLines);
        }
        [Test] public void NullQuotaIgnored() { var s = new ChatSession("sleuth"); s.SetQuota(null, 0); Assert.IsNull(s.Quota); }
    }

    internal sealed class FakeRest : IRestClient
    {
        public readonly List<(string method, string path, string body, string key)> Calls = new List<(string, string, string, string)>();
        public Func<string, string, JToken> Reply = (m, p) => new JObject();
        public Exception Throw;
        public JObject CompetitionCapabilities;
        public Func<Task<bool>> RefreshHandler { get; set; }

        private Task<T> Do<T>(string method, string path, object body, string key)
        {
            Calls.Add((method, path, body as string, key));
            if (Throw != null) return Task.FromException<T>(Throw);
            var value = method == "GET" && path == "/config" && CompetitionCapabilities != null
                ? new JObject { ["competitionCapabilities"] = CompetitionCapabilities.DeepClone() }
                : Reply(method, path);
            return Task.FromResult((T)(object)value);
        }

        public Task<T> Get<T>(string path, CancellationToken ct = default) => Do<T>("GET", path, null, null);
        public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Do<T>("POST", path, body, idempotencyKey);
        public Task<T> Put<T>(string path, object body, CancellationToken ct = default) => Do<T>("PUT", path, body, null);
        public Task<T> Patch<T>(string path, object body, CancellationToken ct = default) => Do<T>("PATCH", path, body, null);
        public Task<T> Delete<T>(string path, CancellationToken ct = default) => Do<T>("DELETE", path, null, null);
        public Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Do<T>(method, path, body, idempotencyKey);
        public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => throw new NotSupportedException();
    }

    public class ApiTests
    {
        private FakeRest _rest;
        private CompetitionApi _api;
        private readonly CancellationToken _ct = CancellationToken.None;

        [SetUp] public void SetUp()
        {
            _rest = new FakeRest { CompetitionCapabilities = JObject.Parse("{\"version\":1,\"targetedCabalInvites\":true,\"beefChallenges\":true}") };
            _api = new CompetitionApi(_rest);
        }

        private static T Wait<T>(Task<T> t) => t.GetAwaiter().GetResult();

        [Test] public void ReadsUseGetAndTheProposedPaths()
        {
            _rest.Reply = (m, p) => p.StartsWith("/tribes") ? JToken.Parse("{\"tribes\":[]}") : p.StartsWith("/districts/") ? JToken.Parse("{\"districtId\":\"d\",\"standings\":[]}") : p.StartsWith("/districts") ? JToken.Parse("{\"districts\":[]}") : new JObject();
            Wait(_api.Tribes(_ct)); Wait(_api.Districts(_ct)); Wait(_api.Standings("timeline", _ct));
            CollectionAssert.AreEqual(new[] { "/tribes", "/districts", "/districts/timeline/standings" }, _rest.Calls.Select(c => c.path).ToArray());
            Assert.IsTrue(_rest.Calls.All(c => c.method == "GET" && c.key == null));
        }
        [Test] public void UnsafeIdsNeverReachThePath()
        {
            Assert.Throws<ArgumentException>(() => Wait(_api.Standings("../me", _ct)));
            Assert.Throws<ArgumentException>(() => Wait(_api.Beef("a/b", _ct)));
            Assert.Throws<ArgumentException>(() => Wait(_api.Access("x?y=1", _ct)));
            Assert.AreEqual(0, _rest.Calls.Count);
        }
        [Test] public void WritesRequireAnIdempotencyKey()
        {
            Assert.Throws<ArgumentException>(() => Wait(_api.Claim(null, _ct)));
            Assert.Throws<ArgumentException>(() => Wait(_api.OpenBeef(new JObject(), "", _ct)));
            Assert.AreEqual(0, _rest.Calls.Count);
        }
        [Test] public void OpenBeefBodyAndKey()
        {
            _rest.Reply = (m, p) => JToken.Parse("{\"matchId\":\"m\",\"challengerUserId\":\"u\",\"topicId\":\"beef_perps_vs_spot\",\"invitedUserId\":\"guest-1\",\"stakeCents\":2500,\"status\":\"open\"}");
            var m1 = Wait(_api.OpenBeef(CompetitionApi.BeefOpenBody(2500, "beef_perps_vs_spot", "guest-1"), "key-1", _ct));
            Assert.AreEqual("m", m1.MatchId);
            var c = _rest.Calls.Single(x => x.method == "POST");
            Assert.AreEqual(("POST", "/beef", "key-1"), (c.method, c.path, c.key));
            var body = JObject.Parse(c.body);
            Assert.AreEqual(2500, (long)body["stakeCents"]);
            Assert.AreEqual("guest-1", (string)body["invitedUserId"]);
            Assert.IsNull(CompetitionApi.BeefOpenBody(1, "t", null)["invitedUserId"]);
        }
        [Test] public void BeefActionsHitTheirOwnRoutes()
        {
            _rest.Reply = (m, p) => JToken.Parse("{\"matchId\":\"m\",\"challengerUserId\":\"u\",\"topicId\":\"t\",\"stakeCents\":1,\"status\":\"open\"}");
            Wait(_api.AcceptBeef("m", "k1", _ct)); Wait(_api.DeclineBeef("m", "k2", _ct)); Wait(_api.CancelBeef("m", "k3", _ct));
            _rest.Reply = (m, p) => JToken.Parse("{\"matchId\":\"m\",\"votedUserId\":\"u\"}");
            Wait(_api.Vote("m", "u", "k4", _ct));
            CollectionAssert.AreEqual(new[] { "/beef/m/accept", "/beef/m/decline", "/beef/m/cancel", "/beef/m/votes" }, _rest.Calls.Select(c => c.path).ToArray());
            Assert.AreEqual("u", (string)JObject.Parse(_rest.Calls[3].body)["votedUserId"]);
        }
        [Test] public void BeefListQuery()
        {
            _rest.Reply = (m, p) => JToken.Parse("{\"matches\":[],\"nextCursor\":null}");
            Wait(_api.BeefList("mine", "open", "c r", _ct));
            Wait(_api.BeefList(null, null, null, _ct));
            Assert.AreEqual("/beef?scope=mine&status=open&cursor=c%20r", _rest.Calls[0].path);
            Assert.AreEqual("/beef", _rest.Calls[1].path);
        }
        [Test] public void EloAndBoardsAndSeasonResults()
        {
            _rest.Reply = (m, p) => p.StartsWith("/beef/elo") ? JToken.Parse("{\"userId\":\"u\",\"elo\":1000,\"matchesPlayed\":0,\"wins\":0,\"losses\":0,\"draws\":0}") : p.StartsWith("/leaderboards") ? JToken.Parse("{\"entries\":[],\"me\":null}") : JToken.Parse("{\"seasonId\":\"s\"}");
            Wait(_api.Elo(null, _ct)); Wait(_api.Elo("u 1", _ct)); Wait(_api.Board("beef_elo", null, _ct)); Wait(_api.Board("net_worth", "league", _ct)); Wait(_api.SeasonResults(null, _ct)); Wait(_api.SeasonResults("2026 S1", _ct));
            CollectionAssert.AreEqual(new[] { "/beef/elo", "/beef/elo?userId=u%201", "/leaderboards/beef_elo", "/leaderboards/net_worth?scope=league", "/me/season-results", "/me/season-results?seasonId=2026%20S1" },
                _rest.Calls.Select(c => c.path).ToArray());
        }
        [Test] public void TribeChangeUsesImplementedBackendRoute()
        {
            Wait(_api.ChangeTribe("builder", "k", _ct));
            Assert.AreEqual(("POST", "/me/tribe", "k"), (_rest.Calls[0].method, _rest.Calls[0].path, _rest.Calls[0].key));
            Assert.AreEqual("builder", (string)JObject.Parse(_rest.Calls[0].body)["tribeId"]);
        }
        [Test] public void FarmClaimAndBail()
        {
            _rest.Reply = (m, p) => p == "/airdrop/claim" ? JToken.Parse("{\"claimId\":\"c\",\"amountCents\":115,\"dayDate\":\"2026-10-08\"}") : JToken.Parse("{\"jailed\":false,\"bailable\":false}");
            Assert.AreEqual(115, Wait(_api.Claim("k", _ct)).AmountCents);
            Assert.IsFalse(Wait(_api.Bail("k2", _ct)).Jailed);
            CollectionAssert.AreEqual(new[] { "/airdrop/claim", "/me/sybil-status/bail" }, _rest.Calls.Select(c => c.path).ToArray());
        }
        [Test] public void CabalCallsNeverCarryACode()
        {
            _rest.Reply = (m, p) => JToken.Parse("{\"inviteId\":\"i\",\"hostUserId\":\"h\",\"guestUserId\":\"guest-1\",\"status\":\"pending\"}");
            Wait(_api.Invite("guest-1", "k1", _ct)); Wait(_api.AcceptInvite("i1", "k2", _ct)); Wait(_api.RevokeInvite("i1", "k3", _ct));
            _rest.Reply = (m, p) => JToken.Parse("{\"invites\":[]}");
            Wait(_api.CabalInvites("guest", "guest-1", _ct));
            Assert.AreEqual("/cabal/invites?guestId=guest-1", _rest.Calls.Last().path);
            Assert.AreEqual("{\"guestUserId\":\"guest-1\"}", _rest.Calls.Single(x => x.path == "/cabal/invites").body);
            Assert.IsFalse(_rest.Calls.Any(c => (c.body ?? "").Contains("code") || c.path.Contains("code")));
        }
        [Test] public void UnsupportedCompetitionWritesAreBlockedBeforeMutation()
        {
            _rest.CompetitionCapabilities = null;
            var invite = Assert.Throws<ApiException>(() => Wait(_api.Invite("guest-1", "k1", _ct)));
            var beef = Assert.Throws<ApiException>(() => Wait(_api.OpenBeef(CompetitionApi.BeefOpenBody(100, "topic", "guest-1"), "k2", _ct)));
            Assert.IsTrue(CompErrors.From(invite, true).IsUnavailable);
            Assert.IsFalse(CompErrors.From(beef, true).OutcomeUnknown);
            Assert.IsTrue(_rest.Calls.All(x => x.method == "GET" && x.path == "/config"));
        }
        [TestCase("{\"version\":1,\"beefChallenges\":\"true\"}")]
        [TestCase("{\"version\":2,\"beefChallenges\":true}")]
        [TestCase("{\"version\":1,\"beefChallenges\":false}")]
        public void CapabilityNeedsExactVersionAndBoolean(string value)
        {
            _rest.CompetitionCapabilities = JObject.Parse(value);
            Assert.Throws<ApiException>(() => Wait(_api.OpenBeef(CompetitionApi.BeefOpenBody(100, "topic", null), "k", _ct)));
            Assert.IsFalse(_rest.Calls.Any(x => x.method == "POST"));
        }
        [Test] public void IgnoredInviteRecipientNeverBecomesSentSuccessAndRetainsRequestKey()
        {
            _rest.Reply = (m, p) => JToken.Parse("{\"inviteId\":\"i\",\"hostUserId\":\"h\",\"guestUserId\":null}");
            var key = new RetainedKey();
            var before = key.KeyFor("POST", "/cabal/invites", "guest-1");
            var error = Assert.Throws<FormatException>(() => Wait(_api.Invite("guest-1", before, _ct)));
            key.Settle(CompErrors.From(error, true));
            Assert.AreEqual(before, key.KeyFor("POST", "/cabal/invites", "guest-1"));
        }
        [TestCase(null, "topic", 100)]
        [TestCase("guest-1", null, 100)]
        [TestCase("guest-1", "topic", 999)]
        public void IgnoredBeefTermsNeverBecomeSentSuccess(string guest, string topic, int stake)
        {
            _rest.Reply = (m, p) => new JObject { ["matchId"] = "m", ["challengerUserId"] = "u", ["invitedUserId"] = guest, ["topicId"] = topic, ["stakeCents"] = stake, ["status"] = "open" };
            var body = CompetitionApi.BeefOpenBody(100, "topic", "guest-1");
            var key = new RetainedKey();
            var before = key.KeyFor("POST", "/beef", body.ToString());
            var error = Assert.Throws<FormatException>(() => Wait(_api.OpenBeef(body, before, _ct)));
            var outcome = CompErrors.From(error, true);
            Assert.IsTrue(outcome.OutcomeUnknown);
            key.Settle(outcome);
            Assert.AreEqual(before, key.KeyFor("POST", "/beef", body.ToString()));
            Assert.AreEqual(before, _rest.Calls.Single(x => x.method == "POST").key);
        }
        [Test] public void CabalListsUseTheBackendCallerFilters()
        {
            _rest.Reply = (m, p) => JToken.Parse("{\"invites\":[]}");
            Wait(_api.CabalInvites("host", "user-1", _ct));
            Assert.AreEqual("/cabal/invites?hostId=user-1", _rest.Calls.Single().path);
            Assert.Throws<ArgumentException>(() => Wait(_api.CabalInvites("all", "user-1", _ct)));
            Assert.Throws<ArgumentException>(() => Wait(_api.CabalInvites("guest", null, _ct)));
            Assert.AreEqual(1, _rest.Calls.Count, "invalid caller filters never send a request");
        }
        [Test] public void UsedCabalInviteFromCurrentBackendIsNotPending()
        {
            var invite = CabalInvite.Parse(JToken.Parse("{\"inviteId\":\"i-1\",\"hostUserId\":\"h-1\",\"guestUserId\":\"g-1\",\"usedAt\":\"2026-10-12T12:00:00Z\"}"));
            Assert.AreEqual("accepted", invite.Status);
            Assert.IsFalse(GateRules.InviteIsPendingFor(invite, "g-1", 0));
        }
        [Test] public void ServerRefusalReasonIsPreserved()
        {
            var reply = LlmReply.Parse(JToken.Parse("{\"response\":\"prompt_injection_detected\",\"refused\":true,\"refusalReason\":\"prompt_injection_detected\"}"));
            Assert.AreEqual("prompt_injection_detected", reply.RefusalReasonId);
            Assert.IsTrue(reply.Refused);
        }
        [Test] public void ChatBodyAndNpcIdFiltering()
        {
            _rest.Reply = (m, p) => JToken.Parse("{\"replyId\":\"r\",\"text\":\"gm\"}");
            Wait(_api.Chat("sleuth", "hello", "npc_9", "k1", _ct));
            Wait(_api.Chat("ai_agent", "hello", "bad id!", "k2", _ct));
            var b0 = JObject.Parse(_rest.Calls[0].body); var b1 = JObject.Parse(_rest.Calls[1].body);
            Assert.AreEqual("npc_9", (string)b0["npcId"]);
            Assert.IsNull(b1["npcId"], "an unsafe npc id is not sent");
            Assert.AreEqual("sleuth", (string)b0["archetype"]);
        }
        [Test] public void ReportsUseNpcReplyTarget()
        {
            Wait(_api.ReportReply("r1", "spam", "k", _ct));
            var b = JObject.Parse(_rest.Calls[0].body);
            Assert.AreEqual("npc_reply", (string)b["targetType"]);
            Assert.AreEqual("/reports", _rest.Calls[0].path);
        }
        [Test] public void XStartRejectsUnsafeUrl()
        {
            _rest.Reply = (m, p) => JToken.Parse("{\"authorizationUrl\":\"https://x.com/i/oauth2/authorize?state=s\",\"state\":\"s\"}");
            Assert.AreEqual("https://x.com/i/oauth2/authorize?state=s", Wait(_api.XLinkStart(_ct)));
            _rest.Reply = (m, p) => JToken.Parse("{\"authorizationUrl\":\"https://evil.example/phish\",\"state\":\"s\"}");
            Assert.Throws<FormatException>(() => Wait(_api.XLinkStart(_ct)));
            _rest.Reply = (m, p) => JToken.Parse("{\"authorizationUrl\":\"javascript:alert(1)\",\"state\":\"s\"}");
            Assert.Throws<FormatException>(() => Wait(_api.XLinkStart(_ct)));
        }
        [Test] public void XStatusNullAndUnlinkSendsNoBody()
        {
            _rest.Reply = (m, p) => JToken.Parse("{\"link\":null}");
            Assert.IsNull(Wait(_api.XLink(_ct)));
            _rest.Reply = (m, p) => null;
            Wait(_api.XUnlink("k", _ct));
            var del = _rest.Calls[1];
            Assert.AreEqual("DELETE /x/link k", del.method + " " + del.path + " " + del.key);
            Assert.IsNull(del.body);
        }
        [Test] public void AliasReadsOnlyTheAlias()
        {
            _rest.Reply = (m, p) => JToken.Parse("{\"id\":\"u\",\"alias\":\"anon\\u0007x\",\"netWorthCents\":5}");
            Assert.AreEqual("anonx", Wait(_api.Alias("u", _ct)));
        }
        [Test] public void MissingRouteSurfacesAsUnavailable()
        {
            _rest.Throw = new ApiException(404, "HTTP_404", "Not Found");
            var ex = Assert.Throws<ApiException>(() => Wait(_api.Farm(_ct)));
            Assert.IsTrue(CompErrors.From(ex, false).IsUnavailable);
        }
        [Test] public void MalformedBodyIsAFormatException()
        {
            _rest.Reply = (m, p) => JToken.Parse("[1,2]");
            Assert.Throws<FormatException>(() => Wait(_api.Farm(_ct)));
        }
        [Test] public void RouteListsAreUnique()
        {
            Assert.AreEqual(CompetitionApi.ProposalRoutes.Length, CompetitionApi.ProposalRoutes.Distinct().Count());
            Assert.AreEqual(31, CompetitionApi.ProposalRoutes.Length);
        }
    }

    public class StringTests
    {
        [Test] public void EveryBeefTopicHasCopy() { foreach (var t in CompStrings.BeefTopics) Assert.IsTrue(CompStrings.Has("topic." + t), t); }
        [Test] public void EveryCopyFormatsWithSpareArgs() { foreach (var k in CompStrings.Keys) Assert.DoesNotThrow(() => CompStrings.F(k, "a", "b", "c"), k); }
        [Test] public void NoMarkupInCopy() { foreach (var k in CompStrings.Keys) Assert.IsFalse(CompStrings.T(k).Contains("<"), k); }
        [Test] public void MissingKeyEchoesKey() { Assert.AreEqual("no.such.key", CompStrings.T("no.such.key")); Assert.IsFalse(CompStrings.Has("no.such.key")); }
        [Test] public void BadFormatDoesNotThrow() => Assert.AreEqual("{0} {1}", CompStrings.F("chat.quotaLeft", new object[0]).Length > 0 ? "{0} {1}" : "");
        [Test] public void ReadableHandlesEdgeCases() { Assert.AreEqual("—", CompStrings.Readable(null)); Assert.AreEqual("—", CompStrings.Readable("___")); Assert.AreEqual("Sybil clone farm", CompStrings.Readable("sybil_clone_farm")); }
        [Test] public void EverySybilReasonFromTheProposalHasCopy() { foreach (var r in new[] { "sybil_clone_farm", "sybil_multi_account", "sybil_vote_ring" }) Assert.IsTrue(CompStrings.Has("sybil.reason." + r)); }
        [Test] public void NoRealBrandsInCopy()
        {
            // Parody names only (docs/AGENTS-BRIEF.md). X is allowed here only as the attribution the X display rules require.
            foreach (var k in CompStrings.Keys)
                foreach (var banned in new[] { "Binance", "Coinbase", "Elon", "Vitalik", "Trump", "pump.fun", "Uniswap" })
                    Assert.IsFalse(CompStrings.T(k).Contains(banned), k);
        }
    }
}
