# CT Life continuation — 19 October 2026

[Inference] **Ready to record the scoped offline gameplay demo.** The latest Seeker pass repaired offline Wave and decorative-car collision, then recorded walking, emotes, car driving, takeoff, flight, landing, normal exit, walkaway and Duo Settings on the new APK. No blocking failure appeared in independently reviewed action windows; no known neon sky band was detected in the final recording. See the [demo confirmation receipt](../progress/audit-demo-confirm-20261019.md) for exact scope, evidence and limits. The app is stopped, original settings restored byte-for-byte, and device ownership released.

Claude's full history and final documents were already reconciled. Earlier driving, flight and route observations remain recorded below with their original builds. The earlier unexplained input sequence is still unattributed; these rendering/camera repairs do not explain it.

## Current source and artifacts

- Frontend: `/Users/mac/Documents/codes/projects/ct-life-repair`, branch `feat/audit-repair-20261019`, original base `25aad110`. Original dirty frontend/backend worktrees were preserved.
- Local implementation checkpoints: `92ee6ada` published realtime contract; `48173cda` integrated island collision/controls/recovery; `065bfb59` byte-identical pre-existing Android likeness plugin metadata; `8542f0a3` vehicle entry authority and bounded online exits; `31e48cee` vehicle control cancellation across input interruptions. No remote push, deployment, store submission, real-wallet payment or message to Iyke occurred.
- Unity6000.3.25f1. **Current pass:**12/12 native IslandSafetyTests passed, successful Android build, and recorded local gameplay confirmation. The previous rendering repair separately passed12/12 on-foot camera tests. No current full-suite rerun. **Prior lifecycle pass** graphics-enabled native EditMode: **2067/2067 passed, zero failed/skipped**, including13 control-lifecycle regressions and the earlier20 entry-authority cases (`editmode-controls-lifecycle-final.xml`). Earlier intentional negative tests and fixture failures remain in the [lifecycle](../progress/audit-vehicle-controls-lifecycle-20261019.md) and [entry-authority](../progress/audit-entry-authority-20261019.md) receipts. Earlier placement replay tests and entry-source external compile checks passed. The latest external harness attempt failed in its generated URP stubs; it is not a successful compile receipt for the lifecycle change. Native Unity validation and Android packaging passed for that change. No iOS build was attempted for this Unity/Seeker task.
- **Latest installed and hash-verified:** `demo-confirm/ctlife-demo-confirm-dev.apk`,263.6MiB,168s,0errors/37warnings; SHA256 `f25ac2abc9273d682319d4588a5d37f8f60b72feb5d8990cada1c8fb24e66383`. Installed at20:22:27UTC; source hashes match `demo-confirm/source-freeze.json`. Normal launch selectedOpenGLES3. The recorded local gameplay sequence completed, including Roadstar landing/normal exit and offline Wave. Receipts:`demo-confirm/installed-build.json`,`action-review.md`,`sky-review.md`,`cleanup.json`.
- **Previous rendering artifact:** `render-repair/ctlife-render-camera-dev.apk`, SHA256 `1e40a8a3c02201c0065a6b3e7437f67397711497893dc17b9daa556594a39469`, source95892a76. Its camera/shader/device-filter repairs are included in the latest APK.
- **Previous lifecycle artifact:** `ctlife-controls-lifecycle-dev.apk`, SHA256 `4ef24fa133acfe9626c2f3cac01596fd624069091f3ed65f983cbb2f3854d07d`, source31e48cee. The approach interruption/reentry and Roadstar observations below belong to that earlier artifact.
- **Prior entry-authority artifact, never installed:** `ctlife-entry-authority-dev.apk`,263.6MiB,133s,0errors/38warnings; SHA256 `e7102bd72e5ff591b015d66f942aed227d801131c0ad328d7a7d41172a801d2b`. Receipt:`entry-authority-build.json`.
- **Earlier installed controls build:** `ctlife-controls-final-dev.apk`, SHA256 `0ffb83f3f87d9a05e246399385961d15cc7277b8bbb7181f8ed0f9f76f710a15`, native2034/2034. The historical device section below belongs to this build unless a receipt explicitly identifies an older APK; those observations are not device passes for the latest artifact.
- Only root/coordinator operates Unity, adb, installation and device input. Root released the device lock at20:53:57UTC after restoring the fresh pre-confirmation backup. Reacquire ownership before a future session; do not control the device concurrently. Do not use scripted `vehicleDemo`/`islandTour` routes as manual acceptance.
- Reconciled documents, proposals and evidence summaries are saved locally at `17cdc852`. A subsequent [QA-tool review](../progress/audit-qa-tools-20261019.md) corrected failed-forwarding launch and observer coverage/startup cancellation, with13/13 host mock checks passing. Those tool changes do not alter the installed APK or add live device acceptance.

## Earlier lifecycle-build device outcomes (4ef24fa…)

- **Entry interruption:** a real two-second proxy cut after the SUV claim at17:06:19UTC left the avatar on foot beside the closed door, and the Drive offer returned. The server released the claim about167ms after the cut; street presence returned about3.581s later. The parked pose stayed unchanged until reentry. The visible sequence supports approach-phase cancellation, not an open-door or seated-phase device pass.
- **Reentry, driving and exit:** the same SUV was entered normally afterward. Short steering/gas/braking, map open/close, fresh gas after closing the map, normal Get out and walking away were observed. The independent observer recorded no errors/rejections. Map opened with controls already released; held-input modal cancellation and device focus/pause cancellation remain untested. Initial exit-grant distance was not measured. See [entry authority](../progress/audit-entry-authority-20261019.md).
- **Roadstar:** an explicit offline showcase trial showed boarding, short ground acceleration/braking, flight into Builder Hills, descent to grass beside buildings, reverse movement onto pavement, normal exit and walking. The transit frame at439.5s displays156km/h and78m. The two recordings have a103s gap; this is not uninterrupted whole-route proof. Final map calibration estimates the post-walk player nearX376/Z238, not the exact touchdown. Precise clear-road landing, distinct ground steering, Pro mode and the complete repeated route remain open. See [Roadstar receipt](../progress/audit-roadstar-device-20261019.md).

## Historical device outcomes on the earlier controls build

- **Plaza:** full shoes/lower legs at arrival, walking, entrance/exterior, Unstick and online arrival. The island paving overlay was26.06cm above the venue floor; the targeted repair retains venue/furniture heights. Native actual-arrival gap1.97mm; original vertex streams and360 unrelated triangles unchanged; repair idempotent.
- **On-foot camera:** held backward movement for5s moved about19.5m south with heading retained until explicit orbit. An obstacle later contracts the camera into a close-up; that clip does not establish full-body run-stop quality throughout. Native tests cover direct input/braking, orbit, paths and seat settling.
- **Duo Settings:** Large and XXL labels/help inspected against the approved shared design. Keep the established components, typography and tokens.
- **Venue recovery:** a foreground2s socket cut returned a fresh Timeline venue session in~3.53s and street presence in~3.73s, followed by observed ordinary walking. Earlier background-shade/adb trials are separate evidence.
- **Driving disconnects:** actual2s and45s cuts released the sports-car claim after193ms/269ms; street presence returned4.212s/46.486s later. The same car was claimed again after both. The long trial measured3.533m/s immediately before cut; parked X/Z stayed unchanged during outage. Observer errors/rejections:0. This is lease loss/fresh joining, not retained-driver resume. Post-final-reclaim driving motion was not measured. The uncontrolled12:50:40UTC transport loss remains unattributed.
- **Helicopter:** exterior lodge placement, Simple controls, steep-slope refusal, local takeoff/landing, normal exit and walking confirmed across the appropriate clips. Near-summit snowy shoulder Get out/0km/h/0m and normal exit at14:25:43.912UTC independently reviewed. This is a narrow shoulder below/right of the cairn, not a cairn landing or whole-route pass.
- **Drone:** visible descent to road-level0m/0km/h and rotor spin-down, then operator/on-foot return. Internal Grounded versus7s-timeout branch remains untraced.
- **Cliff:** continuous video shows airborne departure, sideways/downed reaction, get-up and later ordinary running. It does not distinguish physics-ragdoll from authored knockdown or establish automatic safe-ground rescue.

## Input activity and rendering observations

The first helicopter→on-foot change around13:19–13:21UTC has an unrecorded transition; later video starts on foot beside the helicopter on a road. The user said they were not controlling the phone then. Narrow process/transcript checks did not establish another controller.

The second14:50–14:52UTC sequence is continuously recorded: compact-car entry/drive/exit, walking to the helicopter, boarding, flight and coastal landing, with changing joystick and pink Brake/Down states while root commands were idle. This establishes the visible control/vehicle sequence. Pressed styling can persist after rebinding, so color alone does not timestamp a live pointer event. It does not establish an instantaneous teleport or a confirmed spontaneous game-state defect. The input source remains unverified. A later60s raw touchscreen observation showed no events and cannot explain earlier activity. See [input receipt](../progress/audit-input-observation-20261019.md). Do not attribute either event to a person/process without evidence.

Intermittent colored sky bands and repeated Vulkan vertex-data warnings are documented in [the official-source rendering investigation](../research/audit-vulkan-rendering-20261019.md). The earlier lifecycle-build Roadstar trial also captured the sky fault in a direct adb screenshot, so it is not confined to the recorded video. This does not isolate Unity, the GPU/driver or the compositor. The subsequent [repair pass](../progress/audit-render-camera-repair-20261019.md) guards a separate below-horizon arithmetic singularity and installs an exact Seeker GLES3 workaround, retaining native Unity exclusions. Normal API selection was verified. The later final demo recording had no detected recurrence in40346 screened frames and60 sampled flight-window views; this supports the scoped demo recommendation but does not establish permanent resolution or root cause. No LOD/art change or cold-device60fps claim was made.

## Working priority after the user's correction

1. Proceed to the scoped gameplay demo recording. The focused confirmation completed; do not expand it into a new broad audit. Read the [confirmation receipt](../progress/audit-demo-confirm-20261019.md) for the actual observed sequence.
2. Keep the established design system and backend ownership. The offline showcase does not establish live multiplayer, financial flows, premium entitlement or completion of every shot in the video bible.
3. The known sky band did not recur in the reviewed recordings. Investigate any future recurrence from its actual context; no speculative LOD, geometry-budget or design changes.
4. Settle landscape before starting the final screenrecord. The confirmation source is letterboxed because capture began during portrait startup. Hold Brake only as needed: continued ground hold engages reverse.

## Latest confirmation-pass cleanup

At20:53:57UTC the three fresh pre-pass preference/key/profile files were restored byte-for-byte from private `demo-confirm-20261019/original-private-files.tar`. The newf25ac2a… APK remains installed, the app and recorders are stopped, reverse mappings are4010/2567/7880/7881, and the owned device lock is released. Four retained host recordings matched phone SHA256 before removing only their duplicate phone copies. Temporary touch helper removed; no new backend services were started. Raw captures/logs and preference backups stay private; reviewed stills and receipts are in the external `demo-confirm/` evidence folder.

## Previous rendering-pass cleanup (historical)

At19:20:06UTC the three pre-repair preference/key/profile files were restored byte-for-byte; the1e40a8a… APK was left installed. That state was superseded by the later confirmation pass and cleanup above.

## Earlier cleanup at18:12UTC (historical)

At18:12:33UTC the app was stopped and the three original preference/key/profile files were restored from private `device-backup-input-origin-20261019.tar`; all byte comparisons matched. The pre-restoration test fixture is privately backed up. Never print these files. The latest `4ef24fa…` APK remains installed; no uninstall or data clear occurred.

All recorders are stopped and the temporary device touch helper is removed. Seven new task recording files were SHA256-matched to retained host copies before deleting only their phone duplicates. Six are valid recordings; the first aborted observation lacks an MP4 moov atom and is retained privately as an invalid probe, not acceptance evidence. Earlier unrelated recordings and QA XML remain untouched. Reverse mappings are4010→4010,2567→2567,7880→7880,7881→7881. The owned device lock was released at18:17:06UTC.

The direct observer completed at17:26:14UTC. Owned backend22726 and proxy26668 were stopped after command verification at17:28:38UTC; ports4012/2569/2570/2571 were closed. Original API4010 PID72652 and realtime2567 PID72656 were preserved. Receipts: `input-roadstar-device-restoration.json`, `input-roadstar-recordings-final.json`, `entry-authority-services-cleanup.json`. No remote push, deployment, store submission, real-wallet payment or external message occurred.

## Earlier completed followup cleanup (historical)

At16:11:08UTC the coordinator rechecked all three original followup files byte-for-byte against private `device-backup-followup-20261019.tar`: PlayerPrefs XML, automation key and profile marker. Never print their contents. The reconnected snapshot and pre-final-restore snapshot are also preserved privately.

- At that cleanup, the app was stopped and the installed controls APK hash matched `0ffb83f3f87d9a05e246399385961d15cc7277b8bbb7181f8ed0f9f76f710a15`. No uninstall/data clear.
- All28 task recordings have verified host SHA256 matches and valid ffprobe metadata; only their duplicate phone copies were removed. Earlier unrelated origin videos and QA XML were preserved. Notification/idle-observation material remains private until reviewed.
- Temporary touch dex removed; all recorders stopped; owned Seeker lock released.
- Reverse mappings restored:4010→4010,2567→2567,7880→7880,7881→7881.
- Owned proxy64866 and isolated backend63565 stopped after command verification. Ports4012/2569/2570/2571 are empty. Original API4010 PID72652 and realtime2567 PID72656 remain running. Direct observers were already stopped.

Receipts:`followup-final-cleanup.json`,`followup-services-cleanup-final.json`,`followup-recordings-verified.json`,`followup-recordings-removed.json`. Earlier cleanup paragraphs in chronological progress logs are historical; these receipts describe the completed16:11UTC cleanup, before root resumed testing with a fresh backup.

The resumed online trial subsequently used the isolated backend/proxy (API4012/realtime2569,proxy2570/control2571,audit database,Redis8) and a direct observer. Those services are now stopped as recorded above. Roadstar used an offline showcase fixture; it does not establish premium backend entitlement.

## Backend scope and reading

Isolated backend checkout remains at `7d2f069a3f432347dee7b9fa33d5431676dfb3c0`; Iyke's round-2 street/vehicle changes are present. Frontend proposals contain283 public parking bays and reviewed coverage changes; they were not seeded or deployed.36bays exceed backend polygon-plus30m tolerance;38exceed frontend exact polygons. ORE's door remains outside current coverage. Ownership/rentals, inventory enforcement, weekly-debt remainder, outgoing hit reports, street chat/voice and registered-bay expiry/visibility limits remain documented gaps.

Read [coordinator](../progress/audit-coordinator-20261019.md), [device](../progress/audit-device-followup-20261019.md), [entry authority](../progress/audit-entry-authority-20261019.md), [control lifecycle](../progress/audit-vehicle-controls-lifecycle-20261019.md), [Roadstar](../progress/audit-roadstar-device-20261019.md), [backend](../progress/backend-followup-20261019.md), [plaza](../progress/audit-plaza-grounding-20261019.md), [flight](../progress/audit-flight-followup-20261019.md), [showcase](../progress/audit-showcase-followup-20261019.md), [avatar](../progress/audit-avatar-followup-20261019.md), [Settings](../progress/audit-settings-device-20261019.md) and [Claude reconciliation](CLAUDE-RECONCILIATION-20261019.md), then the revised deep audit/video bible.

Evidence remains outside the repo at `/Users/mac/Documents/codes/projects/ct-life-evidence/audit-20261019/`. Raw SDK/device logs can contain reconnection tokens; keep them private. Historical `device-acceptance.md` covers the initial APK only.
