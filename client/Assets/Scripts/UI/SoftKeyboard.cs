using System;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Closes the Android soft keyboard on request. On the Seeker (Unity 6000.3, GameActivity) a UI Toolkit TextField's
    /// keyboard stayed up after <c>Blur()</c>, and the field's <c>textEdition.touchScreenKeyboard</c> was null while
    /// <c>TouchScreenKeyboard.visible</c> was true (2026-10-08), so there was no Unity handle to close. The fallback asks
    /// Android's InputMethodManager to hide the keyboard for the activity window (<c>hideSoftInputFromWindow</c>), on the UI
    /// thread as Android requires.
    /// </summary>
    public static class SoftKeyboard
    {
        public static void Hide(TextField field)
        {
            var keyboard = field?.textEdition?.touchScreenKeyboard;
            if (keyboard != null && keyboard.active)
            {
                keyboard.active = false;
                return;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (TouchScreenKeyboard.visible) HideAndroid();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void HideAndroid()
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null) return;
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        using var imm = activity.Call<AndroidJavaObject>("getSystemService", "input_method");
                        using var window = activity.Call<AndroidJavaObject>("getWindow");
                        using var decor = window.Call<AndroidJavaObject>("getDecorView");
                        using var token = decor.Call<AndroidJavaObject>("getWindowToken");
                        imm?.Call<bool>("hideSoftInputFromWindow", token, 0);
                    }
                    catch (Exception e) { Log.Warn("ui", "Hiding the soft keyboard failed: " + e.Message); }
                }));
            }
            catch (Exception e) { Log.Warn("ui", "Hiding the soft keyboard failed: " + e.Message); }
        }
#endif
    }
}
