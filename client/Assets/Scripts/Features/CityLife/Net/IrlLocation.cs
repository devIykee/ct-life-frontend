using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace CTLife.CityLife
{
    /// <summary>One location fix for the opt-in IRL check-in, with the OS mock flag. Never stored, never logged.</summary>
    public readonly struct IrlFix
    {
        public readonly double Lat, Lng, AccuracyM;
        public readonly bool IsMock;
        public IrlFix(double lat, double lng, double acc, bool mock) { Lat = lat; Lng = lng; AccuracyM = acc; IsMock = mock; }

        /// <summary>"ok,lat,lng,acc,mock" from the Android plugin (CTLocation.java); null on anything else.</summary>
        public static IrlFix? Parse(string s, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(s)) { error = "pending"; return null; }
            var p = s.Split(',');
            if (p[0] == "err") { error = p.Length > 1 ? p[1] : "unavailable"; return null; }
            if (p[0] != "ok" || p.Length < 5) { error = "unavailable"; return null; }
            var inv = CultureInfo.InvariantCulture;
            if (!double.TryParse(p[1], NumberStyles.Float, inv, out var lat) || !double.TryParse(p[2], NumberStyles.Float, inv, out var lng) ||
                !double.TryParse(p[3], NumberStyles.Float, inv, out var acc)) { error = "unavailable"; return null; }
            if (lat < -90 || lat > 90 || lng < -180 || lng > 180) { error = "unavailable"; return null; }
            return new IrlFix(lat, lng, acc, p[4] == "true");
        }
    }

    /// <summary>
    /// The phone's position, once, when the player taps "Check in" (lane B, 2026-10-24): asks for the Android location
    /// permission at that moment (fine, falls back to coarse), then one fix from CTLocation.androidlib (android.location;
    /// fused on API 31+, GPS or network before). Errors: permission, disabled, timeout, unavailable, unsupported (editor).
    /// </summary>
    public static class IrlLocation
    {
        public static async Task<(IrlFix? fix, string error)> Once(CancellationToken ct, float timeoutSeconds = 20f)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation) && !Permission.HasUserAuthorizedPermission(Permission.CoarseLocation))
            {
                var answered = new TaskCompletionSource<bool>();
                var cb = new PermissionCallbacks();
                cb.PermissionGranted += _ => answered.TrySetResult(true);
                cb.PermissionDenied += _ => answered.TrySetResult(false);
                cb.PermissionDeniedAndDontAskAgain += _ => answered.TrySetResult(false);
                Permission.RequestUserPermissions(new[] { Permission.FineLocation, Permission.CoarseLocation }, cb);
                var t0 = Time.realtimeSinceStartup;
                while (!answered.Task.IsCompleted && Time.realtimeSinceStartup - t0 < 60f) await Awaitable.WaitForSecondsAsync(0.2f, ct);
                if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation) && !Permission.HasUserAuthorizedPermission(Permission.CoarseLocation))
                    return (null, "permission");
            }
            try
            {
                using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
                using var plugin = new AndroidJavaClass("com.ctlife.location.CTLocation");
                plugin.CallStatic("request", activity, (int)(timeoutSeconds * 1000f));
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < timeoutSeconds + 3f)
                {
                    await Awaitable.WaitForSecondsAsync(0.25f, ct);
                    var fix = IrlFix.Parse(plugin.CallStatic<string>("poll"), out var err);
                    if (err == "pending") continue;
                    plugin.CallStatic("clear");
                    Log.Info("events", fix.HasValue ? $"IRL fix (accuracy {fix.Value.AccuracyM:0} m, mock {fix.Value.IsMock})" : "IRL fix failed: " + err);
                    return (fix, err);
                }
                plugin.CallStatic("clear");
                return (null, "timeout");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { Log.Warn("events", "IRL location: " + e.Message); return (null, "unavailable"); }
#else
            await Task.Yield();
            return (null, "unsupported");
#endif
        }
    }
}
