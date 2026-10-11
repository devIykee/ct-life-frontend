using System;
using System.Collections.Generic;

namespace CTLife.UI
{
    public sealed class RunwayLine
    {
        private readonly int[] _costs, _capacity;
        public int Source { get; }
        public int Metric { get; }
        internal RunwayLine(int source, int metric, int[] costs, int[] capacity)
        { Source = source; Metric = metric; _costs = (int[])costs.Clone(); _capacity = (int[])capacity.Clone(); }
        public int Cost(int preset) => _costs[preset];
        public int Capacity(int preset) => _capacity[preset];
    }
    public sealed class RunwayBoard
    {
        private readonly RunwayLine[] _lines;
        public int Story { get; }
        public int Ceiling { get; }
        public int MinimumA { get; }
        public int MinimumB { get; }
        public RunwayLine Line(int index) => _lines[index];
        internal RunwayBoard(int story, Random random)
        {
            Story = story;
            int[][] costs;
            switch (story)
            {
                case 0: Ceiling = 2000; MinimumA = 4; MinimumB = 8;
                    costs = new[] { new[] {900,600,300}, new[] {800,500,200}, new[] {500,350,200}, new[] {400,250,100}, new[] {300,200,100} }; break;
                case 1: Ceiling = 1750; MinimumA = 4; MinimumB = 8;
                    costs = new[] { new[] {900,650,400}, new[] {600,400,200}, new[] {700,400,100}, new[] {500,250,0}, new[] {400,300,200} }; break;
                case 2: Ceiling = 1850; MinimumA = 4; MinimumB = 8;
                    costs = new[] { new[] {800,400,200}, new[] {900,500,200}, new[] {600,350,100}, new[] {500,300,100}, new[] {300,150,0} }; break;
                case 3: Ceiling = 2200; MinimumA = 4; MinimumB = 8;
                    costs = new[] { new[] {1000,750,500}, new[] {700,500,300}, new[] {900,500,100}, new[] {600,500,400}, new[] {400,100,0} }; break;
                default: throw new ArgumentOutOfRangeException(nameof(story));
            }
            _lines = new RunwayLine[5];
            for (int i = 0; i < 5; i++) _lines[i] = new RunwayLine(i, i < 2 ? i : -1, costs[i], i == 0 ? new[] {6,4,2} : i == 1 ? new[] {12,8,4} : new[] {0,0,0});
            for (int i = 4; i > 0; i--) { int j = random.Next(i + 1); (_lines[i], _lines[j]) = (_lines[j], _lines[i]); }
        }
    }
    public sealed class RunwayPlanResult
    {
        public int Score { get; }
        public int Filed { get; }
        public int Valid { get; }
        internal RunwayPlanResult(int filed, int valid) { Filed = filed; Valid = valid; Score = valid * 25; }
    }
    /// <summary>Fictional operating-budget puzzle. No account balance, payment or financial forecast.</summary>
    public sealed class RunwayPlanRound
    {
        public const string ProposedId = "junior_analyst_runway_plan";
        public const int Duration = 60, Maximum = 100, BoardCount = 4, EditLimit = 3;
        private readonly RunwayBoard[] _boards = new RunwayBoard[BoardCount];
        private readonly int[] _presets = new int[5];
        private readonly List<(int line, int before)> _edits = new List<(int, int)>();
        private double _lastNow;
        public double Elapsed { get; private set; }
        public double Remaining => Math.Max(0, Duration - Elapsed);
        public bool Paused { get; private set; }
        public bool Cancelled { get; private set; }
        public bool AllFiled => FiledCount == BoardCount;
        public bool Finished => !Cancelled && (AllFiled || Elapsed >= Duration);
        public int Index { get; private set; }
        public RunwayBoard Current => _boards[Index];
        public bool Filed { get; private set; }
        public bool BoardValid { get; private set; }
        public int FiledCount { get; private set; }
        public int ValidCount { get; private set; }
        public int Edits => _edits.Count;
        public int Score => ValidCount * 25;
        public int Preset(int line) => _presets[line];
        public int Cost { get { int value = 0; for (int i = 0; i < 5; i++) value += Current.Line(i).Cost(_presets[i]); return value; } }
        public int Capacity(int metric) { int value = 0; for (int i = 0; i < 5; i++) if (Current.Line(i).Metric == metric) value += Current.Line(i).Capacity(_presets[i]); return value; }
        public bool MeetsBudget => Cost <= Current.Ceiling;
        public bool MeetsA => Capacity(0) >= Current.MinimumA;
        public bool MeetsB => Capacity(1) >= Current.MinimumB;
        public bool Valid => MeetsBudget && MeetsA && MeetsB;
        public RunwayPlanResult Result => Finished ? new RunwayPlanResult(FiledCount, ValidCount) : throw new InvalidOperationException("Unfinished or cancelled planning has no result.");
        public RunwayPlanRound(double now, int seed)
        {
            Validate(now); _lastNow = now; var random = new Random(seed); var order = new[] {0,1,2,3};
            for (int i = 3; i > 0; i--) { int j = random.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            for (int i = 0; i < BoardCount; i++) _boards[i] = new RunwayBoard(order[i], random);
        }
        private static void Validate(double now) { if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now)); }
        public void Step(double now)
        {
            Validate(now); if (now < _lastNow) throw new ArgumentOutOfRangeException(nameof(now), "Clock moved backwards.");
            double delta = now - _lastNow; _lastNow = now; if (Paused || Cancelled || Finished) return;
            Elapsed = Math.Min(Duration, Elapsed + delta);
        }
        private bool Editable(int expected, double now)
        { Step(now); return !Paused && !Cancelled && !Finished && !Filed && expected == Index; }
        public bool Select(int expected, int line, int preset, double now)
        {
            if (!Editable(expected, now) || line < 0 || line >= 5 || preset < 0 || preset > 2 || Edits >= EditLimit || _presets[line] == preset) return false;
            _edits.Add((line, _presets[line])); _presets[line] = preset; return true;
        }
        public bool Undo(int expected, double now)
        {
            if (!Editable(expected, now) || _edits.Count == 0) return false;
            var edit = _edits[_edits.Count - 1]; _edits.RemoveAt(_edits.Count - 1); _presets[edit.line] = edit.before; return true;
        }
        public bool File(int expected, double now)
        {
            if (!Editable(expected, now)) return false;
            BoardValid = Valid; Filed = true; FiledCount++; if (BoardValid) ValidCount++; return true;
        }
        public bool Next(int expected, double now)
        {
            Step(now); if (Paused || Cancelled || Finished || !Filed || expected != Index || Index >= BoardCount - 1) return false;
            Index++; Filed = false; BoardValid = false; Array.Clear(_presets, 0, 5); _edits.Clear(); return true;
        }
        public bool Pause(double now) { Step(now); if (Finished || Cancelled || Paused) return false; Paused = true; return true; }
        public bool Resume(double now) { Step(now); if (Finished || Cancelled || !Paused) return false; Paused = false; return true; }
        public void Cancel() { if (!Finished) Cancelled = true; }
    }
}
