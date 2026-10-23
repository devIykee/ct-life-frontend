# Input observation — 19 October 2026

**Input origin remains unverified. The user has now confirmed that no one was using the device during the newly identified interval.** The continuous second recording shows ordinary car and helicopter interactions between the endpoint screenshots. It does not show an instantaneous lodge-to-coast relocation. This receipt does not attribute those interactions to the user, another person, a controller, automation or game software.

## Two separate observations

1. **Earlier summit-to-road event, approximately 13:19–13:21 UTC:** the transition itself was not recorded. `ctlife-flight-controls-summit.mp4` ends at device UTC 13:17:40.443; `ctlife-flight-controls-landing-search.mp4` begins at 13:21:41.769 already on foot. The 13:19:15 helicopter screenshot lies in that gap. The cause and intermediate actions remain unresolved.
2. **Later lodge-to-coast event, approximately 14:50–14:52 UTC:** `ctlife-roadstar-continuous.mp4` covers the intermediate actions. The coordinator had not issued the boarding/flight sequence below, and the user said they were not using the device. The recording establishes the visible sequence, not its input source.

## Continuous recording evidence

The host reviewed the complete recording with coarse contact sheets, then the relevant interval at half-second sampling and selected full-resolution exact seeks. The file parses as 2670×1200, duration 688.698544 s, 469,844,595 bytes. Times in this table are **relative video seconds**; the container creation timestamp is not used as the start time. Half-second contact sheets only locate transitions; exact-seek frames take precedence.

| Video time | Visible state |
| --- | --- |
| Immediately before about 476.5 s | Player idle on the lodge helipad, partly obscured by the helicopter. |
| About 477–480 s | Movement stick activates; player runs toward a peach compact car; a Ride offer appears. |
| 480.5 s / 481.5 s exact | Player at the open car door, then seated inside with the door open. |
| About 482–497 s | Car driving HUD, steering and repeated pink Brake states. The compact moves through nearby grass and around the pad. |
| About 498–503 s | Player exits the compact, moves back to the helicopter, and approaches its Fly offer. |
| 505.5 s exact / after 505.5 s | Close entry camera still shows the actor; helicopter driving HUD follows at about 506 s. The craft takes off by about 507 s. |
| About 508–573 s | Continuous flight from mountain toward city/coast, with changing joystick directions and multiple pink Down press/release states. Exact 531.5 s shows 82 km/h and 170 m. |
| 573 s exact | Helicopter beside the coastal road, 14 km/h, 0 m; Down is pink. |
| 575.5 s exact | Helicopter stopped, 0 km/h, 0 m, Get out; Down released. |

No map, menu or fast-travel sequence appears in the reviewed 477–580 s interval. The compact in this sequence is **not the Roadstar**; this footage does not complete the Roadstar trial.

Correction: the earlier version of this receipt overstated what pink Brake/Down styling establishes. `VehicleControls.Pedal` adds `veh-down` on `PointerDownEvent` and removes it on pointer release/cancel/capture loss (`VehicleControls.cs:102–106`). However, `Bind` calls `ReleaseAll`, which clears the logical axes/booleans without clearing pressed classes or releasing captures (`73–76`, `171–176`). Pink styling can therefore survive a vehicle rebind while its logical pedal input is already zero. Color alone does not timestamp a live pointer event or identify its source. The changing controls and vehicle positions remain visible observations; they do not independently prove the origin of the input. Green Up alone does not establish a press because green is its normal styling. `showcaseFleet` spawns the fleet; the separate `vehicleDemo` startup path requires that explicit argument, which was absent from the coordinator's fresh launch command.

The private log contains game tap records at local 15:50:46.615, 15:51:08.765 and 15:51:51.960. Review found no matching `InputReader`, `InputDispatcher` or `injectMotionEvent` record between local 15:50:40 and the coordinator's next recorded injection at 15:52:34.603. Absence of those log entries does not prove absence of input or identify its source. Raw logs and credential-bearing launch commands remain private.

## Later idle observation

The coordinator subsequently observed the device for approximately 60 s, **15:01:09–15:02:11 UTC**, using `getevent -lt /dev/input/event2`. The resulting private `idle-physical-input-20261019.log` is 0 bytes; the host reviewer verified that file size. The coordinator's concurrent `lsof` check of port 5037 showed only the coordinator's recording/getevent connections and the adb server.

This later idle window does **not** identify the origin of the earlier 14:50–14:52 events. It neither reconstructs the missing first transition nor rules out intermittent or unobserved input paths. The coordinator initially paused touch testing for clarification; the user subsequently answered, “No one was using it.” Fresh isolated recording and input-origin investigation are separate from this earlier idle sample. No gameplay change was made for this receipt.

## Retained evidence

Selected exact-seek frames, a sanitized host receipt and a SHA-256 manifest are outside the repository at `../ct-life-evidence/audit-20261019/input-observation/`. The original recording remains at `../ct-life-evidence/audit-20261019/ctlife-roadstar-continuous.mp4`. No raw private log was copied into the repository or public evidence directory.

The separate [flight follow-up](audit-flight-followup-20261019.md) records scoped local/shoulder helicopter exits and cliff fall/get-up observations. Those successful trials do not explain either input observation or complete the unfinished Roadstar route.


## Read-only host automation review

The task-local host review found no route replay or recurring input scheduler in `AuditTouch.java`: each invocation accepts one 1–20,000 ms one/two-finger gesture, normally sends UP and attempts CANCEL in finally if still active. `followup-device.py` only reads screenshots/UI state, and the reverse watchdog only refreshes port mappings. This does not establish a hard process-duration limit or terminal-event delivery after an interrupted injection: the helper checks elapsed duration between inject calls and has no separate timeout around each call.

Two older October 8 scripts can wait for SIWS and automatically relaunch integration/venue checks; their presence is a real automation capability, but no execution evidence links them to either October 19 incident. The source reviewer mapped that integration path to session/venue checks, with no vehicle route or touch injection. The current task-associated process snapshot found no matching AuditTouch, followup, watchdog, legacy device-pass, scrcpy or app_process command. A snapshot cannot exclude short-lived or differently launched commands, and no complete command start/end ledger for 13:19–13:21 or 14:50–14:52 was found among the reviewed task files.

The cached qa-ui timestamp 14:52:39.5795800Z and host file mtime 14:52:40.302401Z support the later incident's UTC alignment. The subsequent 15:01 idle sample remains limited: AOSP describes getevent as kernel-event output, not complete provenance for framework/application injection ([official documentation](https://source.android.com/docs/core/interaction/input/getevent)). Source hashes, exact scope, process patterns and limits are retained outside the repository in `host-input-automation-audit.json` and `.md`. No adb, Unity, process interruption, credential copying or unrelated project-content inspection occurred in this review. Host attribution remains unresolved.

## Fresh controlled observation, 16:25–16:30 UTC

The new recording did **not reproduce the earlier vehicle route**. Metadata records host start at **16:25:35.420779 UTC** and end at **16:30:36.846455 UTC**; the video lasts **300.683156 s** at 2670×1200. Four deliberately issued gestures have start/end records, all with exit code 0. The final gesture ran **16:27:21.748871–16:27:24.596718 UTC**, moving the on-foot player beside the helipad. No further gameplay input was issued by the coordinator during the remaining approximately three minutes.

Independent visual review used a 15-second full-scene overview, a 3-second release-window sequence at video 100–160 s, 1-second avatar/context samples throughout 110–300 s, 1-second full-scene samples over 270–300 s, a quarter-second pose example at 125–130 s, and full-resolution 120 s/final frames. The three separate idle screenshots were also checked. The player repeatedly assumes a brief wide-legged pose, sometimes changing arm position, and returns to the same upright location. The helipad markings, camera composition, road edges and minimap alignment remain fixed in these samples. There is no visible net traversal, vehicle entry or minimap disappearance. The minimap is present in the final video frame and all three idle screenshots. NPC/traffic movement and yellow/green sky flashes are separate visible events.

This is a sampled visual result, not inspection of every one of the 8,962 frames or a measurement of the player's exact transform. No Animator-state trace identifies the repeated pose's clip or trigger. The current source contains presentation-only idle variations, but that alone does not establish which state played. Video-relative time is not frame-perfect synchronized to the host timestamps.

All **252 focus samples**, from **16:25:35.912616–16:30:35.589735 UTC**, report CTLife in focus. The kernel capture is **308 bytes**, containing five device inventory entries and **no event rows**. AOSP defines `getevent` as a live stream of kernel input events; it is not a complete record of framework/application-injected input ([official getevent documentation](https://source.android.com/docs/core/interaction/input/getevent)). The coordinator's deliberate injected gestures are therefore not contradicted by this empty kernel-event stream.

The retained accessibility snapshot has empty **Bound services**, **Enabled services** and **Binding services** sets. The input snapshot lists internal touch/key/headset devices and the virtual keyboard/DPAD, with no external game controller; all listed `IsExternal` fields are false. These are current inventories, not historical proof about the earlier incident or a complete inventory of every possible software input path.

The host connection samples retain the recording/monitoring clients and adb server. The additional sampled client at **16:30:00.450–16:30:01.625 UTC** coincides with the coordinator's confirmed `input-origin-idle-middle.png` screenshot command, whose tool receipt ends at **16:30:01.888 UTC**. Socket snapshots alone do not reveal command intent or exclude shorter-lived clients between samples.

Private evidence remains under `~/.ctlife-local/input-origin-fresh-20261019/`: `recording.mp4`, the start/end and command records, kernel/focus captures, `independent-review.md`/`.json`, and review frames. Video SHA-256 is `3e63e8b702a45843b52e8a1dd806991d83ae8107c4e9775395c636b547a65c4d`. Accessibility/input inventories remain private alongside that directory. No raw private app log was copied into the repository. **The earlier unexplained interactions remain unresolved; this fresh negative observation does not identify their cause.**
