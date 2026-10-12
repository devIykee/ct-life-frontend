READY TO COMMIT: `client/Assets/Resources/UI/Customizer/Illustrations/` and meta, `UI/Icons/pencil-simple.png` and meta, `UI/asset-sources.json`, `art/LICENSES.md` — illustrated roster portraits and sourced edit icon.

# Illustrated character roster — 2026-10-11

Ten separate 2D ink/cel busts are saved in `client/Assets/Resources/UI/Customizer/Illustrations/`: `head_snow`, `head_rain`, `head_hero_assassin`, `head_hero_gynoid`, `head_hero_hazmat`, `head_hero_balaclava`, `head_ubc_m`, `head_ubc_f`, `head_pm_shark`, `head_pm_froggy` (PNG). Each is 1122×1402 RGBA with genuine transparency, no title, card, scenery or frame. Original Unity-rendered portraits and playable models are untouched.

The built-in image_gen tool generated these assets; no API key, CLI image model or programmatic illustration substitute was used. Each actual corresponding Unity portrait was viewed and supplied as the identity/clothing reference. Snow established the ink/cel style, the coordinator reviewed it, and its illustration was supplied as a style-only reference to the other identities. All ten generated finals were inspected; Dex received a targeted lower-edge cleanup pass. The images are stylized interpretations of the actual cast, not screenshots or new playable models. Source character credits and their derivative notices remain applicable.

Prompt set: clean hand-inked bust, crisp dark charcoal contours, restrained interior linework, flat cel colors and simple hard-edged shadows, natural adult proportions, relaxed folded arms, three-quarter facing slightly right, complete hair/head and shoulders, balanced 4:5 composition, transparent RGBA, no 3D shading, no frame, text or effects. Identity constraints:

| Identity | Preserved source features |
| --- | --- |
| Snow | Dark brown skin, tied black braids/bun, charcoal V-neck shirt |
| Rain | Brown skin, blue eyes, swept-back brown ponytail, white top and cyan scarf |
| Gas-mask runner | Pale blue-grey respirator helmet, red lens details, vivid orange technical jacket |
| Gynoid 2000 | Faceless ivory oval head, two antennae, segmented mechanical arms/hands |
| Hazmat operator | Black enclosed hood/respirator, grey-white protective suit and black harness |
| Balaclava streetwear | Bald exposed upper head, lower-face black mask, black jacket and dark red shirt |
| Dex | Tan skin, short swept-up brown hair, angular clean-shaven face, off-white T-shirt |
| Nova | Tan skin, side-parted dark bob, distinct adult face, light grey T-shirt |
| Shark Person | Wide hammerhead with both eye stalks intact, blue/white body, teeth/gill marks and yellow gloves |
| Froggy | Green spotted skin, large red eyes, cream throat/chest, black/yellow markings and orange frog hands |

Full individual prompts, raw generated-file locations and output hashes are in `../ct-life-evidence/character-illustrations-20261011/generated-records.json` and `asset-manifest.json`. The final source PNGs were copied without resizing or image-code edits. A read-only native pixel audit confirmed substantial fully transparent and visible pixels in every image (`alpha-audit.json`); all ten unique metas exist. The PNG set totals 10.89 MiB. Metas request 512px texture import; the coordinator owns the final importer override, UI integration and Seeker inspection.

The separate optional trial input `../ct-life-evidence/character-illustrations-20261011/synthetic-adult-face.png` is a text-generated fictional adult man, front-facing, short curly hair/beard and neutral grey backdrop. No real person's photo was used. It is external evidence only and was sent to the backend operator; it is not bundled in the game.

Edit icon: official [Phosphor pencil-simple bold SVG](https://raw.githubusercontent.com/phosphor-icons/core/2b75f3ad12b420c9504ef05df8d2564a28f8500e/assets/bold/pencil-simple-bold.svg), pinned to the repository's existing Phosphor commit, MIT licence. Rasterized as a flat white 128×128 transparent PNG using native macOS AppKit's SVG reader; no new dependency. Source hash `163a4e787655de6b5b50bfb0dc0e6818c015ba32106eb14fe12c3daf033a892b`, 339 bytes. `asset-sources.json` includes the source and `edit → pencil-simple` mapping. Root owns UiIcon wiring.

No Unity process, mobile build, paid model generation or provider key access was performed by this asset task.
