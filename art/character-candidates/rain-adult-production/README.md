# Rain / Breeze — frozen Unity import bundle

**Historical six-file source bundle, frozen 2026-10-06.** Fresh Blender FBX checks passed and native Unity import subsequently passed. Build-L device review rejected the scarf material. The required [one-map cloth correction](../rain-cloth-correction/README.md) is now applied by the import helper; the original metallic map alone is not accepted. Geometry remains frozen; device animation/material/performance acceptance is still pending. This is a distinct downloaded Rain character, not a Snow recolour.

| Tier | Triangles | Blender vertices | File |
|---|---:|---:|---|
| Creator | 76,974 | 40,013 | `exports/RainAdult_Creator.fbx` |
| World LOD1 | 41,416 | 22,027 | `exports/RainAdult_LOD1.fbx` |
| World LOD2 | 34,817 | 18,695 | `exports/RainAdult_LOD2.fbx` |

Every tier has one skinned mesh, one material, the same 55-bone bind/rest hierarchy and at most four normalized influences. The 54 explicit Humanoid mappings are in `unity-humanoid-map.json`; Root is the extra non-Humanoid bone. Thirty independent digit joints retain real source hand detail. Rain's non-thumb source metacarpals are not mistaken for proximal finger joints: runtime joints 1/2/3 use source joints 2/3/4.

Creator height is 1.695623 m, sole Z=0 in Blender (expected Unity Y=0), object scale 1. FBX uses metre scale 1, `axis_forward=-Z`, `axis_up=Y`, FBX_SCALE_UNITS. Reference geometry is exported in rest pose; the review images use diagnostic poses. Do not add an arbitrary 180-degree visual correction. Unity must build Rain's own canonical T-pose/Humanoid Avatar and sample normalized shared clips.

## Maps and materials

The three 2048² RGBA16 PNGs were baked from actual retained source materials and textures:

- `RainAdult_BaseColor.png`: sRGB.
- `RainAdult_Normal.png`: tangent normal, OpenGL +Y; import as Normal Map.
- `RainAdult_MetallicSmoothness.png`: linear, R=metallic, A=1−roughness, G=1, B=0.

Use URP/Lit, opaque, white base tint. All LODs reuse this atlas UV layout/material; no tier-specific recolour or rebake. The creator atlas measured zero overlapping triangle-interior texel-centre samples at 2048², zero degenerate UV triangles, 24.46% sampled coverage. This raster test is not proof against all subpixel overlap. Reduced meshes reuse interpolated atlas coordinates; mathematical no-overlap is not claimed for every reduced triangle.

## Actual evidence and export checks

- `rain-character-review.jpg`: final creator front/back/face/hand.
- `rain-world-lods.jpg`: all three final tiers.
- `rain-deformation-review.jpg`: static open-wave and bent-knee proofs.
- `renders_compiled/`, `renders_lods/`, `renders_deformation/`: full-resolution actual renders.
- `export-validation.json`: 17 creator checks pass after fresh FBX import.
- `lod-export-validation.json`: 17 checks per reduced tier pass, shared bone rest matrix delta 0; source hand positions and UV samples unchanged. Tested pose geometry round-trip error is at most 1.20e-6 m.
- `bundle-ready.json`: exact six-file SHA256 gate checked by the importer.
- `artifact-manifest.json`: frozen supporting files and proof hashes.

The first compiled white-eye bake and lower 32k/23k reductions were rejected visually despite successful numeric checks. Explicit source UV routing restored eye textures. Scarf seams were unwrapped coherently; copied collar geometry has up to 6 mm clearance. Reduced garment copies remove only the hidden inner top wall while retaining outer fabric and opening rims. Larger final budgets preserve arms, face, neck, hands, ankles and shoes. These are two hero-character tiers; no crowd capacity or 60 fps claim is made.

## Import handoff

The dedicated `client/Assets/Scripts/Editor/RainCharacterImport.cs` compiled in the coordinating agent's build. It accepts:

```text
-executeMethod CTLife.EditorTools.RainCharacterImport.ImportRainAll
-ctRainSource /Users/mac/Documents/codes/projects/ct-life/art/character-candidates/rain-adult-production/exports
```

It verifies the six frozen hashes, creates Rain's own canonical Humanoid Avatar, imports 2048 ASTC 4x4 maps, creates `Assets/Art/characters/authored/Rain/Breeze.prefab` (full creator preview) and `BreezeWorld.prefab` (three tiers with 0.28/0.08/0.02 transition heights), and registers only the exact complete look below. LOD renderer bones bind by name to the main rig with matching rest matrices. Existing Snow and shared controller assets are not rewritten by this helper.

```text
skin_tone3|head_rain|hair_rain_ponytail|top_sleeveless_white|bottom_denim_fitted|shoes_canvas_ochre|acc_scarf_sky
```

Label key: `creator.look.breeze`. Backend acceptance/localization remain coordinating-agent responsibilities. Individual items are not separate interchangeable wardrobe assets.

The initial animation path retains the current shared controller, including Snow's native relaxed idle. That clip's successful Snow validation is not Rain animation acceptance: inspect Rain hands/stance/soles, forward walking, idle-loop contact, sit/stand, turns and gestures in Unity/device. No separate Rain idle was authored in this bundle.

## Limits and licence

The face remains static: no installed blink, gaze, visemes, facial expressions or body/face creator. Jaw exists but is inactive. Hair/scarf secondary motion is flattened to Head/body weights; the copied ponytail is bent downward from the original horizontal rest. Source skin subsurface and hair anisotropy are approximated by ordinary baked PBR. Enlarged far-tier shoes/hair/rims show simplification. Full runtime animation quality, memory/frame time and crowd budgets remain to be measured.

Source: [Blender Studio Rain v3](https://studio.blender.org/characters/rain/v3/), [official source archive](https://studio.blender.org/download-source/files/ee/a7/eea73e55dba1cea31c09848df6a794b2-4.zip), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

Credit: **Rain Rig (CC) Blender Foundation | studio.blender.org**. CT Life derivative changes: adult head/leg proportions, runtime rig/weights, copied ponytail/collar correction, source-material atlas, alternate world reductions and export. Raw `../raw/rain/` source files remain untouched. Publisher source-download and archive hashes are retained in the character candidate licence/download ledger. Canonical Snow mesh/maps/LODs/idle remain untouched.

## Reproduction

Blender 5.1.2, downloaded embedded scripts disabled. Scripts under `../scripts/`: `build_rain_adult.py`, `compile_rain_unity.py`, `derive_rain_world_lods.py`, `validate_rain_unity.py`, `validate_rain_world_lods.py`, `review_rain_compiled.py`, `review_rain_lods.py`, `review_rain_deformations.py`; `rain_review_sheets.swift` labels actual renders. Source-material, compiled and all-tier blends are retained here. Rerunning production scripts changes the frozen deliverable and requires new validation/hashes/review before import.
