using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>Common shape for engines made of boards filed one at a time.</summary>
    public abstract class BoardRound<TBoard> : RoundBase where TBoard : class
    {
        protected readonly List<TBoard> BoardList;

        protected BoardRound(IReadOnlyList<TBoard> boards, int maxScore, double duration) : base(maxScore, duration)
        {
            if (boards == null || boards.Count == 0) throw new ArgumentException("No boards", nameof(boards));
            BoardList = new List<TBoard>(boards);
        }

        public IReadOnlyList<TBoard> Boards => BoardList;
        public int BoardIndex { get; protected set; }
        public TBoard Board => BoardIndex < BoardList.Count ? BoardList[BoardIndex] : null;
        public int Filed => BoardIndex;
        public override bool IsComplete => BoardIndex >= BoardList.Count;
    }

    // ------------------------------------------------------------------ Flag hunt

    public sealed class FlagLine
    {
        public string Text;
        public bool Flaw;
        /// <summary>Shown after filing: why this line is (or is not) a problem.</summary>
        public string Why;
        public FlagLine() { }
        public FlagLine(string text, bool flaw = false, string why = null) { Text = text; Flaw = flaw; Why = why; }
    }

    public sealed class FlagBoard
    {
        public string Title, Brief;
        public FlagLine[] Lines;
        /// <summary>Monospace rendering (code).</summary>
        public bool Code;
        public int FlawCount => Lines.Count(l => l.Flaw);
    }

    public readonly struct FlagFiling
    {
        public readonly FlagBoard Board;
        public readonly int Hits, FalseFlags, Misses;
        public readonly bool[] Flagged;
        public FlagFiling(FlagBoard board, int hits, int falseFlags, int misses, bool[] flagged) { Board = board; Hits = hits; FalseFlags = falseFlags; Misses = misses; Flagged = flagged; }
        public int Points => Math.Max(0, Hits - FalseFlags);
    }

    /// <summary>Engine "Flag hunt": each board has 1-3 marked problems among 5-9 lines. Flag them, then file.
    /// A board scores hits minus false flags (never below zero), so flagging everything earns nothing.</summary>
    public sealed class FlagRound : BoardRound<FlagBoard>
    {
        private readonly HashSet<int> _flags = new HashSet<int>();
        private int _points, _hits, _false, _misses;

        public FlagRound(IReadOnlyList<FlagBoard> boards, int maxScore, double duration) : base(boards, maxScore, duration)
        {
            foreach (var b in boards)
                if (b?.Lines == null || b.Lines.Length < 2 || b.FlawCount == 0 || b.FlawCount == b.Lines.Length)
                    throw new ArgumentException("A flag board needs at least one flaw and one clean line");
        }

        public FlagFiling? Last { get; private set; }
        public bool IsFlagged(int line) => _flags.Contains(line);
        public int FlagCount => _flags.Count;

        public bool Toggle(int line)
        {
            if (!Open || Board == null || line < 0 || line >= Board.Lines.Length) return false;
            if (!_flags.Remove(line)) _flags.Add(line);
            return true;
        }

        public FlagFiling? File()
        {
            if (!Open || Board == null) return null;
            var board = Board;
            int hits = 0, falseFlags = 0, misses = 0;
            var flagged = new bool[board.Lines.Length];
            for (int i = 0; i < board.Lines.Length; i++)
            {
                bool f = _flags.Contains(i);
                flagged[i] = f;
                if (f && board.Lines[i].Flaw) hits++;
                else if (f) falseFlags++;
                else if (board.Lines[i].Flaw) misses++;
            }
            var filing = new FlagFiling(board, hits, falseFlags, misses, flagged);
            _points += filing.Points;
            _hits += hits; _false += falseFlags; _misses += misses;
            _flags.Clear();
            BoardIndex++;
            Last = filing;
            return filing;
        }

        protected override double Points => _points;
        protected override double PointsMax => BoardList.Sum(b => b.FlawCount);

        public override IReadOnlyList<ResultLine> Summary => new[]
        {
            new ResultLine("stat.found", _hits), new ResultLine("stat.falseFlags", _false),
            new ResultLine("stat.missed", _misses), new ResultLine("stat.boards", $"{BoardIndex}/{BoardList.Count}"),
        };
    }

    // ------------------------------------------------------------------ Sequence

    public sealed class SequenceBoard
    {
        public string Title, Brief;
        /// <summary>Items in their correct order.</summary>
        public string[] Items;
        /// <summary>Optional per-item explanation, same order as Items.</summary>
        public string[] Why;
    }

    public readonly struct SequenceFiling
    {
        public readonly SequenceBoard Board;
        public readonly int[] Placed;
        public readonly int InPlace;
        public SequenceFiling(SequenceBoard board, int[] placed, int inPlace) { Board = board; Placed = placed; InPlace = inPlace; }
        public bool Perfect => InPlace == Board.Items.Length;
    }

    /// <summary>Engine "Sequence": tap the items in order to build the line-up, tap a placed item to take it back, then
    /// file. One point per item in its exact position.</summary>
    public sealed class SequenceRound : BoardRound<SequenceBoard>
    {
        private readonly List<int> _placed = new List<int>();
        private readonly List<int[]> _shown = new List<int[]>();
        private int _points, _perfect;

        public SequenceRound(IReadOnlyList<SequenceBoard> boards, int seed, int maxScore, double duration) : base(boards, maxScore, duration)
        {
            var rng = new DetRng(seed);
            foreach (var b in boards)
            {
                if (b?.Items == null || b.Items.Length < 3 || b.Items.Length > 7) throw new ArgumentException("3-7 items per sequence");
                _shown.Add(rng.Derangement(b.Items.Length));
            }
        }

        /// <summary>Display order of the current board's items (indices into Items); never the solved order.</summary>
        public IReadOnlyList<int> Shown => BoardIndex < _shown.Count ? _shown[BoardIndex] : Array.Empty<int>();

        public IReadOnlyList<int> Placed => _placed;
        public bool IsPlaced(int item) => _placed.Contains(item);
        public bool Ready => Board != null && _placed.Count == Board.Items.Length;
        public SequenceFiling? Last { get; private set; }

        public bool Place(int item)
        {
            if (!Open || Board == null || item < 0 || item >= Board.Items.Length || _placed.Contains(item)) return false;
            _placed.Add(item);
            return true;
        }

        /// <summary>Remove the item at a line-up position (later items shift up).</summary>
        public bool Unplace(int position)
        {
            if (!Open || position < 0 || position >= _placed.Count) return false;
            _placed.RemoveAt(position);
            return true;
        }

        public void Clear() { if (Open) _placed.Clear(); }

        public SequenceFiling? File()
        {
            if (!Open || !Ready) return null;
            int inPlace = 0;
            for (int i = 0; i < _placed.Count; i++) if (_placed[i] == i) inPlace++;
            var filing = new SequenceFiling(Board, _placed.ToArray(), inPlace);
            _points += inPlace;
            if (filing.Perfect) _perfect++;
            _placed.Clear();
            BoardIndex++;
            Last = filing;
            return filing;
        }

        protected override double Points => _points;
        protected override double PointsMax => BoardList.Sum(b => b.Items.Length);

        public override IReadOnlyList<ResultLine> Summary => new[]
        {
            new ResultLine("stat.inPlace", _points), new ResultLine("stat.perfectBoards", _perfect),
            new ResultLine("stat.boards", $"{BoardIndex}/{BoardList.Count}"),
        };
    }

    // ------------------------------------------------------------------ Match

    public sealed class MatchBoard
    {
        public string Title, Brief;
        public string[] Left, Right;
        /// <summary>Solution[i] = index into Right for Left[i]. Must be a permutation.</summary>
        public int[] Solution;
        /// <summary>Optional explanation per left item.</summary>
        public string[] Why;
    }

    public readonly struct MatchFiling
    {
        public readonly MatchBoard Board;
        public readonly int[] Pairs;
        public readonly int Correct;
        public MatchFiling(MatchBoard board, int[] pairs, int correct) { Board = board; Pairs = pairs; Correct = correct; }
    }

    /// <summary>Engine "Match": select a left card, then the right card it belongs with. Re-pairing moves a card.
    /// File when every left card is paired. One point per correct pair.</summary>
    public sealed class MatchRound : BoardRound<MatchBoard>
    {
        private int[] _pairs;
        private readonly List<int[]> _rightShown = new List<int[]>();
        private int _points, _perfect;

        /// <summary>Display order of the current board's right column (indices into Right).</summary>
        public IReadOnlyList<int> RightShown => BoardIndex < _rightShown.Count ? _rightShown[BoardIndex] : Array.Empty<int>();

        public MatchRound(IReadOnlyList<MatchBoard> boards, int seed, int maxScore, double duration) : base(boards, maxScore, duration)
        {
            var rng = new DetRng(seed);
            foreach (var b in boards)
            {
                if (b?.Left == null || b.Right == null || b.Left.Length < 2 || b.Left.Length != b.Right.Length || b.Solution == null ||
                    b.Solution.Length != b.Left.Length || b.Solution.Distinct().Count() != b.Solution.Length ||
                    b.Solution.Any(s => s < 0 || s >= b.Right.Length))
                    throw new ArgumentException("A match board needs equal sides and a one-to-one solution");
                // Shown row i holds the solution of a different left card, so the column never starts aligned.
                var perm = rng.Derangement(b.Right.Length);
                _rightShown.Add(perm.Select(k => b.Solution[k]).ToArray());
            }
            ResetPairs();
        }

        public int SelectedLeft { get; private set; } = -1;
        public int PairOf(int left) => _pairs != null && left >= 0 && left < _pairs.Length ? _pairs[left] : -1;
        public int LeftFor(int right) => _pairs == null ? -1 : Array.IndexOf(_pairs, right);
        public bool Ready => _pairs != null && _pairs.All(p => p >= 0);
        public MatchFiling? Last { get; private set; }

        private void ResetPairs()
        {
            SelectedLeft = -1;
            _pairs = Board == null ? null : Enumerable.Repeat(-1, Board.Left.Length).ToArray();
        }

        public bool SelectLeft(int left)
        {
            if (!Open || Board == null || left < 0 || left >= Board.Left.Length) return false;
            SelectedLeft = left;
            return true;
        }

        /// <summary>Pair the selected left card with a right card (by Right index). A right card already taken moves.</summary>
        public bool PairWith(int right)
        {
            if (!Open || Board == null || SelectedLeft < 0 || right < 0 || right >= Board.Right.Length) return false;
            int other = LeftFor(right);
            if (other >= 0) _pairs[other] = -1;
            _pairs[SelectedLeft] = right;
            // Move selection to the next unpaired card to keep thumbs moving.
            int next = Array.IndexOf(_pairs, -1);
            SelectedLeft = next;
            return true;
        }

        public bool Unpair(int left)
        {
            if (!Open || _pairs == null || left < 0 || left >= _pairs.Length || _pairs[left] < 0) return false;
            _pairs[left] = -1;
            SelectedLeft = left;
            return true;
        }

        public MatchFiling? File()
        {
            if (!Open || !Ready) return null;
            int correct = 0;
            for (int i = 0; i < _pairs.Length; i++) if (_pairs[i] == Board.Solution[i]) correct++;
            var filing = new MatchFiling(Board, (int[])_pairs.Clone(), correct);
            _points += correct;
            if (correct == _pairs.Length) _perfect++;
            BoardIndex++;
            ResetPairs();
            Last = filing;
            return filing;
        }

        protected override double Points => _points;
        protected override double PointsMax => BoardList.Sum(b => b.Left.Length);

        public override IReadOnlyList<ResultLine> Summary => new[]
        {
            new ResultLine("stat.pairs", _points), new ResultLine("stat.perfectBoards", _perfect),
            new ResultLine("stat.boards", $"{BoardIndex}/{BoardList.Count}"),
        };
    }

    // ------------------------------------------------------------------ Pick the best

    public sealed class PickOption
    {
        public string Label;
        public string[] Facts = Array.Empty<string>();
        /// <summary>Shown after the pick (the computed value behind the rule).</summary>
        public string Reveal;
    }

    public sealed class PickBoard
    {
        public string Title, Brief;
        /// <summary>Optional context line (e.g. where the traced funds are now).</summary>
        public string Context;
        public PickOption[] Options;
        public int Correct;
        public string Why;
    }

    public readonly struct PickResult
    {
        public readonly PickBoard Board;
        public readonly int Chosen;
        public bool Correct => Chosen == Board.Correct;
        public PickResult(PickBoard board, int chosen) { Board = board; Chosen = chosen; }
    }

    /// <summary>Engine "Pick": each board states a rule and shows 3-4 options; one is right by that rule (computed from
    /// the shown numbers, never by taste). One pick per board.</summary>
    public sealed class PickRound : BoardRound<PickBoard>
    {
        private int _correct, _streak, _best;

        public PickRound(IReadOnlyList<PickBoard> boards, int maxScore, double duration) : base(boards, maxScore, duration)
        {
            foreach (var b in boards)
                if (b?.Options == null || b.Options.Length < 2 || b.Options.Length > 5 || b.Correct < 0 || b.Correct >= b.Options.Length)
                    throw new ArgumentException("A pick board needs 2-5 options and a valid answer");
        }

        public PickResult? Last { get; private set; }
        public int CorrectCount => _correct;

        public PickResult? Pick(int option)
        {
            if (!Open || Board == null || option < 0 || option >= Board.Options.Length) return null;
            var result = new PickResult(Board, option);
            if (result.Correct) { _correct++; _streak++; _best = Math.Max(_best, _streak); } else _streak = 0;
            BoardIndex++;
            Last = result;
            return result;
        }

        protected override double Points => _correct;
        protected override double PointsMax => BoardList.Count;

        public override IReadOnlyList<ResultLine> Summary => new[]
        {
            new ResultLine("stat.correct", _correct), new ResultLine("stat.wrong", BoardIndex - _correct),
            new ResultLine("stat.boards", $"{BoardIndex}/{BoardList.Count}"), new ResultLine("stat.bestStreak", _best),
        };
    }

    // ------------------------------------------------------------------ Combo

    public sealed class ComboOption
    {
        public string Label;
        public int Value;
        /// <summary>Choosing this option fails the board (e.g. cutting a must-keep line).</summary>
        public bool Forbidden;
        public string Why;
        public ComboOption() { }
        public ComboOption(string label, int value, bool forbidden = false, string why = null) { Label = label; Value = value; Forbidden = forbidden; Why = why; }
    }

    public sealed class ComboSlot
    {
        public string Name;
        public ComboOption[] Options;
    }

    public sealed class ComboBoard
    {
        public string Title, Brief, Unit;
        public ComboSlot[] Slots;
        public int Min, Max;

        public bool Passes(int[] choice, out int total, out string failWhy)
        {
            total = 0;
            failWhy = null;
            if (choice == null || choice.Length != Slots.Length) { failWhy = "incomplete"; return false; }
            for (int i = 0; i < Slots.Length; i++)
            {
                if (choice[i] < 0 || choice[i] >= Slots[i].Options.Length) { failWhy = "incomplete"; return false; }
                var o = Slots[i].Options[choice[i]];
                total += o.Value;
                if (o.Forbidden && failWhy == null) failWhy = o.Why ?? "forbidden";
            }
            if (failWhy != null) return false;
            return total >= Min && total <= Max;
        }

        /// <summary>Number of passing combinations (tests assert every board is solvable).</summary>
        public int CountSolutions()
        {
            int count = 0;
            var choice = new int[Slots.Length];
            void Walk(int slot)
            {
                if (slot == Slots.Length) { if (Passes(choice, out _, out _)) count++; return; }
                for (int o = 0; o < Slots[slot].Options.Length; o++) { choice[slot] = o; Walk(slot + 1); }
            }
            Walk(0);
            return count;
        }
    }

    public readonly struct ComboFiling
    {
        public readonly ComboBoard Board;
        public readonly int[] Choice;
        public readonly int Total;
        public readonly bool Passed;
        public readonly string FailWhy;
        public ComboFiling(ComboBoard board, int[] choice, int total, bool passed, string failWhy) { Board = board; Choice = choice; Total = total; Passed = passed; FailWhy = failWhy; }
    }

    /// <summary>Engine "Combo": choose one option per slot so the hidden total lands in the target band without using a
    /// forbidden option. The running total is not shown: the skill is the arithmetic and the judgement. One point per
    /// passing board.</summary>
    public sealed class ComboRound : BoardRound<ComboBoard>
    {
        private int[] _choice;
        private int _passed;

        public ComboRound(IReadOnlyList<ComboBoard> boards, int maxScore, double duration) : base(boards, maxScore, duration)
        {
            foreach (var b in boards)
                if (b?.Slots == null || b.Slots.Length < 2 || b.Slots.Any(s => s.Options == null || s.Options.Length < 2) || b.Min > b.Max)
                    throw new ArgumentException("A combo board needs 2+ slots with 2+ options and a valid band");
            Reset();
        }

        public int ChoiceOf(int slot) => _choice != null && slot >= 0 && slot < _choice.Length ? _choice[slot] : -1;
        public bool Ready => _choice != null && _choice.All(c => c >= 0);
        public ComboFiling? Last { get; private set; }

        private void Reset() => _choice = Board == null ? null : Enumerable.Repeat(-1, Board.Slots.Length).ToArray();

        public bool Choose(int slot, int option)
        {
            if (!Open || Board == null || slot < 0 || slot >= Board.Slots.Length || option < 0 || option >= Board.Slots[slot].Options.Length) return false;
            _choice[slot] = option;
            return true;
        }

        public ComboFiling? File()
        {
            if (!Open || !Ready) return null;
            bool passed = Board.Passes(_choice, out int total, out string why);
            var filing = new ComboFiling(Board, (int[])_choice.Clone(), total, passed, why);
            if (passed) _passed++;
            BoardIndex++;
            Reset();
            Last = filing;
            return filing;
        }

        protected override double Points => _passed;
        protected override double PointsMax => BoardList.Count;

        public override IReadOnlyList<ResultLine> Summary => new[]
        {
            new ResultLine("stat.passed", _passed), new ResultLine("stat.failed", BoardIndex - _passed),
            new ResultLine("stat.boards", $"{BoardIndex}/{BoardList.Count}"),
        };
    }
}
