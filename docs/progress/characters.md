# Character sourcing / adult avatar proof — progress log

Ownership: `art/character-candidates/` and this file only. Date: 2026-10-05.

## Completed sourcing pass

- Opened primary source/license pages; downloaded Snow/Rain source archives and MetaPerson official public evaluation GLBs without accounts or spending.
- Actual model comparison: `art/character-candidates/comparisons/{face,full}-comparison.jpg`; interactive `comparison.html`.
- Four shortlist entries (six models): Snow, Rain, Quaternius free Standard pair, MetaPerson official sample pair. Other candidates screened out on visible style/license gaps.
- Report: `art/character-candidates/README.md`. Per-download ledger and hashes: `LICENSES.md`, `download-manifest.json`. Parent owns merging rows into `art/LICENSES.md`.
- Actual mesh/rig inspection + two diagnostic GLB exports completed. Those native rigs are not game-ready: Snow has 290 deform bones, Rain 392; direct exports are 95/81 MB and retain 15/20 materials. No Unity integration, Humanoid validation or device test claimed.
- Paid API evaluation covers MetaPerson/Avaturn capabilities, $800/mo displayed Pro plans, API tier restrictions and commercial/revocability issues. No vendor contacted and no personal photos uploaded.
- Gallery JavaScript syntax checked and all 12 render paths exist. Comparison sheets visually inspected.

## Accepted next work (user selection relayed by parent)

User chose: **Explore Snow/Rain with adult proportions.** Build only ONE adapted Snow proof before any wider batch. Preserve face quality and adult head/body proportions, create a smaller game rig with at most four influences, purposeful LODs, before/after and deformation renders. Work remains outside Unity.

Status: **one Snow exploration completed and inspected**, with unresolved production-quality work documented. Source files remain immutable. Adult adaptation: head 76%, legs +9%, subtle shoulders/orbit adjustment. New 27-bone draft, max four normalized influences. City LODs 6,001 / 2,672 / 1,068 triangles. Original face remains as the detailed reference; no final production selection or finished avatar system is claimed.

- Review: `art/character-candidates/snow-adult-proof/{before-after,face-comparison,pose-checks,lod-comparison,hand-defect-comparison}.jpg`.
- Candidate blend and FBX in the same directory. All nine Blender FBX round-trip checks pass (bones, parts, triangles, rest geometry, weights and actual test morphs). This is not Unity/device validation.
- Arm raise, walk-contact and seated diagnostic poses rendered; no complete animation acceptance claimed.
- Remaining visible defects: simplified hand dark patches and loss of clothing/face contours. 27-bone rig has coarse fingers and no active jaw/expression set. Ten pieces/thirteen material slots; atlas is not baked.
- Recommendation: controlled creator/near retopology from the retained detailed reference, independent finger chains, expression/identity morph authoring, garment fitting and atlas bake before Rain or a cast rollout. Higher creator mesh and 55-bone canonical rig are proposals, not accepted changes to device budgets.
- Full measured report and exact reproduction commands: `art/character-candidates/snow-adult-proof/README.md`. No Unity/backend changes, accounts, purchases, commits or pushes.

## Reproduction / checks

Source inspection/render commands and caveats are in `art/character-candidates/README.md`. Installed renderer: Blender 5.1.2, scripts disabled when opening downloaded `.blend` files. Swift/AppKit produces labeled comparison sheets without adding dependencies.

Local snapshot batch failed with HTTP 406 on Avatar SDK pricing, while web tool opened its official pricing/terms successfully; manifest distinguishes captured snapshots from web-verified pages. Initial Pillow/OpenImageIO text approaches were unavailable; the completed sheets use Swift/AppKit.

## Targeted hand-quality follow-up — complete checkpoint

Parent requested preservation of detailed hands, independent finger/thumb articulation, phone grip, wave and export proof. This checkpoint is in `art/character-candidates/snow-hand-quality/`; the first proof remains intact for before-state comparisons.

- Dark hand/nail patches are absent in reviewed new closeups; source exposed hand/arm surfaces and UVs are preserved in both near tiers.
- City LOD0 **11,451 triangles / 6,056 vertices**; creator **18,365 / 9,672**. Deliberate +5,450 city triangles preserves the detailed hands rather than forcing the earlier 6k cap.
- **55 bones**, including thirty independently weighted digit joints. Max four normalized influences; every digit joint weighted.
- Both FBXs pass all thirteen checks, including evaluated rest/phone/wave mesh positions after fresh Blender import (maximum delta 8.19e-7 m). FBXs are rest-pose avatar exports; gesture rotations live in JSON and the blend opens in a creator phone review pose.
- Actual renders: `hand-before-after.jpg`, `gesture-proof.jpg`, `gesture-details.jpg`. Parent already inspected the restored relaxed hand and wave.
- GPU renders held until parent confirmed Unity bake complete; no active render jobs left after this checkpoint.
- Still pending: shoulder/sleeve/shoe cleanup, atlas, canonical Humanoid mapping, production expressions/customization and device profiling. No Unity import or full cast authorized/performed in this pass.
- Full report/reproduction: `snow-hand-quality/README.md`.

## Actual Unity delivery and rejected device pose — 2026-10-05

**Historical failed checkpoint: the `quality-review-20261005f` device pose is rejected.** The creator, sign-in portrait and city all visibly show both arms overhead and crossed legs during the supposed idle. Earlier valid-Avatar/numeric-retarget passes did not establish visible animation quality. The following historical checkpoints do not override this failure.

- The actual compiled asset is `art/character-candidates/unity-snow/`: one skinned mesh/material per tier, shared 2048² source-texture atlases, 55 bones, at most four normalized influences. Creator **43,478 triangles**, world LOD1 **21,005**, far LOD2 **12,135**. Detailed hands are retained; creator blend/FBX/maps remain byte-identical after LOD derivation.
- All 17 fresh Blender FBX checks per tier pass, with exact shared bone rest matrices and maximum evaluated pose round-trip difference 1.42e-6 m. These establish export consistency, not the correctness of Unity's Humanoid muscle interpretation.
- Root imported all three tiers and checked all 54 Humanoid mappings, 44 distinct shared clips and 27 sampled body poses. The device captures then exposed the incorrect idle despite those checks.
- Rejected evidence: [creator](screens/quality-review-20261005f/quality-review-20261005f/creator.png), [sign-in](screens/quality-review-20261005f/quality-review-20261005f/signin.png), [city](screens/quality-review-20261005f/quality-review-20261005f/city.png). These are real device images, not Blender reference renders.

### Idle fault isolation and pending correction

Read-only checks verified the controller default state's name is `loco_idle` and its GUID resolves to `client/Assets/Art/animations/loco_idle.fbx`. A fresh Blender import of that **actual clip** was sampled at frames 1, 20, 38, 60 and 76. Its hands remain approximately **0.80–0.85 m** high while its shoulders are **1.27–1.30 m** high: the source idle has the arms down. It is not a stretching clip accidentally selected as idle. The raw CTL body and clip also have matching geometric rest positions.

The serialized Snow rest positions are already near a geometric T pose: upper-arm joint Y≈1.445 m and wrist Y≈1.422 m, about 2.6° slope. Therefore the supported description is **incorrect canonical Unity Humanoid reference pose/orientation**, not a claim that the authored source was in a gross A pose. Valid bone names and human scale did not establish correct muscle axes.

Root's Unity diagnostic reported idle `Arm Down-Up` muscle values around −0.549 on both sides, yet Snow's hands were at Y≈1.77/1.72 m, above its head at Y≈1.49 m. Root then used Unity's editor `AvatarSetupTool.MakePoseValid` on an import clone to capture an enforced T-pose into `HumanDescription`; source FBX, atlas and mesh bind data were not changed. Root reported the corrected import passes all 27 samples and a new idle assertion requiring hands at least 0.25 m below the head. **The correction is pending a fresh device visual capture and is not accepted solely on these numeric checks.** Implementation/reference: [UnityCsReference AvatarSetupTool](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/Inspector/Avatar/AvatarSetupTool.cs); root owns the Unity edit and verification.

### Confirmed animation/expression gaps after the reference-pose correction

Read-only inspection of the actual runtime controller found **46 states referencing 44 distinct FBX files**. The clip registry attributes 12 states to Quaternius UAL 1/2 body clips and 34 to CT Life script-keyed emotes layered on those clips. This is not Snow's native expression/gesture library.

- **Finger animation is missing from the shared retarget path.** `art/pipeline/ctl_config.py` collapses each hand's four fingers into `index_01` and the thumb into `thumb_01`. The source clip's serialized Humanoid mapping has no finger chains. `anim_emotes.py` authors those mitten controls. Snow's thirty independent digit joints therefore provide capability, but the imported legacy clips do not supply the reviewed Snow-native finger poses. The standalone Blender phone/wave diagnostic poses were not silently installed in the controller.
- **Facial animation is not installed.** Current Unity logs report `shapes=0` on creator and both LODs. The conversion rigidly remaps facial deformation to `Head`, retains eye bones and leaves `Jaw` inactive/unweighted. No blink, viseme, gaze or facial-expression runtime driver was found. `AvatarView.SetTalking` only sets its `Talking` flag.
- **Motion speed is not connected to the clips.** `AvatarView.SetSpeed` sets the `speed` float, but the generated controller has zero blend trees and all state speed parameters disabled. Every state plays at speed 1. [Inference] Actual navigation speed can therefore diverge from gait speed; validate planted feet during acceleration, stopping and turns.
- **Emotes replace the full body.** `AnimationLibrary.TryPlay` crossfades layer 0; the controller has one unmasked Base layer and IK pass disabled. The code's “overlay” bookkeeping is not an upper-body animation layer. [Inference] A wave while the avatar continues moving can replace locomotion and produce visible sliding. This requires a moving-gesture capture, not an isolated pose sample.

Evidence locations: `client/Assets/Scripts/Editor/ContentImport.cs` (`BuildAvatarController`), `World/Avatars/AnimationLibrary.cs`, `World/Avatars/AvatarView.cs`, `client/Assets/Art/animations/{clip_map.json,loco_idle.fbx.meta,emote_wave.fbx.meta}`, `art/pipeline/{ctl_config.py,anim_retarget.py,anim_emotes.py}`.

### Authored source material available for the next quality pass

The original Snow blend was reopened read-only with embedded scripts disabled. It contains these actual pose actions: **Hand Relaxed, Hand Fist, Hand Flat, Hand Point, Hand Spread, Hand Spread2; Eyemask Relaxed, Eyemask Closed, Eyemask Default; Mouth Default, Mouth Ff, Mouth Oo, Mouth Uu, Mouth Opensmile, Mouth Teethsmile**. The source head also contains smile, brow, lip and jaw correctives, and the rig has eyelid/lip/brow controls. These are source-art capabilities; they are not present in the compiled runtime face.

[Inference] Recommended correction order after accepting the device idle:

1. Review a 10-second creator idle from front/three-quarter/profile: normal resting arms, uncrossed stable feet, wrist/palm orientation, eye direction and neutral mouth. Then capture walking start/stop/turn, sit/stand and wave while moving. Check mesh intersections and contact throughout each clip, not just joint bounds.
2. Bake controlled **authored Snow** facial poses onto the retained creator geometry: relaxed eyelids, independent left/right blink, subtle smile/brow response, then a small speech set. Preserve iris/eyelid contact and transfer the same expression definitions to near LODs. Do not substitute arbitrary eye scaling or a generic moving jaw for expression work.
3. Author Snow-native neutral-hand, open-wave, point, thumbs-up and phone-grip clips using all thirty digit joints. Export through the actual 55-bone mapping rather than the CTL mitten intermediate. Validate phone contact against the actual prop and confirm each gesture returns to a relaxed hand.
4. Add a masked gesture/hand layer where motion must coexist with walking; connect gait speed to movement and validate the resulting foot contact. Unity 6.3 supports [animation layers and masks](https://docs.unity3d.com/6000.3/Documentation/Manual/AnimationLayers.html), [Humanoid IK](https://docs.unity3d.com/6000.3/Documentation/Manual/InverseKinematics.html), and [imported blend shapes](https://docs.unity3d.com/6000.3/Documentation/Manual/BlendShapes.html). These are implementation routes, not already-delivered features.

This audit changed only this progress document. No Unity/source/atlas edits, new art batch, Rain adaptation, accounts or spending occurred.


## Atlas defect corrected and canonical bundle promoted — 2026-10-05

The G/H device review exposed shoe-blue panels over the left calf. This was a compiled atlas error, not authored clothing: the retained source-material render showed clean denim. The shoe mesh had **1,620 edit-hidden faces / 1,602 edit-hidden vertices**, which still rendered/exported but were skipped by edit-mode Select All / UV packing. A 1024² raster check measured 5,314 shoe/pants overlap samples and 653 shoe/shoe samples.

- Compiler now reveals only copied geometry before UV packing. A whole-mesh 2048² interior-triangle check found a tiny folded hair UV corner too; relaxing that target-only coordinate by about 2.8 texels retained the original source texture UV and geometry. The final creator bake has **zero sampled overlapping interiors**. All three maps were rebaked from real source materials.
- Creator positions, polygon topology, weights and all bone matrices are **exactly identical** to the prior delivery. The original source files remain untouched. Old canonical bundle is archived at `art/character-candidates/unity-snow/archive/pre-calf-correction.zip`; candidate evidence remains in `unity-snow/calf-quality-candidate/`.
- Full back review also exposed reduced scalp/nape separation. Source scalp and nape positions are now protected; the far tier additionally retains lower-neck and garment-opening rings. Forcing 12k after those protections visibly distorted the model and was rejected. Final counts are **43,478 / 21,005 / 15,436 triangles**, **22,639 / 11,060 / 8,187 Blender vertices**. Far-tier device cost remains to be measured.
- Inspected actual front/back/both-side/face views, all-tier calves and bent knee, and final far-tier front/face/wave. The blue patch, nape gap and exposed skin triangle below the far collar are absent in those final renders. Far hair/eyes/cloth/soles are visibly simpler in enlarged inspection; LOD2 is not a creator substitute.
- All **17 fresh-FBX checks per tier** pass, with 55 shared bones, maximum four normalized weights, one skinned mesh/material, exact shared bind/rest matrices, and maximum tested geometry round-trip delta 1.42e-6 m. Detailed hand surface/UV samples survive the reductions.
- The reduced UVs have zero cross-part overlap samples. Local within-part folds remain after QEM reduction: **120 sampled texels in LOD1 and 947 in LOD2**, recorded in `lod-atlas-uv-validation.json`. Those tiers reuse the clean creator bake; local texture distortion remains a simplification limit. Do not describe every reduced UV triangle as mathematically non-overlapping.

Coordinating agent inspected the corrected creator and final far-tier back, authorized promotion, and confirmed the creator imports with all 54 mappings / 44 clips / pose+orientation assertions passing. Corrected **all three FBXs + all three maps** are now frozen in canonical `art/character-candidates/unity-snow/exports`, with matching blend/manifests/checks. Parent is building device revision I. This agent did not edit Unity.

Visual evidence: `unity-snow/{calf-correction,whole-look-review,lod-back-review,lod-bent-knee-review,lod-comparison,lod-face-review,lod-wave-review}.jpg`. Full exact hashes: `unity-snow/artifact-manifest.json`. Canonical LOD1 SHA256 `1e1b42fc466b0957800d706523c589542952ed57dc0bcae247bf70ab9a56ead5`; LOD2 `2ce0488087e0d56ece50e13b7df38ba0db7ddfb3518269580cd3a7511ce90c16`.

## H device idle: pose/orientation corrected, animation quality still provisional

Viewed actual [H creator screenshot](screens/quality-review-20261005h/quality-review-20261005h/creator.png). Arms are down and the model faces the camera, so the F canonical-reference failure is no longer visible there. However, **both hands visibly hold a C-shaped curl and the feet are spread broadly**. This is not the reviewed relaxed Snow hand/body pose and should not be called production character animation. The face is still static and wide-eyed; there is no installed authored blink/gaze/expression system.

Fresh read-only sampling of the actual `client/Assets/Art/animations/loco_idle.fbx` at frames 1, 20, 38, 60 and 76 measured ankle X separation **0.56339–0.56348 m**, compared with hip-joint X separation **0.20779 m** and shoulder X separation **0.39365–0.39671 m**. Thus the broad stance already exists in this legacy source clip; it is not evidence that the corrected Snow mesh proportions are wrong. The two coarse `index_01` source mitten controls are rotated about **78.04°** throughout those samples. Measurements and exact source hash: `art/character-candidates/animation-audit/h-idle-source-measurements.json`.

The source Humanoid mapping still lacks all digit chains. Its coarse finger transform channels do not establish a deliberate thirty-joint Snow hand pose. [Inference] The exact contribution of Unity's default finger-muscle values/reference pose to the visible curl still needs a runtime `HumanPose` finger-muscle capture; it was not measured in this read-only Blender audit. Do not claim the current Snow fingers are receiving the source's authored independent digit curves.

[Inference] Recommended next animation pass: author a dedicated relaxed creator idle with feet near pelvic width, a slight natural turnout/weight shift, neutral wrists and Snow's authored relaxed hand shape. Use the existing thirty digit joints on a hand layer, then validate the transition into and out of open wave/phone grip. Keep the current body clips available for movement while replacing the creator's presentation pose deliberately. Capture ten seconds front/three-quarter/profile and walking start/stop/turn with actual foot contact; the current still image cannot establish loop/contact quality. Bake authored relaxed eyelids/blink and subtle facial expressions in a separate reviewed pass. No late export or Unity edits were made for this diagnosis.


## Dedicated relaxed idle delivered — 2026-10-05

Parent authorized correcting the visible H idle immediately as a **separate animation**, with model/maps/LODs frozen. One new Snow-native six-second breathing loop is delivered at `art/character-candidates/unity-snow/animations/SnowAdult_RelaxedIdle.fbx`, with the same 55-bone rest/bind hierarchy and explicit rotation keys for all thirty digit joints. Source ankle width is 0.172976 m versus hip-joint width 0.170425 m. Neutral wrists and progressively curved, open fingers replace the reviewed broad/clenched pose in the source proof. Spine/chest/neck motion is subtle; feet and hips stay fixed. No facial animation was added.

The first armature-only export was correctly **rejected** by fresh-import validation because its posed Model transforms became the imported rest pose. A frame-zero T-pose reference improved this but did not exactly match the canonical skinned FBX matrices. The final clip FBX therefore includes the unchanged creator mesh as a skin-bind reference; Unity references only the AnimationClip. All **13 checks** now pass: canonical rest matrix delta0, 13 sampled animation skinning matrix deltas at most2.51e-6, exact loop closure, all30 digit curves present and nonconstant, and unchanged canonical six-file import bundle. Across all181 source frames, foot-joint drift is0 and evaluated soles remain at numerical zero.

Final FBX: **2,302,380 bytes**, SHA256 `f0684daf3901de9c1871c10bd09734e7feff98261e6ab069506567117625034a`; take `SnowAdult_Rig|Scene`, frames1–181 at30fps,6seconds. Exact mapping, source blend, pose/hand/body/ground proof and reports are in the same animation directory. Actual front/side/back/three-quarter and hand closeups were inspected. Root/importer agent received explicit frozen handoff; Unity's raw digit bindings and Humanoid finger muscles must pass before replacing `loco_idle`. **Device idle acceptance remains pending**, and the rest of the legacy gesture/facial gaps above are not silently solved by this one clip.


## User-tab audit and distinct second character — 2026-10-05

The user requested inspection of all open Sketchfab tabs and a varied cast, not duplicated Snow bodies. Root supplied six actual model URLs plus the unfiltered `man` search tab; no dedicated car tab was observed. Each public model API response and exact publisher thumbnail was saved and visually inspected. Full facts/unknowns/activity recommendations: `art/character-candidates/user-tabs-audit/README.md`, with `observed-tabs-contact-sheet.jpg` and download hashes.

- Coat, Broadway city block, drone and printable wings have Standard licence metadata and no public free-download flag. Root's actual coat page confirms19.4k triangles, a humanoid/PBR description, NoAI badge and no Download button. The Broadway metadata's100 faces are an image-preview carrier, not real city-pack geometry; official Fab confirms the paid17-building/road/prop pack but fetched price was unavailable.
- Razor Crest and Manhattan Bridge are CC-BY downloadable entries, with333,245 and1,404,978 API faces respectively. The recognizable Star Wars ship's underlying franchise rights are unverified. Bridge preview has visibly rough photographic city surfaces; archive format, imagery provenance and mobile budgets are unverified. Neither was downloaded as game geometry.
- [Inference] Useful CT activity directions are detailed social streets/cafés/offices, landmark photo/collectible trails, optional drone checkpoint races, and original licensed vehicles serving as destinations or rides. These are scoped ideas, not implemented gameplay or approval to import franchise/paid assets.

Rain is now being converted as a **distinct authored female source**, under the existing verified Blender Studio CC-BY4.0 licence. Raw sources and all canonical Snow mesh/maps/LODs/idle files remain untouched. New work lives in `rain-adult-production/`: independent1.696m adult geometry,55-bone rig with30 real digit joints, original detailed face/hands/outfit/ponytail. Rain's non-thumb source chains include metacarpals; runtime proximal/intermediate/distal correctly use source joints2/3/4. The retained creator has76,974 triangles and40,013 Blender vertices. Hair was bent downward in the copied geometry after the side view exposed the source's horizontal rest ponytail; secondary hair dynamics are still absent.

The first compiled visual review was **rejected despite17 successful Blender FBX numeric checks**: eye textures read the atlas UVs and baked white, and collar slivers remained. Compiler/source corrections are in progress; there is deliberately **no `bundle-ready.json` and no clearance for Unity import** yet. Initial alternate world tiers measured32,568/23,734 triangles with full source hand positions/UVs and protected facial/neck regions; these are pending final geometry and visual review, not accepted performance numbers.

Root explicitly expanded this agent's ownership to the new `client/Assets/Scripts/Editor/RainCharacterImport.cs` and `.meta`. That helper is written and root reports successful compilation. It creates Rain's own canonical Humanoid reference/Avatar, preserves the shared controller and Snow importer, binds each LOD by bone name/rest matrix, and registers only the complete Breeze look (`skin_tone3|head_rain|hair_rain_ponytail|top_sleeveless_white|bottom_denim_fitted|shoes_canvas_ochre|acc_scarf_sky`). It requires an all-pass frozen bundle report and exact SHA256 for all3 FBXs/3maps. Localization/backend acceptance remain root-owned; no individual-part customization is claimed. No Unity process or ADB was run by this agent.

## Distinct Rain / Breeze bundle frozen for Unity import — 2026-10-06

The rejected Rain checkpoints above are superseded by the reviewed, frozen bundle in `art/character-candidates/rain-adult-production/`. **`bundle-ready.json` now exists, all checks pass, and root received explicit import clearance.** This completes the art export handoff; Unity/device quality and performance are still separate pending gates.

- Final creator **76,974 triangles / 40,013 Blender vertices**; alternate world LOD1 **41,416 / 22,027**, LOD2 **34,817 / 18,695**. All three use one skinned mesh/material, the same 55-bone rest/bind hierarchy, 54 Humanoid mappings, thirty real digit joints and at most four normalized weights. Original detailed hand positions and UV samples are unchanged in both reduced tiers.
- Explicit source UV routing corrected the white-eye bake; actual blue eyes and source face/hand/outfit textures are visible in final renders. The scarf target UV uses coherent source-seam islands. Copied collar clearance and downward ponytail adaptation are retained. Final creator atlas measured zero overlapping interior texel-centre samples and zero degenerate UV triangles at 2048²; this is a raster check, not proof about all subpixel overlap.
- The earlier lower world budgets were also **rejected visually**: preserving detail while forcing those targets collapsed remaining arm/cheek/neck geometry, and top inner-wall reduction intersected the outer cloth. Final tiers keep a higher measured quality floor and remove only hidden inner top faces in the reduced copies. Full creator geometry/maps remained byte-identical through LOD derivation. Far-tier shoe/hair/rim simplification remains visible under enlarged inspection; these are hero tiers, not accepted crowd budgets.
- Reviewed actual creator front/back/side/face/hand, all-tier silhouettes, and creator/far-tier wave and knee-bend poses. Final evidence: `rain-character-review.jpg`, `rain-world-lods.jpg`, `rain-deformation-review.jpg`, plus full renders under `renders_compiled/`, `renders_lods/`, `renders_deformation/`. These are Blender evidence, not device screenshots or gameplay animation acceptance.
- Fresh FBX import passes **17 checks per tier**. LOD-to-creator rest matrix delta is 0; largest tested rest/gesture geometry round-trip difference is 1.20e-6 m. Creator is 1.695623 m tall with sole at zero; world tiers preserve zero soles. Three actual-source 2048² maps are base colour sRGB, tangent OpenGL +Y normal, and linear metallic/smoothness (R metal, A 1−roughness).

Importer entry point: `CTLife.EditorTools.RainCharacterImport.ImportRainAll`, defaulting to the frozen `rain-adult-production/exports` directory. Root previously compiled the dedicated helper successfully. It registers only the exact complete Breeze look and retains the shared controller. The initial Snow-native relaxed idle requires Rain-specific review of fingers, stance, feet, orientation and transitions; no Rain-native idle or facial animation is claimed. No Unity/ADB process was run by this agent.

SHA256: creator `58adffe37196a8500ad4b9bc43fc2d1ca9dbf8355eecaf44fabb5810cd4f0996`; LOD1 `354ea9811cb2a7e6dc6ed1208a1c7841f0d4f694f4f2b90e3748ae872e66728b`; LOD2 `5f3ff61f6d274cda56ab15c065f003c57050e9991248f9aaeba2bcad1e854d7a`. Full six-file gate and 52-file supporting evidence manifest are in the bundle. README records exact mappings, axes, materials, licence/attribution, reproduction, rejected checkpoints and outstanding limitations. Raw sources and canonical Snow remain untouched. Exports/maps/blends are held frozen during root's import/build.

### Native Rain import and attribution/proposal handoff

Root reports native Rain import passed and development build L started. During that build, only authorized documentation was changed: `art/LICENSES.md` now records actual Rain/Breeze Unity usage and derivative modifications; `docs/fe-contract-proposals/rain-starter-look.md` contains the exact seven-value Look and six new non-skin item entries. The proposal remains **unapproved** and none of those six IDs exists in the imported backend item snapshot. JSON syntax, item order and slots were checked against the dedicated importer; no backend/contract/snapshot files were changed.

`art/character-candidates/rain-runtime-credits-draft.md` provides exact offline Rain notice text, localized title/summary keys, the additional `AssetCredits.Build()` entry and bundled-notice paragraph for root's post-build source window. Those runtime changes are pending; build L is not claimed to contain them. All six frozen Rain export hashes were rechecked unchanged. This follow-up ran no Unity, ADB, render job or runtime source edit.

## Build-L Rain material rejected; required cloth correction — 2026-10-06

Actual device `quality-rain-20261006l/touch/rain-creator-body.png` and `rain-creator-face.png` show the scarf changing from dark metallic teal to bright green between body/face framing. Skin remains brown; the scarf is the material defect. Successful native import did not establish acceptable device shading. Shared Snow idle also leaves Rain's hands against/inside her thigh silhouette; root assigned a separate Rain-native idle to the contract/animation agent.

Two material causes were verified. The original `MAT-rain.scarf` source shader uses Metallic=0.4. The delivered packed PNG's scarf metal channel has median1.0: Blender5.1.2 float-image PNG saving divides RGB by alpha, treating packed smoothness as opacity. A separate numerical probe reproduced input R=.4/A=.25 reloading as R=1/A=.250004, even with CHANNEL_PACKED selected. Local installed URP `LitInput.hlsl` confirms `_METALLICSPECGLOSSMAP` reads metallic directly from texture RGB; `_Metallic=0` is only used without the map. Linear import/alpha-as-data settings were already correct and do not repair incorrect stored RGB.

The separate frozen `art/character-candidates/rain-cloth-correction/` candidate changes only scarf UV interiors plus an8-pixel gutter: metal0, smoothness=min(previous,.4), preserving the roughness texture with a cloth floor of.6. It uses an explicit linear RGBA16 writer; fresh Blender readback differs from quantized input by at most5.96e-8. All21,556 scarf interior pixels read metal0; other covered parts and G/B channels are unchanged. The independent(.4,1,0,.25) probe now round-trips correctly. Base-colour, normal, meshes, rigs, UVs and LODs are unchanged. Original six import files remain intact and are archived in `rain-cloth-correction/archive/rain-pre-cloth-original-six-files.zip`.

Reviewed actual matched before/after body/face renders: the corrected scarf reads as matte light-blue cloth with visible folds. `visual-review.json` and `map-validation.json` both bind to corrected map SHA256 `0f2453768d5928787833dd5f49b13d4d96343cc0b30ed52b140cbe16a6cfdb56`. This is a Blender material gate; device face/body/rotation acceptance remains pending.

Root explicitly authorized new `RainClothImport.cs`/meta and the dedicated Rain importer hook. `CTLife.EditorTools.RainClothImport.ApplyCloth` verifies both review reports and current file hash, copies only the corrected map, synchronously reimports it, retains its GUID/material reference and checks linear independent-alpha settings. Non-preflight `RainCharacterImport.ImportRain` now applies the overlay after copying historical source files, so later full imports require this correction too. Future full Rain compilation uses dielectric scarf settings and the corrected packed-data writer. The full compiler was not rerun. Root received frozen handoff and owns Unity compilation/import/device verification; this agent ran no Unity/ADB.
