using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.Competition
{
    // Read models for docs/fe-contract-proposals/phase4-6 (Phase 6). Money is int64 cents, times are epoch ms, ids are
    // opaque strings. Every Parse throws FormatException on a missing required field. Unknown enum values are kept as
    // strings so a newer server never crashes the client.

    public sealed class TribeInfo
    {
        public string Id, Name, HomeTurf, Perk, EmoteId;
        public int Phase;
        public List<string> OwnedDistricts = new List<string>();
        public int? MemberCount;

        public static TribeInfo Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Tribe");
            return new TribeInfo
            {
                Id = CompWire.ReqStr(o["tribeId"] ?? o["id"], "tribeId"),
                Name = CompWire.Plain(o["name"], 40),
                HomeTurf = CompWire.Plain(o["homeTurf"], 40),
                Perk = CompWire.Plain(o["perk"], 120),
                EmoteId = CompWire.Plain(o["emoteId"], 60),
                Phase = CompWire.IntOr(o["phase"], 1),
                OwnedDistricts = Ids(o["ownedDistricts"]),
                MemberCount = CompWire.IsNull(o["memberCount"]) ? (int?)null : CompWire.IntOr(o["memberCount"], 0),
            };
        }

        public static List<TribeInfo> ParseList(JToken t) => CompWire.List(t, "tribes", "items").Select(Parse).ToList();

        internal static List<string> Ids(JToken t) =>
            t is JArray a ? a.Select(CompWire.Str).Where(CompWire.IsSafeId).ToList() : new List<string>();
    }

    public sealed class DistrictInfo
    {
        public string Id, Name, DefaultHolderTribeId, Lore;
        public List<string> VenueIds = new List<string>();
        /// <summary>Null while nobody holds it (the server then reports the default holder).</summary>
        public string OwnerTribeId;
        public long? ControlledSinceMs;

        public static DistrictInfo Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("District");
            return new DistrictInfo
            {
                Id = CompWire.ReqStr(o["districtId"] ?? o["id"], "districtId"),
                Name = CompWire.Plain(o["name"], 60),
                VenueIds = TribeInfo.Ids(o["venueIds"]),
                OwnerTribeId = CompWire.Str(o["currentOwnerTribeId"]),
                ControlledSinceMs = CompWire.TimeMsOrNull(o["controlledSince"]),
                DefaultHolderTribeId = CompWire.Str(o["defaultHolderTribeId"]),
                Lore = CompWire.Plain(o["lore"], 200),
            };
        }

        public static List<DistrictInfo> ParseList(JToken t) => CompWire.List(t, "districts", "items").Select(Parse).ToList();
    }

    public sealed class TribeScore
    {
        public string TribeId;
        public long Score;
    }

    public sealed class DistrictStandings
    {
        public string DistrictId, DayDate, MyTribeId;
        public long RolloverAtMs;
        public List<TribeScore> Standings = new List<TribeScore>();
        /// <summary>Null for anon players (hidden by the server).</summary>
        public long? MyContribution;

        public static DistrictStandings Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Standings");
            var s = new DistrictStandings
            {
                DistrictId = CompWire.ReqStr(o["districtId"], "districtId"),
                DayDate = CompWire.Plain(o["dayDate"], 12),
                RolloverAtMs = CompWire.TimeMs(o["rolloverAt"]) ?? 0,
                MyTribeId = CompWire.Str(o["myTribeId"]),
                MyContribution = CompWire.OptLong(o["myContribution"]),
            };
            foreach (var row in CompWire.List(o["standings"], "items"))
                s.Standings.Add(new TribeScore { TribeId = CompWire.ReqStr(row["tribeId"], "tribeId"), Score = CompWire.Long(row["score"]) });
            // Highest first; equal scores keep the server order (stable sort).
            s.Standings = s.Standings.Select((r, i) => (r, i)).OrderByDescending(x => x.r.Score).ThenBy(x => x.i).Select(x => x.r).ToList();
            return s;
        }
    }

    public sealed class LeagueInfo
    {
        public string Id, Slug, Label, PromoteToSlug;
        public long MinNetWorthCents;
        public int MaxMembers, SortOrder;

        public static LeagueInfo Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("League");
            var slug = CompWire.ReqStr(o["slug"], "slug");
            return new LeagueInfo
            {
                Id = CompWire.Str(o["leagueId"]) ?? slug,
                Slug = slug,
                Label = CompWire.Plain(o["label"], 40),
                MinNetWorthCents = CompWire.LongOr(o["minNetWorthCents"], 0),
                MaxMembers = CompWire.IntOr(o["maxMembers"], 0),
                SortOrder = CompWire.IntOr(o["sortOrder"], 0),
                PromoteToSlug = CompWire.Str(o["promoteToSlug"]),
            };
        }

        public static List<LeagueInfo> ParseList(JToken t) =>
            CompWire.List(t, "leagues", "items").Select(Parse).OrderBy(l => l.SortOrder).ToList();
    }

    public sealed class LeagueMembership
    {
        public string SeasonIso;
        /// <summary>False before the player's first rollover (the server sends `membership: null`).</summary>
        public bool HasMembership;
        public string LeagueId, PromotedFromLeagueId;
        public long NetWorthCents;
        public int Rank;

        public static LeagueMembership Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("League membership");
            var m = new LeagueMembership { SeasonIso = CompWire.Plain(o["seasonIso"], 20) };
            if (o["membership"] is JObject mem)
            {
                m.HasMembership = true;
                m.LeagueId = CompWire.ReqStr(mem["leagueId"], "leagueId");
                m.NetWorthCents = CompWire.LongOr(mem["netWorthCents"], 0);
                m.Rank = CompWire.IntOr(mem["rank"], 0);
                m.PromotedFromLeagueId = CompWire.Str(mem["promotedFromLeagueId"]);
            }
            return m;
        }
    }

    public sealed class SeasonReward
    {
        public string Kind, ItemId, BadgeId;
        public long? CashCents;
    }

    public sealed class SeasonResult
    {
        public string SeasonId, LeagueId, ToLeagueId, Outcome;
        public int FinalRank;
        public List<SeasonReward> Rewards = new List<SeasonReward>();

        public static SeasonResult Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Season result");
            var r = new SeasonResult
            {
                SeasonId = CompWire.ReqStr(o["seasonId"], "seasonId"),
                LeagueId = CompWire.Str(o["leagueId"]),
                ToLeagueId = CompWire.Str(o["toLeagueId"]),
                Outcome = CompWire.Str(o["outcome"]) ?? "stayed",
                FinalRank = CompWire.IntOr(o["finalRank"], 0),
            };
            if (o["rewards"] is JArray rw)
                foreach (var x in rw)
                    r.Rewards.Add(new SeasonReward
                    {
                        Kind = CompWire.Str(x["kind"]) ?? "",
                        CashCents = CompWire.OptLong(x["cashCents"]),
                        ItemId = CompWire.Str(x["itemId"]),
                        BadgeId = CompWire.Str(x["badgeId"]),
                    });
            return r;
        }
    }

    public sealed class BeefVotes
    {
        public int Challenger, Accepter, Total;
    }

    public sealed class BeefMatch
    {
        public string MatchId, ChallengerUserId, InvitedUserId, AccepterUserId, TopicId, Status, MyVote, WinnerUserId;
        public long StakeCents;
        public int FeeBps;
        public long? PhaseEndsAtMs, OpenedAtMs, AcceptedAtMs, SettledAtMs;
        /// <summary>Null until the server reveals tallies (the proposal hides them until the match closes).</summary>
        public BeefVotes Votes;
        public int? EloChallengerPre, EloAccepterPre, EloChallengerPost, EloAccepterPost;

        public static BeefMatch Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Beef match");
            var m = new BeefMatch
            {
                MatchId = CompWire.ReqStr(o["matchId"] ?? o["id"], "matchId"),
                ChallengerUserId = CompWire.ReqStr(o["challengerUserId"], "challengerUserId"),
                InvitedUserId = CompWire.Str(o["invitedUserId"]),
                AccepterUserId = CompWire.Str(o["accepterUserId"]),
                TopicId = CompWire.Plain(o["topicId"], 60),
                Status = CompWire.ReqStr(o["status"], "status"),
                StakeCents = CompWire.Long(o["stakeCents"]),
                FeeBps = CompWire.IntOr(o["feeBps"], 0),
                PhaseEndsAtMs = CompWire.TimeMsOrNull(o["phaseEndsAt"]),
                OpenedAtMs = CompWire.TimeMsOrNull(o["openedAt"]),
                AcceptedAtMs = CompWire.TimeMsOrNull(o["acceptedAt"]),
                SettledAtMs = CompWire.TimeMsOrNull(o["settledAt"]),
                MyVote = CompWire.Str(o["myVote"]),
                WinnerUserId = CompWire.Str(o["winnerUserId"]),
                EloChallengerPre = Elo(o["eloChallengerPre"]),
                EloAccepterPre = Elo(o["eloAccepterPre"]),
                EloChallengerPost = Elo(o["eloChallengerPost"]),
                EloAccepterPost = Elo(o["eloAccepterPost"]),
            };
            if (o["votes"] is JObject v)
                m.Votes = new BeefVotes
                {
                    Challenger = CompWire.IntOr(v["challenger"], 0),
                    Accepter = CompWire.IntOr(v["accepter"], 0),
                    Total = CompWire.IntOr(v["total"], 0),
                };
            return m;
        }

        private static int? Elo(JToken t) => CompWire.IsNull(t) ? (int?)null : CompWire.IntOr(t, 0);
    }

    public sealed class BeefPage
    {
        public List<BeefMatch> Matches = new List<BeefMatch>();
        public string NextCursor;

        public static BeefPage Parse(JToken t)
        {
            var p = new BeefPage { NextCursor = (t as JObject) != null ? CompWire.Str(t["nextCursor"]) : null };
            foreach (var x in CompWire.List(t, "matches", "items")) p.Matches.Add(BeefMatch.Parse(x));
            return p;
        }
    }

    public sealed class BeefElo
    {
        public string UserId;
        public int Elo, MatchesPlayed, Wins, Losses, Draws;

        public static BeefElo Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Beef Elo");
            return new BeefElo
            {
                UserId = CompWire.Str(o["userId"]),
                Elo = CompWire.IntOr(o["elo"], 0),
                MatchesPlayed = CompWire.IntOr(o["matchesPlayed"], 0),
                Wins = CompWire.IntOr(o["wins"], 0),
                Losses = CompWire.IntOr(o["losses"], 0),
                Draws = CompWire.IntOr(o["draws"], 0),
            };
        }
    }

    public sealed class BoardRow
    {
        public int Rank;
        public string UserId, Alias;
        public long Value;
    }

    /// <summary>rc1 `GET /leaderboards/{boardId}`: `{entries, me}`.</summary>
    public sealed class BoardPage
    {
        public List<BoardRow> Entries = new List<BoardRow>();
        public BoardRow Me;

        public static BoardPage Parse(JToken t)
        {
            var p = new BoardPage();
            foreach (var x in CompWire.List(t, "entries", "items")) p.Entries.Add(Row(x));
            if (t is JObject o && o["me"] is JObject me) p.Me = Row(me);
            return p;
        }

        private static BoardRow Row(JToken x) => new BoardRow
        {
            Rank = CompWire.IntOr(x["rank"], 0),
            UserId = CompWire.Str(x["userId"]),
            Alias = CompWire.Plain(x["alias"], 32),
            Value = CompWire.LongOr(x["value"], 0),
        };
    }

    public sealed class SybilStatus
    {
        public bool Jailed, Bailable;
        public long? JailedAtMs, ReleaseAtMs, BailCents;
        public string ReasonId;

        public static SybilStatus Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Sybil status");
            return new SybilStatus
            {
                Jailed = CompWire.BoolOr(o["jailed"], false),
                Bailable = CompWire.BoolOr(o["bailable"], false),
                JailedAtMs = CompWire.TimeMsOrNull(o["jailedAt"]),
                ReleaseAtMs = CompWire.TimeMsOrNull(o["releaseAt"]),
                BailCents = CompWire.OptLong(o["bailCents"]),
                ReasonId = CompWire.Str(o["reasonId"]),
            };
        }
    }

    public sealed class FarmStatus
    {
        public string DayDate;
        public bool ClaimedToday;
        public long NextClaimAtMs, BaseCents, TodayAmountCents, DailyCapCents;
        public int SgtBonusBps, TribeBonusBps, StreakDays;
        public SybilStatus Sybil;

        public static FarmStatus Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Farm status");
            return new FarmStatus
            {
                DayDate = CompWire.Plain(o["dayDate"], 12),
                ClaimedToday = CompWire.BoolOr(o["claimedToday"], false),
                NextClaimAtMs = CompWire.TimeMs(o["nextClaimAt"]) ?? 0,
                BaseCents = CompWire.LongOr(o["baseCents"], 0),
                SgtBonusBps = CompWire.IntOr(o["sgtBonusBps"], 0),
                TribeBonusBps = CompWire.IntOr(o["tribeBonusBps"], 0),
                TodayAmountCents = CompWire.LongOr(o["todayAmountCents"], 0),
                DailyCapCents = CompWire.LongOr(o["dailyCapCents"], 0),
                StreakDays = CompWire.IntOr(o["streakDays"], 0),
                Sybil = o["sybil"] is JObject s ? SybilStatus.Parse(s) : null,
            };
        }
    }

    public sealed class AirdropClaim
    {
        public string ClaimId, DayDate;
        public int ClaimCount;
        public long AmountCents;
        public bool SgtBonus;
        public long? ClaimedAtMs;

        public static AirdropClaim Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Airdrop claim");
            return new AirdropClaim
            {
                ClaimId = CompWire.Str(o["claimId"]),
                DayDate = CompWire.Plain(o["dayDate"], 12),
                ClaimCount = CompWire.IntOr(o["claimCount"], 1),
                AmountCents = CompWire.Long(o["amountCents"]),
                SgtBonus = CompWire.BoolOr(o["sgtBonus"], false),
                ClaimedAtMs = CompWire.TimeMsOrNull(o["claimedAt"]),
            };
        }

        public static List<AirdropClaim> ParseList(JToken t) => CompWire.List(t, "claims", "items").Select(Parse).ToList();
    }

    public sealed class SybilInmate
    {
        public string UserId, Alias;
        public long? JailedAtMs, ReleaseAtMs;
    }

    public sealed class SybilBarn
    {
        public int Count;
        public List<SybilInmate> Inmates = new List<SybilInmate>();
        public string NextCursor;

        public static SybilBarn Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Sybil Barn");
            var b = new SybilBarn { Count = CompWire.IntOr(o["count"], 0), NextCursor = CompWire.Str(o["nextCursor"]) };
            if (o["inmates"] is JArray a)
                foreach (var x in a)
                    b.Inmates.Add(new SybilInmate
                    {
                        UserId = CompWire.Str(x["userId"]),
                        Alias = CompWire.Plain(x["alias"], 32),
                        JailedAtMs = CompWire.TimeMsOrNull(x["jailedAt"]),
                        ReleaseAtMs = CompWire.TimeMsOrNull(x["releaseAt"]),
                    });
            return b;
        }
    }

    public sealed class VenueAccess
    {
        public string VenueId, ReasonCode;
        public bool Allowed, MembershipRequired, IsMember;
        public long? RequiredNetWorthCents;
        public long NetWorthCents;

        public static VenueAccess Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Venue access");
            return new VenueAccess
            {
                VenueId = CompWire.Str(o["venueId"]),
                Allowed = CompWire.BoolOr(o["allowed"], false),
                ReasonCode = CompWire.Str(o["reasonCode"]),
                RequiredNetWorthCents = CompWire.OptLong(o["requiredNetWorthCents"]),
                NetWorthCents = CompWire.LongOr(o["netWorthCents"], 0),
                MembershipRequired = CompWire.BoolOr(o["membershipRequired"], false),
                IsMember = CompWire.BoolOr(o["isMember"], false),
            };
        }
    }

    public sealed class CabalMembership
    {
        public bool Member;
        public long? SinceMs, ExpiresAtMs;
        public int InvitesRemaining;
        public long HostGateCents;

        public static CabalMembership Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Cabal membership");
            return new CabalMembership
            {
                Member = CompWire.BoolOr(o["member"], false),
                SinceMs = CompWire.TimeMsOrNull(o["since"]),
                ExpiresAtMs = CompWire.TimeMsOrNull(o["expiresAt"]),
                InvitesRemaining = CompWire.IntOr(o["invitesRemaining"], 0),
                HostGateCents = CompWire.LongOr(o["hostGateCents"], 0),
            };
        }
    }

    /// <summary>One Cabal invite. The proposal never returns a bearer code, and this model has no field for one.</summary>
    public sealed class CabalInvite
    {
        public string InviteId, HostUserId, GuestUserId, Status;
        public long? ExpiresAtMs, CreatedAtMs, UsedAtMs;

        public static CabalInvite Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Cabal invite");
            return new CabalInvite
            {
                InviteId = CompWire.ReqStr(o["inviteId"] ?? o["id"], "inviteId"),
                HostUserId = CompWire.Str(o["hostUserId"] ?? o["hostId"]),
                GuestUserId = CompWire.Str(o["guestUserId"] ?? o["guestId"]),
                Status = CompWire.Str(o["status"]) ?? (CompWire.IsNull(o["usedAt"]) ? "pending" : "accepted"),
                ExpiresAtMs = CompWire.TimeMsOrNull(o["expiresAt"]),
                CreatedAtMs = CompWire.TimeMsOrNull(o["createdAt"]),
                UsedAtMs = CompWire.TimeMsOrNull(o["usedAt"]),
            };
        }

        public static List<CabalInvite> ParseList(JToken t) => CompWire.List(t, "invites", "items").Select(Parse).ToList();
    }

    public sealed class LlmQuota
    {
        public int PerMinuteLimit, RemainingThisMinute, PerDayLimit, RemainingToday;
        public long? ResetsAtMs;

        public static LlmQuota Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("LLM quota");
            return new LlmQuota
            {
                PerMinuteLimit = CompWire.IntOr(o["perMinuteLimit"], 0),
                RemainingThisMinute = CompWire.IntOr(o["remainingThisMinute"], 0),
                PerDayLimit = CompWire.IntOr(o["perDayLimit"], 0),
                RemainingToday = CompWire.IntOr(o["remainingToday"], 0),
                ResetsAtMs = CompWire.TimeMsOrNull(o["resetsAt"]),
            };
        }
    }

    public sealed class LlmReply
    {
        public string ReplyId, Archetype, Text, RefusalReasonId;
        public bool Filtered, Refused, AiGenerated;
        public LlmQuota Quota;

        public static LlmReply Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("LLM reply");
            return new LlmReply
            {
                ReplyId = CompWire.Str(o["replyId"]),
                Archetype = CompWire.Str(o["archetype"]),
                // Cap what we will draw; the server's own cap is the chat filter's, this only protects the layout.
                Text = CompWire.Plain(CompWire.Str(o["text"] ?? o["response"]), 1200),
                Filtered = CompWire.BoolOr(o["filtered"], false),
                Refused = CompWire.BoolOr(o["refused"], false),
                RefusalReasonId = CompWire.Str(o["refusalReasonId"] ?? o["refusalReason"]),
                // The client always labels replies as AI-generated, whatever the flag says.
                AiGenerated = true,
                Quota = o["quota"] is JObject q ? LlmQuota.Parse(q) : null,
            };
        }
    }

    public sealed class XLinkInfo
    {
        public string XHandle, XAvatarUrl, XUserId;
        public long? VerifiedAtMs, RefreshedAtMs;

        /// <summary>`{link}` with null when not linked; returns null then.</summary>
        public static XLinkInfo ParseStatus(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("X link");
            if (CompWire.IsNull(o["link"])) return null;
            var l = o["link"] as JObject ?? throw new FormatException("X link");
            return new XLinkInfo
            {
                XHandle = XLinkRules.CleanHandle(CompWire.Str(l["xHandle"])),
                XAvatarUrl = XLinkRules.SafeAvatarUrl(CompWire.Str(l["xAvatarUrl"])),
                XUserId = CompWire.Str(l["xUserId"]),
                VerifiedAtMs = CompWire.TimeMsOrNull(l["verifiedAt"]),
                RefreshedAtMs = CompWire.TimeMsOrNull(l["refreshedAt"]),
            };
        }
    }
}
