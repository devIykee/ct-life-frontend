# Onboarding and personal character integration — 2026-10-11

## Implemented

- Removed the Full/Face controls. Pick/customize use the full-body podium; subsequent questions use a fixed large portrait with no turntable interaction.
- Removed the render-texture selection fade that made opaque characters appear translucent. Actual Rain/Snow torso alpha was measured during idle/wave from four yaw angles.
- Scoped flatter buttons/cards to onboarding. Preserved the existing backdrop, illustrated roster, typography and other game screens. White customization panel, compact category tabs, selected-color label, reset swatch and animated yellow Customize arrow tag.
- Dex/Nova have four real fitted hairstyles each. Snow/Rain retain their existing outfits with independent skin/hair/top/bottom/shoe palette controls. These are options within identities, not duplicate people.
- Personal character is accepted only after a real validated GLB loads as a Humanoid. The accepted job persists through the creator request/profile and is prepared again on resume. An explicit Return to original character action separates stock palette controls from the generated whole model.
- The server price is 3 USDC. Client checkout reviews the quote/network, uses the existing wallet signer, and waits for backend verification. An unresolved receipt does not offer a second charge. Failed paid creation retains delivery owed and a reference for review.

## Verification

- PlayMode initial corrected integration: 16/16 passed, including material alpha, character palette rendering, actual generated GLB retargeting and onboarding flow.
- EditMode palette/identity/creator: 72/72 passed.
- Final focused PlayMode: 8/8 passed. Final LikenessInputTests: 6/6 passed.
- Backend focused regression: 356/356 passed, including 117 palette/character-persistence cases and 24 checkout cases.
- Settled offscreen captures use a stencil/depth attachment and wait for entry animation; earlier two-frame captures were not reliable visual evidence.
- Additional regressions passed: actual personal model/home and animated preview framing 2/2; exact character creation/reconciliation 9/9; profile and likeness input 6/6; empty-audio-pool telemetry 6/6. An initial audio test filter selected zero tests; the corrected namespace ran all six.
- First Android pass verified stock Rain opacity, personal model acceptance and fixed portrait behavior, then exposed the home screen ignoring the personal model. That source path is corrected and regression-tested; the final Android build is installed and device verification passed as recorded below. No live wallet transaction has been tested or charged.

## External state and remaining work

- Tripo support was emailed the exact private-server, in-game end-user use case. Their automatic acknowledgement is not authorization. Public checkout/photo generation remains disabled pending clarification and deployment readiness.
- Iyke replied that his Railway plan does not permit the member invitation. The secret-free release ZIP and exact owner-deploy steps were sent to Iyke on Telegram (archive 22:43, instructions 22:55 local). It contains the 66-file patch, migration/storage/worker setup, validation and checksum. No production deployment has been verified.
- Paid operator trial spent 175 of the authorized 200 credits. The local demonstration reuses the existing corrected synthetic artifact at zero additional provider cost; it is not the user’s face.
- Independent hairstyles for Snow/Rain and extra geometry/outfits for the other human models remain asset work. Do not represent color changes as new hairstyles or clothing geometry.
- Generated-avatar multiplayer appearance replication is not yet implemented. Whole generated meshes do not preserve every stock outfit or free palette control.

Evidence is outside the repository at `/Users/mac/Documents/codes/projects/ct-life-evidence/onboarding-bright-20261011/`.

## Final Seeker verification (completed early 2026-10-12)

- APK: `ctlife-bright-likeness-20261011-release.apk`, 227.0 MiB; Unity Android build succeeded with 0 errors and 25 warnings. The existing warning count is not zero.
- Rain customization: solid character, reset icon renders, all eleven skin swatches fit across explicit rows. No Full/Face controls. Fixed large Find your people portrait touches the left/bottom edges.
- Personal preview: head and feet visible after animation settles; actual synthetic GLB accepted in app. Fresh local QA `LikenessQA12` completed all onboarding choices and normal creation returned 201; refreshed profile retained the exact personal job.
- Final APK cold launch reloaded that personal model on home, facing the camera. An earlier saved QA personal model also entered the city; development logs confirmed personal spawning.
- Correction to an intermediate visual interpretation: the earlier city avatar was not established to be stock. Comparing the actual synthetic reference and fresh spawn logs confirms the personal model. The confirmed stock substitution was on home, now corrected.
- The home-facing runtime regression passed 2/2. The final QA phone log contained no Unity/Android errors or invalid AudioSource-time warnings during the recorded check. This is a bounded device check, not a claim that all crashes are eliminated.
- No real wallet charged and no additional provider generation during these device checks. Tripo operator spend remains 175/200 credits.
- No commit or push performed. Production deployment, live payment acceptance, remaining human wardrobe geometry and multiplayer personal-appearance replication remain outstanding.

Selected phone captures in `../ct-life-evidence/character-podium-20261011/`: `final-rain-customize.png`, `final-rain-people.png`, `final-personal-preview.png`, `final-personal-home.png`.

- Final review handoff: Seeker left at Pick your character, Rain selected, on an isolated local QA identity. Original staging/user profile was not reset. Generated personal model also responded to the city joystick before this handoff.
