using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.Careers
{
    public enum MiniGameEngine { Sort, Flag, Sequence, Match, Pick, Combo, Timing, Balance, Legacy }

    /// <summary>Inputs to build one round: the seed (from the server shift, or a practice nonce) and the registry caps.</summary>
    public readonly struct RoundInit
    {
        public readonly int Seed, MaxScore;
        public readonly double Duration;
        public RoundInit(int seed, int maxScore, double duration) { Seed = seed; MaxScore = maxScore; Duration = duration; }
    }

    /// <summary>
    /// A mini-game: identity, tutorial card and a round factory. `DefaultMaxScore`/`DefaultDuration` are the FE
    /// proposal values; a registry entry from GET /config (contract ids/mini_games.json) overrides both.
    /// </summary>
    public sealed class MiniGameSpec
    {
        public string Id, CareerId, Title, Tagline;
        public MiniGameEngine Engine;
        /// <summary>Tutorial card: 2-4 short rules.</summary>
        public string[] Rules = Array.Empty<string>();
        /// <summary>Tutorial card: how the score is computed, in one sentence.</summary>
        public string ScoringRule;
        /// <summary>Where the real-world version of this task comes from (shown as "Based on"; parody, no brands).</summary>
        public string RealTask;
        public int DefaultMaxScore = 100;
        public double DefaultDuration = 45;
        /// <summary>True when the id is already in the imported contract registry or careers.json; false = FE proposal.</summary>
        public bool InContract;
        /// <summary>Engine labels: Sort bins, Timing act button, Balance meter, etc. (engine specific).</summary>
        public string ActionLabel;
        public Func<RoundInit, IMiniGameRound> Create;

        public IMiniGameRound Build(RoundInit init)
        {
            if (Create == null) throw new InvalidOperationException("Mini-game " + Id + " has no round factory (legacy game).");
            var round = Create(init);
            if (round == null || round.MaxScore != init.MaxScore) throw new InvalidOperationException("Round factory ignored the score cap for " + Id);
            return round;
        }
    }

    /// <summary>A resolved definition used to play: spec (may be null for unknown-but-registered ids) and the caps.</summary>
    public sealed class ResolvedGame
    {
        public string Id, CareerId;
        public MiniGameSpec Spec;
        public int MaxScore;
        public double Duration;
        /// <summary>The cap/duration came from the server registry rather than the FE proposal defaults.</summary>
        public bool FromRegistry;
    }

    /// <summary>Lookup over every built mini-game.</summary>
    public sealed class MiniGameCatalog
    {
        private readonly Dictionary<string, MiniGameSpec> _byId = new Dictionary<string, MiniGameSpec>(StringComparer.Ordinal);
        private readonly List<MiniGameSpec> _all = new List<MiniGameSpec>();

        public MiniGameCatalog(IEnumerable<MiniGameSpec> specs)
        {
            foreach (var s in specs)
            {
                if (s == null || string.IsNullOrEmpty(s.Id) || string.IsNullOrEmpty(s.CareerId)) throw new ArgumentException("Spec without id/career");
                if (_byId.ContainsKey(s.Id)) throw new ArgumentException("Duplicate mini-game id " + s.Id);
                _byId[s.Id] = s;
                _all.Add(s);
            }
        }

        public IReadOnlyList<MiniGameSpec> All => _all;
        public MiniGameSpec Get(string id) => id != null && _byId.TryGetValue(id, out var s) ? s : null;
        public IReadOnlyList<MiniGameSpec> ForCareer(string careerId) => _all.Where(s => s.CareerId == careerId).ToList();

        /// <summary>
        /// Registry values win (the server validates against them); FE defaults otherwise. Registry durations outside
        /// 10..600 s and caps ≤ 0 are rejected as malformed (null), never silently "fixed".
        /// </summary>
        public ResolvedGame Resolve(string id, int? registryMaxScore, int? registryDuration, string registryCareer)
        {
            var spec = Get(id);
            if (registryMaxScore.HasValue || registryDuration.HasValue)
            {
                if (!registryMaxScore.HasValue || registryMaxScore.Value <= 0 || !registryDuration.HasValue ||
                    registryDuration.Value < 10 || registryDuration.Value > 600) return null;
                return new ResolvedGame
                {
                    Id = id, CareerId = registryCareer ?? spec?.CareerId, Spec = spec, MaxScore = registryMaxScore.Value,
                    Duration = registryDuration.Value, FromRegistry = true,
                };
            }
            if (spec == null) return null;
            return new ResolvedGame { Id = id, CareerId = spec.CareerId, Spec = spec, MaxScore = spec.DefaultMaxScore, Duration = spec.DefaultDuration };
        }
    }
}
