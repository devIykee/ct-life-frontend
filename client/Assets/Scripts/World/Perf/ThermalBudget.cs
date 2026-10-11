using System;

namespace CTLife.World
{
    /// <summary>Immediate thermal reductions; thirty continuous cooler seconds before recovery.</summary>
    public sealed class ThermalBudget
    {
        public const double RecoverySeconds = 30;
        public int Severity { get; private set; }
        private readonly bool _limitModerateFrameRate;
        public ThermalBudget(bool limitModerateFrameRate = false) => _limitModerateFrameRate = limitModerateFrameRate;
        public int FramesPerSecond => Severity >= 4 ? 5 : Severity == 3 ? 15
            : Severity == 2 || (_limitModerateFrameRate && Severity == 1) ? 30 : 60;
        public bool AllowScreenSleep => Severity >= 3;
        public int SignalLevel => Math.Min(2, Severity);
        private int _lastAndroid = -1;
        private int _recoveringTo = -1;
        private double _coolSince;

        public void InterruptRecovery() => _recoveringTo = -1;

        public bool Observe(int androidStatus, int adaptiveLevel, double now)
        {
            // A failed sensor read does not mean the device has cooled.
            if (androidStatus >= 0 && androidStatus <= 6) _lastAndroid = androidStatus;
            int androidSeverity = _lastAndroid >= 5 ? 4 : _lastAndroid == 4 ? 3
                : _lastAndroid == 3 ? 2 : _lastAndroid == 2 ? 1 : 0;
            int wanted = Math.Max(androidSeverity, Math.Clamp(adaptiveLevel, 0, 2));
            if (androidStatus < 0 && _lastAndroid >= 0 && wanted < Severity)
            {
                _recoveringTo = -1;
                return false;
            }
            if (wanted >= Severity)
            {
                _recoveringTo = -1;
                bool changed = wanted != Severity;
                Severity = wanted;
                return changed;
            }
            if (_recoveringTo != wanted)
            {
                _recoveringTo = wanted;
                _coolSince = now;
            }
            if (now - _coolSince < RecoverySeconds) return false;
            Severity = wanted;
            _recoveringTo = -1;
            return true;
        }
    }
}
