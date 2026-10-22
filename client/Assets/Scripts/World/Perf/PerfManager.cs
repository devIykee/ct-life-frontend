using System;
using CTLife.Core;
using UnityEngine;
using UnityEngine.AdaptivePerformance;
using UnityEngine.AdaptivePerformance.Provider;
using UnityEngine.Rendering.Universal;

namespace CTLife.World
{
    /// <summary>
    /// Seeker budgets (Build Spec "Performance budgets" and "Unity must-dos"): 60 fps cap (Android defaults to 30),
    /// render scale 0.8, and on a thermal warning drop render scale to 0.75 and halve animation rates.
    /// Android's full thermal status is polled even when Adaptive Performance is active, since its
    /// coarse warning level cannot distinguish severe, critical and emergency temperatures.
    /// </summary>
    public sealed class PerfManager : MonoBehaviour
    {
        public const int TargetFps = 60;
        // World v3 (2026-10-22): sharper on the Seeker: 0.8 cool, never below 0.75 hot (was 0.7 / 0.6, a soft image)
        public const float NormalRenderScale = 0.8f;
        public const float HotRenderScale = 0.75f;

        public static PerfManager Instance { get; private set; }
        public int ThermalLevel { get; private set; }

        private float _originalScale = -1f;
        private float _nextPoll;
        private bool _apActive;
        private int _lastAndroidThermalStatus = -1;
        private float _lastThermalPollAt = -1f;
        private int _adaptiveLevel;
#if UNITY_ANDROID && !UNITY_EDITOR
        // Reduce frame work at Android moderate / AP imminent, before severe pressure.
        // Cool gameplay stays at 60; recovery still needs 30 observed cooler seconds.
        private readonly ThermalBudget _budget = new ThermalBudget(limitModerateFrameRate: true);
#else
        private readonly ThermalBudget _budget = new ThermalBudget();
#endif
        private int _originalFrameRate, _originalSleepTimeout, _originalVSync;
        private float _originalAnimationScale;

        /// <summary>Observed sensor state is separate from the level last applied by adaptation.</summary>
        public string ThermalTelemetry()
        {
            if (_apActive)
            {
                var ap = Holder.Instance;
                bool known = ap != null && ap.Active && ap.SupportedFeature(Feature.WarningLevel);
                var warning = known ? ap.ThermalStatus.ThermalMetrics.WarningLevel : WarningLevel.NoWarning;
                string observed = known ? (warning == WarningLevel.Throttling ? "2" : warning == WarningLevel.ThrottlingImminent ? "1" : "0") : "unknown";
                return $"thermal={observed} thermalSource=AdaptivePerformance thermalRaw={(known ? warning.ToString() : "unknown")} androidThermalRaw={_lastAndroidThermalStatus} thermalApplied={ThermalLevel} thermalBudget={_budget.Severity}";
            }
            string level = _lastAndroidThermalStatus < 0 ? "unknown" : _lastAndroidThermalStatus >= 3 ? "2" : _lastAndroidThermalStatus == 2 ? "1" : "0";
            string raw = _lastAndroidThermalStatus < 0 ? "unknown" : _lastAndroidThermalStatus.ToString(System.Globalization.CultureInfo.InvariantCulture);
            float age = _lastThermalPollAt < 0 ? -1f : Time.unscaledTime - _lastThermalPollAt;
            return FormattableString.Invariant($"thermal={level} thermalSource=PowerManager thermalRaw={raw} thermalApplied={ThermalLevel} thermalBudget={_budget.Severity} thermalPollAgeSec={age:0.0}");
        }

        public static PerfManager Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("CTLife.Perf");
            DontDestroyOnLoad(go);
            go.AddComponent<FpsMeter>();
            return go.AddComponent<PerfManager>();
        }

        private void Awake()
        {
            Instance = this;
            _originalFrameRate = Application.targetFrameRate;
            _originalSleepTimeout = Screen.sleepTimeout;
            _originalVSync = QualitySettings.vSyncCount;
            _originalAnimationScale = AvatarLod.ThermalScale;
            AvatarLod.ThermalScale = 1f;
            Application.targetFrameRate = TargetFps;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            var urp = UniversalRenderPipeline.asset;
            if (urp != null)
            {
                _originalScale = urp.renderScale;
                urp.renderScale = NormalRenderScale;
            }
            else Log.Warn("perf", "No URP asset active; render scale not applied");

            var ap = Holder.Instance;
            if (ap != null && ap.Active)
            {
                ap.ThermalStatus.ThermalEvent += OnThermalEvent;
                _apActive = true;
                if (ap.SupportedFeature(Feature.WarningLevel))
                    _adaptiveLevel = WarningToLevel(ap.ThermalStatus.ThermalMetrics.WarningLevel);
                Log.Info("perf", "Adaptive Performance active: " + ap.Subsystem?.GetType().Name);
            }
            else Log.Info("perf", "Adaptive Performance not active; using PowerManager thermal polling");
            PollThermal();
            Log.Info("perf", $"targetFrameRate={Application.targetFrameRate} renderScale={(urp != null ? urp.renderScale : 1f)} screen={Screen.width}x{Screen.height}@{Screen.currentResolution.refreshRateRatio}");
        }

        private void OnDestroy()
        {
            var urp = UniversalRenderPipeline.asset;
            if (urp != null && _originalScale > 0f) urp.renderScale = _originalScale;
            if (_apActive && Holder.Instance != null) Holder.Instance.ThermalStatus.ThermalEvent -= OnThermalEvent;
            Application.targetFrameRate = _originalFrameRate;
            Screen.sleepTimeout = _originalSleepTimeout;
            QualitySettings.vSyncCount = _originalVSync;
            AvatarLod.ThermalScale = _originalAnimationScale;
            if (Instance == this) Instance = null;
        }

        private void OnThermalEvent(ThermalMetrics ev)
        {
            _adaptiveLevel = WarningToLevel(ev.WarningLevel);
            ApplyThermalBudget();
        }

        private static int WarningToLevel(WarningLevel warning) =>
            warning == WarningLevel.Throttling ? 2 : warning == WarningLevel.ThrottlingImminent ? 1 : 0;

        private void OnApplicationPause(bool paused)
        {
            // Time spent suspended is not a sequence of observed cooler sensor readings.
            _budget.InterruptRecovery();
            if (!paused) _nextPoll = 0f;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextPoll) return;
            PollThermal();
        }

        private void PollThermal()
        {
            _nextPoll = Time.unscaledTime + 5f;
            int status = AndroidThermalStatus();
            _lastAndroidThermalStatus = status;
            _lastThermalPollAt = Time.unscaledTime;
            ApplyThermalBudget();
        }

        private void ApplyThermalBudget()
        {
            if (!_budget.Observe(_lastAndroidThermalStatus, _adaptiveLevel, Time.realtimeSinceStartupAsDouble)) return;
            ThermalLevel = _budget.SignalLevel;
            var urp = UniversalRenderPipeline.asset;
            if (urp != null) urp.renderScale = ThermalLevel >= 2 ? HotRenderScale : NormalRenderScale;
            AvatarLod.ThermalScale = ThermalLevel >= 2 ? 0.5f : 1f;
            Application.targetFrameRate = _budget.FramesPerSecond;
            Screen.sleepTimeout = _budget.AllowScreenSleep ? SleepTimeout.SystemSetting : SleepTimeout.NeverSleep;
            Log.Info("perf", $"Thermal budget {_budget.Severity} (Android {_lastAndroidThermalStatus}, AP {_adaptiveLevel}) → targetFps {Application.targetFrameRate}, renderScale {(urp != null ? urp.renderScale : 1f)}, allowSleep {_budget.AllowScreenSleep}");
            Bus.Publish(new ThermalSignal { level = ThermalLevel });
        }

        private static int AndroidThermalStatus()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var pm = activity.Call<AndroidJavaObject>("getSystemService", "power");
                return pm.Call<int>("getCurrentThermalStatus");
            }
            catch
            {
                return -1;
            }
#else
            return -1;
#endif
        }
    }
}
