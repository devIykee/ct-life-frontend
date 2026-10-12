# CTOS original layered app icons — 12 October 2026

The user rejected the previous single-color Phosphor glyphs inside pastel tiles as insufficiently faithful to current iPhone OS icon presentation. This lane replaces only launcher artwork; app routes, gameplay controls and shared UiIcon assets remain untouched.

## Current primary reference, viewed before drawing

Opened Apple's current [Icon Composer page](https://developer.apple.com/icon-composer/) and visually inspected its [actual app-icon grid](https://developer.apple.com/icon-composer/images/vx-poster-design_2x.png). The page now describes sharper specular treatment, refraction and updated rendering. The inspected grid shows localized shadows, separate object layers, fine light rims, gradients, varied colored or neutral bases, and more visual information than a flat glyph.

Read Apple's [WWDC26 Icon Composer beginners lab](https://developer.apple.com/videos/play/wwdc2026/8012/) transcript, particularly the 2026 rendering discussion and the small-size review guidance. Applied its practical principles: simple distinct geometry, deliberate overlap and material edges, restrained complexity, and evaluation at small size. CT Life is rendering flattened original artwork in Unity; these images do not implement Apple's dynamic Liquid Glass renderer or refraction.

The HIG page's web extraction required JavaScript; the accessible official Composer page, full WWDC26 transcript and actual current image supplied the reference evidence. No third-party advice is the basis for this work. Apple imagery remains outside source in `ct-life-evidence/ctos-icons-20261012/` and is not shipped.

## Original artwork and mapping

Authored using `art/source/phoneos-icons/render-icons.swift`, native CoreGraphics and geometric paths drawn in the script. No external icon pack, downloaded glyph, SF Symbol or Apple app asset appears in the runtime textures. A shared continuous-corner enclosure, top lighting, small object shadows and fine rim highlights tie the set together. Icon color is part of the authored image rather than an arbitrary runtime container tint.

| Route | Authored visual metaphor |
|---|---|
| map | colored street blocks and raised blue location pin |
| jobs | white case over a blue translucent document |
| bank | pale stone bank and gold coin |
| wallet | dark enclosure, stacked cards and folded brown wallet |
| richlist | gold ascending podium and crown |
| home | nested warm house layers on porcelain white |
| profile | raised profile card and blue portrait medallion |
| settings | concentric metal gears and dark inset bearing |
| pulse | rose heart with white pulse line |
| perps | red/green candle silhouettes on a dark graph field |
| predict | overlapping alternative cards, check and cross |
| ore | faceted amber ore on dark stone |
| topup | mint card and raised plus coin |
| timeline | layered news card with a warm image block |
| messages | overlapping glass-like chat bubbles on green |
| spaces | concentric violet broadcast rings and pale microphone |
| calls | dimensional white handset on green |
| shop | coral bag and defined raised handle |
| week | calendar sheet with week dots and completion mark; no invented date |
| biz | layered buildings with distinct window planes |
| ads | pale megaphone and separated sound strokes |
| builder | warm launch field, pale rocket, fins and gold flame |
| vc | investment document, upward funding arrow and coin |
| league | gold trophy, separate handles and inset star |
| beef | two opposing colored conversation bubbles |
| farm | green seedling growing above a coin |
| cabal | three differently colored people within a raised ring |
| npcchat | portrait within a cyan conversation card |
| xlink | overlapping metallic chain links |

`app` is a neutral four-tile fallback for unregistered/future/test routes. `rich_list` is an alias of the existing `richlist` artwork. None of the abstract motif marks represent a live game statistic, date or balance.

## Delivery and verification

- `PhoneAppIcon(string appId)` in `CTLife.UI`, native VisualElement, `PickingMode.Ignore`, class `ct-phone-app-art`. Width/height are owned by the coordinator's phone styles. `HasDedicatedArtwork(id)` identifies covered routes.
- Thirty 192×192 RGBA PNGs (29 apps plus fallback), total **794,745 source bytes**, under `Resources/UI/PhoneOS/Icons`. Alpha outside the enclosure; bilinear filtering; no mipmaps; Android ASTC 4×4, max 256. No new runtime dependency or per-frame artwork renderer.
- Visually inspected the complete large contact sheet, removed an overly obvious surface highlight band, corrected the Shop handle contrast, and inspected the regenerated complete set. Small-size light/dark review sheets are rendered separately at actual 64px size.
- Source PNG dimensions/RGBA headers and 32-character metadata GUIDs checked. Per-file SHA-256/size evidence: `ct-life-evidence/ctos-icons-20261012/asset-manifest.json`.
- Contact sheets: `ct-life-evidence/ctos-icons-20261012/rendered/contact-sheet.png`, `contact-sheet-64px-light.png`, `contact-sheet-64px-dark.png`.
- Runtime integration, native screenshots and Seeker acceptance belong to the coordinator and remain pending for this new icon revision. Earlier CTOS device acceptance did not include these textures. No Unity, ADB or Git operation performed by this lane.
