#if UNITY_EDITOR
using System;
using System.Reflection;
using CTLife.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CTLife.EditorTools
{
    /// <summary>Native API wiring/cleanup check; does not simulate phone thermals or claim device performance.</summary>
    public static class ThermalBudgetReview
    {
        public static void Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PerfManager.Instance != null)
                throw new InvalidOperationException("Thermal wiring check requires an idle Editor without a PerfManager.");
            var pipeline = UniversalRenderPipeline.asset;
            int fps = Application.targetFrameRate, sleep = Screen.sleepTimeout, vsync = QualitySettings.vSyncCount;
            float scale = pipeline != null ? pipeline.renderScale : -1, animation = AvatarLod.ThermalScale;
            var root = new GameObject("Thermal API check") { hideFlags = HideFlags.HideAndDontSave };
            PerfManager manager = null;
            try
            {
                manager = root.AddComponent<PerfManager>();
                Call(manager, "Awake");
                Field("_adaptiveLevel").SetValue(manager, 0);
                Require(Application.targetFrameRate == 60, "Normal game budget must request 60 fps.");
                var desktopPolicy = (ThermalBudget)Field("_budget").GetValue(manager);
                desktopPolicy.Observe(-1, 1, Time.realtimeSinceStartupAsDouble);
                Require(desktopPolicy.FramesPerSecond == 60, "Desktop AP-imminent policy changed.");
                // Explicitly simulate the Android opt-in without changing Editor/player settings.
                Field("_budget").SetValue(manager, new ThermalBudget(limitModerateFrameRate: true));
                Field("_lastAndroidThermalStatus").SetValue(manager, 2);
                Call(manager, "ApplyThermalBudget");
                Require(Application.targetFrameRate == 30 && manager.ThermalLevel == 1, "Android moderate did not request 30 FPS with legacy level 1.");
                Require(AvatarLod.ThermalScale == 1f, "Moderate frame cap unexpectedly changed animation quality.");
                if (pipeline != null) Require(Mathf.Approximately(pipeline.renderScale, .7f), "Moderate frame cap unexpectedly changed render quality.");
                Require(Screen.sleepTimeout == SleepTimeout.NeverSleep, "Moderate state unexpectedly changed screen sleep.");
                Field("_lastAndroidThermalStatus").SetValue(manager, -1);
                Call(manager, "ApplyThermalBudget");
                Require(Application.targetFrameRate == 30, "Unknown sensor released moderate frame cap.");
                Field("_lastAndroidThermalStatus").SetValue(manager, 0);
                Call(manager, "ApplyThermalBudget");
                Require(Application.targetFrameRate == 30, "First cool reading bypassed recovery hysteresis.");
                foreach (var pair in new[] { (3, 30), (4, 15), (5, 5), (6, 5) })
                {
                    Field("_lastAndroidThermalStatus").SetValue(manager, pair.Item1);
                    Call(manager, "ApplyThermalBudget");
                    Require(Application.targetFrameRate == pair.Item2, "Incorrect native FPS request for Android status " + pair.Item1);
                    Require(manager.ThermalLevel == 2, "Legacy signal level must remain compatible.");
                    Require(AvatarLod.ThermalScale == .5f, "Throttled animation budget was not applied.");
                    if (pipeline != null) Require(Mathf.Approximately(pipeline.renderScale, .6f), "Throttled render scale was not applied.");
                    if (pair.Item1 >= 4) Require(Screen.sleepTimeout == SleepTimeout.SystemSetting, "Critical state retained forced screen wakefulness.");
                }
                Field("_lastAndroidThermalStatus").SetValue(manager, -1);
                Call(manager, "ApplyThermalBudget");
                Require(Application.targetFrameRate == 5, "Unknown sensor reset emergency budget.");
                var policy = (ThermalBudget)Field("_budget").GetValue(manager);
                policy.Observe(0, 0, Time.realtimeSinceStartupAsDouble - ThermalBudget.RecoverySeconds - 1);
                Field("_lastAndroidThermalStatus").SetValue(manager, 0);
                Call(manager, "ApplyThermalBudget");
                Require(Application.targetFrameRate == 60 && manager.ThermalLevel == 0, "Cool recovery did not restore the requested frame budget.");
            }
            finally
            {
                if (manager != null) Call(manager, "OnDestroy");
                UnityEngine.Object.DestroyImmediate(root);
            }
            Require(PerfManager.Instance == null, "Destroyed manager retained its singleton.");
            Require(Application.targetFrameRate == fps && Screen.sleepTimeout == sleep && QualitySettings.vSyncCount == vsync,
                "Native settings were not restored after manager teardown.");
            Require(AvatarLod.ThermalScale == animation, "Animation budget was not restored.");
            if (pipeline != null) Require(Mathf.Approximately(pipeline.renderScale, scale), "Pipeline scale was not restored.");
            Debug.Log("CTLIFE [thermal-review] PASS native frame/sleep/render/animation API wiring, Android moderate 30 FPS with unchanged moderate render/animation, desktop preservation, severe-to-emergency escalation, unknown sensor retention, cool recovery and teardown restoration. Simulated signal input; not device thermal/performance acceptance.");
        }

        private static FieldInfo Field(string name) => typeof(PerfManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        private static void Call(PerfManager manager, string name) => typeof(PerfManager).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
        private static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    }
}
#endif
