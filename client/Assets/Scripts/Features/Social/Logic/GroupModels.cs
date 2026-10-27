using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CTLife.Core;
using Newtonsoft.Json.Linq;

namespace CTLife.SocialFeatures
{
    /// <summary>Group kinds (backend #33, docs/GROUPS.md): friends 12 · alpha 50 · tribe 200. Free; nothing is ever paid.</summary>
    public static class GroupKinds
    {
        public const string Friends = "friends", Alpha = "alpha", Tribe = "tribe";
        public static bool IsKnown(string k) => k == Friends || k == Alpha || k == Tribe;
        public static int Cap(string k) => k == Friends ? 12 : k == Alpha ? 50 : k == Tribe ? 200 : 0;
        public static IReadOnlyList<string> Topics(string k) => k == Alpha ? new[] { GroupTopics.Calls, GroupTopics.Chat, GroupTopics.Wins } : new[] { GroupTopics.Chat };
    }

    public static class GroupTopics
    {
        public const string Chat = "chat", Calls = "calls", Wins = "wins";
        public static bool IsKnown(string t) => t == Chat || t == Calls || t == Wins;
    }

    public static class GroupRoles
    {
        public const string Owner = "owner", Mod = "mod", Member = "member";
        public static bool Staff(string r) => r == Owner || r == Mod;
    }

    public sealed class GroupUserRef { public string UserId, Alias; }

    /// <summary>An alpha caller's Trust (graded calls only; the server computes it).</summary>
    public sealed class GroupTrust { public double HitRate, HeldBagPct; public long AvgReturnBps, MaxDrawdownBps; }

    public sealed class GroupBooth { public string VenueId, BoothId; }

    public sealed class GroupLastLine { public string Topic, AuthorAlias, Preview; public long AtMs; }

    /// <summary>GET /groups row (GroupSummary).</summary>
    public sealed class GroupSummary
    {
        public string GroupId, Kind, Name, Role;
        public int MemberCount, MemberCap, Unread;
        public bool Muted;
        public long? MutedUntilMs;
        public GroupLastLine Last;
        public GroupBooth Booth;
    }

    /// <summary>GET /groups/:id (GroupInfo).</summary>
    public sealed class GroupInfo
    {
        public string GroupId, Kind, Name, Role, Rules;
        public int MemberCount, MemberCap, HereWithYou;
        public bool Muted;
        public List<string> Topics = new List<string>();
        /// <summary>Seconds per topic; the server lists only topics with a slow mode (absent = 0).</summary>
        public Dictionary<string, int> SlowMode = new Dictionary<string, int>();
        public GroupUserRef Owner;
        public GroupTrust Trust;
        public GroupBooth Booth;
        public int SlowSeconds(string topic) => topic != null && SlowMode.TryGetValue(topic, out var s) ? s : 0;
    }

    public sealed class GroupMember { public string UserId, Alias, Role; public long JoinedAtMs; }

    /// <summary>A message's server card: a graded alpha call (status and price read live) or a realized PnL.</summary>
    public sealed class GroupCard
    {
        public const string CallType = "call", PnlType = "pnl";
        public string Type, CallId, CoinId, Ticker, Status, DeepLink;
        public long EntryMicros, TargetMicros, StopMicros, PriceMicros, RealizedCents;
        public bool Hit => Status == "hit_target";
        public bool Stopped => Status == "hit_stop";
        public bool Open => Status == "open";
        /// <summary>The live move since the call, in percent ("now +18%"); 0 without a price.</summary>
        public double NowPct => EntryMicros > 0 && PriceMicros > 0 ? (PriceMicros - EntryMicros) * 100.0 / EntryMicros : 0;
    }

    public sealed class GroupMessage
    {
        public string MessageId, GroupId, Topic, AuthorUserId, AuthorAlias, Text;
        public GroupCard Card;
        public long AtMs;
        // realtime group_msg only
        public string GroupName, Kind;
        public bool Muted;
    }

    public sealed class GroupMessagePage { public List<GroupMessage> Messages = new List<GroupMessage>(); public string NextBefore; }

    /// <summary>POST /groups/:id/invites reply.</summary>
    public sealed class GroupInviteLink { public string Code, DeepLink, GroupId; public long? ExpiresAtMs; public int? MaxUses; public int Uses; }

    /// <summary>GET /invites/:code and the Invites list (GroupInvitePreview).</summary>
    public sealed class GroupInvitePreview
    {
        public string Code, GroupId, GroupName, Kind;
        public int MemberCount, MemberCap;
        public GroupUserRef From, Owner;
        public GroupTrust Trust;
        public long? ExpiresAtMs;
        public bool Valid;
        public bool Full => MemberCap > 0 && MemberCount >= MemberCap;
    }

    /// <summary>GET /venues/:venueId/booths row: the booth and the group that holds it (null = free).</summary>
    public sealed class BoothRow { public string VenueId, BoothId, GroupId, GroupName; public int MemberCount, Here; public bool Free => GroupId == null; }

    /// <summary>Realtime group_invite.</summary>
    public sealed class GroupInviteEvent { public string Code, GroupId, GroupName, FromUserId; public long AtMs; }

    /// <summary>
    /// Defensive parsing of backend #33 replies. Everything shown is plain text (labels also disable rich text): names and
    /// aliases lose '&lt;' '&gt;' and control characters and are capped; ids that go into paths must be safe ids or the row is
    /// dropped; tickers are A-Z/0-9 only; the server's micros are decimal strings (above 2^53, read as long).
    /// </summary>
    public static class GroupParse
    {
        private static string S(JToken t) => SocialWire.Str(t);
        private static string Id(JToken t) { var s = S(t); return SocialWire.IsSafeId(s) ? s : null; }

        /// <summary>A display name: no markup brackets, no control characters, capped.</summary>
        public static string Name(string s, int limit = 40)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return SocialWire.Plain(new string(s.Where(c => c != '<' && c != '>').ToArray()), limit).Trim();
        }

        /// <summary>Message / rules text: control characters dropped, capped. Kept literal (rendered with rich text off).</summary>
        public static string Body(string s, int limit = 500) => SocialWire.Plain(s ?? "", limit);

        public static string Ticker(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var t = new string(s.TrimStart('$').ToUpperInvariant().Where(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')).ToArray());
            return t.Length > 10 ? t.Substring(0, 10) : t;
        }

        private static long Micros(JToken t)
        {
            if (SocialWire.IsNull(t)) return 0;
            if (t.Type == JTokenType.Integer) return (long)t;
            var s = S(t);
            return long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private static long? Ms(JToken t) { try { return SocialWire.TimeMs(t); } catch (FormatException) { return null; } }

        public static GroupUserRef User(JToken t)
        {
            if (!(t is JObject o)) return null;
            var id = Id(o["userId"]);
            return id == null ? null : new GroupUserRef { UserId = id, Alias = Name(S(o["alias"]), 32) };
        }

        public static GroupTrust Trust(JToken t)
        {
            if (!(t is JObject o)) return null;
            double D(JToken x) => x != null && (x.Type == JTokenType.Float || x.Type == JTokenType.Integer) ? (double)x : 0;
            return new GroupTrust { HitRate = D(o["hitRate"]), HeldBagPct = D(o["heldBagPct"]), AvgReturnBps = SocialWire.OptLong(o["avgReturnBps"]) ?? 0, MaxDrawdownBps = SocialWire.OptLong(o["maxDrawdownBps"]) ?? 0 };
        }

        public static GroupBooth Booth(JToken t)
        {
            if (!(t is JObject o)) return null;
            var v = Id(o["venueId"]); var b = Id(o["boothId"]);
            return v == null || b == null ? null : new GroupBooth { VenueId = v, BoothId = b };
        }

        public static GroupSummary Summary(JToken t)
        {
            if (!(t is JObject o)) return null;
            var id = Id(o["groupId"]);
            var kind = S(o["kind"]);
            if (id == null || !GroupKinds.IsKnown(kind)) return null;
            GroupLastLine last = null;
            if (o["lastMessage"] is JObject l)
                last = new GroupLastLine { Topic = S(l["topic"]), AuthorAlias = Name(S(l["authorAlias"]), 32), Preview = Body(S(l["preview"]), 120), AtMs = Ms(l["at"]) ?? 0 };
            return new GroupSummary
            {
                GroupId = id, Kind = kind, Name = Name(S(o["name"])), Role = S(o["role"]) ?? GroupRoles.Member,
                MemberCount = SocialWire.IntOr(o["memberCount"], 0), MemberCap = SocialWire.IntOr(o["memberCap"], GroupKinds.Cap(kind)),
                Unread = Math.Min(99, Math.Max(0, SocialWire.IntOr(o["unread"], 0))), Muted = SocialWire.BoolOr(o["muted"], false),
                MutedUntilMs = Ms(o["mutedUntil"]), Last = last, Booth = Booth(o["booth"]),
            };
        }

        public static List<GroupSummary> Summaries(JToken t)
        {
            var list = new List<GroupSummary>();
            foreach (var row in SocialWire.List(t, "groups")) { var g = Summary(row); if (g != null) list.Add(g); }
            return list;
        }

        public static GroupInfo Info(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a group");
            var id = Id(o["groupId"]);
            var kind = S(o["kind"]);
            if (id == null || !GroupKinds.IsKnown(kind)) throw new FormatException("Bad group");
            var info = new GroupInfo
            {
                GroupId = id, Kind = kind, Name = Name(S(o["name"])), Role = S(o["role"]) ?? GroupRoles.Member, Rules = Body(S(o["rules"]), 1000),
                MemberCount = SocialWire.IntOr(o["memberCount"], 0), MemberCap = SocialWire.IntOr(o["memberCap"], GroupKinds.Cap(kind)),
                HereWithYou = Math.Max(0, SocialWire.IntOr(o["hereWithYou"], 0)), Muted = SocialWire.BoolOr(o["muted"], false),
                Owner = User(o["owner"]), Trust = Trust(o["trust"]), Booth = Booth(o["booth"]),
            };
            if (o["topics"] is JArray topics) foreach (var x in topics) { var s = S(x); if (GroupTopics.IsKnown(s)) info.Topics.Add(s); }
            if (info.Topics.Count == 0) info.Topics.AddRange(GroupKinds.Topics(kind));
            if (o["slowMode"] is JObject sm)
                foreach (var p in sm.Properties())
                    if (GroupTopics.IsKnown(p.Name)) info.SlowMode[p.Name] = Math.Max(0, Math.Min(300, SocialWire.IntOr(p.Value, 0)));
            return info;
        }

        public static List<GroupMember> Members(JToken t)
        {
            var list = new List<GroupMember>();
            foreach (var row in SocialWire.List(t, "members"))
            {
                var id = Id(row["userId"]);
                if (id == null) continue;
                list.Add(new GroupMember { UserId = id, Alias = Name(S(row["alias"]), 32), Role = S(row["role"]) ?? GroupRoles.Member, JoinedAtMs = Ms(row["joinedAt"]) ?? 0 });
            }
            return list;
        }

        public static GroupCard Card(JToken t)
        {
            if (!(t is JObject o)) return null;
            var type = S(o["type"]);
            if (type != GroupCard.CallType && type != GroupCard.PnlType) return null;
            var coin = Id(o["coinId"]);
            if (coin == null) return null;
            var link = S(o["deepLink"]);
            // only the coin's own link (ctlife://coin/<id>) is followed
            if (!VenueLinks.TryParseCoin(link, out var linked) || linked != coin) link = "ctlife://coin/" + coin;
            return new GroupCard
            {
                Type = type, CallId = Id(o["callId"]), CoinId = coin, Ticker = Ticker(S(o["ticker"])), Status = S(o["status"]), DeepLink = link,
                EntryMicros = Micros(o["entryMicros"]), TargetMicros = Micros(o["targetMicros"]), StopMicros = Micros(o["stopMicros"]),
                PriceMicros = Micros(o["priceMicros"]), RealizedCents = SocialWire.OptLong(o["realizedCents"]) ?? 0,
            };
        }

        public static GroupMessage Message(JToken t)
        {
            if (!(t is JObject o)) return null;
            var id = Id(o["messageId"]); var group = Id(o["groupId"]); var author = Id(o["authorUserId"]);
            var topic = S(o["topic"]);
            if (id == null || group == null || author == null || !GroupTopics.IsKnown(topic)) return null;
            return new GroupMessage
            {
                MessageId = id, GroupId = group, Topic = topic, AuthorUserId = author, AuthorAlias = Name(S(o["authorAlias"]), 32),
                Text = Body(S(o["text"])), Card = Card(o["card"]), AtMs = Ms(o["at"]) ?? 0,
                GroupName = Name(S(o["groupName"])), Kind = S(o["kind"]), Muted = SocialWire.BoolOr(o["muted"], false),
            };
        }

        public static GroupMessagePage Page(JToken t)
        {
            var page = new GroupMessagePage();
            foreach (var row in SocialWire.List(t, "messages")) { var m = Message(row); if (m != null) page.Messages.Add(m); }
            page.NextBefore = t is JObject o ? S(o["nextBefore"]) : null;
            if (string.IsNullOrWhiteSpace(page.NextBefore)) page.NextBefore = null;
            return page;
        }

        public static GroupInviteLink Link(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected an invite");
            var code = S(o["code"]);
            if (!GroupLinks.IsCode(code)) throw new FormatException("Bad invite code");
            return new GroupInviteLink
            {
                Code = code, DeepLink = GroupLinks.DeepLink(code), GroupId = Id(o["groupId"]), ExpiresAtMs = Ms(o["expiresAt"]),
                MaxUses = SocialWire.IsNull(o["maxUses"]) ? (int?)null : SocialWire.IntOr(o["maxUses"], 0), Uses = SocialWire.IntOr(o["uses"], 0),
            };
        }

        public static GroupInvitePreview Preview(JToken t)
        {
            if (!(t is JObject o)) return null;
            var code = S(o["code"]);
            var kind = S(o["kind"]);
            if (!GroupLinks.IsCode(code) || !GroupKinds.IsKnown(kind)) return null;
            return new GroupInvitePreview
            {
                Code = code, GroupId = Id(o["groupId"]), GroupName = Name(S(o["groupName"])), Kind = kind,
                MemberCount = SocialWire.IntOr(o["memberCount"], 0), MemberCap = SocialWire.IntOr(o["memberCap"], GroupKinds.Cap(kind)),
                From = User(o["from"]), Owner = User(o["owner"]), Trust = Trust(o["trust"]), ExpiresAtMs = Ms(o["expiresAt"]),
                Valid = SocialWire.BoolOr(o["valid"], false),
            };
        }

        public static List<GroupInvitePreview> Previews(JToken t)
        {
            var list = new List<GroupInvitePreview>();
            foreach (var row in SocialWire.List(t, "invites")) { var p = Preview(row); if (p != null) list.Add(p); }
            return list;
        }

        public static List<BoothRow> Booths(JToken t)
        {
            var list = new List<BoothRow>();
            foreach (var row in SocialWire.List(t, "booths"))
            {
                var v = Id(row["venueId"]); var b = Id(row["boothId"]);
                if (v == null || b == null) continue;
                var r = new BoothRow { VenueId = v, BoothId = b };
                if (row["group"] is JObject g && Id(g["groupId"]) is string gid)
                {
                    r.GroupId = gid; r.GroupName = Name(S(g["name"])); r.MemberCount = SocialWire.IntOr(g["memberCount"], 0); r.Here = Math.Max(0, SocialWire.IntOr(g["here"], 0));
                }
                list.Add(r);
            }
            return list;
        }

        public static GroupInviteEvent InviteEvent(JToken t)
        {
            if (!(t is JObject o)) return null;
            var code = S(o["code"]); var group = Id(o["groupId"]); var from = Id(o["fromUserId"]);
            if (!GroupLinks.IsCode(code) || group == null || from == null) return null;
            return new GroupInviteEvent { Code = code, GroupId = group, GroupName = Name(S(o["groupName"])), FromUserId = from, AtMs = Ms(o["at"]) ?? 0 };
        }
    }

    /// <summary>`ctlife://invite/&lt;code&gt;` (backend #33). Codes are short base62 strings; anything else is refused.</summary>
    public static class GroupLinks
    {
        public const string Prefix = "ctlife://invite/";

        public static bool IsCode(string code) =>
            !string.IsNullOrEmpty(code) && code.Length >= 4 && code.Length <= 32 &&
            code.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_');

        public static string DeepLink(string code) => IsCode(code) ? Prefix + code : null;

        public static bool TryParseInvite(string link, out string code)
        {
            code = null;
            if (string.IsNullOrWhiteSpace(link)) return false;
            var s = link.Trim();
            if (s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) s = s.Substring(Prefix.Length);
            else return false;
            var cut = s.IndexOfAny(new[] { '?', '#', '/' });
            if (cut >= 0) s = s.Substring(0, cut);
            if (!IsCode(s)) return false;
            code = s;
            return true;
        }
    }

    /// <summary>What a player may do in a group (client pre-checks; the server is the judge).</summary>
    public static class GroupRules
    {
        public const double TargetXMin = 1.05, TargetXMax = 100;
        public const int StopPctMin = 1, StopPctMax = 95, SlowModeMax = 300, OwnedMax = 5, NameMax = 40, TextMax = 500;

        public static bool CanPost(string kind, string role, string topic)
        {
            if (!GroupKinds.Topics(kind).Contains(topic)) return false;
            if (topic == GroupTopics.Calls) return role == GroupRoles.Owner;   // calls_owner_only
            return true;
        }

        /// <summary>An error key for a call's numbers, or null.</summary>
        public static string CheckCall(double targetX, int stopPct)
        {
            if (double.IsNaN(targetX) || targetX < TargetXMin || targetX > TargetXMax) return "group.err.target";
            if (stopPct < StopPctMin || stopPct > StopPctMax) return "group.err.stop";
            return null;
        }

        /// <summary>Invite links: friends any member; alpha owner or mod; tribe none.</summary>
        public static bool CanInvite(string kind, string role) =>
            kind == GroupKinds.Friends || (kind == GroupKinds.Alpha && GroupRoles.Staff(role));

        public static bool CanLeave(string kind) => kind != GroupKinds.Tribe;
        public static bool CanModerate(string kind, string role) => kind != GroupKinds.Tribe && GroupRoles.Staff(role);

        /// <summary>
        /// The PATCH body's slowMode. Backend main (a19582a) refuses a partial map (every topic of the group is required:
        /// "slowMode.calls expected number"), so the whole map is sent: the current values with one topic changed.
        /// </summary>
        public static JObject SlowModeBody(GroupInfo info, string topic, int seconds)
        {
            var o = new JObject();
            foreach (var t in info.Topics) o[t] = t == topic ? Math.Max(0, Math.Min(SlowModeMax, seconds)) : info.SlowSeconds(t);
            return o;
        }
    }

    /// <summary>
    /// Banners for group traffic (board groupchats-22: banners for calls and mentions only). Never for a blocked author or
    /// inviter, a muted group, your own message, or the chat that is open; the text is plain and scam-flagged texts show the
    /// hint instead (like DMs).
    /// </summary>
    public static class GroupBannerPolicy
    {
        public struct Banner { public bool Show; public string Title, Body; public ScamSignals.Flag Flag; }

        public static bool Mentions(string text, string myAlias)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(myAlias)) return false;
            var needle = "@" + myAlias.Trim();
            int i = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            while (i >= 0)
            {
                int end = i + needle.Length;
                bool boundary = end >= text.Length || !(char.IsLetterOrDigit(text[end]) || text[end] == '_');
                if (boundary) return true;
                i = text.IndexOf(needle, end, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public static Banner For(GroupMessage m, string me, string myAlias, bool authorBlocked, bool muted, bool chatOpen)
        {
            if (m == null || authorBlocked || muted || m.Muted || chatOpen || SocialWire.SameId(m.AuthorUserId, me)) return default;
            bool call = m.Topic == GroupTopics.Calls && m.Card?.Type == GroupCard.CallType;
            if (!call && !Mentions(m.Text, myAlias)) return default;
            var plain = SocialWire.Plain(m.Text ?? "", 160);
            var flag = ScamSignals.Detect(plain);
            string body = call
                ? GroupText.F("banner.call", m.AuthorAlias, m.Card.Ticker)
                : flag == ScamSignals.Flag.None ? GroupText.F("banner.mention", m.AuthorAlias, plain) : SocialStrings.T("flag." + flag);
            return new Banner { Show = true, Title = string.IsNullOrWhiteSpace(m.GroupName) ? GroupText.T("title") : m.GroupName, Body = body, Flag = flag };
        }

        public static Banner Invite(GroupInviteEvent e, string fromName, bool inviterBlocked)
        {
            if (e == null || inviterBlocked) return default;
            return new Banner { Show = true, Title = GroupText.T("invite.title"), Body = GroupText.F("invite.body", fromName, e.GroupName) };
        }
    }

    /// <summary>Group copy (English). Plain text everywhere.</summary>
    public static class GroupText
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["title"] = "Groups",
            ["banner.call"] = "{0} called ${1}",
            ["banner.mention"] = "{0}: {1}",
            ["invite.title"] = "Group invite",
            ["invite.body"] = "{0} invited you to {1}",
            ["group.err.target"] = "Target is 1.05x to 100x.",
            ["group.err.stop"] = "Stop is 1% to 95% below entry.",
            ["err.group_full"] = "That group is full.",
            ["err.invite_expired"] = "That invite has expired.",
            ["err.removed_from_group"] = "You were removed from that group.",
            ["err.group_hidden"] = "That group was hidden after reports.",
            ["err.calls_owner_only"] = "Only the group's owner posts calls.",
            ["err.booth_taken"] = "Another group holds that booth.",
            ["err.no_profit"] = "Wins need a realized profit on that coin.",
            ["err.slow_mode"] = "Slow mode: wait {0}s.",
            ["err.name"] = "Pick another name.",
            ["err.not_member"] = "You're not in that group.",
            ["err.owned_max"] = "You own 5 groups already.",
        };

        public static string T(string key) => key != null && En.TryGetValue(key, out var v) ? NameTable.Apply(v) : key ?? "";
        public static string F(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);

        /// <summary>The copy key for a group error (server reason first), or the generic social key.</summary>
        public static string ErrorKey(SocialError e)
        {
            if (e == null) return null;
            var reason = e.Details is JObject d ? SocialWire.Str(d["reason"]) : null;
            switch (reason)
            {
                case "group_full": case "invite_expired": case "removed_from_group": case "group_hidden": case "calls_owner_only": case "booth_taken":
                    return "err." + reason;
                case "no realized profit on this coin": return "err.no_profit";
            }
            if (e.Is("NAME_NOT_ALLOWED")) return "err.name";
            if (e.Status == 404 && e.Kind != ErrorKind.RouteMissing) return "err.not_member";
            return null;
        }

        public static string Error(SocialError e)
        {
            var key = ErrorKey(e);
            if (key != null) return T(key);
            if (e != null && e.Status == 429 && e.Details is JObject d && SocialWire.OptLong(d["slowModeSeconds"]) is long s && s > 0) return F("err.slow_mode", s);
            return SocialStrings.T(e?.CopyKey ?? "err.generic");
        }
    }
}
