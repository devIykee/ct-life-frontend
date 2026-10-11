# Vincent / Weekender native-import candidate — 2026-10-06

**Frozen source bundle ready for native inspection. Not an in-game, animation or thermal-performance acceptance.** The creator is the recommended close-up asset. The two world tiers are visual candidates, **not final mobile/crowd budgets**.

Source: [Vincent v1](https://studio.blender.org/characters/vincent/v1/), [CC BY4.0](https://creativecommons.org/licenses/by/4.0/). Required credit: **Vincent (CC) Blender Foundation | studio.blender.org**. Original anonymous download, source page/license evidence and SHA are in `../male-source-audit/`. Original source, production Snow and production Rain are unchanged. This is a distinct downloaded male face/body/outfit, not a recolour.

## Import handoff

- `exports/VincentAdult_{Creator,LOD1,LOD2}.fbx`: one skinned mesh/material each, own55-bone rig **VincentAdult_Rig**, root **Root**,30 individually weighted digit joints, at most4 normalized influences. Exact shared exported rest matrices across tiers.
- `unity-humanoid-map.json`:54 explicit HumanBodyBones-enum-to-transform mappings. Use **CreateFromThisModel** with Vincent's own proportions and validated Unity T-pose description. The source bind is an A-pose; do not interpret raw bind rotations as a Humanoid T-pose or copy Snow's Avatar.
- FBX: scale1, metre source, axis-forward **-Z**, axis-up **Y**. Source height **1.789216m**, soleZ0; root must verify Unity soleY0 and +Z character forward under normalized shared clips.
- Shape keys on every tier: **BlinkLeft, BlinkRight, Smile, BrowUp, MouthOpen**. Preserve blendshapes. They are evaluated source controls/cage/correctives baked into portable facial outputs, not a claim of a complete facial system.
- `exports/VincentAdult_BaseColor.png`:2048, sRGB. `VincentAdult_Normal.png`:2048, non-colour tangent **+Y**. `VincentAdult_MetallicSmoothness.png`:2048, **linear R=metallic / A=smoothness**, independent16-bit channels. Cloth/skin are dielectric; only the buckle is metallic. Shader map multipliers must not suppress the texture channels.
- `VincentAdult_Tiers.blend` is the current three-tier authoring file. `VincentAdult_AtlasHighReference.blend` preserves the100,256-triangle geometric reference. `VincentAdult_SourceQuality.blend` preserves the adapted original surfaces/materials before the atlas conversion. The older `VincentAdult_Unity.blend` is a high-reference compile stage, **not the final runtime tier bundle**.
- `bundle-ready.json` freezes the six export files and mapping hash. `fbx-export-validation.json` independently records final export and texture hashes. Re-importing an earlier compile stage would discard the UV/cloth corrections.

| Tier | Triangles | Authoring vertices | Fresh-FBX vertices |
|---|---:|---:|---:|
| Creator | 76,772 | 51,948 | 51,948 |
| LOD1 | 53,216 | 37,832 | 37,832 |
| LOD2 | 42,630 | 31,028 | 31,028 |

## Passed source gates and reviewed output

Fresh Blender FBX import: all55 bone names/parents, exact common tier rests, joint-position error≤0.000002027m, rotation error≤0.004361°, normalized4-weight skinning, all30 digit joints weighted, all five morphs, grounded soles, and posed/morphed surface equivalence under1µm. Head rotation leaves the body below1.30m unchanged. These checks do not establish native retarget quality.

`renders/{creator,lod1,lod2}_tier_{front,side,back,face,deform}.png` are actual final Blender renders, inspected after the local texture corrections. Creator face/hands, front/back garment coverage, shoe silhouettes and raised-arm/bent-leg pose remain identifiable. Blink/smile/mouth evidence is also under `renders/`; the source includes modeled mouth interior and teeth. The far tier has visible facets at close range and must be judged at its actual intended screen size.

The atlas gate checks **both overlapping interiors and sampling blank bake pockets**. All tiers record0 samples for each at2048 in `tier-uv-gate.json`. A reduction can pass an overlap-only check while spanning unpainted chart pockets; this caused the rejected dark cloth triangles. Local source neighborhoods were locked and regenerated. Base-map alpha was opaque in unused pixels, so alpha alone was not a coverage test. Pixel-centre sampling is not a mathematical subpixel proof; reviewed images and native inspection remain necessary.

Film cloth displacement produced distracting moiré when baked at2048. The portable normal rebake scales only shirt/pants micro-displacement to12%; original base colours, roughness, geometry and skin/face normals are retained. `cloth-normal-adaptation.json` records exact hashes/readback; the previous map remains in `archive/prior-cloth-normal/`.

## Remaining acceptance and budget work

- Root runs native own-Avatar/morph/material/retarget checks, then actual Seeker front/back/movement review. Shared Snow idle must pass Vincent's own hand/thigh and sole-clearance gate; author a separate idle if it fails. This bundle contains no accepted Vincent-native animation.
- These current world meshes deliberately retain full detailed hands, shoes and buckle. That is a costly inspection reference, not an acceptable assumption for distant crowds. Next reductions must simplify those tiny surfaces at measured world-screen sizes while retaining near face/silhouette quality. No60fps,10-minute hot1%-low≥45fps or crowd-budget claim is made.
- This is one complete look. Weekender / `creator.look.weekender` is the frontend label proposal; backend IDs/availability and modular customization remain separate work.

## Reproduction

The accepted reduction path is `../scripts/derive_vincent_meshopt.py`, not the rejected Blender-collapse experiment `derive_vincent_tiers.py`. It uses verified MIT meshoptimizer1.3 as a scratch offline tool and copies retained source position/UV/normal/skin/morph samples. Local masks are in `uv-protection-overrides.json`. Atlas packing uses xatlas0.0.11; provenance is in `../male-source-audit/`. No runtime package is added.

After derivation, run `audit_vincent_tier_uvs.py` and `validate_vincent_exports.py` with Blender `--background --disable-autoexec --python-exit-code 1`. If texture inputs are changed, regenerate/inspect the maps and refresh every hash; do not silently reuse this freeze. Root owns all Unity import, registry and device actions.
