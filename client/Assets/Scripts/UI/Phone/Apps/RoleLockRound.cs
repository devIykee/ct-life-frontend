using System;
using System.Collections.Generic;

namespace CTLife.UI
{
    /// <summary>One authored least-privilege board, permuted only in presentation.</summary>
    public sealed class RoleLockBoard
    {
        private readonly int[] _roles, _permissions;
        private readonly int _required;
        public int Story { get; }
        internal RoleLockBoard(int story, int required, Random random)
        {
            Story = story; _required = required;
            _roles = Shuffle(random); _permissions = Shuffle(random);
        }
        private static int[] Shuffle(Random random)
        {
            var values = new[] { 0, 1, 2 };
            for (int i = 2; i > 0; i--) { int j = random.Next(i + 1); (values[i], values[j]) = (values[j], values[i]); }
            return values;
        }
        public int Role(int row) => _roles[row];
        public int Permission(int column) => _permissions[column];
        public bool Requires(int row, int column) => (_required & (1 << (_roles[row] * 3 + _permissions[column]))) != 0;
        public int RequiredMask
        {
            get
            {
                int result = 0;
                for (int row = 0; row < 3; row++) for (int column = 0; column < 3; column++)
                    if (Requires(row, column)) result |= 1 << (row * 3 + column);
                return result;
            }
        }
    }

    public sealed class RoleLockResult
    {
        public int Score { get; }
        public int LockedBoards { get; }
        public int CorrectBoards { get; }
        public int ExtraGrants { get; }
        public int MissingGrants { get; }
        internal RoleLockResult(int locked, int correct, int extra, int missing)
        { LockedBoards = locked; CorrectBoards = correct; ExtraGrants = extra; MissingGrants = missing; Score = correct * 20; }
    }

    /// <summary>Five fictional access boards. No admin service, imported assignment, authority or payout.</summary>
    public sealed class RoleLockRound
    {
        public const string ProposedId = "discord_mod_role_lock";
        public const int Duration = 50, Maximum = 100, BoardCount = 5;
        // Canonical role rows / permission columns; independently checked against authored task copy.
        private static readonly int[] Answers = { 0b000_011_001, 0b000_010_101, 0b100_011_001, 0b100_010_101, 0b100_001_011 };
        private readonly RoleLockBoard[] _boards = new RoleLockBoard[BoardCount];
        private readonly IReadOnlyList<RoleLockBoard> _readOnly;
        private double _lastNow;
        private int _selection, _correct, _locked, _extra, _missing;
        public IReadOnlyList<RoleLockBoard> Boards => _readOnly;
        public int Index { get; private set; }
        public RoleLockBoard Current => _boards[Index];
        public int Selection => _selection;
        public int Score => _correct * 20;
        public int LockedBoards => _locked;
        public int CorrectBoards => _correct;
        public bool Locked { get; private set; }
        public bool Correct => Locked && _selection == Current.RequiredMask;
        public bool AllLocked => _locked == BoardCount;
        public bool Paused { get; private set; }
        public bool Cancelled { get; private set; }
        public double Elapsed { get; private set; }
        public double Remaining => Math.Max(0, Duration - Elapsed);
        public bool Finished => !Cancelled && (AllLocked || Elapsed >= Duration);
        public RoleLockResult Result => Finished ? new RoleLockResult(_locked, _correct, _extra, _missing)
            : throw new InvalidOperationException("An unfinished or cancelled practice has no result.");

        public RoleLockRound(double now, int seed)
        {
            Validate(now); _lastNow = now; _readOnly = Array.AsReadOnly(_boards);
            var random = new Random(seed); var order = new[] { 0, 1, 2, 3, 4 };
            for (int i = 4; i > 0; i--) { int j = random.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            for (int i = 0; i < BoardCount; i++) _boards[i] = new RoleLockBoard(order[i], Answers[order[i]], random);
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
        public bool Toggle(int board, int row, int column, double now)
        {
            if (!Accept(board, now) || Locked || row < 0 || row > 2 || column < 0 || column > 2) return false;
            _selection ^= 1 << (row * 3 + column); return true;
        }
        public bool Clear(int board, double now)
        { if (!Accept(board, now) || Locked || _selection == 0) return false; _selection = 0; return true; }
        public bool Lock(int board, double now)
        {
            if (!Accept(board, now) || Locked) return false;
            Locked = true; _locked++;
            int required = Current.RequiredMask;
            if (_selection == required) _correct++;
            _extra += Bits(_selection & ~required); _missing += Bits(required & ~_selection);
            return true;
        }
        public bool Next(int board, double now)
        {
            if (!Accept(board, now) || !Locked || Index >= BoardCount - 1) return false;
            Index++; Locked = false; _selection = 0; return true;
        }
        public bool Pause(double now)
        { Step(now); if (Cancelled || Finished || Paused) return false; Paused = true; return true; }
        public bool Resume(double now)
        { Step(now); if (Cancelled || Finished || !Paused) return false; Paused = false; return true; }
        public bool Cancel()
        { if (Cancelled) return false; Cancelled = true; return true; }
        public bool Selected(int row, int column) => (_selection & (1 << (row * 3 + column))) != 0;
        public static int Bits(int value)
        { int count = 0; for (int i = 0; i < 9; i++) if ((value & (1 << i)) != 0) count++; return count; }
    }
}
