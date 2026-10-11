# Tactical authored-glove recovery candidate — 9 October 2026

**Unselected art candidate.** Both hands now use the original DanlyVostok glove geometry and finger weights instead of the prior rigid palm/thumb boxes. This does not approve the tactical base for Unity, gameplay animation, or release. All 154 original base files, the original GLB, and the preceding floating-cap exclusion candidate remain unchanged.

## What was recovered

The original GLB contains `Object_94`, mesh `GlovesLowPoly_gloves_0`, material `gloves`: 2,400 vertices with weights on both hands, forearms, and all 30 finger joints. The source geometry is inverted below the floor and does not match the source rest skeleton. Source coordinates were checked against the raw GLB accessor to within 0.77µm; this is present in the file, not an inferred missing-hand problem.

One similarity transform, fitted to 20 authored weight-transition rings across both hands, aligns the complete glove mesh. Its scale is 0.73639948, orthogonal determinant −1, translation and full matrix are in `source-hand-map.json`. The fit has 7.55 mm RMS / 15.75 mm maximum ring-centre error. These surface-ring centres are anatomical fitting witnesses, not exact skeletal joint centres. **This is a measured alignment candidate, not recovery of an exact original bind transform.** The negative determinant required reversing face winding. No vertices were individually snapped, reshaped, invented, or reassigned to a different finger. Existing normalized source weights pass through the established CTS adapter. No weapon or floating-cap geometry was used.

The prior reviewed cap exclusion is retained. The original glove-drop and placeholder-hand options are disabled only in this wrapper's temporary tactical specification. The shared spec and adapter were not changed.

## Evidence and checks

Evidence root: `ct-life-evidence/claude-continuation-20261009/cast-hands/` (sibling of the project).

- `source-probe.json`: source positions, components, mappings, material identity, original evaluated pose and REST.
- `alignment-analysis.json`: pinned source and 20-ring whole-mesh fit.
- `candidate-1/`: paired prior/candidate open hands, candidate synthetic grip, and body previews.
- `candidate-1/receipt.json`: **failed author receipt, preserved**. It compared pre-normalization CTS data with the previously saved normalized rig. Geometry export and previews finished before that assertion.
- `candidate-1-verification/saved-rig-comparison.json`: fresh, read-only comparison of the two final saved blends. **All 65 bones and object matrices exactly equal.** No rest transform or hierarchy was changed.
- `candidate-1-verification/validation_v2.json`: separate fresh import of all 15 candidate FBXs. **15 pass, 0 fail, no source drift.** This does not change the original global validation's 8 failures.
- `candidate-1-verification/fbx-finger-data.json`: all serialized cluster matrices/weights finite; index/weight lengths match; all 30 weighted finger clusters persist in Hero and each of the three crowd tiers. This is not a Unity bindpose proof.

Combined base tiers: Hero 11,275 / LOD0 5,637 / LOD1 2,816 / LOD2 1,125 triangles. Only one tier may render at a time. Gloves are part of `top`; accessory triangles are additional. Each tier retains the established one-material/one-UV layout. The candidate's authored/imported counts agree; no duplicate-count exception was used to pass it.

## Visual and motion limits

The close-up open views show separate thumbs and four fingers replacing the box ends. Both synthetic grip views show actual digit deformation. Their thumb remains splayed and the bent finger surfaces form uneven, angular folds; this is not accepted as a natural closed grip. The simple grip is intentionally a stress pose and is **not** an accepted natural fist, contact pose, or gameplay animation. The whole-body REST previews crop the spread hands; use the explicit close-ups for this comparison.

The existing CTS terminal finger references remain questionable: the source's unweighted finger4 helper inverse-bind matrices point to the origin, and the adapter's distal tails inherit metre-scale lengths. They were left intact so this candidate preserves the current CTS rest exactly. A native own-Humanoid mapping and real idle/walk/run/sit/hand-contact check must resolve that limitation before selecting this base. Cuff fit and low-LOD fingers also need actual moving views. No working cast, runtime catalogue, wire ID, palette, or backend record changed.

## Provenance and modification notice

“Game Ready low poly character tactical” by DanlyVostok, CC BY 4.0:
https://sketchfab.com/3d-models/game-ready-low-poly-character-tactical-342501b0c84843a3a418319c31ea26fa

Source SHA256: `e25e0ac0520ef669c324af2de88b9cb614dd01eeef8d6ed7db917f5f6f682f20`.
Current licence verification and earlier adaptation provenance remain under `art/cast/v2/provenance/`. CT Life modifications for this candidate: previously reviewed isolated cap removal; whole-glove measured alignment and reflection winding correction; established CTS remapping, atlas baking and LOD pipeline. Original source GLB is unchanged.

The author script used for the failed run is pinned in evidence as `candidate-1/author-script-frozen.py`. The current wrapper corrects only the comparison stage to final-saved vs final-saved and refuses to overwrite the existing candidate. It has not been rerun. Existing output is supported by the independent saved-rig comparison and fresh FBX validation above.
