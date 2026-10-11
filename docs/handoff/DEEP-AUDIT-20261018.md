# CT Life: deep audit and fix plan (18 Oct 2026)

**Verdict: not demo-ready yet.** The pieces exist: title, onboarding, lobby, Duo, island, traffic, vehicles and
pedestrians. But a real hand-held play session breaks quickly. Our agents verified features with scripted captures
and fixed demo routes, not by playing the way a person plays. This audit lists what's wrong, why (with code
references where confirmed), and exactly how to fix it.

Evidence labels:
- **[Confirmed in code]**: read in the source on dev `688a4ad9`.
- **[User report]**: seen by the user on the Seeker.
- **[Inference]**: likely cause, not yet reproduced.

Read `docs/handoff/CONTINUE-HERE-20261018.md` first for paths, rules and lanes. Fix in priority order, P0 before P1.

---

## P0: blockers for any demo

### P0-1. Everyone gets a fleet, a helicopter and a drone [User report + Confirmed in code]
- **Cause**:
  - `World/Vehicles/VehicleSystem.cs` `Start()` calls `VehicleSpawner.SpawnShowcase(...)` on every island entry, for
    every player, in every build type. Only `vehicleDemo` is behind the dev flag.
  - `SpawnShowcase` parks compact, sports, roadstar, suv and scooter next to the player and puts a helicopter on the
    nearest pad.
  - The HUD shows a Drone button for everyone (`UI/Hud/Hud.cs` `_droneAction`), and `DeployDrone()` creates a drone from
    nothing.
- **Why it's wrong**: there's no ownership, cost or scarcity, so nothing feels earned. Every player spawning their own
  fleet would also flood a shared world once multiplayer vehicles exist.
- **Fix**:
  1. Put `SpawnShowcase` behind `#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD` **and**
     `LaunchArgs.Get("showcaseFleet")`. Don't call it in normal play.
  2. Normal access model:
     - **Public cars**: World's `ParkedCar` decor cars are takeable (`ParkedCars.cs` already swaps them) and limited to
       what's parked in the world.
     - **Helicopter**: only at helipads (`HeliPad` markers, 3 on the island). Rent it for in-game Cash at the pad
       (prompt "Rent helicopter, $X"). Use a server-authoritative purchase route when Iyke adds one; until then, show
       "Coming soon" instead of free use outside dev builds.
     - **Roadstar**: a premium owned vehicle. Hide it unless the player owns it (Shop or inventory item; ask Iyke for an
       item id). In dev builds, keep it available via `showcaseFleet`.
     - **Drone**: an owned gadget from the Shop (inventory item `drone_cam`). Hide the HUD Drone button unless
       `/inventory` contains it. Dev builds: `showcaseFleet` grants it.
  3. Add `/inventory`-driven gating in `Hud.cs` and `VehicleSystem` (inventory reads already exist in Shop).
  4. Backend ask for Iyke (add to round 3): Shop items `vehicle_roadstar` and `gadget_drone_cam`, plus a
     `POST /v1/rentals/heli {padId}` returning a time-limited rental id (the server deducts Cash). Until then, the
     items stay hidden in production builds.

### P0-2. You can get out somewhere you can't stand, and then you're lost [User report: "I landed in nowhere… drowned on the mountains" + Confirmed in code]
- **Cause**:
  - `VehicleSystem.ExitRoutine`: the exit point is `v.EntryPoint`, falling back to a point beside the vehicle. It only
    checks `FootPlanner.Free(exit)`, which is a capsule overlap test. **Empty air or a point under the terrain counts
    as "free".** Nothing checks for walkable ground below, slope, terrain loaded, or sea.
  - Exit is allowed when `Altitude <= 1.5 && !OverWater`. There's no slope check, so a helicopter or Roadstar resting
    on a steep mountain face can be exited.
  - **No on-foot safety net.** `LocalPlayerController`/`Locomotion` never check sea level or falling out of the world.
    Once on foot in the sea, under terrain or on a cliff, the player stays there.
  - `RecoverFromWater()` explicitly skips the helicopter (`v.Kind != VehicleKind.Helicopter`) and the drone, so a
    helicopter that ends up in the sea or below the terrain is never recovered.
- **Fix**:
  1. `SafeExitPoint(Vehicle v)` (new, in VehicleSystem): try, in order, the left door, right door, rear, front, then
     rings of 2–6 m. Accept a point only if all of these hold:
     - a downward ray (from +2 m, 6 m long) hits Ground/Road/Building-top with `normal.y > 0.8`
     - the hit is within 1.2 m vertically of the vehicle's base
     - the hit is above `SeaLevel + 0.3`
     - `IslandStreamer.IsReady(point)`
     - `NavMesh.SamplePosition(point, 1.5)` succeeds
     - the capsule is free

     If none qualifies, refuse with "Find flat ground to get out" (new string). Never exit into the air.
  2. Exit rule for aircraft: grounded (`FlightPhysics.Grounded` for at least 0.5 s), speed under 2 m/s, ground normal
     `y > 0.85`, not over water.
  3. **Player safety net** (new `World/Players/PlayerSafety.cs`, on the local player):
     - Every 0.5 s, record a "last safe point" when grounded, on the NavMesh, above sea + 0.3 and `IsReady`.
     - **Water**: if feet are below `SeaLevel - 0.4` for more than 1.5 s, play a short "swim / out of water" fade and
       teleport to the last safe point (or the nearest NavMesh point on land within 60 m). Toast: "Washed ashore".
     - **Fell through**: if `y < GroundHeight(x,z) - 2` or `y < -50`, teleport to the last safe point at once.
     - **Stuck**: if grounded is false for more than 3 s with vertical speed near 0 (wedged), teleport to the last safe point.
     - Add an "Unstick me" button in Duo Settings (and a map long-press, "Teleport to nearest road", dev builds) as
       manual recovery.
  4. Vehicles: drop the helicopter exclusion. Recover any vehicle below `SeaLevel - 0.6` (except the boats you'll add
     later), or more than 2 m below the terrain height at x/z, to `_lastSafe`.

### P0-3. Flying outruns the streamed ground; terrain colliders can be missing under you [Inference, matches the user report]
- **Cause**:
  - Cells load **one async cell at a time** (`IslandStreamer` comment).
  - The helicopter (97 km/h) and Roadstar (119–164 km/h) cross a 64 m terrain chunk in under 2.5 s.
  - `FlightPhysics.OverWater` is decided by "the sea surface is nearer than any ground below". Over a mountain whose
    colliders aren't loaded yet, the ray finds no ground, so the craft thinks it's over water. Landing is blocked, or
    the craft settles through the visual-only terrain.
- **Fix**:
  1. In `FlightPhysics`, when the ground ray misses, sample `IslandStreamer.GroundHeight(x,z)` (the heightfield, always
     available). If it's above sea level, treat the craft as over land with ground at that height, not over water.
  2. Use the heightfield for `Altitude` too (the minimum of the collider ray and the heightfield).
  3. Don't let any vehicle settle while `!IslandStreamer.IsReady(position)`. Hold a hover, show "Loading area…",
     call `IslandStreamer.EnsureLoaded(position, 120)`, then allow landing.
  4. Streaming: allow 2–3 concurrent cell loads while `velocity > 20 m/s`. Prefetch along velocity with a horizon of
     `max(6 s, 200 m / speed)`. For terrain colliders specifically, keep a **low-res collider for the whole island**
     always loaded (the heightfield at 8 m resolution as one or a few MeshColliders). Nothing should ever fall through
     the island.

### P0-4. The helicopter is too hard to fly on a phone [User report: "couldn't use the helicopter properly"]
- **Cause**: `UI/Hud/VehicleControls.cs` uses two floating sticks in the air (left = climb/descend and yaw, right =
  pitch/roll), which is real helicopter controls. Mobile players expect GTA-mobile style: one stick moves the craft
  relative to the camera, plus Up/Down buttons. Auto-hover braking also fights small inputs.
- **Fix**:
  1. New default "Simple" flight scheme for the helicopter and drone:
     - **Left stick = desired horizontal velocity in camera space** (forward = camera forward). The controller tilts the
       body to reach it (cap tilt at 20°) and yaws the nose toward the direction of travel smoothly.
     - **Right side: two big buttons, ▲ Up and ▼ Down.** Hold sets a climb rate of ±6 m/s; release holds altitude.
     - Camera drag on the right half still orbits.
     - Auto-hover only when the left stick is released.
     - Move Land/Exit to a single contextual button: "Land" (auto-descends to ground and settles) becomes "Get out" when grounded.
  2. Keep the current two-stick scheme as "Pro" in Duo Settings → Controls.
  3. Show a first-flight coach overlay once: three tips with arrows. It never shows again.
  4. Chase camera for the helicopter: 14 m behind and 5 m above, looking slightly down, with an auto-recentre and a
     1.2 s delay.
  5. Check it by having someone who's never seen it take off, fly to the marina and land on a pad within 60 s.

### P0-5. Things pass through each other [User report]
- **Causes to check** (several):
  - The World generator places buildings, lots and props without a global overlap test against each other and
    against roads (W4 already found trees and a statue on the highway because roads had holes in the placement mask).
  - The instanced kit renderer draws about 25k instances. Not every model is guaranteed a collider that matches its
    footprint.
  - Venue shells: walls dissolve visually from inside, and their colliders may not cover every face.
  - Vehicles: fast Rigidbody bodies with discrete collision can tunnel through thin colliders.
  - ParkedCar colliders are on layer 13; traffic may not avoid them.
- **Fix**:
  1. Generator (`tools/world/islandgen/`): build a single 2D occupancy map (polygons) of roads, sidewalks, lots,
     building footprints, plazas and water. Every placement must test against it. Fail the build with a list of
     overlaps over 5 cm² (unit test `WorldNoOverlapTests`).
  2. Instanced models: generate one simplified BoxCollider (or compound boxes) per model at bake time from mesh
     bounds, per LOD0. Test it: every instanced model id in the manifest has a collider entry.
  3. Venue shells: one closed set of wall colliders per shell, independent of the dissolve visuals.
  4. Vehicles: `CollisionDetectionMode.ContinuousDynamic` for player-driven vehicles and the Roadstar and helicopter,
     `ContinuousSpeculative` for traffic. Building and wall colliders at least 0.5 m thick.
  5. CharacterController: skin width 8% of the radius. The player mask includes 10–14 and 16 (not 15 vehicle
     blockers, not 21 seat taps). Pedestrians avoid props through NavMesh obstacles (carve) on benches, bins, planters
     and parked cars.
  6. Add an editor sweep, `Tools/CT Life/Audit/Interpenetration`: `Physics.ComputePenetration` for every static
     collider pair per district, which writes a report and fails on overlaps over 10 cm.

### P0-6. No reason to play yet: no goals, no loop in the world [User feedback earlier: "there's no interest to this game at ALL"]
- **Cause**: the world is explorable but has no objectives. The minimap has no objective markers ("no objective
  system exists"). Jobs live inside the Duo as minigames, not in the world.
- **Fix** (minimum for the demo):
  1. **Objective system** (`World/Objectives/`): one active objective with a world marker, a minimap marker plus edge
     arrow, distance text and a completion toast. Data-driven list per player.
  2. **First-session quest chain** (about 3 minutes):
     - walk to the Timeline Square screens (onboarding tip)
     - take a parked car to Terminal Café and do a job shift (the existing JobsApp shift opens on arrival)
     - collect your pay (the real `/shifts`)
     - check your wallet on the Duo
     - visit the MtnDAO summit (Roadstar or helicopter rental). The reward is a summit photo-op emote.
  3. **World activities** with markers: job desks inside venues (they exist as spots), the Perp Pit trading floor, a
     Spaces stage, and a street race checkpoint loop (local time-trial using RoadNetwork checkpoints). These give the
     demo video things to do.

---

## P1: important for quality

1. **Multiplayer for vehicles and streets.** Other players see you frozen at the car door, and street walkers are
   invisible to each other. This waits on Iyke's round 2 (`docs/handoff/backend-asks-for-iyke-20261018.md`). Until
   then: when you enter a vehicle, send a "hidden / in vehicle" presence state if any field allows it; otherwise hide
   your avatar for others by leaving the venue room cleanly on enter. Don't leave a frozen ghost.
2. **First-launch shader stutter** (12–15 fps for about 60 s): create a ShaderVariantCollection from a play session.
   Warm it up during the title loading bar (it already shows real boot steps; add "Preparing graphics").
3. **Thermal**: the device sits at the 30 fps cap with GPU about 27 of 33 ms.
   - Reduce downtown draw calls: merge static instanced batches per cell, cull small props beyond 60 m, drop shadow
     distance to 50 m in the city.
   - Target 25 ms GPU at render scale 0.7.
4. **APK size** (about 264 MiB): put the world kits and venue dioramas in Addressables, as remote bundles or Play
   Asset Delivery install-time packs. Keep the title, onboarding and lobby in the base APK.
5. **Traffic quality**:
   - The roundabout recorded only 0–1 yields, so check that its give-way rule really happens.
   - Traffic should treat ParkedCar and player-parked vehicles as obstacles.
   - Despawn traffic stuck for more than 10 s off-screen.
6. **Pedestrians**: the last commit (removes name tags, raises the cap to 16) isn't device-checked. Verify there are no
   tags, people don't walk through each other or props, and they stay off roads except at crossings.
7. **Helicopter tail rotor** doesn't spin (it's part of the body mesh). Split the mesh in Blender and spin it like the main rotor.
8. **Editor (Metal) captures render some ground black.** The device is fine and the cause is unknown. Suspect the
   Toon shader reading uninitialised per-instance data in the editor. Compare the SRP Batcher on and off and check
   the instancing buffer layout. It matters for 4K editor footage in the demo video.
9. **Camera**: in tight interiors and among props, verify the occlusion pull-in never puts the camera inside the
   head. In vehicles, add a collision check so the camera never goes inside buildings.
10. **Weekly debt due** stays at $120 after paying (server). In the client, show "Paid this week ✓" once the ledger
    has a `loan_repay` this week, until Iyke fixes it.

## P2: polish
- Character model flaws seen in close-up (collar cut-out, a glasses-arm stick, jagged cuffs, flat braids, rigid
  skirts, no mouth or expressions). That needs a model pass or a different base for hero shots.
- Lodge demo shot: the summit is behind the camera. Add a summit-facing shot.
- The deferred in-game lighting and look pass (the user wants it when the app is ready).

## Release blockers (not code)
- **ctlife.fun is not owned.** Register it, or change the SIWS identity on the client and server.
- **Tripo API output commercial terms are [Unverified]**, and five statues depend on them. Re-read the terms.
- Credits: Icons8 in the store description, all CC-BY rows in the in-game credits (see the CONTINUE-HERE §8 checklist).

---

## How to verify this time (the agents' testing gap)
- Every P0 fix needs a **hand-played Seeker session** recorded with `adb screenrecord`, not only scripted captures.
- Use this route without dev teleports (except the start):
  1. Spawn at Timeline Square, walk to the beach, wade into the sea (expect "Washed ashore").
  2. Take a parked car downtown, drive to the marina, crash into a building at speed (no pass-through, no flip-through).
  3. Get out on a slope (refused or safe).
  4. Rent the helicopter at the pad, fly with Simple controls to the MtnDAO summit, land on the slope (refused) and on
     the flat cairn (allowed), get out.
  5. Walk down a cliff edge (fall, then recover or roll), call the drone (hidden unless owned).
  6. Fly the Roadstar fast across the island and land on an unloaded far district (no fall-through).
- Record fps, any fall-through, any pass-through, any lost-player state, with timestamps. A fix isn't done until this
  route runs clean twice in a row.

## Suggested lane split
- **Feel lane**: P0-1 (gating, HUD), P0-2 (safe exit and player safety net), P0-3 (flight ground logic), P0-4
  (simple flight controls, coach, camera), P0-6 (objective system and quest chain), P1-1, 2, 5, 6, 7, 9, 10.
- **World lane**: P0-3.4 (streaming concurrency and the whole-island low-res collider), P0-5 (occupancy map,
  instanced colliders, shells, interpenetration sweep), P1-3, 4, 8, the P2 lodge shot.
- **Coordinator**: reviews each fix against the hand-played route, merges, and sends Iyke round 3 (P0-1 shop and
  rental items, the P1-1 interim, the weekly-due fix).
