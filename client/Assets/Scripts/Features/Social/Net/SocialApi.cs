using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// Thin HTTP wrapper for the Phase 3 endpoints (contract section 4: Social, Voice/video/paid calls). Transport policy
    /// (auth refresh, version headers, 429/503 handling, network retries that reuse the Idempotency-Key) is RestClient's.
    /// Every write takes the caller's retained key (<see cref="RetainedKey"/>). Every call is bounded by
    /// <see cref="SpacesRequest.Run{T}"/> so a stalled refresh cannot hang a screen. Responses are parsed by
    /// <see cref="SocialParse"/>; contract paths and bodies are sent as written, with one exception noted at
    /// <see cref="Promote"/>.
    /// </summary>
    public sealed class SocialApi
    {
        private readonly IRestClient _rest;
        public const int TimeoutMs = 30000;

        public SocialApi(IRestClient rest) { _rest = rest ?? throw new ArgumentNullException(nameof(rest)); }

        private static string Seg(string id) => SocialWire.Seg(id);
        private static string Body(JObject o) => (o ?? new JObject()).ToString(Formatting.None);

        private Task<JToken> Get(string path, CancellationToken ct) => SpacesRequest.Run(c => _rest.Get<JToken>(path, c), ct, TimeoutMs);

        private Task<JToken> Write(string method, string path, JObject body, RetainedKey key, CancellationToken ct)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            var json = body == null ? null : Body(body);
            var k = key.For(method, path, json);
            return SpacesRequest.Run(c => _rest.Send<JToken>(method, path, json, true, k, c), ct, TimeoutMs);
        }

        // ---------------------------------------------------------------- users

        public async Task<UserCard> User(string userId, CancellationToken ct) => SocialParse.User(await Get("/users/" + Seg(userId), ct));

        public Task Block(string userId, RetainedKey key, CancellationToken ct) => Write("POST", "/blocks/" + Seg(userId), null, key, ct);

        /// <summary>targetType: user | message | space | call | post.</summary>
        public Task Report(string targetType, string targetId, string reasonId, RetainedKey key, CancellationToken ct) =>
            Write("POST", "/reports", new JObject { ["targetType"] = targetType, ["targetId"] = targetId, ["reasonId"] = reasonId }, key, ct);

        // ---------------------------------------------------------------- RTC (Ph 3)

        /// <summary>
        /// POST /rtc/token {kind: venue|space|call, id}. Always a new Idempotency-Key: a replayed (cached for 24 h) answer
        /// would hand back an expired grant on a later reconnect. Minting is side-effect free, so a retry is harmless.
        /// </summary>
        public async Task<RtcGrant> RtcToken(string kind, string id, CancellationToken ct) =>
            SocialParse.Rtc(await Write("POST", "/rtc/token", new JObject { ["kind"] = kind, ["id"] = id }, new RetainedKey(), ct));

        // ---------------------------------------------------------------- Spaces (Ph 3)

        public async Task<List<SpaceInfo>> LiveSpaces(CancellationToken ct) => SocialParse.Spaces(await Get("/spaces?live=true", ct), out _);

        public async Task<SpaceInfo> Space(string spaceId, CancellationToken ct) => SocialParse.Space(await Get("/spaces/" + Seg(spaceId), ct));

        public async Task<SpaceInfo> CreateSpace(string title, RetainedKey key, CancellationToken ct) =>
            SocialParse.Space(await Write("POST", "/spaces", new JObject { ["title"] = title }, key, ct));

        public Task RaiseHand(string spaceId, RetainedKey key, CancellationToken ct) => Write("POST", "/spaces/" + Seg(spaceId) + "/hand", null, key, ct);

        public Task LowerHand(string spaceId, RetainedKey key, CancellationToken ct) => Write("DELETE", "/spaces/" + Seg(spaceId) + "/hand", null, key, ct);

        /// <summary>
        /// Contract: `POST /spaces/{id}/speakers {userId}`. The backend checkout (9f3e5ef) registered
        /// `POST /spaces/{id}/speakers/{userId}` instead; when the contract route is missing (backend not-found envelope,
        /// nothing executed) the same promotion is sent to that path. Reported as a mismatch.
        /// </summary>
        public async Task Promote(string spaceId, string userId, RetainedKey key, RetainedKey fallbackKey, CancellationToken ct)
        {
            try
            {
                await Write("POST", "/spaces/" + Seg(spaceId) + "/speakers", new JObject { ["userId"] = userId }, key, ct);
            }
            catch (ApiException e) when (SocialError.IsRouteMissing(e))
            {
                key.Complete();
                await Write("POST", "/spaces/" + Seg(spaceId) + "/speakers/" + Seg(userId), null, fallbackKey, ct);
            }
        }

        public Task Demote(string spaceId, string userId, RetainedKey key, CancellationToken ct) =>
            Write("DELETE", "/spaces/" + Seg(spaceId) + "/speakers/" + Seg(userId), null, key, ct);

        public Task EndSpace(string spaceId, RetainedKey key, CancellationToken ct) => Write("POST", "/spaces/" + Seg(spaceId) + "/end", null, key, ct);

        // ---------------------------------------------------------------- calls (Ph 3)

        public async Task<VideoCallStart> StartVideoCall(IReadOnlyList<string> userIds, string me, RetainedKey key, CancellationToken ct) =>
            SocialParse.VideoStart(await Write("POST", "/video-calls", new JObject { ["userIds"] = new JArray(userIds) }, key, ct), me);

        /// <summary>The current server stores free-call participants under video:, and paid participants under paid_call:.</summary>
        public Task<RtcGrant> CallToken(string callId, bool paid, CancellationToken ct) =>
            RtcToken(paid ? "paid_call" : "video", callId, ct);

        /// <summary>Host-only end. A repeated end after the server already closed the call is a confirmed terminal result.</summary>
        public async Task EndVideoCall(string callId, RetainedKey key, CancellationToken ct)
        {
            try { await Write("POST", "/video-calls/" + Seg(callId) + "/end", null, key, ct); }
            catch (ApiException e) when (e.Is("VALIDATION_FAILED") &&
                SocialWire.Str((e.Details as JObject)?["reason"]) == "call already ended") { }
        }

        /// <summary>PUT /creator {enabled, ratePerMinUsdc (base units string), maxMinutes}.</summary>
        public async Task<CreatorProfileRow> PutCreator(bool enabled, string ratePerMinUsdc, int maxMinutes, RetainedKey key, CancellationToken ct) =>
            SocialParse.Creator(await Write("PUT", "/creator", new JObject { ["enabled"] = enabled, ["ratePerMinUsdc"] = ratePerMinUsdc, ["maxMinutes"] = maxMinutes }, key, ct));

        public async Task<PaidCallStart> StartPaidCall(string creatorId, int minutes, RetainedKey key, CancellationToken ct) =>
            SocialParse.PaidStart(await Write("POST", "/paid-calls", new JObject { ["creatorId"] = creatorId, ["minutes"] = minutes }, key, ct));

        public async Task<PaidCallRow> SubmitPaidCall(string callId, string signature, RetainedKey key, CancellationToken ct) =>
            SocialParse.PaidCall(await Write("POST", "/paid-calls/" + Seg(callId) + "/submitted", new JObject { ["signature"] = signature }, key, ct));

        public async Task<PaidCallRow> AcceptPaidCall(string callId, RetainedKey key, CancellationToken ct) =>
            SocialParse.PaidCall(await Write("POST", "/paid-calls/" + Seg(callId) + "/accept", null, key, ct));

        public async Task<PaidCallRow> DeclinePaidCall(string callId, RetainedKey key, CancellationToken ct) =>
            SocialParse.PaidCall(await Write("POST", "/paid-calls/" + Seg(callId) + "/decline", null, key, ct));

        public async Task<PaidCallRow> EndPaidCall(string callId, RetainedKey key, CancellationToken ct) =>
            SocialParse.PaidCall(await Write("POST", "/paid-calls/" + Seg(callId) + "/end", null, key, ct));

        public async Task<PaidCallRow> PaidCall(string callId, CancellationToken ct) => SocialParse.PaidCall(await Get("/paid-calls/" + Seg(callId), ct));

        // ---------------------------------------------------------------- DMs (Ph 3)

        public async Task<List<DmThreadRow>> Threads(CancellationToken ct) => SocialParse.Threads(await Get("/dms", ct), out _);

        public async Task<List<DmRow>> Messages(string userId, string before, CancellationToken ct)
        {
            var path = "/dms/" + Seg(userId) + (string.IsNullOrEmpty(before) ? "" : "?before=" + Uri.EscapeDataString(before));
            return SocialParse.Dms(await Get(path, ct), out _);
        }

        public async Task<DmRow> SendDm(string userId, string text, RetainedKey key, CancellationToken ct) =>
            SocialParse.Dm(await Write("POST", "/dms/" + Seg(userId), new JObject { ["text"] = text }, key, ct));

        // ---------------------------------------------------------------- Timeline, follows (Ph 3; Cash tips removed, backend #25)

        public async Task<List<FeedPostRow>> Feed(string scope, CancellationToken ct) =>
            SocialParse.Feed(await Get("/feed?scope=" + (scope == "following" ? "following" : "global"), ct), out _);

        /// <summary>Backend #32: GET /feed?tab=foryou|following|city&amp;type=&amp;before= → posts, nextBefore, quiet.</summary>
        public async Task<FeedPage> Timeline(string tab, string type, string before, CancellationToken ct)
        {
            var t = tab == "following" ? "following" : tab == "city" ? "city" : "foryou";
            var q = "/feed?tab=" + t;
            if (t == "city" && !string.IsNullOrEmpty(type)) q += "&type=" + Uri.EscapeDataString(type);
            if (!string.IsNullOrEmpty(before)) q += "&before=" + Uri.EscapeDataString(before);
            return SocialParse.Page(await Get(q, ct));
        }

        public async Task<FeedPostRow> Post(string text, RetainedKey key, CancellationToken ct, bool withPlace = false)
        {
            var body = new JObject { ["text"] = text };
            if (withPlace) body["withPlace"] = true;
            return SocialParse.Post(await Write("POST", "/feed/posts", body, key, ct));
        }

        /// <summary>Backend #32: POST /feed/checkins {venueId} (opt-in; within 60 m of the door; once per 15 min).</summary>
        public async Task<FeedPostRow> CheckIn(string venueId, RetainedKey key, CancellationToken ct) =>
            SocialParse.Post(await Write("POST", "/feed/checkins", new JObject { ["venueId"] = venueId }, key, ct));

        public Task Follow(string userId, bool follow, RetainedKey key, CancellationToken ct) =>
            Write(follow ? "POST" : "DELETE", "/follows/" + Seg(userId), null, key, ct);
        // ---------------------------------------------------------------- Groups (backend #33, docs/GROUPS.md)

        public async Task<List<GroupSummary>> Groups(CancellationToken ct) => GroupParse.Summaries(await Get("/groups", ct));

        public async Task<GroupSummary> CreateGroup(string kind, string name, IReadOnlyList<string> inviteUserIds, RetainedKey key, CancellationToken ct)
        {
            var body = new JObject { ["kind"] = kind, ["name"] = name };
            if (inviteUserIds != null && inviteUserIds.Count > 0) body["inviteUserIds"] = new JArray(inviteUserIds);
            return GroupParse.Summary(await Write("POST", "/groups", body, key, ct)) ?? throw new FormatException("Bad group");
        }

        public async Task<List<GroupInvitePreview>> GroupInvites(CancellationToken ct) => GroupParse.Previews(await Get("/groups/invites", ct));

        public async Task<GroupInfo> Group(string groupId, CancellationToken ct) => GroupParse.Info(await Get("/groups/" + Seg(groupId), ct));

        /// <summary>PATCH /groups/:id {name?, rules?, slowMode?} (slowMode: the whole map, see GroupRules.SlowModeBody).</summary>
        public async Task<GroupInfo> PatchGroup(string groupId, string name, string rules, JObject slowMode, RetainedKey key, CancellationToken ct)
        {
            var body = new JObject();
            if (name != null) body["name"] = name;
            if (rules != null) body["rules"] = rules;
            if (slowMode != null) body["slowMode"] = slowMode;
            return GroupParse.Info(await Write("PATCH", "/groups/" + Seg(groupId), body, key, ct));
        }

        public async Task<List<GroupMember>> GroupMembers(string groupId, CancellationToken ct) => GroupParse.Members(await Get("/groups/" + Seg(groupId) + "/members", ct));

        public Task RemoveMember(string groupId, string userId, RetainedKey key, CancellationToken ct) =>
            Write("POST", "/groups/" + Seg(groupId) + "/members/" + Seg(userId) + "/remove", null, key, ct);

        public Task SetRole(string groupId, string userId, string role, RetainedKey key, CancellationToken ct) =>
            Write("POST", "/groups/" + Seg(groupId) + "/members/" + Seg(userId) + "/role", new JObject { ["role"] = role }, key, ct);

        public Task LeaveGroup(string groupId, RetainedKey key, CancellationToken ct) => Write("POST", "/groups/" + Seg(groupId) + "/leave", null, key, ct);

        /// <summary>POST /groups/:id/mute {hours} (0 unmutes) → mutedUntil (ms) or null.</summary>
        public async Task<long?> MuteGroup(string groupId, int hours, RetainedKey key, CancellationToken ct)
        {
            var r = await Write("POST", "/groups/" + Seg(groupId) + "/mute", new JObject { ["hours"] = hours }, key, ct);
            try { return SocialWire.TimeMs((r as JObject)?["mutedUntil"]); } catch (FormatException) { return null; }
        }

        public Task ReadGroup(string groupId, RetainedKey key, CancellationToken ct) => Write("POST", "/groups/" + Seg(groupId) + "/read", null, key, ct);

        public Task ReportGroup(string groupId, string reasonId, string note, RetainedKey key, CancellationToken ct)
        {
            var body = new JObject { ["reasonId"] = reasonId };
            if (!string.IsNullOrWhiteSpace(note)) body["note"] = note;
            return Write("POST", "/groups/" + Seg(groupId) + "/report", body, key, ct);
        }

        public async Task<GroupInviteLink> CreateInviteLink(string groupId, int? maxUses, int? ttlHours, RetainedKey key, CancellationToken ct)
        {
            var body = new JObject();
            if (maxUses.HasValue) body["maxUses"] = maxUses.Value;
            if (ttlHours.HasValue) body["ttlHours"] = ttlHours.Value;
            return GroupParse.Link(await Write("POST", "/groups/" + Seg(groupId) + "/invites", body, key, ct));
        }

        public Task InviteUser(string groupId, string userId, RetainedKey key, CancellationToken ct) =>
            Write("POST", "/groups/" + Seg(groupId) + "/invites/user", new JObject { ["userId"] = userId }, key, ct);

        public async Task<GroupInvitePreview> InvitePreview(string code, CancellationToken ct) =>
            GroupParse.Preview(await Get("/invites/" + InviteSeg(code), ct)) ?? throw new FormatException("Bad invite");

        public async Task<GroupSummary> AcceptInvite(string code, RetainedKey key, CancellationToken ct) =>
            GroupParse.Summary(await Write("POST", "/invites/" + InviteSeg(code) + "/accept", null, key, ct)) ?? throw new FormatException("Bad group");

        public Task DeclineInvite(string code, RetainedKey key, CancellationToken ct) => Write("POST", "/invites/" + InviteSeg(code) + "/decline", null, key, ct);

        public async Task<GroupMessagePage> GroupMessages(string groupId, string topic, string before, CancellationToken ct)
        {
            var q = "/groups/" + Seg(groupId) + "/messages?topic=" + Uri.EscapeDataString(topic ?? GroupTopics.Chat);
            if (!string.IsNullOrEmpty(before)) q += "&before=" + Uri.EscapeDataString(before);
            return GroupParse.Page(await Get(q, ct));
        }

        public async Task<GroupMessage> SendGroupChat(string groupId, string topic, string text, RetainedKey key, CancellationToken ct) =>
            GroupParse.Message(await Write("POST", "/groups/" + Seg(groupId) + "/messages", new JObject { ["topic"] = topic, ["text"] = text }, key, ct))
            ?? throw new FormatException("Bad message");

        /// <summary>An alpha call (owner only): the server prices the entry and grades it like any alpha call.</summary>
        public async Task<GroupMessage> SendGroupCall(string groupId, string coinId, double targetX, int stopPct, string text, RetainedKey key, CancellationToken ct)
        {
            var body = new JObject { ["topic"] = GroupTopics.Calls, ["coinId"] = coinId, ["targetX"] = targetX, ["stopPct"] = stopPct };
            if (!string.IsNullOrWhiteSpace(text)) body["text"] = text;
            return GroupParse.Message(await Write("POST", "/groups/" + Seg(groupId) + "/messages", body, key, ct)) ?? throw new FormatException("Bad message");
        }

        public async Task<GroupMessage> SendGroupWin(string groupId, string coinId, string text, RetainedKey key, CancellationToken ct)
        {
            var body = new JObject { ["topic"] = GroupTopics.Wins, ["coinId"] = coinId };
            if (!string.IsNullOrWhiteSpace(text)) body["text"] = text;
            return GroupParse.Message(await Write("POST", "/groups/" + Seg(groupId) + "/messages", body, key, ct)) ?? throw new FormatException("Bad message");
        }

        public Task DeleteGroupMessage(string groupId, string messageId, RetainedKey key, CancellationToken ct) =>
            Write("DELETE", "/groups/" + Seg(groupId) + "/messages/" + Seg(messageId), null, key, ct);

        public async Task<List<BoothRow>> Booths(string venueId, CancellationToken ct) => GroupParse.Booths(await Get("/venues/" + Seg(venueId) + "/booths", ct));

        public Task ClaimBooth(string groupId, string venueId, string boothId, RetainedKey key, CancellationToken ct) =>
            Write("POST", "/groups/" + Seg(groupId) + "/booth", new JObject { ["venueId"] = venueId, ["boothId"] = boothId }, key, ct);

        public Task ReleaseBooth(string groupId, RetainedKey key, CancellationToken ct) => Write("DELETE", "/groups/" + Seg(groupId) + "/booth", null, key, ct);

        private static string InviteSeg(string code)
        {
            if (!GroupLinks.IsCode(code)) throw new ArgumentException("Bad invite code");
            return Uri.EscapeDataString(code);
        }
    }

    /// <summary>Static facts about the endpoint set, for the progress log and diagnostics.</summary>
    public static class SocialEndpoints
    {
        public static readonly IReadOnlyList<string> Covered = new[]
        {
            "POST /rtc/token", "GET /spaces?live=true", "POST /spaces", "GET /spaces/{id}", "POST /spaces/{id}/hand", "DELETE /spaces/{id}/hand",
            "POST /spaces/{id}/speakers", "DELETE /spaces/{id}/speakers/{userId}", "POST /spaces/{id}/end",
            "POST /video-calls", "POST /video-calls/{id}/end", "PUT /creator", "POST /paid-calls", "POST /paid-calls/{id}/submitted", "POST /paid-calls/{id}/accept",
            "POST /paid-calls/{id}/decline", "POST /paid-calls/{id}/end", "GET /paid-calls/{id}",
            "GET /dms", "GET /dms/{userId}?before=", "POST /dms/{userId}", "GET /feed?scope=", "POST /feed/posts",
            "POST /follows/{userId}", "DELETE /follows/{userId}",
            "GET /users/{id}", "POST /blocks/{userId}", "POST /reports",
            "GET /groups", "POST /groups", "GET /groups/invites", "GET /groups/{id}", "PATCH /groups/{id}", "GET /groups/{id}/members",
            "POST /groups/{id}/members/{userId}/remove", "POST /groups/{id}/members/{userId}/role", "POST /groups/{id}/leave",
            "POST /groups/{id}/mute", "POST /groups/{id}/read", "POST /groups/{id}/report", "POST /groups/{id}/invites",
            "POST /groups/{id}/invites/user", "GET /invites/{code}", "POST /invites/{code}/accept", "POST /invites/{code}/decline",
            "GET /groups/{id}/messages?topic=&before=", "POST /groups/{id}/messages", "DELETE /groups/{id}/messages/{messageId}",
            "GET /venues/{venueId}/booths", "POST /groups/{id}/booth", "DELETE /groups/{id}/booth",
        };

        public static readonly IReadOnlyList<string> Realtime = new[] { "city space_update", "city dm", "city call_invite", "city group_msg", "city group_invite" };
    }
}
