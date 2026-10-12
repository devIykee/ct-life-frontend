# Illustrated cards, concise copy and Rain eyes — 2026-10-12

Follow-up to the user's review of `b88914e1`. The coordinator inspected the supplied Pinterest search and opened the Oscar profile-card reference. Its illustrated perimeter, patterned character field and integrated information rows guide these generated landscape surfaces. See `docs/research/illustrated-cards-20261012.md` for references, prompts and asset provenance.

## Changes

- Rain's right pupil had an actual missing front cap. The export optimizer's rear-eye filter deleted it based on winding. Restoring the cap and correcting both fresh extraction and cached-LOD rebuilding covers 29 appearances, 99 meshes and 41 runtime FBXs. Existing positions, triangles, UVs, weights, normals and skeleton transforms were independently compared through FBX round trips and remain unchanged. Detailed provenance is in `rain-eye-surface-20261012.md`.
- Removed the dream footer instruction, name subtitle, duplicate short-name footer and duplicate age footer. The persistent “3–20 characters” helper, age checkbox and existing validation gates remain.
- The actual selected 3D character, alias, tribe, dream and traits are composited over a generated opaque 2:1 player-card surface. Names remain editable and fit by measured width.
- Each origin has a matching generated surface incorporating its existing baby identity. All live rarity, odds, alias, story, tribe perk, cash, home and debt now sit inside the same card. Existing server draw/results, retry behavior and economy are preserved. The original paintings remain available for the draw and missing-surface fallback.
- Surfaces import at max 2048 with ASTC 6×6 on Android, clamp and no mipmaps. Alpha is disabled on these fully opaque textures. Current [Unity 6.3 alpha import documentation](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/TextureImporterAlphaSource.html) and [USS property reference](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-USS-SupportedProperties.html) were consulted.

## Verification

Evidence lives outside the repository at `../ct-life-evidence/eye-card-revision-20261012/`.

- Before/after eye diagnostic: maximum 1,405 enclosed missing pixels before, zero missing or partly transparent pixels across all 24 animated close-eye views after. Actual shaded images show both black pupils; isolated eye renders rule out opaque face geometry concealing a hole.
- `eyes-after.xml`: 2/2 passed on the corrected candidate, including the always-on 24-pose regression.
- `cards-pass1/receipt.json`: 22 captures passed, including the empty/valid name screens, long/wide names, long dream, four actual characters, all three outcomes, long resource/perk values and the real draw. A visual review caught the small game-cash note touching the rim in the long-home case; its spacing was adjusted for the final pass.

## Final acceptance

- Complete source synced to the clone: `verified-editmode.xml` passed 47/47; `verified-playmode.xml` passed 11/11, zero failed/skipped. The latter includes the final eye diagnostic/always-on regression and existing onboarding controller tests.
- `cards-final/receipt.json`: all 22 screen captures and 11 loaded-surface checks passed, with no failures and fixture preferences restored. Direct review includes the corrected long-home footer, wide alias, Rain name range and all three origin surfaces.
- Android build succeeded: `ctlife-illustrated-cards-eyes-20261012.apk`, 227.0 MiB, 86 seconds, zero errors and 25 warnings. ADB installation returned Success. This pass did not build iOS.
- On the Seeker, a separate local QA identity exercised Rain's people/traits/dream portraits: both pupils remain solid. Dream's duplicate footer is absent; the name screen has just the question, field and persistent 3–20 helper, including during keyboard entry. No duplicate short-name footer is present.
- The generated player card displays the selected Rain model, alias, Builder tribe and two selected traits, with age consent outside. Its dream edit opened the actual selection page; selecting Host a Space and tapping Done returned to the card with that real choice. Continue remained gated until age consent, without another age instruction above it.

- The real local-server draw completed on the Seeker and displayed Insider Baby with the selected alias, Builder launch discount, $3,000 game cash and Studio inside the new illustrated surface. `seeker-origin-card.png` is the actual device screenshot.
- The queried Unity/AndroidRuntime error log was empty; no unexpected app exit was observed in this bounded onboarding check. The user's existing `joel` draft was restored with Builder, Build a project, Early Bird, Rug Veteran and unchecked age consent, and left open at its player card. See `seeker-review-ready.png`.

This pass does not deploy a backend or claim full-game stability.
