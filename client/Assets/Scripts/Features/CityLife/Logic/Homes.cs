using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    /// <summary>One rentable home type (contract `Home` + `ids/homes.json`).</summary>
    public sealed class HomeOffer
    {
        public string Id, Name;
        public long RentCents;
        public int Rooms, Phase;
        public long? NextDueAtMs;
        /// <summary>True when the row came from GET /homes (server truth), not only the bundled/registry config.</summary>
        public bool FromServer;
        /// <summary>Proposed `Home.status` (active | overdue | evicted) and `Home.missedWeeks`; null / -1 until served.</summary>
        public string Status;
        public int MissedWeeks = -1;
        /// <summary>Proposed `Home.frameSlots`; -1 until served.</summary>
        public int FrameSlots = -1;
    }

    /// <summary>
    /// The five homes from the spec, Parent's Basement → Marina Villa. Rows come from GET /homes when the server has it,
    /// else from `ids.homes` with rent from `economy.rentCentsPerWeek`. Rent is never invented: a home with no rent in
    /// either source is listed without a price and cannot be rented from the UI.
    /// </summary>
    public static class HomeCatalog
    {
        public static readonly string[] SpecOrder = { "parents_basement", "hacker_house", "studio", "penthouse", "marina_villa" };

        public static int Tier(string id)
        {
            int i = Array.IndexOf(SpecOrder, id);
            return i < 0 ? SpecOrder.Length : i;
        }

        /// <summary>`ids.homes` (`{homes: [...]}` or `{items: [...]}`) with rent from `economy.rentCentsPerWeek`.</summary>
        public static List<HomeOffer> FromRegistry(JToken idsHomes, JToken economy)
        {
            var list = new List<HomeOffer>();
            JArray rows = null;
            try { rows = CityWire.List(idsHomes, "homes", "items"); } catch (FormatException) { }
            var rent = economy?["rentCentsPerWeek"] as JObject;
            if (rows != null)
                foreach (var r in rows)
                {
                    var id = CityWire.Str(r?["id"]);
                    if (!CityWire.IsSafeId(id)) continue;
                    long cents = CityWire.LongOr(rent?[id], CityWire.LongOr(r["rentCents"], -1));
                    list.Add(new HomeOffer
                    {
                        Id = id, Name = CityWire.Plain(r["name"], 40), RentCents = cents,
                        Rooms = CityWire.IntOr(r["rooms"], 0), Phase = CityWire.IntOr(r["phase"], 1),
                    });
                }
            foreach (var id in SpecOrder)
                if (list.All(h => h.Id != id) && rent?[id] != null)
                    list.Add(new HomeOffer { Id = id, Name = "", RentCents = CityWire.LongOr(rent[id], -1), Phase = 1 });
            return Sort(list);
        }

        /// <summary>Overlays GET /homes (contract: bare `Home[]`; tolerated: `{homes|items: []}`) on the registry rows.</summary>
        public static List<HomeOffer> Merge(List<HomeOffer> registry, JToken response)
        {
            var rows = CityWire.List(response, "homes", "items");
            var byId = (registry ?? new List<HomeOffer>()).ToDictionary(h => h.Id, h => h, StringComparer.Ordinal);
            foreach (var r in rows)
            {
                var id = CityWire.Str(r?["id"]) ?? CityWire.Str(r?["kind"]);
                if (!CityWire.IsSafeId(id)) continue;
                if (!byId.TryGetValue(id, out var h)) byId[id] = h = new HomeOffer { Id = id, Phase = 4 };
                var name = CityWire.Plain(r["name"], 40);
                if (!string.IsNullOrEmpty(name)) h.Name = name;
                h.RentCents = CityWire.LongOr(r["rentCents"], CityWire.LongOr(r["rentCentsPerWeek"], h.RentCents));
                h.NextDueAtMs = CityWire.TimeMsOrNull(r["nextDueAt"]) ?? h.NextDueAtMs;
                var status = CityWire.Str(r["status"]);
                if (status == "active" || status == "overdue" || status == "evicted") h.Status = status;
                h.MissedWeeks = CityWire.IntOr(r["missedWeeks"], h.MissedWeeks);
                h.FrameSlots = CityWire.IntOr(r["frameSlots"], h.FrameSlots);
                h.Rooms = CityWire.IntOr(r["rooms"], h.Rooms);
                h.FromServer = true;
            }
            return Sort(byId.Values.ToList());
        }

        private static List<HomeOffer> Sort(List<HomeOffer> list) =>
            list.OrderBy(h => Tier(h.Id)).ThenBy(h => h.RentCents < 0 ? long.MaxValue : h.RentCents).ThenBy(h => h.Id, StringComparer.Ordinal).ToList();

        /// <summary>
        /// Frames a home can show: one per authored wall spot, capped by the home's room count (`ids/homes.json rooms`)
        /// until the proposed `frameSlots` field exists. [Inference] one frame per room is a presentation choice.
        /// </summary>
        public static int FrameSlots(HomeOffer home, int authoredWallSpots, int serverFrameSlots = -1)
        {
            if (authoredWallSpots <= 0) return 0;
            if (serverFrameSlots >= 0) return Math.Min(authoredWallSpots, serverFrameSlots);
            return home != null && home.Rooms > 0 ? Math.Min(authoredWallSpots, home.Rooms) : authoredWallSpots;
        }
    }

    /// <summary>
    /// Furniture in a home scene → the needs action it runs. FE-owned presentation over FE-owned spot ids
    /// (contract/ids/spots.json `home.bed`, `home.desk`, `home.sofa` exist; `fridge`, `shower`, `plant`, `speaker` and
    /// `wall_1…12` are proposed in docs/fe-contract-proposals/ids/home-spots-city-life.md). The server still decides
    /// every action (POST /needs/actions).
    /// </summary>
    public static class HomeFurniture
    {
        private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["bed"] = new[] { "sleep" },
            ["sofa"] = new[] { "sleep", "party" },
            ["desk"] = new[] { "eat" },
            ["fridge"] = new[] { "eat" },
            ["kitchen"] = new[] { "eat" },
            ["shower"] = new[] { "shower" },
            ["bath"] = new[] { "shower" },
            ["plant"] = new[] { "touch_grass" },
            ["balcony"] = new[] { "touch_grass" },
            ["garden"] = new[] { "touch_grass" },
            ["speaker"] = new[] { "party" },
        };

        /// <summary>`home.bed`, `bed`, `bed_2` → "bed".</summary>
        public static string Kind(string spotId)
        {
            if (string.IsNullOrEmpty(spotId)) return null;
            var s = spotId;
            int dot = s.LastIndexOf('.');
            if (dot >= 0) s = s.Substring(dot + 1);
            int us = s.LastIndexOf('_');
            if (us > 0 && us < s.Length - 1 && s.Substring(us + 1).All(char.IsDigit)) s = s.Substring(0, us);
            return s;
        }

        public static string[] ActionsFor(string spotId)
        {
            var k = Kind(spotId);
            return k != null && Map.TryGetValue(k, out var a) ? a : Array.Empty<string>();
        }

        public static bool IsFrameSpot(string spotId)
        {
            var k = Kind(spotId);
            return k == "wall" || k == "frame";
        }

        /// <summary>1-based frame index from `wall_3` / `home.frame_3`; 0 when absent.</summary>
        public static int FrameIndex(string spotId)
        {
            if (!IsFrameSpot(spotId)) return 0;
            int us = spotId.LastIndexOf('_');
            return us > 0 && int.TryParse(spotId.Substring(us + 1), out var n) && n > 0 ? n : 0;
        }
    }

    /// <summary>A lease receipt: contract `{homeId, rentCents, nextDueAt}`; tolerated backend `HomeRow {kind, rentCentsPerWeek, lastRentPaidAt}`.</summary>
    public sealed class LeaseReceipt
    {
        public string HomeId;
        public long RentCents;
        public long? NextDueAtMs, LastPaidAtMs;

        public static LeaseReceipt Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a lease");
            var kind = CityWire.Str(o["kind"]);
            var id = CityWire.IsSafeId(kind) ? kind : CityWire.Str(o["homeId"]);
            if (string.IsNullOrEmpty(id)) throw new FormatException("Missing homeId");
            return new LeaseReceipt
            {
                HomeId = id,
                RentCents = CityWire.LongOr(o["rentCents"], CityWire.LongOr(o["rentCentsPerWeek"], -1)),
                NextDueAtMs = CityWire.TimeMsOrNull(o["nextDueAt"]),
                LastPaidAtMs = CityWire.TimeMsOrNull(o["lastRentPaidAt"]),
            };
        }
    }

    // ------------------------------------------------------------------ rent, debt, eviction

    public enum RentState { Unknown, Paid, DueSoon, ShortOfCash, DueNow, Overdue, EvictionRisk, Evicted }

    /// <summary>
    /// Rent terms from config. `economy.lottery.exit_liquidity` (weeklyDueCents, missedPenaltyBps) is contract config.
    /// `economy.rentPolicy {graceWeeks, evictToHomeId, dueSoonHours}` is the FE proposal; while it is absent the client
    /// never shows an eviction countdown it cannot source.
    /// </summary>
    public sealed class RentPolicy
    {
        public int GraceWeeks = -1;
        public string EvictToHomeId;
        public long LotteryWeeklyDueCents = -1;
        public int LotteryMissedPenaltyBps = -1;
        /// <summary>FE presentation: how early "due soon" shows. Proposed as `rentPolicy.dueSoonHours`.</summary>
        public int DueSoonHours = 48;

        public static RentPolicy Parse(JToken economy)
        {
            var p = new RentPolicy();
            var el = economy?["lottery"]?["exit_liquidity"];
            p.LotteryWeeklyDueCents = CityWire.LongOr(el?["weeklyDueCents"], -1);
            p.LotteryMissedPenaltyBps = CityWire.IntOr(el?["missedPenaltyBps"], -1);
            var rp = economy?["rentPolicy"];
            if (rp is JObject)
            {
                p.GraceWeeks = CityWire.IntOr(rp["graceWeeks"], -1);
                var to = CityWire.Str(rp["evictToHomeId"]);
                p.EvictToHomeId = CityWire.IsSafeId(to) ? to : null;
                var soon = CityWire.IntOr(rp["dueSoonHours"], 48);
                if (soon > 0 && soon <= 168) p.DueSoonHours = soon;
            }
            return p;
        }
    }

    /// <summary>What the client knows about the lease right now. Proposal fields stay null/-1 until the server sends them.</summary>
    public sealed class RentInputs
    {
        public string HomeId;
        public long RentCents = -1;
        public long? NextDueAtMs;
        public long CashCents, DebtCents;
        /// <summary>Proposed `Home.status`: active | overdue | evicted.</summary>
        public string ServerStatus;
        /// <summary>Proposed `Home.missedWeeks`; -1 unknown.</summary>
        public int MissedWeeks = -1;
    }

    public readonly struct RentView
    {
        public readonly RentState State;
        public readonly bool InDebt;
        public readonly long DueAtMs, RentCents, ShortfallCents;
        /// <summary>Weeks of grace left before eviction (only when the server policy and missed count are known), else -1.</summary>
        public readonly int WeeksBeforeEviction;

        public RentView(RentState state, bool inDebt, long dueAtMs, long rentCents, long shortfall, int weeksBeforeEviction)
        {
            State = state; InDebt = inDebt; DueAtMs = dueAtMs; RentCents = rentCents; ShortfallCents = shortfall; WeeksBeforeEviction = weeksBeforeEviction;
        }

        public string Key => "rent.state." + State.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Rent, debt and eviction state for display (Sunday Unlock collects rent; contract section 1 + Build Spec "Core
    /// loop" 6). Server-authoritative: the client only reads the lease, Cash and debt it was given and never moves money.
    /// Pure; the transitions are tested.
    /// </summary>
    public static class RentLedger
    {
        public static RentView Evaluate(RentInputs i, RentPolicy policy, double nowMs, double unlockFallbackMs)
        {
            policy = policy ?? new RentPolicy();
            if (i == null || string.IsNullOrEmpty(i.HomeId)) return new RentView(RentState.Unknown, false, 0, -1, 0, -1);
            bool inDebt = i.DebtCents > 0;
            long due = i.NextDueAtMs ?? (long)unlockFallbackMs;
            if (string.Equals(i.ServerStatus, "evicted", StringComparison.Ordinal))
                return new RentView(RentState.Evicted, inDebt, due, i.RentCents, 0, 0);
            int left = policy.GraceWeeks > 0 && i.MissedWeeks >= 0 ? Math.Max(0, policy.GraceWeeks - i.MissedWeeks) : -1;
            if (string.Equals(i.ServerStatus, "overdue", StringComparison.Ordinal) || i.MissedWeeks > 0)
            {
                var state = left >= 0 && left <= 1 ? RentState.EvictionRisk : RentState.Overdue;
                return new RentView(state, inDebt, due, i.RentCents, Shortfall(i), left);
            }
            if (i.RentCents < 0) return new RentView(RentState.Unknown, inDebt, due, -1, 0, -1);
            if (nowMs >= due) return new RentView(RentState.DueNow, inDebt, due, i.RentCents, Shortfall(i), left);
            if (due - nowMs <= policy.DueSoonHours * 3_600_000.0)
            {
                long shortfall = Shortfall(i);
                return new RentView(shortfall > 0 ? RentState.ShortOfCash : RentState.DueSoon, inDebt, due, i.RentCents, shortfall, left);
            }
            return new RentView(RentState.Paid, inDebt, due, i.RentCents, 0, left);
        }

        private static long Shortfall(RentInputs i) => i.RentCents > i.CashCents ? i.RentCents - i.CashCents : 0;

        /// <summary>
        /// The message to show when the rent picture changes between two observations (null = say nothing).
        /// Severity only escalates through toasts; recoveries get one "settled" line.
        /// </summary>
        public static string Transition(RentView? before, RentView after, long debtBefore, long debtAfter)
        {
            if (before == null) return after.State == RentState.Evicted ? "rent.toast.evicted" : null;
            var b = before.Value;
            if (after.State == RentState.Evicted && b.State != RentState.Evicted) return "rent.toast.evicted";
            if (after.State == RentState.EvictionRisk && b.State != RentState.EvictionRisk) return "rent.toast.evictionRisk";
            if (after.State == RentState.Overdue && b.State < RentState.Overdue) return "rent.toast.overdue";
            if (debtAfter > debtBefore && debtBefore >= 0) return "rent.toast.debtUp";
            if (b.State >= RentState.DueNow && after.State == RentState.Paid) return "rent.toast.settled";
            if (after.State == RentState.ShortOfCash && b.State != RentState.ShortOfCash) return "rent.toast.short";
            return null;
        }
    }

    // ------------------------------------------------------------------ moving homes

    public enum MoveStage { Idle, Confirming, Signing, Done, Rejected, Unknown }

    /// <summary>
    /// Sign a lease (contract `POST /homes/{homeId}/rent`): moving to another home, or renewing the current one to settle
    /// rent. One retained Idempotency-Key per exact request: an unknown outcome retries with the same key, a definitive
    /// answer retires it. Pure; tested.
    /// </summary>
    public sealed class MoveHomeFlow
    {
        private readonly RetainedKey _key;
        public MoveStage Stage { get; private set; } = MoveStage.Idle;
        public HomeOffer Target { get; private set; }
        public string FromHomeId { get; private set; }
        public CityError Error { get; private set; }
        public LeaseReceipt Receipt { get; private set; }
        public event Action Changed;

        public MoveHomeFlow(Func<string> newKey = null) { _key = new RetainedKey(newKey); }

        public bool IsRenewal => Target != null && Target.Id == FromHomeId;

        /// <summary>Pick a home. Not allowed while a lease request is in flight or its outcome is unknown.</summary>
        public bool Select(HomeOffer target, string currentHomeId)
        {
            if (target == null || !CityWire.IsSafeId(target.Id) || target.RentCents < 0) return false;
            if (Stage == MoveStage.Signing || Stage == MoveStage.Unknown) return false;
            Target = target;
            FromHomeId = currentHomeId;
            Error = null;
            Receipt = null;
            Stage = MoveStage.Confirming;
            Changed?.Invoke();
            return true;
        }

        public string Path => Target == null ? null : "/homes/" + CityWire.Seg(Target.Id) + "/rent";

        /// <summary>Confirming, Rejected (a fresh try) or Unknown (same key) → Signing. Returns the Idempotency-Key, or null.</summary>
        public string BeginSign()
        {
            if (Target == null || (Stage != MoveStage.Confirming && Stage != MoveStage.Unknown && Stage != MoveStage.Rejected)) return null;
            Stage = MoveStage.Signing;
            Error = null;
            Changed?.Invoke();
            return _key.KeyFor("POST", Path, "");
        }

        public void Succeeded(LeaseReceipt receipt)
        {
            if (Stage != MoveStage.Signing) return;
            _key.Complete();
            Receipt = receipt;
            Stage = MoveStage.Done;
            Changed?.Invoke();
        }

        public void Failed(CityError error)
        {
            if (Stage != MoveStage.Signing) return;
            Error = error ?? new CityError { Code = "ERROR", Key = "err.generic" };
            _key.Settle(Error);
            Stage = Error.OutcomeUnknown ? MoveStage.Unknown : MoveStage.Rejected;
            Changed?.Invoke();
        }

        /// <summary>Back to browsing. From Unknown this abandons the retained key on purpose (the caller re-reads /me first).</summary>
        public void Cancel()
        {
            if (Stage == MoveStage.Signing) return;
            _key.Complete();
            Stage = MoveStage.Idle;
            Target = null;
            Error = null;
            Changed?.Invoke();
        }

        public bool KeyRetained => _key.Pending;
    }
}
