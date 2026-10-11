using System;

namespace CTLife.Careers
{
    /// <summary>Score arithmetic shared by every mini-game. The server caps at `maxScore` too; the client never exceeds it.</summary>
    public static class Scoring
    {
        /// <summary>points / pointsMax scaled to [0, maxScore], rounded half away from zero.</summary>
        public static int Scale(double points, double pointsMax, int maxScore)
        {
            if (maxScore <= 0 || pointsMax <= 0 || double.IsNaN(points) || double.IsNaN(pointsMax)) return 0;
            double ratio = points / pointsMax;
            if (double.IsNaN(ratio) || ratio <= 0) return 0;
            if (ratio >= 1) return maxScore;
            return Clamp((int)Math.Round(ratio * maxScore, MidpointRounding.AwayFromZero), maxScore);
        }

        public static int Clamp(int score, int maxScore) => maxScore <= 0 ? 0 : score < 0 ? 0 : score > maxScore ? maxScore : score;

        /// <summary>
        /// Guess-proof points for an n-way choice: each wrong answer cancels 1/(n-1) of a right one, so the expected
        /// value of blind guessing is zero (classic formula scoring). Never negative.
        /// </summary>
        public static double ChanceCorrected(int correct, int wrong, int options)
        {
            if (options <= 1) return Math.Max(0, correct);
            return Math.Max(0, correct - wrong / (double)(options - 1));
        }

        /// <summary>Results-card label for a final score (copy key in CareersStrings).</summary>
        public static string GradeKey(int score, int maxScore)
        {
            if (maxScore <= 0) return "grade.rough";
            double r = score / (double)maxScore;
            return r >= 0.9 ? "grade.clean" : r >= 0.7 ? "grade.solid" : r >= 0.4 ? "grade.mid" : "grade.rough";
        }
    }
}
