using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.SocialFeatures.Tests
{
    /// <summary>Backend #33 (group chats): real local replies from backend main a19582a (2026-10-26, three dev players),
    /// the client's pure state, rules and the block / plain-text safety rules (security review 2026-10-26).</summary>
    public sealed class GroupTests
    {
        private const string ListJson = @"{""groups"":[{""groupId"":""01a178f9-af5b-7a36-aac0-2d1df75d36da"",""kind"":""alpha"",""name"":""Frog Calls"",""role"":""owner"",""memberCount"":2,""memberCap"":50,""unread"":1,""muted"":false,""mutedUntil"":null,""lastMessage"":{""topic"":""chat"",""authorAlias"":""lanecC3"",""preview"":""wen moon"",""at"":""2026-10-26T16:10:10.821Z""},""booth"":{""venueId"":""cabal_lounge"",""boothId"":""booth_2""}},{""groupId"":""01a178f9-6b1a-79eb-a89b-0703e533d176"",""kind"":""friends"",""name"":""Desk Crew"",""role"":""owner"",""memberCount"":1,""memberCap"":12,""unread"":0,""muted"":false,""mutedUntil"":null,""lastMessage"":null,""booth"":null},{""groupId"":""01a178f8-85a2-7357-ae7f-88e06964cd18"",""kind"":""tribe"",""name"":""Trencher tribe"",""role"":""member"",""memberCount"":2,""memberCap"":200,""unread"":0,""muted"":false,""mutedUntil"":null,""lastMessage"":null,""booth"":null}]}";
        private const string PatchJson = @"{""groupId"":""01a178f9-af5b-7a36-aac0-2d1df75d36da"",""kind"":""alpha"",""name"":""Frog Calls"",""rules"":""No shilling <script>"",""role"":""owner"",""memberCount"":2,""memberCap"":50,""topics"":[""calls"",""chat"",""wins""],""slowMode"":{""chat"":30},""owner"":{""userId"":""01a1751e-4395-7f39-aabe-ae41cf57e656"",""alias"":""lanec_rugger""},""trust"":{""hitRate"":0,""avgReturnBps"":0,""maxDrawdownBps"":0,""heldBagPct"":1},""booth"":{""venueId"":""cabal_lounge"",""boothId"":""booth_2""},""hereWithYou"":0,""muted"":false}";
        private const string CallJson = @"{""messageId"":""01a178f9-f948-7e41-aa5d-dad90cfd4a6f"",""groupId"":""01a178f9-af5b-7a36-aac0-2d1df75d36da"",""topic"":""calls"",""authorUserId"":""01a1751e-4395-7f39-aabe-ae41cf57e656"",""authorAlias"":""lanec_rugger"",""text"":""to the moon"",""card"":{""type"":""call"",""callId"":""01a178f9-f908-7a58-a6f9-b168784cc22f"",""coinId"":""01a175b4-b1ca-74ce-ad01-87f296abdd94"",""ticker"":""TLARN"",""entryMicros"":""9210955082845465"",""targetMicros"":""18421910165690930"",""stopMicros"":""6447668557991825"",""status"":""open"",""priceMicros"":""9210955082845465"",""deepLink"":""ctlife://coin/01a175b4-b1ca-74ce-ad01-87f296abdd94""},""at"":""2026-10-26T16:10:10.565Z""}";
        private const string PageJson = @"{""messages"":[{""messageId"":""01a178f9-fa06-71ff-a2f4-594c3c095eb9"",""groupId"":""01a178f9-af5b-7a36-aac0-2d1df75d36da"",""topic"":""chat"",""authorUserId"":""01a178f9-92d9-7ec4-a407-ca082ecf5292"",""authorAlias"":""lanecC3"",""text"":""wen moon"",""card"":null,""at"":""2026-10-26T16:10:10.821Z""},{""messageId"":""01a178f9-cdfd-71d6-a38c-c2384b110605"",""groupId"":""01a178f9-af5b-7a36-aac0-2d1df75d36da"",""topic"":""chat"",""authorUserId"":""01a1751e-4395-7f39-aabe-ae41cf57e656"",""authorAlias"":""lanec_rugger"",""text"":""gm frogs <b>bold</b> see [link]"",""card"":null,""at"":""2026-10-26T16:09:59.547Z""}],""nextBefore"":null}";
        private const string LinkJson = @"{""code"":""hyv3fkFEB5"",""deepLink"":""ctlife://invite/hyv3fkFEB5"",""groupId"":""01a178f9-af5b-7a36-aac0-2d1df75d36da"",""expiresAt"":""2026-10-27T16:09:51.861Z"",""maxUses"":5,""uses"":0}";
        private const string PreviewJson = @"{""code"":""hyv3fkFEB5"",""groupId"":""01a178f9-af5b-7a36-aac0-2d1df75d36da"",""groupName"":""Frog Calls"",""kind"":""alpha"",""memberCount"":1,""memberCap"":50,""from"":{""userId"":""01a1751e-4395-7f39-aabe-ae41cf57e656"",""alias"":""lanec_rugger""},""owner"":{""userId"":""01a1751e-4395-7f39-aabe-ae41cf57e656"",""alias"":""lanec_rugger""},""trust"":{""hitRate"":0,""avgReturnBps"":0,""maxDrawdownBps"":0,""heldBagPct"":0},""expiresAt"":""2026-10-27T16:09:51.861Z"",""valid"":true}";
        private const string MembersJson = @"{""members"":[{""userId"":""01a1751e-4395-7f39-aabe-ae41cf57e656"",""alias"":""lanec_rugger"",""role"":""owner"",""joinedAt"":""2026-10-26T16:09:51.707Z""},{""userId"":""01a178f9-92d9-7ec4-a407-ca082ecf5292"",""alias"":""lanecC3"",""role"":""member"",""joinedAt"":""2026-10-26T16:09:51.991Z""}]}";
        private const string BoothsJson = @"{""booths"":[{""venueId"":""cabal_lounge"",""boothId"":""booth_1"",""group"":null},{""venueId"":""cabal_lounge"",""boothId"":""booth_2"",""group"":null},{""venueId"":""cabal_lounge"",""boothId"":""booth_3"",""group"":null},{""venueId"":""cabal_lounge"",""boothId"":""booth_4"",""group"":null},{""venueId"":""cabal_lounge"",""boothId"":""booth_5"",""group"":null},{""venueId"":""cabal_lounge"",""boothId"":""booth_6"",""group"":null}]}";
        private const string NotOwnerJson = @"{""error"":{""code"":""FORBIDDEN"",""message"":""Caller is not allowed to perform this action."",""details"":{""reason"":""calls_owner_only""}}}";
        private const string NoProfitJson = @"{""error"":{""code"":""VALIDATION_FAILED"",""message"":""Request body or params failed validation."",""details"":{""reason"":""no realized profit on this coin""}}}";

        private const string Me = "01a1751e-4395-7f39-aabe-ae41cf57e656";      // lanec_rugger, the alpha owner
        private const string C3 = "01a178f9-92d9-7ec4-a407-ca082ecf5292";      // lanecC3, a member
        private const string Alpha = "01a178f9-af5b-7a36-aac0-2d1df75d36da";

        private static GroupMessage Msg(string id, string author, string topic, string text, long at, string alias = "x") =>
            new GroupMessage { MessageId = id, GroupId = Alpha, Topic = topic, AuthorUserId = author, AuthorAlias = alias, Text = text, AtMs = at, GroupName = "Frog Calls" };

        [Test]
        public void List_ParsesKindsCapsLastLineAndBooth()
        {
            var list = GroupParse.Summaries(JToken.Parse(ListJson));
            Assert.AreEqual(3, list.Count);
            var a = list[0];
            Assert.AreEqual(GroupKinds.Alpha, a.Kind);
            Assert.AreEqual(50, a.MemberCap);
            Assert.AreEqual(1, a.Unread);
            Assert.AreEqual("wen moon", a.Last.Preview);
            Assert.AreEqual("booth_2", a.Booth.BoothId);
            Assert.AreEqual(12, list[1].MemberCap);
            Assert.AreEqual(GroupKinds.Tribe, list[2].Kind);
            Assert.AreEqual(200, list[2].MemberCap);
            Assert.IsFalse(GroupRules.CanLeave(list[2].Kind), "tribe chats are muted, not left");
        }

        [Test]
        public void Info_KeepsRulesLiteral_AndSlowModeIsSentWhole()
        {
            var info = GroupParse.Info(JToken.Parse(PatchJson));
            Assert.AreEqual("No shilling <script>", info.Rules, "plain text: rendered literally with rich text off");
            CollectionAssert.AreEqual(new[] { "calls", "chat", "wins" }, info.Topics);
            Assert.AreEqual(30, info.SlowSeconds("chat"));
            Assert.AreEqual(0, info.SlowSeconds("calls"), "the server lists only topics with a slow mode");
            var body = GroupRules.SlowModeBody(info, "wins", 999);
            Assert.AreEqual(30, (int)body["chat"]);
            Assert.AreEqual(0, (int)body["calls"]);
            Assert.AreEqual(300, (int)body["wins"], "clamped to 300 s");
            Assert.AreEqual(3, body.Count, "backend main refuses a partial slowMode map");
        }

        [Test]
        public void CallCard_MicrosAboveDoublePrecision_AndLiveStatus()
        {
            var m = GroupParse.Message(JToken.Parse(CallJson));
            Assert.AreEqual(GroupTopics.Calls, m.Topic);
            Assert.AreEqual(GroupCard.CallType, m.Card.Type);
            Assert.AreEqual(9210955082845465L, m.Card.EntryMicros, "above 2^53: parsed exactly as long");
            Assert.AreEqual(18421910165690930L, m.Card.TargetMicros);
            Assert.AreEqual("TLARN", m.Card.Ticker);
            Assert.IsTrue(m.Card.Open);
            Assert.AreEqual(0, m.Card.NowPct, 1e-9);
            Assert.AreEqual("ctlife://coin/01a175b4-b1ca-74ce-ad01-87f296abdd94", m.Card.DeepLink);
        }

        [Test]
        public void Messages_TextIsLiteral_AliasesLoseMarkup_BadRowsDropped()
        {
            var page = GroupParse.Page(JToken.Parse(PageJson));
            Assert.AreEqual(2, page.Messages.Count);
            Assert.AreEqual("gm frogs <b>bold</b> see [link]", page.Messages[1].Text);
            Assert.IsNull(page.NextBefore);
            var bad = GroupParse.Message(JToken.Parse(@"{""messageId"":""../x"",""groupId"":""g"",""topic"":""chat"",""authorUserId"":""u"",""text"":""hi""}"));
            Assert.IsNull(bad, "unsafe ids never reach a path or a list");
            var spoof = GroupParse.Message(JToken.Parse(@"{""messageId"":""m1"",""groupId"":""g"",""topic"":""chat"",""authorUserId"":""u"",""authorAlias"":""<color=red>admin</color>"",""text"":""hi""}"));
            Assert.AreEqual("color=redadmin/color", spoof.AuthorAlias);
            Assert.IsNull(GroupParse.Message(JToken.Parse(@"{""messageId"":""m1"",""groupId"":""g"",""topic"":""dm"",""authorUserId"":""u"",""text"":""hi""}")), "unknown topic");
            var card = GroupParse.Card(JToken.Parse(@"{""type"":""call"",""coinId"":""c1"",""ticker"":""<b>$wif</b>"",""deepLink"":""https://evil.example""}"));
            Assert.AreEqual("BWIFB", card.Ticker);
            Assert.AreEqual("ctlife://coin/c1", card.DeepLink, "only the coin's own link is followed");
        }

        [Test]
        public void Invites_LinkPreviewAndDeepLinks()
        {
            var link = GroupParse.Link(JToken.Parse(LinkJson));
            Assert.AreEqual("ctlife://invite/hyv3fkFEB5", link.DeepLink);
            Assert.AreEqual(5, link.MaxUses);
            var p = GroupParse.Preview(JToken.Parse(PreviewJson));
            Assert.AreEqual("Frog Calls", p.GroupName);
            Assert.AreEqual("lanec_rugger", p.Owner.Alias);
            Assert.IsTrue(p.Valid);
            Assert.IsFalse(p.Full);
            Assert.IsTrue(GroupLinks.TryParseInvite("ctlife://invite/hyv3fkFEB5", out var code));
            Assert.AreEqual("hyv3fkFEB5", code);
            Assert.IsTrue(GroupLinks.TryParseInvite("ctlife://invite/hyv3fkFEB5?src=x", out code));
            Assert.IsFalse(GroupLinks.TryParseInvite("ctlife://invite/<b>", out _));
            Assert.IsFalse(GroupLinks.TryParseInvite("ctlife://invite/../../groups", out _));
            Assert.IsFalse(GroupLinks.TryParseInvite("https://ctlife.fun/invite/abcd", out _));
        }

        [Test]
        public void MembersAndBooths()
        {
            var members = GroupParse.Members(JToken.Parse(MembersJson));
            Assert.AreEqual(GroupRoles.Owner, members[0].Role);
            var booths = GroupParse.Booths(JToken.Parse(BoothsJson));
            Assert.AreEqual(6, booths.Count);
            Assert.IsTrue(booths.All(b => b.Free));
            var held = GroupParse.Booths(JToken.Parse(@"{""booths"":[{""venueId"":""cabal_lounge"",""boothId"":""booth_2"",""group"":{""groupId"":""01a178f9-af5b-7a36-aac0-2d1df75d36da"",""name"":""Frog Calls"",""memberCount"":2,""here"":0}}]}"));
            Assert.AreEqual("Frog Calls", held[0].GroupName);
            Assert.IsFalse(held[0].Free);
        }

        [Test]
        public void Errors_ServerReasonsBecomeCopy()
        {
            SocialError Err(string json, int status)
            {
                var e = JObject.Parse(json)["error"];
                return new SocialError { Code = (string)e["code"], Status = status, Details = e["details"], Kind = ErrorKind.Definitive, CopyKey = "err.generic" };
            }
            Assert.AreEqual("err.calls_owner_only", GroupText.ErrorKey(Err(NotOwnerJson, 403)));
            Assert.AreEqual("err.no_profit", GroupText.ErrorKey(Err(NoProfitJson, 400)));
            var slow = new SocialError { Code = "RATE_LIMITED", Status = 429, Details = JObject.Parse(@"{""slowModeSeconds"":30}"), CopyKey = "err.rateLimited" };
            Assert.AreEqual("Slow mode: wait 30s.", GroupText.Error(slow));
        }

        [Test]
        public void Rules_CallsOwnerOnly_CallNumbers_Invites()
        {
            Assert.IsTrue(GroupRules.CanPost(GroupKinds.Alpha, GroupRoles.Owner, GroupTopics.Calls));
            Assert.IsFalse(GroupRules.CanPost(GroupKinds.Alpha, GroupRoles.Mod, GroupTopics.Calls));
            Assert.IsTrue(GroupRules.CanPost(GroupKinds.Alpha, GroupRoles.Member, GroupTopics.Wins));
            Assert.IsFalse(GroupRules.CanPost(GroupKinds.Friends, GroupRoles.Owner, GroupTopics.Wins), "friends groups only chat");
            Assert.IsNull(GroupRules.CheckCall(2, 30));
            Assert.AreEqual("group.err.target", GroupRules.CheckCall(1.01, 30));
            Assert.AreEqual("group.err.target", GroupRules.CheckCall(101, 30));
            Assert.AreEqual("group.err.stop", GroupRules.CheckCall(2, 96));
            Assert.IsTrue(GroupRules.CanInvite(GroupKinds.Friends, GroupRoles.Member));
            Assert.IsFalse(GroupRules.CanInvite(GroupKinds.Alpha, GroupRoles.Member));
            Assert.IsFalse(GroupRules.CanInvite(GroupKinds.Tribe, GroupRoles.Owner));
        }

        [Test]
        public void State_BlockedAuthorIsNeverStoredOrCounted()
        {
            var s = new GroupState();
            s.SetList(GroupParse.Summaries(JToken.Parse(ListJson)));
            s.ApplyPage(Alpha, "chat", GroupParse.Page(JToken.Parse(PageJson)), id => id == C3);
            Assert.AreEqual(1, s.Thread(Alpha, "chat").Items.Count, "the blocked member's message is skipped in pages too");
            int before = s.Summary(Alpha).Unread;
            Assert.AreEqual(GroupState.Applied.Dropped, s.ApplyIncoming(Msg("m9", C3, "chat", "gm", 9), Me, id => id == C3));
            Assert.AreEqual(before, s.Summary(Alpha).Unread);
            Assert.AreNotEqual("gm", s.Summary(Alpha).Last.Preview, "a blocked author doesn't even move the preview line");
        }

        [Test]
        public void State_UnreadOnlyWhenNotOpenAndNotMine_AndDuplicatesIgnored()
        {
            var s = new GroupState();
            s.SetList(GroupParse.Summaries(JToken.Parse(ListJson)));
            s.Summary(Alpha).Unread = 0;
            Assert.AreEqual(GroupState.Applied.Added, s.ApplyIncoming(Msg("m1", C3, "chat", "a", 1), Me, null));
            Assert.AreEqual(1, s.Summary(Alpha).Unread);
            s.ApplyPage(Alpha, "chat", new GroupMessagePage(), null);   // the chat was loaded
            s.ApplyIncoming(Msg("m5", C3, "chat", "z", 5), Me, null);
            Assert.AreEqual(GroupState.Applied.Duplicate, s.ApplyIncoming(Msg("m5", C3, "chat", "z", 5), Me, null));
            s.Summary(Alpha).Unread = 1;
            s.ApplyIncoming(Msg("m2", Me, "chat", "mine", 2), Me, null);
            Assert.AreEqual(1, s.Summary(Alpha).Unread, "my own message is not unread");
            s.Open(Alpha, "chat");
            Assert.AreEqual(0, s.Summary(Alpha).Unread);
            s.ApplyIncoming(Msg("m3", C3, "chat", "b", 3), Me, null);
            Assert.AreEqual(0, s.Summary(Alpha).Unread, "the open chat stays read");
            s.ApplyIncoming(Msg("m4", C3, "calls", "c", 4), Me, null);
            Assert.AreEqual(1, s.Summary(Alpha).Unread, "another topic of the open group counts");
            Assert.AreEqual(Alpha, s.Groups[0].GroupId, "newest activity first");
        }

        [Test]
        public void State_BlockPurgesMessagesAndInvites_AndBlockedInvitesNeverLand()
        {
            var s = new GroupState();
            s.SetList(GroupParse.Summaries(JToken.Parse(ListJson)));
            s.ApplyPage(Alpha, "chat", GroupParse.Page(JToken.Parse(PageJson)), null);
            Assert.IsTrue(s.ApplyInvite(new GroupInviteEvent { Code = "abcd1234", GroupId = "g2", GroupName = "Desk", FromUserId = C3 }, null));
            Assert.IsFalse(s.ApplyInvite(new GroupInviteEvent { Code = "zzzz9999", GroupId = "g3", GroupName = "Spam", FromUserId = "bad" }, id => id == "bad"));
            Assert.AreEqual(1, s.Invites(null).Count());
            Assert.AreEqual(2, s.OnBlocked(C3), "one message and one invite");
            Assert.AreEqual(0, s.Invites(null).Count());
            Assert.IsTrue(s.Thread(Alpha, "chat").Items.All(m => m.AuthorUserId != C3));
            s.SetInvites(new[] { GroupParse.Preview(JToken.Parse(PreviewJson)) }, id => id == Me);
            Assert.AreEqual(0, s.Invites(null).Count(), "an invite listed by the server from a blocked player is dropped");
        }

        [Test]
        public void Banners_CallsAndMentionsOnly_NeverMutedBlockedMineOrOpen()
        {
            var call = GroupParse.Message(JToken.Parse(CallJson));
            call.GroupName = "Frog Calls";
            Assert.IsTrue(GroupBannerPolicy.For(call, C3, "lanecC3", false, false, false).Show);
            StringAssert.Contains("$TLARN", GroupBannerPolicy.For(call, C3, "lanecC3", false, false, false).Body);
            Assert.IsFalse(GroupBannerPolicy.For(call, Me, "lanec_rugger", false, false, false).Show, "my own call");
            Assert.IsFalse(GroupBannerPolicy.For(call, C3, "lanecC3", true, false, false).Show, "blocked author");
            Assert.IsFalse(GroupBannerPolicy.For(call, C3, "lanecC3", false, true, false).Show, "muted group");
            Assert.IsFalse(GroupBannerPolicy.For(call, C3, "lanecC3", false, false, true).Show, "chat open");
            Assert.IsFalse(GroupBannerPolicy.For(Msg("m1", Me, "chat", "wen moon", 1), C3, "lanecC3", false, false, false).Show, "plain chat: badge only");
            Assert.IsTrue(GroupBannerPolicy.For(Msg("m2", Me, "chat", "@lanecC3 look", 1), C3, "lanecC3", false, false, false).Show);
            Assert.IsFalse(GroupBannerPolicy.For(Msg("m3", Me, "chat", "@lanecC3x look", 1), C3, "lanecC3", false, false, false).Show, "a longer alias is not a mention");
            Assert.IsFalse(GroupBannerPolicy.Invite(new GroupInviteEvent { Code = "abcd", GroupName = "g", FromUserId = C3 }, "x", true).Show);
            Assert.IsTrue(GroupBannerPolicy.Invite(new GroupInviteEvent { Code = "abcd", GroupName = "g", FromUserId = C3 }, "x", false).Show);
        }

        [Test]
        public void Api_CoversTheContract()
        {
            Assert.IsTrue(SocialEndpoints.Covered.Contains("POST /invites/{code}/accept"));
            Assert.IsTrue(SocialEndpoints.Realtime.Contains("city group_msg"));
            Assert.IsTrue(CTLife.Net.RawRoomMessages.CityTypes.Contains("group_msg"));
            Assert.IsTrue(CTLife.Net.RawRoomMessages.CityTypes.Contains("group_invite"));
        }
    }
}
