# Seeker local crowd check — 2026-10-05, build d

**Result:** the current baked city sustained a weighted **60.30 Unity update frames/s** over 155.187 sampled seconds after warm-up. This is an offline placeholder-crowd check, **not the final multiplayer/voice/character performance gate**.

- APK SHA-256: `962cceb3e80f4e0f6ce958329f7346b3cf83e15bb26cfc31e94c0ba414e48c11`; build log `client/Logs/quality-build-20261005d.log`. Android development/IL2CPP build, 0 errors, 29 warnings.
- Requested duration: 180 seconds. The first two FPS windows after crowd-ready were excluded; 31 complete steady windows remain. Startup/launch and final partial window are not included.
- Startup reported display refresh **30 Hz**; this is not evidence of 60 presented frames/s. Separate Android display/presentation validation is required.
- Seeker: 1200 × 2670 display, Android quality index 2, LOD bias 0.7. URP asset render scale **0.6**, AP multiplier 1.0; procedural animation thermal scale 0.5.
- Battery stayed at 44°C and 26%; Android PowerManager thermal status stayed **3 (severe)**. This is a hot-device result with reduced rendering settings.
- 62 registered procedural avatars, zero Animator avatars. **28–42 actors were LOD-enabled** across sampled windows; 20–34 were distance-culled. LOD-enabled does not mean all were inside the camera frustum.
- Synthetic local walkers reuse the current look and move directly between sampled points. They do not exercise server NPC decisions, obstacle avoidance, voice or room networking. They can cross authored obstacles; this is not a navigation acceptance test.
- Observed last-frame counters: **111–124 draw calls/batches**, **124,958–144,904 triangles**. These are sparse samples every five seconds, not whole-run maxima.
- Worst single-window slowest-1% mean: **45.2 fps**. Worst window p99: **16.6 ms**. Minimum individual-frame reciprocal: **30.2 fps**. Do not interpret window percentiles as a whole-run percentile. No dropped telemetry samples.
- End-of-run Android process memory snapshot: **673.4 MiB PSS / 767.9 MiB RSS**, plus ~0.1 MiB swap PSS. This is a snapshot, not peak memory or a leak test.
- Captured log scan found no `FATAL`, `SIGSEGV`, `Exception:` or render attachment errors. This is limited to the saved app-process log.

## Evidence and reproduction

- [Raw device log](screens/quality-perf-20261005d/device.log)
- [Machine-readable summary, including every window](screens/quality-perf-20261005d/summary.json)
- [End-of-run image](screens/quality-perf-20261005d/city-after.png) — captured after sampling stopped.
- [Memory snapshot](screens/quality-perf-20261005d/memory-after.txt)
- Thermal/battery before and after are stored in the same evidence folder.

```sh
python3 client/Tools~/seeker_review.py --seconds 180 --crowd 62 \
  --output /tmp/ct-life-crowd-review
```

The helper runs the installed development APK, unless an explicit `--install` path is supplied. It does not clear global logcat or establish server tunnels. It stops the test app after capturing evidence. See [metric definitions and limitations](performance-caveats.md).

## Remaining acceptance work

- Repeat with final atlas/materials, actual Humanoid rigs and proper animation LOD behavior.
- Complete the stronger spec test: 50 players + 12 server NPCs, voice on, all emoting, 10 minutes on Seeker.
- Measure cold-device normal render scale, actual presented-frame pacing, load/resume latency, peak memory and repeated-travel memory.
- Build e changes runtime fog range only; this record remains specifically tied to build d. A fresh build-e visual check follows.

## Separate build-e presentation check

The startup 30 Hz reading is not a steady-state display measurement. A separate build-e run recorded Android's active display mode at 120 Hz after launch; a later app SurfaceView rolling-buffer sample contained 127 valid actual-present timestamps over 2.105091154 seconds, equivalent to **59.85 presented frames/s**. Median interval 16.576 ms, maximum 33.160 ms. No device display preference was changed.

This short snapshot supports near 60 presentation in that moment; it does **not** convert the full build-d update-loop run into a presented-frame trace. Evidence: [presentation summary](screens/quality-presentation-20261005e/presentation-summary.json), [raw latency buffer](screens/quality-presentation-20261005e/latency.txt), and the display/SurfaceFlinger snapshots in the same folder. The second column is interpreted as actual-present time according to the opened [AOSP FrameTracker source](https://android.googlesource.com/platform/frameworks/native/+/cdb6b16dec3a541b455be99d075004cb2f0a0cd7/services/surfaceflinger/FrameTracker.cpp).
