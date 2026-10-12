# Scene context actions — 2026-10-12

Status: the corrected source passed 66 focused Editor tests, including all 14 context cases, and the 24-shot native capture with 60 moving-camera/QA-picking stress frames. The final Seeker recheck remains pending.

## Research before implementation

- Epic's [Fortnite Button Device design example](https://dev.epicgames.com/documentation/fortnite/using-button-device-design-examples-in-fortnite-creative) attaches an invisible interaction device to a visible computer prop, gives it an interaction radius, and labels the action for that object. This supports making the selected scene object the prompt's reference, while preserving an explicit activation gesture. The official page was opened and read.
- Rare's official [interact prompt support article](https://support.seaofthieves.com/articles/360011910520-Ayuda-con-Fijar-indicadores-de-interacci%C3%B3n-a-la-vista) describes the normal prompt next to the looked-at object, with fixed screen positioning as an accessibility option. Its indexed official text was available; direct opening returned HTTP 403. No screenshot from it was inspected. The opened [Xbox Wire Season Five announcement](https://news.xbox.com/en-us/2021/12/02/break-new-ground-in-sea-of-thieves-season-five/) separately confirms sitting/sleeping as in-world social interactions; it does not establish a present-day UI design.
- Unity 6.3 [Camera.WorldToScreenPoint](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Camera.WorldToScreenPoint.html) defines screen origin and depth; offscreen points are still returned. [RuntimePanelUtils.CameraTransformWorldToPanel](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.RuntimePanelUtils.CameraTransformWorldToPanel.html) maps a world point into the actual panel, avoiding assumptions about device pixels or safe-area scale. [DefaultExecutionOrder](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/DefaultExecutionOrder.html) provides deterministic ordering relative to default-order camera scripts. All three 6.3 pages were opened before code changes.

## Source findings and implementation

The old `InteractionPrompt` was a fixed bottom-right button cluster. The existing `InteractionMachine` already separates offered, approaching, requesting and confirmed engagement states. It owns authoritative claims, busy/cancel actions and releases. The redesign retains that machine and its commands.

- `InteractionPromptSignal` adds the owning controller, selected target ID and an `InteractionPromptAnchor`.
- Idle/approach/request anchor: the selected spot's authored world position plus 0.8 m vertically. A nearer subsequent offer cannot replace an in-progress target's anchor.
- Confirmed engagement anchor: the local actor root plus 1.65 m for seated kinds, 2.1 m for standing kinds. This does not track animated hands/head bones. A destroyed actor invalidates its anchor.
- `InteractionController` emits a presentation tick in order-100 `LateUpdate`, after the default-order `CameraRig` and `AvatarView`. Prompt projection uses the live camera and converts panel coordinates into its HUD parent's local space. No interpolation is applied to a separately lagging camera projection.
- `ContextPromptPlacement` chooses the nearest above-target placement or a free candidate at actual obstacle edges. It respects the HUD safe-area parent, edge inset and obstacle clearance, and leaves the exact anchor uncovered. Invalid/behind-camera/no-free-space conditions hide the prompt. A prior-offset score discourages unnecessary side changes around obstacles.
- `Hud` supplies `InteractionPrompt.SetObstacles` with the header, needs controls, movement, social, personal navigation, chat and emotes. The prompt uses their current measured rectangles, including open overlays. HUD composition and styling remain coordinator-owned.
- The prompt retains all current action verbs and optional mic actions. A pressed button captures its owner and target; `InteractionMachine.AcceptsPrompt` rejects an old action after target/phase changes. Occupancy is refreshed before the HUD's `use` press is forwarded. Merely rendering, selecting or moving the prompt issues no command.
- The visual hierarchy is `ct-hud-interact` → `ct-interact-pointer` + `ct-interact-actions` → existing `ct-interact` buttons. The coordinator owns compact white/plum/mint styling. No strings, dependencies or art were added by this lane.
- `Dispose` unsubscribes both Bus and the static presentation event, unregisters the panel-attachment callback, and is idempotent.

## Verification prepared

`CTLife.Tests.ContextPromptTests` contains 14 expanded EditMode cases: clear above-object placement; six edge/control conflicts; nearby top-edge fallback; open chat with the wide mic-action row; continuous unobstructed pan; invalid/no-free-space hiding; actual claim-to-confirmed anchor transitions; stale target/phase rejection; and disposal/subscription cleanup. Existing `CTLife.Tests.InteractionTests` should run with it to retain authoritative reservation, occupancy and wire payload coverage.

Suggested coordinator filter:

```text
CTLife.Tests.ContextPromptTests|CTLife.Tests.InteractionTests
```

The capture agent is preparing the actual Timeline Square scene/camera with the actual HUD and isolated profile fixtures. Its declared synthetic prompt states are for layout stress only; they do not prove that a network seat claim completed. Actual sit/stand acceptance and Android touch/camera performance remain coordinator work.

No Unity, ADB, Git mutation, build or device action was performed by this lane for this task.

## Device layout error and correction

The coordinator observed repeated `Layout update is struggling to process current layout` errors on the Seeker after the real sit/stand flow. The reported stack enters `VisualTreeLayoutUpdater.Update` through `Panel.ValidateLayout`, `Panel.Pick` and `QaUiSnapshotExporter.Visit`. The attempted log export was empty after the device ring buffer rolled; the original error is available in the coordinator's tool output, not the empty `device-layout-first.log`.

Source inspection found `InteractionPrompt` registered `GeometryChangedEvent` to call `UpdatePosition`, which in turn wrote its own `left` and `top`, tail offsets and presentation classes. That is a layout-invalidating path inside the layout notification. An absolute intrinsic-width row also gets a different available width when its `left` changes near an edge. [Inference] these interacting layout writes account for the device recursion; the short captured stack alone does not identify the first style write.

The correction removes the geometry-positioning callback. Content updates and panel attachment now wait for the next measured frame; positioning runs from the existing post-camera presentation tick (or the explicit Editor capture call). Prompt and tail retain fixed layout origins and move using cached `style.translate`; unchanged translations and visibility do not write styles again. This follows Unity 6.3's [USS transform guidance](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Transform.html), opened during diagnosis, that transforms avoid recalculating surrounding layout.

The capture lane added 60 frames of real camera/actor movement and `QaUiSnapshotExporter.Capture` calls, which exercise the same `Panel.Pick` validation path. The action row changes from one to three buttons during this run.

## Corrected-source verification

Read directly from external evidence under `/Users/mac/Documents/codes/projects/ct-life-evidence/hud-ctos-20261012/`:

- `final-tests.xml`: 66 passed, 0 failed, 0 skipped after the transform correction.
- `native-stress/receipt.json`: `pass: true`, 24 screenshots, 60 moving-camera/QA-picking frames, 0 runtime errors, 0 layout-struggle messages, no failures, 0 REST writes, and restored services.
- Native projection is independently compared against camera viewport coordinates mapped into the rendered world layer. The actor-anchored Stand capture has a 0.000183-panel-unit projection error; visually its action is now above the rendered actor, rather than displaced to the upper-left capture viewport.

The native capture uses local scene/profile fixtures. Its synthetic Stand state demonstrates layout and tracking, not a successful server seat reservation. The coordinator's final Android build and Seeker recheck are still pending; the device error is not yet claimed resolved.
