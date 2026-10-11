# Timeline B2 — arrival composition amendment

[Inference] B2 is ready for coordinated native staging and visual review, not art approval. The CPU checks below address the specific framing failures found in native A/v4 and the unbaked B source. No Assets file was changed while preparing this amendment.

Source: `CityDirectionCandidateB2.cs`; exact amendment from B: `B-to-B2.patch`; standalone compiler receipt: `source-gate.json`.

Only the coordinator imports this helper when Unity is idle. Methods:

- `CTLife.EditorTools.CityDirectionCandidateB2.BuildCandidate`
- `CTLife.EditorTools.CityDirectionCandidateB2.BakeCandidate`

Output: `Assets/Art/Candidates/CityDirection20261006B2/TimelineStreetB2.prefab`. A, B and canonical Timeline remain separate. Stage and bake in separate batch processes. Use the existing external pre-startup baseline and post-exit hash comparison; the internal guard begins after Editor startup. This version protects canonical dependencies, all manifest model dependencies/meta, snapshot, manifest, live venue registry, package manifest/lock and ProjectSettings. Only candidate assets use narrow saves. There is no live registry promotion.

## What changed and why

The same full sourced models retain their B scales: 5.12 × 5.12 × 6.55 m coffee building, 4.95 × 5.81 × 6.76 m bakery, and 4.78 × 5.55 × 9.33 m east building. Height is unchanged. The west coffee address moves to (−6.4, −3.8), the bakery turns −90° at (−11.9, −6.5) to form a connected corner, and the east building moves to (6.2, −3.7). These flank a 7.65 m gap between building footprints. Upper floors intentionally exceed the close gameplay frame; the acceptance target is legible ground-floor identity at adult player scale.

Existing 1536 × 256 shop graphics now have correctly proportioned 6:1 fascia carriers. The old 4.2 × 2.2 m billboard carrier stretched them. New shallow charcoal frames contain 14 triangles each; no new building model or texture download was introduced. The angled bakery fascia sits on the forward part of its east face; a panel farther back would be hidden by the coffee wing. No secondary bakery awning crosses through that wing. The decorative high east ticker is removed from this candidate.

Three table pockets move into the arrival field: coffee (−7.2, −11.7), bakery (−10.5, −11.3), east (6.3, −10.7). Chairs retain adult-size source scale and copied calm wood materials. The tree gardens move out of the near bench sightlines; low planted boxes at (−4.9, −13.2) and (4.7, −12.6) frame the entry instead. The narrower pale promenade remains a visual route over the original plaza, not a new invisible walkable boundary. The kiosk and ATM move to (−1.8, −3.9)/(2.2, −3.9), leaving a 2.85 m clear geometric gap between their exclusions.

## CPU evidence and its limits

`source-bounds.json` contains measured LOD0 bounds from CPU-only Blender FBX imports of 13 existing assets. `audit_composition.py` reproduces projection and solid-clearance evidence in `composition-audit.json`. It uses the actual v4 native log camera: position (1.65, 5.90, −21.78), rotation (27, −12, 0), FOV 42°, 2670 × 1200; player ground target is viewport (0.5, 0.28). Camera position is rounded to the precision printed by Unity.

| Identity face | Horizontal viewport extent | Vertical viewport extent | Projected width |
| --- | --- | --- | --- |
| Coffee | 0.260–0.403 | 0.848–0.919 | 383 px |
| Angled bakery | 0.195–0.251 | 0.821–0.893 | 151 px |
| Terminal | 0.722–0.856 | 0.789–0.847 | 358 px |

All three projected face rectangles fit outside conservative masks for the current v7 HUD. Nine camera-to-face sample rays per fascia intersect none of the new building AABBs. These are geometry checks, not proof that every text pixel is unobscured or readable. The bakery is deliberately the smaller secondary address; judge its oblique lettering on the phone.

All 15 current immutable anchors remain outside the new/moved solid AABBs by at least 1.01 m (bench 6 is the tightest). Native staging still has to build the actual NavMesh and report complete paths. Existing canonical benches, animation envelopes, source awning posts, shadows and the full walking camera require native inspection. No native stage, bake, screenshot, device thermal result or live occupancy result is claimed here. Standalone source compilation passed with the same single CS1701 Newtonsoft/netstandard harness warning as previous isolated builders.

Native review: use the same landscape override as v4/v7 (`reviewCloseCamera=1`, distance 10.8, pitch 27, targetY .28), then walk to benches 4/5/6 and the center. Check text, door/readability, each seated player, passage around the low planters, the west corner joint, and entry/exit. A wider overview may document architecture but must not substitute for this gameplay-camera test. New table chairs remain decoration; only the unchanged six bench anchors advertise sitting. No fictional crowds or live business/market claims were added.

## Café district authority check

The current `contract/ids/venues.json` and imported config `ids.venues` both define `terminal_cafe` without `districtId`, `phase` or `sceneKey`. The build spec's city-map section contains an embedded-map placeholder and does not explicitly assign Terminal Café a district. `docs/progress/backend-dev-feedback.md` item 16 documents this missing contract field.

The current frontend catalog enrichment in `client/Assets/Scripts/Core/Data/Registries.cs` lines 73–105 maps `terminal_cafe` to `trenches` only when the supplied catalog has no district. The isolated Café helper currently writes `timeline`. This is a verified **client consistency discrepancy**, not evidence that the backend/spec authoritatively declares Trenches. `VenueLoader` only fills an empty prefab district; `VenueSession` passes the populated prefab district to music, while the map uses the parsed catalog. At a later coordinated correction, deriving the candidate district from the parsed current `VenueDef` would follow the same value as the map and future explicit catalog metadata. No Café source, prefab, contract or district was changed during this review.
