# CT Life — continuation context

**Checkpoint:** 6 October 2026, approximately 12:00 UTC / 13:00 Africa/Lagos.  
**Purpose:** enable another agent to continue from the actual workspace without inheriting the previous agent’s aesthetic judgments or treating its reports as product approval.

This is a handoff, not a completion report. The game is unfinished. Source changes, imported assets, built APKs, device observations and user approval are different states; they are distinguished below. All paths are relative to the frontend repository unless an absolute path is shown.

The receiving agent needs access to the current workspace and assets; this document is not a project archive. Markdown links in the repository copy resolve from `docs/handoff/`. The standalone Downloads copy uses absolute local links for this Mac.

## 1. Start here

- Frontend repository: `/Users/mac/Documents/codes/projects/ct-life`.
- Unity project: `/Users/mac/Documents/codes/projects/ct-life/client`.
- Separate backend: `/Users/mac/Documents/codes/projects/ct-life-backend`, owned by **devIykee**. Read it when needed; do not implement a competing backend or alter its contract without coordination.
- The conversation’s initial working directory, `/Users/mac/Documents/codes/apps`, is an unrelated Flutter monorepo. CT Life is a Unity Android project. Do not run Flutter acceptance checks against it.
- Git branch: `main`; HEAD: `95cd4b9aa970107338082b9f4f0a88ba76a69278`.
- At the beginning of this handoff there were **115 changed tracked files** (93 modified, 22 deleted) and **7,309 untracked files**. This includes extensive pre-existing work. Git HEAD is not a complete copy of the current game. Do not reset, clean, broadly delete, or attribute every change to this session.
- The current agents stopped at explicit checkpoints for this handoff. No Unity, Blender, native test or device-capture process owned by this work is running.
- The latest `adb devices -l` check returned **no connected devices**. Recheck before any device work. The earlier Seeker connection is not proof of current availability.

Read, in this order:

1. [Current workspace brief](../AGENTS-BRIEF.md).
2. [Claude Build Spec export](../spec/build-spec.md) and [FE/BE contract prose](../spec/contract.md).
3. [Imported contract provenance](../../contract/SOURCE.md), then the actual `contract/openapi.yaml`, `contract/realtime/`, `contract/ids/` and `contract/config/` needed for the task.
4. This handoff’s current-state tables and the linked evidence for the area you change.
5. Relevant source and actual rendered/device output. Do not accept a previous agent’s description instead of inspecting it.

The original PDF is `/Users/mac/Downloads/CT-Life-PRD-v2.2.pdf`. The user subsequently asked that the Claude-created artifact be the product baseline; the repository identifies `docs/spec/` as exports of that artifact. Earlier assistant-generated research is not a replacement for it. Dates, prices, hackathon rules and service availability in any old document are dated research, not newly verified facts.

## 2. User decisions and constraints

These are requirements carried from the conversation, not conclusions about the current implementation:

- Build a **Crypto Twitter lifestyle simulation game for Solana Mobile / Seeker**, with eventual business scope. CT is broader than one chain, but this release is Solana based.
- CT Life is the current project name. “Solana Street” was suggested, not a confirmed final brand. The user’s original comparison products were [Lagos Life](https://lagoslife.eliysites.com/) and [PH Lifestyle](https://www.phlifestyle.fun/); their current usage or popularity was not re-verified for this handoff.
- Preserve all six phases. The user explicitly rejected reducing the ambition because of time. Older submission dates in the spec do not authorize scope cuts.
- **ORE must be used; SKR must remain included.** Their integrations are not complete merely because they appear in plans or UI.
- The user is responsible for frontend/game experience; devIykee owns backend and Solana programs. The desired eventual integration target is under one hour, through aligned contracts and behavior. That target has not been demonstrated.
- The requested “dashboard” is the **signed-in in-game home/lobby on Seeker**. Carry a consistent identity into the website.
- The user criticized the entire presentation: buttons, text, icons, HUD, characters, audio and website feel. They want quality during construction, not an assumption that unattractive placeholders are acceptable until later.
- Supplied references include tactile, colorful mobile-game buttons/lobbies, prominent 3D characters, character cards, dioramas and game websites. These are references, not authorization to copy third-party art.
- Use inspected, appropriately licensed online models. The user rejected the mannequin/stick-figure direction. They chose **“Explore Snow/Rain with adult proportions.”** That is approval to explore, not acceptance of every derivative or permission to make everyone the same model.
- Build a varied cast, movement and NPC behavior. Two starter looks do not complete that requirement.
- Original generated image assets are allowed. An illustrated lobby hero must not be described as playable geometry.
- Solana Mobile should require wallet sign-in. Do not restore the public “enter demo city” path. Explicit development fixtures are separate.
- Default movement: analog control, drag strength for walk/run. Offer saved arrow controls. Inspect ground contact and glitches on Seeker.
- Sound should suit an engaging game. Runtime playback checks do not establish that the user likes the soundtrack.
- Vehicles and wider exploration were suggested; a full GTA-like open world was not settled as a requirement.
- Personal-avatar generation paid with credits/tokens is a proposal to cover generation costs. Provider, price, currency and financial flow are not settled.
- Research before significant changes; verify rather than guess. Agents are authorized and requested. Delegate independent work, then integrate and inspect it.

**No final user approval of the present visual design, characters, audio or overall quality is recorded.** Keep that separate from technical passes below.

## 3. Working boundaries

- Frontend owns Unity, art, animation, sound, client networking, wallet/RTC client and device performance.
- `contract/` is read-only: `contract-v1.0.0-rc1`, backend commit `160d59e`, synced 5 October 2026. It may lag backend main. Do not silently change IDs or invent accepted server behavior.
- FE additions go in `docs/fe-contract-proposals/`; discrepancies are in [backend-dev-feedback.md](../progress/backend-dev-feedback.md).
- Do not commit, push, publicly deploy, create accounts, sign in to new services, spend money or send messages to the backend developer as part of this handoff. Existing reports are for the user to forward.
- Do not perform real financial transactions. The ORE/mainnet requirements and safe test route remain unresolved in the payment audit.
- Prefer CC0 assets; document CC-BY attribution when used. Preserve `art/LICENSES.md`, `audio/LICENSES.md` and bundled notices. The workspace brief excludes Synty, Unity Asset Store assets, ActorCore, Ready Player Me, unprovided-login Mixamo access and Udio; re-read its rationale before choosing sources.
- One coordinator owns Unity import/build and adb. Agents must not launch competing editors or reinstall different APKs.
- **Do not run Unity during device performance measurements:** editor startup has restarted adb in this workflow. Coordinate Blender GPU work with Unity light baking.
- Never use global `AssetDatabase.SaveAssets()` casually in an isolated candidate importer. A failed attempt below modified unrelated materials. Snapshot exact current bytes and verify after the editor exits.
- Preserve candidate/source folders and failure evidence. “Source gate passed” is not “native passed”; “native passed” is not “device passed”; “device passed” is not “user approved.”
- No credentials are included here. Do not put wallet material, tokens, authorization URLs or unrelated notifications into reports.

## 4. Current source versus last APK

| Item | Current state and evidence |
| --- | --- |
| Unity | `6000.3.25f1`; native Android / IL2CPP project. No iOS validation in this checkpoint. |
| Latest APK file | `client/Builds/ctlife-quality-dev.apk`, **M5**, 166,503,982 bytes (158.8 MiB), SHA-256 `ae139b0d148731e98636d719629990837d58598e66c661242cecc39394ae6ecb`. |
| Last installation by this session | M5. Current installed state cannot be rechecked while adb has no device. |
| M5 build result | 87 seconds, 0 errors, 29 warnings. [Receipt](../progress/screens/quality-map-20261006m5/build.json); `client/Logs/quality-build-20261006m5.log`. A successful build is not a zero-warning build. |
| Newer than M5 | Map availability gating; Snow-specific walk correction/override; revised Vincent idle validation; candidate-only lighting saves and revised Timeline candidate transaction protection. These are not in the M5 phone build. |
| Latest native source run | `SnowWalkImport.ImportWalk`, retry B: exit 0, all 241 phases across Snow’s three world skins match frozen candidate evidence; minimum sampled sole −0.476 mm. `client/Logs/snow-walk-import.json` and `quality-snow-walk-import-20261006b.log`. Device validation pending. |
| Compilation | All player/editor assemblies passed `python3 client/Tools~/compile_check.py --editor` before the Snow retry; the retry also compiled/imported in Unity successfully. Future agent edits need their own checks. |
| Source snapshot | [checkpoint.json](checkpoint.json) hashes selected critical files. It is not a full repository archive. Recheck before relying on this document after later edits. |

An M5 copy was preserved at `/tmp/ct-quality-m5-before-stage.apk` for the material comparison. `/tmp` paths are convenience copies and may disappear. Retain the repository evidence and make a new safe backup before overwriting a needed binary.

## 5. Presentation, lobby, controls and website

### Implemented choices — not user-ratified style decisions

- UI Toolkit blue/gold presentation, raised outlined buttons, Lilita One display text, Manrope supporting text and filled/bold Phosphor icons.
- Original illustrated city diorama in the signed-in lobby; profile Cash/energy, home/dream presentation and shortcuts use the existing data path.
- Wallet-only normal sign-in. Development review mode uses labelled fixture data and is compile guarded.
- Analog default, persisted arrow option, walk/run thresholds, cancellation on menu/pause/focus interruption.
- Walk target 1.75 m/s, run 4.5 m/s. Earlier real touch tests covered analog/arrow gestures and release. Do not claim every two-finger camera interruption has been tested.
- Timeline camera distance 18.5 with existing pitch/FOV and a contact shadow. These are implementation choices, not established final framing.
- Local website under `website/`, previously inspected at `http://127.0.0.1:4178/`. Public deployment was not performed. Verify the local server is actually running before using it.

Actual Seeker images to inspect independently:

- [Lobby](../progress/screens/quality-review-20261006m4/quality-review-20261006m4/dashboard.png).
- [City/HUD](../progress/screens/quality-review-20261006m4/quality-review-20261006m4/city.png).
- [Creator](../progress/screens/quality-review-20261006m4/quality-review-20261006m4/creator.png).
- [Normal wallet-only launch, M5](../progress/screens/quality-map-20261006m5/touch/normal-launch-settled.png).
- [Long-name Settings tap](../progress/screens/quality-review-20261006m2/touch/settings-touch.png).

References and implementation records: [game UI direction](../progress/game-ui-direction-20261005.md), [UI research](../progress/ui-visual-research.md), [website report](../progress/website-quality.md), [current quality report](../progress/seeker-game-quality-20261006.md). Older reports contain historical pending items; use later dated evidence rather than assuming every “pending” or “passed” sentence is current. The seven original user-attached reference images are in the conversation; do not assume all original attachment files are packaged in this repository.

### Map correction waiting for an APK

- M5 unifies the phone Map with `CityMapScreen.BuildGuide`: district cards, icons and scrolling were checked on device.
- Actual travel to Builder Hills revealed a greybox. **Only Timeline Square currently has a bundled authored venue**; there are no built Addressables entries.
- Imported venue data lacks phase/scene fields, so the old UI’s availability count overstated implemented destinations.
- Current source counts only a local authored prefab or a built-in loadable scene as available and marks other venues “Opening soon.” This needs a new APK and real taps: count should reflect bundled availability, disabled rows should not travel, Timeline should still open.
- Files: `UI/Screens/CityMapScreen.cs`, `UI/Phone/Apps/CoreApps.cs`, `UI/Text/Strings.cs`, `Resources/UI/ctlife.uss` under `client/Assets/`.
- Later streamed venues remain in scope; a verified content-availability mechanism is still needed.

## 6. Characters and motion

| Character/work | Verified state | Still open |
| --- | --- | --- |
| Snow / Everyday | Registered authored creator + three world tiers; own Humanoid skeleton, atlas, hands and expressions. Latest Snow-only walk override native-passed. | Continuous corrected walk on Seeker; **run contact remains unresolved**; actual visible-LOD contact; lighter city tiers; broader wardrobe. |
| Rain / Breeze | Registered own rig, cloth/material and invalid-weight corrections; six-second Rain-only idle. Native and device evidence exists. | Run sole penetration and support sliding/cadence remain; no accepted corrective run clip. |
| Vincent / Weekender | Frozen creator/old world bundle; own Avatar, mappings, retarget and 20 morph/tier tests passed on native retry B. Shared-idle surfaces were subsequently measured over 52 meshes/13 phases. | **Unregistered**. Updated native importer must be rerun; new lighter world tiers need their own importer, native and device acceptance; proposed backend item IDs are absent. |
| NPC cast routing | Stable exact-look selection plumbing native-tested with a temporary Snow/Rain catalog. | No production role bindings enabled. Old fallback characters remain. Two looks are not a complete varied cast. |

Key source/evidence:

- `client/Assets/Scripts/World/Avatars/CharacterRegistry.cs` and `client/Assets/Resources/Characters/CharacterRegistry.asset`.
- `Editor/AuthoredCharacterImport.cs`, `RainCharacterImport.cs`, `RainIdleImport.cs`, `DirectMovementImport.cs`, `SnowWalkImport.cs`, `VincentCharacterImport.cs` under `client/Assets/Scripts/`.
- [Locomotion report](../progress/locomotion-quality-20261006.md), [Rain idle](../progress/rain-native-idle.md), [NPC cast audit](../progress/npc-cast-integration-audit.md).
- Exact Snow, Rain and Vincent proposed item/look IDs are in `docs/fe-contract-proposals/`. Do not fabricate server acceptance or a successful save.

### Snow walk — latest successful change

- Production shared walk reference speed is 1 m/s with Foot IK enabled; run remains 4.5 m/s reference with Foot IK off. This preserves the existing controller’s other states.
- The accepted native candidate changes only left/right foot-goal translation curves. Muscle/body/rotation curves are retained.
- `SnowWalkImport` copies the pinned clip into Snow’s own folder and assigns `Resources/Characters/SnowController.overrideController` only to the exact Snow look. Other registry entries and the shared controller are checked unchanged.
- Source candidate: `art/character-candidates/locomotion-quality-20261006/native-contact-goal-candidate/`.
- A first promotion attempt rejected an extra 10 mm absolute gap check: LOD2 was 10.06924 mm at one phase, while the original was already 10.068745 mm there. Retry B checks agreement with the frozen per-tier full-cycle data instead. This is an unchanged original airborne phase, not a new 4 mm full-cycle guarantee.
- The frequently quoted 3.979 mm result concerns corrected support in the original aggregate test; it is not the maximum gap for every tier and every phase.
- This correction is walk-only. A native Snow run probe measured −32.32 mm sole penetration at phase 0.0667. M4’s sparse settled run samples missed that phase; they do not demonstrate that running is corrected.

### Rain run — queued experiment, not a correction

- The first muscle-reconstruction approach failed to reproduce the original native pose. The later direct foot-goal approach does not move Rain’s feet with run Foot IK off.
- `art/character-candidates/locomotion-quality-20261006/RainFootGoalRebase.cs` is prepared and compiled in isolation, **not copied to Assets or executed**.
- Proposed next native method: `CTLife.EditorTools.RainFootGoalRebase.BuildRebasedRun`.
- It first measures translation/quaternion goal response, then attempts to reproduce the original IK-off pose with IK on. It must pass baseline joint/sole/rotation gates before applying a correction. Do not simply enable production run Foot IK.
- Even a successful contact experiment does not by itself solve run cadence/sliding.

### Vincent idle — rejected heuristic versus measured surfaces

- Native retry B failed a lateral-axis hand/thigh proxy at −17.8 mm. That proxy was not an intersection test.
- A separate native probe captured actual surfaces at 13 sampled phases (12 unique phases plus the repeated loop endpoint), across four creator/world renderer instances. The offline triangle-distance analysis includes boundary triangles and reports a minimum **2.919555 mm** separation; all 52 captured meshes exceed its explicit **2 mm engineering tolerance**.
- Evidence: [Vincent idle README](../../art/character-candidates/vincent-idle/README.md) and `native-shared-triangle-clearance.json` beside it. Rendered hands are also available there.
- The importer now repeats that geometry gate instead of treating the lateral proxy as pass/fail. **The updated importer has not yet been executed.** Region-selected surface checks are not whole-body containment proofs.
- A separate own-idle candidate exists, but is unassigned. It is not required merely to satisfy the rejected lateral heuristic.

### Vincent lighter world tiers — frozen candidate, no importer yet

- [Handoff](../../art/character-candidates/vincent-world-optimization/README.md); manifest `world-ready.json`, SHA-256 `2d6ef40a9af1aea354f678517979cd28cddb1a06381b974290323a27bb644d0b`.
- Near: **16,557 triangles**, maximum four weights, five facial shapes defaulting to zero, three 1024² maps.
- Far: **6,801 triangles**, maximum two weights, no facial shapes, three 512² maps.
- Same 55 bones/rest binds as frozen creator; all 30 digits retain weights. Source/export checks pass; the atlas and material are different for each tier.
- Native importer must not bind these meshes to the creator’s 2048 atlas or demand five morphs on the intentionally morph-free far tier.
- Enlarged shoe/buckle shading is a recorded concern. Intended-size renders exist; final native visual acceptance and crowd performance do not.
- **`VincentWorldImport.cs` does not exist yet.** No wrapper exposing the private Vincent idle geometry validator was added.
- Lighter Snow/Rain world candidates have **not been started** beyond reading Snow’s source inventory. No new files or selected budgets for them exist.

### Device gait diagnostic limitation

- `client/Assets/Scripts/App/Review/DeviceGaitSampler.cs` currently samples `AvatarView.Body`, the largest authored skin, which may not be the visible LOD. M4 recorded `bodyVisible=false`.
- Its proposed replacement was only discussed: cache per-renderer footwear selections, record observed visible renderer/mesh/actual LOD membership, and explicitly count missing/ambiguous observations. **No patch was written.** `Renderer.isVisible` would still be visibility evidence, not direct GPU draw proof.
- Diagnostic CPU skinning/raycasting changes timing. Never use these runs as performance evidence or force LOD/Animator evaluation to make a test pass.

## 7. Timeline scene candidate and a material restoration

Current canonical Timeline has baked lighting, authored environment assets, navigation and runtime lightmap unload/reload evidence. The proposed entry/fountain improvement is isolated under:

- `art/character-candidates/timeline-polish/` — source, license, matched renders, frozen manifest.
- `client/Assets/Scripts/Editor/TimelinePolishImport.cs` — stage/bake helper.
- Candidate target: `Assets/Art/Candidates/TimelinePolish/TimelineSquarePolish.prefab` inside the Unity project.

Candidate changes: a sourced Kenney basin with curved water geometry, connected paving, moved entry gardens/lamps, retained anchors/camera, rebuilt navigation. It is not published to the venue registry. Static water geometry is not animated-water acceptance.

**Failure that matters for continuation:**

1. First native staging failed a canonical-file hash guard after a global save reserialized five canonical materials and ProjectSettings. The bake did not run.
2. Comparing the exact pre-stage M5 APK found nine material-property differences: neon legacy `_Color` became green instead of white; four foliage materials changed `DoubleSidedGI` and `_AlphaToMask`.
3. Root restored only those nine verified fields with before/proposed SHA checks and a backup. An independent second comparison reports **zero runtime material-field differences from M5** across all five materials.
4. Historical YAML bytes and historical `ProjectSettings.asset` were not recovered. The APK PlayerSettings type-tree parser failed; this was not a failure to read the current YAML. No guessed restoration was made. Do not call this an exact full-workspace rollback.
5. The revised importer now snapshots protected assets/metas and all ProjectSettings, records all mismatches, restores captured bytes on failure, invalidates a failed receipt and saves candidate assets only. The shared lighting builder also saves only its explicit output directory.
6. **Revised native staging and baking have not been run.** Unity startup imports happen before the importer’s internal snapshot. Capture a coordinator-owned external baseline before launch and verify protected hashes after the Unity process exits, not only inside it.

Evidence: [stage failure audit](../../art/character-candidates/timeline-polish/stage-failure-audit/README.md), `semantic-restoration-applied.json`, `semantic-comparison-after-restoration.json` in that folder. Latest failure log: `client/Logs/quality-timeline-polish-stage-20261006.log`.

Backup details:

- `/tmp/ct-before-material-semantic-restoration-20261006.zip`: bytes before the nine-field restoration.
- `/tmp/ct-post-stage-material-baseline-20261006.zip`: **post-failure** baseline, not original pre-stage bytes.
- Revised importer creates durable per-run transaction backups in a named temporary directory and writes the location in its receipt. Copy needed evidence into a persistent handoff location after a run.

## 8. NPCs, audio, performance and incomplete gameplay

### NPC and audio checks

- M4 native NPC tests cover idle/follow/talk/resume, missing/late targets, cancelled actions, gestures, leaving, partial/disconnected paths and deferred state. [NPC report](../progress/npc-navigation-quality.md).
- A NavMeshAgent ordering issue was verified: use `ResetPath`, then zero velocity, then set `isStopped`. Do not revert based on a guessed interpretation of an older log.
- Eight licensed Kevin MacLeod full arrangements and six Kenney UI cues are imported and credited. M3/M4 checks cover streaming, shared lobby/Timeline phase, mute, music-free Spaces, ducking and bounded UI voices. [Audio report](../progress/licensed-music-quality.md).
- Current lobby/Timeline music is “Funky Chunk.” No subjective user listening approval or seamless-loop acceptance is established.

### Performance remains unaccepted

- Spec goal: 50 players + 12 NPCs, emoting, voice on, 10 minutes on Seeker, 60 fps target and at least 45 fps when hot; scene geometry target 100–150k visible triangles; APK target at most 150 MB. These are targets, not results.
- Most recent reported Rain crowd run, M3: 62 local Humanoid actors, no procedural actors, 165.32 sampled seconds, 60.19 weighted Unity-update fps. Worst-window 1% low was **30.1 fps**; render scale **0.6**, thermal throttling/culling active. [Report](../progress/screens/quality-perf-rain-20261006m3/summary.json).
- This was not ten minutes, not 62 visible actors, not connected multiplayer with voice, and not sustained Android presented-frame proof. Sparse geometry samples reached approximately 807k triangles; they are not measured peaks.
- M5 is 158.8 MiB, above the stated 150 MB APK target. Packaging and art budgets remain work.
- Do not quote “60 fps” alone as final performance acceptance. Avoid repeating short crowd runs unless changed assets/runtime or an unresolved question justify them.

### Whole-game completeness

- The spec contains six phases: city/identity; money/markets; voice/calls; city life; projects/ads; competition. Do not infer completion from a phone shortcut or a generated API model.
- No end-to-end acceptance for real wallet authentication, backend multiplayer, payments, ORE staking, SKR spending or calls is supplied by the review fixtures.
- [ORE/SKR audit](../progress/ore-skr-client-audit.md) records missing API wrappers/UI and unresolved network/session/confirmation, v0 transaction, stORE denomination and config inconsistencies. Recheck current source before implementing; the audit is dated 5 October.
- The imported contract lacks the exact starter-look IDs required for Snow/Rain/Vincent. Creator save must respect confirmed server config.
- Paid avatar generation, varied wardrobe, additional finished venues, cars, later gameplay and business launch remain incomplete or proposed. This quality checkpoint did not remove them from scope.

## 9. Reproduction and continuation

Tool paths on this Mac:

```text
Unity: /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity
adb:   /Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
Seeker serial previously used: SM02G40619141343
Package/activity: com.ctlife.app/com.unity3d.player.UnityPlayerGameActivity
```

Source compile, from the frontend root:

```sh
python3 client/Tools~/compile_check.py --editor
```

Android development build, after inspecting changes and preserving needed prior APKs:

```sh
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit \
  -projectPath /Users/mac/Documents/codes/projects/ct-life/client \
  -buildTarget Android \
  -executeMethod CTLife.EditorTools.BuildScript.BuildAndroid \
  -ctlifeOut Builds/ctlife-quality-dev.apk -ctlifeDev \
  -logFile /tmp/ctlife-next-build.log
```

Capture helpers: `client/Tools~/seeker_review.py` (performance) and `client/Assets/Scripts/App/Review/` (native development fixtures). Inspect arguments/source before running. Existing scratch launchers in `/tmp/ct_*` are not authoritative and may install or select an older APK/look.

Native methods already present, with their different purposes:

- `CTLife.EditorTools.SnowWalkImport.ImportWalk` — latest native pass; no need to rerun without changed inputs.
- `CTLife.EditorTools.VincentCharacterImport.ImportVincentAll` — updated idle gate still needs native retry; may publish registry/catalog entries if all gates pass. Snapshot before running.
- `CTLife.EditorTools.VincentIdleProbe.CaptureShared` — all-phase surface capture already completed.
- `CTLife.EditorTools.TimelinePolishImport.BuildCandidate` — revised protected staging, untested.
- `CTLife.EditorTools.TimelinePolishImport.BakeCandidate` — isolated GPU bake, untested. Keep graphics enabled; do not add `-nographics` on this Apple-silicon GPU lightmapping path.
- `CTLife.EditorTools.RainFootGoalRebase.BuildRebasedRun` — isolated source exists outside Assets; not callable until copied/imported deliberately.

**Suggested next checks, not a mandate to preserve the prior agent’s design:**

1. Inspect the original spec, user directions, current UI/device images and source. Decide independently whether the presentation meets the user’s brief.
2. Recheck workspace hashes, device presence and candidate receipts. Preserve the working tree. Establish one Unity/device owner if delegating.
3. Complete a new APK/device checkpoint for already integrated Snow walk and map availability. Measure actual visible-LOD soles rather than overstating the existing sampler.
4. Evaluate Vincent’s updated native import and lighter world candidate; inspect actual game lighting before accepting them or extending the cast.
5. Retry protected Timeline staging/bake with external byte checks. Do not promote an unbaked or failed candidate.
6. Continue Rain contact/cadence work and art/performance optimization with isolated experiments and retained failure evidence.
7. Return to the six-phase backlog and backend contract questions. A presentation checkpoint is not completion of the game.

The new agent may reject an implementation choice when evidence or the user’s requirements justify it. It should preserve settled user decisions, explain material changes, and avoid describing previous technical passes as final creative approval.

## 10. Agent checkpoint at handoff

| Agent name in previous session | Last work | Frozen state |
| --- | --- | --- |
| `/root/ct_character_quality` | Vincent lighter world assets; started reading Snow optimization inputs | Vincent candidate frozen. No new Snow/Rain optimization files; no active processes. |
| `/root/ct_contract_exam` | Locomotion/Vincent idle geometry; proposed visible-LOD gait sampler | Updated Vincent gate saved; Rain helper isolated. Sampler unchanged; no scratch patch; no active processes. |
| `/root/cleanup_docker_preflight` | Name retained from an older task; now Timeline/NPC/world work | Material restoration independently verified. Vincent world importer researched but not written. No active processes. |
| `/root` | Integration, Unity/Seeker checks, Snow walk, map, protection repair | Snow retry B native-passed; no new APK after M5. Writing this handoff; no native/device process active. |

Previous unrelated storage-cleanup, Notion-sharing and Codex-usage conversations are not the current task. Do not resume those merely because they appear in old conversation history.
