using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife.RektBurger
{
    /// <summary>
    /// Rekt Burger, the "blew my port" fallback job (backend PR #16, docs/CAREERS.md §2). Our own parody brand: original name
    /// and art. A simple clock-in job (user decision, 2026-10-25): the server times the shift and pays; the client never
    /// sends an amount. Pure parsing; tested in EditMode.
    /// </summary>
    public sealed class RbState
    {
        public string JobId = "rekt-burger", Name = "Rekt Burger", VenueId = "the_trenches", PropId = "rekt_burger_counter";
        public bool Unlocked, Employed, HasUniform;
        public string Reason;                     // trencher | broke | liquidated | null
        public long HintCashCents, HintDebtCents, BrokeCashAtMost, LiquidationCashBelow;
        public bool HasHint;
        public int StintShifts;
        public long NetWorthCents, GlowUpNetWorthCents;
        public long BaseCents = 300, PerCorrectCents = 60, MaxShiftCents = 1500, DailyCapCents = 4500, EarnedTodayCents, RemainingTodayCents;
        public long ResetsAtMs;
        public long RatePerMinuteCents = 50, MaxShiftSec = 3600;
        public double CounterX = -301.8, CounterZ = -306.7, CounterRadiusM = 25;
        public string UniformCosmeticId;

        public bool CapReached => RemainingTodayCents <= 0;

        public static RbState Parse(JToken t)
        {
            if (t == null || t.Type != JTokenType.Object) throw new FormatException("rekt burger state");
            var s = new RbState
            {
                JobId = CityWire.Str(t["jobId"]) ?? "rekt-burger",
                Name = CityWire.Plain(t["name"], 40) is string n && n.Length > 0 ? n : "Rekt Burger",
                VenueId = CityWire.Str(t["venueId"]) ?? "the_trenches",
                PropId = CityWire.Str(t["propId"]) ?? "rekt_burger_counter",
                Unlocked = CityWire.BoolOr(t["unlocked"], false),
                Reason = CityWire.Str(t["reason"]),
                Employed = CityWire.BoolOr(t["employed"], false),
                StintShifts = CityWire.IntOr(t["stintShifts"], 0),
                NetWorthCents = CityWire.LongOr(t["netWorthCents"], 0),
                GlowUpNetWorthCents = CityWire.LongOr(t["glowUpNetWorthCents"], 100000),
                UniformCosmeticId = CityWire.Str(t["uniformCosmeticId"]),
                HasUniform = CityWire.BoolOr(t["hasUniform"], false),
            };
            if (t["lockedHint"] is JObject h)
            {
                s.HasHint = true;
                s.HintCashCents = CityWire.LongOr(h["cashCents"], 0);
                s.HintDebtCents = CityWire.LongOr(h["debtCents"], 0);
                s.BrokeCashAtMost = CityWire.LongOr(h["brokeCashCentsAtMost"], 0);
                s.LiquidationCashBelow = CityWire.LongOr(h["liquidationCashBelowCents"], 2000);
            }
            var pay = t["pay"];
            s.BaseCents = CityWire.LongOr(pay?["baseCents"], 300);
            s.PerCorrectCents = CityWire.LongOr(pay?["perCorrectOrderCents"], 60);
            s.MaxShiftCents = CityWire.LongOr(pay?["maxShiftCents"], 1500);
            s.DailyCapCents = CityWire.LongOr(pay?["dailyCapCents"], 4500);
            s.EarnedTodayCents = CityWire.LongOr(pay?["earnedTodayCents"], 0);
            s.RemainingTodayCents = CityWire.LongOr(pay?["remainingTodayCents"], s.DailyCapCents - s.EarnedTodayCents);
            s.ResetsAtMs = CityWire.TimeMsOrNull(pay?["resetsAt"]) ?? 0;
            s.RatePerMinuteCents = CityWire.LongOr(pay?["ratePerMinuteCents"], 50);
            s.MaxShiftSec = CityWire.LongOr(pay?["maxShiftSec"], 3600);
            var counter = t["counter"];
            s.CounterX = CityWire.DoubleOr(counter?["x"], -301.8);
            s.CounterZ = CityWire.DoubleOr(counter?["z"], -306.7);
            s.CounterRadiusM = CityWire.DoubleOr(counter?["radiusM"], 25);
            return s;
        }
    }

    /// <summary>Realtime `career_promoted` (city room).</summary>
    public sealed class CareerPromotion
    {
        public string LadderId, LadderName, Title;
        public int FromRank, ToRank, PayBonusBps, Xp;
        public List<string> VenueAccess = new List<string>(), CosmeticIds = new List<string>();

        public static CareerPromotion From(object payload)
        {
            var t = payload as JToken ?? (payload != null ? JObject.FromObject(payload) : null);
            if (t == null) return null;
            var p = new CareerPromotion
            {
                LadderId = CityWire.Str(t["ladderId"]),
                LadderName = CityWire.Plain(t["ladderName"], 40),
                Title = CityWire.Plain(t["title"], 60),
                FromRank = CityWire.IntOr(t["fromRank"], 1),
                ToRank = CityWire.IntOr(t["toRank"], 1),
                PayBonusBps = CityWire.IntOr(t["payBonusBps"], 0),
                Xp = CityWire.IntOr(t["xp"], 0),
            };
            if (t["venueAccess"] is JArray va) p.VenueAccess = va.Select(x => x.ToString()).ToList();
            if (t["cosmeticIds"] is JArray ci) p.CosmeticIds = ci.Select(x => x.ToString()).ToList();
            return string.IsNullOrEmpty(p.Title) ? null : p;
        }
    }
}
