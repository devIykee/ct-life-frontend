using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CTLife.UI
{
    /// <summary>Authored narrative ordering, not an engagement prediction or external publishing operation.</summary>
    public sealed class ThreadDraft
    {
        public int Story { get; }
        // Values are semantic fragment IDs (setup, receipt, payoff); UI only shows localized prose.
        public ReadOnlyCollection<int> Pieces { get; }
        internal ThreadDraft(int story, Random random)
        {
            Story = story;
            var pieces = new[] { 0, 1, 2 };
            for (int i = 2; i > 0; i--) { int j = random.Next(i + 1); (pieces[i], pieces[j]) = (pieces[j], pieces[i]); }
            Pieces = Array.AsReadOnly(pieces);
        }
    }

    public sealed class ThreadRound
    {
        public const int ThreadCount = 5, PieceCount = 3, StoryCount = 30;
        private readonly ThreadDraft[] _drafts = new ThreadDraft[ThreadCount];
        private readonly List<int> _compose = new List<int>();
        private readonly List<int[]> _history = new List<int[]>();
        private readonly double _deadline;
        private readonly int _maxScore;
        public int Index { get; private set; }
        public int Correct { get; private set; }
        public bool Filed { get; private set; }
        public bool LastCorrect { get; private set; }
        public bool Clear => Index >= ThreadCount;
        public ThreadDraft Current => Clear ? null : _drafts[Index];
        public IReadOnlyList<int> Compose => _compose.AsReadOnly();
        public bool CanUndo => _history.Count > 0 && !Filed && !Clear;
        public int Score => (int)Math.Round((double)Correct * _maxScore / ThreadCount, MidpointRounding.AwayFromZero);

        public ThreadRound(double start, double duration, int maxScore, int seed)
            : this(start, duration, maxScore, seed, false) { }
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        // Keep the accepted five-story visual oracle explicit and out of normal play.
        public static ThreadRound Review(double start, double duration, int maxScore, int seed)
            => new ThreadRound(start, duration, maxScore, seed, true);
#endif
        private ThreadRound(double start, double duration, int maxScore, int seed, bool reviewDeck)
        {
            if (!Finite(start) || !Finite(duration) || duration <= 0 || duration > 600 || maxScore <= 0)
                throw new ArgumentOutOfRangeException(nameof(duration));
            _deadline = start + duration; _maxScore = maxScore;
            var random = new Random(seed);
            if (reviewDeck)
            {
                for (int i = 0; i < ThreadCount; i++) _drafts[i] = new ThreadDraft(i, random);
                for (int i = ThreadCount - 1; i > 0; i--) { int j = random.Next(i + 1); (_drafts[i], _drafts[j]) = (_drafts[j], _drafts[i]); }
                return;
            }
            // Partial Fisher–Yates: every story can enter a round; no repeats within its five drafts.
            var stories = new int[StoryCount];
            for (int i = 0; i < StoryCount; i++) stories[i] = i;
            for (int i = 0; i < ThreadCount; i++)
            {
                int j = random.Next(i, StoryCount); (stories[i], stories[j]) = (stories[j], stories[i]);
                _drafts[i] = new ThreadDraft(stories[i], random);
            }
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public double Remaining(double now) => Finite(now) ? Math.Max(0, _deadline - now) : 0;
        public bool Expired(double now) => !Finite(now) || now >= _deadline;
        private bool Editable(double now) => !Clear && !Filed && !Expired(now);
        public bool Add(int fragment, double now)
        {
            if (!Editable(now) || fragment < 0 || fragment >= PieceCount || _compose.Contains(fragment) || _compose.Count >= PieceCount) return false;
            Remember(); _compose.Add(fragment); return true;
        }
        public bool Remove(int index, double now)
        {
            if (!Editable(now) || index < 0 || index >= _compose.Count) return false;
            Remember(); _compose.RemoveAt(index); return true;
        }
        public bool Move(int index, int direction, double now)
        {
            int target = index + direction;
            if (!Editable(now) || (direction != -1 && direction != 1) || index < 0 || index >= _compose.Count || target < 0 || target >= _compose.Count) return false;
            Remember(); (_compose[index], _compose[target]) = (_compose[target], _compose[index]); return true;
        }
        public bool Undo(double now)
        {
            if (!Editable(now) || _history.Count == 0) return false;
            var previous = _history[_history.Count - 1]; _history.RemoveAt(_history.Count - 1);
            _compose.Clear(); _compose.AddRange(previous); return true;
        }
        private void Remember()
        {
            // At most 64 retained edits; normal play requires three. Bound repeated touch input memory.
            if (_history.Count >= 64) _history.RemoveAt(0);
            _history.Add(_compose.ToArray());
        }
        public bool? Publish(double now)
        {
            if (!Editable(now) || _compose.Count != PieceCount) return null;
            LastCorrect = _compose[0] == 0 && _compose[1] == 1 && _compose[2] == 2;
            if (LastCorrect) Correct++;
            Filed = true; _history.Clear(); return LastCorrect;
        }
        public bool Next(double now)
        {
            if (Clear || !Filed || Expired(now)) return false;
            Index++; Filed = false; if (!Clear) _compose.Clear(); _history.Clear(); return true;
        }
    }
}
