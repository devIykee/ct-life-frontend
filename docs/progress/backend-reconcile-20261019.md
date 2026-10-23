# Backend reconciliation — 19 October 2026

> Chronological lane receipt. For the latest installed build, scoped device outcomes and final cleanup, see [the current handoff](../handoff/CONTINUE-HERE-20261019.md). Earlier test counts and pending statements below describe their own checkpoints.

Status: source reconciliation, isolated backend validation and client adapters complete; Unity test execution and Seeker validation belong to the coordinator and were still pending when this receipt was written. No backend implementation changes, deployment, commits, pushes, or messages to Iyke by this lane.

## Source and isolation

- Backend `origin/main`: `7d2f069a3f432347dee7b9fa33d5431676dfb3c0` (fetched today).
- Clean detached worktree: `/Users/mac/Documents/codes/projects/ct-life-backend-audit20261019`.
- Existing dirty backend checkout/server preserved; only its remote refs were fetched.
- Compared with the frontend snapshot at `eeef49d` and `docs/handoff/backend-asks-for-iyke-20261018.md`, plus the final audit's round-3 proposals.
- Two new commits: `4c640af` street/vehicle rooms and venue movement; `7d2f069` real geometry, island-frame venue moves and parking support. REST/OpenAPI/shop/debt files did not change.

## Implemented in backend source

| Requested capability | Actual source behaviour |
|---|---|
| Venue join placement | Entrance spot x/z/rotY; fallback rectangle centre. |
| Jitter-tolerant walking | Token bucket (10/s, burst 5), 6.5 m/s distance budget with 0.5 m slack, maximum one second banked; sends `corrected` when clamped. |
| Overlapping room handoff | Venue/street joins do not deduplicate the same user across rooms. |
| Street rooms | `street`, filtered by district, 100 players, 10 Hz; first in-bounds move places only when no shared position younger than 30 seconds exists; otherwise it restores that movement anchor. 30 m district border and island outline. Geometry includes 10 current districts, not the original message's provisional eight names. |
| Island venue coordinates | Optional `island:true` move; server converts to local walkable bounds, exposes `ix/iz/irotY`. |
| Vehicles | `vehicles`, island-wide, 1,000 clients, 10 Hz; `VehicleState` with pose/driver/kind/speed/flags; driver checks, speed/height clamps and corrections. |
| Interest | StateView adds within 250 m and removes beyond 300 m at 1 Hz, always keeps driver's own vehicle. Requires cross-room recorded player position. |
| Enter/exit | Enter requires recent known position, free vehicle and proximity; unknown ID may register a vehicle with supplied kind/pose. Exit publishes a one-time street walking grant. |
| Hits | Driver/proximity/speed/rate validation and victim-only message relay. |
| Server parking | Seeding/return-to-bay code exists, but `contract/ids/parking.json` has **zero bays**. |

Sources: `server/realtime/rooms/{venue,street,vehicles}.ts`, `movement.ts`, `player.ts`, `contract/realtime/{states,messages}.ts`, `contract/ids/{districts,parking}.json` at the exact backend commit above.

## Still absent / narrower than the final audit proposal

- `vehicle_roadstar` and `gadget_drone_cam` catalogue/entitlement rules, `POST /rentals`, and `inventory_changed` are absent from backend source/contracts. Existing `/inventory` serves cosmetics. The proposed prices/supply/utility were not implemented or approved economics.
- Vehicle registration currently accepts **all seven kinds**, including helicopter/drone/Roadstar, with no inventory/rental check. Hiding client controls does not enforce server ownership or scarcity.
- Client-registered vehicles expire after ten idle minutes, and room state is in memory. This is not persistent owned-vehicle inventory/garage storage.
- Weekly debt remains `min(configuredWeeklyAmount, totalOutstandingDebt)` in `server/application/bank.ts:debtSchedule`; it does not subtract repayments within the current week. No `debtRemaining` field exists. The pending round-2 follow-up remains necessary.
- OpenAPI file unchanged, including the previously reported strict-validation/description issues; validating below rather than assuming fixed.
- Street/vehicle unit tests call room handlers directly with mocked authentication and in-memory presence. Their passes alone cannot establish real authenticated WebSocket interoperability or Unity decoding.

## Verification log

- Offline dependency installation failed: `ERR_PNPM_NO_OFFLINE_TARBALL` for the pinned uWebSockets GitHub archive. The exact-lockfile network retry stalled and was stopped. No package versions were changed. The existing audit checkout has identical `package.json` and `pnpm-lock.yaml`; its installed-dependency symlink was reused. Dependencies resolve from the existing backend `node_modules`, while source, environment, databases, Redis keyspace and listeners are separate.
- `pnpm exec vitest run server/realtime/movement.test.ts server/realtime/rooms/street-vehicles.test.ts server/realtime/rooms/venue.test.ts --reporter=basic`: **28 passed** (13 movement, 9 street/vehicle, 6 venue).
- `pnpm contract:check`: passed; `pnpm typecheck`: passed. Contract check is not strict OpenAPI validation; the known OpenAPI issue was not repaired upstream.
- Fresh isolated databases `ctlife_audit20261019` and `ctlife_audit20261019_test`, migrations through `1700000080000`, Redis database **8** (database 7 already had data and was left alone). API **4012**, realtime **2569**. Existing server/data on 4010/2567 remain untouched. Workers, LLM, push and likeness generation are disabled in this local audit runtime.
- Real authenticated HTTP + two SDK WebSocket clients: **10 passed** — dev auth, shared district, walking correction, authoritative driver acknowledgment, nearby StateView decoding, second-driver rejection, vehicle correction, exit walking grant, overlapping venue joins, invalid JWT rejection. `/inventory` returned 200; `/rentals` returned 404. Redacted receipt: sibling evidence folder `ct-life-evidence/audit-20261019/backend-smoke.json`.
- Client development-player compile harness: passed, zero warnings at first coherent bridge snapshot. Frozen-source editor harness: passed with six warnings in existing editor files (`FeelProbe`, `MapBaker`, `VenueVerify`). Frozen-source production-player harness: passed with three warnings in `CityLifeHub`/`FlowController` owned by the coordinator. An earlier editor run raced the final `CanDrive` edit and reported a missing member; the complete frozen snapshot passed when rerun.
- Five new `IslandProtocolTests` compile in the editor harness: filtered vehicle field mapping, separate island/venue coordinates, culture-independent parking IDs, public/premium wire names, correction/rejection payload decoding. **Execution is not claimed here**; coordinator runs Unity tests.
- Additional real recovery probes: arbitrary same-user leave/rejoin followed by a 60 m jump **fails** as designed (observed x=0.5975 and a correction); `backend-recovery-smoke.json` records this. A fresh street join followed by one second paused and a 4 m move passes with no correction (`backend-nearby-recovery-smoke.json`). Destination venue spawn → valid pose → state acknowledgment → street join passes at (-97.6492, 48.9593), without correction (`backend-venue-travel-smoke.json`). The initial assumption that any fresh street join authorized a teleport was explicitly corrected before final integration.
- A focused compile after the acknowledged-venue fast-travel implementation passes with zero development-player warnings.
- `node --check tools/integration/island-observer.mjs`: passed. This second-client observer writes token-free, pseudonymous position/claim/exit evidence for the coordinator's Seeker run. The backend probe used the JavaScript SDK; it does not establish Unity runtime decoding or device gameplay.
- During the coordinator's installed Seeker run, an independent SDK observer received the phone's Timeline walking presence, an authoritative public SUV driver claim, vehicle motion and release, followed by on-foot presence. Preserved windows: `island-observer.json` (30 minutes) and `island-observer-followup.json` (10 minutes), with no observer errors/rejections. Followup one-second checkpoints recorded a maximum observed SUV speed of 3.054 m/s and at least 8.686 m displacement from the first window's final pose; driver became null at the 03:34:55.955 UTC checkpoint. Removing the adb reverse mapping did not produce an observed lease loss/reclaim, so reconnect recovery remains unverified. A separate Breakpoint observer then received the phone at Conference Hall (-265.331, 307.073), about 436 m from the prior on-foot position, followed by further accepted position changes. That observation establishes cross-district server presence, not visual smoothness, terrain quality or the unstick UI outcome. Evidence: `island-observer-breakpoint.json` and its checkpoints.
- The coordinator reported tapping Unstick at 03:44:20 UTC. Breakpoint samples briefly paused after 03:44:20.768 and resumed by the 03:44:22.985 checkpoint; the position remained (-263.959930, 310.578186), with zero observed horizontal change or snapback through the final 03:47:31 window. This corroborates the coordinator's rejoin log at the same destination; it does not prove recovery from an actually trapped location or an explicit map remove/add event. All three completed observation windows and their limits are summarized in `ct-life-evidence/audit-20261019/device-observer-summary.json`. The isolated backend was left running at observer completion, then stopped after device QA as recorded below.
- Scoped `git diff --check` for this lane's code/contracts/docs: clean. Full-worktree check reports trailing spaces in regenerated Unity prefab YAML owned by the world/build lanes; this lane did not rewrite those assets.

## Client integration delivered

Backend-owned contract files are copied from `7d2f069` exactly, with provenance updated in `contract/SOURCE.md`; only realtime C# was regenerated. REST generation is unchanged. Added `IIslandRealtimeService` as a capability alongside the legacy interface, preserving existing test doubles. Street and vehicle slots are leased to the island visit cancellation token. Both reject transport in `RuntimeMode.IsOfflineIsland`.

The island street bridge joins the published district, supplies 10 Hz walking/standing presence, renders peers on authored ground with height fallback, avoids duplicate venue avatars and hides driving avatars. It handles street corrections outside venue walkable bounds. Venue `corrected` messages now snap the active walking player. Fast travel retires old street/venue writers while walking is paused, joins the destination venue, publishes that venue's server-owned spawn pose with a small heading change, waits for the authoritative heading echo, then snaps to the accepted pose and reopens street presence. This uses the supported venue placement path, not an arbitrary client teleport. District crossing uses the existing break-before-join slot; seamless overlap has not been claimed.

Public vehicle entry now awaits `state.driver == currentUser` before Feel starts the entry animation, with an 8-second response timeout under the 12-second entry timeout. Cancellation/late claims and post-ack aborted entry release the claim. Online parking identities and choice of fallback public model derive from immutable bay coordinates, replacing time-dependent choices. Scooter maps to backend `moped`.

Driver poses send at 10 Hz. Received remote cars are kinematic, use physics-step interpolation and speculative collision; replacement channels discard old remote actors absent from the new room, and a restored live driver lease resumes physics only outside entry/recovery transitions; only another user's driver marks `RemoteOccupied`. Corrections apply to the local vehicle. Successful safe exit sends final pose/release. Dropped transport or lost ownership freezes the car and invokes Feel's validated lost-lease player recovery. Online recovery is limited to nearby safe floor within 4 m; it rejoins street while paused and banks one second of ordinary walking allowance. Distant saved-point recovery remains unavailable online. The existing release grant also covers server onLeave, but exact device drop timing is not established by the source/probes. Online submerged-vehicle recovery releases the owned claim only after scheduling a nearby validated player recovery; it does not teleport the server vehicle back to a local saved pose. Vehicle-hit receipt refuses to knock down an avatar still driving. The incoming handler is implemented; **outgoing remote-player collision hit reports are not wired** in this pass (the existing physics hook skips remote players).

Normal online helicopter, Roadstar and drone entry stays unavailable because the server has no ownership/rental policy to enforce. Client restrictions are not server entitlement enforcement.

## Remaining protocol/product limits

- No backend street room covers `countryside`; the bridge leaves street presence there instead of inventing a district. Vehicle interest/public entry require recent presence, so entering a vehicle far outside a covered district or venue may be refused.
- ORE Refinery is labeled `mountain`, but its published door (133.32, 309.44) is 70.56 m south of that district polygon's minimum z=380, outside the 30 m server border. A live observer joining mountain at the door received `OUT_OF_BOUNDS` on its own moves (`island-observer-mountain.json`); these are observer-probe rejections, not phone decode failures. The client classifies this point as countryside, so venue travel there does not establish street coverage or a supported nearby street reset. The online device travel check was redirected to Conference Hall in the covered Breakpoint district.
- Chat/emote/voice UI still follows the existing venue integration; street channel methods exist, but this pass does not claim street chat/voice UX.
- Parking registry has no server-authored bays. This pass registers stable public IDs when first used. The backend caps concurrent client registrations at two per user and 400 total. At the per-user cap it despawns the oldest free car; it refuses only when no per-user slot can be freed or the global cap is reached. There is no persistent public/owned garage.
- The state map removal cannot distinguish interest loss from ten-minute expiry. The visit's known-bay cache keeps a moved car's original decor hidden after interest loss to avoid duplicating it; an expired car's original bay can remain hidden until the next island visit. A deletion/expiry reason or authoritative parking registry is required to settle that distinction.
- No arbitrary far online rescue is enabled. If no safe floor is available within the bounded nearby rescue radius, the operation cannot use an old distant saved point; explicit destination-venue fast travel has its own acknowledged path.
- This lane's device evidence is limited to the independent network observations above. Visual entry/exit, steering/braking feel, terrain/collision quality, recovery UI and performance remain coordinator verification work. A real transport loss/reconnect was not established by removing the adb reverse mapping.
- Round-3 inventory/rental/debt remainder requests remain open. Do not tell the next developer that all backend asks are complete based on the two realtime commit titles.

## Frontend parking proposal

`tools/export_parking.py` exports actual active `ParkedCar` markers from current serialized cell prefabs, checks ancestor identity transforms, reproduces float32/stable-ID/model-selection semantics, and writes backend-shaped `parking.json` plus per-marker/provenance hashes separately from `contract/`. Empty `VehicleSpawn` showcase bays are intentionally excluded. Final post-bake export: **283 public bays** (99 compact, 51 sports, 133 SUV); no premium markers were present. Validation through the actual backend movement helpers found 283 unique valid shapes, all on island land, with **36 outside published street coverage**. This is a data proposal; neither upstream nor the live isolated server has been seeded with it. Proposal is in `docs/fe-contract-proposals/parking-20261019/`; all recorded input hashes were rechecked against the coordinator's final baked assets and match. The final development-player compile also passes with zero warnings.

## File ownership and runtime handoff

Client lane files: `client/Assets/Generated/Realtime/{PlayerState,StreetState,VehicleState,VehiclesState}.cs`, `Generated/Support/RealtimeMessages.cs`, `Scripts/Net/Realtime/{RealtimeService,StateMapper,Channels,IslandChannels,IslandRealtimeService}.cs`, `Scripts/World/Island/StreetNetworkBridge.cs`, `Scripts/World/Vehicles/{VehicleNetworkBridge,ParkedCars}.cs`, `Scripts/World/Venue/VenueSession.cs`, `Tests/EditMode/IslandProtocolTests.cs`, corresponding new metas, contract snapshot/provenance, `tools/integration/island-observer.mjs`, and `tools/export_parking.py`/parking proposal files. Feel owns `VehicleSystem`, `Vehicle`, `LocalPlayerController` hooks and recovery; coordinator owns RuntimeMode/REST/UI.

The isolated backend remained running until the coordinator finished device validation. Its launcher and raw logs are in the session scratchpad; no runtime environment or token was copied to the repository or evidence. The original dirty backend source was never checked out, reset, patched or committed by this lane.

## Final runtime cleanup

After the coordinator confirmed device QA and restoration of the original app preferences/private backups, only the isolated backend PID **41559** received `SIGTERM`. Its registered shutdown handler stops the API and realtime servers. The backend, its esbuild child **41560**, and the isolated tsx/pnpm launcher processes **41553/41540** all exited. Ports **4012/2569** have no listeners and both local TCP connection attempts return connection refused. The original API/realtime services remain listening on **4010/2567** under their unchanged PIDs **72652/72656**. All observer processes had already ended. No runtime source was edited for cleanup.
