# Flight followup — 19 October 2026

> Chronological lane receipt. For the latest installed build, scoped device outcomes and final cleanup, see [the current handoff](../handoff/CONTINUE-HERE-20261019.md). Earlier test counts and pending statements below describe their own checkpoints.

This follows the Seeker observation that backward/sideways Simple flight curved while the view swung around. No layout, typography, color, icon or other visual token was changed. Existing HUD and Duo design standards remain the reference.

## Research and reproduction

- Installed editor is Unity 6000.3.25f1. Opened official [Quaternion.Euler](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Quaternion.Euler.html) and [UI Toolkit coordinate systems](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-coordinate-and-position-system.html). UI layout uses panel points and a top-left origin; these are not the camera's screen-pixel/up-positive gesture convention.
- `VehicleControls.Compose` transforms the Simple flight stick by `CameraRig.yaw`. `FlightPhysics` turns the aircraft toward that resulting world movement. `CameraRig.ChaseVehicle` then turned the camera toward the aircraft after the idle delay. Together these changed a held backward/sideways command without a new camera gesture.
- Native regression calls the actual chase-camera method for helicopter and drone while the aircraft rotates toward a held backward/sideways command. All four cases failed before the repair; vehicle-relative camera controls passed. Evidence: `../ct-life-evidence/audit-20261019/flight-showcase-before.xml` (combined showcase/camera run, 15 passed/6 failed; 4 camera reproductions and 2 separately corrected showcase fixture errors).
- The flight right-hand drag previously sent unscaled panel delta directly to `Orbit`; on-foot input inverted Y and scaled to screen pixels. The two paths therefore disagreed in direction and gain.

## Repair and verification scope

Simple helicopter/drone cameras now follow position while retaining the player's yaw and pitch. They no longer recenter toward the nose. Pro flight, Roadstar and ordinary driving retain their vehicle-relative recentering. A shared input conversion applies the same panel-to-screen scale and vertical sign to flight and on-foot orbit; the flight path uses full panel width, including on devices with a safe-area inset.

Regressions cover four held-direction cases over 180 camera steps, retention of manual yaw/pitch after the idle delay, four vehicle-relative controls, and equivalent physical drags at two panel scales. Native rerun passed all 22 combined flight/showcase tests (9 flight-camera and 13 placement), zero failures/skips: `flight-showcase-after.xml`. At that checkpoint Seeker acceptance remained pending. Those results are native regression evidence; the later device observations below have a narrower, separately stated scope.

## Integrated checks

Full native Unity EditMode initially passed **2018/2018**, zero failed/skipped (`editmode-followup.xml`); the subsequent graphics-enabled run including the plaza repair passed **2023/2023**, zero failed/skipped (`editmode-plaza-graphics.xml`). Release compile harness passed with three existing unused-field warnings. The followup APK built and installed successfully. Settings Large/XXL device checks passed; the repaired flight controls were still pending at that checkpoint because the Seeker disconnected. Subsequent checks on the installed controls build are recorded below. See [device followup](audit-device-followup-20261019.md). Settings help describes safe-ground recovery without promising the last saved point online; temporarily unavailable recovery shows localized feedback through the existing toast. Shared Duo components and styles were preserved.


## Recorded device follow-up on the final controls build

The integrated controls build passed **2034/2034 native EditMode tests** before installation. Source remained frozen during this host-only video review. The following observations do not mark the full audit route, the twice-run route, Roadstar fast streaming, or arbitrary cliff/slope recovery complete.

The MP4s parsed and decoded successfully. Time bounds below come from Android screenrecord's embedded Winscope v2 frame timestamps, using the layout in [AOSP screenrecord.cpp](https://android.googlesource.com/platform/frameworks/av/+/572778c8ee62a602faaf69554dc1acd28f5902d2/cmds/screenrecord/screenrecord.cpp). They use the device clock; host input-command timestamps can differ slightly. The container creation tag is not used as the recording start. Review used contact sheets across the recordings, denser transition samples and full-resolution frames; derived images remain in the session scratch directory.

| Recording | First–last frame, device UTC | Reviewed evidence |
| --- | --- | --- |
| `ctlife-heli-final-shoulder-attempt.mp4` | 14:23:59.017–14:26:59.005 | Entire 180.022 s sampled; exit interval reviewed at 5 fps |
| `ctlife-heli-repeat-1.mp4` | 13:41:00.193–13:44:00.170 | Entire 180.011 s sampled |
| `ctlife-heli-repeat-2.mp4` | 13:44:05.126–13:47:05.113 | First 20 s reviewed at 2 fps for the subsequent local exit |
| `ctlife-controls-drone-low.mp4` | 13:34:48.861–13:37:48.876 | 180.049 s; detailed return review below |
| `ctlife-route-continuous.mp4` | 14:29:59.122–14:42:11.443 | 732.354 s; cliff interval sampled at 5 fps from 552–568 s, exact surrounding frames and later movement reviewed |

### Helicopter shoulder landing and normal exit

The final shoulder clip shows the helicopter descending beside the snowy summit. At video 33.0 s (device UTC **14:24:32.017**) a full-resolution frame clearly reads **Get out**, **0 km/h**, and **0 m**. The craft remains stationary in the following sampled frames. This is the snowy shoulder below/right of the visible cairn; it is not a touchdown on the cairn platform. The HUD is direct evidence that the game's landing/exit predicate accepted this spot, rather than an independent measurement of all skid contacts.

Around video 104.6 s, the seated view changes to the player beside the helicopter with the normal on-foot controls and a **Fly** offer. This corroborates the coordinator's ordinary Get out action logged at host UTC **14:25:43.912**. Dense samples show the exit presentation and then an upright avatar on the shoulder; no recovery toast, phone menu or map appears during that transition. The avatar remains on the shoulder through the recording's end. This clip establishes the normal exit and restored on-foot UI; it does not contain a sustained post-exit walk or a return journey.

### Local low-altitude repeat

`ctlife-heli-repeat-1.mp4` shows boarding, takeoff to a low hover, descent and the **Get out / 0 m** state again near the road. It ends while still seated. The subsequent exit is in `ctlife-heli-repeat-2.mp4`, not the first file: around 9 s the view changes to on-foot controls and a **Fly** offer; around 11.5 s visible thumb input moves the player beside the craft. The rotor then heavily occludes the camera, matching `heli-repeat-normal-exit-walk.png`. The combined two clips corroborate this local takeoff/landing/normal-exit sequence and brief returned movement; the obstructed view limits character-contact inspection.

### Drone low descent and return

`ctlife-controls-drone-low.mp4` contains more than a camera-return-only result. Explicitly sought frames show the drone at **3 m / 0 km/h** at 145.75 s, then descending at **1 m / 10 km/h** at 146.5 s. At 147.2 s it is visibly at road level and stopped, with **0 m / 0 km/h** and **Rotor 20%**. By 147.25 s the camera and on-foot HUD have returned to the operator beside the helicopter (device UTC approximately 13:37:16.11). An independent second reviewer inspected the same descent and return.

[Inference] The visible descent, road-level stop and rotor spin-down support a touchdown before pocketing. The recording does not expose actual collision contact or the `Grounded` flag. It also has no unambiguous Land-press onset, so it cannot establish whether `Grounded` or the seven-second deadline ended `LandDrone`'s descent loop. Record this as observed low descent/stop/return with that branch unverified; do not use the camera return alone as proof of a physical landing.

### Cliff fall, get-up and returned movement

Two reviewers independently inspected the cliff interval in `ctlife-route-continuous.mp4`. At video 548 s the avatar is airborne above the edge. At 553.5–554 s it is nearly horizontal/sideways on the lower face; from 555–556.5 s it bends/kneels and rises, reaching upright at 557 s. A further downward/off-balance phase appears around 559–564 s, followed by another rise to upright by 567 s (device UTC approximately **14:39:26.122**). This is visibly more than continuous upright sliding. At 680 s the recording shows Sprint input and an ordinary running pose; after release, subsequent samples show upright idle on the same lower mountain face.

[Inference] The downed and rising poses are consistent with the existing fall/knockdown/get-up machinery. `LocalPlayerController.OnLanded` calls `Knockdown.Fall` above the seven-metre fall threshold; a moving hard landing above 2.5 m can instead play `react_roll`. `Knockdown.Fall` can choose either `RagdollRig` or an authored fallback before its get-up phase. The recording exposes neither the measured fall height nor the chosen branch, so physical ragdoll, the specific roll animation and exact state transitions remain unverified.

No safety-recovery veil, recovery toast or distant relocation is visible in the inspected cliff sequence. The available private log has no transition diagnostics that resolve the branch; those source paths do not emit such diagnostics. Record **visible fall/downed reaction, get-up and later resumed movement**. Do not mark automatic safe-ground cliff rescue, all steep slopes, or the full audit route complete from this recording. No source change was made for this observation.

### Remaining visual and coverage limits

Some decoded flight frames contain intermittent rectangular green/yellow areas in the sky; dense shoulder-exit samples also show occasional black patches around the ground marker. Their origin has not been established, so this review does not attribute them to gameplay, rendering, recording or decoding. They remain a visual-evidence caveat. No source change was made for them.

The earlier unexplained summit-to-road transition remains unresolved. `ctlife-flight-controls-summit.mp4` ends at device UTC 13:17:40.443, and `ctlife-flight-controls-landing-search.mp4` begins at 13:21:41.769 already on foot. Neither covers the gap after the 13:19:15 helicopter screenshot. The later successful local and shoulder exits do not retroactively explain that earlier event.
