using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.CityLife.Tests
{
    /// <summary>The event calendar (backend PR #11, feat/events-calendar, as served on 2026-10-24).</summary>
    public sealed class EventsTests
    {
        private const string Current = @"{""items"":[{""slug"":""breakpoint-2026"",""title"":""Breakpoint 2026"",""kind"":""conference"",
            ""startsAt"":""2026-11-15T00:00:00.000Z"",""endsAt"":""2026-11-18T00:00:00.000Z"",""city"":""London"",""realVenue"":""Olympia London"",
            ""ingameVenueId"":""conference_hall"",""skinId"":""london_2026"",""streamUrl"":null,""sourceUrl"":""https://luma.com/breakpoint2026"",""status"":""upcoming""},
            {""slug"":""Bad Slug"",""startsAt"":""2026-11-15T00:00:00Z"",""endsAt"":""2026-11-18T00:00:00Z""},
            {""slug"":""backwards"",""startsAt"":""2026-11-18T00:00:00Z"",""endsAt"":""2026-11-15T00:00:00Z""}],""serverNow"":1792800000000}";

        private const string Details = @"{""slug"":""breakpoint-2026"",""title"":""Breakpoint 2026"",""kind"":""conference"",""startsAt"":""2026-11-15T00:00:00Z"",
            ""endsAt"":""2026-11-18T00:00:00Z"",""city"":""London"",""realVenue"":""Olympia London"",""ingameVenueId"":""conference_hall"",""skinId"":""london_2026"",
            ""streamUrl"":""http://not-https.example"",""sourceUrl"":""https://luma.com/breakpoint2026"",""status"":""live"",
            ""quest"":{""steps"":[{""step"":""booths"",""title"":""Visit 3 booths"",""kind"":""interaction"",""required"":3,""done"":3,""complete"":true},
            {""step"":""trivia"",""title"":""Answer the keynote trivia"",""kind"":""trivia"",""required"":3,""done"":1,""complete"":false,
              ""questions"":[{""id"":""bp26.q_city"",""prompt"":""Which city hosts Breakpoint 2026?"",""answered"":true,""choices"":[{""id"":""a"",""label"":""London""},{""id"":""b"",""label"":""Abu Dhabi""}]}]},
            {""step"":""nods"",""title"":""Find 5 event nods"",""kind"":""interaction"",""required"":5,""done"":0,""complete"":false}],
            ""completed"":false,""reward"":{""cashCents"":20000,""itemKey"":""lanyard_breakpoint_2026"",""claimedAt"":null}}}";

        [Test]
        public void CalendarParsesValidEventsAndServerTime()
        {
            var list = CalendarEvent.ParseList(JToken.Parse(Current), out long now);
            Assert.AreEqual(1792800000000L, now);
            Assert.AreEqual(1, list.Count);
            var e = list[0];
            Assert.AreEqual(EventIds.Breakpoint, e.Slug);
            Assert.AreEqual(EventIds.LondonSkin, e.SkinId);
            Assert.AreEqual(EventIds.Venue, e.VenueId);
            Assert.IsNull(e.StreamUrl);
            Assert.AreEqual(1794700800000L, e.StartsAtMs);       // 2026-11-15T00:00Z
            Assert.AreEqual(3 * 86_400_000L, e.EndsAtMs - e.StartsAtMs);
            Assert.IsTrue(e.IsUpcoming(now));
            Assert.IsTrue(e.IsLive(e.StartsAtMs));
            Assert.IsFalse(e.IsLive(e.EndsAtMs));
            Assert.AreEqual("15–17 Nov 2026", EventWire.Dates(e));
        }

        [Test]
        public void DatesParseFromStringsOrParsedDateTokens()
        {
            var t = JToken.Parse(@"{""a"":""2026-11-15T00:00:00Z""}", new Newtonsoft.Json.Linq.JsonLoadSettings());
            Assert.AreEqual(1794700800000L, EventWire.Ms(t["a"]));
            var o = new JObject { ["d"] = new JValue(new System.DateTime(2026, 11, 15, 0, 0, 0, System.DateTimeKind.Utc)) };
            Assert.AreEqual(1794700800000L, EventWire.Ms(o["d"]));
            Assert.AreEqual(1234L, EventWire.Ms(new JValue(1234L)));
            Assert.AreEqual(0L, EventWire.Ms(JValue.CreateNull()));
        }

        [Test]
        public void DetailsParseQuestChainAndRefuseNonHttpsStream()
        {
            var d = EventDetails.Parse(JToken.Parse(Details));
            Assert.IsNull(d.Event.StreamUrl, "only https link-outs");
            Assert.AreEqual(3, d.Quest.Steps.Count);
            Assert.AreEqual("trivia", d.Quest.Current.Step);
            Assert.IsTrue(d.Quest.Unlocked("trivia"));
            Assert.IsFalse(d.Quest.Unlocked("nods"));
            var trivia = d.Quest.Find("trivia");
            Assert.IsTrue(trivia.IsTrivia);
            Assert.AreEqual(1, trivia.Questions.Count);
            Assert.IsTrue(trivia.Questions[0].Answered);
            Assert.AreEqual(2, trivia.Questions[0].Choices.Count);
            Assert.AreEqual(20000, d.Quest.RewardCents);
            Assert.AreEqual(EventIds.Lanyard, d.Quest.RewardItemKey);
            Assert.IsNull(d.Quest.ClaimedAtMs);
        }

        [Test]
        public void ProgressReplyCarriesAcceptedAndJustCompleted()
        {
            var r = EventProgressResult.Parse(JToken.Parse(@"{""slug"":""breakpoint-2026"",""accepted"":true,""justCompleted"":true,""completed"":true,
                ""steps"":[{""step"":""nods"",""title"":""Find 5"",""kind"":""interaction"",""required"":5,""done"":5,""complete"":true}],
                ""reward"":{""cashCents"":20000,""itemKey"":""lanyard_breakpoint_2026"",""claimedAt"":""2026-11-16T10:00:00Z""}}"));
            Assert.IsTrue(r.Accepted);
            Assert.IsTrue(r.JustCompleted);
            Assert.IsTrue(r.Quest.Completed);
            Assert.IsNotNull(r.Quest.ClaimedAtMs);
        }

        [Test]
        public void RealtimeSignalsParse()
        {
            var live = EventSignal.From("event_live", new Dictionary<string, object>
            {
                ["slug"] = "breakpoint-2026", ["title"] = "Breakpoint 2026", ["skinId"] = "london_2026", ["ingameVenueId"] = "conference_hall",
                ["streamUrl"] = "https://example.org/live", ["endsAt"] = 1794960000000L,
            });
            Assert.IsTrue(live.Live);
            Assert.AreEqual("https://example.org/live", live.StreamUrl);
            Assert.AreEqual(1794960000000L, live.EndsAtMs);
            Assert.IsFalse(EventSignal.From("event_ended", new Dictionary<string, object> { ["slug"] = "breakpoint-2026" }).Live);
            Assert.IsNull(EventSignal.From("event_live", new Dictionary<string, object> { ["slug"] = "../x" }));
            Assert.IsNull(EventSignal.From("delivery_out", new Dictionary<string, object> { ["slug"] = "breakpoint-2026" }));
        }

        [Test]
        public void CountdownsAndIds()
        {
            Assert.AreEqual("21d 04:12:09", EventWire.Countdown(((21L * 24 + 4) * 3600 + 12 * 60 + 9) * 1000));
            Assert.AreEqual("00:00:59", EventWire.Countdown(59_999));
            Assert.AreEqual("00:00:00", EventWire.Countdown(-5));
            Assert.AreEqual("3d 2h", EventWire.Short((3L * 24 + 2) * 3_600_000));
            Assert.AreEqual("now", EventWire.Short(0));
            Assert.AreEqual("bp26.booth_6", EventIds.Booth(5));
            Assert.AreEqual("bp26.nod_1", EventIds.Nod(0));
            Assert.IsTrue(EventIds.SafeProof("bp26.q_city"));
            Assert.IsFalse(EventIds.SafeProof("bp26/q"));
            var e = new CalendarEvent { StartsAtMs = 0, EndsAtMs = 3 * 86_400_000L };
            Assert.AreEqual((1, 3), e.Day(1));
            Assert.AreEqual((3, 3), e.Day(3 * 86_400_000L - 1));
        }

        [Test]
        public void IrlCheckinBoardAndFixParse()
        {
            var r = IrlCheckinResult.Parse(JToken.Parse(@"{""slug"":""breakpoint-2026"",""checkedIn"":true,""checkedInAt"":""2026-11-16T10:00:00Z"",""visible"":false,
                ""firstCheckin"":true,""reward"":{""cashCents"":10000,""itemKeys"":[""irl_badge_breakpoint_2026"",""lanyard_breakpoint_2026_gold""]}}"));
            Assert.IsTrue(r.FirstCheckin);
            Assert.IsFalse(r.Visible);
            Assert.AreEqual(10000, r.RewardCents);
            CollectionAssert.AreEqual(new[] { EventIds.IrlBadge, EventIds.GoldLanyard }, r.RewardItems);
            Assert.AreEqual(0, IrlCheckinResult.Parse(JToken.Parse(@"{""checkedIn"":true,""firstCheckin"":false,""reward"":null}"))!.RewardCents);
            var board = IrlAttendee.ParseList(JToken.Parse(@"{""items"":[{""alias"":""anon"",""look"":{""skinTone"":""skin_02"",""accId"":""acc_none""},""tribeId"":null,""avatarId"":null,""checkedInAt"":""2026-11-16T10:00:00Z""},{""alias"":""""}],""next"":null}"), out var next);
            Assert.AreEqual(1, board.Count);
            Assert.AreEqual("skin_02", board[0].Look[0]);
            Assert.IsNull(next);
            var fix = IrlFix.Parse("ok,51.496390,-0.209720,12.5,false", out var err);
            Assert.IsNull(err);
            Assert.AreEqual(51.49639, fix.Value.Lat, 1e-6);
            Assert.IsFalse(fix.Value.IsMock);
            Assert.IsNull(IrlFix.Parse("err,permission", out err));
            Assert.AreEqual("permission", err);
            Assert.IsNull(IrlFix.Parse("", out err));
            Assert.AreEqual("pending", err);
            Assert.IsTrue(IrlFix.Parse("ok,1,2,3,true", out _).Value.IsMock);
        }

        [Test]
        public void FollowupsParseDoneIdsTriesCooldownsIrlStateAndHandles()
        {
            var d = EventDetails.Parse(JToken.Parse(@"{""slug"":""breakpoint-2026"",""startsAt"":""2026-11-15T00:00:00Z"",""endsAt"":""2026-11-18T00:00:00Z"",""status"":""live"",
                ""quest"":{""steps"":[{""step"":""booths"",""required"":3,""done"":2,""complete"":false,""doneIds"":[""bp26.booth_1"",""bp26.booth_4"",""bad id!""]},
                {""step"":""trivia"",""required"":3,""done"":0,""complete"":false,""questions"":[{""id"":""bp26.q_month"",""prompt"":""When?"",""answered"":false,
                  ""triedChoices"":[""a""],""retryAt"":""2026-11-16T10:01:00Z"",""choices"":[{""id"":""a"",""label"":""Oct""},{""id"":""c"",""label"":""Nov""}]}]}]},
                ""irl"":{""available"":true,""checkedIn"":true,""checkedInAt"":""2026-11-16T09:00:00Z"",""visible"":true}}"));
            var booths = d.Quest.Find("booths");
            CollectionAssert.AreEquivalent(new[] { "bp26.booth_1", "bp26.booth_4" }, booths.DoneIds);
            var q = d.Quest.Find("trivia").Questions[0];
            Assert.IsFalse(q.Answered);
            CollectionAssert.AreEquivalent(new[] { "a" }, q.Tried);
            long retry = EventWire.Ms(JToken.FromObject("2026-11-16T10:01:00Z"));
            Assert.AreEqual(retry, q.RetryAtMs);
            Assert.IsTrue(q.CoolingDown(retry - 1000));
            Assert.IsFalse(q.CoolingDown(retry));
            Assert.IsTrue(d.Irl.Available && d.Irl.CheckedIn && d.Irl.Visible);
            Assert.IsNull(EventDetails.Parse(JToken.Parse(Details)).Irl);

            // 400 wrong_answer details, then 429 TRIVIA_COOLDOWN details
            long now = 1_800_000_000_000;
            var wrong = JToken.Parse(@"{""reason"":""wrong_answer"",""retryAfterSeconds"":60,""triedChoices"":[""a"",""b""]}");
            Assert.AreEqual(now + 60_000, TriviaErrors.RetryAtMs(wrong, now));
            CollectionAssert.AreEqual(new[] { "a", "b" }, TriviaErrors.Tried(wrong));
            Assert.AreEqual(retry, TriviaErrors.RetryAtMs(JToken.Parse(@"{""retryAt"":""2026-11-16T10:01:00Z"",""retryAfterSeconds"":42}"), now));

            Assert.AreEqual("0:45", EventWire.Clock(44_100));
            Assert.AreEqual("10:00", EventWire.Clock(600_000));
            Assert.AreEqual("1:00:00", EventWire.Clock(3_600_000));
            Assert.AreEqual("0:00", EventWire.Clock(-5));

            var board = IrlAttendee.ParseList(JToken.Parse(@"{""items"":[{""alias"":""me"",""handle"":""h_1"",""self"":true},{""alias"":""them"",""handle"":""Ab-9_x""},{""alias"":""evil"",""handle"":""../dm""}]}"), out _);
            Assert.IsTrue(board[0].Self);
            Assert.AreEqual("Ab-9_x", board[1].Handle);
            Assert.IsFalse(board[1].Self);
            Assert.IsNull(board[2].Handle);
        }
    }
}