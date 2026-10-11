# CT Life — character shortlist and avatar pipeline

Verified 2026-10-05.

**Accepted exploration:** the user selected “Explore Snow/Rain with adult proportions”. A single Snow proof is complete as an exploration in [snow-adult-proof/](snow-adult-proof/README.md), with [before/after](snow-adult-proof/before-after.jpg), [pose checks](snow-adult-proof/pose-checks.jpg) and a Blender-validated candidate FBX. It exposes unresolved hand/LOD/material/expression work; it does not select a production avatar or authorize a batch.
 **[Inference] This report contains art judgments and a proposed pipeline; those are recommendations, not completed runtime capabilities.** No accounts, purchases, uploads of personal photos, Unity edits, or service integrations were made.

**Finding:** none of the inspected free assets is a finished Sims-like, morphable, 6k-triangle mobile character system. There are good authored characters; the important missing work is a reusable game mesh, deformation, clothing fit and LODs. Do not replace that work with another generated mannequin set.

**Recommendation:** test **Blender Studio Snow + Rain in one reversible mobile conversion pilot; no production character selection is made**. They have finished faces, clothes, hair and rigs, and commercial adaptation is allowed under CC-BY 4.0. Their native rigs must not be shipped. Their large eyes and young, animated-film proportions also need an explicit adult-proportion art check; the default target remains adult Sims-style humans, not a new Pixar direction. Quaternius is a useful technical baseline, but its free superhero body types fail the everyday-city silhouette. Keep automatic selfie generation outside the critical path. MetaPerson is the paid candidate to evaluate only if that feature justifies the recurring cost and a written license covering CT Life's actual avatar commerce.

**Earlier hand-quality checkpoint:** [detailed hands + independent gestures](snow-hand-quality/README.md), with [before/after](snow-hand-quality/hand-before-after.jpg) and [phone/wave proof](snow-hand-quality/gesture-proof.jpg). The former dark hand patches are absent in the reviewed new closeups. Near tiers deliberately measure 11,451 / 18,365 triangles with 55 bones; both pass posed FBX checks. Atlas and canonical Unity mapping were subsequently completed in the compiled asset below.

**Actual Unity integration asset:** [single skinned mesh + real 2048 atlas](unity-snow/README.md) is now compiled, visually reviewed and delivered to the coordinating Unity agent. [Front](unity-snow/renders/creator_front.png), [face](unity-snow/renders/creator_face_front.png). This is the source-quality 43,478-triangle near avatar, with validated 21,005 / 12,135-triangle world LODs using the same atlas and 55-bone bind. [LOD comparison](unity-snow/lod-comparison.jpg). The coordinating agent reported the creator Unity import and retarget checks passed; in-game/device acceptance remains with that agent.

## Visual evidence

- [Face comparison](comparisons/face-comparison.jpg)
- [Full-body comparison](comparisons/full-comparison.jpg)
- [Interactive local gallery](comparison.html): switch faces/full bodies and inspect each model at full size.
- [Snow official artwork](thumbs/snow_official.jpg), [Rain official artwork](thumbs/rain_official.jpg).

The comparison images are actual downloaded meshes rendered with their supplied textures in Blender 5.1.2/Eevee, using the same lights. They are not image-generator substitutes. Snow/Rain render subdivision was reduced to level 1. Separate cornea/highlight shells were hidden for the local Eevee preview because Snow's original cornea shader rendered opaque; the eyes underneath are the supplied model. No source meshes were resculpted. Quaternius's two normal-map filenames incorrectly contain `_png.png`; the preview script reconnects them to the supplied `.png` files in memory. A neutral T pose is shown; this is an asset comparison, not evidence of final in-game shading or animation.

## Four concrete candidates

| Candidate | Verified source and cost | Actual inspected content | Decision |
| --- | --- | --- | --- |
| **Snow v4** | [Blender Studio](https://studio.blender.org/characters/snow/v4/), free CC-BY 4.0. Published page 8 May 2025; downloaded blend is `snow_v4.2.blend`. | Finished male character, separate head/body/hair/shirt/pants/shoes, facial correctives. Native rig: 1,494 bones, 290 marked deform. Non-subdivided evaluated render geometry: 66,900 triangles. | **Best free male source art.** [Inference] Soft stylization and finished clothing are closer to the target quality than the other free candidates. Requires new runtime rig/topology, texture bake, identity morphs and LODs. |
| **Rain v3** | [Blender Studio](https://studio.blender.org/characters/rain/v3/), free CC-BY 4.0. Page 8 May 2025; archive folder says v3.3, blend says `rain_v3.2.blend`. | Finished female character, separate hair/top/jeans/shoes/scarf. Native rig: 2,166 bones, 392 marked deform. Non-subdivided evaluated render geometry: 79,594 triangles. | **Best free female source art.** [Inference] Strong authored face and consistent style with Snow. More cartoon-like eyes than Sims 4; do not call it an exact match. Same conversion requirement. |
| **Universal Base Characters / Standard** | [Quaternius official listing](https://quaternius.itch.io/universal-base-characters), CC0. Standard free; Source listed $19.99. | Existing local Standard glTFs: male 14,318 tris, female 15,060; 65 skin joints each; 3 meshes each; no morph targets. Supplied light/dark skin maps and modular hair. Free tier is superhero male/female; regular and teen bodies are in paid Source. | **Technical fallback, not the art pick.** [Inference] Easier mobile adaptation, but the free exaggerated musculature is wrong for ordinary city residents. Source regular bodies are a plausible $19.99 one-time alternative; they have not been purchased or inspected. |
| **MetaPerson official sample pair** | [Official avatar comparison and sample downloads](https://avatarsdk.com/avatars/). Samples for evaluation, not a CC0/CC-BY library. [Pro pricing](https://avatarsdk.com/pricing-cloud/) displays $800/mo. | Public sample GLBs downloaded without an account: male 57,729 tris / 10 materials, female 66,144 / 12; 73 skin joints. Actual mesh target sets contain 41, 65 and 18 targets. | **Most complete verified photo/customization route**, but not a free source library. [Inference] Samples are more photorealistic than the target style. Commercial terms, lower export settings, identity quality and device performance need separate validation. |

Triangle measurements are from local assets, not vendor marketing. The Blender totals include supplied renderable meshes with subdivision disabled but other viewport modifiers evaluated. The Quaternius/MetaPerson totals come directly from indexed triangles in their glTF/GLB files. They are not all measured by identical export pipelines; see `inspection.json`, `deformation-inspection.json` and `export-check/results.json`.

## What export testing actually established

Blender source files were opened with embedded scripts disabled. Both contain skinned meshes with armature modifiers and vertex groups. Their named body shape keys are predominantly pose-corrective shapes (elbows, shoulders, fingers, spine), **not** an off-the-shelf body-weight/height/face-identity editor.

`export_check.py` exports selected render meshes and deform bones to GLB with animations and subdivision disabled. Both exports completed, with skin joint/weight data and morph targets present:

| Direct, unoptimized export | Size | Triangles | Skin joints | Materials | Skinned primitives |
| --- | ---: | ---: | ---: | ---: | ---: |
| Snow | 95,094,100 bytes | 49,200 | 293 | 15 | 16 |
| Rain | 81,358,240 bytes | 77,602 | 392 | 20 | 24 |

**This proves file export, not game readiness.** The exporter reports:

```text
WARNING: There are more than 4 joint vertex influences.The 4 with highest weight will be used (and normalized).
WARNING: The active Vertex Color will not be exported, as it is not used in the node tree of the material
```

It also splits UDIMs into materials and warns about multiple texture samplers. Snow's exported triangle total differs from the evaluated source; geometry-modifier and morph preservation need visual validation. No animation round trip, Unity Humanoid mapping, joint-bend comparison, Seeker profiling, or final material equivalence was tested. Treat `export-check/*.glb` as diagnostics, not production assets.

## Recommended local avatar pipeline

**[Inference] Proposed production pipeline. The single [Snow exploration](snow-adult-proof/README.md) now demonstrates proportions, a reduced rig, candidate LODs, static pose checks and an FBX round trip; the production customization pipeline below remains unfinished.**

1. Keep Snow/Rain originals immutable as source art with attribution. First compare adult head-to-body proportions, eye scale and face planes against the user’s Sims reference. Do not silently change the target to their native young animated-film appearance. Finish and inspect **the selected single Snow pilot** before adapting Rain or creating a crowd. Retopologize around eyes, mouth, shoulders, hips and knees; bake texture detail to the existing 1024² hero / 512² crowd atlas target. Use original face shapes as reference for an authored, coherent style.
2. Bind both to one runtime Humanoid layout matching the project's established bone budget. Rebuild weights to at most four influences per vertex. Keep Blender animation controls outside the export. Preserve mouth and eye expression through a small authored runtime set, rather than exporting thousands of control bones.
3. Author body presets and controlled morphs: height/proportions, shoulder/hip width, build, face width, jaw, nose, eye spacing, lips. Do not turn pose-corrective keys into identity controls. Outfit meshes must share the compatible rest skeleton, neck/wrist seams and corresponding body morphs. Occlude body faces under clothes; test the extreme slider combinations. Skin and hair colors need authored material/texture variations, not arbitrary whole-texture hue shifts.
4. Build head/hair/top/bottom/shoes/accessory slots and garment fitting. Existing separated objects are a starting point, not proof that clothing interchanges across the two bodies. Merge the selected pieces into one renderer/material per avatar for the game. Cache the compiled mesh by appearance hash; distribute compact appearance parameters instead of rebuilding every remote avatar every frame.
5. Use the quality-first compiled Snow tiers as the current pilot: creator 43,478, world LOD1 21,005 and far LOD2 12,135 triangles. These deliberately exceed the older spec estimates after the earlier 6k reduction visibly damaged hands and clothing. The coordinating agent accepted a larger pilot budget and owns measured Seeker crowd performance. Preserve face/hands first; any further reduction needs another visual/device pass, not a triangle-count-only decision.
6. Acceptance gate: inspect walk/sit/run, shoulder lift, crouch and all face/body extremes; verify mesh/texture budgets, one material, at most four influences, Humanoid mapping, light/dark skin shading, clothing intersections and the Seeker frame time. **If this gate loses the requested quality, stop multiplying assets and get a deliberately authored game-ready base.** A local one-time source-asset purchase or artist commission is a better next evaluation than an $800/mo identity dependency. No purchase is authorized or made.

This route keeps core avatars usable without a vendor account or runtime service. CC-BY attribution is still required. It does not automatically create a user's likeness from a photo.

## Own-photo / look-like-me assessment

**Manual likeness:** the local editor can show a user-selected photo privately as a reference while they choose face/body/hair presets. That is a truthful feature shape once implemented; it must not be marketed as automatic photo reconstruction. No user photo was provided, uploaded or processed during this research.

**MetaPerson:** current [REST documentation](https://docs.metaperson.avatarsdk.com/rest_api/) describes face/body modification blendshapes, split top/bottom/shoes, hairstyles and tuned exports. Pro includes creator integration and 6,000 avatars/month; the displayed rate is $800/mo plus $0.03 per additional avatar. REST API, custom topology, custom clothes and offline MetaPerson generation are Enterprise features with a quote. The provider's avatar page lists lower LODs, but even its listed body+outfit LOD2 is above the project's whole-avatar 6k budget. The actual sample quality was inspected; a personal-photo likeness result was not.

The [current terms](https://avatarsdk.com/eula/), section 3.1, restrict their clothing/hair features to their own generated avatars and require written notification before licensing/selling avatar rights; a revenue-sharing agreement may be required. [Inference] CT Life's avatar/item commerce needs explicit written terms before this route is selected. Do not transplant MetaPerson outfits onto a CC-BY body. Do not assume paying Pro clears all product uses. No vendor was contacted.

**Avaturn:** [docs](https://docs.avaturn.me/) advertise free integration and unlimited exports; [pricing](https://avaturn.dev/pricing/) lists Pro $800/mo, 1,000 avatars/month and $0.15 extras with API and custom garments. Its [linked terms](https://docs.google.com/document/d/e/2PACX-1vT5_TR6-MNs29LqI-LLKHvIKHVE0iluuapOpHODGRVDaqyfuCsEgaiE3ZIliI1-FN_-9rxJZ3iVo_jJ/pub) require attribution/commercial notice, keep avatar IP with Avaturn, and allow it to demand that avatar usage stop. The terms also limit Pro eligibility. **[Inference] Reject as the default character foundation** given revocability and the unresolved commercial terms. No account was created and no models were downloaded from Avaturn.

If automatic photo likeness becomes essential later, evaluate MetaPerson behind an optional adapter, obtain commercial permission for the exact use, generate on demand, optimize/cache a game-specific export, and keep the local authored avatar as fallback. Photo consent, retention/deletion, vendor availability, queue/error UX, and identity accuracy across the intended user population must be evaluated before product promises. This is an architectural recommendation, not an implemented integration.

## Sources screened out

- **MakeHuman/MPFB:** already rejected by the user; no new generated replacements were made.
- **VIPE / 100Avatars:** actual existing previews inspected. [Inference] Anime/chibi/fantasy proportions do not satisfy the revised target; not a route around the art problem. Existing VIPE VRM remains quarantined and unused.
- **TajSensei:** the [listing](https://tajsensei.itch.io/stylized-modular-characters) has a free male sample and a $9.90 modular anime pack, but no explicit asset license; its comments ask the unanswered license question. Not downloaded or selected for use.
- **CharCrafter, City Folks, old Quaternius modular sets:** existing previews inspected. [Inference] Their simplified/blocky faces fall below the requested quality. Do not make them the new default merely because they are free.
- **BlenderKit Lerato:** [listing](https://www.blendkit.com/asset-gallery-detail/813b5a5e-9598-4f81-994f-8a71754925a2/) is free but **Royalty Free**, not CC0, and advertises 56,227 polygons. Not selected or downloaded; no account created.

## Reproduction

Run from the repository root. Scripts use the installed Blender, and operate only on candidate artifacts.

```sh
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --disable-autoexec -t 4 -P art/character-candidates/scripts/inspect_blend.py
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --disable-autoexec -t 4 -P art/character-candidates/scripts/render_candidates.py
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --disable-autoexec -t 4 -P art/character-candidates/scripts/render_gltf_candidates.py
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --disable-autoexec -t 4 -P art/character-candidates/scripts/export_check.py
swift art/character-candidates/scripts/comparison_sheet.swift
```

See [license ledger](LICENSES.md) and [download manifest](download-manifest.json). Original downloaded models remain in the source/candidate folders. The compiled single-atlas Snow derivative has now been delivered for Unity import; the coordinating agent owns the app replacement and in-game evidence.
