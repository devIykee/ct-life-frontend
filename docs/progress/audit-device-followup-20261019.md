# Seeker followup — 19 October 2026

**Latest state:** source checkpoint `31e48cee` passed **2067/2067 native EditMode tests**, zero failed/skipped. `ctlife-controls-lifecycle-dev.apk` built in129s (263.6MiB,0errors/37warnings), is installed and was hash-verified at16:54:21UTC: SHA256 `4ef24fa133acfe9626c2f3cac01596fd624069091f3ed65f983cbb2f3854d07d` (`controls-lifecycle-build.json`). The earlier `e7102bd…` entry-authority APK was never installed. Scoped entry and Roadstar device trials are complete; final restoration/service cleanup is complete and the device lock is released. The full route remains incomplete. Latest-build observations are separated immediately below from historical earlier-build evidence.

The user confirmed no one was using the Seeker during the questioned intervals. The fresh16:25–16:30UTC observation did not reproduce the earlier route; it does not identify its cause. See [input observation](audit-input-observation-20261019.md) and [lifecycle receipt](audit-vehicle-controls-lifecycle-20261019.md). The16:11UTC restoration/28-recording cleanup in the historical sections was complete before resumed testing. [The current handoff](../handoff/CONTINUE-HERE-20261019.md) is authoritative for continuation.

## Latest-build scoped device outcomes

A real two-second proxy cut at17:06:19UTC interrupted SUV entry while the avatar was still on foot beside the closed door. The Drive offer returned; claim release was about167ms after the cut, and street presence returned about3.581s later. Later ordinary reentry, short steering/gas/braking, map close followed by fresh gas, Get out and walking were observed. The independent observer recorded no errors/rejections. This is approach-phase evidence; open-door/seated cuts and the initial exit-grant distance were not established. The map opened with controls released, so this is not held-input modal cancellation or device focus/pause proof. See [entry-authority receipt](audit-entry-authority-20261019.md).

The offline Roadstar showcase trial showed boarding, short ground acceleration/braking, flight into Builder Hills, descent to grass beside buildings, reverse movement onto pavement, normal exit and walking. The two recordings have a103s gap, and the final calibrated map position belongs to the player after exiting/walking. Precise clear-road touchdown, distinct ground steering, Pro mode and continuous full-route coverage remain unverified. See [Roadstar receipt](audit-roadstar-device-20261019.md). A direct adb screenshot also captured the green/yellow sky fault, leaving a rendering investigation beyond video encoding alone; no responsible subsystem or remedy is established.

## Final cleanup after the latest trials

At18:12:33UTC the app was stopped, the current fixture was privately backed up, and all three original preference/key/profile files were restored from `device-backup-input-origin-20261019.tar`; all byte comparisons matched. The latest APK remains installed. No uninstall/data clear occurred. Recorders are stopped, the temporary device touch helper is removed, and reverse mappings4010/2567/7880/7881 point to their original host ports. Root released the owned device lock at18:17:06UTC.

Seven new task recording files were SHA256-matched to retained host copies before removing only their phone duplicates. Six have valid ffprobe metadata; the initial aborted observation lacks an MP4 moov atom and is preserved privately as an invalid probe, not acceptance evidence. Unrelated earlier recordings and QA XML were retained. The direct observer completed at17:26:14UTC. Owned backend22726/proxy26668 stopped after command verification at17:28:38UTC; ports4012/2569/2570/2571 were closed and original4010/2567 services were preserved. Receipts: `input-roadstar-device-restoration.json`, `input-roadstar-recordings-final.json`, `entry-authority-services-cleanup.json`. No remote push, deployment, store submission, real payment or external message occurred.

**The remaining sections are chronological history. Their older artifact hashes, pending checks and cleanup statements describe those earlier checkpoints.**

## Earlier followup snapshot and scope

`ctlife-followup-dev.apk` was built successfully and installed with `adb install -r`, without uninstalling or clearing app data. SHA-256: `b10e60395848dfaa4ed37cb0547769fa84f3f0176090f741f6ba9174e38abcef`. Build: 263.6 MiB, 143 seconds, 0 errors, 37 warnings. Full native EditMode before that build: **2018 passed, 0 failed, 0 skipped**. This APK predates the subsequently diagnosed plaza geometry repair.

The original 1971-test APK and its partial route results are historical evidence, not acceptance for this followup. The broader prescribed route has not been completed twice.

## Observed results

- Fresh offline Timeline starts with full needs, zero fictional Cash and no Drone access. The visible lower-leg burial persisted in this snapshot, including after ordinary Duo Unstick. It was not counted as resolved.
- Settings was inspected at Large and verified XXL (`Largest` footer), using ordinary UI taps and scrolling. Movement, Simple/Pro flight controls, and Unstick labels/help remain readable and fit the approved Duo layout. See [specific visual receipt](audit-settings-device-20261019.md). The file `followup-settings-xxl-top.png` still shows Large and is explicitly not XXL evidence.
- A real foreground two-second socket cut destroyed five active connections while the independent direct observer remained connected. The same Timeline venue returned with a new session in approximately **3.53 seconds**, followed by the street in **3.73 seconds**. Subsequent ordinary walking was observed in both rooms, without observer errors or rejections. See [backend receipt](backend-followup-20261019.md).
- Earlier interruptions are separate trials: Unity restarted adb; another deliberate cut occurred while Android's notification shade paused the game. Neither substitutes for the foreground trial. The shade screenshot was moved to private task storage because it includes unrelated notifications.
- No driving disconnect was injected. Short and 45-second vehicle failure paths, post-repair flight behavior, showcase placement, summit landing/exit, cliff recovery, Roadstar streaming and the complete repeated route remain unverified on this APK.

## Grounding diagnosis

The combined live island/venue probe found the rendered island `plaza` triangle at **Y=0.26057452** above the venue collision floor at **Y=0** at arrival `(0.773512, -7.359462)` in X/Z. Earlier standalone avatar/venue probes omitted that separate streamed island mesh. The repair therefore belongs to world paving, not an avatar lift. Batch animation did not advance, so the probe's IK comparison is not valid animation evidence. See [avatar investigation](audit-avatar-followup-20261019.md).

## Device interruption and pending restoration

At **10:46 UTC**, `adb devices -l` returned no devices. The user was asked asynchronously to reconnect the Seeker. The app's current state cannot be verified while it is disconnected.

The followup backup is private: `/Users/mac/.ctlife-local/device-backup-followup-20261019.tar`. Its three verified members are the original PlayerPrefs XML, `files/automation.key` and `files/profileInstalled`. Their contents must not be printed or committed. Restoration from the earlier pass does **not** mean this followup's test preferences have been restored.

When adb returns, before final cleanup:

1. Retain/validate the new device recordings. Some contain paused-system UI and must remain private; the first recording was interrupted by adb restart. Do not claim a usable video merely from starting `screenrecord`.
2. Complete the required gameplay checks on the final build, then stop the app and restore the three original private files byte-for-byte from this followup backup.
3. Remove the task-owned `/data/local/tmp/ctlife-audit-touch-20261019.dex` and only the task recordings after verified host copies. Return reverse mappings to API4010/realtime2567 if they remain present.
4. Record the actual installed APK hash and device cleanup outcome. No uninstall, data clear or real-wallet payment is authorized by these checks.

The owned isolated backend, proxy and observers were stopped with matched PID/command/port evidence; original API4010/realtime2567 services remain running. See `backend-followup-cleanup.json`. The gap between the first observer window and the later empty vehicle window is documented; there is no continuous observation claim.

## Reconnected continuation

The user reconnected the Seeker. A second private snapshot of its current three files was preserved as `device-backup-reconnected-20261019.tar`, then the plaza APK installed successfully. Eight reviewed Seeker images show full shoes/lower legs after arrival, walking, the entrance transition, exterior paving, Unstick and online arrival. Original courtyard cobbles/fountain base are visible again. Unstick showed **Back on safe ground** and returned control. Both 180-second plaza recordings were pulled; `ctlife-plaza-final-exit.mp4` parses with ffprobe (179.989744 seconds). The reviewed stills do not quantify gait smoothness or prove every part of the longer traversal was recorded.

That walk exposed a separate on-foot camera feedback loop: direct movement used live camera yaw, the actor turned toward that input, and automatic recenter rotated its coordinate frame again. Eight of ten baseline tests reproduced this; intentional path and seat controls passed. The repair retains camera yaw/pitch during direct input and braking while preserving explicit orbit, paths and new seat settling. Final native suite including all 11 new cases passed **2034/2034**, zero failed/skipped. See the avatar followup receipt for the research and test design.

The controls APK built successfully and installed with `adb install -r`: `ctlife-controls-final-dev.apk`, **263.6 MiB, 118 seconds, zero errors, 37 warnings**, SHA-256 `0ffb83f3f87d9a05e246399385961d15cc7277b8bbb7181f8ed0f9f76f710a15`. A fresh local dev-auth test character is used after the previous character's needs depleted; no real wallet is connected or charged.

The audit backend/proxy restarted on their isolated ports. The prior final-plaza observer saw ordinary walking but no vehicle registrations, then an intentional app stop for the camera repair. Cleanup described above remains pending until testing finishes.

## Controls build: driving disconnects

A five-second held backward stick moved the player about19.5m south, with the camera heading retained until a later explicit orbit. Public SUV entry and ordinary steering/gas moved the SUV onto the road. An uncontrolled simultaneous downstream transport loss at12:50:40UTC released that claim; no injected fault had occurred. Its underlying cause is unverified. Subsequent controlled trials used the adjacent yellow **sports car**, not the SUV.

- **Two-second cut,12:55:25UTC:** four device sockets destroyed; driver claim released193ms later. Street presence returned4.212s later; ordinary on-foot control and visible shoes were reviewed, followed by a new claim of the same sports car.
- **45-second cut,12:58:48UTC:** the observer measured3.533m/s before the cut. The driver claim released269ms later; the unoccupied car's X/Z stayed fixed during the outage. Street presence returned46.486s after the cut, about1.486s after the block ended. The same sports car was claimed again at13:00:19UTC; the device displayed normal Gas/Get out controls. Post-final-reclaim driving motion was not separately measured.
- The independent observer remained connected with zero errors/rejections. These trials demonstrate lease loss and fresh join; the backend does not reserve the old driving lease. See [the backend receipt](backend-followup-20261019.md) for host/client clock distinctions and exact evidence.
- `ctlife-controls-short-actual.mp4` and `ctlife-controls-long-actual.mp4` were pulled. The long file parses as100.021511s, but ends before the recovered street presence; later screenshots and observer events supply that evidence. Recording names alone are not acceptance.

The app was intentionally stopped at13:01:06UTC for cooling after Android reported a49°C battery. A preceding Get out tap was not separately verified before shutdown, so normal exit is not claimed from that sequence. The direct observer stopped afterward; the isolated backend/proxy remain running. No cold-device performance pass exists.


## Continuous flight and cliff observations

These observations used the earlier controls APK `0ffb83f…`. Two independent host-video reviews confirm the local helicopter takeoff, descent, grounded Get out/0m state, then normal exit and ordinary movement across `ctlife-heli-repeat-1.mp4` and the beginning of `ctlife-heli-repeat-2.mp4`. Near the summit, `ctlife-heli-final-shoulder-attempt.mp4` shows a sustained Get out/0km/h/0m state on the snowy shoulder below/right of the cairn, followed by normal exit at14:25:43.912UTC and an on-foot Fly offer beside the craft. This is a narrow shoulder landing, not a cairn landing or an easy-landing claim.

The low drone recording shows descent from3m, a road-level0m/0km/h stop and rotor spin-down at147.0–147.2s, then operator/on-foot view at147.20–147.25s. It supports visible touchdown before pocketing, but does not trace the internal Grounded/timeout branch. See [flight receipt](audit-flight-followup-20261019.md).

The finalized `ctlife-route-continuous.mp4` is732.354489s/540201322bytes. It shows departure from the cliff at548s, a sideways/downed reaction around553.5–554s, rising around555–557s, and upright posture on the lower face. Later ordinary running appears at680s. This establishes fall/reaction/get-up and returned locomotion; it does not establish physics-ragdoll versus authored knockdown, a specific roll animation, or automatic safe-ground rescue. No rescue veil/toast was observed in the reviewed interval.

## Earlier unattributed input activity and route pause

A second unexpected location/control change was captured continuously in `ctlife-roadstar-continuous.mp4` (688.698544s/469844595bytes). Review shows ordinary compact-car entry/driving/exit, walking back to the helicopter, boarding, continuous flight and coastal landing while the coordinator's commands were idle. Several joystick and pink Brake/Down states change. This is not evidence of an instantaneous teleport or a confirmed spontaneous game-state bug. Input origin remains unverified. The user subsequently confirmed no one was using the Seeker during this specific14:50–14:52UTC interval. Root resumed a separately recorded input-origin observation at16:25UTC. The earlier sequence remains unattributed. See [input observation](audit-input-observation-20261019.md).

A later60-second raw touchscreen observation beginning15:01:09UTC produced no events. It does not identify the earlier input source. The later idle recording is private until its contents are reviewed. All recorders have been stopped and their files pulled; copies still remain on the phone. The Roadstar has not been boarded or accepted in this leg. Building/kiosk collision, the complete twice-repeated route and cold-device60fps remain outstanding.

Intermittent colored sky bands in video and repeated Vulkan vertex-data warnings are recorded in [the rendering investigation](../research/audit-vulkan-rendering-20261019.md). Their cause remains unverified; no graphics setting or source remedy was applied from that evidence.

## Local checkpoints and focused followup

The previously tested source is saved locally as `92ee6ada` (published realtime contract) and `48173cda` (integrated island collision, controls and room recovery). No remote push occurred. The staged review found generated whitespace beyond the earlier tracked-only scan:9 generator comment lines in the contract commit, plus1795 Unity serialized empty-field lines in the gameplay commit (397prefab,243meta,1155asset). Handwritten source checks are clean; serialized/generator output was retained unchanged.

A subsequent source review identified a lease-loss gap during acknowledged vehicle entry and the6m local exit fallback exceeding the backend's5m exit allowance. Focused source/test work is underway separately from the installed2034-test APK. Do not attribute forthcoming entry-authority changes to that already-installed artifact or claim device acceptance for them. Original followup app-data restoration and owned backend/proxy/helper/recording cleanup remain pending.


## Earlier16:11UTC restoration and entry-build checkpoint

That source checkpoint passed2054/2054 native EditMode tests,0failed/0skipped, then Android packaging succeeded in133s,263.6MiB,0errors/38warnings. `ctlife-entry-authority-dev.apk` SHA256 `e7102bd72e5ff591b015d66f942aed227d801131c0ad328d7a7d41172a801d2b` was built but never installed. Source was locally committed as `8542f0a3`; no device acceptance is claimed for that artifact.

Original followup preference/key/profile files were restored and rechecked byte-for-byte at16:11:08UTC while the app was stopped. Installed APK hash still matches the controls build. All28 task recordings were ffprobe-validated and SHA256-matched to host copies before removing their phone duplicates; unrelated origin recordings/QA XML were retained. The touch helper and ownership lock were removed; no recorder remains. Reverse mappings4010/2567/7880/7881 point to their original host ports. Owned backend/proxy stopped, original4010/2567 services remain. See `followup-final-cleanup.json` and related verified-recording/service receipts. No uninstall, data clear, real payment, remote push or external message occurred.
