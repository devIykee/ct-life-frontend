# Presentation review — 5 October 2026

**Superseded visual direction: the user subsequently rejected the mineral UI and buttons.** The new tactile game/lobby implementation is documented in [game-ui-direction-20261005.md](game-ui-direction-20261005.md); build K device review is pending. The historical I checks below establish technical behavior only.

**Historical status: I UI inspected; J character-idle review pending.** The user rejected the earlier model quality, UI, typography, icons and sound, and requested visual research before another redesign. The changes below are not user approval or a completed presentation. F/G are failed iteration captures, retained as evidence.

## References actually viewed

The images were inspected before implementation; observations and local research files are documented in [the visual research](ui-visual-research.md).

- [inZOI creator on Pinterest](https://www.pinterest.com/pin/127508233194989371/): large adult character, quiet studio, peripheral appearance controls. The pin is too small for exact typography claims.
- [Betterfly on Pinterest](https://www.pinterest.com/pin/1548181186834509/) / [Further's original project](https://further.group/work/betterfly): shallow currency strip, dimensional central artwork and one concentrated accent.
- [NookPhone on Pinterest](https://www.pinterest.com/pin/440015826099142901/): recognizable app silhouettes, consistent 3×3 grid and visible world around the phone.
- [Life Makeover publisher screenshots](https://store.steampowered.com/app/2626940/Life_Makeover/): large face inspection with real visual samples. The inspected image is landscape, not evidence of portrait-phone usability.
- [The Sims 4 official phone screenshot](https://www.ea.com/fr/games/the-sims/the-sims-4/news/laundry-list-june-23-2026): app choices separated from phone utilities, with consistent object-based icons.

Reference images remain outside the shipped assets. The mineral/ink/lime palette is a design recommendation, not a copied reference or an approved user palette.

## Concrete changes

- Shared mineral/offwhite surfaces, deep ink/aubergine text and lime selection/primary states; moderate headings and centralized visible copy.
- Compact cash/location/self-care HUD and separate lower-corner controls replace the full-width bottom navigation. Existing chat, emote, microphone, map and phone actions remain.
- Creator separates Look, Personality and Identity. The real character has a large stage, face/body inspection and drag rotation. Alias, age, tribe, two traits and dream remain.
- Appearance choices require an exact authored seven-ID prefab mapping. **Everyday** is one complete look; fictitious individual hairstyle/clothing options were removed. Live server confirmation is checked again before submission. Read-only review never creates a character.
- Sign-in uses a real authored portrait, shorter introduction and separate bottom wallet/help/age actions. Full-screen backing covers unsafe-area gaps while controls retain their insets.
- Phone uses a consistent three-column icon grid and preserves availability gates. H shows the overlapping HUD absent while the phone is open; the implementation restores its prior display state on close.
- Phone transitions use sourced open/close cues without an extra generic tap. Settings exposes offline credits and full license notices.

## Bundled assets and licenses

- **Manrope:** official static Regular/Semibold TTFs, unmodified; OFL 1.1. Source commit `468c0dbe38efa331b80bfe9448256abe27be44c3`.
- **Phosphor regular:** official SVG geometry rasterized offline into 192px transparent PNGs; MIT. Source commit `2b75f3ad12b420c9504ef05df8d2564a28f8500e`. The bundle contains 35 rasters; 36 semantic keys currently use 34 distinct glyphs. No runtime icon package.
- **Snow:** “Snow Rig (CC) Blender Foundation | studio.blender.org”; CC-BY 4.0. The shipped notice records modified adult proportions, rig/weights, materials and atlas conversion. This one adapted candidate does not establish cast approval.

Exact URLs, mappings, hashes and reproduction steps are in [the bundled notices](../../client/Assets/Resources/UI/THIRD-PARTY-NOTICES.md) and [source manifest](../../client/Assets/Resources/UI/asset-sources.json). Full notices ship under `Resources/UI/Fonts`, `Icons` and `Licenses`.

## Evidence and unfinished work

- **F failed:** raised-arm/crossed-leg model pose, dark/undersized preview and unsafe-area world strip.
- **G failed:** neutral pose and full-body framing improved, but the character initially faced backward and preview edges were undersampled. The actual gesture-turned front capture was inspected; it does not validate the pending orientation/resolution build.
- Root owns final rendering, import, build and device evidence. The H/I observations below do not establish J acceptance. Neither reference renders nor successful compilation substitute for device evidence.
- Full modular customization, multiple body/skin/hair/clothing choices and the wider adult cast remain required. One exact look is a quality checkpoint, not a reduction of the agreed product.
- Sound remains unfinished: current music/babble are synthesized, ambience selection was not auditioned, and the combined mix is not listening-approved. Runtime crossfades, gains, listener/footstep handling and phone cues do not complete composition, performance or sound design. See [audio evidence and remaining content work](audio-content-audit.md).
- `PhoneCatalog` still uses upcoming placeholders for Phase 2 Pulse/Perps/Predict/ORE/Top up; Phase 3 Timeline/Messages/Spaces/Calls; Phase 4 Shop; Phase 5 Ads; Phase 6 League. Their tiles do not imply functioning markets, staking, payments or calls. Current phase gates remain; all six phases, ORE and SKR remain in scope. See [ORE/SKR client contract audit](ore-skr-client-audit.md).

## H device evidence

- Viewed the actual H [creator](screens/quality-review-20261005h/quality-review-20261005h/creator.png), [sign-in](screens/quality-review-20261005h/quality-review-20261005h/signin.png) and [phone](screens/quality-review-20261005h/quality-review-20261005h/phone.png). The complete front-facing character fits the creator stage; sign-in shows the face and wallet action; phone has no partly covered HUD controls behind it. No further blocking layout issue was identified in these static captures. This is a visual assessment, not customization, authentication or touch-behavior acceptance.
- H Settings (`/tmp/ct-settings-h.png`, provided by root) still had Unity's tiny default slider handle and a duplicate Settings heading. The follow-up removes only that inner title, keeps labels/credits, and styles the native sliders with 124-unit track input areas, rounded track/knob and fill driven by the actual value. Existing audio callbacks remain.
- `Slider.fill` and the shared USS selectors were checked against installed Unity 6000.3.25f1 API documentation and the [official 6.3 Slider reference](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-uxml-element-Slider.html). Static diff checks pass; I evidence follows below.
- The H [device log](screens/quality-review-20261005h/device.log) records movement/facing, five-map lightmap lifecycle and venue round-trip passes. A settled audio snapshot has one music bank, one ambience source and one listener; the live-SFX mute assertion passes. These are recorded runtime checks, not listening approval, sustained performance acceptance or proof of live backend flows.

## I device evidence and remaining J check

- The saved [build receipt](screens/quality-review-20261005i/build.json) records 171 MiB, zero build errors and 29 warnings. All eight [main captures](screens/quality-review-20261005i/quality-review-20261005i/) were inspected: creator, sign-in, city, two movement views, phone, map and bank. No additional blocking UI overflow, overlap or clipping was identified. The creator's wide stance and curled hands remain an animation-quality issue; J's separate Snow-native idle is pending, not approved by these images.
- Inspected the complete Settings touch sequence: [initial maximum](screens/quality-review-20261005i/touch/settings-initial.png) → [minimum](screens/quality-review-20261005i/touch/sfx-zero.png) → [near midpoint](screens/quality-review-20261005i/touch/sfx-middle.png) → [Settings after process relaunch](screens/quality-review-20261005i/touch/sfx-reopened.png) → [original maximum restored](screens/quality-review-20261005i/touch/sfx-restored.png). Fill and handle align at each captured position. Root recorded Android `am force-stop com.ctlife.app` through ADB, a fresh `am start` to the review phone, a 12-second wait, then Settings opened and captured. The near-midpoint position survives that process relaunch. Root restored SFX to the exact 1 endpoint and left Music at 0.7. This validates the tested persistence path, not audible volume quality.
- Creator touch evidence shows [back rotation](screens/quality-review-20261005i/touch/creator-back.png), [front restored](screens/quality-review-20261005i/touch/creator-front.png), [face focus](screens/quality-review-20261005i/touch/creator-face.png), [face after Android Home/resume](screens/quality-review-20261005i/touch/creator-face-resumed.png) and [full body restored](screens/quality-review-20261005i/touch/creator-body-restored.png). The face remains rendered and selected after resume; the body control returns to full-body framing. The [resume receipt](screens/quality-review-20261005i/touch/resume.txt) records the same PID 31634 before/after. This is one same-process background/resume check, separate from the Settings process-relaunch test.
- Settings now has one title, readable labels and intact credits/version/sign-out controls. No UI code changed during this review. Audio still requires listening, and the read-only sample profile does not validate backend transactions or authentication.
- **J remains pending:** inspect the new idle, fingers and stance on the actual device after the targeted character import. No user approval or full presentation acceptance is recorded here.
