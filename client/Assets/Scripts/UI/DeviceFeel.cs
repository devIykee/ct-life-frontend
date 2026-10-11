using System;
using CTLife.Core;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>
    /// Small device-feel hooks for UI: a light haptic tick on press and the system "reduce motion" preference.
    /// Android only; elsewhere the tick is a no-op and motion is allowed.
    /// </summary>
    public static class DeviceFeel
    {
        private static bool? _reducedMotion;

        /// <summary>
        /// True when the player turned animations off in Android settings ("Remove animations", which sets
        /// Settings.Global.ANIMATOR_DURATION_SCALE to 0). CT Life has no in-game motion setting yet. Read once per launch.
        /// </summary>
        public static bool ReducedMotion
        {
            get
            {
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

        /// <summary>
        /// A light "key press" haptic (View.performHapticFeedback(VIRTUAL_KEY)), which honours the phone's own touch-feedback
        /// setting. Runs on the Android UI thread.
        /// </summary>
        public static void Tick()
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
                        view.Call<bool>("performHapticFeedback", 1); // HapticFeedbackConstants.VIRTUAL_KEY
                    }
                    catch (Exception e) { Log.Debug("ui", "haptic tick failed: " + e.Message); }
                    finally { activity.Dispose(); }
                }));
            }
            catch (Exception e) { Log.Debug("ui", "haptic tick unavailable: " + e.Message); }
#endif
        }
    }
}
