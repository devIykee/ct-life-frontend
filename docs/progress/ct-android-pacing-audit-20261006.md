# Seeker startup refresh / Swappy audit — 2026-10-06

[Inference] This report separates verified observations from the interpretation of the startup warning. **No setting/package change is supported by these two startup lines alone.** The later device evidence establishes60Hz operation at the end of this particular run. It does not locate the exact startup mode transition or certify every rendered frame over180seconds.

Read-only scope: inspected the root-owned `/tmp/ct-seeker-playable-six-20261006` receipts, current source/exported Gradle output, and current official Unity/Android sources. No Unity/ADB/device action, live source edit, package update, microphone or game API operation by this agent.

## What was actually observed

| Evidence | Meaning and limit |
|---|---|
| `device.log:19,36–40`: native loader successfully loads `libgame.so`; GameActivity finds it; version4.4.0 | The game bridge exists and loads. The later Swappy message does not establish a missing APK library. |
| `device.log:208–209`: Swappy wrapper initialization success | Local generated `UnitySwappyWrapper.cpp:24–78` assigns function pointers and returns true. This message alone does **not** prove successful `SwappyVk` initialization or presentation cadence. Its native log wrapper suppresses nonfatal Swappy logs, so absence of detailed native errors is also limited evidence. |
| `device.log:346,529`: two `SwappyDisplayManager` InMemoryDexClassLoader lookup errors for `libgame.so`, each followed by Starting looper thread | Auxiliary loader errors occur at startup. No matching fatal/JNI-implementation error appears in this log. Exact source path below explains why construction can continue. |
| `device.log:454`: `screen=2670x1200@30`, target60, renderScale0.7 | One sample inside `PerfManager.Awake`, line91, after target assignment at line69. No subsequent refresh-rate sample is logged. |
| `FpsMeter.cs:80–85`; root `summary.json` | Update/unscaledDeltaTime measurements, not actual presentation timestamps. Excluding two initial windows:33windows,165.379sampled seconds,60.09weighted Update fps; worst window1% low30.1fps and worst windowp99 33.2ms. Do not report perfect/stutter-free60fps. |
| `display-after.txt`: physical DisplayDeviceInfo mode3 / renderFrameRate60.000004; mode3=60fps | Later active display state is60Hz. Other stale/override fields also exist in this dump; do not select a convenient field without identifying which display record it describes. |
| `surface-latency-after.txt`:126valid actual-present timestamps,125intervals | Independently recalculated:2.074013462seconds,60.2696presented fps, median16.591153ms, max16.726077ms. This is a **separate end-of-run2.07second sample**, not full180second presentation coverage. Column2 is actualPresentTime in [AOSP FrameTracker](https://android.googlesource.com/platform/frameworks/native/+/refs/heads/main/services/surfaceflinger/FrameTracker.cpp#169). |

[Inference] The30Hz Awake sample is no longer representative of the later session. A startup display transition or an early cached value is plausible; the supplied logs cannot distinguish them. It is incorrect to call the phone currently30Hz on this evidence, and equally incorrect to claim the exact transition was measured. Android provides display-change callbacks for observing mode changes; frame-rate requests are subject to platform policy. [Android frame-rate guidance](https://developer.android.com/media/optimize/performance/frame-rate#basic-usage)

## Exact Swappy source path

The exported `unityLibrary/build.gradle:20–21` declares GameActivity4.4.0 and games-frame-pacing2.1.2. The manifest sets `android.app.lib_name=game`; generated FramePacing CMake links `games-frame-pacing::swappy_static` into `libswappywrapper.so`.

The official2.1.2 release links source commit `a29e855e6f39df2ba1ef83eb7897c257ee717d66`. At that revision:

- [SwappyDisplayManager.java, constructor lines77–109](https://android.googlesource.com/platform/frameworks/opt/gamesdk/+/a29e855e6f39df2ba1ef83eb7897c257ee717d66/games-frame-pacing/extras/src/main/java/com/google/androidgamesdk/SwappyDisplayManager.java#77) reads that manifest library name, tries `System.loadLibrary`, catches `Throwable`, logs its message and continues to query modes/start/register the display listener. No constructor return occurs in that catch.
- [JNIUtil.h, class loading lines111–241](https://android.googlesource.com/platform/frameworks/opt/gamesdk/+/a29e855e6f39df2ba1ef83eb7897c257ee717d66/src/common/JNIUtil.h#111) contains the internal InMemoryDexClassLoader path and explicitly registers native methods.
- [SwappyDisplayManager.cpp, lines85–101](https://android.googlesource.com/platform/frameworks/opt/gamesdk/+/a29e855e6f39df2ba1ef83eb7897c257ee717d66/games-frame-pacing/common/SwappyDisplayManager.cpp#85) supplies the native method table, loads the Java class and constructs it. These primary source files were fetched/read through Gitiles TEXT after the web renderer could not display the pinned pages.

[Inference] This particular message matches a tolerated secondary library-load attempt from the embedded Java class, while the main GameActivity bridge is already loaded. Call it a **source-explained recoverable lookup warning in this run**, not a proven globally harmless error or a documented Unity issue resolution. The source also has different display-manager/native paths by Android API level; this audit does not establish which complete internal Unity pacing path remained active merely from the wrapper success line. No official Unity issue specifically identifying this exact warning as a fixed regression was found in the targeted search.

## Version and decision checks

Current [Unity6.3 GameActivity requirements](https://docs.unity3d.com/6000.3/Documentation/Manual/android-application-entries-game-activity-requirements.html) recommend4.4.0 and warn against older major versions; the observed version matches. The installed [6000.3.25f1 release](https://unity.com/releases/editor/whats-new/6000.3.25f1) has no stated fix for this loader message. Its separate Vulkan timeout crash entry is not evidence of that crash here.

Current [AndroidX release notes](https://developer.android.com/jetpack/androidx/releases/games#games-frame-pacing-2.1.2) identify2.1.2's API33 presentation-time change;2.1.3 adds16KB-page support. Those notes do not identify the logged secondary library lookup as their fix. Do not manually upgrade the Unity-managed dependency as a response to this warning.

Unity's [targetFrameRate documentation](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Application-targetFrameRate.html) distinguishes Android's default30fps from an explicit request; current source requests60 and uses thermal budgets to lower it when needed. Android [frame-pacing documentation](https://developer.android.com/games/sdk/frame-pacing) distinguishes render-loop timing from compositor/display presentation and describes supported-refresh-rate selection. The current setup code requests optimized frame pacing (`ProjectSetup.cs:87`); this audit did not change or independently query the live Editor property.

Recommendation: retain the current configuration for this run. For the next development measurement, add periodic/current refresh metadata to the existing FPS receipt and capture presentation intervals over a longer bounded window, including resume/mode changes if that behavior is under test. Keep Update fps, current mode and presented cadence separately named. A new fatal JNI/native failure, sustained presentation mismatch or reproducible refresh-change fault would justify a focused integration investigation; the present evidence does not establish one.
