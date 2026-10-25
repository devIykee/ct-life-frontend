using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife.Season
{
    /// <summary>
    /// Season Pass tracks and the community nods book (backend #13, merged and live; docs/SEASON_AND_NODS.md). Every reward is
    /// off-chain and never tradable; the client never sends XP or a reward. Pure parsing; tested in EditMode.
    /// </summary>
    public sealed class SeasonReward
    {
        public long CashCents;
        public string CosmeticId, FurnitureItemId;
        public string Kind => CosmeticId != null ? "cosmetic" : FurnitureItemId != null ? "furniture" : "cash";

        public static SeasonReward Parse(JToken t)
        {
            var o = t as JObject;
            return new SeasonReward
            {
                CashCents = CityWire.LongOr(o?["cashCents"], 0),
                CosmeticId = CityWire.Str(o?["cosmeticId"]),
                FurnitureItemId = CityWire.Str(o?["furnitureItemId"]),
            };
        }
    }

    public sealed class SeasonTier
    {
        public int Tier, XpRequired;
        public SeasonReward Reward;
        public string Status;   // locked | claimable | claimed | pass_required
        public bool Claimable => Status == "claimable";
        public bool Claimed => Status == "claimed";
        public bool PassRequired => Status == "pass_required";

        public static SeasonTier Parse(JToken t) => new SeasonTier
        {
            Tier = CityWire.IntOr(t?["tier"], 0),
            XpRequired = CityWire.IntOr(t?["xpRequired"], 0),
            Reward = SeasonReward.Parse(t?["reward"]),
            Status = CityWire.Str(t?["status"]) ?? "locked",
        };
    }

    public sealed class SeasonView
    {
        public string SeasonId, Name;
        public long StartsAtMs, EndsAtMs;
        public int XpPerTier, Xp, Tier;
        public int? XpToNext;
        public bool HasPass;
        public List<SeasonTier> Free = new List<SeasonTier>(), Paid = new List<SeasonTier>();

        public int ClaimableCount => Free.FindAll(t => t.Claimable).Count + Paid.FindAll(t => t.Claimable).Count;
        public float TierProgress => XpPerTier <= 0 ? 0f : Math.Max(0f, Math.Min(1f, (Xp - Tier * XpPerTier) / (float)XpPerTier));

        public static SeasonView Parse(JToken t)
        {
            if (t == null || t.Type != JTokenType.Object) throw new FormatException("season");
            var s = t["season"];
            var v = new SeasonView
            {
                SeasonId = CityWire.ReqStr(s?["seasonId"], "seasonId"),
                Name = CityWire.Plain(s?["name"], 60),
                StartsAtMs = CityWire.TimeMsOrNull(s?["startsAt"]) ?? 0,
                EndsAtMs = CityWire.TimeMsOrNull(s?["endsAt"]) ?? 0,
                XpPerTier = CityWire.IntOr(s?["xpPerTier"], 0),
                Xp = CityWire.IntOr(t["xp"], 0),
                Tier = CityWire.IntOr(t["tier"], 0),
                XpToNext = CityWire.IsNull(t["xpToNext"]) ? (int?)null : CityWire.IntOr(t["xpToNext"], 0),
                HasPass = CityWire.BoolOr(t["hasPass"], false),
            };
            foreach (var r in (t["tracks"]?["free"] as JArray) ?? new JArray()) v.Free.Add(SeasonTier.Parse(r));
            foreach (var r in (t["tracks"]?["paid"] as JArray) ?? new JArray()) v.Paid.Add(SeasonTier.Parse(r));
            v.Free.Sort((a, b) => a.Tier.CompareTo(b.Tier));
            v.Paid.Sort((a, b) => a.Tier.CompareTo(b.Tier));
            return v;
        }
    }

    public sealed class SeasonClaim
    {
        public string SeasonId, Track;
        public int Tier, Xp;
        public bool AlreadyClaimed;
        public SeasonReward Reward;

        public static SeasonClaim Parse(JToken t) => new SeasonClaim
        {
            SeasonId = CityWire.Str(t?["seasonId"]),
            Track = CityWire.Str(t?["track"]),
            Tier = CityWire.IntOr(t?["tier"], 0),
            Xp = CityWire.IntOr(t?["xp"], 0),
            AlreadyClaimed = CityWire.BoolOr(t?["alreadyClaimed"], false),
            Reward = SeasonReward.Parse(t?["reward"]),
        };
    }

    public sealed class BookNod
    {
        public string NodId, Name, Tier;
        public bool Found;
        public long? CollectedAtMs;
    }

    public sealed class BookSet
    {
        public string SetId, Name, RewardCosmeticId;
        public int Total, Collected;
        public bool Completed, RewardGranted;
        public List<BookNod> Nods = new List<BookNod>();
    }

    public sealed class NodsBook
    {
        public int Total, Collected;
        public List<BookSet> Sets = new List<BookSet>();

        public bool IsFound(string nodId)
        {
            foreach (var s in Sets) foreach (var n in s.Nods) if (n.NodId == nodId) return n.Found;
            return false;
        }

        /// <summary>A collect landed: mark it found locally until the next GET.</summary>
        public void MarkFound(string nodId, long atMs)
        {
            foreach (var s in Sets)
                foreach (var n in s.Nods)
                    if (n.NodId == nodId && !n.Found) { n.Found = true; n.CollectedAtMs = atMs; s.Collected++; Collected++; s.Completed = s.Collected >= s.Total; }
        }

        public static NodsBook Parse(JToken t)
        {
            if (t == null || t.Type != JTokenType.Object) throw new FormatException("nods book");
            var b = new NodsBook { Total = CityWire.IntOr(t["total"], 0), Collected = CityWire.IntOr(t["collected"], 0) };
            foreach (var s in (t["sets"] as JArray) ?? new JArray())
            {
                var set = new BookSet
                {
                    SetId = CityWire.Str(s["setId"]) ?? "",
                    Name = CityWire.Plain(s["name"], 40),
                    RewardCosmeticId = CityWire.Str((s["reward"] as JObject)?["cosmeticId"]),
                    Total = CityWire.IntOr(s["total"], 0),
                    Collected = CityWire.IntOr(s["collected"], 0),
                    Completed = CityWire.BoolOr(s["completed"], false),
                    RewardGranted = CityWire.BoolOr(s["rewardGranted"], false),
                };
                foreach (var n in (s["nods"] as JArray) ?? new JArray())
                    set.Nods.Add(new BookNod
                    {
                        NodId = CityWire.Str(n["nodId"]) ?? "",
                        Name = CityWire.Plain(n["name"], 40),
                        Tier = CityWire.Str(n["tier"]) ?? "C",
                        Found = CityWire.BoolOr(n["found"], false),
                        CollectedAtMs = CityWire.TimeMsOrNull(n["collectedAt"]),
                    });
                b.Sets.Add(set);
            }
            return b;
        }
    }

    public sealed class NodCollect
    {
        public string NodId, SetId, SetRewardCosmeticId;
        public bool AlreadyCollected, SetCompleted;
        public int Xp, SetCollected, SetTotal;

        public static NodCollect Parse(JToken t)
        {
            // `setReward` is null on most finds: index only real objects (JValue null throws on a child access)
            var set = t?["set"] as JObject;
            var reward = t?["setReward"] as JObject;
            return new NodCollect
            {
                NodId = CityWire.Str(t?["nodId"]),
                AlreadyCollected = CityWire.BoolOr(t?["alreadyCollected"], false),
                Xp = CityWire.IntOr(t?["xp"], 0),
                SetId = CityWire.Str(set?["setId"]),
                SetCollected = CityWire.IntOr(set?["collected"], 0),
                SetTotal = CityWire.IntOr(set?["total"], 0),
                SetCompleted = CityWire.BoolOr(set?["completed"], false),
                SetRewardCosmeticId = CityWire.Str(reward?["cosmeticId"]),
            };
        }
    }

    public sealed class HunterRow { public int Rank, Count; public string UserId, Alias; }

    public sealed class HunterBoard
    {
        public int Total;
        public List<HunterRow> Top = new List<HunterRow>();
        public HunterRow Me;

        private static HunterRow Row(JToken r) => r == null || r.Type != JTokenType.Object ? null : new HunterRow
        {
            Rank = CityWire.IntOr(r["rank"], 0), Count = CityWire.IntOr(r["count"], 0),
            UserId = CityWire.Str(r["userId"]), Alias = CityWire.Plain(r["alias"], 24),
        };

        public static HunterBoard Parse(JToken t)
        {
            var b = new HunterBoard { Total = CityWire.IntOr(t?["total"], 0), Me = Row(t?["me"]) };
            foreach (var r in (t?["top"] as JArray) ?? new JArray()) { var row = Row(r); if (row != null) b.Top.Add(row); }
            return b;
        }
    }

    /// <summary>Backend #13 routes. Writes carry the caller's retained Idempotency-Key.</summary>
    public sealed class SeasonApi
    {
        private readonly IRestClient _rest;
        public SeasonApi(IRestClient rest) { _rest = rest ?? throw new ArgumentNullException(nameof(rest)); }

        public static readonly string[] Routes =
        {
            "GET /season", "POST /season/tiers/{n}/claim?track=", "GET /nods", "POST /nods/{nodId}/collect", "GET /nods/leaderboard",
        };

        private Task<JToken> Post(string path, JObject body, string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(key));
            return _rest.Send<JToken>("POST", path, (body ?? new JObject()).ToString(Formatting.None), true, key, ct);
        }

        public async Task<SeasonView> Season(CancellationToken ct) => SeasonView.Parse(await _rest.Get<JToken>("/season", ct));

        public async Task<SeasonClaim> Claim(int tier, string track, string key, CancellationToken ct) =>
            SeasonClaim.Parse(await Post("/season/tiers/" + tier + "/claim?track=" + (track == "paid" ? "paid" : "free"), null, key, ct));

        public async Task<NodsBook> Book(CancellationToken ct) => NodsBook.Parse(await _rest.Get<JToken>("/nods", ct));

        /// <summary>The server decides the find from its own street position (fresh, within 15 m); x/z is only a sanity check.</summary>
        public async Task<NodCollect> Collect(string nodId, double x, double z, string key, CancellationToken ct) =>
            NodCollect.Parse(await Post("/nods/" + CityWire.Seg(nodId) + "/collect",
                new JObject { ["proof"] = new JObject { ["nodId"] = nodId, ["x"] = Math.Round(x, 2), ["z"] = Math.Round(z, 2) } }, key, ct));

        public async Task<HunterBoard> Leaderboard(CancellationToken ct) => HunterBoard.Parse(await _rest.Get<JToken>("/nods/leaderboard", ct));
    }
}
