# Rain weight-only correction candidate — 2026-10-06

**Frozen; native identical-phase comparison and visual review passed. Recommended for device integration; canonical promotion/device acceptance belongs to the coordinating agent.** The canonical Rain source, geometry, textures, idle and registry were not changed.

The movement agent's actual Unity `BakeMesh` walk/run surfaces exposed long triangles from ankle/shoe regions. Matching their atlas UVs back to the frozen creator found 16–43% `Head` influence at the four sampled ankle vertices. Whole-mesh inspection found a larger hip/ankle defect, including one shoe vertex with 52.39% Head weight.

## Cause and correction

The original Rain meshes retain old `DEF-COR-Hip_*` and `DEF-COR-Ankle_*` vertex groups whose bones **do not exist** in the current source rig. Blender's original Armature modifier ignores these groups. The old conversion accepted every `DEF-*` prefix and sent unmatched groups to `Head`. Four tiny stale forearm groups were also wrongly accepted and mapped to Forearm.

`weighted-source-trace.json` audits all **622 weighted group/part occurrences**, identifies **25 stale deform-group names**, and finds no valid weighted source deform bone lacking a target. `DEF-Spine4` occurs on the unused `body_nomask` object; it is not a shipped Head-weight defect. Source mask/side/shape-control groups are explicitly distinguished from actual deform bones.

- Creator: replay the original evaluated source weights, discard groups with no actual deform bone, normalize and retain at most four influences. Source part vertex ordering is checked against the old creator weights before any mutation. The largest replay discrepancy is **0.00001096 weight**, consistent with the old compiler's small-weight pruning; the assertion budget is explicit.
- LOD1/LOD2: remove invalid Head influence from jeans/shoes and renormalize the remaining anatomical weights. Tiny stale forearm deltas are interpolated from the creator onto the reduced body topology.
- No geometry, topology, UV, corner-normal, material, texture, object origin or bone rest change. No source corrective blending is invented for groups that were already ignored by the original source rig.

| Tier | Triangles | Vertices with erroneous Head removed | Other corrected body vertices | Total changed vertices | Removed Head-weight sum |
|---|---:|---:|---:|---:|---:|
| Creator | 76,974 | 2,685 | 46 | 2,735* | 444.66354 |
| LOD1 | 41,416 | 1,255 | 43 | 1,298 | 202.21704 |
| LOD2 | 34,817 | 953 | 39 | 992 | 153.69076 |

\* Creator also restores four shoe weight distributions affected by the old top-four/pruning step. Count changes are measured from actual exported weights; weight sums are sums across vertices, not a percentage of the character.

## Frozen deliverables

- `exports/RainAdult_Creator.fbx` — SHA256 `fe304b9c38d1f4c2eb3f918f147e641d0d7583bded97a4a25dea65f1687add7f`
- `exports/RainAdult_LOD1.fbx` — SHA256 `2c4c4ee8ce7b690bda8419ffea40c4bbe6d5dc34cbeb347c776d1c7421c035c9`
- `exports/RainAdult_LOD2.fbx` — SHA256 `299332b08d5dbd039f547a332f41be9e55c1238ab94e11f8d8900e7b71ac7384`
- `RainAdult_WeightsCorrected.blend`: all three corrected meshes, unchanged55-bone rig `RainAdult_Rig` / bone `Root`.
- `archive/rain-before-weight-correction.zip`: exact prior source/export files and hashes, including the previously required cloth-map overlay where available.
- `weight-correction-validation.json`: frozen-input hashes, geometry/UV/normal digests, exact rest matrices, source replay and per-part change counts.
- `fbx-export-validation.json`: fresh original/candidate FBX checks, all ten checks pass for each tier.

The fresh-FBX tests compare identical rest triangle/UV multisets, vertex counts and rest hierarchies/matrices; max4 normalized weights; absence of Head influence below1.15m; head-only rotation leaving the lower body exactly unchanged; and rest/bent-ankle-and-hip/head-turn surface equivalence between the candidate blend and reimported FBX within10micrometres. These checks do **not** replace the native identical-phase walk/run render gate.

## Integration boundary

Coordinating agent imports these into a **separate diagnostic folder first**, preserving Rain's own valid Humanoid mapping/T-pose procedure and existing clips/cadence. The movement agent's `RainWeightComparisonProbe.cs` compares four phases, two states and all three tiers. Runtime material must retain the accepted cloth-map correction from `../rain-cloth-correction/`; the historical metallic atlas is still rejected. No new maps are supplied by this weight patch.

If accepted, future full imports must apply this weight correction after the historical six-file copy, just as the cloth overlay is mandatory. The historical baseline must not silently restore known bad weights. A future full art regeneration must filter source groups by actual source-rig bone existence and `use_deform`, and fail on an unmapped valid deform bone instead of using a generic Head fallback.

Same licence as Rain: **Rain Rig (CC) Blender Foundation | studio.blender.org**, https://studio.blender.org/characters/rain/v3/ ; https://creativecommons.org/licenses/by/4.0/ . Changes: source-faithful skin-weight filtering and normalization,2026-10-06. No new third-party asset was downloaded for this correction.

## Native visual gate completed

Coordinating agent ran the separate native comparison. The movement agent and character agent inspected `../locomotion-quality-20261006/renders-weights/rain-run-before-after.png`, `rain-walk-before-after.png`, and the LOD1/LOD2 after-run/walk sheets. The creator ankle spikes and hip pull are absent after the weight correction; reduced-tier ankle/hip silhouettes also remain clean. Native bind matrices and measured joint poses are identical, with UV/index topology unchanged. The movement audit measured creator lower-body maximum edge length189mm before versus34mm after; >100mm edge samples279→0 over eight poses. This establishes the targeted defect correction under those sampled poses, not a general production-animation or device-performance pass.
