using System;
using CTLife.Core;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>
    /// Small device-feel hooks for UI: haptics (View.performHapticFeedback, which honours the phone's own touch-feedback
    /// setting) and the system "reduce motion" preference. Android only; elsewhere haptics are no-ops and motion is allowed.
    /// </summary>
    public static class DeviceFeel
    {
        // android.view.HapticFeedbackConstants (AOSP frameworks/base, read 2026-10-08). CONFIRM and REJECT are newer
        // constants; on a phone that does not know them performHapticFeedback returns false and nothing happens.
        private const int VirtualKey = 1, ClockTickConstant = 4, ConfirmConstant = 16, RejectConstant = 17;

        private static bool? _reducedMotion;
        private static bool? _reducedMotionOverride;

        /// <summary>
        /// True when the player turned animations off in Android settings ("Remove animations", which sets
        /// Settings.Global.ANIMATOR_DURATION_SCALE to 0). CT Life has no in-game motion setting yet. Read once per launch.
        /// </summary>
        public static bool ReducedMotion
        {
            get
            {
                if (_reducedMotionOverride.HasValue) return _reducedMotionOverride.Value;
                if (_reducedMotion.HasValue) return _reducedMotion.Value;
                _reducedMotion = false;
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                    using var global = new AndroidJavaClass("android.provider.Settings$Global");
                    _reducedMotion = global.CallStatic<float>("getFloat", resolver, "animator_duration_scale", 1f) == 0f;
                }
                catch (Exception e) { Log.Debug("ui", "animator_duration_scale unreadable: " + e.Message); }
#endif
                return _reducedMotion.Value;
            }
        }

        /// <summary>Editor captures and tests: force reduced motion on or off; null returns to the phone's setting.</summary>
        public static void OverrideReducedMotion(bool? on) => _reducedMotionOverride = on;

        /// <summary>A light "key press" tick (VIRTUAL_KEY): button and card presses.</summary>
        public static void Tick() => Haptic(VirtualKey);

        /// <summary>The lightest tick (CLOCK_TICK): the lottery spotlight hopping card to card.</summary>
        public static void ClockTick() => Haptic(ClockTickConstant);

        /// <summary>A confirming tap (CONFIRM): a good reveal, a finished step, the final celebration.</summary>
        public static void Confirm() => Haptic(ConfirmConstant);

        /// <summary>A soft rejecting thud (REJECT): the debt reveal, an unanswerable CTA, an error.</summary>
        public static void Reject() => Haptic(RejectConstant);

        private static void Haptic(int constant)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        using var window = activity.Call<AndroidJavaObject>("getWindow");
                        using var view = window.Call<AndroidJavaObject>("getDecorView");
                        view.Call<bool>("performHapticFeedback", constant);
                    }
                    catch (Exception e) { Log.Debug("ui", "haptic " + constant + " failed: " + e.Message); }
                    finally { activity.Dispose(); }
                }));
            }
            catch (Exception e) { Log.Debug("ui", "haptic unavailable: " + e.Message); }
#endif
        }
    }
}
