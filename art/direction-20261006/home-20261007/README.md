# Own-home room: current inventory and composition proposal

[Inference] The composition below is an art proposal, not an authored or visually accepted scene. Current-source findings and proposed work are separated. Read-only audit: 2026-10-07. No Assets, registry, networking or needs behavior was changed.

## Current missing work

The missing Phase 1 behavior is local room entry, not a new backend join. `docs/spec/contract.md:183` explicitly says the player's own home is a **local scene selected by `Me.homeId` until Phase 4**. Phase 4 adds home visits; `contract/openapi.yaml:636` returns a `roomId` from `POST /homes/{ownerId}/visit` for `joinById`. The imported public venues registry does not contain `home`. Do not add it to public travel or send `JoinVenue("home")`.

The current phone `HomeApp` presents needs actions and applies only server receipts. It has no 3D entry callback. `FlowController`'s Home/HUD destination is the signed-in lobby. The distinction is intentional in the brief; the lobby should remain available. No authored home prefab/scene was found in `Assets/Art`; `VenueContentRegistry` currently registers only Timeline Square. A greybox via the existing spot registry would not constitute a furnished home.

`VenueSession.Enter(joinRoom:false)` already provides owned local-avatar/camera/lifecycle behavior, while `VenueLoader` already loads local prefabs. It has no production private-home selection by `Me.homeId`. The exact shared transition and cleanup machinery should be reused; do not create a second uncoordinated scene/camera owner.

## Exact immutable room data

The imported snapshot is pinned in `read-only-inventory.json`. Walkable rectangle: X −6…6, Z −5…5. All roots are Y0; rotations are radians.

| Full spot ID | X | Z | Rotation | Kind / declared action |
| --- | ---: | ---: | ---: | --- |
| `home.entrance` | 0 | −4 | 0 | exit |
| `home.exit` | 0 | −5 | 3.142 | exit |
| `home.center` | 0 | 0 | 0 | stand |
| `home.bed` | −4 | 3 | 0 | bed / `act_sit` |
| `home.desk` | 4 | 3 | 0 | terminal / `act_type` |
| `home.sofa` | 0 | 2 | 0 | seat / `act_sit` |

Actual home IDs are `parents_basement`, `hacker_house`, `studio` (Phase 1), `penthouse` and `marina_villa` (Phase 2). The shared spots group is `home`; it is not any one of those home IDs. Registry `rooms` values (4/6/3/8/12) are preserved data, not authorization to invent additional synchronized anchors.

The needs config allows eat/sleep/shower/touch_grass anywhere; party is restricted to specified clubs. A sink or planter is visual dressing until a deliberate UI interaction calls the existing needs action. No local needs/cash prediction, fake sleep completion, new bathroom spot ID or in-home party benefit is proposed.

## First authored composition: Parent's Basement

Recommendation: a compact lived-in domestic room, with a sleeping alcove on the left, the work nook on the right, a central sofa facing the north wall, a small kitchenette near the entrance and a screened bathroom at the southeast perimeter. Use recognizable furniture silhouettes and restrained personal details, not the Café's repeated desk rows or a display catalogue.

The coordinates below are **proposed visual bounds**, subject to native mesh bounds and the exact six-anchor navigation/camera review. They do not move anchors or claim usable contact.

| Zone | Proposed plan bounds | Composition |
| --- | --- | --- |
| Entry and clear route | X −1.25…1.25, Z −5…0.8 | Keep the entrance/exit and center visible. Low south cutaway wall only; no prop/rug lip/collider across the route. |
| Sleeping alcove | X −5.7…−2.5, Z 1.8…4.7 | Adult single bed along the west edge, low warm task light, one shelf. Place its eventual sitting edge around the exact bed anchor only after body/support measurement. A thin screen wall ends before the center corridor. |
| Work nook | X 2.6…5.7, Z 2.0…4.7 | One adult desk with source chair, laptop and a restrained pinboard; no triple trading array. Chair/support and keyboard treatment can reuse accepted Café geometry only through an explicit new home binding after pose validation. |
| Living zone | X −1.6…1.6, Z 1.2…4.7 | Navy two-seat source couch around the sofa anchor, modest television on north wall, books and a lamp at one side. Keep the usable seat orientation +Z. No table across the center approach. |
| Kitchenette | X −5.7…−3.6, Z −3.8…−0.7 | Source fridge, sink and three cabinet units aligned to west wall; one original grocery note, no invented shop or price board. |
| Bathroom | X 3.4…5.7, Z −3.8…−0.8 | Source shower/sink/toilet, matte tile patch flush with existing floor, short cutaway partition with an open route from center. Do not imply three new network interactions. |

Use warm plaster, medium wood, charcoal metal, navy fabric and muted sage kitchen units, drawing from the existing Café/Studio palette. A high north window gives basement identity and daylight; one warm desk light and one living light provide focus. Avoid neon trim, a large slogan wall, saturated yellow pillows and giant central empty flooring. Proposed initial review camera: landscape, distance 8.8 m, pitch30°, yaw−12°, FOV42°; this is a starting composition value, not a measured acceptance. Inspect at entry, center and every interaction angle before selecting it.

The existing kit sheet `art/previews/kit_home.jpg` was visually inspected. The Quaternius bed, navy sofa, desk and kitchen/bathroom family read consistently; the bright yellow KayKit seating was not selected. Exact native scale/support still requires import geometry, not the source sheet.

## Existing licensed inputs and cost

`read-only-inventory.json` pins sixteen existing source model IDs, their FBX hashes, quantities and source dimensions. The proposed source-prop set totals **10,192 LOD0 triangles**, excluding architecture, future fitted geometry and any additional original detail. This is an inventory sum, not a draw-call/GPU or whole-scene budget claim. Preserve source LODs and reuse shared material textures; never recolor the global palette material to style one room.

Publisher pages reopened on 2026-10-07 confirm CC0 for the existing [Quaternius House Interior](https://quaternius.com/packs/ultimatehomeinterior.html), [Quaternius Furniture](https://quaternius.com/packs/ultimatefurniture.html), and [Kenney Furniture Kit](https://kenney.nl/assets/furniture-kit). Their local provenance is already recorded in `art/LICENSES.md`. No additional download/package was required.

## Contact blockers that must stay explicit

- The bed spot declares `act_sit`, not a sleeping animation. The source bed is 0.722 m overall height, while the accepted Café seated support is 0.450 m. A bed mesh placed at the anchor does not make the declared action fit. Inspect its actual mattress/edge and define the intended local sleep interaction before enabling it; do not move the actor root or advertise a lying pose that does not exist.
- The existing navy couch has not been checked against either current adult seated pose. Previous Studio lounge chairs demonstrated why AABB clearance is insufficient. One shared physical support needs both bodies and exit presentation checked; do not copy Café clip bindings into an arbitrary couch.
- The desk can start from the known Café desk/chair/keyboard assembly, but the current Café profile is explicitly venue/spot scoped. Home is not automatically authorized by those receipts. Keep source geometry reuse separate from runtime motion binding.

## Bounded frontend integration proposal

1. Add an explicit localized **Go home** entry to the phone Home page through an injected callback owned by the active `FlowLifetime`. Keep the existing needs action page and signed-in dashboard available.
2. Select a private local prefab from a `HomeContentRegistry` keyed by verified `Me.homeId`; reject an unknown ID visibly rather than silently substituting the cheapest home. Keep public `VenueContentRegistry`, imported `venues` and world map unchanged.
3. Reuse the existing serialized transition/scene/local-avatar ownership through a narrow `EnterHome(homeId)` path: logical spot group `home`, selected private prefab, no realtime join or proximity voice. The load should use the same cancellation/drain/unload boundaries as the existing local scene path. Save a return destination only within the current signed-in flow; returning explicitly rejoins that public venue as normal.
4. Needs still call `Api.NeedsAction` with the current UI request ownership. Local home animations are presentation only. The current null-channel `GoSit` records an intent but does not produce authoritative confirmation; do not pretend it establishes a playable local chair. Local action presentation needs an explicit mode/adapter after the physical fit is accepted.
5. Phase 4 visits later use the imported `POST /homes/{ownerId}/visit` → `joinById` → `HomeState` contract. `HomeState` contains ownerId/homeId/players, but current realtime message headings document venue rooms; actual home sit/stand/voice semantics require backend contract review at that phase. No endpoint or wire behavior is invented here.

The research agent/root own lifecycle implementation. This inventory does not authorize a competing home session or change API/economy behavior.

## Next art work, after Café/Studio queue

Build one isolated Parent's Basement room candidate with these six immutable anchors and current licensed sources, then native entry/center/bed/desk/sofa views with both adult looks, real floor contact and complete approach paths. Freeze chosen proportions/material family before producing Hacker House/Studio variants. Those other Phase 1 homes and Phase 2 residences remain required scope; a single decorated basement does not complete Home. Rent/visits stay Phase 4 and no room is registered from an unreviewed candidate.

## Frozen candidate source and root-owned native procedure

`ParentsBasementCandidateA.cs` now implements the first isolated art stage. It compiles with the actual installed Editor references; only a unique scratch DLL output is allowed in its response file (no Bee/refout/PDB/generated output). `native-import-manifest.json` gives exact source and external pin-file hashes. Research independently reviewed save scope/identity, and its path-set observation was addressed: protection now compares both the complete existing physical file set and hashes under Assets/ProjectSettings/Packages, excluding only this candidate output and its meta. It never restores unrelated bytes or clears their dirty flags. No native stage has been run by this agent.

The source uses exact six anchors, three furniture approaches plus one exit standing approach, actual LOD0 mesh colliders and separate padded nav exclusion bounds. It preserves a clear middle route. Bed/couch/desk remain **unaccepted physical interactions**: model colliders are not reparented/tagged as approved seats and no Café profile is attached. The declared contract animations remain untouched. Home lifecycle must not synthesize a server acknowledgment or infer a sleep pose from this asset.

The implemented prop set includes the specified source family plus two floor lamps, one source decorated shelf, a cup and a rear lightwell. The earlier10,192 figure is the initial selected inventory, not the larger final build sum; `StageReceipt.source_lod0_triangles_manifest_sum` will record the actual completed placements. All21 source model IDs, texture/manifest files are pinned in `build-source-pins.json`. Clean candidate plaster uses an isolated material, and no global palette/texture is tinted or edited.

Root queue:

1. Import the one frozen Editor helper after verifying destination absence and all manifest hashes. Native stage: `-executeMethod CTLife.EditorTools.ParentsBasementCandidateA.BuildCandidate`. Use root's normal protected source snapshot and idle batch launch. If native fails, preserve the candidate folder/receipt; source refuses accidental regeneration over it.
2. Inspect StageReceipt: exact six roots/declarations, four distinct standing approaches, six complete paths with strict XZ sampling, true MeshCollider count, source placement budget. Source bounds/path success does not replace real walking/pose review.
3. Bake in a separate graphics-enabled idle process: `-executeMethod CTLife.EditorTools.ParentsBasementCandidateA.BakeCandidate`. No `-nographics`; candidate-only map output. Compare protected path set and bytes after actual Editor exit.
4. The existing Editor review can capture the **local art scene** without inventing a public venue: `-executeMethod CTLife.EditorTools.FrontendReview.Launch -ctlife-review 1 -ctlife-venue home -ctlife-reviewVenuePrefab Assets/Art/Candidates/ParentsBasement20261007A/ParentsBasementA.prefab -ctlife-reviewLandscape 1 -ctlife-reviewSequence city -ctlife-reviewCapture /tmp/ct-home-basement-art-a-native-20261007 -ctlife-reviewExit 1`. Use the saved Bootstrap `-openfile` launch shape, not `-projectPath` plus `-openfile`; do not set generic close-camera overrides, which would replace the authored8.8m camera. This is a compile-guarded scene override and null-channel art review, not production Home entry/return proof.
5. Research/root own the separate real phone→local Home→return lifecycle. No current builder modifies that path or registers the room. Native adult entry/center/three interaction views and actual walking/furniture review still remain.
