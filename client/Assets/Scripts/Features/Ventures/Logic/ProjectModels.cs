using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.Ventures
{
    public sealed class ProjectSummary
    {
        public string ProjectId, Name, Kind, Status, OwnerUserId, OwnerAlias, CoinId;
        public int MemberCount;
        public long TreasuryCents;
        public List<string> OpenRoles = new List<string>();
        public long CreatedAtMs;
    }

    public sealed class ProjectMember
    {
        public string UserId, Alias, Role;
        public long SalaryCentsPerWeek, JoinedAtMs;
    }

    public sealed class VcStake
    {
        public string VcArchetypeId;
        public int EquityBps;
        public long InvestedCents, UnlockAtMs;
    }

    public sealed class ProjectEvent
    {
        public string EventId, ProjectId;
        public long CashDeltaCents, AtMs;
        public Dictionary<string, string> Params = new Dictionary<string, string>();
    }

    public sealed class ProjectDetail
    {
        public ProjectSummary Project;
        public List<ProjectMember> Members = new List<ProjectMember>();
        public long PayrollCentsPerWeek;
        public long? NextPayrollAtMs;
        public List<VcStake> VcStakes = new List<VcStake>();
        public List<ProjectEvent> Events = new List<ProjectEvent>();
    }

    public sealed class HireOffer
    {
        public string OfferId, ProjectId, ProjectName, FromUserId, ToUserId, Role, Status;
        public long SalaryCentsPerWeek, ExpiresAtMs, CreatedAtMs;
    }

    public sealed class LaunchQuote
    {
        public string ProjectId;
        public long LaunchCostCents, TreasuryCents, QuotedAtMs;
        public int BuilderDiscountBps;
        public bool CanLaunch;
        public List<string> Blockers = new List<string>();
    }

    public sealed class RoomTicket
    {
        public string RoomName, RoomId;
        public long ExpiresAtMs;
    }

    public sealed class VcOffer
    {
        public string OfferId, ProjectId, VcArchetypeId, Status;
        public long InvestCents, UnlockAtMs, ExpiresAtMs;
        public int EquityBps;

        /// <summary>Open for a decision: pending and not past its expiry.</summary>
        public bool IsActionable(double nowMs) => Status == "pending" && ExpiresAtMs > nowMs;
    }

    public sealed class VcDeal
    {
        public string ProjectId, ProjectName, VcArchetypeId, Status;
        public long InvestCents, AtMs;
        public int EquityBps;
    }

    public sealed class Page<T>
    {
        public List<T> Items = new List<T>();
        public string NextCursor;
        public int Skipped;
    }

    public static class ProjectKinds
    {
        /// <summary>Proposal `ProjectSummary.kind` enum (spec: memecoin, DEX, NFT studio, perps venue, L2).</summary>
        public static readonly string[] All = { "memecoin", "dex", "nft_studio", "perps_venue", "l2" };
        /// <summary>Proposal roles that can be offered (the founder role is implicit).</summary>
        public static readonly string[] HireRoles = { "dev", "mod", "kol", "auditor" };
        public static readonly string[] Scopes = { "mine", "hiring", "all" };
    }

    public static class ProjectParse
    {
        private static string Id(JToken t, params string[] keys)
        {
            var id = VWire.FirstStr(t, keys);
            if (!VWire.IsSafeId(id)) throw new FormatException("Bad id");
            return id;
        }

        public static ProjectSummary Summary(JToken t)
        {
            if (!(t is JObject)) throw new FormatException("Expected a project");
            var p = new ProjectSummary
            {
                ProjectId = Id(t, "projectId", "id"), Name = VWire.Plain(t["name"], 40), Kind = VWire.Plain(t["kind"], 24),
                Status = VWire.Plain(t["status"], 16), OwnerUserId = VWire.Str(t["ownerUserId"]) ?? VWire.Str(t["founderId"]),
                OwnerAlias = VWire.Plain(t["ownerAlias"], 32), MemberCount = Math.Max(0, VWire.IntOr(t["memberCount"], 0)),
                TreasuryCents = VWire.LongOr(t["treasuryCents"], 0), CreatedAtMs = VWire.TimeMsOrNull(t["createdAt"]) ?? 0,
            };
            var coin = VWire.Str(t["coinId"]);
            p.CoinId = VWire.IsSafeId(coin) ? coin : null;
            if (t["openRoles"] is JArray roles)
                foreach (var r in roles) { var s = VWire.Str(r); if (VWire.IsSafeId(s) && s.Length <= 24) p.OpenRoles.Add(s); }
            return p;
        }

        public static Page<ProjectSummary> SummaryPage(JToken body)
        {
            var page = new Page<ProjectSummary> { NextCursor = body is JObject o ? VWire.Str(o["nextCursor"]) : null };
            foreach (var row in VWire.List(body, "projects", "items"))
            {
                try { page.Items.Add(Summary(row)); }
                catch (FormatException) { page.Skipped++; }
            }
            return page;
        }

        public static ProjectMember Member(JToken t)
        {
            var uid = VWire.Str(t?["userId"]);
            if (!VWire.IsSafeId(uid)) throw new FormatException("Bad userId");
            return new ProjectMember
            {
                UserId = uid, Alias = VWire.Plain(t["alias"], 32), Role = VWire.Plain(t["role"], 24),
                SalaryCentsPerWeek = VWire.LongOr(t["salaryCentsPerWeek"], 0), JoinedAtMs = VWire.TimeMsOrNull(t["joinedAt"]) ?? 0,
            };
        }

        public static VcStake Stake(JToken t)
        {
            if (!(t is JObject)) throw new FormatException("Expected a stake");
            return new VcStake
            {
                VcArchetypeId = VWire.Plain(t["vcArchetypeId"], 40), EquityBps = VWire.IntOr(t["equityBps"], 0),
                InvestedCents = VWire.LongOr(t["investedCents"], 0), UnlockAtMs = VWire.TimeMsOrNull(t["unlockAt"]) ?? 0,
            };
        }

        public static ProjectEvent Event(JToken t)
        {
            var eid = VWire.Str(t?["eventId"]);
            if (!VWire.IsSafeId(eid)) throw new FormatException("Bad eventId");
            var e = new ProjectEvent
            {
                EventId = eid, ProjectId = VWire.Str(t["projectId"]), CashDeltaCents = VWire.LongOr(t["cashDeltaCents"], 0),
                AtMs = VWire.TimeMsOrNull(t["at"]) ?? 0,
            };
            if (t["params"] is JObject ps)
                foreach (var p in ps.Properties().Take(12))
                {
                    var v = VWire.Plain(p.Value, 60);
                    if (VWire.IsSafeId(p.Name)) e.Params[p.Name] = v;
                }
            return e;
        }

        /// <summary>`GET /projects/{id}`: the proposal's `ProjectDetail`, or (backend main) a bare project row.</summary>
        public static ProjectDetail Detail(JToken body)
        {
            var d = new ProjectDetail();
            if (body?["project"] is JObject) d.Project = Summary(body["project"]);
            else d.Project = Summary(body);
            d.PayrollCentsPerWeek = VWire.LongOr(body["payrollCentsPerWeek"], 0);
            d.NextPayrollAtMs = VWire.TimeMsOrNull(body["nextPayrollAt"]);
            Fill(d.Members, body["members"], Member);
            Fill(d.VcStakes, body["vcStakes"], Stake);
            Fill(d.Events, body["recentEvents"], Event);
            d.Events.Sort((a, b) => b.AtMs.CompareTo(a.AtMs));
            return d;
        }

        private static void Fill<T>(List<T> into, JToken arr, Func<JToken, T> read)
        {
            if (!(arr is JArray a)) return;
            foreach (var row in a)
            {
                try { into.Add(read(row)); }
                catch (FormatException) { /* an unreadable row is not shown */ }
            }
        }

        public static HireOffer Offer(JToken t)
        {
            var o = new HireOffer
            {
                OfferId = Id(t, "offerId", "id"), ProjectId = Id(t, "projectId"), ProjectName = VWire.Plain(t["projectName"], 40),
                FromUserId = VWire.Str(t["fromUserId"]), ToUserId = VWire.Str(t["toUserId"]), Role = VWire.Plain(t["role"], 24),
                Status = VWire.Plain(t["status"], 16), SalaryCentsPerWeek = VWire.LongOr(t["salaryCentsPerWeek"], 0),
                ExpiresAtMs = VWire.TimeMsOrNull(t["expiresAt"]) ?? 0, CreatedAtMs = VWire.TimeMsOrNull(t["createdAt"]) ?? 0,
            };
            return o;
        }

        public static Page<HireOffer> OfferPage(JToken body)
        {
            var page = new Page<HireOffer> { NextCursor = body is JObject o ? VWire.Str(o["nextCursor"]) : null };
            foreach (var row in VWire.List(body, "offers", "items"))
            {
                try { page.Items.Add(Offer(row)); }
                catch (FormatException) { page.Skipped++; }
            }
            return page;
        }

        public static LaunchQuote Quote(JToken t)
        {
            if (!(t is JObject)) throw new FormatException("Expected a quote");
            var q = new LaunchQuote
            {
                ProjectId = VWire.Str(t["projectId"]), LaunchCostCents = VWire.Long(t["launchCostCents"]), TreasuryCents = VWire.LongOr(t["treasuryCents"], 0),
                BuilderDiscountBps = VWire.IntOr(t["builderDiscountBps"], 0), CanLaunch = VWire.BoolOr(t["canLaunch"], false),
                QuotedAtMs = VWire.TimeMsOrNull(t["quotedAt"]) ?? 0,
            };
            if (q.LaunchCostCents < 0) throw new FormatException("Negative cost");
            if (t["blockers"] is JArray bl)
                foreach (var b in bl) { var s = VWire.Str(b); if (VWire.IsSafeId(s) && s.Length <= 40) q.Blockers.Add(s); }
            return q;
        }

        public static RoomTicket Ticket(JToken t)
        {
            var room = VWire.Str(t?["roomName"]);
            var id = VWire.Str(t?["roomId"]);
            if (!VWire.IsSafeId(room) || string.IsNullOrEmpty(id) || id.Length > 120 || id.Any(c => char.IsControl(c) || c == ' '))
                throw new FormatException("Bad room ticket");
            return new RoomTicket { RoomName = room, RoomId = id, ExpiresAtMs = VWire.TimeMsOrNull(t["expiresAt"]) ?? 0 };
        }

        public static VcOffer VcTerm(JToken t)
        {
            return new VcOffer
            {
                OfferId = Id(t, "offerId", "id"), ProjectId = Id(t, "projectId"), VcArchetypeId = VWire.Plain(t["vcArchetypeId"], 40),
                Status = VWire.Plain(t["status"], 16), InvestCents = VWire.Long(t["investCents"]), EquityBps = VWire.IntOr(t["equityBps"], 0),
                UnlockAtMs = VWire.TimeMsOrNull(t["unlockAt"]) ?? 0, ExpiresAtMs = VWire.TimeMsOrNull(t["expiresAt"]) ?? 0,
            };
        }

        public static List<VcOffer> VcOffers(JToken body, out int skipped)
        {
            skipped = 0;
            var list = new List<VcOffer>();
            foreach (var row in VWire.List(body, "offers", "items"))
            {
                try
                {
                    var o = VcTerm(row);
                    if (o.InvestCents <= 0 || o.EquityBps < 1 || o.EquityBps > 10_000) throw new FormatException("Offer out of range");
                    list.Add(o);
                }
                catch (FormatException) { skipped++; }
            }
            return list;
        }

        public static Page<VcDeal> VcDeals(JToken body)
        {
            var page = new Page<VcDeal> { NextCursor = body is JObject o ? VWire.Str(o["nextCursor"]) : null };
            foreach (var row in VWire.List(body, "deals", "items"))
            {
                try
                {
                    page.Items.Add(new VcDeal
                    {
                        ProjectId = Id(row, "projectId"), ProjectName = VWire.Plain(row["projectName"], 40), VcArchetypeId = VWire.Plain(row["vcArchetypeId"], 40),
                        InvestCents = VWire.Long(row["investCents"]), EquityBps = VWire.IntOr(row["equityBps"], 0), Status = VWire.Plain(row["status"], 16),
                        AtMs = VWire.TimeMsOrNull(row["at"]) ?? 0,
                    });
                }
                catch (FormatException) { page.Skipped++; }
            }
            return page;
        }
    }
}
