READY TO COMMIT: client/Assets/Scripts/UI/Phone/PhoneShell.cs; client/Assets/Scripts/UI/Phone/PhoneLauncher.cs (+ meta); client/Assets/Scripts/UI/Phone/PhoneWallpaper.cs (+ meta); client/Assets/Resources/UI/phone-ui.uss; client/Assets/Tests/EditMode/PhoneLauncherTests.cs (+ meta); docs/research/solana-duo-phone-20261012.md; this log — Give CTOS an iPhone-inspired Home Screen inside the approved Solana Duo hardware.

## 12 October 2026

Research: inspected Microsoft Surface Duo single-screen image, Google Pixel Fold hinge/product photograph, Nintendo NookPhone official poster and contained recipe UI. References/evidence and limitations are in the research note.

Code: asymmetric narrow device rim, hardware hinge, white glass, actual clock, 3-column native app launcher, functional home indicator, existing reduced-motion-aware entrance, unified app chrome. Preserved existing catalog IDs, feature/phase gates, app lifecycles and HUD restoration.

Wallet: registered `wallet`, displaying SOL/USDC/SKR/ORE via shared WalletBalanceService. Full token precision, distinct unknown/loading/confirmed-zero states, explicit stale labels, cluster name and read-only refresh. It unsubscribes when closed without disposing shared state.

Tests: all four WalletAppTests passed in the coordinator’s final 31/31 EditMode run. The phone lane inspected final-editmode.xml. No Unity, ADB, Git, downloaded shipping asset or dependency action taken by this lane.

Native Unity compilation, tests and review capture are verified below. Android build and Seeker installation completed; actual touch checks are recorded in `docs/progress/lobby-solana-duo-20261012.md`. Native review does not establish Android behavior or real-wallet RPC availability.

Coordinator compile found CS0246 for the unsupported NUnit NonParallelizable attribute in the new fixture. Removed it; package manifest is com.unity.test-framework 1.6.0; the installed Custom NUnit package reports 2.0.5 (its documentation says it is based on NUnit 3.5). Coordinator’s later native compilation and final tests passed after the correction.

## Runtime screenshot review, pass 3

Inspected coordinator captures in ct-life-evidence/lobby-duo-20261012/lobby-pass3: phone launcher, Wallet ready/large, Map, Bank, Jobs. Removed launcher greeting/subtitle/Your apps heading, leaving the three-column grid directly below Solana Duo. Later-phase heading remains when needed.

Reduced wallet summary to network/address and meaningful loading/error states. Kept full token precision; large captured balances already fit. In phone context only, removed repeated same-as-navbar app titles and Map marketing introduction, retained real place count. Reduced Bank hero spacing. Jobs Details action now receives usable width and active career tab styling survives shared chrome. The final native recapture verifies these refinements; no Unity action performed by this lane.

## Final native verification

Inspected ct-life-evidence/lobby-duo-20261012/lobby-final/receipt.json: pass=true, 28 screenshots, no failures, runtimeErrors=0, servicesRestored=true, restWrites=0. Its explicit scope is the actual DashboardScreen, AvatarPreview, PhoneShell and PhoneCatalog apps using isolated profile and read-only in-memory REST/RPC fixtures; this is not live backend or wallet data. Captured routes include launcher, Map, Jobs, Bank, Profile, Home and Settings, plus Wallet ready/large/unavailable/disconnected cases and phone closed.

Visually inspected final launcher (nine complete first-view app tiles), Jobs (selected Careers tab and unbroken Details label), and Wallet large (full precision fits). final-editmode.xml reports 31 passed, 0 failed, including all four WalletAppTests. Subsequent Android/device acceptance is recorded in the coordinator log.

## Device corrections

Actual Seeker review found Profile wrapping at its last glyph. Launcher labels now take full tile width and use centered single-line text with bounded ellipsis for unusually long names. The new device capture confirms Profile fits. Lower-grid inspection found nine catalog routes using the fallback dots icon; PhoneShell now maps those routes to existing bundled assets. All 29 catalog routes resolve to a concrete icon. The coordinator rebuilt and performed final device checks; see the lobby progress log.

## CTOS Home Screen revision

User approved the hardware but requested an iPhone-like OS inside. Read current Apple Home Screen primary docs and visually inspected all three relevant official images; references and observed details are recorded in the research note. Hardware kept intact.

Replaced the scrolling three-column launcher with four-column pages and persistent Map/Wallet/Messages/Spaces dock. Removed the Home title bar, retained subtle device identity, added original static Painter2D pearl/lavender/mint wallpaper using existing tokens. All app IDs/gates and the actual Wallet service remain connected. Page dots, horizontal swipes, keyboard navigation and the bottom Home control are functional; app-button drag suppression and capture-loss handling guard against a swipe launching an app. Off-page buttons do not enter the keyboard tab order. No external assets or dependencies.

Files: `PhoneShell.cs`, `PhoneLauncher.cs` (+ meta), `PhoneWallpaper.cs` (+ meta), `phone-ui.uss`, `PhoneLauncherTests.cs` (+ meta). Seven tests now cover actual shell/pager behavior. Source and tests handed to root for compile/capture. Current revision's native tests now pass **66/66**, including all seven launcher cases; native visual review also passes as recorded below; Seeker touch review subsequently passed, as recorded below; prior verification sections describe the earlier launcher. No Unity/ADB/build/Git activity by this lane.

First native CTOS run compiled: 64/65 targeted tests passed, including 5/6 initial launcher tests. The failing horizontal swipe exposed Unity captured events bypassing ancestor callbacks. Added direct grid-Button captured-event observers, preserved normal taps/dock behavior, and strengthened the swipe assertion. Independent review found a blank-page capture gap; whitespace now captures immediately and a seventh test covers outside release plus closing during capture. Source/test follow-up passed the coordinator's corrected rerun: **66/66**, including all seven PhoneLauncherTests, as verified directly in `ct-life-evidence/hud-ctos-20261012/editmode-final.xml`. Subsequent native visual/device validation is recorded below.

Native pass2 captured 23 shots with zero runtime errors, but visual review rejected the all-white wallpaper: the painter displayed only white. Working diagnosis is that direct inherited token reads did not resolve for the painter; the subsequent material assertion and recapture below verify the corrected binding. Bound phone-local custom-style aliases to the existing theme tokens, following Unity's [custom-style query pattern](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-get-custom-styles.html). Added assertions for actual nonwhite resolved material color, visibility and layout width to the existing catalog test. The subsequent wallpaper recapture and material assertion both passed, as recorded below. Grid and page-two typography fit in the inspected native image.

Final native CTOS review: visually inspected `native-final/hud-06-ctos-open.png`, `hud-06b-ctos-page-two.png` and Wallet unavailable. Wallpaper now renders lavender/mint curves and the white translucent dock reads clearly. Labels fit both pages. `phone-material.xml` passes 7/7, including resolved nonwhite material/geometry assertions added after the white-wallpaper finding. `native-final/receipt.json` reports PASS, 23 combined HUD/CTOS shots, zero runtime errors, no REST writes, restored services. These are isolated fixture captures, not live backend or real wallet results. Phone native source/test/visual work is verified; subsequent build and Seeker touch acceptance are recorded below.

## Final Seeker acceptance

The coordinator installed the final Android build (227 MiB, SHA-256 `cc3f704e8ad5e2d8b5e79dd3d1f890867aae86caf19f49a1b1892baeff6ea87a`). The existing `joel` / Gynoid player was preserved. Phone source was unchanged after the tested wallpaper binding correction.

Coordinator-performed actual Seeker touches passed:

- Swipe across the Business icon moved to page two without launching an app.
- Vertical drag across Settings neither opened an app nor changed page.
- The persistent Wallet shortcut opened the real Wallet app and showed its disconnected state; no token balance was invented.
- Home returned from Wallet to page two; the first page dot returned to page one; Home closed the device.
- After the final corrected HUD rebuild, a fresh swipe to page two, Home to page one and Home to close all worked again. The HUD Map action opened the actual Map app; its close control restored the HUD.

This lane inspected the final page-two and dock-Wallet device screenshots, and directly read `final-tests.xml` (66/66 passed) and `native-stress/receipt.json` (PASS, 24 shots, no failures, zero runtime errors). Evidence lives under `ct-life-evidence/hud-ctos-20261012/`, including `seeker-ctos-page2-swipe`, `seeker-ctos-vertical-drag`, `seeker-wallet-from-dock`, `seeker-final-ctos-page2`, `seeker-final-ctos-home` and `seeker-final-hud-map` PNG/JSON pairs. Native fixtures remain separate from this device session, and the device's disconnected Wallet does not validate funded-wallet RPC behavior.

Phone source, native tests, native visual review and the scoped Seeker interactions are complete. Overall progress and Git checkpoint are owned by the coordinator. Only phone documentation changed in this final update; no Unity, ADB, build or Git action by this lane.
