# Character revision: source-based customization and personal avatars

## Implemented in source

- Dex/Nova have four fitted hairstyles each, exported from the preserved Hermes UBC split meshes. Only hair changes; head/eyes/brows/body/clothes/feet are unchanged. Every style includes Hero and three world LOD tiers. The original source blends and original whole-body FBXs remain untouched.
- Hairstyle IDs remain slot 2 of the existing seven-ID look. The optional hair-color suffix survives geometry changes. `CharacterPaletteSelection.IdentityLook` identifies the original outfit; `SourceLook` identifies the selected geometry without color overrides.
- Snow/Rain's 24 existing outfits have semantic masks derived from the preserved cast part-ID maps, using their exact shipped SlotUV atlases. Eye/lash/mouth areas, accessories, shoe soles and hardware are excluded from adjacent skin/cloth masks. Normal, metallic, smoothness and emission textures stay bound to the original authored maps.
- `TryChangeOutfit` carries independent supported colors to a different outfit of the same person. It rejects a change to another person. UI ownership remains with the coordinator.
- A prepared, accepted personal avatar can be instantiated by `AvatarFactory.TryCreateLikeness` and displayed transactionally through `AvatarPreview.TrySetLikeness`. The world actor remains the logical movement/seat root, with the animated generated model below it. `VenueSession` reads the separate `PlayerProfile.LikenessJobId`. Missing personal assets do not create a stock substitute.

## Transparency finding

Rain's material is opaque URP Lit with alpha 1. The creator explicitly faded the entire rendered model image to opacity 0.35 during selection changes; the coordinator removed that effect. No alpha was indiscriminately stripped from materials. A graphics regression samples opaque torso pixels in the real Snow/Rain preview at several rotations and two animation states, while verifying that the background stays transparent.

## Validation recorded so far

- Blender 5.1.2 exporter checked triangle caps and normalized bone influences for each new tier.
- A fresh independent Blender import checked all 12 hairstyle FBXs: mesh/rig count, exact triangle counts, UVs and weight bounds passed. Evidence: `ct-life-evidence/character-customization-20261011/hair-reimport-validation.json`.
- Coordinator's first Unity hairstyle import exited 0; generated prefab, palette, registry and import metadata were copied to the source checkout.
- Portrait inspection found the temporary portrait Entry inherited a 180-degree default. Explicit zero yaw is now set; the coordinator’s portrait-only rerun exited 0, and the corrected front-facing portraits were copied back and visually reviewed. The underlying meshes did not have an orientation defect.
- All 48 generated human mask PNGs match their manifest hashes. Bindings deduplicate identical masks to 16 unique textures, reducing their loaded/packaged texture footprint without downsampling. Source PNG total is approximately 0.68 MiB. The coordinator’s Unity human-mask import exited 0; generated materials/catalog/import metadata were copied back. Graphics tests and device acceptance are still pending.
- No Unity/build/device process was launched by this subagent. Full gameplay acceptance is owned by the coordinator.

## Import and test entry points

1. `CTLife.EditorTools.CharacterHairStyleImport.RunBatch` (first import) or `.BakePortraitsBatch` (portrait-only rerun).
2. `CTLife.EditorTools.HumanCharacterPaletteImport.RunBatch` (requires existing shared UBC palette catalog).
3. `CharacterPaletteTests`: style/color independence, exact identity/prefab resolution, foreign IDs, 24 human outfit bindings, wardrobe color carry and original physical-map retention.
4. `CharacterPaletteRenderTests`: original UBC plus Snow/Rain world/preview palette equivalence; shader channel isolation.
5. `DistinctCharacterPreviewTests`: opaque torso/transparent background and personal-avatar failure rollback, in addition to podium/framing coverage.

## Scope still requiring proper art integration

- Dex/Nova source libraries have one fitted garment geometry set each. Color variants are not presented as additional outfits.
- Snow/Rain source libraries contain buzz/locs/afro (and Rain long) hair meshes, but the shipped 24 outfits bake a selected hair geometry and atlas. Independent hair across every outfit should use a modular runtime assembly path; exporting 72+ extra complete bodies would increase mobile asset and memory costs. This part is not represented as complete.
- Generated avatars for remote players need an authoritative realtime appearance contract; current work covers the signed-in player's accepted asset. No remote wire field is fabricated.

## Current API references

- Unity 6.3 `ModelImporter.avatarSetup`: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/ModelImporter-avatarSetup.html
- Unity 6.3 `PrefabUtility.SaveAsPrefabAsset`: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PrefabUtility.SaveAsPrefabAsset.html
- Tangent/normal transformation was checked against the installed URP package `Shaders/LitForwardPass.hlsl` and `ShaderLibrary/SurfaceInput.hlsl`; the palette shader preserves the same normal-map unpacking and tangent sign rules.

## Bounded continuation inventory — 2026-10-11

Read-only source inventory; no assets, runtime code or imports changed during this pass. File existence, recorded part contracts and the preserved UBC object inventory were checked. Cross-body garment fit is **not** established by sharing a skeleton name.

| Person | Preserved geometry available now | Concrete next operation / dependency |
| --- | --- | --- |
| Snow | `art/cast/export/parts/m/{hair,hair_buzz,hair_locs,hair_afro}.fbx` plus matching `_Hero.fbx`, BaseColor/Normal/MetallicSmoothness/Mask PNGs. Exact rig: `SnowAdult_Rig`, 55 bones. | Assemble one hair-free Snow outfit with each of the four hair parts on its own original rig, preserving its face. Prove all rotations and idle/walk/run/sit before extending to the other 11 outfits. The current whole-look prefab includes hair, so adding another hair mesh on top would be wrong. |
| Rain | `art/cast/export/parts/f/{hair,hair_buzz,hair_locs,hair_afro,hair_long}.fbx` and the same Hero/maps set. Exact rig: `RainAdult_Rig`, 55 bones. | Same assembly proof with the five Rain parts. Check long hair against scarf, shoulders and coat while sitting; enforce the existing hood/hair occlusion contract. |
| Runner | Four split parts at `art/cast/v2/export/bases/hero_assassin/parts/hero_assassin__{head,top,bottom,feet}.fbx`. No independent hair export or alternate fitted garment in its manifest. Original preserved source: `art/raw/sketchfab/h01_cyberpunk_assassin_girl/source/infiltrator.fbx`. | First bind the existing v3 top/bottom/shoe tint masks to the current texture, preserving the multicolour suit/accent. Independent hair requires inspecting and separating the original FBX's head/hair surfaces; its presence was not verified in this bounded pass. Do not advertise a hairstyle yet. |
| Cipher | Same four-part set under `hero_balaclava`. Original `art/raw/sketchfab/h13_cyberpunk_character/cyberpunk_character.glb` has separate head, mask, chest, eyes/cornea and leg meshes; no named hair mesh. | Existing material-region masks can support clothing/skin colour after a render check. Keep mask and eyes untouched. A new haircut requires an actually fitted hair source and scalp clearance; there is no alternate outfit or haircut already fitted to this person. |
| Hazmat | Same four-part set under `hero_hazmat`. Original `art/raw/sketchfab/h06_elite_hazmat_operator/scene.gltf` contains one mesh/material. | Integrate reviewed suit and shoe masks first. Its v3 metadata's `hair` tint is not evidence of hair geometry: no independent hair part exists, and the material pass explicitly removed the falsely classified skin mask. Unmasking or a haircut needs real art work. |
| Dex / Nova | `art/cast/v3/export/bases/ubc_{m,f}/parts/` contains one top, bottom and feet geometry set per person. Four hairstyles each are already imported. Dex also has a preserved `ubc_m__facial_Hair_Beard.fbx` (606 Hero / 303 LOD0 triangles), not yet offered. | Add beard only after a face/jaw and animation check. Additional **outfits require fitting**, not changing their skin/body/head to a donor character. Start with one garment set and retain the UBC head, hands, body proportions and skin masks. |

Snow/Rain part contracts are in `art/cast/export/parts/{m,f}/parts.json` (70 / 75 total parts, including body-region hiding requirements). Original editable libraries remain at `../ct-life-evidence/cast/families/{m,f}/{base,wardrobe,lods}.blend`; exporter is `art/cast/scripts/cast_parts_export.py`. The original hair and scalp-derived pieces retain Blender Foundation **CC BY 4.0**; procedural locs/afro/long additions are recorded as mixed derivatives. Keep Snow/Rain attribution on the assembled result.

For Dex/Nova extra garments, the local **CC0 Quaternius** donor libraries are real: `art/cast/v3/export/families/umm/umm.json` has 10 top/bottom/feet sets (hoodie, tee, suit, punk, swat, astro, worker, hiker, farmer, beach); `.../umw/umw.json` has 9 (tee, suit, dress, punk, tactical, cyber, worker, hiker, hood). Corresponding FBXs exist under each `parts/` directory; original source paths in both manifests exist. These sets are compatible within their own recorded families, **not proven fitted to UBC**. Several donor tops include donor skin/arms, so replacing the entire top would alter the person: extract cloth regions, fit to Dex/Nova, transfer weights, preserve the recipient's exposed body, then regenerate masks/LOD tiers. Their original editable family blends remain at `../ct-life-evidence/cast-hermes-20261010/v3/families/{umm,umw}.blend`; UBC originals are in its `bases/{ubc_m,ubc_f}.blend`.

The three Sketchfab heroes remain **CC BY 4.0** with the exact author/source notices in their `art/cast/v3/export/bases/<id>/<id>.json` and `art/LICENSES.md`. Their v3 material maps use the hash-pinned v2 geometry; do not substitute v3-relative paths for v2 part paths. Their existing four split pieces are body sections of one outfit, not four outfits.

Runtime dependency: `ExactModularAvatar` already validates explicit base IDs, complete geometry roles, bind-pose equivalence and tint bindings. It does not yet provide these Snow/Rain catalogs, automatic body-region hiding, accessories, or the seven-ID network mapping for arbitrary parts. The next implementation should start with one verified modular Snow/Rain outfit plus the finite hair choices, then wire its accepted IDs through creator → request → backend → world. Importing every possible complete-body combination first is unnecessary. None of these remaining operations is represented as finished customization.
