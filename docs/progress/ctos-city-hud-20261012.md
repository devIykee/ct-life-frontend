# CTOS and scene actions — 12 October 2026

Implemented the user's requested iPhone-inspired CTOS interior and city HUD refinement. The approved Solana Duo rim, hinges and camera remain intact. References and design observations are recorded in `docs/research/solana-duo-phone-20261012.md` and `docs/research/city-hud-ctos-20261012.md`.

## Shipped behavior

- CTOS has four-column app pages, swipe navigation, page controls, a persistent Map/Wallet/Messages/Spaces dock, and an original lavender/mint wallpaper. Existing app routes and availability gates remain in use. Home returns from an app to its launcher page, from page two to page one, then closes the phone.
- The city HUD uses compact location/map and Cash/needs panels, round social controls, and a quieter movement stick. Chat, emotes and expanded needs are mutually exclusive; utility navigation dismisses temporary panels.
- Context actions follow the selected scene object; confirmed Stand follows the player's root. They avoid measured HUD controls and remain inside the safe viewport. Presses validate the rendered owner, target, phase and seat occupancy before acting.
- Prompt movement uses cached transforms after camera movement. It does not reposition itself from a layout callback.

## Verification and corrections

Evidence is outside the repository at `../ct-life-evidence/hud-ctos-20261012/`.

- `final-tests.xml`: **66 passed**, zero failures. Covers interaction state/ownership, prompt placement/disposal, chat focus, lobby microphone handling, phone paging/drag cancellation and Wallet presentation.
- `native-stress/receipt.json`: **24 native captures**, 60 moving-camera frames with the actual QA snapshot/Panel.Pick traversal, zero runtime errors or layout-struggle messages, zero REST writes, original services restored. Prompt projection is also checked against an independent camera-viewport mapping.
- Initial swipe tests caught Unity's captured Button events bypassing ancestor callbacks. Direct observers on grid Buttons and capture for blank-space gestures corrected that behavior; the original swipe assertions subsequently passed.
- Image review caught missing wallpaper custom-style bindings and an offscreen capture camera/display mismatch. Both were corrected and recaptured. Earlier `native-pass2` images are superseded for scene-anchor evidence.
- The first Android review exposed repeated UI layout errors while moving context prompts. Replacing layout-position changes with cached transforms removed that callback path. The final build passed the new native stress check and the repeated device sequence below.

## Seeker acceptance

Unity **6000.3.25f1**, local development Android build; **227.0 MiB**, 88 seconds, zero build errors and 25 warnings. Build log: `android-final-build.log`. APK: `../ct-life-build/client/Builds/ctlife-ctos-hud-20261012.apk`.

SHA-256: `cc3f704e8ad5e2d8b5e79dd3d1f890867aae86caf19f49a1b1892baeff6ea87a`.

Installed with replacement preserving data on Seeker `SM02G40619141343`. Existing joel/Gynoid character retained. Device QA screenshots use actual touch events and the running local backend; no profile edit or automatic seat command was used.

- Tapped a visible bench, pressed **Sit on the bench**, observed seated character and **Stand up**, then pressed Stand and observed the Sit offer return. Evidence: `seeker-final-sit-offer`, `seeker-final-seated`, `seeker-final-stood`.
- Dragged the camera, moved with the stick and released it, opened CTOS and returned to the HUD. Evidence: `seeker-final-camera-pan`, `seeker-final-movement-release`, `seeker-final-hud-restored`.
- Swiped across an app icon to page two without launching it; Home returned to page one and then closed. Earlier same-phone-source checks covered vertical drag cancellation, page buttons, dock Wallet launch and return. Evidence: `seeker-final-ctos-page2`, `seeker-final-ctos-home`, `seeker-ctos-vertical-drag`, `seeker-wallet-from-dock`.
- Needs, emotes and Android chat keyboard were visually checked. The HUD Map action opened the real Map; its close button restored the HUD. Lobby navigation worked. No test chat message was sent.
- Final error capture contained no Unity/AndroidRuntime error entries. A separate retained-process audit found 71 log lines for PID 9349, zero matching error lines, zero layout-struggle messages and zero fatal lines (`device-final-log-audit.json`). This was a bounded device run, not a long-session stability certification.
- Thermal status was 0 at the final run's start and 1 at its end. Returned to Lobby and backgrounded the app after acceptance.

Not repeated on device: a competing player taking the same seat, live microphone transmission, or real-wallet authenticated balances. Existing interaction/microphone tests cover their changed-code boundaries. The saved development session is not wallet-authenticated and Wallet truthfully displays its disconnected state. iOS and portrait layouts are outside this Android landscape change's acceptance scope.
