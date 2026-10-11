# Snow — corrected atlas and reviewed world LODs

2026-10-05. The calf atlas defect is corrected in the actual FBX/map bundle. Creator geometry, polygon topology, skin weights and all 55 rest/bind matrices are exactly unchanged. Raw Blender Studio source is untouched. The coordinating agent owns Unity/device acceptance; these images are Blender renders.

| Tier | Triangles | Blender vertices | Intended use |
| --- | ---: | ---: | --- |
| Creator | 43,478 | 22,639 | Creator / close inspection |
| LOD1 | 21,005 | 11,060 | Main world tier |
| LOD2 | 15,436 | 8,187 | Far world tier, not creator preview |

All three exports have one skinned mesh, one material, the same 55-bone hierarchy/rest matrices, at most four normalized influences, and a shared 2048² atlas. Height is 1.848586 m; soles are at zero. Far LOD2 is deliberately 3,301 triangles above the previous 12,135 budget: protecting the scalp, nape and collar while forcing the earlier count visibly damaged those surfaces. The final rendered revision is clean at those boundaries. The added geometry still needs device budget assessment.

## Exact import bundle

Use the three `exports/SnowAdult_{Creator,LOD1,LOD2}.fbx` files together with:

- `exports/SnowAdult_BaseColor.png` — sRGB.
- `exports/SnowAdult_Normal.png` — tangent-space OpenGL +Y; Unity Normal Map, no green flip.
- `exports/SnowAdult_MetallicSmoothness.png` — linear; R=metallic, A=1−roughness.
- `unity-humanoid-map.json` — 54 explicit mappings, plus the unmapped Root bone.

All maps are 2048² RGBA16 authoring PNGs. URP/Lit, opaque, white tint, normal/metallic/smoothness multipliers 1. FBX scale 1, forward −Z, up Y. Import source normals. Use CreateFromThisModel and the explicit Humanoid mapping, with Unity's verified canonical T-pose reference. Mesh bind transforms are not a substitute for the canonical Humanoid reference pose. Shared animation root orientation is handled by the coordinating Unity importer, not a compensation baked into this asset.

## Defect and correction

The retained shoe mesh contained 1,620 edit-hidden faces / 1,602 edit-hidden vertices. Hidden edit geometry still rendered and exported, but Select All / UV packing skipped it. Shoe islands therefore overlapped pants and other shoe pieces. A 1024² diagnostic measured 5,314 shoe/pants overlap samples and 653 shoe/shoe samples. The original detailed garment's source-material render was clean.

The compiler now clears hide flags on copied geometry before packing every face. It checks all atlas triangle interiors at 2048² before baking. One tiny authored hair UV fold was also detected: only its target-atlas corner was relaxed by 2.8 texels; source UVs and geometry stayed unchanged. All three material channels were rebaked from the real source shaders. The creator bake has zero overlapping interior samples at that resolution; this raster check is not a mathematical proof against subpixel overlap.

The back review also exposed a nape gap from independently reduced scalp/head surfaces. Both LODs now retain the source scalp and underlying nape. Far LOD2 additionally preserves the lower neck and a ring around garment openings; its head, hair and shirt allocation increased. The original detailed hands remain protected. Source normal transfer and grounded soles are retained.

The reduced UVs have **no cross-part overlaps** at the tested resolution. QEM reduction introduces local within-part UV folds (120 sampled texels in LOD1; 947 in LOD2); they are recorded in `lod-atlas-uv-validation.json`. These tiers reuse the clean creator bake, so those folds do not bake another garment onto the surface. They can produce local texture distortion and are a remaining simplification limit, especially on far-tier shoes and hair.

## Actual visual evidence and checks

- [Before/source/after calf comparison](calf-correction.jpg).
- [Front/back/both sides](whole-look-review.jpg).
- [All three backs](lod-back-review.jpg).
- [Bent-knee comparison](lod-bent-knee-review.jpg).
- [Whole tier comparison](lod-comparison.jpg), [enlarged faces](lod-face-review.jpg), [same-rig wave](lod-wave-review.jpg).

Front, back, both sides, creator face, all-tier calves/bent knees, and the final far-tier front/face/wave were inspected. The calf contamination, nape gap and exposed skin triangle below the far-tier collar are absent in these final views. Far-tier hair clumps, eyes, cloth folds and soles remain visibly simpler in enlarged inspection; this is not a closeup replacement.

All 17 creator FBX checks and 17 per world tier pass after fresh Blender imports, including identical bone rest matrices, ground, four normalized weights, hand surface/UV preservation and evaluated T/phone/wave/seated geometry. Maximum pose round-trip difference is 1.42e-6 m. `correction-validation.json` also records exact creator position/topology/weight/bone equality and byte-identical old delivery before promotion. The previous canonical bundle is preserved in [archive/pre-calf-correction.zip](archive/pre-calf-correction.zip).

These checks do not establish production animation quality. The existing legacy Humanoid clips do not drive Snow's thirty independent finger joints, and the runtime has no authored blink/expression/viseme system. Jaw remains inactive. No photo matching, body/face creator system or cast rollout is claimed.

## Reproduction

Run from the project root, with Blender 5.1.2 and downloaded-file scripts disabled:

```sh
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --disable-autoexec -t 4 --python-exit-code 1 -P art/character-candidates/scripts/compile_snow_unity.py -- --output art/character-candidates/unity-snow/calf-quality-candidate
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --disable-autoexec -t 4 --python-exit-code 1 -P art/character-candidates/scripts/derive_snow_world_lods.py -- --output art/character-candidates/unity-snow/calf-quality-candidate
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --disable-autoexec -t 4 --python-exit-code 1 -P art/character-candidates/scripts/validate_snow_unity.py -- --output art/character-candidates/unity-snow/calf-quality-candidate
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --disable-autoexec -t 4 --python-exit-code 1 -P art/character-candidates/scripts/validate_snow_world_lods.py -- --output art/character-candidates/unity-snow/calf-quality-candidate
```

**Snow Rig (CC) Blender Foundation | studio.blender.org** — [source](https://studio.blender.org/characters/snow/v4/), [CC-BY 4.0](https://creativecommons.org/licenses/by/4.0/). Adaptations: adult proportions, 55-bone game rig, grounded merged mesh, target UV atlas/PBR bake, protected world reductions. Attribution does not imply endorsement.
