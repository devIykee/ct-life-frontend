# Vehicle controls lifecycle — 19 October 2026

**Status: final native suite passed 2,067/2,067, including all 13 lifecycle cases, with zero failures or skips. The integrated APK is installed; the scoped device review below confirms ordinary entry, driving, map close/fresh input and exit. Held-input modal cancellation and device focus/pause remain unverified.** This is an input-cleanup task. It does not establish the source of the unexplained device activity described in [the input observation](audit-input-observation-20261019.md).

## Source evidence

The baseline `VehicleControls` stored stick axes and pedal booleans until pointer release, cancel or capture loss. Its `ReleaseAll` cleared those fields but left owned captures, stick visuals and `veh-down` classes intact. `Bind` called that incomplete reset; `Dispose` only removed subscriptions/updating. It had no focus, pause, detached-panel, hidden-HUD or overlay guard. `WorldInputLayer` already had these lifecycle guards and used the existing `Runner.ApplicationPaused` publisher.

Consequently, pink Brake/Down styling alone cannot date a live pointer press: it can survive a vehicle rebind after the logical boolean has been cleared. A missing release could retain a hold in the same bound vehicle. Neither observation explains the entire compact-drive, exit, helicopter-entry and flight sequence. No runtime synthetic pointer-injection call was found in the searched project-owned C#/Java/Kotlin/XML source; editor and test event injection is separate. This is a source-search result, not proof about all native, system or external input paths.

An additional baseline case was Roadstar flight: `Update` hid the handbrake while `Flying` without clearing its held state/capture. The repair releases this one hidden control while preserving the other thumb's gas/steering.

## Primary-source research and scope

Unity 6.3 documents that pointer capture routes subsequent pointer events to the capturing element until release/loss, and identifies capture-out as the loss notification. Explicit cleanup must therefore release owned captures as well as logical values. [Unity capture events](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Capture-Events.html)

Unity provides a focus-change event and a separate pause callback. Android can lose focus when the keyboard appears; pause handling is also needed for the Home-button case. These are cancellation boundaries, not reasons to resume a prior hold. The proposed implementation follows the project's existing `WorldInputLayer`/`Runner` wiring. [Application.focusChanged](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Application-focusChanged.html), [OnApplicationPause](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/MonoBehaviour.OnApplicationPause.html)

No stationary-hold timeout, movement tuning, UI styling, new input injection, or shipping test API is proposed. Modal detection must retain the existing visible-child/`PickingMode.Position` rule so click-through social overlays do not block controls. Cleanup must clear HUD state without overwriting VehicleSystem's busy exit/landing or explicit scripted input.

## Validation plan

`CTLife.Tests.VehicleControlsLifecycleTests` exercises the actual controls on an attached UI Toolkit panel with real pooled pointer events, captures, Bus vehicle binding and `Vehicle.Input`. Test-only reflection supplies pointer IDs, raises the existing focus/pause publishers, and isolates/restores singleton ownership. Cases cover rebind, focus, pause, modal blocking, hidden HUD, detach, disposal, stale pointer release, stationary simultaneous gas/steering, Roadstar hidden handbrake and busy/scripted command ownership.

The coordinator owns Unity execution and device checks. The source before the repair is retained outside the repository in the session scratchpad. Native failures must be recorded before the production change; passing tests will establish the exercised lifecycle behavior, not incident attribution. Device background/resume and modal cancellation remain separate validation steps.

## Baseline receipt and implementation

The coordinator's Unity run on the unchanged production source completed at 16:35:19–16:35:21 UTC: **13 total, 2 passed, 11 failed**, duration 1.962 s, exit 2. Evidence: `../ct-life-evidence/audit-20261019/vehicle-controls-lifecycle-negative.xml` and its log. The failures covered the expected retained input/presentation, stale pointer release and hidden Roadstar handbrake; stationary simultaneous controls and click-through overlay preservation passed.

The repair adds per-pedal pointer ownership, clears identities before releasing capture, restores stick/pedal presentation, and subscribes to the existing focus/pause publishers with disposal cleanup. Attached-panel visibility, blocking overlays and text-field focus cancel holds; new gestures are rejected while blocked. A fresh gesture is required afterward. Roadstar flight releases only its hidden handbrake. HUD output is neutralized only when the HUD last wrote it and neither a busy vehicle transition nor either scripted-input flag currently owns commands. The regression also covers the exact exit ordering: parked braking is set, Driving becomes null and its binding signal is published while Busy remains true.

The separate compile harness failed before reaching the new tests: its generated Universal Runtime stub pass reported failure with no listed errors, then dependent World files reported five missing `UnityEngine.Rendering.Universal` namespace errors. This is not recorded as a successful compile; native Unity validation is authoritative for this change.

The coordinator's first full native run returned exit 2: **2,067 total, 2,063 passed, 4 failed**, including all **13 new lifecycle cases passing**. Evidence: `../ct-life-evidence/audit-20261019/editmode-controls-lifecycle-full.xml` and its log. The old `HudMapTests.Hud_HasTheMinimapAsItsMapRoute` and three `HudMicLobbyTests` failed in setup because unconditional `Runner.Instance` initialization attempted `DontDestroyOnLoad` in EditMode. The follow-up initializes the persistent host only when `Application.isPlaying`; event subscription remains available to the existing publisher and tests.

Independent review also identified the scripted capture contract: blocking human input must not freeze speed/altitude presentation. The follow-up continues readouts while suppressing input writes, with an added assertion that scripted, unfocused controls show a changed speed and preserve the script's lift command. No legacy test behavior was weakened.

## Final native receipt and remaining limits

The final coordinator-owned Unity run completed at **16:43:55–16:44:34 UTC**, duration **39.172 s**, exit **0**: **2,067 passed, 0 failed, 0 skipped**, including all **13 VehicleControlsLifecycleTests**. This reviewer read the resulting XML. Evidence: `../ct-life-evidence/audit-20261019/editmode-controls-lifecycle-final.xml` and its log. `controls-lifecycle-source-freeze.json` records hashes for the production file, test file and test metadata. Independent read-only review found no remaining concrete source or fixture concern after checking pointer ownership, simultaneous stationary holds, modal semantics, runtime-only Runner setup, busy/scripted command ownership, hidden Roadstar handbrake and telemetry.

The incident recording used the earlier controls APK (`0ffb83…`, the 2,034-test checkpoint). The searched project-owned shipping source contained explicit, launch-argument-gated vehicle/venue demos but no synthetic pointer-event injection call; `qaUi` only wrote telemetry. No demo/waypoint/tour/probe marker appeared in the available PID30452 log. Those negative searches do not exclude native input paths, OS behavior, external injection, or an earlier missing release. The log's three world-tap entries identify calls into the game tap path, not their hardware or process origin. The full driving/boarding sequence remains unattributed.

Native tests reproduce and validate lifecycle cleanup; they do not reconstruct the incident. Device background/resume, opening a modal during a held control, long stationary multitouch and a fresh no-input observation still need their own synchronized evidence on the new APK. The short simultaneous steer/gas trial below is narrower than those cases. No complete demo-route pass is claimed.

## Scoped Seeker validation on the integrated APK

`controls-lifecycle-build.json` records successful installation of `ctlife-controls-lifecycle-dev.apk`, SHA256 `4ef24fa133acfe9626c2f3cac01596fd624069091f3ed65f983cbb2f3854d07d`, verified at **16:54:21 UTC**. This is the integrated 2,067-test checkpoint, built from source commit `31e48cee` in 129 s with 0 errors and 37 warnings. It includes the entry-authority repair.

Independent host review of `ctlife-entry-controls-return-20261019.mp4` (600.584 s) and `ctlife-entry-controls-exit-20261019.mp4` (180.504 s), alongside their `entry-controls-*-20261019-commands.jsonl` and start receipts, confirms:

- Reentry reaches the normal driving HUD/Get out state (return clip 54.5 s). A short simultaneous steering/gas gesture visibly moves and turns the SUV; the following brake gesture ends at 0 km/h. This is a crowded parking/street segment, not a sustained driving-route test.
- The map opens while the car is already stopped with released controls. The exit clip starts in that map; closing it restores the vehicle HUD. A **fresh** gas gesture then moves the car, and fresh braking returns the speed to 0. The map was **not** opened during a held pedal/stick, so this does not substitute for the native cancellation regression.
- The ordinary Get out command around **17:21:31 UTC** returns to the on-foot HUD beside the SUV (exit clip 13.2 s). A later forward gesture visibly moves the avatar away from the car (20.5 s), followed by a released-stick stop.

Reviewed intervals were return 45–60 s, 298–316 s and 404–412 s, and exit 0–28 s, using 2 fps contact sheets plus exact full-resolution seeks at the transitions. No full-video/no-input claim is made from the unreviewed idle periods. Host command and device frame timestamps are separate clocks; these observations do not measure packet-to-render latency. The earlier unexplained activity remains unattributed.
