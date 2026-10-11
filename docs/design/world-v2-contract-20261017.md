# CT Island v2: world contract between the World and Feel lanes (2026-10-17)

The user rejected the v1 island ("like an estate", "a square", "everything in 3s", "AI vibes"). v2 is a GTA-like
stylised island city with real distances, real roads, traffic and vehicles. Two agents build it in parallel. This file
is the interface between them. Change it only through the coordinator.

## User direction (verbatim intent)

- Feels like playing GTA on a different map: drive between places, distance between places is fine and wanted, the
  road itself should look good, vehicles parked and NPCs driving some.
- Not a square, not a grid of 3s, not an estate, not predictable. Look at the reference board
  (`ct-life-evidence/world-v2-20261017/refs/`) and the user's map reference 8 (painterly relief).
- Use ready-made models freely (Sketchfab, Kenney, Quaternius, KayKit, Poly Pizza, CC0/CC-BY with credits). Use Tripo
  credits for custom things that don't exist. Don't hand-build what can be sourced.
- Solana community landmarks belong on the map (e.g. MtnDAO on a real mountain), from
  `docs/research/community-eggs-2026-10.md`.
- Look good first (demo video), light testing. Movement too fast now; controls feel weird; arms-up animation and
  falling look wrong.

## Coordinates and scale

- Island metres. +X east, +Z north, +Y up. Origin = Timeline downtown plaza centre.
- Playable island about 1.6 km × 1.2 km, irregular coastline. Sea to ±1.2 km. Flight ceiling 220 m above sea level.
- Human scale: doors 2.2 m, storeys 3.2–3.6 m, lanes 3.3 m, sidewalks 2.5–4 m.

## Ownership

| Lane | Owns | Must not edit |
|---|---|---|
| **World** | `client/Assets/World/**`, island build tools, terrain, roads, buildings, props, vegetation, water, sky, lighting/post profiles, landmarks, venue placement and building shells, `IslandStreamer` and room handoff, map/atlas bake data and bake tools, `RoadNetwork` data, spawn markers | Player controller, camera, vehicles, traffic AI, HUD/map UI code |
| **Feel** | Player controller and character animation/rig settings, camera, input/controls, vehicles (cars, helicopter, drone, Roadstar), vehicle physics, traffic AI and NPC drivers, HUD and map/minimap UI code | World content and tools above |

Shared files (`art/LICENSES.md`, credits text, `docs/SUBMISSION-KIT.md`): append only, in your own section.

## Data the World lane provides (Feel consumes)

1. **`RoadNetwork` ScriptableObject** at `client/Assets/World/Island/Roads/RoadNetwork.asset`
   - Nodes `{id, position}`; segments `{id, fromNode, toNode, controlPoints (spline), lanesForward, lanesBack,
     laneWidth, speedLimit m/s, kind: highway|avenue|street|mountain|service|bridge|tunnel, oneWay}`; intersections
     `{id, nodeIds, kind: signal|stop|roundabout|merge}`.
   - Runtime API (World implements): `NearestLanePoint(Vector3)`, `LaneCenterline(segmentId, laneIndex)`,
     `NextSegments(segmentId, laneIndex)`, `SpeedLimitAt(segmentId)`.
2. **Spawn markers** (components in the island scene/prefabs): `PlayerArrival` per venue door (exists), `VehicleSpawn`
   (parking bay, allowed classes: compact|suv|sports|moped|roadstar), `HeliPad`, `TrafficZone` (where AI cars may
   spawn/despawn around the player).
3. **Layers** (World defines indices in TagManager, documents them here when set): `Ground`, `Road`, `Building`,
   `Prop`, `Vegetation` (no collision for small plants), `Water`, `VehicleBlocker` (venue doorways: blocks vehicles,
   not people), `CoastWall` (blocks walk/drive, not flight), `Interior`.
4. **Streaming**: `IslandStreamer.Prefetch(Vector3 position, Vector3 velocity, float horizonSeconds)`; Feel calls it from
   vehicles/flight so content is in before arrival. Budgeted async loading, no frame over 33 ms.
5. **Atlas/minimap bake data**: same `atlas.json` shape as v1 (version bumped), baked top-down from the real v2 world, so
   the existing map/minimap UI keeps working. World re-bakes after layout changes.
6. **Offline demo mode**: the island, venues, NPCs, parked cars and traffic must run with local fixtures when the
   backend is down (for filming the demo video). Use the existing review/fixture path; no fake money or progress writes.

## Rules for both lanes

- Commit often; push your own feature branch to `origin` after each phase (`feat/world-v2-*` or `feat/feel-v2-*`). The
  coordinator merges to `dev`/`main`.
- Light verification: compile check plus EditMode tests touching your code; one Seeker run per phase with fps noted. No
  big capture sweeps.
- Licences: read each source's licence page, keep URL + date + author in `art/LICENSES.md` and in-game credits.
- Tripo3D API (key `TRIPO_API_KEY` in `../ct-life-backend/.env`; never print it): dev-time asset generation only.
  Budget for now: World 200 credits, Feel 100 credits (balance 325). Ask the coordinator before exceeding it.
- Sketchfab downloads need the user's logged-in browser: send the coordinator the exact model URLs and wanted format.

## Layers (set by World on feat/world-v2-layout, 2026-10-17)

| Index | Name | On it | Collision |
|---|---|---|---|
| 4 | Water (built-in) | sea, river and lake surfaces (renderers only, no colliders) | none |
| 10 | Ground | terrain chunks (MeshColliders generated as a cell streams in), far terrain (no collider), flight-ceiling box at y = 215 | everything |
| 11 | Road | carriageways, sidewalks, junctions, bridge decks and parapets, tunnel tube, portal frames, helipads | everything |
| 12 | Building | building shells (W2) | everything |
| 13 | Prop | guard rails, roundabout islands, street furniture (W2) | everything |
| 14 | Vegetation | tree trunks only (W2); small plants have no collider | everything |
| 15 | VehicleBlocker | a 5.2 x 2.6 x 1 m box across every venue doorway | Characters (8) and Ragdoll (9) ignore it; vehicles must collide |
| 16 | CoastWall | walls along the coast (at the edge of cliffs and quays, 5 m out on beaches), round the islet, along the causeway | blocks walking and driving; flight should ignore it |
| 17 | Interior | venue dioramas once streamed onto the island (their layer-0 objects) | everything |
| 21 | (Feel) SeatTap | seat tap boxes (taps hit them; bodies and the camera ignore them) | Feel-owned; player ignores 15 |

Feel: vehicle layers 20+. Make flight ignore CoastWall (16); keep cars colliding with VehicleBlocker (15). World has already set Characters/Ragdoll x VehicleBlocker to ignore in the physics matrix.

## World interface notes (W1, feat/world-v2-layout)

- Heights: sea level y = -6, plaza y = 0, flight ceiling y = 214 (`IslandStreamer.FlightCeiling`; collider at 215 on Ground).
- `CTLife.World.Roads.RoadNetwork`: `Assets/World/Island/Roads/RoadNetwork.asset`. `RoadNetwork.Active` is set while the island is loaded.
  - Contents: right-hand traffic, 134 segments, 60 intersections (24 signal, 30 stop, 2 roundabouts with their ring nodes grouped, 4 merge), 338 lanes.
  - Lane numbering: lanes 0..F-1 run forward (from -> to, right of the centreline, 0 innermost); lanes F..F+B-1 run back.
  - `NearestLanePoint(pos, maxDist = 60, filter)` returns `LanePoint {segmentId, lane, position, direction, along, valid}`.
  - `LaneCenterline(segId, lane)` returns the lane in its travel direction, about 2 m per sample. Do not modify the array.
  - `NextSegments(segId, lane)` returns `LaneRef[]`. Right turns leave from the outer lane and left turns from the inner lane.
  - `SpeedLimitAt(segId)`: m/s.
  - Extras: `Segment(id)`, `IntersectionAt(nodeId)`, `LaneStartNode` / `LaneEndNode`, and `Connector(from, to, steps)` (a Bezier path through the junction).
  - `Signal(intersectionId, approachSegmentId, timeSeconds)` returns Green / Amber / Red. Two axis phases, 38 s cycle, per-intersection offset. World's traffic-light props will show the same state.
- `IslandStreamer`:
  - `Prefetch(position, velocity, horizonSeconds)`: predicted points every 1 s, kept for 2 s. Cells (256 m) load within 420 m and unload beyond 560 m, with one async load at a time. Ground colliders are baked off the main thread before a cell counts as loaded.
  - Also: `IsReady(pos)`, `EnsureLoaded(pos, radius)` (await it before teleports and respawns), `GroundHeight(x, z)`, `SeaLevel`, `FlightCeiling`.
- Markers, registered while their cell is loaded:
  - `VehicleSpawn`: 722 street parking bays now; lots come in W2. Kind "street", classes Cars|Moped, 2.4 x 6 m, with a segmentId. The transform's forward is the parked heading.
  - `HeliPad`: three of them (MtnDAO lodge, marina, Breakpoint Park), r = 9 m.
  - `TrafficZone`: one per district, density 0.3 to 1.6.
  - Query them with `IslandMarkers.VehicleSpawns`, `HeliPads`, `TrafficZones`, `SpawnsNear(p, r, class)` and `ZoneAt(p)`. The `Changed` event fires as cells stream.
- Camera: while the island is loaded, `IslandRenderSettings`:
  - raises Camera.main.farClipPlane to 5200 (never lowers it)
  - sets clearFlags to Skybox
  - sets layer cull distances: Prop 160 m, Vegetation 420 m, Interior 220 m

  If Feel's camera writes these every frame, keep far >= 5200 on the island.
- Venues face their streets (any yaw).
  - `IslandVenue` has yaw (degrees clockwise) and elevation.
  - `VenueSceneRoot.TryGetSpot` now returns world yaw for registry spots too.
  - The atlas.json v2 re-bake (W2) adds `rotationDeg` and `center` per area.
- NavMesh: an island-wide pedestrian NavMesh is loaded with the island. It covers districts, roads and venue interiors; walls round each room leave the door open, and there are no corridors. The player moves on colliders.
- Shadows: the URP asset now supports main-light shadows. The island sets shadowDistance 70 while it is loaded and restores it afterwards.
