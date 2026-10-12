# CTOS: iPhone fidelity inside Solana Duo — 12 October 2026

Recommendation: reproduce the current iPhone visual hierarchy with **Inter Regular/Medium/Semibold, individually authored dimensional app icons, a photographic wallpaper, compact labels, and restrained translucent navigation**. Keep the approved Duo hardware. Exact SF Pro/SF Symbols redistribution is blocked by the licenses below; that does not require keeping the existing CTOS styling.

Scope: primary-source research and repository inspection only. This lane has not changed runtime code, imported fonts/icons, run Unity or used the device. The latest user image was not available as an image/file in this agent's context. Its described composition—two upper-left widgets, 4×4 icons, a right-side vertical dock, upper-right status and lower-right search—is the layout brief. Coordinates below are proposed implementation dimensions, not a claimed measurement of that attachment.

## Current Apple reference, not an older remembered iOS

Apple's live [iOS page](https://www.apple.com/os/ios/) and [iOS 27 user guide](https://support.apple.com/guide/iphone/whats-new-in-ios-27-iphfed2c4091/27/ios/27) identify **iOS 27**. Apple describes clearer contrast/refraction and sharper, more detailed app icons. The page's current images were downloaded and visually inspected:

- [iOS 27 icon detail](https://www.apple.com/v/os/g/images/shared/improvements/performance__dzd4tapk2suq_large.jpg): saturated blue/green fields, white and pale internal shapes, distinct foreground layers, soft internal depth and subtle rim highlights. Different apps have different compositions; they are not a uniform dark glyph pasted on differently colored tiles.
- [iOS 27 control detail](https://www.apple.com/v/os/g/images/shared/improvements/design__b82s0uhegkmu_large.jpg): one neutral glass capsule over photographic content, thin black line symbols, strong contrast and a light perimeter. Navigation is visually separate from the artwork behind it.
- [Apple's iOS 26 home-screen comparison](https://www.apple.com/newsroom/images/2025/06/apple-elevates-the-iphone-experience-with-ios-26/article/Apple-WWDC25-iOS-26-Home-Screen-customization-250609_big.jpg.large.jpg), explicitly historical: two widgets above four columns of icons, small labels, small search pill, four dock icons without labels. The rightmost default/light example is the relevant color treatment, not the all-clear example. This is not presented as an iOS 27 full home-screen screenshot.

Reference-only files live outside the repo at `../ct-life-evidence/ctos-iphone-20261012/references/`. They are not shipping assets. Apple's current [Home Screen guide](https://support.apple.com/guide/iphone/customize-apps-and-widgets-on-the-home-screen-iph385473442/ios) supports several appearances; the implementation should deliberately choose the colored/default appearance that matches the user's brief.

## Font and icon licensing: concrete limits

**SF Pro / San Francisco:** Apple's currently published [font agreement](https://developer.apple.com/fonts/), §2B, says: “You may not embed the Apple Font in any software programs or other products.” The same section excludes mockups intended for non-Apple operating systems. The page carries agreement EA1370 dated 2016; it remains the agreement published at that URL on this research date. Apple's [current font documentation](https://developer.apple.com/documentation/technologyoverviews/fonts) also says not to bundle its distributed system fonts. An Android game containing a fictional phone does not become Apple-platform software. Do not copy a locally installed SF file, rasterize SF labels into a shipped atlas, or use an unofficial rehosting package as a workaround.

**SF Symbols / Apple system artwork:** the current [Xcode and Apple SDKs Agreement](https://www.apple.com/legal/sla/docs/xcode.pdf), **§2.10 System-Provided Images**, restricts these assets to developing applications for the applicable Apple-branded products. It also restricts their use in app icons/logos. The [SF Symbols HIG](https://developer.apple.com/design/human-interface-guidelines/sf-symbols) calls out the icon/logo restriction. The search engine returned an older §2.11 excerpt; the current opened PDF is §2.10. The standalone SF Symbols installer agreement was not downloaded/extracted; no installed SF Symbols app was present. The opened primary agreements already establish that exporting Apple's symbols into this APK is not an authorized asset source.

**Ship Inter instead:** [Inter's official site](https://rsms.me/inter/) and [latest release](https://github.com/rsms/inter/releases/tag/v4.1) provide the current stable **4.1**, published **2024-11-16**. The repository API reported `archived=false` and last push `2024-11-19`; do not claim recent development. Its [SIL OFL 1.1](https://raw.githubusercontent.com/rsms/inter/master/LICENSE.txt) allows software embedding/distribution and modification with the copyright/license retained, subject to its conditions. Use static 400/500/600 assets generated from that source or the release statics; retain provenance and OFL. This is a visual recommendation, not a claim that Inter is metrically identical to SF Pro. The font lane is handling the actual import.

For small in-app glyphs, the existing Phosphor family has an [MIT license](https://github.com/phosphor-icons/core/blob/main/LICENSE). Prefer its regular stroke weight, or original drawn equivalents, with consistent optical size. For launcher icons, create original CT Life artwork with the layered construction described below. Do not ship Apple app images or trace SF Symbol contours.

## What the current repo actually does

`phone-ui.uss` currently uses Manrope Semibold for clock, device label, launcher labels, titles and buttons. Launcher icons are 88 units, with 26-unit semibold labels: label/icon ratio **0.295**. Icons are flat CT palette backgrounds and 48-unit dark Phosphor glyphs. The shell has a 124-unit app bar, a horizontal dock, a large page-indicator band and the generated wallpaper. These choices are implemented consistently, but their combined typography, icon construction and spacing do not closely resemble the inspected iPhone references.

Change typography only within `.ct-phone`. Preserve Manrope/Lilita elsewhere. The exact new font references should override inherited headings, inputs, placeholders, navigation buttons, list text, wallet amounts and secondary labels, not only the launcher.

## Proposed layout for the supplied fold-leaf composition

Let **W×H be the visible interior glass**, excluding the approved hardware frame. Use this as the implementation starting point; tune from actual Seeker captures. An 800×1000 example is provided to remove ambiguity.

| Element | Proposed geometry | Example at 800×1000 |
|---|---|---|
| Photograph | Full bleed, no flat color margin; darker quiet areas behind labels | 800×1000 |
| Status | Compact group at upper-right; baseline around .035H | baseline35, right28 |
| Two widgets | Side .2125W; first x=.075W, second x=.3275W; y=.105H | 170×170 at (60,105), (262,105) |
| App grid | Four columns; centers x=.13/.30/.47/.64W | 104,240,376,512 |
| Four rows | Icon centers y=.37/.515/.66/.805H | 370,515,660,805 |
| App art | Side .11W; a continuous-corner mask; no outer plate | 88×88 |
| App label | One centered line, 6–8 units below art; full column width | 20–21 units, width128 |
| Vertical dock | x=.805W, y=.32H, width=.14W, height=.58H | (644,320),112×580 |
| Dock contents | Four icons, no visible labels; center y=.41/.54/.67/.80H | centers410,540,670,800 |
| Search | Small pill near lower-right, clear of dock; separate real hit area | visual156×40 near (596,930) |

Use the actual app catalog; preserve all routes across pages. Do not repeat dock apps in the grid. Keep a fixed widget/dock frame while only the app page changes. If the search pill is present, it must open app search; do not paint a nonfunctional search ornament. Keep the existing accessible hit-area requirements even when the visible icon/label becomes smaller.

Widgets must use real CT Life data: a time/date widget plus actual needs or Cash is a useful starting point. Do not fabricate weather, pending messages, token prices or returns. The right-hand dock is the user's fold-leaf composition, not a claim about stock portrait iPhone placement.

## Typography specification

Apple's [Typography HIG](https://developer.apple.com/design/human-interface-guidelines/typography) lists 17pt default and 11pt minimum for iOS/iPadOS and recommends avoiding thin weights. Those are platform points, not raw Unity units. The following are **CTOS implementation values for the present approximately 800-unit interior**, to be verified on Seeker:

| Role | Proposed Inter style | Unity units |
|---|---|---:|
| Launcher app labels | Regular400; Medium500 only if contrast needs it | 20–21 |
| Status clock | Semibold600, neutral tracking | 24 |
| Widget metadata | Regular400 / Medium500 | 20–22 |
| Widget primary number | Regular400 | 62–72 |
| App large heading | Semibold600 | 54–60 |
| Compact app navigation title | Semibold600 | 32 |
| App body / input / action label | Regular400, Medium500 for emphasis | 30–32 |
| Secondary app text | Regular400 | 24–26 |

No text outline, uppercase treatment, wide tracking or semibold on every line. Use single-line ellipsis for truly long launcher names. Preserve full names in tooltips/accessibility and in the launched app. Measure rendered glyphs with the imported font; do not just reduce nominal sizes. Apple reference label/icon ratio is approximately 0.20 by visual comparison, so 26-unit labels should not accompany 88-unit art.

## Icon, glass and app-chrome specification

Design proposals, not sampled Apple color constants:

- **Launcher:** 512×512 original masters, rasterized to 128/256 as appropriate; continuous-corner shape approximating radius22 at an88-unit display size. Each icon gets a simple luminous background, a distinctive 2–3-color foreground composition, a restrained top-light edge and internal contact shadow. Main motif should occupy roughly60–72% of the icon. App families can share a palette but should not all share the same dark center glyph.
- **Palette:** neutral ink `#111318`, white `#FFFFFF`, secondary gray `#6D727A`, blue `#1687FF`, green `#32D85B`, warm orange `#FF9F0A`, violet `#7A5AF8`, coral `#FF5A62`. These are proposed phone tokens, not replacements for the game's global palette. Keep richer icon colors separate from neutral app backgrounds.
- **Wallpaper:** photographic desert/dune depth matching the user's brief. Preserve grain, light, sky and horizon rather than replacing the scene with large smooth vector blobs. Source a redistributable photograph or generate original imagery; do not export Apple's wallpaper.
- **Dock/search:** a translucent neutral capsule with local wallpaper blur, a thin low-opacity white rim and a quiet shadow. Avoid a saturated purple slab, thick outline or a second plate beneath every icon. Apple recommends [using Liquid Glass sparingly for navigation and controls](https://developer.apple.com/design/human-interface-guidelines/materials), with standard surfaces for content. In Unity, a prefiltered wallpaper patch is a reasonable visual approximation; do not call it Apple's actual dynamic material.
- **Inside apps:** bright white or very light gray content, neutral dividers, smaller compact navigation, and a single colored action tint. Back is a small directional control; close is distinct. Preserve existing navigation and safe touch areas. Avoid recreating a full desktop card system inside the phone.
- **Motion:** proposed160–220ms page movement; small press scale around0.97, fast release, and a short app-open transition. No continuous shimmer or bouncing icons. Respect reduced motion if supported; animation must never steal a swipe or launch an app on pointer release after paging.

Acceptance should compare the actual device at the same apparent interior size as the supplied reference: wallpaper detail, icon silhouette/construction, label ratio/weight, widget proportion, dock placement and app chrome. The previous passing CTOS interaction tests remain necessary but do not establish visual fidelity.
