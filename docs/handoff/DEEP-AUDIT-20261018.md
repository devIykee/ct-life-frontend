# CT Life: deep audit and exact fix plan (18 Oct 2026, v2)

**Verdict: not demo-ready.** The systems all exist, but hand-held play hits several root-cause bugs fast. The agents
tested with scripted captures and fixed routes; nobody played it like a player, so these slipped through. This audit
is written from a line-by-line read of the gameplay code on `dev` (`31b5d601`) and a play session on the Seeker (the
build installed at 15:24, with `islandDemo=1`).

Labels: **[Code]** = confirmed in source (file and line given), **[Device]** = seen on the Seeker today, **[User]** =
reported by the user, **[Inference]** = likely cause, reproduce before fixing. Every fix has an acceptance test. A
fix isn't done until it passes on the Seeker by hand (§6).

---

## 1. "I landed in nowhere / drowned on the mountains" [User]. Three separate root causes

### 1A. Lakes and the river are invisible to the game [Code]
- `Editor/IslandV2/IslandV2Builder.cs` 586–599 `WaterObj(...)` builds the sea, river and lakes as MeshRenderers on
  layer Water, with **no collider and no recorded water height**.
- At runtime the only water the game knows is the **sea level** (`IslandStreamer.SeaLevel`). These all compare
  against sea level only:
  - `FlightPhysics.OverWater`
  - `VehicleSystem.RecoverFromWater`
  - `VehicleSpawner.Reachable`
  - the exit checks
- The two mountain lakes and the river are **above** sea level. A helicopter or Roadstar lands on the lake or river
  **bed** (the terrain under the water), the game sees "not over water" and allows the exit. The player then stands
  underwater in a mountain lake. On foot, the player can also walk straight into the river.

**Fix**:
1. In the builder, emit a runtime `WaterBodies` asset: one entry per lake and river reach, holding a polygon (or
   river polyline with width) and a surface height. For the river, store the height per vertex along its length.
   Load it in `IslandWorld`.
2. Add `IslandStreamer.WaterSurfaceAt(x, z) → float` (NaN when dry) and `IsWater(x, z, y)`. Implement it with a 2 m grid
   lookup baked from the polygons; the sea returns `SeaLevel` outside the coastline.
3. Replace every `SeaLevel` comparison with `WaterSurfaceAt`:
   - `FlightPhysics`: OverWater is `y - surface < groundAlt - 0.3`.
   - `RecoverFromWater`: below the surface by more than 0.6 m.
   - `VehicleSpawner.Reachable`
   - the exit checks
   - the new player safety net (§1C)
   - `PedestrianSystem` and `TrafficSystem` spawn checks
4. Optional depth gameplay: shallow river edges (depth under 0.6 m) are walkable and slow you to 50%.
5. Test (EditMode): sample points inside each lake and river return the surface height, and land points return NaN.
   Device: land the helicopter on a lake and it refuses ("Can't land on water") and hovers; walk into the river and
   you're recovered to the bank (§1C).

### 1B. Flying over ground that hasn't loaded: no collider, so you sink through the mountain [Code + Inference on timing]
- `World/Island/IslandWorld.cs` 208–216 `Stream()` loads **one cell at a time** (`_cellLoading.Count == 0` gate). Each
  `LoadCell` is an async Resources load, an async instantiate and a terrain collider build
  (`IslandTerrainCell.Build`). There are 51 cells and the load radius is 420 m.
- The "far terrain" is always drawn **without colliders** (W1: "Far terrain is always loaded"). You see a mountain
  that isn't solid yet.
- `World/Vehicles/FlightPhysics.cs` 139–147: with no ground hit, `groundAlt = 600`, so `OverWater = seaAlt < 599.7`,
  which is **true anywhere above the sea**. The craft believes it's over water. `Grounded` stays false and landing logic
  is off; the only floor is "skim the water" (stay 1.2 m above sea level).
- Result: approaching an unloaded mountain cell, the helicopter descends through the visible terrain and settles
  1.2 m above sea level **inside the mountain**. Exit is refused ("no water landing"), so you're stuck in a void.
- `VehicleSystem.RecoverFromWater` 419 explicitly excludes `VehicleKind.Helicopter`, so nothing rescues the helicopter.

**Fix**:
1. **Never fly on missing ground.** In `FlightPhysics`, when the ground ray misses, or `!IslandStreamer.IsReady(pos)`,
   use the heightfield `IslandStreamer.GroundHeight(x,z)` (always available) as the ground. Compute
   `groundAlt = y - max(GroundHeight, WaterSurfaceAt)`. Only set `OverWater` from the water lookup (§1A), never from a
   missing ray.
2. **Whole-island low-res collider, always loaded.** Bake the heightfield at 8 m into one MeshCollider (or 4 tiles)
   on layer Ground. Lower it 0.15 m so the detailed cell colliders win where loaded. Nothing can fall through the
   island again.
3. **Streaming throughput.** Let `Stream()` start up to 3 concurrent cell loads when the player or vehicle speed is
   over 15 m/s (keep 1 when walking). Prefetch along velocity with `horizon = clamp(250 m / speed, 4, 10) s` (already
   called from `VehicleSystem` 104; raise its horizon from 4 or 6 s to that).
4. **Landing guard.** Before `Grounded` can be true or the craft settles, require `IsReady(pos)`. If not ready, hold
   the hover, show "Loading area…", and call `EnsureLoaded(pos, 120)`.
5. **Rescue everything.** Remove the Helicopter exclusion in `RecoverFromWater`. Also recover any vehicle more than
   2 m below `GroundHeight(x,z)` (fell through) to `_lastSafe`.
6. Test: in the editor, artificially delay `LoadCell` by 3 s and fly the helicopter at full speed toward the summit.
   It must never go below the heightfield and lands correctly once loaded. Device: fly straight at the mountain from
   Timeline at top speed three times and land on the summit cairn.

### 1C. No safety net on foot, and the exit doesn't check for ground [Code]
- `World/Vehicles/VehicleSystem.cs` `ExitRoutine` (about line 304): the exit point is `v.EntryPoint`, or a point
  1.8–2.6 m to the left. The only test is `FootPlanner.Free(exit)` (`World/Players/FootPlanner.cs` 34), a capsule
  overlap. **Empty air and space under the terrain count as free.** There's no check for a floor, its slope, its height
  relative to the car, the water surface, or whether the ground is loaded.
- The aircraft exit rule (`VehicleSystem` 218) only checks `Altitude > 1.5 || OverWater`, with no slope check. A
  helicopter resting on a steep face can be exited.
- `CharacterBody.SlopeLimit = 45°` (`Avatars/Physicality/CharacterBody.cs` 45). Mountain faces are steeper, so a
  player put there can't walk off it.
- **There's no on-foot recovery anywhere.** `LocalPlayerController` and `Locomotion` never check water, a fall
  through the world, or being stuck.

**Fix**:
1. Add `VehicleSystem.FindSafeExit(Vehicle v, out Vector3 p, out float yaw)`. Candidates, in order: the driver door,
   the passenger door, rear, front, then rings at 2, 3, 4 and 6 m (16 samples each). Accept a candidate only if all of
   these hold:
   - a down-ray from +2 m (6 m long, `CharacterBody.Mask`) hits with `normal.y ≥ 0.82` (35° or less)
   - `|hit.y - vehicleBaseY| ≤ 1.2`
   - `WaterSurfaceAt` is NaN, or `hit.y > surface + 0.3`
   - `IslandStreamer.IsReady(hit)`
   - `NavMesh.SamplePosition(hit, 1.5)` succeeds
   - the capsule is free

   If none passes, refuse with "Find flat ground to get out" (a new string) and keep the player in. Use it in
   `ExitRoutine` instead of `EntryPoint`.
2. Aircraft exit rule: `FlightPhysics.Grounded` for at least 0.5 s, speed under 2 m/s, and the ground normal under the
   craft has `y ≥ 0.85`.
3. **New `World/Players/PlayerSafety.cs`** (added by `LocalPlayerController.Init`):
   - **Track the last safe point** every 0.5 s while grounded: on the NavMesh within 0.5 m, out of water, and
     `IsReady`.
   - **Water**: feet more than 0.5 m below the water surface for 1.2 s. Play a 0.4 s fade, teleport to the last safe
     point (or the nearest NavMesh point on land within 80 m) and toast "Washed ashore".
   - **Fell through**: `y < GroundHeight - 2` or `y < SeaLevel - 20` teleports immediately.
   - **Stuck**: not grounded for over 4 s and moved less than 0.3 m. Teleport.
   - **Steep slope**: standing on `normal.y < 0.7` for over 3 s. Slide the player downhill (add a downhill velocity
     along the slope tangent), or teleport if that fails.
   - In Duo Settings, add "**Unstick me**": teleport to the nearest road node (`RoadNetwork.NearestLanePoint`, ground-snapped).
4. Tests: EditMode tests for `FindSafeExit` on synthetic colliders (a slope, water, a void, flat ground) and for
   `PlayerSafety` state transitions. Device: exit on a slope (refused), wade into the river (washed ashore), drive
   into the sea (recovered).

---

## 2. "Anybody can just spawn a drone, like everybody has one" [User + Device + Code]
- `World/Vehicles/VehicleSystem.cs` 70–77, `Start()`: `VehicleSpawner.SpawnShowcase(...)` runs on **every** island
  entry, in **every** build type. Only `vehicleDemo` is behind the dev `#if`. `SpawnShowcase` (`VehicleSpawner.cs`
  15–41) parks compact, sports, roadstar, suv and scooter next to you, and puts a helicopter on the nearest pad within 260 m.
- `UI/Hud/Hud.cs` 130–133: the Drone HUD action is shown on the island for everyone. `DeployDrone()` creates one from
  nothing. Confirmed on the Seeker: the Drone button is in the HUD for a fresh demo character with $0.
- `VehicleSystem.Abandoned` 125–139: any vehicle more than 320 m away and out of view is **destroyed**. A helicopter
  you left at the summit is gone when you come back, and the showcase never respawns, so the vehicles you saw
  disappear for good after a short drive. That's confusing. [Code]
- **Fix** (the access model):
  1. Put `SpawnShowcase` behind `#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD` **and** an explicit
     `LaunchArgs.Get("showcaseFleet")`. Drop it entirely from normal play.
  2. **Cars and scooters**: the world's `ParkedCar` decor (268 on the island) are the public cars. `ParkedCars.cs`
     already swaps one for a drivable car. Show a "Drive" prompt on any parked car within 3 m. Scooters: add
     `ParkedScooter` from W4's 185 parked scooters and bikes, same pattern.
  3. **Helicopter**: rented at the 3 `HeliPad`s. Put a world prompt at each pad, "Rent helicopter · $250 / 5 min",
     with a confirm sheet. The server must own the money: ask Iyke for `POST /v1/rentals {kind:"heli", padId}` →
     `{rentalId, endsAt, cashCents}`. Until it exists, production builds show "Helicopter rentals coming soon"; dev
     builds can use `showcaseFleet`.
  4. **Roadstar**: an owned premium vehicle (Shop item `vehicle_roadstar`). It's summoned from the Duo's new
     "Garage" app to the nearest free street bay. Hidden unless `/inventory` has it.
  5. **Drone**: an owned gadget (Shop item `gadget_drone_cam`). The HUD Drone button only shows when `/inventory`
     contains it. Read `/inventory` on island entry and on `inventory_changed`.
  6. **Ownership persistence**: owned vehicles are never destroyed by `Abandoned`. They despawn and re-summon from the
     Garage instead. Rentals end at `endsAt`: show a 30 s warning, then the craft auto-lands and the rental closes.
  7. Backend round 3 for Iyke: the shop items above (catalog, price, purchase with Idempotency-Key, inventory
     readback), rentals, and an `inventory_changed` realtime push.
- Test: a fresh account sees no fleet and no drone button. A test account given `gadget_drone_cam` sees the button.
  `showcaseFleet=1` in a dev build restores the old behaviour for filming.


### 2.1 The drone as a rare owned asset: what it's for (user, 18 Oct: "would be nice to be an ingame asset that few people can own, and what's the utility")
- **Scarcity**: a limited-supply Shop item, `gadget_drone_cam`, e.g. 500 units per season, priced high in Cash (or
  a staking unlock, as the user floated for non-human characters). The server enforces supply and ownership.
  Show the owner's drone with a nameplate tag ("@alias's drone") to others once vehicles sync.
- **Utility** (things only drone owners can do):
  1. **Scout**: the drone reveals venue occupancy, live events and other players on the minimap within 150 m for
     the flight (a "radar ping"). That's useful for finding Spaces, beef matches and job desks.
  2. **Content**: a photo/clip mode (free camera, filters, the CT LIFE watermark) that saves 10 s clips and PnL-card
     shots to share on X. A growth loop: every share is an ad.
  3. **Airdrop hunting**: daily "drop crates" spawn on rooftops and hills reachable only by air (the drone, Roadstar
     or helicopter). The drone can tag a crate for its owner to collect on foot (the server validates the
     claim; small Cash or cosmetic rewards).
  4. **Paid drone-cam gig**: in Spaces and live events, a drone owner can be hired as "event cam". Their feed shows on
     the venue screen, and they earn Cash from the host. A real job that needs the asset.
  5. **Status**: the drone's colour and lights are cosmetic slots (Shop items), visible to everyone.
- The client still gates the HUD button on `/inventory` (see the fix list above). The utility features need backend
  support (crate spawns and claims, the gig contract, supply caps). Add them to Iyke round 3.

## 3. "I couldn't use the helicopter properly" [User + Code]
Why it feels broken:
- **A dead stick at start-up with no feedback**: `FlightPhysics.cs` 128 spins the rotor up over **3.2 s**, and lift
  scales with `Rotor01²` (line 158). After 1 s the rotor gives about 10% of the lift, so pushing "up" does nothing
  for 2–3 s, and the HUD gives no "rotor spinning up" cue.
- **Real two-stick controls**: `UI/Hud/VehicleControls.cs` 164. Left stick = lift (y) and yaw (x); right stick =
  pitch (y) and roll (x). That's a real helicopter "mode 2" layout. Casual mobile players expect to point where they
  want to go.
- **Auto-hover fights the player**: `FlightPhysics` 175–180. Whenever the right stick is centred, it pitches against
  the drift, so small corrections feel like the craft resists.
- **Yaw is separate from travel**: the camera doesn't turn the craft. The player has to coordinate yaw and cyclic.
- Thrust is divided by `max(0.35, bodyUp.y)` (line 199). At large tilts that adds a lot of lateral force, which feels twitchy.

**Fix (new default, "Simple" flight, for both the helicopter and the drone)**:
1. **Input model**:
   - The left stick is a *desired horizontal velocity in camera space*: forward means where the camera looks, at up to
     `flightTopSpeed` at full deflection.
   - The right side gets two large hold buttons, **▲ Up / ▼ Down**, for a climb rate of ±6 m/s. Release holds altitude.
   - Dragging the right half (outside the buttons) orbits the camera, as on foot.
   - **Auto-yaw**: the nose turns toward the velocity direction (or the camera forward when hovering) at up to 90°/s.
2. **Controller**: a PD controller on velocity. The tilt target is `atan(desiredAccel / g)` clamped to 20°, with a
   damping term so it stops crisply. Remove the separate auto-hover branch; with the stick released, desired velocity
   is 0, which is a hover.
3. **Start-up**:
   - Tapping Up with the rotor below 0.9 queues the climb.
   - The HUD shows a circular "Rotor 0→100%" ring around the Up button, plus a whine pitch rising (`VehicleAudio`).
   - Shorten the spin-up to 1.6 s.
4. **Landing**: one contextual button. "Land" auto-descends at 2 m/s to the ground under the craft (using §1B's ground
   logic and §1C's slope rule); it becomes "Get out" once grounded and settled. If the ground is too steep, it shows
   "Too steep, move to flat ground".
5. **Camera**: a helicopter chase cam 14 m behind and 5 m above, pitched down 12°, with a recentre delay of 1.0 s
   (`CameraRig.FollowVehicle`, aircraft profile).
6. **Coach**: on the first flight only, show three tips with arrows: "Move", "Up/Down", "Land". Store a flag in
   PlayerPrefs.
7. Keep the current two-stick scheme as **"Pro controls"** in Duo Settings → Controls.
8. Test: someone who has never played takes off within 5 s, flies to the marina pad and lands within 60 s, twice.
   Measure and log the time.

## 4. Things pass through each other [User + Code]

### 4A. Most props have no collider [Code]
- `Editor/IslandV2/IslandV2Dress.cs` 56–66: of about 25k instanced dressing items, only `kind == "tree"` (a trunk
  capsule) and `kind == "lamp" || "pole"` (a capsule) get colliders.
- Buildings get one box each from `buildingBoxes` (line 70), and there are a few explicit boxes: hero plinths, bus
  shelters, railings, the roundabout, the lighthouse.
- **Everything else is ghost geometry**: benches, bins, hydrants, planters, newspaper boxes, kiosks, vendor carts,
  vending machines, market stalls, parked scooters and bikes, rocks, crates, awnings and parasol poles. Players,
  pedestrians and cars all pass through them.

**Fix**:
1. In `IslandV2Kit` (the per-model metadata), add `collider: none|box|capsule|compound` per proto, with a default per
   `kind`:
   - box: bench, bin, hydrant, planter, kiosk, vendor, vending, stall, newspaper box, crate, scooter, bike, rock
   - capsule: parasol pole, sign post
   - none: grass, flowers, ferns, pebbles, awning (overhead)
2. Box size comes from LOD0 bounds × 0.9, aligned to the instance yaw, on layer Prop (13). Rocks over 0.6 m get a box;
   smaller ones get none.
3. Emit them into `cell.Boxes` and `cell.Capsules` exactly as trees are now. Group them under "PropColliders" per cell.
4. Keep the cost in check: about 8–12k boxes island-wide. They're static and in streamed cells, which is fine. Use
   `Physics.autoSyncTransforms = false`.
5. Test (EditMode): every kit proto with a kind in the box list has a collider spec. Count colliders per cell in the
   build receipt.

### 4B. Traffic is on rails and ignores static geometry and decor cars [Code]
- `World/Vehicles/Traffic/TrafficSystem.cs` 372: AI cars are **kinematic** (`Body.MovePosition`), so they pass through
  every static collider. Their lane sensing (`Sense`, 563) only masks Characters, Vehicle and VehicleFlight, **not
  Prop (13)**. It doesn't see `ParkedCar` decor cars, hero plinths or new prop boxes. Where a lane path clips a
  building corner, a junction island or a decor car, the AI car drives through it.
- **Fix**:
  1. Add Prop (13) and Building (12) to the `Sense` mask. Static hits brake the car (gap logic as for vehicles), and
     a static block for more than 4 s triggers a lane change or a despawn when off-screen.
  2. In the World builder, add a test: sweep each lane centreline with a box of car half-width + 0.3 m against
     Building, Prop and ParkedCar colliders, and fail on any hit. Fix the offending lots or bays in the generator
     (`dress.py` must keep decor cars inside bay rectangles, out of lanes).
  3. When the player's dynamic car touches a kinematic traffic car, switch that AI car to dynamic ("knocked", already
     exists via `PreKnock`) **before** contact, not after, so kinematic bodies never shove the player through walls.
     Check `PreKnock` runs on every approach under 6 m, not just head-on.

### 4C. The player and pedestrians walk through people [Code]
- `World/Interaction/CharacterLayers.cs` 15 and 53: `WorldMask` excludes `CharacterMask`, so `BodyMask` (the
  player's movement mask) ignores the Characters layer. **The player walks through NPCs, pedestrians and remote players.**
- `World/Pedestrians/Pedestrian.cs` 122–175: pedestrians are path rails with a lateral offset. They only dodge the
  local player (lines 133–140). **No pedestrian-to-pedestrian avoidance**, and no avoidance of sidewalk props, lamp
  posts or tree pits, which W4 placed on the same sidewalks.
- **Fix**:
  1. Player vs people: add a soft separation in `Locomotion`. An OverlapCapsule each frame on Characters within
     0.6 m pushes the player sideways at up to 2 m/s. Don't make people solid colliders (it would trap players).
  2. Pedestrian vs pedestrian: a spatial hash (2 m cells) in `PedestrianSystem`. Each pedestrian looks 3 m ahead in its
     lane and slows or shifts its `_lateral` when another is within 0.7 m (same rules as the player avoidance).
  3. Pedestrian vs props: bake each sidewalk's "blocked spans" at build time. In `peds.py`, sample the occupancy map
     along each sidewalk polyline every 0.5 m, and record the lateral free interval (the sidewalk width minus props).
     The runtime then clamps `_lateral` to the free interval at each `_s`. Lamp posts and tree pits become slaloms,
     not pass-throughs.
  4. Test: a 60 s editor capture at Timeline downtown with collision logging. Zero pedestrian-to-prop and
     pedestrian-to-pedestrian interpenetration over 0.15 m.

### 4D. A building is one box [Code]
- `IslandV2Dress.cs` 74: buildings get one oriented box at 96% of the lot. L-shaped buildings, setbacks and
  overhangs don't match, so you can clip into recessed corners or hit invisible walls.
- **Fix**: for kit buildings with non-rectangular LOD0 footprints, emit a compound of up to 3 boxes from a simplified
  footprint (convex decomposition of the bottom slice at 1 m height, done once per proto in the kit build). Test: a
  wall-hug sweep around 20 random buildings, with the player capsule never more than 0.3 m from or inside the visual wall.

### 4E. The general guard
- Add an editor tool `Tools/CT Life/Audit/Interpenetration`. For each cell, run `Physics.ComputePenetration` across
  all static collider pairs (Building, Prop, Road), and list pairs that penetrate more than 0.1 m, with positions.
  It writes `ct-life-evidence/audit/interpenetration-<date>.json`. Wire it into the builder receipt, and fail the
  build over a threshold.

## 5. Other confirmed problems

1. **The "pedestrian" name tag is still visible** [Device]. This build (installed 15:24, built from F6's last commit)
   still shows "+ pedestrian" over a passer-by in Timeline Square. The fix in 762059e0 doesn't cover this case. Find
   the label source: the NPC nameplate component attaches to anything with an `NpcIdentity`, so pedestrians need
   `showNameplate = false`, or exclude the "pedestrian" archetype in the nameplate system. Test: no nameplate over
   any pedestrian or NPC driver across 3 venues.
2. **Drone launch puts the camera inside the player's body** [Device]. In the first frame after "Drone", the camera is
   a close-up of the player's torso (the drone spawns at the hands and the chase cam starts behind it, inside the
   avatar). Fix: spawn the drone 1.2 m in front of and 1.5 m above the hands, and start the camera behind the drone
   with the avatar on the ignore layer for the first 0.5 s (fade the avatar's renderers if the camera is within 1 m).
3. **The offline demo still calls the server** [Device]. With `islandDemo=1` the app still sends `GET /ads/live` and
   `/feed` and gets 401 `SIGNATURE_INVALID` (logcat 15:46:14). With the backend down, those wait on timeouts. Fix:
   in demo mode, route REST through the fixture provider (no network) and use the bundled house ads for billboards.
4. **Abandoned vehicles vanish for good** [Code], see §2.6: owned and rented vehicles must not be destroyed.
5. **Vehicles are local only** [Code]. Other players see you frozen at the car door. Interim fix until Iyke's round 2:
   on vehicle enter, send `move` with your seat position each tick if the room accepts it. Otherwise send an emote
   state "driving", and have remote clients hide that avatar and show a generic car placeholder at the last position.
   The real fix is round 2.
6. **The weekly debt due doesn't drop after paying** (server). Interim in the client: if the ledger has a
   `loan_repay` since the last Sunday 20:00 UTC totalling at least the weekly amount, show "Paid this week ✓".
7. **First-launch shader stutter** (12–15 fps for about 60 s). Record a ShaderVariantCollection from a full play
   session and `WarmUp()` it during the title loading bar as a new real boot step, "Preparing graphics".
8. **Thermal** (Seeker at the 30 fps cap; GPU about 27 of 33 ms at render scale 0.6–0.7):
   - downtown has about 460–650 draw calls; merge static instanced batches per cell
   - cull props under 1 m beyond 50 m
   - shadow distance 50 m in the city
   - the ground-detail draw radius from 38 to 28 m on thermal 2 or above
   Target 22 ms GPU.
9. **APK size** (about 264 MiB): move the island kits, cells and venue dioramas to Addressables in an install-time
   asset pack (Play Asset Delivery or the dApp Store equivalent [Unverified]). Keep the title, onboarding and lobby in the base.
10. **The editor (Metal) renders some ground black** (the device is fine). This blocks 4K editor footage for the video.
    Bisect the Toon shader's instanced properties under Metal. Suspect `UNITY_ACCESS_INSTANCED_PROP` reading an
    unset per-instance buffer in some passes.
11. **Product loop: nothing to do** [User: "there's no interest to this game at ALL"]. There's no objective system
    (the minimap has no objective markers). Minimum for the demo:
    - an objective tracker (one active objective, a world beacon, minimap marker plus edge arrow, distance)
    - a 3-minute first-session quest: walk to the screens, take a parked car to Terminal Café, do a shift, collect pay
      (the real `/shifts`), check the Duo wallet, take the Roadstar or a rental up to the MtnDAO summit for a photo emote
    - in-world activity markers: job desks, the trading floor, the Spaces stage, a street time-trial on RoadNetwork checkpoints

## 6. The hand-played acceptance route (run on the Seeker, record it, and fix until clean twice in a row)
1. Launch with `islandDemo=1`, no other flags. Check: no fleet, no drone button (fresh character).
2. Walk Timeline Square to the downtown sidewalk. Brush past pedestrians (no pass-through), weave past benches and
   bins (blocked), check for no nameplates.
3. Take a parked car, drive downtown at speed into a kiosk and a building corner (stopped, no pass-through), get out
   on the sidewalk (safe exit).
4. Drive to the river bank and walk into the river (washed ashore).
5. Go to a helipad. Rent in a dev build with `showcaseFleet=1` until the rental route exists. With Simple controls,
   take off, fly at the mountain at top speed, attempt landing on a steep face (refused) and on the summit cairn
   (accepted), get out, walk.
6. Walk off a cliff edge (roll and land, or the stuck rescue fires).
7. Fly the Roadstar across the island to the far east at top speed, land on a road in a district you haven't visited
   (no fall-through, ground loaded).
8. Use "Unstick me" from Duo Settings.

Log fps, every interpenetration, every lost-player state, and timestamps in `ct-life-evidence/audit/route-<date>.md`
with the screen recording.

## 7. Work order and lanes
- **Feel lane**: §1A (runtime use), §1B.1, 4, 5, §1C, §2, §3, §4B.1 and 3, §4C.1 and 2, §5.1–5.6, §5.11.
- **World lane**: §1A (WaterBodies bake), §1B.2–3, §4A, §4B.2, §4C.3, §4D, §4E, §5.7–5.10.
- **Coordinator**: Iyke round 3 (shop items, rentals, inventory push, weekly due), merge after each acceptance run,
  hold `main` until the §6 route passes twice.
