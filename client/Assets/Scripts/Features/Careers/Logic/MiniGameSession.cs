using System;
using System.Collections.Generic;

namespace CTLife.Careers
{
    /// <summary>A stat line on the results card. `LabelKey` is a CareersStrings key (unknown keys print as-is).</summary>
    public readonly struct ResultLine
    {
        public readonly string LabelKey;
        public readonly string Value;
        public ResultLine(string labelKey, string value) { LabelKey = labelKey; Value = value; }
        public ResultLine(string labelKey, int value) : this(labelKey, value.ToString(System.Globalization.CultureInfo.InvariantCulture)) { }
    }

    /// <summary>
    /// One mini-game's rules and state. Rounds are pure logic: time is the session's *active* elapsed seconds, passed in
    /// through Advance, so pausing freezes a round without the round knowing about pauses or wall clocks.
    /// </summary>
    public interface IMiniGameRound
    {
        int MaxScore { get; }
        double Duration { get; }
        double Elapsed { get; }
        /// <summary>Move round time forward to `elapsed` (active seconds since start). Never moves backwards.</summary>
        void Advance(double elapsed);
        /// <summary>All work is done before the clock (boards filed, deck sorted). Timed-only rounds return false.</summary>
        bool IsComplete { get; }
        /// <summary>Current score in [0, MaxScore].</summary>
        int Score { get; }
        IReadOnlyList<ResultLine> Summary { get; }
    }

    public abstract class RoundBase : IMiniGameRound
    {
        protected RoundBase(int maxScore, double duration)
        {
            if (maxScore <= 0 || maxScore > 100000) throw new ArgumentOutOfRangeException(nameof(maxScore));
            if (!(duration > 0) || duration > 600) throw new ArgumentOutOfRangeException(nameof(duration));
            MaxScore = maxScore;
            Duration = duration;
        }

        public int MaxScore { get; }
        public double Duration { get; }
        public double Elapsed { get; private set; }
        public abstract bool IsComplete { get; }
        /// <summary>Inputs are accepted only while the round is open: not complete and inside its duration.</summary>
        public bool Open => !IsComplete && Elapsed < Duration;

        public void Advance(double elapsed)
        {
            if (double.IsNaN(elapsed) || double.IsInfinity(elapsed)) throw new ArgumentOutOfRangeException(nameof(elapsed));
            if (elapsed <= Elapsed) return;
            Elapsed = Math.Min(Duration, elapsed);
            OnAdvance();
        }

        protected virtual void OnAdvance() { }

        protected abstract double Points { get; }
        protected abstract double PointsMax { get; }
        public int Score => Scoring.Scale(Points, PointsMax, MaxScore);
        public abstract IReadOnlyList<ResultLine> Summary { get; }
    }

    /// <summary>
    /// Active-time clock. Wall time passes into it with Step(now) (any monotonic seconds source). While paused, time
    /// accrues to the pause budget instead of the round. A gap larger than HitchSeconds while playing (the app was
    /// suspended without a focus event, a long GC, a debugger) is not counted and pauses the clock: interruptions
    /// never eat a player's round time.
    /// </summary>
    public sealed class ActiveClock
    {
        public const double HitchSeconds = 1.5;
        private double _last;
        private bool _started;

        public ActiveClock(double duration, double maxPausedSeconds = 180)
        {
            if (!(duration > 0) || duration > 600) throw new ArgumentOutOfRangeException(nameof(duration));
            Duration = duration;
            MaxPausedSeconds = Math.Max(0, maxPausedSeconds);
        }

        public double Duration { get; }
        public double MaxPausedSeconds { get; }
        public double Elapsed { get; private set; }
        public bool Paused { get; private set; }
        public double PausedSeconds { get; private set; }
        public int PauseCount { get; private set; }
        public bool Started => _started;
        public double Remaining => Math.Max(0, Duration - Elapsed);
        public bool Expired => Elapsed >= Duration;
        public bool PauseBudgetExhausted => PausedSeconds >= MaxPausedSeconds;

        public void Start(double now)
        {
            Validate(now);
            if (_started) return;
            _started = true;
            _last = now;
        }

        /// <returns>True when this step was treated as an interruption (the clock is now paused).</returns>
        public bool Step(double now)
        {
            Validate(now);
            if (!_started) return false;
            double delta = now - _last;
            _last = Math.Max(_last, now);
            if (delta <= 0) return false; // a clock that went backwards contributes nothing
            if (Paused)
            {
                PausedSeconds += delta;
                return false;
            }
            if (delta > HitchSeconds)
            {
                Paused = true;
                PauseCount++;
                return true;
            }
            Elapsed = Math.Min(Duration, Elapsed + delta);
            return false;
        }

        public bool Pause(double now)
        {
            Step(now);
            if (!_started || Paused || Expired) return false;
            Paused = true;
            PauseCount++;
            return true;
        }

        public bool Resume(double now)
        {
            Step(now);
            if (!_started || !Paused) return false;
            Paused = false;
            return true;
        }

        private static void Validate(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now));
        }
    }

    public enum MiniGamePhase { Briefing, Playing, Paused, Results, Cancelled }

    public enum PauseReason { Player, Interruption }

    /// <summary>
    /// Mini-game lifecycle: Briefing (tutorial card) → Playing ⇄ Paused → Results, or Cancelled from any live state.
    /// The final score is frozen on entering Results and is always within [0, MaxScore].
    /// End reasons: "complete" (all work done), "time", "player" (ended early), "pauseLimit" (paused too long).
    /// </summary>
    public sealed class MiniGameSession
    {
        public MiniGameSession(IMiniGameRound round, double maxPausedSeconds = 180)
        {
            Round = round ?? throw new ArgumentNullException(nameof(round));
            Clock = new ActiveClock(round.Duration, maxPausedSeconds);
        }

        public IMiniGameRound Round { get; }
        public ActiveClock Clock { get; }
        public MiniGamePhase Phase { get; private set; } = MiniGamePhase.Briefing;
        public PauseReason LastPauseReason { get; private set; }
        public int FinalScore { get; private set; }
        public string EndReason { get; private set; }
        public bool IsTerminal => Phase == MiniGamePhase.Results || Phase == MiniGamePhase.Cancelled;
        public bool CanInteract => Phase == MiniGamePhase.Playing;
        public int LiveScore => Scoring.Clamp(Round.Score, Round.MaxScore);
        public event Action<MiniGamePhase> PhaseChanged;

        public bool Begin(double now)
        {
            if (Phase != MiniGamePhase.Briefing) return false;
            Clock.Start(now);
            Set(MiniGamePhase.Playing);
            return true;
        }

        /// <summary>Per-frame update.</summary>
        public void Tick(double now)
        {
            if (Phase == MiniGamePhase.Playing)
            {
                if (Clock.Step(now))
                {
                    LastPauseReason = PauseReason.Interruption;
                    Set(MiniGamePhase.Paused);
                    return;
                }
                Round.Advance(Clock.Elapsed);
                if (Round.IsComplete) Finish("complete");
                else if (Clock.Expired) Finish("time");
            }
            else if (Phase == MiniGamePhase.Paused)
            {
                Clock.Step(now);
                if (Clock.PauseBudgetExhausted) Finish("pauseLimit");
            }
        }

        /// <summary>Call after any player input so a completing action ends the round on the same frame.</summary>
        public void AfterInput()
        {
            if (Phase == MiniGamePhase.Playing && Round.IsComplete) Finish("complete");
        }

        public bool Pause(double now, PauseReason reason)
        {
            if (Phase != MiniGamePhase.Playing) return false;
            Clock.Step(now);
            Round.Advance(Clock.Elapsed);
            if (!Clock.Pause(now) && !Clock.Paused) return false;
            LastPauseReason = reason;
            Set(MiniGamePhase.Paused);
            return true;
        }

        public bool Resume(double now)
        {
            if (Phase != MiniGamePhase.Paused) return false;
            if (!Clock.Resume(now)) return false;
            Set(MiniGamePhase.Playing);
            return true;
        }

        /// <summary>The player ends the round now and keeps what they scored.</summary>
        public bool EndRound(double now)
        {
            if (Phase != MiniGamePhase.Playing && Phase != MiniGamePhase.Paused) return false;
            if (Phase == MiniGamePhase.Playing) Clock.Step(now);
            Finish("player");
            return true;
        }

        public bool Cancel()
        {
            if (IsTerminal) return false;
            Set(MiniGamePhase.Cancelled);
            return true;
        }

        private void Finish(string reason)
        {
            Round.Advance(Clock.Elapsed);
            FinalScore = Scoring.Clamp(Round.Score, Round.MaxScore);
            EndReason = reason;
            Set(MiniGamePhase.Results);
        }

        private void Set(MiniGamePhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }
    }
}
