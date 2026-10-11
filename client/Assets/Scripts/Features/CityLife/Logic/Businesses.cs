using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    /// <summary>
    /// A business type from config `businesses.json` (contract): cost, income range, bad-day chance and multiplier.
    /// `sellBackBps`, `payoutPeriodHours`, `maxOwned` and `badDays` are FE proposals
    /// (docs/fe-contract-proposals/city-life-businesses.md); absent → the UI does not show those numbers.
    /// </summary>
    public sealed class BusinessDef
    {
        public string Id, Name;
        public long CostCents, IncomeMinCents, IncomeMaxCents;
        public int BadDayChanceBps;
        public double BadDayMultiplier;
        public int SellBackBps = -1, PayoutPeriodHours = -1, MaxOwned = -1;
        public readonly List<string> BadDayIds = new List<string>();

        /// <summary>config README: "badDayMultiplier is the fraction of the lower bound the player receives on a bad day".</summary>
        public long BadDayPayCents => (long)Math.Floor(IncomeMinCents * BadDayMultiplier);

        /// <summary>
        /// [Inference] Expected payout per period if income is uniform over the range: (1 − p)·mean + p·badDay. The
        /// server's distribution is not in the contract, so the UI labels this an estimate.
        /// </summary>
        public double ExpectedPayCents
        {
            get
            {
                double p = BadDayChanceBps / 10000.0;
                double mean = (IncomeMinCents + IncomeMaxCents) / 2.0;
                return (1 - p) * mean + p * BadDayPayCents;
            }
        }

        /// <summary>[Inference] Periods to earn back the price at the expected payout; -1 when undefined.</summary>
        public double BreakEvenPeriods => ExpectedPayCents > 0 ? CostCents / ExpectedPayCents : -1;

        public long SellBackCents => SellBackBps >= 0 ? CostCents * SellBackBps / 10000 : -1;

        public static List<BusinessDef> ParseConfig(JToken cfg)
        {
            var list = new List<BusinessDef>();
            JArray rows;
            try { rows = CityWire.List(cfg, "businesses", "items"); } catch (FormatException) { return list; }
            foreach (var r in rows)
            {
                var id = CityWire.Str(r?["id"]);
                if (!CityWire.IsSafeId(id)) continue;
                var range = r["incomeRangeCents"] as JArray;
                if (range == null || range.Count != 2) continue;
                long min = CityWire.LongOr(range[0], -1), max = CityWire.LongOr(range[1], -1), cost = CityWire.LongOr(r["costCents"], -1);
                if (min < 0 || max < min || cost <= 0) continue;
                var d = new BusinessDef
                {
                    Id = id, Name = CityWire.Plain(r["name"], 40), CostCents = cost, IncomeMinCents = min, IncomeMaxCents = max,
                    BadDayChanceBps = Math.Max(0, Math.Min(10000, CityWire.IntOr(r["badDayChanceBps"], 0))),
                    BadDayMultiplier = Math.Max(0, Math.Min(1, CityWire.DoubleOr(r["badDayMultiplier"], 0))),
                    SellBackBps = CityWire.IntOr(r["sellBackBps"], -1),
                    PayoutPeriodHours = CityWire.IntOr(r["payoutPeriodHours"], -1),
                    MaxOwned = CityWire.IntOr(r["maxOwned"], -1),
                };
                if (d.SellBackBps > 10000) d.SellBackBps = -1;
                if (r["badDays"] is JArray bad)
                    foreach (var b in bad) { var s = CityWire.Str(b?["id"] ?? b); if (CityWire.IsSafeId(s)) d.BadDayIds.Add(s); }
                list.Add(d);
            }
            return list;
        }
    }

    /// <summary>One day of a business (proposal `BusinessDay {kind: normal|bad_day|great_day, payCents, eventId, at}`).</summary>
    public sealed class BusinessDay
    {
        public string Kind, EventId;
        public long PayCents;
        public long? AtMs;

        public static BusinessDay Parse(JToken t)
        {
            if (!(t is JObject o)) return null;
            return new BusinessDay
            {
                Kind = CityWire.Str(o["kind"]) ?? "normal",
                EventId = CityWire.IsSafeId(CityWire.Str(o["eventId"])) ? CityWire.Str(o["eventId"]) : null,
                PayCents = CityWire.LongOr(o["payCents"], 0),
                AtMs = CityWire.TimeMsOrNull(o["at"] ?? o["dayDate"]),
            };
        }
    }

    /// <summary>An owned business (proposal `Business`).</summary>
    public sealed class OwnedBusiness
    {
        public string BusinessId, KindId;
        public long PendingCents, SellBackCents = -1;
        public long? BoughtAtMs, NextPayoutAtMs;
        public BusinessDay LastDay;

        public static OwnedBusiness Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a business");
            var id = CityWire.ReqStr(o["businessId"], "businessId");
            if (!CityWire.IsSafeId(id)) throw new FormatException("Invalid businessId");
            var kind = CityWire.Str(o["kindId"]) ?? CityWire.Str(o["kind"]);
            return new OwnedBusiness
            {
                BusinessId = id, KindId = CityWire.IsSafeId(kind) ? kind : null,
                PendingCents = Math.Max(0, CityWire.LongOr(o["pendingCents"], 0)),
                SellBackCents = CityWire.LongOr(o["sellBackCents"], -1),
                BoughtAtMs = CityWire.TimeMsOrNull(o["boughtAt"] ?? o["createdAt"]),
                NextPayoutAtMs = CityWire.TimeMsOrNull(o["nextPayoutAt"]),
                LastDay = BusinessDay.Parse(o["lastDay"]),
            };
        }

        public static List<OwnedBusiness> ParseList(JToken t)
        {
            var list = new List<OwnedBusiness>();
            foreach (var r in CityWire.List(t, "owned", "businesses", "items")) list.Add(Parse(r));
            return list;
        }
    }

    /// <summary>Proposal `POST /businesses/{id}/collect` → `{collectedCents, days[], cashCents}`.</summary>
    public sealed class CollectReceipt
    {
        public long CollectedCents;
        public long? CashCents;
        public readonly List<BusinessDay> Days = new List<BusinessDay>();

        public static CollectReceipt Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a collect receipt");
            var r = new CollectReceipt { CollectedCents = CityWire.LongOr(o["collectedCents"], CityWire.LongOr(o["payCents"], 0)), CashCents = CityWire.OptLong(o["cashCents"]) };
            if (o["days"] is JArray days) foreach (var d in days) { var p = BusinessDay.Parse(d); if (p != null) r.Days.Add(p); }
            else if (o["kind"] != null) r.Days.Add(BusinessDay.Parse(o));
            return r;
        }

        public int BadDays => Days.FindAll(d => d.Kind == "bad_day").Count;
    }

    /// <summary>
    /// The business backend surface. Contract rc1 has none; the REST implementation targets the proposal, the stub
    /// reports UNAVAILABLE so the UI can show the catalog read-only. Results are already parsed.
    /// </summary>
    public interface IBusinessGateway
    {
        bool IsStub { get; }
        Task<List<OwnedBusiness>> Owned(CancellationToken ct);
        Task<OwnedBusiness> Buy(string kindId, string idempotencyKey, CancellationToken ct);
        Task<CollectReceipt> Collect(string businessId, string idempotencyKey, CancellationToken ct);
        Task<long?> SellBack(string businessId, string idempotencyKey, CancellationToken ct);
    }

    /// <summary>Stub until the server ships businesses: every call fails with UNAVAILABLE (no fake money, no fake rolls).</summary>
    public sealed class StubBusinessGateway : IBusinessGateway
    {
        public bool IsStub => true;
        private static Task<T> No<T>() => Task.FromException<T>(new UnavailableException());
        public Task<List<OwnedBusiness>> Owned(CancellationToken ct) => No<List<OwnedBusiness>>();
        public Task<OwnedBusiness> Buy(string kindId, string key, CancellationToken ct) => No<OwnedBusiness>();
        public Task<CollectReceipt> Collect(string businessId, string key, CancellationToken ct) => No<CollectReceipt>();
        public Task<long?> SellBack(string businessId, string key, CancellationToken ct) => No<long?>();
    }

    /// <summary>Thrown by stubs; CityErrors maps it through ApiException semantics (404 without a contract body → UNAVAILABLE).</summary>
    public sealed class UnavailableException : Exception
    {
        public UnavailableException() : base("Not available on this server yet") { }
    }

    public enum WriteStage { Idle, Sending, Done, Rejected, Unknown }

    /// <summary>
    /// One retained write (buy / collect / sell / save settings): Idle → Sending → Done | Rejected | Unknown. Unknown keeps
    /// the key so "Try again" replays instead of buying twice. Pure; tested.
    /// </summary>
    public sealed class RetainedWriteFlow
    {
        private readonly RetainedKey _key;
        public WriteStage Stage { get; private set; } = WriteStage.Idle;
        public CityError Error { get; private set; }
        public string Request { get; private set; }
        public event Action Changed;

        public RetainedWriteFlow(Func<string> newKey = null) { _key = new RetainedKey(newKey); }

        /// <summary>Returns the key, or null when busy or when a different request would abandon an unknown outcome.</summary>
        public string Begin(string method, string path, string body)
        {
            if (Stage == WriteStage.Sending) return null;
            var req = method + " " + path + "\n" + (body ?? "");
            if (Stage == WriteStage.Unknown && Request != req) return null;
            Request = req;
            Stage = WriteStage.Sending;
            Error = null;
            Changed?.Invoke();
            return _key.KeyFor(method, path, body);
        }

        public void Succeeded()
        {
            if (Stage != WriteStage.Sending) return;
            _key.Complete();
            Stage = WriteStage.Done;
            Changed?.Invoke();
        }

        public void Failed(CityError e)
        {
            if (Stage != WriteStage.Sending) return;
            Error = e ?? new CityError { Code = "ERROR", Key = "err.generic" };
            _key.Settle(Error);
            Stage = Error.OutcomeUnknown ? WriteStage.Unknown : WriteStage.Rejected;
            Changed?.Invoke();
        }

        /// <summary>Back to Idle. Abandoning an Unknown outcome is explicit (the caller re-reads server state first).</summary>
        public void Reset()
        {
            if (Stage == WriteStage.Sending) return;
            _key.Complete();
            Stage = WriteStage.Idle;
            Error = null;
            Request = null;
            Changed?.Invoke();
        }

        public bool KeyRetained => _key.Pending;
    }
}
