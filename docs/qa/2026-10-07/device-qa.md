# CT Life: independent device QA on the installed Seeker build, 7 October 2026

Tester: independent QA agent. This report covers only what was observed on the phone, in the logs and in the APK. Reports from earlier agents were used to find launch switches, not as evidence.

Some statements below are not direct observations. Each one is labelled at the start of the sentence: `[Inference]` for a conclusion drawn from evidence, and `[Unverified]` for something not checked.

## 1. What was tested

| Item | Value |
| --- | --- |
| Device | Solana Mobile Seeker `SM02G40619141343`, Android 16, Mali-G615 MC2, 2670×1200 panel (supports 120/90/60/30 Hz) |
| Package | `com.ctlife.app` 0.1.0 (versionCode 1), `DEBUGGABLE`, development build, installed 2026-10-07 18:01 |
| Identity | SHA-256 of the on-device `base.apk` is `a8e94ba0…b51d`. It matches `client/Builds/ctlife-playable-landscape-v7-clean-package-20261007.apk` (162,442,919 bytes), so this is v7. |
| Conditions | **The phone was on AC power at 100% battery the whole time.** Battery temperature was 35→39 °C. Skin temperature was already 38.3 °C at idle before the first launch, so the room was warm. No unplugged or battery-powered test was possible because adb uses the USB cable. |
| Rules followed | Nothing was installed or uninstalled. Unity was not launched. No wallet approval, credentials, chat messages or transactions. Device settings that were changed for the rotation and thermal tests were restored. Of `accelerometer_rotation` and the thermal override, both were confirmed restored afterwards; `user_rotation` was set back to 0. |

**How the development mode is enabled.** The source shows this, and the device confirmed it. `LaunchArgs.cs` reads Android intent extras, and `GameBootstrap` swaps in `ReviewTransport` (local sample data) when `review=1` is passed. The command used:

`am start -n com.ctlife.app/com.unity3d.player.UnityPlayerGameActivity -e review 1 -e reviewControls 1 -e reviewLandscape 1 -e reviewScreen dashboard|city -e venue terminal_cafe`

This mode joins no room, has no voice and has no backend. The other development path is `autologin=dev`. It needs the Mock backend at `http://localhost:4010`, which does not exist on the phone, so it was not usable.

## 2. Results by flow

| # | Flow | Result | What happened |
| --- | --- | --- | --- |
| 1 | Cold start to sign-in screen | **PASS (slow)** | The sign-in screen is usable about 8.3 s after launch (section 3). About 3 s of that is spent retrying a backend at `localhost:4010` that is not there. |
| 2 | Sign in with Solana (normal user path) | **FAIL (blocker)** | Tapping it shows "No connection. Check your network and try again." The wallet never opens: `GET /auth/challenge → 0` because the build points at the Mock environment `http://localhost:4010/v1`. A real user cannot get past this screen. [02](device/02-signin-tap-wallet.png) |
| 3 | Android Back on sign-in | Minor | Nothing happens; the app does not exit or minimise. |
| 4 | Review (development) mode | PASS | Loads sample data labelled "VISUAL REVIEW · SAMPLE DATA" and reaches "Ready" 5.3–5.4 s after the process starts. |
| 5 | Lobby (dashboard) | PASS | Settings, Explore, Jobs, Bank, Me, Phone and Enter the City all open. The city HUD's Lobby button comes back here. [10](device/10-dashboard.png) |
| 6 | Settings | **PARTIAL** | The panel opens, scrolls and expands Credits. A vertical swipe that starts near a slider changes its value (Sound effects jumped from 100% to about 50%). [11](device/11-settings.png) → [13](device/13-settings-scrolled2.png) |
| 7 | Character creator | PASS (preview only) | Two looks, a full-body / face toggle and drag-to-turn work. The tribe, two traits, dream, alias and 18+ checkbox all work. It ends at "Preview complete. No character has been created." and offers no next step. The keyboard hides the alias field while typing. [72](device/72-creator-face.png), [82](device/82-creator-alias-keyboard.png) |
| 8 | City (Timeline Square) | PASS | Joystick walking, pull-to-run and camera follow all work. There are no NPCs or other people anywhere. [31](device/31-city.png), [32](device/32-city-walk-mid.png) |
| 9 | HUD: Chat | **FAIL** | The system keyboard covers about two-thirds of the screen. No input field or chat history is visible, so the user types blind. [34](device/34-city-chat.png) |
| 10 | HUD: Emotes | **FAIL** | The picker layout is broken. Its 31 buttons are about 20 dp wide and the labels wrap one letter per line, so they cannot be read. Tapping one does nothing in review mode, because the source returns early when there is no room channel. [36](device/36-city-emotes.png) |
| 11 | HUD: Mic | Not available | Shows the toast "Voice isn't available here yet." |
| 12 | Map travel to Terminal Café | PASS | Arrives in about 2.4 s with no loading screen. The player spawns on the pavement outside the cut-away café walls. [63](device/63-cafe.png) |
| 13 | Map travel to Spaces Studio | PASS (with defect) | Arrives in under 3.5 s. The spawn point overlaps a chair. [69](device/69-studio.png) |
| 14 | Home (Parent's Basement) | **NOT REACHABLE** | In review mode the Home app shows only four greyed-out actions and no "Go home" button. `reviewPrivateHomeChecks` is missing from the Android extras whitelist, so the scripted Home path only runs in the editor. Forcing `venue=parents_basement` loads a fallback greybox room with the raw id as its title. [43](device/43-phone-home-app.png), [92](device/92-home-basement.png) |
| 15 | Phone apps | PARTIAL | Map, Jobs, Bank and Settings work. Profile works but truncates "Wallet · visu…only". **Rich List shows "Something went wrong. Please try again."** because it has no review data, and the red Unity Development Console then appears over the game. [41](device/41-phone-richlist.png) |
| 16 | Minigames | PASS (3 of 9 tried) | Earnings Desk: answer, feedback, next, cancel. Mod Desk: answer, feedback, cancel. Clip Cut: set in/out points, cut, see the score, end practice. The other six were not run. Earnings and Mod Desk are text quizzes. [51](device/51-earnings-answer1.png), [55](device/55-moddesk-answer.png), [99](device/99-clipcut-cut.png) |
| 17 | Background and resume | PASS | Same process, state kept (still in the Studio), hot resume in 200 ms. [87](device/87-resume-4s.png) |
| 18 | Rotation | PASS (partly) | The app refuses portrait. Flipping between the two landscape directions could not be tested over adb: the app follows the physical sensor and the phone was lying still. |
| 19 | Low memory | PASS | `send-trim-memory RUNNING_CRITICAL` was delivered (`APP_CMD_LOW_MEMORY`). The process survived but freed no memory (PSS 652→653 MB). |
| 20 | Crashes and ANRs | PASS | Across 8 processes and about 45 minutes there were no FATAL errors, signals, ANRs or low-memory kills. Every exit record is a force-stop I sent. |

## 3. Cold start (normal path, no extras)

There were three cold starts. Timings are measured from `am start`.

| Milestone | Run 1 | Run 2 | Run 3 |
| --- | --- | --- | --- |
| `Displayed` (first frame, black) | 897 ms | 476 ms | 461 ms |
| Unity "Fully drawn" report | 2.59 s | — | 1.72 s |
| "Made with Unity" splash | ~2.2–3.9 s | ~2.1–3.9 s | ~2.1–3.9 s |
| CT Life loading screen ("Loading config…") | — | 4.9 s | 4.95 s |
| Game's first boot log line (`[boot]`) | +4.3 s | — | +3.7 s |
| Three `/config` requests to localhost fail; the game falls back to its built-in copy | +7.7 s | — | +7.0 s |
| **Sign-in screen usable** | ~8 s | **8.37 s** | **8.27 s** |

The usable time was measured by watching for screen hashes to start changing: the 3D avatar on the sign-in screen idles, so the screen changes every frame once it appears. The "Fully drawn" report fires about 6 s before the game can actually be used, so it misreports start-up time. [Inference] Removing the localhost retries and the Unity splash would bring the sign-in screen to about 4–5 s.

## 4. Performance

Unity's own `[fps]` log lines were parsed every 5 s. SurfaceFlinger's per-second frame-delivery rate for the app's surface was read from logcat. Thermal state, battery temperature and memory were sampled every 5–6 s. The joystick was moved in a square pattern so the player kept walking. `dumpsys gfxinfo` records nothing for this app, because Unity draws to its own Vulkan surface and gfxinfo only tracks Android's UI toolkit.

| Scenario (length) | Android thermal status | Frame-rate target | Unity average fps | SurfaceFlinger fps | Worst 1%-low | GPU ms* | Main-thread CPU ms* | Draw calls (min / median / max) | Triangles | Memory (PSS / max RSS) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| City, real conditions (210 s) | 2, then 3 after 35 s | **30** (render scale 0.6) | 30.1 | 29.1–31.2 | 10.8 (one 92 ms hitch at about 20 s) | 12.9 | 10.7 | 119 / 136 / **180** | 115k–143k | 644–674 MB / 763 MB |
| Café interior, real conditions (210 s) | 3 throughout | **30** (render scale 0.6) | 30.1 | 30.1–30.2 | 29.9 | 11.0 | 10.8 | 67 / 153 / **200** | 49k–72k | 655–696 MB / 783 MB |
| City, thermal status forced to 0 (150 s, synthetic) | 0 reported; real skin temperature 41.6–42 °C | 60 (render scale 0.7) | **60.2** | 59.3–60.4 | 36.1 | 13.0 | 8.2 | 118 / 134 / 180 | 115k–143k | 632–663 MB / 754 MB |

\* "Last completed frame" readings from Unity's frame timing, averaged across 5-second windows. They are not true per-window averages. At a 30 fps cap the GPU clocks down, so GPU times from the 30 fps runs are not comparable with the 60 fps run.

**Thermal behaviour.** The phone's skin sensor has throttling thresholds at 39 °C (light), 40 °C (moderate) and 41 °C (severe), read from `thermalservice`. The game's `ThermalBudget` lowers the target to 30 fps at moderate and to render scale 0.6 at severe.

- The phone reached moderate during the first round of cold starts and severe about 7 minutes into testing.
- After 9 minutes with the screen off, the skin temperature levelled off at about 40.0 °C (still moderate) because the phone was charging.
- Skin temperature peaked at 42.4 °C, the CPU at 56.6 °C and the battery at 39 °C.
- While the game targets 30 fps, the whole display drops to 30 Hz (`frameRateOverride uid=10264 30Hz`), so menu scrolling also runs at 30 Hz.

**What this means.** On this phone in these conditions, a player sees 30 fps all the time. When the thermal status was forced to 0, the city held 60 fps at render scale 0.7 with about 3.7 ms of GPU headroom. [Inference] So the 30 fps cap comes from the game's own thermal policy, which trips at 40 °C skin, not from a GPU limit. [Unverified] Whether 60 fps can be sustained without heating past the severe threshold, or while running on battery, was not tested.

Comparison with the spec budgets:

| Budget | Target | Measured | Result |
| --- | --- | --- | --- |
| Frame rate | 60 fps cap, never below 30 | 30 under real conditions; 60 only with the thermal override | **FAIL** (in practice) |
| Memory | ≤ 1.5 GB | PSS 0.63–0.70 GB, RSS ≤ 0.78 GB (graphics 210 MB) | PASS |
| APK size | ≤ 150 MB | 162.44 MB | **FAIL** |
| Draw calls | < 150 | City peak 180; café median 153, peak 200 | **FAIL** |
| Triangles | 100k–150k | 115k–143k in the city with **one** avatar | At the limit; no room left for crowds |

**Garbage collection.** The only Java garbage collections logged were triggered by my own `dumpsys meminfo` calls; they line up one-to-one with them. The Unity (managed) garbage collector does not write to logcat. The one hitch in the city run, 92 ms at about 20 s after launch, could not be traced to a cause.

**Other log entries.** The `mali_gralloc` "unsupported format" errors appear on every launch and resume; they are Vulkan swapchain format probes. MediaTek `perfctl` lines also appear. Swappy, Android's frame-pacing library, logs `couldn't find "libgame.so"` at every start. [Inference] That means Swappy cannot register for display-mode changes. [Unverified] I could not tell whether it has any effect on refresh-rate switching.

## 5. APK size breakdown (`unzip -lv`, 3,103 entries, 161.9 MB stored)

| Stored | Raw | Share | Content |
| --- | --- | --- | --- |
| 46.1 MB | 114.4 MB | 28.5% | Hashed Unity data files (scenes, shared assets, textures, meshes), 1,690 files |
| 36.7 MB | 36.7 MB | 22.7% | **Audio**: 944 FMOD sound-bank files (identified by their `FSB5` header). Everything under `Resources/Audio` is forced into the build. |
| 29.3 MB | 100.6 MB | 18.1% | `libil2cpp.so` (the game's compiled C# code) |
| 15.3 MB | 38.3 MB | 9.4% | `libunity.so`, **not stripped** (development player) |
| 12.4 MB | 12.4 MB | 7.7% | Java bytecode (`.dex`), **not minified**: Guava (2,228 classes), kotlinx coroutines, androidx appcompat, constraintlayout and fragment |
| 9.2 MB | 19.6 MB | 5.7% | `liblivekit_ffi.so` (LiveKit voice), for a feature that shows "isn't available here yet" |
| 5.1 MB | 14.8 MB | 3.1% | il2cpp metadata |

Only arm64 code is shipped, which is correct.

Where the space is wasted:

1. **Audio for places that are not open.** There are ambience tracks for 9 venues (about 50 MB of source WAV), 6 of which are "Opening soon". Each district also ships 6 music stems (bass, drums, full, lead, pad, tension), while the runtime log shows licensed full mixes playing (`full mix: music/licensed/…`). [Inference] Most of the stems are unused.
2. **Development-build overhead**: an unstripped `libunity.so` and the debuggable flag.
3. **No R8 minification** of the Java code.
4. **LiveKit is bundled** although voice is disabled.

[Inference] Fixing items 1–3 would bring the APK under 150 MB. The repo's existing `ctlife-release-size-audit-20261006.apk` is 139.3 MB. [Unverified] Its contents were not compared with v7.

**Manifest.** The app declares `CAMERA` and `BLUETOOTH` permissions it does not use (probably pulled in by LiveKit), plus `allowBackup=true` and `debuggable=true`. All four should be addressed before submitting to the dApp Store.

## 6. Visual and UX quality

The benchmark was The Sims 4 for visuals and bold, tactile mobile-game UI for the interface.

**Lobby.** It is clean and has a clear call to action ("ENTER THE CITY"), but it reads as a dark web dashboard rather than a game lobby. The isometric city illustration is the best-looking asset in the build. It is a flat 2D image, though, and its style does not match the 3D world. The player's own character never appears in the lobby; the profile picture is a generic icon. The settings gear sits loose in the middle of the header, and there is an empty gap under the cash and energy tiles.

**HUD.** The navy rounded tiles are large and readable, and the HUD stays out of the way of the scene.

- The "City · —" and "Market · —" chips show placeholder dashes.
- The phone is a white web-style sheet that covers about 40% of the screen. Its sticky "Where to next?" header takes about a third of the map list, which is mostly "Opening soon" greyed rows.
- The dashboard's small capital labels are about 8 sp; on the selected tribe card, the grey subtitle on yellow has weak contrast.

**Touch targets.**

| Control | Approx. size | Verdict |
| --- | --- | --- |
| HUD tiles | ~45×51 dp | OK |
| Phone button | ~53×60 dp | OK |
| Joystick | ~96 dp | OK |
| Emote buttons | ~20 dp wide | Fails |

Slider rows also intercept vertical scrolling.

**Characters.**

- **Faces.** Look 1 has a startled stare: a bright white ring around a small iris and no shading from the eyelids ([72](device/72-creator-face.png)). Look 2 is more appealing.
- **Contact with the ground.** In the real venues there is **no contact shadow** under the player, so the avatar looks like it floats ([feet crops](device/31-city.png)). The fallback greybox room does have a shadow.
- **Image quality.** Limbs show heavy jagged edges at render scale 0.6–0.7, and the idle pose is stiff.
- **Variety and animation.** There are only 2 looks, the creator cannot change anything, and no animation other than walk, run and idle was seen. Foot sliding could not be judged from still screenshots.
- **Style mismatch.** The illustrated characters in Clip Cut are much richer than the 3D cast, which makes the gap obvious.

**Environment.**

- Blocky, low-texture models with flat lighting and no ambient occlusion.
- **Every venue is empty**: no NPCs and no crowd.
- The interiors are cut away like The Sims, but the café's ceiling lamp floats at head height.
- The camera does not fade buildings that block the view; one fills half the screen after walking north ([35](device/35-city-chat-after-back.png)).
- Spawn points are outside the café and inside a Studio chair.
- On the sign-in screen, a 78 px band on the right edge (the width of the display cutout's safe area) is not covered by the screen's background ([01](device/01-signin.png)).

**Verdict against the benchmark.** The UI typography and spacing are tidy, but the interface is web-like rather than tactile; no press depth or motion was observed. The 3D world and characters are prototype quality and are not in the same class as The Sims 4.

## 7. Top 10 bugs and how to reproduce them

1. **Blocker: a real user cannot sign in.** Cold-launch with no extras, then tap "Sign in with Solana". The error "No connection…" appears. The wallet is never invoked, because the default environment is Mock at `localhost:4010`. The same misconfiguration adds about 3 s to start-up through `/config` retries.
2. **Critical: 30 fps in practice.** Launch review mode into the city (`-e review 1 -e reviewScreen city`). Within 35 s the logs show `thermalRaw=2/3 targetFps=30` and SurfaceFlinger reports about 30.1 fps. With `cmd thermalservice override-status 0` the same scene holds 60.2 fps. The cap comes from the game's `ThermalBudget` trigger at moderate (40 °C skin on this phone).
3. **Major: the emote picker cannot be read.** City HUD, then Emotes: the labels wrap one letter per line. [Inference] The `.ct-chip` buttons inside `.ct-emote-scroll` have no minimum width, unlike the creator chips in `landscape.uss`. The tap did nothing in review mode; [Unverified] whether it works in a real room was not tested.
4. **Major: the keyboard hides text input.** City, then Chat: the keyboard opens with no visible field or history. Creator, then Identity, then tap Alias: the field is hidden while typing.
5. **Major: Home cannot be reached on the device.** Phone, then Home: only greyed-out actions, no "Go home" button. The normal path is blocked by bug 1, and the scripted Home review only runs in the editor.
6. **Major: budgets exceeded.** The APK is 162.44 MB against a 150 MB limit. Draw calls reach 180 in the city and 200 in the café against a limit under 150. Triangles are already 143k with a single avatar.
7. **Moderate: sliders steal scroll gestures.** Dashboard, gear, then swipe up starting just above the Sound effects slider: the volume jumps to the finger's x position (100% to about 50%).
8. **Moderate: Rich List error and pop-up console.** Phone, then Rich List: "Something went wrong. Please try again." An exception is logged, and the Unity Development Console then appears over gameplay showing a red stack trace.
9. **Moderate: avatar grounding and spawn points.** In the city, café and Studio, the player has no contact shadow. Travelling to Spaces Studio spawns the player overlapping a chair, and Terminal Café spawns them outside its walls.
10. **Minor polish.**
    - The sign-in screen has the 78 px right-edge band, and the layout jumps when the error text appears.
    - Back does nothing on the sign-in screen.
    - Profile shows "Wallet · visu…only".
    - The HUD shows "City · —" and "Market · —".
    - The creator ends at "Preview complete" with no next step.
    - The Unity "Made with Unity" splash adds about 1.7 s to start-up.

## 8. Overall verdict

The build is stable and cheap on memory:

- no crashes or ANRs
- background and resume work
- PSS about 0.65 GB
- venue travel in 2–3.5 s

It is **not usable by a real player** and is **not at the target quality**:

- Normal sign-in fails, so everything past the first screen is reachable only through the development switch.
- On this phone in normal warm, charging conditions the game runs at 30 fps rather than 60.
- Chat and emotes are broken.
- Home cannot be reached.
- The APK and draw-call budgets are exceeded.
- The venues are empty, and the characters and world are prototype quality, far from The Sims 4.

The UI layer is tidy but web-like. Clip Cut is the most finished part of the game.

Rating: pre-alpha internal demo.

**Not tested:**

- real wallet sign-in or the backend
- multiplayer and voice
- 6 of the 9 minigames
- unplugged or battery-powered thermal behaviour
- a 10-minute 50-player stress test
- flipping between the two landscape directions
- foot sliding during motion

## 9. Evidence (under `docs/qa/2026-10-07/device/`)

- **Screenshots**: `01…100-*.png` (numbered by flow), plus `cold1-t*.png` and `perf-*-start/end.png`.
- **Cold start**: `cold-start-{1,2,3}-am.txt`, `cold-start-{2,3}-frame-changes.txt`, `cold-start-1-logcat-full.txt`.
- **Performance**: `perf-city/`, `perf-cafe/` and `perf-city-override0/`. Each holds:
  - `samples.csv` (thermal, battery and memory every 5–6 s)
  - `fps-summary.txt`
  - `logcat-window.txt`
  - `meminfo-end.txt`
  - `gfxinfo-framestats.txt`
  - `run.txt`
- **Logs**:
  - `app-pid-*.log` (per-process logs)
  - `logcat-full-session.txt.gz` (the whole session)
  - `exit-info-before.txt` and `exit-info-after.txt`
  - `thermal-final.txt`, `battery-final.txt`, `meminfo-*.txt`
- **APK**: `apk-unzip-lv.txt` and `apk-size-breakdown.txt`.
- **Scripts**: `perfrun.sh` and `parsefps.py`, so the measurements can be repeated.

The folder is about 148 MB, mostly full-resolution PNG screenshots.
