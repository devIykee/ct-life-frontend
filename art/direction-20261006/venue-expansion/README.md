# Venue expansion candidates

Proposal only. Start with `floorplans.svg` and `scene-plans.json`; these are composition/placement specifications, not rendered gameplay. `anchor-contract.json` preserves exact current provisional FE anchors and source hash. `coverage.json` is the source audit; `serialized-geometry-audit.json` is uncullled geometry evidence.

Existing asset provenance is in the environment manifest and `art/LICENSES.md`: Kenney Furniture Kit, Quaternius Ultimate Home Interior/Sci-fi Essentials, existing project-authored modules and their recorded textures. No new download or license assumption was introduced. Candidate import must copy materials/meshes as needed; never tint the shared atlas or save all dirty assets globally.

After root's native stability gate: create separate SpacesStudio/PerpPit candidate folders; preserve root/snapshot/registry/ProjectSettings bytes around stage and bake; validate all unchanged IDs/transforms and NavMesh reachability; compile, render landscape entry/seat/board views; then test genuine room occupancy/audio/feature bindings. Do not promote incomplete gameplay as active solely because the art looks finished.

## Implemented candidate builders (not imported)

`VenueExpansionCandidate.cs` now implements both plans with shared candidate-only material, geometry, navigation and protection utilities. This file remains outside Assets. Source compilation passed against the installed Editor assemblies; see `builder-source-gate.json`. The isolated harness emitted one CS1701 warning for its Newtonsoft netstandard2.0 reference against netstandard2.1. No native staging, baking, visual review, device run or two-client test has occurred for these builders.

| Entry point | Output |
| --- | --- |
| `CTLife.EditorTools.SpacesStudioCandidate.BuildCandidate` | `Assets/Art/Candidates/SpacesStudio20261006/SpacesStudio.prefab` |
| `CTLife.EditorTools.SpacesStudioCandidate.BakeCandidate` | Same prefab, isolated Lighting folder and BakeReceipt |
| `CTLife.EditorTools.PerpPitCandidate.BuildCandidate` | `Assets/Art/Candidates/PerpPit20261006/PerpPit.prefab` |
| `CTLife.EditorTools.PerpPitCandidate.BakeCandidate` | Same prefab, isolated Lighting folder and BakeReceipt |

Only root may copy this source into the Editor folder when native work is idle. Keep this proposal directory in place: builders read its immutable `anchor-contract.json` and locally authored `Textures/` assets. A changed snapshot hash or anchor set stops staging. Stage and bake use separate idle Unity batch processes, with external original-file hashes taken **before Editor startup** and checked **after exit**. No registry promotion is performed.

The internal guard snapshots canonical Timeline dependencies, all manifest source model dependencies, their meta files, the snapshot/manifest, live venue registry, package manifest/lock, and ProjectSettings under a unique `/tmp/ctlife-{venueId}-{phase}-...` folder. It records source hashes, only saves candidate assets explicitly, and restores changed protected bytes on failure without importing after restoration. Startup imports occur before this guard, so the root's external hash comparison is still required. Do not call ProjectSetup, SaveAssets or SaveScene as part of this procedure.

Spaces has 30 individually anchored chairs plus the host podium, a flush stage surface at the common ground level, a framed acoustic backdrop, continuous window recess, side conversation pockets and forked entry paths around the immutable front-row center seat. Seven baked lights establish warm stage practicals and cool window fill; the existing venue lighting component supplies one directional light at runtime. Perp has eight chair/desk/monitor stations, paired service ribbons, a high rear identity board beyond the walkable limit, side racks and lounges, five baked lights, and a flush navigable witness circle.

The added backdrop fins, skirting and floor graphic are basic architectural trim; primary furniture/wall/floor geometry remains sourced from the existing licensed manifest. The three PNGs are new static typography/idle graphics authored locally for the candidate, with no prices, audience counts, activity assertions or fake live ON AIR state. Fonts are rasterized from installed Arial; no font files are distributed. Their hashes and provenance are in `Textures/provenance.json`.

Source-level checks verify all literal model IDs resolve to existing model files, proposal snapshot hash matches, and no global asset/scene save call exists. Actual stage receipts will provide placed source counts and path checks. The earlier `scene-plans.json` triangle sums are proposal estimates; builder trim, recess greenery and lighting fixtures add geometry and must be counted from the actual staged receipt.

Native acceptance must inspect: every exact anchor reachable, full avatar seating pose and chair direction, side-lounge/stand collision clearance, readable landscape entry composition, no podium wall occlusion, baked lighting and copied materials, camera transition between the new venue and Timeline, and measured crowd budgets. Seats intentionally leave the synchronized foot anchor free of a solid nav box; a native pass must assess accidental chair walk-through and add seat-safe collision geometry without moving anchors if needed.

No new host/hand/voice permission or fictional-market actions are implemented. Use existing room sit/stand/proximity-voice hooks for the initial visual/occupancy review; bind actual Spaces/Perps behavior only through the existing server contract in separately owned frontend work.
