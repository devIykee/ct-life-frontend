using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CTLife.Competition
{
    // Server -> client messages from docs/fe-contract-proposals/phase4-6/realtime-additions.ts (Phase 6). Parsing is tolerant:
    // a missing required field drops the message (returns null), unknown fields are ignored, unknown enum strings are kept.

    public sealed class DistrictFlipped { public string DistrictId, TribeId, PreviousTribeId; public long AtMs; }

    public sealed class SeasonRollover { public string SeasonId, PreviousSeasonId; public long AtMs; }

    public sealed class LeagueResultMsg { public string SeasonId, FromLeagueId, ToLeagueId, Outcome; public int Rank; }

    public sealed class BeefChallengeMsg { public string MatchId, FromUserId, TopicId; public long StakeCents; public long ExpiresAtMs; }

    public sealed class BeefUpdateMsg { public string MatchId, Status, ChallengerUserId, AccepterUserId; public long? PhaseEndsAtMs; }

    public sealed class BeefResultMsg
    {
        public string MatchId, WinnerUserId;
        public int VotesChallenger, VotesAccepter, EloDeltaChallenger, EloDeltaAccepter;
        public long PayoutCents, AtMs;
    }

    public sealed class SybilStatusMsg { public bool Jailed; public string ReasonId; public long? ReleaseAtMs; }

    public sealed class CabalInviteMsg { public string InviteId, FromUserId; public long ExpiresAtMs; }

    public sealed class XLinkUpdateMsg { public string Status, Handle, ReasonId; }

    public static class CompetitionMessages
    {
        /// <summary>The Phase 6 message types on the `city` room (and `beef_update`/`beef_result` also on `ratio_arena` venue rooms).</summary>
        public static readonly string[] Types =
        {
            "district_flipped", "season_rollover", "league_result", "beef_challenge", "beef_update", "beef_result",
            "sybil_status", "cabal_invite", "x_link_update",
        };

        public static bool IsPhase6(string type) => Array.IndexOf(Types, type) >= 0;

        /// <summary>Returns the typed message, or null when the type is unknown or a required field is missing or malformed.</summary>
        public static object Parse(string type, JObject o)
        {
            if (o == null) return null;
            try
            {
                switch (type)
                {
                    case "district_flipped":
                        return new DistrictFlipped
                        {
                            DistrictId = Req(o, "districtId"), TribeId = Req(o, "tribeId"),
                            PreviousTribeId = CompWire.Str(o["previousTribeId"]), AtMs = CompWire.LongOr(o["at"], 0),
                        };
                    case "season_rollover":
                        return new SeasonRollover { SeasonId = Req(o, "seasonId"), PreviousSeasonId = CompWire.Str(o["previousSeasonId"]), AtMs = CompWire.LongOr(o["at"], 0) };
                    case "league_result":
                        return new LeagueResultMsg
                        {
                            SeasonId = Req(o, "seasonId"), FromLeagueId = CompWire.Str(o["fromLeagueId"]), ToLeagueId = Req(o, "toLeagueId"),
                            Rank = CompWire.IntOr(o["rank"], 0), Outcome = CompWire.Str(o["outcome"]) ?? "stayed",
                        };
                    case "beef_challenge":
                        return new BeefChallengeMsg
                        {
                            MatchId = Req(o, "matchId"), FromUserId = Req(o, "fromUserId"), TopicId = CompWire.Plain(o["topicId"], 60),
                            StakeCents = CompWire.Long(o["stakeCents"]), ExpiresAtMs = CompWire.LongOr(o["expiresAt"], 0),
                        };
                    case "beef_update":
                        return new BeefUpdateMsg
                        {
                            MatchId = Req(o, "matchId"), Status = Req(o, "status"), ChallengerUserId = Req(o, "challengerUserId"),
                            AccepterUserId = CompWire.Str(o["accepterUserId"]), PhaseEndsAtMs = CompWire.OptLong(o["phaseEndsAt"]),
                        };
                    case "beef_result":
                        return new BeefResultMsg
                        {
                            MatchId = Req(o, "matchId"), WinnerUserId = CompWire.Str(o["winnerUserId"]),
                            VotesChallenger = CompWire.IntOr(o["votesChallenger"], 0), VotesAccepter = CompWire.IntOr(o["votesAccepter"], 0),
                            EloDeltaChallenger = CompWire.IntOr(o["eloDeltaChallenger"], 0), EloDeltaAccepter = CompWire.IntOr(o["eloDeltaAccepter"], 0),
                            PayoutCents = CompWire.LongOr(o["payoutCents"], 0), AtMs = CompWire.LongOr(o["at"], 0),
                        };
                    case "sybil_status":
                        if (o["jailed"] == null || o["jailed"].Type != JTokenType.Boolean) return null;
                        return new SybilStatusMsg { Jailed = (bool)o["jailed"], ReasonId = CompWire.Str(o["reasonId"]), ReleaseAtMs = CompWire.OptLong(o["releaseAt"]) };
                    case "cabal_invite":
                        return new CabalInviteMsg { InviteId = Req(o, "inviteId"), FromUserId = CompWire.Str(o["fromUserId"]), ExpiresAtMs = CompWire.LongOr(o["expiresAt"], 0) };
                    case "x_link_update":
                        return new XLinkUpdateMsg { Status = Req(o, "status"), Handle = XLinkRules.CleanHandle(CompWire.Str(o["handle"])), ReasonId = CompWire.Plain(o["reasonId"], 60) };
                    default:
                        return null;
                }
            }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }
        }

        private static string Req(JObject o, string field)
        {
            var s = CompWire.Str(o[field]);
            if (string.IsNullOrEmpty(s)) throw new FormatException(field);
            return s;
        }
    }

    /// <summary>
    /// The live Phase 6 state fed by realtime messages, and the C# events screens subscribe to. Pure: toasts go to an injectable sink
    /// so tests can run without Unity. The composition root forwards `CityChannel` raw messages with
    /// <see cref="Dispatch(string, object)"/> (hook in docs/progress/requests.md).
    /// </summary>
    public sealed class CompetitionFeed
    {
        public static readonly CompetitionFeed Shared = new CompetitionFeed();

        public readonly DistrictBoard Districts = new DistrictBoard();
        public readonly ArenaBoard Arena = new ArenaBoard();

        public SybilStatusMsg Sybil { get; private set; }
        public LeagueResultMsg LastLeagueResult { get; private set; }
        public string CurrentSeasonId { get; private set; }
        public XLinkUpdateMsg LastXLink { get; internal set; }
        /// <summary>Pending Cabal invites pushed to me, newest last. Ids only: the screen fetches details from the server.</summary>
        public readonly List<CabalInviteMsg> CabalInvites = new List<CabalInviteMsg>();
        public readonly List<BeefChallengeMsg> BeefChallenges = new List<BeefChallengeMsg>();

        /// <summary>Set by the composition root: (text, good/bad/info). Null in tests.</summary>
        public Action<string, ToastTone> Toast;
        /// <summary>The signed-in user id, to word results from my side.</summary>
        public Func<string> MyUserId = () => null;
        /// <summary>The player's tribe id, from the profile, to word district flips from my side.</summary>
        public Func<string> MyTribeId = () => null;
        public Func<string, string> TribeName = id => CompStrings.Readable(id);
        public Func<string, string> DistrictName = id => CompStrings.Readable(id);
        public Func<string, string> LeagueName = id => CompStrings.Readable(id);
        public Func<long> NowMs = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        /// <summary>Turn off when the server already sends the matching `toast` message (avoids doubles).</summary>
        public bool ToastsEnabled = true;

        public event Action<DistrictFlipped> DistrictFlippedEvent;
        public event Action<SeasonRollover> SeasonRolloverEvent;
        public event Action<LeagueResultMsg> LeagueResultEvent;
        public event Action<BeefChallengeMsg> BeefChallengeEvent;
        public event Action<BeefUpdateMsg> BeefUpdateEvent;
        public event Action<BeefResultMsg> BeefResultEvent;
        public event Action<SybilStatusMsg> SybilEvent;
        public event Action<CabalInviteMsg> CabalInviteEvent;
        public event Action<XLinkUpdateMsg> XLinkEvent;

        public void Reset()
        {
            Districts.Clear();
            Arena.Clear();
            Sybil = null;
            LastLeagueResult = null;
            CurrentSeasonId = null;
            LastXLink = null;
            CabalInvites.Clear();
            BeefChallenges.Clear();
        }

        /// <summary>Entry point for raw room messages. Returns true when the type is a Phase 6 message that parsed.</summary>
        public bool Dispatch(string type, object payload)
        {
            if (!CompetitionMessages.IsPhase6(type)) return false;
            var msg = CompetitionMessages.Parse(type, CompWire.Obj(payload));
            if (msg == null) return false;
            Apply(msg);
            return true;
        }

        public void Apply(object msg)
        {
            switch (msg)
            {
                case DistrictFlipped d:
                    Districts.ApplyFlip(d);
                    Say(d.TribeId == MyTribe() && MyTribe() != null ? "feed.districtWon" : d.PreviousTribeId == MyTribe() && MyTribe() != null ? "feed.districtLost" : "feed.districtFlipped",
                        ToneFor(d), DistrictName(d.DistrictId), TribeName(d.TribeId));
                    DistrictFlippedEvent?.Invoke(d);
                    break;
                case SeasonRollover s:
                    CurrentSeasonId = s.SeasonId;
                    Say("feed.seasonRollover", ToastTone.Info);
                    SeasonRolloverEvent?.Invoke(s);
                    break;
                case LeagueResultMsg l:
                    LastLeagueResult = l;
                    Say(LeagueRules.OutcomeKey(l.Outcome) == "league.promoted" ? "feed.promoted" : l.Outcome == "relegated" ? "feed.relegated" : "feed.stayed",
                        l.Outcome == "promoted" ? ToastTone.Good : l.Outcome == "relegated" ? ToastTone.Bad : ToastTone.Info, LeagueName(l.ToLeagueId), l.Rank);
                    LeagueResultEvent?.Invoke(l);
                    break;
                case BeefChallengeMsg c:
                    BeefChallenges.RemoveAll(x => x.MatchId == c.MatchId);
                    BeefChallenges.Add(c);
                    Say("feed.beefChallenge", ToastTone.Info);
                    BeefChallengeEvent?.Invoke(c);
                    break;
                case BeefUpdateMsg u:
                    if (!Arena.ApplyUpdate(u)) break;
                    if (BeefRules.IsFinished(BeefRules.PhaseOf(u.Status))) BeefChallenges.RemoveAll(x => x.MatchId == u.MatchId);
                    BeefUpdateEvent?.Invoke(u);
                    break;
                case BeefResultMsg r:
                    if (!Arena.ApplyResult(r)) break;
                    BeefChallenges.RemoveAll(x => x.MatchId == r.MatchId);
                    ToastResult(r);
                    BeefResultEvent?.Invoke(r);
                    break;
                case SybilStatusMsg y:
                    Sybil = y;
                    Say(y.Jailed ? "feed.jailed" : "feed.released", y.Jailed ? ToastTone.Bad : ToastTone.Good);
                    SybilEvent?.Invoke(y);
                    break;
                case CabalInviteMsg i:
                    CabalInvites.RemoveAll(x => x.InviteId == i.InviteId);
                    CabalInvites.Add(i);
                    Say("feed.cabalInvite", ToastTone.Info);
                    CabalInviteEvent?.Invoke(i);
                    break;
                case XLinkUpdateMsg x:
                    LastXLink = x;
                    Say(x.Status == "linked" ? "feed.xLinked" : x.Status == "unlinked" ? "feed.xUnlinked" : "feed.xFailed", x.Status == "linked" ? ToastTone.Good : x.Status == "failed" ? ToastTone.Bad : ToastTone.Info);
                    XLinkEvent?.Invoke(x);
                    break;
            }
        }

        private string MyTribe() => MyTribeId?.Invoke();

        private ToastTone ToneFor(DistrictFlipped d)
        {
            var mine = MyTribe();
            if (mine == null) return ToastTone.Info;
            return d.TribeId == mine ? ToastTone.Good : d.PreviousTribeId == mine ? ToastTone.Bad : ToastTone.Info;
        }

        private void ToastResult(BeefResultMsg r)
        {
            var me = MyUserId();
            var m = Arena.Find(r.MatchId);
            bool mine = (m != null && (m.ChallengerUserId == me || m.AccepterUserId == me)) || (!string.IsNullOrEmpty(me) && r.WinnerUserId == me);
            if (!mine) return;
            if (string.IsNullOrEmpty(r.WinnerUserId)) Say("feed.beefDraw", ToastTone.Info);
            else if (r.WinnerUserId == me) Say("feed.beefWon", ToastTone.Good, CTLife.Core.Money.Cash(r.PayoutCents));
            else Say("feed.beefLost", ToastTone.Bad);
        }

        private void Say(string key, ToastTone tone, params object[] args)
        {
            if (!ToastsEnabled || Toast == null) return;
            Toast(CompStrings.F(key, args), tone);
        }
    }

    public enum ToastTone { Info, Good, Bad }
}
