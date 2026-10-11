using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.Competition
{
    /// <summary>One district flag for the city map. Everything here comes from the server; nothing is defaulted.</summary>
    public sealed class DistrictFlag
    {
        public string DistrictId, Name;
        /// <summary>Tribe holding the flag, or null when the server reports no owner.</summary>
        public string OwnerTribeId;
        public string DefaultHolderTribeId;
        public long? SinceMs;
        public List<string> VenueIds = new List<string>();
        /// <summary>Epoch ms of the last flip seen live this session (0 = none), so the map can pulse a fresh flip.</summary>
        public long FlippedAtMs;
        public string PreviousTribeId;
    }

    /// <summary>
    /// Tribe district control as data for the city map (spec #38 "District flags"). The map scene reads <see cref="Flags"/> and
    /// subscribes to <see cref="Changed"/>; this class never invents a district: with no server data <see cref="Loaded"/> is false.
    /// Hook for the map: docs/progress/requests.md, entry "2026-10-08 phase6-competition".
    /// </summary>
    public sealed class DistrictBoard
    {
        private readonly Dictionary<string, DistrictFlag> _byId = new Dictionary<string, DistrictFlag>();
        private readonly List<string> _order = new List<string>();

        public bool Loaded { get; private set; }
        public event Action Changed;

        public IReadOnlyList<DistrictFlag> Flags => _order.Select(id => _byId[id]).ToList();

        public DistrictFlag Get(string districtId) => districtId != null && _byId.TryGetValue(districtId, out var f) ? f : null;

        /// <summary>The flag for the district that contains a venue, or null.</summary>
        public DistrictFlag ForVenue(string venueId) => _order.Select(id => _byId[id]).FirstOrDefault(f => f.VenueIds.Contains(venueId));

        /// <summary>The tribe that effectively holds a district: the owner, else the server's default holder.</summary>
        public static string HolderOf(DistrictFlag f) => f == null ? null : f.OwnerTribeId ?? f.DefaultHolderTribeId;

        public void Clear()
        {
            _byId.Clear();
            _order.Clear();
            Loaded = false;
            Changed?.Invoke();
        }

        public void ApplyList(IEnumerable<DistrictInfo> districts)
        {
            var seen = new HashSet<string>();
            foreach (var d in districts)
            {
                seen.Add(d.Id);
                if (!_byId.TryGetValue(d.Id, out var f))
                {
                    f = new DistrictFlag { DistrictId = d.Id };
                    _byId[d.Id] = f;
                    _order.Add(d.Id);
                }
                // A live flip newer than the fetched snapshot must not be rolled back by a slightly older list.
                bool liveNewer = f.FlippedAtMs > 0 && d.ControlledSinceMs.HasValue && f.FlippedAtMs > d.ControlledSinceMs.Value;
                f.Name = d.Name;
                f.VenueIds = d.VenueIds;
                f.DefaultHolderTribeId = d.DefaultHolderTribeId;
                if (!liveNewer)
                {
                    f.OwnerTribeId = d.OwnerTribeId;
                    f.SinceMs = d.ControlledSinceMs;
                }
            }
            foreach (var id in _order.Where(id => !seen.Contains(id)).ToList()) { _byId.Remove(id); _order.Remove(id); }
            Loaded = true;
            Changed?.Invoke();
        }

        public void ApplyFlip(DistrictFlipped m)
        {
            if (!_byId.TryGetValue(m.DistrictId, out var f))
            {
                // A flip for a district we have not fetched yet: keep it, the next list fills in name and venues.
                f = new DistrictFlag { DistrictId = m.DistrictId, Name = CompStrings.Readable(m.DistrictId) };
                _byId[m.DistrictId] = f;
                _order.Add(m.DistrictId);
            }
            f.PreviousTribeId = m.PreviousTribeId ?? f.OwnerTribeId;
            f.OwnerTribeId = m.TribeId;
            f.SinceMs = m.AtMs > 0 ? m.AtMs : (long?)null;
            f.FlippedAtMs = m.AtMs;
            Changed?.Invoke();
        }
    }

    public sealed class ArenaMatch
    {
        public string MatchId, Status, ChallengerUserId, AccepterUserId, WinnerUserId;
        public long? PhaseEndsAtMs;
        public bool HasResult;
        public int VotesChallenger, VotesAccepter;
    }

    /// <summary>
    /// What the Ratio Arena board (`Hook_RatioMeter`) and any arena presentation can show, from `beef_update` / `beef_result`
    /// received while inside the venue (the server broadcasts both on every `ratio_arena` instance). No vote counts appear
    /// before `beef_result`: the proposal hides live tallies, and so does this board.
    /// </summary>
    public sealed class ArenaBoard
    {
        private readonly Dictionary<string, ArenaMatch> _matches = new Dictionary<string, ArenaMatch>();
        private readonly List<string> _recent = new List<string>();
        /// <summary>Keeps the arena memory small; not a game rule.</summary>
        public const int Keep = 8;

        public event Action Changed;

        public ArenaMatch Find(string matchId) => matchId != null && _matches.TryGetValue(matchId, out var m) ? m : null;

        public void Clear()
        {
            _matches.Clear();
            _recent.Clear();
            Changed?.Invoke();
        }

        private ArenaMatch Touch(string id)
        {
            if (!_matches.TryGetValue(id, out var m))
            {
                m = new ArenaMatch { MatchId = id };
                _matches[id] = m;
            }
            _recent.Remove(id);
            _recent.Add(id);
            while (_recent.Count > Keep) { _matches.Remove(_recent[0]); _recent.RemoveAt(0); }
            return m;
        }

        /// <summary>False when nothing changed (the same update also arrives on the city room and on the venue room).</summary>
        public bool ApplyUpdate(BeefUpdateMsg u)
        {
            var existing = Find(u.MatchId);
            if (existing != null && existing.Status == u.Status && existing.PhaseEndsAtMs == u.PhaseEndsAtMs && existing.ChallengerUserId == u.ChallengerUserId
                && (u.AccepterUserId == null || existing.AccepterUserId == u.AccepterUserId)) return false;
            var m = Touch(u.MatchId);
            m.Status = u.Status;
            m.ChallengerUserId = u.ChallengerUserId;
            m.AccepterUserId = u.AccepterUserId ?? m.AccepterUserId;
            m.PhaseEndsAtMs = u.PhaseEndsAtMs;
            Changed?.Invoke();
            return true;
        }

        /// <summary>False when this result was already applied.</summary>
        public bool ApplyResult(BeefResultMsg r)
        {
            if (Find(r.MatchId)?.HasResult == true) return false;
            var m = Touch(r.MatchId);
            m.HasResult = true;
            m.WinnerUserId = r.WinnerUserId;
            m.VotesChallenger = r.VotesChallenger;
            m.VotesAccepter = r.VotesAccepter;
            if (string.IsNullOrEmpty(m.Status) || !BeefRules.IsFinished(BeefRules.PhaseOf(m.Status))) m.Status = "closed";
            Changed?.Invoke();
            return true;
        }

        /// <summary>The match to feature on the board: a live one (voting > active > open, soonest end first), else the latest finished.</summary>
        public ArenaMatch Featured()
        {
            var all = _recent.Select(id => _matches[id]).ToList();
            int Rank(ArenaMatch m)
            {
                switch (BeefRules.PhaseOf(m.Status))
                {
                    case BeefPhase.Voting: return 0;
                    case BeefPhase.Active: return 1;
                    case BeefPhase.Open: return 2;
                    default: return 3;
                }
            }
            return all.Where(m => Rank(m) < 3).OrderBy(Rank).ThenBy(m => m.PhaseEndsAtMs ?? long.MaxValue).FirstOrDefault()
                   ?? all.LastOrDefault();
        }

        /// <summary>Plain text lines for a board texture or UI panel. `alias` resolves a user id to a name (null → anonymous).</summary>
        public List<string> Lines(Func<string, string> alias, long nowMs)
        {
            var lines = new List<string>();
            var m = Featured();
            if (m == null) { lines.Add(CompStrings.T("arena.idle")); return lines; }
            string Name(string id) => string.IsNullOrEmpty(id) ? CompStrings.T("arena.waiting") : (alias?.Invoke(id) ?? CompStrings.T("arena.anon"));
            lines.Add(Name(m.ChallengerUserId) + "  vs  " + Name(m.AccepterUserId));
            var phase = BeefRules.PhaseOf(m.Status);
            lines.Add(CompStrings.T("arena.phase." + (phase == BeefPhase.Unknown ? "unknown" : phase.ToString().ToLowerInvariant())));
            if (!BeefRules.IsFinished(phase) && m.PhaseEndsAtMs.HasValue)
                lines.Add(CompStrings.F("arena.endsIn", CompTime.Countdown(m.PhaseEndsAtMs.Value - nowMs)));
            if (m.HasResult)
            {
                lines.Add(CompStrings.F("arena.votes", m.VotesChallenger, m.VotesAccepter));
                lines.Add(string.IsNullOrEmpty(m.WinnerUserId) ? CompStrings.T("arena.draw") : CompStrings.F("arena.winner", Name(m.WinnerUserId)));
            }
            return lines;
        }
    }
}
