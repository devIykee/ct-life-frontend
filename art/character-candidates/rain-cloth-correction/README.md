# Required Rain cloth-map correction — 2026-10-06

The initial native Rain import passed, but actual build-L device captures showed the scarf changing from dark metallic teal in body view to bright green in face view. The skin itself stayed brown. This is a material defect; prior Blender/export passes did not establish correct device shading.

Source `MAT-rain.scarf` uses Metallic=0.4. In the original exported map, scarf interior metal values have median1.0. A separate2×2 numerical experiment reproduced the export fault: Blender5.1.2 float-image PNG saving divides RGB by alpha, interpreting packed smoothness as opacity; input R=.4/A=.25 reloads as R=1/A=.250004. Merely setting CHANNEL_PACKED did not change that saver behavior. The installed URP Lit shader reads metallic from the map when `_METALLICSPECGLOSSMAP` is active, so material `_Metallic=0` does not neutralize this texture.

The candidate `exports/RainAdult_MetallicSmoothness.png` changes only the scarf UV surface plus an8-pixel gutter: metal=0 and smoothness=min(previous,.4), retaining existing texture variation with roughness at least.6. Other covered body regions and G/B channels are unchanged. It uses an explicit RGBA16 linear PNG writer that never treats A as opacity. Fresh Blender readback matches quantized channel values to5.96e-8, including an independent(.4,1,0,.25) probe. Report: `map-validation.json`.

All original six import files remain byte-identical in `../rain-adult-production/exports/` and are archived together in `archive/rain-pre-cloth-original-six-files.zip`. No mesh, rig, UV, base-colour, normal, LOD, source texture or idle was changed by this correction. Original attribution remains Rain Rig (CC) Blender Foundation | studio.blender.org, CC BY4.0; this material adaptation is an additional October2026 CT Life modification.

Actual Blender front/face before-after review passed; proof files are in `renders/` and bound to the new map SHA in `visual-review.json`. The scarf reads as matte light-blue cloth in both reviewed views. Unity/device review remains pending.

## Required import path

`CTLife.EditorTools.RainClothImport.ApplyCloth` requires both numeric and actual-render review, validates the new SHA256, accepts only the known original or corrected target map, copies just the corrected PNG and performs a synchronous texture reimport. It retains the existing `.meta`/GUID and material binding and checks linear independent-alpha settings. `RainCharacterImport.ImportRain` now calls this overlay after copying the historical canonical files, so a later complete import cannot restore the known-bad scarf map unnoticed. Neither helper runs Unity on its own; the coordinating agent owns execution.

Candidate SHA256: `0f2453768d5928787833dd5f49b13d4d96343cc0b30ed52b140cbe16a6cfdb56`.

Original map SHA256: `a28ce22aa1f298e1d92c03f6b1375f2eeb501e2694f40f638280943106f96420`.

The original bundle's six-file hash gate establishes its unchanged geometry/source baseline, **not acceptance of that historical metallic map**. This overlay is required for the final Rain material. Final device body/face/rotation review remains pending; Blender comparison alone cannot establish the result under Unity reflection probes and lighting.

## Reproduction

`../scripts/correct_rain_cloth_map.py` creates this one-map candidate and channel checks; `png_data_texture.py` provides the independent-channel PNG writer. `review_rain_cloth_map.py` renders identical geometry/lights before and after. The future full Rain compiler now adapts the source scarf to dielectric cloth with the same roughness floor and uses the corrected packed-data writer; the full compiler has not been rerun and none of its frozen geometry or atlas UVs was regenerated.
