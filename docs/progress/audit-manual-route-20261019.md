# Manual device route card — 19 October 2026

Read-only navigation guide. Coordinates are XYZ metres; headings are clockwise from north (+Z): north0°, east90°, south180°, west270°. Spawn positions below are the latest **native fixture logs**, repeated in the passing `editmode-followup.xml`; device spawn logs take precedence when available. Do not teleport or automate the route.

## Current observed outcomes

Controlled touch testing subsequently resumed with synchronized recordings on the integrated APK. The earlier activity documented in [input observation](audit-input-observation-20261019.md) remains unattributed. Preserve these separate outcomes; the whole route is not complete.

- **Helicopter local trial passed within the recorded scope:** boarding, low takeoff, descent, accepted Get out state, normal exit and brief returned on-foot movement. Rotor occlusion limits inspection of character contact.
- **Near-summit shoulder trial passed within the recorded scope:** stationary 0 m / 0 km/h / Get out beside the snowy summit, followed by normal exit and upright on-foot controls. This was the narrow shoulder, not a broad summit pad; exact final craft XYZ, every skid contact, a sustained shoulder walk and the return journey were not independently measured by that clip.
- **Cliff observation verified:** airborne fall, sideways/downed reaction, get-up and later ordinary movement on the lower face. The selected physical-ragdoll/animation branch and automatic safe-ground rescue remain unverified. Do not mark automatic cliff rescue passed.
- **Drone:** low descent to road level, stop/rotor spin-down and return to the operator were visible. The Grounded-versus-seven-second-timeout branch remains untraced.
- **Roadstar now has a scoped controlled arrival:** the second recording shows flight into Builder Hills, descent to grass beside buildings, a settled 0 km/h ground state, brake/reverse movement onto pavement, normal exit and brief on-foot movement. A post-walk map estimate is approximately X376/Z237.7 in `cell_5_4`, about46 m from the intended X420/Z252 point; it is not the landing position. There is a recording gap between the two legs, and green/yellow sky flashes remain visible. See [Roadstar device receipt](audit-roadstar-device-20261019.md). Pro-mode preservation, the full route twice and automatic cliff safe-ground rescue remain unresolved.

Recorded times, files and limitations are in [flight follow-up](audit-flight-followup-20261019.md). The instructions below remain a navigation card, not claims that every trial passed.

## Compact hand-play acceptance card

Current installed artifact: `ctlife-controls-lifecycle-dev.apk`, SHA-256 `4ef24fa133acfe9626c2f3cac01596fd624069091f3ed65f983cbb2f3854d07d`; integrated native suite **2,067/2,067 passed** (`controls-lifecycle-build.json` records installation verification at16:54:21UTC). Earlier helicopter/drone/cliff receipts used the prior `0ffb83…` controls artifact; the later Roadstar trial uses this integrated artifact. Native tests do not establish physical flight acceptance. Use the existing explicit **offline showcase** launch at the lodge (`hackathon_house`) so one fleet covers the three craft. The ordinary online entitlement/parking route does not expose showcase helicopter/Roadstar. Root owns launches, touches, device recording and log collection.

| Craft/mode | Ordinary controls | Camera relationship |
| --- | --- | --- |
| Helicopter/drone, **Simple** | Left floating stick moves horizontally. Hold **Up/Down** for height; release horizontal input to decelerate/hover. | Movement is relative to the view. Right-side drag orbits; automatic yaw recenter is suppressed. Looking elsewhere changes the next forward-stick direction. |
| Helicopter/drone, **Pro** | Left Y = lift, left X = yaw; right Y = pitch, right X = roll. | Right stick is cyclic control, not the Simple orbit gesture. Chase view resumes after idle. |
| Roadstar | **Gas/Brake**, left X = steering; **Fly/Land** folds/unfolds wheels. While flying, left Y = lift; handbrake appears only on ground. | Motion remains relative to the car's nose. Orbit alone does not steer. Chase camera recenters after idle. |

Suggested order (each row is a separate observed outcome, not a preclaimed pass):

| Trial | Hand-play action | Evidence to retain |
| --- | --- | --- |
| Drone basics | From open lodge ground, use the ordinary drone HUD action. In Simple, climb to roughly 5–8 m, move forward/sideways, release, orbit the view, then move forward again. Return over visible dry ground and tap **Land**. | Rotor/Up feedback; movement follows the new view after orbit; release visibly slows the craft; see the physical descent before camera return. |
| Helicopter basics | Walk to the pad and use its ordinary entry offer. Hold Up through spin-up, then test a short low hover, lateral movement, release and a view orbit. Land back on the pad and wait for **Get out**, then exit. | Spawn is outside the lodge, rotor responds, ground clearance is visible, camera does not keep turning during held movement, touchdown/exit puts the actor beside the craft on supported ground. |
| Helicopter terrain | Re-enter and attempt the near-summit route below. Approach slowly, inspect actual surface and landing feedback; a rejected narrow shoulder is not a successful mountain landing. Return to the pad. | Continuous flight footage, readable altitude/landing state, actual touchdown and safe exit if achieved. Record the attempted landmark if it fails. |
| Roadstar ground → air → new area | From the lodge exterior, drive a short clear road segment, brake, turn, then tap Fly. Climb with left Y and use Gas/steering toward Builder Hills below. Release, brake, Land on an observed clear dry road section, then exit. | Wheel/pad transition, usable steering/height control, terrain/collision appearing before arrival in a newly visited district, landing/exit, and the map showing the new district. |
| Pro preservation | After the baseline Simple trials, switch the existing Settings flight row to Pro and perform one short takeoff/turn/landing in clear space; restore Simple. | Both stick mappings remain usable; this is separate from Simple acceptance. |

Start each gesture inside its current visible control region and release before the next. Floating sticks anchor at touch-down, so a tap with no displacement is neutral. Use current QA bounds/screenshots, not a stale guessed screen coordinate. A video plus fresh UI snapshot at each endpoint is more useful than an isolated screenshot claiming a route.

## Readouts and limits

- `qa-ui.json` updates about every 0.5 s and exposes visible labels, `screenBounds`, `tapPoint`, sequence and timestamp. Useful named controls include `veh-gas`, `veh-brake`, `veh-fly`, `veh-exit`; stick zones have classes but no explicit names. The export contains **no aircraft XYZ or camera yaw**. A fresh snapshot proves UI state, not that an intended gesture registered.
- Safe existing spawn logs are `CTLIFE [vehicles] showcase <id>: parked at (x,y,z)`. They locate the initial fleet. No existing continuous offline vehicle-position/camera-heading log was found. Editor-only movement review traces are not available on Seeker; do not invoke game callbacks or use the scripted demo/tour to fill this evidence gap.
- HUD altitude is height over the queried surface, **not world elevation**. Speed includes vertical velocity. The initial helicopter rotor needs about 1.6 s, drone about 0.4 s. Full Up targets at most 6 m/s climb. The summit is about 100 m above the lodge, so a brief Up tap cannot establish summit clearance.
- Minimap/full map use the **local player's** position/heading, not camera yaw. A helicopter/Roadstar driver follows the craft; the drone operator stays at launch, so the map does **not** track drone travel. Use landmarks and video for drone orientation; its leash is 150 m from the operator.
- Helicopter **Land → Get out** requires loaded dry floor, slope normal ≥0.85 (about32°), low height, at least0.5 s grounded and speed below2 m/s. The exit also needs a separate safe human-sized floor/capsule location. `Loading area…`, `No water landing` or `Too steep — move to flat ground` are blocked states, not successful landings.
- Drone Land currently descends for **at most7 s**, then pockets the craft and returns the camera even if it did not become grounded (`VehicleSystem.LandDrone`). Camera return alone therefore does **not** establish touchdown; the first landing trial deliberately starts low. High-altitude/water landing remains a separate behavior to report honestly.

Source basis: `VehicleControls.Compose/Update`, `FlightPhysics.FixedUpdate`, `VehicleSystem.LandDrone/ExitOrLand`, `CameraRig.FollowVehicle`, `Minimap.IslandTick`, `FullMapView.TryPlayer`, and `QaUiSnapshotExporter/Data`. This card adds no debug interface or source changes.

## Locate the lodge fleet

| Landmark/craft | XYZ | Direction from lodge door |
| --- | --- | --- |
| Lodge door | (−249.59,69.21,412.55) | — |
| Helicopter | (−225.15,69.56,427.27) | 28.5m northeast, bearing59° |
| Roadstar | (−236.40,69.24,422.81) | 16.7m northeast, bearing52° |
| Summit Road end | (−240,69.21,412.9) | Just outside the east-facing porch |

## Helicopter: pad → near-summit shoulder → return

1. In **Simple** mode, Up/Down control height; the left stick moves relative to camera yaw. Dragging the view changes that heading. Camera yaw stays where the player looks during Simple flight; it does not chase the turning nose. The minimap uses the player's transform heading, not camera yaw, so an orbit alone is not a compass turn.
2. From the lodge pad, the shoulder is **99m northeast, bearing45.6°**: `(−154.5,168.99,496.5)`. Face the view roughly northeast before moving forward. Its ground is about100m above the pad; establish height before crossing the rising face, then approach gently. This point is about29m southeast of the summit cairn/flags, below the highest peak.
3. For the last descent, **nose north (0°)** is the footprint orientation inspected in source. Release horizontal input and inspect the existing Land/Exit feedback. The center terrain triangle is19.9°; a possible west-side exit is `(−156.5,168.50,496.5)`. A later recorded landing/normal exit succeeded on this near-summit shoulder, with the scope above; the authored coordinate guided the approach but is not an independent measurement of the final craft position. There is no broad flat summit platform.
4. Nearby on-foot descent: head east (90°) along Z496. Terrain drops from Y169.14/X−154 to165.12/X−150,160.14/X−146 and150.47/X−138. Observe actual fall/recovery behavior; these sampled elevations do not establish a clear walking path.
5. Return to the lodge pad on **bearing225.6°**, about99m southwest. The Roadstar fixture is12.1m southwest of the heli fixture, around the lodge's exterior parking cluster.

The large boulder beside the cairn has a flat collision-box roof at `(−180.85,181.45,518.41)`, but its rendered surface is irregular. It is useful for examining proxy mismatch, not a substitute for a visually convincing terrain landing.

## Roadstar: lodge → initially unvisited Builder Hills

**Short flight leg:** destination `(420,19.08,252)`, **678m east-southeast, bearing104.6°** from the lodge Roadstar. Roadstar remains vehicle-relative: steer turns the nose, forward pedal supplies throttle, left-stick vertical supplies lift while flying. Camera orbit alone does not steer it. Use the map to track the route and inspect arrival/landing feedback. Destination is Hills Drive's bend, not a prevalidated parking bay.

**Driving route (~1.2km):** start10.5m south-southwest at Summit Road end. Follow the road's switchbacks, without cutting across slopes:

| Next waypoint | XYZ | Road/direction |
| --- | --- | --- |
| Upper hairpin | (−90,56.10,391.6) | Summit Road, initially east |
| Lower hairpin | (−315,36.10,365.2) | Summit Road, then west |
| Stop/T junction | (−120,19.31,318) | Summit Road, then east |
| Foothill bend | (−20,20.29,340) | Turn east onto Foothill Road |
| River bridge | (30,17.98,332) → (66,17.98,322) | East across the bridge |
| Southeast bend | (96,16.40,306) | Continue Foothill Road |
| Hills roundabout | (~184,8.75,238) | Take east exit |
| Destination | (420,19.08,252) | Hills Drive |

Summit Road width8.6m/max sampled grade8.1%; Foothill11.4m/9%; Hills Drive7.2m/8%. These are authored measurements, not traffic/obstacle clearance results. The destination's `cell_5_4` (X256–512/Z0–256) is outside the lodge's initial420m load radius, so the approach exercises newly visited cells. Observe visible terrain/collision arrival and dynamic performance.

Evidence: current `heightfield.bytes`, `plan.json`, `roads.json`, shipped heli/cell colliders, native fixture output; detailed limits in [the follow-up receipt](audit-showcase-followup-20261019.md). No world changes were made for this route.
