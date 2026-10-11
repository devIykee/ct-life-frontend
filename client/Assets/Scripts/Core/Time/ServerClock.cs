using System;

namespace CTLife.Core
{
    /// <summary>
    /// Server time from the realtime `welcome {serverNow}` message (contract section 5).
    /// Used for `emoteAt` and NPC `startedAt`. Offset = serverNow − local UTC at receipt; the newest sample wins
    /// unless it implies a much larger delay (a late-delivered welcome), in which case the smaller offset is kept.
    /// </summary>
    public static class ServerClock
    {
        private static double _offsetMs;
        private static bool _synced;

        public static bool IsSynced => _synced;
        public static double OffsetMs => _offsetMs;

        public static double LocalNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>Epoch ms in server time.</summary>
        public static double NowMs => LocalNowMs + _offsetMs;

        public static void OnWelcome(double serverNowMs)
        {
            var sample = serverNowMs - LocalNowMs;
            if (!_synced || Math.Abs(sample - _offsetMs) < 2000 || sample > _offsetMs)
                _offsetMs = sample;
            _synced = true;
            Log.Debug("clock", $"offset {_offsetMs:0} ms");
        }

        /// <summary>Seconds from now until a server-time instant (negative if it already passed).</summary>
        public static float SecondsUntil(double serverEpochMs) => (float)((serverEpochMs - NowMs) / 1000.0);

        /// <summary>Seconds since a server-time instant.</summary>
        public static float SecondsSince(double serverEpochMs) => (float)((NowMs - serverEpochMs) / 1000.0);

        public static void Reset()
        {
            _offsetMs = 0;
            _synced = false;
        }
    }
}
