using System;
using System.Collections.Generic;

namespace CTLife.UI
{
    public enum LiveCueAction { Camera, Caption, Applause }
    public enum LiveCueOutcome { Pending, Hit, Early, Wrong, Missed }
    public sealed class LiveCue
    {
        public int Index { get; }
        public LiveCueAction Action { get; }
        public int Copy { get; }
        public double Target { get; }
        public double Opens => Target - LiveCuesRound.HalfWindow;
        public double Closes => Target + LiveCuesRound.HalfWindow;
        public double Approach => Math.Max(0, Target - LiveCuesRound.ApproachSeconds);
        public LiveCueOutcome Outcome { get; internal set; }
        internal LiveCue(int index, LiveCueAction action, int copy)
        { Index = index; Action = action; Copy = copy; Target = 2.7 + 2.2 * index; }
    }
    public sealed class LiveCuesResult
    {
        public int Score { get; }
        public int Hits { get; }
        public int Early { get; }
        public int Wrong { get; }
        public int Missed { get; }
        public int BestCombo { get; }
        internal LiveCuesResult(int hits, int early, int wrong, int missed, int combo)
        { Hits = hits; Early = early; Wrong = wrong; Missed = missed; BestCombo = combo; Score = hits * 5; }
    }
    /// <summary>Authored local timing rules. No real audience, audio capture, social metrics or settlement.</summary>
    public sealed class LiveCuesRound
    {
        public const string ProposedId = "kol_engagement_live_cues";
        public const int Duration = 45, Maximum = 100, CueCount = 20;
        public const double HalfWindow = .4, ApproachSeconds = 1.8;
        private readonly LiveCue[] _cues = new LiveCue[CueCount];
        private readonly IReadOnlyList<LiveCue> _readOnly;
        private double _lastNow;
        public IReadOnlyList<LiveCue> Cues => _readOnly;
        public double Elapsed { get; private set; }
        public double Remaining => Math.Max(0, Duration - Elapsed);
        public int Index { get; private set; }
        public LiveCue Current => Index < CueCount ? _cues[Index] : null;
        public LiveCue Last { get; private set; }
        public bool Paused { get; private set; }
        public bool Cancelled { get; private set; }
        public bool Finished => !Cancelled && Elapsed >= Duration;
        public bool CanPress => !Paused && !Cancelled && !Finished && Current != null && Elapsed >= Current.Approach;
        public bool InWindow => CanPress && Elapsed >= Current.Opens && Elapsed <= Current.Closes;
        public int Hits { get; private set; }
        public int Early { get; private set; }
        public int Wrong { get; private set; }
        public int Missed { get; private set; }
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public int Score => Hits * 5;
        public LiveCuesResult Result => Finished ? new LiveCuesResult(Hits, Early, Wrong, Missed, BestCombo)
            : throw new InvalidOperationException("An unfinished or cancelled show has no result.");
        public LiveCuesRound(double now, int seed)
        {
            ValidateTime(now); _lastNow = now; _readOnly = Array.AsReadOnly(_cues);
            var random = new Random(seed); var actions = new int[CueCount];
            for (int start = 0; start < CueCount; start += 3)
            {
                var bag = new[] { 0, 1, 2 };
                for (int j = 2; j > 0; j--) { int k = random.Next(j + 1); (bag[j], bag[k]) = (bag[k], bag[j]); }
                for (int j = 0; j < 3 && start + j < CueCount; j++) actions[start + j] = bag[j];
            }
            var copies = new int[3][];
            for (int action = 0; action < 3; action++)
            {
                copies[action] = new int[10]; for (int j = 0; j < 10; j++) copies[action][j] = j;
                for (int j = 9; j > 0; j--) { int k = random.Next(j + 1); (copies[action][j], copies[action][k]) = (copies[action][k], copies[action][j]); }
            }
            var used = new int[3];
            for (int i = 0; i < CueCount; i++)
            {
                int chosen = actions[i];
                _cues[i] = new LiveCue(i, (LiveCueAction)chosen, copies[chosen][used[chosen]++]);
            }
        }
        private static void ValidateTime(double now)
        { if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now)); }
        public void Step(double now)
        {
            ValidateTime(now); if (now < _lastNow) throw new ArgumentOutOfRangeException(nameof(now), "Clock moved backwards.");
            double delta = now - _lastNow; _lastNow = now;
            if (Paused || Cancelled || Finished) return;
            Elapsed = Math.Min(Duration, Elapsed + delta);
            // The close edge is inclusive. A late callback resolves expired cues before it can act.
            while (Current != null && Elapsed > Current.Closes) Resolve(LiveCueOutcome.Missed);
        }
        public bool Press(int expectedCue, LiveCueAction action, double now)
        {
            Step(now);
            if (!CanPress || expectedCue != Index || (int)action < 0 || (int)action > 2) return false;
            Resolve(Elapsed < Current.Opens ? LiveCueOutcome.Early : action == Current.Action ? LiveCueOutcome.Hit : LiveCueOutcome.Wrong);
            return true;
        }
        private void Resolve(LiveCueOutcome outcome)
        {
            var cue = Current; cue.Outcome = outcome; Last = cue; Index++;
            if (outcome == LiveCueOutcome.Hit) { Hits++; Combo++; BestCombo = Math.Max(BestCombo, Combo); }
            else { Combo = 0; if (outcome == LiveCueOutcome.Early) Early++; else if (outcome == LiveCueOutcome.Wrong) Wrong++; else Missed++; }
        }
        public bool Pause(double now) { Step(now); if (Finished || Cancelled || Paused) return false; Paused = true; return true; }
        public bool Resume(double now) { Step(now); if (Finished || Cancelled || !Paused) return false; Paused = false; return true; }
        public void Cancel() { if (!Finished) Cancelled = true; }
    }
}
