using System;

namespace CTLife.UI
{
    /// <summary>Original fictional reports. Figures are local puzzle inputs, never a market feed or balance.</summary>
    public sealed class EarningsReport
    {
        public int Kind { get; }
        public int A { get; }
        public int B { get; }
        public int C { get; }
        public decimal Answer { get; }
        public decimal[] Options { get; }
        public int CorrectOption { get; }

        public EarningsReport(int kind, Random random)
        {
            Kind = kind;
            int scale = random.Next(1, 4);
            decimal[] values;
            switch (kind)
            {
                case 0: // Net profit = revenue - all expenses.
                    A = 100 * scale; B = 70 * scale; Answer = A - B;
                    values = new[] { Answer, (decimal)A, B }; break;
                case 1: // Growth is the increase / previous revenue, not current / previous.
                    A = 40 * scale; int growth = 25 * random.Next(1, 5); B = A * (100 + growth) / 100;
                    Answer = 100m * (B - A) / A;
                    // Confuse current-as-percent-of-prior or the absolute increase with growth.
                    values = new[] { Answer, 100m * B / A, (decimal)(B - A) }; break;
                case 2: // Constant monthly net burn; no new funding.
                    B = 10 * scale; int months = random.Next(3, 7); A = B * months; Answer = (decimal)A / B;
                    values = new[] { Answer, Answer + 2, Answer * 2 }; break;
                case 3: // Net margin = profit / revenue.
                    int[] margins = { 10, 20, 25, 40 };
                    A = 100 * scale; B = A * (100 - margins[random.Next(margins.Length)]) / 100;
                    Answer = 100m * (A - B) / A;
                    // Expense share and profit / expenses are different ratios, not net margin.
                    values = new[] { Answer, 100m * B / A, 100m * (A - B) / B }; break;
                case 4: // Core operations exclude a separately disclosed one-off asset sale.
                    A = 80 * scale; B = 100 * scale; C = 50 * scale; Answer = A - B;
                    values = new[] { Answer, (decimal)(A + C - B), A + C }; break;
                case 5: // Current total profit / current diluted share count.
                    C = 40 * scale; B = 20 * random.Next(1, 5) * scale; A = B - 10 * scale;
                    Answer = (decimal)B / C;
                    // Wrong quarter's numerator, or previous profit mistaken for share count.
                    values = new[] { Answer, (decimal)A / C, (decimal)B / A }; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            for (int i = values.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
            Options = values;
            CorrectOption = Array.IndexOf(values, Answer);
        }
    }

    public sealed class EarningsRound
    {
        public const int ReportCount = 6;
        private readonly EarningsReport[] _reports = new EarningsReport[ReportCount];
        private readonly double _deadline;
        private readonly int _maxScore;
        public int Index { get; private set; }
        public int Correct { get; private set; }
        public bool AwaitingNext { get; private set; }
        public bool IsClear => Index >= ReportCount;
        public EarningsReport Current => IsClear ? null : _reports[Index];
        public int Score => (int)Math.Round((double)Correct * _maxScore / ReportCount, MidpointRounding.AwayFromZero);

        public EarningsRound(double start, double duration, int maxScore, int seed)
        {
            if (double.IsNaN(start) || double.IsInfinity(start) || double.IsNaN(duration) ||
                duration <= 0 || duration > 600 || maxScore <= 0) throw new ArgumentOutOfRangeException(nameof(duration));
            _deadline = start + duration;
            _maxScore = maxScore;
            var random = new Random(seed);
            for (int i = 0; i < ReportCount; i++) _reports[i] = new EarningsReport(i, random);
            for (int i = ReportCount - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (_reports[i], _reports[j]) = (_reports[j], _reports[i]);
            }
        }
        public double Remaining(double now) => Math.Max(0, _deadline - now);
        public bool Expired(double now) => now >= _deadline;
        public bool? Answer(int option, double now)
        {
            if (IsClear || AwaitingNext || Expired(now) || option < 0 || option >= Current.Options.Length) return null;
            bool correct = option == Current.CorrectOption;
            if (correct) Correct++;
            AwaitingNext = true;
            return correct;
        }
        public bool Advance(double now)
        {
            if (!AwaitingNext || Expired(now)) return false;
            Index++;
            AwaitingNext = false;
            return true;
        }
    }
}
