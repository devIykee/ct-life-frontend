# Character screen integration — 2026-10-11

Character-screen device pass complete. No commit, push or public deployment. Paid likeness and staking remain unfinished as detailed below.

- Source reference reviewed: Łukasz Zagdan Super Brawl original ArtStation project, reached through Pinterest.
- Ten source-referenced 2D roster portraits, flat preview tools and paid pencil entry beside the selected name, authored All/Men/Women/Others browsing, real preview podium and wider reaction framing.
- Independent Dex/Nova skin, hair, top, bottom and shoe colors. Exact seven-field variants use both reviewed art masks and a fresh server allowlist. Outfits stay within their original identity.
- Studio pricing checked in browser; Studio credits and API credits are separate. API trial spending belongs to the backend operator with a 200-credit cap. Human and Froggy trials consumed 150 in total; both generated replacements remain unapproved for runtime/paid delivery.

## Verification so far

- 112/112 focused EditMode cases passed, including saved identity/wardrobe/colors and fresh-server allowlist requirements.
- 11/11 PlayMode cases passed before Android build, including rendered mask isolation and actual emote framing.
- Palette importer initially failed on variable scope, missing test assembly reference, then incorrectly inferred Cubemap texture shape. These were corrected. All four masks now load as 1024x1024; source hashes and linear/alpha/compression settings validated.
- First Android build stopped at the existing character animation gate: 16 distinct source FBXs had persisted CopyFromOther references and generic-body HumanDescriptions. Their model-specific cached artifacts had hidden the incorrect saved import metadata.
- Rebuilt each source's own CTS/creature Humanoid Avatar and enforced own-source settings on every import. A separate Android process force-reimported all 16 and passed both mapping/Avatar ownership checks and the unchanged cast animation validator. Source geometry is preserved.
- Repeated all 11 PlayMode cases after the Humanoid import repair: 11 passed, 0 failed.
- Backend verification: 247 tests passed across 12 files; typecheck, lint, contract checks and layering checks passed. Existing documentation-check omissions and broken links remain reported separately by the backend agent.
- First successful Android APK: 226.9 MiB, 0 build errors and 25 warnings. Installed and launched on Seeker against the local backend.
- Initial Seeker review confirmed ten illustrated cards in two rows, functional Men filtering, and the real full-body podium. Device review exposed inherited yaw on newly selected identities, name/podium overlap and a clipped selected badge; corrected those before the final device pass.
- Device input review found the expanded preview intercepted Back touches. The creator now places the header immediately above its content, beneath modal overlays (Unity 6000.3 `VisualElement.PlaceInFront`). A new graphics-enabled regression exercises the actual overlapping geometry, `IPanel.Pick`, overlay interception and Back navigation: 1/1 passed after qualifying an ambiguous test-only `Position` type. Device verification of the final APK follows.
- On Seeker, the pencil opened the authenticated availability panel and its return action worked; the panel correctly reports personal characters unavailable.
- Dex palette selections survived a deliberate restart: `skin_08`, `top_ubc_m__red`, `bottom_ubc_m__denim`. An isolated QA character submitted through the normal creation API retained those exact IDs in `/v1/me`, then rendered the red top and denim bottoms in Timeline Square. Screenshots and sanitized seven-field server evidence are outside the repo.
- The observed QA city process reported zero fatal/native-fatal/out-of-memory/null-reference/missing-reference matches and zero Unity error records in its retained log buffer. This is a bounded device observation, not a general crash-free guarantee.

External logs and images: `../ct-life-evidence/character-podium-20261011/`.
Final touch-order APK: `ctlife-podium-20261011-final.apk`, 226.9 MiB, 0 errors, 25 warnings, installed on Seeker. Back now navigates from Customize to Characters on the device. Filters showed exactly Men (Dex/Snow), Women (Nova/Rain/Runner), Others (Froggy/Gynoid/Shark Person/Cipher/Hazmat), All (10), without changing the selected Rain. The phone is returned to the character screen on the original isolated review wallet; palette creation used a separate QA account.

The last roster capture exposed Rain's larger podium still behind its caption. Reserved a bottom caption strip by reducing the stage height from 900 to 820 logical units; the render still fits measured arm/reaction bounds. Rebuilt (86 seconds, 0 errors, 25 warnings) and installed for the final capture. No further feature changes in that build.

Public likeness, real photo/payment/equip and staking entitlements remain unavailable. Human and Froggy operator trials cost 75 credits each, 150 total of the authorized 200; no further generation is running. Both generated replacement meshes remain unapproved for paid delivery. CT personality characters were not added in this pass.
