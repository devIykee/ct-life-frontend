# Vincent world candidate — frozen for native review

This isolated derivative keeps the production creator and its original LOD bundle unchanged. It is ready for a separate native import experiment, **not final near/far quality or crowd-performance acceptance**. The frozen creator remains the character-creator asset.

| Candidate | Triangles | Source vertices | UV/normal split estimate | Weights | Facial shapes | Maps |
|---|---:|---:|---:|---:|---|---|
| WorldNear | 16,557 | 13,727 | 20,326 | maximum 4 | five, default 0 | three 1024² |
| WorldFar | 6,801 | 5,198 | 8,429 | maximum 2 | none | three 512² |

Both have one skinned mesh, one material, UV0=`AtlasUV`, and the same 55 named bones and bind matrices as the frozen creator FBX. All 30 digit joints retain weighted vertices. Far legitimately has some rigid one-bone vertices; maximum 2 is the limit, not a requirement to manufacture a second weight.

## Import changes

- Use `exports/VincentAdult_WorldNear.fbx` and `VincentAdult_WorldFar.fbx` only for world LODs. Keep the 76,772-triangle creator and its preview unchanged.
- Use the existing Vincent own Avatar/skeleton. `world-export-validation.json` checks exact bind-matrix equality against the frozen creator FBX. `unity-humanoid-map.json` is an unchanged copy of the 54-entry mapping. Rig=`VincentAdult_Rig`; mesh names=`VincentAdult_WorldNearMesh` and `VincentAdult_WorldFarMesh`. FBX axes are −Z forward /Y up, unit scale 1.
- These tiers have independent UV layouts and therefore need their own material and map triplet. Do not point them at the 2048 creator atlas. BaseColor is sRGB; Normal is linear tangent+Y; MetallicSmoothness is linear, R=metallic and A=smoothness. The independent raw 16-bit data channels have a readback check. Only the authored buckle is metallic; shoes/cloth/skin are not.
- Near keeps `BlinkLeft`, `BlinkRight`, `Smile`, `BrowUp`, `MouthOpen`, each exported with default 0 and nonzero surface displacement. Far intentionally omits all five, along with hidden teeth. Adapt the importer’s all-tier-five-morph requirement; preserve the creator/near requirement. Never use far for close conversational/creator views.
- Use 4-weight skinning near and 2-weight skinning far, while keeping the shared 55-bone Animator. No animation frame skipping or controller changes are included.
- Preserve native validation of skeleton, bounds, animation, hands, skinning and materials. Source-side checks do not replace native checks.

## Evidence and limits

The fresh Blender FBX gate passes triangle/UV signatures, normalized weights, all 55 bone names/parents, exact shared/frozen-creator bind matrices, geometry under eight diagnostic poses, explicit zero shape defaults and actual nonzero near facial deformation. Round-trip surface error is under 1µm. Source-to-FBX foot-joint translation differs by about 2µm, within the 10µm export tolerance. Both new atlases have zero sampled interior overlaps at 2048² checking resolution. Atlas checks are raster checks, not mathematical proofs of subpixel packing.

Near retains source weights exactly at retained vertices. Far’s two-weight reduction reaches 11.361mm maximum under the diagnostic poses; relaxed-pose 99th percentile is 0.170mm, with six vertices exceeding 5mm. The curled-finger pose reaches 2.202mm. These are authored stress poses, not native clips. The rest sole is 0.078mm near /0.982mm far above the source ground; no character/root rescale or offset was introduced.

`renders/city-160px.png`, `far-64px.png` and `distant-32px.png` compare actual geometry at equal projected heights. Four columns are creator, previous far, new near, new far. `scrutiny-front/back.png` and `stress.png` deliberately enlarge the tiers. Face/hair/glasses and body colour survive the intended sizes; enlarged far shows coarse collar, belt and facial detail.

**Near shoe and buckle lighting remains a native-review concern.** Focused `shoe-normal/flatnormal/albedo.png` shows dark edging persists without the normal map; unlit albedo shows a continuous surface. The cause is coarse geometric/vertex-normal lighting, not the baked normal texture or a demonstrated open material boundary. `belt-*` shows the same coarse buckle reflection and a visibly simplified far belt edge. Do not label the near tier finally accepted before actual game-lighting views.

## Memory and performance

[Estimate] Based on an 80-byte split vertex plus 16-bit indices, near geometry is 1.65MiB and far 0.68MiB. A dense five-shape position/normal/tangent allowance for near is 3.49MiB; far has no shape data. Actual Unity packing/sparsity may differ. `world-resource-audit.json` includes exact assumptions and counts.

[Estimate] At ASTC 6×6 with complete mip chains, all three near maps total 1.79MiB and far 0.45MiB. These are **additional** atlas sets if the 2048 creator remains resident in the same LOD prefab; they do not automatically reduce total texture residency. They do reduce sampled texture size on distant renderers. The whole active renderer uses one material, not three draw materials.

The far tier is 84.0% fewer triangles than the previous 42,630-triangle far tier. This is not a 60fps result. 62 all-far characters alone are 421,662 triangles, before the environment, above the spec’s 100–150k scene target. Actual visibility/crowd density and the ten-minute hot-device gate still need measurement. No performance acceptance is claimed.

## Source and reproduction

[Vincent v1, Blender Studio](https://studio.blender.org/characters/vincent/v1/) — [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Credit: **Vincent (CC) Blender Foundation | studio.blender.org**. Adult adaptation, source-material conversion, atlas packing, world simplification and skin-weight reduction are CT Life modifications. Raw source and all frozen Snow/Rain/Vincent production files remain untouched.

Scripts in this directory derive the meshes, bake their atlases, render comparisons and validate exports. They use the already verified offline meshoptimizer 1.3 and xatlas 0.0.11 scratch tools recorded under `../male-source-audit/`. They are build tools, not runtime dependencies. Do not rerun them into this frozen candidate during native import; make a new candidate directory for any change.

`world-ready.json` hashes the eight exports, mapping and evidence. Its `all_pass` means the stated source/export/resource checks pass; native/device acceptance is explicitly pending.
