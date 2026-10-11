using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Competition.Tests
{
    public class WireTests
    {
        [Test] public void LongAcceptsIntegersAndDecimalStrings() { Assert.AreEqual(12345678901L, CompWire.Long(JToken.Parse("\"12345678901\""))); Assert.AreEqual(5, CompWire.Long(JToken.Parse("5"))); }
        [Test] public void LongRejectsGarbageAndFractions() { Assert.Throws<FormatException>(() => CompWire.Long(JToken.Parse("\"abc\""))); Assert.Throws<FormatException>(() => CompWire.Long(JToken.Parse("1.5"))); }
        [Test] public void LongOrFallsBackOnNullAndGarbage() { Assert.AreEqual(7, CompWire.LongOr(null, 7)); Assert.AreEqual(7, CompWire.LongOr(JToken.Parse("\"x\""), 7)); }
        [Test] public void TimeMsReadsIsoAndEpoch()
        {
            Assert.AreEqual(1760000000000L, CompWire.TimeMs(JToken.Parse("1760000000000")));
            Assert.AreEqual(1760000000000L, CompWire.TimeMs(new JValue("2025-10-09T08:53:20Z")));
            Assert.IsNull(CompWire.TimeMs(JValue.CreateNull()));
            Assert.Throws<FormatException>(() => CompWire.TimeMs(new JValue("not a time")));
            Assert.IsNull(CompWire.TimeMsOrNull(new JValue("not a time")));
        }
        [Test] public void ListAcceptsBareAndWrapped()
        {
            Assert.AreEqual(2, CompWire.List(JToken.Parse("[1,2]"), "items").Count);
            Assert.AreEqual(1, CompWire.List(JToken.Parse("{\"tribes\":[1]}"), "tribes").Count);
            Assert.Throws<FormatException>(() => CompWire.List(JToken.Parse("{\"x\":1}"), "tribes"));
        }
        [Test] public void PlainDropsControlsAndCaps() { Assert.AreEqual("ab", CompWire.Plain("a\u0007b", 10)); Assert.AreEqual(3, CompWire.Plain(new string('x', 99), 3).Length); }
        [TestCase("abc-123_x", true)]
        [TestCase("a/b", false)]
        [TestCase("..", false)]
        [TestCase("", false)]
        [TestCase("a b", false)]
        public void SafeId(string id, bool ok) => Assert.AreEqual(ok, CompWire.IsSafeId(id));
        [Test] public void SegRefusesUnsafe() => Assert.Throws<ArgumentException>(() => CompWire.Seg("../me"));

        [Test]
        public void ObjConvertsColyseusStyleMaps()
        {
            var inner = new Dictionary<object, object> { { "a", (byte)1 } };
            var map = new Dictionary<string, object>
            {
                { "n", (ushort)7 }, { "big", ulong.MaxValue }, { "s", "x" }, { "b", true }, { "nul", null }, { "f", 1.5 },
                { "arr", new object[] { 1, "two" } }, { "inner", inner },
            };
            var o = CompWire.Obj(map);
            Assert.AreEqual(7, (int)o["n"]);
            Assert.AreEqual("18446744073709551615", (string)o["big"]);
            Assert.IsTrue((bool)o["b"]);
            Assert.AreEqual(JTokenType.Null, o["nul"].Type);
            Assert.AreEqual(2, ((JArray)o["arr"]).Count);
            Assert.AreEqual(1, (int)o["inner"]["a"]);
        }
        [Test] public void ObjOfNonMapIsNull() { Assert.IsNull(CompWire.Obj("x")); Assert.IsNull(CompWire.Obj(null)); }
    }

    public class ModelParseTests
    {
        [Test]
        public void TribeListWrappedAndBare()
        {
            var one = "{\"tribeId\":\"builder\",\"name\":\"Builder\",\"homeTurf\":\"builder_hills\",\"perk\":\"p\",\"emoteId\":\"e\",\"phase\":2,\"ownedDistricts\":[\"builder_hills\",\"bad/id\"],\"memberCount\":4}";
            var wrapped = TribeInfo.ParseList(JToken.Parse("{\"tribes\":[" + one + "]}"));
            var bare = TribeInfo.ParseList(JToken.Parse("[" + one + "]"));
            Assert.AreEqual("builder", wrapped[0].Id);
            Assert.AreEqual(1, wrapped[0].OwnedDistricts.Count, "unsafe ids are dropped");
            Assert.AreEqual(4, bare[0].MemberCount);
        }

        [Test]
        public void DistrictParsesNullOwnerAndSince()
        {
            var d = DistrictInfo.ParseList(JToken.Parse("{\"districts\":[{\"districtId\":\"timeline\",\"name\":\"Timeline\",\"venueIds\":[\"ratio_arena\"],\"currentOwnerTribeId\":null,\"controlledSince\":null,\"defaultHolderTribeId\":\"caller\"}]}"))[0];
            Assert.IsNull(d.OwnerTribeId);
            Assert.IsNull(d.ControlledSinceMs);
            Assert.AreEqual("caller", d.DefaultHolderTribeId);
        }

        [Test]
        public void StandingsSortHighestFirstStable()
        {
            var s = DistrictStandings.Parse(JToken.Parse("{\"districtId\":\"d\",\"dayDate\":\"2026-10-08\",\"rolloverAt\":\"2026-10-09T00:05:00Z\",\"standings\":[{\"tribeId\":\"a\",\"score\":5},{\"tribeId\":\"b\",\"score\":9},{\"tribeId\":\"c\",\"score\":5}],\"myTribeId\":\"a\",\"myContribution\":null}"));
            CollectionAssert.AreEqual(new[] { "b", "a", "c" }, s.Standings.Select(x => x.TribeId).ToArray());
            Assert.IsNull(s.MyContribution);
            Assert.Greater(s.RolloverAtMs, 0);
        }

        [Test]
        public void MembershipNullBeforeFirstRollover()
        {
            var m = LeagueMembership.Parse(JToken.Parse("{\"seasonIso\":\"2026-W41\",\"membership\":null}"));
            Assert.IsFalse(m.HasMembership);
            var m2 = LeagueMembership.Parse(JToken.Parse("{\"seasonIso\":\"2026-W41\",\"membership\":{\"leagueId\":\"gold\",\"netWorthCents\":\"123400\",\"rank\":3,\"promotedFromLeagueId\":null}}"));
            Assert.IsTrue(m2.HasMembership);
            Assert.AreEqual(123400, m2.NetWorthCents);
            Assert.AreEqual(3, m2.Rank);
        }

        [Test]
        public void LeaguesSortedByOrder()
        {
            var l = LeagueInfo.ParseList(JToken.Parse("[{\"leagueId\":\"b\",\"slug\":\"b\",\"label\":\"B\",\"sortOrder\":2},{\"leagueId\":\"a\",\"slug\":\"a\",\"label\":\"A\",\"sortOrder\":1,\"promoteToSlug\":\"b\"}]"));
            Assert.AreEqual("a", l[0].Slug);
            Assert.AreEqual("b", l[0].PromoteToSlug);
        }

        [Test]
        public void SeasonResultRewards()
        {
            var r = SeasonResult.Parse(JToken.Parse("{\"seasonId\":\"s1\",\"leagueId\":\"a\",\"finalRank\":2,\"outcome\":\"promoted\",\"toLeagueId\":\"b\",\"rewards\":[{\"kind\":\"cash\",\"cashCents\":\"5000\"},{\"kind\":\"badge\",\"badgeId\":\"top3\"}]}"));
            Assert.AreEqual(2, r.Rewards.Count);
            Assert.AreEqual(5000, r.Rewards[0].CashCents);
            Assert.AreEqual("top3", r.Rewards[1].BadgeId);
        }

        [Test]
        public void BeefMatchHidesVotesWhenNull()
        {
            var m = BeefMatch.Parse(JToken.Parse("{\"matchId\":\"m1\",\"challengerUserId\":\"u1\",\"invitedUserId\":\"u2\",\"accepterUserId\":null,\"topicId\":\"beef_perps_vs_spot\",\"stakeCents\":\"2500\",\"feeBps\":500,\"status\":\"voting\",\"phaseEndsAt\":\"2026-10-08T12:00:00Z\",\"votes\":null,\"myVote\":null,\"winnerUserId\":null,\"eloChallengerPre\":1000,\"eloAccepterPre\":null,\"openedAt\":\"2026-10-08T10:00:00Z\"}"));
            Assert.IsNull(m.Votes);
            Assert.AreEqual(2500, m.StakeCents);
            Assert.IsNull(m.EloAccepterPre);
            Assert.IsTrue(m.PhaseEndsAtMs.HasValue);
        }

        [Test]
        public void BeefMatchKeepsUnknownStatusString()
        {
            var m = BeefMatch.Parse(JToken.Parse("{\"matchId\":\"m\",\"challengerUserId\":\"u\",\"topicId\":\"t\",\"stakeCents\":1,\"status\":\"rematch\"}"));
            Assert.AreEqual("rematch", m.Status);
            Assert.AreEqual(BeefPhase.Unknown, BeefRules.PhaseOf(m.Status));
        }

        [Test] public void BeefMissingRequiredThrows() => Assert.Throws<FormatException>(() => BeefMatch.Parse(JToken.Parse("{\"matchId\":\"m\",\"status\":\"open\",\"stakeCents\":1}")));

        [Test]
        public void BeefPageWrappedAndCursor()
        {
            var p = BeefPage.Parse(JToken.Parse("{\"matches\":[{\"matchId\":\"m\",\"challengerUserId\":\"u\",\"topicId\":\"t\",\"stakeCents\":1,\"status\":\"open\"}],\"nextCursor\":\"abc\"}"));
            Assert.AreEqual(1, p.Matches.Count);
            Assert.AreEqual("abc", p.NextCursor);
        }

        [Test]
        public void BoardPageReadsEntriesAndMe()
        {
            var b = BoardPage.Parse(JToken.Parse("{\"entries\":[{\"rank\":1,\"userId\":\"u\",\"alias\":\"anon\",\"value\":1500}],\"me\":{\"rank\":9,\"userId\":\"me\",\"alias\":\"x\",\"value\":3}}"));
            Assert.AreEqual(1500, b.Entries[0].Value);
            Assert.AreEqual(9, b.Me.Rank);
        }

        [Test]
        public void FarmStatusAndSybilNested()
        {
            var f = FarmStatus.Parse(JToken.Parse("{\"dayDate\":\"2026-10-08\",\"claimedToday\":true,\"nextClaimAt\":\"2026-10-09T00:00:00Z\",\"baseCents\":\"100\",\"sgtBonusBps\":1500,\"tribeBonusBps\":0,\"todayAmountCents\":115,\"dailyCapCents\":500,\"streakDays\":3,\"sybil\":{\"jailed\":false,\"jailedAt\":null,\"releaseAt\":null,\"reasonId\":null,\"bailCents\":null,\"bailable\":false}}"));
            Assert.IsTrue(f.ClaimedToday);
            Assert.AreEqual(115, f.TodayAmountCents);
            Assert.IsFalse(f.Sybil.Jailed);
        }

        [Test]
        public void SybilBarnRoster()
        {
            var b = SybilBarn.Parse(JToken.Parse("{\"count\":2,\"inmates\":[{\"userId\":\"u\",\"alias\":\"clone_1\",\"jailedAt\":\"2026-10-08T00:00:00Z\",\"releaseAt\":null}],\"nextCursor\":null}"));
            Assert.AreEqual(2, b.Count);
            Assert.AreEqual("clone_1", b.Inmates[0].Alias);
            Assert.IsNull(b.Inmates[0].ReleaseAtMs);
        }

        [Test]
        public void VenueAccessParses()
        {
            var a = VenueAccess.Parse(JToken.Parse("{\"venueId\":\"dubai_marina\",\"allowed\":false,\"reasonCode\":\"NET_WORTH_TOO_LOW\",\"requiredNetWorthCents\":\"10000000\",\"netWorthCents\":2500000,\"membershipRequired\":false,\"isMember\":false}"));
            Assert.IsFalse(a.Allowed);
            Assert.AreEqual(10000000, a.RequiredNetWorthCents);
        }

        [Test]
        public void CabalInviteHasNoCodeField()
        {
            // The proposal never returns a bearer code, and the model must not carry one even if a server sends it.
            Assert.IsNull(typeof(CabalInvite).GetField("Code"));
            Assert.IsNull(typeof(CabalInvite).GetField("InviteCode"));
            var i = CabalInvite.Parse(JToken.Parse("{\"inviteId\":\"i1\",\"hostUserId\":\"h\",\"guestUserId\":\"g\",\"status\":\"pending\",\"code\":\"SECRET\",\"expiresAt\":\"2026-10-09T00:00:00Z\"}"));
            Assert.AreEqual("i1", i.InviteId);
            foreach (var f in typeof(CabalInvite).GetFields()) Assert.AreNotEqual("SECRET", f.GetValue(i));
        }

        [Test]
        public void CabalInviteAcceptsBackendIdNames()
        {
            var i = CabalInvite.Parse(JToken.Parse("{\"id\":\"i1\",\"hostId\":\"h\",\"guestId\":\"g\"}"));
            Assert.AreEqual("h", i.HostUserId);
            Assert.AreEqual("pending", i.Status);
        }

        [Test]
        public void LlmReplyIsAlwaysAiGeneratedAndTextCapped()
        {
            var r = LlmReply.Parse(JToken.Parse("{\"replyId\":\"r\",\"archetype\":\"sleuth\",\"text\":\"" + new string('a', 5000) + "\",\"filtered\":true,\"refused\":false,\"refusalReasonId\":null,\"aiGenerated\":false,\"quota\":{\"perMinuteLimit\":5,\"remainingThisMinute\":4,\"perDayLimit\":50,\"remainingToday\":49,\"resetsAt\":\"2026-10-09T00:00:00Z\"}}"));
            Assert.IsTrue(r.AiGenerated);
            Assert.IsTrue(r.Filtered);
            Assert.LessOrEqual(r.Text.Length, 1200);
            Assert.AreEqual(49, r.Quota.RemainingToday);
        }

        [Test]
        public void LlmReplyAcceptsBackendResponseField()
        {
            var r = LlmReply.Parse(JToken.Parse("{\"response\":\"hi\"}"));
            Assert.AreEqual("hi", r.Text);
        }

        [Test]
        public void XLinkStatusNullWhenNotLinked() => Assert.IsNull(XLinkInfo.ParseStatus(JToken.Parse("{\"link\":null}")));

        [Test]
        public void XLinkSanitizesHandleAndAvatar()
        {
            var i = XLinkInfo.ParseStatus(JToken.Parse("{\"link\":{\"xHandle\":\"@ct_anon\",\"xAvatarUrl\":\"https://cdn.example/a.png\",\"verifiedAt\":\"2026-10-08T00:00:00Z\"}}"));
            Assert.AreEqual("ct_anon", i.XHandle);
            Assert.IsNotNull(i.XAvatarUrl);
            var bad = XLinkInfo.ParseStatus(JToken.Parse("{\"link\":{\"xHandle\":\"<b>x</b>\",\"xAvatarUrl\":\"http://insecure/a.png\"}}"));
            Assert.IsNull(bad.XHandle);
            Assert.IsNull(bad.XAvatarUrl);
        }
    }
}
