using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife.Casino
{
    /// <summary>
    /// The Rug Room wire models (backend PR #12, contract tag `casino`). Cash only, play money, 18+. Every success reply carries
    /// the meta block (<see cref="CasinoMeta"/>): the disclaimer for every screen and the session reminder flag. Parsing is pure and
    /// tolerant (missing optional fields fall back); tested in EditMode.
    /// </summary>
    public static class CasinoGames
    {
        public const string Slots = "slots", Crash = "crash", Coinflip = "coinflip", CoinflipH2H = "coinflip_h2h";
        public const string Heads = "heads", Tails = "tails";
        /// <summary>The reel symbols in the server's order (casino-math.ts SLOT_SYMBOLS): the fairness check relies on it.</summary>
        public static readonly string[] Symbols = { "rocket", "diamond_hands", "ape", "laser_eyes", "rug", "wild" };
        public const string FallbackDisclaimer = "Play money. No cash value. 18+";
    }

    public class CasinoMeta
    {
        public string Disclaimer = CasinoGames.FallbackDisclaimer;
        public int SessionMinutes;
        public bool SessionReminder;

        protected void ReadMeta(JToken t)
        {
            var d = CityWire.Str(t?["disclaimer"]);
            if (!string.IsNullOrEmpty(d)) Disclaimer = d;
            SessionMinutes = CityWire.IntOr(t?["sessionMinutes"], 0);
            SessionReminder = CityWire.BoolOr(t?["sessionReminder"], false);
        }

        public static CasinoMeta ParseMeta(JToken t) { var m = new CasinoMeta(); m.ReadMeta(t); return m; }
    }

    public sealed class CasinoState : CasinoMeta
    {
        public bool AgeConfirmed;
        public long? SelfExcludedUntilMs;
        public long MinStakeCents = 100, MaxStakeCents = 10000, DailyStakeCapCents, DailyNetLossCapCents;
        public long StakedCents, NetLossCents, StakeRemainingCents, LossRemainingCents, ResetsAtMs;
        public Dictionary<string, int> ReelWeights = new Dictionary<string, int>();
        public Dictionary<string, double> PayThree = new Dictionary<string, double>(), PayTwo = new Dictionary<string, double>();
        public double SlotsRtp = 0.95, SlotsHitRate, CrashRtp = 0.95, CoinflipRtp = 0.95;
        public double GrowthPerSecond = 0.07, MaxMultiplier = 100, MinAutoCashout = 1.01, CoinflipPayout = 1.9;
        public int H2HHouseCutBps = 500, ChallengeTtlSeconds = 120;

        public bool Excluded(double nowMs) => SelfExcludedUntilMs.HasValue && SelfExcludedUntilMs.Value > nowMs;
        /// <summary>The most a single stake can be today (both caps and the table max).</summary>
        public long StakeRoomCents => Math.Min(MaxStakeCents, Math.Min(StakeRemainingCents, LossRemainingCents));
        public bool CapReached => StakeRoomCents < MinStakeCents;
        public bool StakeCapHit => StakeRemainingCents < MinStakeCents;

        public static CasinoState Parse(JToken t)
        {
            if (t == null || t.Type != JTokenType.Object) throw new FormatException("casino state");
            var s = new CasinoState();
            s.ReadMeta(t);
            s.AgeConfirmed = CityWire.BoolOr(t["ageConfirmed"], false);
            s.SelfExcludedUntilMs = CityWire.TimeMsOrNull(t["selfExcludedUntil"]);
            var lim = t["limits"];
            s.MinStakeCents = CityWire.LongOr(lim?["minStakeCents"], 100);
            s.MaxStakeCents = CityWire.LongOr(lim?["maxStakeCents"], 10000);
            s.DailyStakeCapCents = CityWire.LongOr(lim?["dailyStakeCapCents"], 0);
            s.DailyNetLossCapCents = CityWire.LongOr(lim?["dailyNetLossCapCents"], 0);
            var today = t["today"];
            s.StakedCents = CityWire.LongOr(today?["stakedCents"], 0);
            s.NetLossCents = CityWire.LongOr(today?["netLossCents"], 0);
            s.StakeRemainingCents = CityWire.LongOr(today?["stakeRemainingCents"], 0);
            s.LossRemainingCents = CityWire.LongOr(today?["lossRemainingCents"], 0);
            s.ResetsAtMs = CityWire.TimeMsOrNull(today?["resetsAt"]) ?? 0;
            var games = t["games"];
            var slots = games?["slots"];
            if (slots?["reelWeights"] is JObject rw) foreach (var p in rw.Properties()) s.ReelWeights[p.Name] = CityWire.IntOr(p.Value, 0);
            if (slots?["paytable"]?["three"] is JObject p3) foreach (var p in p3.Properties()) s.PayThree[p.Name] = CityWire.DoubleOr(p.Value, 0);
            if (slots?["paytable"]?["two"] is JObject p2) foreach (var p in p2.Properties()) s.PayTwo[p.Name] = CityWire.DoubleOr(p.Value, 0);
            s.SlotsRtp = CityWire.DoubleOr(slots?["rtp"], 0.95);
            s.SlotsHitRate = CityWire.DoubleOr(slots?["hitRate"], 0);
            var crash = games?["crash"];
            s.GrowthPerSecond = CityWire.DoubleOr(crash?["growthPerSecond"], 0.07);
            s.MaxMultiplier = CityWire.DoubleOr(crash?["maxMultiplier"], 100);
            s.MinAutoCashout = CityWire.DoubleOr(crash?["minAutoCashout"], 1.01);
            s.CrashRtp = CityWire.DoubleOr(crash?["rtp"], 0.95);
            var cf = games?["coinflip"];
            s.CoinflipPayout = CityWire.DoubleOr(cf?["payoutMultiplier"], 1.9);
            s.CoinflipRtp = CityWire.DoubleOr(cf?["rtp"], 0.95);
            s.H2HHouseCutBps = CityWire.IntOr(cf?["h2hHouseCutBps"], 500);
            s.ChallengeTtlSeconds = CityWire.IntOr(cf?["challengeTtlSeconds"], 120);
            if (s.ReelWeights.Count == 0)
                foreach (var (k, w) in new[] { ("rocket", 5), ("diamond_hands", 9), ("ape", 13), ("laser_eyes", 8), ("rug", 10), ("wild", 2) }) s.ReelWeights[k] = w;
            return s;
        }
    }

    public sealed class CasinoRound
    {
        public string RoundId, Game, Status, SeedHash, Seed, ChallengeId;
        public long StakeCents;
        public long? PayoutCents;
        public long CreatedAtMs;
        public long? SettledAtMs;
        public JObject Params = new JObject();
        public JObject Outcome;

        public bool Open => Status == "open";
        public bool Settled => Status == "settled";
        public long NetCents => (PayoutCents ?? 0) - StakeCents;

        // crash
        public long StartedAtMs => CityWire.LongOr(Params["startedAtMs"], CreatedAtMs);
        public double GrowthPerSecond => CityWire.DoubleOr(Params["growthPerSecond"], 0.07);
        public double? AutoCashout => CityWire.IsNull(Params["autoCashout"]) ? (double?)null : CityWire.DoubleOr(Params["autoCashout"], 0);
        public double CrashPoint => CityWire.DoubleOr(Outcome?["crashPoint"], 1);
        public double? CashedOutAt => CityWire.IsNull(Outcome?["cashedOutAt"]) ? (double?)null : CityWire.DoubleOr(Outcome["cashedOutAt"], 0);
        public bool Crashed => CityWire.BoolOr(Outcome?["crashed"], false);

        // slots
        public string[] Reels
        {
            get
            {
                var a = Outcome?["reels"] as JArray;
                if (a == null || a.Count != 3) return null;
                return new[] { CityWire.Str(a[0]), CityWire.Str(a[1]), CityWire.Str(a[2]) };
            }
        }
        public double Multiplier => CityWire.DoubleOr(Outcome?["multiplier"], 0);
        public string WinSymbol => CityWire.Str(Outcome?["winSymbol"]);
        public bool Jackpot => CityWire.BoolOr(Outcome?["jackpot"], false);

        // coinflip (house and head-to-head)
        public string Side => CityWire.Str(Outcome?["side"]) ?? CityWire.Str(Params["side"]);
        public string Result => CityWire.Str(Outcome?["result"]);
        public bool Won => CityWire.BoolOr(Outcome?["won"], (PayoutCents ?? 0) > 0);

        public static CasinoRound Parse(JToken t)
        {
            if (t == null || t.Type != JTokenType.Object) throw new FormatException("casino round");
            return new CasinoRound
            {
                RoundId = CityWire.ReqStr(t["roundId"], "roundId"),
                Game = CityWire.Str(t["game"]) ?? "",
                Status = CityWire.Str(t["status"]) ?? "settled",
                StakeCents = CityWire.LongOr(t["stakeCents"], 0),
                PayoutCents = CityWire.OptLong(t["payoutCents"]),
                Params = t["params"] as JObject ?? new JObject(),
                Outcome = t["outcome"] as JObject,
                SeedHash = CityWire.Str(t["seedHash"]) ?? "",
                Seed = CityWire.Str(t["seed"]),
                ChallengeId = CityWire.Str(t["challengeId"]),
                CreatedAtMs = CityWire.TimeMsOrNull(t["createdAt"]) ?? 0,
                SettledAtMs = CityWire.TimeMsOrNull(t["settledAt"]),
            };
        }

        public static List<CasinoRound> ParseList(JToken t)
        {
            var list = new List<CasinoRound>();
            foreach (var row in CityWire.List(t, "rounds"))
            {
                try { list.Add(Parse(row)); }
                catch (FormatException) { }
            }
            return list;
        }
    }

    public sealed class CasinoBet : CasinoMeta
    {
        public CasinoRound Round;
        public long CashCents;

        public static CasinoBet Parse(JToken t)
        {
            var b = new CasinoBet();
            b.ReadMeta(t);
            b.Round = CasinoRound.Parse(t?["round"]);
            b.CashCents = CityWire.LongOr(t?["cashCents"], -1);
            return b;
        }
    }

    /// <summary>A head-to-head coinflip (backend #25): bragging rights, no Cash. The challenge has no stake, pot, cut or payout.</summary>
    public sealed class CasinoChallenge
    {
        public string ChallengeId, Role, ChallengerId, ChallengerAlias, OpponentId, OpponentAlias, ChallengerSide, Status, SeedHash, Seed, Result, WinnerId;
        public long CreatedAtMs, ExpiresAtMs;
        public long? SettledAtMs;

        public bool Mine => Role == "challenger";
        public bool Incoming => Role == "opponent";
        public bool Open => Status == "open";
        public string PeerId => Mine ? OpponentId : ChallengerId;
        public string PeerAlias => Mine ? OpponentAlias : ChallengerAlias;
        /// <summary>The side the caller is on (the opponent takes the other side).</summary>
        public string MySide => Mine ? ChallengerSide : (ChallengerSide == CasinoGames.Heads ? CasinoGames.Tails : CasinoGames.Heads);
        public bool IWon(string myUserId) => !string.IsNullOrEmpty(WinnerId) && WinnerId == myUserId;
        public double SecondsLeft(double nowMs) => Math.Max(0, (ExpiresAtMs - nowMs) / 1000.0);

        public static CasinoChallenge Parse(JToken t)
        {
            if (t == null || t.Type != JTokenType.Object) throw new FormatException("casino challenge");
            return new CasinoChallenge
            {
                ChallengeId = CityWire.ReqStr(t["challengeId"], "challengeId"),
                Role = CityWire.Str(t["role"]) ?? "",
                ChallengerId = CityWire.Str(t["challengerId"]) ?? "",
                ChallengerAlias = CityWire.Plain(t["challengerAlias"], 24),
                OpponentId = CityWire.Str(t["opponentId"]) ?? "",
                OpponentAlias = CityWire.Plain(t["opponentAlias"], 24),
                ChallengerSide = CityWire.Str(t["challengerSide"]) ?? CasinoGames.Heads,
                Status = CityWire.Str(t["status"]) ?? "open",
                SeedHash = CityWire.Str(t["seedHash"]) ?? "",
                Seed = CityWire.Str(t["seed"]),
                Result = CityWire.Str(t["result"]),
                WinnerId = CityWire.Str(t["winnerId"]),
                CreatedAtMs = CityWire.TimeMsOrNull(t["createdAt"]) ?? 0,
                ExpiresAtMs = CityWire.TimeMsOrNull(t["expiresAt"]) ?? 0,
                SettledAtMs = CityWire.TimeMsOrNull(t["settledAt"]),
            };
        }

        public static List<CasinoChallenge> ParseList(JToken t)
        {
            var list = new List<CasinoChallenge>();
            foreach (var row in CityWire.List(t, "challenges"))
            {
                try { list.Add(Parse(row)); }
                catch (FormatException) { }
            }
            return list;
        }
    }

    public sealed class CasinoChallengeReply : CasinoMeta
    {
        public CasinoChallenge Challenge;
        /// <summary>The caller's Cash, unchanged by a head-to-head (no round, no ledger row).</summary>
        public long CashCents;

        public static CasinoChallengeReply Parse(JToken t)
        {
            var r = new CasinoChallengeReply();
            r.ReadMeta(t);
            r.Challenge = CasinoChallenge.Parse(t?["challenge"]);
            r.CashCents = CityWire.LongOr(t?["cashCents"], -1);
            return r;
        }
    }

    /// <summary>A player the caller can challenge: DM peers and recent opponents.</summary>
    public sealed class CasinoPeer
    {
        public string UserId, Alias;
    }

    /// <summary>
    /// The crash curve and the fairness check, pure (EditMode tests). The curve is the server's: m(t) = e^(g t), shown floored
    /// to hundredths, so the number on screen never runs ahead of what a cash-out at that instant pays.
    /// </summary>
    public static class CasinoMath
    {
        public static double CrashMultiplier(double seconds, double growth) => Math.Exp(growth * Math.Max(0, seconds));
        public static double Floor2(double m) => Math.Floor(m * 100.0 + 1e-9) / 100.0;
        public static double SecondsTo(double multiplier, double growth) => multiplier <= 1 ? 0 : Math.Log(multiplier) / growth;
        public static long Payout(long stakeCents, double multiplier) => (long)Math.Floor(stakeCents * Math.Round(multiplier * 100) / 100.0);

        public static string Sha256Hex(string s)
        {
            using var sha = SHA256.Create();
            return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? "")));
        }

        /// <summary>The n-th draw of a round: HMAC-SHA256(key = seed string, "label:n"), the first 53 bits as a float in [0, 1).</summary>
        public static double Draw(string seed, string label, int counter)
        {
            using var h = new HMACSHA256(Encoding.UTF8.GetBytes(seed ?? ""));
            var d = h.ComputeHash(Encoding.UTF8.GetBytes(label + ":" + counter.ToString(CultureInfo.InvariantCulture)));
            ulong hi = (((ulong)d[0] << 24) | ((ulong)d[1] << 16) | ((ulong)d[2] << 8) | d[3]) & 0x1fffffUL;
            ulong lo = ((ulong)d[4] << 24) | ((ulong)d[5] << 16) | ((ulong)d[6] << 8) | d[7];
            return (hi * 4294967296.0 + lo) / 9007199254740992.0;
        }

        public static string PickSymbol(double u, IReadOnlyDictionary<string, int> weights)
        {
            int total = 0;
            foreach (var s in CasinoGames.Symbols) total += weights.TryGetValue(s, out var w) ? w : 0;
            double r = u * total;
            foreach (var s in CasinoGames.Symbols)
            {
                r -= weights.TryGetValue(s, out var w) ? w : 0;
                if (r < 0) return s;
            }
            return CasinoGames.Symbols[CasinoGames.Symbols.Length - 1];
        }

        public static double CrashPoint(double u, double targetRtp = 0.95, double max = 100)
        {
            double raw = Math.Floor(targetRtp * 100 / (1 - u));
            return Math.Min(Math.Max(100, raw), max * 100) / 100.0;
        }

        public sealed class Verdict
        {
            public bool HashOk, OutcomeOk;
            public string Recomputed;   // what the seed gives, in words
            public bool Ok => HashOk && OutcomeOk;
        }

        /// <summary>Checks a settled round on this phone: SHA-256(seed) == seedHash, then recomputes the outcome from the seed.</summary>
        public static Verdict Verify(CasinoRound r, IReadOnlyDictionary<string, int> weights, double crashRtp = 0.95, double crashMax = 100)
        {
            var v = new Verdict();
            if (r == null || string.IsNullOrEmpty(r.Seed)) return v;
            v.HashOk = string.Equals(Sha256Hex(r.Seed), r.SeedHash, StringComparison.OrdinalIgnoreCase);
            switch (r.Game)
            {
                case CasinoGames.Slots:
                {
                    var label = "slots:" + r.RoundId;
                    var got = new string[3];
                    for (int i = 0; i < 3; i++) got[i] = PickSymbol(Draw(r.Seed, label, i), weights);
                    var shown = r.Reels;
                    v.OutcomeOk = shown != null && shown[0] == got[0] && shown[1] == got[1] && shown[2] == got[2];
                    v.Recomputed = string.Join(" · ", got);
                    break;
                }
                case CasinoGames.Crash:
                {
                    var cp = CrashPoint(Draw(r.Seed, "crash:" + r.RoundId, 0), crashRtp, crashMax);
                    v.OutcomeOk = Math.Abs(cp - r.CrashPoint) < 0.005;
                    v.Recomputed = cp.ToString("0.00", CultureInfo.InvariantCulture) + "x";
                    break;
                }
                case CasinoGames.Coinflip:
                {
                    var side = Draw(r.Seed, "coinflip:" + r.RoundId, 0) < 0.5 ? CasinoGames.Heads : CasinoGames.Tails;
                    v.OutcomeOk = side == r.Result;
                    v.Recomputed = side;
                    break;
                }
                case CasinoGames.CoinflipH2H:
                {
                    var side = Draw(r.Seed, "coinflip_h2h:" + r.ChallengeId, 0) < 0.5 ? CasinoGames.Heads : CasinoGames.Tails;
                    v.OutcomeOk = side == r.Result;
                    v.Recomputed = side;
                    break;
                }
            }
            return v;
        }

        private static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
