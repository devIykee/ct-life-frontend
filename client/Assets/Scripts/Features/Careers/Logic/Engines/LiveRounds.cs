using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.Careers
{
    // ------------------------------------------------------------------ Timing

    /// <summary>
    /// One timing opportunity, pre-sampled from the seed every `Step` seconds: what the player sees (Signal, Tell, Note)
    /// and what acting at that moment is worth (Payoff, may be negative). Best ≤ 0 means the right play is to pass.
    /// </summary>
    public sealed class TimingWindow
    {
        public string Title, Brief;
        public double Step = 0.1;
        public double[] Signal;
        public double[] Payoff;
        /// <summary>A visible warning or cue at that sample (e.g. "dev wallet moving"). May be null.</summary>
        public bool[] Tell;
        /// <summary>Optional one-line event shown from that sample on (e.g. "KOL quote-posted"). May be null.</summary>
        public string[] Note;
        /// <summary>Optional formatted readout per sample (e.g. "Rewards $7.20 · Gas $2.10"). May be null.</summary>
        public string[] Readout;
        /// <summary>Shown under the readout while the tell is lit.</summary>
        public string TellText;

        public int Samples => Signal?.Length ?? 0;
        public double Length => Samples * Step;
        public double Best
        {
            get
            {
                double best = 0;
                foreach (var p in Payoff) if (p > best) best = p;
                return best;
            }
        }
    }

    public readonly struct TimingResult
    {
        public readonly TimingWindow Window;
        public readonly bool Acted;
        public readonly int Sample;
        public readonly double Payoff, Best, Points;
        public TimingResult(TimingWindow window, bool acted, int sample, double payoff, double best, double points)
        { Window = window; Acted = acted; Sample = sample; Payoff = payoff; Best = best; Points = points; }
    }

    /// <summary>
    /// Engine "Timing": one action per window. Acting scores payoff/best (0..1); acting into a loss scores 0; letting a
    /// window with no profitable moment pass scores 1. After an action, a short result beat, then the next window starts.
    /// </summary>
    public sealed class TimingRound : RoundBase
    {
        public const double FeedbackSeconds = 0.9;
        private readonly List<TimingWindow> _windows;
        private readonly List<TimingResult> _results = new List<TimingResult>();
        private double _windowStart, _actAt;
        private bool _acted;
        private double _points;

        public TimingRound(IReadOnlyList<TimingWindow> windows, int maxScore, double duration) : base(maxScore, duration)
        {
            if (windows == null || windows.Count == 0) throw new ArgumentException("No windows", nameof(windows));
            foreach (var w in windows)
                if (w?.Signal == null || w.Payoff == null || w.Samples < 10 || w.Payoff.Length != w.Samples || !(w.Step > 0) ||
                    (w.Tell != null && w.Tell.Length != w.Samples) || (w.Note != null && w.Note.Length != w.Samples) ||
                    (w.Readout != null && w.Readout.Length != w.Samples))
                    throw new ArgumentException("Malformed timing window");
            if (windows.Sum(w => w.Length) > duration + 1e-6) throw new ArgumentException("Windows do not fit the duration");
            _windows = new List<TimingWindow>(windows);
        }

        public IReadOnlyList<TimingWindow> Windows => _windows;
        public int WindowIndex { get; private set; }
        public TimingWindow Window => WindowIndex < _windows.Count ? _windows[WindowIndex] : null;
        public bool Acted => _acted;
        public IReadOnlyList<TimingResult> Results => _results;
        public TimingResult? Last => _results.Count > 0 ? _results[_results.Count - 1] : (TimingResult?)null;
        public override bool IsComplete => WindowIndex >= _windows.Count;
        public double WindowTime => Math.Max(0, Elapsed - _windowStart);

        public int Sample
        {
            get
            {
                var w = Window;
                if (w == null) return 0;
                double t = (_acted ? _actAt : Elapsed) - _windowStart;
                return Math.Max(0, Math.Min(w.Samples - 1, (int)Math.Floor(t / w.Step + 1e-9)));
            }
        }

        public double Signal => Window?.Signal[Sample] ?? 0;
        public string Readout => Window?.Readout?[Sample];
        public bool Tell => Window?.Tell != null && Window.Tell[Sample];
        public string Note
        {
            get
            {
                var w = Window;
                if (w?.Note == null) return null;
                for (int i = Sample; i >= 0; i--) if (!string.IsNullOrEmpty(w.Note[i])) return w.Note[i];
                return null;
            }
        }

        protected override void OnAdvance()
        {
            while (WindowIndex < _windows.Count)
            {
                var w = _windows[WindowIndex];
                if (_acted)
                {
                    if (Elapsed + 1e-9 < _actAt + FeedbackSeconds) return;
                    NextWindow(_actAt + FeedbackSeconds);
                    continue;
                }
                if (Elapsed + 1e-9 < _windowStart + w.Length) return;
                // Window closed without action.
                double best = w.Best;
                double pts = best <= 0 ? 1 : 0;
                _points += pts;
                _results.Add(new TimingResult(w, false, w.Samples - 1, 0, best, pts));
                NextWindow(_windowStart + w.Length);
            }
        }

        private void NextWindow(double start)
        {
            WindowIndex++;
            _windowStart = start;
            _acted = false;
        }

        public TimingResult? Act()
        {
            if (!Open || Window == null || _acted) return null;
            var w = Window;
            int i = Sample;
            double payoff = w.Payoff[i], best = w.Best;
            double pts = best <= 0 || payoff <= 0 ? 0 : Math.Min(1, payoff / best);
            _points += pts;
            var result = new TimingResult(w, true, i, payoff, best, pts);
            _results.Add(result);
            _acted = true;
            _actAt = Elapsed;
            return result;
        }

        protected override double Points => _points;
        protected override double PointsMax => _windows.Count;

        public override IReadOnlyList<ResultLine> Summary => new[]
        {
            new ResultLine("stat.sharp", _results.Count(r => r.Points >= 0.8)),
            new ResultLine("stat.losses", _results.Count(r => r.Acted && r.Payoff <= 0)),
            new ResultLine("stat.passes", _results.Count(r => !r.Acted && r.Best <= 0)),
            new ResultLine("stat.windows", $"{Math.Min(WindowIndex, _windows.Count)}/{_windows.Count}"),
        };
    }

    // ------------------------------------------------------------------ Balance

    public sealed class BalanceAction
    {
        public string Label;
        /// <summary>Added to the meter.</summary>
        public double Delta;
        /// <summary>Fraction of the distance to the band centre removed (0..1), e.g. a hedge.</summary>
        public double PullToCentre;
        public double Cooldown;
        /// <summary>Points cost in in-band seconds (fees, reputation).</summary>
        public double Cost;
        /// <summary>When set, this action answers cues of the same kind.</summary>
        public string Kind;
    }

    public sealed class BalanceCue
    {
        public double At, Window;
        public string Kind, Text;
        public double Reward, Penalty;
    }

    public sealed class BalanceSpec
    {
        public string MeterLabel, LowLabel, HighLabel;
        public double Min, Max, Start, BandLo, BandHi;
        public double Step = 0.05;
        public double SegmentSeconds = 0.5;
        /// <summary>Meter change per second for each segment (seeded order flow / room decay).</summary>
        public double[] Drift;
        /// <summary>Optional note per drift segment (e.g. "Whale dumping into your bid"). May be null.</summary>
        public string[] Notes;
        public BalanceAction[] Actions;
        public BalanceCue[] Cues = Array.Empty<BalanceCue>();
        /// <summary>Applied when a cue-answering action is used with no matching cue live (a misfire).</summary>
        public double MisfirePenalty;
        public double Centre => (BandLo + BandHi) / 2;
    }

    public enum BalanceOutcome { Applied, Answered, Misfire }

    /// <summary>
    /// Engine "Balance": a meter drifts with seeded flow; actions push it (with cooldowns and costs); cues demand the
    /// matching action within a window. Fixed 20 Hz simulation, so results depend only on when inputs happen.
    /// Points: seconds in the green band minus action costs.
    /// </summary>
    public sealed class BalanceRound : RoundBase
    {
        private readonly BalanceSpec _spec;
        private readonly double[] _readyAt;
        private double _sim, _inBand, _costs;
        private int _nextCue;

        public BalanceRound(BalanceSpec spec, int maxScore, double duration) : base(maxScore, duration)
        {
            if (spec == null || spec.Drift == null || spec.Drift.Length == 0 || spec.Actions == null || spec.Actions.Length == 0 ||
                spec.Actions.Length > 4 || !(spec.Max > spec.Min) || spec.BandLo < spec.Min || spec.BandHi > spec.Max || spec.BandLo >= spec.BandHi ||
                !(spec.Step > 0) || !(spec.SegmentSeconds > 0) || (spec.Notes != null && spec.Notes.Length != spec.Drift.Length))
                throw new ArgumentException("Malformed balance spec");
            _spec = spec;
            _readyAt = new double[spec.Actions.Length];
            Value = Clamp(spec.Start);
        }

        public BalanceSpec Spec => _spec;
        public double Value { get; private set; }
        public bool InBand => Value >= _spec.BandLo && Value <= _spec.BandHi;
        public string Note
        {
            get
            {
                if (_spec.Notes == null) return null;
                int seg = (int)Math.Floor(_sim / _spec.SegmentSeconds + 1e-9);
                return seg >= 0 && seg < _spec.Notes.Length ? _spec.Notes[seg] : null;
            }
        }
        public double InBandSeconds => _inBand;
        public BalanceCue ActiveCue { get; private set; }
        public double CueTimeLeft => ActiveCue == null ? 0 : Math.Max(0, ActiveCue.At + ActiveCue.Window - _sim);
        public int Answered { get; private set; }
        public int Missed { get; private set; }
        public int Misfires { get; private set; }
        public override bool IsComplete => false;

        public double CooldownLeft(int action) => action < 0 || action >= _readyAt.Length ? 0 : Math.Max(0, _readyAt[action] - _sim);

        private double Clamp(double v) => v < _spec.Min ? _spec.Min : v > _spec.Max ? _spec.Max : v;

        protected override void OnAdvance()
        {
            while (_sim + _spec.Step <= Elapsed + 1e-9) StepOnce();
        }

        private void StepOnce()
        {
            int seg = (int)Math.Floor(_sim / _spec.SegmentSeconds + 1e-9);
            double drift = _spec.Drift[Math.Min(seg, _spec.Drift.Length - 1)];
            Value = Clamp(Value + drift * _spec.Step);
            if (InBand) _inBand += _spec.Step;
            _sim += _spec.Step;
            if (ActiveCue != null && _sim >= ActiveCue.At + ActiveCue.Window - 1e-9)
            {
                Value = Clamp(Value + ActiveCue.Penalty);
                Missed++;
                ActiveCue = null;
            }
            while (_nextCue < _spec.Cues.Length && _spec.Cues[_nextCue].At <= _sim + 1e-9)
            {
                if (ActiveCue != null) { Value = Clamp(Value + ActiveCue.Penalty); Missed++; }
                ActiveCue = _spec.Cues[_nextCue++];
            }
        }

        public BalanceOutcome? Apply(int action)
        {
            if (!Open || action < 0 || action >= _spec.Actions.Length || CooldownLeft(action) > 1e-9) return null;
            var a = _spec.Actions[action];
            BalanceOutcome outcome = BalanceOutcome.Applied;
            if (!string.IsNullOrEmpty(a.Kind))
            {
                if (ActiveCue != null && ActiveCue.Kind == a.Kind)
                {
                    Value = Clamp(Value + ActiveCue.Reward);
                    Answered++;
                    ActiveCue = null;
                    outcome = BalanceOutcome.Answered;
                }
                else
                {
                    Value = Clamp(Value + _spec.MisfirePenalty);
                    Misfires++;
                    outcome = BalanceOutcome.Misfire;
                }
            }
            Value = Clamp(Value + a.Delta);
            if (a.PullToCentre > 0) Value = Clamp(Value - (Value - _spec.Centre) * Math.Min(1, a.PullToCentre));
            _costs += a.Cost;
            _readyAt[action] = _sim + a.Cooldown;
            return outcome;
        }

        protected override double Points => Math.Max(0, _inBand - _costs);
        protected override double PointsMax => Duration;

        public override IReadOnlyList<ResultLine> Summary
        {
            get
            {
                var lines = new List<ResultLine>
                {
                    new ResultLine("stat.inBand", Math.Round(100 * _inBand / Duration).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%"),
                    new ResultLine("stat.costs", _costs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s"),
                };
                if (_spec.Cues.Length > 0)
                {
                    lines.Add(new ResultLine("stat.answered", Answered));
                    lines.Add(new ResultLine("stat.missed", Missed));
                    lines.Add(new ResultLine("stat.misfires", Misfires));
                }
                return lines;
            }
        }
    }
}
