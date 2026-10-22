# Seeker gameplay-demo confirmation — 19 October 2026

[Inference] **Ready to record the scoped offline gameplay demo.** This recommendation is supported by a fresh installed-build recording of walking, Wave, parked-car blocking, Roadstar driving, takeoff, flight, landing, normal exit, walkaway, and Duo Settings. No blocking failure was found in the independently reviewed action windows. Full live-service promotion and the complete geographic route are outside this confirmation.

The user authorized the remaining confirmation and asked for repairs to take priority over expanding tests. Two concrete faults were found in the first take, repaired, built, and checked again on the Seeker. Existing design components and artwork were retained.

## Repairs

- **Offline Wave:** the emote handler previously returned on the absent venue channel before playing the local animation or closing the picker. Offline island mode now uses the existing AvatarView one-shot path and dismisses the picker. Online acknowledgement behavior remains unchanged. The final recording shows the hand wave and picker dismissal.
- **Lodge decorative cars:** the red sedan and adjacent green SUV were instanced decoration without ParkedCar markers or body collision. The avatar visibly entered the red body in the first recording. Decode now creates reusable static Prop-layer boxes from authored instance transforms/bounds for unmarked vehicle decoration. Marker-owned street cars retain their existing collision and swap/hide ownership. No asset rebake or art change was needed. On-device renewed held movement stops outside the red body and lateral walking resumes.

The initial Leave-button tap opened the map as the existing DoorOpenMap action specifies. Walking through the actual doorway works. This was not treated as a failed exit or repaired as an input bug.

## Build and focused checks

- Unity 6000.3.25f1, native `CTLife.Tests.IslandSafetyTests`: **12/12 passed**, zero failures/skips, Editor exit0. The new actual-lodge regression checks both decor bodies with a player-sized capsule, repeat Decode, and marker-owned collision removal on swap.
- Android development build: **263.6 MiB, 168s, 0 errors, 37 warnings**. Source-freeze hashes matched after packaging.
- APK: `demo-confirm/ctlife-demo-confirm-dev.apk`, SHA256 **f25ac2abc9273d682319d4588a5d37f8f60b72feb5d8990cada1c8fb24e66383**. Installed APK hash matched at installation and cleanup.
- Normal boot selected **OpenGLES3** on the Seeker through the previously committed exact device/GPU filter; no Unity graphics-API override was supplied.
- Independent source review found no concrete regression in the two repairs. No new dependency, UI component, typography, palette or layout was introduced.
- No full-suite rerun, iOS build, all-mode/whole-route run, or light/dark UI audit was performed in this focused Unity Android pass. Earlier broader results belong to their documented source/artifact.

## Final device recording

Launch used `env=Mock`, `islandDemo=1`, `showcaseFleet=1`, `autoenter=1`, `venue=hackathon_house`, `qaUi=1`. These are explicit local development fixtures, not premium account entitlement or live backend evidence. Root exclusively controlled the device using ordinary touch events; no scripted vehicleDemo/islandTour route or runtime teleport was used.

The uninterrupted final source recording spans **20:22:27–20:44:50 UTC**,1342.971s,701640303bytes. SHA256 **26f9b2a14ae4ea5c5018ef20e7779e807c2c195dac16e851d4d18060ea996ec0** matched the phone copy. Long intervals contain idle review time. It is a portrait recording canvas with landscape gameplay letterboxed, suitable as confirmation evidence; the finished promotional take should begin recording after landscape has settled.

| Confirmed action | Approximate source-video seconds |
|---|---:|
| Wave animates and picker closes |112.5–113.5|
| Red decorative car blocks approach and renewed held input |347.5–349;363.5–364.5|
| Roadstar entry completes |644.5–650|
| Ground driving, steering and stopping |737.5–750.5|
| Takeoff and hover; frame801 shows0km/h and32m |792–805|
| Airborne turn toward city/sea |868–871|
| Short forward flight and brake to0 |938–945|
| Land, descent, ground mode and road stop |948–998|
| Normal exit onto solid ground |1133.5–1134|
| Walkaway and Duo home |1245.5–1250|
| Settings opens, outside tap closes, fresh movement responds |1334.5–1342|

The Land command completes: height reads3m at987s and1m at988s; wheels/ground Drift controls return by989s, and the car stops by998s. A later prolonged Brake hold starts at1028s **after touchdown** and causes reverse movement; it was not a brake held throughout descent. Normal exit succeeds afterward. Brief flagpole occlusion occurs during the initial lodge car entry, without blocking entry or subsequent driving. These are raw confirmation takes, not polished choreography or precise landing-placement acceptance.

The earlier first take also confirmed ordinary lodge doorway crossing and Duo Settings. An older fleet recording ended before later transit, and a subsequent old-build coast/ocean attempt was discarded after the coordinator left the vehicle coasting while reviewing captures. Neither is claimed as a continuous successful route. The successful new-build sequence above supersedes them for this scoped confirmation.

## Sky review

No recurrence of the known neon yellow-green upper-sky band was detected in the final clip. An independent reviewer screened all **40346 frames** with the reference color/area test, using the actual letterboxed gameplay area, and visually inspected60 flight-window samples plus four larger/orientation frames. Maximum matching area was0.569%, compared with84.253% in the known-fault reference; no frame crossed the existing broad-band thresholds. Earlier same-rendering-repair clips also had no match in30670 screened frames.

This is evidence of no detected recurrence in these recordings, not proof of permanent resolution or a GPU root cause. The color-specific/downsampled scan can miss different artifacts; manual inspection was sampled. Detailed coverage and limitations are in the external sky review.

## Scope for the demo

The confirmed material supports a local island gameplay demonstration: avatar movement/emote, world interaction, car driving and flying/landing, and the Duo shell. Live multiplayer, calls/Spaces, trading/PnL, payments, server-backed jobs and premium entitlement were not confirmed by this offline take. Existing integration work and earlier device results retain their own scope. The full80-second geographic itinerary and every shot in the video bible were not repeated here.

Proceed with the gameplay recording using the tested build and deliberate, short inputs. Settle landscape before starting screenrecord, and release Brake after stopping because holding it engages reverse. No further broad test pass is required for the above scoped demonstration.

## Cleanup and evidence

At **20:53:57 UTC**, the app was stopped and all three original pre-pass preference/key/profile files were restored byte-for-byte. The tested APK remains installed. All owned recorders stopped; four verified host recordings were retained and only their duplicate phone copies removed. The temporary touch helper was removed, reverse mappings4010/2567/7880/7881 restored, and the owned Seeker lock released. No new backend services were started. No remote push, deployment, store submission, wallet transaction or external message occurred.

External evidence: `/Users/mac/Documents/codes/projects/ct-life-evidence/audit-20261019/demo-confirm/` contains the APK, build/test receipts, source hashes, action review, bounded sky review, verified recording metadata, input ledger and reviewed stills. Raw recordings/logs and preference backups remain private under `/Users/mac/.ctlife-local/demo-confirm-20261019/`. Keep private files and logs out of git.

A **137.8-second proof reel**, `gameplay-proof.mp4`, contains12 explicitly documented hard-cut excerpts with idle intervals removed. It preserves the full gameplay viewport at1200×540/30fps and has no audio because the source contains none. It is review evidence, not a continuous route or the finished promotional video. SHA256 `12696086916f54544ec8384a1c4261d15dac3d33814a45f4d06d79758bb33de6`; source ranges and encoding are in `gameplay-proof-cuts.json`. Complete decode and representative-frame checks passed.
