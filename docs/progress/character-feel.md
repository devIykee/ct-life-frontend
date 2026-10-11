# Character feel (lane: character-feel, branch feat/character-feel)

READY TO COMMIT (round 2, verified 2026-10-08 on dev f8806eb incl. venues round 5): the exact 123 paths in
`ct-life-evidence/character-feel/round2/r2-paths.txt` = every modified/untracked file in this worktree except `*.mat`.
New: `client/Assets/Scripts/World/Avatars/{SeatFitCatalog,SeatPlacement}.cs`, `client/Assets/Scripts/Editor/{SeatFitBuilder,
SeatPoseBuilder,MocapImport,CastWeightRepair,CharacterFeelCapture.R2}.cs`, `client/Assets/Tests/EditMode/SeatFitTests.cs`
(all + `.meta`), `client/Assets/Resources/Characters/SeatFits.asset` (+`.meta`), `client/Assets/Art/animations/seated/`
(28 `.anim` + metas, + `seated.meta`, 11 MB), `client/Assets/Art/animations/mocap/` (3 FBX + metas, + `mocap.meta`),
`art/processed/animations/mocap/` (17 files), `tools/anim/` (4 scripts). Modified: AvatarView, CharacterIK, SeatPresenter,
LocomotionTuning(.cs/.asset), Knockdown, LocalPlayerController, CafeSeatMotionProfile, AvatarController.controller,
RainController.overrideController, CharacterFeelController, CharacterFeelCapture, ContentImport, CastValidation,
`act_{sit_down,stand_up,get_up}.fbx.meta` (loopTime 0), art/LICENSES.md, docs/progress/requests.md (reply to the café v2
request), this log — "feat(characters): fitted seats for every seat kind and both families, typing hands on the device,
cast f-family skin weights, CMU get-up/trip/run-stop, cast-wide run sole correction". Do NOT commit `*.mat` (import
re-serialization). Importer versions change (CTLifeImportPostprocessor 2, new CastWeightRepair / MocapPostprocessor): a
checkout re-imports every model once (~1 min here, 834 assets).
Verified: compile_check dev/editor/release 0 errors (only the pre-existing VenueVerify.cs warning); EditMode 1475/1475
(20 new SeatFitTests); batchmode PlayMode captures `r2seats`, `r2seatsall` (100 seatings), `r2perf`, `r2clips`, `r2run`
(the last two on dev 9f23592f: café-independent), and the round-1 regression `all` (0 T-posed actors, all 8 P2 scenarios ok).

## Round 2 (2026-10-08): seats, clips, cast looks, budget

Evidence: `ct-life-evidence/character-feel/round2/` — `seats/` (3/4 + side close-ups per seat kind × m / f trousers /
f dress, `*_exit.png` stand-up strips), `r2seats-receipt.json`, `r2seatsall-receipt.json`, `clips/` + `r2clips-receipt.json`,
`r2run-receipt.json`, `r2perf-receipt.json`, `regression/`, `seat-measure.json` (every seat, stool rung maps, work surfaces),
`seat-pose-build.json`, `seat-pose-metrics.json`, `controller-apply.json`, `cast-weight-scan-{before,after}.json`,
`looks-probe-*.json`, `mocap/` (Blender check renders), `editmode-results.xml`.

### 1. Seats: every seat kind in Spaces Studio v4, the café and Timeline Square, both families
- **Measured seats** (`SeatFitBuilder`, venue prefabs, render-mesh rays): 171 seats in 26 venues/homes classified Chair,
  Office, Bench, Pouf, Lounge, BeanBag, Stool with seat top, backrest, plateau, floor and (stools) the footrest bar from
  a rung map (Spaces stools: a 16 cm front bar at 0.25 m). Re-measured on round-4 venues (only bench_4 changed).
- **Fitted poses** (`SeatPoseBuilder`): per kind a seat-height blend tree (act_sit / act_sit_lounge 16° / act_sit_beanbag
  28° / act_sit_stool, feet together for the bar), each child solved per family in joint space so the soles land on the
  support at that height (Foot IK off: edited muscle clips carry stale goals). Derived from the UAL CC0 sit (licence row).
- **Placement at runtime** (`SeatPlacement` → AvatarView): the kind's state (or Codex's café fit), the blend height for
  the look's scale and a model offset: hips on the seat top minus the soft sink (bean bag 16 cm, sofa 3.5, pouf 2),
  lumbar 1.5 cm off the backrest, upholstered seats keep the calves in front of the front edge (deep poufs/sofas are sat
  on forward), stool soles on the bar; the offset ramps through sit-down / stand-up. CharacterIK then re-targets the
  pose's measured soles onto the real surface (rugs, stage trims, the stool bar) and lifts IK-goal seated states'
  (Codex café, typing) soles that would sink.
- **Result (production path, RemotePlayer + PlayerState, baked skin vs rendered seat):** 98 of 100 seatings (every seat
  of the three venues, m and f) within: pelvis gap −0.9…−0.1 cm on hard seats / within the sink on soft ones (−2.1…−17.2 cm
  for pouf…bean bag), soles −1.2…+0.7 cm, no backrest clipping. The 2 others are **timeline_square/bench_4**, where the round-4 gate railing runs through the bench's back
  zone (venue item): the sitter is placed 6 cm forward and renders cleanly, the probes still read the railing.
- **Root cause found on the way: the f family's shoes followed the head.** Every Rain-derived cast mesh (100 meshes: all
  f players + Hero, rain_breeze, 17 f NPCs) had 30–50% Head weight on shoe/ankle/hip vertices — the stale
  `DEF-COR-Hip_*/Ankle_*` groups the old conversion sent to Head, already fixed for Rain's own FBX in
  `art/character-candidates/rain-weight-correction/` but re-inherited by the cast export. Seated (and walking) f shoes
  smeared back under the knee, and the seat solve fitted those vertices (straight f legs). `CastWeightRepair` applies the
  reviewed rule at import (no Head influence on a vertex that also carries pelvis/leg weight below the neck; renormalize;
  bind poses matched to the rig in the source axis frame): 41 FBX repaired, scan 100 → 0 affected cast meshes.
  **Cast lane:** fix it at the export (`cast_extract.py`: filter groups by existing deform bones, never fall back to Head);
  the guard is then a no-op. `RainAdult_RelaxedIdle.fbx` (Codex idle source) still carries it; its mesh is not shown.
- **Dress looks:** the f skirts/kaftans/coats (kol, coat, lagos_ankara, lagos_kaftan) are skinned to the pelvis, so the hem
  hangs straight down through the seat when sitting. Placement uses the trousered f reference (same skeleton; the dress
  look's hips land within 0.1 mm of the trousered twin). The hem itself is a cast weighting item (thighs, not pelvis);
  the kol dress's back also sits 3.7 cm into the bench_1 backrest where the trousered looks clear it.
- **Transitions:** sit-down, stand-up and get-up FBX looped (`act_` default), so the cross-fade into the next state showed
  the clip's first frame: a 22 cm hip dip after every stand-up. Now one-shots (ContentImport rule + importer version).

- **Terminal Café v2 (venues round 5, new furniture, same ids):** re-measured and re-fitted. `cafe_chair_a`/`_b` seats are
  0.373 m (Codex's café fits were for 0.45 m chairs, so these use the fitted act_sit / act_type), the office chairs 0.455 m
  (Codex's typing fit still applies). A seat pulled up to a table is classified as a chair (the round-table `cafe_chair_b`
  read as a lounge chair), and the floor under the feet ignores the table's base plate. New: **typing hands on the device** —
  the work surface and the laptop/keyboard on it are measured per seat and CharacterIK puts both wrists on it (2.5 cm above
  the laptop at terminal_1–4, both families). The office keyboards (0.70 m ahead) are out of reach (wrists 0.11–0.25 m short,
  arms straight): anchor nudges requested from the venue builder (requests.md: terminal_5–8 0.25 m, terminal_1–4 0.10 m
  toward the desk). Table seats: hands rest on the table (wrists 1–2 cm above the top). Exits use the generic stand-up.

### 2. Clips (CMU free-use mocap; licence + NSF acknowledgement rows in art/LICENSES.md)
- `act_get_up_front` = CMU 140_02 "Get Up Face Down" (4.5 s, push-up, crawl, kneel, stand). Knockdown uses it when the
  ragdoll rests face down (before: roll over first); the root is placed under the lying body at the start and under the
  standing body at the end (its centre of mass from the IK pass; if the NavMesh owner keeps the root elsewhere, the model
  eases onto it instead of jumping). Verified on both families (`clips/*_getup_*.png`, no hip jump > 10 cm per sample).
- `react_trip` = CMU 104_13 "StumbleWalk" (1.5 s, on the spot): running into a kerb-high edge (< 0.45 m) trips instead
  of a full knockdown; taller props still knock down / bounce as before.
- `loco_run_stop` = CMU 104_09 "JogStop", decelerating steps only (0.8–2.4 s): a run that ends within 0.45 s plays it once,
  for every actor (AvatarView.SetSpeed; independent of the order owners set speed and the idle base).
- Import (`MocapImport`, menu CT Life > Characters > Import CMU mocap clips): each clip FBX gets its own Humanoid Avatar from
  its ASF rest pose after Enforce T-Pose (legs splayed 20° in the ASF), all spine bones mapped (upperneck stays in between:
  Unity reports a 12–26° in-between rotation mismatch, so the head lacks that neck segment's motion [Inference]).
- **Not shipped:** a walk-stop and 90/180° pivots. No clean CC0/free take exists in the downloaded sets (the subagent's scan:
  91_54/56 are hops, 13_26/27 turns are traffic gestures; no walk-stop). Turning in place keeps round 1's turn shuffle.

### 3. Codex's café fits and run correction on the cast looks
- Codex's fitted social/typing states are bound by family controller (SnowController / RainController), so every cast look
  plays them on the café furniture they were fitted to (others get the measured kind poses); their exits now work on cast
  looks too (the Snow exit's sole settle needs the original three-skin body; cast looks take the same exit without it).
  On café v2 only the office terminals (0.455 m chairs) still match Codex's typing fit: pelvis −0.6…−0.8 cm, soles on the
  floor (IK lift), wrists on the keyboard as far as it is reachable, and Codex's work exit plays and hands back to walking
  for both families (`exit-terminal5/r2seats-receipt.json`; the strip's camera is blocked by the wall there).
- **Run correction:** Codex's `SnowRunCorrection` profile was never in Resources (inactive in production) and its table is
  for the UAL sprint on Snow, while the run band plays the UAL Jog on every look. Replaced by a cast-wide rule: the walk /
  run-band planted-sole lift is measured per family offline (`LocomotionTuning.Family.{walk,run}SoleLift`: m 1.2 cm, f 3.1 cm
  run; walks ≈ 0) and CharacterIK lowers planted feet by it. Running on flat ground (`r2run`): stance contact (p10 of the
  lowest sole) m 1.6 → 0.3 cm, f 4.0 → 0.6–0.9 cm; brief toe dips at push-off now reach −2.5 cm (were −0.9 m / +1.3 f).

### 4. Crowd budget
`r2perf` (Spaces Studio, 35 actors, 11 at LOD0): 32 seated (fitted blends, offsets, seated foot IK) vs the same 35
standing: main thread 1.86 vs 1.75 ms; `Animator.ProcessGraph` 0.15 vs 0.07 ms, `AnimationClipPlayable.EvaluateClip`
0.05 vs 0.02 ms per frame (two-clip blends), `Animator.EvaluateIK` 0.08 vs 0.10 ms. [Inference] Editor batchmode on a desktop CPU, so these are
relative costs; the device check is the integration lead's. Seated IK is LOD0-only (2 raycasts per foot per frame).

### Open items
- Cast lane: f-family Head weights at the source; dress hems weighted to the thighs.
- Venues lane: timeline_square bench_4 vs the gate railing; café anchor nudges (requests.md), then re-run SeatFitBuilder /
  SeatPoseBuilder (Build + Measure) to refresh SeatFits.asset.
- Very high seats (0.72–0.80 m bar chairs without a footrest): f soles cannot reach (feet dangle by design).
- The face-down get-up is chosen only when the ragdoll rests face down (physics: 2 of 5 sprints in the final capture).

Round 1, merged on dev as 7618938 — was: READY TO COMMIT (Priority 2, includes Priority 1; verified 2026-10-08): the exact 281 paths in
`ct-life-evidence/character-feel/p2-paths.txt` = every modified/untracked file in this worktree except `*.mat`
(83 non-cast paths + the 99 cast `.fbx.meta` + 99 cast prefabs). New: `client/Assets/Scripts/World/Interaction/`
(7 files + metas, + `Interaction.meta`), `client/Assets/Scripts/World/Avatars/Physicality/` (CharacterBody, Knockdown,
RagdollRig + metas, + `Physicality.meta`), `World/Avatars/{CharacterIK,LocomotionTuning,SeatPresenter}.cs`,
`UI/Hud/InteractionPrompt.cs`, `Editor/{CharacterFeelController,CharacterFeelCapture.P2,VenueFeelAudit,CastValidation,CharacterFeelCapture}.cs`,
`Tests/EditMode/{InteractionTests,CastAnimatableTests}.cs`, `Resources/Characters/LocomotionTuning.asset`, 6 icons in
`Resources/UI/Icons/` (armchair, coffee, desktop, door-open, microphone-stage, person-simple-walk; + metas),
`docs/fe-contract-proposals/character-interactions-20261008.md`, this log. Modified: AvatarController.controller,
CharacterRegistry.asset, AvatarFactory/AvatarView/LocalPlayerController/RemotePlayer/NpcController/VenueSession/
OccupiedSpotAnimation, Hud/Strings/UiIcon, city-hud.uss, asset-sources.json, THIRD-PARTY-NOTICES.md, CastImport/
ContentImport/BuildScript/CTLife.Editor.asmdef, ProjectSettings/TagManager.asset (layers 8 Characters, 9 Ragdoll),
compile_check.py, art/LICENSES.md, docs/progress/requests.md — "feat(characters): player-chosen interactions,
physical body, ragdoll falls, grounding IK, speed-matched gait". Do NOT commit `*.mat`.
Verified: compile_check dev/editor/release 0 errors (only the pre-existing VenueVerify.cs warning); EditMode 940/940
(31 InteractionTests + CastAnimatableTests); batchmode PlayMode capture `all` PASS, 0 T-posed actors.

Round 1, merged — was: READY TO COMMIT (Priority 1 only, if merged before Priority 2; verified 2026-10-08 07:20): `client/Assets/Scripts/Editor/CastImport.cs`,
`client/Assets/Scripts/Editor/CastValidation.cs` (+`.meta`), `client/Assets/Scripts/Editor/CharacterFeelCapture.cs` (+`.meta`),
`client/Assets/Scripts/Editor/ContentImport.cs`, `client/Assets/Scripts/Editor/BuildScript.cs`,
`client/Assets/Scripts/Editor/CTLife.Editor.asmdef`, `client/Assets/Scripts/World/Avatars/AvatarFactory.cs`,
`client/Assets/Tests/EditMode/CastAnimatableTests.cs` (+`.meta`), `client/Tools~/compile_check.py`,
`client/Assets/Resources/Characters/CharacterRegistry.asset`, `client/Assets/Art/characters/cast/looks/*/*.fbx.meta` (99),
`client/Assets/Art/characters/cast/prefabs/*.prefab` (99), `docs/progress/character-feel.md` — "fix(characters): every
cast look owns its Humanoid Avatar; no T-pose (root cause + build gate)". Exact 212-path list and a frozen copy of these
files as verified (in case Priority 2 edits land in the worktree before you merge): `ct-life-evidence/character-feel/p1-paths.txt`,
`p1-snapshot.tar`, `p1-tracked.patch`. Do NOT commit any `*.mat` (Unity URP re-serialization from my imports).
Verified: compile_check dev/editor/release 0 errors (1 pre-existing warning, VenueVerify.cs); EditMode 909/909 incl.
`CastAnimatableTests`; batchmode PlayMode capture PASS (below).

## Priority 1 — every character in a T-pose on device (fixed, verified in batchmode PlayMode)

### Root cause
Two failures of the same mechanism: **a character prefab whose Animator has no Humanoid Avatar.** The shared
controller holds only Humanoid (muscle) clips, so an Animator without an Avatar cannot pose the skeleton and the mesh
renders in its bind pose, which for these rigs is a T-pose.

1. **Cast looks (player + NPCs, since 60c5fcb).** `CastPostprocessor` imported every cast FBX with
   *Copy From Other Avatar* (Snow avatar for family m, Rain for f). Copy From Other reuses the source Avatar's
   skeleton description, which names Snow/Rain's own root and mesh nodes (`SnowAdult_Creator`,
   `SnowAdult_CreatorMesh`; Rain likewise). No cast look has those nodes (`npc_x`, `npc_x_LOD0..2`), so Unity built
   **no Avatar and added no Animator** to the model. Unity logs nothing for this in the console. `CastImport.BuildPrefab` then ran
   `GetComponentInChildren<Animator>() ?? go.AddComponent<Animator>()` and baked an **Avatar-less Animator into all
   99 cast prefabs** (`m_Avatar: {fileID: 0}`, verified in the committed YAML). The cast import also re-pointed the
   Snow/Rain look keys (the local player's look) at those prefabs and cleared their tuned controller overrides, so
   the player T-posed too. This happens on any machine, warm or fresh Library: the broken Animator is in the
   committed prefabs.
2. **Pre-cast NPCs (device screenshot `integration/seeker-dev-0043/02-city.png`, 00:44 Oct 8).** Same mechanism:
   NPC roles resolved to the old MakeHuman FBXs under `Art/characters/npc/`, imported Copy From Other
   `ctl_male_body`. Those models also get no Avatar and no Animator (re-verified: force-reimporting
   `npc/normie_tourist.fbx` with the source present still gives none), and `AvatarFactory` added a bare Animator →
   grey T-pose. In the same screenshot set (`07-after-walk.png`) the **local player (Snow, own Avatar) is animated**.
   Why it "worked under Codex": Codex's NPC evidence came from review fixtures. `NpcCastReview` built an in-memory
   `NpcCastCatalog` that bound NPC roles to Snow/Rain, which have their own Avatars. No `NpcCastCatalog.asset` was ever shipped (none in git
   history), and `review=1` was removed in 2766bc1, so in the real flow every NPC took `legacy-fallback`. Codex's own
   audit already flagged both risks (`character-runtime-audit.md` lines 9 and 46).

Contributing fragility (fixed too): two model postprocessors loaded *another* asset's Avatar during import
(`AssetDatabase.LoadAllAssetsAtPath`) without declaring a dependency. On a fresh Library or a mass reimport the
source is often not imported yet. `CastPostprocessor` then left whatever the next postprocessor set, and
`CTLifeImportPostprocessor` (which also matched every `Assets/Art/characters/` path, cast included) **rewrote**
clips/parts to CreateFromThisModel and copied 22 cast looks from the MakeHuman body. Observed directly in this
worktree's first import: 47 × "source avatar missing", 33 clip metas flipped, 22 cast looks bound to `ctl_male_body`.

Not the cause (checked): 55-bone naming (cast hierarchy names match Snow/Rain exactly), the clips (all
`humanMotion`, valid), LODGroup children (one Animator on the root), Animator culling with LOD (the game camera
renders every frame on device; the capture harness reproduces that).

### Fix (at the source)
- `CastImport.ConfigureAvatar`: each look gets its **own** Humanoid Avatar the way Snow/Rain build theirs
  (`AuthoredCharacterImport`): the shared 54-bone Snow/Rain map plus a skeleton description captured from the look's own
  rest hierarchy after Unity's Enforce T-Pose. It is stored in the look's `.meta` (`avatarSetup: CreateFromThisModel`,
  `userData: ct-life:cast-own-avatar-v1`), so importing needs no other asset and a fresh Library imports
  identically. Menu: CT Life > Cast > Rebuild look Avatars. Batch: `CastImport.RepairBatch`.
- `CastPostprocessor`: no longer touches avatar settings and never reads another asset. It optimizes the hierarchy
  only once the Avatar is configured.
- `CastImport.BuildPrefab`: requires exactly one root Animator with a valid Humanoid Avatar and throws otherwise
  (no `AddComponent<Animator>()` fallback). All 99 prefabs were rebuilt; they now use the model's own Animator.
- Registry: the Snow/Rain tuned motion is restored per family (m looks → `SnowController` contact-fitted walk,
  f looks → `RainController` own relaxed idle). Both are Humanoid muscle clips.
- `CTLifeImportPostprocessor`: skips the cast; declares `context.DependsOnArtifact(ctl_*_body.fbx)` and keeps the
  stored setup instead of downgrading when the body is not imported yet.
- Gates: `CastValidation` (every registry/cast character: one root Animator, valid Humanoid Avatar, all controller
  clips Humanoid, and sampled idle/walk/sit **span/height ≤ 0.7**; a T-pose is ~0.9). Wired into
  `BuildScript.BuildPlayer` (the build fails rather than ship a T-pose), `CastImport.Run`, and the EditMode test
  `CastAnimatableTests`. `AvatarFactory` logs a dev warning if an Avatar-less character is ever spawned.

### Evidence (`/Users/mac/Documents/codes/projects/ct-life-evidence/character-feel/`)
- `cast-validate-before.json`: 99/99 prefabs "Animator has no Avatar"; model import state per look.
- `cast-validate-after.json`: 0 problems; per-prefab pose span (idle 0.27–0.48, walk 0.32–0.55, sit 0.40–0.66).
- `p1-receipt.json` + frames from the **game camera** (CameraRig with each venue's authored framing; AvatarLod crowd
  ranking and CullCompletely as in the game; player through `LocalPlayerController`, NPCs through `CreateNpc`):
  `{timeline_square,terminal_cafe,spaces_studio}-{idle,idle-close,walk-strip,sit,sit-close}.png`.
  Looks: players `player_m_kol`, `player_f_lagos_ankara`; NPCs `rekt_guy`, `giveaway_guy`, `spaces_host`. All 15
  actors are in the requested state in every venue (`loco_idle` / `loco_walk` / `act_sit`, one `act_type` at a café
  terminal); span 0.27–0.59, none near T (PASS).
- Re-run: `Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.CharacterFeelCapture.Launch`
  (no `-nographics`), and `...CastValidation.ValidateBatch -ctlife-castReport <json>`.

### Merge notes for the integration lead
- After pulling, Unity reimports the 99 cast FBXs from their updated metas. No manual step. A fresh clone imports
  correctly too. The build now fails loudly if any spawnable character cannot animate.
- My first fresh import re-serialized ~156 `.mat` files (URP AssetVersion sub-asset) and flipped clip/part metas via the
  old postprocessor. I restored all of those; none are in the commit.
- [Inference] The device should show all characters animated after this merge; I have not run it on the Seeker
  (out of my lane). Verify there: Timeline Square / Terminal Café / Spaces Studio, idle, walk, sit.

## Open items (Priority 1)
- Café/Studio fitted seat states (`CafeSeatMotionProfile`) bind only to Snow/Rain's exact original Avatars, so cast
  looks use the generic `act_sit`/`act_type`. Seated hands/feet contact is part of Priority 2c.
- `SnowRunCorrection` (Codex's run fix) matches only the original Snow prefab; cast looks run uncorrected.
- `AuthoredIdleImport`/`RainIdleImport` throw on a fresh Library (they require their creator Avatar first). That's harmless
  (the stored settings apply), but noisy. Not changed (Codex's importers).

## Priority 2 — feel (built, verified in batchmode PlayMode)

### a) Player-chosen interactions (`World/Interaction/`, `UI/Hud/InteractionPrompt.cs`)
- Data-driven from the venue's SpotAnchors and the contract spot registry (`InteractableCatalog`): seats/benches/sofas,
  stage chairs, terminals, the host's mic (stage spot with a standing anim), the door (`exit`), and a customer-side
  counter for an `npc` counter spot. Terminal Café yields 8 terminals, 4 table seats, door, counter.
- `InteractionFinder` offers the nearest free target (range per kind, facing preference, hysteresis; a tapped seat is
  offered from up to 14 m). **Nothing sits by itself**: a tap only focuses the CTA (`VenueSession.OnSpotTapped` no
  longer walks and sits).
- CTA: a big brand-yellow pill above the right dock ("Sit" / "Sit on the bench" / "Use terminal" / "Take the mic" /
  "Lean on the counter" / "Leave"), then "Stand up" / "Step away" (mic adds Wave / Hype emotes). A pulsing floor ring
  marks the target.
- `InteractionMachine` (pure, tested): Idle → Approaching (walk to the approach) → Requesting (`sit {spotId}`) → Engaged
  on the server's `spotId` echo; rejected / 5 s no-answer (releases with `stand`) / stick input cancel; follows the
  server on rejoin. Local-only fallback (no room, or kinds the contract has no message for) engages on this client.
- Networked: the existing `sit` / `stand` / `PlayerState.spotId/anim`. Others see it through the same presenter (below);
  the mic pose is a shared FE rule. Proposal for counter/door/reactions/seated poses:
  `docs/fe-contract-proposals/character-interactions-20261008.md`.
- Sit-down / stand-up for everyone (`SeatPresenter`): the measured clips (act_sit_down keeps the feet on the seat root
  while the hips travel 0.25 m back / 0.33 m down; act_stand_up reverses it). It steps/turns onto the seat root, plays
  the clip, then loops; the stand-up mirrors this. It moves only the visual model; the logical root stays server/NavMesh-owned.
  `AvatarFactory` now gives every world avatar a logical root with the model as a child (the Snow world-prefab
  structure Codex's café exit already needs). NPCs `act` at a seat: walk to its approach, sit down, stand before leaving;
  NPC-held seats are not offered.

### b) Physicality (`World/Avatars/Physicality/`, layers 8 Characters / 9 Ragdoll)
- `CharacterBody`: the local player's CharacterController (r 0.28, step 0.3) moves in direct control while the agent
  follows (`updatePosition` off, `nextPosition` synced), so props with colliders block the body even where the NavMesh
  does not carve them. NPCs and remote players get a kinematic body capsule, so you bump into people.
- Ledges: when the stick pushes past a NavMesh edge with ground ≥0.45 m lower and no wall, the body steps over the rim and
  falls with gravity. Landing is soft (jump_land), hard ≥1.6 m (react_roll), or a knockdown above 2.6 m; then the agent
  re-binds where it landed.
- `Knockdown`: a bump at ≥2.4 m/s staggers (recoil, ~0.55 s without control); ≥3.4 m/s knocks you down. It trips over
  props under 0.9 m and bounces off taller ones; running into an NPC knocks them down too. Ragdoll (`RagdollRig`):
  11 bodies built on demand. It is LOD0 only, within 14 m, and **at most 2 at once** (the third falls animated); the
  character is de-optimized for the fall and re-optimized after. The fall starts from the current pose (the animator
  is re-posed after de-optimizing). On rest, a face-down body rolls onto its back (no face-down get-up clip yet).
  Then the root is re-placed so act_get_up's first (lying) pose matches the body, every bone blends from the rest
  pose over 0.45 s, and control returns. The local player's fall is shown to the room via `emote react_liquidated`
  ([Unverified] server acceptance).
- Venue colliders: validator `VenueFeelAudit` + the list in `requests.md` (Timeline Square: 23 walk-through regions,
  all 6 benches; Café/Studio clean).
- Fixed on the way: `LocalPlayerController` measured speed as last frame's move over this frame's delta time, so
  uneven frames tripped the warp guard and flickered idle/run crossfades (the run never completed a cycle). Direct
  moves now report the body's true velocity.

### c) Grounding (`CharacterIK`, IK pass on; LOD0 / visible / not knocked down only)
- Feet: planted feet on the real ground (steps, slopes) and never below it; the pelvis drops for a lower foot. It only
  runs in states with Humanoid Foot IK, where IK goals were verified to track the foot bones within 1–10 mm
  (`probe_ik_goals`, `gait` "IK goal vs foot bone"). The builder now enables Foot IK on idle/run/idle variations.
- Look-at: head/eyes toward the nearest speaker; NPCs toward nearby people; seated people toward someone nearby; the
  local player glances at the camera when idle.
- Hands: rest on a table edge with elbow hints, but only within easy reach (≤0.45 m from the seated hips). Café chairs
  are 0.67 m from their tables, so hands stay in the lap there (as authored).

### d) Animation quality (`CharacterFeelController` builds the controller in place, idempotently)
- 22 imported-but-unwired CC0 clips wired as states (sit-down/stand-up, get-up, falls, jump/land, roll, idle
  variations, counter lean, interact, jog...). `ContentImport.BuildAvatarController` now upserts and never recreates
  (it used to wipe Codex's hand-tuned states).
- Speed-matched blend: one Locomotion tree (walk 2.2 m/s ↔ run band 3.6 m/s) behind both loco_walk/loco_run (switching
  never restarts the cycle), damped speed, `locoRate` = speed / native ground speed. Walk keeps Codex's 1.0 m/s reference
  (measured in motion: stance slide 0.12 m/s at 1.0 m/s and 0.23 at 2.0 m/s, the same as the baseline; my static fit of
  0.895 slid more, so it was rejected). Run band = UAL Jog (measured 4.47 m / 4.91 f m/s → 1.0× at 4.5 m/s) instead of
  the Sprint, which needs 0.62–0.77× there ("slow bounding", ct_motion.md). Reversible: `CharacterFeelController.RunBandClip`.
  The estimator matches Codex's Rain sprint fit within 0.6%.
- Idle variations every 9–18 s (look around / stretch / fold arms; LOD0, not talking); turn in place steps at walk
  cadence instead of spinning on the spot.

### Evidence (`ct-life-evidence/character-feel/`; `all-receipt.json` has every metric)
- `p2/p2-cta.png` (Sit CTA + ring), `p2-approach_sit-strip.png` (Cancel → walk → sit-down → "Stand up"),
  `p2-stand_up-strip.png`, `p2-remote_sit-strip.png` (a remote PlayerState seat), `p2-npc_sit-strip.png` (NPC walks and
  sits; that seat is not offered), `p2-walk_into_table-strip.png` (0/140 frames with a prop inside the body; stops
  2.04 m from the table centre), `p2-sprint_fall_getup-strip.png` (4.5 m/s → ragdoll → rolled → get-up; control back in
  4.1 s), `p2-rekt_budget-strip.png` (3 at once: 2 ragdoll + 1 animated, 0 leaked), `p2-ledge-strip.png` (1.97 m drop →
  roll landing), `p2-feet_step_ik_off_on-strip.png` (foot through the step vs on it), `p2-look_at_speaker_off_on-strip.png`,
  `p2-hands_on_table_off_on-strip.png`, `p2-gait_speeds-strip.png`, `p2-idle_variation-strip.png`, `p2-turn_in_place-strip.png`.
- `controller-apply.json` / `clip-measure.json` (clip ground speeds, hips tracks, get-up/fall poses);
  `venue-physicality-audit.json`.
- Re-run: `...CharacterFeelCapture.Launch -ctlife-captureSet all|p1|p2 [-ctlife-scenario <name>]` (no `-nographics`);
  `...CharacterFeelController.ApplyBatch` (after any clip change); `...VenueFeelAudit.RunBatch -ctlife-auditOut <json>`.

### Budgets (Seeker)
IK only for LOD0 (nearest 12), visible, not knocked down: ≤4 raycasts per character per frame; attention re-picked
every 0.3 s. Ragdolls cost nothing until a fall: at most 2, LOD0, within 14 m. Animators keep CullCompletely + AvatarLod's
forced LOD and the 25 m cull. A manual lower animator rate for LOD1/2 was not added: per-animator `Animator.Update` would
lose Unity's batched, multithreaded animation. [Inference] The added Humanoid Foot IK and IK pass are small per
character; **device frame time is not measured** (integration lead: crowd of 35 in Timeline Square).

### Open items
- Device check (integration lead): CTA layout, sit/stand feel, a sprint fall, the gait at walk/run, and fps with a crowd.
- Venue lane: colliders + NavMesh carving per `requests.md`. Until then, agent-driven paths (approach walks, NPCs) can
  clip uncarved props (the direct-control body cannot).
- Contract: proposal filed (interact / reactions / seated poses); confirm whether `emote react_liquidated` is accepted.
- Clips we lack (CC0, sources verified by the sourcing agent in `ct-life/docs/progress/animation-sources-20261008.md`;
  nothing downloaded or imported by me): **face-down get-up** (CMU 140_01/140_02; needs ASF/AMC→BVH→FBX, Blender is at
  /Applications/Blender.app; until then bodies roll onto their back), **true trip** (CMU 104_13 trimmed; today a
  physics trip), **walk-stop** (CMU 104_14 region), **in-place pivots 90/180 L/R** (CMU 91_56/91_54 trimmed + mirrored;
  today the turn shuffle), **seated variants** (KayKit Sit_Chair_Idle, CC0; needs the seated-pose contract field to be
  seen by others). Record each in art/LICENSES.md when imported.
- Unchanged and still Snow/Rain-only: Codex's fitted café seat states and SnowRunCorrection (exact original Avatars).
- The UAL Jog's documented sole depth is handled by the ground clamp in CharacterIK at LOD0; distant runners use the raw clip.
