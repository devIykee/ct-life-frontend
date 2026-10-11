using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;

namespace CTLife.SocialFeatures
{
    public enum PermissionResult { Granted, Denied, DeniedForever, Dismissed }

    /// <summary>
    /// Android runtime permissions for the microphone and camera, requested at the moment of use (never at startup).
    /// Handles Granted, Denied (permanent when Android no longer shows a rationale) and RequestDismissed (Unity docs,
    /// ScriptReference/Android.PermissionCallbacks; the DontAskAgain event is obsolete in 6000.3.25). The core `MicPermission`
    /// only listens for Granted/Denied, so a dismissed dialog waits for its timeout; calling this first makes it return
    /// immediately (the permission is then already granted or the feature is not attempted). Unity adds CAMERA/RECORD_AUDIO to the manifest when
    /// WebCamTexture/Microphone are used (Unity manual, "Unity-handled permissions").
    /// </summary>
    public static class MediaPermission
    {
        public enum Kind { Microphone, Camera }

        public static bool Has(Kind kind)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return UnityEngine.Android.Permission.HasUserAuthorizedPermission(Name(kind));
#else
            return true;
#endif
        }

        public static async Task<PermissionResult> Request(Kind kind, CancellationToken ct)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var name = Name(kind);
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(name)) return PermissionResult.Granted;
            var tcs = new TaskCompletionSource<PermissionResult>();
            var cb = new UnityEngine.Android.PermissionCallbacks();
            cb.PermissionGranted += _ => tcs.TrySetResult(PermissionResult.Granted);
            // `PermissionDeniedAndDontAskAgain` is obsolete in 6000.3.25 ("Unreliable. Query ShouldShowRequestPermissionRationale
            // and use PermissionDenied event"): after a denial without a rationale the system dialog will not show again.
            cb.PermissionDenied += _ => tcs.TrySetResult(UnityEngine.Android.Permission.ShouldShowRequestPermissionRationale(name)
                ? PermissionResult.Denied : PermissionResult.DeniedForever);
            cb.PermissionRequestDismissed += _ => tcs.TrySetResult(PermissionResult.Dismissed);
            UnityEngine.Android.Permission.RequestUserPermission(name, cb);
            using (ct.Register(() => tcs.TrySetCanceled()))
                return await tcs.Task;
#else
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            return PermissionResult.Granted;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static string Name(Kind kind) => kind == Kind.Camera ? UnityEngine.Android.Permission.Camera : UnityEngine.Android.Permission.Microphone;
#endif

        /// <summary>Opens this app's system settings page (after "don't ask again"). [Unverified] on the Seeker.</summary>
        public static void OpenAppSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.settings.APPLICATION_DETAILS_SETTINGS"))
                using (var uris = new AndroidJavaClass("android.net.Uri"))
                using (var uri = uris.CallStatic<AndroidJavaObject>("fromParts", "package", activity.Call<string>("getPackageName"), null))
                {
                    intent.Call<AndroidJavaObject>("setData", uri);
                    intent.Call<AndroidJavaObject>("addFlags", 0x10000000); // FLAG_ACTIVITY_NEW_TASK
                    activity.Call("startActivity", intent);
                }
            }
            catch (System.Exception e) { Log.Warn("social", "Settings intent failed: " + e.GetType().Name); }
#else
            Log.Info("social", "OpenAppSettings is Android-only");
#endif
        }

        /// <summary>SocialStrings key for a refusal.</summary>
        public static string Key(Kind kind, PermissionResult r)
        {
            var k = kind == Kind.Camera ? "camera" : "mic";
            switch (r)
            {
                case PermissionResult.DeniedForever: return "perm." + k + "Blocked";
                case PermissionResult.Dismissed:
                case PermissionResult.Denied: return "perm." + k + "Denied";
                default: return null;
            }
        }
    }
}
