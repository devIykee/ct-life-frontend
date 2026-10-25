using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Career ladders (backend PR #16, `GET /careers?view=ladders`, docs/CAREERS.md): five archetype ladders on top of the
    /// jobs, seven ranks each (Intern to Legend), XP from paid shifts and related actions, a pay bonus, venue-area flags and
    /// cosmetics at ranks 3/5/7. Parsing is tolerant and pure (EditMode tests).
    /// </summary>
    public sealed class LadderRank
    {
        public int Rank;
        public string Id, Title, CosmeticId, Status;
        public int XpRequired, PayBonusBps;
        public List<string> VenueAccess = new List<string>();

        public bool Achieved => Status == "achieved";
        public bool Current => Status == "current";
        public bool Next => Status == "next";

        public static LadderRank Parse(JToken t) => new LadderRank
        {
            Rank = CareersWire.Int(t, "rank") ?? 0,
            Id = CareersWire.Str(t, "id") ?? "",
            Title = CareersWire.Str(t, "title") ?? "",
            XpRequired = CareersWire.Int(t, "xpRequired") ?? 0,
            PayBonusBps = CareersWire.Int(t, "payBonusBps") ?? 0,
            VenueAccess = CareersWire.Strings(t?["venueAccess"]),
            CosmeticId = CareersWire.Str(t, "cosmeticId"),
            Status = CareersWire.Str(t, "status") ?? "locked",
        };
    }

    public sealed class Ladder
    {
        public string Id, Name, Title;
        public List<string> TribeIds = new List<string>(), JobIds = new List<string>();
        public bool Active, Suggested;
        public int Xp, Rank, PayBonusBps;
        public int? XpToNext;
        public LadderRank NextRank;
        public List<LadderRank> Ranks = new List<LadderRank>();

        /// <summary>Progress from this rank's threshold to the next (1 at the top rank).</summary>
        public float Progress
        {
            get
            {
                var cur = Ranks.Find(r => r.Rank == Rank);
                var nxt = Ranks.Find(r => r.Rank == Rank + 1);
                if (cur == null || nxt == null) return 1f;
                float span = Math.Max(1, nxt.XpRequired - cur.XpRequired);
                return Math.Max(0f, Math.Min(1f, (Xp - cur.XpRequired) / span));
            }
        }

        public static Ladder Parse(JToken t)
        {
            var l = new Ladder
            {
                Id = CareersWire.Str(t, "id") ?? throw new FormatException("ladder id"),
                Name = CareersWire.Str(t, "name") ?? "",
                TribeIds = CareersWire.Strings(t?["tribeIds"]),
                JobIds = CareersWire.Strings(t?["jobIds"]),
                Active = CareersWire.Bool(t, "active") ?? false,
                Suggested = CareersWire.Bool(t, "suggested") ?? false,
                Xp = CareersWire.Int(t, "xp") ?? 0,
                Rank = CareersWire.Int(t, "rank") ?? 1,
                Title = CareersWire.Str(t, "title") ?? "",
                PayBonusBps = CareersWire.Int(t, "payBonusBps") ?? 0,
                XpToNext = CareersWire.Int(t, "xpToNext"),
            };
            if (t?["nextRank"] is JObject n) l.NextRank = LadderRank.Parse(n);
            foreach (var r in CareersWire.List(t, "ranks") ?? new JArray()) l.Ranks.Add(LadderRank.Parse(r));
            l.Ranks.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            return l;
        }
    }

    public sealed class LadderBoard
    {
        public string MyLadderId, SuggestedLadderId;
        public int JoinCooldownHours = 72;
        public DateTime? CanSwitchAtUtc;
        public List<string> VenueAccess = new List<string>();
        public List<Ladder> Ladders = new List<Ladder>();

        public Ladder Mine => Ladders.Find(l => l.Id == MyLadderId) ?? Ladders.Find(l => l.Active);
        public bool CanSwitchNow(DateTime nowUtc) => CanSwitchAtUtc == null || CanSwitchAtUtc.Value <= nowUtc;

        public static LadderBoard Parse(JToken t)
        {
            if (t == null || t.Type != JTokenType.Object) throw new FormatException("ladders");
            var b = new LadderBoard
            {
                MyLadderId = CareersWire.Str(t, "myLadderId"),
                SuggestedLadderId = CareersWire.Str(t, "suggestedLadderId"),
                JoinCooldownHours = CareersWire.Int(t, "joinCooldownHours") ?? 72,
                CanSwitchAtUtc = CareersWire.Time(t["canSwitchAt"]),
                VenueAccess = CareersWire.Strings(t["venueAccess"]),
            };
            foreach (var l in CareersWire.List(t, "ladders") ?? new JArray())
            {
                try { b.Ladders.Add(Ladder.Parse(l)); }
                catch (FormatException) { }
            }
            return b;
        }

        /// <summary>"+7% pay" from basis points (whole percent, or one decimal when needed).</summary>
        public static string Bonus(int bps) => bps % 100 == 0 ? "+" + bps / 100 + "%" : "+" + (bps / 100.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";

        /// <summary>"perp_pit:high_roller_floor" -> "Perp Pit high roller floor".</summary>
        public static string Area(string flag)
        {
            if (string.IsNullOrEmpty(flag)) return "";
            var parts = flag.Split(':');
            string Pretty(string s) => s.Replace('_', ' ');
            string venue = Pretty(parts[0]);
            venue = venue.Length > 0 ? char.ToUpperInvariant(venue[0]) + venue.Substring(1) : venue;
            return parts.Length > 1 ? venue + " · " + Pretty(parts[1]) : venue;
        }
    }

    public sealed class LadderJoin
    {
        public string LadderId, Title;
        public bool AlreadyMember;
        public int Rank, Xp;
        public DateTime? NextSwitchAtUtc;

        public static LadderJoin Parse(JToken t) => new LadderJoin
        {
            LadderId = CareersWire.Str(t, "ladderId") ?? "",
            AlreadyMember = CareersWire.Bool(t, "alreadyMember") ?? false,
            Rank = CareersWire.Int(t, "rank") ?? 1,
            Title = CareersWire.Str(t, "title") ?? "",
            Xp = CareersWire.Int(t, "xp") ?? 0,
            NextSwitchAtUtc = CareersWire.Time(t?["nextSwitchAt"]),
        };
    }
}
