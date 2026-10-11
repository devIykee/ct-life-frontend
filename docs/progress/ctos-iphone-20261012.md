# Solana Duo: current iPhone visual reference — 12 October 2026

Status: implemented, native-tested and installed on the Seeker. Ready to commit and push.

The user rejected the earlier launcher because its icon construction and typography still looked like the game. This pass follows the supplied fold-leaf image and current Apple references, preserving the approved hardware and existing app routes. Primary research: [iPhone fidelity](../research/ctos-iphone-fidelity-20261012.md), [icon construction](../research/ctos-app-icons-20261012.md).

## Implemented

- Phone-only Inter4.1 Regular/Medium/SemiBold, official static TTFs with in-app OFL notice. Game fonts unchanged. Exact Apple font/symbol assets are not bundled.
- Original192px layered art for all29 app routes, plus fallback. Transparent continuous-corner enclosures, individually composed objects and colours; source renderer included.
- Original photographic dune wallpaper, two functional widgets, four-column paging, persistent vertical dock, compact status and search control.
- Calendar/analogue time uses actual local time; city schematic counts the same authored districts as Map and marks only a confirmed active venue. The schematic is not a geographically accurate map.
- App search filters the complete registered catalog, preserving phase/degraded gates, page state and real navigation. Native text entry; search dismissal uses the existing Seeker keyboard cleanup.
- Dock/search use cached wallpaper-aware blur, with a512px texture-side cap, no frame-buffer capture and no recurring render loop. Geometry changes and application focus recovery refresh the backing; detach releases textures/materials. This is a Unity approximation of glass, not Apple's native Liquid Glass renderer.
- Neutral app surfaces, phone-specific type sizes and blue actions, original lightweight back/close/search geometry.

## Generated wallpaper provenance

Built-in imagegen; no CLI/API fallback. Original file:
`/Users/mac/.codex/generated_images/01a11126-ce0a-7a03-8d49-7d2f1b9dc9f5/exec-ac02c446-f578-456a-98e2-6a3ff4e89cf1.png`.

Final project asset: `client/Assets/Resources/UI/PhoneOS/Wallpaper/Dunes.png` (copied unchanged).

Exact prompt:

> Use case: photorealistic-natural. Asset type: portrait wallpaper texture for an in-game mobile phone home screen, not a device mockup. Create an original premium landscape photograph of soft pale champagne sand dunes with fine wind-ripple texture, distant dark warm-brown rocky mountains, clear muted powder-blue sky. Portrait 1024 by 1536 composition. Horizon at 38 percent from top, sky uncluttered in top third for two UI widgets. Foreground dunes curve diagonally, calm areas for bright app icons. Warm late-afternoon natural side light, restrained realistic color, luminous atmosphere, camera-grade detail, understated and believable. Full-bleed image only. No phone, no frame, no interface, no text, no icons, no logos, no watermark, no person, no neon, no fantastical structures.

## Verification

External evidence: `/Users/mac/Documents/codes/projects/ct-life-evidence/iphone-ctos-20261012/`.

Initial launcher/wallet run:13passed,0failed. First actual Unity2670×1200render:26captures, no runtime errors or recursive-layout warnings; reviewed home, page2, Wallet, Search, map widget. Fixture captures are explicitly marked and use no network writes. Review identified tight icon-row spacing and role-font selector precedence; these were corrected before final device testing.

Final native run:26captures; runtimeErrors0, layoutMessages0, failures[]. Reviewed home, detailed schematic map widget, blurred dock/search, pages, Search and Wallet.

### Final shipping verification

- Unity6000.3.25f1, Android-target EditMode:15passed,0failed,0skipped (`tests-shipping.xml`). Includes catalog preservation, dock navigation, swipe suppression, cross-page search, no-result search, focused-input Escape, close-before-focus and Wallet states.
- Android development build: succeeded in88s,227.0MiB,0errors,25warnings. No claim of a warning-free build. APK`ct-life-build/client/Builds/ctlife-iphone-ctos-20261012.apk`; SHA256`268cc0756c774a3f10524035e4d8d15594bfd83919e0bac835a7474382c3d66a`.
- All93changed shipping assets matched the build clone byte-for-byte before install.
- Seeker install succeeded. Existing saved joel/Gynoid profile and game Cash remained; no reset, likeness request, wallet charge or purchase performed.
- Actual touch swipe across a first-page app reached page2 without launching it. Persistent Wallet dock opened the real Wallet route; Home retained page2. Both page controls worked.
- App search accepted native Android keyboard input, filtered to Wallet, and opened it. Keyboard dismissed on result launch, Cancel and outside-phone close; Android`mInputShown=false`and screenshots confirmed this.
- Calendar and city widgets opened the real Week and Map pages. Week's remote competition section displayed its existing no-connection state; this pass did not verify that backend service. Wallet reported not connected in the existing development session, so no live token-balance claim is made.
- Background/resume retained PID14130and restored home wallpaper/glass. Left the app on the first Solana Duo home page.
- Retained process-log audit:129lines,0Unity errors,0AndroidRuntime errors,0fatal signals/exceptions,0recursive-layout warnings. Filtered device error capture also produced no error records. This is scoped observation, not a crash-free guarantee.
- Thermal status0before testing and1at completion; phone backgrounded during build/doc work.
- Evidence: `seeker-home.png`, `seeker-page-two.png`, `seeker-search-keyboard.png`, `seeker-search-opened-wallet.png`, `seeker-search-scrim-closed.png`, `seeker-widget-map.png`, `seeker-widget-week.png`, `seeker-final.png`, accompanying QA snapshots and`device-checks.json`.

The UI remains a fictional OS rendered in Unity, using original assets and an Android system keyboard. It is not Apple's OS, font, symbols or native dynamic Liquid Glass implementation. No iOS build was applicable to this Android-only target.

