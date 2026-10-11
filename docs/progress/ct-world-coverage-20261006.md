# Whole-world coverage and next venues — 2026-10-06

[Inference] The user's concern is substantiated by shipped-content coverage: the registry exposes one authored destination, Timeline Square. Expanding that plaza again will not supply the different places and activities promised by the city map. The most decisive next content sequence is promote a verified Terminal Café, then deliver Spaces Studio with actual host/listener interactions, followed by Perp Pit with server-backed fictional-market interaction. Native stability and phone evidence remain separate gates.

## What is shipped versus planned

Read-only audit: `Assets/Resources/World/VenueContentRegistry.asset` has exactly one entry, `timeline_square`. Build Settings contains only Bootstrap and Spike. No catalog scene keys are present in the imported registry. `CityMapScreen.BuildGuide` correctly enables only a phase-eligible content-catalog entry or loadable built-in scene: the other fifteen catalog destinations are locked. `VenueLoader` can construct a generic greybox for direct/development entry; that is not an authored venue or proof of multiplayer acceptance.

| Exact catalog ID | Original contract IDs → positioned FE snapshot IDs | Animation anchors | Content/map state |
| --- | ---: | ---: | --- |
| `timeline_square` | 4 → 15 | 6 | Shipped authored; map open |
| `spaces_studio` | 11 → 34 | 31 | No authored prefab; map locked |
| `terminal_cafe` | 8 → 17 | 12 | Baked isolated candidate; map locked |
| `perp_pit` | 4 → 13 | 8 | No authored prefab; map locked |
| `rekt_clinic` | 6 → 11 | 6 | No authored prefab; map locked |
| `ore_refinery` | 4 → 10 | 4 | No authored prefab; map locked |
| `launchpad_tower` | 1 → 15 | 5 | No authored prefab; map locked |
| `prediction_pit` | 0 → 10 | 6 | No authored prefab; map locked |
| `builder_hills` | 0 → 0 | 0 | No authored prefab; map locked |
| `audit_row` | 0 → 7 | 0 | No authored prefab; map locked |
| `vc_row` | 0 → 7 | 0 | No authored prefab; map locked |
| `ratio_arena` | 0 → 7 | 0 | No authored prefab; map locked |
| `airdrop_farm` | 0 → 0 | 0 | No authored prefab; map locked |
| `dubai_marina` | 0 → 7 | 0 | No authored prefab; map locked |
| `cabal_lounge` | 0 → 7 | 0 | No authored prefab; map locked |
| `discord_heights` | 0 → 7 | 0 | No authored prefab; map locked |

The 22 spot sets and 16 venue catalog IDs are not aligned. Catalog IDs `builder_hills` and `airdrop_farm` have no positioned spot sets. Spot sets `airdrop_fields`, `club_liquidity`, `conference_hall`, `ct_jail`, `hackathon_house`, `home`, `sybil_barn`, `the_trenches` have no catalog entry. `home` may intentionally be a private-room route; it still is not a catalog destination. Do not silently rename district IDs into venue IDs. The spec describes 21 venues; the imported catalog currently lists 16. A contract-owner reconciliation is needed before promising the whole map.

The snapshot's positions are explicitly **provisional FE additive** `1.0.0-rc1+fe.1`. Existing original IDs are retained in every examined set. Adoption of the extra anchors and coordinates by the live server was not verified in this audit. Keep candidate anchors exact; report any server rejection rather than moving anchors to hide it.

## Actual geometry evidence and budget risk

[Serialized geometry audit](../../art/direction-20261006/venue-expansion/serialized-geometry-audit.json) selects only each LODGroup's first authored level plus ungrouped meshes, resolves every referenced mesh asset and totals submesh index counts. It is read-only and **not** a visible-frame or draw-call measurement.

| Prefab | All MeshRenderers / LODGroups | Selected highest-LOD renderers / material slots | Serialized triangles |
| --- | ---: | ---: | ---: |
| Shipped Timeline Square | 229 / 70 | 121 / 170 | 124,610 |
| Isolated Timeline A | 291 / 88 | 163 / 216 | 137,274 |
| Isolated Terminal Café | 178 / 42 | 120 / 134 | 47,485 |

Timeline B is staged/unbaked, with a receipt reporting 20,916 additional source-manifest LOD0 triangles; do not add that blindly to the canonical total because B removes/replaces objects. Café has 17 reachable anchor paths in its stage receipt and two lightmaps binding 166 renderers. Timeline A preserves 15 anchors and has five lightmaps binding 275 renderers. Native seat clearance/visual/device stress acceptance is still pending.

The spec's total target is 100–150k visible triangles, under 150 draw calls, one realtime directional light, and a 50-player + 12-NPC device stress test. [Inference] The plaza's uncullled geometry alone nearly consumes that target. Material-slot count is not draw-call count, but confirms that batching/culling must be measured before adding crowds. The spec's upper avatar tier allocation alone sums to 160,800 triangles if all 50 are visible, so crowd visibility and actual LOD distribution must drive acceptance rather than treating independent targets as already compatible.

## Existing interaction hooks and missing loops

`VenueSession.Enter(venueId)` loads art and joins the venue room. `WorldInput` finds an animation anchor near a tap; `LocalPlayerController.GoSit` walks there; `VenueChannel.Sit` sends `sit {spotId}`; server PlayerState confirms animation/spot; `Stand` releases. No new seat protocol is needed for these candidates. Two-client spot contention, late join and reconnect still require staging verification.

HUD mic toggling can connect the generic venue LiveKit room through `/rtc/token` and spatial audio. However, the Spaces phone entry is a ComingApp; frontend facade methods/UI for creating a Space, hand queue, speaker promotion and ending a Space are absent. The contract defines those endpoints. A stage seat is **not** permission to publish audio. Do not make `stage_host` occupancy grant host powers or light an ON AIR sign without confirmed Space state.

Likewise, Perps is a ComingApp. Existing `act_type` occupancy is not a working trading terminal. The contract defines fictional-Cash `/perps` operations and authoritative liquidation results, but the game-facing Perps UI/facade is absent. No fabricated PnL, prices, viewers, host status or online occupants may substitute for these hooks.

## Concrete next composition

The [technical floorplans](../../art/direction-20261006/venue-expansion/floorplans.svg), [placement proposal](../../art/direction-20261006/venue-expansion/scene-plans.json), and [exact anchors](../../art/direction-20261006/venue-expansion/anchor-contract.json) are outside Assets, not imported or rendered game evidence.

**Spaces Studio:** 24×20m, 34 exact anchors. A long broadcast salon has ten host-facing chairs across z=6 and host at(0,8), twenty audience chairs in four rows, low side lounges, a glazed left wall, dark acoustic right wall, and a full-height identity wall behind the host. Use warm wood, petrol upholstery and warm studio practicals with cool window fill. A flush dark-wood stage field gives visual hierarchy while keeping every foot at y=0; the existing raised platform cannot be dropped under synchronized ground-level actors. Entrance(0,-9) immediately faces seat_18(0,-8), so route around it through x=±1.5 and wider side aisles, not by moving the chair. Preserve the actual 0/3.142rad audience/stage orientation. Existing Kenney lounge chairs, Quaternius furniture and authored studio equipment make 9,682 source LOD0 triangles before a 4k shell/trim allowance. Floors/walls combine by copied materials; studio seating stays individually claimable. The scene has a purpose when a real group can listen, request a mic and hand host control safely.

**Perp Pit:** 28×20m, 13 exact anchors. Two continuous four-station desk ribbons at z=4.82 and8.82 face a wide high market board; dark service racks at the side walls create a recognizable trading floor. The foreground is a public witness circle centered on liquidation_zone(0,-3), framed by side recovery lounges and an industrial entrance. Keep that circle traversable: floor graphic, no blocking pedestal. Board geometry belongs beyond z=10 so leverage_board(0,9.5) remains reachable. Concrete, matte dark steel and restrained amber create a different identity from Café; green/red signals appear only with real server data. Existing Sci-fi Essentials desks/chairs, triple monitors, server racks and window modules sum to 20,402 source triangles before a 4k detail allowance. Empty screens show house identity/idle state, never invented live charts.

Both proposals need landscape camera and seating proofs before material polish, native nav paths to every exact anchor, one copied-material bake, collision exclusion at seats, and a two-client interaction pass. Renderer and triangle allowances are planning targets, not acceptance. Camera must show near-entry people plus the destination within the first view; no 12m dead foreground. No new assets, package changes, contract changes, registry promotions or native launches occurred in this task.
