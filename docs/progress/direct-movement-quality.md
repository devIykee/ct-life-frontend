# Direct movement and grounding — 2026-10-05

The user requested direct analog movement, walking/running from drag strength, an arrow
option, and investigation of floating feet. This replaces ground tap navigation as the
default interaction; avatar/NPC taps and intentional walk-to-seat actions remain.

## Research and design

Apple's official handheld-game guidance recommends a broad movement touch area, integrating
sprint into farther thumbstick drag, separate camera gestures, visible press feedback,
safe-area placement, and hiding gameplay controls while menus are open. It also discusses
tap movement for overhead games; the user's explicit preference here is direct movement.
Source opened: [Design great interfaces for handheld games](https://developer.apple.com/videos/play/meet-with-apple/243/),
movement/control placement sections. Android's
[natural input guidance](https://developer.android.com/games/develop/multiplatform/enable-natural-input-on-all-form-factors)
also describes touch callbacks receiving mouse/stylus input; Unity UI Toolkit pointer
events supply the shared input path here.

The lower-left control uses the existing UI safe-area layer. The UI owner is styling a
420×420 reference-unit area, a 300-unit ring and 124-unit thumb/arrow hit targets; sizes and
colors stay in shared USS. Dynamic stick translation alone is set by the control code.
The arrow glyphs use the bundled Phosphor caret asset, rotated by USS. The second thumb
can still pan/zoom the camera through the world-input layer.

## Implemented source

- `Core/Services/ControlSettings.cs`: persisted local-only Analog (default) / Arrows choice.
  Shared gain policy: dead zone 0.14; run enters at 0.82 and exits below 0.70; walk tops out
  at 2.2 m/s, run at 4.5 m/s. Arrow hold is walking strength; outward drag reaches running.
- `UI/Hud/MovementControls.cs`: pointer capture/release/cancel/capture-loss handling;
  opposing arrows cancel and diagonals do not increase speed. Cancels on hidden HUD,
  menus/modals/text input, mode change, detachment and venue change. Input generations
  discard stale gestures after a server correction/seat confirmation without cancelling
  a new intentional seat path. `SettingsApp` exposes the persisted choice.
- `World/Players/LocalPlayerController.cs`: camera-yaw-relative input; manual velocity
  through the existing NavMeshAgent; NavMesh edge projection for wall sliding, edge stop
  and contract walkable checks. No new wire message or authoritative coordinate exists:
  `VenueChannel.Move` still sends only `{x,z,rotY}` at its existing 10 Hz limit.
  Release/pause/UI/seat/correction cancels motion. Missing refreshed input times out after
  0.25 seconds. Deliberate `MoveTo` remains for seat actions and development review.
- Actual XZ displacement drives animation speed and footstep cadence. Separate animation
  enter/exit thresholds limit repeated walk/run/idle crossfades at boundaries.
- `WorldInput` no longer emits ground navigation; the removed VenueSession ground handler
  is the only venue-session behavior changed. Avatar/NPC/claimable-seat taps are preserved.
- `Editor/DirectMovementImport.cs`: changes only `loco_walk` and `loco_run` state rate
  settings. Uses the existing `speed` float at nominal rates 2.2 and 4.5 respectively.
  Keeps every motion (including native idle), other states and controller GUID; restores
  saved controller bytes on failure. Verifies actual Animator state rate/multiplier at
  nominal and half speeds, plus idle rate, when root runs it in Unity.

Unity's [NavMeshAgent.velocity API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMeshAgent-velocity.html)
explicitly supports player-controlled velocity while retaining NavMesh constraints. It
overrides automatic acceleration and avoidance, so this code sets its own acceleration,
turning and boundary checks and resets velocity when relinquishing direct control.
The pinned 6000.3.25f1 XML and
[AnimatorState speed-parameter API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Animations.AnimatorState-speedParameterActive.html)
were checked before implementation. Native collision/animation acceptance still needs Unity.

## Grounding evidence and correction

[The H device log](screens/quality-review-20261005h/device.log) records player root
`(0.00,0.08,-14.00)` during its final framing check. `VenueContentBuilder.BuildGround`
places the Timeline paving top and walkable BoxCollider top at y=0. The navigation settings
have 0.1667 m cells and `buildHeightMesh: 0`. The player's capsule is a trigger used for
picking, not a physical ground constraint. The old controller never adjusted NavMeshAgent
`baseOffset`.

The controller now samples navigation height and independently raycasts downward to a
non-trigger, upward-facing floor collider. Their measured height difference becomes the
agent's base offset, checked at entry and while moving. No fixed −8 cm correction, source
mesh change, clip change or network Y field is introduced. This addresses the observed
navigation-plane gap; it does not establish that every animation sole is correctly
planted. Native idle sole/loop checks and actual moving-foot device review remain separate.

## Verification and root handoff

Pinned Roslyn compilation at final checkpoint: Core 21, World 25, UI 25 and Editor 10 files all passed. An initial
UI compile found two unassigned local callback variables; those were initialized and the
four assemblies passed on rerun. `git diff --check` passed for the modified existing files.

A scratch C# harness compiled the actual `ControlSettings.cs` against a PlayerPrefs-only
stub and passed **620 checks**: default/restore/invalid preferences, notification behavior,
dead zone/invalid input, monotonically increasing drag, speed cap, separate run thresholds,
boundary jitter, arrow walking, running and release. This is input-policy verification,
not a simulation of Unity touch events, NavMesh or physical collisions.
Harness: `/var/folders/zm/v0w94xb95p11_5df6rxm0js80000gp/T/ct-controls-regression-lnnul2cq`.

Root alone runs Unity/device. Run this synchronous method after native idle import and
after any `ContentImport.SyncAll` (that existing method recreates controller states):

```sh
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit \
  -projectPath /Users/mac/Documents/codes/projects/ct-life/client \
  -executeMethod CTLife.EditorTools.DirectMovementImport.ConfigureLocomotion \
  -logFile /Users/mac/Documents/codes/projects/ct-life/client/Logs/direct-locomotion-import.log
```

Device acceptance still required:

1. Analog short drag walks; farther drag runs; releasing stops; arrows persist after restart.
2. Camera-relative up/sideways movement faces its actual velocity after turning. Diagonals
   stay below 4.5 m/s. Wall/edge holds remain on NavMesh and do not animate running in place.
3. Phone, dashboard, pause, seat confirmation, venue change and server correction release
   the captured gesture; closing UI does not resume an old held input. Seat taps still work.
4. `LocalPlayerController.ReviewSnapshot()` reports input, actual speed, run state,
   navigation/floor heights, base offset, root-to-ground gap, input epoch and pending path.
   Timeline's previously observed root gap should be measured again rather than assumed.
5. Inspect shoe contact and cadence during slow walk/run/turn and transitions. The helper's
   nominal clip rates preserve the existing authored gait timing at 2.2/4.5 m/s; they are
   not a newly measured stride calibration or foot-IK pass.

No Unity/editor/adb execution, audio change, backend change or world regeneration was
performed by this agent for this task.
