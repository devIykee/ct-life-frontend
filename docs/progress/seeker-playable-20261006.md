# Seeker playable landscape evidence — 2026-10-06

Build: `client/Builds/ctlife-playable-landscape-20261006.apk`, 173,353,746 bytes.
SHA256: `fd6abaa3fa72c1b6364704e5da92dfcb4c7f42d1ba58cc1337daeaa3f2bb177a`.
Unity 6000.3.25f1 Android development IL2CPP/ARM64 build succeeded in 177 seconds with
0 errors and 4 warnings. The prior APK was preserved. Installation succeeded; the measured
activity cold launch took 1,096 ms.

## Device workload and outcome

The actual Seeker ran the normal registered Timeline B2 Lamp prefab and camera at 2670 × 1200
with six authored synthetic local avatars, without networking or voice, for 180 seconds.
It retained the same process at completion. Warm-up exclusions left 33 complete windows and
165.379 sampled seconds, meeting the 160-second coverage requirement. Unity was closed during
this measurement.

- Weighted Unity Update average: 60.09 FPS; zero discarded frame samples.
- Worst five-second window 1% low: 30.1 FPS. Worst window 99th percentile: 33.2 ms.
  These are window summaries, not whole-run percentiles or Android presentation measurements.
- Six registered Animator avatars throughout. Visible LOD0 ranged from 1 to 6; some actors
  were outside the view.
- Render scale: 0.7. Adaptive Performance multiplier: 1.0.
- Sparse last-frame counters: 75–191 draw calls and 89,098–229,591 triangles. These are not peaks.
- Every sampled aggregate Android thermal status was 0. Battery temperature rose from 30.0°C
  to 31.0°C.
- Post-run memory snapshot: RSS 740,580 KiB (723.2 MiB), PSS 643,935 KiB (628.8 MiB).
  This is a single snapshot, not a peak or leak test.
- No fatal exception or signal marker in the retained app-scoped log; no process restart observed.
- The harness left CT Life open. This bounded offline run does not establish long-session,
  server or voice reliability.

A separate post-run physical display query reported 60 Hz in mode 3. The current game BLAST
layer's SurfaceFlinger latency history yielded 126 valid presentation timestamps across
2.074 seconds: 60.2696 presented FPS, with a median interval of 16.591 ms and maximum of
16.726 ms. This short follow-up sample does not certify presentation for the earlier 180 seconds.
AOSP identifies the second dumped column as `actualPresentTime` in
[FrameTracker.cpp](https://android.googlesource.com/platform/frameworks/native/+/master/services/surfaceflinger/FrameTracker.cpp).
The startup Unity 30 Hz log and secondary Swappy lookup messages are examined separately in
`ct-android-pacing-audit-20261006.md`. Neither prompted an unsupported package or settings change.

## Android touch routing and preview defect

Root injected an Android touchscreen swipe from (328, 962) to (482, 962) over 1,400 ms.
The stick engaged Run, the avatar moved right with camera follow, and the avatar returned
to idle with a centered stick on release. Before, during and after captures are retained.
This exercises Android input routing; it is not a human-finger assessment of how movement
feels. The same process remained alive.

Tapping the Phone button and Jobs icon opened their real screens. Jobs then displayed the
expected ReviewTransport connected-only rejection as a generic error and a development
console. This is a preview usability defect: installed practice games lacked an ordinary
phone entry. A guarded PracticeJobsApp with no API requests or payout is being staged to
provide real, unscripted games. This APK does not contain that repair. Normal connected Jobs
will retain its existing assignment and settlement flow. Root closed the phone panel and
development console, retained the app-scoped logs, and left the city open.

Evidence: `screens/seeker-playable-six-20261006/` and `screens/seeker-touch-20261006/`.
The full app log and display dump remain in `/tmp/ct-seeker-playable-six-20261006/`.
Unrelated display and system application identifiers were not copied into this report.

## Package size remains over budget

This development APK is 165.3 MiB (173.4 decimal MB), above the spec's 150 MB limit.
A release build has not yet been measured. The archive's three largest compressed native
components are IL2CPP (28.8 MB), Unity (15.5 MB) and LiveKit (9.2 MB); these numbers alone
do not identify removable code. The build log also includes SDK Resources art, both
characters' creator textures and the district audio library. Its asset table reports
uncompressed sizes and must not be subtracted directly from APK bytes. The exact archive
ranking is retained in `screens/seeker-playable-six-20261006/apk-size-audit.json`.
No package, art or audio was removed to make an untested size claim.


## Playable Android v2 — actual touch follow-up

The [playable landscape v2 APK](../../client/Builds/ctlife-playable-landscape-v2-20261006.apk) is installed on the Seeker. SHA-256: `e616bd525a4d7e42a3c71d91e922dc3fdb54029da19c01bcdfb9e41bf317d755`; size: 173,430,689 bytes (165.4 MiB). Unity reported success in 142 seconds, with 0 build errors and 2 build warnings. This development APK remains above the 150 MB spec; release size has not been measured. It adds the practice Jobs route, the 30-story Thread pool, Home parser/request ownership, PhoneShell disposal and a bounded Jobs catalog load. It does not include subsequent animation candidates or the newly found Thread scroll correction.

The 30-second, one-avatar cold-launch smoke stayed in the same process (PID 26925). Three steady windows covered 15.029 seconds at 60.28 weighted Unity update FPS. The worst window had a 1% low of 59.9 FPS and a p99 frame time of 16.7 ms, with no dropped samples. This short smoke is not a stress/performance certification. Aggregate thermal status was 0 during the measured launch run.

Actual ADB touch through the displayed Phone → Jobs route opened all three games. Mod accepted an Allow answer with visible correct feedback; Earnings accepted 300k revenue − 210k expenses = 90k profit. Their End controls returned to Jobs. Thread completed a full, normally randomized 50-second round with five distinct stories (23, 1, 13, 21, 18), all 15 fragment taps, five publishes and five Next/Finish actions. The 50/50 result was visually reviewed. This external screenshot/OCR driver knew the authored answer order, but did not seed the game, call reflection, mutate scores, invoke fixture factories or submit API requests. Replay and Return to Jobs worked. Home opened its actual four disabled unsigned-preview controls without the old array-index exception. No economic writes or XP/payout were attempted.

The first Thread touch probe correctly exposed a presentation defect: publishing incremented the visible tally, but its explanation remained below the composer viewport. Its feedback assertion failed; the game did not crash. Scoring was then tested separately against the visible tally and final result, and was not reported as a feedback pass. The correction was staged separately and is not present in this APK.

The interactive pass stayed at PID 26925. The retained app log contained none of the searched fatal, `NullReferenceException`, `ArgumentException`, `InvalidOperationException` or `[Error]` markers. Aggregate thermal status rose from 0 through 1 to 2 during continued interactive use. At the end, battery temperature was 36 °C, battery level was 27%, and USB was connected. Root intentionally sent Android HOME at status 2 and verified that the same process remained alive, to let the device cool during desktop work. This was an intentional background action, not an unexplained close or force-stop. It does not prove indefinite thermal stability or rule out every possible crash.

See the [safe device evidence](screens/seeker-playable-v2-20261006/). The full app log and external test helpers remain in `/tmp`; no unrelated app inventory was copied.


## Non-development size audit

A separate local non-development build succeeded in 295 seconds with zero build errors and three build warnings. `client/Builds/ctlife-release-size-audit-20261006.apk` is 139,294,433 bytes (139.3 decimal MB; 132.8 MiB), SHA-256 `0661b4271e1b976db43cd0b7bb4ed7a2e04a46089df523f43e0b849b8a47897b`. This is below the 150 MB requirement for the current registered content, with about 10.7 MB of decimal headroom. It does not include a registered Café and is not a final content-size or store-delivery certification. No asset quality was reduced for this measurement. The APK was neither installed over the interactive preview nor published. [Compressed-entry audit](screens/seeker-playable-v2-20261006/release-size-audit.json).


## Playable Android v3 — build only, cooling hold

The local v3 build succeeded in 273 seconds with zero errors and one BuildReport warning. `client/Builds/ctlife-playable-landscape-v3-20261006.apk` is 154,728,081 bytes (154.7 decimal MB; 147.6 MiB), SHA-256 `d08b4108f24c5a86f7b33ddfb1f405a60d4a4ec377fa578063511373eb96f94b`. As a development build, it still exceeds the 150 decimal MB requirement. It includes the Android moderate-heat 30 FPS policy, Thread explanation scrolling, practice result styling, Flow ownership cleanup, the seat-hit ancestry correction and removal of the unused legacy analytics dependency. The Café remains unregistered.

It is **not installed**. Read-only preflight found aggregate Android thermal status 1, battery temperature 36 °C, 27% charge and USB power. The stricter cool-start check required status 0 and refused the launch; v2 remained backgrounded without an install, restart or force-stop. Device effectiveness of the new heat policy and Thread feedback visibility remain pending. [Build/preflight receipt](screens/seeker-playable-v3-20261006/build-and-preflight.json).

The remaining build warning identifies Active Input Handling set to Both as unsupported on Android. A source/serialized-asset audit found no legacy Input usage or manually added legacy EventSystem modules, and the existing Input System package is version 1.20.0. A separate one-line setting correction is now integrated for native input checks and the next build, following Unity 6.3 UI Toolkit input guidance. It is not in v3 and is not evidence of crash or thermal causality.
