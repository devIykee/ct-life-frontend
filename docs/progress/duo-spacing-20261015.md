# Solana Duo spacing — 15 October 2026

READY TO COMMIT: `client/Assets/Scripts/UI/Phone/PhoneShell.cs`, `client/Assets/Scripts/UI/Phone/PhoneLauncher.cs`, `client/Assets/Scripts/UI/Text/Strings.cs`, `client/Assets/Resources/UI/phone-ui.uss`, `client/Assets/Resources/UI/Brand/solana-logomark.png{,.meta}`, `art/LICENSES.md`, this note — Center Duo dock/search and use official header mark.

The user requested a centered dock/search group with coherent spacing, matching widths, and the official Solana mark before “Duo”. Inspected the actual 15 October Seeker screenshot at `ct-life-evidence/resume-20261015/seeker-resume-duo.png` and existing phone layout before editing.

- Grouped the vertical dock and search into one right-hand rail, vertically centered in the launcher. Both are 124 units wide with a 20-unit gap and the same 62-unit end radius. The dock retains four existing routes and icon artwork.
- Search remains a real button; its existing search overlay hides the entire rail. The wallpaper glass still observes its ancestor geometry, so moving the group retains the sampled backdrop.
- Replaced the three approximate CSS stripes and “Solana Duo” label with the unmodified official gradient mark followed by localized “Duo”. Read the [official branding page](https://solana.com/branding) and fetched its original PNG; source, hash and brand-usage guidance are recorded in `art/LICENSES.md`.
- Hardware, wallpaper, widgets, grid, routes, app screens and account state are unchanged.

Validation by this lane: reviewed the scoped diff, checked existing `PhoneLauncherTests` queries remain recursive, verified source PNG dimensions (101×88), byte count (3,485) and SHA256. No dependencies added. No Unity/ADB/build/Git mutation performed by this lane. Repository-wide whitespace check reports only the pre-existing `lobby-rooftop.png.meta` whitespace, preserved untouched.

Coordinator verification:

- Existing `CTLife.Tests.PhoneLauncherTests`: **11 passed, 0 failed** in Unity 6000.3.25f1. No new test or dependency added for this layout change.
- Android development build succeeded: **227.0 MiB, 106 seconds, 0 errors, 25 warnings**. APK SHA256 `e4fde35b6ca70a380162d1b2ca42323e16921d79664d2ba19a72921326cc343c`.
- Installed on Seeker and visually inspected the actual rendered header and centered controls. Device UI geometry reports matching dock/search width of approximately 138 screen pixels. The group spans y258–960 inside the launcher y112–1106: both centers are y609.
- Actual Seeker touches opened/cancelled search, opened Wallet from the dock and returned from Wallet with Home. Wallet correctly showed its disconnected state for the preserved developer profile. Scoped device error log was empty.
- Restored the beginning of onboarding: the opening `Sign in with Solana` screen is visible. The previous local session was privately backed up; its character was not reset. No wallet signature/payment was performed.
- The device lost its ADB reverse mappings during the initial walkthrough. Re-applied local-server forwards and started the existing reconnect helper for this review. Local API/realtime health returned 200; local Helius readiness remains unavailable and was not changed by this UI work.

Evidence is outside Git in `ct-life-evidence/duo-spacing-20261015/`: phone-tests.xml, android-build.log, seeker-duo.png/json, seeker-search.json, seeker-wallet.json, device-errors.log and seeker-onboarding-start.png/json.
