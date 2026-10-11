using System;

namespace CTLife.Audio
{
    /// <summary>Gains (linear 0..1) of each music layer plus the music low-pass cutoff for one market mood.</summary>
    public struct MoodLayers
    {
        public float Pad, Bass, Drums, Lead, Tension, FullMix, CutoffHz;

        /// <summary>The low-pass is bypassed (filter component disabled) at or above this cutoff.</summary>
        public bool FilterActive => CutoffHz < MoodMix.OpenCutoffHz - 1f;

        public float Gain(string layer)
        {
            switch (layer)
            {
                case "pad": return Pad;
                case "bass": return Bass;
                case "drums": return Drums;
                case "lead": return Lead;
                case "tension": return Tension;
                case "full_mix": return FullMix;
                default: return 0f;
            }
        }
    }

    /// <summary>
    /// Build Spec "Adaptive music": `market_mood` −1 (red) … +1 (green). Green brings in drums and lead; red low-passes
    /// the mix and adds a tension layer. Mapping from audio/README.md:
    /// <code>
    /// mood          pad  bass  drums          lead                tension        low-pass
    /// +1.0 … +0.5   on   on    on             on                  off            open
    /// +0.5 …  0     on   on    fades in from 0  off → in after +0.5  off          open
    ///  0   … −0.5   on   on    off            off                 fades in       20 kHz → 2.5 kHz
    /// −0.5 … −1     on   on    off            off                 on             2.5 kHz → 900 Hz
    /// </code>
    /// Full-mix sets (a complete licensed composition, no stems) keep the arrangement intact: drums and lead are
    /// already in it, so green = neutral = the unfiltered track; red applies the same low-pass and the same tension
    /// layer (a key- and tempo-neutral bed, `music.&lt;key&gt;.stems.tension`) on top.
    /// </summary>
    public static class MoodMix
    {
        public const float OpenCutoffHz = 20000f;
        public const float MidCutoffHz = 2500f;
        public const float FloorCutoffHz = 900f;

        /// <summary>Market Mood basis points (real SOL 24h change, city room) → −1..1. ±5% saturates.</summary>
        public static float FromBps(int bps) => AudioMath.Clamp(bps / 500f, -1f, 1f);

        public static float DrumsGain(float mood) => AudioMath.SmoothStep01(AudioMath.InverseLerp(0f, 0.5f, mood));
        public static float LeadGain(float mood) => AudioMath.SmoothStep01(AudioMath.InverseLerp(0.35f, 0.5f, mood));
        public static float TensionGain(float mood) => AudioMath.SmoothStep01(AudioMath.InverseLerp(0f, -0.5f, mood));

        public static float CutoffHz(float mood)
        {
            if (float.IsNaN(mood) || mood >= 0f) return OpenCutoffHz;
            if (mood >= -0.5f) return AudioMath.LogLerp(OpenCutoffHz, MidCutoffHz, -mood / 0.5f);
            return AudioMath.LogLerp(MidCutoffHz, FloorCutoffHz, (-mood - 0.5f) / 0.5f);
        }

        public static MoodLayers Evaluate(float mood, bool fullMixMode)
        {
            mood = AudioMath.Clamp(float.IsNaN(mood) ? 0f : mood, -1f, 1f);
            var l = new MoodLayers { Tension = TensionGain(mood), CutoffHz = CutoffHz(mood) };
            if (fullMixMode) l.FullMix = 1f;
            else
            {
                l.Pad = 1f;
                l.Bass = 1f;
                l.Drums = DrumsGain(mood);
                l.Lead = LeadGain(mood);
            }
            return l;
        }
    }

    /// <summary>Bar arithmetic on the DSP clock (4/4).</summary>
    public static class BarClock
    {
        public static double BarSeconds(double bpm, int beatsPerBar = 4) =>
            (bpm > 1.0 && !double.IsNaN(bpm) ? 60.0 / bpm : 0.5) * Math.Max(1, beatsPerBar);

        /// <summary>Index of the bar containing `now` (−1 before the start).</summary>
        public static long BarIndex(double startDsp, double bpm, double nowDsp, int beatsPerBar = 4)
        {
            if (nowDsp < startDsp) return -1;
            return (long)Math.Floor((nowDsp - startDsp) / BarSeconds(bpm, beatsPerBar));
        }

        /// <summary>DSP time of the next bar line strictly after `now` (the start itself when not started yet).</summary>
        public static double NextBarDsp(double startDsp, double bpm, double nowDsp, int beatsPerBar = 4)
        {
            if (nowDsp < startDsp) return startDsp;
            double bar = BarSeconds(bpm, beatsPerBar);
            return startDsp + (Math.Floor((nowDsp - startDsp) / bar) + 1.0) * bar;
        }
    }

    /// <summary>
    /// Stateful mood follower for one music set (README: "Fade over ~2 bars; switch on bar lines"). A new mood is
    /// latched at the next bar line; layer gains then move to the target over two bars (clamped 2–8 s), the cutoff
    /// moves in log-frequency at the same pace. The first tick after <see cref="Restart"/> snaps to the target so a
    /// venue never starts with a fade from the previous venue's state.
    /// </summary>
    public sealed class MoodController
    {
        private float _target;
        private long _lastBar = long.MinValue;
        private bool _snap = true;
        private MoodLayers _current;

        public float TargetMood => _target;
        public float AppliedMood { get; private set; }
        public MoodLayers Current => _current;
        public bool FullMixMode { get; private set; }

        /// <summary>
        /// False for sets that must ignore the market (the title / onboarding theme): the mix is evaluated at mood 0
        /// (pad/bass on, drums in halfway, no lead or tension, filter open) — for a full mix: the plain track. The requested
        /// mood is still remembered and applies again as soon as a reactive set plays.
        /// </summary>
        public bool Reactive { get; set; } = true;

        /// <summary>The mood the mix is built from: the latched market mood, or 0 for a non-reactive set.</summary>
        public float EffectiveMood => Reactive ? AppliedMood : 0f;

        public void SetMood(float mood) => _target = AudioMath.Clamp(float.IsNaN(mood) ? 0f : mood, -1f, 1f);

        /// <summary>A new set started (or the mode changed): latch the target immediately on the next tick.</summary>
        public void Restart(bool fullMixMode)
        {
            FullMixMode = fullMixMode;
            _snap = true;
            _lastBar = long.MinValue;
        }

        public static float FadeSeconds(double bpm) => (float)Math.Max(2.0, Math.Min(8.0, 2.0 * BarClock.BarSeconds(bpm)));

        public MoodLayers Tick(double nowDsp, double setStartDsp, double bpm, float deltaSeconds)
        {
            long bar = BarClock.BarIndex(setStartDsp, bpm, nowDsp);
            if (_snap)
            {
                AppliedMood = _target;
                _current = MoodMix.Evaluate(EffectiveMood, FullMixMode);
                _lastBar = bar;
                _snap = false;
                return _current;
            }
            if (bar != _lastBar)
            {
                _lastBar = bar;
                AppliedMood = _target;
            }
            var goal = MoodMix.Evaluate(EffectiveMood, FullMixMode);
            if (float.IsNaN(deltaSeconds) || deltaSeconds <= 0f) return _current;
            float step = Math.Min(1f, deltaSeconds) / FadeSeconds(bpm);
            _current.Pad = AudioMath.MoveTowards(_current.Pad, goal.Pad, step);
            _current.Bass = AudioMath.MoveTowards(_current.Bass, goal.Bass, step);
            _current.Drums = AudioMath.MoveTowards(_current.Drums, goal.Drums, step);
            _current.Lead = AudioMath.MoveTowards(_current.Lead, goal.Lead, step);
            _current.Tension = AudioMath.MoveTowards(_current.Tension, goal.Tension, step);
            _current.FullMix = AudioMath.MoveTowards(_current.FullMix, goal.FullMix, step);
            // Full open→floor range is log2(20000/900) ≈ 4.5 octaves; cross it in one fade.
            double cur = Math.Log(Math.Max(20f, _current.CutoffHz), 2), tgt = Math.Log(goal.CutoffHz, 2);
            double octStep = 4.5 * step;
            cur = Math.Abs(tgt - cur) <= octStep ? tgt : cur + Math.Sign(tgt - cur) * octStep;
            _current.CutoffHz = (float)Math.Pow(2, cur);
            return _current;
        }
    }
}
