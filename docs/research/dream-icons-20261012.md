# Dream icon coherence — 2026-10-12

The served-contract snapshot contains **eight** dream IDs. The existing dream UI used fallback glyph discs, while the trait choices already used the authored purple badge PNG family. This change adds `OnboardingCopy.DreamArt(id)` and six matching badge assets. Two existing assets are exact semantic matches and remain reused.

| Registry dream | Art name in `Resources/UI/Kit/Icons` | Visible symbol |
|---|---|---|
| `make_10k` | `cash` (existing) | Two coin stacks |
| `top_rich_list` | `dream_top_rich_list` | Trophy and crown |
| `rug_survivor` | `rug_veteran` (existing) | Rolled rug and shield |
| `build_a_project` | `dream_build_a_project` | Rocket launching from a laptop |
| `get_rugged_by_creator` | `dream_get_rugged_by_creator` | Rolled rug and falling coin |
| `host_a_space` | `dream_host_a_space` | Broadcast microphone |
| `stake_ore` | `dream_stake_ore` | Ingot and padlock |
| `call_a_top` | `dream_call_a_top` | Chart peak and target |

Unknown IDs return `null`, retaining the caller's existing `DreamIcon` fallback. No registry IDs, labels or backend behavior changed. Screen layout and card integration are separate changes owned by the main task.

## Source and style evidence

The existing six trait badges and functional kit badges were visually inspected. `cash.png` and `rug_veteran.png` fit their dream meanings directly. A briefcase, bank building or generic speech bubble did not describe the other dreams precisely enough to justify reuse.

The user requested authored cartoon icons in the Oct 9 Claude review, using the attached Cartoon Buttons Set as an example “in our own theme.” The dream assets extend the already implemented trait icon family; this is not a change to the white screen surfaces or navigation controls. Full user-context evidence: [player-cards-context-20261012.md](player-cards-context-20261012.md).

All six new images were created with the built-in `image_gen.imagegen` tool using the existing `cash.png` as the style reference and `transparent_background: true`. No external icon pack or dependency was added.

Shared prompt: one centered production UI icon; match the reference's round silhouette, CT-purple domed face, dark-plum rim, lavender upper band, white upper-left oval shine and companion dot, violet lower lip; one simple pure-white symbol with dark-plum outlines/dividers; readable at 64 px; balanced transparent margin; actual alpha outside the circle; no background, exterior shadow, text, labels, numbers, logos or watermark. Each prompt then specified exactly the corresponding symbol listed above.

Original generated files remain in `/Users/mac/.codex/generated_images/01a12bf8-ea0a-74c3-b630-447fda297131/`:

| Art | Original file |
|---|---|
| `dream_top_rich_list` | `exec-3d0bd1ae-695e-4d84-82b6-dcda1e76d866.png` |
| `dream_build_a_project` | `exec-6cf5d2b9-851b-451b-9f19-177d59afa231.png` |
| `dream_get_rugged_by_creator` | `exec-b96403d1-62f2-4268-8f1c-d266d0ccc8c2.png` |
| `dream_host_a_space` | `exec-2d40247d-99b2-4df4-955c-0ee8ca8bfc1c.png` |
| `dream_stake_ore` | `exec-6aeba535-0856-4612-abb3-d1d7ad55158d.png` |
| `dream_call_a_top` | `exec-72cfe54d-000b-4f90-8ede-a7c132d6b186.png` |

Exact prompts/source-to-destination manifest: `../ct-life-evidence/player-cards-20261012/dream-icon-provenance.json`. Accepted images were copied byte-for-byte into the project; originals were preserved.

## Validation

- All six images visually inspected: distinct requested symbols, consistent badge family, no text or solid rectangular background.
- PNGs are 1254 × 1254 RGBA with alpha ranging from 0 to 255. Four have entirely zero-alpha outer edges. The rug/coin and microphone images have respectively 3 and 7 black outer-edge pixels at alpha 1/255; no opaque or off-white edge pixels. Those negligible alpha remnants are recorded rather than described as exact zero.
- Import metadata follows the existing `cash.png` settings: Android ASTC 6×6, max 128, alpha enabled, no mipmaps, clamp wrapping. Every new GUID is a distinct valid 32-digit hexadecimal value.
- An asset-manifest check confirmed all eight registry dreams map to eight distinct existing PNGs. Unknown-ID fallback is present. `git diff --check` passed for `OnboardingCopy.cs`.
- Unity import, integrated screen review and Seeker validation are owned by the main task; they were not performed by this asset subtask.

Detailed pixel checks: `../ct-life-evidence/player-cards-20261012/dream-icon-alpha-checks.json`.
