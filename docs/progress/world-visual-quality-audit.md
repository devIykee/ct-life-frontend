# Timeline Square visual audit — 2026-10-06

**Read-only audit. No world, camera, runtime, model, material, navigation or lighting file changed.** This is a ranked polish proposal for the existing city. The supplied Sims-style city direction and later colorful game/lobby references remain the brief; the three changes below do not add new game systems.

## Viewed evidence and method

- Actual Seeker entry views: [M3 city](screens/quality-audio-20261006m3/quality-audio-20261006m3/city.png) and [L city](screens/quality-review-20261006l/quality-review-20261006l/city.png). Both have a large uninterrupted brown ground area between the player and fountain. Very little planted landscape or architecture is visible in this initial view. The fountain top is cropped by the screen edge; the HUD covers additional fountain detail. These are real device captures, with offline sample data.
- Existing actual-asset previews viewed: [full square](../../art/previews/venue_timeline_square.jpg), [Timeline kit](../../art/previews/kit_timeline_square.jpg), [street furniture](../../art/previews/kit_props.jpg), and [nature kit](../../art/previews/kit_nature.jpg). These are Blender source presentations, **not device captures**. Their fuller composition shows that many useful assets already exist.
- [Generated lobby diorama](../../client/Assets/Resources/UI/Art/CityDiorama.png) was viewed as the current illustrated identity: layered planting, readable paths, small social furniture groups, and water that reads as water. It is an illustration, not a playable scene or evidence of achievable runtime detail.
- Read the actual placement/material generator `client/Assets/Scripts/Editor/VenueContentBuilder.cs`, source modelling scripts `art/pipeline/env_custom.py` / `env_config.py`, current scene prefab, local licences, manifest and original source paths.
- Independently imported twelve processed FBXs in Blender background mode, CPU only, two threads, to count actual mesh loop triangles/material slots. Counts below match the manifest. Inspection output: `/tmp/ct-world-audit-20261006/mesh-inspection.json`; runner/log beside it. No render, export, source edit, Unity or ADB operation was performed. Coordinated CPU use with the character agent; no GPU work.

**Main finding:** the first view is under-composed. Existing planting and street props largely sit outside its portrait frame. Replacing all buildings or adding more distant assets would leave that first impression unresolved. Texture scale is not demonstrably wrong: the source paving is specified as a 2m surface and the supplied tile/UV coverage is 2m. The visible problem is uninterrupted repetition and weak separation of paths/social areas, not a verified unit-scale error.

## 1. Bring two existing planted/social groups into the entry composition

**Highest impact.** Make the first walk through the square read as a landscaped social place, using the existing assets. Do not move contract seats, entrance, exit, center, crier or interaction anchors.

- Current garden groups nearest the entry sit at `(-12,-7)` and `(12,-7)`; lamps are at `x=±17,z=-11`. They are too far to the sides to contribute much to the current narrow entry frame. See `BuildGardens` and the real captures above.
- Proposed starting placement study: relocate those two complete garden groups to approximately `(-6,-3)` and `(6,-3)`, and the two entry lamps to approximately `(-4.5,-11.8)` and `(4.5,-11.8)`. These are **candidate coordinates for visual/navigation verification**, not tested safe positions. Retain a clear central walk corridor and the existing seat approaches; adjust the grouping after actual portrait captures. Do not fill the center with collision or tall foreground canopies.
- Keep the two different inspected tree silhouettes: `tree_blossom_4` and `tree_blossom_1`, each with its existing planter, flowering bush and small flower clump. Place flowers into the planted group, rather than scattered isolated on the paving. A slight change in yaw/scale can avoid bilateral cloning without adding new assets.
- Keep the current detailed `park_bench`; the 44-triangle KayKit `bench_city` was visually inspected and is a backless block. It would reduce quality for the current seated social scene despite its lower cost. The current park bench is 420/210 triangles and one material.

| Existing source, reusable as-is | Actual triangles LOD0 / LOD1 / LOD2 | Material slots per selected tier |
|---|---:|---:|
| `client/Assets/Art/nature/tree_blossom_4.fbx` | 1,256 / 627 / 251 | 2 |
| `client/Assets/Art/nature/tree_blossom_1.fbx` | 3,848 / 1,923 / 769 (manifest and viewed kit; not one of the twelve independent reimports) | 2 |
| `client/Assets/Art/props/planter_round.fbx` | 118 | 2 |
| `client/Assets/Art/nature/bush_large_flowers.fbx` | 402 / 201 | 2 |
| `client/Assets/Art/nature/flower_clump_2.fbx` | 136 | 1 |
| `client/Assets/Art/props/street_lamp_single.fbx` | 1,028 / 514 / 204 | 1 |

The two planted groups plus two lamps already total **8,472 LOD0 triangles**. Relocation adds **zero meshes, triangles, textures or material identities** to the authored scene. It can still increase **visible** geometry and alpha-tested leaf overdraw in the entry view; that requires fresh device counts. Shared materials do not mean a guaranteed single draw call. Retain current LOD groups and inspect nearby leaf edges/occlusion on Seeker.

Sources/licence: the pink trees derive from `art/raw/quaternius/ultimate_stylized_nature/glTF/MapleTree_*.gltf`; the existing [official Ultimate Stylized Nature page](https://quaternius.com/packs/ultimatestylizednature.html) explicitly links CC0, and the local pack licence was read. Planters are from the free Downtown City MegaKit, `art/raw/quaternius/downtown_city_megakit/Exports/FBX (Unity)/Prop_Planter_Single.fbx`, with local `License_Standard.txt` explicitly CC0. Lamps are from the existing CC0 Modular Streets pack recorded in `art/LICENSES.md`. No new download is needed.

## 2. Make the entry path readable with the existing paving modules

The device image spends most of its open scene area on one warm-brown texture. The procedural floor is regular and correctly tiled, but the current rectangular/border treatment does not tell the eye where to enter, meet or sit.

- Use the existing light `plaza_hexpaving_2m` to form a **4m-wide, 16m-long entry spine** from the entrance toward the clear central meeting area: replace the sixteen 2m tiles covering `x=-2..2,z=-14..2` in a placement study. Keep its geometry flush; this is surface zoning, not a raised obstacle.
- Keep brown paving for the social pockets around the fixed benches. End the spine deliberately at the meeting area instead of stretching another undifferentiated strip across the entire square. The final junction shape needs a device capture; this audit does not claim that the candidate pattern is accepted.
- Preserve the physical 2m texture scale. Do not inflate each brick to solve a compositional issue. Review moiré/contrast at the actual 0.6 world render scale before changing import filtering or adding texture detail.

Exact assets: `client/Assets/Art/environment/timeline_square/plaza_paving_2m.fbx` and `plaza_hexpaving_2m.fbx`; source modelling at `art/pipeline/env_custom.py:478`, using the existing 1K diffuse textures. Each actual FBX has **14 triangles and two material slots**. Replacing sixteen tiles changes **224 authored triangles to 224**: zero net geometry. Both surface materials already exist in the scene and `CombinePaving` merges by shared material, so this proposal needs no new texture or material identity. New draw count remains a runtime question; do not equate source tile count with submissions.

The [official Rectangular Paving page](https://polyhaven.com/a/rectangular_paving) identifies the physical width as 2m and the asset as CC0. Existing texture provenance is recorded in `art/LICENSES.md`. The current texture is a weathered photographic surface; its busy appearance is visible in the device shot. First change its **coverage/layout**, then judge whether a restrained derivative texture is still needed. No replacement texture download is justified yet.

## 3. Replace the fountain's toy-like spike with a readable water feature

The current hero prop is visibly the weakest supplied model: a large plain cylinder basin, straight support, shallow saucer and solid gold cone. On device the cyan disk looks flat and static. The viewed kit preview confirms this is authored geometry, not an import/LOD error.

- Rework the **existing owned CC0 source**, `art/pipeline/env_custom.py:429` → `art/raw/ctlife_custom/plaza_fountain.glb` → `art/processed/environment/timeline_square/plaza_fountain.fbx`. Keep the existing footprint and approved navigation clearance around its `(0,7.5)` placement.
- Remove the solid gold spike. Give the bowl a visible inset lip and use a narrow water jet/cascade silhouette to connect the upper bowl to the basin. Retain a restrained turquoise water surface; add a small bounded ripple/flow loop only after the still silhouette reads correctly. Do not solve this with many transparent particles or a reflective water system.
- Keep stone/palette and water as **two shared materials**. The water should receive its own authored highlight/ripple treatment: current `ResolveMaterial` applies the general smoothness 0.12 and disabled specular highlights to it, then only changes its color. The raised water surface correction in `RepairFountainSurface` already solves coplanar geometry; it does not supply water detail or movement.
- Have the art owner show an actual close source preview and the unchanged entrance/center camera views before accepting the replacement. The entry camera currently crops the upper feature; a better model alone cannot repair that framing. Reassess its screen visibility with the garden/path study, preserving gameplay control positions and player readability.

**Verified current cost:** 444 LOD0 / 222 LOD1 triangles, two material slots, 4.8m diameter and 2.9m high. **Proposed authoring budget, not a measured replacement:** at most 1,500 LOD0 / 600 LOD1 triangles and two materials, with a bounded simple water loop. That is at most +1,056 / +378 source triangles over the current tiers. A final mesh does not exist yet; do not report this budget as an imported asset or its expected frame rate.

A narrow primary-source search checked the [official Kenney Fantasy Town Kit page](https://kenney.nl/assets/fantasy-town-kit), which lists CC0 and a public download, but it did **not establish a better fountain model**. No pack was downloaded and no unseen model is recommended. The available owned source makes a targeted authored improvement more concrete than an unverified replacement hunt. The current sourced trees, benches, planters and café furniture are adequate for this first composition pass; wholesale replacement is not recommended.

## Acceptance order and limits

1. Apply and inspect the entry composition/path study first, with existing assets. Capture entrance, center and both side approaches at the actual gameplay camera, with HUD present; compare the same positions before/after.
2. Revalidate navigation paths to every existing spot and seat approach after relocated obstacles; regenerate navigation and baked lighting through the existing root-owned workflow. Do not silently move anchors to fit dressing.
3. Inspect fountain source and native views; then measure real visible triangles/batches and frame timing on Seeker. Existing whole-city authored totals or placeholder crowd tests are not proof of the revised visible/alpha workload.

The sample city has no live population in these images. This audit does not mistake that absence for a production NPC-count bug or propose fake residents as dressing. A fuller authored cast and backend-driven city activity remain separate ongoing work.


## Native destination content finding — M5, 2026-10-06

The coordinating agent's actual Map travel test reached Builder Hills but loaded an empty Greybox floor (`Greybox builder_hills 0spots`). The current VenueContentRegistry has only Timeline Square, with no Addressables venue entries and only Bootstrap/Spike build scenes. The other fifteen venue environments are not authored/shipped yet. Root is gating the map to identify those destinations as Opening soon while preserving direct developer fixtures. This is unfinished full-world content, not a reduction of agreed district scope. The isolated Timeline study below does not complete those venues. Evidence: `docs/progress/screens/quality-map-20261006m5/touch/map-travel-builder-hills.png` (coordinator-reported; not independently viewed by this agent).


## Revised isolated candidate and native staging — 2026-10-06

The coordinator rejected the first dead-end paving strip and rigid fountain streams. A revised study was rendered and actually inspected from the native portrait entry camera, then reviewed by the coordinator as a clear visual improvement. That is source-review approval to prepare a native candidate, not device or user acceptance.

Current artifacts: `art/character-candidates/timeline-polish/README.md`, `renders/entry-before.png`, `renders/entry-after.png`, `renders/center-after.png`, and matched fountain close-ups. The fair baseline now reproduces the native +0.04 m water-height repair in world space; earlier coplanar source artifacts are not part of the comparison.

- Kenney's official CC0 Fantasy Town Kit 2.0 was downloaded through its public ZIP, and actual meshes/previews were inspected. The candidate retains its rounded basin profile, retopologized at 24/16 radial segments; authored upper bowl/arcs/overflow/foam complete the static form. Exact hashes, source profile and notice are preserved; `art/LICENSES.md` has the new candidate-only row. Wade/AnyRPG CC0 fountain previews were viewed, but their sign-in-restricted meshes were not downloaded.
- Fountain: 1,236 / 592 triangles, two materials, 4.8 m diameter, roughly 2.47 m high; budget checks pass. The static arcs and foam remain a geometry study, not completed flowing water or audio.
- Paving: continuous 4 m routes linking all four approaches and a rounded fountain court; 42 valid triangles, one reused material, original 2 m UV repeat. Ground geometry and contract bounds remain intact. Native depth stability of the 0.008 m visual layer is pending.
- Two existing garden groups and lamps move into the entry composition. Zero net authored vegetation triangles/materials added, but visible foliage/overdraw can increase.

`TimelinePolishImport.cs` is the sole new client file owned for this stage. It consumes a frozen hash manifest, builds only under `Assets/Art/Candidates/TimelinePolish`, checks all anchors/camera values, validates geometry/UVs, creates a basin-only collider, rebuilds navigation and checks complete entrance paths to every positioned spot plus spawn/exit/center projections. The revised importer backs up canonical dependencies, registry/snapshot, metadata and ProjectSettings bytes before stage/bake, with failure rollback and a required post-process-exit hash check. The first native run exposed that the original hash-only guard could detect changes but could not restore them. It has no publish method. Root's parameterized bake helper keeps candidate lightmaps/materials/UV2/scene/receipt isolated.

Validation performed by this agent: Blender 5.1.2 exported-FBX reimport, finite bounds, zero degenerate triangles, UV completeness, exact triangle/material budgets and grounded basin; all passed. Offline Unity-reference editor compilation also passed all assemblies (Editor: 22 source files). Native import/navigation, full light bake, real Seeker visual comparison and rendering cost are still pending; root alone executes them. Full command and historical 53.6-second canonical bake context are in the candidate README.


## Failed first native stage and recovery audit

The first stage failed before bake at a canonical material hash mismatch. The save boundary wrote five generated materials and ProjectSettings. There is no pre-stage byte backup for those materials; exact historical YAML restoration is unavailable. Read-only extraction of the exact pre-stage M5 APK verified nine material-property changes: neon legacy `_Color` changed from white to green, and four foliage materials changed double-sided GI false→true and alpha-to-mask 0→1. All remaining runtime material fields, shader names, texture reference GUIDs and UV transforms match. ProjectSettings' historical delta remains unresolved; its APK PlayerSettings parser failed.

Complete evidence, reader limitations and the unapplied hash-pinned semantic restoration proposal are in `art/character-candidates/timeline-polish/stage-failure-audit/README.md`. Root owns any restoration. Revised importer source and root's scoped bake saves pass offline compilation (all assemblies, Editor23); native retry, rollback behavior, bake, Seeker appearance/navigation and performance remain unverified. This failed stage is not a successful candidate acceptance.
