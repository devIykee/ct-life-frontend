# Feel audit repair — 19 October 2026

> Chronological lane receipt. For the latest installed build, scoped device outcomes and final cleanup, see [the current handoff](../handoff/CONTINUE-HERE-20261019.md). Earlier test counts and pending statements below describe their own checkpoints.

Source base: integrated dev25aad110, working branch feat/audit-repair-20261019. No Unity, adb, commits, pushes, backend writes or external messages from this lane. Coordinator owns builds/device verification.

## Implemented; integrated tests passed, device verification partial

- `VehicleSystem.FindSafeExit`: driver/passenger/rear/front then bounded rings; loaded, dry ray-hit floor <=35degrees, <=1.2m height difference, free standing capsule, no person overlap or wall between vehicle and candidate. Island has no navigation mesh, so exits do not require one. Brake first, revalidate after door animation, retain driving state on refusal. Aircraft must remain grounded on a suitably flat surface0.5s at speed<2m/s. Passenger-side exits can open the matching door.
- `PlayerSafety`: observe last dry walkable floor every0.5s; recover sustained submersion1.2s, fallthrough immediately, stationary airborne4s, stationary steep surface3s. Load and revalidate destination before teleport; bounded nearby-ground search if no saved point. Existing collider floor wins over heightfield so a tunnel is not mistaken for falling under terrain. Public `RecoverToSafety(bool notify=true)` is the Settings hook. Recovery now publishes PlayerRecoveryFadeSignal: dim0.2s before teleport, clear0.2s after; OnDisable/OnDestroy/cancel clears immediately. Root owns the actual HUD overlay. Recovering blocks movement and vehicle entry briefly; cleanup resumes the correct body state.
- `FlightPhysics`: water comes only from finite World water lookup; missing ray uses heightfield, never water sentinel600. No settling until streamed ground is ready; bounded EnsureLoaded requests; clamp/hover over missing collision and water. Simple helicopter/drone default: camera-relative desired horizontal velocity, bounded acceleration, directional yaw, Up/Down hold, release hover, queued startup climb; rotor1.6s; contextual Land/Getout and loading/water/slope feedback. Pro controls retained via `FlightPhysics.ProControls` (`ctlife.flight.pro`). Roadstar retains its drive/fly control mapping.
- Drone launch2.2m in front/2.6m above feet, space/path checks; camera initializes at vehicle pivot instead of easing through player's body, hides close owner. Heli chase14m/5m/12degrees and1s recentre.
- Access defaults denied for drone. `SetDroneEntitlement(bool)`, `CanDeployDrone`, `AccessChanged`; root integrates inventory/HUD. Showcase fleet requires explicit dev `showcaseFleet=1`; restricted craft usable only with offline island fixture too (online cannot bypass authority). Spawn/offer/entry all gate restricted kinds. Public compact/SUV/sports/scooter retained. Roadstar decor stays static unless permitted. No prices, rental promises, scarce supply or server ownership invented.
- `AuthorizeEnter(Vehicle,CancellationToken)->Task<bool>` waits before physical entry. Timeout/abort/destroy cancel; `EntryAborted` releases an acknowledged but aborted claim in network bridge. `RemoteOccupied` distinguishes remote claim from local driver. Backend lane owns actual bridge and final parked-car stable IDs/state integration.
- Traffic static Building/Prop/vegetation/coast sensing plus physics-step swept box; braking rather than rail passage; preknock before rail motion; blocked offscreen cars retire after4s. Dry spawn checks.
- Player soft separation from people, capped2m/s; pedestrian keep-right/yield to peers, stop/reverse at static obstacles instead of clipping. Dry/free pedestrian spawns. Existing ambient flag already excludes pedestrian display names; coordinator must verify actual new build.

## Verification

Added 22 focused EditMode tests in `AuditSafetyTests.cs`: dry flat/slope/void/height/headroom exits, missing-ground/water/bridge classification, bounded simple-flight braking/direction, submersion/reset/fall/stuck timer behavior, static traffic sweep, access policy, and driver recovery near the current car. The coordinator reports the latest integrated run passed all 1971 EditMode tests, and the resulting APK was installed on the Seeker. This supersedes the earlier 1945-test pass. This lane did not run Unity or adb. The device checks below are the coordinator's actual partial touch QA; the full acceptance route remains incomplete.

### Actual Seeker checks reported by the coordinator

- Normal fresh entry: no drone or premium fleet available.
- On foot: elevated-lake water recovery reached a dry bank and displayed “Washed ashore.” Duo Settings → Unstick closed the phone and recovered the player.
- Public car: entered, drove with gas and steering, reversed, collided with a parked car, and exited safely.
- Drone in explicit offline showcase: launch camera was clear of the owner; Up reached 47 m and hover was observed. Recall is **not verified**: the first Land tap did not register.
- Helicopter in explicit offline showcase: initial entry at `hackathon_house` placed it on the authored nearby pad. Entering, Up takeoff to 30 m, and climbing/flying forward toward the summit region were exercised. Land on steep terrain refused with feedback. Flat touchdown and safe exit at the summit remain unverified.
- Android thermal status was 3 and the game's 30 fps cap remained unchanged. These observations do not establish a 60 fps result.

Observed issues remain: showcase cars appeared inside the lodge/on its porch; no helicopter was observed around Timeline Square. [Inference] Timeline's strict clear-ground search may have silently skipped the helicopter, but spawn failure was not confirmed. Plaza foot clipping is visible in the baseline as well as the new build. The observed run-stop facing turn also remains; its runtime cause has not been confirmed.

Still unverified: the full audit route twice; summit safe exit and cliff cases; Roadstar fast streaming; drone recall; flat aircraft landing and exit; high-speed arrival into unvisited terrain; river recovery; static-obstacle traffic behavior; and online claim rejection, timeout, loss and exit with another client. Partial passes above do not establish acceptance of these remaining paths.

## File ownership

Feel: World/Vehicles except backend-added VehicleNetworkBridge.cs; World/Players; World/Pedestrians; UI/Hud/VehicleControls.cs; vehicle-only CameraRig changes; 22 tests. One authorized LaunchArgs whitelist key. ParkedCars.cs transferred to networking lane after adding access/dry gating; preserve their subsequent stable identity changes. Root owns strings, HUD, Settings and Unity/device tests; World owns Island water/collision/streaming.


## Independent flight/control review: acceptance checklist

The pure tests establish surface classification and controller direction/bounds, not actual Rigidbody behavior or touchscreen usability. The partial device observations above cover only the stated actions; this full checklist remains the acceptance scope:

1. Fresh offline island without showcase: no drone or restrictedcraft, publiccar/scooter remains usable. Explicit offline showcase restores filmingcraft; onlineflag must not bypassserver.
2. Helicopter: holdUp immediately; rotor cue rises, liftoff within5s. QuickUp tap duringstartup should queue one short climb, Down cancels it. Release to hover; confirm no continuing ascent/descent or tilt runaway.
3. Move forward with camera yaw0/90/180, strafeleft/right while orbiting right-halfdrag; holdUp+move simultaneously and then background/release both touches. No stale input. TogglePro andback without residual axes.
4. TapLand overlake andsteepface; it remainsclear/blocked and explainswhy. Movingstick cancelsLand so a safer spotcan bechosen. Loadedflatland -> settle0.5sunder2m/s -> Getout; doorcrossing has real dryheadroom.
5. Fly maximumspeed towardunvisited mountain; no sinkthroughvisibleterrain, loadingcue ifneeded, colliderfallbackmustusefullheightfield surface. Repeat3times, including summit and tunnel/bridgeareas (no false underterrainrescue).
6. Drone firstframe: lensclearofownerandprops, collision-awarelaunch refusal underroof. Range150m remainsunchanged. Recallwhilehigh/overwater restoresonfootcameraandownervisibility; confirm dronecontrollerinputdoesnotmovetheowner.
7. Riveronfoot: sustainwaterdepth>0.5m for1.2s, fade0.2/teleport/fade0.2; continuewalkingafter. Cancel/closeworldduringloadingandduringfade: no stuckdarkoverlay or suspendedCharacterController. Unstickwhilepreviouslyseated clearsseatstate.
8. Kiosk/building/parkedcarcontact: dynamicplayercarstops; AItrafficbrakesorwaits beforestaticgeometry, side/rearimpacts switchfromkinematic beforecontact. Pedestrianpairandplayerbrush tests mustshowseparationwithoutblockingallpassage.

Review-driven corrections afterfirsttestpass: simple stick cancelsautoLand; coarsealways-loadedcollider cannotmask below-visibleterrain; only real road/tunnelfloor overridesheightfield; recovery now fades and suspends/resumesinput.

Added3 access-policy regressions after the first suite run: fresh player public-only, drone ownership does not unlock other premium craft, explicit offline showcase permits testfleet. These are included in the latest coordinator-reported 1971-test pass.

## Independent network review and lease recovery

Reviewed the new vehicle/street bridge implementation with the networking lane. Acknowledgement is the authoritative driver field. Cancelled entry releases a late acknowledgement; remote vehicles disable local car/flight physics and move through kinematic Rigidbody updates; local driving requires a live lease. Driver knockdown relays are ignored while claiming/driving. Release sends a final pose only while still the owner. These are source findings, not two-client device proof.

`VehicleSystem.RecoverLostNetworkLease()` keeps the server-owned car in place and detaches the local person only after a safe destination is loaded and checked. It first searches within 4 m of the current vehicle. Only offline recovery may use the saved distant point; online recovery remains within 4 m throughout revalidation. The backend lane verified that the server exit grant is limited to 5 m for 15 seconds. A later probe corrected the earlier claim that rejoining permits a far first move: the street restores recent shared presence for 30 seconds. `StreetNetworkBridge.ResetForNearbyRecovery(destination)` now leaves, joins the destination district, verifies the join, and waits one second while the player remains paused. The backend lane live-probed a 4 m move accepted after that wait under the ordinary 6.5 m/s movement budget. This is synchronization and ordinary movement allowance, not distant teleport authorization. Street updates/corrections and venue corrections are suppressed during recovery. Server vehicle release publishes the exit grant on exit and disconnect, but drop/release timing still requires device verification. No authoritative distant rescue exists in the current contract. The terrain lookup is secondary to a nearby authored floor so tunnel recovery can stay in the tunnel.

Three additional regressions cover the nearby-floor preference after a long drive, refusal when neither nearby nor saved ground exists, and refusal of a distant saved fallback under online policy. Manual checks must include lost lease after a long trip, packet drop, late entry acknowledgement, online refusal when nearby ground is unavailable, and recovery while a second user takes the car. The wire cannot distinguish interest removal from parked-car expiry; the bridge conservatively retains known bay IDs. This limitation remains documented in the networking lane.

## Root integration review

Reviewed HUD recovery veil/access subscription cleanup, Settings callbacks and offline guards. No additional UI null/lifecycle or shared-material issue found. Identified raw likeness and ad upload transports bypassing RestClient; coordinator added guards. Added regressions for likeness read/upload and ad upload before auth refresh/transport, REST early rejection with stale in-memory identity, and cancellation precedence across all four entry points. Tests use throwing auth traps so a missing guard cannot open a network request; session setup uses reflection without touching stored preferences. Added the Ventures reference to the existing EditMode test assembly. These tests are included in the latest coordinator-reported 1971-test pass.

## Final authority review corrections

Found and coordinated a reconnect freeze: a dropped lease disabled local car physics, but a restored owned lease did not re-enable it after a failed rescue. Backend added a restore policy requiring a live owned lease, kinematic body, and neither entry/recovery transition active. Three regressions cover reconnect restoration, transition protection, and changed ownership. Also confirmed stale remote actors survived a replacement vehicle channel because clearing the old schema mirror did not emit removal events; backend owns replacement cleanup.

Online vehicle water/fallthrough recovery no longer moves the car to a remembered local pose. It schedules the existing nearby person recovery first, then releases the owned claim through `ReleaseForSafety`; a second release after detach is harmless. Offline car recovery is unchanged. If no dry floor is available within 4 m online, no distant rescue is invented; this remains a contract limitation requiring further backend support. VehicleSystem comments now reflect the actual online bridge. These source changes and 22 safety tests are included in the latest coordinator-reported 1971-test pass and installed APK; the online recovery/device acceptance paths remain unverified.
