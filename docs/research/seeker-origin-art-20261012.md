# Seeker origin illustration — 2026-10-12

Added `client/Assets/Resources/UI/Onboarding/origin-seeker_baby.png` to complete the illustrated origin family. The existing Insider Baby and Exit Liquidity Baby PNGs were both visually inspected and supplied to the built-in image generation tool as style/framing references. Neither was replaced.

The new art shows a playful baby with short curls, a light explorer jacket with purple trim, and one black mobile device with mint/violet edging, seated by a modest sunlit city doorstep. Two small painted sparkles accent the phone. It retains the painted rounded purple rim, pale inner trim, expressive baby proportions and warm lighting of the existing cards. It contains no text, statistics, logos, money pile or benefit claims. The device is a stylized illustration, not an exact verified hardware rendering.

Generated with the built-in `image_gen.imagegen` tool, `transparent_background: true`. Original retained at:

`/Users/mac/.codex/generated_images/01a12bf8-ea0a-74c3-b630-447fda297131/exec-f4274741-f047-4d43-a416-6666d1faad64.png`

Full exact prompt, reference paths and destination manifest: `../ct-life-evidence/player-cards-20261012/seeker-origin-provenance.json`. The brief requested a third matching portrait card; a smart playful baby holding a black Solana-mobile-style device; a light explorer jacket with restrained purple accents; a welcoming city near home; no wealth/benefit symbolism or readable UI. The output was copied byte-for-byte into game resources.

Validation completed:

- Visual review confirmed coherent framing, one phone, visible face/hands/feet, no unwanted text, and the requested bright city setting.
- PNG is 1069 × 1471 RGBA. Aspect ratio 0.7267 versus 0.7273 for both 640 × 880 references, a difference under 0.1%.
- Alpha ranges from 0 to 255. Maximum outer-edge alpha is 1/255, with 145 near-transparent edge pixels; no opaque rectangular background. Exact pixel-check evidence is retained in `../ct-life-evidence/player-cards-20261012/seeker-origin-alpha-checks.json`.
- Metadata copies the existing Insider Baby import configuration with a new GUID: no mipmaps, clamp, alpha enabled, Android ASTC 6×6 at maximum 1024. The existing `OnboardingArtImporter` handles the same `origin-` naming family.
- SHA256: `eb7096f6774195f8734b6f1baa72c78d15f7f71827be30a28aee01999314ca1e`.

The main task owns `OnboardingArt` lookup integration, reveal layout/cropping and Unity/Seeker verification. Those steps were not performed by this art subtask.
