# Café B3 social-table composition candidate

Source-compiled outside Assets. Native stage, bake, ray selection and visual review remain pending. Original B3Social and frozen B3Hits remain intact.

## Exact scope

This is a copy-on-write descendant of frozen B3Hits. The only furniture transform changes are four existing social tables and their three decorative objects each, all translated by exactly Z −0.20 m:

| Object | Old anchor-relative Z | New anchor-relative Z |
|---|---:|---:|
| home_table_roundsmall | +1.20 | +1.00 |
| cup_coffee | +0.99 | +0.79 |
| croissant | +1.17 | +0.97 |
| plant_small | +1.48 | +1.28 |

All X/Y coordinates, yaws, scales, four social chairs, eight workstations, all 17 immutable anchors and 12 authored approaches remain unchanged. Existing B3Hits truthful mesh colliders and SpotAnchor ancestry are retained. Navigation is rebuilt from the moved real table bounds; no prior NavMesh or baked lightmap is copied.

The remainder of `B3Hits-to-B3Tables.patch` is the distinct class/output name, receipt wording and protection of B3Hits output if it exists. Existing source models/materials and prior candidates are protected. `baseline-sha256.json` records the exact parent sources and runtime WorldInput baseline; `source-sha256.json` freezes this candidate.

## Evidence and limitations

Actual B3Social native body/table exports were tested at eight phases × three LODs × two looks. A −0.20 m table translation had zero tested table triangle crossings. Conservative world-Z front-body/table separation remains Snow 65.5–74.4 mm and Rain 81.3–92.4 mm. See `../terminal-cafe-b3-fit/native-social-furniture-surfaces.json`. This is sampled surface evidence, not continuous-time clearance, reachable hand contact, native placement or navigation acceptance. Social hands stay in the lap.

Standalone compilation passed against the actual allowed Editor assembly references. Only the existing Newtonsoft/netstandard CS1701 warning appears. No live Assets or Unity process was changed/launched by this work.

## Coordinator procedure

After native slot release, import only `TerminalCafeCandidateB3Tables.cs` into `client/Assets/Scripts/Editor/`. Stage with `CTLife.EditorTools.TerminalCafeCandidateB3Tables.BuildCandidate`. Output is distinct: `Assets/Art/Candidates/TerminalCafe20261006B3Tables/TerminalCafeB3Tables.prefab`.

Require the original 17 anchor coordinates/yaws and 12 approaches, complete paths, no protected drift, and the exact four-table/12-decoration delta in placements. Original B3Hits remains the first focused hit-selection fixture target; its success does not automatically accept this later moved-table composition. Repeat actual camera rays/walking on the final combined prefab. Native social contact/room camera rendering must show the new gap; do not infer a visual pass from CPU clearance.

The isolated bake method is `CTLife.EditorTools.TerminalCafeCandidateB3Tables.BakeCandidate`, but this intermediate version still contains unaccepted B2 work desks/chairs and has no centered keyboard. Do not register it as the completed Café. Merge only the subsequently accepted shared workstation into a new final output, then rebuild navigation and bake that exact output once.
