# Solana Duo — phone shell and wallet detail

12 October 2026. Implemented by the phone UI lane; runtime capture and device validation belong to the coordinating agent.

## Direction grounded in the references

The requested name is **Solana Duo**. This is CT Life's fictional in-game device, not a claim about a released Apple or Solana Mobile product.

- [Microsoft: different ways to use Surface Duo](https://support.microsoft.com/en-us/surface/different-ways-to-use-surface-duo) documents folding one screen backwards for single-screen use. Its actual [single-screen product image](https://support.microsoft.com/en-us/surface/media/surface-duo-2-phone-ui.jpg) was downloaded and visually inspected. The broad portrait panel, straighter hinged edge and two small hinge pieces are the physical reference. This is a historical industrial-design reference, not a current buying recommendation.
- [Google: Pixel 10 Pro Fold hinge design, 9 October 2025](https://blog.google/products-and-platforms/devices/pixel/pixel-10-pro-fold-gearless-hinge-design/) explains reducing the hinge's protrusion, polished aluminum and fine chamfered edges. The article's actual product/design photograph was inspected. We borrow restrained edge highlights and a narrow rim, not product branding or specifications.
- [Nintendo: Animal Crossing New Horizons](https://www.nintendo.com/en-gb/Games/Nintendo-Switch-games/Animal-Crossing-New-Horizons-1438623.html), section “Your life in the palm of your hand,” describes NookPhone apps for daily life. The official embedded video's poster and the [official recipe-detail screenshot](https://www.nintendo.com/eu/media/images/08_content_images/games_6/nintendo_switch_7/nswitch_animalcrossingnewhorizons/NSwitch_AnimalCrossingNewHorizons_Islandlife_DIY_Slider_Scr01.jpg) were visually inspected. They show a cheerful device integrated into the world and concise, contained task UI. The three-column CT Life launcher is our layout decision; the inspected poster does not show the NookPhone launcher.

The US Animal Crossing explore page and Google Store page failed to fetch through web tools. The official pages and images above were accessible. No third-party reference imagery ships in the game.

Local evidence: `ct-life-evidence/phone-20261012/{surface-duo.jpg,pixel-hinge.webp,nookphone.jpg,nookphone-recipes.jpg}` (outside the source repository).

## Applied design

Design decision: render one broad half of the device at the right edge of landscape play, preserving city context. The previous phone was a 980-unit wide sheet. The new shell is 832 units wide, inset 24 vertically, with an asymmetric hinge edge, slim dark rim, white glass and existing CT Life token colors. The launcher stays three columns. Existing app names, IDs, phase gates and unavailable states stay connected to the same catalog.

The status strip shows the actual local clock; decorative camera and device stripes do not purport to report battery, network or wallet state. There is no simulated system status. Back, close, app tiles and the home indicator are real native controls with 124-unit minimum interactive height. The entrance uses existing reduced-motion-aware motion helpers. Closing cancels animation and the clock schedule; the previous HUD visibility restoration is preserved.

Shared app chrome uses the existing Manrope typography, white cards, consistent line weights and the game's accent button. Feature-owned content and app service wiring are unchanged apart from registration of the requested Wallet app.

## Real wallet detail

The `wallet` app reads `WalletBalanceService` and remains distinct from fictional Cash in Bank. It renders SOL, USDC, SKR and ORE using on-chain precision supplied by the service. A null observation displays “Unavailable” or “Loading…”; only an observed zero displays `0`. Previously observed stale amounts remain visibly labeled. It shows the service's cluster, supports read-only refresh and never signs, sends, prices or totals holdings.

`Closed()` removes the page's subscription and scheduled refresh. It does not dispose the shared service. Copy lives in `WalletStrings.cs`; `phone.title` is supplied by the coordinator's Strings update.

## Validation

- Source review: preserved phase/degraded gating, app `Closed()` callbacks, outside-close and HUD restoration, disposal, and UI native button semantics.
- All four actual `WalletApp` EditMode tests passed: confirmed zero vs missing observation; one-lamport precision; disconnected page; close unsubscribes while shared service remains usable. The coordinator’s final-editmode.xml records 31/31 passed overall.
- Native Unity capture receipt `ct-life-evidence/lobby-duo-20261012/lobby-final/receipt.json` records pass=true, 28 screenshots, zero failures/runtime errors, restored services and no REST writes. This exercised actual native UI with isolated read-only profile/REST/RPC fixtures, not a live backend or real wallet. Captures cover launcher, Map, Jobs, Bank, Profile, Home, Settings, Wallet state cases, long names, large amounts and phone close.
- Android build, Seeker installation and scoped touch verification are recorded in `docs/progress/lobby-solana-duo-20261012.md`. The device session is a developer login; the player’s real wallet holdings were not verified. Public RPC production reliability remains a separate infrastructure concern.

## Pass 3 visual refinement

The runtime capture showed the greeting, subtitle and app-section label consuming the space needed for the third launcher row. Removed those elements: home starts directly with apps. Wallet no longer repeats a Your tokens title and an always-present balance-description line. The summary keeps network/address, while live error/loading states remain visible. Phone-contained Map drops its repeated intro/header while preserving the real available-place count and standalone map presentation. Matching app/nav titles are suppressed within the shell only. Jobs action width and selected-tab style, and Bank hero spacing, were refined from the actual captures. Final native screenshots were inspected: nine launcher apps are fully visible, the Jobs selected tab is clear, Details stays on one line, and large Wallet amounts preserve precision without clipping.

## CTOS Home Screen revision — 12 October 2026

The user approved the Duo hardware, but said the software inside did not feel like iPhone OS. This revision supersedes the three-column launcher decision above; the hardware and feature app content remain intact.

### Primary sources opened and images inspected

- [Apple: Open apps on iPhone](https://support.apple.com/guide/iphone/open-apps-iphca3d8b4e3/ios). The current page served the iOS 27 guide when checked. Its [actual Home Screen image](https://ipcdn-web.apple.com/assets/v2/web/46b4bf23-5982-4e56-ac3e-efaeed6f1c4d) shows four app columns, small centered labels, a persistent four-icon dock, wallpaper and minimal status hierarchy. The instructions describe horizontal page swipes and returning Home with a bottom-edge gesture.
- [Apple: Customize apps and widgets on the Home Screen](https://support.apple.com/guide/iphone/customize-apps-and-widgets-on-the-home-screen-iph385473442/ios), including its [actual appearance comparison](https://ipcdn-web.apple.com/assets/v2/web/7691fb51-24a7-4523-8e68-994729bb243b). Observed consistent icon silhouettes, subdued translucent surfaces and wallpaper-led presentation.
- [Apple: Move apps and widgets on the Home Screen](https://support.apple.com/guide/iphone/move-apps-and-widgets-on-the-home-screen-iphd2fc8ce30/ios), including its [actual Home Screen image](https://ipcdn-web.apple.com/assets/v2/web/6b4ed969-c013-4b3b-b7bf-3f3fac1da86e). Observed page dots and a dock separate from the horizontally moving app pages.

Inspected local evidence is outside the repository: `ct-life-evidence/ctos-20261012/{home.png,customize.png,move.png}`. No Apple artwork, icons or wallpaper are bundled in CT Life.

### Applied interpretation

- A static original pearl, lavender and mint wallpaper drawn with UI Toolkit's Painter2D, using existing CT Life theme colors. It has no frame update, image download, texture asset, shader or dependency. This is an original visual interpretation of the reference's sweeping light forms, not an Apple wallpaper copy.
- Four columns, four rows per page. All registered apps appear exactly once; Map, Wallet, Messages and Spaces live in the persistent dock. Live-phase grid apps precede later-phase ones. Planned or temporarily unavailable apps retain their visible state and the same runtime gate.
- Home has no app-style title bar. Solana Duo identification is small in the existing status strip; the clock is real local time. Feature apps retain their existing back/title/close bar and content.
- Page dots are real buttons with localized page-number tooltips and 124-unit targets. Horizontal swipes slide between pages; short swipes settle on the current page. Vertical drags do not turn a page or launch an app. Taking capture from a child app button suppresses its release action. Left/right/Home keys support desktop navigation; nonvisible page buttons are removed from the tab order. Motion uses the existing reduced-motion setting.
- The bottom home control returns from an app to its previous page, returns a later Home page to the first page, then closes the device when already at its first Home page. Its tooltip reflects the current action. The outside scrim and app close control remain functional.
- No simulated battery/network readouts, fake Search, dead widget or app-reordering controls were added. Existing catalog route IDs, phase/degraded gating, app disposal and real Wallet service are unchanged.

### Implementation sources and validation scope

Checked the installed Unity 6000.3.25f1 UIElements XML and current primary documentation: [pointer events](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Pointer-Events.html), [Painter2D](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.Painter2D.html), and [CustomStyleResolvedEvent](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.CustomStyleResolvedEvent.html). Pointer release events can lack position data, so the pager uses the last move position. Child capture-loss bubbling must not be mistaken for cancellation of the viewport's own capture.

Seven `PhoneLauncherTests` exercise the actual PhoneShell and launcher: all-route uniqueness/page allocation, page-dot and app/back lifecycle, dock launch and home/close behavior, future-phase gate, horizontal swipe suppression and the next fresh tap, vertical drag rejection, and whitespace capture/release outside the viewport. The corrected native test run passed all seven cases within 66/66 targeted tests; native visual review also passed as recorded below; Seeker acceptance subsequently passed as recorded below; earlier 31/31 tests and 28 screenshots above apply to the previous launcher.

### Native input failure found and addressed in source

The first CTOS test run compiled and passed 64 of 65 targeted tests. Five of six launcher cases passed; horizontal swipe expected page 1 and stayed on page 0. This was a production event-routing error: the native Button captured the pointer, and Unity skips ancestor callbacks for captured pointer events. Confirmed in [Unity 6.3 event handling documentation](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Events-Handling.html) and [Unity's reference dispatcher](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/UIElements/Core/Events/EventDispatchUtilities.cs). Grid Buttons now directly observe captured Move/Up/Cancel before native Clickable, taking viewport capture only after the drag threshold. Dock Buttons retain their normal behavior.

An independent read-only review also found that blank page presses could lose their release outside the viewport. Those presses now capture immediately; a seventh actual-panel test verifies outside release and close cleanup. Existing swipe assertions were retained and strengthened. The corrected rerun passed 66/66, including all seven launcher tests (`ct-life-evidence/hud-ctos-20261012/editmode-final.xml`). Native visual acceptance is recorded below; Seeker acceptance subsequently passed as recorded below. The initial failure evidence is `ct-life-evidence/hud-ctos-20261012/editmode.xml`.

### Final native visual and material verification

Inspected `ct-life-evidence/hud-ctos-20261012/native-final/hud-06-ctos-open.png` and `hud-06b-ctos-page-two.png`: the original lavender/mint wallpaper is visible, the translucent white dock separates from it, all 16 first-page labels and nine second-page labels fit. The first capture had rendered white; binding element-local custom-style aliases to the existing theme tokens made the painter receive its colors. No new palette values were introduced. The existing catalog test now checks material visibility, positive geometry and a resolved nonwhite theme color.

`phone-material.xml` passed 7/7 launcher tests after that binding change. The final combined native capture receipt reports pass=true, 23 shots, zero runtime errors and no REST writes. The capture uses the actual city/HUD/phone with isolated in-memory profile and synthetic prompt states; it is not a live backend or real-wallet session. App page, page return, Wallet unavailable and close captures remain present. Native acceptance is complete; subsequent Seeker touch/build acceptance is recorded below.

### Final device acceptance

The coordinator tested the installed 227 MiB Android build on Seeker, preserving the existing `joel` / Gynoid player. A horizontal swipe beginning on Business turned the page without launching an app; a vertical Settings drag did not launch or change page. The Wallet dock shortcut opened the actual disconnected Wallet state. Home returned to the previous page, the first-page dot navigated, and Home closed the phone. After the final HUD rebuild, swipe/page-one/Home-close checks passed again; opening real Map from the HUD and closing it restored the HUD. Phone source was unchanged after the tested wallpaper correction.

This lane inspected `seeker-final-ctos-page2.png` and `seeker-wallet-from-dock.png` and directly checked `final-tests.xml` (66/66 passed) plus `native-stress/receipt.json` (24 shots, PASS, zero failures/runtime errors). Evidence root: `ct-life-evidence/hud-ctos-20261012/`. Final installed build SHA-256 recorded by the coordinator: `cc3f704e8ad5e2d8b5e79dd3d1f890867aae86caf19f49a1b1892baeff6ea87a`. This completes scoped native/device acceptance; disconnected Wallet presentation is not a funded-wallet RPC test.
