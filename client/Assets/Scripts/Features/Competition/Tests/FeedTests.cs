using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Competition.Tests
{
    public class MessageParseTests
    {
        private static object P(string type, string json) => CompetitionMessages.Parse(type, JObject.Parse(json));

        [Test] public void DistrictFlipped()
        {
            var m = (DistrictFlipped)P("district_flipped", "{\"districtId\":\"timeline\",\"tribeId\":\"caller\",\"previousTribeId\":\"anon\",\"at\":123}");
            Assert.AreEqual("timeline", m.DistrictId); Assert.AreEqual("anon", m.PreviousTribeId); Assert.AreEqual(123, m.AtMs);
        }
        [Test] public void SeasonRollover() => Assert.AreEqual("s2", ((SeasonRollover)P("season_rollover", "{\"seasonId\":\"s2\",\"previousSeasonId\":\"s1\",\"at\":1}")).SeasonId);
        [Test] public void LeagueResult()
        {
            var m = (LeagueResultMsg)P("league_result", "{\"seasonId\":\"s\",\"fromLeagueId\":\"a\",\"toLeagueId\":\"b\",\"rank\":4,\"outcome\":\"promoted\"}");
            Assert.AreEqual("promoted", m.Outcome); Assert.AreEqual(4, m.Rank);
        }
        [Test] public void BeefChallenge()
        {
            var m = (BeefChallengeMsg)P("beef_challenge", "{\"matchId\":\"m\",\"fromUserId\":\"u\",\"topicId\":\"beef_perps_vs_spot\",\"stakeCents\":500,\"expiresAt\":99}");
            Assert.AreEqual(500, m.StakeCents);
        }
        [Test] public void BeefUpdateKeepsUnknownStatus()
        {
            var m = (BeefUpdateMsg)P("beef_update", "{\"matchId\":\"m\",\"status\":\"rematch\",\"challengerUserId\":\"c\"}");
            Assert.AreEqual("rematch", m.Status); Assert.IsNull(m.AccepterUserId); Assert.IsNull(m.PhaseEndsAtMs);
        }
        [Test] public void BeefResultDrawHasNoWinner()
        {
            var m = (BeefResultMsg)P("beef_result", "{\"matchId\":\"m\",\"votesChallenger\":3,\"votesAccepter\":3,\"eloDeltaChallenger\":0,\"eloDeltaAccepter\":0,\"payoutCents\":0,\"at\":5}");
            Assert.IsNull(m.WinnerUserId);
        }
        [Test] public void SybilStatusNeedsBoolean()
        {
            Assert.IsTrue(((SybilStatusMsg)P("sybil_status", "{\"jailed\":true,\"reasonId\":\"sybil_clone_farm\",\"releaseAt\":99}")).Jailed);
            Assert.IsNull(P("sybil_status", "{\"jailed\":\"yes\"}"));
            Assert.IsNull(P("sybil_status", "{}"));
        }
        [Test] public void CabalInviteCarriesNoCode()
        {
            var m = (CabalInviteMsg)P("cabal_invite", "{\"inviteId\":\"i\",\"fromUserId\":\"h\",\"expiresAt\":9,\"code\":\"SECRET\"}");
            Assert.AreEqual("i", m.InviteId);
            foreach (var f in typeof(CabalInviteMsg).GetFields()) Assert.AreNotEqual("SECRET", f.GetValue(m));
        }
        [Test] public void XLinkUpdateSanitizesHandle()
        {
            var m = (XLinkUpdateMsg)P("x_link_update", "{\"status\":\"linked\",\"handle\":\"<b>x\"}");
            Assert.AreEqual("linked", m.Status); Assert.IsNull(m.Handle);
            Assert.AreEqual("ct_anon", ((XLinkUpdateMsg)P("x_link_update", "{\"status\":\"linked\",\"handle\":\"ct_anon\"}")).Handle);
        }
        [TestCase("district_flipped", "{\"tribeId\":\"a\"}")]
        [TestCase("season_rollover", "{}")]
        [TestCase("league_result", "{\"seasonId\":\"s\"}")]
        [TestCase("beef_challenge", "{\"matchId\":\"m\"}")]
        [TestCase("beef_update", "{\"matchId\":\"m\",\"status\":\"open\"}")]
        [TestCase("beef_result", "{}")]
        [TestCase("cabal_invite", "{}")]
        [TestCase("x_link_update", "{}")]
        public void MissingRequiredFieldsDropTheMessage(string type, string json) => Assert.IsNull(P(type, json));
        [Test] public void MalformedNumbersDropTheMessage() => Assert.IsNull(P("beef_challenge", "{\"matchId\":\"m\",\"fromUserId\":\"u\",\"stakeCents\":\"abc\"}"));
        [Test] public void UnknownTypeAndNull() { Assert.IsNull(P("mystery", "{}")); Assert.IsNull(CompetitionMessages.Parse("beef_update", null)); Assert.IsFalse(CompetitionMessages.IsPhase6("mystery")); }
        [Test] public void EveryTypeIsRecognised() { foreach (var t in CompetitionMessages.Types) Assert.IsTrue(CompetitionMessages.IsPhase6(t)); Assert.AreEqual(9, CompetitionMessages.Types.Length); }
    }

    public class FeedTests
    {
        private CompetitionFeed _feed;
        private List<(string text, ToastTone tone)> _toasts;

        [SetUp]
        public void SetUp()
        {
            _toasts = new List<(string, ToastTone)>();
            _feed = new CompetitionFeed { MyUserId = () => "me", MyTribeId = () => "caller" };
            _feed.Toast = (t, k) => _toasts.Add((t, k));
        }

        private bool D(string type, string json) => _feed.Dispatch(type, new Dictionary<string, object>(JObject.Parse(json).Properties().ToDictionary(p => p.Name, p => (object)(p.Value.Type == JTokenType.Integer ? (object)(long)p.Value : p.Value.Type == JTokenType.Boolean ? (object)(bool)p.Value : (string)p.Value))));

        [Test] public void NonPhase6TypesAreIgnored() { Assert.IsFalse(_feed.Dispatch("welcome", new Dictionary<string, object>())); Assert.AreEqual(0, _toasts.Count); }
        [Test] public void GarbagePayloadIsIgnored() { Assert.IsFalse(_feed.Dispatch("beef_update", "not a map")); Assert.IsFalse(_feed.Dispatch("beef_update", null)); }

        [Test] public void DistrictFlipToMyTribeIsGood()
        {
            Assert.IsTrue(D("district_flipped", "{\"districtId\":\"timeline\",\"tribeId\":\"caller\",\"previousTribeId\":\"anon\",\"at\":100}"));
            Assert.AreEqual(ToastTone.Good, _toasts[0].tone);
            Assert.AreEqual("caller", DistrictBoard.HolderOf(_feed.Districts.Get("timeline")));
        }
        [Test] public void DistrictLostIsBad()
        {
            D("district_flipped", "{\"districtId\":\"trenches\",\"tribeId\":\"trencher\",\"previousTribeId\":\"caller\",\"at\":100}");
            Assert.AreEqual(ToastTone.Bad, _toasts[0].tone);
            StringAssert.Contains("lost", _toasts[0].text);
        }
        [Test] public void DistrictFlipBetweenOthersIsInfo()
        {
            D("district_flipped", "{\"districtId\":\"trenches\",\"tribeId\":\"trencher\",\"previousTribeId\":\"anon\",\"at\":100}");
            Assert.AreEqual(ToastTone.Info, _toasts[0].tone);
        }
        [Test] public void ToastsCanBeSwitchedOffWhenServerSendsItsOwn()
        {
            _feed.ToastsEnabled = false;
            D("season_rollover", "{\"seasonId\":\"s2\",\"previousSeasonId\":\"s1\",\"at\":1}");
            Assert.AreEqual(0, _toasts.Count);
            Assert.AreEqual("s2", _feed.CurrentSeasonId, "state still updates");
        }
        [Test] public void LeagueResultToneByOutcome()
        {
            D("league_result", "{\"seasonId\":\"s\",\"toLeagueId\":\"gold\",\"rank\":2,\"outcome\":\"promoted\"}");
            D("league_result", "{\"seasonId\":\"s\",\"toLeagueId\":\"silver\",\"rank\":90,\"outcome\":\"relegated\"}");
            D("league_result", "{\"seasonId\":\"s\",\"toLeagueId\":\"silver\",\"rank\":40,\"outcome\":\"stayed\"}");
            CollectionAssert.AreEqual(new[] { ToastTone.Good, ToastTone.Bad, ToastTone.Info }, _toasts.Select(t => t.tone).ToArray());
            Assert.AreEqual("stayed", _feed.LastLeagueResult.Outcome);
        }
        [Test] public void RolloverEventFires()
        {
            SeasonRollover seen = null;
            _feed.SeasonRolloverEvent += m => seen = m;
            D("season_rollover", "{\"seasonId\":\"s2\",\"previousSeasonId\":\"s1\",\"at\":1}");
            Assert.AreEqual("s2", seen.SeasonId);
        }
        [Test] public void BeefChallengeQueuedAndClearedWhenMatchFinishes()
        {
            D("beef_challenge", "{\"matchId\":\"m1\",\"fromUserId\":\"u\",\"topicId\":\"t\",\"stakeCents\":100,\"expiresAt\":9}");
            D("beef_challenge", "{\"matchId\":\"m1\",\"fromUserId\":\"u\",\"topicId\":\"t\",\"stakeCents\":100,\"expiresAt\":9}");
            Assert.AreEqual(1, _feed.BeefChallenges.Count, "no duplicates");
            D("beef_update", "{\"matchId\":\"m1\",\"status\":\"cancelled\",\"challengerUserId\":\"u\"}");
            Assert.AreEqual(0, _feed.BeefChallenges.Count);
        }
        [Test] public void BeefResultToastsOnlyForParticipants()
        {
            D("beef_update", "{\"matchId\":\"m1\",\"status\":\"voting\",\"challengerUserId\":\"me\",\"accepterUserId\":\"other\"}");
            D("beef_result", "{\"matchId\":\"m1\",\"winnerUserId\":\"me\",\"votesChallenger\":5,\"votesAccepter\":2,\"eloDeltaChallenger\":12,\"eloDeltaAccepter\":-12,\"payoutCents\":1900,\"at\":5}");
            Assert.AreEqual(1, _toasts.Count);
            Assert.AreEqual(ToastTone.Good, _toasts[0].tone);
            StringAssert.Contains("$19.00", _toasts[0].text);
            D("beef_update", "{\"matchId\":\"m2\",\"status\":\"voting\",\"challengerUserId\":\"x\",\"accepterUserId\":\"y\"}");
            D("beef_result", "{\"matchId\":\"m2\",\"winnerUserId\":\"x\",\"votesChallenger\":1,\"votesAccepter\":0,\"eloDeltaChallenger\":1,\"eloDeltaAccepter\":-1,\"payoutCents\":10,\"at\":6}");
            Assert.AreEqual(1, _toasts.Count, "spectators get no result toast");
        }
        [Test] public void BeefLossAndDraw()
        {
            D("beef_update", "{\"matchId\":\"m1\",\"status\":\"voting\",\"challengerUserId\":\"me\",\"accepterUserId\":\"o\"}");
            D("beef_result", "{\"matchId\":\"m1\",\"winnerUserId\":\"o\",\"votesChallenger\":1,\"votesAccepter\":2,\"eloDeltaChallenger\":-9,\"eloDeltaAccepter\":9,\"payoutCents\":0,\"at\":5}");
            Assert.AreEqual(ToastTone.Bad, _toasts[0].tone);
            D("beef_update", "{\"matchId\":\"m3\",\"status\":\"voting\",\"challengerUserId\":\"me\",\"accepterUserId\":\"o\"}");
            D("beef_result", "{\"matchId\":\"m3\",\"votesChallenger\":2,\"votesAccepter\":2,\"eloDeltaChallenger\":0,\"eloDeltaAccepter\":0,\"payoutCents\":0,\"at\":5}");
            Assert.AreEqual(ToastTone.Info, _toasts[1].tone);
        }
        [Test] public void DuplicateDeliveryFromCityAndVenueRoomsIsIdempotent()
        {
            int updates = 0, results = 0;
            _feed.BeefUpdateEvent += _ => updates++;
            _feed.BeefResultEvent += _ => results++;
            const string upd = "{\"matchId\":\"m1\",\"status\":\"voting\",\"challengerUserId\":\"me\",\"accepterUserId\":\"o\"}";
            const string res = "{\"matchId\":\"m1\",\"winnerUserId\":\"me\",\"votesChallenger\":2,\"votesAccepter\":1,\"eloDeltaChallenger\":5,\"eloDeltaAccepter\":-5,\"payoutCents\":100,\"at\":5}";
            D("beef_update", upd); D("beef_update", upd);
            D("beef_result", res); D("beef_result", res);
            Assert.AreEqual(1, updates);
            Assert.AreEqual(1, results);
            Assert.AreEqual(1, _toasts.Count, "one result toast, not two");
        }
        [Test] public void SybilStatusUpdatesStateAndToasts()
        {
            D("sybil_status", "{\"jailed\":true,\"reasonId\":\"sybil_clone_farm\",\"releaseAt\":999}");
            Assert.IsTrue(_feed.Sybil.Jailed);
            Assert.AreEqual(ToastTone.Bad, _toasts[0].tone);
            D("sybil_status", "{\"jailed\":false}");
            Assert.IsFalse(_feed.Sybil.Jailed);
            Assert.AreEqual(ToastTone.Good, _toasts[1].tone);
        }
        [Test] public void CabalInviteDedupedByIdAndNoCodeStored()
        {
            D("cabal_invite", "{\"inviteId\":\"i1\",\"fromUserId\":\"h\",\"expiresAt\":9}");
            D("cabal_invite", "{\"inviteId\":\"i1\",\"fromUserId\":\"h\",\"expiresAt\":9}");
            D("cabal_invite", "{\"inviteId\":\"i2\",\"fromUserId\":\"h\",\"expiresAt\":9}");
            Assert.AreEqual(2, _feed.CabalInvites.Count);
        }
        [Test] public void XLinkUpdateStates()
        {
            D("x_link_update", "{\"status\":\"linked\",\"handle\":\"ct_anon\"}");
            Assert.AreEqual(ToastTone.Good, _toasts[0].tone);
            D("x_link_update", "{\"status\":\"failed\",\"reasonId\":\"access_denied\"}");
            Assert.AreEqual(ToastTone.Bad, _toasts[1].tone);
            Assert.AreEqual("failed", _feed.LastXLink.Status);
        }
        [Test] public void ResetClearsEverything()
        {
            D("sybil_status", "{\"jailed\":true}");
            D("district_flipped", "{\"districtId\":\"d\",\"tribeId\":\"a\",\"at\":1}");
            D("cabal_invite", "{\"inviteId\":\"i\",\"expiresAt\":1}");
            _feed.Reset();
            Assert.IsNull(_feed.Sybil); Assert.AreEqual(0, _feed.Districts.Flags.Count); Assert.AreEqual(0, _feed.CabalInvites.Count); Assert.IsFalse(_feed.Districts.Loaded);
        }
        [Test] public void ColyseusStyleObjectKeyMapsWork()
        {
            var map = new Dictionary<object, object> { { "districtId", "d" }, { "tribeId", "a" }, { "at", (uint)5 } };
            Assert.IsTrue(_feed.Dispatch("district_flipped", map));
        }
    }

    public class BoardTests
    {
        private static DistrictInfo Info(string id, string owner, long? since, params string[] venues) =>
            new DistrictInfo { Id = id, Name = id.ToUpperInvariant(), OwnerTribeId = owner, ControlledSinceMs = since, DefaultHolderTribeId = "anon", VenueIds = venues.ToList() };

        [Test] public void NotLoadedUntilServerAnswers() { var b = new DistrictBoard(); Assert.IsFalse(b.Loaded); Assert.AreEqual(0, b.Flags.Count); }
        [Test] public void ListFillsFlagsInServerOrder()
        {
            var b = new DistrictBoard();
            int changed = 0;
            b.Changed += () => changed++;
            b.ApplyList(new[] { Info("timeline", "caller", 10, "ratio_arena"), Info("trenches", null, null, "perp_pit") });
            Assert.IsTrue(b.Loaded);
            CollectionAssert.AreEqual(new[] { "timeline", "trenches" }, b.Flags.Select(f => f.DistrictId).ToArray());
            Assert.AreEqual("timeline", b.ForVenue("ratio_arena").DistrictId);
            Assert.IsNull(b.ForVenue("nowhere"));
            Assert.AreEqual("anon", DistrictBoard.HolderOf(b.Get("trenches")), "default holder when unowned");
            Assert.AreEqual(1, changed);
        }
        [Test] public void FlipUpdatesOwnerAndMarksFresh()
        {
            var b = new DistrictBoard();
            b.ApplyList(new[] { Info("timeline", "caller", 10) });
            b.ApplyFlip(new DistrictFlipped { DistrictId = "timeline", TribeId = "builder", PreviousTribeId = "caller", AtMs = 500 });
            var f = b.Get("timeline");
            Assert.AreEqual("builder", f.OwnerTribeId); Assert.AreEqual("caller", f.PreviousTribeId); Assert.AreEqual(500, f.FlippedAtMs); Assert.AreEqual(500, f.SinceMs);
        }
        [Test] public void OlderListDoesNotRollBackALiveFlip()
        {
            var b = new DistrictBoard();
            b.ApplyList(new[] { Info("timeline", "caller", 10) });
            b.ApplyFlip(new DistrictFlipped { DistrictId = "timeline", TribeId = "builder", AtMs = 500 });
            b.ApplyList(new[] { Info("timeline", "caller", 400) });
            Assert.AreEqual("builder", b.Get("timeline").OwnerTribeId);
            b.ApplyList(new[] { Info("timeline", "anon", 900) });
            Assert.AreEqual("anon", b.Get("timeline").OwnerTribeId, "a newer server snapshot wins");
        }
        [Test] public void FlipForUnknownDistrictIsKept()
        {
            var b = new DistrictBoard();
            b.ApplyFlip(new DistrictFlipped { DistrictId = "new_district", TribeId = "a", AtMs = 1 });
            Assert.AreEqual("New district", b.Get("new_district").Name);
        }
        [Test] public void ListDropsDistrictsTheServerRemoved()
        {
            var b = new DistrictBoard();
            b.ApplyList(new[] { Info("a", null, null), Info("b", null, null) });
            b.ApplyList(new[] { Info("b", null, null) });
            Assert.AreEqual(1, b.Flags.Count);
        }

        [Test] public void ArenaIdleLine() => CollectionAssert.AreEqual(new[] { CompStrings.T("arena.idle") }, new ArenaBoard().Lines(null, 0));
        [Test] public void ArenaNeverShowsVotesBeforeResult()
        {
            var a = new ArenaBoard();
            a.ApplyUpdate(new BeefUpdateMsg { MatchId = "m", Status = "voting", ChallengerUserId = "c", AccepterUserId = "a", PhaseEndsAtMs = 5000 });
            var lines = a.Lines(id => id == "c" ? "alice" : "bob", 1000);
            Assert.AreEqual("alice  vs  bob", lines[0]);
            Assert.IsFalse(lines.Any(l => l.Contains("votes")));
            StringAssert.Contains("4s", lines.Last());
        }
        [Test] public void ArenaShowsResultOnceKnown()
        {
            var a = new ArenaBoard();
            a.ApplyUpdate(new BeefUpdateMsg { MatchId = "m", Status = "voting", ChallengerUserId = "c", AccepterUserId = "a" });
            a.ApplyResult(new BeefResultMsg { MatchId = "m", WinnerUserId = "a", VotesChallenger = 3, VotesAccepter = 9 });
            var lines = a.Lines(id => id, 0);
            Assert.IsTrue(lines.Any(l => l.Contains("3 - 9")));
            Assert.IsTrue(lines.Any(l => l.Contains("a wins")));
        }
        [Test] public void ArenaDrawAndFeaturedPrefersLive()
        {
            var a = new ArenaBoard();
            a.ApplyUpdate(new BeefUpdateMsg { MatchId = "old", Status = "closed", ChallengerUserId = "c" });
            a.ApplyUpdate(new BeefUpdateMsg { MatchId = "live", Status = "active", ChallengerUserId = "c", PhaseEndsAtMs = 10 });
            Assert.AreEqual("live", a.Featured().MatchId);
            a.ApplyUpdate(new BeefUpdateMsg { MatchId = "live", Status = "closed", ChallengerUserId = "c" });
            a.ApplyResult(new BeefResultMsg { MatchId = "live" });
            Assert.IsTrue(a.Lines(null, 0).Contains(CompStrings.T("arena.draw")));
        }
        [Test] public void ArenaKeepsBoundedMemory()
        {
            var a = new ArenaBoard();
            for (int i = 0; i < 20; i++) a.ApplyUpdate(new BeefUpdateMsg { MatchId = "m" + i, Status = "open", ChallengerUserId = "c" });
            Assert.IsNull(a.Find("m0")); Assert.IsNotNull(a.Find("m19"));
        }
    }
}
