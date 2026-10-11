using CTLife.Core;
using UnityEngine;
using UnityEngine.AdaptivePerformance;
using UnityEngine.Rendering.Universal;

namespace CTLife.World
{
    /// <summary>
    /// Seeker budgets (Build Spec "Performance budgets" and "Unity must-dos"): 60 fps cap (Android defaults to 30),
    /// render scale 0.7, and on a thermal warning drop render scale to 0.6 and halve animation rates.
    /// Thermal signal: Adaptive Performance 6.0 (Android provider) when it is initialised, else Android's
    /// PowerManager.getCurrentThermalStatus() (API 29+) polled every 5 s.
    /// </summary>
    public sealed class PerfManager : MonoBehaviour
    {
        public const int TargetFps = 60;
        public const float NormalRenderScale = 0.7f;
        public const float HotRenderScale = 0.6f;

        public static PerfManager Instance { get; private set; }
        public int ThermalLevel { get; private set; }

        private float _originalScale = -1f;
        private float _nextPoll;
        private bool _apActive;

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
                Log.Info("perf", "Adaptive Performance active: " + ap.Subsystem?.GetType().Name);
            }
            else Log.Info("perf", "Adaptive Performance not active; using PowerManager thermal polling");
            Log.Info("perf", $"targetFrameRate={Application.targetFrameRate} renderScale={(urp != null ? urp.renderScale : 1f)} screen={Screen.width}x{Screen.height}@{Screen.currentResolution.refreshRateRatio}");
        }

        private void OnDestroy()
        {
            var urp = UniversalRenderPipeline.asset;
            if (urp != null && _originalScale > 0f) urp.renderScale = _originalScale;
            if (_apActive && Holder.Instance != null) Holder.Instance.ThermalStatus.ThermalEvent -= OnThermalEvent;
        }

        private void OnThermalEvent(ThermalMetrics ev)
        {
            int level = ev.WarningLevel == WarningLevel.Throttling ? 2 : ev.WarningLevel == WarningLevel.ThrottlingImminent ? 1 : 0;
            SetThermal(level, $"AP {ev.WarningLevel} temp={ev.TemperatureLevel:0.00} trend={ev.TemperatureTrend:0.00}");
        }

        private void Update()
        {
            if (_apActive || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 5f;
            int status = AndroidThermalStatus();
            if (status < 0) return;
            // THERMAL_STATUS_NONE 0, LIGHT 1, MODERATE 2, SEVERE 3, CRITICAL 4, EMERGENCY 5, SHUTDOWN 6.
            SetThermal(status >= 3 ? 2 : status == 2 ? 1 : 0, "PowerManager status " + status);
        }

        private void SetThermal(int level, string why)
        {
            if (level == ThermalLevel) return;
            ThermalLevel = level;
            var urp = UniversalRenderPipeline.asset;
            if (urp != null) urp.renderScale = level >= 2 ? HotRenderScale : NormalRenderScale;
            AvatarLod.ThermalScale = level >= 2 ? 0.5f : 1f;
            Log.Info("perf", $"Thermal level {level} ({why}) → renderScale {(urp != null ? urp.renderScale : 1f)}");
            Bus.Publish(new ThermalSignal { level = level });
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
