# Lobby, Solana Duo and read-only wallet — 12 October 2026

## Delivered scope

The signed-in landscape lobby now gives the selected live character a larger, stable stage against an original illustrated CT City rooftop. Identity, fictional Cash and real wallet holdings have distinct positions. Left-edge navigation, bottom needs, quieter Dream/Home cards and a flat Enter the City action replace the previous crowded hierarchy. The illustration is a lobby backdrop, not a newly built playable map.

Solana Duo is one broad leaf of a fictional folding phone: asymmetric hinged rim, white glass, real local clock, a three-column launcher, working home indicator, back and close controls. Shared app typography, cards and actions were refined while retaining the existing catalog, feature gates and app behavior. Wallet displays SOL/USDC/SKR/ORE observations from the shared mainnet service, separate from game Cash. No signing or transfer is performed by balance viewing.

Research and sources: `docs/research/lobby-solana-duo-20261012.md`, `solana-duo-phone-20261012.md`, `wallet-balances-20261012.md`. Generated artwork prompt and provenance are committed; reference-game artwork is not bundled.

## Verification

- Native Unity capture: 28 states passed; 16 measured character/animation cases (Snow, Rain, Dex, Gynoid) had no cropped mesh vertices or vertices covered by controls. Zero recorded runtime errors, zero REST writes, test services restored. Includes large names/amounts, debt, needs explainer, wallet states and seven existing apps.
- EditMode: 31 passed, zero failed/skipped. PlayMode: 11 passed, zero failed; one existing optional eye screenshot exporter ignored because its opt-in output path was unset. Includes the actual Unity public-mainnet RPC transport test and Rain pupil regression.
- The final device-only corrections after those runs were launcher label sizing/nowrap and icon aliases. Profile had wrapped on Android despite the desktop capture; its final Seeker screenshot confirms a single line. All 29 catalog routes were checked against bundled icon assets; nine omitted route aliases now have meaningful icons instead of fallback dots. No new dependency was added. Android compilation and device review cover these corrections.
- Earlier test compilation rejected an unsupported NUnit attribute (CS0246); it was removed. Earlier layout checks caught the needs popover timing and overflowing amount presentation; the final automated checks passed after corrections. No failed step is presented as passing.

External evidence directory: `/Users/mac/Documents/codes/projects/ct-life-evidence/lobby-duo-20261012/`. Key receipts: `final-editmode.xml`, `final-playmode.xml`, `unity-public-rpc.json`, `lobby-final/receipt.json`. Native fixture screenshots are visibly labeled REVIEW / LOCAL FIXTURES and do not represent the player's holdings.

## Seeker acceptance

Installed on Seeker SM02G40619141343 using `adb install -r -t`; launched with local backend and QA UI observation enabled, preserving the existing joel profile and Gynoid look. No fresh profile, forced new sign-in or character substitution. The Gynoid's faceless design was checked against its source portrait and saved server look.

Observed device checks: lobby and character reactions; Solana Duo open; nine full first-view app tiles; launcher scrolling to lower apps; Wallet's disconnected state and disabled refresh; live Bank Cash/debt; live Jobs board; home indicator and back to launcher; close to lobby; Enter the City reached Timeline Square; city phone opened and outside-dismiss restored the HUD; Lobby returned to the new home screen. Captures include `seeker-lobby-final.png`, `seeker-phone-final.png`, `seeker-bank-final.png`, `seeker-jobs-live.png`, `seeker-wallet.png`, `seeker-phone-scrolled.png`, `seeker-city-hud-restored.png`.

The existing device session is a developer login rather than SIWS. Consequently Wallet truthfully shows Not connected and unknown holdings; the player's own SOL/USDC/SKR/ORE values, signed-in device refresh and logout clearing are **not accepted on device yet**. Automated service/state tests and public-address RPC reads do not replace that remaining test. No wallet prompt or money movement occurred.

During one local pass the ADB reverse mappings disappeared; Jobs showed loading and lobby showed Offline. Restoring the four local reverse ports allowed live data to load. Final launch restores these mappings. Recent Unity/AndroidRuntime error queries returned no entries. Thermal service was normal initially and mild (status 1) after city checks; the app was backgrounded during builds. This is a bounded smoke test, not a sustained performance or crash-free guarantee.

## Build and remaining limits

Android IL2CPP APK is stored outside the checkout at `../ct-life-build/client/Builds/ctlife-solana-duo-20261012.apk`. Final build succeeded in 79 seconds: 227.0 MiB, zero errors, 25 warnings (including compressed launcher-icon warnings). This is a local development build with real backend services, not the isolated review fixture. Evidence: `android-build-release.log`, `android-release-receipt.json`; SHA-256 `98ad48ad421aba3ebd203a3f6f71fb6acf8d543876720dba1486ba365314a092`. Installation returned Success. No iOS build was attempted; the project targets Seeker/Android.

Final installed-build recheck: `seeker-phone-icons-final.png` shows the corrected Week/Business/Builder/VC icons on the actual device; `seeker-phone-final.png` shows Profile on one line. Closing returned to the lobby, captured again as `seeker-lobby-final.png`. Recent Unity/AndroidRuntime error query remained empty. Thermal status was mild (1); the app was backgrounded after the checks instead of leaving a foreground 3D session running unattended.

The official public RPC endpoint has rate limits and no production availability commitment; a dedicated launch endpoint remains infrastructure follow-up. On-chain SKR precision is six decimals, while the imported contract configuration says five. Balance reads use on-chain precision; the read-only contract snapshot was preserved and a correction proposal committed.

Backend generation, Tripo permission and Iyke's Railway deployment are outside this presentation change. No backend files, paid generation, account settings or external deployment were changed. The unrelated `docs/SUBMISSION-KIT.md` was excluded from this commit.
