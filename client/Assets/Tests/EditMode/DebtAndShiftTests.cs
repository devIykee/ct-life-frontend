using System;
using CTLife.Net;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Tests
{
    /// <summary>Backend eeef49d (2026-10-17): the debt instalment from GET /me and how the phone says it; the shift-ready banner timing.</summary>
    public class DebtAndShiftTests
    {
        /// <summary>The same decode GET /me uses (MeCompat, then the typed model with the client's JSON settings).</summary>
        private static (long? due, long? weekly) Decode(string json)
        {
            var body = JObject.Parse("{\"user\":{\"id\":\"01a11953-dc8d-7983-ad9c-eb0104443d3b\",\"wallet\":\"w\",\"isSeeker\":false}," +
                "\"character\":null,\"needs\":{\"energy\":50,\"hunger\":50,\"fun\":50,\"hygiene\":50,\"sanity\":50},\"skills\":{\"clout\":0,\"coding\":0,\"trading\":0,\"writing\":0,\"design\":0,\"security\":0},\"cashCents\":0,\"debtCents\":0,\"homeId\":\"parents_basement\",\"minerTier\":\"none\",\"flags\":{}}");
            body.Merge(JObject.Parse(json));
            var me = MeCompat.Normalize(body).ToObject<CTLife.Rest.Model.Me>(Newtonsoft.Json.JsonSerializer.Create(CTLife.Core.Json.Settings));
            return DebtSchedule.From(me);
        }

        [Test]
        public void DebtSchedule_ReadsTheTypedMeFields_NullsStayUnknown()
        {
            var (due, weekly) = Decode("{\"debtCents\":72000,\"debtDueAt\":\"2026-10-18T20:00:00.000Z\",\"debtWeeklyDueCents\":12000}");
            Assert.AreEqual(DateTimeOffset.Parse("2026-10-18T20:00:00Z").ToUnixTimeMilliseconds(), due);
            Assert.AreEqual(12000, weekly);
            var (n1, n2) = Decode("{\"debtCents\":0,\"debtDueAt\":null,\"debtWeeklyDueCents\":null}");
            Assert.IsNull(n1); Assert.IsNull(n2, "null is unknown, not $0");
            var (g1, g2) = Decode("{\"debtDueAt\":\"soon\",\"debtWeeklyDueCents\":\"-5\"}");
            Assert.IsNull(g1, "garbage is unknown, never a date (and never a failed /me)"); Assert.IsNull(g2);
            var (o1, o2) = Decode("{\"debtCents\":100}");
            Assert.IsNull(o1, "an older backend: no schedule"); Assert.IsNull(o2);
            var (p1, p2) = Decode("{\"debtDueAt\":\"2026-10-18T21:00:00+01:00\",\"debtWeeklyDueCents\":\"500\"}");
            Assert.AreEqual(DateTimeOffset.Parse("2026-10-18T20:00:00Z").ToUnixTimeMilliseconds(), p1, "offsets are honoured");
            Assert.AreEqual(500, p2);
        }

        private static long Ms(string iso) => DateTimeOffset.Parse(iso).ToUnixTimeMilliseconds();

        [Test]
        public void DebtDue_SaysWhenInThePlayersTime()
        {
            var utc = TimeZoneInfo.Utc;
            long due = Ms("2026-10-18T20:00:00Z");   // Sunday Unlock
            Assert.AreEqual("Sun 20:00", DebtDue.When(due, Ms("2026-10-15T09:00:00Z"), utc));
            Assert.AreEqual(Strings.F("debt.tomorrow", "20:00"), DebtDue.When(due, Ms("2026-10-17T22:00:00Z"), utc));
            Assert.AreEqual(Strings.F("debt.today", "20:00"), DebtDue.When(due, Ms("2026-10-18T08:00:00Z"), utc));
            Assert.AreEqual(Strings.T("debt.overdue"), DebtDue.When(due, Ms("2026-10-18T20:00:01Z"), utc));
            Assert.AreEqual("18 Oct", DebtDue.When(due, Ms("2026-10-08T09:00:00Z"), utc));
            var lagos = TimeZoneInfo.CreateCustomTimeZone("WAT", TimeSpan.FromHours(1), "WAT", "WAT");
            Assert.AreEqual("Sun 21:00", DebtDue.When(due, Ms("2026-10-15T09:00:00Z"), lagos), "shown in local time");
            Assert.IsNull(DebtDue.Line(null, due, Ms("2026-10-15T09:00:00Z"), utc), "no schedule, no line");
            StringAssert.Contains("$120", DebtDue.Line(12000, due, Ms("2026-10-15T09:00:00Z"), utc));
        }

        [Test]
        public void ShiftReady_FiresAtEndsAt_NotBefore_NotForAStaleShift()
        {
            long ends = Ms("2026-10-18T12:00:00Z");
            Assert.IsFalse(ShiftReadyNotice.Due(ends, ends - 1));
            Assert.IsTrue(ShiftReadyNotice.Due(ends, ends));
            Assert.IsTrue(ShiftReadyNotice.Due(ends, ends + 3600_000));
            Assert.IsFalse(ShiftReadyNotice.Due(ends, ends + 7 * 3600_000L), "a shift from hours ago is not news");
            Assert.IsFalse(ShiftReadyNotice.Due(-1, ends));
        }
    }
}
