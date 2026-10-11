using System;
using System.Globalization;
using CTLife.Core;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>
    /// "Shift done, collect your pay" (backend: POST /v1/shifts returns endsAt). Starting a shift schedules one local banner
    /// for its end; it posts through the Duo notification center (so Do Not Disturb and the Jobs app's own banner toggle
    /// apply) while the game screen is up, survives an app restart (kept in prefs), and is cancelled when the shift is
    /// collected. Pure scheduling (<see cref="Due"/>) is tested; the banner layer's 100 ms tick calls <see cref="Poll"/>.
    /// </summary>
    public static class ShiftReadyNotice
    {
        public const string AppId = "jobs";
        private const string Key = "ctlife.shift.ready";
        /// <summary>Opens an app by id (set where the phone is built); a banner tap opens Jobs.</summary>
        public static Action<string> Opener;

        private static long _dueMs = -1;
        private static bool _loaded;

        /// <summary>Schedule the banner for a started shift's end (UTC). A newer shift replaces an older one.</summary>
        public static void Schedule(DateTime endsAtUtc)
        {
            if (endsAtUtc == default) return;
            _dueMs = new DateTimeOffset(DateTime.SpecifyKind(endsAtUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            _loaded = true;
            Save();
        }

        /// <summary>The pay was collected (or the shift abandoned): no banner.</summary>
        public static void Cancel()
        {
            _dueMs = -1; _loaded = true;
            Save();
        }

        public static long DueMs { get { Load(); return _dueMs; } }

        /// <summary>Whether a scheduled banner should post now (and not one from a stale, long-gone shift).</summary>
        public static bool Due(long dueMs, long nowMs) => dueMs > 0 && nowMs >= dueMs && nowMs - dueMs < 6L * 3600 * 1000;

        public static void Poll(long nowMs)
        {
            Load();
            if (_dueMs <= 0 || nowMs < _dueMs) return;
            bool fresh = Due(_dueMs, nowMs);
            _dueMs = -1;
            Save();
            if (!fresh) return;
            DuoNotificationCenter.Post(new DuoNotification
            {
                AppId = AppId, ThreadKey = "shift-ready", Title = Strings.T("shift.ready.title"), Body = Strings.T("shift.ready.body"),
                Open = () => Opener?.Invoke(AppId),
            });
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var s = PlayerPrefs.GetString(Key, "");
                _dueMs = long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;
            }
            catch (Exception) { _dueMs = -1; }
        }

        private static void Save()
        {
            try
            {
                if (_dueMs > 0) PlayerPrefs.SetString(Key, _dueMs.ToString(CultureInfo.InvariantCulture));
                else PlayerPrefs.DeleteKey(Key);
            }
            catch (Exception) { }
        }
    }
}
