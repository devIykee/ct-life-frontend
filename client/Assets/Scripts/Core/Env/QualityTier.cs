using System;
using System.Globalization;
using UnityEngine;

namespace CTLife.Core
{
    /// <summary>
    /// The global graphics tier (lane B, 2026-10-25). <b>High</b> is the game as tuned on the Seeker (Mali-G615, 8 GB).
    /// <b>Low</b> is for entry phones such as the Infinix X6517 (Unisoc SC9863A, PowerVR GE8322, 4 GB), where the island ran
    /// at 5-9 fps: there the GPU is the wall (the frame waits on present / FinishFrameRendering) with a heavy main thread
    /// behind it (the kit renderer, physics sub-steps). The tier only decides; World code applies it (LowTier, PerfManager,
    /// IslandAtmosphere, IslandRenderSettings, IslandInstanceRenderer, IslandTerrainCell, traffic and pedestrians).
    /// Choice: the player's setting (Settings → Graphics: Auto / Low / High), else automatic from SystemInfo.
    /// Dev builds also read the launch extra <c>quality=low|high|auto</c> and the knob extras <c>lowRs</c>, <c>lowDist</c>,
    /// <c>lowFar</c>, <c>lowFixed</c> for on-device bisection.
    /// </summary>
    public static class QualityTier
    {
        public enum Tier { High, Low }
        public enum Pref { Auto, Low, High }

        private const string PrefKey = "ctlife.graphics";

        /// <summary>Raised when the effective tier changes (settings); appliers re-read the knobs.</summary>
        public static event Action Changed;

        public static Tier Current { get; private set; } = Tier.High;
        public static bool IsLow => Current == Tier.Low;
        public static Tier Auto { get; private set; } = Tier.High;
        public static string AutoReason { get; private set; } = "";
        public static Pref Setting { get; private set; } = Pref.Auto;

        // ---- the Low tier's knobs (High uses the game's own values)
        public static float LowRenderScale = 0.6f;
        public static float LowHotRenderScale = 0.5f;
        /// <summary>Scales every kit (instanced props, buildings, trees) LOD and cull distance.</summary>
        public static float LowKitDistance = 0.45f;
        /// <summary>The kit renderer's building occlusion on Low (CPU to build vs draws saved).</summary>
        public static bool LowKitOcclusion = false;
        public static float LowFarClip = 1500f;
        public static float LowPropCull = 90f, LowVegetationCull = 200f, LowInteriorCull = 120f;
        public static int LowTrafficCars = 5, LowPedestrians = 6;
        /// <summary>Characters (by nearness, the local player included) that get the IK pass (feet, look-at, hands) on Low.</summary>
        public static int LowIkAvatars = 4;
        /// <summary>The 2026-10-27 main-thread pass (kit batch reuse on Low, the kit's per-prototype cell skip, IK for the
        /// nearest few on Low). Dev builds: launch extra <c>lowCpu=0</c> turns it off for before/after runs on one build.</summary>
        public static bool CpuPass = true;
        /// <summary>Low: the kit renderer reuses its batches every other frame / keeps the LOD cross-fade (dev bisection
        /// extra <c>lowReuse=0|1</c>; <c>lowFade=0|1|2</c> below).</summary>
        public static bool LowKitReuse = true;
        /// <summary>Low: which kit LOD switches keep the dithered cross-fade: 0 none, 1 all, 2 buildings only (the far views).</summary>
        public static int LowKitFade = 2;
        public static float LowLodBias = 0.4f;
        /// <summary>Physics step on Low (High keeps the project's 0.02 s): one step per frame at 24-30 fps instead of two.</summary>
        public static float LowFixedDelta = 1f / 30f;

        private static bool _init;

        /// <summary>Kit LOD/cull distance multiplier for the current tier.</summary>
        public static float KitDistance => IsLow ? LowKitDistance : 1f;

        public static void Init()
        {
            if (_init) return;
            _init = true;
            Detect(out var auto, out var reason);
            Auto = auto; AutoReason = reason;
            try { Setting = (Pref)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, 2); } catch (Exception) { Setting = Pref.Auto; }
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            switch (LaunchArgs.Get("quality"))
            {
                case "low": Setting = Pref.Low; break;
                case "high": Setting = Pref.High; break;
                case "auto": Setting = Pref.Auto; break;
            }
            Knob("lowRs", ref LowRenderScale);
            Knob("lowDist", ref LowKitDistance);
            Knob("lowFar", ref LowFarClip);
            Knob("lowFixed", ref LowFixedDelta);
            if (LaunchArgs.Get("lowOcc") is string occ) LowKitOcclusion = occ == "1";
            if (LaunchArgs.Get("lowCpu") == "0") CpuPass = false;
            if (LaunchArgs.Get("lowReuse") is string reuse) LowKitReuse = reuse == "1";
            if (LaunchArgs.Get("lowFade") is string fade && int.TryParse(fade, out int fadeMode)) LowKitFade = Mathf.Clamp(fadeMode, 0, 2);
#endif
            Current = Resolve(Setting, Auto);
            Log.Info("quality", $"tier={Current} setting={Setting} auto={Auto} ({AutoReason}) ram={SystemInfo.systemMemorySize}MB gpu='{SystemInfo.graphicsDeviceName}' cpu={SystemInfo.processorCount}x{SystemInfo.processorFrequency}MHz");
        }

        public static Tier Resolve(Pref setting, Tier auto) => setting == Pref.Low ? Tier.Low : setting == Pref.High ? Tier.High : auto;

        /// <summary>The player's choice in Settings: saved, applied at once.</summary>
        public static void Set(Pref p)
        {
            Init();
            Setting = p;
            try { PlayerPrefs.SetInt(PrefKey, (int)p); PlayerPrefs.Save(); } catch (Exception) { }
            var next = Resolve(p, Auto);
            if (next == Current) return;
            Current = next;
            Log.Info("quality", "tier=" + Current + " (setting " + p + ")");
            try { Changed?.Invoke(); } catch (Exception e) { Log.Exception("quality", e); }
        }

        private static void Detect(out Tier tier, out string reason)
        {
            tier = Classify(SystemInfo.systemMemorySize, SystemInfo.graphicsDeviceName, Application.isMobilePlatform, out reason);
        }

        /// <summary>Pure (tested): Low for mobile devices with at most ~4 GB of RAM or an entry-level GPU family.</summary>
        public static Tier Classify(int ramMb, string gpu, bool mobile, out string reason)
        {
            reason = "desktop";
            if (!mobile) return Tier.High;
            if (ramMb > 0 && ramMb <= 4600) { reason = "ram " + ramMb + "MB"; return Tier.Low; }
            if (WeakGpu(gpu)) { reason = "gpu " + gpu; return Tier.Low; }
            reason = "ram " + ramMb + "MB, gpu " + gpu;
            return Tier.High;
        }

        /// <summary>Entry GPU families: PowerVR Rogue GE/GM/GX6, Mali-4xx/T6xx-T8xx/G31/G51/G52/G57 MC1-2, Adreno 3xx/4xx/50x-51x/60x-61x.</summary>
        public static bool WeakGpu(string gpu)
        {
            if (string.IsNullOrEmpty(gpu)) return false;
            var g = gpu.ToLowerInvariant();
            if (g.Contains("powervr") && (g.Contains("ge8") || g.Contains("gm9") || g.Contains("gx6") || g.Contains("g6") || g.Contains("sgx"))) return true;
            if (g.Contains("mali"))
            {
                if (g.Contains("mali-4") || g.Contains("mali-t") || g.Contains("mali-g31") || g.Contains("mali-g51") || g.Contains("mali-g52")) return true;
                if (g.Contains("mali-g57") && (g.Contains("mc1") || g.Contains("mc2"))) return true;
            }
            if (g.Contains("adreno"))
            {
                int i = g.IndexOf("adreno", StringComparison.Ordinal);
                var digits = new System.Text.StringBuilder();
                for (int k = i + 6; k < g.Length && digits.Length < 3; k++) { if (char.IsDigit(g[k])) digits.Append(g[k]); else if (digits.Length > 0) break; }
                if (int.TryParse(digits.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 300 && (n < 520 || (n >= 600 && n < 620))) return true;
            }
            return false;
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        private static void Knob(string key, ref float v)
        {
            if (LaunchArgs.Get(key) is string s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f > 0f) v = f;
        }
#endif
    }
}
