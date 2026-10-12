# Character and card review — 2026-10-12

Current change follows the user's October 12 review. The previous completed onboarding work was pushed to `dev` as `cc6355fd` before this pass. The user now explicitly requests a push after each completed, verified change; the coordinator rule in `docs/AGENTS-BRIEF.md` records that.

## What changed

- The raised stretch now has complete authored finger curves on Snow/Rain, with the shared articulated clip covering Dex/Nova. Body/arm/root curves are preserved. This is skeletal animation and skinning, not hand physics or collision simulation.
- Rain's clothing now renders its opaque inner-facing surfaces. Both original and recoloured appearances retain the source's surface-side choice and consistent backface lighting. The old small torso-alpha test did not cover these holes.
- The large question portrait renders across the physical panel, including the camera-cutout inset, while preserving the face's size and position. The model remains noninteractive on questions and has no podium. Clipping occurs at the physical screen rather than the old square portrait boundary.
- All eight dreams use matching authored purple badges and compact white choices. Six new icons were generated to match the existing cash/trait family; each dream has a distinct icon.
- The player identity card uses the actual customized model, a bright graphic field, prominent alias, tribe and compact dream/traits. Consent remains outside. Names fit by measured text width, including a valid twenty-character wide alias. The card retains its edit targets and hides an origin that has not been drawn.
- Origin results have a compact illustration and open adjacent starting information, with a flat Continue action. The existing draw, probabilities, server results and economy are preserved. Seeker now has matching artwork instead of the plain missing-art fallback.

## Reference and technical evidence

The recovered Claude request for a player identity card and Hermes's seven supplied image references are separate from Hermes's subsequent origin-card implementation. All seven attachments were inspected. The primary composition reference is the user's Storm Breakers image; its actual Pinterest page was also opened and visually reviewed in Chrome. See `docs/research/player-cards-context-20261012.md`, `dream-icons-20261012.md`, `seeker-origin-art-20261012.md` and `art/LICENSES.md`.

The camera implementation follows Unity 6.3's [custom projection matrix](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Camera-projectionMatrix.html) and [RenderTexture lifetime](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/RenderTexture.Release.html) documentation. The larger canvas reuses a bounded render target; it does not add another camera.

## Desktop verification

Evidence is outside the repository at `../ct-life-evidence/onboarding-refinements-20261012/`.

- `verified-playmode.xml`: 13 passed, zero failed/skipped. Actual animated surfaces and readback pixels exercise portrait scale/position, render coverage beyond the old frame, four human hand poses, palette preservation, and onboarding behavior.
- Portrait readbacks retain all 17,671 sampled opaque interior pixels across Rain/Dex/Shark. Maximum measured face-height difference is 0.07 UI pixels. See `docs/progress/raised-hands-opacity-20261012.md` for the full diagnostic and the test-only active-render-target warning.
- `verified-editmode.xml`: 64 of 65 initially passed. The single failure was the older assertion that Seeker artwork must be absent. That assertion now requires the shipped Seeker face and still checks a genuinely missing origin returns null; `verified-art.xml` passes its rerun. Both finger-curve preservation tests passed.
- `final/receipt.json`: 20 rendered screen captures, all checks passed, fixture preferences restored. Includes Snow/Rain/Dex/Shark identity cards, long names/dreams, unchecked/checked age consent, actual raised-hand question poses, all three origin outcomes, long resource labels, and the real draw animation.

## Android and Seeker acceptance

- Android development build succeeded with zero errors and 25 warnings: `ctlife-cards-hands-20261012.apk`, 227.0 MiB. It was installed successfully on the Seeker. This pass did not build iOS.
- Rain's original white top and red recolour were visually reviewed on the phone, including rotation. The new large people/traits portraits, matching dream badges, identity card and its long dream label were also reviewed on-device.
- With a separate local QA identity, tapping the identity card's dream row opened the selected dream, changing it to “Build a project” and tapping Done returned to the same card with the new choice. Age consent stayed outside the card and gated Continue.
- The first origin request displayed the connection error/retry state. Inspection found the four ADB USB reverse mappings absent while all four local services were listening. Restoring the mappings and tapping Try Again completed the real local-server draw and revealed Insider Baby, with the Builder perk, $3,000 game cash and Studio. This verified recovery without bypassing the draw.
- The queried Unity/AndroidRuntime error log was empty, and no unexpected app exit was observed during this acceptance pass. This is a bounded onboarding check, not a full-game stability certification.
- The user's existing `joel` draft was then restored unchanged and left at its new player card: Builder, Build a project, Early Bird and Rug Veteran, with age consent still unchecked. Device screenshots are `seeker-review-ready.png` and `seeker-origin-result-retry.png` in the external evidence directory.
- Final thermal inspection reported status 2 (moderate), skin 40.4°C. No production backend deployment or paid-avatar generation was performed in this pass.
