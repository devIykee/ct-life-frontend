using System.Collections.Generic;
using System.Globalization;

namespace CTLife.UI
{
    public static class EarningsStrings
    {
        private static readonly Dictionary<string, string> Copy = new Dictionary<string, string>
        {
            ["eyebrow"] = "JUNIOR ANALYST", ["title"] = "Receipts over vibes.",
            ["rules"] = "Read the figures. File the answer the numbers support.",
            ["fictional"] = "FICTIONAL REPORTS · GAME CASH FIGURES",
            ["practice"] = "PRACTICE · NO PAYOUT", ["live"] = "SHIFT IN PROGRESS",
            ["progress"] = "REPORT {0:00} / {1:00}", ["correctCount"] = "{0} / {1} verified",
            ["timer"] = "{0}s", ["next"] = "Next report", ["finish"] = "File the last report",
            ["correct"] = "The figures check out.", ["incorrect"] = "The headline got ahead of the numbers.",
            ["clear"] = "Research filed.", ["clearDetail"] = "{0} of {1} reports verified. Your shift ends when the clock does.",
            ["clock"] = "The shift clock continues while you are away.",
            ["done"] = "Research complete", ["result"] = "{0} / {1} points · no Game Cash or XP awarded",
            ["interrupted"] = "Practice interrupted", ["interruptedDetail"] = "This practice could not finish. No Game Cash or XP was awarded. You can try again or return to the city.",
            ["again"] = "Play again", ["back"] = "Return to city", ["kCash"] = "{0:0.##}k Cash",
            ["percent"] = "{0:0.##}%", ["months"] = "{0:0.##} months", ["perShare"] = "{0:0.##} Cash / share",
            ["profitOption"] = "Profit: {0:0.##}k Cash", ["coreOption"] = "Core result: {0:+0.##;-0.##;0}k Cash",
            ["0.company"] = "GM Coffee Co.", ["0.headline"] = "“Revenue is flying. Profit must be too.”",
            ["0.question"] = "What was this quarter’s net profit?",
            ["0.a"] = "Revenue", ["0.b"] = "All expenses", ["0.note"] = "Same quarter. All expenses are included.",
            ["0.reason"] = "Revenue {0} − expenses {1} = {2}. Revenue is not profit.",
            ["1.company"] = "Blockspace Bakery", ["1.headline"] = "“We’re at {0} of last quarter. That’s {0} growth, right?”",
            ["1.question"] = "How much did revenue grow?",
            ["1.a"] = "Last quarter", ["1.b"] = "This quarter", ["1.note"] = "Revenue, measured over equal-length quarters.",
            ["1.reason"] = "Revenue rose from {0} to {1}. Divide the {4} increase by last quarter’s revenue: {2} growth.",
            ["2.company"] = "Runway Labs", ["2.headline"] = "“We have enough runway. Please stop asking how much.”",
            ["2.question"] = "How long does the cash last?",
            ["2.a"] = "Cash available", ["2.b"] = "Monthly net burn", ["2.note"] = "Burn stays constant. No new funding or extra cash inflows.",
            ["2.reason"] = "{0} in cash ÷ {1} net burn each month = {2}. A funding announcement is not cash in the bank.",
            ["3.company"] = "Validator Valet", ["3.headline"] = "“Our margin is {0}. I circled the bigger number.”",
            ["3.question"] = "What is the net profit margin?",
            ["3.a"] = "Revenue", ["3.b"] = "All expenses", ["3.note"] = "Net margin = (revenue − expenses) ÷ revenue.",
            ["3.reason"] = "{0} revenue − {1} expenses leaves {2} of revenue as profit. The {5} is the expense share.",
            ["4.company"] = "One More Pivot", ["4.headline"] = "“Profitable quarter. Ignore the office we just sold.”",
            ["4.question"] = "How did core operations perform?",
            ["4.a"] = "Core revenue", ["4.b"] = "Operating costs", ["4.note"] = "A separate, one-off asset sale added {0}. Exclude it from core operations.",
            ["4.reason"] = "Core revenue {0} − operating costs {1} = {2}. The one-off sale improved the reported total, not core operations.",
            ["5.company"] = "More Shares Ltd.", ["5.headline"] = "“Total profit went up. Every share must earn more.”",
            ["5.question"] = "What are current earnings per share?",
            ["5.a"] = "Last quarter profit", ["5.b"] = "This quarter profit", ["5.note"] = "Current diluted share count: {0}k. Divide this quarter’s profit by current shares.",
            ["5.reason"] = "This quarter’s {1} profit ÷ {3}k current shares = {2}. Higher total profit alone does not tell you what each share earns."
        };
        public static string T(string key) => Copy.TryGetValue(key, out string value) ? value : key;
        public static string F(string key, params object[] values) => string.Format(CultureInfo.CurrentCulture, T(key), values);
        public static string Option(EarningsReport report, decimal value)
        {
            string key = report.Kind == 0 ? "profitOption" : report.Kind == 1 || report.Kind == 3 ? "percent" :
                report.Kind == 2 ? "months" : report.Kind == 4 ? "coreOption" : "perShare";
            return F(key, value);
        }
        public static string Headline(EarningsReport report)
        {
            decimal mistakenPercent = report.Kind == 1 || report.Kind == 3 ? 100m * report.B / report.A : 0;
            return F(report.Kind + ".headline", F("percent", mistakenPercent));
        }
        public static string Note(EarningsReport report) => F(report.Kind + ".note", report.Kind == 4 ? F("kCash", report.C) : report.C.ToString(CultureInfo.CurrentCulture));
        public static string Reason(EarningsReport report) => F(report.Kind + ".reason", F("kCash", report.A), F("kCash", report.B), Option(report, report.Answer), report.C, F("kCash", report.B - report.A), F("percent", 100m * report.B / report.A));
    }
}
