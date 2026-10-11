using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Competition.Tests
{
    public class ErrorTests
    {
        private static CompError E(int status, string code, JToken details = null, bool network = false, bool write = false) =>
            CompErrors.From(new ApiException(status, code, "msg", details, network), write);

        [Test] public void MissingRouteIsUnavailable() { Assert.IsTrue(E(404, "HTTP_404").IsUnavailable); Assert.IsTrue(E(405, "HTTP_405").IsUnavailable); Assert.IsTrue(E(501, "HTTP_501").IsUnavailable); }
        [Test] public void ServerNoRouteIsUnavailable() { Assert.IsTrue(CompErrors.From(new ApiException(404, "NOT_FOUND", "No route for this path and method."), false).IsUnavailable); }
        [Test] public void ContractNotFoundIsNotUnavailable() { var e = E(404, "NOT_FOUND"); Assert.IsFalse(e.IsUnavailable); Assert.AreEqual("err.notFound", e.Key); }
        [Test] public void UnavailableMessageSaysNotAvailable() => StringAssert.Contains("Not available", E(404, "HTTP_404").Message);
        [Test] public void LlmDisabledIsItsOwnState() { var e = E(503, "LLM_DISABLED"); Assert.IsFalse(e.IsUnavailable); Assert.AreEqual("err.llmDisabled", e.Key); }
        [Test] public void XLinkDisabled() => Assert.AreEqual("err.xDisabled", E(503, "X_LINK_DISABLED").Key);
        [Test] public void XAccountAlreadyLinked() => Assert.AreEqual("err.xAlreadyLinked", E(409, "X_ACCOUNT_ALREADY_LINKED").Key);
        [Test] public void ServerErrorRetryable() { var e = E(500, "INTERNAL_ERROR_X"); Assert.IsTrue(e.Retryable); Assert.AreEqual("err.server", e.Key); }
        [Test] public void WriteServerErrorLeavesOutcomeUnknown() { var e = E(500, "HTTP_500", null, false, true); Assert.IsTrue(e.OutcomeUnknown); }
        [Test] public void NetworkReadVsWrite() { Assert.IsFalse(E(0, "NETWORK_ERROR", null, true).OutcomeUnknown); Assert.IsTrue(E(0, "NETWORK_ERROR", null, true, true).OutcomeUnknown); }
        [Test] public void RateLimitedCarriesRetryAfter()
        {
            var e = E(429, "RATE_LIMITED", JObject.Parse("{\"retryAfterMs\":12000}"));
            Assert.AreEqual(12000, e.RetryAfterMs);
            StringAssert.Contains("12s", e.Message);
        }
        [Test] public void InsufficientCashFromBackendVariant()
        {
            var e = E(400, "VALIDATION_FAILED", JObject.Parse("{\"reason\":\"insufficient cash\",\"have\":100,\"need\":350}"));
            Assert.AreEqual("INSUFFICIENT_CASH", e.Code);
            StringAssert.Contains("$2.50", e.Message);
        }
        [Test] public void ForbiddenStakeReason() { var e = E(403, "FORBIDDEN", JObject.Parse("{\"reason\":\"insufficient_cash_for_stake\"}")); Assert.AreEqual("INSUFFICIENT_CASH", e.Code); }
        [Test] public void ForbiddenSybilReason() { var e = E(403, "FORBIDDEN", JObject.Parse("{\"reason\":\"sybil_jailed\"}")); Assert.AreEqual("SYBIL_JAILED", e.Code); }
        [Test] public void NetWorthTooLowShowsAmountWhenGiven()
        {
            var e = E(403, "NET_WORTH_TOO_LOW", JObject.Parse("{\"requiredNetWorthCents\":10000000}"));
            StringAssert.Contains("$100,000.00", e.Message);
            Assert.AreEqual("err.netWorth", E(403, "NET_WORTH_TOO_LOW").Key);
        }
        [TestCase("INVITE_REQUIRED", "err.inviteRequired")]
        [TestCase("INVITE_INVALID", "err.inviteInvalid")]
        [TestCase("VOTE_NOT_ALLOWED", "err.voteNotAllowed")]
        [TestCase("ALREADY_VOTED", "err.alreadyVoted")]
        [TestCase("SYBIL_JAILED", "err.sybilJailed")]
        [TestCase("PASS_REQUIRED", "err.passRequired")]
        public void ProposedCodesMap(string code, string key) => Assert.AreEqual(key, E(403, code).Key);
        [Test] public void UnknownCodeIsGeneric() { Assert.AreEqual("err.generic", E(418, "TEAPOT").Key); }
        [Test] public void FormatExceptionIsInvalidResponse() { Assert.AreEqual("INVALID_RESPONSE", CompErrors.From(new FormatException(), false).Code); }
        [Test] public void TimeoutWriteUnknownOutcome() { Assert.IsTrue(CompErrors.From(new TimeoutException(), true).OutcomeUnknown); }
        [Test] public void NullExceptionSafe() => Assert.AreEqual("err.generic", CompErrors.From(null, false).Key);

        [Test] public void JoinRejectionWording()
        {
            Assert.IsNotNull(CompErrors.JoinRejection("NET_WORTH_TOO_LOW"));
            Assert.IsNotNull(CompErrors.JoinRejection("INVITE_REQUIRED"));
            Assert.IsNotNull(CompErrors.JoinRejection("SYBIL_JAILED"));
            Assert.IsNull(CompErrors.JoinRejection("VENUE_FULL"));
        }

        [Test] public void RetainedKeyReusedWhileOutcomeUnknown()
        {
            int n = 0;
            var k = new RetainedKey(() => "k" + ++n);
            var a = k.KeyFor("POST", "/beef", "{x}");
            k.Settle(new CompError { OutcomeUnknown = true });
            Assert.AreEqual(a, k.KeyFor("POST", "/beef", "{x}"));
            k.Settle(new CompError { OutcomeUnknown = false });
            Assert.AreNotEqual(a, k.KeyFor("POST", "/beef", "{x}"));
        }
        [Test] public void RetainedKeyNewForDifferentBody()
        {
            int n = 0;
            var k = new RetainedKey(() => "k" + ++n);
            var a = k.KeyFor("POST", "/beef", "1");
            Assert.AreNotEqual(a, k.KeyFor("POST", "/beef", "2"));
        }
        [Test] public void CountdownText() { Assert.AreEqual("0s", CompTime.Countdown(0)); Assert.AreEqual("1m 5s", CompTime.Countdown(65000)); Assert.AreEqual("1d 1h", CompTime.Countdown(90000000)); Assert.AreEqual("2h 0m", CompTime.Countdown(7200000)); }
    }

    public class BeefRuleTests
    {
        private static BeefMatch M(string status, string challenger = "c", string invited = null, string accepter = null, string myVote = null, string winner = null) =>
            new BeefMatch { MatchId = "m", ChallengerUserId = challenger, InvitedUserId = invited, AccepterUserId = accepter, Status = status, MyVote = myVote, WinnerUserId = winner, StakeCents = 1000, FeeBps = 500, TopicId = "beef_perps_vs_spot" };

        [TestCase("open", BeefPhase.Open)] [TestCase("active", BeefPhase.Active)] [TestCase("voting", BeefPhase.Voting)] [TestCase("closed", BeefPhase.Closed)]
        [TestCase("declined", BeefPhase.Declined)] [TestCase("cancelled", BeefPhase.Cancelled)] [TestCase("expired", BeefPhase.Expired)] [TestCase("zzz", BeefPhase.Unknown)]
        public void PhaseMapping(string s, BeefPhase p) => Assert.AreEqual(p, BeefRules.PhaseOf(s));

        [Test] public void FinishedPhases() { Assert.IsTrue(BeefRules.IsFinished(BeefPhase.Closed)); Assert.IsFalse(BeefRules.IsFinished(BeefPhase.Voting)); Assert.IsFalse(BeefRules.IsFinished(BeefPhase.Unknown)); }

        [Test] public void ChallengerCanOnlyCancelOpen()
        {
            var a = BeefRules.ActionsFor(M("open"), "c");
            Assert.IsTrue(a.CanCancel); Assert.IsFalse(a.CanAccept); Assert.IsFalse(a.CanDecline); Assert.IsFalse(a.CanVote);
            Assert.IsFalse(BeefRules.ActionsFor(M("active"), "c").CanCancel);
        }
        [Test] public void InvitedCanAcceptOrDecline()
        {
            var a = BeefRules.ActionsFor(M("open", invited: "me"), "me");
            Assert.IsTrue(a.CanAccept); Assert.IsTrue(a.CanDecline); Assert.IsFalse(a.CanCancel);
        }
        [Test] public void OthersCannotAcceptInvitedOnlyMatch() => Assert.IsFalse(BeefRules.ActionsFor(M("open", invited: "someone"), "me").CanAccept);
        [Test] public void AnyoneCanAcceptOpenToAllButNotOwn()
        {
            Assert.IsTrue(BeefRules.ActionsFor(M("open"), "me").CanAccept);
            Assert.IsFalse(BeefRules.ActionsFor(M("open"), "c").CanAccept);
            Assert.IsFalse(BeefRules.ActionsFor(M("open"), "me").CanDecline, "only an invited player declines");
        }
        [Test] public void SpectatorVotesOnceWhileVoting()
        {
            Assert.IsTrue(BeefRules.ActionsFor(M("voting", accepter: "a"), "me").CanVote);
            Assert.IsFalse(BeefRules.ActionsFor(M("voting", accepter: "a", myVote: "a"), "me").CanVote);
            Assert.IsFalse(BeefRules.ActionsFor(M("active", accepter: "a"), "me").CanVote);
            Assert.IsFalse(BeefRules.ActionsFor(M("closed", accepter: "a"), "me").CanVote);
        }
        [Test] public void ParticipantsNeverVote()
        {
            Assert.IsFalse(BeefRules.ActionsFor(M("voting", accepter: "a"), "c").CanVote);
            Assert.IsFalse(BeefRules.ActionsFor(M("voting", accepter: "a"), "a").CanVote);
        }
        [Test] public void NoUserNoActions()
        {
            var a = BeefRules.ActionsFor(M("open"), null);
            Assert.IsFalse(a.CanAccept || a.CanCancel || a.CanDecline || a.CanVote);
            var b = BeefRules.ActionsFor(null, "x");
            Assert.IsFalse(b.CanAccept || b.CanCancel || b.CanDecline || b.CanVote);
        }
        [Test] public void RoleOf()
        {
            Assert.AreEqual(BeefRole.Challenger, BeefRules.RoleOf(M("open"), "c"));
            Assert.AreEqual(BeefRole.Invited, BeefRules.RoleOf(M("open", invited: "i"), "i"));
            Assert.AreEqual(BeefRole.Spectator, BeefRules.RoleOf(M("active", invited: "i"), "i"), "an invitee who never accepted is a spectator once it is live");
            Assert.AreEqual(BeefRole.Accepter, BeefRules.RoleOf(M("active", accepter: "a"), "a"));
        }

        [TestCase("5", 500)] [TestCase("12.5", 1250)] [TestCase("12.50", 1250)] [TestCase("$3", 300)] [TestCase(".5", 50)] [TestCase(" 7 ", 700)] [TestCase("0.01", 1)]
        public void StakeParsesGood(string text, long cents) { Assert.IsTrue(BeefRules.TryParseStakeCents(text, out var c)); Assert.AreEqual(cents, c); }
        [TestCase("")] [TestCase("abc")] [TestCase("1.234")] [TestCase("-5")] [TestCase("1,5")] [TestCase("1.2.3")] [TestCase(".")] [TestCase("9999999999999")] [TestCase("1e3")]
        public void StakeParsesBad(string text) => Assert.IsFalse(BeefRules.TryParseStakeCents(text, out _));
        [Test] public void StakeMustBePositive() { Assert.IsNotNull(BeefRules.StakeError(0)); Assert.IsNotNull(BeefRules.StakeError(-1)); Assert.IsNull(BeefRules.StakeError(1)); }

        [Test] public void OutcomeForParticipants()
        {
            Assert.AreEqual("won", BeefRules.OutcomeFor(M("closed", accepter: "a", winner: "c"), "c"));
            Assert.AreEqual("lost", BeefRules.OutcomeFor(M("closed", accepter: "a", winner: "c"), "a"));
            Assert.AreEqual("draw", BeefRules.OutcomeFor(M("closed", accepter: "a"), "a"));
            Assert.AreEqual("none", BeefRules.OutcomeFor(M("closed", accepter: "a", winner: "c"), "spectator"));
            Assert.AreEqual("none", BeefRules.OutcomeFor(M("voting", accepter: "a"), "a"));
        }
        [Test] public void EloDeltaFromServerValues()
        {
            var m = M("closed", accepter: "a", winner: "c");
            m.EloChallengerPre = 1000; m.EloChallengerPost = 1016; m.EloAccepterPre = 1000; m.EloAccepterPost = 984;
            Assert.AreEqual(16, BeefRules.EloDeltaFor(m, "c"));
            Assert.AreEqual(-16, BeefRules.EloDeltaFor(m, "a"));
            Assert.IsNull(BeefRules.EloDeltaFor(m, "x"));
            m.EloChallengerPost = null;
            Assert.IsNull(BeefRules.EloDeltaFor(m, "c"));
        }
        [Test] public void SignedElo() { Assert.AreEqual("+5", BeefRules.SignedElo(5)); Assert.AreEqual("-5", BeefRules.SignedElo(-5)); Assert.AreEqual("0", BeefRules.SignedElo(0)); }
        [Test] public void EstimatedPayoutUsesServerFee() { Assert.AreEqual(1900, BeefRules.EstimatedPayoutCents(M("open"))); var m = M("open"); m.FeeBps = 0; Assert.AreEqual(2000, BeefRules.EstimatedPayoutCents(m)); m.FeeBps = 99999; Assert.AreEqual(0, BeefRules.EstimatedPayoutCents(m)); }
        [Test] public void OrderLiveFirstThenFinishedNewest()
        {
            var live = M("voting"); live.MatchId = "live"; live.PhaseEndsAtMs = 100;
            var later = M("active"); later.MatchId = "later"; later.PhaseEndsAtMs = 200;
            var old = M("closed"); old.MatchId = "old"; old.SettledAtMs = 10;
            var fresh = M("expired"); fresh.MatchId = "fresh"; fresh.SettledAtMs = 50;
            CollectionAssert.AreEqual(new[] { "live", "later", "fresh", "old" }, BeefRules.Order(new[] { old, later, fresh, live }).Select(x => x.MatchId).ToArray());
        }
        [Test] public void TopicLabelFallsBack() { Assert.AreEqual("Perps vs spot", BeefRules.TopicLabel("beef_perps_vs_spot")); Assert.AreEqual("Beef new topic", BeefRules.TopicLabel("beef_new_topic")); }
    }

    public class GateFarmXTests
    {
        [Test] public void GatedVenues() { Assert.IsTrue(GateRules.IsGated("dubai_marina")); Assert.IsTrue(GateRules.IsGated("cabal_lounge")); Assert.IsTrue(GateRules.IsGated("sybil_barn")); Assert.IsFalse(GateRules.IsGated("ratio_arena")); }
        [Test] public void AllowedHasNoMessage() => Assert.IsNull(GateRules.Message(new VenueAccess { Allowed = true }));
        [Test] public void NetWorthMessageNamesAmounts()
        {
            var m = GateRules.Message(new VenueAccess { Allowed = false, ReasonCode = "NET_WORTH_TOO_LOW", RequiredNetWorthCents = 10000000, NetWorthCents = 2500000 });
            StringAssert.Contains("$100,000.00", m); StringAssert.Contains("$25,000.00", m);
        }
        [Test] public void NetWorthMessageWithoutAmount() => Assert.AreEqual(CompStrings.T("err.netWorth"), GateRules.Message(new VenueAccess { ReasonCode = "NET_WORTH_TOO_LOW" }));
        [Test] public void InviteAndJailAndUnknown()
        {
            StringAssert.Contains("invited", GateRules.Message(new VenueAccess { ReasonCode = "INVITE_REQUIRED" }));
            StringAssert.Contains("Sybil", GateRules.Message(new VenueAccess { ReasonCode = "SYBIL_JAILED" }));
            Assert.AreEqual(CompStrings.T("gate.denied"), GateRules.Message(new VenueAccess { ReasonCode = "SOMETHING_NEW" }));
        }
        [Test] public void Shortfall()
        {
            Assert.AreEqual(7500000, GateRules.Shortfall(new VenueAccess { RequiredNetWorthCents = 10000000, NetWorthCents = 2500000 }));
            Assert.IsNull(GateRules.Shortfall(new VenueAccess { RequiredNetWorthCents = 100, NetWorthCents = 200 }));
            Assert.IsNull(GateRules.Shortfall(new VenueAccess()));
        }
        [Test] public void HostBlock()
        {
            Assert.AreEqual("cabal.notMember", GateRules.HostBlock(new CabalMembership { Member = false }));
            Assert.AreEqual("cabal.noInvites", GateRules.HostBlock(new CabalMembership { Member = true, InvitesRemaining = 0 }));
            Assert.IsNull(GateRules.HostBlock(new CabalMembership { Member = true, InvitesRemaining = 2 }));
            Assert.IsNull(GateRules.HostBlock(null));
        }
        [Test] public void PendingInviteForMeOnly()
        {
            var i = new CabalInvite { Status = "pending", GuestUserId = "me", ExpiresAtMs = 2000 };
            Assert.IsTrue(GateRules.InviteIsPendingFor(i, "me", 1000));
            Assert.IsFalse(GateRules.InviteIsPendingFor(i, "me", 3000), "expired");
            Assert.IsFalse(GateRules.InviteIsPendingFor(i, "other", 1000));
            i.Status = "accepted";
            Assert.IsFalse(GateRules.InviteIsPendingFor(i, "me", 1000));
        }

        [Test] public void FarmClaimability()
        {
            var f = new FarmStatus { ClaimedToday = false, NextClaimAtMs = 5000 };
            Assert.IsTrue(FarmRules.CanClaim(f, null, 1000));
            f.ClaimedToday = true;
            Assert.IsFalse(FarmRules.CanClaim(f, null, 1000));
            Assert.IsTrue(FarmRules.CanClaim(f, null, 5000), "the next day opens at nextClaimAt");
            Assert.IsFalse(FarmRules.CanClaim(new FarmStatus(), new SybilStatus { Jailed = true }, 1000), "jailed players cannot claim");
            Assert.IsFalse(FarmRules.CanClaim(null, null, 1));
            Assert.AreEqual(4000, FarmRules.MsUntilNextClaim(f, 1000));
            Assert.AreEqual(0, FarmRules.MsUntilNextClaim(f, 9000));
        }
        [Test] public void JailReasonLabels()
        {
            Assert.AreEqual("Farming with clones", FarmRules.JailReasonLabel("sybil_clone_farm"));
            Assert.AreEqual(CompStrings.T("sybil.reason.unknown"), FarmRules.JailReasonLabel("brand_new"));
            Assert.AreEqual(CompStrings.T("sybil.reason.unknown"), FarmRules.JailReasonLabel(null));
        }

        [TestCase("https://x.com/i/oauth2/authorize?client_id=1&state=s", true)]
        [TestCase("https://twitter.com/i/oauth2/authorize?x=1", true)]
        [TestCase("https://api.x.com/2/oauth2", true)]
        [TestCase("http://x.com/i/oauth2/authorize", false)]
        [TestCase("https://evilx.com/i/oauth2/authorize", false)]
        [TestCase("https://x.com.evil.example/a", false)]
        [TestCase("https://user:pw@x.com/a", false)]
        [TestCase("javascript:alert(1)", false)]
        [TestCase("ctlife://callback", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void AuthUrlAllowList(string url, bool ok) => Assert.AreEqual(ok, XLinkRules.IsSafeAuthUrl(url));
        [Test] public void AvatarMustBeHttps() { Assert.IsNotNull(XLinkRules.SafeAvatarUrl("https://pbs.example/a.jpg")); Assert.IsNull(XLinkRules.SafeAvatarUrl("http://pbs.example/a.jpg")); Assert.IsNull(XLinkRules.SafeAvatarUrl("file:///etc/passwd")); Assert.IsNull(XLinkRules.SafeAvatarUrl(new string('a', 600))); }
        [TestCase("ct_anon", "ct_anon")] [TestCase("@ct_anon", "ct_anon")] [TestCase("a b", null)] [TestCase("<script>", null)] [TestCase("", null)] [TestCase("0123456789012345678901234567890123", null)]
        public void HandleClean(string raw, string expected) => Assert.AreEqual(expected, XLinkRules.CleanHandle(raw));
        [Test] public void DisplayAddsAt() { Assert.AreEqual("@ct_anon", XLinkRules.Display("ct_anon")); Assert.IsNull(XLinkRules.Display("bad handle")); }

        [Test] public void LeagueLabelLookupAndFallback()
        {
            var l = new List<LeagueInfo> { new LeagueInfo { Id = "id1", Slug = "gold", Label = "Gold" } };
            Assert.AreEqual("Gold", LeagueRules.LeagueLabel(l, "gold"));
            Assert.AreEqual("Gold", LeagueRules.LeagueLabel(l, "id1"));
            Assert.AreEqual("Mystery league", LeagueRules.LeagueLabel(l, "mystery_league"));
            Assert.AreEqual("—", LeagueRules.LeagueLabel(l, null));
            Assert.AreEqual("league.promoted", LeagueRules.OutcomeKey("promoted"));
            Assert.AreEqual("league.stayed", LeagueRules.OutcomeKey("weird"));
        }

        [Test] public void LinksRouteVenuesAndNpcs()
        {
            Assert.AreEqual("beef", CompetitionLinks.AppForVenue("ratio_arena"));
            Assert.AreEqual("farm", CompetitionLinks.AppForVenue("airdrop_fields"));
            Assert.AreEqual("farm", CompetitionLinks.AppForVenue("sybil_barn"));
            Assert.AreEqual("cabal", CompetitionLinks.AppForVenue("cabal_lounge"));
            Assert.AreEqual("cabal", CompetitionLinks.AppForVenue("dubai_marina"));
            Assert.IsNull(CompetitionLinks.AppForVenue("perp_pit"));
            string opened = null;
            CompetitionLinks.OpenApp = id => opened = id;
            try
            {
                Assert.IsFalse(CompetitionLinks.OpenNpcChat("rug_dev", null));
                Assert.IsNull(opened);
                Assert.IsTrue(CompetitionLinks.OpenNpcChat("sleuth", "npc_1"));
                Assert.AreEqual("npcchat", opened);
                CompetitionLinks.TakePendingChat(out var a, out var n);
                Assert.AreEqual("sleuth", a); Assert.AreEqual("npc_1", n);
                CompetitionLinks.TakePendingChat(out a, out n);
                Assert.IsNull(a);
                CompetitionLinks.InviteToCabal("bad/id", "x");
                CompetitionLinks.TakePendingCabalGuest(out var g, out _);
                Assert.IsNull(g, "unsafe ids are refused");
            }
            finally { CompetitionLinks.OpenApp = null; }
        }
    }
}
