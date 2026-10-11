# Timeline Square — isolated visual candidate, 6 October 2026

**Status: source study visually reviewed; first native stage FAILED before bake because five canonical materials were saved with changed properties. Revised importer compiled; semantic restoration proposal and native retry await coordinator review. Nothing is published.** See [failed-stage audit](stage-failure-audit/README.md) for exact M5 deltas, preservation limits and the unapplied restoration patch. The coordinating agent owns Unity import, navigation, light baking and Seeker validation. These renders use the actual source meshes and current entry-camera parameters in Blender; they are not native screenshots or a performance result.

## Review these images

- [Entry before](renders/entry-before.png) / [entry after](renders/entry-after.png): identical portrait camera and controlled lighting; Snow at the actual spawn `(0, 0, -14)`.
- [Center after](renders/center-after.png): same lens, pitch and yaw, following Snow at the center anchor. Shows the connected circular court and north approach.
- [Fountain before](renders/fountain-before.png) / [fountain after](renders/fountain-after.png): identical close-up camera for asset inspection.

The baseline applies the current native fountain's 0.04 m water-surface repair in world coordinates. Its original coplanar source defect is not used to exaggerate improvement. Snow now faces into the square in both comparison images. There is no HUD in these renders. Lighting, shadows, transparency and material response remain Blender approximations, not the current baked Unity result.

## Three changes

1. **Bring existing city dressing into the entry composition.** Relocate two existing garden bundles from `(±12, -7)` to `(±4.8, -4.6)` and two existing entry lamps from `(±17, -11)` to `(±3.2, -10.7)`. No additional vegetation meshes are added. The entry view now includes a blossom canopy, planter and lamp rather than mostly unbroken ground. More foliage becomes visible, so zero additional authored triangles does not mean zero additional rendering cost.
2. **Connect the paving.** Replace the rejected dead-end strip proposal with one continuous planar surface: a 4 m north–south route, a 4 m cross-route through the center, and a 5.3 m-radius court around the existing fountain. It reaches the south sidewalk, north approach and both side edges. The mesh uses the existing Poly Haven hex-paving texture at its existing 2 m physical repeat. It adds no collider or navigation source.
3. **Replace the spike fountain silhouette.** Use Kenney's inspected, CC0 rounded basin profile, retopologized at 24/16 radial segments, plus an authored upper bowl, three visible arcs above the bowl, three curved overflow streams at LOD0, and small irregular foam patches/droplets. The basin's lowest ring sits at zero. Two opaque materials avoid stacked transparent water planes. Water is static geometry in this candidate; it has no animation, particle effect, sound or reflection claim.

## Measured asset cost

| Change | LOD0 | LOD1 | Materials | Notes |
|---|---:|---:|---:|---|
| New fountain | 1,236 triangles | 592 triangles | 2 | Existing fountain: 444 / 222. Net geometry change: +792 / +370. Same 4.8 m maximum diameter; new height approximately 2.47 m. |
| Connected paving surface | 42 triangles | Same mesh | 1 existing | Existing ground remains beneath this visual surface. It sits 0.008 m above ground; native depth behavior requires verification. |
| Two gardens and two lamps | 8,472 triangles moved | Existing authored tiers | Existing | Zero net authored geometry/material additions. Camera-visible foliage/overdraw changes. |

Exact mesh measurements are in `fountain-costs.json`, `plaza-surface-costs.json` and `export-validation.json`. These numbers are authored mesh costs, not visible triangles, draw calls, GPU time or Seeker frame rate. No FPS claim is made.

## Source selection and rights

- **Adopted:** [Kenney Fantasy Town Kit 2.0](https://kenney.nl/assets/fantasy-town-kit), CC0. Downloaded from its public official ZIP on 2026-10-06, without an account. Actually inspected the supplied pack preview and both round-fountain meshes. The simple basin has 910 stone triangles plus a 46-triangle water plane in the original FBX; the detail variant has 1,486 stone plus 142 water triangles. The candidate retains the simple basin's measured radial profile and reduces its 48 radial segments to 24/16. The original OBJ, notice, hashes and modifications are preserved in `source/kenney/` and `source-provenance.json`.
- **Viewed, not downloaded:** [Wade's fountain](https://blendswap.com/blend/2537), CC0, and [AnyRPG's Medieval Fountains](https://blendswap.com/blend/26453), CC0. Their actual preview images were viewed. Useful details were recessed bowls, articulated rims, a narrow neck and a substantial lower basin. BlendSwap explicitly requires sign-in to download; that boundary was respected. Neither mesh is part of this candidate.
- Existing Quaternius gardens, lamps and environment assets retain their recorded CC0 licences. Existing [Poly Haven hexagonal paving](https://polyhaven.com/a/hexagonal_concrete_paving) is reused; it is not a newly downloaded texture.
- Snow in comparison images: **Snow Rig (CC) Blender Foundation | studio.blender.org**, [source](https://studio.blender.org/characters/snow/v4/), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The existing CT Life adult-proportion/rig/atlas/idle derivative is appended read-only for scale. No Blender Foundation endorsement is implied.

`art/LICENSES.md` has the new Kenney row. No paid, login-restricted or proprietary evaluation asset was added.

## Isolated handoff

- `plaza_fountain_polish.blend` and `.fbx`: two authored LOD meshes, material names `M_timeline_polish_stone` and `M_timeline_polish_water`.
- `timeline_polish_stone.png` and `timeline_polish_water.png`: original 16×16 four-band color atlases. Preserve point filtering / palette UVs; both are opaque.
- `timeline_plaza_surface.fbx`: a single planar mesh with the existing hex-paving material. Its UVs extend beyond 0–1 intentionally; use Repeat, not Clamp.
- `candidate-manifest-entry.json`: explicit proposal for both model IDs. Paths are proposed import destinations and do not imply those live assets exist.
- `candidate-placement.patch`: **unapplied** diff against the current venue generator. Do not apply before importing/reviewing the proposed IDs.
- `baseline-placements.json`: 102 sourced model placements parsed from the current native Timeline prefab. Paving/road loops are reconstructed from the current generator because the native prefab combines them.
- `applied-study-transforms.json`: actual garden/lamp transform changes used in the renders.
- `camera-proof.json`: pitch 35°, yaw −8°, distance 18.5 m, vertical FOV 42°, focus height 1 m, target viewport Y 0.39. The mathematical spawn-foot projection was checked at `(0.5, 0.39)` in a 720×1602 portrait render.

## Preserved layout and pending native checks

The proposed diff does not edit the contract, walkable rectangles, spawn, exit, center, interaction anchors or seat anchors. The fountain remains at `(0, 7.5)` with the existing 4.5×3×4.5 navigation exclusion. Garden exclusions move with the existing bundles; the native candidate importer adds one low-sided convex collider only around the solid basin (0–0.707 m high), and no collider for jets, paving, lamps or foliage. The ±2 m central corridor remains clear of garden exclusions and lamp bases in the source placement plan. This is a placement constraint, not a native pathfinding result.

Before live acceptance, the coordinator still needs to check:

- Imported mesh scale, UV/material response, normals and LOD selection on Seeker.
- Entry and center composition with the real HUD, both character looks and actual baked lighting.
- Ground contact and depth stability of the 0.008 m paving layer, including distance and moving camera.
- Routes from spawn/exit/center to existing seats, crier positions and interaction anchors after rebaking navigation; no unintended tap interception.
- Visible foliage/triangle/batch cost and performance using the actual device telemetry.
- Water animation and sound quality as a separate runtime presentation task; static geometry does not complete a convincing flowing fountain.

## Reproduce

From repository root, using the installed Blender 5.1.2:

```sh
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -t 2 -P art/character-candidates/timeline-polish/build_candidate.py -- --render
```

`--geometry-only` rebuilds all study geometry/metadata without running the GPU renderer. Without either option, the script rebuilds only fountain assets/metadata and the unapplied source diff. The full command also rebuilds connected paving, native-camera comparisons and its manifest proposal. It never writes `client/`. Coordinate GPU use with the character artist and root first.

Iteration record: the first strip and cone/plate fountain were rejected; V2's numerous falling streams still looked structural. Current V3 reduces the overflow count and places the small arcs above the bowl. The first paving run hit Blender 5.1's changed `tessellate_polygon` return type; its locally installed docstring confirms index tuples, and the candidate now consumes those indices. A LOD1 budget assertion caught 606 triangles; removing distant micro-foam geometry reduced the final result to 592. These are source checks, not native acceptance.


## Native candidate staging — revised after failed first execution

`client/Assets/Scripts/Editor/TimelinePolishImport.cs` compiled against installed Unity 6000.3.25f1 references; all assemblies passed the offline editor compilation. No Unity or ADB command was executed by this agent.

The importer:

- Verifies `frozen-manifest.json`: both FBXs, both palette PNGs, source validation/provenance/build script, exact canonical Timeline prefab, contract snapshot and existing generator. Changed bytes stop staging rather than silently rebase the reviewed study.
- Copies only into `Assets/Art/Candidates/TimelinePolish/`; clones the canonical prefab and preserves its venue/district IDs, camera values and every spot anchor.
- Removes inherited lightmap bindings and stale contact cards from the candidate copy. The candidate is deliberately **not ready for runtime review until its new bake succeeds**.
- Sets explicit palette import settings and materials, validates imported scale/ground contact/UVs/triangle counts, removes any importer-created LOD groups and creates one fountain group at the existing prop thresholds (0.02 / 0.002). No cross-fade.
- Adds one 24-sided, 96-triangle convex physics hull around the basin; no jet or paving collider. This collision mesh is separate from rendered triangles.
- Rebuilds navigation from the same contract ground boxes, existing garden exclusions at their updated positions, and the existing fountain exclusion. It adds the nav data temporarily at a 10 km test offset to isolate validation from any open editor venue, checks the known spawn/exit/center projection, and requires complete entrance paths to every positioned registered spot. The temporary instance is removed afterward.
- Captures durable byte backups and hashes of the canonical prefab dependency closure, relevant registry/snapshot, metadata and all ProjectSettings before stage/bake. Clones inherited materials, saves candidate assets only, and restores captured bytes on failure with a complete mismatch receipt. Post-process-exit hash verification is required. The original failed implementation kept hashes only and did not roll back; see the failure audit. No registry entry is published.
- Writes `BuildReceipt.json` under the candidate directory. Staging records `staged_unbaked_not_published`; baking accepts only that exact validated prefab/source manifest and records the baked prefab hash, bound map count and `baked_isolated_not_published`.

The coordinator exposed `VenueLightingBuilder.BakeVenue(prefabPath, outputDirectory)`, retaining the old canonical wrapper. The candidate calls it with its own prefab and `Assets/Art/Candidates/TimelinePolish/Lighting`, so mesh UV2 copies, materials, scene, lighting settings, maps and receipt all stay in the candidate tree. The source generator and old environment source files are not overwritten.

After reviewing/restoring the verified M5 material fields and retaining the unresolved ProjectSettings caveat, run these **sequentially, coordinator only**, with no other Unity editor/bake using the project:

```sh
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath client -executeMethod CTLife.EditorTools.TimelinePolishImport.BuildCandidate -logFile /tmp/ct-timeline-polish-stage.log
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath client -executeMethod CTLife.EditorTools.TimelinePolishImport.BakeCandidate --ct-lightmapper=gpu -logFile /tmp/ct-timeline-polish-bake.log
```

Do **not** add `-nographics` to the bake: installed Unity on Apple silicon requires ProgressiveGPU. `Lightmapping.Bake()` is synchronous in the existing helper; `-quit` follows the completed bake/binding validation. Historical canonical `LastBake.txt` records 53.6 seconds, five maps, 217 bound renderers and 3,473,408 color texels. Candidate bake duration and atlas count are unmeasured. Full-venue rebaking is required because planting and lamps moved; this stage does not reuse stale lighting.

Only root may choose the later review/promotion mechanism after the candidate passes staging and bake checks. This importer has no automatic publish method. The illustrative generator patch is retained for traceability, not the preferred staging path.

Final export validation also detected two zero-area triangulation faces along collinear paving outline edges. The generator now filters them, and reimported FBX checks confirm 42 valid surface triangles, zero degenerate triangles across all three meshes, finite positions and complete UV loops. The visible render composition is unchanged by removing zero-area faces.
