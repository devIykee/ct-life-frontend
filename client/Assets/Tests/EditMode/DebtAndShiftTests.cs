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
        [Test]
        public void DebtSchedule_ReadsTheNewMeFields_NullsStayUnknown()
        {
            var (due, weekly) = DebtSchedule.Read(JObject.Parse("{\"debtCents\":72000,\"debtDueAt\":\"2026-10-18T20:00:00.000Z\",\"debtWeeklyDueCents\":12000}"));
            Assert.AreEqual(DateTimeOffset.Parse("2026-10-18T20:00:00Z").ToUnixTimeMilliseconds(), due);
            Assert.AreEqual(12000, weekly);
            var (n1, n2) = DebtSchedule.Read(JObject.Parse("{\"debtCents\":0,\"debtDueAt\":null,\"debtWeeklyDueCents\":null}"));
            Assert.IsNull(n1); Assert.IsNull(n2);
            var (g1, g2) = DebtSchedule.Read(JObject.Parse("{\"debtDueAt\":\"soon\",\"debtWeeklyDueCents\":\"-5\"}"));
            Assert.IsNull(g1, "garbage is unknown, never a date"); Assert.IsNull(g2);
            var (o1, o2) = DebtSchedule.Read(JObject.Parse("{\"debtCents\":100}"));
            Assert.IsNull(o1, "an older backend: no schedule"); Assert.IsNull(o2);
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
