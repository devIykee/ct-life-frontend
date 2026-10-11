using System;
using System.Collections.Generic;

namespace CTLife.UI
{
    public sealed class ReceiptEntry
    {
        public int Amount { get; }
        public int Day { get; }
        public string Reference { get; }
        public int Item { get; }
        internal ReceiptEntry(int amount, int day, string reference, int item)
        { Amount = amount; Day = day; Reference = reference; Item = item; }
        public bool Matches(ReceiptEntry other) => other != null && Amount == other.Amount && Day == other.Day && Reference == other.Reference;
    }

    public sealed class ReceiptMatchBoard
    {
        private readonly ReceiptEntry[] _rows, _receipts;
        public int Story { get; }
        internal ReceiptMatchBoard(int story, ReceiptEntry[] entries, Random random)
        {
            Story = story; _rows = (ReceiptEntry[])entries.Clone(); _receipts = (ReceiptEntry[])entries.Clone();
            Shuffle(_rows, random); Shuffle(_receipts, random);
        }
        internal static void Shuffle<T>(T[] values, Random random)
        { for (int i = values.Length - 1; i > 0; i--) { int j = random.Next(i + 1); (values[i], values[j]) = (values[j], values[i]); } }
        public ReceiptEntry Row(int row) => _rows[row];
        public ReceiptEntry Receipt(int receipt) => _receipts[receipt];
        public bool Matches(int row, int receipt) => row >= 0 && row < 4 && receipt >= 0 && receipt < 4 && Row(row).Matches(Receipt(receipt));
        public int CorrectReceipt(int row)
        { for (int receipt = 0; receipt < 4; receipt++) if (Matches(row, receipt)) return receipt; throw new InvalidOperationException("Authored entry has no receipt."); }
    }

    public sealed class ReceiptMatchResult
    {
        public int Score => CorrectPairs * 5;
        public int FiledBoards { get; }
        public int CorrectPairs { get; }
        public int WrongPairs { get; }
        public int UnpairedRows { get; }
        internal ReceiptMatchResult(int filed, int correct, int wrong)
        { FiledBoards = filed; CorrectPairs = correct; WrongPairs = wrong; UnpairedRows = 20 - correct - wrong; }
    }

    /// <summary>Authored local receipts, not an account ledger, market or money movement.</summary>
    public sealed class ReceiptMatchRound
    {
        public const string ProposedId = "junior_analyst_receipt_match";
        public const int Duration = 50, Maximum = 100, BoardCount = 5, PairCount = 4;
        private readonly ReceiptMatchBoard[] _boards = new ReceiptMatchBoard[BoardCount];
        private readonly IReadOnlyList<ReceiptMatchBoard> _readOnly;
        private readonly int[] _pairs = { -1, -1, -1, -1 };
        private double _lastNow;
        private int _filed, _correct, _wrong;
        public IReadOnlyList<ReceiptMatchBoard> Boards => _readOnly;
        public int Index { get; private set; }
        public ReceiptMatchBoard Current => _boards[Index];
        public int Selected { get; private set; } = -1;
        public int Score => _correct * 5;
        public int FiledBoards => _filed;
        public int CorrectPairs => _correct;
        public bool Filed { get; private set; }
        public int BoardCorrect { get; private set; }
        public bool AllFiled => _filed == BoardCount;
        public bool Paused { get; private set; }
        public bool Cancelled { get; private set; }
        public double Elapsed { get; private set; }
        public double Remaining => Math.Max(0, Duration - Elapsed);
        public bool Finished => !Cancelled && (AllFiled || Elapsed >= Duration);
        public int PairedCount { get { int count = 0; foreach (int pair in _pairs) if (pair >= 0) count++; return count; } }
        public ReceiptMatchResult Result => Finished ? new ReceiptMatchResult(_filed, _correct, _wrong)
            : throw new InvalidOperationException("An unfinished or cancelled practice has no result.");

        public ReceiptMatchRound(double now, int seed)
        {
            Validate(now); _lastNow = now; _readOnly = Array.AsReadOnly(_boards);
            var random = new Random(seed); var order = new[] { 0, 1, 2, 3, 4 }; ReceiptMatchBoard.Shuffle(order, random);
            for (int i = 0; i < BoardCount; i++) _boards[i] = new ReceiptMatchBoard(order[i], Entries(order[i]), random);
        }
        private static ReceiptEntry[] Entries(int story)
        {
            // Deliberate collisions: amount alone is never enough. Board1 also requires both reference and date;
            // board2 shares a reference; board3 shares a date. Every complete tuple remains unique.
            int[][] amounts = { new[]{120,120,80,80}, new[]{180,180,180,180}, new[]{420,420,250,250}, new[]{64,64,96,96}, new[]{250,250,400,400} };
            int[][] days = { new[]{3,4,3,4}, new[]{5,6,5,6}, new[]{7,8,7,8}, new[]{9,9,9,9}, new[]{10,11,11,10} };
            string[][] refs = { new[]{"CF-11","CF-11","CB-14","CB-14"}, new[]{"ST-21","ST-21","ST-28","ST-28"},
                new[]{"DM-31","DM-31","DM-31","DM-31"}, new[]{"BD-41","BD-48","BD-41","BD-48"}, new[]{"CL-51","CL-58","CL-51","CL-58"} };
            var entries = new ReceiptEntry[4];
            for (int i = 0; i < 4; i++) entries[i] = new ReceiptEntry(amounts[story][i], days[story][i], refs[story][i], i);
            return entries;
        }
        private static void Validate(double now)
        { if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now)); }
        public void Step(double now)
        {
            Validate(now); if (now < _lastNow) throw new ArgumentOutOfRangeException(nameof(now), "Clock moved backwards.");
            double delta = now - _lastNow; _lastNow = now;
            if (Paused || Cancelled || Finished) return;
            Elapsed = Math.Min(Duration, Elapsed + delta);
        }
        private bool Accept(int board, double now)
        { Step(now); return board == Index && !Cancelled && !Paused && !Finished; }
        public bool Select(int board, int receipt, double now)
        {
            if (!Accept(board, now) || Filed || receipt < 0 || receipt >= PairCount) return false;
            Selected = Selected == receipt ? -1 : receipt; return true;
        }
        public int Pair(int row) => _pairs[row];
        public int RowFor(int receipt)
        { for (int row = 0; row < PairCount; row++) if (_pairs[row] == receipt) return row; return -1; }
        public bool Place(int board, int row, double now)
        {
            if (!Accept(board, now) || Filed || row < 0 || row >= PairCount) return false;
            if (Selected < 0)
            { if (_pairs[row] < 0) return false; _pairs[row] = -1; return true; }
            int oldRow = RowFor(Selected), displaced = _pairs[row];
            if (oldRow == row) { Selected = -1; return true; }
            _pairs[row] = Selected;
            if (oldRow >= 0) _pairs[oldRow] = displaced; // An occupied move swaps, never duplicates or silently loses a pair.
            Selected = -1; return true;
        }
        public bool Clear(int board, double now)
        {
            if (!Accept(board, now) || Filed || (PairedCount == 0 && Selected < 0)) return false;
            for (int row = 0; row < PairCount; row++) _pairs[row] = -1; Selected = -1; return true;
        }
        public bool File(int board, double now)
        {
            if (!Accept(board, now) || Filed) return false;
            Filed = true; _filed++; Selected = -1; BoardCorrect = 0;
            for (int row = 0; row < PairCount; row++)
            { if (Current.Matches(row, _pairs[row])) { _correct++; BoardCorrect++; } else if (_pairs[row] >= 0) _wrong++; }
            return true;
        }
        public bool Next(int board, double now)
        {
            if (!Accept(board, now) || !Filed || Index >= BoardCount - 1) return false;
            Index++; Filed = false; BoardCorrect = 0; Selected = -1;
            for (int row = 0; row < PairCount; row++) _pairs[row] = -1;
            return true;
        }
        public bool Pause(double now)
        { Step(now); if (Cancelled || Finished || Paused) return false; Paused = true; return true; }
        public bool Resume(double now)
        { Step(now); if (Cancelled || Finished || !Paused) return false; Paused = false; return true; }
        public bool Cancel() { if (Cancelled) return false; Cancelled = true; return true; }
    }
}
