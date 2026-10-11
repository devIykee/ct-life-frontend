using System;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.Kit
{
    /// <summary>Easing curves (t in 0..1). Timings follow docs/research/duolingo-design-teardown.md section B7.</summary>
    public static class Ease
    {
        public static float Linear(float t) => t;
        public static float InCubic(float t) => t * t * t;
        public static float OutCubic(float t) { t = 1f - t; return 1f - t * t * t; }
        public static float OutQuint(float t) { t = 1f - t; return 1f - t * t * t * t * t; }
        public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;

        public static float OutBack(float t)
        {
            const float s = 1.70158f;
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }
    }

    /// <summary>
    /// One code-driven animation on a VisualElement, ticked by that element's scheduler (UI Toolkit's own clock, so it
    /// pauses with the panel). The step callback is allocated once when the tween starts; ticks allocate nothing.
    /// </summary>
    public sealed class Tween
    {
        private readonly Action<float> _step;
        private readonly Func<float, float> _ease;
        private readonly float _duration;
        private readonly TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>();
        private IVisualElementScheduledItem _item;
        private float _start;

        private Tween(Action<float> step, float seconds, Func<float, float> ease)
        {
            _step = step;
            _duration = Mathf.Max(0.0001f, seconds);
            _ease = ease ?? Ease.Linear;
        }

        public Task Completion => _done.Task;
        public bool Running => _item != null;

        /// <summary>Runs `step(eased progress)` for `seconds` (scaled by <see cref="Motion.TimeScale"/>). Instant under reduced motion
        /// when `skipWhenReduced` is set. The element must be in a panel for the scheduler to tick.</summary>
        public static Tween Run(VisualElement owner, float seconds, Action<float> step, Func<float, float> ease = null, bool skipWhenReduced = true)
        {
            var tween = new Tween(step, seconds, ease);
            if (owner == null || (skipWhenReduced && DeviceFeel.ReducedMotion) || owner.panel == null)
            {
                tween.Finish();
                return tween;
            }
            tween._start = Motion.Now;
            tween._step(tween._ease(0f));
            tween._item = owner.schedule.Execute(tween.Tick).Every(16);
            return tween;
        }

        private void Tick()
        {
            float t = (Motion.Now - _start) / _duration;
            if (t >= 1f) { Finish(); return; }
            try { _step(_ease(t)); }
            catch (Exception e) { Log.Exception("ui", e); Finish(); }
        }

        /// <summary>Jumps to the end state and completes.</summary>
        public void Finish()
        {
            _item?.Pause();
            _item = null;
            try { _step?.Invoke(_ease(1f)); }
            catch (Exception e) { Log.Exception("ui", e); }
            _done.TrySetResult(true);
        }

        /// <summary>Stops where it is (no end state) and completes.</summary>
        public void Cancel()
        {
            _item?.Pause();
            _item = null;
            _done.TrySetResult(false);
        }
    }

    /// <summary>Shared motion helpers. All of them respect the phone's reduced-motion setting.</summary>
    public static class Motion
    {
        /// <summary>1 = real time. Editor captures slow animations down to sample frame strips; never changed at runtime.</summary>
        public static float TimeScale = 1f;
        private static float _scaledBase, _realBase;

        public static float Now
        {
            get
            {
                float real = Time.realtimeSinceStartup;
                return _scaledBase + (real - _realBase) * TimeScale;
            }
        }

        /// <summary>Changes the time scale without a jump in <see cref="Now"/>.</summary>
        public static void SetTimeScale(float scale)
        {
            _scaledBase = Now;
            _realBase = Time.realtimeSinceStartup;
            TimeScale = Mathf.Max(0.01f, scale);
        }

        public static bool Reduced => DeviceFeel.ReducedMotion;

        /// <summary>Waits `seconds` of animation time (respects <see cref="TimeScale"/>).</summary>
        public static async Task Delay(float seconds)
        {
            float end = Now + seconds;
            while (Now < end) await Awaitable.NextFrameAsync();
        }

        /// <summary>Horizontal error shake: 280 ms, three swings, decaying (spec B7). Skipped under reduced motion.</summary>
        public static Tween Shake(VisualElement e, float amplitude = 22f, float seconds = 0.28f) =>
            Tween.Run(e, seconds, t => e.style.translate = new Translate(amplitude * Mathf.Sin(t * Mathf.PI * 6f) * (1f - t), 0f));

        /// <summary>A quick scale pop (peak then back to 1). Skipped under reduced motion.</summary>
        public static Tween Pop(VisualElement e, float peak = 1.06f, float seconds = 0.22f) =>
            Tween.Run(e, seconds, t =>
            {
                float s = 1f + (peak - 1f) * Mathf.Sin(t * Mathf.PI);
                e.style.scale = new Scale(new Vector2(s, s));
            });

        /// <summary>Fades and slides an element in from `dx, dy`: content entering (320 ms, ease-out-quint, spec B7).</summary>
        public static Tween Enter(VisualElement e, float dx, float dy, float seconds = 0.32f)
        {
            if (Reduced) return Fade(e, 0f, 1f, 0.12f);
            return Tween.Run(e, seconds, t =>
            {
                e.style.opacity = t;
                e.style.translate = new Translate(dx * (1f - t), dy * (1f - t));
            }, Ease.OutQuint);
        }

        /// <summary>Opacity tween. Under reduced motion this is the 120 ms fade every animation becomes.</summary>
        public static Tween Fade(VisualElement e, float from, float to, float seconds = 0.2f) =>
            Tween.Run(e, Reduced ? Mathf.Min(seconds, 0.12f) : seconds, t => e.style.opacity = Mathf.Lerp(from, to, t), Ease.OutCubic, skipWhenReduced: false);
    }

    /// <summary>
    /// Every onboarding sound and haptic, named in one place so QA can see them together (spec section C4). Sounds go through
    /// the Bus (SfxSignal), so the audio settings and cooldowns apply; haptics honour the phone's touch-feedback setting.
    /// </summary>
    public static class Cues
    {
        public const string SfxTap = "sfx_ui_tap", SfxSelect = "sfx_ui_toggle_on", SfxDeselect = "sfx_ui_toggle_off",
            SfxError = "sfx_ui_error", SfxStep = "sfx_ui_success", SfxHop = "sfx_ui_toggle_on", SfxFlip = "sfx_pnl_card_flip",
            SfxGood = "sfx_moon_horn", SfxDebt = "sfx_rug", SfxCoins = "sfx_cash_in", SfxCelebrate = "sfx_graduation";

        public static void Play(string sfx, float volume = 1f)
        {
            if (!string.IsNullOrEmpty(sfx)) Bus.Publish(new SfxSignal(sfx, volume));
        }

        /// <summary>Card select: tick sound + light haptic.</summary>
        public static void Select() { Play(SfxSelect, 0.8f); DeviceFeel.Tick(); }

        /// <summary>A selection pushed out (third trait): low tick, no haptic.</summary>
        public static void Deselect() => Play(SfxDeselect, 0.6f);

        /// <summary>The draw spotlight hopping: quiet tick + the lightest haptic.</summary>
        public static void Hop() { Play(SfxHop, 0.45f); DeviceFeel.ClockTick(); }

        /// <summary>A step completed (NEXT on an answered step).</summary>
        public static void Step() { Play(SfxStep, 0.55f); DeviceFeel.Confirm(); }

        /// <summary>A disabled CTA tapped or a failed submit: error blip + reject haptic.</summary>
        public static void Nope() { Play(SfxError, 0.6f); DeviceFeel.Reject(); }
    }
}
