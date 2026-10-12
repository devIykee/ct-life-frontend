# Illustrated landscape card surfaces — 2026-10-12

The user rejected the prior player and origin layouts and requested an image-generated card base populated with code. The new visual reference is the [Oscar game profile card on Pinterest](https://www.pinterest.com/pin/1900024839881546/), inspected by the coordinator in the browser and by the art agent from `/Users/mac/Downloads/f66ae92ca1e41453cd7c48aa4b7d5022.jpg`.

The reference has an illustrated card boundary, a checkerboard character field, a small top tab, a prominent character, and ordered information rows contained within the same card. The project adaptation uses a **2:1 landscape** composition, existing mint/lavender/plum colours and a bright white information field. No text or game values are baked into the generated surface.

## Player surface

Accepted generated base, inspected directly:

`/Users/mac/.codex/generated_images/01a11126-ce0a-7a03-8d49-7d2f1b9dc9f5/exec-1331054b-c84f-4011-90fb-dc9e95f74248.png`

Project copy: `client/Assets/Resources/UI/Onboarding/player-card-surface.png` (integrated by the coordinator).

It has a narrow rounded purple/plum rim, a mint/lavender checkerboard field on the left, a diagonal boundary, a small skyline detail, a blank mint upper-left tab, and two fine horizontal separators at approximately 54% and 75% of card height. The right information field is otherwise blank. The actual chosen 3D avatar, alias, tribe, dream and traits are populated by code.

The first output (`exec-8b380c4d-61c4-4820-928f-92447bde854d.png`, same generated-image directory) had unwanted alpha holes in its white regions and was rejected after direct coordinator inspection. The accepted second generation is opaque. The art agent inspected the accepted base only.

The accepted edit used built-in `image_gen.imagegen`, with the rejected output as its local image reference and `transparent_background: false`. Exact edit prompt:

> Edit the attached card into a usable finished game UI asset. Preserve the 2:1 composition, left mint/lavender checker art area, curve/angle separating left and right, mint upper-left tab, city linework and two fine separators. CRITICAL: the entire right text surface must be SOLID PURE WHITE #FFFFFF, fully OPAQUE, evenly flat. There must be no transparent hole, grain, gray shading, mottling or gray gradient anywhere inside the card. Fill all those damaged right zones with uninterrupted pure white; only keep the two thin horizontal lines. The overall canvas is opaque, including white outside corners. Simplify the outer rim to a clean narrow rich plum stroke and narrow lavender inset stroke, remove the heavy doubled 3D bevel and all glow. Keep tiny mint bottom-left corner accent. No text or characters. Rounded rectangle edge nearly touches canvas all sides. Straight-on landscape asset. Clean, bright printed collectible card with subtle illustrated left area, calm white text area.

## Origin surfaces

Three independent built-in `image_gen.imagegen` calls used the accepted base as the strict geometry/style reference and the corresponding existing origin painting as the character reference. Each request used `transparent_background: false`.

| Project surface in `client/Assets/Resources/UI/Onboarding/` | Character reference | Original output filename |
|---|---|---|
| `origin-surface-insider.png` | `origin-insider_baby.png` | `exec-15135416-d2b9-40cd-b9ed-a1c65693b54e.png` |
| `origin-surface-exit_liquidity.png` | `origin-exit_liquidity_baby.png` | `exec-a0480ec2-adbd-4bba-bba6-7c488c5205ff.png` |
| `origin-surface-seeker.png` | `origin-seeker_baby.png` | `exec-768f754c-a895-4a3e-841c-f7c11970363b.png` |

All three originals remain under `/Users/mac/.codex/generated_images/01a12bf8-ea0a-74c3-b630-447fda297131/`. Existing portrait paintings were preserved. The accepted generated outputs were copied byte-for-byte to the new sibling paths.

Shared prompt constraints: retain the accepted 2:1 frame, blank mint tab and exactly two separators at 54% and 75%; keep x44–94% as a blank opaque white information area; integrate the source baby into the left 36–40% below the tab; preserve recognizable face, hair, expression, outfit, pose and defining prop; remove the old portrait border and room backdrop; do not create an inset picture frame; no text, labels, icons, stats or duplicate characters; entire output opaque, including white corners. Outcome-specific instructions retained the Insider's suit/coins, Exit Liquidity's worried face/hoodie/empty piggy bank, and Seeker's curls/explorer jacket/black phone.

Exact full prompts, source paths and destination manifest: `../ct-life-evidence/player-cards-20261012/origin-surfaces-provenance.json`.

## Validation and integration boundary

- All three images were visually inspected together against the accepted base and their original baby paintings. Their rim, tab, separator geometry and palette remain consistent. Each baby and defining prop are recognizable; there are no baked text or game-value claims.
- Every output is **1774 × 887 RGB**, exactly 2:1. RGB has no alpha channel: the complete image is opaque, including white information regions. No transparency repair or compositing script was used.
- The sampled blank right-hand field ranges from RGB 253 to 255, with channel means approximately 253.9. It is neutral bright white with the generated base's very light surface variation; it is not an exactly uniform RGB-255 fill.
- New metadata uses unique 32-character GUIDs, max 2048, Android ASTC 6×6, no mipmaps, clamp and no alpha. The coordinator added the corresponding max-2048 opaque-surface exception to `OnboardingArtImporter`, because the older generic origin rule would otherwise reduce them to 1024.
- Original hashes and opacity/colour checks are retained in `../ct-life-evidence/player-cards-20261012/origin-surfaces-pixel-checks.json`.
- The coordinator owns the 1520 × 760 runtime card geometry, live text/benefits, 3D avatar composition, resource lookup, Unity tests and Seeker review. This art subtask did not operate Unity, ADB or Git and did not edit runtime UI code or styles.

| Outcome | SHA256 |
|---|---|
| Insider | `2416c4cdff0dfa69db5a401e53c36a980e859f36ac0ddf8c41c583b0364552bd` |
| Exit Liquidity | `24d303f199e4d34f7a1f8945547d1f5577dcbb99a80b596dfddc98fd68ced272` |
| Seeker | `c6940f2702bb5d9b58d4751bfb25c3008ecfe0c28d1c54c287c8b834903d7ea2` |
