# Seeker performance telemetry and caveats

## Current-source addendum — before the build-I authored crowd run

Updated 5 October 2026 after the build-H movement/audio review. No new sustained-performance result is asserted here. The build-D source findings and measurement history below remain historical; this addendum supersedes their registry/placeholder and authored-disabled-renderer findings for the current source.

- **The exact authored Snow look now resolves to a world prefab.** `CharacterRegistry.asset` maps `skin_tone3|head_snow|hair_snow_bun|top_vneck_charcoal|bottom_denim_light|shoes_sneakers_blue|acc_none` to `Assets/Art/characters/authored/Snow/EverydayWorld.prefab`, with the separate full-detail `Everyday.prefab` for creator previews. `defaultPlayer` remains null; the exact look entry supplies this model. This does not imply every possible look/archetype resolves to Snow.
- **World mesh LODs are present.** The authored Snow manifest records 55 bones. `AuthoredCharacterImport.ImportSnowLods` remaps the three skinned meshes to the same skeleton/material and validates bind matrices by bone name. The current world prefab has an `LODGroup` with screen-relative thresholds 0.28 / 0.08 / 0.02. Its mesh selection is driven by projected size and LOD bias, separately from the `AvatarLod` nearest-12/next-24 rank policy. Consequently, `lod0Enabled` / `lod1Enabled` / `lod2Enabled` counters are avatar policy tiers, not measurements of which authored mesh LOD rendered.
- **Initially disabled renderers are preserved.** `AvatarView.Init` records each renderer's initial `enabled` state, and `ApplyLod` applies `!culled && _rendererEnabledAtInit[i]`. The old finding that this loop indiscriminately re-enabled authored-disabled renderers has been corrected in current source. Enabled renderer counts still do not establish actual LODGroup/frustum submission or draw calls.
- **Humanoid evaluation is still full rate while enabled and visible to Animator culling.** `ApplyLod` applies half/quarter update intervals and thermal halving only to `ProceduralAnimator`. The real Animator receives `CullCompletely`, `enabled = !culled` and `speed = 1`; no deliberate half/quarter Humanoid evaluation scheduler is implemented here. The log's `animationThermalScale=0.5` therefore must not be described as halving authored Humanoid animation evaluation.
- **Use the next run's actual component counts.** The upcoming request is 62 registered local synthetic avatars using the current look. Confirm `animatorAvatars`, `proceduralAvatars`, distance-culling and render-eligible distributions from that run. The old build-D result used procedural placeholders and cannot establish performance parity with these authored assets.

Source evidence: [registry](../../client/Assets/Resources/Characters/CharacterRegistry.asset), [world prefab](../../client/Assets/Art/characters/authored/Snow/EverydayWorld.prefab), [character importer](../../client/Assets/Scripts/Editor/AuthoredCharacterImport.cs), [avatar state/LOD application](../../client/Assets/Scripts/World/Avatars/AvatarView.cs), [Snow source manifest](../../art/character-candidates/unity-snow/manifest.json).

### Current harness limits to retain in the next report

[seeker_review.py](../../client/Tools~/seeker_review.py) starts an offline city with `reviewCrowd`; it does not request movement checks or view captures during the timed interval. Memory and the final screenshot are collected afterward. It discards the first two FPS windows after the crowd-ready log and computes total frames / total `sampleSeconds` for the remaining complete windows. Report that actual sampled duration, not the requested 180 seconds as though all were measured.

- The workload is one local player plus 61 synthetic walkers sharing one current look. Walkers move directly between points and occasionally emote; they do not use NavMesh avoidance, room networking, server decisions or LiveKit. This is not the separate 10-minute, voice-on, 50-player-plus-12-NPC gate.
- `summary.json` requires only two steady windows and an intact logcat process. A logcat process remaining alive is not an app-liveness assertion; inspect the raw log and duration coverage. The summary records nonzero dropped samples rather than automatically failing them, and does not assert that all requested avatars remained registered. Verify those fields before describing a run as complete.
- Do not turn sparse last-frame draw/triangle ranges into peak-budget guarantees or average five-second percentile values into a whole-run percentile. Unity update FPS remains distinct from Android presented-frame pacing.
- Include observed thermal state, battery conditions, render scale/multiplier, LOD bias and actual avatar counts. End-of-run PSS/RSS is a snapshot, not peak memory or a leak test.
- The interrupted build-G attempt produced no valid summary; it must remain separate from the next completed run. The coordinator is keeping Unity/import work outside the upcoming device measurement interval.

## Historical build-D source inspection

5 October 2026. This records source inspection and the telemetry changes approved for build d. It does not report a completed sustained-performance pass.

## What this crowd run measures

- `reviewCrowd=62` creates one local player plus 61 local `CrowdWalker` avatars from the same current player look. It does not create 50 connected players and 12 server-driven NPCs. Voice and room networking are disabled in the review path. Source: `client/Assets/Scripts/App/Review/FrontendReviewController.cs`, `AddReviewCrowd` and `Travel`.
- The inspected registry has no `defaultPlayer` and an empty `looks` list; the modular parts folder has no base/parts assets. The current player-look path resolves to `PlaceholderHumanoid` plus `ProceduralAnimator`. Runtime `proceduralAvatars` / `animatorAvatars` counts now make that distinction visible instead of assuming which art was loaded. Sources: `client/Assets/Resources/Characters/CharacterRegistry.asset`, `CharacterRegistry.ForLook`, `ItemCatalog.TryResolve`, `AvatarFactory.Create`.
- These results can characterize the engine spike/current city with current temporary actors. They do not validate final imported character geometry, materials, textures, Humanoid animation, voice, realtime synchronization, server NPC behavior or appearance changes.
- The original engine spike asks for 62 placeholder avatars (`docs/spec/build-spec.md:511`). The later venue stress definition is stronger: 50 players plus 12 NPCs, all emoting, voice on, 10 minutes on Seeker (`docs/spec/build-spec.md:765`). Keep those claims separate.

## New log fields

Every complete measurement window reports:

| Fields | Meaning / limit |
| --- | --- |
| `avg`, `min`, `p1`, `p99ms`, `frames`, `sampleSeconds`, `windowSeconds` | Unity update-loop frame-time statistics over the current window, normally about 5 seconds. Not a GPU-only timing or Android presented-frame measurement. `p1` is the reciprocal of mean frame time for the slowest 1% of samples, with at least one sample. |
| `droppedSamples` | Invalid or buffer-overflow samples omitted from calculation. A run with drops needs investigation; the fixed buffer holds 1,200 samples per window. |
| `avatars`, `avatarsMetric=registered` | Non-null registered `AvatarView` instances. **Not visible or drawn avatars.** |
| `lod0Enabled`, `lod1Enabled`, `lod2Enabled` | Avatars assigned each tier and not distance-culled by `AvatarLod`. These are eligibility counts, not frustum/occlusion visibility. |
| `distanceCulled`, `lodPending` | Avatars hidden by the 25 m policy, or not yet assigned a tier. The first assignment can lag spawn by 0.25 seconds. |
| `renderersEnabled` | Enabled, active renderer components on registered avatars, including blob shadows. Renderer LODGroup and camera visibility remain separate. This is not a draw-call count. |
| `proceduralAvatars`, `animatorAvatars` | Presence of the procedural animation component versus an Animator. “Animator” alone does not certify a final Humanoid asset or complete animation set. |
| `qualityIndex`, `lodBias`, `renderScaleAsset`, `urpAdaptive`, `apRenderScaleMultiplier`, `animationThermalScale`, `targetFps`, `screen` | Current configuration alongside the FPS sample. Asset render scale is not claimed to be the final per-camera target size; URP may apply Adaptive Performance's multiplier. |
| `thermal`, `thermalSource`, `thermalRaw`, `thermalApplied` | Observed normalized sensor level (or `unknown`), its source/raw value, and the adaptation level last applied. A missing sensor is no longer reported as healthy level 0. AP support is checked for the WarningLevel feature before reading it. |
| `thermalPollAgeSec` | Age since the latest PowerManager poll attempt, not a guarantee of fresh underlying hardware data. Raw/normalized values are `unknown` after a failed poll. |
| `drawCallsLast`, `batchesLast`, `trianglesLast`, `countersMetric=last-recorded-frame` | Latest available native counter sample, **not window average/peak, scene-only totals or visible-character counts**. `warming` means no sample yet; `unavailable` means the current player did not expose/start the counter. Numeric zero is not substituted for missing counters. |

FPS uses invariant decimal formatting for log parsing. The old `avatars=` key remains, with an explicit metric label, to avoid changing its meaning silently.

## Telemetry lifecycle and API verification

- Counters are discovered from the current player's available profiler handles, started once per named metric and disposed when the meter is disabled. No new packages or unsafe C# block are required.
- Pause/focus changes discard the partial measurement window and skip the first resumed frame. These boundaries are excluded from steady-state metrics; do not claim the log includes resume latency. A separate resume/launch measurement is needed for that experience.
- A one-sample window is now reported. The old fewer-than-two-samples guard could omit a severe single-frame stall spanning the window. Zero-valid-sample windows explicitly warn.
- API signatures were checked in the installed Unity 6000.3.25f1 `UnityEngine.CoreModule.xml` and `UnityEngine.AdaptivePerformanceModule.xml`: ProfilerRecorder, available-handle discovery, description/category, sample count, last value, disposal, supported thermal feature and AP render multiplier. The local URP source applies the multiplier in `UniversalRenderPipeline.ApplyAdaptivePerformance`.
- Official matching documentation: [ProfilerRecorder](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html) and [GetAvailable](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable.html). Runtime availability must still be checked in the built player; an API existing does not mean every counter is exposed by every build.

## Remaining source findings — not changed by this pass

1. **Final-character LOD/animation behavior differs from placeholders.** `AvatarView.ApplyLod` swaps meshes only when `PlaceholderLods` exists. Half/quarter-rate updates and thermal halving affect `ProceduralAnimator.UpdateInterval`; a real Animator is only enabled/disabled and otherwise has speed 1. Imported prefabs may bring their own LODGroups, but this code does not implement their planned half/quarter animation evaluation. Re-test final assets and implement the intended budget before claiming parity.
2. **Culling reduces work.** Nearest 12 / next 24 / remaining avatars get tiers; nonlocal avatars beyond 25 m from the local player are disabled. A 62-registered-avatar sample can contain substantially fewer render-eligible avatars. `CrowdWalker` movement and `AvatarView.Update` continue for distance-culled actors; the workload reduction is mainly rendering/animation.
3. **Thermal adaptation changes quality.** Existing policy changes render scale 0.7 → 0.6 and procedural animation cadence only at normalized level 2. Level 1 does not apply that reduction. Thresholds were preserved. Every FPS interpretation must include the logged scale/multiplier/animation cadence.
4. **AP initialization/lifecycle remains imperfect.** PerfManager chooses AP versus PowerManager once in Awake. If AP starts later it stays on polling; if AP was already hot before subscription, observed state may differ from the last event-applied level. Observed and applied levels are now logged separately. Destruction restores render scale but does not reset static `AvatarLod.ThermalScale`; destroying/recreating the manager in one process after a hot state can retain a reduced animation cadence. Normal venue travel keeps the manager alive, but teardown/restart-in-editor deserves a separate check.
5. **Authored disabled renderers.** The pre-existing `ApplyLod` loop sets every cached renderer's `enabled` flag to `!culled`. That can re-enable an intentionally disabled authored renderer. This telemetry pass did not introduce or change that loop. Preserve authored state when improving the avatar implementation.
6. **Frame windows are not tagged by content.** FpsMeter persists across scene travel. Startup, loading, creator preview, phone UI and screenshot work can appear in a window. After crowd-ready, discard the first full window and record a city-only interval. For transition UX, analyze those windows separately rather than deleting bad frames from a claimed whole-session result.
7. **Five-second percentiles are not whole-run percentiles.** Do not average `p99ms`/`p1` values and call the result a 10-minute percentile. Whole-run average can be computed as total frames / total sampleSeconds; report the worst individual-window statistics or retain raw timings for a true run percentile.
8. **Latest-frame render counters are sparse observations.** A single sample every 5 seconds can miss a draw/triangle spike. Use profiler capture or buffered counter samples if validating a strict maximum budget. These counters are useful current observations, not proof that the under-150-draw-call/100–150k-triangle targets were held at every instant.
9. **Not all budgets are measured here.** This meter does not measure RSS/PSS, network throughput, audio/voice costs or actual presentation latency. Separate measurements are required for the spec's memory/network constraints.

## How to describe the next run

Use wording such as: “Current source city, 62 registered synthetic procedural avatars, offline/no voice, build configuration and logged LOD/thermal/render-scale distribution shown.” Include actual sample duration and observed render-eligible counts. Do not shorten that to “62 final characters at 60 fps.”

Do not mix measurements from the earlier build c with the new labelled telemetry without identifying the build. The root agent owns Unity compilation, device deployment and the sustained run; runtime counter availability and final measurements were pending when this note was written.
