using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;

namespace CTLife.Competition
{
    public enum BeefPhase { Unknown, Open, Active, Voting, Closed, Declined, Cancelled, Expired }

    public enum BeefRole { Spectator, Challenger, Invited, Accepter }

    /// <summary>What the viewer may try. The server decides; a button that should not be shown is merely hidden here.</summary>
    public struct BeefActions
    {
        public bool CanAccept, CanDecline, CanCancel, CanVote;
    }

    /// <summary>
    /// Presentation rules for CT Beef. Settlement, vote counting, eligibility and Elo are server-authoritative: nothing here
    /// computes a winner or an Elo value, it only reads what the server sent.
    /// </summary>
    public static class BeefRules
    {
        public static BeefPhase PhaseOf(string status)
        {
            switch (status)
            {
                case "open": return BeefPhase.Open;
                case "active": return BeefPhase.Active;
                case "voting": return BeefPhase.Voting;
                case "closed": return BeefPhase.Closed;
                case "declined": return BeefPhase.Declined;
                case "cancelled": return BeefPhase.Cancelled;
                case "expired": return BeefPhase.Expired;
                default: return BeefPhase.Unknown;
            }
        }

        public static bool IsFinished(BeefPhase p) =>
            p == BeefPhase.Closed || p == BeefPhase.Declined || p == BeefPhase.Cancelled || p == BeefPhase.Expired;

        public static BeefRole RoleOf(BeefMatch m, string me)
        {
            if (m == null || string.IsNullOrEmpty(me)) return BeefRole.Spectator;
            if (m.ChallengerUserId == me) return BeefRole.Challenger;
            if (m.AccepterUserId == me) return BeefRole.Accepter;
            if (m.InvitedUserId == me && PhaseOf(m.Status) == BeefPhase.Open) return BeefRole.Invited;
            return BeefRole.Spectator;
        }

        public static BeefActions ActionsFor(BeefMatch m, string me)
        {
            var a = new BeefActions();
            if (m == null || string.IsNullOrEmpty(me)) return a;
            var phase = PhaseOf(m.Status);
            var role = RoleOf(m, me);
            if (phase == BeefPhase.Open)
            {
                bool openToAll = string.IsNullOrEmpty(m.InvitedUserId);
                a.CanAccept = role == BeefRole.Invited || (openToAll && role == BeefRole.Spectator);
                a.CanDecline = role == BeefRole.Invited;
                a.CanCancel = role == BeefRole.Challenger;
            }
            // Participants cannot vote. Jailed or too-new accounts are refused by the server (VOTE_NOT_ALLOWED).
            if (phase == BeefPhase.Voting && role == BeefRole.Spectator && string.IsNullOrEmpty(m.MyVote)) a.CanVote = true;
            return a;
        }

        /// <summary>Null when fine. Only structural checks: a positive whole-cent stake. The server owns minimum, maximum and balance.</summary>
        public static string StakeError(long stakeCents)
        {
            if (stakeCents <= 0) return "stake.positive";
            return null;
        }

        /// <summary>Parses a dollars field ("12", "12.5", "12.50") to cents. False when it is not a plain non-negative amount.</summary>
        public static bool TryParseStakeCents(string text, out long cents)
        {
            cents = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.Trim();
            if (s.StartsWith("$", StringComparison.Ordinal)) s = s.Substring(1);
            var parts = s.Split('.');
            if (parts.Length > 2 || parts[0].Length == 0 && parts.Length == 1) return false;
            if (parts[0].Length > 12 || !parts[0].All(char.IsDigit)) return false;
            string frac = parts.Length == 2 ? parts[1] : "";
            if (frac.Length > 2 || !frac.All(char.IsDigit)) return false;
            if (parts[0].Length == 0 && frac.Length == 0) return false;
            long whole = parts[0].Length == 0 ? 0 : long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            long f = frac.Length == 0 ? 0 : long.Parse(frac.PadRight(2, '0'), System.Globalization.CultureInfo.InvariantCulture);
            cents = whole * 100 + f;
            return true;
        }

        public static string TopicLabel(string topicId) => CompStrings.Has("topic." + topicId) ? CompStrings.T("topic." + topicId) : CompStrings.Readable(topicId);

        /// <summary>The viewer's outcome once a match is closed. "none" when it is not closed or the viewer did not take part.</summary>
        public static string OutcomeFor(BeefMatch m, string me)
        {
            if (m == null || PhaseOf(m.Status) != BeefPhase.Closed) return "none";
            var role = RoleOf(m, me);
            if (role != BeefRole.Challenger && role != BeefRole.Accepter) return "none";
            if (string.IsNullOrEmpty(m.WinnerUserId)) return "draw";
            return m.WinnerUserId == me ? "won" : "lost";
        }

        /// <summary>Elo change for the viewer from the server's pre/post values; null when either is missing.</summary>
        public static int? EloDeltaFor(BeefMatch m, string me)
        {
            var role = RoleOf(m, me);
            if (role == BeefRole.Challenger && m.EloChallengerPre.HasValue && m.EloChallengerPost.HasValue) return m.EloChallengerPost.Value - m.EloChallengerPre.Value;
            if (role == BeefRole.Accepter && m.EloAccepterPre.HasValue && m.EloAccepterPost.HasValue) return m.EloAccepterPost.Value - m.EloAccepterPre.Value;
            return null;
        }

        /// <summary>Total of both stakes minus the server's fee, for display as an estimate. The server pays what it pays.</summary>
        public static long EstimatedPayoutCents(BeefMatch m)
        {
            long pot = checked(m.StakeCents * 2);
            long fee = pot * Math.Max(0, Math.Min(10000, m.FeeBps)) / 10000;
            return pot - fee;
        }

        public static string SignedElo(int delta) => (delta > 0 ? "+" : "") + delta.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Open/active/voting first (soonest end first), then finished matches newest first.</summary>
        public static List<BeefMatch> Order(IEnumerable<BeefMatch> matches)
        {
            return matches.OrderBy(m => IsFinished(PhaseOf(m.Status)) ? 1 : 0)
                .ThenBy(m => IsFinished(PhaseOf(m.Status)) ? 0 : (m.PhaseEndsAtMs ?? long.MaxValue))
                .ThenByDescending(m => m.SettledAtMs ?? m.OpenedAtMs ?? 0).ToList();
        }
    }

    /// <summary>Net-worth gates and venue access messaging. The decision is the server's.</summary>
    public static class GateRules
    {
        public static readonly string[] GatedVenues = { "dubai_marina", "cabal_lounge", "sybil_barn" };

        public static bool IsGated(string venueId) => Array.IndexOf(GatedVenues, venueId) >= 0;

        /// <summary>One line telling the player why they cannot enter, or null when allowed. Never shows an invite code.</summary>
        public static string Message(VenueAccess a)
        {
            if (a == null || a.Allowed) return null;
            switch (a.ReasonCode)
            {
                case "NET_WORTH_TOO_LOW":
                    return a.RequiredNetWorthCents.HasValue
                        ? CompStrings.F("gate.netWorth", Money.Cash(a.RequiredNetWorthCents.Value), Money.Cash(a.NetWorthCents))
                        : CompStrings.T("err.netWorth");
                case "INVITE_REQUIRED":
                case "NOT_A_MEMBER": return CompStrings.T("gate.invite"); // The server's code for cabal_lounge (venues-gating.ts).
                case "SYBIL_JAILED": return CompStrings.T("gate.sybil");
                case "NOT_JAILED": return CompStrings.T("gate.notJailed"); // The server's code for sybil_barn.
                default: return CompStrings.T("gate.denied");
            }
        }

        /// <summary>Remaining net worth to reach the gate, or null when unknown or already met.</summary>
        public static long? Shortfall(VenueAccess a)
        {
            if (a == null || !a.RequiredNetWorthCents.HasValue) return null;
            long s = a.RequiredNetWorthCents.Value - a.NetWorthCents;
            return s > 0 ? s : (long?)null;
        }

        /// <summary>Host side: can the player start an invite, judged only from server fields.</summary>
        public static string HostBlock(CabalMembership m)
        {
            if (m == null) return null;
            if (!m.Member) return "cabal.notMember";
            if (m.InvitesRemaining <= 0) return "cabal.noInvites";
            return null;
        }

        public static bool InviteIsPendingFor(CabalInvite i, string me, long nowMs) =>
            i != null && i.Status == "pending" && i.GuestUserId == me && (!i.ExpiresAtMs.HasValue || i.ExpiresAtMs.Value > nowMs);
    }

    public static class FarmRules
    {
        public static bool CanClaim(FarmStatus f, SybilStatus sybil, long nowMs)
        {
            if (f == null) return false;
            if ((sybil ?? f.Sybil)?.Jailed == true) return false;
            return !f.ClaimedToday || (f.NextClaimAtMs > 0 && nowMs >= f.NextClaimAtMs);
        }

        public static long MsUntilNextClaim(FarmStatus f, long nowMs) => f == null ? 0 : Math.Max(0, f.NextClaimAtMs - nowMs);

        public static string JailReasonLabel(string reasonId)
        {
            if (string.IsNullOrEmpty(reasonId)) return CompStrings.T("sybil.reason.unknown");
            return CompStrings.Has("sybil.reason." + reasonId) ? CompStrings.T("sybil.reason." + reasonId) : CompStrings.T("sybil.reason.unknown");
        }
    }

    public static class LeagueRules
    {
        public static string LeagueLabel(IReadOnlyList<LeagueInfo> leagues, string idOrSlug)
        {
            if (string.IsNullOrEmpty(idOrSlug)) return "—";
            var l = leagues?.FirstOrDefault(x => x.Id == idOrSlug || x.Slug == idOrSlug);
            return l != null && !string.IsNullOrEmpty(l.Label) ? l.Label : CompStrings.Readable(idOrSlug);
        }

        public static string OutcomeKey(string outcome)
        {
            switch (outcome)
            {
                case "promoted": return "league.promoted";
                case "relegated": return "league.relegated";
                default: return "league.stayed";
            }
        }
    }

    /// <summary>The X-link display rules (X Developer Policy as summarised in the proposal): handle and avatar only, https only.</summary>
    public static class XLinkRules
    {
        private static readonly string[] AuthHosts = { "x.com", "twitter.com" };

        /// <summary>
        /// The authorize URL must be https on x.com or twitter.com (or a subdomain), with no userinfo. [Unverified] These host names are
        /// the client's allow-list; X's docs name both. Anything else is refused and never opened.
        /// </summary>
        public static bool IsSafeAuthUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
            if (u.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(u.UserInfo)) return false;
            var host = u.Host.ToLowerInvariant();
            return AuthHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));
        }

        /// <summary>The avatar must be https; null otherwise (the UI then shows the handle alone).</summary>
        public static string SafeAvatarUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || url.Length > 512) return null;
            return Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(u.UserInfo) ? u.AbsoluteUri : null;
        }

        /// <summary>A handle without '@': letters, digits and underscores, 1..32. Null when it is anything else.</summary>
        public static string CleanHandle(string handle)
        {
            if (string.IsNullOrEmpty(handle)) return null;
            var h = handle.StartsWith("@", StringComparison.Ordinal) ? handle.Substring(1) : handle;
            if (h.Length < 1 || h.Length > 32) return null;
            return h.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_') ? h : null;
        }

        public static string Display(string handle) => CleanHandle(handle) is string h ? "@" + h : null;
    }
}
