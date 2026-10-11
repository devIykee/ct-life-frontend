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
