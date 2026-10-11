using System;
using System.Collections.Generic;

namespace CTLife.Careers
{
    /// <summary>Optional colour sample shown on a card (designer games). 0xRRGGBB.</summary>
    public sealed class Swatch
    {
        public readonly uint Foreground, Background;
        public readonly string Sample;
        public Swatch(uint foreground, uint background, string sample) { Foreground = foreground; Background = background; Sample = sample; }
    }

    public sealed class SortCard
    {
        public string Title, Body;
        public string[] Chips = Array.Empty<string>();
        /// <summary>Correct bin index (ignored when the round has an ISortJudge).</summary>
        public int Bin;
        /// <summary>Why the correct bin is correct; shown after every answer.</summary>
        public string Why;
        public Swatch Swatch;
        /// <summary>Stateful games put structured data here for their judge (e.g. a spend amount).</summary>
        public long Amount;
        public string Kind;
    }

    /// <summary>For stateful sorting (the correct bin depends on earlier decisions, e.g. a remaining budget).</summary>
    public interface ISortJudge
    {
        int CorrectBin(SortCard card);
        string Why(SortCard card, int correctBin);
        void Record(SortCard card, int chosenBin);
        /// <summary>One status line shown above the card (e.g. "Budget left $12.40"). Null for none.</summary>
        string Status { get; }
    }

    public readonly struct SortAnswer
    {
        public readonly SortCard Card;
        public readonly int Chosen, CorrectBin;
        public readonly string Why;
        public bool Correct => Chosen == CorrectBin;
        public SortAnswer(SortCard card, int chosen, int correctBin, string why) { Card = card; Chosen = chosen; CorrectBin = correctBin; Why = why; }
    }

    /// <summary>
    /// Engine "Sort": a finite deck, one card at a time, pick a bin. Formula scoring (wrong answers cancel 1/(bins-1) of a
    /// right one) so blind tapping is worth zero. Cards left when the clock ends score nothing.
    /// </summary>
    public sealed class SortRound : RoundBase
    {
        private readonly List<SortCard> _cards;
        private readonly ISortJudge _judge;
        private int _index;

        public SortRound(IReadOnlyList<string> bins, IReadOnlyList<SortCard> cards, int maxScore, double duration, ISortJudge judge = null)
            : base(maxScore, duration)
        {
            if (bins == null || bins.Count < 2 || bins.Count > 4) throw new ArgumentException("2-4 bins", nameof(bins));
            if (cards == null || cards.Count == 0) throw new ArgumentException("Empty deck", nameof(cards));
            foreach (var c in cards)
                if (c == null || (judge == null && (c.Bin < 0 || c.Bin >= bins.Count))) throw new ArgumentException("Card without a valid bin");
            Bins = bins;
            _cards = new List<SortCard>(cards);
            _judge = judge;
        }

        public IReadOnlyList<string> Bins { get; }
        public IReadOnlyList<SortCard> Cards => _cards;
        public int Index => _index;
        public SortCard Current => _index < _cards.Count ? _cards[_index] : null;
        public int Correct { get; private set; }
        public int Wrong { get; private set; }
        public int Streak { get; private set; }
        public int BestStreak { get; private set; }
        public SortAnswer? Last { get; private set; }
        public string Status => _judge?.Status;
        public override bool IsComplete => _index >= _cards.Count;

        public SortAnswer? Answer(int bin)
        {
            if (!Open || bin < 0 || bin >= Bins.Count) return null;
            var card = _cards[_index];
            int correct = _judge?.CorrectBin(card) ?? card.Bin;
            string why = _judge != null ? _judge.Why(card, correct) : card.Why;
            _judge?.Record(card, bin);
            if (bin == correct) { Correct++; Streak++; BestStreak = Math.Max(BestStreak, Streak); }
            else { Wrong++; Streak = 0; }
            _index++;
            var answer = new SortAnswer(card, bin, correct, why);
            Last = answer;
            return answer;
        }

        protected override double Points => Scoring.ChanceCorrected(Correct, Wrong, Bins.Count);
        protected override double PointsMax => _cards.Count;

        public override IReadOnlyList<ResultLine> Summary => new[]
        {
            new ResultLine("stat.correct", Correct), new ResultLine("stat.wrong", Wrong),
            new ResultLine("stat.unanswered", _cards.Count - _index), new ResultLine("stat.bestStreak", BestStreak),
        };
    }
}
