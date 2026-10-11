# Moderate thermal response — 2026-10-06

Recommendation: cap Android gameplay at 30 FPS when the existing policy reaches severity 1 (Android MODERATE or Adaptive Performance ThrottlingImminent), retaining 60 FPS when cool and the existing 30-second recovery delay. This is a proposed workload reduction supported by an actual device event; its effect on temperature and sustained play has not yet been measured. It is not evidence that this policy was the sole cause of the user's heat report.

## Actual evidence

At `10-06 21:50:44.525`, process 26925 logged `Thermal budget 1 (Android 2, AP 0) → targetFps 60, renderScale 0.7, allowSleep False` in `/tmp/ct-seeker-v2-touch/app-logcat.txt:840`. The [preserved interactive inputs](screens/seeker-playable-v2-20261006/inputs.jsonl) move from aggregate thermal status 0 to 1 to 2 during the one-avatar review. The first and last recorded input timestamps are 439.0 seconds apart; the recorded transition to 2 is after the first 0 sample, not a controlled measure of time from a thermally equilibrated launch.

The [preserved post-run thermal dump](screens/seeker-playable-v2-20261006/thermal-after.txt) records aggregate status 2 without a status override. Its current HAL readings include SKIN 40.338 °C/status 2, CPU/GPU 51.019 °C/status 0 and battery 36 °C/status 0. The same device's skin threshold table puts moderate at 40 °C and severe at 41 °C. These are device-reported sensor/threshold values, not an independent surface-temperature measurement or universal safety limits. The [battery dump](screens/seeker-playable-v2-20261006/battery-after.txt) records USB power, battery level 27% and temperature 360 tenths °C. The aggregate warning therefore deserves a response even though the coarser AP input remained 0; absence of CPU/GPU warning does not negate the recorded skin/aggregate warning.

The existing policy maps Android status 2 to severity 1, but `ThermalBudget.FramesPerSecond` returns 60 until severity 2. `PerfManager` also retains render scale 0.7 and animation scale 1 at severity 1. Consequently this recorded moderate event changes the signal/log, but not these three workload controls. The policy already polls Android every 5 seconds, preserves a previous known status after an unknown read, interrupts recovery on pause, and waits 30 seconds after the first lower-state observation before recovery. These behaviors are retained.

## Current primary guidance

[Android PowerManager](https://developer.android.com/reference/android/os/PowerManager#THERMAL_STATUS_MODERATE) defines status 2 as moderate throttling and status 3 as severe throttling. [Android's Thermal API guide](https://developer.android.com/games/optimize/adpf/thermal) recommends adjusting workload proactively, including frame rate or fidelity, rather than waiting for heavy throttling. It also warns that status implementations vary across devices and recommends validating status with thermal headroom. This patch responds to an observed positive status; it does not add headroom support or treat status 0 as proof of thermal headroom. That sensor-validation limitation remains open.

[Unity 6.3 targetFrameRate](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Application-targetFrameRate.html) documents frame-rate caps on Android as a battery/heat control. A requested rate should divide the current display refresh rate; 30 divides the previously observed 60 Hz mode. Actual presentation rate still requires measurement, and setting a target is not proof the renderer reaches it. [Unity 6.3 WarningLevel](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AdaptivePerformance.WarningLevel.html) says applications should adjust when ThrottlingImminent is reported. The installed AP package is 6.0.0 and the existing three-value mapping is unchanged; no package or API is added.

## Bounded candidate

Staged outside Assets in `art/gameplay-candidates/thermal-moderate-20261006/`. Three narrow patches: ThermalBudget's opt-in frame policy, PerfManager's Android-player constructor selection, and the existing Editor native wiring probe. Default policy remains unchanged; only `UNITY_ANDROID && !UNITY_EDITOR` enables the earlier frame cap. No ProjectSettings, platform permission, background behavior, rendering quality, economic API or gameplay timer is changed.

| Observed Android state / AP equivalent | Policy severity | Android target FPS | Render / animation scale | Screen sleep |
|---|---:|---:|---|---|
| None or light, no AP warning | 0 | 60 | 0.7 / 1 | Existing forced-awake gameplay |
| Moderate, or AP imminent | 1 | 30 | 0.7 / 1 | Unchanged |
| Severe, or AP throttling | 2 | 30 | 0.6 / 0.5 | Unchanged |
| Critical | 3 | 15 | 0.6 / 0.5 | System setting |
| Emergency / shutdown | 4 | 5 | 0.6 / 0.5 | System setting |

Escalation applies on the next observed signal/poll. Recovery retains the existing 30-second lower-state window; with 5-second polling, application occurs on the first qualifying subsequent poll. Recurrence, an unknown read during recovery or pause resets the recovery window. Recovery from severe to moderate still holds the 30 FPS cap while restoring moderate visual quality after the window. Legacy public signal levels are unchanged. The tradeoff is lower temporal resolution and responsiveness while under pressure; cool gameplay remains 60 FPS. A lower target does not imply half the heat or guarantee cooling.

## Verification and remaining acceptance

Executed 72 controlled assertions against the actual staged policy: Android/default tables, AP precedence, moderate response, exact recovery boundary, warning recurrence, unknown sensor and pause interruption, severe/critical/emergency escalation and recovery. The existing 27 thermal transition assertions also pass with the default policy. A negative control removing the moderate cap fails at Android status 2. Full actual World compilation passed with Android-development, Editor and production defines; full Editor source compiled using its actual native assembly references. No Unity process, native probe, ADB command or device workload was run by this agent.

The staged `ThermalBudgetReview.Check` extends the existing native API/restoration probe: explicitly selects the Android policy in the Editor, expects 30 FPS/legacy signal 1 at moderate with unchanged 0.7 render scale, animation 1 and screen sleep, verifies unknown-read retention and recovery, then runs existing severe/emergency/settings-restoration checks. Native acceptance is pending root's queue; simulated inputs do not establish real thermal efficacy.

After native wiring passes, root should verify an actual Android build from a documented starting temperature under the same interactive workload, recording aggregate status, current skin/battery sensors, policy changes, Update timing and a bounded actual-presentation sample. Moderate must request 30 FPS without further input, repeated warnings must not flap the cap, and a measured lower-state window must restore 60. Touch/game durations and original settings restoration also need confirmation. Do not deliberately prolong uncomfortable heat to obtain a pass. Development-build overhead, USB charging, world/UI workload, environment and other device activity were not isolated in the earlier run. Longer/crowded/voice sessions and indefinite thermal stability remain unestablished; the broader six-phase scope is unchanged.

## Native wiring acceptance

Root ran `CTLife.EditorTools.ThermalBudgetReview.Check` in the installed Unity 6000.3.25f1 Editor: [bounded receipt](screens/thermal-moderate-native-20261006/receipt.json), exit 0. Actual frame-rate, screen-sleep, render-scale and animation APIs passed the moderate 30 FPS / 0.7 / 1 state, desktop preservation, escalation, unknown-sensor retention, cool recovery and original-setting restoration. The probe injects Android policy signals; this is native API wiring evidence, not physical sensor or heat-efficacy acceptance. The patches are now integrated in live source. A post-change Seeker temperature/presentation measurement remains outstanding.
