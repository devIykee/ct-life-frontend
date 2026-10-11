# Locomotion quality — native evidence, 6 October 2026

Current source/build status: root applied the shared `loco_walk` reference of 1.0 m/s with walk-only Foot IK and promoted the verified weight-only Rain correction. Build M4 completed (154 MiB, zero errors, 29 warnings). Continuous Seeker gait captures now cover both Rain and Snow: root/collider alignment, target speeds and release checks pass, while Snow walking still has sampled 24.1 mm sole penetration and Rain running has sampled 30.9 mm penetration. Rain's walking samples are within 3.9 mm of the floor. These are short diagnostic captures of the largest authored body renderer, not a performance result or approval of the full visible gait. The original run remains unchanged; the existing jog candidate also needs contact correction before any replacement. The historical measurements below retain their original configurations.

## Evidence and scope

- Device recording: [Rain stride](screens/quality-rain-20261006l2b/touch/rain-stride.mp4), 15.983 seconds, 1200×2670, 60 fps. [Overview](../../art/character-candidates/locomotion-quality-20261006/device-video/overview.jpg) samples two frames per second; it is insufficient to measure individual foot contacts by itself.
- Root executed [LocomotionQualityProbe.cs](../../art/character-candidates/locomotion-quality-20261006/LocomotionQualityProbe.cs), `CTLife.EditorTools.LocomotionQualityProbe.Capture`. [Native execution log](../../client/Logs/quality-locomotion-probe-20261006m.log).
- Four native cycles: 61 normalized phases each, including the repeated endpoint; actual Unity `Animator` and `SkinnedMeshRenderer.BakeMesh`, each model’s own valid Humanoid Avatar and current controller/override. No imported clip/controller changes. Speeds: walk 1.75 m/s, run 4.5 m/s. This isolates steady-state poses and does not simulate runtime transitions or NavMesh traversal.
- [Native data](../../art/character-candidates/locomotion-quality-20261006/native-current), [reproducible analysis](../../art/character-candidates/locomotion-quality-20261006/analyze_native_cycles.py), [numeric report](../../art/character-candidates/locomotion-quality-20261006/native-analysis.json).
- True native mesh side sheets: [Rain walk](../../art/character-candidates/locomotion-quality-20261006/renders-current/rain-loco_walk-side.png), [Rain run](../../art/character-candidates/locomotion-quality-20261006/renders-current/rain-loco_run-side.png), [Snow walk](../../art/character-candidates/locomotion-quality-20261006/renders-current/snow-loco_walk-side.png), [Snow run](../../art/character-candidates/locomotion-quality-20261006/renders-current/snow-loco_run-side.png). Eight phases per sheet: 0, 8/60, 15/60, 22/60, 30/60, 38/60, 45/60, 52/60. Geometry comes from native baked meshes, not a Blender retarget approximation. Neutral Blender preview lights/base-color materials differ from device presentation; these sheets establish pose/silhouette only.

## Walking: cadence is materially wrong

The existing `loco_walk` state uses `speed / 2.2`. With the measured 1.333333-second source cycle and actual 1.75 m/s movement, that gives a 1.67619-second cycle and 2.93333 metres of world travel. Native ankle excursion is only 0.640–0.645 m for Snow and 0.656–0.662 m for Rain.

Fit the central near-flat support interval independently on both ankle and toe joints: left phases 6–15, right phases 36–45. Those intervals avoid heel-strike and late toe-off. Least-squares local Z versus **original clip time** gives the reference speeds below. Averages include both sides and both landmarks, rather than treating a changing lowest-vertex centroid as a stationary material point.

| Own Avatar | Current reference | Measured candidate reference | Candidate cycle at 1.75 m/s | Current central-support fitted forward sliding |
|---|---:|---:|---:|---:|
| Rain | 2.2 m/s | 1.0464 m/s | 0.79726 s | 0.891–0.934 m/s |
| Snow | 2.2 m/s | 0.9392 m/s | 0.71558 s | 0.971–1.020 m/s |

[Inference] Setting the walking state to `speed / measuredReference` should greatly reduce this central support sliding. It does not correct all heel/toe-roll motion or establish a complete foot-lock solution. The native candidate run below must confirm the changed time mapping and unchanged pose/other states before production integration.

The intended minimal integration is state-specific walking cadence. `speed` must remain the **actual** movement speed; do not globally inflate that parameter, which also drives run speed and other systems. Keep `loco_run`, `react_run_away`, idle and gestures unchanged. Per-character controller state settings would retain the measured difference. A narrowly scoped shared reference of1.0 m/s is also a concrete candidate: the measured fits predict Rain central-support residuals −0.140..−0.044m/s and Snow +0.036..+0.143m/s at 1.75 m/s. This is a deliberate compromise versus the exact per-avatar references, with substantially less change to runtime plumbing. Native/device verification still applies.

## Foot-ground alignment is a separate issue

Measured minimum mesh sole heights in the current walk are −34.33 mm (Rain) and −30.87 mm (Snow), despite a correctly placed character root. Across the cycle, the lower of the two soles ranges from −34.33 to +14.40 mm for Rain, and −30.87 to −0.41 mm for Snow.

A constant upward root offset large enough to remove the deepest penetration would leave the nearest foot up to 48.74 mm above ground on Rain and 30.46 mm on Snow. It would also move already validated native idle feet. This is evidence against a blanket character-height offset. It is not proof that one importer flag will solve the problem.

The isolated candidate compares the existing imported motion with `AnimatorState.iKOnFeet` off/on. Unity’s [state Foot IK API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimatorState-iKOnFeet.html) controls whether foot IK is respected; [Humanoid IK documentation](https://docs.unity3d.com/6000.0/Documentation/Manual/InverseKinematics.html) describes explicit goal-based IK separately. The project’s pinned Unity 6000.3.25f1 `UnityEditor.xml` confirms this property and the in-memory controller APIs. No foot-raycast solver or new package was introduced.

## Running and footwear

The original map selects Quaternius UAL1 `Sprint_Loop` for `loco_run`; its 0.666667-second cycle at 4.5 m/s travels 3 m. Both soles are above 3 cm for 38/60 Rain phases and 37/60 Snow phases. The head-to-hip forward angle spans about 34–41° on Rain and 35–42° on Snow. These are measured characteristics of the current sprint, not a biomechanical pass/fail standard. The full cycle includes leg extension and real tuck/flight, so the single crouched screenshot is not grounds for replacing this clip blindly.

Rain’s native side sheets show triangular stretched surfaces projecting behind some shoes/ankles. Snow rendered with the same exporter does not show those projections. The character artist traced affected native vertices by exact atlas UVs to the canonical mesh and found inappropriate Head weight on ankle vertices: canonical vertex 30960 contains Foot.L 45.2636%, Head 43.0042%, Calf.L 11.7322% at Z 0.084 m. This is a verified skinning defect; its source correction belongs to the character artist. Cadence and root offsets cannot correct the head-driven triangle stretching.

A future clip override requires care: `react_run_away` shares the `loco_run.fbx` asset. Replacing that clip through `AnimatorOverrideController` would also change the reaction. Preserve state independence with a separate clip key/state motion if a later run change is justified.

## Isolated cadence/Foot IK verification — ready for root

Re-copy the diagnostic helper into the Editor location, then execute:

```text
CTLife.EditorTools.LocomotionQualityProbe.CaptureCadenceCandidates
```

It creates six **unsaved** one-state controllers: control (2.2 reference), calibrated, calibrated + Foot IK, on Snow and Rain. It records 61 phases/eight native meshes per cycle under `art/character-candidates/locomotion-quality-20261006/native-cadence`. Existing clips, own Avatars and exact creator meshes are reused. Original shared controller, character override and clip file hashes are checked after capture. It does not write production assets or run reactions.

Pinned Roslyn compile passed with zero errors; only the existing Newtonsoft netstandard 2.0/2.1 identity warning appeared. Native execution subsequently passed: [cadence log](../../client/Logs/quality-cadence-probe-20261006.log). All six candidates have 61 phases/eight exported meshes, and source hashes stayed unchanged. Every measured joint at equal phase is exactly unchanged by cadence alone. Device walking and start/stop transitions still require root’s actual capture.


## Native cadence / Foot IK result

| Candidate | Rain | Snow |
|---|---:|---:|
| Exact calibrated playback rate at 1.75 m/s | 1.6724007 | 1.8632880 |
| Exact calibrated physical cycle | 0.7972571 s | 0.7155809 s |
| Central-support maximum fitted slide after cadence alone | 0.0564 m/s | 0.0752 m/s |
| Worst sole depth, original / Foot IK | −34.33 / −5.40 mm | −30.87 / −26.47 mm |
| Phases with either sole >10 mm below plane, original / Foot IK | 37 / 0 of 60 | 39 / 30 of 60 |
| Both soles above 30 mm after Foot IK | 0 of 60 | 0 of 60 |
| Foot IK support-fit mean reference speed | 0.98447 m/s | 1.01566 m/s |

Foot IK changes lower-leg joint positions by up to 171 mm on Rain and 179 mm on Snow, so it is a real pose change and was visually reviewed. Upper-body pose/lean stayed unchanged. [Rain Foot IK sheet](../../art/character-candidates/locomotion-quality-20261006/renders-cadence/rain-loco_walk-calibrated-footik-side.png) and [Snow Foot IK sheet](../../art/character-candidates/locomotion-quality-20261006/renders-cadence/snow-loco_walk-calibrated-footik-side.png) show coherent support/swing postures. Rain’s known skin-weight spikes remain and are independent of this test.

The Foot IK trajectories cluster around the same reference speed on both models. **Recommended production candidate:** only the walking state uses `state.speed = 1f / 1.0f`, `speedParameter = "speed"`, `speedParameterActive = true`, and `iKOnFeet = true`. Run keeps its 4.5 m/s reference, and all other states remain unchanged. [Inference] Applied to the measured Foot IK trajectories, this common reference gives central-support fitted residuals of −0.0204..+0.1007 m/s on Rain and −0.0766..+0.0488 m/s on Snow at 1.75 m/s travel. The six-way native run sampled the exact per-avatar reference rates; this shared-reference residual is calculated from those same native phase trajectories, not a claimed additional device measurement.

Snow’s remaining trailing-foot/toe penetration is unresolved. Do not describe this as complete foot locking or all-grounding approval. Root placement remains unchanged; there is no global root-height lift. The current importer retains original root Y, `heightFromFeet = false`, and baked Y motion. Unity’s [6.3 root-motion documentation](https://docs.unity3d.com/6000.3/Documentation/Manual/RootMotion.html) describes the Feet root-Y projection especially for unbaked vertical root motion; no importer flag was changed on speculation.

## Rain weight-only before/after proof — initial diagnostic handoff

[RainWeightComparisonProbe.cs](../../art/character-candidates/locomotion-quality-20261006/RainWeightComparisonProbe.cs) is a separate diagnostic helper. Root copies it alongside `LocomotionQualityProbe` and executes:

```text
CTLife.EditorTools.RainWeightComparisonProbe.Capture
```

Wait for the artist’s frozen `art/character-candidates/rain-weight-correction/exports` bundle. The helper imports Creator/LOD1/LOD2 only into `Assets/Art/characters/authored/RainWeightDiagnostic/`; `-ctRainWeightSource` overrides the input directory. It reuses Rain’s own valid Avatar, metre/baked-axis settings and unchanged current walk/run/controller. Production models, registry, controller, shared clips and material assets are not modified.

Checks compare imported rest-position/UV multisets, triangle/vertex counts and per-bone bind matrices, and reject remaining Head weight below the knee region. It exports 48 native before/after meshes: three tiers × two motions × four equal phases × before/after, plus matching joint measurements under `native-rain-weights`. Protected production file hashes are checked afterward. This is intended to establish that weight correction changes the malformed surface while preserving skeleton motion; it does not authorize the candidate into the registry.

Pinned compilation passed with zero errors. The standalone compile linked the already imported diagnostic assembly, producing a duplicate probe-type warning in addition to the existing Newtonsoft reference warning. Native execution subsequently passed after the diagnostic null-handling correction described below; device proof remains pending.

## Follow-up: Snow forefoot and running — investigation in progress

The [Snow source audit](../../art/character-candidates/locomotion-quality-20261006/snow-sole-source-audit.json) traces low vertices from the native Foot IK meshes back to the canonical source by exact atlas UVs. Sampled points are correctly weighted to Foot/Toes. For example, source vertex19148 is 74.37% Toes.L and25.63% Foot.L; distal vertex16709 is100% Toes.L. Their rest sole heights are approximately0.15mm and2.00mm. No Head contamination occurs in these sampled points.

At the worst measured Snow phase37/60, the ankle is202mm above the root plane and the toe joint is57mm above it, yet the shoe surface reaches−26.47mm. [Inference] This points to the shoe/toe orientation during rolling support, rather than an incorrectly placed character root. The current evidence does not establish whether the remaining correction belongs to the Humanoid foot reference, the clip’s toe motion, or footwear-aware IK; no global lift or Avatar edit has been applied.

[FootContactProbe.cs](../../art/character-candidates/locomotion-quality-20261006/FootContactProbe.cs) is ready for root:

```text
CTLife.EditorTools.FootContactProbe.Capture
```

It records each model’s own `humanScale`, left/right `feetBottomHeight`, and Humanoid foot/toe reference transforms. It compares the current `Sprint_Loop` and the already available CC0 `Jog_Fwd_Loop` (`art/processed/animations/loco_jog.fbx`) with Foot IK off/on, using unsaved one-state controllers on both models. The jog imports only into `Assets/Art/characters/authored/LocomotionDiagnostic/`. Its clip import uses the same existing CTL animation-source Avatar as the already shipped sprint; Snow/Rain model Avatars remain their own. No production clip/controller changes are authorized by this diagnostic.

Output: `native-run-contact`, eight full native cycles plus foot-reference and toe-muscle-curve facts. The helper checks production clip/controller/registry hashes afterward. Pinned compilation passed with zero errors and the same diagnostic-link warnings noted above. Root subsequently executed all eight cycles successfully; the result is recorded below.

The weight-only Rain probe’s first native attempt passed the Creator rest/UV/bind comparison, then failed at diagnostic Animator creation: C# `??` did not respect Unity’s fake-null object semantics. The helper now uses explicit Unity `== null` checks and recompiled. This was a diagnostic-code error, not a failure of the artist’s candidate; root's successful retry is recorded below.

## Rain weight-only native acceptance

Root’s corrected-helper retry passed: [native log](../../client/Logs/quality-rain-weight-probe-20261006b.log), [rest/bind receipt](../../art/character-candidates/locomotion-quality-20261006/native-rain-weights/comparison.json), [posed-mesh analysis](../../art/character-candidates/locomotion-quality-20261006/rain-native-weight-analysis.json). Four equal phases on walk/run were sampled across Creator/LOD1/LOD2, before and after correction. Both sides used the same current controller (shared walk reference1.0 plus walk Foot IK; run unchanged), so cadence cannot explain the shape difference.

- All three tiers: rest geometry/UVs and triangle/vertex counts unchanged, bind-matrix maximum delta0, native sampled joint-position maximum delta0, and exported UV/index topology exactly identical. Maximum Head influence below65cm dropped from52.3–52.4% to0.
- Creator: maximum mesh-edge length with both endpoints below45cm fell from189.4mm to34.1mm across the eight sampled poses. Edge observations longer than100mm in that region fell from279 to0. These descriptive metrics identify the removed spikes; they are not a general mesh-quality threshold.
- LOD1/LOD2 retained some228mm edges across the length of the decimated shoes. Their actual posed silhouettes were inspected and showed intact shoe outlines without the earlier projections. A universal edge-length cutoff would incorrectly reject those legitimate coarse triangles.

Creator proof, **top row before / bottom row after**: [run](../../art/character-candidates/locomotion-quality-20261006/renders-weights/rain-run-before-after.png), [walk](../../art/character-candidates/locomotion-quality-20261006/renders-weights/rain-walk-before-after.png). After-correction tier checks: [LOD1 run](../../art/character-candidates/locomotion-quality-20261006/renders-weights/rain-LOD1-after-loco_run-side.png), [LOD1 walk](../../art/character-candidates/locomotion-quality-20261006/renders-weights/rain-LOD1-after-loco_walk-side.png), [LOD2 run](../../art/character-candidates/locomotion-quality-20261006/renders-weights/rain-LOD2-after-loco_run-side.png), [LOD2 walk](../../art/character-candidates/locomotion-quality-20261006/renders-weights/rain-LOD2-after-loco_walk-side.png).

Recommendation to root: promote the weight-only candidate for device testing. The observed ankle spikes and hip/waist pull are removed while the skeleton animation is unchanged. This accepts the specific weight correction on the sampled native poses; it does not certify every animation or device frame.

## Run probe outcome: reject broad Foot IK enablement

The native run-contact probe passed execution and recorded all eight cycles. State Foot IK, although helpful for this walk, makes the current sprint’s contact worse: both soles exceed30mm for52/60 Snow phases and56/60 Rain phases, compared with37/38 without it. The smaller of the two sole heights reaches270–284mm with Foot IK. The existing jog also exceeds30mm on both feet for48–54/60 Foot IK phases. **Do not enable Foot IK on run as part of the walking correction.**

Avatar facts show Snow’s `leftFeetBottomHeight` and `rightFeetBottomHeight` are0.091585m, exactly its rest ankle height above the source sole. Rain’s value is0.085484m. There is no measured constant sole-height discrepancy to justify a blanket Avatar or root offset.

An additional `FootContactProbe.CaptureToeNeutral` method uses an unsaved walk-clip copy with only the two Toe Up-Down muscle curves set to zero, compared against the unchanged original at equal phase, with walk Foot IK and a 1.0 reference. This isolates toe animation’s role; it does not propose shipping neutral toes. `LocomotionQualityProbe` records a null source hash for this unsaved clip instead of opening an empty asset path. Both sources compile; no production assets are changed. Root subsequently executed all four native cycles; the rejected result is recorded below.


## Physical running cadence and source integrity

[Run contact analysis](../../art/character-candidates/locomotion-quality-20261006/run-contact-analysis.json) fits each foot’s longest contiguous backward support segment, requiring mesh sole height between−80 and+30mm and both ankle/toe landmarks moving backward. Separate ankle and toe fits retain uncertainty from the rolling foot; these are not full foot-lock measurements.

- Current Sprint at1×: fitted contact speed6.4–7.7m/s across the two models. At the actual4.5m/s world speed this corresponds to roughly1.9–3.2m/s backward sliding during those short support intervals.
- Existing Jog at1×: Rain ankle/toe fits approximately4.0–5.3m/s, Snow4.3–4.9m/s. That is materially closer to actual movement. Jog’s measured head-to-hip forward angle is18–31°, compared with Sprint34–42°.
- Raw Jog still penetrates the plane by up to66mm on Rain and51mm on Snow. Its Foot IK variant floats through most phases. **The cadence/lean evidence makes Jog a candidate for further correction, not an approved replacement.** Keep production run and `react_run_away` unchanged until ground contact is addressed and the actual cycle is reviewed.

An [existing-animation round-trip audit](../../art/character-candidates/locomotion-quality-20261006/animation-roundtrip-audit.json) compared all sampled joint heads in the authored `anims_male.blend` work file with the current exported walk/run/jog FBXs. Maximum pose differences are below1µm; rest-head differences are below0.54µm. This rules out a material export-position corruption in these three files. It does not validate the artistic gait or retargeted shoe contact.

The Snow toe curve at its deepest sole phase37/60 is already close to neutral (`Left Toes Up-Down≈−0.011`); the same curve reaches−0.506 at phase30/60. Thus a neutral-toe test may worsen earlier contact rather than solve the worst phase. The isolated test remains useful to distinguish toe articulation from ankle/Avatar placement; do not assume zeroing that curve is a correction.

The existing Jog was also rendered from the actual native samples: [Snow side cycle](../../art/character-candidates/locomotion-quality-20261006/renders-run-contact/snow-loco_run-jog-original-side.png), [Rain side cycle](../../art/character-candidates/locomotion-quality-20261006/renders-run-contact/rain-loco_run-jog-original-side.png). These are eight equal phases with neutral diagnostic lighting. The Rain jog capture predates the weight correction; its ankle spikes are historical and do not show a regression in the promoted meshes. The jog's less extreme torso lean does not outweigh its unresolved ground-contact defect.

## Native toe-neutral result — rejected

Root's [native execution](../../client/Logs/quality-toe-probe-20261006.log) passed all four diagnostic captures. [Reproducible analysis](../../art/character-candidates/locomotion-quality-20261006/analyze_toe_neutral.py) and [numeric receipt](../../art/character-candidates/locomotion-quality-20261006/toe-neutral-analysis.json) compare 60 unique phases on each own Avatar at shared walking reference 1.0 m/s, actual speed 1.75 m/s and Foot IK enabled.

| Observation | Original toes | Neutral toes |
|---|---:|---:|
| Snow minimum sole height | −26.47 mm | −46.25 mm |
| Snow phases with either sole below −10 mm | 30/60 | 44/60 |
| Rain minimum sole height | −5.40 mm | −28.40 mm |
| Rain phases with either sole below −10 mm | 0/60 | 28/60 |

Measured joint positions are identical between the two versions; this does not mean terminal toe rotations are identical. At Snow's original worst phase 37/60, the left sole changes only from −26.47 to −26.77 mm. The deepest neutral-toe error shifts to phase 31/60. **Retain the original toe curves.** Existing toe articulation provides useful clearance, and neutralization does not address the late-roll problem. Snow's remaining contact issue stays open; no root lift, Avatar edit or new production motion was introduced.

## Continuous device gait sampler — source ready, device execution pending

[DeviceGaitSampler.cs](../../art/character-candidates/locomotion-quality-20261006/DeviceGaitSampler.cs) is development-only diagnostic source for root to copy into `Assets/Scripts/App/Review`. Its guard is `(UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD`. It observes the existing, continuously evaluated Animator after `Awaitable.EndOfFrameAsync`; it never calls `Animator.Update`, changes a clip/controller, forces a pose or changes LOD.

Per moving case, construct `new DeviceGaitSampler(player, "rain-direct-walk", venue.groundMask.value)`, call `SampleAfterEndOfFrame()` after each awaited end of frame, and call `WriteAndDispose(captureDirectory)` in `finally`. Use a separate label for run. Default cadence is 20 samples/second, clamped to 12–24, with an absolute 120-sample cap. JSON and CSV are written as `gait-<label>.json` and `.csv`; partial samples are preserved when the caller uses `finally`.

The sampler reuses one temporary baked mesh and preallocated vertex/clip lists. Rest-mesh weights select each shoe using combined mapped Foot/Toes influence above 0.5. The same vertex indices are retained throughout the case, including independent fixed material anchors where a Foot/Toes influence of at least 0.75 exists. Recorded data includes actual root position/velocity, independent root displacement speed, current/next state hashes and phases, transitions, clip/rate, ankle/toe positions and rotations, root-relative sole minima, and per-sole collider gaps. The lowest surface vertex can change between samples, so sliding analysis should use the fixed material anchors or bone trajectories, not differences between changing minima.

The sampled renderer is explicitly `AvatarView.Body`, the largest authored world renderer; this is not proof of the GPU-selected LOD. Mesh identity, vertex count, enabled/visible flags and declared avatar tier are recorded. The helper fails visibly if the body mesh changes or rest weights are unreadable. Both current authored importers retain readable meshes.

Unity's [BakeMesh documentation](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html) describes CPU skinning for the snapshot, and [GetVertices](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.GetVertices.html) supports a reused list. These signatures were also checked in the installed 6000.3.25f1 XML and compiled against its player API. The isolated compile passed with zero errors; only the response-file `/noconfig` warning appeared. **Do not run this sampler in crowd/performance captures:** it adds CPU skinning and three collider rays per recorded sample. Per-sample cost and real timestamps are included so its diagnostic timing is explicit. Actual device results remain pending.

## Audio review follow-up

Read-only review of root's revised `AudioPresentationReview.cs` confirmed that streamed playback progress uses `timeSamples`, duck checks inspect actual source gains at −9/−15/−6/0 dB, stinger expiry derives from the imported clip length, and `music_*` filtering excludes SFX/ambience sources on the shared GameObject. No additional blocking API issue was found in this scope. Native playback state and any listening assessment remain separate from this source review.

## M4 continuous Seeker gait — measured 6 October 2026

The coordinator captured both characters with the same M4 APK and real continuous Animator evaluation. Rain's [device log](screens/quality-review-20261006m4/device.log) records the exact tuple `skin_tone3|head_rain|hair_rain_ponytail|top_sleeveless_white|bottom_denim_fitted|shoes_canvas_ochre|acc_scarf_sky` at line 374. Snow's [device log](screens/quality-gait-snow-20261006m4/device.log) records `skin_tone3|head_snow|hair_snow_bun|top_vneck_charcoal|bottom_denim_light|shoes_sneakers_blue|acc_none` at line 357. Recorded Avatars are `RainAdult_CreatorAvatar` and `SnowAdult_CreatorAvatar`; controllers are `Rain Native Idle` and `AvatarController` respectively.

Raw records: [Rain walk JSON](screens/quality-review-20261006m4/quality-review-20261006m4/gait-direct-walk.json), [Rain run JSON](screens/quality-review-20261006m4/quality-review-20261006m4/gait-direct-run.json), [Snow walk JSON](screens/quality-gait-snow-20261006m4/quality-gait-snow-20261006m4/gait-direct-walk.json), [Snow run JSON](screens/quality-gait-snow-20261006m4/quality-gait-snow-20261006m4/gait-direct-run.json). Each adjacent CSV has the same row count and exactly matching timestamps and left/right sole gaps as its JSON.

Each commanded case lasts about 1.4 seconds along a selected 6 m clear NavMesh direction. The table's travel is the sum of root displacement between the first and last **recorded** samples; it excludes movement before/after those samples. Sampled path length and net displacement agree, so the captured root path is straight. All 95 recorded root and per-foot ground rays find horizontal paving at Y=0; root Y and root-to-ground gap are exactly 0 throughout. Agent base offset is −0.083333 m. The separate runtime grounding log reports the voxelized navigation plane at +0.0833 m. Both characters reach 1.75/4.5 m/s and the harness reports zero release drift.

“Settled” below requires the requested locomotion state, no Animator transition, actual speed within 0.01 m/s of target, and root-forward dot movement direction above 0.999. This excludes the initial turn, acceleration and idle/walk/run blends. It is an analysis filter, not a new runtime rule.

| Character / case | Records / settled | First-to-last sample span | Sampled root travel | Settled phase-fit cycle | Settled lowest sole gap | Largest gap of the nearer sole |
|---|---:|---:|---:|---:|---:|---:|
| Rain walk | 24 / 20 | 1.3305 s | 2.2397 m | 0.7630 s | −3.91 mm | +15.33 mm |
| Rain run | 24 / 16 | 1.3597 s | 5.4187 m | 0.6661 s | −30.94 mm | +164.05 mm |
| Snow walk | 24 / 20 | 1.3793 s | 2.2991 m | 0.7621 s | −24.12 mm | +6.43 mm |
| Snow run | 23 / 15 | 1.3220 s | 5.2686 m | 0.6654 s | −5.54 mm | +174.33 mm |

The fitted periods come from least-squares unwrapped normalized phase versus real timestamp. They closely match the intended walk cycle of 0.76190 s (`1.33333 / 1.75`) and run cycle of 0.66667 s (`0.66667 / 1`). Rain's settled capture spans 1.437 walk cycles and 1.293 run cycles; Snow spans 1.458 and 1.293. This verifies the cadence actually applied during this capture, not comprehensive foot locking.

Rain walk has no >10 mm sole penetration in any of its 24 samples. Snow walk has 10/20 settled samples below −10 mm (13/24 including transition samples). Both walks always have at least one sampled sole within 30 mm of the ground. Rain run has one settled sample below −10 mm and both soles above 30 mm in 9/16 settled samples; Snow run has no settled sample below −10 mm and both above 30 mm in 10/15. Running includes genuine flight, so these flight counts are descriptive, not automatic failure criteria. Snow's run does have a −14.98 mm sample during the startup idle/walk blend.

**Do not interpret Snow's smaller sampled run error as a correction.** Its settled phase grid jumps from 0.014 to 0.114 and misses the isolated probe's deepest phase, 0.0667 (−32.32 mm). The shorter capture and irregular sampling cannot establish the whole-cycle minimum. Rain's device −30.94 mm occurs at phase 0.039, confirming that a real contact error remains despite correct root placement.

For comparison with the 61-phase isolated probes, each device phase was bracketed between adjacent offline phases, with linear interpolation of the two ankle and two toe root-space positions. Both walks agree closely: median landmark error is about 0.33 mm and maximum error is 1.46 mm; interpolated sole gaps differ by at most 1.16 mm. Thus the steady walking findings are reproduced during continuous real Animator playback. Run median landmark errors are 0.77 mm on Rain and 1.15 mm on Snow, with maxima 7.82/12.19 mm; fast nonlinear motion and the 1/60-cycle interpolation grid limit that comparison. Snow's interpolated run sole difference stays within 3.57 mm. The older Rain run-contact mesh predates the weight correction, so its sole-surface differences (up to 28.17 mm) are not a clean runtime-versus-editor comparison; the skeleton comparison remains useful because the correction preserved bones.

Viewed actual captures: [Rain walk](screens/quality-review-20261006m4/quality-review-20261006m4/direct-walk.png), [Rain run](screens/quality-review-20261006m4/quality-review-20261006m4/direct-run.png), [Snow walk](screens/quality-gait-snow-20261006m4/quality-gait-snow-20261006m4/direct-walk.png), [Snow run](screens/quality-gait-snow-20261006m4/quality-gait-snow-20261006m4/direct-run.png). Rain's earlier long ankle projections are absent in these inspected frames. The running captures show forward torso lean and a tucked phase, consistent with the cycle sheets; a screenshot alone does not establish contact duration or gait quality. Screenshots are captured after sampler export, so they are not the exact last JSON sample.

### Sampling costs and limits

The requested 20 Hz is an upper sampling rate, not a promised uniform grid. Actual first-to-last rates are 16.64–17.29 samples/s, with intervals 50.02–68.51 ms. No case reaches the 120-record cap. Sampling work uses CPU skinning and three collider rays, and includes other recorder work inside its stopwatch:

| Case | Median recorder elapsed time | Sample p95 | Maximum | Sum over case |
|---|---:|---:|---:|---:|
| Rain walk | 4.476 ms | 6.293 ms | 6.554 ms | 103.61 ms |
| Rain run | 4.376 ms | 5.212 ms | 5.421 ms | 103.90 ms |
| Snow walk | 1.763 ms | 2.462 ms | 4.445 ms | 44.04 ms |
| Snow run | 1.568 ms | 2.064 ms | 2.170 ms | 36.98 ms |

These are **sampler elapsed timings**, not game frame-time percentiles, FPS or a crowd/performance claim. Constructor allocations and JSON/CSV export are outside the per-sample stopwatch. Rain's measured body has 48,200 vertices and Snow's 24,779; the recorder's different workloads also limit cross-character comparisons.

Every sample says the selected `AvatarView.Body` is enabled but `bodyVisible=false`. The visible avatar appears in the screenshots, while this recorder deliberately bakes the largest authored renderer. Therefore the measurements establish its continuously animated surface and shared skeleton, not the exact submitted LOD surface. Sole minima can change vertex identity between phases. Fixed material anchors are recorded, but are not guaranteed planted contact points; the short data set does not justify a complete sliding-rate claim from them. Slopes, sustained turns, collisions, release blending and long-run behavior remain outside these straight, short cases.

The next corrective candidate should preserve the verified geometry/rest rig and target Snow walk's late forefoot penetration plus Rain run contact. Root-height lifting, toe neutralization and broad run Foot IK are rejected paths. A separate bounded tier-sampling improvement can measure every authored LOD at the same few diagnostic phases; it should not replace motion correction with more instrumentation work.


## Isolated contact candidate — source ready, native result pending

[FootContactCandidate.cs](../../art/character-candidates/locomotion-quality-20261006/FootContactCandidate.cs) prepares a Snow walk and Rain run candidate using their current own Avatars and **every authored world LOD**. It retains the source body/root/upper-body curves. At 121 closed-loop phases it measures the actual shoe surface, lifts only a penetrating foot target, and solves that leg without stretching. Foot world orientation, toe articulation and the original knee bend side are retained. The positive correction has a 4 mm continuously differentiable blend band; at an exactly grounded source sole it requests 1 mm of clearance. Corrections above the observed-defect bounds (40 mm Snow / 50 mm Rain) fail. These are isolated experiments, not accepted motion.

Root copies the helper into `Assets/Scripts/Editor` and runs:

```text
CTLife.EditorTools.FootContactCandidate.BuildCandidates
```

The helper first reconstructs an **uncorrected** clip from native leg muscles and requires its joints and soles to reproduce the original within 2 mm. This catches Humanoid conversion/Foot IK differences before judging a corrected candidate. Snow's existing walk Foot IK is sampled into the curves; both generated candidates are evaluated with state Foot IK disabled. A later integration must preserve that state-specific setting. No production controller, registry or source FBX is changed. In particular, Rain's run candidate must not accidentally replace `react_run_away`, which currently shares the source run clip.

Each candidate is then checked at 241 phases: penetration no deeper than 2 mm, corrected contact no higher than 8 mm, flight sole changes no more than 2 mm, hip/upper-body and foot/toe horizontal changes no more than 2 mm, foot/toe rotation changes no more than 1 degree, and no more than two additional/lost both-feet-above-30-mm phases. The original running cadence and sliding remain explicitly unresolved; the correction may not change support ankle horizontal velocity by more than 0.4 m/s. Per-tier gaps and correction phase windows are recorded.

The leg-muscle curves use equal endpoint values and periodic cubic tangents. Native foot/toe loop position must close within 1 mm, and the one-sided velocity mismatch at the wrap may not exceed the original by more than 0.1 m/s. This checks the actual reconstructed pose, beyond the mathematical curve closure. No discontinuous height clamp is applied to runtime motion.

Outputs are `native-contact-candidate/candidate-receipt.json`, dense before/round-trip/candidate pose JSON, and eight equal-phase before/after mesh exports for every tier. Mesh proof is retained even if numeric acceptance fails. Only a passing case saves unreferenced staging `.anim` assets under `Assets/Art/characters/authored/LocomotionCandidate/`, named `Snow_WalkContact_{Roundtrip,Candidate}` and `Rain_RunContact_{Roundtrip,Candidate}`. Production file SHA checks run afterward. Existing staging assets are not evidence for the current run: the receipt's `all_pass` and per-case result are authoritative.

The installed Unity 6000.3.25f1 APIs were checked locally, with primary references for [GetHumanPose](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/HumanPoseHandler.GetHumanPose.html), [MuscleName](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/HumanTrait.MuscleName.html) and [SetEditorCurve](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AnimationUtility.SetEditorCurve.html). Isolated Roslyn compilation passed with zero errors after disambiguating Unity's and .NET's `CompressionLevel` enum; the response-file `/noconfig` warning remains. Native execution, rendered pose inspection and continuous device verification are still pending.


### First contact candidate native result — rejected

Root executed [the native helper](../../client/Logs/quality-contact-candidate-20261006.log). Both cases failed the uncorrected reconstruction gate; [the failed receipt](../../art/character-candidates/locomotion-quality-20261006/native-contact-candidate/candidate-receipt.json) and before/round-trip/candidate meshes are retained. No staging clip was published or assigned.

Snow's leg-muscle-only round-trip has an 18.54 mm maximum joint error and 8.71 mm sole error. At phase 0.24583 its hips/chest/head/hands all shift together by approximately (−4.01, −5.29, −17.31) mm. Rain has a 20.13 mm knee error despite its smaller 0.634 mm sole error. The corrected Rain candidate moves a foot/toe horizontally by up to 102 mm. These are unacceptable conversion effects; lowering the reconstruction tolerance would not address them.

[Inference] The whole-body translation and joint mismatch are consistent with mixing reconstructed native muscle values with the original clip's body/IK-goal curves, rather than preserving the complete Humanoid pose representation. Unity's [root-motion documentation](https://docs.unity3d.com/6000.3/Documentation/Manual/RootMotion.html) distinguishes the body mass center from the root and stores muscle/IK goals relative to that body transform. A subsequent isolated trial should preserve the original muscles/body and test a minimal foot-goal translation, with a measured small-displacement response before relying on units or orientation. The current failed candidate is not a motion improvement.


### Foot-goal trial — separate source ready

The failed first helper and `native-contact-candidate` outputs remain unchanged. [FootGoalCandidate.cs](../../art/character-candidates/locomotion-quality-20261006/FootGoalCandidate.cs) is the separate trial. Root copies this new file into Editor and runs `CTLife.EditorTools.FootGoalCandidate.BuildGoalCandidates`. It writes `native-contact-goal-candidate`; its staging filenames add `_Goals` before `_Roundtrip`/`_Candidate`.

It first measures a 1 mm upward foot-goal request separately on both feet, with Foot IK off and on. The selected production setting (Snow walk on, Rain run off) must produce the requested ankle Y displacement within 0.25 mm, keep horizontal drift within 0.25 mm, and keep upper-body displacement within 0.25 mm. The alternative setting is recorded, not silently selected. Missing target bindings or an incorrect measured response reject the approach before a corrected clip is saved.

The candidate preserves all original muscle/body/rotation curves and changes only `LeftFootT.xyz`/`RightFootT.xyz`. The conversion from world-Y clearance to body-relative goal coordinates uses the sampled own Avatar body orientation and human scale; **that conversion is unverified until the native response test passes**. The original Foot IK setting stays enabled/disabled exactly as before. An unchanged clone must reproduce the original, and the same dense contact, upper-body, foot orientation, horizontal displacement, flight and periodic-position/velocity gates apply to the corrected clip. Run cadence remains unchanged. Isolated pinned compilation passed with zero errors and the response-file `/noconfig` warning.


### Foot-goal native result — Snow accepted numerically, Rain response rejected

The coordinator's `native-contact-goal-candidate/candidate-receipt.json` records an individual Snow pass and Rain failure; the aggregate run is therefore **not** an all-case pass. Snow's unchanged clip clone reproduces all sampled joints/soles exactly. A 1 mm target-Y request produces 0.999987/1.000226 mm ankle motion with Foot IK on and no upper-body movement. With Foot IK off, those curves produce zero ankle displacement.

Across 241 phases and all three Snow world tiers, the corrected walk reaches a minimum sole height of −0.476 mm and a maximum corrected support gap of 3.979 mm. Upper-body position and measured foot/toe rotation changes are zero; maximum foot/toe horizontal change is 1.059 mm. Flight sole changes are at most 0.027 mm, no both-feet flight phases are added, and support ankle velocity changes by at most 0.167 m/s. The loop position closes exactly. One-sided loop velocity mismatch improves from 2.145 to 1.537 m/s, but remains nonzero; this does not establish a perfectly smooth physical loop.

Actual native mesh proof: [original body](../../art/character-candidates/locomotion-quality-20261006/renders-contact-goals/snow-original-side.png), [candidate body](../../art/character-candidates/locomotion-quality-20261006/renders-contact-goals/snow-candidate-side.png), [original feet with ground line](../../art/character-candidates/locomotion-quality-20261006/renders-contact-goals/snow-original-feet-groundline.png), [candidate feet with ground line](../../art/character-candidates/locomotion-quality-20261006/renders-contact-goals/snow-candidate-feet-groundline.png). Eight equal-phase actual tier-zero meshes were inspected; foot articulation and upper-body pose remain visually consistent. The coordinator is preparing a separate Snow-only override promotion and device check; this candidate generator did not assign production motion.

Rain's current IK-off run ignores the foot-goal translation, so it failed the response gate before candidate publication. The IK-on response follows the same 1 mm request, but the old unmodified IK-on run floats. A separate `RainFootGoalRebase.cs` trial is prepared to measure quaternion response, then reconstruct the entire original IK-off foot pose with rebased translation/rotation goals under IK on. It must reproduce every tracked joint and sole within 2 mm and foot/toe rotations within 1 degree **before** adding any support correction. Original body/muscle curves and cadence remain unchanged. Baseline rejection keeps production run unchanged. Native execution of that rebase trial remains pending.


The Snow receipt's 3.979 mm corrected-support metric uses the minimum sole across tiers for each foot and applies only where the original foot was within 4 mm of the plane. It is **not** a full-cycle hover ceiling. Evaluating each tier's own support samples gives maxima of 3.798 / 3.979 / 4.303 mm (tiers 0/1/2). Across the entire cycle, the nearer-sole maxima are 9.818 / 9.788 / 10.069 mm, effectively unchanged from the original 9.817 / 9.787 / 10.069 mm. The coordinator's first promotion check rejected LOD2 phase 37 at 10.06924 mm against a new absolute 10 mm limit; the original already measured 10.068745 mm there. That rejection does not identify newly introduced hover. The promotion check is being corrected to compare the exact frozen per-tier dense poses plus penetration bounds, preserving this original phase.
