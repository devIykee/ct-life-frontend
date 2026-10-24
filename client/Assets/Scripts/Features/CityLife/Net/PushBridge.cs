using System;
using CTLife.Core;
using UnityEngine;

namespace CTLife.CityLife
{
    /// <summary>
    /// The native side of push (Assets/Plugins/Android/CTPush.androidlib, Firebase Cloud Messaging for Android). Push is on
    /// only when the build carried a google-services.json (PushConfigBuild turns it into the Firebase resource values); without
    /// it <see cref="Available"/> is false, nothing else is called, and the game runs as before. Also reads the notification's
    /// `deep_link` from the launch intent (cold start, or a tap while running) and asks for POST_NOTIFICATIONS (Android 13+).
    /// </summary>
    public static class PushBridge
    {
        public const string Permission = "android.permission.POST_NOTIFICATIONS";
        private static bool? _available;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaClass _cls;
        private static AndroidJavaClass Cls => _cls ??= new AndroidJavaClass("com.ctlife.push.CTPush");
        private static AndroidJavaObject Activity
        {
            get { using var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"); return up.GetStatic<AndroidJavaObject>("currentActivity"); }
        }
        private static int Sdk { get { using var v = new AndroidJavaClass("android.os.Build$VERSION"); return v.GetStatic<int>("SDK_INT"); } }
#endif

        /// <summary>Firebase initialised from the bundled config (false in the editor and in builds without google-services.json).</summary>
        public static bool Available
        {
            get
            {
                if (_available.HasValue) return _available.Value;
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    using var act = Activity;
                    _available = Cls.CallStatic<bool>("init", act);
                    Log.Info("push", _available.Value ? "FCM ready (google-services.json bundled)"
                        : "push disabled: no google-services.json in this build (Firebase not configured)");
                }
                catch (Exception e) { _available = false; Log.Warn("push", "push disabled: native plugin unavailable (" + e.GetType().Name + ")"); }
#else
                _available = false;
                Log.Info("push", "push disabled: not an Android device build");
#endif
                return _available.Value;
            }
        }

        /// <summary>True when the OS lets the app post notifications (always true below Android 13).</summary>
        public static bool PermissionGranted
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try { return Sdk < 33 || UnityEngine.Android.Permission.HasUserAuthorizedPermission(Permission); }
                catch (Exception) { return false; }
#else
                return false;
#endif
            }
        }

        /// <summary>The OS needs asking (Android 13+, not granted yet).</summary>
        public static bool NeedsPermission
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try { return Sdk >= 33 && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(Permission); }
                catch (Exception) { return false; }
#else
                return false;
#endif
            }
        }

        /// <summary>Shows the system prompt; <paramref name="done"/> gets true when granted.</summary>
        public static void RequestPermission(Action<bool> done)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!NeedsPermission) { done?.Invoke(PermissionGranted); return; }
            var cb = new UnityEngine.Android.PermissionCallbacks();
            cb.PermissionGranted += _ => { Log.Info("push", "POST_NOTIFICATIONS granted"); done?.Invoke(true); };
            cb.PermissionDenied += _ => { Log.Info("push", "POST_NOTIFICATIONS denied"); done?.Invoke(false); };
            UnityEngine.Android.Permission.RequestUserPermission(Permission, cb);
#else
            Log.Info("push", "permission prompt skipped: not an Android device build");
            done?.Invoke(false);
#endif
        }

        /// <summary>Asks Firebase for the token (async on the Java side); poll <see cref="Token"/>.</summary>
        public static void RequestToken()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Available) try { Cls.CallStatic("requestToken"); } catch (Exception e) { Log.Warn("push", "token request failed: " + e.GetType().Name); }
#endif
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        private static string _devToken = LaunchArgs.Get("devPushToken");
        private static int _devRotations;
        /// <summary>Dev builds without Firebase: a stand-in token (launch extra devPushToken) so registration, sign-out
        /// unregister and rotation can be tested against the backend; it rotates like a real token on delete.</summary>
        public static string DevToken => string.IsNullOrEmpty(_devToken) ? null : _devToken;
#else
        public static string DevToken => null;
#endif

        /// <summary>Sign-out: deletes the FCM token on the device (FirebaseMessaging.deleteToken). No-op without Firebase.</summary>
        public static void DeleteToken()
        {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (DevToken != null) { _devToken = LaunchArgs.Get("devPushToken") + "-r" + (++_devRotations); Log.Info("push", "dev token rotated"); }
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Available) try { Cls.CallStatic("deleteToken"); } catch (Exception e) { Log.Warn("push", "token delete failed: " + e.GetType().Name); }
#endif
        }

        public static string Token
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!Available) return null;
                try { return Cls.CallStatic<string>("token"); } catch (Exception) { return null; }
#else
                return null;
#endif
            }
        }

        public static string TokenError
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!Available) return null;
                try { return Cls.CallStatic<string>("tokenError"); } catch (Exception) { return null; }
#else
                return null;
#endif
            }
        }

        /// <summary>The `deep_link` of the notification that opened (or re-focused) the app, consumed once; or a `ctlife://` URI.</summary>
        public static string TakeLaunchLink()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { using var act = Activity; return Cls.CallStatic<string>("takeLaunchLink", act); }
            catch (Exception e) { Log.Warn("push", "launch link unreadable: " + e.GetType().Name); return null; }
#else
            return null;
#endif
        }

        /// <summary>A message FCM delivered while the game was in front (JSON `{title, body, type, deep_link}`), or null.</summary>
        public static string PollForeground()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Available) return null;
            try { return Cls.CallStatic<string>("poll"); } catch (Exception) { return null; }
#else
            return null;
#endif
        }
    }
}
