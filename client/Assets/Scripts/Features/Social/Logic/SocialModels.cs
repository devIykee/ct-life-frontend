using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.SocialFeatures
{
    /// <summary>`Space {spaceId, title, hostId, speakerIds[], handIds[], listenerCount}`. Ids are opaque strings.</summary>
    public sealed class SpaceInfo
    {
        public string Id, Title, HostId;
        /// <summary>Stage speakers, excluding the host, distinct.</summary>
        public List<string> SpeakerIds = new List<string>();
        /// <summary>Raised hands ("speaker requests"), excluding anyone already on stage, in server order.</summary>
        public List<string> HandIds = new List<string>();
        public int ListenerCount;
        /// <summary>Backend extension (`status: ended`). The contract has no status; a 404 also ends the Space.</summary>
        public bool Ended;

        public bool OnStage(string user) => SocialWire.SameId(HostId, user) || SpeakerIds.Any(x => SocialWire.SameId(x, user));
        public bool HandRaised(string user) => HandIds.Any(x => SocialWire.SameId(x, user));
    }

    /// <summary>`POST /rtc/token` → `{url, token, expiresAt, canPublish}`.</summary>
    public sealed class RtcGrant
    {
        public string Url, Token;
        public long ExpiresAtMs;
        public bool CanPublish;

        /// <summary>
        /// Has a URL and token and is not about to expire. `ExpiresAtMs` 0 means the server sent no expiry (the backend's
        /// embedded `livekit {token, url}`); LiveKit itself then rejects an expired token at connect.
        /// </summary>
        public bool Usable(double nowMs, double marginMs = 5000) =>
            !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Token) && (ExpiresAtMs <= 0 || ExpiresAtMs > nowMs + marginMs);
    }

    public sealed class FeedPostRow
    {
        public string PostId, UserId, Alias, Text;
        public long AtMs;
        /// <summary>player (a normal post), sleuth (the Sleuth's exposé of a creator dump, backend #14) or career (a server-written
        /// post about the author's own career). Unknown kinds read as player.</summary>
        public string Kind = FeedKinds.Player;
        /// <summary>Server meta for sleuth / career posts (null for player posts).</summary>
        public SleuthMeta Sleuth;
        /// <summary>`meta.deepLink` (sleuth: `venue:launchpad_tower`; career: the job's link); null when absent.</summary>
        public string DeepLink;
        /// <summary>The server's meta for non-player posts (event cards: coinId, ticker, alias, venueId...); null otherwise.</summary>
        public JObject Meta;
        /// <summary>Backend #32: the city, the Sleuth or an NPC caller (shows the NPC badge).</summary>
        public bool IsNpc;
        /// <summary>Backend #32: the one server-built action card, an optional second (venue) card, the Sleuth's verdict box
        /// under a coin card, and launch cards' holders + followed buyers. Null on older servers.</summary>
        public ServerCard Card, SecondaryCard;
        public PostVerdict Verdict;
        public PostSocial Social;
        public bool IsSleuth => Kind == FeedKinds.Sleuth;
    }

    /// <summary>A server action card (backend #32 docs/TIMELINE.md): coin (live price, rugged red / dead grey), venue or profile.</summary>
    public sealed class ServerCard
    {
        public string Type, CoinId, Ticker, Name, Stage, VenueId, UserId, Alias, DeepLink;
        public long McapCents;
        public int Change5mBps, Holders;
        public bool Rugged, Dead;

        public static ServerCard Parse(JToken t)
        {
            if (!(t is JObject o)) return null;
            var type = SocialWire.Str(o["type"]);
            if (type != "coin" && type != "venue" && type != "profile") return null;
            var c = new ServerCard
            {
                Type = type,
                CoinId = SafeOrNull(SocialWire.Str(o["coinId"])),
                Ticker = Plain(o["ticker"], 16),
                Name = Plain(o["name"], 48),
                Stage = Plain(o["stage"], 16),
                VenueId = VenueOrNull(SocialWire.Str(o["venueId"])),
                UserId = SafeOrNull(SocialWire.Str(o["userId"])),
                Alias = Plain(o["alias"], 40),
                DeepLink = SocialWire.Str(o["deepLink"]) is string d && d.Length <= 120 ? d : null,
                McapCents = SocialWire.OptLong(o["mcapCents"]) ?? 0,
                Change5mBps = SocialWire.IntOr(o["change5mBps"], 0),
                Holders = SocialWire.IntOr(o["holders"], 0),
                Rugged = SocialWire.BoolOr(o["rugged"], false),
                Dead = SocialWire.BoolOr(o["dead"], false),
            };
            if (type == "coin" && c.CoinId == null || type == "venue" && c.VenueId == null || type == "profile" && c.UserId == null) return null;
            return c;
        }

        private static string Plain(JToken t, int max) { var s = SocialWire.Plain(SocialWire.Str(t), max); return string.IsNullOrEmpty(s) ? null : s; }
        private static string SafeOrNull(string id) => SocialWire.IsSafeId(id) ? id : null;
        private static string VenueOrNull(string id) => CTLife.Core.VenueLinks.TryParse("venue:" + (id ?? ""), out var v) ? v : null;
    }

    public sealed class PostVerdict
    {
        public string PostId, Text;
        public int SellSharePct;
        public static PostVerdict Parse(JToken t) => t is JObject o && !string.IsNullOrEmpty(SocialWire.Str(o["text"]))
            ? new PostVerdict { PostId = SocialWire.Str(o["postId"]), Text = SocialWire.Plain(SocialWire.Str(o["text"]), 280), SellSharePct = SocialWire.IntOr(o["sellSharePct"], 0) }
            : null;
    }

    public sealed class PostSocial
    {
        public int Holders;
        public List<(string userId, string alias)> Followed = new List<(string, string)>();
        public static PostSocial Parse(JToken t)
        {
            if (!(t is JObject o)) return null;
            var s = new PostSocial { Holders = SocialWire.IntOr(o["holders"], 0) };
            if (o["followed"] is JArray a)
                foreach (var f in a.Take(5))
                    if (SocialWire.IsSafeId(SocialWire.Str(f["userId"]))) s.Followed.Add((SocialWire.Str(f["userId"]), SocialWire.Plain(SocialWire.Str(f["alias"]), 40)));
            return s;
        }
    }

    /// <summary>A Timeline page (backend #32): posts in the server's order, the cursor, and the "quiet" flag (null = old server).</summary>
    public sealed class FeedPage
    {
        public List<FeedPostRow> Posts = new List<FeedPostRow>();
        public string NextBefore;
        public bool? Quiet;
        public int Skipped;
    }

    public static class FeedKinds
    {
        public const string Player = "player", Sleuth = "sleuth", Career = "career";
        /// <summary>Known kinds pass through (sleuth, career, and the city's event kinds in <see cref="EventKinds"/>); anything
        /// else reads as a player post.</summary>
        public static string Of(string raw) => raw == Sleuth ? Sleuth : raw == Career ? Career
            : raw == EventKinds.Caller ? EventKinds.Caller : raw == EventKinds.Checkin ? EventKinds.Checkin
            : EventKinds.IsCity(raw) ? raw : Player;
    }

    /// <summary>A Sleuth exposé's facts (`meta {coinId, ticker, launcherUserId, launcherAlias, sellSharePct, heat, deepLink}`).</summary>
    public sealed class SleuthMeta
    {
        public string CoinId, Ticker, LauncherUserId, LauncherAlias;
        public int SellSharePct, Heat;

        public static SleuthMeta Parse(JToken meta)
        {
            if (!(meta is JObject m)) return null;
            var coin = SocialWire.Str(m["coinId"]);
            var ticker = SocialWire.Plain(SocialWire.Str(m["ticker"]), 16);
            if (!SocialWire.IsSafeId(coin) || string.IsNullOrEmpty(ticker)) return null;
            var launcher = SocialWire.Str(m["launcherUserId"]);
            return new SleuthMeta
            {
                CoinId = coin,
                Ticker = ticker,
                LauncherUserId = SocialWire.IsSafeId(launcher) ? launcher : null,
                LauncherAlias = SocialWire.Plain(SocialWire.Str(m["launcherAlias"]), 40),
                SellSharePct = Math.Max(0, Math.Min(100, SocialWire.IntOr(m["sellSharePct"], 0))),
                Heat = Math.Max(0, Math.Min(100, SocialWire.IntOr(m["heat"], 0))),
            };
        }
    }

    public sealed class DmThreadRow
    {
        public string UserId, Alias, LastMessage;
        public long LastAtMs;
    }

    public enum DmSendState { Sent, Sending, Failed }

    public sealed class DmRow
    {
        public string MessageId, FromUserId, ToUserId, Text;
        public long AtMs;
        public DmSendState State = DmSendState.Sent;
        /// <summary>Idempotency-Key of a local, not yet confirmed message (its MessageId is `local:{key}`).</summary>
        public string LocalKey;
        public string FailureKey;
        public bool RetrySameKey;
        public bool IsLocal => LocalKey != null;
    }

    public sealed class CreatorProfileRow
    {
        public bool Enabled;
        public string RatePerMinUsdc;
        public int MaxMinutes;
    }

    public enum PaidCallStatus { Unknown, AwaitingPayment, Ringing, Live, Settled, Refunded }

    public sealed class PaidCallRow
    {
        public string CallId, RawStatus;
        public PaidCallStatus Status;
        public int? MinutesUsed;
        /// <summary>USDC base units, server values (null until settled).</summary>
        public string CreatorAmount, RefundAmount;
        public bool Terminal => Status == PaidCallStatus.Settled || Status == PaidCallStatus.Refunded;
    }

    public sealed class PaidCallStart
    {
        public string CallId, SessionPda, Tx;
        /// <summary>A LiveKit grant embedded in the response (backend variant `livekit {token,url}`); null per contract.</summary>
        public RtcGrant EmbeddedGrant;
    }

    public sealed class VideoCallStart
    {
        public string CallId;
        /// <summary>The caller's own grant when the backend embeds one (`tokens[]`); null per contract.</summary>
        public RtcGrant EmbeddedGrant;
    }

    public sealed class UserCard
    {
        public string Id, Alias;
        public bool IsSeeker;
    }

    /// <summary>Parses Phase 3 payloads (contract shape first, then the backend variants named in the progress log).</summary>
    public static class SocialParse
    {
        public static SpaceInfo Space(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a Space");
            var inner = o["space"] as JObject; // tolerate `{space: {...}}`
            if (inner != null) o = inner;
            var s = new SpaceInfo
            {
                Id = SocialWire.ReqStr(SocialWire.First(o, "spaceId", "id"), "spaceId"),
                Title = SocialWire.Plain(SocialWire.Str(o["title"]), 80),
                HostId = SocialWire.ReqStr(SocialWire.First(o, "hostId", "hostUserId"), "hostId"),
                ListenerCount = Math.Max(0, SocialWire.IntOr(o["listenerCount"], 0)),
                Ended = string.Equals(SocialWire.Str(o["status"]), "ended", StringComparison.OrdinalIgnoreCase),
            };
            if (string.IsNullOrWhiteSpace(s.Title)) s.Title = "Space";
            var host = s.HostId;
            s.SpeakerIds = SocialWire.Ids(SocialWire.First(o, "speakerIds", "speakerUserIds"))
                .Where(x => !SocialWire.SameId(x, host)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var stage = new HashSet<string>(s.SpeakerIds, StringComparer.OrdinalIgnoreCase) { host };
            s.HandIds = SocialWire.Ids(SocialWire.First(o, "handIds", "handUserIds"))
                .Where(x => !stage.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return s;
        }

        /// <summary>A bare `Space[]` or `{spaces: []}`. Unreadable rows are skipped and counted.</summary>
        public static List<SpaceInfo> Spaces(JToken t, out int skipped)
        {
            skipped = 0;
            var list = new List<SpaceInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in SocialWire.List(t, "spaces", "items"))
            {
                try
                {
                    var s = Space(row);
                    if (!s.Ended && seen.Add(s.Id)) list.Add(s);
                }
                catch (FormatException) { skipped++; }
            }
            return list;
        }

        public static RtcGrant Rtc(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a token");
            var expires = SocialWire.TimeMs(SocialWire.First(o, "expiresAt", "expires_at"));
            return new RtcGrant
            {
                Url = SocialWire.ReqStr(o["url"], "url"),
                Token = SocialWire.ReqStr(o["token"], "token"),
                // A grant without an expiry is treated as short-lived (the backend issuer defaults to 1 h).
                ExpiresAtMs = expires ?? 0,
                CanPublish = SocialWire.BoolOr(o["canPublish"], false),
            };
        }

        public static FeedPostRow Post(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a post");
            var row = new FeedPostRow
            {
                PostId = SocialWire.ReqStr(SocialWire.First(o, "postId", "id"), "postId"),
                UserId = SocialWire.ReqStr(o["userId"], "userId"),
                Alias = SocialWire.Plain(SocialWire.Str(o["alias"]), 40),
                Text = SocialWire.Plain(SocialWire.Str(o["text"]), 280),
                AtMs = SocialWire.TimeMs(SocialWire.First(o, "at", "createdAt")) ?? 0,
                Kind = FeedKinds.Of(SocialWire.Str(o["kind"])),
            };
            row.IsNpc = SocialWire.BoolOr(o["isNpc"], false);
            row.Card = ServerCard.Parse(o["card"]);
            row.SecondaryCard = ServerCard.Parse(o["secondaryCard"]);
            row.Verdict = PostVerdict.Parse(o["verdict"]);
            row.Social = PostSocial.Parse(o["social"]);
            if (row.Kind != FeedKinds.Player && o["meta"] is JObject meta)
            {
                row.Meta = meta;
                var link = SocialWire.Str(meta["deepLink"]);
                row.DeepLink = !string.IsNullOrEmpty(link) && link.Length <= 120 ? link : null;
                if (row.Kind == FeedKinds.Sleuth) row.Sleuth = SleuthMeta.Parse(meta);
            }
            return row;
        }

        public static FeedPage Page(JToken t)
        {
            var page = new FeedPage { Posts = Feed(t, out var skipped), Skipped = skipped };
            if (t is JObject o)
            {
                page.NextBefore = SocialWire.Str(o["nextBefore"]);
                if (o["quiet"] != null && o["quiet"].Type == JTokenType.Boolean) page.Quiet = (bool)o["quiet"];
            }
            return page;
        }

        public static List<FeedPostRow> Feed(JToken t, out int skipped)
        {
            skipped = 0;
            var list = new List<FeedPostRow>();
            foreach (var row in SocialWire.List(t, "posts", "items"))
            {
                try { list.Add(Post(row)); }
                catch (FormatException) { skipped++; }
            }
            return list;
        }

        public static List<DmThreadRow> Threads(JToken t, out int skipped)
        {
            skipped = 0;
            var list = new List<DmThreadRow>();
            foreach (var row in SocialWire.List(t, "threads", "items"))
            {
                try
                {
                    list.Add(new DmThreadRow
                    {
                        UserId = SocialWire.ReqStr(SocialWire.First(row, "userId", "peerUserId"), "userId"),
                        Alias = SocialWire.Plain(SocialWire.Str(row["alias"]), 40),
                        LastMessage = SocialWire.Plain(SocialWire.Str(SocialWire.First(row, "lastMessage", "text")), 120),
                        LastAtMs = SocialWire.TimeMs(SocialWire.First(row, "lastAt", "createdAt", "at")) ?? 0,
                    });
                }
                catch (FormatException) { skipped++; }
            }
            return list;
        }

        public static DmRow Dm(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a message");
            return new DmRow
            {
                MessageId = SocialWire.ReqStr(SocialWire.First(o, "messageId", "id"), "messageId"),
                FromUserId = SocialWire.ReqStr(o["fromUserId"], "fromUserId"),
                ToUserId = SocialWire.Str(o["toUserId"]),
                Text = SocialWire.Plain(SocialWire.Str(o["text"]), 500),
                AtMs = SocialWire.TimeMs(SocialWire.First(o, "at", "createdAt")) ?? 0,
            };
        }

        public static List<DmRow> Dms(JToken t, out int skipped)
        {
            skipped = 0;
            var list = new List<DmRow>();
            foreach (var row in SocialWire.List(t, "messages", "items"))
            {
                try { list.Add(Dm(row)); }
                catch (FormatException) { skipped++; }
            }
            return list;
        }

        public static CreatorProfileRow Creator(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a creator profile");
            var rate = SocialWire.OptUnits(o["ratePerMinUsdc"]);
            if (rate == null) throw new FormatException("Missing ratePerMinUsdc");
            return new CreatorProfileRow
            {
                Enabled = SocialWire.BoolOr(o["enabled"], false),
                RatePerMinUsdc = rate,
                MaxMinutes = SocialWire.IntOr(o["maxMinutes"], 0),
            };
        }

        public static PaidCallStatus Status(string raw)
        {
            switch ((raw ?? "").Trim().ToLowerInvariant())
            {
                case "awaiting_payment": case "pending": return PaidCallStatus.AwaitingPayment;
                case "ringing": return PaidCallStatus.Ringing;
                case "live": case "active": return PaidCallStatus.Live;
                case "settled": return PaidCallStatus.Settled;
                case "refunded": return PaidCallStatus.Refunded;
                default: return PaidCallStatus.Unknown;
            }
        }

        public static PaidCallRow PaidCall(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a paid call");
            if (o["call"] is JObject inner && SocialWire.IsNull(o["status"])) o = inner;
            var raw = SocialWire.Str(o["status"]);
            var status = Status(raw);
            if (status == PaidCallStatus.Unknown) throw new FormatException("Unknown paid-call status");
            var minutes = SocialWire.OptLong(o["minutesUsed"]);
            return new PaidCallRow
            {
                CallId = SocialWire.ReqStr(SocialWire.First(o, "callId", "id"), "callId"),
                RawStatus = raw,
                Status = status,
                MinutesUsed = minutes.HasValue ? (int?)Math.Max(0, Math.Min(int.MaxValue, minutes.Value)) : null,
                CreatorAmount = SocialWire.OptUnits(o["creatorAmount"]),
                RefundAmount = SocialWire.OptUnits(o["refundAmount"]),
            };
        }

        public static PaidCallStart PaidStart(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a paid-call start");
            var call = o["call"] as JObject;
            var start = new PaidCallStart
            {
                CallId = SocialWire.ReqStr(SocialWire.First(o, "callId") ?? SocialWire.First(call, "callId"), "callId"),
                SessionPda = SocialWire.Str(o["sessionPda"]),
                Tx = SocialWire.Str(o["tx"]),
            };
            if (o["livekit"] is JObject lk)
            {
                try { start.EmbeddedGrant = Rtc(lk); }
                catch (FormatException) { }
            }
            return start;
        }

        /// <summary>`{callId}` per contract; the backend answers `{call: {callId}, tokens: [{userId, token, url, expiresAt}]}`.</summary>
        public static VideoCallStart VideoStart(JToken t, string me)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a call");
            var call = o["call"] as JObject;
            var start = new VideoCallStart { CallId = SocialWire.ReqStr(SocialWire.First(o, "callId") ?? SocialWire.First(call, "callId"), "callId") };
            if (o["tokens"] is JArray tokens)
                foreach (var row in tokens)
                {
                    if (!SocialWire.SameId(SocialWire.Str(row["userId"]), me)) continue;
                    try { start.EmbeddedGrant = Rtc(row); }
                    catch (FormatException) { }
                    break;
                }
            return start;
        }

        public static UserCard User(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a profile");
            return new UserCard
            {
                Id = SocialWire.ReqStr(SocialWire.First(o, "id", "userId"), "id"),
                Alias = SocialWire.Plain(SocialWire.Str(o["alias"]), 40),
                IsSeeker = SocialWire.BoolOr(o["isSeeker"], false),
            };
        }
    }
}
