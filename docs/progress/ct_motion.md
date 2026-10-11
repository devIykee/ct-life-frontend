# Motion review — 6 October 2026

[Inference] The sideways-running complaint is supported by measured native poses, and the source-to-Humanoid import is the first place to isolate. This is an investigation, not a report that the game movement is corrected.

## Evidence inspected

- Handoff, agent brief, relevant original build spec and ownership rules.
- Actual M4 Snow and Rain `direct-run.png` device frames, 8-phase native Rain run side render, and the earlier recorded Rain stride overview.
- Native full-cycle JSON and processed FBX audit, rather than only the previous report's conclusions.
- `LocalPlayerController`, `AvatarView`, `AvatarFactory`, shared clip importer, shared controller configuration, and current device gait sampler.

The native run head-to-hip angle has a persistent lateral component at all 60 unique phases: Snow +17.26°..+29.70° (mean +21.59°), Rain +17.12°..+28.52° (mean +21.04°). The pre-Unity processed sprint has an X/up mean of −2.13°, spanning −9.87°..+3.97°. Coordinate handedness differs, so the signs are not a one-to-one retarget comparison, but the source crosses the center and the native result does not. Existing jog candidates also lean consistently sideways. Foot IK and shoe-weight corrections do not alter the joint bias.

[Inference] A candidate cause is the imported root direction: current clips derive it from Unity's Body Orientation, while the raw sprint is strongly forward-bent. Unity documents Body Orientation as an average of upper/lower body orientation relative to the Avatar T-pose, and offers Original orientation plus an offset. See [Unity 6.3 root motion manual](https://docs.unity3d.com/6000.3/Documentation/Manual/RootMotion.html). The proposed isolated experiment retains the same FBX bytes, source Avatar, animation compression, rate and Foot IK state, and changes only the root-orientation import settings.

## Deliverables

- `art/character-candidates/motion-review-20261006/analyze_posture.py`: reproducible offline measurements.
- `art/character-candidates/motion-review-20261006/posture-analysis.json`: current measured results.
- `client/Assets/Scripts/Editor/RunPostureReview.cs`: candidate-only import/capture helper. Produces authored-root 0°/180° variants for sprint and jog and controls on both own Snow/Rain Avatars. No registry/controller promotion. Never calls global `AssetDatabase.SaveAssets`.

## Native checkpoint

Coordinator only:

```
-executeMethod CTLife.EditorTools.RunPostureReview.CaptureOrientation
```

Output: `art/character-candidates/motion-review-20261006/native-orientation/`.

Run after the compile check passes, with a coordinator-owned external production baseline. Then rerun `python3 art/character-candidates/motion-review-20261006/analyze_posture.py` and inspect frontal/side poses, loop and foot placement. A root-direction candidate cannot be promoted solely because its lateral angle decreases: it must face the intended travel direction and retain the source loop. Existing support/cadence issues remain independent acceptance work.

## Not done

No Unity or adb process was launched by this agent. No production movement or animation changed. No corrected gait has been device-accepted. The current sampler still measures `AvatarView.Body`, not observed visible LOD, and therefore cannot certify visible shoe contact. Root owns native/device work; `LocalPlayerController.cs` remains root-owned.

## Native root cause isolated

Coordinator ran `RunPostureReview.CaptureOrientation`: exit 0, 12 full cycles, source bytes unchanged. The candidate uses the exact same sprint FBX, Avatar, rate and IK configuration; only root orientation changes from Body-derived forward to Original with 180° offset.

The native whole-body result rotates **−23.8139° about Y**. Snow's mean face direction is now +0.18° relative to travel, compared with +24.00° in the baseline; Rain is +0.55° compared with +24.37°. Across all 61 captured phases per character, full root-local joint positions match that one rigid heading correction within 0.000001 m. Sole-height change is below 0.00000024 m. The game was rendering a forward-leaning sprint at an angle to travel. This import setting is a verified cause of the sideways presentation in the measured native cycle.

The correction does **not** improve the sprint's known foot penetration, support sliding or excessive forward hunch. Jog also benefits from orientation correction, but its foot-contact defects remain and no jog replacement is proposed for production in this checkpoint.

- Mathematical report: `art/character-candidates/motion-review-20261006/orientation-comparison.json`.
- Independent reproducible check: `python3 art/character-candidates/motion-review-20261006/verify_orientation.py`.
- Native-baked geometry front comparison: `art/character-candidates/motion-review-20261006/renders/snow-sprint-front-control-top-candidate-bottom.png`. Top row baseline, bottom row candidate, eight identical phases. The top row is translated upward only to lay out the sheet. Neutral diagnostic lighting; this sheet is not ground-contact evidence.

## Prepared integration

`RunPostureReview.PromoteRunOrientation` validates frozen candidate/input hashes and full-cycle heading-only agreement, backs up the production run metadata in a uniquely named temporary directory, applies the scoped marker and reimports. It then re-captures both actual character Avatars and checks all 122 phases against the frozen candidate. The controller and registry stay byte-protected. Failed runs restore the original run metadata and write a failed receipt.

The narrow `ContentImport.OnPreprocessAnimation` branch honors the marker only for `Assets/Art/animations/loco_run.fbx`; `DirectMovementImport` owns the marker/configuration. The run FBX is also referenced by `react_run_away`, so that alias receives the same heading correction. No state rates, masks, muscle curves or Foot IK settings are changed.

The revised development-only `DeviceGaitSampler` is now in its production diagnostic path after standalone C# compilation passed. It records per-renderer identities and declared LOD memberships, samples only a single valid observed visible renderer, counts missing/ambiguous/invalid observations, and records root-local head/hip angles. Its visibility observation is not GPU draw proof; it remains unsuitable as performance evidence. Native/device acceptance is still pending. The source is also preserved in the candidate folder.

## Promotion accepted by native execution

Coordinator ran `CTLife.EditorTools.RunPostureReview.PromoteRunOrientation`: exit 0. The new production import reproduced the selected candidate at all 122 sampled phases. Coordinator's external byte audit reported exactly one non-source asset change: `loco_run.fbx.meta`.

Receipt: `art/character-candidates/motion-review-20261006/promotion-receipt.json`. Final metadata SHA-256: `669360664ad82c37615b75bb32ce8852e34794b1c6ce74b379333ed66ea89613`. Source FBX bytes are unchanged. Root's complete source compile passed in `/tmp/ct-direction-source-check-final.log`. Snow and Rain native geometry comparisons have both been rendered and inspected. The run now aligns to navigation forward in these native measurements; continuous runtime/device movement and visible-LOD diagnostic acceptance remain pending.

## Independent authored motion and third adult base

The first direct-original-UAL experiment was captured by the coordinator and rejected: native source Sprint sole minima are approximately −114mm Rain /−133mm Snow, and source Jog −42mm /−74mm with persistent lateral bias. An isolated follow-up separates the genuinely unanimated source rest FBX from its animation FBX; `RunSourceRestReview.cs` and all frozen source files remain outside production motion paths. It has not run at this checkpoint. Original source root-motion speeds also differ materially from the game's 4.5m/s run: Sprint 8.25m/s, Jog 5.357m/s, Walk 0.975m/s on the original source rig. These source-unit speeds are not final target-avatar calibration.

An isolated Vincent lighter-world import helper is prepared under `art/character-candidates/motion-review-20261006/vincent-world-review/`. It uses a fresh own near-FBX Avatar, two lighter skins and separate atlases, candidate-specific controller lineage, full mapped joint comparison and five-action hand/thigh/sole sampling. A JSON-only binding proposes the existing `wallet_tracker` role without player cosmetics or a heavy creator preview dependency. Standalone full Editor compilation passed. Native staging and all visual/device acceptance remain pending, held while the coordinator investigates the user's phone closure.

The read-only `mobile-memory-audit.md` in the motion folder records historical intentional `reviewExit` terminations, saved memory measurements and allocation/lifetime risks. No current crash cause is diagnosed. Runtime/phone changes require current exit evidence; archived memory snapshots are not a controlled comparison.

## Cadence audit staged during GUI work

`cadence-review.md` and reproducible `audit_cadence.py`/`cadence-audit.json` now trace current controllers, speed parameters, input and actual-speed hysteresis, fixed-time crossfades, historical M4 timing and Foley calibration. Current run is speed/4.5 with Foot IK off on both own Avatars; no BlendTree exists. Both current override controllers inherit that run. Heading-corrected short joint windows span fitted references 6.510–8.880m/s across landmarks/characters, so source speed 8.25m/s alone does not establish the proper rate. Applying it would make the current sprint's full cycle 1.222s at 4.5m/s; visual slow-motion risk is explicit.

`RunCadenceCandidateReview.cs` compiles outside Assets. Coordinator method `CTLife.EditorTools.RunCadenceCandidateReview.Capture` compares references 4.5/6.5/8.25 using unsaved controllers, checks native speed factors, and captures all world LODs with fixed material sole patches at 241 phases. It has no import/save/promotion path. Actual input/navigation/proportions, Foot IK and production bytes remain unchanged. It awaits coordinator native scheduling; no rate or contact result is claimed. Production changes stay held until the source-rest trial and timed visual/device comparison are accepted.

Walking Foley still uses 1.463m step distance from the old 2.2m/s reference. Its timing now differs from the retimed walk's assumed two plants/cycle; sound/contact review is required alongside cadence, with no audio edit made here.

## Runtime boundary and resource ownership review

Staged `runtime-boundaries/unity-null-boundaries.patch` replaced four unsafe Unity-object null boundaries across CameraRig, NpcController and MovementControls. The coordinator reviewed/applied it while Unity was closed. Movement cancellation retains its previously cached live player; only a Unity-null cached player falls back to the current session's player. No blanket null-operator rewrite was proposed. Coordinator native compilation/acceptance is separate.

The modular assembler creates a new Mesh per call and optionally an atlas Texture2D + Material, without a matching destruction owner. Registered Snow/Rain prefab creation does not use this branch. An exact two-file ownership patch and a native lifetime probe are staged under `art/character-candidates/motion-review-20261006/assembler-ownership/`, outside Assets. It destroys only its explicitly allocated assets, preserves caller/shared inputs, retains hidden-body resources, and cleans up partial allocation failures. Shared placeholder caches remain untouched.

The patch passes `git apply --check`; candidate World code under Android and Editor defines and the standalone Editor probe compile with Unity 6.3.25f1 Roslyn, output in `/tmp/ct-avatar-ownership-compile/`. Coordinator methods are `CTLife.EditorTools.GeneratedAvatarOwnershipReview.CaptureBaseline` before applying the patch, and `.CaptureCandidate` afterward. Each has source-hash guards and records 25 lifetime cases, including inactive ancestors and shared material overrides. Native execution, deferred Play-mode cleanup and Android memory measurements are pending. No current phone closure is attributed to this defect and no live Assets were changed by this ownership review.

### Ownership probe execution-mode correction

Coordinator ran the original Edit-mode baseline (25 meshes remained), then applied the ownership patch. The Edit-mode candidate failed on its first case. That probe was invalid for normal runtime-only MonoBehaviour callbacks: the owner deliberately has no ExecuteAlways attribute. Its first failed receipt is retained; runtime code was not changed to satisfy this test.

With explicit coordinator ownership, `client/Assets/Scripts/Editor/GeneratedAvatarOwnershipReview.cs` was replaced by a Play-mode state machine mirrored in the outside-Assets candidate. It uses clean empty scene entry, SessionState through domain transitions, real advancing player frame numbers, deferred Destroy, three post-destruction frames, native destroyed-target checks, and restoration before batch exit. Isolated Editor compilation passes without output (`/tmp/ct-avatar-ownership-compile/Editor-playmode.log`), frozen helper SHA `c320472d79b6e6bb0f2db5080652f61820087116db61c200a9e14d92fb78cafd`.

Coordinator should run the same `.CaptureCandidate` with `-batchmode` but **without `-quit`**; the helper exits batch itself. New receipt is `assembler-ownership/native-playmode-candidate.json`. No runtime source changed in this probe correction. Revised native execution and Android measurement remain pending; no crash cause or memory saving is claimed.

### Ownership runtime acceptance

Coordinator's revised Play-mode run exited 0 (`/tmp/ct-avatar-ownership-playmode-20261006.log`). The receipt records frames 1–202, zero probe runtime errors and successful scene restoration. All 25 generated meshes and all 22 generated atlas/material pairs were destroyed; every shared source/override asset remained alive. Hidden bodies retained resources over two frames; each destroyed target and its resources were checked exactly three player frames after Destroy. Eleven cases destroyed an entire initially inactive avatar, with the remaining cases destroying its generated body.

The receipt hashes match the integrated runtime files. This is accepted lifetime behavior for the tested creation/destruction paths, not Android memory reduction or a phone-closure diagnosis. Allocation-failure and live-body domain-reload paths remain untested. No additional Assets source edit was made after the probe froze. The next motion queue is documented in `art/character-candidates/motion-review-20261006/native-motion-queue.md`.

## Delegated native motion slot completed and released

With explicit exclusive Unity ownership from the coordinator, ran SourceRest and Cadence synchronously. Both exited 0: `/tmp/ct-run-source-rest-20261006.log` (67.91s) and `/tmp/ct-run-cadence-20261006.log` (45.55s). A minimal inherited environment allowlist excluded application secrets. All 6,130 preexisting Assets/ProjectSettings/Packages files stayed byte-identical after the rest trial; only 11 expected helper/candidate additions appeared. All 6,141 files stayed byte-identical after cadence, with no additions. No Editor process remained when ownership was explicitly released.

The rest-source Avatar materially improves the previously failed direct-source import, but Snow still reaches −48.3mm Sprint /−41.1mm Jog sole minima. Rain reaches −19.5mm /−9.9mm. Jog is more upright, but no replacement is accepted. Direct rest-source correct facing is Original 0°; production processed FBX remains Original 180° unchanged. Cadence's nine native rates per character preserve same-phase joints exactly; its 24 fixed-patch near-plane masks have only 0–2 samples each, insufficient for reliable planted-foot rate selection. Full evidence is in `native-motion-results.md/.json`, the two native output folders and `native-execution/`.

Root resumed exclusive Unity ownership for B2/ModDesk. Further contact work is staged outside Assets only. `RunRestContactReview.cs` now compiles in isolation (`/tmp/ct-rest-contact-compile/compile.log`) and compares rest-source Jog Original/Feet Y projection × Foot IK off/on, including source rig controls, continuous phase advancement and all three target world skins. It is not yet imported or run and has no promotion/global-save path.


## Rest-Jog matrix analyzed; narrow goal trim staged

Coordinator native matrix exit0, 12 cases, protected drift empty. OriginalY + Foot IK improves all-world-LOD sole minima from −41.09mm to−8.45mm Snow and−9.94mm to+3.88mm Rain; Rain left foot still hovers at+10.52mm. FeetY is rejected because it lowers hips about80mm and suppresses flight. No production source/rate replacement accepted. Source feet-bottom/rest-shoe references agree; no vertical avatar shim is justified. Whole-body loop flags originate in source hand seam, not foot discontinuity.

CPU-only native-geometry front/side sheets completed and were inspected. They preserve upright/alternating posture but quarter-cycle exports mostly show flight, so they do not prove stance. Reproducible all-LOD fixed-material cadence analysis finds provisional refs5.82553m/s Snow/5.53965m/s Rain, with common-window sensitivity around1.2%/0.5%. Existing1× at4.5m/s would still slide; final rates must follow corrected contact capture.

Frozen outside-Assets `RestJogGoalCandidate.cs` SHA ec2f40636c4877ef3180819b1635ee20cd55c0d8b34cf77b80617f5f8dc5070c compiles exit0 with no output in `/tmp/ct-rest-jog-goal-compile/compile.log`. Root method `CTLife.EditorTools.RestJogGoalCandidate.BuildCandidates` is ready for a later exclusive native slot. It tests signed±1mm goal response, applies per-foot stance-only trim, protects body/root/XZ/flight, samples241 phases/allLODs, records fixed patches/eight geometry phases and has finally-block asset guards. No live Assets edited or Unity launched while root owns native. Full outcome, exact limits and narrow per-look controller integration path are in `rest-contact-results.md`.


### Rest-Jog V1 sampler rejected; V2 frozen outside Assets

During an explicitly assigned exclusive native slot, V1 exited0 after72.361s, but independent frame review found static joints, zero correction and240/240 airborne dense phases. Its all_pass receipt is invalid and all generated clips remain unreferenced. The exact stopped-time mechanism was not instrumented; V1's parameterized state/scene-move ordering differs from the proven constant1× matrix path. No production promotion occurred. Full snapshots show7,369 preexisting Assets/ProjectSettings/Packages files byte-identical,11 scoped additions only. No Unity process remained; ownership was explicitly released. Root reported ADB restarted during Unity initialization, invalidating concurrent phone log capture; no crash attribution.

V2 SHA `5d3d201501330a5bb0ab07a7f5530a02eeadea0d638880eb0957bf63b91fdec9` moves/configures the rig in the same order as the matrix, uses constant1× with explicit Animator speed1, and adds native warm-up/per-step/phase/rate assertions plus mandatory moving-joint/ground-flight ranges. It checks every original pose against the independent native matrix within2mm before correction, rejects empty correction/stance windows, and requires corrected contact on every expected stance phase. Separate V2 output/asset paths preserve the failure record. Standalone compiler succeeded after excluding an unused missing Addressables.Editor cached reference; final log `/tmp/ct-rest-jog-goal-compile-v2/compile.log`. No V2 live Assets edit or native run yet.


### V2/V3 contact evidence and unbiased run comparison

V2 genuinely advanced and reproduced the independent matrix, then failed on4.13/3.99mm foot/toe XZ drift from reconstructing source endpoint derivatives. V3 preserves the original cubic foot-goal curves and adds only a separate correction. Native V3 exit1 because Rain loop velocity worsens; Snow passes all numerical gates (−1.488mm minimum sole,0.0435mm horizontal change, unchanged body/rotation/clear flight). Receipt and both failed predecessors remain in the motion folder. The Snow candidate hash is frozen; no production assignment has occurred.

Corrected Snow stance calibration gives reference5.84536284m/s, proposed0.769841× at world4.5m/s,1.65steps/s. This may feel like slow bounding; contact statistics do not accept it. Frozen full-controller playback helper compares current Sprint and corrected Jog in continuous landscape native game-camera/side renders. First delegated native failed at Editor compilation due to a missing Audio assembly reference, with only helper+meta added. Ownership was released; repaired helper reflects actual runtime Foley without adding an assembly dependency, and root imported it. Exact helper hash/current queue/evidence are in `rest-contact-results.md`. Native rendering and device feel remain pending.

A strict current-Sprint equivalent, same40mm correction bound/pose gates, is staged outside Assets. Historical Creator samples warn that a low global foot minimum hides76–90mm hover during some source-defined stance phases; current-world native evidence must decide. A Rain-only240-knot/480-phase boundary trial is also staged with unchanged gates. Standalone compiles pass; no new native launch is authorized yet.

### Seated source and contact audit

Reviewed root's actual B2 seated native images/data. act_sit and act_type states both disable FootIK; their imports bake OriginalY atzero height offset through the old male body Avatar. Sitting derives from UAL; typing is custom spine-relative wrist targets, without desk constraints. Root-space offsets would mix separate foot, seat and desk problems and are not proposed. A read-only `SeatedFootIkReview.cs` compares both actions/looks withFootIK off/on, uses exact existing clips and anchors in unsaved controllers, validates control hips/soles against root's independent capture, and records per-foot/bone-weighted hand surfaces. It compiles outside Assets; native scheduling pending. World agent holds final furniture acceptance.

### Continuous run and seated native follow-up

Snow continuous capture completed480images/4s, exact current controller versus isolated corrected Jog, protected drift empty. Independent30Hz analysis confirms Jog airborne windows0.367–0.400s versus Sprint0.200–0.233s. Jog near-plane toe-patch residual is about0.109m/s mean in28valid samples; its proposed contact-phase cues match6/7 visible descent events exactly and one1frame late. Current distance Foley is4–6frames away from visible plants. Jog is not accepted: long bounding remains a feel concern. Side plane is edge-on/invisible, so its stills cannot visually prove floor contact. Sequence metrics are in `native-snow-playback-v1/independent-sequence-analysis.json`.

Initial Sprint candidate failed source comparison. A separate read-only native diagnostic proves the historical direct-Play control matches every joint exactly; only LeftHand at phase0 differs in continuous playback,3.510mm Snow/2.971mm Rain, because warm-up1.99999821 samples the authored loop end rather than direct0. No global tolerance change is proposed. FootIK itself changes footXZ about181–205mm during contact and alters swingY substantially, making a simple Y trim against that IK baseline inappropriate. `SprintGoalResponseReview.cs` now stages signed1mm translation/1degree quaternion response tests before reconstructing per-look goals from original production FK. Native response pending; cadence remains1× for that proposed comparison.

Seated matrix completed natively. FootIK alone overshoots and spreads feet; measured per-look goals preserve original ankleXZ/hips and ground social feet. Both social cases pass; typing residualXZ2.067/3.126mm fails unchanged2mm bound. A bounded residual refinement is staged. World actual chair triangles show remaining support gaps; one shared0.450m B3 seat plus explicit per-look seated-body authoring is being tested with exact actor roots/fixed feet. Details/current hashes and hand reach evidence are consolidated in `seated-contact-results.md`. No seated clip is promoted.

### Native source-response and shared social contact follow-up

All96 Sprint T/Q response cases passed with≤0.405µm translation response error, recorded0° requested-rotation error and unchanged body. Rotation changes ankle position, so the new frozen SprintFkContactCandidate first reconstructs exact production FK and requires a zero-lift replay before contact correction. Timing stays1×/world4.5; actual stance/skating and continuous feel remain unaccepted. A source patch-velocity audit records why below-floor minima cannot alone establish intended plants.

Social body fit completed native exit0, both looks pass and protected drift empty. Explicit authored body deltas are−2.307mm Snow/−38.179mm Rain with exact roots and feet preserved below1µm. World’s48 phase/LOD checks found no tested chair intersections/penetration; measured support gaps still warrant the queued render, and other venues’ seated furniture remains outside this acceptance.

Typing refinement failed at the original whole-body loop seam, not interior solve accuracy. A separate first/last10% C1 curve closure and dependent closed-source foot candidate are frozen outside Assets with unchanged contact gates. Hand review is blocked until the new feet/body baseline passes; no runtime hand IK is proposed. CandidateREADME records exact server action selection, controller/LOD ownership, look-change limitations, importer preservation hazards and the offline bake/replay recommendation. No live Assets edits or Unity launch during root’s APK/device ownership. Exact hashes and methods are in the motion evidence documents.


### Current native results and frozen next probe

- Typing whole-body closure and closed-source feet passed native for both looks with protected drift empty. Endpoint all-skin closure is≤2.21µm; feet all-LOD soles≈0.497–0.596mm, ankle XZ≤2.70µm. Work chair/body and keyboard/hands remain unaccepted; knee displacement remains explicit.
- Sprint T/Q zero-lift reconstruction failed despite source agreement≤2.21µm. Knees moved70–93mm; default Foot IK added≈18° flexion and4.8–5.6mm segment-length changes. No contact correction ran. Initial knee-pole inference was corrected after measuring actual bend-plane direction.
- `SprintDirectLegCandidate.cs` frozen SHA `b7eead7ae0a8d77cb69f2eb6fa5e9f35e2f3b3de04ba37ec6114f4d923d4d28e`, compile0. Original no-IK direct fixed-length leg target,16 muscle-delta curves, Foot IK off; native zero-delta and geometry replay must pass before contact/anatomy gates. Rain's analytically predicted28.47° knee change remains above the unchanged15° gate. No live edits/native launch performed.


### Calibrated hands and dense Rain source review

Combined typing hand/body/foot comparison now passes both native looks with original physical bounds intact. Goal-to-wrist frame calibration removed the erroneous84°/92° orientation mismatch; index gaps≤2.97µm, zero protected drift. Shared curved office body fit also passed; world is checking final complete furniture triangles. An offline34-muscle Humanoid bake/FootIK-off replay helper is frozen and compiled, native pending, no production assignments.

Rain dense rest-Avatar Sprint source was captured481poses/all3tiers, zero drift: sole−20.59mm vsproduction−39.36mm, head–hip32.38° vs41.16°, same.667s cadence. It still predicts17.272° bend for floor contact, above15°, so remains unaccepted. Original processed Rain plus a broad20.72mm body shift was rejected for support hover. Stable anatomical-axis analysis is retained separately; it resolves the near-straight direction spike but cannot make the geometric contact/bend conflict disappear.


### Persistent typing bake audit

Independent motion/world review found the staged helper proved only pre-save replay. Staged revision `persistent-replay-v2` (SHA `682086f68bfbf6deb339992575cddb0a98145d593a9378dbee0e68f4dca4b34a`) now saves only isolated candidates, unloads/reimports/reloads the exact clip, repeats all 241-phase joint/contact/all-LOD vertex gates, and requires unchanged on-disk bytes. Explicit finite guards precede aggregate pose/skin errors. Standalone Editor compilation passed; native validation remains pending under root ownership. No live Assets, Unity or ADB changes made. Procedure and receipt contract are in `typing-offline-bake-audit.md`; old helper hash is superseded.


### Fresh-process persistent typing replay

Native pre-save bake passed geometry but failed its same-process unload/null proxy. Preserved failed receipt and saved assets. A separate read-only helper loaded those exact hashes after a verified new Unity process start and passed all241-phase, all-three-tier vertex/contact/curve-scope gates with IK OFF and zero drift. Snow skin error0.724mm; Rain1.238mm; soles0.497–0.511mm. Native source/result acceptance is now established for the saved clips; furniture/controller/device acceptance and any production assignment remain pending.


### Café scoped controller integration frozen (native pending)

Staged `cafe-seat-integration/runtime-proposal.patch` preserves the global shared controller, existing Snow/Rain overrides and CharacterRegistry. The explicit final-prefab profile binds one per-look derived controller at player initialization; exact spot+authoritative action resolves private social FootIK-ON or persisted typing-FK/OFF states. Canonical actions and seat context survive repeated updates/emote return; movement/stand clears context. Empty local animation does not opt into fitted motion. World agent independently reviewed these boundaries. Full World compile and patch dry-run pass.

`CafeSeatControllerReview.Capture` is frozen at SHA `911b5b646d4b5a5ef8b8c8f62e1e10392d1750f22227d53faa616f7aebc88148`; Editor-reference compilation passes against the staged World assembly. It creates only two unassigned copied controllers, preserves all46 baseline state semantics/effective overrides, compares all-LOD native poses and captures actual AvatarView crossfades plus authority/context cases. Numeric status is explicitly visual-pending; fixed-root fixture does not prove final-room navigation/connected seating/device performance. Exact baseline/source hashes and attachment/transition acceptance procedure are in that folder's README/manifests. Native run and any final-profile attachment remain root-owned. No further pose refinements are active.

### Furniture v2 visual review / CPU transition capture amendment

Viewed all18 images; static body/chair/feet/arm reach are coherent with no new visible pose defect. Featureless keyboard is a world appearance task. Exact duplicate image hashes prove the old00/80/160 labels do not establish distinct visual phases even though native CPU poses advance. Evidence retained in motion-review `typing-furniture-v2-visual-review.{md,json}`. Staged CafeSeatControllerReview now uses measured CPU mesh snapshots with original materials/transform and render advancement checks; full World + Editor compile against native Bee references passes. Root owns native execution; no live edits or production assignments.

Correction: the IsWalkable X/Z defect claim was incorrect. ToLocal returns Vector2(x,z); l.y is the correct venue Z. The staged patch was withdrawn before any live edit and retained only as REJECTED evidence.

### Controller v1 preservation vs transition quality / v2 failure

Native original46-state comparisons reproduce exact skins for both looks, but canonical fallback sit→stand sinks154/144mm and run→idle83/76mm. These remain production motion defects. Native explicit fitted v2 entry/exit identifies direct typing↔idle sinking126–132mm; social entry/exit and social↔typing stay grounded. V2 is failed, zero protected drift, no final registration. Source-only strategy comparison now compiles (social bridge vs temporary typing FootIK-ON copy with full-body/handoff continuity); no runtime change. Full results in `cafe-controller-transition-results.{md,json}`.

### Café final-room bridge stage (2026-10-06)

The fixed-root native strategy passed social bridging for both looks; FootIK-copy typing is rejected (16.128/27.311mm skin change). Independent side-image review agrees with root: planted shoes, body rising, fixed roots only. A narrowly scoped AvatarView bridge and actual Local/Remote Play-mode fixture are frozen under `art/character-candidates/motion-review-20261006/cafe-seat-integration/bridge-runtime/`. World and Editor native-reference compilations exit0. Runtime SHA81d48d41764d658d1da3e42cb0c0f177a109e1490812c5b622f51ea7d9bdd583; fixture SHAec426321c12b8a9bc80d1469ac7c0452cfa5332552108fe1989b5d345c1864cb. No live edits or native launch by motion agent.

World's independent HasState guard feedback is implemented and the overlay-ownership comment corrected. The fixture separately records approach restoration and real subsequent input motion, rejects fitted entry/exit soles below−2mm, samples all3LODs, captures actual CPU pose images, and covers social/typing, both-leg reseat interruption, culling/resume, missing spot/action and actual timed emote completion. No network authority or final visual acceptance is claimed. Root owns next import/native run; attachment remains blocked on real moving-player/furniture evidence.

### Actual final-room native V1 failed; V2 fixture frozen

Root imported/runtime81d48d + fixtureec4263 and ran actual Playmode. It ended exit1 at Snow culled-state observation:0.15s produced1realframe vs required3. Rain neverexecuted;0drift. Observed Snow fitted contacts pass only their collectedscope, notaggregateacceptance. Real directmovement1.551m follows separate1.354m restoration; ordinary run-stop sole−42.513mm remains backlog. Independent actualPNG review found sidecamera projects alongX, concealing the lateral anchor→approach relocation; root independentlyagreed. No visualexitapproval.

V2 fixtureSHA867dd7a45bd5ede52a7cac7786a3c6a4a6c691e9b2d5a119106a0e16dd443f59 compilesexit0 againstnativeEditorrefs. Bounded duration+minimum3realframes and15sindependentwalltimeout, complete20contactcasecoverage gate, fixedoldseat-focus oblique companion/pre-restoration images. Runtime/clips/controllers unchanged. V1 failedsource/nativeJSON/independentreview preserved in bridge-runtime. Rootownsnextimport/run; no live source/device/native bymotionagent.


### Actual exit presentation rejected; physical emergence candidate staged

V2 oblique native evidence exposes a1.3537m seated-body relocation before standing; actual floor contact does not accept it. V2 remains failed/partial at an over-specific remote idle assertion; V3 assertion-only repair is frozen/compiled and still does not fix the visible warp. A separate continuous presentation/runtime patch compiles outside Assets, preserving logical authority and latest input, but is explicitly not ready to import: actual idle-at-seat intersects chair lower legs. World is measuring a rearward-chair alternative and exact exit corridor/turns.

Existing licensed Sitting_Exit native source capture completed8cases/zero drift. Raw FK sinks76.6/114mm; default FootIK floats/spreads; final quarter steps backward into chair. A bounded calibrated planted-foot source-prefix rise candidate is frozen with World+Editor compile0, no runtime/clip assignment. Root owns native execution; geometry/visual acceptance remains pending. Exact manifests and limitations live under cafe-seat-integration/exit-traversal/.

2026-10-07: Choreography native211 samples/look passed. CPU audit114 exported poses/LODs: zero tested chair+desk crossings; Rain corrected forearms visibly clear, real-time feel pending. Staged compiled CafeExitOfflineBakeReview (hash in exit-bake-manifest.json) creates only isolated clips and requires a separate fresh-process all-skin replay. Runtime traversal still not imported; next target is one actual room/control sequence, not further submillimetre pose polish.

2026-10-07: Offline exit bake v1 failed HumanPose representation13.3–13.6mm, source target exact; measured leg segment stretch explains the position loss. Root approved exit-only solver path. Frozen runtime-work-exit-manifest.json and room helper compile clean using actual World/Editor references; native acceptance pending. Includes research occupied-spot resolver call sites and same-instance remote state handling. No live edits/Unity/ADB by motion agent.

2026-10-07: Actual room v1 failed repeated-seat fixture stale-confirmation setup, not proven control rejection. First Snow exit observed−0.436mm floor minimum/unchanged logical root, plausible inspected gameplay/oblique frames; only5 authored phase frames due synchronous captures. Helper-only v2 uses fresh GoSit intent and separates no-image timing/contact from opt-in diagnostic capture. Runtime unchanged. Rain/remote pending.

### 2026-10-07 actual room v2 / second-step stage

- V2 actual Local/Remote both looks completed but correctly failed contact: ordinary Rain walk−5.53mm and Snow remote walk→idle−7.96mm. Inspected rise/first pair remain≥+.497mm. Receipt retained `/tmp/ct-cafe-continuous-exit-v2-timing-20261007/receipt.json`, protected2991/drift0, cleanup0.
- Verified unique game frames AND unique Time.time values for all1654/1043/1015/1129 observations; uncapped Editor counts are not phone performance. Input-to-translation≈2.22s remains a feel concern.
- Remote first restored-parent frame visibly unsuitable: feet temporarily+172/+179mm, then recover. Staged narrow two-file re-pin patch `.../exit-traversal/remote-parent-order-v3/`, World compile0; no live edit/native acceptance.
- Close v1 gait-frame6 and CPU surfaces reject current handoff:97 chair triangles crossed/all3LODs, desk0. Smaller extra lateral endpoints through+.70 fail; actual3-pose/all3LOD envelope at+.75X/−.05Z clears chair+desk. This does not accept a trajectory or Rain.
- New frozen `CafeExitSecondStepCandidate.Capture` adds a second+.30m planted pair after existing+.45/−.05pair, preserving inspected first1.50s; same grounding/reach/anatomy limits. Source SHA3fe3b5baace4b6a69a562f0e04ddf76ac7fa3831c63f5db2e9a7439dbbc12c57, manifest b29a546e43197feb6a14877b315d310647a409f07cfea95d17ca56769662b425. Editor compile0. Root owns import/native; runtime tables/profile remain unchanged/unassigned.


2026-10-07 — exit handoff bounded next steps: second-step native source PASS both looks (265frames/2.2s; original first1.5s unchanged), independent chair+desk audit138body meshes zero tested triangle crossings. Root has new remote pre-Animator presentation re-pin live. Rain v1 contact floor/anatomy passes but absolute loop gate fails inherited2.166mm seam (+.342µm foot change); failed receipt preserved. Root explicitly authorized v2 source-relative nonregression, no whole-gait reauthoring. V2 helper frozen57210ff89579152b4e58295b5b057f1539d640c7235c811b6975dc44616c837c, source compile0, native pending. Snow stop comparison rejected destinationFootIKON (same−7.904mm transient, finalskin54.2mm changed). Unused exit-only analytic leg correction/native10phase comparison frozen under exit-settle-contact-source-manifest.json; World/Editor compile0. No new production assignments/live edits/native launched by motion agent. Final room60Hz/controls/furniture proof remains required; second-step and helper source compile are not interactive acceptance.

2026-10-07 — Rain v2 native PASS, saved clip57bc3c9a2357b5bb036a7955a96e26acc8f9d695c6ebd6b532dbf0f36b938d4c, original loop explicitly retained. Snow bounded contact native PASS10phases/all3skins/protecteddrift0; independent final state audit confirms idle/no transition in every case, finalskin delta0. Actual .674 CPU side snapshots inspected. Runtime v3 source frozen and compiled0; exact four-file patch preserves live remote parent-order correction, no Local/Remote/VenueSession replacement. Companion preflight performs fresh saved Rain full-cycle replay before the actual room in the same invocation. Native room/source package remains unpromoted. Cache is11,083shoe vertices/165matrices; desktop CPU timing counters added, mobile/concurrent cost unmeasured.

2026-10-07 — v3 attempted-room failures preserved: process99210 independently passed saved Rain241phase/three-tier replay with0joint/sole/rotation error, then room clean-scene guard refused dirty Bootstrap before Play. A scratch-scene companion follow-up failed SetActiveScene and preserved original setup/protected bytes. Root chose separate valid saved-replay proof as prerequisite: current room helper2a8430e26723fa5473181da107e00d78300903319afecd3bfb049e552001706a removes inline sampling, pins immutable process99210 PASS and exact current saved bytes, leaves clean-scene guard unchanged. Source compiled0. No game-room acceptance yet.

Shared social exit reuse scoped/staged: exact Café .450m chair copied into StudioF2 supports starting the accepted SocialState(FootIKON), then existing licensed rise/Rain-arm clearance/two planted pairs. Fourcase signed authoring helper compiled0; Studio speaker yaw180requires negative actorX toward unchanged global+X approach. No social runtime/registry changes, native pending. Studio's unchanged stage decorative top+6mm is a separate occupied-floor boundary and was sent to world; no actor shim proposed.

### 2026-10-07 — V3 actual room desktop evidence, bounded acceptance

Root's `/tmp/ct-cafe-continuous-exit-v3-pinned-replay-timing-20261007` completed with all four fitted Local/Remote exits above −2 mm, all 32 component scenarios complete, no cleanup failure, and 3,000 protected files unchanged. Independent trace audit is at `art/character-candidates/motion-review-20261006/cafe-seat-integration/exit-traversal/room-v3-independent-audit/timing-trace-audit.json`. Every recorded row has a unique advancing game frame/time. The 43,189 `Require` logs include repeated Editor ticks and are not independent pose samples.

Remote initial parent restoration now retains the presentation at the exact seat: Snow frame898 and Rain2189 move the logical root to its approach while soles remain approximately +0.5 mm and the presentation does not jump. Authored presentation finishes at +0.75 X / −0.05 Z; the subsequent turn/walk/align reaches the logical root. Minimum all-tier sole on complete fitted exits is −0.198 mm Snow / −0.129 mm Rain. Held input is accepted during rise but actual logical movement starts around2.43 s; this latency remains a product tradeoff. Snow exit-settle needs6–7 calls per complete exit, total6.59–7.86 ms, maximum1.31–3.08 ms per call on this desktop; cached11,083 shoe vertices /165 matrices. Repeated prepare rows retain cumulative counters and must not be summed. Median observed dt is near1/60 s, but long frames remain; no locked60FPS, mobile, crowded-scene, or connected-authority acceptance is claimed.

Held movement subsequently reaches the existing ordinary `loco_run` (hash918792671), outside exit ownership and outside transition: Snow frame836 at phase1.069536 has −31.064 mm soles; Rain frame2131 at phase1.048331 has −37.435 mm, both speed4.5. This is an unresolved ordinary-run defect, not covered by fitted-exit acceptance. The source loop/contact correction should remain an authored clip task rather than spreading the seat solver over all movement.

The independent actual composed-room diagnostic audit covers192 body meshes (8 scenarios ×8 captured phases ×3 LODs): every desk crossing count is0, every Rain chair count is0, and all Snow turn/step/align samples clear. Snow's first diagnostic callback has1–3 crossed upper-seat triangles; 3mm-grid underside ray depth is2.48mm atLOD0,0.76mm atLOD1,1.81mm atLOD2. This is small support contact visible only in closer geometry review, not the old chair-front/desk-leg obstruction. Evidence: `room-v3-independent-audit/composed-room-furniture-surfaces.json` and `snow-first-callback-seat-depth.json`. Images were viewed at actual gameplay and oblique scale. Diagnostic rendering slows the sequence and is not a continuous feel recording.

A separate source correspondence issue was exposed: the first runtime IK callback advances goal time by the entire frame delta although the new native WorkExit state is still at phase0. In timing capture this puts Snow goals0.21985 s and Rain0.27496 s ahead of the underlying stand clip. Comparing remote hips against the frozen authored source at goal time shows maximum235/285mm discrepancy during rise; they converge after idle. This is not inferred from soles. The narrow proposed first-callback clock correction is staged outside Assets in `exit-traversal/first-callback-clock-v4/`; it begins goal time at0 on the first callback and advances subsequent unique frames only. No live mutation or new acceptance yet. Source/body clock and actual room/furniture behavior need rechecking before final profile registration.

### 2026-10-07 — source-check isolation error, native recovery root-owned

Correction: v4 World/Editor csc checks exited0 but copied native rsp files still contained `-refout:` into shared Bee reference paths. Those references acquired the scratch assembly identities `CTLife.World.FirstCallback` / `CTLife.Editor.FirstCallback`; root's subsequent F3 launch could not resolve the former. I reported the exact two cached ref paths, stopped compiler writes and made no cache/Assets/native repair. Root owns recovery. See `exit-traversal/first-callback-clock-v4/COMPILE-ISOLATION-INCIDENT.md`. This is an agent tooling error, not diagnosed game behavior; previous source-check status is not isolated native acceptance. Existing V3 room results remain unchanged.


## 2026-10-07 — v4 clock review, foreground timing boundary, shared social evidence

V4 actual work-exit room completed all4 fitted contact cases and control/interruption cases but FAILED the desktop cadence aggregate. All30 observed cases had99.820–101.002ms medians (373 unique consecutive frames; worst326.349ms). Source/goal clocks now agree to native float precision (maximum about0.000060ms), including first callback0. Independent trace summary is `first-callback-clock-v4/native-timing-audit.json`; no60Hz/device acceptance is claimed. Official Unity UUM-91847 documents Editor app-deactivation throttling, but this run did not record focus, so attributing it remains an inference.

Staged helper-only foreground-cadence-v5 waits for real Game/app focus with60s bound before scenarios; records focus, actual enabled game camera and contact-sampling cost; preserves every original gate and keeps focus-loss samples. No QueuePlayerLoopUpdate, Animator.Update, preference change or runtime pose change. Private compiler exit0 with native input/copy drift[]. Root owns import/native.

Shared social V1 passing cases have207 exported body meshes across23 phases and3LODs. Independent actual shared-chair plus final-position table triangle tests found zero crossings; Snow-left still fails the separate83.716mm required pelvis drop versus80mm gate. Frozen V2 uses two37.5cm increments, same75cm endpoint/timing and unchanged pre-step rise, without gate relaxation. It remains native-pending.

Rain rest-Avatar Sprint is preferred over the old copied-Avatar source: native penetration improves39.36→20.59mm and forward posture41.16→32.38degrees. Fixed foot orientation still requires17.272degree knee adjustment near the seam (5of481 phases). Staged RainRestSprintFootRollReview measures actual all-LOD response to at most4degree heel-strike pitch before one bounded source-curve candidate; no body/root lift, asset or runtime write. Private source compile0, native pending.


F4 static contact follow-up: four exact native LOD0 meshes at phase8.345938, yaw0 or180.023346degrees, show floor minima+0.513..+0.517mm and zero tested shared-chair triangle crossings. Posterior support minimum gaps4.583mmSnow/4.855mmRain; no rays penetrating beyond2mm. F2 copy/F3 preservation/F4 held-geometry hashes are pinned in `exit-traversal/studio-f4-static-contact.json`. Viewed Snow audience/Rain stage side PNGs without a visible shoe/back collision. This is static fitted-preview evidence, not social exit or connected authority. V5 foreground run stopped before any timed case because focus was never observed; failed output preserved, no cadence/contact result. Root is addressing actual Editor window visibility, with no gate changes.
